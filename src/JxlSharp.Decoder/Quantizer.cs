// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    // Quantizes DC and AC coefficients, with separate quantization tables according
    // to the quant_kind (which is currently computed from the AC strategy and the
    // block index inside that strategy).

    /// <summary>
    /// Serialized global scale and DC quantization parameters.
    /// Ported from <c>jxl::QuantizerParams</c> in <c>lib/jxl/quantizer.h</c> and <c>lib/jxl/quantizer.cc</c>.
    /// </summary>
    public sealed class QuantizerParams : Fields
    {
        /// <summary>Serialized global quantization scale.</summary>
        public uint GlobalScale;

        /// <summary>Serialized DC quantization factor.</summary>
        public uint QuantDc;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuantizerParams"/> class with default values.
        /// </summary>
        public QuantizerParams()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "QuantizerParams";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            Status s = visitor.U32(
                U32Distr.BitsOffset(11, 1),
                U32Distr.BitsOffset(11, 2049),
                U32Distr.BitsOffset(12, 4097),
                U32Distr.BitsOffset(16, 8193),
                1,
                ref GlobalScale);

            if (!s)
            {
                return s;
            }

            return visitor.U32(
                U32Distr.Val(16),
                U32Distr.BitsOffset(5, 1),
                U32Distr.BitsOffset(8, 1),
                U32Distr.BitsOffset(16, 1),
                1,
                ref QuantDc);
        }
    }

    /// <summary>
    /// Quantizes and dequantizes DC and AC coefficients using <see cref="DequantMatrices"/>.
    /// Ported from <c>jxl::Quantizer</c> in <c>lib/jxl/quantizer.h</c> and <c>lib/jxl/quantizer.cc</c>.
    /// </summary>
    public sealed unsafe class Quantizer
    {
        /// <summary>Global scale denominator (<c>1 &lt;&lt; 16 = 65536</c>).</summary>
        public const int kGlobalScaleDenom = 1 << 16;

        /// <summary>Global scale numerator (4096).</summary>
        public const int kGlobalScaleNumerator = 4096;

        /// <summary>Default quantization step (64).</summary>
        public const int kDefaultQuant = 64;

        /// <summary>Maximum raw quant field value (256).</summary>
        public const int kQuantMax = 256;

        // Returns adjusted version of a quantized integer, such that its value is
        // closer to the expected value of the original.
        // The residuals of AC coefficients that we quantize are not uniformly
        // distributed. Numerical experiments show that they have a distribution with
        // the "shape" of 1/(1+x^2) [up to some coefficients]. This means that the
        // expected value of a coefficient that gets quantized to x will not be x
        // itself, but (at least with reasonable approximation):
        // - 0 if x is 0
        // - x * biases[c] if x is 1 or -1
        // - x - biases[3]/x otherwise
        // This follows from computing the distribution of the quantization bias, which
        // can be approximated fairly well by <constant>/x when |x| is at least two.

        /// <summary>Default quantization bias numerator for <c>|x| &gt;= 2</c> (0.145f).</summary>
        public const float kBiasNumerator = 0.145f;

        // zero-biases for quantizing channels X, Y, B

        /// <summary>Default zero-biases for quantizing channels X, Y, B.</summary>
        public static ReadOnlySpan<float> ZeroBiasDefault => new float[]
        {
            0.5f,
            0.5f,
            0.5f,
        };

        /// <summary>Default AC dequantization biases for channels X, Y, B and the numerator.</summary>
        public static ReadOnlySpan<float> DefaultQuantBias => new float[]
        {
            1.0f - 0.05465007330715401f,
            1.0f - 0.07005449891748593f,
            1.0f - 0.049935103337343655f,
            0.145f,
        };

        private readonly float[] _mulDc    = new float[4];
        private readonly float[] _invMulDc = new float[4];
        private readonly float[] _zeroBias = new float[3];
        private readonly DequantMatrices _dequant;

        // These are serialized:
        private int _globalScale;
        private int _quantDc;

        // These are derived from global_scale_:
        private float _invGlobalScale;
        private float _globalScaleFloat; // reciprocal of inv_global_scale_
        private float _invQuantDc;

        /// <summary>
        /// Initializes a new instance of the <see cref="Quantizer"/> class with default scale and DC quant.
        /// </summary>
        /// <param name="dequant">Underlying <see cref="DequantMatrices"/>.</param>
        public Quantizer(DequantMatrices dequant)
            : this(dequant, kDefaultQuant, kGlobalScaleDenom / kDefaultQuant)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Quantizer"/> class with explicit <paramref name="quantDc"/> and <paramref name="globalScale"/>.
        /// </summary>
        /// <param name="dequant">Underlying <see cref="DequantMatrices"/>.</param>
        /// <param name="quantDc">DC quantization step.</param>
        /// <param name="globalScale">Global quantization scale.</param>
        public Quantizer(DequantMatrices dequant, int quantDc, int globalScale)
        {
            _globalScale = globalScale;
            _quantDc     = quantDc;
            _dequant     = dequant;

            RecomputeFromGlobalScale();
            _invQuantDc = _invGlobalScale / _quantDc;
            ZeroBiasDefault.CopyTo(_zeroBias);
        }

        /// <summary>
        /// Clamps a float quantizer value to the valid integer range <c>[1, kQuantMax]</c>.
        /// </summary>
        /// <param name="val">Unclamped float quantizer value.</param>
        /// <returns>Clamped integer value in <c>[1, 256]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampVal(float val)
        {
            return (int)MathF.Max(1.0f, MathF.Min(val, kQuantMax));
        }

        /// <summary>
        /// Scales the global scale by <paramref name="scale"/> and recomputes derived fields.
        /// </summary>
        /// <param name="scale">Multiplier to apply to the current global scale.</param>
        /// <returns>Actual ratio <c>newGlobalScale / oldGlobalScale</c>.</returns>
        public float ScaleGlobalScale(float scale)
        {
            int newGlobalScale = (int)MathF.Round(_globalScale * scale, MidpointRounding.AwayFromZero);
            float scaleOut     = newGlobalScale * 1.0f / _globalScale;
            _globalScale       = newGlobalScale;
            RecomputeFromGlobalScale();

            return scaleOut;
        }

        /// <summary>
        /// Recomputes derived fields after <c>global_scale_</c> or <c>quant_dc_</c> has changed.
        /// </summary>
        public void RecomputeFromGlobalScale()
        {
            _globalScaleFloat = (float)(_globalScale * (1.0 / kGlobalScaleDenom));
            _invGlobalScale   = (float)(1.0 * kGlobalScaleDenom / _globalScale);
            _invQuantDc       = _invGlobalScale / _quantDc;

            for (nuint c = 0; c < 3; c++)
            {
                _mulDc[(int)c]    = GetDcStep(c);
                _invMulDc[(int)c] = GetInvDcStep(c);
            }
        }

        /// <summary>
        /// Returns scaling factor such that <c>Scale() * (RawDC() or RawQuantField())</c>
        /// pixels yields the same float values returned by <c>GetQuantField</c>.
        /// </summary>
        /// <returns>Global scale as a float.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Scale() => _globalScaleFloat;

        /// <summary>
        /// Returns the reciprocal of <see cref="Scale"/>.
        /// </summary>
        /// <returns>Inverse global scale.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float InvGlobalScale() => _invGlobalScale;

        /// <summary>
        /// Converts the floating-point quant field <paramref name="qf"/> within <paramref name="rect"/> into integer <paramref name="rawQuantField"/>.
        /// </summary>
        /// <param name="qf">Floating-point quant field plane.</param>
        /// <param name="rect">Rectangle to convert.</param>
        /// <param name="rawQuantField">Destination integer quant field plane.</param>
        public void SetQuantFieldRect(ImagePlane<float> qf, Rect rect, ImagePlane<int> rawQuantField)
        {
            nuint h = (nuint)rect.YSize;
            nuint w = (nuint)rect.XSize;

            for (nuint y = 0; y < h; ++y)
            {
                float* rowQf = rect.ConstRow(qf, y);
                int* rowQi   = rect.Row(rawQuantField, y);

                for (nuint x = 0; x < w; ++x)
                {
                    rowQi[x] = ClampVal(rowQf[x] * _invGlobalScale + 0.5f);
                }
            }
        }

        /// <summary>
        /// Computes global scale and DC quant from <paramref name="quantDc"/> and <paramref name="qf"/>, and optionally populates <paramref name="rawQuantField"/>.
        /// </summary>
        /// <param name="quantDc">Floating-point DC quantizer value.</param>
        /// <param name="qf">Floating-point AC quant field.</param>
        /// <param name="rawQuantField">Optional destination integer quant field.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetQuantField(float quantDc, ImagePlane<float> qf, ImagePlane<int>? rawQuantField)
        {
            int w        = (int)qf.xsize;
            int h        = (int)qf.ysize;
            float[] data = new float[w * h];

            for (int y = 0; y < h; ++y)
            {
                float* rowQf = qf.Row((nuint)y);
                int rowBase  = w * y;

                for (int x = 0; x < w; ++x)
                {
                    data[rowBase + x] = rowQf[x];
                }
            }

            Array.Sort(data);
            float quantMedian  = data[data.Length / 2];
            float[] deviations = new float[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                deviations[i] = MathF.Abs(data[i] - quantMedian);
            }

            Array.Sort(deviations);
            float quantMedianAbsd = deviations[deviations.Length / 2];

            ComputeGlobalScaleAndQuant(quantDc, quantMedian, quantMedianAbsd);

            if (rawQuantField is not null)
            {
                if (!ImageOps.SameSize(rawQuantField, qf))
                {
                    return Status.Failure("Quant field size mismatch");
                }

                SetQuantFieldRect(qf, new Rect(qf), rawQuantField);
            }

            return true;
        }

        /// <summary>
        /// Sets uniform DC and AC quantization values across <paramref name="rawQuantField"/>.
        /// </summary>
        /// <param name="quantDc">Floating-point DC quantizer value.</param>
        /// <param name="quantAc">Uniform floating-point AC quantizer value.</param>
        /// <param name="rawQuantField">Destination integer quant field plane.</param>
        public void SetQuant(float quantDc, float quantAc, ImagePlane<int> rawQuantField)
        {
            ComputeGlobalScaleAndQuant(quantDc, quantAc, 0.0f);
            int val = ClampVal(quantAc * _invGlobalScale + 0.5f);
            ImageOps.FillImage(val, rawQuantField);
        }

        /// <summary>
        /// Returns the DC quantization base value, which is currently global (not
        /// adaptive). The actual scale factor used to dequantize pixels in channel <c>c</c>
        /// is: <c>InvQuantDc() * dequant.DCQuant(c)</c>.
        /// </summary>
        /// <returns>Inverse DC quantization base value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float InvQuantDc() => _invQuantDc;

        /// <summary>
        /// Returns the AC dequantization multiplier for a given integer <paramref name="quant"/> value.
        /// Dequantize by multiplying with this times <c>dequant_matrix</c>.
        /// </summary>
        /// <param name="quant">Integer quant field value for the block.</param>
        /// <returns><c>InvGlobalScale() / quant</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float InvQuantAc(int quant) => _invGlobalScale / quant;

        /// <summary>
        /// Gets the current serialized <see cref="QuantizerParams"/>.
        /// </summary>
        /// <returns>A populated <see cref="QuantizerParams"/> instance.</returns>
        public QuantizerParams GetParams()
        {
            return new QuantizerParams
            {
                GlobalScale = (uint)_globalScale,
                QuantDc     = (uint)_quantDc,
            };
        }

        /// <summary>
        /// Decodes the quantizer parameters from <paramref name="reader"/> and recomputes derived fields.
        /// </summary>
        /// <param name="reader">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Decode(BitReader reader)
        {
            QuantizerParams qParams = new QuantizerParams();

            if (!Bundle.Read(reader, qParams))
            {
                return false;
            }

            _globalScale = (int)qParams.GlobalScale;
            _quantDc     = (int)qParams.QuantDc;
            RecomputeFromGlobalScale();

            return true;
        }

        /// <summary>
        /// Returns a pointer to the dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Aligned pointer to the dequantization matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float* DequantMatrix(AcStrategyType quantKind, nuint c)
        {
            return _dequant.Matrix(quantKind, c);
        }

        /// <summary>
        /// Returns a pointer to the inverse dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Aligned pointer to the inverse dequantization matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float* InvDequantMatrix(AcStrategyType quantKind, nuint c)
        {
            return _dequant.InvMatrix(quantKind, c);
        }

        /// <summary>
        /// Calculates the DC dequantization step for channel <paramref name="c"/>.
        /// </summary>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>DC dequantization step.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetDcStep(nuint c)
        {
            return _invQuantDc * _dequant.DCQuant(c);
        }

        /// <summary>
        /// Calculates the inverse DC dequantization step for channel <paramref name="c"/>.
        /// </summary>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Inverse DC dequantization step.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetInvDcStep(nuint c)
        {
            return _dequant.InvDCQuant(c) * (_globalScaleFloat * _quantDc);
        }

        /// <summary>
        /// Gets the 4-element array of DC dequantization multipliers.
        /// </summary>
        /// <returns>Read-only span of DC multipliers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> MulDC() => _mulDc;

        /// <summary>
        /// Gets the 4-element array of inverse DC dequantization multipliers.
        /// </summary>
        /// <returns>Read-only span of inverse DC multipliers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> InvMulDC() => _invMulDc;

        /// <summary>
        /// Resets both <see cref="MulDC"/> and <see cref="InvMulDC"/> to all 1.0f.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearDCMul()
        {
            Array.Fill(_mulDc, 1.0f);
            Array.Fill(_invMulDc, 1.0f);
        }

        /// <summary>
        /// Computes <c>global_scale_</c> and <c>quant_dc_</c> from <paramref name="quantDc"/>, <paramref name="quantMedian"/>, and <paramref name="quantMedianAbsd"/>.
        /// </summary>
        /// <param name="quantDc">Floating-point DC quantizer target.</param>
        /// <param name="quantMedian">Median of the floating-point AC quant field.</param>
        /// <param name="quantMedianAbsd">Median absolute deviation of the AC quant field.</param>
        public void ComputeGlobalScaleAndQuant(
            float quantDc,
            float quantMedian,
            float quantMedianAbsd)
        {
            // Target value for the median value in the quant field.
            const float kQuantFieldTarget = 5.0f;

            // We reduce the median of the quant field by the median absolute deviation:
            // higher resolution on highly varying quant fields.
            float scale = kGlobalScaleDenom * (quantMedian - quantMedianAbsd) / kQuantFieldTarget;

            // Ensure that new_global_scale is positive and no more than 1<<15.
            scale = Math.Clamp(scale, 1.0f, 1 << 15);
            int newGlobalScale = (int)scale;

            // Ensure that quant_dc_ will always be at least
            // 0.625 * kGlobalScaleDenom/kGlobalScaleNumerator = 10.
            int scaledQuantDc = (int)(quantDc * kGlobalScaleNumerator * 1.6);

            if (newGlobalScale > scaledQuantDc)
            {
                newGlobalScale = Math.Max(1, scaledQuantDc);
            }

            _globalScale = newGlobalScale;

            // Code below uses inv_global_scale_.
            RecomputeFromGlobalScale();

            float fval = MathF.Min(1 << 16, quantDc * _invGlobalScale + 0.5f);
            _quantDc   = (int)fval;

            // quant_dc_ was updated, recompute values.
            RecomputeFromGlobalScale();
        }
    }
}
