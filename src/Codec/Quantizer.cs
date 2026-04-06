// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Quantizer
{
    public const double DC_SCALE = 1024.0;
    public const double GAIN_SCALE = 256.0;

    public static short[] Q_DC(float[] dc)
    {
        var q = new short[dc.Length];
        for (var i = 0; i < dc.Length; i++)
        {
            var v = (int)Math.Round(dc[i] * DC_SCALE);
            v = Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
            q[i] = (short)v;
        }

        return q;
    }

    public static float[] DEQ_DC(short[] q)
    {
        var f = new float[q.Length];
        for (var i = 0; i < q.Length; i++) f[i] = q[i] / (float)DC_SCALE;
        return f;
    }

    public static double Lambda(double energy, string quality)
    {
        var baseL = quality == "ultra" ? 0.015 : quality == "fast" ? 0.03 : 0.02;
        return baseL * (1.0 + 0.25 * energy);
    }

    public static short Q_Gain(float gain)
    {
        var v = (int)Math.Round(gain * GAIN_SCALE);
        v = Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
        return (short)v;
    }

    public static float[] DEQ_Gain(short[] q)
    {
        var f = new float[q.Length];
        for (var i = 0; i < q.Length; i++) f[i] = q[i] / (float)GAIN_SCALE;
        return f;
    }

    public static float TransformStep(string quality, bool intra, int blockSize)
    {
        var baseStep = quality == "ultra" ? 0.020f : quality == "fast" ? 0.042f : 0.030f;
        if (intra)
            baseStep *= 0.90f;
        if (blockSize <= 4)
            baseStep *= 0.85f;
        return baseStep;
    }

    public static short QuantizeTransform(float value, float step)
    {
        step = Math.Max(step, 1e-6f);
        if (Math.Abs(value) < step * 0.60f)
            return 0;

        var q = (int)Math.Round(value / step);
        return (short)Math.Clamp(q, short.MinValue, short.MaxValue);
    }

    public static float DequantizeTransform(short value, float step)
    {
        return value * step;
    }

    public static int EstimateCoeffBits(short coeff)
    {
        if (coeff == 0)
            return 1;

        var magnitude = Math.Abs((int)coeff);
        return 2 + (int)Math.Floor(Math.Log(magnitude, 2)) + 1;
    }

    public static int EstimateSparseTransformBits(short[] coeffs, int bs)
    {
        if (bs > 4)
            return EstimateSparseTransformBitsLinear(coeffs);

        var bits = 0;
        var zeroRun = 0;
        var nonZeroCount = 0;
        foreach (var index in ZigZagIndices(bs))
        {
            if (coeffs[index] == 0)
            {
                zeroRun++;
                continue;
            }

            nonZeroCount++;
            zeroRun = 0;
        }

        bits += EstimateVarUIntBits((uint)nonZeroCount);
        zeroRun = 0;
        foreach (var index in ZigZagIndices(bs))
        {
            if (coeffs[index] == 0)
            {
                zeroRun++;
                continue;
            }

            bits += EstimateVarUIntBits((uint)zeroRun);
            bits += EstimateVarUIntBits(ZigZagEncode(coeffs[index]));
            zeroRun = 0;
        }

        return bits;
    }

    private static int EstimateSparseTransformBitsLinear(short[] coeffs)
    {
        var bits = 0;
        var zeroRun = 0;
        var nonZeroCount = 0;
        for (var i = 0; i < coeffs.Length; i++)
        {
            if (coeffs[i] == 0)
                continue;
            nonZeroCount++;
        }

        bits += EstimateVarUIntBits((uint)nonZeroCount);
        for (var i = 0; i < coeffs.Length; i++)
        {
            if (coeffs[i] == 0)
            {
                zeroRun++;
                continue;
            }

            bits += EstimateVarUIntBits((uint)zeroRun);
            bits += EstimateVarUIntBits(ZigZagEncode(coeffs[i]));
            zeroRun = 0;
        }

        return bits;
    }

    public static int EstimateVarUIntBits(uint value)
    {
        return EstimateVarUIntBytes(value) * 8;
    }

    public static int EstimateVarUIntBytes(uint value)
    {
        var bytes = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            bytes++;
        }

        return bytes;
    }

    public static uint ZigZagEncode(short value)
    {
        var v = value;
        return (uint)((v << 1) ^ (v >> 15));
    }

    public static void PruneTransform(short[] coeffs, string quality, int bs)
    {
        var order = ZigZagIndices(bs);
        for (var rank = 1; rank < order.Length; rank++)
        {
            var threshold = GetPruneThreshold(rank, order.Length, quality);
            if (threshold <= 0)
                continue;

            var index = order[rank];
            if (Math.Abs((int)coeffs[index]) <= threshold)
                coeffs[index] = 0;
        }
    }

    private static int GetPruneThreshold(int rank, int total, string quality)
    {
        if (quality == "ultra")
        {
            if (rank > total * 3 / 4) return 1;
            return 0;
        }

        if (quality == "fast")
        {
            if (rank > total / 3) return 2;
            if (rank > total / 5) return 1;
            return 0;
        }

        if (rank > total * 2 / 3) return 1;
        return 0;
    }

    public static int[] ZigZagIndices(int bs)
    {
        var order = new int[bs * bs];
        var i = 0;
        for (var diag = 0; diag <= (bs - 1) * 2; diag++)
        {
            var yStart = Math.Max(0, diag - (bs - 1));
            var yEnd = Math.Min(bs - 1, diag);
            if ((diag & 1) == 0)
            {
                for (var y = yEnd; y >= yStart; y--)
                {
                    var x = diag - y;
                    order[i++] = y * bs + x;
                }
            }
            else
            {
                for (var y = yStart; y <= yEnd; y++)
                {
                    var x = diag - y;
                    order[i++] = y * bs + x;
                }
            }
        }

        return order;
    }
}
