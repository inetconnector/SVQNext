// Public Domain
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SVQNext.Codec
{
    public static class VQ
    {
        public static (float[,] Codebook, float[] Mean) Build(float[][,] Y, int bs, int K, int sampleFrames, int search, int Q, string mode)
        {
            var patches=new List<float[]>();
            for (int i=0;i<sampleFrames;i++)
            {
                var (mv,p) = Motion.BlockMatch(Y[i],Y[i+1],bs,search,Q,mode);
                int Hc=p.GetLength(0), Wc=p.GetLength(1);
                var res=new float[Hc,Wc];
                for (int y=0;y<Hc;y++) for (int x=0;x<Wc;x++) res[y,x]=Y[i+1][y,x]-p[y,x];
                patches.Add(Extract(res,bs,out _));
            }
            float[] all=Concat(patches);
            var M=ToMatrix(all,bs); int N=M.GetLength(0), D=M.GetLength(1);

            var rnd=new Random(1);
            var centers=new List<int>{ rnd.Next(N) };
            double[] dist=new double[N];
            for (int kk=1; kk<K; kk++)
            {
                double sum=0;
                for (int i=0;i<N;i++)
                {
                    double d2=double.MaxValue;
                    foreach (var c in centers)
                    {
                        double d=0; for (int j=0;j<D;j++){ double df=M[i,j]-M[c,j]; d+=df*df; }
                        if (d<d2) d2=d;
                    }
                    dist[i]=d2; sum+=d2;
                }
                double r=rnd.NextDouble()*sum, acc=0; int pick=0;
                for (int i=0;i<N;i++){ acc+=dist[i]; if (acc>=r){ pick=i; break; } }
                centers.Add(pick);
            }

            var code=new float[K,D]; var mu=new float[K];
            for (int k=0;k<K;k++)
            {
                double m=0; for (int j=0;j<D;j++) m+=M[centers[k],j]; m/=D;
                mu[k]=(float)m;
                for (int j=0;j<D;j++) code[k,j]=M[centers[k],j]-(float)m;
            }

            int[] assign=new int[N];
            for (int it=0; it<8; it++)
            {
                Parallel.For(0,N,i=>{
                    int best=0; double bestv=double.MaxValue;
                    for (int k=0;k<K;k++)
                    {
                        double d2=0;
                        for (int j=0;j<D;j++)
                        { double diff=M[i,j]-(code[k,j]+mu[k]); d2+=diff*diff; }
                        if (d2<bestv){bestv=d2; best=k;}
                    }
                    assign[i]=best;
                });
                var sum=new double[K,D]; var cnt=new int[K];
                for (int i=0;i<N;i++){ int k=assign[i]; cnt[k]++; for (int j=0;j<D;j++) sum[k,j]+=M[i,j]; }
                for (int k=0;k<K;k++)
                {
                    if (cnt[k]==0) continue;
                    double m=0; for (int j=0;j<D;j++) m+=sum[k,j]; m/=D*cnt[k];
                    mu[k]=(float)m;
                    for (int j=0;j<D;j++) code[k,j]=(float)(sum[k,j]/cnt[k]-m);
                }
            }
            return (code, mu);
        }

        public static float[] Extract(float[,] img,int bs,out (int Hc,int Wc) shape)
        {
            int Hc=img.GetLength(0)/bs*bs, Wc=img.GetLength(1)/bs*bs;
            shape=(Hc,Wc); int gh=Hc/bs, gw=Wc/bs;
            var a=new float[gh*gw*bs*bs]; int p=0;
            for (int by=0;by<gh;by++) for (int bx=0;bx<gw;bx++)
                for (int yy=0;yy<bs;yy++) for (int xx=0;xx<bs;xx++)
                    a[p++]=img[by*bs+yy, bx*bs+xx];
            return a;
        }
        public static float[,] ToMatrix(float[] blocks,int bs)
        {
            int N=blocks.Length/(bs*bs); var M=new float[N,bs*bs];
            Buffer.BlockCopy(blocks,0,M,0,blocks.Length*sizeof(float)); return M;
        }
        static float[] Concat(List<float[]> lists)
        {
            int len=0; foreach(var a in lists) len+=a.Length;
            var r=new float[len]; int p=0;
            foreach(var a in lists){ Buffer.BlockCopy(a,0,r,p*sizeof(float),a.Length*sizeof(float)); p+=a.Length; }
            return r;
        }
    }
}
