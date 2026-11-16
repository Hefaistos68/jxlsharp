using System;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks vector rows against the scalar path, including tails and buffer boundaries.</summary>
    [TestFixture]
    public sealed class ColorPipelineSimdTests
    {
        /// <summary>Each width exercises a different vector/tail combination.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(31)]
        public void ColorRowsMatchScalarAndPreserveSentinels(int width)
        {
            foreach (TransferFunction tf in new[] { TransferFunction.SRGB, TransferFunction.PQ, TransferFunction.HLG, TransferFunction.Tf709 })
            {
                for (int kind = 0; kind < 5; kind++)
                {
                    OutputEncodingInfo info = new OutputEncodingInfo();
                    Assert.That(info.SetFromMetadata(new CodecMetadata()).IsOk, Is.True);
                    info.ColorEncoding.Tf.SetTransferFunction(tf);
                    if (kind == 2) info.ColorEncoding.SetColorSpace(ColorSpace.XYB);
                    using RenderPipelineStage stage = kind switch
                    {
                        0 => RenderPipelineStages.GetYCbCrStage(),
                        1 or 2 => RenderPipelineStages.GetXYBStage(info),
                        3 => RenderPipelineStages.GetToLinearStage(info),
                        _ => RenderPipelineStages.GetFromLinearStage(info)!,
                    };
                    float[][] actual = new float[3][];
                    float[][] expected = new float[3][];
                    for (int c = 0; c < 3; c++)
                    {
                        actual[c] = new float[width + 2];
                        actual[c][0] = actual[c][width + 1] = -12345f;
                        for (int x = 0; x < width; x++)
                            actual[c][x + 1] = x == 0 ? 0f : ((x * 17 + c * 7) % 101) / 100f;
                        expected[c] = (float[])actual[c].Clone();
                    }
                    Assert.That(stage.ProcessRow(Rows(actual, 1), Array.Empty<PipelineRow[]>(), (0, 0), (nuint)width, 0, 0, 0).IsOk, Is.True);
                    for (int x = 0; x < width; x++)
                        Assert.That(stage.ProcessRow(Rows(expected, x + 1), Array.Empty<PipelineRow[]>(), (0, 0), 1, 0, 0, 0).IsOk, Is.True);
                    for (int c = 0; c < 3; c++)
                    {
                        Assert.That(actual[c][0], Is.EqualTo(-12345f));
                        Assert.That(actual[c][width + 1], Is.EqualTo(-12345f));
                        for (int x = 1; x <= width; x++)
                            Assert.That(actual[c][x], Is.EqualTo(expected[c][x]).Within(2e-5f * Math.Max(1f, Math.Abs(expected[c][x]))), $"{tf}, kind={kind}, width={width}, c={c}, x={x}");
                    }
                }
            }
        }

        /// <summary>Checks exponent-table selection and the linear boundary against the original sRGB approximation.</summary>
        [Test]
        public void FastSrgbMatchesScalarAcrossExponentTable()
        {
            float[] samples = new float[8];
            for (int batch = 0; batch < 128; batch++)
            {
                for (int lane = 0; lane < 8; lane++)
                    samples[lane] = batch == 0 ? new[] { -1f, -0f, 0f, 0.0031307f, 0.0031308f, 0.0031309f, 1f, 2f }[lane]
                        : MathF.ScaleB(0.5f + lane / 16f, batch % 32 - 24);
                Vector256<float> result = TfSrgb.FastLinearToSrgb(Vector256.Create(samples));
                for (int lane = 0; lane < 8; lane++)
                    Assert.That(result.GetElement(lane), Is.EqualTo(TfSrgb.FastLinearToSrgb(samples[lane])));
            }
        }

        private static PipelineRow[][] Rows(float[][] data, int offset)
        {
            PipelineRow[][] rows = new PipelineRow[3][];
            for (int c = 0; c < 3; c++)
                rows[c] = new[] { new PipelineRow(data[c], offset - RenderPipelineStage.KRenderPipelineXOffset) };
            return rows;
        }
    }
}

