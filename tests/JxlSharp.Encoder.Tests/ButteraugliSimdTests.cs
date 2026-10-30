using System;
using System.Reflection;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Compares SIMD interiors with the retained scalar border and pixel kernels.</summary>
    [TestFixture]
    public sealed class ButteraugliSimdTests
    {
        private static T Private<T>(string name) where T : Delegate => typeof(Butteraugli).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();

        /// <summary>Malta directional sums, asymmetric double thresholds and erosion retain scalar rounding.</summary>
        [TestCase(1, 1)]
        [TestCase(7, 9)]
        [TestCase(8, 8)]
        [TestCase(9, 9)]
        [TestCase(15, 11)]
        [TestCase(16, 9)]
        [TestCase(17, 17)]
        [TestCase(33, 19)]
        public void MaltaAndMaskMatchScalar(int width, int height)
        {
            using ImagePlane<float> a = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> b = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> diffs = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> actual = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            Random random = new Random(456);
            for (nuint y = 0; y < (nuint)height; y++)
                for (int x = 0; x < width; x++)
                {
                    a.Row(y)[x] = (float)(random.NextDouble() * 8 - 4);
                    b.Row(y)[x] = (float)(random.NextDouble() * 8 - 4);
                }
            var difference = Private<Func<float, float, float, float, float, float>>("ComputeSingleMaltaDiff");
            var malta = Private<Func<bool, ImagePlane<float>, nuint, nuint, float>>("PaddedMaltaUnit");
            foreach (bool lf in new[] { false, true })
            {
                ImageOps.FillImage(.125f, actual);
                var status = lf ? Butteraugli.MaltaDiffMapLF(a, b, .7, 1.3, .2, diffs, actual) : Butteraugli.MaltaDiffMap(a, b, .7, 1.3, .2, diffs, actual);
                Assert.That(status.IsOk, Is.True);
                double mul = lf ? .611612573796 : .39905817637;
                float gt = (float)(mul * Math.Sqrt(.5 * .7) / 8.5 * .2), lt = (float)(mul * Math.Sqrt(.33 * 1.3) / 8.5 * .2);
                for (nuint y = 0; y < (nuint)height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float expectedDiff = difference(a.ConstRow(y)[x], b.ConstRow(y)[x], .2f, gt, lt);
                        Assert.That(BitConverter.SingleToInt32Bits(diffs.ConstRow(y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedDiff)), $"diff {x},{y}");
                        float expected = .125f + malta(lf, diffs, (nuint)x, y);
                        Assert.That(BitConverter.SingleToInt32Bits(actual.ConstRow(y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"Malta {x},{y}");
                    }
            }
            // Duplicate and negative values exercise the strict sorting comparisons.
            for (nuint y = 0; y < (nuint)height; y++)
                for (int x = 0; x < width; x++) a.Row(y)[x] = (x + (int)y) % 4 - 2;
            var erode = Private<Action<ImagePlane<float>, ImagePlane<float>>>("FuzzyErosion");
            var pixel = Private<Func<ImagePlane<float>, nuint, nuint, nuint, nuint, float>>("FuzzyErodePixel");
            erode(a, actual);
            for (nuint y = 0; y < (nuint)height; y++)
                for (int x = 0; x < width; x++)
                    Assert.That(BitConverter.SingleToInt32Bits(actual.ConstRow(y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(pixel(a, (nuint)x, y, (nuint)width, (nuint)height))));
        }

        /// <summary>Transpose tiles and short kernels retain normalized border handling.</summary>
        [TestCase(1, 1)]
        [TestCase(7, 9)]
        [TestCase(8, 8)]
        [TestCase(9, 17)]
        [TestCase(17, 9)]
        [TestCase(33, 19)]
        public void ConvolutionTransposeMatchesScalar(int width, int height)
        {
            using ImagePlane<float> input = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, (nuint)height).Value;
            using ImagePlane<float> output = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)height, (nuint)width).Value;
            Random random = new Random(789);
            for (nuint y = 0; y < (nuint)height; y++)
                for (int x = 0; x < width; x++) input.Row(y)[x] = (float)(random.NextDouble() * 4 - 2);
            var convolve = Private<Func<ImagePlane<float>, float[], ImagePlane<float>, Status>>("ConvolutionWithTranspose");
            foreach (float sigma in new[] { .5f, 1.5f, 2.7f, 7.1f })
            {
                float[] kernel = Butteraugli.ComputeKernel(sigma);
                Assert.That(convolve(input, kernel, output).IsOk, Is.True);
                int half = kernel.Length / 2;
                float allWeights = 0; foreach (float w in kernel) allWeights += w;
                float[] scaled = new float[half + 1];
                for (int i = 0; i <= half; i++) scaled[i] = kernel[i] * (1f / allWeights);
                for (int x = 0; x < width; x++)
                    for (nuint y = 0; y < (nuint)height; y++)
                    {
                        ReadOnlySpan<float> row = input.ConstRow(y);
                        float expected;
                        if (x >= half && x < width - half)
                        {
                            expected = row[x] * scaled[half];
                            for (int i = 0; i < half; i++) expected += (row[x - half + i] + row[x + half - i]) * scaled[i];
                        }
                        else
                        {
                            int lo = Math.Max(0, x - half), hi = Math.Min(width - 1, x + half);
                            float weight = 0, sum = 0;
                            for (int i = lo; i <= hi; i++) weight += kernel[i - x + half];
                            for (int i = lo; i <= hi; i++) sum += row[i] * kernel[i - x + half];
                            expected = sum * (1f / weight);
                        }
                        Assert.That(BitConverter.SingleToInt32Bits(output.ConstRow((nuint)x)[(int)y]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"sigma={sigma}, x={x}, y={y}");
                    }
            }
        }
    }
}
