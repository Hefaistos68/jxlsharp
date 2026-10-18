// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Integration.Tests
{
    /// <summary>
    /// Preview frame encoding/decoding integration tests ported from <c>lib/jxl/preview_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class PreviewTests
    {
        /// <summary>
        /// Verifies roundtrip encoding and decoding of a codestream with an explicit preview frame.
        /// Ported from <c>PreviewTest.RoundtripGivenPreview</c> in <c>lib/jxl/preview_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripGivenPreview()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            byte[] orig         = TestUtils.ReadTestData("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");
            CodecInOut io       = new CodecInOut(mm);

            ExtrasCodec.SetFromBytes(orig, new ColorHints(), io).IsOk.ShouldBeTrue();
            io.ShrinkTo(io.XSize / 8, io.YSize / 8).IsOk.ShouldBeTrue();

            StatusOr<ImageBundle> previewCopyOr = io.Main.Copy();

            previewCopyOr.IsOk.ShouldBeTrue();
            io.PreviewFrame = previewCopyOr.Value;

            const nuint previewXSize = 15;
            const nuint previewYSize = 27;

            io.PreviewFrame.ShrinkTo(previewXSize, previewYSize).IsOk.ShouldBeTrue();
            io.Metadata.M.HavePreview = true;
            io.Metadata.M.PreviewSize.Set(io.PreviewFrame.XSize(), io.PreviewFrame.YSize()).IsOk.ShouldBeTrue();

            CompressParams cparams = new CompressParams
            {
                ButteraugliDistance = 2.0f,
                SpeedTier           = JxlSharp.Encoder.SpeedTier.kSquirrel,
            };

            cparams.SetCms(JxlCms.GetDefaultCms());

            JXLDecompressParams dparams = new JXLDecompressParams();
            CodecInOut io2              = new CodecInOut(mm);

            TestUtils.Roundtrip(io, cparams, dparams, io2, out _).ShouldBeTrue();
            io2.Metadata.M.PreviewSize.XSize().ShouldBe(previewXSize);
            io2.Metadata.M.PreviewSize.YSize().ShouldBe(previewYSize);
            io2.PreviewFrame.XSize().ShouldBe(previewXSize);
            io2.PreviewFrame.YSize().ShouldBe(previewYSize);

            ButteraugliParams baParams = new ButteraugliParams();
            float previewDist          = TestUtils.ButteraugliDistance(
                io.PreviewFrame,
                io2.PreviewFrame,
                in baParams,
                JxlCms.GetDefaultCms());
            float mainDist             = TestUtils.ButteraugliDistance(
                io.Main,
                io2.Main,
                in baParams,
                JxlCms.GetDefaultCms());

            previewDist.ShouldBeLessThanOrEqualTo(2.5f);
            mainDist.ShouldBeLessThanOrEqualTo(2.5f);
        }
    }

    /// <summary>
    /// Progressive passes and downsampling integration tests ported from <c>lib/jxl/passes_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class PassesTests
    {
        /// <summary>
        /// Verifies progressive AC roundtrip on a small downscaled image.
        /// Ported from <c>PassesTest.RoundtripSmallPasses</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripSmallPasses()
        {
            PackedPixelFile ppf = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");

            ShrinkImage(ppf, ppf.XSize / 8, ppf.YSize / 8);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 1.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveAc, 1);

            PackedPixelFile ppf2 = new PackedPixelFile();
            nuint compressedSize = TestUtils.Roundtrip(ppf, cparams, new JXLDecompressParams(), null, ppf2);

            compressedSize.ShouldBeLessThanOrEqualTo((nuint)5600);
            TestUtils.ButteraugliDistance(ppf, ppf2).ShouldBeLessThanOrEqualTo(1.2f);
        }

        /// <summary>
        /// Verifies progressive AC roundtrip on an unaligned image dimension.
        /// Ported from <c>PassesTest.RoundtripUnalignedPasses</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripUnalignedPasses()
        {
            PackedPixelFile ppf = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");

            ShrinkImage(ppf, ppf.XSize / 12, ppf.YSize / 7);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 2.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveAc, 1);

            PackedPixelFile ppf2 = new PackedPixelFile();
            nuint compressedSize = TestUtils.Roundtrip(ppf, cparams, new JXLDecompressParams(), null, ppf2);

            compressedSize.ShouldBeLessThanOrEqualTo((nuint)5200);
            TestUtils.ButteraugliDistance(ppf, ppf2).ShouldBeLessThanOrEqualTo(2.0f);
        }

        /// <summary>
        /// Verifies progressive AC roundtrip across multiple groups.
        /// Ported from <c>PassesTest.RoundtripMultiGroupPasses</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripMultiGroupPasses()
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);

            PackedPixelFile ppf = LoadTestImage("jxl/flower/flower.png");
            ShrinkImage(ppf, 600, 1024);

            JXLCompressParams cparams = new JXLCompressParams
            {
                Distance = 1.0f,
            };
            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveAc, 1);

            JXLDecompressParams dparams = new JXLDecompressParams();
            PackedPixelFile ppfOut      = new PackedPixelFile();

            TestUtils.Roundtrip(ppf, cparams, dparams, pool.Pool, ppfOut);
            TestUtils.ButteraugliDistance(ppf, ppfOut, pool.Pool).ShouldBeLessThanOrEqualTo(1.5f);
        }

        /// <summary>
        /// Verifies that two consecutive progressive roundtrips at distance 2.0 produce identical bitstreams and distances.
        /// Ported from <c>PassesTest.RoundtripProgressiveConsistent</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripProgressiveConsistent()
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            PackedPixelFile ppf           = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");
            JXLCompressParams cparams     = new JXLCompressParams { Distance = 2.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 7);
            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveAc, 1);

            for (nuint xsize = 48; xsize > 44; --xsize)
            {
                ShrinkImage(ppf, xsize, 15);

                PackedPixelFile ppf2 = new PackedPixelFile();
                nuint size2          = TestUtils.Roundtrip(ppf, cparams, new JXLDecompressParams(), pool.Get(), ppf2);
                PackedPixelFile ppf3 = new PackedPixelFile();
                nuint size3          = TestUtils.Roundtrip(ppf, cparams, new JXLDecompressParams(), pool.Get(), ppf3);

                size2.ShouldBe(size3);

                float dist2 = TestUtils.ButteraugliDistance(ppf, ppf2, pool.Get());
                float dist3 = TestUtils.ButteraugliDistance(ppf, ppf3, pool.Get());

                dist2.ShouldBe(dist3);
            }
        }

        /// <summary>
        /// Verifies that all progressive downsampling factors (1, 2, 4, 8) decode cleanly to the full image dimensions.
        /// Ported from <c>PassesTest.AllDownsampleFeasible</c> and <c>AllDownsampleFeasibleQProgressive</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AllDownsampleFeasible(bool qProgressive)
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            PackedPixelFile ppf           = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");

            ShrinkImage(ppf, 128, 128);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 1.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 7);
            cparams.AddOption(
                qProgressive ? JxlEncoderFrameSettingId.QProgressiveAc : JxlEncoderFrameSettingId.ProgressiveAc,
                1);

            CodecJxl.EncodeImageJXL(cparams, ppf, null, out byte[] compressed).ShouldBeTrue();

            nuint[] downsamplings = { 1, 2, 4, 8 };

            foreach (nuint ds in downsamplings)
            {
                JXLDecompressParams dparams = new JXLDecompressParams { MaxDownsampling = ds };

                TestUtils.DefaultAcceptedFormats(dparams);

                PackedPixelFile output = new PackedPixelFile();

                CodecJxl.DecodeImageJXL(compressed, dparams, out _, output).ShouldBeTrue();
                output.XSize.ShouldBe(ppf.XSize);
                output.YSize.ShouldBe(ppf.YSize);
            }
        }

        /// <summary>
        /// Verifies non-progressive DC image decoding with large <c>MaxDownsampling</c>.
        /// Ported from <c>PassesTest.NonProgressiveDCImage</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void NonProgressiveDCImage()
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            PackedPixelFile ppf           = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");

            ShrinkImage(ppf, 128, 128);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 1.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 7);
            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, 0);

            JXLDecompressParams dparams = new JXLDecompressParams { MaxDownsampling = 100 };
            PackedPixelFile output      = new PackedPixelFile();
            nuint compressedSize        = TestUtils.Roundtrip(ppf, cparams, dparams, pool.Get(), output, out nuint decodedSize);

            compressedSize.ShouldBeGreaterThan((nuint)0);
            decodedSize.ShouldBeLessThanOrEqualTo(compressedSize);
            output.XSize.ShouldBe(ppf.XSize);
            output.YSize.ShouldBe(ppf.YSize);
        }

        /// <summary>
        /// Verifies progressive AC roundtrip with Gaborish disabled.
        /// Ported from <c>PassesTest.RoundtripSmallNoGaborishPasses</c> in <c>lib/jxl/passes_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripSmallNoGaborishPasses()
        {
            PackedPixelFile ppf = LoadTestImage("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");

            ShrinkImage(ppf, ppf.XSize / 8, ppf.YSize / 8);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 1.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Gaborish, 0);
            cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveAc, 1);

            PackedPixelFile ppf2 = new PackedPixelFile();
            nuint compressedSize = TestUtils.Roundtrip(ppf, cparams, new JXLDecompressParams(), null, ppf2);

            compressedSize.ShouldBeLessThanOrEqualTo((nuint)5700);
            TestUtils.ButteraugliDistance(ppf, ppf2).ShouldBeLessThanOrEqualTo(1.2f);
        }

        private static PackedPixelFile LoadTestImage(string path)
        {
            byte[] orig         = TestUtils.ReadTestData(path);
            PackedPixelFile ppf = new PackedPixelFile();

            ExtrasDecodeFacade.DecodeBytes(orig, new ColorHints(), ppf).IsOk.ShouldBeTrue();

            return ppf;
        }

        private static void ShrinkImage(PackedPixelFile ppf, nuint newXSize, nuint newYSize)
        {
            ppf.ShrinkTo(newXSize, newYSize).IsOk.ShouldBeTrue();

            List<PackedFrame> frames = new List<PackedFrame>(ppf.Frames.Count);

            foreach (PackedFrame frame in ppf.Frames)
            {
                StatusOr<PackedFrame> copyOr = frame.Copy();

                copyOr.IsOk.ShouldBeTrue();
                frames.Add(copyOr.Value);
            }

            ppf.Frames.Clear();
            ppf.Frames.AddRange(frames);
        }
    }

    /// <summary>
    /// End-to-end C-API roundtrip integration tests ported from <c>lib/jxl/roundtrip_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class RoundtripTests
    {
        private static readonly byte[] EncodedTestProfile =
        {
            0x1f, 0x8b, 0x01, 0x13, 0x10, 0x00, 0x00, 0x00, 0x20, 0x4c, 0xcc, 0x03,
            0xe7, 0xa0, 0xa5, 0xa2, 0x90, 0xa4, 0x27, 0xe8, 0x79, 0x1d, 0xe3, 0x26,
            0x57, 0x54, 0xef, 0x00, 0xe8, 0x97, 0x02, 0xce, 0xa1, 0xd7, 0x85, 0x16,
            0xb4, 0x29, 0x94, 0x58, 0xf2, 0x56, 0xc0, 0x76, 0xea, 0x23, 0xec, 0x7c,
            0x73, 0x51, 0x41, 0x40, 0x23, 0x21, 0x95, 0x04, 0x75, 0x12, 0xc9, 0xcc,
            0x16, 0xbd, 0xb6, 0x99, 0xad, 0xf8, 0x75, 0x35, 0xb6, 0x42, 0xae, 0xae,
            0xae, 0x86, 0x56, 0xf8, 0xcc, 0x16, 0x30, 0xb3, 0x45, 0xad, 0x0d, 0x40,
            0xd6, 0xd1, 0xd6, 0x99, 0x40, 0xbe, 0xe2, 0xdc, 0x31, 0x07, 0xa6, 0xb9,
            0x27, 0x92, 0x38, 0x00, 0x03, 0x5e, 0x2c, 0xbe, 0xe6, 0xfb, 0x19, 0xbf,
            0xf3, 0x6d, 0xbc, 0x4d, 0x64, 0xe5, 0xba, 0x76, 0xde, 0x31, 0x65, 0x66,
            0x14, 0xa6, 0x3a, 0xc5, 0x8f, 0xb1, 0xb4, 0xba, 0x1f, 0xb1, 0xb8, 0xd4,
            0x75, 0xba, 0x18, 0x86, 0x95, 0x3c, 0x26, 0xf6, 0x25, 0x62, 0x53, 0xfd,
            0x9c, 0x94, 0x76, 0xf6, 0x95, 0x2c, 0xb1, 0xfd, 0xdc, 0xc0, 0xe4, 0x3f,
            0xb3, 0xff, 0x67, 0xde, 0xd5, 0x94, 0xcc, 0xb0, 0x83, 0x2f, 0x28, 0x93,
            0x92, 0x03, 0xa1, 0x41, 0x64, 0x60, 0x62, 0x70, 0x80, 0x87, 0xaf, 0xe7,
            0x60, 0x4a, 0x20, 0x23, 0xb3, 0x11, 0x07, 0x38, 0x38, 0xd4, 0x0a, 0x66,
            0xb5, 0x93, 0x41, 0x90, 0x19, 0x17, 0x18, 0x60, 0xa5, 0x0b, 0x7a, 0x24,
            0xaa, 0x20, 0x81, 0xac, 0xa9, 0xa1, 0x70, 0xa6, 0x12, 0x8a, 0x4a, 0xa3,
            0xa0, 0xf9, 0x9a, 0x97, 0xe7, 0xa8, 0xac, 0x08, 0xa8, 0xc4, 0x2a, 0x86,
            0xa7, 0x69, 0x1e, 0x67, 0xe6, 0xbe, 0xa4, 0xd3, 0xff, 0x91, 0x61, 0xf6,
            0x8a, 0xe6, 0xb5, 0xb3, 0x61, 0x9f, 0x19, 0x17, 0x98, 0x27, 0x6b, 0xe9,
            0x08, 0x98, 0xe1, 0x21, 0x4a, 0x09, 0xb5, 0xd7, 0xca, 0xfa, 0x94, 0xd0,
            0x69, 0x1a, 0xeb, 0x52, 0x01, 0x4e, 0xf5, 0xf6, 0xdf, 0x7f, 0xe7, 0x29,
            0x70, 0xee, 0x04, 0xda, 0x2f, 0xa4, 0xff, 0xfe, 0xbb, 0x6f, 0xa8, 0xff,
            0xfe, 0xdb, 0xaf, 0x08, 0xf6, 0x72, 0xa1, 0x40, 0x5d, 0xf0, 0x2d, 0x08,
            0x82, 0x5b, 0x87, 0xbd, 0x10, 0x08, 0xe9, 0x07, 0xee, 0x4b, 0x80, 0xda,
            0x4a, 0x04, 0xc5, 0x5e, 0xa0, 0xb7, 0x1e, 0x60, 0xb0, 0x59, 0x76, 0x60,
            0x0b, 0x2e, 0x19, 0x8a, 0x2e, 0x1c, 0xe6, 0x06, 0x20, 0xb8, 0x64, 0x18,
            0x2a, 0xcf, 0x51, 0x94, 0xd4, 0xee, 0xc3, 0xfe, 0x39, 0x74, 0xd4, 0x2b,
            0x48, 0xc9, 0x83, 0x4c, 0x9b, 0xd0, 0x4c, 0x35, 0x10, 0xe3, 0x09, 0xf7,
            0x72, 0xf0, 0x7a, 0x0e, 0xbf, 0x7d, 0x36, 0x2e, 0x19, 0x7e, 0x3f, 0x0c,
            0xf7, 0x93, 0xe7, 0xf4, 0x1d, 0x32, 0xc6, 0xb0, 0x89, 0xad, 0xe0, 0x28,
            0xc1, 0xa7, 0x59, 0xe3, 0x00,
        };

        /// <summary>
        /// Verifies float, uint16, and uint8 frame roundtrip compression across channels, container, lossless, and extra channels.
        /// Ported from <c>RoundtripTest.FloatFrameRoundtripTest</c>, <c>Uint16FrameRoundtripTest</c>, and <c>Uint8FrameRoundtripTest</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [TestCase(JxlDataType.Float, false, false, 3u)]
        [TestCase(JxlDataType.Float, true, true, 3u)]
        [TestCase(JxlDataType.UInt16, false, false, 3u)]
        [TestCase(JxlDataType.UInt16, true, true, 4u)]
        [TestCase(JxlDataType.UInt8, false, false, 1u)]
        [TestCase(JxlDataType.UInt8, true, true, 3u)]
        [TestCase(JxlDataType.UInt8, true, true, 4u)]
        public void FrameRoundtripTest(JxlDataType dataType, bool lossless, bool useContainer, uint numChannels)
        {
            JxlPixelFormat pixelFormat = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = dataType,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            List<(JxlExtraChannelType Type, string Name)> extraChannels = new List<(JxlExtraChannelType, string)>();

            if (dataType != JxlDataType.Float)
            {
                extraChannels.Add((JxlExtraChannelType.Depth, "very deep"));
            }

            VerifyRoundtripCompression(
                (31, 33),
                in pixelFormat,
                in pixelFormat,
                (lossless, useContainer, 1u, false, -1),
                extraChannels);
        }

        /// <summary>
        /// Verifies non-linear sRGB input encoded as XYB and decoded to float.
        /// Ported from <c>RoundtripTest.TestNonlinearSrgbAsXybEncoded</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [TestCase(false, 1u)]
        [TestCase(true, 3u)]
        [TestCase(false, 4u)]
        public void TestNonlinearSrgbAsXybEncoded(bool useContainer, uint numChannels)
        {
            JxlPixelFormat formatIn = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            JxlPixelFormat formatOut = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = JxlDataType.Float,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            VerifyRoundtripCompression(
                (31, 33),
                in formatIn,
                in formatOut,
                (false, useContainer, 1u, false, -1),
                Array.Empty<(JxlExtraChannelType, string)>());
        }

        /// <summary>
        /// Verifies resampling and upsampling modes during roundtrip compression.
        /// Ported from <c>RoundtripTest.Resampling</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void Resampling()
        {
            JxlPixelFormat pixelFormat = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            VerifyRoundtripCompression(
                (32, 32),
                in pixelFormat,
                in pixelFormat,
                (false, false, 2u, false, -1),
                Array.Empty<(JxlExtraChannelType, string)>());

            foreach (uint factor in new uint[] { 2, 4 })
            {
                foreach (int upsamplingMode in new int[] { -1, 0, 1 })
                {
                    VerifyRoundtripCompression(
                        (32, 32),
                        in pixelFormat,
                        in pixelFormat,
                        (true, false, factor, true, upsamplingMode),
                        Array.Empty<(JxlExtraChannelType, string)>());
                }
            }
        }

        /// <summary>
        /// Verifies multi-frame animation roundtrip with and without a <c>jxli</c> frame index box.
        /// Ported from <c>RoundtripTest.MultiFrameTest</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void MultiFrameTest()
        {
            JxlPixelFormat pixelFormat = new JxlPixelFormat
            {
                NumChannels = 4,
                DataType    = JxlDataType.Float,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            const nuint xsize    = 31;
            const nuint ysize    = 33;
            const nuint nbFrames = 3;
            nuint baseSize       = 0;

            foreach (int indexFrames in new int[] { 0, 1 })
            {
                byte[] originalBytes = GetTestImageBytes(xsize, ysize * nbFrames, in pixelFormat);
                byte[] compressed    = EncodeMultiFrameAnimation(xsize, ysize, nbFrames, indexFrames, in pixelFormat, originalBytes);

                if (indexFrames == 0)
                {
                    baseSize = (nuint)compressed.Length;
                }
                else
                {
                    ((nuint)compressed.Length).ShouldBeGreaterThanOrEqualTo(baseSize + 8u);
                }

                DecodeAndVerifyMultiFrameAnimation(xsize, ysize, nbFrames, in pixelFormat, originalBytes, compressed);
            }
        }

        /// <summary>
        /// Verifies that an ICC profile is preserved byte-for-byte through <see cref="JxlEncoder.SetICCProfile"/> and <see cref="JxlDecoder.GetColorAsICCProfile"/>.
        /// Ported from <c>RoundtripTest.TestICCProfile</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void TestICCProfile()
        {
            using BitReader reader = new BitReader(EncodedTestProfile);

            TestUtils.ReadICC(reader, out byte[] icc).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            const nuint xsize    = 25;
            const nuint ysize    = 37;
            byte[] originalBytes = GetTestImageBytes(xsize, ysize, in format);

            using JxlEncoder enc = JxlEncoder.Create(null);
            JxlBasicInfo basicInfo = default;

            TestUtils.JxlBasicInfoSetFromPixelFormat(ref basicInfo, in format);
            basicInfo.XSize               = (uint)xsize;
            basicInfo.YSize               = (uint)ysize;
            basicInfo.UsesOriginalProfile = true;

            enc.SetBasicInfo(in basicInfo).ShouldBe(JxlEncoderStatus.Success);
            enc.SetICCProfile(icc).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);

            enc.AddImageFrame(settings, in format, originalBytes).ShouldBe(JxlEncoderStatus.Success);
            enc.CloseInput();

            byte[] compressed = EncodeWithEncoder(enc);

            using JxlDecoder dec = JxlDecoder.Create(null);

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.FullImage))
               .ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(compressed).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
            dec.ImageOutBufferSize(format, out nuint bufferSize).ShouldBe(JxlDecoderStatus.Success);
            bufferSize.ShouldBe((nuint)originalBytes.Length);

            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.XSize.ShouldBe((uint)xsize);
            info.YSize.ShouldBe((uint)ysize);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.ColorEncoding);
            dec.GetICCProfileSize(JxlColorProfileTarget.Original, out nuint decIccSize).ShouldBe(JxlDecoderStatus.Success);
            decIccSize.ShouldBe((nuint)icc.Length);

            byte[] decIcc = new byte[(int)decIccSize];

            dec.GetColorAsICCProfile(JxlColorProfileTarget.Original, decIcc).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedImageOutBuffer);

            byte[] decodedBytes = new byte[(int)bufferSize];

            dec.SetImageOutBuffer(format, decodedBytes).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.FullImage);
            decIcc.ShouldBe(icc);
        }

        /// <summary>
        /// Verifies lossless JPEG transcoding and byte-for-byte JPEG reconstruction via <see cref="JxlEncoder"/> and <see cref="JxlDecoder"/>.
        /// Ported from <c>RoundtripTest.TestJPEGReconstruction</c> in <c>lib/jxl/roundtrip_test.cc</c>.
        /// </summary>
        [Test]
        public void TestJPEGReconstruction()
        {
            byte[] orig = TestUtils.ReadTestData("jxl/flower/flower.png.im_q85_420.jpg");

            using JxlEncoder enc             = JxlEncoder.Create(null);
            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);

            enc.UseContainer(true).ShouldBe(JxlEncoderStatus.Success);
            enc.StoreJPEGMetadata(true).ShouldBe(JxlEncoderStatus.Success);
            enc.AddJPEGFrame(settings, orig).ShouldBe(JxlEncoderStatus.Success);
            enc.CloseInput();

            byte[] compressed         = EncodeWithEncoder(enc);
            byte[] reconstructedBytes = ReconstructJpegWithDecoder(compressed);

            reconstructedBytes.Length.ShouldBe(orig.Length);
            reconstructedBytes.AsSpan().SequenceEqual(orig).ShouldBeTrue();
        }

        private static byte[] ReconstructJpegWithDecoder(byte[] compressed)
        {
            using JxlDecoder dec = JxlDecoder.Create(null);

            dec.SubscribeEvents((int)(JxlDecoderStatus.JpegReconstruction | JxlDecoderStatus.FullImage))
               .ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(compressed).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.JpegReconstruction);

            byte[] buffer = new byte[4096];
            nuint used    = 0;

            dec.SetJPEGBuffer(buffer, 0, buffer.Length).ShouldBe(JxlDecoderStatus.Success);

            JxlDecoderStatus status = JxlDecoderStatus.JpegNeedMoreOutput;

            while (status == JxlDecoderStatus.JpegNeedMoreOutput)
            {
                used = (nuint)buffer.Length - dec.ReleaseJPEGBuffer();
                Array.Resize(ref buffer, buffer.Length * 2);
                dec.SetJPEGBuffer(buffer, (int)used, buffer.Length - (int)used).ShouldBe(JxlDecoderStatus.Success);
                status = dec.ProcessInput();
            }

            status.ShouldBe(JxlDecoderStatus.FullImage);
            used = (nuint)buffer.Length - dec.ReleaseJPEGBuffer();

            return buffer.AsSpan(0, (int)used).ToArray();
        }

        private static void VerifyRoundtripCompression(
            (nuint XSize, nuint YSize) dims,
            in JxlPixelFormat inputFormat,
            in JxlPixelFormat outputFormat,
            (bool Lossless, bool UseContainer, uint Resampling, bool AlreadyDownsampled, int UpsamplingMode) opts,
            IReadOnlyList<(JxlExtraChannelType Type, string Name)> extraChannels)
        {
            nuint origXSize = opts.AlreadyDownsampled ? Common.DivCeil(dims.XSize, opts.Resampling) : dims.XSize;
            nuint origYSize = opts.AlreadyDownsampled ? Common.DivCeil(dims.YSize, opts.Resampling) : dims.YSize;

            JxlPixelFormat ecFormat = inputFormat;
            ecFormat.NumChannels    = 1;

            byte[] ecBytes       = GetTestImageBytes(dims.XSize, dims.YSize, in ecFormat);
            byte[] originalBytes = GetTestImageBytes(origXSize, origYSize, in inputFormat);
            byte[] compressed    = EncodeRoundtripStream(dims, in inputFormat, in ecFormat, opts, extraChannels, originalBytes, ecBytes);

            DecodeAndVerifyRoundtripStream(dims, in inputFormat, in outputFormat, in ecFormat, opts, extraChannels, ecBytes, compressed);
        }

        private static byte[] EncodeRoundtripStream(
            (nuint XSize, nuint YSize) dims,
            in JxlPixelFormat inputFormat,
            in JxlPixelFormat ecFormat,
            (bool Lossless, bool UseContainer, uint Resampling, bool AlreadyDownsampled, int UpsamplingMode) opts,
            IReadOnlyList<(JxlExtraChannelType Type, string Name)> extraChannels,
            byte[] originalBytes,
            byte[] ecBytes)
        {
            using JxlEncoder enc = JxlEncoder.Create(null);

            enc.SetCodestreamLevel(10).ShouldBe(JxlEncoderStatus.Success);
            enc.UseContainer(opts.UseContainer).ShouldBe(JxlEncoderStatus.Success);

            JxlBasicInfo basicInfo = default;

            TestUtils.JxlBasicInfoSetFromPixelFormat(ref basicInfo, in inputFormat);
            basicInfo.XSize               = (uint)dims.XSize;
            basicInfo.YSize               = (uint)dims.YSize;
            basicInfo.UsesOriginalProfile = opts.Lossless;

            uint hasInterleavedAlpha   = (inputFormat.NumChannels == 2 || inputFormat.NumChannels == 4) ? 1u : 0u;
            basicInfo.NumExtraChannels = (uint)extraChannels.Count + hasInterleavedAlpha;

            enc.SetBasicInfo(in basicInfo).ShouldBe(JxlEncoderStatus.Success);

            bool isGray                    = inputFormat.NumChannels < 3;
            JxlColorEncoding colorEncoding = inputFormat.DataType == JxlDataType.Float
                ? ColorEncoding.LinearSRGB(isGray).ToExternal()
                : ColorEncoding.SRGB(isGray).ToExternal();

            ConfigureExtraChannelMetadata(enc, in basicInfo, opts.Lossless, hasInterleavedAlpha, extraChannels);
            enc.SetColorEncoding(in colorEncoding).ShouldBe(JxlEncoderStatus.Success);
            enc.SetUpsamplingMode(opts.Resampling, opts.UpsamplingMode).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);

            enc.SetFrameLossless(settings, opts.Lossless).ShouldBe(JxlEncoderStatus.Success);

            if (opts.Resampling > 1)
            {
                enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.Resampling, opts.Resampling).ShouldBe(JxlEncoderStatus.Success);
                enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.AlreadyDownsampled, opts.AlreadyDownsampled ? 1 : 0).ShouldBe(JxlEncoderStatus.Success);
            }

            enc.AddImageFrame(settings, in inputFormat, originalBytes).ShouldBe(JxlEncoderStatus.Success);

            for (int i = 0; i < extraChannels.Count; i++)
            {
                enc.SetExtraChannelBuffer(settings, in ecFormat, ecBytes, (uint)(i + (int)hasInterleavedAlpha))
                   .ShouldBe(JxlEncoderStatus.Success);
            }

            enc.CloseInput();

            return EncodeWithEncoder(enc);
        }

        private static void ConfigureExtraChannelMetadata(
            JxlEncoder enc,
            in JxlBasicInfo basicInfo,
            bool lossless,
            uint hasInterleavedAlpha,
            IReadOnlyList<(JxlExtraChannelType Type, string Name)> extraChannels)
        {
            for (int i = 0; i < extraChannels.Count; i++)
            {
                JxlEncoder.JxlEncoderInitExtraChannelInfo(extraChannels[i].Type, out JxlExtraChannelInfo channelInfo);
                channelInfo.BitsPerSample         = lossless ? basicInfo.BitsPerSample : 8u;
                channelInfo.ExponentBitsPerSample = lossless ? basicInfo.ExponentBitsPerSample : 0u;

                nuint slot = (nuint)(i + (int)hasInterleavedAlpha);

                enc.SetExtraChannelInfo(slot, in channelInfo).ShouldBe(JxlEncoderStatus.Success);
                enc.SetExtraChannelName(slot, extraChannels[i].Name).ShouldBe(JxlEncoderStatus.Success);
            }
        }

        private static void DecodeAndVerifyRoundtripStream(
            (nuint XSize, nuint YSize) dims,
            in JxlPixelFormat inputFormat,
            in JxlPixelFormat outputFormat,
            in JxlPixelFormat ecFormat,
            (bool Lossless, bool UseContainer, uint Resampling, bool AlreadyDownsampled, int UpsamplingMode) opts,
            IReadOnlyList<(JxlExtraChannelType Type, string Name)> extraChannels,
            byte[] ecBytes,
            byte[] compressed)
        {
            using JxlDecoder dec = JxlDecoder.Create(null);

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.FullImage))
               .ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(compressed).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);

            dec.ImageOutBufferSize(outputFormat, out nuint bufferSize).ShouldBe(JxlDecoderStatus.Success);
            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.XSize.ShouldBe((uint)dims.XSize);
            info.YSize.ShouldBe((uint)dims.YSize);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.ColorEncoding);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedImageOutBuffer);

            byte[] decodedBytes = new byte[(int)bufferSize];

            dec.SetImageOutBuffer(outputFormat, decodedBytes).ShouldBe(JxlDecoderStatus.Success);

            uint hasInterleavedAlpha = (inputFormat.NumChannels == 2 || inputFormat.NumChannels == 4) ? 1u : 0u;
            List<byte[]> ecDecoded   = AttachExtraChannelOutBuffers(dec, in ecFormat, hasInterleavedAlpha, extraChannels);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.FullImage);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Success);

            if (opts.Lossless)
            {
                for (int i = 0; i < ecDecoded.Count; i++)
                {
                    ecDecoded[i].ShouldBe(ecBytes);
                }
            }
        }

        private static List<byte[]> AttachExtraChannelOutBuffers(
            JxlDecoder dec,
            in JxlPixelFormat ecFormat,
            uint hasInterleavedAlpha,
            IReadOnlyList<(JxlExtraChannelType Type, string Name)> extraChannels)
        {
            List<byte[]> ecDecoded = new List<byte[]>(extraChannels.Count);

            for (int i = 0; i < extraChannels.Count; i++)
            {
                nuint slot = (nuint)(i + (int)hasInterleavedAlpha);
                uint slotU = (uint)(i + (int)hasInterleavedAlpha);

                dec.GetExtraChannelInfo(slot, out JxlExtraChannelInfo ecInfo).ShouldBe(JxlDecoderStatus.Success);
                ecInfo.Type.ShouldBe(extraChannels[i].Type);

                byte[] nameBuf = new byte[(int)ecInfo.NameLength + 1];

                dec.GetExtraChannelName(slot, nameBuf).ShouldBe(JxlDecoderStatus.Success);
                Encoding.UTF8.GetString(nameBuf, 0, (int)ecInfo.NameLength).ShouldBe(extraChannels[i].Name);

                dec.ExtraChannelBufferSize(ecFormat, out nuint ecSize, slotU).ShouldBe(JxlDecoderStatus.Success);
                byte[] buf = new byte[(int)ecSize];

                ecDecoded.Add(buf);
                dec.SetExtraChannelBuffer(ecFormat, buf, slotU).ShouldBe(JxlDecoderStatus.Success);
            }

            return ecDecoded;
        }

        private static byte[] EncodeMultiFrameAnimation(
            nuint xsize,
            nuint ysize,
            nuint nbFrames,
            int indexFrames,
            in JxlPixelFormat pixelFormat,
            byte[] originalBytes)
        {
            using JxlEncoder enc = JxlEncoder.Create(null);

            enc.UseContainer(true).ShouldBe(JxlEncoderStatus.Success);

            JxlBasicInfo basicInfo = default;

            TestUtils.JxlBasicInfoSetFromPixelFormat(ref basicInfo, in pixelFormat);
            basicInfo.XSize               = (uint)xsize;
            basicInfo.YSize               = (uint)ysize;
            basicInfo.UsesOriginalProfile = false;
            basicInfo.HaveAnimation       = true;
            basicInfo.Animation           = new JxlAnimationHeader
            {
                TpsNumerator   = 1,
                TpsDenominator = 1,
            };

            enc.SetCodestreamLevel(10).ShouldBe(JxlEncoderStatus.Success);
            enc.SetBasicInfo(in basicInfo).ShouldBe(JxlEncoderStatus.Success);

            JxlColorEncoding colorEncoding = ColorEncoding.LinearSRGB(pixelFormat.NumChannels < 3).ToExternal();

            enc.SetColorEncoding(in colorEncoding).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);

            enc.SetFrameLossless(settings, false).ShouldBe(JxlEncoderStatus.Success);

            if (indexFrames == 1)
            {
                enc.FrameSettingsSetOption(settings, JxlEncoderFrameSettingId.FrameIndexBox, 1).ShouldBe(JxlEncoderStatus.Success);
            }

            int oneFrameSize = originalBytes.Length / (int)nbFrames;

            JxlEncoder.JxlEncoderInitFrameHeader(out JxlFrameHeader frameHeader);
            frameHeader.Duration = 1;
            frameHeader.IsLast   = false;

            for (nuint i = 0; i < nbFrames; i++)
            {
                if (i + 1 == nbFrames)
                {
                    frameHeader.IsLast = true;
                }

                enc.SetFrameHeader(settings, in frameHeader).ShouldBe(JxlEncoderStatus.Success);
                enc.AddImageFrame(settings, in pixelFormat, originalBytes.AsSpan((int)i * oneFrameSize, oneFrameSize))
                   .ShouldBe(JxlEncoderStatus.Success);
            }

            enc.CloseInput();

            return EncodeWithEncoder(enc);
        }

        private static void DecodeAndVerifyMultiFrameAnimation(
            nuint xsize,
            nuint ysize,
            nuint nbFrames,
            in JxlPixelFormat pixelFormat,
            byte[] originalBytes,
            byte[] compressed)
        {
            using JxlDecoder dec = JxlDecoder.Create(null);
            int oneFrameSize     = originalBytes.Length / (int)nbFrames;

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.FullImage))
               .ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(compressed).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
            dec.ImageOutBufferSize(pixelFormat, out nuint bufferSize).ShouldBe(JxlDecoderStatus.Success);
            bufferSize.ShouldBe((nuint)oneFrameSize);

            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.XSize.ShouldBe((uint)xsize);
            info.YSize.ShouldBe((uint)ysize);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.ColorEncoding);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.NeedImageOutBuffer);

            byte[] frameBuf = new byte[oneFrameSize];

            for (nuint i = 0; i < nbFrames; i++)
            {
                dec.SetImageOutBuffer(pixelFormat, frameBuf).ShouldBe(JxlDecoderStatus.Success);
                dec.ProcessInput().ShouldBe(JxlDecoderStatus.FullImage);
            }
        }

        private static byte[] EncodeWithEncoder(JxlEncoder enc)
        {
            byte[] compressed       = new byte[64];
            int offset              = 0;
            JxlEncoderStatus status = JxlEncoderStatus.NeedMoreOutput;

            while (status == JxlEncoderStatus.NeedMoreOutput)
            {
                status = enc.ProcessOutput(compressed, ref offset);

                if (status == JxlEncoderStatus.NeedMoreOutput)
                {
                    Array.Resize(ref compressed, compressed.Length * 2);
                }
            }

            status.ShouldBe(JxlEncoderStatus.Success);

            return compressed.AsSpan(0, offset).ToArray();
        }

        private static byte[] GetTestImageBytes(nuint xsize, nuint ysize, in JxlPixelFormat format)
        {
            int numChannels     = (int)format.NumChannels;
            int bytesPerChannel = (int)(PackedImage.BitsPerChannel(format.DataType) / 8);
            byte[] bytes        = new byte[(int)(xsize * ysize) * numChannels * bytesPerChannel];

            for (nuint y = 0; y < ysize; y++)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    for (int chan = 0; chan < numChannels; chan++)
                    {
                        float val  = ComputeSyntheticSample(x, y, xsize, ysize, chan);
                        int offset = (((int)(y * xsize + x) * numChannels) + chan) * bytesPerChannel;

                        WriteSampleValue(bytes.AsSpan(offset, bytesPerChannel), format.DataType, val);
                    }
                }
            }

            return bytes;
        }

        private static float ComputeSyntheticSample(nuint x, nuint y, nuint xsize, nuint ysize, int chan)
        {
            return (chan % 4) switch
            {
                0 => (float)y / ysize,
                1 => (float)x / xsize,
                2 => (float)(x + y) / (xsize + ysize),
                _ => (float)(x * y) / (xsize * ysize),
            };
        }

        private static void WriteSampleValue(Span<byte> dst, JxlDataType dataType, float val)
        {
            if (dataType == JxlDataType.UInt8)
            {
                dst[0] = (byte)(val * byte.MaxValue);
            }
            else if (dataType == JxlDataType.UInt16)
            {
                ushort v16 = (ushort)(val * ushort.MaxValue);
                MemoryMarshal.Write(dst, in v16);
            }
            else if (dataType == JxlDataType.Float)
            {
                MemoryMarshal.Write(dst, in val);
            }
        }
    }

    /// <summary>
    /// Comprehensive end-to-end JXL roundtrip, lossless, JPEG recompression, and streaming tests ported from <c>lib/jxl/jxl_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class JxlTests
    {
        /// <summary>
        /// Verifies roundtrip of a 1x1 RGB image and 1x1 RGBA image.
        /// Ported from <c>JxlTest.RoundtripSinglePixel</c> and <c>RoundtripSinglePixelWithAlpha</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(3u)]
        [TestCase(4u)]
        public void RoundtripSinglePixel(uint channels)
        {
            TestImage t = new TestImage();

            t.SetDimensions(1, 1).IsOk.ShouldBeTrue();
            t.SetChannels(channels).IsOk.ShouldBeTrue();

            StatusOr<TestImage.Frame> frameOr = t.AddFrame();

            frameOr.IsOk.ShouldBeTrue();
            frameOr.Value.ZeroFill();

            PackedPixelFile ppfOut = new PackedPixelFile();
            nuint compressedSize   = TestUtils.Roundtrip(t.Ppf, new JXLCompressParams(), new JXLDecompressParams(), null, ppfOut);

            compressedSize.ShouldBeInRange((nuint)15, (nuint)60);
        }

        /// <summary>
        /// Verifies that corrupting the codestream signature bytes causes decoding to fail cleanly.
        /// Ported from <c>JxlTest.RoundtripMarker</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripMarker()
        {
            TestImage t = new TestImage();

            t.SetDimensions(1, 1).IsOk.ShouldBeTrue();

            StatusOr<TestImage.Frame> frameOr = t.AddFrame();

            frameOr.IsOk.ShouldBeTrue();
            frameOr.Value.ZeroFill();

            for (int i = 0; i < 2; i++)
            {
                CodecJxl.EncodeImageJXL(new JXLCompressParams(), t.Ppf, null, out byte[] compressed).ShouldBeTrue();
                compressed[i] ^= 0xFF;

                PackedPixelFile ppfOut = new PackedPixelFile();

                CodecJxl.DecodeImageJXL(compressed, new JXLDecompressParams(), out _, ppfOut).ShouldBeFalse();
            }
        }

        /// <summary>
        /// Verifies fast effort-7 roundtrip on a 32x32 image and a downscaled small D1 image.
        /// Ported from <c>JxlTest.RoundtripTinyFast</c> and <c>RoundtripSmallD1</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripTinyFastAndSmallD1()
        {
            byte[] orig = TestUtils.ReadTestData("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");
            TestImage t = new TestImage();

            t.DecodeFromBytes(orig).IsOk.ShouldBeTrue();
            t.ClearMetadata();
            t.SetDimensions(32, 32).IsOk.ShouldBeTrue();

            JXLCompressParams cparams = new JXLCompressParams { Distance = 4.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 7);

            PackedPixelFile ppfOut = new PackedPixelFile();
            nuint size             = TestUtils.Roundtrip(t.Ppf, cparams, new JXLDecompressParams(), null, ppfOut);

            size.ShouldBeInRange((nuint)100, (nuint)300);

            t.Ppf.Info.IntensityTarget = 100.0f;
            PackedPixelFile ppfOut2    = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, new JXLCompressParams(), new JXLDecompressParams(), null, ppfOut2);
            ppfOut2.Info.IntensityTarget.ShouldBe(100.0f);
        }

        /// <summary>
        /// Verifies out-of-order parallel group processing using <see cref="FakeParallelRunner"/>.
        /// Ported from <c>JxlTest.RoundtripOutOfOrderProcessing</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripOutOfOrderProcessing()
        {
            FakeParallelRunner fakePool = new FakeParallelRunner(123, 4);
            ThreadPool pool             = new ThreadPool(FakeParallelRunner.Runner, fakePool.RunnerOpaque);
            byte[] orig                 = TestUtils.ReadTestData("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");
            TestImage t                 = new TestImage();

            t.DecodeFromBytes(orig).IsOk.ShouldBeTrue();
            t.ClearMetadata();
            t.SetDimensions(265, 267).IsOk.ShouldBeTrue();

            JXLCompressParams cparams = new JXLCompressParams();

            cparams.AddOption(JxlEncoderFrameSettingId.Epf, 3);

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, new JXLDecompressParams(), pool, ppfOut).ShouldBeGreaterThan((nuint)100);
            TestUtils.ButteraugliDistance(t.Ppf, ppfOut).ShouldBeLessThanOrEqualTo(2.0f);
        }

        /// <summary>
        /// Verifies patch dictionary detection and reconstruction on synthetic repeating patterns with and without alpha.
        /// Ported from <c>JxlTest.RoundtripSmallPatches</c> and <c>RoundtripSmallPatchesAlpha</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(3u)]
        [TestCase(4u)]
        public void RoundtripSmallPatches(uint channels)
        {
            TestImage t = new TestImage();

            t.SetDimensions(128, 128).IsOk.ShouldBeTrue();
            t.SetChannels(channels).IsOk.ShouldBeTrue();
            t.SetColorEncoding("RGB_D65_SRG_Rel_Lin").IsOk.ShouldBeTrue();

            StatusOr<TestImage.Frame> frameOr = t.AddFrame();

            frameOr.IsOk.ShouldBeTrue();

            TestImage.Frame frame = frameOr.Value;

            frame.ZeroFill();
            FillRepeatingPatchPattern(t, frame, channels == 4);

            JXLCompressParams cparams = new JXLCompressParams { Distance = 0.1f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 7);

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, new JXLDecompressParams(), null, ppfOut).ShouldBeLessThanOrEqualTo((nuint)1000);
            TestUtils.ButteraugliDistance(t.Ppf, ppfOut).ShouldBeLessThanOrEqualTo(0.1f);
        }

        /// <summary>
        /// Verifies grayscale roundtrip and grayscale-to-RGB color space conversion on decode.
        /// Ported from <c>JxlTest.RoundtripGrayscale</c> and <c>RoundtripLossless8Gray</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripGrayscaleAndLosslessGray()
        {
            byte[] orig = TestUtils.ReadTestData("external/wesaturate/500px/cvo9xd_keong_macan_grayscale.png");
            TestImage t = new TestImage();

            t.SetColorEncoding("Gra_D65_Rel_SRG").IsOk.ShouldBeTrue();
            t.DecodeFromBytes(orig).IsOk.ShouldBeTrue();
            t.ClearMetadata();
            t.SetDimensions(128, 128).IsOk.ShouldBeTrue();

            JXLCompressParams cparams   = TestUtils.CompressParamsForLossless();
            JXLDecompressParams dparams = new JXLDecompressParams();

            dparams.AcceptedFormats.Add(t.Ppf.Frames[0].Color.Format);

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, dparams, null, ppfOut);
            TestUtils.ComputeDistance2(t.Ppf, ppfOut).ShouldBe(0.0f);
            ppfOut.ColorEncoding.ColorSpace.ShouldBe(JxlColorSpace.Gray);
        }

        /// <summary>
        /// Verifies lossless 8-bit and 16-bit alpha roundtrips, including 16-bit values below 64.
        /// Ported from <c>JxlTest.RoundtripLossless8Alpha</c>, <c>RoundtripLossless16Alpha</c>, and <c>RoundtripLossless16AlphaNotMisdetectedAs8Bit</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(65535u)]
        [TestCase(64u)]
        public void RoundtripLossless16Alpha(uint maxVal)
        {
            const nuint xsize = 128;
            const nuint ysize = 64;
            TestImage t       = new TestImage();

            t.SetDimensions(xsize, ysize).IsOk.ShouldBeTrue();
            t.SetChannels(4).IsOk.ShouldBeTrue();
            t.SetAllBitDepths(16);

            StatusOr<TestImage.Frame> frameOr = t.AddFrame();

            frameOr.IsOk.ShouldBeTrue();

            TestImage.Frame frame = frameOr.Value;

            frame.ZeroFill();
            Fill16BitRgbaPattern(frame, xsize, ysize, maxVal);

            JXLCompressParams cparams   = TestUtils.CompressParamsForLossless();
            JXLDecompressParams dparams = new JXLDecompressParams();

            dparams.AcceptedFormats.Add(t.Ppf.Frames[0].Color.Format);

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, dparams, null, ppfOut);
            TestUtils.ComputeDistance2(t.Ppf, ppfOut).ShouldBe(0.0f);
            ppfOut.Info.BitsPerSample.ShouldBe(16u);
            ppfOut.Info.AlphaBits.ShouldBe(16u);
            TestUtils.SameAlpha(t.Ppf, ppfOut).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies lossless PNM/PGM/PPM/PAM roundtrip across bit depths and channel counts.
        /// Ported from <c>JxlTest.LosslessPNMRoundtrip</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(1, 1, "g", ".pgm")]
        [TestCase(8, 1, "g", ".pgm")]
        [TestCase(8, 2, "ga", ".pam")]
        [TestCase(8, 3, "rgb", ".ppm")]
        [TestCase(8, 4, "rgba", ".pam")]
        [TestCase(16, 3, "rgb", ".ppm")]
        [TestCase(16, 4, "rgba", ".pam")]
        public void LosslessPNMRoundtrip(int bitDepth, int channels, string chanName, string extension)
        {
            string filename = $"jxl/flower/flower_small.{chanName}.depth{bitDepth}{extension}";
            byte[] orig     = TestUtils.ReadTestData(filename);
            TestImage t     = new TestImage();

            if (channels < 3)
            {
                t.SetColorEncoding("Gra_D65_Rel_SRG").IsOk.ShouldBeTrue();
            }

            t.DecodeFromBytes(orig).IsOk.ShouldBeTrue();

            JXLCompressParams cparams = TestUtils.CompressParamsForLossless();

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 1);

            JXLDecompressParams dparams = new JXLDecompressParams();

            dparams.AcceptedFormats.Add(t.Ppf.Frames[0].Color.Format);
            dparams.OutputBitdepth.Type = JxlBitDepthType.FromCodestream;

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, dparams, null, ppfOut);

            Extras.Encoder? encoder = ExtrasEncodeFacade.FromExtension(extension);

            encoder.ShouldNotBeNull();

            EncodedImage encoded = new EncodedImage();

            encoder!.Encode(ppfOut, encoded, null).IsOk.ShouldBeTrue();
            encoded.Bitstreams.Count.ShouldBe(1);
            encoded.Bitstreams[0].Length.ShouldBe(orig.Length);
            encoded.Bitstreams[0].AsSpan().SequenceEqual(orig).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies lossless roundtrip on small palette/few-color images.
        /// Ported from <c>JxlTest.LosslessSmallFewColors</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase("jxl/blending/cropped_traffic_light_frame-0.png")]
        [TestCase("palette/358colors.png")]
        public void LosslessSmallFewColors(string testFile)
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            byte[] orig                   = TestUtils.ReadTestData(testFile);
            TestImage t                   = new TestImage();

            t.DecodeFromBytes(orig).IsOk.ShouldBeTrue();
            t.ClearMetadata();

            JXLCompressParams cparams = new JXLCompressParams { Distance = 0.0f };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 1);

            JXLDecompressParams dparams = new JXLDecompressParams();

            dparams.AcceptedFormats.Add(t.Ppf.Frames[0].Color.Format);

            PackedPixelFile ppfOut = new PackedPixelFile();

            TestUtils.Roundtrip(t.Ppf, cparams, dparams, pool.Get(), ppfOut);
            TestUtils.ComputeDistance2(t.Ppf, ppfOut).ShouldBe(0.0f);
        }

        /// <summary>
        /// Verifies lossless JPEG recompression and byte-exact reconstruction across chroma subsamplings, restarts, progressive JPEGs, and metadata.
        /// Ported from <c>JxlTest.RoundtripJpegRecompression*</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase("jxl/flower/flower.png.im_q85_444.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_gray.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_420.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_422.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_440.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_asymmetric.jpg")]
        [TestCase("jxl/flower/flower.png.im_q85_420_progr.jpg")]
        [TestCase("jxl/jpeg_reconstruction/1x1_exif_xmp.jpg")]
        [TestCase("jxl/jpeg_reconstruction/bicycles_restarts.jpg")]
        [TestCase("jxl/jpeg_reconstruction/sideways_bench.jpg")]
        public void RoundtripJpegRecompression(string jpegPath)
        {
            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            byte[] jpegIn                 = TestUtils.ReadTestData(jpegPath);

            CodecJxl.EncodeImageJXL(new JXLCompressParams(), new PackedPixelFile(), jpegIn, out byte[] compressed).ShouldBeTrue();

            JXLDecompressParams dparams = new JXLDecompressParams();
            List<byte> outJpeg          = new List<byte>();
            PackedPixelFile ppf         = new PackedPixelFile();

            CodecJxl.DecodeImageJXL(compressed, dparams, out _, ppf, outJpeg).ShouldBeTrue();
            outJpeg.Count.ShouldBe(jpegIn.Length);
            outJpeg.ToArray().AsSpan().SequenceEqual(jpegIn).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies streaming buffering mode (<c>Buffering = 3</c>) produces identical pixels to non-streaming mode.
        /// Ported from <c>JxlStreamingEncodingTest.StreamingSamePixels</c> in <c>lib/jxl/jxl_test.cc</c>.
        /// </summary>
        [TestCase(1, 0.0f)]
        [TestCase(3, 1.0f)]
        [TestCase(5, 0.0f)]
        public void StreamingSamePixels(int effort, float distance)
        {
            byte[] orig     = TestUtils.ReadTestData("external/wesaturate/500px/u76c0g_bliznaca_srgb8.png");
            TestImage image = new TestImage();

            image.DecodeFromBytes(orig).IsOk.ShouldBeTrue();
            image.SetDimensions(128, 128).IsOk.ShouldBeTrue();

            JXLCompressParams cparams = new JXLCompressParams { Distance = distance };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, effort);
            cparams.AddOption(JxlEncoderFrameSettingId.UseFullImageHeuristics, 0);

            using ThreadPoolForTests pool = new ThreadPoolForTests(4);
            PackedPixelFile ppfOut        = new PackedPixelFile();

            TestUtils.Roundtrip(image.Ppf, cparams, new JXLDecompressParams(), pool.Get(), ppfOut);

            cparams.AddOption(JxlEncoderFrameSettingId.Buffering, 3);
            PackedPixelFile ppfOutStreaming = new PackedPixelFile();

            TestUtils.Roundtrip(image.Ppf, cparams, new JXLDecompressParams(), pool.Get(), ppfOutStreaming);
            TestUtils.SamePixels(ppfOut, ppfOutStreaming).ShouldBeTrue();
        }

        private static void FillRepeatingPatchPattern(TestImage t, TestImage.Frame frame, bool hasAlpha)
        {
            for (nuint y = 0; y < t.Ppf.Info.YSize; ++y)
            {
                for (nuint x = 0; x < t.Ppf.Info.XSize; ++x)
                {
                    if ((x % 4) == 0 && ((y / 32) % 4) == 0)
                    {
                        frame.SetValue(y, x, 1, 127.0f / 255.0f).IsOk.ShouldBeTrue();
                    }

                    if (hasAlpha)
                    {
                        frame.SetValue(y, x, 3, 1.0f).IsOk.ShouldBeTrue();
                    }
                }
            }
        }

        private static void Fill16BitRgbaPattern(TestImage.Frame frame, nuint xsize, nuint ysize, uint maxVal)
        {
            const float mul = 1.0f / 65535.0f;

            for (nuint y = 0; y < ysize; y++)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    ushort r = (ushort)((y * maxVal) / ysize);
                    ushort g = (ushort)(((x * maxVal) / xsize) + 37u);
                    ushort b = (ushort)(((y + x) * maxVal) / (xsize + ysize));

                    frame.SetValue(y, x, 0, r * mul).IsOk.ShouldBeTrue();
                    frame.SetValue(y, x, 1, g * mul).IsOk.ShouldBeTrue();
                    frame.SetValue(y, x, 2, b * mul).IsOk.ShouldBeTrue();
                    frame.SetValue(y, x, 3, g * mul).IsOk.ShouldBeTrue();
                }
            }
        }
    }
}
