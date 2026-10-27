// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlColorEncoding"/>.
    /// </summary>
    [TestFixture]
    public class ColorEncodingTests
    {
        /// <summary>
        /// Verifies <see cref="JxlColorEncoding"/> equality and property storage.
        /// </summary>
        [Test]
        public void ColorEncodingEqualityAndPropertiesWork()
        {
            JxlColorEncoding srgbA = new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Rgb,
                WhitePoint       = JxlWhitePoint.D65,
                WhitePointX      = 0.3127,
                WhitePointY      = 0.3290,
                Primaries        = JxlPrimaries.Srgb,
                PrimariesRedX    = 0.64,
                PrimariesRedY    = 0.33,
                PrimariesGreenX  = 0.30,
                PrimariesGreenY  = 0.60,
                PrimariesBlueX   = 0.15,
                PrimariesBlueY   = 0.06,
                TransferFunction = JxlTransferFunction.Srgb,
                Gamma            = 0.0,
                RenderingIntent  = JxlRenderingIntent.Relative,
            };

            JxlColorEncoding srgbB = srgbA;
            JxlColorEncoding p3    = srgbA;
            p3.Primaries           = JxlPrimaries.P3;

            (srgbA == srgbB).ShouldBeTrue();
            (srgbA != p3).ShouldBeTrue();
            srgbA.Equals((object)srgbB).ShouldBeTrue();
            srgbA.Equals((object?)null).ShouldBeFalse();
            srgbA.GetHashCode().ShouldBe(srgbB.GetHashCode());
        }
    }
}
