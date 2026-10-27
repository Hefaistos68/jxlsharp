// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Extras;

namespace JxlSharp
{
    /// <summary>
    /// Represents the complete output of an extended JPEG XL decode operation, including the full
    /// <see cref="PackedPixelFile"/> (all frames, extra channels, ICC profiles, and metadata boxes),
    /// the primary frame's <see cref="JxlImageData"/>, and optional byte-exact reconstructed JPEG data.
    /// </summary>
    public sealed class JxlDecodeResult
    {
        /// <summary>
        /// Gets the full decoded <see cref="PackedPixelFile"/> containing all frames, extra channels,
        /// color profiles, and container metadata boxes.
        /// </summary>
        public PackedPixelFile PixelFile { get; }

        /// <summary>
        /// Gets the primary frame's interleaved pixel data (<see cref="JxlImageData"/>), or <c>null</c>
        /// if only lossless JPEG reconstruction was produced without pixel frames.
        /// </summary>
        public JxlImageData? PrimaryImage { get; }

        /// <summary>
        /// Gets the decoded JPEG XL basic header information.
        /// </summary>
        public JxlBasicInfo BasicInfo => PixelFile.Info;

        /// <summary>
        /// Gets the color encoding description of the decoded pixels.
        /// </summary>
        public JxlColorEncoding ColorEncoding => PixelFile.ColorEncoding;

        /// <summary>
        /// Gets the ICC profile of the decoded pixels.
        /// </summary>
        public byte[] IccProfile => PixelFile.Icc;

        /// <summary>
        /// Gets the original ICC profile embedded in the codestream metadata.
        /// </summary>
        public byte[] OriginalIccProfile => PixelFile.OrigIcc;

        /// <summary>
        /// Gets the decoded container metadata boxes (EXIF, XMP, JUMBF, IPTC, JHGM).
        /// </summary>
        public PackedMetadata Metadata => PixelFile.Metadata;

        /// <summary>
        /// Gets the byte-exact reconstructed JPEG bitstream when <see cref="JxlDecodeOptions.ReconstructJpegIfAvailable"/>
        /// was enabled and a <c>jbrd</c> box was present; otherwise <c>null</c>.
        /// </summary>
        public byte[]? ReconstructedJpegBytes { get; }

        /// <summary>
        /// Gets the number of bytes consumed from the input buffer.
        /// </summary>
        public nuint ConsumedBytes { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlDecodeResult"/> class.
        /// </summary>
        /// <param name="pixelFile">Decoded <see cref="PackedPixelFile"/>.</param>
        /// <param name="reconstructedJpegBytes">Optional reconstructed JPEG bytes.</param>
        /// <param name="consumedBytes">Number of input bytes consumed.</param>
        public JxlDecodeResult(
            PackedPixelFile pixelFile,
            byte[]? reconstructedJpegBytes = null,
            nuint consumedBytes = 0)
        {
            ArgumentNullException.ThrowIfNull(pixelFile);

            PixelFile              = pixelFile;
            ReconstructedJpegBytes = reconstructedJpegBytes;
            ConsumedBytes          = consumedBytes;
            PrimaryImage           = pixelFile.Frames.Count > 0
                ? JxlImageData.FromPackedPixelFile(pixelFile, 0)
                : null;
        }
    }
}
