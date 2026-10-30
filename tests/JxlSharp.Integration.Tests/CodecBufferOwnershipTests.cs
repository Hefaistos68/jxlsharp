using System;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Extras;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp;

/// <summary>Checks buffer ownership across optimized facade and extras routes.</summary>
[TestFixture]
public sealed class CodecBufferOwnershipTests
{
    /// <summary>Public conversions continue creating independent pixel snapshots.</summary>
    [TestCase(JxlDataType.UInt8, 1)]
    [TestCase(JxlDataType.UInt16, 2)]
    [TestCase(JxlDataType.Float16, 3)]
    [TestCase(JxlDataType.Float, 4)]
    public void PublicPixelConversionsKeepIndependentStorage(JxlDataType type, int channels)
    {
        var source = new byte[9 * 7 * channels * (int)(PackedImage.BitsPerChannel(type) / 8)];
        new Random(42).NextBytes(source);
        var image = JxlImageData.FromPixels(source, 9, 7, channels, type, 0);
        var packed = image.ToPackedPixelFile();
        var extracted = JxlImageData.FromPackedPixelFile(packed);
        image.Pixels.ShouldBe(source);
        packed.Frames[0].Color.PixelsBuffer.ShouldBe(source);
        extracted.Pixels.ShouldBe(source);
        source[0] ^= 255;
        image.Pixels[0].ShouldNotBe(source[0]);
        packed.Frames[0].Color.Pixels()[1] ^= 255;
        image.Pixels[1].ShouldNotBe(packed.Frames[0].Color.Pixels()[1]);
        extracted.Pixels[1].ShouldBe(image.Pixels[1]);
        extracted.Pixels[2] ^= 255;
        extracted.Pixels[2].ShouldNotBe(packed.Frames[0].Color.Pixels()[2]);
    }

    /// <summary>Simple routes preserve exact pixels and extended results retain independent views.</summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void FacadeRoutesPreservePixelsAndOwnership(int channels)
    {
        const int width = 17, height = 9;
        var pixels = new byte[width * height * channels];
        new Random(17).NextBytes(pixels);
        var codec = new JxlSharpCodec();
        byte[] encoded = codec.EncodeLossless(pixels, width, height, channels, 3);
        byte[] qualityEncoded = codec.Encode(pixels, width, height, channels, 100, 3);
        qualityEncoded.ShouldBe(encoded);
        codec.Encode(pixels, width, height, channels, 90, 3).ShouldBe(
            codec.Encode(new JxlImageData(pixels, width, height, channels), 90, 3));
        var options = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = (uint)channels };
        var result = codec.Decode(encoded, options);
        result.PrimaryImage!.Pixels.ShouldBe(pixels);
        result.PrimaryImage.Pixels[0] ^= 255;
        result.PixelFile.Frames[0].Color.Pixels()[0].ShouldBe(pixels[0]);
        result.PixelFile.Frames[0].Color.Pixels()[1] ^= 255;
        result.PrimaryImage.Pixels[1].ShouldBe(pixels[1]);
        using var stream = new MemoryStream(encoded);
        var streamed = codec.Decode(stream);
        var simple = codec.Decode(encoded);
        streamed.Pixels.ShouldBe(simple.Pixels);
        simple.Pixels[0] ^= 255;
        codec.Decode(encoded).Pixels.ShouldBe(streamed.Pixels);
        streamed.Pixels.ShouldNotBeSameAs(encoded);
    }

    /// <summary>The borrowed-memory route honors slice offsets, sizes and consumed-byte counts.</summary>
    [Test]
    public void InternalMemoryDecodeHonorsSlices()
    {
        var codec = new JxlSharpCodec();
        byte[] pixels = new byte[11 * 13 * 3];
        new Random(31).NextBytes(pixels);
        byte[] encoded = codec.EncodeLossless(pixels, 11, 13, 3, 3);
        var storage = new byte[encoded.Length + 37];
        encoded.CopyTo(storage, 19);
        var packed = new PackedPixelFile();
        var parameters = new JXLDecompressParams();
        CodecJxl.DecodeImageJXLFromMemory(storage.AsMemory(19, encoded.Length), parameters,
            out nuint consumed, packed).ShouldBeTrue();
        consumed.ShouldBe((nuint)encoded.Length);
        packed.Frames[0].Color.PixelsBuffer.ShouldBe(pixels);
        CodecJxl.DecodeImageJXLFromMemory(storage.AsMemory(19, 1), parameters,
            out consumed, packed).ShouldBeFalse();
        consumed.ShouldBe((nuint)0);
        packed.Frames.Count.ShouldBe(0);
    }

    /// <summary>Stream routes keep the existing MemoryStream position and snapshot behavior.</summary>
    [Test]
    public void StreamDecodeUsesLogicalMemoryStreamContents()
    {
        var codec = new JxlSharpCodec();
        byte[] pixels = new byte[8 * 9 * 3];
        new Random(4).NextBytes(pixels);
        byte[] encoded = codec.EncodeLossless(pixels, 8, 9, 3, 3);
        var storage = new byte[encoded.Length + 27];
        encoded.CopyTo(storage, 13);
        using var stream = new MemoryStream(storage, 13, encoded.Length, false);
        stream.Position = stream.Length;
        codec.Decode(stream).Pixels.ShouldBe(pixels);
        stream.Position.ShouldBe(stream.Length);
        var options = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3 };
        codec.Decode(stream, options).PrimaryImage!.Pixels.ShouldBe(pixels);
    }

    /// <summary>File paths use privately owned input while public results remain independent.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void FileDecodePreservesPixels(bool externalFormat)
    {
        var codec = new JxlSharpCodec();
        byte[] pixels = new byte[7 * 11 * 3];
        new Random(18).NextBytes(pixels);
        byte[] encoded = codec.EncodeLossless(pixels, 7, 11, 3, 3);
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + (externalFormat ? ".ppm" : ".jxl"));
        try
        {
            File.WriteAllBytes(path, externalFormat ? codec.DecodeToFormat(encoded, ".ppm") : encoded);
            codec.DecodeFile(path).Pixels.ShouldBe(pixels);
            var options = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3 };
            var result = codec.DecodeFile(path, options);
            result.PrimaryImage!.Pixels.ShouldBe(pixels);
            result.PrimaryImage.Pixels[0] ^= 255;
            result.PixelFile.Frames[0].Color.Pixels()[0].ShouldBe(pixels[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Reconstruction probing and color fallback reuse input without changing the output.</summary>
    [Test]
    public void ReconstructionProbeAndColorFallbackKeepSourceSnapshot()
    {
        var codec = new JxlSharpCodec();
        byte[] pixels = new byte[13 * 9 * 3];
        new Random(8).NextBytes(pixels);
        byte[] encoded = codec.EncodeLossless(pixels, 13, 9, 3, 3);
        var options = new JxlDecodeOptions
        {
            NumThreads = 1, RequestedChannels = 3, ColorSpace = "invalid_cs",
            ReconstructJpegIfAvailable = true,
        };
        var result = codec.Decode(encoded, options);
        result.PrimaryImage!.Pixels.ShouldBe(pixels);
        result.ReconstructedJpegBytes.ShouldBeNull();
        options.ColorSpace.ShouldBe("invalid_cs");
        Array.Fill(encoded, (byte)0);
        result.PrimaryImage.Pixels.ShouldBe(pixels);
    }
}
