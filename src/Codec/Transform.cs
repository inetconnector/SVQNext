// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Transform
{
    public static void HadamardBlock(float[,] block, int bs)
    {
        MathUtil.Hadamard2D(block, bs);
    }

    public static float[,] Sub(float[,] A, float[,] B)
    {
        int hA = A.GetLength(0), wA = A.GetLength(1);
        int hB = B.GetLength(0), wB = B.GetLength(1);

        if (hA != hB || wA != wB)
            throw new ArgumentException($"Matrix sizes differ: A={hA}x{wA}, B={hB}x{wB}");

        var R = new float[hA, wA];

        for (int y = 0; y < hA; y++)
        for (int x = 0; x < wA; x++)
            R[y, x] = A[y, x] - B[y, x];

        return R;
    }

    public static float[,] AlignToBlock(float[,] src, int bs)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        if (bs <= 0) throw new ArgumentOutOfRangeException(nameof(bs));

        int h = src.GetLength(0);
        int w = src.GetLength(1);

        int newH = ((h + bs - 1) / bs) * bs;
        int newW = ((w + bs - 1) / bs) * bs;

        if (newH == h && newW == w)
            return src;

        var padded = new float[newH, newW];
        var lastY = Math.Max(0, h - 1);
        var lastX = Math.Max(0, w - 1);

        for (var y = 0; y < newH; y++)
        {
            var sy = y < h ? y : lastY;
            for (var x = 0; x < newW; x++)
            {
                var sx = x < w ? x : lastX;
                padded[y, x] = src[sy, sx];
            }
        }

        return padded;
    }

}