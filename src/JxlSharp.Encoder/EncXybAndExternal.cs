// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Converts linear RGB / sRGB images to XYB or YCbCr color spaces.
    /// Ported from <c>lib/jxl/enc_xyb.h</c> and <c>lib/jxl/enc_xyb.cc</c>.
    /// </summary>
    public static class EncXyb
    {
        /// <summary>
        /// Computes fast cube root using initial IEEE-754 exponent/mantissa approximation
        /// followed by Newton-Raphson iterations.
        /// Matches <c>CubeRootAndAdd</c> in <c>lib/jxl/base/fast_math-inl.h</c>.
        /// </summary>
        /// <param name="v">Non-negative input value.</param>
        /// <param name="add">Bias term to add to <c>cbrt(v)</c>.</param>
        /// <returns><c>cbrt(v) + add</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubeRootAndAdd(float v, float add)
        {
            return FastMath.CubeRootAndAdd(v, add);
        }

        // Mixedird representation: opsin absorbance matrix application + cube root + XYB linear combination.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OpsinAbsorbance(
            float r,
            float g,
            float b,
            ReadOnlySpan<float> premulAbsorb,
            out float mixed0,
            out float mixed1,
            out float mixed2)
        {
            float m0 = premulAbsorb[0] * r + premulAbsorb[1] * g + premulAbsorb[2] * b + premulAbsorb[9];
            float m1 = premulAbsorb[3] * r + premulAbsorb[4] * g + premulAbsorb[5] * b + premulAbsorb[10];
            float m2 = premulAbsorb[6] * r + premulAbsorb[7] * g + premulAbsorb[8] * b + premulAbsorb[11];

            // Clamp negative values to zero prior to cube root.
            mixed0 = MathF.Max(0.0f, m0);
            mixed1 = MathF.Max(0.0f, m1);
            mixed2 = MathF.Max(0.0f, m2);
        }

        /// <summary>
        /// Precomputes the 12-float premultiplied opsin absorbance matrix and bias array
        /// (or 48-float SIMD-expanded buffer if length >= 48).
        /// Ported from <c>jxl::ComputePremulAbsorb</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="intensityTarget">Intensity target in nits (e.g., 255.0f).</param>
        /// <param name="premulAbsorb">Destination span of at least 12 floats.</param>
        public static void ComputePremulAbsorb(float intensityTarget, Span<float> premulAbsorb)
        {
            float mul = intensityTarget / 255.0f;

            for (int j = 0; j < 3; ++j)
            {
                for (int i = 0; i < 3; ++i)
                {
                    premulAbsorb[j * 3 + i] = Cms.OpsinParams.KOpsinAbsorbanceMatrix[j][i] * mul;
                }
            }

            for (int j = 0; j < 3; ++j)
            {
                float negBias       = -MathF.Cbrt(Cms.OpsinParams.KOpsinAbsorbanceBias[j]);
                premulAbsorb[9 + j] = Cms.OpsinParams.KOpsinAbsorbanceBias[j];

                if (premulAbsorb.Length >= 15)
                {
                    premulAbsorb[12 + j] = negBias;
                }
            }
        }

        /// <summary>
        /// Converts a single row of linear RGB values into XYB in-place or to separate spans.
        /// Ported from <c>jxl::LinearRGBRowToXYB</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="row0">Channel 0 (in: linear R, out: X).</param>
        /// <param name="row1">Channel 1 (in: linear G, out: Y).</param>
        /// <param name="row2">Channel 2 (in: linear B, out: B).</param>
        /// <param name="premulAbsorb">Premultiplied absorbance array from <see cref="ComputePremulAbsorb"/>.</param>
        /// <param name="xsize">Number of pixels in the row.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void LinearRGBRowToXYB(
            Span<float> row0,
            Span<float> row1,
            Span<float> row2,
            ReadOnlySpan<float> premulAbsorb,
            nuint xsize)
        {
            float negBias0 = premulAbsorb.Length >= 15
                ? premulAbsorb[12]
                : -MathF.Cbrt(premulAbsorb[9]);
            float negBias1 = premulAbsorb.Length >= 15
                ? premulAbsorb[13]
                : -MathF.Cbrt(premulAbsorb[10]);
            float negBias2 = premulAbsorb.Length >= 15
                ? premulAbsorb[14]
                : -MathF.Cbrt(premulAbsorb[11]);

            for (nuint x = 0; x < xsize; ++x)
            {
                int ix  = (int)x;
                float r = row0[ix];
                float g = row1[ix];
                float b = row2[ix];

                OpsinAbsorbance(r, g, b, premulAbsorb, out float m0, out float m1, out float m2);

                float c0 = CubeRootAndAdd(m0, negBias0);
                float c1 = CubeRootAndAdd(m1, negBias1);
                float c2 = CubeRootAndAdd(m2, negBias2);

                row0[ix] = 0.5f * (c0 - c1);
                row1[ix] = 0.5f * (c0 + c1);
                row2[ix] = c2;
            }
        }

        /// <summary>
        /// Converts an sRGB8 or linear sRGB plane row into XYB.
        /// </summary>
        /// <param name="hasRgba">Whether input is 3-channel color (<c>true</c>) or 1-channel grayscale (<c>false</c>).</param>
        /// <param name="isLinear">Whether input is already linear (<c>true</c>) or sRGB TF (<c>false</c>).</param>
        /// <param name="src">Source 3-plane float image.</param>
        /// <param name="srcRect">Source rectangle.</param>
        /// <param name="y">Row index within <paramref name="srcRect"/>.</param>
        /// <param name="premulAbsorb">Premultiplied absorbance array.</param>
        /// <param name="xyb">Destination XYB image.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ConvertRowToXyb(
            bool hasRgba,
            bool isLinear,
            Image3<float> src,
            Rect srcRect,
            nuint y,
            ReadOnlySpan<float> premulAbsorb,
            Image3<float> xyb)
        {
            nuint xsize                 = (nuint)srcRect.XSize;
            ReadOnlySpan<float> srcRow0 = srcRect.ConstPlaneRow(src, 0, y);
            ReadOnlySpan<float> srcRow1 = hasRgba ? srcRect.ConstPlaneRow(src, 1, y) : srcRow0;
            ReadOnlySpan<float> srcRow2 = hasRgba ? srcRect.ConstPlaneRow(src, 2, y) : srcRow0;
            Span<float> dstRow0         = xyb.PlaneRow(0, y);
            Span<float> dstRow1         = xyb.PlaneRow(1, y);
            Span<float> dstRow2         = xyb.PlaneRow(2, y);

            for (nuint x = 0; x < xsize; ++x)
            {
                int ix      = (int)x;
                dstRow0[ix] = isLinear ? srcRow0[ix] : TfSrgbToLinear(srcRow0[ix]);
                dstRow1[ix] = isLinear ? srcRow1[ix] : TfSrgbToLinear(srcRow1[ix]);
                dstRow2[ix] = isLinear ? srcRow2[ix] : TfSrgbToLinear(srcRow2[ix]);
            }

            LinearRGBRowToXYB(dstRow0, dstRow1, dstRow2, premulAbsorb, xsize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float TfSrgbToLinear(float val)
        {
            float sign = val < 0.0f ? -1.0f : 1.0f;
            float abs  = MathF.Abs(val);

            if (abs <= 0.04045f)
            {
                return val / 12.92f;
            }

            return sign * MathF.Pow((abs + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>
        /// Converts any color space <see cref="ImageBundle"/> to XYB <see cref="Image3{Single}"/>.
        /// Ported from <c>jxl::ToXYB</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="inBundle">Input image bundle.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="xyb">Output XYB 3-plane float image.</param>
        /// <param name="cms">CMS interface for non-sRGB profiles.</param>
        /// <param name="linear">Optional output linear sRGB image if requested by caller.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ToXYB(
            ImageBundle inBundle,
            ThreadPool? pool,
            Image3<float> xyb,
            JxlCmsInterface cms,
            Image3<float>? linear = null)
        {
            float intensityTarget = inBundle.Metadata != null
                ? inBundle.Metadata.IntensityTarget()
                : Common.kDefaultIntensityTarget;

            ColorTransformSource src = new ColorTransformSource(
                inBundle.CCurrent,
                intensityTarget,
                inBundle.Color(),
                inBundle.Black(),
                new Rect(inBundle.Color()));

            return ToXYB(in src, pool, xyb, cms, linear);
        }

        /// <summary>
        /// Converts a <see cref="ColorTransformSource"/> to XYB <see cref="Image3{Single}"/>.
        /// Ported from <c>jxl::ToXYB</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="src">Source color image, encoding, intensity target, optional black plane, and region.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="xyb">Destination XYB image (must already be allocated to at least <c>src.Region</c> size).</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="linear">Optional output linear sRGB image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ToXYB(
            in ColorTransformSource src,
            ThreadPool? pool,
            Image3<float> xyb,
            JxlCmsInterface cms,
            Image3<float>? linear = null)
        {
            Span<float> premulAbsorb = stackalloc float[15];
            ComputePremulAbsorb(src.IntensityTarget, premulAbsorb);

            bool isSrgb       = src.CCurrent.IsSRGB();
            bool isLinearSrgb = src.CCurrent.IsLinearSRGB();
            bool hasBlack     = src.Black != null;

            if (!hasBlack && (isSrgb || isLinearSrgb) && linear == null)
            {
                return ToXybFastPath(in src, isLinearSrgb, premulAbsorb.ToArray(), pool, xyb);
            }

            return ToXybViaLinearSrgb(in src, premulAbsorb.ToArray(), pool, xyb, cms, linear);
        }

        private static Status ToXybFastPath(
            in ColorTransformSource src,
            bool isLinear,
            float[] premulAbsorb,
            ThreadPool? pool,
            Image3<float> xyb)
        {
            bool hasRgba     = !src.CCurrent.IsGray();
            Image3<float> c  = src.Color;
            Rect rect        = src.Region;
            nuint ysize      = (nuint)rect.YSize;

            Status runStatus = DataParallel.RunOnPool(
                pool,
                0u,
                (uint)ysize,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    ConvertRowToXyb(hasRgba, isLinear, c, rect, task, premulAbsorb, xyb);

                    return Status.OkStatus;
                },
                "ToXYB");

            return runStatus;
        }

        private static Status ToXybViaLinearSrgb(
            in ColorTransformSource src,
            float[] premulAbsorb,
            ThreadPool? pool,
            Image3<float> xyb,
            JxlCmsInterface cms,
            Image3<float>? linear)
        {
            ColorEncoding cLinearSrgb  = ColorEncoding.LinearSRGB(src.CCurrent.IsGray());
            Image3<float> linearTarget = linear ?? xyb;

            if (!ImageBundle.ApplyColorTransform(in src, cLinearSrgb, cms, pool, ref linearTarget))
            {
                return false;
            }

            bool hasRgba = !src.CCurrent.IsGray();
            nuint ysize  = (nuint)src.Region.YSize;
            Rect linRect = new Rect(0, 0, src.Region.XSize, src.Region.YSize);

            return DataParallel.RunOnPool(
                pool,
                0u,
                (uint)ysize,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    ConvertRowToXyb(hasRgba, isLinear: true, linearTarget, linRect, task, premulAbsorb, xyb);

                    return Status.OkStatus;
                },
                "LinearToXYB");
        }

        /// <summary>
        /// Scales a row of XYB values into the [0, 1] modular range.
        /// Ported from <c>jxl::ScaleXYBRow</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="row0">X plane row.</param>
        /// <param name="row1">Y plane row.</param>
        /// <param name="row2">B plane row.</param>
        /// <param name="xsize">Number of pixels in the row.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ScaleXYBRow(Span<float> row0, Span<float> row1, Span<float> row2, nuint xsize)
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                int ix   = (int)x;
                row2[ix] = (row2[ix] - row1[ix] + Cms.OpsinParams.KScaledXybOffset2) * Cms.OpsinParams.KScaledXybScale2;
                row0[ix] = (row0[ix] + Cms.OpsinParams.KScaledXybOffset0) * Cms.OpsinParams.KScaledXybScale0;
                row1[ix] = (row1[ix] + Cms.OpsinParams.KScaledXybOffset1) * Cms.OpsinParams.KScaledXybScale1;
            }
        }

        /// <summary>
        /// Scales an entire 3-plane XYB image into the [0, 1] modular range in-place.
        /// Ported from <c>jxl::ScaleXYB</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="ops">XYB image to scale in-place.</param>
        public static void ScaleXYB(Image3<float> ops)
        {
            nuint xsize = ops.XSize;
            nuint ysize = ops.YSize;

            for (nuint y = 0; y < ysize; ++y)
            {
                ScaleXYBRow(ops.PlaneRow(0, y), ops.PlaneRow(1, y), ops.PlaneRow(2, y), xsize);
            }
        }

        /// <summary>
        /// Converts an RGB float image to YCbCr (BT.601 full-range) in <paramref name="yPlane"/>, <paramref name="cbPlane"/>, and <paramref name="crPlane"/>.
        /// Note that JXL's YCbCr ordering stores plane 0 = Cb, plane 1 = Y, plane 2 = Cr.
        /// Ported from <c>jxl::RgbToYcbcr</c> in <c>lib/jxl/enc_xyb.cc</c>.
        /// </summary>
        /// <param name="rPlane">Red input plane.</param>
        /// <param name="gPlane">Green input plane.</param>
        /// <param name="bPlane">Blue input plane.</param>
        /// <param name="yPlane">Luminance (Y) output plane.</param>
        /// <param name="cbPlane">Blue-difference (Cb) output plane.</param>
        /// <param name="crPlane">Red-difference (Cr) output plane.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status RgbToYcbcr(
            ImagePlane<float> rPlane,
            ImagePlane<float> gPlane,
            ImagePlane<float> bPlane,
            ImagePlane<float> yPlane,
            ImagePlane<float> cbPlane,
            ImagePlane<float> crPlane,
            ThreadPool? pool)
        {
            nuint xsize = rPlane.XSize;
            nuint ysize = rPlane.YSize;

            if (xsize == 0 || ysize == 0)
            {
                return true;
            }

            return DataParallel.RunOnPool(
                pool,
                0u,
                (uint)ysize,
                ThreadPool.NoInit,
                (y, _) =>
                {
                    RgbRowToYcbcr(
                        rPlane.ConstRow(y),
                        gPlane.ConstRow(y),
                        bPlane.ConstRow(y),
                        yPlane.Row(y),
                        cbPlane.Row(y),
                        crPlane.Row(y),
                        xsize);

                    return Status.OkStatus;
                },
                "RgbToYcbcr");
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void RgbRowToYcbcr(
            ReadOnlySpan<float> rRow,
            ReadOnlySpan<float> gRow,
            ReadOnlySpan<float> bRow,
            Span<float> yRow,
            Span<float> cbRow,
            Span<float> crRow,
            nuint xsize)
        {
            // Full-range BT.601 weights as in lib/jxl/enc_xyb.cc
            const float kKr     = 0.299f;
            const float kKb     = 0.114f;
            const float kKg     = 1.0f - kKr - kKb;
            const float kScaleB = 0.5f / (1.0f - kKb);
            const float kScaleR = 0.5f / (1.0f - kKr);

            for (nuint x = 0; x < xsize; ++x)
            {
                int ix    = (int)x;
                float r   = rRow[ix];
                float g   = gRow[ix];
                float b   = bRow[ix];
                float lum = kKr * r + kKg * g + kKb * b;

                yRow[ix]  = lum - 0.5f;
                cbRow[ix] = (b - lum) * kScaleB;
                crRow[ix] = (r - lum) * kScaleR;
            }
        }
    }

    /// <summary>
    /// Encoder-side helper facade for <see cref="ImageBundle"/> color space transformations.
    /// Ported from <c>lib/jxl/enc_image_bundle.h</c> and <c>lib/jxl/enc_image_bundle.cc</c>.
    /// </summary>
    public static class EncImageBundle
    {
        /// <summary>
        /// Applies a color space transformation from <paramref name="src"/> to <paramref name="cDesired"/>.
        /// </summary>
        /// <param name="src">Source color image, encoding, intensity target, optional black plane, and region.</param>
        /// <param name="cDesired">Target color encoding.</param>
        /// <param name="cms">CMS interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="output">Output 3-plane float image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ApplyColorTransform(
            in ColorTransformSource src,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ThreadPool? pool,
            ref Image3<float> output)
        {
            return ImageBundle.ApplyColorTransform(in src, cDesired, cms, pool, ref output);
        }

        /// <summary>
        /// Transforms <paramref name="inBundle"/> into <paramref name="store"/> if its color encoding differs from <paramref name="cDesired"/>.
        /// </summary>
        /// <param name="inBundle">Input image bundle.</param>
        /// <param name="cDesired">Desired color encoding.</param>
        /// <param name="cms">CMS interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="store">Scratch bundle used when transformation is required.</param>
        /// <param name="outBundle">Resulting bundle reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status TransformIfNeeded(
            ImageBundle inBundle,
            ColorEncoding cDesired,
            JxlCmsInterface cms,
            ThreadPool? pool,
            ImageBundle store,
            out ImageBundle outBundle)
        {
            return ImageBundle.TransformIfNeeded(inBundle, cDesired, cms, pool, store, out outBundle);
        }
    }

    /// <summary>
    /// Parameters for external pixel buffer conversion into <see cref="ImagePlane{Single}"/> or <see cref="ImageBundle"/>.
    /// </summary>
    public readonly struct ExternalBufferSpec
    {
        /// <summary>Width in pixels.</summary>
        public readonly nuint XSize;

        /// <summary>Height in pixels.</summary>
        public readonly nuint YSize;

        /// <summary>Row stride in bytes (if 0, tightly packed stride is computed).</summary>
        public readonly nuint RowStride;

        /// <summary>Bits per sample (8, 16, or 32; or custom 1..32 for integer conversion).</summary>
        public readonly nuint BitsPerSample;

        /// <summary>Pixel data format (endianness, data type, num channels, align).</summary>
        public readonly JxlPixelFormat PixelFormat;

        /// <summary>Channel index within interleaved pixel to extract (0..NumChannels-1).</summary>
        public readonly nuint ChannelIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalBufferSpec"/> struct.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="rowStride">Row stride in bytes (0 = packed).</param>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="pixelFormat">Pixel format.</param>
        /// <param name="channelIndex">Interleaved channel index.</param>
        public ExternalBufferSpec(
            nuint xsize,
            nuint ysize,
            nuint rowStride,
            nuint bitsPerSample,
            JxlPixelFormat pixelFormat,
            nuint channelIndex = 0)
        {
            XSize         = xsize;
            YSize         = ysize;
            RowStride     = rowStride;
            BitsPerSample = bitsPerSample;
            PixelFormat   = pixelFormat;
            ChannelIndex  = channelIndex;
        }
    }

    /// <summary>
    /// Converts external interleaved byte buffers (uint8, uint16, float16, float32) into internal
    /// float planes and <see cref="ImageBundle"/> instances.
    /// Ported from <c>lib/jxl/enc_external_image.h</c> and <c>lib/jxl/enc_external_image.cc</c>.
    /// </summary>
    public static class EncExternalImage
    {
        /// <summary>
        /// Converts a single channel from an external interleaved buffer into <paramref name="outPlane"/> without size checks.
        /// Ported from <c>jxl::ConvertFromExternalNoSizeCheck</c> in <c>lib/jxl/enc_external_image.cc</c>.
        /// </summary>
        /// <param name="bytes">Source external pixel buffer.</param>
        /// <param name="spec">External buffer dimensions, stride, bit depth, pixel format, and channel index.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outPlane">Destination float plane (must already be allocated to <c>spec.XSize</c> x <c>spec.YSize</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ConvertFromExternalNoSizeCheck(
            ReadOnlySpan<byte> bytes,
            in ExternalBufferSpec spec,
            ThreadPool? pool,
            ImagePlane<float> outPlane)
        {
            if (spec.XSize == 0 || spec.YSize == 0)
            {
                return Status.Failure("Empty image dimensions");
            }

            if (spec.ChannelIndex >= spec.PixelFormat.NumChannels)
            {
                return Status.Failure("Channel index out of range");
            }

            nuint bytesPerSample = GetBytesPerSample(spec.PixelFormat.DataType, spec.BitsPerSample);

            if (bytesPerSample == 0)
            {
                return Status.Failure("Unsupported data type or bits_per_sample");
            }

            nuint bytesPerPixel = spec.PixelFormat.NumChannels * bytesPerSample;
            nuint rowStride     = ComputeRowStride(spec.XSize, bytesPerPixel, spec.RowStride, spec.PixelFormat.Align);

            for (nuint y = 0; y < spec.YSize; ++y)
            {
                int rowOffset             = (int)((y * rowStride) + (spec.ChannelIndex * bytesPerSample));
                ReadOnlySpan<byte> srcRow = bytes.Slice(rowOffset);
                Span<float> dstRow        = outPlane.Row(y);

                ConvertExternalRow(srcRow, spec.XSize, bytesPerPixel, in spec, dstRow);
            }

            return true;
        }

        /// <summary>
        /// Converts a single channel from an external interleaved buffer into <paramref name="outPlane"/> with buffer size validation.
        /// Ported from <c>jxl::ConvertFromExternal</c> in <c>lib/jxl/enc_external_image.cc</c>.
        /// </summary>
        /// <param name="bytes">Source external pixel buffer.</param>
        /// <param name="spec">External buffer dimensions, stride, bit depth, pixel format, and channel index.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outPlane">Destination float plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ConvertFromExternal(
            ReadOnlySpan<byte> bytes,
            in ExternalBufferSpec spec,
            ThreadPool? pool,
            ImagePlane<float> outPlane)
        {
            if (!ValidateBufferSize(bytes.Length, in spec))
            {
                return Status.Failure("External buffer is smaller than required size");
            }

            return ConvertFromExternalNoSizeCheck(bytes, in spec, pool, outPlane);
        }

        /// <summary>
        /// Allocates and populates an <see cref="ImagePlane{Single}"/> from a single-channel external buffer.
        /// Ported from <c>jxl::BufferToImageF</c> in <c>lib/jxl/enc_external_image.cc</c>.
        /// </summary>
        /// <param name="pixelFormat">Pixel format (must have <c>NumChannels == 1</c>).</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="bytes">Source byte buffer.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outPlane">Allocated and populated float plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status BufferToImageF(
            JxlPixelFormat pixelFormat,
            nuint xsize,
            nuint ysize,
            ReadOnlySpan<byte> bytes,
            ThreadPool? pool,
            out ImagePlane<float>? outPlane)
        {
            outPlane = null;

            if (pixelFormat.NumChannels != 1)
            {
                return Status.Failure("BufferToImageF requires NumChannels == 1");
            }

            nuint bitsPerSample     = GetNominalBitsPerSample(pixelFormat.DataType);
            ExternalBufferSpec spec = new ExternalBufferSpec(xsize, ysize, 0, bitsPerSample, pixelFormat, 0);

            if (!ValidateBufferSize(bytes.Length, in spec))
            {
                return Status.Failure("External buffer is smaller than required size");
            }

            StatusOr<ImagePlane<float>> planeOr = ImagePlane<float>.Create(JxlMemoryManager.Default, xsize, ysize);

            if (!planeOr.IsOk)
            {
                return planeOr.Status;
            }

            outPlane = planeOr.Value;

            return ConvertFromExternalNoSizeCheck(bytes, in spec, pool, outPlane);
        }

        /// <summary>
        /// Converts an interleaved color (and optional alpha) external buffer into an <see cref="ImageBundle"/>.
        /// Ported from <c>jxl::BufferToImageBundle</c> in <c>lib/jxl/enc_external_image.cc</c>.
        /// </summary>
        /// <param name="pixelFormat">Pixel format (1, 2, 3, or 4 channels).</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="bytes">Source interleaved pixel buffer.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="ib">Destination <see cref="ImageBundle"/> (must have associated <see cref="ImageMetadata"/>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status BufferToImageBundle(
            JxlPixelFormat pixelFormat,
            nuint xsize,
            nuint ysize,
            ReadOnlySpan<byte> bytes,
            ThreadPool? pool,
            ImageBundle ib)
        {
            if (ib.Metadata == null)
            {
                return Status.Failure("ImageBundle metadata must be non-null");
            }

            uint numChannels  = pixelFormat.NumChannels;
            bool isGray       = ib.Metadata.ColorEncoding.IsGray();
            nuint colorChans  = isGray ? 1u : 3u;
            bool hasAlphaChan = numChannels == 2 || numChannels == 4;

            if (numChannels < colorChans || numChannels > 4 || (isGray && numChannels == 3) || (!isGray && numChannels == 2))
            {
                return Status.Failure("Incompatible NumChannels for ImageBundle metadata");
            }

            nuint bitsPerSample          = GetEffectiveBitsPerSample(pixelFormat.DataType, ib.Metadata.BitDepth.BitsPerSample);
            ExternalBufferSpec checkSpec = new ExternalBufferSpec(xsize, ysize, 0, bitsPerSample, pixelFormat, 0);

            if (!ValidateBufferSize(bytes.Length, in checkSpec))
            {
                return Status.Failure("External buffer is smaller than required size");
            }

            if (!PopulateBundleColor(pixelFormat, xsize, ysize, bitsPerSample, isGray, bytes, (pool, ib)))
            {
                return false;
            }

            if (hasAlphaChan)
            {
                return PopulateBundleAlpha(pixelFormat, xsize, ysize, bitsPerSample, colorChans, bytes, (pool, ib));
            }

            return true;
        }

        private static Status PopulateBundleColor(
            JxlPixelFormat pixelFormat,
            nuint xsize,
            nuint ysize,
            nuint bitsPerSample,
            bool isGray,
            ReadOnlySpan<byte> bytes,
            (ThreadPool? Pool, ImageBundle Ib) ctx)
        {
            JxlMemoryManager mm             = ctx.Ib.MemoryManager ?? JxlMemoryManager.Default;
            StatusOr<Image3<float>> colorOr = Image3<float>.Create(mm, xsize, ysize);

            if (!colorOr.IsOk)
            {
                return colorOr.Status;
            }

            Image3<float> color = colorOr.Value;

            if (isGray)
            {
                ExternalBufferSpec s0 = new ExternalBufferSpec(xsize, ysize, 0, bitsPerSample, pixelFormat, 0);

                if (!ConvertFromExternalNoSizeCheck(bytes, in s0, ctx.Pool, color.Plane(0)) ||
                    !ImageOps.CopyImageTo(color.Plane(0), color.Plane(1)) ||
                    !ImageOps.CopyImageTo(color.Plane(0), color.Plane(2)))
                {
                    color.Dispose();

                    return false;
                }
            }
            else
            {
                for (nuint c = 0; c < 3; ++c)
                {
                    ExternalBufferSpec sc = new ExternalBufferSpec(xsize, ysize, 0, bitsPerSample, pixelFormat, c);

                    if (!ConvertFromExternalNoSizeCheck(bytes, in sc, ctx.Pool, color.Plane(c)))
                    {
                        color.Dispose();

                        return false;
                    }
                }
            }

            return ctx.Ib.SetFromImage(color, ctx.Ib.Metadata!.ColorEncoding);
        }

        private static Status PopulateBundleAlpha(
            JxlPixelFormat pixelFormat,
            nuint xsize,
            nuint ysize,
            nuint bitsPerSample,
            nuint alphaChanIdx,
            ReadOnlySpan<byte> bytes,
            (ThreadPool? Pool, ImageBundle Ib) ctx)
        {
            ExtraChannelInfo? alphaInfo = ctx.Ib.Metadata!.Find(ExtraChannel.Alpha);

            if (alphaInfo == null)
            {
                return Status.Failure("Metadata does not define an alpha extra channel");
            }

            nuint alphaBits                     = GetEffectiveBitsPerSample(pixelFormat.DataType, alphaInfo.BitDepth.BitsPerSample);
            JxlMemoryManager mm                 = ctx.Ib.MemoryManager ?? JxlMemoryManager.Default;
            StatusOr<ImagePlane<float>> alphaOr = ImagePlane<float>.Create(mm, xsize, ysize);

            if (!alphaOr.IsOk)
            {
                return alphaOr.Status;
            }

            ImagePlane<float> alphaPlane = alphaOr.Value;
            ExternalBufferSpec sa        = new ExternalBufferSpec(xsize, ysize, 0, alphaBits, pixelFormat, alphaChanIdx);

            if (!ConvertFromExternalNoSizeCheck(bytes, in sa, ctx.Pool, alphaPlane))
            {
                alphaPlane.Dispose();

                return false;
            }

            if (ctx.Ib.ExtraChannels.Count == 0)
            {
                return ctx.Ib.SetAlpha(alphaPlane);
            }

            List<ImagePlane<float>> ecs = new List<ImagePlane<float>>(ctx.Ib.ExtraChannels) { alphaPlane };

            return ctx.Ib.SetExtraChannels(ecs);
        }

        private static bool ValidateBufferSize(int bufferLength, in ExternalBufferSpec spec)
        {
            if (spec.XSize == 0 || spec.YSize == 0 || spec.ChannelIndex >= spec.PixelFormat.NumChannels)
            {
                return false;
            }

            nuint bytesPerSample = GetBytesPerSample(spec.PixelFormat.DataType, spec.BitsPerSample);

            if (bytesPerSample == 0)
            {
                return false;
            }

            nuint bytesPerPixel = spec.PixelFormat.NumChannels * bytesPerSample;
            nuint rowSize       = spec.XSize * bytesPerPixel;
            nuint rowStride     = ComputeRowStride(spec.XSize, bytesPerPixel, spec.RowStride, spec.PixelFormat.Align);

            if (rowStride < rowSize)
            {
                return false;
            }

            nuint requiredBytes = (spec.YSize - 1) * rowStride + rowSize;

            return (nuint)bufferLength >= requiredBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint ComputeRowStride(nuint xsize, nuint bytesPerPixel, nuint rowStride, nuint align)
        {
            if (rowStride != 0)
            {
                return rowStride;
            }

            nuint rowSize = xsize * bytesPerPixel;

            if (align <= 1)
            {
                return rowSize;
            }

            return ((rowSize + align - 1) / align) * align;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint GetBytesPerSample(JxlDataType dataType, nuint bitsPerSample)
        {
            return dataType switch
            {
                JxlDataType.UInt8   => bitsPerSample > 0 && bitsPerSample <= 8 ? 1u : 0u,
                JxlDataType.UInt16  => bitsPerSample > 8 && bitsPerSample <= 16 ? 2u : 0u,
                JxlDataType.Float16 => bitsPerSample == 16 ? 2u : 0u,
                JxlDataType.Float   => bitsPerSample == 32 ? 4u : 0u,
                _                   => 0u,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint GetNominalBitsPerSample(JxlDataType dataType)
        {
            return dataType switch
            {
                JxlDataType.UInt8   => 8u,
                JxlDataType.UInt16  => 16u,
                JxlDataType.Float16 => 16u,
                JxlDataType.Float   => 32u,
                _                   => 0u,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint GetEffectiveBitsPerSample(JxlDataType dataType, uint metadataBits)
        {
            if (dataType == JxlDataType.Float)
            {
                return 32u;
            }

            if (dataType == JxlDataType.Float16)
            {
                return 16u;
            }

            if (dataType == JxlDataType.UInt8)
            {
                return metadataBits > 0 && metadataBits <= 8 ? metadataBits : 8u;
            }

            if (dataType == JxlDataType.UInt16)
            {
                return metadataBits > 8 && metadataBits <= 16 ? metadataBits : 16u;
            }

            return metadataBits;
        }

        private static void ConvertExternalRow(
            ReadOnlySpan<byte> srcRow,
            nuint xsize,
            nuint bytesPerPixel,
            in ExternalBufferSpec spec,
            Span<float> dstRow)
        {
            bool littleEndian = IsLittleEndian(spec.PixelFormat.Endianness);
            float invMax      = spec.BitsPerSample < 32
                ? (float)(1.0 / ((1UL << (int)spec.BitsPerSample) - 1UL))
                : (float)(1.0 / uint.MaxValue);

            switch (spec.PixelFormat.DataType)
            {
                case JxlDataType.UInt8:
                    ConvertRowUint8(srcRow, xsize, bytesPerPixel, invMax, dstRow);
                    break;

                case JxlDataType.UInt16:
                    ConvertRowUint16(srcRow, xsize, bytesPerPixel, littleEndian, invMax, dstRow);
                    break;

                case JxlDataType.Float16:
                    ConvertRowFloat16(srcRow, xsize, bytesPerPixel, littleEndian, dstRow);
                    break;

                case JxlDataType.Float:
                    ConvertRowFloat32(srcRow, xsize, bytesPerPixel, littleEndian, dstRow);
                    break;
            }
        }

        private static void ConvertRowUint8(
            ReadOnlySpan<byte> srcRow,
            nuint xsize,
            nuint step,
            float mul,
            Span<float> dstRow)
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                dstRow[(int)x] = srcRow[(int)(x * step)] * mul;
            }
        }

        private static void ConvertRowUint16(
            ReadOnlySpan<byte> srcRow,
            nuint xsize,
            nuint step,
            bool littleEndian,
            float mul,
            Span<float> dstRow)
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                ReadOnlySpan<byte> p = srcRow.Slice((int)(x * step), 2);
                ushort raw           = littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(p)
                    : BinaryPrimitives.ReadUInt16BigEndian(p);
                dstRow[(int)x]       = raw * mul;
            }
        }

        private static void ConvertRowFloat16(
            ReadOnlySpan<byte> srcRow,
            nuint xsize,
            nuint step,
            bool littleEndian,
            Span<float> dstRow)
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                ReadOnlySpan<byte> p = srcRow.Slice((int)(x * step), 2);
                ushort raw           = littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(p)
                    : BinaryPrimitives.ReadUInt16BigEndian(p);
                dstRow[(int)x]       = (float)BitConverter.UInt16BitsToHalf(raw);
            }
        }

        private static void ConvertRowFloat32(
            ReadOnlySpan<byte> srcRow,
            nuint xsize,
            nuint step,
            bool littleEndian,
            Span<float> dstRow)
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                ReadOnlySpan<byte> p = srcRow.Slice((int)(x * step), 4);
                uint raw             = littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(p)
                    : BinaryPrimitives.ReadUInt32BigEndian(p);
                dstRow[(int)x]       = BitConverter.UInt32BitsToSingle(raw);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLittleEndian(JxlEndianness endianness)
        {
            if (endianness == JxlEndianness.BigEndian)
            {
                return false;
            }

            if (endianness == JxlEndianness.LittleEndian)
            {
                return true;
            }

            return BitConverter.IsLittleEndian;
        }
    }

    /// <summary>
    /// Diagnostic image dumping utilities controlled by <see cref="CompressParams.DebugImage"/>.
    /// Ported from <c>lib/jxl/enc_debug_image.h</c> and <c>lib/jxl/enc_debug_image.cc</c>.
    /// </summary>
    public static class EncDebugImage
    {
        /// <summary>
        /// Returns whether debug image output is enabled on <paramref name="cparams"/>.
        /// Ported from <c>jxl::WantDebugOutput</c> in <c>lib/jxl/enc_debug_image.h</c>.
        /// </summary>
        /// <param name="cparams">Compression parameters.</param>
        /// <returns><c>true</c> if a debug image callback is registered.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WantDebugOutput(CompressParams cparams)
        {
            return cparams.DebugImage != null;
        }

        /// <summary>
        /// Dumps an sRGB <see cref="Image3{Single}"/> or integer image to the debug callback on <paramref name="cparams"/>.
        /// Ported from <c>jxl::DumpImage</c> in <c>lib/jxl/enc_debug_image.cc</c>.
        /// </summary>
        /// <param name="cparams">Compression parameters containing the callback.</param>
        /// <param name="label">Diagnostic image label.</param>
        /// <param name="image">3-plane float image in [0, 255] or [0, 1] scale.</param>
        public static void DumpImage(CompressParams cparams, string label, Image3<float> image)
        {
            if (cparams.DebugImage == null || image.XSize == 0 || image.YSize == 0)
            {
                return;
            }

            nuint xsize     = image.XSize;
            nuint ysize     = image.YSize;
            ushort[] pixels = new ushort[xsize * ysize * 3];

            for (nuint y = 0; y < ysize; ++y)
            {
                ReadOnlySpan<float> r0 = image.ConstPlaneRow(0, y);
                ReadOnlySpan<float> r1 = image.ConstPlaneRow(1, y);
                ReadOnlySpan<float> r2 = image.ConstPlaneRow(2, y);

                for (nuint x = 0; x < xsize; ++x)
                {
                    int ix          = (int)x;
                    nuint idx       = (y * xsize + x) * 3;
                    pixels[idx]     = QuantizeU16(r0[ix]);
                    pixels[idx + 1] = QuantizeU16(r1[ix]);
                    pixels[idx + 2] = QuantizeU16(r2[ix]);
                }
            }

            cparams.DebugImage(cparams.DebugImageOpaque, label, xsize, ysize, ColorEncoding.SRGB().ToExternal(), pixels);
        }

        /// <summary>
        /// Dumps a single normalized plane to the debug callback on <paramref name="cparams"/>.
        /// Ported from <c>jxl::DumpPlaneNormalized</c> in <c>lib/jxl/enc_debug_image.cc</c>.
        /// </summary>
        /// <typeparam name="T">Plane element type (<see cref="float"/>, <see cref="byte"/>, <see cref="int"/>, <see cref="short"/>).</typeparam>
        /// <param name="cparams">Compression parameters.</param>
        /// <param name="label">Diagnostic image label.</param>
        /// <param name="plane">Plane to normalize and dump.</param>
        public static void DumpPlaneNormalized<T>(CompressParams cparams, string label, ImagePlane<T> plane)
            where T : unmanaged
        {
            if (cparams.DebugImage == null || plane.XSize == 0 || plane.YSize == 0)
            {
                return;
            }

            nuint xsize = plane.XSize;
            nuint ysize = plane.YSize;
            FindMinMax(plane, out double minVal, out double maxVal);
            double range    = maxVal > minVal ? 65535.0 / (maxVal - minVal) : 0.0;
            ushort[] pixels = new ushort[xsize * ysize];

            for (nuint y = 0; y < ysize; ++y)
            {
                ReadOnlySpan<T> row = plane.ConstRow(y);

                for (nuint x = 0; x < xsize; ++x)
                {
                    double v              = Convert.ToDouble(row[(int)x]);
                    pixels[y * xsize + x] = (ushort)Math.Clamp(Math.Round((v - minVal) * range), 0.0, 65535.0);
                }
            }

            cparams.DebugImage(
                cparams.DebugImageOpaque,
                label,
                xsize,
                ysize,
                ColorEncoding.SRGB(isGray: true).ToExternal(),
                pixels);
        }

        private static void FindMinMax<T>(ImagePlane<T> plane, out double minVal, out double maxVal)
            where T : unmanaged
        {
            minVal = double.PositiveInfinity;
            maxVal = double.NegativeInfinity;

            for (nuint y = 0; y < plane.YSize; ++y)
            {
                ReadOnlySpan<T> row = plane.ConstRow(y);

                for (nuint x = 0; x < plane.XSize; ++x)
                {
                    double v = Convert.ToDouble(row[(int)x]);

                    if (v < minVal)
                    {
                        minVal = v;
                    }

                    if (v > maxVal)
                    {
                        maxVal = v;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort QuantizeU16(float v)
        {
            float scaled = v <= 1.0f ? v * 65535.0f : (v / 255.0f) * 65535.0f;

            return (ushort)Math.Clamp(MathF.Round(scaled), 0.0f, 65535.0f);
        }
    }
}
