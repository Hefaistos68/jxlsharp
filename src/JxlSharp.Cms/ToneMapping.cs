// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Rec. ITU-R BT.2408 tone mapper (scalar and SIMD).
    /// Ported from <c>jxl::Rec2408ToneMapperBase</c> and <c>jxl::Rec2408ToneMapper</c>
    /// in <c>lib/jxl/cms/tone_mapping.h</c> and <c>lib/jxl/cms/tone_mapping-inl.h</c>.
    /// </summary>
    public sealed class Rec2408ToneMapper
    {
        private readonly float _sourceMin;
        private readonly float _sourceMax;
        private readonly float _targetMin;
        private readonly float _targetMax;
        private readonly float _redY;
        private readonly float _greenY;
        private readonly float _blueY;

        private readonly float _pqMasteringMin;
        private readonly float _pqMasteringMax;
        private readonly float _pqMasteringRange;
        private readonly float _invPqMasteringRange;
        private readonly float _minLum;
        private readonly float _maxLum;
        private readonly float _ks;
        private readonly float _invOneMinusKs;
        private readonly float _normalizer;
        private readonly float _invTargetPeak;
        private readonly TfPq _tfPq = new TfPq(1.0f);

        /// <summary>
        /// Initializes a new instance of the <see cref="Rec2408ToneMapper"/> class.
        /// </summary>
        /// <param name="sourceMin">Source minimum luminance in nits.</param>
        /// <param name="sourceMax">Source maximum luminance in nits.</param>
        /// <param name="targetMin">Target minimum luminance in nits.</param>
        /// <param name="targetMax">Target maximum luminance in nits.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        public Rec2408ToneMapper(
            float sourceMin,
            float sourceMax,
            float targetMin,
            float targetMax,
            Vector3 primariesLuminances)
        {
            _sourceMin = sourceMin;
            _sourceMax = sourceMax;
            _targetMin = targetMin;
            _targetMax = targetMax;
            _redY      = primariesLuminances[0];
            _greenY    = primariesLuminances[1];
            _blueY     = primariesLuminances[2];

            _pqMasteringMin      = InvEotfScalar(_sourceMin);
            _pqMasteringMax      = InvEotfScalar(_sourceMax);
            _pqMasteringRange    = _pqMasteringMax - _pqMasteringMin;
            _invPqMasteringRange = 1.0f / _pqMasteringRange;
            _minLum              = (InvEotfScalar(_targetMin) - _pqMasteringMin) * _invPqMasteringRange;
            _maxLum              = (InvEotfScalar(_targetMax) - _pqMasteringMin) * _invPqMasteringRange;
            _ks                  = (1.5f * _maxLum) - 0.5f;
            _invOneMinusKs       = 1.0f / Math.Max(1e-6f, 1.0f - _ks);
            _normalizer          = _sourceMax / _targetMax;
            _invTargetPeak       = 1.0f / _targetMax;
        }

        /// <summary>
        /// Applies Rec. 2408 tone mapping in-place to a scalar RGB color.
        /// </summary>
        /// <param name="rgb">Normalized linear RGB color to tone map.</param>
        public void ToneMap(ref Vector3 rgb)
        {
            float luminance      = _sourceMax * ((_redY * rgb[0]) + (_greenY * rgb[1]) + (_blueY * rgb[2]));
            float normalizedPq   = Math.Min(1.0f, (InvEotfScalar(luminance) - _pqMasteringMin) * _invPqMasteringRange);
            float e2             = normalizedPq < _ks ? normalizedPq : PScalar(normalizedPq);
            float oneMinusE2     = 1.0f - e2;
            float oneMinusE2Sq   = oneMinusE2 * oneMinusE2;
            float oneMinusE2Quad = oneMinusE2Sq * oneMinusE2Sq;
            float e3             = (_minLum * oneMinusE2Quad) + e2;
            float e4             = (e3 * _pqMasteringRange) + _pqMasteringMin;
            float d4             = (float)TfPqBase.DisplayFromEncoded(1.0f, e4);
            float newLuminance   = Common.Clamp1(d4, 0.0f, _targetMax);
            const float minLuminance = 1e-6f;
            bool useCap          = luminance <= minLuminance;
            float ratio          = newLuminance / Math.Max(luminance, minLuminance);
            float cap            = newLuminance * _invTargetPeak;
            float multiplier     = ratio * _normalizer;

            rgb[0] = useCap ? cap : rgb[0] * multiplier;
            rgb[1] = useCap ? cap : rgb[1] * multiplier;
            rgb[2] = useCap ? cap : rgb[2] * multiplier;
        }

        /// <summary>
        /// Applies Rec. 2408 tone mapping in-place to 4-lane SIMD RGB vectors.
        /// </summary>
        /// <param name="red">Red channel vector.</param>
        /// <param name="green">Green channel vector.</param>
        /// <param name="blue">Blue channel vector.</param>
        public void ToneMap(ref Vector128<float> red, ref Vector128<float> green, ref Vector128<float> blue)
        {
            Vector128<float> lumSum = (Vector128.Create(_redY) * red) +
                                      (Vector128.Create(_greenY) * green) +
                                      (Vector128.Create(_blueY) * blue);
            Vector128<float> luminance           = Vector128.Create(_sourceMax) * lumSum;
            Vector128<float> pqMasteringMin      = Vector128.Create(_pqMasteringMin);
            Vector128<float> invPqMasteringRange = Vector128.Create(_invPqMasteringRange);
            Vector128<float> normalizedPq        = Vector128.Min(
                Vector128.Create(1.0f),
                (InvEotfSimd(luminance) - pqMasteringMin) * invPqMasteringRange);

            Vector128<float> ks             = Vector128.Create(_ks);
            Vector128<float> e2             = Vector128.ConditionalSelect(Vector128.LessThan(normalizedPq, ks), normalizedPq, PSimd(normalizedPq));
            Vector128<float> oneMinusE2     = Vector128.Create(1.0f) - e2;
            Vector128<float> oneMinusE2Sq   = oneMinusE2 * oneMinusE2;
            Vector128<float> oneMinusE2Quad = oneMinusE2Sq * oneMinusE2Sq;
            Vector128<float> e3             = (Vector128.Create(_minLum) * oneMinusE2Quad) + e2;
            Vector128<float> e4             = (e3 * Vector128.Create(_pqMasteringRange)) + pqMasteringMin;
            Vector128<float> decoded        = _tfPq.DisplayFromEncoded(e4);
            Vector128<float> zero           = Vector128<float>.Zero;
            Vector128<float> nonNegDecoded  = Vector128.Max(zero, decoded);
            Vector128<float> newLuminance   = Vector128.Min(Vector128.Create(_targetMax), nonNegDecoded);

            Vector128<float> minLuminance = Vector128.Create(1e-6f);
            Vector128<float> useCap       = Vector128.LessThanOrEqual(luminance, minLuminance);
            Vector128<float> ratio        = newLuminance / Vector128.Max(luminance, minLuminance);
            Vector128<float> cap          = newLuminance * Vector128.Create(_invTargetPeak);
            Vector128<float> multiplier   = ratio * Vector128.Create(_normalizer);

            red   = Vector128.ConditionalSelect(useCap, cap, red * multiplier);
            green = Vector128.ConditionalSelect(useCap, cap, green * multiplier);
            blue  = Vector128.ConditionalSelect(useCap, cap, blue * multiplier);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InvEotfScalar(float luminance)
        {
            return (float)TfPqBase.EncodedFromDisplay(1.0f, luminance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float TScalar(float a)
        {
            return (a - _ks) * _invOneMinusKs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float PScalar(float b)
        {
            float tb  = TScalar(b);
            float tb2 = tb * tb;
            float tb3 = tb2 * tb;

            return (((2.0f * tb3) - (3.0f * tb2) + 1.0f) * _ks) +
                   ((tb3 - (2.0f * tb2) + tb) * (1.0f - _ks)) +
                   (((-2.0f * tb3) + (3.0f * tb2)) * _maxLum);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector128<float> InvEotfSimd(Vector128<float> luminance)
        {
            return _tfPq.EncodedFromDisplay(luminance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector128<float> TSimd(Vector128<float> a)
        {
            return (a - Vector128.Create(_ks)) * Vector128.Create(_invOneMinusKs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector128<float> PSimd(Vector128<float> b)
        {
            Vector128<float> tb     = TSimd(b);
            Vector128<float> tb2    = tb * tb;
            Vector128<float> tb3    = tb2 * tb;
            Vector128<float> ks     = Vector128.Create(_ks);
            Vector128<float> maxLum = Vector128.Create(_maxLum);

            Vector128<float> term1 = ((Vector128.Create(2.0f) * tb3) + (Vector128.Create(-3.0f) * tb2) + Vector128.Create(1.0f)) * ks;
            Vector128<float> term2 = (tb3 + (Vector128.Create(-2.0f) * tb2) + tb) * (Vector128.Create(1.0f) - ks);
            Vector128<float> term3 = ((Vector128.Create(-2.0f) * tb3) + (Vector128.Create(3.0f) * tb2)) * maxLum;

            return term1 + term2 + term3;
        }
    }

    /// <summary>
    /// Hybrid Log-Gamma (HLG) opto-optical transfer function (OOTF).
    /// Ported from <c>jxl::HlgOOTF_Base</c> and <c>jxl::HlgOOTF</c>
    /// in <c>lib/jxl/cms/tone_mapping.h</c> and <c>lib/jxl/cms/tone_mapping-inl.h</c>.
    /// </summary>
    public sealed class HlgOotf
    {
        private readonly float _exponent;
        private readonly bool _applyOotf;
        private readonly float _redY;
        private readonly float _greenY;
        private readonly float _blueY;

        /// <summary>
        /// Initializes a new instance of the <see cref="HlgOotf"/> class between two display luminances.
        /// </summary>
        /// <param name="sourceLuminance">Source luminance in nits.</param>
        /// <param name="targetLuminance">Target luminance in nits.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        public HlgOotf(float sourceLuminance, float targetLuminance, Vector3 primariesLuminances)
            : this(MathF.Pow(1.111f, MathF.Log2(targetLuminance / sourceLuminance)), primariesLuminances)
        {
        }

        private HlgOotf(float gamma, Vector3 luminances)
        {
            _exponent  = gamma - 1.0f;
            _applyOotf = _exponent < -0.01f || _exponent > 0.01f;
            _redY      = luminances[0];
            _greenY    = luminances[1];
            _blueY     = luminances[2];
        }

        /// <summary>
        /// Gets a value indicating whether gamut mapping is warranted after applying this OOTF.
        /// </summary>
        public bool WarrantsGamutMapping => _applyOotf && _exponent < 0.0f;

        /// <summary>
        /// Creates an <see cref="HlgOotf"/> converting from scene light to display light.
        /// </summary>
        /// <param name="displayLuminance">Display peak luminance in nits.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        /// <returns>Configured <see cref="HlgOotf"/>.</returns>
        public static HlgOotf FromSceneLight(float displayLuminance, Vector3 primariesLuminances)
        {
            float gamma = 1.2f * MathF.Pow(1.111f, MathF.Log2(displayLuminance / 1000.0f));

            return new HlgOotf(gamma, primariesLuminances);
        }

        /// <summary>
        /// Creates an <see cref="HlgOotf"/> converting from display light to scene light.
        /// </summary>
        /// <param name="displayLuminance">Display peak luminance in nits.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        /// <returns>Configured <see cref="HlgOotf"/>.</returns>
        public static HlgOotf ToSceneLight(float displayLuminance, Vector3 primariesLuminances)
        {
            float gamma = (1.0f / 1.2f) * MathF.Pow(1.111f, -MathF.Log2(displayLuminance / 1000.0f));

            return new HlgOotf(gamma, primariesLuminances);
        }

        /// <summary>
        /// Applies the HLG OOTF in-place to a scalar RGB color.
        /// </summary>
        /// <param name="rgb">Linear RGB color.</param>
        public void Apply(ref Vector3 rgb)
        {
            if (!_applyOotf)
            {
                return;
            }

            float luminance = (_redY * rgb[0]) + (_greenY * rgb[1]) + (_blueY * rgb[2]);
            float ratio     = Math.Min(MathF.Pow(luminance, _exponent), 1e9f);

            rgb[0] *= ratio;
            rgb[1] *= ratio;
            rgb[2] *= ratio;
        }

        /// <summary>
        /// Applies the HLG OOTF in-place to 4-lane SIMD RGB vectors.
        /// </summary>
        /// <param name="red">Red channel vector.</param>
        /// <param name="green">Green channel vector.</param>
        /// <param name="blue">Blue channel vector.</param>
        public void Apply(ref Vector128<float> red, ref Vector128<float> green, ref Vector128<float> blue)
        {
            if (!_applyOotf)
            {
                return;
            }

            Vector128<float> luminance = (Vector128.Create(_redY) * red) +
                                         (Vector128.Create(_greenY) * green) +
                                         (Vector128.Create(_blueY) * blue);
            Vector128<float> ratio     = Vector128.Min(FastMath.FastPowf(luminance, Vector128.Create(_exponent)), Vector128.Create(1e9f));

            red   *= ratio;
            green *= ratio;
            blue  *= ratio;
        }
    }

    /// <summary>
    /// Scalar and SIMD gamut mapping helpers.
    /// Ported from <c>jxl::GamutMapScalar</c> and <c>jxl::GamutMap</c>
    /// in <c>lib/jxl/cms/tone_mapping.h</c> and <c>lib/jxl/cms/tone_mapping-inl.h</c>.
    /// </summary>
    public static class ToneMapping
    {
        /// <summary>
        /// Desaturates out-of-gamut scalar RGB pixels into the <c>[0, 1]</c> cube.
        /// </summary>
        /// <param name="rgb">RGB color to gamut map in-place.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        /// <param name="preserveSaturation">Balance between luminance and saturation preservation.</param>
        public static void GamutMapScalar(
            ref Vector3 rgb,
            Vector3 primariesLuminances,
            float preserveSaturation = 0.1f)
        {
            float luminance = (primariesLuminances[0] * rgb[0]) +
                              (primariesLuminances[1] * rgb[1]) +
                              (primariesLuminances[2] * rgb[2]);

            float grayMixSaturation = 0.0f;
            float grayMixLuminance  = 0.0f;

            for (int idx = 0; idx < 3; ++idx)
            {
                float val                 = rgb[idx];
                float valMinusGray        = val - luminance;
                float invValMinusGray     = 1.0f / (valMinusGray == 0.0f ? 1.0f : valMinusGray);
                float valOverValMinusGray = val * invValMinusGray;

                grayMixSaturation = valMinusGray >= 0.0f
                    ? grayMixSaturation
                    : Math.Max(grayMixSaturation, valOverValMinusGray);

                grayMixLuminance = Math.Max(
                    grayMixLuminance,
                    valMinusGray <= 0.0f
                        ? grayMixSaturation
                        : valOverValMinusGray - invValMinusGray);
            }

            float grayMix = Common.Clamp1(
                (preserveSaturation * (grayMixSaturation - grayMixLuminance)) + grayMixLuminance,
                0.0f,
                1.0f);

            for (int idx = 0; idx < 3; ++idx)
            {
                float val = rgb[idx];
                rgb[idx] = (grayMix * (luminance - val)) + val;
            }

            float maxClr     = Math.Max(Math.Max(1.0f, rgb[0]), Math.Max(rgb[1], rgb[2]));
            float normalizer = 1.0f / maxClr;

            rgb[0] *= normalizer;
            rgb[1] *= normalizer;
            rgb[2] *= normalizer;
        }

        /// <summary>
        /// Desaturates out-of-gamut 4-lane SIMD RGB vectors into the <c>[0, 1]</c> cube.
        /// </summary>
        /// <param name="red">Red channel vector.</param>
        /// <param name="green">Green channel vector.</param>
        /// <param name="blue">Blue channel vector.</param>
        /// <param name="primariesLuminances">Y components of the RGB primaries.</param>
        /// <param name="preserveSaturation">Balance between luminance and saturation preservation.</param>
        public static void GamutMap(
            ref Vector128<float> red,
            ref Vector128<float> green,
            ref Vector128<float> blue,
            Vector3 primariesLuminances,
            float preserveSaturation = 0.1f)
        {
            Vector128<float> luminance = (Vector128.Create(primariesLuminances[0]) * red) +
                                         (Vector128.Create(primariesLuminances[1]) * green) +
                                         (Vector128.Create(primariesLuminances[2]) * blue);

            Vector128<float> zero              = Vector128<float>.Zero;
            Vector128<float> one               = Vector128.Create(1.0f);
            Vector128<float> grayMixSaturation = zero;
            Vector128<float> grayMixLuminance  = zero;

            AccumulateChannelGamutMix(red, luminance, zero, one, ref grayMixSaturation, ref grayMixLuminance);
            AccumulateChannelGamutMix(green, luminance, zero, one, ref grayMixSaturation, ref grayMixLuminance);
            AccumulateChannelGamutMix(blue, luminance, zero, one, ref grayMixSaturation, ref grayMixLuminance);

            Vector128<float> rawMix  = (Vector128.Create(preserveSaturation) * (grayMixSaturation - grayMixLuminance)) + grayMixLuminance;
            Vector128<float> grayMix = Vector128.Min(Vector128.Max(rawMix, zero), one);

            red   = (grayMix * (luminance - red)) + red;
            green = (grayMix * (luminance - green)) + green;
            blue  = (grayMix * (luminance - blue)) + blue;

            Vector128<float> maxClr     = Vector128.Max(Vector128.Max(one, red), Vector128.Max(green, blue));
            Vector128<float> normalizer = one / maxClr;

            red   *= normalizer;
            green *= normalizer;
            blue  *= normalizer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AccumulateChannelGamutMix(
            Vector128<float> val,
            Vector128<float> luminance,
            Vector128<float> zero,
            Vector128<float> one,
            ref Vector128<float> grayMixSaturation,
            ref Vector128<float> grayMixLuminance)
        {
            Vector128<float> valMinusGray        = val - luminance;
            Vector128<float> denom               = Vector128.ConditionalSelect(Vector128.Equals(valMinusGray, zero), one, valMinusGray);
            Vector128<float> invValMinusGray     = one / denom;
            Vector128<float> valOverValMinusGray = val * invValMinusGray;

            grayMixSaturation = Vector128.ConditionalSelect(
                Vector128.GreaterThanOrEqual(valMinusGray, zero),
                grayMixSaturation,
                Vector128.Max(grayMixSaturation, valOverValMinusGray));

            Vector128<float> lumCandidate = Vector128.ConditionalSelect(
                Vector128.LessThanOrEqual(valMinusGray, zero),
                grayMixSaturation,
                valOverValMinusGray - invValMinusGray);

            grayMixLuminance = Vector128.Max(grayMixLuminance, lumCandidate);
        }
    }
}
