// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Color space of the image data.
    /// </summary>
    public enum JxlColorSpace
    {
        /// <summary>
        /// Tristimulus RGB.
        /// </summary>
        Rgb = 0,

        /// <summary>
        /// Luminance based, the primaries in <see cref="JxlColorEncoding"/> must be ignored.
        /// This value implies that <c>NumColorChannels</c> in <see cref="JxlBasicInfo"/> is 1,
        /// any other value implies <c>NumColorChannels</c> is 3.
        /// </summary>
        Gray = 1,

        /// <summary>
        /// XYB (opsin) color space.
        /// </summary>
        Xyb = 2,

        /// <summary>
        /// None of the other table entries describe the color space appropriately.
        /// </summary>
        Unknown = 3,
    }

    /// <summary>
    /// Built-in white points for color encoding.
    /// </summary>
    public enum JxlWhitePoint
    {
        /// <summary>
        /// CIE Standard Illuminant D65: 0.3127, 0.3290.
        /// </summary>
        D65 = 1,

        /// <summary>
        /// White point must be read from the <see cref="JxlColorEncoding"/> white point fields, or as ICC profile.
        /// </summary>
        Custom = 2,

        /// <summary>
        /// CIE Standard Illuminant E (equal-energy): 1/3, 1/3.
        /// </summary>
        E = 10,

        /// <summary>
        /// DCI-P3 from SMPTE RP 431-2: 0.314, 0.351.
        /// </summary>
        Dci = 11,
    }

    /// <summary>
    /// Built-in primaries for color encoding.
    /// </summary>
    public enum JxlPrimaries
    {
        /// <summary>
        /// The CIE xy values of the red, green and blue primaries are:
        /// 0.639998686, 0.330010138; 0.300003784, 0.600003357; 0.150002046, 0.059997204.
        /// </summary>
        Srgb = 1,

        /// <summary>
        /// Primaries must be read from the <see cref="JxlColorEncoding"/> primary xy fields, or as ICC profile.
        /// </summary>
        Custom = 2,

        /// <summary>
        /// As specified in Rec. ITU-R BT.2100-1.
        /// </summary>
        Bt2100 = 9,

        /// <summary>
        /// Alias for <see cref="Bt2100"/> (Rec. ITU-R BT.2100-1).
        /// </summary>
        P2100 = 9,

        /// <summary>
        /// As specified in SMPTE RP 431-2.
        /// </summary>
        P3 = 11,
    }

    /// <summary>
    /// Built-in transfer functions for color encoding.
    /// </summary>
    public enum JxlTransferFunction
    {
        /// <summary>
        /// As specified in ITU-R BT.709-6.
        /// </summary>
        Bt709 = 1,

        /// <summary>
        /// None of the other table entries describe the transfer function.
        /// </summary>
        Unknown = 2,

        /// <summary>
        /// The gamma exponent is 1.
        /// </summary>
        Linear = 8,

        /// <summary>
        /// As specified in IEC 61966-2-1 sRGB.
        /// </summary>
        Srgb = 13,

        /// <summary>
        /// As specified in SMPTE ST 2084.
        /// </summary>
        Pq = 16,

        /// <summary>
        /// As specified in SMPTE ST 428-1.
        /// </summary>
        Dci = 17,

        /// <summary>
        /// As specified in Rec. ITU-R BT.2100-1 (HLG).
        /// </summary>
        Hlg = 18,

        /// <summary>
        /// Transfer function follows power law given by the gamma value in <see cref="JxlColorEncoding"/>.
        /// </summary>
        Gamma = 65535,
    }

    /// <summary>
    /// Rendering intent for color encoding, as specified in ISO 15076-1:2010.
    /// </summary>
    public enum JxlRenderingIntent
    {
        /// <summary>
        /// Perceptual rendering intent (vendor-specific).
        /// </summary>
        Perceptual = 0,

        /// <summary>
        /// Media-relative colorimetric rendering intent.
        /// </summary>
        Relative = 1,

        /// <summary>
        /// Saturation rendering intent (vendor-specific).
        /// </summary>
        Saturation = 2,

        /// <summary>
        /// ICC-absolute colorimetric rendering intent.
        /// </summary>
        Absolute = 3,
    }

    /// <summary>
    /// 2D CIE xy chromaticity coordinate pair.
    /// </summary>
    public struct ColorCoordinate : IEquatable<ColorCoordinate>
    {
        /// <summary>
        /// Gets or sets the CIE x coordinate.
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Gets or sets the CIE y coordinate.
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorCoordinate"/> struct.
        /// </summary>
        /// <param name="x">CIE x coordinate.</param>
        /// <param name="y">CIE y coordinate.</param>
        public ColorCoordinate(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <inheritdoc />
        public readonly bool Equals(ColorCoordinate other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        /// <inheritdoc />
        public override readonly bool Equals(object? obj)
        {
            return obj is ColorCoordinate other && Equals(other);
        }

        /// <inheritdoc />
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal.</returns>
        public static bool operator ==(ColorCoordinate left, ColorCoordinate right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal.</returns>
        public static bool operator !=(ColorCoordinate left, ColorCoordinate right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Color encoding of the image as structured information
    /// (ported from <c>include/jxl/color_encoding.h</c>).
    /// </summary>
    public struct JxlColorEncoding : IEquatable<JxlColorEncoding>
    {
        /// <summary>
        /// Color space of the image data.
        /// </summary>
        public JxlColorSpace ColorSpace { get; set; }

        /// <summary>
        /// Built-in white point. If <see cref="JxlWhitePoint.Custom"/>, use <see cref="WhitePointX"/> and <see cref="WhitePointY"/>.
        /// </summary>
        public JxlWhitePoint WhitePoint { get; set; }

        /// <summary>
        /// Numerical whitepoint X value in CIE xy space.
        /// </summary>
        public double WhitePointX { get; set; }

        /// <summary>
        /// Numerical whitepoint Y value in CIE xy space.
        /// </summary>
        public double WhitePointY { get; set; }

        /// <summary>
        /// Gets or sets the white point CIE xy coordinates as a pair.
        /// </summary>
        public ColorCoordinate WhitePointXy
        {
            readonly get => new ColorCoordinate(WhitePointX, WhitePointY);
            set
            {
                WhitePointX = value.X;
                WhitePointY = value.Y;
            }
        }

        /// <summary>
        /// Built-in RGB primaries. If <see cref="JxlPrimaries.Custom"/>, use the numerical primary fields.
        /// </summary>
        public JxlPrimaries Primaries { get; set; }

        /// <summary>
        /// Numerical red primary X value in CIE xy space.
        /// </summary>
        public double PrimariesRedX { get; set; }

        /// <summary>
        /// Numerical red primary Y value in CIE xy space.
        /// </summary>
        public double PrimariesRedY { get; set; }

        /// <summary>
        /// Gets or sets the red primary CIE xy coordinates as a pair.
        /// </summary>
        public ColorCoordinate PrimariesRedXy
        {
            readonly get => new ColorCoordinate(PrimariesRedX, PrimariesRedY);
            set
            {
                PrimariesRedX = value.X;
                PrimariesRedY = value.Y;
            }
        }

        /// <summary>
        /// Numerical green primary X value in CIE xy space.
        /// </summary>
        public double PrimariesGreenX { get; set; }

        /// <summary>
        /// Numerical green primary Y value in CIE xy space.
        /// </summary>
        public double PrimariesGreenY { get; set; }

        /// <summary>
        /// Gets or sets the green primary CIE xy coordinates as a pair.
        /// </summary>
        public ColorCoordinate PrimariesGreenXy
        {
            readonly get => new ColorCoordinate(PrimariesGreenX, PrimariesGreenY);
            set
            {
                PrimariesGreenX = value.X;
                PrimariesGreenY = value.Y;
            }
        }

        /// <summary>
        /// Numerical blue primary X value in CIE xy space.
        /// </summary>
        public double PrimariesBlueX { get; set; }

        /// <summary>
        /// Numerical blue primary Y value in CIE xy space.
        /// </summary>
        public double PrimariesBlueY { get; set; }

        /// <summary>
        /// Gets or sets the blue primary CIE xy coordinates as a pair.
        /// </summary>
        public ColorCoordinate PrimariesBlueXy
        {
            readonly get => new ColorCoordinate(PrimariesBlueX, PrimariesBlueY);
            set
            {
                PrimariesBlueX = value.X;
                PrimariesBlueY = value.Y;
            }
        }

        /// <summary>
        /// Transfer function.
        /// </summary>
        public JxlTransferFunction TransferFunction { get; set; }

        /// <summary>
        /// Gamma value used when <see cref="TransferFunction"/> is <see cref="JxlTransferFunction.Gamma"/>.
        /// </summary>
        public double Gamma { get; set; }

        /// <summary>
        /// Rendering intent defined for the color profile.
        /// </summary>
        public JxlRenderingIntent RenderingIntent { get; set; }

        /// <inheritdoc />
        public bool Equals(JxlColorEncoding other)
        {
            return ColorSpace == other.ColorSpace &&
                   WhitePoint == other.WhitePoint &&
                   WhitePointX.Equals(other.WhitePointX) &&
                   WhitePointY.Equals(other.WhitePointY) &&
                   Primaries == other.Primaries &&
                   PrimariesRedX.Equals(other.PrimariesRedX) &&
                   PrimariesRedY.Equals(other.PrimariesRedY) &&
                   PrimariesGreenX.Equals(other.PrimariesGreenX) &&
                   PrimariesGreenY.Equals(other.PrimariesGreenY) &&
                   PrimariesBlueX.Equals(other.PrimariesBlueX) &&
                   PrimariesBlueY.Equals(other.PrimariesBlueY) &&
                   TransferFunction == other.TransferFunction &&
                   Gamma.Equals(other.Gamma) &&
                   RenderingIntent == other.RenderingIntent;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlColorEncoding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hc = default;
            hc.Add(ColorSpace);
            hc.Add(WhitePoint);
            hc.Add(WhitePointX);
            hc.Add(WhitePointY);
            hc.Add(Primaries);
            hc.Add(TransferFunction);
            hc.Add(Gamma);
            hc.Add(RenderingIntent);

            return hc.ToHashCode();
        }

        /// <summary>
        /// Determines whether two <see cref="JxlColorEncoding"/> values are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlColorEncoding left, JxlColorEncoding right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlColorEncoding"/> values are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlColorEncoding left, JxlColorEncoding right)
        {
            return !left.Equals(right);
        }
    }
}
