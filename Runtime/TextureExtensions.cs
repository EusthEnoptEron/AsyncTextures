using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Zomg.AsyncTextures.Utils;

namespace Zomg.AsyncTextures
{
    public static class TextureExtensions
    {
        /// <summary>
        /// Loads data into an existing texture.
        /// Only works if the texture already has the right dimensions and format; otherwise you should probably be using <see cref="AsyncTextureLoader.LoadTextureAsync(System.IO.Stream)"/>.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="data"></param>
        /// <param name="cancellationToken"></param>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="Exception"></exception>
        public static async Task LoadImageAsync<T>(this T texture, byte[] data, CancellationToken cancellationToken = default) where T : Texture
        {
            await MainThreadRegister.Context;
            var decoded = await AsyncTextureLoader.Instance.DecodeImageAsync(data, cancellationToken);

            if (texture is RenderTexture rt && rt.enableRandomWrite)
            {
                await AsyncTextureLoader.Instance.UploadDataAsync(rt, decoded, cancellationToken);
            }
            else
            {
                rt = await AsyncTextureLoader.Instance.AcquireTextureAsync(decoded.Width, decoded.Height, temporary: true);
                try
                {
                    await AsyncTextureLoader.Instance.UploadDataAsync(rt, decoded, cancellationToken);

                    // Copy into existing texture
                    if (texture.width != rt.width || texture.height != rt.height)
                    {
                        if (texture is Texture2D tex2d)
                        {
                            tex2d.Resize(rt.width, rt.height);
                            tex2d.Apply();

                            Debug.LogWarning("Texture had to be resized -- use AsyncTextureLoader for true async texture generation!");
                        }
                        else
                        {
                            throw new Exception($"Texture has wrong size! Expected {rt.width}x{rt.height}, got {texture.width}x{texture.height}");
                        }
                    }

                    Graphics.CopyTexture(rt, texture);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }
    }
}