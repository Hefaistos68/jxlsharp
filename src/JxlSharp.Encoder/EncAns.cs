// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    public sealed partial class EntropyEncodingData
    {
        /// <summary>
        /// Builds and stores ANS or Prefix encoding tables for a single <paramref name="histogram"/>.
        /// Ported from <c>jxl::EntropyEncodingData::BuildAndStoreANSEncodingData</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="ansHistogramStrategy">ANS histogram quantization strategy.</param>
        /// <param name="histogram">Source symbol frequency histogram.</param>
        /// <param name="writer">Destination bit writer, or <c>null</c> to only estimate cost.</param>
        /// <returns>Estimated or actual bit cost on success.</returns>
        public StatusOr<nuint> BuildAndStoreANSEncodingData(
            JxlMemoryManager? memoryManager,
            HistogramParams.ANSHistogramStrategy ansHistogramStrategy,
            Histogram histogram,
            BitWriter? writer)
        {
            _ = memoryManager;
            ANSEncSymbolInfo[] info = EncodingInfo[EncodingInfo.Count - 1];
            nuint size              = histogram.AlphabetSize();

            if (UsePrefixCode)
            {
                return BuildPrefixEncodingData(histogram, size, info, writer);
            }

            StatusOr<EncAns.ANSEncodingHistogram> normalizedOr =
                EncAns.ANSEncodingHistogram.ComputeBest(histogram, ansHistogramStrategy);

            if (!normalizedOr.IsOk)
            {
                return normalizedOr.Status;
            }

            EncAns.ANSEncodingHistogram normalized = normalizedOr.Value;
            AliasTable.Entry[] aliasTable          = new AliasTable.Entry[AnsParams.AnsMaxAlphabetSize];

            Status aliasStatus = AliasTable.InitAliasTable(
                normalized.Counts,
                AnsParams.AnsLogTabSize,
                LogAlphaSize,
                aliasTable);

            if (!aliasStatus)
            {
                return aliasStatus;
            }

            normalized.ANSBuildInfoTable(aliasTable, LogAlphaSize, info);

            if (writer != null)
            {
                Status encodeStatus = normalized.Encode(writer);

                if (!encodeStatus)
                {
                    return encodeStatus;
                }
            }

            return (nuint)MathF.Ceiling(normalized.Cost);
        }

        /// <summary>
        /// Chooses optimal <see cref="HybridUintConfig"/> entries for each clustered histogram.
        /// Ported from <c>jxl::EntropyEncodingData::ChooseUintConfigs</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="histParams">Histogram parameters.</param>
        /// <param name="tokens">Token streams.</param>
        /// <param name="clusteredHistograms">Clustered histograms to update.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status ChooseUintConfigs(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            IReadOnlyList<List<Token>> tokens,
            List<Histogram> clusteredHistograms)
        {
            _ = memoryManager;
            InitDefaultLogAlphaSize(histParams);

            if (EncAns.AnsFuzzerFriendly)
            {
                UintConfig.Clear();
                UintConfig.Add(new HybridUintConfig(7, 0, 0));

                return true;
            }

            UintConfig.Clear();
            HybridUintConfig defaultConfig = histParams.UintConfig();

            for (int i = 0; i < clusteredHistograms.Count; i++)
            {
                UintConfig.Add(defaultConfig);
            }

            if ((histParams.UintMethod != HistogramParams.HybridUintMethod.kBest &&
                 histParams.UintMethod != HistogramParams.HybridUintMethod.kFast) ||
                histParams.StreamingMode)
            {
                return true;
            }

            HybridUintConfig[] configs = GetCandidateUintConfigs(histParams.UintMethod);

            return SearchBestUintConfigs(tokens, clusteredHistograms, configs);
        }

        /// <summary>
        /// Clusters histograms, selects <see cref="HybridUintConfig"/>s, and encodes the resulting tables.
        /// Ported from <c>jxl::EntropyEncodingData::BuildAndStoreEntropyCodes</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="histParams">Histogram parameters.</param>
        /// <param name="tokens">Input token streams.</param>
        /// <param name="builder">Unclustered per-context histograms.</param>
        /// <param name="writer">Optional destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns>Total bit cost on success.</returns>
        public StatusOr<nuint> BuildAndStoreEntropyCodes(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            IReadOnlyList<List<Token>> tokens,
            IReadOnlyList<Histogram> builder,
            BitWriter? writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            nuint prevHistograms                = (nuint)EncodingInfo.Count;
            List<Histogram> clusteredHistograms = BuildInitialClusteredHistograms();
            nuint contextOffset                 = (nuint)ContextMap.Length;
            Array.Resize(ref ContextMap, (int)(contextOffset + (nuint)builder.Count));

            Status clusterStatus = ClusterAndWriteContextMap(
                histParams,
                builder,
                contextOffset,
                clusteredHistograms,
                writer,
                layer,
                auxOut);

            if (!clusterStatus)
            {
                return clusterStatus;
            }

            RecordClusteredEntropy(prevHistograms, clusteredHistograms, layer, auxOut);

            Status uintStatus = ChooseUintConfigs(memoryManager, histParams, tokens, clusteredHistograms);

            if (!uintStatus)
            {
                return uintStatus;
            }

            nuint cost = WriteEntropyHeaderAndUintConfigs(clusteredHistograms, writer);

            return EncodeClusteredHistograms(
                memoryManager,
                histParams,
                prevHistograms,
                clusteredHistograms,
                cost,
                writer,
                (layer, auxOut));
        }

        private static StatusOr<nuint> BuildPrefixEncodingData(
            Histogram histogram,
            nuint size,
            ANSEncSymbolInfo[] info,
            BitWriter? writer)
        {
            if (size <= 1)
            {
                return (nuint)0;
            }

            uint[] histo = new uint[size];

            for (nuint i = 0; i < size; i++)
            {
                int c = histogram.Counts[(int)i];

                if (c < 0)
                {
                    return Status.Failure("Negative histogram count");
                }

                histo[i] = (uint)c;
            }

            byte[] depths = new byte[size];
            ushort[] bits = new ushort[size];
            nuint cost;

            if (writer == null)
            {
                BitWriter tmpWriter = new BitWriter();
                Status tmpStatus    = tmpWriter.WithMaxBits(
                    (8 * size) + 8,
                    LayerType.Header,
                    null,
                    () => EncHuffman.BuildAndStoreHuffmanTree(histo, size, depths, bits, tmpWriter));

                if (!tmpStatus)
                {
                    return tmpStatus;
                }

                cost = tmpWriter.BitsWritten;
            }
            else
            {
                nuint start      = writer.BitsWritten;
                Status hufStatus = EncHuffman.BuildAndStoreHuffmanTree(histo, size, depths, bits, writer);

                if (!hufStatus)
                {
                    return hufStatus;
                }

                cost = writer.BitsWritten - start;
            }

            for (nuint i = 0; i < size; i++)
            {
                info[i].Bits  = depths[i] == 0 ? (ushort)0 : bits[i];
                info[i].Depth = depths[i];
                cost         += histo[i] * (nuint)info[i].Depth;
            }

            return cost;
        }

        private void InitDefaultLogAlphaSize(HistogramParams histParams)
        {
            if (UsePrefixCode)
            {
                LogAlphaSize = AnsParams.PrefixMaxBits;
            }
            else if (histParams.StreamingMode || Lz77.Enabled)
            {
                LogAlphaSize = 8;
            }
            else
            {
                LogAlphaSize = 7;
            }
        }

        private static HybridUintConfig[] GetCandidateUintConfigs(HistogramParams.HybridUintMethod method)
        {
            if (method == HistogramParams.HybridUintMethod.kBest)
            {
                return new[]
                {
                    new HybridUintConfig(4, 2, 0),
                    new HybridUintConfig(4, 1, 0),
                    new HybridUintConfig(4, 2, 1),
                    new HybridUintConfig(4, 2, 2),
                    new HybridUintConfig(4, 1, 2),
                    new HybridUintConfig(5, 2, 0),
                    new HybridUintConfig(5, 1, 0),
                    new HybridUintConfig(5, 2, 1),
                    new HybridUintConfig(5, 2, 2),
                    new HybridUintConfig(5, 1, 2),
                    new HybridUintConfig(3, 2, 0),
                    new HybridUintConfig(3, 1, 0),
                    new HybridUintConfig(3, 2, 1),
                    new HybridUintConfig(3, 1, 2),
                    new HybridUintConfig(4, 1, 3),
                    new HybridUintConfig(5, 1, 4),
                    new HybridUintConfig(5, 2, 3),
                    new HybridUintConfig(6, 1, 5),
                    new HybridUintConfig(6, 2, 4),
                    new HybridUintConfig(6, 0, 0),
                    new HybridUintConfig(0, 0, 0),
                    new HybridUintConfig(2, 0, 1),
                    new HybridUintConfig(7, 0, 0),
                    new HybridUintConfig(8, 0, 0),
                    new HybridUintConfig(9, 0, 0),
                    new HybridUintConfig(10, 0, 0),
                    new HybridUintConfig(11, 0, 0),
                    new HybridUintConfig(12, 0, 0),
                };
            }

            return new[]
            {
                new HybridUintConfig(4, 2, 0),
                new HybridUintConfig(4, 1, 2),
                new HybridUintConfig(0, 0, 0),
                new HybridUintConfig(2, 0, 1),
            };
        }

        private Status SearchBestUintConfigs(
            IReadOnlyList<List<Token>> tokens,
            List<Histogram> clusteredHistograms,
            HybridUintConfig[] configs)
        {
            int numHisto               = clusteredHistograms.Count;
            nuint[] histoVolume        = new nuint[2 * numHisto];
            nuint[] histoOffset        = new nuint[(2 * numHisto) + 1];
            uint[] maxValuePerHisto    = new uint[2 * numHisto];
            uint[] transposed          = BuildTransposedTokenValues(
                tokens,
                numHisto,
                histoVolume,
                histoOffset,
                maxValuePerHisto,
                out nuint maxHistoVolume);

            uint[] tmpTokens = new uint[maxHistoVolume + 16];

            for (int h = 0; h < numHisto; h++)
            {
                Status status = SelectBestConfigForHistogram(
                    h,
                    configs,
                    maxValuePerHisto[h],
                    transposed.AsSpan((int)histoOffset[h], (int)histoVolume[h]),
                    tmpTokens,
                    clusteredHistograms);

                if (!status)
                {
                    return status;
                }
            }

            return FinalizeLz77AndLogAlphaSize(
                numHisto,
                histoVolume,
                histoOffset,
                maxValuePerHisto,
                transposed,
                tmpTokens,
                clusteredHistograms);
        }

        private uint[] BuildTransposedTokenValues(
            IReadOnlyList<List<Token>> tokens,
            int numHisto,
            nuint[] histoVolume,
            nuint[] histoOffset,
            uint[] maxValuePerHisto,
            out nuint maxHistoVolume)
        {
            for (int s = 0; s < tokens.Count; s++)
            {
                List<Token> stream = tokens[s];

                for (int i = 0; i < stream.Count; i++)
                {
                    Token token = stream[i];
                    int histo   = ContextMap[token.Context] + (token.IsLz77Length ? numHisto : 0);
                    histoVolume[histo]++;
                }
            }

            maxHistoVolume = 0;

            for (int h = 0; h < 2 * numHisto; h++)
            {
                if (histoVolume[h] > maxHistoVolume)
                {
                    maxHistoVolume = histoVolume[h];
                }

                histoOffset[h + 1] = histoOffset[h] + histoVolume[h];
            }

            uint[] transposed  = new uint[histoOffset[2 * numHisto] + 16];
            nuint[] nextOffset = (nuint[])histoOffset.Clone();

            for (int s = 0; s < tokens.Count; s++)
            {
                List<Token> stream = tokens[s];

                for (int i = 0; i < stream.Count; i++)
                {
                    Token token                        = stream[i];
                    int histo                          = ContextMap[token.Context] + (token.IsLz77Length ? numHisto : 0);
                    transposed[nextOffset[histo]++]    = token.Value;
                }
            }

            for (int h = 0; h < 2 * numHisto; h++)
            {
                maxValuePerHisto[h] = MaxSpanValue(transposed.AsSpan((int)histoOffset[h], (int)histoVolume[h]));
            }

            return transposed;
        }

        private static uint MaxSpanValue(ReadOnlySpan<uint> values)
        {
            uint max = 0;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        private Status SelectBestConfigForHistogram(
            int h,
            HybridUintConfig[] configs,
            uint maxV,
            ReadOnlySpan<uint> data,
            Span<uint> tmpTokens,
            List<Histogram> clusteredHistograms)
        {
            float bestCost = float.MaxValue;

            for (int c = 0; c < configs.Length; c++)
            {
                HybridUintConfig cfg = configs[c];
                cfg.Encode(maxV, out uint tok, out _, out _);
                tok |= cfg.LsbMask();

                if (tok >= AnsParams.AnsMaxAlphabetSize || (Lz77.Enabled && tok >= Lz77.MinSymbol))
                {
                    continue;
                }

                Histogram histo = new Histogram();
                histo.EnsureCapacity(tok + 1);
                uint extraBits = EncAns.EstimateTokenCost(data, cfg, tmpTokens);

                for (int i = 0; i < data.Length; i++)
                {
                    histo.FastAdd(tmpTokens[i]);
                }

                histo.Condition();
                StatusOr<float> popCostOr = histo.ANSPopulationCost();

                if (!popCostOr.IsOk)
                {
                    return popCostOr.Status;
                }

                float cost  = popCostOr.Value + extraBits;
                cost       += Bits.CeilLog2Nonzero(cfg.SplitExponent + 1);
                cost       += Bits.CeilLog2Nonzero(cfg.SplitExponent - cfg.MsbInToken + 1);

                if (cost < bestCost)
                {
                    UintConfig[h] = cfg;
                    bestCost      = cost;
                    clusteredHistograms[h].Swap(histo);
                }
            }

            return true;
        }

        private Status FinalizeLz77AndLogAlphaSize(
            int numHisto,
            nuint[] histoVolume,
            nuint[] histoOffset,
            uint[] maxValuePerHisto,
            uint[] transposed,
            Span<uint> tmpTokens,
            List<Histogram> clusteredHistograms)
        {
            nuint maxTok = 0;

            for (int h = 0; h < numHisto; h++)
            {
                Histogram histo = clusteredHistograms[h];
                maxTok          = Math.Max(maxTok, histo.MaxSymbol());
                nuint len       = histoVolume[numHisto + h];

                if (len == 0)
                {
                    continue;
                }

                uint maxHistoTok = maxValuePerHisto[numHisto + h];
                Lz77.LengthUintConfig.Encode(maxHistoTok, out uint tok, out _, out _);
                tok |= Lz77.LengthUintConfig.LsbMask();
                tok += Lz77.MinSymbol;
                histo.EnsureCapacity(tok + 1);

                ReadOnlySpan<uint> data = transposed.AsSpan((int)histoOffset[numHisto + h], (int)len);
                _                       = EncAns.EstimateTokenCost(data, Lz77.LengthUintConfig, tmpTokens);

                for (int i = 0; i < (int)len; i++)
                {
                    histo.FastAdd(tmpTokens[i] + Lz77.MinSymbol);
                }

                histo.Condition();
                maxTok = Math.Max(maxTok, histo.MaxSymbol());
            }

            nuint logSize = 5;

            while (maxTok >= (1u << (int)logSize))
            {
                logSize++;
            }

            nuint maxLogAlphaSize = UsePrefixCode ? (nuint)AnsParams.PrefixMaxBits : 8u;

            if (logSize > maxLogAlphaSize)
            {
                return Status.Failure("log_alpha_size exceeds maximum");
            }

            LogAlphaSize = UsePrefixCode ? (nuint)AnsParams.PrefixMaxBits : logSize;

            return true;
        }

        private List<Histogram> BuildInitialClusteredHistograms()
        {
            List<Histogram> clusteredHistograms = new List<Histogram>(EncodingInfo.Count);

            for (int i = 0; i < EncodingInfo.Count; i++)
            {
                clusteredHistograms.Add(HistogramFromSymbolInfo(EncodingInfo[i], UsePrefixCode));
            }

            return clusteredHistograms;
        }

        private static Histogram HistogramFromSymbolInfo(ANSEncSymbolInfo[] encodingInfo, bool usePrefixCode)
        {
            Histogram histo = new Histogram();
            histo.EnsureCapacity((nuint)encodingInfo.Length);
            histo.TotalCount = 0;

            for (int i = 0; i < encodingInfo.Length; i++)
            {
                ANSEncSymbolInfo info = encodingInfo[i];
                int count             = usePrefixCode
                    ? (info.Depth != 0 ? (1 << (AnsParams.PrefixMaxBits - info.Depth)) : 0)
                    : info.Freq;
                histo.Counts[i]       = count;
                histo.TotalCount     += (nuint)count;
            }

            return histo;
        }

        private Status ClusterAndWriteContextMap(
            HistogramParams histParams,
            IReadOnlyList<Histogram> builder,
            nuint contextOffset,
            List<Histogram> clusteredHistograms,
            BitWriter? writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (builder.Count > 1)
            {
                if (!EncAns.AnsFuzzerFriendly)
                {
                    List<uint> histogramSymbols = new List<uint>();
                    Status clusterStatus        = EncCluster.ClusterHistograms(
                        histParams,
                        builder,
                        EncContextMap.KClustersLimit,
                        clusteredHistograms,
                        histogramSymbols);

                    if (!clusterStatus)
                    {
                        return clusterStatus;
                    }

                    for (int c = 0; c < builder.Count; c++)
                    {
                        ContextMap[(int)contextOffset + c] = (byte)histogramSymbols[c];
                    }
                }
                else
                {
                    PopulateFuzzerFriendlyCluster(builder, clusteredHistograms);
                }

                if (writer != null)
                {
                    return EncContextMap.EncodeContextMap(
                        ContextMap,
                        (nuint)clusteredHistograms.Count,
                        writer,
                        layer,
                        auxOut);
                }

                return true;
            }

            if (EncodingInfo.Count != 0)
            {
                return Status.Failure("Expected empty encoding_info for single histogram");
            }

            clusteredHistograms.Add(builder[0].Clone());

            return true;
        }

        private void PopulateFuzzerFriendlyCluster(
            IReadOnlyList<Histogram> builder,
            List<Histogram> clusteredHistograms)
        {
            Array.Fill(ContextMap, (byte)0);
            nuint maxSymbol = 0;

            for (int i = 0; i < builder.Count; i++)
            {
                maxSymbol = Math.Max((nuint)builder[i].Counts.Count, maxSymbol);
            }

            nuint numSymbols = 1u << Bits.CeilLog2Nonzero(maxSymbol + 1);
            clusteredHistograms.Clear();
            Histogram h0 = new Histogram();

            for (nuint i = 0; i < numSymbols; i++)
            {
                h0.Add(i);
            }

            clusteredHistograms.Add(h0);
        }

        private static void RecordClusteredEntropy(
            nuint prevHistograms,
            List<Histogram> clusteredHistograms,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (auxOut == null)
            {
                return;
            }

            for (int i = (int)prevHistograms; i < clusteredHistograms.Count; i++)
            {
                auxOut.Layer(layer).ClusteredEntropy += clusteredHistograms[i].ShannonEntropy();
            }
        }

        private nuint WriteEntropyHeaderAndUintConfigs(
            List<Histogram> clusteredHistograms,
            BitWriter? writer)
        {
            SizeWriter sizeWriter = default;
            nuint cost            = UsePrefixCode ? 1u : 3u;

            if (writer != null)
            {
                writer.Write(1, UsePrefixCode ? 1UL : 0UL);

                if (!UsePrefixCode)
                {
                    writer.Write(2, LogAlphaSize - 5);
                }

                EncAns.EncodeUintConfigs(UintConfig, writer, LogAlphaSize);
            }
            else
            {
                EncAns.EncodeUintConfigs(UintConfig, ref sizeWriter, LogAlphaSize);
            }

            if (UsePrefixCode)
            {
                for (int i = 0; i < clusteredHistograms.Count; i++)
                {
                    nuint alphabetSize = Math.Max((nuint)1, clusteredHistograms[i].AlphabetSize());

                    if (writer != null)
                    {
                        EncAns.StoreVarLenUint16(alphabetSize - 1, writer);
                    }
                    else
                    {
                        EncAns.StoreVarLenUint16(alphabetSize - 1, ref sizeWriter);
                    }
                }
            }

            return cost + sizeWriter.Size;
        }

        private StatusOr<nuint> EncodeClusteredHistograms(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            nuint prevHistograms,
            List<Histogram> clusteredHistograms,
            nuint initialCost,
            BitWriter? writer,
            (LayerType Layer, AuxOut? AuxOut) layerAux)
        {
            nuint cost = initialCost;

            for (int c = (int)prevHistograms; c < clusteredHistograms.Count; c++)
            {
                nuint alphabetSize         = clusteredHistograms[c].AlphabetSize();
                ANSEncSymbolInfo[] symInfo = new ANSEncSymbolInfo[Math.Max((nuint)1, alphabetSize)];

                for (int s = 0; s < symInfo.Length; s++)
                {
                    symInfo[s] = new ANSEncSymbolInfo();
                }

                EncodingInfo.Add(symInfo);
                BitWriter? histoWriter = writer;

                if (histParams.StreamingMode)
                {
                    BitWriter streamHistoWriter = new BitWriter(memoryManager);
                    EncodedHistograms.Add(streamHistoWriter);
                    histoWriter = streamHistoWriter;
                }

                Status stepStatus = EncodeSingleClusteredHistogram(
                    memoryManager,
                    histParams.AnsHistogramStrategy,
                    clusteredHistograms[c],
                    alphabetSize,
                    histoWriter,
                    layerAux,
                    ref cost);

                if (!stepStatus)
                {
                    return stepStatus;
                }

                if (histParams.StreamingMode && writer != null && histoWriter != null)
                {
                    Status appendStatus = writer.AppendUnaligned(histoWriter);

                    if (!appendStatus)
                    {
                        return appendStatus;
                    }
                }
            }

            return cost;
        }

        private Status EncodeSingleClusteredHistogram(
            JxlMemoryManager? memoryManager,
            HistogramParams.ANSHistogramStrategy strategy,
            Histogram histo,
            nuint alphabetSize,
            BitWriter? histoWriter,
            (LayerType Layer, AuxOut? AuxOut) layerAux,
            ref nuint cost)
        {
            if (histoWriter == null)
            {
                StatusOr<nuint> ansCostOr = BuildAndStoreANSEncodingData(memoryManager, strategy, histo, null);

                if (!ansCostOr.IsOk)
                {
                    return ansCostOr.Status;
                }

                cost += ansCostOr.Value;

                return true;
            }

            nuint localCost   = 0;
            Status withStatus = histoWriter.WithMaxBits(
                256 + (alphabetSize * 24),
                layerAux.Layer,
                layerAux.AuxOut,
                () =>
                {
                    StatusOr<nuint> ansCostOr = BuildAndStoreANSEncodingData(
                        memoryManager,
                        strategy,
                        histo,
                        histoWriter);

                    if (!ansCostOr.IsOk)
                    {
                        return ansCostOr.Status;
                    }

                    localCost = ansCostOr.Value;

                    return true;
                },
                finishedHistogram: true);

            cost += localCost;

            return withStatus;
        }
    }

    /// <summary>
    /// ANS and Prefix entropy coding functions, histogram normalization, and token serialization.
    /// Ported from <c>lib/jxl/enc_ans.h</c>, <c>lib/jxl/enc_ans.cc</c>, and <c>lib/jxl/enc_ans_simd.cc</c>.
    /// </summary>
    public static class EncAns
    {
        private const int KMaxNumSymbolsForSmallCode = 2;

        internal static bool AnsFuzzerFriendly;

        /// <summary>
        /// Sets the fuzzer-friendly ANS mode.
        /// Ported from <c>jxl::SetANSFuzzerFriendly</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="ansFuzzerFriendly">Whether fuzzer-friendly mode is enabled.</param>
        public static void SetANSFuzzerFriendly(bool ansFuzzerFriendly)
        {
            AnsFuzzerFriendly = ansFuzzerFriendly;
        }

        /// <summary>
        /// Computes the ANS population cost (header + data bits) for a histogram with <paramref name="data"/> and <paramref name="totalCount"/>.
        /// Ported from <c>jxl::Histogram::ANSPopulationCost</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="data">Per-symbol counts.</param>
        /// <param name="totalCount">Sum of counts.</param>
        /// <returns>Estimated bit cost.</returns>
        public static StatusOr<float> ANSPopulationCost(IReadOnlyList<int> data, nuint totalCount)
        {
            if (data.Count > AnsParams.AnsMaxAlphabetSize)
            {
                return float.MaxValue;
            }

            Histogram temp = new Histogram
            {
                Counts     = data is List<int> list ? list : new List<int>(data),
                TotalCount = totalCount,
            };

            StatusOr<ANSEncodingHistogram> normOr = ANSEncodingHistogram.ComputeBest(
                temp,
                HistogramParams.ANSHistogramStrategy.kFast);

            if (!normOr.IsOk)
            {
                return normOr.Status;
            }

            return normOr.Value.Cost;
        }

        /// <summary>
        /// Encodes a single <see cref="HybridUintConfig"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeUintConfig</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="uintConfig">Configuration to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfig(HybridUintConfig uintConfig, BitWriter writer, nuint logAlphaSize)
        {
            writer.Write((nuint)Bits.CeilLog2Nonzero(logAlphaSize + 1), uintConfig.SplitExponent);

            if (uintConfig.SplitExponent == logAlphaSize)
            {
                return;
            }

            nuint nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent + 1);
            writer.Write(nbits, uintConfig.MsbInToken);
            nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent - uintConfig.MsbInToken + 1);
            writer.Write(nbits, uintConfig.LsbInToken);
        }

        /// <summary>
        /// Accumulates the bit size of a single <see cref="HybridUintConfig"/> into <paramref name="writer"/>.
        /// </summary>
        /// <param name="uintConfig">Configuration to size.</param>
        /// <param name="writer">Size accumulator.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfig(HybridUintConfig uintConfig, ref SizeWriter writer, nuint logAlphaSize)
        {
            writer.Write((nuint)Bits.CeilLog2Nonzero(logAlphaSize + 1), uintConfig.SplitExponent);

            if (uintConfig.SplitExponent == logAlphaSize)
            {
                return;
            }

            nuint nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent + 1);
            writer.Write(nbits, uintConfig.MsbInToken);
            nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent - uintConfig.MsbInToken + 1);
            writer.Write(nbits, uintConfig.LsbInToken);
        }

        /// <summary>
        /// Encodes a list of <see cref="HybridUintConfig"/> entries into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeUintConfigs</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="uintConfig">Configurations to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfigs(
            IReadOnlyList<HybridUintConfig> uintConfig,
            BitWriter writer,
            nuint logAlphaSize)
        {
            for (int i = 0; i < uintConfig.Count; i++)
            {
                EncodeUintConfig(uintConfig[i], writer, logAlphaSize);
            }
        }

        /// <summary>
        /// Accumulates the bit size of a list of <see cref="HybridUintConfig"/> entries into <paramref name="writer"/>.
        /// </summary>
        /// <param name="uintConfig">Configurations to size.</param>
        /// <param name="writer">Size accumulator.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfigs(
            IReadOnlyList<HybridUintConfig> uintConfig,
            ref SizeWriter writer,
            nuint logAlphaSize)
        {
            for (int i = 0; i < uintConfig.Count; i++)
            {
                EncodeUintConfig(uintConfig[i], ref writer, logAlphaSize);
            }
        }

        /// <summary>
        /// Encodes pre-built streaming histograms from <paramref name="codes"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeHistograms</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="codes">Compiled entropy encoding data.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodeHistograms(
            EntropyEncodingData codes,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            return writer.WithMaxBits(
                128 + (EncContextMap.KClustersLimit * 136),
                layer,
                auxOut,
                () =>
                {
                    if (!Bundle.Write(codes.Lz77, writer, layer, auxOut))
                    {
                        return false;
                    }

                    if (codes.Lz77.Enabled)
                    {
                        EncodeUintConfig(codes.Lz77.LengthUintConfig, writer, 8);
                    }

                    if (!EncContextMap.EncodeContextMap(
                            codes.ContextMap,
                            (nuint)codes.EncodingInfo.Count,
                            writer,
                            layer,
                            auxOut))
                    {
                        return false;
                    }

                    writer.Write(1, codes.UsePrefixCode ? 1UL : 0UL);
                    nuint logAlphaSize = 8;

                    if (codes.UsePrefixCode)
                    {
                        logAlphaSize = AnsParams.PrefixMaxBits;
                    }
                    else
                    {
                        writer.Write(2, logAlphaSize - 5);
                    }

                    EncodeUintConfigs(codes.UintConfig, writer, logAlphaSize);

                    if (codes.UsePrefixCode)
                    {
                        for (int i = 0; i < codes.EncodingInfo.Count; i++)
                        {
                            StoreVarLenUint16((nuint)codes.EncodingInfo[i].Length - 1, writer);
                        }
                    }

                    for (int i = 0; i < codes.EncodedHistograms.Count; i++)
                    {
                        if (!writer.AppendUnaligned(codes.EncodedHistograms[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                },
                finishedHistogram: true);
        }

        /// <summary>
        /// Applies optional LZ77, builds and clusters histograms, and encodes them into <paramref name="writer"/>.
        /// Ported from <c>jxl::BuildAndEncodeHistograms</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="histParams">Histogram parameters.</param>
        /// <param name="numContexts">Number of input contexts.</param>
        /// <param name="tokens">Token streams (updated in place if LZ77 is enabled).</param>
        /// <param name="codes">Destination <see cref="EntropyEncodingData"/>.</param>
        /// <param name="writer">Optional destination bit writer.</param>
        /// <param name="layerAux">Statistics layer and optional <see cref="AuxOut"/>.</param>
        /// <returns>Total bit cost on success.</returns>
        public static StatusOr<nuint> BuildAndEncodeHistograms(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            nuint numContexts,
            List<List<Token>> tokens,
            EntropyEncodingData codes,
            BitWriter? writer,
            (LayerType Layer, AuxOut? AuxOut) layerAux = default)
        {
            codes.Lz77.NonserializedDistanceContext = numContexts;
            codes.Lz77.MinSymbol                    = histParams.ForceHuffman ? 512u : 224u;
            List<List<Token>> tokensLz77            = EncLz77.ApplyLZ77(histParams, numContexts, tokens, codes.Lz77);

            if (tokensLz77.Count > 0)
            {
                codes.Lz77.Enabled = true;
            }

            if (AnsFuzzerFriendly)
            {
                codes.Lz77.LengthUintConfig = new HybridUintConfig(10, 0, 0);
                codes.Lz77.MinSymbol        = 2048;
            }

            return ExecuteBuildAndEncodeHistograms(
                memoryManager,
                histParams,
                numContexts,
                tokens,
                tokensLz77,
                codes,
                (writer, layerAux.Layer, layerAux.AuxOut));
        }

        /// <summary>
        /// Writes <paramref name="tokens"/> into <paramref name="writer"/> using pre-built <paramref name="codes"/>.
        /// Ported from <c>jxl::WriteTokens</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="tokens">Tokens to encode.</param>
        /// <param name="codes">Entropy encoding tables.</param>
        /// <param name="contextOffset">Base context offset in <see cref="EntropyEncodingData.ContextMap"/>.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <returns>Number of extra bits written.</returns>
        public static nuint WriteTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer)
        {
            if (codes.UsePrefixCode)
            {
                return WritePrefixTokens(tokens, codes, contextOffset, writer);
            }

            return WriteAnsTokens(tokens, codes, contextOffset, writer);
        }

        /// <summary>
        /// Reserves space in <paramref name="writer"/> and writes <paramref name="tokens"/> using <paramref name="codes"/>.
        /// Ported from <c>jxl::WriteTokens</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="tokens">Tokens to encode.</param>
        /// <param name="codes">Entropy encoding tables.</param>
        /// <param name="contextOffset">Base context offset in <see cref="EntropyEncodingData.ContextMap"/>.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            return writer.WithMaxBits(
                (46 * (nuint)tokens.Count) + (32 * 1024 * 4),
                layer,
                auxOut,
                () =>
                {
                    nuint numExtraBits = WriteTokens(tokens, codes, contextOffset, writer);

                    if (auxOut != null)
                    {
                        auxOut.Layer(layer).ExtraBits += numExtraBits;
                    }

                    return true;
                });
        }

        /// <summary>
        /// Encodes <paramref name="values"/> with <paramref name="cfg"/>, writing token indices to <paramref name="outTokens"/> and returning total extra bits.
        /// Ported from <c>jxl::EstimateTokenCost</c> in <c>lib/jxl/enc_ans_simd.cc</c>.
        /// </summary>
        /// <param name="values">Input values.</param>
        /// <param name="cfg">Hybrid integer config.</param>
        /// <param name="outTokens">Output token buffer.</param>
        /// <returns>Total extra bits.</returns>
        public static uint EstimateTokenCost(
            ReadOnlySpan<uint> values,
            HybridUintConfig cfg,
            Span<uint> outTokens)
        {
            uint extraBits = 0;

            for (int i = 0; i < values.Length; i++)
            {
                cfg.Encode(values[i], out uint tok, out uint nbits, out _);
                extraBits    += nbits;
                outTokens[i]  = tok;
            }

            return extraBits;
        }

        internal static void StoreVarLenUint8(nuint n, BitWriter writer)
        {
            Debug.Assert(n <= 255);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero(n);
                writer.Write(3, nbits);
                writer.Write(nbits, n - (1UL << (int)nbits));
            }
        }

        internal static void StoreVarLenUint8(nuint n, ref SizeWriter writer)
        {
            Debug.Assert(n <= 255);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero(n);
                writer.Write(3, nbits);
                writer.Write(nbits, n - (1UL << (int)nbits));
            }
        }

        internal static void StoreVarLenUint16(nuint n, BitWriter writer)
        {
            Debug.Assert(n <= 65535);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero(n);
                writer.Write(4, nbits);
                writer.Write(nbits, n - (1UL << (int)nbits));
            }
        }

        internal static void StoreVarLenUint16(nuint n, ref SizeWriter writer)
        {
            Debug.Assert(n <= 65535);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero(n);
                writer.Write(4, nbits);
                writer.Write(nbits, n - (1UL << (int)nbits));
            }
        }

        private static StatusOr<nuint> ExecuteBuildAndEncodeHistograms(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            nuint numContexts,
            List<List<Token>> tokens,
            List<List<Token>> tokensLz77,
            EntropyEncodingData codes,
            (BitWriter? Writer, LayerType Layer, AuxOut? AuxOut) io)
        {
            nuint cost        = 0;
            nuint maxContexts = Math.Min(numContexts, EncContextMap.KClustersLimit);

            Status Body()
            {
                Status headerStatus = WriteOrEstimateLz77Header(
                    codes,
                    tokens,
                    tokensLz77,
                    io,
                    ref numContexts,
                    ref cost);

                if (!headerStatus)
                {
                    return headerStatus;
                }

                List<Histogram> builder = BuildContextHistograms(
                    histParams,
                    numContexts,
                    tokens,
                    codes,
                    out nuint totalTokens);

                if (histParams.InitializeGlobalState)
                {
                    codes.UsePrefixCode = ShouldUsePrefixCode(histParams, builder, totalTokens);
                }

                if (histParams.AddFixedHistograms)
                {
                    Status fixedStatus = AddFixedFlatHistogram(memoryManager, histParams, codes);

                    if (!fixedStatus)
                    {
                        return fixedStatus;
                    }
                }

                StatusOr<nuint> entropyBitsOr = codes.BuildAndStoreEntropyCodes(
                    memoryManager,
                    histParams,
                    tokens,
                    builder,
                    io.Writer,
                    io.Layer,
                    io.AuxOut);

                if (!entropyBitsOr.IsOk)
                {
                    return entropyBitsOr.Status;
                }

                cost += entropyBitsOr.Value;

                return true;
            }

            Status execStatus = io.Writer != null
                ? io.Writer.WithMaxBits(
                    128 + (numContexts * 40) + (maxContexts * 96),
                    io.Layer,
                    io.AuxOut,
                    Body,
                    finishedHistogram: true)
                : Body();

            if (!execStatus)
            {
                return execStatus;
            }

            if (io.AuxOut != null)
            {
                io.AuxOut.Layer(io.Layer).NumClusteredHistograms += (nuint)codes.EncodingInfo.Count;
            }

            return cost;
        }

        private static Status WriteOrEstimateLz77Header(
            EntropyEncodingData codes,
            List<List<Token>> tokens,
            List<List<Token>> tokensLz77,
            (BitWriter? Writer, LayerType Layer, AuxOut? AuxOut) io,
            ref nuint numContexts,
            ref nuint cost)
        {
            if (io.Writer != null)
            {
                if (!Bundle.Write(codes.Lz77, io.Writer, io.Layer, io.AuxOut))
                {
                    return false;
                }
            }
            else
            {
                if (!Bundle.CanEncode(codes.Lz77, out _, out nuint bits))
                {
                    return false;
                }

                cost += bits;
            }

            if (codes.Lz77.Enabled)
            {
                if (io.Writer != null)
                {
                    nuint b = io.Writer.BitsWritten;
                    EncodeUintConfig(codes.Lz77.LengthUintConfig, io.Writer, 8);
                    cost += io.Writer.BitsWritten - b;
                }
                else
                {
                    SizeWriter sizeWriter = default;
                    EncodeUintConfig(codes.Lz77.LengthUintConfig, ref sizeWriter, 8);
                    cost += sizeWriter.Size;
                }

                numContexts += 1;
                tokens.Clear();
                tokens.AddRange(tokensLz77);
            }

            return true;
        }

        private static List<Histogram> BuildContextHistograms(
            HistogramParams histParams,
            nuint numContexts,
            List<List<Token>> tokens,
            EntropyEncodingData codes,
            out nuint totalTokens)
        {
            totalTokens                  = 0;
            List<Histogram> builder      = new List<Histogram>((int)numContexts);
            HybridUintConfig uintConfig  = AnsFuzzerFriendly
                ? new HybridUintConfig(10, 0, 0)
                : histParams.UintConfig();

            for (nuint i = 0; i < numContexts; i++)
            {
                builder.Add(new Histogram());
            }

            for (int s = 0; s < tokens.Count; s++)
            {
                List<Token> stream = tokens[s];

                for (int i = 0; i < stream.Count; i++)
                {
                    Token token          = stream[i];
                    totalTokens++;
                    HybridUintConfig cfg = codes.Lz77.Enabled && token.IsLz77Length
                        ? codes.Lz77.LengthUintConfig
                        : uintConfig;
                    cfg.Encode(token.Value, out uint tok, out _, out _);

                    if (codes.Lz77.Enabled && token.IsLz77Length)
                    {
                        tok += codes.Lz77.MinSymbol;
                    }

                    int ctx = numContexts == 1 ? 0 : (int)token.Context;
                    builder[ctx].Add(tok);
                }
            }

            if (histParams.AddMissingSymbols)
            {
                for (int c = 0; c < (int)numContexts; c++)
                {
                    for (uint symbol = 0; symbol < AnsParams.AnsMaxAlphabetSize; symbol++)
                    {
                        builder[c].Add(symbol);
                    }
                }
            }

            return builder;
        }

        private static bool ShouldUsePrefixCode(
            HistogramParams histParams,
            List<Histogram> builder,
            nuint totalTokens)
        {
            if (histParams.ForceHuffman ||
                totalTokens < 100 ||
                histParams.Clustering == HistogramParams.ClusteringType.kFastest ||
                AnsFuzzerFriendly)
            {
                return true;
            }

            for (int i = 0; i < builder.Count; i++)
            {
                if (builder[i].ShannonEntropy() >= 1e-5f)
                {
                    return false;
                }
            }

            return true;
        }

        private static Status AddFixedFlatHistogram(
            JxlMemoryManager? memoryManager,
            HistogramParams histParams,
            EntropyEncodingData codes)
        {
            const nuint alphabetSize    = AnsParams.AnsMaxAlphabetSize;
            codes.LogAlphaSize          = 8;
            ANSEncSymbolInfo[] symInfos = new ANSEncSymbolInfo[alphabetSize];

            for (int i = 0; i < symInfos.Length; i++)
            {
                symInfos[i] = new ANSEncSymbolInfo();
            }

            codes.EncodingInfo.Add(symInfos);
            BitWriter histoWriter = new BitWriter(memoryManager);
            codes.EncodedHistograms.Add(histoWriter);

            return histoWriter.WithMaxBits(
                256 + (alphabetSize * 24),
                LayerType.Header,
                null,
                () =>
                {
                    StatusOr<nuint> ansCostOr = codes.BuildAndStoreANSEncodingData(
                        memoryManager,
                        histParams.AnsHistogramStrategy,
                        Histogram.Flat((int)alphabetSize, AnsParams.AnsTabSize),
                        histoWriter);

                    return ansCostOr.Status;
                });
        }

        private static nuint WritePrefixTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer)
        {
            nuint numExtraBits = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                Token token          = tokens[i];
                int histo            = codes.ContextMap[(int)contextOffset + (int)token.Context];
                HybridUintConfig cfg = token.IsLz77Length
                    ? codes.Lz77.LengthUintConfig
                    : codes.UintConfig[histo];
                cfg.Encode(token.Value, out uint tok, out uint nbits, out uint bits);

                if (token.IsLz77Length)
                {
                    tok += codes.Lz77.MinSymbol;
                }

                ANSEncSymbolInfo info = codes.EncodingInfo[histo][tok];
                ulong data            = info.Bits | ((ulong)bits << info.Depth);
                writer.Write(info.Depth + nbits, data);
                numExtraBits += nbits;
            }

            return numExtraBits;
        }

        private static nuint WriteAnsTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer)
        {
            nuint numExtraBits   = 0;
            List<ulong> outWords = new List<ulong>(tokens.Count);
            List<byte> outNbits  = new List<byte>(tokens.Count);
            ulong allbits        = 0;
            nuint numallbits     = 0;
            ANSCoder ans         = ANSCoder.Create();
            bool multiOrLz77     = codes.Lz77.Enabled || codes.ContextMap.Length > 1;

            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                Token token = tokens[i];
                int histo   = multiOrLz77 ? codes.ContextMap[(int)contextOffset + (int)token.Context] : 0;
                HybridUintConfig cfg = multiOrLz77 && token.IsLz77Length
                    ? codes.Lz77.LengthUintConfig
                    : codes.UintConfig[histo];
                cfg.Encode(token.Value, out uint tok, out uint nbits, out uint bits);

                if (multiOrLz77 && token.IsLz77Length)
                {
                    tok += codes.Lz77.MinSymbol;
                }

                ANSEncSymbolInfo info = codes.EncodingInfo[histo][tok];
                AddReversedBits(bits, nbits, outWords, outNbits, ref allbits, ref numallbits);
                numExtraBits += nbits;
                uint ansBits  = ans.PutSymbol(info, out byte ansNbits);
                AddReversedBits(ansBits, ansNbits, outWords, outNbits, ref allbits, ref numallbits);
            }

            writer.Write(32, ans.GetState());
            writer.Write(numallbits, allbits);

            for (int i = outWords.Count - 1; i >= 0; i--)
            {
                writer.Write(outNbits[i], outWords[i]);
            }

            return numExtraBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddReversedBits(
            ulong bits,
            nuint nbits,
            List<ulong> outWords,
            List<byte> outNbits,
            ref ulong allbits,
            ref nuint numallbits)
        {
            if (nbits == 0)
            {
                return;
            }

            if (numallbits + nbits > BitWriter.KMaxBitsPerCall)
            {
                outWords.Add(allbits);
                outNbits.Add((byte)numallbits);
                numallbits = 0;
                allbits    = 0;
            }

            allbits    = (allbits << (int)nbits) | bits;
            numallbits += nbits;
        }

        /// <summary>
        /// Normalized ANS histogram and bit-width RLE encoder.
        /// Ported from <c>jxl::ANSEncodingHistogram</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        internal sealed class ANSEncodingHistogram
        {
            private static readonly uint[] Lg2Lut = BuildLg2Lut();
            private static readonly AllowedCountsTable AllowedCounts = BuildAllowedCounts();

            private static readonly byte[] KBitWidthLengths =
            {
                5, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 6, 7, 7,
            };

            private static readonly byte[] KBitWidthSymbols =
            {
                17, 11, 15, 3, 9, 7, 4, 2, 5, 6, 0, 33, 1, 65,
            };

            private const byte KMinReps = 5;
            private const int KRepSymbol = (int)AnsParams.AnsLogTabSize + 1;

            private uint _method;
            private nuint _omitPos;
            private nuint _alphabetSize;
            private nuint _numSymbols;
            private readonly nuint[] _symbols = new nuint[KMaxNumSymbolsForSmallCode];
            private int[] _counts = Array.Empty<int>();

            public int[] Counts => _counts;

            public float Cost { get; private set; }

            public static StatusOr<ANSEncodingHistogram> ComputeBest(
                Histogram histo,
                HistogramParams.ANSHistogramStrategy strategy)
            {
                ANSEncodingHistogram result = new ANSEncodingHistogram
                {
                    _alphabetSize = histo.AlphabetSize(),
                };

                if (result._alphabetSize > AnsParams.AnsMaxAlphabetSize)
                {
                    return Status.Failure("Too many entries in an ANS histogram");
                }

                if (result._alphabetSize == 0)
                {
                    result._method     = 1;
                    result._numSymbols = 0;
                    result.Cost        = 3.0f;

                    return result;
                }

                result.InitializeFlatCandidate(histo);
                nuint symbolCount = result.CountNonZeroSymbols(histo);

                if (symbolCount == 1)
                {
                    return result.BuildSingleSymbolHistogram(histo);
                }

                Status searchStatus = result.SearchBestShift(histo, strategy);

                if (!searchStatus)
                {
                    return searchStatus;
                }

                return result;
            }

            public Status Encode(BitWriter writer)
            {
                BitWriterAdapter adapter = new BitWriterAdapter(writer);

                return EncodeCore(ref adapter);
            }

            public Status Encode(ref SizeWriter writer)
            {
                SizeWriterAdapter adapter = new SizeWriterAdapter(writer);
                Status status             = EncodeCore(ref adapter);
                writer                    = adapter.Writer;

                return status;
            }

            public void ANSBuildInfoTable(
                ReadOnlySpan<AliasTable.Entry> table,
                nuint logAlphaSize,
                ANSEncSymbolInfo[] info)
            {
                nuint limit = Math.Max((nuint)1, _alphabetSize);

                for (nuint s = 0; s < limit; s++)
                {
                    int freq     = s == _alphabetSize ? AnsParams.AnsTabSize : _counts[s];
                    info[s].Freq = (ushort)freq;
                    info[s].IFreq = freq != 0
                        ? ((1UL << ANSEncSymbolInfo.KReciprocalPrecision) + info[s].Freq - 1UL) / info[s].Freq
                        : 1UL;
                    info[s].ReverseMap = new ushort[freq];
                }

                nuint logEntrySize    = AnsParams.AnsLogTabSize - logAlphaSize;
                nuint entrySizeMinus1 = (1u << (int)logEntrySize) - 1u;

                for (nuint i = 0; i < (nuint)AnsParams.AnsTabSize; i++)
                {
                    AliasTable.Symbol s                   = AliasTable.Lookup(table, i, logEntrySize, entrySizeMinus1);
                    info[s.Value].ReverseMap[s.Offset]    = (ushort)i;
                }
            }

            private ANSEncodingHistogram Clone()
            {
                ANSEncodingHistogram copy = new ANSEncodingHistogram
                {
                    Cost          = Cost,
                    _method       = _method,
                    _omitPos      = _omitPos,
                    _alphabetSize = _alphabetSize,
                    _numSymbols   = _numSymbols,
                    _counts       = (int[])_counts.Clone(),
                };
                Array.Copy(_symbols, copy._symbols, KMaxNumSymbolsForSmallCode);

                return copy;
            }

            private void InitializeFlatCandidate(Histogram histo)
            {
                _method     = 0;
                _numSymbols = _alphabetSize;
                int[] flat  = AliasTable.CreateFlatHistogram((int)_alphabetSize, AnsParams.AnsTabSize);
                _counts     = new int[histo.Counts.Count];
                Array.Copy(flat, _counts, flat.Length);

                SizeWriter writer = default;
                _                 = Encode(ref writer);
                Cost              = writer.Size + EstimateDataBitsFlat(histo);
            }

            private nuint CountNonZeroSymbols(Histogram histo)
            {
                nuint symbolCount = 0;

                for (nuint n = 0; n < _alphabetSize; n++)
                {
                    if (histo.Counts[(int)n] > 0)
                    {
                        if (symbolCount < KMaxNumSymbolsForSmallCode)
                        {
                            _symbols[symbolCount] = n;
                        }

                        symbolCount++;
                    }
                }

                _numSymbols = symbolCount;

                return symbolCount;
            }

            private ANSEncodingHistogram BuildSingleSymbolHistogram(Histogram histo)
            {
                _method              = 1;
                _counts              = histo.Counts.ToArray();
                _counts[_symbols[0]] = AnsParams.AnsTabSize;
                SizeWriter writer    = default;
                _                    = Encode(ref writer);
                Cost                 = writer.Size;

                return this;
            }

            private Status SearchBestShift(
                Histogram histo,
                HistogramParams.ANSHistogramStrategy strategy)
            {
                switch (strategy)
                {
                    case HistogramParams.ANSHistogramStrategy.kPrecise:
                        for (uint shift = 0; shift < AnsParams.AnsLogTabSize; shift++)
                        {
                            if (!TryShift(histo, shift))
                            {
                                return Status.Failure("Failed to rebalance ANS histogram");
                            }
                        }

                        return true;

                    case HistogramParams.ANSHistogramStrategy.kApproximate:
                        for (uint shift = 0; shift <= AnsParams.AnsLogTabSize; shift += 2)
                        {
                            if (!TryShift(histo, shift))
                            {
                                return Status.Failure("Failed to rebalance ANS histogram");
                            }
                        }

                        return true;

                    default:
                        if (!TryShift(histo, 0) ||
                            !TryShift(histo, AnsParams.AnsLogTabSize / 2) ||
                            !TryShift(histo, AnsParams.AnsLogTabSize))
                        {
                            return Status.Failure("Failed to rebalance ANS histogram");
                        }

                        return true;
                }
            }

            private bool TryShift(Histogram histo, uint shift)
            {
                ANSEncodingHistogram normalized = Clone();
                normalized._method              = Math.Min(shift, AnsParams.AnsLogTabSize - 1) + 1;

                if (!normalized.RebalanceHistogram(histo))
                {
                    return false;
                }

                SizeWriter writer = default;

                if (!normalized.Encode(ref writer))
                {
                    return false;
                }

                normalized.Cost = writer.Size + normalized.EstimateDataBits(histo);

                if (normalized.Cost < Cost)
                {
                    Cost          = normalized.Cost;
                    _method       = normalized._method;
                    _omitPos      = normalized._omitPos;
                    _alphabetSize = normalized._alphabetSize;
                    _numSymbols   = normalized._numSymbols;
                    _counts       = normalized._counts;
                    Array.Copy(normalized._symbols, _symbols, KMaxNumSymbolsForSmallCode);
                }

                return true;
            }

            private Status EncodeCore<TWriter>(ref TWriter writer)
                where TWriter : struct, IBitSink
            {
                if (_alphabetSize > AnsParams.AnsMaxAlphabetSize)
                {
                    return Status.Failure("Alphabet size exceeds ANS maximum");
                }

                if (_method == 0)
                {
                    writer.Write(1, 0);
                    writer.Write(1, 1);
                    writer.StoreVarLenUint8(_alphabetSize - 1);

                    return true;
                }

                if (_numSymbols <= KMaxNumSymbolsForSmallCode)
                {
                    EncodeSmallTree(ref writer);

                    return true;
                }

                EncodeGeneralTree(ref writer);

                return true;
            }

            private void EncodeSmallTree<TWriter>(ref TWriter writer)
                where TWriter : struct, IBitSink
            {
                writer.Write(1, 1);

                if (_numSymbols == 0)
                {
                    writer.Write(1, 0);
                    writer.StoreVarLenUint8(0);
                }
                else
                {
                    writer.Write(1, _numSymbols - 1);

                    for (nuint i = 0; i < _numSymbols; i++)
                    {
                        writer.StoreVarLenUint8(_symbols[i]);
                    }
                }

                if (_numSymbols == 2)
                {
                    writer.Write(AnsParams.AnsLogTabSize, (ulong)_counts[_symbols[0]]);
                }
            }

            private void EncodeGeneralTree<TWriter>(ref TWriter writer)
                where TWriter : struct, IBitSink
            {
                writer.Write(1, 0);
                writer.Write(1, 0);

                int upperBoundLog = Bits.FloorLog2Nonzero(AnsParams.AnsLogTabSize + 1);
                int log           = Bits.FloorLog2Nonzero(_method);
                writer.Write((nuint)log, (1UL << log) - 1UL);

                if (log != upperBoundLog)
                {
                    writer.Write(1, 0);
                }

                writer.Write((nuint)log, ((1UL << log) - 1UL) & _method);
                writer.StoreVarLenUint8(_alphabetSize - 3);

                Span<byte> same     = stackalloc byte[AnsParams.AnsMaxAlphabetSize];
                Span<byte> bitWidth = stackalloc byte[AnsParams.AnsMaxAlphabetSize];
                ComputeRleAndBitWidths(same, bitWidth);
                WriteCountBitWidths(ref writer, same, bitWidth);
                WritePrecisionBits(ref writer, same, bitWidth);
            }

            private void ComputeRleAndBitWidths(Span<byte> same, Span<byte> bitWidth)
            {
                same.Clear();
                bitWidth.Clear();
                nuint last = 0;

                for (nuint i = 1; i <= _alphabetSize; i++)
                {
                    if (i == _alphabetSize || i == _omitPos || i == _omitPos + 1 || _counts[i] != _counts[last])
                    {
                        same[(int)last] = (byte)(i - last);
                        last            = i;
                    }
                }

                int omitWidth = 10;

                for (nuint i = 0; i < _alphabetSize; i++)
                {
                    if (i != _omitPos && _counts[i] > 0)
                    {
                        bitWidth[(int)i] = (byte)(Bits.FloorLog2Nonzero((uint)_counts[i]) + 1);
                        int candidate    = bitWidth[(int)i] + (i < _omitPos ? 1 : 0);

                        if (candidate > omitWidth)
                        {
                            omitWidth = candidate;
                        }
                    }
                }

                bitWidth[(int)_omitPos] = (byte)omitWidth;
            }

            private void WriteCountBitWidths<TWriter>(
                ref TWriter writer,
                ReadOnlySpan<byte> same,
                ReadOnlySpan<byte> bitWidth)
                where TWriter : struct, IBitSink
            {
                for (nuint i = 0; i < _alphabetSize; i++)
                {
                    int bw = bitWidth[(int)i];
                    writer.Write(KBitWidthLengths[bw], KBitWidthSymbols[bw]);

                    if (same[(int)i] >= KMinReps)
                    {
                        writer.Write(KBitWidthLengths[KRepSymbol], KBitWidthSymbols[KRepSymbol]);
                        writer.StoreVarLenUint8((nuint)(same[(int)i] - KMinReps));
                        i += (nuint)(same[(int)i] - 1);
                    }
                }
            }

            private void WritePrecisionBits<TWriter>(
                ref TWriter writer,
                ReadOnlySpan<byte> same,
                ReadOnlySpan<byte> bitWidth)
                where TWriter : struct, IBitSink
            {
                uint shift = _method - 1;

                if (shift == 0)
                {
                    return;
                }

                for (nuint i = 0; i < _alphabetSize; i++)
                {
                    if (bitWidth[(int)i] > 1 && i != _omitPos)
                    {
                        int bitcount = (int)AliasTable.GetPopulationCountPrecision((uint)(bitWidth[(int)i] - 1), shift);
                        int dropBits = bitWidth[(int)i] - 1 - bitcount;
                        writer.Write((nuint)bitcount, (ulong)((_counts[i] >> dropBits) - (1 << bitcount)));
                    }

                    if (same[(int)i] >= KMinReps)
                    {
                        i += (nuint)(same[(int)i] - 1);
                    }
                }
            }

            private float EstimateDataBits(Histogram histo)
            {
                long sum = 0;

                for (nuint i = 0; i < _alphabetSize; i++)
                {
                    sum += histo.Counts[(int)i] * (long)Lg2Lut[_counts[i]];
                }

                return (histo.TotalCount - MathF.ScaleB(sum, -31)) * AnsParams.AnsLogTabSize;
            }

            private static float EstimateDataBitsFlat(Histogram histo)
            {
                nuint len     = histo.AlphabetSize();
                long flatBits = (long)Lg2Lut[len] * AnsParams.AnsLogTabSize;

                return MathF.ScaleB((float)histo.TotalCount * flatBits, -31);
            }

            private static uint SmallestIncrementLog(uint count, uint shift)
            {
                if (count == 0)
                {
                    return 0;
                }

                uint bits     = (uint)Bits.FloorLog2Nonzero(count);
                uint dropBits = bits - AliasTable.GetPopulationCountPrecision(bits, shift);

                return dropBits;
            }

            private bool RebalanceHistogram(Histogram histo)
            {
                uint shift                  = _method - 1;
                CountsEntropy[] ac          = AllowedCounts.Array[shift];
                ushort[] ai                 = AllowedCounts.Index[shift];
                List<EntropyDelta> bins     = InitRebalanceBins(
                    histo,
                    shift,
                    ai,
                    out nuint remainderPos,
                    out long maxFreq,
                    out int rest);

                if (bins.Count > 0)
                {
                    RunGreedyRebalanceLoop(bins, ac, maxFreq, ref rest);

                    for (int i = 0; i < bins.Count; i++)
                    {
                        EntropyDelta a     = bins[i];
                        _counts[a.BinInd]  = ac[a.CountInd].Count;
                    }

                    for (nuint n = 0; n < remainderPos; n++)
                    {
                        if (_counts[n] >= 2048)
                        {
                            _counts[remainderPos] = _counts[n];
                            remainderPos          = n;
                            break;
                        }
                    }
                }

                _counts[remainderPos] = rest;
                _omitPos              = remainderPos;

                return _counts[remainderPos] > 0;
            }

            private List<EntropyDelta> InitRebalanceBins(
                Histogram histo,
                uint shift,
                ushort[] ai,
                out nuint remainderPos,
                out long maxFreq,
                out int rest)
            {
                List<EntropyDelta> bins = new List<EntropyDelta>((int)_alphabetSize);
                double norm             = (double)AnsParams.AnsTabSize / histo.TotalCount;
                remainderPos            = 0;
                maxFreq                 = 0;
                rest                    = AnsParams.AnsTabSize;

                for (nuint n = 0; n < _alphabetSize; n++)
                {
                    int freq = histo.Counts[(int)n];

                    if (freq > maxFreq)
                    {
                        remainderPos = n;
                        maxFreq      = freq;
                    }

                    double target = freq * norm;
                    int count     = Math.Max((int)Math.Round(target, MidpointRounding.AwayFromZero), freq > 0 ? 1 : 0);
                    count         = Math.Min(count, AnsParams.AnsTabSize - 1);
                    uint stepLog  = SmallestIncrementLog((uint)count, shift);
                    int inc       = 1 << (int)stepLog;
                    count        &= ~(inc - 1);

                    _counts[n]    = count;
                    rest         -= count;

                    if (target > 1.0)
                    {
                        bins.Add(new EntropyDelta(freq, ai[count], n));
                    }
                }

                for (int i = 0; i < bins.Count; i++)
                {
                    if (bins[i].BinInd == remainderPos)
                    {
                        bins.RemoveAt(i);
                        break;
                    }
                }

                rest += _counts[remainderPos];

                return bins;
            }

            private static void RunGreedyRebalanceLoop(
                List<EntropyDelta> bins,
                CountsEntropy[] ac,
                long maxFreq,
                ref int rest)
            {
                Span<long> balanceInc = stackalloc long[(int)AnsParams.AnsLogTabSize - 1];
                Span<long> balanceDec = stackalloc long[(int)AnsParams.AnsLogTabSize - 1];
                uint maxLog           = (uint)ac[1].StepLog;

                while (true)
                {
                    UpdateBalancePenalties(maxLog, maxFreq, rest, balanceInc, balanceDec);
                    int bestIncIdx = FindBestIncBin(bins, ac, balanceInc, out long bestIncDelta);

                    if (bestIncDelta > 0)
                    {
                        EntropyDelta b = bins[bestIncIdx];
                        rest          -= 1 << ac[b.CountInd].StepLog;
                        b.CountInd--;
                        bins[bestIncIdx] = b;
                    }
                    else
                    {
                        int bestDecIdx = FindBestDecBin(bins, ac, balanceDec, out long bestDecDelta);

                        if (bestDecDelta >= 0)
                        {
                            break;
                        }

                        EntropyDelta b = bins[bestDecIdx];
                        b.CountInd++;
                        rest            += 1 << ac[b.CountInd].StepLog;
                        bins[bestDecIdx] = b;
                    }
                }
            }

            private static void UpdateBalancePenalties(
                uint maxLog,
                long maxFreq,
                int rest,
                Span<long> balanceInc,
                Span<long> balanceDec)
            {
                for (uint log = 0; log <= maxLog; log++)
                {
                    int delta = 1 << (int)log;

                    if (rest >= AnsParams.AnsTabSize)
                    {
                        balanceInc[(int)log] = 0;
                        balanceDec[(int)log] = 0;
                    }
                    else if (rest > 1)
                    {
                        balanceInc[(int)log] = rest > delta
                            ? maxFreq * (long)(Lg2Lut[rest] - Lg2Lut[rest - delta])
                            : long.MaxValue;
                        balanceDec[(int)log] = rest + delta < AnsParams.AnsTabSize
                            ? maxFreq * (long)(Lg2Lut[rest + delta] - Lg2Lut[rest])
                            : 0;
                    }
                    else
                    {
                        balanceInc[(int)log] = long.MaxValue;
                        balanceDec[(int)log] = long.MaxValue;
                    }
                }
            }

            private static int FindBestIncBin(
                List<EntropyDelta> bins,
                CountsEntropy[] ac,
                ReadOnlySpan<long> balanceInc,
                out long bestDelta)
            {
                int bestIdx   = 0;
                EntropyDelta b0 = bins[0];
                bestDelta     = (b0.Freq * (long)ac[b0.CountInd].DeltaLg2) - balanceInc[ac[b0.CountInd].StepLog];
                long bestUnit = bestDelta >> ac[b0.CountInd].StepLog;

                for (int i = 1; i < bins.Count; i++)
                {
                    EntropyDelta b = bins[i];
                    long delta     = (b.Freq * (long)ac[b.CountInd].DeltaLg2) - balanceInc[ac[b.CountInd].StepLog];
                    long unit      = delta >> ac[b.CountInd].StepLog;

                    if (unit > bestUnit)
                    {
                        bestIdx   = i;
                        bestDelta = delta;
                        bestUnit  = unit;
                    }
                }

                return bestIdx;
            }

            private static int FindBestDecBin(
                List<EntropyDelta> bins,
                CountsEntropy[] ac,
                ReadOnlySpan<long> balanceDec,
                out long bestDelta)
            {
                int bestIdx     = 0;
                EntropyDelta b0 = bins[0];
                bestDelta       = (b0.Freq * (long)ac[b0.CountInd + 1].DeltaLg2) - balanceDec[ac[b0.CountInd + 1].StepLog];
                long bestUnit   = bestDelta >> ac[b0.CountInd + 1].StepLog;

                for (int i = 1; i < bins.Count; i++)
                {
                    EntropyDelta b = bins[i];
                    long delta     = (b.Freq * (long)ac[b.CountInd + 1].DeltaLg2) - balanceDec[ac[b.CountInd + 1].StepLog];
                    long unit      = delta >> ac[b.CountInd + 1].StepLog;

                    if (unit < bestUnit)
                    {
                        bestIdx   = i;
                        bestDelta = delta;
                        bestUnit  = unit;
                    }
                }

                return bestIdx;
            }

            private static uint[] BuildLg2Lut()
            {
                uint[] lut = new uint[AnsParams.AnsTabSize + 1];
                lut[0]     = 0;

                for (int i = 1; i < lut.Length; i++)
                {
                    double scaled = Math.ScaleB(Math.Log2(i) / AnsParams.AnsLogTabSize, 31);
                    lut[i]        = (uint)Math.Round(scaled, MidpointRounding.AwayFromZero);
                }

                return lut;
            }

            private static AllowedCountsTable BuildAllowedCounts()
            {
                AllowedCountsTable result = new AllowedCountsTable();

                for (uint shift = 0; shift < AnsParams.AnsLogTabSize; shift++)
                {
                    CountsEntropy[] ac = result.Array[shift];
                    ushort[] ai        = result.Index[shift];
                    int last           = ~0;
                    int slot           = 0;
                    ac[0].DeltaLg2     = 0;
                    ac[0].StepLog      = 0;

                    for (int i = AnsParams.AnsTabSize - 1; i >= 0; i--)
                    {
                        int curr = i & ~((1 << (int)SmallestIncrementLog((uint)i, shift)) - 1);

                        if (curr == last)
                        {
                            continue;
                        }

                        last           = curr;
                        ac[slot].Count = (short)curr;
                        ai[curr]       = (ushort)slot;

                        if (curr == 0)
                        {
                            ac[slot].DeltaLg2 = int.MaxValue;
                            ac[slot].StepLog  = 0;
                        }
                        else if (slot > 0)
                        {
                            int prev          = ac[slot - 1].Count;
                            double scaled     = Math.ScaleB(Math.Log2((double)prev / curr) / AnsParams.AnsLogTabSize, 31);
                            ac[slot].DeltaLg2 = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
                            ac[slot].StepLog  = (short)Bits.FloorLog2Nonzero((uint)(prev - curr));
                        }

                        slot++;
                    }
                }

                return result;
            }

            private struct CountsEntropy
            {
                public short Count;
                public short StepLog;
                public int DeltaLg2;
            }

            private struct EntropyDelta
            {
                public int Freq;
                public int CountInd;
                public nuint BinInd;

                public EntropyDelta(int freq, int countInd, nuint binInd)
                {
                    Freq     = freq;
                    CountInd = countInd;
                    BinInd   = binInd;
                }
            }

            private sealed class AllowedCountsTable
            {
                public readonly CountsEntropy[][] Array = new CountsEntropy[AnsParams.AnsLogTabSize][];
                public readonly ushort[][] Index = new ushort[AnsParams.AnsLogTabSize][];

                public AllowedCountsTable()
                {
                    for (int i = 0; i < (int)AnsParams.AnsLogTabSize; i++)
                    {
                        Array[i] = new CountsEntropy[AnsParams.AnsTabSize];
                        Index[i] = new ushort[AnsParams.AnsTabSize];
                    }
                }
            }

            private interface IBitSink
            {
                void Write(nuint numBits, ulong bits);

                void StoreVarLenUint8(nuint n);
            }

            private readonly struct BitWriterAdapter : IBitSink
            {
                private readonly BitWriter _writer;

                public BitWriterAdapter(BitWriter writer)
                {
                    _writer = writer;
                }

                public void Write(nuint numBits, ulong bits) => _writer.Write(numBits, bits);

                public void StoreVarLenUint8(nuint n) => EncAns.StoreVarLenUint8(n, _writer);
            }

            private struct SizeWriterAdapter : IBitSink
            {
                public SizeWriter Writer;

                public SizeWriterAdapter(SizeWriter writer)
                {
                    Writer = writer;
                }

                public void Write(nuint numBits, ulong bits) => Writer.Write(numBits, bits);

                public void StoreVarLenUint8(nuint n) => EncAns.StoreVarLenUint8(n, ref Writer);
            }
        }
    }
}
