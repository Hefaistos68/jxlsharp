using System;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Differential checks of spatial SIMD filters, tails, halos and chunk boundaries.</summary>
    [TestFixture]
    public sealed class SpatialFilterSimdTests
    {
        private const int Padding = 16;

        /// <summary>Every block alignment preserves sigma selection, border weights and skip comparisons.</summary>
        [Test]
        public void SigmaVectorsMatchPerPixelPreparation()
        {
            float[] sigmas = new float[32];
            float[] values = { -10f, -.5f, -2f, 0f, Epf.KMinSigma, float.NaN, float.PositiveInfinity };
            for (int i = 0; i < sigmas.Length; i++) sigmas[i] = values[i % values.Length];
            for (nuint y = 0; y < 16; y++)
            for (nuint firstX = 0; firstX < 32; firstX++)
            foreach (float sm in new[] { .7f, 1.65f, 4f })
            {
                float bsm = sm * .83f;
                var row = new EpfStageHelpers.SigmaRow8(y, firstX, sm, bsm);
                var sigma = row.Load(sigmas, firstX, out var mask);
                var inverse = sigma * row.SadMul;
                for (int lane = 0; lane < 8; lane++)
                {
                    nuint x = firstX + (nuint)lane;
                    float s = sigmas[(int)((x + Epf.KSigmaPadding * FrameDimensions.KBlockDim) / FrameDimensions.KBlockDim)];
                    Assert.That(mask.GetElement(lane), Is.EqualTo(s < Epf.KMinSigma ? -1 : 0));
                    float expected = s * EpfStageHelpers.GetSadMul(y, x % FrameDimensions.KBlockDim, sm, bsm);
                    if (float.IsNaN(expected)) Assert.That(float.IsNaN(inverse.GetElement(lane)), Is.True);
                    else Assert.That(BitConverter.SingleToInt32Bits(inverse.GetElement(lane)),
                        Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));
                }
            }
        }

        /// <summary>Compare full rows with the original four-lane Gaborish and scalar EPF paths.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(17)]
        [TestCase(33)]
        public void FiltersPreserveHaloAndMatchOriginal(int width)
        {
            LoopFilter lf = new LoopFilter { Gab = true, EpfIters = 3 };
            using ImagePlane<float> sigma = ImagePlane<float>.Create(JxlMemoryManager.Default, 32, 8).Value;
            for (nuint y = 0; y < sigma.YSize; y++)
                for (int x = 0; x < (int)sigma.XSize; x++)
                    sigma.Row(y)[x] = x % 3 == 0 ? -10f : x % 3 == 1 ? -0.5f : -2f;
            for (int kind = 0; kind < 4; kind++)
            foreach (int left in new[] { 0, 1, 5 })
            foreach (int right in new[] { 0, 3 })
            foreach (int xpos in new[] { 0, 7, 32, 35 })
            foreach (int ypos in new[] { 0, 3, 7, 8 })
            {
                using RenderPipelineStage stage = kind == 0 ? RenderPipelineStages.GetGaborishStage(lf)!
                    : RenderPipelineStages.GetEPFStage(lf, sigma, (EpfStage)(kind - 1))!;
                // Leave exactly the required right halo after the rounded Gaborish range.
                int length = Padding + (width + right + 3) / 4 * 4 + 3;
                float[][][] input = Input(3, 7, length);
                float[][][] actual = Output(3, 1, length);
                float[][][] expected = Output(3, 1, length);
                Assert.That(stage.ProcessRow(Rows(input, Padding), Rows(actual, Padding), ((nuint)left, (nuint)right), (nuint)width, (nuint)xpos, (nuint)ypos, 0).IsOk, Is.True);
                int start = -((left + 3) / 4 * 4);
                int step = kind == 0 ? 4 : 1;
                for (int x = start; x < width + right; x += step)
                    Assert.That(stage.ProcessRow(Rows(input, Padding + x), Rows(expected, Padding + x), (0, 0), 1, (nuint)(xpos + x), (nuint)ypos, 0).IsOk, Is.True);
                Compare(actual, expected, $"kind={kind}, width={width}, left={left}, right={right}, xpos={xpos}, ypos={ypos}");
            }
        }

        /// <summary>All upsampling factors must match scalar output through both sides of a 1024-pixel chunk.</summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(17)]
        [TestCase(1023)]
        [TestCase(1024)]
        [TestCase(1025)]
        public void UpsamplingMatchesScalarAcrossChunks(int width)
        {
            for (nuint shift = 1; shift <= 3; shift++)
            {
                int n = 1 << (int)shift;
                using RenderPipelineStage stage = RenderPipelineStages.GetUpsamplingStage(JxlMemoryManager.Default, new CustomTransformData(), 0, shift)!;
                Assert.That(stage.PrepareForThreads(1).IsOk, Is.True);
                float[][][] input = Input(1, 5, Padding + width + 2);
                float[][][] actual = Output(1, n, Padding + width * n + 1);
                float[][][] expected = Output(1, n, Padding + width * n + 1);
                Assert.That(stage.ProcessRow(Rows(input, Padding), Rows(actual, Padding), (0, 0), (nuint)width, 0, 0, 0).IsOk, Is.True);
                for (int x = 0; x < width; x++)
                    Assert.That(stage.ProcessRow(Rows(input, Padding + x), Rows(expected, Padding + x * n), (0, 0), 1, (nuint)x, 0, 0).IsOk, Is.True);
                Compare(actual, expected, $"shift={shift}, width={width}");
            }
        }

        private static float[][][] Input(int channels, int rows, int length)
        {
            float[][][] data = Output(channels, rows, length);
            Random random = new Random(123);
            foreach (float[][] channel in data)
                foreach (float[] row in channel)
                    for (int x = 0; x < length; x++) row[x] = (float)(random.NextDouble() * 2 - 1);
            return data;
        }

        private static float[][][] Output(int channels, int rows, int length)
        {
            float[][][] data = new float[channels][][];
            for (int c = 0; c < channels; c++)
            {
                data[c] = new float[rows][];
                for (int y = 0; y < rows; y++)
                {
                    data[c][y] = new float[length];
                    Array.Fill(data[c][y], -12345f);
                }
            }
            return data;
        }

        private static PipelineRow[][] Rows(float[][][] data, int offset)
        {
            PipelineRow[][] rows = new PipelineRow[data.Length][];
            for (int c = 0; c < data.Length; c++)
            {
                rows[c] = new PipelineRow[data[c].Length];
                for (int y = 0; y < rows[c].Length; y++)
                    rows[c][y] = new PipelineRow(data[c][y], offset - RenderPipelineStage.KRenderPipelineXOffset);
            }
            return rows;
        }

        private static void Compare(float[][][] actual, float[][][] expected, string context)
        {
            for (int c = 0; c < actual.Length; c++)
                for (int y = 0; y < actual[c].Length; y++)
                    for (int x = 0; x < actual[c][y].Length; x++)
                        Assert.That(BitConverter.SingleToInt32Bits(actual[c][y][x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected[c][y][x])), $"{context}, c={c}, y={y}, x={x}");
        }
    }
}
