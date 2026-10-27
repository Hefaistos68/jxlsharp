// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Upper limit on pixel dimensions/area, enforced by <see cref="ExtrasCommon.VerifyDimensions"/>.
    /// Ported from <c>lib/extras/size_constraints.h</c>.
    /// </summary>
    public struct SizeConstraints
    {
        /// <summary>Maximum allowed width in pixels.</summary>
        public uint DecMaxXSize;

        /// <summary>Maximum allowed height in pixels.</summary>
        public uint DecMaxYSize;

        /// <summary>Maximum allowed total number of pixels.</summary>
        public ulong DecMaxPixels;

        /// <summary>
        /// Initializes default size constraints corresponding to JXL level 10.
        /// </summary>
        /// <returns>Default <see cref="SizeConstraints"/>.</returns>
        public static SizeConstraints Default()
        {
            return new SizeConstraints
            {
                DecMaxXSize  = 1u << 30,
                DecMaxYSize  = 1u << 30,
                DecMaxPixels = 1UL << 40,
            };
        }
    }

    /// <summary>
    /// Class representing an interleaved image with one or more channels.
    /// Ported from <c>lib/extras/packed_image.h</c> and <c>lib/extras/packed_image.cc</c>.
    /// </summary>
    public sealed class PackedImage
    {
        private readonly nuint _bytesPerChannel;
        private readonly nuint _pixelStride;
        private readonly bool _swapEndianness;
        private readonly byte[] _pixels;

        /// <summary>Gets or sets the image width in pixels.</summary>
        public nuint XSize { get; private set; }

        /// <summary>Gets or sets the image height in pixels.</summary>
        public nuint YSize { get; private set; }

        /// <summary>Gets the number of bytes per row.</summary>
        public nuint Stride { get; }

        /// <summary>Gets the pixel storage format.</summary>
        public JxlPixelFormat Format { get; }

        /// <summary>Gets the buffer size of the underlying pixel storage in bytes.</summary>
        public nuint PixelsSize { get; }

        /// <summary>Gets the stride of a single pixel in bytes.</summary>
        public nuint PixelStride => _pixelStride;

        /// <summary>Gets the underlying raw interleaved byte array.</summary>
        public byte[] PixelsBuffer => _pixels;

        private PackedImage(nuint xsize, nuint ysize, JxlPixelFormat format, nuint stride)
        {
            XSize       = xsize;
            YSize       = ysize;
            Stride      = stride;
            Format      = format;
            PixelsSize  = ysize * stride;

            int allocLen     = (int)Math.Max((nuint)1, PixelsSize);
            _pixels          = new byte[allocLen];
            _bytesPerChannel = BitsPerChannel(format.DataType) / Common.KBitsPerByte;
            _pixelStride     = format.NumChannels * _bytesPerChannel;
            _swapEndianness  = SwapEndianness(format.Endianness);
        }

        /// <summary>
        /// Creates a new <see cref="PackedImage"/> with the given dimensions and pixel format.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="format">Pixel storage format.</param>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <returns>The created <see cref="PackedImage"/> or failure status.</returns>
        public static StatusOr<PackedImage> Create(
            nuint xsize,
            nuint ysize,
            in JxlPixelFormat format,
            JxlMemoryManager? memoryManager = null)
        {
            _ = memoryManager;
            StatusOr<nuint> strideResult = CalcStride(in format, xsize);

            if (!strideResult.IsOk)
            {
                return strideResult.Status;
            }

            nuint stride = strideResult.Value;

            if (stride > 0)
            {
                nuint pixelsSize = ysize * stride;

                if ((pixelsSize / stride) != ysize || pixelsSize > (nuint)Array.MaxLength)
                {
                    return Status.Failure("Image too big");
                }
            }

            return new PackedImage(xsize, ysize, format, stride);
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="PackedImage"/> with compact row stride.
        /// </summary>
        /// <returns>A copy of this <see cref="PackedImage"/>.</returns>
        public StatusOr<PackedImage> Copy()
        {
            StatusOr<nuint> strideResult = CalcStride(Format, XSize);

            if (!strideResult.IsOk)
            {
                return strideResult.Status;
            }

            nuint copyStride = strideResult.Value;
            PackedImage copy = new PackedImage(XSize, YSize, Format, copyStride);

            if (Stride == copyStride)
            {
                int totalBytes = (int)(YSize * Stride);
                Buffer.BlockCopy(_pixels, 0, copy._pixels, 0, totalBytes);
            }
            else
            {
                for (nuint y = 0; y < YSize; y++)
                {
                    Buffer.BlockCopy(
                        _pixels,
                        (int)(y * Stride),
                        copy._pixels,
                        (int)(y * copyStride),
                        (int)copyStride);
                }
            }

            return copy;
        }

        /// <summary>
        /// Returns a mutable span over the entire pixel buffer.
        /// </summary>
        /// <returns>A <see cref="Span{T}"/> of bytes.</returns>
        public Span<byte> Pixels()
        {
            return _pixels.AsSpan(0, (int)PixelsSize);
        }

        /// <summary>
        /// Returns a span starting at the given coordinates and channel.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="x">Column index.</param>
        /// <param name="c">Channel index.</param>
        /// <returns>A <see cref="Span{T}"/> starting at the channel sample.</returns>
        public Span<byte> Pixels(nuint y, nuint x, nuint c)
        {
            int offset = (int)(y * Stride + x * _pixelStride + c * _bytesPerChannel);

            return _pixels.AsSpan(offset);
        }

        /// <summary>
        /// Returns a read-only span starting at the given coordinates and channel.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="x">Column index.</param>
        /// <param name="c">Channel index.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> starting at the channel sample.</returns>
        public ReadOnlySpan<byte> ConstPixels(nuint y, nuint x, nuint c)
        {
            int offset = (int)(y * Stride + x * _pixelStride + c * _bytesPerChannel);

            return _pixels.AsSpan(offset);
        }

        /// <summary>
        /// Validates that a <see cref="JxlDataType"/> is supported by <see cref="PackedImage"/>.
        /// </summary>
        /// <param name="dataType">Data type to validate.</param>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        public static Status ValidateDataType(JxlDataType dataType)
        {
            if (dataType != JxlDataType.UInt8 &&
                dataType != JxlDataType.UInt16 &&
                dataType != JxlDataType.Float &&
                dataType != JxlDataType.Float16)
            {
                return Status.Failure($"Unhandled data type: {(int)dataType}");
            }

            return Status.Ok;
        }

        /// <summary>
        /// Returns the number of bits per channel for the given <see cref="JxlDataType"/>.
        /// </summary>
        /// <param name="dataType">Data type.</param>
        /// <returns>Number of bits per channel (8, 16, or 32).</returns>
        public static nuint BitsPerChannel(JxlDataType dataType)
        {
            switch (dataType)
            {
                case JxlDataType.UInt8:
                    return 8;
                case JxlDataType.UInt16:
                    return 16;
                case JxlDataType.Float:
                    return 32;
                case JxlDataType.Float16:
                    return 16;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Reads a normalized floating-point pixel value in <c>[0, 1]</c> at <c>(y, x, c)</c>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="x">Column index.</param>
        /// <param name="c">Channel index.</param>
        /// <returns>Normalized sample value.</returns>
        public float GetPixelValue(nuint y, nuint x, nuint c)
        {
            ReadOnlySpan<byte> data = ConstPixels(y, x, c);

            switch (Format.DataType)
            {
                case JxlDataType.UInt8:
                    return data[0] * (1.0f / 255.0f);
                case JxlDataType.UInt16:
                {
                    ushort val = BinaryPrimitives.ReadUInt16LittleEndian(data);

                    if (!BitConverter.IsLittleEndian)
                    {
                        val = BinaryPrimitives.ReverseEndianness(val);
                    }

                    if (_swapEndianness)
                    {
                        val = BinaryPrimitives.ReverseEndianness(val);
                    }

                    return val * (1.0f / 65535.0f);
                }
                case JxlDataType.Float:
                {
                    uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data);

                    if (!BitConverter.IsLittleEndian)
                    {
                        raw = BinaryPrimitives.ReverseEndianness(raw);
                    }

                    if (_swapEndianness)
                    {
                        raw = BinaryPrimitives.ReverseEndianness(raw);
                    }

                    return BitConverter.UInt32BitsToSingle(raw);
                }
                default:
                    return 0.0f;
            }
        }

        /// <summary>
        /// Writes a normalized floating-point pixel value at <c>(y, x, c)</c>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="x">Column index.</param>
        /// <param name="c">Channel index.</param>
        /// <param name="val">Normalized sample value.</param>
        public void SetPixelValue(nuint y, nuint x, nuint c, float val)
        {
            Span<byte> data = Pixels(y, x, c);

            switch (Format.DataType)
            {
                case JxlDataType.UInt8:
                {
                    float clamped = Common.Clamp1(MathF.Round(val * 255.0f, MidpointRounding.AwayFromZero), 0.0f, 255.0f);
                    data[0]       = (byte)clamped;
                    break;
                }
                case JxlDataType.UInt16:
                {
                    float clamped = Common.Clamp1(MathF.Round(val * 65535.0f, MidpointRounding.AwayFromZero), 0.0f, 65535.0f);
                    ushort val16  = (ushort)clamped;

                    if (_swapEndianness)
                    {
                        val16 = BinaryPrimitives.ReverseEndianness(val16);
                    }

                    MemoryMarshal.Write(data, in val16);
                    break;
                }
                case JxlDataType.Float:
                {
                    uint raw = BitConverter.SingleToUInt32Bits(val);

                    if (_swapEndianness)
                    {
                        raw = BinaryPrimitives.ReverseEndianness(raw);
                    }

                    MemoryMarshal.Write(data, in raw);
                    break;
                }
            }
        }

        /// <summary>
        /// Logically resizes this image to smaller or equal dimensions without reallocating storage.
        /// </summary>
        /// <param name="newXSize">New width.</param>
        /// <param name="newYSize">New height.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status ShrinkTo(nuint newXSize, nuint newYSize)
        {
            if (newXSize > XSize || newYSize > YSize)
            {
                return Status.Failure("Cannot shrink PackedImage to a larger size");
            }

            XSize = newXSize;
            YSize = newYSize;

            return Status.Ok;
        }

        private static bool SwapEndianness(JxlEndianness endianness)
        {
            return (endianness == JxlEndianness.BigEndian && BitConverter.IsLittleEndian) ||
                   (endianness == JxlEndianness.LittleEndian && !BitConverter.IsLittleEndian);
        }

        private static StatusOr<nuint> CalcStride(in JxlPixelFormat format, nuint xsize)
        {
            nuint multiplier = (BitsPerChannel(format.DataType) * format.NumChannels) / Common.KBitsPerByte;

            if (!Common.SafeMul(xsize, multiplier, out nuint stride))
            {
                return Status.Failure("Image too big");
            }

            if (format.Align > 1)
            {
                nuint rem = stride % format.Align;

                if (rem != 0)
                {
                    if (!Common.SafeAdd(stride, format.Align - rem, out stride))
                    {
                        return Status.Failure("Image too big");
                    }
                }
            }

            return stride;
        }
    }

    /// <summary>
    /// Helper class representing a frame, as seen from the API.
    /// Ported from <c>lib/extras/packed_image.h</c>.
    /// </summary>
    public sealed class PackedFrame
    {
        /// <summary>Gets or sets the frame metadata.</summary>
        public JxlFrameHeader FrameInfo;

        /// <summary>Gets or sets the frame name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the pixel data for the color (or grayscale) channels.</summary>
        public PackedImage Color { get; set; }

        /// <summary>Gets the extra channel image data.</summary>
        public List<PackedImage> ExtraChannels { get; } = new List<PackedImage>();

        /// <summary>
        /// Initializes a new instance of the <see cref="PackedFrame"/> class.
        /// </summary>
        /// <param name="image">Color channel packed image.</param>
        public PackedFrame(PackedImage image)
        {
            Color = image;
            JxlEncoder.JxlEncoderInitFrameHeader(out FrameInfo);
        }

        /// <summary>
        /// Creates a new <see cref="PackedFrame"/> with the given dimensions and pixel format.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="format">Pixel format.</param>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <returns>The created <see cref="PackedFrame"/> or failure status.</returns>
        public static StatusOr<PackedFrame> Create(
            nuint xsize,
            nuint ysize,
            in JxlPixelFormat format,
            JxlMemoryManager? memoryManager = null)
        {
            StatusOr<PackedImage> imageResult = PackedImage.Create(xsize, ysize, in format, memoryManager);

            if (!imageResult.IsOk)
            {
                return imageResult.Status;
            }

            return new PackedFrame(imageResult.Value);
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="PackedFrame"/>.
        /// </summary>
        /// <returns>A deep copy of this <see cref="PackedFrame"/>.</returns>
        public StatusOr<PackedFrame> Copy()
        {
            StatusOr<PackedImage> colorCopy = Color.Copy();

            if (!colorCopy.IsOk)
            {
                return colorCopy.Status;
            }

            PackedFrame copy = new PackedFrame(colorCopy.Value)
            {
                FrameInfo = FrameInfo,
                Name      = Name,
            };

            foreach (PackedImage ec in ExtraChannels)
            {
                StatusOr<PackedImage> ecCopy = ec.Copy();

                if (!ecCopy.IsOk)
                {
                    return ecCopy.Status;
                }

                copy.ExtraChannels.Add(ecCopy.Value);
            }

            return copy;
        }

        /// <summary>
        /// Logically resizes this frame to smaller or equal dimensions.
        /// </summary>
        /// <param name="newXSize">New width.</param>
        /// <param name="newYSize">New height.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status ShrinkTo(nuint newXSize, nuint newYSize)
        {
            Status status = Color.ShrinkTo(newXSize, newYSize);

            if (!status)
            {
                return status;
            }

            foreach (PackedImage ec in ExtraChannels)
            {
                status = ec.ShrinkTo(newXSize, newYSize);

                if (!status)
                {
                    return status;
                }
            }

            JxlLayerInfo layerInfo = FrameInfo.LayerInfo;
            layerInfo.XSize        = (uint)newXSize;
            layerInfo.YSize        = (uint)newYSize;
            FrameInfo.LayerInfo    = layerInfo;

            return Status.Ok;
        }
    }

    /// <summary>
    /// Represents a chunked packed frame for streaming input.
    /// Ported from <c>lib/extras/packed_image.h</c>.
    /// </summary>
    public sealed class ChunkedPackedFrame
    {
        private readonly Func<JxlPixelFormat> _getFormat;

        /// <summary>Frame metadata.</summary>
        public JxlFrameHeader FrameInfo;

        /// <summary>Gets or sets the frame name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets the width in pixels.</summary>
        public nuint XSize { get; }

        /// <summary>Gets the height in pixels.</summary>
        public nuint YSize { get; }

        /// <summary>Gets the pixel format.</summary>
        public JxlPixelFormat Format { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkedPackedFrame"/> class.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="getFormat">Callback returning the pixel format.</param>
        public ChunkedPackedFrame(nuint xsize, nuint ysize, Func<JxlPixelFormat> getFormat)
        {
            XSize      = xsize;
            YSize      = ysize;
            _getFormat = getFormat;
            Format     = _getFormat();
            JxlEncoder.JxlEncoderInitFrameHeader(out FrameInfo);
        }
    }

    /// <summary>
    /// Optional metadata associated with a file.
    /// Ported from <c>lib/extras/packed_image.h</c>.
    /// </summary>
    public sealed class PackedMetadata
    {
        /// <summary>Gets or sets the EXIF payload.</summary>
        public byte[] Exif { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the IPTC payload.</summary>
        public byte[] Iptc { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the JHGM gain map payload.</summary>
        public byte[] Jhgm { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the JUMBF payload.</summary>
        public byte[] Jumbf { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the XMP payload.</summary>
        public byte[] Xmp { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Extra channel metadata information.
    /// Ported from <c>lib/extras/packed_image.h</c>.
    /// </summary>
    public sealed class PackedExtraChannel
    {
        /// <summary>Extra channel info header.</summary>
        public JxlExtraChannelInfo EcInfo;

        /// <summary>Extra channel index.</summary>
        public nuint Index { get; set; }

        /// <summary>Extra channel name.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Indicates whether <see cref="PackedPixelFile.ColorEncoding"/> or <see cref="PackedPixelFile.Icc"/>
    /// is the authoritative color representation.
    /// </summary>
    public enum PrimaryColorRepresentation
    {
        /// <summary>ColorEncoding is authoritative.</summary>
        ColorEncodingIsPrimary = 0,

        /// <summary>ICC profile is authoritative.</summary>
        IccIsPrimary = 1,
    }

    /// <summary>
    /// Helper class representing a JXL image file as decoded to pixels from the API.
    /// Ported from <c>lib/extras/packed_image.h</c>.
    /// </summary>
    public sealed class PackedPixelFile
    {
        /// <summary>Basic image info.</summary>
        public JxlBasicInfo Info;

        /// <summary>Gets the extra channel metadata list.</summary>
        public List<PackedExtraChannel> ExtraChannelsInfo { get; } = new List<PackedExtraChannel>();

        /// <summary>Gets or sets the primary color representation.</summary>
        public PrimaryColorRepresentation PrimaryColorRepresentation { get; set; } =
            PrimaryColorRepresentation.ColorEncodingIsPrimary;

        /// <summary>Color encoding of the decoded pixels.</summary>
        public JxlColorEncoding ColorEncoding;

        /// <summary>Gets or sets the ICC profile of the decoded pixels.</summary>
        public byte[] Icc { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the ICC profile of the original image.</summary>
        public byte[] OrigIcc { get; set; } = Array.Empty<byte>();

        /// <summary>Input bit depth specification.</summary>
        public JxlBitDepth InputBitdepth = new JxlBitDepth
        {
            Type                  = JxlBitDepthType.FromPixelFormat,
            BitsPerSample         = 0,
            ExponentBitsPerSample = 0,
        };

        /// <summary>Gets or sets the optional preview frame.</summary>
        public PackedFrame? PreviewFrame { get; set; }

        /// <summary>Gets the list of frames.</summary>
        public List<PackedFrame> Frames { get; } = new List<PackedFrame>();

        /// <summary>Gets the list of chunked frames.</summary>
        public List<ChunkedPackedFrame> ChunkedFrames { get; } = new List<ChunkedPackedFrame>();

        /// <summary>Gets the file metadata boxes.</summary>
        public PackedMetadata Metadata { get; } = new PackedMetadata();

        /// <summary>
        /// Initializes a new instance of the <see cref="PackedPixelFile"/> class.
        /// </summary>
        public PackedPixelFile()
        {
            JxlEncoder.JxlEncoderInitBasicInfo(out Info);
        }

        /// <summary>Gets the number of frames.</summary>
        public nuint NumFrames => ChunkedFrames.Count == 0 ? (nuint)Frames.Count : (nuint)ChunkedFrames.Count;

        /// <summary>Gets the image width in pixels.</summary>
        public nuint XSize => Info.XSize;

        /// <summary>Gets the image height in pixels.</summary>
        public nuint YSize => Info.YSize;

        /// <summary>
        /// Logically resizes all frames and basic info to smaller or equal dimensions.
        /// </summary>
        /// <param name="newXSize">New width.</param>
        /// <param name="newYSize">New height.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status ShrinkTo(nuint newXSize, nuint newYSize)
        {
            foreach (PackedFrame frame in Frames)
            {
                Status status = frame.ShrinkTo(newXSize, newYSize);

                if (!status)
                {
                    return status;
                }
            }

            Info.XSize = (uint)newXSize;
            Info.YSize = (uint)newYSize;

            return Status.Ok;
        }
    }

    /// <summary>
    /// Format negotiation and dimension validation helpers.
    /// Ported from <c>lib/extras/common.cc</c> and <c>lib/extras/size_constraints.h</c>.
    /// </summary>
    public static class ExtrasCommon
    {
        /// <summary>
        /// Verifies that image dimensions are non-zero and within <paramref name="constraints"/>.
        /// </summary>
        /// <param name="constraints">Optional size constraints; defaults to level 10 when null.</param>
        /// <param name="xs">Width in pixels.</param>
        /// <param name="ys">Height in pixels.</param>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        public static Status VerifyDimensions(SizeConstraints? constraints, nuint xs, nuint ys)
        {
            SizeConstraints limit = constraints ?? SizeConstraints.Default();

            if (xs == 0 || ys == 0)
            {
                return Status.Failure("Empty image.");
            }

            if (xs > limit.DecMaxXSize)
            {
                return Status.Failure("Image too wide.");
            }

            if (ys > limit.DecMaxYSize)
            {
                return Status.Failure("Image too tall.");
            }

            ulong numPixels = (ulong)xs * (ulong)ys;

            if (numPixels > limit.DecMaxPixels)
            {
                return Status.Failure("Image too big.");
            }

            return Status.Ok;
        }

        /// <summary>
        /// Selects the best matching <see cref="JxlPixelFormat"/> from <paramref name="acceptedFormats"/>
        /// for the given <paramref name="basicInfo"/>.
        /// Ported from <c>lib/extras/common.cc</c>.
        /// </summary>
        /// <param name="acceptedFormats">List of accepted formats.</param>
        /// <param name="basicInfo">Image basic info.</param>
        /// <param name="format">Receives the selected format.</param>
        /// <returns><see cref="Status.Ok"/> if an appropriate format was found.</returns>
        public static Status SelectFormat(
            IReadOnlyList<JxlPixelFormat> acceptedFormats,
            in JxlBasicInfo basicInfo,
            out JxlPixelFormat format)
        {
            format                 = default;
            nuint originalBitDepth = basicInfo.BitsPerSample;
            nuint currentBitDepth  = 0;

            bool[] boolChoices = { false, true };

            foreach (bool dropAlpha in boolChoices)
            {
                if (dropAlpha && basicInfo.AlphaBits == 0)
                {
                    continue;
                }

                foreach (bool promoteGray in boolChoices)
                {
                    if (promoteGray && basicInfo.NumColorChannels != 1)
                    {
                        continue;
                    }

                    if (currentBitDepth != 0)
                    {
                        continue;
                    }

                    uint numChannels = basicInfo.NumColorChannels;

                    if (promoteGray)
                    {
                        numChannels = 3;
                    }

                    if (!dropAlpha && basicInfo.AlphaBits != 0)
                    {
                        numChannels += 1;
                    }

                    Status matchStatus = TryMatchCandidateFormat(
                        acceptedFormats,
                        numChannels,
                        originalBitDepth,
                        ref currentBitDepth,
                        ref format);

                    if (!matchStatus)
                    {
                        return matchStatus;
                    }
                }
            }

            if (currentBitDepth == 0)
            {
                return Status.Failure("no appropriate format found");
            }

            return Status.Ok;
        }

        private static Status TryMatchCandidateFormat(
            IReadOnlyList<JxlPixelFormat> acceptedFormats,
            uint numChannels,
            nuint originalBitDepth,
            ref nuint currentBitDepth,
            ref JxlPixelFormat format)
        {
            for (int i = 0; i < acceptedFormats.Count; i++)
            {
                JxlPixelFormat candidate = acceptedFormats[i];

                if (candidate.NumChannels != numChannels)
                {
                    continue;
                }

                Status valid = PackedImage.ValidateDataType(candidate.DataType);

                if (!valid)
                {
                    return valid;
                }

                nuint candidateBitDepth = PackedImage.BitsPerChannel(candidate.DataType);
                bool betterFit          =
                    (originalBitDepth <= candidateBitDepth && candidateBitDepth < currentBitDepth) ||
                    (currentBitDepth < candidateBitDepth && currentBitDepth < originalBitDepth);

                if (betterFit)
                {
                    format          = candidate;
                    currentBitDepth = candidateBitDepth;
                }
            }

            return Status.Ok;
        }
    }

    /// <summary>
    /// High-resolution monotonic timer helper.
    /// Ported from <c>lib/extras/time.h</c> and <c>lib/extras/time.cc</c>.
    /// </summary>
    public static class ExtrasTime
    {
        /// <summary>
        /// Returns the current monotonic time in seconds.
        /// </summary>
        /// <returns>Seconds elapsed from an arbitrary origin.</returns>
        public static double Now()
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Holds a preview, a main image or one or more frames, plus the inputs/outputs
    /// to/from decoding/encoding.
    /// Ported from <c>lib/extras/codec_in_out.h</c>.
    /// </summary>
    public sealed class CodecInOut
    {
        /// <summary>Gets the memory manager.</summary>
        public JxlMemoryManager MemoryManager { get; }

        /// <summary>Gets the codec metadata applying to preview and all frames.</summary>
        public CodecMetadata Metadata { get; } = new CodecMetadata();

        /// <summary>Gets or sets the preview frame image bundle.</summary>
        public ImageBundle PreviewFrame { get; set; }

        /// <summary>Gets the list of frame image bundles.</summary>
        public List<ImageBundle> Frames { get; } = new List<ImageBundle>(1);

        /// <summary>Gets or sets the target Butteraugli distance hint for tests.</summary>
        public float TargetButteraugliDistance { get; set; } = 1.0f;

        /// <summary>
        /// Initializes a new instance of the <see cref="CodecInOut"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager; defaults to <see cref="JxlMemoryManager.Default"/>.</param>
        public CodecInOut(JxlMemoryManager? memoryManager = null)
        {
            MemoryManager = memoryManager ?? JxlMemoryManager.Default;
            PreviewFrame  = new ImageBundle(MemoryManager, Metadata.M);
            Frames.Add(new ImageBundle(MemoryManager, Metadata.M));
        }

        /// <summary>
        /// Returns the index of the last still frame.
        /// </summary>
        /// <returns>Frame index.</returns>
        public int LastStillFrame()
        {
            int last = 0;

            for (int i = 0; i < Frames.Count; i++)
            {
                last = i;

                if (Frames[i].Duration > 0)
                {
                    break;
                }
            }

            return last;
        }

        /// <summary>Gets the main <see cref="ImageBundle"/> frame.</summary>
        public ImageBundle Main => Frames[LastStillFrame()];

        /// <summary>Gets the width in pixels.</summary>
        public int XSize => (int)Metadata.Size.XSize();

        /// <summary>Gets the height in pixels.</summary>
        public int YSize => (int)Metadata.Size.YSize();

        /// <summary>
        /// Sets the main frame from an <see cref="Image3{T}"/> and color encoding.
        /// </summary>
        /// <param name="color">3-plane floating-point color image.</param>
        /// <param name="cCurrent">Color encoding of <paramref name="color"/>.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status SetFromImage(Image3<float> color, ColorEncoding cCurrent)
        {
            Status status = Main.SetFromImage(color, cCurrent);

            if (!status)
            {
                return status;
            }

            Luminance.SetIntensityTarget(Metadata.M);

            return SetSize((uint)Main.XSize(), (uint)Main.YSize());
        }

        /// <summary>
        /// Sets the codestream size metadata.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status SetSize(uint xsize, uint ysize)
        {
            return Metadata.Size.Set(xsize, ysize);
        }

        /// <summary>
        /// Verifies metadata consistency across the codestream and all frames.
        /// </summary>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        public Status CheckMetadata()
        {
            if (Metadata.M.BitDepth.BitsPerSample == 0)
            {
                return Status.Failure("BitsPerSample is zero");
            }

            if (Metadata.M.ColorEncoding.ICC().Length == 0)
            {
                return Status.Failure("ICC is empty");
            }

            if (PreviewFrame.XSize() != 0)
            {
                Status previewStatus = PreviewFrame.VerifyMetadata();

                if (!previewStatus)
                {
                    return previewStatus;
                }
            }

            foreach (ImageBundle ib in Frames)
            {
                Status frameStatus = ib.VerifyMetadata();

                if (!frameStatus)
                {
                    return frameStatus;
                }
            }

            return Status.Ok;
        }

        /// <summary>
        /// Logically shrinks all frames and size metadata to <c>(xsize, ysize)</c>.
        /// </summary>
        /// <param name="xsize">New width.</param>
        /// <param name="ysize">New height.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public Status ShrinkTo(int xsize, int ysize)
        {
            foreach (ImageBundle ib in Frames)
            {
                Status status = ib.ShrinkTo((nuint)xsize, (nuint)ysize);

                if (!status)
                {
                    return status;
                }
            }

            return SetSize((uint)xsize, (uint)ysize);
        }
    }

    /// <summary>
    /// Codecs supported by <c>DecodeBytes</c> and <c>Encode</c>.
    /// Ported from <c>lib/extras/dec/decode.h</c>.
    /// </summary>
    public enum Codec : uint
    {
        /// <summary>Unknown or unsupported codec.</summary>
        Unknown = 0,

        /// <summary>PNG / APNG codec.</summary>
        Png = 1,

        /// <summary>PNM (PBM, PGM, PPM, PAM, PFM) codec.</summary>
        Pnm = 2,

        /// <summary>PGX codec.</summary>
        Pgx = 3,

        /// <summary>JPEG codec.</summary>
        Jpg = 4,

        /// <summary>GIF codec.</summary>
        Gif = 5,

        /// <summary>OpenEXR codec.</summary>
        Exr = 6,

        /// <summary>JPEG XL codec.</summary>
        Jxl = 7,
    }

    /// <summary>
    /// Key-value color space and metadata hints for image decoders.
    /// Ported from <c>lib/extras/dec/color_hints.h</c>.
    /// </summary>
    public sealed class ColorHints
    {
        private readonly List<(string Key, string Value)> _kv = new List<(string Key, string Value)>();

        /// <summary>
        /// Appends a key-value hint (e.g., <c>key="color_space"</c>, <c>value="RGB_D65_SRG_Rel_SRG"</c>).
        /// </summary>
        /// <param name="key">Hint key.</param>
        /// <param name="value">Hint value.</param>
        public void Add(string key, string value)
        {
            _kv.Add((key, value));
        }

        /// <summary>
        /// Invokes <paramref name="func"/> for each key/value pair in insertion order.
        /// </summary>
        /// <param name="func">Callback returning <see cref="Status"/>.</param>
        /// <returns><see cref="Status.Ok"/> if all callbacks succeeded.</returns>
        public Status Foreach(Func<string, string, Status> func)
        {
            foreach ((string key, string value) in _kv)
            {
                Status ok = func(key, value);

                if (!ok)
                {
                    return Status.Failure("ColorHints::Foreach returned false");
                }
            }

            return Status.Ok;
        }
    }

    /// <summary>
    /// Holds the output bitstreams produced by an <see cref="Encoder"/>.
    /// Ported from <c>lib/extras/enc/encode.h</c>.
    /// </summary>
    public sealed class EncodedImage
    {
        /// <summary>Gets the primary frame bitstreams.</summary>
        public List<byte[]> Bitstreams { get; } = new List<byte[]>();

        /// <summary>Gets the extra channel bitstreams per extra channel and frame.</summary>
        public List<List<byte[]>> ExtraChannelBitstreams { get; } = new List<List<byte[]>>();

        /// <summary>Gets or sets the preview bitstream.</summary>
        public byte[] PreviewBitstream { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets the external ICC profile when not embedded in the bitstream.</summary>
        public byte[] Icc { get; set; } = Array.Empty<byte>();

        /// <summary>Gets or sets additional metadata output (used by NumPyEncoder).</summary>
        public byte[] Metadata { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Base class for external image format encoders.
    /// Ported from <c>lib/extras/enc/encode.h</c> and <c>lib/extras/enc/encode.cc</c>.
    /// </summary>
    public abstract class Encoder
    {
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Gets the options dictionary configured on this encoder.</summary>
        protected IReadOnlyDictionary<string, string> Options => _options;

        /// <summary>
        /// Returns the list of pixel formats accepted by this encoder.
        /// </summary>
        /// <returns>Accepted pixel formats.</returns>
        public abstract IReadOnlyList<JxlPixelFormat> AcceptedFormats();

        /// <summary>
        /// Gets a value indicating whether this encoder can write CMYK images.
        /// </summary>
        public virtual bool AcceptsCmyk => false;

        /// <summary>
        /// Encodes <paramref name="ppf"/> into <paramref name="encodedImage"/>.
        /// </summary>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="encodedImage">Output encoded bitstreams.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public abstract Status Encode(PackedPixelFile ppf, EncodedImage encodedImage, ThreadPool? pool = null);

        /// <summary>
        /// Sets an encoder-specific string option.
        /// </summary>
        /// <param name="name">Option name.</param>
        /// <param name="value">Option value.</param>
        public void SetOption(string name, string value)
        {
            _options[name] = value;
        }

        /// <summary>
        /// Creates an <see cref="Encoder"/> instance for the given file extension (e.g., <c>".ppm"</c>, <c>".pgx"</c>).
        /// </summary>
        /// <param name="extension">File extension including leading dot.</param>
        /// <returns>An <see cref="Encoder"/> or null if unsupported.</returns>
        public static Encoder? FromExtension(string extension)
        {
            return ExtrasEncodeFacade.FromExtension(extension);
        }

        /// <summary>
        /// Verifies that <paramref name="info"/> has valid dimensions, color channels, alpha bits, and orientation.
        /// </summary>
        /// <param name="info">Basic info to validate.</param>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        public static Status VerifyBasicInfo(in JxlBasicInfo info)
        {
            if (info.XSize == 0 || info.YSize == 0)
            {
                return Status.Failure("Empty image");
            }

            if (info.NumColorChannels != 1 && info.NumColorChannels != 3)
            {
                return Status.Failure("Invalid number of color channels");
            }

            if (info.AlphaBits > 0 && info.AlphaBits != info.BitsPerSample)
            {
                return Status.Failure("Alpha bit depth does not match image bit depth");
            }

            if (info.Orientation != JxlOrientation.Identity)
            {
                return Status.Failure("Orientation must be identity");
            }

            return Status.Ok;
        }

        /// <summary>
        /// Verifies that <paramref name="image"/> stride, size, and channel count match <paramref name="info"/>.
        /// </summary>
        /// <param name="image">Packed image to validate.</param>
        /// <param name="info">Basic info.</param>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        public static Status VerifyImageSize(PackedImage image, in JxlBasicInfo info)
        {
            if (image.Stride != image.XSize * image.PixelStride)
            {
                return Status.Failure("Invalid image stride.");
            }

            if (image.PixelsSize != image.YSize * image.Stride)
            {
                return Status.Failure("Invalid image size.");
            }

            uint infoNumChannels = info.NumColorChannels + (info.AlphaBits > 0 ? 1u : 0u);

            if (image.Format.NumChannels != infoNumChannels)
            {
                return Status.Failure("Frame size does not match image size");
            }

            return Status.Ok;
        }

        /// <summary>
        /// Verifies that <paramref name="dataType"/>, <paramref name="bitsPerSample"/>, and <paramref name="exponentBits"/> are compatible.
        /// </summary>
        /// <param name="dataType">Sample data type.</param>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="exponentBits">Exponent bits per sample.</param>
        /// <returns><see cref="Status.Ok"/> if compatible.</returns>
        public static Status VerifyBitDepth(JxlDataType dataType, uint bitsPerSample, uint exponentBits)
        {
            bool invalidUint8   = dataType == JxlDataType.UInt8 && (bitsPerSample == 0 || bitsPerSample > 8 || exponentBits != 0);
            bool invalidUint16  = dataType == JxlDataType.UInt16 && (bitsPerSample <= 8 || bitsPerSample > 16 || exponentBits != 0);
            bool invalidFloat16 = dataType == JxlDataType.Float16 && (bitsPerSample > 16 || exponentBits > 5);

            if (invalidUint8 || invalidUint16 || invalidFloat16)
            {
                return Status.Failure(
                    $"Incompatible data_type {(int)dataType} and bit depth {bitsPerSample} with exponent bits {exponentBits}");
            }

            return Status.Ok;
        }

        /// <summary>
        /// Verifies that <paramref name="format"/> is in <see cref="AcceptedFormats"/>.
        /// </summary>
        /// <param name="format">Pixel format to check.</param>
        /// <returns><see cref="Status.Ok"/> if accepted.</returns>
        protected Status VerifyFormat(in JxlPixelFormat format)
        {
            foreach (JxlPixelFormat f in AcceptedFormats())
            {
                if (f.NumChannels != format.NumChannels || f.DataType != format.DataType)
                {
                    continue;
                }

                if (f.DataType == JxlDataType.UInt8 ||
                    f.Endianness == format.Endianness ||
                    MatchesNativeEndianness(f.Endianness, format.Endianness))
                {
                    return Status.Ok;
                }
            }

            return Status.Failure("Format is not in the list of accepted formats.");
        }

        /// <summary>
        /// Verifies image size, pixel format, and bit depth against <paramref name="info"/>.
        /// </summary>
        /// <param name="image">Packed image to verify.</param>
        /// <param name="info">Basic info.</param>
        /// <returns><see cref="Status.Ok"/> if valid.</returns>
        protected Status VerifyPackedImage(PackedImage image, in JxlBasicInfo info)
        {
            Status status = VerifyImageSize(image, in info);

            if (!status)
            {
                return status;
            }

            status = VerifyFormat(image.Format);

            if (!status)
            {
                return status;
            }

            return VerifyBitDepth(image.Format.DataType, info.BitsPerSample, info.ExponentBitsPerSample);
        }

        private static bool MatchesNativeEndianness(JxlEndianness a, JxlEndianness b)
        {
            JxlEndianness native = BitConverter.IsLittleEndian ? JxlEndianness.LittleEndian : JxlEndianness.BigEndian;
            JxlEndianness normA  = a == JxlEndianness.NativeEndian ? native : a;
            JxlEndianness normB  = b == JxlEndianness.NativeEndian ? native : b;

            return normA == normB;
        }
    }

    /// <summary>
    /// Option entry for <see cref="JXLCompressParams"/>.
    /// Ported from <c>lib/extras/enc/jxl.h</c>.
    /// </summary>
    public readonly struct JXLOption
    {
        /// <summary>Frame setting identifier.</summary>
        public readonly JxlEncoderFrameSettingId Id;

        /// <summary>True if <see cref="FVal"/> is used; false if <see cref="IVal"/> is used.</summary>
        public readonly bool IsFloat;

        /// <summary>Integer option value.</summary>
        public readonly long IVal;

        /// <summary>Floating-point option value.</summary>
        public readonly float FVal;

        /// <summary>Frame index to which this option applies.</summary>
        public readonly nuint FrameIndex;

        /// <summary>
        /// Initializes a new integer <see cref="JXLOption"/>.
        /// </summary>
        /// <param name="id">Setting ID.</param>
        /// <param name="val">Integer value.</param>
        /// <param name="frameIndex">Frame index.</param>
        public JXLOption(JxlEncoderFrameSettingId id, long val, nuint frameIndex = 0)
        {
            Id         = id;
            IsFloat    = false;
            IVal       = val;
            FVal       = 0.0f;
            FrameIndex = frameIndex;
        }

        /// <summary>
        /// Initializes a new floating-point <see cref="JXLOption"/>.
        /// </summary>
        /// <param name="id">Setting ID.</param>
        /// <param name="val">Float value.</param>
        /// <param name="frameIndex">Frame index.</param>
        public JXLOption(JxlEncoderFrameSettingId id, float val, nuint frameIndex = 0)
        {
            Id         = id;
            IsFloat    = true;
            IVal       = 0;
            FVal       = val;
            FrameIndex = frameIndex;
        }
    }

    /// <summary>
    /// Parameters for compressing a <see cref="PackedPixelFile"/> to JPEG XL.
    /// Ported from <c>lib/extras/enc/jxl.h</c>.
    /// </summary>
    public sealed class JXLCompressParams
    {
        /// <summary>Gets the list of encoder frame setting options.</summary>
        public List<JXLOption> Options { get; } = new List<JXLOption>();

        /// <summary>Target Butteraugli distance (0.0 means lossless).</summary>
        public float Distance { get; set; } = 1.0f;

        /// <summary>Target alpha channel distance.</summary>
        public float AlphaDistance { get; set; } = 0.0f;

        /// <summary>Forces container mode when true.</summary>
        public bool UseContainer { get; set; }

        /// <summary>Enables byte-exact JPEG reconstruction metadata for JPEG inputs.</summary>
        public bool JpegStoreMetadata { get; set; } = true;

        /// <summary>Strips EXIF when transcoding JPEG.</summary>
        public bool JpegStripExif { get; set; }

        /// <summary>Strips XMP when transcoding JPEG.</summary>
        public bool JpegStripXmp { get; set; }

        /// <summary>Strips JUMBF when transcoding JPEG.</summary>
        public bool JpegStripJumbf { get; set; }

        /// <summary>Whether to compress metadata boxes with Brotli (<c>brob</c>).</summary>
        public bool CompressBoxes { get; set; } = true;

        /// <summary>Intensity target in nits (0 means library default).</summary>
        public float IntensityTarget { get; set; }

        /// <summary>Already downsampled factor.</summary>
        public int AlreadyDownsampled { get; set; } = 1;

        /// <summary>Upsampling mode (-1 for default).</summary>
        public int UpsamplingMode { get; set; } = -1;

        /// <summary>Override bit depth (0 for none).</summary>
        public nuint OverrideBitdepth { get; set; }

        /// <summary>Codestream level (-1 for automatic).</summary>
        public int CodestreamLevel { get; set; } = -1;

        /// <summary>Premultiply alpha override (-1 for automatic).</summary>
        public int Premultiply { get; set; } = -1;

        /// <summary>Alpha channel stripping mode (-1 default, 0 keep, 1 strip, 2 strip if opaque).</summary>
        public int StripAlpha { get; set; } = -1;

        /// <summary>Optional parallel runner.</summary>
        public IParallelRunner? Runner { get; set; }

        /// <summary>Optional custom memory manager.</summary>
        public JxlMemoryManager? MemoryManager { get; set; }

        /// <summary>Optional encoder stats collector.</summary>
        public JxlSharp.Encoder.JxlEncoderStats? Stats { get; set; }

        /// <summary>Allows expert encoder options (such as Effort = 10/11).</summary>
        public bool AllowExpertOptions { get; set; }

        /// <summary>
        /// Adds an integer frame setting option.
        /// </summary>
        /// <param name="id">Setting ID.</param>
        /// <param name="val">Integer value.</param>
        public void AddOption(JxlEncoderFrameSettingId id, long val)
        {
            Options.Add(new JXLOption(id, val, 0));
        }

        /// <summary>
        /// Adds a floating-point frame setting option.
        /// </summary>
        /// <param name="id">Setting ID.</param>
        /// <param name="val">Floating-point value.</param>
        public void AddFloatOption(JxlEncoderFrameSettingId id, float val)
        {
            Options.Add(new JXLOption(id, val, 0));
        }
    }

    /// <summary>
    /// Parameters for decompressing a JPEG XL image into a <see cref="PackedPixelFile"/>.
    /// Ported from <c>lib/extras/dec/jxl.h</c>.
    /// </summary>
    public sealed class JXLDecompressParams
    {
        /// <summary>Gets the list of accepted pixel formats.</summary>
        public List<JxlPixelFormat> AcceptedFormats { get; } = new List<JxlPixelFormat>();

        /// <summary>Gets or sets the requested output color space description.</summary>
        public string ColorSpace { get; set; } = string.Empty;

        /// <summary>Gets or sets the requested output color space description for CMYK images.</summary>
        public string ColorSpaceForCmyk { get; set; } = string.Empty;

        /// <summary>If non-zero, performs tone mapping to this intensity target luminance in nits.</summary>
        public float DisplayNits { get; set; }

        /// <summary>Whether spot colors are rendered on the image.</summary>
        public bool RenderSpotcolors { get; set; } = true;

        /// <summary>Whether to keep or undo the orientation given in the header.</summary>
        public bool KeepOrientation { get; set; }

        /// <summary>Whether frames are coalesced.</summary>
        public bool Coalescing { get; set; } = true;

        /// <summary>Optional parallel runner.</summary>
        public IParallelRunner? Runner { get; set; }

        /// <summary>Optional memory manager.</summary>
        public JxlMemoryManager? MemoryManager { get; set; }

        /// <summary>Whether truncated input should be allowed.</summary>
        public bool AllowPartialInput { get; set; }

        /// <summary>Maximum number of passes to decode.</summary>
        public uint MaxPasses { get; set; } = uint.MaxValue;

        /// <summary>Maximum tolerable downsampling factor.</summary>
        public nuint MaxDownsampling { get; set; } = 1;

        /// <summary>Whether to use the image callback or buffer for output.</summary>
        public bool UseImageCallback { get; set; } = true;

        /// <summary>Whether to unpremultiply colors for associated alpha channels.</summary>
        public bool UnpremultiplyAlpha { get; set; }

        /// <summary>Controls the effective bit depth of the output pixels.</summary>
        public JxlBitDepth OutputBitdepth = new JxlBitDepth
        {
            Type                  = JxlBitDepthType.FromPixelFormat,
            BitsPerSample         = 0,
            ExponentBitsPerSample = 0,
        };
    }
}
