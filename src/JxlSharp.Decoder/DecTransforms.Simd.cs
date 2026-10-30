// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace JxlSharp.Decoder
{
    public static partial class DecTransforms
    {
        // Each SIMD lane is an independent column. The fixed-size butterflies keep
        // the scalar arithmetic order, including its explicitly fused operations.
        private static void TransformColumnsSimd(int n, int columns, DctFrom from, DctTo to, bool inverse, Span<float> tmp)
        {
            Span<Vector256<float>> values = stackalloc Vector256<float>[32];
            int column = 0;
            Vector256<float> scale = Vector256.Create(1.0f / n);
            for (; column + 8 <= columns; column += 8)
            {
                for (int row = 0; row < n; row++)
                    values[row] = Vector256.Create(from.Slice((nuint)row, (nuint)column).Slice(0, 8));
                if (inverse)
                {
                    switch (n)
                    {
                        case 4: Inverse4Simd(values); break;
                        case 8: Inverse8Simd(values); break;
                        case 16: Inverse16Simd(values); break;
                        case 32: Inverse32Simd(values); break;
                    }
                }
                else
                {
                    switch (n)
                    {
                        case 4: Forward4Simd(values); break;
                        case 8: Forward8Simd(values); break;
                        case 16: Forward16Simd(values); break;
                        case 32: Forward32Simd(values); break;
                    }
                }
                for (int row = 0; row < n; row++)
                {
                    Vector256<float> result = inverse ? values[row] : values[row] * scale;
                    result.CopyTo(to.Slice((nuint)row, (nuint)column).Slice(0, 8));
                }
            }
            if (column < columns)
            {
                DctFrom tailFrom = new DctFrom(from.Data.Slice(column), from.Stride);
                DctTo tailTo = new DctTo(to.Data.Slice(column), to.Stride);
                if (inverse) Idct1D(n, columns - column, tailFrom, tailTo, tmp);
                else Dct1D(n, columns - column, tailFrom, tailTo, tmp);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Forward4Simd(Span<Vector256<float>> values)
        {
            Vector256<float> v0 = values[0];
            Vector256<float> v1 = values[1];
            Vector256<float> v2 = values[2];
            Vector256<float> v3 = values[3];
            Vector256<float> v4 = v0 + v3;
            Vector256<float> v5 = v1 + v2;
            Vector256<float> v6 = (v0 - v3) * Vector256.Create(0.541196100146197f);
            Vector256<float> v7 = (v1 - v2) * Vector256.Create(1.3065629648763764f);
            Vector256<float> v8 = v6 + v7;
            Vector256<float> v9 = v6 - v7;
            Vector256<float> v10 = v4 + v5;
            Vector256<float> v11 = Fma.MultiplyAdd(v8, Vector256.Create(DctScales.kSqrt2), v9);
            Vector256<float> v12 = v4 - v5;
            values[0] = v10;
            values[1] = v11;
            values[2] = v12;
            values[3] = v9;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Forward8Simd(Span<Vector256<float>> values)
        {
            Forward8Simd(ref values[0], ref values[1], ref values[2], ref values[3], ref values[4], ref values[5], ref values[6], ref values[7]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Forward8Simd(ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2, ref Vector256<float> r3, ref Vector256<float> r4, ref Vector256<float> r5, ref Vector256<float> r6, ref Vector256<float> r7)
        {
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(8);
            Vector256<float> v0 = r0;
            Vector256<float> v1 = r1;
            Vector256<float> v2 = r2;
            Vector256<float> v3 = r3;
            Vector256<float> v4 = r4;
            Vector256<float> v5 = r5;
            Vector256<float> v6 = r6;
            Vector256<float> v7 = r7;
            Vector256<float> v8 = v0 + v7;
            Vector256<float> v9 = v1 + v6;
            Vector256<float> v10 = v2 + v5;
            Vector256<float> v11 = v3 + v4;
            Vector256<float> v12 = v8 + v11;
            Vector256<float> v13 = v9 + v10;
            Vector256<float> v14 = (v8 - v11) * Vector256.Create(0.541196100146197f);
            Vector256<float> v15 = (v9 - v10) * Vector256.Create(1.3065629648763764f);
            Vector256<float> v16 = v14 + v15;
            Vector256<float> v17 = v14 - v15;
            Vector256<float> v18 = v12 + v13;
            Vector256<float> v19 = Fma.MultiplyAdd(v16, Vector256.Create(DctScales.kSqrt2), v17);
            Vector256<float> v20 = v12 - v13;
            Vector256<float> v21 = (v0 - v7) * Vector256.Create(multipliers[0]);
            Vector256<float> v22 = (v1 - v6) * Vector256.Create(multipliers[1]);
            Vector256<float> v23 = (v2 - v5) * Vector256.Create(multipliers[2]);
            Vector256<float> v24 = (v3 - v4) * Vector256.Create(multipliers[3]);
            Vector256<float> v25 = v21 + v24;
            Vector256<float> v26 = v22 + v23;
            Vector256<float> v27 = (v21 - v24) * Vector256.Create(0.541196100146197f);
            Vector256<float> v28 = (v22 - v23) * Vector256.Create(1.3065629648763764f);
            Vector256<float> v29 = v27 + v28;
            Vector256<float> v30 = v27 - v28;
            Vector256<float> v31 = v25 + v26;
            Vector256<float> v32 = Fma.MultiplyAdd(v29, Vector256.Create(DctScales.kSqrt2), v30);
            Vector256<float> v33 = v25 - v26;
            Vector256<float> v34 = Fma.MultiplyAdd(v31, Vector256.Create(DctScales.kSqrt2), v32);
            Vector256<float> v35 = v32 + v33;
            Vector256<float> v36 = v33 + v30;
            r0 = v18;
            r1 = v34;
            r2 = v19;
            r3 = v35;
            r4 = v20;
            r5 = v36;
            r6 = v17;
            r7 = v30;
        }

        private static void Forward16Simd(Span<Vector256<float>> values)
        {
            Span<Vector256<float>> even = stackalloc Vector256<float>[8];
            Span<Vector256<float>> odd = stackalloc Vector256<float>[8];
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(16);
            for (int i = 0; i < 8; i++)
            {
                even[i] = values[i] + values[16 - i - 1];
                odd[i] = (values[i] - values[16 - i - 1]) * Vector256.Create(multipliers[i]);
            }
            Forward8Simd(even);
            Forward8Simd(odd);
            odd[0] = Fma.MultiplyAdd(odd[0], Vector256.Create(DctScales.kSqrt2), odd[1]);
            for (int i = 1; i + 1 < 8; i++) odd[i] += odd[i + 1];
            for (int i = 0; i < 8; i++)
            {
                values[2 * i] = even[i];
                values[2 * i + 1] = odd[i];
            }
        }

        private static void Forward32Simd(Span<Vector256<float>> values)
        {
            Span<Vector256<float>> even = stackalloc Vector256<float>[16];
            Span<Vector256<float>> odd = stackalloc Vector256<float>[16];
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(32);
            for (int i = 0; i < 16; i++)
            {
                even[i] = values[i] + values[32 - i - 1];
                odd[i] = (values[i] - values[32 - i - 1]) * Vector256.Create(multipliers[i]);
            }
            Forward16Simd(even);
            Forward16Simd(odd);
            odd[0] = Fma.MultiplyAdd(odd[0], Vector256.Create(DctScales.kSqrt2), odd[1]);
            for (int i = 1; i + 1 < 16; i++) odd[i] += odd[i + 1];
            for (int i = 0; i < 16; i++)
            {
                values[2 * i] = even[i];
                values[2 * i + 1] = odd[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse4Simd(Span<Vector256<float>> values)
        {
            Vector256<float> v0 = values[0];
            Vector256<float> v1 = values[1];
            Vector256<float> v2 = values[2];
            Vector256<float> v3 = values[3];
            Vector256<float> v4 = v0 + v2;
            Vector256<float> v5 = v0 - v2;
            Vector256<float> v6 = v3 + v1;
            Vector256<float> v7 = v1 * Vector256.Create(DctScales.kSqrt2);
            Vector256<float> v8 = v7 + v6;
            Vector256<float> v9 = v7 - v6;
            Vector256<float> v10 = Fma.MultiplyAdd(Vector256.Create(0.541196100146197f), v8, v4);
            Vector256<float> v11 = Fma.MultiplyAdd(Vector256.Create(1.3065629648763764f), v9, v5);
            Vector256<float> v12 = Fma.MultiplyAdd(-Vector256.Create(1.3065629648763764f), v9, v5);
            Vector256<float> v13 = Fma.MultiplyAdd(-Vector256.Create(0.541196100146197f), v8, v4);
            values[0] = v10;
            values[1] = v11;
            values[2] = v12;
            values[3] = v13;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse8Simd(Span<Vector256<float>> values)
        {
            Inverse8Simd(ref values[0], ref values[1], ref values[2], ref values[3], ref values[4], ref values[5], ref values[6], ref values[7]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse8Simd(ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2, ref Vector256<float> r3, ref Vector256<float> r4, ref Vector256<float> r5, ref Vector256<float> r6, ref Vector256<float> r7)
        {
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(8);
            Vector256<float> v0 = r0;
            Vector256<float> v1 = r1;
            Vector256<float> v2 = r2;
            Vector256<float> v3 = r3;
            Vector256<float> v4 = r4;
            Vector256<float> v5 = r5;
            Vector256<float> v6 = r6;
            Vector256<float> v7 = r7;
            Vector256<float> v8 = v0 + v4;
            Vector256<float> v9 = v0 - v4;
            Vector256<float> v10 = v6 + v2;
            Vector256<float> v11 = v2 * Vector256.Create(DctScales.kSqrt2);
            Vector256<float> v12 = v11 + v10;
            Vector256<float> v13 = v11 - v10;
            Vector256<float> v14 = Fma.MultiplyAdd(Vector256.Create(0.541196100146197f), v12, v8);
            Vector256<float> v15 = Fma.MultiplyAdd(Vector256.Create(1.3065629648763764f), v13, v9);
            Vector256<float> v16 = Fma.MultiplyAdd(-Vector256.Create(1.3065629648763764f), v13, v9);
            Vector256<float> v17 = Fma.MultiplyAdd(-Vector256.Create(0.541196100146197f), v12, v8);
            Vector256<float> v18 = v7 + v5;
            Vector256<float> v19 = v5 + v3;
            Vector256<float> v20 = v3 + v1;
            Vector256<float> v21 = v1 * Vector256.Create(DctScales.kSqrt2);
            Vector256<float> v22 = v21 + v19;
            Vector256<float> v23 = v21 - v19;
            Vector256<float> v24 = v18 + v20;
            Vector256<float> v25 = v20 * Vector256.Create(DctScales.kSqrt2);
            Vector256<float> v26 = v25 + v24;
            Vector256<float> v27 = v25 - v24;
            Vector256<float> v28 = Fma.MultiplyAdd(Vector256.Create(0.541196100146197f), v26, v22);
            Vector256<float> v29 = Fma.MultiplyAdd(Vector256.Create(1.3065629648763764f), v27, v23);
            Vector256<float> v30 = Fma.MultiplyAdd(-Vector256.Create(1.3065629648763764f), v27, v23);
            Vector256<float> v31 = Fma.MultiplyAdd(-Vector256.Create(0.541196100146197f), v26, v22);
            Vector256<float> v32 = Fma.MultiplyAdd(Vector256.Create(multipliers[0]), v28, v14);
            Vector256<float> v33 = Fma.MultiplyAdd(-Vector256.Create(multipliers[0]), v28, v14);
            Vector256<float> v34 = Fma.MultiplyAdd(Vector256.Create(multipliers[1]), v29, v15);
            Vector256<float> v35 = Fma.MultiplyAdd(-Vector256.Create(multipliers[1]), v29, v15);
            Vector256<float> v36 = Fma.MultiplyAdd(Vector256.Create(multipliers[2]), v30, v16);
            Vector256<float> v37 = Fma.MultiplyAdd(-Vector256.Create(multipliers[2]), v30, v16);
            Vector256<float> v38 = Fma.MultiplyAdd(Vector256.Create(multipliers[3]), v31, v17);
            Vector256<float> v39 = Fma.MultiplyAdd(-Vector256.Create(multipliers[3]), v31, v17);
            r0 = v32;
            r1 = v34;
            r2 = v36;
            r3 = v38;
            r4 = v39;
            r5 = v37;
            r6 = v35;
            r7 = v33;
        }

        private static void Inverse16Simd(Span<Vector256<float>> values)
        {
            Span<Vector256<float>> even = stackalloc Vector256<float>[8];
            Span<Vector256<float>> odd = stackalloc Vector256<float>[8];
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(16);
            for (int i = 0; i < 8; i++)
            {
                even[i] = values[2 * i];
                odd[i] = values[2 * i + 1];
            }
            Inverse8Simd(even);
            for (int i = 8-1; i > 0; i--) odd[i] += odd[i - 1];
            odd[0] *= Vector256.Create(DctScales.kSqrt2);
            Inverse8Simd(odd);
            for (int i = 0; i < 8; i++)
            {
                Vector256<float> mul = Vector256.Create(multipliers[i]);
                values[i] = Fma.MultiplyAdd(mul, odd[i], even[i]);
                values[16 - i - 1] = Fma.MultiplyAdd(-mul, odd[i], even[i]);
            }
        }

        private static void Inverse32Simd(Span<Vector256<float>> values)
        {
            Span<Vector256<float>> even = stackalloc Vector256<float>[16];
            Span<Vector256<float>> odd = stackalloc Vector256<float>[16];
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(32);
            for (int i = 0; i < 16; i++)
            {
                even[i] = values[2 * i];
                odd[i] = values[2 * i + 1];
            }
            Inverse16Simd(even);
            for (int i = 16-1; i > 0; i--) odd[i] += odd[i - 1];
            odd[0] *= Vector256.Create(DctScales.kSqrt2);
            Inverse16Simd(odd);
            for (int i = 0; i < 16; i++)
            {
                Vector256<float> mul = Vector256.Create(multipliers[i]);
                values[i] = Fma.MultiplyAdd(mul, odd[i], even[i]);
                values[32 - i - 1] = Fma.MultiplyAdd(-mul, odd[i], even[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Forward8BlockSimd(DctFrom from, DctTo to)
        {
            Vector256<float> r0 = Vector256.Create(from.Slice(0, 0).Slice(0, 8));
            Vector256<float> r1 = Vector256.Create(from.Slice(1, 0).Slice(0, 8));
            Vector256<float> r2 = Vector256.Create(from.Slice(2, 0).Slice(0, 8));
            Vector256<float> r3 = Vector256.Create(from.Slice(3, 0).Slice(0, 8));
            Vector256<float> r4 = Vector256.Create(from.Slice(4, 0).Slice(0, 8));
            Vector256<float> r5 = Vector256.Create(from.Slice(5, 0).Slice(0, 8));
            Vector256<float> r6 = Vector256.Create(from.Slice(6, 0).Slice(0, 8));
            Vector256<float> r7 = Vector256.Create(from.Slice(7, 0).Slice(0, 8));
            Forward8Simd(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            (r0 * Vector256.Create(0.125f)).CopyTo(to.Slice(0, 0).Slice(0, 8));
            (r1 * Vector256.Create(0.125f)).CopyTo(to.Slice(1, 0).Slice(0, 8));
            (r2 * Vector256.Create(0.125f)).CopyTo(to.Slice(2, 0).Slice(0, 8));
            (r3 * Vector256.Create(0.125f)).CopyTo(to.Slice(3, 0).Slice(0, 8));
            (r4 * Vector256.Create(0.125f)).CopyTo(to.Slice(4, 0).Slice(0, 8));
            (r5 * Vector256.Create(0.125f)).CopyTo(to.Slice(5, 0).Slice(0, 8));
            (r6 * Vector256.Create(0.125f)).CopyTo(to.Slice(6, 0).Slice(0, 8));
            (r7 * Vector256.Create(0.125f)).CopyTo(to.Slice(7, 0).Slice(0, 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse8BlockSimd(DctFrom from, DctTo to)
        {
            Vector256<float> r0 = Vector256.Create(from.Slice(0, 0).Slice(0, 8));
            Vector256<float> r1 = Vector256.Create(from.Slice(1, 0).Slice(0, 8));
            Vector256<float> r2 = Vector256.Create(from.Slice(2, 0).Slice(0, 8));
            Vector256<float> r3 = Vector256.Create(from.Slice(3, 0).Slice(0, 8));
            Vector256<float> r4 = Vector256.Create(from.Slice(4, 0).Slice(0, 8));
            Vector256<float> r5 = Vector256.Create(from.Slice(5, 0).Slice(0, 8));
            Vector256<float> r6 = Vector256.Create(from.Slice(6, 0).Slice(0, 8));
            Vector256<float> r7 = Vector256.Create(from.Slice(7, 0).Slice(0, 8));
            Inverse8Simd(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            r0.CopyTo(to.Slice(0, 0).Slice(0, 8));
            r1.CopyTo(to.Slice(1, 0).Slice(0, 8));
            r2.CopyTo(to.Slice(2, 0).Slice(0, 8));
            r3.CopyTo(to.Slice(3, 0).Slice(0, 8));
            r4.CopyTo(to.Slice(4, 0).Slice(0, 8));
            r5.CopyTo(to.Slice(5, 0).Slice(0, 8));
            r6.CopyTo(to.Slice(6, 0).Slice(0, 8));
            r7.CopyTo(to.Slice(7, 0).Slice(0, 8));
        }
    }
}
