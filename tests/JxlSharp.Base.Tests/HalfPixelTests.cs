using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NUnit.Framework;

namespace JxlSharp.Base.Tests
{
    /// <summary>Bit-exact half conversion, including every binary16 encoding and rounding ties.</summary>
    [TestFixture]
    public sealed class HalfPixelTests
    {
        /// <summary>All half values preserve the scalar conversion's zero signs and NaN payloads.</summary>
        [Test]
        public void EveryHalfEncodingMatchesScalar()
        {
            if (!Avx2.IsSupported) Assert.Ignore("Requires AVX2");
            Span<uint> input = stackalloc uint[8];
            Span<float> output = stackalloc float[8];
            for (int start = 0; start < 65536; start += 8)
            {
                for (int i = 0; i < 8; i++) input[i] = (uint)(start + i);
                PixelPacking.HalfToFloat(Vector256.Create(input)).CopyTo(output);
                for (int i = 0; i < 8; i++)
                {
                    float scalar = (float)BitConverter.UInt16BitsToHalf((ushort)input[i]);
                    Assert.That(BitConverter.SingleToUInt32Bits(output[i]), Is.EqualTo(BitConverter.SingleToUInt32Bits(scalar)), $"half={input[i]:x4}");
                }
            }
        }

        /// <summary>Check float encodings around each half rounding boundary plus random full-range floats.</summary>
        [Test]
        public void FloatRoundingMatchesScalar()
        {
            if (!Avx2.IsSupported) Assert.Ignore("Requires AVX2");
            Span<float> input = stackalloc float[8];
            Span<ushort> output = stackalloc ushort[8];
            Random random = new Random(123);
            for (int half = 0; half < 0x7c00; half++)
            {
                float lo = (float)BitConverter.UInt16BitsToHalf((ushort)half);
                float hi = half == 0x7bff ? 65536f : (float)BitConverter.UInt16BitsToHalf((ushort)(half + 1));
                float middle = (lo + hi) * 0.5f;
                input[0] = lo; input[1] = middle; input[2] = MathF.BitDecrement(middle); input[3] = MathF.BitIncrement(middle);
                for (int i = 0; i < 4; i++) input[i + 4] = -input[i];
                Check(input, output);
            }
            for (int trial = 0; trial < 50000; trial++)
            {
                for (int i = 0; i < 8; i++) input[i] = BitConverter.UInt32BitsToSingle((uint)random.NextInt64(0, 1L << 32));
                Check(input, output);
            }

            static void Check(ReadOnlySpan<float> input, Span<ushort> output)
            {
                PixelPacking.FloatToHalf(Vector256.Create(input)).CopyTo(output);
                for (int i = 0; i < 8; i++)
                    Assert.That(output[i], Is.EqualTo(BitConverter.HalfToUInt16Bits((Half)input[i])), $"float={BitConverter.SingleToUInt32Bits(input[i]):x8}");
            }
        }
    }
}
