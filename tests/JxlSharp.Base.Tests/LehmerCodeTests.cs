// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LehmerCode"/> (ported from <c>lib/jxl/lehmer_code_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class LehmerCodeTests
    {
        /// <summary>
        /// Ported from <c>LehmerCodeTest.TestRoundtrips</c>.
        /// </summary>
        [Test]
        public void TestRoundtrips()
        {
            RoundtripSizeRangeUInt16(1, 1026);

            // Ensures PermutationT can fit > 16 bit values.
            RoundtripSizeRangeUInt32(65536, 65540);
        }

        private static void RoundtripSizeRangeUInt16(uint begin, uint end)
        {
            nuint maxN        = end - 1;
            nuint paddedN     = (nuint)1 << (int)Bits.CeilLog2Nonzero(maxN + 1);
            ushort[] perm     = new ushort[maxN];
            uint[] temp       = new uint[paddedN];
            uint[] lehmer     = new uint[maxN];
            ushort[] decoded  = new ushort[maxN];

            for (uint n = begin; n < end; ++n)
            {
                RoundtripUInt16(n, perm, temp, lehmer, decoded);
            }
        }

        private static void RoundtripUInt16(
            uint n,
            ushort[] permutation,
            uint[] temp,
            uint[] lehmer,
            ushort[] decoded)
        {
            nuint paddedN = (nuint)1 << (int)Bits.CeilLog2Nonzero(n);
            Rng rng       = new Rng(((ulong)n * 65537ul) + 13ul);

            for (uint i = 0; i < n; ++i)
            {
                permutation[i] = (ushort)i;
            }

            Span<ushort> permSpan = permutation.AsSpan(0, (int)n);
            Span<uint> tempSpan   = temp.AsSpan(0, (int)Math.Max(paddedN, (nuint)n + 1));
            Span<uint> lehmerSpan = lehmer.AsSpan(0, (int)n);
            Span<ushort> decSpan  = decoded.AsSpan(0, (int)n);

            for (int rep = 0; rep < 3; ++rep)
            {
                rng.Shuffle(permSpan);

                LehmerCode.ComputeLehmerCode(permSpan, tempSpan, n, lehmerSpan).IsOk.ShouldBeTrue();
                temp.AsSpan(0, (int)paddedN).Clear();
                LehmerCode.DecodeLehmerCode(lehmerSpan, tempSpan, n, decSpan).IsOk.ShouldBeTrue();

                permSpan.SequenceEqual(decSpan).ShouldBeTrue();
            }
        }

        private static void RoundtripSizeRangeUInt32(uint begin, uint end)
        {
            nuint maxN       = end - 1;
            nuint paddedN    = (nuint)1 << (int)Bits.CeilLog2Nonzero(maxN + 1);
            uint[] perm      = new uint[maxN];
            uint[] temp      = new uint[paddedN];
            uint[] lehmer    = new uint[maxN];
            uint[] decoded   = new uint[maxN];

            for (uint n = begin; n < end; ++n)
            {
                RoundtripUInt32(n, perm, temp, lehmer, decoded);
            }
        }

        private static void RoundtripUInt32(
            uint n,
            uint[] permutation,
            uint[] temp,
            uint[] lehmer,
            uint[] decoded)
        {
            nuint paddedN = (nuint)1 << (int)Bits.CeilLog2Nonzero(n);
            Rng rng       = new Rng(((ulong)n * 65537ul) + 13ul);

            for (uint i = 0; i < n; ++i)
            {
                permutation[i] = i;
            }

            Span<uint> permSpan   = permutation.AsSpan(0, (int)n);
            Span<uint> tempSpan   = temp.AsSpan(0, (int)Math.Max(paddedN, (nuint)n + 1));
            Span<uint> lehmerSpan = lehmer.AsSpan(0, (int)n);
            Span<uint> decSpan    = decoded.AsSpan(0, (int)n);

            for (int rep = 0; rep < 3; ++rep)
            {
                rng.Shuffle(permSpan);

                LehmerCode.ComputeLehmerCode(permSpan, tempSpan, n, lehmerSpan).IsOk.ShouldBeTrue();
                temp.AsSpan(0, (int)paddedN).Clear();
                LehmerCode.DecodeLehmerCode(lehmerSpan, tempSpan, n, decSpan).IsOk.ShouldBeTrue();

                permSpan.SequenceEqual(decSpan).ShouldBeTrue();
            }
        }
    }
}
