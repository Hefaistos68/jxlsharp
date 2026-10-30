using System;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks the vector convolution interior against the scalar tap-by-tap definition.</summary>
    [TestFixture]
    public sealed class ConvolveSimdTests
    {
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
