
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SVQNext.Compat
{
    public static class ImageSharpCompat
    {
        public static Bitmap NewBitmap(int width, int height)
        {
            return new Image<Rgba32>(width, height);
        }

        public static Bitmap LoadBitmap(string path)
        {
            return Image.Load<Rgba32>(path);
        }

        public static void SaveBitmap(Bitmap img, string path)
        {
            img.Save(path); // format inferred from extension
        }

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
