// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Common parameters and alias table utilities for ANS and Prefix entropy coding.
    /// Ported from <c>lib/jxl/ans_params.h</c>, <c>lib/jxl/ans_common.h</c>, and <c>lib/jxl/ans_common.cc</c>.
    /// </summary>
    public static class AnsParams
    {
        // TODO(veluca): decide if 12 is the best constant here (valid range is up to
        // 16). This requires recomputing the Huffman tables in {enc,dec}_ans.cc
        // 14 gives a 0.2% improvement at d1 and makes d8 slightly worse. This is
        // likely not worth the increase in encoder complexity.

        /// <summary>Log2 of the ANS table size (12).</summary>
        public const uint AnsLogTabSize = 12u;

        /// <summary>ANS table size (4096).</summary>
        public const int AnsTabSize = 1 << (int)AnsLogTabSize;

        /// <summary>ANS table bitmask (4095).</summary>
        public const int AnsTabMask = AnsTabSize - 1;

        /// <summary>Largest possible symbol to be encoded by prefix coding (4096).</summary>
        public const int PrefixMaxAlphabetSize = 4096;

        /// <summary>Largest possible symbol to be encoded by ANS (256).</summary>
        public const int AnsMaxAlphabetSize = 256;

        /// <summary>Max number of bits for prefix coding (15).</summary>
        public const int PrefixMaxBits = 15;

        /// <summary>Initial ANS state, used as CRC signature (0x13).</summary>
        public const uint AnsSignature = 0x13u;
    }

    /// <summary>
    /// Alias table for O(1) ANS symbol lookup.
    /// Ported from <c>jxl::AliasTable</c> in <c>lib/jxl/ans_common.h</c> and <c>lib/jxl/ans_common.cc</c>.
    /// </summary>
    public static class AliasTable
    {
        /// <summary>
        /// Decoded ANS symbol entry returned by <see cref="Lookup"/>.
        /// </summary>
        public struct Symbol
        {
            /// <summary>Decoded symbol value.</summary>
            public nuint Value;

            /// <summary>Offset within the symbol's frequency range.</summary>
            public nuint Offset;

            /// <summary>Frequency of the symbol.</summary>
            public nuint Freq;
        }

        // Working set size matters here (~64 tables x 256 entries).
        // offsets0 is always zero (beginning of [0] side among the same symbol).
        // offsets1 is an offset of (pos >= cutoff) side decremented by cutoff.

        /// <summary>
        /// Compact 8-byte entry in an ANS alias table.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Entry
        {
            /// <summary>Cutoff threshold (<c>&lt; kEntrySizeMinus1</c> when used by ANS).</summary>
            public byte Cutoff;

            /// <summary>Right-hand symbol value (<c>&lt; alphabet size</c>).</summary>
            public byte RightValue;

            /// <summary>Frequency of symbol <c>i</c>.</summary>
            public ushort Freq0;

            // Only used if `greater` (see Lookup)

            /// <summary>Offset for <c>right_value</c> decremented by <see cref="Cutoff"/> (<c>&lt;= ANS_TAB_SIZE</c>).</summary>
            public ushort Offsets1;

            /// <summary><c>freq1 ^ freq0</c> for branchless selection in <see cref="Lookup"/>.</summary>
            public ushort Freq1XorFreq0;
        }

        /// <summary>
        /// Returns the precision (number of bits) that should be used to store
        /// a histogram count such that <c>Log2Floor(count) == logcount</c>.
        /// </summary>
        /// <param name="logcount">Floor of base-2 log of count.</param>
        /// <param name="shift">Distribution precision shift.</param>
        /// <returns>Number of precision bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetPopulationCountPrecision(uint logcount, uint shift)
        {
            int r = Math.Min(
                (int)logcount,
                (int)shift - (int)((AnsParams.AnsLogTabSize - logcount) >> 1));

            if (r < 0)
            {
                return 0;
            }

            return (uint)r;
        }

        /// <summary>
        /// Returns a histogram where the counts are positive, differ by at most 1,
        /// and add up to <paramref name="totalCount"/>. The bigger counts (if any) are at the beginning
        /// of the histogram.
        /// </summary>
        /// <param name="length">Histogram length.</param>
        /// <param name="totalCount">Sum of all counts.</param>
        /// <returns>Flat histogram array.</returns>
        public static int[] CreateFlatHistogram(int length, int totalCount)
        {
            Debug.Assert(length > 0);
            Debug.Assert(length <= totalCount);

            int count     = totalCount / length;
            int[] result  = new int[length];
            int remCounts = totalCount % length;

            Array.Fill(result, count);

            for (int i = 0; i < remCounts; ++i)
            {
                ++result[i];
            }

            return result;
        }

        /// <summary>
        /// Performs a constant-time symbol lookup in the alias <paramref name="table"/>.
        /// </summary>
        /// <param name="table">Alias table entries.</param>
        /// <param name="value">Input value in <c>[0, ANS_TAB_SIZE)</c>.</param>
        /// <param name="logEntrySize">Log2 of the entry size.</param>
        /// <param name="entrySizeMinus1">Bitmask <c>(1 &lt;&lt; logEntrySize) - 1</c>.</param>
        /// <returns>Resolved <see cref="Symbol"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Symbol Lookup(
            ReadOnlySpan<Entry> table,
            nuint value,
            nuint logEntrySize,
            nuint entrySizeMinus1)
        {
            nuint i   = value >> (int)logEntrySize;
            nuint pos = value & entrySizeMinus1;

            ref readonly Entry e  = ref table[(int)i];
            nuint cutoff          = e.Cutoff;
            nuint rightValue      = e.RightValue;
            nuint freq0           = e.Freq0;
            bool greater          = pos >= cutoff;
            nuint offsets1Or0     = greater ? e.Offsets1 : 0u;
            nuint freq1XorFreq0   = greater ? e.Freq1XorFreq0 : 0u;

            Symbol s;
            s.Value  = greater ? rightValue : i;
            s.Offset = offsets1Or0 + pos;
            s.Freq   = freq0 ^ freq1XorFreq0;

            return s;
        }

        /// <summary>
        /// Performs a constant-time symbol lookup in <paramref name="tables"/> starting at <paramref name="tableOffset"/>.
        /// </summary>
        /// <param name="tables">Flat array of alias tables.</param>
        /// <param name="tableOffset">Start index of the histogram's alias table.</param>
        /// <param name="value">Input value in <c>[0, ANS_TAB_SIZE)</c>.</param>
        /// <param name="logEntrySize">Log2 of the entry size.</param>
        /// <param name="entrySizeMinus1">Bitmask <c>(1 &lt;&lt; logEntrySize) - 1</c>.</param>
        /// <returns>Resolved <see cref="Symbol"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Symbol LookupAtOffset(
            Entry[] tables,
            nuint tableOffset,
            nuint value,
            nuint logEntrySize,
            nuint entrySizeMinus1)
        {
            nuint i   = value >> (int)logEntrySize;
            nuint pos = value & entrySizeMinus1;

            ref readonly Entry e  = ref tables[(int)(tableOffset + i)];
            nuint cutoff          = e.Cutoff;
            nuint rightValue      = e.RightValue;
            nuint freq0           = e.Freq0;
            bool greater          = pos >= cutoff;
            nuint offsets1Or0     = greater ? e.Offsets1 : 0u;
            nuint freq1XorFreq0   = greater ? e.Freq1XorFreq0 : 0u;

            Symbol s;
            s.Value  = greater ? rightValue : i;
            s.Offset = offsets1Or0 + pos;
            s.Freq   = freq0 ^ freq1XorFreq0;

            return s;
        }

        /// <summary>
        /// Computes an alias table for a given probability <paramref name="distribution"/>.
        /// </summary>
        /// <param name="distribution">Symbol frequency counts summing to <c>1 &lt;&lt; logRange</c>.</param>
        /// <param name="logRange">Log2 of the total distribution sum.</param>
        /// <param name="logAlphaSize">Log2 of the alias table size.</param>
        /// <param name="a">Output alias table span of length at least <c>1 &lt;&lt; logAlphaSize</c>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InitAliasTable(
            ReadOnlySpan<int> distribution,
            uint logRange,
            nuint logAlphaSize,
            Span<Entry> a)
        {
            uint range      = 1u << (int)logRange;
            nuint tableSize = 1u << (int)logAlphaSize;

            if (tableSize > range)
            {
                return Status.Failure("AliasTable table_size > range");
            }

            List<int> dist = TrimTrailingZeros(distribution, (int)range);

            if ((nuint)dist.Count > tableSize)
            {
                return Status.Failure("AliasTable distribution size > table_size");
            }

            uint entrySize = range >> (int)logAlphaSize; // this is exact

            if (!ValidateDistributionSum(dist, range, out int singleSymbol))
            {
                return Status.Failure("AliasTable distribution sum mismatch");
            }

            if (singleSymbol != -1)
            {
                return FillSingleSymbolTable(singleSymbol, tableSize, entrySize, a);
            }

            BalanceAliasBuckets(dist, tableSize, entrySize, a, out uint[] cutoffs);
            FinalizeAliasEntries(dist, tableSize, entrySize, cutoffs, a);

            return true;
        }

        private static List<int> TrimTrailingZeros(ReadOnlySpan<int> distribution, int range)
        {
            int len = distribution.Length;

            while (len > 0 && distribution[len - 1] == 0)
            {
                len--;
            }

            List<int> dist = new List<int>(Math.Max(len, 1));

            for (int i = 0; i < len; i++)
            {
                dist.Add(distribution[i]);
            }

            // Ensure that a valid table is always returned, even for an empty
            // alphabet. Otherwise, a specially-crafted stream might crash the
            // decoder.
            if (dist.Count == 0)
            {
                dist.Add(range);
            }

            return dist;
        }

        private static Status ValidateDistributionSum(List<int> dist, uint range, out int singleSymbol)
        {
            singleSymbol = -1;
            int sum      = 0;

            for (int sym = 0; sym < dist.Count; sym++)
            {
                int v = dist[sym];
                sum  += v;

                if (v == AnsParams.AnsTabSize)
                {
                    if (singleSymbol != -1)
                    {
                        return false;
                    }

                    singleSymbol = sym;
                }
            }

            return (uint)sum == range;
        }

        private static Status FillSingleSymbolTable(
            int singleSymbol,
            nuint tableSize,
            uint entrySize,
            Span<Entry> a)
        {
            byte sym = (byte)singleSymbol;

            if (singleSymbol != sym)
            {
                return false;
            }

            for (nuint i = 0; i < tableSize; i++)
            {
                a[(int)i].RightValue    = sym;
                a[(int)i].Cutoff        = 0;
                a[(int)i].Offsets1      = (ushort)(entrySize * (uint)i);
                a[(int)i].Freq0         = 0;
                a[(int)i].Freq1XorFreq0 = AnsParams.AnsTabSize;
            }

            return true;
        }

        private static void BalanceAliasBuckets(
            List<int> dist,
            nuint tableSize,
            uint entrySize,
            Span<Entry> a,
            out uint[] cutoffs)
        {
            List<uint> underfullPosn = new List<uint>();
            List<uint> overfullPosn  = new List<uint>();
            cutoffs                  = new uint[(int)tableSize];

            for (int i = 0; i < dist.Count; i++)
            {
                cutoffs[i] = (uint)dist[i];

                if (cutoffs[i] > entrySize)
                {
                    overfullPosn.Add((uint)i);
                }
                else if (cutoffs[i] < entrySize)
                {
                    underfullPosn.Add((uint)i);
                }
            }

            for (uint i = (uint)dist.Count; i < (uint)tableSize; i++)
            {
                cutoffs[i] = 0;
                underfullPosn.Add(i);
            }

            while (overfullPosn.Count > 0)
            {
                uint overfullI = overfullPosn[overfullPosn.Count - 1];
                overfullPosn.RemoveAt(overfullPosn.Count - 1);

                uint underfullI = underfullPosn[underfullPosn.Count - 1];
                underfullPosn.RemoveAt(underfullPosn.Count - 1);

                uint underfullBy     = entrySize - cutoffs[underfullI];
                cutoffs[overfullI]  -= underfullBy;

                a[(int)underfullI].RightValue = (byte)overfullI;
                a[(int)underfullI].Offsets1   = (ushort)cutoffs[overfullI];

                if (cutoffs[overfullI] < entrySize)
                {
                    underfullPosn.Add(overfullI);
                }
                else if (cutoffs[overfullI] > entrySize)
                {
                    overfullPosn.Add(overfullI);
                }
            }
        }

        private static void FinalizeAliasEntries(
            List<int> dist,
            nuint tableSize,
            uint entrySize,
            uint[] cutoffs,
            Span<Entry> a)
        {
            for (uint i = 0; i < (uint)tableSize; i++)
            {
                int idx = (int)i;

                if (cutoffs[idx] == entrySize)
                {
                    a[idx].RightValue = (byte)i;
                    a[idx].Offsets1   = 0;
                    a[idx].Cutoff     = 0;
                }
                else
                {
                    a[idx].Offsets1 = (ushort)(a[idx].Offsets1 - cutoffs[idx]);
                    a[idx].Cutoff   = (byte)cutoffs[idx];
                }

                int freq0 = idx < dist.Count ? dist[idx] : 0;
                int i1    = a[idx].RightValue;
                int freq1 = i1 < dist.Count ? dist[i1] : 0;

                a[idx].Freq0         = (ushort)freq0;
                a[idx].Freq1XorFreq0 = (ushort)(freq1 ^ freq0);
            }
        }
    }
}
