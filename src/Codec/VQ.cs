// Public Domain

namespace SVQNext.Codec;

public static class VQ
{
    public static (float[,] Codebook, float[] Mean) Build(float[][,] Y, int bs, int K, int sampleFrames, int search,
        int Q, string mode)
    {
        var patches = new List<float[]>();
        for (var i = 0; i < sampleFrames; i++)
        {
            var (mv, p) = Motion.BlockMatch(Y[i], Y[i + 1], bs, search, Q, mode);
            int Hc = p.GetLength(0), Wc = p.GetLength(1);
            var res = new float[Hc, Wc];
            for (var y = 0; y < Hc; y++)
            for (var x = 0; x < Wc; x++)
                res[y, x] = Y[i + 1][y, x] - p[y, x];
            patches.Add(Extract(res, bs, out _));
        }

        var all = Concat(patches);
        var M = ToMatrix(all, bs);
        int N = M.GetLength(0), D = M.GetLength(1);

        var rnd = new Random(1);
        var centers = new List<int> { rnd.Next(N) };
        var dist = new double[N];
        for (var kk = 1; kk < K; kk++)
        {
            double sum = 0;
            for (var i = 0; i < N; i++)
            {
                var d2 = double.MaxValue;
                foreach (var c in centers)
                {
                    double d = 0;
                    for (var j = 0; j < D; j++)
                    {
                        double df = M[i, j] - M[c, j];
                        d += df * df;
                    }

                    if (d < d2) d2 = d;
                }

                dist[i] = d2;
                sum += d2;
            }

            double r = rnd.NextDouble() * sum, acc = 0;
            var pick = 0;
            for (var i = 0; i < N; i++)
            {
                acc += dist[i];
                if (acc >= r)
                {
                    pick = i;
                    break;
                }
            }

            centers.Add(pick);
        }

        var code = new float[K, D];
        var mu = new float[K];
        for (var k = 0; k < K; k++)
        {
            double m = 0;
            for (var j = 0; j < D; j++) m += M[centers[k], j];
            m /= D;
            mu[k] = (float)m;
            for (var j = 0; j < D; j++) code[k, j] = M[centers[k], j] - (float)m;
        }

        var assign = new int[N];
        for (var it = 0; it < 8; it++)
        {
            Parallel.For(0, N, i =>
            {
                var best = 0;
                var bestv = double.MaxValue;
                for (var k = 0; k < K; k++)
                {
                    double d2 = 0;
                    for (var j = 0; j < D; j++)
                    {
                        double diff = M[i, j] - (code[k, j] + mu[k]);
                        d2 += diff * diff;
                    }

                    if (d2 < bestv)
                    {
                        bestv = d2;
                        best = k;
                    }
                }

                assign[i] = best;
            });
            var sum = new double[K, D];
            var cnt = new int[K];
            for (var i = 0; i < N; i++)
            {
                var k = assign[i];
                cnt[k]++;
                for (var j = 0; j < D; j++) sum[k, j] += M[i, j];
            }

            for (var k = 0; k < K; k++)
            {
                if (cnt[k] == 0) continue;
                double m = 0;
                for (var j = 0; j < D; j++) m += sum[k, j];
                m /= D * cnt[k];
                mu[k] = (float)m;
                for (var j = 0; j < D; j++) code[k, j] = (float)(sum[k, j] / cnt[k] - m);
            }
        }

        return (code, mu);
    }

    public static float[] Extract(float[,] img, int bs, out (int Hc, int Wc) shape)
    {
        int Hc = img.GetLength(0) / bs * bs, Wc = img.GetLength(1) / bs * bs;
        shape = (Hc, Wc);
        int gh = Hc / bs, gw = Wc / bs;
        var a = new float[gh * gw * bs * bs];
        var p = 0;
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        for (var yy = 0; yy < bs; yy++)
        for (var xx = 0; xx < bs; xx++)
            a[p++] = img[by * bs + yy, bx * bs + xx];
        return a;
    }

    public static float[,] ToMatrix(float[] blocks, int bs)
    {
        var N = blocks.Length / (bs * bs);
        var M = new float[N, bs * bs];
        Buffer.BlockCopy(blocks, 0, M, 0, blocks.Length * sizeof(float));
        return M;
    }

    private static float[] Concat(List<float[]> lists)
    {
        var len = 0;
        foreach (var a in lists) len += a.Length;
        var r = new float[len];
        var p = 0;
        foreach (var a in lists)
        {
            Buffer.BlockCopy(a, 0, r, p * sizeof(float), a.Length * sizeof(float));
            p += a.Length;
        }

        return r;
    }
}