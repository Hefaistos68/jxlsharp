// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Input RGBA row pointers for <see cref="Alpha.PerformAlphaBlending(in AlphaBlendingInputLayer, in AlphaBlendingInputLayer, in AlphaBlendingOutput, nuint, bool, bool)"/>.
    /// Ported from <c>jxl::AlphaBlendingInputLayer</c> in <c>lib/jxl/alpha.h</c>.
    /// </summary>
    public readonly unsafe struct AlphaBlendingInputLayer
    {
        /// <summary>Red channel row pointer.</summary>
        public readonly float* R;

        /// <summary>Green channel row pointer.</summary>
        public readonly float* G;

        /// <summary>Blue channel row pointer.</summary>
        public readonly float* B;

        /// <summary>Alpha channel row pointer.</summary>
        public readonly float* A;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaBlendingInputLayer"/> struct.
        /// </summary>
        /// <param name="r">Red channel row pointer.</param>
        /// <param name="g">Green channel row pointer.</param>
        /// <param name="b">Blue channel row pointer.</param>
        /// <param name="a">Alpha channel row pointer.</param>
        public AlphaBlendingInputLayer(float* r, float* g, float* b, float* a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>
    /// Output RGBA row pointers for <see cref="Alpha.PerformAlphaBlending(in AlphaBlendingInputLayer, in AlphaBlendingInputLayer, in AlphaBlendingOutput, nuint, bool, bool)"/>.
    /// Ported from <c>jxl::AlphaBlendingOutput</c> in <c>lib/jxl/alpha.h</c>.
    /// </summary>
    public readonly unsafe struct AlphaBlendingOutput
    {
        /// <summary>Red channel output row pointer.</summary>
        public readonly float* R;

        /// <summary>Green channel output row pointer.</summary>
        public readonly float* G;

        /// <summary>Blue channel output row pointer.</summary>
        public readonly float* B;

        /// <summary>Alpha channel output row pointer.</summary>
        public readonly float* A;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaBlendingOutput"/> struct.
        /// </summary>
        /// <param name="r">Red channel output row pointer.</param>
        /// <param name="g">Green channel output row pointer.</param>
        /// <param name="b">Blue channel output row pointer.</param>
        /// <param name="a">Alpha channel output row pointer.</param>
        public AlphaBlendingOutput(float* r, float* g, float* b, float* a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>
    /// Alpha compositing, premultiplication, and unpremultiplication primitives.
    /// Ported from <c>lib/jxl/alpha.h</c> and <c>lib/jxl/alpha.cc</c>.
    /// </summary>
    public static unsafe class Alpha
    {
        // A very small value to avoid divisions by zero when converting to
        // unpremultiplied alpha. Page 21 of the technical introduction to OpenEXR
        // (https://www.openexr.com/documentation/TechnicalIntroduction.pdf) recommends
        // "a power of two" that is "less than half of the smallest positive 16-bit
        // floating-point value". That smallest value happens to be the denormal number
        // 2^-24, so 2^-26 should be a good choice.

        /// <summary>Small threshold value (<c>2^-26</c>) to avoid division by zero during alpha unpremultiplication.</summary>
        public const float kSmallAlpha = 1.0f / (1u << 26);

        // Note: The pointers in `out` are allowed to alias those in `bg` or `fg`.
        // No pointer shall be null.

        /// <summary>
        /// Performs 4-channel RGBA alpha blending of <paramref name="fg"/> over <paramref name="bg"/> into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background RGBA row pointers.</param>
        /// <param name="fg">Foreground RGBA row pointers.</param>
        /// <param name="output">Output RGBA row pointers (may alias <paramref name="bg"/> or <paramref name="fg"/>).</param>
        /// <param name="numPixels">Number of pixels to blend.</param>
        /// <param name="alphaIsPremultiplied">Whether RGB channels are premultiplied by alpha.</param>
        /// <param name="clamp">Whether foreground alpha values should be clamped to <c>[0, 1]</c>.</param>
        public static void PerformAlphaBlending(
            in AlphaBlendingInputLayer bg,
            in AlphaBlendingInputLayer fg,
            in AlphaBlendingOutput output,
            nuint numPixels,
            bool alphaIsPremultiplied,
            bool clamp)
        {
            if (alphaIsPremultiplied)
            {
                for (nuint x = 0; x < numPixels; ++x)
                {
                    float fga   = clamp ? Clamp01(fg.A[x]) : fg.A[x];
                    float invFa = 1.0f - fga;
                    output.R[x] = fg.R[x] + bg.R[x] * invFa;
                    output.G[x] = fg.G[x] + bg.G[x] * invFa;
                    output.B[x] = fg.B[x] + bg.B[x] * invFa;
                    output.A[x] = 1.0f - invFa * (1.0f - bg.A[x]);
                }

                return;
            }

            for (nuint x = 0; x < numPixels; ++x)
            {
                float fga   = clamp ? Clamp01(fg.A[x]) : fg.A[x];
                float invFa = 1.0f - fga;
                float newA  = 1.0f - invFa * (1.0f - bg.A[x]);
                float rNewA = newA > 0.0f ? 1.0f / newA : 0.0f;
                float bgWeight = bg.A[x] * invFa;

                output.R[x] = (fg.R[x] * fga + bg.R[x] * bgWeight) * rNewA;
                output.G[x] = (fg.G[x] * fga + bg.G[x] * bgWeight) * rNewA;
                output.B[x] = (fg.B[x] * fga + bg.B[x] * bgWeight) * rNewA;
                output.A[x] = newA;
            }
        }

        /// <summary>
        /// Performs single-plane alpha blending of <paramref name="fg"/> over <paramref name="bg"/> into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background sample row pointer.</param>
        /// <param name="bga">Background alpha row pointer.</param>
        /// <param name="fg">Foreground sample row pointer.</param>
        /// <param name="fga">Foreground alpha row pointer.</param>
        /// <param name="output">Output sample row pointer.</param>
        /// <param name="numPixels">Number of pixels to blend.</param>
        /// <param name="flags">Tuple of (<c>AlphaIsPremultiplied</c>, <c>Clamp</c>).</param>
        public static void PerformAlphaBlending(
            float* bg,
            float* bga,
            float* fg,
            float* fga,
            float* output,
            nuint numPixels,
            (bool AlphaIsPremultiplied, bool Clamp) flags)
        {
            bool clamp = flags.Clamp;

            if (bg == bga && fg == fga)
            {
                for (nuint x = 0; x < numPixels; ++x)
                {
                    float fa  = clamp ? Clamp01(fga[x]) : fga[x];
                    output[x] = 1.0f - (1.0f - fa) * (1.0f - bga[x]);
                }

                return;
            }

            if (flags.AlphaIsPremultiplied)
            {
                for (nuint x = 0; x < numPixels; ++x)
                {
                    float fa  = clamp ? Clamp01(fga[x]) : fga[x];
                    output[x] = fg[x] + bg[x] * (1.0f - fa);
                }

                return;
            }

            for (nuint x = 0; x < numPixels; ++x)
            {
                float fa    = clamp ? Clamp01(fga[x]) : fga[x];
                float newA  = 1.0f - (1.0f - fa) * (1.0f - bga[x]);
                float rNewA = newA > 0.0f ? 1.0f / newA : 0.0f;
                output[x]   = (fg[x] * fa + bg[x] * bga[x] * (1.0f - fa)) * rNewA;
            }
        }

        /// <summary>
        /// Performs alpha-weighted addition (<c>bg + fg * fga</c>) into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background sample row pointer.</param>
        /// <param name="fg">Foreground sample row pointer.</param>
        /// <param name="fga">Foreground alpha row pointer.</param>
        /// <param name="output">Output sample row pointer.</param>
        /// <param name="numPixels">Number of pixels.</param>
        /// <param name="clamp">Whether <paramref name="fga"/> is clamped to <c>[0, 1]</c>.</param>
        public static void PerformAlphaWeightedAdd(
            float* bg,
            float* fg,
            float* fga,
            float* output,
            nuint numPixels,
            bool clamp)
        {
            if (fg == fga)
            {
                if (output != bg && numPixels > 0)
                {
                    Buffer.MemoryCopy(bg, output, numPixels * sizeof(float), numPixels * sizeof(float));
                }

                return;
            }

            if (clamp)
            {
                for (nuint x = 0; x < numPixels; ++x)
                {
                    output[x] = bg[x] + fg[x] * Clamp01(fga[x]);
                }

                return;
            }

            for (nuint x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] + fg[x] * fga[x];
            }
        }

        /// <summary>
        /// Performs multiplicative blending (<c>bg * fg</c>) into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background sample row pointer.</param>
        /// <param name="fg">Foreground factor row pointer.</param>
        /// <param name="output">Output sample row pointer.</param>
        /// <param name="numPixels">Number of pixels.</param>
        /// <param name="clamp">Whether <paramref name="fg"/> is clamped to <c>[0, 1]</c>.</param>
        public static void PerformMulBlending(
            float* bg,
            float* fg,
            float* output,
            nuint numPixels,
            bool clamp)
        {
            if (clamp)
            {
                for (nuint x = 0; x < numPixels; ++x)
                {
                    output[x] = bg[x] * Clamp01(fg[x]);
                }

                return;
            }

            for (nuint x = 0; x < numPixels; ++x)
            {
                output[x] = bg[x] * fg[x];
            }
        }

        /// <summary>
        /// Premultiplies in-place RGB channels by <c>max(kSmallAlpha, a[x])</c>.
        /// </summary>
        /// <param name="r">Red channel row pointer.</param>
        /// <param name="g">Green channel row pointer.</param>
        /// <param name="b">Blue channel row pointer.</param>
        /// <param name="a">Alpha channel row pointer.</param>
        /// <param name="numPixels">Number of pixels.</param>
        public static void PremultiplyAlpha(
            float* r,
            float* g,
            float* b,
            float* a,
            nuint numPixels)
        {
            for (nuint x = 0; x < numPixels; ++x)
            {
                float multiplier = MathF.Max(kSmallAlpha, a[x]);
                r[x]            *= multiplier;
                g[x]            *= multiplier;
                b[x]            *= multiplier;
            }
        }

        /// <summary>
        /// Unpremultiplies in-place RGB channels by <c>1.0f / max(kSmallAlpha, a[x])</c>.
        /// </summary>
        /// <param name="r">Red channel row pointer.</param>
        /// <param name="g">Green channel row pointer.</param>
        /// <param name="b">Blue channel row pointer.</param>
        /// <param name="a">Alpha channel row pointer.</param>
        /// <param name="numPixels">Number of pixels.</param>
        public static void UnpremultiplyAlpha(
            float* r,
            float* g,
            float* b,
            float* a,
            nuint numPixels)
        {
            for (nuint x = 0; x < numPixels; ++x)
            {
                float multiplier = 1.0f / MathF.Max(kSmallAlpha, a[x]);
                r[x]            *= multiplier;
                g[x]            *= multiplier;
                b[x]            *= multiplier;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp01(float x)
        {
            return Common.Clamp1(x, 0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Frame and patch row blending operations across color and extra channels.
    /// Ported from <c>lib/jxl/blending.h</c> and <c>lib/jxl/blending.cc</c>.
    /// </summary>
    public static unsafe class Blending
    {
        /// <summary>
        /// Bundles background, foreground, and output plane row pointer arrays for <see cref="PerformBlending"/>.
        /// </summary>
        public readonly struct BlendingRowPointers
        {
            /// <summary>Background channel row pointers (3 color + extra channels).</summary>
            public readonly float** Bg;

            /// <summary>Foreground channel row pointers (3 color + extra channels).</summary>
            public readonly float** Fg;

            /// <summary>Output channel row pointers (3 color + extra channels).</summary>
            public readonly float** Out;

            /// <summary>Starting horizontal offset in pixels.</summary>
            public readonly nuint X0;

            /// <summary>Number of pixels to blend.</summary>
            public readonly nuint XSize;

            /// <summary>
            /// Initializes a new instance of the <see cref="BlendingRowPointers"/> struct.
            /// </summary>
            /// <param name="bg">Background row pointers.</param>
            /// <param name="fg">Foreground row pointers.</param>
            /// <param name="output">Output row pointers.</param>
            /// <param name="x0">Horizontal start offset.</param>
            /// <param name="xsize">Number of pixels to blend.</param>
            public BlendingRowPointers(float** bg, float** fg, float** output, nuint x0, nuint xsize)
            {
                Bg    = bg;
                Fg    = fg;
                Out   = output;
                X0    = x0;
                XSize = xsize;
            }
        }

        /// <summary>
        /// Determines whether the frame described by <paramref name="frameHeader"/> requires blending with a reference frame.
        /// </summary>
        /// <param name="frameHeader">Frame header to inspect.</param>
        /// <returns><see langword="true"/> if blending is required; otherwise <see langword="false"/>.</returns>
        public static bool NeedsBlending(FrameHeader frameHeader)
        {
            if (!(frameHeader.FrameType == FrameType.RegularFrame ||
                  frameHeader.FrameType == FrameType.SkipProgressive))
            {
                return false;
            }

            BlendingInfo info = frameHeader.BlendingInfo;
            bool replaceAll   = info.Mode == BlendMode.Replace;

            foreach (BlendingInfo ecInfo in frameHeader.ExtraChannelBlendingInfo)
            {
                if (ecInfo.Mode != BlendMode.Replace)
                {
                    replaceAll = false;
                }
            }

            // Replace the full frame: nothing to do.
            if (!frameHeader.CustomSizeOrOrigin && replaceAll)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs blending of one horizontal span of color and extra channels.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary row buffer allocation.</param>
        /// <param name="ptrs">Background, foreground, and output row pointers and span bounds.</param>
        /// <param name="colorBlending">Color channel blending parameters.</param>
        /// <param name="ecBlending">Extra channel blending parameters span.</param>
        /// <param name="extraChannelInfo">Metadata for all extra channels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status PerformBlending(
            JxlMemoryManager? memoryManager,
            in BlendingRowPointers ptrs,
            PatchBlending colorBlending,
            ReadOnlySpan<PatchBlending> ecBlending,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            bool hasAlpha = HasAnyAlphaChannel(extraChannelInfo);
            nuint numEc   = (nuint)extraChannelInfo.Count;

            StatusOr<ImagePlane<float>> tmpResult = ImagePlane<float>.Create(
                memoryManager ?? JxlMemoryManager.Default,
                ptrs.XSize,
                3 + numEc);

            if (!tmpResult.IsOk)
            {
                return tmpResult.Status;
            }

            using ImagePlane<float> tmp = tmpResult.Value;

            // Blend extra channels first so that we use the pre-blending alpha.
            for (nuint i = 0; i < numEc; i++)
            {
                BlendSingleExtraChannel(in ptrs, i, ecBlending[(int)i], extraChannelInfo, tmp);
            }

            BlendColorChannels(in ptrs, colorBlending, hasAlpha, extraChannelInfo, tmp);

            if (ptrs.XSize != 0)
            {
                nuint copyBytes = ptrs.XSize * sizeof(float);

                for (nuint i = 0; i < 3 + numEc; i++)
                {
                    Buffer.MemoryCopy(tmp.Row(i), ptrs.Out[i] + ptrs.X0, copyBytes, copyBytes);
                }
            }

            return true;
        }

        private static bool HasAnyAlphaChannel(IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            for (int i = 0; i < extraChannelInfo.Count; i++)
            {
                if (extraChannelInfo[i].Type == ExtraChannel.Alpha)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BlendSingleExtraChannel(
            in BlendingRowPointers ptrs,
            nuint ecIndex,
            PatchBlending ecBlend,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo,
            ImagePlane<float> tmp)
        {
            nuint chIdx   = 3 + ecIndex;
            nuint x0      = ptrs.X0;
            nuint xsize   = ptrs.XSize;
            float* dstRow = tmp.Row(chIdx);
            float* bgRow  = ptrs.Bg[chIdx] + x0;
            float* fgRow  = ptrs.Fg[chIdx] + x0;

            switch (ecBlend.Mode)
            {
                case PatchBlendMode.kAdd:
                    for (nuint x = 0; x < xsize; x++)
                    {
                        dstRow[x] = bgRow[x] + fgRow[x];
                    }

                    break;

                case PatchBlendMode.kBlendAbove:
                case PatchBlendMode.kBlendBelow:
                    BlendExtraChannelWithAlpha(in ptrs, bgRow, fgRow, dstRow, ecBlend, extraChannelInfo);
                    break;

                case PatchBlendMode.kAlphaWeightedAddAbove:
                    Alpha.PerformAlphaWeightedAdd(
                        bgRow,
                        fgRow,
                        ptrs.Fg[3 + ecBlend.AlphaChannel] + x0,
                        dstRow,
                        xsize,
                        ecBlend.Clamp);
                    break;

                case PatchBlendMode.kAlphaWeightedAddBelow:
                    Alpha.PerformAlphaWeightedAdd(
                        fgRow,
                        bgRow,
                        ptrs.Bg[3 + ecBlend.AlphaChannel] + x0,
                        dstRow,
                        xsize,
                        ecBlend.Clamp);
                    break;

                case PatchBlendMode.kMul:
                    Alpha.PerformMulBlending(bgRow, fgRow, dstRow, xsize, ecBlend.Clamp);
                    break;

                case PatchBlendMode.kReplace:
                    if (xsize != 0)
                    {
                        Buffer.MemoryCopy(fgRow, dstRow, xsize * sizeof(float), xsize * sizeof(float));
                    }

                    break;

                case PatchBlendMode.kNone:
                    if (xsize != 0)
                    {
                        Buffer.MemoryCopy(bgRow, dstRow, xsize * sizeof(float), xsize * sizeof(float));
                    }

                    break;
            }
        }

        private static void BlendExtraChannelWithAlpha(
            in BlendingRowPointers ptrs,
            float* bgRow,
            float* fgRow,
            float* dstRow,
            PatchBlending ecBlend,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            uint alpha           = ecBlend.AlphaChannel;
            bool isPremultiplied = extraChannelInfo[(int)alpha].AlphaAssociated;
            float* bgaRow        = ptrs.Bg[3 + alpha] + ptrs.X0;
            float* fgaRow        = ptrs.Fg[3 + alpha] + ptrs.X0;

            if (ecBlend.Mode == PatchBlendMode.kBlendAbove)
            {
                Alpha.PerformAlphaBlending(
                    bgRow, bgaRow, fgRow, fgaRow, dstRow, ptrs.XSize, (isPremultiplied, ecBlend.Clamp));
            }
            else
            {
                Alpha.PerformAlphaBlending(
                    fgRow, fgaRow, bgRow, bgaRow, dstRow, ptrs.XSize, (isPremultiplied, ecBlend.Clamp));
            }
        }

        private static void BlendColorChannels(
            in BlendingRowPointers ptrs,
            PatchBlending colorBlending,
            bool hasAlpha,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo,
            ImagePlane<float> tmp)
        {
            uint alpha = colorBlending.AlphaChannel;

            switch (colorBlending.Mode)
            {
                case PatchBlendMode.kAdd:
                    AddColorPlanes(in ptrs, tmp);
                    break;

                case PatchBlendMode.kAlphaWeightedAddAbove:
                    if (hasAlpha)
                    {
                        AddWeightedColorPlanes(ptrs.Bg, ptrs.Fg, ptrs.X0, ptrs.XSize, alpha, colorBlending.Clamp, tmp);
                    }
                    else
                    {
                        AddColorPlanes(in ptrs, tmp);
                    }

                    break;

                case PatchBlendMode.kAlphaWeightedAddBelow:
                    if (hasAlpha)
                    {
                        AddWeightedColorPlanes(ptrs.Fg, ptrs.Bg, ptrs.X0, ptrs.XSize, alpha, colorBlending.Clamp, tmp);
                    }
                    else
                    {
                        AddColorPlanes(in ptrs, tmp);
                    }

                    break;

                case PatchBlendMode.kBlendAbove:
                    if (hasAlpha)
                    {
                        BlendWeightedColorPlanes(
                            ptrs.Bg, ptrs.Fg, ptrs.X0, ptrs.XSize, alpha, extraChannelInfo[(int)alpha].AlphaAssociated, colorBlending.Clamp, tmp);
                    }
                    else
                    {
                        CopyColorPlanes(ptrs.Fg, ptrs.X0, ptrs.XSize, tmp);
                    }

                    break;

                case PatchBlendMode.kBlendBelow:
                    if (hasAlpha)
                    {
                        BlendWeightedColorPlanes(
                            ptrs.Fg, ptrs.Bg, ptrs.X0, ptrs.XSize, alpha, extraChannelInfo[(int)alpha].AlphaAssociated, colorBlending.Clamp, tmp);
                    }
                    else
                    {
                        CopyColorPlanes(ptrs.Fg, ptrs.X0, ptrs.XSize, tmp);
                    }

                    break;

                case PatchBlendMode.kMul:
                    for (nuint p = 0; p < 3; p++)
                    {
                        Alpha.PerformMulBlending(
                            ptrs.Bg[p] + ptrs.X0,
                            ptrs.Fg[p] + ptrs.X0,
                            tmp.Row(p),
                            ptrs.XSize,
                            colorBlending.Clamp);
                    }

                    break;

                case PatchBlendMode.kReplace:
                    CopyColorPlanes(ptrs.Fg, ptrs.X0, ptrs.XSize, tmp);
                    break;

                case PatchBlendMode.kNone:
                    CopyColorPlanes(ptrs.Bg, ptrs.X0, ptrs.XSize, tmp);
                    break;
            }
        }

        private static void AddColorPlanes(in BlendingRowPointers ptrs, ImagePlane<float> tmp)
        {
            nuint x0    = ptrs.X0;
            nuint xsize = ptrs.XSize;

            for (nuint p = 0; p < 3; p++)
            {
                float* dst = tmp.Row(p);
                float* bg  = ptrs.Bg[p] + x0;
                float* fg  = ptrs.Fg[p] + x0;

                for (nuint x = 0; x < xsize; x++)
                {
                    dst[x] = bg[x] + fg[x];
                }
            }
        }

        private static void AddWeightedColorPlanes(
            float** bottom,
            float** top,
            nuint x0,
            nuint xsize,
            uint alpha,
            bool clamp,
            ImagePlane<float> tmp)
        {
            float* topAlpha = top[3 + alpha] + x0;

            for (nuint c = 0; c < 3; c++)
            {
                Alpha.PerformAlphaWeightedAdd(
                    bottom[c] + x0,
                    top[c] + x0,
                    topAlpha,
                    tmp.Row(c),
                    xsize,
                    clamp);
            }
        }

        private static void BlendWeightedColorPlanes(
            float** bottom,
            float** top,
            nuint x0,
            nuint xsize,
            uint alpha,
            bool isPremultiplied,
            bool clamp,
            ImagePlane<float> tmp)
        {
            AlphaBlendingInputLayer bgLayer = new AlphaBlendingInputLayer(
                bottom[0] + x0,
                bottom[1] + x0,
                bottom[2] + x0,
                bottom[3 + alpha] + x0);

            AlphaBlendingInputLayer fgLayer = new AlphaBlendingInputLayer(
                top[0] + x0,
                top[1] + x0,
                top[2] + x0,
                top[3 + alpha] + x0);

            AlphaBlendingOutput outLayer = new AlphaBlendingOutput(
                tmp.Row(0),
                tmp.Row(1),
                tmp.Row(2),
                tmp.Row(3 + alpha));

            Alpha.PerformAlphaBlending(in bgLayer, in fgLayer, in outLayer, xsize, isPremultiplied, clamp);
        }

        private static void CopyColorPlanes(float** src, nuint x0, nuint xsize, ImagePlane<float> tmp)
        {
            if (xsize == 0)
            {
                return;
            }

            nuint copyBytes = xsize * sizeof(float);

            for (nuint p = 0; p < 3; p++)
            {
                Buffer.MemoryCopy(src[p] + x0, tmp.Row(p), copyBytes, copyBytes);
            }
        }
    }
}
