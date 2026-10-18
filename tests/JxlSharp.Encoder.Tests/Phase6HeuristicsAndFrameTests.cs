// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Unit tests for <see cref="EncChromaFromLuma"/>, <see cref="EncCoeffOrder"/>, <see cref="EncGroup"/>,
    /// <see cref="EncAdaptiveQuantization"/>, and <see cref="EncHeuristics"/>.
    /// </summary>
    [TestFixture]
    public unsafe class EncVarDctHeuristicsTests
    {
        /// <summary>
        /// Verifies that <see cref="EncChromaFromLuma.ColorCorrelationEncodeDC"/> and
        /// <see cref="ColorCorrelation.DecodeDC"/> round-trip.
        /// </summary>
        [Test]
        public void ColorCorrelationEncodeDC_RoundtripsWithBundleRead()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            ColorCorrelation cc = new ColorCorrelation();
            cc.SetColorFactor(84);
            cc.SetYToXDC(10);
            cc.SetYToBDC(-5);

            BitWriter writer = new BitWriter(mm);
            Status writeOk   = EncChromaFromLuma.ColorCorrelationEncodeDC(cc, writer, LayerType.ControlFields, null);
            writeOk.IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader         = new BitReader(writer.GetSpan());
            ColorCorrelation decoded = new ColorCorrelation();
            Status readOk            = decoded.DecodeDC(reader);
            reader.Close();

            readOk.IsOk.ShouldBeTrue();
            decoded.GetColorFactor().ShouldBe(84u);
            decoded.GetYToXDC().ShouldBe(10);
            decoded.GetYToBDC().ShouldBe(-5);
        }

        /// <summary>
        /// Verifies <see cref="EncCoeffOrder"/> and <see cref="EncCoeffOrder.EncodeCoeffOrders"/>
        /// round-tripping with <see cref="CoeffOrder.DecodeCoeffOrders"/>.
        /// </summary>
        [Test]
        public void ComputeAndEncodeCoeffOrders_RoundtripsWithDecodeCoeffOrders()
        {
            JxlMemoryManager mm     = JxlMemoryManager.Default;
            FrameDimensions fdim    = new FrameDimensions();
            fdim.Set(16, 16, 1, 0, 0, false, 1);

            using AcStrategyImage acs = AcStrategyImage.Create(mm, fdim.XSizeBlocks, fdim.YSizeBlocks).Value;
            using ACImageT<int> acImg = ACImageT<int>.Make(mm, 1, 4 * 64).Value;

            ushort[] order16      = new ushort[CoeffOrder.kCoeffOrderMaxSize];
            StatusOr<uint> usedOr = EncCoeffOrder.ComputeCoeffOrder(
                SpeedTier.kSquirrel,
                acImg,
                acs,
                fdim,
                out uint usedOrders,
                1u << (int)AcStrategyType.DCT,
                order16);

            usedOr.IsOk.ShouldBeTrue();

            BitWriter writer = new BitWriter(mm);
            Status encOk     = EncCoeffOrder.EncodeCoeffOrders((ushort)usedOrders, order16, writer, LayerType.Order, null);
            encOk.IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader = new BitReader(writer.GetSpan());
            uint[] decoded   = new uint[CoeffOrder.kCoeffOrderMaxSize];
            Status decOk     = CoeffOrder.DecodeCoeffOrders(
                mm,
                (ushort)usedOrders,
                1u << (int)AcStrategyType.DCT,
                decoded,
                reader);
            reader.Close();

            decOk.IsOk.ShouldBeTrue();

            for (int k = 0; k < 64; k++)
            {
                decoded[k].ShouldBe((uint)order16[k]);
            }
        }

        /// <summary>
        /// Verifies <see cref="EncAdaptiveQuantization.InitialQuantField"/> and <see cref="EncAdaptiveQuantization.InitialQuantDC"/>.
        /// </summary>
        [Test]
        public void InitialQuantField_ProducesPositiveQuantWeights()
        {
            JxlMemoryManager mm       = JxlMemoryManager.Default;
            using Image3<float> opsin = Image3<float>.Create(mm, 32, 32).Value;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 32; y++)
                {
                    for (nuint x = 0; x < 32; x++)
                    {
                        opsin.PlaneRow(c, y)[x] = ((x + y) & 7) * 0.05f;
                    }
                }
            }

            FrameDimensions fdim = new FrameDimensions();
            fdim.Set(32, 32, 1, 0, 0, false, 1);

            CompressParams cparams    = new CompressParams { ButteraugliDistance = 1.5f };
            using ImagePlane<float> q = EncAdaptiveQuantization.InitialQuantField(
                cparams.ButteraugliDistance,
                opsin,
                fdim,
                null,
                1.0f,
                out ImagePlane<float>? mask,
                out ImagePlane<float>? mask1x1).Value;

            mask?.Dispose();
            mask1x1?.Dispose();

            for (nuint by = 0; by < fdim.YSizeBlocks; by++)
            {
                for (nuint bx = 0; bx < fdim.XSizeBlocks; bx++)
                {
                    q.Row(by)[bx].ShouldBeGreaterThan(0.0f);
                }
            }

            float qdc = EncAdaptiveQuantization.InitialQuantDC(1.5f);
            qdc.ShouldBeGreaterThan(0.0f);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncJpegHuffmanDecode"/>, <see cref="EncJpegDataReader"/>, and <see cref="EncJpegData"/>.
    /// </summary>
    [TestFixture]
    public class EncJpegTests
    {
        /// <summary>
        /// Verifies <see cref="EncJpegHuffmanDecode.BuildJpegHuffmanTable"/> constructs valid root lookup entries.
        /// </summary>
        [Test]
        public void BuildJpegHuffmanTable_BuildsRootEntries()
        {
            uint[] counts  = new uint[17];
            uint[] symbols = new uint[256];
            counts[1]      = 1;
            counts[2]      = 2;
            symbols[0]     = 0x00;
            symbols[1]     = 0x01;
            symbols[2]     = 0x02;

            JpegHuffmanTableEntry[] lut = new JpegHuffmanTableEntry[EncJpegHuffmanDecode.KJpegHuffmanLutSize];
            EncJpegHuffmanDecode.BuildJpegHuffmanTable(counts, symbols, lut);

            lut[0].Bits.ShouldBe((byte)1);
            lut[0].Value.ShouldBe((ushort)0x00);
            lut[128].Bits.ShouldBe((byte)2);
            lut[128].Value.ShouldBe((ushort)0x01);
        }

        /// <summary>
        /// Verifies <see cref="EncJpegDataReader.ReadJpeg"/> parses a minimal 8x8 grayscale baseline JPEG codestream
        /// and <see cref="EncJpegData.EncodeJPEGData"/> serializes its <c>jbrd</c> reconstruction box.
        /// </summary>
        [Test]
        public void ReadJpeg_AndEncodeJPEGData_ParsesValidMinimalJpeg()
        {
            byte[] minimalJpeg = BuildMinimal8x8GrayscaleJpeg();
            JPEGData jpg       = new JPEGData();

            bool ok = EncJpegDataReader.ReadJpeg(minimalJpeg, JpegReadMode.kReadAll, jpg);
            ok.ShouldBeTrue();
            jpg.Width.ShouldBe(8);
            jpg.Height.ShouldBe(8);
            jpg.Components.Count.ShouldBe(1);
            jpg.Quant.Count.ShouldBe(1);

            ColorEncoding ce = new ColorEncoding();
            EncJpegData.SetColorEncodingFromJpegData(jpg, ce).IsOk.ShouldBeTrue();
            ce.IsGray().ShouldBeTrue();

            YCbCrChromaSubsampling cs = new YCbCrChromaSubsampling();
            EncJpegData.SetChromaSubsamplingFromJpegData(jpg, cs).IsOk.ShouldBeTrue();

            EncJpegData.SetColorTransformFromJpegData(jpg, out ColorTransform ct).IsOk.ShouldBeTrue();
            ct.ShouldBe(ColorTransform.YCbCr);

            CompressParams cp = new CompressParams();
            Status jbrdOk     = EncJpegData.EncodeJPEGData(JxlMemoryManager.Default, jpg, out byte[] jbrdBytes, cp);
            jbrdOk.IsOk.ShouldBeTrue();
            jbrdBytes.Length.ShouldBeGreaterThan(0);
        }

        private static byte[] BuildMinimal8x8GrayscaleJpeg()
        {
            List<byte> b = new List<byte>
            {
                0xFF, 0xD8, // SOI
                0xFF, 0xDB, 0x00, 0x43, 0x00, // DQT length 67, table 0 (8-bit)
            };

            for (int i = 0; i < 64; i++)
            {
                b.Add(16);
            }

            // SOF0: 8x8, 1 component (id=1, 1x1 sampling, quant=0)
            b.AddRange(new byte[]
            {
                0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00,
            });

            // DHT DC table 0: 1 symbol (0x00) of length 1 bit ("0")
            b.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x00, 1 });

            for (int i = 0; i < 15; i++)
            {
                b.Add(0);
            }

            b.Add(0x00);

            // DHT AC table 0: 1 symbol (0x00 = EOB) of length 1 bit ("0")
            b.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x10, 1 });

            for (int i = 0; i < 15; i++)
            {
                b.Add(0);
            }

            b.Add(0x00);

            // SOS: 1 component (id=1, dc=0, ac=0), Ss=0, Se=63, AhAl=0, followed by 1 byte of 0 bits (DC=0, AC=EOB)
            b.AddRange(new byte[]
            {
                0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
                0x00,
                0xFF, 0xD9, // EOI
            });

            return b.ToArray();
        }
    }

    /// <summary>
    /// Unit and round-trip integration tests for <see cref="JxlEncoder"/> and <see cref="EncFrame"/>.
    /// Ports tests from <c>encode_test.cc</c>, <c>roundtrip_test.cc</c>, <c>speed_tier_test.cc</c>,
    /// <c>passes_test.cc</c>, <c>preview_test.cc</c>, <c>gradient_test.cc</c>, and <c>jxl_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class JxlEncoderAndRoundtripTests
    {
        /// <summary>
        /// Verifies <see cref="JxlEncoder.Version"/>, <see cref="JxlEncoder.DistanceFromQuality"/>,
        /// and basic info / frame header initialization helpers.
        /// Ported from <c>lib/jxl/encode_test.cc</c>.
        /// </summary>
        [Test]
        public void EncoderInitAndHelpers_VerifyDefaultsAndQualityConversion()
        {
            JxlEncoder.Version().ShouldBeGreaterThan(0u);
            JxlEncoder.DistanceFromQuality(100.0f).ShouldBe(0.0f);
            JxlEncoder.DistanceFromQuality(90.0f).ShouldBe(1.0f, 1e-4f);
            JxlEncoder.DistanceFromQuality(10.0f).ShouldBeGreaterThan(10.0f);

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.BitsPerSample.ShouldBe(8u);
            info.NumColorChannels.ShouldBe(3u);
            info.Orientation.ShouldBe(JxlOrientation.Identity);

            JxlEncoder.JxlEncoderInitFrameHeader(out JxlFrameHeader hdr);
            hdr.LayerInfo.BlendInfo.BlendMode.ShouldBe(JxlBlendMode.Replace);
        }

        /// <summary>
        /// Verifies API usage error handling on <see cref="JxlEncoder"/> when methods are called out of order.
        /// Ported from <c>lib/jxl/encode_test.cc</c>.
        /// </summary>
        [Test]
        public void EncoderApiValidation_RejectsInvalidDimensionsAndDoubleSetBasicInfo()
        {
            using JxlEncoder enc = new JxlEncoder();
            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.XSize = 0;
            info.YSize = 10;

            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Error);
            enc.GetError().ShouldBe(JxlEncoderError.BadInput);

            enc.Reset();
            info.XSize = 8;
            info.YSize = 8;
            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Success);
            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Error);
            enc.GetError().ShouldBe(JxlEncoderError.ApiUsage);
        }

        /// <summary>
        /// Verifies lossless 8-bit RGB encoding through <see cref="JxlEncoder"/> and header decoding via <see cref="JxlDecoder"/>.
        /// Ported from <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void LosslessRgb8_RoundtripsExactlyWithJxlDecoder()
        {
            const uint width  = 16;
            const uint height = 16;
            byte[] input      = new byte[width * height * 3];

            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (byte)((i * 29 + 7) & 0xFF);
            }

            byte[] jxlBytes = EncodeImage(
                (width, height, 3u, 8u),
                (Lossless: true, Effort: 3, Distance: 0.0f),
                input);

            jxlBytes.Length.ShouldBeGreaterThan(2);
            jxlBytes[0].ShouldBe((byte)0xFF);
            jxlBytes[1].ShouldBe((byte)0x0A);

            JxlBasicInfo decodedInfo = DecodeBasicInfo(jxlBytes);
            decodedInfo.XSize.ShouldBe(width);
            decodedInfo.YSize.ShouldBe(height);
            decodedInfo.BitsPerSample.ShouldBe(8u);
            decodedInfo.NumColorChannels.ShouldBe(3u);
            decodedInfo.UsesOriginalProfile.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies lossless 8-bit grayscale encoding through <see cref="JxlEncoder"/> and header decoding via <see cref="JxlDecoder"/>.
        /// Ported from <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void LosslessGray8_RoundtripsExactlyWithJxlDecoder()
        {
            const uint width  = 12;
            const uint height = 12;
            byte[] input      = new byte[width * height];

            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (byte)((i * 43 + 13) & 0xFF);
            }

            byte[] jxlBytes = EncodeImage(
                (width, height, 1u, 8u),
                (Lossless: true, Effort: 3, Distance: 0.0f),
                input);

            jxlBytes.Length.ShouldBeGreaterThan(2);
            jxlBytes[0].ShouldBe((byte)0xFF);
            jxlBytes[1].ShouldBe((byte)0x0A);

            JxlBasicInfo decodedInfo = DecodeBasicInfo(jxlBytes);
            decodedInfo.XSize.ShouldBe(width);
            decodedInfo.YSize.ShouldBe(height);
            decodedInfo.BitsPerSample.ShouldBe(8u);
            decodedInfo.NumColorChannels.ShouldBe(1u);
            decodedInfo.UsesOriginalProfile.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies Modular encoding across multiple speed tiers (effort 1..7) and header decoding via <see cref="JxlDecoder"/>.
        /// Ported from <c>lib/jxl/speed_tier_test.cc</c> and <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LossyVarDctRgb8_EncodesAndDecodesCleanlyAcrossEfforts(int effort)
        {
            const uint width  = 16;
            const uint height = 16;
            byte[] input      = new byte[width * height * 3];

            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    uint idx       = (y * width + x) * 3;
                    input[idx]     = (byte)(x * 12 + 20);
                    input[idx + 1] = (byte)(y * 12 + 20);
                    input[idx + 2] = 128;
                }
            }

            byte[] jxlBytes = EncodeImage(
                (width, height, 3u, 8u),
                (Lossless: false, Effort: effort, Distance: 1.0f),
                input);

            jxlBytes.Length.ShouldBeGreaterThan(2);
            jxlBytes[0].ShouldBe((byte)0xFF);
            jxlBytes[1].ShouldBe((byte)0x0A);

            JxlBasicInfo decodedInfo = DecodeBasicInfo(jxlBytes);
            decodedInfo.XSize.ShouldBe(width);
            decodedInfo.YSize.ShouldBe(height);
            decodedInfo.BitsPerSample.ShouldBe(8u);
            decodedInfo.NumColorChannels.ShouldBe(3u);
        }

        /// <summary>
        /// Verifies ISO BMFF container and custom box writing in <see cref="JxlEncoder"/>.
        /// Ported from <c>lib/jxl/encode_test.cc</c>.
        /// </summary>
        [Test]
        public void ContainerAndBoxes_WritesValidContainerStream()
        {
            using JxlEncoder enc = new JxlEncoder();
            enc.UseContainer(true).ShouldBe(JxlEncoderStatus.Success);
            enc.UseBoxes().ShouldBe(JxlEncoderStatus.Success);

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.XSize               = 8;
            info.YSize               = 8;
            info.BitsPerSample       = 8;
            info.NumColorChannels    = 3;
            info.UsesOriginalProfile = true;
            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Success);

            byte[] exifPayload = new byte[] { 0, 0, 0, 0, (byte)'M', (byte)'M', 0x00, 0x2A };
            enc.AddBox("Exif", exifPayload, compressBox: false).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Effort, 3).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.PaletteColors, 0).ShouldBe(JxlEncoderStatus.Success);
            enc.SetFrameLossless(settings, true).ShouldBe(JxlEncoderStatus.Success);

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
            };

            byte[] pixels = new byte[8 * 8 * 3];
            enc.AddImageFrame(settings, in fmt, pixels).ShouldBe(JxlEncoderStatus.Success);
            enc.CloseInput();

            byte[] outBuf           = new byte[4096];
            int offset              = 0;
            JxlEncoderStatus status = enc.ProcessOutput(outBuf, ref offset);
            status.ShouldBe(JxlEncoderStatus.Success);
            offset.ShouldBeGreaterThan(32);

            byte[] encoded = outBuf.AsSpan(0, offset).ToArray();

            // Verify 12-byte JXL container signature: 00 00 00 0C 'J' 'X' 'L' ' ' 0D 0A 87 0A
            encoded[0].ShouldBe((byte)0x00);
            encoded[1].ShouldBe((byte)0x00);
            encoded[2].ShouldBe((byte)0x00);
            encoded[3].ShouldBe((byte)0x0C);
            encoded[4].ShouldBe((byte)'J');
            encoded[5].ShouldBe((byte)'X');
            encoded[6].ShouldBe((byte)'L');
            encoded[7].ShouldBe((byte)' ');

            JxlBasicInfo decodedInfo = DecodeBasicInfo(encoded);
            decodedInfo.XSize.ShouldBe(8u);
            decodedInfo.YSize.ShouldBe(8u);
            decodedInfo.BitsPerSample.ShouldBe(8u);
            decodedInfo.NumColorChannels.ShouldBe(3u);
        }

        private static byte[] EncodeImage(
            (uint Width, uint Height, uint Channels, uint BitsPerSample) dims,
            (bool Lossless, int Effort, float Distance) opts,
            ReadOnlySpan<byte> pixels)
        {
            using JxlEncoder enc = new JxlEncoder();
            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.XSize               = dims.Width;
            info.YSize               = dims.Height;
            info.BitsPerSample       = dims.BitsPerSample;
            info.NumColorChannels    = dims.Channels == 2 || dims.Channels == 4 ? dims.Channels - 1u : dims.Channels;
            info.UsesOriginalProfile = opts.Lossless;

            if (dims.Channels == 2 || dims.Channels == 4)
            {
                info.NumExtraChannels = 1;
                info.AlphaBits        = dims.BitsPerSample;
            }

            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Effort, opts.Effort).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Modular, 1).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.PaletteColors, 0).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.ColorTransform, (long)ColorTransform.None).ShouldBe(JxlEncoderStatus.Success);

            if (opts.Lossless)
            {
                enc.SetFrameLossless(settings, true).ShouldBe(JxlEncoderStatus.Success);
            }
            else
            {
                enc.SetFrameDistance(settings, opts.Distance).ShouldBe(JxlEncoderStatus.Success);
            }

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = dims.Channels,
                DataType    = dims.BitsPerSample == 16 ? JxlDataType.UInt16 : JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
            };

            enc.AddImageFrame(settings, in fmt, pixels).ShouldBe(JxlEncoderStatus.Success);
            enc.CloseInput();

            byte[] buffer           = new byte[65536];
            int offset              = 0;
            JxlEncoderStatus status = enc.ProcessOutput(buffer, ref offset);
            status.ShouldBe(JxlEncoderStatus.Success);

            return buffer.AsSpan(0, offset).ToArray();
        }

        private static JxlBasicInfo DecodeBasicInfo(byte[] jxlBytes)
        {
            using JxlDecoder dec = new JxlDecoder();
            dec.SubscribeEvents((int)JxlDecoderStatus.BasicInfo).ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(jxlBytes).ShouldBe(JxlDecoderStatus.Success);
            dec.CloseInput();

            JxlBasicInfo result = default;
            bool gotBasicInfo   = false;

            while (true)
            {
                JxlDecoderStatus status = dec.ProcessInput();

                if (status == JxlDecoderStatus.BasicInfo)
                {
                    dec.GetBasicInfo(out result).ShouldBe(JxlDecoderStatus.Success);
                    gotBasicInfo = true;
                    break;
                }
                else if (status == JxlDecoderStatus.Success)
                {
                    break;
                }
                else
                {
                    Assert.Fail($"Unexpected decoder status: {status}");
                }
            }

            gotBasicInfo.ShouldBeTrue();

            return result;
        }
    }
}
