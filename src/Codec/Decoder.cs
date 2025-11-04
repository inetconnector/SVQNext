// Public Domain
using System;

namespace SVQNext.Codec
{
    public static class Decoder
    {
        public static float[][,] DecodeY(EncodedSequence seq, float[,] codebook, float[] mu, int bs, bool filters)
        {
            int T=seq.T; var outY=new float[T][,];
            for (int t=0;t<T;t++)
            {
                var f=seq.Frames[t];
                var pred = t==0 ? new float[f.Shape.Hc,f.Shape.Wc] : outY[t-1];
                int gh=f.Shape.Hc/bs, gw=f.Shape.Wc/bs;
                var resBlocks=new float[gh*gw, bs*bs];
                for (int n=0;n<gh*gw;n++)
                {
                    int k=f.Idx[n]; float meanK=mu[k];
                    for (int j=0;j<bs*bs;j++) resBlocks[n,j]=codebook[k,j]+meanK + f.DCq[n]/(float)Quantizer.DC_SCALE;
                }
                var flat=new float[f.Shape.Hc*f.Shape.Wc];
                int bi=0;
                for (int by=0;by<gh;by++)
                for (int bx=0;bx<gw;bx++)
                {
                    for (int yy=0;yy<bs;yy++)
                    for (int xx=0;xx<bs;xx++)
                        flat[(by*bs+yy)*f.Shape.Wc + (bx*bs+xx)] = resBlocks[bi, yy*bs+xx];
                    bi++;
                }
                var Yrec=new float[f.Shape.Hc,f.Shape.Wc];
                for (int y=0;y<f.Shape.Hc;y++)
                for (int x=0;x<f.Shape.Wc;x++)
                    Yrec[y,x]=MathUtil.Clamp01(pred[y,x]+flat[y*f.Shape.Wc+x]);
                if (filters){ LoopFilter.Bilateral(Yrec,1,1.0f,0.08f); LoopFilter.Dering(Yrec,3); }
                outY[t]=Yrec;
            }
            return outY;
        }
    }
}
