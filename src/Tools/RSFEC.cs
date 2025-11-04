// Public Domain

namespace SVQNext.Tools;

// Minimal GF(256) Reed–Solomon encoder with erasure recovery (no error locator search).
// For streaming parity over fixed-size symbols (bytes). Suitable for (n,k) with small parity.
public static class RSFEC
{
    private const int PRIM = 0x11d;
    private static readonly byte[] EXP = new byte[512];
    private static readonly byte[] LOG = new byte[256];

    static RSFEC()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            EXP[i] = (byte)x;
            LOG[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= PRIM;
        }

        for (var i = 255; i < 512; i++) EXP[i] = EXP[i - 255];
        LOG[0] = 0;
    }

    private static byte gf_mul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        var r = LOG[a] + LOG[b];
        return EXP[r];
    }

    private static byte gf_div(byte a, byte b)
    {
        if (a == 0) return 0;
        if (b == 0) throw new DivideByZeroException();
        var r = LOG[a] - LOG[b];
        if (r < 0) r += 255;
        return EXP[r];
    }

    private static byte gf_pow(byte a, int p)
    {
        if (a == 0) return 0;
        var r = LOG[a] * p % 255;
        if (r < 0) r += 255;
        return EXP[r];
    }

    // Generate generator polynomial for t parity symbols
    private static byte[] Generator(int t)
    {
        var g = new byte[] { 1 };
        for (var i = 0; i < t; i++)
        {
            var ng = new byte[g.Length + 1];
            for (var j = 0; j < g.Length; j++)
            {
                // ng[j]   += g[j]
                ng[j] ^= g[j];
                // ng[j+1] += g[j] * a^i
                var mult = gf_pow(2, i); // primitive element
                ng[j + 1] ^= gf_mul(g[j], mult);
            }

            g = ng;
        }

        return g;
    }

    // Encode message -> message + t parity
    public static byte[] Encode(byte[] message, int t)
    {
        var g = Generator(t);
        var parity = new byte[t];
        foreach (var m in message)
        {
            var feedback = (byte)(m ^ parity[0]);
            // shift
            for (var i = 0; i < t - 1; i++) parity[i] = parity[i + 1];
            parity[t - 1] = 0;
            if (feedback != 0)
                for (var i = 0; i < t; i++)
                    parity[i] ^= gf_mul(g[i + 1], feedback);
        }

        var outb = new byte[message.Length + t];
        Buffer.BlockCopy(message, 0, outb, 0, message.Length);
        Buffer.BlockCopy(parity, 0, outb, message.Length, t);
        return outb;
    }

    // Basic erasure recovery given erasure positions (indices in codeword). Limited use.
    // For simplicity we only support <= t erasures and solve via Gaussian elimination in GF(256).
    public static byte[] Recover(byte[] codeword, int k, List<int> erasures)
    {
        var n = codeword.Length;
        var t = n - k;
        if (erasures.Count == 0) return codeword;
        if (erasures.Count > t) throw new InvalidOperationException("Too many erasures");

        // Build equations using syndromes S_i = sum c_j * (a^j)^i  (not optimized)
        byte alpha = 2;
        var synd = new byte[t];
        for (var i = 0; i < t; i++)
        {
            byte s = 0;
            for (var j = 0; j < n; j++)
            {
                var aji = gf_pow(alpha, j * (i + 1) % 255);
                s ^= gf_mul(codeword[j], aji);
            }

            synd[i] = s;
        }

        // If no erasures, synd all zero for valid codeword; else solve linear system for erased symbols
        var m = erasures.Count;
        // Matrix A (t x m), vector synd' (t)
        var A = new byte[t, m];
        for (var i = 0; i < t; i++)
        for (var e = 0; e < m; e++)
        {
            var j = erasures[e];
            A[i, e] = gf_pow(alpha, j * (i + 1) % 255);
        }

        // Solve least squares (pick first m equations)
        var M = new byte[m, m];
        var b = new byte[m];
        for (var i = 0; i < m; i++)
        {
            for (var e = 0; e < m; e++) M[i, e] = A[i, e];
            b[i] = synd[i];
        }

        // Gaussian elimination in GF(256)
        var row = 0;
        for (var col = 0; col < m && row < m; col++)
        {
            var pivot = row;
            for (var r = row; r < m; r++)
                if (M[r, col] != 0)
                {
                    pivot = r;
                    break;
                }

            if (M[pivot, col] == 0) continue;
            if (pivot != row)
            {
                for (var c = col; c < m; c++)
                {
                    var tmp = M[row, c];
                    M[row, c] = M[pivot, c];
                    M[pivot, c] = tmp;
                }

                var tb = b[row];
                b[row] = b[pivot];
                b[pivot] = tb;
            }

            var inv = gf_div(1, M[row, col]);
            for (var c = col; c < m; c++) M[row, c] = gf_mul(M[row, c], inv);
            b[row] = gf_mul(b[row], inv);
            for (var r = 0; r < m; r++)
            {
                if (r == row) continue;
                var factor = M[r, col];
                if (factor == 0) continue;
                for (var c = col; c < m; c++) M[r, c] ^= gf_mul(factor, M[row, c]);
                b[r] ^= gf_mul(factor, b[row]);
            }

            row++;
        }

        // Now M should be identity; b are the unknown erased symbols
        var res = (byte[])codeword.Clone();
        for (var e = 0; e < m; e++) res[erasures[e]] = b[e];
        return res;
    }
}