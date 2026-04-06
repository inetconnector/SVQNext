// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Encoder
{
    public static EncodedSequence Encode(float[][,] Y, int bs, int search, int QMotion, float[,] codebook,
        float[] meanVec, string quality, string searchMode, int gop, bool useB, bool loop)
    {
        var T = Y.Length;
        var frames = new EncodedFrame[T];
        for (var i = 0; i < T; i++)
            Y[i] = Transform.AlignToBlock(Y[i], bs);
        (int Hc, int Wc) shape = (Y[0].GetLength(0), Y[0].GetLength(1));
        var hist = new Dictionary<int, int>();

        // I-frame 0
        frames[0] = EncodeFrame(Y[0], new float[shape.Hc, shape.Wc], codebook, meanVec, hist, bs, quality);
        frames[0].IsRefresh = true;
        frames[0].RefPrev = -1;

        for (var t = 1; t < T; t++)
        {
            var (mv, pred) = Motion.BlockMatch(Y[t - 1], Y[t], bs, search, QMotion, searchMode);
            if (ShouldRefreshFrame(Y[t], pred, t, gop, quality, bs))
            {
                hist.Clear();
                frames[t] = EncodeFrame(Y[t], new float[shape.Hc, shape.Wc], codebook, meanVec, hist, bs, quality);
                frames[t].IsRefresh = true;
                frames[t].RefPrev = -1;
                continue;
            }

            var (refinedMv, refinedPred, modes) = SelectInterModes(Y[t - 1], Y[t], mv, bs, QMotion, quality);
            frames[t] = EncodeFrame(Y[t], refinedPred, codebook, meanVec, hist, bs, quality, modes);
            frames[t].MV = refinedMv;
            frames[t].Modes = modes;
            frames[t].RefPrev = t - 1;
        }

        return new EncodedSequence
        {
            Frames = frames, T = T, H = shape.Hc, W = shape.Wc, BS = bs, QMotion = QMotion, Search = search, GOP = gop,
            SearchMode = searchMode, Loop = loop, UseB = useB
        };
    }

    private static EncodedFrame EncodeFrame(float[,] curr, float[,] pred, float[,] codebook, float[] mu,
        Dictionary<int, int> hist, int bs, string quality, byte[]? modes = null)
    {
        int Hc = curr.GetLength(0) / bs * bs, Wc = curr.GetLength(1) / bs * bs, gh = Hc / bs, gw = Wc / bs;
        curr = Transform.AlignToBlock(curr, bs);
        pred = Transform.AlignToBlock(pred, bs);
        var blockCount = gh * gw;
        var motionModes = modes ?? new byte[blockCount];
        var predictionModes = new byte[blockCount];
        var partitionModes = new byte[blockCount];
        var intraModes = new byte[blockCount];
        var residualModes = new byte[blockCount];
        var txQ = new short[blockCount * bs * bs];
        var recon = new float[Hc, Wc];

        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            var blockIndex = by * gw + bx;
            var x0 = bx * bs;
            var y0 = by * bs;
            var currentBlock = Transform.ExtractBlock(curr, x0, y0, bs);
            var interPrediction = Transform.ExtractBlock(pred, x0, y0, bs);
            var lambda = Quantizer.Lambda(ComputeBlockEnergy(currentBlock), quality);

            var best = EvaluateInterTransformCandidate(currentBlock, interPrediction, quality, BlockCodingMode.InterFull, bs);
            best.Cost = best.Distortion + lambda * (best.BitCost + 8);

            if (bs >= 4)
            {
                var splitInter = EvaluateInterTransformCandidate(currentBlock, interPrediction, quality, BlockCodingMode.InterSplit, bs / 2);
                splitInter.Cost = splitInter.Distortion + lambda * (splitInter.BitCost + 10);
                if (splitInter.Cost < best.Cost)
                    best = splitInter;
            }

            var intraDc = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDcFull);
            intraDc.Cost = intraDc.Distortion + lambda * (intraDc.BitCost + 7);
            if (intraDc.Cost < best.Cost)
                best = intraDc;

            var intraVertical = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraVerticalFull);
            intraVertical.Cost = intraVertical.Distortion + lambda * (intraVertical.BitCost + 7);
            if (intraVertical.Cost < best.Cost)
                best = intraVertical;

            var intraHorizontal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraHorizontalFull);
            intraHorizontal.Cost = intraHorizontal.Distortion + lambda * (intraHorizontal.BitCost + 7);
            if (intraHorizontal.Cost < best.Cost)
                best = intraHorizontal;

            var intraPlanar = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraPlanarFull);
            intraPlanar.Cost = intraPlanar.Distortion + lambda * (intraPlanar.BitCost + 7);
            if (intraPlanar.Cost < best.Cost)
                best = intraPlanar;

            var intraDiagonal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDiagonalFull);
            intraDiagonal.Cost = intraDiagonal.Distortion + lambda * (intraDiagonal.BitCost + 7);
            if (intraDiagonal.Cost < best.Cost)
                best = intraDiagonal;

            if (bs >= 4)
            {
                var splitIntraDc = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDcSplit);
                splitIntraDc.Cost = splitIntraDc.Distortion + lambda * (splitIntraDc.BitCost + 9);
                if (splitIntraDc.Cost < best.Cost)
                    best = splitIntraDc;

                var splitIntraVertical = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraVerticalSplit);
                splitIntraVertical.Cost = splitIntraVertical.Distortion + lambda * (splitIntraVertical.BitCost + 9);
                if (splitIntraVertical.Cost < best.Cost)
                    best = splitIntraVertical;

                var splitIntraHorizontal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraHorizontalSplit);
                splitIntraHorizontal.Cost = splitIntraHorizontal.Distortion + lambda * (splitIntraHorizontal.BitCost + 9);
                if (splitIntraHorizontal.Cost < best.Cost)
                    best = splitIntraHorizontal;

                var splitIntraPlanar = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraPlanarSplit);
                splitIntraPlanar.Cost = splitIntraPlanar.Distortion + lambda * (splitIntraPlanar.BitCost + 9);
                if (splitIntraPlanar.Cost < best.Cost)
                    best = splitIntraPlanar;

                var splitIntraDiagonal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDiagonalSplit);
                splitIntraDiagonal.Cost = splitIntraDiagonal.Distortion + lambda * (splitIntraDiagonal.BitCost + 9);
                if (splitIntraDiagonal.Cost < best.Cost)
                    best = splitIntraDiagonal;
            }

            if (motionModes[blockIndex] != 0)
            {
                var skip = EvaluateSkipCandidate(currentBlock, interPrediction);
                skip.Cost = skip.Distortion + lambda * (motionModes[blockIndex] is 2 or 3 ? 2 : 4);
                if (skip.Cost < best.Cost)
                {
                    best = skip;
                }
            }

            residualModes[blockIndex] = best.Mode;
            BlockSyntax.DecodeLegacyMode(best.Mode, out predictionModes[blockIndex], out partitionModes[blockIndex], out intraModes[blockIndex]);
            Array.Copy(best.Coefficients, 0, txQ, blockIndex * bs * bs, bs * bs);
            Transform.WriteBlock(recon, x0, y0, best.Reconstruction);
        }

        return new EncodedFrame
        {
            MV = new short[gh, gw, 2],
            Modes = motionModes,
            PredictionModes = predictionModes,
            PartitionModes = partitionModes,
            IntraModes = intraModes,
            ResidualModes = residualModes,
            TxQ = txQ,
            Idx = Array.Empty<ushort>(),
            GainQ = Array.Empty<short>(),
            DCq = Array.Empty<short>(),
            Shape = (Hc, Wc)
        };
    }

    private static BlockCandidate EvaluateSkipCandidate(float[,] currentBlock, float[,] prediction)
    {
        return new BlockCandidate(
            BlockCodingMode.Skip,
            new short[currentBlock.GetLength(0) * currentBlock.GetLength(1)],
            prediction,
            ComputeBlockDistortion(currentBlock, prediction),
            0);
    }

    private static BlockCandidate EvaluateInterTransformCandidate(float[,] currentBlock, float[,] prediction, string quality, byte mode, int blockSize)
    {
        if (mode == BlockCodingMode.InterFull)
            return EvaluateTransformUnit(currentBlock, prediction, quality, false, mode);

        return EvaluateSplitCandidate(currentBlock, prediction, quality, false, mode, blockSize);
    }

    private static BlockCandidate EvaluateIntraCandidate(float[,] currentBlock, float[,] reconFrame, int x0, int y0, int bs, string quality, byte mode)
    {
        var predictionMode = mode switch
        {
            BlockCodingMode.IntraDcFull or BlockCodingMode.IntraDcSplit => BlockCodingMode.IntraDcFull,
            BlockCodingMode.IntraVerticalFull or BlockCodingMode.IntraVerticalSplit => BlockCodingMode.IntraVerticalFull,
            BlockCodingMode.IntraHorizontalFull or BlockCodingMode.IntraHorizontalSplit => BlockCodingMode.IntraHorizontalFull,
            BlockCodingMode.IntraPlanarFull or BlockCodingMode.IntraPlanarSplit => BlockCodingMode.IntraPlanarFull,
            _ => BlockCodingMode.IntraDiagonalFull
        };

        var prediction = BuildIntraPrediction(reconFrame, x0, y0, bs, predictionMode);
        if (!BlockCodingMode.IsSplit(mode))
            return EvaluateTransformUnit(currentBlock, prediction, quality, true, mode);

        return EvaluateSplitCandidate(currentBlock, prediction, quality, true, mode, bs / 2);
    }

    private static BlockCandidate EvaluateSplitCandidate(float[,] currentBlock, float[,] prediction, string quality, bool intra, byte mode, int subSize)
    {
        var bs = currentBlock.GetLength(0);
        var tx = new short[bs * bs];
        var recon = new float[bs, bs];
        double distortion = 0;
        var bitCost = 0;
        var qOffset = 0;
        for (var subY = 0; subY < bs; subY += subSize)
        for (var subX = 0; subX < bs; subX += subSize)
        {
            var currentSub = ExtractSubBlock(currentBlock, subX, subY, subSize);
            var predictionSub = ExtractSubBlock(prediction, subX, subY, subSize);
            var candidate = EvaluateTransformUnit(currentSub, predictionSub, quality, intra, mode);
            Array.Copy(candidate.Coefficients, 0, tx, qOffset, candidate.Coefficients.Length);
            qOffset += candidate.Coefficients.Length;
            distortion += candidate.Distortion;
            bitCost += candidate.BitCost;
            WriteSubBlock(recon, subX, subY, candidate.Reconstruction);
        }

        return new BlockCandidate(mode, tx, recon, distortion, bitCost);
    }

    private static BlockCandidate EvaluateTransformUnit(float[,] currentBlock, float[,] prediction, string quality, bool intra, byte mode)
    {
        var bs = currentBlock.GetLength(0);
        var residual = Transform.Sub(currentBlock, prediction);
        var coeffs = Transform.ForwardHadamard(residual);
        var dequantized = new float[bs, bs];
        var q = new short[bs * bs];
        var i = 0;
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
        {
            var weight = 1f + 0.12f * (x + y);
            var step = Quantizer.TransformStep(quality, intra, bs) * weight;
            var qv = Quantizer.QuantizeTransform(coeffs[y, x], step);
            q[i++] = qv;
            dequantized[y, x] = Quantizer.DequantizeTransform(qv, step);
        }

        Quantizer.PruneTransform(q, quality, bs);
        i = 0;
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
        {
            var weight = 1f + 0.12f * (x + y);
            var step = Quantizer.TransformStep(quality, intra, bs) * weight;
            dequantized[y, x] = Quantizer.DequantizeTransform(q[i++], step);
        }

        var bitCost = Quantizer.EstimateSparseTransformBits(q, bs);

        var reconResidual = Transform.InverseHadamard(dequantized);
        var reconstruction = new float[bs, bs];
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            reconstruction[y, x] = MathUtil.Clamp01(prediction[y, x] + reconResidual[y, x]);

        var distortion = ComputeBlockDistortion(currentBlock, reconstruction);
        return new BlockCandidate(mode, q, reconstruction, distortion, bitCost);
    }

    private static float[,] BuildIntraPrediction(float[,] reconFrame, int x0, int y0, int bs, byte mode)
    {
        var prediction = new float[bs, bs];
        var topAvailable = y0 > 0;
        var leftAvailable = x0 > 0;
        var dc = 0.5f;
        if (topAvailable || leftAvailable)
        {
            double sum = 0;
            var count = 0;
            if (topAvailable)
            {
                for (var x = 0; x < bs; x++)
                {
                    sum += reconFrame[y0 - 1, x0 + x];
                    count++;
                }
            }

            if (leftAvailable)
            {
                for (var y = 0; y < bs; y++)
                {
                    sum += reconFrame[y0 + y, x0 - 1];
                    count++;
                }
            }

            dc = count > 0 ? (float)(sum / count) : 0.5f;
        }

        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
        {
            prediction[y, x] = mode switch
            {
                BlockCodingMode.IntraVerticalFull when topAvailable => reconFrame[y0 - 1, x0 + x],
                BlockCodingMode.IntraHorizontalFull when leftAvailable => reconFrame[y0 + y, x0 - 1],
                BlockCodingMode.IntraPlanarFull when topAvailable && leftAvailable => PredictPlanar(reconFrame, x0, y0, bs, x, y),
                BlockCodingMode.IntraDiagonalFull when topAvailable => reconFrame[y0 - 1, x0 + Math.Min(bs - 1, x + y)],
                BlockCodingMode.IntraDiagonalFull when leftAvailable => reconFrame[y0 + Math.Min(bs - 1, x + y), x0 - 1],
                _ => dc
            };
        }

        return prediction;
    }

    private static float PredictPlanar(float[,] reconFrame, int x0, int y0, int bs, int x, int y)
    {
        var top = reconFrame[y0 - 1, x0 + x];
        var left = reconFrame[y0 + y, x0 - 1];
        var wx = x / (float)Math.Max(1, bs - 1);
        var wy = y / (float)Math.Max(1, bs - 1);
        return ((1f - wy) * top + (1f - wx) * left) * 0.5f + (wx + wy) * 0.25f * (top + left);
    }

    private static float[,] ExtractSubBlock(float[,] block, int x0, int y0, int bs)
    {
        var sub = new float[bs, bs];
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            sub[y, x] = block[y0 + y, x0 + x];
        return sub;
    }

    private static void WriteSubBlock(float[,] block, int x0, int y0, float[,] sub)
    {
        var bs = sub.GetLength(0);
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            block[y0 + y, x0 + x] = sub[y, x];
    }

    private static double ComputeBlockEnergy(float[,] block)
    {
        double energy = 0;
        var h = block.GetLength(0);
        var w = block.GetLength(1);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            energy += block[y, x] * block[y, x];
        return energy / Math.Max(1, h * w);
    }

    private static double ComputeBlockDistortion(float[,] original, float[,] reconstructed)
    {
        double distortion = 0;
        var h = original.GetLength(0);
        var w = original.GetLength(1);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var diff = original[y, x] - reconstructed[y, x];
            distortion += diff * diff;
        }

        return distortion;
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

    private static float[] ComputeNorms(float[,] codebook, int width)
    {
        var count = codebook.GetLength(0);
        var norms = new float[count];
        for (var k = 0; k < count; k++)
        {
            double sum = 0;
            for (var j = 0; j < width; j++)
            {
                var v = codebook[k, j];
                sum += v * v;
            }

            norms[k] = (float)sum;
        }

        return norms;
    }

    private static (short[,,] MV, float[,] Pred, byte[] Modes) SelectInterModes(
        float[,] reference,
        float[,] current,
        short[,,] initialMv,
        int bs,
        int qMotion,
        string quality)
    {
        int Hc = current.GetLength(0) / bs * bs, Wc = current.GetLength(1) / bs * bs;
        int gh = Hc / bs, gw = Wc / bs;
        var refinedMv = new short[gh, gw, 2];
        var pred = new float[Hc, Wc];
        var modes = new byte[gh * gw];
        var zeroResidualThreshold = GetZeroResidualThreshold(bs, quality);

        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            var bestU = initialMv[by, bx, 0];
            var bestV = initialMv[by, bx, 1];
            var bestMode = (byte)1;
            var bestError = ComputeBlockError(reference, current, bx * bs, by * bs, bs, qMotion, bestU, bestV);

            if (bx > 0)
            {
                var leftU = refinedMv[by, bx - 1, 0];
                var leftV = refinedMv[by, bx - 1, 1];
                var leftError = ComputeBlockError(reference, current, bx * bs, by * bs, bs, qMotion, leftU, leftV);
                if (leftError < bestError)
                {
                    bestError = leftError;
                    bestU = leftU;
                    bestV = leftV;
                    bestMode = 2;
                }
            }

            if (by > 0)
            {
                var topU = refinedMv[by - 1, bx, 0];
                var topV = refinedMv[by - 1, bx, 1];
                var topError = ComputeBlockError(reference, current, bx * bs, by * bs, bs, qMotion, topU, topV);
                if (topError < bestError)
                {
                    bestError = topError;
                    bestU = topU;
                    bestV = topV;
                    bestMode = 3;
                }
            }

            refinedMv[by, bx, 0] = bestU;
            refinedMv[by, bx, 1] = bestV;

            var blockIndex = by * gw + bx;
            modes[blockIndex] = bestError <= zeroResidualThreshold ? bestMode : (byte)0;

            for (var yy = 0; yy < bs; yy++)
            for (var xx = 0; xx < bs; xx++)
            {
                var x = bx * bs + xx;
                var y = by * bs + yy;
                pred[y, x] = Motion.Sample(reference, (x << qMotion) + bestU, (y << qMotion) + bestV, qMotion);
            }
        }

        return (refinedMv, pred, modes);
    }

    private static double ComputeBlockError(float[,] reference, float[,] current, int x0, int y0, int bs, int qMotion, int u, int v)
    {
        double error = 0;
        for (var yy = 0; yy < bs; yy++)
        for (var xx = 0; xx < bs; xx++)
        {
            var x = x0 + xx;
            var y = y0 + yy;
            var predicted = Motion.Sample(reference, (x << qMotion) + u, (y << qMotion) + v, qMotion);
            var diff = current[y, x] - predicted;
            error += diff * diff;
        }

        return error;
    }

    private static double GetZeroResidualThreshold(int bs, string quality)
    {
        var mse = quality == "ultra" ? 0.00003 : quality == "fast" ? 0.00008 : 0.00005;
        return mse * bs * bs;
    }

    private static bool ShouldRefreshFrame(float[,] current, float[,] prediction, int frameIndex, int gop, string quality, int bs)
    {
        if (gop > 0 && frameIndex % gop == 0)
            return true;

        var sceneThreshold = quality == "ultra" ? 0.010 : quality == "fast" ? 0.018 : 0.013;
        var blockRatioThreshold = quality == "ultra" ? 0.30 : quality == "fast" ? 0.42 : 0.35;
        var mse = ComputeFrameMse(current, prediction);
        if (mse <= sceneThreshold)
            return false;

        return ComputeHighErrorBlockRatio(current, prediction, bs, sceneThreshold * 1.4) >= blockRatioThreshold;
    }

    private static double ComputeFrameMse(float[,] current, float[,] prediction)
    {
        var h = current.GetLength(0);
        var w = current.GetLength(1);
        double mse = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var diff = current[y, x] - prediction[y, x];
            mse += diff * diff;
        }

        return mse / Math.Max(1, h * w);
    }

    private static double ComputeHighErrorBlockRatio(float[,] current, float[,] prediction, int bs, double mseThreshold)
    {
        var h = current.GetLength(0) / bs * bs;
        var w = current.GetLength(1) / bs * bs;
        var highErrorBlocks = 0;
        var totalBlocks = Math.Max(1, (h / bs) * (w / bs));
        for (var y0 = 0; y0 < h; y0 += bs)
        for (var x0 = 0; x0 < w; x0 += bs)
        {
            double blockMse = 0;
            for (var yy = 0; yy < bs; yy++)
            for (var xx = 0; xx < bs; xx++)
            {
                var diff = current[y0 + yy, x0 + xx] - prediction[y0 + yy, x0 + xx];
                blockMse += diff * diff;
            }

            blockMse /= bs * bs;
            if (blockMse > mseThreshold)
                highErrorBlocks++;
        }

        return highErrorBlocks / (double)totalBlocks;
    }

    private sealed class BlockCandidate
    {
        public BlockCandidate(byte mode, short[] coefficients, float[,] reconstruction, double distortion, int bitCost)
        {
            Mode = mode;
            Coefficients = coefficients;
            Reconstruction = reconstruction;
            Distortion = distortion;
            BitCost = bitCost;
        }

        public byte Mode { get; }
        public short[] Coefficients { get; }
        public float[,] Reconstruction { get; }
        public double Distortion { get; }
        public int BitCost { get; }
        public double Cost { get; set; }
    }
}
