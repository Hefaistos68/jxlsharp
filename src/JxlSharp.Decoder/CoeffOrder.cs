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
    /// Permutation to/from factorial base representation (Lehmer code).
    /// Ported from <c>lib/jxl/lehmer_code.h</c>.
    /// </summary>
    public static class LehmerCode
    {
        /// <summary>
        /// Isolates the value of the lowest 1-bit of <paramref name="t"/>.
        /// </summary>
        /// <param name="t">Input 32-bit signed integer.</param>
        /// <returns><c>t &amp; -t</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ValueOfLowest1Bit(int t) => t & -t;

        /// <summary>
        /// Isolates the value of the lowest 1-bit of <paramref name="t"/>.
        /// </summary>
        /// <param name="t">Input 32-bit unsigned integer.</param>
        /// <returns><c>t &amp; (uint)-(int)t</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ValueOfLowest1Bit(uint t) => t & (uint)-(int)t;

        /// <summary>
        /// Isolates the value of the lowest 1-bit of <paramref name="t"/>.
        /// </summary>
        /// <param name="t">Input native unsigned integer.</param>
        /// <returns><c>t &amp; (nuint)-(nint)t</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint ValueOfLowest1Bit(nuint t) => t & (nuint)(-(nint)t);

        /// <summary>
        /// Computes the Lehmer (factorial basis) code of <paramref name="permutation"/>, an array of <paramref name="n"/>
        /// unique indices in <c>[0..n)</c>, and stores it in <paramref name="code"/>. <c>O(n log n)</c> time.
        /// <paramref name="temp"/> must have <c>n + 1</c> elements but need not be initialized.
        /// </summary>
        /// <param name="permutation">Input permutation indices.</param>
        /// <param name="temp">Scratch Fenwick tree buffer of length at least <c>n + 1</c>.</param>
        /// <param name="n">Number of elements in the permutation.</param>
        /// <param name="code">Output Lehmer code buffer of length at least <paramref name="n"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ComputeLehmerCode(
            ReadOnlySpan<uint> permutation,
            Span<uint> temp,
            nuint n,
            Span<uint> code)
        {
            temp.Slice(0, (int)(n + 1)).Clear();

            for (nuint idx = 0; idx < n; ++idx)
            {
                uint s       = permutation[(int)idx];
                uint penalty = SumFenwick(temp, s + 1);

                if (s < penalty)
                {
                    return Status.Failure("Invalid permutation element in ComputeLehmerCode");
                }

                code[(int)idx] = s - penalty;
                AddFenwick(temp, s + 1, n + 1);
            }

            return true;
        }

        /// <summary>
        /// Decodes the Lehmer code in <paramref name="code"/> into <paramref name="permutation"/>.
        /// <paramref name="temp"/> must have <c>1 &lt;&lt; CeilLog2(n)</c> elements but need not be initialized.
        /// </summary>
        /// <param name="code">Input Lehmer code of length at least <paramref name="n"/>.</param>
        /// <param name="temp">Scratch implicit order-statistics tree of length at least <c>1 &lt;&lt; CeilLog2Nonzero(n)</c>.</param>
        /// <param name="n">Number of elements in the permutation.</param>
        /// <param name="permutation">Output permutation indices of length at least <paramref name="n"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeLehmerCode(
            ReadOnlySpan<uint> code,
            Span<uint> temp,
            nuint n,
            Span<uint> permutation)
        {
            if (n == 0)
            {
                return Status.Failure("Empty Lehmer code");
            }

            nuint log2n   = (nuint)Bits.CeilLog2Nonzero((ulong)n);
            nuint paddedN = 1u << (int)log2n;

            for (nuint i = 0; i < paddedN; i++)
            {
                int i1       = (int)(i + 1);
                temp[(int)i] = (uint)ValueOfLowest1Bit(i1);
            }

            for (nuint i = 0; i < n; i++)
            {
                if (code[(int)i] + i >= n)
                {
                    return Status.Failure("Invalid Lehmer code rank");
                }

                uint rank = code[(int)i] + 1;

                if (!ExtractUnusedElement(rank, paddedN, log2n, temp, out nuint next))
                {
                    return false;
                }

                permutation[(int)i] = (uint)next;
                MarkElementUsed(next + 1, paddedN, temp);
            }

            return true;
        }

        private static uint SumFenwick(ReadOnlySpan<uint> temp, uint i)
        {
            uint penalty = 0;

            while (i != 0)
            {
                penalty += temp[(int)i];
                i       &= i - 1; // clear lowest bit
            }

            return penalty;
        }

        private static void AddFenwick(Span<uint> temp, uint i, nuint limit)
        {
            while (i < (uint)limit)
            {
                temp[(int)i] += 1;
                i            += ValueOfLowest1Bit(i);
            }
        }

        private static Status ExtractUnusedElement(
            uint rank,
            nuint paddedN,
            nuint log2n,
            ReadOnlySpan<uint> temp,
            out nuint next)
        {
            // Extract i-th unused element via implicit order-statistics tree.
            nuint bit = paddedN;
            next      = 0;

            for (nuint b = 0; b <= log2n; b++)
            {
                nuint cand = next + bit;

                if (cand < 1)
                {
                    return Status.Failure("Invalid candidate in Lehmer tree");
                }

                bit >>= 1;

                if (temp[(int)(cand - 1)] < rank)
                {
                    next  = cand;
                    rank -= temp[(int)(cand - 1)];
                }
            }

            return true;
        }

        private static void MarkElementUsed(nuint next, nuint paddedN, Span<uint> temp)
        {
            while (next <= paddedN)
            {
                temp[(int)(next - 1)] -= 1;
                next                  += ValueOfLowest1Bit(next);
            }
        }
    }

    /// <summary>
    /// Coefficient ordering layout, offsets, and ANS permutation decoding.
    /// Ported from <c>lib/jxl/coeff_order_fwd.h</c>, <c>lib/jxl/coeff_order.h</c>, and <c>lib/jxl/coeff_order.cc</c>.
    /// </summary>
    public static class CoeffOrder
    {
        // Maximum number of orders to be used. Note that this needs to be multiplied by
        // the number of channels. One per "size class" (plus one extra for DCT8),
        // shared between transforms of size XxY and of size YxX.

        /// <summary>Maximum number of order buckets per channel (13).</summary>
        public const byte kNumOrders = 13;

        // Those offsets get multiplied by kDCTBlockSize.

        /// <summary>Total 8x8 blocks across all 13 order buckets and 3 channels (6156).</summary>
        public const nuint kCoeffOrderLimit = 6156;

        /// <summary>Maximum number of coefficient order entries across all buckets and channels (393984).</summary>
        public const nuint kCoeffOrderMaxSize = kCoeffOrderLimit * FrameDimensions.kDCTBlockSize;

        /// <summary>Number of ANS contexts used for permutation decoding (8).</summary>
        public const uint kPermutationContexts = 8;

        private static readonly HybridUintConfig OrderContextConfig = new HybridUintConfig(0, 0, 0);

        /// <summary>
        /// Cumulative block offsets for each <c>(order_bucket * 3 + channel)</c> pair.
        /// Multiply by <see cref="FrameDimensions.kDCTBlockSize"/> to obtain coefficient offsets.
        /// </summary>
        public static ReadOnlySpan<nuint> CoeffOrderOffsets => new nuint[]
        {
            0,    1,    2,    3,    4,    5,    6,    10,   14,   18,
            34,   50,   66,   68,   70,   72,   76,   80,   84,   92,
            100,  108,  172,  236,  300,  332,  364,  396,  652,  908,
            1164, 1292, 1420, 1548, 2572, 3596, 4620, 5132, 5644, kCoeffOrderLimit,
        };

        /// <summary>
        /// Mapping from <see cref="AcStrategyType"/> index to order bucket index in <c>[0, 12]</c>.
        /// Strategies with different natural orders have different buckets.
        /// </summary>
        public static ReadOnlySpan<byte> StrategyOrder => new byte[]
        {
            0, 1, 1, 1, 2, 3, 4, 4, 5,  5,  6,  6,  1,  1,
            1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
        };

        /// <summary>
        /// Returns the coefficient offset into the flat order array for bucket <paramref name="orderBucket"/> and <paramref name="channel"/>.
        /// </summary>
        /// <param name="orderBucket">Order bucket index in <c>[0..kNumOrders]</c>.</param>
        /// <param name="channel">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Offset in number of coefficients.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint CoeffOrderOffset(int orderBucket, int channel)
        {
            return CoeffOrderOffsets[3 * orderBucket + channel] * FrameDimensions.kDCTBlockSize;
        }

        // DCT coefficients are laid out in such a way that the number of rows of
        // coefficients is always the smaller coordinate.

        /// <summary>
        /// Returns the canonical number of coefficient rows (always the smaller dimension).
        /// </summary>
        /// <param name="rows">Block rows.</param>
        /// <param name="columns">Block columns.</param>
        /// <returns><c>min(rows, columns)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint CoefficientRows(nuint rows, nuint columns)
        {
            return rows < columns ? rows : columns;
        }

        /// <summary>
        /// Returns the canonical number of coefficient columns (always the larger dimension).
        /// </summary>
        /// <param name="rows">Block rows.</param>
        /// <param name="columns">Block columns.</param>
        /// <returns><c>max(rows, columns)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint CoefficientColumns(nuint rows, nuint columns)
        {
            return rows < columns ? columns : rows;
        }

        /// <summary>
        /// Normalizes (<paramref name="rows"/>, <paramref name="columns"/>) so that <paramref name="rows"/> &lt;= <paramref name="columns"/>.
        /// </summary>
        /// <param name="rows">Block rows, updated in-place to the smaller dimension.</param>
        /// <param name="columns">Block columns, updated in-place to the larger dimension.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CoefficientLayout(ref nuint rows, ref nuint columns)
        {
            nuint r = rows;
            nuint c = columns;
            rows    = CoefficientRows(r, c);
            columns = CoefficientColumns(r, c);
        }

        /// <summary>
        /// Computes the ANS context index for a Lehmer code value or permutation size.
        /// </summary>
        /// <param name="val">Previous Lehmer value or permutation size.</param>
        /// <returns>Context index in <c>[0, kPermutationContexts - 1]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CoeffOrderContext(uint val)
        {
            OrderContextConfig.Encode(val, out uint token, out _, out _);

            return Math.Min(token, kPermutationContexts - 1);
        }

        /// <summary>
        /// Decodes a standalone permutation of length <paramref name="size"/> starting at <paramref name="skip"/> from <paramref name="br"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary allocations.</param>
        /// <param name="skip">Number of leading identity elements to skip.</param>
        /// <param name="size">Total permutation size.</param>
        /// <param name="order">Output permutation span, or empty if only consuming the bitstream.</param>
        /// <param name="br">BitReader positioned at the permutation histogram.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodePermutation(
            JxlMemoryManager? memoryManager,
            nuint skip,
            nuint size,
            Span<uint> order,
            BitReader br)
        {
            ANSCode code = new ANSCode();

            if (!DecAns.DecodeHistograms(memoryManager, br, kPermutationContexts, code, out byte[] contextMap))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerResult = ANSSymbolReader.Create(code, br);

            if (!readerResult.IsOk)
            {
                return readerResult.Status;
            }

            ANSSymbolReader reader = readerResult.Value;

            if (!ReadPermutation(skip, size, order, !order.IsEmpty, br, reader, contextMap))
            {
                return false;
            }

            if (!reader.CheckANSFinalState())
            {
                return Status.Failure("Invalid ANS stream");
            }

            return true;
        }

        /// <summary>
        /// Decodes all coefficient orders indicated by <paramref name="usedOrders"/> and populates natural orders for <paramref name="usedAcs"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary allocations.</param>
        /// <param name="usedOrders">Bitmask of order buckets with custom permutations in the bitstream.</param>
        /// <param name="usedAcs">Bitmask of <see cref="AcStrategyType"/> values used in the frame.</param>
        /// <param name="order">Destination flat coefficient order buffer of length at least <see cref="kCoeffOrderMaxSize"/>.</param>
        /// <param name="br">BitReader positioned at the coefficient order stream.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeCoeffOrders(
            JxlMemoryManager? memoryManager,
            ushort usedOrders,
            uint usedAcs,
            Span<uint> order,
            BitReader br)
        {
            byte[] contextMap       = Array.Empty<byte>();
            ANSSymbolReader? reader = null;

            // Bitstream does not have histograms if no coefficient order is used.
            if (usedOrders != 0)
            {
                ANSCode code = new ANSCode();

                if (!DecAns.DecodeHistograms(memoryManager, br, kPermutationContexts, code, out contextMap))
                {
                    return false;
                }

                StatusOr<ANSSymbolReader> readerResult = ANSSymbolReader.Create(code, br);

                if (!readerResult.IsOk)
                {
                    return readerResult.Status;
                }

                reader = readerResult.Value;
            }

            uint acsMask        = ComputeAcsMask(usedAcs);
            ushort computed     = 0;
            uint[] naturalOrder = Array.Empty<uint>();

            for (byte o = 0; o < AcStrategy.kNumValidStrategies; ++o)
            {
                byte ord = StrategyOrder[o];

                if ((computed & (1u << ord)) != 0)
                {
                    continue;
                }

                computed |= (ushort)(1u << ord);

                CoeffOrderDecodeContext ctx = new CoeffOrderDecodeContext(order, br, reader, contextMap);

                if (!ProcessStrategyOrderBucket(o, ord, usedOrders, acsMask, ref naturalOrder, in ctx))
                {
                    return false;
                }
            }

            if (usedOrders != 0 && !reader!.CheckANSFinalState())
            {
                return Status.Failure("Invalid ANS stream");
            }

            return true;
        }

        private readonly ref struct CoeffOrderDecodeContext
        {
            public readonly Span<uint> Order;
            public readonly BitReader Br;
            public readonly ANSSymbolReader? Reader;
            public readonly ReadOnlySpan<byte> ContextMap;

            public CoeffOrderDecodeContext(
                Span<uint> order,
                BitReader br,
                ANSSymbolReader? reader,
                ReadOnlySpan<byte> contextMap)
            {
                Order      = order;
                Br         = br;
                Reader     = reader;
                ContextMap = contextMap;
            }
        }

        private static uint ComputeAcsMask(uint usedAcs)
        {
            uint acsMask              = 0;
            ReadOnlySpan<byte> sOrder = StrategyOrder;

            for (byte o = 0; o < AcStrategy.kNumValidStrategies; ++o)
            {
                if ((usedAcs & (1u << o)) != 0)
                {
                    acsMask |= 1u << sOrder[o];
                }
            }

            return acsMask;
        }

        private static Status ProcessStrategyOrderBucket(
            byte rawStrategy,
            byte ord,
            ushort usedOrders,
            uint acsMask,
            ref uint[] naturalOrder,
            in CoeffOrderDecodeContext ctx)
        {
            AcStrategy acs = AcStrategy.FromRawStrategy(rawStrategy);
            bool used      = (acsMask & (1u << ord)) != 0;
            bool hasCustom = (usedOrders & (1u << ord)) != 0;
            nuint llf      = acs.CoveredBlocksX() * acs.CoveredBlocksY();
            nuint size     = FrameDimensions.kDCTBlockSize * llf;

            if (used || hasCustom)
            {
                if ((nuint)naturalOrder.Length < size)
                {
                    Array.Resize(ref naturalOrder, (int)size);
                }

                acs.ComputeNaturalCoeffOrder(naturalOrder.AsSpan(0, (int)size));
            }

            if (!hasCustom)
            {
                if (used)
                {
                    CopyNaturalOrderToChannels(ord, size, naturalOrder, ctx.Order);
                }

                return true;
            }

            return DecodeCustomBucketChannels(acs, ord, used, naturalOrder, in ctx);
        }

        private static void CopyNaturalOrderToChannels(
            byte ord,
            nuint size,
            ReadOnlySpan<uint> naturalOrder,
            Span<uint> order)
        {
            ReadOnlySpan<uint> src = naturalOrder.Slice(0, (int)size);

            for (int c = 0; c < 3; c++)
            {
                nuint offset = CoeffOrderOffset(ord, c);
                src.CopyTo(order.Slice((int)offset, (int)size));
            }
        }

        private static Status DecodeCustomBucketChannels(
            AcStrategy acs,
            byte ord,
            bool used,
            ReadOnlySpan<uint> naturalOrder,
            in CoeffOrderDecodeContext ctx)
        {
            nuint llf  = acs.CoveredBlocksX() * acs.CoveredBlocksY();
            nuint size = FrameDimensions.kDCTBlockSize * llf;

            for (int c = 0; c < 3; c++)
            {
                Span<uint> dest = used
                    ? ctx.Order.Slice((int)CoeffOrderOffset(ord, c), (int)size)
                    : Span<uint>.Empty;

                if (!DecodeCoeffOrder(llf, size, dest, used, naturalOrder, in ctx))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status DecodeCoeffOrder(
            nuint llf,
            nuint size,
            Span<uint> order,
            bool hasOrder,
            ReadOnlySpan<uint> naturalOrder,
            in CoeffOrderDecodeContext ctx)
        {
            if (!ReadPermutation(llf, size, order, hasOrder, ctx.Br, ctx.Reader!, ctx.ContextMap))
            {
                return false;
            }

            if (!hasOrder)
            {
                return true;
            }

            for (int k = 0; k < (int)size; ++k)
            {
                order[k] = naturalOrder[(int)order[k]];
            }

            return true;
        }

        internal static Status ReadPermutation(
            nuint skip,
            nuint size,
            Span<uint> order,
            bool hasOrder,
            BitReader br,
            ANSSymbolReader reader,
            ReadOnlySpan<byte> contextMap)
        {
            uint[] lehmer = new uint[(int)size];
            // temp space needs to be as large as the next power of 2, so doubling the
            // allocated size is enough.
            uint[] temp   = new uint[(int)(size * 2)];
            nuint end     = reader.ReadHybridUint(CoeffOrderContext((uint)size), br, contextMap) + skip;

            if (end > size)
            {
                return Status.Failure("Invalid permutation size");
            }

            uint last = 0;

            for (nuint i = skip; i < end; ++i)
            {
                uint val       = reader.ReadHybridUint(CoeffOrderContext(last), br, contextMap);
                lehmer[(int)i] = val;
                last           = val;

                if (val >= size - i)
                {
                    return Status.Failure("Invalid lehmer code");
                }
            }

            if (!hasOrder)
            {
                return true;
            }

            return LehmerCode.DecodeLehmerCode(lehmer, temp, size, order);
        }
    }
}
