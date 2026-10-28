// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Range bounds <c>[min, max)</c> for each static property (<c>channel</c>, <c>group_id</c>).
    /// Ported from <c>jxl::StaticPropRange</c> in <c>lib/jxl/modular/options.h</c>.
    /// </summary>
    public struct StaticPropRange : IEquatable<StaticPropRange>, IComparable<StaticPropRange>
    {
        /// <summary>Minimum channel index (inclusive).</summary>
        public uint ChanMin;

        /// <summary>Maximum channel index (exclusive).</summary>
        public uint ChanMax;

        /// <summary>Minimum group/stream index (inclusive).</summary>
        public uint GroupMin;

        /// <summary>Maximum group/stream index (exclusive).</summary>
        public uint GroupMax;

        /// <summary>
        /// Gets or sets the <c>(Min, Max)</c> bounds for static property <paramref name="propIdx"/> (0 or 1).
        /// </summary>
        /// <param name="propIdx">Static property index (0 = channel, 1 = group_id).</param>
        /// <returns>Tuple of <c>(Min, Max)</c>.</returns>
        public (uint Min, uint Max) this[int propIdx]
        {
            readonly get => propIdx == 0 ? (ChanMin, ChanMax) : (GroupMin, GroupMax);
            set
            {
                if (propIdx == 0)
                {
                    ChanMin = value.Min;
                    ChanMax = value.Max;
                }
                else
                {
                    GroupMin = value.Min;
                    GroupMax = value.Max;
                }
            }
        }

        /// <summary>
        /// Gets or sets a specific bound (<paramref name="boundIdx"/> = 0 for min, 1 for max) of static property <paramref name="propIdx"/>.
        /// </summary>
        /// <param name="propIdx">Static property index (0 or 1).</param>
        /// <param name="boundIdx">Bound index (0 or 1).</param>
        /// <returns>Bound value.</returns>
        public uint this[int propIdx, int boundIdx]
        {
            readonly get => propIdx == 0
                ? (boundIdx == 0 ? ChanMin : ChanMax)
                : (boundIdx == 0 ? GroupMin : GroupMax);
            set
            {
                if (propIdx == 0)
                {
                    if (boundIdx == 0)
                    {
                        ChanMin = value;
                    }
                    else
                    {
                        ChanMax = value;
                    }
                }
                else
                {
                    if (boundIdx == 0)
                    {
                        GroupMin = value;
                    }
                    else
                    {
                        GroupMax = value;
                    }
                }
            }
        }

        /// <inheritdoc />
        public readonly bool Equals(StaticPropRange other)
        {
            return ChanMin == other.ChanMin &&
                   ChanMax == other.ChanMax &&
                   GroupMin == other.GroupMin &&
                   GroupMax == other.GroupMax;
        }

        /// <inheritdoc />
        public override readonly bool Equals(object? obj) => obj is StaticPropRange other && Equals(other);

        /// <inheritdoc />
        public override readonly int GetHashCode() => HashCode.Combine(ChanMin, ChanMax, GroupMin, GroupMax);

        /// <inheritdoc />
        public readonly int CompareTo(StaticPropRange other)
        {
            int c0 = ChanMin.CompareTo(other.ChanMin);

            if (c0 != 0)
            {
                return c0;
            }

            int c1 = ChanMax.CompareTo(other.ChanMax);

            if (c1 != 0)
            {
                return c1;
            }

            int g0 = GroupMin.CompareTo(other.GroupMin);

            if (g0 != 0)
            {
                return g0;
            }

            return GroupMax.CompareTo(other.GroupMax);
        }

        /// <summary>Equality operator.</summary>
        public static bool operator ==(StaticPropRange left, StaticPropRange right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(StaticPropRange left, StaticPropRange right) => !left.Equals(right);
    }

    /// <summary>
    /// Associates a <see cref="StaticPropRange"/> with a residual quantization multiplier.
    /// Ported from <c>jxl::ModularMultiplierInfo</c> in <c>lib/jxl/modular/options.h</c>.
    /// </summary>
    public struct ModularMultiplierInfo
    {
        /// <summary>Static property range to which <see cref="Multiplier"/> applies.</summary>
        public StaticPropRange Range;

        /// <summary>Residual quantization multiplier.</summary>
        public uint Multiplier;
    }

    /// <summary>
    /// Compact 16-bit representation of a tokenized residual (<c>tok</c> and <c>nbits</c>) in <see cref="TreeSamples"/>.
    /// Ported from <c>jxl::ResidualToken</c> in <c>lib/jxl/modular/encoding/enc_ma.h</c>.
    /// </summary>
    public struct ResidualToken
    {
        /// <summary>ANS token symbol index.</summary>
        public byte Tok;

        /// <summary>Number of extra bits required to represent the residual.</summary>
        public byte NBits;
    }

    /// <summary>
    /// Quantized pixel and property samples collected for Meta-Adaptive (MA) tree learning.
    /// Ported from <c>jxl::TreeSamples</c> in <c>lib/jxl/modular/encoding/enc_ma.h</c> and <c>enc_ma.cc</c>.
    /// </summary>
    public sealed class TreeSamples
    {
        private const nuint KDedupEntryShift = 5;
        private const int KMaxQuantizedProp  = 255;

        // Pre-tokenized residuals and extra bits count, for each predictor and sample.
        private readonly List<List<ResidualToken>> _residuals = new List<List<ResidualToken>>();

        // Quantized property values, for each property and sample.
        // The first two properties (static properties: channel and group_id) are quantized with a
        // simple subtract-and-clamp; other properties are quantized by lookup in the
        // corresponding quantization table in props_to_quant.
        private readonly List<List<byte>> _props = new List<List<byte>>();

        private readonly List<Predictor> _predictors    = new List<Predictor>();
        private readonly List<uint> _propsToUse         = new List<uint>();
        private readonly List<uint> _sampleCounts       = new List<uint>();
        private readonly List<List<int>> _compactProps  = new List<List<int>>();

        // Property quantization tables. For a non-static property p, at index i in
        // props_to_use, if the property value is x, its quantized value will be the
        // index of the first element in props_to_quant[i] that is >= x (which is
        // guaranteed to fit in an uint8_t).
        private readonly List<List<int>> _propsToQuant = new List<List<int>>();
        private readonly uint[] _staticPropBase        = new uint[ModularOptions.KNumStaticProperties];
        private uint _numSamples;

        // Data structure to deduplicate samples and weight counts instead.
        private nuint _dedupTableSize;
        private nuint _dedupMask;
        private readonly List<uint> _dedupTable = new List<uint>();

        /// <summary>
        /// Gets the number of unique samples stored.
        /// </summary>
        public nuint NumUniqueSamples => (nuint)_sampleCounts.Count;

        /// <summary>
        /// Gets the total number of samples added (including duplicates).
        /// </summary>
        public nuint NumSamples => _numSamples;

        /// <summary>
        /// Gets a value indicating whether at least one sample has been collected.
        /// </summary>
        public bool HasSamples => _sampleCounts.Count > 0;

        /// <summary>
        /// Gets the number of candidate predictors configured.
        /// </summary>
        public nuint NumPredictors => (nuint)_predictors.Count;

        /// <summary>
        /// Gets the number of candidate splitting properties configured.
        /// </summary>
        public nuint NumProperties => (nuint)_propsToUse.Count;

        /// <summary>
        /// Configures the candidate predictors to evaluate based on <paramref name="predictor"/> and <paramref name="wpTreeMode"/>.
        /// </summary>
        /// <param name="predictor">Predictor mode from <see cref="ModularOptions"/>.</param>
        /// <param name="wpTreeMode">Tree mode constraint.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetPredictor(Predictor predictor, ModularOptions.TreeMode wpTreeMode)
        {
            if (wpTreeMode == ModularOptions.TreeMode.GradientOnly)
            {
                _predictors.Add(Predictor.Gradient);

                return true;
            }

            if (wpTreeMode == ModularOptions.TreeMode.WPOnly)
            {
                _predictors.Add(Predictor.Weighted);

                return true;
            }

            if (predictor == Predictor.Undefined)
            {
                predictor = Predictor.Best;
            }

            if (predictor == Predictor.Best)
            {
                _predictors.Add(Predictor.Gradient);

                if (wpTreeMode != ModularOptions.TreeMode.NoWP)
                {
                    _predictors.Add(Predictor.Weighted);
                }

                return true;
            }

            if (predictor == Predictor.Variable)
            {
                for (int i = 0; i < ModularOptions.KNumModularPredictors; i++)
                {
                    if (wpTreeMode == ModularOptions.TreeMode.NoWP && (Predictor)i == Predictor.Weighted)
                    {
                        continue;
                    }

                    _predictors.Add((Predictor)i);
                }

                return true;
            }

            if (wpTreeMode == ModularOptions.TreeMode.NoWP && predictor == Predictor.Weighted)
            {
                return Status.Failure("Invalid predictor settings");
            }

            _predictors.Add(predictor);

            return true;
        }

        /// <summary>
        /// Configures the candidate splitting properties to evaluate.
        /// </summary>
        /// <param name="properties">List of property indices.</param>
        /// <param name="wpTreeMode">Tree mode constraint.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetProperties(IReadOnlyList<uint> properties, ModularOptions.TreeMode wpTreeMode)
        {
            if (wpTreeMode == ModularOptions.TreeMode.WPOnly)
            {
                _propsToUse.Clear();
                _propsToUse.Add((uint)ModularPredictors.KWPProp);

                return true;
            }

            if (wpTreeMode == ModularOptions.TreeMode.GradientOnly)
            {
                _propsToUse.Clear();
                _propsToUse.Add((uint)ModularPredictors.KGradientProp);

                return true;
            }

            _propsToUse.Clear();

            for (int i = 0; i < properties.Count; i++)
            {
                uint p = properties[i];

                if (wpTreeMode == ModularOptions.TreeMode.NoWP && p == (uint)ModularPredictors.KWPProp)
                {
                    continue;
                }

                _propsToUse.Add(p);
            }

            return !_propsToUse.Count.Equals(0);
        }

        /// <summary>
        /// Returns the <see cref="Predictor"/> at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">Predictor index.</param>
        /// <returns><see cref="Predictor"/> value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Predictor PredictorFromIndex(nuint index) => _predictors[(int)index];

        /// <summary>
        /// Returns the property ID at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">Property index.</param>
        /// <returns>Property ID.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PropertyFromIndex(nuint index) => (int)_propsToUse[(int)index];

        /// <summary>
        /// Unquantizes a quantized property threshold back to the actual integer split threshold.
        /// </summary>
        /// <param name="propertyIndex">Configured property index.</param>
        /// <param name="quant">Quantized threshold byte.</param>
        /// <returns>Unquantized split value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int UnquantizeProperty(nuint propertyIndex, byte quant)
        {
            return _compactProps[(int)propertyIndex][quant];
        }

        /// <summary>
        /// Returns the quantized property value for sample <paramref name="sample"/> and property <paramref name="propertyIndex"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte QuantizedProperty(nuint propertyIndex, uint sample)
        {
            return _props[(int)propertyIndex][(int)sample];
        }

        /// <summary>
        /// Returns the <see cref="ResidualToken"/> for <paramref name="predictor"/> and <paramref name="sample"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ResidualToken Residual(nuint predictor, uint sample)
        {
            return _residuals[(int)predictor][(int)sample];
        }

        /// <summary>
        /// Returns the deduplicated sample weight for <paramref name="sample"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Count(uint sample) => _sampleCounts[(int)sample];

        /// <summary>
        /// Returns the sorted list of unquantized property split values for <paramref name="propertyIndex"/>.
        /// </summary>
        public IReadOnlyList<int> PropertyMapping(nuint propertyIndex) => _compactProps[(int)propertyIndex];

        /// <summary>
        /// Swaps the sample at <paramref name="a"/> with the sample at <paramref name="b"/> across all internal arrays.
        /// </summary>
        /// <param name="a">First sample index.</param>
        /// <param name="b">Second sample index.</param>
        public void Swap(uint a, uint b)
        {
            if (a == b)
            {
                return;
            }

            int ia = (int)a;
            int ib = (int)b;

            for (int i = 0; i < _residuals.Count; i++)
            {
                List<ResidualToken> r = _residuals[i];
                (r[ia], r[ib])        = (r[ib], r[ia]);
            }

            for (int i = 0; i < _props.Count; i++)
            {
                List<byte> p   = _props[i];
                (p[ia], p[ib]) = (p[ib], p[ia]);
            }

            (_sampleCounts[ia], _sampleCounts[ib]) = (_sampleCounts[ib], _sampleCounts[ia]);
        }

        /// <summary>
        /// Initializes quantization tables for all candidate properties prior to sample collection.
        /// </summary>
        public void PreQuantizeProperties(
            StaticPropRange range,
            IReadOnlyList<ModularMultiplierInfo> multiplierInfo,
            IReadOnlyList<uint> groupPixelCount,
            IReadOnlyList<uint> channelPixelCount,
            List<int> pixelSamples,
            List<int> diffSamples,
            nuint maxPropertyValues)
        {
            maxPropertyValues = Math.Min((nuint)(KMaxQuantizedProp + 1), maxPropertyValues);
            _propsToQuant.Clear();

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                _propsToQuant.Add(new List<int>());
            }

            List<int> groupSplits   = BuildStaticSplits(range, 1, multiplierInfo, groupPixelCount, maxPropertyValues);
            List<int> channelSplits = BuildStaticSplits(range, 0, multiplierInfo, channelPixelCount, maxPropertyValues);

            pixelSamples.Sort();
            diffSamples.Sort();

            List<int> pixelSampleSplits = ChooseSplitPoints(pixelSamples, (int)maxPropertyValues, forceZero: false);
            List<int> diffSampleSplits  = ChooseSplitPoints(diffSamples, (int)maxPropertyValues, forceZero: true);

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                uint p = _propsToUse[i];

                if (p == 0)
                {
                    _propsToQuant[i] = new List<int>(channelSplits);
                }
                else if (p == 1)
                {
                    _propsToQuant[i] = new List<int>(groupSplits);
                }
                else if (p == 4 || p == 5 ||
                         (p >= ModularPredictors.KNumNonrefProperties &&
                          (p - ModularPredictors.KNumNonrefProperties) % ModularPredictors.KExtraPropsPerChannel < 2))
                {
                    _propsToQuant[i] = new List<int>(pixelSampleSplits);
                }
                else
                {
                    _propsToQuant[i] = new List<int>(diffSampleSplits);
                }
            }
        }

        /// <summary>
        /// Pre-allocates internal storage for collecting approximately <paramref name="numSamples"/> new samples.
        /// </summary>
        /// <param name="numSamples">Estimated sample count to add.</param>
        public void PrepareForSamples(nuint numSamples)
        {
            while (_residuals.Count < _predictors.Count)
            {
                _residuals.Add(new List<ResidualToken>());
            }

            for (int i = 0; i < _residuals.Count; i++)
            {
                _residuals[i].EnsureCapacity(_residuals[i].Count + (int)numSamples);
            }

            while (_props.Count < _propsToUse.Count)
            {
                _props.Add(new List<byte>());
            }

            for (int i = 0; i < _props.Count; i++)
            {
                _props[i].EnsureCapacity(_props[i].Count + (int)numSamples);
            }

            nuint totalSamples = numSamples + (nuint)_sampleCounts.Count;

            if (_dedupTableSize <= totalSamples)
            {
                int logSize = (int)Bits.CeilLog2Nonzero((ulong)totalSamples) + 1;
                InitTable(1u << logSize);
            }
        }

        /// <summary>
        /// Quantizes and adds a single pixel sample and its predictor residuals.
        /// </summary>
        /// <param name="pixel">True pixel value.</param>
        /// <param name="properties">Computed property values for the pixel.</param>
        /// <param name="predictions">Predicted values indexed by <see cref="Predictor"/>.</param>
        public void AddSample(int pixel, ReadOnlySpan<int> properties, ReadOnlySpan<long> predictions)
        {
            Span<ResidualToken> res = stackalloc ResidualToken[_predictors.Count];

            for (int i = 0; i < _predictors.Count; i++)
            {
                uint v = PackSigned.Pack((int)(pixel - predictions[(int)_predictors[i]]));
                EncMa.EncodeHybridUint000(v, out uint tok, out uint nbits);
                res[i] = new ResidualToken { Tok = (byte)tok, NBits = (byte)nbits };
            }

            Span<byte> quantProps = stackalloc byte[_propsToUse.Count];

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                quantProps[i] = QuantizePropertyValue(i, properties[(int)_propsToUse[i]]);
            }

            _numSamples++;

            if (IncrementIfPresent(res, quantProps))
            {
                return;
            }

            int pos = _sampleCounts.Count;
            _sampleCounts.Add(1);

            for (int i = 0; i < _predictors.Count; i++)
            {
                _residuals[i].Add(res[i]);
            }

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                _props[i].Add(quantProps[i]);
            }

            AddToTable((uint)pos);
        }

        /// <summary>
        /// Finalizes sample collection by compacting unused quantized property buckets.
        /// </summary>
        public void AllSamplesDone()
        {
            _compactProps.Clear();

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                _compactProps.Add(new List<int>());
                CompactPropertyChannel(i);
            }
        }

        /// <summary>
        /// Quantizes static property <paramref name="property"/> according to the learned quantization table.
        /// </summary>
        /// <param name="property">Static property index (0 or 1).</param>
        /// <param name="v">Property value.</param>
        /// <returns>Quantized property index.</returns>
        public uint QuantizeStaticProperty(uint property, uint v)
        {
            for (int i = 0; i < _propsToUse.Count; i++)
            {
                if (_propsToUse[i] == property)
                {
                    return QuantizeValue(_compactProps[i], (int)v);
                }
            }

            return 0;
        }

        private static List<int> BuildStaticSplits(
            StaticPropRange range,
            int propIdx,
            IReadOnlyList<ModularMultiplierInfo> multiplierInfo,
            IReadOnlyList<uint> pixelCounts,
            nuint maxPropertyValues)
        {
            List<int> ForcedSplits(StaticPropRange r)
            {
                List<int> splits = new List<int> { (int)r[propIdx, 1] };

                for (int i = 0; i < multiplierInfo.Count; i++)
                {
                    StaticPropRange mr = multiplierInfo[i].Range;

                    if (mr[propIdx, 0] > r[propIdx, 0] && mr[propIdx, 0] <= r[propIdx, 1])
                    {
                        splits.Add((int)mr[propIdx, 0]);
                    }

                    if (mr[propIdx, 1] > r[propIdx, 0] && mr[propIdx, 1] <= r[propIdx, 1])
                    {
                        splits.Add((int)mr[propIdx, 1]);
                    }
                }

                splits.Sort();
                int write = 0;

                for (int i = 0; i < splits.Count; i++)
                {
                    if (write == 0 || splits[i] != splits[write - 1])
                    {
                        splits[write++] = splits[i];
                    }
                }

                splits.RemoveRange(write, splits.Count - write);

                return splits;
            }

            List<int> forced = ForcedSplits(range);
            List<int> result = new List<int>();
            uint cur         = range[propIdx, 0];

            for (int i = 0; i < forced.Count; i++)
            {
                uint end = (uint)forced[i];

                if (end <= cur)
                {
                    continue;
                }

                ulong totalPixels = 0;

                for (uint j = cur; j < end && j < (uint)pixelCounts.Count; j++)
                {
                    totalPixels += pixelCounts[(int)j];
                }

                ulong sliceSize      = (totalPixels + maxPropertyValues - 1) / maxPropertyValues;
                ulong currentSlicePx = 0;

                for (uint j = cur; j < end; j++)
                {
                    uint px = j < (uint)pixelCounts.Count ? pixelCounts[(int)j] : 0u;

                    if (currentSlicePx + px >= sliceSize && currentSlicePx > 0)
                    {
                        result.Add((int)j - 1);
                        currentSlicePx = px;
                    }
                    else
                    {
                        currentSlicePx += px;
                    }
                }

                result.Add((int)end - 1);
                cur = end;
            }

            if (result.Count > 0)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (result.Count > KMaxQuantizedProp)
            {
                result.RemoveRange(KMaxQuantizedProp, result.Count - KMaxQuantizedProp);
            }

            return result;
        }

        private static List<int> ChooseSplitPoints(List<int> samples, int numSplitPoints, bool forceZero)
        {
            List<int> Discretize(ReadOnlySpan<int> pts, int count)
            {
                List<int> result = new List<int>(count);

                if (pts.Length == 0 || count <= 0)
                {
                    return result;
                }

                for (int i = 0; i < count; i++)
                {
                    int idx = (int)(((long)i * pts.Length) / count);
                    result.Add(pts[idx]);
                }

                result.Sort();
                int write = 0;

                for (int i = 0; i < result.Count; i++)
                {
                    if (write == 0 || result[i] != result[write - 1])
                    {
                        result[write++] = result[i];
                    }
                }

                result.RemoveRange(write, result.Count - write);

                return result;
            }

            if (!forceZero)
            {
                return Discretize(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples),
                    numSplitPoints - 1);
            }

            Span<int> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples);
            int negEnd     = 0;

            while (negEnd < span.Length && span[negEnd] < 0)
            {
                negEnd++;
            }

            int posBegin = negEnd;

            while (posBegin < span.Length && span[posBegin] <= 0)
            {
                posBegin++;
            }

            int negCount  = negEnd;
            int posCount  = span.Length - posBegin;
            int numPoints = Math.Max(numSplitPoints - 3, 0);
            int nonZero   = Math.Max(negCount + posCount, 1);
            int negSplits = (int)Math.Round(((double)negCount * numPoints) / nonZero);
            int posSplits = numPoints - negSplits;

            List<int> resultPts = Discretize(span.Slice(0, negCount), negSplits);

            if (negCount > 0)
            {
                resultPts.Add(-1);
            }

            if (posCount > 0)
            {
                resultPts.Add(0);
            }

            List<int> posPts = Discretize(span.Slice(posBegin, posCount), posSplits);
            resultPts.AddRange(posPts);

            int writeIdx = 0;

            for (int i = 0; i < resultPts.Count; i++)
            {
                if (writeIdx == 0 || resultPts[i] != resultPts[writeIdx - 1])
                {
                    resultPts[writeIdx++] = resultPts[i];
                }
            }

            resultPts.RemoveRange(writeIdx, resultPts.Count - writeIdx);

            return resultPts;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte QuantizeValue(List<int> splitPoints, int val)
        {
            int lo = 0;
            int hi = splitPoints.Count;

            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;

                if (splitPoints[mid] < val)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return (byte)lo;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte QuantizePropertyValue(int propIndex, int val)
        {
            return QuantizeValue(_propsToQuant[propIndex], val);
        }

        private void CompactPropertyChannel(int i)
        {
            List<bool> hasValue = new List<bool>(KMaxQuantizedProp + 1);

            for (int k = 0; k <= KMaxQuantizedProp; k++)
            {
                hasValue.Add(false);
            }

            List<byte> propList = _props[i];

            for (int s = 0; s < propList.Count; s++)
            {
                hasValue[propList[s]] = true;
            }

            int[] remap = new int[KMaxQuantizedProp + 1];
            int p       = 0;

            for (int j = 0; j < _propsToQuant[i].Count; j++)
            {
                remap[j] = p;

                if (hasValue[j])
                {
                    _compactProps[i].Add(_propsToQuant[i][j]);
                    p++;
                }
            }

            remap[_propsToQuant[i].Count] = p;

            if (p > 0 && !hasValue[_propsToQuant[i].Count])
            {
                _compactProps[i].RemoveAt(_compactProps[i].Count - 1);
            }

            for (int s = 0; s < propList.Count; s++)
            {
                propList[s] = (byte)Math.Min(remap[propList[s]], _compactProps[i].Count);
            }
        }

        private void InitTable(nuint tableSize)
        {
            _dedupTableSize = tableSize;
            _dedupMask      = tableSize - 1;
            _dedupTable.Clear();

            for (nuint i = 0; i < tableSize; i++)
            {
                _dedupTable.Add(uint.MaxValue);
            }

            for (int s = 0; s < _sampleCounts.Count; s++)
            {
                AddToTable((uint)s);
            }
        }

        private ulong Hash1(ReadOnlySpan<ResidualToken> residuals, ReadOnlySpan<byte> props)
        {
            const ulong kM = 0xc6a4a7935bd1e995UL;
            const int kR   = 47;
            ulong h        = ((ulong)_predictors.Count * 2UL) + (ulong)_propsToUse.Count;

            for (int i = 0; i < residuals.Length; i++)
            {
                ulong k = ((ulong)residuals[i].Tok << 8) | residuals[i].NBits;
                k      *= kM;
                k      ^= k >> kR;
                k      *= kM;
                h      ^= k;
                h      *= kM;
            }

            for (int i = 0; i < props.Length; i++)
            {
                ulong k = props[i];
                k      *= kM;
                k      ^= k >> kR;
                k      *= kM;
                h      ^= k;
                h      *= kM;
            }

            h ^= h >> kR;
            h *= kM;
            h ^= h >> kR;

            return h;
        }

        private void AddToTable(uint pos)
        {
            Span<ResidualToken> res = stackalloc ResidualToken[_predictors.Count];

            for (int i = 0; i < _predictors.Count; i++)
            {
                res[i] = _residuals[i][(int)pos];
            }

            Span<byte> quantProps = stackalloc byte[_propsToUse.Count];

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                quantProps[i] = _props[i][(int)pos];
            }

            nuint tablePos = (nuint)Hash1(res, quantProps) & _dedupMask;

            if (_dedupTable[(int)tablePos] == uint.MaxValue)
            {
                _dedupTable[(int)tablePos] = pos << (int)KDedupEntryShift;
            }
        }

        private bool IsSameSample(uint pos, ReadOnlySpan<ResidualToken> residuals, ReadOnlySpan<byte> props)
        {
            int ipos = (int)pos;

            for (int i = 0; i < _predictors.Count; i++)
            {
                ResidualToken rt = _residuals[i][ipos];

                if (rt.Tok != residuals[i].Tok || rt.NBits != residuals[i].NBits)
                {
                    return false;
                }
            }

            for (int i = 0; i < _propsToUse.Count; i++)
            {
                if (_props[i][ipos] != props[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool IncrementIfPresent(ReadOnlySpan<ResidualToken> residuals, ReadOnlySpan<byte> props)
        {
            nuint tablePos = (nuint)Hash1(residuals, props) & _dedupMask;
            uint entry     = _dedupTable[(int)tablePos];

            if (entry == uint.MaxValue)
            {
                return false;
            }

            uint pos = entry >> (int)KDedupEntryShift;

            if (IsSameSample(pos, residuals, props))
            {
                _sampleCounts[(int)pos]++;

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Meta-Adaptive (MA) decision tree learning, bit estimation, and tree tokenization.
    /// Ported from <c>lib/jxl/modular/encoding/enc_ma.h</c> and <c>enc_ma.cc</c>.
    /// </summary>
    public static class EncMa
    {
        private struct SplitInfo
        {
            public int Pos;
            public uint Begin;
            public uint End;
            public StaticPropRange AscendantProps;
        }

        private struct BestSplitCandidate
        {
            public nuint Prop;
            public int Val;
            public nuint Pred;
            public float Cost;
            public float LCost;
        }

        /// <summary>
        /// Encodes <paramref name="value"/> using HybridUint config <c>(4, 0, 0)</c>.
        /// </summary>
        /// <param name="value">Unsigned packed value.</param>
        /// <param name="token">Output token.</param>
        /// <param name="nbits">Output extra bit count.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EncodeHybridUint000(uint value, out uint token, out uint nbits)
        {
            if (value < 16u)
            {
                token = value;
                nbits = 0;
            }
            else
            {
                uint n = (uint)Bits.FloorLog2Nonzero(value);
                nbits  = n;
                token  = 15u + n;
            }
        }

        /// <summary>
        /// Estimates the bit cost of histogram <paramref name="counts"/> plus <paramref name="extraBits"/>.
        /// Ported from <c>EstimateBits</c> in <c>lib/jxl/modular/encoding/enc_ma.cc</c>.
        /// </summary>
        /// <param name="counts">Token counts.</param>
        /// <param name="extraBits">Extra bits per token.</param>
        /// <param name="totalSymbols">Sum of all counts.</param>
        /// <returns>Estimated bit cost.</returns>
        public static float EstimateBits(ReadOnlySpan<uint> counts, ReadOnlySpan<uint> extraBits, uint totalSymbols)
        {
            if (totalSymbols == 0)
            {
                return 0.0f;
            }

            float bits      = 0.0f;
            float invTotal  = 1.0f / totalSymbols;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0)
                {
                    bits += counts[i] * ((extraBits[i] - MathF.Log2(counts[i] * invTotal)));
                }
            }

            return bits;
        }

        /// <summary>
        /// Collects subsampled pixel and local gradient samples from <paramref name="image"/> for property pre-quantization.
        /// Ported from <c>jxl::CollectPixelSamples</c> in <c>lib/jxl/modular/encoding/enc_ma.cc</c>.
        /// </summary>
        public static void CollectPixelSamples(
            ModularImage image,
            ModularOptions options,
            uint groupId,
            List<uint> groupPixelCount,
            List<uint> channelPixelCount,
            List<int> pixelSamples,
            List<int> diffSamples)
        {
            if (options.NbRepeats == 0)
            {
                return;
            }

            while (groupPixelCount.Count <= (int)groupId)
            {
                groupPixelCount.Add(0);
            }

            if (channelPixelCount.Count < image.Channels.Count)
            {
                while (channelPixelCount.Count < image.Channels.Count)
                {
                    channelPixelCount.Add(0);
                }
            }

            ulong totalPixels = 0;

            for (int i = 0; i < image.Channels.Count; i++)
            {
                if ((nuint)i >= image.NbMetaChannels &&
                    (image.Channels[i].W > options.MaxChanSize ||
                     image.Channels[i].H > options.MaxChanSize))
                {
                    break;
                }

                uint px                  = (uint)(image.Channels[i].W * image.Channels[i].H);
                groupPixelCount[(int)groupId] += px;
                channelPixelCount[i]          += px;
                totalPixels                   += px;
            }

            double pixelFraction = Math.Min(1.0, options.NbRepeats);
            ulong threshold      = (ulong)((uint.MaxValue) * pixelFraction);
            ulong s0             = 0x94D049BB133111EBUL ^ ((ulong)groupId << 16);
            ulong s1             = 0xBF58476D1CE4E5B9UL;

            for (int i = 0; i < image.Channels.Count; i++)
            {
                Channel ch = image.Channels[i];

                if ((nuint)i >= image.NbMetaChannels &&
                    (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize))
                {
                    break;
                }

                if (ch.H == 0 || ch.W <= 2)
                {
                    continue;
                }

                CollectChannelPixelSamples(ch, threshold, ref s0, ref s1, pixelSamples, diffSamples);
            }
        }

        /// <summary>
        /// Tokenizes an MA <paramref name="tree"/> into <paramref name="tokens"/> and produces the canonical round-tripped
        /// <paramref name="decoderTree"/> with leaf IDs assigned in breadth-first order.
        /// Ported from <c>jxl::TokenizeTree</c> in <c>lib/jxl/modular/encoding/enc_ma.cc</c>.
        /// </summary>
        /// <param name="tree">Input MA decision tree.</param>
        /// <param name="tokens">Destination token list.</param>
        /// <param name="decoderTree">Output canonical tree matching decoder ordering.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status TokenizeTree(
            IReadOnlyList<PropertyDecisionNode> tree,
            List<Token> tokens,
            List<PropertyDecisionNode> decoderTree)
        {
            if (tree.Count > (int)ModularOptions.KMaxTreeSize)
            {
                return Status.Failure("Tree too large");
            }

            Queue<int> q = new Queue<int>();
            q.Enqueue(0);
            int leafId = 0;
            decoderTree.Clear();
            tokens.Clear();

            while (q.Count > 0)
            {
                int cur                   = q.Dequeue();
                PropertyDecisionNode node = tree[cur];

                if (node.Property == -1)
                {
                    tokens.Add(new Token((uint)MATreeContext.PropertyContext, 0));
                    tokens.Add(new Token((uint)MATreeContext.PredictorContext, (uint)node.Predictor));
                    tokens.Add(new Token((uint)MATreeContext.OffsetContext, PackSigned.Pack((int)node.PredictorOffset)));

                    uint mulLog  = (uint)Bits.Num0BitsBelowLS1BitNonzero(node.Multiplier);
                    uint mulBits = (node.Multiplier >> (int)mulLog) - 1u;
                    tokens.Add(new Token((uint)MATreeContext.MultiplierLogContext, mulLog));
                    tokens.Add(new Token((uint)MATreeContext.MultiplierBitsContext, mulBits));

                    decoderTree.Add(new PropertyDecisionNode(
                        -1,
                        0,
                        leafId++,
                        0,
                        node.Predictor,
                        node.PredictorOffset,
                        node.Multiplier));
                }
                else
                {
                    int lchild = decoderTree.Count + q.Count + 1;
                    int rchild = lchild + 1;
                    q.Enqueue((int)node.LChild);
                    q.Enqueue((int)node.RChild);

                    tokens.Add(new Token((uint)MATreeContext.PropertyContext, (uint)(node.Property + 1)));
                    tokens.Add(new Token((uint)MATreeContext.SplitValContext, PackSigned.Pack(node.SplitVal)));

                    decoderTree.Add(new PropertyDecisionNode(
                        node.Property,
                        node.SplitVal,
                        lchild,
                        rchild,
                        Predictor.Zero,
                        0,
                        1));
                }
            }

            return true;
        }

        /// <summary>
        /// Learns the optimal MA decision tree from <paramref name="treeSamples"/>.
        /// Ported from <c>jxl::ComputeBestTree</c> in <c>lib/jxl/modular/encoding/enc_ma.cc</c>.
        /// </summary>
        public static Status ComputeBestTree(
            TreeSamples treeSamples,
            float threshold,
            IReadOnlyList<ModularMultiplierInfo> mulInfo,
            StaticPropRange staticPropRange,
            float fastDecodeMultiplier,
            List<PropertyDecisionNode> tree)
        {
            List<ModularMultiplierInfo> localMulInfo = QuantizeMultipliers(treeSamples, mulInfo, staticPropRange);

            for (int i = 0; i < ModularOptions.KNumStaticProperties; i++)
            {
                staticPropRange[i, 0] = treeSamples.QuantizeStaticProperty((uint)i, staticPropRange[i, 0]);
                staticPropRange[i, 1] = treeSamples.QuantizeStaticProperty((uint)i, staticPropRange[i, 1] - 1) + 1;
            }

            Queue<SplitInfo> toSplit = new Queue<SplitInfo>();
            toSplit.Enqueue(new SplitInfo
            {
                Pos            = 0,
                Begin          = 0,
                End            = (uint)treeSamples.NumUniqueSamples,
                AscendantProps = staticPropRange,
            });

            tree.Clear();
            tree.Add(PropertyDecisionNode.Leaf(treeSamples.PredictorFromIndex(0)));

            while (toSplit.Count > 0)
            {
                SplitInfo cur = toSplit.Dequeue();
                SplitNode(
                    treeSamples,
                    threshold,
                    localMulInfo,
                    fastDecodeMultiplier,
                    cur,
                    toSplit,
                    tree);
            }

            UnquantizeTreeNodes(treeSamples, tree);

            return true;
        }

        private static void CollectChannelPixelSamples(
            Channel ch,
            ulong threshold,
            ref ulong s0,
            ref ulong s1,
            List<int> pixelSamples,
            List<int> diffSamples)
        {
            for (nuint y = 1; y < ch.H; y++)
            {
                ReadOnlySpan<int> r     = ch.ConstRow(y);
                ReadOnlySpan<int> rPrev = ch.ConstRow(y - 1);

                for (int x = 1; x < (int)ch.W - 1; x++)
                {
                    ulong a    = s0;
                    ulong b    = s1;
                    ulong bits = unchecked(a + b);
                    s0         = b;
                    a         ^= a << 23;
                    a         ^= b ^ (a >> 18) ^ (b >> 5);
                    s1         = a;

                    if ((bits >> 32) <= threshold)
                    {
                        pixelSamples.Add(Math.Abs(r[x]));
                        diffSamples.Add(r[x] - rPrev[x]);
                        diffSamples.Add(r[x] - r[x - 1]);
                    }
                }
            }
        }

        private static List<ModularMultiplierInfo> QuantizeMultipliers(
            TreeSamples treeSamples,
            IReadOnlyList<ModularMultiplierInfo> mulInfo,
            StaticPropRange staticPropRange)
        {
            List<ModularMultiplierInfo> result = new List<ModularMultiplierInfo>();

            for (int j = 0; j < mulInfo.Count; j++)
            {
                ModularMultiplierInfo info = mulInfo[j];
                bool intersects            = true;

                for (int i = 0; i < ModularOptions.KNumStaticProperties; i++)
                {
                    if (info.Range[i, 1] <= staticPropRange[i, 0] ||
                        info.Range[i, 0] >= staticPropRange[i, 1])
                    {
                        intersects = false;
                        break;
                    }

                    uint c0          = Math.Max(info.Range[i, 0], staticPropRange[i, 0]);
                    uint c1          = Math.Min(info.Range[i, 1], staticPropRange[i, 1]);
                    info.Range[i, 0] = treeSamples.QuantizeStaticProperty((uint)i, c0);
                    info.Range[i, 1] = treeSamples.QuantizeStaticProperty((uint)i, c1 - 1) + 1;
                }

                if (intersects && info.Multiplier != 1)
                {
                    result.Add(info);
                }
            }

            return result;
        }

        private static void SplitNode(
            TreeSamples treeSamples,
            float threshold,
            List<ModularMultiplierInfo> mulInfo,
            float fastDecodeMultiplier,
            SplitInfo cur,
            Queue<SplitInfo> toSplit,
            List<PropertyDecisionNode> tree)
        {
            if (cur.Begin >= cur.End)
            {
                return;
            }

            boolForcedSplit(cur.AscendantProps, mulInfo, out int forcedProp, out uint forcedVal, out uint multiplier);

            nuint bestPred;
            float baseBits = FindBestPredictor(treeSamples, cur.Begin, cur.End, out bestPred);

            PropertyDecisionNode curNode = tree[cur.Pos];
            curNode.Predictor            = treeSamples.PredictorFromIndex(bestPred);
            curNode.Multiplier           = multiplier;
            tree[cur.Pos]                = curNode;

            BestSplitCandidate best = new BestSplitCandidate
            {
                Prop  = nuint.MaxValue,
                Val   = -1,
                Pred  = bestPred,
                Cost  = forcedProp != -1 ? float.MaxValue : baseBits - threshold,
                LCost = 0,
            };

            EvaluateAllProperties(treeSamples, cur, forcedProp, forcedVal, threshold, fastDecodeMultiplier, ref best);

            if (best.Prop == nuint.MaxValue)
            {
                return;
            }

            ApplyBestSplit(treeSamples, cur, best, toSplit, tree);
        }

        private static void boolForcedSplit(
            StaticPropRange ascendantProps,
            List<ModularMultiplierInfo> mulInfo,
            out int forcedProp,
            out uint forcedVal,
            out uint multiplier)
        {
            forcedProp = -1;
            forcedVal  = 0;
            multiplier = 1;

            for (int k = 0; k < mulInfo.Count; k++)
            {
                ModularMultiplierInfo info = mulInfo[k];
                bool intersect             = true;

                for (int p = 0; p < ModularOptions.KNumStaticProperties; p++)
                {
                    if (info.Range[p, 1] <= ascendantProps[p, 0] ||
                        info.Range[p, 0] >= ascendantProps[p, 1])
                    {
                        intersect = false;
                        break;
                    }
                }

                if (!intersect)
                {
                    continue;
                }

                for (int p = 0; p < ModularOptions.KNumStaticProperties; p++)
                {
                    if (info.Range[p, 1] < ascendantProps[p, 1])
                    {
                        forcedProp = p;
                        forcedVal  = info.Range[p, 1] - 1;
                        break;
                    }

                    if (info.Range[p, 0] > ascendantProps[p, 0])
                    {
                        forcedProp = p;
                        forcedVal  = info.Range[p, 0] - 1;
                        break;
                    }
                }

                if (forcedProp == -1)
                {
                    multiplier = info.Multiplier;
                }

                break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float FindBestPredictor(
            TreeSamples treeSamples,
            uint begin,
            uint end,
            out nuint bestPred)
        {
            const int kMaxTok       = 64;
            Span<uint> counts       = stackalloc uint[kMaxTok];
            Span<uint> extraBits    = stackalloc uint[kMaxTok];
            bestPred                = 0;
            float bestCost          = float.MaxValue;

            for (nuint pred = 0; pred < treeSamples.NumPredictors; pred++)
            {
                counts.Clear();
                extraBits.Clear();
                uint tot = 0;

                for (uint s = begin; s < end; s++)
                {
                    ResidualToken rt = treeSamples.Residual(pred, s);
                    uint cnt         = treeSamples.Count(s);
                    int tok          = Math.Min((int)rt.Tok, kMaxTok - 1);
                    counts[tok]     += cnt;
                    extraBits[tok]   = rt.NBits;
                    tot             += cnt;
                }

                float cost = EstimateBits(counts, extraBits, tot);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPred = pred;
                }
            }

            return bestCost;
        }

        private static void EvaluateAllProperties(
            TreeSamples treeSamples,
            SplitInfo cur,
            int forcedProp,
            uint forcedVal,
            float threshold,
            float fastDecodeMultiplier,
            ref BestSplitCandidate best)
        {
            for (nuint pIdx = 0; pIdx < treeSamples.NumProperties; pIdx++)
            {
                int propId = treeSamples.PropertyFromIndex(pIdx);

                if (forcedProp != -1 && propId != forcedProp)
                {
                    continue;
                }

                int maxQuant = treeSamples.PropertyMapping(pIdx).Count;

                if (maxQuant == 0)
                {
                    continue;
                }

                EvaluatePropertySplits(
                    treeSamples,
                    cur,
                    pIdx,
                    propId,
                    forcedProp != -1 ? (int)forcedVal : -1,
                    threshold,
                    fastDecodeMultiplier,
                    ref best);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void EvaluatePropertySplits(
            TreeSamples treeSamples,
            SplitInfo cur,
            nuint pIdx,
            int propId,
            int forcedVal,
            float threshold,
            float fastDecodeMultiplier,
            ref BestSplitCandidate best)
        {
            int numBuckets        = treeSamples.PropertyMapping(pIdx).Count + 1;
            const int kMaxTok     = 64;
            uint[] bucketCounts   = new uint[numBuckets * kMaxTok];
            uint[] bucketTotals   = new uint[numBuckets];
            uint[] extraBitsTable = new uint[kMaxTok];

            for (uint s = cur.Begin; s < cur.End; s++)
            {
                byte q           = treeSamples.QuantizedProperty(pIdx, s);
                ResidualToken rt = treeSamples.Residual(best.Pred, s);
                uint cnt         = treeSamples.Count(s);
                int tok          = Math.Min((int)rt.Tok, kMaxTok - 1);

                if (q < numBuckets)
                {
                    bucketCounts[(q * kMaxTok) + tok] += cnt;
                    bucketTotals[q]                   += cnt;
                    extraBitsTable[tok]                = rt.NBits;
                }
            }

            Span<uint> leftCounts  = stackalloc uint[kMaxTok];
            Span<uint> rightCounts = stackalloc uint[kMaxTok];
            uint leftTotal         = 0;
            uint rightTotal        = 0;

            for (int b = 0; b < numBuckets; b++)
            {
                leftTotal += bucketTotals[b];

                for (int t = 0; t < kMaxTok; t++)
                {
                    leftCounts[t] += bucketCounts[(b * kMaxTok) + t];
                }
            }

            float mul = propId >= ModularOptions.KNumStaticProperties ? fastDecodeMultiplier : 1.0f;

            for (int v = 0; v < numBuckets - 1; v++)
            {
                uint bTot   = bucketTotals[v];
                rightTotal += bTot;
                leftTotal  -= bTot;

                for (int t = 0; t < kMaxTok; t++)
                {
                    uint c          = bucketCounts[(v * kMaxTok) + t];
                    rightCounts[t] += c;
                    leftCounts[t]  -= c;
                }

                if (forcedVal != -1 && v != forcedVal)
                {
                    continue;
                }

                if (leftTotal == 0 || rightTotal == 0)
                {
                    continue;
                }

                float lCost = EstimateBits(leftCounts, extraBitsTable, leftTotal);
                float rCost = EstimateBits(rightCounts, extraBitsTable, rightTotal);
                float cost  = (lCost + rCost + threshold) * mul;

                if (cost < best.Cost)
                {
                    best.Cost  = cost;
                    best.Prop  = pIdx;
                    best.Val   = v;
                    best.LCost = lCost;
                }
            }
        }

        private static void ApplyBestSplit(
            TreeSamples treeSamples,
            SplitInfo cur,
            BestSplitCandidate best,
            Queue<SplitInfo> toSplit,
            List<PropertyDecisionNode> tree)
        {
            // Partition samples in [cur.Begin, cur.End): samples with property > best.Val go left (first half).
            uint splitPos = cur.Begin;

            for (uint s = cur.Begin; s < cur.End; s++)
            {
                if (treeSamples.QuantizedProperty(best.Prop, s) > best.Val)
                {
                    treeSamples.Swap(splitPos, s);
                    splitPos++;
                }
            }

            int propId    = treeSamples.PropertyFromIndex(best.Prop);
            int lchildIdx = tree.Count;
            int rchildIdx = lchildIdx + 1;

            PropertyDecisionNode curNode = tree[cur.Pos];
            curNode.Property             = (short)best.Prop;
            curNode.SplitVal             = best.Val;
            curNode.LChild               = (uint)lchildIdx;
            curNode.RChild               = (uint)rchildIdx;
            tree[cur.Pos]                = curNode;

            Predictor leafPred = treeSamples.PredictorFromIndex(best.Pred);
            tree.Add(PropertyDecisionNode.Leaf(leafPred));
            tree.Add(PropertyDecisionNode.Leaf(leafPred));

            StaticPropRange leftProps  = cur.AscendantProps;
            StaticPropRange rightProps = cur.AscendantProps;

            if (propId < ModularOptions.KNumStaticProperties)
            {
                leftProps[propId, 0]  = (uint)(best.Val + 1);
                rightProps[propId, 1] = (uint)(best.Val + 1);
            }

            toSplit.Enqueue(new SplitInfo
            {
                Pos            = lchildIdx,
                Begin          = cur.Begin,
                End            = splitPos,
                AscendantProps = leftProps,
            });

            toSplit.Enqueue(new SplitInfo
            {
                Pos            = rchildIdx,
                Begin          = splitPos,
                End            = cur.End,
                AscendantProps = rightProps,
            });
        }

        private static void UnquantizeTreeNodes(TreeSamples treeSamples, List<PropertyDecisionNode> tree)
        {
            for (int i = 0; i < tree.Count; i++)
            {
                PropertyDecisionNode node = tree[i];

                if (node.Property != -1)
                {
                    nuint propIdx = (nuint)node.Property;
                    node.Property = (short)treeSamples.PropertyFromIndex(propIdx);
                    node.SplitVal = treeSamples.UnquantizeProperty(propIdx, (byte)node.SplitVal);
                    tree[i]       = node;
                }
            }
        }
    }

    /// <summary>
    /// Diagnostic GraphViz (<c>.dot</c>) printer for Modular MA decision trees.
    /// Ported from <c>lib/jxl/modular/encoding/enc_debug_tree.h</c> and <c>enc_debug_tree.cc</c>.
    /// </summary>
    public static class EncDebugTree
    {
        private static readonly string[] PropertyNames =
        {
            "c", "g", "y", "x", "|N|", "|W|", "N", "W",
            "W-WW-NW+NWW", "W+N-NW", "W-NW", "NW-N", "N-NE", "N-NN", "W-WW", "WGH",
        };

        private static readonly string[] PredictorNames =
        {
            "Zero", "W", "N", "Avg(W,N)", "Select", "Gradient", "Weighted",
            "NE", "NW", "WW", "Avg(W,NW)", "Avg(N,NW)", "Avg(N,NE)", "Avg(6*N-2*NN+7*W+WW+NEE+3*NE+8)",
        };

        /// <summary>
        /// Formats <paramref name="tree"/> as a GraphViz DOT string.
        /// </summary>
        /// <param name="tree">MA decision tree.</param>
        /// <returns>DOT graph text.</returns>
        public static string FormatTreeDot(IReadOnlyList<PropertyDecisionNode> tree)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("digraph Tree {");

            for (int cur = 0; cur < tree.Count; cur++)
            {
                PropertyDecisionNode node = tree[cur];

                if (node.Property < 0)
                {
                    string predName = (int)node.Predictor < PredictorNames.Length
                        ? PredictorNames[(int)node.Predictor]
                        : node.Predictor.ToString();
                    sb.AppendLine(
                        CultureInfo.InvariantCulture,
                        $" n{cur} [label=\"{predName}{node.PredictorOffset:+#;-#;+0} (x{node.Multiplier})\"];");
                }
                else
                {
                    string propName = node.Property < PropertyNames.Length
                        ? PropertyNames[node.Property]
                        : $"Prev{(node.Property - 16) / 4}-{(node.Property - 16) % 4}";
                    sb.AppendLine(CultureInfo.InvariantCulture, $" n{cur} [label=\"{propName} > {node.SplitVal}\"];");
                    sb.AppendLine(CultureInfo.InvariantCulture, $" n{cur} -> n{node.LChild} [label=yes];");
                    sb.AppendLine(CultureInfo.InvariantCulture, $" n{cur} -> n{node.RChild} [label=no];");
                }
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Writes <paramref name="tree"/> as a GraphViz <c>.dot</c> file at <c><paramref name="path"/>.dot</c>.
        /// Ported from <c>jxl::PrintTree</c> in <c>lib/jxl/modular/encoding/enc_debug_tree.cc</c>.
        /// </summary>
        /// <param name="tree">MA decision tree.</param>
        /// <param name="path">Base file path (without <c>.dot</c> extension).</param>
        public static void PrintTree(IReadOnlyList<PropertyDecisionNode> tree, string path)
        {
            File.WriteAllText(path + ".dot", FormatTreeDot(tree));
        }
    }

    /// <summary>
    /// Forward Reversible Color Transform (RCT) for Modular images.
    /// Ported from <c>lib/jxl/modular/transform/enc_rct.h</c> and <c>enc_rct.cc</c>.
    /// </summary>
    public static class EncRct
    {
        /// <summary>
        /// Applies forward RCT <paramref name="rctType"/> from <paramref name="inChannels"/> into <paramref name="outChannels"/>.
        /// Ported from <c>jxl::FwdRct</c> in <c>lib/jxl/modular/transform/enc_rct.cc</c>.
        /// </summary>
        /// <param name="inChannels">Three input channels.</param>
        /// <param name="outChannels">Three output channels (may alias <paramref name="inChannels"/>).</param>
        /// <param name="rctType">RCT type in <c>0..41</c>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status FwdRct(
            IReadOnlyList<Channel> inChannels,
            IReadOnlyList<Channel> outChannels,
            nuint rctType,
            ThreadPool? pool = null)
        {
            if (rctType >= 42)
            {
                return Status.Failure("Invalid RCT type");
            }

            int permutation = (int)(rctType / 7);
            int custom      = (int)(rctType % 7);
            nuint w         = inChannels[0].W;
            nuint h         = inChannels[0].H;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)h,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    FwdRctRow(inChannels, outChannels, w, task, permutation, custom);

                    return true;
                },
                "FwdRct");
        }

        /// <summary>
        /// Applies an in-place forward RCT on three channels of <paramref name="input"/> starting at <paramref name="beginC"/>.
        /// </summary>
        public static Status FwdRct(ModularImage input, nuint beginC, nuint rctType, ThreadPool? pool = null)
        {
            Status chk = ModularTransformOps.CheckEqualChannels(input, (uint)beginC, (uint)(beginC + 2));

            if (!chk.IsOk)
            {
                return chk;
            }

            if (rctType == 0)
            {
                return false;
            }

            Channel[] chans =
            {
                input.Channels[(int)beginC],
                input.Channels[(int)beginC + 1],
                input.Channels[(int)beginC + 2],
            };

            return FwdRct(chans, chans, rctType, pool);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void FwdRctRow(
            IReadOnlyList<Channel> inChannels,
            IReadOnlyList<Channel> outChannels,
            nuint w,
            uint y,
            int permutation,
            int custom)
        {
            int idx0 = permutation % 3;
            int idx1 = (permutation + 1 + (permutation / 3)) % 3;
            int idx2 = (permutation + 2 - (permutation / 3)) % 3;

            ReadOnlySpan<int> r0In = inChannels[idx0].ConstRow(y);
            ReadOnlySpan<int> r1In = inChannels[idx1].ConstRow(y);
            ReadOnlySpan<int> r2In = inChannels[idx2].ConstRow(y);

            Span<int> r0Out = outChannels[0].Row(y);
            Span<int> r1Out = outChannels[1].Row(y);
            Span<int> r2Out = outChannels[2].Row(y);

            for (int x = 0; x < (int)w; x++)
            {
                int r = r0In[x];
                int g = r1In[x];
                int b = r2In[x];

                ApplyPixelFwdRct(custom, r, g, b, out r0Out[x], out r1Out[x], out r2Out[x]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyPixelFwdRct(
            int custom,
            int r,
            int g,
            int b,
            out int o0,
            out int o1,
            out int o2)
        {
            switch (custom)
            {
                case 0:
                    o0 = r;
                    o1 = g;
                    o2 = b;
                    break;
                case 1:
                    o0 = r;
                    o1 = g;
                    o2 = b - r;
                    break;
                case 2:
                    o0 = r;
                    o1 = g - r;
                    o2 = b;
                    break;
                case 3:
                    o0 = r;
                    o1 = g - r;
                    o2 = b - r;
                    break;
                case 4:
                    o1 = g - ((r + b) >> 1);
                    o0 = r;
                    o2 = b;
                    break;
                case 5:
                    o0 = r;
                    o1 = g - ((r + b) >> 1);
                    o2 = b - r;
                    break;
                default: // case 6 (YCoCg)
                    int t = r - b;
                    int y = b + (t >> 1);
                    int c = g - y;
                    o0    = y + (c >> 1);
                    o1    = t;
                    o2    = c;
                    break;
            }
        }
    }

    /// <summary>
    /// Forward Haar-like Squeeze transform for Modular images.
    /// Ported from <c>lib/jxl/modular/transform/enc_squeeze.h</c> and <c>enc_squeeze.cc</c>.
    /// </summary>
    public static class EncSqueeze
    {
        /// <summary>
        /// Populates default squeeze parameters for <paramref name="image"/>.
        /// </summary>
        public static void DefaultSqueezeParameters(List<SqueezeParams> parameters, ModularImage image)
        {
            ModularTransformOps.DefaultSqueezeParameters(parameters, image);
        }

        /// <summary>
        /// Applies the forward Squeeze transform across <paramref name="parameters"/> on <paramref name="input"/>.
        /// Ported from <c>jxl::FwdSqueeze</c> in <c>lib/jxl/modular/transform/enc_squeeze.cc</c>.
        /// </summary>
        public static Status FwdSqueeze(
            ModularImage input,
            List<SqueezeParams> parameters,
            ThreadPool? pool = null)
        {
            if (parameters.Count == 0)
            {
                DefaultSqueezeParameters(parameters, input);
            }

            if (parameters.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                SqueezeParams sp = parameters[i];
                uint beginC      = sp.BeginC;
                uint endC        = sp.BeginC + sp.NumC - 1u;
                uint offset      = sp.InPlace ? endC + 1u : (uint)input.Channels.Count;

                Status chk = ModularTransformOps.CheckMetaSqueezeParams(sp, input.Channels.Count);

                if (!chk.IsOk)
                {
                    return chk;
                }

                Status stepOk = FwdSqueezeStep(input, sp.Horizontal, beginC, endC, offset, pool);

                if (!stepOk.IsOk)
                {
                    return stepOk;
                }
            }

            return true;
        }

        private static Status FwdSqueezeStep(
            ModularImage input,
            bool horizontal,
            uint beginC,
            uint endC,
            uint outOffset,
            ThreadPool? pool)
        {
            JxlMemoryManager? mm = input.MemoryManager;

            for (uint c = beginC; c <= endC; c++)
            {
                Channel chIn = input.Channels[(int)c];
                nuint w      = chIn.W;
                nuint h      = chIn.H;

                StatusOr<Channel> avgOr = horizontal
                    ? Channel.Create(mm, (w + 1u) / 2u, h, chIn.HShift + 1, chIn.VShift)
                    : Channel.Create(mm, w, (h + 1u) / 2u, chIn.HShift, chIn.VShift + 1);

                if (!avgOr.IsOk)
                {
                    return avgOr.Status;
                }

                StatusOr<Channel> resOr = horizontal
                    ? Channel.Create(mm, w - ((w + 1u) / 2u), h, chIn.HShift + 1, chIn.VShift)
                    : Channel.Create(mm, w, h - ((h + 1u) / 2u), chIn.HShift, chIn.VShift + 1);

                if (!resOr.IsOk)
                {
                    avgOr.Value.Dispose();

                    return resOr.Status;
                }

                Channel chAvg   = avgOr.Value;
                Channel chRes   = resOr.Value;
                chAvg.Component = chIn.Component;
                chRes.Component = chIn.Component;

                if (horizontal)
                {
                    RunSmoothHorizontalSqueeze(chIn, chAvg, chRes, pool);
                }
                else
                {
                    RunSmoothVerticalSqueeze(chIn, chAvg, chRes, pool);
                }

                chIn.Dispose();
                input.Channels[(int)c] = chAvg;
                input.Channels.Insert((int)(outOffset + (c - beginC)), chRes);
            }

            return true;
        }

        private static void RunSmoothHorizontalSqueeze(Channel chIn, Channel chAvg, Channel chRes, ThreadPool? pool)
        {
            if (chIn.W == 0 || chIn.H == 0)
            {
                return;
            }

            DataParallel.RunOnPool(
                pool,
                0,
                (uint)chIn.H,
                ThreadPool.NoInit,
                (y, _) =>
                {
                    SmoothHorizontalRow(chIn.ConstRow(y), chAvg.Row(y), chRes.Row(y), (int)chIn.W);

                    return true;
                },
                "FwdHorizontalSqueeze");
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void SmoothHorizontalRow(
            ReadOnlySpan<int> rowIn,
            Span<int> rowAvg,
            Span<int> rowRes,
            int w)
        {
            int wRes = w / 2;

            for (int x = 0; x < wRes; x++)
            {
                long a    = rowIn[2 * x];
                long b    = rowIn[(2 * x) + 1];
                long avg  = (a + b + (a > b ? 1L : 0L)) >> 1;
                rowAvg[x] = (int)avg;
            }

            if ((w & 1) != 0)
            {
                rowAvg[wRes] = rowIn[w - 1];
            }

            for (int x = 0; x < wRes; x++)
            {
                long a       = rowIn[2 * x];
                long b       = rowIn[(2 * x) + 1];
                long diff    = a - b;
                long avg     = rowAvg[x];
                long left    = x > 0 ? rowIn[(2 * x) - 1] : avg;
                long nextAvg = x + 1 < rowAvg.Length ? rowAvg[x + 1] : avg;
                long tend    = ModularTransformOps.SmoothTendency(left, avg, nextAvg);
                rowRes[x]    = (int)(diff - tend);
            }
        }

        private static void RunSmoothVerticalSqueeze(Channel chIn, Channel chAvg, Channel chRes, ThreadPool? pool)
        {
            if (chIn.W == 0 || chIn.H == 0)
            {
                return;
            }

            int h    = (int)chIn.H;
            int w    = (int)chIn.W;
            int hRes = h / 2;

            for (int y = 0; y < hRes; y++)
            {
                ReadOnlySpan<int> rA = chIn.ConstRow((nuint)(2 * y));
                ReadOnlySpan<int> rB = chIn.ConstRow((nuint)((2 * y) + 1));
                Span<int> rAvg       = chAvg.Row((nuint)y);

                for (int x = 0; x < w; x++)
                {
                    long a  = rA[x];
                    long b  = rB[x];
                    rAvg[x] = (int)((a + b + (a > b ? 1L : 0L)) >> 1);
                }
            }

            if ((h & 1) != 0)
            {
                chIn.ConstRow((nuint)(h - 1)).CopyTo(chAvg.Row((nuint)hRes));
            }

            DataParallel.RunOnPool(
                pool,
                0,
                (uint)hRes,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    SmoothVerticalRow(chIn, chAvg, chRes, (int)task, hRes, w);

                    return true;
                },
                "FwdVerticalSqueeze");
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void SmoothVerticalRow(
            Channel chIn,
            Channel chAvg,
            Channel chRes,
            int y,
            int hRes,
            int w)
        {
            _ = hRes;
            ReadOnlySpan<int> rA    = chIn.ConstRow((nuint)(2 * y));
            ReadOnlySpan<int> rB    = chIn.ConstRow((nuint)((2 * y) + 1));
            ReadOnlySpan<int> rAvg  = chAvg.ConstRow((nuint)y);
            ReadOnlySpan<int> rTop  = y > 0 ? chIn.ConstRow((nuint)((2 * y) - 1)) : rAvg;
            ReadOnlySpan<int> rNext = y + 1 < (int)chAvg.H ? chAvg.ConstRow((nuint)(y + 1)) : rAvg;
            Span<int> rRes          = chRes.Row((nuint)y);

            for (int x = 0; x < w; x++)
            {
                long a       = rA[x];
                long b       = rB[x];
                long diff    = a - b;
                long avg     = rAvg[x];
                long top     = rTop[x];
                long nextAvg = rNext[x];
                long tend    = ModularTransformOps.SmoothTendency(top, avg, nextAvg);
                rRes[x]      = (int)(diff - tend);
            }
        }
    }

    /// <summary>
    /// Options for <see cref="EncPalette.FwdPalette"/>.
    /// </summary>
    public readonly record struct PaletteTransformConfig(
        uint BeginC,
        uint EndC,
        bool Ordered,
        bool Lossy,
        WeightedHeader WpHeader);

    /// <summary>
    /// Forward Palette and Channel-Compaction transform for Modular images.
    /// Ported from <c>lib/jxl/modular/transform/enc_palette.h</c> and <c>enc_palette.cc</c>.
    /// </summary>
    public static class EncPalette
    {
        private const int KMaxPaletteLookupTableSize = 1 << 16;
        private const int KLargeCube                 = 5;
        private const int KSmallCube                 = 4;
        private const int KLargeCubeOffset           = KSmallCube * KSmallCube * KSmallCube;
        private const int KImplicitPaletteSize       = KLargeCubeOffset + (KLargeCube * KLargeCube * KLargeCube);

        private sealed class IntArrayComparer : IComparer<int[]>, IEqualityComparer<int[]>
        {
            public static readonly IntArrayComparer Instance = new IntArrayComparer();

            public int Compare(int[]? x, int[]? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                int len = Math.Min(x.Length, y.Length);

                for (int i = 0; i < len; i++)
                {
                    int cmp = x[i].CompareTo(y[i]);

                    if (cmp != 0)
                    {
                        return cmp;
                    }
                }

                return x.Length.CompareTo(y.Length);
            }

            public bool Equals(int[]? x, int[]? y) => Compare(x, y) == 0;

            public int GetHashCode(int[] obj)
            {
                HashCode hc = default;

                for (int i = 0; i < obj.Length; i++)
                {
                    hc.Add(obj[i]);
                }

                return hc.ToHashCode();
            }
        }

        /// <summary>
        /// Applies the forward Palette transform to channels <c>[config.BeginC, config.EndC]</c> of <paramref name="input"/>.
        /// Ported from <c>jxl::FwdPalette</c> in <c>lib/jxl/modular/transform/enc_palette.cc</c>.
        /// </summary>
        public static Status FwdPalette(
            ModularImage input,
            PaletteTransformConfig config,
            ref uint nbColors,
            ref uint nbDeltas,
            ref Predictor predictor)
        {
            Status chk = ModularTransformOps.CheckEqualChannels(input, config.BeginC, config.EndC);

            if (!chk.IsOk)
            {
                return chk;
            }

            if (config.BeginC < input.NbMetaChannels || input.BitDepth >= 32)
            {
                return false;
            }

            if (!config.Lossy && nbColors < 2)
            {
                return false;
            }

            uint nb = config.EndC - config.BeginC + 1u;

            if (!config.Lossy && nb == 1)
            {
                return FwdSingleChannelPalette(input, config.BeginC, ref nbColors, ref predictor);
            }

            return FwdMultiChannelPalette(input, config, nb, ref nbColors, ref nbDeltas, ref predictor);
        }

        private static Status FwdSingleChannelPalette(
            ModularImage input,
            uint beginC,
            ref uint nbColors,
            ref Predictor predictor)
        {
            if (nbColors == 0)
            {
                return false;
            }

            Channel ch = input.Channels[(int)beginC];
            EncTransform.ComputeMinMax(ch, out int minVal, out int maxVal);
            long lookupSize = (long)maxVal - minVal + 1L;

            if (lookupSize > KMaxPaletteLookupTableSize)
            {
                return FwdSingleChannelPaletteSet(input, ch, ref nbColors, ref predictor);
            }

            int[] lookup = new int[(int)lookupSize];
            int idx      = 0;

            for (nuint y = 0; y < ch.H; y++)
            {
                ReadOnlySpan<int> p = ch.ConstRow(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    int off = p[x] - minVal;

                    if (lookup[off] == 0)
                    {
                        lookup[off] = 1;
                        idx++;

                        if (idx > (int)nbColors)
                        {
                            return false;
                        }
                    }
                }
            }

            return BuildSingleChannelLookupPalette(input, ch, lookup, minVal, idx, ref nbColors, ref predictor);
        }

        private static Status BuildSingleChannelLookupPalette(
            ModularImage input,
            Channel ch,
            int[] lookup,
            int minVal,
            int count,
            ref uint nbColors,
            ref Predictor predictor)
        {
            StatusOr<Channel> pchOr = Channel.Create(input.MemoryManager, (nuint)count, 1, -1, -1);

            if (!pchOr.IsOk)
            {
                return pchOr.Status;
            }

            Channel pch     = pchOr.Value;
            nbColors        = (uint)count;
            int idx         = 0;
            Span<int> pal   = pch.Row(0);

            for (int i = 0; i < lookup.Length; i++)
            {
                if (lookup[i] != 0)
                {
                    pal[idx]  = i + minVal;
                    lookup[i] = idx;
                    idx++;
                }
            }

            for (nuint y = 0; y < ch.H; y++)
            {
                Span<int> p = ch.Row(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    p[x] = lookup[p[x] - minVal];
                }
            }

            predictor = Predictor.Zero;
            input.NbMetaChannels++;
            input.Channels.Insert(0, pch);

            return true;
        }

        private static Status FwdSingleChannelPaletteSet(
            ModularImage input,
            Channel ch,
            ref uint nbColors,
            ref Predictor predictor)
        {
            SortedSet<int> chPalette = new SortedSet<int>();

            for (nuint y = 0; y < ch.H; y++)
            {
                ReadOnlySpan<int> p = ch.ConstRow(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    if (chPalette.Add(p[x]) && chPalette.Count > (int)nbColors)
                    {
                        return false;
                    }
                }
            }

            int count               = chPalette.Count;
            StatusOr<Channel> pchOr = Channel.Create(input.MemoryManager, (nuint)count, 1, -1, -1);

            if (!pchOr.IsOk)
            {
                return pchOr.Status;
            }

            Channel pch   = pchOr.Value;
            nbColors      = (uint)count;
            Span<int> pal = pch.Row(0);
            int idx       = 0;

            foreach (int color in chPalette)
            {
                pal[idx++] = color;
            }

            for (nuint y = 0; y < ch.H; y++)
            {
                Span<int> p = ch.Row(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    p[x] = pal.IndexOf(p[x]);
                }
            }

            predictor = Predictor.Zero;
            input.NbMetaChannels++;
            input.Channels.Insert(0, pch);

            return true;
        }

        private static Status FwdMultiChannelPalette(
            ModularImage input,
            PaletteTransformConfig config,
            uint nb,
            ref uint nbColors,
            ref uint nbDeltas,
            ref Predictor predictor)
        {
            nbDeltas = 0;

            Dictionary<int[], int> implicitSet = new Dictionary<int[], int>(IntArrayComparer.Instance);
            List<int[]> implicitColors         = new List<int[]>(KImplicitPaletteSize);

            for (int k = 0; k < KImplicitPaletteSize; k++)
            {
                int[] col = new int[nb];

                for (int c = 0; c < (int)nb; c++)
                {
                    col[c] = ModularTransformOps.GetPaletteValue(
                        input.Channels[0],
                        k,
                        c,
                        0,
                        input.BitDepth);
                }

                implicitSet[col] = k;
                implicitColors.Add(col);
            }

            if (!CollectCandidatePalette(
                input,
                config.BeginC,
                nb,
                nbColors,
                implicitSet,
                out List<int[]> candidateOrder,
                out Dictionary<int[], int> freqMap,
                out uint implicitUsed))
            {
                return false;
            }

            nbColors = (uint)candidateOrder.Count;

            if (!config.Lossy && nbColors + implicitUsed <= 1)
            {
                return false;
            }

            FinalizeMultiChannelPalette(
                input,
                config,
                nb,
                implicitColors,
                candidateOrder,
                freqMap,
                ref nbColors,
                ref predictor);

            return true;
        }

        private static bool CollectCandidatePalette(
            ModularImage input,
            uint beginC,
            uint nb,
            uint maxColors,
            Dictionary<int[], int> implicitSet,
            out List<int[]> candidateOrder,
            out Dictionary<int[], int> freqMap,
            out uint implicitUsed)
        {
            candidateOrder = new List<int[]>();
            freqMap        = new Dictionary<int[], int>(IntArrayComparer.Instance);
            implicitUsed   = 0;

            HashSet<int[]> seen = new HashSet<int[]>(IntArrayComparer.Instance);
            nuint w             = input.Channels[(int)beginC].W;
            nuint h             = input.Channels[(int)beginC].H;

            for (nuint y = 0; y < h; y++)
            {
                for (int x = 0; x < (int)w; x++)
                {
                    int[] col = new int[nb];

                    for (int c = 0; c < (int)nb; c++)
                    {
                        col[c] = input.Channels[(int)beginC + c].ConstRow(y)[x];
                    }

                    if (seen.Add(col))
                    {
                        if (implicitSet.ContainsKey(col))
                        {
                            implicitUsed++;
                        }
                        else
                        {
                            candidateOrder.Add(col);

                            if (candidateOrder.Count > (int)maxColors)
                            {
                                return false;
                            }
                        }
                    }

                    freqMap[col] = freqMap.TryGetValue(col, out int count) ? count + 1 : 1;
                }
            }

            return true;
        }

        private static void FinalizeMultiChannelPalette(
            ModularImage input,
            PaletteTransformConfig config,
            uint nb,
            List<int[]> implicitColors,
            List<int[]> candidateOrder,
            Dictionary<int[], int> freqMap,
            ref uint nbColors,
            ref Predictor predictor)
        {
            for (int k = 0; k < implicitColors.Count; k++)
            {
                int[] col = implicitColors[k];

                if (freqMap.TryGetValue(col, out int freq) && freq > 10)
                {
                    nbColors++;
                    candidateOrder.Add(col);
                }
            }

            if (config.Ordered && nb >= 3)
            {
                candidateOrder.Sort((ap, bp) => ComparePaletteColors(ap, bp, freqMap));
            }

            Dictionary<int[], int> invPalette = new Dictionary<int[], int>(IntArrayComparer.Instance);

            for (int k = 0; k < implicitColors.Count; k++)
            {
                invPalette[implicitColors[k]] = (int)nbColors + k;
            }

            StatusOr<Channel> pchOr = Channel.Create(input.MemoryManager, nbColors, nb, -1, -1);
            Channel pch             = pchOr.Value;

            for (int clr = 0; clr < candidateOrder.Count; clr++)
            {
                int[] pcol = candidateOrder[clr];

                for (int c = 0; c < (int)nb; c++)
                {
                    pch.Row((nuint)c)[clr] = pcol[c];
                }

                invPalette[pcol] = clr;
            }

            MapPixelsToPaletteIndices(input, config.BeginC, nb, invPalette);

            predictor = Predictor.Zero;
            input.NbMetaChannels++;

            for (int c = (int)config.EndC; c >= (int)config.BeginC + 1; c--)
            {
                input.Channels[c].Dispose();
                input.Channels.RemoveAt(c);
            }

            input.Channels.Insert(0, pch);
        }

        private static int ComparePaletteColors(int[] ap, int[] bp, Dictionary<int[], int> freqMap)
        {
            float ay = (0.299f * ap[0]) + (0.587f * ap[1]) + (0.114f * ap[2]) + 0.1f;
            float by = (0.299f * bp[0]) + (0.587f * bp[1]) + (0.114f * bp[2]) + 0.1f;

            if (ap.Length > 3)
            {
                ay *= 1.0f + ap[3];
            }

            if (bp.Length > 3)
            {
                by *= 1.0f + bp[3];
            }

            ay = freqMap.TryGetValue(ap, out int fa) && fa > 4 ? -ay : ay;
            by = freqMap.TryGetValue(bp, out int fb) && fb > 4 ? -by : by;

            return ay.CompareTo(by);
        }

        private static void MapPixelsToPaletteIndices(
            ModularImage input,
            uint beginC,
            uint nb,
            Dictionary<int[], int> invPalette)
        {
            Channel firstCh = input.Channels[(int)beginC];
            nuint w         = firstCh.W;
            nuint h         = firstCh.H;
            int[] col       = new int[nb];

            for (nuint y = 0; y < h; y++)
            {
                Span<int> outRow = firstCh.Row(y);

                for (int x = 0; x < (int)w; x++)
                {
                    for (int c = 0; c < (int)nb; c++)
                    {
                        col[c] = input.Channels[(int)beginC + c].ConstRow(y)[x];
                    }

                    outRow[x] = invPalette[col];
                }
            }
        }
    }

    /// <summary>
    /// Forward dispatcher and min/max helper for Modular transforms.
    /// Ported from <c>lib/jxl/modular/transform/enc_transform.h</c> and <c>enc_transform.cc</c>.
    /// </summary>
    public static class EncTransform
    {
        /// <summary>
        /// Applies the forward transform <paramref name="tr"/> in-place on <paramref name="input"/>.
        /// Ported from <c>jxl::TransformForward</c> in <c>lib/jxl/modular/transform/enc_transform.cc</c>.
        /// </summary>
        public static Status TransformForward(
            Transform tr,
            ModularImage input,
            WeightedHeader wpHeader,
            ThreadPool? pool = null)
        {
            switch (tr.Id)
            {
                case TransformId.RCT:
                    return EncRct.FwdRct(input, tr.BeginC, tr.RctType, pool);

                case TransformId.Squeeze:
                    return EncSqueeze.FwdSqueeze(input, tr.Squeezes, pool);

                case TransformId.Palette:
                    PaletteTransformConfig cfg = new PaletteTransformConfig(
                        tr.BeginC,
                        tr.BeginC + tr.NumC - 1u,
                        tr.OrderedPalette,
                        tr.LossyPalette,
                        wpHeader);

                    return EncPalette.FwdPalette(input, cfg, ref tr.NbColors, ref tr.NbDeltas, ref tr.Predictor);

                default:
                    return Status.Failure($"Unknown transformation (ID={(uint)tr.Id})");
            }
        }

        /// <summary>
        /// Computes the minimum and maximum sample values in <paramref name="ch"/>.
        /// Ported from <c>jxl::compute_minmax</c> in <c>lib/jxl/modular/transform/enc_transform.cc</c>.
        /// </summary>
        /// <param name="ch">Channel plane to inspect.</param>
        /// <param name="min">Output minimum sample value.</param>
        /// <param name="max">Output maximum sample value.</param>
        public static void ComputeMinMax(Channel ch, out int min, out int max)
        {
            int realMin = int.MaxValue;
            int realMax = int.MinValue;

            for (nuint y = 0; y < ch.H; y++)
            {
                ReadOnlySpan<int> p = ch.ConstRow(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    int v = p[x];

                    if (v < realMin)
                    {
                        realMin = v;
                    }

                    if (v > realMax)
                    {
                        realMax = v;
                    }
                }
            }

            min = realMin;
            max = realMax;
        }
    }
}
