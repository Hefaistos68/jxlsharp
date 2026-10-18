// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using JxlSharp.Base;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Delegate that returns a read-only pointer to row <paramref name="y"/>.
    /// </summary>
    /// <param name="y">Zero-based row index.</param>
    /// <returns>Pointer to the first element of row <paramref name="y"/>.</returns>
    public unsafe delegate float* GetConstRow(nuint y);

    /// <summary>
    /// Delegate that returns a writable pointer to row <paramref name="y"/>.
    /// </summary>
    /// <param name="y">Zero-based row index.</param>
    /// <returns>Pointer to the first element of row <paramref name="y"/>.</returns>
    public unsafe delegate float* GetRow(nuint y);

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
            const double kPi       = 3.141592653589793238;
            RecursiveGaussian rg   = new RecursiveGaussian();
            double radius          = Math.Round(3.2795 * sigma + 0.2546);
            double piDiv2R         = kPi / (2.0 * radius);
            double[] omega         = new double[3] { piDiv2R, 3.0 * piDiv2R, 5.0 * piDiv2R };
            double p1              = +1.0 / Math.Tan(0.5 * omega[0]);
            double p3              = -1.0 / Math.Tan(0.5 * omega[1]);
            double p5              = +1.0 / Math.Tan(0.5 * omega[2]);
            double r1              = +p1 * p1 / Math.Sin(omega[0]);
            double r3              = -p3 * p3 / Math.Sin(omega[1]);
            double r5              = +p5 * p5 / Math.Sin(omega[2]);
            double negHalfSigma2   = -0.5 * sigma * sigma;
            double recipRadius     = 1.0 / radius;
            double[] rho           = new double[3];

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

            MatrixOps.Mul3x3Vector( in matrix, in gamma, out Vector3d beta);

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
        public static unsafe void FastGaussian1D(
            RecursiveGaussian rg,
            nuint xsize,
            ReadOnlySpan<float> inRow,
            Span<float> outRow)
        {
            fixed (float* inPtr = inRow)
            fixed (float* outPtr = outRow)
            {
                FastGaussian1D(rg, xsize, inPtr, outPtr);
            }
        }

        /// <summary>
        /// Applies a 1D Gaussian filter with zero-pad boundary handling and runtime independent of sigma.
        /// </summary>
        /// <param name="rg">Precomputed recursive Gaussian parameters.</param>
        /// <param name="xsize">Number of elements in the row.</param>
        /// <param name="inRow">Pointer to the input row.</param>
        /// <param name="outRow">Pointer to the output row.</param>
        public static unsafe void FastGaussian1D(
            RecursiveGaussian rg,
            nuint xsize,
            float* inRow,
            float* outRow)
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
        public static unsafe Status FastGaussian(
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
        public static unsafe Status FastGaussian(
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

        private static unsafe void StepHorizontalScalar(
            in HorizontalState s,
            float* inRow,
            float* outRow,
            nint xsize,
            nint radius,
            nint n)
        {
            nint left      = n - radius - 1;
            nint right     = n + radius - 1;
            float leftVal  = left >= 0 ? inRow[left] : 0.0f;
            float rightVal = right < xsize ? inRow[right] : 0.0f;
            Vec4 sum       = Vec4.Set(leftVal + rightVal);
            Vec4 out1      = Vec4.Mul(sum, s.MulIn1);
            Vec4 out3      = Vec4.Mul(sum, s.MulIn3);
            Vec4 out5      = Vec4.Mul(sum, s.MulIn5);

            out1     = Vec4.MulAdd(s.MulPrev21, s.Prev2[0], out1);
            out3     = Vec4.MulAdd(s.MulPrev23, s.Prev2[1], out3);
            out5     = Vec4.MulAdd(s.MulPrev25, s.Prev2[2], out5);
            Vec4 p1  = s.Prev[0];
            Vec4 p3  = s.Prev[1];
            Vec4 p5  = s.Prev[2];
            out1     = Vec4.MulAdd(s.MulPrev1, p1, out1);
            out3     = Vec4.MulAdd(s.MulPrev3, p3, out3);
            out5     = Vec4.MulAdd(s.MulPrev5, p5, out5);

            s.Prev2[0] = p1;
            s.Prev2[1] = p3;
            s.Prev2[2] = p5;
            s.Prev[0]  = out1;
            s.Prev[1]  = out3;
            s.Prev[2]  = out5;

            if (n >= 0)
            {
                outRow[n] = out1.X + (out3.X + out5.X);
            }
        }

        private static unsafe void StepHorizontalVector4(
            in HorizontalState s,
            float* inRow,
            float* outRow,
            nint radius,
            nint n)
        {
            Vec4 sum  = Vec4.Add(Vec4.Load(inRow + n - radius - 1), Vec4.Load(inRow + n + radius - 1));
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

            total.Store(outRow + n);
        }

        private static unsafe Status FastGaussianHorizontal(
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

        private static unsafe Status FastGaussianVertical(
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
            float* zero             = mem.GetPointer<float>();
            float* ringBuffer       = zero + FastPaceLanes;

            new Span<float>(zero, (int)FastPaceLanes).Clear();

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

        private static unsafe void VerticalStrip(
            RecursiveGaussian rg,
            nuint x,
            nuint width,
            nuint ysize,
            float* ringBuffer,
            float* zero,
            GaussianRowAccessors rows)
        {
            int ringFloats = (int)(3 * width * RingBufferLen);

            new Span<float>(ringBuffer, ringFloats).Clear();

            nint radius       = (nint)rg.Radius;
            nint height       = (nint)ysize;
            nint n            = -radius + 1;
            nint topBorderEnd = Math.Min(radius + 1, height);

            for (; n < 0; ++n)
            {
                nuint bottom  = (nuint)(n + radius - 1);
                float* inPos  = bottom < ysize ? rows.In(bottom) + x : zero;

                VerticalStepSingle(rg, width, n, ringBuffer, inPos, null);
            }

            for (n = 0; n < topBorderEnd; ++n)
            {
                nuint bottom  = (nuint)(n + radius - 1);
                float* inPos  = bottom < ysize ? rows.In(bottom) + x : zero;
                float* outPos = rows.Out((nuint)n) + x;

                VerticalStepSingle(rg, width, n, ringBuffer, inPos, outPos);
            }

            for (; n < height; ++n)
            {
                nuint top     = (nuint)(n - radius - 1);
                nuint bottom  = (nuint)(n + radius - 1);
                float* pos1   = rows.In(top) + x;
                float* pos2   = bottom < ysize ? rows.In(bottom) + x : zero;
                float* outPos = rows.Out((nuint)n) + x;

                VerticalStepTwo(rg, width, n, ringBuffer, pos1, pos2, outPos);
            }
        }

        private static unsafe void VerticalStepSingle(
            RecursiveGaussian rg,
            nuint width,
            nint n,
            float* ringBuffer,
            float* inPos,
            float* outPos)
        {
            float d11 = rg.D1[0];
            float d13 = rg.D1[4];
            float d15 = rg.D1[8];
            float n21 = rg.N2[0];
            float n23 = rg.N2[4];
            float n25 = rg.N2[8];
            float* y1 = ringBuffer + 0 * width * RingBufferLen;
            float* y3 = ringBuffer + 1 * width * RingBufferLen;
            float* y5 = ringBuffer + 2 * width * RingBufferLen;
            nuint n0  = (nuint)n & RingBufferMask;
            nuint n1  = (nuint)(n - 1) & RingBufferMask;
            nuint n2  = (nuint)(n - 2) & RingBufferMask;

            for (nuint col = 0; col < width; ++col)
            {
                float sum  = inPos[col];
                float val1 = n21 * sum - d11 * y1[width * n1 + col] - y1[width * n2 + col];
                float val3 = n23 * sum - d13 * y3[width * n1 + col] - y3[width * n2 + col];
                float val5 = n25 * sum - d15 * y5[width * n1 + col] - y5[width * n2 + col];

                y1[width * n0 + col] = val1;
                y3[width * n0 + col] = val3;
                y5[width * n0 + col] = val5;

                if (outPos != null)
                {
                    outPos[col] = val1 + (val3 + val5);
                }
            }
        }

        private static unsafe void VerticalStepTwo(
            RecursiveGaussian rg,
            nuint width,
            nint n,
            float* ringBuffer,
            float* pos1,
            float* pos2,
            float* outPos)
        {
            float d11 = rg.D1[0];
            float d13 = rg.D1[4];
            float d15 = rg.D1[8];
            float n21 = rg.N2[0];
            float n23 = rg.N2[4];
            float n25 = rg.N2[8];
            float* y1 = ringBuffer + 0 * width * RingBufferLen;
            float* y3 = ringBuffer + 1 * width * RingBufferLen;
            float* y5 = ringBuffer + 2 * width * RingBufferLen;
            nuint n0  = (nuint)n & RingBufferMask;
            nuint n1  = (nuint)(n - 1) & RingBufferMask;
            nuint n2  = (nuint)(n - 2) & RingBufferMask;

            for (nuint col = 0; col < width; ++col)
            {
                float sum  = pos1[col] + pos2[col];
                float val1 = n21 * sum - d11 * y1[width * n1 + col] - y1[width * n2 + col];
                float val3 = n23 * sum - d13 * y3[width * n1 + col] - y3[width * n2 + col];
                float val5 = n25 * sum - d15 * y5[width * n1 + col] - y5[width * n2 + col];

                y1[width * n0 + col] = val1;
                y3[width * n0 + col] = val3;
                y5[width * n0 + col] = val5;
                outPos[col]          = val1 + (val3 + val5);
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

        private readonly struct Vec4
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;
            public readonly float W;

            public Vec4(float x, float y, float z, float w)
            {
                X = x;
                Y = y;
                Z = z;
                W = w;
            }

            public static Vec4 Set(float v)
            {
                return new Vec4(v, v, v, v);
            }

            public static Vec4 LoadArray(float[] arr, int offset)
            {
                return new Vec4(arr[offset + 0], arr[offset + 1], arr[offset + 2], arr[offset + 3]);
            }

            public static unsafe Vec4 Load(float* ptr)
            {
                return new Vec4(ptr[0], ptr[1], ptr[2], ptr[3]);
            }

            public static Vec4 Add(Vec4 a, Vec4 b)
            {
                return new Vec4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
            }

            public static Vec4 Mul(Vec4 a, Vec4 b)
            {
                return new Vec4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
            }

            public static Vec4 MulAdd(Vec4 a, Vec4 b, Vec4 c)
            {
                return new Vec4(
                    a.X * b.X + c.X,
                    a.Y * b.Y + c.Y,
                    a.Z * b.Z + c.Z,
                    a.W * b.W + c.W);
            }

            public Vec4 Broadcast0()
            {
                return new Vec4(X, X, X, X);
            }

            public Vec4 Broadcast1()
            {
                return new Vec4(Y, Y, Y, Y);
            }

            public Vec4 Broadcast2()
            {
                return new Vec4(Z, Z, Z, Z);
            }

            public Vec4 Broadcast3()
            {
                return new Vec4(W, W, W, W);
            }

            public Vec4 ShiftLeft1()
            {
                return new Vec4(0.0f, X, Y, Z);
            }

            public Vec4 ShiftLeft2()
            {
                return new Vec4(0.0f, 0.0f, X, Y);
            }

            public Vec4 ShiftLeft3()
            {
                return new Vec4(0.0f, 0.0f, 0.0f, X);
            }

            public unsafe void Store(float* ptr)
            {
                ptr[0] = X;
                ptr[1] = Y;
                ptr[2] = Z;
                ptr[3] = W;
            }
        }
    }
}
