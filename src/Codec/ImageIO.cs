// Public Domain
using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;.Imaging;
using System.IO;

namespace SVQNext.Codec
{
    public static class ImageIO
    {
        public static float[,,] ToFloatRgb(Bitmap bmp)
        {
            int h=bmp.Height, w=bmp.Width;
            var a=new float[h,w,3];
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                var c=bmp.GetPixel(x,y);
                a[y,x,0]=c.R/255f; a[y,x,1]=c.G/255f; a[y,x,2]=c.B/255f;
            }
            return a;
        }

        public static Bitmap ToBitmap(float[,,] rgb)
        {
            int h=rgb.GetLength(0), w=rgb.GetLength(1);
            var bmp=SVQNext.Compat.ImageSharpCompat.NewBitmap(w,h);
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                int r=MathUtil.Clamp01To255(rgb[y,x,0]);
                int g=MathUtil.Clamp01To255(rgb[y,x,1]);
                int b=MathUtil.Clamp01To255(rgb[y,x,2]);
                bmp.SetPixel(x,y, Color.FromArgb(r,g,b));
            }
            return bmp;
        }

        public static void SaveGif(List<Bitmap> frames, string path)
        {
            var encoder = GetEncoder(null /* format inferred by extension */);
            using var first = frames[0];
            var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
            first.Save(path);
            encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
            for (int i=1;i<frames.Count;i++)
            {
                using var fr = frames[i];
                first.SaveAdd(fr, encParams);
            }
            encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.Flush);
            first.SaveAdd(encParams);
        }

        static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach(var c in codecs) if (c.FormatID==format.Guid) return c;
            return null!;
        }

        public static List<Bitmap> GenerateDemo(int T, int H, int W)
        {
            var frames=new List<Bitmap>();
            for (int t=0;t<T;t++)
            {
                Bitmap bmp=SVQNext.Compat.ImageSharpCompat.NewBitmap(W,H);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    for (int y=0;y<H;y++)
                    for (int x=0;x<W;x++)
                    {
                        int r=(x*2+t*3)%256;
                        int gc=(y*2+t*3)%256;
                        int b=((x+y)+t*3)%256;
                        bmp.SetPixel(x,y, Color.FromArgb(r,gc,b));
                    }
                    int cx=(int)((W/2)+(W/3)*Math.Sin(2*Math.PI*t/60.0));
                    int cy=(int)((H/2)+(H/4)*Math.Cos(2*Math.PI*t/60.0));
                    int rc=12;
                    g.FillEllipse(Brushes.White, cx-rc, cy-rc, rc*2, rc*2);
                }
                frames.Add(bmp);
            }
            return frames;
        }
    }
}
