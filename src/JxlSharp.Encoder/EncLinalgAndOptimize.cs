// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// 2-element double-precision vector for 2x2 linear algebra operations.
    /// Ported from <c>jxl::Vector2</c> in <c>lib/jxl/enc_linalg.h</c>.
    /// </summary>
    public struct Vector2D
    {
        /// <summary>First element.</summary>
        public double X;

        /// <summary>Second element.</summary>
        public double Y;

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector2D"/> struct.
        /// </summary>
        /// <param name="x">First element.</param>
        /// <param name="y">Second element.</param>
        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Gets or sets the element at <paramref name="index"/> (0 or 1).
        /// </summary>
        /// <param name="index">Element index.</param>
        public double this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => index == 0 ? X : Y;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (index == 0)
                {
                    X = value;
                }
                else
                {
                    Y = value;
                }
            }
        }
    }

    /// <summary>
    /// 2x2 double-precision matrix (<c>row</c>, <c>column</c>).
    /// Ported from <c>jxl::Matrix2x2</c> in <c>lib/jxl/enc_linalg.h</c>.
    /// </summary>
    public struct Matrix2x2
    {
        /// <summary>Row 0.</summary>
        public Vector2D Row0;

        /// <summary>Row 1.</summary>
        public Vector2D Row1;

        /// <summary>
        /// Initializes a new instance of the <see cref="Matrix2x2"/> struct.
        /// </summary>
        /// <param name="m00">Element (0, 0).</param>
        /// <param name="m01">Element (0, 1).</param>
        /// <param name="m10">Element (1, 0).</param>
        /// <param name="m11">Element (1, 1).</param>
        public Matrix2x2(double m00, double m01, double m10, double m11)
        {
            Row0 = new Vector2D(m00, m01);
            Row1 = new Vector2D(m10, m11);
        }

        /// <summary>
        /// Gets or sets the element at (<paramref name="row"/>, <paramref name="col"/>).
        /// </summary>
        /// <param name="row">Row index (0 or 1).</param>
        /// <param name="col">Column index (0 or 1).</param>
        public double this[int row, int col]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => row == 0 ? Row0[col] : Row1[col];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (row == 0)
                {
                    Row0[col] = value;
                }
                else
                {
                    Row1[col] = value;
                }
            }
        }
    }

    /// <summary>
    /// 2x2 symmetric matrix eigendecomposition utilities.
    /// Ported from <c>lib/jxl/enc_linalg.h</c> and <c>lib/jxl/enc_linalg.cc</c>.
    /// </summary>
    public static class EncLinalg
    {
        /// <summary>
        /// Decomposes symmetric 2x2 matrix <paramref name="a"/> into orthogonal matrix <paramref name="u"/> and diagonal <paramref name="diag"/>
        /// such that <c>A = U * Diagonal(diag) * Transpose(U)</c>.
        /// Ported from <c>jxl::ConvertToDiagonal</c> in <c>lib/jxl/enc_linalg.cc</c>.
        /// </summary>
        /// <param name="a">Symmetric 2x2 input matrix.</param>
        /// <param name="diag">Output eigenvalues.</param>
        /// <param name="u">Output orthogonal eigenvector matrix.</param>
        public static void ConvertToDiagonal(in Matrix2x2 a, out Vector2D diag, out Matrix2x2 u)
        {
            // Check A is symmetric.
            Debug.Assert(Math.Abs(a[0, 1] - a[1, 0]) < 1e-15);

            double b = -(a[0, 0] + a[1, 1]);
            double c = (a[0, 0] * a[1, 1]) - (a[0, 1] * a[0, 1]);
            double d = (b * b) - (4.0 * c);

            if (Math.Abs(a[0, 1]) < 1e-10 || d < 0.0)
            {
                // Already diagonal.
                diag = new Vector2D(a[0, 0], a[1, 1]);
                u    = new Matrix2x2(1.0, 0.0, 0.0, 1.0);

                return;
            }

            double sqd = Math.Sqrt(d);
            double l1  = (-b - sqd) * 0.5;
            double l2  = (-b + sqd) * 0.5;

            double v1x = a[0, 0] - l1;
            double v1y = a[1, 0];
            double v1n = 1.0 / double.Hypot(v1x, v1y);
            v1x       *= v1n;
            v1y       *= v1n;

            diag = new Vector2D(l1, l2);
            u    = new Matrix2x2(v1y, -v1x, v1x, v1y);
        }

        /// <summary>
        /// Decomposes symmetric 2x2 array <paramref name="a"/> into orthogonal 2x2 array <paramref name="u"/> and diagonal <paramref name="diag"/>.
        /// </summary>
        /// <param name="a">2x2 symmetric input array.</param>
        /// <param name="diag">2-element eigenvalue span.</param>
        /// <param name="u">2x2 orthogonal output array.</param>
        public static void ConvertToDiagonal(double[,] a, Span<double> diag, double[,] u)
        {
            Matrix2x2 matA = new Matrix2x2(a[0, 0], a[0, 1], a[1, 0], a[1, 1]);
            ConvertToDiagonal(in matA, out Vector2D vecDiag, out Matrix2x2 matU);

            diag[0] = vecDiag.X;
            diag[1] = vecDiag.Y;
            u[0, 0] = matU[0, 0];
            u[0, 1] = matU[0, 1];
            u[1, 0] = matU[1, 0];
            u[1, 1] = matU[1, 1];
        }
    }

    /// <summary>
    /// Interface for differentiable objective functions optimized by <see cref="EncOptimize"/>.
    /// Ported from the <c>Function</c> concept in <c>lib/jxl/enc_optimize.h</c>.
    /// </summary>
    public interface IScaledConjugateGradientFunction
    {
        /// <summary>
        /// Computes the objective value at <paramref name="w"/> and writes the negative gradient vector into <paramref name="df"/>.
        /// </summary>
        /// <param name="w">Current parameter vector.</param>
        /// <param name="df">Output negative gradient vector at <paramref name="w"/>.</param>
        /// <returns>Objective value at <paramref name="w"/>.</returns>
        double Compute(ReadOnlySpan<double> w, Span<double> df);
    }

    /// <summary>
    /// Multi-dimensional nonlinear optimization using the Scaled Conjugate Gradient method.
    /// Ported from <c>lib/jxl/enc_optimize.h</c>.
    /// </summary>
    public static class EncOptimize
    {
        /// <summary>
        /// Optimizes <paramref name="f"/> starting from <paramref name="w0"/> using the Scaled Conjugate Gradient method.
        /// Ported from <c>jxl::optimize::OptimizeWithScaledConjugateGradientMethod</c> in <c>lib/jxl/enc_optimize.h</c>.
        /// </summary>
        /// <typeparam name="TFunction">Type implementing <see cref="IScaledConjugateGradientFunction"/>.</typeparam>
        /// <param name="f">Objective and negative-gradient evaluator.</param>
        /// <param name="w0">Initial parameter vector.</param>
        /// <param name="gradNormThreshold">Convergence threshold on gradient norm.</param>
        /// <param name="maxIters">Maximum number of iterations.</param>
        /// <returns>Optimized parameter vector.</returns>
        public static double[] OptimizeWithScaledConjugateGradientMethod<TFunction>(
            TFunction f,
            ReadOnlySpan<double> w0,
            double gradNormThreshold,
            nuint maxIters)
            where TFunction : IScaledConjugateGradientFunction
        {
            int n               = w0.Length;
            double rsqThreshold = gradNormThreshold * gradNormThreshold;
            double[] w          = w0.ToArray();
            double[] wp         = new double[n];
            double[] r          = new double[n];
            double[] rt         = new double[n];
            double[] e          = new double[n];
            double[] p          = new double[n];

            double fw      = f.Compute(w, r);
            double rsq     = Dot(r, r);
            Array.Copy(r, e, n);
            Array.Copy(r, p, n);

            ScgState state = new ScgState
            {
                Fw      = fw,
                Rsq     = rsq,
                L       = 1.0,
                Success = true,
            };

            for (nuint k = 0; k < maxIters; k++)
            {
                if (StepScaledConjugateGradient(f, n, rsqThreshold, w, wp, (r, rt, e, p), ref state))
                {
                    break;
                }
            }

            return w;
        }

        /// <summary>
        /// Optimizes a delegate objective function starting from <paramref name="w0"/> using the Scaled Conjugate Gradient method.
        /// </summary>
        /// <param name="compute">Delegate accepting <c>(w, df)</c> and returning the objective value.</param>
        /// <param name="w0">Initial parameter vector.</param>
        /// <param name="gradNormThreshold">Convergence threshold on gradient norm.</param>
        /// <param name="maxIters">Maximum number of iterations.</param>
        /// <returns>Optimized parameter vector.</returns>
        public static double[] OptimizeWithScaledConjugateGradientMethod(
            Func<double[], double[], double> compute,
            ReadOnlySpan<double> w0,
            double gradNormThreshold,
            nuint maxIters)
        {
            DelegateFunction wrapper = new DelegateFunction(compute);

            return OptimizeWithScaledConjugateGradientMethod(wrapper, w0, gradNormThreshold, maxIters);
        }

        private static bool StepScaledConjugateGradient<TFunction>(
            TFunction f,
            int n,
            double rsqThreshold,
            double[] w,
            double[] wp,
            (double[] R, double[] Rt, double[] E, double[] P) vecs,
            ref ScgState state)
            where TFunction : IScaledConjugateGradientFunction
        {
            const double Sigma0 = 0.0001;

            if (state.Success)
            {
                state.M = -Dot(vecs.P, vecs.R);

                if (state.M >= 0.0)
                {
                    Array.Copy(vecs.R, vecs.P, n);
                    state.M = -Dot(vecs.P, vecs.R);
                }

                state.Psq  = Dot(vecs.P, vecs.P);
                double s   = Sigma0 / Math.Sqrt(state.Psq);
                AddScaled(w, s, vecs.P, wp);
                _          = f.Compute(wp, vecs.Rt);
                state.T    = DotDiff(vecs.P, vecs.R, vecs.Rt) / s;
            }

            double d = state.T + (state.L * state.Psq);

            if (d <= 0.0)
            {
                d        = state.L * state.Psq;
                state.L -= state.T / state.Psq;
            }

            double a = -state.M / d;
            AddScaled(w, a, vecs.P, wp);
            double fp    = f.Compute(wp, vecs.Rt);
            double capD  = 2.0 * (fp - state.Fw) / (a * state.M);

            return UpdateScgDirection(
                n,
                rsqThreshold,
                fp,
                capD,
                w,
                wp,
                new ScgUpdateArgs(vecs.R, vecs.Rt, vecs.E, vecs.P, ref state));
        }

        private static bool UpdateScgDirection(
            int n,
            double rsqThreshold,
            double fp,
            double capD,
            double[] w,
            double[] wp,
            ScgUpdateArgs args)
        {
            const double LMin = 1.0e-15;
            const double LMax = 1.0e15;
            ref ScgState state = ref args.State;

            if (capD >= 0.0)
            {
                state.Success = true;
                state.NSuccess++;
                Array.Copy(wp, w, n);
                Array.Copy(args.R, args.E, n);
                Array.Copy(args.Rt, args.R, n);
                state.Rsq = Dot(args.R, args.R);
                state.Fw  = fp;

                if (state.Rsq <= rsqThreshold)
                {
                    return true;
                }
            }
            else
            {
                state.Success = false;
            }

            if (capD < 0.25)
            {
                state.L = Math.Min(4.0 * state.L, LMax);
            }
            else if (capD > 0.75)
            {
                state.L = Math.Max(0.25 * state.L, LMin);
            }

            if ((state.NSuccess % (nuint)n) == 0)
            {
                Array.Copy(args.R, args.P, n);
                state.L = 1.0;
            }
            else if (state.Success)
            {
                double b = DotDiff(args.R, args.E, args.R) / state.M;
                ScaleAndAdd(b, args.P, args.R, args.P);
            }

            return false;
        }

        private static double Dot(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
        {
            double r = 0.0;

            for (int i = 0; i < x.Length; i++)
            {
                r += x[i] * y[i];
            }

            return r;
        }

        private static double DotDiff(ReadOnlySpan<double> p, ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            double r = 0.0;

            for (int i = 0; i < p.Length; i++)
            {
                r += p[i] * (a[i] - b[i]);
            }

            return r;
        }

        private static void AddScaled(ReadOnlySpan<double> x, double s, ReadOnlySpan<double> p, Span<double> dst)
        {
            for (int i = 0; i < x.Length; i++)
            {
                dst[i] = x[i] + (s * p[i]);
            }
        }

        private static void ScaleAndAdd(double b, ReadOnlySpan<double> p, ReadOnlySpan<double> r, Span<double> dst)
        {
            for (int i = 0; i < p.Length; i++)
            {
                dst[i] = (b * p[i]) + r[i];
            }
        }

        private struct ScgState
        {
            public double Psq;
            public double Fw;
            public double Rsq;
            public double T;
            public double M;
            public double L;
            public bool Success;
            public nuint NSuccess;
        }

        private ref struct ScgUpdateArgs
        {
            public double[] R;
            public double[] Rt;
            public double[] E;
            public double[] P;
            public ref ScgState State;

            public ScgUpdateArgs(double[] r, double[] rt, double[] e, double[] p, ref ScgState state)
            {
                R     = r;
                Rt    = rt;
                E     = e;
                P     = p;
                State = ref state;
            }

            public static implicit operator ScgUpdateArgs((double[] R, double[] Rt, double[] E, double[] P, ScgState) unused) =>
                throw new NotSupportedException();
        }

        private readonly struct DelegateFunction : IScaledConjugateGradientFunction
        {
            private readonly Func<double[], double[], double> _fn;

            public DelegateFunction(Func<double[], double[], double> fn)
            {
                _fn = fn;
            }

            public double Compute(ReadOnlySpan<double> w, Span<double> df)
            {
                double[] wArr  = w.ToArray();
                double[] dfArr = new double[df.Length];
                double res     = _fn(wArr, dfArr);
                dfArr.CopyTo(df);

                return res;
            }
        }
    }
}
