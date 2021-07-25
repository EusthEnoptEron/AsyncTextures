using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zomg.AsyncTextures.Types;

namespace Zomg.AsyncTextures
{
    /// <summary>
    /// Interface for custom image decoders.
    /// </summary>
    public interface IAsyncImageDecoder
    {
        /// <summary>
        /// Decodes an image into the RGBA32 format.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<DecodedImage> DecodeImageAsync(Stream input, CancellationToken cancellationToken = default);
    }
}