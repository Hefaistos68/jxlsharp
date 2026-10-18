// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Parsed PNM/PAM/PFM header metadata.
    /// Ported from <c>jxl::extras::HeaderPNM</c> in <c>lib/extras/dec/pnm.h</c>.
    /// </summary>
    public sealed class HeaderPNM
    {
        /// <summary>Gets or sets the image width in pixels.</summary>
        public nuint XSize { get; set; }

        /// <summary>Gets or sets the image height in pixels.</summary>
        public nuint YSize { get; set; }

        /// <summary>Gets or sets a value indicating whether the image is grayscale.</summary>
        public bool IsGray { get; set; }

        /// <summary>Gets or sets a value indicating whether the image has an alpha channel.</summary>
        public bool HasAlpha { get; set; }

        /// <summary>Gets or sets the number of bits per sample.</summary>
        public nuint BitsPerSample { get; set; }

        /// <summary>Gets or sets a value indicating whether samples are 32-bit IEEE floats (PFM).</summary>
        public bool FloatingPoint { get; set; }

        /// <summary>Gets or sets a value indicating whether multi-byte samples are big-endian.</summary>
        public bool BigEndian { get; set; }

        /// <summary>Gets the extra channel types declared in PAM headers.</summary>
        public List<JxlExtraChannelType> EcTypes { get; } = new List<JxlExtraChannelType>();
    }

    /// <summary>
    /// PNM (PBM/PGM/PPM/PAM/PFM) image decoder and encoder implementations.
    /// Ported from <c>lib/extras/dec/pnm.cc</c> and <c>lib/extras/enc/pnm.cc</c>.
    /// </summary>
    public static class CodecPnm
    {
        /// <summary>
        /// Decodes a binary PNM/PAM/PFM image from <paramref name="bytes"/> into <paramref name="ppf"/>.
        /// Ported from <c>jxl::extras::DecodeImagePNM</c> in <c>lib/extras/dec/pnm.cc</c>.
        /// </summary>
        /// <param name="bytes">Input file bytes.</param>
        /// <param name="colorHints">Color space hints (defaults to sRGB if unspecified).</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success; otherwise failure.</returns>
        public static Status DecodeImagePNM(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            if (bytes.Length < 2)
            {
                return false;
            }

            PnmParser parser = new PnmParser(bytes);
            HeaderPNM header = new HeaderPNM();

            if (!parser.ParseHeader(header, out int pos))
            {
                return false;
            }

            Status dimStatus = ExtrasCommon.VerifyDimensions(constraints, header.XSize, header.YSize);

            if (!dimStatus)
            {
                return dimStatus;
            }

            if (header.BitsPerSample == 0 || header.BitsPerSample > 32)
            {
                return Status.Failure("PNM: bits_per_sample invalid");
            }

            Status hintStatus = ColorHintsHelper.ApplyColorHints(colorHints, colorAlreadySet: false, header.IsGray, ppf);

            if (!hintStatus)
            {
                return hintStatus;
            }

            PopulatePnmBasicInfo(header, ppf);

            return DecodePnmPixels(bytes.Slice(pos), header, ppf);
        }

        /// <summary>
        /// Parses an ASCII signed floating-point number using the PNM header parser.
        /// Exposed for testing (<c>PnmParseSigned</c> in <c>lib/extras/dec/pnm.cc</c>).
        /// </summary>
        /// <param name="str">ASCII byte span.</param>
        /// <param name="v">Parsed double value.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status PnmParseSigned(ReadOnlySpan<byte> str, out double v)
        {
            PnmParser parser = new PnmParser(str);

            return parser.ParseSigned(out v);
        }

        /// <summary>
        /// Parses an ASCII unsigned integer using the PNM header parser.
        /// Exposed for testing (<c>PnmParseUnsigned</c> in <c>lib/extras/dec/pnm.cc</c>).
        /// </summary>
        /// <param name="str">ASCII byte span.</param>
        /// <param name="v">Parsed unsigned integer value.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public static Status PnmParseUnsigned(ReadOnlySpan<byte> str, out nuint v)
        {
            PnmParser parser = new PnmParser(str);

            return parser.ParseUnsigned(out v);
        }

        /// <summary>Creates a PAM (<c>.pam</c>) encoder.</summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPAMEncoder() => new PamEncoder();

        /// <summary>Creates a PGM (<c>.pgm</c>) encoder.</summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPGMEncoder() => new PgmEncoder();

        /// <summary>Creates a generic PNM (<c>.pnm</c>) encoder.</summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPNMEncoder() => new PnmGenericEncoder();

        /// <summary>Creates a PPM (<c>.ppm</c>) encoder.</summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPPMEncoder() => new PpmEncoder();

        /// <summary>Creates a PFM (<c>.pfm</c>) encoder.</summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPFMEncoder() => new PfmEncoder();

        private static void PopulatePnmBasicInfo(HeaderPNM header, PackedPixelFile ppf)
        {
            uint bitsPerSample         = header.FloatingPoint ? 32u : (uint)header.BitsPerSample;
            uint exponentBitsPerSample = header.FloatingPoint ? 8u : 0u;
            uint numAlphaChannels      = header.HasAlpha ? 1u : 0u;
            uint numColorChannels      = header.IsGray ? 1u : 3u;

            JxlBasicInfo info          = ppf.Info;
            info.XSize                 = (uint)header.XSize;
            info.YSize                 = (uint)header.YSize;
            info.BitsPerSample         = bitsPerSample;
            info.ExponentBitsPerSample = exponentBitsPerSample;
            info.Orientation           = JxlOrientation.Identity;
            info.AlphaBits             = header.HasAlpha ? bitsPerSample : 0u;
            info.AlphaExponentBits     = 0u;
            info.NumColorChannels      = numColorChannels;
            info.NumExtraChannels      = numAlphaChannels + (uint)header.EcTypes.Count;
            ppf.Info                   = info;

            ppf.ExtraChannelsInfo.Clear();

            for (int i = 0; i < header.EcTypes.Count; i++)
            {
                PackedExtraChannel pec = new PackedExtraChannel();
                JxlExtraChannelInfo ec = pec.EcInfo;
                ec.BitsPerSample       = bitsPerSample;
                ec.Type                = header.EcTypes[i];
                pec.EcInfo             = ec;
                ppf.ExtraChannelsInfo.Add(pec);
            }
        }

        private static Status DecodePnmPixels(
            ReadOnlySpan<byte> payload,
            HeaderPNM header,
            PackedPixelFile ppf)
        {
            JxlDataType dataType  = SelectPnmDataType(header);
            nuint twidth          = PackedImage.BitsPerChannel(dataType) / Common.KBitsPerByte;
            uint numAlpha         = header.HasAlpha ? 1u : 0u;
            uint numInterleaved   = ppf.Info.NumColorChannels + numAlpha;
            JxlEndianness endian  = header.BigEndian ? JxlEndianness.BigEndian : JxlEndianness.LittleEndian;
            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = numInterleaved,
                DataType    = dataType,
                Endianness  = endian,
                Align       = 0,
            };

            nuint totalChannels = numInterleaved + (nuint)header.EcTypes.Count;

            if (!Common.SafeMul(header.XSize, totalChannels, out nuint requiredSize) ||
                !Common.SafeMul(requiredSize, twidth, out requiredSize) ||
                !Common.SafeMul(requiredSize, header.YSize, out requiredSize))
            {
                return Status.Failure("PNM image dimensions are too large");
            }

            if ((nuint)payload.Length < requiredSize)
            {
                return Status.Failure("PNM file too small");
            }

            Status allocStatus = AllocatePnmFrame(header, in format, out PackedFrame? frame);

            if (!allocStatus || frame is null)
            {
                return allocStatus;
            }

            ppf.Frames.Clear();
            ppf.Frames.Add(frame);

            Status copyStatus = CopyPnmFramePixels(payload, header, frame, dataType, twidth, numInterleaved);

            if (!copyStatus)
            {
                return copyStatus;
            }

            if (ppf.Info.ExponentBitsPerSample == 0)
            {
                JxlBitDepth bitDepth = ppf.InputBitdepth;
                bitDepth.Type        = JxlBitDepthType.FromCodestream;
                ppf.InputBitdepth    = bitDepth;
            }

            return Status.Ok;
        }

        private static JxlDataType SelectPnmDataType(HeaderPNM header)
        {
            if (header.FloatingPoint)
            {
                return JxlDataType.Float;
            }

            return header.BitsPerSample > 8 ? JxlDataType.UInt16 : JxlDataType.UInt8;
        }

        private static Status AllocatePnmFrame(
            HeaderPNM header,
            in JxlPixelFormat format,
            out PackedFrame? frame)
        {
            StatusOr<PackedFrame> frameResult = PackedFrame.Create(header.XSize, header.YSize, in format);

            if (!frameResult.IsOk)
            {
                frame = null;

                return frameResult.Status;
            }

            frame = frameResult.Value;

            JxlPixelFormat ecFormat = new JxlPixelFormat
            {
                NumChannels = 1,
                DataType    = format.DataType,
                Endianness  = format.Endianness,
                Align       = 0,
            };

            for (int i = 0; i < header.EcTypes.Count; i++)
            {
                StatusOr<PackedImage> ecResult = PackedImage.Create(header.XSize, header.YSize, in ecFormat);

                if (!ecResult.IsOk)
                {
                    return ecResult.Status;
                }

                frame.ExtraChannels.Add(ecResult.Value);
            }

            return Status.Ok;
        }

        private static Status CopyPnmFramePixels(
            ReadOnlySpan<byte> payload,
            HeaderPNM header,
            PackedFrame frame,
            JxlDataType dataType,
            nuint twidth,
            uint numInterleaved)
        {
            Span<byte> colorOut = frame.Color.Pixels();

            if (frame.ExtraChannels.Count == 0)
            {
                bool flippedY   = header.BitsPerSample == 32;
                int rowStride   = (int)frame.Color.Stride;
                int totalPixels = (int)header.YSize * rowStride;

                if (!flippedY)
                {
                    payload.Slice(0, totalPixels).CopyTo(colorOut);

                    return Status.Ok;
                }

                for (int y = 0; y < (int)header.YSize; y++)
                {
                    int yOut = (int)header.YSize - 1 - y;
                    payload.Slice(y * rowStride, rowStride).CopyTo(colorOut.Slice(yOut * rowStride, rowStride));
                }

                return Status.Ok;
            }

            Status validStatus = PackedImage.ValidateDataType(dataType);

            if (!validStatus)
            {
                return validStatus;
            }

            DeinterleavePnmExtraChannels(payload, header, frame, (int)twidth, (int)(twidth * numInterleaved));

            return Status.Ok;
        }

        private static void DeinterleavePnmExtraChannels(
            ReadOnlySpan<byte> payload,
            HeaderPNM header,
            PackedFrame frame,
            int twidth,
            int colorStride)
        {
            Span<byte> colorOut = frame.Color.Pixels();
            int ecCount         = frame.ExtraChannels.Count;
            int totalPixels     = (int)(header.XSize * header.YSize);
            int inOffset        = 0;
            int colorOffset     = 0;
            int ecOffset        = 0;

            for (int p = 0; p < totalPixels; p++)
            {
                payload.Slice(inOffset, colorStride).CopyTo(colorOut.Slice(colorOffset, colorStride));
                inOffset    += colorStride;
                colorOffset += colorStride;

                for (int e = 0; e < ecCount; e++)
                {
                    Span<byte> ecSpan = frame.ExtraChannels[e].Pixels();
                    payload.Slice(inOffset, twidth).CopyTo(ecSpan.Slice(ecOffset, twidth));
                    inOffset += twidth;
                }

                ecOffset += twidth;
            }
        }

        private ref struct PnmParser
        {
            private readonly ReadOnlySpan<byte> _bytes;
            private int _pos;

            public PnmParser(ReadOnlySpan<byte> bytes)
            {
                _bytes = bytes;
                _pos   = 0;
            }

            public Status ParseHeader(HeaderPNM header, out int dataStartPos)
            {
                dataStartPos = 0;

                if (_bytes.Length < 2 || _bytes[0] != (byte)'P')
                {
                    return false;
                }

                byte type = _bytes[1];
                _pos      = 2;

                Status status = DispatchHeaderByType(type, header);

                if (!status)
                {
                    return status;
                }

                dataStartPos = _pos;

                return Status.Ok;
            }

            public Status ParseUnsigned(out nuint number)
            {
                number = 0;

                if (_pos == _bytes.Length || !IsDigit(_bytes[_pos]))
                {
                    return Status.Failure("PNM: expected unsigned number");
                }

                while (_pos < _bytes.Length && IsDigit(_bytes[_pos]))
                {
                    if (!Common.SafeMul(number, 10, out number) ||
                        !Common.SafeAdd(number, (nuint)(_bytes[_pos] - (byte)'0'), out number))
                    {
                        return Status.Failure("PNM: unsigned overflow");
                    }

                    _pos++;
                }

                return Status.Ok;
            }

            public Status ParseSigned(out double number)
            {
                number = 0.0;

                if (_pos == _bytes.Length)
                {
                    return Status.Failure("PNM: reached end before signed");
                }

                bool isNeg = _bytes[_pos] == (byte)'-';

                if (isNeg || _bytes[_pos] == (byte)'+')
                {
                    _pos++;

                    if (_pos == _bytes.Length)
                    {
                        return Status.Failure("PNM: reached end before digits");
                    }
                }

                int numDigits = ParseIntegerDigits(ref number) + ParseFractionDigits(ref number);

                if (numDigits == 0)
                {
                    return Status.Failure("PNM: no digits in signed");
                }

                if (isNeg)
                {
                    number = -number;
                }

                return Status.Ok;
            }

            private int ParseIntegerDigits(ref double number)
            {
                int count = 0;

                while (_pos < _bytes.Length && IsDigit(_bytes[_pos]))
                {
                    count++;
                    number *= 10.0;
                    number += _bytes[_pos] - (byte)'0';
                    _pos++;
                }

                return count;
            }

            private int ParseFractionDigits(ref double number)
            {
                if (_pos >= _bytes.Length || _bytes[_pos] != (byte)'.')
                {
                    return 0;
                }

                _pos++;
                int count    = 0;
                double place = 0.1;

                while (_pos < _bytes.Length && IsDigit(_bytes[_pos]))
                {
                    count++;
                    number += (_bytes[_pos] - (byte)'0') * place;
                    place  *= 0.1;
                    _pos++;
                }

                return count;
            }

            private Status DispatchHeaderByType(byte type, HeaderPNM header)
            {
                switch (type)
                {
                    case (byte)'1':
                    case (byte)'2':
                    case (byte)'3':
                        return Status.Failure("ascii pnm not supported");

                    case (byte)'4':
                        header.IsGray = true;

                        return ParseHeaderPBM(header);

                    case (byte)'5':
                        header.IsGray = true;

                        return ParseHeaderPNM(header);

                    case (byte)'6':
                        header.IsGray = false;

                        return ParseHeaderPNM(header);

                    case (byte)'7':
                        return ParseHeaderPAM(header);

                    case (byte)'F':
                        header.IsGray = false;

                        return ParseHeaderPFM(header);

                    case (byte)'f':
                        header.IsGray = true;

                        return ParseHeaderPFM(header);

                    default:
                        return false;
                }
            }

            private Status ParseHeaderPNM(HeaderPNM header)
            {
                if (!SkipBlank() ||
                    !ParseUnsigned(out nuint xsize) ||
                    !SkipBlank() ||
                    !ParseUnsigned(out nuint ysize) ||
                    !SkipBlank() ||
                    !ParseUnsigned(out nuint maxVal) ||
                    !SkipSingleWhitespace())
                {
                    return false;
                }

                if (maxVal == 0 || maxVal >= 65536)
                {
                    return Status.Failure("PNM: bad MaxVal");
                }

                nuint bitsPerSample = (nuint)(Bits.FloorLog2Nonzero((uint)maxVal) + 1);

                if (((1u << (int)bitsPerSample) - 1u) != (uint)maxVal)
                {
                    return Status.Failure("PNM: unsupported MaxVal (expected 2^n - 1)");
                }

                header.XSize         = xsize;
                header.YSize         = ysize;
                header.BitsPerSample = bitsPerSample;
                header.FloatingPoint = false;
                header.BigEndian     = true;

                return Status.Ok;
            }

            private Status ParseHeaderPBM(HeaderPNM header)
            {
                if (!SkipBlank() ||
                    !ParseUnsigned(out nuint xsize) ||
                    !SkipBlank() ||
                    !ParseUnsigned(out nuint ysize) ||
                    !SkipSingleWhitespace())
                {
                    return false;
                }

                header.XSize         = xsize;
                header.YSize         = ysize;
                header.BitsPerSample = 1;
                header.FloatingPoint = false;
                header.BigEndian     = true;

                return Status.Ok;
            }

            private Status ParseHeaderPFM(HeaderPNM header)
            {
                if (!SkipSingleWhitespace() ||
                    !ParseUnsigned(out nuint xsize) ||
                    !SkipBlank() ||
                    !ParseUnsigned(out nuint ysize) ||
                    !SkipSingleWhitespace() ||
                    !ParseSigned(out double scale) ||
                    !SkipSingleWhitespace())
                {
                    return false;
                }

                if (scale == 0.0 || double.IsInfinity(scale) || double.IsNaN(scale))
                {
                    return Status.Failure("PFM: bad scale value");
                }

                header.XSize         = xsize;
                header.YSize         = ysize;
                header.BitsPerSample = 32;
                header.FloatingPoint = true;
                header.BigEndian     = scale > 0.0;

                return Status.Ok;
            }

            private Status ParseHeaderPAM(HeaderPNM header)
            {
                nuint depth       = 1;
                nuint maxVal      = 255;
                bool seenTupltype = false;
                bool isBw         = false;

                while (_pos < _bytes.Length)
                {
                    SkipWhitespace();

                    if (_pos == _bytes.Length)
                    {
                        return false;
                    }

                    if (MatchString("ENDHDR"))
                    {
                        if (!SkipSingleWhitespace())
                        {
                            return false;
                        }

                        return FinalizePamHeader(header, depth, maxVal, seenTupltype, isBw);
                    }

                    Status lineStatus = ParsePamHeaderField(header, ref depth, ref maxVal, ref seenTupltype, ref isBw);

                    if (!lineStatus)
                    {
                        return lineStatus;
                    }
                }

                return false;
            }

            private Status ParsePamHeaderField(
                HeaderPNM header,
                ref nuint depth,
                ref nuint maxVal,
                ref bool seenTupltype,
                ref bool isBw)
            {
                if (_bytes[_pos] == (byte)'#')
                {
                    SkipLine();

                    return Status.Ok;
                }

                Status fieldStatus = TryMatchPamKeyword(header, ref depth, ref maxVal, ref seenTupltype, ref isBw);

                if (!fieldStatus)
                {
                    return fieldStatus;
                }

                SkipLine();

                return Status.Ok;
            }

            private Status TryMatchPamKeyword(
                HeaderPNM header,
                ref nuint depth,
                ref nuint maxVal,
                ref bool seenTupltype,
                ref bool isBw)
            {
                if (MatchString("WIDTH"))
                {
                    if (!SkipFluidWhitespace() || !ParseUnsigned(out nuint xsize))
                    {
                        return false;
                    }

                    header.XSize = xsize;

                    return Status.Ok;
                }

                if (MatchString("HEIGHT"))
                {
                    if (!SkipFluidWhitespace() || !ParseUnsigned(out nuint ysize))
                    {
                        return false;
                    }

                    header.YSize = ysize;

                    return Status.Ok;
                }

                if (MatchString("DEPTH"))
                {
                    return SkipFluidWhitespace() && ParseUnsigned(out depth);
                }

                if (MatchString("MAXVAL"))
                {
                    return SkipFluidWhitespace() && ParseUnsigned(out maxVal);
                }

                if (MatchString("TUPLTYPE"))
                {
                    return SkipFluidWhitespace() && ParsePamTuplType(header, ref seenTupltype, ref isBw);
                }

                return Status.Failure("PAM: unknown header keyword");
            }

            private Status ParsePamTuplType(HeaderPNM header, ref bool seenTupltype, ref bool isBw)
            {
                if (MatchString("BLACKANDWHITE_ALPHA"))
                {
                    seenTupltype    = true;
                    isBw            = true;
                    header.IsGray   = true;
                    header.HasAlpha = true;

                    return Status.Ok;
                }

                if (MatchString("BLACKANDWHITE"))
                {
                    seenTupltype  = true;
                    isBw          = true;
                    header.IsGray = true;

                    return Status.Ok;
                }

                if (MatchString("GRAYSCALE_ALPHA"))
                {
                    seenTupltype    = true;
                    header.IsGray   = true;
                    header.HasAlpha = true;

                    return Status.Ok;
                }

                if (MatchString("GRAYSCALE"))
                {
                    seenTupltype  = true;
                    header.IsGray = true;

                    return Status.Ok;
                }

                if (MatchString("RGB_ALPHA"))
                {
                    seenTupltype    = true;
                    header.IsGray   = false;
                    header.HasAlpha = true;

                    return Status.Ok;
                }

                if (MatchString("RGB"))
                {
                    seenTupltype  = true;
                    header.IsGray = false;

                    return Status.Ok;
                }

                return ParsePamExtraTuplType(header);
            }

            private Status ParsePamExtraTuplType(HeaderPNM header)
            {
                if (MatchString("ALPHA"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Alpha);

                    return Status.Ok;
                }

                if (MatchString("DEPTH"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Depth);

                    return Status.Ok;
                }

                if (MatchString("SPOT_COLOR"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.SpotColor);

                    return Status.Ok;
                }

                if (MatchString("SELECTION_MASK"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.SelectionMask);

                    return Status.Ok;
                }

                if (MatchString("BLACK"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Black);

                    return Status.Ok;
                }

                if (MatchString("CFA"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Cfa);

                    return Status.Ok;
                }

                if (MatchString("THERMAL"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Thermal);

                    return Status.Ok;
                }

                if (MatchString("Optional"))
                {
                    header.EcTypes.Add(JxlExtraChannelType.Optional);

                    return Status.Ok;
                }

                return Status.Failure("PAM: unknown tuple type");
            }

            private static Status FinalizePamHeader(
                HeaderPNM header,
                nuint depth,
                nuint maxVal,
                bool seenTupltype,
                bool isBw)
            {
                if (!seenTupltype && !InferPamTuplTypeFromDepth(header, depth))
                {
                    return Status.Failure("PAM: invalid depth without TUPLTYPE");
                }

                nuint numChannels = (header.IsGray ? 1u : 3u) + (header.HasAlpha ? 1u : 0u);

                if (numChannels > depth)
                {
                    return Status.Failure("PAM: invalid depth");
                }

                while ((nuint)header.EcTypes.Count < depth - numChannels)
                {
                    header.EcTypes.Add(JxlExtraChannelType.Optional);
                }

                if (maxVal == 0 || maxVal >= 65536 || (isBw && maxVal != 1))
                {
                    return Status.Failure("PAM: bad MaxVal");
                }

                header.BitsPerSample = (nuint)(Bits.FloorLog2Nonzero((uint)maxVal) + 1);
                header.FloatingPoint = false;
                header.BigEndian     = true;

                return Status.Ok;
            }

            private static bool InferPamTuplTypeFromDepth(HeaderPNM header, nuint depth)
            {
                if (depth == 1)
                {
                    header.IsGray = true;

                    return true;
                }

                if (depth == 2)
                {
                    header.IsGray   = true;
                    header.HasAlpha = true;

                    return true;
                }

                if (depth == 3)
                {
                    header.IsGray = false;

                    return true;
                }

                if (depth == 4)
                {
                    header.IsGray   = false;
                    header.HasAlpha = true;

                    return true;
                }

                return false;
            }

            private bool MatchString(string keyword, bool skipTrailingFluidWhitespace = false)
            {
                if (_pos + keyword.Length > _bytes.Length)
                {
                    return false;
                }

                for (int i = 0; i < keyword.Length; i++)
                {
                    if (_bytes[_pos + i] != (byte)keyword[i])
                    {
                        return false;
                    }
                }

                _pos += keyword.Length;

                if (skipTrailingFluidWhitespace)
                {
                    SkipFluidWhitespace();
                }

                return true;
            }

            private void SkipLine()
            {
                while (_pos < _bytes.Length && !IsLineBreak(_bytes[_pos]))
                {
                    _pos++;
                }

                if (_pos < _bytes.Length)
                {
                    _pos++;
                }
            }

            private void SkipWhitespace()
            {
                while (_pos < _bytes.Length && IsWhitespace(_bytes[_pos]))
                {
                    _pos++;
                }
            }

            private Status SkipFluidWhitespace()
            {
                int start = _pos;

                while (_pos < _bytes.Length && IsFluidWhitespace(_bytes[_pos]))
                {
                    _pos++;
                }

                return _pos > start ? Status.Ok : Status.Failure("PNM: expected fluid whitespace");
            }

            private Status SkipBlank()
            {
                if (_pos == _bytes.Length)
                {
                    return Status.Failure("PNM: reached end before blank");
                }

                int start = _pos;

                while (_pos < _bytes.Length && (IsWhitespace(_bytes[_pos]) || _bytes[_pos] == (byte)'#'))
                {
                    if (_bytes[_pos] == (byte)'#')
                    {
                        SkipLine();
                    }
                    else
                    {
                        _pos++;
                    }
                }

                return _pos > start ? Status.Ok : Status.Failure("PNM: expected blank");
            }

            private Status SkipSingleWhitespace()
            {
                if (_pos == _bytes.Length || !IsWhitespace(_bytes[_pos]))
                {
                    return Status.Failure("PNM: expected whitespace");
                }

                _pos++;

                return Status.Ok;
            }

            private static bool IsDigit(byte c) => c >= (byte)'0' && c <= (byte)'9';

            private static bool IsLineBreak(byte c) => c == (byte)'\r' || c == (byte)'\n';

            private static bool IsFluidWhitespace(byte c) => c == (byte)' ' || c == (byte)'\t';

            private static bool IsWhitespace(byte c) => IsLineBreak(c) || IsFluidWhitespace(c);
        }

        private abstract class PnmEncoderBase : Encoder
        {
            public override Status Encode(PackedPixelFile ppf, EncodedImage encodedImage, ThreadPool? pool = null)
            {
                Status infoStatus = VerifyBasicInfo(ppf.Info);

                if (!infoStatus)
                {
                    return infoStatus;
                }

                if (ppf.ColorEncoding.TransferFunction == JxlTransferFunction.Linear)
                {
                    encodedImage.Icc = ppf.Icc;
                }

                encodedImage.Bitstreams.Clear();

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    Status frameStatus = EncodeFrame(ppf, ppf.Frames[i], out byte[] frameBytes);

                    if (!frameStatus)
                    {
                        return frameStatus;
                    }

                    encodedImage.Bitstreams.Add(frameBytes);
                }

                return EncodeExtraChannelStreams(ppf, encodedImage);
            }

            protected virtual Status EncodeExtraChannelStreams(PackedPixelFile ppf, EncodedImage encodedImage)
            {
                return Status.Ok;
            }

            protected abstract Status EncodeFrame(PackedPixelFile ppf, PackedFrame frame, out byte[] bytes);

            protected static byte[] CombineHeaderAndPixels(string headerText, ReadOnlySpan<byte> pixels, bool flipY, int stride, int ysize)
            {
                byte[] headerBytes = Encoding.ASCII.GetBytes(headerText);
                byte[] output      = new byte[headerBytes.Length + (stride * ysize)];
                Buffer.BlockCopy(headerBytes, 0, output, 0, headerBytes.Length);
                Span<byte> dest = output.AsSpan(headerBytes.Length);

                if (!flipY)
                {
                    pixels.Slice(0, stride * ysize).CopyTo(dest);

                    return output;
                }

                for (int y = 0; y < ysize; y++)
                {
                    int srcY = ysize - 1 - y;
                    pixels.Slice(srcY * stride, stride).CopyTo(dest.Slice(y * stride, stride));
                }

                return output;
            }
        }

        private class PnmGenericEncoder : PnmEncoderBase
        {
            private static readonly JxlPixelFormat[] Formats = BuildFormats();

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;

            protected override Status EncodeFrame(PackedPixelFile ppf, PackedFrame frame, out byte[] bytes)
            {
                return EncodePnmImage(frame.Color, ppf.Info.BitsPerSample, out bytes);
            }

            protected override Status EncodeExtraChannelStreams(PackedPixelFile ppf, EncodedImage encodedImage)
            {
                encodedImage.ExtraChannelBitstreams.Clear();

                for (int ecIdx = 0; ecIdx < ppf.ExtraChannelsInfo.Count; ecIdx++)
                {
                    List<byte[]> ecStreams = new List<byte[]>();
                    uint ecBits            = ppf.ExtraChannelsInfo[ecIdx].EcInfo.BitsPerSample;

                    for (int fIdx = 0; fIdx < ppf.Frames.Count; fIdx++)
                    {
                        PackedImage ecImage = ppf.Frames[fIdx].ExtraChannels[ecIdx];
                        Status ecStatus     = EncodePnmImage(ecImage, ecBits, out byte[] ecBytes);

                        if (!ecStatus)
                        {
                            return ecStatus;
                        }

                        ecStreams.Add(ecBytes);
                    }

                    encodedImage.ExtraChannelBitstreams.Add(ecStreams);
                }

                return Status.Ok;
            }

            protected Status EncodePnmImage(PackedImage color, uint bitsPerSample, out byte[] bytes)
            {
                bytes = Array.Empty<byte>();

                Status fmtStatus = VerifyFormat(color.Format);

                if (!fmtStatus)
                {
                    return fmtStatus;
                }

                if (color.Format.DataType == JxlDataType.Float)
                {
                    return EncodePfmImage(color, out bytes);
                }

                if (bitsPerSample == 0 || bitsPerSample > 16)
                {
                    return Status.Failure("Invalid bits_per_sample");
                }

                uint maxval       = (1u << (int)bitsPerSample) - 1u;
                char typeChar     = color.Format.NumChannels == 1 ? '5' : '6';
                string headerText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"P{typeChar}\n{color.XSize} {color.YSize}\n{maxval}\n");

                bytes = CombineHeaderAndPixels(
                    headerText,
                    color.Pixels(),
                    flipY: false,
                    (int)color.Stride,
                    (int)color.YSize);

                return Status.Ok;
            }

            protected static Status EncodePfmImage(PackedImage color, out byte[] bytes)
            {
                char typeChar     = color.Format.NumChannels == 1 ? 'f' : 'F';
                string scaleStr   = color.Format.Endianness == JxlEndianness.LittleEndian ? "-1.0" : "1.0";
                string headerText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"P{typeChar}\n{color.XSize} {color.YSize}\n{scaleStr}\n");

                bytes = CombineHeaderAndPixels(
                    headerText,
                    color.Pixels(),
                    flipY: true,
                    (int)color.Stride,
                    (int)color.YSize);

                return Status.Ok;
            }

            private static JxlPixelFormat[] BuildFormats()
            {
                List<JxlPixelFormat> list = new List<JxlPixelFormat>();
                uint[] channels           = { 1u, 3u };
                JxlDataType[] types       = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };
                JxlEndianness[] endians   = { JxlEndianness.BigEndian, JxlEndianness.LittleEndian };

                foreach (uint c in channels)
                {
                    foreach (JxlDataType dt in types)
                    {
                        foreach (JxlEndianness end in endians)
                        {
                            if (dt == JxlDataType.UInt16 && end == JxlEndianness.LittleEndian)
                            {
                                continue;
                            }

                            list.Add(new JxlPixelFormat { NumChannels = c, DataType = dt, Endianness = end, Align = 0 });
                        }
                    }
                }

                return list.ToArray();
            }
        }

        private sealed class PgmEncoder : PnmGenericEncoder
        {
            private static readonly JxlPixelFormat[] Formats =
            {
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.BigEndian, Align = 0 },
            };

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;
        }

        private sealed class PpmEncoder : PnmGenericEncoder
        {
            private static readonly JxlPixelFormat[] Formats =
            {
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.BigEndian, Align = 0 },
            };

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;
        }

        private sealed class PfmEncoder : PnmGenericEncoder
        {
            private static readonly JxlPixelFormat[] Formats =
            {
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
            };

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;
        }

        private sealed class PamEncoder : PnmEncoderBase
        {
            private static readonly JxlPixelFormat[] Formats = BuildPamFormats();

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;

            protected override Status EncodeFrame(PackedPixelFile ppf, PackedFrame frame, out byte[] bytes)
            {
                bytes              = Array.Empty<byte>();
                PackedImage color  = frame.Color;
                Status validStatus = VerifyPackedImage(color, in ppf.Info);

                if (!validStatus)
                {
                    return validStatus;
                }

                if (frame.ExtraChannels.Count < ppf.ExtraChannelsInfo.Count)
                {
                    return Status.Failure("Invalid number of extra channels.");
                }

                Status ecValid = ValidateExtraChannels(ppf, frame, color);

                if (!ecValid)
                {
                    return ecValid;
                }

                string headerText = FormatPamHeader(ppf, frame, color);
                bytes             = InterleavePamPixels(headerText, ppf, frame, color);

                return Status.Ok;
            }

            private Status ValidateExtraChannels(PackedPixelFile ppf, PackedFrame frame, PackedImage color)
            {
                for (int i = 0; i < ppf.ExtraChannelsInfo.Count; i++)
                {
                    PackedImage ec    = frame.ExtraChannels[i];
                    Status sizeStatus = VerifyImageSize(ec, in ppf.Info);

                    if (!sizeStatus)
                    {
                        return sizeStatus;
                    }

                    if (ec.Format.DataType != color.Format.DataType ||
                        ec.Format.Endianness != color.Format.Endianness)
                    {
                        return Status.Failure("Incompatible extra channel format.");
                    }
                }

                return Status.Ok;
            }

            private static string FormatPamHeader(PackedPixelFile ppf, PackedFrame frame, PackedImage color)
            {
                string colorType = color.Format.NumChannels switch
                {
                    1 => "GRAYSCALE",
                    2 => "GRAYSCALE_ALPHA",
                    3 => "RGB",
                    _ => "RGB_ALPHA",
                };

                uint depth         = color.Format.NumChannels + (uint)ppf.ExtraChannelsInfo.Count;
                uint maxval        = (1u << (int)ppf.Info.BitsPerSample) - 1u;
                StringBuilder sb   = new StringBuilder();
                sb.Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"P7\nWIDTH {color.XSize}\nHEIGHT {color.YSize}\nDEPTH {depth}\nMAXVAL {maxval}\nTUPLTYPE {colorType}\n"));

                for (int i = 0; i < ppf.ExtraChannelsInfo.Count; i++)
                {
                    sb.Append("TUPLTYPE ");
                    sb.Append(ExtraChannelTypeName(ppf.ExtraChannelsInfo[i].EcInfo.Type));
                    sb.Append('\n');
                }

                sb.Append("ENDHDR\n");

                return sb.ToString();
            }

            private static string ExtraChannelTypeName(JxlExtraChannelType type)
            {
                return type switch
                {
                    JxlExtraChannelType.Alpha         => "ALPHA",
                    JxlExtraChannelType.Depth         => "DEPTH",
                    JxlExtraChannelType.SpotColor     => "SPOT_COLOR",
                    JxlExtraChannelType.SelectionMask => "SELECTION_MASK",
                    JxlExtraChannelType.Black         => "BLACK",
                    JxlExtraChannelType.Cfa           => "CFA",
                    JxlExtraChannelType.Thermal       => "THERMAL",
                    _                                 => "Optional",
                };
            }

            private static byte[] InterleavePamPixels(
                string headerText,
                PackedPixelFile ppf,
                PackedFrame frame,
                PackedImage color)
            {
                byte[] headerBytes   = Encoding.ASCII.GetBytes(headerText);
                int dataSize         = (int)PackedImage.BitsPerChannel(color.Format.DataType) / (int)Common.KBitsPerByte;
                int numColorChannels = (int)color.Format.NumChannels;
                int numEc            = ppf.ExtraChannelsInfo.Count;
                int numPixels        = (int)(color.XSize * color.YSize);
                int pixelBytes       = (numColorChannels + numEc) * dataSize;
                byte[] output        = new byte[headerBytes.Length + (numPixels * pixelBytes)];
                Buffer.BlockCopy(headerBytes, 0, output, 0, headerBytes.Length);

                ReadOnlySpan<byte> colorSpan = color.Pixels();
                int colorStride              = numColorChannels * dataSize;
                int outPos                   = headerBytes.Length;

                for (int p = 0; p < numPixels; p++)
                {
                    colorSpan.Slice(p * colorStride, colorStride).CopyTo(output.AsSpan(outPos, colorStride));
                    outPos += colorStride;

                    for (int e = 0; e < numEc; e++)
                    {
                        ReadOnlySpan<byte> ecSpan = frame.ExtraChannels[e].Pixels();
                        ecSpan.Slice(p * dataSize, dataSize).CopyTo(output.AsSpan(outPos, dataSize));
                        outPos += dataSize;
                    }
                }

                return output;
            }

            private static JxlPixelFormat[] BuildPamFormats()
            {
                List<JxlPixelFormat> list = new List<JxlPixelFormat>();
                JxlDataType[] types       = { JxlDataType.UInt8, JxlDataType.UInt16 };
                JxlEndianness[] endians   = { JxlEndianness.BigEndian, JxlEndianness.LittleEndian };

                for (uint c = 1; c <= 4; c++)
                {
                    foreach (JxlDataType dt in types)
                    {
                        foreach (JxlEndianness end in endians)
                        {
                            if (dt == JxlDataType.UInt16 && end == JxlEndianness.LittleEndian)
                            {
                                continue;
                            }

                            list.Add(new JxlPixelFormat { NumChannels = c, DataType = dt, Endianness = end, Align = 0 });
                        }
                    }
                }

                return list.ToArray();
            }
        }
    }

    /// <summary>
    /// PGX image decoder and encoder implementations.
    /// Ported from <c>lib/extras/dec/pgx.cc</c> and <c>lib/extras/enc/pgx.cc</c>.
    /// </summary>
    public static class CodecPgx
    {
        /// <summary>
        /// Decodes a PGX image from <paramref name="bytes"/> into <paramref name="ppf"/>.
        /// Ported from <c>jxl::extras::DecodeImagePGX</c> in <c>lib/extras/dec/pgx.cc</c>.
        /// </summary>
        /// <param name="bytes">Input PGX file bytes.</param>
        /// <param name="colorHints">Color space hints (defaults to grayscale sRGB).</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success; otherwise failure.</returns>
        public static Status DecodeImagePGX(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            PgxParser parser = new PgxParser(bytes);
            HeaderPGX header = new HeaderPGX();

            if (!parser.ParseHeader(ref header, out int pos))
            {
                return false;
            }

            Status dimStatus = ExtrasCommon.VerifyDimensions(constraints, header.XSize, header.YSize);

            if (!dimStatus)
            {
                return dimStatus;
            }

            if (header.BitsPerSample == 0 || header.BitsPerSample > 16)
            {
                return Status.Failure("PGX: bits_per_sample invalid");
            }

            if (!header.IsUnsigned)
            {
                return Status.Failure("PGX: signed not supported");
            }

            Status hintStatus = ColorHintsHelper.ApplyColorHints(colorHints, colorAlreadySet: false, isGray: true, ppf);

            if (!hintStatus)
            {
                return hintStatus;
            }

            return CopyPgxPixels(bytes.Slice(pos), in header, ppf);
        }

        /// <summary>
        /// Creates a PGX (<c>.pgx</c>) encoder.
        /// Ported from <c>jxl::extras::GetPGXEncoder</c> in <c>lib/extras/enc/pgx.cc</c>.
        /// </summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetPGXEncoder() => new PgxEncoder();

        private static Status CopyPgxPixels(
            ReadOnlySpan<byte> payload,
            in HeaderPGX header,
            PackedPixelFile ppf)
        {
            JxlBasicInfo info          = ppf.Info;
            info.XSize                 = (uint)header.XSize;
            info.YSize                 = (uint)header.YSize;
            info.BitsPerSample         = (uint)header.BitsPerSample;
            info.ExponentBitsPerSample = 0;
            info.UsesOriginalProfile   = true;
            info.Orientation           = JxlOrientation.Identity;
            info.AlphaBits             = 0;
            info.AlphaExponentBits     = 0;
            info.NumColorChannels      = 1;
            info.NumExtraChannels      = 0;
            ppf.Info                   = info;

            JxlDataType dataType  = header.BitsPerSample > 8 ? JxlDataType.UInt16 : JxlDataType.UInt8;
            JxlEndianness endian  = header.BigEndian ? JxlEndianness.BigEndian : JxlEndianness.LittleEndian;
            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = 1,
                DataType    = dataType,
                Endianness  = endian,
                Align       = 0,
            };

            nuint bytesPerSample = dataType == JxlDataType.UInt8 ? 1u : 2u;

            if (!Common.SafeMul(header.XSize, header.YSize, out nuint numSamples) ||
                !Common.SafeMul(numSamples, bytesPerSample, out nuint requiredBytes))
            {
                return Status.Failure("PGX: image too large");
            }

            if ((nuint)payload.Length < requiredBytes)
            {
                return Status.Failure("PGX file too small");
            }

            StatusOr<PackedFrame> frameResult = PackedFrame.Create(header.XSize, header.YSize, in format);

            if (!frameResult.IsOk)
            {
                return frameResult.Status;
            }

            PackedFrame frame = frameResult.Value;

            payload.Slice(0, (int)requiredBytes).CopyTo(frame.Color.Pixels());
            ppf.Frames.Clear();
            ppf.Frames.Add(frame);

            return Status.Ok;
        }

        private struct HeaderPGX
        {
            public nuint XSize;
            public nuint YSize;
            public nuint BitsPerSample;
            public bool BigEndian;
            public bool IsUnsigned;
        }

        private ref struct PgxParser
        {
            private readonly ReadOnlySpan<byte> _bytes;
            private int _pos;

            public PgxParser(ReadOnlySpan<byte> bytes)
            {
                _bytes = bytes;
                _pos   = 0;
            }

            public Status ParseHeader(ref HeaderPGX header, out int dataPos)
            {
                dataPos = 0;

                if (!SkipVerify((byte)'P', (byte)'G') ||
                    !SkipSpace() ||
                    !ParseEndian(ref header) ||
                    !SkipSpace() ||
                    !ParseSignAndDepth(ref header))
                {
                    return false;
                }

                if (!SkipSpace() ||
                    !ParseUnsigned(out header.XSize) ||
                    !SkipSpace() ||
                    !ParseUnsigned(out header.YSize) ||
                    !SkipLineBreak())
                {
                    return false;
                }

                dataPos = _pos;

                return Status.Ok;
            }

            private bool ParseEndian(ref HeaderPGX header)
            {
                if (SkipVerify((byte)'M', (byte)'L'))
                {
                    header.BigEndian = true;

                    return true;
                }

                if (SkipVerify((byte)'L', (byte)'M'))
                {
                    header.BigEndian = false;

                    return true;
                }

                return false;
            }

            private bool ParseSignAndDepth(ref HeaderPGX header)
            {
                if (_pos < _bytes.Length && _bytes[_pos] == (byte)'+')
                {
                    header.IsUnsigned = true;
                    _pos++;
                }
                else if (_pos < _bytes.Length && _bytes[_pos] == (byte)'-')
                {
                    header.IsUnsigned = false;
                    _pos++;
                }
                else
                {
                    header.IsUnsigned = true;
                }

                SkipSpace();

                return ParseUnsigned(out header.BitsPerSample);
            }

            private Status ParseUnsigned(out nuint number)
            {
                number = 0;

                if (_pos == _bytes.Length || !IsDigit(_bytes[_pos]))
                {
                    return Status.Failure("PGX: expected unsigned number");
                }

                while (_pos < _bytes.Length && IsDigit(_bytes[_pos]))
                {
                    if (!Common.SafeMul(number, 10, out number) ||
                        !Common.SafeAdd(number, (nuint)(_bytes[_pos] - (byte)'0'), out number))
                    {
                        return Status.Failure("PGX: unsigned overflow");
                    }

                    _pos++;
                }

                return Status.Ok;
            }

            private bool SkipVerify(byte c1, byte c2)
            {
                if (_pos + 2 > _bytes.Length)
                {
                    return false;
                }

                if (_bytes[_pos] != c1 || _bytes[_pos + 1] != c2)
                {
                    return false;
                }

                _pos += 2;

                return true;
            }

            private Status SkipSpace()
            {
                if (_pos == _bytes.Length)
                {
                    return Status.Failure("PGX: reached end before space");
                }

                int start = _pos;

                while (_pos < _bytes.Length && (_bytes[_pos] == (byte)' ' || _bytes[_pos] == (byte)'\t'))
                {
                    _pos++;
                }

                return _pos > start ? Status.Ok : Status.Failure("PGX: expected space");
            }

            private Status SkipLineBreak()
            {
                if (_pos == _bytes.Length)
                {
                    return Status.Failure("PGX: reached end before line break");
                }

                if (_bytes[_pos] != 0x0A && _bytes[_pos] != 0x0D)
                {
                    return Status.Failure("PGX: expected line break");
                }

                _pos++;

                if (_pos < _bytes.Length && _bytes[_pos - 1] == 0x0D && _bytes[_pos] == 0x0A)
                {
                    _pos++;
                }

                return Status.Ok;
            }

            private static bool IsDigit(byte c) => c >= (byte)'0' && c <= (byte)'9';
        }

        private sealed class PgxEncoder : Encoder
        {
            private static readonly JxlPixelFormat[] Formats =
            {
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8, Endianness = JxlEndianness.BigEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt16, Endianness = JxlEndianness.BigEndian, Align = 0 },
            };

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;

            public override Status Encode(PackedPixelFile ppf, EncodedImage encodedImage, ThreadPool? pool = null)
            {
                Status infoStatus = VerifyBasicInfo(ppf.Info);

                if (!infoStatus)
                {
                    return infoStatus;
                }

                encodedImage.Icc = ppf.Icc;
                encodedImage.Bitstreams.Clear();

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    Status frameStatus = EncodeFrame(ppf.Info, ppf.Frames[i], out byte[] frameBytes);

                    if (!frameStatus)
                    {
                        return frameStatus;
                    }

                    encodedImage.Bitstreams.Add(frameBytes);
                }

                return Status.Ok;
            }

            private Status EncodeFrame(in JxlBasicInfo info, PackedFrame frame, out byte[] bytes)
            {
                bytes              = Array.Empty<byte>();
                PackedImage color  = frame.Color;
                Status validStatus = VerifyPackedImage(color, in info);

                if (!validStatus)
                {
                    return validStatus;
                }

                string headerText  = string.Create(
                    CultureInfo.InvariantCulture,
                    $"PG ML + {info.BitsPerSample} {info.XSize} {info.YSize}\n");
                byte[] headerBytes = Encoding.ASCII.GetBytes(headerText);
                int pixelByteCount = (int)color.PixelsSize;
                bytes              = new byte[headerBytes.Length + pixelByteCount];

                Buffer.BlockCopy(headerBytes, 0, bytes, 0, headerBytes.Length);
                color.Pixels().Slice(0, pixelByteCount).CopyTo(bytes.AsSpan(headerBytes.Length));

                return Status.Ok;
            }
        }
    }

    /// <summary>
    /// NumPy (<c>.npy</c>) array encoder implementation.
    /// Ported from <c>lib/extras/enc/npy.h</c> and <c>lib/extras/enc/npy.cc</c>.
    /// </summary>
    public static class CodecNpy
    {
        /// <summary>
        /// Creates a NumPy (<c>.npy</c>) encoder.
        /// Ported from <c>jxl::extras::GetNumPyEncoder</c> in <c>lib/extras/enc/npy.cc</c>.
        /// </summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder GetNumPyEncoder() => new NumPyEncoder();

        private sealed class NumPyEncoder : Encoder
        {
            private static readonly JxlPixelFormat[] Formats =
            {
                new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 2, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
                new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.Float, Endianness = JxlEndianness.LittleEndian, Align = 0 },
            };

            private static readonly byte[] NumpyMagicPrefix =
            {
                0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 0x01, 0x00,
            };

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;

            public override Status Encode(PackedPixelFile ppf, EncodedImage encodedImage, ThreadPool? pool = null)
            {
                Status infoStatus = VerifyBasicInfo(ppf.Info);

                if (!infoStatus)
                {
                    return infoStatus;
                }

                if (ppf.Frames.Count == 0)
                {
                    return Status.Failure("No frames to encode");
                }

                uint numChannels = ppf.Frames[0].Color.Format.NumChannels;
                Status valStatus = ValidateAllFrames(ppf, numChannels);

                if (!valStatus)
                {
                    return valStatus;
                }

                encodedImage.Bitstreams.Clear();
                encodedImage.Bitstreams.Add(BuildNumpyArray(ppf, numChannels));
                encodedImage.Metadata = BuildNumpyMetadata(ppf);

                return Status.Ok;
            }

            private Status ValidateAllFrames(PackedPixelFile ppf, uint expectedChannels)
            {
                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    PackedImage color  = ppf.Frames[i].Color;
                    Status validStatus = VerifyPackedImage(color, in ppf.Info);

                    if (!validStatus)
                    {
                        return validStatus;
                    }

                    if (color.Format.NumChannels != expectedChannels)
                    {
                        return Status.Failure("Consistent channel count across frames is required");
                    }
                }

                return Status.Ok;
            }

            private static byte[] BuildNumpyArray(PackedPixelFile ppf, uint numChannels)
            {
                string dictStr = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{'descr': '<f4', 'fortran_order': False, 'shape': ({ppf.Frames.Count}, {ppf.Info.YSize}, {ppf.Info.XSize}, {numChannels}), }}\n");
                int padCount = (16 - ((NumpyMagicPrefix.Length + 2 + dictStr.Length) % 16)) % 16;

                if (padCount > 0)
                {
                    dictStr = dictStr.Substring(0, dictStr.Length - 1) + new string(' ', padCount) + "\n";
                }

                byte[] dictBytes    = Encoding.ASCII.GetBytes(dictStr);
                int headerLen       = NumpyMagicPrefix.Length + 2 + dictBytes.Length;
                int totalPixelBytes = 0;

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    totalPixelBytes += (int)ppf.Frames[i].Color.PixelsSize;
                }

                byte[] output = new byte[headerLen + totalPixelBytes];
                Buffer.BlockCopy(NumpyMagicPrefix, 0, output, 0, NumpyMagicPrefix.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(NumpyMagicPrefix.Length, 2), (ushort)dictBytes.Length);
                Buffer.BlockCopy(dictBytes, 0, output, NumpyMagicPrefix.Length + 2, dictBytes.Length);

                int offset = headerLen;

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    PackedImage color = ppf.Frames[i].Color;
                    int size          = (int)color.PixelsSize;
                    color.Pixels().Slice(0, size).CopyTo(output.AsSpan(offset, size));
                    offset += size;
                }

                return output;
            }

            private static byte[] BuildNumpyMetadata(PackedPixelFile ppf)
            {
                StringBuilder sb   = new StringBuilder();
                uint maxTicks      = Math.Max(1u, ppf.Info.Animation.TpsDenominator);
                uint tpsNumerator  = Math.Max(1u, ppf.Info.Animation.TpsNumerator);
                double msPerTick   = (1000.0 * maxTicks) / tpsNumerator;

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    double durationMs = ppf.Info.HaveAnimation ? ppf.Frames[i].FrameInfo.Duration * msPerTick : 0.0;
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(durationMs)} ms\n"));
                }

                return Encoding.ASCII.GetBytes(sb.ToString());
            }
        }
    }
}
