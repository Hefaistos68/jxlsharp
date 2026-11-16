using System;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp;

/// <summary>Checks codec scheduling on either side of a group boundary.</summary>
[TestFixture]
public sealed class CodecThreadingTests
{
    /// <summary>Single-task initialization and parallel initialization produce identical output.</summary>
    [TestCase(1, true)]
    [TestCase(255, true)]
    [TestCase(256, true)]
    [TestCase(257, true)]
    [TestCase(255, false)]
    [TestCase(257, false)]
    public void ThreadCountsProduceIdenticalPixels(int size, bool lossless)
    {
        var pixels = new byte[size * size * 3];
        new Random(17).NextBytes(pixels);
        var image = new JxlImageData(pixels, size, size, 3);
        var codec = new JxlSharpCodec();
        byte[]? expected = null;
        foreach (int threads in new[] { 1, 2, 4, 0 })
        {
            byte[] encoded = codec.Encode(image, new JxlEncodeOptions
            {
                Lossless = lossless, Distance = lossless ? 0 : 1, Effort = 3, NumThreads = threads,
            });
            var decoded = codec.Decode(encoded, new JxlDecodeOptions
            {
                NumThreads = threads, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8,
                ReconstructJpegIfAvailable = false,
            });
            byte[] actual = decoded.PrimaryImage!.Pixels;
            if (lossless) actual.ShouldBe(pixels);
            expected ??= actual;
            actual.ShouldBe(expected);
        }
    }
}
