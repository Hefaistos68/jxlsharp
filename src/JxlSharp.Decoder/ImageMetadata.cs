// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Main codestream header bundles, the metadata that applies to all frames.
// Enums must align with the C API definitions in codestream_header.h.

using System;
using System.Collections.Generic;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// EXIF orientation of the image (values 1..8 match the EXIF definitions).
    /// Ported from <c>jxl::Orientation</c> in <c>lib/jxl/image_metadata.h</c>.
    /// </summary>
    public enum Orientation : uint
    {
        /// <summary>Identity orientation (1).</summary>
        Identity = (uint)JxlOrientation.Identity,

        /// <summary>Flip horizontal (2).</summary>
        FlipHorizontal = (uint)JxlOrientation.FlipHorizontal,

        /// <summary>Rotate 180 degrees (3).</summary>
        Rotate180 = (uint)JxlOrientation.Rotate180,

        /// <summary>Flip vertical (4).</summary>
        FlipVertical = (uint)JxlOrientation.FlipVertical,

        /// <summary>Transpose (5).</summary>
        Transpose = (uint)JxlOrientation.Transpose,

        /// <summary>Rotate 90 degrees clockwise (6).</summary>
        Rotate90 = (uint)JxlOrientation.Rotate90Cw,

        /// <summary>Anti-transpose (7).</summary>
        AntiTranspose = (uint)JxlOrientation.AntiTranspose,

        /// <summary>Rotate 270 degrees clockwise / 90 CCW (8).</summary>
        Rotate270 = (uint)JxlOrientation.Rotate90Ccw,
    }

    /// <summary>
    /// Type of an extra channel.
    /// Ported from <c>jxl::ExtraChannel</c> in <c>lib/jxl/image_metadata.h</c>.
    /// </summary>
    public enum ExtraChannel : uint
    {
        /// <summary>Alpha channel.</summary>
        Alpha = (uint)JxlExtraChannelType.Alpha,

        /// <summary>Depth channel.</summary>
        Depth = (uint)JxlExtraChannelType.Depth,

        /// <summary>Spot color channel.</summary>
        SpotColor = (uint)JxlExtraChannelType.SpotColor,

        /// <summary>Selection mask channel.</summary>
        SelectionMask = (uint)JxlExtraChannelType.SelectionMask,

        /// <summary>Black channel (for CMYK).</summary>
        Black = (uint)JxlExtraChannelType.Black,

        /// <summary>CFA (Bayer) channel.</summary>
        CFA = (uint)JxlExtraChannelType.Cfa,

        /// <summary>Thermal channel.</summary>
        Thermal = (uint)JxlExtraChannelType.Thermal,

        /// <summary>Reserved channel 0.</summary>
        Reserved0 = (uint)JxlExtraChannelType.Reserved0,

        /// <summary>Reserved channel 1.</summary>
        Reserved1 = (uint)JxlExtraChannelType.Reserved1,

        /// <summary>Reserved channel 2.</summary>
        Reserved2 = (uint)JxlExtraChannelType.Reserved2,

        /// <summary>Reserved channel 3.</summary>
        Reserved3 = (uint)JxlExtraChannelType.Reserved3,

        /// <summary>Reserved channel 4.</summary>
        Reserved4 = (uint)JxlExtraChannelType.Reserved4,

        /// <summary>Reserved channel 5.</summary>
        Reserved5 = (uint)JxlExtraChannelType.Reserved5,

        /// <summary>Reserved channel 6.</summary>
        Reserved6 = (uint)JxlExtraChannelType.Reserved6,

        /// <summary>Reserved channel 7.</summary>
        Reserved7 = (uint)JxlExtraChannelType.Reserved7,

        /// <summary>Unknown extra channel (disambiguated via name string; raise warning if unsupported).</summary>
        Unknown = (uint)JxlExtraChannelType.Unknown,

        /// <summary>Optional extra channel (like <see cref="Unknown"/>, but can silently be ignored).</summary>
        Optional = (uint)JxlExtraChannelType.Optional,
    }

    /// <summary>
    /// Sample bit depth description used in <see cref="ImageMetadata"/> and <see cref="ExtraChannelInfo"/>.
    /// Ported from <c>jxl::BitDepth</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class BitDepth : Fields
    {
        /// <summary>Whether the original (uncompressed) samples are floating point or unsigned integer.</summary>
        public bool FloatingPointSample;

        /// <summary>Bit depth of the original (uncompressed) image samples (1..32).</summary>
        public uint BitsPerSample;

        /// <summary>Floating point exponent bits of the original image samples (only used if <see cref="FloatingPointSample"/> is <c>true</c>).</summary>
        public uint ExponentBitsPerSample;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitDepth"/> class with default values.
        /// </summary>
        public BitDepth()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "BitDepth";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.Bool(false, ref FloatingPointSample))
            {
                return false;
            }

            if (!FloatingPointSample)
            {
                if (!visitor.U32(
                    U32Distr.Val(8),
                    U32Distr.Val(10),
                    U32Distr.Val(12),
                    U32Distr.BitsOffset(6, 1),
                    8,
                    ref BitsPerSample))
                {
                    return false;
                }

                ExponentBitsPerSample = 0;

                return BitsPerSample <= 31 ? Status.OkStatus : Status.Failure($"Invalid bits_per_sample: {BitsPerSample}");
            }

            return VisitFloatingPointFields(visitor);
        }

        /// <summary>
        /// Returns a debug string representation of this bit depth.
        /// </summary>
        /// <returns>Debug string (e.g., <c>"U8"</c> or <c>"F32.8"</c>).</returns>
        public string DebugString()
        {
            string prefix = FloatingPointSample ? "F" : "U";

            return FloatingPointSample
                ? $"{prefix}{BitsPerSample}.{ExponentBitsPerSample}"
                : $"{prefix}{BitsPerSample}";
        }

        private Status VisitFloatingPointFields(Visitor visitor)
        {
            if (!visitor.U32(
                U32Distr.Val(32),
                U32Distr.Val(16),
                U32Distr.Val(24),
                U32Distr.BitsOffset(6, 1),
                32,
                ref BitsPerSample))
            {
                return false;
            }

            const uint offset      = 1;
            ExponentBitsPerSample -= offset;

            if (!visitor.Bits(4, 8 - offset, ref ExponentBitsPerSample))
            {
                return false;
            }

            ExponentBitsPerSample += offset;

            if (ExponentBitsPerSample < 2 || ExponentBitsPerSample > 8)
            {
                return Status.Failure($"Invalid exponent_bits_per_sample: {ExponentBitsPerSample}");
            }

            int mantissaBits = (int)BitsPerSample - (int)ExponentBitsPerSample - 1;

            if (mantissaBits < 2 || mantissaBits > 23)
            {
                return Status.Failure($"Invalid bits_per_sample: {BitsPerSample}");
            }

            return true;
        }
    }

    /// <summary>
    /// Describes one extra channel in <see cref="ImageMetadata"/>.
    /// Ported from <c>jxl::ExtraChannelInfo</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class ExtraChannelInfo : Fields
    {
        /// <summary>Valid bitmask for <see cref="ExtraChannel"/>.</summary>
        public static readonly ulong ExtraChannelBits =
            FieldEncoding.MakeBit(ExtraChannel.Alpha) |
            FieldEncoding.MakeBit(ExtraChannel.Depth) |
            FieldEncoding.MakeBit(ExtraChannel.SpotColor) |
            FieldEncoding.MakeBit(ExtraChannel.SelectionMask) |
            FieldEncoding.MakeBit(ExtraChannel.Black) |
            FieldEncoding.MakeBit(ExtraChannel.CFA) |
            FieldEncoding.MakeBit(ExtraChannel.Thermal) |
            FieldEncoding.MakeBit(ExtraChannel.Unknown) |
            FieldEncoding.MakeBit(ExtraChannel.Optional);

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Type of the extra channel.</summary>
        public ExtraChannel Type;

        /// <summary>Bit depth of the extra channel.</summary>
        public BitDepth BitDepth = new BitDepth();

        /// <summary>Downsampling shift on each axis (<c>2^DimShift</c>).</summary>
        public uint DimShift;

        /// <summary>UTF-8 channel name.</summary>
        public string Name = string.Empty;

        /// <summary>Whether alpha is associated / premultiplied (when <see cref="Type"/> is <see cref="ExtraChannel.Alpha"/>).</summary>
        public bool AlphaAssociated;

        /// <summary>Spot color in linear RGBA (when <see cref="Type"/> is <see cref="ExtraChannel.SpotColor"/>).</summary>
        public float[] SpotColor = new float[4];

        /// <summary>CFA channel index (when <see cref="Type"/> is <see cref="ExtraChannel.CFA"/>).</summary>
        public uint CfaChannel;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtraChannelInfo"/> class with default values.
        /// </summary>
        public ExtraChannelInfo()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "ExtraChannelInfo";

        /// <summary>
        /// Clones this <see cref="ExtraChannelInfo"/>.
        /// </summary>
        /// <returns>Cloned <see cref="ExtraChannelInfo"/>.</returns>
        public ExtraChannelInfo Clone()
        {
            ExtraChannelInfo copy = new ExtraChannelInfo
            {
                AllDefault      = AllDefault,
                Type            = Type,
                DimShift        = DimShift,
                Name            = Name,
                AlphaAssociated = AlphaAssociated,
                CfaChannel      = CfaChannel,
            };
            copy.BitDepth.FloatingPointSample   = BitDepth.FloatingPointSample;
            copy.BitDepth.BitsPerSample         = BitDepth.BitsPerSample;
            copy.BitDepth.ExponentBitsPerSample = BitDepth.ExponentBitsPerSample;
            Array.Copy(SpotColor, copy.SpotColor, 4);

            return copy;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            if (!VisitGeneralFields(visitor))
            {
                return false;
            }

            if (!VisitConditionalFields(visitor))
            {
                return false;
            }

            if (Type == ExtraChannel.Unknown ||
                ((int)Type >= (int)ExtraChannel.Reserved0 && (int)Type <= (int)ExtraChannel.Reserved7))
            {
                return Status.Failure($"Unknown extra channel (bits {BitDepth.BitsPerSample}, shift {DimShift}, name '{Name}')");
            }

            return true;
        }

        /// <summary>
        /// Returns a debug string representation of this extra channel.
        /// </summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            string typeStr = Type switch
            {
                ExtraChannel.Alpha         => "Alpha",
                ExtraChannel.Depth         => "Depth",
                ExtraChannel.SpotColor     => "Spot",
                ExtraChannel.SelectionMask => "Mask",
                ExtraChannel.Black         => "Black",
                ExtraChannel.CFA           => "CFA",
                ExtraChannel.Thermal       => "Thermal",
                _                          => "Unknown",
            };
            string premul = Type == ExtraChannel.Alpha && AlphaAssociated ? "(premul)" : string.Empty;

            return $"{typeStr}{premul} {BitDepth.DebugString()} shift: {DimShift}";
        }

        private Status VisitGeneralFields(Visitor visitor)
        {
            if (!visitor.Enum(ExtraChannel.Alpha, ref Type, ExtraChannelBits))
            {
                return false;
            }

            if (!visitor.VisitNested(BitDepth))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Val(3),
                U32Distr.Val(4),
                U32Distr.BitsOffset(3, 1),
                0,
                ref DimShift))
            {
                return false;
            }

            if ((1u << (int)DimShift) > 8u)
            {
                return Status.Failure($"dim_shift {DimShift} too large");
            }

            return ImageMetadataMethods.VisitNameString(visitor, ref Name);
        }

        private Status VisitConditionalFields(Visitor visitor)
        {
            if (visitor.Conditional(Type == ExtraChannel.Alpha) && !visitor.Bool(false, ref AlphaAssociated))
            {
                return false;
            }

            if (visitor.Conditional(Type == ExtraChannel.SpotColor))
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!visitor.F16(0.0f, ref SpotColor[i]))
                    {
                        return false;
                    }
                }
            }

            if (visitor.Conditional(Type == ExtraChannel.CFA))
            {
                return visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Bits(2),
                    U32Distr.BitsOffset(4, 3),
                    U32Distr.BitsOffset(8, 19),
                    1,
                    ref CfaChannel);
            }

            return true;
        }
    }

    /// <summary>
    /// Inverse opsin absorbance matrix and quantization biases for XYB decoding.
    /// Ported from <c>jxl::OpsinInverseMatrix</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class OpsinInverseMatrix : Fields
    {
        /// <summary>Default quantization biases from <c>lib/jxl/quantizer.h</c>.</summary>
        public static readonly float[] KDefaultQuantBias =
        {
            1.0f - 0.05465007330715401f,
            1.0f - 0.07005449891748593f,
            1.0f - 0.049935103337343655f,
            0.145f,
        };

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>3x3 inverse opsin absorbance matrix.</summary>
        public Matrix3x3 InverseMatrix;

        /// <summary>Opsin absorbance biases for R, G, B.</summary>
        public float[] OpsinBiases = new float[3];

        /// <summary>Quantization biases (4 entries).</summary>
        public float[] QuantBiases = new float[4];

        /// <summary>
        /// Initializes a new instance of the <see cref="OpsinInverseMatrix"/> class with default values.
        /// </summary>
        public OpsinInverseMatrix()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "OpsinInverseMatrix";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            Matrix3x3 defaultInverse = OpsinParams.DefaultInverseOpsinAbsorbanceMatrix();

            for (int j = 0; j < 3; ++j)
            {
                Vector3 defRow = defaultInverse[j];
                Vector3 curRow = InverseMatrix[j];

                for (int i = 0; i < 3; ++i)
                {
                    float val = curRow[i];

                    if (!visitor.F16(defRow[i], ref val))
                    {
                        return false;
                    }

                    curRow[i] = val;
                }

                InverseMatrix[j] = curRow;
            }

            for (int i = 0; i < 3; ++i)
            {
                if (!visitor.F16(OpsinParams.KNegOpsinAbsorbanceBiasRgb[i], ref OpsinBiases[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < 4; ++i)
            {
                if (!visitor.F16(KDefaultQuantBias[i], ref QuantBiases[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Populates <paramref name="opsinParams"/> from this <see cref="OpsinInverseMatrix"/> and <paramref name="intensityTarget"/>.
        /// Ported from <c>jxl::OpsinInverseMatrix::ToOpsinParams</c> in <c>lib/jxl/dec_xyb.cc</c>.
        /// </summary>
        /// <param name="intensityTarget">Intensity target in nits.</param>
        /// <param name="opsinParams">Destination <see cref="OpsinParams"/>.</param>
        public void ToOpsinParams(float intensityTarget, OpsinParams opsinParams)
        {
            OpsinParams.InitSIMDInverseMatrix(
                in InverseMatrix,
                opsinParams.InverseOpsinMatrix,
                intensityTarget);

            for (int c = 0; c < 3; ++c)
            {
                opsinParams.OpsinBiases[c]     = OpsinBiases[c];
                opsinParams.OpsinBiasesCbrt[c] = MathF.Cbrt(opsinParams.OpsinBiases[c]);
            }

            opsinParams.OpsinBiases[3]     = 1.0f;
            opsinParams.OpsinBiasesCbrt[3] = 1.0f;
            Array.Copy(QuantBiases, opsinParams.QuantBiases, 4);
        }
    }

    /// <summary>
    /// Information useful for mapping HDR images to lower dynamic range displays.
    /// Ported from <c>jxl::ToneMapping</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class ToneMapping : Fields
    {
        /// <summary>Default intensity target in nits (255).</summary>
        public const float KDefaultIntensityTarget = 255.0f;

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Upper bound on the intensity level present in the image in nits.</summary>
        public float IntensityTarget;

        /// <summary>Lower bound on the intensity level present in the image in nits.</summary>
        public float MinNits;

        /// <summary>Whether <see cref="LinearBelow"/> is a ratio [0, 1] of maximum display brightness.</summary>
        public bool RelativeToMaxDisplay;

        /// <summary>Brightness threshold below which tone mapping leaves pixels linear.</summary>
        public float LinearBelow;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToneMapping"/> class with default values.
        /// </summary>
        public ToneMapping()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "ToneMapping";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            if (!visitor.F16(KDefaultIntensityTarget, ref IntensityTarget))
            {
                return false;
            }

            if (IntensityTarget <= 0.0f)
            {
                return Status.Failure("invalid intensity target");
            }

            if (!visitor.F16(0.0f, ref MinNits))
            {
                return false;
            }

            if (MinNits < 0.0f || MinNits > IntensityTarget)
            {
                return Status.Failure($"invalid min {MinNits} vs max {IntensityTarget}");
            }

            if (!visitor.Bool(false, ref RelativeToMaxDisplay))
            {
                return false;
            }

            if (!visitor.F16(0.0f, ref LinearBelow))
            {
                return false;
            }

            if (LinearBelow < 0.0f || (RelativeToMaxDisplay && LinearBelow > 1.0f))
            {
                return Status.Failure("invalid linear_below");
            }

            return true;
        }
    }

    /// <summary>
    /// Contains weights to customize XYB and upsampling transforms.
    /// Ported from <c>jxl::CustomTransformData</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class CustomTransformData : Fields
    {
        private static readonly float[] KWeights2 =
        {
            -0.01716200f, -0.03452303f, -0.04022174f, -0.02921014f, -0.00624645f,
            0.14111091f,  0.28896755f,  0.00278718f,  -0.01610267f, 0.56661550f,
            0.03777607f,  -0.01986694f, -0.03144731f, -0.01185068f, -0.00213539f,
        };

        private static readonly float[] KWeights4 =
        {
            -0.02419067f, -0.03491987f, -0.03693351f, -0.03094285f, -0.00529785f,
            -0.01663432f, -0.03556863f, -0.03888905f, -0.03516850f, -0.00989469f,
            0.23651958f,  0.33392945f,  -0.01073543f, -0.01313181f, -0.03556694f,
            0.13048175f,  0.40103025f,  0.03951150f,  -0.02077584f, 0.46914198f,
            -0.00209270f, -0.01484589f, -0.04064806f, 0.18942530f,  0.56279892f,
            0.06674400f,  -0.02335494f, -0.03551682f, -0.00754830f, -0.02267919f,
            -0.02363578f, 0.00315804f,  -0.03399098f, -0.01359519f, -0.00091653f,
            -0.00335467f, -0.01163294f, -0.01610294f, -0.00974088f, -0.00191622f,
            -0.01095446f, -0.03198464f, -0.04455121f, -0.02799790f, -0.00645912f,
            0.06390599f,  0.22963888f,  0.00630981f,  -0.01897349f, 0.67537268f,
            0.08483369f,  -0.02534994f, -0.02205197f, -0.01667999f, -0.00384443f,
        };

        private static readonly float[] KWeights8 =
        {
            -0.02928613f, -0.03706353f, -0.03783812f, -0.03324558f, -0.00447632f,
            -0.02519406f, -0.03752601f, -0.03901508f, -0.03663285f, -0.00646649f,
            -0.02066407f, -0.03838633f, -0.04002101f, -0.03900035f, -0.00901973f,
            -0.01626393f, -0.03954148f, -0.04046620f, -0.03979621f, -0.01224485f,
            0.29895328f,  0.35757708f,  -0.02447552f, -0.01081748f, -0.04314594f,
            0.23903219f,  0.41119301f,  -0.00573046f, -0.01450239f, -0.04246845f,
            0.17567618f,  0.45220643f,  0.02287757f,  -0.01936783f, -0.03583255f,
            0.11572472f,  0.47416733f,  0.06284440f,  -0.02685066f, 0.42720050f,
            -0.02248939f, -0.01155273f, -0.04562755f, 0.28689496f,  0.49093869f,
            -0.00007891f, -0.01545926f, -0.04562659f, 0.21238920f,  0.53980934f,
            0.03369474f,  -0.02070211f, -0.03866988f, 0.14229550f,  0.56593398f,
            0.08045181f,  -0.02888298f, -0.03680918f, -0.00542229f, -0.02920477f,
            -0.02788574f, -0.02118180f, -0.03942402f, -0.00775547f, -0.02433614f,
            -0.03193943f, -0.02030828f, -0.04044014f, -0.01074016f, -0.01930822f,
            -0.03620399f, -0.01974125f, -0.03919545f, -0.01456093f, -0.00045072f,
            -0.00360110f, -0.01020207f, -0.01231907f, -0.00638988f, -0.00071592f,
            -0.00279122f, -0.00957115f, -0.01288327f, -0.00730937f, -0.00107783f,
            -0.00210156f, -0.00890705f, -0.01317668f, -0.00813895f, -0.00153491f,
            -0.02128481f, -0.04173044f, -0.04831487f, -0.03293190f, -0.00525260f,
            -0.01720322f, -0.04052736f, -0.05045706f, -0.03607317f, -0.00738030f,
            -0.01341764f, -0.03965629f, -0.05151616f, -0.03814886f, -0.01005819f,
            0.18968273f,  0.33063684f,  -0.01300105f, -0.01372950f, -0.04017465f,
            0.13727832f,  0.36402234f,  0.01027890f,  -0.01832107f, -0.03365072f,
            0.08734506f,  0.38194295f,  0.04338228f,  -0.02525993f, 0.56408126f,
            0.00458352f,  -0.01648227f, -0.04887868f, 0.24585519f,  0.62026135f,
            0.04314807f,  -0.02213737f, -0.04158014f, 0.16637289f,  0.65027023f,
            0.09621636f,  -0.03101388f, -0.04082742f, -0.00904519f, -0.02790922f,
            -0.02117818f, 0.00798662f,  -0.03995711f, -0.01243427f, -0.02231705f,
            -0.02946266f, 0.00992055f,  -0.03600283f, -0.01684920f, -0.00111684f,
            -0.00411204f, -0.01297130f, -0.01723725f, -0.01022545f, -0.00165306f,
            -0.00313110f, -0.01218016f, -0.01763266f, -0.01125620f, -0.00231663f,
            -0.01374149f, -0.03797620f, -0.05142937f, -0.03117307f, -0.00581914f,
            -0.01064003f, -0.03608089f, -0.05272168f, -0.03375670f, -0.00795586f,
            0.09628104f,  0.27129991f,  -0.00353779f, -0.01734151f, -0.03153981f,
            0.05686230f,  0.28500998f,  0.02230594f,  -0.02374955f, 0.68214326f,
            0.05018048f,  -0.02320852f, -0.04383616f, 0.18459474f,  0.71517975f,
            0.10805613f,  -0.03263677f, -0.03637639f, -0.01394373f, -0.02511203f,
            -0.01728636f, 0.05407331f,  -0.02867568f, -0.01893131f, -0.00240854f,
            -0.00446511f, -0.01636187f, -0.02377053f, -0.01522848f, -0.00333334f,
            -0.00819975f, -0.02964169f, -0.04499287f, -0.02745350f, -0.00612408f,
            0.02727416f,  0.19446600f,  0.00159832f,  -0.02232473f, 0.74982506f,
            0.11452620f,  -0.03348048f, -0.01605681f, -0.02070339f, -0.00458223f,
        };

        /// <summary>Must be set before calling <see cref="VisitFields"/> to match <see cref="ImageMetadata.XybEncoded"/>.</summary>
        public bool NonserializedXybEncoded;

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Opsin inverse matrix bundle.</summary>
        public OpsinInverseMatrix OpsinInverseMatrix = new OpsinInverseMatrix();

        /// <summary>Bitmask of custom upsampling weights (bit 0 = 2x, bit 1 = 4x, bit 2 = 8x).</summary>
        public uint CustomWeightsMask;

        /// <summary>Custom 2x upsampling kernel weights (15 entries).</summary>
        public float[] Upsampling2Weights = new float[15];

        /// <summary>Custom 4x upsampling kernel weights (55 entries).</summary>
        public float[] Upsampling4Weights = new float[55];

        /// <summary>Custom 8x upsampling kernel weights (210 entries).</summary>
        public float[] Upsampling8Weights = new float[210];

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomTransformData"/> class with default values.
        /// </summary>
        public CustomTransformData()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "CustomTransformData";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            if (visitor.Conditional(NonserializedXybEncoded) && !visitor.VisitNested(OpsinInverseMatrix))
            {
                return false;
            }

            if (!visitor.Bits(3, 0, ref CustomWeightsMask))
            {
                return false;
            }

            if (visitor.Conditional((CustomWeightsMask & 0x1u) != 0) &&
                !VisitWeights(visitor, KWeights2, Upsampling2Weights))
            {
                return false;
            }

            if (visitor.Conditional((CustomWeightsMask & 0x2u) != 0) &&
                !VisitWeights(visitor, KWeights4, Upsampling4Weights))
            {
                return false;
            }

            if (visitor.Conditional((CustomWeightsMask & 0x4u) != 0))
            {
                return VisitWeights(visitor, KWeights8, Upsampling8Weights);
            }

            return true;
        }

        private static Status VisitWeights(Visitor visitor, float[] defaults, float[] weights)
        {
            for (int i = 0; i < defaults.Length; i++)
            {
                if (!visitor.F16(defaults[i], ref weights[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Properties of the original image bundle, enabling round-trip reconstruction of metadata.
    /// Ported from <c>jxl::ImageMetadata</c> in <c>lib/jxl/image_metadata.h</c> and <c>lib/jxl/image_metadata.cc</c>.
    /// </summary>
    public sealed class ImageMetadata : Fields
    {
        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Original sample bit depth.</summary>
        public BitDepth BitDepth = new BitDepth();

        /// <summary>Whether a 16-bit signed modular buffer is sufficient.</summary>
        public bool Modular16BitBufferSufficient;

        /// <summary>Whether frames are encoded in the absolute XYB color space.</summary>
        public bool XybEncoded;

        /// <summary>Color encoding metadata.</summary>
        public ColorEncoding ColorEncoding = new ColorEncoding();

        /// <summary>EXIF orientation (1..8, default 1).</summary>
        public uint Orientation = 1;

        /// <summary>Whether a preview frame is present.</summary>
        public bool HavePreview;

        /// <summary>Whether animation frames are present.</summary>
        public bool HaveAnimation;

        /// <summary>Whether an intrinsic display size is present.</summary>
        public bool HaveIntrinsicSize;

        /// <summary>Intrinsic display dimensions (only valid if <see cref="HaveIntrinsicSize"/> is <c>true</c>).</summary>
        public SizeHeader IntrinsicSize = new SizeHeader();

        /// <summary>Tone mapping metadata.</summary>
        public ToneMapping ToneMapping = new ToneMapping();

        /// <summary>Number of extra channels.</summary>
        public uint NumExtraChannels;

        /// <summary>Per-extra-channel metadata.</summary>
        public List<ExtraChannelInfo> ExtraChannelInfo = new List<ExtraChannelInfo>();

        /// <summary>Preview dimensions (only valid if <see cref="HavePreview"/> is <c>true</c>).</summary>
        public PreviewHeader PreviewSize = new PreviewHeader();

        /// <summary>Animation header (only valid if <see cref="HaveAnimation"/> is <c>true</c>).</summary>
        public AnimationHeader Animation = new AnimationHeader();

        /// <summary>Extension bitmask.</summary>
        public ulong Extensions;

        /// <summary>Option to stop parsing after basic info.</summary>
        public bool NonserializedOnlyParseBasicInfo;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageMetadata"/> class with default values.
        /// </summary>
        public ImageMetadata()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "ImageMetadata";

        /// <summary>
        /// Returns the bit depth of the first alpha channel, or 0 if no alpha channel is present.
        /// </summary>
        /// <returns>Alpha bit depth, or 0.</returns>
        public uint GetAlphaBits()
        {
            ExtraChannelInfo? alpha = Find(ExtraChannel.Alpha);

            return alpha != null ? alpha.BitDepth.BitsPerSample : 0u;
        }

        /// <summary>
        /// Returns whether an alpha channel is present.
        /// </summary>
        /// <returns><c>true</c> if an alpha channel exists.</returns>
        public bool HasAlpha() => GetAlphaBits() != 0;

        /// <summary>
        /// Sets the bit depth of the alpha channel, adding or removing alpha extra channels as needed.
        /// </summary>
        /// <param name="bits">Alpha bit depth, or 0 to remove alpha channels.</param>
        /// <param name="alphaIsPremultiplied">Whether alpha is premultiplied.</param>
        public void SetAlphaBits(uint bits, bool alphaIsPremultiplied = false)
        {
            ExtraChannelInfo? alpha = Find(ExtraChannel.Alpha);

            if (bits == 0)
            {
                if (alpha != null)
                {
                    ExtraChannelInfo.RemoveAll(eci => eci.Type == ExtraChannel.Alpha);
                }
            }
            else if (alpha == null)
            {
                ExtraChannelInfo info = new ExtraChannelInfo
                {
                    Type            = ExtraChannel.Alpha,
                    DimShift        = 0,
                    AlphaAssociated = alphaIsPremultiplied,
                };
                info.BitDepth.BitsPerSample = bits;
                ExtraChannelInfo.Insert(0, info);
            }
            else
            {
                alpha.BitDepth.BitsPerSample         = bits;
                alpha.BitDepth.FloatingPointSample   = false;
                alpha.BitDepth.ExponentBitsPerSample = 0;
                alpha.AlphaAssociated                = alphaIsPremultiplied;
            }

            NumExtraChannels = (uint)ExtraChannelInfo.Count;

            if (bits > 12)
            {
                Modular16BitBufferSufficient = false;
            }
        }

        /// <summary>
        /// Configures <see cref="BitDepth"/> for unsigned integer samples of <paramref name="bits"/> bits.
        /// </summary>
        /// <param name="bits">Bits per sample.</param>
        public void SetUintSamples(uint bits)
        {
            BitDepth.BitsPerSample         = bits;
            BitDepth.ExponentBitsPerSample = 0;
            BitDepth.FloatingPointSample   = false;

            if (bits > 12)
            {
                Modular16BitBufferSufficient = false;
            }
        }

        /// <summary>
        /// Configures <see cref="BitDepth"/> for 32-bit IEEE 754 floating-point samples.
        /// </summary>
        public void SetFloat32Samples()
        {
            BitDepth.BitsPerSample         = 32;
            BitDepth.ExponentBitsPerSample = 8;
            BitDepth.FloatingPointSample   = true;
            Modular16BitBufferSufficient   = false;
        }

        /// <summary>
        /// Configures <see cref="BitDepth"/> for 16-bit IEEE 754 half-precision floating-point samples.
        /// </summary>
        public void SetFloat16Samples()
        {
            BitDepth.BitsPerSample         = 16;
            BitDepth.ExponentBitsPerSample = 5;
            BitDepth.FloatingPointSample   = true;
            Modular16BitBufferSufficient   = false;
        }

        /// <summary>
        /// Sets the tone mapping intensity target in nits.
        /// </summary>
        /// <param name="intensityTarget">Intensity target in nits.</param>
        public void SetIntensityTarget(float intensityTarget)
        {
            ToneMapping.IntensityTarget = intensityTarget;
        }

        /// <summary>
        /// Gets the tone mapping intensity target in nits.
        /// </summary>
        /// <returns>Intensity target in nits.</returns>
        public float IntensityTarget()
        {
            return ToneMapping.IntensityTarget;
        }

        /// <summary>
        /// Finds the first <see cref="Decoder.ExtraChannelInfo"/> of the given <paramref name="type"/>, or <c>null</c>.
        /// </summary>
        /// <param name="type">Extra channel type to search for.</param>
        /// <returns>Matching <see cref="Decoder.ExtraChannelInfo"/> or <c>null</c>.</returns>
        public ExtraChannelInfo? Find(ExtraChannel type)
        {
            foreach (ExtraChannelInfo eci in ExtraChannelInfo)
            {
                if (eci.Type == type)
                {
                    return eci;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the EXIF orientation as an <see cref="Decoder.Orientation"/> enum value.
        /// </summary>
        /// <returns>Current <see cref="Decoder.Orientation"/>.</returns>
        public Orientation GetOrientation()
        {
            return (Orientation)Orientation;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            if (!VisitExtraFieldsSection(visitor, out bool extraFields))
            {
                return false;
            }

            if (!VisitBitDepthAndExtraChannels(visitor))
            {
                return false;
            }

            if (!VisitColorAndToneMapping(visitor, extraFields))
            {
                return false;
            }

            // Treat as if only the fields up to extra channels exist.
            if (visitor.IsReading && NonserializedOnlyParseBasicInfo)
            {
                return true;
            }

            if (!visitor.BeginExtensions(ref Extensions))
            {
                return false;
            }

            // Extensions: in chronological order of being added to the format.
            return visitor.EndExtensions();
        }

        /// <summary>
        /// Returns a debug string summary of this <see cref="ImageMetadata"/>.
        /// </summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(BitDepth.DebugString());

            if (Modular16BitBufferSufficient)
            {
                sb.Append(" (modular 16)");
            }

            sb.Append(XybEncoded ? " xyb encoded" : " orig profile");
            sb.Append(' ').Append(ColorEncoding.Description());

            if (NumExtraChannels > 0)
            {
                sb.Append(" extra channels:");

                for (int i = 0; i < ExtraChannelInfo.Count; ++i)
                {
                    sb.Append(" (").Append(ExtraChannelInfo[i].DebugString()).Append(')');

                    if (i + 1 < ExtraChannelInfo.Count)
                    {
                        sb.Append(',');
                    }
                }
            }

            if (HavePreview)
            {
                sb.Append($" preview: {PreviewSize.XSize()}x{PreviewSize.YSize()}");
            }

            if (Orientation != 1)
            {
                sb.Append($" orientation: {Orientation}");
            }

            return sb.ToString();
        }

        private Status VisitExtraFieldsSection(Visitor visitor, out bool extraFields)
        {
            bool toneMappingDefault = visitor.IsReading ? false : Bundle.AllDefault(ToneMapping);
            extraFields             = Orientation != 1 ||
                                      HavePreview ||
                                      HaveAnimation ||
                                      HaveIntrinsicSize ||
                                      !toneMappingDefault;

            if (!visitor.Bool(false, ref extraFields))
            {
                return false;
            }

            if (!visitor.Conditional(extraFields))
            {
                Orientation       = 1;
                HaveIntrinsicSize = false;
                HavePreview       = false;
                HaveAnimation     = false;

                return true;
            }

            return VisitExtraHeaderNestedFields(visitor);
        }

        private Status VisitExtraHeaderNestedFields(Visitor visitor)
        {
            Orientation--;

            if (!visitor.Bits(3, 0, ref Orientation))
            {
                return false;
            }

            Orientation++;

            if (!visitor.Bool(false, ref HaveIntrinsicSize))
            {
                return false;
            }

            if (visitor.Conditional(HaveIntrinsicSize) && !visitor.VisitNested(IntrinsicSize))
            {
                return false;
            }

            if (!visitor.Bool(false, ref HavePreview))
            {
                return false;
            }

            if (visitor.Conditional(HavePreview) && !visitor.VisitNested(PreviewSize))
            {
                return false;
            }

            if (!visitor.Bool(false, ref HaveAnimation))
            {
                return false;
            }

            if (visitor.Conditional(HaveAnimation))
            {
                return visitor.VisitNested(Animation);
            }

            return true;
        }

        private Status VisitBitDepthAndExtraChannels(Visitor visitor)
        {
            if (!visitor.VisitNested(BitDepth))
            {
                return false;
            }

            if (!visitor.Bool(true, ref Modular16BitBufferSufficient))
            {
                return false;
            }

            NumExtraChannels = (uint)ExtraChannelInfo.Count;

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Val(1),
                U32Distr.BitsOffset(4, 2),
                U32Distr.BitsOffset(12, 1),
                0,
                ref NumExtraChannels))
            {
                return false;
            }

            if (!visitor.Conditional(NumExtraChannels != 0))
            {
                return true;
            }

            if (visitor.IsReading)
            {
                ExtraChannelInfo.Clear();

                for (uint i = 0; i < NumExtraChannels; i++)
                {
                    ExtraChannelInfo.Add(new ExtraChannelInfo());
                }
            }

            foreach (ExtraChannelInfo eci in ExtraChannelInfo)
            {
                if (!visitor.VisitNested(eci))
                {
                    return false;
                }
            }

            return true;
        }

        private Status VisitColorAndToneMapping(Visitor visitor, bool extraFields)
        {
            if (!visitor.Bool(true, ref XybEncoded))
            {
                return false;
            }

            if (!visitor.VisitNested(ColorEncoding))
            {
                return false;
            }

            if (visitor.Conditional(extraFields))
            {
                return visitor.VisitNested(ToneMapping);
            }

            return true;
        }
    }

    /// <summary>
    /// All metadata applicable to the entire codestream (dimensions, image metadata, custom transform data).
    /// Ported from <c>jxl::CodecMetadata</c> in <c>lib/jxl/image_metadata.h</c>.
    /// </summary>
    public sealed class CodecMetadata
    {
        /// <summary>Global image metadata.</summary>
        public ImageMetadata M = new ImageMetadata();

        /// <summary>Nominal size of the codestream.</summary>
        public SizeHeader Size = new SizeHeader();

        /// <summary>Custom transform weights and opsin inverse matrix.</summary>
        public CustomTransformData TransformData = new CustomTransformData();

        /// <summary>Returns the nominal codestream width in pixels.</summary>
        /// <returns>Width in pixels.</returns>
        public nuint XSize() => Size.XSize();

        /// <summary>Returns the nominal codestream height in pixels.</summary>
        /// <returns>Height in pixels.</returns>
        public nuint YSize() => Size.YSize();

        /// <summary>
        /// Returns the oriented image width.
        /// </summary>
        /// <param name="keepOrientation">Whether to skip applying orientation.</param>
        /// <returns>Oriented width in pixels.</returns>
        public nuint OrientedXSize(bool keepOrientation)
        {
            return (uint)M.GetOrientation() > 4 && !keepOrientation ? YSize() : XSize();
        }

        /// <summary>
        /// Returns the oriented image height.
        /// </summary>
        /// <param name="keepOrientation">Whether to skip applying orientation.</param>
        /// <returns>Oriented height in pixels.</returns>
        public nuint OrientedYSize(bool keepOrientation)
        {
            return (uint)M.GetOrientation() > 4 && !keepOrientation ? XSize() : YSize();
        }

        /// <summary>
        /// Returns the oriented preview width.
        /// </summary>
        /// <param name="keepOrientation">Whether to skip applying orientation.</param>
        /// <returns>Oriented preview width in pixels.</returns>
        public nuint OrientedPreviewXSize(bool keepOrientation)
        {
            return (uint)M.GetOrientation() > 4 && !keepOrientation
                ? M.PreviewSize.YSize()
                : M.PreviewSize.XSize();
        }

        /// <summary>
        /// Returns the oriented preview height.
        /// </summary>
        /// <param name="keepOrientation">Whether to skip applying orientation.</param>
        /// <returns>Oriented preview height in pixels.</returns>
        public nuint OrientedPreviewYSize(bool keepOrientation)
        {
            return (uint)M.GetOrientation() > 4 && !keepOrientation
                ? M.PreviewSize.XSize()
                : M.PreviewSize.YSize();
        }

        /// <summary>
        /// Returns a debug string summary of this <see cref="CodecMetadata"/>.
        /// </summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            return $"{Size.XSize()}x{Size.YSize()} {M.DebugString()}";
        }
    }

    /// <summary>
    /// Helper methods for reading and writing <see cref="ImageMetadata"/> and <see cref="CodecMetadata"/>.
    /// Ported from <c>lib/jxl/image_metadata.h</c>, <c>lib/jxl/image_metadata.cc</c>, and <c>lib/jxl/enc_fields.cc</c>.
    /// </summary>
    public static class ImageMetadataMethods
    {
        /// <summary>
        /// Visits a UTF-8 name string in an <see cref="ExtraChannelInfo"/> or <see cref="FrameHeader"/>.
        /// </summary>
        /// <param name="visitor">Field visitor.</param>
        /// <param name="name">UTF-8 string reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status VisitNameString(Visitor visitor, ref string name)
        {
            byte[] utf8     = string.IsNullOrEmpty(name) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(name);
            uint nameLength = (uint)utf8.Length;

            // Allows layer name lengths up to 1071 bytes
            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Bits(4),
                U32Distr.BitsOffset(5, 16),
                U32Distr.BitsOffset(10, 48),
                0,
                ref nameLength))
            {
                return false;
            }

            if (nameLength == 0)
            {
                name = string.Empty;

                return true;
            }

            if (visitor.IsReading && utf8.Length != nameLength)
            {
                utf8 = new byte[nameLength];
            }

            for (uint i = 0; i < nameLength; i++)
            {
                uint c = utf8[i];

                if (!visitor.Bits(8, 0, ref c))
                {
                    return false;
                }

                utf8[i] = (byte)c;
            }

            if (visitor.IsReading)
            {
                name = Encoding.UTF8.GetString(utf8);
            }

            return true;
        }

        /// <summary>
        /// Reads an <see cref="ImageMetadata"/> bundle from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="metadata">Destination image metadata.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadImageMetadata(BitReader reader, ImageMetadata metadata)
        {
            return Bundle.Read(reader, metadata);
        }

        /// <summary>
        /// Writes an <see cref="ImageMetadata"/> bundle to <paramref name="writer"/>.
        /// </summary>
        /// <param name="metadata">Source image metadata.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteImageMetadata(
            ImageMetadata metadata,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            return Bundle.Write(metadata, writer, layer, auxOut);
        }

        /// <summary>
        /// Writes the full codestream signature and headers (<see cref="SizeHeader"/>, <see cref="ImageMetadata"/>,
        /// and <see cref="CustomTransformData"/>) to <paramref name="writer"/>.
        /// </summary>
        /// <param name="metadata">Source codec metadata.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteCodestreamHeaders(
            CodecMetadata metadata,
            BitWriter writer,
            AuxOut? auxOut)
        {
            // Marker/signature
            Status markerOk = writer.WithMaxBits(
                16,
                LayerType.Header,
                auxOut,
                () =>
                {
                    writer.Write(8, 0xFF);
                    writer.Write(8, Headers.KCodestreamMarker);

                    return true;
                });

            if (!markerOk)
            {
                return markerOk;
            }

            if (!Headers.WriteSizeHeader(metadata.Size, writer, LayerType.Header, auxOut))
            {
                return false;
            }

            if (!WriteImageMetadata(metadata.M, writer, LayerType.Header, auxOut))
            {
                return false;
            }

            metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;

            return Bundle.Write(metadata.TransformData, writer, LayerType.Header, auxOut);
        }
    }
}
