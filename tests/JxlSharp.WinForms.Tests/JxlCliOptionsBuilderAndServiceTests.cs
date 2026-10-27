// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.Tools;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Unit tests for <see cref="JxlCliOptionsBuilder"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlCliOptionsBuilderTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlCliOptionsBuilder.BuildCjxlArgs"/> and <see cref="JxlCliOptionsBuilder.CreateCompressParams"/>
        /// map all <see cref="CompressArgs"/> fields accurately.
        /// </summary>
        [Test]
        public void BuildCjxlArgsAndCreateCompressParamsMapAllOptions()
        {
            Mock<IServiceProvider> mockSp = new Mock<IServiceProvider>();
            JxlCliOptionsBuilder builder  = new JxlCliOptionsBuilder();

            CompressArgs verArgs = new CompressArgs { Version = true };
            builder.BuildCjxlArgs(verArgs).ShouldContain("--version");

            CompressArgs helpArgs = new CompressArgs { Help = true };
            builder.BuildCjxlArgs(helpArgs).ShouldContain("--help");

            CompressArgs cargs = CreateFullCompressArgs();
            string[] cli       = builder.BuildCjxlArgs(cargs);

            mockSp.Object.ShouldNotBeNull();
            cli.ShouldContain("in.png");
            cli.ShouldContain("out.jxl");
            cli.ShouldContain("--quality=85");
            cli.ShouldContain("--alpha_distance=0.5");
            cli.ShouldContain("--modular=1");
            cli.ShouldContain("--lossless_jpeg=0");
            cli.ShouldContain("--progressive");
            cli.ShouldContain("--allow_expert_options");
            cli.ShouldContain("--already_downsampled");
            cli.ShouldContain("--streaming_input");
            cli.ShouldContain("--streaming_output");
            cli.ShouldContain("--disable_output");
            cli.ShouldContain("--quiet");
            cli.ShouldContain("-v");
            cli.ShouldContain("-x");
            cli.ShouldContain("color_space=RGB_D65_SRG_Rel_SRG");

            PackedPixelFile ppf       = new PackedPixelFile();
            JXLCompressParams jparams = builder.CreateCompressParams(cargs, ppf, isJpegInput: true);
            jparams.Distance.ShouldBeGreaterThan(0.0f);
            jparams.AllowExpertOptions.ShouldBeTrue();

            cargs.GotQuality   = false;
            cargs.GotDistance  = false;
            cargs.LosslessJpeg = Override.On;
            cargs.Progressive  = false;
            cargs.Modular      = Override.Off;
            JXLCompressParams losslessParams = builder.CreateCompressParams(cargs, ppf, isJpegInput: true);
            losslessParams.Distance.ShouldBe(0.0f);
        }

        /// <summary>
        /// Verifies that <see cref="JxlCliOptionsBuilder.BuildDjxlArgs"/>, <see cref="JxlCliOptionsBuilder.BuildJxlInfoArgs"/>,
        /// <see cref="JxlCliOptionsBuilder.BuildJxlTranArgs"/>, and <see cref="JxlCliOptionsBuilder.CreateDecompressParams"/>
        /// map all option fields accurately.
        /// </summary>
        [Test]
        public void BuildDjxlJxlInfoAndJxlTranArgsMapAllOptions()
        {
            JxlCliOptionsBuilder builder = new JxlCliOptionsBuilder();

            builder.BuildDjxlArgs(new DecompressArgs { Version = true }).ShouldContain("--version");
            builder.BuildDjxlArgs(new DecompressArgs { Help = true }).ShouldContain("--help");

            DecompressArgs dargs = CreateFullDecompressArgs();
            string[] djxlCli     = builder.BuildDjxlArgs(dargs);

            djxlCli.ShouldContain("in.jxl");
            djxlCli.ShouldContain("out.jpg");
            djxlCli.ShouldContain("--pixels_to_jpeg");
            djxlCli.ShouldContain("--allow_partial_files");
            djxlCli.ShouldContain("--allow_more_progressive_steps");
            djxlCli.ShouldContain("--unpremultiply_alpha");
            djxlCli.ShouldContain("--output_frames");
            djxlCli.ShouldContain("--output_extra_channels");
            djxlCli.ShouldContain("--use_sjpeg");
            djxlCli.ShouldContain("--print_read_bytes");
            djxlCli.ShouldContain("--disable_output");
            djxlCli.ShouldContain("--quiet");
            djxlCli.ShouldContain("--num_passes=3");
            djxlCli.ShouldContain("--display_nits=250");
            djxlCli.ShouldContain("--color_space=RGB_D65_SRG_Rel_SRG");

            JXLDecompressParams dparams = builder.CreateDecompressParams(dargs);
            dparams.OutputBitdepth.Type.ShouldBe(JxlBitDepthType.Custom);
            dparams.OutputBitdepth.BitsPerSample.ShouldBe(16u);

            builder.BuildJxlInfoArgs("a.jxl", verbose: true, help: true).ShouldContain("-h");
            builder.BuildJxlInfoArgs("a.jxl", verbose: true, help: false).ShouldContain("-v");

            builder.BuildJxlTranArgs(new JxlTranArgs { Help = true }).ShouldContain("--help");
            JxlTranArgs targs = new JxlTranArgs { FileIn = "a.jxl", FileOut = "b.jxl", Pack = true, Extract = true, Quiet = true };
            targs.AddBoxSpecs.Add("Exif=exif.bin");
            targs.RemoveBoxTypes.Add("xml ");
            string[] tranCli = builder.BuildJxlTranArgs(targs);
            tranCli.ShouldContain("--pack");
            tranCli.ShouldContain("--extract");
            tranCli.ShouldContain("--add_box=Exif=exif.bin");
            tranCli.ShouldContain("--remove_box=xml ");
        }

        private static CompressArgs CreateFullCompressArgs()
        {
            CompressArgs cargs = new CompressArgs
            {
                FileIn             = "in.png",
                FileOut            = "out.jxl",
                Quality            = 85.0f,
                GotQuality         = true,
                GotDistance        = false,
                AlphaDistance      = 0.5f,
                GotAlphaDistance   = true,
                Effort             = 5,
                Modular            = Override.On,
                LosslessJpeg       = Override.Off,
                Container          = Override.On,
                KeepInvisible      = Override.On,
                Dots               = Override.Off,
                Patches            = Override.On,
                Gaborish           = Override.Off,
                GroupOrder         = Override.On,
                ProgressiveAc      = Override.On,
                QProgressiveAc     = Override.Off,
                JpegReconCfl       = Override.On,
                CompressBoxes      = true,
                PhotonNoiseIso     = 400.0f,
                IntensityTarget    = 1000.0f,
                Resampling         = 2,
                CenterX            = 4,
                CenterY            = 4,
                Progressive        = true,
                AllowExpertOptions = true,
                AlreadyDownsampled = true,
                StreamingInput     = true,
                StreamingOutput    = true,
                DisableOutput      = true,
                Quiet              = true,
                Verbosity          = 1,
            };

            cargs.ColorHints.Add("color_space", "RGB_D65_SRG_Rel_SRG");

            return cargs;
        }

        private static DecompressArgs CreateFullDecompressArgs()
        {
            return new DecompressArgs
            {
                FileIn                    = "in.jxl",
                FileOut                   = "out.jpg",
                PixelsToJpeg              = true,
                AllowPartialFiles         = true,
                AllowMoreProgressiveSteps = true,
                UnpremultiplyAlpha        = true,
                OutputFrames              = true,
                OutputExtraChannels       = true,
                UseSjpeg                  = true,
                PrintReadBytes            = true,
                DisableOutput             = true,
                Quiet                     = true,
                BitsPerSample             = 16,
                MaxPasses                 = 3,
                DisplayNits               = 250.0,
                ColorSpace                = "RGB_D65_SRG_Rel_SRG",
                AlphaBlend                = "black",
                PreviewOut                = "prev.png",
                IccOut                    = "out.icc",
                OrigIccOut                = "orig.icc",
                MetadataOut               = "meta.bin",
            };
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlImageService"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlImageServiceTests
    {
        /// <summary>
        /// Verifies round-trip conversion between <see cref="Bitmap"/>, <see cref="PackedPixelFile"/>, JPEG, and JPEG XL,
        /// as well as saving and loading files and computing quality metrics.
        /// </summary>
        [Test]
        public void LoadConvertAndSaveJpgAndJxlRoundTripsSuccessfully()
        {
            JxlImageService service = new JxlImageService();
            string tempDir          = Path.Combine(Path.GetTempPath(), $"jxl_svc_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using Bitmap srcBmp = CreateGradientBitmap(16, 16, PixelFormat.Format32bppArgb);
                PackedPixelFile ppf = service.ConvertBitmapToPackedPixelFile(srcBmp);

                ppf.XSize.ShouldBe((nuint)16);
                ppf.YSize.ShouldBe((nuint)16);

                byte[] jpgBytes  = service.EncodeBitmapToJpegBytes(srcBmp, 92);
                string jpgPath   = Path.Combine(tempDir, "input.jpg");
                File.WriteAllBytes(jpgPath, jpgBytes);

                JxlLoadedImage loadedJpg = service.LoadImageFile(jpgPath);
                loadedJpg.DetectedCodec.ShouldBe(Codec.Jpg);
                loadedJpg.OriginalJpegBytes.ShouldNotBeNull();

                string jxlPath   = Path.Combine(tempDir, "output.jxl");
                CompressArgs ca  = new CompressArgs { Effort = 2, LosslessJpeg = Override.Off, Distance = 1.0f, GotDistance = true };
                DecompressArgs da = new DecompressArgs { JpegQuality = 90 };

                service.SaveImageFile(jxlPath, in loadedJpg, ca, da, out string saveLog).ShouldBeTrue();
                saveLog.ShouldNotBeEmpty();

                JxlLoadedImage loadedJxl = service.LoadImageFile(jxlPath, da);
                loadedJxl.DetectedCodec.ShouldBe(Codec.Jxl);
                loadedJxl.JxlReport.ShouldNotBeNull();
                loadedJxl.JxlReport!.IsValid.ShouldBeTrue();

                string savedJpgPath = Path.Combine(tempDir, "reencoded.jpg");
                service.SaveImageFile(savedJpgPath, in loadedJxl, ca, da, out _).ShouldBeTrue();
                File.Exists(savedJpgPath).ShouldBeTrue();

                string savedPngPath = Path.Combine(tempDir, "saved.png");
                service.SaveImageFile(savedPngPath, in loadedJxl, ca, da, out _).ShouldBeTrue();
                JxlLoadedImage loadedPng = service.LoadImageFile(savedPngPath);
                loadedPng.DetectedCodec.ShouldBe(Codec.Png);

                service.ComputeImageMetrics(loadedJpg.PixelFile, loadedJxl.PixelFile, out double psnr, out _).ShouldBeTrue();
                psnr.ShouldBeGreaterThan(15.0);

                loadedJpg.PreviewBitmap.Dispose();
                loadedJxl.PreviewBitmap.Dispose();
                loadedPng.PreviewBitmap.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="JxlImageService.ConvertPackedPixelFileToBitmap"/> across 1, 2, 3, and 4 channels
        /// and <c>UInt8</c> (4-bit), <c>UInt16</c> (BigEndian and LittleEndian), <c>Float16</c>, and <c>Float32</c> formats.
        /// </summary>
        [Test]
        public void ConvertPackedPixelFileToBitmapSupportsAllFormatsAndBitDepths()
        {
            JxlImageService service = new JxlImageService();

            VerifyFormatBitmapConversion(service, 1, JxlDataType.UInt8, JxlEndianness.LittleEndian, 4);
            VerifyFormatBitmapConversion(service, 2, JxlDataType.UInt16, JxlEndianness.LittleEndian, 12);
            VerifyFormatBitmapConversion(service, 3, JxlDataType.UInt16, JxlEndianness.BigEndian, 16);
            VerifyFormatBitmapConversion(service, 4, JxlDataType.Float16, JxlEndianness.LittleEndian, 16);
            VerifyFormatBitmapConversion(service, 3, JxlDataType.Float16, JxlEndianness.BigEndian, 16);
            VerifyFormatBitmapConversion(service, 4, JxlDataType.Float, JxlEndianness.LittleEndian, 32);
            VerifyFormatBitmapConversion(service, 3, JxlDataType.Float, JxlEndianness.BigEndian, 32);
        }

        /// <summary>
        /// Verifies CLI tool wrappers (<see cref="JxlImageService.RunCjxl"/>, <see cref="JxlImageService.RunDjxl"/>,
        /// <see cref="JxlImageService.RunJxlInfo"/>, <see cref="JxlImageService.RunJxlTran"/>) on JPG and JXL files.
        /// </summary>
        [Test]
        public void RunCliToolsViaServiceSucceedsOnJpgAndJxlFiles()
        {
            JxlImageService service = new JxlImageService();
            string tempDir          = Path.Combine(Path.GetTempPath(), $"jxl_cli_svc_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                using Bitmap bmp = CreateGradientBitmap(16, 16, PixelFormat.Format24bppRgb);
                byte[] jpgBytes  = service.EncodeBitmapToJpegBytes(bmp, 90);
                string jpgIn     = Path.Combine(tempDir, "in.jpg");
                string jxlOut    = Path.Combine(tempDir, "out.jxl");
                string jpgOut    = Path.Combine(tempDir, "out.jpg");
                string tranOut   = Path.Combine(tempDir, "tran.jxl");

                File.WriteAllBytes(jpgIn, jpgBytes);

                CompressArgs cargs = new CompressArgs
                {
                    FileIn       = jpgIn,
                    FileOut      = jxlOut,
                    Effort       = 2,
                    LosslessJpeg = Override.Off,
                    Distance     = 1.0f,
                    GotDistance  = true,
                };

                service.RunCjxl(cargs, out _, out _).ShouldBe(0);
                File.Exists(jxlOut).ShouldBeTrue();

                service.RunJxlInfo(jxlOut, verbose: true, out string infoOut, out _).ShouldBe(0);
                infoOut.ShouldContain("16x16");

                DecompressArgs dargs = new DecompressArgs
                {
                    FileIn       = jxlOut,
                    FileOut      = jpgOut,
                    PixelsToJpeg = true,
                    JpegQuality  = 90,
                };

                service.RunDjxl(dargs, out _, out _).ShouldBe(0);
                File.Exists(jpgOut).ShouldBeTrue();

                JxlTranArgs targs = new JxlTranArgs
                {
                    FileIn  = jxlOut,
                    FileOut = tranOut,
                    Pack    = true,
                };

                service.RunJxlTran(targs, out _, out _).ShouldBe(0);
                File.Exists(tranOut).ShouldBeTrue();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void VerifyFormatBitmapConversion(
            JxlImageService service,
            uint numChannels,
            JxlDataType dataType,
            JxlEndianness endianness,
            uint bitsPerSample)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.XSize            = 4;
            info.YSize            = 4;
            info.BitsPerSample    = bitsPerSample;
            info.NumColorChannels = numChannels >= 3 ? 3u : 1u;
            ppf.Info              = info;

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = dataType,
                Endianness  = endianness,
                Align       = 0,
            };

            PackedFrame frame = PackedFrame.Create(4, 4, fmt).Value;
            FillTestSamples(frame.Color.Pixels(), dataType, endianness);
            ppf.Frames.Add(frame);

            using Bitmap bmp = service.ConvertPackedPixelFileToBitmap(ppf);
            bmp.Width.ShouldBe(4);
            bmp.Height.ShouldBe(4);
        }

        private static void FillTestSamples(Span<byte> dst, JxlDataType dataType, JxlEndianness endianness)
        {
            bool big = endianness == JxlEndianness.BigEndian;

            if (dataType == JxlDataType.UInt8)
            {
                dst.Fill(7);
            }
            else if (dataType == JxlDataType.UInt16)
            {
                for (int i = 0; i + 2 <= dst.Length; i += 2)
                {
                    if (big)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(dst.Slice(i, 2), 1024);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(i, 2), 1024);
                    }
                }
            }
            else if (dataType == JxlDataType.Float16)
            {
                ushort halfOne = BitConverter.HalfToUInt16Bits((Half)0.5f);

                for (int i = 0; i + 2 <= dst.Length; i += 2)
                {
                    if (big)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(dst.Slice(i, 2), halfOne);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(i, 2), halfOne);
                    }
                }
            }
            else
            {
                for (int i = 0; i + 4 <= dst.Length; i += 4)
                {
                    if (big)
                    {
                        BinaryPrimitives.WriteSingleBigEndian(dst.Slice(i, 4), 0.5f);
                    }
                    else
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(dst.Slice(i, 4), 0.5f);
                    }
                }
            }
        }

        private static Bitmap CreateGradientBitmap(int width, int height, PixelFormat format)
        {
            Bitmap bmp = new Bitmap(width, height, format);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(255, x * 12, y * 12, 128));
                }
            }

            return bmp;
        }
    }
}
