// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Decoder
{
    public static float[][,] DecodeY(EncodedSequence seq, float[,] codebook, float[] mu, int bs, string quality, bool filters)
    {
        var T = seq.T;
        var outY = new float[T][,];
        for (var t = 0; t < T; t++)
        {
            var f = seq.Frames[t];
            var pred = BuildPrediction(seq, outY, t, bs);
            var Yrec = new float[f.Shape.Hc, f.Shape.Wc];
            if (f.TxQ is { Length: > 0 } && f.ResidualModes is { Length: > 0 })
            {
                DecodeTransformFrame(f, pred, Yrec, bs, quality);
            }
            else
            {
                DecodeLegacyFrame(f, pred, Yrec, codebook, bs);
            }

            if (filters)
            {
                LoopFilter.Bilateral(Yrec);
                LoopFilter.Dering(Yrec);
            }

            outY[t] = Yrec;
        }

        return outY;
    }

    private static void DecodeTransformFrame(EncodedFrame frame, float[,] interPrediction, float[,] output, int bs, string quality)
    {
        var gh = frame.Shape.Hc / bs;
        var gw = frame.Shape.Wc / bs;
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            var blockIndex = by * gw + bx;
            var x0 = bx * bs;
            var y0 = by * bs;
            var mode = frame.ResidualModes[blockIndex];
            var txOffset = blockIndex * bs * bs;
            var tx = new short[bs * bs];
            Array.Copy(frame.TxQ, txOffset, tx, 0, tx.Length);

            float[,] block;
            if (mode == BlockCodingMode.Skip)
            {
                block = Transform.ExtractBlock(interPrediction, x0, y0, bs);
            }
            else if (mode == BlockCodingMode.InterFull)
            {
                var prediction = Transform.ExtractBlock(interPrediction, x0, y0, bs);
                block = ReconstructTransformBlock(tx, prediction, bs, quality, mode);
            }
            else if (mode == BlockCodingMode.InterSplit)
            {
                var prediction = Transform.ExtractBlock(interPrediction, x0, y0, bs);
                var subPartitionMask = frame.SubPartitionMasks is { Length: > 0 } ? frame.SubPartitionMasks[blockIndex] : (byte)0;
                block = ReconstructSplitBlock(tx, prediction, bs, quality, mode, subPartitionMask);
            }
            else if (mode == BlockCodingMode.IntraDcFull || mode == BlockCodingMode.IntraVerticalFull ||
                     mode == BlockCodingMode.IntraHorizontalFull || mode == BlockCodingMode.IntraPlanarFull ||
                     mode == BlockCodingMode.IntraDiagonalFull || mode == BlockCodingMode.IntraSmoothFull)
            {
                var prediction = BuildIntraPrediction(output, x0, y0, bs, mode);
                block = ReconstructTransformBlock(tx, prediction, bs, quality, mode);
            }
            else
            {
                var prediction = BuildIntraPrediction(output, x0, y0, bs, mode);
                var subPartitionMask = frame.SubPartitionMasks is { Length: > 0 } ? frame.SubPartitionMasks[blockIndex] : (byte)0;
                block = ReconstructSplitBlock(tx, prediction, bs, quality, mode, subPartitionMask);
            }

            Transform.WriteBlock(output, x0, y0, block);
        }
    }

    private static void DecodeLegacyFrame(EncodedFrame frame, float[,] pred, float[,] output, float[,] codebook, int bs)
    {
        int gh = frame.Shape.Hc / bs, gw = frame.Shape.Wc / bs;
        var resBlocks = new float[gh * gw, bs * bs];
        var dc = Quantizer.DEQ_DC(frame.DCq);
        var gain = frame.GainQ is { Length: > 0 } ? Quantizer.DEQ_Gain(frame.GainQ) : null;
        for (var n = 0; n < gh * gw; n++)
        {
            if (frame.Modes is { Length: > 0 } && frame.Modes[n] != 0)
                continue;

            int k = frame.Idx[n];
            var blockBase = dc[n];
            var blockGain = gain?[n] ?? 1f;
            for (var j = 0; j < bs * bs; j++)
                resBlocks[n, j] = blockGain * codebook[k, j] + blockBase;
        }

        var flat = new float[frame.Shape.Hc * frame.Shape.Wc];
        var bi = 0;
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            for (var yy = 0; yy < bs; yy++)
            for (var xx = 0; xx < bs; xx++)
                flat[(by * bs + yy) * frame.Shape.Wc + bx * bs + xx] = resBlocks[bi, yy * bs + xx];
            bi++;
        }

        for (var y = 0; y < frame.Shape.Hc; y++)
        for (var x = 0; x < frame.Shape.Wc; x++)
            output[y, x] = MathUtil.Clamp01(pred[y, x] + flat[y * frame.Shape.Wc + x]);
    }

    private static float[,] BuildPrediction(EncodedSequence seq, float[][,] outY, int frameIndex, int bs)
    {
        var frame = seq.Frames[frameIndex];
        if (frame.IsRefresh || frameIndex == 0 || frame.RefPrev < 0 || outY[frame.RefPrev] == null)
            return new float[frame.Shape.Hc, frame.Shape.Wc];

        var reference = outY[frame.RefPrev];
        return Motion.Compensate(reference, frame.MV, bs, seq.QMotion);
    }

    private static float[,] ReconstructTransformBlock(short[] q, float[,] prediction, int bs, string quality, byte mode)
    {
        var coeffs = new float[bs, bs];
        var i = 0;
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
        {
            var weight = 1f + 0.12f * (x + y);
            var step = Quantizer.TransformStep(quality, mode, bs) * weight;
            coeffs[y, x] = Quantizer.DequantizeTransform(q[i++], step);
        }

        var residual = Transform.InverseHadamard(coeffs);
        var block = new float[bs, bs];
        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
            block[y, x] = MathUtil.Clamp01(prediction[y, x] + residual[y, x]);
        return block;
    }

    private static float[,] ReconstructSplitBlock(short[] q, float[,] prediction, int bs, string quality, byte mode, byte subPartitionMask)
    {
        var subSize = bs / 2;
        var block = new float[bs, bs];
        var qOffset = 0;
        var quadrantIndex = 0;
        for (var subY = 0; subY < bs; subY += subSize)
        for (var subX = 0; subX < bs; subX += subSize)
        {
            var coeffCount = subSize * subSize;
            var subQ = new short[coeffCount];
            Array.Copy(q, qOffset, subQ, 0, coeffCount);
            qOffset += coeffCount;
            var predictionSub = ExtractSubBlock(prediction, subX, subY, subSize);
            var splitFurther = subSize >= 4 && ((subPartitionMask >> quadrantIndex) & 1) != 0;
            var reconSub = splitFurther
                ? ReconstructNestedSplitBlock(subQ, predictionSub, subSize, quality, mode)
                : ReconstructTransformBlock(subQ, predictionSub, subSize, quality, mode);
            WriteSubBlock(block, subX, subY, reconSub);
            quadrantIndex++;
        }

        return block;
    }

    private static float[,] ReconstructNestedSplitBlock(short[] q, float[,] prediction, int bs, string quality, byte mode)
    {
        var leafSize = bs / 2;
        var block = new float[bs, bs];
        var qOffset = 0;
        for (var subY = 0; subY < bs; subY += leafSize)
        for (var subX = 0; subX < bs; subX += leafSize)
        {
            var coeffCount = leafSize * leafSize;
            var subQ = new short[coeffCount];
            Array.Copy(q, qOffset, subQ, 0, coeffCount);
            qOffset += coeffCount;
            var predictionSub = ExtractSubBlock(prediction, subX, subY, leafSize);
            var reconSub = ReconstructTransformBlock(subQ, predictionSub, leafSize, quality, mode);
            WriteSubBlock(block, subX, subY, reconSub);
        }

        return block;
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

        var fullMode = mode switch
        {
            BlockCodingMode.IntraDcSplit => BlockCodingMode.IntraDcFull,
            BlockCodingMode.IntraVerticalSplit => BlockCodingMode.IntraVerticalFull,
            BlockCodingMode.IntraHorizontalSplit => BlockCodingMode.IntraHorizontalFull,
            BlockCodingMode.IntraPlanarSplit => BlockCodingMode.IntraPlanarFull,
            BlockCodingMode.IntraDiagonalSplit => BlockCodingMode.IntraDiagonalFull,
            BlockCodingMode.IntraSmoothSplit => BlockCodingMode.IntraSmoothFull,
            _ => mode
        };

        for (var y = 0; y < bs; y++)
        for (var x = 0; x < bs; x++)
        {
            prediction[y, x] = fullMode switch
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
}
