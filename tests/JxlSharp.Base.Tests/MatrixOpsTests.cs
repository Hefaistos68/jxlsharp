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
    /// Unit tests for <see cref="MatrixOps"/>.
    /// </summary>
    [TestFixture]
    public class MatrixOpsTests
    {
        /// <summary>
        /// Verifies 3x3 single-precision and double-precision matrix multiplication, vector multiplication, and inversion.
        /// </summary>
        [Test]
        public void MulAndInv3x3Matrix_ShouldProduceIdentityOnRoundTrip()
        {
            float[] m =
            {
                2.0f, 1.0f, 0.0f,
                0.0f, 3.0f, 1.0f,
                1.0f, 0.0f, 2.0f,
            };

            float[] inv  = (float[])m.Clone();
            float[] prod = new float[9];
            float[] v    = { 1.0f, 2.0f, 3.0f };
            float[] mv   = new float[3];
            float[] vOut = new float[3];

            MatrixOps.Inv3x3Matrix(inv).IsOk.ShouldBeTrue();
            MatrixOps.Mul3x3Matrix(m, inv, prod);

            prod[0].ShouldBe(1.0f, 1e-5f);
            prod[4].ShouldBe(1.0f, 1e-5f);
            prod[8].ShouldBe(1.0f, 1e-5f);
            prod[1].ShouldBe(0.0f, 1e-5f);
            prod[2].ShouldBe(0.0f, 1e-5f);

            MatrixOps.Mul3x3Vector(m, v, mv);
            MatrixOps.Mul3x3Vector(inv, mv, vOut);

            vOut[0].ShouldBe(v[0], 1e-5f);
            vOut[1].ShouldBe(v[1], 1e-5f);
            vOut[2].ShouldBe(v[2], 1e-5f);

            // Double precision
            double[] md    = { 2.0, 1.0, 0.0, 0.0, 3.0, 1.0, 1.0, 0.0, 2.0 };
            double[] invd  = (double[])md.Clone();
            double[] prodd = new double[9];
            double[] vd    = { 1.0, 2.0, 3.0 };
            double[] mvd   = new double[3];

            MatrixOps.Inv3x3Matrix(invd).IsOk.ShouldBeTrue();
            MatrixOps.Mul3x3Matrix(md, invd, prodd);
            MatrixOps.Mul3x3Vector(md, vd, mvd);

            prodd[0].ShouldBe(1.0, 1e-12);
            prodd[4].ShouldBe(1.0, 1e-12);
            prodd[8].ShouldBe(1.0, 1e-12);

            // Singular matrix fails inversion
            float[] singular = new float[9];
            MatrixOps.Inv3x3Matrix(singular).IsOk.ShouldBeFalse();
        }
    }
}
