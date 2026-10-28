// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Misc utilities for SIMD operations (ported from <c>lib/jxl/simd_util.h</c>,
    /// <c>lib/jxl/simd_util.cc</c>, and <c>lib/jxl/simd_util-inl.h</c>).
    /// </summary>
    public static class SimdUtil
    {
        /// <summary>
        /// Maximal vector size in bytes.
        /// </summary>
        /// <returns>Vector size in bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint MaxVectorSize()
        {
            return (nuint)(Vector<float>.Count * sizeof(float));
        }

        /// <summary>
        /// Returns the maximum value in the given span of unsigned 32-bit integers.
        /// </summary>
        /// <param name="data">Input data span.</param>
        /// <returns>Maximum value, or 0 if the span is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint MaxValue(ReadOnlySpan<uint> data)
        {
            if (data.IsEmpty)
            {
                return 0;
            }

            return TensorPrimitives.Max(data);
        }

        /// <summary>
        /// Returns the maximum value in the first <paramref name="len"/> elements of <paramref name="data"/>.
        /// </summary>
        /// <param name="data">Input data span.</param>
        /// <param name="len">Number of elements to inspect.</param>
        /// <returns>Maximum value, or 0 if <paramref name="len"/> is 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint MaxValue(ReadOnlySpan<uint> data, nuint len)
        {
            if (len == 0)
            {
                return 0;
            }

            return TensorPrimitives.Max(data.Slice(0, (int)len));
        }

        /// <summary>
        /// Stores <c>v0[0], v1[0], v0[1], v1[1], ...</c> to <paramref name="mem"/>, in this order.
        /// </summary>
        /// <typeparam name="T">4-byte unmanaged element type.</typeparam>
        /// <param name="v0">First vector span.</param>
        /// <param name="v1">Second vector span.</param>
        /// <param name="mem">Destination interleaved buffer of length at least <c>2 * v0.Length</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreInterleaved<T>(ReadOnlySpan<T> v0, ReadOnlySpan<T> v1, Span<T> mem)
            where T : unmanaged
        {
            int lanes = v0.Length;

            for (int i = 0; i < lanes; i++)
            {
                int baseIdx      = 2 * i;
                mem[baseIdx]     = v0[i];
                mem[baseIdx + 1] = v1[i];
            }
        }

        /// <summary>
        /// Stores <c>v0[0], v1[0], v2[0], v3[0], v0[1], ...</c> to <paramref name="mem"/>, in this order.
        /// </summary>
        /// <typeparam name="T">4-byte unmanaged element type.</typeparam>
        /// <param name="v0">First vector span.</param>
        /// <param name="v1">Second vector span.</param>
        /// <param name="v2">Third vector span.</param>
        /// <param name="v3">Fourth vector span.</param>
        /// <param name="mem">Destination interleaved buffer of length at least <c>4 * v0.Length</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreInterleaved<T>(
            ReadOnlySpan<T> v0,
            ReadOnlySpan<T> v1,
            ReadOnlySpan<T> v2,
            ReadOnlySpan<T> v3,
            Span<T> mem)
            where T : unmanaged
        {
            int lanes = v0.Length;

            for (int i = 0; i < lanes; i++)
            {
                int baseIdx      = 4 * i;
                mem[baseIdx]     = v0[i];
                mem[baseIdx + 1] = v1[i];
                mem[baseIdx + 2] = v2[i];
                mem[baseIdx + 3] = v3[i];
            }
        }

        /// <summary>
        /// Stores 8 vectors <c>v0[0]..v7[0], v0[1]..v7[1], ...</c> interleaved to <paramref name="mem"/>.
        /// </summary>
        /// <param name="v0123">Concatenated first 4 vectors (each of length <paramref name="lanes"/>).</param>
        /// <param name="v4567">Concatenated second 4 vectors (each of length <paramref name="lanes"/>).</param>
        /// <param name="lanes">Number of lanes per vector.</param>
        /// <param name="mem">Destination buffer of length at least <c>8 * lanes</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreInterleaved8(
            ReadOnlySpan<float> v0123,
            ReadOnlySpan<float> v4567,
            int lanes,
            Span<float> mem)
        {
            for (int i = 0; i < lanes; i++)
            {
                int baseIdx      = 8 * i;
                mem[baseIdx]     = v0123[i];
                mem[baseIdx + 1] = v0123[lanes + i];
                mem[baseIdx + 2] = v0123[(2 * lanes) + i];
                mem[baseIdx + 3] = v0123[(3 * lanes) + i];
                mem[baseIdx + 4] = v4567[i];
                mem[baseIdx + 5] = v4567[lanes + i];
                mem[baseIdx + 6] = v4567[(2 * lanes) + i];
                mem[baseIdx + 7] = v4567[(3 * lanes) + i];
            }
        }

        /// <summary>
        /// Transposes an 8x8 block of 32-bit integers.
        /// </summary>
        /// <param name="from">Source buffer with stride <paramref name="fromStride"/>.</param>
        /// <param name="to">Destination 8x8 block (stride 8).</param>
        /// <param name="fromStride">Row stride of <paramref name="from"/> in elements.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Transpose8x8Block(ReadOnlySpan<int> from, Span<int> to, nuint fromStride)
        {
            int stride = (int)fromStride;

            for (int n = 0; n < 8; ++n)
            {
                int rowOffset = n * stride;

                for (int m = 0; m < 8; ++m)
                {
                    to[(m * 8) + n] = from[rowOffset + m];
                }
            }
        }
    }
}
