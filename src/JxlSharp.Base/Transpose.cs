// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace JxlSharp.Base
{
    /// <summary>
    /// Block transpose for DCT/IDCT (ported from <c>lib/jxl/transpose-inl.h</c>).
    /// </summary>
    public static class Transpose
    {
        /// <summary>
        /// Transposes a <paramref name="rows"/> x <paramref name="cols"/> block from <paramref name="from"/>
        /// (with row stride <paramref name="fromStride"/>) into a <paramref name="cols"/> x <paramref name="rows"/>
        /// block in <paramref name="to"/> (with row stride <paramref name="toStride"/>).
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="from">Source block buffer.</param>
        /// <param name="fromStride">Source row stride in elements.</param>
        /// <param name="to">Destination block buffer.</param>
        /// <param name="toStride">Destination row stride in elements.</param>
        /// <param name="rows">Number of rows in source block.</param>
        /// <param name="cols">Number of columns in source block.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenericTransposeBlock<T>(
            ReadOnlySpan<T> from,
            int fromStride,
            Span<T> to,
            int toStride,
            int rows,
            int cols)
            where T : unmanaged
        {
            // Tile independent 32-bit blocks; retain scalar ordering for overlapping spans.
            if (Avx.IsSupported && Unsafe.SizeOf<T>() == 4 && rows >= 8 && cols >= 8 &&
                fromStride >= cols && toStride >= rows && !from.Overlaps(to))
            {
                ReadOnlySpan<float> source = MemoryMarshal.Cast<T, float>(from);
                Span<float> destination = MemoryMarshal.Cast<T, float>(to);
                int fullRows = rows & ~7;
                int fullCols = cols & ~7;
                for (int y = 0; y < fullRows; y += 8)
                {
                    for (int x = 0; x < fullCols; x += 8)
                        Transpose8x8(source.Slice(y * fromStride + x), fromStride,
                            destination.Slice(x * toStride + y), toStride);
                    for (int x = fullCols; x < cols; x++)
                        for (int lane = 0; lane < 8; lane++)
                            to[x * toStride + y + lane] = from[(y + lane) * fromStride + x];
                }
                for (int y = fullRows; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                        to[x * toStride + y] = from[y * fromStride + x];
                return;
            }

            for (int n = 0; n < rows; ++n)
            {
                int srcRow = n * fromStride;

                for (int m = 0; m < cols; ++m)
                {
                    to[(m * toStride) + n] = from[srcRow + m];
                }
            }
        }

        /// <summary>
        /// Transposes a <paramref name="rows"/> x <paramref name="cols"/> block from <paramref name="from"/>
        /// into <paramref name="to"/> using unsigned native strides and dimensions.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="from">Source block span.</param>
        /// <param name="fromStride">Source row stride in elements.</param>
        /// <param name="to">Destination block span.</param>
        /// <param name="toStride">Destination row stride in elements.</param>
        /// <param name="rows">Number of rows in source block.</param>
        /// <param name="cols">Number of columns in source block.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenericTransposeBlock<T>(
            ReadOnlySpan<T> from,
            nuint fromStride,
            Span<T> to,
            nuint toStride,
            nuint rows,
            nuint cols)
            where T : unmanaged
        {
            GenericTransposeBlock(from, (int)fromStride, to, (int)toStride, (int)rows, (int)cols);
        }
        /// <summary>Transposes an 8x8 block of floats with arbitrary row strides.</summary>
        /// <param name="from">Source block.</param>
        /// <param name="fromStride">Source stride in elements.</param>
        /// <param name="to">Destination block.</param>
        /// <param name="toStride">Destination stride in elements.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Transpose8x8(ReadOnlySpan<float> from, int fromStride, Span<float> to, int toStride)
        {
            if (!Avx.IsSupported)
            {
                // Snapshot the tile before writing, matching the register path even in-place.
                Span<float> tile = stackalloc float[64];
                for (int row = 0; row < 8; row++)
                    from.Slice(row * fromStride, 8).CopyTo(tile.Slice(row * 8, 8));
                for (int row = 0; row < 8; row++)
                    for (int col = 0; col < 8; col++)
                        to[col * toStride + row] = tile[row * 8 + col];
                return;
            }
            Vector256<float> r0 = Vector256.Create(from.Slice(0 * fromStride, 8));
            Vector256<float> r1 = Vector256.Create(from.Slice(1 * fromStride, 8));
            Vector256<float> r2 = Vector256.Create(from.Slice(2 * fromStride, 8));
            Vector256<float> r3 = Vector256.Create(from.Slice(3 * fromStride, 8));
            Vector256<float> r4 = Vector256.Create(from.Slice(4 * fromStride, 8));
            Vector256<float> r5 = Vector256.Create(from.Slice(5 * fromStride, 8));
            Vector256<float> r6 = Vector256.Create(from.Slice(6 * fromStride, 8));
            Vector256<float> r7 = Vector256.Create(from.Slice(7 * fromStride, 8));
            Vector256<float> t0 = Avx.UnpackLow(r0, r1);
            Vector256<float> t1 = Avx.UnpackHigh(r0, r1);
            Vector256<float> t2 = Avx.UnpackLow(r2, r3);
            Vector256<float> t3 = Avx.UnpackHigh(r2, r3);
            Vector256<float> t4 = Avx.UnpackLow(r4, r5);
            Vector256<float> t5 = Avx.UnpackHigh(r4, r5);
            Vector256<float> t6 = Avx.UnpackLow(r6, r7);
            Vector256<float> t7 = Avx.UnpackHigh(r6, r7);
            Vector256<float> u0 = Avx.Shuffle(t0, t2, 0x44);
            Vector256<float> u1 = Avx.Shuffle(t0, t2, 0xee);
            Vector256<float> u2 = Avx.Shuffle(t1, t3, 0x44);
            Vector256<float> u3 = Avx.Shuffle(t1, t3, 0xee);
            Vector256<float> u4 = Avx.Shuffle(t4, t6, 0x44);
            Vector256<float> u5 = Avx.Shuffle(t4, t6, 0xee);
            Vector256<float> u6 = Avx.Shuffle(t5, t7, 0x44);
            Vector256<float> u7 = Avx.Shuffle(t5, t7, 0xee);
            Avx.Permute2x128(u0, u4, 0x20).CopyTo(to.Slice(0 * toStride, 8));
            Avx.Permute2x128(u1, u5, 0x20).CopyTo(to.Slice(1 * toStride, 8));
            Avx.Permute2x128(u2, u6, 0x20).CopyTo(to.Slice(2 * toStride, 8));
            Avx.Permute2x128(u3, u7, 0x20).CopyTo(to.Slice(3 * toStride, 8));
            Avx.Permute2x128(u0, u4, 0x31).CopyTo(to.Slice(4 * toStride, 8));
            Avx.Permute2x128(u1, u5, 0x31).CopyTo(to.Slice(5 * toStride, 8));
            Avx.Permute2x128(u2, u6, 0x31).CopyTo(to.Slice(6 * toStride, 8));
            Avx.Permute2x128(u3, u7, 0x31).CopyTo(to.Slice(7 * toStride, 8));
        }
    }
}
