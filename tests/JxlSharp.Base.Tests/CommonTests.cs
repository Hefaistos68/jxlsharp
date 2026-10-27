// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Common"/>.
    /// </summary>
    [TestFixture]
    public class CommonTests
    {
        /// <summary>
        /// Verifies rounding and division helpers.
        /// </summary>
        [Test]
        public void RoundingAndDivCeil_ShouldComputeExpectedValues()
        {
            Common.RoundUpBitsToByteMultiple(0).ShouldBe(0);
            Common.RoundUpBitsToByteMultiple(1).ShouldBe(8);
            Common.RoundUpBitsToByteMultiple(8).ShouldBe(8);
            Common.RoundUpBitsToByteMultiple((nuint)9u).ShouldBe((nuint)16u);

            Common.RoundUpToBlockDim(0).ShouldBe(0);
            Common.RoundUpToBlockDim(7).ShouldBe(8);
            Common.RoundUpToBlockDim((nuint)15u).ShouldBe((nuint)16u);

            Common.DivCeil(10, 3).ShouldBe(4);
            Common.DivCeil(9L, 3L).ShouldBe(3L);
            Common.DivCeil(10ul, 3ul).ShouldBe(4ul);

            Common.RoundUpTo(10, 4).ShouldBe(12);
            Common.RoundUpTo(10ul, 4ul).ShouldBe(12ul);
        }

        /// <summary>
        /// Verifies overflow-safe arithmetic operations.
        /// </summary>
        [Test]
        public void SafeArithmetic_ShouldDetectOverflows()
        {
            Common.SafeAdd(10ul, 20ul, out ulong sum).ShouldBeTrue();
            sum.ShouldBe(30ul);

            Common.SafeAdd(ulong.MaxValue, 1ul, out _).ShouldBeFalse();

            Common.SafeMul(0ul, ulong.MaxValue, out ulong prodZero).ShouldBeTrue();
            prodZero.ShouldBe(0ul);

            Common.SafeMul(6ul, 7ul, out ulong prod).ShouldBeTrue();
            prod.ShouldBe(42ul);

            Common.SafeMul(ulong.MaxValue, 2ul, out _).ShouldBeFalse();

            Common.SubOverflow(10, 3, out int diff).ShouldBeFalse();
            diff.ShouldBe(7);

            Common.SubOverflow(int.MinValue, 1, out _).ShouldBeTrue();

            Common.SafeRoundUpTo(15ul, 1ul, out ulong r1).ShouldBeTrue();
            r1.ShouldBe(15ul);

            Common.SafeRoundUpTo(16ul, 8ul, out ulong r2).ShouldBeTrue();
            r2.ShouldBe(16ul);

            Common.SafeRoundUpTo(13ul, 8ul, out ulong r3).ShouldBeTrue();
            r3.ShouldBe(16ul);

            Common.SafeRoundUpTo(ulong.MaxValue, 8ul, out _).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies Pi, Clamp1, and ToString helpers.
        /// </summary>
        [Test]
        public void MathAndFormatHelpers_ShouldReturnExpectedResults()
        {
            Common.Pi(2.0).ShouldBe(2.0 * Common.kPi, 1e-12);
            Common.Pi(2.0f).ShouldBe((float)(2.0 * Common.kPi), 1e-5f);

            Common.Clamp1(5, 0, 10).ShouldBe(5);
            Common.Clamp1(-5, 0, 10).ShouldBe(0);
            Common.Clamp1(15, 0, 10).ShouldBe(10);

            Common.ToString(123).ShouldBe("123");
            Common.ToString(1.5).ShouldBe("1.5");
        }
    }
}
