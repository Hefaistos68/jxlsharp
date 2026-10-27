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
    /// Unit tests for <see cref="RationalPolynomial"/> (ported from lib/jxl/rational_polynomial_test.cc).
    /// </summary>
    [TestFixture]
    public class RationalPolynomialTests
    {
        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestSimpleGamma).
        /// </summary>
        [Test]
        public void TestSimpleGamma()
        {
            float[] p =
            {
                -5.0646949363741811E-05f, 6.7369380528439771E-05f,
                8.9376652530412794E-05f,  2.1153513301520462E-06f,
                -6.9130322970386449E-08f, 3.9424752749293728E-10f,
                1.2360288207619576E-13f,
            };

            float[] q =
            {
                -6.6389733798591366E-06f, 1.3299859726565908E-05f,
                3.8538748358398873E-06f,  -2.8707687262928236E-08f,
                -6.6897385800005434E-10f, 6.1428748869186003E-12f,
                -2.5475738169252870E-15f,
            };

            float err = RunApproximation(0.77f, 274.579999999999984f, p, q, SimpleGamma);

            err.ShouldBeLessThan(0.05f);
        }

        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestLinearToSrgb8Direct).
        /// </summary>
        [Test]
        public void TestLinearToSrgb8Direct()
        {
            float[] p =
            {
                -9.5357499040105154E-05f, 4.6761186249798248E-04f,
                2.5708174333943594E-04f,  1.5250087770436082E-05f,
                1.1946768008931187E-07f,  5.9916446295972850E-11f,
            };

            float[] q =
            {
                1.8932479758079768E-05f, 2.7312342474687321E-05f,
                4.3901204783327006E-06f, 1.0417787306920273E-07f,
                3.0084206762140419E-10f,
            };

            float err = RunApproximation(0.77f, 255.0f, p, q, LinearToSrgb8Direct);

            err.ShouldBeLessThan(0.05f);
        }

        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestExp).
        /// </summary>
        [Test]
        public void TestExp()
        {
            float[] p =
            {
                9.6266879665530902E-01f,
                4.8961265681586763E-01f,
                8.2619259189548433E-02f,
            };

            float[] q =
            {
                9.6259895571622622E-01f,
                -4.7272457588933831E-01f,
                7.4802088567547664E-02f,
            };

            float err = RunApproximation(-1.0f, 1.0f, p, q, MathF.Exp);

            err.ShouldBeLessThan(1E-4f);
        }

        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestNegExp).
        /// </summary>
        [Test]
        public void TestNegExp()
        {
            float[] p =
            {
                5.9580258551150123E-02f, -2.5073728806886408E-02f,
                4.1561830213689248E-03f, -3.1815408488900372E-04f,
                9.3866690094906802E-06f,
            };

            float[] q =
            {
                5.9579108238812878E-02f, 3.4542074345478582E-02f,
                8.7263562483501714E-03f, 1.4095109143061216E-03f,
            };

            float err = RunApproximation(0.0f, 10.0f, p, q, x => MathF.Exp(-x));

            err.ShouldBeLessThan(3E-5f);
        }

        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestSin).
        /// </summary>
        [Test]
        public void TestSin()
        {
            float[] p =
            {
                1.5518122109203780E-05f,  2.3388958643675966E+00f,
                -8.6705520940849157E-01f, -1.9702294764873535E-01f,
                1.2193404314472320E-01f,  -1.7373966109788839E-02f,
                7.8829435883034796E-04f,
            };

            float[] q =
            {
                2.3394371422557279E+00f, -8.7028221081288615E-01f,
                2.0052872219658430E-01f, -3.2460335995264836E-02f,
                3.1546157932479282E-03f, -1.6692542019380155E-04f,
            };

            float err = RunApproximation(0.0f, Common.Pi(1.0f) * 2.0f, p, q, MathF.Sin);

            err.ShouldBeLessThan(7E-4f);
        }

        /// <summary>
        /// Port of TEST(RationalPolynomialTest, TestLog) and Vector128 overload check.
        /// </summary>
        [Test]
        public void TestLogAndVector128()
        {
            float[] p =
            {
                -1.8503833400518310E-06f,
                1.4287160470083755E+00f,
                7.4245873327820566E-01f,
            };

            float[] q =
            {
                9.9032814277590719E-01f,
                1.0096718572241148E+00f,
                1.7409343003366853E-01f,
            };

            Vector128<float> vx   = Vector128.Create(0.1f, 0.2f, -0.1f, 0.0f);
            Vector128<float> vres = RationalPolynomial.EvalRationalPolynomial(vx, p, q);

            vres.GetElement(0).ShouldBe(RationalPolynomial.EvalRationalPolynomial(0.1f, p, q), 1e-6f);
            vres.GetElement(1).ShouldBe(RationalPolynomial.EvalRationalPolynomial(0.2f, p, q), 1e-6f);
        }

        private static float LinearToSrgb8Direct(float val)
        {
            if (val < 0.0f)
            {
                return 0.0f;
            }

            if (val >= 255.0f)
            {
                return 255.0f;
            }

            if (val <= 10.0f / 12.92f)
            {
                return val * 12.92f;
            }

            return (float)(255.0 * ((Math.Pow(val / 255.0, 1.0 / 2.4) * 1.055) - 0.055));
        }

        private static float SimpleGamma(float v)
        {
            const float kGamma = 0.387494322593f;
            const float limit  = 43.01745241042018f;
            float bright       = v - limit;

            if (bright >= 0.0f)
            {
                const float mul = 0.0383723643799f;
                v -= bright * mul;
            }

            const float limit2 = 94.68634353321337f;
            float bright2      = v - limit2;

            if (bright2 >= 0.0f)
            {
                const float mul = 0.22885405968f;
                v -= bright2 * mul;
            }

            const float offset = 0.156775786057f;
            const float scale  = 8.898059160493739f;

            return scale * (offset + MathF.Pow(v, kGamma));
        }

        private static float RunApproximation(
            float x0,
            float x1,
            ReadOnlySpan<float> p,
            ReadOnlySpan<float> q,
            Func<float, float> funcToApprox)
        {
            float maxerr = 0.0f;
            float step   = (x1 - x0) / 10000.0f;

            for (float x = x0; x <= x1; x += step)
            {
                float f   = funcToApprox(x);
                float g   = RationalPolynomial.EvalRationalPolynomial(x, p, q);
                float err = MathF.Abs(g - f);

                maxerr = MathF.Max(err, maxerr);
            }

            return maxerr;
        }
    }
}
