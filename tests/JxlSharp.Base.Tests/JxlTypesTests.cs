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
    /// Unit tests for <see cref="JxlPixelFormat"/>, <see cref="JxlBitDepth"/>, and <see cref="JxlBoxType"/>.
    /// </summary>
    [TestFixture]
    public class JxlTypesTests
    {
        /// <summary>
        /// Verifies <see cref="JxlPixelFormat"/> construction, properties, and equality.
        /// </summary>
        [Test]
        public void JxlPixelFormat_EqualityAndProperties_ShouldMatch()
        {
            JxlPixelFormat format1 = new JxlPixelFormat(4u, JxlDataType.Float, JxlEndianness.LittleEndian, 16u);
            JxlPixelFormat format2 = new JxlPixelFormat(4u, JxlDataType.Float, JxlEndianness.LittleEndian, 16u);
            JxlPixelFormat format3 = new JxlPixelFormat(3u, JxlDataType.UInt8, JxlEndianness.NativeEndian, 0u);

            format1.NumChannels.ShouldBe(4u);
            format1.DataType.ShouldBe(JxlDataType.Float);
            format1.Endianness.ShouldBe(JxlEndianness.LittleEndian);
            format1.Align.ShouldBe((nuint)16u);

            (format1 == format2).ShouldBeTrue();
            (format1 != format3).ShouldBeTrue();
            format1.Equals((object)format2).ShouldBeTrue();
            format1.Equals((object)"invalid").ShouldBeFalse();
            format1.GetHashCode().ShouldBe(format2.GetHashCode());

            format3.NumChannels = 2u;
            format3.DataType    = JxlDataType.Float16;
            format3.Endianness  = JxlEndianness.BigEndian;
            format3.Align       = 8u;

            format3.NumChannels.ShouldBe(2u);
            format3.DataType.ShouldBe(JxlDataType.Float16);
            format3.Endianness.ShouldBe(JxlEndianness.BigEndian);
            format3.Align.ShouldBe((nuint)8u);
        }

        /// <summary>
        /// Verifies <see cref="JxlBitDepth"/> construction, properties, and equality.
        /// </summary>
        [Test]
        public void JxlBitDepth_EqualityAndProperties_ShouldMatch()
        {
            JxlBitDepth depth1 = new JxlBitDepth(JxlBitDepthType.Custom, 12u, 0u);
            JxlBitDepth depth2 = new JxlBitDepth(JxlBitDepthType.Custom, 12u, 0u);
            JxlBitDepth depth3 = new JxlBitDepth(JxlBitDepthType.FromPixelFormat, 16u, 5u);

            depth1.Type.ShouldBe(JxlBitDepthType.Custom);
            depth1.BitsPerSample.ShouldBe(12u);
            depth1.ExponentBitsPerSample.ShouldBe(0u);

            (depth1 == depth2).ShouldBeTrue();
            (depth1 != depth3).ShouldBeTrue();
            depth1.Equals((object)depth2).ShouldBeTrue();
            depth1.Equals((object?)null).ShouldBeFalse();
            depth1.GetHashCode().ShouldBe(depth2.GetHashCode());

            depth3.Type                  = JxlBitDepthType.FromCodestream;
            depth3.BitsPerSample         = 10u;
            depth3.ExponentBitsPerSample = 4u;

            depth3.Type.ShouldBe(JxlBitDepthType.FromCodestream);
            depth3.BitsPerSample.ShouldBe(10u);
            depth3.ExponentBitsPerSample.ShouldBe(4u);
        }

        /// <summary>
        /// Verifies <see cref="JxlBoxType"/> construction, string conversion, and equality.
        /// </summary>
        [Test]
        public void JxlBoxType_ConstructorsAndToString_ShouldMatch()
        {
            JxlBoxType fromString = new JxlBoxType("jxlc");
            JxlBoxType fromBytes  = new JxlBoxType((byte)'j', (byte)'x', (byte)'l', (byte)'c');
            JxlBoxType otherBox   = new JxlBoxType("brob");

            fromString.ToString().ShouldBe("jxlc");
            fromString.Packed.ShouldBe(fromBytes.Packed);
            (fromString == fromBytes).ShouldBeTrue();
            (fromString != otherBox).ShouldBeTrue();
            fromString.Equals((object)fromBytes).ShouldBeTrue();
            fromString.Equals((object?)null).ShouldBeFalse();
            fromString.GetHashCode().ShouldBe(fromBytes.GetHashCode());

            Should.Throw<ArgumentException>(() => new JxlBoxType("abc"));
            Should.Throw<ArgumentException>(() => new JxlBoxType(null!));
        }
    }
}
