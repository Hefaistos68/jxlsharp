// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Identifies which step of the 3-step edge-preserving filter (EPF) to construct.
    /// Ported from <c>jxl::EpfStage</c> in <c>lib/jxl/render_pipeline/stage_epf.h</c>.
    /// </summary>
    public enum EpfStage : byte
    {
        /// <summary>First EPF step (7x7 filter, only run when <c>epf_iters &gt;= 3</c>).</summary>
        Zero = 0,

        /// <summary>Second EPF step (5x5 filter, run when <c>epf_iters &gt;= 1</c>).</summary>
        One = 1,

        /// <summary>Third EPF step (3x3 filter, run when <c>epf_iters &gt;= 2</c>).</summary>
        Two = 2,
    }

    /// <summary>
    /// Factory methods for creating JPEG XL <see cref="RenderPipelineStage"/> instances.
    /// Ported from <c>lib/jxl/render_pipeline/stage_*.h</c> and <c>stage_*.cc</c>.
    /// </summary>
    public static partial class RenderPipelineStages
    {
        /// <summary>
        /// Creates a decoder-side Gaborish 3x3 smoothing stage when <paramref name="lf"/> has <c>Gab == true</c>.
        /// Ported from <c>jxl::GetGaborishStage</c> in <c>lib/jxl/render_pipeline/stage_gaborish.cc</c>.
        /// </summary>
        /// <param name="lf">Loop filter configuration.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> or <c>null</c> if Gaborish is disabled.</returns>
        public static RenderPipelineStage? GetGaborishStage(LoopFilter lf)
        {
            if (!lf.Gab)
            {
                return null;
            }

            return new GaborishStage(lf);
        }

        /// <summary>
        /// Creates the <paramref name="epfStage"/>-th Edge-Preserving Filter stage with <paramref name="lf"/> and <paramref name="sigma"/>.
        /// Ported from <c>jxl::GetEPFStage</c> in <c>lib/jxl/render_pipeline/stage_epf.cc</c>.
        /// </summary>
        /// <param name="lf">Loop filter configuration.</param>
        /// <param name="sigma">Precomputed <c>1 / sigma</c> plane.</param>
        /// <param name="epfStage">EPF step selector.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> or <c>null</c> if EPF is disabled.</returns>
        public static RenderPipelineStage? GetEPFStage(
            LoopFilter lf,
            ImagePlane<float> sigma,
            EpfStage epfStage)
        {
            if (lf.EpfIters == 0)
            {
                return null;
            }

            return epfStage switch
            {
                EpfStage.Zero => new Epf0Stage(lf, sigma),
                EpfStage.One  => new Epf1Stage(lf, sigma),
                EpfStage.Two  => new Epf2Stage(lf, sigma),
                _             => null,
            };
        }

        /// <summary>
        /// Creates a 2x horizontal or vertical chroma upsampling stage for <paramref name="channel"/>.
        /// Ported from <c>jxl::GetChromaUpsamplingStage</c> in <c>lib/jxl/render_pipeline/stage_chroma_upsampling.cc</c>.
        /// </summary>
        /// <param name="channel">Channel index to upsample.</param>
        /// <param name="horizontal">Whether to upsample horizontally (<c>true</c>) or vertically (<c>false</c>).</param>
        /// <returns>New chroma upsampling <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetChromaUpsamplingStage(nuint channel, bool horizontal)
        {
            return horizontal
                ? new HorizontalChromaUpsamplingStage(channel)
                : new VerticalChromaUpsamplingStage(channel);
        }

        /// <summary>
        /// Creates a 5x5 separable/non-separable upsampling stage for channel <paramref name="c"/> by <c>1 &lt;&lt; shift</c>.
        /// Ported from <c>jxl::GetUpsamplingStage</c> in <c>lib/jxl/render_pipeline/stage_upsampling.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate per-thread scratch buffers.</param>
        /// <param name="upsFactors">Custom upsampling kernel weights.</param>
        /// <param name="c">Channel index to upsample.</param>
        /// <param name="shift">Log2 of upsampling factor (1, 2, or 3 for 2x, 4x, or 8x).</param>
        /// <returns>New upsampling <see cref="RenderPipelineStage"/> or <c>null</c> if <paramref name="shift"/> is invalid.</returns>
        public static RenderPipelineStage? GetUpsamplingStage(
            JxlMemoryManager? memoryManager,
            CustomTransformData upsFactors,
            nuint c,
            nuint shift)
        {
            if (shift < 1 || shift > 3)
            {
                return null;
            }

            return new UpsamplingStage(memoryManager, upsFactors, c, shift);
        }

        /// <summary>
        /// Creates a stage that draws decoded <paramref name="patches"/> onto the image channels.
        /// Ported from <c>jxl::GetPatchesStage</c> in <c>lib/jxl/render_pipeline/stage_patches.cc</c>.
        /// </summary>
        /// <param name="patches">Decoded patch dictionary.</param>
        /// <param name="extraChannelInfo">Metadata for extra channels.</param>
        /// <returns>New patches <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetPatchesStage(
            PatchDictionary patches,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            return new PatchDictionaryStage(patches, extraChannelInfo);
        }

        /// <summary>
        /// Creates a stage that adds rendered <paramref name="splines"/> to the XYB channels.
        /// Ported from <c>jxl::GetSplineStage</c> in <c>lib/jxl/render_pipeline/stage_splines.cc</c>.
        /// </summary>
        /// <param name="splines">Decoded splines.</param>
        /// <returns>New spline <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetSplineStage(Splines splines)
        {
            return new SplineStage(splines);
        }

        /// <summary>
        /// Creates a stage that adds synthesized noise to the XYB color channels.
        /// Ported from <c>jxl::GetAddNoiseStage</c> in <c>lib/jxl/render_pipeline/stage_noise.cc</c>.
        /// </summary>
        /// <param name="noiseParams">Noise LUT parameters.</param>
        /// <param name="colorCorrelation">Chroma-from-luma correlation ratios.</param>
        /// <param name="noiseCStart">Index of the first temporary noise channel.</param>
        /// <returns>New noise addition <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetAddNoiseStage(
            NoiseParams noiseParams,
            ColorCorrelation colorCorrelation,
            nuint noiseCStart)
        {
            return new AddNoiseStage(noiseParams, colorCorrelation, noiseCStart);
        }

        /// <summary>
        /// Creates a stage that applies a 5x5 high-pass box-filter convolution to the 3 temporary noise channels.
        /// Ported from <c>jxl::GetConvolveNoiseStage</c> in <c>lib/jxl/render_pipeline/stage_noise.cc</c>.
        /// </summary>
        /// <param name="noiseCStart">Index of the first temporary noise channel.</param>
        /// <returns>New noise convolution <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetConvolveNoiseStage(nuint noiseCStart)
        {
            return new ConvolveNoiseStage(noiseCStart);
        }

        /// <summary>
        /// Creates a stage that converts YCbCr channels (stored in Cb, Y, Cr order in channels 0, 1, 2) to RGB.
        /// Ported from <c>jxl::GetYCbCrStage</c> in <c>lib/jxl/render_pipeline/stage_ycbcr.cc</c>.
        /// </summary>
        /// <returns>New YCbCr-to-RGB <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetYCbCrStage()
        {
            return new YCbCrStage();
        }

        /// <summary>
        /// Creates a stage that converts XYB channels to linear RGB (or scaled XYB).
        /// Ported from <c>jxl::GetXYBStage</c> in <c>lib/jxl/render_pipeline/stage_xyb.cc</c>.
        /// </summary>
        /// <param name="outputEncodingInfo">Output encoding and opsin inverse parameters.</param>
        /// <returns>New XYB <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetXYBStage(OutputEncodingInfo outputEncodingInfo)
        {
            return new XybStage(outputEncodingInfo);
        }

        /// <summary>
        /// Creates a fast XYB-to-sRGB8 stage if supported on the current target.
        /// Ported from <c>jxl::GetFastXYBTosRGB8Stage</c> in <c>lib/jxl/render_pipeline/stage_xyb.cc</c>.
        /// </summary>
        /// <param name="rgb">Destination 8-bit buffer memory.</param>
        /// <param name="stride">Row stride in bytes.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="rgba">Whether output has 4 channels (RGBA) or 3 (RGB).</param>
        /// <param name="hasAlpha">Whether an alpha channel is present.</param>
        /// <param name="alphaC">Alpha channel index.</param>
        /// <returns>New fast XYB stage, or <c>null</c> if fast XYB is disabled.</returns>
        public static RenderPipelineStage? GetFastXYBTosRGB8Stage(
            Memory<byte> rgb,
            nuint stride,
            nuint width,
            nuint height,
            bool rgba,
            bool hasAlpha,
            nuint alphaC)
        {
            if (!DecXyb.HasFastXYBTosRGB8())
            {
                return null;
            }

            return new FastXybStage(rgb, stride, width, height, rgba, hasAlpha, alphaC);
        }

        /// <summary>
        /// Creates a stage that composites a spot color extra channel onto the RGB channels.
        /// Ported from <c>jxl::GetSpotColorStage</c> in <c>lib/jxl/render_pipeline/stage_spot.cc</c>.
        /// </summary>
        /// <param name="spotCOffset">Extra channel index (<c>spot_c = 3 + spotCOffset</c>).</param>
        /// <param name="spotColor">4-element RGBA spot color array.</param>
        /// <returns>New spot color <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetSpotColorStage(nuint spotCOffset, float[] spotColor)
        {
            return new SpotColorStage(spotCOffset, spotColor);
        }

        /// <summary>
        /// Creates a stage that converts channels 0..2 from <paramref name="outputEncodingInfo"/>'s transfer function to linear light.
        /// Ported from <c>jxl::GetToLinearStage</c> in <c>lib/jxl/render_pipeline/stage_to_linear.cc</c>.
        /// </summary>
        /// <param name="outputEncodingInfo">Output encoding descriptor.</param>
        /// <returns>New to-linear <see cref="RenderPipelineStage"/>.</returns>
        public static RenderPipelineStage GetToLinearStage(OutputEncodingInfo outputEncodingInfo)
        {
            return new ToLinearStage(outputEncodingInfo);
        }

        /// <summary>
        /// Creates a stage that converts channels 0..2 from linear light to <paramref name="outputEncodingInfo"/>'s transfer function.
        /// Ported from <c>jxl::GetFromLinearStage</c> in <c>lib/jxl/render_pipeline/stage_from_linear.cc</c>.
        /// </summary>
        /// <param name="outputEncodingInfo">Output encoding descriptor.</param>
        /// <returns>New from-linear <see cref="RenderPipelineStage"/> or <c>null</c> if unsupported.</returns>
        public static RenderPipelineStage? GetFromLinearStage(OutputEncodingInfo outputEncodingInfo)
        {
            ref CustomTransferFunction tf = ref outputEncodingInfo.ColorEncoding.Tf;

            if (!tf.IsLinear() &&
                !tf.IsSRGB() &&
                !tf.IsPQ() &&
                !tf.IsHLG() &&
                !tf.Is709() &&
                !tf.HaveGamma &&
                !tf.IsDCI())
            {
                return null;
            }

            return new FromLinearStage(outputEncodingInfo);
        }

        /// <summary>
        /// Creates a tone mapping stage if <paramref name="outputEncodingInfo"/> requests HDR-to-SDR/HDR tone mapping.
        /// Ported from <c>jxl::GetToneMappingStage</c> in <c>lib/jxl/render_pipeline/stage_tone_mapping.cc</c>.
        /// </summary>
        /// <param name="outputEncodingInfo">Output encoding descriptor.</param>
        /// <returns>New tone mapping <see cref="RenderPipelineStage"/> or <c>null</c> if not needed.</returns>
        public static RenderPipelineStage? GetToneMappingStage(OutputEncodingInfo outputEncodingInfo)
        {
            ToneMappingStage stage = new ToneMappingStage(outputEncodingInfo);

            return stage.IsNeeded ? stage : null;
        }

        /// <summary>
        /// Creates a CMS color space transform stage if needed.
        /// Ported from <c>jxl::GetCmsStage</c> in <c>lib/jxl/render_pipeline/stage_cms.cc</c>.
        /// </summary>
        /// <param name="outputEncodingInfo">Output encoding descriptor.</param>
        /// <param name="linear">Whether input to the CMS stage is in linear light (<c>true</c>) or original encoding (<c>false</c>).</param>
        /// <returns>New CMS <see cref="RenderPipelineStage"/> or <c>null</c> if not needed.</returns>
        public static RenderPipelineStage? GetCmsStage(OutputEncodingInfo outputEncodingInfo, bool linear = true)
        {
            CmsStage stage = new CmsStage(outputEncodingInfo, linear);

            if (!stage.IsNeeded)
            {
                stage.Dispose();

                return null;
            }

            return stage;
        }
    }

    /// <summary>
    /// Applies a 3x3 Gaborish deblocking/smoothing convolution to channels 0..2.
    /// Ported from <c>jxl::HWY_NAMESPACE::GaborishStage</c> in <c>lib/jxl/render_pipeline/stage_gaborish.cc</c>.
    /// </summary>
    internal sealed class GaborishStage : RenderPipelineStage
    {
        private readonly float[] _weights = new float[9];

        public GaborishStage(LoopFilter lf)
            : base(Settings.SymmetricBorderOnly(1))
        {
            _weights[0] = 1.0f;
            _weights[1] = lf.GabXWeight1;
            _weights[2] = lf.GabXWeight2;
            _weights[3] = 1.0f;
            _weights[4] = lf.GabYWeight1;
            _weights[5] = lf.GabYWeight2;
            _weights[6] = 1.0f;
            _weights[7] = lf.GabBWeight1;
            _weights[8] = lf.GabBWeight2;

            for (int c = 0; c < 3; c++)
            {
                float div             = _weights[3 * c] + (4.0f * (_weights[(3 * c) + 1] + _weights[(3 * c) + 2]));
                float mul             = 1.0f / div;
                _weights[3 * c]       *= mul;
                _weights[(3 * c) + 1] *= mul;
                _weights[(3 * c) + 2] *= mul;
            }
        }

        /// <inheritdoc />
        public override string GetName() => "Gab";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            const nuint lanes = 4;
            nint xStart       = -(nint)Common.RoundUpTo(xextra.Left, lanes);
            nint xEnd         = (nint)(xsize + xextra.Right);

            for (nuint c = 0; c < 3; c++)
            {
                PipelineRow rowT    = GetInputRow(inputRows, c, -1);
                PipelineRow rowM    = GetInputRow(inputRows, c, 0);
                PipelineRow rowB    = GetInputRow(inputRows, c, 1);
                PipelineRow rowOut  = GetOutputRow(outputRows, c, 0);
                Vector128<float> w0 = Vector128.Create(_weights[3 * c]);
                Vector128<float> w1 = Vector128.Create(_weights[(3 * c) + 1]);
                Vector128<float> w2 = Vector128.Create(_weights[(3 * c) + 2]);

                nint x = xStart;
                if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                {
                    Vector256<float> w0v = Vector256.Create(_weights[3 * c]);
                    Vector256<float> w1v = Vector256.Create(_weights[(3 * c) + 1]);
                    Vector256<float> w2v = Vector256.Create(_weights[(3 * c) + 2]);
                    for (; x + 8 <= xEnd; x += 8)
                    {
                        Vector256<float> t    = Vector256.LoadUnsafe(ref rowT[x]);
                        Vector256<float> tl   = Vector256.LoadUnsafe(ref rowT[x - 1]);
                        Vector256<float> tr   = Vector256.LoadUnsafe(ref rowT[x + 1]);
                        Vector256<float> m    = Vector256.LoadUnsafe(ref rowM[x]);
                        Vector256<float> l    = Vector256.LoadUnsafe(ref rowM[x - 1]);
                        Vector256<float> r    = Vector256.LoadUnsafe(ref rowM[x + 1]);
                        Vector256<float> b    = Vector256.LoadUnsafe(ref rowB[x]);
                        Vector256<float> bl   = Vector256.LoadUnsafe(ref rowB[x - 1]);
                        Vector256<float> br   = Vector256.LoadUnsafe(ref rowB[x + 1]);
                        Vector256<float> sum0 = m;
                        Vector256<float> sum1 = (l + r) + (t + b);
                        Vector256<float> sum2 = (tl + tr) + (bl + br);
                        Vector256<float> px   = (sum2 * w2v) + (sum1 * w1v) + (sum0 * w0v);
                        px.StoreUnsafe(ref rowOut[x]);
                    }
                }

                for (; x < xEnd; x += (nint)lanes)
                {
                    Vector128<float> t    = Vector128.LoadUnsafe(ref rowT[x]);
                    Vector128<float> tl   = Vector128.LoadUnsafe(ref rowT[x - 1]);
                    Vector128<float> tr   = Vector128.LoadUnsafe(ref rowT[x + 1]);
                    Vector128<float> m    = Vector128.LoadUnsafe(ref rowM[x]);
                    Vector128<float> l    = Vector128.LoadUnsafe(ref rowM[x - 1]);
                    Vector128<float> r    = Vector128.LoadUnsafe(ref rowM[x + 1]);
                    Vector128<float> b    = Vector128.LoadUnsafe(ref rowB[x]);
                    Vector128<float> bl   = Vector128.LoadUnsafe(ref rowB[x - 1]);
                    Vector128<float> br   = Vector128.LoadUnsafe(ref rowB[x + 1]);
                    Vector128<float> sum0 = m;
                    Vector128<float> sum1 = (l + r) + (t + b);
                    Vector128<float> sum2 = (tl + tr) + (bl + br);
                    Vector128<float> px   = (sum2 * w2) + (sum1 * w1) + (sum0 * w0);
                    px.StoreUnsafe(ref rowOut[x]);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Shared helper utilities for EPF stages 0, 1, and 2.
    /// Ported from <c>lib/jxl/render_pipeline/stage_epf.cc</c>.
    /// </summary>
    internal static class EpfStageHelpers
    {
        public static readonly (int Row, int Col)[] SadsOffsets =
        {
            (-2, 0), (-1, -1), (-1, 0), (-1, 1), (0, -2), (0, -1),
            (0, 1),  (0, 2),   (1, -1), (1, 0),  (1, 1),  (2, 0),
        };

        public static readonly (int Row, int Col)[] PlusOffsets =
        {
            (0, 0), (-1, 0), (0, -1), (1, 0), (0, 1),
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WeightScalar(float sad, float invSigma)
        {
            float v = (sad * invSigma) + 1.0f;

            return v < 0.0f ? 0.0f : v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> WeightScalar(Vector256<float> sad, Vector256<float> invSigma)
        {
            Vector256<float> value = sad * invSigma + Vector256.Create(1.0f);
            return Vector256.ConditionalSelect(Vector256.LessThan(value, Vector256<float>.Zero), Vector256<float>.Zero, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetSadMul(nuint ypos, nuint ix, float sm, float bsm)
        {
            bool isRowBorder = (ypos % FrameDimensions.KBlockDim) == 0 ||
                               (ypos % FrameDimensions.KBlockDim) == (FrameDimensions.KBlockDim - 1);

            if (isRowBorder || ix == 0 || ix == FrameDimensions.KBlockDim - 1)
            {
                return bsm;
            }

            return sm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyUnfilteredPixel(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            nint x)
        {
            for (nuint c = 0; c < 3; c++)
            {
                PipelineRow src = inputRows[c][inputRows[c].Length / 2] + RenderPipelineStage.KRenderPipelineXOffset;
                PipelineRow dst = outputRows[c][0] + RenderPipelineStage.KRenderPipelineXOffset;
                dst[x]          = src[x];
            }
        }
    }

    /// <summary>
    /// EPF Stage 0: 5x5 plus-shaped kernel with 5 SADs per pixel (7x7 effective support).
    /// Ported from <c>jxl::HWY_NAMESPACE::EPF0Stage</c> in <c>lib/jxl/render_pipeline/stage_epf.cc</c>.
    /// </summary>
    internal sealed class Epf0Stage : RenderPipelineStage
    {
        private readonly LoopFilter _lf;
        private readonly ImagePlane<float> _sigma;

        public Epf0Stage(LoopFilter lf, ImagePlane<float> sigma)
            : base(Settings.SymmetricBorderOnly(3))
        {
            _lf    = lf;
            _sigma = sigma;
        }

        /// <inheritdoc />
        public override string GetName() => "EPF0";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart                  = -(nint)Common.RoundUpTo(xextra.Left, 4u);
            nint xEnd                    = (nint)(xsize + xextra.Right);
            ReadOnlySpan<float> rowSigma = _sigma.ConstRow((ypos / FrameDimensions.KBlockDim) + Epf.KSigmaPadding);
            float sm                     = _lf.EpfPass0SigmaScale * 1.65f;
            float bsm                    = sm * _lf.EpfBorderSadMul;
            PipelineRow out0             = GetOutputRow(outputRows, 0, 0);
            PipelineRow out1             = GetOutputRow(outputRows, 1, 0);
            PipelineRow out2             = GetOutputRow(outputRows, 2, 0);

            nint x = xStart;
            Span<float> invSigmas = stackalloc float[8];
            Span<int> skip = stackalloc int[8];
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; x + 8 <= xEnd; x += 8)
                {
                    for (int lane = 0; lane < 8; lane++)
                    {
                        nuint absX = (nuint)(x + lane + (nint)xpos);
                        nuint bx = (absX + Epf.KSigmaPadding * FrameDimensions.KBlockDim) / FrameDimensions.KBlockDim;
                        float sigma = rowSigma[(int)bx];
                        skip[lane] = sigma < Epf.KMinSigma ? -1 : 0;
                        invSigmas[lane] = sigma * EpfStageHelpers.GetSadMul(ypos, absX % FrameDimensions.KBlockDim, sm, bsm);
                    }
                    Vector256<int> mask = Vector256.Create(skip);
                    if (Vector256.EqualsAll(mask, Vector256.Create(-1)))
                    {
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]).StoreUnsafe(ref out0[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]).StoreUnsafe(ref out1[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]).StoreUnsafe(ref out2[x]);
                        continue;
                    }
                    FilterPixel(inputRows, x, Vector256.Create(invSigmas), out Vector256<float> vx, out Vector256<float> vy, out Vector256<float> vb);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]), vx).StoreUnsafe(ref out0[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]), vy).StoreUnsafe(ref out1[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]), vb).StoreUnsafe(ref out2[x]);
                }
            }

            for (; x < xEnd; x++)
            {
                nuint absX = (nuint)(x + (nint)xpos);
                nuint bx   = (absX + (Epf.KSigmaPadding * FrameDimensions.KBlockDim)) / FrameDimensions.KBlockDim;
                nuint ix   = absX % FrameDimensions.KBlockDim;

                if (rowSigma[(int)bx] < Epf.KMinSigma)
                {
                    out0[x] = GetInputRow(inputRows, 0, 0)[x];
                    out1[x] = GetInputRow(inputRows, 1, 0)[x];
                    out2[x] = GetInputRow(inputRows, 2, 0)[x];
                    continue;
                }

                float invSigma = rowSigma[(int)bx] * EpfStageHelpers.GetSadMul(ypos, ix, sm, bsm);
                FilterPixel(inputRows, x, invSigma, out out0[x], out out1[x], out out2[x]);
            }

            return true;
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            float invSigma,
            out float outX,
            out float outY,
            out float outB)
        {
            Span<float> sads = stackalloc float[12];
            sads.Clear();

            for (nuint c = 0; c < 3; c++)
            {
                float scale = _lf.EpfChannelScale[c];

                for (int i = 0; i < 12; i++)
                {
                    (int sRow, int sCol) = EpfStageHelpers.SadsOffsets[i];
                    float sad            = 0.0f;

                    for (int p = 0; p < 5; p++)
                    {
                        (int pRow, int pCol) = EpfStageHelpers.PlusOffsets[p];
                        float r11            = GetInputRow(inputRows, c, pRow)[x + pCol];
                        float c11            = GetInputRow(inputRows, c, sRow + pRow)[x + sCol + pCol];
                        sad                 += MathF.Abs(r11 - c11);
                    }

                    sads[i] += sad * scale;
                }
            }

            float accW = 1.0f;
            float accX = GetInputRow(inputRows, 0, 0)[x];
            float accY = GetInputRow(inputRows, 1, 0)[x];
            float accB = GetInputRow(inputRows, 2, 0)[x];

            for (int i = 0; i < 12; i++)
            {
                (int sRow, int sCol) = EpfStageHelpers.SadsOffsets[i];
                float weight         = EpfStageHelpers.WeightScalar(sads[i], invSigma);
                accW                += weight;
                accX                += weight * GetInputRow(inputRows, 0, sRow)[x + sCol];
                accY                += weight * GetInputRow(inputRows, 1, sRow)[x + sCol];
                accB                += weight * GetInputRow(inputRows, 2, sRow)[x + sCol];
            }

            float invW = 1.0f / accW;
            outX       = accX * invW;
            outY       = accY * invW;
            outB       = accB * invW;
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            Vector256<float> invSigma,
            out Vector256<float> outX,
            out Vector256<float> outY,
            out Vector256<float> outB)
        {
            Span<Vector256<float>> sads = stackalloc Vector256<float>[12];
            sads.Clear();

            for (nuint c = 0; c < 3; c++)
            {
                Vector256<float> scale = Vector256.Create(_lf.EpfChannelScale[c]);

                for (int i = 0; i < 12; i++)
                {
                    (int sRow, int sCol) = EpfStageHelpers.SadsOffsets[i];
                    Vector256<float> sad            = Vector256.Create(0.0f);

                    for (int p = 0; p < 5; p++)
                    {
                        (int pRow, int pCol) = EpfStageHelpers.PlusOffsets[p];
                        Vector256<float> r11            = Vector256.LoadUnsafe(ref GetInputRow(inputRows, c, pRow)[x + pCol]);
                        Vector256<float> c11            = Vector256.LoadUnsafe(ref GetInputRow(inputRows, c, sRow + pRow)[x + sCol + pCol]);
                        sad                 += Vector256.Abs(r11 - c11);
                    }

                    sads[i] += sad * scale;
                }
            }

            Vector256<float> accW = Vector256.Create(1.0f);
            Vector256<float> accX = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]);
            Vector256<float> accY = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]);
            Vector256<float> accB = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]);

            for (int i = 0; i < 12; i++)
            {
                (int sRow, int sCol) = EpfStageHelpers.SadsOffsets[i];
                Vector256<float> weight         = EpfStageHelpers.WeightScalar(sads[i], invSigma);
                accW                += weight;
                accX                += weight * Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, sRow)[x + sCol]);
                accY                += weight * Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, sRow)[x + sCol]);
                accB                += weight * Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, sRow)[x + sCol]);
            }

            Vector256<float> invW = Vector256.Create(1.0f) / accW;
            outX       = accX * invW;
            outY       = accY * invW;
            outB       = accB * invW;
        }
    }

    /// <summary>
    /// EPF Stage 1: 3x3 plus-shaped kernel with 5 SADs per pixel (5x5 effective support).
    /// Ported from <c>jxl::HWY_NAMESPACE::EPF1Stage</c> in <c>lib/jxl/render_pipeline/stage_epf.cc</c>.
    /// </summary>
    internal sealed class Epf1Stage : RenderPipelineStage
    {
        private readonly LoopFilter _lf;
        private readonly ImagePlane<float> _sigma;

        public Epf1Stage(LoopFilter lf, ImagePlane<float> sigma)
            : base(Settings.SymmetricBorderOnly(2))
        {
            _lf    = lf;
            _sigma = sigma;
        }

        /// <inheritdoc />
        public override string GetName() => "EPF1";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart                  = -(nint)Common.RoundUpTo(xextra.Left, 4u);
            nint xEnd                    = (nint)(xsize + xextra.Right);
            ReadOnlySpan<float> rowSigma = _sigma.ConstRow((ypos / FrameDimensions.KBlockDim) + Epf.KSigmaPadding);
            const float sm               = 1.65f;
            float bsm                    = sm * _lf.EpfBorderSadMul;
            PipelineRow out0             = GetOutputRow(outputRows, 0, 0);
            PipelineRow out1             = GetOutputRow(outputRows, 1, 0);
            PipelineRow out2             = GetOutputRow(outputRows, 2, 0);

            nint x = xStart;
            Span<float> invSigmas = stackalloc float[8];
            Span<int> skip = stackalloc int[8];
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; x + 8 <= xEnd; x += 8)
                {
                    for (int lane = 0; lane < 8; lane++)
                    {
                        nuint absX = (nuint)(x + lane + (nint)xpos);
                        nuint bx = (absX + Epf.KSigmaPadding * FrameDimensions.KBlockDim) / FrameDimensions.KBlockDim;
                        float sigma = rowSigma[(int)bx];
                        skip[lane] = sigma < Epf.KMinSigma ? -1 : 0;
                        invSigmas[lane] = sigma * EpfStageHelpers.GetSadMul(ypos, absX % FrameDimensions.KBlockDim, sm, bsm);
                    }
                    Vector256<int> mask = Vector256.Create(skip);
                    if (Vector256.EqualsAll(mask, Vector256.Create(-1)))
                    {
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]).StoreUnsafe(ref out0[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]).StoreUnsafe(ref out1[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]).StoreUnsafe(ref out2[x]);
                        continue;
                    }
                    FilterPixel(inputRows, x, Vector256.Create(invSigmas), out Vector256<float> vx, out Vector256<float> vy, out Vector256<float> vb);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]), vx).StoreUnsafe(ref out0[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]), vy).StoreUnsafe(ref out1[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]), vb).StoreUnsafe(ref out2[x]);
                }
            }

            for (; x < xEnd; x++)
            {
                nuint absX = (nuint)(x + (nint)xpos);
                nuint bx   = (absX + (Epf.KSigmaPadding * FrameDimensions.KBlockDim)) / FrameDimensions.KBlockDim;
                nuint ix   = absX % FrameDimensions.KBlockDim;

                if (rowSigma[(int)bx] < Epf.KMinSigma)
                {
                    out0[x] = GetInputRow(inputRows, 0, 0)[x];
                    out1[x] = GetInputRow(inputRows, 1, 0)[x];
                    out2[x] = GetInputRow(inputRows, 2, 0)[x];
                    continue;
                }

                float invSigma = rowSigma[(int)bx] * EpfStageHelpers.GetSadMul(ypos, ix, sm, bsm);
                FilterPixel(inputRows, x, invSigma, out out0[x], out out1[x], out out2[x]);
            }

            return true;
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            float invSigma,
            out float outX,
            out float outY,
            out float outB)
        {
            float sad0 = 0.0f;
            float sad1 = 0.0f;
            float sad2 = 0.0f;
            float sad3 = 0.0f;

            for (nuint c = 0; c < 3; c++)
            {
                PipelineRow rm2 = GetInputRow(inputRows, c, -2);
                PipelineRow rm1 = GetInputRow(inputRows, c, -1);
                PipelineRow r0  = GetInputRow(inputRows, c, 0);
                PipelineRow rp1 = GetInputRow(inputRows, c, 1);
                PipelineRow rp2 = GetInputRow(inputRows, c, 2);

                float p20 = rm2[x];
                float p21 = rm1[x];
                float p11 = rm1[x - 1];
                float p31 = rm1[x + 1];
                float p02 = r0[x - 2];
                float p12 = r0[x - 1];
                float p22 = r0[x];
                float p32 = r0[x + 1];
                float p42 = r0[x + 2];
                float p13 = rp1[x - 1];
                float p23 = rp1[x];
                float p33 = rp1[x + 1];
                float p24 = rp2[x];

                float d2021 = MathF.Abs(p20 - p21);
                float d1121 = MathF.Abs(p11 - p21);
                float d3121 = MathF.Abs(p31 - p21);
                float d0212 = MathF.Abs(p02 - p12);
                float d1112 = MathF.Abs(p11 - p12);
                float d1222 = MathF.Abs(p12 - p22);
                float d2221 = MathF.Abs(p22 - p21);
                float d3132 = MathF.Abs(p31 - p32);
                float d2232 = MathF.Abs(p22 - p32);
                float d4232 = MathF.Abs(p42 - p32);
                float d1312 = MathF.Abs(p13 - p12);
                float d2223 = MathF.Abs(p22 - p23);
                float d1323 = MathF.Abs(p13 - p23);
                float d3323 = MathF.Abs(p33 - p23);
                float d3332 = MathF.Abs(p33 - p32);
                float d2423 = MathF.Abs(p24 - p23);

                float scale = _lf.EpfChannelScale[c];
                sad0       += (d2021 + d1112 + d2221 + d3132 + d2223) * scale;
                sad1       += (d1121 + d0212 + d1222 + d2232 + d1323) * scale;
                sad2       += (d3121 + d1222 + d2232 + d4232 + d3323) * scale;
                sad3       += (d2221 + d1312 + d2223 + d3332 + d2423) * scale;
            }

            float w0   = EpfStageHelpers.WeightScalar(sad0, invSigma);
            float w1   = EpfStageHelpers.WeightScalar(sad1, invSigma);
            float w2   = EpfStageHelpers.WeightScalar(sad2, invSigma);
            float w3   = EpfStageHelpers.WeightScalar(sad3, invSigma);
            float invW = 1.0f / (1.0f + w0 + w1 + w2 + w3);

            outX = FilterChannelPlus(inputRows, 0, x, w0, w1, w2, w3) * invW;
            outY = FilterChannelPlus(inputRows, 1, x, w0, w1, w2, w3) * invW;
            outB = FilterChannelPlus(inputRows, 2, x, w0, w1, w2, w3) * invW;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float FilterChannelPlus(
            PipelineRow[][] inputRows,
            nuint c,
            nint x,
            float w0,
            float w1,
            float w2,
            float w3)
        {
            PipelineRow rm1 = GetInputRow(inputRows, c, -1);
            PipelineRow r0  = GetInputRow(inputRows, c, 0);
            PipelineRow rp1 = GetInputRow(inputRows, c, 1);

            return r0[x] +
                   (w0 * rm1[x]) +
                   (w1 * r0[x - 1]) +
                   (w2 * r0[x + 1]) +
                   (w3 * rp1[x]);
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            Vector256<float> invSigma,
            out Vector256<float> outX,
            out Vector256<float> outY,
            out Vector256<float> outB)
        {
            Vector256<float> sad0 = Vector256.Create(0.0f);
            Vector256<float> sad1 = Vector256.Create(0.0f);
            Vector256<float> sad2 = Vector256.Create(0.0f);
            Vector256<float> sad3 = Vector256.Create(0.0f);

            for (nuint c = 0; c < 3; c++)
            {
                PipelineRow rm2 = GetInputRow(inputRows, c, -2);
                PipelineRow rm1 = GetInputRow(inputRows, c, -1);
                PipelineRow r0  = GetInputRow(inputRows, c, 0);
                PipelineRow rp1 = GetInputRow(inputRows, c, 1);
                PipelineRow rp2 = GetInputRow(inputRows, c, 2);

                Vector256<float> p20 = Vector256.LoadUnsafe(ref rm2[x]);
                Vector256<float> p21 = Vector256.LoadUnsafe(ref rm1[x]);
                Vector256<float> p11 = Vector256.LoadUnsafe(ref rm1[x - 1]);
                Vector256<float> p31 = Vector256.LoadUnsafe(ref rm1[x + 1]);
                Vector256<float> p02 = Vector256.LoadUnsafe(ref r0[x - 2]);
                Vector256<float> p12 = Vector256.LoadUnsafe(ref r0[x - 1]);
                Vector256<float> p22 = Vector256.LoadUnsafe(ref r0[x]);
                Vector256<float> p32 = Vector256.LoadUnsafe(ref r0[x + 1]);
                Vector256<float> p42 = Vector256.LoadUnsafe(ref r0[x + 2]);
                Vector256<float> p13 = Vector256.LoadUnsafe(ref rp1[x - 1]);
                Vector256<float> p23 = Vector256.LoadUnsafe(ref rp1[x]);
                Vector256<float> p33 = Vector256.LoadUnsafe(ref rp1[x + 1]);
                Vector256<float> p24 = Vector256.LoadUnsafe(ref rp2[x]);

                Vector256<float> d2021 = Vector256.Abs(p20 - p21);
                Vector256<float> d1121 = Vector256.Abs(p11 - p21);
                Vector256<float> d3121 = Vector256.Abs(p31 - p21);
                Vector256<float> d0212 = Vector256.Abs(p02 - p12);
                Vector256<float> d1112 = Vector256.Abs(p11 - p12);
                Vector256<float> d1222 = Vector256.Abs(p12 - p22);
                Vector256<float> d2221 = Vector256.Abs(p22 - p21);
                Vector256<float> d3132 = Vector256.Abs(p31 - p32);
                Vector256<float> d2232 = Vector256.Abs(p22 - p32);
                Vector256<float> d4232 = Vector256.Abs(p42 - p32);
                Vector256<float> d1312 = Vector256.Abs(p13 - p12);
                Vector256<float> d2223 = Vector256.Abs(p22 - p23);
                Vector256<float> d1323 = Vector256.Abs(p13 - p23);
                Vector256<float> d3323 = Vector256.Abs(p33 - p23);
                Vector256<float> d3332 = Vector256.Abs(p33 - p32);
                Vector256<float> d2423 = Vector256.Abs(p24 - p23);

                Vector256<float> scale = Vector256.Create(_lf.EpfChannelScale[c]);
                sad0       += (d2021 + d1112 + d2221 + d3132 + d2223) * scale;
                sad1       += (d1121 + d0212 + d1222 + d2232 + d1323) * scale;
                sad2       += (d3121 + d1222 + d2232 + d4232 + d3323) * scale;
                sad3       += (d2221 + d1312 + d2223 + d3332 + d2423) * scale;
            }

            Vector256<float> w0   = EpfStageHelpers.WeightScalar(sad0, invSigma);
            Vector256<float> w1   = EpfStageHelpers.WeightScalar(sad1, invSigma);
            Vector256<float> w2   = EpfStageHelpers.WeightScalar(sad2, invSigma);
            Vector256<float> w3   = EpfStageHelpers.WeightScalar(sad3, invSigma);
            Vector256<float> invW = Vector256.Create(1.0f) / (Vector256.Create(1.0f) + w0 + w1 + w2 + w3);

            outX = FilterChannelPlus(inputRows, 0, x, w0, w1, w2, w3) * invW;
            outY = FilterChannelPlus(inputRows, 1, x, w0, w1, w2, w3) * invW;
            outB = FilterChannelPlus(inputRows, 2, x, w0, w1, w2, w3) * invW;
        }

        private Vector256<float> FilterChannelPlus(
            PipelineRow[][] inputRows,
            nuint c,
            nint x,
            Vector256<float> w0,
            Vector256<float> w1,
            Vector256<float> w2,
            Vector256<float> w3)
        {
            PipelineRow rm1 = GetInputRow(inputRows, c, -1);
            PipelineRow r0  = GetInputRow(inputRows, c, 0);
            PipelineRow rp1 = GetInputRow(inputRows, c, 1);

            return Vector256.LoadUnsafe(ref r0[x]) +
                   (w0 * Vector256.LoadUnsafe(ref rm1[x])) +
                   (w1 * Vector256.LoadUnsafe(ref r0[x - 1])) +
                   (w2 * Vector256.LoadUnsafe(ref r0[x + 1])) +
                   (w3 * Vector256.LoadUnsafe(ref rp1[x]));
        }
    }

    /// <summary>
    /// EPF Stage 2: 3x3 plus-shaped kernel with 1 SAD per pixel (3x3 effective support).
    /// Ported from <c>jxl::HWY_NAMESPACE::EPF2Stage</c> in <c>lib/jxl/render_pipeline/stage_epf.cc</c>.
    /// </summary>
    internal sealed class Epf2Stage : RenderPipelineStage
    {
        private readonly LoopFilter _lf;
        private readonly ImagePlane<float> _sigma;

        public Epf2Stage(LoopFilter lf, ImagePlane<float> sigma)
            : base(Settings.SymmetricBorderOnly(1))
        {
            _lf    = lf;
            _sigma = sigma;
        }

        /// <inheritdoc />
        public override string GetName() => "EPF2";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart                  = -(nint)Common.RoundUpTo(xextra.Left, 4u);
            nint xEnd                    = (nint)(xsize + xextra.Right);
            ReadOnlySpan<float> rowSigma = _sigma.ConstRow((ypos / FrameDimensions.KBlockDim) + Epf.KSigmaPadding);
            float sm                     = _lf.EpfPass2SigmaScale * 1.65f;
            float bsm                    = sm * _lf.EpfBorderSadMul;
            PipelineRow out0             = GetOutputRow(outputRows, 0, 0);
            PipelineRow out1             = GetOutputRow(outputRows, 1, 0);
            PipelineRow out2             = GetOutputRow(outputRows, 2, 0);

            nint x = xStart;
            Span<float> invSigmas = stackalloc float[8];
            Span<int> skip = stackalloc int[8];
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; x + 8 <= xEnd; x += 8)
                {
                    for (int lane = 0; lane < 8; lane++)
                    {
                        nuint absX = (nuint)(x + lane + (nint)xpos);
                        nuint bx = (absX + Epf.KSigmaPadding * FrameDimensions.KBlockDim) / FrameDimensions.KBlockDim;
                        float sigma = rowSigma[(int)bx];
                        skip[lane] = sigma < Epf.KMinSigma ? -1 : 0;
                        invSigmas[lane] = sigma * EpfStageHelpers.GetSadMul(ypos, absX % FrameDimensions.KBlockDim, sm, bsm);
                    }
                    Vector256<int> mask = Vector256.Create(skip);
                    if (Vector256.EqualsAll(mask, Vector256.Create(-1)))
                    {
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]).StoreUnsafe(ref out0[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]).StoreUnsafe(ref out1[x]);
                        Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]).StoreUnsafe(ref out2[x]);
                        continue;
                    }
                    FilterPixel(inputRows, x, Vector256.Create(invSigmas), out Vector256<float> vx, out Vector256<float> vy, out Vector256<float> vb);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]), vx).StoreUnsafe(ref out0[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]), vy).StoreUnsafe(ref out1[x]);
                    Vector256.ConditionalSelect(mask.AsSingle(), Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]), vb).StoreUnsafe(ref out2[x]);
                }
            }

            for (; x < xEnd; x++)
            {
                nuint absX = (nuint)(x + (nint)xpos);
                nuint bx   = (absX + (Epf.KSigmaPadding * FrameDimensions.KBlockDim)) / FrameDimensions.KBlockDim;
                nuint ix   = absX % FrameDimensions.KBlockDim;

                if (rowSigma[(int)bx] < Epf.KMinSigma)
                {
                    out0[x] = GetInputRow(inputRows, 0, 0)[x];
                    out1[x] = GetInputRow(inputRows, 1, 0)[x];
                    out2[x] = GetInputRow(inputRows, 2, 0)[x];
                    continue;
                }

                float invSigma = rowSigma[(int)bx] * EpfStageHelpers.GetSadMul(ypos, ix, sm, bsm);
                FilterPixel(inputRows, x, invSigma, out out0[x], out out1[x], out out2[x]);
            }

            return true;
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            float invSigma,
            out float outX,
            out float outY,
            out float outB)
        {
            float rx = GetInputRow(inputRows, 0, 0)[x];
            float ry = GetInputRow(inputRows, 1, 0)[x];
            float rb = GetInputRow(inputRows, 2, 0)[x];

            float accW = 1.0f;
            float accX = rx;
            float accY = ry;
            float accB = rb;

            AccumulateNeighbor(inputRows, -1, x, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 0, x - 1, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 0, x + 1, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 1, x, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);

            float invW = 1.0f / accW;
            outX       = accX * invW;
            outY       = accY * invW;
            outB       = accB * invW;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AccumulateNeighbor(
            PipelineRow[][] inputRows,
            int rowOffset,
            nint colX,
            float rx,
            float ry,
            float rb,
            float invSigma,
            ref float accX,
            ref float accY,
            ref float accB,
            ref float accW)
        {
            float cx = GetInputRow(inputRows, 0, rowOffset)[colX];
            float cy = GetInputRow(inputRows, 1, rowOffset)[colX];
            float cb = GetInputRow(inputRows, 2, rowOffset)[colX];

            float sad = (MathF.Abs(cx - rx) * _lf.EpfChannelScale[0]) +
                        (MathF.Abs(cy - ry) * _lf.EpfChannelScale[1]) +
                        (MathF.Abs(cb - rb) * _lf.EpfChannelScale[2]);
            float weight = EpfStageHelpers.WeightScalar(sad, invSigma);

            accW += weight;
            accX += weight * cx;
            accY += weight * cy;
            accB += weight * cb;
        }

        private void FilterPixel(
            PipelineRow[][] inputRows,
            nint x,
            Vector256<float> invSigma,
            out Vector256<float> outX,
            out Vector256<float> outY,
            out Vector256<float> outB)
        {
            Vector256<float> rx = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, 0)[x]);
            Vector256<float> ry = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, 0)[x]);
            Vector256<float> rb = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, 0)[x]);

            Vector256<float> accW = Vector256.Create(1.0f);
            Vector256<float> accX = rx;
            Vector256<float> accY = ry;
            Vector256<float> accB = rb;

            AccumulateNeighbor(inputRows, -1, x, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 0, x - 1, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 0, x + 1, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);
            AccumulateNeighbor(inputRows, 1, x, rx, ry, rb, invSigma, ref accX, ref accY, ref accB, ref accW);

            Vector256<float> invW = Vector256.Create(1.0f) / accW;
            outX       = accX * invW;
            outY       = accY * invW;
            outB       = accB * invW;
        }

        private void AccumulateNeighbor(
            PipelineRow[][] inputRows,
            int rowOffset,
            nint colX,
            Vector256<float> rx,
            Vector256<float> ry,
            Vector256<float> rb,
            Vector256<float> invSigma,
            ref Vector256<float> accX,
            ref Vector256<float> accY,
            ref Vector256<float> accB,
            ref Vector256<float> accW)
        {
            Vector256<float> cx = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 0, rowOffset)[colX]);
            Vector256<float> cy = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 1, rowOffset)[colX]);
            Vector256<float> cb = Vector256.LoadUnsafe(ref GetInputRow(inputRows, 2, rowOffset)[colX]);

            Vector256<float> sad = (Vector256.Abs(cx - rx) * Vector256.Create(_lf.EpfChannelScale[0])) +
                        (Vector256.Abs(cy - ry) * Vector256.Create(_lf.EpfChannelScale[1])) +
                        (Vector256.Abs(cb - rb) * Vector256.Create(_lf.EpfChannelScale[2]));
            Vector256<float> weight = EpfStageHelpers.WeightScalar(sad, invSigma);

            accW += weight;
            accX += weight * cx;
            accY += weight * cy;
            accB += weight * cb;
        }
    }

    /// <summary>
    /// 2x horizontal chroma upsampling stage (<c>[0.75, 0.25]</c> linear filter).
    /// Ported from <c>jxl::HWY_NAMESPACE::HorizontalChromaUpsamplingStage</c> in <c>lib/jxl/render_pipeline/stage_chroma_upsampling.cc</c>.
    /// </summary>
    internal sealed class HorizontalChromaUpsamplingStage : RenderPipelineStage
    {
        private readonly nuint _c;

        public HorizontalChromaUpsamplingStage(nuint channel)
            : base(Settings.CreateShiftX(shift: 1, border: 1))
        {
            _c = channel;
        }

        /// <inheritdoc />
        public override string GetName() => "HChromaUps";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c == _c ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart        = -(nint)Common.RoundUpTo(xextra.Left, 4u);
            nint xEnd          = (nint)(xsize + xextra.Right);
            PipelineRow rowIn  = GetInputRow(inputRows, _c, 0);
            PipelineRow rowOut = GetOutputRow(outputRows, _c, 0);

            for (nint x = xStart; x < xEnd; x++)
            {
                float current       = rowIn[x] * 0.75f;
                float prev          = rowIn[x - 1];
                float next          = rowIn[x + 1];
                rowOut[2 * x]       = (0.25f * prev) + current;
                rowOut[(2 * x) + 1] = (0.25f * next) + current;
            }

            return true;
        }
    }

    /// <summary>
    /// 2x vertical chroma upsampling stage (<c>[0.75, 0.25]</c> linear filter).
    /// Ported from <c>jxl::HWY_NAMESPACE::VerticalChromaUpsamplingStage</c> in <c>lib/jxl/render_pipeline/stage_chroma_upsampling.cc</c>.
    /// </summary>
    internal sealed class VerticalChromaUpsamplingStage : RenderPipelineStage
    {
        private readonly nuint _c;

        public VerticalChromaUpsamplingStage(nuint channel)
            : base(Settings.CreateShiftY(shift: 1, border: 1))
        {
            _c = channel;
        }

        /// <inheritdoc />
        public override string GetName() => "VChromaUps";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c == _c ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart         = -(nint)Common.RoundUpTo(xextra.Left, 4u);
            nint xEnd           = (nint)(xsize + xextra.Right);
            PipelineRow rowTop  = GetInputRow(inputRows, _c, -1);
            PipelineRow rowMid  = GetInputRow(inputRows, _c, 0);
            PipelineRow rowBot  = GetInputRow(inputRows, _c, 1);
            PipelineRow rowOut0 = GetOutputRow(outputRows, _c, 0);
            PipelineRow rowOut1 = GetOutputRow(outputRows, _c, 1);

            for (nint x = xStart; x < xEnd; x++)
            {
                float midScaled = rowMid[x] * 0.75f;
                rowOut0[x]      = (rowTop[x] * 0.25f) + midScaled;
                rowOut1[x]      = (rowBot[x] * 0.25f) + midScaled;
            }

            return true;
        }
    }

    /// <summary>
    /// 2x, 4x, or 8x non-separable 5x5 clamped upsampling stage.
    /// Ported from <c>jxl::HWY_NAMESPACE::UpsamplingStage</c> in <c>lib/jxl/render_pipeline/stage_upsampling.cc</c>.
    /// </summary>
    internal sealed class UpsamplingStage : RenderPipelineStage
    {
        private const int KChunkSize = 1024;

        private readonly nuint _c;
        private readonly float[] _kernel = new float[64 * 25];
        private readonly JxlMemoryManager? _memoryManager;
        private float[][] _mins = Array.Empty<float[]>();
        private float[][] _maxs = Array.Empty<float[]>();

        public UpsamplingStage(
            JxlMemoryManager? memoryManager,
            CustomTransformData upsFactors,
            nuint c,
            nuint shift)
            : base(Settings.Symmetric(shift: shift, border: 2))
        {
            _c             = c;
            _memoryManager = memoryManager;

            float[] weights = shift == 1
                ? upsFactors.Upsampling2Weights
                : (shift == 2 ? upsFactors.Upsampling4Weights : upsFactors.Upsampling8Weights);
            int n = 1 << (int)shift;
            int h = n / 2;

            InitKernel(weights, n, h);
        }

        /// <inheritdoc />
        public override string GetName() => "Upsample";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c == _c ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status PrepareForThreads(nuint numThreads)
        {
            int count  = Math.Max(1, (int)numThreads);
            int length = KChunkSize + 16;
            _mins      = new float[count][];
            _maxs      = new float[count][];

            for (int t = 0; t < count; t++)
            {
                _mins[t] = new float[length];
                _maxs[t] = new float[length];
            }

            return true;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("UpsamplingStage requires zero xextra");
            }

            int n        = 1 << (int)StageSettings.ShiftX;
            int tIdx     = _mins.Length > 0 ? Math.Min((int)threadId, _mins.Length - 1) : 0;
            float[] mins = _mins.Length > 0 ? _mins[tIdx] : new float[KChunkSize + 16];
            float[] maxs = _maxs.Length > 0 ? _maxs[tIdx] : new float[KChunkSize + 16];

            for (nuint x0 = 0; x0 < xsize; x0 += KChunkSize)
            {
                nuint xend = Math.Min(x0 + KChunkSize, xsize);
                nuint len  = xend - x0;
                PreProcessChunk(inputRows, x0, len, mins, maxs);
                ProcessChunk(inputRows, outputRows, x0, len, n, mins, maxs);
            }

            return true;
        }

        private void InitKernel(float[] weights, int n, int h)
        {
            for (int ky = 0; ky < h; ++ky)
            {
                for (int kx = 0; kx < h; ++kx)
                {
                    int offset0 = ((ky * n) + kx) * 25;
                    int offset1 = ((ky * n) + (n - 1 - kx)) * 25;
                    int offset2 = (((n - 1 - ky) * n) + kx) * 25;
                    int offset3 = (((n - 1 - ky) * n) + (n - 1 - kx)) * 25;

                    for (int py = 0; py < 5; ++py)
                    {
                        for (int px = 0; px < 5; ++px)
                        {
                            int j   = (5 * ky) + py;
                            int i   = (5 * kx) + px;
                            int my  = Math.Min(i, j);
                            int mx  = Math.Max(i, j);
                            float w = weights[(5 * h * my) - ((my * (my - 1)) / 2) + mx - my];

                            _kernel[offset0 + (py * 5) + px]             = w;
                            _kernel[offset1 + (py * 5) + (4 - px)]       = w;
                            _kernel[offset2 + ((4 - py) * 5) + px]       = w;
                            _kernel[offset3 + ((4 - py) * 5) + (4 - px)] = w;
                        }
                    }
                }
            }
        }

        private void PreProcessChunk(
            PipelineRow[][] inputRows,
            nuint x0,
            nuint len,
            float[] mins,
            float[] maxs)
        {
            Span<float> colMin = stackalloc float[(int)len + 4];
            Span<float> colMax = stackalloc float[(int)len + 4];
            PipelineRow r0     = GetInputRow(inputRows, _c, -2) + (nint)x0 - 2;
            PipelineRow r1     = GetInputRow(inputRows, _c, -1) + (nint)x0 - 2;
            PipelineRow r2     = GetInputRow(inputRows, _c, 0) + (nint)x0 - 2;
            PipelineRow r3     = GetInputRow(inputRows, _c, 1) + (nint)x0 - 2;
            PipelineRow r4     = GetInputRow(inputRows, _c, 2) + (nint)x0 - 2;

            int vx = 0;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; vx + 8 <= (int)len + 4; vx += 8)
                {
                    Vector256<float> v0  = Vector256.LoadUnsafe(ref r0[vx]);
                    Vector256<float> v1  = Vector256.LoadUnsafe(ref r1[vx]);
                    Vector256<float> v2  = Vector256.LoadUnsafe(ref r2[vx]);
                    Vector256<float> v3  = Vector256.LoadUnsafe(ref r3[vx]);
                    Vector256<float> v4  = Vector256.LoadUnsafe(ref r4[vx]);
                    (Vector256.Min(v4, Vector256.Min(Vector256.Min(v0, v1), Vector256.Min(v2, v3)))).StoreUnsafe(ref colMin[vx]);
                    (Vector256.Max(v4, Vector256.Max(Vector256.Max(v0, v1), Vector256.Max(v2, v3)))).StoreUnsafe(ref colMax[vx]);

                }
            }

            for (int x = vx; x < (int)len + 4; x++)
            {
                float v0  = r0[x];
                float v1  = r1[x];
                float v2  = r2[x];
                float v3  = r3[x];
                float v4  = r4[x];
                colMin[x] = MathF.Min(v4, MathF.Min(MathF.Min(v0, v1), MathF.Min(v2, v3)));
                colMax[x] = MathF.Max(v4, MathF.Max(MathF.Max(v0, v1), MathF.Max(v2, v3)));
            }

            int hx = 0;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; hx + 8 <= (int)len; hx += 8)
                {
                    (Vector256.Min(
                        Vector256.LoadUnsafe(ref colMin[hx + 4]),
                        Vector256.Min(Vector256.Min(Vector256.LoadUnsafe(ref colMin[hx]), Vector256.LoadUnsafe(ref colMin[hx + 1])), Vector256.Min(Vector256.LoadUnsafe(ref colMin[hx + 2]), Vector256.LoadUnsafe(ref colMin[hx + 3]))))).StoreUnsafe(ref mins[hx]);
                    (Vector256.Max(
                        Vector256.LoadUnsafe(ref colMax[hx + 4]),
                        Vector256.Max(Vector256.Max(Vector256.LoadUnsafe(ref colMax[hx]), Vector256.LoadUnsafe(ref colMax[hx + 1])), Vector256.Max(Vector256.LoadUnsafe(ref colMax[hx + 2]), Vector256.LoadUnsafe(ref colMax[hx + 3]))))).StoreUnsafe(ref maxs[hx]);

                }
            }

            for (int x = hx; x < (int)len; x++)
            {
                mins[x] = MathF.Min(
                    colMin[x + 4],
                    MathF.Min(MathF.Min(colMin[x], colMin[x + 1]), MathF.Min(colMin[x + 2], colMin[x + 3])));
                maxs[x] = MathF.Max(
                    colMax[x + 4],
                    MathF.Max(MathF.Max(colMax[x], colMax[x + 1]), MathF.Max(colMax[x + 2], colMax[x + 3])));
            }
        }

        private void ProcessChunk(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            nuint x0,
            nuint len,
            int n,
            float[] mins,
            float[] maxs)
        {
            PipelineRow r0 = GetInputRow(inputRows, _c, -2) + (nint)x0;
            PipelineRow r1 = GetInputRow(inputRows, _c, -1) + (nint)x0;
            PipelineRow r2 = GetInputRow(inputRows, _c, 0) + (nint)x0;
            PipelineRow r3 = GetInputRow(inputRows, _c, 1) + (nint)x0;
            PipelineRow r4 = GetInputRow(inputRows, _c, 2) + (nint)x0;

            Span<float> phases = stackalloc float[8 * 8];
            for (int oy = 0; oy < n; oy++)
            {
                PipelineRow dstRow = GetOutputRow(outputRows, _c, (nuint)oy) + ((nint)x0 * n);

                int vectorEnd = 0;
                if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                {
                    for (; vectorEnd + 8 <= (int)len; vectorEnd += 8)
                    {
                        Vector256<float> minVal = Vector256.LoadUnsafe(ref mins[vectorEnd]);
                        Vector256<float> maxVal = Vector256.LoadUnsafe(ref maxs[vectorEnd]);
                        for (int ox = 0; ox < n; ox++)
                        {
                            int kBase = ((n * oy) + ox) * 25;
                            Vector256<float> sum = Convolve5x5Vector(r0, r1, r2, r3, r4, vectorEnd, kBase);
                            sum = Vector256.ConditionalSelect(Vector256.LessThan(sum, minVal), minVal, sum);
                            sum = Vector256.ConditionalSelect(Vector256.GreaterThan(sum, maxVal), maxVal, sum);
                            sum.StoreUnsafe(ref phases[ox * 8]);
                        }
                        for (int lane = 0; lane < 8; lane++)
                            for (int ox = 0; ox < n; ox++)
                                dstRow[(vectorEnd + lane) * n + ox] = phases[ox * 8 + lane];
                    }
                }

                for (int x = vectorEnd; x < (int)len; x++)
                {
                    float minVal = mins[x];
                    float maxVal = maxs[x];

                    for (int ox = 0; ox < n; ox++)
                    {
                        int kBase = ((n * oy) + ox) * 25;
                        float sum = Convolve5x5(r0, r1, r2, r3, r4, x, kBase);
                        dstRow[(x * n) + ox] = Math.Clamp(sum, minVal, maxVal);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Convolve5x5(
            PipelineRow r0,
            PipelineRow r1,
            PipelineRow r2,
            PipelineRow r3,
            PipelineRow r4,
            int x,
            int kBase)
        {
            float sum = 0.0f;

            for (int ix = -2; ix <= 2; ix++)
            {
                int col = ix + 2;
                sum    += r0[x + ix] * _kernel[kBase + col];
                sum    += r1[x + ix] * _kernel[kBase + 5 + col];
                sum    += r2[x + ix] * _kernel[kBase + 10 + col];
                sum    += r3[x + ix] * _kernel[kBase + 15 + col];
                sum    += r4[x + ix] * _kernel[kBase + 20 + col];
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector256<float> Convolve5x5Vector(
            PipelineRow r0,
            PipelineRow r1,
            PipelineRow r2,
            PipelineRow r3,
            PipelineRow r4,
            int x,
            int kBase)
        {
            Vector256<float> sum = Vector256<float>.Zero;

            for (int ix = -2; ix <= 2; ix++)
            {
                int col = ix + 2;
                sum    += Vector256.LoadUnsafe(ref r0[x + ix]) * Vector256.Create(_kernel[kBase + col]);
                sum    += Vector256.LoadUnsafe(ref r1[x + ix]) * Vector256.Create(_kernel[kBase + 5 + col]);
                sum    += Vector256.LoadUnsafe(ref r2[x + ix]) * Vector256.Create(_kernel[kBase + 10 + col]);
                sum    += Vector256.LoadUnsafe(ref r3[x + ix]) * Vector256.Create(_kernel[kBase + 15 + col]);
                sum    += Vector256.LoadUnsafe(ref r4[x + ix]) * Vector256.Create(_kernel[kBase + 20 + col]);
            }

            return sum;
        }
    }

    /// <summary>
    /// Renders patches from a <see cref="PatchDictionary"/> into all color and extra channels.
    /// Ported from <c>jxl::PatchDictionaryStage</c> in <c>lib/jxl/render_pipeline/stage_patches.cc</c>.
    /// </summary>
    internal sealed class PatchDictionaryStage : RenderPipelineStage
    {
        private readonly PatchDictionary _patches;
        private readonly IReadOnlyList<ExtraChannelInfo> _extraChannelInfo;

        public PatchDictionaryStage(
            PatchDictionary patches,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
            : base(new Settings())
        {
            _patches          = patches;
            _extraChannelInfo = extraChannelInfo;
        }

        /// <inheritdoc />
        public override string GetName() => "Patches";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            nuint numChannels = 3 + (nuint)_extraChannelInfo.Count;

            return c < numChannels
                ? RenderPipelineChannelMode.InPlace
                : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            int numChannels       = 3 + _extraChannelInfo.Count;
            PipelineRow[] rowPtrs = new PipelineRow[numChannels];

            for (int i = 0; i < numChannels; i++)
            {
                rowPtrs[i] = GetInputRow(inputRows, (nuint)i, 0) - (nint)xextra.Left;
            }

            return _patches.AddOneRow(
                rowPtrs,
                ypos,
                xpos - xextra.Left,
                xsize + xextra.Left + xextra.Right,
                _extraChannelInfo);
        }
    }

    /// <summary>
    /// Adds rendered splines from <see cref="Splines"/> to the XYB channels.
    /// Ported from <c>jxl::HWY_NAMESPACE::SplineStage</c> in <c>lib/jxl/render_pipeline/stage_splines.cc</c>.
    /// </summary>
    internal sealed class SplineStage : RenderPipelineStage
    {
        private readonly Splines _splines;

        public SplineStage(Splines splines)
            : base(new Settings())
        {
            _splines = splines;
        }

        /// <inheritdoc />
        public override string GetName() => "Splines";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            PipelineRow rowX = GetInputRow(inputRows, 0, 0) - (nint)xextra.Left;
            PipelineRow rowY = GetInputRow(inputRows, 1, 0) - (nint)xextra.Left;
            PipelineRow rowB = GetInputRow(inputRows, 2, 0) - (nint)xextra.Left;

            _splines.AddToRow(
                rowX.AsSpan(),
                rowY.AsSpan(),
                rowB.AsSpan(),
                ypos,
                xpos - xextra.Left,
                xpos + xsize + xextra.Right);

            return true;
        }
    }

    /// <summary>
    /// Adds synthesized and convolved noise to the XYB channels.
    /// Ported from <c>jxl::HWY_NAMESPACE::AddNoiseStage</c> in <c>lib/jxl/render_pipeline/stage_noise.cc</c>.
    /// </summary>
    internal sealed class AddNoiseStage : RenderPipelineStage
    {
        private const float KNormConst = 0.22f;
        private const float KRGCorr    = 0.9921875f; // 127/128
        private const float KRGNCorr   = 0.0078125f; // 1/128

        private readonly NoiseParams _noiseParams;
        private readonly ColorCorrelation _colorCorrelation;
        private readonly nuint _firstC;

        public AddNoiseStage(
            NoiseParams noiseParams,
            ColorCorrelation colorCorrelation,
            nuint firstC)
            : base(new Settings())
        {
            _noiseParams      = noiseParams;
            _colorCorrelation = colorCorrelation;
            _firstC           = firstC;
        }

        /// <inheritdoc />
        public override string GetName() => "AddNoise";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            if (c >= _firstC)
            {
                return RenderPipelineChannelMode.Input;
            }

            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (!_noiseParams.HasAny())
            {
                return true;
            }

            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("AddNoiseStage requires zero xextra");
            }

            float ytox          = _colorCorrelation.YtoXRatio(0);
            float ytob          = _colorCorrelation.YtoBRatio(0);
            PipelineRow rowX    = GetInputRow(inputRows, 0, 0);
            PipelineRow rowY    = GetInputRow(inputRows, 1, 0);
            PipelineRow rowB    = GetInputRow(inputRows, 2, 0);
            PipelineRow rowRndR = GetInputRow(inputRows, _firstC + 0, 0);
            PipelineRow rowRndG = GetInputRow(inputRows, _firstC + 1, 0);
            PipelineRow rowRndC = GetInputRow(inputRows, _firstC + 2, 0);

            for (nuint x = 0; x < xsize; x++)
            {
                float vx       = rowX[x];
                float vy       = rowY[x];
                float inG      = (vy - vx) * 0.5f;
                float inR      = (vy + vx) * 0.5f;
                float nsG      = EvalNoiseStrength(inG);
                float nsR      = EvalNoiseStrength(inR);
                float rndR     = rowRndR[x] * KNormConst;
                float rndG     = rowRndG[x] * KNormConst;
                float rndC     = rowRndC[x] * KNormConst;
                float redNoise = nsR * ((KRGNCorr * rndR) + (KRGCorr * rndC));
                float grnNoise = nsG * ((KRGNCorr * rndG) + (KRGCorr * rndC));
                float rgNoise  = redNoise + grnNoise;

                rowX[x] = (ytox * rgNoise) + (redNoise - grnNoise) + vx;
                rowY[x] = vy + rgNoise;
                rowB[x] = (ytob * rgNoise) + rowB[x];
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float EvalNoiseStrength(float x)
        {
            const float kScale = NoiseParams.KNumNoisePoints - 2;
            float scaledX      = MathF.Max(0.0f, x * kScale);
            float floorX       = MathF.Floor(scaledX);
            float fracX        = scaledX - floorX;

            if (scaledX >= kScale + 1.0f)
            {
                floorX = kScale;
                fracX  = 1.0f;
            }

            int idx   = (int)floorX;
            float low = _noiseParams.Lut[idx];
            float hi  = _noiseParams.Lut[idx + 1];
            float val = ((hi - low) * fracX) + low;

            return Math.Clamp(val, 0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Applies a 5x5 subtract-box-filter convolution to the 3 temporary noise channels.
    /// Ported from <c>jxl::HWY_NAMESPACE::ConvolveNoiseStage</c> in <c>lib/jxl/render_pipeline/stage_noise.cc</c>.
    /// </summary>
    internal sealed class ConvolveNoiseStage : RenderPipelineStage
    {
        private readonly nuint _firstC;

        public ConvolveNoiseStage(nuint firstC)
            : base(Settings.SymmetricBorderOnly(2))
        {
            _firstC = firstC;
        }

        /// <inheritdoc />
        public override string GetName() => "ConvNoise";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c >= _firstC ? RenderPipelineChannelMode.InOut : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            nint xStart = -(nint)xextra.Left;
            nint xEnd   = (nint)(xsize + xextra.Right);

            for (nuint c = _firstC; c < _firstC + 3; c++)
            {
                PipelineRow r0     = GetInputRow(inputRows, c, -2);
                PipelineRow r1     = GetInputRow(inputRows, c, -1);
                PipelineRow r2     = GetInputRow(inputRows, c, 0);
                PipelineRow r3     = GetInputRow(inputRows, c, 1);
                PipelineRow r4     = GetInputRow(inputRows, c, 2);
                PipelineRow rowOut = GetOutputRow(outputRows, c, 0);

                for (nint x = xStart; x < xEnd; x++)
                {
                    float p00    = r2[x];
                    float others = 0.0f;

                    for (nint i = -2; i <= 2; i++)
                    {
                        others += r0[x + i] + r1[x + i] + r3[x + i] + r4[x + i];
                    }

                    others   += r2[x - 2] + r2[x - 1] + r2[x + 1] + r2[x + 2];
                    rowOut[x] = (others * 0.16f) + (p00 * -3.84f);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Converts full-range BT.601 YCbCr channels (Cb in channel 0, Y in channel 1, Cr in channel 2) to RGB in-place.
    /// Ported from <c>jxl::HWY_NAMESPACE::kYCbCrStage</c> in <c>lib/jxl/render_pipeline/stage_ycbcr.cc</c>.
    /// </summary>
    internal sealed class YCbCrStage : RenderPipelineStage
    {
        public YCbCrStage()
        : base(new Settings())
        {
        }

        /// <inheritdoc />
        public override string GetName() => "YCbCr";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("YCbCrStage requires zero xextra");
            }

            const float c128 = 128.0f / 255.0f;
            const float crcr = 1.402f;
            const float cgcb = -0.114f * 1.772f / 0.587f;
            const float cgcr = -0.299f * 1.402f / 0.587f;
            const float cbcb = 1.772f;

            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);

            nuint x = 0;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            {
                for (; x + 8 <= xsize; x += 8)
                {
                    Vector256<float> yVal = Vector256.LoadUnsafe(ref row1[x]) + Vector256.Create(c128);
                    Vector256<float> cbVal = Vector256.LoadUnsafe(ref row0[x]);
                    Vector256<float> crVal = Vector256.LoadUnsafe(ref row2[x]);
                    ((Vector256.Create(crcr) * crVal) + yVal).StoreUnsafe(ref row0[x]);
                    ((Vector256.Create(cgcr) * crVal) + (Vector256.Create(cgcb) * cbVal) + yVal).StoreUnsafe(ref row1[x]);
                    ((Vector256.Create(cbcb) * cbVal) + yVal).StoreUnsafe(ref row2[x]);

                }
            }

            for (; x < xsize; x++)
            {
                float yVal  = row1[x] + c128;
                float cbVal = row0[x];
                float crVal = row2[x];
                row0[x]     = (crcr * crVal) + yVal;
                row1[x]     = (cgcr * crVal) + (cgcb * cbVal) + yVal;
                row2[x]     = (cbcb * cbVal) + yVal;
            }

            return true;
        }
    }

    /// <summary>
    /// Converts XYB channels to linear RGB (or scaled XYB when the output color space is XYB).
    /// Ported from <c>jxl::HWY_NAMESPACE::XYBStage</c> in <c>lib/jxl/render_pipeline/stage_xyb.cc</c>.
    /// </summary>
    internal sealed class XybStage : RenderPipelineStage
    {
        private readonly OpsinParams _opsinParams;
        private readonly bool _outputIsXyb;

        public XybStage(OutputEncodingInfo outputEncodingInfo)
        : base(new Settings())
        {
            _opsinParams = outputEncodingInfo.OpsinParams;
            _outputIsXyb = outputEncodingInfo.ColorEncoding.GetColorSpace() == ColorSpace.XYB;
        }

        /// <inheritdoc />
        public override string GetName() => "XYB";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("XybStage requires zero xextra");
            }

            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);

            if (_outputIsXyb)
            {
                float scaleX    = Cms.OpsinParams.KScaledXybScale0;
                float scaleY    = Cms.OpsinParams.KScaledXybScale1;
                float scaleBmy  = Cms.OpsinParams.KScaledXybScale2;
                float offsetX   = Cms.OpsinParams.KScaledXybOffset0;
                float offsetY   = Cms.OpsinParams.KScaledXybOffset1;
                float offsetBmy = Cms.OpsinParams.KScaledXybOffset2;

                nuint x = 0;
                if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                {
                    for (; x + 8 <= xsize; x += 8)
                    {
                        Vector256<float> inX = Vector256.LoadUnsafe(ref row0[x]);
                        Vector256<float> inY = Vector256.LoadUnsafe(ref row1[x]);
                        Vector256<float> inB = Vector256.LoadUnsafe(ref row2[x]);
                        ((inX + Vector256.Create(offsetX)) * Vector256.Create(scaleX)).StoreUnsafe(ref row0[x]);
                        ((inY + Vector256.Create(offsetY)) * Vector256.Create(scaleY)).StoreUnsafe(ref row1[x]);
                        (((inB - inY) + Vector256.Create(offsetBmy)) * Vector256.Create(scaleBmy)).StoreUnsafe(ref row2[x]);

                    }
                }

                for (; x < xsize; x++)
                {
                    float inX = row0[x];
                    float inY = row1[x];
                    float inB = row2[x];
                    row0[x]   = (inX + offsetX) * scaleX;
                    row1[x]   = (inY + offsetY) * scaleY;
                    row2[x]   = ((inB - inY) + offsetBmy) * scaleBmy;
                }
            }
            else
            {
                nuint x = 0;

                if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                {
                    for (; x + 8 <= xsize; x += 8)
                    {
                        Vector256<float> inX = Vector256.LoadUnsafe(ref row0[x]);
                        Vector256<float> inY = Vector256.LoadUnsafe(ref row1[x]);
                        Vector256<float> inB = Vector256.LoadUnsafe(ref row2[x]);
                        DecXyb.XybToRgb(
                            inX,
                            inY,
                            inB,
                            _opsinParams,
                            out Vector256<float> r,
                            out Vector256<float> g,
                            out Vector256<float> b);
                        r.StoreUnsafe(ref row0[x]);
                        g.StoreUnsafe(ref row1[x]);
                        b.StoreUnsafe(ref row2[x]);
                    }

                }

                for (; x + 4 <= xsize; x += 4)
                {
                    Vector128<float> inX = Vector128.LoadUnsafe(ref row0[x]);
                    Vector128<float> inY = Vector128.LoadUnsafe(ref row1[x]);
                    Vector128<float> inB = Vector128.LoadUnsafe(ref row2[x]);
                    DecXyb.XybToRgb(
                        inX,
                        inY,
                        inB,
                        _opsinParams,
                        out Vector128<float> r,
                        out Vector128<float> g,
                        out Vector128<float> b);
                    r.StoreUnsafe(ref row0[x]);
                    g.StoreUnsafe(ref row1[x]);
                    b.StoreUnsafe(ref row2[x]);
                }

                for (; x < xsize; x++)
                {
                    DecXyb.XybToRgbScalar(
                        row0[x],
                        row1[x],
                        row2[x],
                        _opsinParams,
                        out row0[x],
                        out row1[x],
                        out row2[x]);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Fast XYB-to-sRGB8 direct output stage.
    /// Ported from <c>jxl::FastXYBStage</c> in <c>lib/jxl/render_pipeline/stage_xyb.cc</c>.
    /// </summary>
    internal sealed class FastXybStage : RenderPipelineStage
    {
        private readonly Memory<byte> _rgb;
        private readonly nuint _stride;
        private readonly nuint _width;
        private readonly nuint _height;
        private readonly bool _rgba;
        private readonly bool _hasAlpha;
        private readonly nuint _alphaC;

        public FastXybStage(
            Memory<byte> rgb,
            nuint stride,
            nuint width,
            nuint height,
            bool rgba,
            bool hasAlpha,
            nuint alphaC)
            : base(new Settings())
        {
            _rgb      = rgb;
            _stride   = stride;
            _width    = width;
            _height   = height;
            _rgba     = rgba;
            _hasAlpha = hasAlpha;
            _alphaC   = alphaC;
        }

        /// <inheritdoc />
        public override string GetName() => "FastXYB";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 || (_hasAlpha && c == _alphaC)
                ? RenderPipelineChannelMode.Input
                : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (ypos >= _height)
            {
                return true;
            }

            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("FastXybStage requires zero xextra");
            }

            nuint xSpan             = Math.Min(xsize, _width - xpos);
            ReadOnlySpan<float> rowX = GetInputRow(inputRows, 0, 0).AsSpan(0, (int)xSpan);
            ReadOnlySpan<float> rowY = GetInputRow(inputRows, 1, 0).AsSpan(0, (int)xSpan);
            ReadOnlySpan<float> rowB = GetInputRow(inputRows, 2, 0).AsSpan(0, (int)xSpan);
            ReadOnlySpan<float> rowA = _hasAlpha ? GetInputRow(inputRows, _alphaC, 0).AsSpan(0, (int)xSpan) : ReadOnlySpan<float>.Empty;
            int outOffset           = (int)((_stride * ypos) + ((_rgba ? 4u : 3u) * xpos));
            Span<byte> outBuf       = _rgb.Span.Slice(outOffset);

            return DecXyb.FastXYBTosRGB8(rowX, rowY, rowB, rowA, outBuf, _rgba, xSpan);
        }
    }

    /// <summary>
    /// Composites a spot color extra channel onto the RGB channels in-place.
    /// Ported from <c>jxl::SpotColorStage</c> in <c>lib/jxl/render_pipeline/stage_spot.cc</c>.
    /// </summary>
    internal sealed class SpotColorStage : RenderPipelineStage
    {
        private readonly nuint _spotC;
        private readonly float[] _spotColor;

        public SpotColorStage(nuint spotCOffset, float[] spotColor)
            : base(new Settings())
        {
            _spotC     = 3 + spotCOffset;
            _spotColor = spotColor;
        }

        /// <inheritdoc />
        public override string GetName() => "Spot";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            if (c < 3)
            {
                return RenderPipelineChannelMode.InPlace;
            }

            return c == _spotC ? RenderPipelineChannelMode.Input : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("SpotColorStage requires zero xextra");
            }

            float scale   = _spotColor[3];
            PipelineRow s = GetInputRow(inputRows, _spotC, 0);

            for (nuint c = 0; c < 3; c++)
            {
                PipelineRow p = GetInputRow(inputRows, c, 0);
                float spotVal = _spotColor[c];

                for (nuint x = 0; x < xsize; x++)
                {
                    float mix = scale * s[x];
                    p[x]      = (mix * spotVal) + ((1.0f - mix) * p[x]);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Converts channels 0..2 from <see cref="OutputEncodingInfo.ColorEncoding"/>'s transfer function to linear light.
    /// Ported from <c>jxl::HWY_NAMESPACE::ToLinearStage</c> in <c>lib/jxl/render_pipeline/stage_to_linear.cc</c>.
    /// </summary>
    internal sealed class ToLinearStage : RenderPipelineStage
    {
        private readonly OutputEncodingInfo _info;
        private readonly TfSrgb _tfSrgb = new TfSrgb();
        private readonly TfPq _tfPq;
        private readonly TfHlg _tfHlg = new TfHlg();
        private readonly Tf709 _tf709 = new Tf709();
        private readonly HlgOotf _hlgOotf;
        private readonly float _gamma;
        private readonly bool _valid;

        public ToLinearStage(OutputEncodingInfo outputEncodingInfo)
            : base(new Settings())
        {
            _info    = outputEncodingInfo;
            _tfPq    = new TfPq(outputEncodingInfo.OrigIntensityTarget);
            _hlgOotf = HlgOotf.FromSceneLight(outputEncodingInfo.OrigIntensityTarget, outputEncodingInfo.Luminances);
            _gamma   = outputEncodingInfo.InverseGamma != 0.0f ? 1.0f / outputEncodingInfo.InverseGamma : 1.0f;

            ref CustomTransferFunction tf = ref outputEncodingInfo.ColorEncoding.Tf;
            _valid = tf.IsLinear() ||
                     tf.IsSRGB() ||
                     tf.IsPQ() ||
                     tf.IsHLG() ||
                     tf.Is709() ||
                     tf.HaveGamma ||
                     tf.IsDCI();
        }

        /// <inheritdoc />
        public override string GetName() => "ToLinear";

        /// <inheritdoc />
        public override Status IsInitialized() => _valid;

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("ToLinearStage requires zero xextra");
            }

            ref CustomTransferFunction tf = ref _info.ColorEncoding.Tf;

            if (tf.IsLinear())
            {
                return true;
            }

            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);

            nuint x = 0;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && (tf.IsSRGB() || tf.IsPQ() || tf.IsHLG() || tf.Is709()))
            {
                for (; x + 8 <= xsize; x += 8)
                {
                    Vector256<float> r = Vector256.LoadUnsafe(ref row0[x]);
                    Vector256<float> g = Vector256.LoadUnsafe(ref row1[x]);
                    Vector256<float> b = Vector256.LoadUnsafe(ref row2[x]);
                    if (tf.IsSRGB())
                    {
                        r = _tfSrgb.DisplayFromEncoded(r);
                        g = _tfSrgb.DisplayFromEncoded(g);
                        b = _tfSrgb.DisplayFromEncoded(b);
                    }
                    else if (tf.IsPQ())
                    {
                        r = _tfPq.DisplayFromEncoded(r);
                        g = _tfPq.DisplayFromEncoded(g);
                        b = _tfPq.DisplayFromEncoded(b);
                    }
                    else if (tf.Is709())
                    {
                        r = _tf709.DisplayFromEncoded(r);
                        g = _tf709.DisplayFromEncoded(g);
                        b = _tf709.DisplayFromEncoded(b);
                    }
                    else
                    {
                        r = _tfHlg.DisplayFromEncoded(r);
                        g = _tfHlg.DisplayFromEncoded(g);
                        b = _tfHlg.DisplayFromEncoded(b);
                        _hlgOotf.Apply(ref r, ref g, ref b);
                    }
                    r.StoreUnsafe(ref row0[x]);
                    g.StoreUnsafe(ref row1[x]);
                    b.StoreUnsafe(ref row2[x]);
                }
            }

            for (; x < xsize; x++)
            {
                TransformPixel(ref tf, ref row0[x], ref row1[x], ref row2[x]);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformPixel(
            ref CustomTransferFunction tf,
            ref float r,
            ref float g,
            ref float b)
        {
            if (tf.IsSRGB())
            {
                r = _tfSrgb.DisplayFromEncoded(r);
                g = _tfSrgb.DisplayFromEncoded(g);
                b = _tfSrgb.DisplayFromEncoded(b);
            }
            else if (tf.IsPQ())
            {
                r = _tfPq.DisplayFromEncoded(r);
                g = _tfPq.DisplayFromEncoded(g);
                b = _tfPq.DisplayFromEncoded(b);
            }
            else if (tf.IsHLG())
            {
                Vector3 rgb = new Vector3(
                    _tfHlg.DisplayFromEncoded(r),
                    _tfHlg.DisplayFromEncoded(g),
                    _tfHlg.DisplayFromEncoded(b));
                _hlgOotf.Apply(ref rgb);
                r = rgb[0];
                g = rgb[1];
                b = rgb[2];
            }
            else if (tf.Is709())
            {
                r = _tf709.DisplayFromEncoded(r);
                g = _tf709.DisplayFromEncoded(g);
                b = _tf709.DisplayFromEncoded(b);
            }
            else if (tf.HaveGamma || tf.IsDCI())
            {
                r = r <= 1e-5f ? 0.0f : MathF.Pow(r, _gamma);
                g = g <= 1e-5f ? 0.0f : MathF.Pow(g, _gamma);
                b = b <= 1e-5f ? 0.0f : MathF.Pow(b, _gamma);
            }
        }
    }

    /// <summary>
    /// Converts channels 0..2 from linear light to <see cref="OutputEncodingInfo.ColorEncoding"/>'s transfer function.
    /// Ported from <c>jxl::HWY_NAMESPACE::FromLinearStage</c> in <c>lib/jxl/render_pipeline/stage_from_linear.cc</c>.
    /// </summary>
    internal sealed class FromLinearStage : RenderPipelineStage
    {
        private readonly OutputEncodingInfo _info;
        private readonly TfPq _tfPq;
        private readonly TfHlg _tfHlg = new TfHlg();
        private readonly Tf709 _tf709 = new Tf709();
        private readonly HlgOotf _hlgOotf;
        private readonly float _inverseGamma;

        public FromLinearStage(OutputEncodingInfo outputEncodingInfo)
            : base(new Settings())
        {
            _info         = outputEncodingInfo;
            _tfPq         = new TfPq(outputEncodingInfo.OrigIntensityTarget);
            _hlgOotf      = HlgOotf.ToSceneLight(outputEncodingInfo.DesiredIntensityTarget, outputEncodingInfo.Luminances);
            _inverseGamma = outputEncodingInfo.InverseGamma;
        }

        /// <inheritdoc />
        public override string GetName() => "FromLinear";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("FromLinearStage requires zero xextra");
            }

            ref CustomTransferFunction tf = ref _info.ColorEncoding.Tf;

            if (tf.IsLinear())
            {
                return true;
            }

            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);

            nuint x = 0;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && (tf.IsSRGB() || tf.IsPQ() || tf.IsHLG() || tf.Is709()))
            {
                for (; x + 8 <= xsize; x += 8)
                {
                    Vector256<float> r = Vector256.LoadUnsafe(ref row0[x]);
                    Vector256<float> g = Vector256.LoadUnsafe(ref row1[x]);
                    Vector256<float> b = Vector256.LoadUnsafe(ref row2[x]);
                    if (tf.IsSRGB())
                    {
                        r = TfSrgb.FastLinearToSrgb(r);
                        g = TfSrgb.FastLinearToSrgb(g);
                        b = TfSrgb.FastLinearToSrgb(b);
                    }
                    else if (tf.IsPQ())
                    {
                        r = _tfPq.EncodedFromDisplay(r);
                        g = _tfPq.EncodedFromDisplay(g);
                        b = _tfPq.EncodedFromDisplay(b);
                    }
                    else if (tf.Is709())
                    {
                        r = _tf709.EncodedFromDisplay(r);
                        g = _tf709.EncodedFromDisplay(g);
                        b = _tf709.EncodedFromDisplay(b);
                    }
                    else
                    {
                        _hlgOotf.Apply(ref r, ref g, ref b);
                        r = _tfHlg.EncodedFromDisplay(r);
                        g = _tfHlg.EncodedFromDisplay(g);
                        b = _tfHlg.EncodedFromDisplay(b);
                    }
                    r.StoreUnsafe(ref row0[x]);
                    g.StoreUnsafe(ref row1[x]);
                    b.StoreUnsafe(ref row2[x]);
                }
            }

            for (; x < xsize; x++)
            {
                TransformPixel(ref tf, ref row0[x], ref row1[x], ref row2[x]);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformPixel(
            ref CustomTransferFunction tf,
            ref float r,
            ref float g,
            ref float b)
        {
            if (tf.IsSRGB())
            {
                r = TfSrgb.FastLinearToSrgb(r);
                g = TfSrgb.FastLinearToSrgb(g);
                b = TfSrgb.FastLinearToSrgb(b);
            }
            else if (tf.IsPQ())
            {
                r = _tfPq.EncodedFromDisplay(r);
                g = _tfPq.EncodedFromDisplay(g);
                b = _tfPq.EncodedFromDisplay(b);
            }
            else if (tf.IsHLG())
            {
                Vector3 rgb = new Vector3(r, g, b);
                _hlgOotf.Apply(ref rgb);
                r = _tfHlg.EncodedFromDisplay(rgb[0]);
                g = _tfHlg.EncodedFromDisplay(rgb[1]);
                b = _tfHlg.EncodedFromDisplay(rgb[2]);
            }
            else if (tf.Is709())
            {
                r = _tf709.EncodedFromDisplay(r);
                g = _tf709.EncodedFromDisplay(g);
                b = _tf709.EncodedFromDisplay(b);
            }
            else if (tf.HaveGamma || tf.IsDCI())
            {
                r = r <= 1e-5f ? 0.0f : MathF.Pow(r, _inverseGamma);
                g = g <= 1e-5f ? 0.0f : MathF.Pow(g, _inverseGamma);
                b = b <= 1e-5f ? 0.0f : MathF.Pow(b, _inverseGamma);
            }
        }
    }

    /// <summary>
    /// Applies Rec. 2408 tone mapping or HLG OOTF and gamut mapping to linear RGB channels.
    /// Ported from <c>jxl::HWY_NAMESPACE::ToneMappingStage</c> in <c>lib/jxl/render_pipeline/stage_tone_mapping.cc</c>.
    /// </summary>
    internal sealed class ToneMappingStage : RenderPipelineStage
    {
        private readonly OutputEncodingInfo _outputEncodingInfo;
        private readonly Rec2408ToneMapper? _toneMapper;
        private readonly HlgOotf? _hlgOotf;
        private readonly float _toIntensityTarget = 1.0f;
        private readonly float _fromDesiredIntensityTarget = 1.0f;

        public ToneMappingStage(OutputEncodingInfo outputEncodingInfo)
            : base(new Settings())
        {
            _outputEncodingInfo = outputEncodingInfo;

            if (_outputEncodingInfo.DesiredIntensityTarget == _outputEncodingInfo.OrigIntensityTarget)
            {
                return;
            }

            ref CustomTransferFunction origTf = ref _outputEncodingInfo.OrigColorEncoding.Tf;
            ref CustomTransferFunction destTf = ref _outputEncodingInfo.ColorEncoding.Tf;

            if (origTf.IsPQ() && _outputEncodingInfo.DesiredIntensityTarget < _outputEncodingInfo.OrigIntensityTarget)
            {
                _toneMapper = new Rec2408ToneMapper(
                    0.0f,
                    _outputEncodingInfo.OrigIntensityTarget,
                    0.0f,
                    _outputEncodingInfo.DesiredIntensityTarget,
                    _outputEncodingInfo.Luminances);
            }
            else if (origTf.IsHLG() && !destTf.IsHLG())
            {
                _hlgOotf = new HlgOotf(
                    _outputEncodingInfo.OrigIntensityTarget,
                    _outputEncodingInfo.DesiredIntensityTarget,
                    _outputEncodingInfo.Luminances);
            }

            if (destTf.IsPQ() && (_toneMapper != null || _hlgOotf != null))
            {
                _toIntensityTarget          = 10000.0f / _outputEncodingInfo.OrigIntensityTarget;
                _fromDesiredIntensityTarget = _outputEncodingInfo.DesiredIntensityTarget / 10000.0f;
            }
        }

        public bool IsNeeded => _toneMapper != null || _hlgOotf != null;

        /// <inheritdoc />
        public override string GetName() => "ToneMapping";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            return c < 3 ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (!IsNeeded)
            {
                return true;
            }

            if (xextra.Left != 0 || xextra.Right != 0)
            {
                return Status.Failure("ToneMappingStage requires zero xextra");
            }

            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);
            bool doGamutMap  = _toneMapper != null || (_hlgOotf != null && _hlgOotf.WarrantsGamutMapping);

            for (nuint x = 0; x < xsize; x++)
            {
                Vector3 rgb = new Vector3(
                    row0[x] * _toIntensityTarget,
                    row1[x] * _toIntensityTarget,
                    row2[x] * _toIntensityTarget);

                if (_toneMapper != null)
                {
                    _toneMapper.ToneMap(ref rgb);
                }
                else
                {
                    _hlgOotf!.Apply(ref rgb);
                }

                if (doGamutMap)
                {
                    Cms.ToneMapping.GamutMapScalar(ref rgb, _outputEncodingInfo.Luminances);
                }

                row0[x] = rgb[0] * _fromDesiredIntensityTarget;
                row1[x] = rgb[1] * _fromDesiredIntensityTarget;
                row2[x] = rgb[2] * _fromDesiredIntensityTarget;
            }

            return true;
        }
    }

    /// <summary>
    /// Executes an ICC color space conversion via <see cref="ColorSpaceTransform"/>.
    /// Ported from <c>jxl::HWY_NAMESPACE::CmsStage</c> in <c>lib/jxl/render_pipeline/stage_cms.cc</c>.
    /// </summary>
    internal sealed class CmsStage : RenderPipelineStage
    {
        private readonly OutputEncodingInfo _outputEncodingInfo;
        private readonly ColorEncoding _cSrc;
        private nuint _xsize;
        private ColorSpaceTransform? _colorSpaceTransform;

        public CmsStage(OutputEncodingInfo outputEncodingInfo, bool linear)
            : base(new Settings())
        {
            _outputEncodingInfo = outputEncodingInfo;
            _cSrc               = linear
                ? outputEncodingInfo.LinearColorEncoding
                : outputEncodingInfo.OrigColorEncoding;
        }

        public bool IsNeeded
        {
            get
            {
                nuint channelsSrc          = _cSrc.IsCMYK() ? 4u : _cSrc.Channels();
                nuint channelsDst          = _outputEncodingInfo.ColorEncoding.Channels();
                bool notMixingColorAndGrey = channelsSrc == channelsDst || (channelsSrc == 4 && channelsDst == 3);

                return _outputEncodingInfo.CmsSet &&
                       !_cSrc.SameColorEncoding(_outputEncodingInfo.ColorEncoding) &&
                       notMixingColorAndGrey;
            }
        }

        /// <inheritdoc />
        public override string GetName() => "Cms";

        /// <inheritdoc />
        public override RenderPipelineChannelMode GetChannelMode(nuint c)
        {
            nuint limit = _cSrc.IsCMYK() ? 4u : 3u;

            return c < limit ? RenderPipelineChannelMode.InPlace : RenderPipelineChannelMode.Ignored;
        }

        /// <inheritdoc />
        public override Status SetInputSizes(IReadOnlyList<(nuint XSize, nuint YSize)> inputSizes)
        {
            if (inputSizes.Count < 3)
            {
                return Status.Failure("CmsStage requires at least 3 channels");
            }

            for (int c = 1; c < inputSizes.Count; c++)
            {
                if (inputSizes[c].XSize != inputSizes[0].XSize ||
                    inputSizes[c].YSize != inputSizes[0].YSize)
                {
                    return Status.Failure("CmsStage channel size mismatch");
                }
            }

            _xsize = inputSizes[0].XSize;

            return true;
        }

        /// <inheritdoc />
        public override Status PrepareForThreads(nuint numThreads)
        {
            _colorSpaceTransform?.Dispose();
            _colorSpaceTransform = new ColorSpaceTransform(_outputEncodingInfo.ColorManagementSystem);

            return _colorSpaceTransform.Init(
                _cSrc,
                _outputEncodingInfo.ColorEncoding,
                _outputEncodingInfo.DesiredIntensityTarget,
                _xsize,
                numThreads);
        }

        /// <inheritdoc />
        public override Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId)
        {
            if (xextra.Left != 0 || xextra.Right != 0 || xsize > _xsize || _colorSpaceTransform == null)
            {
                return Status.Failure("CmsStage invalid row parameters");
            }

            float[]? mutableBufSrc = _colorSpaceTransform.BufSrc(threadId);
            float[]? bufDst        = _colorSpaceTransform.BufDst(threadId);

            if (mutableBufSrc == null || bufDst == null)
            {
                return Status.Failure("CmsStage missing thread buffers");
            }

            InterleaveSource(inputRows, mutableBufSrc, xsize);

            Status runStatus = _colorSpaceTransform.Run(threadId, mutableBufSrc, bufDst, xsize);

            if (!runStatus)
            {
                return runStatus;
            }

            DeinterleaveDestination(inputRows, bufDst, xsize);

            return true;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            _colorSpaceTransform?.Dispose();
            _colorSpaceTransform = null;
        }

        private void InterleaveSource(PipelineRow[][] inputRows, float[] dst, nuint xsize)
        {
            if (_cSrc.IsCMYK())
            {
                PipelineRow row0 = GetInputRow(inputRows, 0, 0);
                PipelineRow row1 = GetInputRow(inputRows, 1, 0);
                PipelineRow row2 = GetInputRow(inputRows, 2, 0);
                PipelineRow row3 = GetInputRow(inputRows, 3, 0);

                PixelPacking.Interleave4(row0.AsSpan(0, (int)xsize), row1.AsSpan(0, (int)xsize), row2.AsSpan(0, (int)xsize), row3.AsSpan(0, (int)xsize), dst, (int)xsize);
            }
            else if (_cSrc.Channels() == 1)
            {
                PipelineRow row0 = GetInputRow(inputRows, 0, 0);
                row0.AsSpan(0, (int)xsize).CopyTo(dst);
            }
            else
            {
                PipelineRow row0 = GetInputRow(inputRows, 0, 0);
                PipelineRow row1 = GetInputRow(inputRows, 1, 0);
                PipelineRow row2 = GetInputRow(inputRows, 2, 0);

                PixelPacking.Interleave3(row0.AsSpan(0, (int)xsize), row1.AsSpan(0, (int)xsize), row2.AsSpan(0, (int)xsize), dst, (int)xsize);
            }
        }

        private void DeinterleaveDestination(PipelineRow[][] inputRows, float[] src, nuint xsize)
        {
            PipelineRow row0 = GetInputRow(inputRows, 0, 0);
            PipelineRow row1 = GetInputRow(inputRows, 1, 0);
            PipelineRow row2 = GetInputRow(inputRows, 2, 0);

            if (_outputEncodingInfo.ColorEncoding.Channels() == 1)
            {
                ReadOnlySpan<float> span = src.AsSpan(0, (int)xsize);
                span.CopyTo(row0.AsSpan(0, (int)xsize));
                span.CopyTo(row1.AsSpan(0, (int)xsize));
                span.CopyTo(row2.AsSpan(0, (int)xsize));
            }
            else
            {
                PixelPacking.Deinterleave3(src, row0.AsSpan(0, (int)xsize), row1.AsSpan(0, (int)xsize), row2.AsSpan(0, (int)xsize), (int)xsize);
            }
        }
    }
}
