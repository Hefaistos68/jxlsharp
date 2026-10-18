// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Extras.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CompressedIcc"/> ported from <c>lib/extras/compressed_icc_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class CompressedIccTest
    {
        /// <summary>
        /// Verifies that <see cref="CompressedIcc.JxlICCProfileEncode"/> and <see cref="CompressedIcc.JxlICCProfileDecode"/>
        /// roundtrip the test ICC profile losslessly and achieve compression.
        /// Ported from <c>CompressedIccTest.Roundtrip</c> in <c>lib/extras/compressed_icc_test.cc</c>.
        /// </summary>
        [Test]
        public void Roundtrip()
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            byte[] icc                     = TestUtils.GetIccTestProfile();

            bool encoded = CompressedIcc.JxlICCProfileEncode(memoryManager, icc, out byte[] compressedIcc);

            encoded.ShouldBeTrue();
            compressedIcc.Length.ShouldBeLessThan(icc.Length);

            bool decoded = CompressedIcc.JxlICCProfileDecode(memoryManager, compressedIcc, out byte[] decompressedIcc);

            decoded.ShouldBeTrue();
            decompressedIcc.Length.ShouldBe(icc.Length);
            decompressedIcc.ShouldBe(icc);
        }

        /// <summary>
        /// Verifies that empty inputs are rejected by <see cref="CompressedIcc"/>.
        /// </summary>
        [Test]
        public void EmptyInputFails()
        {
            bool encoded = CompressedIcc.JxlICCProfileEncode(null, ReadOnlySpan<byte>.Empty, out _);
            bool decoded = CompressedIcc.JxlICCProfileDecode(null, ReadOnlySpan<byte>.Empty, out _);

            encoded.ShouldBeFalse();
            decoded.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ColorDescription"/> and <see cref="ColorHintsHelper"/>
    /// ported from <c>lib/extras/dec/color_description_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class ColorDescriptionTest
    {
        /// <summary>
        /// Verifies that <c>ParseDescription(Description(c))</c> yields the same <see cref="ColorEncoding"/>
        /// across all supported color space, white point, primaries, transfer function, and rendering intent combinations.
        /// Ported from <c>ColorDescriptionTest.RoundTripAll</c> in <c>lib/extras/dec/color_description_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundTripAll()
        {
            IReadOnlyList<ColorEncodingDescriptor> allEncodings = TestUtils.AllEncodings();

            foreach (ColorEncodingDescriptor cdesc in allEncodings)
            {
                ColorEncoding cOriginal = TestUtils.ColorEncodingFromDescriptor(cdesc);
                string description      = ColorDescription.Description(cOriginal);

                Status parsed = ColorDescription.ParseDescription(description, out JxlColorEncoding cExternal);

                ((bool)parsed).ShouldBeTrue(description);

                ColorEncoding cInternal = new ColorEncoding();
                Status converted        = cInternal.FromExternal(cExternal);

                ((bool)converted).ShouldBeTrue(description);
                cOriginal.SameColorEncoding(cInternal).ShouldBeTrue(description);
            }
        }

        /// <summary>
        /// Verifies that <c>Gra_2_Per_gnan</c> is rejected.
        /// Ported from <c>ColorDescriptionTest.NanGamma</c> in <c>lib/extras/dec/color_description_test.cc</c>.
        /// </summary>
        [Test]
        public void NanGamma()
        {
            Status status = ColorDescription.ParseDescription("Gra_2_Per_gnan", out _);

            ((bool)status).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies custom chromaticities and gamma roundtripping through <see cref="ColorDescription"/>
        /// and <see cref="ColorHintsHelper.ApplyColorHints"/>.
        /// </summary>
        [Test]
        public void CustomGammaAndColorHints()
        {
            Status status = ColorDescription.ParseDescription("RGB_D65_SRG_Rel_g0.45455", out JxlColorEncoding cExt);

            ((bool)status).ShouldBeTrue();
            cExt.TransferFunction.ShouldBe(JxlTransferFunction.Gamma);
            cExt.Gamma.ShouldBe(0.45455, 1e-4);

            ColorHints hints    = new ColorHints();
            PackedPixelFile ppf = new PackedPixelFile();

            hints.Add("color_space", "RGB_D65_SRG_Rel_Lin");

            Status hintStatus = ColorHintsHelper.ApplyColorHints(hints, false, false, ppf);

            ((bool)hintStatus).ShouldBeTrue();
            ppf.ColorEncoding.TransferFunction.ShouldBe(JxlTransferFunction.Linear);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="CodecPgx"/> ported from <c>lib/extras/dec/pgx_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class CodecPGXTest
    {
        /// <summary>
        /// Verifies decoding an 8-bit 2x3 PGX image and converting to <see cref="CodecInOut"/>.
        /// Ported from <c>CodecPGXTest.Test8bits</c> in <c>lib/extras/dec/pgx_test.cc</c>.
        /// </summary>
        [Test]
        public unsafe void Test8bits()
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            byte[] pgx                     = Encoding.ASCII.GetBytes("PG ML + 8 2 3\npixels");
            PackedPixelFile ppf            = new PackedPixelFile();

            ((bool)CodecPgx.DecodeImagePGX(pgx, new ColorHints(), ppf)).ShouldBeTrue();

            CodecInOut io = new CodecInOut(memoryManager);

            ((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io)).ShouldBeTrue();
            ImageOps.ScaleImage(255.0f, io.Main.Color());

            io.Metadata.M.BitDepth.FloatingPointSample.ShouldBeFalse();
            io.Metadata.M.BitDepth.BitsPerSample.ShouldBe(8u);
            io.Metadata.M.ColorEncoding.IsGray().ShouldBeTrue();
            io.XSize.ShouldBe(2);
            io.YSize.ShouldBe(3);

            char[][] expected =
            {
                new[] { 'p', 'i' },
                new[] { 'x', 'e' },
                new[] { 'l', 's' },
            };

            for (nuint y = 0; y < 3; ++y)
            {
                for (nuint x = 0; x < 2; ++x)
                {
                    io.Main.Color().Plane(0).Row(y)[x].ShouldBe((float)expected[y][x], 1e-4f);
                }
            }
        }

        /// <summary>
        /// Verifies decoding a 16-bit 2x3 PGX image and converting to <see cref="CodecInOut"/>.
        /// Ported from <c>CodecPGXTest.Test16bits</c> in <c>lib/extras/dec/pgx_test.cc</c>.
        /// </summary>
        [Test]
        public unsafe void Test16bits()
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            byte[] pgx                     = Encoding.ASCII.GetBytes("PG ML + 16 2 3\np_i_x_e_l_s_");
            PackedPixelFile ppf            = new PackedPixelFile();

            ((bool)CodecPgx.DecodeImagePGX(pgx, new ColorHints(), ppf)).ShouldBeTrue();

            CodecInOut io = new CodecInOut(memoryManager);

            ((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io)).ShouldBeTrue();
            ImageOps.ScaleImage(255.0f, io.Main.Color());

            io.Metadata.M.BitDepth.FloatingPointSample.ShouldBeFalse();
            io.Metadata.M.BitDepth.BitsPerSample.ShouldBe(16u);
            io.Metadata.M.ColorEncoding.IsGray().ShouldBeTrue();
            io.XSize.ShouldBe(2);
            io.YSize.ShouldBe(3);

            char[][] expected =
            {
                new[] { 'p', 'i' },
                new[] { 'x', 'e' },
                new[] { 'l', 's' },
            };

            for (nuint y = 0; y < 3; ++y)
            {
                for (nuint x = 0; x < 2; ++x)
                {
                    float expectedValue = (256.0f * expected[y][x]) + '_';
                    (io.Main.Color().Plane(0).Row(y)[x] * 257.0f).ShouldBe(expectedValue, 1e-2f);
                }
            }
        }

        /// <summary>
        /// Verifies that PGX dimensions overflowing <c>nuint</c> during parsing are rejected.
        /// Ported from <c>CodecPGXTest.RejectsDimensionsThatOverflowParseUnsigned</c> in <c>lib/extras/dec/pgx_test.cc</c>.
        /// </summary>
        [Test]
        public void RejectsDimensionsThatOverflowParseUnsigned()
        {
            byte[] pgx          = Encoding.ASCII.GetBytes("PG ML + 8 99999999999999999999999 2\np");
            PackedPixelFile ppf = new PackedPixelFile();

            ((bool)CodecPgx.DecodeImagePGX(pgx, new ColorHints(), ppf)).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies that PGX dimensions whose product <c>xsize * ysize</c> overflows are rejected.
        /// Ported from <c>CodecPGXTest.RejectsDimensionProductOverflow</c> in <c>lib/extras/dec/pgx_test.cc</c>.
        /// </summary>
        [Test]
        public void RejectsDimensionProductOverflow()
        {
            byte[] pgx          = Encoding.ASCII.GetBytes("PG ML + 8 8589934592 8589934592\n");
            PackedPixelFile ppf = new PackedPixelFile();

            ((bool)CodecPgx.DecodeImagePGX(pgx, new ColorHints(), ppf)).ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="GainMapCodec"/> ported from <c>lib/extras/gain_map_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class GainMapTest
    {
        /// <summary>
        /// Verifies roundtripping <see cref="JxlGainMapBundle"/> against the golden binary representation
        /// across all combinations of <paramref name="hasColorEncoding"/> and <paramref name="hasIcc"/>.
        /// Ported from <c>GainMapTest.GainMapRoundtrip</c> in <c>lib/extras/gain_map_test.cc</c>.
        /// </summary>
        /// <param name="hasColorEncoding">Whether color encoding is present.</param>
        /// <param name="hasIcc">Whether alternate ICC profile is present.</param>
        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GainMapRoundtrip(bool hasColorEncoding, bool hasIcc)
        {
            byte[] iccData       = hasIcc ? TestUtils.GetCompressedIccTestProfile() : Array.Empty<byte>();
            byte[] goldenGainMap = BuildGoldenTestGainMap(hasIcc, hasColorEncoding);

            byte[] gainMapMetadata = Encoding.ASCII.GetBytes(
                "placeholder gain map metadata, fill with actual example after (ISO 21496-1) is finalized");
            byte[] gainMapBytes    = Encoding.ASCII.GetBytes(
                "placeholder for an actual naked JPEG XL codestream");

            JxlGainMapBundle origBundle = new JxlGainMapBundle
            {
                JhgmVersion     = 0,
                GainMapMetadata = gainMapMetadata,
                HasColorEncoding = hasColorEncoding,
                AltIcc          = iccData,
                GainMap         = gainMapBytes,
            };

            if (hasColorEncoding)
            {
                origBundle.ColorEncoding = ColorEncoding.LinearSRGB(false).ToExternal();
            }

            GainMapCodec.JxlGainMapGetBundleSize(in origBundle, out nuint bundleSize).ShouldBeTrue();
            bundleSize.ShouldBe((nuint)goldenGainMap.Length);

            byte[] buffer = new byte[bundleSize];

            GainMapCodec.JxlGainMapWriteBundle(in origBundle, buffer, out nuint bytesWritten).ShouldBeTrue();
            bytesWritten.ShouldBe(bundleSize);
            buffer[0].ShouldBe(origBundle.JhgmVersion);
            buffer.ShouldBe(goldenGainMap);

            GainMapCodec.JxlGainMapReadBundle(out JxlGainMapBundle outputBundle, buffer, out nuint bytesRead).ShouldBeTrue();
            bytesRead.ShouldBe(bundleSize);
            outputBundle.JhgmVersion.ShouldBe(origBundle.JhgmVersion);
            outputBundle.HasColorEncoding.ShouldBe(hasColorEncoding);
            outputBundle.GainMapMetadata.ShouldBe(gainMapMetadata);
            outputBundle.AltIcc.ShouldBe(iccData);
            outputBundle.GainMap.ShouldBe(gainMapBytes);
        }

        private static byte[] BuildGoldenTestGainMap(bool hasIcc, bool hasColorEncoding)
        {
            List<byte> gainMap = new List<byte> { 0x00, 0x00, 0x58 };
            gainMap.AddRange(Encoding.ASCII.GetBytes(
                "placeholder gain map metadata, fill with actual example after (ISO 21496-1) is finalized"));

            gainMap.Add(hasColorEncoding ? (byte)3 : (byte)0);

            if (hasColorEncoding)
            {
                gainMap.AddRange(new byte[] { 0x50, 0xB4, 0x00 });
            }

            if (hasIcc)
            {
                byte[] iccData = TestUtils.GetCompressedIccTestProfile();
                int len        = iccData.Length;
                gainMap.Add((byte)((len >> 24) & 0xFF));
                gainMap.Add((byte)((len >> 16) & 0xFF));
                gainMap.Add((byte)((len >> 8) & 0xFF));
                gainMap.Add((byte)(len & 0xFF));
                gainMap.AddRange(iccData);
            }
            else
            {
                gainMap.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
            }

            gainMap.AddRange(Encoding.ASCII.GetBytes("placeholder for an actual naked JPEG XL codestream"));

            return gainMap.ToArray();
        }
    }

    /// <summary>
    /// Unit tests for format codecs (<see cref="CodecPnm"/>, <see cref="CodecPgx"/>, <see cref="CodecApng"/>,
    /// <see cref="ExtrasCommon"/>, <see cref="ExtrasDecodeFacade"/>, <see cref="ExtrasEncodeFacade"/>)
    /// ported from <c>lib/extras/codec_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class CodecTest
    {
        /// <summary>
        /// Verifies roundtripping images across PNG, PNM, and PGX codecs for various bit depths,
        /// grayscale/RGB, alpha, endianness, and extra channels.
        /// Ported from <c>CodecTest.TestRoundTrip</c> in <c>lib/extras/codec_test.cc</c>.
        /// </summary>
        [Test]
        public void TestRoundTrip()
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);

            Codec[] codecs        = { Codec.Png, Codec.Pnm, Codec.Pgx };
            int[] bitsPerSamples  = { 4, 8, 10, 12, 16, 32 };
            bool[] bools          = { false, true };

            foreach (Codec codec in codecs)
            {
                foreach (int bps in bitsPerSamples)
                {
                    foreach (bool isGray in bools)
                    {
                        foreach (bool addAlpha in bools)
                        {
                            foreach (bool bigEndian in bools)
                            {
                                RunSingleRoundTrip(codec, (nuint)bps, isGray, addAlpha, bigEndian, false, pool.Pool);

                                if (codec == Codec.Pnm && isGray && !addAlpha && bps <= 16)
                                {
                                    RunSingleRoundTrip(codec, (nuint)bps, isGray, addAlpha, bigEndian, true, pool.Pool);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Verifies byte-exact lossless roundtripping of all <c>jxl/flower/flower_small.*.depth*</c> PNM/PGM/PPM/PAM test files.
        /// Ported from <c>CodecTest.LosslessPNMRoundtrip</c> in <c>lib/extras/codec_test.cc</c>.
        /// </summary>
        [Test]
        public void LosslessPNMRoundtrip()
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);

            string[] kChannels  = { "", "g", "ga", "rgb", "rgba" };
            string[] kExtension = { "", ".pgm", ".pam", ".ppm", ".pam" };

            for (int bitDepth = 1; bitDepth <= 16; ++bitDepth)
            {
                for (int channels = 1; channels <= 4; ++channels)
                {
                    if (bitDepth == 1 && (channels == 2 || channels == 4))
                    {
                        continue;
                    }

                    string extension = kExtension[channels];
                    string filename  = $"jxl/flower/flower_small.{kChannels[channels]}.depth{bitDepth}{extension}";
                    byte[] orig      = TestUtils.ReadTestData(filename);

                    PackedPixelFile ppf   = new PackedPixelFile();
                    ColorHints colorHints = new ColorHints();

                    colorHints.Add("color_space", channels < 3 ? "Gra_D65_Rel_SRG" : "RGB_D65_SRG_Rel_SRG");

                    ((bool)ExtrasDecodeFacade.DecodeBytes(orig, colorHints, ppf)).ShouldBeTrue(filename);

                    Extras.Encoder? encoder = ExtrasEncodeFacade.FromExtension(extension);

                    encoder.ShouldNotBeNull(filename);

                    EncodedImage encoded = new EncodedImage();

                    ((bool)encoder.Encode(ppf, encoded, pool.Pool)).ShouldBeTrue(filename);
                    encoded.Bitstreams.Count.ShouldBe(1);
                    encoded.Bitstreams[0].Length.ShouldBe(orig.Length, filename);
                    encoded.Bitstreams[0].AsSpan().SequenceEqual(orig).ShouldBeTrue(filename);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="CodecPnm.PnmParseUnsigned"/> and <see cref="CodecPnm.PnmParseSigned"/>.
        /// Ported from <c>CodecTest.TestPNM</c> in <c>lib/extras/codec_test.cc</c>.
        /// </summary>
        [Test]
        public void TestPNM()
        {
            string maxUnsigned         = nuint.MaxValue.ToString();
            string overflowingUnsigned = maxUnsigned + "0";

            ((bool)CodecPnm.PnmParseUnsigned(""u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseUnsigned("+"u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseUnsigned("-"u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseUnsigned("A"u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseUnsigned(Encoding.ASCII.GetBytes(overflowingUnsigned), out _)).ShouldBeFalse();

            ((bool)CodecPnm.PnmParseSigned(""u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseSigned("+"u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseSigned("-"u8, out _)).ShouldBeFalse();
            ((bool)CodecPnm.PnmParseSigned("A"u8, out _)).ShouldBeFalse();

            ((bool)CodecPnm.PnmParseUnsigned("1"u8, out nuint u)).ShouldBeTrue();
            u.ShouldBe((nuint)1);

            ((bool)CodecPnm.PnmParseUnsigned("32"u8, out u)).ShouldBeTrue();
            u.ShouldBe((nuint)32);

            ((bool)CodecPnm.PnmParseUnsigned(Encoding.ASCII.GetBytes(maxUnsigned), out u)).ShouldBeTrue();
            u.ShouldBe(nuint.MaxValue);

            ((bool)CodecPnm.PnmParseSigned("1"u8, out double d)).ShouldBeTrue();
            d.ShouldBe(1.0);

            ((bool)CodecPnm.PnmParseSigned("+2"u8, out d)).ShouldBeTrue();
            d.ShouldBe(2.0);

            ((bool)CodecPnm.PnmParseSigned("-3"u8, out d)).ShouldBeTrue();
            d.ShouldBe(-3.0, 1e-15);

            ((bool)CodecPnm.PnmParseSigned("3.141592"u8, out d)).ShouldBeTrue();
            d.ShouldBe(3.141592, 1e-15);

            ((bool)CodecPnm.PnmParseSigned("-3.141592"u8, out d)).ShouldBeTrue();
            d.ShouldBe(-3.141592, 1e-15);
        }

        /// <summary>
        /// Verifies pixel format negotiation via <see cref="ExtrasCommon.SelectFormat"/>.
        /// Ported from <c>CodecTest.FormatNegotiation*</c> in <c>lib/extras/codec_test.cc</c>.
        /// </summary>
        [Test]
        public void FormatNegotiation()
        {
            JxlPixelFormat[] acceptedFormats =
            {
                new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.NativeEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.NativeEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.NativeEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.NativeEndian, Align = 0 },
            };

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.BitsPerSample    = 12;
            info.NumColorChannels = 2;

            ((bool)ExtrasCommon.SelectFormat(acceptedFormats, in info, out JxlPixelFormat format)).ShouldBeFalse();

            info.NumColorChannels = 3;
            ((bool)ExtrasCommon.SelectFormat(acceptedFormats, in info, out format)).ShouldBeTrue();
            format.NumChannels.ShouldBe(info.NumColorChannels);
            format.DataType.ShouldBe(JxlDataType.UInt16);

            // Grayscale promotion
            JxlPixelFormat[] rgbOnlyFormats =
            {
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.NativeEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.NativeEndian, Align = 0 },
            };

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo grayInfo);
            grayInfo.BitsPerSample    = 8;
            grayInfo.NumColorChannels = 1;

            ((bool)ExtrasCommon.SelectFormat(rgbOnlyFormats, in grayInfo, out format)).ShouldBeTrue();
            format.NumChannels.ShouldBe(3u);
            format.DataType.ShouldBe(JxlDataType.UInt8);

            // Gray+Alpha promotion
            grayInfo.AlphaBits = 8;
            ((bool)ExtrasCommon.SelectFormat(rgbOnlyFormats, in grayInfo, out format)).ShouldBeTrue();
            format.NumChannels.ShouldBe(3u);
            format.DataType.ShouldBe(JxlDataType.UInt8);

            // No match
            JxlPixelFormat[] fiveChanFormats =
            {
                new JxlPixelFormat { NumChannels = 5, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.NativeEndian, Align = 0 },
            };
            grayInfo.AlphaBits = 0;
            ((bool)ExtrasCommon.SelectFormat(fiveChanFormats, in grayInfo, out format)).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies decoding and re-encoding a real PNG image from <c>testdata</c>.
        /// Ported from <c>CodecTest.EncodeToPNG</c> in <c>lib/extras/codec_test.cc</c>.
        /// </summary>
        [Test]
        public void EncodeToPNG()
        {
            Extras.Encoder? pngEncoder = ExtrasEncodeFacade.FromExtension(".png");

            pngEncoder.ShouldNotBeNull();

            byte[] originalPng  = TestUtils.ReadTestData("external/wesaturate/500px/tmshre_riaphotographs_srgb8.png");
            PackedPixelFile ppf = new PackedPixelFile();

            ((bool)ExtrasDecodeFacade.DecodeBytes(originalPng, new ColorHints(), ppf)).ShouldBeTrue();

            JxlPixelFormat format   = ppf.Frames[0].Color.Format;
            bool acceptedMatch      = false;

            foreach (JxlPixelFormat candidate in pngEncoder.AcceptedFormats())
            {
                if (candidate.NumChannels == format.NumChannels &&
                    candidate.DataType == format.DataType &&
                    candidate.Endianness == format.Endianness)
                {
                    acceptedMatch = true;
                    break;
                }
            }

            acceptedMatch.ShouldBeTrue();

            EncodedImage encodedPng = new EncodedImage();

            ((bool)pngEncoder.Encode(ppf, encodedPng, null)).ShouldBeTrue();
            encodedPng.Icc.ShouldBeEmpty();
            encodedPng.Bitstreams.Count.ShouldBe(1);

            PackedPixelFile decodedPpf = new PackedPixelFile();

            ((bool)ExtrasDecodeFacade.DecodeBytes(encodedPng.Bitstreams[0], new ColorHints(), decodedPpf)).ShouldBeTrue();
            decodedPpf.Info.BitsPerSample.ShouldBe(ppf.Info.BitsPerSample);
            decodedPpf.Frames.Count.ShouldBe(1);

            VerifySameImage(
                ppf.Frames[0].Color,
                ppf.Info.BitsPerSample,
                decodedPpf.Frames[0].Color,
                decodedPpf.Info.BitsPerSample);
        }

        private static void RunSingleRoundTrip(
            Codec codec,
            nuint bitsPerSample,
            bool isGray,
            bool addAlpha,
            bool bigEndian,
            bool addExtraChannels,
            ThreadPool? pool)
        {
            if (!ShouldTestRoundtrip(codec, bitsPerSample, isGray, addAlpha, bigEndian))
            {
                return;
            }

            string extension       = ExtensionFromCodec(codec, isGray, addAlpha, bitsPerSample, addExtraChannels);
            Extras.Encoder? encoder = ExtrasEncodeFacade.FromExtension(extension);

            ExtrasDecodeFacade.CanDecode(codec).ShouldBeTrue();
            encoder.ShouldNotBeNull();

            PackedPixelFile ppfIn = CreateTestImage(codec, 7, 4, bitsPerSample, isGray, addAlpha, bigEndian, addExtraChannels);
            EncodedImage encoded  = new EncodedImage();

            ((bool)encoder.Encode(ppfIn, encoded, pool)).ShouldBeTrue();
            encoded.Bitstreams.Count.ShouldBe(1);

            PackedPixelFile ppfOut = new PackedPixelFile();
            ColorHints colorHints  = new ColorHints();

            if (codec == Codec.Pnm || codec == Codec.Pgx)
            {
                colorHints.Add("color_space", isGray ? "Gra_D65_Rel_SRG" : "RGB_D65_SRG_Rel_SRG");
            }

            ((bool)ExtrasDecodeFacade.DecodeBytes(encoded.Bitstreams[0], colorHints, ppfOut)).ShouldBeTrue();
            ppfOut.Frames.Count.ShouldBe(1);

            VerifySameImage(
                ppfIn.Frames[0].Color,
                ppfIn.Info.BitsPerSample,
                ppfOut.Frames[0].Color,
                ppfOut.Info.BitsPerSample);

            ppfOut.Frames[0].ExtraChannels.Count.ShouldBe(ppfIn.Frames[0].ExtraChannels.Count);

            for (int i = 0; i < ppfIn.Frames[0].ExtraChannels.Count; i++)
            {
                VerifySameImage(
                    ppfIn.Frames[0].ExtraChannels[i],
                    ppfIn.Info.BitsPerSample,
                    ppfOut.Frames[0].ExtraChannels[i],
                    ppfOut.Info.BitsPerSample);
                ppfOut.ExtraChannelsInfo[i].EcInfo.Type.ShouldBe(ppfIn.ExtraChannelsInfo[i].EcInfo.Type);
            }
        }

        private static bool ShouldTestRoundtrip(Codec codec, nuint bitsPerSample, bool isGray, bool addAlpha, bool bigEndian)
        {
            if (codec == Codec.Png)
            {
                return bitsPerSample <= 16;
            }

            if (codec == Codec.Pnm)
            {
                return (bitsPerSample <= 16 && bigEndian) || (bitsPerSample == 32 && !addAlpha && !bigEndian);
            }

            if (codec == Codec.Pgx)
            {
                return (bitsPerSample == 8 || (bitsPerSample == 16 && bigEndian)) && isGray && !addAlpha;
            }

            return false;
        }

        private static string ExtensionFromCodec(
            Codec codec,
            bool isGray,
            bool hasAlpha,
            nuint bitsPerSample,
            bool addExtraChannels = false)
        {
            return codec switch
            {
                Codec.Pgx => ".pgx",
                Codec.Png => ".png",
                Codec.Pnm when bitsPerSample == 32 => ".pfm",
                Codec.Pnm when hasAlpha || addExtraChannels => ".pam",
                Codec.Pnm => isGray ? ".pgm" : ".ppm",
                _ => string.Empty,
            };
        }

        private static PackedPixelFile CreateTestImage(
            Codec codec,
            nuint xsize,
            nuint ysize,
            nuint bitsPerSample,
            bool isGray,
            bool addAlpha,
            bool bigEndian,
            bool addExtraChannels)
        {
            PackedPixelFile ppf                = new PackedPixelFile();
            ppf.Info.XSize                     = (uint)xsize;
            ppf.Info.YSize                     = (uint)ysize;
            ppf.Info.BitsPerSample             = (uint)bitsPerSample;
            ppf.Info.ExponentBitsPerSample     = bitsPerSample == 32 ? 8u : 0u;
            ppf.Info.NumColorChannels          = isGray ? 1u : 3u;
            ppf.Info.AlphaBits                 = addAlpha ? (uint)bitsPerSample : 0u;
            ppf.Info.AlphaPremultiplied        = codec == Codec.Exr;
            ppf.Info.Orientation               = JxlOrientation.Identity;

            ColorEncoding cInternal            = ColorEncoding.LinearSRGB(isGray);
            ppf.ColorEncoding                  = cInternal.ToExternal();
            ppf.Icc                            = cInternal.ICC();

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = (isGray ? 1u : 3u) + (addAlpha ? 1u : 0u),
                DataType    = bitsPerSample == 32 ? JxlDataType.Float : (bitsPerSample > 8 ? JxlDataType.UInt16 : JxlDataType.UInt8),
                Endianness  = bigEndian ? JxlEndianness.BigEndian : JxlEndianness.LittleEndian,
                Align       = 0,
            };

            StatusOr<PackedFrame> frameOr = PackedFrame.Create(xsize, ysize, in format);
            frameOr.IsOk.ShouldBeTrue();

            PackedFrame frame = frameOr.Value;
            FillPackedImage(bitsPerSample, frame.Color);

            if (addExtraChannels)
            {
                for (int i = 0; i < 7; i++)
                {
                    JxlPixelFormat ecFormat = format;
                    ecFormat.NumChannels    = 1;

                    StatusOr<PackedImage> ecOr = PackedImage.Create(xsize, ysize, in ecFormat);
                    ecOr.IsOk.ShouldBeTrue();

                    PackedImage ec = ecOr.Value;
                    FillPackedImage(bitsPerSample, ec);
                    frame.ExtraChannels.Add(ec);

                    PackedExtraChannel pec   = new PackedExtraChannel();
                    pec.EcInfo.BitsPerSample = (uint)bitsPerSample;
                    pec.EcInfo.Type          = (JxlExtraChannelType)i;
                    ppf.ExtraChannelsInfo.Add(pec);
                }
            }

            ppf.Frames.Add(frame);

            return ppf;
        }

        private static void FillPackedImage(nuint bitsPerSample, PackedImage image)
        {
            JxlPixelFormat format = image.Format;
            int bytesPerChannel   = (int)(PackedImage.BitsPerChannel(format.DataType) / 8);
            Span<byte> dst        = image.Pixels();
            Rng rng               = new Rng(129);
            int offset            = 0;
            ulong maxVal          = (1UL << (int)bitsPerSample) - 1UL;

            for (nuint y = 0; y < image.YSize; y++)
            {
                for (nuint x = 0; x < image.XSize; x++)
                {
                    for (nuint c = 0; c < format.NumChannels; c++)
                    {
                        if (format.DataType == JxlDataType.UInt8)
                        {
                            dst[offset] = (byte)rng.UniformU(0, (uint)maxVal);
                        }
                        else if (format.DataType == JxlDataType.UInt16)
                        {
                            uint val = (uint)rng.UniformU(0, (uint)maxVal);

                            if (format.Endianness == JxlEndianness.BigEndian)
                            {
                                ByteOrder.StoreBE16(val, dst.Slice(offset, 2));
                            }
                            else
                            {
                                ByteOrder.StoreLE16(val, dst.Slice(offset, 2));
                            }
                        }
                        else
                        {
                            float val = rng.UniformF(0.0f, 1.0f);
                            uint uval = BitConverter.SingleToUInt32Bits(val);

                            if (format.Endianness == JxlEndianness.BigEndian)
                            {
                                ByteOrder.StoreBE32(uval, dst.Slice(offset, 4));
                            }
                            else
                            {
                                ByteOrder.StoreLE32(uval, dst.Slice(offset, 4));
                            }
                        }

                        offset += bytesPerChannel;
                    }
                }
            }
        }

        private static void VerifySameImage(
            PackedImage im0,
            nuint bitsPerSample0,
            PackedImage im1,
            nuint bitsPerSample1)
        {
            im0.XSize.ShouldBe(im1.XSize);
            im0.YSize.ShouldBe(im1.YSize);
            im0.Format.NumChannels.ShouldBe(im1.Format.NumChannels);

            JxlPixelFormat fmt0 = im0.Format;
            JxlPixelFormat fmt1 = im1.Format;

            double factor0 = 1.0 / ((1UL << (int)Math.Min(TestUtils.GetPrecision(fmt0.DataType), bitsPerSample0)) - 1UL);
            double factor1 = 1.0 / ((1UL << (int)Math.Min(TestUtils.GetPrecision(fmt1.DataType), bitsPerSample1)) - 1UL);

            double[] rgba0   = TestUtils.ConvertToRGBA32(im0.Pixels(), im0.XSize, im0.YSize, in fmt0, factor0);
            double[] rgba1   = TestUtils.ConvertToRGBA32(im1.Pixels(), im1.XSize, im1.YSize, in fmt1, factor1);
            double tolerance = (bitsPerSample0 == 32 || bitsPerSample1 == 32)
                ? 0.5 * Math.Max(factor0, factor1)
                : 0.5 * Math.Min(factor0, factor1);

            for (nuint y = 0; y < im0.YSize; y++)
            {
                for (nuint x = 0; x < im0.XSize; x++)
                {
                    for (nuint c = 0; c < fmt0.NumChannels; c++)
                    {
                        int ix = (int)(((y * im0.XSize + x) * 4) + c);
                        rgba1[ix].ShouldBe(rgba0[ix], tolerance);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="CodecJxl"/>, <see cref="CodecNpy"/>, <see cref="ExtrasCodec"/>,
    /// <see cref="ExtrasAlphaBlend"/>, <see cref="ExtrasExif"/>, <see cref="ExtrasHlg"/>,
    /// <see cref="ExtrasMetrics"/>, <see cref="ExtrasToneMapping"/>, and <see cref="TestImage"/>.
    /// </summary>
    [TestFixture]
    public sealed class ExtrasComponentsAndJxlRoundtripTests
    {
        /// <summary>
        /// Verifies encoding and decoding a <see cref="PackedPixelFile"/> via <see cref="CodecJxl.EncodeImageJXL"/>
        /// and <see cref="CodecJxl.DecodeImageJXL"/>.
        /// </summary>
        [Test]
        public void JxlEncodeAndDecodeRoundtrip()
        {
            TestImage testImg            = new TestImage();
            testImg.Ppf.Info.Orientation = JxlOrientation.Identity;

            ((bool)testImg.SetDimensions(8, 8)).ShouldBeTrue();
            ((bool)testImg.SetChannels(3)).ShouldBeTrue();
            testImg.SetAllBitDepths(8);
            ((bool)testImg.SetColorEncoding("RGB_D65_SRG_Rel_Lin")).ShouldBeTrue();

            StatusOr<TestImage.Frame> frameOr = testImg.AddFrame();
            frameOr.IsOk.ShouldBeTrue();
            frameOr.Value.RandomFill();

            JXLCompressParams cparams = TestUtils.CompressParamsForLossless();

            bool encoded = CodecJxl.EncodeImageJXL(cparams, testImg.Ppf, null, out byte[] compressed);

            encoded.ShouldBeTrue();
            compressed.Length.ShouldBeGreaterThan(2);

            byte[] jxlTestData          = TestUtils.ReadTestData("jxl/blending/cropped_traffic_light.jxl");
            JXLDecompressParams dparams = new JXLDecompressParams();
            TestUtils.DefaultAcceptedFormats(dparams);
            dparams.UseImageCallback    = false;
            PackedPixelFile decodedPpf  = new PackedPixelFile();

            bool decoded = CodecJxl.DecodeImageJXL(jxlTestData, dparams, out nuint decodedBytes, decodedPpf);

            decoded.ShouldBeTrue();
            decodedBytes.ShouldBe((nuint)jxlTestData.Length);
            decodedPpf.Frames.Count.ShouldBe(4);
        }

        /// <summary>
        /// Verifies <see cref="CodecNpy.GetNumPyEncoder"/> produces a valid <c>.npy</c> header and payload.
        /// </summary>
        [Test]
        public void NumPyEncoderProducesValidOutput()
        {
            Extras.Encoder? npyEncoder = ExtrasEncodeFacade.FromExtension(".npy");

            npyEncoder.ShouldNotBeNull();

            TestImage testImg = new TestImage();
            ((bool)testImg.SetDimensions(4, 3)).ShouldBeTrue();
            ((bool)testImg.SetChannels(3)).ShouldBeTrue();
            testImg.SetDataType(JxlDataType.Float);

            StatusOr<TestImage.Frame> frameOr = testImg.AddFrame();
            frameOr.IsOk.ShouldBeTrue();
            frameOr.Value.ZeroFill();

            EncodedImage encoded = new EncodedImage();

            ((bool)npyEncoder.Encode(testImg.Ppf, encoded, null)).ShouldBeTrue();
            encoded.Bitstreams.Count.ShouldBe(1);
            encoded.Bitstreams[0][0].ShouldBe((byte)0x93);
            Encoding.ASCII.GetString(encoded.Bitstreams[0], 1, 5).ShouldBe("NUMPY");
        }

        /// <summary>
        /// Verifies <see cref="ExtrasAlphaBlend.AlphaBlend"/>, <see cref="ExtrasExif.ResetExifOrientation"/>,
        /// <see cref="ExtrasHlg"/>, <see cref="ExtrasMetrics"/>, and <see cref="ExtrasToneMapping"/>.
        /// </summary>
        [Test]
        public void ExtrasUtilitiesOperations()
        {
            // HLG gamma calculation
            float gamma = ExtrasHlg.GetHlgGamma(1000.0f);
            gamma.ShouldBe(1.2f, 1e-3f);

            // AlphaBlend on 8-bit RGBA PackedPixelFile
            TestImage testImg = new TestImage();
            ((bool)testImg.SetDimensions(4, 4)).ShouldBeTrue();
            ((bool)testImg.SetChannels(4)).ShouldBeTrue();
            testImg.SetAllBitDepths(8);

            StatusOr<TestImage.Frame> frameOr = testImg.AddFrame();
            frameOr.IsOk.ShouldBeTrue();
            frameOr.Value.RandomFill();

            ReadOnlySpan<float> bg = stackalloc float[] { 1.0f, 1.0f, 1.0f };
            ((bool)ExtrasAlphaBlend.AlphaBlend(testImg.Ppf, bg)).ShouldBeTrue();
            testImg.Ppf.Info.AlphaBits.ShouldBe(0u);
            testImg.Ppf.Frames[0].Color.Format.NumChannels.ShouldBe(3u);

            // Exif orientation reset
            byte[] exif =
            {
                (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
                0x00, 0x01,
                0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };
            ExtrasExif.ResetExifOrientation(exif);
            exif[19].ShouldBe((byte)1);

            // Codec detection & facade helpers
            ExtrasDecodeFacade.ListOfDecodeCodecs().ShouldContain("PNM");
            ExtrasEncodeFacade.ListOfEncodeCodecs().ShouldContain("pnm");

            nuint bps  = 0;
            string ext = string.Empty;
            ExtrasDecodeFacade.CodecFromPath("image.ppm", ref bps, ref ext).ShouldBe(Codec.Pnm);
            CodecJpg.CanDecodeJPG().ShouldBeFalse();
            CodecGif.CanDecodeGIF().ShouldBeFalse();
            CodecExr.CanDecodeEXR().ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="DctForTest"/> and <see cref="FakeParallelRunner"/> in <see cref="JxlSharp.TestLib"/>.
        /// </summary>
        [Test]
        public void TestLibDctAndFakeParallelRunner()
        {
            double[] block = { 1.0, 2.0, 3.0, 4.0 };
            double[] coeffs = new double[4];
            double[] recon  = new double[4];

            DctForTest.Dct1D(block, coeffs, 4);
            DctForTest.Idct1D(coeffs, recon, 4);

            for (int i = 0; i < 4; i++)
            {
                recon[i].ShouldBe(block[i], 1e-9);
            }

            FakeParallelRunner runner = new FakeParallelRunner(orderSeed: 42, numThreads: 2);
            ThreadPool pool           = new ThreadPool(FakeParallelRunner.Runner, runner.RunnerOpaque);
            int sum                   = 0;

            bool ran = DataParallel.RunOnPool(
                pool,
                0,
                8,
                static _ => Status.Ok,
                (task, _) =>
                {
                    System.Threading.Interlocked.Add(ref sum, (int)task);

                    return Status.Ok;
                },
                "TestFakeRunner");

            ran.ShouldBeTrue();
            sum.ShouldBe(28);
        }
    }
}
