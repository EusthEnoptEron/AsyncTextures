using UnityEngine;

namespace Zomg.AsyncTextures.Types
{
    /// <summary>
    /// Represents a decoded image.
    /// </summary>
    public struct DecodedImage
    {
        /// <summary>
        /// Gets the width of the decoded image.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the decoded image.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the format of the decoded image.
        /// </summary>
        public TextureFormat Format => TextureFormat.RGBA32;

        /// <summary>
        /// Gets the raw data of the decoded image.
        /// </summary>
        public byte[] Data { get; }

        public DecodedImage(int width, int height, byte[] data)
        {
            Width = width;
            Height = height;
            Data = data;
        }
    }
}