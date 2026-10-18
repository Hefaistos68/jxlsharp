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
    /// <summary>
    /// LZ77 / RLE backward-reference token stream compressor.
    /// Ported from <c>lib/jxl/enc_lz77.h</c> and <c>lib/jxl/enc_lz77.cc</c>.
    /// </summary>
    public static class EncLz77
    {
        private const int KHashSize = 3;

        private static readonly float[] KLenCostTable =
        {
            2.797667318563126f,  3.213177690381199f,  2.5706009246743737f,
            2.408392498667534f,  2.829649191872326f,  3.3923087753324577f,
            4.029267451554331f,  4.415576699706408f,  4.509357574741465f,
            9.21481543803004f,   10.020590190114898f, 11.858671627804766f,
            12.45853300490526f,  11.713105831990857f, 12.561996324849314f,
            13.775477692278367f, 13.174027068768641f,
        };

        private static readonly float[] KDistCostTable =
        {
            6.368282626312716f,  5.680793277090298f,  8.347404197105247f,
            7.641619201599141f,  6.914328374119438f,  7.959808291537444f,
            8.70023120759855f,   8.71378518934703f,   9.379132523982769f,
            9.110472749092708f,  9.159029569270908f,  9.430936766731973f,
            7.278284055315169f,  7.8278514904267755f, 10.026641158289236f,
            9.976049229827066f,  9.64351607048908f,   9.563403863480442f,
            10.171474111762747f, 10.45950155077234f,  9.994813912104219f,
            10.322524683741156f, 8.465808729388186f,  8.756254166066853f,
            10.160930174662234f, 10.247329273413435f, 10.04090403724809f,
            10.129398517544082f, 9.342311691539546f,  9.07608009102374f,
            10.104799540677513f, 10.378079384990906f, 10.165828974075072f,
            10.337595322341553f, 7.940557464567944f,  10.575665823319431f,
            11.023344321751955f, 10.736144698831827f, 11.118277044595054f,
            7.468468230648442f,  10.738305230932939f, 10.906980780216568f,
            10.163468216353817f, 10.17805759656433f,  11.167283670483565f,
            11.147050200274544f, 10.517921919244333f, 10.651764778156886f,
            10.17074446448919f,  11.217636876224745f, 11.261630721139484f,
            11.403140815247259f, 10.892472096873417f, 11.1859607804481f,
            8.017346947551262f,  7.895143720278828f,  11.036577113822025f,
            11.170562110315794f, 10.326988722591086f, 10.40872184751056f,
            11.213498225466386f, 11.30580635516863f,  10.672272515665442f,
            10.768069466228063f, 11.145257364153565f, 11.64668307145549f,
            10.593156194627339f, 11.207499484844943f, 10.767517766396908f,
            10.826629811407042f, 10.737764794499988f, 10.6200448518045f,
            10.191315385198092f, 8.468384171390085f,  11.731295299170432f,
            11.824619886654398f, 10.41518844301179f,  10.16310536548649f,
            10.539423685097576f, 10.495136599328031f, 10.469112847728267f,
            11.72057686174922f,  10.910326337834674f, 11.378921834673758f,
            11.847759036098536f, 11.92071647623854f,  10.810628276345282f,
            11.008601085273893f, 11.910326337834674f, 11.949212023423133f,
            11.298614839104337f, 11.611603659010392f, 10.472930394619985f,
            11.835564720850282f, 11.523267392285337f, 12.01055816679611f,
            8.413029688994023f,  11.895784139536406f, 11.984679534970505f,
            11.220654278717394f, 11.716311684833672f, 10.61036646226114f,
            10.89849965960364f,  10.203762898863669f, 10.997560826267238f,
            11.484217379438984f, 11.792836176993665f, 12.24310468755171f,
            11.464858097919262f, 12.212747017409377f, 11.425595666074955f,
            11.572048533398757f, 12.742093965163013f, 11.381874288645637f,
            12.191870445817015f, 11.683156920035426f, 11.152442115262197f,
            11.90303691580457f,  11.653292787169159f, 11.938615382266098f,
            16.970641701570223f, 16.853602280380002f, 17.26240782594733f,
            16.644655390108507f, 17.14310889757499f,  16.910935455445955f,
            17.505678976959697f, 17.213498225466388f, 2.4162310293553024f,
            3.494587244462329f,  3.5258600986408344f, 3.4959806589517095f,
            3.098390886949687f,  3.343454654302911f,  3.588847442290287f,
            4.14614790111827f,   5.152948641990529f,  7.433696808092598f,
            9.716311684833672f,
        };

        /// <summary>
        /// Returns a list of token streams with LZ77 compression applied according to <paramref name="histParams"/>,
        /// or an empty list if LZ77 compression is not beneficial.
        /// Ported from <c>jxl::ApplyLZ77</c> in <c>lib/jxl/enc_lz77.cc</c>.
        /// </summary>
        /// <param name="histParams">Histogram and LZ77 method parameters.</param>
        /// <param name="numContexts">Number of token contexts.</param>
        /// <param name="tokens">Input token streams.</param>
        /// <param name="lz77">LZ77 configuration parameters.</param>
        /// <returns>LZ77-compressed token streams, or an empty list if not beneficial.</returns>
        public static List<List<Token>> ApplyLZ77(
            HistogramParams histParams,
            nuint numContexts,
            IReadOnlyList<List<Token>> tokens,
            LZ77Params lz77)
        {
            return histParams.Lz77Method switch
            {
                HistogramParams.LZ77Method.kRLE         => ApplyLZ77Rle(histParams, numContexts, tokens, lz77),
                HistogramParams.LZ77Method.kLZ77b1w3f   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 1, false),
                HistogramParams.LZ77Method.kLZ77b3w3f   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 3, false),
                HistogramParams.LZ77Method.kLZ77b7w3f   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 7, false),
                HistogramParams.LZ77Method.kLZ77b15w3f  => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 15, false),
                HistogramParams.LZ77Method.kLZ77b31w3f  => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 31, false),
                HistogramParams.LZ77Method.kLZ77b1w3t   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 1, true),
                HistogramParams.LZ77Method.kLZ77b3w3t   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 3, true),
                HistogramParams.LZ77Method.kLZ77b7w3t   => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 7, true),
                HistogramParams.LZ77Method.kLZ77b15w3t  => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 15, true),
                HistogramParams.LZ77Method.kLZ77b31w3t  => ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 31, true),
                HistogramParams.LZ77Method.kOptc1       => ApplyLZ77Optimal(histParams, numContexts, tokens, lz77, 1),
                HistogramParams.LZ77Method.kOptc3       => ApplyLZ77Optimal(histParams, numContexts, tokens, lz77, 3),
                HistogramParams.LZ77Method.kOptc8       => ApplyLZ77Optimal(histParams, numContexts, tokens, lz77, 8),
                HistogramParams.LZ77Method.kOptc256     => ApplyLZ77Optimal(histParams, numContexts, tokens, lz77, 256),
                _                                       => new List<List<Token>>(),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetHash(nuint pos, uint[] data)
        {
            if (pos + KHashSize > (nuint)data.Length)
            {
                return 0;
            }

            uint h = 0;

            for (nuint i = 0; i < KHashSize; i++)
            {
                h ^= data[pos + i] + 0x9e3779b9u + (h << 6) + (h >> 2);
            }

            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;

            return h;
        }

        private static float LenCost(nuint len)
        {
            HybridUintConfig cfg = new HybridUintConfig(1, 0, 0);
            cfg.Encode((uint)len, out uint tok, out uint nbits, out _);

            if (tok >= (uint)KLenCostTable.Length)
            {
                tok = (uint)(KLenCostTable.Length - 1);
            }

            return KLenCostTable[tok] + nbits;
        }

        private static float DistCost(nuint dist)
        {
            HybridUintConfig cfg = new HybridUintConfig(7, 0, 0);
            cfg.Encode((uint)dist, out uint tok, out uint nbits, out _);

            if (tok >= (uint)KDistCostTable.Length)
            {
                tok = (uint)(KDistCostTable.Length - 1);
            }

            return KDistCostTable[tok] + nbits;
        }

        private static List<List<Token>> ApplyLZ77Rle(
            HistogramParams histParams,
            nuint numContexts,
            IReadOnlyList<List<Token>> tokens,
            LZ77Params lz77)
        {
            List<List<Token>> tokensLz77 = new List<List<Token>>(tokens.Count);
            SymbolCostEstimator sce      = new SymbolCostEstimator(numContexts, histParams.ForceHuffman, tokens, lz77);
            float bitDecrease            = 0.0f;
            nuint totalSymbols           = 0;
            HybridUintConfig uintConfig  = new HybridUintConfig(4, 2, 0);

            for (int stream = 0; stream < tokens.Count; stream++)
            {
                nuint distanceMultiplier = stream < histParams.ImageWidths.Count ? histParams.ImageWidths[stream] : 0;
                List<Token> inStream     = tokens[stream];
                List<Token> outStream    = new List<Token>(inStream.Count);
                tokensLz77.Add(outStream);
                totalSymbols            += (nuint)inStream.Count;
                float[] symCost          = BuildCumulativeSymbolCosts(inStream, uintConfig, sce);

                EncodeRleStream(inStream, outStream, symCost, distanceMultiplier, lz77, ref bitDecrease);
            }

            if (bitDecrease > (totalSymbols * 0.2f) + 16.0f)
            {
                return tokensLz77;
            }

            return new List<List<Token>>();
        }

        private static void EncodeRleStream(
            List<Token> inStream,
            List<Token> outStream,
            float[] symCost,
            nuint distanceMultiplier,
            LZ77Params lz77,
            ref float bitDecrease)
        {
            uint distanceSymbol = distanceMultiplier != 0 ? 1u : 0u;

            for (int i = 0; i < inStream.Count; i++)
            {
                int numToCopy = 0;

                if (i > 0)
                {
                    uint prevVal = inStream[i - 1].Value;

                    while (i + numToCopy < inStream.Count && inStream[i + numToCopy].Value == prevVal)
                    {
                        numToCopy++;
                    }
                }

                if (numToCopy == 0)
                {
                    outStream.Add(inStream[i]);
                    continue;
                }

                float cost     = symCost[i + numToCopy] - symCost[i];
                uint lz77Len   = (uint)numToCopy - lz77.MinLength;
                float lz77Cost = (uint)numToCopy >= lz77.MinLength
                    ? Bits.CeilLog2Nonzero(lz77Len + 1u) + 1.0f
                    : 0.0f;

                if ((uint)numToCopy < lz77.MinLength || cost <= lz77Cost)
                {
                    for (int j = 0; j < numToCopy; j++)
                    {
                        outStream.Add(inStream[i + j]);
                    }

                    i += numToCopy - 1;
                    continue;
                }

                outStream.Add(new Token(inStream[i].Context, lz77Len, isLz77Length: true));
                i           += numToCopy - 1;
                bitDecrease += cost - lz77Cost;
                outStream.Add(new Token((uint)lz77.NonserializedDistanceContext, distanceSymbol));
            }
        }

        private static float[] BuildCumulativeSymbolCosts(
            List<Token> inStream,
            in HybridUintConfig uintConfig,
            SymbolCostEstimator sce)
        {
            float[] symCost = new float[inStream.Count + 1];

            for (int i = 0; i < inStream.Count; i++)
            {
                uintConfig.Encode(inStream[i].Value, out uint tok, out uint nbits, out _);
                symCost[i + 1] = sce.GetBits(inStream[i].Context, tok) + nbits + symCost[i];
            }

            return symCost;
        }

        private static List<List<Token>> ApplyLZ77Fast(
            HistogramParams histParams,
            nuint numContexts,
            IReadOnlyList<List<Token>> tokens,
            LZ77Params lz77,
            ushort bucketSize,
            bool runtimeCostComparison)
        {
            List<List<Token>> tokensLz77 = new List<List<Token>>(tokens.Count);
            SymbolCostEstimator sce      = new SymbolCostEstimator(numContexts, histParams.ForceHuffman, tokens, lz77);
            float bitDecrease            = 0.0f;
            nuint totalSymbols           = 0;
            HybridUintConfig uintConfig  = new HybridUintConfig(4, 2, 0);

            for (int stream = 0; stream < tokens.Count; stream++)
            {
                nuint distanceMultiplier = stream < histParams.ImageWidths.Count ? histParams.ImageWidths[stream] : 0;
                List<Token> inStream     = tokens[stream];
                List<Token> outStream    = new List<Token>(inStream.Count);
                tokensLz77.Add(outStream);
                totalSymbols            += (nuint)inStream.Count;

                uint[] data     = new uint[inStream.Count];
                float[] symCost = new float[inStream.Count + 1];

                for (int pos = 0; pos < inStream.Count; pos++)
                {
                    uintConfig.Encode(inStream[pos].Value, out uint tok, out uint nbits, out _);
                    symCost[pos + 1] = sce.GetBits(inStream[pos].Context, tok) + nbits + symCost[pos];
                    data[pos]        = inStream[pos].Value;
                }

                EncodeFastLz77Stream(
                    inStream,
                    outStream,
                    data,
                    symCost,
                    (distanceMultiplier, bucketSize, runtimeCostComparison),
                    lz77,
                    sce,
                    ref bitDecrease);
            }

            if (bitDecrease > (totalSymbols * 0.2f) + 16.0f)
            {
                return tokensLz77;
            }

            return new List<List<Token>>();
        }

        private static void EncodeFastLz77Stream(
            List<Token> inStream,
            List<Token> outStream,
            uint[] data,
            float[] symCost,
            (nuint DistanceMultiplier, ushort BucketSize, bool RuntimeCostComparison) cfg,
            LZ77Params lz77,
            SymbolCostEstimator sce,
            ref float bitDecrease)
        {
            nuint minLength     = lz77.MinLength;
            LZ77HashMap hashMap = new LZ77HashMap(data, cfg.DistanceMultiplier, cfg.BucketSize);

            for (nuint pos = 0; pos < (nuint)inStream.Count; pos++)
            {
                outStream.Add(inStream[(int)pos]);

                if (pos + KHashSize >= (nuint)inStream.Count)
                {
                    continue;
                }

                hashMap.FindMatch(pos, out nuint len, out nuint distSymbol, minLength);

                if (len < minLength)
                {
                    continue;
                }

                float cost     = symCost[pos + len] - symCost[pos];
                nuint lz77Len  = len - lz77.MinLength;
                float lz77Cost = LenCost(lz77Len) + DistCost(distSymbol);

                if (cfg.RuntimeCostComparison && lz77Cost > cost)
                {
                    for (nuint offset = 1; offset < len; offset++)
                    {
                        outStream.Add(inStream[(int)(pos + offset)]);
                        hashMap.Update(pos + offset);
                    }

                    pos += len - 1;
                    continue;
                }

                Token last   = outStream[outStream.Count - 1];
                bitDecrease += cost - lz77Cost - sce.AddSymbolCost(last.Context);

                outStream[outStream.Count - 1] = new Token(last.Context, (uint)(len - minLength), isLz77Length: true);
                outStream.Add(new Token((uint)lz77.NonserializedDistanceContext, (uint)distSymbol));

                for (nuint offset = 1; offset < len; offset++)
                {
                    hashMap.Update(pos + offset);
                }

                pos += len - 1;
            }
        }

        private static List<List<Token>> ApplyLZ77Optimal(
            HistogramParams histParams,
            nuint numContexts,
            IReadOnlyList<List<Token>> tokens,
            LZ77Params lz77,
            uint maxChainLength)
        {
            List<List<Token>> tokensForCostEstimate = ApplyLZ77Fast(histParams, numContexts, tokens, lz77, 7, false);

            if (tokensForCostEstimate.Count == 0)
            {
                return new List<List<Token>>();
            }

            SymbolCostEstimator sce      = new SymbolCostEstimator(numContexts + 1, histParams.ForceHuffman, tokensForCostEstimate, lz77);
            List<List<Token>> tokensLz77 = new List<List<Token>>(tokens.Count);
            HybridUintConfig uintConfig  = new HybridUintConfig(4, 2, 0);

            for (int stream = 0; stream < tokens.Count; stream++)
            {
                nuint distanceMultiplier = stream < histParams.ImageWidths.Count ? histParams.ImageWidths[stream] : 0;
                List<Token> inStream     = tokens[stream];
                List<Token> outStream    = new List<Token>(inStream.Count);
                tokensLz77.Add(outStream);

                if (inStream.Count == 0)
                {
                    continue;
                }

                float[] symCost = BuildCumulativeSymbolCosts(inStream, uintConfig, sce);
                EncodeOptimalStream(inStream, outStream, symCost, distanceMultiplier, maxChainLength, lz77, sce);
            }

            return tokensLz77;
        }

        private struct MatchInfo
        {
            public uint Len;
            public uint DistSymbol;
            public uint Ctx;
            public float TotalCost;
        }

        private static void EncodeOptimalStream(
            List<Token> inStream,
            List<Token> outStream,
            float[] symCost,
            nuint distanceMultiplier,
            uint maxChainLength,
            LZ77Params lz77,
            SymbolCostEstimator sce)
        {
            nuint maxDistance = (nuint)inStream.Count;
            nuint minLength   = lz77.MinLength;
            nuint maxLength   = (nuint)inStream.Count;
            nuint windowSize  = 1;

            while (windowSize < maxDistance && windowSize < (nuint)ANSSymbolReader.KWindowSize)
            {
                windowSize <<= 1;
            }

            HashChain chain          = new HashChain(inStream, windowSize, minLength, maxLength, distanceMultiplier, maxChainLength);
            MatchInfo[] prefixCosts  = new MatchInfo[inStream.Count + 1];

            for (int i = 1; i <= inStream.Count; i++)
            {
                prefixCosts[i].TotalCost = float.MaxValue;
            }

            List<uint> distSymbols = new List<uint>();
            nuint rleLength        = 0;
            nuint skipLz77         = 0;

            for (nuint i = 0; i < (nuint)inStream.Count; i++)
            {
                chain.Update(i);
                float litCost = prefixCosts[i].TotalCost + symCost[i + 1] - symCost[i];

                if (prefixCosts[i + 1].TotalCost > litCost)
                {
                    prefixCosts[i + 1].DistSymbol = 0;
                    prefixCosts[i + 1].Len        = 1;
                    prefixCosts[i + 1].Ctx        = inStream[(int)i].Context;
                    prefixCosts[i + 1].TotalCost  = litCost;
                }

                if (skipLz77 > 0)
                {
                    skipLz77--;
                    continue;
                }

                (rleLength, skipLz77) = EvaluateOptimalMatchesAtPosition(
                    i,
                    inStream[(int)i].Context,
                    chain,
                    distSymbols,
                    prefixCosts,
                    (distanceMultiplier, minLength, maxDistance),
                    (lz77, sce, rleLength, skipLz77));
            }

            BacktrackOptimalPath(inStream, outStream, prefixCosts, minLength, lz77);
        }

        private static (nuint RleLength, nuint SkipLz77) EvaluateOptimalMatchesAtPosition(
            nuint i,
            uint tokenCtx,
            HashChain chain,
            List<uint> distSymbols,
            MatchInfo[] prefixCosts,
            (nuint DistanceMultiplier, nuint MinLength, nuint MaxDistance) bounds,
            (LZ77Params Lz77, SymbolCostEstimator Sce, nuint RleLength, nuint SkipLz77) state)
        {
            distSymbols.Clear();
            chain.FindMatches(i, (int)bounds.MaxDistance, distSymbols);

            if ((nuint)distSymbols.Count <= bounds.MinLength)
            {
                return (state.RleLength, state.SkipLz77);
            }

            uint bestCost = distSymbols[distSymbols.Count - 1];

            for (int j = distSymbols.Count - 1; j >= (int)bounds.MinLength; j--)
            {
                if (distSymbols[j] < bestCost)
                {
                    bestCost = distSymbols[j];
                }

                distSymbols[j] = bestCost;
            }

            for (nuint j = bounds.MinLength; j < (nuint)distSymbols.Count; j++)
            {
                float lz77Cost = state.Sce.LenCost(tokenCtx, j - bounds.MinLength, state.Lz77) +
                                 state.Sce.DistCost(distSymbols[(int)j], state.Lz77);
                float cost     = prefixCosts[i].TotalCost + lz77Cost;

                if (prefixCosts[i + j].TotalCost > cost)
                {
                    prefixCosts[i + j].Len        = (uint)j;
                    prefixCosts[i + j].DistSymbol = distSymbols[(int)j] + 1u;
                    prefixCosts[i + j].Ctx        = tokenCtx;
                    prefixCosts[i + j].TotalCost  = cost;
                }
            }

            uint lastDist   = distSymbols[distSymbols.Count - 1];
            nuint rleLength = ((lastDist == 0 && bounds.DistanceMultiplier == 0) ||
                               (lastDist == 1 && bounds.DistanceMultiplier != 0))
                ? state.RleLength + 1
                : 0;
            nuint skipLz77  = state.SkipLz77;

            if (rleLength >= 8 && distSymbols.Count > 9)
            {
                skipLz77  = (nuint)(distSymbols.Count - 10);
                rleLength = 0;
            }

            return (rleLength, skipLz77);
        }

        private static void BacktrackOptimalPath(
            List<Token> inStream,
            List<Token> outStream,
            MatchInfo[] prefixCosts,
            nuint minLength,
            LZ77Params lz77)
        {
            int pos = inStream.Count;

            while (pos > 0)
            {
                bool isLz77Length = prefixCosts[pos].DistSymbol != 0;

                if (isLz77Length)
                {
                    uint distSymbol = prefixCosts[pos].DistSymbol - 1u;
                    outStream.Add(new Token((uint)lz77.NonserializedDistanceContext, distSymbol));
                }

                uint val = isLz77Length
                    ? prefixCosts[pos].Len - (uint)minLength
                    : inStream[pos - 1].Value;
                outStream.Add(new Token(prefixCosts[pos].Ctx, val, isLz77Length));
                pos -= (int)prefixCosts[pos].Len;
            }

            outStream.Reverse();
        }

        private sealed class SymbolCostEstimator
        {
            private readonly nuint _maxAlphabetSize;
            private readonly float[] _bits;
            private readonly float[] _addSymbolCost;

            public SymbolCostEstimator(
                nuint numContexts,
                bool forceHuffman,
                IReadOnlyList<List<Token>> tokens,
                LZ77Params lz77)
            {
                Histogram[] builder = new Histogram[numContexts];

                for (nuint i = 0; i < numContexts; i++)
                {
                    builder[i] = new Histogram();
                }

                HybridUintConfig uintConfig = new HybridUintConfig(4, 2, 0);

                for (int s = 0; s < tokens.Count; s++)
                {
                    List<Token> stream = tokens[s];

                    for (int t = 0; t < stream.Count; t++)
                    {
                        Token token          = stream[t];
                        HybridUintConfig cfg = token.IsLz77Length ? lz77.LengthUintConfig : uintConfig;
                        cfg.Encode(token.Value, out uint tok, out _, out _);

                        if (token.IsLz77Length)
                        {
                            tok += lz77.MinSymbol;
                        }

                        Debug.Assert(token.Context < numContexts);
                        builder[token.Context].Add(tok);
                    }
                }

                for (nuint i = 0; i < numContexts; i++)
                {
                    if ((nuint)builder[i].Counts.Count > _maxAlphabetSize)
                    {
                        _maxAlphabetSize = (nuint)builder[i].Counts.Count;
                    }
                }

                _bits          = new float[numContexts * _maxAlphabetSize];
                _addSymbolCost = new float[numContexts];

                for (nuint i = 0; i < numContexts; i++)
                {
                    PopulateContextCosts(i, builder[i], forceHuffman);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float GetBits(nuint ctx, nuint sym)
            {
                nuint idx = (ctx * _maxAlphabetSize) + sym;

                return idx < (nuint)_bits.Length ? _bits[idx] : AnsParams.AnsLogTabSize;
            }

            public float LenCost(nuint ctx, nuint len, LZ77Params lz77)
            {
                lz77.LengthUintConfig.Encode((uint)len, out uint tok, out uint nbits, out _);
                tok += lz77.MinSymbol;

                return nbits + GetBits(ctx, tok);
            }

            public float DistCost(nuint len, LZ77Params lz77)
            {
                HybridUintConfig cfg = new HybridUintConfig(4, 2, 0);
                cfg.Encode((uint)len, out uint tok, out uint nbits, out _);

                return nbits + GetBits(lz77.NonserializedDistanceContext, tok);
            }

            public float AddSymbolCost(nuint idx) => _addSymbolCost[idx];

            private void PopulateContextCosts(nuint ctxIdx, Histogram hist, bool forceHuffman)
            {
                float invTotal  = 1.0f / (hist.TotalCount + 1e-8f);
                float totalCost = 0.0f;

                for (int j = 0; j < hist.Counts.Count; j++)
                {
                    int cnt    = hist.Counts[j];
                    float cost = 0.0f;

                    if (cnt != 0 && (nuint)cnt != hist.TotalCount)
                    {
                        cost = -FastMath.FastLog2f(cnt * invTotal);

                        if (forceHuffman)
                        {
                            cost = MathF.Ceiling(cost);
                        }
                    }
                    else if (cnt == 0)
                    {
                        cost = AnsParams.AnsLogTabSize;
                    }

                    _bits[(ctxIdx * _maxAlphabetSize) + (nuint)j] = cost;
                    totalCost                                    += cost * cnt;
                }

                _addSymbolCost[ctxIdx] = Math.Max(0.0f, 6.0f - (totalCost * invTotal));
            }
        }

        private sealed class LZ77HashMap
        {
            private readonly uint[] _data;
            private readonly uint _hashMask;
            private readonly ushort _bucketSize;
            private readonly uint[] _bucketData;
            private readonly ushort[] _bucketIdx;
            private readonly ushort[] _bucketCount;
            private readonly sbyte[] _specialDistTable;
            private readonly nuint _numSpecialDistances;

            public LZ77HashMap(uint[] data, nuint distanceMultiplier, ushort bucketSize, int hashBits = 14)
            {
                _data       = data;
                _hashMask   = (1u << hashBits) - 1u;
                _bucketSize = bucketSize;
                int numBuckets = (int)_hashMask + 1;
                _bucketData  = new uint[numBuckets * bucketSize];
                _bucketIdx   = new ushort[numBuckets];
                _bucketCount = new ushort[numBuckets];

                if (distanceMultiplier != 0)
                {
                    _numSpecialDistances = ANSSymbolReader.KNumSpecialDistances;
                    _specialDistTable    = new sbyte[(distanceMultiplier * 8) + 8];
                    Array.Fill(_specialDistTable, (sbyte)-1);

                    for (sbyte i = 0; i < ANSSymbolReader.KNumSpecialDistances; i++)
                    {
                        int sd = ANSSymbolReader.SpecialDistance(i, (int)distanceMultiplier);

                        if (sd < _specialDistTable.Length)
                        {
                            _specialDistTable[sd] = i;
                        }
                    }
                }
                else
                {
                    _specialDistTable = Array.Empty<sbyte>();
                }
            }

            public void FindMatch(nuint pos, out nuint len, out nuint distSymbol, nuint minLen)
            {
                uint h     = GetHash(pos, _data) & _hashMask;
                len        = 0;
                distSymbol = 0;
                int bBase  = (int)h * _bucketSize;
                ushort cnt = _bucketCount[h];

                for (ushort i = 0; i < cnt; i++)
                {
                    nuint candidate = _bucketData[bBase + i];
                    nuint dist      = pos - candidate;
                    nuint curLength = MatchLength(candidate, pos);

                    if (curLength < len || curLength < minLen)
                    {
                        continue;
                    }

                    nuint curDistSymbol = _numSpecialDistances + dist - 1;

                    if (dist < (nuint)_specialDistTable.Length)
                    {
                        sbyte lookup = _specialDistTable[dist];

                        if (lookup >= 0)
                        {
                            curDistSymbol = (nuint)lookup;
                        }
                    }

                    if (curLength == len && curDistSymbol >= distSymbol)
                    {
                        continue;
                    }

                    len        = curLength;
                    distSymbol = curDistSymbol;
                }

                InsertPosition(h, (uint)pos);
            }

            public void Update(nuint pos)
            {
                uint h = GetHash(pos, _data) & _hashMask;
                InsertPosition(h, (uint)pos);
            }

            private void InsertPosition(uint h, uint pos)
            {
                int bBase                       = (int)h * _bucketSize;
                ushort idx                      = _bucketIdx[h];
                _bucketData[bBase + idx]        = pos;
                _bucketCount[h]                 = Math.Min((ushort)(_bucketCount[h] + 1), _bucketSize);
                ushort nextIdx                  = (ushort)(idx + 1);
                _bucketIdx[h]                   = nextIdx >= _bucketSize ? (ushort)0 : nextIdx;
            }

            private nuint MatchLength(nuint a, nuint b)
            {
                nuint len = 0;

                while (b + len < (nuint)_data.Length && _data[a + len] == _data[b + len])
                {
                    ++len;
                }

                return len;
            }
        }

        private sealed class HashChain
        {
            private const uint KHashNumValues = 32768;
            private const uint KHashMask      = KHashNumValues - 1;

            private readonly nuint _size;
            private readonly uint[] _data;
            private readonly int[] _head;
            private readonly uint[] _chain;
            private readonly int[] _val;
            private readonly int[] _headz;
            private readonly uint[] _chainz;
            private readonly uint[] _zeros;
            private uint _numzeros;
            private readonly nuint _windowSize;
            private readonly nuint _windowMask;
            private readonly nuint _minLength;
            private readonly nuint _maxLength;
            private readonly Dictionary<int, int> _specialDistTable = new Dictionary<int, int>();
            private readonly nuint _numSpecialDistances;
            private readonly uint _maxChainLength;

            public HashChain(
                List<Token> data,
                nuint windowSize,
                nuint minLength,
                nuint maxLength,
                nuint distanceMultiplier,
                uint maxChainLength)
            {
                _size           = (nuint)data.Count;
                _windowSize     = windowSize;
                _windowMask     = windowSize - 1;
                _minLength      = minLength;
                _maxLength      = maxLength;
                _maxChainLength = maxChainLength;
                _data           = new uint[_size];

                for (int i = 0; i < data.Count; i++)
                {
                    _data[i] = data[i].Value;
                }

                _head = new int[KHashNumValues];
                Array.Fill(_head, -1);
                _val  = new int[_windowSize];
                Array.Fill(_val, -1);
                _chain  = new uint[_windowSize];
                _zeros  = new uint[_windowSize];
                _headz  = new int[_windowSize + 1];
                Array.Fill(_headz, -1);
                _chainz = new uint[_windowSize];

                for (uint i = 0; i < (uint)_windowSize; ++i)
                {
                    _chain[i]  = i;
                    _chainz[i] = i;
                }

                if (distanceMultiplier != 0)
                {
                    for (int i = ANSSymbolReader.KNumSpecialDistances - 1; i >= 0; --i)
                    {
                        _specialDistTable[ANSSymbolReader.SpecialDistance(i, (int)distanceMultiplier)] = i;
                    }

                    _numSpecialDistances = ANSSymbolReader.KNumSpecialDistances;
                }
            }

            public void Update(nuint pos)
            {
                uint hashval = GetHash(pos, _data) & KHashMask;
                uint wpos    = (uint)(pos & _windowMask);
                _val[wpos]   = (int)hashval;

                if (_head[hashval] != -1)
                {
                    _chain[wpos] = (uint)_head[hashval];
                }

                _head[hashval] = (int)wpos;

                if (pos > 0 && _data[pos] != _data[pos - 1])
                {
                    _numzeros = 0;
                }

                _numzeros    = CountZeros(pos, _numzeros);
                _zeros[wpos] = _numzeros;

                if (_headz[_numzeros] != -1)
                {
                    _chainz[wpos] = (uint)_headz[_numzeros];
                }

                _headz[_numzeros] = (int)wpos;
            }

            public void FindMatches(nuint pos, int maxDist, List<uint> distSymbols)
            {
                _ = maxDist;
                uint wpos        = (uint)(pos & _windowMask);
                uint hashval     = GetHash(pos, _data) & KHashMask;
                uint hashpos     = _chain[wpos];
                int prevDist     = 0;
                int end          = (int)Math.Min(pos + _maxLength, _size);
                uint chainlength = 0;
                uint bestLen     = 0;

                for (; ;)
                {
                    int dist = hashpos <= wpos
                        ? (int)(wpos - hashpos)
                        : (int)(wpos - hashpos + _windowMask + 1);

                    if (dist < prevDist)
                    {
                        break;
                    }

                    prevDist = dist;
                    uint len = 0;

                    if (dist > 0)
                    {
                        len = MeasureMatchAtDistance((int)pos, dist, end, hashpos, ref bestLen, distSymbols);
                    }

                    chainlength++;

                    if (chainlength >= _maxChainLength || !AdvanceHashChain(ref hashpos, hashval, len))
                    {
                        break;
                    }
                }
            }

            private uint CountZeros(nuint pos, uint prevzeros)
            {
                nuint end = Math.Min(pos + _windowSize, _size);

                if (prevzeros > 0)
                {
                    if (prevzeros >= _windowMask && _data[end - 1] == 0 && end == pos + _windowSize)
                    {
                        return prevzeros;
                    }

                    return prevzeros - 1;
                }

                uint num = 0;

                while (pos + num < end && _data[pos + num] == 0)
                {
                    num++;
                }

                return num;
            }

            private uint MeasureMatchAtDistance(
                int pos,
                int dist,
                int end,
                uint hashpos,
                ref uint bestLen,
                List<uint> distSymbols)
            {
                int i = pos;
                int j = pos - dist;

                if (_numzeros > 3)
                {
                    int r = (int)Math.Min(_numzeros - 1, _zeros[hashpos]);

                    if (i + r >= end)
                    {
                        r = end - i - 1;
                    }

                    i += r;
                    j += r;
                }

                while (i < end && _data[i] == _data[j])
                {
                    i++;
                    j++;
                }

                uint len = (uint)(i - pos);

                if (len >= _minLength && len + 2 >= bestLen)
                {
                    uint distSymbol = _specialDistTable.TryGetValue(dist, out int special)
                        ? (uint)special
                        : (uint)(_numSpecialDistances + (nuint)dist - 1);

                    while (distSymbols.Count <= (int)len)
                    {
                        distSymbols.Add(distSymbol);
                    }

                    if (distSymbol < distSymbols[(int)len])
                    {
                        distSymbols[(int)len] = distSymbol;
                    }

                    if (len > bestLen)
                    {
                        bestLen = len;
                    }
                }

                return len;
            }

            private bool AdvanceHashChain(ref uint hashpos, uint hashval, uint len)
            {
                if (_numzeros >= 3 && len > _numzeros)
                {
                    if (hashpos == _chainz[hashpos])
                    {
                        return false;
                    }

                    hashpos = _chainz[hashpos];

                    return _zeros[hashpos] == _numzeros;
                }

                if (hashpos == _chain[hashpos])
                {
                    return false;
                }

                hashpos = _chain[hashpos];

                return _val[hashpos] == (int)hashval;
            }
        }
    }
}
