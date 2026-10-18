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
    /// Additional high-coverage unit tests for <c>JxlSharp.Extras</c> targeting remaining branches
    /// in <see cref="ColorDescription"/>, <see cref="ColorHintsHelper"/>, <see cref="PackedImage"/>,
    /// <see cref="PackedImageConvert"/>, <see cref="ExtrasCodec"/>, <see cref="CodecPnm"/>,
    /// <see cref="CodecPgx"/>, <see cref="CodecNpy"/>, <see cref="CodecApng"/>, <see cref="CodecJxl"/>,
    /// and <c>ExtrasUtilities</c>.
    /// </summary>
    [TestFixture]
    public unsafe class ExtrasAdditionalCoverageTests
    {
        /// <summary>
        /// Covers remaining branches in <see cref="ColorDescription"/> and <see cref="ColorHintsHelper"/>.
        /// </summary>
        [Test]
        public void ColorDescriptionAndHints_RemainingBranches_ExecuteCleanly()
        {
            Should.Throw<ArgumentNullException>(() => ColorDescription.Description((ColorEncoding)null!));
            ColorDescription.Description(ColorEncoding.SRGB()).ShouldNotBeEmpty();

            string[] moreValid =
            {
                "RGB_D65_210_Rel_SRG",
                "RGB_D65_P3_Rel_SRG",
                "RGB_D65_SRG_Rel_",
            };

            foreach (string s in moreValid)
            {
                ColorDescription.ParseDescription(s, out _).IsOk.ShouldBeTrue(s);
            }

            string[] moreInvalid =
            {
                "RGB_D65",
                "RGB_D65_0.1;0.2;0.3;0.4;0.5;0.6;0.7_Rel_SRG",
                "RGB_D65_SRG",
                "RGB_D65_SRG_Rel",
                "RGB_;0.3_SRG_Rel_SRG",
            };

            foreach (string bad in moreInvalid)
            {
                ColorDescription.ParseDescription(bad, out _).IsOk.ShouldBeFalse(bad);
            }

            PackedPixelFile ppf = new PackedPixelFile();

            Should.Throw<ArgumentNullException>(() => ColorHintsHelper.ApplyColorHints(null!, false, false, ppf));
            Should.Throw<ArgumentNullException>(() => ColorHintsHelper.ApplyColorHints(new ColorHints(), false, false, null!));

            ColorHints dupHints = new ColorHints();

            dupHints.Add("color_space", "sRGB");
            dupHints.Add("color_space", "DisplayP3");
            ColorHintsHelper.ApplyColorHints(dupHints, false, false, ppf).IsOk.ShouldBeFalse();

            ColorHints emptyValueHints = new ColorHints();

            emptyValueHints.Add("exif", string.Empty);
            ColorHintsHelper.ApplyColorHints(emptyValueHints, false, false, ppf).IsOk.ShouldBeTrue();
            ppf.Metadata.Exif.ShouldBeEmpty();
        }

        /// <summary>
        /// Covers remaining branches in <see cref="PackedImage"/>, <see cref="PackedFrame"/>,
        /// <see cref="ChunkedPackedFrame"/>, <see cref="PackedPixelFile"/>, <see cref="ExtrasCommon"/>,
        /// <see cref="ExtrasTime"/>, <see cref="CodecInOut"/>, <see cref="Encoder"/>, and <see cref="JXLCompressParams"/>.
        /// </summary>
        [Test]
        public void PackedImageAndTypes_RemainingBranches_ExecuteCleanly()
        {
            PackedImage.ValidateDataType((JxlDataType)99).IsOk.ShouldBeFalse();
            PackedImage.BitsPerChannel((JxlDataType)99).ShouldBe((nuint)0);

            JxlPixelFormat alignedFmt = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 16,
            };

            PackedImage alignedImg = PackedImage.Create(3, 2, in alignedFmt).Value!;

            alignedImg.Stride.ShouldBe((nuint)16);
            alignedImg.Copy().IsOk.ShouldBeTrue();

            JxlPixelFormat f16Fmt = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float16 };
            PackedImage f16Img    = PackedImage.Create(2, 2, in f16Fmt).Value!;

            f16Img.GetPixelValue(0, 0, 0).ShouldBe(0.0f);

            // Overflow in CalcStride / Create
            PackedImage.Create(nuint.MaxValue, 2, in alignedFmt).IsOk.ShouldBeFalse();
            PackedFrame.Create(nuint.MaxValue, 2, in alignedFmt).IsOk.ShouldBeFalse();

            ChunkedPackedFrame chunked = new ChunkedPackedFrame(4, 4, () => alignedFmt);

            chunked.YSize.ShouldBe((nuint)4);
            chunked.Format.NumChannels.ShouldBe(3u);

            PackedPixelFile ppf = new PackedPixelFile();

            ppf.ChunkedFrames.Add(chunked);
            ppf.NumFrames.ShouldBe((nuint)1);

            SizeConstraints smallLimit = new SizeConstraints { DecMaxXSize = 4, DecMaxYSize = 4, DecMaxPixels = 8 };

            ExtrasCommon.VerifyDimensions(smallLimit, 0, 2).IsOk.ShouldBeFalse();
            ExtrasCommon.VerifyDimensions(smallLimit, 5, 2).IsOk.ShouldBeFalse();
            ExtrasCommon.VerifyDimensions(smallLimit, 2, 5).IsOk.ShouldBeFalse();
            ExtrasCommon.VerifyDimensions(smallLimit, 3, 3).IsOk.ShouldBeFalse();

            // SelectFormat with gray promotion and alpha drop, plus failure case
            JxlBasicInfo grayAlphaInfo     = default;
            grayAlphaInfo.BitsPerSample    = 8;
            grayAlphaInfo.NumColorChannels = 1;
            grayAlphaInfo.AlphaBits        = 8;

            JxlPixelFormat[] rgbOnly = { new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 } };

            ExtrasCommon.SelectFormat(rgbOnly, in grayAlphaInfo, out JxlPixelFormat chosen).IsOk.ShouldBeTrue();
            chosen.NumChannels.ShouldBe(3u);
            ExtrasCommon.SelectFormat(Array.Empty<JxlPixelFormat>(), in grayAlphaInfo, out _).IsOk.ShouldBeFalse();

            ExtrasTime.Now().ShouldBeGreaterThan(0.0);

            VerifyCodecInOutAndEncoderValidation();
        }

        private static void VerifyCodecInOutAndEncoderValidation()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io       = new CodecInOut(mm);

            io.Metadata.M.BitDepth.BitsPerSample = 0;
            io.CheckMetadata().IsOk.ShouldBeFalse();

            io.Metadata.M.SetUintSamples(8);
            io.Frames[0].Duration = 10;
            io.LastStillFrame().ShouldBe(0);

            Encoder pnmEnc = Encoder.FromExtension(".ppm")!;

            pnmEnc.ShouldNotBeNull();
            pnmEnc.AcceptsCmyk.ShouldBeFalse();
            pnmEnc.SetOption("key", "val");

            JxlBasicInfo info = default;

            Encoder.VerifyBasicInfo(in info).IsOk.ShouldBeFalse();
            info.XSize            = 2;
            info.YSize            = 2;
            info.NumColorChannels = 2;
            Encoder.VerifyBasicInfo(in info).IsOk.ShouldBeFalse();

            info.NumColorChannels = 3;
            info.BitsPerSample    = 8;
            info.AlphaBits        = 16;
            Encoder.VerifyBasicInfo(in info).IsOk.ShouldBeFalse();

            info.AlphaBits   = 8;
            info.Orientation = JxlOrientation.Rotate90Cw;
            Encoder.VerifyBasicInfo(in info).IsOk.ShouldBeFalse();

            info.Orientation = JxlOrientation.Identity;
            JxlPixelFormat rgbFmt = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 };
            PackedImage rgbImg    = PackedImage.Create(2, 2, in rgbFmt).Value!;

            Encoder.VerifyImageSize(rgbImg, in info).IsOk.ShouldBeFalse();

            JXLCompressParams cp = new JXLCompressParams
            {
                JpegStripExif  = true,
                JpegStripXmp   = true,
                JpegStripJumbf = true,
            };

            cp.JpegStripExif.ShouldBeTrue();
            cp.JpegStripXmp.ShouldBeTrue();
            cp.JpegStripJumbf.ShouldBeTrue();
        }

        /// <summary>
        /// Covers remaining argument validation and error branches in <see cref="PackedImageConvert"/>.
        /// </summary>
        [Test]
        public void PackedImageConvert_ErrorAndEdgeBranches_ExecuteCleanly()
        {
            JxlMemoryManager mm       = TestMemoryManager.MemoryManager();
            using Image3<float> img3f = CreateImage3F(mm, 2, 2, 0.5f);
            CodecInOut io             = new CodecInOut(mm);
            PackedPixelFile ppf       = new PackedPixelFile();
            JxlPixelFormat rgbFmt     = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 };
            JxlPixelFormat badDtFmt   = new JxlPixelFormat { NumChannels = 3, DataType = (JxlDataType)99 };
            JxlPixelFormat ch4Fmt     = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8 };

            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, null!));
            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertCodecInOutToPackedPixelFile(null!, in rgbFmt, ColorEncoding.SRGB(), null, ppf));
            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertCodecInOutToPackedPixelFile(io, in rgbFmt, null!, null, ppf));
            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertCodecInOutToPackedPixelFile(io, in rgbFmt, ColorEncoding.SRGB(), null, null!));
            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertImage3FToPackedPixelFile(null!, ColorEncoding.SRGB(), rgbFmt, null));
            Should.Throw<ArgumentNullException>(() => PackedImageConvert.ConvertImage3FToPackedPixelFile(img3f, null!, rgbFmt, null));

            PackedImageConvert.ConvertImage3FToPackedPixelFile(img3f, ColorEncoding.SRGB(), ch4Fmt, null).IsOk.ShouldBeFalse();
            PackedImageConvert.ConvertImage3FToPackedPixelFile(img3f, ColorEncoding.SRGB(), badDtFmt, null).IsOk.ShouldBeFalse();
            PackedImageConvert.ConvertCodecInOutToPackedPixelFile(io, in badDtFmt, ColorEncoding.SRGB(), null, ppf).IsOk.ShouldBeFalse();

            VerifyPpfToCodecInOutErrorCases(mm, rgbFmt, ch4Fmt);
        }

        private static void VerifyPpfToCodecInOutErrorCases(
            JxlMemoryManager mm,
            JxlPixelFormat rgbFmt,
            JxlPixelFormat ch4Fmt)
        {
            CodecInOut io       = new CodecInOut(mm);
            PackedPixelFile ppf = new PackedPixelFile();

            ppf.Frames.Add(PackedFrame.Create(2, 2, in rgbFmt).Value!);

            // Zero dimensions
            ppf.Info.XSize = 0;
            ppf.Info.YSize = 0;
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Invalid orientation
            ppf.Info.XSize       = 2;
            ppf.Info.YSize       = 2;
            ppf.Info.Orientation = (JxlOrientation)99;
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Invalid BitsPerSample / ExponentBitsPerSample combination
            ppf.Info.Orientation           = JxlOrientation.Identity;
            ppf.Info.BitsPerSample         = 8;
            ppf.Info.ExponentBitsPerSample = 5;
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Float16 metadata branch + mismatched AlphaBits
            ppf.Info.BitsPerSample         = 16;
            ppf.Info.ExponentBitsPerSample = 5;
            ppf.Info.AlphaBits             = 8;
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Invalid external color encoding
            ppf.Info.BitsPerSample         = 8;
            ppf.Info.ExponentBitsPerSample = 0;
            ppf.Info.AlphaBits             = 0;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            ppf.Icc                        = new byte[] { 1, 2, 3 };
            ppf.ColorEncoding              = new JxlColorEncoding { ColorSpace = (JxlColorSpace)99 };
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Grayscale mismatch between ColorEncoding (gray) and 3-channel frame
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.ColorEncodingIsPrimary;
            ppf.ColorEncoding              = ColorEncoding.SRGB(isGray: true).ToExternal();
            ppf.Info.NumColorChannels      = 1;
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();

            // Interleaved alpha (4 channels) without AlphaBits set on Info
            ppf.ColorEncoding         = ColorEncoding.SRGB(isGray: false).ToExternal();
            ppf.Info.NumColorChannels = 3;
            ppf.Info.AlphaBits        = 0;
            ppf.Frames.Clear();
            ppf.Frames.Add(PackedFrame.Create(2, 2, in ch4Fmt).Value!);
            PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Covers remaining branches in <see cref="ExtrasDecodeFacade"/>, <see cref="ExtrasEncodeFacade"/>, and <see cref="ExtrasCodec"/>.
        /// </summary>
        [Test]
        public void CodecFacade_AllRemainingBranches_ExecuteCleanly()
        {
            ExtrasDecodeFacade.DetectCodec("PG ML + 8 1 1\n"u8).ShouldBe(Codec.Pgx);
            ExtrasDecodeFacade.DetectCodec("P6\n"u8).ShouldBe(Codec.Pnm);
            ExtrasDecodeFacade.DetectCodec("PX\n"u8).ShouldBe(Codec.Unknown);
            ExtrasDecodeFacade.DetectCodec(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }).ShouldBe(Codec.Png);
            ExtrasDecodeFacade.DetectCodec("GIF87a"u8).ShouldBe(Codec.Gif);
            ExtrasDecodeFacade.DetectCodec("GIF89a"u8).ShouldBe(Codec.Gif);
            ExtrasDecodeFacade.DetectCodec(new byte[] { 0xFF, 0xD8, 0xFF }).ShouldBe(Codec.Jpg);
            ExtrasDecodeFacade.DetectCodec(new byte[] { 0x76, 0x2F, 0x31, 0x01 }).ShouldBe(Codec.Exr);
            ExtrasDecodeFacade.DetectCodec(new byte[] { 0xFF, 0x0A }).ShouldBe(Codec.Jxl);
            ExtrasDecodeFacade.DetectCodec(new byte[] { 0x00 }).ShouldBe(Codec.Unknown);

            ExtrasDecodeFacade.CodecFromPath("no_dot_in_filename").ShouldBe(Codec.Unknown);
            ExtrasDecodeFacade.CodecFromPath("image.jpeg").ShouldBe(Codec.Jpg);
            ExtrasDecodeFacade.CodecFromPath("image.apng").ShouldBe(Codec.Png);

            ExtrasEncodeFacade.FromExtension(string.Empty).ShouldBeNull();
            ExtrasEncodeFacade.FromExtension(".apng").ShouldNotBeNull();
            ExtrasEncodeFacade.FromExtension(".jpeg").ShouldBeNull();

            VerifyExtrasCodecEncodeAndDecodeOverloads();
        }

        private static void VerifyExtrasCodecEncodeAndDecodeOverloads()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io       = new CodecInOut(mm);

            io.SetFromImage(CreateImage3F(mm, 4, 4, 0.5f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            CodecInOut grayIo = new CodecInOut(mm);

            grayIo.Metadata.M.ColorEncoding = ColorEncoding.SRGB(isGray: true);
            grayIo.SetFromImage(CreateImage3F(mm, 4, 4, 0.5f), ColorEncoding.SRGB(isGray: true)).IsOk.ShouldBeTrue();

            ExtrasCodec.Encode(io, Codec.Unknown, ColorEncoding.SRGB(), 8, out _).IsOk.ShouldBeFalse();
            ExtrasCodec.Encode(io, Codec.Jpg, ColorEncoding.SRGB(), 8, out _).IsOk.ShouldBeFalse();
            ExtrasCodec.Encode(io, Codec.Exr, ColorEncoding.SRGB(), 8, out _).IsOk.ShouldBeFalse();
            ExtrasCodec.Encode(io, "out.exr", out _).IsOk.ShouldBeFalse();

            ExtrasCodec.Encode(io, Codec.Pnm, ColorEncoding.SRGB(), 32, out byte[] pfmBytes).IsOk.ShouldBeTrue();
            ExtrasCodec.Encode(grayIo, Codec.Pnm, ColorEncoding.SRGB(isGray: true), 8, out byte[] pgmBytes).IsOk.ShouldBeTrue();
            ExtrasCodec.Encode(grayIo, Codec.Pgx, ColorEncoding.SRGB(isGray: true), 8, out byte[] pgxBytes).IsOk.ShouldBeTrue();
            ExtrasCodec.Encode(io, Codec.Png, ColorEncoding.SRGB(), 8, out byte[] pngBytes).IsOk.ShouldBeTrue();
            pgmBytes.ShouldNotBeEmpty();

            // Decode via DecodeBytes and SetFromBytes overloads
            PackedPixelFile ppf    = new PackedPixelFile();
            PackedPixelFile pgxPpf = new PackedPixelFile();

            ExtrasDecodeFacade.DecodeBytes(pfmBytes, new ColorHints(), ppf).IsOk.ShouldBeTrue();
            ExtrasDecodeFacade.DecodeBytes(pgxBytes, new ColorHints(), pgxPpf).IsOk.ShouldBeTrue();
            ExtrasDecodeFacade.DecodeBytes(new byte[] { 0, 1, 2 }, new ColorHints(), ppf).IsOk.ShouldBeFalse();

            CodecInOut io2 = new CodecInOut(mm);

            ExtrasCodec.SetFromBytes(pngBytes, io2).IsOk.ShouldBeTrue();
            ExtrasCodec.SetFromBytes(new byte[] { 0, 1 }, io2).IsOk.ShouldBeFalse();

            // Encode PackedPixelFile by path and codec
            ExtrasCodec.Encode(pgxPpf, "out.pgx", out _).IsOk.ShouldBeTrue();
            ExtrasCodec.Encode(pgxPpf, "out.unknown", out _).IsOk.ShouldBeFalse();
            ExtrasCodec.Encode(new PackedPixelFile(), "out.png", out _).IsOk.ShouldBeFalse();
            ExtrasCodec.Encode(new PackedPixelFile(), Codec.Png, out _).IsOk.ShouldBeFalse();

            // Encode JXL and decode via ExtrasDecodeFacade.DecodeBytes with color_space hint
            JXLCompressParams cp = new JXLCompressParams { Distance = 1.0f };

            CodecJxl.EncodeImageJXL(cp, pgxPpf, null, out byte[] jxlBytes).ShouldBeTrue();
            ColorHints csHints = new ColorHints();

            csHints.Add("color_space", "Gra_D65_Rel_SRG");
            PackedPixelFile jxlPpf = new PackedPixelFile();

            ExtrasDecodeFacade.DecodeBytes(jxlBytes, csHints, jxlPpf).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Covers remaining branches in <see cref="GainMapCodec"/>, <see cref="ExtrasAlphaBlend"/>,
        /// <see cref="ExtrasExif"/>, <see cref="ExtrasHlg"/>, <see cref="ExtrasMetrics"/>, and <see cref="ExtrasToneMapping"/>.
        /// </summary>
        [Test]
        public void ExtrasUtilities_RemainingErrorAndEdgeBranches_ExecuteCleanly()
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();

            // GainMapCodec error & non-color-encoding branches
            GainMapCodec.JxlGainMapGetBundleSize(null!, out _).ShouldBeFalse();
            JxlGainMapBundle hugeMeta = new JxlGainMapBundle { GainMapMetadata = new byte[70000] };

            GainMapCodec.JxlGainMapGetBundleSize(in hugeMeta, out _).ShouldBeFalse();

            JxlGainMapBundle badCe = new JxlGainMapBundle
            {
                HasColorEncoding = true,
                ColorEncoding    = new JxlColorEncoding { ColorSpace = (JxlColorSpace)99 },
            };

            GainMapCodec.JxlGainMapGetBundleSize(in badCe, out _).ShouldBeFalse();
            GainMapCodec.JxlGainMapWriteBundle(in badCe, new byte[16], out _).ShouldBeFalse();

            JxlGainMapBundle noCe = new JxlGainMapBundle { HasColorEncoding = false };

            GainMapCodec.JxlGainMapGetBundleSize(in noCe, out nuint noCeSize).ShouldBeTrue();
            GainMapCodec.JxlGainMapWriteBundle(in noCe, new byte[noCeSize - 1], out _).ShouldBeFalse();
            byte[] noCeBuf = new byte[noCeSize];

            GainMapCodec.JxlGainMapWriteBundle(in noCe, noCeBuf, out _).ShouldBeTrue();
            GainMapCodec.JxlGainMapReadBundle(out JxlGainMapBundle noCeParsed, noCeBuf, out _).ShouldBeTrue();
            noCeParsed.HasColorEncoding.ShouldBeFalse();

            // Truncated buffers for JxlGainMapReadBundle
            for (int len = 1; len < (int)noCeSize; len++)
            {
                GainMapCodec.JxlGainMapReadBundle(out _, noCeBuf.AsSpan(0, len), out _).ShouldBeFalse();
            }

            // ExtrasAlphaBlend & ExtrasExif edge cases
            PackedPixelFile alphaPpf = new PackedPixelFile();

            alphaPpf.Info.AlphaBits = 8;
            ExtrasAlphaBlend.AlphaBlend(alphaPpf, new float[] { 0.5f }).IsOk.ShouldBeFalse();

            ExtrasExif.ResetExifOrientation(new byte[4]);
            ExtrasExif.ResetExifOrientation(new byte[16]);

            byte[] exifMultiTag = new byte[44];

            exifMultiTag[0] = (byte)'I';
            exifMultiTag[1] = (byte)'I';
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(2, 2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(exifMultiTag.AsSpan(4, 4), 8u);
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(8, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(10, 2), 256); // non-orientation tag first
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(22, 2), 274);
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(24, 2), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(exifMultiTag.AsSpan(26, 4), 1u);
            BinaryPrimitives.WriteUInt16LittleEndian(exifMultiTag.AsSpan(30, 2), 8);
            ExtrasExif.ResetExifOrientation(exifMultiTag);
            BinaryPrimitives.ReadUInt16LittleEndian(exifMultiTag.AsSpan(30, 2)).ShouldBe((ushort)1);

            VerifyHlgMetricsToneMappingGuards(mm);
        }

        private static void VerifyHlgMetricsToneMappingGuards(JxlMemoryManager mm)
        {
            Should.Throw<ArgumentNullException>(() => ExtrasHlg.HlgOOTF(null!, 1.2f));

            ButteraugliParams ba = new ButteraugliParams();

            Should.Throw<ArgumentNullException>(() => ExtrasMetrics.ComputeDistanceP(null!, in ba, 3.0));
            using ImagePlane<float> emptyPlane = new ImagePlane<float>();

            ExtrasMetrics.ComputeDistanceP(emptyPlane, in ba, 3.0).Value.ShouldBe(0.0);

            ImageMetadata meta = new ImageMetadata();

            meta.SetFloat32Samples();
            ImageBundle ib1 = new ImageBundle(mm, meta);
            ImageBundle ib2 = new ImageBundle(mm, meta);

            ib1.SetFromImage(CreateImage3F(mm, 2, 2, 0.5f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ib2.SetFromImage(CreateImage3F(mm, 4, 4, 0.5f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            JxlCmsInterface cms = JxlCms.GetDefaultCms();

            Should.Throw<ArgumentNullException>(() => ExtrasMetrics.ComputeDistance2(null!, ib1, cms));
            Should.Throw<ArgumentNullException>(() => ExtrasMetrics.ComputeDistance2(ib1, null!, cms));
            Should.Throw<ArgumentNullException>(() => ExtrasMetrics.ComputePSNR(null!, ib1, cms));
            Should.Throw<ArgumentNullException>(() => ExtrasMetrics.ComputePSNR(ib1, null!, cms));

            ExtrasMetrics.ComputePSNR(ib1, ib2, cms).ShouldBe(0.0);
            ExtrasMetrics.ComputeDistance2(ib1, ib1, cms).ShouldBe(0.0);

            Should.Throw<ArgumentNullException>(() => ExtrasToneMapping.ToneMapTo(new ExtrasToneMapping.LuminanceRange(0.1f, 100f), null!));
            Should.Throw<ArgumentNullException>(() => ExtrasToneMapping.GamutMap(null!, 0.5f));
        }

        /// <summary>
        /// Covers remaining branches in <see cref="CodecPnm"/>, <see cref="CodecPgx"/>, and <see cref="CodecNpy"/>.
        /// </summary>
        [Test]
        public void CodecPnmPgxAndNpy_RemainingBranches_ExecuteCleanly()
        {
            CodecPnm.PnmParseSigned("+12.34"u8, out double sVal).IsOk.ShouldBeTrue();
            sVal.ShouldBe(12.34, 1e-4);
            CodecPnm.PnmParseSigned("-0.5"u8, out double nVal).IsOk.ShouldBeTrue();
            nVal.ShouldBe(-0.5, 1e-4);
            CodecPnm.PnmParseSigned(""u8, out _).IsOk.ShouldBeFalse();
            CodecPnm.PnmParseSigned("-"u8, out _).IsOk.ShouldBeFalse();
            CodecPnm.PnmParseSigned("abc"u8, out _).IsOk.ShouldBeFalse();

            CodecPnm.PnmParseUnsigned("12345"u8, out nuint uVal).IsOk.ShouldBeTrue();
            uVal.ShouldBe((nuint)12345);
            CodecPnm.PnmParseUnsigned(""u8, out _).IsOk.ShouldBeFalse();
            CodecPnm.PnmParseUnsigned("999999999999999999999999999"u8, out _).IsOk.ShouldBeFalse();

            ColorHints emptyHints = new ColorHints();
            PackedPixelFile ppf   = new PackedPixelFile();

            CodecPnm.DecodeImagePNM(new byte[] { (byte)'P' }, emptyHints, ppf).IsOk.ShouldBeFalse();
            CodecPnm.DecodeImagePNM(
                ConcatAsciiAndBytes("P5\n10 10\n255\n", new byte[100]),
                emptyHints,
                ppf,
                new SizeConstraints { DecMaxXSize = 4, DecMaxYSize = 4, DecMaxPixels = 16 }).IsOk.ShouldBeFalse();

            // Invalid PNM/PFM headers
            string[] badPnmHeaders =
            {
                "P5\n2 2\n0\n",
                "P5\n2 2\n70000\n",
                "P5\n2 2\n100\n",
                "P4\n2\n",
                "PF\n2 2\n0.0\n",
                "PF\n2\n",
                "P9\n2 2\n255\n",
            };

            foreach (string badHdr in badPnmHeaders)
            {
                CodecPnm.DecodeImagePNM(ConcatAsciiAndBytes(badHdr, new byte[16]), emptyHints, ppf).IsOk.ShouldBeFalse(badHdr);
            }

            // Truncated PNM payload
            CodecPnm.DecodeImagePNM(ConcatAsciiAndBytes("P5\n4 4\n255\n", new byte[2]), emptyHints, ppf).IsOk.ShouldBeFalse();

            VerifyPamAndPgxAndNpyRemainingBranches();
        }

        private static void VerifyPamAndPgxAndNpyRemainingBranches()
        {
            ColorHints emptyHints = new ColorHints();
            PackedPixelFile ppf   = new PackedPixelFile();

            // PAM with comments, all extra TUPLTYPEs, and implicit depth 1..4
            string[] validPam =
            {
                "P7\n# comment\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 255\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nENDHDR\n",
                "P7\nWIDTH 1\nHEIGHT 1\nDEPTH 5\nMAXVAL 255\nTUPLTYPE RGB\nENDHDR\n",
                "P7\nWIDTH 1\nHEIGHT 1\nDEPTH 9\nMAXVAL 255\nTUPLTYPE GRAYSCALE\nTUPLTYPE ALPHA\nTUPLTYPE DEPTH\nTUPLTYPE SPOT_COLOR\nTUPLTYPE SELECTION_MASK\nTUPLTYPE BLACK\nTUPLTYPE CFA\nTUPLTYPE THERMAL\nTUPLTYPE Optional\nENDHDR\n",
            };

            foreach (string hdr in validPam)
            {
                CodecPnm.DecodeImagePNM(ConcatAsciiAndBytes(hdr, new byte[32]), emptyHints, ppf).IsOk.ShouldBeTrue(hdr);
            }

            // Encode PAM with all extra channel types + Encode PNM with extra channel streams and linear ICC
            EncodedImage encImg = new EncodedImage();

            ppf.ColorEncoding = ColorEncoding.LinearSRGB(isGray: true).ToExternal();
            ppf.Icc           = ColorEncoding.LinearSRGB(isGray: true).ICC();
            CodecPnm.GetPAMEncoder().Encode(ppf, encImg).IsOk.ShouldBeTrue();
            CodecPnm.GetPNMEncoder().Encode(ppf, encImg).IsOk.ShouldBeTrue();
            encImg.ExtraChannelBitstreams.ShouldNotBeEmpty();

            // Invalid PAM headers
            string[] invalidPam =
            {
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 5\nMAXVAL 255\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 255\nTUPLTYPE RGB\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 0\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 1\nMAXVAL 255\nTUPLTYPE UNKNOWN_TYPE\nENDHDR\n",
                "P7\nUNKNOWN_KEYWORD 1\nENDHDR\n",
                "P7\nWIDTH 2\nHEIGHT 1\n",
            };

            foreach (string badPam in invalidPam)
            {
                CodecPnm.DecodeImagePNM(ConcatAsciiAndBytes(badPam, new byte[16]), emptyHints, ppf).IsOk.ShouldBeFalse(badPam);
            }

            // PGX error branches
            string[] invalidPgx =
            {
                "PG XX + 8 2 2\n",
                "PG ML + 0 2 2\n",
                "PG ML + 24 2 2\n",
                "PG ML + 8 x 2\n",
                "PG ML + 8 2 2 ",
                "PG",
            };

            foreach (string badPgx in invalidPgx)
            {
                CodecPgx.DecodeImagePGX(ConcatAsciiAndBytes(badPgx, new byte[8]), emptyHints, ppf).IsOk.ShouldBeFalse(badPgx);
            }

            CodecPgx.DecodeImagePGX(ConcatAsciiAndBytes("PG ML + 8 4 4\n", new byte[2]), emptyHints, ppf).IsOk.ShouldBeFalse();
            CodecPgx.GetPGXEncoder().Encode(new PackedPixelFile(), encImg).IsOk.ShouldBeFalse();
            ppf.Info.NumColorChannels = 3;
            CodecPgx.GetPGXEncoder().Encode(ppf, encImg).IsOk.ShouldBeFalse();

            // NPY with animation and error cases
            PackedPixelFile emptyFramesPpf = new PackedPixelFile();

            emptyFramesPpf.Info.XSize            = 2;
            emptyFramesPpf.Info.YSize            = 2;
            emptyFramesPpf.Info.NumColorChannels = 3;
            emptyFramesPpf.Info.BitsPerSample    = 32;
            CodecNpy.GetNumPyEncoder().Encode(emptyFramesPpf, encImg).IsOk.ShouldBeFalse();

            JxlPixelFormat f3Fmt = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian };
            JxlPixelFormat f1Fmt = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian };

            emptyFramesPpf.Info.ExponentBitsPerSample = 8;
            emptyFramesPpf.Info.HaveAnimation         = true;
            emptyFramesPpf.Info.Animation             = new JxlAnimationHeader { TpsNumerator = 10, TpsDenominator = 1 };
            PackedFrame f0 = PackedFrame.Create(2, 2, in f3Fmt).Value!;

            f0.FrameInfo.Duration = 5;
            emptyFramesPpf.Frames.Add(f0);
            CodecNpy.GetNumPyEncoder().Encode(emptyFramesPpf, encImg).IsOk.ShouldBeTrue();

            emptyFramesPpf.Frames.Add(PackedFrame.Create(2, 2, in f1Fmt).Value!);
            CodecNpy.GetNumPyEncoder().Encode(emptyFramesPpf, encImg).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Covers remaining branches in <see cref="CodecApng"/> including iCCP, sRGB, gAMA, cICP, eXIf,
        /// 8-bit/16-bit RGB <c>tRNS</c>, extra channel encoding, sub-8-bit/16-bit sample scaling, and error paths.
        /// </summary>
        [Test]
        public void CodecApng_RemainingChunksTrnsAndErrorBranches_ExecuteCleanly()
        {
            Encoder apngEnc    = CodecApng.GetAPNGEncoder()!;
            ColorHints noHints = new ColorHints();

            // 1. Encode with IccIsPrimary + ExtraChannels + 4-bit UInt8 scaling + 12-bit UInt16 scaling + LittleEndian Float
            PackedPixelFile ppf = new PackedPixelFile();

            ppf.Info.XSize                 = 2;
            ppf.Info.YSize                 = 2;
            ppf.Info.NumColorChannels      = 3;
            ppf.Info.BitsPerSample         = 4;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            ppf.Icc                        = ColorEncoding.SRGB().ICC();
            ppf.ExtraChannelsInfo.Add(new PackedExtraChannel
            {
                Index  = 0,
                EcInfo = new JxlExtraChannelInfo { BitsPerSample = 12, Type = JxlExtraChannelType.Depth },
            });

            JxlPixelFormat rgb8Fmt = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8 };
            JxlPixelFormat ec16Le  = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.LittleEndian };
            PackedFrame frame      = PackedFrame.Create(2, 2, in rgb8Fmt).Value!;

            frame.ExtraChannels.Add(PackedImage.Create(2, 2, in ec16Le).Value!);
            ppf.Frames.Add(frame);

            EncodedImage encImg = new EncodedImage();

            apngEnc.Encode(ppf, encImg).IsOk.ShouldBeTrue();
            encImg.ExtraChannelBitstreams.Count.ShouldBe(1);

            // Decode the iCCP PNG produced above
            PackedPixelFile decIccp = new PackedPixelFile();

            CodecApng.DecodeImageAPNG(encImg.Bitstreams[0], noHints, decIccp).IsOk.ShouldBeTrue();
            decIccp.PrimaryColorRepresentation.ShouldBe(PrimaryColorRepresentation.IccIsPrimary);

            // Encode LittleEndian Float
            PackedPixelFile fPpf = new PackedPixelFile();

            fPpf.Info.XSize                 = 2;
            fPpf.Info.YSize                 = 2;
            fPpf.Info.NumColorChannels      = 3;
            fPpf.Info.BitsPerSample         = 32;
            fPpf.Info.ExponentBitsPerSample = 8;
            JxlPixelFormat fLeFmt = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian };

            fPpf.Frames.Add(PackedFrame.Create(2, 2, in fLeFmt).Value!);
            apngEnc.Encode(fPpf, encImg).IsOk.ShouldBeTrue();

            VerifyCustomPngVariantsAndErrors(noHints);
        }

        private static void VerifyCustomPngVariantsAndErrors(ColorHints noHints)
        {
            PackedPixelFile ppf = new PackedPixelFile();

            // cICP variants (primaries 11, 12, 99 and transfers 6, 8, 17, 18)
            byte[][] cicps =
            {
                new byte[] { 11, 6, 0, 1 },
                new byte[] { 12, 8, 0, 1 },
                new byte[] { 1, 17, 0, 1 },
                new byte[] { 9, 18, 0, 1 },
                new byte[] { 99, 1, 0, 1 },
                new byte[] { 1, 99, 0, 1 },
                new byte[] { 1, 13, 1, 1 },
                new byte[] { 1, 13 },
            };

            foreach (byte[] cicp in cicps)
            {
                byte[] png = BuildCustomPngWithChunks(8, 2, cicp, null, null, null);

                CodecApng.DecodeImageAPNG(png, noHints, ppf).IsOk.ShouldBeTrue();
            }

            // sRGB chunk, gAMA=1.0 (100000), gAMA=0.5 (50000), eXIf chunk, and 8-bit/16-bit RGB with tRNS
            CodecApng.DecodeImageAPNG(BuildCustomPngWithChunks(8, 2, null, new byte[] { 0 }, null, null), noHints, ppf).IsOk.ShouldBeTrue();
            CodecApng.DecodeImageAPNG(BuildCustomPngWithChunks(8, 2, null, null, 100000u, new byte[] { 0, 0, 0, 0, 0, 0 }), noHints, ppf).IsOk.ShouldBeTrue();
            CodecApng.DecodeImageAPNG(BuildCustomPngWithChunks(16, 2, null, null, 50000u, new byte[] { 0, 0, 0, 0, 0, 0 }), noHints, ppf).IsOk.ShouldBeTrue();

            // Error cases (corrupt iCCP stream, invalid iCCP header, bad PLTE, missing PLTE, invalid filter)
            CodecApng.DecodeImageAPNG(BuildPngWithExtraChunk(2, "iCCP", new byte[] { 1 }, 0), noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(BuildPngWithExtraChunk(2, "iCCP", new byte[] { (byte)'a', 0, 0, 1, 2 }, 0), noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(BuildPngWithExtraChunk(3, "PLTE", new byte[] { 1, 2 }, 0), noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(BuildPngWithExtraChunk(3, "tEXt", Array.Empty<byte>(), 0), noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(BuildPngWithExtraChunk(2, "tEXt", Array.Empty<byte>(), 99), noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(new byte[] { 1, 2, 3 }, noHints, ppf).IsOk.ShouldBeFalse();
            CodecApng.DecodeImageAPNG(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 99, 73, 72, 68, 82 }, noHints, ppf).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Covers remaining branches in <see cref="CodecJxl"/> including lossless encoding, extra channels,
        /// multi-frame options, stats collection, progressive decoding, callback decoding, custom bitdepths,
        /// and error paths.
        /// </summary>
        [Test]
        public void CodecJxl_AdvancedOptionsExtraChannelsAndProgressive_ExecuteCleanly()
        {
            PackedPixelFile ppf = new PackedPixelFile();

            ppf.Info.XSize                 = 8;
            ppf.Info.YSize                 = 8;
            ppf.Info.BitsPerSample         = 8;
            ppf.Info.NumColorChannels      = 3;
            ppf.Info.AlphaBits             = 8;
            ppf.Info.HaveAnimation         = true;
            ppf.Info.Animation             = new JxlAnimationHeader { TpsNumerator = 10, TpsDenominator = 1 };
            ppf.ColorEncoding              = ColorEncoding.SRGB().ToExternal();
            ppf.Metadata.Jhgm              = new byte[] { 1, 2, 3 };

            // Valid TIFF header at offset 0 for Exif.IsExif
            byte[] validTiff = new byte[32];

            validTiff[0] = (byte)'I';
            validTiff[1] = (byte)'I';
            BinaryPrimitives.WriteUInt16LittleEndian(validTiff.AsSpan(2, 2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(validTiff.AsSpan(4, 4), 8u);
            BinaryPrimitives.WriteUInt16LittleEndian(validTiff.AsSpan(8, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(validTiff.AsSpan(10, 2), 274);
            BinaryPrimitives.WriteUInt16LittleEndian(validTiff.AsSpan(12, 2), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(validTiff.AsSpan(14, 4), 1u);
            BinaryPrimitives.WriteUInt16LittleEndian(validTiff.AsSpan(18, 2), 6);
            ppf.Metadata.Exif = validTiff;

            JxlEncoder.JxlEncoderInitExtraChannelInfo(JxlExtraChannelType.SpotColor, out JxlExtraChannelInfo spotInfo);
            spotInfo.BitsPerSample = 8;
            ppf.ExtraChannelsInfo.Add(new PackedExtraChannel
            {
                Index  = 0,
                Name   = "spot0",
                EcInfo = spotInfo,
            });

            JxlPixelFormat rgbaFmt = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8 };
            JxlPixelFormat ecFmt   = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8 };

            for (int i = 0; i < 2; i++)
            {
                PackedFrame frame = PackedFrame.Create(8, 8, in rgbaFmt).Value!;

                frame.Name               = "fr" + i;
                frame.FrameInfo.Duration = 5;
                frame.FrameInfo.IsLast   = i == 1;
                frame.ExtraChannels.Add(PackedImage.Create(8, 8, in ecFmt).Value!);
                ppf.Frames.Add(frame);
            }

            JXLCompressParams cp = new JXLCompressParams
            {
                Distance           = 0.0f,
                AlphaDistance      = 0.0f,
                Premultiply        = 1,
                AlreadyDownsampled = 1,
                Stats              = new JxlSharp.Encoder.JxlEncoderStats(),
            };

            cp.Options.Add(new JXLOption(JxlEncoderFrameSettingId.Effort, 3L, 0));
            cp.Options.Add(new JXLOption(JxlEncoderFrameSettingId.Effort, 3L, 1));

            CodecJxl.EncodeImageJXL(cp, ppf, null, out byte[] jxlBytes).ShouldBeTrue();

            VerifyCodecJxlDecodeModesAndErrors(jxlBytes);
        }

        private static void VerifyCodecJxlDecodeModesAndErrors(byte[] jxlBytes)
        {
            // 1. Decode with UseImageCallback = true, RenderSpotcolors = false, Custom OutputBitdepth, MaxPasses = 1
            JXLDecompressParams dpCallback = new JXLDecompressParams
            {
                UseImageCallback = true,
                RenderSpotcolors = false,
                KeepOrientation  = false,
                MaxPasses        = 1,
                OutputBitdepth   = new JxlBitDepth
                {
                    Type          = JxlBitDepthType.Custom,
                    BitsPerSample = 8,
                },
            };

            PackedPixelFile dec1 = new PackedPixelFile();

            CodecJxl.DecodeImageJXL(jxlBytes, dpCallback, out _, dec1).ShouldBeTrue();
            dec1.ExtraChannelsInfo.ShouldNotBeEmpty();

            // 2. Decode with UInt16 and Float AcceptedFormats and MaxDownsampling = 2
            foreach (JxlDataType dt in new[] { JxlDataType.UInt16, JxlDataType.Float })
            {
                JXLDecompressParams dpFmt = new JXLDecompressParams
                {
                    UseImageCallback = false,
                    MaxDownsampling  = 2,
                };

                dpFmt.AcceptedFormats.Add(new JxlPixelFormat
                {
                    NumChannels = 4,
                    DataType    = dt,
                    Endianness  = JxlEndianness.NativeEndian,
                });

                PackedPixelFile decFmt = new PackedPixelFile();

                CodecJxl.DecodeImageJXL(jxlBytes, dpFmt, out _, decFmt).ShouldBeTrue();
            }

            // 3. Error paths in EncodeImageJXL & DecodeImageJXL
            CodecJxl.EncodeImageJXL(new JXLCompressParams(), new PackedPixelFile(), null, out _).ShouldBeFalse();
            CodecJxl.DecodeImageJXL(ReadOnlySpan<byte>.Empty, new JXLDecompressParams(), out _, new PackedPixelFile()).ShouldBeFalse();
            CodecJxl.DecodeImageJXL(
                jxlBytes,
                new JXLDecompressParams(),
                out _,
                new PackedPixelFile(),
                null,
                new SizeConstraints { DecMaxXSize = 2, DecMaxYSize = 2, DecMaxPixels = 4 }).ShouldBeFalse();
            CodecJxl.DecodeImageJXL(
                jxlBytes,
                new JXLDecompressParams { ColorSpace = "invalid_cs" },
                out _,
                new PackedPixelFile()).ShouldBeFalse();
        }

        private static Image3<float> CreateImage3F(JxlMemoryManager mm, nuint xsize, nuint ysize, float val)
        {
            Image3<float> img = Image3<float>.Create(mm, xsize, ysize).Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < ysize; y++)
                {
                    float* row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < xsize; x++)
                    {
                        row[x] = val;
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

        private static byte[] BuildCustomPngWithChunks(
            byte bitDepth,
            byte colorType,
            byte[]? cicp,
            byte[]? srgb,
            uint? gama,
            byte[]? trns)
        {
            const uint width  = 2;
            const uint height = 2;

            using MemoryStream ms = new MemoryStream();

            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            byte[] ihdr = new byte[13];

            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), width);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), height);
            ihdr[8] = bitDepth;
            ihdr[9] = colorType;
            WritePngChunk(ms, "IHDR", ihdr);

            if (cicp != null)
            {
                WritePngChunk(ms, "cICP", cicp);
            }

            if (srgb != null)
            {
                WritePngChunk(ms, "sRGB", srgb);
            }

            if (gama.HasValue)
            {
                byte[] gBytes = new byte[4];

                BinaryPrimitives.WriteUInt32BigEndian(gBytes, gama.Value);
                WritePngChunk(ms, "gAMA", gBytes);
            }

            if (trns != null)
            {
                WritePngChunk(ms, "tRNS", trns);
            }

            WritePngChunk(ms, "eXIf", new byte[] { (byte)'I', (byte)'I', 42, 0 });

            int bytesPerPixel = (colorType == 2 ? 3 : 1) * (bitDepth / 8);
            int rowBytes      = (int)width * bytesPerPixel;
            byte[] raw        = new byte[(int)height * (1 + rowBytes)];

            using MemoryStream zlibMs = new MemoryStream();

            using (ZLibStream z = new ZLibStream(zlibMs, CompressionLevel.Fastest, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }

            WritePngChunk(ms, "IDAT", zlibMs.ToArray());
            WritePngChunk(ms, "IEND", Array.Empty<byte>());

            return ms.ToArray();
        }

        private static byte[] BuildPngWithExtraChunk(byte colorType, string chunkType, byte[] chunkData, byte filterType)
        {
            using MemoryStream ms = new MemoryStream();

            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            byte[] ihdr = new byte[13];

            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), 2);
            ihdr[8] = 8;
            ihdr[9] = colorType;
            WritePngChunk(ms, "IHDR", ihdr);
            WritePngChunk(ms, chunkType, chunkData);

            int rowBytes = 2 * (colorType == 2 ? 3 : 1);
            byte[] raw   = new byte[2 * (1 + rowBytes)];

            raw[0]              = filterType;
            raw[1 + rowBytes]   = filterType;

            using MemoryStream zlibMs = new MemoryStream();

            using (ZLibStream z = new ZLibStream(zlibMs, CompressionLevel.Fastest, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }

            WritePngChunk(ms, "IDAT", zlibMs.ToArray());
            WritePngChunk(ms, "IEND", Array.Empty<byte>());

            return ms.ToArray();
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
