// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// ANS entropy context indices for spline decoding.
    /// Ported from <c>jxl::SplineEntropyContexts</c> in <c>lib/jxl/splines.h</c>.
    /// </summary>
    public static class SplineEntropyContexts
    {
        /// <summary>Context for spline quantization adjustment (0).</summary>
        public const nuint kQuantizationAdjustmentContext = 0;

        /// <summary>Context for spline starting positions (1).</summary>
        public const nuint kStartingPositionContext = 1;

        /// <summary>Context for the number of splines (2).</summary>
        public const nuint kNumSplinesContext = 2;

        /// <summary>Context for the number of control points in a spline (3).</summary>
        public const nuint kNumControlPointsContext = 3;

        /// <summary>Context for double-delta control point coordinates (4).</summary>
        public const nuint kControlPointsContext = 4;

        /// <summary>Context for color and sigma DCT32 coefficients (5).</summary>
        public const nuint kDCTContext = 5;

        /// <summary>Total number of spline entropy contexts (6).</summary>
        public const nuint kNumSplineContexts = 6;
    }

    /// <summary>
    /// Dequantized Catmull-Rom spline with 32-coefficient color and Gaussian sigma DCT descriptors.
    /// Ported from <c>jxl::Spline</c> in <c>lib/jxl/splines.h</c>.
    /// </summary>
    public sealed class Spline
    {
        /// <summary>Desired arc-length rendering step in pixels (1.0f).</summary>
        public const float kDesiredRenderingDistance = 1.0f;

        /// <summary>
        /// 2D point in floating-point pixel coordinates.
        /// </summary>
        public struct Point : IEquatable<Point>
        {
            /// <summary>Horizontal coordinate.</summary>
            public float X;

            /// <summary>Vertical coordinate.</summary>
            public float Y;

            /// <summary>
            /// Initializes a new instance of the <see cref="Point"/> struct.
            /// </summary>
            /// <param name="x">Horizontal coordinate.</param>
            /// <param name="y">Vertical coordinate.</param>
            public Point(float x, float y)
            {
                X = x;
                Y = y;
            }

            /// <inheritdoc />
            public readonly bool Equals(Point other)
            {
                return MathF.Abs(X - other.X) < 1e-3f && MathF.Abs(Y - other.Y) < 1e-3f;
            }

            /// <inheritdoc />
            public override readonly bool Equals(object? obj) => obj is Point other && Equals(other);

            /// <inheritdoc />
            public override readonly int GetHashCode() => HashCode.Combine(X, Y);

            /// <summary>Checks approximate equality within <c>1e-3f</c>.</summary>
            public static bool operator ==(Point left, Point right) => left.Equals(right);

            /// <summary>Checks approximate inequality.</summary>
            public static bool operator !=(Point left, Point right) => !left.Equals(right);
        }

        /// <summary>Control points of the Catmull-Rom spline.</summary>
        public readonly List<Point> ControlPoints = new List<Point>();

        /// <summary>X, Y, B 32-coefficient DCT arrays.</summary>
        public readonly float[][] ColorDct =
        {
            new float[32],
            new float[32],
            new float[32],
        };

        // Splines are drawn by normalized Gaussian splatting. This controls the
        // Gaussian's parameter along the spline.

        /// <summary>Gaussian sigma 32-coefficient DCT array.</summary>
        public readonly float[] SigmaDct = new float[32];

        /// <summary>
        /// Creates a deep copy of this <see cref="Spline"/>.
        /// </summary>
        /// <returns>Cloned <see cref="Spline"/>.</returns>
        public Spline Clone()
        {
            Spline copy = new Spline();
            copy.ControlPoints.AddRange(ControlPoints);

            for (int c = 0; c < 3; c++)
            {
                Array.Copy(ColorDct[c], copy.ColorDct[c], 32);
            }

            Array.Copy(SigmaDct, copy.SigmaDct, 32);

            return copy;
        }
    }

    /// <summary>
    /// Quantized representation of a <see cref="Spline"/> with double-delta-encoded control points.
    /// Ported from <c>jxl::QuantizedSpline</c> in <c>lib/jxl/splines.h</c> and <c>lib/jxl/splines.cc</c>.
    /// </summary>
    public sealed class QuantizedSpline
    {
        private static readonly float[] ChannelWeight = { 0.0042f, 0.075f, 0.07f, 0.3333f };
        private static readonly int[] ChannelOrder    = { 1, 0, 2 };

        private readonly List<(long X, long Y)> _controlPoints = new List<(long X, long Y)>();
        private readonly int[][] _colorDct =
        {
            new int[32],
            new int[32],
            new int[32],
        };
        private readonly int[] _sigmaDct = new int[32];

        /// <summary>Gets the double-delta-encoded control points.</summary>
        public IReadOnlyList<(long X, long Y)> ControlPoints => _controlPoints;

        /// <summary>Gets the quantized 3x32 color DCT coefficients.</summary>
        public int[][] ColorDct => _colorDct;

        /// <summary>Gets the quantized 32 sigma DCT coefficients.</summary>
        public int[] SigmaDct => _sigmaDct;

        /// <summary>
        /// Quantizes a continuous <see cref="Spline"/> into a <see cref="QuantizedSpline"/>.
        /// </summary>
        /// <param name="original">Source continuous spline.</param>
        /// <param name="quantizationAdjustment">Quantization precision adjustment.</param>
        /// <param name="yToX">Y-to-X color correlation ratio.</param>
        /// <param name="yToB">Y-to-B color correlation ratio.</param>
        /// <returns><see cref="StatusOr{QuantizedSpline}"/> containing the quantized spline.</returns>
        public static StatusOr<QuantizedSpline> Create(
            Spline original,
            int quantizationAdjustment,
            float yToX,
            float yToB)
        {
            if (original.ControlPoints.Count == 0)
            {
                return Status.Failure("Spline must have at least one control point");
            }

            QuantizedSpline result = new QuantizedSpline();
            EncodeControlPointDeltas(original.ControlPoints, result._controlPoints);
            QuantizeDctCoefficients(original, quantizationAdjustment, yToX, yToB, result);

            return result;
        }

        /// <summary>
        /// Dequantizes this <see cref="QuantizedSpline"/> into <paramref name="result"/>.
        /// </summary>
        /// <param name="startingPoint">Starting control point of the spline.</param>
        /// <param name="quantizationAdjustment">Quantization precision adjustment.</param>
        /// <param name="correlation">Tuple of (<c>YToX</c>, <c>YToB</c>) correlation ratios.</param>
        /// <param name="imageSize">Total number of pixels in the frame (<c>xsize * ysize</c>).</param>
        /// <param name="totalEstimatedAreaReached">Cumulative estimated rendering area across all splines.</param>
        /// <param name="result">Destination dequantized <see cref="Spline"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Dequantize(
            Spline.Point startingPoint,
            int quantizationAdjustment,
            (float YToX, float YToB) correlation,
            ulong imageSize,
            ref ulong totalEstimatedAreaReached,
            Spline result)
        {
            const ulong kOne = 1UL;
            ulong areaLimit  = Math.Min(1024UL * imageSize + (kOne << 32), kOne << 42);

            if (!ReconstructControlPoints(startingPoint, areaLimit, result.ControlPoints, out ulong manhattanDistance))
            {
                return Status.Failure("Spline control points out of bounds or area limit exceeded");
            }

            float invQuant = InvAdjustedQuant(quantizationAdjustment);
            DequantizeColorDct(invQuant, correlation.YToX, correlation.YToB, result);

            ulong logColor     = EstimateLogColorInfluence(invQuant, correlation.YToX, correlation.YToB);
            float weightLimit  = MathF.Ceiling(MathF.Sqrt(((float)areaLimit / logColor) / Math.Max(1UL, manhattanDistance)));
            ulong widthEst     = DequantizeSigmaDctAndEstimateWidth(invQuant, weightLimit, logColor, result);

            totalEstimatedAreaReached += widthEst * manhattanDistance;

            if (totalEstimatedAreaReached > areaLimit)
            {
                return Status.Failure("Too large total_estimated_area reached");
            }

            return true;
        }

        /// <summary>
        /// Decodes this <see cref="QuantizedSpline"/> from <paramref name="br"/> using <paramref name="decoder"/>.
        /// </summary>
        /// <param name="contextMap">ANS context map.</param>
        /// <param name="decoder">ANS symbol reader.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="maxControlPoints">Maximum allowed control points in the frame.</param>
        /// <param name="totalNumControlPoints">Cumulative control point counter across all splines.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Decode(
            ReadOnlySpan<byte> contextMap,
            ANSSymbolReader decoder,
            BitReader br,
            nuint maxControlPoints,
            ref nuint totalNumControlPoints)
        {
            nuint numControlPoints = decoder.ReadHybridUint(SplineEntropyContexts.kNumControlPointsContext, br, contextMap);

            if (numControlPoints > maxControlPoints)
            {
                return Status.Failure("Too many control points");
            }

            totalNumControlPoints += numControlPoints;

            if (totalNumControlPoints > maxControlPoints)
            {
                return Status.Failure("Too many control points");
            }

            _controlPoints.Clear();
            const long kDeltaLimit = 1L << 30;

            for (nuint i = 0; i < numControlPoints; i++)
            {
                long dx = PackSigned.Unpack(decoder.ReadHybridUint(SplineEntropyContexts.kControlPointsContext, br, contextMap));
                long dy = PackSigned.Unpack(decoder.ReadHybridUint(SplineEntropyContexts.kControlPointsContext, br, contextMap));

                if (dx >= kDeltaLimit || dx <= -kDeltaLimit || dy >= kDeltaLimit || dy <= -kDeltaLimit)
                {
                    return Status.Failure("Spline delta-delta is out of bounds");
                }

                _controlPoints.Add((dx, dy));
            }

            for (int c = 0; c < 3; c++)
            {
                if (!DecodeDctArray(decoder, br, contextMap, _colorDct[c]))
                {
                    return false;
                }
            }

            return DecodeDctArray(decoder, br, contextMap, _sigmaDct);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float AdjustedQuant(int adjustment)
        {
            return adjustment >= 0
                ? 1.0f + 0.125f * adjustment
                : 1.0f / (1.0f - 0.125f * adjustment);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float InvAdjustedQuant(int adjustment)
        {
            return adjustment >= 0
                ? 1.0f / (1.0f + 0.125f * adjustment)
                : 1.0f - 0.125f * adjustment;
        }

        internal static Status ValidateSplinePointPos(double x, double y)
        {
            const double kSplinePosLimit = 1u << 23;

            if (x >= kSplinePosLimit || x <= -kSplinePosLimit ||
                y >= kSplinePosLimit || y <= -kSplinePosLimit)
            {
                return Status.Failure("Spline coordinates out of bounds");
            }

            return true;
        }

        private static void EncodeControlPointDeltas(
            IReadOnlyList<Spline.Point> srcPoints,
            List<(long X, long Y)> dstDeltas)
        {
            dstDeltas.Clear();
            Spline.Point start = srcPoints[0];
            int previousX      = (int)MathF.Round(start.X, MidpointRounding.AwayFromZero);
            int previousY      = (int)MathF.Round(start.Y, MidpointRounding.AwayFromZero);
            int previousDeltaX = 0;
            int previousDeltaY = 0;

            for (int i = 1; i < srcPoints.Count; i++)
            {
                int newX      = (int)MathF.Round(srcPoints[i].X, MidpointRounding.AwayFromZero);
                int newY      = (int)MathF.Round(srcPoints[i].Y, MidpointRounding.AwayFromZero);
                int newDeltaX = newX - previousX;
                int newDeltaY = newY - previousY;

                dstDeltas.Add((newDeltaX - previousDeltaX, newDeltaY - previousDeltaY));
                previousDeltaX = newDeltaX;
                previousDeltaY = newDeltaY;
                previousX      = newX;
                previousY      = newY;
            }
        }

        private static void QuantizeDctCoefficients(
            Spline original,
            int quantizationAdjustment,
            float yToX,
            float yToB,
            QuantizedSpline result)
        {
            float quant    = AdjustedQuant(quantizationAdjustment);
            float invQuant = InvAdjustedQuant(quantizationAdjustment);

            foreach (int c in ChannelOrder)
            {
                float factor = c == 0 ? yToX : (c == 1 ? 0.0f : yToB);

                for (int i = 0; i < 32; ++i)
                {
                    float dctFactor    = i == 0 ? DctScales.kSqrt2 : 1.0f;
                    float invDctFactor = i == 0 ? DctScales.kSqrt0_5 : 1.0f;
                    float restoredY    = result._colorDct[1][i] * invDctFactor * ChannelWeight[1] * invQuant;
                    float decorrelated = original.ColorDct[c][i] - factor * restoredY;
                    result._colorDct[c][i] = ToClampedInt(decorrelated * dctFactor * quant / ChannelWeight[c]);
                }
            }

            for (int i = 0; i < 32; ++i)
            {
                float dctFactor     = i == 0 ? DctScales.kSqrt2 : 1.0f;
                result._sigmaDct[i] = ToClampedInt(original.SigmaDct[i] * dctFactor * quant / ChannelWeight[3]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ToClampedInt(float v)
        {
            const float kMax = int.MaxValue - 127;
            const float kMin = -kMax;

            return (int)MathF.Round(Common.Clamp1(v, kMin, kMax), MidpointRounding.AwayFromZero);
        }

        private bool ReconstructControlPoints(
            Spline.Point startingPoint,
            ulong areaLimit,
            List<Spline.Point> dstPoints,
            out ulong manhattanDistance)
        {
            manhattanDistance = 0;
            dstPoints.Clear();

            float px = MathF.Round(startingPoint.X, MidpointRounding.AwayFromZero);
            float py = MathF.Round(startingPoint.Y, MidpointRounding.AwayFromZero);

            if (!ValidateSplinePointPos(px, py))
            {
                return false;
            }

            int currentX      = (int)px;
            int currentY      = (int)py;
            int currentDeltaX = 0;
            int currentDeltaY = 0;
            dstPoints.Add(new Spline.Point(currentX, currentY));

            for (int i = 0; i < _controlPoints.Count; i++)
            {
                currentDeltaX     += (int)_controlPoints[i].X;
                currentDeltaY     += (int)_controlPoints[i].Y;
                manhattanDistance += (ulong)Math.Abs((long)currentDeltaX) + (ulong)Math.Abs((long)currentDeltaY);

                if (manhattanDistance > areaLimit || !ValidateSplinePointPos(currentDeltaX, currentDeltaY))
                {
                    return false;
                }

                currentX += currentDeltaX;
                currentY += currentDeltaY;

                if (!ValidateSplinePointPos(currentX, currentY))
                {
                    return false;
                }

                dstPoints.Add(new Spline.Point(currentX, currentY));
            }

            return true;
        }

        private void DequantizeColorDct(float invQuant, float yToX, float yToB, Spline result)
        {
            for (int c = 0; c < 3; ++c)
            {
                for (int i = 0; i < 32; ++i)
                {
                    float invDctFactor     = i == 0 ? DctScales.kSqrt0_5 : 1.0f;
                    result.ColorDct[c][i] = _colorDct[c][i] * invDctFactor * ChannelWeight[c] * invQuant;
                }
            }

            for (int i = 0; i < 32; ++i)
            {
                result.ColorDct[0][i] += yToX * result.ColorDct[1][i];
                result.ColorDct[2][i] += yToB * result.ColorDct[1][i];
            }
        }

        private ulong EstimateLogColorInfluence(float invQuant, float yToX, float yToB)
        {
            Span<ulong> color = stackalloc ulong[3];

            for (int c = 0; c < 3; ++c)
            {
                for (int i = 0; i < 32; ++i)
                {
                    color[c] += (ulong)MathF.Ceiling(invQuant * Math.Abs((long)_colorDct[c][i]));
                }
            }

            color[0] += (ulong)MathF.Ceiling(MathF.Abs(yToX)) * color[1];
            color[2] += (ulong)MathF.Ceiling(MathF.Abs(yToB)) * color[1];

            ulong maxColor = Math.Max(color[1], Math.Max(color[0], color[2]));

            return Math.Max(1UL, (ulong)Bits.CeilLog2Nonzero(1UL + maxColor));
        }

        private ulong DequantizeSigmaDctAndEstimateWidth(
            float invQuant,
            float weightLimit,
            ulong logColor,
            Spline result)
        {
            ulong widthEstimate = 0;

            for (int i = 0; i < 32; ++i)
            {
                float invDctFactor   = i == 0 ? DctScales.kSqrt0_5 : 1.0f;
                result.SigmaDct[i]   = _sigmaDct[i] * invDctFactor * ChannelWeight[3] * invQuant;
                float weightF        = MathF.Ceiling(invQuant * Math.Abs((long)_sigmaDct[i]));
                ulong weight         = (ulong)MathF.Min(weightLimit, MathF.Max(1.0f, weightF));
                widthEstimate       += weight * weight * logColor;
            }

            return widthEstimate;
        }

        private static Status DecodeDctArray(
            ANSSymbolReader decoder,
            BitReader br,
            ReadOnlySpan<byte> contextMap,
            Span<int> dct)
        {
            for (int i = 0; i < 32; ++i)
            {
                dct[i] = PackSigned.Unpack(decoder.ReadHybridUint(SplineEntropyContexts.kDCTContext, br, contextMap));

                if (dct[i] == int.MinValue)
                {
                    return Status.Failure("The weird number in spline DCT");
                }
            }

            return true;
        }
    }

    /// <summary>
    /// A single drawable Gaussian splat segment of a spline.
    /// Ported from <c>jxl::SplineSegment</c> in <c>lib/jxl/splines.h</c>.
    /// </summary>
    public struct SplineSegment
    {
        /// <summary>Horizontal Gaussian center.</summary>
        public float CenterX;

        /// <summary>Vertical Gaussian center.</summary>
        public float CenterY;

        /// <summary>Maximum rendering distance in pixels.</summary>
        public float MaximumDistance;

        /// <summary>Reciprocal of Gaussian sigma (<c>1.0f / sigma</c>).</summary>
        public float InvSigma;

        /// <summary><c>0.25f * sigma * intensity</c>.</summary>
        public float SigmaOver4TimesIntensity;

        /// <summary>Channel X color multiplier.</summary>
        public float ColorX;

        /// <summary>Channel Y color multiplier.</summary>
        public float ColorY;

        /// <summary>Channel B color multiplier.</summary>
        public float ColorB;
    }

    /// <summary>
    /// Half-open vertical row span <c>[Start, End)</c> covered by a <see cref="SplineSegment"/>.
    /// Ported from <c>jxl::SplineSegmentSpan</c> in <c>lib/jxl/splines.h</c>.
    /// </summary>
    public readonly struct SplineSegmentSpan
    {
        /// <summary>Inclusive starting row index.</summary>
        public readonly nuint Start;

        /// <summary>Exclusive ending row index.</summary>
        public readonly nuint End;

        /// <summary>
        /// Initializes a new instance of the <see cref="SplineSegmentSpan"/> struct.
        /// </summary>
        /// <param name="start">Inclusive starting row index.</param>
        /// <param name="end">Exclusive ending row index.</param>
        public SplineSegmentSpan(nuint start, nuint end)
        {
            Start = start;
            End   = end;
        }
    }

    /// <summary>
    /// View over quantized splines and their starting control points.
    /// Ported from <c>jxl::SplineDataView</c> in <c>lib/jxl/splines.h</c>.
    /// </summary>
    public readonly struct SplineDataView
    {
        /// <summary>Quantized splines.</summary>
        public readonly IReadOnlyList<QuantizedSpline> Splines;

        /// <summary>Starting control points corresponding to each spline.</summary>
        public readonly IReadOnlyList<Spline.Point> StartingPoints;

        /// <summary>
        /// Initializes a new instance of the <see cref="SplineDataView"/> struct.
        /// </summary>
        /// <param name="splines">Quantized splines list.</param>
        /// <param name="startingPoints">Starting points list.</param>
        public SplineDataView(IReadOnlyList<QuantizedSpline> splines, IReadOnlyList<Spline.Point> startingPoints)
        {
            Splines        = splines;
            StartingPoints = startingPoints;
        }

        /// <summary>Returns whether any splines are present.</summary>
        public bool HasAny => Splines is not null && Splines.Count > 0;
    }

    /// <summary>
    /// Decodes, caches, and renders Gaussian-splatted Catmull-Rom splines onto XYB opsin planes.
    /// Ported from <c>jxl::Splines</c> in <c>lib/jxl/splines.h</c> and <c>lib/jxl/splines.cc</c>.
    /// </summary>
    public sealed class Splines : IDisposable
    {
        private const nuint kMaxNumControlPoints             = 1u << 20;
        private const nuint kMaxNumControlPointsPerPixelRatio = 2;

        private static readonly float[] ContinuousIdctMultipliers = InitContinuousIdctMultipliers();

        private readonly JxlMemoryManager? _memoryManager;
        private int _quantizationAdjustment;
        private readonly List<QuantizedSpline> _splinesStorage     = new List<QuantizedSpline>();
        private readonly List<Spline.Point> _startingPointsStorage = new List<Spline.Point>();
        private SplineDataView _data;
        private readonly List<SplineSegment> _segments = new List<SplineSegment>();
        private nuint[]? _segmentIndices;
        private nint[]? _segmentYStart;

        /// <summary>
        /// Initializes a new instance of the <see cref="Splines"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for draw cache allocations.</param>
        public Splines(JxlMemoryManager? memoryManager)
        {
            _memoryManager = memoryManager;
            Clear();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Splines"/> class for testing with pre-quantized splines.
        /// </summary>
        /// <param name="quantizationAdjustment">Quantization precision adjustment.</param>
        /// <param name="splines">Quantized splines.</param>
        /// <param name="startingPoints">Starting points for each spline.</param>
        public Splines(
            int quantizationAdjustment,
            IReadOnlyList<QuantizedSpline> splines,
            IReadOnlyList<Spline.Point> startingPoints)
        {
            _memoryManager          = JxlMemoryManager.Default;
            _quantizationAdjustment = quantizationAdjustment;
            _splinesStorage.AddRange(splines);
            _startingPointsStorage.AddRange(startingPoints);
            _data = new SplineDataView(_splinesStorage, _startingPointsStorage);
        }

        /// <summary>Gets the quantized splines in this collection.</summary>
        public IReadOnlyList<QuantizedSpline> QuantizedSplines => _data.Splines ?? Array.Empty<QuantizedSpline>();

        /// <summary>Gets the starting points for each spline in this collection.</summary>
        public IReadOnlyList<Spline.Point> StartingPoints => _data.StartingPoints ?? Array.Empty<Spline.Point>();

        /// <summary>Gets the quantization precision adjustment.</summary>
        public int GetQuantizationAdjustment() => _quantizationAdjustment;

        /// <summary>
        /// Replaces the spline data view and clears any existing draw cache.
        /// </summary>
        /// <param name="data">New spline data view.</param>
        public void SetData(SplineDataView data)
        {
            Clear();
            _data = data;
        }

        /// <summary>
        /// Returns whether any splines are stored in this collection.
        /// </summary>
        /// <returns><see langword="true"/> if at least one spline is present.</returns>
        public bool HasAny() => _data.HasAny;

        /// <summary>
        /// Clears all splines and releases draw cache buffers.
        /// </summary>
        public void Clear()
        {
            _quantizationAdjustment = 0;
            _splinesStorage.Clear();
            _startingPointsStorage.Clear();
            _data = default;
            _segments.Clear();
            _segmentIndices = null;
            _segmentYStart  = null;
        }

        /// <summary>
        /// Decodes the frame splines from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="numPixels">Total number of pixels in the frame (<c>xsize * ysize</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Decode(BitReader br, nuint numPixels)
        {
            ANSCode code = new ANSCode();

            if (!DecAns.DecodeHistograms(
                    _memoryManager,
                    br,
                    SplineEntropyContexts.kNumSplineContexts,
                    code,
                    out byte[] contextMap))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerResult = ANSSymbolReader.Create(code, br);

            if (!readerResult.IsOk)
            {
                return readerResult.Status;
            }

            ANSSymbolReader decoder = readerResult.Value;
            nuint numSplines        = decoder.ReadHybridUint(SplineEntropyContexts.kNumSplinesContext, br, contextMap);
            nuint maxControlPoints  = Math.Min(kMaxNumControlPoints, numPixels / kMaxNumControlPointsPerPixelRatio);

            if (numSplines > maxControlPoints || numSplines + 1 > maxControlPoints)
            {
                return Status.Failure("Too many splines");
            }

            numSplines++;

            if (!DecodeAllStartingPoints(_startingPointsStorage, br, decoder, contextMap, numSplines))
            {
                return false;
            }

            _quantizationAdjustment = PackSigned.Unpack(
                decoder.ReadHybridUint(SplineEntropyContexts.kQuantizationAdjustmentContext, br, contextMap));

            _splinesStorage.Clear();
            nuint numControlPoints = numSplines;

            for (nuint i = 0; i < numSplines; ++i)
            {
                QuantizedSpline spline = new QuantizedSpline();

                if (!spline.Decode(contextMap, decoder, br, maxControlPoints, ref numControlPoints))
                {
                    return false;
                }

                _splinesStorage.Add(spline);
            }

            if (!decoder.CheckANSFinalState())
            {
                return Status.Failure("Invalid ANS final state in Splines.Decode");
            }

            _data = new SplineDataView(_splinesStorage, _startingPointsStorage);

            return HasAny() ? Status.OkStatus : Status.Failure("Decoded splines but got none");
        }

        /// <summary>
        /// Initializes the precomputed Gaussian segment draw cache for an image of size <paramref name="imageXSize"/> x <paramref name="imageYSize"/>.
        /// </summary>
        /// <param name="imageXSize">Frame width in pixels.</param>
        /// <param name="imageYSize">Frame height in pixels.</param>
        /// <param name="colorCorrelation">Global color correlation parameters.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitializeDrawCache(
            nuint imageXSize,
            nuint imageYSize,
            ColorCorrelation colorCorrelation)
        {
            _segments.Clear();
            _segmentIndices = null;
            _segmentYStart  = null;

            StatusOr<List<Spline>> dequantResult = DequantizeAllSplines(imageXSize, imageYSize, colorCorrelation);

            if (!dequantResult.IsOk)
            {
                return dequantResult.Status;
            }

            List<SplineSegmentSpan> segmentSpans = new List<SplineSegmentSpan>();
            List<Spline.Point> intermediatePts   = new List<Spline.Point>();

            foreach (Spline spline in dequantResult.Value)
            {
                BuildSplineSegments(imageYSize, spline, intermediatePts, segmentSpans);
            }

            return BuildRowIndexTables(imageYSize, segmentSpans);
        }

        /// <summary>
        /// Adds rendered splines onto <paramref name="opsin"/> within <paramref name="opsinRect"/>.
        /// </summary>
        /// <param name="opsin">Target 3-plane XYB opsin image.</param>
        /// <param name="opsinRect">Region in <paramref name="opsin"/> to update.</param>
        public void AddTo(Image3<float> opsin, Rect opsinRect)
        {
            Apply(opsin, opsinRect, add: true);
        }

        /// <summary>
        /// Adds rendered splines onto a single horizontal row segment <c>[x0, x1)</c> at row <paramref name="y"/>.
        /// </summary>
        /// <param name="rowX">X channel row span (starting at <paramref name="x0"/>).</param>
        /// <param name="rowY">Y channel row span (starting at <paramref name="x0"/>).</param>
        /// <param name="rowB">B channel row span (starting at <paramref name="x0"/>).</param>
        /// <param name="y">Row index in the image.</param>
        /// <param name="x0">Inclusive starting X coordinate.</param>
        /// <param name="x1">Exclusive ending X coordinate.</param>
        public void AddToRow(Span<float> rowX, Span<float> rowY, Span<float> rowB, nuint y, nuint x0, nuint x1)
        {
            ApplyToRow(rowX, rowY, rowB, y, x0, x1, add: true);
        }

        /// <summary>
        /// Subtracts rendered splines from <paramref name="opsin"/> across its entire bounds.
        /// </summary>
        /// <param name="opsin">Target 3-plane XYB opsin image.</param>
        public void SubtractFrom(Image3<float> opsin)
        {
            Apply(opsin, new Rect(opsin), add: false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Clear();
        }

        private static float[] InitContinuousIdctMultipliers()
        {
            float[] mults = new float[32];

            for (int i = 0; i < 32; i++)
            {
                mults[i] = (float)(Common.kPi / 32.0 * i);
            }

            return mults;
        }

        private static float ContinuousIDCT(ReadOnlySpan<float> dct, float t)
        {
            float result   = 0.0f;
            float tAndHalf = t + 0.5f;

            for (int i = 0; i < 32; i++)
            {
                float cosArg   = ContinuousIdctMultipliers[i] * tAndHalf;
                float cosVal   = FastMath.FastCosf(cosArg);
                float localRes = dct[i] * cosVal;
                result         = MathF.FusedMultiplyAdd(DctScales.kSqrt2, localRes, result);
            }

            return result;
        }

        private static Status DecodeAllStartingPoints(
            List<Spline.Point> points,
            BitReader br,
            ANSSymbolReader reader,
            ReadOnlySpan<byte> contextMap,
            nuint numSplines)
        {
            points.Clear();
            long lastX = 0;
            long lastY = 0;

            for (nuint i = 0; i < numSplines; i++)
            {
                uint dx = reader.ReadHybridUint(SplineEntropyContexts.kStartingPositionContext, br, contextMap);
                uint dy = reader.ReadHybridUint(SplineEntropyContexts.kStartingPositionContext, br, contextMap);
                long x  = i != 0 ? PackSigned.Unpack(dx) + lastX : dx;
                long y  = i != 0 ? PackSigned.Unpack(dy) + lastY : dy;

                if (!QuantizedSpline.ValidateSplinePointPos(x, y))
                {
                    return Status.Failure("Spline starting point out of bounds");
                }

                points.Add(new Spline.Point(x, y));
                lastX = x;
                lastY = y;
            }

            return true;
        }

        private StatusOr<List<Spline>> DequantizeAllSplines(
            nuint imageXSize,
            nuint imageYSize,
            ColorCorrelation colorCorrelation)
        {
            IReadOnlyList<QuantizedSpline> qSplines = QuantizedSplines;
            IReadOnlyList<Spline.Point> starts      = StartingPoints;
            List<Spline> splines                    = new List<Spline>(qSplines.Count);
            ulong totalEstimatedArea                = 0;
            (float, float) corr                     = (colorCorrelation.YtoXRatio(0), colorCorrelation.YtoBRatio(0));
            ulong imageSize                         = imageXSize * imageYSize;

            for (int i = 0; i < qSplines.Count; ++i)
            {
                Spline spline = new Spline();

                if (!qSplines[i].Dequantize(starts[i], _quantizationAdjustment, corr, imageSize, ref totalEstimatedArea, spline))
                {
                    return Status.Failure("Failed to dequantize spline");
                }

                if (HasAdjacentIdenticalPoints(spline.ControlPoints))
                {
                    return Status.Failure("Identical successive control points in spline");
                }

                splines.Add(spline);
            }

            return splines;
        }

        private static bool HasAdjacentIdenticalPoints(List<Spline.Point> pts)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (pts[i] == pts[i + 1])
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildSplineSegments(
            nuint imageYSize,
            Spline spline,
            List<Spline.Point> intermediatePoints,
            List<SplineSegmentSpan> segmentSpans)
        {
            intermediatePoints.Clear();
            DrawCentripetalCatmullRomSpline(spline.ControlPoints, intermediatePoints);

            List<(Spline.Point Point, float Multiplier)> pointsToDraw = new List<(Spline.Point, float)>();
            ForEachEquallySpacedPoint(intermediatePoints, pointsToDraw);

            float arcLength = (pointsToDraw.Count - 2) * Spline.kDesiredRenderingDistance +
                              pointsToDraw[pointsToDraw.Count - 1].Multiplier;

            if (arcLength <= 0.0f)
            {
                return;
            }

            SegmentsFromPoints(imageYSize, spline, pointsToDraw, arcLength, _segments, segmentSpans);
        }

        private static void DrawCentripetalCatmullRomSpline(
            IReadOnlyList<Spline.Point> controlPoints,
            List<Spline.Point> result)
        {
            if (controlPoints.Count == 0)
            {
                return;
            }

            if (controlPoints.Count == 1)
            {
                result.Add(controlPoints[0]);

                return;
            }

            const int kNumPoints      = 16;
            List<Spline.Point> points = new List<Spline.Point>(controlPoints.Count + 2);
            Spline.Point p0           = controlPoints[0];
            Spline.Point p1           = controlPoints[1];
            points.Add(new Spline.Point(p0.X + (p0.X - p1.X), p0.Y + (p0.Y - p1.Y)));
            points.AddRange(controlPoints);

            Spline.Point pLast     = controlPoints[controlPoints.Count - 1];
            Spline.Point pPrevLast = controlPoints[controlPoints.Count - 2];
            points.Add(new Spline.Point(pLast.X + (pLast.X - pPrevLast.X), pLast.Y + (pLast.Y - pPrevLast.Y)));

            for (int start = 0; start < points.Count - 3; ++start)
            {
                InterpolateCatmullRomSpan(points, start, kNumPoints, result);
            }

            result.Add(points[points.Count - 2]);
        }

        private static void InterpolateCatmullRomSpan(
            List<Spline.Point> points,
            int start,
            int numPoints,
            List<Spline.Point> result)
        {
            result.Add(points[start + 1]);
            Span<float> d = stackalloc float[3];
            Span<float> t = stackalloc float[4];
            t[0]          = 0.0f;

            for (int k = 0; k < 3; ++k)
            {
                float dx = points[start + k + 1].X - points[start + k].X;
                float dy = points[start + k + 1].Y - points[start + k].Y;
                d[k]     = MathF.Sqrt(MathF.Sqrt(dx * dx + dy * dy));
                t[k + 1] = t[k] + d[k];
            }

            Span<Spline.Point> a = stackalloc Spline.Point[3];
            Span<Spline.Point> b = stackalloc Spline.Point[2];

            for (int i = 1; i < numPoints; ++i)
            {
                float tt = d[0] + ((float)i / numPoints) * d[1];

                for (int k = 0; k < 3; ++k)
                {
                    a[k] = LerpPoint(points[start + k], points[start + k + 1], (tt - t[k]) / d[k]);
                }

                for (int k = 0; k < 2; ++k)
                {
                    b[k] = LerpPoint(a[k], a[k + 1], (tt - t[k]) / (d[k] + d[k + 1]));
                }

                result.Add(LerpPoint(b[0], b[1], (tt - t[1]) / d[1]));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Spline.Point LerpPoint(Spline.Point p0, Spline.Point p1, float ratio)
        {
            return new Spline.Point(
                p0.X + ratio * (p1.X - p0.X),
                p0.Y + ratio * (p1.Y - p0.Y));
        }

        private static void ForEachEquallySpacedPoint(
            List<Spline.Point> points,
            List<(Spline.Point Point, float Multiplier)> output)
        {
            Spline.Point current = points[0];
            output.Add((current, Spline.kDesiredRenderingDistance));
            int nextIdx = 0;

            while (nextIdx < points.Count)
            {
                Spline.Point previous      = current;
                float arclengthFromPrev    = 0.0f;

                for (; ;)
                {
                    if (nextIdx == points.Count)
                    {
                        output.Add((previous, arclengthFromPrev));

                        return;
                    }

                    Spline.Point nextPt   = points[nextIdx];
                    float dx              = nextPt.X - previous.X;
                    float dy              = nextPt.Y - previous.Y;
                    float arclengthToNext = MathF.Sqrt(dx * dx + dy * dy);

                    if (arclengthFromPrev + arclengthToNext >= Spline.kDesiredRenderingDistance)
                    {
                        float ratio = (Spline.kDesiredRenderingDistance - arclengthFromPrev) / arclengthToNext;
                        current     = LerpPoint(previous, nextPt, ratio);
                        output.Add((current, Spline.kDesiredRenderingDistance));

                        break;
                    }

                    arclengthFromPrev += arclengthToNext;
                    previous           = nextPt;
                    ++nextIdx;
                }
            }
        }

        private static void SegmentsFromPoints(
            nuint imageYSize,
            Spline spline,
            List<(Spline.Point Point, float Multiplier)> pointsToDraw,
            float arcLength,
            List<SplineSegment> segments,
            List<SplineSegmentSpan> segmentSpans)
        {
            float invArcLength = 1.0f / arcLength;
            Span<float> color  = stackalloc float[3];

            for (int k = 0; k < pointsToDraw.Count; k++)
            {
                Spline.Point point     = pointsToDraw[k].Point;
                float multiplier       = pointsToDraw[k].Multiplier;
                float progressAlongArc = MathF.Min(1.0f, (k * Spline.kDesiredRenderingDistance) * invArcLength);
                float t                = 31.0f * progressAlongArc;

                for (int c = 0; c < 3; ++c)
                {
                    color[c] = ContinuousIDCT(spline.ColorDct[c], t);
                }

                float sigma = ContinuousIDCT(spline.SigmaDct, t);
                ComputeSegments(imageYSize, point, multiplier, color, sigma, segments, segmentSpans);
            }
        }

        private static void ComputeSegments(
            nuint imageYSize,
            Spline.Point center,
            float intensity,
            ReadOnlySpan<float> color,
            float sigma,
            List<SplineSegment> segments,
            List<SplineSegmentSpan> segmentSpans)
        {
            if (!(float.IsFinite(sigma) && sigma != 0.0f && float.IsFinite(1.0f / sigma) && float.IsFinite(intensity)))
            {
                return;
            }

            const float kDistanceExp = 3.0f;
            float maxColor           = 0.01f;

            for (int c = 0; c < 3; c++)
            {
                maxColor = MathF.Max(maxColor, MathF.Abs(color[c] * intensity));
            }

            float maximumDistance = MathF.Sqrt(
                -2.0f * sigma * sigma * (MathF.Log(0.1f) * kDistanceExp - MathF.Log(maxColor)));

            nint y0 = (nint)MathF.Round(center.Y - maximumDistance, MidpointRounding.AwayFromZero);
            y0      = Math.Max(y0, (nint)0);
            nint y1 = (nint)MathF.Round(center.Y + maximumDistance, MidpointRounding.AwayFromZero) + 1;
            y1      = Math.Min(y1, (nint)imageYSize);

            if (y1 <= y0)
            {
                return;
            }

            segments.Add(new SplineSegment
            {
                CenterY                  = center.Y,
                CenterX                  = center.X,
                ColorX                   = color[0],
                ColorY                   = color[1],
                ColorB                   = color[2],
                InvSigma                 = 1.0f / sigma,
                SigmaOver4TimesIntensity = 0.25f * sigma * intensity,
                MaximumDistance          = maximumDistance,
            });

            segmentSpans.Add(new SplineSegmentSpan((nuint)y0, (nuint)y1));
        }

        private Status BuildRowIndexTables(nuint imageYSize, List<SplineSegmentSpan> segmentSpans)
        {
            nint[] segmentYStart  = new nint[imageYSize + 2];
            _segmentYStart        = segmentYStart;
            Span<nint> population = segmentYStart.AsSpan(1);

            for (int i = 0; i < segmentSpans.Count; i++)
            {
                population[(int)segmentSpans[i].Start]++;
                population[(int)segmentSpans[i].End]--;
            }

            nuint total   = 0;
            nint coverage = 0;

            for (nuint y = 0; y < imageYSize; y++)
            {
                coverage          += population[(int)y];
                population[(int)y] = (nint)total;
                total             += (nuint)coverage;
            }

            nuint[] segmentIndices = new nuint[total];
            _segmentIndices        = segmentIndices;

            for (nuint i = 0; i < (nuint)_segments.Count; ++i)
            {
                SplineSegmentSpan span = segmentSpans[(int)i];

                for (nuint y = span.Start; y < span.End; y++)
                {
                    segmentIndices[population[(int)y]++] = i;
                }
            }

            return true;
        }

        private void Apply(Image3<float> opsin, Rect opsinRect, bool add)
        {
            if (_segments.Count == 0)
            {
                return;
            }

            nuint y0 = (nuint)opsinRect.Y0;
            nuint x0 = (nuint)opsinRect.X0;
            nuint x1 = (nuint)opsinRect.X1;
            nuint h  = (nuint)opsinRect.YSize;

            for (nuint y = 0; y < h; y++)
            {
                nuint rowY = y0 + y;

                ApplyToRow(
                    opsin.PlaneRow(0, rowY).Slice((int)x0),
                    opsin.PlaneRow(1, rowY).Slice((int)x0),
                    opsin.PlaneRow(2, rowY).Slice((int)x0),
                    rowY,
                    x0,
                    x1,
                    add);
            }
        }

        private void ApplyToRow(
            Span<float> rowX,
            Span<float> rowY,
            Span<float> rowB,
            nuint y,
            nuint x0,
            nuint x1,
            bool add)
        {
            if (_segments.Count == 0 || _segmentIndices is null || _segmentYStart is null)
            {
                return;
            }

            nint startIdx = _segmentYStart[y];
            nint endIdx   = _segmentYStart[y + 1];

            for (nint i = startIdx; i < endIdx; i++)
            {
                SplineSegment seg = _segments[(int)_segmentIndices[i]];

                DrawSegmentOnRow(in seg, add, y, x0, x1, rowX, rowY, rowB);
            }
        }

        private static void DrawSegmentOnRow(
            in SplineSegment segment,
            bool add,
            nuint y,
            nuint x0,
            nuint x1,
            Span<float> rowX,
            Span<float> rowY,
            Span<float> rowB)
        {
            nint start = (nint)MathF.Round(segment.CenterX - segment.MaximumDistance, MidpointRounding.AwayFromZero);
            nint end   = (nint)MathF.Round(segment.CenterX + segment.MaximumDistance, MidpointRounding.AwayFromZero);

            if (end < (nint)x0 || start >= (nint)x1)
            {
                return;
            }

            nuint spanX0           = (nuint)(Math.Max((nint)x0, start) - (nint)x0);
            nuint spanX1           = (nuint)(Math.Min((nint)x1, end + 1) - (nint)x0);
            float dy               = y - segment.CenterY;
            float dy2              = dy * dy;
            float cx               = add ? segment.ColorX : -segment.ColorX;
            float cy               = add ? segment.ColorY : -segment.ColorY;
            float cb               = add ? segment.ColorB : -segment.ColorB;
            const float kOneOver2s2 = 0.353553391f;

            for (nuint x = spanX0; x < spanX1; ++x)
            {
                float dx       = (x + x0) - segment.CenterX;
                float distance = MathF.Sqrt(MathF.FusedMultiplyAdd(dx, dx, dy2));
                float erfPlus  = FastMath.FastErff(MathF.FusedMultiplyAdd(distance, 0.5f, kOneOver2s2) * segment.InvSigma);
                float erfMinus = FastMath.FastErff(MathF.FusedMultiplyAdd(distance, 0.5f, -kOneOver2s2) * segment.InvSigma);
                float oneDim   = erfPlus - erfMinus;
                float localInt = segment.SigmaOver4TimesIntensity * (oneDim * oneDim);

                rowX[(int)x] = MathF.FusedMultiplyAdd(cx, localInt, rowX[(int)x]);
                rowY[(int)x] = MathF.FusedMultiplyAdd(cy, localInt, rowY[(int)x]);
                rowB[(int)x] = MathF.FusedMultiplyAdd(cb, localInt, rowB[(int)x]);
            }
        }
    }
}
