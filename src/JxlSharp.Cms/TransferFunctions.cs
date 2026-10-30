// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    // Definitions for BT.2100-2 transfer functions (used inside/outside SIMD):
    // "display" is linear light (nits) normalized to [0, 1].
    // "encoded" is a nonlinear encoding (e.g. PQ) in [0, 1].
    // "scene" is a linear function of photon counts, normalized to [0, 1].
    //
    // Despite the stated ranges, we need unbounded transfer functions: see
    // http://www.littlecms.com/CIC18_UnboundedCMM.pdf. Inputs can be negative or
    // above 1 due to chromatic adaptation. To avoid severe round-trip errors caused
    // by clamping, we mirror negative inputs via copysign (f(-x) = -f(x), see
    // https://developer.apple.com/documentation/coregraphics/cgcolorspace/1644735-extendedsrgb)
    // and extend the function domains above 1.

    /// <summary>
    /// Base double-precision Hybrid Log-Gamma (HLG) transfer function.
    /// Ported from <c>jxl::TF_HLG_Base</c> in <c>lib/jxl/cms/transfer_functions.h</c>.
    /// </summary>
    public class TfHlgBase
    {
        /// <summary>HLG constant a.</summary>
        protected const double KA = 0.17883277;

        /// <summary>Reciprocal of HLG constant a.</summary>
        protected const double KRa = 1.0 / KA;

        /// <summary>HLG constant b.</summary>
        protected const double KB = 1.0 - (4.0 * KA);

        /// <summary>HLG constant c.</summary>
        protected const double KC = 0.5599107295;

        /// <summary>Constant 1 / 12.</summary>
        protected const double KInv12 = 1.0 / 12.0;

        /// <summary>
        /// Evaluates the HLG EOTF (<paramref name="e"/> = encoded).
        /// </summary>
        /// <param name="e">Encoded value.</param>
        /// <returns>Normalized linear display light.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DisplayFromEncoded(double e)
        {
            return Ootf(InvOetf(e));
        }

        /// <summary>
        /// Evaluates the inverse HLG EOTF (<paramref name="d"/> = display).
        /// </summary>
        /// <param name="d">Normalized linear display light.</param>
        /// <returns>Encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double EncodedFromDisplay(double d)
        {
            return Oetf(InvOotf(d));
        }

        // OETF (defines the HLG approach). s = scene, returns encoded.
        private static double Oetf(double s)
        {
            if (s == 0.0)
            {
                return 0.0;
            }

            double originalSign = s;
            s = Math.Abs(s);

            if (s <= KInv12)
            {
                return Math.CopySign(Math.Sqrt(3.0 * s), originalSign);
            }

            double e = (KA * Math.Log((12.0 * s) - KB)) + KC;

            return Math.CopySign(e, originalSign);
        }

        // e = encoded, returns scene.
        private static double InvOetf(double e)
        {
            if (e == 0.0)
            {
                return 0.0;
            }

            double originalSign = e;
            e = Math.Abs(e);

            if (e <= 0.5)
            {
                return Math.CopySign(e * e * (1.0 / 3.0), originalSign);
            }

            double s = (Math.Exp((e - KC) * KRa) + KB) * KInv12;

            return Math.CopySign(s, originalSign);
        }

        // s = scene, returns display.
        private static double Ootf(double s)
        {
            // The actual (red channel) OOTF is RD = alpha * YS^(gamma-1) * RS, where
            // YS = 0.2627 * RS + 0.6780 * GS + 0.0593 * BS. Let alpha = 1 so we return
            // "display" (normalized [0, 1]) instead of nits. Our transfer function
            // interface does not allow a dependency on YS. Fortunately, the system
            // gamma at 334 nits is 1.0, so this reduces to RD = RS.
            return s;
        }

        // d = display, returns scene.
        private static double InvOotf(double d)
        {
            return d; // see Ootf().
        }
    }

    /// <summary>
    /// Base double-precision Perceptual Quantization (PQ) transfer function.
    /// Ported from <c>jxl::TF_PQ_Base</c> in <c>lib/jxl/cms/transfer_functions.h</c>.
    /// </summary>
    public class TfPqBase
    {
        /// <summary>PQ constant m1.</summary>
        protected const double KM1 = 2610.0 / 16384.0;

        /// <summary>PQ constant m2.</summary>
        protected const double KM2 = (2523.0 / 4096.0) * 128.0;

        /// <summary>PQ constant c1.</summary>
        protected const double KC1 = 3424.0 / 4096.0;

        /// <summary>PQ constant c2.</summary>
        protected const double KC2 = (2413.0 / 4096.0) * 32.0;

        /// <summary>PQ constant c3.</summary>
        protected const double KC3 = (2392.0 / 4096.0) * 32.0;

        /// <summary>
        /// Evaluates the PQ EOTF (<paramref name="e"/> = encoded).
        /// </summary>
        /// <param name="displayIntensityTarget">Display peak luminance in nits.</param>
        /// <param name="e">Encoded value.</param>
        /// <returns>Normalized linear display light.</returns>
        public static double DisplayFromEncoded(float displayIntensityTarget, double e)
        {
            if (e == 0.0)
            {
                return 0.0;
            }

            double originalSign = e;
            e = Math.Abs(e);

            double xp  = Math.Pow(e, 1.0 / KM2);
            double num = Math.Max(xp - KC1, 0.0);
            double den = KC2 - (KC3 * xp);
            double d   = Math.Pow(num / den, 1.0 / KM1);

            return Math.CopySign(d * (10000.0f / displayIntensityTarget), originalSign);
        }

        /// <summary>
        /// Evaluates the inverse PQ EOTF (<paramref name="d"/> = display).
        /// </summary>
        /// <param name="displayIntensityTarget">Display peak luminance in nits.</param>
        /// <param name="d">Normalized linear display light.</param>
        /// <returns>Encoded value.</returns>
        public static double EncodedFromDisplay(float displayIntensityTarget, double d)
        {
            if (d == 0.0)
            {
                return 0.0;
            }

            double originalSign = d;
            d = Math.Abs(d);

            double xp  = Math.Pow(d * (displayIntensityTarget * (1.0f / 10000.0f)), KM1);
            double num = KC1 + (xp * KC2);
            double den = 1.0 + (xp * KC3);
            double e   = Math.Pow(num / den, KM2);

            return Math.CopySign(e, originalSign);
        }
    }

    /// <summary>
    /// Fast single-precision / SIMD Hybrid Log-Gamma (HLG) transfer function.
    /// Ported from <c>jxl::TF_HLG</c> in <c>lib/jxl/cms/transfer_functions-inl.h</c>.
    /// </summary>
    public sealed class TfHlg : TfHlgBase
    {
        private const float K05    = 0.5f;
        private const float K3     = 3.0f;
        private const float KInv3  = (float)(1.0 / 3.0);
        private const float KHiAdd = (float)(KB * KInv12);

        // std::exp(-kC * kRA) * kInv12;
        private const float KHiMul = 0.003639807079052639f;

        // kRA * std::log2e_v
        private const float KHiPow = 8.067285659607931f;

        /// <summary>
        /// Evaluates the inverse HLG EOTF for a single float (maximum error 5e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light.</param>
        /// <returns>Encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float EncodedFromDisplay(float x)
        {
            return EncodedFromDisplay(Vector128.Create(x)).GetElement(0);
        }

        /// <summary>
        /// Evaluates the inverse HLG EOTF for a 4-lane vector (maximum error 5e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light vector.</param>
        /// <returns>Encoded value vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> EncodedFromDisplay(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            Vector128<float> belowInv12 = Vector128.LessThanOrEqual(x, Vector128.Create((float)KInv12));
            Vector128<float> lo         = Vector128.Sqrt(Vector128.Create(K3) * x);
            Vector128<float> logArg     = (Vector128.Create(12.0f) * x) + Vector128.Create((float)(-KB));
            Vector128<float> hi         = (Vector128.Create((float)(KA * Common.kInvLog2e)) * FastMath.FastLog2f(logArg)) + Vector128.Create((float)KC);
            Vector128<float> magnitude  = Vector128.ConditionalSelect(belowInv12, lo, hi);

            return Vector128.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates the inverse HLG EOTF for a 8-lane vector (maximum error 5e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light vector.</param>
        /// <returns>Encoded value vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> EncodedFromDisplay(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            Vector256<float> belowInv12 = Vector256.LessThanOrEqual(x, Vector256.Create((float)KInv12));
            Vector256<float> lo         = Vector256.Sqrt(Vector256.Create(K3) * x);
            Vector256<float> logArg     = (Vector256.Create(12.0f) * x) + Vector256.Create((float)(-KB));
            Vector256<float> hi         = (Vector256.Create((float)(KA * Common.kInvLog2e)) * FastMath.FastLog2f(logArg)) + Vector256.Create((float)KC);
            Vector256<float> magnitude  = Vector256.ConditionalSelect(belowInv12, lo, hi);

            return Vector256.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates the HLG EOTF for a single float.
        /// </summary>
        /// <param name="x">Encoded value.</param>
        /// <returns>Normalized linear display light.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DisplayFromEncoded(float x)
        {
            return DisplayFromEncoded(Vector128.Create(x)).GetElement(0);
        }

        /// <summary>
        /// Evaluates the HLG EOTF for a 4-lane vector.
        /// </summary>
        /// <param name="x">Encoded value vector.</param>
        /// <returns>Normalized linear display light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> DisplayFromEncoded(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            Vector128<float> below05   = Vector128.LessThanOrEqual(x, Vector128.Create(K05));
            Vector128<float> lo        = x * (x * Vector128.Create(KInv3));
            Vector128<float> hi        = (FastMath.FastPow2f(x * Vector128.Create(KHiPow)) * Vector128.Create(KHiMul)) + Vector128.Create(KHiAdd);
            Vector128<float> magnitude = Vector128.ConditionalSelect(below05, lo, hi);

            return Vector128.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates the HLG EOTF for a 8-lane vector.
        /// </summary>
        /// <param name="x">Encoded value vector.</param>
        /// <returns>Normalized linear display light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> DisplayFromEncoded(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            Vector256<float> below05   = Vector256.LessThanOrEqual(x, Vector256.Create(K05));
            Vector256<float> lo        = x * (x * Vector256.Create(KInv3));
            Vector256<float> hi        = (FastMath.FastPow2f(x * Vector256.Create(KHiPow)) * Vector256.Create(KHiMul)) + Vector256.Create(KHiAdd);
            Vector256<float> magnitude = Vector256.ConditionalSelect(below05, lo, hi);

            return Vector256.AndNot(magnitude, signMask) | originalSign;
        }
    }

    /// <summary>
    /// BT.709 transfer function.
    /// Ported from <c>jxl::TF_709</c> in <c>lib/jxl/cms/transfer_functions-inl.h</c>.
    /// </summary>
    public sealed class Tf709
    {
        private const double KThresh = 0.018;
        private const double KMulLow = 4.5;
        private const double KMulHi  = 1.099;
        private const double KPowHi  = 0.45;
        private const double KSub    = -0.099;

        private const float KInvThresh = 0.081f;
        private const float KInvMulLow = (float)(1.0 / 4.5);
        private const float KInvMulHi  = (float)(1.0 / 1.099);
        private const float KInvPowHi  = (float)(1.0 / 0.45);
        private const float KInvAdd    = (float)(0.099 / 1.099);

        /// <summary>
        /// Evaluates double-precision BT.709 OETF.
        /// </summary>
        /// <param name="d">Linear light value.</param>
        /// <returns>BT.709 encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double EncodedFromDisplay(double d)
        {
            if (d < KThresh)
            {
                return KMulLow * d;
            }

            return (KMulHi * Math.Pow(d, KPowHi)) + KSub;
        }

        /// <summary>
        /// Evaluates single-precision BT.709 OETF (maximum error 1e-6).
        /// </summary>
        /// <param name="x">Linear light value.</param>
        /// <returns>BT.709 encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float EncodedFromDisplay(float x)
        {
            return EncodedFromDisplay(Vector128.Create(x)).GetElement(0);
        }

        /// <summary>
        /// Evaluates SIMD BT.709 OETF (maximum error 1e-6).
        /// </summary>
        /// <param name="x">Linear light vector.</param>
        /// <returns>BT.709 encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> EncodedFromDisplay(Vector128<float> x)
        {
            Vector128<float> low  = Vector128.Create((float)KMulLow) * x;
            Vector128<float> hi   = (Vector128.Create((float)KMulHi) * FastMath.FastPowf(x, Vector128.Create((float)KPowHi))) + Vector128.Create((float)KSub);
            Vector128<float> mask = Vector128.LessThanOrEqual(x, Vector128.Create((float)KThresh));

            return Vector128.ConditionalSelect(mask, low, hi);
        }

        /// <summary>
        /// Evaluates SIMD BT.709 OETF (maximum error 1e-6).
        /// </summary>
        /// <param name="x">Linear light vector.</param>
        /// <returns>BT.709 encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> EncodedFromDisplay(Vector256<float> x)
        {
            Vector256<float> low  = Vector256.Create((float)KMulLow) * x;
            Vector256<float> hi   = (Vector256.Create((float)KMulHi) * FastMath.FastPowf(x, Vector256.Create((float)KPowHi))) + Vector256.Create((float)KSub);
            Vector256<float> mask = Vector256.LessThanOrEqual(x, Vector256.Create((float)KThresh));

            return Vector256.ConditionalSelect(mask, low, hi);
        }

        /// <summary>
        /// Evaluates single-precision BT.709 EOTF.
        /// </summary>
        /// <param name="x">BT.709 encoded value.</param>
        /// <returns>Linear light value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DisplayFromEncoded(float x)
        {
            return DisplayFromEncoded(Vector128.Create(x)).GetElement(0);
        }

        /// <summary>
        /// Evaluates SIMD BT.709 EOTF.
        /// </summary>
        /// <param name="x">BT.709 encoded vector.</param>
        /// <returns>Linear light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> DisplayFromEncoded(Vector128<float> x)
        {
            Vector128<float> low  = Vector128.Create(KInvMulLow) * x;
            Vector128<float> hi   = FastMath.FastPowf((x * Vector128.Create(KInvMulHi)) + Vector128.Create(KInvAdd), Vector128.Create(KInvPowHi));
            Vector128<float> mask = Vector128.LessThan(x, Vector128.Create(KInvThresh));

            return Vector128.ConditionalSelect(mask, low, hi);
        }

        /// <summary>
        /// Evaluates SIMD BT.709 EOTF.
        /// </summary>
        /// <param name="x">BT.709 encoded vector.</param>
        /// <returns>Linear light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> DisplayFromEncoded(Vector256<float> x)
        {
            Vector256<float> low  = Vector256.Create(KInvMulLow) * x;
            Vector256<float> hi   = FastMath.FastPowf((x * Vector256.Create(KInvMulHi)) + Vector256.Create(KInvAdd), Vector256.Create(KInvPowHi));
            Vector256<float> mask = Vector256.LessThan(x, Vector256.Create(KInvThresh));

            return Vector256.ConditionalSelect(mask, low, hi);
        }
    }

    /// <summary>
    /// Fast rational-polynomial Perceptual Quantization (PQ) transfer function.
    /// Ported from <c>jxl::TF_PQ</c> in <c>lib/jxl/cms/transfer_functions-inl.h</c>.
    /// </summary>
    public sealed class TfPq : TfPqBase
    {
        private static readonly float[] DisplayP =
        {
            2.62975656e-04f,
            -6.23553089e-03f,
            7.38602301e-01f,
            2.64553172e+00f,
            5.50034862e-01f,
        };

        private static readonly float[] DisplayQ =
        {
            4.21350107e+02f,
            -4.28736818e+02f,
            1.74364667e+02f,
            -3.39078883e+01f,
            2.67718770e+00f,
        };

        private static readonly float[] EncodedPHi =
        {
            1.351392e-02f,
            -1.095778e+00f,
            5.522776e+01f,
            1.492516e+02f,
            4.838434e+01f,
        };

        private static readonly float[] EncodedQHi =
        {
            1.012416e+00f,
            2.016708e+01f,
            9.263710e+01f,
            1.120607e+02f,
            2.590418e+01f,
        };

        private static readonly float[] EncodedPLo =
        {
            9.863406e-06f,
            3.881234e-01f,
            1.352821e+02f,
            6.889862e+04f,
            -2.864824e+05f,
        };

        private static readonly float[] EncodedQLo =
        {
            3.371868e+01f,
            1.477719e+03f,
            1.608477e+04f,
            -4.389884e+04f,
            -2.072546e+05f,
        };

        private readonly float _displayScalingFactorTo10000Nits;
        private readonly float _displayScalingFactorFrom10000Nits;

        /// <summary>
        /// Initializes a new instance of the <see cref="TfPq"/> class.
        /// </summary>
        /// <param name="displayIntensityTarget">Display peak luminance in nits.</param>
        public TfPq(float displayIntensityTarget = Common.kDefaultIntensityTarget)
        {
            _displayScalingFactorTo10000Nits   = displayIntensityTarget * (1.0f / 10000.0f);
            _displayScalingFactorFrom10000Nits = 10000.0f / displayIntensityTarget;
        }

        /// <summary>
        /// Evaluates single-precision PQ EOTF (maximum error 3e-6).
        /// </summary>
        /// <param name="x">PQ encoded value.</param>
        /// <returns>Normalized linear display light.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DisplayFromEncoded(float x)
        {
            uint bits         = BitConverter.SingleToUInt32Bits(x);
            uint originalSign = bits & 0x80000000u;
            x = BitConverter.UInt32BitsToSingle(bits & 0x7FFFFFFFu);

            float xpxx      = (x * x) + x;
            float magnitude = RationalPolynomial.EvalRationalPolynomial(xpxx, DisplayP, DisplayQ) * _displayScalingFactorFrom10000Nits;
            uint magBits    = BitConverter.SingleToUInt32Bits(magnitude) & 0x7FFFFFFFu;

            return BitConverter.UInt32BitsToSingle(magBits | originalSign);
        }

        /// <summary>
        /// Evaluates SIMD PQ EOTF (maximum error 3e-6).
        /// </summary>
        /// <param name="x">PQ encoded vector.</param>
        /// <returns>Normalized linear display light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> DisplayFromEncoded(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            // 4-over-4-degree rational polynomial approximation on x+x*x. This improves
            // the maximum error by about 5x over a rational polynomial for x.
            Vector128<float> xpxx      = (x * x) + x;
            Vector128<float> magnitude = RationalPolynomial.EvalRationalPolynomial(xpxx, DisplayP, DisplayQ);
            Vector128<float> scaled    = magnitude * Vector128.Create(_displayScalingFactorFrom10000Nits);

            return Vector128.AndNot(scaled, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates SIMD PQ EOTF (maximum error 3e-6).
        /// </summary>
        /// <param name="x">PQ encoded vector.</param>
        /// <returns>Normalized linear display light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> DisplayFromEncoded(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            // 4-over-4-degree rational polynomial approximation on x+x*x. This improves
            // the maximum error by about 5x over a rational polynomial for x.
            Vector256<float> xpxx      = (x * x) + x;
            Vector256<float> magnitude = RationalPolynomial.EvalRationalPolynomial(xpxx, DisplayP, DisplayQ);
            Vector256<float> scaled    = magnitude * Vector256.Create(_displayScalingFactorFrom10000Nits);

            return Vector256.AndNot(scaled, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates single-precision inverse PQ EOTF (maximum error 7e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light.</param>
        /// <returns>PQ encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float EncodedFromDisplay(float x)
        {
            uint bits         = BitConverter.SingleToUInt32Bits(x);
            uint originalSign = bits & 0x80000000u;
            x = BitConverter.UInt32BitsToSingle(bits & 0x7FFFFFFFu);

            float xto025    = MathF.Sqrt(MathF.Sqrt(x * _displayScalingFactorTo10000Nits));
            float magnitude = x < 1e-4f
                ? RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPLo, EncodedQLo)
                : RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPHi, EncodedQHi);
            uint magBits    = BitConverter.SingleToUInt32Bits(magnitude) & 0x7FFFFFFFu;

            return BitConverter.UInt32BitsToSingle(magBits | originalSign);
        }

        /// <summary>
        /// Evaluates SIMD inverse PQ EOTF (maximum error 7e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light vector.</param>
        /// <returns>PQ encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> EncodedFromDisplay(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            // 4-over-4-degree rational polynomial approximation on x**0.25, with two
            // different polynomials above and below 1e-4.
            Vector128<float> xto025    = Vector128.Sqrt(Vector128.Sqrt(x * Vector128.Create(_displayScalingFactorTo10000Nits)));
            Vector128<float> lo        = RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPLo, EncodedQLo);
            Vector128<float> hi        = RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPHi, EncodedQHi);
            Vector128<float> magnitude = Vector128.ConditionalSelect(Vector128.LessThan(x, Vector128.Create(1e-4f)), lo, hi);

            return Vector128.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates SIMD inverse PQ EOTF (maximum error 7e-7).
        /// </summary>
        /// <param name="x">Normalized linear display light vector.</param>
        /// <returns>PQ encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> EncodedFromDisplay(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            // 4-over-4-degree rational polynomial approximation on x**0.25, with two
            // different polynomials above and below 1e-4.
            Vector256<float> xto025    = Vector256.Sqrt(Vector256.Sqrt(x * Vector256.Create(_displayScalingFactorTo10000Nits)));
            Vector256<float> lo        = RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPLo, EncodedQLo);
            Vector256<float> hi        = RationalPolynomial.EvalRationalPolynomial(xto025, EncodedPHi, EncodedQHi);
            Vector256<float> magnitude = Vector256.ConditionalSelect(Vector256.LessThan(x, Vector256.Create(1e-4f)), lo, hi);

            return Vector256.AndNot(magnitude, signMask) | originalSign;
        }
    }

    /// <summary>
    /// Fast rational-polynomial sRGB transfer function.
    /// Ported from <c>jxl::TF_SRGB</c> and <c>jxl::FastLinearToSRGB</c> in <c>lib/jxl/cms/transfer_functions-inl.h</c>.
    /// </summary>
    public sealed class TfSrgb
    {
        private const float KThreshSrgbToLinear = 0.04045f;
        private const float KThreshLinearToSrgb = 0.0031308f;
        private const float KLowDiv             = 12.92f;
        private const float KLowDivInv          = 1.0f / KLowDiv;

        private static readonly float[] DisplayP =
        {
            2.200248328e-04f,
            1.043637593e-02f,
            1.624820318e-01f,
            7.961564959e-01f,
            8.210152774e-01f,
        };

        private static readonly float[] DisplayQ =
        {
            2.631846970e-01f,
            1.076976492e+00f,
            4.987528350e-01f,
            -5.512498495e-02f,
            6.521209011e-03f,
        };

        private static readonly float[] EncodedP =
        {
            -5.135152395e-04f,
            5.287254571e-03f,
            3.903842876e-01f,
            1.474205315e+00f,
            7.352629620e-01f,
        };

        private static readonly float[] EncodedQ =
        {
            1.004519624e-02f,
            3.036675394e-01f,
            1.340816930e+00f,
            9.258482155e-01f,
            2.424867759e-02f,
        };

        private static readonly byte[] K2To512Powers25To18Bits =
        {
            0x0,  0xa,  0x19, 0x26, 0x32, 0x41, 0x4d, 0x5c,
            0x68, 0x75, 0x83, 0x8f, 0xa0, 0xaa, 0xb9, 0xc6,
        };

        private static readonly byte[] K2To512Powers17To10Bits =
        {
            0x0,  0xb7, 0x4,  0xd,  0xcb, 0xe7, 0x41, 0x68,
            0x51, 0xd1, 0xeb, 0xf2, 0x0,  0xb7, 0x4,  0xd,
        };

        private static readonly Vector256<uint> FastSrgbMulLow = CreateFastSrgbMultipliers(0);
        private static readonly Vector256<uint> FastSrgbMulHigh = CreateFastSrgbMultipliers(8);

        private static Vector256<uint> CreateFastSrgbMultipliers(int offset)
        {
            Span<uint> bits = stackalloc uint[8];
            for (int lane = 0; lane < bits.Length; lane++)
            {
                bits[lane] = ((uint)K2To512Powers25To18Bits[offset + lane] << 18) |
                             ((uint)K2To512Powers17To10Bits[offset + lane] << 10) | 0x40000000u;
            }
            return Vector256.Create(bits);
        }

        /// <summary>
        /// Linear to sRGB conversion with error of at most 1.2e-4 (scalar).
        /// </summary>
        /// <param name="v">Linear input value.</param>
        /// <returns>sRGB encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastLinearToSrgb(float v)
        {
            if (v < 0.0031308f)
            {
                return v * 12.92f;
            }

            const uint k2To512PowersBaseBits = 0x40000000u;
            uint vBits                       = BitConverter.SingleToUInt32Bits(v);

            // Convert to 0.25 - 0.5 range.
            float v025To05 = BitConverter.UInt32BitsToSingle((vBits | 0x3e800000u) & 0x3effffffu);

            // third degree polynomial approximation between 0.25 and 0.5
            // of 1.055/2^(7/2.4) * x^(1/2.4) * 0.5.
            float d1  = (v025To05 * 0.059914046f) - 0.108894556f;
            float d2  = (d1 * v025To05) + 0.107963754f;
            float pow = (d2 * v025To05) + 0.018092343f;

            uint exp     = ((vBits >> 23) - 118u) & 0xfu;
            uint mulBits = ((uint)K2To512Powers25To18Bits[exp] << 18) |
                           ((uint)K2To512Powers17To10Bits[exp] << 10) |
                           k2To512PowersBaseBits;
            float mul    = BitConverter.UInt32BitsToSingle(mulBits);

            return (pow * mul) - 0.055f;
        }

        /// <summary>
        /// Linear to sRGB conversion with error of at most 1.2e-4 (SIMD).
        /// </summary>
        /// <param name="v">Linear input vector.</param>
        /// <returns>sRGB encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> FastLinearToSrgb(Vector128<float> v)
        {
            return Vector128.Create(
                FastLinearToSrgb(v.GetElement(0)),
                FastLinearToSrgb(v.GetElement(1)),
                FastLinearToSrgb(v.GetElement(2)),
                FastLinearToSrgb(v.GetElement(3)));
        }

        /// <summary>Converts eight linear samples to sRGB using the scalar approximation's coefficients and exponent table.</summary>
        /// <param name="v">Linear input samples.</param>
        /// <returns>sRGB encoded samples.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> FastLinearToSrgb(Vector256<float> v)
        {
            Vector256<uint> bits = v.AsUInt32();
            Vector256<float> reduced = ((bits | Vector256.Create(0x3e800000u)) & Vector256.Create(0x3effffffu)).AsSingle();
            Vector256<float> d1 = (reduced * Vector256.Create(0.059914046f)) - Vector256.Create(0.108894556f);
            Vector256<float> d2 = (d1 * reduced) + Vector256.Create(0.107963754f);
            Vector256<float> pow = (d2 * reduced) + Vector256.Create(0.018092343f);
            Vector256<uint> exponent = (Vector256.ShiftRightLogical(bits, 23) - Vector256.Create(118u)) & Vector256.Create(15u);
            Vector256<uint> index = exponent & Vector256.Create(7u);
            Vector256<uint> low = Vector256.Shuffle(FastSrgbMulLow, index);
            Vector256<uint> high = Vector256.Shuffle(FastSrgbMulHigh, index);
            Vector256<float> mul = Vector256.ConditionalSelect(Vector256.LessThan(exponent, Vector256.Create(8u)), low, high).AsSingle();
            return Vector256.ConditionalSelect(Vector256.LessThan(v, Vector256.Create(0.0031308f)),
                v * Vector256.Create(12.92f), (pow * mul) - Vector256.Create(0.055f));
        }


        /// <summary>
        /// Evaluates single-precision sRGB EOTF.
        /// </summary>
        /// <param name="x">sRGB encoded value.</param>
        /// <returns>Linear light value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DisplayFromEncoded(float x)
        {
            uint bits         = BitConverter.SingleToUInt32Bits(x);
            uint originalSign = bits & 0x80000000u;
            x = BitConverter.UInt32BitsToSingle(bits & 0x7FFFFFFFu);

            float magnitude = x > KThreshSrgbToLinear
                ? RationalPolynomial.EvalRationalPolynomial(x, DisplayP, DisplayQ)
                : x * KLowDivInv;
            uint magBits    = BitConverter.SingleToUInt32Bits(magnitude) & 0x7FFFFFFFu;

            return BitConverter.UInt32BitsToSingle(magBits | originalSign);
        }

        /// <summary>
        /// Evaluates SIMD sRGB EOTF.
        /// </summary>
        /// <param name="x">sRGB encoded vector.</param>
        /// <returns>Linear light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> DisplayFromEncoded(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            Vector128<float> linear    = x * Vector128.Create(KLowDivInv);
            Vector128<float> poly      = RationalPolynomial.EvalRationalPolynomial(x, DisplayP, DisplayQ);
            Vector128<float> magnitude = Vector128.ConditionalSelect(
                Vector128.GreaterThan(x, Vector128.Create(KThreshSrgbToLinear)),
                poly,
                linear);

            return Vector128.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates SIMD sRGB EOTF.
        /// </summary>
        /// <param name="x">sRGB encoded vector.</param>
        /// <returns>Linear light vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> DisplayFromEncoded(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            Vector256<float> linear    = x * Vector256.Create(KLowDivInv);
            Vector256<float> poly      = RationalPolynomial.EvalRationalPolynomial(x, DisplayP, DisplayQ);
            Vector256<float> magnitude = Vector256.ConditionalSelect(
                Vector256.GreaterThan(x, Vector256.Create(KThreshSrgbToLinear)),
                poly,
                linear);

            return Vector256.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates single-precision inverse sRGB EOTF (error ~5e-7).
        /// </summary>
        /// <param name="x">Linear light value.</param>
        /// <returns>sRGB encoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float EncodedFromDisplay(float x)
        {
            uint bits         = BitConverter.SingleToUInt32Bits(x);
            uint originalSign = bits & 0x80000000u;
            x = BitConverter.UInt32BitsToSingle(bits & 0x7FFFFFFFu);

            float magnitude = x > KThreshLinearToSrgb
                ? RationalPolynomial.EvalRationalPolynomial(MathF.Sqrt(x), EncodedP, EncodedQ)
                : x * KLowDiv;
            uint magBits    = BitConverter.SingleToUInt32Bits(magnitude) & 0x7FFFFFFFu;

            return BitConverter.UInt32BitsToSingle(magBits | originalSign);
        }

        /// <summary>
        /// Evaluates SIMD inverse sRGB EOTF (error ~5e-7).
        /// </summary>
        /// <param name="x">Linear light vector.</param>
        /// <returns>sRGB encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> EncodedFromDisplay(Vector128<float> x)
        {
            Vector128<float> signMask     = Vector128.Create(0x80000000u).AsSingle();
            Vector128<float> originalSign = x & signMask;
            x = Vector128.AndNot(x, signMask); // abs

            Vector128<float> linear    = x * Vector128.Create(KLowDiv);
            Vector128<float> poly      = RationalPolynomial.EvalRationalPolynomial(Vector128.Sqrt(x), EncodedP, EncodedQ);
            Vector128<float> magnitude = Vector128.ConditionalSelect(
                Vector128.GreaterThan(x, Vector128.Create(KThreshLinearToSrgb)),
                poly,
                linear);

            return Vector128.AndNot(magnitude, signMask) | originalSign;
        }

        /// <summary>
        /// Evaluates SIMD inverse sRGB EOTF (error ~5e-7).
        /// </summary>
        /// <param name="x">Linear light vector.</param>
        /// <returns>sRGB encoded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<float> EncodedFromDisplay(Vector256<float> x)
        {
            Vector256<float> signMask     = Vector256.Create(0x80000000u).AsSingle();
            Vector256<float> originalSign = x & signMask;
            x = Vector256.AndNot(x, signMask); // abs

            Vector256<float> linear    = x * Vector256.Create(KLowDiv);
            Vector256<float> poly      = RationalPolynomial.EvalRationalPolynomial(Vector256.Sqrt(x), EncodedP, EncodedQ);
            Vector256<float> magnitude = Vector256.ConditionalSelect(
                Vector256.GreaterThan(x, Vector256.Create(KThreshLinearToSrgb)),
                poly,
                linear);

            return Vector256.AndNot(magnitude, signMask) | originalSign;
        }
    }
}
