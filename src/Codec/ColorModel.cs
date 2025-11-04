// Public Domain
using System;

namespace SVQNext.Codec
{
    public static class ColorModel
    {
        // Public-domain constants for BT.709 and BT.2020
        public static readonly (double Kr,double Kb) BT709 = (0.2126, 0.0722);
        public static readonly (double Kr,double Kb) BT2020 = (0.2627, 0.0593);

        public static (double Kr,double Kb) GetPrimaries(string name) =>
            name.ToLower() switch
            {
                "bt2020" => BT2020,
                _ => BT709
            };

        public static void RgbToYCbCr(float[,,] rgb, out float[,] Y, out float[,] Cb, out float[,] Cr, string cs="bt709")
        {
            var (Kr,Kb)=GetPrimaries(cs);
            int h=rgb.GetLength(0), w=rgb.GetLength(1);
            Y=new float[h,w]; Cb=new float[h,w]; Cr=new float[h,w];
            double Kg=1.0-Kr-Kb;
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                double R=rgb[y,x,0], G=rgb[y,x,1], B=rgb[y,x,2];
                double Yp=Kr*R + Kg*G + Kb*B;
                double Cbp=(B-Yp)/(2*(1-Kb));
                double Crp=(R-Yp)/(2*(1-Kr));
                Y[y,x]=(float)Yp; Cb[y,x]=(float)Cbp; Cr[y,x]=(float)Crp;
            }
        }

        public static float[,,] YCbCrToRgb(float[,] Y, float[,] Cb, float[,] Cr, string cs="bt709")
        {
            var (Kr,Kb)=GetPrimaries(cs);
            int h=Y.GetLength(0), w=Y.GetLength(1);
            var rgb=new float[h,w,3];
            double Kg=1.0-Kr-Kb;
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                double Yp=Y[y,x], Cbp=Cb[y,x], Crp=Cr[y,x];
                double R=Yp + 2*(1-Kr)*Crp;
                double B=Yp + 2*(1-Kb)*Cbp;
                double G=(Yp - Kr*R - Kb*B)/Kg;
                rgb[y,x,0]=(float)MathUtil.Clamp01((float)R);
                rgb[y,x,1]=(float)MathUtil.Clamp01((float)G);
                rgb[y,x,2]=(float)MathUtil.Clamp01((float)B);
            }
            return rgb;
        }

        public static float[,] Subsample420(float[,] c)
        {
            int h=c.GetLength(0), w=c.GetLength(1);
            int H2=h/2, W2=w/2;
            var outp=new float[H2,W2];
            for (int y=0;y<H2;y++)
            for (int x=0;x<W2;x++)
            {
                int yy=y*2, xx=x*2;
                float sum=c[yy,xx]+c[yy+1,xx]+c[yy,xx+1]+c[yy+1,xx+1];
                outp[y,x]=sum*0.25f;
            }
            return outp;
        }
        public static float[,] Upsample420(float[,] c4)
        {
            int H2=c4.GetLength(0), W2=c4.GetLength(1);
            var c=new float[H2*2,W2*2];
            for (int y=0;y<H2;y++)
            for (int x=0;x<W2;x++)
            {
                float v=c4[y,x];
                c[2*y,2*x]=v; c[2*y+1,2*x]=v; c[2*y,2*x+1]=v; c[2*y+1,2*x+1]=v;
            }
            return c;
        }

        // Simple open HDR transfer (no PQ/HLG): piecewise gamma that maps 0..1 -> 0..1 with extended headroom.
        public static float EncodeHDR(float v, int bitdepth)
        {
            // map linear [0..1] to perceptual-ish curve
            double a = 0.17883277; // open constant (approx for display gamma compression)
            double b = 1 - 4*a;
            double y = v<0.5 ? Math.Pow(v, 1/2.2) : (a*Math.Log(1+ (v-0.5)*10)+0.5*b);
            // quantize to bitdepth range later; keep float here
            return (float)MathUtil.Clamp01((float)y);
        }
        public static float DecodeHDR(float v, int bitdepth)
        {
            // inverse of above (approx)
            double a = 0.17883277;
            double b = 1 - 4*a;
            double lin;
            if (v<0.5) lin = Math.Pow(v, 2.2);
            else       lin = 0.5 + (Math.Exp((v-0.5)/a) - 1.0)/10.0;
            return (float)MathUtil.Clamp01((float)lin);
        }
    }
}
