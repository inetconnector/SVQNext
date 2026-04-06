// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public static class Pipeline
{
    private const float CodebookScale = 32767f;

    public static Mux EncodeToMux(IReadOnlyList<float[,,]> rgb, string quality, string searchMode, int bs, int search,
        int QMotion, int gop, bool useB, bool filters, bool useHdr, int bitdepth, string colorspace, bool scalable,
        int targetKbps)
    {
        if (rgb == null) throw new ArgumentNullException(nameof(rgb));
        if (rgb.Count == 0) throw new ArgumentException("At least one frame is required for encoding.", nameof(rgb));

        var T = rgb.Count;
        var H = rgb[0].GetLength(0);
        var W = rgb[0].GetLength(1);
        // Colorspace
        var yFrames = new List<float[,]>(T);
        var cbFrames = new List<float[,]>(T);
        var crFrames = new List<float[,]>(T);
        for (var t = 0; t < T; t++)
        {
            var frame = rgb[t];
            if (frame.GetLength(0) != H || frame.GetLength(1) != W)
                throw new InvalidDataException("All frames must share the same dimensions.");

            ColorModel.RgbToYCbCr(frame, out var Yp, out var Cb, out var Cr, colorspace);
            var alignedY = Transform.AlignToBlock(Yp, bs);
            var alignedCb = Transform.AlignToBlock(Cb, bs);
            var alignedCr = Transform.AlignToBlock(Cr, bs);
            if (useHdr)
                for (var y = 0; y < alignedY.GetLength(0); y++)
                for (var x = 0; x < alignedY.GetLength(1); x++)
                    alignedY[y, x] = ColorModel.EncodeHDR(alignedY[y, x], bitdepth);
            yFrames.Add(alignedY);
            cbFrames.Add(ColorModel.Subsample420(alignedCb));
            crFrames.Add(ColorModel.Subsample420(alignedCr));
        }

        var Y = yFrames.ToArray();
        var Cb4 = cbFrames.ToArray();
        var Cr4 = crFrames.ToArray();

        float[][,] YForCodebook;
        if (Y.Length < 2)
        {
            YForCodebook = new float[2][,];
            YForCodebook[0] = Y[0];
            YForCodebook[1] = Y[0];
        }
        else
        {
            YForCodebook = Y;
        }

        var K = quality == "ultra" ? 512 : quality == "fast" ? 128 : 256;
        var sampleFrames = Math.Max(1, Math.Min(8, YForCodebook.Length - 1));
        var (code, mu) = VQ.Build(YForCodebook, bs, K, sampleFrames, search, QMotion, searchMode);

        var seq = Encoder.Encode(Y, bs, search, QMotion, code, mu, quality, searchMode, gop, useB, filters);


        // Build chunks: first a CONFIG chunk (type=0) with codebook/mean/bs/dims
        var chunks = new List<Chunk>();
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(0); // type=CONFIG
            bw.Write(bs);
            bw.Write(K);
            bw.Write(H);
            bw.Write(W);
            bw.Write(seq.H);
            bw.Write(seq.W); // cropped dims
            // write codebook [K,D] and mean[K]
            var codeK = code.GetLength(0);
            var D = code.GetLength(1);
            bw.Write(codeK);
            bw.Write(D);
            bw.Write(1); // quantized codebook payload
            for (var k = 0; k < codeK; k++)
            for (var j = 0; j < D; j++)
                bw.Write(QuantizeCodebookValue(code[k, j]));
            for (var k = 0; k < codeK; k++) bw.Write((short)0);
            var bytes = ms.ToArray();
            chunks.Add(CreateChunk(bytes));
        }
        // Per-frame chunks (type=1)
        float[,]? prevCbRecon = null;
        float[,]? prevCrRecon = null;
        for (var t = 0; t < T; t++)
        {
            var f = seq.Frames[t];
            var cb = Cb4[t];
            var cr = Cr4[t];
            int H2 = cb.GetLength(0), W2 = cb.GetLength(1);
            var useChromaPred = prevCbRecon != null && prevCrRecon != null && !f.IsRefresh;
            byte[] cbPayload;
            byte[] crPayload;
            float[,] cbRecon;
            float[,] crRecon;
            if (useChromaPred)
            {
                var predCb = CompensateChroma420(prevCbRecon!, f.MV, bs, QMotion);
                var predCr = CompensateChroma420(prevCrRecon!, f.MV, bs, QMotion);
                var cbResidualQ = ChromaQuant.QResidual(SubtractPlane(cb, predCb));
                var crResidualQ = ChromaQuant.QResidual(SubtractPlane(cr, predCr));
                var cbPredRecon = AddChromaClamp(predCb, ChromaQuant.DEQResidual(cbResidualQ, H2, W2));
                var crPredRecon = AddChromaClamp(predCr, ChromaQuant.DEQResidual(crResidualQ, H2, W2));
                var cbAbsQ = ChromaQuant.Q(cb);
                var crAbsQ = ChromaQuant.Q(cr);
                var cbAbsRecon = ChromaQuant.DEQ(cbAbsQ, H2, W2);
                var crAbsRecon = ChromaQuant.DEQ(crAbsQ, H2, W2);
                var predError = ComputePlaneMse(cb, cbPredRecon) + ComputePlaneMse(cr, crPredRecon);
                var absError = ComputePlaneMse(cb, cbAbsRecon) + ComputePlaneMse(cr, crAbsRecon);
                if (predError <= absError)
                {
                    useChromaPred = true;
                    cbPayload = cbResidualQ;
                    crPayload = crResidualQ;
                    cbRecon = cbPredRecon;
                    crRecon = crPredRecon;
                }
                else
                {
                    useChromaPred = false;
                    cbPayload = cbAbsQ;
                    crPayload = crAbsQ;
                    cbRecon = cbAbsRecon;
                    crRecon = crAbsRecon;
                }
            }
            else
            {
                cbPayload = ChromaQuant.Q(cb);
                crPayload = ChromaQuant.Q(cr);
                cbRecon = ChromaQuant.DEQ(cbPayload, H2, W2);
                crRecon = ChromaQuant.DEQ(crPayload, H2, W2);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(1); // type=FRAME
            var flags = (byte)0;
            if (f.IsB) flags |= 1;
            if (f.IsRefresh) flags |= 1 << 1;
            if (useChromaPred) flags |= 1 << 2;
            bw.Write(flags);
            bw.Write(f.RefPrev);
            bw.Write(f.RefNext);
            var mvBytes = f.IsRefresh ? Array.Empty<byte>() : SerializeMotionField(f.MV, f.Modes);
            bw.Write(mvBytes.Length);
            bw.Write(mvBytes);
            var modeBytes = SerializeInterModes(f.Modes);
            bw.Write(modeBytes.Length);
            bw.Write(modeBytes);
            var predictionModeBytes = SerializePredictionModes(f.PredictionModes);
            bw.Write(predictionModeBytes.Length);
            bw.Write(predictionModeBytes);
            var partitionModeBytes = SerializePartitionModes(f.PartitionModes);
            bw.Write(partitionModeBytes.Length);
            bw.Write(partitionModeBytes);
            var intraModeBytes = SerializeIntraModes(f.IntraModes);
            bw.Write(intraModeBytes.Length);
            bw.Write(intraModeBytes);
            var txBytes = SerializeTransformCoefficients(f.TxQ, bs);
            bw.Write(txBytes.Length);
            bw.Write(txBytes);
            bw.Write(H2);
            bw.Write(W2);
            bw.Write(cbPayload.Length);
            bw.Write(cbPayload);
            bw.Write(crPayload.Length);
            bw.Write(crPayload);
            var bytes = ms.ToArray();
            chunks.Add(CreateChunk(bytes));
            prevCbRecon = cbRecon;
            prevCrRecon = crRecon;
        }

        var vmeta = new Dictionary<string, string>
        {
            { "type", "video" }, { "w", seq.W.ToString() }, { "h", seq.H.ToString() },
            { "source_w", W.ToString() }, { "source_h", H.ToString() },
            { "bs", bs.ToString() }, { "k", K.ToString() }, { "colorspace", colorspace },
            { "bitdepth", bitdepth.ToString() }, { "hdr", useHdr ? "1" : "0" },
            { "filters", filters ? "1" : "0" }, { "search", search.ToString() },
            { "qmotion", QMotion.ToString() }, { "searchmode", searchMode },
            { "quality", quality },
            { "loop", filters ? "1" : "0" }, { "useb", useB ? "1" : "0" },
            { "gop", gop.ToString() }, { "vqmode", "gsvq1" }, { "intermode", "skip-merge1" }, { "chunkcodec", "deflate1" }, { "mvmode", "predvar1" }, { "chromamode", "pred420-1" }, { "residualmode", "hadamard-rdo2" }, { "predsyntax", "splitpred1" }
        };
        var vtrack = new Track("video", vmeta, chunks);
        var mux = new Mux(new List<Track> { vtrack });
        return mux;
    }

    public static (float[][,,] Rgb, int T, int H, int W) DecodeFromMux(Mux mux)
    {
        if (mux == null) throw new ArgumentNullException(nameof(mux));
        var v = mux.Tracks.FirstOrDefault(t => t.Type == "video") ?? throw new InvalidDataException("no video");
        var colorspace = v.Meta.TryGetValue("colorspace", out var cs) ? cs : "bt709";
        var useHdr = v.Meta.TryGetValue("hdr", out var hdrFlag) && hdrFlag == "1";
        var bitDepth = v.Meta.TryGetValue("bitdepth", out var depthStr) && int.TryParse(depthStr, out var parsedDepth)
            ? parsedDepth
            : 10;
        var sourceHeight = v.Meta.TryGetValue("source_h", out var sourceHStr) && int.TryParse(sourceHStr, out var parsedH)
            ? parsedH
            : 0;
        var sourceWidth = v.Meta.TryGetValue("source_w", out var sourceWStr) && int.TryParse(sourceWStr, out var parsedW)
            ? parsedW
            : 0;

        if (v.Chunks.Count == 0) throw new InvalidDataException("video track has no chunks");

        int bs, seqH, seqW;
        float[,] codebook;
        float[] mu;

        using (var cfgMs = new MemoryStream(DecodeChunk(v.Chunks[0].Data)))
        using (var cfgBr = new BinaryReader(cfgMs))
        {
            var type = cfgBr.ReadInt32();
            if (type != 0) throw new InvalidDataException("first chunk is not CONFIG");
            bs = cfgBr.ReadInt32();
            cfgBr.ReadInt32(); // K stored in metadata as well
            cfgBr.ReadInt32(); // source height
            cfgBr.ReadInt32(); // source width
            seqH = cfgBr.ReadInt32();
            seqW = cfgBr.ReadInt32();
            var cfgK = cfgBr.ReadInt32();
            var D = cfgBr.ReadInt32();
            var quantizedCodebook = cfgBr.ReadInt32() != 0;
            codebook = new float[cfgK, D];
            for (var k = 0; k < cfgK; k++)
            for (var j = 0; j < D; j++)
                codebook[k, j] = quantizedCodebook ? DequantizeCodebookValue(cfgBr.ReadInt16()) : cfgBr.ReadSingle();
            mu = new float[cfgK];
            for (var k = 0; k < cfgK; k++) mu[k] = quantizedCodebook ? cfgBr.ReadInt16() / CodebookScale : cfgBr.ReadSingle();
        }

        var frameCount = v.Chunks.Count - 1;
        if (frameCount <= 0) throw new InvalidDataException("no frame chunks present");

        var seq = new EncodedSequence
        {
            Frames = new EncodedFrame[frameCount],
            T = frameCount,
            H = seqH,
            W = seqW,
            BS = bs,
            QMotion = 0,
            Search = 0,
            GOP = 0,
            SearchMode = "diamond",
            Loop = true,
            UseB = false
        };

        if (v.Meta.TryGetValue("qmotion", out var qMotionStr) && int.TryParse(qMotionStr, out var qMotion))
            seq.QMotion = qMotion;
        if (v.Meta.TryGetValue("search", out var searchStr) && int.TryParse(searchStr, out var searchVal))
            seq.Search = searchVal;
        if (v.Meta.TryGetValue("searchmode", out var searchModeStr) && !string.IsNullOrEmpty(searchModeStr))
            seq.SearchMode = searchModeStr;
        if (v.Meta.TryGetValue("loop", out var loopStr)) seq.Loop = loopStr != "0";
        if (v.Meta.TryGetValue("useb", out var useBStr)) seq.UseB = useBStr == "1";
        if (v.Meta.TryGetValue("gop", out var gopStr) && int.TryParse(gopStr, out var gopVal)) seq.GOP = gopVal;
        var useGainShape = v.Meta.TryGetValue("vqmode", out var vqMode) && vqMode == "gsvq1";
        var residualMode = v.Meta.TryGetValue("residualmode", out var residualModeStr) ? residualModeStr : string.Empty;
        var useTransformResiduals = residualMode is "hadamard-rdo1" or "hadamard-rdo2";
        var usePackedTransformResiduals = residualMode == "hadamard-rdo2";
        var useSeparatedPredictionSyntax = v.Meta.TryGetValue("predsyntax", out var predSyntax) && predSyntax == "splitpred1";
        var quality = v.Meta.TryGetValue("quality", out var qualityStr) ? qualityStr : "medium";

        var cbUpsampled = new List<float[,]>(frameCount);
        var crUpsampled = new List<float[,]>(frameCount);
        float[,]? prevCbDecoded = null;
        float[,]? prevCrDecoded = null;

        for (var idx = 0; idx < frameCount; idx++)
        {
            using var ms = new MemoryStream(DecodeChunk(v.Chunks[idx + 1].Data));
            using var br = new BinaryReader(ms);
            var type = br.ReadInt32();
            if (type != 1) throw new InvalidDataException("unexpected chunk type");
            var frameFlags = br.ReadByte();
            var frame = new EncodedFrame
            {
                RefPrev = br.ReadInt32(),
                RefNext = br.ReadInt32(),
                IsB = (frameFlags & 1) != 0,
                IsRefresh = (frameFlags & (1 << 1)) != 0,
                UseChromaPred = (frameFlags & (1 << 2)) != 0
            };
            var gh = seqH / bs;
            var gw = seqW / bs;
            var mvLen = br.ReadInt32();
            var mvPayload = br.ReadBytes(mvLen);
            var nModes = br.ReadInt32();
            frame.Modes = DeserializeInterModes(br.ReadBytes(nModes), gh * gw);
            frame.MV = mvLen > 0 ? DeserializeMotionField(mvPayload, gh, gw, frame.Modes) : new short[gh, gw, 2];
            if (useTransformResiduals)
            {
                if (useSeparatedPredictionSyntax)
                {
                    var nPredictionModes = br.ReadInt32();
                    frame.PredictionModes = DeserializePredictionModes(br.ReadBytes(nPredictionModes), gh * gw);
                    var nPartitionModes = br.ReadInt32();
                    frame.PartitionModes = DeserializePartitionModes(br.ReadBytes(nPartitionModes), gh * gw);
                    var nIntraModes = br.ReadInt32();
                    frame.IntraModes = DeserializeIntraModes(br.ReadBytes(nIntraModes), gh * gw);
                    frame.ResidualModes = BuildResidualModes(frame.PredictionModes, frame.PartitionModes, frame.IntraModes);
                }
                else
                {
                    frame.PredictionModes = Array.Empty<byte>();
                    frame.PartitionModes = Array.Empty<byte>();
                    frame.IntraModes = Array.Empty<byte>();
                    var nResidualModes = br.ReadInt32();
                    if (usePackedTransformResiduals)
                    {
                        frame.ResidualModes = DeserializeResidualModes(br.ReadBytes(nResidualModes), gh * gw);
                    }
                    else
                    {
                        frame.ResidualModes = br.ReadBytes(nResidualModes);
                    }
                }

                var nTx = br.ReadInt32();
                if (usePackedTransformResiduals)
                {
                    frame.TxQ = DeserializeTransformCoefficients(br.ReadBytes(nTx), gh * gw * bs * bs, bs);
                }
                else
                {
                    frame.TxQ = new short[nTx];
                    for (var i = 0; i < nTx; i++) frame.TxQ[i] = br.ReadInt16();
                }

                frame.Idx = Array.Empty<ushort>();
                frame.GainQ = Array.Empty<short>();
                frame.DCq = Array.Empty<short>();
            }
            else
            {
                frame.PredictionModes = Array.Empty<byte>();
                frame.PartitionModes = Array.Empty<byte>();
                frame.IntraModes = Array.Empty<byte>();
                frame.ResidualModes = Array.Empty<byte>();
                frame.TxQ = Array.Empty<short>();
                var nIdx = br.ReadInt32();
                frame.Idx = new ushort[nIdx];
                for (var i = 0; i < nIdx; i++) frame.Idx[i] = br.ReadUInt16();
                if (useGainShape)
                {
                    var nGain = br.ReadInt32();
                    frame.GainQ = new short[nGain];
                    for (var i = 0; i < nGain; i++) frame.GainQ[i] = br.ReadInt16();
                }
                else
                {
                    frame.GainQ = Array.Empty<short>();
                }

                var nDc = br.ReadInt32();
                frame.DCq = new short[nDc];
                for (var i = 0; i < nDc; i++) frame.DCq[i] = br.ReadInt16();
            }
            var H2 = br.ReadInt32();
            var W2 = br.ReadInt32();
            var cbLen = br.ReadInt32();
            var cbQ = br.ReadBytes(cbLen);
            var crLen = br.ReadInt32();
            var crQ = br.ReadBytes(crLen);

            frame.Shape = (seqH, seqW);
            seq.Frames[idx] = frame;

            float[,] cb4;
            float[,] cr4;
            if (frame.UseChromaPred && prevCbDecoded != null && prevCrDecoded != null && !frame.IsRefresh)
            {
                var predCb = CompensateChroma420(prevCbDecoded, frame.MV, bs, seq.QMotion);
                var predCr = CompensateChroma420(prevCrDecoded, frame.MV, bs, seq.QMotion);
                cb4 = AddChromaClamp(predCb, ChromaQuant.DEQResidual(cbQ, H2, W2));
                cr4 = AddChromaClamp(predCr, ChromaQuant.DEQResidual(crQ, H2, W2));
            }
            else
            {
                cb4 = ChromaQuant.DEQ(cbQ, H2, W2);
                cr4 = ChromaQuant.DEQ(crQ, H2, W2);
            }

            prevCbDecoded = cb4;
            prevCrDecoded = cr4;
            cbUpsampled.Add(ColorModel.Upsample420(cb4));
            crUpsampled.Add(ColorModel.Upsample420(cr4));
        }

        var applyFilters = !v.Meta.TryGetValue("filters", out var filterStr) || filterStr != "0";
        var Yrec = Decoder.DecodeY(seq, codebook, mu, bs, quality, applyFilters);

        if (useHdr)
            for (var t = 0; t < Yrec.Length; t++)
            {
                var Yplane = Yrec[t];
                var h = Yplane.GetLength(0);
                var w = Yplane.GetLength(1);
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    Yplane[y, x] = ColorModel.DecodeHDR(Yplane[y, x], bitDepth);
            }

        if (sourceHeight <= 0 || sourceHeight > seqH) sourceHeight = seqH;
        if (sourceWidth <= 0 || sourceWidth > seqW) sourceWidth = seqW;

        var rgb = new float[frameCount][,,];
        for (var t = 0; t < frameCount; t++)
        {
            var full = ColorModel.YCbCrToRgb(Yrec[t], cbUpsampled[t], crUpsampled[t], colorspace);
            rgb[t] = MathUtil.CropRgb(full, sourceHeight, sourceWidth);
        }

        return (rgb, frameCount, sourceHeight, sourceWidth);
    }

    private static Chunk CreateChunk(byte[] rawPayload)
    {
        var compressed = Entropy.Encode(rawPayload);
        var useCompressed = compressed.Length + 1 < rawPayload.Length + 1;
        var finalPayload = new byte[(useCompressed ? compressed : rawPayload).Length + 1];
        finalPayload[0] = useCompressed ? (byte)1 : (byte)0;
        Buffer.BlockCopy(useCompressed ? compressed : rawPayload, 0, finalPayload, 1, finalPayload.Length - 1);
        return new Chunk(finalPayload, CRC32.Compute(finalPayload));
    }

    private static byte[] DecodeChunk(byte[] storedPayload)
    {
        if (storedPayload.Length == 0)
            throw new InvalidDataException("empty chunk payload");

        if (storedPayload[0] == 0)
        {
            var raw = new byte[storedPayload.Length - 1];
            Buffer.BlockCopy(storedPayload, 1, raw, 0, raw.Length);
            return raw;
        }

        if (storedPayload[0] == 1)
        {
            var compressed = new byte[storedPayload.Length - 1];
            Buffer.BlockCopy(storedPayload, 1, compressed, 0, compressed.Length);
            return Entropy.Decode(compressed);
        }

        throw new InvalidDataException("unknown chunk codec");
    }

    private static short QuantizeCodebookValue(float value)
    {
        var scaled = (int)Math.Round(Math.Clamp(value, -1f, 1f) * CodebookScale);
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    private static float DequantizeCodebookValue(short value)
    {
        return value / CodebookScale;
    }

    private static byte[] SerializeMotionField(short[,,] mv, byte[] modes)
    {
        using var ms = new MemoryStream();
        var gh = mv.GetLength(0);
        var gw = mv.GetLength(1);
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
        var mode = modes[by * gw + bx];
        if (mode == 2 || mode == 3)
            continue;

        for (var comp = 0; comp < 2; comp++)
        {
            var predictor = PredictMotionComponent(mv, by, bx, comp);
            var delta = mv[by, bx, comp] - predictor;
            WriteVarUInt(ms, ZigZagEncode(delta));
        }
        }

        return ms.ToArray();
    }

    private static short[,,] DeserializeMotionField(byte[] data, int gh, int gw, byte[] modes)
    {
        var mv = new short[gh, gw, 2];
        using var ms = new MemoryStream(data);
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
        var mode = modes[by * gw + bx];
        if (mode == 2 && bx > 0)
        {
            mv[by, bx, 0] = mv[by, bx - 1, 0];
            mv[by, bx, 1] = mv[by, bx - 1, 1];
            continue;
        }

        if (mode == 3 && by > 0)
        {
            mv[by, bx, 0] = mv[by - 1, bx, 0];
            mv[by, bx, 1] = mv[by - 1, bx, 1];
            continue;
        }

        for (var comp = 0; comp < 2; comp++)
        {
            var predictor = PredictMotionComponent(mv, by, bx, comp);
            var delta = ZigZagDecode(ReadVarUInt(ms));
            mv[by, bx, comp] = (short)(predictor + delta);
        }
        }

        return mv;
    }

    private static short PredictMotionComponent(short[,,] mv, int by, int bx, int comp)
    {
        var hasLeft = bx > 0;
        var hasTop = by > 0;
        var hasTopLeft = by > 0 && bx > 0;
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

    private static void WriteVarUInt(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static uint ReadVarUInt(Stream stream)
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    private static uint ZigZagEncode(int value)
    {
        return (uint)((value << 1) ^ (value >> 31));
    }

    private static int ZigZagDecode(uint value)
    {
        return (int)((value >> 1) ^ (uint)-(int)(value & 1));
    }

    private static float[,] CompensateChroma420(float[,] reference, short[,,] mv, int bs, int qMotion)
    {
        var cbs = Math.Max(1, bs / 2);
        var gh = mv.GetLength(0);
        var gw = mv.GetLength(1);
        var predicted = new float[gh * cbs, gw * cbs];
        for (var by = 0; by < gh; by++)
        for (var bx = 0; bx < gw; bx++)
        {
            var u = mv[by, bx, 0] / 2;
            var v = mv[by, bx, 1] / 2;
            for (var yy = 0; yy < cbs; yy++)
            for (var xx = 0; xx < cbs; xx++)
            {
                var x = bx * cbs + xx;
                var y = by * cbs + yy;
                predicted[y, x] = Motion.Sample(reference, (x << qMotion) + u, (y << qMotion) + v, qMotion);
            }
        }

        return predicted;
    }

    private static float[,] SubtractPlane(float[,] current, float[,] prediction)
    {
        var h = current.GetLength(0);
        var w = current.GetLength(1);
        var residual = new float[h, w];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            residual[y, x] = Math.Clamp(current[y, x] - prediction[y, x], -0.5f, 0.5f);
        return residual;
    }

    private static float[,] AddChromaClamp(float[,] prediction, float[,] residual)
    {
        var h = prediction.GetLength(0);
        var w = prediction.GetLength(1);
        var result = new float[h, w];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            result[y, x] = Math.Clamp(prediction[y, x] + residual[y, x], -0.5f, 0.5f);
        return result;
    }

    private static double ComputePlaneMse(float[,] original, float[,] reconstructed)
    {
        var h = original.GetLength(0);
        var w = original.GetLength(1);
        double mse = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var diff = original[y, x] - reconstructed[y, x];
            mse += diff * diff;
        }

        return mse / Math.Max(1, h * w);
    }

    private static byte[] SerializeResidualModes(byte[] modes)
    {
        var packed = new byte[(modes.Length + 1) / 2];
        for (var i = 0; i < modes.Length; i++)
        {
            var nibble = (byte)(modes[i] & 0x0F);
            if ((i & 1) == 0)
                packed[i / 2] = nibble;
            else
                packed[i / 2] |= (byte)(nibble << 4);
        }

        return packed;
    }

    private static byte[] SerializePredictionModes(byte[] modes)
    {
        return SerializePackedBits(modes, 2);
    }

    private static byte[] SerializePartitionModes(byte[] modes)
    {
        return SerializePackedBits(modes, 1);
    }

    private static byte[] SerializeIntraModes(byte[] modes)
    {
        return SerializePackedBits(modes, 3);
    }

    private static byte[] SerializeInterModes(byte[] modes)
    {
        var packed = new byte[(modes.Length + 3) / 4];
        for (var i = 0; i < modes.Length; i++)
        {
            var shift = (i & 3) * 2;
            packed[i / 4] |= (byte)((modes[i] & 0x03) << shift);
        }

        return packed;
    }

    private static byte[] DeserializeInterModes(byte[] packed, int expectedCount)
    {
        var modes = new byte[expectedCount];
        for (var i = 0; i < expectedCount; i++)
        {
            var shift = (i & 3) * 2;
            modes[i] = (byte)((packed[i / 4] >> shift) & 0x03);
        }

        return modes;
    }

    private static byte[] DeserializePredictionModes(byte[] packed, int expectedCount)
    {
        return DeserializePackedBits(packed, expectedCount, 2);
    }

    private static byte[] DeserializePartitionModes(byte[] packed, int expectedCount)
    {
        return DeserializePackedBits(packed, expectedCount, 1);
    }

    private static byte[] DeserializeIntraModes(byte[] packed, int expectedCount)
    {
        return DeserializePackedBits(packed, expectedCount, 3);
    }

    private static byte[] DeserializeResidualModes(byte[] packed, int expectedCount)
    {
        var modes = new byte[expectedCount];
        for (var i = 0; i < expectedCount; i++)
        {
            var value = packed[i / 2];
            modes[i] = (byte)(((i & 1) == 0) ? (value & 0x0F) : ((value >> 4) & 0x0F));
        }

        return modes;
    }

    private static byte[] SerializePackedBits(byte[] values, int bitsPerValue)
    {
        if (values.Length == 0)
            return Array.Empty<byte>();

        var totalBits = values.Length * bitsPerValue;
        var packed = new byte[(totalBits + 7) / 8];
        var bitOffset = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            for (var bit = 0; bit < bitsPerValue; bit++)
            {
                if (((value >> bit) & 1) == 0)
                    continue;

                var absoluteBit = bitOffset + bit;
                packed[absoluteBit / 8] |= (byte)(1 << (absoluteBit % 8));
            }

            bitOffset += bitsPerValue;
        }

        return packed;
    }

    private static byte[] DeserializePackedBits(byte[] packed, int expectedCount, int bitsPerValue)
    {
        var values = new byte[expectedCount];
        var bitOffset = 0;
        for (var i = 0; i < expectedCount; i++)
        {
            byte value = 0;
            for (var bit = 0; bit < bitsPerValue; bit++)
            {
                var absoluteBit = bitOffset + bit;
                if (((packed[absoluteBit / 8] >> (absoluteBit % 8)) & 1) != 0)
                    value |= (byte)(1 << bit);
            }

            values[i] = value;
            bitOffset += bitsPerValue;
        }

        return values;
    }

    private static byte[] BuildResidualModes(byte[] predictionModes, byte[] partitionModes, byte[] intraModes)
    {
        var count = predictionModes.Length;
        var modes = new byte[count];
        for (var i = 0; i < count; i++)
            modes[i] = BlockSyntax.EncodeLegacyMode(predictionModes[i], partitionModes[i], intraModes[i]);
        return modes;
    }

    private static byte[] SerializeTransformCoefficients(short[] coeffs, int bs)
    {
        if (bs > 4)
            return SerializeTransformCoefficientsLinear(coeffs);

        using var ms = new MemoryStream();
        var blockArea = bs * bs;
        var order = Quantizer.ZigZagIndices(bs);
        for (var blockOffset = 0; blockOffset < coeffs.Length; blockOffset += blockArea)
        {
            var zeroRun = 0;
            var nonZeroCount = 0;
            for (var orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                var coeff = coeffs[blockOffset + order[orderIndex]];
                if (coeff == 0)
                    continue;

                nonZeroCount++;
            }

            WriteVarUInt(ms, (uint)nonZeroCount);
            for (var orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                var coeff = coeffs[blockOffset + order[orderIndex]];
                if (coeff == 0)
                {
                    zeroRun++;
                    continue;
                }

                WriteVarUInt(ms, (uint)zeroRun);
                WriteVarUInt(ms, ZigZagEncode(coeff));
                zeroRun = 0;
            }
        }
        return ms.ToArray();
    }

    private static short[] DeserializeTransformCoefficients(byte[] data, int expectedCount, int bs)
    {
        if (bs > 4)
            return DeserializeTransformCoefficientsLinear(data, expectedCount);

        var coeffs = new short[expectedCount];
        var blockArea = bs * bs;
        var order = Quantizer.ZigZagIndices(bs);
        using var ms = new MemoryStream(data);
        for (var blockOffset = 0; blockOffset < expectedCount; blockOffset += blockArea)
        {
            var nonZeroCount = (int)ReadVarUInt(ms);
            var orderIndex = 0;
            for (var pair = 0; pair < nonZeroCount; pair++)
            {
                var zeroRun = (int)ReadVarUInt(ms);
                orderIndex += zeroRun;
                if (orderIndex >= order.Length)
                    throw new InvalidDataException("transform coefficient stream is out of range");

                coeffs[blockOffset + order[orderIndex]] = ZigZagDecodeToShort(ReadVarUInt(ms));
                orderIndex++;
            }
        }

        return coeffs;
    }

    private static byte[] SerializeTransformCoefficientsLinear(short[] coeffs)
    {
        using var ms = new MemoryStream();
        var nonZeroCount = 0;
        for (var i = 0; i < coeffs.Length; i++)
        {
            if (coeffs[i] != 0)
                nonZeroCount++;
        }

        WriteVarUInt(ms, (uint)nonZeroCount);
        var zeroRun = 0;
        for (var i = 0; i < coeffs.Length; i++)
        {
            var coeff = coeffs[i];
            if (coeff == 0)
            {
                zeroRun++;
                continue;
            }

            WriteVarUInt(ms, (uint)zeroRun);
            WriteVarUInt(ms, ZigZagEncode(coeff));
            zeroRun = 0;
        }

        return ms.ToArray();
    }

    private static short[] DeserializeTransformCoefficientsLinear(byte[] data, int expectedCount)
    {
        var coeffs = new short[expectedCount];
        using var ms = new MemoryStream(data);
        var nonZeroCount = (int)ReadVarUInt(ms);
        var index = 0;
        for (var pair = 0; pair < nonZeroCount; pair++)
        {
            var zeroRun = (int)ReadVarUInt(ms);
            index += zeroRun;
            if (index >= expectedCount)
                throw new InvalidDataException("linear transform coefficient stream is out of range");

            coeffs[index++] = ZigZagDecodeToShort(ReadVarUInt(ms));
        }

        return coeffs;
    }

    private static uint ZigZagEncode(short value)
    {
        var v = value;
        return (uint)((v << 1) ^ (v >> 15));
    }

    private static short ZigZagDecodeToShort(uint value)
    {
        return (short)((value >> 1) ^ (uint)-(int)(value & 1));
    }
}
