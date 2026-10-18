// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PackSignedHelper"/>.
    /// </summary>
    [TestFixture]
    public class PackSignedTests
    {
        /// <summary>
        /// Verifies that <see cref="PackSignedHelper.PackSigned"/> and <see cref="PackSignedHelper.UnpackSigned"/>
        /// roundtrip signed values and match zigzag mapping.
        /// </summary>
        [Test]
        public void PackAndUnpackSignedRoundtrip()
        {
            PackSignedHelper.PackSigned(0).ShouldBe(0u);
            PackSignedHelper.PackSigned(-1).ShouldBe(1u);
            PackSignedHelper.PackSigned(1).ShouldBe(2u);
            PackSignedHelper.PackSigned(-2).ShouldBe(3u);
            PackSignedHelper.PackSigned(2).ShouldBe(4u);

            int[] testValues = [0, 1, -1, 2, -2, 127, -128, 32767, -32768, 1000000, -1000000, int.MaxValue, int.MinValue];

            foreach (int v in testValues)
            {
                uint packed  = PackSignedHelper.PackSigned(v);
                int unpacked = PackSignedHelper.UnpackSigned(packed);
                unpacked.ShouldBe(v);
            }
        }
    }
}
