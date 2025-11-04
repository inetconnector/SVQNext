// Public Domain
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SVQNext.Codec
{
    public static class Encoder
    {
        public static EncodedSequence Encode(float[][,] Y, int bs, int search, int QMotion, float[,] codebook, float[] meanVec, string quality, string searchMode, int gop, bool useB, bool loop)
        {
            int T=Y.Length;
            var frames=new EncodedFrame[T];
            (int Hc,int Wc) shape=(Y[0].GetLength(0)/bs*bs, Y[0].GetLength(1)/bs*bs);
            var hist=new Dictionary<int,int>();

            // I-frame 0
            frames[0]=EncodeFrame(Y[0], new float[shape.Hc,shape.Wc], codebook, meanVec, hist, bs, quality);

            for (int t=1;t<T;t++)
            {
                var (mv,pred)=Motion.BlockMatch(Y[t-1], Y[t], bs, search, QMotion, searchMode);
                frames[t]=EncodeFrame(Y[t], pred, codebook, meanVec, hist, bs, quality);
                frames[t].MV = mv;
                frames[t].RefPrev = t-1;
            }
            return new EncodedSequence{ Frames=frames, T=T, H=shape.Hc, W=shape.Wc, BS=bs, QMotion=QMotion, Search=search, GOP=gop, SearchMode=searchMode, Loop=loop, UseB=useB };
        }

        static EncodedFrame EncodeFrame(float[,] curr, float[,] pred, float[,] codebook, float[] mu, Dictionary<int,int> hist, int bs, string quality)
        {
            int Hc=curr.GetLength(0)/bs*bs, Wc=curr.GetLength(1)/bs*bs, gh=Hc/bs, gw=Wc/bs;
            var res=Transform.Sub(curr, pred);
            var blocks=VQ.Extract(res, bs, out _);
            var centered=Center(blocks, bs, out var dc);
            int N=centered.GetLength(0), D=centered.GetLength(1), K=codebook.GetLength(0);
            var (bits,worst)=EstimateBits(hist,K);
            var idx=new ushort[N];
            Parallel.For(0,N,n=>{
                double best=double.MaxValue; int bestK=0;
                // energy proxy
                double energy=0; for (int j=0;j<D;j++){ double d=centered[n,j]; energy+=d*d; }
                double lam=Quantizer.Lambda(energy, quality);
                for (int k=0;k<K;k++)
                {
                    double d2=0;
                    for (int j=0;j<D;j++){ double diff=centered[n,j]-codebook[k,j]; d2+=diff*diff; }
                    double cost=d2 + lam*(bits[k]>0?bits[k]:worst);
                    if (cost<best){best=cost; bestK=k;}
                }
                idx[n]=(ushort)bestK;
            });
            foreach (var i in idx){ if (!hist.ContainsKey(i)) hist[i]=0; hist[i]++; }
            var dcq=Quantizer.Q_DC(dc);
            return new EncodedFrame{ MV=new short[gh,gw,2], Idx=idx, DCq=dcq, Shape=(Hc,Wc) };
        }

        static float[,] Center(float[] blocks,int bs,out float[] dc)
        {
            int N=blocks.Length/(bs*bs);
            var M=new float[N,bs*bs];
            dc=new float[N]; int p=0;
            for (int n=0;n<N;n++)
            {
                double sum=0; for (int i=0;i<bs*bs;i++) sum+=blocks[p+i];
                float m=(float)(sum/(bs*bs)); dc[n]=m;
                for (int i=0;i<bs*bs;i++) M[n,i]=blocks[p+i]-m;
                p+=bs*bs;
            }
            return M;
        }

        static (float[] bits, float worst) EstimateBits(Dictionary<int,int> hist,int K)
        {
            var bits=new float[K]; if (hist.Count==0) return (bits,16f);
            double total=hist.Values.Sum()+1e-9; float worst=16f;
            foreach (var kv in hist)
            {
                int k=kv.Key; if (k<0||k>=K) continue;
                double p=Math.Max(kv.Value/total, 1e-12);
                bits[k]=(float)(-Math.Log(p,2)); if (bits[k]>worst) worst=bits[k];
            }
            return (bits,worst);
        }
    }
}
