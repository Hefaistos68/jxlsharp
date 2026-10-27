// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Rng"/>.
    /// </summary>
    [TestFixture]
    public class RngTests
    {
        /// <summary>
        /// Verifies deterministic output and bounds of <see cref="Rng"/> distributions.
        /// </summary>
        [Test]
        public void Rng_DistributionsAndShuffle_ShouldStayWithinBounds()
        {
            Rng rng1 = new Rng(42);
            Rng rng2 = new Rng(42);

            rng1.NextUInt64().ShouldBe(rng2.NextUInt64());

            for (int i = 0; i < 100; i++)
            {
                long ui = rng1.UniformI(-10, 10);
                ui.ShouldBeGreaterThanOrEqualTo(-10L);
                ui.ShouldBeLessThan(10L);

                ulong uu = rng1.UniformU(5ul, 25ul);
                uu.ShouldBeGreaterThanOrEqualTo(5ul);
                uu.ShouldBeLessThan(25ul);

                float uf = rng1.UniformF(-2.0f, 3.0f);
                uf.ShouldBeGreaterThanOrEqualTo(-2.0f);
                uf.ShouldBeLessThan(3.0f);
            }

            rng1.Bernoulli(0.0f).ShouldBeFalse();
            rng1.Bernoulli(1.0f).ShouldBeTrue();

            float geomDist = Rng.MakeGeometric(0.5f);
            uint geomSample = rng1.Geometric(geomDist);
            geomSample.ShouldBeGreaterThanOrEqualTo(0u);

            int[] items = { 1, 2, 3, 4, 5, 6, 7, 8 };
            rng1.Shuffle(items);
            items.Length.ShouldBe(8);
        }
    }
}
