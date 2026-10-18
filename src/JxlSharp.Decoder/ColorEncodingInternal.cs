// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Valid enum bitmasks for JPEG XL color encoding fields.
    /// Ported from <c>lib/jxl/color_encoding_internal.h</c>.
    /// </summary>
    public static class ColorEncodingEnumBits
    {
        /// <summary>Valid bitmask for <see cref="ColorSpace"/>.</summary>
        public static readonly ulong ColorSpaceBits =
            FieldEncoding.MakeBit(ColorSpace.RGB) |
            FieldEncoding.MakeBit(ColorSpace.Gray) |
            FieldEncoding.MakeBit(ColorSpace.XYB) |
            FieldEncoding.MakeBit(ColorSpace.Unknown);

        /// <summary>Valid bitmask for <see cref="WhitePoint"/>.</summary>
        public static readonly ulong WhitePointBits =
            FieldEncoding.MakeBit(WhitePoint.D65) |
            FieldEncoding.MakeBit(WhitePoint.Custom) |
            FieldEncoding.MakeBit(WhitePoint.E) |
            FieldEncoding.MakeBit(WhitePoint.DCI);

        /// <summary>Valid bitmask for <see cref="Primaries"/>.</summary>
        public static readonly ulong PrimariesBits =
            FieldEncoding.MakeBit(Primaries.SRGB) |
            FieldEncoding.MakeBit(Primaries.Custom) |
            FieldEncoding.MakeBit(Primaries.P2100) |
            FieldEncoding.MakeBit(Primaries.P3);

        /// <summary>Valid bitmask for <see cref="TransferFunction"/>.</summary>
        public static readonly ulong TransferFunctionBits =
            FieldEncoding.MakeBit(TransferFunction.Tf709) |
            FieldEncoding.MakeBit(TransferFunction.Linear) |
            FieldEncoding.MakeBit(TransferFunction.SRGB) |
            FieldEncoding.MakeBit(TransferFunction.PQ) |
            FieldEncoding.MakeBit(TransferFunction.DCI) |
            FieldEncoding.MakeBit(TransferFunction.HLG) |
            FieldEncoding.MakeBit(TransferFunction.Unknown);

        /// <summary>Valid bitmask for <see cref="RenderingIntent"/>.</summary>
        public static readonly ulong RenderingIntentBits =
            FieldEncoding.MakeBit(RenderingIntent.Perceptual) |
            FieldEncoding.MakeBit(RenderingIntent.Relative) |
            FieldEncoding.MakeBit(RenderingIntent.Saturation) |
            FieldEncoding.MakeBit(RenderingIntent.Absolute);
    }

    /// <summary>
    /// Serializable <see cref="Fields"/> bundle for <see cref="Customxy"/>.
    /// Ported from <c>jxl::Customxy</c> in <c>lib/jxl/color_encoding_internal.h</c> and <c>color_encoding_internal.cc</c>.
    /// </summary>
    public sealed class CustomxyBundle : Fields
    {
        /// <summary>Underlying quantized chromaticity storage.</summary>
        public Customxy Storage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomxyBundle"/> class with default values.
        /// </summary>
        public CustomxyBundle()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "Customxy";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            uint ux = PackSigned.Pack(Storage.X);

            if (!visitor.U32(
                U32Distr.Bits(19),
                U32Distr.BitsOffset(19, 524288),
                U32Distr.BitsOffset(20, 1048576),
                U32Distr.BitsOffset(21, 2097152),
                0,
                ref ux))
            {
                return false;
            }

            Storage.X = PackSigned.Unpack(ux);
            uint uy   = PackSigned.Pack(Storage.Y);

            if (!visitor.U32(
                U32Distr.Bits(19),
                U32Distr.BitsOffset(19, 524288),
                U32Distr.BitsOffset(20, 1048576),
                U32Distr.BitsOffset(21, 2097152),
                0,
                ref uy))
            {
                return false;
            }

            Storage.Y = PackSigned.Unpack(uy);

            return true;
        }
    }

    /// <summary>
    /// Serializable <see cref="Fields"/> bundle for <see cref="CustomTransferFunction"/>.
    /// Ported from <c>jxl::CustomTransferFunction</c> in <c>lib/jxl/color_encoding_internal.h</c> and <c>color_encoding_internal.cc</c>.
    /// </summary>
    public sealed class CustomTransferFunctionBundle : Fields
    {
        /// <summary>Color space context (must be set before calling <see cref="VisitFields"/>).</summary>
        public ColorSpace NonserializedColorSpace = ColorSpace.RGB;

        /// <summary>Underlying transfer function storage.</summary>
        public CustomTransferFunction Storage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomTransferFunctionBundle"/> class with default values.
        /// </summary>
        public CustomTransferFunctionBundle()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "CustomTransferFunction";

        /// <summary>
        /// Sets fields and returns <c>true</c> if <see cref="NonserializedColorSpace"/> has an implicit
        /// transfer function; otherwise leaves fields unchanged and returns <c>false</c>.
        /// </summary>
        /// <returns><c>true</c> if an implicit transfer function was set.</returns>
        public bool SetImplicit()
        {
            if (NonserializedColorSpace == ColorSpace.XYB)
            {
                return Storage.SetGamma(1.0 / 3.0).IsOk;
            }

            return false;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.Conditional(!SetImplicit()))
            {
                return true;
            }

            if (!visitor.Bool(false, ref Storage.HaveGamma))
            {
                return false;
            }

            if (visitor.Conditional(Storage.HaveGamma) && !VisitGamma(visitor))
            {
                return false;
            }

            if (visitor.Conditional(!Storage.HaveGamma))
            {
                return visitor.Enum(
                    TransferFunction.SRGB,
                    ref Storage.TransferFunction,
                    ColorEncodingEnumBits.TransferFunctionBits);
            }

            return true;
        }

        private Status VisitGamma(Visitor visitor)
        {
            // Gamma is represented as a 24-bit int, the exponent used is
            // gamma_ / 1e7. Valid values are (0, 1]. On the low end side, we also
            // limit it to kMaxGamma/1e7.
            if (!visitor.Bits(24, CustomTransferFunction.KGammaMul, ref Storage.Gamma))
            {
                return false;
            }

            if (Storage.Gamma > CustomTransferFunction.KGammaMul ||
                (ulong)Storage.Gamma * CustomTransferFunction.KMaxGamma < CustomTransferFunction.KGammaMul)
            {
                return Status.Failure($"Invalid gamma {Storage.Gamma}");
            }

            return true;
        }
    }

    /// <summary>
    /// Compact encoding of data required to interpret and translate pixels to a known color space.
    /// Ported from <c>jxl::ColorEncoding</c> in <c>lib/jxl/color_encoding_internal.h</c> and <c>color_encoding_internal.cc</c>.
    /// </summary>
    public sealed class ColorEncoding : Fields
    {
        private readonly CustomxyBundle _white = new CustomxyBundle();
        private readonly CustomTransferFunctionBundle _tf = new CustomTransferFunctionBundle();
        private readonly CustomxyBundle _red = new CustomxyBundle();
        private readonly CustomxyBundle _green = new CustomxyBundle();
        private readonly CustomxyBundle _blue = new CustomxyBundle();

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Underlying CMS color encoding storage.</summary>
        public CmsColorEncoding Storage = new CmsColorEncoding();

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorEncoding"/> class with default values.
        /// </summary>
        public ColorEncoding()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "ColorEncoding";

        /// <summary>
        /// Gets a reference to the underlying <see cref="CustomTransferFunction"/>.
        /// </summary>
        public ref CustomTransferFunction Tf => ref Storage.Tf;

        /// <summary>
        /// Gets the underlying <see cref="CmsColorEncoding"/> view.
        /// </summary>
        public CmsColorEncoding View => Storage;

        /// <summary>
        /// Returns a ready-to-use sRGB <see cref="ColorEncoding"/>.
        /// </summary>
        /// <param name="isGray">Whether to return the grayscale variant.</param>
        /// <returns>sRGB <see cref="ColorEncoding"/>.</returns>
        public static ColorEncoding SRGB(bool isGray = false)
        {
            return new ColorEncoding
            {
                Storage = CmsColorEncoding.SRGB(isGray),
            };
        }

        /// <summary>
        /// Returns a ready-to-use linear sRGB <see cref="ColorEncoding"/>.
        /// </summary>
        /// <param name="isGray">Whether to return the grayscale variant.</param>
        /// <returns>Linear sRGB <see cref="ColorEncoding"/>.</returns>
        public static ColorEncoding LinearSRGB(bool isGray = false)
        {
            return new ColorEncoding
            {
                Storage = CmsColorEncoding.LinearSRGB(isGray),
            };
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="ColorEncoding"/>.
        /// </summary>
        /// <returns>Cloned <see cref="ColorEncoding"/>.</returns>
        public ColorEncoding Clone()
        {
            return new ColorEncoding
            {
                AllDefault = AllDefault,
                Storage    = Storage.Clone(),
            };
        }

        /// <summary>
        /// Copies all fields and ICC bytes from <paramref name="other"/> into this instance.
        /// </summary>
        /// <param name="other">Source <see cref="ColorEncoding"/>.</param>
        public void CopyFrom(ColorEncoding other)
        {
            AllDefault = other.AllDefault;
            Storage    = other.Storage.Clone();
        }

        /// <summary>
        /// Generates an ICC profile from the current fields.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if an ICC profile was created.</returns>
        public Status CreateICC() => Storage.CreateICC();

        /// <summary>
        /// Returns the raw ICC profile bytes.
        /// </summary>
        /// <returns>ICC profile byte array.</returns>
        public byte[] ICC() => Storage.Icc;

        /// <summary>
        /// Assigns and decodes <paramref name="icc"/> using <paramref name="cms"/>.
        /// </summary>
        /// <param name="icc">ICC profile bytes.</param>
        /// <param name="cms">CMS interface.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetICC(byte[] icc, JxlCmsInterface cms) => Storage.SetICC(icc, cms);

        /// <summary>
        /// Sets the raw ICC profile bytes without parsing fields.
        /// </summary>
        /// <param name="icc">Raw ICC profile bytes.</param>
        public void SetICCRaw(byte[] icc)
        {
            Storage.Icc        = icc;
            Storage.HaveFields = false;
            Storage.WantIcc    = true;
        }

        /// <summary>
        /// Returns whether to send the raw ICC profile in the codestream.
        /// </summary>
        /// <returns><c>true</c> if raw ICC is wanted.</returns>
        public bool WantICC() => Storage.WantIcc;

        /// <summary>
        /// Returns whether the direct color space fields are set.
        /// </summary>
        /// <returns><c>true</c> if fields are valid.</returns>
        public bool HaveFields() => Storage.HaveFields;

        /// <summary>
        /// Causes <see cref="WantICC"/> to return <c>false</c> if the ICC profile can be reconstructed from fields.
        /// </summary>
        /// <param name="cms">CMS interface.</param>
        public void DecideIfWantICC(JxlCmsInterface cms)
        {
            if (Storage.Icc.Length == 0 || cms.SetFieldsFromIcc == null)
            {
                return;
            }

            JxlColorEncoding c = default;

            if (!cms.SetFieldsFromIcc(cms.SetFieldsData, Storage.Icc, ref c, out bool cmyk) || cmyk)
            {
                return;
            }

            if (!JxlCmsInternal.MaybeCreateProfile(c, out _))
            {
                return;
            }

            Storage.WantIcc = false;
        }

        /// <summary>Returns whether the color space is grayscale.</summary>
        /// <returns><c>true</c> if grayscale.</returns>
        public bool IsGray() => Storage.IsGray;

        /// <summary>Returns whether the color space is CMYK.</summary>
        /// <returns><c>true</c> if CMYK.</returns>
        public bool IsCMYK() => Storage.IsCMYK;

        /// <summary>Returns the number of color channels (1 for grayscale, 3 otherwise).</summary>
        /// <returns>Number of channels.</returns>
        public nuint Channels() => Storage.Channels();

        /// <summary>Returns whether primaries are applicable for this color space.</summary>
        /// <returns><c>true</c> if primaries apply.</returns>
        public bool HasPrimaries() => Storage.HasPrimaries();

        /// <summary>
        /// Sets the white point to D65 and returns <c>true</c> if the color space is XYB; otherwise <c>false</c>.
        /// </summary>
        /// <returns><c>true</c> if white point is implicit.</returns>
        public bool ImplicitWhitePoint()
        {
            if (Storage.ColorSpace == ColorSpace.XYB)
            {
                Storage.WhitePoint = WhitePoint.D65;

                return true;
            }

            return false;
        }

        /// <summary>Returns whether the color space is known to be sRGB (or sRGB gray).</summary>
        /// <returns><c>true</c> if sRGB.</returns>
        public bool IsSRGB()
        {
            if (!Storage.HaveFields)
            {
                return false;
            }

            if (!IsGray() && Storage.ColorSpace != ColorSpace.RGB)
            {
                return false;
            }

            return Storage.WhitePoint == WhitePoint.D65 &&
                   Storage.Primaries == Primaries.SRGB &&
                   Storage.Tf.IsSRGB();
        }

        /// <summary>Returns whether the color space is known to be linear sRGB (or linear sRGB gray).</summary>
        /// <returns><c>true</c> if linear sRGB.</returns>
        public bool IsLinearSRGB()
        {
            if (!Storage.HaveFields)
            {
                return false;
            }

            if (!IsGray() && Storage.ColorSpace != ColorSpace.RGB)
            {
                return false;
            }

            return Storage.WhitePoint == WhitePoint.D65 &&
                   Storage.Primaries == Primaries.SRGB &&
                   Storage.Tf.IsLinear();
        }

        /// <summary>
        /// Configures this instance as sRGB (RGB or Gray) and generates its ICC profile.
        /// </summary>
        /// <param name="cs">Color space (<see cref="ColorSpace.RGB"/> or <see cref="ColorSpace.Gray"/>).</param>
        /// <param name="ri">Rendering intent.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetSRGB(ColorSpace cs, RenderingIntent ri = RenderingIntent.Relative)
        {
            Storage.Icc = Array.Empty<byte>();

            if (cs != ColorSpace.Gray && cs != ColorSpace.RGB)
            {
                return Status.Failure("Invalid color space for sRGB");
            }

            Storage.ColorSpace          = cs;
            Storage.WhitePoint          = WhitePoint.D65;
            Storage.Primaries           = Primaries.SRGB;
            Storage.Tf.TransferFunction = TransferFunction.SRGB;
            Storage.Tf.HaveGamma        = false;
            Storage.RenderingIntent     = ri;

            return CreateICC();
        }

        /// <summary>Gets the color space.</summary>
        /// <returns>Current <see cref="ColorSpace"/>.</returns>
        public ColorSpace GetColorSpace() => Storage.ColorSpace;

        /// <summary>Sets the color space.</summary>
        /// <param name="cs">Color space to set.</param>
        public void SetColorSpace(ColorSpace cs) => Storage.ColorSpace = cs;

        /// <summary>Gets the CIE xy white point coordinates.</summary>
        /// <returns>White point <see cref="CIExy"/>.</returns>
        public CIExy GetWhitePoint() => Storage.GetWhitePoint();

        /// <summary>Sets the white point from CIE xy coordinates.</summary>
        /// <param name="xy">White point coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetWhitePoint(CIExy xy) => Storage.SetWhitePoint(xy);

        /// <summary>Gets the white point enum type.</summary>
        /// <returns>Current <see cref="WhitePoint"/>.</returns>
        public WhitePoint GetWhitePointType() => Storage.WhitePoint;

        /// <summary>Sets the white point enum type.</summary>
        /// <param name="wp">White point type to set.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetWhitePointType(WhitePoint wp)
        {
            if (!Storage.HaveFields)
            {
                return Status.Failure("Fields not available");
            }

            Storage.WhitePoint = wp;

            return true;
        }

        /// <summary>Gets the RGB primary CIE xy coordinates.</summary>
        /// <param name="p">Output primaries coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status GetPrimaries(out PrimariesCIExy p) => Storage.GetPrimaries(out p);

        /// <summary>Sets the RGB primaries from CIE xy coordinates.</summary>
        /// <param name="xy">Primaries coordinates.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetPrimaries(PrimariesCIExy xy) => Storage.SetPrimaries(xy);

        /// <summary>Gets the primaries enum type.</summary>
        /// <returns>Current <see cref="Primaries"/>.</returns>
        public Primaries GetPrimariesType() => Storage.Primaries;

        /// <summary>Sets the primaries enum type.</summary>
        /// <param name="p">Primaries type to set.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetPrimariesType(Primaries p)
        {
            if (!Storage.HaveFields || !HasPrimaries())
            {
                return Status.Failure("Primaries not available");
            }

            Storage.Primaries = p;

            return true;
        }

        /// <summary>Gets the rendering intent.</summary>
        /// <returns>Current <see cref="RenderingIntent"/>.</returns>
        public RenderingIntent GetRenderingIntent() => Storage.RenderingIntent;

        /// <summary>Sets the rendering intent.</summary>
        /// <param name="ri">Rendering intent to set.</param>
        public void SetRenderingIntent(RenderingIntent ri) => Storage.RenderingIntent = ri;

        /// <summary>Checks whether two <see cref="ColorEncoding"/> instances have the same color encoding.</summary>
        /// <param name="other">Other color encoding.</param>
        /// <returns><c>true</c> if identical.</returns>
        public bool SameColorEncoding(ColorEncoding other) => Storage.SameColorEncoding(other.Storage);

        /// <summary>Checks whether two <see cref="ColorEncoding"/> instances have the same color space.</summary>
        /// <param name="other">Other color encoding.</param>
        /// <returns><c>true</c> if same color space.</returns>
        public bool SameColorSpace(ColorEncoding other) => Storage.SameColorSpace(other.Storage);

        /// <summary>Converts this instance to an external <see cref="JxlColorEncoding"/>.</summary>
        /// <returns>External <see cref="JxlColorEncoding"/>.</returns>
        public JxlColorEncoding ToExternal() => Storage.ToExternal();

        /// <summary>Populates this instance from an external <see cref="JxlColorEncoding"/> and generates an ICC profile.</summary>
        /// <param name="external">External color encoding.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status FromExternal(JxlColorEncoding external)
        {
            if (!Storage.FromExternal(external))
            {
                return false;
            }

            CreateICC();

            return true;
        }

        /// <summary>Returns a human-readable description of this color encoding.</summary>
        /// <returns>Description string.</returns>
        public string Description()
        {
            JxlColorEncoding external = Storage.ToExternal();

            return JxlCmsInternal.ColorEncodingDescription(external);
        }

        /// <inheritdoc />
        public override string ToString() => Description();

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                // Overwrite all serialized fields, but not any nonserialized_*.
                visitor.SetDefault(this);

                return true;
            }

            if (!visitor.Bool(false, ref Storage.WantIcc))
            {
                return false;
            }

            // Always send even if want_icc_ because this affects decoding.
            // We can skip the white point/primaries because they do not.
            if (!visitor.Enum(ColorSpace.RGB, ref Storage.ColorSpace, ColorEncodingEnumBits.ColorSpaceBits))
            {
                return false;
            }

            if (visitor.Conditional(!WantICC()) && !VisitNonIccFields(visitor))
            {
                return false;
            }

            if (WantICC() && visitor.IsReading)
            {
                // Haven't called SetICC() yet, do nothing.
                return true;
            }

            return ICC().Length != 0 ? Status.OkStatus : Status.Failure("Empty ICC");
        }

        private Status VisitNonIccFields(Visitor visitor)
        {
            if (!VisitWhitePointAndPrimaries(visitor))
            {
                return false;
            }

            _tf.NonserializedColorSpace = Storage.ColorSpace;
            _tf.Storage                 = Storage.Tf;

            if (!visitor.VisitNested(_tf))
            {
                return false;
            }

            Storage.Tf = _tf.Storage;

            if (!visitor.Enum(
                RenderingIntent.Relative,
                ref Storage.RenderingIntent,
                ColorEncodingEnumBits.RenderingIntentBits))
            {
                return false;
            }

            // We didn't have ICC, so all fields should be known.
            if (Storage.ColorSpace == ColorSpace.Unknown || Storage.Tf.IsUnknown())
            {
                return Status.Failure("No ICC but ColorSpace or TransferFunction is Unknown");
            }

            return CreateICC();
        }

        private Status VisitWhitePointAndPrimaries(Visitor visitor)
        {
            if (visitor.Conditional(!ImplicitWhitePoint()))
            {
                if (!visitor.Enum(WhitePoint.D65, ref Storage.WhitePoint, ColorEncodingEnumBits.WhitePointBits))
                {
                    return false;
                }

                if (visitor.Conditional(Storage.WhitePoint == WhitePoint.Custom))
                {
                    _white.Storage = Storage.White;

                    if (!visitor.VisitNested(_white))
                    {
                        return false;
                    }

                    Storage.White = _white.Storage;
                }
            }

            if (visitor.Conditional(HasPrimaries()))
            {
                return VisitPrimariesFields(visitor);
            }

            return true;
        }

        private Status VisitPrimariesFields(Visitor visitor)
        {
            if (!visitor.Enum(Primaries.SRGB, ref Storage.Primaries, ColorEncodingEnumBits.PrimariesBits))
            {
                return false;
            }

            if (!visitor.Conditional(Storage.Primaries == Primaries.Custom))
            {
                return true;
            }

            _red.Storage = Storage.Red;

            if (!visitor.VisitNested(_red))
            {
                return false;
            }

            Storage.Red    = _red.Storage;
            _green.Storage = Storage.Green;

            if (!visitor.VisitNested(_green))
            {
                return false;
            }

            Storage.Green = _green.Storage;
            _blue.Storage = Storage.Blue;

            if (!visitor.VisitNested(_blue))
            {
                return false;
            }

            Storage.Blue = _blue.Storage;

            return true;
        }
    }

    /// <summary>
    /// Scoped wrapper around <see cref="JxlCmsInterface"/> for executing color space transforms.
    /// Ported from <c>jxl::ColorSpaceTransform</c> in <c>lib/jxl/color_encoding_internal.h</c>.
    /// </summary>
    public sealed class ColorSpaceTransform : IDisposable
    {
        private readonly JxlCmsInterface _cms;
        private object? _cmsData;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorSpaceTransform"/> class.
        /// </summary>
        /// <param name="cms">CMS interface implementation.</param>
        public ColorSpaceTransform(JxlCmsInterface cms)
        {
            _cms = cms;
        }

        /// <summary>
        /// Initializes the color space transform between <paramref name="cSrc"/> and <paramref name="cDst"/>.
        /// </summary>
        /// <param name="cSrc">Source color encoding.</param>
        /// <param name="cDst">Destination color encoding.</param>
        /// <param name="intensityTarget">Intensity target in nits.</param>
        /// <param name="xsize">Number of pixels per thread buffer.</param>
        /// <param name="numThreads">Maximum number of concurrent threads.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Init(
            ColorEncoding cSrc,
            ColorEncoding cDst,
            float intensityTarget,
            nuint xsize,
            nuint numThreads)
        {
            if (cDst.IsCMYK())
            {
                return Status.Failure("Conversion to CMYK is not supported");
            }

            if (_cms.Init == null)
            {
                return Status.Failure("CMS Init callback is null");
            }

            JxlColorProfile inputProfile = new JxlColorProfile
            {
                IccData       = cSrc.ICC(),
                ColorEncoding = cSrc.ToExternal(),
                NumChannels   = cSrc.IsCMYK() ? 4u : cSrc.Channels(),
            };
            JxlColorProfile outputProfile = new JxlColorProfile
            {
                IccData       = cDst.ICC(),
                ColorEncoding = cDst.ToExternal(),
                NumChannels   = cDst.Channels(),
            };

            _cmsData = _cms.Init(_cms.InitData, numThreads, xsize, inputProfile, outputProfile, intensityTarget);

            return _cmsData != null;
        }

        /// <summary>
        /// Returns the source pixel buffer for the specified <paramref name="thread"/>.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <returns>Source float buffer.</returns>
        public float[]? BufSrc(nuint thread)
        {
            return _cms.GetSrcBuf?.Invoke(_cmsData, thread);
        }

        /// <summary>
        /// Returns the destination pixel buffer for the specified <paramref name="thread"/>.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <returns>Destination float buffer.</returns>
        public float[]? BufDst(nuint thread)
        {
            return _cms.GetDstBuf?.Invoke(_cmsData, thread);
        }

        /// <summary>
        /// Runs the color space transform on <paramref name="xsize"/> pixels.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <param name="bufSrc">Input pixel buffer.</param>
        /// <param name="bufDst">Output pixel buffer.</param>
        /// <param name="xsize">Number of pixels to transform.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Run(nuint thread, ReadOnlySpan<float> bufSrc, Span<float> bufDst, nuint xsize)
        {
            if (_cms.Run == null)
            {
                return false;
            }

            return _cms.Run(_cmsData, thread, bufSrc, bufDst, xsize);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_cmsData != null)
            {
                _cms.Destroy?.Invoke(_cmsData);
                _cmsData = null;
            }
        }
    }
}
