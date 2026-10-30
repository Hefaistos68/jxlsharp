// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Fast SIMD math ops (log2, encoder only, cos, erf for splines)

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace JxlSharp.Base
{
    /// <summary>
    /// Fast scalar and SIMD mathematical approximations (Log2, Pow2, Pow, Cos, Erf, CubeRootAndAdd).
    /// </summary>
    public static class FastMath
    {
        // 2,2 rational polynomial approximation of std::log1p(x) / std::log(2).
        private static readonly float[] Log2P =
        {
            -1.8503833400518310E-06f,
            1.4287160470083755E+00f,
            7.4245873327820566E-01f,
        };

        private static readonly float[] Log2Q =
        {
            9.9032814277590719E-01f,
            1.0096718572241148E+00f,
            1.7409343003366853E-01f,
        };

        /// <summary>
        /// Computes base-2 logarithm like std::log2. Undefined if negative / NaN.
        /// L1 error ~3.9E-6.
        /// </summary>
        /// <param name="x">Positive floating-point value.</param>
        /// <returns>Approximate base-2 logarithm of <paramref name="x"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastLog2f(float x)
        {
            int xBits = BitConverter.SingleToInt32Bits(x);

            // Range reduction to [-1/3, 1/3] - 3 integer, 2 float ops
            int expBits = xBits - 0x3f2aaaab; // = 2/3

            // Shifted exponent = log2; also used to clear mantissa.
            int expShifted = expBits >> 23;
            float mantissa = BitConverter.Int32BitsToSingle(xBits - (expShifted << 23));
            float expVal   = expShifted;

            return RationalPolynomial.EvalRationalPolynomial(mantissa - 1.0f, Log2P, Log2Q) + expVal;
        }

        /// <summary>
        /// Computes base-2 logarithm over a 4-lane <see cref="Vector128{Single}"/>.
        /// </summary>
        /// <param name="x">4-lane positive float vector.</param>
        /// <returns>Approximate base-2 logarithm vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> FastLog2f(Vector128<float> x)
        {
            Vector128<int> xBits      = x.AsInt32();
            Vector128<int> expBits    = xBits - Vector128.Create(0x3f2aaaab);
            Vector128<int> expShifted = Vector128.ShiftRightArithmetic(expBits, 23);
            Vector128<float> mantissa = (xBits - Vector128.ShiftLeft(expShifted, 23)).AsSingle();
            Vector128<float> expVal   = Vector128.ConvertToSingle(expShifted);

            return RationalPolynomial.EvalRationalPolynomial(
                mantissa - Vector128.Create(1.0f),
                Log2P,
                Log2Q) + expVal;
        }

        /// <summary>
        /// Computes base-2 logarithm over a 8-lane <see cref="Vector256{Single}"/>.
        /// </summary>
        /// <param name="x">8-lane positive float vector.</param>
        /// <returns>Approximate base-2 logarithm vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> FastLog2f(Vector256<float> x)
        {
            Vector256<int> xBits      = x.AsInt32();
            Vector256<int> expBits    = xBits - Vector256.Create(0x3f2aaaab);
            Vector256<int> expShifted = Vector256.ShiftRightArithmetic(expBits, 23);
            Vector256<float> mantissa = (xBits - Vector256.ShiftLeft(expShifted, 23)).AsSingle();
            Vector256<float> expVal   = Vector256.ConvertToSingle(expShifted);

            return RationalPolynomial.EvalRationalPolynomial(
                mantissa - Vector256.Create(1.0f),
                Log2P,
                Log2Q) + expVal;
        }

        /// <summary>
        /// Computes 2^x with max relative error ~3e-7.
        /// </summary>
        /// <param name="x">Exponent value.</param>
        /// <returns>Approximate value of <c>2^x</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastPow2f(float x)
        {
            float floorx = MathF.Floor(x);
            int expBits  = ((int)floorx + 127) << 23;
            float exp    = BitConverter.Int32BitsToSingle(expBits);
            float frac   = x - floorx;

            float num = frac + 1.01749063e+01f;
            num       = MathF.FusedMultiplyAdd(num, frac, 4.88687798e+01f);
            num       = MathF.FusedMultiplyAdd(num, frac, 9.85506591e+01f);
            num      *= exp;

            float den = MathF.FusedMultiplyAdd(frac, 2.10242958e-01f, -2.22328856e-02f);
            den       = MathF.FusedMultiplyAdd(den, frac, -1.94414990e+01f);
            den       = MathF.FusedMultiplyAdd(den, frac, 9.85506633e+01f);

            return num / den;
        }

        /// <summary>
        /// Computes 2^x over a 4-lane <see cref="Vector128{Single}"/> with max relative error ~3e-7.
        /// </summary>
        /// <param name="x">4-lane exponent vector.</param>
        /// <returns>Approximate value of <c>2^x</c> per lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> FastPow2f(Vector128<float> x)
        {
            Vector128<float> floorx  = Vector128.Floor(x);
            Vector128<int> expBits   = Vector128.ShiftLeft(Vector128.ConvertToInt32(floorx) + Vector128.Create(127), 23);
            Vector128<float> exp     = expBits.AsSingle();
            Vector128<float> frac    = x - floorx;

            Vector128<float> num = frac + Vector128.Create(1.01749063e+01f);
            num                  = (num * frac) + Vector128.Create(4.88687798e+01f);
            num                  = (num * frac) + Vector128.Create(9.85506591e+01f);
            num                 *= exp;

            Vector128<float> den = (frac * Vector128.Create(2.10242958e-01f)) + Vector128.Create(-2.22328856e-02f);
            den                  = (den * frac) + Vector128.Create(-1.94414990e+01f);
            den                  = (den * frac) + Vector128.Create(9.85506633e+01f);

            return num / den;
        }

        /// <summary>
        /// Computes 2^x over a 8-lane <see cref="Vector256{Single}"/> with max relative error ~3e-7.
        /// </summary>
        /// <param name="x">8-lane exponent vector.</param>
        /// <returns>Approximate value of <c>2^x</c> per lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> FastPow2f(Vector256<float> x)
        {
            Vector256<float> floorx  = Vector256.Floor(x);
            Vector256<int> expBits   = Vector256.ShiftLeft(Vector256.ConvertToInt32(floorx) + Vector256.Create(127), 23);
            Vector256<float> exp     = expBits.AsSingle();
            Vector256<float> frac    = x - floorx;

            Vector256<float> num = frac + Vector256.Create(1.01749063e+01f);
            num                  = (num * frac) + Vector256.Create(4.88687798e+01f);
            num                  = (num * frac) + Vector256.Create(9.85506591e+01f);
            num                 *= exp;

            Vector256<float> den = (frac * Vector256.Create(2.10242958e-01f)) + Vector256.Create(-2.22328856e-02f);
            den                  = (den * frac) + Vector256.Create(-1.94414990e+01f);
            den                  = (den * frac) + Vector256.Create(9.85506633e+01f);

            return num / den;
        }

        /// <summary>
        /// Computes <c>baseVal^exponent</c> with max relative error ~3e-5.
        /// </summary>
        /// <param name="baseVal">Base value (positive).</param>
        /// <param name="exponent">Exponent value.</param>
        /// <returns>Approximate value of <c>baseVal^exponent</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastPowf(float baseVal, float exponent)
        {
            return FastPow2f(FastLog2f(baseVal) * exponent);
        }

        /// <summary>
        /// Computes <c>baseVal^exponent</c> over 4-lane <see cref="Vector128{Single}"/> vectors with max relative error ~3e-5.
        /// </summary>
        /// <param name="baseVal">Base value vector (positive).</param>
        /// <param name="exponent">Exponent value vector.</param>
        /// <returns>Approximate value of <c>baseVal^exponent</c> per lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> FastPowf(Vector128<float> baseVal, Vector128<float> exponent)
        {
            return FastPow2f(FastLog2f(baseVal) * exponent);
        }

        /// <summary>
        /// Computes <c>baseVal^exponent</c> over 8-lane <see cref="Vector256{Single}"/> vectors with max relative error ~3e-5.
        /// </summary>
        /// <param name="baseVal">Base value vector (positive).</param>
        /// <param name="exponent">Exponent value vector.</param>
        /// <returns>Approximate value of <c>baseVal^exponent</c> per lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> FastPowf(Vector256<float> baseVal, Vector256<float> exponent)
        {
            return FastPow2f(FastLog2f(baseVal) * exponent);
        }

        /// <summary>
        /// Computes cosine like std::cos with L1 error ~7e-5.
        /// </summary>
        /// <param name="x">Angle in radians.</param>
        /// <returns>Approximate cosine of <paramref name="x"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastCosf(float x)
        {
            // Step 1: range reduction to [0, 2pi)
            const float pi     = (float)Common.kPi;
            const float pi2    = pi * 2.0f;
            const float pi2Inv = 0.5f / pi;

            float npi2    = MathF.Floor(x * pi2Inv) * pi2;
            float xmodpi2 = x - npi2;

            // Step 2: range reduction to [0, pi]
            float xPi = MathF.Min(xmodpi2, pi2 - xmodpi2);

            // Step 3: range reduction to [0, pi/2]
            bool abovePiHalf = xPi >= (pi / 2.0f);
            float xPiHalf    = abovePiHalf ? (pi - xPi) : xPi;

            // Step 4: Taylor-like approximation, scaled by 2**0.75 to make angle
            // duplication steps faster, on x/4.
            float xs             = xPiHalf * 0.25f;
            float x2             = xs * xs;
            float x4             = x2 * x2;
            float cosxPrescaling = MathF.FusedMultiplyAdd(
                x4,
                0.06960438f,
                MathF.FusedMultiplyAdd(x2, -0.84087373f, 1.68179268f));

            // Step 5: angle duplication.
            float cosxScale1 = MathF.FusedMultiplyAdd(cosxPrescaling, cosxPrescaling, -1.414213562f);
            float cosxScale2 = MathF.FusedMultiplyAdd(cosxScale1, cosxScale1, -1.0f);

            // Step 6: change sign if needed.
            uint signBit = abovePiHalf ? 0x80000000u : 0u;

            return BitConverter.UInt32BitsToSingle(signBit ^ BitConverter.SingleToUInt32Bits(cosxScale2));
        }

        /// <summary>
        /// Computes the error function like std::erf with L1 error ~7e-4.
        /// </summary>
        /// <param name="x">Input value.</param>
        /// <returns>Approximate error function <c>erf(x)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastErff(float x)
        {
            // Formula from
            // https://en.wikipedia.org/wiki/Error_function#Numerical_approximations
            // but constants have been recomputed.
            bool xle0  = x <= 0.0f;
            float absx = MathF.Abs(x);

            // Compute 1 - 1 / ((((x * a + b) * x + c) * x + d) * x + 1)**4
            float denom1    = MathF.FusedMultiplyAdd(absx, 7.77394369e-02f, 2.05260015e-04f);
            float denom2    = MathF.FusedMultiplyAdd(denom1, absx, 2.32120216e-01f);
            float denom3    = MathF.FusedMultiplyAdd(denom2, absx, 2.77820801e-01f);
            float denom4    = MathF.FusedMultiplyAdd(denom3, absx, 1.0f);
            float denom5    = denom4 * denom4;
            float invDenom5 = 1.0f / denom5;
            float result    = MathF.FusedMultiplyAdd(-invDenom5, invDenom5, 1.0f);

            // Change sign if needed.
            uint signBit = xle0 ? 0x80000000u : 0u;

            return BitConverter.UInt32BitsToSingle(signBit ^ BitConverter.SingleToUInt32Bits(result));
        }

        /// <summary>
        /// Returns <c>cbrt(x) + add</c> with 6 ulp max error.
        /// Modified from vectormath_exp.h, Apache 2 license.
        /// </summary>
        /// <param name="x">Non-negative input value.</param>
        /// <param name="add">Value to add to the cube root.</param>
        /// <returns>Approximate value of <c>cbrt(x) + add</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubeRootAndAdd(float x, float add)
        {
            const int kExpBias = 0x54800000; // cast(1.) + cast(1.) / 3
            const int kExpMul  = 0x002AAAAA; // shifted 1/3
            const float k1_3   = 1.0f / 3.0f;
            const float k4_3   = 4.0f / 3.0f;

            float xa  = x; // assume inputs never negative
            float xa3 = k1_3 * xa;

            // Multiply exponent by -1/3
            int m1 = BitConverter.SingleToInt32Bits(xa);

            // Special case for 0. 0 is represented with an exponent of 0, so the
            // "kExpBias - 1/3 * exp" below gives the wrong result.
            int m2  = m1 == 0 ? 0 : kExpBias - ((m1 >> 23) * kExpMul);
            float r = BitConverter.Int32BitsToSingle(m2);

            // Newton-Raphson iterations
            for (int i = 0; i < 3; i++)
            {
                float r2 = r * r;
                r        = MathF.FusedMultiplyAdd(-xa3, r2 * r2, k4_3 * r);
            }

            // Final iteration
            float r2Final = r * r;
            r             = MathF.FusedMultiplyAdd(k1_3, MathF.FusedMultiplyAdd(-xa, r2Final * r2Final, r), r);
            r2Final       = r * r;
            r             = MathF.FusedMultiplyAdd(r2Final, x, add);

            return r;
        }
        /// <summary>Computes cbrt(x) + add for eight non-negative samples using the scalar algorithm's fused operations.</summary>
        /// <param name="x">Non-negative input samples.</param>
        /// <param name="add">Bias added to each cube root.</param>
        /// <returns>Eight cube roots with the bias added.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> CubeRootAndAdd(Vector256<float> x, Vector256<float> add)
        {
            if (!Avx2.IsSupported || !Fma.IsSupported)
            {
                Vector256<float> result = Vector256<float>.Zero;
                for (int lane = 0; lane < Vector256<float>.Count; lane++)
                    result = result.WithElement(lane, CubeRootAndAdd(x.GetElement(lane), add.GetElement(lane)));
                return result;
            }

            Vector256<float> oneThird = Vector256.Create(1.0f / 3.0f);
            Vector256<float> fourThirds = Vector256.Create(4.0f / 3.0f);
            Vector256<float> xa3 = oneThird * x;
            Vector256<int> bits = x.AsInt32();
            Vector256<int> estimate = Vector256.Create(0x54800000) -
                (Vector256.ShiftRightArithmetic(bits, 23) * Vector256.Create(0x002AAAAA));
            Vector256<float> r = Vector256.ConditionalSelect(Vector256.Equals(bits, Vector256<int>.Zero),
                Vector256<int>.Zero, estimate).AsSingle();
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Vector256<float> r2 = r * r;
                r = Fma.MultiplyAdd(-xa3, r2 * r2, fourThirds * r);
            }
            Vector256<float> square = r * r;
            r = Fma.MultiplyAdd(oneThird, Fma.MultiplyAdd(-x, square * square, r), r);
            square = r * r;
            return Fma.MultiplyAdd(square, x, add);
        }
    }
}
