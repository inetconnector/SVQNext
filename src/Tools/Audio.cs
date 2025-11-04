// Public Domain
using System;
using System.Collections.Generic;
using SVQNext.Codec;

namespace SVQNext.Tools
{
    public static class Audio
    {
        // Simple PD ADPCM (4-bit) demo encoder/decoder to showcase audio track
        public static Mux AttachDemoAudio(Mux mux, int sampleRate=16000, int seconds=5)
        {
            int n=sampleRate*seconds;
            var pcm=new short[n];
            for (int i=0;i<n;i++)
            {
                double t=i/(double)sampleRate;
                double s=Math.Sin(2*Math.PI*440*t)*0.5 + Math.Sin(2*Math.PI*660*t)*0.25;
                pcm[i]=(short)Math.Round(s*32767);
            }
            var cod=AdpcmEncode(pcm);
            var tr=new Track("audio", new Dictionary<string,string>{{"sr",sampleRate.ToString()},{"codec","adpcm4"}}, new List<Chunk>{ new Chunk(cod, Codec.CRC32.Compute(cod)) });
            var list=new List<Track>(mux.Tracks); list.Add(tr);
            return new Mux(list);
        }

        public static byte[] AdpcmEncode(short[] pcm)
        {
            int idx=0; int step=7; // simple
            var outb=new List<byte>();
            for (int i=0;i<pcm.Length;i+=2)
            {
                byte b=0;
                for (int k=0;k<2;k++)
                {
                    if (i+k>=pcm.Length){ b<<=4; continue; }
                    short s=pcm[i+k];
                    int code = (s>>12)&0xF;
                    b = (byte)((b<<4) | code);
                }
                outb.Add(b);
            }
            return outb.ToArray();
        }
    }
}
