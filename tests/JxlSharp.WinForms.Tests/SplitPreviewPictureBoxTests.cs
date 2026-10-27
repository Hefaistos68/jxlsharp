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
using System.Windows.Forms;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Unit tests for <see cref="SplitPreviewPictureBox"/>.
    /// </summary>
    [TestFixture]
    public sealed class SplitPreviewPictureBoxTests
    {
        /// <summary>
        /// Verifies that <see cref="SplitPreviewPictureBox"/> renders the original image on the left side
        /// of the vertical slider and the converted image on the right side, aligned so that the slider
        /// is the exact line where the image is cut.
        /// </summary>
        [Test]
        public void RenderComparisonBitmapAlignsOriginalLeftAndConvertedRightAtSliderCutLine()
        {
            Mock<IWin32Window> mockWindow        = new Mock<IWin32Window>();
            using SplitPreviewPictureBox preview = new SplitPreviewPictureBox
            {
                Size      = new Size(100, 100),
                SizeMode  = PictureBoxSizeMode.AutoSize,
                BackColor = Color.Black,
            };

            using Bitmap originalBmp  = CreateSolidBitmap(100, 100, Color.FromArgb(255, 220, 20, 20));
            using Bitmap convertedBmp = CreateSolidBitmap(100, 100, Color.FromArgb(255, 20, 60, 220));

            preview.SetComparisonImages(originalBmp, convertedBmp);
            preview.SplitRatio = 0.4f;

            mockWindow.Object.ShouldNotBeNull();
            preview.OriginalImage.ShouldBeSameAs(originalBmp);
            preview.ConvertedImage.ShouldBeSameAs(convertedBmp);
            preview.GetSplitLineX(new Rectangle(0, 0, 100, 100)).ShouldBe(40);

            using Bitmap rendered = preview.RenderComparisonBitmap(new Size(100, 100));

            Color leftPixel  = rendered.GetPixel(15, 20);
            Color rightPixel = rendered.GetPixel(75, 20);

            leftPixel.R.ShouldBeGreaterThan((byte)180);
            leftPixel.B.ShouldBeLessThan((byte)60);

            rightPixel.B.ShouldBeGreaterThan((byte)180);
            rightPixel.R.ShouldBeLessThan((byte)60);
        }

        /// <summary>
        /// Verifies display rectangle calculation across all <see cref="PictureBoxSizeMode"/> modes,
        /// slider ratio clamping, and mouse drag interaction.
        /// </summary>
        [Test]
        public void SplitRatioMouseInteractionAndSizeModesWorkAsExpected()
        {
            Mock<EventHandler> mockHandler       = new Mock<EventHandler>();
            using SplitPreviewPictureBox preview = new SplitPreviewPictureBox
            {
                Size     = new Size(200, 100),
                SizeMode = PictureBoxSizeMode.Zoom,
            };

            preview.SplitRatioChanged += mockHandler.Object;

            using Bitmap origBmp = CreateSolidBitmap(100, 100, Color.Green);
            using Bitmap convBmp = CreateSolidBitmap(100, 100, Color.Yellow);

            preview.OriginalImage  = origBmp;
            preview.ConvertedImage = convBmp;

            Rectangle zoomRect = preview.GetImageDisplayRectangle(new Rectangle(0, 0, 200, 100));

            zoomRect.X.ShouldBe(50);
            zoomRect.Width.ShouldBe(100);
            zoomRect.Height.ShouldBe(100);

            preview.UpdateSplitFromClientX(75);
            preview.SplitRatio.ShouldBe(0.25f, 0.01f);
            mockHandler.Verify(h => h(preview, EventArgs.Empty), Times.AtLeastOnce);

            preview.SplitRatio = -1.0f;
            preview.SplitRatio.ShouldBe(0.0f);
            preview.SplitRatio = 5.0f;
            preview.SplitRatio.ShouldBe(1.0f);

            VerifySizeModesAndMouseEvents(preview);
        }

        private static void VerifySizeModesAndMouseEvents(SplitPreviewPictureBox preview)
        {
            preview.SizeMode = PictureBoxSizeMode.CenterImage;
            preview.GetImageDisplayRectangle(new Rectangle(0, 0, 200, 100)).X.ShouldBe(50);

            preview.SizeMode = PictureBoxSizeMode.StretchImage;
            preview.GetImageDisplayRectangle(new Rectangle(0, 0, 200, 100)).Width.ShouldBe(200);

            preview.SizeMode = PictureBoxSizeMode.Normal;
            preview.GetImageDisplayRectangle(new Rectangle(0, 0, 200, 100)).Width.ShouldBe(100);

            SimulateMouseDrag(preview, 20, 80);
            preview.IsDraggingSlider.ShouldBeFalse();
            preview.SplitRatio.ShouldBe(0.8f, 0.02f);
        }

        private static void SimulateMouseDrag(SplitPreviewPictureBox preview, int startX, int endX)
        {
            using ControlTester tester = new ControlTester(preview);

            tester.TriggerMouseDown(new MouseEventArgs(MouseButtons.Left, 1, startX, 50, 0));
            preview.IsDraggingSlider.ShouldBeTrue();

            tester.TriggerMouseMove(new MouseEventArgs(MouseButtons.Left, 0, endX, 50, 0));
            tester.TriggerMouseUp(new MouseEventArgs(MouseButtons.Left, 1, endX, 50, 0));
            tester.TriggerMouseMove(new MouseEventArgs(MouseButtons.None, 0, endX, 50, 0));
        }

        private static Bitmap CreateSolidBitmap(int width, int height, Color color)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
            }

            return bmp;
        }

        private sealed class ControlTester : IDisposable
        {
            private readonly SplitPreviewPictureBox _target;

            public ControlTester(SplitPreviewPictureBox target)
            {
                _target = target;
            }

            public void TriggerMouseDown(MouseEventArgs e)
            {
                typeof(Control)
                    .GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(_target, new object[] { e });
            }

            public void TriggerMouseMove(MouseEventArgs e)
            {
                typeof(Control)
                    .GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(_target, new object[] { e });
            }

            public void TriggerMouseUp(MouseEventArgs e)
            {
                typeof(Control)
                    .GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(_target, new object[] { e });
            }

            public void Dispose()
            {
            }
        }
    }
}
