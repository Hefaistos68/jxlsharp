// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Xorshift128Plus"/> (ported from lib/jxl/xorshift128plus_test.cc).
    /// </summary>
    [TestFixture]
    public class Xorshift128PlusTests
    {
        private static readonly ulong[][] ExpectedFirst4 =
        {
            new ulong[]
            {
                0x6E901576D477CBB1ul, 0xE9E53789195DA2A2ul, 0xB681F6DDA5E0AE99ul,
                0x8EFD18CE21FD6896ul, 0xA898A80DF75CF532ul, 0x50CEB2C9E2DE7E32ul,
                0x3CA7C2FEB25C0DD0ul, 0xA4D0866B80B4D836ul,
            },
            new ulong[]
            {
                0x8CD6A1E6233D3A26ul, 0x3D4603ADE98B112Dul, 0xDC427AF674019E36ul,
                0xE28B4D230705AC53ul, 0x7297E9BBA88783DDul, 0x34D3D23CFCD9B41Aul,
                0x5A223615ADBE96B8ul, 0xE5EB529027CFBD01ul,
            },
            new ulong[]
            {
                0xC1894CF00DFAC6A2ul, 0x18EDF8AE9085E404ul, 0x8E936625296B4CCDul,
                0x31971EF3A14A899Bul, 0xBE87535FCE0BF26Aul, 0x576F7A752BC6649Ful,
                0xA44CBADCE0C6B937ul, 0x3DBA819BB17A353Aul,
            },
            new ulong[]
            {
                0x27CE38DFCC1C5EB6ul, 0x920BEB5606340256ul, 0x3986CBC40C9AFC2Cul,
                0xE22BCB3EEB1E191Eul, 0x6E1FCDD3602A8FBAul, 0x052CB044E5415A29ul,
                0x46266646EFB9ECD7ul, 0x8F44914618D29335ul,
            },
        };

        private static readonly ulong[] ExpectedVector63 =
        {
            0xCA5BB519ECE7481Bul, 0xFD496155E55CA945ul, 0xF753B9DBB1515F81ul,
            0x50549E8BAC0F70E7ul, 0x8614FB0271E21C60ul, 0x60C72947EB0F0070ul,
            0xA6511C10AEE742B6ul, 0x48FB48F2CACCB43Eul,
        };

        /// <summary>
        /// Port of TestGolden in xorshift128plus_test.cc.
        /// Ensures Xorshift128+ returns exact bit-for-bit golden outputs matching C++ libjxl.
        /// </summary>
        [Test]
        public void TestGolden()
        {
            Xorshift128Plus rng = new Xorshift128Plus(12345);
            Span<ulong> lanes   = stackalloc ulong[Xorshift128Plus.N];

            for (int vector = 0; vector < 64; ++vector)
            {
                rng.Fill(lanes);

                if (vector < ExpectedFirst4.Length)
                {
                    for (int i = 0; i < Xorshift128Plus.N; ++i)
                    {
                        lanes[i].ShouldBe(ExpectedFirst4[vector][i]);
                    }
                }
                else if (vector == 63)
                {
                    for (int i = 0; i < Xorshift128Plus.N; ++i)
                    {
                        lanes[i].ShouldBe(ExpectedVector63[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Port of TestSeedChanges in xorshift128plus_test.cc and 4-seed constructor check.
        /// </summary>
        [Test]
        public void TestSeedChanges()
        {
            const int kNumSeeds   = 16384;
            HashSet<ulong> unique = new HashSet<ulong>(kNumSeeds);
            Span<ulong> lanes     = stackalloc ulong[Xorshift128Plus.N];

            for (ulong seed = 0; seed < kNumSeeds; ++seed)
            {
                Xorshift128Plus rng = new Xorshift128Plus(seed);

                rng.Fill(lanes);
                unique.Add(lanes[0]);
            }

            unique.Count.ShouldBe(kNumSeeds);

            Xorshift128Plus rng4 = new Xorshift128Plus(1u, 2u, 3u, 4u);
            rng4.Fill(lanes);
            lanes[0].ShouldNotBe(0ul);
        }

        /// <summary>
        /// Port of TestFloat and TestNotZero in xorshift128plus_test.cc.
        /// </summary>
        [Test]
        public void TestFloatAndNotZero()
        {
            Xorshift128Plus rng = new Xorshift128Plus(7u);
            Span<ulong> batch64 = stackalloc ulong[Xorshift128Plus.N];
            double sum          = 0.0;
            int count           = 0;
            int numZero         = 0;

            while (count < 32000)
            {
                rng.Fill(batch64);

                for (int i = 0; i < Xorshift128Plus.N; ++i)
                {
                    if (batch64[i] == 0)
                    {
                        numZero++;
                    }

                    uint lo   = (uint)(batch64[i] & 0xFFFFFFFFul);
                    uint hi   = (uint)(batch64[i] >> 32);
                    float fLo = BitConverter.UInt32BitsToSingle((lo >> 9) | 0x3F800000u);
                    float fHi = BitConverter.UInt32BitsToSingle((hi >> 9) | 0x3F800000u);

                    sum   += fLo + fHi;
                    count += 2;
                }
            }

            (sum / count).ShouldBe(1.5, 0.00702);
            numZero.ShouldBeLessThanOrEqualTo(1);
        }
    }
}
