// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using JxlSharp.Base;
using JxlSharp.Extras;
using JxlSharp.Tools;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Unit tests for <see cref="MainForm"/>.
    /// </summary>
    [TestFixture]
    public sealed class MainFormTests
    {
        /// <summary>
        /// Verifies that <see cref="MainForm"/> round-trips all <see cref="CompressArgs"/>, <see cref="DecompressArgs"/>,
        /// and <see cref="JxlTranArgs"/> option controls accurately.
        /// </summary>
        [Test]
        public void UiOptionsControlsRoundTripCompressDecompressAndTranArgs()
        {
            Mock<IWin32Window> mockOwner = new Mock<IWin32Window>();
            using MainForm form          = new MainForm();

            CompressArgs cargsIn = new CompressArgs
            {
                Quality            = 88.0f,
                GotQuality         = true,
                GotDistance        = false,
                AlphaDistance      = 1.0f,
                Effort             = 4,
                LosslessJpeg       = Override.Off,
                Modular            = Override.On,
                Container          = Override.On,
                CompressBoxes      = false,
                Progressive        = true,
                ProgressiveAc      = Override.On,
                QProgressiveAc     = Override.Off,
                ProgressiveDc      = 1,
                Responsive         = 1,
                Epf                = 2,
                FasterDecoding     = 2,
                Resampling         = 2,
                EcResampling       = 2,
                BrotliEffort       = 5,
                Gaborish           = Override.On,
                Dots               = Override.Off,
                Patches            = Override.On,
                KeepInvisible      = Override.Off,
                GroupOrder         = Override.On,
                JpegReconCfl       = Override.On,
                CenterX            = 8,
                CenterY            = 8,
                PhotonNoiseIso     = 200.0f,
                IntensityTarget    = 500.0f,
                OverrideBitdepth   = 8,
                CodestreamLevel    = 5,
                UpsamplingMode     = 1,
                Premultiply        = 0,
                StripAlpha         = 0,
                Buffering          = 1,
                NumThreads         = 2,
                NumReps            = 2,
                AllowExpertOptions = true,
                AlreadyDownsampled = true,
                StreamingInput     = true,
                StreamingOutput    = true,
                Quiet              = true,
            };

            form.ApplyCompressArgsToUi(cargsIn);
            CompressArgs cargsOut = form.ReadCompressArgsFromUi();

            mockOwner.Object.ShouldNotBeNull();
            cargsOut.GotQuality.ShouldBeTrue();
            cargsOut.Quality.ShouldBe(88.0f);
            cargsOut.Effort.ShouldBe(4L);
            cargsOut.Modular.ShouldBe(Override.On);
            cargsOut.LosslessJpeg.ShouldBe(Override.Off);
            cargsOut.AllowExpertOptions.ShouldBeTrue();

            DecompressArgs dargsIn = new DecompressArgs
            {
                PixelsToJpeg              = true,
                JpegQuality               = 85,
                UseSjpeg                  = true,
                BitsPerSample             = 16,
                ColorSpace                = "RGB_D65_SRG_Rel_SRG",
                DisplayNits               = 300.0,
                AlphaBlend                = "black",
                UnpremultiplyAlpha        = true,
                MaxDownsampling           = 2,
                MaxPasses                 = 4,
                AllowPartialFiles         = true,
                AllowMoreProgressiveSteps = true,
                RenderSpotcolors          = false,
                Coalescing                = false,
                OutputFrames              = true,
                OutputExtraChannels       = true,
                UseImageCallback          = false,
                PrintReadBytes            = true,
                Quiet                     = true,
                NumThreads                = 4,
                NumReps                   = 2,
                PreviewOut                = "p.png",
                IccOut                    = "i.icc",
                OrigIccOut                = "o.icc",
                MetadataOut               = "m.bin",
            };

            form.ApplyDecompressArgsToUi(dargsIn);
            DecompressArgs dargsOut = form.ReadDecompressArgsFromUi();
            dargsOut.PixelsToJpeg.ShouldBeTrue();
            dargsOut.JpegQuality.ShouldBe((nuint)85);
            dargsOut.BitsPerSample.ShouldBe(16);
            dargsOut.MaxPasses.ShouldBe(4u);

            JxlTranArgs targsIn = new JxlTranArgs { Pack = true, Extract = false, Quiet = true };
            targsIn.AddBoxSpecs.Add("Exif=a.bin");
            targsIn.RemoveBoxTypes.Add("xml ");
            form.ApplyJxlTranArgsToUi(targsIn);
            JxlTranArgs targsOut = form.ReadJxlTranArgsFromUi();
            targsOut.Pack.ShouldBeTrue();
            targsOut.AddBoxSpecs.ShouldContain("Exif=a.bin");
            targsOut.RemoveBoxTypes.ShouldContain("xml ");
        }

        /// <summary>
        /// Verifies loading, previewing, converting between JPG and JXL, inspecting JXL properties in the UI grid,
        /// saving, computing quality metrics, and executing CLI tools via <see cref="MainForm"/>.
        /// </summary>
        [Test]
        public void MainFormWorkflowLoadsPreviewsConvertsInspectsAndRunsCliTools()
        {
            using MainForm form     = new MainForm();
            JxlImageService service = new JxlImageService();
            string tempDir          = Path.Combine(Path.GetTempPath(), $"jxl_form_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                form.ConvertCurrentToJxlAndPreview();
                form.ConvertCurrentToJpegAndPreview();
                form.SaveCurrentImageToPath(Path.Combine(tempDir, "none.jxl")).ShouldBeFalse();
                form.ApplyJxlTranToCurrentImage().ShouldBeFalse();
                form.ComputeAndLogCurrentQualityMetrics().ShouldBeFalse();

                using Bitmap bmp = new Bitmap(16, 16, PixelFormat.Format24bppRgb);
                byte[] jpgBytes  = service.EncodeBitmapToJpegBytes(bmp, 90);
                string jpgPath   = Path.Combine(tempDir, "source.jpg");
                File.WriteAllBytes(jpgPath, jpgBytes);

                _ = form.Handle;
                _ = form.RightTabControl.Handle;

                form.LoadFromFilePath(jpgPath);
                form.OriginalImage.ShouldNotBeNull();
                form.OriginalImage!.Value.DetectedCodec.ShouldBe(Codec.Jpg);
                form.CurrentImage.ShouldNotBeNull();
                form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jpg);
                form.RightTabControl.SelectedTab.ShouldBe(form.PropertiesTabPage);
                form.DecodeTabPage.Enabled.ShouldBeFalse();
                form.PreviewPictureBox.Image.ShouldNotBeNull();
                form.PreviewPictureBox.OriginalImage.ShouldNotBeNull();
                form.PreviewPictureBox.ConvertedImage.ShouldBeNull();
                form.PreviewPictureBox.ShowSplitter.ShouldBeFalse();

                form.RightTabControl.SelectedTab = form.EncodeTabPage;
                form.PreviewPictureBox.ShowSplitter.ShouldBeTrue();
                form.PreviewPictureBox.ConvertedImage.ShouldNotBeNull();
                form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);

                CompressArgs fastEncode = new CompressArgs { Effort = 1, LosslessJpeg = Override.Off, Distance = 2.0f, GotDistance = true };
                form.ApplyCompressArgsToUi(fastEncode);

                form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);
                form.CurrentPropertyReport.ShouldNotBeNull();
                form.PropertiesGrid.Rows.Count.ShouldBeGreaterThan(15);
                form.VerboseInfoTextBox.Text.ShouldContain("16x16");

                form.ApplyJxlTranArgsToUi(new JxlTranArgs { Pack = true });
                form.ApplyJxlTranToCurrentImage().ShouldBeTrue();

                form.ComputeAndLogCurrentQualityMetrics().ShouldBeTrue();

                string savedJxl = Path.Combine(tempDir, "saved.jxl");
                form.SaveCurrentImageToPath(savedJxl).ShouldBeTrue();
                File.Exists(savedJxl).ShouldBeTrue();

                form.ConvertCurrentToJpegAndPreview();
                form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jpg);
                form.OriginalImage!.Value.DetectedCodec.ShouldBe(Codec.Jpg);

                form.PreviewPictureBox.SplitRatio = 0.35f;
                form.PreviewPictureBox.SplitRatio.ShouldBe(0.35f, 0.001f);

                form.SetZoomMode(PictureBoxSizeMode.AutoSize);
                form.PreviewPictureBox.SizeMode.ShouldBe(PictureBoxSizeMode.AutoSize);
                form.SetZoomMode(PictureBoxSizeMode.Zoom);

                VerifyFormCliExecution(form, jpgPath, savedJxl, tempDir);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void VerifyFormCliExecution(MainForm form, string jpgPath, string savedJxl, string tempDir)
        {
            string cliJxl = Path.Combine(tempDir, "cli_out.jxl");
            string cliPng = Path.Combine(tempDir, "cli_out.png");

            form.ExecuteCjxlToPath(jpgPath, cliJxl).ShouldBe(0);
            form.ExecuteJxlInfoOnPath(cliJxl, verbose: true).ShouldBe(0);
            form.ExecuteDjxlToPath(savedJxl, cliPng).ShouldBe(0);

            form.ExecuteRawCliCommand("cjxl", "--version").ShouldBe(0);
            form.ExecuteRawCliCommand("djxl", "--version").ShouldBe(0);
            form.ExecuteRawCliCommand("jxlinfo", "--help").ShouldBe(0);
            form.ExecuteRawCliCommand("jxltran", "--version").ShouldBe(0);
            form.ConsoleLogTextBox.Text.ShouldContain("Exit Code: 0");
        }

        /// <summary>
        /// Verifies that loading an RGBA PNG image (<c>flower_alpha.png</c>) converts to JXL and previews without throwing.
        /// </summary>
        [Test]
        public void LoadFlowerAlphaPngConvertsAndPreviewsWithoutException()
        {
            Mock<IWin32Window> mockOwner = new Mock<IWin32Window>();
            using MainForm form          = new MainForm();
            string flowerAlphaPath       = @"P:\source\jxl#\libjxl\testdata\jxl\flower\flower_alpha.png";

            mockOwner.Object.ShouldNotBeNull();

            if (!File.Exists(flowerAlphaPath))
            {
                return;
            }

            _ = form.Handle;
            _ = form.RightTabControl.Handle;

            form.LoadFromFilePath(flowerAlphaPath);

            form.OriginalImage.ShouldNotBeNull();
            form.CurrentImage.ShouldNotBeNull();
            form.RightTabControl.SelectedTab.ShouldBe(form.PropertiesTabPage);
            form.DecodeTabPage.Enabled.ShouldBeFalse();
            form.PreviewPictureBox.ShowSplitter.ShouldBeFalse();

            form.RightTabControl.SelectedTab = form.EncodeTabPage;
            form.PreviewPictureBox.ShowSplitter.ShouldBeTrue();
            form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);
        }

        /// <summary>
        /// Verifies that loading an image starts on the JXL Properties tab without a splitter, switching to the Encode tab
        /// shows the splitter with the encoded image on the right, and switching to the Decode tab is enabled only when
        /// the loaded image is an encoded JXL image and shows the decoded image on the right.
        /// </summary>
        [Test]
        public void SplitPreviewSliderShowsOriginalOnLeftAndEncodedOrDecodedOnRight()
        {
            Mock<IWin32Window> mockOwner = new Mock<IWin32Window>();
            using MainForm form          = new MainForm();
            JxlImageService service      = new JxlImageService();

            mockOwner.Object.ShouldNotBeNull();
            _ = form.Handle;
            _ = form.RightTabControl.Handle;

            using Bitmap srcBmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    srcBmp.SetPixel(x, y, Color.FromArgb(128, (x * 16) & 255, (y * 16) & 255, 200));
                }
            }

            byte[] jpgBytes = service.EncodeBitmapToJpegBytes(srcBmp, 95);

            form.LoadFromBytes(jpgBytes, "test.jpg");

            Bitmap? jpgOriginalRef = form.OriginalImage!.Value.PreviewBitmap;
            form.RightTabControl.SelectedTab.ShouldBe(form.PropertiesTabPage);
            form.DecodeTabPage.Enabled.ShouldBeFalse();
            form.PreviewPictureBox.ShowSplitter.ShouldBeFalse();
            form.PreviewPictureBox.ConvertedImage.ShouldBeNull();
            form.PreviewPictureBox.OriginalImage.ShouldBeSameAs(jpgOriginalRef);

            form.RightTabControl.SelectedTab = form.EncodeTabPage;
            form.PreviewPictureBox.ShowSplitter.ShouldBeTrue();
            form.PreviewPictureBox.OriginalImage.ShouldBeSameAs(jpgOriginalRef);
            form.PreviewPictureBox.ConvertedImage.ShouldBeSameAs(form.CurrentImage!.Value.PreviewBitmap);
            form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);

            byte[] jxlBytes = form.CurrentImage!.Value.RawBytes;

            form.LoadFromBytes(jxlBytes, "test.jxl");

            Bitmap? jxlOriginalRef = form.OriginalImage!.Value.PreviewBitmap;
            form.RightTabControl.SelectedTab.ShouldBe(form.PropertiesTabPage);
            form.DecodeTabPage.Enabled.ShouldBeTrue();
            form.PreviewPictureBox.ShowSplitter.ShouldBeFalse();
            form.PreviewPictureBox.ConvertedImage.ShouldBeNull();
            form.PreviewPictureBox.OriginalImage.ShouldBeSameAs(jxlOriginalRef);

            form.RightTabControl.SelectedTab = form.DecodeTabPage;
            form.PreviewPictureBox.ShowSplitter.ShouldBeTrue();
            form.PreviewPictureBox.OriginalImage.ShouldBeSameAs(jxlOriginalRef);
            form.PreviewPictureBox.ConvertedImage.ShouldBeSameAs(form.CurrentImage!.Value.PreviewBitmap);
            form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);

            form.ApplyDecompressArgsToUi(new DecompressArgs
            {
                MaxDownsampling = 2,
                BitsPerSample   = 8,
            });

            form.PreviewPictureBox.ShowSplitter.ShouldBeTrue();
            form.PreviewPictureBox.OriginalImage.ShouldBeSameAs(jxlOriginalRef);
            form.PreviewPictureBox.ConvertedImage.ShouldBeSameAs(form.CurrentImage!.Value.PreviewBitmap);
            form.CurrentImage!.Value.DetectedCodec.ShouldBe(Codec.Jxl);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Program"/>.
    /// </summary>
    [TestFixture]
    public sealed class ProgramTests
    {
        /// <summary>
        /// Verifies <see cref="Program.CreateMainForm"/> and <see cref="Program.DispatchCliCommand"/>.
        /// </summary>
        [Test]
        public void CreateMainFormAndDispatchCliCommandWorkAsExpected()
        {
            Mock<TextWriter> mockWriter = new Mock<TextWriter>();
            JxlImageService service     = new JxlImageService();
            string tempJpg              = Path.Combine(Path.GetTempPath(), $"prog_test_{Guid.NewGuid():N}.jpg");

            try
            {
                using Bitmap bmp = new Bitmap(8, 8, PixelFormat.Format24bppRgb);
                File.WriteAllBytes(tempJpg, service.EncodeBitmapToJpegBytes(bmp, 85));

                using MainForm formWithFile = Program.CreateMainForm(new[] { tempJpg });
                formWithFile.CurrentImage.ShouldNotBeNull();

                using MainForm formEmpty = Program.CreateMainForm(Array.Empty<string>());
                formEmpty.CurrentImage.ShouldBeNull();

                using StringWriter outSw = new StringWriter();
                using StringWriter errSw = new StringWriter();

                mockWriter.Object.ShouldNotBeNull();
                Program.DispatchCliCommand(new[] { "--cli" }, outSw, errSw).ShouldBe(1);
                Program.DispatchCliCommand(new[] { "--cli", "unknown" }, outSw, errSw).ShouldBe(1);
                Program.DispatchCliCommand(new[] { "--cli", "cjxl", "--version" }, outSw, errSw).ShouldBe(0);
                Program.DispatchCliCommand(new[] { "--cli", "djxl", "--version" }, outSw, errSw).ShouldBe(0);
                Program.DispatchCliCommand(new[] { "--cli", "jxlinfo", "--help" }, outSw, errSw).ShouldBe(0);
                Program.DispatchCliCommand(new[] { "--cli", "jxltran", "--version" }, outSw, errSw).ShouldBe(0);
                Program.Main(new[] { "--cli", "cjxl", "--version" }).ShouldBe(0);
            }
            finally
            {
                if (File.Exists(tempJpg))
                {
                    File.Delete(tempJpg);
                }
            }
        }
    }
}
