// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Image orientation metadata.
    /// Values 1..8 match the EXIF definitions.
    /// The name indicates the operation to perform to transform from the encoded
    /// image to the display image.
    /// </summary>
    public enum JxlOrientation
    {
        /// <summary>
        /// Identity orientation (no transformation).
        /// </summary>
        Identity = 1,

        /// <summary>
        /// Flip horizontally.
        /// </summary>
        FlipHorizontal = 2,

        /// <summary>
        /// Rotate 180 degrees.
        /// </summary>
        Rotate180 = 3,

        /// <summary>
        /// Flip vertically.
        /// </summary>
        FlipVertical = 4,

        /// <summary>
        /// Transpose across main diagonal.
        /// </summary>
        Transpose = 5,

        /// <summary>
        /// Rotate 90 degrees clockwise.
        /// </summary>
        Rotate90Cw = 6,

        /// <summary>
        /// Transpose across anti-diagonal.
        /// </summary>
        AntiTranspose = 7,

        /// <summary>
        /// Rotate 90 degrees counter-clockwise.
        /// </summary>
        Rotate90Ccw = 8,
    }

    /// <summary>
    /// Given type of an extra channel.
    /// </summary>
    public enum JxlExtraChannelType
    {
        /// <summary>
        /// Alpha channel.
        /// </summary>
        Alpha = 0,

        /// <summary>
        /// Depth channel.
        /// </summary>
        Depth = 1,

        /// <summary>
        /// Spot color channel.
        /// </summary>
        SpotColor = 2,

        /// <summary>
        /// Selection mask channel.
        /// </summary>
        SelectionMask = 3,

        /// <summary>
        /// Black channel (for CMYK).
        /// </summary>
        Black = 4,

        /// <summary>
        /// Color Filter Array (Bayer) channel.
        /// </summary>
        Cfa = 5,

        /// <summary>
        /// Thermal channel.
        /// </summary>
        Thermal = 6,

        /// <summary>
        /// Reserved channel type 0.
        /// </summary>
        Reserved0 = 7,

        /// <summary>
        /// Reserved channel type 1.
        /// </summary>
        Reserved1 = 8,

        /// <summary>
        /// Reserved channel type 2.
        /// </summary>
        Reserved2 = 9,

        /// <summary>
        /// Reserved channel type 3.
        /// </summary>
        Reserved3 = 10,

        /// <summary>
        /// Reserved channel type 4.
        /// </summary>
        Reserved4 = 11,

        /// <summary>
        /// Reserved channel type 5.
        /// </summary>
        Reserved5 = 12,

        /// <summary>
        /// Reserved channel type 6.
        /// </summary>
        Reserved6 = 13,

        /// <summary>
        /// Reserved channel type 7.
        /// </summary>
        Reserved7 = 14,

        /// <summary>
        /// Unknown extra channel type.
        /// </summary>
        Unknown = 15,

        /// <summary>
        /// Optional extra channel type.
        /// </summary>
        Optional = 16,
    }

    /// <summary>
    /// Frame blend modes.
    /// When decoding, if coalescing is enabled (default), this can be ignored.
    /// </summary>
    public enum JxlBlendMode
    {
        /// <summary>
        /// Replace base layer.
        /// </summary>
        Replace = 0,

        /// <summary>
        /// Add to base layer.
        /// </summary>
        Add = 1,

        /// <summary>
        /// Alpha-blend with base layer.
        /// </summary>
        Blend = 2,

        /// <summary>
        /// Multiply and add with base layer.
        /// </summary>
        MulAdd = 3,

        /// <summary>
        /// Multiply with base layer.
        /// </summary>
        Mul = 4,
    }

    /// <summary>
    /// The codestream preview header.
    /// </summary>
    public struct JxlPreviewHeader : IEquatable<JxlPreviewHeader>
    {
        /// <summary>
        /// Preview width in pixels.
        /// </summary>
        public uint XSize { get; set; }

        /// <summary>
        /// Preview height in pixels.
        /// </summary>
        public uint YSize { get; set; }

        /// <inheritdoc />
        public bool Equals(JxlPreviewHeader other)
        {
            return XSize == other.XSize && YSize == other.YSize;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlPreviewHeader other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(XSize, YSize);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlPreviewHeader"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlPreviewHeader left, JxlPreviewHeader right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlPreviewHeader"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlPreviewHeader left, JxlPreviewHeader right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The codestream animation header, optionally present in the beginning of
    /// the codestream, and if it is it applies to all animation frames, unlike
    /// <see cref="JxlFrameHeader"/> which applies to an individual frame.
    /// </summary>
    public struct JxlAnimationHeader : IEquatable<JxlAnimationHeader>
    {
        /// <summary>
        /// Numerator of ticks per second of a single animation frame time unit.
        /// </summary>
        public uint TpsNumerator { get; set; }

        /// <summary>
        /// Denominator of ticks per second of a single animation frame time unit.
        /// </summary>
        public uint TpsDenominator { get; set; }

        /// <summary>
        /// Amount of animation loops, or 0 to repeat infinitely.
        /// </summary>
        public uint NumLoops { get; set; }

        /// <summary>
        /// Whether animation time codes are present at animation frames in the codestream.
        /// </summary>
        public bool HaveTimecodes { get; set; }

        /// <inheritdoc />
        public bool Equals(JxlAnimationHeader other)
        {
            return TpsNumerator == other.TpsNumerator &&
                   TpsDenominator == other.TpsDenominator &&
                   NumLoops == other.NumLoops &&
                   HaveTimecodes == other.HaveTimecodes;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlAnimationHeader other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(TpsNumerator, TpsDenominator, NumLoops, HaveTimecodes);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlAnimationHeader"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlAnimationHeader left, JxlAnimationHeader right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlAnimationHeader"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlAnimationHeader left, JxlAnimationHeader right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Basic image information. This information is available from the file
    /// signature and first part of the codestream header.
    /// </summary>
    public struct JxlBasicInfo
    {
        /// <summary>
        /// Whether the codestream is embedded in the container format.
        /// </summary>
        public bool HaveContainer { get; set; }

        /// <summary>
        /// Width of the image in pixels, before applying orientation.
        /// </summary>
        public uint XSize { get; set; }

        /// <summary>
        /// Height of the image in pixels, before applying orientation.
        /// </summary>
        public uint YSize { get; set; }

        /// <summary>
        /// Original image color channel bit depth.
        /// </summary>
        public uint BitsPerSample { get; set; }

        /// <summary>
        /// Original image color channel floating point exponent bits, or 0 if they are unsigned integer.
        /// </summary>
        public uint ExponentBitsPerSample { get; set; }

        /// <summary>
        /// Upper bound on the intensity level present in the image in nits.
        /// </summary>
        public float IntensityTarget { get; set; }

        /// <summary>
        /// Lower bound on the intensity level present in the image.
        /// </summary>
        public float MinNits { get; set; }

        /// <summary>
        /// Whether <see cref="LinearBelow"/> is relative to maximum display brightness.
        /// </summary>
        public bool RelativeToMaxDisplay { get; set; }

        /// <summary>
        /// Brightness threshold below which tone mapping leaves pixels linear.
        /// </summary>
        public float LinearBelow { get; set; }

        /// <summary>
        /// Whether the data in the codestream is encoded in the original color profile.
        /// </summary>
        public bool UsesOriginalProfile { get; set; }

        /// <summary>
        /// Indicates a preview image exists near the beginning of the codestream.
        /// </summary>
        public bool HavePreview { get; set; }

        /// <summary>
        /// Indicates animation frames exist in the codestream.
        /// </summary>
        public bool HaveAnimation { get; set; }

        /// <summary>
        /// Image orientation, value 1-8 matching the values used by JEITA CP-3451C (Exif version 2.3).
        /// </summary>
        public JxlOrientation Orientation { get; set; }

        /// <summary>
        /// Number of color channels encoded in the image (1 for grayscale, 3 for color).
        /// </summary>
        public uint NumColorChannels { get; set; }

        /// <summary>
        /// Number of additional image channels.
        /// </summary>
        public uint NumExtraChannels { get; set; }

        /// <summary>
        /// Bit depth of the encoded alpha channel, or 0 if there is no alpha channel.
        /// </summary>
        public uint AlphaBits { get; set; }

        /// <summary>
        /// Alpha channel floating point exponent bits, or 0 if they are unsigned integer.
        /// </summary>
        public uint AlphaExponentBits { get; set; }

        /// <summary>
        /// Whether the alpha channel is premultiplied.
        /// </summary>
        public bool AlphaPremultiplied { get; set; }

        /// <summary>
        /// Dimensions of encoded preview image, only used if <see cref="HavePreview"/> is <c>true</c>.
        /// </summary>
        public JxlPreviewHeader Preview { get; set; }

        /// <summary>
        /// Animation header with global animation properties for all frames.
        /// </summary>
        public JxlAnimationHeader Animation { get; set; }

        /// <summary>
        /// Intrinsic width of the image.
        /// </summary>
        public uint IntrinsicXSize { get; set; }

        /// <summary>
        /// Intrinsic height of the image.
        /// </summary>
        public uint IntrinsicYSize { get; set; }
    }

    /// <summary>
    /// Information for a single extra channel.
    /// </summary>
    public struct JxlExtraChannelInfo
    {
        /// <summary>
        /// Given type of an extra channel.
        /// </summary>
        public JxlExtraChannelType Type { get; set; }

        /// <summary>
        /// Total bits per sample for this channel.
        /// </summary>
        public uint BitsPerSample { get; set; }

        /// <summary>
        /// Floating point exponent bits per channel, or 0 if they are unsigned integer.
        /// </summary>
        public uint ExponentBitsPerSample { get; set; }

        /// <summary>
        /// The exponent the channel is downsampled by on each axis.
        /// </summary>
        public uint DimShift { get; set; }

        /// <summary>
        /// Length of the extra channel name in bytes, or 0 if no name. Excludes null termination character.
        /// </summary>
        public uint NameLength { get; set; }

        /// <summary>
        /// Whether alpha channel uses premultiplied alpha. Only applicable if type is <see cref="JxlExtraChannelType.Alpha"/>.
        /// </summary>
        public bool AlphaPremultiplied { get; set; }

        /// <summary>
        /// Spot color red component in linear RGBA.
        /// </summary>
        public float SpotColorR { get; set; }

        /// <summary>
        /// Spot color green component in linear RGBA.
        /// </summary>
        public float SpotColorG { get; set; }

        /// <summary>
        /// Spot color blue component in linear RGBA.
        /// </summary>
        public float SpotColorB { get; set; }

        /// <summary>
        /// Spot color alpha component in linear RGBA.
        /// </summary>
        public float SpotColorA { get; set; }

        /// <summary>
        /// CFA channel index. Only applicable if type is <see cref="JxlExtraChannelType.Cfa"/>.
        /// </summary>
        public uint CfaChannel { get; set; }
    }

    /// <summary>
    /// Extensions in the codestream header.
    /// </summary>
    public struct JxlHeaderExtensions : IEquatable<JxlHeaderExtensions>
    {
        /// <summary>
        /// Extension bits.
        /// </summary>
        public ulong Extensions { get; set; }

        /// <inheritdoc />
        public bool Equals(JxlHeaderExtensions other)
        {
            return Extensions == other.Extensions;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlHeaderExtensions other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Extensions.GetHashCode();
        }

        /// <summary>
        /// Determines whether two <see cref="JxlHeaderExtensions"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlHeaderExtensions left, JxlHeaderExtensions right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlHeaderExtensions"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlHeaderExtensions left, JxlHeaderExtensions right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The information about blending the color channels or a single extra channel.
    /// When decoding, if coalescing is enabled (default), this can be ignored and
    /// the blend mode is considered to be <see cref="JxlBlendMode.Replace"/>.
    /// When encoding, these settings apply to the pixel data given to the encoder.
    /// </summary>
    public struct JxlBlendInfo : IEquatable<JxlBlendInfo>
    {
        /// <summary>
        /// Blend mode.
        /// </summary>
        public JxlBlendMode BlendMode { get; set; }

        /// <summary>
        /// Reference frame ID to use as the 'bottom' layer (0-3).
        /// </summary>
        public uint Source { get; set; }

        /// <summary>
        /// Which extra channel to use as the 'alpha' channel for blend modes
        /// <see cref="JxlBlendMode.Blend"/> and <see cref="JxlBlendMode.MulAdd"/>.
        /// </summary>
        public uint Alpha { get; set; }

        /// <summary>
        /// Clamp values to [0,1] for the purpose of blending.
        /// </summary>
        public bool Clamp { get; set; }

        /// <inheritdoc />
        public bool Equals(JxlBlendInfo other)
        {
            return BlendMode == other.BlendMode &&
                   Source == other.Source &&
                   Alpha == other.Alpha &&
                   Clamp == other.Clamp;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlBlendInfo other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(BlendMode, Source, Alpha, Clamp);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBlendInfo"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlBlendInfo left, JxlBlendInfo right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBlendInfo"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlBlendInfo left, JxlBlendInfo right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The information about layers.
    /// When decoding, if coalescing is enabled (default), this can be ignored.
    /// When encoding, these settings apply to the pixel data given to the encoder,
    /// the encoder could choose an internal representation that differs.
    /// </summary>
    public struct JxlLayerInfo
    {
        /// <summary>
        /// Whether cropping is applied for this frame.
        /// </summary>
        public bool HaveCrop { get; set; }

        /// <summary>
        /// Horizontal offset of the frame (can be negative).
        /// </summary>
        public int CropX0 { get; set; }

        /// <summary>
        /// Vertical offset of the frame (can be negative).
        /// </summary>
        public int CropY0 { get; set; }

        /// <summary>
        /// Width of the frame (number of columns).
        /// </summary>
        public uint XSize { get; set; }

        /// <summary>
        /// Height of the frame (number of rows).
        /// </summary>
        public uint YSize { get; set; }

        /// <summary>
        /// The blending info for the color channels.
        /// </summary>
        public JxlBlendInfo BlendInfo { get; set; }

        /// <summary>
        /// After blending, save the frame as reference frame with this ID (0-3).
        /// </summary>
        public uint SaveAsReference { get; set; }
    }

    /// <summary>
    /// The header of one displayed frame or non-coalesced layer.
    /// </summary>
    public struct JxlFrameHeader
    {
        /// <summary>
        /// How long to wait after rendering in ticks.
        /// </summary>
        public uint Duration { get; set; }

        /// <summary>
        /// SMPTE timecode of the current frame in form 0xHHMMSSFF, or 0.
        /// </summary>
        public uint Timecode { get; set; }

        /// <summary>
        /// Length of the frame name in bytes, or 0 if no name.
        /// </summary>
        public uint NameLength { get; set; }

        /// <summary>
        /// Indicates this is the last animation frame.
        /// </summary>
        public bool IsLast { get; set; }

        /// <summary>
        /// Information about the layer in case of no coalescing.
        /// </summary>
        public JxlLayerInfo LayerInfo { get; set; }
    }
}
