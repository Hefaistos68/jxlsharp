using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Encoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp;

/// <summary>Checks the fast lossless path through the public encoder API.</summary>
[TestFixture]
public sealed class FastLosslessIntegrationTests
{
    /// <summary>Checks sample depth, byte order, padding, container and input ownership.</summary>
    [TestCase(1, 8, false, false)]
    [TestCase(2, 8, false, true)]
    [TestCase(3, 8, false, true)]
    [TestCase(4, 8, false, false)]
    [TestCase(1, 16, false, true)]
    [TestCase(2, 16, true, false)]
    [TestCase(3, 16, true, true)]
    [TestCase(4, 16, false, false)]
    [TestCase(3, 10, true, false)]
    [TestCase(4, 12, false, true)]
    [TestCase(1, 14, false, false)]
    public void PaddedInput_IsSnapshottedAndRoundTrips(int channels, int bits, bool bigEndian, bool container)
    {
        const int width = 257, height = 19;
        int sampleBytes = bits <= 8 ? 1 : 2;
        int rowBytes = width * channels * sampleBytes;
        int stride = (rowBytes + 15) / 16 * 16;
        byte[] input = new byte[stride * (height - 1) + rowBytes];
        byte[] expected = new byte[rowBytes * height];
        var random = new Random(173);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width * channels; x++)
            {
                ushort value = (ushort)random.Next(1 << bits);
                int packed = y * rowBytes + x * sampleBytes;
                int padded = y * stride + x * sampleBytes;
                if (sampleBytes == 1) input[padded] = expected[packed] = (byte)value;
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(packed), value);
                    if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(input.AsSpan(padded), value);
                    else BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(padded), value);
                }
            }
        using var enc = CreateEncoder(width, height, channels, bits);
        enc.UseContainer(container).ShouldBe(JxlEncoderStatus.Success);
        var settings = enc.FrameSettingsCreate(null);
        enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Effort, 1).ShouldBe(JxlEncoderStatus.Success);
        enc.SetFrameLossless(settings, true).ShouldBe(JxlEncoderStatus.Success);
        var depth = new JxlBitDepth { Type = JxlBitDepthType.FromCodestream };
        enc.SetFrameBitDepth(settings, in depth).ShouldBe(JxlEncoderStatus.Success);
        var format = new JxlPixelFormat { NumChannels = (uint)channels,
            DataType = sampleBytes == 1 ? JxlDataType.UInt8 : JxlDataType.UInt16,
            Endianness = bigEndian ? JxlEndianness.BigEndian : JxlEndianness.LittleEndian, Align = 16 };
        enc.AddImageFrame(settings, in format, input).ShouldBe(JxlEncoderStatus.Success);
        Array.Fill(input, (byte)0);
        enc.CloseInput();
        byte[] encoded = Drain(enc);
        var result = new JxlSharpCodec().Decode(encoded, new JxlDecodeOptions { NumThreads = 1,
            RequestedChannels = (uint)channels, RequestedDataType = format.DataType,
            OutputBitdepth = depth });
        result.PrimaryImage!.Pixels.ShouldBe(expected);
    }

    /// <summary>Verifies that normal encoding dispatches fast groups to the configured runner.</summary>
    [Test]
    public void EffortOne_UsesFastGroupsThroughConfiguredRunner()
    {
        using var enc = CreateEncoder(513, 257, 3, 8);
        var ranges = new List<(uint, uint)>();
        var pool = new JxlSharp.Base.ThreadPool((_, opaque, init, function, start, end) =>
        {
            ranges.Add((start, end));
            int status = init(opaque, 1);
            if (status != 0) return status;
            for (uint i = start; i < end; i++) function(opaque, i, 0);
            return 0;
        }, null);
        enc.SetParallelRunner(pool).ShouldBe(JxlEncoderStatus.Success);
        var settings = enc.FrameSettingsCreate(null);
        enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Effort, 1).ShouldBe(JxlEncoderStatus.Success);
        enc.SetFrameLossless(settings, true).ShouldBe(JxlEncoderStatus.Success);
        var format = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 };
        byte[] pixels = new byte[513 * 257 * 3];
        new Random(71).NextBytes(pixels);
        enc.AddImageFrame(settings, in format, pixels).ShouldBe(JxlEncoderStatus.Success);
        ranges.ShouldBeEmpty(); // No conversion to floating point before encoding.
        enc.CloseInput();
        byte[] encoded = Drain(enc);
        ranges.ShouldBe(new[] { (0u, 6u) }); // Three columns by two rows of 256-pixel groups.
        var decoded = new JxlSharpCodec().Decode(encoded,
            new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 });
        decoded.PrimaryImage!.Pixels.ShouldBe(pixels);
    }

    /// <summary>Checks late instrumentation falls back to the general encoder.</summary>
    [Test]
    public void StatsAddedAfterQueueing_FallsBackAndRoundTrips()
    {
        using var enc = CreateEncoder(17, 9, 3, 8);
        var settings = enc.FrameSettingsCreate(null);
        enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Effort, 1).ShouldBe(JxlEncoderStatus.Success);
        enc.SetFrameLossless(settings, true).ShouldBe(JxlEncoderStatus.Success);
        var format = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 };
        byte[] input = new byte[17 * 9 * 3];
        new Random(11).NextBytes(input);
        enc.AddImageFrame(settings, in format, input).ShouldBe(JxlEncoderStatus.Success);
        enc.CollectStats(settings, new JxlSharp.Encoder.JxlEncoderStats());
        enc.CloseInput();
        var decoded = new JxlSharpCodec().Decode(Drain(enc),
            new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 });
        decoded.PrimaryImage!.Pixels.ShouldBe(input);
    }

    /// <summary>Checks pooled group buffers across changing images, tails and sample depths.</summary>
    [TestCase(1, 8, 257)]
    [TestCase(2, 8, 257)]
    [TestCase(3, 8, 257)]
    [TestCase(4, 8, 257)]
    [TestCase(1, 16, 257)]
    [TestCase(2, 16, 257)]
    [TestCase(3, 16, 257)]
    [TestCase(4, 16, 257)]
    [TestCase(3, 8, 256)]
    [TestCase(4, 16, 256)]
    public void ReusedGroupBuffers_PreservePixels(int channels, int bits, int width)
    {
        int height = width;
        byte[] pixels = new byte[width * height * channels * (bits / 8)];
        var random = new Random(channels + bits);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            if (iteration == 1) Array.Fill(pixels, (byte)0);
            else if (iteration == 2) Array.Fill(pixels, (byte)255);
            else random.NextBytes(pixels);
            var encoded = EncFastLossless.FastLosslessEncode(pixels,
                ((nuint)width, (nuint)(width * channels * (bits / 8)), (nuint)height, (nuint)channels, (nuint)bits));
            encoded.IsOk.ShouldBeTrue();
            var decoded = new JxlSharpCodec().Decode(encoded.Value, new JxlDecodeOptions
            {
                NumThreads = 1, RequestedChannels = (uint)channels,
                RequestedDataType = bits == 8 ? JxlDataType.UInt8 : JxlDataType.UInt16,
            });
            decoded.PrimaryImage!.Pixels.ShouldBe(pixels);
        }
    }

    private static JxlEncoder CreateEncoder(int width, int height, int channels, int bits)
    {
        var enc = new JxlEncoder();
        JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
        info.XSize = (uint)width; info.YSize = (uint)height;
        info.NumColorChannels = channels <= 2 ? 1u : 3u;
        info.BitsPerSample = (uint)bits;
        info.UsesOriginalProfile = true;
        if (channels is 2 or 4) { info.NumExtraChannels = 1; info.AlphaBits = (uint)bits; }
        enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Success);
        return enc;
    }

    private static byte[] Drain(JxlEncoder enc)
    {
        var bytes = new List<byte>();
        byte[] chunk = new byte[127];
        JxlEncoderStatus status;
        do
        {
            int offset = 0;
            status = enc.ProcessOutput(chunk, ref offset);
            bytes.AddRange(chunk.AsSpan(0, offset));
            (status is JxlEncoderStatus.Success or JxlEncoderStatus.NeedMoreOutput).ShouldBeTrue();
        } while (status == JxlEncoderStatus.NeedMoreOutput);
        return bytes.ToArray();
    }
}




