// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using JxlSharp.Base;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Delegate that returns a read-only span for row <paramref name="y"/>.
    /// </summary>
    /// <param name="y">Zero-based row index.</param>
    /// <returns>Read-only span of row <paramref name="y"/>.</returns>
    public delegate ReadOnlySpan<float> GetConstRow(nuint y);

    /// <summary>
    /// Delegate that returns a writable span for row <paramref name="y"/>.
    /// </summary>
    /// <param name="y">Zero-based row index.</param>
    /// <returns>Writable span of row <paramref name="y"/>.</returns>
    public delegate Span<float> GetRow(nuint y);

    /// <summary>
    /// Bundles row accessor delegates for 2D Gaussian filtering.
    /// </summary>
    public readonly struct GaussianRowAccessors
    {
        /// <summary>Input row accessor.</summary>
        public readonly GetConstRow In;

        /// <summary>Temporary intermediate row accessor.</summary>
        public readonly GetRow Temp;

        /// <summary>Output row accessor.</summary>
        public readonly GetRow Out;

        /// <summary>
        /// Initializes a new instance of the <see cref="GaussianRowAccessors"/> struct.
        /// </summary>
        /// <param name="inRow">Input row accessor.</param>
        /// <param name="tempRow">Temporary row accessor.</param>
        /// <param name="outRow">Output row accessor.</param>
        public GaussianRowAccessors(GetConstRow inRow, GetRow tempRow, GetRow outRow)
        {
            In   = inRow;
            Temp = tempRow;
            Out  = outRow;
        }
    }

    /// <summary>
    /// Precomputed recursive IIR filter coefficients for <see cref="GaussBlur"/>.
    /// Port of <c>jxl::RecursiveGaussian</c> from <c>tools/gauss_blur.h</c>.
    /// </summary>
    public sealed class RecursiveGaussian
    {
        /// <summary>
        /// Numerator coefficients for k={1,3,5}, each broadcasted 4x.
        /// </summary>
        public readonly float[] N2       = new float[3 * 4];

        /// <summary>
        /// Denominator coefficients for k={1,3,5}, each broadcasted 4x.
        /// </summary>
        public readonly float[] D1       = new float[3 * 4];

        /// <summary>
        /// Horizontal unrolled 4-lane multipliers for the previous output.
        /// </summary>
        public readonly float[] MulPrev  = new float[3 * 4];

        /// <summary>
        /// Horizontal unrolled 4-lane multipliers for the second-to-last output.
        /// </summary>
        public readonly float[] MulPrev2 = new float[3 * 4];

        /// <summary>
        /// Horizontal unrolled 4-lane multipliers for the current inputs 0..3.
        /// </summary>
        public readonly float[] MulIn    = new float[3 * 4];

        /// <summary>
        /// Filter truncation radius <c>N</c>.
        /// </summary>
        public nuint Radius;
    }

    /// <summary>
    /// Fast recursive 1D and 2D Gaussian blur using truncated cosine functions
    /// (Charalampidis 2016). Port of <c>tools/gauss_blur.cc</c>.
    /// </summary>
    public static class GaussBlur
    {
        private const int GaussMaxLanes   = 4;
        private const nuint RingBufferLen  = 4;
        private const nuint RingBufferMask = RingBufferLen - 1;
        private const nuint FastPaceLanes  = 16;

        /// <summary>
        /// Precomputes recursive Gaussian filter coefficients for <paramref name="sigma"/>.
        /// </summary>
        /// <param name="sigma">Standard deviation of the Gaussian kernel.</param>
        /// <returns>Precomputed <see cref="RecursiveGaussian"/> instance.</returns>
        public static RecursiveGaussian CreateRecursiveGaussian(double sigma)
        {
            const double kPi     = 3.141592653589793238;
            RecursiveGaussian rg = new RecursiveGaussian();
            double radius        = Math.Round(3.2795 * sigma + 0.2546);
            double piDiv2R       = kPi / (2.0 * radius);
            double[] omega       = new double[3] { piDiv2R, 3.0 * piDiv2R, 5.0 * piDiv2R };
            double p1            = +1.0 / Math.Tan(0.5 * omega[0]);
            double p3            = -1.0 / Math.Tan(0.5 * omega[1]);
            double p5            = +1.0 / Math.Tan(0.5 * omega[2]);
            double r1            = +p1 * p1 / Math.Sin(omega[0]);
            double r3            = -p3 * p3 / Math.Sin(omega[1]);
            double r5            = +p5 * p5 / Math.Sin(omega[2]);
            double negHalfSigma2 = -0.5 * sigma * sigma;
            double recipRadius   = 1.0 / radius;
            double[] rho         = new double[3];

            for (int i = 0; i < 3; ++i)
            {
                rho[i] = Math.Exp(negHalfSigma2 * omega[i] * omega[i]) * recipRadius;
            }

            double d13        = p1 * r3 - r1 * p3;
            double d35        = p3 * r5 - r3 * p5;
            double d51        = p5 * r1 - r5 * p1;
            double recipD13   = 1.0 / d13;
            double zeta15     = d35 * recipD13;
            double zeta35     = d51 * recipD13;
            Matrix3x3d matrix = new Matrix3x3d(
                new Vector3d(p1, p3, p5),
                new Vector3d(r1, r3, r5),
                new Vector3d(zeta15, zeta35, 1.0));
            Status status     = MatrixOps.Inv3x3Matrix(ref matrix);

            Debug.Assert(status.IsOk);

            Vector3d gamma = new Vector3d(
                1.0,
                radius * radius - sigma * sigma,
                zeta15 * rho[0] + zeta35 * rho[1] + rho[2]);

            MatrixOps.Mul3x3Vector(in matrix, in gamma, out Vector3d beta);

            double sum = beta[0] * p1 + beta[1] * p3 + beta[2] * p5;

            Debug.Assert(Math.Abs(sum - 1.0) < 1e-12);

            rg.Radius = (nuint)(int)radius;

            PopulateRecursiveConstants(rg, radius, omega, in beta);

            return rg;
        }

        /// <summary>
        /// Applies a 1D Gaussian filter with zero-pad boundary handling and runtime independent of sigma.
        /// </summary>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="xsize">Number of elements in the row.</param>
        /// <param name="inRow">Input row span.</param>
        /// <param name="outRow">Output row span.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void FastGaussian1D(
            RecursiveGaussian rg,
            nuint xsize,
            ReadOnlySpan<float> inRow,
            Span<float> outRow)
        {
            nint length       = (nint)xsize;
            nint radius       = (nint)rg.Radius;
            nint n            = -radius + 1;
            nint firstAligned = (nint)Common.RoundUpTo((nuint)(radius + 1), (nuint)GaussMaxLanes);
            nint leftLimit    = Math.Min(firstAligned, length);
            HorizontalState s = new HorizontalState(rg);

            for (; n < leftLimit; ++n)
            {
                StepHorizontalScalar(in s, inRow, outRow, length, radius, n);
            }

            s.BroadcastLane0();

            nint unrolledLimit = length - radius + 1 - (GaussMaxLanes - 1);

            for (; n < unrolledLimit; n += GaussMaxLanes)
            {
                StepHorizontalVector4(in s, inRow, outRow, radius, n);
            }

            for (; n < length; ++n)
            {
                StepHorizontalScalar(in s, inRow, outRow, length, radius, n);
            }
        }

        /// <summary>
        /// Applies a 2D Gaussian filter with zero-pad boundary handling.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary buffer allocation.</param>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="xsize">Width of the image.</param>
        /// <param name="ysize">Height of the image.</param>
        /// <param name="inRow">Input row accessor.</param>
        /// <param name="tempRow">Temporary row accessor.</param>
        /// <param name="outRow">Output row accessor.</param>
        /// <returns>Success or error status.</returns>
        public static Status FastGaussian(
            JxlMemoryManager? memoryManager,
            RecursiveGaussian rg,
            nuint xsize,
            nuint ysize,
            GetConstRow inRow,
            GetRow tempRow,
            GetRow outRow)
        {
            GaussianRowAccessors rows = new GaussianRowAccessors(inRow, tempRow, outRow);

            return FastGaussian(memoryManager, rg, xsize, ysize, rows, null);
        }

        /// <summary>
        /// Applies a 2D Gaussian filter with zero-pad boundary handling and optional thread pool.
        /// </summary>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="xsize">Width of the image.</param>
        /// <param name="ysize">Height of the image.</param>
        /// <param name="inRow">Input row accessor.</param>
        /// <param name="tempRow">Temporary row accessor.</param>
        /// <param name="outRow">Output row accessor.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Success or error status.</returns>
        public static Status FastGaussian(
            RecursiveGaussian rg,
            nuint xsize,
            nuint ysize,
            GetConstRow inRow,
            GetRow tempRow,
            GetRow outRow,
            ThreadPool? pool = null)
        {
            GaussianRowAccessors rows = new GaussianRowAccessors(inRow, tempRow, outRow);

            return FastGaussian(null, rg, xsize, ysize, rows, pool);
        }

        /// <summary>
        /// Applies a 2D Gaussian filter with zero-pad boundary handling using bundled row accessors.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary buffer allocation.</param>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="xsize">Width of the image.</param>
        /// <param name="ysize">Height of the image.</param>
        /// <param name="rows">Row accessors for input, temporary, and output buffers.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Success or error status.</returns>
        public static Status FastGaussian(
            JxlMemoryManager? memoryManager,
            RecursiveGaussian rg,
            nuint xsize,
            nuint ysize,
            GaussianRowAccessors rows,
            ThreadPool? pool = null)
        {
            Status horizontalStatus = FastGaussianHorizontal(rg, xsize, ysize, rows.In, rows.Temp, pool);

            if (!horizontalStatus)
            {
                return horizontalStatus;
            }

            GetConstRow tempIn = y => rows.Temp(y);

            return FastGaussianVertical(memoryManager, rg, xsize, ysize, tempIn, rows.Out);
        }

        /// <summary>
        /// Applies a 2D Gaussian filter directly on <see cref="ImagePlane{T}"/> instances.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary buffer allocation.</param>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="inPlane">Input plane.</param>
        /// <param name="tempPlane">Temporary plane of matching dimensions.</param>
        /// <param name="outPlane">Output plane of matching dimensions.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Success or error status.</returns>
        public static Status FastGaussian(
            JxlMemoryManager? memoryManager,
            RecursiveGaussian rg,
            ImagePlane<float> inPlane,
            ImagePlane<float> tempPlane,
            ImagePlane<float> outPlane,
            ThreadPool? pool = null)
        {
            GaussianRowAccessors rows = new GaussianRowAccessors(
                y => inPlane.ConstRow(y),
                y => tempPlane.Row(y),
                y => outPlane.Row(y));

            return FastGaussian(memoryManager, rg, inPlane.XSize, inPlane.YSize, rows, pool);
        }

        private static void PopulateRecursiveConstants(
            RecursiveGaussian rg,
            double radius,
            double[] omega,
            in Vector3d beta)
        {
            for (int i = 0; i < 3; ++i)
            {
                double n2Val = -beta[i] * Math.Cos(omega[i] * (radius + 1.0));
                double d1Val = -2.0 * Math.Cos(omega[i]);
                double d2    = d1Val * d1Val;
                int baseIdx  = 4 * i;

                for (int lane = 0; lane < 4; ++lane)
                {
                    rg.N2[baseIdx + lane] = (float)n2Val;
                    rg.D1[baseIdx + lane] = (float)d1Val;
                }

                rg.MulPrev[baseIdx + 0]  = (float)(-d1Val);
                rg.MulPrev[baseIdx + 1]  = (float)(d2 - 1.0);
                rg.MulPrev[baseIdx + 2]  = (float)(-d2 * d1Val + 2.0 * d1Val);
                rg.MulPrev[baseIdx + 3]  = (float)(d2 * d2 - 3.0 * d2 + 1.0);
                rg.MulPrev2[baseIdx + 0] = -1.0f;
                rg.MulPrev2[baseIdx + 1] = (float)d1Val;
                rg.MulPrev2[baseIdx + 2] = (float)(-d2 + 1.0);
                rg.MulPrev2[baseIdx + 3] = (float)(d2 * d1Val - 2.0 * d1Val);
                rg.MulIn[baseIdx + 0]    = (float)n2Val;
                rg.MulIn[baseIdx + 1]    = (float)(-d1Val * n2Val);
                rg.MulIn[baseIdx + 2]    = (float)(d2 * n2Val - n2Val);
                rg.MulIn[baseIdx + 3]    = (float)(-d2 * d1Val * n2Val + 2.0 * d1Val * n2Val);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StepHorizontalScalar(
            in HorizontalState s,
            ReadOnlySpan<float> inRow,
            Span<float> outRow,
            nint xsize,
            nint radius,
            nint n)
        {
            nint left      = n - radius - 1;
            nint right     = n + radius - 1;
            float leftVal  = left >= 0 ? inRow[(int)left] : 0.0f;
            float rightVal = right < xsize ? inRow[(int)right] : 0.0f;
            Vec4 sum       = Vec4.Set(leftVal + rightVal);
            Vec4 out1      = Vec4.Mul(sum, s.MulIn1);
            Vec4 out3      = Vec4.Mul(sum, s.MulIn3);
            Vec4 out5      = Vec4.Mul(sum, s.MulIn5);

            out1    = Vec4.MulAdd(s.MulPrev21, s.Prev2[0], out1);
            out3    = Vec4.MulAdd(s.MulPrev23, s.Prev2[1], out3);
            out5    = Vec4.MulAdd(s.MulPrev25, s.Prev2[2], out5);
            Vec4 p1 = s.Prev[0];
            Vec4 p3 = s.Prev[1];
            Vec4 p5 = s.Prev[2];
            out1    = Vec4.MulAdd(s.MulPrev1, p1, out1);
            out3    = Vec4.MulAdd(s.MulPrev3, p3, out3);
            out5    = Vec4.MulAdd(s.MulPrev5, p5, out5);

            s.Prev2[0] = p1;
            s.Prev2[1] = p3;
            s.Prev2[2] = p5;
            s.Prev[0]  = out1;
            s.Prev[1]  = out3;
            s.Prev[2]  = out5;

            if (n >= 0)
            {
                outRow[(int)n] = out1.X + (out3.X + out5.X);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StepHorizontalVector4(
            in HorizontalState s,
            ReadOnlySpan<float> inRow,
            Span<float> outRow,
            nint radius,
            nint n)
        {
            Vec4 sum  = Vec4.Add(
                Vec4.Load(inRow.Slice((int)(n - radius - 1))),
                Vec4.Load(inRow.Slice((int)(n + radius - 1))));
            Vec4 in0  = sum.Broadcast0();
            Vec4 in1  = sum.Broadcast1();
            Vec4 in2  = sum.Broadcast2();
            Vec4 in3  = sum.Broadcast3();
            Vec4 out1 = Vec4.Mul(in0, s.MulIn1);
            Vec4 out3 = Vec4.Mul(in0, s.MulIn3);
            Vec4 out5 = Vec4.Mul(in0, s.MulIn5);

            out1 = Vec4.MulAdd(s.MulIn1.ShiftLeft1(), in1, out1);
            out3 = Vec4.MulAdd(s.MulIn3.ShiftLeft1(), in1, out3);
            out5 = Vec4.MulAdd(s.MulIn5.ShiftLeft1(), in1, out5);
            out1 = Vec4.MulAdd(s.MulIn1.ShiftLeft2(), in2, out1);
            out3 = Vec4.MulAdd(s.MulIn3.ShiftLeft2(), in2, out3);
            out5 = Vec4.MulAdd(s.MulIn5.ShiftLeft2(), in2, out5);
            out1 = Vec4.MulAdd(s.MulIn1.ShiftLeft3(), in3, out1);
            out3 = Vec4.MulAdd(s.MulIn3.ShiftLeft3(), in3, out3);
            out5 = Vec4.MulAdd(s.MulIn5.ShiftLeft3(), in3, out5);

            out1 = Vec4.MulAdd(s.MulPrev21, s.Prev2[0], out1);
            out3 = Vec4.MulAdd(s.MulPrev23, s.Prev2[1], out3);
            out5 = Vec4.MulAdd(s.MulPrev25, s.Prev2[2], out5);
            out1 = Vec4.MulAdd(s.MulPrev1, s.Prev[0], out1);
            out3 = Vec4.MulAdd(s.MulPrev3, s.Prev[1], out3);
            out5 = Vec4.MulAdd(s.MulPrev5, s.Prev[2], out5);

            s.Prev2[0] = out1.Broadcast2();
            s.Prev2[1] = out3.Broadcast2();
            s.Prev2[2] = out5.Broadcast2();
            s.Prev[0]  = out1.Broadcast3();
            s.Prev[1]  = out3.Broadcast3();
            s.Prev[2]  = out5.Broadcast3();

            Vec4 total = Vec4.Add(out1, Vec4.Add(out3, out5));

            total.Store(outRow.Slice((int)n));
        }

        private static Status FastGaussianHorizontal(
            RecursiveGaussian rg,
            nuint xsize,
            nuint ysize,
            GetConstRow inRow,
            GetRow outRow,
            ThreadPool? pool)
        {
            Func<uint, nuint, Status> processLine = (task, _) =>
            {
                nuint y = task;

                FastGaussian1D(rg, xsize, inRow(y), outRow(y));

                return true;
            };

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                ThreadPool.NoInit,
                processLine,
                "FastGaussianHorizontal");
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status FastGaussianVertical(
            JxlMemoryManager? memoryManager,
            RecursiveGaussian rg,
            nuint xsize,
            nuint ysize,
            GetConstRow inRow,
            GetRow outRow)
        {
            nuint scratchBytes = FastPaceLanes * sizeof(float) * (1 + 3 * RingBufferLen);
            var memResult      = AlignedMemory.Create(memoryManager, scratchBytes);

            if (!memResult.IsOk)
            {
                return memResult.Status;
            }

            using AlignedMemory mem = memResult.Value!;
            Span<float> scratch     = mem.GetSpan<float>();
            Span<float> zero        = scratch.Slice(0, (int)FastPaceLanes);
            Span<float> ringBuffer  = scratch.Slice((int)FastPaceLanes);

            zero.Clear();

            GaussianRowAccessors rows = new GaussianRowAccessors(inRow, outRow, outRow);
            nuint x                   = 0;

            for (; x + FastPaceLanes <= xsize; x += FastPaceLanes)
            {
                VerticalStrip(rg, x, FastPaceLanes, ysize, ringBuffer, zero, rows);
            }

            if (x < xsize)
            {
                VerticalStrip(rg, x, xsize - x, ysize, ringBuffer, zero, rows);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void VerticalStrip(
            RecursiveGaussian rg,
            nuint x,
            nuint width,
            nuint ysize,
            Span<float> ringBuffer,
            ReadOnlySpan<float> zero,
            GaussianRowAccessors rows)
        {
            int ringFloats = (int)(3 * width * RingBufferLen);

            ringBuffer.Slice(0, ringFloats).Clear();

            nint radius       = (nint)rg.Radius;
            nint height       = (nint)ysize;
            nint n            = -radius + 1;
            nint topBorderEnd = Math.Min(radius + 1, height);

            for (; n < 0; ++n)
            {
                nuint bottom              = (nuint)(n + radius - 1);
                ReadOnlySpan<float> inPos = bottom < ysize ? rows.In(bottom).Slice((int)x) : zero;

                VerticalStepSingle(rg, width, n, ringBuffer, inPos, Span<float>.Empty);
            }

            for (n = 0; n < topBorderEnd; ++n)
            {
                nuint bottom              = (nuint)(n + radius - 1);
                ReadOnlySpan<float> inPos = bottom < ysize ? rows.In(bottom).Slice((int)x) : zero;
                Span<float> outPos        = rows.Out((nuint)n).Slice((int)x);

                VerticalStepSingle(rg, width, n, ringBuffer, inPos, outPos);
            }

            for (; n < height; ++n)
            {
                nuint top                = (nuint)(n - radius - 1);
                nuint bottom             = (nuint)(n + radius - 1);
                ReadOnlySpan<float> pos1 = rows.In(top).Slice((int)x);
                ReadOnlySpan<float> pos2 = bottom < ysize ? rows.In(bottom).Slice((int)x) : zero;
                Span<float> outPos       = rows.Out((nuint)n).Slice((int)x);

                VerticalStepTwo(rg, width, n, ringBuffer, pos1, pos2, outPos);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VerticalStepSingle(
            RecursiveGaussian rg,
            nuint width,
            nint n,
            Span<float> ringBuffer,
            ReadOnlySpan<float> inPos,
            Span<float> outPos)
        {
            float d11      = rg.D1[0];
            float d13      = rg.D1[4];
            float d15      = rg.D1[8];
            float n21      = rg.N2[0];
            float n23      = rg.N2[4];
            float n25      = rg.N2[8];
            int blockLen   = (int)(width * RingBufferLen);
            Span<float> y1 = ringBuffer.Slice(0, blockLen);
            Span<float> y3 = ringBuffer.Slice(blockLen, blockLen);
            Span<float> y5 = ringBuffer.Slice(2 * blockLen, blockLen);
            nuint n0       = (nuint)n & RingBufferMask;
            nuint n1       = (nuint)(n - 1) & RingBufferMask;
            nuint n2       = (nuint)(n - 2) & RingBufferMask;
            bool hasOut    = !outPos.IsEmpty;

            nuint col = 0;
            if (Avx2.IsSupported)
            {
                for (; col + 8 <= width; col += 8)
                {
                    int cIdx   = (int)col;
                    int idx0   = (int)(width * n0 + col);
                    int idx1   = (int)(width * n1 + col);
                    int idx2   = (int)(width * n2 + col);
                    Vector256<float> sum  = Vector256.Create(inPos.Slice(cIdx, 8));
                    Vector256<float> val1 = Vector256.Create(n21) * sum - Vector256.Create(d11) * Vector256.Create(y1.Slice(idx1, 8)) - Vector256.Create(y1.Slice(idx2, 8));
                    Vector256<float> val3 = Vector256.Create(n23) * sum - Vector256.Create(d13) * Vector256.Create(y3.Slice(idx1, 8)) - Vector256.Create(y3.Slice(idx2, 8));
                    Vector256<float> val5 = Vector256.Create(n25) * sum - Vector256.Create(d15) * Vector256.Create(y5.Slice(idx1, 8)) - Vector256.Create(y5.Slice(idx2, 8));

                    val1.CopyTo(y1.Slice(idx0, 8));
                    val3.CopyTo(y3.Slice(idx0, 8));
                    val5.CopyTo(y5.Slice(idx0, 8));

                    if (hasOut)
                    {
                        (val1 + (val3 + val5)).CopyTo(outPos.Slice(cIdx, 8));
                    }

                }
            }

            for (; col < width; ++col)
            {
                int cIdx   = (int)col;
                int idx0   = (int)(width * n0 + col);
                int idx1   = (int)(width * n1 + col);
                int idx2   = (int)(width * n2 + col);
                float sum  = inPos[cIdx];
                float val1 = n21 * sum - d11 * y1[idx1] - y1[idx2];
                float val3 = n23 * sum - d13 * y3[idx1] - y3[idx2];
                float val5 = n25 * sum - d15 * y5[idx1] - y5[idx2];

                y1[idx0] = val1;
                y3[idx0] = val3;
                y5[idx0] = val5;

                if (hasOut)
                {
                    outPos[cIdx] = val1 + (val3 + val5);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VerticalStepTwo(
            RecursiveGaussian rg,
            nuint width,
            nint n,
            Span<float> ringBuffer,
            ReadOnlySpan<float> pos1,
            ReadOnlySpan<float> pos2,
            Span<float> outPos)
        {
            float d11      = rg.D1[0];
            float d13      = rg.D1[4];
            float d15      = rg.D1[8];
            float n21      = rg.N2[0];
            float n23      = rg.N2[4];
            float n25      = rg.N2[8];
            int blockLen   = (int)(width * RingBufferLen);
            Span<float> y1 = ringBuffer.Slice(0, blockLen);
            Span<float> y3 = ringBuffer.Slice(blockLen, blockLen);
            Span<float> y5 = ringBuffer.Slice(2 * blockLen, blockLen);
            nuint n0       = (nuint)n & RingBufferMask;
            nuint n1       = (nuint)(n - 1) & RingBufferMask;
            nuint n2       = (nuint)(n - 2) & RingBufferMask;

            nuint col = 0;
            if (Avx2.IsSupported)
            {
                for (; col + 8 <= width; col += 8)
                {
                    int cIdx   = (int)col;
                    int idx0   = (int)(width * n0 + col);
                    int idx1   = (int)(width * n1 + col);
                    int idx2   = (int)(width * n2 + col);
                    Vector256<float> sum  = Vector256.Create(pos1.Slice(cIdx, 8)) + Vector256.Create(pos2.Slice(cIdx, 8));
                    Vector256<float> val1 = Vector256.Create(n21) * sum - Vector256.Create(d11) * Vector256.Create(y1.Slice(idx1, 8)) - Vector256.Create(y1.Slice(idx2, 8));
                    Vector256<float> val3 = Vector256.Create(n23) * sum - Vector256.Create(d13) * Vector256.Create(y3.Slice(idx1, 8)) - Vector256.Create(y3.Slice(idx2, 8));
                    Vector256<float> val5 = Vector256.Create(n25) * sum - Vector256.Create(d15) * Vector256.Create(y5.Slice(idx1, 8)) - Vector256.Create(y5.Slice(idx2, 8));

                    val1.CopyTo(y1.Slice(idx0, 8));
                    val3.CopyTo(y3.Slice(idx0, 8));
                    val5.CopyTo(y5.Slice(idx0, 8));
                    (val1 + (val3 + val5)).CopyTo(outPos.Slice(cIdx, 8));

                }
            }

            for (; col < width; ++col)
            {
                int cIdx   = (int)col;
                int idx0   = (int)(width * n0 + col);
                int idx1   = (int)(width * n1 + col);
                int idx2   = (int)(width * n2 + col);
                float sum  = pos1[cIdx] + pos2[cIdx];
                float val1 = n21 * sum - d11 * y1[idx1] - y1[idx2];
                float val3 = n23 * sum - d13 * y3[idx1] - y3[idx2];
                float val5 = n25 * sum - d15 * y5[idx1] - y5[idx2];

                y1[idx0]     = val1;
                y3[idx0]     = val3;
                y5[idx0]     = val5;
                outPos[cIdx] = val1 + (val3 + val5);
            }
        }

        private readonly struct HorizontalState
        {
            public readonly Vec4 MulIn1;
            public readonly Vec4 MulIn3;
            public readonly Vec4 MulIn5;
            public readonly Vec4 MulPrev1;
            public readonly Vec4 MulPrev3;
            public readonly Vec4 MulPrev5;
            public readonly Vec4 MulPrev21;
            public readonly Vec4 MulPrev23;
            public readonly Vec4 MulPrev25;
            public readonly Vec4[] Prev;
            public readonly Vec4[] Prev2;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public HorizontalState(RecursiveGaussian rg)
            {
                MulIn1    = Vec4.LoadArray(rg.MulIn, 0);
                MulIn3    = Vec4.LoadArray(rg.MulIn, 4);
                MulIn5    = Vec4.LoadArray(rg.MulIn, 8);
                MulPrev1  = Vec4.LoadArray(rg.MulPrev, 0);
                MulPrev3  = Vec4.LoadArray(rg.MulPrev, 4);
                MulPrev5  = Vec4.LoadArray(rg.MulPrev, 8);
                MulPrev21 = Vec4.LoadArray(rg.MulPrev2, 0);
                MulPrev23 = Vec4.LoadArray(rg.MulPrev2, 4);
                MulPrev25 = Vec4.LoadArray(rg.MulPrev2, 8);
                Prev      = new Vec4[3];
                Prev2     = new Vec4[3];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void BroadcastLane0()
            {
                Prev2[0] = Prev2[0].Broadcast0();
                Prev2[1] = Prev2[1].Broadcast0();
                Prev2[2] = Prev2[2].Broadcast0();
                Prev[0]  = Prev[0].Broadcast0();
                Prev[1]  = Prev[1].Broadcast0();
                Prev[2]  = Prev[2].Broadcast0();
            }
        }

        // Retain the four-step horizontal recurrence and separate multiply/add rounding.
        // Only the representation changes from four scalar fields to a SIMD register.
        private readonly struct Vec4
        {
            private readonly Vector128<float> _value;
            public float X { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _value.GetElement(0); }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4(float x, float y, float z, float w) => _value = Vector128.Create(x, y, z, w);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Vec4(Vector128<float> value) => _value = value;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 Set(float v) => new Vec4(Vector128.Create(v));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 LoadArray(float[] array, int offset) => new Vec4(Vector128.Create(array.AsSpan(offset, 4)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 Load(ReadOnlySpan<float> span) => new Vec4(Vector128.Create(span.Slice(0, 4)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 Add(Vec4 a, Vec4 b) => new Vec4(a._value + b._value);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 Mul(Vec4 a, Vec4 b) => new Vec4(a._value * b._value);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vec4 MulAdd(Vec4 a, Vec4 b, Vec4 c) => new Vec4(a._value * b._value + c._value);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 Broadcast0() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(0)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 Broadcast1() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(1)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 Broadcast2() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(2)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 Broadcast3() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(3)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 ShiftLeft1() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(-1, 0, 1, 2)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 ShiftLeft2() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(-1, -1, 0, 1)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vec4 ShiftLeft3() => new Vec4(Vector128.Shuffle(_value, Vector128.Create(-1, -1, -1, 0)));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Store(Span<float> span) => _value.CopyTo(span);
        }
    }
}
