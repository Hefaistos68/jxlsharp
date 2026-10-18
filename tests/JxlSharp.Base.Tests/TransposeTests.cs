// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Transpose"/>.
    /// </summary>
    [TestFixture]
    public class TransposeTests
    {
        /// <summary>
        /// Verifies <see cref="Transpose.GenericTransposeBlock{T}(System.ReadOnlySpan{T}, int, System.Span{T}, int, int, int)"/>
        /// and pointer overload for rectangular and square blocks.
        /// </summary>
        [Test]
        public unsafe void GenericTransposeBlockTransposesCorrectly()
        {
            const int rows = 4;
            const int cols = 8;

            float[] src = new float[rows * cols];
            float[] dst = new float[cols * rows];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    src[(r * cols) + c] = (r * 100.0f) + c;
                }
            }

            Transpose.GenericTransposeBlock<float>(src, cols, dst, rows, rows, cols);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    dst[(c * rows) + r].ShouldBe((r * 100.0f) + c);
                }
            }

            float[] roundtrip = new float[rows * cols];
            fixed (float* pDst = dst)
            fixed (float* pRt = roundtrip)
            {
                Transpose.GenericTransposeBlock(pDst, (nuint)rows, pRt, (nuint)cols, (nuint)cols, (nuint)rows);
            }

            roundtrip.ShouldBe(src);
        }
    }
}
