// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="ByteOrder"/> (ported from lib/jxl/byte_order_test.cc).
    /// </summary>
    [TestFixture]
    public class ByteOrderTests
    {
        /// <summary>
        /// Port of TEST(ByteOrderTest, TestRoundTripBE16).
        /// </summary>
        [Test]
        public void TestRoundTripBE16()
        {
            const uint input = 0x1234u;
            Span<byte> buf   = stackalloc byte[2];

            ByteOrder.StoreBE16(input, buf);
            ByteOrder.LoadBE16(buf).ShouldBe(input);
            ByteOrder.LoadLE16(buf).ShouldNotBe(input);
        }

        /// <summary>
        /// Port of TEST(ByteOrderTest, TestRoundTripLE16).
        /// </summary>
        [Test]
        public void TestRoundTripLE16()
        {
            const uint input = 0x1234u;
            Span<byte> buf   = stackalloc byte[2];

            ByteOrder.StoreLE16(input, buf);
            ByteOrder.LoadLE16(buf).ShouldBe(input);
            ByteOrder.LoadBE16(buf).ShouldNotBe(input);
        }

        /// <summary>
        /// Port of TEST(ByteOrderTest, TestRoundTripBE32).
        /// </summary>
        [Test]
        public void TestRoundTripBE32()
        {
            const uint input = 0xFEDCBA98u;
            Span<byte> buf   = stackalloc byte[4];

            ByteOrder.StoreBE32(input, buf);
            ByteOrder.LoadBE32(buf).ShouldBe(input);
            ByteOrder.LoadLE32(buf).ShouldNotBe(input);
        }

        /// <summary>
        /// Port of TEST(ByteOrderTest, TestRoundTripLE32).
        /// </summary>
        [Test]
        public void TestRoundTripLE32()
        {
            const uint input = 0xFEDCBA98u;
            Span<byte> buf   = stackalloc byte[4];

            ByteOrder.StoreLE32(input, buf);
            ByteOrder.LoadLE32(buf).ShouldBe(input);
            ByteOrder.LoadBE32(buf).ShouldNotBe(input);
        }

        /// <summary>
        /// Port of TEST(ByteOrderTest, TestRoundTripLE64) and BE64/Float helpers.
        /// </summary>
        [Test]
        public void TestRoundTrip64AndFloat()
        {
            const ulong input = 0xFEDCBA9876543210ul;
            Span<byte> buf8   = stackalloc byte[8];
            Span<byte> buf4   = stackalloc byte[4];

            ByteOrder.StoreLE64(input, buf8);
            ByteOrder.LoadLE64(buf8).ShouldBe(input);

            ByteOrder.StoreBE64(input, buf8);
            ByteOrder.LoadBE64(buf8).ShouldBe(input);

            ByteOrder.IsLittleEndian().ShouldBe(BitConverter.IsLittleEndian);
            ByteOrder.SwapEndianness(JxlEndianness.NativeEndian).ShouldBeFalse();
            ByteOrder.SwapEndianness(JxlEndianness.BigEndian).ShouldBe(BitConverter.IsLittleEndian);
            ByteOrder.SwapEndianness(JxlEndianness.LittleEndian).ShouldBe(!BitConverter.IsLittleEndian);

            const float f = 1.25f;
            uint rawBits  = BitConverter.SingleToUInt32Bits(f);

            ByteOrder.StoreLE32(rawBits, buf4);
            ByteOrder.LoadLEFloat(buf4).ShouldBe(f);

            ByteOrder.StoreBE32(rawBits, buf4);
            ByteOrder.LoadBEFloat(buf4).ShouldBe(f);

            ByteOrder.BSwapFloat(ByteOrder.BSwapFloat(f)).ShouldBe(f);
        }
    }
}
