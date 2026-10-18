// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Permutation &lt;=&gt; factorial base representation (Lehmer code)
    /// (ported from <c>lib/jxl/lehmer_code.h</c>).
    /// </summary>
    public static class LehmerCode
    {
        /// <summary>
        /// Returns the value of the lowest set bit (<c>t &amp; -t</c>).
        /// </summary>
        /// <param name="t">Input value.</param>
        /// <returns>Lowest set bit value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ValueOfLowest1Bit(int t)
        {
            return t & -t;
        }

        /// <summary>
        /// Returns the value of the lowest set bit (<c>t &amp; -t</c>) for an unsigned integer.
        /// </summary>
        /// <param name="t">Input value.</param>
        /// <returns>Lowest set bit value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ValueOfLowest1Bit(uint t)
        {
            return (uint)((int)t & -(int)t);
        }

        /// <summary>
        /// Returns the value of the lowest set bit (<c>t &amp; -t</c>) for a native unsigned integer.
        /// </summary>
        /// <param name="t">Input value.</param>
        /// <returns>Lowest set bit value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint ValueOfLowest1Bit(nuint t)
        {
            return (nuint)((nint)t & -(nint)t);
        }

        /// <summary>
        /// Computes the Lehmer (factorial basis) code of <paramref name="permutation"/>, an array of <paramref name="n"/>
        /// unique indices in <c>[0..n)</c>, and stores it in <paramref name="code"/><c>[0..n)</c>. <c>O(N log N)</c> time.
        /// <paramref name="temp"/> must have at least <c>n + 1</c> elements but need not be initialized.
        /// </summary>
        /// <param name="permutation">Input permutation of 32-bit unsigned indices.</param>
        /// <param name="temp">Scratch space of length at least <c>n + 1</c>.</param>
        /// <param name="n">Number of elements.</param>
        /// <param name="code">Output Lehmer code of length at least <c>n</c>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status ComputeLehmerCode(
            ReadOnlySpan<uint> permutation,
            Span<uint> temp,
            nuint n,
            Span<uint> code)
        {
            temp.Slice(0, (int)(n + 1)).Clear();

            for (nuint idx = 0; idx < n; ++idx)
            {
                uint s = permutation[(int)idx];

                // Compute sum in Fenwick tree
                uint penalty = SumFenwick(temp, s + 1);

                if (s < penalty)
                {
                    return Status.Failure("Invalid permutation element");
                }

                code[(int)idx] = s - penalty;

                // Add operation in Fenwick tree
                AddFenwick(temp, s + 1, (uint)(n + 1));
            }

            return true;
        }

        /// <summary>
        /// Computes the Lehmer (factorial basis) code of <paramref name="permutation"/> (16-bit elements).
        /// <paramref name="temp"/> must have at least <c>n + 1</c> elements.
        /// </summary>
        /// <param name="permutation">Input permutation of 16-bit unsigned indices.</param>
        /// <param name="temp">Scratch space of length at least <c>n + 1</c>.</param>
        /// <param name="n">Number of elements.</param>
        /// <param name="code">Output Lehmer code of length at least <c>n</c>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status ComputeLehmerCode(
            ReadOnlySpan<ushort> permutation,
            Span<uint> temp,
            nuint n,
            Span<uint> code)
        {
            temp.Slice(0, (int)(n + 1)).Clear();

            for (nuint idx = 0; idx < n; ++idx)
            {
                uint s = permutation[(int)idx];

                // Compute sum in Fenwick tree
                uint penalty = SumFenwick(temp, s + 1);

                if (s < penalty)
                {
                    return Status.Failure("Invalid permutation element");
                }

                code[(int)idx] = s - penalty;

                // Add operation in Fenwick tree
                AddFenwick(temp, s + 1, (uint)(n + 1));
            }

            return true;
        }

        /// <summary>
        /// Decodes the Lehmer code in <paramref name="code"/><c>[0..n)</c> into <paramref name="permutation"/><c>[0..n)</c>.
        /// <paramref name="temp"/> must have at least <c>1 &lt;&lt; CeilLog2(n)</c> elements.
        /// </summary>
        /// <param name="code">Input Lehmer code.</param>
        /// <param name="temp">Scratch space of length at least <c>1 &lt;&lt; CeilLog2Nonzero(n)</c>.</param>
        /// <param name="n">Number of elements (must be non-zero).</param>
        /// <param name="permutation">Output decoded permutation.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status DecodeLehmerCode(
            ReadOnlySpan<uint> code,
            Span<uint> temp,
            nuint n,
            Span<uint> permutation)
        {
            if (n == 0)
            {
                return Status.Failure("n must be non-zero");
            }

            nuint log2n   = (nuint)Bits.CeilLog2Nonzero(n);
            nuint paddedN = (nuint)1 << (int)log2n;

            InitDecodeTree(temp, paddedN);

            for (nuint i = 0; i < n; i++)
            {
                if (code[(int)i] + i >= n)
                {
                    return Status.Failure("Lehmer code out of range");
                }

                uint rank  = code[(int)i] + 1;
                nuint next = ExtractAndMarkUnused(temp, paddedN, log2n, rank);

                permutation[(int)i] = (uint)next;
            }

            return true;
        }

        /// <summary>
        /// Decodes the Lehmer code in <paramref name="code"/><c>[0..n)</c> into 16-bit <paramref name="permutation"/><c>[0..n)</c>.
        /// <paramref name="temp"/> must have at least <c>1 &lt;&lt; CeilLog2(n)</c> elements.
        /// </summary>
        /// <param name="code">Input Lehmer code.</param>
        /// <param name="temp">Scratch space of length at least <c>1 &lt;&lt; CeilLog2Nonzero(n)</c>.</param>
        /// <param name="n">Number of elements (must be non-zero).</param>
        /// <param name="permutation">Output decoded 16-bit permutation.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status DecodeLehmerCode(
            ReadOnlySpan<uint> code,
            Span<uint> temp,
            nuint n,
            Span<ushort> permutation)
        {
            if (n == 0)
            {
                return Status.Failure("n must be non-zero");
            }

            nuint log2n   = (nuint)Bits.CeilLog2Nonzero(n);
            nuint paddedN = (nuint)1 << (int)log2n;

            InitDecodeTree(temp, paddedN);

            for (nuint i = 0; i < n; i++)
            {
                if (code[(int)i] + i >= n)
                {
                    return Status.Failure("Lehmer code out of range");
                }

                uint rank  = code[(int)i] + 1;
                nuint next = ExtractAndMarkUnused(temp, paddedN, log2n, rank);

                permutation[(int)i] = (ushort)next;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint SumFenwick(ReadOnlySpan<uint> temp, uint i)
        {
            uint penalty = 0;

            while (i != 0)
            {
                penalty += temp[(int)i];
                i &= i - 1; // clear lowest bit
            }

            return penalty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddFenwick(Span<uint> temp, uint i, uint limit)
        {
            while (i < limit)
            {
                temp[(int)i] += 1;
                i += ValueOfLowest1Bit(i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InitDecodeTree(Span<uint> temp, nuint paddedN)
        {
            for (nuint i = 0; i < paddedN; i++)
            {
                int i1       = (int)(i + 1);
                temp[(int)i] = (uint)ValueOfLowest1Bit(i1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint ExtractAndMarkUnused(Span<uint> temp, nuint paddedN, nuint log2n, uint rank)
        {
            // Extract i-th unused element via implicit order-statistics tree.
            nuint bit  = paddedN;
            nuint next = 0;

            for (nuint b = 0; b <= log2n; b++)
            {
                nuint cand = next + bit;
                bit >>= 1;

                if (temp[(int)(cand - 1)] < rank)
                {
                    next = cand;
                    rank -= temp[(int)(cand - 1)];
                }
            }

            nuint result = next;

            // Mark as used
            next += 1;

            while (next <= paddedN)
            {
                temp[(int)(next - 1)] -= 1;
                next += ValueOfLowest1Bit(next);
            }

            return result;
        }
    }
}
