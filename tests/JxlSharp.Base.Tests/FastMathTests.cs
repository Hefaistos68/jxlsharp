// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.Intrinsics;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="FastMath"/> (ported from lib/jxl/fast_math_test.cc).
    /// </summary>
    [TestFixture]
    public class FastMathTests
    {
        /// <summary>
        /// Port of TestFastLog2 in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestFastLog2()
        {
            const int kNumTrials = 1 << 16;
            Rng rng              = new Rng(1);

            for (int i = 0; i < kNumTrials; i++)
            {
                float f      = rng.UniformF(1e-7f, 1e3f);
                float actual = FastMath.FastLog2f(f);
                float absErr = MathF.Abs(MathF.Log2(f) - actual);

                absErr.ShouldBeLessThan(3.1E-6f);
            }

            Vector128<float> vInput  = Vector128.Create(1.0f, 2.0f, 4.0f, 8.0f);
            Vector128<float> vOutput = FastMath.FastLog2f(vInput);

            vOutput.GetElement(0).ShouldBe(0.0f, 3.1E-6f);
            vOutput.GetElement(1).ShouldBe(1.0f, 3.1E-6f);
            vOutput.GetElement(2).ShouldBe(2.0f, 3.1E-6f);
            vOutput.GetElement(3).ShouldBe(3.0f, 3.1E-6f);
        }

        /// <summary>
        /// Port of TestFastPow2 in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestFastPow2()
        {
            const int kNumTrials = 1 << 16;
            Rng rng              = new Rng(1);

            for (int i = 0; i < kNumTrials; i++)
            {
                float f        = rng.UniformF(-100.0f, 100.0f);
                float actual   = FastMath.FastPow2f(f);
                float expected = MathF.Pow(2.0f, f);
                float relErr   = MathF.Abs(expected - actual) / expected;

                relErr.ShouldBeLessThan(3.1E-6f);
            }
        }

        /// <summary>
        /// Port of TestFastPow in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestFastPow()
        {
            const int kNumTrials = 1 << 16;
            Rng rng              = new Rng(1);

            for (int i = 0; i < kNumTrials; i++)
            {
                float b        = rng.UniformF(1e-3f, 1e3f);
                float e        = rng.UniformF(-10.0f, 10.0f);
                float actual   = FastMath.FastPowf(b, e);
                float expected = MathF.Pow(b, e);
                float relErr   = MathF.Abs(expected - actual) / expected;

                relErr.ShouldBeLessThan(3E-5f);
            }
        }

        /// <summary>
        /// Port of TestFastCos in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestFastCos()
        {
            const int kNumTrials = 1 << 16;
            Rng rng              = new Rng(1);

            for (int i = 0; i < kNumTrials; i++)
            {
                float f      = rng.UniformF(-1e3f, 1e3f);
                float actual = FastMath.FastCosf(f);
                float absErr = MathF.Abs(MathF.Cos(f) - actual);

                absErr.ShouldBeLessThan(7E-5f);
            }
        }

        /// <summary>
        /// Port of TestFastErf in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestFastErf()
        {
            const int kNumTrials = 1 << 15;
            Rng rng              = new Rng(1);

            for (int i = 0; i < kNumTrials; i++)
            {
                float f        = rng.UniformF(-5.0f, 5.0f);
                float actual   = FastMath.FastErff(f);
                float expected = ReferenceErf(f);
                float absErr   = MathF.Abs(expected - actual);

                absErr.ShouldBeLessThan(7E-4f);
            }
        }

        /// <summary>
        /// Port of TestCubeRoot in fast_math_test.cc.
        /// </summary>
        [Test]
        public void TestCubeRoot()
        {
            for (ulong x5 = 0; x5 < 200000; x5 += 10)
            {
                float x        = x5 * 1E-5f;
                float expected = MathF.Cbrt(x);
                float approx   = FastMath.CubeRootAndAdd(x, 0.0f);

                approx.ShouldBe(expected, 8E-7f);
            }
        }

        // High-precision Abramowitz & Stegun 7.1.26 reference erf for testing.
        private static float ReferenceErf(double x)
        {
            int sign = x < 0 ? -1 : 1;
            x        = Math.Abs(x);

            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p  = 0.3275911;

            double t = 1.0 / (1.0 + (p * x));
            double y = 1.0 - (((((((((a5 * t) + a4) * t) + a3) * t) + a2) * t) + a1) * t * Math.Exp(-x * x));

            return (float)(sign * y);
        }
    }
}
