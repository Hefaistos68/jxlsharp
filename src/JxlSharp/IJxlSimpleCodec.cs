// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.IO;
using JxlSharp.Base;

namespace JxlSharp
{
    /// <summary>
    /// Simple, streamlined interface for encoding and decoding JPEG XL (<c>.jxl</c>) images
    /// using the most common parameter defaults (visually lossless quality <c>90</c> / distance <c>1.0</c>,
    /// effort <c>7</c> Squirrel, automatic 8-bit sRGB / RGBA pixel format, and multithreading).
    /// </summary>
    public interface IJxlSimpleCodec
    {
        /// <summary>
        /// Inspects a JPEG XL codestream or container buffer and returns its <see cref="JxlBasicInfo"/> header
        /// without decoding the full image pixels.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <returns>Parsed <see cref="JxlBasicInfo"/> header.</returns>
        JxlBasicInfo GetInfo(ReadOnlySpan<byte> jxlBytes);

        /// <summary>
        /// Encodes raw 8-bit interleaved pixel bytes (Grayscale, Gray+Alpha, RGB, or RGBA) into JPEG XL format
        /// using common parameter defaults.
        /// </summary>
        /// <param name="pixels">Interleaved 8-bit pixel buffer.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="channels">Number of interleaved channels (<c>1..4</c>, default <c>4</c> RGBA).</param>
        /// <param name="quality">Target quality (<c>0..100</c>; <c>90</c> = visually lossless default, <c>100</c> = mathematically lossless).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c> Squirrel).</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] Encode(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            float quality = 90.0f,
            int effort = 7);

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance into JPEG XL format using common parameter defaults.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="quality">Target quality (<c>0..100</c>; <c>90</c> = visually lossless default, <c>100</c> = mathematically lossless).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c> Squirrel).</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] Encode(JxlImageData image, float quality = 90.0f, int effort = 7);

        /// <summary>
        /// Encodes raw 8-bit interleaved pixel bytes into mathematically lossless JPEG XL format (<c>Distance = 0.0</c>).
        /// </summary>
        /// <param name="pixels">Interleaved 8-bit pixel buffer.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="channels">Number of interleaved channels (<c>1..4</c>, default <c>4</c> RGBA).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c> Squirrel).</param>
        /// <returns>Losslessly encoded JPEG XL byte array.</returns>
        byte[] EncodeLossless(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            int effort = 7);

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance into mathematically lossless JPEG XL format (<c>Distance = 0.0</c>).
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c> Squirrel).</param>
        /// <returns>Losslessly encoded JPEG XL byte array.</returns>
        byte[] EncodeLossless(JxlImageData image, int effort = 7);

        /// <summary>
        /// Encodes an image from an existing file format buffer (PNG, PNM/PPM/PGM/PFM, PGX, JPEG, GIF, EXR) into JPEG XL bytes.
        /// </summary>
        /// <param name="imageFileBytes">Encoded input image file bytes.</param>
        /// <param name="quality">Target quality (<c>0..100</c>; default <c>90</c>).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] EncodeFromFormat(ReadOnlySpan<byte> imageFileBytes, float quality = 90.0f, int effort = 7);

        /// <summary>
        /// Encodes an input image file on disk into a <c>.jxl</c> file on disk using common defaults.
        /// </summary>
        /// <param name="inputImagePath">Source image file path.</param>
        /// <param name="outputJxlPath">Destination <c>.jxl</c> file path.</param>
        /// <param name="quality">Target quality (<c>0..100</c>; default <c>90</c>).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        void EncodeFile(string inputImagePath, string outputJxlPath, float quality = 90.0f, int effort = 7);

        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> instance and writes the resulting JPEG XL bytes to <paramref name="outputStream"/>.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="outputStream">Writable destination stream.</param>
        /// <param name="quality">Target quality (<c>0..100</c>; default <c>90</c>).</param>
        /// <param name="effort">Encoder speed/effort tier (<c>1..9</c>, default <c>7</c>).</param>
        void EncodeToStream(JxlImageData image, Stream outputStream, float quality = 90.0f, int effort = 7);

        /// <summary>
        /// Decodes a JPEG XL byte buffer into a <see cref="JxlImageData"/> instance using default parameters.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <returns>Decoded <see cref="JxlImageData"/>.</returns>
        JxlImageData Decode(ReadOnlySpan<byte> jxlBytes);

        /// <summary>
        /// Decodes a JPEG XL stream into a <see cref="JxlImageData"/> instance using default parameters.
        /// </summary>
        /// <param name="inputStream">Readable stream containing JPEG XL bytes.</param>
        /// <returns>Decoded <see cref="JxlImageData"/>.</returns>
        JxlImageData Decode(Stream inputStream);

        /// <summary>
        /// Decodes a <c>.jxl</c> (or other supported image format) file from disk into a <see cref="JxlImageData"/> instance.
        /// </summary>
        /// <param name="filePath">Path to the image file on disk.</param>
        /// <returns>Decoded <see cref="JxlImageData"/>.</returns>
        JxlImageData DecodeFile(string filePath);

        /// <summary>
        /// Decodes JPEG XL bytes and converts the image into the target file format specified by <paramref name="targetExtension"/>
        /// (e.g., <c>".png"</c>, <c>".ppm"</c>, <c>".pfm"</c>, <c>".pgx"</c>, <c>".npy"</c>, <c>".jpg"</c>).
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <param name="targetExtension">Target file extension including leading dot (e.g., <c>".png"</c> or <c>".ppm"</c>).</param>
        /// <returns>Encoded bytes in the target format.</returns>
        byte[] DecodeToFormat(ReadOnlySpan<byte> jxlBytes, string targetExtension);

        /// <summary>
        /// Decodes a JPEG XL file from disk and writes the converted output image to <paramref name="outputImagePath"/>.
        /// </summary>
        /// <param name="inputJxlPath">Input <c>.jxl</c> file path.</param>
        /// <param name="outputImagePath">Output image file path (format determined by extension).</param>
        void DecodeToFile(string inputJxlPath, string outputImagePath);
    }
}
