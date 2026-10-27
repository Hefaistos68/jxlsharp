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
    public readonly ref struct AlphaBlendingInputLayer
    {
        /// <summary>Red channel row span.</summary>
        public readonly ReadOnlySpan<float> R;

        /// <summary>Green channel row span.</summary>
        public readonly ReadOnlySpan<float> G;

        /// <summary>Blue channel row span.</summary>
        public readonly ReadOnlySpan<float> B;

        /// <summary>Alpha channel row span.</summary>
        public readonly ReadOnlySpan<float> A;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaBlendingInputLayer"/> struct.
        /// </summary>
        /// <param name="r">Red channel row span.</param>
        /// <param name="g">Green channel row span.</param>
        /// <param name="b">Blue channel row span.</param>
        /// <param name="a">Alpha channel row span.</param>
        public AlphaBlendingInputLayer(
            ReadOnlySpan<float> r,
            ReadOnlySpan<float> g,
            ReadOnlySpan<float> b,
            ReadOnlySpan<float> a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>
    /// Output RGBA row spans for <see cref="Alpha.PerformAlphaBlending(in AlphaBlendingInputLayer, in AlphaBlendingInputLayer, in AlphaBlendingOutput, nuint, bool, bool)"/>.
    /// Ported from <c>jxl::AlphaBlendingOutput</c> in <c>lib/jxl/alpha.h</c>.
    /// </summary>
    public readonly ref struct AlphaBlendingOutput
    {
        /// <summary>Red channel output row span.</summary>
        public readonly Span<float> R;

        /// <summary>Green channel output row span.</summary>
        public readonly Span<float> G;

        /// <summary>Blue channel output row span.</summary>
        public readonly Span<float> B;

        /// <summary>Alpha channel output row span.</summary>
        public readonly Span<float> A;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaBlendingOutput"/> struct.
        /// </summary>
        /// <param name="r">Red channel output row span.</param>
        /// <param name="g">Green channel output row span.</param>
        /// <param name="b">Blue channel output row span.</param>
        /// <param name="a">Alpha channel output row span.</param>
        public AlphaBlendingOutput(
            Span<float> r,
            Span<float> g,
            Span<float> b,
            Span<float> a)
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
    public static class Alpha
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
        /// <param name="bg">Background RGBA row spans.</param>
        /// <param name="fg">Foreground RGBA row spans.</param>
        /// <param name="output">Output RGBA row spans (may alias <paramref name="bg"/> or <paramref name="fg"/>).</param>
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
                    int ix         = (int)x;
                    float fga      = clamp ? Clamp01(fg.A[ix]) : fg.A[ix];
                    float invFa    = 1.0f - fga;
                    output.R[ix]   = fg.R[ix] + bg.R[ix] * invFa;
                    output.G[ix]   = fg.G[ix] + bg.G[ix] * invFa;
                    output.B[ix]   = fg.B[ix] + bg.B[ix] * invFa;
                    output.A[ix]   = 1.0f - invFa * (1.0f - bg.A[ix]);
                }

                return;
            }

            for (nuint x = 0; x < numPixels; ++x)
            {
                int ix         = (int)x;
                float fga      = clamp ? Clamp01(fg.A[ix]) : fg.A[ix];
                float invFa    = 1.0f - fga;
                float newA     = 1.0f - invFa * (1.0f - bg.A[ix]);
                float rNewA    = newA > 0.0f ? 1.0f / newA : 0.0f;
                float bgWeight = bg.A[ix] * invFa;

                output.R[ix] = (fg.R[ix] * fga + bg.R[ix] * bgWeight) * rNewA;
                output.G[ix] = (fg.G[ix] * fga + bg.G[ix] * bgWeight) * rNewA;
                output.B[ix] = (fg.B[ix] * fga + bg.B[ix] * bgWeight) * rNewA;
                output.A[ix] = newA;
            }
        }

        /// <summary>
        /// Performs single-plane alpha blending of <paramref name="fg"/> over <paramref name="bg"/> into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background sample row span.</param>
        /// <param name="bga">Background alpha row span.</param>
        /// <param name="fg">Foreground sample row span.</param>
        /// <param name="fga">Foreground alpha row span.</param>
        /// <param name="output">Output sample row span.</param>
        /// <param name="numPixels">Number of pixels to blend.</param>
        /// <param name="flags">Tuple of (<c>AlphaIsPremultiplied</c>, <c>Clamp</c>).</param>
        public static void PerformAlphaBlending(
            ReadOnlySpan<float> bg,
            ReadOnlySpan<float> bga,
            ReadOnlySpan<float> fg,
            ReadOnlySpan<float> fga,
            Span<float> output,
            nuint numPixels,
            (bool AlphaIsPremultiplied, bool Clamp) flags)
        {
            bool clamp = flags.Clamp;
            int count  = (int)numPixels;

            if (bg.Slice(0, count) == bga.Slice(0, count) &&
                fg.Slice(0, count) == fga.Slice(0, count))
            {
                for (int x = 0; x < count; ++x)
                {
                    float fa  = clamp ? Clamp01(fga[x]) : fga[x];
                    output[x] = 1.0f - (1.0f - fa) * (1.0f - bga[x]);
                }

                return;
            }

            if (flags.AlphaIsPremultiplied)
            {
                for (int x = 0; x < count; ++x)
                {
                    float fa  = clamp ? Clamp01(fga[x]) : fga[x];
                    output[x] = fg[x] + bg[x] * (1.0f - fa);
                }

                return;
            }

            for (int x = 0; x < count; ++x)
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
        /// <param name="bg">Background sample row span.</param>
        /// <param name="fg">Foreground sample row span.</param>
        /// <param name="fga">Foreground alpha row span.</param>
        /// <param name="output">Output sample row span.</param>
        /// <param name="numPixels">Number of pixels.</param>
        /// <param name="clamp">Whether <paramref name="fga"/> is clamped to <c>[0, 1]</c>.</param>
        public static void PerformAlphaWeightedAdd(
            ReadOnlySpan<float> bg,
            ReadOnlySpan<float> fg,
            ReadOnlySpan<float> fga,
            Span<float> output,
            nuint numPixels,
            bool clamp)
        {
            int count = (int)numPixels;

            if (fg.Slice(0, count) == fga.Slice(0, count))
            {
                if (count > 0 && output.Slice(0, count) != bg.Slice(0, count))
                {
                    bg.Slice(0, count).CopyTo(output.Slice(0, count));
                }

                return;
            }

            if (clamp)
            {
                for (int x = 0; x < count; ++x)
                {
                    output[x] = bg[x] + fg[x] * Clamp01(fga[x]);
                }

                return;
            }

            for (int x = 0; x < count; ++x)
            {
                output[x] = bg[x] + fg[x] * fga[x];
            }
        }

        /// <summary>
        /// Performs multiplicative blending (<c>bg * fg</c>) into <paramref name="output"/>.
        /// </summary>
        /// <param name="bg">Background sample row span.</param>
        /// <param name="fg">Foreground factor row span.</param>
        /// <param name="output">Output sample row span.</param>
        /// <param name="numPixels">Number of pixels.</param>
        /// <param name="clamp">Whether <paramref name="fg"/> is clamped to <c>[0, 1]</c>.</param>
        public static void PerformMulBlending(
            ReadOnlySpan<float> bg,
            ReadOnlySpan<float> fg,
            Span<float> output,
            nuint numPixels,
            bool clamp)
        {
            int count = (int)numPixels;

            if (clamp)
            {
                for (int x = 0; x < count; ++x)
                {
                    output[x] = bg[x] * Clamp01(fg[x]);
                }

                return;
            }

            for (int x = 0; x < count; ++x)
            {
                output[x] = bg[x] * fg[x];
            }
        }

        /// <summary>
        /// Premultiplies in-place RGB channels by <c>max(kSmallAlpha, a[x])</c>.
        /// </summary>
        /// <param name="r">Red channel row span.</param>
        /// <param name="g">Green channel row span.</param>
        /// <param name="b">Blue channel row span.</param>
        /// <param name="a">Alpha channel row span.</param>
        /// <param name="numPixels">Number of pixels.</param>
        public static void PremultiplyAlpha(
            Span<float> r,
            Span<float> g,
            Span<float> b,
            ReadOnlySpan<float> a,
            nuint numPixels)
        {
            int count = (int)numPixels;

            for (int x = 0; x < count; ++x)
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
        /// <param name="r">Red channel row span.</param>
        /// <param name="g">Green channel row span.</param>
        /// <param name="b">Blue channel row span.</param>
        /// <param name="a">Alpha channel row span.</param>
        /// <param name="numPixels">Number of pixels.</param>
        public static void UnpremultiplyAlpha(
            Span<float> r,
            Span<float> g,
            Span<float> b,
            ReadOnlySpan<float> a,
            nuint numPixels)
        {
            int count = (int)numPixels;

            for (int x = 0; x < count; ++x)
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
    public static class Blending
    {
        /// <summary>
        /// Bundles background, foreground, and output plane row views for <see cref="PerformBlending"/>.
        /// </summary>
        public readonly struct BlendingRowPointers
        {
            /// <summary>Background channel row views (3 color + extra channels).</summary>
            public readonly PipelineRow[] Bg;

            /// <summary>Foreground channel row views (3 color + extra channels).</summary>
            public readonly PipelineRow[] Fg;

            /// <summary>Output channel row views (3 color + extra channels).</summary>
            public readonly PipelineRow[] Out;

            /// <summary>Starting horizontal offset in pixels.</summary>
            public readonly nuint X0;

            /// <summary>Number of pixels to blend.</summary>
            public readonly nuint XSize;

            /// <summary>
            /// Initializes a new instance of the <see cref="BlendingRowPointers"/> struct.
            /// </summary>
            /// <param name="bg">Background row views.</param>
            /// <param name="fg">Foreground row views.</param>
            /// <param name="output">Output row views.</param>
            /// <param name="x0">Horizontal start offset.</param>
            /// <param name="xsize">Number of pixels to blend.</param>
            public BlendingRowPointers(PipelineRow[] bg, PipelineRow[] fg, PipelineRow[] output, nuint x0, nuint xsize)
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
        /// <param name="ptrs">Background, foreground, and output row views and span bounds.</param>
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
                int count = (int)ptrs.XSize;
                int x0    = (int)ptrs.X0;

                for (nuint i = 0; i < 3 + numEc; i++)
                {
                    tmp.Row(i).Slice(0, count).CopyTo(ptrs.Out[(int)i].AsSpan(x0, count));
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
            nuint chIdx               = 3 + ecIndex;
            int x0                    = (int)ptrs.X0;
            nuint xsize               = ptrs.XSize;
            int count                 = (int)xsize;
            Span<float> dstRow        = tmp.Row(chIdx).Slice(0, count);
            ReadOnlySpan<float> bgRow = ptrs.Bg[(int)chIdx].AsSpan(x0, count);
            ReadOnlySpan<float> fgRow = ptrs.Fg[(int)chIdx].AsSpan(x0, count);

            switch (ecBlend.Mode)
            {
                case PatchBlendMode.kAdd:
                    for (int x = 0; x < count; x++)
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
                        ptrs.Fg[(int)(3 + ecBlend.AlphaChannel)].AsSpan(x0, count),
                        dstRow,
                        xsize,
                        ecBlend.Clamp);
                    break;

                case PatchBlendMode.kAlphaWeightedAddBelow:
                    Alpha.PerformAlphaWeightedAdd(
                        fgRow,
                        bgRow,
                        ptrs.Bg[(int)(3 + ecBlend.AlphaChannel)].AsSpan(x0, count),
                        dstRow,
                        xsize,
                        ecBlend.Clamp);
                    break;

                case PatchBlendMode.kMul:
                    Alpha.PerformMulBlending(bgRow, fgRow, dstRow, xsize, ecBlend.Clamp);
                    break;

                case PatchBlendMode.kReplace:
                    if (count != 0)
                    {
                        fgRow.CopyTo(dstRow);
                    }

                    break;

                case PatchBlendMode.kNone:
                    if (count != 0)
                    {
                        bgRow.CopyTo(dstRow);
                    }

                    break;
            }
        }

        private static void BlendExtraChannelWithAlpha(
            in BlendingRowPointers ptrs,
            ReadOnlySpan<float> bgRow,
            ReadOnlySpan<float> fgRow,
            Span<float> dstRow,
            PatchBlending ecBlend,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            uint alpha                 = ecBlend.AlphaChannel;
            bool isPremultiplied       = extraChannelInfo[(int)alpha].AlphaAssociated;
            int x0                     = (int)ptrs.X0;
            int count                  = (int)ptrs.XSize;
            ReadOnlySpan<float> bgaRow = ptrs.Bg[(int)(3 + alpha)].AsSpan(x0, count);
            ReadOnlySpan<float> fgaRow = ptrs.Fg[(int)(3 + alpha)].AsSpan(x0, count);

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
                            ptrs.Bg,
                            ptrs.Fg,
                            ptrs.X0,
                            ptrs.XSize,
                            alpha,
                            (extraChannelInfo[(int)alpha].AlphaAssociated, colorBlending.Clamp),
                            tmp);
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
                            ptrs.Fg,
                            ptrs.Bg,
                            ptrs.X0,
                            ptrs.XSize,
                            alpha,
                            (extraChannelInfo[(int)alpha].AlphaAssociated, colorBlending.Clamp),
                            tmp);
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
                            ptrs.Bg[(int)p].AsSpan((int)ptrs.X0, (int)ptrs.XSize),
                            ptrs.Fg[(int)p].AsSpan((int)ptrs.X0, (int)ptrs.XSize),
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
            int x0    = (int)ptrs.X0;
            int count = (int)ptrs.XSize;

            for (nuint p = 0; p < 3; p++)
            {
                Span<float> dst        = tmp.Row(p);
                ReadOnlySpan<float> bg = ptrs.Bg[(int)p].AsSpan(x0, count);
                ReadOnlySpan<float> fg = ptrs.Fg[(int)p].AsSpan(x0, count);

                for (int x = 0; x < count; x++)
                {
                    dst[x] = bg[x] + fg[x];
                }
            }
        }

        private static void AddWeightedColorPlanes(
            PipelineRow[] bottom,
            PipelineRow[] top,
            nuint x0,
            nuint xsize,
            uint alpha,
            bool clamp,
            ImagePlane<float> tmp)
        {
            int start                    = (int)x0;
            int count                    = (int)xsize;
            ReadOnlySpan<float> topAlpha = top[(int)(3 + alpha)].AsSpan(start, count);

            for (nuint c = 0; c < 3; c++)
            {
                Alpha.PerformAlphaWeightedAdd(
                    bottom[(int)c].AsSpan(start, count),
                    top[(int)c].AsSpan(start, count),
                    topAlpha,
                    tmp.Row(c),
                    xsize,
                    clamp);
            }
        }

        private static void BlendWeightedColorPlanes(
            PipelineRow[] bottom,
            PipelineRow[] top,
            nuint x0,
            nuint xsize,
            uint alpha,
            (bool IsPremultiplied, bool Clamp) flags,
            ImagePlane<float> tmp)
        {
            int start = (int)x0;
            int count = (int)xsize;

            AlphaBlendingInputLayer bgLayer = new AlphaBlendingInputLayer(
                bottom[0].AsSpan(start, count),
                bottom[1].AsSpan(start, count),
                bottom[2].AsSpan(start, count),
                bottom[(int)(3 + alpha)].AsSpan(start, count));

            AlphaBlendingInputLayer fgLayer = new AlphaBlendingInputLayer(
                top[0].AsSpan(start, count),
                top[1].AsSpan(start, count),
                top[2].AsSpan(start, count),
                top[(int)(3 + alpha)].AsSpan(start, count));

            AlphaBlendingOutput outLayer = new AlphaBlendingOutput(
                tmp.Row(0).Slice(0, count),
                tmp.Row(1).Slice(0, count),
                tmp.Row(2).Slice(0, count),
                tmp.Row(3 + alpha).Slice(0, count));

            Alpha.PerformAlphaBlending(in bgLayer, in fgLayer, in outLayer, xsize, flags.IsPremultiplied, flags.Clamp);
        }

        private static void CopyColorPlanes(PipelineRow[] src, nuint x0, nuint xsize, ImagePlane<float> tmp)
        {
            if (xsize == 0)
            {
                return;
            }

            int start = (int)x0;
            int count = (int)xsize;

            for (nuint p = 0; p < 3; p++)
            {
                src[(int)p].AsSpan(start, count).CopyTo(tmp.Row(p).Slice(0, count));
            }
        }
    }
}
