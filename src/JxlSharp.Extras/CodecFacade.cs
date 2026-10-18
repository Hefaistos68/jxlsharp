// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Facade for detecting, querying, and decoding image formats into a <see cref="PackedPixelFile"/>.
    /// Ported from <c>lib/extras/dec/decode.h</c> and <c>lib/extras/dec/decode.cc</c>.
    /// </summary>
    public static class ExtrasDecodeFacade
    {
        /// <summary>
        /// Detects the image <see cref="Codec"/> from the leading magic bytes of <paramref name="bytes"/>.
        /// </summary>
        /// <param name="bytes">Input file bytes.</param>
        /// <returns>Detected <see cref="Codec"/>, or <see cref="Codec.Unknown"/> if unrecognized.</returns>
        public static Codec DetectCodec(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == (byte)'P')
            {
                if (bytes[1] == (byte)'G')
                {
                    return Codec.Pgx;
                }

                byte t = bytes[1];

                if ((t >= (byte)'1' && t <= (byte)'7') || t == (byte)'F' || t == (byte)'f')
                {
                    return Codec.Pnm;
                }
            }

            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return Codec.Png;
            }

            return DetectRemainingCodecs(bytes);
        }

        /// <summary>
        /// Checks whether the specified <paramref name="codec"/> can be decoded.
        /// Ported from <c>jxl::extras::CanDecode</c> in <c>lib/extras/dec/decode.cc</c>.
        /// </summary>
        /// <param name="codec">Codec to query.</param>
        /// <returns><c>true</c> if supported; otherwise <c>false</c>.</returns>
        public static bool CanDecode(Codec codec)
        {
            return codec switch
            {
                Codec.Exr => CodecExr.CanDecodeEXR(),
                Codec.Gif => CodecGif.CanDecodeGIF(),
                Codec.Jpg => CodecJpg.CanDecodeJPG(),
                Codec.Pnm => true,
                Codec.Pgx => true,
                Codec.Png => CodecApng.CanDecodeAPNG(),
                Codec.Jxl => true,
                _         => false,
            };
        }

        /// <summary>
        /// Returns a comma-separated list of codecs that can be decoded.
        /// Ported from <c>jxl::extras::ListOfDecodeCodecs</c> in <c>lib/extras/dec/decode.cc</c>.
        /// </summary>
        /// <returns>Human-readable codec list.</returns>
        public static string ListOfDecodeCodecs()
        {
            StringBuilder sb = new StringBuilder("PNM, PGX, JXL");

            if (CanDecode(Codec.Png))
            {
                sb.Append(", PNG, APNG");
            }

            if (CanDecode(Codec.Jpg))
            {
                sb.Append(", JPEG");
            }

            if (CanDecode(Codec.Gif))
            {
                sb.Append(", GIF");
            }

            if (CanDecode(Codec.Exr))
            {
                sb.Append(", EXR");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Resolves a <see cref="Codec"/> from the file extension of <paramref name="path"/>.
        /// Ported from <c>jxl::extras::CodecFromPath</c> in <c>lib/extras/dec/decode.cc</c>.
        /// </summary>
        /// <param name="path">File path or name.</param>
        /// <param name="bitsPerSample">Updated to 32 if the extension is <c>.pfm</c>; otherwise left unchanged.</param>
        /// <param name="extension">Receives the lowercase extension string (including leading dot).</param>
        /// <returns>Resolved <see cref="Codec"/>.</returns>
        public static Codec CodecFromPath(
            string path,
            ref nuint bitsPerSample,
            ref string extension)
        {
            int dotIndex = path.LastIndexOf('.');

            if (dotIndex < 0)
            {
                extension = string.Empty;

                return Codec.Unknown;
            }

            extension = path.Substring(dotIndex).ToLowerInvariant();

            return MapExtensionToCodec(extension, ref bitsPerSample);
        }

        /// <summary>
        /// Resolves a <see cref="Codec"/> from the file extension of <paramref name="path"/>.
        /// </summary>
        /// <param name="path">File path or name.</param>
        /// <returns>Resolved <see cref="Codec"/>.</returns>
        public static Codec CodecFromPath(string path)
        {
            nuint bitsPerSample = 0;
            string extension    = string.Empty;

            return CodecFromPath(path, ref bitsPerSample, ref extension);
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/> into <paramref name="ppf"/> using automatic format detection.
        /// Ported from <c>jxl::extras::DecodeBytes</c> in <c>lib/extras/dec/decode.cc</c>.
        /// </summary>
        /// <param name="bytes">Input encoded file bytes.</param>
        /// <param name="colorHints">Color space hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success; otherwise failure.</returns>
        public static Status DecodeBytes(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            return DecodeBytes(bytes, colorHints, ppf, constraints, out _, null, coalescing: true);
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/> into <paramref name="ppf"/> using automatic format detection.
        /// Ported from <c>jxl::extras::DecodeBytes</c> in <c>lib/extras/dec/decode.cc</c>.
        /// </summary>
        /// <param name="bytes">Input encoded file bytes.</param>
        /// <param name="colorHints">Color space hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <param name="origCodec">Receives the detected codec that decoded the image.</param>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="coalescing">Whether JXL animation/composite frames should be coalesced.</param>
        /// <returns><see cref="Status.Ok"/> on success; otherwise failure.</returns>
        public static Status DecodeBytes(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints,
            out Codec origCodec,
            JxlMemoryManager? memoryManager = null,
            bool coalescing = true)
        {
            origCodec = Codec.Unknown;

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo initInfo);
            ppf.Info = initInfo;

            Codec codec = TryDecodeNonJxl(bytes, colorHints, ppf, constraints);

            if (codec == Codec.Unknown &&
                TryDecodeJxlBytes(bytes, colorHints, ppf, constraints, memoryManager, coalescing))
            {
                codec = Codec.Jxl;
            }

            if (codec == Codec.Unknown)
            {
                return Status.Failure("Codecs failed to decode");
            }

            origCodec = codec;

            return Status.Ok;
        }

        private static Codec DetectRemainingCodecs(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 6 &&
                bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'8' &&
                (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
            {
                return Codec.Gif;
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return Codec.Jpg;
            }

            if (bytes.Length >= 4 &&
                bytes[0] == 0x76 && bytes[1] == 0x2F && bytes[2] == 0x31 && bytes[3] == 0x01)
            {
                return Codec.Exr;
            }

            JxlSignature sig = JxlDecoder.CheckSignature(bytes);

            if (sig == JxlSignature.Codestream || sig == JxlSignature.Container)
            {
                return Codec.Jxl;
            }

            return Codec.Unknown;
        }

        private static Codec MapExtensionToCodec(string extension, ref nuint bitsPerSample)
        {
            switch (extension)
            {
                case ".png":
                case ".apng":
                    return Codec.Png;

                case ".jpg":
                case ".jpeg":
                    return Codec.Jpg;

                case ".pgm":
                case ".pnm":
                case ".ppm":
                case ".pam":
                    return Codec.Pnm;

                case ".pfm":
                    bitsPerSample = 32;
                    return Codec.Pnm;

                case ".pgx":
                    return Codec.Pgx;

                case ".gif":
                    return Codec.Gif;

                case ".exr":
                    return Codec.Exr;

                case ".jxl":
                    return Codec.Jxl;

                default:
                    return Codec.Unknown;
            }
        }

        private static Codec TryDecodeNonJxl(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints)
        {
            if (CodecPnm.DecodeImagePNM(bytes, colorHints, ppf, constraints))
            {
                return Codec.Pnm;
            }

            if (CodecPgx.DecodeImagePGX(bytes, colorHints, ppf, constraints))
            {
                return Codec.Pgx;
            }

            if (CodecApng.DecodeImageAPNG(bytes, colorHints, ppf, constraints))
            {
                return Codec.Png;
            }

            if (CodecGif.DecodeImageGIF(bytes, colorHints, ppf, constraints))
            {
                return Codec.Gif;
            }

            if (CodecJpg.DecodeImageJPG(bytes, colorHints, ppf, constraints))
            {
                return Codec.Jpg;
            }

            if (CodecExr.DecodeImageEXR(bytes, colorHints, ppf, constraints))
            {
                return Codec.Exr;
            }

            return Codec.Unknown;
        }

        private static bool TryDecodeJxlBytes(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints,
            JxlMemoryManager? memoryManager,
            bool coalescing)
        {
            JXLDecompressParams dparams = new JXLDecompressParams
            {
                MemoryManager = memoryManager,
                Coalescing    = coalescing,
            };

            uint[] channels     = { 1u, 2u, 3u, 4u };
            JxlDataType[] types = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };

            foreach (uint c in channels)
            {
                foreach (JxlDataType dt in types)
                {
                    dparams.AcceptedFormats.Add(new JxlPixelFormat
                    {
                        NumChannels = c,
                        DataType    = dt,
                        Endianness  = JxlEndianness.NativeEndian,
                        Align       = 0,
                    });
                }
            }

            bool validHints = colorHints.Foreach((key, value) =>
            {
                if (key == "color_space")
                {
                    dparams.ColorSpace = value;
                }

                return Status.Ok;
            });

            if (!validHints)
            {
                return false;
            }

            return CodecJxl.DecodeImageJXL(bytes, dparams, out _, ppf, null, constraints);
        }
    }

    /// <summary>
    /// Facade for selecting format encoders by file extension.
    /// Ported from <c>lib/extras/enc/encode.h</c> and <c>lib/extras/enc/encode.cc</c>.
    /// </summary>
    public static class ExtrasEncodeFacade
    {
        /// <summary>
        /// Instantiates an <see cref="Encoder"/> matching <paramref name="extension"/> (case-insensitive).
        /// Ported from <c>jxl::extras::Encoder::FromExtension</c> in <c>lib/extras/enc/encode.cc</c>.
        /// </summary>
        /// <param name="extension">File extension including leading dot (e.g. <c>".ppm"</c>, <c>".png"</c>).</param>
        /// <returns>Matching <see cref="Encoder"/>, or <c>null</c> if unsupported.</returns>
        public static Encoder? FromExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }

            string ext = extension.ToLowerInvariant();

            return ext switch
            {
                ".png"  => CodecApng.GetAPNGEncoder(),
                ".apng" => CodecApng.GetAPNGEncoder(),
                ".jpg"  => CodecJpg.GetJPEGEncoder(),
                ".jpeg" => CodecJpg.GetJPEGEncoder(),
                ".npy"  => CodecNpy.GetNumPyEncoder(),
                ".pgx"  => CodecPgx.GetPGXEncoder(),
                ".pam"  => CodecPnm.GetPAMEncoder(),
                ".pgm"  => CodecPnm.GetPGMEncoder(),
                ".pnm"  => CodecPnm.GetPNMEncoder(),
                ".ppm"  => CodecPnm.GetPPMEncoder(),
                ".pfm"  => CodecPnm.GetPFMEncoder(),
                ".exr"  => CodecExr.GetEXREncoder(),
                _       => null,
            };
        }

        /// <summary>
        /// Returns a comma-separated list of file extensions supported by <see cref="FromExtension"/>.
        /// Ported from <c>jxl::extras::ListOfEncodeCodecs</c> in <c>lib/extras/enc/encode.cc</c>.
        /// </summary>
        /// <returns>Supported extensions string.</returns>
        public static string ListOfEncodeCodecs()
        {
            StringBuilder sb = new StringBuilder(".pam, .pgm, .pnm, .ppm, .pfm, .pgx, .npy");

            if (CodecApng.GetAPNGEncoder() is not null)
            {
                sb.Append(", .png, .apng");
            }

            if (CodecJpg.GetJPEGEncoder() is not null)
            {
                sb.Append(", .jpg, .jpeg");
            }

            if (CodecExr.GetEXREncoder() is not null)
            {
                sb.Append(", .exr");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// High-level helpers for decoding into <see cref="CodecInOut"/> and encoding from <see cref="PackedPixelFile"/> or <see cref="CodecInOut"/>.
    /// Ported from <c>lib/extras/codec.h</c> and <c>lib/extras/codec.cc</c>.
    /// </summary>
    public static class ExtrasCodec
    {
        /// <summary>
        /// Decodes <paramref name="bytes"/> into <paramref name="io"/> and reports the detected <paramref name="origCodec"/>.
        /// Ported from <c>jxl::SetFromBytes</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="bytes">Input encoded file bytes.</param>
        /// <param name="colorHints">Color space hints.</param>
        /// <param name="io">Destination <see cref="CodecInOut"/>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <param name="origCodec">Receives the detected codec.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status SetFromBytes(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            CodecInOut io,
            ThreadPool? pool,
            SizeConstraints? constraints,
            out Codec origCodec)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            Status decStatus    = ExtrasDecodeFacade.DecodeBytes(
                bytes,
                colorHints,
                ppf,
                constraints,
                out origCodec,
                io.MemoryManager);

            if (!decStatus)
            {
                return decStatus;
            }

            return PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool, io);
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/> into <paramref name="io"/> using <paramref name="colorHints"/>.
        /// Ported from <c>jxl::SetFromBytes</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="bytes">Input encoded file bytes.</param>
        /// <param name="colorHints">Color space hints.</param>
        /// <param name="io">Destination <see cref="CodecInOut"/>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status SetFromBytes(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            CodecInOut io,
            ThreadPool? pool = null,
            SizeConstraints? constraints = null)
        {
            return SetFromBytes(bytes, colorHints, io, pool, constraints, out _);
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/> into <paramref name="io"/> with empty color hints.
        /// Ported from <c>jxl::SetFromBytes</c> in <c>lib/extras/codec.h</c>.
        /// </summary>
        /// <param name="bytes">Input encoded file bytes.</param>
        /// <param name="io">Destination <see cref="CodecInOut"/>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status SetFromBytes(
            ReadOnlySpan<byte> bytes,
            CodecInOut io,
            ThreadPool? pool = null,
            SizeConstraints? constraints = null)
        {
            return SetFromBytes(bytes, new ColorHints(), io, pool, constraints, out _);
        }

        /// <summary>
        /// Encodes <paramref name="ppf"/> using the specified <paramref name="codec"/> into <paramref name="bytes"/>.
        /// Ported from <c>jxl::Encode</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="codec">Target codec.</param>
        /// <param name="bytes">Outputs the encoded bitstream bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status Encode(
            PackedPixelFile ppf,
            Codec codec,
            out byte[] bytes,
            ThreadPool? pool = null)
        {
            bytes             = Array.Empty<byte>();
            Encoder? encoder  = EncoderFromCodec(codec, ppf.Info.NumColorChannels == 1, ppf.Info.AlphaBits > 0, ppf.Info.ExponentBitsPerSample > 0);

            if (encoder is null)
            {
                return Status.Failure("Invalid codec");
            }

            EncodedImage encodedImage = new EncodedImage();
            Status encStatus          = encoder.Encode(ppf, encodedImage, pool);

            if (!encStatus)
            {
                return encStatus;
            }

            if (encodedImage.Bitstreams.Count == 0)
            {
                return Status.Failure("No bitstream produced");
            }

            bytes = encodedImage.Bitstreams[0];

            return Status.Ok;
        }

        /// <summary>
        /// Encodes <paramref name="ppf"/> into <paramref name="bytes"/> using the codec inferred from <paramref name="pathname"/>.
        /// Ported from <c>jxl::Encode</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="pathname">Target file path or extension.</param>
        /// <param name="bytes">Outputs the encoded bitstream bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status Encode(
            PackedPixelFile ppf,
            string pathname,
            out byte[] bytes,
            ThreadPool? pool = null)
        {
            bytes            = Array.Empty<byte>();
            nuint bits       = 0;
            string extension = string.Empty;
            Codec codec      = ExtrasDecodeFacade.CodecFromPath(pathname, ref bits, ref extension);
            Encoder? encoder = ExtrasEncodeFacade.FromExtension(extension);

            if (encoder is null)
            {
                return Encode(ppf, codec, out bytes, pool);
            }

            EncodedImage encodedImage = new EncodedImage();
            Status encStatus          = encoder.Encode(ppf, encodedImage, pool);

            if (!encStatus)
            {
                return encStatus;
            }

            if (encodedImage.Bitstreams.Count == 0)
            {
                return Status.Failure("No bitstream produced");
            }

            bytes = encodedImage.Bitstreams[0];

            return Status.Ok;
        }

        /// <summary>
        /// Converts <paramref name="io"/> to a <see cref="PackedPixelFile"/> and encodes it using <paramref name="codec"/>.
        /// Ported from <c>jxl::Encode</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="io">Source <see cref="CodecInOut"/>.</param>
        /// <param name="codec">Target codec.</param>
        /// <param name="cDesired">Desired output color encoding.</param>
        /// <param name="bitsPerSample">Target bits per sample.</param>
        /// <param name="bytes">Outputs the encoded bitstream bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status Encode(
            CodecInOut io,
            Codec codec,
            ColorEncoding cDesired,
            nuint bitsPerSample,
            out byte[] bytes,
            ThreadPool? pool = null)
        {
            bytes = Array.Empty<byte>();

            Status checkStatus = io.CheckMetadata();

            if (!checkStatus)
            {
                return checkStatus;
            }

            bool isGray       = cDesired.IsGray();
            bool hasAlpha     = io.Metadata.M.HasAlpha();
            bool isFloat      = io.Metadata.M.BitDepth.FloatingPointSample || (codec == Codec.Pnm && bitsPerSample == 32);
            Encoder? encoder  = EncoderFromCodec(codec, isGray, hasAlpha, isFloat);

            if (encoder is null)
            {
                return Status.Failure("Invalid codec");
            }

            Status fmtStatus = SelectCodecInOutFormat(io, encoder, isGray, hasAlpha, isFloat, bitsPerSample, out JxlPixelFormat format);

            if (!fmtStatus)
            {
                return fmtStatus;
            }

            PackedPixelFile ppf = new PackedPixelFile();
            Status convStatus   = PackedImageConvert.ConvertCodecInOutToPackedPixelFile(io, in format, cDesired, pool, ppf);

            if (!convStatus)
            {
                return convStatus;
            }

            ppf.Info.BitsPerSample         = (uint)bitsPerSample;
            ppf.Info.ExponentBitsPerSample = isFloat ? 8u : 0u;

            return EncodeWithEncoder(encoder, ppf, out bytes, pool);
        }

        /// <summary>
        /// Converts <paramref name="io"/> to a <see cref="PackedPixelFile"/> and encodes it based on <paramref name="pathname"/>.
        /// Ported from <c>jxl::Encode</c> in <c>lib/extras/codec.cc</c>.
        /// </summary>
        /// <param name="io">Source <see cref="CodecInOut"/>.</param>
        /// <param name="pathname">Target file path.</param>
        /// <param name="bytes">Outputs the encoded bitstream bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status Encode(
            CodecInOut io,
            string pathname,
            out byte[] bytes,
            ThreadPool? pool = null)
        {
            nuint bitsPerSample = io.Metadata.M.BitDepth.BitsPerSample;
            string extension    = string.Empty;
            Codec codec         = ExtrasDecodeFacade.CodecFromPath(pathname, ref bitsPerSample, ref extension);

            ColorEncoding cDesired = codec == Codec.Exr
                ? ColorEncoding.LinearSRGB(io.Main.IsGray())
                : io.Metadata.M.ColorEncoding;

            return Encode(io, codec, cDesired, bitsPerSample, out bytes, pool);
        }

        private static Encoder? EncoderFromCodec(
            Codec codec,
            bool isGray,
            bool hasAlpha,
            bool floatingPoint)
        {
            switch (codec)
            {
                case Codec.Png:
                    return CodecApng.GetAPNGEncoder();

                case Codec.Jpg:
                    return CodecJpg.GetJPEGEncoder();

                case Codec.Pnm:
                    if (floatingPoint)
                    {
                        return CodecPnm.GetPFMEncoder();
                    }

                    if (hasAlpha)
                    {
                        return CodecPnm.GetPAMEncoder();
                    }

                    return isGray ? CodecPnm.GetPGMEncoder() : CodecPnm.GetPPMEncoder();

                case Codec.Pgx:
                    return CodecPgx.GetPGXEncoder();

                case Codec.Exr:
                    return CodecExr.GetEXREncoder();

                default:
                    return null;
            }
        }

        private static Status SelectCodecInOutFormat(
            CodecInOut io,
            Encoder encoder,
            bool isGray,
            bool hasAlpha,
            bool isFloat,
            nuint bitsPerSample,
            out JxlPixelFormat format)
        {
            JxlBasicInfo info          = default;
            JxlEncoder.JxlEncoderInitBasicInfo(out info);
            info.BitsPerSample         = (uint)bitsPerSample;
            info.ExponentBitsPerSample = isFloat ? 8u : 0u;
            info.NumColorChannels      = isGray ? 1u : 3u;
            info.AlphaBits             = hasAlpha ? (uint)bitsPerSample : 0u;

            return ExtrasCommon.SelectFormat(encoder.AcceptedFormats(), in info, out format);
        }

        private static Status EncodeWithEncoder(
            Encoder encoder,
            PackedPixelFile ppf,
            out byte[] bytes,
            ThreadPool? pool)
        {
            bytes                     = Array.Empty<byte>();
            EncodedImage encodedImage = new EncodedImage();
            Status status             = encoder.Encode(ppf, encodedImage, pool);

            if (!status)
            {
                return status;
            }

            if (encodedImage.Bitstreams.Count == 0)
            {
                return Status.Failure("No bitstream produced");
            }

            bytes = encodedImage.Bitstreams[0];

            return Status.Ok;
        }
    }
}
