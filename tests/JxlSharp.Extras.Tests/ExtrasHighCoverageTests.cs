using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.TestLib;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Extras
{
    /// <summary>
    /// High-coverage unit tests for <c>JxlSharp.Extras</c> covering color descriptions,
    /// packed images, format conversions, PNM/PGX/NPY/APNG/JXL codecs, facades, and utilities.
    /// </summary>
    [TestFixture]
    public class ExtrasHighCoverageTests
    {

        /// <summary>
        /// Verifies <see cref="ColorDescription"/> parsing and formatting across aliases, color spaces,
        /// white points, primaries, rendering intents, and transfer functions.
        /// </summary>
        [Test]
        public void ColorDescription_ParseAndFormat_CoverAllBranches()
        {
            string[] validSpecs =
            {
                "sRGB",
                "DisplayP3",
                "Adobe98",
                "ProPhoto",
                "Rec2100PQ",
                "Rec2100HLG",
                "Gra_D65_Rel_SRG",
                "XYB_Per",
                "CS?_D65_SRG_Rel_SRG",
                "RGB_D65_SRG_Rel_SRG",
                "RGB_Cst_SRG_Rel_SRG",
                "RGB_EER_SRG_Rel_SRG",
                "RGB_DCI_SRG_Rel_SRG",
                "RGB_D50_SRG_Rel_SRG",
                "RGB_0.3127;0.329_SRG_Rel_SRG",
                "RGB_D65_Cst_Rel_SRG",
                "RGB_D65_202_Rel_SRG",
                "RGB_D65_DCI_Rel_SRG",
                "RGB_D65_Ado_Rel_SRG",
                "RGB_D65_Pro_Rel_SRG",
                "RGB_D65_0.64;0.33;0.3;0.6;0.15;0.06_Rel_SRG",
                "RGB_D65_SRG_Per_709",
                "RGB_D65_SRG_Sat_TF?",
                "RGB_D65_SRG_Abs_Lin",
                "RGB_D65_SRG_Rel_PeQ",
                "RGB_D65_SRG_Rel_DCI",
                "RGB_D65_SRG_Rel_HLG",
                "RGB_D65_SRG_Rel_Ado",
                "RGB_D65_SRG_Rel_Pro",
                "RGB_D65_SRG_Rel_g0.45",
            };

            foreach (string spec in validSpecs)
            {
                ColorDescription.ParseDescription(spec, out JxlColorEncoding c).IsOk.ShouldBeTrue(spec);
                ColorDescription.Description(in c).ShouldNotBeEmpty();
            }

            string[] invalidSpecs =
            {
                "",
                "BadCS_D65_SRG_Rel_SRG",
                "RGB",
                "RGB_BadWP_SRG_Rel_SRG",
                "RGB_0.31;bad_SRG_Rel_SRG",
                "RGB_D65_BadPrim_Rel_SRG",
                "RGB_D65_0.1;0.2;0.3_Rel_SRG",
                "RGB_D65_SRG_BadIntent_SRG",
                "RGB_D65_SRG_Rel_BadTF",
                "RGB_D65_SRG_Rel_gNaN",
                "RGB_D65_SRG_Rel_gBad",
                "RGB_D65_SRG_Rel_SRG_ExtraToken",
                "XYB_D65_SRG_Rel_SRG",
            };

            foreach (string bad in invalidSpecs)
            {
                ColorDescription.ParseDescription(bad, out _).IsOk.ShouldBeFalse(bad);
            }
        }

        /// <summary>
        /// Verifies <see cref="ColorHints"/> and <see cref="ColorHintsHelper"/> on <see cref="PackedPixelFile"/>.
        /// </summary>
        [Test]
        public void ColorHints_And_ApplyColorHints_CoverAllBranches()
        {
            byte[] srgbIcc      = ColorEncoding.SRGB().ICC();
            string rawIccString = Encoding.Latin1.GetString(srgbIcc);

            ColorHints hints = new ColorHints();

            hints.Add("color_space", "RGB_D65_SRG_Rel_Lin");
            hints.Add("exif", "\x01\x02\x03\x04");
            hints.Add("xmp", "<xmp/>");
            hints.Add("jumbf", "jumb");
            hints.Add("unknown_key", "ignored");

            PackedPixelFile ppf = new PackedPixelFile();

            ColorHintsHelper.ApplyColorHints(hints, colorAlreadySet: false, isGray: false, ppf).IsOk.ShouldBeTrue();
            ppf.Metadata.Exif.ShouldNotBeEmpty();
            ppf.Metadata.Xmp.ShouldNotBeEmpty();
            ppf.Metadata.Jumbf.ShouldNotBeEmpty();

            // When colorAlreadySet is true, color_space and icc_pathname are ignored and succeed
            ColorHintsHelper.ApplyColorHints(hints, colorAlreadySet: true, isGray: false, ppf).IsOk.ShouldBeTrue();

            ColorHints iccHints = new ColorHints();

            iccHints.Add("icc", rawIccString);
            ColorHintsHelper.ApplyColorHints(iccHints, colorAlreadySet: false, isGray: false, ppf).IsOk.ShouldBeTrue();
            ppf.Icc.ShouldBe(srgbIcc);

            // Default fallback when colorAlreadySet is false and no color_space/icc hint
            ColorHints emptyHints = new ColorHints();

            ColorHintsHelper.ApplyColorHints(emptyHints, colorAlreadySet: false, isGray: true, ppf).IsOk.ShouldBeTrue();

            // Invalid color_space and unsupported icc_pathname when colorAlreadySet is false
            ColorHints badColorHints = new ColorHints();

            badColorHints.Add("color_space", "invalid_cs");
            ColorHintsHelper.ApplyColorHints(badColorHints, colorAlreadySet: false, isGray: false, ppf).IsOk.ShouldBeFalse();

            ColorHints iccPathHints = new ColorHints();

            iccPathHints.Add("icc_pathname", "some.icc");
            ColorHintsHelper.ApplyColorHints(iccPathHints, colorAlreadySet: false, isGray: false, ppf).IsOk.ShouldBeFalse();
            ColorHintsHelper.ApplyColorHints(iccPathHints, colorAlreadySet: true, isGray: false, ppf).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="PackedImage"/>, <see cref="PackedFrame"/>, <see cref="ChunkedPackedFrame"/>,
        /// <see cref="PackedPixelFile"/>, <see cref="Encoder"/>, <see cref="ExtrasCommon"/>, and <see cref="CodecInOut"/>.
        /// </summary>
        [Test]
        public void PackedImageAndTypes_AllMethodsAndFormats_CoverAllBranches()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();

            PackedImage.ValidateDataType(JxlDataType.UInt8).IsOk.ShouldBeTrue();
            PackedImage.BitsPerChannel(JxlDataType.UInt8).ShouldBe((nuint)8);
            PackedImage.BitsPerChannel(JxlDataType.UInt16).ShouldBe((nuint)16);
            PackedImage.BitsPerChannel(JxlDataType.Float16).ShouldBe((nuint)16);
            PackedImage.BitsPerChannel(JxlDataType.Float).ShouldBe((nuint)32);

            JxlPixelFormat[] formats =
            {
                new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.NativeEndian, Align = 8 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float16, Endianness = JxlEndianness.LittleEndian, Align = 0 },
            };

            foreach (JxlPixelFormat fmt in formats)
            {
                StatusOr<PackedImage> imgRes = PackedImage.Create(5, 4, in fmt);

                imgRes.IsOk.ShouldBeTrue();
                PackedImage img = imgRes.Value!;

                img.SetPixelValue(2, 1, 0, 0.5f);
                float val = img.GetPixelValue(2, 1, 0);

                if (fmt.DataType != JxlDataType.Float16)
                {
                    val.ShouldBe(0.5f, 0.02f);
                }

                StatusOr<PackedImage> copyRes = img.Copy();

                copyRes.IsOk.ShouldBeTrue();
                img.ShrinkTo(3, 2).IsOk.ShouldBeTrue();
                img.ShrinkTo(10, 10).IsOk.ShouldBeFalse();
            }

            StatusOr<PackedFrame> frameRes = PackedFrame.Create(6, 6, in formats[0]);

            frameRes.IsOk.ShouldBeTrue();
            JxlPixelFormat ecFmt          = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8 };
            StatusOr<PackedImage> ecRes   = PackedImage.Create(6, 6, in ecFmt);

            frameRes.Value!.ExtraChannels.Add(ecRes.Value!);
            StatusOr<PackedFrame> frameCopy = frameRes.Value.Copy();

            frameCopy.IsOk.ShouldBeTrue();
            frameCopy.Value!.ShrinkTo(4, 4).IsOk.ShouldBeTrue();
            frameCopy.Value.ShrinkTo(8, 8).IsOk.ShouldBeFalse();

            ChunkedPackedFrame chunked = new ChunkedPackedFrame(4, 4, () => formats[0]);

            chunked.XSize.ShouldBe((nuint)4);

            PackedPixelFile ppf = new PackedPixelFile();

            ppf.Info.XSize            = 6;
            ppf.Info.YSize            = 6;
            ppf.Info.NumColorChannels = 3;
            ppf.Info.AlphaBits        = 8;
            ppf.Info.BitsPerSample    = 8;
            ppf.Frames.Add(frameRes.Value);
            ppf.ShrinkTo(4, 4).IsOk.ShouldBeTrue();

            ExtrasCommon.VerifyDimensions(null, 4, 4).IsOk.ShouldBeTrue();
            ExtrasCommon.SelectFormat(formats, in ppf.Info, out JxlPixelFormat selected).IsOk.ShouldBeTrue();
            selected.NumChannels.ShouldBe(4u);

            Encoder.VerifyBitDepth(JxlDataType.UInt8, 8, 0).IsOk.ShouldBeTrue();
            Encoder.VerifyBitDepth(JxlDataType.UInt8, 16, 0).IsOk.ShouldBeFalse();
            Encoder.VerifyBitDepth(JxlDataType.Float, 32, 8).IsOk.ShouldBeTrue();
            Encoder.VerifyBitDepth(JxlDataType.Float16, 32, 8).IsOk.ShouldBeFalse();

            CodecInOut io = new CodecInOut(mm);

            io.SetSize(4, 4);
            Image3<float> img3f = CreateTestImage3F(mm, 4, 4, 0.25f);

            io.SetFromImage(img3f, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            io.CheckMetadata().IsOk.ShouldBeTrue();
            io.ShrinkTo(2, 2);
        }

        /// <summary>
        /// Verifies <see cref="PackedImageConvert"/> conversions between <see cref="Image3{T}"/>,
        /// <see cref="PackedPixelFile"/>, and <see cref="CodecInOut"/> across all data types and bit depths.
        /// </summary>
        [Test]
        public void PackedImageConvert_AllDataTypesAndChannels_RoundtripAndValidate()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            using Image3<float> img = CreateTestImage3F(mm, 4, 4, 0.25f);

            JxlDataType[] types = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float16, JxlDataType.Float };

            foreach (JxlDataType dt in types)
            {
                foreach (uint ch in new[] { 1u, 3u })
                {
                    JxlPixelFormat fmt = new JxlPixelFormat
                    {
                        NumChannels = ch,
                        DataType    = dt,
                        Endianness  = JxlEndianness.LittleEndian,
                    };

                    ColorEncoding cEnc                  = ColorEncoding.SRGB(isGray: ch == 1);
                    StatusOr<PackedPixelFile> ppfResult = PackedImageConvert.ConvertImage3FToPackedPixelFile(img, cEnc, fmt, null);

                    ppfResult.IsOk.ShouldBeTrue();
                    PackedPixelFile ppf = ppfResult.Value!;

                    CodecInOut io = new CodecInOut(mm);

                    PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeTrue();

                    PackedPixelFile ppfBack = new PackedPixelFile();

                    PackedImageConvert.ConvertCodecInOutToPackedPixelFile(
                        io,
                        in fmt,
                        cEnc,
                        null,
                        ppfBack).IsOk.ShouldBeTrue();
                }
            }

            VerifyRichPpfConversion(mm);
        }

        private static void VerifyRichPpfConversion(JxlMemoryManager mm)
        {
            PackedPixelFile richPpf = new PackedPixelFile();

            richPpf.Info.XSize                 = 4;
            richPpf.Info.YSize                 = 4;
            richPpf.Info.BitsPerSample         = 8;
            richPpf.Info.NumColorChannels      = 3;
            richPpf.Info.AlphaBits             = 8;
            richPpf.Info.HavePreview           = true;
            richPpf.Info.Preview               = new JxlPreviewHeader { XSize = 2, YSize = 2 };
            richPpf.Info.HaveAnimation         = true;
            richPpf.Info.Animation             = new JxlAnimationHeader { TpsNumerator = 10, TpsDenominator = 1 };
            richPpf.Info.IntensityTarget       = 300f;
            richPpf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            richPpf.Icc                        = ColorEncoding.SRGB().ICC();
            richPpf.ExtraChannelsInfo.Add(new PackedExtraChannel
            {
                Index  = 0,
                Name   = "spot",
                EcInfo = new JxlExtraChannelInfo
                {
                    Type          = JxlExtraChannelType.SpotColor,
                    BitsPerSample = 8,
                },
            });

            JxlPixelFormat rgbaFmt = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8 };
            JxlPixelFormat ecFmt   = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8 };

            richPpf.PreviewFrame  = PackedFrame.Create(2, 2, in rgbaFmt).Value;
            PackedFrame mainFrame = PackedFrame.Create(4, 4, in rgbaFmt).Value!;

            mainFrame.Name      = "f0";
            mainFrame.FrameInfo = new JxlFrameHeader { Duration = 5, IsLast = true };
            mainFrame.ExtraChannels.Add(PackedImage.Create(4, 4, in ecFmt).Value!);
            richPpf.Frames.Add(mainFrame);

            CodecInOut richIo = new CodecInOut(mm);

            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(richPpf, null, richIo).IsOk.ShouldBeTrue();

            PackedPixelFile richPpfBack = new PackedPixelFile();

            PackedImageConvert.ConvertCodecInOutToPackedPixelFile(
                richIo,
                in rgbaFmt,
                ColorEncoding.SRGB(),
                null,
                richPpfBack).IsOk.ShouldBeTrue();

            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertPackedPixelFileToCodecInOut(null!, null, richIo));
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(new PackedPixelFile(), null, richIo).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="CompressedIcc"/>, <see cref="GainMapCodec"/>, <see cref="ExtrasAlphaBlend"/>,
        /// <see cref="ExtrasExif"/>, <see cref="ExtrasHlg"/>, <see cref="ExtrasMetrics"/>, and <see cref="ExtrasToneMapping"/>.
        /// </summary>
        [Test]
        public void ExtrasUtilities_AllClassesAndEdgeCases_CoverAllBranches()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            byte[] srgbIcc      = ColorEncoding.SRGB().ICC();

            // 1. CompressedIcc
            CompressedIcc.JxlICCProfileEncode(mm, srgbIcc, out byte[] encodedIcc).ShouldBeTrue();
            CompressedIcc.JxlICCProfileDecode(mm, encodedIcc, out byte[] decodedIcc).ShouldBeTrue();
            decodedIcc.ShouldBe(srgbIcc);
            CompressedIcc.JxlICCProfileEncode(mm, ReadOnlySpan<byte>.Empty, out _).ShouldBeFalse();
            CompressedIcc.JxlICCProfileDecode(mm, ReadOnlySpan<byte>.Empty, out _).ShouldBeFalse();

            // 2. GainMapCodec
            JxlGainMapBundle bundle = new JxlGainMapBundle
            {
                JhgmVersion      = 0,
                GainMapMetadata  = new byte[] { 10, 20, 30 },
                HasColorEncoding = true,
                ColorEncoding    = ColorEncoding.SRGB().ToExternal(),
                AltIcc           = srgbIcc,
                GainMap          = new byte[] { 1, 2, 3, 4, 5 },
            };

            GainMapCodec.JxlGainMapGetBundleSize(in bundle, out nuint bundleSize).ShouldBeTrue();
            byte[] bundleBuf = new byte[bundleSize];

            GainMapCodec.JxlGainMapWriteBundle(in bundle, bundleBuf, out nuint bytesWritten).ShouldBeTrue();
            bytesWritten.ShouldBe(bundleSize);
            GainMapCodec.JxlGainMapReadBundle(out JxlGainMapBundle parsedBundle, bundleBuf, out nuint bytesRead).ShouldBeTrue();
            bytesRead.ShouldBe(bundleSize);
            parsedBundle.HasColorEncoding.ShouldBeTrue();
            parsedBundle.GainMap.ShouldBe(bundle.GainMap);

            GainMapCodec.JxlGainMapReadBundle(out _, ReadOnlySpan<byte>.Empty, out _).ShouldBeFalse();
            GainMapCodec.JxlGainMapReadBundle(out _, new byte[] { 99, 0, 0 }, out _).ShouldBeFalse();

            // 3. ExtrasAlphaBlend, 4. ExtrasExif, 5. HLG/Metrics/ToneMapping
            VerifyAlphaBlendAndExif();
            VerifyHlgMetricsAndToneMapping(mm);
        }

        private static void VerifyAlphaBlendAndExif()
        {
            ExtrasAlphaBlend.AlphaBlend(null!, new float[] { 0, 0, 0 }).IsOk.ShouldBeTrue();

            foreach (JxlDataType dt in new[] { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float })
            {
                foreach (uint ch in new[] { 2u, 4u })
                {
                    PackedPixelFile ppf = new PackedPixelFile();

                    ppf.Info.XSize            = 4;
                    ppf.Info.YSize            = 4;
                    ppf.Info.NumColorChannels = ch - 1;
                    ppf.Info.AlphaBits        = dt == JxlDataType.UInt8 ? 8u : (dt == JxlDataType.UInt16 ? 16u : 32u);
                    ppf.Info.BitsPerSample    = ppf.Info.AlphaBits;
                    ppf.Info.HavePreview      = true;
                    ppf.Info.Preview          = new JxlPreviewHeader { XSize = 2, YSize = 2 };

                    JxlPixelFormat fmt = new JxlPixelFormat { NumChannels = ch, DataType = dt };

                    ppf.PreviewFrame = PackedFrame.Create(2, 2, in fmt).Value;
                    ppf.Frames.Add(PackedFrame.Create(4, 4, in fmt).Value!);

                    ExtrasAlphaBlend.AlphaBlend(ppf, new float[] { 0.5f, 0.5f, 0.5f }).IsOk.ShouldBeTrue();
                    ppf.Info.AlphaBits.ShouldBe(0u);
                }
            }

            // Little-endian II and big-endian MM TIFF with Orientation tag 274 at offset 8
            byte[] exifLe = new byte[32];

            exifLe[0] = (byte)'I';
            exifLe[1] = (byte)'I';
            BinaryPrimitives.WriteUInt16LittleEndian(exifLe.AsSpan(2, 2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(exifLe.AsSpan(4, 4), 8u);
            BinaryPrimitives.WriteUInt16LittleEndian(exifLe.AsSpan(8, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(exifLe.AsSpan(10, 2), 274);
            BinaryPrimitives.WriteUInt16LittleEndian(exifLe.AsSpan(12, 2), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(exifLe.AsSpan(14, 4), 1u);
            BinaryPrimitives.WriteUInt16LittleEndian(exifLe.AsSpan(18, 2), 6);
            ExtrasExif.ResetExifOrientation(exifLe);
            BinaryPrimitives.ReadUInt16LittleEndian(exifLe.AsSpan(18, 2)).ShouldBe((ushort)1);

            byte[] exifBe = new byte[32];

            exifBe[0] = (byte)'M';
            exifBe[1] = (byte)'M';
            BinaryPrimitives.WriteUInt16BigEndian(exifBe.AsSpan(2, 2), 42);
            BinaryPrimitives.WriteUInt32BigEndian(exifBe.AsSpan(4, 4), 8u);
            BinaryPrimitives.WriteUInt16BigEndian(exifBe.AsSpan(8, 2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(exifBe.AsSpan(10, 2), 274);
            BinaryPrimitives.WriteUInt16BigEndian(exifBe.AsSpan(12, 2), 3);
            BinaryPrimitives.WriteUInt32BigEndian(exifBe.AsSpan(14, 4), 1u);
            BinaryPrimitives.WriteUInt16BigEndian(exifBe.AsSpan(18, 2), 6);
            ExtrasExif.ResetExifOrientation(exifBe);
            BinaryPrimitives.ReadUInt16BigEndian(exifBe.AsSpan(18, 2)).ShouldBe((ushort)1);
        }

        private static void VerifyHlgMetricsAndToneMapping(JxlMemoryManager mm)
        {
            ExtrasHlg.GetHlgGamma(1000f, 5f).ShouldBeGreaterThan(1.0f);

            ImageMetadata meta = new ImageMetadata();

            meta.SetFloat32Samples();
            meta.SetIntensityTarget(1000f);
            ColorEncoding linSrgb = ColorEncoding.LinearSRGB();

            meta.ColorEncoding = linSrgb;
            ImageBundle ib1    = new ImageBundle(mm, meta);
            ImageBundle ib2    = new ImageBundle(mm, meta);

            ib1.SetFromImage(CreateTestImage3F(mm, 4, 4, 0.5f), linSrgb).IsOk.ShouldBeTrue();
            ib2.SetFromImage(CreateTestImage3F(mm, 4, 4, 0.5f), linSrgb).IsOk.ShouldBeTrue();

            ExtrasHlg.HlgOOTF(ib1, 1.2f).IsOk.ShouldBeTrue();
            ExtrasHlg.HlgInverseOOTF(ib1, 1.2f).IsOk.ShouldBeTrue();

            using ImagePlane<float> distPlane = ImagePlane<float>.Create(mm, 4, 4).Value!;

            ImageOps.ZeroFillImage(distPlane);
            distPlane.Row(0)[0]        = 0.5f;
            ButteraugliParams baParams = new ButteraugliParams();

            ExtrasMetrics.ComputeDistanceP(distPlane, in baParams, 3.0).Value.ShouldBeGreaterThan(0.0);
            ExtrasMetrics.ComputeDistanceP(distPlane, in baParams, 2.0).Value.ShouldBeGreaterThan(0.0);
            ExtrasMetrics.ComputeDistance2(ib1, ib2, JxlCms.GetDefaultCms()).ShouldBeGreaterThanOrEqualTo(0.0);
            ExtrasMetrics.ComputePSNR(ib1, ib2, JxlCms.GetDefaultCms()).ShouldBeGreaterThanOrEqualTo(0.0);

            ib2.Color().PlaneRow(0, 0)[0] = 0.9f;
            ExtrasMetrics.ComputePSNR(ib1, ib2, JxlCms.GetDefaultCms()).ShouldBeGreaterThan(0.0);

            CodecInOut tmIo = new CodecInOut(mm);

            tmIo.Metadata.M = meta;
            tmIo.Frames.Add(ib1);
            ExtrasToneMapping.ToneMapTo(new ExtrasToneMapping.LuminanceRange(0.1f, 300f), tmIo).IsOk.ShouldBeTrue();
            ExtrasToneMapping.GamutMap(tmIo, 0.5f).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="CodecPnm"/>, <see cref="CodecPgx"/>, and <see cref="CodecNpy"/> decoding and encoding.
        /// </summary>
        [Test]
        public void CodecPnmPgxAndNpy_AllVariantsAndEncoders_CoverAllBranches()
        {
            ColorHints emptyHints = new ColorHints();

            // P1 ASCII should fail in binary decoder
            CodecPnm.DecodeImagePNM(Encoding.ASCII.GetBytes("P1\n2 2\n0 1 1 0\n"), emptyHints, new PackedPixelFile()).IsOk.ShouldBeFalse();

            // P4 PBM binary
            byte[] pbm             = ConcatAsciiAndBytes("P4\n# comment\n2 2\n", new byte[] { 0x80, 0x40, 0x20, 0x10 });
            PackedPixelFile ppfPbm = new PackedPixelFile();

            CodecPnm.DecodeImagePNM(pbm, emptyHints, ppfPbm).IsOk.ShouldBeTrue();

            // P5 PGM 8-bit and 16-bit
            byte[] pgm8              = ConcatAsciiAndBytes("P5\n# comment\n2 2\n255\n", new byte[] { 10, 20, 30, 40 });
            byte[] pgm16             = ConcatAsciiAndBytes("P5\n2 2\n65535\n", new byte[] { 0, 10, 0, 20, 0, 30, 0, 40 });
            PackedPixelFile ppfPgm8  = new PackedPixelFile();
            PackedPixelFile ppfPgm16 = new PackedPixelFile();

            CodecPnm.DecodeImagePNM(pgm8, emptyHints, ppfPgm8).IsOk.ShouldBeTrue();
            CodecPnm.DecodeImagePNM(pgm16, emptyHints, ppfPgm16).IsOk.ShouldBeTrue();

            // P6 PPM 8-bit
            byte[] ppm8            = ConcatAsciiAndBytes("P6\n2 1\n255\n", new byte[] { 255, 0, 0, 0, 255, 0 });
            PackedPixelFile ppfPpm = new PackedPixelFile();

            CodecPnm.DecodeImagePNM(ppm8, emptyHints, ppfPpm).IsOk.ShouldBeTrue();

            // P7 PAM with various TUPLTYPEs and extra channels
            string[] pamHeaders =
            {
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 1\nTUPLTYPE BLACKANDWHITE\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 1\nTUPLTYPE BLACKANDWHITE_ALPHA\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 255\nTUPLTYPE GRAYSCALE\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nTUPLTYPE RGB\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 5\nMAXVAL 255\nTUPLTYPE RGB\nTUPLTYPE DEPTH\nTUPLTYPE SPOT_COLOR\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nENDHDR\n",
            };

            foreach (string hdr in pamHeaders)
            {
                byte[] pamBytes        = ConcatAsciiAndBytes(hdr, new byte[16]);
                PackedPixelFile ppfPam = new PackedPixelFile();

                CodecPnm.DecodeImagePNM(pamBytes, emptyHints, ppfPam).IsOk.ShouldBeTrue(hdr);
            }

            // PFM 3-channel (PF) positive and negative scale, and 1-channel (Pf)
            byte[] pfmRgbLe        = ConcatAsciiAndBytes("PF\n2 1\n-1.0\n", new byte[24]);
            byte[] pfmRgbBe        = ConcatAsciiAndBytes("PF\n2 1\n1.0\n", new byte[24]);
            byte[] pfmGrayLe       = ConcatAsciiAndBytes("Pf\n2 1\n-1.0\n", new byte[8]);
            PackedPixelFile ppfPfm = new PackedPixelFile();

            CodecPnm.DecodeImagePNM(pfmRgbLe, emptyHints, ppfPfm).IsOk.ShouldBeTrue();
            CodecPnm.DecodeImagePNM(pfmRgbBe, emptyHints, ppfPfm).IsOk.ShouldBeTrue();
            CodecPnm.DecodeImagePNM(pfmGrayLe, emptyHints, ppfPfm).IsOk.ShouldBeTrue();

            // Encoders: PAM, PGM, PNM, PPM, PFM
            EncodedImage encImg = new EncodedImage();

            CodecPnm.GetPAMEncoder().Encode(ppfPpm, encImg).IsOk.ShouldBeTrue();
            CodecPnm.GetPGMEncoder().Encode(ppfPgm8, encImg).IsOk.ShouldBeTrue();
            CodecPnm.GetPNMEncoder().Encode(ppfPpm, encImg).IsOk.ShouldBeTrue();
            CodecPnm.GetPPMEncoder().Encode(ppfPpm, encImg).IsOk.ShouldBeTrue();
            CodecPnm.GetPFMEncoder().Encode(ppfPfm, encImg).IsOk.ShouldBeTrue();

            // PGX decode & encode
            byte[] pgx8            = ConcatAsciiAndBytes("PG ML + 8 2 2\n", new byte[] { 1, 2, 3, 4 });
            byte[] pgx16           = ConcatAsciiAndBytes("PG LM 16 2 2\r\n", new byte[] { 1, 0, 2, 0, 3, 0, 4, 0 });
            byte[] pgxNeg          = ConcatAsciiAndBytes("PG ML - 8 2 2\n", new byte[] { 1, 2, 3, 4 });
            PackedPixelFile ppfPgx = new PackedPixelFile();

            CodecPgx.DecodeImagePGX(pgx8, emptyHints, ppfPgx).IsOk.ShouldBeTrue();
            CodecPgx.DecodeImagePGX(pgx16, emptyHints, ppfPgx).IsOk.ShouldBeTrue();
            CodecPgx.DecodeImagePGX(pgxNeg, emptyHints, ppfPgx).IsOk.ShouldBeFalse();

            // Re-decode big-endian 8-bit PGX for PGX encoder (which accepts BigEndian)
            CodecPgx.DecodeImagePGX(pgx8, emptyHints, ppfPgx).IsOk.ShouldBeTrue();
            CodecPgx.GetPGXEncoder().Encode(ppfPgx, encImg).IsOk.ShouldBeTrue();

            // NumPy encode
            CodecNpy.GetNumPyEncoder().Encode(ppfPfm, encImg).IsOk.ShouldBeTrue();
            encImg.Bitstreams.ShouldNotBeEmpty();
            CodecNpy.GetNumPyEncoder().Encode(new PackedPixelFile(), encImg).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="CodecApng"/> encoding and decoding across color types, bit depths,
        /// interlace modes, scanline filters, and metadata chunks.
        /// </summary>
        [Test]
        public void CodecApng_EncodeAndDecodeAllColorTypesFiltersAndChunks_Succeed()
        {
            CodecApng.CanDecodeAPNG().ShouldBeTrue();
            Encoder apngEnc    = CodecApng.GetAPNGEncoder()!;
            ColorHints noHints = new ColorHints();

            // Encode & decode via GetAPNGEncoder across UInt8, UInt16, Float, and channel counts 1..4
            foreach (JxlDataType dt in new[] { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float })
            {
                foreach (uint ch in new[] { 1u, 2u, 3u, 4u })
                {
                    PackedPixelFile ppf = new PackedPixelFile();

                    ppf.Info.XSize                 = 4;
                    ppf.Info.YSize                 = 4;
                    ppf.Info.NumColorChannels      = (ch == 1 || ch == 2) ? 1u : 3u;
                    ppf.Info.BitsPerSample         = dt == JxlDataType.UInt8 ? 8u : (dt == JxlDataType.UInt16 ? 16u : 32u);
                    ppf.Info.AlphaBits             = (ch == 2 || ch == 4) ? ppf.Info.BitsPerSample : 0u;
                    ppf.Info.ExponentBitsPerSample = dt == JxlDataType.Float ? 8u : 0u;
                    ppf.ColorEncoding              = ColorEncoding.SRGB(isGray: ppf.Info.NumColorChannels == 1).ToExternal();
                    ppf.Metadata.Exif              = new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'I', 42, 0 };

                    JxlPixelFormat fmt = new JxlPixelFormat
                    {
                        NumChannels = ch,
                        DataType    = dt,
                        Endianness  = JxlEndianness.BigEndian,
                    };

                    ppf.Frames.Add(PackedFrame.Create(4, 4, in fmt).Value!);

                    EncodedImage encoded = new EncodedImage();

                    apngEnc.Encode(ppf, encoded).IsOk.ShouldBeTrue();

                    PackedPixelFile decoded = new PackedPixelFile();

                    CodecApng.DecodeImageAPNG(encoded.Bitstreams[0], noHints, decoded).IsOk.ShouldBeTrue();
                }
            }

            // Custom PNGs testing Adam7 interlace, Palette (PLTE + tRNS), Filters 0..4, cICP, gAMA, cHRM
            byte[] palPng          = BuildCustomPng(4, 4, 8, 3, 0, true, new byte[] { 1, 13, 0, 1 });
            PackedPixelFile palPpf = new PackedPixelFile();

            CodecApng.DecodeImageAPNG(palPng, noHints, palPpf).IsOk.ShouldBeTrue();

            byte[] adam7Png          = BuildCustomPng(5, 5, 8, 2, 1, false, new byte[] { 9, 16, 0, 1 });
            PackedPixelFile adam7Ppf = new PackedPixelFile();

            CodecApng.DecodeImageAPNG(adam7Png, noHints, adam7Ppf).IsOk.ShouldBeTrue();

            // Sub-8-bit grayscale PNG (4-bit) with tRNS and gAMA/cHRM
            byte[] gray4Png          = BuildCustomPng(4, 4, 4, 0, 0, true, null);
            PackedPixelFile gray4Ppf = new PackedPixelFile();

            CodecApng.DecodeImageAPNG(gray4Png, noHints, gray4Ppf).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="CodecJxl"/>, <see cref="CodecJpg"/>, <see cref="CodecGif"/>, <see cref="CodecExr"/>,
        /// <see cref="ExtrasCodec"/>, <see cref="ExtrasDecodeFacade"/>, and <see cref="ExtrasEncodeFacade"/>.
        /// </summary>
        [Test]
        public void CodecJxlExternalAndFacades_AllEntryPoints_CoverAllBranches()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();

            // External codec stubs
            CodecJpg.CanDecodeJPG().ShouldBeFalse();
            CodecJpg.DecodeImageJPG(new byte[] { 1, 2 }, new ColorHints(), new PackedPixelFile()).IsOk.ShouldBeFalse();
            CodecJpg.GetJPEGEncoder().ShouldBeNull();

            CodecGif.CanDecodeGIF().ShouldBeFalse();
            CodecGif.DecodeImageGIF(new byte[] { 1, 2 }, new ColorHints(), new PackedPixelFile()).IsOk.ShouldBeFalse();

            CodecExr.CanDecodeEXR().ShouldBeFalse();
            CodecExr.DecodeImageEXR(new byte[] { 1, 2 }, new ColorHints(), new PackedPixelFile()).IsOk.ShouldBeFalse();
            CodecExr.GetEXREncoder().ShouldBeNull();

            // CodecFromPath & ExtensionFromCodec & CanDecode
            ExtrasDecodeFacade.ListOfDecodeCodecs().ShouldNotBeEmpty();
            ExtrasEncodeFacade.ListOfEncodeCodecs().ShouldNotBeEmpty();

            string[] extensions = { ".ppm", ".pgm", ".pbm", ".pam", ".pfm", ".pgx", ".png", ".jxl", ".jpg", ".gif", ".exr", ".npy", ".unknown" };

            foreach (string ext in extensions)
            {
                nuint bps     = 8;
                string extOut = ext;
                Codec c       = ExtrasDecodeFacade.CodecFromPath("file" + ext, ref bps, ref extOut);

                ExtrasDecodeFacade.CanDecode(c);
                ExtrasDecodeFacade.CodecFromPath("file" + ext).ShouldBe(c);
                ExtrasEncodeFacade.FromExtension(ext);
            }

            // Rich CodecJxl encode & decode + Facades
            PackedPixelFile ppf = new PackedPixelFile();

            ppf.Info.XSize                 = 8;
            ppf.Info.YSize                 = 8;
            ppf.Info.BitsPerSample         = 8;
            ppf.Info.NumColorChannels      = 3;
            ppf.Info.AlphaBits             = 8;
            ppf.Info.Orientation           = JxlOrientation.Rotate90Cw;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            ppf.Icc                        = ColorEncoding.SRGB().ICC();
            ppf.Metadata.Exif              = new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 0, 0 };
            ppf.Metadata.Xmp               = new byte[] { (byte)'<', (byte)'x', (byte)'/', (byte)'>' };
            ppf.Metadata.Jumbf             = new byte[] { 1, 2, 3, 4 };
            ppf.Metadata.Iptc              = new byte[] { 5, 6, 7, 8 };

            JxlPixelFormat fmt = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8 };

            ppf.Frames.Add(PackedFrame.Create(8, 8, in fmt).Value!);

            JXLCompressParams cparams = new JXLCompressParams
            {
                Distance           = 1.0f,
                AlphaDistance      = 1.0f,
                UseContainer       = true,
                CompressBoxes      = false,
                CodestreamLevel    = 5,
                UpsamplingMode     = 1,
                IntensityTarget    = 250f,
                OverrideBitdepth   = 8,
                Premultiply        = -1,
                AllowExpertOptions = true,
            };

            cparams.AddOption(JxlEncoderFrameSettingId.Effort, 3);
            cparams.AddFloatOption(JxlEncoderFrameSettingId.PhotonNoise, 50f);

            CodecJxl.EncodeImageJXL(cparams, ppf, null, out byte[] jxlBytes).ShouldBeTrue();

            JXLDecompressParams dparams = new JXLDecompressParams
            {
                DisplayNits      = 250f,
                KeepOrientation  = false,
                UseImageCallback = false,
                ColorSpace       = "RGB_D65_SRG_Rel_SRG",
            };

            PackedPixelFile decodedPpf = new PackedPixelFile();

            CodecJxl.DecodeImageJXL(jxlBytes, dparams, out nuint decBytes, decodedPpf).ShouldBeTrue();
            decBytes.ShouldBeGreaterThan((nuint)0);

            // ExtrasCodec & ExtrasDecodeFacade
            ppf.Info.Orientation = JxlOrientation.Identity;
            ExtrasCodec.Encode(ppf, Codec.Png, out byte[] pngBytes).IsOk.ShouldBeTrue();
            PackedPixelFile fromFacade = new PackedPixelFile();

            ExtrasDecodeFacade.DecodeBytes(pngBytes, new ColorHints(), fromFacade, null, out Codec detected).IsOk.ShouldBeTrue();
            detected.ShouldBe(Codec.Png);
            fromFacade.Info.XSize.ShouldBe(8u);

            CodecInOut io = new CodecInOut(mm);

            ExtrasCodec.SetFromBytes(pngBytes, new ColorHints(), io).IsOk.ShouldBeTrue();
            ExtrasCodec.Encode(io, "out.ppm", out byte[] ppmBytes).IsOk.ShouldBeTrue();
            ppmBytes.ShouldNotBeEmpty();
        }

        private static Image3<float> CreateTestImage3F(JxlMemoryManager mm, nuint xsize, nuint ysize, float scale)
        {
            Image3<float> img = Image3<float>.Create(mm, xsize, ysize).Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < ysize; y++)
                {
                    Span<float> row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < xsize; x++)
                    {
                        row[(int)x] = scale * (x + 1) / xsize;
                    }
                }
            }

            return img;
        }

        private static byte[] ConcatAsciiAndBytes(string asciiHeader, byte[] payload)
        {
            byte[] hdr    = Encoding.ASCII.GetBytes(asciiHeader);
            byte[] result = new byte[hdr.Length + payload.Length];

            Buffer.BlockCopy(hdr, 0, result, 0, hdr.Length);
            Buffer.BlockCopy(payload, 0, result, hdr.Length, payload.Length);

            return result;
        }

        private static byte[] BuildCustomPng(
            uint width,
            uint height,
            byte bitDepth,
            byte colorType,
            byte interlace,
            bool includePaletteAndTrns,
            byte[]? cicp)
        {
            using MemoryStream ms = new MemoryStream();

            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            byte[] ihdr = new byte[13];

            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), width);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), height);
            ihdr[8]  = bitDepth;
            ihdr[9]  = colorType;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = interlace;
            WritePngChunk(ms, "IHDR", ihdr);

            WriteColorAndPaletteChunks(ms, colorType, includePaletteAndTrns, cicp);
            byte[] idat = BuildIdatPayload(width, height, bitDepth, colorType, interlace);

            WritePngChunk(ms, "IDAT", idat);
            WritePngChunk(ms, "IEND", Array.Empty<byte>());

            return ms.ToArray();
        }

        private static void WriteColorAndPaletteChunks(
            MemoryStream ms,
            byte colorType,
            bool includePaletteAndTrns,
            byte[]? cicp)
        {
            if (cicp != null)
            {
                WritePngChunk(ms, "cICP", cicp);
            }
            else
            {
                byte[] gama = new byte[4];

                BinaryPrimitives.WriteUInt32BigEndian(gama, 45455u);
                WritePngChunk(ms, "gAMA", gama);

                byte[] chrm = new byte[32];

                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(0, 4), 31270u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(4, 4), 32900u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(8, 4), 64000u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(12, 4), 33000u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(16, 4), 30000u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(20, 4), 60000u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(24, 4), 15000u);
                BinaryPrimitives.WriteUInt32BigEndian(chrm.AsSpan(28, 4), 6000u);
                WritePngChunk(ms, "cHRM", chrm);
            }

            if (!includePaletteAndTrns)
            {
                return;
            }

            if (colorType == 3)
            {
                WritePngChunk(ms, "PLTE", new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 });
                WritePngChunk(ms, "tRNS", new byte[] { 128, 255 });
            }
            else if (colorType == 0)
            {
                WritePngChunk(ms, "tRNS", new byte[] { 0, 1 });
            }
        }

        private static byte[] BuildIdatPayload(
            uint width,
            uint height,
            byte bitDepth,
            byte colorType,
            byte interlace)
        {
            using MemoryStream rawScanlines = new MemoryStream();

            if (interlace == 0)
            {
                int channels = colorType == 2 ? 3 : 1;
                int rowBits  = (int)width * channels * bitDepth;
                int rowBytes = (rowBits + 7) / 8;

                for (int y = 0; y < height; y++)
                {
                    rawScanlines.WriteByte((byte)(y % 5));
                    rawScanlines.Write(new byte[rowBytes]);
                }
            }
            else
            {
                int[] passW = { 1, 1, 2, 2, 3, 2, 5 };
                int[] passH = { 1, 1, 1, 2, 2, 3, 2 };

                for (int p = 0; p < 7; p++)
                {
                    for (int y = 0; y < passH[p]; y++)
                    {
                        rawScanlines.WriteByte((byte)(y % 5));
                        rawScanlines.Write(new byte[passW[p] * 3]);
                    }
                }
            }

            using MemoryStream zlibStream = new MemoryStream();

            using (ZLibStream z = new ZLibStream(zlibStream, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] raw = rawScanlines.ToArray();

                z.Write(raw, 0, raw.Length);
            }

            return zlibStream.ToArray();
        }

        private static void WritePngChunk(Stream s, string type, byte[] data)
        {
            Span<byte> lenAndType = stackalloc byte[8];

            BinaryPrimitives.WriteUInt32BigEndian(lenAndType.Slice(0, 4), (uint)data.Length);
            Encoding.ASCII.GetBytes(type, lenAndType.Slice(4, 4));
            s.Write(lenAndType);
            s.Write(data);

            uint crc = 0xFFFFFFFFu;

            for (int i = 4; i < 8; i++)
            {
                crc = UpdateCrc(crc, lenAndType[i]);
            }

            for (int i = 0; i < data.Length; i++)
            {
                crc = UpdateCrc(crc, data[i]);
            }

            Span<byte> crcBytes = stackalloc byte[4];

            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFFu);
            s.Write(crcBytes);
        }

        private static uint UpdateCrc(uint crc, byte b)
        {
            crc ^= b;

            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            return crc;
        }
    }
}
