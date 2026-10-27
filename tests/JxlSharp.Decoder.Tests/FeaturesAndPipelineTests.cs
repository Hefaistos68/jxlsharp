// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Unit tests for <see cref="Alpha"/>.
    /// Ported from <c>lib/jxl/alpha_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class AlphaTests
    {
        /// <summary>
        /// Verifies normal alpha blending matches the C++ <c>AlphaTest.Blending</c> test.
        /// </summary>
        [Test]
        public void PerformAlphaBlending_NonPremultiplied_ShouldMatchExpectedValues()
        {
            float[] bgRgb0  = new float[] { 100f, 110f, 120f, 130f };
            float[] bgRgb1  = new float[] { 200f, 210f, 220f, 230f };
            float[] bgRgb2  = new float[] { 300f, 310f, 320f, 330f };
            float[] bgA     = new float[] { 0.0f, 0.5f, 0.5f, 1.0f };
            float[] fgRgb0  = new float[] { 25f, 20f, 10f, 20f };
            float[] fgRgb1  = new float[] { 35f, 30f, 20f, 30f };
            float[] fgRgb2  = new float[] { 45f, 40f, 30f, 40f };
            float[] fgA     = new float[] { 0.0f, 0.0f, 0.5f, 0.5f };
            float[] outRgb0 = new float[4];
            float[] outRgb1 = new float[4];
            float[] outRgb2 = new float[4];
            float[] outA    = new float[4];

            AlphaBlendingInputLayer bg = new AlphaBlendingInputLayer(bgRgb0, bgRgb1, bgRgb2, bgA);
            AlphaBlendingInputLayer fg = new AlphaBlendingInputLayer(fgRgb0, fgRgb1, fgRgb2, fgA);
            AlphaBlendingOutput dst    = new AlphaBlendingOutput(outRgb0, outRgb1, outRgb2, outA);

            Alpha.PerformAlphaBlending(
                in bg,
                in fg,
                in dst,
                4,
                alphaIsPremultiplied: false,
                clamp: false);

            outRgb0[0].ShouldBe(0.0f, 0.05f);
            outRgb0[1].ShouldBe(110f, 0.05f);
            outRgb0[2].ShouldBe(140f / 3f, 0.05f);
            outRgb0[3].ShouldBe(75f, 0.05f);

            outA[0].ShouldBe(0.0f, 1e-5f);
            outA[1].ShouldBe(0.5f, 1e-5f);
            outA[2].ShouldBe(0.75f, 1e-5f);
            outA[3].ShouldBe(1.0f, 1e-5f);
        }

        /// <summary>
        /// Verifies premultiplied alpha blending matches the C++ <c>AlphaTest.Blending</c> test.
        /// </summary>
        [Test]
        public void PerformAlphaBlending_Premultiplied_ShouldMatchExpectedValues()
        {
            float[] bgRgb0  = new float[] { 100f, 110f, 120f, 130f };
            float[] bgRgb1  = new float[] { 200f, 210f, 220f, 230f };
            float[] bgRgb2  = new float[] { 300f, 310f, 320f, 330f };
            float[] bgA     = new float[] { 0.0f, 0.5f, 0.5f, 1.0f };
            float[] fgRgb0  = new float[] { 25f, 20f, 10f, 20f };
            float[] fgRgb1  = new float[] { 35f, 30f, 20f, 30f };
            float[] fgRgb2  = new float[] { 45f, 40f, 30f, 40f };
            float[] fgA     = new float[] { 0.0f, 0.0f, 0.5f, 0.5f };
            float[] outRgb0 = new float[4];
            float[] outRgb1 = new float[4];
            float[] outRgb2 = new float[4];
            float[] outA    = new float[4];

            AlphaBlendingInputLayer bg = new AlphaBlendingInputLayer(bgRgb0, bgRgb1, bgRgb2, bgA);
            AlphaBlendingInputLayer fg = new AlphaBlendingInputLayer(fgRgb0, fgRgb1, fgRgb2, fgA);
            AlphaBlendingOutput dst    = new AlphaBlendingOutput(outRgb0, outRgb1, outRgb2, outA);

            Alpha.PerformAlphaBlending(
                in bg,
                in fg,
                in dst,
                4,
                alphaIsPremultiplied: true,
                clamp: false);

            outRgb0[0].ShouldBe(125f, 0.05f);
            outRgb0[1].ShouldBe(130f, 0.05f);
            outRgb0[2].ShouldBe(70f, 0.05f);
            outRgb0[3].ShouldBe(85f, 0.05f);

            outA[0].ShouldBe(0.0f, 1e-5f);
            outA[1].ShouldBe(0.5f, 1e-5f);
            outA[2].ShouldBe(0.75f, 1e-5f);
            outA[3].ShouldBe(1.0f, 1e-5f);
        }

        /// <summary>
        /// Verifies premultiply and unpremultiply alpha operations match <c>AlphaTest.PremultiplyAndUnpremultiply</c>.
        /// </summary>
        [Test]
        public void PremultiplyAndUnpremultiplyAlpha_ShouldMatchExpectedValues()
        {
            float[] a = new float[] { 0.0f, 63f / 255f, 127f / 255f, 1.0f };
            float[] r = new float[] { 120f, 130f, 140f, 150f };
            float[] g = new float[] { 124f, 134f, 144f, 154f };
            float[] b = new float[] { 127f, 137f, 147f, 157f };

            Alpha.PremultiplyAlpha(r, g, b, a, 4);

            r[0].ShouldBe(0.0f, 1e-5f);
            r[1].ShouldBe(130f * 63f / 255f, 1e-5f);
            r[2].ShouldBe(140f * 127f / 255f, 1e-5f);
            r[3].ShouldBe(150f, 1e-5f);

            Alpha.UnpremultiplyAlpha(r, g, b, a, 4);

            r[1].ShouldBe(130f, 1e-4f);
            r[2].ShouldBe(140f, 1e-4f);
            r[3].ShouldBe(150f, 1e-4f);
            g[1].ShouldBe(134f, 1e-4f);
            b[1].ShouldBe(137f, 1e-4f);
        }

        /// <summary>
        /// Verifies single-plane multiplicative blending and weighted addition.
        /// </summary>
        [Test]
        public void PerformMulBlendingAndWeightedAdd_ShouldBlendCorrectly()
        {
            float[] bg  = new float[] { 2.0f, 4.0f };
            float[] fg  = new float[] { 0.5f, 0.25f };
            float[] fga = new float[] { 0.5f, 0.25f };
            float[] dst = new float[2];

            Alpha.PerformMulBlending(bg, fg, dst, 2, clamp: true);
            dst[0].ShouldBe(1.0f, 1e-6f);
            dst[1].ShouldBe(1.0f, 1e-6f);

            Alpha.PerformAlphaWeightedAdd(bg, fg, fga, dst, 2, clamp: false);
            dst[0].ShouldBe(2.0f + 0.5f * 0.5f, 1e-6f);
            dst[1].ShouldBe(4.0f + 0.25f * 0.25f, 1e-6f);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Blending"/>.
    /// Ported from <c>lib/jxl/blending_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class BlendingTests
    {
        /// <summary>
        /// Verifies <see cref="Blending.NeedsBlending"/> and <see cref="Blending.PerformBlending"/> for a replacement frame.
        /// </summary>
        [Test]
        public void PerformBlending_Replace_ShouldCopyForegroundIntoCropRegion()
        {
            CodecMetadata metadata        = new CodecMetadata();
            FrameHeader frameHeader       = new FrameHeader(metadata);
            frameHeader.BlendingInfo.Mode = BlendMode.Replace;
            frameHeader.FrameType         = FrameType.RegularFrame;

            Blending.NeedsBlending(frameHeader).ShouldBeFalse();

            frameHeader.CustomSizeOrOrigin = true;
            frameHeader.FrameOrigin.X0     = 1;
            frameHeader.FrameOrigin.Y0     = 1;
            frameHeader.FrameSize.XSize    = 2;
            frameHeader.FrameSize.YSize    = 2;

            Blending.NeedsBlending(frameHeader).ShouldBeTrue();

            JxlMemoryManager mm    = JxlMemoryManager.Default;
            ImagePlane<float> bg0  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> bg1  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> bg2  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> fg0  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> fg1  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> fg2  = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> dst0 = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> dst1 = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> dst2 = ImagePlane<float>.Create(mm, 4, 4).Value;

            ImageOps.FillImage(1.0f, bg0);
            ImageOps.FillImage(1.0f, bg1);
            ImageOps.FillImage(1.0f, bg2);
            ImageOps.FillImage(9.0f, fg0);
            ImageOps.FillImage(9.0f, fg1);
            ImageOps.FillImage(9.0f, fg2);

            PatchBlending[] patchBlending = new PatchBlending[]
            {
                new PatchBlending { Mode = PatchBlendMode.kReplace },
            };

            PipelineRow[] bgRows  = new PipelineRow[] { new PipelineRow(bg0, 0), new PipelineRow(bg1, 0), new PipelineRow(bg2, 0) };
            PipelineRow[] fgRows  = new PipelineRow[] { new PipelineRow(fg0, 0), new PipelineRow(fg1, 0), new PipelineRow(fg2, 0) };
            PipelineRow[] dstRows = new PipelineRow[] { new PipelineRow(dst0, 0), new PipelineRow(dst1, 0), new PipelineRow(dst2, 0) };
            Blending.BlendingRowPointers rowPtrs = new Blending.BlendingRowPointers(bgRows, fgRows, dstRows, 0, 4);

            Status status = Blending.PerformBlending(
                mm,
                in rowPtrs,
                patchBlending[0],
                ReadOnlySpan<PatchBlending>.Empty,
                Array.Empty<ExtraChannelInfo>());

            status.IsOk.ShouldBeTrue();
            dst0.Row(0)[0].ShouldBe(9.0f, 1e-6f);

            bg0.Dispose();
            bg1.Dispose();
            bg2.Dispose();
            fg0.Dispose();
            fg1.Dispose();
            fg2.Dispose();
            dst0.Dispose();
            dst1.Dispose();
            dst2.Dispose();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PatchPosition"/>, <see cref="PatchReferencePosition"/>,
    /// <see cref="PatchBlending"/>, <see cref="ReferenceFrame"/>, and <see cref="PatchDictionary"/>.
    /// </summary>
    [TestFixture]
    public sealed class PatchDictionaryTests
    {
        /// <summary>
        /// Verifies <see cref="PatchBlending.UsesAlpha"/>, <see cref="PatchBlending.UsesClamp"/>,
        /// <see cref="PatchReferencePosition"/>, and <see cref="PatchPosition"/> properties.
        /// </summary>
        [Test]
        public void PatchBlendingAndPositions_ShouldHaveExpectedHelperBehavior()
        {
            PatchBlending.UsesAlpha(PatchBlendMode.kBlendAbove).ShouldBeTrue();
            PatchBlending.UsesAlpha(PatchBlendMode.kReplace).ShouldBeFalse();
            PatchBlending.UsesClamp(PatchBlendMode.kMul).ShouldBeTrue();
            PatchBlending.UsesClamp(PatchBlendMode.kAdd).ShouldBeFalse();

            PatchReferencePosition refPos = new PatchReferencePosition { Ref = 1, X0 = 2, Y0 = 3, XSize = 4, YSize = 5 };
            PatchPosition pos             = new PatchPosition { X = 10, Y = 20, RefPosIdx = 0 };

            refPos.XSize.ShouldBe(4u);
            pos.X.ShouldBe(10u);
        }

        /// <summary>
        /// Verifies <see cref="PatchDictionary"/> initialization, clear, and empty patch application.
        /// </summary>
        [Test]
        public void PatchDictionary_ClearAndAddOneRow_ShouldSucceedWhenEmpty()
        {
            PatchDictionary dict = new PatchDictionary(JxlMemoryManager.Default);

            dict.HasAny().ShouldBeFalse();
            dict.Positions.Count.ShouldBe(0);

            ReferenceFrame[] refs = new ReferenceFrame[4];

            for (int i = 0; i < 4; i++)
            {
                refs[i] = new ReferenceFrame();
            }

            dict.SetShared(refs);
            dict.Clear();
            dict.HasAny().ShouldBeFalse();
            dict.GetPatchesForRow(0).Count.ShouldBe(0);
            dict.GetReferences().ShouldBe(0);

            float[] r0         = new float[4];
            float[] r1         = new float[4];
            float[] r2         = new float[4];
            PipelineRow[] rows = new PipelineRow[]
            {
                new PipelineRow(r0, 0),
                new PipelineRow(r1, 0),
                new PipelineRow(r2, 0),
            };
            Status status = dict.AddOneRow(rows, 0, 0, 4, Array.Empty<ExtraChannelInfo>());

            status.IsOk.ShouldBeFalse();

            for (int i = 0; i < 4; i++)
            {
                refs[i].Dispose();
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Spline"/>, <see cref="QuantizedSpline"/>,
    /// <see cref="SplineSegment"/>, <see cref="SplineSegmentSpan"/>, <see cref="SplineDataView"/>,
    /// and <see cref="Splines"/>.
    /// Ported from <c>lib/jxl/splines_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class SplinesTests
    {
        /// <summary>
        /// Verifies <see cref="QuantizedSpline"/> quantization and dequantization roundtrip.
        /// </summary>
        [Test]
        public void QuantizedSpline_FromSplineAndDequantize_ShouldRoundtripControlPoints()
        {
            Spline original = new Spline();
            original.ControlPoints.Add(new Spline.Point(9f, 54f));
            original.ControlPoints.Add(new Spline.Point(118f, 159f));
            original.ControlPoints.Add(new Spline.Point(97f, 3f));

            original.ColorDct[0][0] = 0.5f;
            original.ColorDct[1][0] = 0.5f;
            original.ColorDct[2][0] = 0.5f;
            original.SigmaDct[0]    = 1.0f;

            StatusOr<QuantizedSpline> qSplineOr = QuantizedSpline.Create(original, 0, 0.0f, 0.0f);
            qSplineOr.IsOk.ShouldBeTrue();
            QuantizedSpline qSpline = qSplineOr.Value;
            Spline restored         = new Spline();
            ulong totalArea         = 0;

            Status status = qSpline.Dequantize(
                original.ControlPoints[0],
                0,
                (0.0f, 0.0f),
                320UL * 320UL,
                ref totalArea,
                restored);

            status.IsOk.ShouldBeTrue();
            restored.ControlPoints.Count.ShouldBe(3);
            restored.ControlPoints[0].ShouldBe(original.ControlPoints[0]);
            restored.ControlPoints[1].ShouldBe(original.ControlPoints[1]);
            restored.ControlPoints[2].ShouldBe(original.ControlPoints[2]);
        }

        /// <summary>
        /// Verifies <see cref="Splines.InitializeDrawCache"/>, <see cref="Splines.AddTo"/>, and <see cref="Splines.SubtractFrom"/>.
        /// </summary>
        [Test]
        public void Splines_AddToAndSubtractFrom_ShouldBeReversible()
        {
            Spline spline = new Spline();
            spline.ControlPoints.Add(new Spline.Point(10f, 10f));
            spline.ControlPoints.Add(new Spline.Point(30f, 30f));
            spline.ControlPoints.Add(new Spline.Point(50f, 20f));

            spline.ColorDct[0][0] = 1.0f;
            spline.ColorDct[1][0] = 1.0f;
            spline.ColorDct[2][0] = 1.0f;
            spline.SigmaDct[0]    = 1.5f;

            ColorCorrelation corr               = new ColorCorrelation();
            StatusOr<QuantizedSpline> qSplineOr = QuantizedSpline.Create(
                spline,
                0,
                corr.YtoXRatio(0),
                corr.YtoBRatio(0));
            qSplineOr.IsOk.ShouldBeTrue();
            QuantizedSpline qSpline = qSplineOr.Value;

            using Splines splines = new Splines(
                0,
                new List<QuantizedSpline> { qSpline },
                new List<Spline.Point> { spline.ControlPoints[0] });

            splines.HasAny().ShouldBeTrue();
            splines.GetQuantizationAdjustment().ShouldBe(0);

            Status initStatus = splines.InitializeDrawCache(64, 64, corr);
            initStatus.IsOk.ShouldBeTrue();

            using Image3<float> img = Image3<float>.Create(JxlMemoryManager.Default, 64, 64).Value;
            ImageOps.ZeroFillImage(img);

            splines.AddTo(img, new Rect(img));
            splines.SubtractFrom(img);

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 64; y++)
                {
                    Span<float> row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < 64; x++)
                    {
                        row[(int)x].ShouldBe(0.0f, 1e-4f);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Xorshift128Plus"/> and <see cref="DecNoise"/>.
    /// </summary>
    [TestFixture]
    public sealed class DecNoiseTests
    {
        /// <summary>
        /// Verifies <see cref="Xorshift128Plus"/> deterministic pseudo-random generation.
        /// </summary>
        [Test]
        public void Xorshift128Plus_Fill_ShouldProduceDeterministicBits()
        {
            Xorshift128Plus rng1 = new Xorshift128Plus(1, 2, 3, 4);
            Xorshift128Plus rng2 = new Xorshift128Plus(1, 2, 3, 4);

            Span<ulong> buf1 = stackalloc ulong[Xorshift128Plus.N];
            Span<ulong> buf2 = stackalloc ulong[Xorshift128Plus.N];
            rng1.Fill(buf1);
            rng2.Fill(buf2);

            buf1.SequenceEqual(buf2).ShouldBeTrue();
            buf1[0].ShouldNotBe(0UL);
        }

        /// <summary>
        /// Verifies <see cref="DecNoise.Random3Planes"/> generates finite noise floats in <c>[1, 2)</c>.
        /// </summary>
        [Test]
        public void DecNoise_Random3Planes_ShouldGenerateFiniteNoiseInInterval()
        {
            using Image3<float> noise = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;
            Rect rect                 = new Rect(noise);

            DecNoise.Random3Planes(
                0,
                0,
                0,
                0,
                (noise.Plane(0), rect),
                (noise.Plane(1), rect),
                (noise.Plane(2), rect));

            float val = noise.PlaneRow(0, 0)[0];

            float.IsFinite(val).ShouldBeTrue();
            val.ShouldBeInRange(1.0f, 2.0f);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Epf"/>, <see cref="OutputEncodingInfo"/>, <see cref="DecXyb"/>, and <see cref="OpsinParams"/>.
    /// Ported from <c>lib/jxl/opsin_inverse_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class FiltersAndXybTests
    {
        /// <summary>
        /// Verifies <see cref="OpsinParams.Init"/> and <see cref="DecXyb.OpsinToLinear"/>.
        /// </summary>
        [Test]
        public void OpsinToLinear_ZeroXyb_ShouldProduceZeroLinearRgb()
        {
            OpsinParams opsinParams = new OpsinParams();
            opsinParams.Init(1.0f);

            using Image3<float> img = Image3<float>.Create(JxlMemoryManager.Default, 8, 8).Value;
            ImageOps.ZeroFillImage(img);

            DecXyb.OpsinToLinear(img, new Rect(img), null, img, opsinParams).IsOk.ShouldBeTrue();

            for (nuint c = 0; c < 3; c++)
            {
                img.PlaneRow(c, 0)[0].ShouldBe(0.0f, 1e-4f);
            }
        }

        /// <summary>
        /// Verifies <see cref="OutputEncodingInfo.SetFromMetadata"/> configures the inverse matrix and color encoding.
        /// </summary>
        [Test]
        public void OutputEncodingInfo_SetFromMetadata_ShouldInitializeFields()
        {
            CodecMetadata metadata  = new CodecMetadata();
            OutputEncodingInfo info = new OutputEncodingInfo();

            Status status = info.SetFromMetadata(metadata);

            status.IsOk.ShouldBeTrue();
            info.ColorEncoding.GetColorSpace().ShouldBe(ColorSpace.RGB);
            info.DesiredIntensityTarget.ShouldBeGreaterThan(0.0f);
        }

        /// <summary>
        /// Verifies <see cref="Epf.ComputeSigma"/> populates finite inverse-sigma values.
        /// </summary>
        [Test]
        public void Epf_ComputeSigma_ShouldPopulateSigmaPlane()
        {
            CodecMetadata metadata          = new CodecMetadata();
            FrameHeader frameHeader         = new FrameHeader(metadata);
            frameHeader.LoopFilter.EpfIters = 2;

            using PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);
            PassesSharedState.InitializePassesSharedState(frameHeader, decState.Shared).IsOk.ShouldBeTrue();
            decState.Init(frameHeader).IsOk.ShouldBeTrue();

            decState.Shared.AcStrategy.FillDCT8();
            ImageOps.FillImage(1, decState.Shared.RawQuantField);
            ImageOps.FillImage((byte)0, decState.Shared.EpfSharpness);
            decState.Shared.Quantizer.SetQuant(1.0f, 1.0f, decState.Shared.RawQuantField);

            Rect blockRect = new Rect(0, 0, (int)decState.Shared.FrameDim.XSizeBlocks, (int)decState.Shared.FrameDim.YSizeBlocks);
            Epf.ComputeSigma(
                frameHeader.LoopFilter,
                in blockRect,
                decState.Shared.AcStrategy,
                decState.Shared.Quantizer.Scale(),
                decState.Shared.RawQuantField,
                decState.Shared.EpfSharpness,
                decState.Sigma);

            float sigmaVal = decState.Sigma.Row(PassesDecoderState.kSigmaPadding)[(int)PassesDecoderState.kSigmaPadding];

            float.IsFinite(sigmaVal).ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="RenderPipeline"/>, <see cref="SimpleRenderPipeline"/>,
    /// <see cref="LowMemoryRenderPipeline"/>, <see cref="GroupBorderAssigner"/>,
    /// <see cref="RenderPipelineStages"/>, <see cref="ACImageT{T}"/>,
    /// <see cref="PassesSharedState"/>, <see cref="PassesDecoderState"/>, and <see cref="GroupDecCache"/>.
    /// Ported from <c>lib/jxl/render_pipeline/render_pipeline_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class RenderPipelineAndStateTests
    {
        /// <summary>
        /// Verifies that <see cref="SimpleRenderPipeline"/> and <see cref="LowMemoryRenderPipeline"/>
        /// produce identical results when running Gaborish + Upsampling + WriteToImage3F stages.
        /// </summary>
        [Test]
        public void RenderPipeline_SimpleVsLowMemory_ShouldProduceIdenticalOutput()
        {
            JxlMemoryManager mm      = JxlMemoryManager.Default;
            FrameDimensions frameDim = new FrameDimensions();
            frameDim.Set(16, 16, 1, 0, 0, false, 1);

            LoopFilter lf = new LoopFilter { Gab = true };

            using PassesSharedState sharedSimple = new PassesSharedState();
            using PassesSharedState sharedLowMem = new PassesSharedState();

            RunTestPipeline(mm, frameDim, lf, useSimple: true, sharedSimple);
            RunTestPipeline(mm, frameDim, lf, useSimple: false, sharedLowMem);

            Image3<float> outSimple = sharedSimple.DcFrames[0];
            Image3<float> outLowMem = sharedLowMem.DcFrames[0];

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 16; y++)
                {
                    Span<float> rowS = outSimple.PlaneRow(c, y);
                    Span<float> rowL = outLowMem.PlaneRow(c, y);

                    for (nuint x = 0; x < 16; x++)
                    {
                        rowL[(int)x].ShouldBe(rowS[(int)x], 1e-5f);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="GroupBorderAssigner"/> assigns the full frame area once all groups finish.
        /// </summary>
        [Test]
        public void GroupBorderAssigner_GroupDone_ShouldCoverEntireFrame()
        {
            FrameDimensions frameDim = new FrameDimensions();
            frameDim.Set(300, 300, 1, 0, 0, false, 1);

            GroupBorderAssigner assigner = new GroupBorderAssigner();
            assigner.Init(frameDim);

            nuint totalPixels = 0;
            Span<Rect> rects  = stackalloc Rect[GroupBorderAssigner.KMaxToFinalize];

            for (nuint g = 0; g < frameDim.NumGroups; g++)
            {
                assigner.GroupDone(g, 2, 2, rects, out nuint numRects);

                for (nuint r = 0; r < numRects; r++)
                {
                    totalPixels += (nuint)(rects[(int)r].XSize * rects[(int)r].YSize);
                }
            }

            totalPixels.ShouldBe(frameDim.XSize * frameDim.YSize);
        }

        /// <summary>
        /// Verifies <see cref="ACImageT{T}"/> allocation, row access, and zero-filling.
        /// </summary>
        [Test]
        public void ACImageT_MakeAndZeroFill_ShouldWorkForInt16AndInt32()
        {
            using ACImageT<short> ac16 = ACImageT<short>.Make(JxlMemoryManager.Default, 64, 2).Value;
            using ACImageT<int> ac32   = ACImageT<int>.Make(JxlMemoryManager.Default, 64, 2).Value;

            ac16.Type.ShouldBe(ACType.k16);
            ac32.Type.ShouldBe(ACType.k32);
            ac16.IsEmpty.ShouldBeFalse();
            ac32.IsEmpty.ShouldBeFalse();

            ac16.PlaneRow(0, 0, 0).Span16[0] = 42;
            ac16.ConstPlaneRow(0, 0, 0).Mem16.Span[0].ShouldBe((short)42);
            ac16.ZeroFill();
            ac16.ConstPlaneRow(0, 0, 0).Mem16.Span[0].ShouldBe((short)0);

            ac32.PlaneRow(1, 0, 0).Span32[0] = 123456;
            ac32.ConstPlaneRow(1, 0, 0).Mem32.Span[0].ShouldBe(123456);
            ac32.ZeroFillPlane(1);
            ac32.ConstPlaneRow(1, 0, 0).Mem32.Span[0].ShouldBe(0);
        }

        /// <summary>
        /// Verifies <see cref="GroupDecCache.InitOnce"/> and <see cref="GroupDecCache.InitDCBufferOnce"/>.
        /// </summary>
        [Test]
        public void GroupDecCache_InitOnce_ShouldAllocateScratchBuffers()
        {
            using GroupDecCache cache = new GroupDecCache();

            Status status1 = cache.InitOnce(JxlMemoryManager.Default, 1, 1u << (int)AcStrategyType.DCT);
            Status status2 = cache.InitDCBufferOnce(JxlMemoryManager.Default);

            status1.IsOk.ShouldBeTrue();
            status2.IsOk.ShouldBeTrue();
            (cache.DecGroupBlock != null).ShouldBeTrue();
            (cache.ScratchSpace != null).ShouldBeTrue();
            cache.DcBuffer.XSize.ShouldBeGreaterThan(0u);
        }

        private static void RunTestPipeline(
            JxlMemoryManager mm,
            FrameDimensions frameDim,
            LoopFilter lf,
            bool useSimple,
            PassesSharedState shared)
        {
            RenderPipeline.Builder builder = new RenderPipeline.Builder(mm, 3);

            if (useSimple)
            {
                builder.UseSimpleImplementation();
            }

            builder.AddStage(RenderPipelineStages.GetGaborishStage(lf)).IsOk.ShouldBeTrue();
            builder.AddStage(RenderPipelineStages.GetWriteToImage3FStage(mm, shared, 0)).IsOk.ShouldBeTrue();

            using RenderPipeline pipeline = builder.Finalize(frameDim).Value;
            pipeline.PrepareForThreads(1, useGroupIds: false).IsOk.ShouldBeTrue();

            RenderPipelineInput input = pipeline.GetInputBuffers(0, 0);

            for (nuint c = 0; c < 3; c++)
            {
                (ImagePlane<float> plane, Rect rect) = input.GetBuffer(c);

                for (nuint y = 0; y < (nuint)rect.YSize; y++)
                {
                    Span<float> row = rect.Row(plane, y);

                    for (nuint x = 0; x < (nuint)rect.XSize; x++)
                    {
                        row[(int)x] = (float)(x + y + c) * 0.05f;
                    }
                }
            }

            input.Done().IsOk.ShouldBeTrue();
            pipeline.PassesWithAllInput().ShouldBe(1u);
        }
    }
}
