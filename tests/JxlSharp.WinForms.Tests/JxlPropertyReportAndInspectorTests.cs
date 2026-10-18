// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Unit tests for <see cref="JxlPropertyReport"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlPropertyReportTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlPropertyReport.BuildPropertyRows"/> populates all categories
        /// including preview, animation, color encoding, extra channels, frames, and container boxes.
        /// </summary>
        [Test]
        public void BuildPropertyRowsPopulatesAllMetadataCategories()
        {
            Mock<IFormatProvider> mockProvider = new Mock<IFormatProvider>();
            mockProvider.Setup(p => p.GetFormat(It.IsAny<Type>())).Returns((object?)null);

            JxlPropertyReport report = new JxlPropertyReport
            {
                SourceLabel              = "sample.jxl",
                FileSize                 = 4096,
                Signature                = JxlSignature.Container,
                IsValid                  = true,
                HasColorEncoding         = true,
                ColorDescriptionString   = "RGB_D65_SRG_Rel_SRG",
                OriginalIccSize          = 512,
                DataIccSize              = 512,
                HasJpegReconstruction    = true,
                AnimationDurationSeconds = 1.25f,
                VerboseInfoText          = "JPEG XL container, 64x64",
            };

            PopulateSampleBasicAndColorInfo(report);
            PopulateSampleChannelsFramesAndBoxes(report);
            report.BuildPropertyRows();

            mockProvider.Object.ShouldNotBeNull();
            report.PropertyRows.Count.ShouldBeGreaterThan(35);
            report.PropertyRows.ShouldContain(r => r.Category == "File" && r.Name == "Source" && r.Value == "sample.jxl");
            report.PropertyRows.ShouldContain(r => r.Category == "Preview" && r.Name == "Preview Dimensions" && r.Value == "16 x 16");
            report.PropertyRows.ShouldContain(r => r.Category == "Animation" && r.Name == "NumLoops" && r.Value == "3");
            report.PropertyRows.ShouldContain(r => r.Category == "ColorEncoding" && r.Name == "Description" && r.Value == "RGB_D65_SRG_Rel_SRG");
            report.PropertyRows.ShouldContain(r => r.Category == "ExtraChannel[0]" && r.Name == "Name" && r.Value == "AlphaChan");
            report.PropertyRows.ShouldContain(r => r.Category == "Frame[0]" && r.Name == "Name" && r.Value == "MainFrame");
            report.PropertyRows.ShouldContain(r => r.Category == "Box[0]" && r.Name == "Type" && r.Value == "brob (Exif)");
        }

        /// <summary>
        /// Verifies that <see cref="JxlPropertyReport.BuildPropertyRows"/> handles minimal non-animated reports without color encoding.
        /// </summary>
        [Test]
        public void BuildPropertyRowsHandlesMinimalReportWithoutColorEncoding()
        {
            JxlPropertyReport report = new JxlPropertyReport
            {
                SourceLabel      = "minimal.jxl",
                FileSize         = 128,
                Signature        = JxlSignature.Codestream,
                IsValid          = true,
                HasColorEncoding = false,
            };

            report.ExtraChannels.Add(new JxlExtraChannelSummary(0, string.Empty, new JxlExtraChannelInfo { Type = JxlExtraChannelType.Alpha, BitsPerSample = 8 }));
            report.Frames.Add(new JxlFrameSummary(0, string.Empty, new JxlFrameHeader { IsLast = true }, 0.0f));
            report.Boxes.Add(new JxlContainerBoxSummary(0, "jxlc", string.Empty, 100, "Codestream"));
            report.BuildPropertyRows();

            report.PropertyRows.ShouldContain(r => r.Category == "ExtraChannel[0]" && r.Value == "(unnamed)");
            report.PropertyRows.ShouldContain(r => r.Category == "Frame[0]" && r.Value == "(unnamed)");
            report.PropertyRows.ShouldContain(r => r.Category == "Box[0]" && r.Value == "jxlc");
        }

        private static void PopulateSampleBasicAndColorInfo(JxlPropertyReport report)
        {
            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.HaveContainer        = true;
            info.XSize                = 64;
            info.YSize                = 64;
            info.IntrinsicXSize       = 64;
            info.IntrinsicYSize       = 64;
            info.BitsPerSample        = 8;
            info.NumColorChannels     = 3;
            info.NumExtraChannels     = 1;
            info.AlphaBits            = 8;
            info.HavePreview          = true;
            info.Preview              = new JxlPreviewHeader { XSize = 16, YSize = 16 };
            info.HaveAnimation        = true;
            info.Animation            = new JxlAnimationHeader { TpsNumerator = 100, TpsDenominator = 1, NumLoops = 3, HaveTimecodes = true };
            report.BasicInfo          = info;

            ColorDescription.ParseDescription("RGB_D65_SRG_Rel_SRG", out JxlColorEncoding ce);
            report.ColorEncoding = ce;
        }

        private static void PopulateSampleChannelsFramesAndBoxes(JxlPropertyReport report)
        {
            JxlExtraChannelInfo ec = new JxlExtraChannelInfo
            {
                Type          = JxlExtraChannelType.Alpha,
                BitsPerSample = 8,
            };

            JxlFrameHeader fh = new JxlFrameHeader
            {
                Duration  = 125,
                IsLast    = true,
                LayerInfo = new JxlLayerInfo
                {
                    XSize     = 64,
                    YSize     = 64,
                    HaveCrop  = true,
                    CropX0    = 2,
                    CropY0    = 4,
                    BlendInfo = new JxlBlendInfo { BlendMode = JxlBlendMode.Blend },
                },
            };

            report.ExtraChannels.Add(new JxlExtraChannelSummary(0, "AlphaChan", ec));
            report.Frames.Add(new JxlFrameSummary(0, "MainFrame", fh, 125.0f));
            report.Boxes.Add(new JxlContainerBoxSummary(0, "brob", "Exif", 48, "Brotli Exif"));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlPropertyInspector"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlPropertyInspectorTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlPropertyInspector.InspectBytes"/> extracts codestream metadata,
        /// color encoding, frames, and verbose info from a valid JXL image.
        /// </summary>
        [Test]
        public void InspectBytesExtractsCodestreamAndContainerMetadata()
        {
            JxlPropertyInspector inspector = new JxlPropertyInspector();
            TestImage testImg              = new TestImage();

            testImg.SetDimensions(16, 16);
            testImg.SetChannels(4);
            testImg.SetAllBitDepths(8);

            TestImage.Frame frame = testImg.AddFrame().Value;
            frame.RandomFill(42);

            JXLCompressParams cparams = new JXLCompressParams
            {
                Distance     = 0.0f,
                UseContainer = true,
            };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 1);
            cparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);

            bool encoded = CodecJxl.EncodeImageJXL(cparams, testImg.Ppf, null, out byte[] jxlBytes);
            encoded.ShouldBeTrue();

            JxlPropertyReport report = inspector.InspectBytes(jxlBytes, "test_rgba.jxl");

            report.IsValid.ShouldBeTrue();
            report.BasicInfo.XSize.ShouldBe(16u);
            report.BasicInfo.YSize.ShouldBe(16u);
            report.BasicInfo.AlphaBits.ShouldBe(8u);
            report.ExtraChannels.Count.ShouldBe(1);
            report.Frames.Count.ShouldBeGreaterThanOrEqualTo(1);
            report.HasColorEncoding.ShouldBeTrue();
            report.VerboseInfoText.ShouldContain("16x16");
            report.PropertyRows.Count.ShouldBeGreaterThan(20);
        }

        /// <summary>
        /// Verifies that <see cref="JxlPropertyInspector.InspectFile"/> handles missing files and valid JXL container files with boxes.
        /// </summary>
        [Test]
        public void InspectFileHandlesMissingAndValidContainerFileWithBoxes()
        {
            JxlPropertyInspector inspector = new JxlPropertyInspector();
            JxlPropertyReport missing      = inspector.InspectFile(Path.Combine(Path.GetTempPath(), "non_existent_12345.jxl"));

            missing.IsValid.ShouldBeFalse();
            missing.PropertyRows.Count.ShouldBeGreaterThan(0);

            byte[] invalidBytes          = new byte[] { 1, 2, 3, 4 };
            JxlPropertyReport badReport  = inspector.InspectBytes(invalidBytes, "bad.bin");
            badReport.IsValid.ShouldBeFalse();

            byte[] containerWithBoxes = BuildSyntheticContainerWithAllBoxes();
            string tempFile           = Path.Combine(Path.GetTempPath(), $"inspect_boxes_{Guid.NewGuid():N}.jxl");

            try
            {
                File.WriteAllBytes(tempFile, containerWithBoxes);
                JxlPropertyReport boxReport = inspector.InspectFile(tempFile);

                boxReport.IsValid.ShouldBeTrue();
                boxReport.Signature.ShouldBe(JxlSignature.Container);
                boxReport.HasJpegReconstruction.ShouldBeTrue();
                boxReport.Boxes.ShouldContain(b => b.BoxType == "JXL ");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "ftyp");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "jxll");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "jbrd");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "Exif");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "xml ");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "jumb");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "brob" && b.InnerBoxType == "Exif");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "jhgm");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "jxlp");
                boxReport.Boxes.ShouldContain(b => b.BoxType == "cust");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static byte[] BuildSyntheticContainerWithAllBoxes()
        {
            TestImage testImg = new TestImage();
            testImg.SetDimensions(8, 8);
            testImg.SetChannels(3);
            testImg.SetAllBitDepths(8);
            testImg.AddFrame().Value.ZeroFill();

            JXLCompressParams cparams = new JXLCompressParams { Distance = 0.0f };
            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 1);
            cparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
            CodecJxl.EncodeImageJXL(cparams, testImg.Ppf, null, out byte[] rawCodestream);

            using MemoryStream ms = new MemoryStream();

            WriteBox(ms, "JXL ", new byte[] { 0x0D, 0x0A, 0x87, 0x0A });
            WriteBox(ms, "ftyp", Encoding.ASCII.GetBytes("jxl \0\0\0\0jxl "));
            WriteBox(ms, "jxll", new byte[] { 5 });
            WriteBox(ms, "jxlc", rawCodestream);
            WriteBox(ms, "jxlp", new byte[] { 0, 0, 0, 0 });
            WriteBox(ms, "jbrd", new byte[] { 1, 2, 3, 4 });
            WriteBox(ms, "Exif", new byte[] { 0, 0, 0, 0, 0x49, 0x49 });
            WriteBox(ms, "xml ", Encoding.UTF8.GetBytes("<xmp/>"));
            WriteBox(ms, "jumb", new byte[] { 10, 20, 30 });
            WriteBox(ms, "brob", Encoding.ASCII.GetBytes("Exif1234"));
            WriteBox(ms, "jhgm", new byte[] { 99, 88 });
            WriteBox64(ms, "cust", new byte[] { 1, 2, 3 });
            WriteUnboundedBox(ms, "tail", new byte[] { 7, 8 });

            return ms.ToArray();
        }

        private static void WriteBox(Stream s, string type, byte[] payload)
        {
            Span<byte> hdr = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), (uint)(8 + payload.Length));
            Encoding.ASCII.GetBytes(type, hdr.Slice(4, 4));
            s.Write(hdr);
            s.Write(payload);
        }

        private static void WriteBox64(Stream s, string type, byte[] payload)
        {
            Span<byte> hdr = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), 1u);
            Encoding.ASCII.GetBytes(type, hdr.Slice(4, 4));
            BinaryPrimitives.WriteUInt64BigEndian(hdr.Slice(8, 8), (ulong)(16 + payload.Length));
            s.Write(hdr);
            s.Write(payload);
        }

        private static void WriteUnboundedBox(Stream s, string type, byte[] payload)
        {
            Span<byte> hdr = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), 0u);
            Encoding.ASCII.GetBytes(type, hdr.Slice(4, 4));
            s.Write(hdr);
            s.Write(payload);
        }
    }
}
