// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SVQNext.Compat;

namespace SVQNext.Codec;

/// <summary>
///     Helper routines that translate between ImageSharp bitmaps and the float based
///     representation used throughout the codec. The implementations favor tight loops
///     over convenience APIs to keep memory bandwidth high and allocations low.
/// </summary>
public static class ImageIO
{
    /// <summary>
    ///     Converts an ImageSharp bitmap into an RGB float cube in the 0..1 range.
    /// </summary>
    public static float[,,] ToFloatRgb(Bitmap bmp)
    {
        var h = bmp.Height;
        var w = bmp.Width;
        var result = new float[h, w, 3];

        for (var y = 0; y < h; y++)
        {
            var row = bmp.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < w; x++)
            {
                var c = row[x];
                result[y, x, 0] = c.R / 255f;
                result[y, x, 1] = c.G / 255f;
                result[y, x, 2] = c.B / 255f;
            }
        }

        return result;
    }

    /// <summary>
    ///     Converts an RGB float cube in the 0..1 range back into an ImageSharp bitmap.
    /// </summary>
    public static Bitmap ToBitmap(float[,,] rgb)
    {
        var h = rgb.GetLength(0);
        var w = rgb.GetLength(1);
        var bmp = ImageSharpCompat.NewBitmap(w, h);

        for (var y = 0; y < h; y++)
        {
            var row = bmp.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < w; x++)
                row[x] = new Rgba32(
                    (byte)MathUtil.Clamp01To255(rgb[y, x, 0]),
                    (byte)MathUtil.Clamp01To255(rgb[y, x, 1]),
                    (byte)MathUtil.Clamp01To255(rgb[y, x, 2])
                );
        }

        return bmp;
    }

    /// <summary>
    ///     Saves a sequence of frames as a looping GIF on disk.
    /// </summary>
    public static void SaveGif(IReadOnlyList<Bitmap> frames, string path)
    {
        if (frames == null) throw new ArgumentNullException(nameof(frames));
        if (frames.Count == 0)
            throw new ArgumentException("At least one frame is required to encode a GIF.", nameof(frames));

        var expected = new List<Rgba32[]>(frames.Count);
        for (var i = 0; i < frames.Count; i++) expected.Add(CapturePixels(frames[i]));

        const int compressionFactor = 4;
        const int maxChannelError = 2;
        const double maxMeanSquaredError = 0.75;

        var attempts = new GifEncoder?[]
        {
            null,
            new GifEncoder { ColorTableMode = GifColorTableMode.Local },
            new GifEncoder { ColorTableMode = GifColorTableMode.Global }
        };

        foreach (var encoder in attempts)
            if (TryEncodeGif(
                    frames,
                    path,
                    expected,
                    encoder,
                    compressionFactor,
                    maxChannelError,
                    maxMeanSquaredError
                ))
                return;

        throw new InvalidOperationException("Failed to encode GIF near-losslessly with sufficient compression.");
    }

    private static bool TryEncodeGif(IReadOnlyList<Bitmap> frames, string path, IReadOnlyList<Rgba32[]> expected,
        GifEncoder? encoder, int compressionFactor, int maxChannelError, double maxMeanSquaredError)
    {
        using var gif = BuildGif(frames);
        ConfigureGifMetadata(gif);

        if (encoder == null)
            gif.SaveAsGif(path);
        else
            gif.Save(path, encoder);

        var encodedBytes = new FileInfo(path).Length;

        return GifNearLossless(path, expected, gif.Width, gif.Height, maxChannelError, maxMeanSquaredError) &&
               EncodedIsSignificantlySmaller(encodedBytes, frames.Count, gif.Width, gif.Height, compressionFactor);
    }

    private static Image<Rgba32> BuildGif(IReadOnlyList<Bitmap> frames)
    {
        var gif = frames[0].Clone();

        for (var i = 1; i < frames.Count; i++)
        {
            using var clone = frames[i].Clone();
            gif.Frames.AddFrame(clone.Frames.RootFrame);
        }

        return gif;
    }

    private static void ConfigureGifMetadata(Image<Rgba32> gif)
    {
        foreach (var frame in gif.Frames)
        {
            var meta = frame.Metadata.GetGifMetadata();
            meta.FrameDelay = 4;
        }

        gif.Metadata.GetGifMetadata().RepeatCount = 0;
    }

    private static Rgba32[] CapturePixels(Bitmap frame)
    {
        var pixels = new Rgba32[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static bool GifNearLossless(string path, IReadOnlyList<Rgba32[]> expected, int width, int height,
        int maxChannelError, double maxMeanSquaredError)
    {
        using var image = Image.Load<Rgba32>(path);
        if (image.Frames.Count != expected.Count) return false;

        var totalSquaredError = 0d;
        var totalSamples = 0L;
        var maxObservedError = 0;

        for (var i = 0; i < expected.Count; i++)
        {
            var frame = image.Frames[i];
            if (frame.Width != width || frame.Height != height) return false;

            var pixels = new Rgba32[width * height];
            frame.CopyPixelDataTo(pixels);
            var reference = expected[i];

            for (var p = 0; p < pixels.Length; p++)
            {
                var decoded = pixels[p];
                var source = reference[p];

                var dr = Math.Abs(decoded.R - source.R);
                var dg = Math.Abs(decoded.G - source.G);
                var db = Math.Abs(decoded.B - source.B);

                maxObservedError = Math.Max(maxObservedError, Math.Max(dr, Math.Max(dg, db)));

                totalSquaredError += dr * dr + dg * dg + db * db;
                totalSamples += 3;
            }
        }

        var meanSquaredError = totalSamples == 0 ? 0 : totalSquaredError / totalSamples;
        return maxObservedError <= maxChannelError && meanSquaredError <= maxMeanSquaredError;
    }

    private static bool EncodedIsSignificantlySmaller(long encodedBytes, int frameCount, int width, int height,
        int compressionFactor)
    {
        if (frameCount == 0) return false;

        var encodedPerFrame = (double)encodedBytes / frameCount;
        var decodedPerFrame = width * height * 4.0;

        return encodedPerFrame * compressionFactor <= decodedPerFrame;
    }

    /// <summary>
    ///     Generates a set of vivid demo frames with a moving highlight to showcase the codec.
    /// </summary>
    public static List<Bitmap> GenerateDemo(int frameCount, int height, int width)
    {
        var frames = new List<Bitmap>(frameCount);

        for (var t = 0; t < frameCount; t++)
        {
            var bmp = ImageSharpCompat.NewBitmap(width, height);

            for (var y = 0; y < height; y++)
            {
                var row = bmp.DangerousGetPixelRowMemory(y).Span;
                for (var x = 0; x < width; x++)
                    row[x] = new Rgba32(
                        (byte)((x * 2 + t * 3) & 0xFF),
                        (byte)((y * 2 + t * 3) & 0xFF),
                        (byte)((x + y + t * 3) & 0xFF)
                    );
            }

            var cx = (int)(width / 2.0 + width / 3.0 * Math.Sin(2 * Math.PI * t / 60.0));
            var cy = (int)(height / 2.0 + height / 4.0 * Math.Cos(2 * Math.PI * t / 60.0));
            const int radius = 12;
            var radiusSquared = radius * radius;

            for (var dy = -radius; dy <= radius; dy++)
            {
                var y = cy + dy;
                if ((uint)y >= (uint)height) continue;

                var row = bmp.DangerousGetPixelRowMemory(y).Span;
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (dy * dy + dx * dx > radiusSquared) continue;

                    var x = cx + dx;
                    if ((uint)x >= (uint)width) continue;

                    row[x] = new Rgba32(255, 255, 255);
                }
            }

            frames.Add(bmp);
        }

        return frames;
    }
}