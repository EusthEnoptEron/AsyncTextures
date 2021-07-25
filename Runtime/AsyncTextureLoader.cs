using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StbImageSharp;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using Zomg.AsyncTextures.Types;
using Zomg.AsyncTextures.Utils;
using Debug = UnityEngine.Debug;

namespace Zomg.AsyncTextures
{
    /// <summary>
    /// Asynchronous loader for runtime textures using compute shaders.
    /// While you can create multiple instances of the class, it is essentially a singleton. Multiple instances will not share their time slices.
    /// </summary>
    /// <remarks>Note that this class will subscribe to the <see cref="Application.quitting"/> event for clean-up, so you do not necessarily need to dispose yourself.</remarks>
    public class AsyncTextureLoader : IDisposable
    {
        private static AsyncTextureLoader _Instance = null;

        /// <summary>
        /// Gets an instance of the loader.
        /// </summary>
        public static AsyncTextureLoader Instance
        {
            get
            {
                if (_Instance == null)
                {
                    _Instance = new AsyncTextureLoader();
                }

                return _Instance;
            }
        }

        public AsyncTextureLoader()
        {
            if (_Instance == null)
            {
                _Instance = this;
            }

            Application.quitting += Dispose;
        }

        /// <summary>
        /// Gets or sets the decoder used for decoding images.
        /// </summary>
        public IAsyncImageDecoder ImageDecoder { get; set; } = new AsyncImageDecoder();

        private AsyncMonitor _asyncMonitor = new AsyncMonitor();
        private ComputeShader _computeShader;
        private ComputeBuffer _computeBuffer;

        /// <summary>
        /// Gets or sets the time budget in milliseconds.
        /// </summary>
        public float UploadTimeSlice { get; set; } = 3.0f;

        /// <summary>
        /// Gets or sets the buffer size when writing to the GPU.
        /// </summary>
        public int BufferSize { get; set; } = (int)Math.Pow(2, 15) * 4;


        /// <summary>
        /// Gets or set the initial compute buffer size (in pixel count)
        /// </summary>
        public int InitialComputeBufferSize { get; set; } = 4096 * 4096;

        private ConcurrentQueue<Task> _queue = new ConcurrentQueue<Task>();
        private int _widthProp = Shader.PropertyToID("Width");
        private int _heightProp = Shader.PropertyToID("Height");
        private int _offsetXProp = Shader.PropertyToID("OffsetX");
        private int _offsetYProp = Shader.PropertyToID("OffsetY");
        private int _resultProp = Shader.PropertyToID("Result");
        private int _inputProp = Shader.PropertyToID("Input");

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private bool _disposed = false;

        private void Init()
        {
            Debug.Log("Loading compute shader...");
            _computeShader = Resources.Load<ComputeShader>("Shaders/TextureUpload");
        }


        /// <summary>
        /// Prewarms the compute shader with the default initial compute buffer size.
        /// </summary>
        public void Prewarm()
        {
            Prewarm(InitialComputeBufferSize, 1);
        }

        /// <summary>
        /// Prewarms the compute shader for textures of the given resolution.
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public void Prewarm(int width, int height)
        {
            if (_computeShader == null)
            {
                InitialComputeBufferSize = width * height;
                Init();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Debug.Log("Disposing async texture loader");
                _cancellationTokenSource.Cancel();
                _asyncMonitor?.Dispose();
                _computeBuffer?.Dispose();

                _disposed = true;

                Application.quitting -= Dispose;
            }
        }


        private async Task<ComputeBuffer> AcquireBuffer(int width, int height, CancellationToken token)
        {
            token = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellationTokenSource.Token).Token;

            Debug.Log("Waiting for my turn...");

            await _asyncMonitor.WaitAsync(token);

            Debug.Log("It's my turn!");
            if (!_computeShader)
            {
                Init();
            }

            int requiredLength = width * height;
            if (_computeBuffer == null || _computeBuffer.count < requiredLength)
            {
                int size = Math.Max(ToPowerOfTwo(requiredLength), InitialComputeBufferSize);

                Debug.Log($"Creating compute buffer of {size * 4 / 1000 / 1000}MiB");

                // Dipose old
                _computeBuffer?.Dispose();

                // Create new
                _computeBuffer = new ComputeBuffer(size, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
            }

            return _computeBuffer;
        }

        private void ReturnBuffer()
        {
            // await Task.Yield();
            _asyncMonitor.Pulse();
        }

        private static int ToPowerOfTwo(int number)
        {
            return (int)Mathf.Pow(2, Mathf.CeilToInt(Mathf.Log(number, 2)));
        }

        #region Public API

        /// <summary>
        /// Decode image using <see cref="ImageDecoder"/>.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<DecodedImage> DecodeImageAsync(byte[] input, CancellationToken cancellationToken = default)
        {
            return DecodeImageAsync(new MemoryStream(input), cancellationToken);
        }

        /// <summary>
        /// Decode image using <see cref="ImageDecoder"/>.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<DecodedImage> DecodeImageAsync(Stream input, CancellationToken cancellationToken = default)
        {
            return ImageDecoder.DecodeImageAsync(input, cancellationToken);
        }


        /// <summary>
        /// Acquires a render texture that is compatible with this class. Can be called from any thread.
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="mipCount"></param>
        /// <param name="temporary"></param>
        /// <returns></returns>
        public async Task<RenderTexture> AcquireTextureAsync(int width, int height, int mipCount = -1, bool temporary = false)
        {
            // Switch to main thread if need be and possible
            await MainThreadRegister.Context;

            var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, 0, mipCount)
            {
                enableRandomWrite = true,
                autoGenerateMips = false,
                useMipMap = mipCount != 0
            };

            var tex = temporary
                ? RenderTexture.GetTemporary(descriptor)
                : new RenderTexture(descriptor);

            if (!temporary)
            {
                tex.Create();
            }

            return tex;
        }


        /// <summary>
        /// Asynchronously updates part of a texture with the provided pixel data.
        /// [IMPORTANT] For the time being, the data layout must be RGBA32.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="image"></param>
        /// <param name="token">An optional cancellation token which might trigger a <see cref="OperationCanceledException"/></param>
        /// <exception cref="OperationCanceledException">If the operation was cancelled.</exception>
        /// <exception cref="AssertionException">If the pre conditions weren't met.</exception>
        public async Task UploadDataAsync(RenderTexture texture, DecodedImage image, CancellationToken token = default)
        {
            await UploadDataAsync(texture, 0, 0, image.Width, image.Height, 0, image.Data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates part of a texture with the provided pixel data.
        /// [IMPORTANT] For the time being, the data layout must be RGBA32.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="data"></param>
        /// <param name="token">An optional cancellation token which might trigger a <see cref="OperationCanceledException"/></param>
        /// <exception cref="OperationCanceledException">If the operation was cancelled.</exception>
        /// <exception cref="AssertionException">If the pre conditions weren't met.</exception>
        public async Task UploadDataAsync(RenderTexture texture, int width, int height, byte[] data, CancellationToken token = default)
        {
            await UploadDataAsync(texture, 0, 0, width, height, 0, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates part of a texture with the provided pixel data.
        /// [IMPORTANT] For the time being, the data layout must be RGBA32.
        /// </summary>
        /// <param name="texture">The texture to copy the data into. Must have the <see cref="RenderTexture.enableRandomWrite"/> flag enabled.</param>
        /// <param name="xOffset">X offset from which to copy.</param>
        /// <param name="yOffset">Y offset from which to copy.</param>
        /// <param name="width">Amount in the x dimension to copy.</param>
        /// <param name="height">Amount in the y dimension to copy.</param>
        /// <param name="mipLevel">Which mip level to copy into. NOTE: Not properly implemented yet! Mips are automatically generated.</param>
        /// <param name="data">The actual pixel data as RGBA32.</param>
        /// <param name="token">An optional cancellation token which might trigger a <see cref="OperationCanceledException"/></param>
        /// <exception cref="OperationCanceledException">If the operation was canceled.</exception>
        /// <exception cref="AssertionException">If the pre conditions weren't met.</exception>
        public async Task UploadDataAsync(RenderTexture texture, int xOffset, int yOffset, int width, int height, int mipLevel, byte[] data,
            CancellationToken token = default)
        {
            token = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellationTokenSource.Token).Token;

            await MainThreadRegister.Context;

            // Check pre-conditions 
            Assert.IsTrue(texture.width >= xOffset + width);
            Assert.IsTrue(texture.height >= yOffset + height);
            Assert.IsTrue(xOffset >= 0);
            Assert.IsTrue(yOffset >= 0);

            var computeBuffer = await AcquireBuffer(width, height, token);
            try
            {
                // Upload to compute buffer (CPU -> GPU)
                int written = 0;
                var sw = Stopwatch.StartNew();
                Debug.Log("Starting copying...");
                while (written < data.Length)
                {
                    // Check for cancellation
                    token.ThrowIfCancellationRequested();

                    int toWrite = Mathf.Min(data.Length - written, BufferSize);
                    var intbuffer = computeBuffer.BeginWrite<uint>(written / 4, toWrite / 4);
                    var buffer = intbuffer.Reinterpret<byte>(sizeof(uint));

                    NativeArray<byte>.Copy(data, written, buffer, 0, toWrite);

                    written += toWrite;
                    computeBuffer.EndWrite<uint>(toWrite / 4);

                    if (sw.Elapsed.TotalMilliseconds > UploadTimeSlice)
                    {
                        await Task.Yield();
                        sw.Restart();
                    }
                }

                await Task.Yield();

                // Check for cancellation
                token.ThrowIfCancellationRequested();

                // Copy to texture (GPU -> GPU)
                _computeShader.SetTexture(0, _resultProp, texture, mipLevel);
                _computeShader.SetInt(_widthProp, width);
                _computeShader.SetInt(_heightProp, height);
                _computeShader.SetInt(_offsetXProp, xOffset);
                _computeShader.SetInt(_offsetYProp, yOffset);
                _computeShader.SetInt("ImageHeight", texture.height);
                _computeShader.SetBuffer(0, _inputProp, computeBuffer);

                _computeShader.Dispatch(0, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

                // Wait a frame
                await Task.Yield();

                if (texture.useMipMap)
                {
                    texture.GenerateMips();
                }

                // Check for cancellation
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                // Return buffer for others
                ReturnBuffer();
            }
        }


        /// <summary>
        /// Asynchronously loads a texture. Will automatically fall back to the blocking approach if compute shaders are not supported.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public async Task<Texture> LoadTextureAsync(Stream input)
        {
            if (SystemInfo.supportsComputeShaders)
            {
                var image = await DecodeImageAsync(input);
                var texture = await AcquireTextureAsync(image.Width, image.Height);
                await UploadDataAsync(texture, 0, 0, image.Width, image.Height, 0, image.Data);
                return texture;
            }
            else
            {
                Debug.LogWarning("System does not support compute shaders -- falling back to built-in method.");
                var texture = new Texture2D(1, 1);
                var bytes = new MemoryStream();
                await input.CopyToAsync(bytes);
                texture.LoadImage(bytes.ToArray());
                return texture;
            }
        }

        /// <summary>
        /// Asynchronously loads a texture. Will automatically fall back to the blocking approach if compute shaders are not supported.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public Task<Texture> LoadTextureAsync(byte[] bytes)
        {
            return LoadTextureAsync(new MemoryStream(bytes));
        }

        #endregion
    }
}