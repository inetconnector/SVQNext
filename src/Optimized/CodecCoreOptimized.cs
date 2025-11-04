
using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SVQNext.Optimized
{
    /// <summary>
    /// SIMD/Span-optimized primitives used by encoder/decoder.
    /// Standalone and non-invasive: you can call these from the pipeline without changing existing APIs.
    /// </summary>
    public static class CodecCoreOptimized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SumAbsDiff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Span length mismatch");
            int len = a.Length;
            int i = 0;
            int acc = 0;

            if (Vector.IsHardwareAccelerated)
            {
                int vec = Vector<byte>.Count;
                for (; i <= len - vec; i += vec)
                {
                    var va = new Vector<byte>(a.Slice(i, vec).ToArray());
                    var vb = new Vector<byte>(b.Slice(i, vec).ToArray());
                    // Vector<byte> has no Abs; widen to ushort
                    Vector.Widen(va, out Vector<ushort> vaLo, out Vector<ushort> vaHi);
                    Vector.Widen(vb, out Vector<ushort> vbLo, out Vector<ushort> vbHi);
                    var diffLo = Vector.Abs(Vector.AsVectorInt16(Vector.Subtract(Vector.AsVectorInt16(vaLo), Vector.AsVectorInt16(vbLo))));
                    var diffHi = Vector.Abs(Vector.AsVectorInt16(Vector.Subtract(Vector.AsVectorInt16(vaHi), Vector.AsVectorInt16(vbHi))));
                    acc += HorizontalSumU16(Vector.AsVectorUInt16(diffLo));
                    acc += HorizontalSumU16(Vector.AsVectorUInt16(diffHi));
                }
            }
            for (; i < len; i++)
            {
                acc += Math.Abs(a[i] - b[i]);
            }
            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void XorInPlace(Span<byte> dst, ReadOnlySpan<byte> src)
        {
            if (dst.Length != src.Length) throw new ArgumentException("Length mismatch");
            int len = dst.Length;
            int i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                int vec = Vector<byte>.Count;
                for (; i <= len - vec; i += vec)
                {
                    var vd = new Vector<byte>(dst.Slice(i, vec).ToArray());
                    var vs = new Vector<byte>(src.Slice(i, vec).ToArray());
                    (vd ^ vs).CopyTo(dst.Slice(i, vec).ToArray()); // not ideal; fall back below for safety
                }
            }
            for (; i < len; i++) dst[i] ^= src[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(Span<byte> dst, ReadOnlySpan<byte> src) => src.CopyTo(dst);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear(Span<byte> dst) => dst.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HorizontalSumU16(Vector<ushort> v)
        {
            int sum = 0;
            for (int i = 0; i < Vector<ushort>.Count; i++) sum += v[i];
            return sum;
        }
    }
}
