// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Color space the color pixel data is encoded in.
    /// Ported from <c>jxl::cms::ColorSpace</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public enum ColorSpace : uint
    {
        /// <summary>Trichromatic color data (also includes CMYK if a kBlack ExtraChannelInfo is present).</summary>
        RGB = 0,

        /// <summary>Single-channel grayscale data.</summary>
        Gray = 1,

        /// <summary>XYB color space (implies fixed values for primaries/white point).</summary>
        XYB = 2,

        /// <summary>Non-RGB/gray data.</summary>
        Unknown = 3,
    }

    /// <summary>
    /// White point values from CICP ColourPrimaries.
    /// Ported from <c>jxl::cms::WhitePoint</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public enum WhitePoint : uint
    {
        /// <summary>sRGB / BT.709 / Display P3 / BT.2020 D65 white point.</summary>
        D65 = 1,

        /// <summary>Custom white point encoded in separate fields.</summary>
        Custom = 2,

        /// <summary>Equal-energy (XYZ) white point.</summary>
        E = 10,

        /// <summary>DCI-P3 white point.</summary>
        DCI = 11,
    }

    /// <summary>
    /// Primaries values from CICP ColourPrimaries.
    /// Ported from <c>jxl::cms::Primaries</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public enum Primaries : uint
    {
        /// <summary>sRGB / BT.709 primaries.</summary>
        SRGB = 1,

        /// <summary>Custom primaries encoded in separate fields.</summary>
        Custom = 2,

        /// <summary>BT.2100 / BT.2020 primaries.</summary>
        P2100 = 9,

        /// <summary>P3 primaries.</summary>
        P3 = 11,
    }

    /// <summary>
    /// Transfer function values from CICP TransferCharacteristics.
    /// Ported from <c>jxl::cms::TransferFunction</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public enum TransferFunction : uint
    {
        /// <summary>BT.709 transfer function.</summary>
        Tf709 = 1,

        /// <summary>Unknown transfer function.</summary>
        Unknown = 2,

        /// <summary>Linear light transfer function.</summary>
        Linear = 8,

        /// <summary>sRGB transfer function.</summary>
        SRGB = 13,

        /// <summary>BT.2100 Perceptual Quantization (PQ) transfer function.</summary>
        PQ = 16,

        /// <summary>SMPTE RP 431-2 DCI transfer function.</summary>
        DCI = 17,

        /// <summary>BT.2100 Hybrid Log-Gamma (HLG) transfer function.</summary>
        HLG = 18,
    }

    /// <summary>
    /// ICC rendering intent.
    /// Ported from <c>jxl::cms::RenderingIntent</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public enum RenderingIntent : uint
    {
        /// <summary>Perceptual rendering intent.</summary>
        Perceptual = 0,

        /// <summary>Media-relative colorimetric rendering intent.</summary>
        Relative = 1,

        /// <summary>Saturation rendering intent.</summary>
        Saturation = 2,

        /// <summary>ICC-absolute colorimetric rendering intent.</summary>
        Absolute = 3,
    }

    /// <summary>
    /// CIE 1931 2-degree observer chromaticity coordinates (x, y).
    /// Ported from <c>jxl::cms::CIExy</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public struct CIExy : IEquatable<CIExy>
    {
        /// <summary>Chromaticity x coordinate.</summary>
        public double X;

        /// <summary>Chromaticity y coordinate.</summary>
        public double Y;

        /// <summary>
        /// Initializes a new instance of the <see cref="CIExy"/> struct.
        /// </summary>
        /// <param name="x">Chromaticity x coordinate.</param>
        /// <param name="y">Chromaticity y coordinate.</param>
        public CIExy(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <inheritdoc />
        public bool Equals(CIExy other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is CIExy other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{{x={X}, y={Y}}}";
        }
    }

    /// <summary>
    /// Red, green, and blue primary chromaticities.
    /// Ported from <c>jxl::cms::PrimariesCIExy</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public struct PrimariesCIExy : IEquatable<PrimariesCIExy>
    {
        /// <summary>Red primary chromaticity.</summary>
        public CIExy R;

        /// <summary>Green primary chromaticity.</summary>
        public CIExy G;

        /// <summary>Blue primary chromaticity.</summary>
        public CIExy B;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimariesCIExy"/> struct.
        /// </summary>
        /// <param name="r">Red primary chromaticity.</param>
        /// <param name="g">Green primary chromaticity.</param>
        /// <param name="b">Blue primary chromaticity.</param>
        public PrimariesCIExy(CIExy r, CIExy g, CIExy b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <inheritdoc />
        public bool Equals(PrimariesCIExy other)
        {
            return R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PrimariesCIExy other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(R, G, B);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{{r={R}, g={G}, b={B}}}";
        }
    }

    /// <summary>
    /// Serializable fixed-point form of <see cref="CIExy"/>.
    /// Ported from <c>jxl::cms::Customxy</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public struct Customxy
    {
        /// <summary>Fixed-point multiplier (<c>1,000,000</c>).</summary>
        public const uint KMul = 1000000;

        /// <summary>Rough limit for valid chromaticity coordinates.</summary>
        public const double KRoughLimit = 4.0;

        /// <summary>Minimum quantized coordinate value.</summary>
        public const int KMin = -0x200000;

        /// <summary>Maximum quantized coordinate value.</summary>
        public const int KMax = 0x1FFFFF;

        /// <summary>Quantized x coordinate.</summary>
        public int X;

        /// <summary>Quantized y coordinate.</summary>
        public int Y;

        /// <summary>
        /// Converts the quantized coordinates back to <see cref="CIExy"/>.
        /// </summary>
        /// <returns>Chromaticity coordinates.</returns>
        public readonly CIExy GetValue()
        {
            return new CIExy(X * (1.0 / KMul), Y * (1.0 / KMul));
        }

        /// <summary>
        /// Quantizes and stores the given <see cref="CIExy"/> coordinates.
        /// </summary>
        /// <param name="xy">Chromaticity coordinates to store.</param>
        /// <returns><see cref="Status.OkStatus"/> if in bounds; failure otherwise.</returns>
        public Status SetValue(CIExy xy)
        {
            bool ok = (Math.Abs(xy.X) < KRoughLimit) && (Math.Abs(xy.Y) < KRoughLimit);

            if (!ok)
            {
                return Status.Failure("X or Y is out of bounds");
            }

            X = (int)MathF.Round((float)(xy.X * KMul), MidpointRounding.AwayFromZero);

            if (X < KMin || X > KMax)
            {
                return Status.Failure("X is out of bounds");
            }

            Y = (int)MathF.Round((float)(xy.Y * KMul), MidpointRounding.AwayFromZero);

            if (Y < KMin || Y > KMax)
            {
                return Status.Failure("Y is out of bounds");
            }

            return true;
        }

        /// <summary>
        /// Checks whether another <see cref="Customxy"/> has identical quantized coordinates.
        /// </summary>
        /// <param name="other">Other value to compare.</param>
        /// <returns><c>true</c> if equal.</returns>
        public readonly bool IsSame(Customxy other)
        {
            return X == other.X && Y == other.Y;
        }
    }

    /// <summary>
    /// Serializable transfer function or custom gamma exponent.
    /// Ported from <c>jxl::cms::CustomTransferFunction</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>.
    /// </summary>
    public struct CustomTransferFunction
    {
        /// <summary>Highest reasonable value for the gamma of a transfer curve.</summary>
        public const uint KMaxGamma = 8192;

        /// <summary>Fixed-point multiplier for serialized gamma (<c>10,000,000</c>).</summary>
        public const uint KGammaMul = 10000000;

        /// <summary>Whether a custom gamma exponent is used instead of an enumerated transfer function.</summary>
        public bool HaveGamma;

        /// <summary>Quantized OETF gamma exponent (only used if <see cref="HaveGamma"/> is <c>true</c>).</summary>
        public uint Gamma;

        /// <summary>Enumerated transfer function (only used if <see cref="HaveGamma"/> is <c>false</c>).</summary>
        public TransferFunction TransferFunction;

        /// <summary>
        /// Creates a default <see cref="CustomTransferFunction"/> initialized to <see cref="Cms.TransferFunction.SRGB"/>.
        /// </summary>
        /// <returns>Default <see cref="CustomTransferFunction"/>.</returns>
        public static CustomTransferFunction CreateDefault()
        {
            return new CustomTransferFunction
            {
                HaveGamma        = false,
                Gamma            = 0,
                TransferFunction = TransferFunction.SRGB,
            };
        }

        /// <summary>
        /// Gets the enumerated transfer function (or <see cref="Cms.TransferFunction.Unknown"/> if <see cref="HaveGamma"/> is <c>true</c>).
        /// </summary>
        /// <returns>The <see cref="Cms.TransferFunction"/>.</returns>
        public readonly TransferFunction GetTransferFunction()
        {
            return HaveGamma ? TransferFunction.Unknown : TransferFunction;
        }

        /// <summary>
        /// Sets an enumerated transfer function and clears <see cref="HaveGamma"/>.
        /// </summary>
        /// <param name="tf">Transfer function to set.</param>
        public void SetTransferFunction(TransferFunction tf)
        {
            HaveGamma        = false;
            TransferFunction = tf;
        }

        /// <summary>Gets a value indicating whether the transfer function is unknown.</summary>
        /// <returns><c>true</c> if unknown.</returns>
        public readonly bool IsUnknown() => !HaveGamma && TransferFunction == TransferFunction.Unknown;

        /// <summary>Gets a value indicating whether the transfer function is sRGB.</summary>
        /// <returns><c>true</c> if sRGB.</returns>
        public readonly bool IsSRGB() => !HaveGamma && TransferFunction == TransferFunction.SRGB;

        /// <summary>Gets a value indicating whether the transfer function is linear.</summary>
        /// <returns><c>true</c> if linear.</returns>
        public readonly bool IsLinear() => !HaveGamma && TransferFunction == TransferFunction.Linear;

        /// <summary>Gets a value indicating whether the transfer function is PQ.</summary>
        /// <returns><c>true</c> if PQ.</returns>
        public readonly bool IsPQ() => !HaveGamma && TransferFunction == TransferFunction.PQ;

        /// <summary>Gets a value indicating whether the transfer function is HLG.</summary>
        /// <returns><c>true</c> if HLG.</returns>
        public readonly bool IsHLG() => !HaveGamma && TransferFunction == TransferFunction.HLG;

        /// <summary>Gets a value indicating whether the transfer function is BT.709.</summary>
        /// <returns><c>true</c> if BT.709.</returns>
        public readonly bool Is709() => !HaveGamma && TransferFunction == TransferFunction.Tf709;

        /// <summary>Gets a value indicating whether the transfer function is DCI.</summary>
        /// <returns><c>true</c> if DCI.</returns>
        public readonly bool IsDCI() => !HaveGamma && TransferFunction == TransferFunction.DCI;

        /// <summary>
        /// Returns the custom gamma value in <c>(0, 1]</c>, or <c>0.0</c> if <see cref="HaveGamma"/> is <c>false</c>.
        /// </summary>
        /// <returns>Gamma value.</returns>
        public readonly double GetGamma()
        {
            if (!HaveGamma)
            {
                return 0.0;
            }

            return Gamma * (1.0 / KGammaMul);
        }

        /// <summary>
        /// Sets a custom gamma value in <c>[1 / KMaxGamma, 1.0]</c>, mapping <c>1.0</c> to Linear and <c>1/2.6</c> to DCI.
        /// </summary>
        /// <param name="newGamma">Gamma exponent.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid; failure otherwise.</returns>
        public Status SetGamma(double newGamma)
        {
            if (newGamma < (1.0 / KMaxGamma) || newGamma > 1.0)
            {
                return Status.Failure("Invalid gamma");
            }

            HaveGamma = false;

            if (CmsColorEncoding.ApproxEq(newGamma, 1.0))
            {
                TransferFunction = TransferFunction.Linear;

                return true;
            }

            if (CmsColorEncoding.ApproxEq(newGamma, 1.0 / 2.6))
            {
                TransferFunction = TransferFunction.DCI;

                return true;
            }

            // Don't translate 0.45.. to kSRGB nor k709 - that might change pixel
            // values because those curves also have a linear part.
            HaveGamma        = true;
            Gamma            = (uint)MathF.Round((float)(newGamma * KGammaMul), MidpointRounding.AwayFromZero);
            TransferFunction = TransferFunction.Unknown;

            return true;
        }

        /// <summary>
        /// Checks whether two <see cref="CustomTransferFunction"/> instances represent the same transfer curve.
        /// </summary>
        /// <param name="other">Other transfer function.</param>
        /// <returns><c>true</c> if identical.</returns>
        public readonly bool IsSame(CustomTransferFunction other)
        {
            if (HaveGamma != other.HaveGamma)
            {
                return false;
            }

            if (HaveGamma)
            {
                return Gamma == other.Gamma;
            }

            return TransferFunction == other.TransferFunction;
        }
    }

    /// <summary>
    /// Compact encoding of data required to interpret and translate pixels to a known color space.
    /// Ported from <c>jxl::cms::ColorEncoding</c> in <c>lib/jxl/cms/color_encoding_cms.h</c>
    /// and <c>jxl::ColorEncoding</c> in <c>lib/jxl/color_encoding_internal.h</c>.
    /// </summary>
    public sealed class CmsColorEncoding
    {
        private static readonly CmsColorEncoding CachedSrgbColor       = CreatePredefined(Primaries.SRGB, TransferFunction.SRGB, isGray: false);
        private static readonly CmsColorEncoding CachedSrgbGray        = CreatePredefined(Primaries.SRGB, TransferFunction.SRGB, isGray: true);
        private static readonly CmsColorEncoding CachedLinearSrgbColor = CreatePredefined(Primaries.SRGB, TransferFunction.Linear, isGray: false);
        private static readonly CmsColorEncoding CachedLinearSrgbGray  = CreatePredefined(Primaries.SRGB, TransferFunction.Linear, isGray: true);

        /// <summary>White point enum value (only valid if <see cref="HaveFields"/> is <c>true</c>).</summary>
        public WhitePoint WhitePoint = WhitePoint.D65;

        /// <summary>Primaries enum value (only valid if <see cref="HasPrimaries"/> is <c>true</c>).</summary>
        public Primaries Primaries = Primaries.SRGB;

        /// <summary>Rendering intent.</summary>
        public RenderingIntent RenderingIntent = RenderingIntent.Relative;

        /// <summary>Whether direct color space fields are valid.</summary>
        public bool HaveFields = true;

        /// <summary>Raw ICC profile bytes.</summary>
        public byte[] Icc = Array.Empty<byte>();

        /// <summary>Color space.</summary>
        public ColorSpace ColorSpace = ColorSpace.RGB;

        /// <summary>Whether the ICC profile represents CMYK data.</summary>
        public bool Cmyk;

        /// <summary>Transfer function configuration.</summary>
        public CustomTransferFunction Tf = CustomTransferFunction.CreateDefault();

        /// <summary>Custom white point coordinates (used when <see cref="WhitePoint"/> is <see cref="Cms.WhitePoint.Custom"/>).</summary>
        public Customxy White;

        /// <summary>Custom red primary coordinates (used when <see cref="Primaries"/> is <see cref="Cms.Primaries.Custom"/>).</summary>
        public Customxy Red;

        /// <summary>Custom green primary coordinates (used when <see cref="Primaries"/> is <see cref="Cms.Primaries.Custom"/>).</summary>
        public Customxy Green;

        /// <summary>Custom blue primary coordinates (used when <see cref="Primaries"/> is <see cref="Cms.Primaries.Custom"/>).</summary>
        public Customxy Blue;

        /// <summary>Whether to send the raw ICC profile in the codestream.</summary>
        public bool WantIcc;

        /// <summary>
        /// Gets a value indicating whether the color space is grayscale.
        /// </summary>
        public bool IsGray => ColorSpace == ColorSpace.Gray;

        /// <summary>
        /// Gets a value indicating whether the color space is CMYK.
        /// </summary>
        public bool IsCMYK => Cmyk;

        /// <summary>
        /// Returns whether two double values are approximately equal within <paramref name="maxL1"/>.
        /// </summary>
        /// <param name="a">First value.</param>
        /// <param name="b">Second value.</param>
        /// <param name="maxL1">Maximum absolute difference.</param>
        /// <returns><c>true</c> if within tolerance.</returns>
        public static bool ApproxEq(double a, double b, double maxL1 = 1e-3)
        {
            return Math.Abs(a - b) <= maxL1;
        }

        /// <summary>
        /// Returns a ready-to-use sRGB <see cref="CmsColorEncoding"/>.
        /// </summary>
        /// <param name="isGray">Whether to return the grayscale variant.</param>
        /// <returns>Cloned sRGB <see cref="CmsColorEncoding"/>.</returns>
        public static CmsColorEncoding SRGB(bool isGray = false)
        {
            return (isGray ? CachedSrgbGray : CachedSrgbColor).Clone();
        }

        /// <summary>
        /// Returns a ready-to-use linear sRGB <see cref="CmsColorEncoding"/>.
        /// </summary>
        /// <param name="isGray">Whether to return the grayscale variant.</param>
        /// <returns>Cloned linear sRGB <see cref="CmsColorEncoding"/>.</returns>
        public static CmsColorEncoding LinearSRGB(bool isGray = false)
        {
            return (isGray ? CachedLinearSrgbGray : CachedLinearSrgbColor).Clone();
        }

        /// <summary>
        /// Converts an external <see cref="JxlWhitePoint"/> to internal <see cref="WhitePoint"/>.
        /// </summary>
        /// <param name="external">External white point enum.</param>
        /// <param name="output">Converted internal white point.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status WhitePointFromExternal(JxlWhitePoint external, out WhitePoint output)
        {
            switch (external)
            {
                case JxlWhitePoint.D65:
                    output = WhitePoint.D65;

                    return true;
                case JxlWhitePoint.Custom:
                    output = WhitePoint.Custom;

                    return true;
                case JxlWhitePoint.E:
                    output = WhitePoint.E;

                    return true;
                case JxlWhitePoint.Dci:
                    output = WhitePoint.DCI;

                    return true;
                default:
                    output = default;

                    return Status.Failure("Invalid WhitePoint enum value");
            }
        }

        /// <summary>
        /// Converts an external <see cref="JxlPrimaries"/> to internal <see cref="Primaries"/>.
        /// </summary>
        /// <param name="external">External primaries enum.</param>
        /// <param name="output">Converted internal primaries.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status PrimariesFromExternal(JxlPrimaries external, out Primaries output)
        {
            switch (external)
            {
                case JxlPrimaries.Srgb:
                    output = Primaries.SRGB;

                    return true;
                case JxlPrimaries.Custom:
                    output = Primaries.Custom;

                    return true;
                case JxlPrimaries.P2100:
                    output = Primaries.P2100;

                    return true;
                case JxlPrimaries.P3:
                    output = Primaries.P3;

                    return true;
                default:
                    output = default;

                    return Status.Failure("Invalid Primaries enum value");
            }
        }

        /// <summary>
        /// Converts an external <see cref="JxlRenderingIntent"/> to internal <see cref="RenderingIntent"/>.
        /// </summary>
        /// <param name="external">External rendering intent enum.</param>
        /// <param name="output">Converted internal rendering intent.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status RenderingIntentFromExternal(JxlRenderingIntent external, out RenderingIntent output)
        {
            switch (external)
            {
                case JxlRenderingIntent.Perceptual:
                    output = RenderingIntent.Perceptual;

                    return true;
                case JxlRenderingIntent.Relative:
                    output = RenderingIntent.Relative;

                    return true;
                case JxlRenderingIntent.Saturation:
                    output = RenderingIntent.Saturation;

                    return true;
                case JxlRenderingIntent.Absolute:
                    output = RenderingIntent.Absolute;

                    return true;
                default:
                    output = default;

                    return Status.Failure("Invalid RenderingIntent enum value");
            }
        }

        /// <summary>
        /// Converts an external <see cref="JxlTransferFunction"/> to internal <see cref="TransferFunction"/>.
        /// </summary>
        /// <param name="external">External transfer function enum.</param>
        /// <param name="internalTf">Converted internal transfer function.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status ConvertExternalToInternalTransferFunction(
            JxlTransferFunction external,
            out TransferFunction internalTf)
        {
            switch (external)
            {
                case JxlTransferFunction.Bt709:
                    internalTf = TransferFunction.Tf709;

                    return true;
                case JxlTransferFunction.Unknown:
                    internalTf = TransferFunction.Unknown;

                    return true;
                case JxlTransferFunction.Linear:
                    internalTf = TransferFunction.Linear;

                    return true;
                case JxlTransferFunction.Srgb:
                    internalTf = TransferFunction.SRGB;

                    return true;
                case JxlTransferFunction.Pq:
                    internalTf = TransferFunction.PQ;

                    return true;
                case JxlTransferFunction.Dci:
                    internalTf = TransferFunction.DCI;

                    return true;
                case JxlTransferFunction.Hlg:
                    internalTf = TransferFunction.HLG;

                    return true;
                case JxlTransferFunction.Gamma:
                    internalTf = default;

                    return Status.Failure("Gamma should be handled separately");
                default:
                    internalTf = default;

                    return Status.Failure("Invalid TransferFunction enum value");
            }
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="CmsColorEncoding"/>.
        /// </summary>
        /// <returns>Cloned <see cref="CmsColorEncoding"/>.</returns>
        public CmsColorEncoding Clone()
        {
            byte[] iccCopy = new byte[Icc.Length];
            Array.Copy(Icc, iccCopy, Icc.Length);

            return new CmsColorEncoding
            {
                WhitePoint      = WhitePoint,
                Primaries       = Primaries,
                RenderingIntent = RenderingIntent,
                HaveFields      = HaveFields,
                Icc             = iccCopy,
                ColorSpace      = ColorSpace,
                Cmyk            = Cmyk,
                Tf              = Tf,
                White           = White,
                Red             = Red,
                Green           = Green,
                Blue            = Blue,
                WantIcc         = WantIcc,
            };
        }

        /// <summary>
        /// Returns <c>false</c> if primaries are not applicable for <see cref="ColorSpace"/>.
        /// </summary>
        /// <returns><c>true</c> if primaries apply.</returns>
        public bool HasPrimaries()
        {
            return ColorSpace != ColorSpace.Gray && ColorSpace != ColorSpace.XYB;
        }

        /// <summary>
        /// Returns the number of color channels (1 for Gray, 3 otherwise).
        /// </summary>
        /// <returns>Channel count.</returns>
        public nuint Channels()
        {
            return ColorSpace == ColorSpace.Gray ? 1u : 3u;
        }

        /// <summary>
        /// Retrieves the CIE xy coordinates of the RGB primaries.
        /// </summary>
        /// <param name="xy">Output primaries coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status GetPrimaries(out PrimariesCIExy xy)
        {
            xy = default;

            if (!HaveFields || !HasPrimaries())
            {
                return Status.Failure("Primaries not available");
            }

            switch (Primaries)
            {
                case Primaries.Custom:
                    xy.R = Red.GetValue();
                    xy.G = Green.GetValue();
                    xy.B = Blue.GetValue();
                    break;

                case Primaries.SRGB:
                    xy.R = new CIExy(0.639998686, 0.330010138);
                    xy.G = new CIExy(0.300003784, 0.600003357);
                    xy.B = new CIExy(0.150002046, 0.059997204);
                    break;

                case Primaries.P2100:
                    xy.R = new CIExy(0.708, 0.292);
                    xy.G = new CIExy(0.170, 0.797);
                    xy.B = new CIExy(0.131, 0.046);
                    break;

                case Primaries.P3:
                    xy.R = new CIExy(0.680, 0.320);
                    xy.G = new CIExy(0.265, 0.690);
                    xy.B = new CIExy(0.150, 0.060);
                    break;

                default:
                    return Status.Failure("Unexpected Primaries enum");
            }

            return true;
        }

        /// <summary>
        /// Sets the primaries from CIE xy coordinates, matching standard primaries when within tolerance.
        /// </summary>
        /// <param name="xy">Primaries coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetPrimaries(PrimariesCIExy xy)
        {
            if (!HaveFields || !HasPrimaries())
            {
                return Status.Failure("Primaries not available");
            }

            if (xy.R.X == 0.0 || xy.R.Y == 0.0 || xy.G.X == 0.0 || xy.G.Y == 0.0 || xy.B.X == 0.0 || xy.B.Y == 0.0)
            {
                return Status.Failure("Invalid primaries");
            }

            if (MatchesStandardPrimaries(xy, out Primaries matched))
            {
                Primaries = matched;

                return true;
            }

            Primaries = Primaries.Custom;

            if (!Red.SetValue(xy.R))
            {
                return Status.Failure("Invalid red primary");
            }

            if (!Green.SetValue(xy.G))
            {
                return Status.Failure("Invalid green primary");
            }

            return Blue.SetValue(xy.B);
        }

        /// <summary>
        /// Gets the white point CIE xy coordinates.
        /// </summary>
        /// <returns>White point <see cref="CIExy"/>.</returns>
        public CIExy GetWhitePoint()
        {
            if (!HaveFields)
            {
                return default;
            }

            return WhitePoint switch
            {
                WhitePoint.Custom => White.GetValue(),
                WhitePoint.D65    => new CIExy(0.3127, 0.3290),
                WhitePoint.DCI    => new CIExy(0.314, 0.351),
                WhitePoint.E      => new CIExy(1.0 / 3.0, 1.0 / 3.0),
                _                 => default,
            };
        }

        /// <summary>
        /// Sets the white point from CIE xy coordinates, matching standard white points when within tolerance.
        /// </summary>
        /// <param name="xy">White point coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetWhitePoint(CIExy xy)
        {
            if (!HaveFields)
            {
                return Status.Failure("Fields not available");
            }

            if (xy.X == 0.0 || xy.Y == 0.0)
            {
                return Status.Failure("Invalid white point");
            }

            if (ApproxEq(xy.X, 0.3127) && ApproxEq(xy.Y, 0.3290))
            {
                WhitePoint = WhitePoint.D65;

                return true;
            }

            if (ApproxEq(xy.X, 1.0 / 3.0) && ApproxEq(xy.Y, 1.0 / 3.0))
            {
                WhitePoint = WhitePoint.E;

                return true;
            }

            if (ApproxEq(xy.X, 0.314) && ApproxEq(xy.Y, 0.351))
            {
                WhitePoint = WhitePoint.DCI;

                return true;
            }

            WhitePoint = WhitePoint.Custom;

            return White.SetValue(xy);
        }

        /// <summary>
        /// Checks if the color spaces (including white point / primaries) are the same,
        /// ignoring transfer function, rendering intent, and ICC bytes.
        /// </summary>
        /// <param name="other">Other color encoding.</param>
        /// <returns><c>true</c> if same color space.</returns>
        public bool SameColorSpace(CmsColorEncoding other)
        {
            if (ColorSpace != other.ColorSpace || WhitePoint != other.WhitePoint)
            {
                return false;
            }

            if (WhitePoint == WhitePoint.Custom && !White.IsSame(other.White))
            {
                return false;
            }

            if (HasPrimaries() != other.HasPrimaries())
            {
                return false;
            }

            return !HasPrimaries() || SamePrimaries(other);
        }

        /// <summary>
        /// Checks if the color space and transfer function are the same, ignoring rendering intent and ICC bytes.
        /// </summary>
        /// <param name="other">Other color encoding.</param>
        /// <returns><c>true</c> if same color encoding.</returns>
        public bool SameColorEncoding(CmsColorEncoding other)
        {
            return SameColorSpace(other) && Tf.IsSame(other.Tf) && !Cmyk && !other.Cmyk;
        }

        /// <summary>
        /// Populates fields from an ICC profile using <paramref name="cms"/>.
        /// </summary>
        /// <param name="newIcc">ICC profile bytes.</param>
        /// <param name="cms">CMS interface.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetFieldsFromICC(byte[] newIcc, JxlCmsInterface cms)
        {
            if (newIcc == null || newIcc.Length == 0)
            {
                return Status.Failure("Empty ICC");
            }

            ColorSpace          = ColorSpace.Unknown;
            Tf.TransferFunction = TransferFunction.Unknown;
            Icc                 = Array.Empty<byte>();

            JxlColorEncoding external = default;

            if (cms.SetFieldsFromIcc == null ||
                !cms.SetFieldsFromIcc(cms.SetFieldsData, newIcc, ref external, out bool newCmyk))
            {
                return Status.Failure("SetFieldsFromICC failed");
            }

            Cmyk = newCmyk;

            if (!FromExternal(external))
            {
                return Status.Failure("FromExternal failed");
            }

            Icc = newIcc;

            return true;
        }

        /// <summary>
        /// Sets the ICC profile and parses its fields using <paramref name="cms"/>.
        /// </summary>
        /// <param name="icc">ICC profile bytes.</param>
        /// <param name="cms">CMS interface.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetICC(byte[] icc, JxlCmsInterface cms)
        {
            if (cms == null || icc == null || icc.Length == 0)
            {
                return Status.Failure("Invalid ICC or CMS");
            }

            HaveFields   = true;
            Status valid = SetFieldsFromICC(icc, cms);
            WantIcc      = valid.IsOk;

            return valid;
        }

        /// <summary>
        /// Generates an ICC profile from the current fields and stores it in <see cref="Icc"/>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if an ICC profile was created.</returns>
        public Status CreateICC()
        {
            Icc                       = Array.Empty<byte>();
            JxlColorEncoding external = ToExternal();

            if (!JxlCmsInternal.MaybeCreateProfile(external, out byte[] generated) || generated.Length == 0)
            {
                Icc = Array.Empty<byte>();

                return Status.Failure("Failed to create ICC profile");
            }

            Icc = generated;

            return true;
        }

        /// <summary>
        /// Converts this internal encoding to a <see cref="JxlColorEncoding"/>.
        /// </summary>
        /// <returns>External <see cref="JxlColorEncoding"/>.</returns>
        public JxlColorEncoding ToExternal()
        {
            if (!HaveFields)
            {
                return CreateErrorExternal();
            }

            CIExy wp = GetWhitePoint();
            JxlColorEncoding external = new JxlColorEncoding
            {
                ColorSpace      = (JxlColorSpace)ColorSpace,
                WhitePoint      = (JxlWhitePoint)WhitePoint,
                WhitePointXy    = new ColorCoordinate(wp.X, wp.Y),
                RenderingIntent = (JxlRenderingIntent)RenderingIntent,
            };

            if (external.ColorSpace == JxlColorSpace.Rgb || external.ColorSpace == JxlColorSpace.Unknown)
            {
                external.Primaries = (JxlPrimaries)Primaries;

                if (!GetPrimaries(out PrimariesCIExy p))
                {
                    return CreateErrorExternal();
                }

                external.PrimariesRedXy   = new ColorCoordinate(p.R.X, p.R.Y);
                external.PrimariesGreenXy = new ColorCoordinate(p.G.X, p.G.Y);
                external.PrimariesBlueXy  = new ColorCoordinate(p.B.X, p.B.Y);
            }

            if (Tf.HaveGamma)
            {
                external.TransferFunction = JxlTransferFunction.Gamma;
                external.Gamma            = Tf.GetGamma();
            }
            else
            {
                external.TransferFunction = (JxlTransferFunction)Tf.GetTransferFunction();
                external.Gamma            = 0.0;
            }

            return external;
        }

        /// <summary>
        /// Populates fields from an external <see cref="JxlColorEncoding"/> (does not create ICC).
        /// </summary>
        /// <param name="external">External color encoding.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status FromExternal(JxlColorEncoding external)
        {
            ColorSpace = (ColorSpace)external.ColorSpace;

            if (!WhitePointFromExternal(external.WhitePoint, out WhitePoint))
            {
                return Status.Failure("Invalid WhitePoint");
            }

            if (external.WhitePoint == JxlWhitePoint.Custom &&
                !SetWhitePoint(new CIExy(external.WhitePointXy.X, external.WhitePointXy.Y)))
            {
                return Status.Failure("Invalid custom WhitePoint");
            }

            if ((external.ColorSpace == JxlColorSpace.Rgb || external.ColorSpace == JxlColorSpace.Unknown) &&
                !ApplyExternalPrimaries(external))
            {
                return Status.Failure("Invalid Primaries");
            }

            if (!ApplyExternalTransferFunction(external))
            {
                return Status.Failure("Invalid TransferFunction");
            }

            if (!RenderingIntentFromExternal(external.RenderingIntent, out RenderingIntent))
            {
                return Status.Failure("Invalid RenderingIntent");
            }

            Icc = Array.Empty<byte>();

            return true;
        }

        private static CmsColorEncoding CreatePredefined(Primaries pr, TransferFunction tf, bool isGray)
        {
            CmsColorEncoding c = new CmsColorEncoding
            {
                ColorSpace = isGray ? ColorSpace.Gray : ColorSpace.RGB,
                WhitePoint = WhitePoint.D65,
                Primaries  = pr,
            };
            c.Tf.SetTransferFunction(tf);
            c.CreateICC();

            return c;
        }

        private static JxlColorEncoding CreateErrorExternal()
        {
            return new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Unknown,
                Primaries        = JxlPrimaries.Custom,
                RenderingIntent  = JxlRenderingIntent.Perceptual,
                TransferFunction = JxlTransferFunction.Unknown,
                WhitePoint       = JxlWhitePoint.Custom,
            };
        }

        private static bool MatchesStandardPrimaries(PrimariesCIExy xy, out Primaries matched)
        {
            if (ApproxEq(xy.R.X, 0.64) && ApproxEq(xy.R.Y, 0.33) &&
                ApproxEq(xy.G.X, 0.30) && ApproxEq(xy.G.Y, 0.60) &&
                ApproxEq(xy.B.X, 0.15) && ApproxEq(xy.B.Y, 0.06))
            {
                matched = Primaries.SRGB;

                return true;
            }

            if (ApproxEq(xy.R.X, 0.708) && ApproxEq(xy.R.Y, 0.292) &&
                ApproxEq(xy.G.X, 0.170) && ApproxEq(xy.G.Y, 0.797) &&
                ApproxEq(xy.B.X, 0.131) && ApproxEq(xy.B.Y, 0.046))
            {
                matched = Primaries.P2100;

                return true;
            }

            if (ApproxEq(xy.R.X, 0.680) && ApproxEq(xy.R.Y, 0.320) &&
                ApproxEq(xy.G.X, 0.265) && ApproxEq(xy.G.Y, 0.690) &&
                ApproxEq(xy.B.X, 0.150) && ApproxEq(xy.B.Y, 0.060))
            {
                matched = Primaries.P3;

                return true;
            }

            matched = default;

            return false;
        }

        private bool SamePrimaries(CmsColorEncoding other)
        {
            if (Primaries != other.Primaries)
            {
                return false;
            }

            if (Primaries != Primaries.Custom)
            {
                return true;
            }

            return Red.IsSame(other.Red) && Green.IsSame(other.Green) && Blue.IsSame(other.Blue);
        }

        private Status ApplyExternalPrimaries(JxlColorEncoding external)
        {
            if (!PrimariesFromExternal(external.Primaries, out Primaries))
            {
                return Status.Failure("Invalid Primaries");
            }

            if (external.Primaries != JxlPrimaries.Custom)
            {
                return true;
            }

            PrimariesCIExy newPrimaries = new PrimariesCIExy(
                new CIExy(external.PrimariesRedXy.X, external.PrimariesRedXy.Y),
                new CIExy(external.PrimariesGreenXy.X, external.PrimariesGreenXy.Y),
                new CIExy(external.PrimariesBlueXy.X, external.PrimariesBlueXy.Y));

            return SetPrimaries(newPrimaries);
        }

        private Status ApplyExternalTransferFunction(JxlColorEncoding external)
        {
            CustomTransferFunction newTf = CustomTransferFunction.CreateDefault();

            if (external.TransferFunction == JxlTransferFunction.Gamma)
            {
                if (!newTf.SetGamma(external.Gamma))
                {
                    return Status.Failure("Invalid Gamma");
                }
            }
            else
            {
                if (!ConvertExternalToInternalTransferFunction(external.TransferFunction, out TransferFunction tfEnum))
                {
                    return Status.Failure("Invalid TransferFunction");
                }

                newTf.SetTransferFunction(tfEnum);
            }

            Tf = newTf;

            return true;
        }
    }
}
