// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Wrap mode that mirrors out-of-bounds coordinates (repeating the edge pixel once).
    /// Useful for convolutions.
    /// </summary>
    public readonly struct WrapMirror
    {
        /// <summary>
        /// Wraps <paramref name="coord"/> into <c>[0, size)</c> by mirroring.
        /// </summary>
        /// <param name="coord">Coordinate to wrap.</param>
        /// <param name="size">Dimension size.</param>
        /// <returns>Mirrored coordinate in <c>[0, size)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Invoke(long coord, long size)
        {
            return ImageOps.Mirror(coord, size);
        }
    }

    /// <summary>
    /// Wrap mode that returns the same coordinate unchanged.
    /// </summary>
    public readonly struct WrapUnchanged
    {
        /// <summary>
        /// Returns <paramref name="coord"/> unchanged.
        /// </summary>
        /// <param name="coord">Coordinate.</param>
        /// <param name="size">Dimension size (unused).</param>
        /// <returns><paramref name="coord"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Invoke(long coord, long size)
        {
            return coord;
        }
    }

    /// <summary>
    /// Operations on images (ported from <c>lib/jxl/image_ops.h</c> and <c>lib/jxl/image_ops.cc</c>).
    /// </summary>
    public static class ImageOps
    {
        /// <summary>
        /// Standard DCT block dimension (8x8).
        /// </summary>
        public const nuint BlockDim = 8;

        /// <summary>
        /// Returns whether two image/plane objects have the same width and height.
        /// </summary>
        /// <param name="image1">First image.</param>
        /// <param name="image2">Second image.</param>
        /// <returns><c>true</c> if dimensions match; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameSize(IImageDimensions image1, IImageDimensions image2)
        {
            return image1.XSize == image2.XSize && image1.YSize == image2.YSize;
        }

        /// <summary>
        /// Returns whether two rectangles have the same width and height.
        /// </summary>
        /// <param name="rect1">First rectangle.</param>
        /// <param name="rect2">Second rectangle.</param>
        /// <returns><c>true</c> if dimensions match; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameSize(Rect rect1, Rect rect2)
        {
            return rect1.XSize == rect2.XSize && rect1.YSize == rect2.YSize;
        }

        /// <summary>
        /// Copies all valid pixels from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="from">Source plane.</param>
        /// <param name="to">Destination plane of the same size.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status CopyImageTo<T>(ImagePlane<T> from, ImagePlane<T> to)
            where T : unmanaged
        {
            if (!SameSize(from, to))
            {
                return Status.Failure("Source and destination planes have different sizes");
            }

            if (from.YSize == 0 || from.XSize == 0)
            {
                return true;
            }

            int width = (int)from.XSize;

            for (nuint y = 0; y < from.YSize; ++y)
            {
                from.ConstRow(y).Slice(0, width).CopyTo(to.Row(y).Slice(0, width));
            }

            return true;
        }

        /// <summary>
        /// Copies <c>from:rectFrom</c> to <c>to:rectTo</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="rectFrom">Source region.</param>
        /// <param name="from">Source plane.</param>
        /// <param name="rectTo">Destination region.</param>
        /// <param name="to">Destination plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status CopyImageTo<T>(
            Rect rectFrom,
            ImagePlane<T> from,
            Rect rectTo,
            ImagePlane<T> to)
            where T : unmanaged
        {
            if (!SameSize(rectFrom, rectTo) || !rectFrom.IsInside(from) || !rectTo.IsInside(to))
            {
                return Status.Failure("Invalid rectangles for CopyImageTo");
            }

            if (rectFrom.XSize == 0)
            {
                return true;
            }

            int width    = rectFrom.XSize;
            nuint height = (nuint)rectFrom.YSize;

            for (nuint y = 0; y < height; ++y)
            {
                rectFrom.ConstRow(from, y).Slice(0, width).CopyTo(rectTo.Row(to, y).Slice(0, width));
            }

            return true;
        }

        /// <summary>
        /// Copies <c>from:rectFrom</c> to <c>to:rectTo</c> across all 3 planes.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="rectFrom">Source region.</param>
        /// <param name="from">Source 3-plane image.</param>
        /// <param name="rectTo">Destination region.</param>
        /// <param name="to">Destination 3-plane image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status CopyImageTo<T>(
            Rect rectFrom,
            Image3<T> from,
            Rect rectTo,
            Image3<T> to)
            where T : unmanaged
        {
            if (!SameSize(rectFrom, rectTo))
            {
                return Status.Failure("Rectangles have different sizes");
            }

            for (nuint c = 0; c < 3; c++)
            {
                Status status = CopyImageTo(rectFrom, from.Plane(c), rectTo, to.Plane(c));

                if (!status)
                {
                    return status;
                }
            }

            return true;
        }

        /// <summary>
        /// Copies all 3 planes of <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="from">Source 3-plane image.</param>
        /// <param name="to">Destination 3-plane image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status CopyImageTo<T>(Image3<T> from, Image3<T> to)
            where T : unmanaged
        {
            return CopyImageTo(new Rect(from), from, new Rect(to), to);
        }

        /// <summary>
        /// Copies <c>from:fromRect</c> to <c>to:toRect</c>; also copies <paramref name="padding"/> pixels of
        /// border around <c>from:fromRect</c>, in all directions, whenever they are inside the first image.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="fromRect">Source region.</param>
        /// <param name="from">Source plane.</param>
        /// <param name="padding">Padding in pixels.</param>
        /// <param name="toRect">Destination region.</param>
        /// <param name="to">Destination plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status CopyImageToWithPadding<T>(
            Rect fromRect,
            ImagePlane<T> from,
            nuint padding,
            Rect toRect,
            ImagePlane<T> to)
            where T : unmanaged
        {
            int pad     = (int)padding;
            int xextra0 = Math.Min(pad, fromRect.X0);
            int xextra1 = Math.Min(pad, (int)from.XSize - fromRect.X0 - fromRect.XSize);
            int yextra0 = Math.Min(pad, fromRect.Y0);
            int yextra1 = Math.Min(pad, (int)from.YSize - fromRect.Y0 - fromRect.YSize);

            if (toRect.X0 < xextra0 || toRect.Y0 < yextra0)
            {
                return Status.Failure("Destination rectangle too close to border for padding");
            }

            Rect expandedFrom = new Rect(
                fromRect.X0 - xextra0,
                fromRect.Y0 - yextra0,
                fromRect.XSize + xextra0 + xextra1,
                fromRect.YSize + yextra0 + yextra1);

            Rect expandedTo = new Rect(
                toRect.X0 - xextra0,
                toRect.Y0 - yextra0,
                toRect.XSize + xextra0 + xextra1,
                toRect.YSize + yextra0 + yextra1);

            return CopyImageTo(expandedFrom, from, expandedTo, to);
        }

        /// <summary>
        /// Returns linear combination of two single-channel planes: <c>lambda1 * image1 + lambda2 * image2</c>.
        /// </summary>
        /// <typeparam name="T">Numeric sample type.</typeparam>
        /// <param name="lambda1">First weight.</param>
        /// <param name="image1">First plane.</param>
        /// <param name="lambda2">Second weight.</param>
        /// <param name="image2">Second plane.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the combined plane.</returns>
        public static StatusOr<ImagePlane<T>> LinComb<T>(
            T lambda1,
            ImagePlane<T> image1,
            T lambda2,
            ImagePlane<T> image2)
            where T : unmanaged, INumber<T>
        {
            nuint xsize = image1.XSize;
            nuint ysize = image1.YSize;

            if (xsize != image2.XSize || ysize != image2.YSize)
            {
                return Status.Failure("Image dimensions do not match");
            }

            StatusOr<ImagePlane<T>> outResult = ImagePlane<T>.Create(image1.MemoryManager, xsize, ysize);

            if (!outResult.IsOk)
            {
                return outResult.Status;
            }

            ImagePlane<T> output = outResult.Value;
            int width            = (int)xsize;

            for (nuint y = 0; y < ysize; ++y)
            {
                ReadOnlySpan<T> row1 = image1.ConstRow(y);
                ReadOnlySpan<T> row2 = image2.ConstRow(y);
                Span<T> rowOut       = output.Row(y);

                for (int x = 0; x < width; ++x)
                {
                    rowOut[x] = (lambda1 * row1[x]) + (lambda2 * row2[x]);
                }
            }

            return output;
        }

        /// <summary>
        /// Multiplies <paramref name="image"/> by <paramref name="lambda"/> in-place.
        /// </summary>
        /// <typeparam name="T">Numeric sample type.</typeparam>
        /// <param name="lambda">Scale factor.</param>
        /// <param name="image">Plane to scale in-place.</param>
        public static void ScaleImage<T>(T lambda, ImagePlane<T> image)
            where T : unmanaged, INumber<T>
        {
            int width = (int)image.XSize;

            if (width == 0)
            {
                return;
            }

            for (nuint y = 0; y < image.YSize; ++y)
            {
                Span<T> row = image.RowSpan(y);

                if (typeof(T) == typeof(float))
                {
                    Span<float> fRow = MemoryMarshal.Cast<T, float>(row);
                    float fLambda    = Unsafe.As<T, float>(ref lambda);
                    TensorPrimitives.Multiply(fRow, fLambda, fRow);
                }
                else
                {
                    for (int x = 0; x < width; ++x)
                    {
                        row[x] = lambda * row[x];
                    }
                }
            }
        }

        /// <summary>
        /// Multiplies all three planes of <paramref name="image"/> by <paramref name="lambda"/> in-place.
        /// </summary>
        /// <typeparam name="T">Numeric sample type.</typeparam>
        /// <param name="lambda">Scale factor.</param>
        /// <param name="image">3-plane image to scale in-place.</param>
        public static void ScaleImage<T>(T lambda, Image3<T> image)
            where T : unmanaged, INumber<T>
        {
            for (nuint c = 0; c < 3; ++c)
            {
                ScaleImage(lambda, image.Plane(c));
            }
        }

        /// <summary>
        /// Fills all valid pixels of <paramref name="image"/> with <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="value">Fill value.</param>
        /// <param name="image">Target plane.</param>
        public static void FillImage<T>(T value, ImagePlane<T> image)
            where T : unmanaged
        {
            for (nuint y = 0; y < image.YSize; ++y)
            {
                image.RowSpan(y).Fill(value);
            }
        }

        /// <summary>
        /// Initializes all planes of <paramref name="image"/> to the same <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="value">Fill value.</param>
        /// <param name="image">Target 3-plane image.</param>
        public static void FillImage<T>(T value, Image3<T> image)
            where T : unmanaged
        {
            for (nuint c = 0; c < 3; ++c)
            {
                for (nuint y = 0; y < image.YSize; ++y)
                {
                    image.PlaneRowSpan(c, y).Fill(value);
                }
            }
        }

        /// <summary>
        /// Fills the rectangular region <paramref name="rect"/> of <paramref name="image"/> with <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="value">Fill value.</param>
        /// <param name="image">Target plane.</param>
        /// <param name="rect">Target rectangle.</param>
        public static void FillPlane<T>(T value, ImagePlane<T> image, Rect rect)
            where T : unmanaged
        {
            nuint height = (nuint)rect.YSize;
            int width    = rect.XSize;

            if (width <= 0)
            {
                return;
            }

            for (nuint y = 0; y < height; ++y)
            {
                rect.Row(image, y).Slice(0, width).Fill(value);
            }
        }

        /// <summary>
        /// Zero-fills all valid pixels of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="image">Target plane.</param>
        public static void ZeroFillImage<T>(ImagePlane<T> image)
            where T : unmanaged
        {
            if (image.XSize == 0)
            {
                return;
            }

            for (nuint y = 0; y < image.YSize; ++y)
            {
                image.RowSpan(y).Clear();
            }
        }

        /// <summary>
        /// Zero-fills all valid pixels of all 3 planes in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged sample type.</typeparam>
        /// <param name="image">Target 3-plane image.</param>
        public static void ZeroFillImage<T>(Image3<T> image)
            where T : unmanaged
        {
            if (image.XSize == 0)
            {
                return;
            }

            for (nuint c = 0; c < 3; ++c)
            {
                for (nuint y = 0; y < image.YSize; ++y)
                {
                    image.PlaneRowSpan(c, y).Clear();
                }
            }
        }

        /// <summary>
        /// Mirrors out of bounds coordinates and returns valid coordinates unchanged.
        /// The mirror is outside the last column (border pixel is also replicated).
        /// </summary>
        /// <param name="x">Coordinate to mirror.</param>
        /// <param name="xsize">Dimension bound (must be positive).</param>
        /// <returns>Mirrored coordinate in <c>[0, xsize)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Mirror(long x, long xsize)
        {
            while (x < 0 || x >= xsize)
            {
                if (x < 0)
                {
                    x = -x - 1;
                }
                else
                {
                    x = (2 * xsize) - 1 - x;
                }
            }

            return x;
        }

        /// <summary>
        /// Computes the minimum and maximum pixel value in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Comparable numeric type with min/max bounds.</typeparam>
        /// <param name="image">Input plane.</param>
        /// <param name="min">Outputs minimum value.</param>
        /// <param name="max">Outputs maximum value.</param>
        public static void ImageMinMax<T>(ImagePlane<T> image, out T min, out T max)
            where T : unmanaged, IComparable<T>, IMinMaxValue<T>
        {
            min = T.MaxValue;
            max = T.MinValue;

            int width = (int)image.XSize;

            if (width == 0)
            {
                return;
            }

            for (nuint y = 0; y < image.YSize; ++y)
            {
                ReadOnlySpan<T> row = image.ConstRowSpan(y);

                for (int x = 0; x < width; ++x)
                {
                    T val = row[x];

                    if (val.CompareTo(min) < 0)
                    {
                        min = val;
                    }

                    if (val.CompareTo(max) > 0)
                    {
                        max = val;
                    }
                }
            }
        }

        /// <summary>
        /// Pads <paramref name="input"/> in-place to a multiple of <paramref name="blockDim"/> by edge replication.
        /// Assumes that the <paramref name="input"/> image was originally allocated large enough.
        /// </summary>
        /// <param name="input">Image to pad in-place.</param>
        /// <param name="blockDim">Block dimension multiple (default 8).</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status PadImageToBlockMultipleInPlace(Image3<float> input, nuint blockDim = BlockDim)
        {
            nuint xsizeOrig = input.XSize;
            nuint ysizeOrig = input.YSize;
            nuint xsize     = Common.RoundUpTo(xsizeOrig, blockDim);
            nuint ysize     = Common.RoundUpTo(ysizeOrig, blockDim);

            // Expands image size to the originally-allocated size.
            Status status = input.ShrinkTo(xsize, ysize);

            if (!status)
            {
                return status;
            }

            for (nuint c = 0; c < 3; c++)
            {
                PadPlaneToBlockMultiple(input.Plane(c), xsizeOrig, ysizeOrig, xsize, ysize);
            }

            return true;
        }

        /// <summary>
        /// Downsamples a single-channel float image by the given <paramref name="factor"/>.
        /// </summary>
        /// <param name="image">Input plane.</param>
        /// <param name="factor">Downsampling factor (must not be 1).</param>
        /// <returns><see cref="StatusOr{T}"/> containing the downsampled plane.</returns>
        public static StatusOr<ImagePlane<float>> DownsampleImage(ImagePlane<float> image, nuint factor)
        {
            if (factor == 0 || factor == 1)
            {
                return Status.Failure("Invalid downsample factor");
            }

            // Allocate extra space to avoid a reallocation when padding.
            JxlMemoryManager? memoryManager       = image.MemoryManager;
            StatusOr<ImagePlane<float>> outResult = ImagePlane<float>.Create(
                memoryManager,
                Common.DivCeil(image.XSize, factor) + BlockDim,
                Common.DivCeil(image.YSize, factor) + BlockDim);

            if (!outResult.IsOk)
            {
                return outResult.Status;
            }

            ImagePlane<float> downsampled = outResult.Value;
            Status status                 = DoDownsampleImage(image, factor, downsampled);

            if (!status)
            {
                downsampled.Dispose();

                return status;
            }

            return downsampled;
        }

        /// <summary>
        /// Downsamples a 3-channel float image by the given <paramref name="factor"/>.
        /// </summary>
        /// <param name="opsin">Input 3-plane image.</param>
        /// <param name="factor">Downsampling factor (must not be 1).</param>
        /// <returns><see cref="StatusOr{T}"/> containing the downsampled 3-plane image.</returns>
        public static StatusOr<Image3<float>> DownsampleImage(Image3<float> opsin, nuint factor)
        {
            if (factor == 0 || factor == 1)
            {
                return Status.Failure("Invalid downsample factor");
            }

            JxlMemoryManager? memoryManager   = opsin.MemoryManager;
            StatusOr<Image3<float>> outResult = Image3<float>.Create(
                memoryManager,
                Common.DivCeil(opsin.XSize, factor) + BlockDim,
                Common.DivCeil(opsin.YSize, factor) + BlockDim);

            if (!outResult.IsOk)
            {
                return outResult.Status;
            }

            Image3<float> downsampled = outResult.Value;
            Status shrinkStatus       = downsampled.ShrinkTo(downsampled.XSize - BlockDim, downsampled.YSize - BlockDim);

            if (!shrinkStatus)
            {
                downsampled.Dispose();

                return shrinkStatus;
            }

            for (nuint c = 0; c < 3; c++)
            {
                Status status = DoDownsampleImage(opsin.Plane(c), factor, downsampled.Plane(c));

                if (!status)
                {
                    downsampled.Dispose();

                    return status;
                }
            }

            return downsampled;
        }

        private static void PadPlaneToBlockMultiple(
            ImagePlane<float> plane,
            nuint xsizeOrig,
            nuint ysizeOrig,
            nuint xsize,
            nuint ysize)
        {
            int xOrig = (int)xsizeOrig;
            int padW  = (int)(xsize - xsizeOrig);

            for (nuint y = 0; y < ysizeOrig; y++)
            {
                Span<float> row = plane.Row(y);
                float last      = row[xOrig - 1];

                if (padW > 0)
                {
                    row.Slice(xOrig, padW).Fill(last);
                }
            }

            ReadOnlySpan<float> rowSrc = plane.ConstRow(ysizeOrig - 1).Slice(0, (int)xsize);

            for (nuint y = ysizeOrig; y < ysize; y++)
            {
                rowSrc.CopyTo(plane.Row(y).Slice(0, (int)xsize));
            }
        }

        private static Status DoDownsampleImage(
            ImagePlane<float> input,
            nuint factor,
            ImagePlane<float> output)
        {
            Status status = output.ShrinkTo(
                Common.DivCeil(input.XSize, factor),
                Common.DivCeil(input.YSize, factor));

            if (!status)
            {
                return status;
            }

            nuint inStride = (nuint)input.PixelsPerRow;

            for (nuint y = 0; y < output.YSize; y++)
            {
                Span<float> rowOut       = output.Row(y);
                ReadOnlySpan<float> rowIn = input.ConstRow(factor * y);

                for (nuint x = 0; x < output.XSize; x++)
                {
                    rowOut[(int)x] = AverageWindow(rowIn, inStride, x, y, factor, input.XSize, input.YSize);
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AverageWindow(
            ReadOnlySpan<float> rowIn,
            nuint inStride,
            nuint x,
            nuint y,
            nuint factor,
            nuint inXSize,
            nuint inYSize)
        {
            nuint cnt = 0;
            float sum = 0.0f;

            for (nuint iy = 0; iy < factor && iy + (factor * y) < inYSize; iy++)
            {
                for (nuint ix = 0; ix < factor && ix + (factor * x) < inXSize; ix++)
                {
                    sum += rowIn[(int)((iy * inStride) + (x * factor) + ix)];
                    cnt++;
                }
            }

            return sum / cnt;
        }
    }
}
