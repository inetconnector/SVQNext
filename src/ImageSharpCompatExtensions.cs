
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SVQNext.Compat
{
    /// <summary>
    /// Light-weight shim that offers familiar Bitmap-style helpers while internally
    /// using ImageSharp's fast GPU-friendly buffers.
    /// </summary>
    public static class ImageSharpCompat
    {
        /// <summary>
        /// Allocates a new RGBA bitmap backed by ImageSharp.
        /// </summary>
        public static Bitmap NewBitmap(int width, int height)
        {
            return new Image<Rgba32>(width, height);
        }

        /// <summary>
        /// Loads a bitmap from disk using ImageSharp's decoder pipeline.
        /// </summary>
        public static Bitmap LoadBitmap(string path)
        {
            return Image.Load<Rgba32>(path);
        }

        /// <summary>
        /// Saves a bitmap to disk; the format is inferred from the file extension.
        /// </summary>
        public static void SaveBitmap(Bitmap img, string path)
        {
            img.Save(path); // format inferred from extension
        }

        /// <summary>
        /// Extension helpers to mirror the old System.Drawing API surface.
        /// </summary>
        public static int Width(this Bitmap img) => img.Width;
        public static int Height(this Bitmap img) => img.Height;

        public static Rgba32 GetPixel(this Bitmap img, int x, int y)
        {
            return img[x, y];
        }

        public static void SetPixel(this Bitmap img, int x, int y, Rgba32 color)
        {
            img[x, y] = color;
        }
    }
}
