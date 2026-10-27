// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Bits"/> (ported from lib/jxl/bits_test.cc).
    /// </summary>
    [TestFixture]
    public class BitsTests
    {
        /// <summary>
        /// Port of TEST(BitsTest, TestNumZeroBits).
        /// </summary>
        [Test]
        public void TestNumZeroBits()
        {
            // Zero input is well-defined.
            Bits.Num0BitsAboveMS1Bit(0u).ShouldBe(32);
            Bits.Num0BitsAboveMS1Bit(0ul).ShouldBe(64);
            Bits.Num0BitsBelowLS1Bit(0u).ShouldBe(32);
            Bits.Num0BitsBelowLS1Bit(0ul).ShouldBe(64);

            Bits.Num0BitsAboveMS1Bit(1u).ShouldBe(31);
            Bits.Num0BitsAboveMS1Bit(2u).ShouldBe(30);
            Bits.Num0BitsAboveMS1Bit(1ul).ShouldBe(63);
            Bits.Num0BitsAboveMS1Bit(2ul).ShouldBe(62);

            Bits.Num0BitsBelowLS1Bit(1u).ShouldBe(0);
            Bits.Num0BitsBelowLS1Bit(1ul).ShouldBe(0);
            Bits.Num0BitsBelowLS1Bit(2u).ShouldBe(1);
            Bits.Num0BitsBelowLS1Bit(2ul).ShouldBe(1);

            Bits.Num0BitsAboveMS1Bit(0x80000000u).ShouldBe(0);
            Bits.Num0BitsAboveMS1Bit(0x8000000000000000ul).ShouldBe(0);
            Bits.Num0BitsBelowLS1Bit(0x80000000u).ShouldBe(31);
            Bits.Num0BitsBelowLS1Bit(0x8000000000000000ul).ShouldBe(63);
        }

        /// <summary>
        /// Port of TEST(BitsTest, TestFloorLog2).
        /// </summary>
        [Test]
        public void TestFloorLog2()
        {
            // for input = [1, 7]
            int[] expected = { 0, 1, 1, 2, 2, 2, 2 };

            for (uint i = 1; i <= 7; ++i)
            {
                Bits.FloorLog2Nonzero(i).ShouldBe(expected[i - 1]);
                Bits.FloorLog2Nonzero((ulong)i).ShouldBe(expected[i - 1]);
            }

            Bits.FloorLog2Nonzero(0x00000fffu).ShouldBe(11); // 4095
            Bits.FloorLog2Nonzero(0x00001000u).ShouldBe(12); // 4096
            Bits.FloorLog2Nonzero(0x00001001u).ShouldBe(12); // 4097

            Bits.FloorLog2Nonzero(0x80000000u).ShouldBe(31);
            Bits.FloorLog2Nonzero(0x80000001u).ShouldBe(31);
            Bits.FloorLog2Nonzero(0xFFFFFFFFu).ShouldBe(31);

            Bits.FloorLog2Nonzero(0x80000000ul).ShouldBe(31);
            Bits.FloorLog2Nonzero(0x80000001ul).ShouldBe(31);
            Bits.FloorLog2Nonzero(0xFFFFFFFFul).ShouldBe(31);

            Bits.FloorLog2Nonzero(0x8000000000000000ul).ShouldBe(63);
            Bits.FloorLog2Nonzero(0x8000000000000001ul).ShouldBe(63);
            Bits.FloorLog2Nonzero(0xFFFFFFFFFFFFFFFFul).ShouldBe(63);
        }

        /// <summary>
        /// Port of TEST(BitsTest, TestCeilLog2).
        /// </summary>
        [Test]
        public void TestCeilLog2()
        {
            // for input = [1, 7]
            int[] expected = { 0, 1, 2, 2, 3, 3, 3 };

            for (uint i = 1; i <= 7; ++i)
            {
                Bits.CeilLog2Nonzero(i).ShouldBe(expected[i - 1]);
                Bits.CeilLog2Nonzero((ulong)i).ShouldBe(expected[i - 1]);
            }

            Bits.CeilLog2Nonzero(0x00000fffu).ShouldBe(12); // 4095
            Bits.CeilLog2Nonzero(0x00001000u).ShouldBe(12); // 4096
            Bits.CeilLog2Nonzero(0x00001001u).ShouldBe(13); // 4097

            Bits.CeilLog2Nonzero(0x80000000u).ShouldBe(31);
            Bits.CeilLog2Nonzero(0x80000001u).ShouldBe(32);
            Bits.CeilLog2Nonzero(0xFFFFFFFFu).ShouldBe(32);

            Bits.CeilLog2Nonzero(0x80000000ul).ShouldBe(31);
            Bits.CeilLog2Nonzero(0x80000001ul).ShouldBe(32);
            Bits.CeilLog2Nonzero(0xFFFFFFFFul).ShouldBe(32);

            Bits.CeilLog2Nonzero(0x8000000000000000ul).ShouldBe(63);
            Bits.CeilLog2Nonzero(0x8000000000000001ul).ShouldBe(64);
            Bits.CeilLog2Nonzero(0xFFFFFFFFFFFFFFFFul).ShouldBe(64);
        }
    }
}
