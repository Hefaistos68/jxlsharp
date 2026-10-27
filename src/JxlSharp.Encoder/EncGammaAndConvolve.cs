// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Direct sRGB transfer function helpers.
    /// Ported from <c>lib/jxl/enc_gamma_correct.h</c>.
    /// </summary>
    public static class EncGammaCorrect
    {
        /// <summary>
        /// Converts an sRGB value in <c>[0, 1]</c> to linear light in <c>[0, 1]</c>.
        /// Ported from <c>jxl::Srgb8ToLinearDirect</c> in <c>lib/jxl/enc_gamma_correct.h</c>.
        /// </summary>
        /// <param name="srgb">sRGB input value in <c>[0, 1]</c>.</param>
        /// <returns>Linear light value in <c>[0, 1]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Srgb8ToLinearDirect(double srgb)
        {
            if (srgb <= 0.0)
            {
                return 0.0;
            }

            if (srgb <= 0.04045)
            {
                return srgb / 12.92;
            }

            if (srgb >= 1.0)
            {
                return 1.0;
            }

            return Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Converts a linear light value in <c>[0, 1]</c> to sRGB in <c>[0, 1]</c>.
        /// Ported from <c>jxl::LinearToSrgb8Direct</c> in <c>lib/jxl/enc_gamma_correct.h</c>.
        /// </summary>
        /// <param name="linear">Linear light input value in <c>[0, 1]</c>.</param>
        /// <returns>sRGB value in <c>[0, 1]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LinearToSrgb8Direct(double linear)
        {
            if (linear <= 0.0)
            {
                return 0.0;
            }

            if (linear >= 1.0)
            {
                return 1.0;
            }

            if (linear <= 0.0031308)
            {
                return linear * 12.92;
            }

            return (Math.Pow(linear, 1.0 / 2.4) * 1.055) - 0.055;
        }
    }

    /// <summary>
    /// Single-pass 5x5 separable convolution with mirrored boundaries.
    /// Ported from <c>lib/jxl/enc_convolve_separable5.cc</c>.
    /// </summary>
    public static class EncConvolve
    {
        private const int KRadius = 2;

        private static readonly nuint[] KBorderLut =
        {
            0, 0, 0, 0, 0, 0xBAD, 0xBAD, 0xBAD, // 1 row
            1, 0, 0, 1, 1, 0,     0xBAD, 0xBAD, // 2 rows
            1, 0, 0, 1, 2, 2,     1,     0xBAD, // 3 rows
            1, 0, 0, 1, 2, 3,     3,     2,     // 4 rows
        };

        /// <summary>
        /// Convolves <paramref name="inPlane"/> within <paramref name="rect"/> using 5x5 separable <paramref name="weights"/>
        /// and stores the result in <paramref name="outPlane"/>.
        /// Ported from <c>jxl::Separable5</c> in <c>lib/jxl/enc_convolve_separable5.cc</c>.
        /// </summary>
        /// <param name="inPlane">Input float plane.</param>
        /// <param name="rect">Region in <paramref name="inPlane"/> to convolve.</param>
        /// <param name="weights">Normalized 5x5 separable weights.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outPlane">Destination float plane of size <paramref name="rect"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Separable5(
            ImagePlane<float> inPlane,
            Rect rect,
            WeightsSeparable5 weights,
            ThreadPool? pool,
            ImagePlane<float> outPlane)
        {
            nuint xsize = (nuint)rect.XSize;
            nuint ysize = (nuint)rect.YSize;

            if (xsize < 2 * KRadius)
            {
                return Convolve.SlowSeparable5(
                    inPlane,
                    rect,
                    weights,
                    pool,
                    outPlane,
                    new Rect(outPlane));
            }

            if (xsize != outPlane.XSize || ysize != outPlane.YSize)
            {
                return Status.Failure("Separable5 rect and outPlane size mismatch");
            }

            nuint ybegin = (nuint)rect.Y0;
            nuint yend   = (nuint)rect.Y1;

            while (ybegin < yend && ybegin < KRadius)
            {
                ybegin++;
            }

            while (ybegin < yend && yend + KRadius > (nuint)inPlane.ysize)
            {
                yend--;
            }

            nuint y0 = (nuint)rect.Y0;

            if (ybegin > y0)
            {
                ConvolveRowsRange(inPlane, rect, weights, outPlane, 0, ybegin - y0, isBorder: true);
            }

            if (yend > ybegin)
            {
                Status interiorStatus = DataParallel.RunOnPool(
                    pool,
                    (uint)(ybegin - y0),
                    (uint)(yend - y0),
                    ThreadPool.NoInit,
                    (y, _) =>
                    {
                        ConvolveRow(inPlane, rect, weights, outPlane, y, isBorder: false);

                        return true;
                    },
                    "Convolve");

                if (!interiorStatus)
                {
                    return interiorStatus;
                }
            }

            if (yend < (nuint)rect.Y1)
            {
                ConvolveRowsRange(inPlane, rect, weights, outPlane, yend - y0, ysize, isBorder: true);
            }

            return true;
        }

        private static void ConvolveRowsRange(
            ImagePlane<float> inPlane,
            Rect rect,
            WeightsSeparable5 weights,
            ImagePlane<float> outPlane,
            nuint yStart,
            nuint yEnd,
            bool isBorder)
        {
            for (nuint y = yStart; y < yEnd; y++)
            {
                ConvolveRow(inPlane, rect, weights, outPlane, y, isBorder);
            }
        }

        private static void ConvolveRow(
            ImagePlane<float> inPlane,
            Rect rect,
            WeightsSeparable5 weights,
            ImagePlane<float> outPlane,
            nuint y,
            bool isBorder)
        {
            long xsize                                   = rect.XSize;
            int rectX                                    = (int)rect.X0;
            nuint imgY                                   = (nuint)rect.Y0 + y;
            (nuint yT2, nuint yT1, nuint yB1, nuint yB2) = isBorder
                ? ResolveBorderRowIndices(imgY, (nuint)inPlane.ysize)
                : (imgY - 2, imgY - 1, imgY + 1, imgY + 2);

            ReadOnlySpan<float> rowM  = rect.ConstRow(inPlane, y);
            ReadOnlySpan<float> rowT2 = inPlane.ConstRow(yT2).Slice(rectX);
            ReadOnlySpan<float> rowT1 = inPlane.ConstRow(yT1).Slice(rectX);
            ReadOnlySpan<float> rowB1 = inPlane.ConstRow(yB1).Slice(rectX);
            ReadOnlySpan<float> rowB2 = inPlane.ConstRow(yB2).Slice(rectX);
            Span<float> rowOut        = outPlane.Row(y);

            (float Wh0, float Wh1, float Wh2) wh = (weights.Horz[0], weights.Horz[4], weights.Horz[8]);
            float wv0                            = weights.Vert[0];
            float wv1                            = weights.Vert[4];
            float wv2                            = weights.Vert[8];

            for (long x = 0; x < xsize; x++)
            {
                int xm2 = (int)(x >= 2 ? x - 2 : ImageOps.Mirror(x - 2, xsize));
                int xm1 = (int)(x >= 1 ? x - 1 : ImageOps.Mirror(x - 1, xsize));
                int ix  = (int)x;
                int xp1 = (int)(x + 1 < xsize ? x + 1 : ImageOps.Mirror(x + 1, xsize));
                int xp2 = (int)(x + 2 < xsize ? x + 2 : ImageOps.Mirror(x + 2, xsize));

                float c0   = HorzSample(rowM, xm2, xm1, ix, xp1, xp2, wh) * wv0;
                float c1t  = HorzSample(rowT1, xm2, xm1, ix, xp1, xp2, wh);
                float c1b  = HorzSample(rowB1, xm2, xm1, ix, xp1, xp2, wh);
                float c1   = ((c1t + c1b) * wv1) + c0;
                float c2t  = HorzSample(rowT2, xm2, xm1, ix, xp1, xp2, wh);
                float c2b  = HorzSample(rowB2, xm2, xm1, ix, xp1, xp2, wh);
                rowOut[ix] = ((c2t + c2b) * wv2) + c1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorzSample(
            ReadOnlySpan<float> row,
            int xm2,
            int xm1,
            int x,
            int xp1,
            int xp2,
            (float Wh0, float Wh1, float Wh2) w)
        {
            float mul0 = row[x] * w.Wh0;
            float mul1 = ((row[xm1] + row[xp1]) * w.Wh1) + mul0;

            return ((row[xm2] + row[xp2]) * w.Wh2) + mul1;
        }

        private static (nuint YT2, nuint YT1, nuint YB1, nuint YB2) ResolveBorderRowIndices(nuint imgY, nuint inY)
        {
            if (inY <= 2 * KRadius)
            {
                Debug.Assert(inY <= 4);
                nuint o = (inY * 8) - 6 + imgY;

                return (KBorderLut[o - 2], KBorderLut[o - 1], KBorderLut[o + 1], KBorderLut[o + 2]);
            }

            if (imgY < KRadius)
            {
                return imgY == 0
                    ? (1u, 0u, 1u, 2u)
                    : (0u, 0u, 2u, 3u);
            }

            return imgY + 1 == inY
                ? (imgY - 2, imgY - 1, imgY, imgY - 1)
                : (imgY - 2, imgY - 1, imgY + 1, imgY + 1);
        }
    }
}
