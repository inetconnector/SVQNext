// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class MathUtil
{
    public static float Clamp01(float v)
    {
        return Math.Max(0f, Math.Min(1f, v));
    }

    public static int Clamp01To255(float v)
    {
        return (int)Math.Round(Math.Max(0, Math.Min(1, v)) * 255.0);
    }

    public static double PSNR(float[,,] A, float[,,] B)
    {
        int h = A.GetLength(0), w = A.GetLength(1);
        double mse = 0, n = h * w * 3;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 3; c++)
        {
            double d = A[y, x, c] - B[y, x, c];
            mse += d * d;
        }

        mse /= n;
        if (mse <= 1e-12) return 99.0;
        return 10.0 * Math.Log10(1.0 / mse);
    }

    public static double SSIM(float[,,] A, float[,,] B)
    {
        int h = A.GetLength(0), w = A.GetLength(1);
        double C1 = 0.01 * 0.01, C2 = 0.03 * 0.03;
        double sum = 0;
        for (var c = 0; c < 3; c++)
        {
            double muA = 0, muB = 0;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                muA += A[y, x, c];
                muB += B[y, x, c];
            }

            muA /= h;
            muA /= w;
            muB /= h;
            muB /= w;
            double varA = 0, varB = 0, cov = 0;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                double da = A[y, x, c] - muA, db = B[y, x, c] - muB;
                varA += da * da;
                varB += db * db;
                cov += da * db;
            }

            varA /= h * w - 1;
            varB /= h * w - 1;
            cov /= h * w - 1;
            var num = (2 * muA * muB + C1) * (2 * cov + C2);
            var den = (muA * muA + muB * muB + C1) * (varA + varB + C2);
            sum += num / den;
        }

        return sum / 3.0;
    }

    public static void Hadamard2D(float[,] arr, int bs)
    {
        for (var y = 0; y < bs; y++)
        for (var s = 1; s < bs; s <<= 1)
        for (var i = 0; i < bs; i += s << 1)
        for (var j = 0; j < s; j++)
        {
            float a = arr[y, i + j], b = arr[y, i + j + s];
            arr[y, i + j] = a + b;
            arr[y, i + j + s] = a - b;
        }

        for (var x = 0; x < bs; x++)
        for (var s = 1; s < bs; s <<= 1)
        for (var i = 0; i < bs; i += s << 1)
        for (var j = 0; j < s; j++)
        {
            float a = arr[i + j, x], b = arr[i + j + s, x];
            arr[i + j, x] = a + b;
            arr[i + j + s, x] = a - b;
        }
    }

    public static float[,,] CropRgb(float[,,] src, int targetH, int targetW)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        var srcH = src.GetLength(0);
        var srcW = src.GetLength(1);
        if (targetH <= 0 || targetH > srcH) targetH = srcH;
        if (targetW <= 0 || targetW > srcW) targetW = srcW;
        var cropped = new float[targetH, targetW, 3];
        for (var y = 0; y < targetH; y++)
        for (var x = 0; x < targetW; x++)
        for (var c = 0; c < 3; c++)
            cropped[y, x, c] = src[y, x, c];
        return cropped;
    }
}