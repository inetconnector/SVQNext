// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Encoder
{
    public static EncodedSequence Encode(float[][,] Y, int bs, int search, int QMotion, float[,] codebook,
        float[] meanVec, string quality, string searchMode, int gop, bool useB, bool loop)
    {
        var T = Y.Length;
        var frames = new EncodedFrame[T];
        (int Hc, int Wc) shape = (Y[0].GetLength(0) / bs * bs, Y[0].GetLength(1) / bs * bs);
        var hist = new Dictionary<int, int>();

        // I-frame 0
        frames[0] = EncodeFrame(Y[0], new float[shape.Hc, shape.Wc], codebook, meanVec, hist, bs, quality);

        for (var t = 1; t < T; t++)
        {
            var (mv, pred) = Motion.BlockMatch(Y[t - 1], Y[t], bs, search, QMotion, searchMode);
            frames[t] = EncodeFrame(Y[t], pred, codebook, meanVec, hist, bs, quality);
            frames[t].MV = mv;
            frames[t].RefPrev = t - 1;
        }

        return new EncodedSequence
        {
            Frames = frames, T = T, H = shape.Hc, W = shape.Wc, BS = bs, QMotion = QMotion, Search = search, GOP = gop,
            SearchMode = searchMode, Loop = loop, UseB = useB
        };
    }

    private static EncodedFrame EncodeFrame(float[,] curr, float[,] pred, float[,] codebook, float[] mu,
        Dictionary<int, int> hist, int bs, string quality)
    {
        int Hc = curr.GetLength(0) / bs * bs, Wc = curr.GetLength(1) / bs * bs, gh = Hc / bs, gw = Wc / bs;
        curr = Transform.AlignToBlock(curr, bs);
        pred = Transform.AlignToBlock(pred, bs);
        var res = Transform.Sub(curr, pred);
        var blocks = VQ.Extract(res, bs, out _);
        var centered = Center(blocks, bs, out var dc);
        int N = centered.GetLength(0), D = centered.GetLength(1), K = codebook.GetLength(0);
        var (bits, worst) = EstimateBits(hist, K);
        var idx = new ushort[N];
        Parallel.For(0, N, n =>
        {
            var best = double.MaxValue;
            var bestK = 0;
            // energy proxy
            double energy = 0;
            for (var j = 0; j < D; j++)
            {
                double d = centered[n, j];
                energy += d * d;
            }

            var lam = Quantizer.Lambda(energy, quality);
            for (var k = 0; k < K; k++)
            {
                double d2 = 0;
                for (var j = 0; j < D; j++)
                {
                    double diff = centered[n, j] - codebook[k, j];
                    d2 += diff * diff;
                }

                var cost = d2 + lam * (bits[k] > 0 ? bits[k] : worst);
                if (cost < best)
                {
                    best = cost;
                    bestK = k;
                }
            }

            idx[n] = (ushort)bestK;
        });
        foreach (var i in idx)
        {
            if (!hist.ContainsKey(i)) hist[i] = 0;
            hist[i]++;
        }

        var dcq = Quantizer.Q_DC(dc);
        return new EncodedFrame { MV = new short[gh, gw, 2], Idx = idx, DCq = dcq, Shape = (Hc, Wc) };
    }

    private static float[,] Center(float[] blocks, int bs, out float[] dc)
    {
        var N = blocks.Length / (bs * bs);
        var M = new float[N, bs * bs];
        dc = new float[N];
        var p = 0;
        for (var n = 0; n < N; n++)
        {
            double sum = 0;
            for (var i = 0; i < bs * bs; i++) sum += blocks[p + i];
            var m = (float)(sum / (bs * bs));
            dc[n] = m;
            for (var i = 0; i < bs * bs; i++) M[n, i] = blocks[p + i] - m;
            p += bs * bs;
        }

        return M;
    }

    private static (float[] bits, float worst) EstimateBits(Dictionary<int, int> hist, int K)
    {
        var bits = new float[K];
        if (hist.Count == 0) return (bits, 16f);
        var total = hist.Values.Sum() + 1e-9;
        var worst = 16f;
        foreach (var kv in hist)
        {
            var k = kv.Key;
            if (k < 0 || k >= K) continue;
            var p = Math.Max(kv.Value / total, 1e-12);
            bits[k] = (float)-Math.Log(p, 2);
            if (bits[k] > worst) worst = bits[k];
        }

        return (bits, worst);
    }
}