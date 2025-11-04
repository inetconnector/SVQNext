// Public Domain
using System;
using System.Collections.Generic;

namespace SVQNext.Tools
{
    // Minimal GF(256) Reed–Solomon encoder with erasure recovery (no error locator search).
    // For streaming parity over fixed-size symbols (bytes). Suitable for (n,k) with small parity.
    public static class RSFEC
    {
        static readonly byte[] EXP = new byte[512];
        static readonly byte[] LOG = new byte[256];
        const int PRIM = 0x11d;
        static RSFEC()
        {
            int x=1;
            for (int i=0;i<255;i++)
            {
                EXP[i]=(byte)x; LOG[x]=(byte)i;
                x<<=1;
                if ((x&0x100)!=0) x^=PRIM;
            }
            for (int i=255;i<512;i++) EXP[i]=EXP[i-255];
            LOG[0]=0;
        }
        static byte gf_mul(byte a, byte b)
        {
            if (a==0 || b==0) return 0;
            int r = LOG[a] + LOG[b];
            return EXP[r];
        }

        static byte gf_div(byte a, byte b)
        {
            if (a==0) return 0;
            if (b==0) throw new DivideByZeroException();
            int r = LOG[a] - LOG[b];
            if (r<0) r+=255;
            return EXP[r];
        }

        static byte gf_pow(byte a, int p)
        {
            if (a==0) return 0;
            int r = (LOG[a]*p)%255;
            if (r<0) r+=255;
            return EXP[r];
        }

        // Generate generator polynomial for t parity symbols
        static byte[] Generator(int t)
        {
            var g=new byte[]{1};
            for (int i=0;i<t;i++)
            {
                var ng=new byte[g.Length+1];
                for (int j=0;j<g.Length;j++)
                {
                    // ng[j]   += g[j]
                    ng[j] ^= g[j];
                    // ng[j+1] += g[j] * a^i
                    byte mult = gf_pow(2, i); // primitive element
                    ng[j+1] ^= gf_mul(g[j], mult);
                }
                g=ng;
            }
            return g;
        }

        // Encode message -> message + t parity
        public static byte[] Encode(byte[] message, int t)
        {
            var g=Generator(t);
            var parity=new byte[t];
            foreach (var m in message)
            {
                byte feedback = (byte)(m ^ parity[0]);
                // shift
                for (int i=0;i<t-1;i++) parity[i]=parity[i+1];
                parity[t-1]=0;
                if (feedback!=0)
                {
                    for (int i=0;i<t;i++)
                        parity[i] ^= gf_mul(g[i+1], feedback);
                }
            }
            var outb=new byte[message.Length+t];
            Buffer.BlockCopy(message,0,outb,0,message.Length);
            Buffer.BlockCopy(parity,0,outb,message.Length,t);
            return outb;
        }

        // Basic erasure recovery given erasure positions (indices in codeword). Limited use.
        // For simplicity we only support <= t erasures and solve via Gaussian elimination in GF(256).
        public static byte[] Recover(byte[] codeword, int k, List<int> erasures)
        {
            int n=codeword.Length;
            int t=n-k;
            if (erasures.Count==0) return codeword;
            if (erasures.Count>t) throw new InvalidOperationException("Too many erasures");

            // Build equations using syndromes S_i = sum c_j * (a^j)^i  (not optimized)
            byte alpha = 2;
            byte[] synd = new byte[t];
            for (int i=0;i<t;i++)
            {
                byte s=0;
                for (int j=0;j<n;j++)
                {
                    byte aji = gf_pow(alpha, (j*(i+1))%255);
                    s ^= gf_mul(codeword[j], aji);
                }
                synd[i]=s;
            }
            // If no erasures, synd all zero for valid codeword; else solve linear system for erased symbols
            int m=erasures.Count;
            // Matrix A (t x m), vector synd' (t)
            byte[,] A=new byte[t,m];
            for (int i=0;i<t;i++)
            {
                for (int e=0;e<m;e++)
                {
                    int j=erasures[e];
                    A[i,e]=gf_pow(alpha, (j*(i+1))%255);
                }
            }
            // Solve least squares (pick first m equations)
            byte[,] M=new byte[m,m];
            byte[] b=new byte[m];
            for (int i=0;i<m;i++)
            {
                for (int e=0;e<m;e++) M[i,e]=A[i,e];
                b[i]=synd[i];
            }
            // Gaussian elimination in GF(256)
            int row=0;
            for (int col=0; col<m && row<m; col++)
            {
                int pivot=row;
                for (int r=row;r<m;r++)
                {
                    if (M[r,col]!=0)
                    {
                        pivot=r;
                        break;
                    }
                }
                if (M[pivot,col]==0)
                {
                    continue;
                }
                if (pivot!=row)
                {
                    for (int c=col;c<m;c++)
                    {
                        byte tmp=M[row,c];
                        M[row,c]=M[pivot,c];
                        M[pivot,c]=tmp;
                    }
                    byte tb=b[row];
                    b[row]=b[pivot];
                    b[pivot]=tb;
                }
                byte inv = gf_div(1, M[row,col]);
                for (int c=col;c<m;c++)
                {
                    M[row,c]=gf_mul(M[row,c], inv);
                }
                b[row]=gf_mul(b[row], inv);
                for (int r=0;r<m;r++)
                {
                    if (r==row)
                    {
                        continue;
                    }
                    byte factor=M[r,col];
                    if (factor==0)
                    {
                        continue;
                    }
                    for (int c=col;c<m;c++)
                    {
                        M[r,c] ^= gf_mul(factor, M[row,c]);
                    }
                    b[r] ^= gf_mul(factor, b[row]);
                }
                row++;
            }
            // Now M should be identity; b are the unknown erased symbols
            var res=(byte[])codeword.Clone();
            for (int e=0;e<m;e++) res[erasures[e]]=b[e];
            return res;
        }
    }
}
