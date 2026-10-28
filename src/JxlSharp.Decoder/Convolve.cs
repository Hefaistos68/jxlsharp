// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// 2D convolution.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    // Weights must already be normalized.

    /// <summary>
    /// Normalized weights for a 3x3 symmetric 2D convolution kernel.
    /// Ported from <c>jxl::WeightsSymmetric3</c> in <c>lib/jxl/convolve.h</c>.
    /// </summary>
    public sealed class WeightsSymmetric3
    {
        // d r d (each replicated 4x)
        // r c r
        // d r d

        /// <summary>Center weight replicated 4x.</summary>
        public readonly float[] C = new float[4];

        /// <summary>Immediate horizontal/vertical neighbor weight replicated 4x.</summary>
        public readonly float[] R = new float[4];

        /// <summary>Diagonal corner neighbor weight replicated 4x.</summary>
        public readonly float[] D = new float[4];

        /// <summary>
        /// Initializes a new empty instance of the <see cref="WeightsSymmetric3"/> class.
        /// </summary>
        public WeightsSymmetric3()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WeightsSymmetric3"/> class from scalar weights.
        /// </summary>
        /// <param name="c">Center weight.</param>
        /// <param name="r">Immediate orthogonal neighbor weight.</param>
        /// <param name="d">Diagonal corner weight.</param>
        public WeightsSymmetric3(float c, float r, float d)
        {
            Array.Fill(C, c);
            Array.Fill(R, r);
            Array.Fill(D, d);
        }
    }

    /// <summary>
    /// Normalized weights for a 5x5 symmetric 2D convolution kernel.
    /// Ported from <c>jxl::WeightsSymmetric5</c> in <c>lib/jxl/convolve.h</c>.
    /// </summary>
    public sealed class WeightsSymmetric5
    {
        // The lower-right quadrant is: c r R  (each replicated 4x)
        //                              r d L
        //                              R L D

        /// <summary>Center weight (0, 0) replicated 4x.</summary>
        public readonly float[] C = new float[4];

        /// <summary>Distance (1, 0) / (0, 1) weight replicated 4x.</summary>
        public readonly float[] r = new float[4];

        /// <summary>Distance (2, 0) / (0, 2) weight replicated 4x.</summary>
        public readonly float[] R = new float[4];

        /// <summary>Distance (1, 1) weight replicated 4x.</summary>
        public readonly float[] d = new float[4];

        /// <summary>Distance (2, 2) weight replicated 4x.</summary>
        public readonly float[] D = new float[4];

        /// <summary>Distance (2, 1) / (1, 2) knight's-move weight replicated 4x.</summary>
        public readonly float[] L = new float[4];

        /// <summary>
        /// Initializes a new empty instance of the <see cref="WeightsSymmetric5"/> class.
        /// </summary>
        public WeightsSymmetric5()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WeightsSymmetric5"/> class from scalar weights.
        /// </summary>
        /// <param name="c">Weight at (0, 0).</param>
        /// <param name="r1">Weight at (1, 0).</param>
        /// <param name="r2">Weight at (2, 0).</param>
        /// <param name="d1">Weight at (1, 1).</param>
        /// <param name="d2">Weight at (2, 2).</param>
        /// <param name="l">Weight at (2, 1).</param>
        public WeightsSymmetric5(float c, float r1, float r2, float d1, float d2, float l)
        {
            Array.Fill(C, c);
            Array.Fill(r, r1);
            Array.Fill(R, r2);
            Array.Fill(d, d1);
            Array.Fill(D, d2);
            Array.Fill(L, l);
        }
    }

    // Weights for separable 5x5 filters (typically but not necessarily the same
    // values for horizontal and vertical directions). The kernel must already be
    // normalized, but note that values for negative offsets are omitted, so the
    // given values do not sum to 1.

    /// <summary>
    /// Normalized weights for a separable 5x5 2D filter.
    /// Ported from <c>jxl::WeightsSeparable5</c> in <c>lib/jxl/convolve.h</c>.
    /// </summary>
    public sealed class WeightsSeparable5
    {
        /// <summary>Horizontal 1D weights for distances 0..2 (each replicated 4x, total length 12).</summary>
        public readonly float[] Horz = new float[3 * 4];

        /// <summary>Vertical 1D weights for distances 0..2 (each replicated 4x, total length 12).</summary>
        public readonly float[] Vert = new float[3 * 4];

        /// <summary>
        /// Initializes a new empty instance of the <see cref="WeightsSeparable5"/> class.
        /// </summary>
        public WeightsSeparable5()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WeightsSeparable5"/> class from 1D distance weights.
        /// </summary>
        /// <param name="h0">Horizontal distance-0 weight.</param>
        /// <param name="h1">Horizontal distance-1 weight.</param>
        /// <param name="h2">Horizontal distance-2 weight.</param>
        /// <param name="v0">Vertical distance-0 weight.</param>
        /// <param name="v1">Vertical distance-1 weight.</param>
        /// <param name="v2">Vertical distance-2 weight.</param>
        public WeightsSeparable5(float h0, float h1, float h2, float v0, float v1, float v2)
        {
            Horz.AsSpan(0, 4).Fill(h0);
            Horz.AsSpan(4, 4).Fill(h1);
            Horz.AsSpan(8, 4).Fill(h2);
            Vert.AsSpan(0, 4).Fill(v0);
            Vert.AsSpan(4, 4).Fill(v1);
            Vert.AsSpan(8, 4).Fill(v2);
        }
    }

    /// <summary>
    /// Synthesizes left/right neighbors from a vector of center pixels.
    /// Ported from <c>jxl::Neighbors</c> in <c>lib/jxl/convolve-inl.h</c>.
    /// </summary>
    public static class Neighbors
    {
        /// <summary>
        /// Populates <paramref name="dst"/> such that <c>dst[i] == c[Mirror(i - 1)]</c>.
        /// </summary>
        /// <param name="c">Input center lanes.</param>
        /// <param name="dst">Output shifted/mirrored lanes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FirstL1(ReadOnlySpan<float> c, Span<float> dst)
        {
            int n = Math.Min(c.Length, dst.Length);

            if (n == 0)
            {
                return;
            }

            dst[0] = c[0];

            for (int i = 1; i < n; i++)
            {
                dst[i] = c[i - 1];
            }
        }

        /// <summary>
        /// Populates <paramref name="dst"/> such that <c>dst[i] == c[Mirror(i - 2)]</c>.
        /// </summary>
        /// <param name="c">Input center lanes (length at least 2).</param>
        /// <param name="dst">Output shifted/mirrored lanes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FirstL2(ReadOnlySpan<float> c, Span<float> dst)
        {
            int n = Math.Min(c.Length, dst.Length);

            if (n < 2)
            {
                return;
            }

            dst[0] = c[1];
            dst[1] = c[0];

            for (int i = 2; i < n; i++)
            {
                dst[i] = c[i - 2];
            }
        }
    }

    /// <summary>
    /// 2D symmetric and separable convolution functions.
    /// Ported from <c>lib/jxl/convolve.h</c>, <c>lib/jxl/convolve_slow.cc</c>, and <c>lib/jxl/convolve_symmetric5.cc</c>.
    /// </summary>
    public static class Convolve
    {
        /// <summary>
        /// Applies a 3x3 symmetric convolution to <paramref name="input"/> within <paramref name="rect"/> and writes the result to <paramref name="output"/>.
        /// </summary>
        /// <param name="input">Source float plane.</param>
        /// <param name="rect">Rectangle to convolve.</param>
        /// <param name="weights">Normalized 3x3 symmetric weights.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Destination float plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Status SlowSymmetric3(
            ImagePlane<float> input,
            Rect rect,
            WeightsSymmetric3 weights,
            ThreadPool? pool,
            ImagePlane<float> output)
        {
            long xsize = (long)rect.XSize;
            long ysize = (long)rect.YSize;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    long iy            = task;
                    Span<float> outRow = output.Row((nuint)iy);
                    bool mirrorY       = iy < 1 || iy >= ysize - 1;

                    SlowSymmetric3Row(input, iy, xsize, ysize, mirrorY, weights, outRow);

                    return true;
                },
                "SlowSymmetric3");
        }

        /// <summary>
        /// Applies a 5x5 separable convolution to <paramref name="input"/> within <paramref name="inRect"/> and writes the result to <paramref name="output"/> within <paramref name="outRect"/>.
        /// </summary>
        /// <param name="input">Source float plane.</param>
        /// <param name="inRect">Input region to convolve.</param>
        /// <param name="weights">Normalized 5x5 separable weights.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Destination float plane.</param>
        /// <param name="outRect">Destination region in <paramref name="output"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Status SlowSeparable5(
            ImagePlane<float> input,
            Rect inRect,
            WeightsSeparable5 weights,
            ThreadPool? pool,
            ImagePlane<float> output,
            Rect outRect)
        {
            if (inRect.XSize != outRect.XSize ||
                inRect.YSize != outRect.YSize ||
                !inRect.IsInside(new Rect(input)) ||
                !outRect.IsInside(new Rect(output)))
            {
                return Status.Failure("Invalid rectangles for SlowSeparable5");
            }

            nuint ysize = (nuint)inRect.YSize;
            nuint xsize = (nuint)inRect.XSize;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    long y             = task;
                    Span<float> rowOut = outRect.Row(output, (nuint)y);

                    for (nuint x = 0; x < xsize; ++x)
                    {
                        StatusOr<float> px = SlowSeparablePixel(input, inRect, (long)x, y, 2, weights);

                        if (!px.IsOk)
                        {
                            return px.Status;
                        }

                        rowOut[(int)x] = px.Value;
                    }

                    return true;
                },
                "SlowSeparable");
        }

        /// <summary>
        /// Applies a 5x5 symmetric convolution to <paramref name="input"/> within <paramref name="inRect"/> and writes the result to <paramref name="output"/> within <paramref name="outRect"/>.
        /// </summary>
        /// <param name="input">Source float plane.</param>
        /// <param name="inRect">Input region to convolve.</param>
        /// <param name="weights">Normalized 5x5 symmetric weights.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Destination float plane.</param>
        /// <param name="outRect">Destination region in <paramref name="output"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Status Symmetric5(
            ImagePlane<float> input,
            Rect inRect,
            WeightsSymmetric5 weights,
            ThreadPool? pool,
            ImagePlane<float> output,
            Rect outRect)
        {
            if (inRect.XSize != outRect.XSize || inRect.YSize != outRect.YSize)
            {
                return Status.Failure("Rectangle size mismatch in Symmetric5");
            }

            nuint ysize = (nuint)inRect.YSize;
            long inH    = (long)input.ysize;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    nuint riy          = task;
                    long iy            = (long)inRect.Y0 + (long)riy;
                    Span<float> rowOut = outRect.Row(output, riy);
                    bool mirrorY       = iy < 2 || iy >= inH - 2;

                    Symmetric5Row(input, inRect, iy, mirrorY, weights, rowOut);

                    return true;
                },
                "Symmetric5x5Convolution");
        }

        /// <summary>
        /// Applies a 5x5 separable convolution to <paramref name="input"/> within <paramref name="rect"/> and writes the result to <paramref name="output"/>.
        /// </summary>
        /// <param name="input">Source float plane.</param>
        /// <param name="rect">Rectangle to convolve.</param>
        /// <param name="weights">Normalized 5x5 separable weights.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Destination float plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Separable5(
            ImagePlane<float> input,
            Rect rect,
            WeightsSeparable5 weights,
            ThreadPool? pool,
            ImagePlane<float> output)
        {
            Rect outRect = new Rect(0, 0, rect.XSize, rect.YSize);

            return SlowSeparable5(input, rect, weights, pool, output, outRect);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SlowSymmetric3Row(
            ImagePlane<float> input,
            long iy,
            long xsize,
            long ysize,
            bool mirrorY,
            WeightsSymmetric3 weights,
            Span<float> rowOut)
        {
            rowOut[0] = SlowSymmetric3Pixel(input, 0, iy, (xsize, ysize), (true, mirrorY), weights);

            for (long ix = 1; ix < xsize - 1; ix++)
            {
                rowOut[(int)ix] = SlowSymmetric3Pixel(input, ix, iy, (xsize, ysize), (false, mirrorY), weights);
            }

            if (xsize > 1)
            {
                long lastIx = xsize - 1;

                rowOut[(int)lastIx] = SlowSymmetric3Pixel(input, lastIx, iy, (xsize, ysize), (true, mirrorY), weights);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SlowSymmetric3Pixel(
            ImagePlane<float> input,
            long ix,
            long iy,
            (long XSize, long YSize) dims,
            (bool MirrorX, bool MirrorY) wrap,
            WeightsSymmetric3 weights)
        {
            float sum = 0.0f;
            float wc0 = weights.C[0];
            float wr0 = weights.R[0];
            float wd0 = weights.D[0];
            long xm1  = wrap.MirrorX ? ImageOps.Mirror(ix - 1, dims.XSize) : ix - 1;
            long xp1  = wrap.MirrorX ? ImageOps.Mirror(ix + 1, dims.XSize) : ix + 1;

            for (long ky = -1; ky <= 1; ky++)
            {
                long y                    = wrap.MirrorY ? ImageOps.Mirror(iy + ky, dims.YSize) : iy + ky;
                ReadOnlySpan<float> rowIn = input.ConstRow((nuint)y);
                float wc                  = ky == 0 ? wc0 : wr0;
                float wlr                 = ky == 0 ? wr0 : wd0;

                sum += rowIn[(int)ix] * wc + (rowIn[(int)xm1] + rowIn[(int)xp1]) * wlr;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static StatusOr<float> SlowSeparablePixel(
            ImagePlane<float> input,
            Rect rect,
            long x,
            long y,
            int radius,
            WeightsSeparable5 weights)
        {
            long xsize   = (long)input.xsize;
            long ysize   = (long)input.ysize;
            long baseX   = (long)rect.X0 + x;
            long baseY   = (long)rect.Y0 + y;
            float mul    = 0.0f;
            float[] horz = weights.Horz;
            float[] vert = weights.Vert;

            for (int dy = -radius; dy <= radius; ++dy)
            {
                float wy = vert[Math.Abs(dy) * 4];
                long sy  = ImageOps.Mirror(baseY + dy, ysize);

                if ((ulong)sy >= (ulong)ysize)
                {
                    return Status.Failure("Separable Y coordinate out of bounds");
                }

                ReadOnlySpan<float> row = input.ConstRow((nuint)sy);

                for (int dx = -radius; dx <= radius; ++dx)
                {
                    float wx = horz[Math.Abs(dx) * 4];
                    long sx  = ImageOps.Mirror(baseX + dx, xsize);

                    if ((ulong)sx >= (ulong)xsize)
                    {
                        return Status.Failure("Separable X coordinate out of bounds");
                    }

                    mul += row[(int)sx] * wx * wy;
                }
            }

            return mul;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Symmetric5Row(
            ImagePlane<float> input,
            Rect rect,
            long iy,
            bool mirrorY,
            WeightsSymmetric5 weights,
            Span<float> rowOut)
        {
            nuint xend = (nuint)rect.X1;
            nuint rix  = 0;

            for (nuint ix = (nuint)rect.X0; ix < xend; ++ix, ++rix)
            {
                rowOut[(int)rix] = Symmetric5Border(input, (long)ix, iy, mirrorY, weights);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Symmetric5Border(
            ImagePlane<float> input,
            long ix,
            long iy,
            bool mirrorY,
            WeightsSymmetric5 weights)
        {
            float w0   = weights.C[0];
            float w1   = weights.r[0];
            float w2   = weights.R[0];
            float w4   = weights.d[0];
            float w5   = weights.L[0];
            float w8   = weights.D[0];
            long xsize = (long)input.xsize;
            long ysize = (long)input.ysize;
            float sum0 = WeightedSumBorder(input, (ix, iy), (xsize, ysize), mirrorY, (w0, w1, w2));
            float sum1 = WeightedSumBorder(input, (ix, iy + 2), (xsize, ysize), mirrorY, (w2, w5, w8));

            sum0 += WeightedSumBorder(input, (ix, iy - 2), (xsize, ysize), mirrorY, (w2, w5, w8));
            sum0 += WeightedSumBorder(input, (ix, iy - 1), (xsize, ysize), mirrorY, (w1, w4, w5));
            sum1 += WeightedSumBorder(input, (ix, iy + 1), (xsize, ysize), mirrorY, (w1, w4, w5));

            return sum0 + sum1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float WeightedSumBorder(
            ImagePlane<float> input,
            (long Ix, long Iy) pos,
            (long XSize, long YSize) dims,
            bool mirrorY,
            (float Wx0, float Wx1, float Wx2) w)
        {
            long wy                 = mirrorY ? ImageOps.Mirror(pos.Iy, dims.YSize) : pos.Iy;
            ReadOnlySpan<float> row = input.ConstRow((nuint)wy);
            float inM2              = row[(int)ImageOps.Mirror(pos.Ix - 2, dims.XSize)];
            float inP2              = row[(int)ImageOps.Mirror(pos.Ix + 2, dims.XSize)];
            float inM1              = row[(int)ImageOps.Mirror(pos.Ix - 1, dims.XSize)];
            float inP1              = row[(int)ImageOps.Mirror(pos.Ix + 1, dims.XSize)];
            float in00              = row[(int)pos.Ix];
            float sum2              = w.Wx2 * (inM2 + inP2);
            float sum1              = w.Wx1 * (inM1 + inP1);
            float sum0              = w.Wx0 * in00;

            return sum2 + (sum1 + sum0);
        }
    }
}
