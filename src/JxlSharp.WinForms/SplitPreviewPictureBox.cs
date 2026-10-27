// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// A <see cref="PictureBox"/> control that displays an aligned split comparison between an original image
    /// on the left side and a converted image on the right side, separated by a draggable vertical slider.
    /// </summary>
    public sealed class SplitPreviewPictureBox : PictureBox
    {
        private const int SliderHitTolerancePixels = 10;
        private const int SliderHandleRadius       = 14;

        private Bitmap? _originalImage;
        private Bitmap? _convertedImage;
        private float _splitRatio = 0.5f;
        private bool _showSplitter = true;
        private bool _isDraggingSlider;

        /// <summary>
        /// Initializes a new instance of the <see cref="SplitPreviewPictureBox"/> class.
        /// </summary>
        public SplitPreviewPictureBox()
        {
            DoubleBuffered = true;
        }

        /// <summary>
        /// Occurs when <see cref="SplitRatio"/> changes.
        /// </summary>
        public event EventHandler? SplitRatioChanged;

        /// <summary>
        /// Gets or sets the original loaded image displayed to the left of the vertical slider.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? OriginalImage
        {
            get => _originalImage;
            set
            {
                _originalImage = value;
                _showSplitter  = _originalImage is not null && _convertedImage is not null;
                Image          = _convertedImage ?? _originalImage;
                Invalidate();
            }
        }

        /// <summary>
        /// Gets or sets the converted image displayed to the right of the vertical slider.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? ConvertedImage
        {
            get => _convertedImage;
            set
            {
                _convertedImage = value;
                _showSplitter   = _originalImage is not null && _convertedImage is not null;
                Image           = _convertedImage ?? _originalImage;
                Invalidate();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the vertical split slider and dual-image comparison are shown.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowSplitter
        {
            get => _showSplitter;
            set
            {
                if (_showSplitter == value)
                {
                    return;
                }

                _showSplitter = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Gets or sets the normalized horizontal position (<c>0.0</c> to <c>1.0</c>) of the vertical slider.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float SplitRatio
        {
            get => _splitRatio;
            set
            {
                float clamped = Math.Clamp(value, 0.0f, 1.0f);

                if (Math.Abs(_splitRatio - clamped) < 0.0001f)
                {
                    return;
                }

                _splitRatio = clamped;
                Invalidate();
                SplitRatioChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the user is currently dragging the vertical slider.
        /// </summary>
        public bool IsDraggingSlider => _isDraggingSlider;

        /// <summary>
        /// Displays only the single <paramref name="originalImage"/> without the vertical splitter or comparison image.
        /// </summary>
        /// <param name="originalImage">Original image to display.</param>
        public void SetSingleImage(Bitmap? originalImage)
        {
            _originalImage  = originalImage;
            _convertedImage = null;
            _showSplitter   = false;
            Image           = originalImage;
            Invalidate();
        }

        /// <summary>
        /// Updates both the original and converted comparison images and refreshes the preview.
        /// </summary>
        /// <param name="originalImage">Original loaded image shown on the left side.</param>
        /// <param name="convertedImage">Converted image shown on the right side.</param>
        public void SetComparisonImages(Bitmap? originalImage, Bitmap? convertedImage)
        {
            _originalImage  = originalImage;
            _convertedImage = convertedImage;
            _showSplitter   = originalImage is not null && convertedImage is not null;
            Image           = convertedImage ?? originalImage;
            Invalidate();
        }

        /// <summary>
        /// Computes the destination rectangle within <see cref="Control.ClientRectangle"/> where both aligned images are drawn.
        /// </summary>
        /// <returns>Aligned image display rectangle in client coordinates.</returns>
        public Rectangle GetImageDisplayRectangle()
        {
            return GetImageDisplayRectangle(ClientRectangle);
        }

        /// <summary>
        /// Computes the destination rectangle within <paramref name="bounds"/> where both aligned images are drawn.
        /// </summary>
        /// <param name="bounds">Available canvas bounds.</param>
        /// <returns>Aligned image display rectangle.</returns>
        public Rectangle GetImageDisplayRectangle(Rectangle bounds)
        {
            Image? reference = _originalImage ?? _convertedImage ?? Image;

            if (reference is null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return Rectangle.Empty;
            }

            int imgW = Math.Max(1, reference.Width);
            int imgH = Math.Max(1, reference.Height);

            if (SizeMode == PictureBoxSizeMode.AutoSize || SizeMode == PictureBoxSizeMode.Normal)
            {
                return new Rectangle(bounds.Left, bounds.Top, imgW, imgH);
            }

            if (SizeMode == PictureBoxSizeMode.CenterImage)
            {
                int cx = bounds.Left + ((bounds.Width - imgW) / 2);
                int cy = bounds.Top + ((bounds.Height - imgH) / 2);

                return new Rectangle(cx, cy, imgW, imgH);
            }

            if (SizeMode == PictureBoxSizeMode.StretchImage)
            {
                return bounds;
            }

            return ComputeZoomRectangle(bounds, imgW, imgH);
        }

        /// <summary>
        /// Gets the client X coordinate of the vertical cut line within the current display rectangle.
        /// </summary>
        /// <returns>Client X coordinate of the vertical slider.</returns>
        public int GetSplitLineX()
        {
            return GetSplitLineX(GetImageDisplayRectangle());
        }

        /// <summary>
        /// Gets the X coordinate of the vertical cut line within the specified <paramref name="imageRect"/>.
        /// </summary>
        /// <param name="imageRect">Aligned image display rectangle.</param>
        /// <returns>X coordinate of the vertical slider.</returns>
        public int GetSplitLineX(Rectangle imageRect)
        {
            if (imageRect.Width <= 0)
            {
                return imageRect.Left;
            }

            int offset = (int)Math.Round(imageRect.Width * _splitRatio);

            return imageRect.Left + Math.Clamp(offset, 0, imageRect.Width);
        }

        /// <summary>
        /// Updates <see cref="SplitRatio"/> based on a client X coordinate inside the preview control.
        /// </summary>
        /// <param name="clientX">Mouse or slider X coordinate in client pixels.</param>
        public void UpdateSplitFromClientX(int clientX)
        {
            Rectangle imageRect = GetImageDisplayRectangle();

            if (imageRect.Width <= 0)
            {
                return;
            }

            float ratio = (clientX - imageRect.Left) / (float)imageRect.Width;

            SplitRatio = Math.Clamp(ratio, 0.0f, 1.0f);
        }

        /// <summary>
        /// Renders the split comparison view (original on the left, converted on the right, and vertical slider)
        /// onto the provided <paramref name="graphics"/> surface.
        /// </summary>
        /// <param name="graphics">Target graphics surface.</param>
        /// <param name="bounds">Target canvas bounds.</param>
        public void RenderComparisonToGraphics(Graphics graphics, Rectangle bounds)
        {
            ArgumentNullException.ThrowIfNull(graphics);

            Rectangle destRect = GetImageDisplayRectangle(bounds);

            if (destRect.Width <= 0 || destRect.Height <= 0)
            {
                return;
            }

            if (_showSplitter && _originalImage is not null && _convertedImage is not null)
            {
                int splitX = GetSplitLineX(destRect);

                DrawSplitPair(graphics, _originalImage, _convertedImage, destRect, splitX);
                DrawVerticalSlider(graphics, destRect, splitX);

                return;
            }

            Image? single = _originalImage ?? _convertedImage ?? Image;

            if (single is not null)
            {
                graphics.DrawImage(single, destRect);
            }
        }

        /// <summary>
        /// Renders the current split comparison view into a new <see cref="Bitmap"/> of the specified size.
        /// </summary>
        /// <param name="canvasSize">Output bitmap dimensions.</param>
        /// <returns>Rendered <see cref="Bitmap"/> showing the split comparison.</returns>
        public Bitmap RenderComparisonBitmap(Size canvasSize)
        {
            int width     = Math.Max(1, canvasSize.Width);
            int height    = Math.Max(1, canvasSize.Height);
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(BackColor);
                RenderComparisonToGraphics(g, new Rectangle(0, 0, width, height));
            }

            return result;
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs pe)
        {
            ArgumentNullException.ThrowIfNull(pe);

            if (_originalImage is null && _convertedImage is null)
            {
                base.OnPaint(pe);

                return;
            }

            RenderComparisonToGraphics(pe.Graphics, ClientRectangle);
        }

        /// <inheritdoc />
        protected override void OnMouseDown(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left &&
                _showSplitter &&
                _originalImage is not null &&
                _convertedImage is not null)
            {
                _isDraggingSlider = true;
                Capture           = true;
                Cursor            = Cursors.VSplit;
                UpdateSplitFromClientX(e.X);
            }
        }

        /// <inheritdoc />
        protected override void OnMouseMove(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            base.OnMouseMove(e);

            if (_isDraggingSlider)
            {
                UpdateSplitFromClientX(e.X);

                return;
            }

            UpdateHoverCursor(e.Location);
        }

        /// <inheritdoc />
        protected override void OnMouseUp(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left && _isDraggingSlider)
            {
                _isDraggingSlider = false;
                Capture           = false;
                UpdateHoverCursor(e.Location);
            }
        }

        /// <inheritdoc />
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        private static Rectangle ComputeZoomRectangle(Rectangle bounds, int imgW, int imgH)
        {
            double scaleX = (double)bounds.Width / imgW;
            double scaleY = (double)bounds.Height / imgH;
            double scale  = Math.Min(scaleX, scaleY);
            int drawW     = Math.Max(1, (int)Math.Round(imgW * scale));
            int drawH     = Math.Max(1, (int)Math.Round(imgH * scale));
            int drawX     = bounds.Left + ((bounds.Width - drawW) / 2);
            int drawY     = bounds.Top + ((bounds.Height - drawH) / 2);

            return new Rectangle(drawX, drawY, drawW, drawH);
        }

        private static void DrawSplitPair(
            Graphics graphics,
            Bitmap original,
            Bitmap converted,
            Rectangle destRect,
            int splitX)
        {
            int leftWidth  = Math.Clamp(splitX - destRect.Left, 0, destRect.Width);
            int rightWidth = Math.Clamp(destRect.Right - splitX, 0, destRect.Width);

            if (leftWidth > 0)
            {
                Rectangle leftClip = new Rectangle(destRect.Left, destRect.Top, leftWidth, destRect.Height);

                DrawClippedImage(graphics, original, destRect, leftClip);
            }

            if (rightWidth > 0)
            {
                Rectangle rightClip = new Rectangle(splitX, destRect.Top, rightWidth, destRect.Height);

                DrawClippedImage(graphics, converted, destRect, rightClip);
            }
        }

        private static void DrawClippedImage(Graphics graphics, Bitmap image, Rectangle destRect, Rectangle clipRect)
        {
            GraphicsState state = graphics.Save();

            try
            {
                graphics.SetClip(clipRect, CombineMode.Intersect);
                graphics.DrawImage(image, destRect);
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void DrawVerticalSlider(Graphics graphics, Rectangle destRect, int splitX)
        {
            SmoothingMode prevSmoothing = graphics.SmoothingMode;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                using Pen shadowPen = new Pen(Color.FromArgb(160, 0, 0, 0), 3.0f);
                using Pen linePen   = new Pen(Color.White, 1.5f);

                graphics.DrawLine(shadowPen, splitX, destRect.Top, splitX, destRect.Bottom);
                graphics.DrawLine(linePen, splitX, destRect.Top, splitX, destRect.Bottom);

                DrawSliderGrip(graphics, splitX, destRect.Top + (destRect.Height / 2));
            }
            finally
            {
                graphics.SmoothingMode = prevSmoothing;
            }
        }

        private static void DrawSliderGrip(Graphics graphics, int centerX, int centerY)
        {
            Rectangle gripRect = new Rectangle(
                centerX - SliderHandleRadius,
                centerY - SliderHandleRadius,
                SliderHandleRadius * 2,
                SliderHandleRadius * 2);

            using SolidBrush fillBrush = new SolidBrush(Color.FromArgb(210, 32, 32, 32));
            using Pen borderPen        = new Pen(Color.White, 1.5f);
            using Pen arrowPen         = new Pen(Color.White, 1.5f);

            graphics.FillEllipse(fillBrush, gripRect);
            graphics.DrawEllipse(borderPen, gripRect);

            // Left chevron
            graphics.DrawLine(arrowPen, centerX - 3, centerY - 4, centerX - 7, centerY);
            graphics.DrawLine(arrowPen, centerX - 7, centerY, centerX - 3, centerY + 4);

            // Right chevron
            graphics.DrawLine(arrowPen, centerX + 3, centerY - 4, centerX + 7, centerY);
            graphics.DrawLine(arrowPen, centerX + 7, centerY, centerX + 3, centerY + 4);
        }

        private void UpdateHoverCursor(Point location)
        {
            if (!_showSplitter || _originalImage is null || _convertedImage is null)
            {
                Cursor = Cursors.Default;

                return;
            }

            Rectangle imageRect = GetImageDisplayRectangle();

            if (imageRect.Contains(location) && Math.Abs(location.X - GetSplitLineX(imageRect)) <= SliderHitTolerancePixels)
            {
                Cursor = Cursors.VSplit;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
