// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SimdUtil"/> (ported from <c>lib/jxl/simd_util_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class SimdUtilTests
    {
        /// <summary>
        /// Verifies <see cref="SimdUtil.StoreInterleaved{T}(ReadOnlySpan{T}, ReadOnlySpan{T}, Span{T})"/>
        /// matching <c>TestInterleave2</c> in <c>simd_util_test.cc</c>.
        /// </summary>
        [Test]
        public void TestInterleave2()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            int lanes                      = (int)(SimdUtil.MaxVectorSize() / sizeof(float));

            StatusOr<AlignedMemory> memResult = AlignedMemory.Create(memoryManager, (nuint)(lanes * 2 * sizeof(float)));
            memResult.IsOk.ShouldBeTrue();

            using AlignedMemory mem = memResult.Value;
            float[] vec1            = new float[lanes];
            float[] vec2            = new float[lanes];

            for (int i = 0; i < lanes; i++)
            {
                vec1[i] = (0 * 128.0f) + i;
                vec2[i] = (1 * 128.0f) + i;
            }

            Span<float> output = mem.GetSpan<float>(lanes * 2);
            SimdUtil.StoreInterleaved<float>(vec1, vec2, output);

            for (int i = 0; i < lanes; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    output[(2 * i) + j].ShouldBe((j * 128.0f) + i);
                }
            }
        }

        /// <summary>
        /// Verifies 4-way <see cref="SimdUtil.StoreInterleaved{T}(ReadOnlySpan{T}, ReadOnlySpan{T}, ReadOnlySpan{T}, ReadOnlySpan{T}, Span{T})"/>
        /// matching <c>TestInterleave4</c> in <c>simd_util_test.cc</c>.
        /// </summary>
        [Test]
        public void TestInterleave4()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            int lanes                      = (int)(SimdUtil.MaxVectorSize() / sizeof(float));

            StatusOr<AlignedMemory> memResult = AlignedMemory.Create(memoryManager, (nuint)(lanes * 4 * sizeof(float)));
            memResult.IsOk.ShouldBeTrue();

            using AlignedMemory mem = memResult.Value;
            float[] vec1            = new float[lanes];
            float[] vec2            = new float[lanes];
            float[] vec3            = new float[lanes];
            float[] vec4            = new float[lanes];

            for (int i = 0; i < lanes; i++)
            {
                vec1[i] = (0 * 128.0f) + i;
                vec2[i] = (1 * 128.0f) + i;
                vec3[i] = (2 * 128.0f) + i;
                vec4[i] = (3 * 128.0f) + i;
            }

            Span<float> output = mem.GetSpan<float>(lanes * 4);
            SimdUtil.StoreInterleaved<float>(vec1, vec2, vec3, vec4, output);

            for (int i = 0; i < lanes; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    output[(4 * i) + j].ShouldBe((j * 128.0f) + i);
                }
            }
        }

        /// <summary>
        /// Verifies 8-way <see cref="SimdUtil.StoreInterleaved8"/>
        /// matching <c>TestInterleave8</c> in <c>simd_util_test.cc</c>.
        /// </summary>
        [Test]
        public void TestInterleave8()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            int lanes                      = (int)(SimdUtil.MaxVectorSize() / sizeof(float));

            StatusOr<AlignedMemory> memResult = AlignedMemory.Create(memoryManager, (nuint)(lanes * 8 * sizeof(float)));
            memResult.IsOk.ShouldBeTrue();

            using AlignedMemory mem = memResult.Value;
            float[] v0123           = new float[lanes * 4];
            float[] v4567           = new float[lanes * 4];

            for (int v = 0; v < 4; v++)
            {
                for (int i = 0; i < lanes; i++)
                {
                    v0123[(v * lanes) + i] = (v * 128.0f) + i;
                    v4567[(v * lanes) + i] = ((v + 4) * 128.0f) + i;
                }
            }

            Span<float> output = mem.GetSpan<float>(lanes * 8);
            SimdUtil.StoreInterleaved8(v0123, v4567, lanes, output);

            for (int i = 0; i < lanes; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    output[(8 * i) + j].ShouldBe((j * 128.0f) + i);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="SimdUtil.MaxValue(ReadOnlySpan{uint})"/> and <see cref="SimdUtil.Transpose8x8Block"/>.
        /// </summary>
        [Test]
        public void MaxValueAndTranspose8x8BlockWork()
        {
            uint[] values = new uint[37];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (uint)((i * 17) % 100);
            }

            values[23] = 9999u;
            values[36] = 12345u;

            SimdUtil.MaxValue(values).ShouldBe(12345u);
            SimdUtil.MaxValue(values, (nuint)values.Length).ShouldBe(12345u);
            SimdUtil.MaxValue(values, 0).ShouldBe(0u);

            int[] block      = new int[64];
            int[] transposed = new int[64];

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    block[(r * 8) + c] = (r * 10) + c;
                }
            }

            SimdUtil.Transpose8x8Block(block, transposed, 8);

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    transposed[(c * 8) + r].ShouldBe((r * 10) + c);
                }
            }
        }
    }
}
