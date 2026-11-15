using System;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks the vector convolution interior against the scalar tap-by-tap definition.</summary>
    [TestFixture]
    public sealed class ConvolveSimdTests
    {
        /// <summary>Symmetric filtering keeps the scalar order at borders, crops and SIMD tails.</summary>
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(7, 3)]
        [TestCase(8, 7)]
        [TestCase(9, 9)]
        [TestCase(12, 8)]
        [TestCase(17, 13)]
        [TestCase(33, 19)]
        [TestCase(257, 25)]
        public void Symmetric5MatchesScalar(int width, int height)
        {
            foreach (int crop in new[] { 0, 1, 3 })
            {
                using var source = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)(width + crop), (nuint)(height + crop)).Value;
                using var output = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)(width + 4), (nuint)(height + 4)).Value;
                ImageOps.FillImage(-12345f, output);
                var random = new Random(651);
                for (nuint y = 0; y < source.YSize; y++)
                    for (int x = 0; x < (int)source.XSize; x++)
                        source.Row(y)[x] = (float)(random.NextDouble() * 400 - 200);
                var weights = new WeightsSymmetric5(1.3f, -.11f, -.07f, .013f, .0041f, -.0027f);
                var inputRect = new Rect(crop, crop, width, height);
                var outputRect = new Rect(1, 2, width, height);
                Assert.That(Convolve.Symmetric5(source, inputRect, weights, null, output, outputRect).IsOk, Is.True);
                for (nuint y = 0; y < output.YSize; y++)
                    for (int x = 0; x < (int)output.XSize; x++)
                    {
                        float expected = -12345f;
                        if (x >= 1 && x < 1 + width && y >= 2 && y < (nuint)(2 + height))
                            expected = SymmetricReference(source, crop + x - 1, crop + (int)y - 2, weights);
                        Assert.That(BitConverter.SingleToInt32Bits(output.ConstRow(y)[x]),
                            Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"{width}x{height}, crop={crop}, x={x}, y={y}");
                    }
            }
        }

        /// <summary>In-place calls retain the existing sequential update semantics.</summary>
        [Test]
        public void Symmetric5InPlaceKeepsScalarOrder()
        {
            using var actual = ImagePlane<float>.Create(JxlMemoryManager.Default, 17, 7).Value;
            using var expected = ImagePlane<float>.Create(JxlMemoryManager.Default, 17, 7).Value;
            for (nuint y = 0; y < 7; y++)
                for (int x = 0; x < 17; x++) actual.Row(y)[x] = expected.Row(y)[x] = x * .13f + (float)y;
            var weights = new WeightsSymmetric5(.6f, .04f, .015f, .01f, .002f, .003f);
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 17; x++) expected.Row((nuint)y)[x] = SymmetricReference(expected, x, y, weights);
            Assert.That(Convolve.Symmetric5(actual, new Rect(actual), weights, null, actual, new Rect(actual)).IsOk, Is.True);
            for (nuint y = 0; y < 7; y++)
                for (int x = 0; x < 17; x++)
                    Assert.That(BitConverter.SingleToInt32Bits(actual.ConstRow(y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.ConstRow(y)[x])));
        }

        private static float SymmetricReference(ImagePlane<float> image, int x, int y, WeightsSymmetric5 w)
        {
            float Horizontal(int yy, float w0, float w1, float w2)
            {
                ReadOnlySpan<float> row = image.ConstRow((nuint)Reflect(yy, (int)image.YSize));
                float outer = w2 * (row[Reflect(x - 2, (int)image.XSize)] + row[Reflect(x + 2, (int)image.XSize)]);
                float inner = w1 * (row[Reflect(x - 1, (int)image.XSize)] + row[Reflect(x + 1, (int)image.XSize)]);
                return outer + (inner + w0 * row[x]);
            }
            float a = Horizontal(y, w.C[0], w.r[0], w.R[0]);
            float b = Horizontal(y + 2, w.R[0], w.L[0], w.D[0]);
            a += Horizontal(y - 2, w.R[0], w.L[0], w.D[0]);
            a += Horizontal(y - 1, w.r[0], w.d[0], w.L[0]);
            b += Horizontal(y + 1, w.r[0], w.d[0], w.L[0]);
            return a + b;
        }

        private static int Reflect(int value, int size)
        {
            while (value < 0 || value >= size) value = value < 0 ? -value - 1 : 2 * size - value - 1;
            return value;
        }

        /// <summary>Global mirrored boundaries, cropped rectangles and untouched output regions.</summary>
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(7, 3)]
        [TestCase(8, 7)]
        [TestCase(9, 9)]
        [TestCase(12, 8)]
        [TestCase(17, 13)]
        [TestCase(33, 19)]
        public void Separable5MatchesScalar(int width, int height)
        {
            foreach (int crop in new[] { 0, 1, 3 })
            {
                using ImagePlane<float> source = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)(width + crop), (nuint)(height + crop)).Value;
                using ImagePlane<float> output = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)(width + 4), (nuint)(height + 4)).Value;
                ImageOps.FillImage(-12345f, output);
                Random random = new Random(123);
                for (nuint y = 0; y < source.YSize; y++)
                    for (int x = 0; x < (int)source.XSize; x++) source.Row(y)[x] = (float)(random.NextDouble() * 4 - 2);
                WeightsSeparable5 weights = new WeightsSeparable5(.6f, .15f, .05f, .5f, .2f, .05f);
                Rect inputRect = new Rect(crop, crop, width, height), outputRect = new Rect(1, 2, width, height);
                Assert.That(Convolve.SlowSeparable5(source, inputRect, weights, null, output, outputRect).IsOk, Is.True);
                for (nuint y = 0; y < output.YSize; y++)
                    for (int x = 0; x < (int)output.XSize; x++)
                    {
                        float expected = -12345f;
                        if (x >= 1 && x < 1 + width && y >= 2 && y < (nuint)(2 + height))
                        {
                            expected = 0;
                            for (int dy = -2; dy <= 2; dy++)
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    long sy = ImageOps.Mirror(crop + (long)y - 2 + dy, (long)source.YSize);
                                    long sx = ImageOps.Mirror(crop + x - 1 + dx, (long)source.XSize);
                                    expected += source.ConstRow((nuint)sy)[(int)sx] * weights.Horz[Math.Abs(dx) * 4] * weights.Vert[Math.Abs(dy) * 4];
                                }
                        }
                        Assert.That(BitConverter.SingleToInt32Bits(output.ConstRow(y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"{width}x{height}, crop={crop}, x={x}, y={y}");
                    }
            }
        }
    }
}
