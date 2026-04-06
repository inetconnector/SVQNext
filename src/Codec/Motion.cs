// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Motion
{
    public static (short[,,] MV, float[,] Pred) BlockMatch(float[,] refImg, float[,] curImg, int bs, int search, int Q,
        string mode)
    {
        int Hc = refImg.GetLength(0) / bs * bs, Wc = refImg.GetLength(1) / bs * bs;
        int gh = Hc / bs, gw = Wc / bs;
        var mv = new short[gh, gw, 2];
        var pred = new float[Hc, Wc];
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            int x0 = bx * bs, y0 = by * bs;
            var (startU, startV) = PredictMotionStart(mv, by, bx);
            var (u, v) = mode == "full"
                ? Full(refImg, curImg, x0, y0, bs, search, Q, startU, startV)
                : Diamond(refImg, curImg, x0, y0, bs, search, Q, startU, startV);
            mv[by, bx, 0] = (short)u;
            mv[by, bx, 1] = (short)v;
            for (var yy = 0; yy < bs; yy++)
            for (var xx = 0; xx < bs; xx++)
                pred[y0 + yy, x0 + xx] = Sample(refImg, ((x0 + xx) << Q) + u, ((y0 + yy) << Q) + v, Q);
        }

        return (mv, pred);
    }

    public static float[,] Compensate(float[,] refImg, short[,,] mv, int bs, int Q)
    {
        int Hc = refImg.GetLength(0) / bs * bs, Wc = refImg.GetLength(1) / bs * bs;
        int gh = Hc / bs, gw = Wc / bs;
        var pred = new float[Hc, Wc];

        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            int x0 = bx * bs, y0 = by * bs;
            var u = mv[by, bx, 0];
            var v = mv[by, bx, 1];
            for (var yy = 0; yy < bs; yy++)
            for (var xx = 0; xx < bs; xx++)
                pred[y0 + yy, x0 + xx] = Sample(refImg, ((x0 + xx) << Q) + u, ((y0 + yy) << Q) + v, Q);
        }

        return pred;
    }

    private static (int, int) Full(float[,] refImg, float[,] cur, int x0, int y0, int bs, int search, int Q, int startU, int startV)
    {
        var best = double.MaxValue;
        int bu = startU, bv = startV, R = search * (1 << Q);
        for (var dv = -R; dv <= R; dv++)
        for (var du = -R; du <= R; du++)
        {
            var candU = startU + du;
            var candV = startV + dv;
            var d = SAD(refImg, cur, x0, y0, bs, candU, candV, Q);
            if (d < best)
            {
                best = d;
                bu = candU;
                bv = candV;
            }
        }

        return (bu, bv);
    }

    private static (int, int) Diamond(float[,] refImg, float[,] cur, int x0, int y0, int bs, int search, int Q, int startU, int startV)
    {
        int step = 1 << Q, R = search * step;
        int u = Math.Clamp(startU, -R, R), v = Math.Clamp(startV, -R, R);
        var best = SAD(refImg, cur, x0, y0, bs, u, v, Q);
        var improved = true;
        int[][] dirs = new[] { new[] { step, 0 }, new[] { -step, 0 }, new[] { 0, step }, new[] { 0, -step } };
        while (improved)
        {
            improved = false;
            foreach (var d in dirs)
            {
                int uu = u + d[0], vv = v + d[1];
                if (Math.Abs(uu) > R || Math.Abs(vv) > R) continue;
                var val = SAD(refImg, cur, x0, y0, bs, uu, vv, Q);
                if (val < best)
                {
                    best = val;
                    u = uu;
                    v = vv;
                    improved = true;
                }
            }
        }

        return (u, v);
    }

    private static (int U, int V) PredictMotionStart(short[,,] mv, int by, int bx)
    {
        var hasLeft = bx > 0;
        var hasTop = by > 0;
        var hasTopLeft = hasLeft && hasTop;
        if (hasLeft && hasTop && hasTopLeft)
        {
            var leftU = mv[by, bx - 1, 0];
            var topU = mv[by - 1, bx, 0];
            var topLeftU = mv[by - 1, bx - 1, 0];
            var leftV = mv[by, bx - 1, 1];
            var topV = mv[by - 1, bx, 1];
            var topLeftV = mv[by - 1, bx - 1, 1];
            return (
                Median(leftU, topU, leftU + topU - topLeftU),
                Median(leftV, topV, leftV + topV - topLeftV));
        }

        if (hasLeft) return (mv[by, bx - 1, 0], mv[by, bx - 1, 1]);
        if (hasTop) return (mv[by - 1, bx, 0], mv[by - 1, bx, 1]);
        return (0, 0);
    }

    private static int Median(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return b;
    }

    private static double SAD(float[,] refImg, float[,] cur, int x0, int y0, int bs, int du, int dv, int Q)
    {
        double s = 0;
        for (var yy = 0; yy < bs; yy++)
        for (var xx = 0; xx < bs; xx++)
        {
            var r = Sample(refImg, ((x0 + xx) << Q) + du, ((y0 + yy) << Q) + dv, Q);
            var d = cur[y0 + yy, x0 + xx] - r;
            s += d * d;
        }

        return s;
    }

    public static float Sample(float[,] img, int x_q, int y_q, int Q)
    {
        int H = img.GetLength(0), W = img.GetLength(1);
        int xi = x_q >> Q, yi = y_q >> Q;
        var mask = (1 << Q) - 1;
        float fx = (x_q & mask) / (float)(1 << Q), fy = (y_q & mask) / (float)(1 << Q);
        int x1 = Math.Max(0, Math.Min(W - 1, xi)), y1 = Math.Max(0, Math.Min(H - 1, yi));
        int x2 = Math.Max(0, Math.Min(W - 1, xi + 1)), y2 = Math.Max(0, Math.Min(H - 1, yi + 1));
        float v11 = img[y1, x1], v21 = img[y1, x2], v12 = img[y2, x1], v22 = img[y2, x2];
        float v1 = v11 * (1 - fx) + v21 * fx, v2 = v12 * (1 - fx) + v22 * fx;
        return v1 * (1 - fy) + v2 * fy;
    }
}
