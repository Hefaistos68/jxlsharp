// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Iaca"/> and <see cref="ScopeIACA"/> (ported from <c>lib/jxl/iaca_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class IacaTests
    {
        /// <summary>
        /// Ported from <c>IacaTest.MarkersDefaultToDisabledAndDoNotCrash</c>.
        /// </summary>
        [Test]
        public void MarkersDefaultToDisabledAndDoNotCrash()
        {
            Should.NotThrow(() =>
            {
                Iaca.BeginIACA();
                Iaca.EndIACA();
            });
        }

        /// <summary>
        /// Ported from <c>IacaTest.ScopeDefaultToDisabledAndDoNotCrash</c>.
        /// </summary>
        [Test]
        public void ScopeDefaultToDisabledAndDoNotCrash()
        {
            Should.NotThrow(() =>
            {
                using ScopeIACA iaca = new ScopeIACA();
            });
        }
    }
}
