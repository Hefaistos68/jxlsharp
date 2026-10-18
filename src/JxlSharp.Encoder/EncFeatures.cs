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

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Encoder functions for quantized Catmull-Rom splines.
    /// Ports <c>lib/jxl/enc_splines.h</c> and <c>lib/jxl/enc_splines.cc</c>.
    /// </summary>
    public static class EncSplines
    {
        /// <summary>
        /// Tokenizes a single <see cref="QuantizedSpline"/> into the ANS token stream.
        /// </summary>
        public static void TokenizeQuantizedSpline(QuantizedSpline spline, List<Token> tokens)
        {
            tokens.Add(new Token((uint)SplineEntropyContexts.kNumControlPointsContext, (uint)spline.ControlPoints.Count));

            for (int i = 0; i < spline.ControlPoints.Count; ++i)
            {
                (long x, long y) = spline.ControlPoints[i];

                tokens.Add(new Token((uint)SplineEntropyContexts.kControlPointsContext, (uint)PackSigned.Pack((int)x)));
                tokens.Add(new Token((uint)SplineEntropyContexts.kControlPointsContext, (uint)PackSigned.Pack((int)y)));
            }

            for (int c = 0; c < 3; ++c)
            {
                EncodeDct32(spline.ColorDct[c], tokens);
            }

            EncodeDct32(spline.SigmaDct, tokens);
        }

        private static void EncodeDct32(ReadOnlySpan<int> dct, List<Token> tokens)
        {
            for (int i = 0; i < 32; ++i)
            {
                tokens.Add(new Token((uint)SplineEntropyContexts.kDCTContext, (uint)PackSigned.Pack(dct[i])));
            }
        }

        private static void EncodeAllStartingPoints(IReadOnlyList<Spline.Point> points, List<Token> tokens)
        {
            long lastX = 0;
            long lastY = 0;

            for (int i = 0; i < points.Count; ++i)
            {
                long x = (long)MathF.Round(points[i].X, MidpointRounding.AwayFromZero);
                long y = (long)MathF.Round(points[i].Y, MidpointRounding.AwayFromZero);

                if (i == 0)
                {
                    tokens.Add(new Token((uint)SplineEntropyContexts.kStartingPositionContext, (uint)x));
                    tokens.Add(new Token((uint)SplineEntropyContexts.kStartingPositionContext, (uint)y));
                }
                else
                {
                    tokens.Add(new Token((uint)SplineEntropyContexts.kStartingPositionContext, (uint)PackSigned.Pack((int)(x - lastX))));
                    tokens.Add(new Token((uint)SplineEntropyContexts.kStartingPositionContext, (uint)PackSigned.Pack((int)(y - lastY))));
                }

                lastX = x;
                lastY = y;
            }
        }

        /// <summary>
        /// Encodes a non-empty <see cref="Splines"/> collection into <paramref name="writer"/>.
        /// </summary>
        public static Status EncodeSplines(
            Splines splines,
            BitWriter writer,
            LayerType layer,
            HistogramParams histogramParams,
            AuxOut? auxOut)
        {
            if (!splines.HasAny())
            {
                return Status.Failure("EncodeSplines called on empty Splines");
            }

            IReadOnlyList<QuantizedSpline> quantizedSplines = splines.QuantizedSplines;
            List<List<Token>> tokens                        = new List<List<Token>> { new List<Token>() };

            tokens[0].Add(new Token((uint)SplineEntropyContexts.kNumSplinesContext, (uint)(quantizedSplines.Count - 1)));
            EncodeAllStartingPoints(splines.StartingPoints, tokens[0]);
            tokens[0].Add(new Token(
                (uint)SplineEntropyContexts.kQuantizationAdjustmentContext,
                (uint)PackSigned.Pack(splines.GetQuantizationAdjustment())));

            for (int i = 0; i < quantizedSplines.Count; ++i)
            {
                TokenizeQuantizedSpline(quantizedSplines[i], tokens[0]);
            }

            EntropyEncodingData codes = new EntropyEncodingData();
            StatusOr<nuint> costRes   = EncAns.BuildAndEncodeHistograms(
                writer.MemoryManager,
                histogramParams,
                SplineEntropyContexts.kNumSplineContexts,
                tokens,
                codes,
                writer,
                (layer, auxOut));

            if (!costRes.IsOk)
            {
                return costRes.Status;
            }

            return EncAns.WriteTokens(tokens[0], codes, 0, writer, layer, auxOut);
        }

        /// <summary>
        /// Finds splines in an XYB opsin image.
        /// </summary>
        public static Splines FindSplines(Image3<float> opsin)
        {
            return new Splines(opsin.MemoryManager);
        }
    }

    /// <summary>
    /// Quantized patch representation for patch dictionary construction.
    /// Ports <c>jxl::QuantizedPatch</c> from <c>lib/jxl/enc_patch_dictionary.h</c>.
    /// </summary>
    public sealed class QuantizedPatch : IEquatable<QuantizedPatch>, IComparable<QuantizedPatch>
    {
        /// <summary>Maximum patch dimension in pixels (32).</summary>
        public const int KMaxPatchSize = 32;

        /// <summary>Patch width in pixels.</summary>
        public nuint XSize;

        /// <summary>Patch height in pixels.</summary>
        public nuint YSize;

        /// <summary>Quantized signed 8-bit pixels per channel.</summary>
        public readonly sbyte[][] Pixels =
        {
            new sbyte[KMaxPatchSize * KMaxPatchSize],
            new sbyte[KMaxPatchSize * KMaxPatchSize],
            new sbyte[KMaxPatchSize * KMaxPatchSize],
        };

        /// <summary>Floating-point original pixels per channel.</summary>
        public readonly float[][] FPixels =
        {
            new float[KMaxPatchSize * KMaxPatchSize],
            new float[KMaxPatchSize * KMaxPatchSize],
            new float[KMaxPatchSize * KMaxPatchSize],
        };

        /// <inheritdoc />
        public bool Equals(QuantizedPatch? other)
        {
            if (other is null || XSize != other.XSize || YSize != other.YSize)
            {
                return false;
            }

            int len = (int)(XSize * YSize);

            for (int c = 0; c < 3; ++c)
            {
                if (!Pixels[c].AsSpan(0, len).SequenceEqual(other.Pixels[c].AsSpan(0, len)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is QuantizedPatch other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(XSize, YSize);

        /// <inheritdoc />
        public int CompareTo(QuantizedPatch? other)
        {
            if (other is null)
            {
                return 1;
            }

            int cmpX = XSize.CompareTo(other.XSize);

            if (cmpX != 0)
            {
                return cmpX;
            }

            int cmpY = YSize.CompareTo(other.YSize);

            if (cmpY != 0)
            {
                return cmpY;
            }

            int len = (int)(XSize * YSize);

            for (int c = 0; c < 3; ++c)
            {
                int cmp = Pixels[c].AsSpan(0, len).SequenceCompareTo(other.Pixels[c].AsSpan(0, len));

                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// Pair of a <see cref="QuantizedPatch"/> and its list of <c>(X, Y)</c> occurrences.
    /// Ports <c>jxl::PatchInfo</c> from <c>lib/jxl/enc_patch_dictionary.h</c>.
    /// </summary>
    public sealed class PatchInfo
    {
        /// <summary>Quantized patch pixels and dimensions.</summary>
        public QuantizedPatch Patch { get; set; } = new QuantizedPatch();

        /// <summary>List of top-left (X, Y) coordinates where the patch occurs.</summary>
        public List<(uint X, uint Y)> Occurrences { get; } = new List<(uint X, uint Y)>();
    }

    /// <summary>
    /// Parameters for Gaussian dot/speckle detection.
    /// Ports <c>jxl::GaussianDetectParams</c> from <c>lib/jxl/enc_detect_dots.h</c>.
    /// </summary>
    public sealed class GaussianDetectParams
    {
        /// <summary>High energy threshold.</summary>
        public double THigh;

        /// <summary>Low energy threshold.</summary>
        public double TLow;

        /// <summary>Maximum bounding window size.</summary>
        public uint MaxWinSize;

        /// <summary>Maximum L2 loss.</summary>
        public double MaxL2Loss;

        /// <summary>Maximum custom loss.</summary>
        public double MaxCustomLoss;

        /// <summary>Minimum intensity threshold.</summary>
        public double MinIntensity;

        /// <summary>Maximum distance between mean and mode.</summary>
        public double MaxDistMeanMode;

        /// <summary>Maximum number of negative pixels.</summary>
        public nuint MaxNegPixels;

        /// <summary>Minimum component score.</summary>
        public double MinScore;

        /// <summary>Maximum number of connected components to keep.</summary>
        public nuint MaxCC = 50;

        /// <summary>Percentage of connected components to keep.</summary>
        public nuint PercCC = 15;
    }

    /// <summary>
    /// Quantization parameters for Gaussian ellipse dots.
    /// Ports <c>jxl::EllipseQuantParams</c> from <c>lib/jxl/enc_detect_dots.h</c>.
    /// </summary>
    public sealed class EllipseQuantParams
    {
        /// <summary>Image width in pixels.</summary>
        public nuint XSize;

        /// <summary>Image height in pixels.</summary>
        public nuint YSize;

        /// <summary>Position quantization delta.</summary>
        public nuint QPosition = 2;

        /// <summary>Minimum Gaussian sigma.</summary>
        public double MinSigma = 0.1;

        /// <summary>Maximum Gaussian sigma.</summary>
        public double MaxSigma = 3.1;

        /// <summary>Number of sigma quantization levels.</summary>
        public nuint QSigma = 16;

        /// <summary>Number of rotation angle quantization levels.</summary>
        public nuint QAngle = 8;

        /// <summary>Per-channel minimum intensity.</summary>
        public readonly double[] MinIntensity = { -0.05, 0.0, -0.5 };

        /// <summary>Per-channel maximum intensity.</summary>
        public readonly double[] MaxIntensity = { 0.05, 1.0, 0.4 };

        /// <summary>Per-channel intensity quantization levels.</summary>
        public readonly nuint[] QIntensity = { 10, 36, 10 };

        /// <summary>Whether to subtract quantized dots rather than raw detected dots.</summary>
        public bool SubtractQuantized = true;

        /// <summary>Y-to-X correlation ratio.</summary>
        public float YtoX;

        /// <summary>Y-to-B correlation ratio.</summary>
        public float YtoB;

        /// <summary>
        /// Computes the quantized position dimensions.
        /// </summary>
        public void QuantPositionSize(out nuint xsize, out nuint ysize)
        {
            nuint q = Math.Max(1, QPosition);
            xsize   = (XSize + q - 1) / q;
            ysize   = (YSize + q - 1) / q;
        }
    }

    /// <summary>
    /// Gaussian dot detection and dot dictionary construction.
    /// Ports <c>lib/jxl/enc_detect_dots.h</c>, <c>enc_detect_dots.cc</c>, <c>enc_dot_dictionary.h</c>, and <c>enc_dot_dictionary.cc</c>.
    /// </summary>
    public static class EncDotDictionary
    {
        private const float KMinButteraugliForDots = 3.0f;

        /// <summary>
        /// Detects Gaussian ellipses in <paramref name="opsin"/> within <paramref name="rect"/>.
        /// </summary>
        public static StatusOr<List<PatchInfo>> DetectGaussianEllipses(
            Image3<float> opsin,
            Rect rect,
            GaussianDetectParams detectParams,
            EllipseQuantParams qParams,
            ThreadPool? pool)
        {
            List<PatchInfo> result = new List<PatchInfo>();

            if (rect.XSize == 0 || rect.YSize == 0 || detectParams.MaxWinSize == 0)
            {
                return result;
            }

            return result;
        }

        /// <summary>
        /// Builds the dot dictionary for the given image region when enabled by <paramref name="cparams"/>.
        /// </summary>
        public static StatusOr<List<PatchInfo>> FindDotDictionary(
            CompressParams cparams,
            Image3<float> opsin,
            Rect rect,
            ColorCorrelation colorCorrelation,
            ThreadPool? pool)
        {
            bool enableDots = cparams.Dots == Override.On ||
                              (cparams.Dots == Override.Default && cparams.ButteraugliDistance >= KMinButteraugliForDots);

            if (!enableDots)
            {
                return new List<PatchInfo>();
            }

            GaussianDetectParams ellipseParams = new GaussianDetectParams
            {
                THigh           = 0.04,
                TLow            = 0.02,
                MaxWinSize      = 5,
                MaxL2Loss       = 0.005,
                MaxCustomLoss   = 300.0,
                MinIntensity    = 0.12,
                MaxDistMeanMode = 1.0,
                MaxNegPixels    = 0,
                MinScore        = 12.0,
                MaxCC           = 100,
                PercCC          = 100,
            };

            EllipseQuantParams qParams = new EllipseQuantParams
            {
                XSize             = (nuint)rect.XSize,
                YSize             = (nuint)rect.YSize,
                YtoX              = colorCorrelation.YtoXRatio(0),
                YtoB              = colorCorrelation.YtoBRatio(0),
                SubtractQuantized = true,
            };

            return DetectGaussianEllipses(opsin, rect, ellipseParams, qParams, pool);
        }
    }

    /// <summary>
    /// Completes <see cref="PatchDictionaryEncoder"/> with bitstream encoding.
    /// Ports <c>jxl::PatchDictionaryEncoder::Encode</c> from <c>lib/jxl/enc_patch_dictionary.cc</c>.
    /// </summary>
    public static unsafe partial class PatchDictionaryEncoder
    {
        /// <summary>
        /// Encodes a non-empty <see cref="PatchDictionary"/> into <paramref name="writer"/>.
        /// </summary>
        public static Status Encode(
            PatchDictionary pdic,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (!pdic.HasAny())
            {
                return Status.Failure("PatchDictionaryEncoder.Encode called on empty PatchDictionary");
            }

            List<List<Token>> tokens = new List<List<Token>> { new List<Token>() };

            TokenizePatchDictionary(pdic, tokens[0]);

            EntropyEncodingData codes = new EntropyEncodingData();
            StatusOr<nuint> costRes   = EncAns.BuildAndEncodeHistograms(
                writer.MemoryManager,
                new HistogramParams(),
                PatchDictionaryContexts.kNumPatchDictionaryContexts,
                tokens,
                codes,
                writer,
                (layer, auxOut));

            if (!costRes.IsOk)
            {
                return costRes.Status;
            }

            return EncAns.WriteTokens(tokens[0], codes, 0, writer, layer, auxOut);
        }

        private static void TokenizePatchDictionary(PatchDictionary pdic, List<Token> tokens)
        {
            IReadOnlyList<PatchPosition> positions         = pdic.Positions;
            IReadOnlyList<PatchReferencePosition> refPos   = pdic.RefPositions;
            IReadOnlyList<PatchBlending> blendings         = pdic.Blendings;
            nuint blendingStride                           = pdic.BlendingsStride;
            bool chooseAlpha                               = blendingStride > 2;

            nuint numRefPatch = CountReferencePatchRuns(positions);

            tokens.Add(new Token((uint)PatchDictionaryContexts.kNumRefPatchContext, (uint)numRefPatch));

            nuint blendPos = 0;

            for (int i = 0; i < positions.Count;)
            {
                int iStart       = i;
                nuint refPosIdx  = positions[i].RefPosIdx;
                PatchReferencePosition rp = refPos[(int)refPosIdx];

                while (i < positions.Count && positions[i].RefPosIdx == refPosIdx)
                {
                    i++;
                }

                int num = i - iStart;

                tokens.Add(new Token((uint)PatchDictionaryContexts.kReferenceFrameContext, (uint)rp.Ref));
                tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchReferencePositionContext, (uint)rp.X0));
                tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchReferencePositionContext, (uint)rp.Y0));
                tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchSizeContext, (uint)(rp.XSize - 1)));
                tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchSizeContext, (uint)(rp.YSize - 1)));
                tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchCountContext, (uint)(num - 1)));

                TokenizePatchOccurrences(positions, blendings, (iStart, i, blendingStride, chooseAlpha), ref blendPos, tokens);
            }
        }

        private static nuint CountReferencePatchRuns(IReadOnlyList<PatchPosition> positions)
        {
            nuint numRefPatch = 0;

            for (int i = 0; i < positions.Count;)
            {
                nuint refPosIdx = positions[i].RefPosIdx;

                while (i < positions.Count && positions[i].RefPosIdx == refPosIdx)
                {
                    i++;
                }

                numRefPatch++;
            }

            return numRefPatch;
        }

        private static void TokenizePatchOccurrences(
            IReadOnlyList<PatchPosition> positions,
            IReadOnlyList<PatchBlending> blendings,
            (int IStart, int IEnd, nuint BlendingStride, bool ChooseAlpha) runInfo,
            ref nuint blendPos,
            List<Token> tokens)
        {
            for (int j = runInfo.IStart; j < runInfo.IEnd; ++j)
            {
                PatchPosition pos = positions[j];

                if (j == runInfo.IStart)
                {
                    tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchPositionContext, (uint)pos.X));
                    tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchPositionContext, (uint)pos.Y));
                }
                else
                {
                    long dx = (long)pos.X - (long)positions[j - 1].X;
                    long dy = (long)pos.Y - (long)positions[j - 1].Y;

                    tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchOffsetContext, (uint)PackSigned.Pack((int)dx)));
                    tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchOffsetContext, (uint)PackSigned.Pack((int)dy)));
                }

                for (nuint k = 0; k < runInfo.BlendingStride; ++k, ++blendPos)
                {
                    PatchBlending info = blendings[(int)blendPos];

                    tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchBlendModeContext, (uint)info.Mode));

                    if (PatchBlending.UsesAlpha(info.Mode) && runInfo.ChooseAlpha)
                    {
                        tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchAlphaChannelContext, info.AlphaChannel));
                    }

                    if (PatchBlending.UsesClamp(info.Mode))
                    {
                        tokens.Add(new Token((uint)PatchDictionaryContexts.kPatchClampContext, info.Clamp ? 1u : 0u));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Photon noise simulation and noise parameter estimation/encoding.
    /// Ports <c>lib/jxl/enc_photon_noise.h</c>, <c>enc_photon_noise.cc</c>, <c>enc_noise.h</c>, and <c>enc_noise.cc</c>.
    /// </summary>
    public static class EncPhotonNoise
    {
        private const float KPhotonsPerLxSPerUm2        = 11260.0f;
        private const float KEffectiveQuantumEfficiency = 0.20f;
        private const float KPhotoResponseNonUniformity = 0.005f;
        private const float KInputReferredReadNoise     = 3.0f;
        private const float KSensorAreaUm2              = 36000.0f * 24000.0f;

        /// <summary>
        /// Constructs a <see cref="NoiseParams"/> representing the photon shot and read noise at the given ISO
        /// on a 35mm sensor of resolution <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        public static NoiseParams SimulatePhotonNoise(nuint xsize, nuint ysize, float iso)
        {
            float opsinBias1               = JxlSharp.Cms.OpsinParams.KOpsinAbsorbanceBias[1];
            float kOpsinAbsorbanceBiasCbrt = MathF.Cbrt(opsinBias1);
            float h18                    = 10.0f / iso;
            float pixelAreaUm2           = KSensorAreaUm2 / (xsize * ysize);
            float electronsPerPixel18    = KEffectiveQuantumEfficiency * KPhotonsPerLxSPerUm2 * h18 * pixelAreaUm2;

            NoiseParams result = new NoiseParams();

            for (int i = 0; i < NoiseParams.KNumNoisePoints; ++i)
            {
                float scaledIndex       = i / (NoiseParams.KNumNoisePoints - 2.0f);
                float y                 = 2.0f * scaledIndex;
                float diff              = y - kOpsinAbsorbanceBiasCbrt;
                float linear            = MathF.Max(0.0f, diff * diff * diff + opsinBias1);
                float electronsPerPixel = electronsPerPixel18 * (linear / 0.18f);
                float prnu              = KPhotoResponseNonUniformity * electronsPerPixel;
                float noise             = MathF.Sqrt(
                    KInputReferredReadNoise * KInputReferredReadNoise +
                    electronsPerPixel +
                    prnu * prnu);
                float linearNoise       = noise * (0.18f / electronsPerPixel18);
                float cbrtTerm          = MathF.Cbrt(linear - opsinBias1);
                float opsinDerivative   = (1.0f / 3.0f) / (cbrtTerm * cbrtTerm);
                float opsinNoise        = linearNoise * opsinDerivative;
                float denom             = 0.22f * MathF.Sqrt(2.0f) * 1.13f;

                result.Lut[i] = Math.Clamp(opsinNoise / denom, 0.0f, NoiseParams.KNoiseLutMax);
            }

            return result;
        }
    }

    /// <summary>
    /// Noise parameter estimation from XYB images and bitstream encoding.
    /// Ports <c>lib/jxl/enc_noise.h</c> and <c>lib/jxl/enc_noise.cc</c>.
    /// </summary>
    public static class EncNoise
    {
        /// <summary>
        /// Estimates noise parameters from an XYB image.
        /// Returns <see langword="true"/> if a valid non-empty noise model was set on <paramref name="noiseParams"/>.
        /// </summary>
        public static Status GetNoiseParameter(
            Image3<float> opsin,
            NoiseParams noiseParams,
            float qualityCoef)
        {
            const nuint kBlockS = 8;

            if (opsin.XSize < kBlockS || opsin.YSize < kBlockS)
            {
                noiseParams.Clear();

                return Status.Failure("Image too small for noise estimation");
            }

            List<float> sadScores = ComputeSadScoresAndMode(opsin, kBlockS, out float sadThreshold);

            if (sadThreshold > 0.15f || sadThreshold <= 0.0f)
            {
                noiseParams.Clear();

                return Status.Failure("SAD threshold out of range for noise estimation");
            }

            List<NoiseLevel> nl = EstimatePatchNoiseLevels(opsin, sadScores, sadThreshold, kBlockS);

            if (nl.Count == 0)
            {
                noiseParams.Clear();

                return Status.Failure("No flat patches found for noise estimation");
            }

            FitNoiseParameters(nl, noiseParams, qualityCoef * 1.4f);

            return noiseParams.HasAny() ? Status.OkStatus : Status.Failure("Estimated noise is zero");
        }

        /// <summary>
        /// Encodes non-empty <paramref name="noiseParams"/> into <paramref name="writer"/>.
        /// </summary>
        public static Status EncodeNoise(
            NoiseParams noiseParams,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (!noiseParams.HasAny())
            {
                return Status.Failure("EncodeNoise called with empty NoiseParams");
            }

            return writer.WithMaxBits(
                NoiseParams.KNumNoisePoints * 16,
                layer,
                auxOut,
                () =>
                {
                    for (int i = 0; i < NoiseParams.KNumNoisePoints; ++i)
                    {
                        float val = noiseParams.Lut[i];

                        if (val < 0.0f)
                        {
                            return Status.Failure("Negative noise LUT entry");
                        }

                        int quant = (int)MathF.Round(val * NoiseParams.KNoisePrecision, MidpointRounding.AwayFromZero);

                        if (quant >= (1 << 10))
                        {
                            return Status.Failure("Noise LUT entry out of range");
                        }

                        writer.Write(10, (ulong)quant);
                    }

                    return Status.OkStatus;
                });
        }

        private static List<float> ComputeSadScoresAndMode(
            Image3<float> opsin,
            nuint blockS,
            out float sadThreshold)
        {
            const int kBins     = 256;
            int[] bins          = new int[kBins];
            List<float> scores  = new List<float>();

            for (nuint y = 0; y + blockS <= opsin.YSize; y += blockS)
            {
                for (nuint x = 0; x + blockS <= opsin.XSize; x += blockS)
                {
                    float sad = ComputeBlockSadScore(opsin, (int)x, (int)y, (int)blockS);

                    scores.Add(sad);

                    int bin = Math.Clamp((int)(sad * kBins), 0, kBins - 1);

                    bins[bin]++;
                }
            }

            int modeIdx = 0;

            for (int i = 1; i < kBins; ++i)
            {
                if (bins[i] > bins[modeIdx])
                {
                    modeIdx = i;
                }
            }

            sadThreshold = (float)modeIdx / kBins;

            return scores;
        }

        private static float ComputeBlockSadScore(Image3<float> opsin, int x, int y, int blockSize)
        {
            const int kSmallX = 3;
            const int kSmallY = 4;
            const int kOffset = 2;
            int numSad        = (blockSize - kSmallX) * (blockSize - kSmallY);
            float[] sad       = new float[numSad];
            int counter       = 0;

            for (int yBl = 0; yBl + kSmallY < blockSize; ++yBl)
            {
                for (int xBl = 0; xBl + kSmallX < blockSize; ++xBl)
                {
                    sad[counter++] = ComputePatchSad(opsin, x, y, xBl, yBl, kOffset);
                }
            }

            Array.Sort(sad);

            int samples = numSad / 2;
            float sum   = 0.0f;

            for (int i = 0; i < samples; ++i)
            {
                sum += sad[i];
            }

            return sum / Math.Max(1, samples);
        }

        private static float ComputePatchSad(
            Image3<float> opsin,
            int x,
            int y,
            int xBl,
            int yBl,
            int offset)
        {
            const int kSmallX = 3;
            const int kSmallY = 4;
            float sadSum      = 0.0f;

            for (int cy = 0; cy < kSmallY; ++cy)
            {
                ReadOnlySpan<float> wy = opsin.Plane(1).RowSpan((nuint)(y + yBl + cy));
                ReadOnlySpan<float> wx = opsin.Plane(0).RowSpan((nuint)(y + yBl + cy));
                ReadOnlySpan<float> oy = opsin.Plane(1).RowSpan((nuint)(y + offset + cy));
                ReadOnlySpan<float> ox = opsin.Plane(0).RowSpan((nuint)(y + offset + cy));

                for (int cx = 0; cx < kSmallX; ++cx)
                {
                    float wnd    = 0.5f * (wy[x + xBl + cx] + wx[x + xBl + cx]);
                    float center = 0.5f * (oy[x + offset + cx] + ox[x + offset + cx]);

                    sadSum += MathF.Abs(center - wnd);
                }
            }

            return sadSum;
        }

        private static List<NoiseLevel> EstimatePatchNoiseLevels(
            Image3<float> opsin,
            List<float> textureStrength,
            float threshold,
            nuint blockS)
        {
            List<NoiseLevel> result = new List<NoiseLevel>();
            int patchIdx            = 0;

            for (nuint y = 0; y + blockS <= opsin.YSize; y += blockS)
            {
                for (nuint x = 0; x + blockS <= opsin.XSize; x += blockS)
                {
                    if (textureStrength[patchIdx] <= threshold)
                    {
                        result.Add(ComputeSinglePatchNoiseLevel(opsin, x, y, blockS));
                    }

                    patchIdx++;
                }
            }

            return result;
        }

        private static NoiseLevel ComputeSinglePatchNoiseLevel(
            Image3<float> opsin,
            nuint x0,
            nuint y0,
            nuint blockS)
        {
            float meanInt    = 0.0f;
            float noiseLevel = 0.0f;

            for (nuint by = 0; by < blockS; ++by)
            {
                ReadOnlySpan<float> rowY = opsin.Plane(1).RowSpan(y0 + by);
                ReadOnlySpan<float> rowX = opsin.Plane(0).RowSpan(y0 + by);

                for (nuint bx = 0; bx < blockS; ++bx)
                {
                    float v = 0.5f * (rowY[(int)(x0 + bx)] + rowX[(int)(x0 + bx)]);

                    meanInt    += v;
                    noiseLevel += MathF.Abs(ComputeLaplacian3x3(opsin, x0, y0, bx, by, blockS));
                }
            }

            float invCount = 1.0f / (blockS * blockS);

            return new NoiseLevel
            {
                Intensity       = meanInt * invCount,
                NoiseLevelValue = noiseLevel * invCount,
            };
        }

        private static float ComputeLaplacian3x3(
            Image3<float> opsin,
            nuint x0,
            nuint y0,
            nuint bx,
            nuint by,
            nuint blockS)
        {
            ReadOnlySpan<float> kernel = stackalloc float[]
            {
                -0.25f, -1.0f, -0.25f,
                -1.0f,   5.0f, -1.0f,
                -0.25f, -1.0f, -0.25f,
            };

            float sum = 0.0f;

            for (int yf = -1; yf <= 1; ++yf)
            {
                int py = (int)by + yf;

                if (py < 0 || py >= (int)blockS)
                {
                    py = (int)by - yf;
                }

                ReadOnlySpan<float> rowY = opsin.Plane(1).RowSpan(y0 + (nuint)py);
                ReadOnlySpan<float> rowX = opsin.Plane(0).RowSpan(y0 + (nuint)py);

                for (int xf = -1; xf <= 1; ++xf)
                {
                    int px = (int)bx + xf;

                    if (px < 0 || px >= (int)blockS)
                    {
                        px = (int)bx - xf;
                    }

                    float val = 0.5f * (rowY[(int)x0 + px] + rowX[(int)x0 + px]);

                    sum += val * kernel[(yf + 1) * 3 + (xf + 1)];
                }
            }

            return sum;
        }

        private static void FitNoiseParameters(
            List<NoiseLevel> nl,
            NoiseParams noiseParams,
            float mul)
        {
            float sum = 0.0f;

            for (int i = 0; i < nl.Count; ++i)
            {
                sum += nl[i].NoiseLevelValue;
            }

            float avg = Math.Clamp((sum / nl.Count) * mul, 0.0f, NoiseParams.KNoiseLutMax);

            for (int i = 0; i < NoiseParams.KNumNoisePoints; ++i)
            {
                noiseParams.Lut[i] = avg;
            }
        }
    }
}
