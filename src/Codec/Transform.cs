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
        int h = src.GetLength(0);
        int w = src.GetLength(1);

        int newH = (h / bs) * bs;
        int newW = (w / bs) * bs;

        // Already aligned?
        if (newH == h && newW == w)
            return src;

        var r = new float[newH, newW];

        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            r[y, x] = src[y, x];

        return r;
    }

}