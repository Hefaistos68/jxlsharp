// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Converts between <see cref="PackedPixelFile"/>, <see cref="CodecInOut"/>, and <see cref="Image3{T}"/>.
    /// Ported from <c>lib/extras/packed_image_convert.h</c> and <c>lib/extras/packed_image_convert.cc</c>.
    /// </summary>
    public static class PackedImageConvert
    {
        /// <summary>
        /// Converts an external <see cref="PackedPixelFile"/> into an internal <see cref="CodecInOut"/> for encoding or processing.
        /// Ported from <c>jxl::extras::ConvertPackedPixelFileToCodecInOut</c> in <c>lib/extras/packed_image_convert.cc</c>.
        /// </summary>
        /// <param name="ppf">Source packed pixel file.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="io">Destination codec container.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise failure.</returns>
        public static Status ConvertPackedPixelFileToCodecInOut(
            PackedPixelFile ppf,
            ThreadPool? pool,
            CodecInOut io)
        {
            if (ppf == null)
            {
                throw new ArgumentNullException(nameof(ppf));
            }

            if (io == null)
            {
                throw new ArgumentNullException(nameof(io));
            }

            if (!ValidateAndCopyBasicMetadata(ppf, io) ||
                !ApplyPpfBitDepthAndAlpha(ppf, io) ||
                !ApplyPpfColorSpace(ppf, io) ||
                !ConvertPpfPreviewAndFrames(ppf, pool, io))
            {
                return false;
            }

            ApplyPpfLuminanceAndAnimation(ppf, io);

            return io.CheckMetadata();
        }

        /// <summary>
        /// Converts an internal <see cref="CodecInOut"/> into an external <see cref="PackedPixelFile"/>.
        /// Ported from <c>jxl::extras::ConvertCodecInOutToPackedPixelFile</c> in <c>lib/extras/packed_image_convert.cc</c>.
        /// </summary>
        /// <param name="io">Source codec container.</param>
        /// <param name="pixelFormat">Target packed pixel format.</param>
        /// <param name="cDesired">Desired target color encoding.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise failure.</returns>
        public static Status ConvertCodecInOutToPackedPixelFile(
            CodecInOut io,
            in JxlPixelFormat pixelFormat,
            ColorEncoding cDesired,
            ThreadPool? pool,
            PackedPixelFile ppf)
        {
            if (io == null)
            {
                throw new ArgumentNullException(nameof(io));
            }

            if (cDesired == null)
            {
                throw new ArgumentNullException(nameof(cDesired));
            }

            if (ppf == null)
            {
                throw new ArgumentNullException(nameof(ppf));
            }

            if (!PopulatePpfHeaderFromCodecInOut(io, in pixelFormat, cDesired, ppf))
            {
                return false;
            }

            return ConvertCodecInOutFramesToPpf(io, in pixelFormat, cDesired, pool, ppf);
        }

        /// <summary>
        /// Converts a 3-plane floating-point image <paramref name="image"/> into a <see cref="PackedPixelFile"/>.
        /// Ported from <c>jxl::extras::ConvertImage3FToPackedPixelFile</c> in <c>lib/extras/packed_image_convert.cc</c>.
        /// </summary>
        /// <param name="image">Source 3-plane float image.</param>
        /// <param name="cEnc">Color encoding of <paramref name="image"/>.</param>
        /// <param name="format">Target packed pixel format.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Converted <see cref="PackedPixelFile"/> on success; otherwise failure status.</returns>
        public static StatusOr<PackedPixelFile> ConvertImage3FToPackedPixelFile(
            Image3<float> image,
            ColorEncoding cEnc,
            JxlPixelFormat format,
            ThreadPool? pool)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (cEnc == null)
            {
                throw new ArgumentNullException(nameof(cEnc));
            }

            if (format.NumChannels == 0 || format.NumChannels > 3)
            {
                return Status.Failure("Invalid number of channels for Image3F");
            }

            JxlColorEncoding extColor = cEnc.ToExternal();

            PackedPixelFile ppf       = new PackedPixelFile();
            ppf.Info.XSize            = (uint)image.XSize;
            ppf.Info.YSize            = (uint)image.YSize;
            ppf.Info.NumColorChannels = (uint)cEnc.Channels();
            ppf.ColorEncoding         = extColor;
            ppf.Info.BitsPerSample    = (uint)PackedImage.BitsPerChannel(format.DataType);

            if (!TryGetExponentBits(format.DataType, out uint exponentBits))
            {
                return Status.Failure("JxlPixelFormat data type not supported");
            }

            ppf.Info.ExponentBitsPerSample = exponentBits;

            StatusOr<PackedFrame> frameOr = PackedFrame.Create(image.XSize, image.YSize, in format);

            if (!frameOr.IsOk)
            {
                return frameOr.Status;
            }

            PackedFrame frame             = frameOr.Value;
            ImagePlane<float>?[] channels = new ImagePlane<float>?[DecExternalImage.kMaxChannels];

            for (int c = 0; c < (int)format.NumChannels; ++c)
            {
                channels[c] = image.Plane((nuint)c);
            }

            bool floatOut = format.DataType == JxlDataType.Float || format.DataType == JxlDataType.Float16;

            ExternalConvertOptions opts = new ExternalConvertOptions(
                ppf.Info.BitsPerSample,
                floatOut,
                format.Endianness,
                frame.Color.Stride,
                pool,
                frame.Color.PixelsBuffer,
                frame.Color.PixelsSize);

            if (!DecExternalImage.ConvertChannelsToExternal(channels, format.NumChannels, in opts))
            {
                return Status.Failure("ConvertChannelsToExternal failed");
            }

            ppf.Frames.Add(frame);

            return ppf;
        }

        private static Status ValidateAndCopyBasicMetadata(PackedPixelFile ppf, CodecInOut io)
        {
            if (ppf.Frames.Count == 0)
            {
                return Status.Failure("PackedPixelFile has no frames");
            }

            if (ppf.Info.XSize == 0 || ppf.Info.YSize == 0)
            {
                return Status.Failure("PackedPixelFile has zero dimensions");
            }

            int orientation = (int)ppf.Info.Orientation;

            if (orientation < 1 || orientation > 8)
            {
                return Status.Failure("Invalid orientation");
            }

            Status sizeStatus = io.SetSize(ppf.Info.XSize, ppf.Info.YSize);

            if (!sizeStatus)
            {
                return sizeStatus;
            }

            io.Metadata.M.Orientation = (uint)orientation;

            return true;
        }

        private static Status ApplyPpfBitDepthAndAlpha(PackedPixelFile ppf, CodecInOut io)
        {
            if (ppf.Info.ExponentBitsPerSample == 0)
            {
                io.Metadata.M.SetUintSamples(ppf.Info.BitsPerSample);
            }
            else if (ppf.Info.BitsPerSample == 32 && ppf.Info.ExponentBitsPerSample == 8)
            {
                io.Metadata.M.SetFloat32Samples();
            }
            else if (ppf.Info.BitsPerSample == 16 && ppf.Info.ExponentBitsPerSample == 5)
            {
                io.Metadata.M.SetFloat16Samples();
            }
            else
            {
                return Status.Failure("Invalid pixel format");
            }

            if (ppf.Info.AlphaBits != 0)
            {
                if (ppf.Info.AlphaBits != ppf.Info.BitsPerSample ||
                    ppf.Info.AlphaExponentBits != ppf.Info.ExponentBitsPerSample)
                {
                    return Status.Failure("Alpha bit depth does not match sample bit depth");
                }

                io.Metadata.M.SetAlphaBits(ppf.Info.AlphaBits, ppf.Info.AlphaPremultiplied);
                ExtraChannelInfo? alpha = io.Metadata.M.Find(ExtraChannel.Alpha);

                if (alpha != null)
                {
                    alpha.BitDepth.BitsPerSample         = io.Metadata.M.BitDepth.BitsPerSample;
                    alpha.BitDepth.ExponentBitsPerSample = io.Metadata.M.BitDepth.ExponentBitsPerSample;
                    alpha.BitDepth.FloatingPointSample   = io.Metadata.M.BitDepth.FloatingPointSample;
                }
            }

            CopyPpfExtraChannelMetadata(ppf, io);

            return true;
        }

        private static void CopyPpfExtraChannelMetadata(PackedPixelFile ppf, CodecInOut io)
        {
            foreach (PackedExtraChannel item in ppf.ExtraChannelsInfo)
            {
                ExtraChannelInfo ec               = new ExtraChannelInfo();
                ec.Type                           = (ExtraChannel)item.EcInfo.Type;
                ec.BitDepth.BitsPerSample         = item.EcInfo.BitsPerSample;
                ec.BitDepth.ExponentBitsPerSample = item.EcInfo.ExponentBitsPerSample;
                ec.BitDepth.FloatingPointSample   = item.EcInfo.ExponentBitsPerSample != 0;
                ec.DimShift                       = item.EcInfo.DimShift;
                ec.Name                           = item.Name ?? string.Empty;
                ec.AlphaAssociated                = item.EcInfo.AlphaPremultiplied;
                ec.SpotColor[0]                   = item.EcInfo.SpotColorR;
                ec.SpotColor[1]                   = item.EcInfo.SpotColorG;
                ec.SpotColor[2]                   = item.EcInfo.SpotColorB;
                ec.SpotColor[3]                   = item.EcInfo.SpotColorA;
                io.Metadata.M.ExtraChannelInfo.Add(ec);
            }
        }

        private static Status ApplyPpfColorSpace(PackedPixelFile ppf, CodecInOut io)
        {
            if (ppf.PrimaryColorRepresentation == PrimaryColorRepresentation.IccIsPrimary &&
                ppf.Icc.Length > 0)
            {
                if (!io.Metadata.M.ColorEncoding.SetICC(ppf.Icc, JxlCms.GetDefaultCms()))
                {
                    return ConvertExternalColorEncoding(ppf, io.Metadata.M.ColorEncoding);
                }

                return true;
            }

            return ConvertExternalColorEncoding(ppf, io.Metadata.M.ColorEncoding);
        }

        private static Status ConvertExternalColorEncoding(PackedPixelFile ppf, ColorEncoding colorEncoding)
        {
            if (!colorEncoding.FromExternal(ppf.ColorEncoding))
            {
                return Status.Failure("Failed to convert external color encoding");
            }

            if (colorEncoding.ICC().Length == 0)
            {
                return Status.Failure("Failed to generate ICC");
            }

            return true;
        }

        private static Status ConvertPpfPreviewAndFrames(PackedPixelFile ppf, ThreadPool? pool, CodecInOut io)
        {
            if (ppf.PreviewFrame != null)
            {
                uint previewXsize         = (uint)ppf.PreviewFrame.Color.XSize;
                uint previewYsize         = (uint)ppf.PreviewFrame.Color.YSize;
                io.Metadata.M.HavePreview = true;

                if (!io.Metadata.M.PreviewSize.Set(previewXsize, previewYsize) ||
                    !ConvertPackedFrameToImageBundle(ppf, ppf.PreviewFrame, io, pool, io.PreviewFrame))
                {
                    return false;
                }
            }

            io.Frames.Clear();

            foreach (PackedFrame frame in ppf.Frames)
            {
                ImageBundle frameBundle = new ImageBundle(io.MemoryManager, io.Metadata.M);

                if (!ConvertPackedFrameToImageBundle(ppf, frame, io, pool, frameBundle))
                {
                    frameBundle.Dispose();

                    return false;
                }

                io.Frames.Add(frameBundle);
            }

            return true;
        }

        private static Status ConvertPackedFrameToImageBundle(
            PackedPixelFile ppf,
            PackedFrame frame,
            CodecInOut io,
            ThreadPool? pool,
            ImageBundle bundle)
        {
            if (frame.Color.PixelsSize == 0 && (frame.Color.XSize != 0 && frame.Color.YSize != 0))
            {
                return Status.Failure("Frame color buffer is empty");
            }

            nuint frameBitsPerSample = ppf.InputBitdepth.Type == JxlBitDepthType.FromPixelFormat
                ? PackedImage.BitsPerChannel(frame.Color.Format.DataType)
                : ppf.Info.BitsPerSample;
            uint numChannels         = frame.Color.Format.NumChannels;

            if (frameBitsPerSample == 0 || (numChannels < 1 || numChannels > 4))
            {
                return Status.Failure("Invalid packed frame format");
            }

            if (!ConvertInterleavedToBundle(frameBitsPerSample, frame.Color, io, pool, bundle))
            {
                return false;
            }

            bundle.Name = frame.Name ?? string.Empty;

            return ConvertFrameExtraChannels(frame, io, pool, bundle);
        }

        private static Status ConvertInterleavedToBundle(
            nuint frameBitsPerSample,
            PackedImage colorImage,
            CodecInOut io,
            ThreadPool? pool,
            ImageBundle bundle)
        {
            uint numChannels = colorImage.Format.NumChannels;
            bool isGray      = numChannels <= 2;

            if (io.Metadata.M.ColorEncoding.IsGray() != isGray)
            {
                return Status.Failure("Color space grayscale mismatch with pixel format");
            }

            StatusOr<Image3<float>> colorOr = Image3<float>.Create(
                io.MemoryManager,
                colorImage.XSize,
                colorImage.YSize);

            if (!colorOr.IsOk)
            {
                return colorOr.Status;
            }

            Image3<float> color      = colorOr.Value;
            ReadOnlySpan<byte> bytes = colorImage.Pixels();
            nuint cChannels          = io.Metadata.M.ColorEncoding.Channels();

            for (nuint c = 0; c < cChannels; ++c)
            {
                ExternalBufferSpec spec = new ExternalBufferSpec(
                    colorImage.XSize,
                    colorImage.YSize,
                    colorImage.Stride,
                    frameBitsPerSample,
                    colorImage.Format,
                    c);

                if (!EncExternalImage.ConvertFromExternal(bytes, in spec, pool, color.Plane(c)))
                {
                    color.Dispose();

                    return false;
                }
            }

            if (cChannels == 1)
            {
                ImageOps.CopyImageTo(color.Plane(0), color.Plane(1));
                ImageOps.CopyImageTo(color.Plane(0), color.Plane(2));
            }

            bundle.SetFromImage(color, io.Metadata.M.ColorEncoding);

            return ConvertInterleavedAlphaIfPresent(frameBitsPerSample, colorImage, io, pool, bundle);
        }

        private static Status ConvertInterleavedAlphaIfPresent(
            nuint frameBitsPerSample,
            PackedImage colorImage,
            CodecInOut io,
            ThreadPool? pool,
            ImageBundle bundle)
        {
            bool hasAlpha  = colorImage.Format.NumChannels == 2 || colorImage.Format.NumChannels == 4;
            uint alphaBits = io.Metadata.M.GetAlphaBits();

            if (hasAlpha && alphaBits == 0)
            {
                return Status.Failure("Interleaved alpha present without alpha_bits set");
            }

            if (!hasAlpha && alphaBits == 0)
            {
                return true;
            }

            StatusOr<ImagePlane<float>> alphaOr = ImagePlane<float>.Create(
                io.MemoryManager,
                colorImage.XSize,
                colorImage.YSize);

            if (!alphaOr.IsOk)
            {
                return alphaOr.Status;
            }

            ImagePlane<float> alpha = alphaOr.Value;

            if (hasAlpha)
            {
                ExternalBufferSpec spec = new ExternalBufferSpec(
                    colorImage.XSize,
                    colorImage.YSize,
                    colorImage.Stride,
                    frameBitsPerSample,
                    colorImage.Format,
                    io.Metadata.M.ColorEncoding.Channels());

                if (!EncExternalImage.ConvertFromExternal(colorImage.Pixels(), in spec, pool, alpha))
                {
                    alpha.Dispose();

                    return false;
                }
            }

            bundle.SetAlpha(alpha);

            return true;
        }

        private static Status ConvertFrameExtraChannels(
            PackedFrame frame,
            CodecInOut io,
            ThreadPool? pool,
            ImageBundle bundle)
        {
            _ = io;

            foreach (PackedImage ppfEc in frame.ExtraChannels)
            {
                Status ok = EncExternalImage.BufferToImageF(
                    ppfEc.Format,
                    ppfEc.XSize,
                    ppfEc.YSize,
                    ppfEc.Pixels(),
                    pool,
                    out ImagePlane<float>? ec);

                if (!ok || ec == null)
                {
                    ec?.Dispose();

                    return false;
                }

                bundle.ExtraChannels.Add(ec);
            }

            return true;
        }

        private static void ApplyPpfLuminanceAndAnimation(PackedPixelFile ppf, CodecInOut io)
        {
            if (ppf.Info.IntensityTarget != 0)
            {
                io.Metadata.M.SetIntensityTarget(ppf.Info.IntensityTarget);
            }
            else
            {
                Luminance.SetIntensityTarget(io.Metadata.M);
            }

            io.Metadata.M.ToneMapping.MinNits              = ppf.Info.MinNits;
            io.Metadata.M.ToneMapping.RelativeToMaxDisplay = ppf.Info.RelativeToMaxDisplay;
            io.Metadata.M.ToneMapping.LinearBelow          = ppf.Info.LinearBelow;
            io.Metadata.M.HaveAnimation                    = ppf.Info.HaveAnimation;
            io.Metadata.M.Animation.TpsNumerator           = ppf.Info.Animation.TpsNumerator;
            io.Metadata.M.Animation.TpsDenominator         = ppf.Info.Animation.TpsDenominator;
            io.Metadata.M.Animation.NumLoops               = ppf.Info.Animation.NumLoops;
            io.Metadata.M.Animation.HaveTimecodes          = ppf.Info.Animation.HaveTimecodes;
        }

        private static Status PopulatePpfHeaderFromCodecInOut(
            CodecInOut io,
            in JxlPixelFormat pixelFormat,
            ColorEncoding cDesired,
            PackedPixelFile ppf)
        {
            ppf.Info.XSize            = (uint)io.XSize;
            ppf.Info.YSize            = (uint)io.YSize;
            ppf.Info.NumColorChannels = (uint)cDesired.Channels();
            ppf.Info.BitsPerSample    = (uint)PackedImage.BitsPerChannel(pixelFormat.DataType);

            if (!TryGetExponentBits(pixelFormat.DataType, out uint exponentBits))
            {
                return Status.Failure("JxlPixelFormat data type not supported");
            }

            ppf.Info.ExponentBitsPerSample = exponentBits;
            ppf.Info.IntensityTarget       = io.Metadata.M.IntensityTarget();
            ppf.Info.LinearBelow           = io.Metadata.M.ToneMapping.LinearBelow;
            ppf.Info.MinNits               = io.Metadata.M.ToneMapping.MinNits;
            ppf.Info.RelativeToMaxDisplay  = io.Metadata.M.ToneMapping.RelativeToMaxDisplay;
            ppf.Info.UsesOriginalProfile   = !io.Metadata.M.XybEncoded;
            ppf.Info.Orientation           = (JxlOrientation)io.Metadata.M.Orientation;

            if (!PopulatePpfAlphaAndColor(io, cDesired, ppf))
            {
                return false;
            }

            ppf.Info.HavePreview   = io.Metadata.M.HavePreview;
            ppf.Info.HaveAnimation = io.Metadata.M.HaveAnimation;

            JxlAnimationHeader anim = ppf.Info.Animation;
            anim.TpsNumerator       = io.Metadata.M.Animation.TpsNumerator;
            anim.TpsDenominator     = io.Metadata.M.Animation.TpsDenominator;
            anim.NumLoops           = io.Metadata.M.Animation.NumLoops;
            anim.HaveTimecodes      = io.Metadata.M.Animation.HaveTimecodes;
            ppf.Info.Animation      = anim;

            return true;
        }

        private static Status PopulatePpfAlphaAndColor(
            CodecInOut io,
            ColorEncoding cDesired,
            PackedPixelFile ppf)
        {
            if (io.Metadata.M.HasAlpha())
            {
                ExtraChannelInfo? alphaChannel = io.Metadata.M.Find(ExtraChannel.Alpha);

                if (alphaChannel == null)
                {
                    return Status.Failure("Alpha channel metadata not found");
                }

                ppf.Info.AlphaBits         = ppf.Info.BitsPerSample;
                ppf.Info.AlphaExponentBits = alphaChannel.BitDepth.FloatingPointSample
                    ? ppf.Info.ExponentBitsPerSample
                    : 0u;
                ppf.Info.AlphaPremultiplied = alphaChannel.AlphaAssociated;
            }

            ppf.ColorEncoding              = cDesired.ToExternal();
            ppf.Icc                        = (byte[])cDesired.ICC().Clone();
            ppf.PrimaryColorRepresentation = ppf.ColorEncoding.ColorSpace == JxlColorSpace.Unknown
                ? PrimaryColorRepresentation.IccIsPrimary
                : PrimaryColorRepresentation.ColorEncodingIsPrimary;

            return true;
        }

        private static Status ConvertCodecInOutFramesToPpf(
            CodecInOut io,
            in JxlPixelFormat pixelFormat,
            ColorEncoding cDesired,
            ThreadPool? pool,
            PackedPixelFile ppf)
        {
            bool hasAlpha = io.Metadata.M.HasAlpha();

            ppf.Frames.Clear();

            for (int i = 0; i < io.Frames.Count; ++i)
            {
                ImageBundle ib = io.Frames[i];

                if (hasAlpha && !ib.HasAlpha())
                {
                    return Status.Failure("Frame missing expected alpha channel");
                }

                ImageMetadata? meta     = ib.Metadata;
                using ImageBundle store = new ImageBundle(io.MemoryManager, meta);

                if (!ImageBundle.TransformIfNeeded(ib, cDesired, JxlCms.GetDefaultCms(), pool, store, out ImageBundle? transformed) ||
                    transformed == null)
                {
                    return false;
                }

                StatusOr<PackedFrame> frameOr = PackedFrame.Create(ib.XSize(), ib.YSize(), in pixelFormat);

                if (!frameOr.IsOk)
                {
                    return frameOr.Status;
                }

                PackedFrame frame = frameOr.Value;
                bool floatOut     = pixelFormat.DataType == JxlDataType.Float || pixelFormat.DataType == JxlDataType.Float16;

                ExternalConvertOptions opts = new ExternalConvertOptions(
                    ppf.Info.BitsPerSample,
                    floatOut,
                    pixelFormat.Endianness,
                    frame.Color.Stride,
                    pool,
                    frame.Color.PixelsBuffer,
                    frame.Color.PixelsSize);

                if (!DecExternalImage.ConvertToExternal(transformed, pixelFormat.NumChannels, unpremulAlpha: false, in opts))
                {
                    return false;
                }

                frame.FrameInfo.Duration   = ib.Duration;
                frame.FrameInfo.Timecode   = ib.Timecode;
                frame.FrameInfo.NameLength = (uint)(ib.Name?.Length ?? 0);
                frame.FrameInfo.IsLast     = (i == io.Frames.Count - 1);
                frame.Name                 = ib.Name ?? string.Empty;
                ppf.Frames.Add(frame);
            }

            return true;
        }

        private static bool TryGetExponentBits(JxlDataType dataType, out uint exponentBits)
        {
            switch (dataType)
            {
                case JxlDataType.UInt8:
                case JxlDataType.UInt16:
                    exponentBits = 0;

                    return true;

                case JxlDataType.Float:
                    exponentBits = 8;

                    return true;

                case JxlDataType.Float16:
                    exponentBits = 5;

                    return true;

                default:
                    exponentBits = 0;

                    return false;
            }
        }
    }
}
