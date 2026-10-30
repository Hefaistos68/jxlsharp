// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Fast evaluation of rational polynomials for approximating functions.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace JxlSharp.Base
{
    /// <summary>
    /// Evaluates rational polynomials <c>P(x) / Q(x)</c> via Horner's scheme.
    /// </summary>
    public static class RationalPolynomial
    {
        /// <summary>
        /// Approximates smooth functions via rational polynomials (i.e. dividing two
        /// polynomials). Evaluates polynomials via Horner's scheme, which is faster than
        /// Clenshaw recurrence for Chebyshev polynomials.
        /// </summary>
        /// <param name="x">Evaluation point.</param>
        /// <param name="p">Numerator polynomial coefficients <c>p[0..degP]</c>.</param>
        /// <param name="q">Denominator polynomial coefficients <c>q[0..degQ]</c>.</param>
        /// <returns>Evaluated rational polynomial <c>P(x) / Q(x)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvalRationalPolynomial(float x, ReadOnlySpan<float> p, ReadOnlySpan<float> q)
        {
            int degP = p.Length - 1;
            int degQ = q.Length - 1;
            float yp = p[degP];
            float yq = q[degQ];

            for (int i = degP - 1; i >= 0; --i)
            {
                yp = MathF.FusedMultiplyAdd(yp, x, p[i]);
            }

            for (int i = degQ - 1; i >= 0; --i)
            {
                yq = MathF.FusedMultiplyAdd(yq, x, q[i]);
            }

            return yp / yq;
        }

        /// <summary>
        /// Evaluates a rational polynomial over a 4-lane <see cref="Vector128{Single}"/> using Horner's scheme.
        /// </summary>
        /// <param name="x">4-lane input vector.</param>
        /// <param name="p">Numerator polynomial coefficients <c>p[0..degP]</c>.</param>
        /// <param name="q">Denominator polynomial coefficients <c>q[0..degQ]</c>.</param>
        /// <returns>Evaluated 4-lane vector <c>P(x) / Q(x)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> EvalRationalPolynomial(
            Vector128<float> x,
            ReadOnlySpan<float> p,
            ReadOnlySpan<float> q)
        {
            int degP            = p.Length - 1;
            int degQ            = q.Length - 1;
            Vector128<float> yp = Vector128.Create(p[degP]);
            Vector128<float> yq = Vector128.Create(q[degQ]);

            for (int i = degP - 1; i >= 0; --i)
            {
                yp = (yp * x) + Vector128.Create(p[i]);
            }

            for (int i = degQ - 1; i >= 0; --i)
            {
                yq = (yq * x) + Vector128.Create(q[i]);
            }

            return yp / yq;
        }

        /// <summary>
        /// Evaluates a rational polynomial over a 8-lane <see cref="Vector256{Single}"/> using Horner's scheme.
        /// </summary>
        /// <param name="x">8-lane input vector.</param>
        /// <param name="p">Numerator polynomial coefficients <c>p[0..degP]</c>.</param>
        /// <param name="q">Denominator polynomial coefficients <c>q[0..degQ]</c>.</param>
        /// <returns>Evaluated 8-lane vector <c>P(x) / Q(x)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> EvalRationalPolynomial(
            Vector256<float> x,
            ReadOnlySpan<float> p,
            ReadOnlySpan<float> q)
        {
            int degP            = p.Length - 1;
            int degQ            = q.Length - 1;
            Vector256<float> yp = Vector256.Create(p[degP]);
            Vector256<float> yq = Vector256.Create(q[degQ]);

            for (int i = degP - 1; i >= 0; --i)
            {
                yp = (yp * x) + Vector256.Create(p[i]);
            }

            for (int i = degQ - 1; i >= 0; --i)
            {
                yq = (yq * x) + Vector256.Create(q[i]);
            }

            return yp / yq;
        }
    }
}
