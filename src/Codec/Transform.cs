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
    
    public static float[,] ExtractBlock(float[,] src, int x0, int y0, int bs)
    {
        var block = new float[bs, bs];
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            block[y, x] = src[y0 + y, x0 + x];
        return block;
    }

    public static void WriteBlock(float[,] dst, int x0, int y0, float[,] block)
    {
        var bs = block.GetLength(0);
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            dst[y0 + y, x0 + x] = block[y, x];
    }

    public static float[,] ForwardHadamard(float[,] block)
    {
        var bs = block.GetLength(0);
        var coeffs = (float[,])block.Clone();
        HadamardBlock(coeffs, bs);
        return coeffs;
    }

    public static float[,] InverseHadamard(float[,] coeffs)
    {
        var bs = coeffs.GetLength(0);
        var block = (float[,])coeffs.Clone();
        HadamardBlock(block, bs);
        var scale = 1f / (bs * bs);
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            block[y, x] *= scale;
        return block;
    }
}
