// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlEncoderStats"/>.
    /// </summary>
    [TestFixture]
    public class JxlEncoderStatsTests
    {
        /// <summary>
        /// Verifies <see cref="JxlEncoderStats.Get"/>, <see cref="JxlEncoderStats.Set"/>, and <see cref="JxlEncoderStats.Merge"/>.
        /// </summary>
        [Test]
        public void SetGetAndMergeStatsWork()
        {
            JxlEncoderStats statsA = JxlEncoderStats.Create();
            JxlEncoderStats statsB = JxlEncoderStats.Create();

            statsA.Set(JxlEncoderStatsKey.HeaderBits, 120);
            statsA.Set(JxlEncoderStatsKey.AcBits, 500);

            statsB.Set(JxlEncoderStatsKey.HeaderBits, 30);
            statsB.Set(JxlEncoderStatsKey.DcBits, 80);

            statsA.Merge(statsB);
            statsA.Merge(null);

            statsA.Get(JxlEncoderStatsKey.HeaderBits).ShouldBe((nuint)150);
            statsA.Get(JxlEncoderStatsKey.AcBits).ShouldBe((nuint)500);
            statsA.Get(JxlEncoderStatsKey.DcBits).ShouldBe((nuint)80);
            statsA.Get(JxlEncoderStatsKey.NumStats).ShouldBe((nuint)0);
        }
    }
}
