// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.Threads;

namespace JxlSharp
{
    /// <summary>
    /// Unified public facade for JPEG XL (<c>libjxl</c>) encoding, decoding, color management, and multithreading.
    /// </summary>
    public static class JxlCodec
    {
        private static readonly JxlSharpCodec DefaultCodec = new JxlSharpCodec();

        /// <summary>
        /// Gets the numeric library version (<c>0x000D00</c> for v0.13.0).
        /// </summary>
        public static uint Version => JxlDecoder.Version();

        /// <summary>
        /// Gets the default <see cref="IJxlSimpleCodec"/> instance for encoding and decoding with common defaults.
        /// </summary>
        public static IJxlSimpleCodec Simple => DefaultCodec;

        /// <summary>
        /// Gets the default <see cref="IJxlExtendedCodec"/> instance for encoding and decoding with full option control.
        /// </summary>
        public static IJxlExtendedCodec Extended => DefaultCodec;

        /// <summary>
        /// Checks whether <paramref name="buf"/> starts with a valid JPEG XL codestream or container signature.
        /// </summary>
        /// <param name="buf">Input byte buffer to inspect.</param>
        /// <returns>The detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(ReadOnlySpan<byte> buf)
        {
            return JxlDecoder.CheckSignature(buf);
        }

        /// <summary>
        /// Inspects a JPEG XL codestream or container buffer and returns its <see cref="JxlBasicInfo"/> header.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <returns>Parsed <see cref="JxlBasicInfo"/> header.</returns>
        public static JxlBasicInfo GetInfo(ReadOnlySpan<byte> jxlBytes)
        {
            return DefaultCodec.GetInfo(jxlBytes);
        }

        /// <summary>
        /// Encodes raw 8-bit interleaved pixel bytes into JPEG XL format using common parameter defaults.
        /// </summary>
        /// <param name="pixels">Interleaved 8-bit pixel buffer.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="channels">Number of interleaved channels (<c>1..4</c>, default <c>4</c> RGBA).</param>
        /// <param name="quality">Target quality (<c>0..100</c>; default <c>90</c>).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        public static byte[] Encode(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            float quality = 90.0f,
            int effort = 7)
        {
            return DefaultCodec.Encode(pixels, width, height, channels, quality, effort);
        }

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance into JPEG XL format using common parameter defaults.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="quality">Target quality (<c>0..100</c>; default <c>90</c>).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        public static byte[] Encode(JxlImageData image, float quality = 90.0f, int effort = 7)
        {
            return DefaultCodec.Encode(image, quality, effort);
        }

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance into JPEG XL format using extended <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="options">Extended encoding options.</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        public static byte[] Encode(JxlImageData image, JxlEncodeOptions options)
        {
            return DefaultCodec.Encode(image, options);
        }

        /// <summary>
        /// Encodes a <see cref="PackedPixelFile"/> into JPEG XL format using extended <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="pixelFile">Input packed pixel file.</param>
        /// <param name="options">Extended encoding options.</param>
        /// <param name="losslessJpegBytes">Optional raw JPEG bytes for lossless transcoding.</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        public static byte[] Encode(PackedPixelFile pixelFile, JxlEncodeOptions options, byte[]? losslessJpegBytes = null)
        {
            return DefaultCodec.Encode(pixelFile, options, losslessJpegBytes);
        }

        /// <summary>
        /// Encodes raw 8-bit interleaved pixel bytes into mathematically lossless JPEG XL format.
        /// </summary>
        /// <param name="pixels">Interleaved 8-bit pixel buffer.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="channels">Number of interleaved channels (<c>1..4</c>, default <c>4</c> RGBA).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        /// <returns>Losslessly encoded JPEG XL byte array.</returns>
        public static byte[] EncodeLossless(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            int effort = 7)
        {
            return DefaultCodec.EncodeLossless(pixels, width, height, channels, effort);
        }

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance into mathematically lossless JPEG XL format.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        /// <returns>Losslessly encoded JPEG XL byte array.</returns>
        public static byte[] EncodeLossless(JxlImageData image, int effort = 7)
        {
            return DefaultCodec.EncodeLossless(image, effort);
        }

        /// <summary>
        /// Decodes a JPEG XL byte buffer into a <see cref="JxlImageData"/> instance using default parameters.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <returns>Decoded <see cref="JxlImageData"/>.</returns>
        public static JxlImageData Decode(ReadOnlySpan<byte> jxlBytes)
        {
            return DefaultCodec.Decode(jxlBytes);
        }

        /// <summary>
        /// Decodes a JPEG XL byte buffer using extended <see cref="JxlDecodeOptions"/>.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <param name="options">Extended decoding options.</param>
        /// <returns>Full <see cref="JxlDecodeResult"/>.</returns>
        public static JxlDecodeResult Decode(ReadOnlySpan<byte> jxlBytes, JxlDecodeOptions options)
        {
            return DefaultCodec.Decode(jxlBytes, options);
        }

        /// <summary>
        /// Creates a new <see cref="JxlDecoder"/> instance.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlDecoder"/>.</returns>
        public static JxlDecoder CreateDecoder(JxlMemoryManager? memoryManager = null)
        {
            return JxlDecoder.Create(memoryManager);
        }

        /// <summary>
        /// Creates a new <see cref="JxlEncoder"/> instance.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlEncoder"/>.</returns>
        public static JxlEncoder CreateEncoder(JxlMemoryManager? memoryManager = null)
        {
            return JxlEncoder.Create(memoryManager);
        }

        /// <summary>
        /// Returns the default JPEG XL Color Management System (<see cref="JxlCmsInterface"/>) implementation.
        /// </summary>
        /// <returns>Default <see cref="JxlCmsInterface"/>.</returns>
        public static JxlCmsInterface GetDefaultCms()
        {
            return JxlCms.GetDefaultCms();
        }

        /// <summary>
        /// Creates a new <see cref="ThreadParallelRunner"/> with the specified number of worker threads.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <param name="numWorkerThreads">Optional worker thread count (defaults to logical processor count).</param>
        /// <returns>A newly initialized <see cref="ThreadParallelRunner"/>.</returns>
        public static ThreadParallelRunner? CreateThreadParallelRunner(
            JxlMemoryManager? memoryManager = null,
            nuint? numWorkerThreads = null)
        {
            nuint workers = numWorkerThreads ?? ThreadParallelRunner.DefaultNumWorkerThreads();

            return ThreadParallelRunner.Create(memoryManager, workers);
        }
    }
}
