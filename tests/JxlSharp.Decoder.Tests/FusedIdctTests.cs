using System;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Compare the fused 8x8 inverse with the original sequence of two 1D passes.</summary>
    [TestFixture]
    public sealed class FusedIdctTests
    {
        /// <summary>Preserve pixels, unmutated inputs, strides, offsets and guard sentinels.</summary>
        [Test]
        public void StridedOutputMatchesTwoPassBits()
        {
            var random = new Random(183);

            foreach (int stride in new[] { 8, 9, 17, 257 })
            {
                for (int pattern = 0; pattern < 68; pattern++)
                {
                    float[] from   = new float[70];
                    float[] actual = new float[7 * stride + 14];

                    Array.Fill(from, -12345f);
                    Array.Fill(actual, -12345f);

                    for (int k = 0; k < 64; k++)
                    {
                        from[3 + k] = pattern < 64 ? (k == pattern ? -.7f : 0f) : pattern switch
                        {
                            64 => (float)(random.NextDouble() * 2000 - 1000),
                            65 => k % 3 == 0 ? -0f : 0f,
                            66 => k % 2 == 0 ? float.Epsilon : -float.Epsilon,
                            _ => k % 13 == 0 ? (float)(random.NextDouble() - .5) : 0,
                        };
                    }

                    float[] originalFrom  = (float[])from.Clone();
                    float[] referenceFrom = (float[])from.Clone();
                    float[] expected      = (float[])actual.Clone();

                    Reference(referenceFrom.AsSpan(3, 64), new DctTo(expected.AsSpan(3), (nuint)stride), new float[256]);
                    DecTransforms.ComputeScaledIDCT(8, 8, from.AsSpan(3, 64),
                        new DctTo(actual.AsSpan(3), (nuint)stride), new float[256]);

                    if (Avx.IsSupported)
                    {
                        AssertBitEqual(from, originalFrom);
                    }

                    AssertStridedOutput(actual, expected, 3, stride);
                }
            }
        }

        /// <summary>Overlapping inputs, outputs and scratch buffers write only the 8x8 output elements accurately.</summary>
        [TestCase(3, 128)]
        [TestCase(7, 128)]
        [TestCase(128, 3)]
        public void AliasingPreservesGeneralPath(int outputOffset, int scratchOffset)
        {
            float[] actual = new float[512];
            var random     = new Random(391);

            for (int i = 0; i < actual.Length; i++)
            {
                actual[i] = (float)(random.NextDouble() - .5);
            }

            float[] initial  = (float[])actual.Clone();
            float[] expected = (float[])actual.Clone();

            Reference(expected.AsSpan(3, 64), new DctTo(expected.AsSpan(outputOffset, 71), 9), expected.AsSpan(scratchOffset, 256));
            DecTransforms.ComputeScaledIDCT(8, 8, actual.AsSpan(3, 64),
                new DctTo(actual.AsSpan(outputOffset, 71), 9), actual.AsSpan(scratchOffset, 256));

            if (Avx.IsSupported)
            {
                AssertStridedOutputWithGuards(actual, expected, initial, outputOffset, 9);
            }
            else
            {
                AssertBitEqual(actual, expected);
            }
        }

        private static void Reference(Span<float> from, DctTo to, Span<float> scratch)
        {
            DecTransforms.Idct1D(8, 8, new DctFrom(from, 8), new DctTo(scratch, 8), scratch.Slice(64));
            DctTranspose.Run(8, 8, new DctFrom(scratch, 8), new DctTo(from, 8));
            DecTransforms.Idct1D(8, 8, new DctFrom(from, 8), to, scratch.Slice(64));
        }

        private static void AssertBitEqual(float[] actual, float[] expected)
        {
            for (int i = 0; i < actual.Length; i++)
            {
                BitConverter.SingleToInt32Bits(actual[i]).ShouldBe(
                    BitConverter.SingleToInt32Bits(expected[i]),
                    $"element={i}");
            }
        }

        private static void AssertStridedOutput(float[] actual, float[] expected, int offset, int stride)
        {
            for (int i = 0; i < actual.Length; i++)
            {
                if (IsInBlock8x8(i, offset, stride))
                {
                    actual[i].ShouldBe(expected[i], 1e-3f, $"output={i}");
                }
                else
                {
                    BitConverter.SingleToInt32Bits(actual[i]).ShouldBe(
                        BitConverter.SingleToInt32Bits(-12345f),
                        $"guard={i}");
                }
            }
        }

        private static void AssertStridedOutputWithGuards(
            float[] actual,
            float[] expected,
            float[] initial,
            int offset,
            int stride)
        {
            for (int i = 0; i < actual.Length; i++)
            {
                if (IsInBlock8x8(i, offset, stride))
                {
                    actual[i].ShouldBe(expected[i], 1e-3f, $"output={i}");
                }
                else
                {
                    BitConverter.SingleToInt32Bits(actual[i]).ShouldBe(
                        BitConverter.SingleToInt32Bits(initial[i]),
                        $"guard={i}");
                }
            }
        }

        private static bool IsInBlock8x8(int index, int offset, int stride)
        {
            int rel = index - offset;

            if (rel < 0)
            {
                return false;
            }

            int row = rel / stride;
            int col = rel % stride;

            return row < 8 && col < 8;
        }
    }
}
