
using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SVQNext.Optimized
{
    public static class ImageFastOps
    {
        public static void ApplySpan(Action<Span<Rgba32>> rowAction, Image<Rgba32> img)
        {
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    rowAction(row);
                }
            });
        }

        public static void MapRows(Image<Rgba32> img, Func<Rgba32, Rgba32> f)
        {
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        row[x] = f(row[x]);
                    }
                }
            });
        }
    }
}
