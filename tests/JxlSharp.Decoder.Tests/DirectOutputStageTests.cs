using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Checks direct pixel writes against the callback-only output path.</summary>
[TestFixture]
public sealed class DirectOutputStageTests
{
    /// <summary>Formats, orientation, byte order, alpha and unaligned row padding stay identical.</summary>
    [TestCase(JxlDataType.UInt8)]
    [TestCase(JxlDataType.UInt16)]
    [TestCase(JxlDataType.Float16)]
    [TestCase(JxlDataType.Float)]
    public void DirectCallbackBufferMatchesBufferedOutput(JxlDataType type)
    {
        foreach (int channels in new[] { 1, 2, 3, 4 })
        foreach (int width in new[] { 1, 8, 17, 257 })
        for (int orientation = 1; orientation <= 8; orientation++)
        foreach (var endian in new[] { JxlEndianness.LittleEndian, JxlEndianness.BigEndian })
        {
            const int height = 3;
            int sampleBytes = type == JxlDataType.UInt8 ? 1 : type == JxlDataType.Float ? 4 : 2;
            bool transpose = orientation > 4;
            int outWidth = transpose ? height : width;
            int outHeight = transpose ? width : height;
            int stride = outWidth * channels * sampleBytes + 5;
            var expected = new Sink(stride, outHeight, channels * sampleBytes);
            var actual = new Sink(stride, outHeight, channels * sampleBytes);
            byte[] unused = new byte[stride * outHeight];
            Array.Fill(unused, (byte)0xA5);
            var format = new JxlPixelFormat { NumChannels = (uint)channels, DataType = type, Endianness = endian };
            ImageOutput MakeOutput(Sink sink, bool direct) => new ImageOutput
            {
                Format = format, BitsPerSample = type == JxlDataType.UInt8 ? 8u : 16u,
                Stride = (nuint)stride,
                Buffer = direct ? sink.Memory : unused.AsMemory(),
                BufferSize = direct ? (nuint)sink.Memory.Length : (nuint)unused.Length,
                CallbackBuffer = direct,
                Callback = new PixelCallback((_, _, _) => sink,
                    (opaque, _, x, y, count, pixels) => ((Sink)opaque!).Write(x, y, count, pixels),
                    opaque => ((Sink)opaque!).Destroyed = true, null),
            };
            using (var reference = RenderPipelineStages.GetWriteToOutputStage(MakeOutput(expected, false),
                ((nuint)width, height), (channels is 2 or 4, false, 3), (Orientation)orientation, Array.Empty<ImageOutput>(), JxlMemoryManager.Default))
            using (var current = RenderPipelineStages.GetWriteToOutputStage(MakeOutput(actual, true),
                ((nuint)width, height), (channels is 2 or 4, false, 3), (Orientation)orientation, Array.Empty<ImageOutput>(), JxlMemoryManager.Default))
            {
                Assert.That(reference.PrepareForThreads(1).IsOk, Is.True);
                Assert.That(current.PrepareForThreads(1).IsOk, Is.True);
                for (int y = 0; y < height; y++)
                {
                    var rows = new PipelineRow[4][];
                    for (int c = 0; c < 4; c++)
                    {
                        float[] data = new float[width + 4];
                        for (int x = 0; x < width; x++) data[x + 2] = (x % 17) * .045f + c * .04f + y * .01f;
                        rows[c] = new[] { new PipelineRow(data, 2 - RenderPipelineStage.KRenderPipelineXOffset) };
                    }
                    Assert.That(reference.ProcessRow(rows, Array.Empty<PipelineRow[]>(), (0, 0), (nuint)width, 0, (nuint)y, 0).IsOk, Is.True);
                    Assert.That(current.ProcessRow(rows, Array.Empty<PipelineRow[]>(), (0, 0), (nuint)width, 0, (nuint)y, 0).IsOk, Is.True);
                }
            }
            Assert.That(actual.Data, Is.EqualTo(expected.Data), $"{type}, channels={channels}, width={width}, orientation={orientation}, endian={endian}");
            Assert.That(actual.Calls, Is.EqualTo(expected.Calls));
            foreach (byte value in unused) Assert.That(value, Is.EqualTo((byte)0xA5));
            Assert.That(actual.Destroyed && expected.Destroyed, Is.True);
            Assert.That(actual.AliasedCalls, Is.EqualTo(transpose ? 0 : actual.Calls.Count));
        }
    }

    private sealed class Sink
    {
        public readonly byte[] Data;
        public readonly int Stride, PixelBytes;
        public readonly List<(nuint, nuint, nuint)> Calls = new();
        public bool Destroyed;
        public int AliasedCalls;
        public Memory<byte> Memory => Data.AsMemory(3, Data.Length - 6);
        public Sink(int stride, int height, int pixelBytes)
        {
            Stride = stride; PixelBytes = pixelBytes;
            Data = new byte[stride * height + 6];
            Array.Fill(Data, (byte)0xA5);
        }
        public void Write(nuint x, nuint y, nuint count, ReadOnlySpan<byte> pixels)
        {
            Calls.Add((x, y, count));
            Span<byte> target = Memory.Span.Slice((int)y * Stride + (int)x * PixelBytes, (int)count * PixelBytes);
            if (pixels.Overlaps(target, out int offset) && offset == 0) AliasedCalls++;
            pixels.CopyTo(target);
        }
    }
}
