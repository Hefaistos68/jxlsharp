// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// High-coverage unit tests for <see cref="JxlSharp.Encoder"/> subsystems including features,
    /// LZ77, Butteraugli, comparator, Modular encoding, FastLossless, frame/JPEG transcoding, and public API.
    /// </summary>
    [TestFixture]
    public sealed unsafe class EncoderHighCoverageTests
    {
        /// <summary>
        /// Verifies spline tokenization and encoding for empty and multi-spline collections.
        /// </summary>
        [Test]
        public void EncSplines_TokenizeAndEncode_CoversEmptyAndMultipleSplines()
        {
            JxlMemoryManager mm  = JxlMemoryManager.Default;
            Splines emptySplines = new Splines(mm);
            BitWriter emptyBw    = new BitWriter(mm);

            Status emptyStatus = EncSplines.EncodeSplines(
                emptySplines,
                emptyBw,
                LayerType.Splines,
                new HistogramParams(),
                null);

            emptyStatus.IsOk.ShouldBeFalse();

            Spline s0 = CreateSampleSpline(2.0f, 3.0f, 0.25f);
            Spline s1 = CreateSampleSpline(6.0f, 8.0f, -0.15f);

            StatusOr<QuantizedSpline> qs0Or = QuantizedSpline.Create(s0, 0, 0.0f, 1.0f);
            StatusOr<QuantizedSpline> qs1Or = QuantizedSpline.Create(s1, 0, 0.0f, 1.0f);

            qs0Or.IsOk.ShouldBeTrue();
            qs1Or.IsOk.ShouldBeTrue();

            List<Token> tokens = new List<Token>();

            EncSplines.TokenizeQuantizedSpline(qs0Or.Value, tokens);
            tokens.Count.ShouldBeGreaterThan(0);

            List<QuantizedSpline> qList  = new List<QuantizedSpline> { qs0Or.Value, qs1Or.Value };
            List<Spline.Point> startList = new List<Spline.Point>
            {
                new Spline.Point(2.0f, 3.0f),
                new Spline.Point(6.0f, 8.0f),
            };

            using Splines populated = new Splines(2, qList, startList);
            BitWriter bw            = new BitWriter(mm);
            AuxOut aux              = new AuxOut();

            Status encodeOk = EncSplines.EncodeSplines(
                populated,
                bw,
                LayerType.Splines,
                new HistogramParams(),
                aux);

            encodeOk.IsOk.ShouldBeTrue();
            ((ulong)bw.BitsWritten).ShouldBeGreaterThan(0UL);
        }

        /// <summary>
        /// Verifies <see cref="QuantizedPatch"/> equality, hashing, comparison, and dot parameter helpers.
        /// </summary>
        [Test]
        public void QuantizedPatch_EqualsGetHashCodeCompareTo_CoversAllBranches()
        {
            QuantizedPatch p0    = CreateQuantizedPatch(4, 4, 1);
            QuantizedPatch pSame = CreateQuantizedPatch(4, 4, 1);
            QuantizedPatch pDiff = CreateQuantizedPatch(4, 4, 2);
            QuantizedPatch pWide = CreateQuantizedPatch(6, 4, 1);
            QuantizedPatch pTall = CreateQuantizedPatch(4, 6, 1);

            p0.Equals(null).ShouldBeFalse();
            p0.Equals((object?)null).ShouldBeFalse();
            p0.Equals((object)pSame).ShouldBeTrue();
            p0.Equals(pSame).ShouldBeTrue();
            p0.Equals(pDiff).ShouldBeFalse();
            p0.Equals(pWide).ShouldBeFalse();
            p0.Equals(pTall).ShouldBeFalse();

            p0.GetHashCode().ShouldBe(pSame.GetHashCode());
            p0.CompareTo(null).ShouldBe(1);
            p0.CompareTo(pSame).ShouldBe(0);
            p0.CompareTo(pDiff).ShouldBeLessThan(0);
            p0.CompareTo(pWide).ShouldBeLessThan(0);
            p0.CompareTo(pTall).ShouldBeLessThan(0);

            PatchInfo patchInfo      = new PatchInfo { Patch = p0 };
            GaussianDetectParams gdp = new GaussianDetectParams { THigh = 0.1, TLow = 0.02, MaxWinSize = 5 };
            EllipseQuantParams eqp   = new EllipseQuantParams
            {
                XSize     = 64,
                YSize     = 32,
                QPosition = 4,
                YtoX      = 0.0f,
                YtoB      = 1.0f,
            };

            patchInfo.Occurrences.Add((2u, 3u));
            eqp.QuantPositionSize(out nuint qx, out nuint qy);
            ((ulong)qx).ShouldBe(16UL);
            ((ulong)qy).ShouldBe(8UL);
            patchInfo.Occurrences.Count.ShouldBe(1);
            gdp.MaxWinSize.ShouldBe(5u);
        }

        /// <summary>
        /// Verifies <see cref="EncDotDictionary"/> ellipse detection and dot dictionary search overrides.
        /// </summary>
        [Test]
        public void EncDotDictionary_DetectAndFind_CoversAllOverrides()
        {
            JxlMemoryManager mm      = JxlMemoryManager.Default;
            using Image3<float> ops  = CreateCheckerImage3(mm, 16, 16, 0.2f, 0.8f);
            GaussianDetectParams gdp = new GaussianDetectParams
            {
                THigh      = 0.05,
                TLow       = 0.01,
                MaxWinSize = 5,
                MaxCC      = 10,
                MinScore   = 1.0,
            };
            EllipseQuantParams eqp = new EllipseQuantParams
            {
                XSize     = 16,
                YSize     = 16,
                QPosition = 2,
            };
            StatusOr<List<PatchInfo>> emptyRes = EncDotDictionary.DetectGaussianEllipses(
                ops,
                new Rect(0, 0, 0, 0),
                gdp,
                eqp,
                null);
            StatusOr<List<PatchInfo>> validRes = EncDotDictionary.DetectGaussianEllipses(
                ops,
                new Rect(0, 0, 16, 16),
                gdp,
                eqp,
                null);

            emptyRes.IsOk.ShouldBeTrue();
            validRes.IsOk.ShouldBeTrue();
            emptyRes.Value.Count.ShouldBe(0);
            validRes.Value.Count.ShouldBe(0);

            CompressParams cpOff  = new CompressParams { Dots = Override.Off };
            CompressParams cpOn   = new CompressParams { Dots = Override.On, ButteraugliDistance = 1.0f };
            CompressParams cpDef  = new CompressParams { Dots = Override.Default, ButteraugliDistance = 4.0f };
            Rect fullRect         = new Rect(0, 0, 16, 16);
            ColorCorrelation corr = new ColorCorrelation();

            EncDotDictionary.FindDotDictionary(cpOff, ops, fullRect, corr, null).Value.ShouldBeEmpty();
            EncDotDictionary.FindDotDictionary(cpOn, ops, fullRect, corr, null).Value.ShouldBeEmpty();
            EncDotDictionary.FindDotDictionary(cpDef, ops, fullRect, corr, null).Value.ShouldBeEmpty();
        }

        /// <summary>
        /// Verifies <see cref="PatchDictionaryEncoder"/> serialization and reference subtraction.
        /// </summary>
        [Test]
        public void PatchDictionaryEncoder_EncodeAndSubtract_CoversMultiRefAndAlphaBlending()
        {
            JxlMemoryManager mm       = JxlMemoryManager.Default;
            PatchDictionary emptyDict = new PatchDictionary(mm);
            BitWriter emptyBw         = new BitWriter(mm);

            PatchDictionaryEncoder.Encode(emptyDict, emptyBw, LayerType.Dictionary, null).IsOk.ShouldBeFalse();

            PatchDictionary pdic = CreateMultiRefPatchDictionary();
            BitWriter bw         = new BitWriter(mm);
            AuxOut aux           = new AuxOut();
            Status encOk         = PatchDictionaryEncoder.Encode(pdic, bw, LayerType.Dictionary, aux);

            encOk.IsOk.ShouldBeTrue();
            ((ulong)bw.BitsWritten).ShouldBeGreaterThan(0UL);

            CodecMetadata meta         = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: false);
            using ImageBundle refIb    = new ImageBundle(mm, meta.M);
            Image3<float> refColor     = CreateCheckerImage3(mm, 16, 16, 0.5f, 0.5f);
            ReferenceFrame[] refFrames = new ReferenceFrame[4];

            refIb.SetFromImage(refColor, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            for (int i = 0; i < 4; i++)
            {
                refFrames[i] = new ReferenceFrame { Frame = refIb, IbIsInXyb = true };
            }

            pdic.SetShared(refFrames);

            using Image3<float> opsin = CreateCheckerImage3(mm, 16, 16, 1.0f, 1.0f);
            Status subOk              = PatchDictionaryEncoder.SubtractFrom(pdic, opsin);

            subOk.IsOk.ShouldBeTrue();

            using PassesEncoderState state = new PassesEncoderState(mm);

            PatchDictionaryEncoder.FindBestPatchDictionary(
                opsin,
                state,
                JxlCms.GetDefaultCms(),
                null,
                aux).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="EncNoise.GetNoiseParameter"/> and <see cref="EncNoise.EncodeNoise"/> across all branches.
        /// </summary>
        [Test]
        public void EncNoise_GetNoiseParameterAndEncodeNoise_CoversAllThresholdsAndValidation()
        {
            JxlMemoryManager mm        = JxlMemoryManager.Default;
            using Image3<float> tiny   = CreateCheckerImage3(mm, 4, 4, 0.1f, 0.2f);
            using Image3<float> flat   = CreateCheckerImage3(mm, 16, 16, 0.3f, 0.3f);
            using Image3<float> loud   = CreateCheckerImage3(mm, 16, 16, 0.0f, 5.0f);
            using Image3<float> gentle = CreateGentleNoisyImage3(mm, 16, 16);
            NoiseParams np             = new NoiseParams();

            EncNoise.GetNoiseParameter(tiny, np, 1.0f).IsOk.ShouldBeFalse();
            EncNoise.GetNoiseParameter(flat, np, 1.0f).IsOk.ShouldBeFalse();
            np.HasAny().ShouldBeFalse();

            EncNoise.GetNoiseParameter(loud, np, 1.0f).IsOk.ShouldBeFalse();
            np.HasAny().ShouldBeFalse();

            EncNoise.GetNoiseParameter(gentle, np, 1.0f).IsOk.ShouldBeTrue();
            np.HasAny().ShouldBeTrue();

            BitWriter bw       = new BitWriter(mm);
            AuxOut aux         = new AuxOut();
            NoiseParams negNp  = new NoiseParams();
            NoiseParams hugeNp = new NoiseParams();
            NoiseParams photon = EncPhotonNoise.SimulatePhotonNoise(64, 64, 800.0f);

            EncNoise.EncodeNoise(new NoiseParams(), bw, LayerType.Noise, aux).IsOk.ShouldBeFalse();
            negNp.Lut[0] = -0.5f;
            EncNoise.EncodeNoise(negNp, bw, LayerType.Noise, aux).IsOk.ShouldBeFalse();
            hugeNp.Lut[0] = 10.0f;
            EncNoise.EncodeNoise(hugeNp, bw, LayerType.Noise, aux).IsOk.ShouldBeFalse();
            EncNoise.EncodeNoise(photon, bw, LayerType.Noise, aux).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="EncLz77.ApplyLZ77"/> across RLE and greedy window methods with and without special distances.
        /// </summary>
        [Test]
        public void EncLz77_ApplyLz77_CoversRleGreedyAndSpecialDistances()
        {
            List<List<Token>> streams            = BuildCompressibleTokenStreams();
            HistogramParams.LZ77Method[] methods =
            {
                HistogramParams.LZ77Method.kNone,
                HistogramParams.LZ77Method.kRLE,
                HistogramParams.LZ77Method.kLZ77b1w3f,
                HistogramParams.LZ77Method.kLZ77b3w3f,
                HistogramParams.LZ77Method.kLZ77b7w3f,
                HistogramParams.LZ77Method.kLZ77b15w3f,
                HistogramParams.LZ77Method.kLZ77b31w3f,
                HistogramParams.LZ77Method.kLZ77b1w3t,
                HistogramParams.LZ77Method.kLZ77b3w3t,
                HistogramParams.LZ77Method.kLZ77b7w3t,
                HistogramParams.LZ77Method.kLZ77b15w3t,
                HistogramParams.LZ77Method.kLZ77b31w3t,
            };

            foreach (HistogramParams.LZ77Method method in methods)
            {
                HistogramParams hpWithWidth = new HistogramParams
                {
                    Lz77Method   = method,
                    ForceHuffman = false,
                    ImageWidths  = new List<nuint> { 16 },
                };
                LZ77Params lz77A    = new LZ77Params { MinSymbol = 224, NonserializedDistanceContext = 2 };
                List<List<Token>> r = EncLz77.ApplyLZ77(hpWithWidth, 2, streams, lz77A);

                if (method != HistogramParams.LZ77Method.kNone)
                {
                    r.Count.ShouldBeGreaterThan(0);
                }

                HistogramParams hpNoWidth = new HistogramParams
                {
                    Lz77Method   = method,
                    ForceHuffman = true,
                };
                LZ77Params lz77B = new LZ77Params { MinSymbol = 512, NonserializedDistanceContext = 2 };

                _ = EncLz77.ApplyLZ77(hpNoWidth, 2, streams, lz77B);
            }
        }

        /// <summary>
        /// Verifies <see cref="EncLz77.ApplyLZ77"/> optimal DP matching methods (<c>kOptc1..kOptc256</c>).
        /// </summary>
        [Test]
        public void EncLz77_ApplyLz77Optimal_CoversOptimalChainsAndSpecialDistances()
        {
            List<List<Token>> streams               = BuildCompressibleTokenStreams();
            HistogramParams.LZ77Method[] optMethods =
            {
                HistogramParams.LZ77Method.kOptc1,
                HistogramParams.LZ77Method.kOptc3,
                HistogramParams.LZ77Method.kOptc8,
                HistogramParams.LZ77Method.kOptc256,
            };

            foreach (HistogramParams.LZ77Method method in optMethods)
            {
                HistogramParams hpAns = new HistogramParams
                {
                    Lz77Method   = method,
                    ForceHuffman = false,
                    ImageWidths  = new List<nuint> { 16 },
                };
                LZ77Params lz77Ans       = new LZ77Params { MinSymbol = 224, NonserializedDistanceContext = 2 };
                List<List<Token>> outAns = EncLz77.ApplyLZ77(hpAns, 2, streams, lz77Ans);

                outAns.Count.ShouldBeGreaterThan(0);

                HistogramParams hpHuff = new HistogramParams
                {
                    Lz77Method   = method,
                    ForceHuffman = true,
                };
                LZ77Params lz77Huff       = new LZ77Params { MinSymbol = 512, NonserializedDistanceContext = 2 };
                List<List<Token>> outHuff = EncLz77.ApplyLZ77(hpHuff, 2, streams, lz77Huff);

                outHuff.Count.ShouldBeGreaterThan(0);
            }
        }

        /// <summary>
        /// Verifies <see cref="Butteraugli"/> in-place, diffmap, small-image, odd-dimension large-image, and heatmap paths.
        /// </summary>
        [Test]
        public void Butteraugli_InPlaceAndDiffmap_CoversSmallLargeAndEdgeCases()
        {
            JxlMemoryManager mm        = JxlMemoryManager.Default;
            ButteraugliParams bp       = ButteraugliParams.Default;
            using BlurTemp blurTemp    = new BlurTemp();
            using ImagePlane<float> p8 = ImagePlane<float>.Create(mm, 8, 4).Value;
            Status trOk                = blurTemp.GetTransposed(p8, out ImagePlane<float> _);

            trOk.IsOk.ShouldBeTrue();

            using Image3<float> empty0 = new Image3<float>();
            using Image3<float> smallA = CreateCheckerImage3(mm, 4, 4, 0.1f, 0.5f);
            using Image3<float> smallB = CreateCheckerImage3(mm, 4, 4, 0.2f, 0.7f);
            using Image3<float> largeA = CreateCheckerImage3(mm, 17, 17, 0.15f, 0.65f);
            using Image3<float> largeB = CreateCheckerImage3(mm, 17, 17, 0.25f, 0.85f);
            ImagePlane<float> dmDummy  = new ImagePlane<float>();
            ImagePlane<float> dmSmall  = new ImagePlane<float>();
            ImagePlane<float> dmLarge  = new ImagePlane<float>();

            Butteraugli.ButteraugliInterfaceInPlace(empty0, empty0, in bp, ref dmDummy, out double _).IsOk.ShouldBeFalse();
            Butteraugli.ButteraugliInterfaceInPlace(smallA, largeA, in bp, ref dmDummy, out double _).IsOk.ShouldBeFalse();

            Status smallInPlaceOk = Butteraugli.ButteraugliInterfaceInPlace(smallA, smallB, in bp, ref dmSmall, out double scoreSmall);

            smallInPlaceOk.IsOk.ShouldBeTrue();
            scoreSmall.ShouldBeGreaterThanOrEqualTo(0.0);
            dmSmall.Dispose();

            Status largeInPlaceOk = Butteraugli.ButteraugliInterfaceInPlace(largeA, largeB, in bp, ref dmLarge, out double scoreLarge);

            largeInPlaceOk.IsOk.ShouldBeTrue();
            scoreLarge.ShouldBeGreaterThanOrEqualTo(0.0);
            dmLarge.Dispose();

            using Image3<float> refSmall = CreateCheckerImage3(mm, 4, 4, 0.1f, 0.4f);
            using Image3<float> cmpSmall = CreateCheckerImage3(mm, 4, 4, 0.2f, 0.6f);
            ImagePlane<float> diffSmall  = new ImagePlane<float>();

            Butteraugli.ButteraugliDiffmap(empty0, empty0, in bp, ref dmDummy).IsOk.ShouldBeFalse();
            Butteraugli.ButteraugliDiffmap(refSmall, largeA, in bp, ref dmDummy).IsOk.ShouldBeFalse();

            Status diffSmallOk = Butteraugli.ButteraugliDiffmap(refSmall, cmpSmall, in bp, ref diffSmall);

            diffSmallOk.IsOk.ShouldBeTrue();

            using ImagePlane<float> hmInput = CreateHeatmapScorePlane(mm);
            StatusOr<Image3<float>> hmOr    = Butteraugli.CreateHeatMapImage(hmInput, 1.0, 5.0);

            hmOr.IsOk.ShouldBeTrue();
            hmOr.Value.Dispose();
            diffSmall.Dispose();
            dmDummy.Dispose();
        }

        /// <summary>
        /// Verifies <see cref="ButteraugliComparator"/>, <see cref="JxlButteraugliComparator"/>, and <see cref="EncComparator"/> alpha blending.
        /// </summary>
        [Test]
        public void ButteraugliComparator_AndJxlButteraugliComparator_CoversAllMethods()
        {
            JxlMemoryManager mm                      = JxlMemoryManager.Default;
            ButteraugliParams bp                     = ButteraugliParams.Default;
            using Image3<float> img4A                = CreateCheckerImage3(mm, 4, 4, 0.1f, 0.5f);
            using Image3<float> img4B                = CreateCheckerImage3(mm, 4, 4, 0.2f, 0.6f);
            using Image3<float> img16A               = CreateCheckerImage3(mm, 16, 16, 0.1f, 0.5f);
            using Image3<float> img16B               = CreateCheckerImage3(mm, 16, 16, 0.3f, 0.8f);
            StatusOr<ButteraugliComparator> comp4Or  = ButteraugliComparator.Make(img4A, in bp);
            StatusOr<ButteraugliComparator> comp16Or = ButteraugliComparator.Make(img16A, in bp);

            comp4Or.IsOk.ShouldBeTrue();
            comp16Or.IsOk.ShouldBeTrue();

            using ButteraugliComparator comp4  = comp4Or.Value;
            using ButteraugliComparator comp16 = comp16Or.Value;
            ImagePlane<float> dm4              = ImagePlane<float>.Create(mm, 4, 4).Value;
            ImagePlane<float> dm16             = ImagePlane<float>.Create(mm, 16, 16).Value;

            comp4.Diffmap(img4B, ref dm4).IsOk.ShouldBeTrue();
            comp4.DiffmapOpsinDynamicsImage(img4B, out ImagePlane<float> dmOpsin4).IsOk.ShouldBeTrue();
            comp4.Mask(out ImagePlane<float> mask4).IsOk.ShouldBeTrue();
            comp16.Diffmap(img16B, ref dm16).IsOk.ShouldBeTrue();
            comp16.Mask(out ImagePlane<float> mask16).IsOk.ShouldBeTrue();

            ExerciseJxlButteraugliComparatorAndAlphaScore(mm, bp, img16A, img16B, dm16);

            dm4.Dispose();
            dmOpsin4.Dispose();
            mask4.Dispose();
            dm16.Dispose();
            mask16.Dispose();
        }

        /// <summary>
        /// Verifies <see cref="StaticPropRange"/> and <see cref="TreeSamples"/> across all predictor and property modes.
        /// </summary>
        [Test]
        public void StaticPropRange_AndTreeSamples_CoversAllPredictorsAndProperties()
        {
            StaticPropRange r0 = new StaticPropRange { ChanMin = 1, ChanMax = 5, GroupMin = 0, GroupMax = 2 };
            StaticPropRange r1 = new StaticPropRange { ChanMin = 1, ChanMax = 5, GroupMin = 0, GroupMax = 2 };
            StaticPropRange r2 = new StaticPropRange { ChanMin = 2, ChanMax = 6, GroupMin = 1, GroupMax = 3 };

            r2[0]    = (3u, 7u);
            r2[1]    = (4u, 8u);
            r2[0, 0] = 3u;
            r2[0, 1] = 7u;
            r2[1, 0] = 4u;
            r2[1, 1] = 8u;

            r0[0].Min.ShouldBe(1u);
            r0[1].Max.ShouldBe(2u);
            r2[0, 0].ShouldBe(3u);
            r2[1, 1].ShouldBe(8u);
            (r0 == r1).ShouldBeTrue();
            (r0 != r2).ShouldBeTrue();
            r0.Equals(r1).ShouldBeTrue();
            r0.Equals((object)r1).ShouldBeTrue();
            r0.Equals((object?)null).ShouldBeFalse();
            r0.GetHashCode().ShouldBe(r1.GetHashCode());
            r0.CompareTo(r1).ShouldBe(0);
            r0.CompareTo(r2).ShouldBeLessThan(0);

            ModularOptions.TreeMode[] modes =
            {
                ModularOptions.TreeMode.GradientOnly,
                ModularOptions.TreeMode.WPOnly,
                ModularOptions.TreeMode.NoWP,
                ModularOptions.TreeMode.Default,
            };
            Predictor[] preds =
            {
                Predictor.Undefined,
                Predictor.Best,
                Predictor.Variable,
                Predictor.Zero,
                Predictor.Left,
                Predictor.Gradient,
                Predictor.Weighted,
            };

            foreach (ModularOptions.TreeMode mode in modes)
            {
                foreach (Predictor pred in preds)
                {
                    TreeSamples ts = new TreeSamples();
                    Status predOk  = ts.SetPredictor(pred, mode);
                    Status propsOk = ts.SetProperties(new List<uint> { 0, 1 }, mode);

                    if (mode == ModularOptions.TreeMode.NoWP && pred == Predictor.Weighted)
                    {
                        predOk.IsOk.ShouldBeFalse();
                    }
                    else
                    {
                        predOk.IsOk.ShouldBeTrue();
                    }

                    propsOk.IsOk.ShouldBeTrue();
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="EncDebugTree.FormatTreeDot"/> and <see cref="EncDebugTree.PrintTree"/>.
        /// </summary>
        [Test]
        public void EncDebugTree_PrintTreeAndFormatTreeDot_CoversSplitAndLeafNodes()
        {
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(2, 10, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Gradient, 0, 1),
                PropertyDecisionNode.Split(16, 0, 3, 4),
                PropertyDecisionNode.Leaf(Predictor.Weighted, 2, 1),
                PropertyDecisionNode.Leaf(Predictor.Zero, 0, 1),
            };
            string dot      = EncDebugTree.FormatTreeDot(tree);
            string baseName = Path.Combine(Path.GetTempPath(), "jxl_test_tree_" + Guid.NewGuid().ToString("N"));
            string dotPath  = baseName + ".dot";

            dot.ShouldContain("digraph Tree");
            dot.ShouldContain("Prev0-0");

            try
            {
                EncDebugTree.PrintTree(tree, baseName);
                File.Exists(dotPath).ShouldBeTrue();
            }
            finally
            {
                if (File.Exists(dotPath))
                {
                    File.Delete(dotPath);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="EncRct"/>, <see cref="EncSqueeze"/>, <see cref="EncPalette"/>, and <see cref="EncTransform"/>.
        /// </summary>
        [Test]
        public void EncRct_EncSqueeze_EncPalette_EncTransform_CoversAllForwardTransforms()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;

            using (ModularImage img0 = CreateTestModularImage(mm, 8, 8, 3, 8))
            {
                EncRct.FwdRct(img0, 0, 0, null).IsOk.ShouldBeFalse();
                EncRct.FwdRct(img0.Channels, img0.Channels, 0, null).IsOk.ShouldBeTrue();
                EncRct.FwdRct(img0, 0, 42, null).IsOk.ShouldBeFalse();
            }

            for (nuint rctType = 1; rctType < 42; rctType++)
            {
                using ModularImage img = CreateTestModularImage(mm, 8, 8, 3, 8);
                Status rctOk           = EncRct.FwdRct(img, 0, rctType, null);

                rctOk.IsOk.ShouldBeTrue();
            }

            using ModularImage sqImg  = CreateTestModularImage(mm, 9, 9, 3, 8);
            List<SqueezeParams> sqOne = new List<SqueezeParams>
            {
                new SqueezeParams { Horizontal = true, InPlace = true, BeginC = 0, NumC = 3 },
            };

            EncSqueeze.FwdSqueeze(sqImg, new List<SqueezeParams>(), null).IsOk.ShouldBeTrue();
            EncSqueeze.FwdSqueeze(sqImg, sqOne, null).IsOk.ShouldBeTrue();

            ExerciseForwardPaletteVariants(mm);
        }

        /// <summary>
        /// Verifies <see cref="EncModularEncoding.PredefinedTree"/>, <see cref="EncModularEncoding.ModularCompress"/>,
        /// and <see cref="EncModularEncoding.ModularGenericCompress"/> across all <see cref="ModularOptions.TreeKind"/> values.
        /// </summary>
        [Test]
        public void EncModularEncoding_PredefinedTreesAndModularCompress_CoversAllTreeKinds()
        {
            JxlMemoryManager mm          = JxlMemoryManager.Default;
            using ModularImage emptyImg  = CreateTestModularImage(mm, 2, 2, 1, 8);
            using ModularImage zeroDim   = new ModularImage(mm);
            using ModularImage fourChImg = CreateTestModularImage(mm, 8, 8, 4, 8);

            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.JpegTranscodeACMeta, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.TrivialTreeNoPredictor, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.FalconACMeta, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.ACMeta, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.ACMeta, 2048, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.WPFixedDC, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.GradientFixedDC, 512, 8, 0).Count.ShouldBeGreaterThan(0);
            EncModularEncoding.PredefinedTree(ModularOptions.TreeKind.GradientFixedDC, 512, 8, 2).Count.ShouldBeGreaterThan(0);

            ModularOptions opts             = ModularOptions.Default();
            GroupHeader header              = new GroupHeader();
            List<Token> tokens              = new List<Token>();
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode> { PropertyDecisionNode.Leaf(Predictor.Zero) };

            emptyImg.Error = true;
            EncModularEncoding.ModularCompress(emptyImg, opts, 0, tree, header, tokens, out nuint _).IsOk.ShouldBeFalse();
            EncModularEncoding.ModularCompress(zeroDim, opts, 0, tree, header, tokens, out nuint _).IsOk.ShouldBeTrue();

            ModularOptions zeroOpts = ModularOptions.Default();

            zeroOpts.ZeroTokens = true;
            EncModularEncoding.ModularCompress(fourChImg, zeroOpts, 0, tree, header, tokens, out nuint _).IsOk.ShouldBeTrue();

            BitWriter bwLearn        = new BitWriter(mm);
            BitWriter bwFixed        = new BitWriter(mm);
            ModularOptions fixedOpts = ModularOptions.Default();

            fixedOpts.Kind = ModularOptions.TreeKind.WPFixedDC;
            EncModularEncoding.ModularGenericCompress(fourChImg, opts, bwLearn).IsOk.ShouldBeTrue();
            EncModularEncoding.ModularGenericCompress(fourChImg, fixedOpts, bwFixed).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameEncoder"/> lossy modular, float16/float32, 24-bit integer, and VarDCT metadata helpers.
        /// </summary>
        [Test]
        public void ModularFrameEncoder_LossyFloatAndVarDctHelpers_CoversRemainingPaths()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;

            EncodeFrameWithSettings(mm, 16, modular: true, distance: 2.0f, ColorTransform.None, responsive: 1);
            EncodeFrameWithSettings(mm, 16, modular: true, distance: 2.0f, ColorTransform.XYB, responsive: 0, manualXyb: true);
            EncodeCustomBitDepthModularFrame(mm, 16, 5, isFloat: true);
            EncodeCustomBitDepthModularFrame(mm, 32, 8, isFloat: true);
            EncodeCustomBitDepthModularFrame(mm, 24, 0, isFloat: false);

            ExerciseModularVarDctDcAndAcMetadata(mm);
        }

        /// <summary>
        /// Verifies <see cref="EncFastLossless"/> palette, 16-bit big/little endian, streaming, chunked output, and large headers.
        /// </summary>
        [Test]
        public void EncFastLossless_PaletteAnd16BitAndStreaming_CoversAllBranches()
        {
            ExerciseFastLosslessPaletteAnd16Bit();
            ExerciseFastLosslessStreamingAndChunkedOutput();
        }

        /// <summary>
        /// Verifies <see cref="JxlEncoder"/> options, float options, boxes, extra channels, stats, and frame index box.
        /// </summary>
        [Test]
        public void JxlEncoder_OptionsBoxesAndFrameSettings_CoversApiSurface()
        {
            ExerciseJxlEncoderStatsAndOptions();
            ExerciseJxlEncoderBoxesExtraChannelsAndIndexBox();
        }

        /// <summary>
        /// Verifies <see cref="EncFrame"/> progressive modes, downsampling, extra channel resampling, and JPEG transcoding.
        /// </summary>
        [Test]
        public void EncFrame_ProgressiveDownsamplingAndJpegTranscode_CoversAllSections()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            DequantMatrices mat = new DequantMatrices();
            Quantizer quant     = new Quantizer(mat);
            float[] blk         = new float[64];
            int[] qblk          = new int[64];

            qblk[0] = 5;
            qblk[1] = 2;
            blk[1]  = 0.1f;

            EncGroup.AdjustQuantBlockAC(quant, AcStrategyType.DCT, 1, 1, blk, qblk);
            qblk[0].ShouldBe(0);
            qblk[1].ShouldBe(0);

            EncodeResampledAndProgressiveFrames(mm);
            EncodeJpegTranscodeFrames(mm);
        }

        /// <summary>
        /// Verifies <see cref="EncJpegHuffmanDecode"/>, <see cref="EncJpegDataReader"/>, and <see cref="EncJpegData"/>.
        /// </summary>
        [Test]
        public void EncJpeg_HuffmanReaderAndData_CoversBaselineAndProgressiveJpeg()
        {
            ExerciseJpegHuffmanTableBuilder();
            ExerciseJpegDataReaderAndEncJpegData();
        }

        /// <summary>
        /// Verifies <see cref="ProgressiveSplitter"/>, <see cref="HistogramParams"/>, <see cref="EncQuantWeights"/>,
        /// <see cref="EncCoeffOrder"/>, <see cref="EncCluster"/>, <see cref="EncContextMap"/>, <see cref="EncAns"/>,
        /// and <see cref="EncExternalImage"/>.
        /// </summary>
        [Test]
        public void EncParams_AndQuantWeights_AndHeuristics_CoversRemainingMethods()
        {
            ExerciseProgressiveSplitterAndHistogramParams();
            ExerciseQuantWeightsAndCoeffOrders();
            ExerciseClusterContextMapAndAns();
            ExerciseXybExternalImageAndDebugImage();
        }

        private static Spline CreateSampleSpline(float startX, float startY, float dctVal)
        {
            Spline s = new Spline();

            s.ControlPoints.Add(new Spline.Point(startX, startY));
            s.ControlPoints.Add(new Spline.Point(startX + 3.0f, startY + 2.0f));
            s.ControlPoints.Add(new Spline.Point(startX + 7.0f, startY + 5.0f));
            s.ControlPoints.Add(new Spline.Point(startX + 10.0f, startY + 9.0f));

            for (int c = 0; c < 3; c++)
            {
                s.ColorDct[c][0] = dctVal;
                s.ColorDct[c][1] = dctVal * 0.5f;
            }

            s.SigmaDct[0] = 0.5f;

            return s;
        }

        private static QuantizedPatch CreateQuantizedPatch(nuint xsize, nuint ysize, sbyte fillVal)
        {
            QuantizedPatch p = new QuantizedPatch
            {
                XSize = xsize,
                YSize = ysize,
            };

            for (int c = 0; c < 3; c++)
            {
                Array.Fill(p.Pixels[c], fillVal);
                p.FPixels[c][0] = fillVal * 0.1f;
            }

            return p;
        }

        private static Image3<float> CreateCheckerImage3(
            JxlMemoryManager mm,
            nuint xsize,
            nuint ysize,
            float valA,
            float valB)
        {
            Image3<float> img = Image3<float>.Create(mm, xsize, ysize).Value;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < ysize; y++)
                {
                    Span<float> row = img.Plane(c).RowSpan(y);

                    for (nuint x = 0; x < xsize; x++)
                    {
                        row[(int)x] = ((x + y + c) & 1) == 0 ? valA : valB;
                    }
                }
            }

            return img;
        }

        private static Image3<float> CreateGentleNoisyImage3(JxlMemoryManager mm, nuint xsize, nuint ysize)
        {
            Image3<float> img = Image3<float>.Create(mm, xsize, ysize).Value;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < ysize; y++)
                {
                    Span<float> row = img.Plane(c).RowSpan(y);

                    for (nuint x = 0; x < xsize; x++)
                    {
                        float scale = (x < 8 && y < 8) ? 0.002f : 0.008f;
                        float delta = ((int)((x * 7) + (y * 13) + c) % 5 - 2) * scale;

                        row[(int)x] = 0.25f + delta;
                    }
                }
            }

            return img;
        }

        private static PatchDictionary CreateMultiRefPatchDictionary()
        {
            PatchDictionary pdic                      = new PatchDictionary(JxlMemoryManager.Default);
            List<PatchReferencePosition> refPositions = new List<PatchReferencePosition>
            {
                new PatchReferencePosition { Ref = 0, X0 = 1, Y0 = 1, XSize = 4, YSize = 4 },
                new PatchReferencePosition { Ref = 1, X0 = 2, Y0 = 2, XSize = 4, YSize = 4 },
            };
            List<PatchPosition> positions = new List<PatchPosition>
            {
                new PatchPosition { X = 2, Y = 2, RefPosIdx = 0 },
                new PatchPosition { X = 6, Y = 6, RefPosIdx = 0 },
                new PatchPosition { X = 4, Y = 8, RefPosIdx = 1 },
            };
            List<PatchBlending> blendings = new List<PatchBlending>
            {
                new PatchBlending { Mode = PatchBlendMode.kAdd, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kBlendAbove, AlphaChannel = 1, Clamp = true },
                new PatchBlending { Mode = PatchBlendMode.kReplace, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kAdd, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kBlendBelow, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kMul, AlphaChannel = 0, Clamp = true },
                new PatchBlending { Mode = PatchBlendMode.kReplace, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kAdd, AlphaChannel = 0, Clamp = false },
                new PatchBlending { Mode = PatchBlendMode.kNone, AlphaChannel = 0, Clamp = false },
            };

            pdic.SetPositions(positions, refPositions, blendings, 3);

            return pdic;
        }

        private static CodecMetadata CreateBasicMetadata(uint xsize, uint ysize, bool isGray, bool hasAlpha)
        {
            CodecMetadata meta = new CodecMetadata();

            meta.M.SetUintSamples(8);
            meta.M.ColorEncoding = ColorEncoding.SRGB(isGray);
            meta.Size.Set(xsize, ysize);

            if (hasAlpha)
            {
                ExtraChannelInfo ec = new ExtraChannelInfo
                {
                    Type            = ExtraChannel.Alpha,
                    AlphaAssociated = false,
                };

                ec.BitDepth.BitsPerSample = 8;
                meta.M.ExtraChannelInfo.Add(ec);
                meta.M.NumExtraChannels = 1;
            }

            return meta;
        }

        private static List<List<Token>> BuildCompressibleTokenStreams()
        {
            List<Token> s0 = new List<Token>(512);

            for (int i = 0; i < 64; i++)
            {
                s0.Add(new Token(0, 0));
            }

            uint[] pattern = { 1024, 1025, 1026, 1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034, 1035, 1036, 1037, 1038, 1039 };

            for (int rep = 0; rep < 20; rep++)
            {
                for (int j = 0; j < pattern.Length; j++)
                {
                    s0.Add(new Token((uint)(j & 1), pattern[j]));
                }
            }

            for (int i = 0; i < 32; i++)
            {
                s0.Add(new Token(1, 0));
            }

            for (int i = 0; i < 64; i++)
            {
                s0.Add(new Token(0, 2048));
            }

            return new List<List<Token>> { s0 };
        }

        private static ImagePlane<float> CreateHeatmapScorePlane(JxlMemoryManager mm)
        {
            float[] scores       = { 0.0f, 0.3f, 0.7f, 1.1f, 1.6f, 2.2f, 3.5f, 6.0f, 12.0f, 25.0f, 60.0f, 120.0f };
            ImagePlane<float> pl = ImagePlane<float>.Create(mm, (nuint)scores.Length, 1).Value;
            Span<float> row      = pl.RowSpan(0);

            for (int i = 0; i < scores.Length; i++)
            {
                row[i] = scores[i];
            }

            return pl;
        }

        private static void ExerciseJxlButteraugliComparatorAndAlphaScore(
            JxlMemoryManager mm,
            ButteraugliParams bp,
            Image3<float> img16A,
            Image3<float> img16B,
            ImagePlane<float> dm16)
        {
            JxlButteraugliComparator jbc = new JxlButteraugliComparator(in bp, JxlCms.GetDefaultCms());
            ImagePlane<float>? nullDm    = null;
            ImagePlane<float>? refDm     = dm16;
            float score                  = 0.0f;

            jbc.CompareWith(new ImageBundle(mm), ref nullDm, out score).IsOk.ShouldBeFalse();
            jbc.Mask(out ImagePlane<float> maskOut).IsOk.ShouldBeFalse();
            jbc.DiffmapOpsinDynamicsImage(img16B, out ImagePlane<float> dmOut).IsOk.ShouldBeFalse();

            jbc.GoodQualityScore().ShouldBeGreaterThan(0.0f);
            jbc.BadQualityScore().ShouldBeGreaterThan(jbc.GoodQualityScore());

            CodecMetadata meta16     = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: true);
            CodecMetadata meta4      = CreateBasicMetadata(4, 4, isGray: false, hasAlpha: false);
            using ImageBundle ib16A  = new ImageBundle(mm, meta16.M);
            using ImageBundle ib16B  = new ImageBundle(mm, meta16.M);
            using ImageBundle ib4    = new ImageBundle(mm, meta4.M);
            ImagePlane<float> alphaA = ImagePlane<float>.Create(mm, 16, 16).Value;
            ImagePlane<float> alphaB = ImagePlane<float>.Create(mm, 16, 16).Value;

            ib16A.SetFromImage(CreateCheckerImage3(mm, 16, 16, 0.1f, 0.5f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ib16B.SetFromImage(CreateCheckerImage3(mm, 16, 16, 0.2f, 0.7f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ib4.SetFromImage(CreateCheckerImage3(mm, 4, 4, 0.1f, 0.5f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ImageOps.FillImage(0.5f, alphaA);
            ImageOps.FillImage(0.8f, alphaB);
            ib16A.SetAlpha(alphaA).IsOk.ShouldBeTrue();
            ib16B.SetAlpha(alphaB).IsOk.ShouldBeTrue();

            jbc.SetReferenceImage(ib16A).IsOk.ShouldBeTrue();
            jbc.SetLinearReferenceImage(img16A).IsOk.ShouldBeTrue();
            jbc.Mask(out maskOut).IsOk.ShouldBeTrue();
            maskOut.Dispose();
            jbc.DiffmapOpsinDynamicsImage(img16B, out dmOut).IsOk.ShouldBeTrue();
            dmOut.Dispose();
            jbc.CompareWith(ib4, ref refDm, out score).IsOk.ShouldBeFalse();
            jbc.CompareWith(ib16B, ref refDm, out score).IsOk.ShouldBeTrue();

            EncComparator.ComputeScore(ib16A, ib16B, jbc, JxlCms.GetDefaultCms(), out score, ref refDm, (null, false)).IsOk.ShouldBeTrue();

            meta16.M.ExtraChannelInfo[0].AlphaAssociated = true;
            EncComparator.ComputeScore(ib16A, ib16B, jbc, JxlCms.GetDefaultCms(), out score, ref refDm, (null, false)).IsOk.ShouldBeTrue();
        }

        private static ModularImage CreateTestModularImage(
            JxlMemoryManager mm,
            nuint xsize,
            nuint ysize,
            int numChannels,
            int bitdepth)
        {
            ModularImage img = ModularImage.Create(mm, xsize, ysize, bitdepth, numChannels).Value;

            for (int c = 0; c < numChannels; c++)
            {
                for (nuint y = 0; y < ysize; y++)
                {
                    Span<int> row = img.Channels[c].Row(y);

                    for (nuint x = 0; x < xsize; x++)
                    {
                        row[(int)x] = (int)(((x + y + (nuint)c) % 4) * 20);
                    }
                }
            }

            return img;
        }

        private static void ExerciseForwardPaletteVariants(JxlMemoryManager mm)
        {
            WeightedHeader wh              = new WeightedHeader();
            using ModularImage oneChLookup = CreateTestModularImage(mm, 8, 8, 1, 8);
            uint nbColors                  = 16;
            uint nbDeltas                  = 0;
            Predictor pred                 = Predictor.Zero;

            EncPalette.FwdPalette(oneChLookup, new PaletteTransformConfig(0, 0, true, false, wh), ref nbColors, ref nbDeltas, ref pred).IsOk.ShouldBeTrue();

            using ModularImage oneChSet = CreateTestModularImage(mm, 8, 8, 1, 24);

            oneChSet.Channels[0].Row(0)[0] = 0;
            oneChSet.Channels[0].Row(0)[1] = 100000;
            nbColors                       = 16;
            nbDeltas                       = 0;
            pred                           = Predictor.Zero;
            EncPalette.FwdPalette(oneChSet, new PaletteTransformConfig(0, 0, true, false, wh), ref nbColors, ref nbDeltas, ref pred).IsOk.ShouldBeTrue();

            using ModularImage threeChOrd = CreateTestModularImage(mm, 8, 8, 3, 8);

            nbColors = 16;
            nbDeltas = 0;
            pred     = Predictor.Zero;
            EncPalette.FwdPalette(threeChOrd, new PaletteTransformConfig(0, 2, true, false, wh), ref nbColors, ref nbDeltas, ref pred).IsOk.ShouldBeTrue();

            using ModularImage threeChUnord = CreateImplicitColorModularImage(mm);

            nbColors = 16;
            nbDeltas = 0;
            pred     = Predictor.Zero;
            EncPalette.FwdPalette(threeChUnord, new PaletteTransformConfig(0, 2, false, false, wh), ref nbColors, ref nbDeltas, ref pred).IsOk.ShouldBeTrue();

            using ModularImage threeChLossy = CreateTestModularImage(mm, 8, 8, 3, 8);

            nbColors = 8;
            nbDeltas = 0;
            pred     = Predictor.Zero;
            EncPalette.FwdPalette(threeChLossy, new PaletteTransformConfig(0, 2, false, true, wh), ref nbColors, ref nbDeltas, ref pred).IsOk.ShouldBeTrue();

            using ModularImage trImg = CreateTestModularImage(mm, 8, 8, 3, 8);
            Transform badTr          = new Transform(TransformId.Invalid);
            Transform rctTr          = new Transform(TransformId.RCT) { BeginC = 0, RctType = 6 };

            EncTransform.TransformForward(badTr, trImg, wh, null).IsOk.ShouldBeFalse();
            EncTransform.TransformForward(rctTr, trImg, wh, null).IsOk.ShouldBeTrue();
        }

        private static ModularImage CreateImplicitColorModularImage(JxlMemoryManager mm)
        {
            ModularImage img = ModularImage.Create(mm, 8, 8, 8, 3).Value;

            for (nuint y = 0; y < 8; y++)
            {
                for (nuint x = 0; x < 8; x++)
                {
                    int val = y < 3 ? 0 : (y < 6 ? 255 : (int)(x * 10));

                    for (int c = 0; c < 3; c++)
                    {
                        img.Channels[c].Row(y)[(int)x] = val;
                    }
                }
            }

            return img;
        }

        private static void EncodeFrameWithSettings(
            JxlMemoryManager mm,
            uint size,
            bool modular,
            float distance,
            ColorTransform ct,
            int responsive,
            bool manualXyb = false)
        {
            CodecMetadata meta       = CreateBasicMetadata(size, size, isGray: false, hasAlpha: true);
            using ImageBundle ib     = new ImageBundle(mm, meta.M);
            ImagePlane<float> alpha  = ImagePlane<float>.Create(mm, size, size).Value;

            ib.SetFromImage(CreateCheckerImage3(mm, size, size, 0.2f, 0.8f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ImageOps.FillImage(0.75f, alpha);
            ib.SetAlpha(alpha).IsOk.ShouldBeTrue();

            CompressParams cp = new CompressParams
            {
                ModularMode         = modular,
                ButteraugliDistance = distance,
                ColorTransform      = ct,
                Responsive          = responsive,
                EcDistance          = new List<float> { -1.0f },
            };

            if (manualXyb)
            {
                cp.ManualXybFactors = new List<float> { 1.0f, 1.0f, 1.0f };
            }

            BitWriter bw = new BitWriter(mm);
            AuxOut aux   = new AuxOut();
            Status ok    = EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, aux));

            ok.IsOk.ShouldBeTrue();
        }

        private static void EncodeCustomBitDepthModularFrame(
            JxlMemoryManager mm,
            uint bitsPerSample,
            uint expBits,
            bool isFloat)
        {
            CodecMetadata meta = new CodecMetadata();

            if (isFloat)
            {
                meta.M.BitDepth.FloatingPointSample   = true;
                meta.M.BitDepth.BitsPerSample         = bitsPerSample;
                meta.M.BitDepth.ExponentBitsPerSample = expBits;
            }
            else
            {
                meta.M.SetUintSamples(bitsPerSample);
            }

            meta.M.ColorEncoding = ColorEncoding.SRGB(isGray: false);
            meta.Size.Set(8, 8);

            using ImageBundle ib = new ImageBundle(mm, meta.M);

            ib.SetFromImage(CreateCheckerImage3(mm, 8, 8, -0.25f, 0.75f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            CompressParams cp = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
            };
            BitWriter bw = new BitWriter(mm);

            EncFrame.EncodeFrame(mm, cp, new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, null)).IsOk.ShouldBeTrue();
        }

        private static void ExerciseModularVarDctDcAndAcMetadata(JxlMemoryManager mm)
        {
            CodecMetadata meta = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: false);
            FrameHeader fh     = new FrameHeader(meta);
            SpeedTier[] tiers  = { SpeedTier.kSquirrel, SpeedTier.kFalcon, SpeedTier.kThunder, SpeedTier.kLightning };

            fh.FrameSize.XSize = 16;
            fh.FrameSize.YSize = 16;
            fh.Encoding        = FrameEncoding.VarDCT;

            foreach (SpeedTier tier in tiers)
            {
                CompressParams cp              = new CompressParams { SpeedTier = tier, ButteraugliDistance = 1.5f };
                using PassesEncoderState state = new PassesEncoderState(mm) { CParams = cp };

                PassesSharedState.InitializePassesSharedState(fh, state.Shared, encoder: true).IsOk.ShouldBeTrue();
                state.Shared.AcStrategy.Set(0, 0, AcStrategyType.DCT);
                state.Shared.AcStrategy.Set(1, 0, AcStrategyType.DCT);
                state.Shared.AcStrategy.Set(0, 1, AcStrategyType.DCT);
                state.Shared.AcStrategy.Set(1, 1, AcStrategyType.DCT);

                using ModularFrameEncoder mfe  = ModularFrameEncoder.Create(mm, fh, cp, streamingMode: false).Value;
                using Image3<float> dc         = CreateCheckerImage3(mm, 2, 2, 0.1f, 0.5f);
                Rect dcRect                    = new Rect(0, 0, 2, 2);

                mfe.AddVarDCTDC(fh, dc, dcRect, 0, nlDc: true, state, jpegTranscode: false).IsOk.ShouldBeTrue();
                mfe.AddACMetadata(dcRect, 0, jpegTranscode: false, state).IsOk.ShouldBeTrue();
                mfe.ClearStreamData(ModularStreamId.VarDCTDC(0));
                mfe.ClearModularStreamData();
                ((ulong)mfe.ComputeStreamingAbsoluteAcGroupId(0, 0, state.Shared.FrameDim)).ShouldBe(0UL);
            }
        }

        private static void ExerciseFastLosslessPaletteAnd16Bit()
        {
            byte[] palPixels = new byte[64 * 64 * 4];

            for (int i = 0; i < 64 * 64; i++)
            {
                byte v = (byte)((i & 7) * 10);

                palPixels[(i * 4) + 0] = v;
                palPixels[(i * 4) + 1] = (byte)(v + 1);
                palPixels[(i * 4) + 2] = (byte)(v + 2);
                palPixels[(i * 4) + 3] = 255;
            }

            for (nuint nbChans = 1; nbChans <= 4; nbChans++)
            {
                StatusOr<byte[]> encoded = EncFastLossless.FastLosslessEncode(
                    palPixels,
                    (64, 64 * nbChans, 64, nbChans, 8),
                    bigEndian: false,
                    effort: 2);

                encoded.IsOk.ShouldBeTrue();
                encoded.Value.Length.ShouldBeGreaterThan(0);
            }

            byte[] manyColors = new byte[64 * 64 * 3];

            for (int i = 0; i < manyColors.Length; i++)
            {
                manyColors[i] = (byte)((i * 37) & 0xFF);
            }

            StatusOr<byte[]> fallbackEnc = EncFastLossless.FastLosslessEncode(manyColors, (64, 64 * 3, 64, 3, 8), false, effort: 2);

            fallbackEnc.IsOk.ShouldBeTrue();
            fallbackEnc.Value.Length.ShouldBeGreaterThan(0);

            nuint[] bitdepths = { 10, 12, 14, 16 };
            byte[] u16Pixels  = new byte[16 * 16 * 8];

            for (int i = 0; i < u16Pixels.Length; i++)
            {
                u16Pixels[i] = (byte)(i & 0x3F);
            }

            foreach (nuint bd in bitdepths)
            {
                for (nuint ch = 1; ch <= 4; ch++)
                {
                    EncFastLossless.FastLosslessEncode(u16Pixels, (16, 16 * ch * 2, 16, ch, bd), false, 1).Value.Length.ShouldBeGreaterThan(0);
                    EncFastLossless.FastLosslessEncode(u16Pixels, (16, 16 * ch * 2, 16, ch, bd), true, 2).Value.Length.ShouldBeGreaterThan(0);
                }
            }
        }

        private static void ExerciseFastLosslessStreamingAndChunkedOutput()
        {
            byte[] largeImg = new byte[260 * 260 * 3];

            for (int i = 0; i < largeImg.Length; i++)
            {
                largeImg[i] = (byte)((i & 3) * 20);
            }

            fixed (byte* pLarge = largeImg)
            {
                JxlChunkedFrameInputSource input = EncFastLossless.CreateMemoryInputSource(pLarge, 260 * 3, 3, 8);
                FastLosslessPrepareArgs prepArgs = new FastLosslessPrepareArgs(input, 260, 260, 3, 8, false, 2, false);
                JxlFastLosslessFrameState state  = EncFastLossless.JxlFastLosslessPrepareFrame(in prepArgs)!;

                EncFastLossless.JxlFastLosslessProcessFrame(state, true, null, null, null).ShouldBeTrue();
                ((ulong)EncFastLossless.JxlFastLosslessMaxRequiredOutput(state)).ShouldBeGreaterThan(0UL);

                Span<byte> chunk  = stackalloc byte[64];
                nuint totalCopied = 0;

                while (true)
                {
                    nuint written = EncFastLossless.JxlFastLosslessWriteOutput(state, chunk);

                    if (written == 0)
                    {
                        break;
                    }

                    totalCopied += written;
                }

                ((ulong)totalCopied).ShouldBeGreaterThan(0UL);

                TestStreamingOutputProcessor proc  = new TestStreamingOutputProcessor();
                FastLosslessPrepareArgs streamArgs = new FastLosslessPrepareArgs(input, 260, 260, 3, 8, false, 1, false);
                JxlFastLosslessFrameState streamSt = EncFastLossless.JxlFastLosslessPrepareFrame(in streamArgs)!;

                EncFastLossless.JxlFastLosslessProcessFrame(streamSt, true, null, null, proc).ShouldBeTrue();
                ((ulong)proc.TotalWritten).ShouldBeGreaterThan(0UL);

                using JxlFastLosslessFrameState hdrMed = new JxlFastLosslessFrameState
                {
                    Width    = 1200,
                    Height   = 9000,
                    NbChans  = 1,
                    Bitdepth = 8,
                };
                using JxlFastLosslessFrameState hdrHug = new JxlFastLosslessFrameState
                {
                    Width    = 300000,
                    Height   = 300000,
                    NbChans  = 1,
                    Bitdepth = 8,
                };

                EncFastLossless.JxlFastLosslessPrepareHeader(hdrMed, 1, 1);
                EncFastLossless.JxlFastLosslessPrepareHeader(hdrHug, 1, 1);
            }
        }

        private sealed class TestStreamingOutputProcessor : IJxlFastLosslessOutputProcessor
        {
            public nuint CurrentPosition { get; private set; }

            public nuint TotalWritten { get; private set; }

            public Status Seek(nuint position)
            {
                CurrentPosition = position;

                return true;
            }

            public Status AppendBytes(ReadOnlySpan<byte> data)
            {
                CurrentPosition += (nuint)data.Length;
                TotalWritten    += (nuint)data.Length;

                return true;
            }
        }

        private static void ExerciseJxlEncoderStatsAndOptions()
        {
            JxlEncoderStats statsA = new JxlEncoderStats();
            JxlEncoderStats statsB = new JxlEncoderStats();

            statsA.Set(JxlEncoderStatsKey.HeaderBits, 128);
            statsB.Set(JxlEncoderStatsKey.HeaderBits, 64);
            statsA.Merge(statsB);
            ((ulong)statsA.Get(JxlEncoderStatsKey.HeaderBits)).ShouldBe(192UL);
            statsA.Set((JxlEncoderStatsKey)999, 10);
            ((ulong)statsA.Get((JxlEncoderStatsKey)999)).ShouldBe(0UL);

            using JxlEncoder enc             = new JxlEncoder();
            JxlEncoderFrameSettings settings = enc.FrameSettingsCreate(null);

            foreach (JxlEncoderFrameSettingId id in Enum.GetValues<JxlEncoderFrameSettingId>())
            {
                _ = enc.FrameSettingsSetOption(settings, id, 1);
                _ = enc.FrameSettingsSetOption(settings, id, -999);
                _ = enc.FrameSettingsSetOption(settings, id, 999999);
            }

            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.PhotonNoise, 400.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.PhotonNoise, -1.0f).ShouldBe(JxlEncoderStatus.Error);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.PhotonNoise, float.NaN).ShouldBe(JxlEncoderStatus.Error);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.ChannelColorsGlobalPercent, 50.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.ChannelColorsGlobalPercent, 150.0f).ShouldBe(JxlEncoderStatus.Error);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.ChannelColorsGroupPercent, 50.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.ModularMaTreeLearningPercent, 50.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetFloatOption(settings, JxlEncoderFrameSettingId.Effort, 5.0f).ShouldBe(JxlEncoderStatus.Error);
        }

        private static void ExerciseJxlEncoderBoxesExtraChannelsAndIndexBox()
        {
            using JxlEncoder enc = new JxlEncoder();

            enc.AllowExpertOptions();
            enc.SetCodestreamLevel(5).ShouldBe(JxlEncoderStatus.Success);
            enc.SetCodestreamLevel(10).ShouldBe(JxlEncoderStatus.Success);
            enc.SetCodestreamLevel(-1).ShouldBe(JxlEncoderStatus.Success);
            enc.SetCodestreamLevel(99).ShouldBe(JxlEncoderStatus.Error);
            enc.SetUpsamplingMode(4, 1).ShouldBe(JxlEncoderStatus.Success);
            enc.SetUpsamplingMode(3, 1).ShouldBe(JxlEncoderStatus.Error);
            enc.StoreJPEGMetadata(true).ShouldBe(JxlEncoderStatus.Success);
            enc.UseContainer(true).ShouldBe(JxlEncoderStatus.Success);

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            JxlEncoder.JxlEncoderInitExtraChannelInfo(JxlExtraChannelType.Alpha, out JxlExtraChannelInfo ec);

            info.XSize             = 8;
            info.YSize             = 8;
            info.NumExtraChannels  = 1;
            info.AlphaBits         = 8;
            info.HaveAnimation     = true;
            info.Animation         = new JxlAnimationHeader { TpsNumerator = 10, TpsDenominator = 1 };

            enc.SetBasicInfo(in info).ShouldBe(JxlEncoderStatus.Success);
            enc.GetRequiredCodestreamLevel().ShouldBe(5);
            enc.SetColorEncoding(ColorEncoding.SRGB().ToExternal()).ShouldBe(JxlEncoderStatus.Success);
            enc.SetExtraChannelInfo(0, in ec).ShouldBe(JxlEncoderStatus.Success);
            enc.SetExtraChannelName(0, "alpha_ch").ShouldBe(JxlEncoderStatus.Success);

            enc.UseBoxes().ShouldBe(JxlEncoderStatus.Success);
            enc.AddBox("xml ", new byte[] { 1, 2, 3, 4 }, compressBox: false).ShouldBe(JxlEncoderStatus.Success);
            enc.AddBox("Exif", new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, compressBox: true).ShouldBe(JxlEncoderStatus.Success);
            enc.AddBox("jxlc", new byte[] { 1 }, compressBox: false).ShouldBe(JxlEncoderStatus.Error);
            enc.CloseBoxes();

            JxlEncoderFrameSettings fs = enc.FrameSettingsCreate(null);
            JxlEncoderStats stats      = new JxlEncoderStats();
            JxlPixelFormat pfRgb       = new JxlPixelFormat(3, JxlDataType.UInt8, JxlEndianness.NativeEndian, 0);
            JxlPixelFormat pfAlpha     = new JxlPixelFormat(1, JxlDataType.UInt8, JxlEndianness.NativeEndian, 0);
            byte[] rgbBytes            = new byte[8 * 8 * 3];
            byte[] alphaBytes          = new byte[8 * 8];
            byte[] outBuf              = new byte[16384];
            int offset                 = 0;

            JxlEncoder.JxlEncoderInitBlendInfo(out JxlBlendInfo blend);

            enc.CollectStats(fs, stats);
            enc.SetFrameName(fs, "frame0").ShouldBe(JxlEncoderStatus.Success);
            enc.SetFrameDistance(fs, 1.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.SetExtraChannelDistance(fs, 0, 0.0f).ShouldBe(JxlEncoderStatus.Success);
            enc.FrameSettingsSetOption(fs, JxlEncoderFrameSettingId.FrameIndexBox, 1).ShouldBe(JxlEncoderStatus.Success);
            enc.SetExtraChannelBlendInfo(fs, 0, in blend).ShouldBe(JxlEncoderStatus.Success);
            enc.AddImageFrame(fs, in pfRgb, rgbBytes).ShouldBe(JxlEncoderStatus.Success);
            enc.SetExtraChannelBuffer(fs, in pfAlpha, alphaBytes, 0).ShouldBe(JxlEncoderStatus.Success);
            enc.CloseInput();
            enc.ProcessOutput(outBuf, ref offset).ShouldBe(JxlEncoderStatus.Success);
            offset.ShouldBeGreaterThan(0);

            enc.Reset();
            enc.Destroy();
        }

        private static void EncodeResampledAndProgressiveFrames(JxlMemoryManager mm)
        {
            CodecMetadata meta = CreateBasicMetadata(16, 16, isGray: false, hasAlpha: true);

            meta.M.HaveAnimation = true;

            using ImageBundle ib    = new ImageBundle(mm, meta.M);
            ImagePlane<float> alpha = ImagePlane<float>.Create(mm, 16, 16).Value;

            ib.SetFromImage(CreateCheckerImage3(mm, 16, 16, 0.2f, 0.7f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ImageOps.FillImage(0.9f, alpha);
            ib.SetAlpha(alpha).IsOk.ShouldBeTrue();

            CompressParams cpVar = new CompressParams
            {
                ModularMode         = false,
                ButteraugliDistance = 1.5f,
                Resampling          = 2,
                EcResampling        = 2,
                ProgressiveMode     = Override.On,
                Gaborish            = Override.On,
                Epf                 = 2,
            };
            FrameInfo fi    = new FrameInfo
            {
                CustomSizeOrOrigin = true,
                OriginX0           = 2,
                OriginY0           = 2,
                SaveAsReference    = 1,
                Duration           = 5,
                Timecode           = 1,
            };
            BitWriter bwVar = new BitWriter(mm);

            EncFrame.EncodeFrame(mm, cpVar, fi, meta, ib, JxlCms.GetDefaultCms(), (null, bwVar, new AuxOut())).IsOk.ShouldBeTrue();

            using ImageBundle ibMod    = new ImageBundle(mm, meta.M);
            ImagePlane<float> alphaMod = ImagePlane<float>.Create(mm, 16, 16).Value;

            ibMod.SetFromImage(CreateCheckerImage3(mm, 16, 16, 0.2f, 0.7f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            ImageOps.FillImage(0.9f, alphaMod);
            ibMod.SetAlpha(alphaMod).IsOk.ShouldBeTrue();

            CompressParams cpMod = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                Resampling          = 2,
                EcResampling        = 2,
                QProgressiveMode    = Override.On,
            };
            BitWriter bwMod = new BitWriter(mm);

            EncFrame.EncodeFrame(mm, cpMod, new FrameInfo(), meta, ibMod, JxlCms.GetDefaultCms(), (null, bwMod, null)).IsOk.ShouldBeTrue();
        }

        private static void EncodeJpegTranscodeFrames(JxlMemoryManager mm)
        {
            byte[] jpegBytes = BuildSyntheticBaselineJpeg(includeMetaAndRestart: true);
            Status parseOk   = EncJpegData.ParseJPG(mm, jpegBytes, out JPEGData? jpgData);

            parseOk.IsOk.ShouldBeTrue();
            jpgData.ShouldNotBeNull();

            CodecMetadata meta   = CreateBasicMetadata(8, 8, isGray: false, hasAlpha: false);
            using ImageBundle ib = new ImageBundle(mm, meta.M)
            {
                JpegData = jpgData,
            };
            BitWriter bw         = new BitWriter(mm);

            EncFrame.EncodeFrame(mm, new CompressParams(), new FrameInfo(), meta, ib, JxlCms.GetDefaultCms(), (null, bw, null)).IsOk.ShouldBeTrue();
            ((ulong)bw.BitsWritten).ShouldBeGreaterThan(0UL);
        }

        private static void ExerciseJpegHuffmanTableBuilder()
        {
            uint[] singleCount             = new uint[17];
            uint[] singleSym               = { 5 };
            JpegHuffmanTableEntry[] lutOne = new JpegHuffmanTableEntry[EncJpegHuffmanDecode.KJpegHuffmanLutSize];

            singleCount[1] = 1;
            EncJpegHuffmanDecode.BuildJpegHuffmanTable(singleCount, singleSym, lutOne);
            lutOne[0].Value.ShouldBe((ushort)5);

            uint[] deepCounts               = new uint[17];
            uint[] deepSyms                 = { 0, 1, 2 };
            JpegHuffmanTableEntry[] lutDeep = new JpegHuffmanTableEntry[EncJpegHuffmanDecode.KJpegHuffmanLutSize];

            deepCounts[1]  = 1;
            deepCounts[9]  = 1;
            deepCounts[10] = 1;
            EncJpegHuffmanDecode.BuildJpegHuffmanTable(deepCounts, deepSyms, lutDeep);
            lutDeep[0].Bits.ShouldBe((byte)1);
        }

        private static void ExerciseJpegDataReaderAndEncJpegData()
        {
            byte[] baseJpeg = BuildSyntheticBaselineJpeg(includeMetaAndRestart: true);
            JPEGData jpgHdr = new JPEGData();
            JPEGData jpgTbl = new JPEGData();
            JPEGData jpgAll = new JPEGData();

            EncJpegDataReader.ReadJpeg(new byte[] { 1, 2 }, JpegReadMode.kReadAll, jpgAll).ShouldBeFalse();
            EncJpegDataReader.ReadJpeg(baseJpeg, JpegReadMode.kReadHeader, jpgHdr).ShouldBeTrue();
            EncJpegDataReader.ReadJpeg(baseJpeg, JpegReadMode.kReadTables, jpgTbl).ShouldBeTrue();
            EncJpegDataReader.ReadJpeg(baseJpeg, JpegReadMode.kReadAll, jpgAll).ShouldBeTrue();

            byte[] progJpeg  = BuildSyntheticProgressiveJpeg();
            JPEGData jpgProg = new JPEGData();

            EncJpegDataReader.ReadJpeg(progJpeg, JpegReadMode.kReadAll, jpgProg).ShouldBeTrue();

            ColorEncoding ce           = new ColorEncoding();
            YCbCrChromaSubsampling css = new YCbCrChromaSubsampling();
            JpegBlobs blobs            = new JpegBlobs();

            EncJpegData.SetColorEncodingFromJpegData(jpgAll, ce).IsOk.ShouldBeTrue();
            EncJpegData.SetChromaSubsamplingFromJpegData(jpgAll, css).IsOk.ShouldBeTrue();
            EncJpegData.SetColorTransformFromJpegData(jpgAll, out ColorTransform ct).IsOk.ShouldBeTrue();
            EncJpegData.SetBlobsFromJpegData(jpgAll, blobs).IsOk.ShouldBeTrue();
            blobs.Exif.Length.ShouldBeGreaterThan(0);
            blobs.Xmp.Length.ShouldBeGreaterThan(0);

            EncJpegData.EncodeJPEGData(JxlMemoryManager.Default, jpgAll, out byte[] jbrdBytes, new CompressParams()).IsOk.ShouldBeTrue();
            jbrdBytes.Length.ShouldBeGreaterThan(0);
            _ = ct;
        }

        private static byte[] BuildSyntheticBaselineJpeg(bool includeMetaAndRestart)
        {
            List<byte> b = new List<byte> { 0xFF, 0xD8 };

            if (includeMetaAndRestart)
            {
                byte[] exif    = { 0xFF, 0xE1, 0x00, 0x0A, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, 0x11, 0x22 };
                List<byte> xmp = new List<byte> { 0xFF, 0xE1, 0x00, 33 };
                byte[] com     = { 0xFF, 0xFE, 0x00, 0x04, (byte)'h', (byte)'i' };
                byte[] dri     = { 0xFF, 0xDD, 0x00, 0x04, 0x00, 0x01 };

                b.Add(0x42);
                b.AddRange(exif);
                xmp.AddRange(System.Text.Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"));
                xmp.Add(0xAA);
                xmp.Add(0xBB);
                b.AddRange(xmp);
                b.AddRange(com);
                b.AddRange(dri);
            }

            AppendDqt16Bit(b);
            AppendSimpleDht(b, slotId: 0x00, symbol: 0);
            AppendSimpleDht(b, slotId: 0x10, symbol: 0);

            byte[] sof0 =
            {
                0xFF, 0xC0, 0x00, 17, 8, 0x00, 8, 0x00, 16, 3,
                1, 0x11, 0,
                2, 0x11, 0,
                3, 0x11, 0,
            };
            byte[] sos =
            {
                0xFF, 0xDA, 0x00, 12, 3,
                1, 0x00,
                2, 0x00,
                3, 0x00,
                0, 63, 0,
            };

            b.AddRange(sof0);
            b.AddRange(sos);

            if (includeMetaAndRestart)
            {
                b.Add(0x00);
                b.Add(0xFF);
                b.Add(0xD0);
                b.Add(0x00);
            }
            else
            {
                b.Add(0x00);
            }

            b.Add(0xFF);
            b.Add(0xD9);

            if (includeMetaAndRestart)
            {
                b.Add(0x99);
            }

            return b.ToArray();
        }

        private static byte[] BuildSyntheticProgressiveJpeg()
        {
            List<byte> b = new List<byte> { 0xFF, 0xD8 };

            AppendDqt16Bit(b);
            AppendSimpleDht(b, slotId: 0x00, symbol: 0);
            AppendTwoSymbolDht(b, slotId: 0x10, sym0: 0x00, sym1: 0x01);

            byte[] sof2 =
            {
                0xFF, 0xC2, 0x00, 11, 8, 0x00, 8, 0x00, 8, 1,
                1, 0x11, 0,
            };
            byte[] sosDcFirst  = { 0xFF, 0xDA, 0x00, 8, 1, 1, 0x00, 0, 0, 0x01 };
            byte[] sosDcRefine = { 0xFF, 0xDA, 0x00, 8, 1, 1, 0x00, 0, 0, 0x10 };
            byte[] sosAcFirst  = { 0xFF, 0xDA, 0x00, 8, 1, 1, 0x00, 1, 2, 0x01 };
            byte[] sosAcRefine = { 0xFF, 0xDA, 0x00, 8, 1, 1, 0x00, 1, 2, 0x10 };

            b.AddRange(sof2);
            b.AddRange(sosDcFirst);
            b.Add(0x00);
            b.AddRange(sosDcRefine);
            b.Add(0x80);
            b.AddRange(sosAcFirst);
            b.Add(0x67);
            b.AddRange(sosAcRefine);
            b.Add(0x7F);
            b.Add(0xFF);
            b.Add(0xD9);

            return b.ToArray();
        }

        private static void AppendDqt16Bit(List<byte> b)
        {
            b.Add(0xFF);
            b.Add(0xDB);
            b.Add(0x00);
            b.Add(131);
            b.Add(0x10);

            for (int k = 0; k < 64; k++)
            {
                b.Add(0x00);
                b.Add(0x10);
            }
        }

        private static void AppendSimpleDht(List<byte> b, byte slotId, byte symbol)
        {
            b.Add(0xFF);
            b.Add(0xC4);
            b.Add(0x00);
            b.Add(20);
            b.Add(slotId);
            b.Add(1);

            for (int i = 2; i <= 16; i++)
            {
                b.Add(0);
            }

            b.Add(symbol);
        }

        private static void AppendTwoSymbolDht(List<byte> b, byte slotId, byte sym0, byte sym1)
        {
            b.Add(0xFF);
            b.Add(0xC4);
            b.Add(0x00);
            b.Add(21);
            b.Add(slotId);
            b.Add(0);
            b.Add(2);

            for (int i = 3; i <= 16; i++)
            {
                b.Add(0);
            }

            b.Add(sym0);
            b.Add(sym1);
        }

        private static void ExerciseProgressiveSplitterAndHistogramParams()
        {
            ProgressiveSplitter splitter = new ProgressiveSplitter();
            Span<PassDefinition> passes  = stackalloc PassDefinition[3]
            {
                new PassDefinition(2, 1, 4),
                new PassDefinition(4, 0, 2),
                new PassDefinition(8, 0, 1),
            };
            ProgressiveMode mode = new ProgressiveMode(passes);
            Passes passesHeader  = new Passes();

            splitter.SetProgressiveMode(mode);
            splitter.InitPasses(passesHeader).IsOk.ShouldBeTrue();

            short[] src16  = new short[64];
            short[] dst0   = new short[64];
            short[] dst1   = new short[64];
            short[] dst2   = new short[64];
            AcStrategy acs = AcStrategy.FromRawStrategy(AcStrategyType.DCT);

            src16[1]  = 12;
            src16[10] = -8;

            fixed (short* pSrc = src16)
            fixed (short* p0 = dst0)
            fixed (short* p1 = dst1)
            fixed (short* p2 = dst2)
            {
                Span<nint> outPtrs = stackalloc nint[3] { (nint)p0, (nint)p1, (nint)p2 };

                splitter.SplitACCoefficients(pSrc, acs, 0, 0, outPtrs);
                splitter.SetProgressiveMode(new ProgressiveMode());
                splitter.SplitACCoefficients(pSrc, acs, 0, 0, outPtrs.Slice(0, 1));
            }

            SpeedTier[] tiers =
            {
                SpeedTier.kTortoise,
                SpeedTier.kKitten,
                SpeedTier.kSquirrel,
                SpeedTier.kHare,
                SpeedTier.kFalcon,
                SpeedTier.kThunder,
                SpeedTier.kLightning,
            };

            foreach (SpeedTier tier in tiers)
            {
                CompressParams cp = new CompressParams
                {
                    SpeedTier         = tier,
                    ModularMode       = true,
                    DecodingSpeedTier = 2,
                };

                cp.Options.Predictor = Predictor.Zero;
                _ = HistogramParams.ForModular(cp, new byte[] { 1 }, streamingMode: false);
            }
        }

        private static void ExerciseQuantWeightsAndCoeffOrders()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            DequantMatrices mat = new DequantMatrices();

            mat.SetEncodings(DequantMatrices.Library());
            EncQuantWeights.DequantMatricesRoundtrip(mm, mat).IsOk.ShouldBeTrue();
            EncQuantWeights.DequantMatricesScaleDC(mm, mat, 1.5f).IsOk.ShouldBeTrue();

            using Image3<float> gabImg     = CreateCheckerImage3(mm, 8, 8, 0.2f, 0.6f);
            ReadOnlySpan<float> extremeMul = stackalloc float[3] { 100.0f, 100.0f, 100.0f };
            FrameDimensions fd             = new FrameDimensions();

            EncGaborish.GaborishInverse(gabImg, new Rect(0, 0, 8, 8), extremeMul, null).IsOk.ShouldBeTrue();
            fd.Set(16, 16, 1, 0, 0, false, 1);

            using AcStrategyImage acsImg = AcStrategyImage.Create(mm, fd.XSizeBlocks, fd.YSizeBlocks).Value;
            Rect rect                    = new Rect(0, 0, (int)fd.XSizeBlocks, (int)fd.YSizeBlocks);
            AcQuantBlockArray blkArr     = new AcQuantBlockArray(4);
            ushort[] orderOut            = new ushort[CoeffOrder.kCoeffOrderMaxSize];
            uint allUsedOrders           = 0;

            acsImg.Set(0, 0, AcStrategyType.DCT16X16);
            EncCoeffOrder.ComputeUsedOrders(SpeedTier.kFalcon, acsImg, rect).ShouldBe(0u);
            EncCoeffOrder.ComputeUsedOrders(SpeedTier.kHare, acsImg, rect);

            uint used = EncCoeffOrder.ComputeUsedOrders(SpeedTier.kSquirrel, acsImg, rect);

            blkArr.Block(0, 0, 64)[10] = 5;
            blkArr.Block(0, 1, 64)[12] = 7;
            EncCoeffOrder.ComputeCoeffOrder(
                SpeedTier.kSquirrel,
                blkArr,
                acsImg,
                fd,
                ref allUsedOrders,
                (0, (ushort)(used | 1u), 0),
                orderOut);
            allUsedOrders.ShouldBeGreaterThan(0u);

            BitWriter bw               = new BitWriter(mm);
            using ImagePlane<float> qf = ImagePlane<float>.Create(mm, fd.XSizeBlocks, fd.YSizeBlocks).Value;

            EncCoeffOrder.EncodeCoeffOrders((ushort)allUsedOrders, orderOut, bw, LayerType.Order, null).IsOk.ShouldBeTrue();
            ImageOps.FillImage(2.0f, qf);
            EncAdaptiveQuantization.AdjustQuantField(acsImg, rect, qf);

            using ImagePlane<float> tileMap = EncAdaptiveQuantization.TileDistMap(qf, 2, 1, acsImg);
            using Image3<float> dsIter      = EncHeuristics.DownsampleImage2Iterative(gabImg, 4).Value;

            ((ulong)tileMap.XSize).ShouldBeGreaterThan(0UL);
            ((ulong)dsIter.XSize).ShouldBeGreaterThan(0UL);
        }

        private static void ExerciseClusterContextMapAndAns()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            Histogram hFlat     = Histogram.Flat(16, 64);
            Histogram hClone    = hFlat.Clone();

            hClone.EnsureCapacity(32);
            hClone.FastAdd(5);
            hClone.Condition();
            hClone.ShannonEntropy().ShouldBeGreaterThan(0.0f);
            hClone.ANSPopulationCost().IsOk.ShouldBeTrue();
            hClone.Swap(hFlat);

            List<Histogram> inHists  = new List<Histogram>
            {
                Histogram.Flat(8, 32),
                Histogram.Flat(8, 32),
                Histogram.Flat(16, 64),
                Histogram.Flat(16, 64),
            };
            List<Histogram> outHists = new List<Histogram> { Histogram.Flat(8, 32) };
            List<uint> syms          = new List<uint>();
            HistogramParams hpBest   = new HistogramParams { Clustering = HistogramParams.ClusteringType.kBest };
            BitWriter bwCtx          = new BitWriter(mm);
            BlockCtxMap bcm          = new BlockCtxMap();

            EncCluster.ClusterHistograms(hpBest, inHists, 8, outHists, syms).IsOk.ShouldBeTrue();
            outHists.Clear();
            EncCluster.ClusterHistograms(hpBest, inHists, 8, outHists, syms).IsOk.ShouldBeTrue();
            EncContextMap.EncodeContextMap(new byte[] { 0, 1 }, 2, bwCtx, LayerType.Header, null).IsOk.ShouldBeTrue();
            bcm.DcThresholds[0] = new[] { 4 };
            bcm.QfThresholds    = new uint[] { 2 };
            EncContextMap.EncodeBlockCtxMap(bcm, bwCtx, null).IsOk.ShouldBeTrue();

            List<List<Token>> tokStreams = BuildCompressibleTokenStreams();
            EntropyEncodingData codes    = new EntropyEncodingData();
            BitWriter bwAns              = new BitWriter(mm);

            try
            {
                EncAns.SetANSFuzzerFriendly(true);
                EncAns.BuildAndEncodeHistograms(mm, new HistogramParams(), 2, tokStreams, codes, bwAns).IsOk.ShouldBeTrue();
            }
            finally
            {
                EncAns.SetANSFuzzerFriendly(false);
            }

            HistogramParams hpStream        = new HistogramParams
            {
                StreamingMode      = true,
                AddFixedHistograms = true,
                AddMissingSymbols  = true,
            };
            EntropyEncodingData streamCodes = new EntropyEncodingData();
            List<List<Token>> sTokens       = BuildCompressibleTokenStreams();

            EncAns.BuildAndEncodeHistograms(mm, hpStream, 2, sTokens, streamCodes, bwAns).IsOk.ShouldBeTrue();
            EncAns.EncodeHistograms(streamCodes, bwAns, LayerType.Header, null).IsOk.ShouldBeTrue();
        }

        private static void ExerciseXybExternalImageAndDebugImage()
        {
            JxlMemoryManager mm        = JxlMemoryManager.Default;
            CodecMetadata meta         = CreateBasicMetadata(4, 4, isGray: false, hasAlpha: true);
            using ImageBundle ib       = new ImageBundle(mm, meta.M);
            using ImageBundle store    = new ImageBundle(mm, meta.M);
            using Image3<float> xyb    = Image3<float>.Create(mm, 4, 4).Value;
            using Image3<float> linear = Image3<float>.Create(mm, 4, 4).Value;

            ib.SetFromImage(CreateCheckerImage3(mm, 4, 4, 0.1f, 0.9f), ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            EncXyb.ToXYB(ib, null, xyb, JxlCms.GetDefaultCms(), linear).IsOk.ShouldBeTrue();
            EncXyb.ScaleXYB(xyb);
            EncXyb.RgbToYcbcr(xyb.Plane(0), xyb.Plane(1), xyb.Plane(2), linear.Plane(0), linear.Plane(1), linear.Plane(2), null).IsOk.ShouldBeTrue();
            EncImageBundle.TransformIfNeeded(ib, ColorEncoding.LinearSRGB(), JxlCms.GetDefaultCms(), null, store, out _).IsOk.ShouldBeTrue();

            JxlDataType[] types     = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float16, JxlDataType.Float };
            JxlEndianness[] endians = { JxlEndianness.LittleEndian, JxlEndianness.BigEndian };
            byte[] rawBuf           = new byte[4 * 4 * 4 * 4];

            foreach (JxlDataType dt in types)
            {
                foreach (JxlEndianness end in endians)
                {
                    using ImageBundle bundle = new ImageBundle(mm, meta.M);
                    JxlPixelFormat pf1       = new JxlPixelFormat(1, dt, end, 0);
                    JxlPixelFormat pf4       = new JxlPixelFormat(4, dt, end, 0);

                    EncExternalImage.BufferToImageF(pf1, 4, 4, rawBuf, null, out ImagePlane<float>? pl).IsOk.ShouldBeTrue();
                    pl?.Dispose();
                    EncExternalImage.BufferToImageBundle(pf4, 4, 4, rawBuf, null, bundle).IsOk.ShouldBeTrue();
                }
            }

            int dumpCount          = 0;
            CompressParams cpDebug = new CompressParams
            {
                DebugImage = (_, _, _, _, _, _) => dumpCount++,
            };

            EncDebugImage.WantDebugOutput(cpDebug).ShouldBeTrue();
            EncDebugImage.DumpImage(cpDebug, "test_rgb", xyb);
            EncDebugImage.DumpPlaneNormalized(cpDebug, "test_plane", xyb.Plane(0));
            dumpCount.ShouldBe(2);
        }
    }
}
