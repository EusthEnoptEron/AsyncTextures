using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StbImageSharp;
using Zomg.AsyncTextures.Types;

namespace Zomg.AsyncTextures
{
    public class StbImageDecoder : IAsyncImageDecoder
    {
        public Task<DecodedImage> DecodeImageAsync(Stream input, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var stream = input;

                if (!stream.CanSeek)
                {
                    // Fallback because stream has to be seekable
                    stream = new MemoryStream();
                    await input.CopyToAsync(stream);
                    stream.Seek(0, SeekOrigin.Begin);
                }

                var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                return new DecodedImage(img.Width, img.Height, img.Data);
            });
        }
    }
}