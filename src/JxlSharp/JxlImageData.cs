// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Encoder;
using JxlSharp.Extras;

namespace JxlSharp
{
    /// <summary>
    /// Represents an interleaved pixel buffer along with its dimensions, sample format,
    /// color encoding, and optional metadata boxes for simple and extended JPEG XL operations.
    /// </summary>
    public sealed class JxlImageData
    {
        /// <summary>
        /// Gets the image width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the image height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the number of interleaved channels per pixel (1 = Gray, 2 = Gray+Alpha, 3 = RGB, 4 = RGBA).
        /// </summary>
        public int Channels { get; }

        /// <summary>
        /// Gets the sample storage data type.
        /// </summary>
        public JxlDataType DataType { get; }

        /// <summary>
        /// Gets the logical bit depth per sample (e.g., 8, 16, or 32).
        /// </summary>
        public uint BitsPerSample { get; }

        /// <summary>
        /// Gets the raw interleaved pixel buffer.
        /// </summary>
        public byte[] Pixels { get; }

        /// <summary>
        /// Gets a value indicating whether the image includes an alpha channel (<see cref="Channels"/> is 2 or 4).
        /// </summary>
        public bool HasAlpha => Channels == 2 || Channels == 4;

        /// <summary>
        /// Gets or sets the JPEG XL basic header information associated with this image.
        /// </summary>
        public JxlBasicInfo BasicInfo { get; set; }

        /// <summary>
        /// Gets or sets the color encoding description of the pixel data.
        /// </summary>
        public JxlColorEncoding ColorEncoding { get; set; }

        /// <summary>
        /// Gets or sets the optional ICC profile bytes.
        /// </summary>
        public byte[] IccProfile { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets or sets the optional metadata boxes (EXIF, XMP, JUMBF, IPTC, JHGM).
        /// </summary>
        public PackedMetadata Metadata { get; set; } = new PackedMetadata();

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlImageData"/> class.
        /// </summary>
        /// <param name="pixels">Interleaved pixel bytes.</param>
        /// <param name="width">Width in pixels (<c>&gt; 0</c>).</param>
        /// <param name="height">Height in pixels (<c>&gt; 0</c>).</param>
        /// <param name="channels">Number of channels (<c>1..4</c>).</param>
        /// <param name="dataType">Sample storage data type.</param>
        /// <param name="bitsPerSample">Bits per sample (0 to infer from <paramref name="dataType"/>).</param>
        public JxlImageData(
            byte[] pixels,
            int width,
            int height,
            int channels = 4,
            JxlDataType dataType = JxlDataType.UInt8,
            uint bitsPerSample = 8)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ValidateDimensionsAndChannels(width, height, channels);

            uint resolvedBits = bitsPerSample > 0 ? bitsPerSample : (uint)PackedImage.BitsPerChannel(dataType);
            int minByteLength = CalculateRequiredByteLength(width, height, channels, dataType);

            if (pixels.Length < minByteLength)
            {
                throw new ArgumentException(
                    $"Pixel buffer length ({pixels.Length}) is smaller than required ({minByteLength} bytes).",
                    nameof(pixels));
            }

            Pixels        = pixels;
            Width         = width;
            Height        = height;
            Channels      = channels;
            DataType      = dataType;
            BitsPerSample = resolvedBits;
            BasicInfo     = CreateDefaultBasicInfo(width, height, channels, dataType, resolvedBits);
            ColorEncoding = CreateDefaultColorEncoding(channels);
        }

        /// <summary>
        /// Creates a new <see cref="JxlImageData"/> by copying the specified pixel span.
        /// </summary>
        /// <param name="pixels">Interleaved pixel bytes.</param>
        /// <param name="width">Width in pixels (<c>&gt; 0</c>).</param>
        /// <param name="height">Height in pixels (<c>&gt; 0</c>).</param>
        /// <param name="channels">Number of channels (<c>1..4</c>).</param>
        /// <param name="dataType">Sample storage data type.</param>
        /// <param name="bitsPerSample">Bits per sample (0 to infer from <paramref name="dataType"/>).</param>
        /// <returns>A populated <see cref="JxlImageData"/> instance.</returns>
        public static JxlImageData FromPixels(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            JxlDataType dataType = JxlDataType.UInt8,
            uint bitsPerSample = 8)
        {
            return new JxlImageData(pixels.ToArray(), width, height, channels, dataType, bitsPerSample);
        }

        /// <summary>
        /// Creates a <see cref="JxlImageData"/> from a frame of a <see cref="PackedPixelFile"/>.
        /// </summary>
        /// <param name="pixelFile">Source <see cref="PackedPixelFile"/>.</param>
        /// <param name="frameIndex">Zero-based frame index.</param>
        /// <returns>Extracted <see cref="JxlImageData"/>.</returns>
        public static JxlImageData FromPackedPixelFile(PackedPixelFile pixelFile, int frameIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(pixelFile);

            if (pixelFile.Frames.Count == 0)
            {
                throw new ArgumentException("PackedPixelFile contains no frames.", nameof(pixelFile));
            }

            if (frameIndex < 0 || frameIndex >= pixelFile.Frames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            }

            PackedImage color = pixelFile.Frames[frameIndex].Color;
            byte[] buffer     = color.Pixels().ToArray();
            uint bits         = pixelFile.Info.BitsPerSample > 0
                ? pixelFile.Info.BitsPerSample
                : (uint)PackedImage.BitsPerChannel(color.Format.DataType);

            JxlImageData image = new JxlImageData(
                buffer,
                (int)color.XSize,
                (int)color.YSize,
                (int)color.Format.NumChannels,
                color.Format.DataType,
                bits)
            {
                BasicInfo     = pixelFile.Info,
                ColorEncoding = pixelFile.ColorEncoding,
                IccProfile    = pixelFile.Icc.Length > 0 ? (byte[])pixelFile.Icc.Clone() : Array.Empty<byte>(),
                Metadata      = CloneMetadata(pixelFile.Metadata),
            };

            return image;
        }

        /// <summary>
        /// Converts this <see cref="JxlImageData"/> into a single-frame <see cref="PackedPixelFile"/>.
        /// </summary>
        /// <returns>A populated <see cref="PackedPixelFile"/>.</returns>
        public PackedPixelFile ToPackedPixelFile()
        {
            PackedPixelFile ppf = new PackedPixelFile
            {
                Info          = BasicInfo,
                ColorEncoding = ColorEncoding,
            };

            CopyMetadata(Metadata, ppf.Metadata);

            ppf.Info.XSize            = (uint)Width;
            ppf.Info.YSize            = (uint)Height;
            ppf.Info.NumColorChannels = Channels <= 2 ? 1u : 3u;
            ppf.Info.BitsPerSample    = BitsPerSample;
            ppf.Info.AlphaBits        = HasAlpha ? BitsPerSample : 0u;
            ppf.Info.NumExtraChannels = HasAlpha ? 1u : 0u;

            if (IccProfile.Length > 0)
            {
                ppf.Icc                        = (byte[])IccProfile.Clone();
                ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            }

            JxlPixelFormat format = new JxlPixelFormat(
                (uint)Channels,
                DataType,
                JxlEndianness.NativeEndian,
                0);

            StatusOr<PackedFrame> frameOr = PackedFrame.Create((nuint)Width, (nuint)Height, in format);

            if (!frameOr.IsOk)
            {
                throw new InvalidOperationException("Failed to allocate PackedFrame for JxlImageData.");
            }

            PackedFrame frame = frameOr.Value;
            int byteCount     = (int)frame.Color.PixelsSize;

            Pixels.AsSpan(0, byteCount).CopyTo(frame.Color.Pixels());
            ppf.Frames.Add(frame);

            return ppf;
        }

        private static void ValidateDimensionsAndChannels(int width, int height, int channels)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
            }

            if (channels < 1 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be between 1 and 4.");
            }
        }

        private static int CalculateRequiredByteLength(int width, int height, int channels, JxlDataType dataType)
        {
            int bytesPerChannel = (int)(PackedImage.BitsPerChannel(dataType) / 8u);

            if (bytesPerChannel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dataType), "Unsupported JxlDataType.");
            }

            long totalBytes = (long)width * height * channels * bytesPerChannel;

            if (totalBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions exceed maximum buffer size.");
            }

            return (int)totalBytes;
        }

        private static JxlBasicInfo CreateDefaultBasicInfo(
            int width,
            int height,
            int channels,
            JxlDataType dataType,
            uint bitsPerSample)
        {
            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);

            bool hasAlpha              = channels == 2 || channels == 4;
            uint exponentBits          = dataType == JxlDataType.Float ? 8u : (dataType == JxlDataType.Float16 ? 5u : 0u);
            info.XSize                 = (uint)width;
            info.YSize                 = (uint)height;
            info.NumColorChannels      = channels <= 2 ? 1u : 3u;
            info.BitsPerSample         = bitsPerSample;
            info.ExponentBitsPerSample = exponentBits;
            info.AlphaBits             = hasAlpha ? bitsPerSample : 0u;
            info.AlphaExponentBits     = hasAlpha ? exponentBits : 0u;
            info.NumExtraChannels      = hasAlpha ? 1u : 0u;
            info.Orientation           = JxlOrientation.Identity;

            return info;
        }

        private static JxlColorEncoding CreateDefaultColorEncoding(int channels)
        {
            return new JxlColorEncoding
            {
                ColorSpace       = channels <= 2 ? JxlColorSpace.Gray : JxlColorSpace.Rgb,
                WhitePoint       = JxlWhitePoint.D65,
                Primaries        = JxlPrimaries.Srgb,
                TransferFunction = JxlTransferFunction.Srgb,
                RenderingIntent  = JxlRenderingIntent.Relative,
            };
        }

        private static PackedMetadata CloneMetadata(PackedMetadata source)
        {
            PackedMetadata target = new PackedMetadata();

            CopyMetadata(source, target);

            return target;
        }

        private static void CopyMetadata(PackedMetadata? source, PackedMetadata target)
        {
            if (source is null)
            {
                return;
            }

            target.Exif  = source.Exif.Length > 0 ? (byte[])source.Exif.Clone() : Array.Empty<byte>();
            target.Xmp   = source.Xmp.Length > 0 ? (byte[])source.Xmp.Clone() : Array.Empty<byte>();
            target.Jumbf = source.Jumbf.Length > 0 ? (byte[])source.Jumbf.Clone() : Array.Empty<byte>();
            target.Iptc  = source.Iptc.Length > 0 ? (byte[])source.Iptc.Clone() : Array.Empty<byte>();
            target.Jhgm  = source.Jhgm.Length > 0 ? (byte[])source.Jhgm.Clone() : Array.Empty<byte>();
        }
    }
}
