using System;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Tools.Tests
{
    /// <summary>Checks the eight-column recurrence against independent scalar columns.</summary>
    [TestFixture]
    public sealed class GaussBlurSimdTests
    {
        /// <summary>Short images, all strip tails and top/bottom zero padding keep scalar rounding.</summary>
        [TestCase(1, 1)]
        [TestCase(7, 3)]
        [TestCase(8, 7)]
        [TestCase(9, 9)]
        [TestCase(15, 17)]
        [TestCase(16, 19)]
        [TestCase(17, 23)]
        [TestCase(33, 31)]
        public void VerticalRecurrenceMatchesScalar(int width, int height)
        {
            using ImagePlane<float> input = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> temp = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> actual = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            Random random = new Random(123);
            for (nuint y = 0; y < (nuint)height; y++)
                for (int x = 0; x < width; x++) input.Row(y)[x] = (float)(random.NextDouble() * 2 - 1);
            foreach (double sigma in new[] { .5, 2.7, 7.155933 })
            {
                RecursiveGaussian rg = GaussBlur.CreateRecursiveGaussian(sigma);
                Assert.That(GaussBlur.FastGaussian(JxlMemoryManager.Default, rg, input, temp, actual).IsOk, Is.True);
                for (int x = 0; x < width; x++)
                {
                    float[,] states = new float[3, 4];
                    int radius = (int)rg.Radius;
                    for (int n = -radius + 1; n < height; n++)
                    {
                        int bottom = n + radius - 1, top = n - radius - 1;
                        float sum = bottom < height ? temp.ConstRow((nuint)bottom)[x] : 0f;
                        if (n >= radius + 1) sum = temp.ConstRow((nuint)top)[x] + sum;
                        int i0 = n & 3, i1 = (n - 1) & 3, i2 = (n - 2) & 3;
                        for (int harmonic = 0; harmonic < 3; harmonic++)
                            states[harmonic, i0] = rg.N2[harmonic * 4] * sum - rg.D1[harmonic * 4] * states[harmonic, i1] - states[harmonic, i2];
                        if (n >= 0)
                        {
                            float expected = states[0, i0] + (states[1, i0] + states[2, i0]);
                            Assert.That(BitConverter.SingleToInt32Bits(actual.ConstRow((nuint)n)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"sigma={sigma}, {width}x{height}, x={x}, y={n}");
                        }
                    }
                }
            }
        }
    }
}
