using SixLabors.ImageSharp.PixelFormats;

namespace SVQNext.Optimized;

public static class ImageFastOps
{
    public delegate void RowAction(Span<Rgba32> row);

    public static void ApplySpan(RowAction rowAction, Bitmap img)
    {
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                rowAction(row);
            }
        });
    }

    public static void MapRows(Bitmap img, Func<Rgba32, Rgba32> f)
    {
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = f(row[x]);
            }
        });
    }
}