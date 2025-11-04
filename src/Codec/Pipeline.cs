// Public Domain
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SVQNext.Codec
{
    public static class Pipeline
    {
        public static Mux EncodeToMux(float[,,][] rgb, string quality, string searchMode, int bs, int search, int QMotion, int gop, bool useB, bool filters, bool useHdr, int bitdepth, string colorspace, bool scalable, int targetKbps)
        {
            int T=rgb.Length, H=rgb[0].GetLength(0), W=rgb[0].GetLength(1);
            // Colorspace
            var Y  = new float[T][,];
            var Cb4= new float[T][,];
            var Cr4= new float[T][,];
            for (int t=0;t<T;t++)
            {
                ColorModel.RgbToYCbCr(rgb[t], out var Yp, out var Cb, out var Cr, colorspace);
                if (useHdr)
                {
                    for (int y=0;y<Yp.GetLength(0);y++)
                    for (int x=0;x<Yp.GetLength(1);x++) Yp[y,x]=ColorModel.EncodeHDR(Yp[y,x], bitdepth);
                }
                Y[t]=Yp;
                Cb4[t]=ColorModel.Subsample420(Cb);
                Cr4[t]=ColorModel.Subsample420(Cr);
            }

            int K = quality=="ultra"?512: quality=="fast"?128:256;
            var (code, mu) = VQ.Build(Y, bs, K, Math.Min(8,T-1), search, QMotion, searchMode);

            var seq = Encoder.Encode(Y, bs, search, QMotion, Flatten(code), mu, quality, searchMode, gop, useB, filters);

            
            // Build chunks: first a CONFIG chunk (type=0) with codebook/mean/bs/dims
            var chunks = new List<Chunk>();
            {
                using var ms=new MemoryStream();
                using var bw=new BinaryWriter(ms);
                bw.Write(0); // type=CONFIG
                bw.Write(bs); bw.Write(K);
                bw.Write(H); bw.Write(W);
                bw.Write(seq.H); bw.Write(seq.W); // cropped dims
                // write codebook [K,D] and mean[K]
                int D = bs*bs;
                bw.Write(K); bw.Write(D);
                for (int k=0;k<K;k++) for (int j=0;j<D;j++) bw.Write(code[k,j]);
                for (int k=0;k<K;k++) bw.Write(mu[k]);
                var bytes = ms.ToArray();
                chunks.Add(new Chunk(bytes, CRC32.Compute(bytes)));
            }
            // Per-frame chunks (type=1)
            for (int t=0;t<T;t++)
            {
                var f=seq.Frames[t];
                using var ms=new MemoryStream();
                using var bw=new BinaryWriter(ms);
                bw.Write(1); // type=FRAME
                bw.Write(f.IsB?1:0);
                bw.Write(f.RefPrev); bw.Write(f.RefNext);
                int gh=f.MV.GetLength(0), gw=f.MV.GetLength(1);
                bw.Write(gh); bw.Write(gw);
                for (int by=0;by<gh;by++)
                    for (int bx=0;bx<gw;bx++)
                    { bw.Write(f.MV[by,bx,0]); bw.Write(f.MV[by,bx,1]); }
                bw.Write(f.Idx.Length); foreach (var v in f.Idx) bw.Write(v);
                bw.Write(f.DCq.Length); foreach (var v in f.DCq) bw.Write(v);
                // chroma 4:2:0
                var CbQ = ChromaQuant.Q(ColorModel.Subsample420(new float[H,W])); // placeholder neutral
                var CrQ = ChromaQuant.Q(ColorModel.Subsample420(new float[H,W])); // placeholder neutral
                int H2 = H/2, W2=W/2;
                bw.Write(H2); bw.Write(W2);
                bw.Write(CbQ.Length); bw.Write(CbQ);
                bw.Write(CrQ.Length); bw.Write(CrQ);
                var bytes = ms.ToArray();
                chunks.Add(new Chunk(bytes, CRC32.Compute(bytes)));
            }
            var vmeta = new Dictionary<string,string>{
                {"type","video"}, {"w",W.ToString()}, {"h",H.ToString()},
                {"bs",bs.ToString()}, {"k",K.ToString()}, {"colorspace",colorspace},
                {"bitdepth",bitdepth.ToString()}, {"hdr", useHdr? "1":"0"}
            };
            var vtrack = new Track("video", vmeta, chunks);
            var mux = new Mux(new List<Track>{ vtrack });
            return mux;
        }

        public static (float[,,][] Rgb, int T, int H, int W) DecodeFromMux(Mux mux)
        {
            var v = mux.Tracks.FirstOrDefault(t=>t.Type=="video") ?? throw new InvalidDataException("no video");
            int W=int.Parse(v.Meta["w"]), H=int.Parse(v.Meta["h"]);
            int T=v.Chunks.Count;
            // For demo we reconstruct neutral chroma and just output grayscale -> RGB
            var Yrec = new float[T][,];
            for (int t=0;t<T;t++)
            {
                using var ms=new MemoryStream(v.Chunks[t].Data);
                using var br=new BinaryReader(ms);
                bool isB=br.ReadInt32()!=0; int rp=br.ReadInt32(), rn=br.ReadInt32();
                int gh=br.ReadInt32(), gw=br.ReadInt32();
                var mv=new short[gh,gw,2];
                for (int by=0;by<gh;by++) for (int bx=0;bx<gw;bx++){ mv[by,bx,0]=br.ReadInt16(); mv[by,bx,1]=br.ReadInt16(); }
                int nIdx=br.ReadInt32(); for (int i=0;i<nIdx;i++) br.ReadUInt16();
                int nDc=br.ReadInt32(); for (int i=0;i<nDc;i++) br.ReadInt16();
                // Placeholder: show a simple gradient instead of full decoder for demo path
                var Y=new float[H/8*8, W/8*8];
                for (int y=0;y<Y.GetLength(0);y++) for (int x=0;x<Y.GetLength(1);x++) Y[y,x]=(float)(y/(double)Math.Max(1,Y.GetLength(0)-1));
                Yrec[t]=Y;
            }
            var rgb = new float[T][,,];
            for (int t=0;t<T;t++)
            {
                var Cb=new float[H, W]; var Cr=new float[H, W];
                rgb[t] = ColorModel.YCbCrToRgb(Yrec[t], Cb, Cr, "bt709");
            }
            return (rgb, T, H, W);
        }

        static float[,] Flatten(float[,] cb) => cb; // helper for signature match
    }
}
