// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Parameters for exporting planar float channels to an external interleaved buffer or callback.
    /// </summary>
    public readonly struct ExternalConvertOptions
    {
        /// <summary>Bits per sample (8, 16, or 32).</summary>
        public readonly nuint BitsPerSample;

        /// <summary>Whether the output sample format is floating-point.</summary>
        public readonly bool FloatOut;

        /// <summary>Endianness of the output samples.</summary>
        public readonly JxlEndianness Endianness;

        /// <summary>Row stride in bytes for buffer output.</summary>
        public readonly nuint Stride;

        /// <summary>Optional thread pool for parallel conversion.</summary>
        public readonly ThreadPool? Pool;

        /// <summary>Target output buffer (may be empty if <see cref="OutCallback"/> is active).</summary>
        public readonly Memory<byte> OutBuffer;

        /// <summary>Size of <see cref="OutBuffer"/> in bytes.</summary>
        public readonly nuint OutSize;

        /// <summary>Optional pixel output callback.</summary>
        public readonly PixelCallback OutCallback;

        /// <summary>Image orientation to undo during export.</summary>
        public readonly Orientation UndoOrientation;

        /// <summary>Initializes a new instance of the <see cref="ExternalConvertOptions"/> struct.</summary>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="floatOut">True for float output.</param>
        /// <param name="endianness">Output endianness.</param>
        /// <param name="stride">Output row stride in bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outBuffer">Destination buffer.</param>
        /// <param name="outSize">Destination buffer size in bytes.</param>
        public ExternalConvertOptions(
            nuint bitsPerSample,
            bool floatOut,
            JxlEndianness endianness,
            nuint stride,
            ThreadPool? pool,
            Memory<byte> outBuffer,
            nuint outSize)
        {
            BitsPerSample   = bitsPerSample;
            FloatOut        = floatOut;
            Endianness      = endianness;
            Stride          = stride;
            Pool            = pool;
            OutBuffer       = outBuffer;
            OutSize         = outSize;
            OutCallback     = default;
            UndoOrientation = Orientation.Identity;
        }

        /// <summary>Initializes a new instance of the <see cref="ExternalConvertOptions"/> struct.</summary>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="floatOut">True for float output.</param>
        /// <param name="endianness">Output endianness.</param>
        /// <param name="stride">Output row stride in bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outBuffer">Destination buffer.</param>
        public ExternalConvertOptions(
            nuint bitsPerSample,
            bool floatOut,
            JxlEndianness endianness,
            nuint stride,
            ThreadPool? pool,
            Memory<byte> outBuffer)
            : this(bitsPerSample, floatOut, endianness, stride, pool, outBuffer, (nuint)outBuffer.Length)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="ExternalConvertOptions"/> struct.</summary>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="floatOut">True for float output.</param>
        /// <param name="endianness">Output endianness.</param>
        /// <param name="stride">Output row stride in bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outBuffer">Destination byte array.</param>
        /// <param name="outSize">Destination buffer size in bytes.</param>
        public ExternalConvertOptions(
            nuint bitsPerSample,
            bool floatOut,
            JxlEndianness endianness,
            nuint stride,
            ThreadPool? pool,
            byte[]? outBuffer,
            nuint outSize)
            : this(
                bitsPerSample,
                floatOut,
                endianness,
                stride,
                pool,
                outBuffer != null ? outBuffer.AsMemory() : Memory<byte>.Empty,
                outSize)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="ExternalConvertOptions"/> struct.</summary>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="floatOut">True for float output.</param>
        /// <param name="endianness">Output endianness.</param>
        /// <param name="stride">Output row stride in bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="outBuffer">Destination byte array.</param>
        public ExternalConvertOptions(
            nuint bitsPerSample,
            bool floatOut,
            JxlEndianness endianness,
            nuint stride,
            ThreadPool? pool,
            byte[]? outBuffer)
            : this(
                bitsPerSample,
                floatOut,
                endianness,
                stride,
                pool,
                outBuffer != null ? outBuffer.AsMemory() : Memory<byte>.Empty,
                outBuffer != null ? (nuint)outBuffer.Length : 0u)
        {
        }

        /// <summary>Returns a copy with the specified <paramref name="callback"/> and <paramref name="undoOrientation"/>.</summary>
        /// <param name="callback">Pixel callback.</param>
        /// <param name="undoOrientation">Orientation transform.</param>
        /// <returns>Updated options instance.</returns>
        public ExternalConvertOptions WithCallbackAndOrientation(PixelCallback callback, Orientation undoOrientation)
        {
            return new ExternalConvertOptions(
                BitsPerSample,
                FloatOut,
                Endianness,
                Stride,
                Pool,
                OutBuffer,
                OutSize,
                callback,
                undoOrientation);
        }

        private ExternalConvertOptions(
            nuint bitsPerSample,
            bool floatOut,
            JxlEndianness endianness,
            nuint stride,
            ThreadPool? pool,
            Memory<byte> outBuffer,
            nuint outSize,
            PixelCallback outCallback,
            Orientation undoOrientation)
        {
            BitsPerSample   = bitsPerSample;
            FloatOut        = floatOut;
            Endianness      = endianness;
            Stride          = stride;
            Pool            = pool;
            OutBuffer       = outBuffer;
            OutSize         = outSize;
            OutCallback     = outCallback;
            UndoOrientation = undoOrientation;
        }
    }

    /// <summary>
    /// Converts internal planar floating-point images to external interleaved integer or floating-point formats.
    /// Ported from <c>lib/jxl/dec_external_image.h</c> and <c>lib/jxl/dec_external_image.cc</c>.
    /// </summary>
    public static class DecExternalImage
    {
        // Maximum number of channels for theConvertChannelsToExternal function.
        /// <summary>Maximum number of interleaved channels supported by <see cref="ConvertChannelsToExternal"/>.</summary>
        public const nuint kMaxChannels = 4;

        /// <summary>
        /// Clamps and scales a float row in [0, 1] to unsigned integers in [0, (1 &lt;&lt; bitsPerSample) - 1].
        /// Ported from <c>FloatToU32</c> in <c>lib/jxl/dec_external_image.cc</c>.
        /// </summary>
        /// <param name="inRow">Input float span.</param>
        /// <param name="outRow">Output uint32 span.</param>
        /// <param name="num">Number of samples to convert.</param>
        /// <param name="mul">Scale factor <c>(1u &lt;&lt; bitsPerSample) - 1</c>.</param>
        /// <param name="bitsPerSample">Target bits per sample.</param>
        public static void FloatToU32(ReadOnlySpan<float> inRow, Span<uint> outRow, nuint num, float mul, nuint bitsPerSample)
        {
            float maxCap = (bitsPerSample == 32) ? 4294967295.0f : ((1u << (int)bitsPerSample) - 1u);

            for (nuint x = 0; x < num; x++)
            {
                float v = inRow[(int)x];

                // Clamp turns NaN to min, but comparing First with >=0 turns NaN to 0.
                if (!(v >= 0.0f))
                {
                    v = 0.0f;
                }
                else if (v >= 1.0f)
                {
                    v = maxCap;
                }
                else
                {
                    v = (v * mul) + 0.5f;
                }

                outRow[(int)x] = (uint)v;
            }
        }

        /// <summary>
        /// Converts a float32 row to IEEE 754 binary16 (half-precision) uint16 bit representations.
        /// Ported from <c>FloatToF16</c> in <c>lib/jxl/dec_external_image.cc</c>.
        /// </summary>
        /// <param name="inRow">Input float32 span.</param>
        /// <param name="outRow">Output uint16 raw half-float bits span.</param>
        /// <param name="num">Number of samples to convert.</param>
        public static void FloatToF16(ReadOnlySpan<float> inRow, Span<ushort> outRow, nuint num)
        {
            for (nuint x = 0; x < num; x++)
            {
                Half half      = (Half)inRow[(int)x];
                outRow[(int)x] = BitConverter.HalfToUInt16Bits(half);
            }
        }

        /// <summary>
        /// Converts a list of planar float channels to an external interleaved buffer or callback.
        /// Ported from <c>jxl::ConvertChannelsToExternal</c> in <c>lib/jxl/dec_external_image.cc</c>.
        /// </summary>
        /// <param name="channels">Array of up to <see cref="kMaxChannels"/> channel planes (null entries duplicate the previous channel).</param>
        /// <param name="numChannels">Number of valid entries in <paramref name="channels"/>.</param>
        /// <param name="options">Conversion options and output destination.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ConvertChannelsToExternal(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            in ExternalConvertOptions options)
        {
            if (numChannels == 0 || numChannels > kMaxChannels || channels.Length < (int)numChannels || channels[0] == null)
            {
                return Status.Failure("Invalid channel array in ConvertChannelsToExternal");
            }

            if (options.FloatOut ? (options.BitsPerSample != 16 && options.BitsPerSample != 32)
                                 : (options.BitsPerSample == 0 || options.BitsPerSample > 32))
            {
                return Status.Failure("Unsupported bits_per_sample in ConvertChannelsToExternal");
            }

            ImagePlane<float>?[] localChannels = new ImagePlane<float>?[kMaxChannels];

            for (nuint c = 0; c < numChannels; c++)
            {
                localChannels[c] = channels[c] ?? (c > 0 ? localChannels[c - 1] : null);
            }

            nuint bytesPerChannel = DivCeil(options.BitsPerSample, 8u);
            nuint bytesPerPixel   = numChannels * bytesPerChannel;
            nuint xsize           = localChannels[0]!.XSize;
            nuint ysize           = localChannels[0]!.YSize;

            if (options.Stride < bytesPerPixel * xsize)
            {
                return Status.Failure("stride is smaller than scanline width in bytes");
            }

            if (!options.OutCallback.IsPresent() &&
                (options.OutSize < ((ysize - 1) * options.Stride) + (xsize * bytesPerPixel) ||
                 (nuint)options.OutBuffer.Length < ((ysize - 1) * options.Stride) + (xsize * bytesPerPixel)))
            {
                return Status.Failure("out_size is too small to store image");
            }

            bool littleEndian = options.Endianness == JxlEndianness.LittleEndian ||
                                (options.Endianness == JxlEndianness.NativeEndian && ByteOrder.IsLittleEndian());

            if (options.UndoOrientation != Orientation.Identity)
            {
                return ConvertWithUndoOrientation(localChannels, numChannels, in options, bytesPerPixel, xsize, ysize);
            }

            return ConvertScanlinesDirect(localChannels, numChannels, in options, bytesPerPixel, xsize, ysize, littleEndian);
        }

        /// <summary>
        /// Converts an <see cref="ImageBundle"/> (color + optional alpha) to an external interleaved buffer or callback.
        /// Ported from <c>jxl::ConvertToExternal</c> in <c>lib/jxl/dec_external_image.cc</c>.
        /// </summary>
        /// <param name="ib">Source image bundle.</param>
        /// <param name="numChannels">Requested output channel count (1..4).</param>
        /// <param name="unpremulAlpha">True if premultiplied alpha should be unpremultiplied when exporting.</param>
        /// <param name="options">Conversion options and output destination.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ConvertToExternal(
            ImageBundle ib,
            nuint numChannels,
            bool unpremulAlpha,
            in ExternalConvertOptions options)
        {
            bool wantAlpha  = numChannels == 2 || numChannels == 4;
            bool hasAlpha   = ib.HasAlpha();
            nuint colorDims = numChannels <= 2 ? 1u : 3u;
            nuint xsize     = ib.XSize();
            nuint ysize     = ib.YSize();

            Image3<float>? tempColor = null;
            ImagePlane<float>? ones  = null;

            try
            {
                Image3<float> color = ib.Color();

                if (ib.AlphaIsPremultiplied() && hasAlpha && unpremulAlpha)
                {
                    StatusOr<Image3<float>> copyOr = Image3<float>.Create(ib.MemoryManager, xsize, ysize);

                    if (!copyOr.IsOk)
                    {
                        return copyOr.Status;
                    }

                    tempColor = copyOr.Value;

                    if (!ImageOps.CopyImageTo(color, tempColor))
                    {
                        return false;
                    }

                    UnpremultiplyImageBundleColor(tempColor, ib.Alpha()!, xsize, ysize);
                    color = tempColor;
                }

                ImagePlane<float>?[] channels = new ImagePlane<float>?[kMaxChannels];
                PopulateColorChannels(color, colorDims, channels);

                if (wantAlpha)
                {
                    if (hasAlpha)
                    {
                        channels[colorDims] = ib.Alpha();
                    }
                    else
                    {
                        StatusOr<ImagePlane<float>> onesOr = CreateOpaqueAlphaPlane(ib.MemoryManager, xsize, ysize);

                        if (!onesOr.IsOk)
                        {
                            return onesOr.Status;
                        }

                        ones                = onesOr.Value;
                        channels[colorDims] = ones;
                    }
                }

                return ConvertChannelsToExternal(channels, numChannels, in options);
            }
            finally
            {
                tempColor?.Dispose();
                ones?.Dispose();
            }
        }

        private static void PopulateColorChannels(
            Image3<float> color,
            nuint colorChannels,
            ImagePlane<float>?[] channels)
        {
            channels[0] = color.Plane(0);

            if (colorChannels == 3)
            {
                channels[1] = color.Plane(1);
                channels[2] = color.Plane(2);
            }
        }

        private static void UnpremultiplyImageBundleColor(
            Image3<float> color,
            ImagePlane<float> alpha,
            nuint xsize,
            nuint ysize)
        {
            for (nuint y = 0; y < ysize; y++)
            {
                Alpha.UnpremultiplyAlpha(
                    color.PlaneRow(0, y),
                    color.PlaneRow(1, y),
                    color.PlaneRow(2, y),
                    alpha.ConstRow(y),
                    xsize);
            }
        }

        private static StatusOr<ImagePlane<float>> CreateOpaqueAlphaPlane(
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize)
        {
            StatusOr<ImagePlane<float>> onesOr = ImagePlane<float>.Create(memoryManager, xsize, ysize);

            if (!onesOr.IsOk)
            {
                return onesOr.Status;
            }

            ImagePlane<float> ones = onesOr.Value;
            ImageOps.FillImage(1.0f, ones);

            return ones;
        }

        private static Status ConvertWithUndoOrientation(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            in ExternalConvertOptions options,
            nuint bytesPerPixel,
            nuint xsize,
            nuint ysize)
        {
            nuint orientedBytes = xsize * ysize * bytesPerPixel;
            byte[] tempBuffer   = new byte[orientedBytes];

            ExternalConvertOptions identityOpts = new ExternalConvertOptions(
                options.BitsPerSample,
                options.FloatOut,
                options.Endianness,
                xsize * bytesPerPixel,
                options.Pool,
                tempBuffer.AsMemory(),
                orientedBytes);

            Status status = ConvertChannelsToExternal(channels, numChannels, in identityOpts);

            if (!status.IsOk)
            {
                return status;
            }

            UndoOrientation(in options, tempBuffer, xsize, ysize, bytesPerPixel);

            return true;
        }

        private static void UndoOrientation(
            in ExternalConvertOptions options,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            switch (options.UndoOrientation)
            {
                case Orientation.FlipHorizontal:
                    UndoFlipHorizontal(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.Rotate180:
                    UndoRotate180(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.FlipVertical:
                    UndoFlipVertical(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.Transpose:
                    UndoTranspose(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.Rotate90:
                    UndoRotate90(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.AntiTranspose:
                    UndoAntiTranspose(in options, src, xsize, ysize, bytesPerPixel);
                    break;

                case Orientation.Rotate270:
                    UndoRotate270(in options, src, xsize, ysize, bytesPerPixel);
                    break;
            }
        }

        private static void UndoFlipHorizontal(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, xsize, ysize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)((((long)y * (long)xsize) + (long)xsize - 1 - (long)x) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private static void UndoRotate180(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, xsize, ysize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)(((((long)ysize - 1 - (long)y) * (long)xsize) + (long)xsize - 1 - (long)x) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private static void UndoFlipVertical(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int rowBytes = (int)(xsize * bytesPerPixel);
            OrientLines(in opts, 1, ysize, (nuint)rowBytes, (dst, _, y) =>
            {
                int srcOffset = (int)(((long)ysize - 1 - (long)y) * rowBytes);
                src.AsSpan(srcOffset, rowBytes).CopyTo(dst.Slice(0, rowBytes));
            });
        }

        private static void UndoTranspose(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, ysize, xsize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)((((long)x * (long)xsize) + (long)y) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private static void UndoRotate90(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, ysize, xsize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)(((((long)ysize - 1 - (long)x) * (long)xsize) + (long)y) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private static void UndoAntiTranspose(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, ysize, xsize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)(((((long)ysize - 1 - (long)x) * (long)xsize) + (long)xsize - 1 - (long)y) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private static void UndoRotate270(
            in ExternalConvertOptions opts,
            byte[] src,
            nuint xsize,
            nuint ysize,
            nuint bytesPerPixel)
        {
            int bpp = (int)bytesPerPixel;
            OrientLines(in opts, ysize, xsize, bytesPerPixel, (dst, x, y) =>
            {
                int srcOffset = (int)((((long)x * (long)xsize) + (long)xsize - 1 - (long)y) * bpp);
                src.AsSpan(srcOffset, bpp).CopyTo(dst.Slice((int)x * bpp, bpp));
            });
        }

        private delegate void OrientPixelAction(Span<byte> dstRow, nuint x, nuint y);

        private static void OrientLines(
            in ExternalConvertOptions opts,
            nuint rxsize,
            nuint rysize,
            nuint bytesPerPixel,
            OrientPixelAction action)
        {
            if (opts.OutCallback.IsPresent())
            {
                object? runOpaque = opts.OutCallback.Init(1, rxsize);
                byte[] rowBuffer  = new byte[rxsize * bytesPerPixel];

                try
                {
                    Span<byte> rowSpan = rowBuffer.AsSpan();

                    for (nuint y = 0; y < rysize; y++)
                    {
                        for (nuint x = 0; x < rxsize; x++)
                        {
                            action(rowSpan, x, y);
                        }

                        opts.OutCallback.Run!(runOpaque, 0, 0, y, rxsize, rowSpan);
                    }
                }
                finally
                {
                    opts.OutCallback.Destroy!(runOpaque);
                }

                return;
            }

            ExternalConvertOptions capturedOpts = opts;
            DataParallel.RunOnPool(
                opts.Pool,
                0,
                (uint)rysize,
                _ => Status.OkStatus,
                (task, _) =>
                {
                    nuint y           = task;
                    Span<byte> dstRow = capturedOpts.OutBuffer.Span.Slice((int)(y * capturedOpts.Stride));

                    for (nuint x = 0; x < rxsize; x++)
                    {
                        action(dstRow, x, y);
                    }

                    return true;
                },
                "UndoOrientation");
        }

        private static Status ConvertScanlinesDirect(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            in ExternalConvertOptions options,
            nuint bytesPerPixel,
            nuint xsize,
            nuint ysize,
            bool littleEndian)
        {
            ExternalConvertOptions capturedOpts = options;
            object? callbackOpaque              = null;
            byte[][]? threadLineBuffers         = null;

            try
            {
                Status status = DataParallel.RunOnPool(
                    options.Pool,
                    0,
                    (uint)ysize,
                    numThreads =>
                    {
                        if (capturedOpts.OutCallback.IsPresent())
                        {
                            callbackOpaque    = capturedOpts.OutCallback.Init(numThreads, xsize);
                            threadLineBuffers = new byte[numThreads][];
                            int rowByteCount  = (int)(xsize * bytesPerPixel);

                            for (nuint t = 0; t < numThreads; t++)
                            {
                                threadLineBuffers[t] = new byte[rowByteCount];
                            }

                            return callbackOpaque != null || capturedOpts.OutCallback.InitCallback == null;
                        }

                        return true;
                    },
                    (task, thread) =>
                    {
                        nuint y = task;

                        if (capturedOpts.OutCallback.IsPresent())
                        {
                            Span<byte> lineSpan = threadLineBuffers![thread].AsSpan();
                            ConvertSingleRow(channels, numChannels, in capturedOpts, xsize, y, littleEndian, lineSpan);
                            capturedOpts.OutCallback.Run!(callbackOpaque, thread, 0, y, xsize, lineSpan);
                        }
                        else
                        {
                            Span<byte> rowOut = capturedOpts.OutBuffer.Span.Slice((int)(y * capturedOpts.Stride));
                            ConvertSingleRow(channels, numChannels, in capturedOpts, xsize, y, littleEndian, rowOut);
                        }

                        return true;
                    },
                    "ConvertExtraChannels");

                return status;
            }
            finally
            {
                if (options.OutCallback.IsPresent())
                {
                    options.OutCallback.Destroy!(callbackOpaque);
                }
            }
        }

        private static void ConvertSingleRow(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            in ExternalConvertOptions options,
            nuint xsize,
            nuint y,
            bool littleEndian,
            Span<byte> rowOut)
        {
            if (options.FloatOut)
            {
                ConvertFloatRow(channels, numChannels, options.BitsPerSample, xsize, y, littleEndian, rowOut);

                return;
            }

            ConvertUintRow(channels, numChannels, options.BitsPerSample, xsize, y, littleEndian, rowOut);
        }

        private static void ConvertFloatRow(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            nuint bitsPerSample,
            nuint xsize,
            nuint y,
            bool littleEndian,
            Span<byte> rowOut)
        {
            if (bitsPerSample == 16)
            {
                ushort[] tempF16     = new ushort[xsize];
                Span<ushort> tempSpan = tempF16.AsSpan();

                for (nuint c = 0; c < numChannels; c++)
                {
                    FloatToF16(channels[c]!.ConstRow(y), tempSpan, xsize);
                    InterleaveU16Channel(tempSpan, c, numChannels, xsize, littleEndian, rowOut);
                }

                return;
            }

            uint[] tempU32     = new uint[xsize];
            Span<uint> u32Span = tempU32.AsSpan();

            for (nuint c = 0; c < numChannels; c++)
            {
                ReadOnlySpan<float> inRow = channels[c]!.ConstRow(y);

                for (nuint x = 0; x < xsize; x++)
                {
                    u32Span[(int)x] = BitConverter.SingleToUInt32Bits(inRow[(int)x]);
                }

                InterleaveU32Channel(u32Span, c, numChannels, xsize, littleEndian, rowOut);
            }
        }

        private static void ConvertUintRow(
            ImagePlane<float>?[] channels,
            nuint numChannels,
            nuint bitsPerSample,
            nuint xsize,
            nuint y,
            bool littleEndian,
            Span<byte> rowOut)
        {
            // Multiplier for converting from [0, 1] to [0, (1 << bits_per_sample) - 1].
            float mul          = (bitsPerSample == 32) ? 4294967295.0f : ((1u << (int)bitsPerSample) - 1u);
            uint[] tempU32     = new uint[xsize];
            Span<uint> tempSpan = tempU32.AsSpan();

            for (nuint c = 0; c < numChannels; c++)
            {
                FloatToU32(channels[c]!.ConstRow(y), tempSpan, xsize, mul, bitsPerSample);
                StoreUintChannel(tempSpan, c, numChannels, bitsPerSample, xsize, littleEndian, rowOut);
            }
        }

        private static void StoreUintChannel(
            ReadOnlySpan<uint> tempPtr,
            nuint c,
            nuint numChannels,
            nuint bitsPerSample,
            nuint xsize,
            bool littleEndian,
            Span<byte> rowOut)
        {
            if (bitsPerSample <= 8)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    rowOut[(int)((x * numChannels) + c)] = (byte)tempPtr[(int)x];
                }

                return;
            }

            if (bitsPerSample <= 16)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    Span<byte> dst = rowOut.Slice((int)(((x * numChannels) + c) * 2), 2);

                    if (littleEndian)
                    {
                        ByteOrder.StoreLE16((ushort)tempPtr[(int)x], dst);
                    }
                    else
                    {
                        ByteOrder.StoreBE16((ushort)tempPtr[(int)x], dst);
                    }
                }

                return;
            }

            if (bitsPerSample <= 24)
            {
                StoreU24Channel(tempPtr, c, numChannels, xsize, littleEndian, rowOut);

                return;
            }

            InterleaveU32Channel(tempPtr, c, numChannels, xsize, littleEndian, rowOut);
        }

        private static void StoreU24Channel(
            ReadOnlySpan<uint> tempPtr,
            nuint c,
            nuint numChannels,
            nuint xsize,
            bool littleEndian,
            Span<byte> rowOut)
        {
            for (nuint x = 0; x < xsize; x++)
            {
                uint val       = tempPtr[(int)x];
                Span<byte> dst = rowOut.Slice((int)(((x * numChannels) + c) * 3), 3);

                if (littleEndian)
                {
                    dst[0] = (byte)(val & 0xFFu);
                    dst[1] = (byte)((val >> 8) & 0xFFu);
                    dst[2] = (byte)((val >> 16) & 0xFFu);
                }
                else
                {
                    dst[0] = (byte)((val >> 16) & 0xFFu);
                    dst[1] = (byte)((val >> 8) & 0xFFu);
                    dst[2] = (byte)(val & 0xFFu);
                }
            }
        }

        private static void InterleaveU16Channel(
            ReadOnlySpan<ushort> tempPtr,
            nuint c,
            nuint numChannels,
            nuint xsize,
            bool littleEndian,
            Span<byte> rowOut)
        {
            for (nuint x = 0; x < xsize; x++)
            {
                Span<byte> dst = rowOut.Slice((int)(((x * numChannels) + c) * 2), 2);

                if (littleEndian)
                {
                    ByteOrder.StoreLE16(tempPtr[(int)x], dst);
                }
                else
                {
                    ByteOrder.StoreBE16(tempPtr[(int)x], dst);
                }
            }
        }

        private static void InterleaveU32Channel(
            ReadOnlySpan<uint> tempPtr,
            nuint c,
            nuint numChannels,
            nuint xsize,
            bool littleEndian,
            Span<byte> rowOut)
        {
            for (nuint x = 0; x < xsize; x++)
            {
                Span<byte> dst = rowOut.Slice((int)(((x * numChannels) + c) * 4), 4);

                if (littleEndian)
                {
                    ByteOrder.StoreLE32(tempPtr[(int)x], dst);
                }
                else
                {
                    ByteOrder.StoreBE32(tempPtr[(int)x], dst);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint DivCeil(nuint a, nuint b) => (a + b - 1) / b;
    }
}
