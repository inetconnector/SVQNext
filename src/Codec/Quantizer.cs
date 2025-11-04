// Public Domain

namespace SVQNext.Codec;

public static class Quantizer
{
    public const double DC_SCALE = 1024.0;

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
}