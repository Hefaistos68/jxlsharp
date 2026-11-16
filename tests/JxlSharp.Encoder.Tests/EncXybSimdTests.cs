using System;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Verifies the eight-pixel XYB path and scalar tails.</summary>
    [TestFixture]
    public sealed class EncXybSimdTests
    {
        /// <summary>Vector conversion must preserve scalar results and surrounding memory.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(257)]
        public void RowsMatchScalar(int width)
        {
            foreach (int matrixSize in new[] { 12, 15 })
            foreach (float intensity in new[] { 80f, 255f, 1000f, 10000f })
            {
                float[] matrix = new float[matrixSize];
                EncXyb.ComputePremulAbsorb(intensity, matrix);
                float[][] actual = new float[3][];
                float[][] expected = new float[3][];
                for (int c = 0; c < 3; c++)
                {
                    actual[c] = new float[width + 2];
                    actual[c][0] = actual[c][width + 1] = -12345f;
                    float[] samples = { 0f, -0f, -1f, -0.01f, 0.00001f, 0.5f, 1f, 4f };
                    for (int x = 0; x < width; x++) actual[c][x + 1] = samples[(x + c * 3) % samples.Length];
                    expected[c] = (float[])actual[c].Clone();
                }
                EncXyb.LinearRGBRowToXYB(actual[0].AsSpan(1, width), actual[1].AsSpan(1, width), actual[2].AsSpan(1, width), matrix, (nuint)width);
                for (int x = 1; x <= width; x++)
                    EncXyb.LinearRGBRowToXYB(expected[0].AsSpan(x, 1), expected[1].AsSpan(x, 1), expected[2].AsSpan(x, 1), matrix, 1);
                for (int c = 0; c < 3; c++)
                    for (int x = 0; x < width + 2; x++)
                        Assert.That(BitConverter.SingleToInt32Bits(actual[c][x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected[c][x])), $"width={width}, matrix={matrixSize}, intensity={intensity}, c={c}, x={x}");
            }
        }

        /// <summary>Checks exponent initialization, zero, and Newton iterations across a wide input range.</summary>
        [Test]
        public void CubeRootMatchesScalarBits()
        {
            for (int exponent = -126; exponent <= 120; exponent++)
            {
                float[] values = new float[8];
                for (int lane = 0; lane < 8; lane++) values[lane] = lane == 0 ? 0f : MathF.ScaleB(0.5f + lane / 16f, exponent);
                Vector256<float> result = FastMath.CubeRootAndAdd(Vector256.Create(values), Vector256.Create(-0.02f));
                for (int lane = 0; lane < 8; lane++)
                    Assert.That(BitConverter.SingleToInt32Bits(result.GetElement(lane)), Is.EqualTo(BitConverter.SingleToInt32Bits(FastMath.CubeRootAndAdd(values[lane], -0.02f))));
            }
        }
    }
}
