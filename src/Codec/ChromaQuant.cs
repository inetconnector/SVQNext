// Public Domain
using System;

namespace SVQNext.Codec
{
    public static class ChromaQuant
    {
        public const int CHROMA_Q = 255;
        public static byte[] Q(float[,] c)
        {
            int h=c.GetLength(0), w=c.GetLength(1);
            var arr=new byte[h*w]; int i=0;
            for (int y=0;y<h;y++) for (int x=0;x<w;x++)
            {
                double v=(c[y,x]+0.5)*CHROMA_Q;
                int iv=(int)Math.Round(v);
                if (iv<0) iv=0; if (iv>CHROMA_Q) iv=CHROMA_Q;
                arr[i++]=(byte)iv;
            }
            return arr;
        }
        public static float[,] DEQ(byte[] q, int H2, int W2)
        {
            var c=new float[H2,W2]; int i=0;
            for (int y=0;y<H2;y++) for (int x=0;x<W2;x++)
                c[y,x]=q[i++]/(float)CHROMA_Q - 0.5f;
            return c;
        }
    }
}
