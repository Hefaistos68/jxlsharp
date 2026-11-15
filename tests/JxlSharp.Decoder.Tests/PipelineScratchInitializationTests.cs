using System;
using System.Threading.Tasks;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Poison scratch storage to expose reads outside the initialized group pixels.</summary>
[TestFixture]
public sealed class PipelineScratchInitializationTests
{
    /// <summary>Public pipeline preparation preserves the zeroed input buffer contract.</summary>
    [Test]
    public void PublicPreparationKeepsInputsZeroInitialized()
    {
        var dimensions = new FrameDimensions();
        dimensions.Set(17, 9, 1, 0, 0, false, 1);
        using var output = Image3<float>.Create(null, 17, 9).Value;
        var builder = new RenderPipeline.Builder(null, 3);
        Assert.That(builder.AddStage(new CaptureStage(output)).IsOk, Is.True);
        using var pipeline = builder.Finalize(dimensions).Value;
        Assert.That(pipeline.PrepareForThreads(4, false).IsOk, Is.True);
        var input = pipeline.GetInputBuffers(0, 0);
        for (nuint c = 0; c < 3; c++)
            Assert.That(input.GetBuffer(c).Plane.BackingArray, Is.All.Zero);
    }

    /// <summary>Filters, partial edge groups, worker/group storage and repeated passes ignore poisoned padding.</summary>
    [TestCase(1, 1, false)]
    [TestCase(1, 1, true)]
    [TestCase(7, 9, false)]
    [TestCase(257, 17, false)]
    [TestCase(513, 263, false)]
    [TestCase(513, 263, true)]
    [TestCase(513, 513, false)]
    public void PoisonedInputsMatchZeroedInputs(int width, int height, bool groupStorage)
    {
        using var expected = Run(width, height, groupStorage, 0f);
        using var actual = Run(width, height, groupStorage, float.NaN);
        for (nuint c = 0; c < 3; c++)
        for (nuint y = 0; y < (nuint)height; y++)
        for (int x = 0; x < width; x++)
        {
            float value = actual.PlaneRow(c, y)[x];
            Assert.That(float.IsFinite(value), Is.True, $"c={c}, x={x}, y={y}");
            Assert.That(value, Is.GreaterThan(0f));
            Assert.That(BitConverter.SingleToInt32Bits(value),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected.PlaneRow(c, y)[x])));
        }
    }

    /// <summary>5x5 upsampling also ignores padding beyond the initialized low-resolution pixels.</summary>
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    public void UpsamplingIgnoresPoisonedPadding(int factor)
    {
        using var expected = Run(1027, 519, false, 0f, factor);
        using var actual = Run(1027, 519, false, float.NaN, factor);
        for (nuint c = 0; c < 3; c++)
        for (nuint y = 0; y < 519; y++)
        for (int x = 0; x < 1027; x++)
        {
            float value = actual.PlaneRow(c, y)[x];
            Assert.That(float.IsFinite(value), Is.True);
            Assert.That(value, Is.GreaterThan(0f));
            Assert.That(BitConverter.SingleToInt32Bits(value),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected.PlaneRow(c, y)[x])));
        }
    }

    private sealed class CaptureStage(Image3<float> output) : RenderPipelineStage(new RenderPipelineStage.Settings())
    {
        public override string GetName() => "Capture";

        public override RenderPipelineChannelMode GetChannelMode(nuint c) =>
            c < 3 ? RenderPipelineChannelMode.Input : RenderPipelineChannelMode.Ignored;

        public override Status ProcessRow(PipelineRow[][] inputRows, PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra, nuint xsize, nuint xpos, nuint ypos, nuint threadId)
        {
            for (nuint c = 0; c < 3; c++)
                GetInputRow(inputRows, c, 0).AsSpan(0, (int)xsize)
                    .CopyTo(output.PlaneRow(c, ypos).Slice((int)xpos, (int)xsize));
            return true;
        }
    }

    private static Image3<float> Run(int width, int height, bool groupStorage, float poison, int factor = 1)
    {
        var dimensions = new FrameDimensions();
        dimensions.Set((nuint)width, (nuint)height, 1, 0, 0, false, (nuint)factor);
        var output = Image3<float>.Create(null, (nuint)width, (nuint)height).Value;
        using var sigma = ImagePlane<float>.Create(null, dimensions.XSizeBlocks + 32, dimensions.YSizeBlocks + 32).Value;
        ImageOps.FillImage(-2f, sigma);
        var filter = new LoopFilter { Gab = true, EpfIters = 3 };
        var builder = new RenderPipeline.Builder(null, 3);
        Assert.That(builder.AddStage(RenderPipelineStages.GetGaborishStage(filter)).IsOk, Is.True);
        for (int i = 0; i < 3; i++)
            Assert.That(builder.AddStage(RenderPipelineStages.GetEPFStage(filter, sigma, (EpfStage)i)).IsOk, Is.True);
        if (factor > 1)
            for (nuint c = 0; c < 3; c++)
                Assert.That(builder.AddStage(RenderPipelineStages.GetUpsamplingStage(null,
                    new CustomTransformData(), c, factor == 2 ? 1u : factor == 4 ? 2u : 3u)).IsOk, Is.True);
        Assert.That(builder.AddStage(new CaptureStage(output)).IsOk, Is.True);
        using var pipeline = builder.Finalize(dimensions).Value;
        Assert.That(pipeline.PrepareForDecoderThreads(4, groupStorage).IsOk, Is.True);
        int groups = (int)dimensions.NumGroups;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int g = 0; g < groups; g++) pipeline.ClearDone((nuint)g);
            var workers = new Task[4];
            int currentPass = pass;
            for (int thread = 0; thread < 4; thread++)
            {
                int id = thread;
                workers[id] = Task.Run(() =>
                {
                    for (int step = id; step < groups; step += 4)
                    {
                        nuint g = (nuint)(groups - 1 - step);
                        var input = pipeline.GetInputBuffers(g, (nuint)id);
                        for (nuint c = 0; c < 3; c++)
                        {
                            var (plane, rect) = input.GetBuffer(c);
                            Array.Fill(plane.BackingArray, poison);
                            int gx = (int)(g % dimensions.XSizeGroups) * (int)dimensions.GroupDim;
                            int gy = (int)(g / dimensions.XSizeGroups) * (int)dimensions.GroupDim;
                            for (nuint y = 0; y < (nuint)rect.YSize; y++)
                            {
                                Span<float> row = rect.Row(plane, y);
                                for (int x = 0; x < rect.XSize; x++)
                                    row[x] = .25f + ((gx + x + gy + (int)y + (int)c * 17) % 31) * .01f + currentPass * .1f;
                            }
                        }
                        Assert.That(input.Done().IsOk, Is.True);
                    }
                });
            }
            Task.WaitAll(workers);
            Assert.That(pipeline.PassesWithAllInput(), Is.EqualTo((nuint)(pass + 1)));
        }
        return output;
    }
}
