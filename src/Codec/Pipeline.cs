// Public Domain

namespace SVQNext.Codec;

public static class Pipeline
{
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
            if (useHdr)
                for (var y = 0; y < Yp.GetLength(0); y++)
                for (var x = 0; x < Yp.GetLength(1); x++)
                    Yp[y, x] = ColorModel.EncodeHDR(Yp[y, x], bitdepth);
            yFrames.Add(Yp);
            cbFrames.Add(ColorModel.Subsample420(Cb));
            crFrames.Add(ColorModel.Subsample420(Cr));
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
            for (var k = 0; k < codeK; k++)
            for (var j = 0; j < D; j++)
                bw.Write(code[k, j]);
            for (var k = 0; k < codeK; k++) bw.Write(mu[k]);
            var bytes = ms.ToArray();
            chunks.Add(new Chunk(bytes, CRC32.Compute(bytes)));
        }
        // Per-frame chunks (type=1)
        for (var t = 0; t < T; t++)
        {
            var f = seq.Frames[t];
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(1); // type=FRAME
            bw.Write(f.IsB ? 1 : 0);
            bw.Write(f.RefPrev);
            bw.Write(f.RefNext);
            int gh = f.MV.GetLength(0), gw = f.MV.GetLength(1);
            bw.Write(gh);
            bw.Write(gw);
            for (var by = 0; by < gh; by++)
            for (var bx = 0; bx < gw; bx++)
            {
                bw.Write(f.MV[by, bx, 0]);
                bw.Write(f.MV[by, bx, 1]);
            }

            bw.Write(f.Idx.Length);
            foreach (var v in f.Idx) bw.Write(v);
            bw.Write(f.DCq.Length);
            foreach (var v in f.DCq) bw.Write(v);
            // chroma 4:2:0
            var cb = Cb4[t];
            var cr = Cr4[t];
            int H2 = cb.GetLength(0), W2 = cb.GetLength(1);
            var CbQ = ChromaQuant.Q(cb);
            var CrQ = ChromaQuant.Q(cr);
            bw.Write(H2);
            bw.Write(W2);
            bw.Write(CbQ.Length);
            bw.Write(CbQ);
            bw.Write(CrQ.Length);
            bw.Write(CrQ);
            var bytes = ms.ToArray();
            chunks.Add(new Chunk(bytes, CRC32.Compute(bytes)));
        }

        var vmeta = new Dictionary<string, string>
        {
            { "type", "video" }, { "w", seq.W.ToString() }, { "h", seq.H.ToString() },
            { "source_w", W.ToString() }, { "source_h", H.ToString() },
            { "bs", bs.ToString() }, { "k", K.ToString() }, { "colorspace", colorspace },
            { "bitdepth", bitdepth.ToString() }, { "hdr", useHdr ? "1" : "0" },
            { "filters", filters ? "1" : "0" }, { "search", search.ToString() },
            { "qmotion", QMotion.ToString() }, { "searchmode", searchMode },
            { "loop", filters ? "1" : "0" }, { "useb", useB ? "1" : "0" },
            { "gop", gop.ToString() }
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

        if (v.Chunks.Count == 0) throw new InvalidDataException("video track has no chunks");

        int bs, seqH, seqW;
        float[,] codebook;
        float[] mu;

        using (var cfgMs = new MemoryStream(v.Chunks[0].Data))
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
            codebook = new float[cfgK, D];
            for (var k = 0; k < cfgK; k++)
            for (var j = 0; j < D; j++)
                codebook[k, j] = cfgBr.ReadSingle();
            mu = new float[cfgK];
            for (var k = 0; k < cfgK; k++) mu[k] = cfgBr.ReadSingle();
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

        var cbUpsampled = new List<float[,]>(frameCount);
        var crUpsampled = new List<float[,]>(frameCount);

        for (var idx = 0; idx < frameCount; idx++)
        {
            using var ms = new MemoryStream(v.Chunks[idx + 1].Data);
            using var br = new BinaryReader(ms);
            var type = br.ReadInt32();
            if (type != 1) throw new InvalidDataException("unexpected chunk type");
            var frame = new EncodedFrame
            {
                IsB = br.ReadInt32() != 0,
                RefPrev = br.ReadInt32(),
                RefNext = br.ReadInt32()
            };
            var gh = br.ReadInt32();
            var gw = br.ReadInt32();
            frame.MV = new short[gh, gw, 2];
            for (var by = 0; by < gh; by++)
            for (var bx = 0; bx < gw; bx++)
            {
                frame.MV[by, bx, 0] = br.ReadInt16();
                frame.MV[by, bx, 1] = br.ReadInt16();
            }

            var nIdx = br.ReadInt32();
            frame.Idx = new ushort[nIdx];
            for (var i = 0; i < nIdx; i++) frame.Idx[i] = br.ReadUInt16();
            var nDc = br.ReadInt32();
            frame.DCq = new short[nDc];
            for (var i = 0; i < nDc; i++) frame.DCq[i] = br.ReadInt16();
            var H2 = br.ReadInt32();
            var W2 = br.ReadInt32();
            var cbLen = br.ReadInt32();
            var cbQ = br.ReadBytes(cbLen);
            var crLen = br.ReadInt32();
            var crQ = br.ReadBytes(crLen);

            frame.Shape = (seqH, seqW);
            seq.Frames[idx] = frame;

            var cb4 = ChromaQuant.DEQ(cbQ, H2, W2);
            var cr4 = ChromaQuant.DEQ(crQ, H2, W2);
            cbUpsampled.Add(ColorModel.Upsample420(cb4));
            crUpsampled.Add(ColorModel.Upsample420(cr4));
        }

        var applyFilters = !v.Meta.TryGetValue("filters", out var filterStr) || filterStr != "0";
        var Yrec = Decoder.DecodeY(seq, codebook, mu, bs, applyFilters);

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

        var rgb = new float[frameCount][,,];
        for (var t = 0; t < frameCount; t++)
            rgb[t] = ColorModel.YCbCrToRgb(Yrec[t], cbUpsampled[t], crUpsampled[t], colorspace);
        return (rgb, frameCount, seqH, seqW);
    }
}