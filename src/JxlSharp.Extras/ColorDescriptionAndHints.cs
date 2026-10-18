// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Globalization;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Parses and formats textual descriptions of <see cref="JxlColorEncoding"/> and <see cref="ColorEncoding"/>.
    /// Ported from <c>lib/extras/dec/color_description.h</c> and <c>lib/extras/dec/color_description.cc</c>.
    /// </summary>
    public static class ColorDescription
    {
        /// <summary>
        /// Parses a color encoding description string into <paramref name="c"/>.
        /// Ported from <c>jxl::ParseDescription</c> in <c>lib/extras/dec/color_description.cc</c>.
        /// </summary>
        /// <param name="description">Color space description string (either a shorthand alias or underscore-separated tokens).</param>
        /// <param name="c">Output external color encoding.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise failure.</returns>
        public static Status ParseDescription(string description, out JxlColorEncoding c)
        {
            c = default;

            if (string.IsNullOrEmpty(description))
            {
                return Status.Failure("Empty color description");
            }

            string normalized = ExpandShorthandAlias(description);
            Tokenizer tok     = new Tokenizer(normalized, '_');

            if (!ParseColorSpace(ref tok, ref c) ||
                !ParseWhitePoint(ref tok, ref c) ||
                !ParsePrimaries(ref tok, ref c) ||
                !ParseRenderingIntent(ref tok, ref c) ||
                !ParseTransferFunction(ref tok, ref c))
            {
                return false;
            }

            if (!tok.Done())
            {
                return Status.Failure("Unexpected trailing tokens in color description");
            }

            return true;
        }

        /// <summary>
        /// Returns a canonical string description of <paramref name="c"/> that can be round-tripped via <see cref="ParseDescription"/>.
        /// </summary>
        /// <param name="c">Internal color encoding.</param>
        /// <returns>Textual color description.</returns>
        public static string Description(ColorEncoding c)
        {
            if (c == null)
            {
                throw new ArgumentNullException(nameof(c));
            }

            JxlColorEncoding external = c.ToExternal();

            return Description(in external);
        }

        /// <summary>
        /// Returns a canonical string description of <paramref name="c"/> that can be round-tripped via <see cref="ParseDescription"/>.
        /// Ported from <c>jxl::Description</c> in <c>lib/extras/dec/color_description.cc</c>.
        /// </summary>
        /// <param name="c">External color encoding.</param>
        /// <returns>Textual color description.</returns>
        public static string Description(in JxlColorEncoding c)
        {
            return JxlCmsInternal.ColorEncodingDescription(c, uniqueName: true);
        }

        private static string ExpandShorthandAlias(string description)
        {
            return description switch
            {
                "sRGB"       => "RGB_D65_SRG_Rel_SRG",
                "DisplayP3"  => "RGB_D65_DCI_Rel_SRG",
                "Adobe98"    => "RGB_D65_Ado_Rel_Ado",
                "ProPhoto"   => "RGB_D50_Pro_Rel_Pro",
                "Rec2100PQ"  => "RGB_D65_202_Rel_PeQ",
                "Rec2100HLG" => "RGB_D65_202_Rel_HLG",
                _            => description,
            };
        }

        private static Status ParseColorSpace(ref Tokenizer tok, ref JxlColorEncoding c)
        {
            if (!tok.Next(out string str))
            {
                return false;
            }

            switch (str)
            {
                case "RGB":
                    c.ColorSpace = JxlColorSpace.Rgb;

                    return true;

                case "Gra":
                    c.ColorSpace = JxlColorSpace.Gray;

                    return true;

                case "XYB":
                    c.ColorSpace = JxlColorSpace.Xyb;

                    return true;

                case "CS?":
                    c.ColorSpace = JxlColorSpace.Unknown;

                    return true;

                default:
                    return Status.Failure("Invalid ColorSpace");
            }
        }

        private static Status ParseWhitePoint(ref Tokenizer tok, ref JxlColorEncoding c)
        {
            if (c.ColorSpace == JxlColorSpace.Xyb)
            {
                c.WhitePoint = JxlWhitePoint.D65;

                return true;
            }

            if (!tok.Next(out string str))
            {
                return false;
            }

            if (TryParseNamedWhitePoint(str, ref c))
            {
                return true;
            }

            return ParseCustomWhitePoint(str, ref c);
        }

        private static bool TryParseNamedWhitePoint(string str, ref JxlColorEncoding c)
        {
            switch (str)
            {
                case "D65":
                    c.WhitePoint = JxlWhitePoint.D65;

                    return true;

                case "Cst":
                    c.WhitePoint = JxlWhitePoint.Custom;

                    return true;

                case "EER":
                    c.WhitePoint = JxlWhitePoint.E;

                    return true;

                case "DCI":
                    c.WhitePoint = JxlWhitePoint.Dci;

                    return true;

                case "D50":
                    c.WhitePoint  = JxlWhitePoint.Custom;
                    c.WhitePointX = 0.345669;
                    c.WhitePointY = 0.358496;

                    return true;

                default:
                    return false;
            }
        }

        private static Status ParseCustomWhitePoint(string str, ref JxlColorEncoding c)
        {
            Tokenizer xyTok = new Tokenizer(str, ';');

            if (!xyTok.Next(out string wxStr) ||
                !xyTok.Next(out string wyStr) ||
                !xyTok.Done() ||
                !ParseDouble(wxStr, out double wx) ||
                !ParseDouble(wyStr, out double wy))
            {
                return Status.Failure("Invalid WhitePoint");
            }

            c.WhitePoint  = JxlWhitePoint.Custom;
            c.WhitePointX = wx;
            c.WhitePointY = wy;

            return true;
        }

        private static Status ParsePrimaries(ref Tokenizer tok, ref JxlColorEncoding c)
        {
            if (c.ColorSpace == JxlColorSpace.Gray || c.ColorSpace == JxlColorSpace.Xyb)
            {
                return true;
            }

            if (!tok.Next(out string str))
            {
                return false;
            }

            if (TryParseNamedPrimaries(str, ref c))
            {
                return true;
            }

            return ParseCustomPrimaries(str, ref c);
        }

        private static bool TryParseNamedPrimaries(string str, ref JxlColorEncoding c)
        {
            switch (str)
            {
                case "SRG":
                    c.Primaries = JxlPrimaries.Srgb;

                    return true;

                case "Cst":
                    c.Primaries = JxlPrimaries.Custom;

                    return true;

                case "202":
                case "210":
                    c.Primaries = JxlPrimaries.P2100;

                    return true;

                case "DCI":
                case "P3":
                    c.Primaries = JxlPrimaries.P3;

                    return true;

                case "Ado":
                    return SetCustomPrimaries(ref c, 0.6400, 0.3300, 0.2100, 0.7100, 0.1500, 0.0600);

                case "Pro":
                    return SetCustomPrimaries(ref c, 0.734699, 0.265301, 0.159597, 0.840403, 0.036598, 0.000105);

                default:
                    return false;
            }
        }

        private static bool SetCustomPrimaries(
            ref JxlColorEncoding c,
            double rx,
            double ry,
            double gx,
            double gy,
            double bx,
            double by)
        {
            c.Primaries       = JxlPrimaries.Custom;
            c.PrimariesRedX   = rx;
            c.PrimariesRedY   = ry;
            c.PrimariesGreenX = gx;
            c.PrimariesGreenY = gy;
            c.PrimariesBlueX  = bx;
            c.PrimariesBlueY  = by;

            return true;
        }

        private static Status ParseCustomPrimaries(string str, ref JxlColorEncoding c)
        {
            Tokenizer xyTok = new Tokenizer(str, ';');
            Span<double> xy = stackalloc double[6];

            for (int i = 0; i < 6; ++i)
            {
                if (!xyTok.Next(out string part) || !ParseDouble(part, out xy[i]))
                {
                    return Status.Failure("Invalid Primaries");
                }
            }

            if (!xyTok.Done())
            {
                return Status.Failure("Invalid Primaries");
            }

            SetCustomPrimaries(ref c, xy[0], xy[1], xy[2], xy[3], xy[4], xy[5]);

            return true;
        }

        private static Status ParseRenderingIntent(ref Tokenizer tok, ref JxlColorEncoding c)
        {
            if (!tok.Next(out string str))
            {
                return false;
            }

            switch (str)
            {
                case "Per":
                    c.RenderingIntent = JxlRenderingIntent.Perceptual;

                    return true;

                case "Rel":
                    c.RenderingIntent = JxlRenderingIntent.Relative;

                    return true;

                case "Sat":
                    c.RenderingIntent = JxlRenderingIntent.Saturation;

                    return true;

                case "Abs":
                    c.RenderingIntent = JxlRenderingIntent.Absolute;

                    return true;

                default:
                    return Status.Failure("Invalid RenderingIntent");
            }
        }

        private static Status ParseTransferFunction(ref Tokenizer tok, ref JxlColorEncoding c)
        {
            if (c.ColorSpace == JxlColorSpace.Xyb)
            {
                c.TransferFunction = JxlTransferFunction.Gamma;
                c.Gamma            = 1.0 / 3.0;

                return true;
            }

            if (!tok.Next(out string str))
            {
                return false;
            }

            if (TryParseNamedTransferFunction(str, ref c))
            {
                return true;
            }

            if (str.Length > 1 && str[0] == 'g' && ParseDouble(str.Substring(1), out double gamma))
            {
                c.TransferFunction = JxlTransferFunction.Gamma;
                c.Gamma            = gamma;

                return true;
            }

            return Status.Failure("Invalid TransferFunction");
        }

        private static bool TryParseNamedTransferFunction(string str, ref JxlColorEncoding c)
        {
            switch (str)
            {
                case "709":
                    c.TransferFunction = JxlTransferFunction.Bt709;

                    return true;

                case "TF?":
                    c.TransferFunction = JxlTransferFunction.Unknown;

                    return true;

                case "Lin":
                    c.TransferFunction = JxlTransferFunction.Linear;

                    return true;

                case "SRG":
                    c.TransferFunction = JxlTransferFunction.Srgb;

                    return true;

                case "PeQ":
                    c.TransferFunction = JxlTransferFunction.Pq;

                    return true;

                case "DCI":
                    c.TransferFunction = JxlTransferFunction.Dci;

                    return true;

                case "HLG":
                    c.TransferFunction = JxlTransferFunction.Hlg;

                    return true;

                case "":
                    c.TransferFunction = JxlTransferFunction.Gamma;

                    return true;

                case "Ado":
                    c.TransferFunction = JxlTransferFunction.Gamma;
                    c.Gamma            = 256.0 / 563.0;

                    return true;

                case "Pro":
                    c.TransferFunction = JxlTransferFunction.Gamma;
                    c.Gamma            = 1.0 / 1.8;

                    return true;

                default:
                    return false;
            }
        }

        private static Status ParseDouble(string num, out double d)
        {
            d = 0.0;

            if (string.IsNullOrEmpty(num))
            {
                return false;
            }

            bool parsed = double.TryParse(
                num,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value);

            if (!parsed || double.IsNaN(value) || double.IsInfinity(value))
            {
                return Status.Failure("Invalid double in color description");
            }

            d = value;

            return true;
        }

        private struct Tokenizer
        {
            private readonly string _input;
            private readonly char _separator;
            private int _pos;

            public Tokenizer(string input, char separator)
            {
                _input     = input;
                _separator = separator;
                _pos       = 0;
            }

            public Status Next(out string next)
            {
                if (Done())
                {
                    next = string.Empty;

                    return Status.Failure("Expected token");
                }

                int end = _input.IndexOf(_separator, _pos);

                if (end < 0)
                {
                    next = _input.Substring(_pos);
                    _pos = _input.Length + 1;
                }
                else
                {
                    next = _input.Substring(_pos, end - _pos);
                    _pos = end + 1;
                }

                return true;
            }

            public readonly bool Done()
            {
                return _pos > _input.Length;
            }
        }
    }

    /// <summary>
    /// Applies user-supplied <see cref="ColorHints"/> to a <see cref="PackedPixelFile"/>.
    /// Ported from <c>lib/extras/dec/color_hints.h</c> and <c>lib/extras/dec/color_hints.cc</c>.
    /// </summary>
    public static class ColorHintsHelper
    {
        /// <summary>
        /// Applies <paramref name="colorHints"/> (such as <c>color_space</c>, <c>icc_pathname</c>, <c>icc</c>, <c>exif</c>, <c>xmp</c>, <c>jumbf</c>)
        /// to <paramref name="ppf"/>.
        /// Ported from <c>jxl::extras::ApplyColorHints</c> in <c>lib/extras/dec/color_hints.cc</c>.
        /// </summary>
        /// <param name="colorHints">Key/value hints collection.</param>
        /// <param name="colorAlreadySet">Whether the decoder already determined the color space from the input file.</param>
        /// <param name="isGray">Whether the image is single-channel grayscale.</param>
        /// <param name="ppf">Target <see cref="PackedPixelFile"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise failure.</returns>
        public static Status ApplyColorHints(
            ColorHints colorHints,
            bool colorAlreadySet,
            bool isGray,
            PackedPixelFile ppf)
        {
            if (colorHints == null)
            {
                throw new ArgumentNullException(nameof(colorHints));
            }

            if (ppf == null)
            {
                throw new ArgumentNullException(nameof(ppf));
            }

            bool gotColorSpace = false;
            Status status      = colorHints.Foreach(
                (key, value) => ApplySingleHint(key, value, colorAlreadySet, ref gotColorSpace, ppf));

            if (!status)
            {
                return status;
            }

            if (!colorAlreadySet && !gotColorSpace)
            {
                ppf.ColorEncoding = ColorEncoding.SRGB(isGray).ToExternal();
            }

            return true;
        }

        private static Status ApplySingleHint(
            string key,
            string value,
            bool colorAlreadySet,
            ref bool gotColorSpace,
            PackedPixelFile ppf)
        {
            if (colorAlreadySet && (key == "color_space" || key == "icc_pathname"))
            {
                return true;
            }

            switch (key)
            {
                case "color_space":
                    return ApplyColorSpaceHint(value, ref gotColorSpace, ppf);

                case "icc_pathname":
                    return Status.Failure("icc_pathname hint requires file I/O; use the 'icc' hint instead");

                case "icc":
                    ppf.Icc       = StringToRawBytes(value);
                    gotColorSpace = true;

                    return true;

                case "exif":
                    ppf.Metadata.Exif = StringToRawBytes(value);

                    return true;

                case "xmp":
                    ppf.Metadata.Xmp = StringToRawBytes(value);

                    return true;

                case "jumbf":
                    ppf.Metadata.Jumbf = StringToRawBytes(value);

                    return true;

                default:
                    return true;
            }
        }

        private static Status ApplyColorSpaceHint(
            string value,
            ref bool gotColorSpace,
            PackedPixelFile ppf)
        {
            if (gotColorSpace)
            {
                return Status.Failure("Multiple color_space/icc hints specified");
            }

            if (!ColorDescription.ParseDescription(value, out JxlColorEncoding cExternal))
            {
                return Status.Failure("Failed to apply color_space");
            }

            ppf.ColorEncoding = cExternal;
            gotColorSpace     = true;

            return true;
        }

        private static byte[] StringToRawBytes(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Array.Empty<byte>();
            }

            byte[] result = new byte[value.Length];

            for (int i = 0; i < value.Length; ++i)
            {
                result[i] = unchecked((byte)value[i]);
            }

            return result;
        }
    }
}
