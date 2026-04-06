// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class ColorModel
{
    // Public-domain constants for BT.709 and BT.2020
    public static readonly (double Kr, double Kb) BT709 = (0.2126, 0.0722);
    public static readonly (double Kr, double Kb) BT2020 = (0.2627, 0.0593);

    public static (double Kr, double Kb) GetPrimaries(string name)
    {
        return name.ToLower() switch
        {
            "bt2020" => BT2020,
            _ => BT709
        };
    }

    public static void RgbToYCbCr(float[,,] rgb, out float[,] Y, out float[,] Cb, out float[,] Cr, string cs = "bt709")
    {
        var (Kr, Kb) = GetPrimaries(cs);
        int h = rgb.GetLength(0), w = rgb.GetLength(1);
        Y = new float[h, w];
        Cb = new float[h, w];
        Cr = new float[h, w];
        var Kg = 1.0 - Kr - Kb;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            double R = rgb[y, x, 0], G = rgb[y, x, 1], B = rgb[y, x, 2];
            var Yp = Kr * R + Kg * G + Kb * B;
            var Cbp = (B - Yp) / (2 * (1 - Kb));
            var Crp = (R - Yp) / (2 * (1 - Kr));
            Y[y, x] = (float)Yp;
            Cb[y, x] = (float)Cbp;
            Cr[y, x] = (float)Crp;
        }
    }

    public static float[,,] YCbCrToRgb(float[,] Y, float[,] Cb, float[,] Cr, string cs = "bt709")
    {
        var (Kr, Kb) = GetPrimaries(cs);
        int h = Y.GetLength(0), w = Y.GetLength(1);
        var rgb = new float[h, w, 3];
        var Kg = 1.0 - Kr - Kb;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            double Yp = Y[y, x], Cbp = Cb[y, x], Crp = Cr[y, x];
            var R = Yp + 2 * (1 - Kr) * Crp;
            var B = Yp + 2 * (1 - Kb) * Cbp;
            var G = (Yp - Kr * R - Kb * B) / Kg;
            rgb[y, x, 0] = MathUtil.Clamp01((float)R);
            rgb[y, x, 1] = MathUtil.Clamp01((float)G);
            rgb[y, x, 2] = MathUtil.Clamp01((float)B);
        }

        return rgb;
    }

    public static float[,] Subsample420(float[,] c)
    {
        int h = c.GetLength(0), w = c.GetLength(1);
        int H2 = h / 2, W2 = w / 2;
        var outp = new float[H2, W2];
        for (var y = 0; y < H2; y++)
        for (var x = 0; x < W2; x++)
        {
            int yy = y * 2, xx = x * 2;
            var sum = c[yy, xx] + c[yy + 1, xx] + c[yy, xx + 1] + c[yy + 1, xx + 1];
            outp[y, x] = sum * 0.25f;
        }

        return outp;
    }

    public static float[,] Upsample420(float[,] c4)
    {
        int H2 = c4.GetLength(0), W2 = c4.GetLength(1);
        var c = new float[H2 * 2, W2 * 2];
        for (var y = 0; y < c.GetLength(0); y++)
        for (var x = 0; x < c.GetLength(1); x++)
        {
            var srcY = (y + 0.5f) * 0.5f - 0.5f;
            var srcX = (x + 0.5f) * 0.5f - 0.5f;
            c[y, x] = SampleBilinear(c4, srcY, srcX);
        }

        return c;
    }

    private static float SampleBilinear(float[,] plane, float y, float x)
    {
        var h = plane.GetLength(0);
        var w = plane.GetLength(1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, h - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, w - 1);
        var y1 = Math.Clamp(y0 + 1, 0, h - 1);
        var x1 = Math.Clamp(x0 + 1, 0, w - 1);
        var fy = y - y0;
        var fx = x - x0;
        var v00 = plane[y0, x0];
        var v10 = plane[y1, x0];
        var v01 = plane[y0, x1];
        var v11 = plane[y1, x1];
        var top = v00 * (1 - fx) + v01 * fx;
        var bottom = v10 * (1 - fx) + v11 * fx;
        return top * (1 - fy) + bottom * fy;
    }

    // Simple open HDR transfer (no PQ/HLG): piecewise gamma that maps 0..1 -> 0..1 with extended headroom.
    public static float EncodeHDR(float v, int bitdepth)
    {
        // map linear [0..1] to perceptual-ish curve
        var a = 0.17883277; // open constant (approx for display gamma compression)
        var b = 1 - 4 * a;
        var y = v < 0.5 ? Math.Pow(v, 1 / 2.2) : a * Math.Log(1 + (v - 0.5) * 10) + 0.5 * b;
        // quantize to bitdepth range later; keep float here
        return MathUtil.Clamp01((float)y);
    }

    public static float DecodeHDR(float v, int bitdepth)
    {
        // inverse of above (approx)
        var a = 0.17883277;
        var b = 1 - 4 * a;
        double lin;
        if (v < 0.5) lin = Math.Pow(v, 2.2);
        else lin = 0.5 + (Math.Exp((v - 0.5) / a) - 1.0) / 10.0;
        return MathUtil.Clamp01((float)lin);
    }
}
