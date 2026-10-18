// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Bundles source image parameters for <see cref="ImageBundle.ApplyColorTransform"/>.
    /// </summary>
    public readonly struct ColorTransformSource
    {
        /// <summary>Source color encoding.</summary>
        public readonly ColorEncoding CCurrent;

        /// <summary>Intensity target in nits.</summary>
        public readonly float IntensityTarget;

        /// <summary>Source 3-plane color image.</summary>
        public readonly Image3<float> Color;

        /// <summary>Optional CMYK black plane.</summary>
        public readonly ImagePlane<float>? Black;

        /// <summary>Rectangular region to transform.</summary>
        public readonly Rect Region;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorTransformSource"/> struct.
        /// </summary>
        /// <param name="cCurrent">Source color encoding.</param>
        /// <param name="intensityTarget">Intensity target in nits.</param>
        /// <param name="color">Source 3-plane color image.</param>
        /// <param name="black">Optional CMYK black plane.</param>
        /// <param name="region">Rectangular region to transform.</param>
        public ColorTransformSource(
            ColorEncoding cCurrent,
            float intensityTarget,
            Image3<float> color,
            ImagePlane<float>? black,
            Rect region)
        {
            CCurrent        = cCurrent;
            IntensityTarget = intensityTarget;
            Color           = color;
            Black           = black;
            Region          = region;
        }
    }

    /// <summary>
    /// A bundle of color, alpha, depth, and extra channel plane images for a frame.
    /// Ported from <c>jxl::ImageBundle</c> in <c>lib/jxl/image_bundle.h</c>, <c>lib/jxl/image_bundle.cc</c>,
    /// <c>lib/jxl/enc_image_bundle.h</c>, and <c>lib/jxl/enc_image_bundle.cc</c>.
    /// </summary>
    public sealed class ImageBundle : IDisposable
    {
        private readonly ImageMetadata? _metadata;
        private Image3<float> _color = new Image3<float>();
        private ColorEncoding _cCurrent = new ColorEncoding();
        private List<ImagePlane<float>> _extraChannels = new List<ImagePlane<float>>();
        private nuint _decodedBytes;

        /// <summary>Optional JPEG reconstruction data when transcoding to/from JPEG.</summary>
        public JPEGData? JpegData;

        /// <summary>Input JPEG color space transform (when transcoding JPEG).</summary>
        public ColorTransform ColorTransform = ColorTransform.None;

        /// <summary>Input YCbCr chroma subsampling.</summary>
        public YCbCrChromaSubsampling ChromaSubsampling = new YCbCrChromaSubsampling();

        /// <summary>Returns whether this bundle carries JPEG DCT coefficients (<see cref="JpegData"/> is non-null).</summary>
        /// <returns><c>true</c> if <see cref="JpegData"/> is present.</returns>
        public bool IsJPEG() => JpegData != null;

        /// <summary>Frame origin within the canvas.</summary>
        public FrameOrigin Origin = new FrameOrigin();

        /// <summary>Animation frame duration in ticks.</summary>
        public uint Duration;

        /// <summary>Animation frame timecode.</summary>
        public uint Timecode;

        /// <summary>Whether this frame is saved as a reference for the next frame.</summary>
        public bool UseForNextFrame;

        /// <summary>Whether blending is enabled for this frame.</summary>
        public bool Blend;

        /// <summary>Blending mode for this frame.</summary>
        public BlendMode BlendMode = BlendMode.Blend;

        /// <summary>Frame name.</summary>
        public string Name = string.Empty;

        /// <summary>
        /// Initializes a new uninitialized instance of the <see cref="ImageBundle"/> class for use as an output parameter.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public ImageBundle(JxlMemoryManager? memoryManager)
        {
            MemoryManager = memoryManager;
            _metadata     = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageBundle"/> class associated with <paramref name="metadata"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="metadata">Associated image metadata.</param>
        public ImageBundle(JxlMemoryManager? memoryManager, ImageMetadata? metadata)
        {
            MemoryManager = memoryManager;
            _metadata     = metadata;
        }

        /// <summary>Gets the memory manager associated with this bundle.</summary>
        public JxlMemoryManager? MemoryManager { get; }

        /// <summary>Gets the associated <see cref="ImageMetadata"/>.</summary>
        public ImageMetadata? Metadata => _metadata;

        /// <summary>Gets the current <see cref="ColorEncoding"/> of the color planes.</summary>
        public ColorEncoding CCurrent => _cCurrent;

        /// <summary>Gets the extra channel planes.</summary>
        public List<ImagePlane<float>> ExtraChannels => _extraChannels;

        /// <summary>Gets the number of input bytes read to decode this bundle.</summary>
        public nuint DecodedBytes => _decodedBytes;

        /// <summary>
        /// Creates a deep copy of this <see cref="ImageBundle"/>.
        /// </summary>
        /// <returns><see cref="StatusOr{ImageBundle}"/> containing the cloned bundle.</returns>
        public StatusOr<ImageBundle> Copy()
        {
            ImageBundle copy = new ImageBundle(MemoryManager, _metadata);

            if (HasColor())
            {
                StatusOr<Image3<float>> colorOr = Image3<float>.Create(MemoryManager, _color.XSize, _color.YSize);

                if (!colorOr.IsOk)
                {
                    return colorOr.Status;
                }

                copy._color = colorOr.Value;

                if (!ImageOps.CopyImageTo(_color, copy._color))
                {
                    copy.Dispose();

                    return Status.Failure("Failed to copy color planes");
                }
            }

            copy._cCurrent = _cCurrent.Clone();

            foreach (ImagePlane<float> plane in _extraChannels)
            {
                StatusOr<ImagePlane<float>> ecOr = ImagePlane<float>.Create(MemoryManager, plane.XSize, plane.YSize);

                if (!ecOr.IsOk)
                {
                    copy.Dispose();

                    return ecOr.Status;
                }

                ImagePlane<float> ec = ecOr.Value;

                if (!ImageOps.CopyImageTo(plane, ec))
                {
                    ec.Dispose();
                    copy.Dispose();

                    return Status.Failure("Failed to copy extra channel plane");
                }

                copy._extraChannels.Add(ec);
            }

            copy.ColorTransform    = ColorTransform;
            copy.ChromaSubsampling = ChromaSubsampling;
            copy.Origin            = Origin;
            copy.Duration          = Duration;
            copy.Timecode          = Timecode;
            copy.UseForNextFrame   = UseForNextFrame;
            copy.Blend             = Blend;
            copy.BlendMode         = BlendMode;
            copy.Name              = Name;

            return copy;
        }

        /// <summary>Gets the width in pixels.</summary>
        /// <returns>Width in pixels.</returns>
        public nuint XSize()
        {
            if (_color.XSize != 0)
            {
                return _color.XSize;
            }

            return _extraChannels.Count == 0 ? 0 : _extraChannels[0].XSize;
        }

        /// <summary>Gets the height in pixels.</summary>
        /// <returns>Height in pixels.</returns>
        public nuint YSize()
        {
            if (_color.YSize != 0)
            {
                return _color.YSize;
            }

            return _extraChannels.Count == 0 ? 0 : _extraChannels[0].YSize;
        }

        /// <summary>
        /// Shrinks the color and extra channel planes to <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        /// <param name="xsize">New logical width.</param>
        /// <param name="ysize">New logical height.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status ShrinkTo(nuint xsize, nuint ysize)
        {
            if (HasColor() && !_color.ShrinkTo(xsize, ysize))
            {
                return false;
            }

            foreach (ImagePlane<float> ec in _extraChannels)
            {
                if (!ec.ShrinkTo(xsize, ysize))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Gets the width taking EXIF orientation into account.</summary>
        /// <returns>Oriented width in pixels.</returns>
        public nuint OrientedXSize()
        {
            if (_metadata != null && (uint)_metadata.GetOrientation() > 4u)
            {
                return YSize();
            }

            return XSize();
        }

        /// <summary>Gets the height taking EXIF orientation into account.</summary>
        /// <returns>Oriented height in pixels.</returns>
        public nuint OrientedYSize()
        {
            if (_metadata != null && (uint)_metadata.GetOrientation() > 4u)
            {
                return XSize();
            }

            return YSize();
        }

        /// <summary>Returns whether <see cref="Color"/> is valid/usable.</summary>
        /// <returns><c>true</c> if color planes are allocated.</returns>
        public bool HasColor() => _color.XSize != 0;

        /// <summary>Resets and disposes the color planes.</summary>
        public void RemoveColor()
        {
            _color.Dispose();
            _color = new Image3<float>();
        }

        /// <summary>Gets the 3-plane color image (valid only when <see cref="HasColor"/> is <c>true</c>).</summary>
        /// <returns>The 3-plane color image.</returns>
        public Image3<float> Color()
        {
            Debug.Assert(HasColor());

            return _color;
        }

        /// <summary>
        /// Sets the color planes and current color encoding.
        /// </summary>
        /// <param name="color">3-plane color image.</param>
        /// <param name="cCurrent">Color encoding of <paramref name="color"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetFromImage(Image3<float> color, ColorEncoding cCurrent)
        {
            if (color.XSize == 0 || color.YSize == 0)
            {
                return Status.Failure("Color image dimensions must be non-zero");
            }

            if (_metadata != null && _metadata.ColorEncoding.IsGray() != cCurrent.IsGray())
            {
                return Status.Failure("Grayscale mismatch between metadata and c_current");
            }

            if (!ReferenceEquals(_color, color))
            {
                _color.Dispose();
                _color = color;
            }

            _cCurrent = cCurrent.Clone();

            return VerifySizes();
        }

        /// <summary>Returns whether the current color encoding is grayscale.</summary>
        public bool IsGray() => _cCurrent.IsGray();

        /// <summary>Returns whether the current color encoding is sRGB.</summary>
        public bool IsSRGB() => _cCurrent.IsSRGB();

        /// <summary>Returns whether the current color encoding is linear sRGB.</summary>
        public bool IsLinearSRGB() => _cCurrent.IsLinearSRGB();

        /// <summary>
        /// Sets the current color profile without performing any pixel transformation.
        /// </summary>
        /// <param name="newCCurrent">New color encoding.</param>
        public void OverrideProfile(ColorEncoding newCCurrent)
        {
            _cCurrent = newCCurrent.Clone();
        }

        /// <summary>
        /// Transforms the color planes in-place to <paramref name="cDesired"/> and updates <see cref="CCurrent"/>.
        /// </summary>
        /// <param name="cDesired">Target color encoding.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status TransformTo(ColorEncoding cDesired, JxlCmsInterface cms, ThreadPool? pool = null)
        {
            if (!CopyTo(new Rect(_color), cDesired, cms, ref _color, pool))
            {
                return false;
            }

            _cCurrent = cDesired.Clone();

            return true;
        }

        /// <summary>
        /// Copies <paramref name="rect"/> of this bundle, converts to <paramref name="cDesired"/>, and writes to <paramref name="output"/>.
        /// </summary>
        /// <param name="rect">Source region.</param>
        /// <param name="cDesired">Target color encoding.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="output">Destination 3-plane image.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status CopyTo(
            Rect rect,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ref Image3<float> output,
            ThreadPool? pool = null)
        {
            float intensityTarget = _metadata != null
                ? _metadata.IntensityTarget()
                : Common.kDefaultIntensityTarget;

            ColorTransformSource src = new ColorTransformSource(
                _cCurrent,
                intensityTarget,
                _color,
                Black(),
                rect);

            return ApplyColorTransform(in src, cDesired, cms, pool, ref output);
        }

        /// <summary>
        /// Applies a color space transformation from <paramref name="src"/> to <paramref name="cDesired"/>.
        /// Ported from <c>jxl::ApplyColorTransform</c> in <c>lib/jxl/enc_image_bundle.cc</c>.
        /// </summary>
        /// <param name="src">Source color image, encoding, intensity target, and region.</param>
        /// <param name="cDesired">Destination color encoding.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Output 3-plane float image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ApplyColorTransform(
            in ColorTransformSource src,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ThreadPool? pool,
            ref Image3<float> output)
        {
            if (src.CCurrent.IsGray() != cDesired.IsGray())
            {
                return Status.Failure("Grayscale mismatch in ApplyColorTransform");
            }

            nuint reqX = (nuint)src.Region.XSize;
            nuint reqY = (nuint)src.Region.YSize;

            if (output.XSize < reqX || output.YSize < reqY)
            {
                StatusOr<Image3<float>> created = Image3<float>.Create(src.Color.MemoryManager, reqX, reqY);

                if (!created.IsOk)
                {
                    return created.Status;
                }

                output.Dispose();
                output = created.Value;
            }
            else if (!output.ShrinkTo(reqX, reqY))
            {
                return false;
            }

            return RunColorTransformRows(in src, cDesired, cms, pool, output);
        }

        /// <summary>
        /// Transforms <paramref name="input"/> to <paramref name="cDesired"/> into <paramref name="store"/> if needed,
        /// or sets <paramref name="output"/> directly to <paramref name="input"/> if already in <paramref name="cDesired"/>.
        /// Ported from <c>jxl::TransformIfNeeded</c> in <c>lib/jxl/enc_image_bundle.cc</c>.
        /// </summary>
        /// <param name="input">Input image bundle.</param>
        /// <param name="cDesired">Desired color encoding.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="store">Scratch bundle used when transformation is required.</param>
        /// <param name="output">Resulting bundle reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status TransformIfNeeded(
            ImageBundle input,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ThreadPool? pool,
            ImageBundle store,
            out ImageBundle output)
        {
            output = input;

            if (input.CCurrent.SameColorEncoding(cDesired) && !input.HasBlack())
            {
                return true;
            }

            StatusOr<Image3<float>> colorOr = Image3<float>.Create(
                input.MemoryManager,
                input.Color().XSize,
                input.Color().YSize);

            if (!colorOr.IsOk)
            {
                return colorOr.Status;
            }

            Image3<float> color = colorOr.Value;

            if (!ImageOps.CopyImageTo(input.Color(), color) ||
                !store.SetFromImage(color, input.CCurrent))
            {
                color.Dispose();

                return false;
            }

            if (input.HasExtraChannels() && !CopyExtraChannelsToStore(input, store))
            {
                return false;
            }

            if (!store.TransformTo(cDesired, cms, pool))
            {
                return false;
            }

            output = store;

            return true;
        }

        /// <summary>Detects the effective bit depth of the image.</summary>
        /// <returns>Nominal bits per sample.</returns>
        public nuint DetectRealBitdepth()
        {
            return _metadata != null ? _metadata.BitDepth.BitsPerSample : 0;
        }

        /// <summary>Returns whether metadata defines an alpha extra channel.</summary>
        public bool HasAlpha() => _metadata?.Find(ExtraChannel.Alpha) != null;

        /// <summary>Returns whether the alpha channel is premultiplied.</summary>
        public bool AlphaIsPremultiplied()
        {
            ExtraChannelInfo? eci = _metadata?.Find(ExtraChannel.Alpha);

            return eci != null && eci.AlphaAssociated;
        }

        /// <summary>Gets the alpha channel plane, or <c>null</c> if none exists.</summary>
        public ImagePlane<float>? Alpha()
        {
            if (_metadata == null || !HasAlpha())
            {
                return null;
            }

            int idx = FindExtraChannelIndex(ExtraChannel.Alpha);
            Debug.Assert(idx >= 0 && idx < _extraChannels.Count);

            return idx >= 0 && idx < _extraChannels.Count ? _extraChannels[idx] : null;
        }

        /// <summary>
        /// Appends <paramref name="alpha"/> as the alpha extra channel plane.
        /// </summary>
        /// <param name="alpha">Alpha plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetAlpha(ImagePlane<float> alpha)
        {
            if (_metadata == null)
            {
                return Status.Failure("Metadata is null");
            }

            int eciIdx = FindExtraChannelIndex(ExtraChannel.Alpha);

            if (eciIdx < 0 || alpha.XSize == 0 || alpha.YSize == 0)
            {
                return Status.Failure("Missing alpha channel info or empty alpha plane");
            }

            if (eciIdx != _extraChannels.Count)
            {
                return Status.Failure("Incorrect SetAlpha call");
            }

            _extraChannels.Add(alpha);

            return VerifySizes();
        }

        /// <summary>Returns whether metadata defines a CMYK black extra channel.</summary>
        public bool HasBlack() => _metadata?.Find(ExtraChannel.Black) != null;

        /// <summary>Gets the CMYK black channel plane, or <c>null</c> if none exists.</summary>
        public ImagePlane<float>? Black()
        {
            if (_metadata == null || !HasBlack())
            {
                return null;
            }

            int idx = FindExtraChannelIndex(ExtraChannel.Black);
            Debug.Assert(idx >= 0 && idx < _extraChannels.Count);

            return idx >= 0 && idx < _extraChannels.Count ? _extraChannels[idx] : null;
        }

        /// <summary>
        /// Replaces the extra channel planes with <paramref name="extraChannels"/>.
        /// </summary>
        /// <param name="extraChannels">Non-empty extra channel planes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetExtraChannels(List<ImagePlane<float>> extraChannels)
        {
            foreach (ImagePlane<float> plane in extraChannels)
            {
                if (plane.XSize == 0 || plane.YSize == 0)
                {
                    return Status.Failure("Extra channel plane has zero dimension");
                }
            }

            ClearExtraChannels();
            _extraChannels = extraChannels;

            return VerifySizes();
        }

        /// <summary>Disposes and clears all extra channel planes.</summary>
        public void ClearExtraChannels()
        {
            foreach (ImagePlane<float> ec in _extraChannels)
            {
                ec.Dispose();
            }

            _extraChannels.Clear();
        }

        /// <summary>Returns whether any extra channel planes are present.</summary>
        public bool HasExtraChannels() => _extraChannels.Count != 0;

        /// <summary>
        /// Verifies consistency between <see cref="Metadata"/> and this bundle's color/alpha state.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public Status VerifyMetadata()
        {
            if (_metadata == null)
            {
                return Status.Failure("Metadata is null");
            }

            if (_cCurrent.ICC().Length == 0 || _metadata.ColorEncoding.IsGray() != IsGray())
            {
                return Status.Failure("ColorEncoding mismatch in VerifyMetadata");
            }

            if (_metadata.HasAlpha())
            {
                ImagePlane<float>? a = Alpha();

                if (a == null || a.XSize == 0)
                {
                    return Status.Failure("Missing or empty alpha plane");
                }
            }

            return _metadata.GetAlphaBits() <= 32u;
        }

        /// <summary>Sets the number of input bytes consumed for this bundle.</summary>
        /// <param name="decodedBytes">Consumed byte count.</param>
        public void SetDecodedBytes(nuint decodedBytes)
        {
            _decodedBytes = decodedBytes;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _color.Dispose();
            ClearExtraChannels();
        }

        private static Status CopyExtraChannelsToStore(ImageBundle input, ImageBundle store)
        {
            List<ImagePlane<float>> extraChannels = new List<ImagePlane<float>>(input.ExtraChannels.Count);

            foreach (ImagePlane<float> extraChannel in input.ExtraChannels)
            {
                StatusOr<ImagePlane<float>> ecOr = ImagePlane<float>.Create(
                    input.MemoryManager,
                    extraChannel.XSize,
                    extraChannel.YSize);

                if (!ecOr.IsOk)
                {
                    foreach (ImagePlane<float> item in extraChannels)
                    {
                        item.Dispose();
                    }

                    return ecOr.Status;
                }

                ImagePlane<float> ec = ecOr.Value;

                if (!ImageOps.CopyImageTo(extraChannel, ec))
                {
                    ec.Dispose();

                    foreach (ImagePlane<float> item in extraChannels)
                    {
                        item.Dispose();
                    }

                    return false;
                }

                extraChannels.Add(ec);
            }

            return store.SetExtraChannels(extraChannels);
        }

        private static Status RunColorTransformRows(
            in ColorTransformSource src,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ThreadPool? pool,
            Image3<float> output)
        {
            using ColorSpaceTransform cTransform = new ColorSpaceTransform(cms);
            ColorTransformSource capturedSrc     = src;
            nuint width                          = (nuint)src.Region.XSize;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)src.Region.YSize,
                numThreads => cTransform.Init(capturedSrc.CCurrent, cDesired, capturedSrc.IntensityTarget, width, numThreads),
                (y, thread) => TransformSingleRow(in capturedSrc, cTransform, output, y, thread),
                "Colorspace transform");
        }

        private static unsafe Status TransformSingleRow(
            in ColorTransformSource src,
            ColorSpaceTransform cTransform,
            Image3<float> output,
            uint y,
            nuint thread)
        {
            float[]? mutableSrcBuf = cTransform.BufSrc(thread);
            float[]? dstBuf        = cTransform.BufDst(thread);
            int xsize              = src.Region.XSize;

            if (mutableSrcBuf == null || dstBuf == null)
            {
                return Status.Failure("CMS buffers are null");
            }

            if (!InterleaveInputRow(in src, y, xsize, mutableSrcBuf))
            {
                return false;
            }

            if (!cTransform.Run(thread, mutableSrcBuf.AsSpan(0, xsize * (int)src.CCurrent.Channels()), dstBuf, (nuint)xsize))
            {
                return false;
            }

            DeinterleaveOutputRow(src.CCurrent.IsGray(), y, xsize, dstBuf, output);

            return true;
        }

        private static unsafe Status InterleaveInputRow(
            in ColorTransformSource src,
            uint y,
            int xsize,
            float[] mutableSrcBuf)
        {
            if (src.CCurrent.IsGray())
            {
                float* rowIn0 = src.Region.ConstPlaneRow(src.Color, 0, y);

                for (int x = 0; x < xsize; x++)
                {
                    mutableSrcBuf[x] = rowIn0[x];
                }

                return true;
            }

            if (src.CCurrent.IsCMYK())
            {
                if (src.Black == null)
                {
                    return Status.Failure("Black plane is missing for CMYK transform");
                }

                float* rowC = src.Region.ConstPlaneRow(src.Color, 0, y);
                float* rowM = src.Region.ConstPlaneRow(src.Color, 1, y);
                float* rowY = src.Region.ConstPlaneRow(src.Color, 2, y);
                float* rowK = src.Region.ConstRow(src.Black, y);

                for (int x = 0; x < xsize; x++)
                {
                    mutableSrcBuf[(4 * x) + 0] = rowC[x];
                    mutableSrcBuf[(4 * x) + 1] = rowM[x];
                    mutableSrcBuf[(4 * x) + 2] = rowY[x];
                    mutableSrcBuf[(4 * x) + 3] = rowK[x];
                }

                return true;
            }

            float* r0 = src.Region.ConstPlaneRow(src.Color, 0, y);
            float* r1 = src.Region.ConstPlaneRow(src.Color, 1, y);
            float* r2 = src.Region.ConstPlaneRow(src.Color, 2, y);

            for (int x = 0; x < xsize; x++)
            {
                mutableSrcBuf[(3 * x) + 0] = r0[x];
                mutableSrcBuf[(3 * x) + 1] = r1[x];
                mutableSrcBuf[(3 * x) + 2] = r2[x];
            }

            return true;
        }

        private static unsafe void DeinterleaveOutputRow(
            bool isGray,
            uint y,
            int xsize,
            float[] dstBuf,
            Image3<float> output)
        {
            float* rowOut0 = output.PlaneRow(0, y);
            float* rowOut1 = output.PlaneRow(1, y);
            float* rowOut2 = output.PlaneRow(2, y);

            if (isGray)
            {
                for (int x = 0; x < xsize; x++)
                {
                    float v    = dstBuf[x];
                    rowOut0[x] = v;
                    rowOut1[x] = v;
                    rowOut2[x] = v;
                }
            }
            else
            {
                for (int x = 0; x < xsize; x++)
                {
                    rowOut0[x] = dstBuf[(3 * x) + 0];
                    rowOut1[x] = dstBuf[(3 * x) + 1];
                    rowOut2[x] = dstBuf[(3 * x) + 2];
                }
            }
        }

        private int FindExtraChannelIndex(ExtraChannel type)
        {
            if (_metadata == null)
            {
                return -1;
            }

            for (int i = 0; i < _metadata.ExtraChannelInfo.Count; i++)
            {
                if (_metadata.ExtraChannelInfo[i].Type == type)
                {
                    return i;
                }
            }

            return -1;
        }

        private Status VerifySizes()
        {
            nuint xs = XSize();
            nuint ys = YSize();

            if (HasExtraChannels())
            {
                if (xs == 0 || ys == 0)
                {
                    return Status.Failure("ImageBundle has zero dimensions with extra channels");
                }

                foreach (ImagePlane<float> ec in _extraChannels)
                {
                    if (ec.XSize != xs || ec.YSize != ys)
                    {
                        return Status.Failure("Extra channel dimensions do not match bundle dimensions");
                    }
                }
            }

            return true;
        }
    }
}
