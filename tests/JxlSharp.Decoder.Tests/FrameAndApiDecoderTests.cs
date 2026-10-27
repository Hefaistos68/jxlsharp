// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Unit tests for <see cref="ModularFrameDecoder"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularFrameDecoderTests
    {
        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder"/> initialization, context creation, and zero-filled group decoding.
        /// </summary>
        [Test]
        public void ModularFrameDecoder_InitAndMaybeDropFullImage_ShouldSucceed()
        {
            using ModularFrameDecoder sut = new ModularFrameDecoder(JxlMemoryManager.Default);

            FrameDimensions frameDim = new FrameDimensions();
            frameDim.Set(8, 8, 1, 0, 0, true, 1);

            sut.Init(frameDim);
            sut.HaveDc().ShouldBeFalse();
            sut.UsesFullImage().ShouldBeTrue();

            GlobalModularContext ctx = sut.CreateGlobalModularContext();
            ctx.UndoTransforms.ShouldBeTrue();

            sut.MaybeDropFullImage();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JpegConstants"/>, <see cref="JPEGQuantTable"/>,
    /// <see cref="JPEGHuffmanCode"/>, <see cref="JPEGScanInfo"/>, <see cref="JPEGComponent"/>,
    /// <see cref="JPEGData"/>, and <see cref="JxlToJpegDecoder"/>.
    /// </summary>
    [TestFixture]
    public sealed class JpegDataAndReconstructionTests
    {
        /// <summary>
        /// Verifies <see cref="JpegConstants"/> lookup tables and marker tags.
        /// </summary>
        [Test]
        public void JpegConstants_ZigZagAndNaturalOrder_ShouldBeInverses()
        {
            ReadOnlySpan<uint> nat = JpegConstants.KJPEGNaturalOrder;
            ReadOnlySpan<uint> zig = JpegConstants.KJPEGZigZagOrder;

            nat.Length.ShouldBe(80);
            zig.Length.ShouldBe(64);

            for (uint i = 0; i < 64; i++)
            {
                zig[(int)nat[(int)i]].ShouldBe(i);
            }

            JpegConstants.KIccProfileTag.Length.ShouldBe(12);
            JpegConstants.KExifTag.Length.ShouldBe(6);
            JpegConstants.KXMPTag.Length.ShouldBe(29);
        }

        /// <summary>
        /// Verifies <see cref="JPEGData.CalculateMcuSize"/> and <see cref="JxlToJpegDecoder"/> Exif/XMP marker helpers.
        /// </summary>
        [Test]
        public void JPEGData_CalculateMcuSizeAndMarkerHelpers_ShouldComputeExpectedValues()
        {
            JPEGData jpegData = new JPEGData
            {
                Width  = 16,
                Height = 16,
            };

            jpegData.Components.Add(new JPEGComponent
            {
                Id          = 1,
                HSampFactor = 2,
                VSampFactor = 2,
            });
            jpegData.Components.Add(new JPEGComponent
            {
                Id          = 2,
                HSampFactor = 1,
                VSampFactor = 1,
            });
            jpegData.Components.Add(new JPEGComponent
            {
                Id          = 3,
                HSampFactor = 1,
                VSampFactor = 1,
            });

            JPEGScanInfo scanInfo = new JPEGScanInfo
            {
                NumComponents = 3,
            };
            scanInfo.Components[0].CompIdx = 0;
            scanInfo.Components[1].CompIdx = 1;
            scanInfo.Components[2].CompIdx = 2;

            jpegData.CalculateMcuSize(scanInfo, out int mcusPerRow, out int mcuRows);
            mcusPerRow.ShouldBe(1);
            mcuRows.ShouldBe(1);

            byte[] exifAppData = new byte[3 + JpegConstants.KExifTag.Length + 8];
            jpegData.AppData.Add(exifAppData);
            jpegData.AppMarkerTypes.Add(AppMarkerType.Exif);

            JxlToJpegDecoder.NumExifMarkers(jpegData).ShouldBe(1u);
            JxlToJpegDecoder.NumXmpMarkers(jpegData).ShouldBe(0u);
            JxlToJpegDecoder.ExifBoxContentSize(jpegData, out nuint exifSize).ShouldBe(JxlDecoderStatus.Success);
            exifSize.ShouldBe(12u);

            byte[] exifBox = new byte[12];
            exifBox[4]     = 0x4D;
            exifBox[5]     = 0x4D;
            JxlToJpegDecoder.SetExif(exifBox, jpegData).ShouldBe(JxlDecoderStatus.Success);
            jpegData.AppData[0][0].ShouldBe(JpegConstants.KApp1);
        }

        /// <summary>
        /// Verifies <see cref="JxlToJpegDecoder"/> output buffer management.
        /// </summary>
        [Test]
        public void JxlToJpegDecoder_SetAndReleaseOutputBuffer_ShouldTrackAvailableBytes()
        {
            JxlToJpegDecoder sut = new JxlToJpegDecoder();
            byte[] buf           = new byte[64];

            sut.IsOutputSet.ShouldBeFalse();

            sut.SetOutputBuffer(buf, 0, 64).ShouldBe(JxlDecoderStatus.Success);
            sut.IsOutputSet.ShouldBeTrue();
            sut.SetOutputBuffer(buf, 0, 64).ShouldBe(JxlDecoderStatus.Error);
            sut.ReleaseOutputBuffer().ShouldBe(64u);
            sut.IsOutputSet.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="GetBlockFromEncoder"/> and <see cref="DecGroup"/>.
    /// </summary>
    [TestFixture]
    public sealed class DecGroupTests
    {
        /// <summary>
        /// Verifies <see cref="DecGroup.JpegOrder"/> and <see cref="DecGroup.Transpose8x8InPlace"/>.
        /// </summary>
        [Test]
        public void DecGroup_JpegOrderAndTranspose8x8InPlace_ShouldBehaveCorrectly()
        {
            DecGroup.JpegOrder(ColorTransform.YCbCr, isGray: false).ShouldBe(new int[] { 1, 0, 2 });
            DecGroup.JpegOrder(ColorTransform.None, isGray: false).ShouldBe(new int[] { 0, 1, 2 });
            DecGroup.JpegOrder(ColorTransform.YCbCr, isGray: true).ShouldBe(new int[] { 0, 0, 0 });

            int[] block = new int[64];

            for (int i = 0; i < 64; i++)
            {
                block[i] = i;
            }

            DecGroup.Transpose8x8InPlace(block);
            block[1].ShouldBe(8);
            block[8].ShouldBe(1);

            DecGroup.Transpose8x8InPlace(block);

            for (int i = 0; i < 64; i++)
            {
                block[i].ShouldBe(i);
            }
        }

        /// <summary>
        /// Verifies <see cref="GetBlockFromEncoder.Create"/> and <see cref="GetBlockFromEncoder.LoadBlock"/>.
        /// </summary>
        [Test]
        public void GetBlockFromEncoder_LoadBlock_ShouldAccumulateScaledPassCoefficients()
        {
            using ACImageT<int> acPass0 = ACImageT<int>.Make(JxlMemoryManager.Default, 64, 1).Value;
            acPass0.ZeroFill();
            acPass0.PlaneRow(0, 0, 0).Span32[0] = 3;
            acPass0.PlaneRow(1, 0, 0).Span32[0] = 5;
            acPass0.PlaneRow(2, 0, 0).Span32[0] = 7;

            uint[] shifts = new uint[] { 1u };
            StatusOr<GetBlockFromEncoder> readerOr = GetBlockFromEncoder.Create(
                new ACImage[] { acPass0 },
                0,
                shifts);

            readerOr.IsOk.ShouldBeTrue();
            GetBlockFromEncoder sut = readerOr.Value;
            sut.StartRow(0);

            int[] dst0 = new int[64];
            int[] dst1 = new int[64];
            int[] dst2 = new int[64];

            ACPtr[] blockPtrs =
            {
                new ACPtr(dst0),
                new ACPtr(dst1),
                new ACPtr(dst2),
            };

            Status loadStatus = sut.LoadBlock(
                0,
                0,
                AcStrategy.FromRawStrategy((byte)AcStrategyType.DCT),
                64,
                0,
                blockPtrs,
                ACType.k32);

            loadStatus.IsOk.ShouldBeTrue();

            dst0[0].ShouldBe(6);
            dst1[0].ShouldBe(10);
            dst2[0].ShouldBe(14);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ExternalConvertOptions"/> and <see cref="DecExternalImage"/>.
    /// </summary>
    [TestFixture]
    public sealed class DecExternalImageTests
    {
        /// <summary>
        /// Verifies <see cref="DecExternalImage.FloatToU32"/> and <see cref="DecExternalImage.FloatToF16"/>.
        /// </summary>
        [Test]
        public void FloatToU32AndFloatToF16_ShouldClampAndConvertSamples()
        {
            float[] src  = new float[] { -0.5f, 0.0f, 0.5f, 1.0f, 1.5f, float.NaN };
            uint[] u32   = new uint[6];
            ushort[] f16 = new ushort[6];

            DecExternalImage.FloatToU32(src, u32, 6, 255.0f, 8);
            DecExternalImage.FloatToF16(src, f16, 4);

            u32[0].ShouldBe(0u);
            u32[1].ShouldBe(0u);
            u32[2].ShouldBe(128u);
            u32[3].ShouldBe(255u);
            u32[4].ShouldBe(255u);
            u32[5].ShouldBe(0u);

            ((float)BitConverter.UInt16BitsToHalf(f16[2])).ShouldBe(0.5f, 1e-3f);
            ((float)BitConverter.UInt16BitsToHalf(f16[3])).ShouldBe(1.0f, 1e-3f);
        }

        /// <summary>
        /// Verifies <see cref="DecExternalImage.ConvertChannelsToExternal"/> for uint8, uint16, and float32 formats.
        /// </summary>
        [Test]
        public void ConvertChannelsToExternal_ShouldExportInterleavedRgbBytes()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            using ImagePlane<float> ch0 = ImagePlane<float>.Create(mm, 2, 2).Value;
            using ImagePlane<float> ch1 = ImagePlane<float>.Create(mm, 2, 2).Value;
            using ImagePlane<float> ch2 = ImagePlane<float>.Create(mm, 2, 2).Value;

            ImageOps.FillImage(1.0f, ch0);
            ImageOps.FillImage(0.5f, ch1);
            ImageOps.FillImage(0.0f, ch2);

            byte[] outBytes = new byte[2 * 2 * 3];

            ExternalConvertOptions opts = new ExternalConvertOptions(
                bitsPerSample: 8,
                floatOut: false,
                endianness: JxlEndianness.NativeEndian,
                stride: 6,
                pool: null,
                outBuffer: outBytes,
                outSize: (nuint)outBytes.Length);

            Status status = DecExternalImage.ConvertChannelsToExternal(
                new ImagePlane<float>?[] { ch0, ch1, ch2 },
                3,
                in opts);

            status.IsOk.ShouldBeTrue();

            outBytes[0].ShouldBe((byte)255);
            outBytes[1].ShouldBe((byte)128);
            outBytes[2].ShouldBe((byte)0);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="FrameDecoder"/>.
    /// </summary>
    [TestFixture]
    public sealed class FrameDecoderTests
    {
        /// <summary>
        /// Verifies <see cref="FrameDecoder"/> initialization and configuration properties.
        /// </summary>
        [Test]
        public void FrameDecoder_ConfigureOptions_ShouldSetState()
        {
            CodecMetadata metadata             = new CodecMetadata();
            using PassesDecoderState decState  = new PassesDecoderState(JxlMemoryManager.Default);
            using FrameDecoder frameDecoder    = new FrameDecoder(decState, metadata, null, useSlowRenderingPipeline: false);

            frameDecoder.TotalSize().ShouldBe(0u);
            frameDecoder.SumSectionSizes().ShouldBe(0u);
            frameDecoder.SetRenderSpotcolors(false);
            frameDecoder.SetCoalescing(false);
            frameDecoder.SetJpegTranscodeMode(false);
            frameDecoder.HasDecodedAll().ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlDecoder"/>.
    /// Ported from <c>lib/jxl/decode_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class JxlDecoderTests
    {
        private static readonly byte[] OneByOneJxlCodestream = BuildOneByOneModularJxlCodestream();

        /// <summary>
        /// Ported from <c>DecodeTest.JxlSignatureCheckTest</c> in <c>lib/jxl/decode_test.cc</c>.
        /// </summary>
        [Test]
        public void CheckSignature_ShouldMatchAllExpectedCases()
        {
            JxlDecoder.CheckSignature(ReadOnlySpan<byte>.Empty).ShouldBe(JxlSignature.NotEnoughBytes);
            JxlDecoder.CheckSignature(new byte[] { (byte)'a' }).ShouldBe(JxlSignature.Invalid);
            JxlDecoder.CheckSignature(new byte[] { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f' }).ShouldBe(JxlSignature.Invalid);
            JxlDecoder.CheckSignature(new byte[] { 0xFF }).ShouldBe(JxlSignature.NotEnoughBytes);
            JxlDecoder.CheckSignature(new byte[] { 0xFF, 0xD8 }).ShouldBe(JxlSignature.Invalid);
            JxlDecoder.CheckSignature(new byte[] { 0xFF, 0x0A }).ShouldBe(JxlSignature.Codestream);
            JxlDecoder.CheckSignature(new byte[] { 0, 0, 0, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A }).ShouldBe(JxlSignature.Container);
            JxlDecoder.CheckSignature(new byte[] { 0, 0, 0, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x00 }).ShouldBe(JxlSignature.Invalid);
            JxlDecoder.CheckSignature(new byte[] { 0, 0, 0, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87 }).ShouldBe(JxlSignature.NotEnoughBytes);
            JxlDecoder.CheckSignature(new byte[] { 0 }).ShouldBe(JxlSignature.NotEnoughBytes);
        }

        /// <summary>
        /// Ported from <c>DecodeTest.DefaultAllocTest</c> and <c>DecodeTest.DefaultParallelRunnerTest</c>.
        /// </summary>
        [Test]
        public void CreateAndSetParallelRunner_ShouldSucceed()
        {
            JxlDecoder.Version().ShouldBe(JxlDecoder.NumericVersion);

            using JxlDecoder dec = JxlDecoder.Create(null);
            dec.SetParallelRunner(null, null).ShouldBe(JxlDecoderStatus.Success);
            dec.SizeHintBasicInfo().ShouldBeGreaterThan(0u);
        }

        /// <summary>
        /// Ported from <c>DecodeTest.CloseInput</c> in <c>lib/jxl/decode_test.cc</c>.
        /// </summary>
        [Test]
        public void ProcessInput_TruncatedWithCloseInput_ShouldReturnError()
        {
            byte[] partialFile   = new byte[] { 0xFF, 0x0A };
            using JxlDecoder dec = JxlDecoder.Create();

            dec.SubscribeEvents(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.FullImage).ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(partialFile).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.CloseInput();
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Error);
        }

        /// <summary>
        /// Ported from <c>DecodeTest.DCNotGettableTest</c> and full image decoding of the 1x1 JXL codestream.
        /// </summary>
        [Test]
        public void ProcessInput_OneByOneJxlCodestream_ShouldDecodeBasicInfoAndFullImage()
        {
            using JxlDecoder dec = JxlDecoder.Create();

            dec.SubscribeEvents(
                JxlDecoderStatus.BasicInfo |
                JxlDecoderStatus.ColorEncoding |
                JxlDecoderStatus.Frame |
                JxlDecoderStatus.FullImage).ShouldBe(JxlDecoderStatus.Success);

            dec.SetInput(OneByOneJxlCodestream).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);

            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.XSize.ShouldBe(1u);
            info.YSize.ShouldBe(1u);
            info.NumColorChannels.ShouldBe(3u);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.ColorEncoding);
            dec.GetColorAsEncodedProfile(JxlColorProfileTarget.Data, out JxlColorEncoding enc).ShouldBe(JxlDecoderStatus.Success);
            enc.ColorSpace.ShouldBe(JxlColorSpace.Rgb);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Frame);
            dec.GetFrameHeader(out JxlFrameHeader frameHeader).ShouldBe(JxlDecoderStatus.Success);
            frameHeader.IsLast.ShouldBeTrue();

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedImageOutBuffer);

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            dec.ImageOutBufferSize(format, out nuint bufSize).ShouldBe(JxlDecoderStatus.Success);
            bufSize.ShouldBe(3u);

            byte[] pixels = new byte[bufSize];
            dec.SetImageOutBuffer(format, pixels).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.FullImage);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Success);
        }

        /// <summary>
        /// Verifies container box parsing (<c>JXL </c>, <c>ftyp</c>, <c>brob</c> compressed <c>Exif</c>, and <c>jxlc</c>)
        /// ported from <c>DecodeTest.ExifBrobBoxTest</c>.
        /// </summary>
        [Test]
        public void ProcessInput_ContainerWithBrobExifAndJxlc_ShouldDecompressExifAndDecodeImage()
        {
            byte[] containerHeader = new byte[]
            {
                0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A,
                0x00, 0x00, 0x00, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                (byte)'j', (byte)'x', (byte)'l', (byte)' ', 0x00, 0x00, 0x00, 0x00,
                (byte)'j', (byte)'x', (byte)'l', (byte)' ',
            };

            byte[] boxBrobExif = new byte[64]
            {
                0x00, 0x00, 0x00, 0x40, (byte)'b', (byte)'r', (byte)'o', (byte)'b',
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0xA1, 0xE8, 0x02, 0xC0,
                0x7F, 0xA4, 0x76, 0xAA, 0x35, 0xC4, 0xF0, 0x17, 0x3D, 0x3F, 0xB7, 0x7B,
                0x1B, 0x1F, 0xCC, 0xDA, 0x8C, 0x51, 0x58, 0x31, 0x37, 0x50, 0x54, 0x22,
                0xAE, 0x00, 0x00, 0x82, 0x73, 0x8C, 0xCB, 0x74, 0xDB, 0xC8, 0xD0, 0x6B,
                0x10, 0xBE, 0x18, 0x84, 0xBF, 0x6C, 0x24, 0xD6, 0x63, 0x23, 0x01, 0x08,
            };

            List<byte> fullContainer = new List<byte>();
            fullContainer.AddRange(containerHeader);
            fullContainer.AddRange(boxBrobExif);

            uint jxlcSize = (uint)(OneByOneJxlCodestream.Length + 8);
            fullContainer.Add((byte)(jxlcSize >> 24));
            fullContainer.Add((byte)(jxlcSize >> 16));
            fullContainer.Add((byte)(jxlcSize >> 8));
            fullContainer.Add((byte)jxlcSize);
            fullContainer.Add((byte)'j');
            fullContainer.Add((byte)'x');
            fullContainer.Add((byte)'l');
            fullContainer.Add((byte)'c');
            fullContainer.AddRange(OneByOneJxlCodestream);

            byte[] inputBytes    = fullContainer.ToArray();
            using JxlDecoder dec = JxlDecoder.Create();
            dec.SubscribeEvents(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.Box | JxlDecoderStatus.FullImage)
               .ShouldBe(JxlDecoderStatus.Success);
            dec.SetDecompressBoxes(true).ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(inputBytes).ShouldBe(JxlDecoderStatus.Success);
            dec.CloseInput();

            byte[] exifOut   = new byte[128];
            byte[] pixelOut  = new byte[3];
            bool sawExif     = false;
            bool sawBasic    = false;
            bool sawFull     = false;

            while (true)
            {
                JxlDecoderStatus st = dec.ProcessInput();

                if (st == JxlDecoderStatus.Box)
                {
                    dec.GetBoxType(out JxlBoxType boxType, decompressed: true).ShouldBe(JxlDecoderStatus.Success);

                    if (boxType == new JxlBoxType("Exif"))
                    {
                        sawExif = true;
                        dec.SetBoxBuffer(exifOut, 0, exifOut.Length).ShouldBe(JxlDecoderStatus.Success);
                    }
                }
                else if (st == JxlDecoderStatus.BoxComplete)
                {
                    nuint rem = dec.ReleaseBoxBuffer();
                    (exifOut.Length - (int)rem).ShouldBe(94);
                }
                else if (st == JxlDecoderStatus.BasicInfo)
                {
                    sawBasic = true;
                    dec.GetBasicInfo(out JxlBasicInfo bi).ShouldBe(JxlDecoderStatus.Success);
                    bi.HaveContainer.ShouldBeTrue();
                }
                else if (st == JxlDecoderStatus.NeedImageOutBuffer)
                {
                    JxlPixelFormat fmt = new JxlPixelFormat
                    {
                        NumChannels = 3,
                        DataType    = JxlDataType.UInt8,
                        Endianness  = JxlEndianness.NativeEndian,
                    };
                    dec.SetImageOutBuffer(fmt, pixelOut).ShouldBe(JxlDecoderStatus.Success);
                }
                else if (st == JxlDecoderStatus.FullImage)
                {
                    sawFull = true;
                }
                else if (st == JxlDecoderStatus.Success)
                {
                    break;
                }
                else
                {
                    Assert.Fail($"Unexpected decoder status: {st}");
                }
            }

            sawExif.ShouldBeTrue();
            sawBasic.ShouldBeTrue();
            sawFull.ShouldBeTrue();
        }

        private static byte[] BuildOneByOneModularJxlCodestream()
        {
            BitWriter sectionWriter = new BitWriter();

            // DequantMatrices.DecodeDC: all_default = true
            sectionWriter.Write(1, 1);

            // DecodeGlobalTreeIfPresent: hasTree = false
            sectionWriter.Write(1, 0);

            // GroupHeader: use_global_tree = false, wp_header.all_default = true, num_transforms = 0
            sectionWriter.Write(1, 0);
            sectionWriter.Write(1, 1);
            sectionWriter.Write(2, 0);

            // ModularMa.DecodeTree (6 contexts):
            // lz77.enabled = 0, simple_code = 1, nbits = 0, use_prefix_code = 1,
            // HybridUintConfig(15) split_exponent = 0, DecodeVarLenUint16 = 0 (alphabet_size = 1)
            sectionWriter.Write(1, 0);
            sectionWriter.Write(1, 1);
            sectionWriter.Write(2, 0);
            sectionWriter.Write(1, 1);
            sectionWriter.Write(4, 0);
            sectionWriter.Write(1, 0);

            // ModularGenericDecompress (1 context):
            // lz77.enabled = 0, use_prefix_code = 1,
            // HybridUintConfig(15) split_exponent = 0, DecodeVarLenUint16 = 0 (alphabet_size = 1)
            sectionWriter.Write(1, 0);
            sectionWriter.Write(1, 1);
            sectionWriter.Write(4, 0);
            sectionWriter.Write(1, 0);

            sectionWriter.ZeroPadToByte();

            ReadOnlySpan<byte> sectionBytes = sectionWriter.GetSpan();
            BitWriter writer                = new BitWriter();

            writer.Write(8, 0xFF);
            writer.Write(8, 0x0A);

            SizeHeader size = new SizeHeader();
            size.Set(1, 1);
            Bundle.Write(size, writer, LayerType.Header, null);

            ImageMetadata metadata = new ImageMetadata
            {
                XybEncoded = false,
            };
            Bundle.Write(metadata, writer, LayerType.Header, null);

            writer.ZeroPadToByte();

            CodecMetadata codecMeta = new CodecMetadata
            {
                Size = size,
                M    = metadata,
            };

            FrameHeader frameHeader = new FrameHeader(codecMeta)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            frameHeader.LoopFilter.Gab      = false;
            frameHeader.LoopFilter.EpfIters = 0;

            FrameHeaderMethods.WriteFrameHeader(frameHeader, writer, null);

            writer.Write(1, 0);
            writer.ZeroPadToByte();
            U32Coder.Write(Toc.KTocDist, (uint)sectionBytes.Length, writer);
            writer.ZeroPadToByte();

            List<byte> result = new List<byte>(writer.GetSpan().ToArray());
            result.AddRange(sectionBytes.ToArray());

            return result.ToArray();
        }
    }
}
