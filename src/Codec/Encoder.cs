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
            var bestReference = FindBestReference(Y, t, bs, search, QMotion, searchMode, quality);
            if (ShouldRefreshFrame(Y[t], bestReference.Prediction, t, gop, quality, bs))
            {
                hist.Clear();
                frames[t] = EncodeFrame(Y[t], new float[shape.Hc, shape.Wc], codebook, meanVec, hist, bs, quality);
                frames[t].IsRefresh = true;
                frames[t].RefPrev = -1;
                continue;
            }

            frames[t] = EncodeFrame(Y[t], bestReference.Prediction, codebook, meanVec, hist, bs, quality, bestReference.MotionModes);
            frames[t].MV = bestReference.MotionVectors;
            frames[t].Modes = bestReference.MotionModes;
            frames[t].RefPrev = bestReference.ReferenceIndex;
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
        var subPartitionMasks = new byte[blockCount];
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

            var best = EvaluateInterTransformCandidate(currentBlock, interPrediction, quality, BlockCodingMode.InterFull, bs, lambda);
            best.Cost = best.Distortion + lambda * (best.BitCost + 8);

            if (bs >= 4)
            {
                var splitInter = EvaluateInterTransformCandidate(currentBlock, interPrediction, quality, BlockCodingMode.InterSplit, bs / 2, lambda);
                splitInter.Cost = splitInter.Distortion + lambda * (splitInter.BitCost + 10);
                if (splitInter.Cost < best.Cost)
                    best = splitInter;
            }

            var intraDc = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDcFull, lambda);
            intraDc.Cost = intraDc.Distortion + lambda * (intraDc.BitCost + 7);
            if (intraDc.Cost < best.Cost)
                best = intraDc;

            var intraVertical = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraVerticalFull, lambda);
            intraVertical.Cost = intraVertical.Distortion + lambda * (intraVertical.BitCost + 7);
            if (intraVertical.Cost < best.Cost)
                best = intraVertical;

            var intraHorizontal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraHorizontalFull, lambda);
            intraHorizontal.Cost = intraHorizontal.Distortion + lambda * (intraHorizontal.BitCost + 7);
            if (intraHorizontal.Cost < best.Cost)
                best = intraHorizontal;

            var intraPlanar = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraPlanarFull, lambda);
            intraPlanar.Cost = intraPlanar.Distortion + lambda * (intraPlanar.BitCost + 7);
            if (intraPlanar.Cost < best.Cost)
                best = intraPlanar;

            var intraDiagonal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDiagonalFull, lambda);
            intraDiagonal.Cost = intraDiagonal.Distortion + lambda * (intraDiagonal.BitCost + 7);
            if (intraDiagonal.Cost < best.Cost)
                best = intraDiagonal;

            var intraSmooth = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraSmoothFull, lambda);
            intraSmooth.Cost = intraSmooth.Distortion + lambda * (intraSmooth.BitCost + 7);
            if (intraSmooth.Cost < best.Cost)
                best = intraSmooth;

            if (bs >= 4)
            {
                var splitIntraDc = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDcSplit, lambda);
                splitIntraDc.Cost = splitIntraDc.Distortion + lambda * (splitIntraDc.BitCost + 9);
                if (splitIntraDc.Cost < best.Cost)
                    best = splitIntraDc;

                var splitIntraVertical = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraVerticalSplit, lambda);
                splitIntraVertical.Cost = splitIntraVertical.Distortion + lambda * (splitIntraVertical.BitCost + 9);
                if (splitIntraVertical.Cost < best.Cost)
                    best = splitIntraVertical;

                var splitIntraHorizontal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraHorizontalSplit, lambda);
                splitIntraHorizontal.Cost = splitIntraHorizontal.Distortion + lambda * (splitIntraHorizontal.BitCost + 9);
                if (splitIntraHorizontal.Cost < best.Cost)
                    best = splitIntraHorizontal;

                var splitIntraPlanar = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraPlanarSplit, lambda);
                splitIntraPlanar.Cost = splitIntraPlanar.Distortion + lambda * (splitIntraPlanar.BitCost + 9);
                if (splitIntraPlanar.Cost < best.Cost)
                    best = splitIntraPlanar;

                var splitIntraDiagonal = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraDiagonalSplit, lambda);
                splitIntraDiagonal.Cost = splitIntraDiagonal.Distortion + lambda * (splitIntraDiagonal.BitCost + 9);
                if (splitIntraDiagonal.Cost < best.Cost)
                    best = splitIntraDiagonal;

                var splitIntraSmooth = EvaluateIntraCandidate(currentBlock, recon, x0, y0, bs, quality, BlockCodingMode.IntraSmoothSplit, lambda);
                splitIntraSmooth.Cost = splitIntraSmooth.Distortion + lambda * (splitIntraSmooth.BitCost + 9);
                if (splitIntraSmooth.Cost < best.Cost)
                    best = splitIntraSmooth;
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
            subPartitionMasks[blockIndex] = best.SubPartitionMask;
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
            SubPartitionMasks = subPartitionMasks,
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

    private static BlockCandidate EvaluateInterTransformCandidate(float[,] currentBlock, float[,] prediction, string quality, byte mode, int blockSize, double lambda)
    {
        if (mode == BlockCodingMode.InterFull)
            return EvaluateTransformUnit(currentBlock, prediction, quality, mode);

        return EvaluateSplitCandidate(currentBlock, prediction, quality, mode, blockSize, lambda);
    }

    private static BlockCandidate EvaluateIntraCandidate(float[,] currentBlock, float[,] reconFrame, int x0, int y0, int bs, string quality, byte mode, double lambda)
    {
        var predictionMode = mode switch
        {
            BlockCodingMode.IntraDcFull or BlockCodingMode.IntraDcSplit => BlockCodingMode.IntraDcFull,
            BlockCodingMode.IntraVerticalFull or BlockCodingMode.IntraVerticalSplit => BlockCodingMode.IntraVerticalFull,
            BlockCodingMode.IntraHorizontalFull or BlockCodingMode.IntraHorizontalSplit => BlockCodingMode.IntraHorizontalFull,
            BlockCodingMode.IntraPlanarFull or BlockCodingMode.IntraPlanarSplit => BlockCodingMode.IntraPlanarFull,
            BlockCodingMode.IntraDiagonalFull or BlockCodingMode.IntraDiagonalSplit => BlockCodingMode.IntraDiagonalFull,
            _ => BlockCodingMode.IntraSmoothFull
        };

        var prediction = BuildIntraPrediction(reconFrame, x0, y0, bs, predictionMode);
        if (!BlockCodingMode.IsSplit(mode))
            return EvaluateTransformUnit(currentBlock, prediction, quality, mode);

        return EvaluateSplitCandidate(currentBlock, prediction, quality, mode, bs / 2, lambda);
    }

    private static BlockCandidate EvaluateSplitCandidate(float[,] currentBlock, float[,] prediction, string quality, byte mode, int subSize, double lambda)
    {
        var bs = currentBlock.GetLength(0);
        var tx = new short[bs * bs];
        var recon = new float[bs, bs];
        double distortion = 0;
        var bitCost = 0;
        var qOffset = 0;
        byte subPartitionMask = 0;
        var quadrantIndex = 0;
        for (var subY = 0; subY < bs; subY += subSize)
        for (var subX = 0; subX < bs; subX += subSize)
        {
            var currentSub = ExtractSubBlock(currentBlock, subX, subY, subSize);
            var predictionSub = ExtractSubBlock(prediction, subX, subY, subSize);
            var candidate = EvaluateTransformUnit(currentSub, predictionSub, quality, mode);
            if (subSize >= 4)
            {
                var nestedCandidate = EvaluateNestedSplitCandidate(currentSub, predictionSub, quality, mode, subSize / 2);
                var directScore = candidate.Distortion + lambda * candidate.BitCost;
                var nestedScore = nestedCandidate.Distortion + lambda * (nestedCandidate.BitCost + 3);
                if (nestedScore < directScore)
                {
                    candidate = nestedCandidate;
                    subPartitionMask |= (byte)(1 << quadrantIndex);
                }
            }

            Array.Copy(candidate.Coefficients, 0, tx, qOffset, candidate.Coefficients.Length);
            qOffset += candidate.Coefficients.Length;
            distortion += candidate.Distortion;
            bitCost += candidate.BitCost;
            WriteSubBlock(recon, subX, subY, candidate.Reconstruction);
            quadrantIndex++;
        }

        return new BlockCandidate(mode, tx, recon, distortion, bitCost, subPartitionMask);
    }

    private static BlockCandidate EvaluateNestedSplitCandidate(float[,] currentBlock, float[,] prediction, string quality, byte mode, int leafSize)
    {
        var bs = currentBlock.GetLength(0);
        var tx = new short[bs * bs];
        var recon = new float[bs, bs];
        double distortion = 0;
        var bitCost = 0;
        var qOffset = 0;
        for (var subY = 0; subY < bs; subY += leafSize)
        for (var subX = 0; subX < bs; subX += leafSize)
        {
            var currentLeaf = ExtractSubBlock(currentBlock, subX, subY, leafSize);
            var predictionLeaf = ExtractSubBlock(prediction, subX, subY, leafSize);
            var candidate = EvaluateTransformUnit(currentLeaf, predictionLeaf, quality, mode);
            Array.Copy(candidate.Coefficients, 0, tx, qOffset, candidate.Coefficients.Length);
            qOffset += candidate.Coefficients.Length;
            distortion += candidate.Distortion;
            bitCost += candidate.BitCost;
            WriteSubBlock(recon, subX, subY, candidate.Reconstruction);
        }

        return new BlockCandidate(mode, tx, recon, distortion, bitCost);
    }

    private static BlockCandidate EvaluateTransformUnit(float[,] currentBlock, float[,] prediction, string quality, byte mode)
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
            var step = Quantizer.TransformStep(quality, mode, bs) * weight;
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
            var step = Quantizer.TransformStep(quality, mode, bs) * weight;
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
                BlockCodingMode.IntraSmoothFull => PredictSmooth(reconFrame, x0, y0, bs, x, y, topAvailable, leftAvailable, dc),
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

    private static float PredictSmooth(float[,] reconFrame, int x0, int y0, int bs, int x, int y, bool topAvailable, bool leftAvailable, float dc)
    {
        var top = topAvailable ? reconFrame[y0 - 1, x0 + x] : dc;
        var left = leftAvailable ? reconFrame[y0 + y, x0 - 1] : dc;
        var blendX = x / (float)Math.Max(1, bs - 1);
        var blendY = y / (float)Math.Max(1, bs - 1);
        var edge = ((1f - blendY) * top + (1f - blendX) * left) * 0.5f;
        var interior = dc * (0.65f + 0.35f * (blendX + blendY) * 0.5f);
        return 0.6f * edge + 0.4f * interior;
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

    private static ReferenceCandidate FindBestReference(float[][,] frames, int frameIndex, int bs, int search, int qMotion, string searchMode, string quality)
    {
        var startRef = Math.Max(0, frameIndex - 3);
        ReferenceCandidate? best = null;
        for (var refIndex = frameIndex - 1; refIndex >= startRef; refIndex--)
        {
            var adjustedSearch = refIndex == frameIndex - 1 ? search : Math.Max(1, search - 1);
            var (mv, pred) = Motion.BlockMatch(frames[refIndex], frames[frameIndex], bs, adjustedSearch, qMotion, searchMode);
            var (refinedMv, refinedPred, modes) = SelectInterModes(frames[refIndex], frames[frameIndex], mv, bs, qMotion, quality);
            var mse = ComputeFrameMse(frames[frameIndex], refinedPred);
            var bitCost = EstimateMotionBitCost(refinedMv, modes);
            var agePenalty = (frameIndex - refIndex - 1) * 10;
            var score = mse + 0.00001 * (bitCost + agePenalty);
            var candidate = new ReferenceCandidate(refIndex, refinedMv, refinedPred, modes, mse, bitCost, score);
            if (best == null || candidate.Score < best.Score)
                best = candidate;
        }

        return best ?? throw new InvalidOperationException("No reference candidate available.");
    }

    private static int EstimateMotionBitCost(short[,,] mv, byte[] modes)
    {
        var gh = mv.GetLength(0);
        var gw = mv.GetLength(1);
        var bits = 0;
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            var mode = modes[by * gw + bx];
            if (mode is 2 or 3)
            {
                bits += 1;
                continue;
            }

            bits += 3;
            for (var comp = 0; comp < 2; comp++)
            {
                var predictor = PredictMotionComponent(mv, by, bx, comp);
                var delta = (short)(mv[by, bx, comp] - predictor);
                bits += Quantizer.EstimateVarUIntBits(Quantizer.ZigZagEncode(delta));
            }
        }

        return bits;
    }

    private static short PredictMotionComponent(short[,,] mv, int by, int bx, int comp)
    {
        var hasLeft = bx > 0;
        var hasTop = by > 0;
        var hasTopLeft = hasLeft && hasTop;
        if (hasLeft && hasTop && hasTopLeft)
        {
            var left = mv[by, bx - 1, comp];
            var top = mv[by - 1, bx, comp];
            var topLeft = mv[by - 1, bx - 1, comp];
            return Median(left, top, (short)(left + top - topLeft));
        }

        if (hasLeft) return mv[by, bx - 1, comp];
        if (hasTop) return mv[by - 1, bx, comp];
        return 0;
    }

    private static short Median(short a, short b, short c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return b;
    }

    private sealed class BlockCandidate
    {
        public BlockCandidate(byte mode, short[] coefficients, float[,] reconstruction, double distortion, int bitCost, byte subPartitionMask = 0)
        {
            Mode = mode;
            Coefficients = coefficients;
            Reconstruction = reconstruction;
            Distortion = distortion;
            BitCost = bitCost;
            SubPartitionMask = subPartitionMask;
        }

        public byte Mode { get; }
        public short[] Coefficients { get; }
        public float[,] Reconstruction { get; }
        public double Distortion { get; }
        public int BitCost { get; }
        public byte SubPartitionMask { get; }
        public double Cost { get; set; }
    }

    private sealed class ReferenceCandidate
    {
        public ReferenceCandidate(int referenceIndex, short[,,] motionVectors, float[,] prediction, byte[] motionModes, double mse, int bitCost, double score)
        {
            ReferenceIndex = referenceIndex;
            MotionVectors = motionVectors;
            Prediction = prediction;
            MotionModes = motionModes;
            Mse = mse;
            BitCost = bitCost;
            Score = score;
        }

        public int ReferenceIndex { get; }
        public short[,,] MotionVectors { get; }
        public float[,] Prediction { get; }
        public byte[] MotionModes { get; }
        public double Mse { get; }
        public int BitCost { get; }
        public double Score { get; }
    }
}
