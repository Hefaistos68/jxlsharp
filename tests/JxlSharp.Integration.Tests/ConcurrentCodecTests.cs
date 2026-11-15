using System;
using System.Threading;
using System.Threading.Tasks;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp;

/// <summary>Checks concurrent operations on a shared stateless facade and read-only options.</summary>
[TestFixture]
public sealed class ConcurrentCodecTests
{
    /// <summary>Concurrent images retain independent runner state and match serial output.</summary>
    [TestCase(true, 1)]
    [TestCase(true, 4)]
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 4)]
    [TestCase(false, 0)]
    public async Task ConcurrentImagesMatchSerialResults(bool lossless, int threads)
    {
        const int count = 4, width = 257, height = 129;
        var codec = new JxlSharpCodec();
        var images = new JxlImageData[count];
        var encoded = new byte[count][];
        var decoded = new byte[count][];
        var serialEncode = new JxlEncodeOptions { Lossless = lossless, Distance = lossless ? 0 : 1, Effort = 3, NumThreads = 1 };
        var serialDecode = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };
        for (int i = 0; i < count; i++)
        {
            byte[] pixels = new byte[width * height * 3];
            new Random(17 + i).NextBytes(pixels);
            images[i] = new JxlImageData(pixels, width, height, 3);
            encoded[i] = codec.Encode(images[i], serialEncode);
            decoded[i] = codec.Decode(encoded[i], serialDecode).PrimaryImage!.Pixels;
            if (lossless) decoded[i].ShouldBe(pixels);
        }
        var encodeOptions = new JxlEncodeOptions { Lossless = lossless, Distance = lossless ? 0 : 1, Effort = 3, NumThreads = threads };
        var decodeOptions = new JxlDecodeOptions { NumThreads = threads, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };
        using var start = new Barrier(count);
        var jobs = new Task[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            jobs[i] = Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    byte[] result = codec.Encode(images[index], encodeOptions);
                    result.ShouldBe(encoded[index]);
                    codec.Decode(result, decodeOptions).PrimaryImage!.Pixels.ShouldBe(decoded[index]);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        await Task.WhenAll(jobs);
        encodeOptions.NumThreads.ShouldBe(threads);
        decodeOptions.NumThreads.ShouldBe(threads);
    }
}
