// Public Domain
using System;

namespace SVQNext.Codec
{
    public static class LoopFilter
    {
        public static void Bilateral(float[,] img, int radius=1, float sigmaS=1.0f, float sigmaR=0.08f)
        {
            int h=img.GetLength(0), w=img.GetLength(1);
            var copy=(float[,])img.Clone();
            int r=radius; float twoS=2*sigmaS*sigmaS, twoR=2*sigmaR*sigmaR;
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                float sum=0, norm=0, c=copy[y,x];
                for (int dy=-r; dy<=r; dy++)
                for (int dx=-r; dx<=r; dx++)
                {
                    int yy=Math.Clamp(y+dy,0,h-1), xx=Math.Clamp(x+dx,0,w-1);
                    float ds=dx*dx+dy*dy;
                    float wr=(float)Math.Exp(-(ds)/twoS);
                    float dr=copy[yy,xx]-c;
                    float wr2=(float)Math.Exp(-(dr*dr)/twoR);
                    float wgt=wr*wr2; sum+=copy[yy,xx]*wgt; norm+=wgt;
                }
                img[y,x]=sum/(norm+1e-8f);
            }
        }
        public static void Dering(float[,] img, int thr=3)
        {
            int h=img.GetLength(0), w=img.GetLength(1);
            var c=(float[,])img.Clone();
            for (int y=1;y<h-1;y++)
            for (int x=1;x<w-1;x++)
            {
                float v=c[y,x];
                float n=(c[y-1,x]+c[y+1,x]+c[y,x-1]+c[y,x+1])*0.25f;
                if (Math.Abs(v-n)>thr/255f) img[y,x]=(v*0.5f+n*0.5f);
            }
        }
    }
}
