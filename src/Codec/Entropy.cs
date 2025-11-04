// Public Domain
using System;
using System.Collections.Generic;

namespace SVQNext.Codec
{
    public static class Entropy
    {
        const uint RANS_L = 1<<23;
        public static byte[] Encode(byte[] data)
        {
            uint[] f = new uint[256];
            for (int i=0;i<256;i++) f[i]=1;
            uint tot=256, x=RANS_L;
            var stack=new List<uint>();
            foreach (var s in data)
            {
                uint fs=f[s];
                while (x >= ((RANS_L)/tot) * fs) { stack.Add(x & 0xFF); x >>= 8; }
                uint cum=0; for (int a=0;a<s;a++) cum+=f[a];
                x = (x/fs)*tot + (x%fs) + cum;
                f[s]++;
                tot++;
                if (tot > (1<<15))
                {
                    tot=0;
                    for (int i=0;i<256;i++)
                    {
                        f[i]=(f[i]+1)>>1;
                        tot+=f[i];
                    }
                }
            }
            var outb=new List<byte>();
            void add4(uint v){ outb.Add((byte)(v)); outb.Add((byte)(v>>8)); outb.Add((byte)(v>>16)); outb.Add((byte)(v>>24)); }
            add4((uint)data.Length); add4(x);
            for (int i=stack.Count-1;i>=0;i--) outb.Add((byte)(stack[i]&0xFF));
            return outb.ToArray();
        }
        public static byte[] Decode(byte[] cod)
        {
            int p=0;
            uint len = (uint)(cod[p] | (cod[p+1]<<8) | (cod[p+2]<<16) | (cod[p+3]<<24)); p+=4;
            uint x   = (uint)(cod[p] | (cod[p+1]<<8) | (cod[p+2]<<16) | (cod[p+3]<<24)); p+=4;
            uint[] f=new uint[256];
            for (int i=0;i<256;i++) f[i]=1;
            uint tot=256;
            var res=new byte[len];
            for (int i=(int)len-1;i>=0;i--)
            {
                uint v=x%tot, cum=0; int s=0;
                while (cum+f[s]<=v){ cum+=f[s]; s++; }
                res[i]=(byte)s;
                x = f[s]*(x/tot) + (x%tot) - cum;
                if (p<cod.Length){ x=(x<<8) | cod[p++]; }
                f[s]++;
                tot++;
                if (tot> (1<<15))
                {
                    tot=0;
                    for (int k=0;k<256;k++)
                    {
                        f[k]=(f[k]+1)>>1;
                        tot+=f[k];
                    }
                }
            }
            return res;
        }
    }
}
