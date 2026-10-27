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
    /// Unit tests for <see cref="FloatHelper"/>.
    /// </summary>
    [TestFixture]
    public class FloatHelperTests
    {
        /// <summary>
        /// Verifies <see cref="FloatHelper.LoadFloat16"/> on zero, subnormal, normal, and infinity values.
        /// </summary>
        [Test]
        public void LoadFloat16_ShouldConvertHalfPrecisionBits()
        {
            FloatHelper.LoadFloat16(0x0000).ShouldBe(0.0f);
            FloatHelper.LoadFloat16(0x8000).ShouldBe(-0.0f);
            FloatHelper.LoadFloat16(0x3C00).ShouldBe(1.0f);
            FloatHelper.LoadFloat16(0xBC00).ShouldBe(-1.0f);
            FloatHelper.LoadFloat16(0x0001).ShouldBe((float)BitConverter.UInt16BitsToHalf(0x0001), 1e-10f);
            FloatHelper.LoadFloat16(0x8001).ShouldBe((float)BitConverter.UInt16BitsToHalf(0x8001), 1e-10f);
            float.IsPositiveInfinity(FloatHelper.LoadFloat16(0x7C00)).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="FloatHelper.LoadFloatRow"/> across all supported <see cref="JxlDataType"/> formats.
        /// </summary>
        [Test]
        public void LoadFloatRow_AllDataTypes_ShouldLoadExpectedFloats()
        {
            Span<float> dst = stackalloc float[2];

            // UInt8
            ReadOnlySpan<byte> u8Src = stackalloc byte[] { 10, 20 };
            FloatHelper.LoadFloatRow(u8Src, 2, 1, JxlDataType.UInt8, true, 0.5f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(5.0f);
            dst[1].ShouldBe(10.0f);

            // UInt16 LE and BE
            Span<byte> u16Src = stackalloc byte[4];
            ByteOrder.StoreLE16(100u, u16Src.Slice(0, 2));
            ByteOrder.StoreLE16(200u, u16Src.Slice(2, 2));
            FloatHelper.LoadFloatRow(u16Src, 2, 2, JxlDataType.UInt16, true, 0.1f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(10.0f, 1e-5f);
            dst[1].ShouldBe(20.0f, 1e-5f);

            ByteOrder.StoreBE16(300u, u16Src.Slice(0, 2));
            ByteOrder.StoreBE16(400u, u16Src.Slice(2, 2));
            FloatHelper.LoadFloatRow(u16Src, 2, 2, JxlDataType.UInt16, false, 0.1f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(30.0f, 1e-5f);
            dst[1].ShouldBe(40.0f, 1e-5f);

            // Float32 LE and BE
            Span<byte> f32Src = stackalloc byte[8];
            ByteOrder.StoreLE32(BitConverter.SingleToUInt32Bits(1.5f), f32Src.Slice(0, 4));
            ByteOrder.StoreLE32(BitConverter.SingleToUInt32Bits(2.5f), f32Src.Slice(4, 4));
            FloatHelper.LoadFloatRow(f32Src, 2, 4, JxlDataType.Float, true, 1.0f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(1.5f);
            dst[1].ShouldBe(2.5f);

            ByteOrder.StoreBE32(BitConverter.SingleToUInt32Bits(3.5f), f32Src.Slice(0, 4));
            ByteOrder.StoreBE32(BitConverter.SingleToUInt32Bits(4.5f), f32Src.Slice(4, 4));
            FloatHelper.LoadFloatRow(f32Src, 2, 4, JxlDataType.Float, false, 1.0f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(3.5f);
            dst[1].ShouldBe(4.5f);

            // Float16 LE and BE
            ByteOrder.StoreLE16(0x3C00u, u16Src.Slice(0, 2));
            ByteOrder.StoreLE16(0x4000u, u16Src.Slice(2, 2));
            FloatHelper.LoadFloatRow(u16Src, 2, 2, JxlDataType.Float16, true, 1.0f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(1.0f);
            dst[1].ShouldBe(2.0f);

            ByteOrder.StoreBE16(0x3C00u, u16Src.Slice(0, 2));
            ByteOrder.StoreBE16(0x4000u, u16Src.Slice(2, 2));
            FloatHelper.LoadFloatRow(u16Src, 2, 2, JxlDataType.Float16, false, 1.0f, dst).IsOk.ShouldBeTrue();
            dst[0].ShouldBe(1.0f);
            dst[1].ShouldBe(2.0f);

            // Unsupported format
            FloatHelper.LoadFloatRow(u8Src, 1, 1, (JxlDataType)99, true, 1.0f, dst).IsOk.ShouldBeFalse();
        }
    }
}
