// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.IO;
using JxlSharp.Extras;

namespace JxlSharp
{
    /// <summary>
    /// Extended JPEG XL codec interface that inherits all simple operations from <see cref="IJxlSimpleCodec"/>
    /// and adds full-control encoding and decoding overloads accepting <see cref="JxlEncodeOptions"/>,
    /// <see cref="JxlDecodeOptions"/>, and multi-frame <see cref="PackedPixelFile"/> structures.
    /// </summary>
    public interface IJxlExtendedCodec : IJxlSimpleCodec
    {
        /// <summary>
        /// Encodes a <see cref="JxlImageData"/> into JPEG XL format using the full set of <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="image">Input image data.</param>
        /// <param name="options">Extended encoding options and parameters.</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] Encode(JxlImageData image, JxlEncodeOptions options);

        /// <summary>
        /// Encodes a multi-frame or extra-channel <see cref="PackedPixelFile"/> (or transcodes <paramref name="losslessJpegBytes"/>)
        /// into JPEG XL format using the full set of <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="pixelFile">Input packed pixel file.</param>
        /// <param name="options">Extended encoding options and parameters.</param>
        /// <param name="losslessJpegBytes">Optional raw JPEG bytes for lossless JPEG bitstream transcoding.</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] Encode(PackedPixelFile pixelFile, JxlEncodeOptions options, byte[]? losslessJpegBytes = null);

        /// <summary>
        /// Encodes an image from an existing file format buffer (PNG, PNM/PPM/PGM/PFM, PGX, JPEG, GIF, EXR)
        /// into JPEG XL bytes using <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="imageFileBytes">Encoded input image file bytes.</param>
        /// <param name="options">Extended encoding options and parameters.</param>
        /// <returns>Encoded JPEG XL byte array.</returns>
        byte[] EncodeFromFormat(ReadOnlySpan<byte> imageFileBytes, JxlEncodeOptions options);

        /// <summary>
        /// Encodes an input image file on disk into a <c>.jxl</c> file on disk using <see cref="JxlEncodeOptions"/>.
        /// </summary>
        /// <param name="inputImagePath">Source image file path.</param>
        /// <param name="outputJxlPath">Destination <c>.jxl</c> file path.</param>
        /// <param name="options">Extended encoding options and parameters.</param>
        void EncodeFile(string inputImagePath, string outputJxlPath, JxlEncodeOptions options);

        /// <summary>
        /// Encodes a <see cref="PackedPixelFile"/> using <see cref="JxlEncodeOptions"/> and writes the output to <paramref name="outputStream"/>.
        /// </summary>
        /// <param name="pixelFile">Input packed pixel file.</param>
        /// <param name="outputStream">Writable destination stream.</param>
        /// <param name="options">Extended encoding options and parameters.</param>
        /// <param name="losslessJpegBytes">Optional raw JPEG bytes for lossless JPEG bitstream transcoding.</param>
        void EncodeToStream(
            PackedPixelFile pixelFile,
            Stream outputStream,
            JxlEncodeOptions options,
            byte[]? losslessJpegBytes = null);

        /// <summary>
        /// Decodes a JPEG XL byte buffer using the full set of <see cref="JxlDecodeOptions"/>.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <param name="options">Extended decoding options and parameters.</param>
        /// <returns>Full <see cref="JxlDecodeResult"/> containing frames, metadata, and optional reconstructed JPEG bytes.</returns>
        JxlDecodeResult Decode(ReadOnlySpan<byte> jxlBytes, JxlDecodeOptions options);

        /// <summary>
        /// Decodes a JPEG XL stream using the full set of <see cref="JxlDecodeOptions"/>.
        /// </summary>
        /// <param name="inputStream">Readable stream containing JPEG XL bytes.</param>
        /// <param name="options">Extended decoding options and parameters.</param>
        /// <returns>Full <see cref="JxlDecodeResult"/>.</returns>
        JxlDecodeResult Decode(Stream inputStream, JxlDecodeOptions options);

        /// <summary>
        /// Decodes an image file from disk using the full set of <see cref="JxlDecodeOptions"/>.
        /// </summary>
        /// <param name="filePath">Path to the image file on disk.</param>
        /// <param name="options">Extended decoding options and parameters.</param>
        /// <returns>Full <see cref="JxlDecodeResult"/>.</returns>
        JxlDecodeResult DecodeFile(string filePath, JxlDecodeOptions options);

        /// <summary>
        /// Decodes JPEG XL bytes using <see cref="JxlDecodeOptions"/> and encodes the result into the format
        /// specified by <paramref name="targetExtension"/>.
        /// </summary>
        /// <param name="jxlBytes">Encoded JPEG XL bytes.</param>
        /// <param name="targetExtension">Target file extension including leading dot (e.g., <c>".png"</c>, <c>".ppm"</c>, <c>".jpg"</c>).</param>
        /// <param name="options">Extended decoding options and parameters.</param>
        /// <returns>Encoded bytes in the target format.</returns>
        byte[] DecodeToFormat(ReadOnlySpan<byte> jxlBytes, string targetExtension, JxlDecodeOptions options);
    }
}
