// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// AC coefficient context modeling constants and zero-density lookup tables.
    /// Ported from <c>lib/jxl/ac_context.h</c>.
    /// </summary>
    public static class AcContext
    {
        /// <summary>Block context used for scanning order, number of non-zeros, AC coefficients.</summary>
        public const uint KDctOrderContextStart = 0;

        /// <summary>Number of buckets for predicted non-zeros (37).</summary>
        public const uint KNonZeroBuckets = 37;

        /// <summary>Supremum of <c>ZeroDensityContext(x, y) + 1</c> when <c>x + y &lt; 64</c> (458).</summary>
        public const int KZeroDensityContextCount = 458;

        /// <summary>Supremum of <c>ZeroDensityContext(x, y) + 1</c> (474).</summary>
        public const int KZeroDensityContextLimit = 474;

        private static readonly ushort[] CoeffFreqContext =
        {
            0xBAD, 0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14,
            15,    15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
            23,    23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26,
            27,    27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30,
        };

        private static readonly ushort[] CoeffNumNonzeroContext =
        {
            0xBAD, 0,   31,  62,  62,  93,  93,  93,  93,  123, 123, 123, 123,
            152,   152, 152, 152, 152, 152, 152, 152, 180, 180, 180, 180, 180,
            180,   180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
            206,   206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
            206,   206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
        };

        /// <summary>
        /// Computes the pre-clustered zero-density context index for AC coefficient coding.
        /// </summary>
        /// <param name="nonzerosLeft">Remaining non-zero coefficients in the block.</param>
        /// <param name="k">Coefficient index in scan order.</param>
        /// <param name="coveredBlocks">Number of 8x8 blocks covered by the transform.</param>
        /// <param name="log2CoveredBlocks">Log2 of <paramref name="coveredBlocks"/>.</param>
        /// <param name="prev">Whether the previous coefficient was non-zero (0 or 1).</param>
        /// <returns>Zero-density context offset.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint ZeroDensityContext(
            nuint nonzerosLeft,
            nuint k,
            nuint coveredBlocks,
            nuint log2CoveredBlocks,
            nuint prev)
        {
            Debug.Assert((1u << (int)log2CoveredBlocks) == coveredBlocks);
            nonzerosLeft = (nonzerosLeft + coveredBlocks - 1u) >> (int)log2CoveredBlocks;
            k          >>= (int)log2CoveredBlocks;
            Debug.Assert(k > 0 && k < 64);
            Debug.Assert(nonzerosLeft > 0 && nonzerosLeft < 64);

            return (nuint)(((CoeffNumNonzeroContext[(int)nonzerosLeft] + CoeffFreqContext[(int)k]) * 2) + (int)prev);
        }
    }

    /// <summary>
    /// Block context map for AC coefficient entropy coding.
    /// Ported from <c>jxl::BlockCtxMap</c> in <c>lib/jxl/ac_context.h</c>.
    /// </summary>
    public sealed class BlockCtxMap
    {
        /// <summary>Default context map clustering large transforms together.</summary>
        public static ReadOnlySpan<byte> DefaultCtxMap => new byte[]
        {
            0, 1, 2, 2, 3,  3,  4,  5,  6,  6,  6,  6,  6,
            7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
            7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
        };

        /// <summary>DC bucket thresholds per channel (X, Y, B).</summary>
        public int[][] DcThresholds =
        {
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
        };

        /// <summary>Quantizer field thresholds.</summary>
        public uint[] QfThresholds = Array.Empty<uint>();

        /// <summary>Flattened context map lookup table.</summary>
        public byte[] CtxMap;

        /// <summary>Number of distinct block contexts.</summary>
        public nuint NumCtxs;

        /// <summary>Number of DC contexts.</summary>
        public nuint NumDcCtxs;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockCtxMap"/> class with default context mappings.
        /// </summary>
        public BlockCtxMap()
        {
            CtxMap    = DefaultCtxMap.ToArray();
            NumCtxs   = 15;
            NumDcCtxs = 1;
        }

        /// <summary>
        /// Resets this instance to the default context map.
        /// </summary>
        public void ResetToDefault()
        {
            DcThresholds[0] = Array.Empty<int>();
            DcThresholds[1] = Array.Empty<int>();
            DcThresholds[2] = Array.Empty<int>();
            QfThresholds    = Array.Empty<uint>();
            CtxMap          = DefaultCtxMap.ToArray();
            NumCtxs         = 15;
            NumDcCtxs       = 1;
        }

        /// <summary>
        /// Computes the block context index for a given DC index, quantizer factor, order ID, and channel.
        /// </summary>
        /// <param name="dcIdx">DC bucket index.</param>
        /// <param name="qf">Quantizer field value.</param>
        /// <param name="ord">Coefficient order index.</param>
        /// <param name="c">Channel index (0, 1, or 2).</param>
        /// <returns>Clustered block context index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint Context(int dcIdx, uint qf, nuint ord, nuint c)
        {
            nuint qfIdx = 0;

            for (int i = 0; i < QfThresholds.Length; i++)
            {
                if (qf > QfThresholds[i])
                {
                    qfIdx++;
                }
            }

            nuint idx = c < 2 ? c ^ 1u : 2u;
            idx       = (idx * FrameDimensions.KNumOrders) + ord;
            idx       = (idx * ((nuint)QfThresholds.Length + 1u)) + qfIdx;
            idx       = (idx * NumDcCtxs) + (nuint)dcIdx;

            return CtxMap[(int)idx];
        }

        /// <summary>
        /// Returns the offset of zero-density contexts for <paramref name="blockCtx"/>.
        /// </summary>
        /// <param name="blockCtx">Block context index.</param>
        /// <returns>Zero-density context base offset.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ZeroDensityContextsOffset(uint blockCtx)
        {
            return (uint)((NumCtxs * AcContext.KNonZeroBuckets) + ((nuint)AcContext.KZeroDensityContextCount * blockCtx));
        }

        /// <summary>
        /// Returns the total number of AC contexts across all block contexts.
        /// </summary>
        /// <returns>Total AC contexts.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NumACContexts()
        {
            return (uint)(NumCtxs * (AcContext.KNonZeroBuckets + (nuint)AcContext.KZeroDensityContextCount));
        }

        /// <summary>
        /// Computes the context index for the number of non-zeros in a block.
        /// </summary>
        /// <param name="nonZeros">Predicted number of non-zeros.</param>
        /// <param name="blockCtx">Block context index.</param>
        /// <returns>Non-zero context index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NonZeroContext(uint nonZeros, uint blockCtx)
        {
            if (nonZeros >= 64)
            {
                nonZeros = 64;
            }

            uint ctx = nonZeros < 8 ? nonZeros : 4u + (nonZeros / 2u);

            return (uint)((ctx * NumCtxs) + blockCtx);
        }
    }

    /// <summary>
    /// Entropy coding and context modeling helpers for DC/AC coefficients and block context maps.
    /// Ported from <c>lib/jxl/entropy_coder.h</c> and <c>lib/jxl/entropy_coder.cc</c>.
    /// </summary>
    public static class EntropyCoder
    {
        /// <summary>U32 distribution for DC threshold encoding.</summary>
        public static readonly U32Enc DcThresholdDist = new U32Enc(
            U32Distr.Bits(4),
            U32Distr.BitsOffset(8, 16),
            U32Distr.BitsOffset(16, 272),
            U32Distr.BitsOffset(32, 65808));

        /// <summary>U32 distribution for quantizer field threshold encoding.</summary>
        public static readonly U32Enc QfThresholdDist = new U32Enc(
            U32Distr.Bits(2),
            U32Distr.BitsOffset(3, 4),
            U32Distr.BitsOffset(5, 12),
            U32Distr.BitsOffset(8, 44));

        /// <summary>
        /// Predicts a value from the top and left neighbors.
        /// </summary>
        /// <param name="rowTop">Top row (empty if <c>y == 0</c>).</param>
        /// <param name="row">Current row.</param>
        /// <param name="x">Column index.</param>
        /// <param name="defaultVal">Fallback value when <c>x == 0</c> and <paramref name="rowTop"/> is empty.</param>
        /// <returns>Predicted 32-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PredictFromTopAndLeft(
            ReadOnlySpan<int> rowTop,
            ReadOnlySpan<int> row,
            int x,
            int defaultVal)
        {
            if (x == 0)
            {
                return rowTop.IsEmpty ? defaultVal : rowTop[x];
            }

            if (rowTop.IsEmpty)
            {
                return row[x - 1];
            }

            return (rowTop[x] + row[x - 1] + 1) / 2;
        }

        /// <summary>
        /// Decodes a <see cref="BlockCtxMap"/> from <paramref name="br"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="blockCtxMap">Destination <see cref="BlockCtxMap"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeBlockCtxMap(
            JxlMemoryManager? memoryManager,
            BitReader br,
            BlockCtxMap blockCtxMap)
        {
            bool isDefault = br.ReadFixedBits(1) != 0;

            if (isDefault)
            {
                blockCtxMap.ResetToDefault();

                return true;
            }

            blockCtxMap.NumDcCtxs = 1;

            for (int j = 0; j < 3; j++)
            {
                int len                      = (int)br.ReadFixedBits(4);
                blockCtxMap.DcThresholds[j]  = new int[len];
                blockCtxMap.NumDcCtxs       *= (nuint)len + 1u;

                for (int i = 0; i < len; i++)
                {
                    blockCtxMap.DcThresholds[j][i] = PackSigned.Unpack(U32Coder.Read(DcThresholdDist, br));
                }
            }

            int qfLen                = (int)br.ReadFixedBits(4);
            blockCtxMap.QfThresholds = new uint[qfLen];

            for (int i = 0; i < qfLen; i++)
            {
                blockCtxMap.QfThresholds[i] = U32Coder.Read(QfThresholdDist, br) + 1u;
            }

            if (blockCtxMap.NumDcCtxs * ((nuint)qfLen + 1u) > 64)
            {
                return Status.Failure("Invalid block context map: too big");
            }

            int ctxMapSize     = (int)(3u * FrameDimensions.KNumOrders * blockCtxMap.NumDcCtxs * ((nuint)qfLen + 1u));
            blockCtxMap.CtxMap = new byte[ctxMapSize];

            if (!DecContextMap.DecodeContextMap(memoryManager, blockCtxMap.CtxMap, out blockCtxMap.NumCtxs, br))
            {
                return false;
            }

            if (blockCtxMap.NumCtxs > 16)
            {
                return Status.Failure("Invalid block context map: too many distinct contexts");
            }

            return true;
        }
    }
}
