// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Additional high-coverage unit tests for <see cref="JxlSharp.Encoder"/> covering Modular WP/RCT/palette
    /// cost heuristics, custom float conversion, greedy histogram cluster refinement, multi-block AC tokenization,
    /// alpha-blended Butteraugli comparison, linear algebra/SCG optimization, and separable convolution borders.
    /// </summary>
    [TestFixture]
    public sealed unsafe class EncoderAdditionalCoverageTests
    {
        /// <summary>
        /// Verifies <see cref="EncCluster.ClusterHistograms"/> greedy merge refinement (<c>kBest</c> clustering),
        /// <c>HistogramPair</c> comparison, and alive cluster compaction.
        /// </summary>
        [Test]
        public void EncCluster_ClusterHistogramsBest_ExecutesGreedyMergeAndCompaction()
        {
            List<Histogram> inHists  = new List<Histogram>();
            List<Histogram> outHists = new List<Histogram>();
            List<uint> symbols       = new List<uint>();

            for (int k = 0; k < 4; k++)
            {
                Histogram h = new Histogram();

                h.EnsureCapacity(72);

                for (int s = 0; s < 64; s++)
                {
                    int c = (s % 7) + 1;

                    h.Counts[s]  += c;
                    h.TotalCount += (nuint)c;
                }

                h.Counts[64 + k] += 25;
                h.TotalCount     += 25;
                inHists.Add(h);
            }

            HistogramParams hp = new HistogramParams
            {
                Clustering    = HistogramParams.ClusteringType.kBest,
                MaxHistograms = 8,
            };

            Status status = EncCluster.ClusterHistograms(hp, inHists, 8, outHists, symbols);

            status.IsOk.ShouldBeTrue();
            outHists.Count.ShouldBeGreaterThan(0);
            outHists.Count.ShouldBeLessThan(4);
            symbols.Count.ShouldBe(4);
        }

        /// <summary>
        /// Verifies <see cref="EncEntropyCoder.TokenizeCoefficients"/> for multi-block transforms (<c>DCT16X16</c>)
        /// and non-zero coefficient mismatch detection.
        /// </summary>
        [Test]
        public void EncEntropyCoder_TokenizeCoefficients_CoversMultiBlockAndMismatchError()
        {
            JxlMemoryManager mm          = JxlMemoryManager.Default;
            using AcStrategyImage acsImg = AcStrategyImage.Create(mm, 2, 2).Value;
            using Image3<int> tmpNzeros  = Image3<int>.Create(mm, 2, 2).Value;
            using ImagePlane<byte> qdc   = ImagePlane<byte>.Create(mm, 2, 2).Value;
            using ImagePlane<int> qf     = ImagePlane<int>.Create(mm, 2, 2).Value;
            YCbCrChromaSubsampling cs    = new YCbCrChromaSubsampling();
            BlockCtxMap bcm              = new BlockCtxMap();
            List<Token> tokens           = new List<Token>();
            Rect rect                    = new Rect(0, 0, 2, 2);

            acsImg.Set(0, 0, AcStrategyType.DCT16X16);
            ImageOps.FillImage((byte)1, qdc);
            ImageOps.FillImage(1, qf);

            uint[] orders = new uint[CoeffOrder.kCoeffOrderMaxSize];
            int[] ac0     = new int[256];
            int[] ac1     = new int[256];
            int[] ac2     = new int[256];

            for (int i = 0; i < orders.Length; i++)
            {
                orders[i] = (uint)(i % 256);
            }

            ac0[16] = 4;
            ac1[20] = -3;
            ac2[32] = 2;

            fixed (uint* pOrders = orders)
            fixed (int* p0 = ac0)
            fixed (int* p1 = ac1)
            fixed (int* p2 = ac2)
            {
                int** acRows = stackalloc int*[3] { p0, p1, p2 };

                Status ok = EncEntropyCoder.TokenizeCoefficients(
                    pOrders,
                    rect,
                    acRows,
                    acsImg,
                    cs,
                    tmpNzeros,
                    (tokens, qdc, qf, bcm));

                ok.IsOk.ShouldBeTrue();
                tokens.Count.ShouldBeGreaterThan(0);

                Array.Clear(orders, 0, orders.Length);

                Status mismatch = EncEntropyCoder.TokenizeCoefficients(
                    pOrders,
                    rect,
                    acRows,
                    acsImg,
                    cs,
                    tmpNzeros,
                    (tokens, qdc, qf, bcm));

                mismatch.IsOk.ShouldBeFalse();
            }
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameEncoder"/> lossless speed tiers (<c>kTortoise</c>, <c>kKitten</c>,
        /// <c>kWombat</c>, <c>kGlacier</c>), WP cost estimation, RCT search, and palette cost rollback.
        /// </summary>
        [Test]
        public void EncModularEncoding_SlowSpeedTiers_CoversWpCostRctSearchAndPalettes()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            CodecMetadata meta  = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);

            SpeedTier[] slowTiers =
            {
                SpeedTier.kTortoise,
                SpeedTier.kKitten,
                SpeedTier.kWombat,
                SpeedTier.kGlacier,
            };

            foreach (SpeedTier tier in slowTiers)
            {
                using ImageBundle ib = CreateGradientBundle(mm, meta, 8, 8);
                BitWriter bw         = new BitWriter(mm);
                CompressParams cp    = new CompressParams
                {
                    ModularMode         = true,
                    ButteraugliDistance = 0.0f,
                    SpeedTier           = tier,
                    ColorTransform      = ColorTransform.None,
                    PaletteColors       = 16,
                    Colorspace          = -1,
                };

                if (tier == SpeedTier.kGlacier)
                {
                    cp.Options.Predictor = Predictor.Undefined;
                }

                Status status = EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, null));

                status.IsOk.ShouldBeTrue();
                ((ulong)bw.BitsWritten).ShouldBeGreaterThan(0UL);
            }
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameEncoder"/> decoding speed tiers, lossy Modular quantization,
        /// manual XYB factors, grayscale Modular encoding, and error branches.
        /// </summary>
        [Test]
        public void EncModularEncoding_DecodingTiersLossyAndXyb_CoversOptionsAndQuantization()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;

            ExerciseDecodingSpeedTierOptions(mm);
            ExerciseLossyModularAndXybFactors(mm);
            ExerciseModularValidationErrors(mm);
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameEncoder"/> custom float bit-depth conversion (<c>BitsPerSample != 16</c> and <c>!= 32</c>),
        /// high-precision normalized float conversion (<c>bits &gt; 22</c>), and Gaborish/spline preprocessing.
        /// </summary>
        [Test]
        public void EncModularEncoding_CustomFloatAndColorFeatures_CoversFloatToIntAndPreprocess()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;

            ExerciseCustomFloatModularEncoding(mm);
            ExerciseHighBitDepthNormalizedModularEncoding(mm);
            ExerciseModularGaborishAndCustomSplines(mm);
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameEncoder.AddVarDCTDC"/>, <see cref="ModularFrameEncoder.AddACMetadata"/>,
        /// and <see cref="ModularFrameEncoder.EncodeQuantTable"/> across near-lossless WP, near-lossless gradient,
        /// outlier detection, and multi-chunk tree merging.
        /// </summary>
        [Test]
        public void EncModularEncoding_VarDctDcAcMetadataAndQuantTable_CoversNearLosslessAndTreeMerge()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;

            ExerciseNearLosslessVarDctDcAndOutliers(mm);
            ExerciseVarDctKittenFrameAndQuantTables(mm);
        }

        /// <summary>
        /// Verifies <see cref="JxlButteraugliComparator"/> and <see cref="EncComparator.ComputeScore"/>
        /// including uninitialized guards, size mismatch checks, and premultiplied/unpremultiplied alpha blending.
        /// </summary>
        [Test]
        public void EncComparator_ComputeScoreAndGuards_CoversAlphaBlendingAndErrorBranches()
        {
            JxlMemoryManager mm     = JxlMemoryManager.Default;
            ButteraugliParams bp    = new ButteraugliParams();
            JxlCmsInterface cms     = JxlCms.GetDefaultCms();
            using DummyComparator d = new DummyComparator();

            d.GoodQualityScore().ShouldBe(1.0f);
            d.BadQualityScore().ShouldBe(2.0f);

            using JxlButteraugliComparator comp = new JxlButteraugliComparator(in bp, cms);
            CodecMetadata meta8                 = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: true);
            CodecMetadata meta16                = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: false);
            using ImageBundle ib0               = CreateAlphaBundle(mm, meta8, 8, 8, 0.25f, 0.6f, premultiplied: false);
            using ImageBundle ib1               = CreateAlphaBundle(mm, meta8, 8, 8, 0.30f, 0.8f, premultiplied: true);
            using ImageBundle ib16              = CreateGradientBundle(mm, meta16, 16, 16);
            ImagePlane<float>? nullDiff         = null;

            comp.CompareWith(ib0, ref nullDiff, out _).IsOk.ShouldBeFalse();
            comp.Mask(out _).IsOk.ShouldBeFalse();
            comp.DiffmapOpsinDynamicsImage(ib0.Color(), out _).IsOk.ShouldBeFalse();

            comp.SetReferenceImage(ib0).IsOk.ShouldBeTrue();
            comp.CompareWith(ib16, ref nullDiff, out _).IsOk.ShouldBeFalse();
            comp.CompareWith(ib0, ref nullDiff, out float selfScore).IsOk.ShouldBeTrue();
            selfScore.ShouldBeGreaterThanOrEqualTo(0.0f);

            comp.Mask(out ImagePlane<float> maskPlane).IsOk.ShouldBeTrue();
            maskPlane.Dispose();

            using ImagePlane<float> diffInit = ImagePlane<float>.Create(mm, 8, 8).Value;
            ImagePlane<float>? diffOut       = diffInit;

            Status scoreOk = EncComparator.ComputeScore(ib0, ib1, comp, cms, out float alphaScore, ref diffOut);

            scoreOk.IsOk.ShouldBeTrue();
            alphaScore.ShouldBeGreaterThanOrEqualTo(0.0f);
            diffOut?.Dispose();
        }

        /// <summary>
        /// Verifies <see cref="Vector2D"/>, <see cref="Matrix2x2"/>, <see cref="EncLinalg.ConvertToDiagonal(double[,], Span{double}, double[,])"/>,
        /// and <see cref="EncOptimize.OptimizeWithScaledConjugateGradientMethod"/> non-convex step adjustments.
        /// </summary>
        [Test]
        public void EncLinalgAndOptimize_IndexersArrayOverloadAndNonConvexStep_CoversAllBranches()
        {
            Vector2D v = new Vector2D(0.0, 0.0);

            v[0] = 3.5;
            v[1] = -1.5;
            v[0].ShouldBe(3.5);
            v[1].ShouldBe(-1.5);

            Matrix2x2 m = new Matrix2x2(0.0, 0.0, 0.0, 0.0);

            m[0, 0] = 4.0;
            m[0, 1] = 1.0;
            m[1, 0] = 1.0;
            m[1, 1] = 3.0;
            m[0, 0].ShouldBe(4.0);
            m[1, 1].ShouldBe(3.0);

            double[,] arrA    = new double[2, 2] { { 5.0, 2.0 }, { 2.0, 3.0 } };
            double[,] arrU    = new double[2, 2];
            Span<double> diag = stackalloc double[2];

            EncLinalg.ConvertToDiagonal(arrA, diag, arrU);
            diag[0].ShouldBeLessThan(diag[1]);

            double[,] diagIn = new double[2, 2] { { 4.0, 0.0 }, { 0.0, 7.0 } };

            EncLinalg.ConvertToDiagonal(diagIn, diag, arrU);
            diag[0].ShouldBe(4.0);
            diag[1].ShouldBe(7.0);

            int evalCount = 0;
            double[] w0   = { 2.0, -2.0 };

            double[] res = EncOptimize.OptimizeWithScaledConjugateGradientMethod(
                (w, df) =>
                {
                    evalCount++;

                    if (evalCount <= 2)
                    {
                        df[0] = w[0];
                        df[1] = w[1];

                        return 100.0 - ((w[0] * w[0]) + (w[1] * w[1]));
                    }

                    df[0] = -2.0 * w[0];
                    df[1] = -2.0 * w[1];

                    return (w[0] * w[0]) + (w[1] * w[1]);
                },
                w0,
                1e-4,
                12);

            res.Length.ShouldBe(2);
        }

        /// <summary>
        /// Verifies <see cref="EncGammaCorrect"/>, <see cref="EncConvolve.Separable5"/> small-width/small-height border paths,
        /// <see cref="EncXyb.RgbToYcbcr"/> empty plane handling, and <see cref="EncImageBundle.ApplyColorTransform"/>.
        /// </summary>
        [Test]
        public void EncGammaConvolveAndXyb_SmallDimensionsAndBorders_CoversEdgeBranches()
        {
            EncGammaCorrect.Srgb8ToLinearDirect(-0.1).ShouldBe(0.0);
            EncGammaCorrect.Srgb8ToLinearDirect(0.02).ShouldBeGreaterThan(0.0);
            EncGammaCorrect.Srgb8ToLinearDirect(1.2).ShouldBe(1.0);
            EncGammaCorrect.LinearToSrgb8Direct(-0.1).ShouldBe(0.0);
            EncGammaCorrect.LinearToSrgb8Direct(0.001).ShouldBeGreaterThan(0.0);
            EncGammaCorrect.LinearToSrgb8Direct(1.2).ShouldBe(1.0);

            JxlMemoryManager mm             = JxlMemoryManager.Default;
            WeightsSeparable5 weights       = new WeightsSeparable5(1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f);
            using ImagePlane<float> narrowI = ImagePlane<float>.Create(mm, 3, 6).Value;
            using ImagePlane<float> narrowO = ImagePlane<float>.Create(mm, 3, 6).Value;
            using ImagePlane<float> shortI  = ImagePlane<float>.Create(mm, 8, 3).Value;
            using ImagePlane<float> shortO  = ImagePlane<float>.Create(mm, 8, 3).Value;

            ImageOps.FillImage(0.5f, narrowI);
            ImageOps.FillImage(0.5f, shortI);

            EncConvolve.Separable5(narrowI, new Rect(0, 0, 3, 6), weights, null, narrowO).IsOk.ShouldBeTrue();
            EncConvolve.Separable5(shortI, new Rect(0, 0, 8, 3), weights, null, narrowO).IsOk.ShouldBeFalse();
            EncConvolve.Separable5(shortI, new Rect(0, 0, 8, 3), weights, null, shortO).IsOk.ShouldBeTrue();

            ImagePlane<float> emptyPlane = new ImagePlane<float>();

            EncXyb.RgbToYcbcr(emptyPlane, emptyPlane, emptyPlane, emptyPlane, emptyPlane, emptyPlane, null).IsOk.ShouldBeTrue();

            using Image3<float> srcRgb = Image3<float>.Create(mm, 4, 4).Value;
            using Image3<float> dstRgb = Image3<float>.Create(mm, 4, 4).Value;
            Image3<float> outRef       = dstRgb;

            ImageOps.FillImage(0.4f, srcRgb);

            ColorTransformSource src = new ColorTransformSource(
                ColorEncoding.SRGB(),
                255.0f,
                srcRgb,
                null,
                new Rect(0, 0, 4, 4));

            EncImageBundle.ApplyColorTransform(in src, ColorEncoding.LinearSRGB(), JxlCms.GetDefaultCms(), null, ref outRef).IsOk.ShouldBeTrue();
        }

        private static void ExerciseDecodingSpeedTierOptions(JxlMemoryManager mm)
        {
            CodecMetadata meta = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);

            int[] decTiers = { 1, 2, 3, 4 };

            foreach (int decTier in decTiers)
            {
                using ImageBundle ib = CreateGradientBundle(mm, meta, 8, 8);
                BitWriter bw         = new BitWriter(mm);
                CompressParams cp    = new CompressParams
                {
                    ModularMode         = true,
                    ButteraugliDistance = 0.0f,
                    Responsive          = decTier == 1 ? 1 : 0,
                    DecodingSpeedTier   = (nuint)decTier,
                    Colorspace          = -1,
                };

                if (decTier == 4)
                {
                    cp.Options.Predictor = Predictor.Weighted;
                }

                EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, null)).IsOk.ShouldBeTrue();
            }

            using ImageBundle ibPal = CreateGradientBundle(mm, meta, 8, 8);
            BitWriter bwPal         = new BitWriter(mm);
            CompressParams cpPal    = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                LossyPalette        = true,
                PaletteColors       = 16,
            };

            cpPal.Options.Predictor = Predictor.Gradient;
            EncFrame.EncodeFrame(mm, cpPal, new FrameInfo(), meta, ibPal, JxlCms.GetDefaultCms(), (null, bwPal, null)).IsOk.ShouldBeTrue();
        }

        private static void ExerciseLossyModularAndXybFactors(JxlMemoryManager mm)
        {
            CodecMetadata meta   = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: true);
            using ImageBundle ib = CreateAlphaBundle(mm, meta, 8, 8, 0.3f, 0.8f, premultiplied: false);
            BitWriter bwLossy    = new BitWriter(mm);
            CompressParams cp    = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 3.0f,
                ColorTransform      = ColorTransform.None,
                Responsive          = 1,
                Colorspace          = 6,
            };

            cp.EcDistance.Add(-1.0f);
            cp.Options.Predictor = Predictor.Weighted;
            EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bwLossy, null)).IsOk.ShouldBeTrue();

            using ImageBundle ibXyb = CreateGradientBundle(mm, CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false), 8, 8);
            BitWriter bwXyb         = new BitWriter(mm);
            CompressParams cpXyb    = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 1.5f,
                ColorTransform      = ColorTransform.XYB,
                Responsive          = 0,
            };

            cpXyb.ManualXybFactors.Add(512.0f);
            cpXyb.ManualXybFactors.Add(128.0f);
            cpXyb.ManualXybFactors.Add(128.0f);
            EncFrame.EncodeFrame(mm, cpXyb, new FrameInfo(), CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false), ibXyb, JxlCms.GetDefaultCms(), (null, bwXyb, null)).IsOk.ShouldBeTrue();

            CodecMetadata grayMeta   = CreateBasicMetadata(8, 8, isGray: true, hasAlpha: false);
            using ImageBundle ibGray = CreateGradientBundle(mm, grayMeta, 8, 8);
            BitWriter bwGray         = new BitWriter(mm);
            CompressParams cpGray    = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
            };

            EncFrame.EncodeFrame(mm, cpGray, new FrameInfo(), grayMeta, ibGray, JxlCms.GetDefaultCms(), (null, bwGray, null)).IsOk.ShouldBeTrue();
        }

        private static void ExerciseModularValidationErrors(JxlMemoryManager mm)
        {
            CodecMetadata meta24 = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);

            meta24.M.BitDepth.BitsPerSample = 24;

            using ImageBundle ib24   = CreateGradientBundle(mm, meta24, 8, 8);
            BitWriter bw24           = new BitWriter(mm);
            CompressParams cpLevel5  = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
                Level               = 5,
            };

            EncFrame.EncodeFrame(mm, cpLevel5, new FrameInfo(), meta24, ib24, JxlCms.GetDefaultCms(), (null, bw24, null)).IsOk.ShouldBeFalse();

            CodecMetadata meta8      = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);
            using ImageBundle ib8    = CreateGradientBundle(mm, meta8, 8, 8);
            BitWriter bwZeroRepeats  = new BitWriter(mm);
            CompressParams cpRepeats = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 2.0f,
                ColorTransform      = ColorTransform.None,
            };

            cpRepeats.Options.NbRepeats = 0.0f;
            EncFrame.EncodeFrame(mm, cpRepeats, new FrameInfo(), meta8, ib8, JxlCms.GetDefaultCms(), (null, bwZeroRepeats, null)).IsOk.ShouldBeFalse();
        }

        private static void ExerciseCustomFloatModularEncoding(JxlMemoryManager mm)
        {
            CodecMetadata metaFp = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);

            metaFp.M.BitDepth.FloatingPointSample   = true;
            metaFp.M.BitDepth.BitsPerSample         = 24;
            metaFp.M.BitDepth.ExponentBitsPerSample = 6;

            using ImageBundle ibFp   = new ImageBundle(mm, metaFp.M);
            Image3<float> imgFp      = Image3<float>.Create(mm, 8, 8).Value;
            Span<float> r0           = imgFp.Plane(0).RowSpan(0);
            BitWriter bwFp           = new BitWriter(mm);
            CompressParams cpFp      = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
            };

            ImageOps.FillImage(0.5f, imgFp);
            r0[0] = 0.0f;
            r0[1] = -0.0f;
            r0[2] = -0.25f;
            r0[3] = 1.5f;
            r0[4] = float.PositiveInfinity;
            ibFp.SetFromImage(imgFp, ColorEncoding.LinearSRGB()).IsOk.ShouldBeTrue();

            EncFrame.EncodeFrame(mm, cpFp, new FrameInfo(), metaFp, ibFp, JxlCms.GetDefaultCms(), (null, bwFp, null)).IsOk.ShouldBeTrue();

            ibFp.Color().Plane(0).RowSpan(0)[4] = 1e-20f;
            EncFrame.EncodeFrame(mm, cpFp, new FrameInfo(), metaFp, ibFp, JxlCms.GetDefaultCms(), (null, new BitWriter(mm), null)).IsOk.ShouldBeFalse();

            ibFp.Color().Plane(0).RowSpan(0)[4] = 1e-10f;
            EncFrame.EncodeFrame(mm, cpFp, new FrameInfo(), metaFp, ibFp, JxlCms.GetDefaultCms(), (null, new BitWriter(mm), null)).IsOk.ShouldBeFalse();

            ibFp.Color().Plane(0).RowSpan(0)[4]     = 0.5f;
            metaFp.M.BitDepth.BitsPerSample         = 32;
            metaFp.M.BitDepth.ExponentBitsPerSample = 7;
            EncFrame.EncodeFrame(mm, cpFp, new FrameInfo(), metaFp, ibFp, JxlCms.GetDefaultCms(), (null, new BitWriter(mm), null)).IsOk.ShouldBeFalse();
        }

        private static void ExerciseHighBitDepthNormalizedModularEncoding(JxlMemoryManager mm)
        {
            CodecMetadata meta24 = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);

            meta24.M.BitDepth.FloatingPointSample   = false;
            meta24.M.BitDepth.BitsPerSample         = 24;
            meta24.M.BitDepth.ExponentBitsPerSample = 0;

            using ImageBundle ib24 = CreateGradientBundle(mm, meta24, 8, 8);
            BitWriter bw24         = new BitWriter(mm);
            CompressParams cp24    = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
                Level               = 10,
            };

            EncFrame.EncodeFrame(mm, cp24, new FrameInfo(), meta24, ib24, JxlCms.GetDefaultCms(), (null, bw24, null)).IsOk.ShouldBeTrue();
        }

        private static void ExerciseModularGaborishAndCustomSplines(JxlMemoryManager mm)
        {
            CodecMetadata meta                = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);
            FrameHeader fh                    = new FrameHeader(meta);
            CompressParams cp                 = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
            };
            using PassesEncoderState encState = new PassesEncoderState(mm) { CParams = cp };

            fh.Encoding       = FrameEncoding.Modular;
            fh.ColorTransform = ColorTransform.None;
            fh.LoopFilter.Gab = true;

            Spline sampleSpline            = CreateSimpleSpline();
            QuantizedSpline qSpline        = QuantizedSpline.Create(sampleSpline, 0, 0.0f, 1.0f).Value;
            List<QuantizedSpline> qList    = new List<QuantizedSpline> { qSpline };
            List<Spline.Point> startPoints = new List<Spline.Point> { new Spline.Point(1.0f, 1.0f) };
            using Splines customSplines    = new Splines(2, qList, startPoints);

            cp.CustomSplines = new SplineDataView(qList, startPoints);
            PassesSharedState.InitializePassesSharedState(fh, encState.Shared, encoder: true).IsOk.ShouldBeTrue();

            using ModularFrameEncoder mfe = ModularFrameEncoder.Create(mm, fh, cp, streamingMode: false).Value;
            using Image3<float> color     = Image3<float>.Create(mm, 8, 8).Value;
            FrameDimensions fd            = fh.ToFrameDimensions();
            Rect rect                     = new Rect(0, 0, 8, 8);

            ImageOps.FillImage(0.5f, color);

            ModularEncodingDataArgs args = new ModularEncodingDataArgs
            {
                FrameHeader   = fh,
                Metadata      = meta.M,
                Color         = color,
                ExtraChannels = Array.Empty<ImagePlane<float>>(),
                GroupRect     = rect,
                PatchDim      = fd,
                FrameAreaRect = rect,
                EncState      = encState,
                Cms           = JxlCms.GetDefaultCms(),
                Pool          = null,
                AuxOut        = null,
                DoColor       = true,
            };

            mfe.ComputeEncodingData(args).IsOk.ShouldBeTrue();
        }

        private static void ExerciseNearLosslessVarDctDcAndOutliers(JxlMemoryManager mm)
        {
            CodecMetadata meta                = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: false);
            FrameHeader fh                    = new FrameHeader(meta);
            CompressParams cpWp               = new CompressParams { SpeedTier = SpeedTier.kTortoise, DecodingSpeedTier = 0 };
            CompressParams cpGrad             = new CompressParams { SpeedTier = SpeedTier.kKitten, DecodingSpeedTier = 1 };
            using PassesEncoderState encState = new PassesEncoderState(mm) { CParams = cpWp };
            using Image3<float> dc            = Image3<float>.Create(mm, 2, 2).Value;
            Rect dcRect                       = new Rect(0, 0, 2, 2);

            PassesSharedState.InitializePassesSharedState(fh, encState.Shared, encoder: true).IsOk.ShouldBeTrue();
            ImageOps.FillImage(0.25f, dc);
            dc.Plane(0).RowSpan(0)[0] = 50.0f;
            dc.Plane(1).RowSpan(0)[1] = -50.0f;

            using ModularFrameEncoder mfeWp   = ModularFrameEncoder.Create(mm, fh, cpWp, streamingMode: false).Value;
            using ModularFrameEncoder mfeGrad = ModularFrameEncoder.Create(mm, fh, cpGrad, streamingMode: false).Value;

            mfeWp.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: true, encState, jpegTranscode: false).IsOk.ShouldBeTrue();
            mfeWp.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: false, encState, jpegTranscode: false).IsOk.ShouldBeTrue();
            mfeGrad.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: true, encState, jpegTranscode: false).IsOk.ShouldBeTrue();
            mfeGrad.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: false, encState, jpegTranscode: false).IsOk.ShouldBeTrue();

            dc.Plane(1).RowSpan(0)[0] = float.NaN;
            mfeWp.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: true, encState, jpegTranscode: false).IsOk.ShouldBeFalse();

            dc.Plane(1).RowSpan(0)[0] = 0.0f;

            Span<byte> hs = stackalloc byte[3] { 1, 2, 1 };
            Span<byte> vs = stackalloc byte[3] { 1, 2, 1 };

            fh.ChromaSubsampling.Set(hs, vs).IsOk.ShouldBeTrue();
            mfeWp.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: true, encState, jpegTranscode: false).IsOk.ShouldBeFalse();
            mfeWp.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: false, encState, jpegTranscode: false).IsOk.ShouldBeTrue();
        }

        private static void ExerciseVarDctKittenFrameAndQuantTables(JxlMemoryManager mm)
        {
            CodecMetadata meta   = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: false);
            using ImageBundle ib = CreateGradientBundle(mm, meta, 16, 16);
            BitWriter bw         = new BitWriter(mm);
            CompressParams cp    = new CompressParams
            {
                ModularMode               = false,
                ButteraugliDistance       = 1.0f,
                SpeedTier                 = SpeedTier.kKitten,
                ForceCflJpegRecompression = true,
                Gaborish                  = Override.Off,
            };

            EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, null)).IsOk.ShouldBeTrue();

            FrameHeader fh                = new FrameHeader(meta);
            using ModularFrameEncoder mfe = ModularFrameEncoder.Create(mm, fh, cp, streamingMode: false).Value;
            int[] rawQ                    = new int[3 * 8 * 8];
            QuantEncoding qe              = QuantEncoding.RAW(rawQ, 2);
            BitWriter bwQt                = new BitWriter(mm);

            Array.Fill(rawQ, 4);
            mfe.AddQuantTable(8, 8, qe, 0).IsOk.ShouldBeTrue();
            mfe.AddQuantTable(8, 8, qe, 99).IsOk.ShouldBeFalse();
            mfe.AddQuantTable(4, 4, qe, 0).IsOk.ShouldBeFalse();
            ModularFrameEncoder.EncodeQuantTable(mm, 8, 8, bwQt, qe, 0, mfe).IsOk.ShouldBeTrue();
            ModularFrameEncoder.EncodeQuantTable(mm, 8, 8, bwQt, qe, 99, mfe).IsOk.ShouldBeFalse();
            ModularFrameEncoder.EncodeQuantTable(mm, 8, 8, new BitWriter(mm), qe, 0, null).IsOk.ShouldBeTrue();

            ExerciseJpegMetadataEdgeCases(mm);
        }

        private static void ExerciseJpegMetadataEdgeCases(JxlMemoryManager mm)
        {
            JPEGData jpgEmpty                  = new JPEGData();
            YCbCrChromaSubsampling css         = new YCbCrChromaSubsampling();
            ColorEncoding ce                   = new ColorEncoding();
            byte[] iccChunk                    = new byte[20];
            byte[] iccTag                      = System.Text.Encoding.ASCII.GetBytes("ICC_PROFILE\0");
            byte[] adobeApp14                  = new byte[15];
            byte[] adobeTag                    = System.Text.Encoding.ASCII.GetBytes("Adobe");

            EncJpegData.SetChromaSubsamplingFromJpegData(jpgEmpty, css).IsOk.ShouldBeFalse();
            EncJpegData.SetColorTransformFromJpegData(jpgEmpty, out _).IsOk.ShouldBeFalse();

            jpgEmpty.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1, Id = 1 });
            EncJpegData.SetChromaSubsamplingFromJpegData(jpgEmpty, css).IsOk.ShouldBeTrue();
            EncJpegData.SetColorTransformFromJpegData(jpgEmpty, out _).IsOk.ShouldBeTrue();
            EncJpegData.SetColorEncodingFromJpegData(jpgEmpty, ce).IsOk.ShouldBeTrue();

            iccChunk[0] = 0xE2;
            iccChunk[1] = 0;
            iccChunk[2] = 18;
            iccTag.CopyTo(iccChunk.AsSpan(3));
            iccChunk[15] = 1;
            iccChunk[16] = 1;
            iccChunk[17] = 0xAA;

            adobeApp14[0] = 0xEE;
            adobeApp14[1] = 0;
            adobeApp14[2] = 14;
            adobeTag.CopyTo(adobeApp14.AsSpan(3));
            adobeApp14[14] = 0;

            jpgEmpty.Components.Clear();
            jpgEmpty.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1, Id = 'R' });
            jpgEmpty.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1, Id = 'G' });
            jpgEmpty.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1, Id = 'B' });
            jpgEmpty.MarkerOrder.Add(0xEE);
            jpgEmpty.AppData.Add(adobeApp14);
            EncJpegData.SetColorTransformFromJpegData(jpgEmpty, out ColorTransform ctRgb).IsOk.ShouldBeTrue();
            ctRgb.ShouldBe(ColorTransform.None);

            jpgEmpty.AppData.Add(iccChunk);
            _ = EncJpegData.SetColorEncodingFromJpegData(jpgEmpty, ce);

            iccChunk[15] = 2;
            EncJpegData.EncodeJPEGData(mm, jpgEmpty, out _, new CompressParams()).IsOk.ShouldBeFalse();
        }

        private static CodecMetadata CreateBasicMetadata(uint xsize, uint ysize, bool isGray, bool hasAlpha)
        {
            CodecMetadata meta = new CodecMetadata();

            meta.Size.Set(xsize, ysize).IsOk.ShouldBeTrue();
            meta.M.ColorEncoding = isGray ? ColorEncoding.SRGB(isGray: true) : ColorEncoding.SRGB(isGray: false);
            meta.M.SetUintSamples(8);

            if (hasAlpha)
            {
                ExtraChannelInfo eci = new ExtraChannelInfo
                {
                    Type            = ExtraChannel.Alpha,
                    AlphaAssociated = false,
                };

                eci.BitDepth.BitsPerSample = 8;
                meta.M.ExtraChannelInfo.Add(eci);
                meta.M.NumExtraChannels = 1;
            }

            return meta;
        }

        private static ImageBundle CreateGradientBundle(JxlMemoryManager mm, CodecMetadata meta, nuint xsize, nuint ysize)
        {
            ImageBundle ib    = new ImageBundle(mm, meta.M);
            Image3<float> img = Image3<float>.Create(mm, xsize, ysize).Value;
            float invX        = 1.0f / Math.Max(1u, xsize);
            float invY        = 1.0f / Math.Max(1u, ysize);

            for (nuint y = 0; y < ysize; y++)
            {
                Span<float> r0 = img.Plane(0).RowSpan(y);
                Span<float> r1 = img.Plane(1).RowSpan(y);
                Span<float> r2 = img.Plane(2).RowSpan(y);

                for (nuint x = 0; x < xsize; x++)
                {
                    r0[(int)x] = ((x + y) % 4) * 0.2f;
                    r1[(int)x] = x * invX;
                    r2[(int)x] = y * invY;
                }
            }

            ib.SetFromImage(img, meta.M.ColorEncoding).IsOk.ShouldBeTrue();

            return ib;
        }

        private static ImageBundle CreateAlphaBundle(
            JxlMemoryManager mm,
            CodecMetadata meta,
            nuint xsize,
            nuint ysize,
            float baseColor,
            float alphaVal,
            bool premultiplied)
        {
            meta.M.ExtraChannelInfo[0].AlphaAssociated = premultiplied;

            ImageBundle ib          = new ImageBundle(mm, meta.M);
            Image3<float> color     = Image3<float>.Create(mm, xsize, ysize).Value;
            ImagePlane<float> alpha = ImagePlane<float>.Create(mm, xsize, ysize).Value;

            ImageOps.FillImage(baseColor, color);
            ImageOps.FillImage(alphaVal, alpha);
            ib.SetFromImage(color, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ib.SetAlpha(alpha).IsOk.ShouldBeTrue();

            return ib;
        }

        private static Spline CreateSimpleSpline()
        {
            Spline s = new Spline();

            s.ControlPoints.Add(new Spline.Point(1.0f, 1.0f));
            s.ControlPoints.Add(new Spline.Point(3.0f, 2.0f));
            s.ControlPoints.Add(new Spline.Point(5.0f, 4.0f));
            s.ControlPoints.Add(new Spline.Point(7.0f, 6.0f));

            for (int c = 0; c < 3; c++)
            {
                s.ColorDct[c][0] = 0.2f;
            }

            s.SigmaDct[0] = 0.5f;

            return s;
        }

        private sealed class DummyComparator : Comparator
        {
            public override Status SetReferenceImage(ImageBundle reference)
            {
                return true;
            }

            public override Status SetLinearReferenceImage(Image3<float> linearReference)
            {
                return true;
            }

            public override Status CompareWith(ImageBundle actual, ref ImagePlane<float>? diffmap, out float score)
            {
                score = 0.0f;

                return true;
            }

            public override float GoodQualityScore()
            {
                return 1.0f;
            }

            public override float BadQualityScore()
            {
                return 2.0f;
            }
        }
    }
}
