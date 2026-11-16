using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Fused output must preserve transfer, dithering, tails and callback semantics exactly.</summary>
[TestFixture]
public sealed class FusedSrgb8OutputTests
{
    /// <summary>Compare the fused writer with the original two stages, including guarded output strides.</summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(255)]
    [TestCase(257)]
    [TestCase(1027)]
    public void FusedOutputMatchesSeparateStages(int width)
    {
        for (int mode = 0; mode < 3; mode++)
        foreach (bool xyb in new[] { false, true })
        {
            var expected = Run(width, mode, false, xyb);
            var actual = Run(width, mode, true, xyb);
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes), $"width={width}, mode={mode}");
            Assert.That(actual.Calls, Is.EqualTo(expected.Calls));
        }
    }

    private static (byte[] Bytes, string Calls) Run(int width, int mode, bool fused, bool xyb)
    {
        const int xpos = 5, ypos = 3;
        int stride = (width + xpos + 8) * 3 + 11;
        byte[] bytes = new byte[stride * 5];
        Array.Fill(bytes, (byte)0xa7);
        var calls = new List<string>();
        var output = new ImageOutput
        {
            Format = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 },
            BitsPerSample = 8,
            Buffer = mode == 1 ? Memory<byte>.Empty : bytes,
            BufferSize = (nuint)bytes.Length,
            Stride = (nuint)stride,
            CallbackBuffer = mode == 2,
        };
        if (mode != 0)
            output.Callback = new PixelCallback((opaque, _, _) => opaque,
                (_, _, x, y, count, pixels) =>
                {
                    calls.Add($"{x},{y},{count}");
                    pixels.CopyTo(bytes.AsSpan((int)y * stride + (int)x * 3, (int)count * 3));
                }, _ => { }, new object());
        var info = new OutputEncodingInfo();
        Assert.That(info.SetFromMetadata(new CodecMetadata()).IsOk, Is.True);
        info.ColorEncoding.Tf.SetTransferFunction(TransferFunction.SRGB);
        float[] values = { float.NegativeInfinity, -1f, -0f, float.Epsilon, .0031307f, .0031308f,
            .0031309f, .18f, .5f, 1f, 1.5f, float.PositiveInfinity, float.NaN };
        var rows = new PipelineRow[3][];
        for (int c = 0; c < 3; c++)
        {
            float[] row = new float[width + 2];
            Array.Fill(row, float.NaN);
            for (int x = 0; x < width; x++) row[x + 1] = values[(x * 7 + c * 3) % values.Length];
            rows[c] = new[] { new PipelineRow(row, 1 - RenderPipelineStage.KRenderPipelineXOffset) };
        }
        using RenderPipelineStage writer = fused
            ? RenderPipelineStages.GetWriteLinearSrgb8Stage(output, ((nuint)(width + xpos + 8), 5),
                (false, false, 3), Array.Empty<ImageOutput>(), null, xyb ? info.OpsinParams : null)
            : RenderPipelineStages.GetWriteToOutputStage(output, ((nuint)(width + xpos + 8), 5),
                (false, false, 3), Orientation.Identity, Array.Empty<ImageOutput>(), null);
        Assert.That(writer.PrepareForThreads(1).IsOk, Is.True);
        if (!fused)
        {
            if (xyb)
            {
                using var transform = RenderPipelineStages.GetXYBStage(info);
                Assert.That(transform.ProcessRow(rows, Array.Empty<PipelineRow[]>(), (0, 0),
                    (nuint)width, xpos, ypos, 0).IsOk, Is.True);
            }
            using var transfer = RenderPipelineStages.GetFromLinearStage(info)!;
            Assert.That(transfer.ProcessRow(rows, Array.Empty<PipelineRow[]>(), (0, 0),
                (nuint)width, xpos, ypos, 0).IsOk, Is.True);
        }
        Assert.That(writer.ProcessRow(rows, Array.Empty<PipelineRow[]>(), (0, 0),
            (nuint)width, xpos, ypos, 0).IsOk, Is.True);
        return (bytes, string.Join(";", calls));
    }
}
