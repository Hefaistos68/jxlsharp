// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

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
        public static void GenericTransposeBlock<T>(
            ReadOnlySpan<T> from,
            int fromStride,
            Span<T> to,
            int toStride,
            int rows,
            int cols)
            where T : unmanaged
        {
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
    }
}
