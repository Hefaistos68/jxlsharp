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
    /// Unit tests for <see cref="ScopeGuard"/>.
    /// </summary>
    [TestFixture]
    public class ScopeGuardTests
    {
        /// <summary>
        /// Verifies that an armed <see cref="ScopeGuard"/> invokes its callback once upon disposal.
        /// </summary>
        [Test]
        public void Dispose_WhenArmed_ShouldInvokeCallbackOnce()
        {
            int callCount = 0;

            using (ScopeGuard guard = ScopeGuard.Create(() => callCount++))
            {
                callCount.ShouldBe(0);
            }

            callCount.ShouldBe(1);
        }

        /// <summary>
        /// Verifies that a disarmed <see cref="ScopeGuard"/> does not invoke its callback upon disposal.
        /// </summary>
        [Test]
        public void Dispose_WhenDisarmed_ShouldNotInvokeCallback()
        {
            int callCount = 0;

            using (ScopeGuard guard = ScopeGuard.Create(() => callCount++))
            {
                guard.Disarm();
            }

            callCount.ShouldBe(0);
            Should.Throw<ArgumentNullException>(() => new ScopeGuard(null!));
        }
    }
}
