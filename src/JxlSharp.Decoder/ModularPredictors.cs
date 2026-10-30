// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Serializable header parameters for the Modular Weighted Predictor.
    /// Ported from <c>jxl::weighted::Header</c> in <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public sealed class WeightedHeader : Fields
    {
        /// <summary>Number of sub-predictors combined by the weighted predictor (4).</summary>
        public const int KNumPredictors = 4;

        /// <summary>Extra precision bits used internally by the weighted predictor (3).</summary>
        public const int KPredExtraBits = 3;

        /// <summary>Rounding bias before shifting right by <see cref="KPredExtraBits"/> (3).</summary>
        public const long KPredictionRound = ((1L << KPredExtraBits) >> 1) - 1L;

        /// <summary>Number of MA tree properties produced by the weighted predictor (1).</summary>
        public const int KNumProperties = 1;

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Sub-predictor 1 weight factor.</summary>
        public int P1C;

        /// <summary>Sub-predictor 2 weight factor.</summary>
        public int P2C;

        /// <summary>Sub-predictor 3 coefficient A.</summary>
        public int P3Ca;

        /// <summary>Sub-predictor 3 coefficient B.</summary>
        public int P3Cb;

        /// <summary>Sub-predictor 3 coefficient C.</summary>
        public int P3Cc;

        /// <summary>Sub-predictor 3 coefficient D.</summary>
        public int P3Cd;

        /// <summary>Sub-predictor 3 coefficient E.</summary>
        public int P3Ce;

        /// <summary>Maximum error weights for the 4 sub-predictors.</summary>
        public uint[] W = new uint[KNumPredictors];

        /// <summary>
        /// Initializes a new instance of the <see cref="WeightedHeader"/> class with default values.
        /// </summary>
        public WeightedHeader()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "WeightedPredictorHeader";

        /// <summary>
        /// Creates a deep copy of this <see cref="WeightedHeader"/>.
        /// </summary>
        /// <returns>Cloned <see cref="WeightedHeader"/>.</returns>
        public WeightedHeader Clone()
        {
            WeightedHeader copy = new WeightedHeader
            {
                AllDefault = AllDefault,
                P1C        = P1C,
                P2C        = P2C,
                P3Ca       = P3Ca,
                P3Cb       = P3Cb,
                P3Cc       = P3Cc,
                P3Cd       = P3Cd,
                P3Ce       = P3Ce,
            };
            Array.Copy(W, copy.W, KNumPredictors);

            return copy;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                // Overwrite all serialized fields, but not any nonserialized_*.
                visitor.SetDefault(this);

                return true;
            }

            if (!VisitParam(visitor, 16, ref P1C) ||
                !VisitParam(visitor, 10, ref P2C) ||
                !VisitParam(visitor, 7, ref P3Ca) ||
                !VisitParam(visitor, 7, ref P3Cb) ||
                !VisitParam(visitor, 7, ref P3Cc) ||
                !VisitParam(visitor, 0, ref P3Cd) ||
                !VisitParam(visitor, 0, ref P3Ce))
            {
                return false;
            }

            return VisitWeights(visitor);
        }

        /// <summary>
        /// Sets the parameters to one of the encoder presets (0..4).
        /// Ported from <c>jxl::weighted::PredictorMode</c>.
        /// </summary>
        /// <param name="mode">Preset index.</param>
        public void SetPredictorMode(int mode)
        {
            switch (mode)
            {
                case 0:
                    // ~ lossless16 predictor
                    SetParams(0xd, 0xc, 0xc, 0xc, 16, 10);
                    SetP3Params(7, 7, 7, 0, 0);
                    break;
                case 1:
                    // ~ default lossless8 predictor
                    SetParams(0xd, 0xc, 0xc, 0xb, 8, 8);
                    SetP3Params(4, 0, 3, 23, 2);
                    break;
                case 2:
                    // ~ west lossless8 predictor
                    SetParams(0xd, 0xc, 0xd, 0xc, 10, 9);
                    SetP3Params(7, 0, 0, 16, 9);
                    break;
                case 3:
                    // ~ north lossless8 predictor
                    SetParams(0xd, 0xd, 0xc, 0xc, 16, 8);
                    SetP3Params(0, 16, 0, 23, 0);
                    break;
                default:
                    // something else, because why not
                    SetParams(0xd, 0xc, 0xc, 0xc, 10, 10);
                    SetP3Params(5, 5, 5, 12, 4);
                    break;
            }
        }

        private static Status VisitParam(Visitor visitor, uint defaultVal, ref int p)
        {
            uint up = (uint)p;

            if (!visitor.Bits(5, defaultVal, ref up))
            {
                return false;
            }

            p = (int)up;

            return true;
        }

        private Status VisitWeights(Visitor visitor)
        {
            if (!visitor.Bits(4, 0xd, ref W[0]) ||
                !visitor.Bits(4, 0xc, ref W[1]) ||
                !visitor.Bits(4, 0xc, ref W[2]) ||
                !visitor.Bits(4, 0xc, ref W[3]))
            {
                return false;
            }

            return true;
        }

        private void SetParams(uint w0, uint w1, uint w2, uint w3, int p1c, int p2c)
        {
            W[0] = w0;
            W[1] = w1;
            W[2] = w2;
            W[3] = w3;
            P1C  = p1c;
            P2C  = p2c;
        }

        private void SetP3Params(int ca, int cb, int cc, int cd, int ce)
        {
            P3Ca = ca;
            P3Cb = cb;
            P3Cc = cc;
            P3Cd = cd;
            P3Ce = ce;
        }
    }

    /// <summary>
    /// Neighboring pixel values for a single Modular pixel coordinate.
    /// </summary>
    public struct NeighborPixels
    {
        /// <summary>Left neighbor (W).</summary>
        public long Left;

        /// <summary>Top neighbor (N).</summary>
        public long Top;

        /// <summary>Second top neighbor (NN).</summary>
        public long TopTop;

        /// <summary>Top-left neighbor (NW).</summary>
        public long TopLeft;

        /// <summary>Top-right neighbor (NE).</summary>
        public long TopRight;

        /// <summary>Second left neighbor (WW).</summary>
        public long LeftLeft;

        /// <summary>Top-right-right neighbor (NEE).</summary>
        public long TopRightRight;
    }

    /// <summary>
    /// Per-channel state for the Modular Weighted Predictor.
    /// Ported from <c>jxl::weighted::State</c> in <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public sealed class WeightedState
    {
        // Allows to approximate division by a number from 1 to 64.
        // for (int i = 0; i < 64; i++) divlookup[i] = (1 << 24) / (i + 1);
        private static readonly uint[] DivLookup =
        {
            16777216, 8388608, 5592405, 4194304, 3355443, 2796202, 2396745, 2097152,
            1864135,  1677721, 1525201, 1398101, 1290555, 1198372, 1118481, 1048576,
            986895,   932067,  883011,  838860,  798915,  762600,  729444,  699050,
            671088,   645277,  621378,  599186,  578524,  559240,  541200,  524288,
            508400,   493447,  479349,  466033,  453438,  441505,  430185,  419430,
            409200,   399457,  390167,  381300,  372827,  364722,  356962,  349525,
            342392,   335544,  328965,  322638,  316551,  310689,  305040,  299593,
            294337,   289262,  284359,  279620,  275036,  270600,  266305,  262144,
        };

        private readonly nuint _xsize;
        private readonly int _stride;
        private readonly uint[] _predErrors0;
        private readonly uint[] _predErrors1;
        private readonly uint[] _predErrors2;
        private readonly uint[] _predErrors3;
        private readonly uint _w0;
        private readonly uint _w1;
        private readonly uint _w2;
        private readonly uint _w3;
        private readonly int _p1c;
        private readonly int _p2c;
        private readonly int _p3ca;
        private readonly int _p3cb;
        private readonly int _p3cc;
        private readonly int _p3cd;
        private readonly int _p3ce;
        private long _pred0;
        private long _pred1;
        private long _pred2;
        private long _pred3;
        private Vector256<long> _predictions;

        /// <summary>Sub-predictor outputs shifted left by <see cref="WeightedHeader.KPredExtraBits"/>.</summary>
        public readonly long[] Prediction = new long[WeightedHeader.KNumPredictors];

        /// <summary>Combined weighted prediction before shifting right.</summary>
        public long Pred;

        /// <summary>Accumulated sub-predictor absolute error buffers (2 rows each).</summary>
        public readonly uint[][] PredErrors = new uint[WeightedHeader.KNumPredictors][];

        /// <summary>Signed prediction error buffer (2 rows).</summary>
        public readonly int[] Error;

        /// <summary>Associated <see cref="WeightedHeader"/> configuration.</summary>
        public readonly WeightedHeader Header;

        /// <summary>Gets the channel width.</summary>
        public nuint XSize => _xsize;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeightedState"/> class.
        /// </summary>
        /// <param name="header">Weighted predictor header parameters.</param>
        /// <param name="xsize">Channel width.</param>
        /// <param name="ysize">Channel height.</param>
        public WeightedState(WeightedHeader header, nuint xsize, nuint ysize)
        {
            _             = ysize;
            Header        = header;
            _xsize        = xsize;
            _stride       = (int)xsize + 2;
            int rowBufLen = _stride * 2;

            for (int i = 0; i < WeightedHeader.KNumPredictors; i++)
            {
                PredErrors[i] = new uint[rowBufLen];
            }

            _predErrors0 = PredErrors[0];
            _predErrors1 = PredErrors[1];
            _predErrors2 = PredErrors[2];
            _predErrors3 = PredErrors[3];
            _w0          = header.W[0];
            _w1          = header.W[1];
            _w2          = header.W[2];
            _w3          = header.W[3];
            _p1c         = header.P1C;
            _p2c         = header.P2C;
            _p3ca        = header.P3Ca;
            _p3cb        = header.P3Cb;
            _p3cc        = header.P3Cc;
            _p3cd        = header.P3Cd;
            _p3ce        = header.P3Ce;
            Error        = new int[rowBufLen];
        }

        /// <summary>
        /// Shifts <paramref name="x"/> left by <see cref="WeightedHeader.KPredExtraBits"/>.
        /// </summary>
        /// <param name="x">Input value.</param>
        /// <returns>Shifted value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long AddBits(long x) => x << WeightedHeader.KPredExtraBits;

        /// <summary>
        /// Approximates <c>4 + (maxweight &lt;&lt; 24) / (x + 1)</c> without division.
        /// </summary>
        /// <param name="x">Accumulated error.</param>
        /// <param name="maxweight">Header weight parameter.</param>
        /// <returns>Computed weight.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ErrorWeight(ulong x, uint maxweight)
        {
            int shift = (int)Bits.FloorLog2Nonzero(x + 1UL) - 5;

            if (shift < 0)
            {
                shift = 0;
            }

            return 4u + ((maxweight * DivLookup[(int)(x >> shift)]) >> shift);
        }

        /// <summary>
        /// Approximates the weighted average of 4 predictions using <paramref name="w"/> without division.
        /// </summary>
        /// <param name="p">Array of 4 sub-predictions.</param>
        /// <param name="w">Span of 4 weights (must sum to at least 16).</param>
        /// <returns>Weighted average prediction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long WeightedAverage(ReadOnlySpan<long> p, Span<uint> w)
        {
            uint weightSum = w[0] + w[1] + w[2] + w[3];
            Debug.Assert(weightSum > 15);
            int logShift = (int)Bits.FloorLog2Nonzero(weightSum) - 4;
            weightSum    = 0;

            for (int i = 0; i < WeightedHeader.KNumPredictors; i++)
            {
                w[i]      >>= logShift;
                weightSum  += w[i];
            }

            long sum = (weightSum >> 1) - 1;

            for (int i = 0; i < WeightedHeader.KNumPredictors; i++)
            {
                sum += p[i] * w[i];
            }

            return (sum * DivLookup[(int)(weightSum - 1)]) >> 24;
        }

        /// <summary>
        /// Computes the weighted prediction for pixel <c>(x, y)</c> and optionally stores the WP property.
        /// </summary>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="xsize">Channel width.</param>
        /// <param name="neighbors">Neighboring pixel values (Top, Left, TopRight, TopLeft, TopTop).</param>
        /// <param name="properties">Optional property span to populate when <paramref name="computeProperties"/> is <c>true</c>.</param>
        /// <param name="offset">Property index at which to store the WP property.</param>
        /// <param name="computeProperties">Whether to compute and store the WP property.</param>
        /// <returns>Predicted sample value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Predict(
            nuint x,
            nuint y,
            nuint xsize,
            in NeighborPixels neighbors,
            Span<int> properties,
            int offset,
            bool computeProperties)
        {
            int stride  = _stride;
            int curRow  = (y & 1u) != 0 ? 0 : stride;
            int prevRow = (y & 1u) != 0 ? stride : 0;
            int posN    = prevRow + (int)x;
            int posNE   = x < xsize - 1u ? posN + 1 : posN;
            int posNW   = x > 0 ? posN - 1 : posN;

            uint w0 = ErrorWeight(_predErrors0[posN] + _predErrors0[posNE] + _predErrors0[posNW], _w0);
            uint w1 = ErrorWeight(_predErrors1[posN] + _predErrors1[posNE] + _predErrors1[posNW], _w1);
            uint w2 = ErrorWeight(_predErrors2[posN] + _predErrors2[posNE] + _predErrors2[posNW], _w2);
            uint w3 = ErrorWeight(_predErrors3[posN] + _predErrors3[posNE] + _predErrors3[posNW], _w3);

            long n  = AddBits(neighbors.Top);
            long w  = AddBits(neighbors.Left);
            long ne = AddBits(neighbors.TopRight);
            long nw = AddBits(neighbors.TopLeft);
            long nn = AddBits(neighbors.TopTop);

            long teW   = x == 0 ? 0 : Error[curRow + (int)x - 1];
            long teN   = Error[posN];
            long teNW  = Error[posNW];
            long sumWN = teN + teW;
            long teNE  = Error[posNE];

            if (computeProperties)
            {
                properties[offset] = (int)ComputeWpProperty(teW, teN, teNW, teNE);
            }

            long p0 = w + ne - n;
            long p1 = n - (((sumWN + teNE) * _p1c) >> 5);
            long p2 = w - (((sumWN + teNW) * _p2c) >> 5);
            long p3 = n - (((teNW * _p3ca) + (teN * _p3cb) + (teNE * _p3cc) +
                            ((nn - n) * _p3cd) + ((nw - w) * _p3ce)) >> 5);

            if (Avx2.IsSupported)
            {
                _predictions = Vector256.Create(p0, p1, p2, p3);
            }
            else
            {
                _pred0 = p0;
                _pred1 = p1;
                _pred2 = p2;
                _pred3 = p3;
            }

            uint weightSum = w0 + w1 + w2 + w3;
            int logShift   = (int)Bits.FloorLog2Nonzero(weightSum) - 4;
            w0           >>= logShift;
            w1           >>= logShift;
            w2           >>= logShift;
            w3           >>= logShift;
            weightSum      = w0 + w1 + w2 + w3;
            long sum       = (weightSum >> 1) - 1 + (p0 * w0) + (p1 * w1) + (p2 * w2) + (p3 * w3);
            long pred      = (sum * DivLookup[(int)(weightSum - 1)]) >> 24;
            Pred           = pred;

            // If all three have the same sign, skip clamping.
            if (((teN ^ teW) | (teN ^ teNW)) > 0)
            {
                return (pred + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits;
            }

            // Otherwise, clamp to min/max of neighbouring pixels (just W, NE, N).
            long mx = Math.Max(w, Math.Max(ne, n));
            long mn = Math.Min(w, Math.Min(ne, n));
            pred    = Math.Max(mn, Math.Min(mx, pred));
            Pred    = pred;

            return (pred + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits;
        }

        /// <summary>
        /// Updates the error buffers after decoding or encoding the true pixel value <paramref name="val"/> at <c>(x, y)</c>.
        /// </summary>
        /// <param name="val">True pixel value.</param>
        /// <param name="x">Column index.</param>
        /// <param name="y">Row index.</param>
        /// <param name="xsize">Channel width.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateErrors(long val, nuint x, nuint y, nuint xsize)
        {
            _             = xsize;
            int stride    = _stride;
            int curRow    = (y & 1u) != 0 ? 0 : stride;
            int prevRow   = (y & 1u) != 0 ? stride : 0;
            int curIdx    = curRow + (int)x;
            int prevNeIdx = prevRow + (int)x + 1;
            val           = AddBits(val);

            Error[curIdx] = (int)(Pred - val);

            uint err0, err1, err2, err3;
            if (Avx2.IsSupported)
            {
                // The four predictors are independent here; neighbouring pixels are not.
                Vector256<long> delta = Avx2.Subtract(_predictions, Vector256.Create(val));
                if (Vector256.EqualsAll(delta, Vector256<long>.Zero))
                {
                    // Exact predictions still overwrite the recycled row's errors.
                    // Adding zero to the NE errors can be omitted.
                    _predErrors0[curIdx] = 0;
                    _predErrors1[curIdx] = 0;
                    _predErrors2[curIdx] = 0;
                    _predErrors3[curIdx] = 0;
                    return;
                }

                Vector256<long> sign = Avx2.CompareGreaterThan(Vector256<long>.Zero, delta);
                Vector256<long> absolute = Avx2.Subtract(Avx2.Xor(delta, sign), sign);
                // Preserve Math.Abs(long.MinValue)'s exception, also for non-codec callers.
                if (Avx.MoveMask(absolute.AsDouble()) != 0)
                {
                    throw new OverflowException();
                }

                Vector256<ulong> errors = Avx2.ShiftRightLogical(
                    Avx2.Add(absolute, Vector256.Create(WeightedHeader.KPredictionRound)).AsUInt64(),
                    WeightedHeader.KPredExtraBits);
                // Only the low 32 bits survive the scalar cast, so a logical shift is exact.
                err0 = (uint)errors.GetElement(0);
                err1 = (uint)errors.GetElement(1);
                err2 = (uint)errors.GetElement(2);
                err3 = (uint)errors.GetElement(3);
            }
            else
            {
                err0 = (uint)((Math.Abs(_pred0 - val) + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits);
                err1 = (uint)((Math.Abs(_pred1 - val) + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits);
                err2 = (uint)((Math.Abs(_pred2 - val) + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits);
                err3 = (uint)((Math.Abs(_pred3 - val) + WeightedHeader.KPredictionRound) >> WeightedHeader.KPredExtraBits);
            }

            // For predicting in the next row.
            _predErrors0[curIdx] = err0;
            _predErrors1[curIdx] = err1;
            _predErrors2[curIdx] = err2;
            _predErrors3[curIdx] = err3;

            // Add the error on this pixel to the error on the NE pixel. This has the
            // effect of adding the error on this pixel to the E and EE pixels.
            _predErrors0[prevNeIdx] += err0;
            _predErrors1[prevNeIdx] += err1;
            _predErrors2[prevNeIdx] += err2;
            _predErrors3[prevNeIdx] += err3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ComputeWpProperty(long teW, long teN, long teNW, long teNE)
        {
            long p = teW;

            if (Math.Abs(teN) > Math.Abs(p))
            {
                p = teN;
            }

            if (Math.Abs(teNW) > Math.Abs(p))
            {
                p = teNW;
            }

            if (Math.Abs(teNE) > Math.Abs(p))
            {
                p = teNE;
            }

            return p;
        }
    }

    /// <summary>
    /// Flattened 2-level MA decision node used for fast tree traversal during Modular decoding.
    /// Ported from <c>jxl::FlatDecisionNode</c> in <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public struct FlatDecisionNode
    {
        /// <summary>Property index of the top node, or -1 if this node is a leaf.</summary>
        public int Property0;

        /// <summary>Split value of the top node (when <see cref="Property0"/> &gt;= 0).</summary>
        public int SplitVal0;

        /// <summary>Predictor of the leaf node (when <see cref="Property0"/> == -1).</summary>
        public Predictor Predictor;

        /// <summary>Split value of child 0.</summary>
        public int SplitValChild0;

        /// <summary>Split value of child 1.</summary>
        public int SplitValChild1;

        /// <summary>Leaf multiplier (when <see cref="Property0"/> == -1).</summary>
        public int Multiplier;

        /// <summary>Base child index (or context ID if leaf).</summary>
        public uint ChildID;

        /// <summary>Property index of child 0.</summary>
        public short PropertyChild0;

        /// <summary>Property index of child 1.</summary>
        public short PropertyChild1;

        /// <summary>Leaf predictor offset (when <see cref="Property0"/> == -1).</summary>
        public int PredictorOffset;
    }

    /// <summary>
    /// Fast lookup helper over a flattened MA tree (<see cref="FlatDecisionNode"/>).
    /// Ported from <c>jxl::MATreeLookup</c> in <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public sealed class MATreeLookup
    {
        private readonly FlatDecisionNode[] _nodes;

        /// <summary>
        /// Result of traversing the MA decision tree for a single pixel.
        /// </summary>
        public struct LookupResult
        {
            /// <summary>Entropy coding context ID.</summary>
            public uint Context;

            /// <summary>Selected predictor.</summary>
            public Predictor Predictor;

            /// <summary>Additive prediction offset.</summary>
            public int Offset;

            /// <summary>Residual multiplier.</summary>
            public int Multiplier;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MATreeLookup"/> class.
        /// </summary>
        /// <param name="tree">Flattened decision tree array.</param>
        public MATreeLookup(FlatDecisionNode[] tree)
        {
            _nodes = tree;
        }

        /// <summary>
        /// Traverses the flattened MA tree using <paramref name="properties"/> and returns the leaf parameters.
        /// </summary>
        /// <param name="properties">Current pixel properties.</param>
        /// <returns>Leaf <see cref="LookupResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LookupResult Lookup(ReadOnlySpan<int> properties)
        {
            uint pos = 0;

            while (true)
            {
                ref readonly FlatDecisionNode node = ref _nodes[pos];

                if (node.Property0 < 0)
                {
                    return new LookupResult
                    {
                        Context    = node.ChildID,
                        Predictor  = node.Predictor,
                        Offset     = node.PredictorOffset,
                        Multiplier = node.Multiplier,
                    };
                }

                bool p0   = properties[node.Property0] <= node.SplitVal0;
                uint off0 = properties[node.PropertyChild0] <= node.SplitValChild0 ? 1u : 0u;
                uint off1 = 2u | (properties[node.PropertyChild1] <= node.SplitValChild1 ? 1u : 0u);
                pos       = node.ChildID + (p0 ? off1 : off0);
            }
        }
    }

    /// <summary>
    /// Result of predicting a single Modular pixel.
    /// Ported from <c>jxl::PredictionResult</c> in <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public struct PredictionResult
    {
        /// <summary>Entropy coding context index.</summary>
        public int Context;

        /// <summary>Predicted pixel value (including offset).</summary>
        public long Guess;

        /// <summary>Predictor used.</summary>
        public Predictor Predictor;

        /// <summary>Residual multiplier.</summary>
        public int Multiplier;
    }

    /// <summary>
    /// Context property calculation and pixel prediction functions for Modular coding.
    /// Ported from <c>lib/jxl/modular/encoding/context_predict.h</c>.
    /// </summary>
    public static class ModularPredictors
    {
        /// <summary>Number of extra properties added per reference channel (4).</summary>
        public const int KExtraPropsPerChannel = 4;

        /// <summary>Number of non-reference properties (2 static + 13 spatial + 1 WP = 16).</summary>
        public const int KNumNonrefProperties =
            ModularOptions.KNumStaticProperties + 13 + WeightedHeader.KNumProperties;

        /// <summary>Property index of the weighted predictor error property (15).</summary>
        public const int KWPProp = KNumNonrefProperties - WeightedHeader.KNumProperties;

        /// <summary>Property index of the local gradient property (9).</summary>
        public const int KGradientProp = 9;

        /// <summary>
        /// Returns <c>true</c> if <paramref name="predictor"/> uses the weighted predictor state.
        /// </summary>
        /// <param name="predictor">Predictor enum value.</param>
        /// <returns><c>true</c> if weighted state is required.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PredictorHasWeighted(Predictor predictor)
        {
            return predictor == Predictor.Weighted ||
                   predictor == Predictor.Best ||
                   predictor == Predictor.Variable;
        }

        /// <summary>
        /// Clamps gradient <c>n + w - l</c> to <c>[min(n, w), max(n, w)]</c>.
        /// Ported from <c>jxl::ClampedGradient</c>.
        /// </summary>
        /// <param name="n">Top neighbor (N).</param>
        /// <param name="w">Left neighbor (W).</param>
        /// <param name="l">Top-left neighbor (NW).</param>
        /// <returns>Clamped gradient prediction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampedGradient(int n, int w, int l)
        {
            int m          = Math.Min(n, w);
            int maxVal     = Math.Max(n, w);
            int grad       = unchecked((int)((uint)n + (uint)w - (uint)l));
            int gradClampM = l < m ? maxVal : grad;

            return l > maxVal ? m : gradClampM;
        }

        /// <summary>
        /// Clamps gradient <c>n + w - l</c> to <c>[min(n, w), max(n, w)]</c> for 64-bit wide pixel types.
        /// </summary>
        /// <param name="n">Top neighbor (N).</param>
        /// <param name="w">Left neighbor (W).</param>
        /// <param name="l">Top-left neighbor (NW).</param>
        /// <returns>Clamped gradient prediction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampedGradient(long n, long w, long l)
        {
            return ClampedGradient((int)n, (int)w, (int)l);
        }

        /// <summary>
        /// Selects between <paramref name="a"/> and <paramref name="b"/> based on proximity to <c>a + b - c</c>.
        /// Ported from <c>jxl::Select</c>.
        /// </summary>
        /// <param name="a">Left neighbor.</param>
        /// <param name="b">Top neighbor.</param>
        /// <param name="c">Top-left neighbor.</param>
        /// <returns>Selected value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Select(long a, long b, long c)
        {
            long p  = a + b - c;
            long pa = Math.Abs(p - a);
            long pb = Math.Abs(p - b);

            return pa < pb ? a : b;
        }

        /// <summary>
        /// Evaluates a single <see cref="Predictor"/> given <paramref name="neighbors"/> and <paramref name="wpPred"/>.
        /// Ported from <c>jxl::detail::PredictOne</c>.
        /// </summary>
        /// <param name="p">Predictor mode.</param>
        /// <param name="neighbors">Neighboring pixel values.</param>
        /// <param name="wpPred">Weighted predictor output.</param>
        /// <returns>Predicted sample value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long PredictOne(Predictor p, in NeighborPixels neighbors, long wpPred)
        {
            switch (p)
            {
                case Predictor.Zero:
                    return 0;
                case Predictor.Left:
                    return neighbors.Left;
                case Predictor.Top:
                    return neighbors.Top;
                case Predictor.Select:
                    return Select(neighbors.Left, neighbors.Top, neighbors.TopLeft);
                case Predictor.Weighted:
                    return wpPred;
                case Predictor.Gradient:
                    return ClampedGradient(neighbors.Left, neighbors.Top, neighbors.TopLeft);
                case Predictor.TopLeft:
                    return neighbors.TopLeft;
                case Predictor.TopRight:
                    return neighbors.TopRight;
                case Predictor.LeftLeft:
                    return neighbors.LeftLeft;
                default:
                    return PredictAverage(p, in neighbors);
            }
        }

        /// <summary>
        /// Initializes static and row properties in <paramref name="p"/> at the start of row <paramref name="y"/>.
        /// Ported from <c>jxl::InitPropsRow</c>.
        /// </summary>
        /// <param name="p">Property span.</param>
        /// <param name="staticProps">Static properties (channel, group_id).</param>
        /// <param name="y">Row index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InitPropsRow(Span<int> p, ReadOnlySpan<int> staticProps, int y)
        {
            for (int i = 0; i < ModularOptions.KNumStaticProperties; i++)
            {
                p[i] = staticProps[i];
            }

            p[2] = y;
            p[9] = 0; // local gradient.
        }

        /// <summary>
        /// Precomputes reference properties from previously decoded channels of identical dimensions and shifts.
        /// Ported from <c>jxl::PrecomputeReferences</c>.
        /// </summary>
        /// <param name="ch">Current channel.</param>
        /// <param name="y">Row index.</param>
        /// <param name="image">Full modular image.</param>
        /// <param name="chanIndex">Current channel index.</param>
        /// <param name="references">Destination reference property channel (<c>w = num_extra_props</c>, <c>h = ch.W</c>).</param>
        public static void PrecomputeReferences(
            Channel ch,
            nuint y,
            ModularImage image,
            uint chanIndex,
            Channel references)
        {
            if (references.W == 0)
            {
                return;
            }

            ImageOps.ZeroFillImage(references.Plane);

            nuint offset        = 0;
            nuint numExtraProps = references.W;

            for (int j = (int)chanIndex - 1; j >= 0 && offset < numExtraProps; j--)
            {
                Channel refCh = image.Channels[j];

                if (refCh.W != ch.W || refCh.H != ch.H || refCh.HShift != ch.HShift || refCh.VShift != ch.VShift)
                {
                    continue;
                }

                PopulateChannelReferences(ch.W, y, refCh, references, (int)offset);
                offset += KExtraPropsPerChannel;
            }
        }

        /// <summary>
        /// Gathers the 7 spatial neighbor pixels for coordinate <c>(x, y)</c> in <paramref name="ch"/>.
        /// </summary>
        /// <param name="ch">Channel plane.</param>
        /// <param name="x">Column index.</param>
        /// <param name="y">Row index.</param>
        /// <param name="noEdgeCases">Whether interior bounds (<c>y &gt; 1</c>, <c>1 &lt; x &lt; w - 2</c>) are guaranteed.</param>
        /// <returns>Populated <see cref="NeighborPixels"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NeighborPixels GatherNeighbors(Channel ch, int x, int y, bool noEdgeCases)
        {
            ReadOnlySpan<int> rowCur = ch.ConstRow((nuint)y);

            if (noEdgeCases || (y > 1 && x > 1 && x + 2 < rowCur.Length))
            {
                ReadOnlySpan<int> rowTop    = ch.ConstRow((nuint)(y - 1));
                ReadOnlySpan<int> rowTopTop = ch.ConstRow((nuint)(y - 2));

                return new NeighborPixels
                {
                    Left          = rowCur[x - 1],
                    Top           = rowTop[x],
                    TopLeft       = rowTop[x - 1],
                    TopRight      = rowTop[x + 1],
                    LeftLeft      = rowCur[x - 2],
                    TopTop        = rowTopTop[x],
                    TopRightRight = rowTop[x + 2],
                };
            }

            return GatherNeighborsWithEdgeCases(ch, rowCur, x, y);
        }

        /// <summary>
        /// Gathers the 7 spatial neighbor pixels for coordinate <c>(x, y)</c> using pre-fetched row spans.
        /// </summary>
        /// <param name="rowCur">Current row span (<c>y</c>).</param>
        /// <param name="rowTop">Previous row span (<c>y - 1</c>, or <paramref name="rowCur"/> when <c>y == 0</c>).</param>
        /// <param name="rowTopTop">Two-rows-above span (<c>y - 2</c>, or <paramref name="rowTop"/> when <c>y &lt;= 1</c>).</param>
        /// <param name="x">Column index.</param>
        /// <param name="y">Row index.</param>
        /// <returns>Populated <see cref="NeighborPixels"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NeighborPixels GatherNeighbors(
            ReadOnlySpan<int> rowCur,
            ReadOnlySpan<int> rowTop,
            ReadOnlySpan<int> rowTopTop,
            int x,
            int y)
        {
            if (y > 1 && x > 1 && x + 2 < rowCur.Length)
            {
                return new NeighborPixels
                {
                    Left          = rowCur[x - 1],
                    Top           = rowTop[x],
                    TopLeft       = rowTop[x - 1],
                    TopRight      = rowTop[x + 1],
                    LeftLeft      = rowCur[x - 2],
                    TopTop        = rowTopTop[x],
                    TopRightRight = rowTop[x + 2],
                };
            }

            return GatherNeighborsFromRowsWithEdgeCases(rowCur, rowTop, rowTopTop, x, y);
        }

        /// <summary>
        /// Populates spatial properties 3..14 in <paramref name="p"/> from <paramref name="neighbors"/>.
        /// </summary>
        /// <param name="p">Properties buffer.</param>
        /// <param name="x">Column index.</param>
        /// <param name="neighbors">Neighboring pixel values.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeSpatialProperties(Span<int> p, int x, in NeighborPixels neighbors)
        {
            long top      = neighbors.Top;
            long left     = neighbors.Left;
            long topleft  = neighbors.TopLeft;
            long topright = neighbors.TopRight;
            long leftleft = neighbors.LeftLeft;
            long toptop   = neighbors.TopTop;

            // location
            p[3] = x;

            // neighbors
            p[4] = (int)(top > 0 ? top : -top);
            p[5] = (int)(left > 0 ? left : -left);
            p[6] = (int)top;
            p[7] = (int)left;

            // local gradient
            p[8] = (int)(left - p[9]);
            p[9] = (int)(left + top - topleft);

            // FFV1 context properties
            p[10] = (int)(left - topleft);
            p[11] = (int)(topleft - top);
            p[12] = (int)(top - topright);
            p[13] = (int)(top - toptop);
            p[14] = (int)(left - leftleft);
        }

        /// <summary>
        /// Predicts a pixel without an MA tree or weighted predictor.
        /// Ported from <c>jxl::PredictNoTreeNoWP</c>.
        /// </summary>
        /// <param name="ch">Channel being decoded.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="predictor">Fixed predictor to evaluate.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictNoTreeNoWP(Channel ch, int x, int y, Predictor predictor)
        {
            NeighborPixels neighbors = GatherNeighbors(ch, x, y, noEdgeCases: false);

            return new PredictionResult
            {
                Context    = 0,
                Guess      = PredictOne(predictor, in neighbors, 0),
                Predictor  = predictor,
                Multiplier = 1,
            };
        }

        /// <summary>
        /// Predicts a pixel without an MA tree, using the weighted predictor state.
        /// Ported from <c>jxl::PredictNoTreeWP</c>.
        /// </summary>
        /// <param name="ch">Channel being decoded.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="predictor">Predictor to evaluate.</param>
        /// <param name="wpState">Weighted predictor state.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictNoTreeWP(
            Channel ch,
            int x,
            int y,
            Predictor predictor,
            WeightedState wpState)
        {
            NeighborPixels neighbors = GatherNeighbors(ch, x, y, noEdgeCases: false);
            long wpPred              = wpState.Predict(
                (nuint)x,
                (nuint)y,
                ch.W,
                in neighbors,
                Span<int>.Empty,
                0,
                computeProperties: false);

            return new PredictionResult
            {
                Context    = 0,
                Guess      = PredictOne(predictor, in neighbors, wpPred),
                Predictor  = predictor,
                Multiplier = 1,
            };
        }

        /// <summary>
        /// Predicts a pixel using an MA tree lookup without the weighted predictor.
        /// Ported from <c>jxl::PredictTreeNoWP</c> and <c>jxl::PredictTreeNoWPNEC</c>.
        /// </summary>
        /// <param name="p">Properties buffer.</param>
        /// <param name="ch">Channel being decoded.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="treeLookup">Flattened MA tree lookup.</param>
        /// <param name="references">Precomputed reference channel properties.</param>
        /// <param name="noEdgeCases">Whether edge checks can be skipped.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictTreeNoWP(
            Span<int> p,
            Channel ch,
            int x,
            int y,
            MATreeLookup treeLookup,
            Channel references,
            bool noEdgeCases = false)
        {
            NeighborPixels neighbors = GatherNeighbors(ch, x, y, noEdgeCases);
            ComputeSpatialProperties(p, x, in neighbors);

            if (!noEdgeCases && references.W > 0)
            {
                ReadOnlySpan<int> rp = references.ConstRow((nuint)x);

                for (int i = 0; i < (int)references.W; i++)
                {
                    p[KNumNonrefProperties + i] = rp[i];
                }
            }

            MATreeLookup.LookupResult lr = treeLookup.Lookup(p);
            long pred                    = PredictOne(lr.Predictor, in neighbors, 0);

            return new PredictionResult
            {
                Context    = (int)lr.Context,
                Guess      = lr.Offset + pred,
                Predictor  = lr.Predictor,
                Multiplier = lr.Multiplier,
            };
        }

        /// <summary>
        /// Predicts a pixel using an MA tree lookup without the weighted predictor from pre-gathered <paramref name="neighbors"/>.
        /// </summary>
        /// <param name="p">Properties buffer.</param>
        /// <param name="neighbors">Pre-gathered spatial neighbors.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="treeLookup">Flattened MA tree lookup.</param>
        /// <param name="references">Precomputed reference channel properties.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictTreeNoWP(
            Span<int> p,
            in NeighborPixels neighbors,
            int x,
            MATreeLookup treeLookup,
            Channel references)
        {
            ComputeSpatialProperties(p, x, in neighbors);

            if (references.W > 0)
            {
                ReadOnlySpan<int> rp = references.ConstRow((nuint)x);

                for (int i = 0; i < (int)references.W; i++)
                {
                    p[KNumNonrefProperties + i] = rp[i];
                }
            }

            MATreeLookup.LookupResult lr = treeLookup.Lookup(p);
            long pred                    = PredictOne(lr.Predictor, in neighbors, 0);

            return new PredictionResult
            {
                Context    = (int)lr.Context,
                Guess      = lr.Offset + pred,
                Predictor  = lr.Predictor,
                Multiplier = lr.Multiplier,
            };
        }

        /// <summary>
        /// Predicts a pixel using both an MA tree lookup and the weighted predictor.
        /// Ported from <c>jxl::PredictTreeWP</c> and <c>jxl::PredictTreeWPNEC</c>.
        /// </summary>
        /// <param name="p">Properties buffer.</param>
        /// <param name="ch">Channel being decoded.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="treeLookup">Flattened MA tree lookup.</param>
        /// <param name="references">Precomputed reference channel properties.</param>
        /// <param name="wpState">Weighted predictor state.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictTreeWP(
            Span<int> p,
            Channel ch,
            int x,
            int y,
            MATreeLookup treeLookup,
            Channel references,
            WeightedState wpState)
        {
            NeighborPixels neighbors = GatherNeighbors(ch, x, y, noEdgeCases: false);

            return PredictTreeWP(p, in neighbors, x, y, treeLookup, references, wpState);
        }

        /// <summary>
        /// Predicts a pixel using both an MA tree lookup and the weighted predictor from pre-gathered <paramref name="neighbors"/>.
        /// </summary>
        /// <param name="p">Properties buffer.</param>
        /// <param name="neighbors">Pre-gathered spatial neighbors.</param>
        /// <param name="x">Column coordinate.</param>
        /// <param name="y">Row coordinate.</param>
        /// <param name="treeLookup">Flattened MA tree lookup.</param>
        /// <param name="references">Precomputed reference channel properties.</param>
        /// <param name="wpState">Weighted predictor state.</param>
        /// <returns><see cref="PredictionResult"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PredictionResult PredictTreeWP(
            Span<int> p,
            in NeighborPixels neighbors,
            int x,
            int y,
            MATreeLookup treeLookup,
            Channel references,
            WeightedState wpState)
        {
            ComputeSpatialProperties(p, x, in neighbors);

            long wpPred = wpState.Predict(
                (nuint)x,
                (nuint)y,
                wpState.XSize,
                in neighbors,
                p,
                KWPProp,
                computeProperties: true);

            if (references.W > 0)
            {
                ReadOnlySpan<int> rp = references.ConstRow((nuint)x);

                for (int i = 0; i < (int)references.W; i++)
                {
                    p[KNumNonrefProperties + i] = rp[i];
                }
            }

            MATreeLookup.LookupResult lr = treeLookup.Lookup(p);
            long pred                    = PredictOne(lr.Predictor, in neighbors, wpPred);

            return new PredictionResult
            {
                Context    = (int)lr.Context,
                Guess      = lr.Offset + pred,
                Predictor  = lr.Predictor,
                Multiplier = lr.Multiplier,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long PredictAverage(Predictor p, in NeighborPixels neighbors)
        {
            switch (p)
            {
                case Predictor.Average0:
                    return (neighbors.Left + neighbors.Top) / 2;
                case Predictor.Average1:
                    return (neighbors.Left + neighbors.TopLeft) / 2;
                case Predictor.Average2:
                    return (neighbors.TopLeft + neighbors.Top) / 2;
                case Predictor.Average3:
                    return (neighbors.Top + neighbors.TopRight) / 2;
                case Predictor.Average4:
                    return ((6 * neighbors.Top) - (2 * neighbors.TopTop) + (7 * neighbors.Left) +
                            neighbors.LeftLeft + neighbors.TopRightRight + (3 * neighbors.TopRight) + 8) / 16;
                default:
                    return 0;
            }
        }

        private static NeighborPixels GatherNeighborsWithEdgeCases(
            Channel ch,
            ReadOnlySpan<int> rowCur,
            int x,
            int y)
        {
            ReadOnlySpan<int> rowTop    = y > 0 ? ch.ConstRow((nuint)(y - 1)) : rowCur;
            ReadOnlySpan<int> rowTopTop = y > 1 ? ch.ConstRow((nuint)(y - 2)) : rowTop;

            return GatherNeighborsFromRowsWithEdgeCases(rowCur, rowTop, rowTopTop, x, y);
        }

        private static NeighborPixels GatherNeighborsFromRowsWithEdgeCases(
            ReadOnlySpan<int> rowCur,
            ReadOnlySpan<int> rowTop,
            ReadOnlySpan<int> rowTopTop,
            int x,
            int y)
        {
            int w              = rowCur.Length;
            long left          = x > 0 ? rowCur[x - 1] : (y > 0 ? rowTop[x] : 0);
            long top           = y > 0 ? rowTop[x] : left;
            long topleft       = x > 0 && y > 0 ? rowTop[x - 1] : left;
            long topright      = x + 1 < w && y > 0 ? rowTop[x + 1] : top;
            long leftleft      = x > 1 ? rowCur[x - 2] : left;
            long toptop        = y > 1 ? rowTopTop[x] : top;
            long toprightright = x + 2 < w && y > 0 ? rowTop[x + 2] : topright;

            return new NeighborPixels
            {
                Left          = left,
                Top           = top,
                TopLeft       = topleft,
                TopRight      = topright,
                LeftLeft      = leftleft,
                TopTop        = toptop,
                TopRightRight = toprightright,
            };
        }

        private static void PopulateChannelReferences(
            nuint w,
            nuint y,
            Channel refCh,
            Channel references,
            int offset)
        {
            ReadOnlySpan<int> rpp     = refCh.ConstRow(y);
            ReadOnlySpan<int> rpprev  = refCh.ConstRow(y > 0 ? y - 1u : 0u);

            for (nuint x = 0; x < w; x++)
            {
                Span<int> rp    = references.Row(x);
                long v          = rpp[(int)x];
                rp[offset]      = (int)Math.Abs(v);
                rp[offset + 1]  = (int)v;
                long vleft      = x > 0 ? rpp[(int)x - 1] : 0;
                long vtop       = y > 0 ? rpprev[(int)x] : vleft;
                long vtopleft   = x > 0 && y > 0 ? rpprev[(int)x - 1] : vleft;
                long vpredicted = ClampedGradient(vleft, vtop, vtopleft);
                rp[offset + 2]  = (int)Math.Abs(v - vpredicted);
                rp[offset + 3]  = (int)(v - vpredicted);
            }
        }
    }
}
