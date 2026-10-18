// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlGainMapBundle"/>.
    /// </summary>
    [TestFixture]
    public class GainMapTests
    {
        /// <summary>
        /// Verifies <see cref="JxlGainMapBundle"/> properties and computed sizes.
        /// </summary>
        [Test]
        public void GainMapBundlePropertiesAndSizesWork()
        {
            JxlGainMapBundle bundle = new JxlGainMapBundle
            {
                JhgmVersion      = 1,
                GainMapMetadata  = [10, 20, 30],
                HasColorEncoding = true,
                ColorEncoding    = new JxlColorEncoding { ColorSpace = JxlColorSpace.Rgb },
                AltIcc           = [1, 2, 3, 4, 5],
                GainMap          = [0xFF, 0x0A],
            };

            bundle.JhgmVersion.ShouldBe((byte)1);
            bundle.GainMapMetadataSize.ShouldBe((ushort)3);
            bundle.HasColorEncoding.ShouldBeTrue();
            bundle.ColorEncoding.ColorSpace.ShouldBe(JxlColorSpace.Rgb);
            bundle.AltIccSize.ShouldBe(5u);
            bundle.GainMapSize.ShouldBe(2u);
        }
    }
}
