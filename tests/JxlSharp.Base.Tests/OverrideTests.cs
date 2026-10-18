// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Override"/> and <see cref="OverrideHelper"/>.
    /// </summary>
    [TestFixture]
    public class OverrideTests
    {
        /// <summary>
        /// Verifies <see cref="OverrideHelper.OverrideFromBool"/> and <see cref="OverrideHelper.ApplyOverride"/>.
        /// </summary>
        [Test]
        public void OverrideHelper_ShouldResolveConditionsCorrectly()
        {
            OverrideHelper.OverrideFromBool(true).ShouldBe(Override.On);
            OverrideHelper.OverrideFromBool(false).ShouldBe(Override.Off);

            OverrideHelper.ApplyOverride(Override.On, false).ShouldBeTrue();
            OverrideHelper.ApplyOverride(Override.Off, true).ShouldBeFalse();
            OverrideHelper.ApplyOverride(Override.Default, true).ShouldBeTrue();
            OverrideHelper.ApplyOverride(Override.Default, false).ShouldBeFalse();
        }
    }
}
