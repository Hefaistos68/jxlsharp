// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace JxlSharp.Base
{
    // Eight-pixel kernels shared by external pixel conversion and CMS row buffers.
    internal static class PixelPacking
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<uint> LoadU16(ReadOnlySpan<byte> source, bool littleEndian)
        {
            Vector128<byte> words = Vector128.Create(source.Slice(0, 16));
            if (!littleEndian) words = Ssse3.Shuffle(words, Vector128.Create((byte)1,0,3,2,5,4,7,6,9,8,11,10,13,12,15,14));
            return Avx2.ConvertToVector256Int32(words.AsUInt16()).AsUInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<float> HalfToFloat(Vector256<uint> bits)
        {
            // Binary16 subnormals are integer multiples of 2^-24; all such products
            // are exactly representable as float32. NaNs retain payload/sign and are quieted.
            Vector256<uint> value = bits & Vector256.Create(0x7fffu);
            Vector256<uint> sign = (bits & Vector256.Create(0x8000u)) << 16;
            Vector256<uint> normal = (value << 13) + Vector256.Create(0x38000000u);
            Vector256<uint> special = (value << 13) | Vector256.Create(0x7f800000u);
            special |= Vector256.ConditionalSelect(Vector256.GreaterThan(value, Vector256.Create(0x7c00u)), Vector256.Create(0x00400000u), Vector256<uint>.Zero);
            normal = Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(value, Vector256.Create(0x7c00u)), special, normal);
            Vector256<float> subnormal = Vector256.ConvertToSingle(value) * Vector256.Create(1f / 16777216f);
            normal = Vector256.ConditionalSelect(Vector256.LessThan(value, Vector256.Create(0x400u)), subnormal.AsUInt32(), normal);
            return (normal | sign).AsSingle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<ushort> FloatToHalf(Vector256<float> values)
        {
            Vector256<uint> bits = values.AsUInt32();
            Vector256<uint> value = bits & Vector256.Create(0x7fffffffu);
            Vector256<uint> sign = (bits >> 16) & Vector256.Create(0x8000u);
            // Round to nearest, ties to even, using integer arithmetic in both ranges.
            Vector256<uint> normal = ((value + Vector256.Create(0xfffu) + ((value >> 13) & Vector256.Create(1u))) >> 13) - Vector256.Create(0x1c000u);
            normal = Vector256.Min(normal, Vector256.Create(0x7c00u));
            Vector256<uint> mantissa = (value & Vector256.Create(0x7fffffu)) | Vector256.Create(0x800000u);
            // Express the implicit float32 significand in units of the half subnormal quantum.
            Vector256<uint> shift = Vector256.Create(126u) - (value >> 23);
            Vector256<uint> retained = Avx2.ShiftRightLogicalVariable(mantissa, shift);
            Vector256<uint> bias = Avx2.ShiftLeftLogicalVariable(Vector256.Create(1u), shift - Vector256.Create(1u)) - Vector256.Create(1u);
            Vector256<uint> subnormal = Avx2.ShiftRightLogicalVariable(mantissa + bias + (retained & Vector256.Create(1u)), shift);
            normal = Vector256.ConditionalSelect(Vector256.LessThan(value, Vector256.Create(0x38800000u)), subnormal, normal);
            Vector256<uint> nan = Vector256.Create(0x7e00u) | ((value >> 13) & Vector256.Create(0x3ffu));
            normal = Vector256.ConditionalSelect(Vector256.GreaterThan(value, Vector256.Create(0x7f800000u)), nan, normal) | sign;
            return Vector128.Narrow(normal.GetLower(), normal.GetUpper());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void StoreHalf(Vector256<float> values, Span<byte> destination, bool littleEndian)
        {
            Vector128<byte> words = FloatToHalf(values).AsByte();
            if (!littleEndian) words = Ssse3.Shuffle(words, Vector128.Create((byte)1,0,3,2,5,4,7,6,9,8,11,10,13,12,15,14));
            words.CopyTo(destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Interleave2(Vector256<float> a, Vector256<float> b, out Vector256<float> x, out Vector256<float> y)
        {
            Vector256<float> low = Avx.UnpackLow(a, b), high = Avx.UnpackHigh(a, b);
            x = Avx.Permute2x128(low, high, 0x20);
            y = Avx.Permute2x128(low, high, 0x31);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Interleave3(Vector256<float> a, Vector256<float> b, Vector256<float> c,
            out Vector256<float> x, out Vector256<float> y, out Vector256<float> z)
        {
            Vector256<float> p00 = Avx2.PermuteVar8x32(a, Vector256.Create(0, 0, 0, 1, 0, 0, 2, 0));
            Vector256<float> p01 = Avx2.PermuteVar8x32(b, Vector256.Create(0, 0, 0, 0, 1, 0, 0, 2));
            Vector256<float> p02 = Avx2.PermuteVar8x32(c, Vector256.Create(0, 0, 0, 0, 0, 1, 0, 0));
            Vector256<float> m01 = Vector256.Create(0, -1, 0, 0, -1, 0, 0, -1).AsSingle();
            Vector256<float> m02 = Vector256.Create(0, 0, -1, 0, 0, -1, 0, 0).AsSingle();
            x = Vector256.ConditionalSelect(m02, p02, Vector256.ConditionalSelect(m01, p01, p00));
            Vector256<float> p10 = Avx2.PermuteVar8x32(a, Vector256.Create(0, 3, 0, 0, 4, 0, 0, 5));
            Vector256<float> p11 = Avx2.PermuteVar8x32(b, Vector256.Create(0, 0, 3, 0, 0, 4, 0, 0));
            Vector256<float> p12 = Avx2.PermuteVar8x32(c, Vector256.Create(2, 0, 0, 3, 0, 0, 4, 0));
            Vector256<float> m11 = Vector256.Create(0, 0, -1, 0, 0, -1, 0, 0).AsSingle();
            Vector256<float> m12 = Vector256.Create(-1, 0, 0, -1, 0, 0, -1, 0).AsSingle();
            y = Vector256.ConditionalSelect(m12, p12, Vector256.ConditionalSelect(m11, p11, p10));
            Vector256<float> p20 = Avx2.PermuteVar8x32(a, Vector256.Create(0, 0, 6, 0, 0, 7, 0, 0));
            Vector256<float> p21 = Avx2.PermuteVar8x32(b, Vector256.Create(5, 0, 0, 6, 0, 0, 7, 0));
            Vector256<float> p22 = Avx2.PermuteVar8x32(c, Vector256.Create(0, 5, 0, 0, 6, 0, 0, 7));
            Vector256<float> m21 = Vector256.Create(-1, 0, 0, -1, 0, 0, -1, 0).AsSingle();
            Vector256<float> m22 = Vector256.Create(0, -1, 0, 0, -1, 0, 0, -1).AsSingle();
            z = Vector256.ConditionalSelect(m22, p22, Vector256.ConditionalSelect(m21, p21, p20));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Deinterleave3(Vector256<float> a, Vector256<float> b, Vector256<float> c,
            out Vector256<float> x, out Vector256<float> y, out Vector256<float> z)
        {
            Vector256<float> p00 = Avx2.PermuteVar8x32(a, Vector256.Create(0, 3, 6, 0, 0, 0, 0, 0));
            Vector256<float> p01 = Avx2.PermuteVar8x32(b, Vector256.Create(0, 0, 0, 1, 4, 7, 0, 0));
            Vector256<float> p02 = Avx2.PermuteVar8x32(c, Vector256.Create(0, 0, 0, 0, 0, 0, 2, 5));
            Vector256<float> m01 = Vector256.Create(0, 0, 0, -1, -1, -1, 0, 0).AsSingle();
            Vector256<float> m02 = Vector256.Create(0, 0, 0, 0, 0, 0, -1, -1).AsSingle();
            x = Vector256.ConditionalSelect(m02, p02, Vector256.ConditionalSelect(m01, p01, p00));
            Vector256<float> p10 = Avx2.PermuteVar8x32(a, Vector256.Create(1, 4, 7, 0, 0, 0, 0, 0));
            Vector256<float> p11 = Avx2.PermuteVar8x32(b, Vector256.Create(0, 0, 0, 2, 5, 0, 0, 0));
            Vector256<float> p12 = Avx2.PermuteVar8x32(c, Vector256.Create(0, 0, 0, 0, 0, 0, 3, 6));
            Vector256<float> m11 = Vector256.Create(0, 0, 0, -1, -1, 0, 0, 0).AsSingle();
            Vector256<float> m12 = Vector256.Create(0, 0, 0, 0, 0, -1, -1, -1).AsSingle();
            y = Vector256.ConditionalSelect(m12, p12, Vector256.ConditionalSelect(m11, p11, p10));
            Vector256<float> p20 = Avx2.PermuteVar8x32(a, Vector256.Create(2, 5, 0, 0, 0, 0, 0, 0));
            Vector256<float> p21 = Avx2.PermuteVar8x32(b, Vector256.Create(0, 0, 0, 3, 6, 0, 0, 0));
            Vector256<float> p22 = Avx2.PermuteVar8x32(c, Vector256.Create(0, 0, 0, 0, 0, 1, 4, 7));
            Vector256<float> m21 = Vector256.Create(0, 0, -1, -1, -1, 0, 0, 0).AsSingle();
            Vector256<float> m22 = Vector256.Create(0, 0, 0, 0, 0, -1, -1, -1).AsSingle();
            z = Vector256.ConditionalSelect(m22, p22, Vector256.ConditionalSelect(m21, p21, p20));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Interleave4(Vector256<float> r, Vector256<float> g, Vector256<float> b, Vector256<float> a,
            out Vector256<float> x, out Vector256<float> y, out Vector256<float> z, out Vector256<float> w)
        {
            Vector256<float> rg0 = Avx.UnpackLow(r, g), rg1 = Avx.UnpackHigh(r, g);
            Vector256<float> ba0 = Avx.UnpackLow(b, a), ba1 = Avx.UnpackHigh(b, a);
            Vector256<float> p0 = Avx.Shuffle(rg0, ba0, 0x44), p1 = Avx.Shuffle(rg0, ba0, 0xee);
            Vector256<float> p2 = Avx.Shuffle(rg1, ba1, 0x44), p3 = Avx.Shuffle(rg1, ba1, 0xee);
            x = Avx.Permute2x128(p0, p1, 0x20);
            y = Avx.Permute2x128(p2, p3, 0x20);
            z = Avx.Permute2x128(p0, p1, 0x31);
            w = Avx.Permute2x128(p2, p3, 0x31);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<uint> Quantize(Vector256<float> v, float mul) => Quantize(v, mul, mul);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<uint> Quantize(Vector256<float> v, float mul, float cap)
        {
            Vector256<float> rounded = v * Vector256.Create(mul) + Vector256.Create(0.5f);
            rounded = Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(v, Vector256.Create(1f)), Vector256.Create(cap), rounded);
            rounded = Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(v, Vector256<float>.Zero), rounded, Vector256<float>.Zero);
            return Avx.ConvertToVector256Int32WithTruncation(rounded).AsUInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint Quantize(float v, float mul) => !(v >= 0f) ? 0u : v >= 1f ? (uint)mul : (uint)(v * mul + 0.5f);

        internal static void ByteChannelMasks(int channels, int channel, out Vector128<byte> low, out Vector128<byte> high)
        {
            switch ((channels << 2) | channel)
            {
                case 12:
                    low = Vector128.Create((byte)0,3,6,9,12,15,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,128,128,10,13,128,128,128,128,128,128,128,128);
                    return;
                case 13:
                    low = Vector128.Create((byte)1,4,7,10,13,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,128,8,11,14,128,128,128,128,128,128,128,128);
                    return;
                case 14:
                    low = Vector128.Create((byte)2,5,8,11,14,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,128,9,12,15,128,128,128,128,128,128,128,128);
                    return;
                case 16:
                    low = Vector128.Create((byte)0,4,8,12,128,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,0,4,8,12,128,128,128,128,128,128,128,128);
                    return;
                case 17:
                    low = Vector128.Create((byte)1,5,9,13,128,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,1,5,9,13,128,128,128,128,128,128,128,128);
                    return;
                case 18:
                    low = Vector128.Create((byte)2,6,10,14,128,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,2,6,10,14,128,128,128,128,128,128,128,128);
                    return;
                case 19:
                    low = Vector128.Create((byte)3,7,11,15,128,128,128,128,128,128,128,128,128,128,128,128);
                    high = Vector128.Create((byte)128,128,128,128,3,7,11,15,128,128,128,128,128,128,128,128);
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<float> LoadByteChannel(ReadOnlySpan<byte> source, int channels,
            Vector128<byte> lowMask, Vector128<byte> highMask, float mul)
        {
            Vector128<byte> low = Vector128.Create(source.Slice(0, 16));
            Vector128<byte> high = Vector128.Create(source.Slice(channels == 3 ? 8 : 16, 16));
            Vector128<byte> selected = Ssse3.Shuffle(low, lowMask) | Ssse3.Shuffle(high, highMask);
            return Vector256.ConvertToSingle(Avx2.ConvertToVector256Int32(selected)) * Vector256.Create(mul);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<float> LoadSamples(ReadOnlySpan<byte> source, bool isFloat, bool littleEndian, float mul)
        {
            if (isFloat)
            {
                Vector256<byte> data = Vector256.Create(source.Slice(0, 32));
                if (!littleEndian) data = Avx2.Shuffle(data, Vector256.Create((byte)3,2,1,0,7,6,5,4,11,10,9,8,15,14,13,12,19,18,17,16,23,22,21,20,27,26,25,24,31,30,29,28));
                return data.AsSingle();
            }
            Vector128<byte> words = Vector128.Create(source.Slice(0, 16));
            if (!littleEndian) words = Ssse3.Shuffle(words, Vector128.Create((byte)1,0,3,2,5,4,7,6,9,8,11,10,13,12,15,14));
            return Vector256.ConvertToSingle(Avx2.ConvertToVector256Int32(words.AsUInt16())) * Vector256.Create(mul);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void StoreSamples(Vector256<float> samples, Span<byte> destination, int bytes, bool isFloat, bool littleEndian)
        {
            if (isFloat || bytes == 4)
            {
                Vector256<byte> bits = samples.AsByte();
                if (!littleEndian) bits = Avx2.Shuffle(bits, Vector256.Create((byte)3,2,1,0,7,6,5,4,11,10,9,8,15,14,13,12,19,18,17,16,23,22,21,20,27,26,25,24,31,30,29,28));
                bits.CopyTo(destination);
                return;
            }
            if (bytes == 3)
            {
                Vector256<byte> mask = littleEndian
                    ? Vector256.Create((byte)0,1,2,4,5,6,8,9,10,12,13,14,128,128,128,128,0,1,2,4,5,6,8,9,10,12,13,14,128,128,128,128)
                    : Vector256.Create((byte)2,1,0,6,5,4,10,9,8,14,13,12,128,128,128,128,2,1,0,6,5,4,10,9,8,14,13,12,128,128,128,128);
                Vector256<byte> packed = Avx2.Shuffle(samples.AsByte(), mask);
                Vector128<byte> low = packed.GetLower(), high = packed.GetUpper();
                (low | Sse2.ShiftLeftLogical128BitLane(high, 12)).CopyTo(destination);
                Sse2.ShiftRightLogical128BitLane(high, 4).GetLower().CopyTo(destination.Slice(16));
                return;
            }
            Vector128<ushort> words = Vector128.Narrow(samples.GetLower().AsUInt32(), samples.GetUpper().AsUInt32());
            if (bytes == 1)
                Vector128.Narrow(words, Vector128<ushort>.Zero).GetLower().CopyTo(destination);
            else
            {
                Vector128<byte> data = words.AsByte();
                if (!littleEndian) data = Ssse3.Shuffle(data, Vector128.Create((byte)1,0,3,2,5,4,7,6,9,8,11,10,13,12,15,14));
                data.CopyTo(destination);
            }
        }

        internal static void Interleave3(ReadOnlySpan<float> r, ReadOnlySpan<float> g, ReadOnlySpan<float> b, Span<float> destination, int count)
        {
            int x = 0;
            if (Avx2.IsSupported)
            {
                for (; x + 8 <= count; x += 8)
                {
                    Interleave3(Vector256.Create(r.Slice(x, 8)), Vector256.Create(g.Slice(x, 8)), Vector256.Create(b.Slice(x, 8)), out Vector256<float> p, out Vector256<float> q, out Vector256<float> s);
                    p.CopyTo(destination.Slice(x * 3 + 0, 8));
                    q.CopyTo(destination.Slice(x * 3 + 8, 8));
                    s.CopyTo(destination.Slice(x * 3 + 16, 8));
                }
            }
            for (; x < count; x++)
            {
                destination[x * 3 + 0] = r[x];
                destination[x * 3 + 1] = g[x];
                destination[x * 3 + 2] = b[x];
            }
        }

        internal static void Interleave4(ReadOnlySpan<float> r, ReadOnlySpan<float> g, ReadOnlySpan<float> b, ReadOnlySpan<float> a, Span<float> destination, int count)
        {
            int x = 0;
            if (Avx2.IsSupported)
            {
                for (; x + 8 <= count; x += 8)
                {
                    Interleave4(Vector256.Create(r.Slice(x, 8)), Vector256.Create(g.Slice(x, 8)), Vector256.Create(b.Slice(x, 8)), Vector256.Create(a.Slice(x, 8)), out Vector256<float> p, out Vector256<float> q, out Vector256<float> s, out Vector256<float> t);
                    p.CopyTo(destination.Slice(x * 4 + 0, 8));
                    q.CopyTo(destination.Slice(x * 4 + 8, 8));
                    s.CopyTo(destination.Slice(x * 4 + 16, 8));
                    t.CopyTo(destination.Slice(x * 4 + 24, 8));
                }
            }
            for (; x < count; x++)
            {
                destination[x * 4 + 0] = r[x];
                destination[x * 4 + 1] = g[x];
                destination[x * 4 + 2] = b[x];
                destination[x * 4 + 3] = a[x];
            }
        }

        internal static void Deinterleave3(ReadOnlySpan<float> source, Span<float> r, Span<float> g, Span<float> b, int count)
        {
            int x = 0;
            if (Avx2.IsSupported)
            {
                for (; x + 8 <= count; x += 8)
                {
                    Deinterleave3(Vector256.Create(source.Slice(x * 3 + 0, 8)), Vector256.Create(source.Slice(x * 3 + 8, 8)), Vector256.Create(source.Slice(x * 3 + 16, 8)), out Vector256<float> vr, out Vector256<float> vg, out Vector256<float> vb);
                    vr.CopyTo(r.Slice(x, 8));
                    vg.CopyTo(g.Slice(x, 8));
                    vb.CopyTo(b.Slice(x, 8));
                }
            }
            for (; x < count; x++)
            {
                r[x] = source[x * 3 + 0];
                g[x] = source[x * 3 + 1];
                b[x] = source[x * 3 + 2];
            }
        }
    }
}
