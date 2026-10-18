// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Cms.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OpsinParams"/> (including <c>GoldenXYBCube</c> from <c>lib/jxl/color_management_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class OpsinParamsTests
    {
        /// <summary>
        /// Ported from <c>ColorManagementTest.GoldenXYBCube</c>.
        /// </summary>
        [Test]
        public void GoldenXybCubeMatchesExpected()
        {
            List<int> actual  = new List<int>();
            Vector3[,,] cube  = OpsinParams.UnscaledA2BCube();

            for (int ix = 0; ix < 2; ++ix)
            {
                for (int iy = 0; iy < 2; ++iy)
                {
                    for (int ib = 0; ib < 2; ++ib)
                    {
                        Vector3 outF = cube[ix, iy, ib];

                        for (int i = 0; i < 3; ++i)
                        {
                            int val = (int)MathF.Round(65535.0f * outF[i], MidpointRounding.AwayFromZero);
                            val.ShouldBeInRange(0, 65535);
                            actual.Add(val);
                        }
                    }
                }
            }

            int[] expected =
            {
                0,     3206,  0,     0,     3206,  28873,
                62329, 65535, 36662, 62329, 65535, 65535,
                3206,  0,     0,     3206,  0,     28873,
                65535, 62329, 36662, 65535, 62329, 65535,
            };

            actual.ToArray().ShouldBe(expected);
        }

        /// <summary>
        /// Verifies that <see cref="OpsinParams.KOpsinAbsorbanceMatrix"/> and <see cref="OpsinParams.DefaultInverseOpsinAbsorbanceMatrix"/>
        /// are inverses of each other.
        /// </summary>
        [Test]
        public void OpsinAbsorbanceMatrixAndInverseMultiplyToIdentity()
        {
            Matrix3x3 inv = OpsinParams.DefaultInverseOpsinAbsorbanceMatrix();
            MatrixOps.Mul3x3Matrix(in OpsinParams.KOpsinAbsorbanceMatrix, in inv, out Matrix3x3 prod);

            for (int r = 0; r < 3; ++r)
            {
                for (int c = 0; c < 3; ++c)
                {
                    prod[r][c].ShouldBe(r == c ? 1.0f : 0.0f, 1e-5f);
                }
            }
        }
    }
}
