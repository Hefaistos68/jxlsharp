// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Functions for clustering similar histograms together, encoding context maps,
// and tokenizing quantized AC coefficients.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Additional methods on <see cref="Histogram"/> for entropy, conditioning, and ANS population cost.
    /// Ported from <c>lib/jxl/enc_ans_params.h</c> and <c>lib/jxl/enc_cluster.cc</c>.
    /// </summary>
    public sealed partial class Histogram
    {
        /// <summary>
        /// Creates a flat histogram of length <paramref name="length"/> with total count <paramref name="totalCount"/>.
        /// </summary>
        /// <param name="length">Alphabet length.</param>
        /// <param name="totalCount">Total count to distribute.</param>
        /// <returns>Flat <see cref="Histogram"/>.</returns>
        public static Histogram Flat(int length, int totalCount)
        {
            Histogram flat  = new Histogram();
            int[] flatArr   = AliasTable.CreateFlatHistogram(length, totalCount);
            flat.Counts     = new List<int>(flatArr);
            flat.TotalCount = (nuint)totalCount;

            return flat;
        }

        /// <summary>
        /// Trims trailing zero blocks and recomputes <see cref="TotalCount"/> after a sequence of <see cref="FastAdd"/> calls.
        /// Ported from <c>jxl::HistogramCondition</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        public void Condition()
        {
            int total = 0;
            int nzPos = -KRounding;

            for (int i = 0; i < Counts.Count; i += KRounding)
            {
                bool nz  = false;
                int stop = Math.Min(i + KRounding, Counts.Count);

                for (int k = i; k < stop; k++)
                {
                    int c  = Counts[k];
                    total += c;

                    if (c != 0)
                    {
                        nz = true;
                    }
                }

                if (nz)
                {
                    nzPos = i;
                }
            }

            int newSize = Math.Max(0, nzPos + KRounding);

            if (Counts.Count > newSize)
            {
                Counts.RemoveRange(newSize, Counts.Count - newSize);
            }
            else
            {
                while (Counts.Count < newSize)
                {
                    Counts.Add(0);
                }
            }

            TotalCount = (nuint)total;
        }

        /// <summary>
        /// Returns an estimate of the number of bits required to encode this histogram (header bits plus data bits).
        /// Ported from <c>jxl::Histogram::ANSPopulationCost</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <returns>Estimated bit cost.</returns>
        public StatusOr<float> ANSPopulationCost()
        {
            return EncAns.ANSPopulationCost(Counts, TotalCount);
        }

        /// <summary>
        /// Computes and caches the Shannon entropy (in total bits) of this histogram.
        /// Ported from <c>jxl::Histogram::ShannonEntropy</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        /// <returns>Shannon entropy in bits.</returns>
        public float ShannonEntropy()
        {
            EncCluster.HistogramEntropy(this);

            return Entropy;
        }
    }

    /// <summary>
    /// Functions for clustering similar histograms together.
    /// Ported from <c>lib/jxl/enc_cluster.h</c> and <c>lib/jxl/enc_cluster.cc</c>.
    /// </summary>
    public static class EncCluster
    {
        private const float KMinDistanceForDistinct = 48.0f;

        /// <summary>
        /// Computes and stores the Shannon entropy of <paramref name="a"/> in <see cref="Histogram.Entropy"/>.
        /// Ported from <c>jxl::HistogramEntropy</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        /// <param name="a">Target histogram.</param>
        public static void HistogramEntropy(Histogram a)
        {
            a.Entropy = 0.0f;

            if (a.TotalCount == 0)
            {
                return;
            }

            float total   = a.TotalCount;
            float invTot  = 1.0f / total;
            float sumBits = 0.0f;

            for (int i = 0; i < a.Counts.Count; i++)
            {
                float count = a.Counts[i];

                if (count > 0.0f && count != total)
                {
                    sumBits -= count * FastMath.FastLog2f(invTot * count);
                }
            }

            a.Entropy = sumBits;
        }

        /// <summary>
        /// Computes the entropy distance between two histograms <paramref name="a"/> and <paramref name="b"/>.
        /// Ported from <c>jxl::HistogramDistance</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        /// <param name="a">First histogram (must have <see cref="Histogram.Entropy"/> up to date).</param>
        /// <param name="b">Second histogram (must have <see cref="Histogram.Entropy"/> up to date).</param>
        /// <returns>Additional bits required if <paramref name="a"/> and <paramref name="b"/> are merged.</returns>
        public static float HistogramDistance(Histogram a, Histogram b)
        {
            if (a.TotalCount == 0 || b.TotalCount == 0)
            {
                return 0.0f;
            }

            float total         = a.TotalCount + b.TotalCount;
            float invTot        = 1.0f / total;
            float totalDistance = 0.0f;
            int maxLen          = Math.Max(a.Counts.Count, b.Counts.Count);

            for (int i = 0; i < maxLen; i++)
            {
                int ac      = i < a.Counts.Count ? a.Counts[i] : 0;
                int bc      = i < b.Counts.Count ? b.Counts[i] : 0;
                float count = ac + bc;

                if (count > 0.0f && count != total)
                {
                    totalDistance -= count * FastMath.FastLog2f(invTot * count);
                }
            }

            return totalDistance - a.Entropy - b.Entropy;
        }

        /// <summary>
        /// Performs fast k-means-like clustering over <paramref name="inHists"/>.
        /// Ported from <c>jxl::FastClusterHistograms</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        /// <param name="inHists">Input histograms.</param>
        /// <param name="maxHistograms">Maximum total clusters in <paramref name="outHists"/>.</param>
        /// <param name="prefixCodes">Whether prefix (Huffman) coding is forced.</param>
        /// <param name="outHists">Output clustered histograms.</param>
        /// <param name="histogramSymbols">Output mapping from input histogram index to cluster index.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status FastClusterHistograms(
            IReadOnlyList<Histogram> inHists,
            nuint maxHistograms,
            bool prefixCodes,
            List<Histogram> outHists,
            List<uint> histogramSymbols)
        {
            nuint prevHistograms = (nuint)outHists.Count;

            if (outHists.Capacity < (int)maxHistograms)
            {
                outHists.Capacity = (int)maxHistograms;
            }

            histogramSymbols.Clear();

            for (int i = 0; i < inHists.Count; i++)
            {
                histogramSymbols.Add((uint)maxHistograms);
            }

            float[] dists    = new float[inHists.Count];
            Array.Fill(dists, float.MaxValue);
            nuint largestIdx = InitializeClusterSeeds(inHists, prevHistograms, outHists, histogramSymbols, dists);

            SelectDistinctCentroids(inHists, maxHistograms, prefixCodes, outHists, histogramSymbols, dists, largestIdx);

            return AssignRemainingToClusters(inHists, maxHistograms, prevHistograms, prefixCodes, outHists, histogramSymbols);
        }

        /// <summary>
        /// Clusters similar histograms in <paramref name="inHists"/> together into <paramref name="outHists"/>
        /// and records the cluster index for each input histogram in <paramref name="histogramSymbols"/>.
        /// Ported from <c>jxl::ClusterHistograms</c> in <c>lib/jxl/enc_cluster.cc</c>.
        /// </summary>
        /// <param name="histParams">Clustering parameters.</param>
        /// <param name="inHists">Input per-context histograms.</param>
        /// <param name="maxHistograms">Maximum number of new clusters.</param>
        /// <param name="outHists">Output clustered histograms.</param>
        /// <param name="histogramSymbols">Output context-to-cluster map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ClusterHistograms(
            HistogramParams histParams,
            IReadOnlyList<Histogram> inHists,
            nuint maxHistograms,
            List<Histogram> outHists,
            List<uint> histogramSymbols)
        {
            nuint prevHistograms = (nuint)outHists.Count;
            maxHistograms        = Math.Min(maxHistograms, histParams.MaxHistograms);
            maxHistograms        = Math.Min(maxHistograms, (nuint)inHists.Count);

            if (histParams.Clustering == HistogramParams.ClusteringType.kFastest)
            {
                maxHistograms = Math.Min(maxHistograms, 4u);
            }

            if (!FastClusterHistograms(
                    inHists,
                    prevHistograms + maxHistograms,
                    histParams.ForceHuffman,
                    outHists,
                    histogramSymbols))
            {
                return false;
            }

            if (prevHistograms == 0 &&
                histParams.Clustering == HistogramParams.ClusteringType.kBest)
            {
                if (!RefineClustersGreedyMerge(outHists, histogramSymbols))
                {
                    return false;
                }
            }

            // Convert the context map to a canonical form.
            HistogramReindex(outHists, prevHistograms, histogramSymbols);

            return true;
        }

        private static nuint NumSymbols(Histogram a, Histogram? b)
        {
            nuint n  = 0;
            int size = Math.Max(a.Counts.Count, b != null ? b.Counts.Count : 0);

            for (int i = 0; i < size; i++)
            {
                int count = (i < a.Counts.Count ? a.Counts[i] : 0) +
                            (b != null && i < b.Counts.Count ? b.Counts[i] : 0);

                if (count > 0)
                {
                    n++;
                }
            }

            return n;
        }

        private static float ClusterDistance(Histogram a, Histogram b, bool prefixCodes)
        {
            float distance = HistogramDistance(a, b);

            if (!prefixCodes || a.TotalCount == 0 || b.TotalCount == 0 || NumSymbols(a, b) <= 1)
            {
                return distance;
            }

            float single = 0.0f;

            if (NumSymbols(a, null) <= 1)
            {
                single += a.TotalCount;
            }

            if (NumSymbols(b, null) <= 1)
            {
                single += b.TotalCount;
            }

            return Math.Max(distance, single);
        }

        private static float HistogramKLDivergence(Histogram actual, Histogram coding)
        {
            if (actual.TotalCount == 0)
            {
                return 0.0f;
            }

            if (coding.TotalCount == 0)
            {
                return float.PositiveInfinity;
            }

            float codingInv = 1.0f / coding.TotalCount;
            float totalCost = 0.0f;

            for (int i = 0; i < actual.Counts.Count; i++)
            {
                int counts = actual.Counts[i];

                if (counts == 0)
                {
                    continue;
                }

                int codingCounts = i < coding.Counts.Count ? coding.Counts[i] : 0;

                if (codingCounts == 0)
                {
                    return float.PositiveInfinity;
                }

                float codingProb    = codingCounts * codingInv;
                float negCodingCost = FastMath.FastLog2f(codingProb);
                totalCost          -= counts * negCodingCost;
            }

            return totalCost - actual.Entropy;
        }

        private static nuint InitializeClusterSeeds(
            IReadOnlyList<Histogram> inHists,
            nuint prevHistograms,
            List<Histogram> outHists,
            List<uint> histogramSymbols,
            float[] dists)
        {
            nuint largestIdx = 0;

            for (int i = 0; i < inHists.Count; i++)
            {
                if (inHists[i].TotalCount == 0)
                {
                    histogramSymbols[i] = 0;
                    dists[i]            = 0.0f;
                    continue;
                }

                HistogramEntropy(inHists[i]);

                if (inHists[i].TotalCount > inHists[(int)largestIdx].TotalCount)
                {
                    largestIdx = (nuint)i;
                }
            }

            if (prevHistograms > 0)
            {
                for (int j = 0; j < (int)prevHistograms; ++j)
                {
                    HistogramEntropy(outHists[j]);
                }

                float maxDistVal = 0.0f;
                int maxDistIdx   = (int)largestIdx;

                for (int i = 0; i < inHists.Count; i++)
                {
                    if (dists[i] == 0.0f)
                    {
                        continue;
                    }

                    for (int j = 0; j < (int)prevHistograms; ++j)
                    {
                        dists[i] = Math.Min(HistogramKLDivergence(inHists[i], outHists[j]), dists[i]);
                    }

                    if (dists[i] > maxDistVal)
                    {
                        maxDistVal = dists[i];
                        maxDistIdx = i;
                    }
                }

                if (maxDistVal > 0.0f)
                {
                    largestIdx = (nuint)maxDistIdx;
                }
            }

            return largestIdx;
        }

        private static void SelectDistinctCentroids(
            IReadOnlyList<Histogram> inHists,
            nuint maxHistograms,
            bool prefixCodes,
            List<Histogram> outHists,
            List<uint> histogramSymbols,
            float[] dists,
            nuint largestIdx)
        {
            while ((nuint)outHists.Count < maxHistograms && inHists.Count > 0)
            {
                histogramSymbols[(int)largestIdx] = (uint)outHists.Count;
                outHists.Add(inHists[(int)largestIdx].Clone());
                dists[(int)largestIdx]            = 0.0f;
                largestIdx                        = 0;
                Histogram back                    = outHists[outHists.Count - 1];

                for (int i = 0; i < inHists.Count; i++)
                {
                    if (dists[i] == 0.0f)
                    {
                        continue;
                    }

                    dists[i] = Math.Min(ClusterDistance(inHists[i], back, prefixCodes), dists[i]);

                    if (dists[i] > dists[(int)largestIdx])
                    {
                        largestIdx = (nuint)i;
                    }
                }

                if (dists[(int)largestIdx] < KMinDistanceForDistinct)
                {
                    break;
                }
            }
        }

        private static Status AssignRemainingToClusters(
            IReadOnlyList<Histogram> inHists,
            nuint maxHistograms,
            nuint prevHistograms,
            bool prefixCodes,
            List<Histogram> outHists,
            List<uint> histogramSymbols)
        {
            for (int i = 0; i < inHists.Count; i++)
            {
                if (histogramSymbols[i] != (uint)maxHistograms)
                {
                    continue;
                }

                int best       = 0;
                float bestDist = float.MaxValue;

                for (int j = 0; j < outHists.Count; j++)
                {
                    float dist = (nuint)j < prevHistograms
                        ? HistogramKLDivergence(inHists[i], outHists[j])
                        : ClusterDistance(inHists[i], outHists[j], prefixCodes);

                    if (dist < bestDist)
                    {
                        best     = j;
                        bestDist = dist;
                    }
                }

                if (bestDist >= float.MaxValue)
                {
                    return Status.Failure("No valid histogram cluster found");
                }

                if ((nuint)best >= prevHistograms)
                {
                    outHists[best].AddHistogram(inHists[i]);
                    HistogramEntropy(outHists[best]);
                }

                histogramSymbols[i] = (uint)best;
            }

            return true;
        }

        private readonly struct HistogramPair : IComparable<HistogramPair>
        {
            public readonly float Cost;
            public readonly uint First;
            public readonly uint Second;
            public readonly uint Version;

            public HistogramPair(float cost, uint first, uint second, uint version)
            {
                Cost    = cost;
                First   = first;
                Second  = second;
                Version = version;
            }

            public int CompareTo(HistogramPair other)
            {
                int c = Cost.CompareTo(other.Cost);

                if (c != 0)
                {
                    return c;
                }

                c = First.CompareTo(other.First);

                if (c != 0)
                {
                    return c;
                }

                c = Second.CompareTo(other.Second);

                return c != 0 ? c : Version.CompareTo(other.Version);
            }
        }

        private static Status RefineClustersGreedyMerge(
            List<Histogram> outHists,
            List<uint> histogramSymbols)
        {
            int count = outHists.Count;

            for (int i = 0; i < count; i++)
            {
                StatusOr<float> costOr = outHists[i].ANSPopulationCost();

                if (!costOr.IsOk)
                {
                    return costOr.Status;
                }

                outHists[i].Entropy = costOr.Value;
            }

            uint nextVersion  = 2;
            uint[] version    = new uint[count];
            uint[] renumber   = new uint[count];
            Array.Fill(version, 1u);

            for (int i = 0; i < count; i++)
            {
                renumber[i] = (uint)i;
            }

            PriorityQueue<HistogramPair, HistogramPair> pairsToMerge = new PriorityQueue<HistogramPair, HistogramPair>();

            if (!EnqueueInitialPairs(outHists, version, pairsToMerge))
            {
                return false;
            }

            while (pairsToMerge.Count > 0)
            {
                HistogramPair top = pairsToMerge.Dequeue();

                if (!ProcessMergeCandidate(top, outHists, version, renumber, ref nextVersion, pairsToMerge))
                {
                    return false;
                }
            }

            CompactAliveClusters(outHists, version, renumber, histogramSymbols);

            return true;
        }

        private static Status EnqueueInitialPairs(
            List<Histogram> outHists,
            uint[] version,
            PriorityQueue<HistogramPair, HistogramPair> pairsToMerge)
        {
            for (uint i = 0; i < (uint)outHists.Count; i++)
            {
                for (uint j = i + 1; j < (uint)outHists.Count; j++)
                {
                    Histogram histo = new Histogram();
                    histo.AddHistogram(outHists[(int)i]);
                    histo.AddHistogram(outHists[(int)j]);

                    StatusOr<float> costOr = histo.ANSPopulationCost();

                    if (!costOr.IsOk)
                    {
                        return costOr.Status;
                    }

                    float cost = costOr.Value - (outHists[(int)i].Entropy + outHists[(int)j].Entropy);

                    if (cost >= 0.0f)
                    {
                        continue;
                    }

                    HistogramPair pair = new HistogramPair(cost, i, j, Math.Max(version[i], version[j]));
                    pairsToMerge.Enqueue(pair, pair);
                }
            }

            return true;
        }

        private static Status ProcessMergeCandidate(
            HistogramPair top,
            List<Histogram> outHists,
            uint[] version,
            uint[] renumber,
            ref uint nextVersion,
            PriorityQueue<HistogramPair, HistogramPair> pairsToMerge)
        {
            uint first  = top.First;
            uint second = top.Second;
            uint ver    = top.Version;

            if (ver != Math.Max(version[first], version[second]) ||
                version[first] == 0 ||
                version[second] == 0)
            {
                return true;
            }

            outHists[(int)first].AddHistogram(outHists[(int)second]);
            StatusOr<float> costOr = outHists[(int)first].ANSPopulationCost();

            if (!costOr.IsOk)
            {
                return costOr.Status;
            }

            outHists[(int)first].Entropy = costOr.Value;

            for (int k = 0; k < renumber.Length; k++)
            {
                if (renumber[k] == second)
                {
                    renumber[k] = first;
                }
            }

            version[second] = 0;
            version[first]  = nextVersion++;

            for (uint j = 0; j < (uint)outHists.Count; j++)
            {
                if (j == first || version[j] == 0)
                {
                    continue;
                }

                Histogram histo = new Histogram();
                histo.AddHistogram(outHists[(int)first]);
                histo.AddHistogram(outHists[(int)j]);

                StatusOr<float> mergeCostOr = histo.ANSPopulationCost();

                if (!mergeCostOr.IsOk)
                {
                    return mergeCostOr.Status;
                }

                float mergeCost = mergeCostOr.Value - (outHists[(int)first].Entropy + outHists[(int)j].Entropy);

                if (mergeCost >= 0.0f)
                {
                    continue;
                }

                HistogramPair pair = new HistogramPair(
                    mergeCost,
                    Math.Min(first, j),
                    Math.Max(first, j),
                    Math.Max(version[first], version[j]));
                pairsToMerge.Enqueue(pair, pair);
            }

            return true;
        }

        private static void CompactAliveClusters(
            List<Histogram> outHists,
            uint[] version,
            uint[] renumber,
            List<uint> histogramSymbols)
        {
            uint[] reverseRenumbering = new uint[outHists.Count];
            Array.Fill(reverseRenumbering, uint.MaxValue);
            int numAlive = 0;

            for (int i = 0; i < outHists.Count; i++)
            {
                if (version[i] == 0)
                {
                    continue;
                }

                outHists[numAlive++]  = outHists[i];
                reverseRenumbering[i] = (uint)(numAlive - 1);
            }

            if (outHists.Count > numAlive)
            {
                outHists.RemoveRange(numAlive, outHists.Count - numAlive);
            }

            for (int i = 0; i < histogramSymbols.Count; i++)
            {
                histogramSymbols[i] = reverseRenumbering[renumber[histogramSymbols[i]]];
            }
        }

        private static void HistogramReindex(
            List<Histogram> outHists,
            nuint prevHistograms,
            List<uint> symbols)
        {
            List<Histogram> tmp             = new List<Histogram>(outHists);
            Dictionary<uint, uint> newIndex = new Dictionary<uint, uint>();

            for (uint i = 0; i < (uint)prevHistograms; ++i)
            {
                newIndex[i] = i;
            }

            uint nextIndex = (uint)prevHistograms;

            for (int i = 0; i < symbols.Count; i++)
            {
                uint symbol = symbols[i];

                if (!newIndex.ContainsKey(symbol))
                {
                    newIndex[symbol]          = nextIndex;
                    outHists[(int)nextIndex]  = tmp[(int)symbol];
                    ++nextIndex;
                }
            }

            if (outHists.Count > (int)nextIndex)
            {
                outHists.RemoveRange((int)nextIndex, outHists.Count - (int)nextIndex);
            }

            for (int i = 0; i < symbols.Count; i++)
            {
                symbols[i] = newIndex[symbols[i]];
            }
        }
    }

    /// <summary>
    /// Functions for encoding context maps and block context maps into the bitstream.
    /// Ported from <c>lib/jxl/enc_context_map.h</c> and <c>lib/jxl/enc_context_map.cc</c>.
    /// </summary>
    public static class EncContextMap
    {
        /// <summary>Maximum number of histogram clusters (128).</summary>
        public const nuint KClustersLimit = 128;

        /// <summary>
        /// Encodes <paramref name="contextMap"/> with <paramref name="numHistograms"/> distinct histogram IDs into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeContextMap</c> in <c>lib/jxl/enc_context_map.cc</c>.
        /// </summary>
        /// <param name="contextMap">Context-to-cluster mapping.</param>
        /// <param name="numHistograms">Number of distinct histogram clusters.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodeContextMap(
            IReadOnlyList<byte> contextMap,
            nuint numHistograms,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (numHistograms == 1)
            {
                // Simple code: 0 bits per entry.
                writer.Write(1, 1);
                writer.Write(2, 0);

                return true;
            }

            JxlMemoryManager? memoryManager = writer.MemoryManager;
            byte[] transformedSymbols       = MoveToFrontTransform(contextMap);
            List<List<Token>> tokens        = new List<List<Token>> { new List<Token>(contextMap.Count) };
            List<List<Token>> mtfTokens     = new List<List<Token>> { new List<Token>(transformedSymbols.Length) };

            for (int i = 0; i < contextMap.Count; i++)
            {
                tokens[0].Add(new Token(0, contextMap[i]));
            }

            for (int i = 0; i < transformedSymbols.Length; i++)
            {
                mtfTokens[0].Add(new Token(0, transformedSymbols[i]));
            }

            HistogramParams histParams = new HistogramParams
            {
                UintMethod = HistogramParams.HybridUintMethod.kContextMap,
            };

            StatusOr<nuint> ansCostOr = EstimateContextMapTokensCost(memoryManager, histParams, tokens);

            if (!ansCostOr.IsOk)
            {
                return ansCostOr.Status;
            }

            StatusOr<nuint> mtfCostOr = EstimateContextMapTokensCost(memoryManager, histParams, mtfTokens);

            if (!mtfCostOr.IsOk)
            {
                return mtfCostOr.Status;
            }

            nuint ansCost    = ansCostOr.Value;
            nuint mtfCost    = mtfCostOr.Value;
            bool useMtf      = mtfCost < ansCost;
            nuint entryBits  = (nuint)Bits.CeilLog2Nonzero((ulong)numHistograms);
            nuint simpleCost = entryBits * (nuint)contextMap.Count;

            if (entryBits < 4 && simpleCost < ansCost && simpleCost < mtfCost)
            {
                return WriteSimpleContextMap(contextMap, entryBits, writer, layer, auxOut);
            }

            tokens[0].Clear();

            for (int i = 0; i < transformedSymbols.Length; i++)
            {
                tokens[0].Add(new Token(0, useMtf ? transformedSymbols[i] : contextMap[i]));
            }

            return WriteEntropyContextMap(memoryManager, histParams, tokens, useMtf, writer, layer, auxOut);
        }

        /// <summary>
        /// Encodes a <see cref="BlockCtxMap"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeBlockCtxMap</c> in <c>lib/jxl/enc_context_map.cc</c>.
        /// </summary>
        /// <param name="blockCtxMap">Block context map to encode.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodeBlockCtxMap(
            BlockCtxMap blockCtxMap,
            BitWriter writer,
            AuxOut? auxOut)
        {
            int[][] dct    = blockCtxMap.DcThresholds;
            uint[] qft     = blockCtxMap.QfThresholds;
            byte[] ctxMap  = blockCtxMap.CtxMap;
            nuint maxBits  = (nuint)(((dct[0].Length + dct[1].Length + dct[2].Length + qft.Length) * 34) +
                             1 + 4 + 4 + (ctxMap.Length * 10) + 1024);

            return writer.WithMaxBits(
                maxBits,
                LayerType.Ac,
                auxOut,
                () =>
                {
                    if (IsDefaultBlockCtxMap(dct, qft, ctxMap))
                    {
                        writer.Write(1, 1);

                        return true;
                    }

                    writer.Write(1, 0);

                    for (int j = 0; j < 3; j++)
                    {
                        writer.Write(4, (ulong)dct[j].Length);

                        for (int i = 0; i < dct[j].Length; i++)
                        {
                            if (!U32Coder.Write(EntropyCoder.DcThresholdDist, PackSigned.Pack(dct[j][i]), writer))
                            {
                                return false;
                            }
                        }
                    }

                    writer.Write(4, (ulong)qft.Length);

                    for (int i = 0; i < qft.Length; i++)
                    {
                        if (!U32Coder.Write(EntropyCoder.QfThresholdDist, qft[i] - 1u, writer))
                        {
                            return false;
                        }
                    }

                    return EncodeContextMap(ctxMap, blockCtxMap.NumCtxs, writer, LayerType.Ac, auxOut);
                });
        }

        private static int IndexOf(ReadOnlySpan<byte> v, byte value)
        {
            for (int i = 0; i < v.Length; ++i)
            {
                if (v[i] == value)
                {
                    return i;
                }
            }

            return v.Length;
        }

        private static void MoveToFront(Span<byte> v, int index)
        {
            byte value = v[index];

            for (int i = index; i != 0; --i)
            {
                v[i] = v[i - 1];
            }

            v[0] = value;
        }

        private static byte[] MoveToFrontTransform(IReadOnlyList<byte> v)
        {
            if (v.Count == 0)
            {
                return Array.Empty<byte>();
            }

            byte maxValue = 0;

            for (int i = 0; i < v.Count; i++)
            {
                if (v[i] > maxValue)
                {
                    maxValue = v[i];
                }
            }

            byte[] mtf = new byte[maxValue + 1];

            for (int i = 0; i <= maxValue; ++i)
            {
                mtf[i] = (byte)i;
            }

            byte[] result = new byte[v.Count];

            for (int i = 0; i < v.Count; ++i)
            {
                int index = IndexOf(mtf, v[i]);
                Debug.Assert(index < mtf.Length);
                result[i] = (byte)index;
                MoveToFront(mtf, index);
            }

            return result;
        }

        private static StatusOr<nuint> EstimateContextMapTokensCost(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            List<List<Token>> tokens)
        {
            EntropyEncodingData codes = new EntropyEncodingData();

            return EncAns.BuildAndEncodeHistograms(
                memoryManager,
                histParams,
                1,
                tokens,
                codes,
                null,
                (LayerType.Header, null));
        }

        private static Status WriteSimpleContextMap(
            IReadOnlyList<byte> contextMap,
            nuint entryBits,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            return writer.WithMaxBits(
                3 + (entryBits * (nuint)contextMap.Count),
                layer,
                auxOut,
                () =>
                {
                    writer.Write(1, 1);
                    writer.Write(2, entryBits);

                    for (int i = 0; i < contextMap.Count; i++)
                    {
                        writer.Write(entryBits, contextMap[i]);
                    }

                    return true;
                });
        }

        private static Status WriteEntropyContextMap(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            List<List<Token>> tokens,
            bool useMtf,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            return writer.WithMaxBits(
                2 + ((nuint)tokens[0].Count * 24),
                layer,
                auxOut,
                () =>
                {
                    writer.Write(1, 0);
                    writer.Write(1, useMtf ? 1UL : 0UL);
                    EntropyEncodingData codes = new EntropyEncodingData();
                    StatusOr<nuint> costOr    = EncAns.BuildAndEncodeHistograms(
                        memoryManager,
                        histParams,
                        1,
                        tokens,
                        codes,
                        writer,
                        (layer, auxOut));

                    if (!costOr.IsOk)
                    {
                        return costOr.Status;
                    }

                    EncAns.WriteTokens(tokens[0], codes, 0, writer);

                    return true;
                });
        }

        private static bool IsDefaultBlockCtxMap(int[][] dct, uint[] qft, byte[] ctxMap)
        {
            if (dct[0].Length != 0 || dct[1].Length != 0 || dct[2].Length != 0 || qft.Length != 0)
            {
                return false;
            }

            return ctxMap.AsSpan().SequenceEqual(BlockCtxMap.DefaultCtxMap);
        }
    }

    /// <summary>
    /// Tokenizes quantized DCT NxN AC coefficients into entropy <see cref="Token"/> streams.
    /// Ported from <c>lib/jxl/enc_entropy_coder.h</c> and <c>lib/jxl/enc_entropy_coder.cc</c>.
    /// </summary>
    public static class EncEntropyCoder
    {
        private static readonly int[] KChannelVisitOrder = { 1, 0, 2 };

        /// <summary>
        /// Generates DCT NxN quantized AC coefficient tokens for all blocks within <paramref name="rect"/>.
        /// Ported from <c>jxl::TokenizeCoefficients</c> in <c>lib/jxl/enc_entropy_coder.cc</c>.
        /// </summary>
        /// <param name="orders">Flat coefficient scan order table.</param>
        /// <param name="rect">Block-unit rectangle within the group.</param>
        /// <param name="acRows">Per-channel quantized AC coefficient pointers (length 3).</param>
        /// <param name="acStrategy">AC transform strategy image.</param>
        /// <param name="cs">Chroma subsampling configuration.</param>
        /// <param name="tmpNumNzeroes">Scratch 3-plane image for tracking per-block non-zero counts.</param>
        /// <param name="outputAndContext">Tuple of output token list, quantized DC plane, quantizer field plane, and block context map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status TokenizeCoefficients(
            ReadOnlySpan<uint> orders,
            Rect rect,
            ReadOnlySpan<ACPtr> acRows,
            AcStrategyImage acStrategy,
            YCbCrChromaSubsampling cs,
            Image3<int> tmpNumNzeroes,
            (List<Token> Output, ImagePlane<byte> Qdc, ImagePlane<int> Qf, BlockCtxMap BlockCtxMap) outputAndContext)
        {
            nuint xsizeBlocks  = (nuint)rect.XSize;
            nuint ysizeBlocks  = (nuint)rect.YSize;
            List<Token> output = outputAndContext.Output;
            output.Clear();
            int estimatedCap = Math.Min((int)(xsizeBlocks * ysizeBlocks * 16), 8192);

            if (output.Capacity < estimatedCap)
            {
                output.Capacity = estimatedCap;
            }

            nuint[] offset     = new nuint[3];
            nuint nzerosStride = (nuint)tmpNumNzeroes.PixelsPerRow;

            for (nuint by = 0; by < ysizeBlocks; ++by)
            {
                BlockRowPointers ptrs = SetupBlockRowPointers(
                    by,
                    rect,
                    orders,
                    acRows,
                    tmpNumNzeroes,
                    (outputAndContext.Qdc, outputAndContext.Qf, nzerosStride));
                AcStrategyRow acsRow  = acStrategy.ConstRow(rect, by);

                for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                {
                    AcStrategy acs = acsRow[bx];

                    if (!acs.IsFirstBlock())
                    {
                        continue;
                    }

                    if (!TokenizeBlockAllChannels(
                            bx,
                            by,
                            acs,
                            cs,
                            in ptrs,
                            outputAndContext.BlockCtxMap,
                            (offset, output)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private readonly ref struct BlockRowPointers
        {
            public readonly ReadOnlySpan<uint> Orders;
            public readonly ReadOnlySpan<ACPtr> AcRows;
            public readonly Image3<int> TmpNumNzeroes;
            public readonly ReadOnlySpan<byte> RowQdc;
            public readonly ReadOnlySpan<int> RowQf;
            public readonly nuint NzerosStride;

            public BlockRowPointers(
                ReadOnlySpan<uint> orders,
                ReadOnlySpan<ACPtr> acRows,
                Image3<int> tmpNumNzeroes,
                ReadOnlySpan<byte> rowQdc,
                ReadOnlySpan<int> rowQf,
                nuint nzerosStride)
            {
                Orders        = orders;
                AcRows        = acRows;
                TmpNumNzeroes = tmpNumNzeroes;
                RowQdc        = rowQdc;
                RowQf         = rowQf;
                NzerosStride  = nzerosStride;
            }
        }

        private static BlockRowPointers SetupBlockRowPointers(
            nuint by,
            Rect rect,
            ReadOnlySpan<uint> orders,
            ReadOnlySpan<ACPtr> acRows,
            Image3<int> tmpNumNzeroes,
            (ImagePlane<byte> Qdc, ImagePlane<int> Qf, nuint NzerosStride) inputs)
        {
            ReadOnlySpan<byte> rowQdc = inputs.Qdc.ConstRow((nuint)rect.Y0 + by).Slice(rect.X0);
            ReadOnlySpan<int> rowQf   = rect.ConstRow(inputs.Qf, by);

            return new BlockRowPointers(
                orders,
                acRows,
                tmpNumNzeroes,
                rowQdc,
                rowQf,
                inputs.NzerosStride);
        }

        private static Status TokenizeBlockAllChannels(
            nuint bx,
            nuint by,
            AcStrategy acs,
            YCbCrChromaSubsampling cs,
            in BlockRowPointers ptrs,
            BlockCtxMap blockCtxMap,
            (nuint[] Offset, List<Token> Output) state)
        {
            nuint[] offset          = state.Offset;
            List<Token> output      = state.Output;
            nuint cx                = acs.CoveredBlocksX();
            nuint cy                = acs.CoveredBlocksY();
            nuint coveredBlocks     = cx * cy;
            nuint log2CoveredBlocks = (nuint)Bits.Num0BitsBelowLS1BitNonzero((ulong)coveredBlocks);
            nuint size              = coveredBlocks * FrameDimensions.kDCTBlockSize;
            CoeffOrder.CoefficientLayout(ref cy, ref cx);

            for (int idx = 0; idx < 3; idx++)
            {
                int c     = KChannelVisitOrder[idx];
                int hs    = (int)cs.HShift((nuint)c);
                int vs    = (int)cs.VShift((nuint)c);
                nuint sbx = bx >> hs;
                nuint sby = by >> vs;

                if ((sbx << hs) != bx || (sby << vs) != by)
                {
                    continue;
                }

                Span<int> rowNzeros            = ptrs.TmpNumNzeroes.PlaneRow((nuint)c, sby);
                ReadOnlySpan<int> rowNzerosTop = sby == 0
                    ? ReadOnlySpan<int>.Empty
                    : ptrs.TmpNumNzeroes.ConstPlaneRow((nuint)c, sby - 1);
                ReadOnlySpan<int> block        = ptrs.AcRows[c].Span32.Slice((int)offset[c]);
                int nzeros                     = coveredBlocks == 1
                    ? NumNonZero8x8ExceptDC(block, rowNzeros.Slice((int)sbx))
                    : NumNonZeroExceptLLF((cx, cy, coveredBlocks, log2CoveredBlocks, ptrs.NzerosStride), acs, block, rowNzeros.Slice((int)sbx));

                int ord                  = CoeffOrder.StrategyOrder[acs.RawStrategy()];
                ReadOnlySpan<uint> order = ptrs.Orders.Slice((int)CoeffOrder.CoeffOrderOffset(ord, c));

                int predictedNzeros = PredictNzeros(rowNzerosTop, rowNzeros, (int)sbx);
                uint blockCtx       = (uint)blockCtxMap.Context(ptrs.RowQdc[(int)bx], (uint)ptrs.RowQf[(int)sbx], (nuint)ord, (nuint)c);
                uint nzeroCtx       = blockCtxMap.NonZeroContext((uint)predictedNzeros, blockCtx);

                output.Add(new Token(nzeroCtx, (uint)nzeros));
                uint histoOffset = blockCtxMap.ZeroDensityContextsOffset(blockCtx);

                if (!EmitBlockChannelTokens(block, order, nzeros, histoOffset, (coveredBlocks, log2CoveredBlocks, size), output))
                {
                    return Status.Failure("Non-zero count mismatch during TokenizeCoefficients");
                }

                offset[c] += size;
            }

            return true;
        }

        private static bool EmitBlockChannelTokens(
            ReadOnlySpan<int> block,
            ReadOnlySpan<uint> order,
            int nzeros,
            uint histoOffset,
            (nuint CoveredBlocks, nuint Log2CoveredBlocks, nuint Size) geom,
            List<Token> output)
        {
            nuint prev = (nuint)nzeros > (geom.Size / 16) ? 0u : 1u;

            for (nuint k = geom.CoveredBlocks; k < geom.Size && nzeros != 0; ++k)
            {
                int coeff   = block[(int)order[(int)k]];
                nuint ctx   = histoOffset + AcContext.ZeroDensityContext((nuint)nzeros, k, geom.CoveredBlocks, geom.Log2CoveredBlocks, prev);
                uint uCoeff = PackSigned.Pack(coeff);
                output.Add(new Token((uint)ctx, uCoeff));
                prev    = coeff != 0 ? 1u : 0u;
                nzeros -= (int)prev;
            }

            return nzeros == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PredictNzeros(ReadOnlySpan<int> rowTop, ReadOnlySpan<int> row, int x)
        {
            if (x == 0)
            {
                return rowTop.IsEmpty ? 32 : rowTop[0];
            }

            if (rowTop.IsEmpty)
            {
                return row[x - 1];
            }

            return (rowTop[x] + row[x - 1] + 1) / 2;
        }

        private static int NumNonZero8x8ExceptDC(ReadOnlySpan<int> block, Span<int> nzerosPos)
        {
            int nzeros = 0;

            for (nuint k = 1; k < FrameDimensions.kDCTBlockSize; k++)
            {
                if (block[(int)k] != 0)
                {
                    nzeros++;
                }
            }

            nzerosPos[0] = nzeros;

            return nzeros;
        }

        private static int NumNonZeroExceptLLF(
            (nuint Cx, nuint Cy, nuint CoveredBlocks, nuint Log2CoveredBlocks, nuint Stride) geom,
            AcStrategy acs,
            ReadOnlySpan<int> block,
            Span<int> nzerosPos)
        {
            int nzeros     = 0;
            nuint rowWidth = geom.Cx * FrameDimensions.kBlockDim;

            for (nuint y = 0; y < geom.Cy * FrameDimensions.kBlockDim; y++)
            {
                nuint startX          = y < geom.Cy ? geom.Cx : 0;
                ReadOnlySpan<int> row = block.Slice((int)(y * rowWidth));

                for (nuint x = startX; x < rowWidth; x++)
                {
                    if (row[(int)x] != 0)
                    {
                        nzeros++;
                    }
                }
            }

            int shiftedNzeros = (int)(((nuint)nzeros + geom.CoveredBlocks - 1) >> (int)geom.Log2CoveredBlocks);
            nuint stride      = geom.Stride;

            for (nuint y = 0; y < acs.CoveredBlocksY(); y++)
            {
                for (nuint x = 0; x < acs.CoveredBlocksX(); x++)
                {
                    nzerosPos[(int)(x + (y * stride))] = shiftedNzeros;
                }
            }

            return nzeros;
        }
    }
}
