// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Extra transfer function preprocessing/postprocessing mode.
    /// Ported from <c>jxl::ExtraTF</c> in <c>lib/jxl/cms/jxl_cms_internal.h</c>.
    /// </summary>
    public enum ExtraTf
    {
        /// <summary>No extra transfer function step.</summary>
        None,

        /// <summary>Perceptual Quantization (PQ) transfer function.</summary>
        PQ,

        /// <summary>Hybrid Log-Gamma (HLG) transfer function.</summary>
        HLG,

        /// <summary>sRGB transfer function.</summary>
        SRGB,
    }

    /// <summary>
    /// ICC profile synthesis, chromatic adaptation matrices, and color encoding descriptions.
    /// Ported from <c>lib/jxl/cms/jxl_cms_internal.h</c>.
    /// </summary>
    public static class JxlCmsInternal
    {
        /// <summary>Whether 3D LUT ICC tone mapping is enabled for HDR profiles.</summary>
        public const bool KEnable3DToneMapping = true;

        /// <summary>
        /// Bradford chromatic adaptation matrix (XYZ to LMS).
        /// </summary>
        public static readonly Matrix3x3 KBradford = new Matrix3x3(
            new Vector3(0.8951f, 0.2664f, -0.1614f),
            new Vector3(-0.7502f, 1.7135f, 0.0367f),
            new Vector3(0.0389f, -0.0685f, 1.0296f));

        /// <summary>
        /// Inverse Bradford chromatic adaptation matrix (LMS to XYZ).
        /// </summary>
        public static readonly Matrix3x3 KBradfordInv = new Matrix3x3(
            new Vector3(0.9869929f, -0.1470543f, 0.1599627f),
            new Vector3(0.4323053f, 0.5183603f, 0.0492912f),
            new Vector3(-0.0085287f, 0.0400428f, 0.9684867f));

        private static readonly uint[] Md5SineParts =
        {
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a,
            0xa8304613, 0xfd469501, 0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be,
            0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821, 0xf61e2562, 0xc040b340,
            0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8,
            0x676f02d9, 0x8d2a4c8a, 0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c,
            0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70, 0x289b7ec6, 0xeaa127fa,
            0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92,
            0xffeff47d, 0x85845dd1, 0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1,
            0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        private static readonly int[] Md5Shifts =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9,  14, 20, 5, 9,  14, 20, 5, 9,  14, 20, 5, 9,  14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly double[] XybAtoBMatrix =
        {
            1.5170095, -1.1065225, 0.071623,
            -0.050022, 0.5683655,  -0.018344,
            -1.387676, 1.1145555,  0.6857255,
        };

        /// <summary>
        /// Computes the RGB-to-XYZ matrix for the given primaries and white point.
        /// </summary>
        /// <param name="p">Primary chromaticities.</param>
        /// <param name="wp">White point chromaticity.</param>
        /// <param name="matrix">Output 3x3 RGB-to-XYZ matrix.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status PrimariesToXYZ(PrimariesCIExy p, CIExy wp, out Matrix3x3 matrix)
        {
            matrix  = default;
            float rx = (float)p.R.X;
            float ry = (float)p.R.Y;
            float gx = (float)p.G.X;
            float gy = (float)p.G.Y;
            float bx = (float)p.B.X;
            float by = (float)p.B.Y;
            float wx = (float)wp.X;
            float wy = (float)wp.Y;

            bool ok = wx >= 0.0f && wx <= 1.0f && wy > 0.0f && wy <= 1.0f;

            if (!ok)
            {
                return Status.Failure("Invalid white point");
            }

            Matrix3x3 primaries = new Matrix3x3(
                new Vector3(rx, gx, bx),
                new Vector3(ry, gy, by),
                new Vector3(1.0f - rx - ry, 1.0f - gx - gy, 1.0f - bx - by));

            Matrix3x3 primariesInv = primaries;

            if (!MatrixOps.Inv3x3Matrix(ref primariesInv))
            {
                return Status.Failure("Singular primaries matrix");
            }

            Vector3 w = new Vector3(wx / wy, 1.0f, (1.0f - wx - wy) / wy);

            if (!float.IsFinite(w[0]) || !float.IsFinite(w[2]))
            {
                return Status.Failure("Non-finite white point XYZ");
            }

            MatrixOps.Mul3x3Vector(in primariesInv, in w, out Vector3 xyz);

            Matrix3x3 a = new Matrix3x3(
                new Vector3(xyz[0], 0.0f, 0.0f),
                new Vector3(0.0f, xyz[1], 0.0f),
                new Vector3(0.0f, 0.0f, xyz[2]));

            MatrixOps.Mul3x3Matrix(in primaries, in a, out matrix);

            return true;
        }

        /// <summary>
        /// Computes the Bradford chromatic adaptation matrix from <c>(wx, wy)</c> to D50.
        /// </summary>
        /// <param name="wx">White point x coordinate.</param>
        /// <param name="wy">White point y coordinate.</param>
        /// <param name="matrix">Output 3x3 chromatic adaptation matrix.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status AdaptToXYZD50(float wx, float wy, out Matrix3x3 matrix)
        {
            matrix  = default;
            bool ok = wx >= 0.0f && wx <= 1.0f && wy > 0.0f && wy <= 1.0f;

            if (!ok)
            {
                return Status.Failure("Invalid white point");
            }

            Vector3 w = new Vector3(wx / wy, 1.0f, (1.0f - wx - wy) / wy);

            if (!float.IsFinite(w[0]) || !float.IsFinite(w[2]))
            {
                return Status.Failure("Invalid white point");
            }

            Vector3 w50 = new Vector3(0.96422f, 1.0f, 0.82521f);

            MatrixOps.Mul3x3Vector(in KBradford, in w, out Vector3 lms);
            MatrixOps.Mul3x3Vector(in KBradford, in w50, out Vector3 lms50);

            if (lms[0] == 0.0f || lms[1] == 0.0f || lms[2] == 0.0f)
            {
                return Status.Failure("Invalid white point");
            }

            Matrix3x3 a = new Matrix3x3(
                new Vector3(lms50[0] / lms[0], 0.0f, 0.0f),
                new Vector3(0.0f, lms50[1] / lms[1], 0.0f),
                new Vector3(0.0f, 0.0f, lms50[2] / lms[2]));

            if (!float.IsFinite(a[0][0]) || !float.IsFinite(a[1][1]) || !float.IsFinite(a[2][2]))
            {
                return Status.Failure("Invalid white point");
            }

            MatrixOps.Mul3x3Matrix(in a, in KBradford, out Matrix3x3 b);
            MatrixOps.Mul3x3Matrix(in KBradfordInv, in b, out matrix);

            return true;
        }

        /// <summary>
        /// Computes the D50-adapted RGB-to-XYZ matrix for the given primaries and white point.
        /// </summary>
        /// <param name="p">Primary chromaticities.</param>
        /// <param name="wp">White point chromaticity.</param>
        /// <param name="matrix">Output 3x3 D50-adapted RGB-to-XYZ matrix.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status PrimariesToXYZD50(PrimariesCIExy p, CIExy wp, out Matrix3x3 matrix)
        {
            matrix = default;

            if (!PrimariesToXYZ(p, wp, out Matrix3x3 toXyz))
            {
                return Status.Failure("PrimariesToXYZ failed");
            }

            if (!AdaptToXYZD50((float)wp.X, (float)wp.Y, out Matrix3x3 d50))
            {
                return Status.Failure("AdaptToXYZD50 failed");
            }

            MatrixOps.Mul3x3Matrix(in d50, in toXyz, out matrix);

            return true;
        }

        /// <summary>
        /// Tone-maps a single normalized RGB pixel into 8-bit PCS Lab coordinates.
        /// </summary>
        /// <param name="c">Color encoding.</param>
        /// <param name="input">Input 3-element RGB pixel.</param>
        /// <param name="pcslabOut">Output 3-byte PCS Lab value.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ToneMapPixel(JxlColorEncoding c, ReadOnlySpan<float> input, Span<byte> pcslabOut)
        {
            PrimariesCIExy p = new PrimariesCIExy(
                new CIExy(c.PrimariesRedXy.X, c.PrimariesRedXy.Y),
                new CIExy(c.PrimariesGreenXy.X, c.PrimariesGreenXy.Y),
                new CIExy(c.PrimariesBlueXy.X, c.PrimariesBlueXy.Y));
            CIExy wp = new CIExy(c.WhitePointXy.X, c.WhitePointXy.Y);

            if (!PrimariesToXYZ(p, wp, out Matrix3x3 primariesXyz))
            {
                return Status.Failure("PrimariesToXYZ failed");
            }

            Vector3 luminances = primariesXyz[1];
            Vector3 linear     = DecodeAndToneMapPixel(c.TransferFunction, input, luminances);
            ToneMapping.GamutMapScalar(ref linear, luminances, 0.3f);

            if (!AdaptToXYZD50((float)wp.X, (float)wp.Y, out Matrix3x3 chad))
            {
                return Status.Failure("AdaptToXYZD50 failed");
            }

            MatrixOps.Mul3x3Matrix(in chad, in primariesXyz, out Matrix3x3 toXyzd50);
            MatrixOps.Mul3x3Vector(in toXyzd50, in linear, out Vector3 xyz);

            const float kXn = 0.964212f;
            const float kYn = 1.0f;
            const float kZn = 0.825188f;

            float fx = LabF(xyz[0] / kXn);
            float fy = LabF(xyz[1] / kYn);
            float fz = LabF(xyz[2] / kZn);

            pcslabOut[0] = (byte)MathF.Round(255.0f * Common.Clamp1((1.16f * fy) - 0.16f, 0.0f, 1.0f), MidpointRounding.AwayFromZero);
            pcslabOut[1] = (byte)MathF.Round(128.0f + Common.Clamp1(500.0f * (fx - fy), -128.0f, 127.0f), MidpointRounding.AwayFromZero);
            pcslabOut[2] = (byte)MathF.Round(128.0f + Common.Clamp1(200.0f * (fy - fz), -128.0f, 127.0f), MidpointRounding.AwayFromZero);

            return true;
        }

        /// <summary>
        /// Creates a 16-bit LUT curve of length <paramref name="n"/> for PQ or HLG.
        /// </summary>
        /// <param name="n">Number of table entries (at most 4096).</param>
        /// <param name="tf">Transfer function (<see cref="ExtraTf.PQ"/> or <see cref="ExtraTf.HLG"/>).</param>
        /// <param name="toneMap">Whether to apply Rec. 2408 tone mapping for PQ.</param>
        /// <returns>Array of <paramref name="n"/> 16-bit table entries.</returns>
        public static ushort[] CreateTableCurve(int n, ExtraTf tf, bool toneMap)
        {
            const float kPqIntensityTarget = 10000.0f;
            Vector3 luminances             = new Vector3(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f);
            Rec2408ToneMapper toneMapper   = new Rec2408ToneMapper(
                0.0f,
                kPqIntensityTarget,
                0.0f,
                Common.kDefaultIntensityTarget,
                luminances);

            ushort[] table = new ushort[n];

            for (int i = 0; i < n; ++i)
            {
                float x   = (float)i / (n - 1);
                double dx = x;
                double y  = tf == ExtraTf.HLG
                    ? TfHlgBase.DisplayFromEncoded(dx)
                    : TfPqBase.DisplayFromEncoded(kPqIntensityTarget, dx);

                if (toneMap && tf == ExtraTf.PQ)
                {
                    float l      = (float)(y * 10000.0 / kPqIntensityTarget);
                    Vector3 gray = new Vector3(l, l, l);
                    toneMapper.ToneMap(ref gray);
                    y = gray[0];
                }

                y = Common.Clamp1(y, 0.0, 1.0);
                table[i] = (ushort)MathF.Round((float)(y * 65535.0), MidpointRounding.AwayFromZero);
            }

            return table;
        }

        /// <summary>
        /// Converts white point CIE xy coordinates to CIE XYZ with <c>Y = 1</c>.
        /// </summary>
        /// <param name="wx">White point x coordinate.</param>
        /// <param name="wy">White point y coordinate.</param>
        /// <param name="xyz">Output XYZ vector.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status CIEXYZFromWhiteCIExy(double wx, double wy, out Vector3 xyz)
        {
            xyz = default;

            if (Math.Abs(wy) < 1e-12)
            {
                return Status.Failure("Y value is too small");
            }

            float factor = (float)(1.0 / wy);
            xyz = new Vector3((float)(wx * factor), 1.0f, (float)((1.0 - wx - wy) * factor));

            return true;
        }

        /// <summary>
        /// Returns whether the given <see cref="JxlColorEncoding"/> supports 3D LUT tone mapping in an ICC profile.
        /// </summary>
        /// <param name="encoding">Color encoding.</param>
        /// <returns><c>true</c> if tone mapping can be embedded.</returns>
        public static bool CanToneMap(JxlColorEncoding encoding)
        {
            JxlTransferFunction tf = encoding.TransferFunction;
            JxlPrimaries p         = encoding.Primaries;
            JxlWhitePoint wp       = encoding.WhitePoint;

            return encoding.ColorSpace == JxlColorSpace.Rgb &&
                   (tf == JxlTransferFunction.Pq || tf == JxlTransferFunction.Hlg) &&
                   ((p == JxlPrimaries.P3 && (wp == JxlWhitePoint.D65 || wp == JxlWhitePoint.Dci)) ||
                    (p != JxlPrimaries.Custom && wp == JxlWhitePoint.D65));
        }

        /// <summary>
        /// Computes the 16-byte RFC 1321 MD5 digest of <paramref name="data"/> for an ICC profile ID.
        /// </summary>
        /// <param name="data">Input profile bytes.</param>
        /// <param name="sum">Output 16-byte MD5 digest.</param>
        public static void IccComputeMd5(ReadOnlySpan<byte> data, Span<byte> sum)
        {
            int extra    = (64 - ((data.Length + 1 + 8) & 63)) & 63;
            int totalLen = data.Length + 1 + extra + 8;
            byte[] data64 = new byte[totalLen];
            data.CopyTo(data64);
            data64[data.Length] = 128;

            ulong bitLen = (ulong)data.Length << 3;

            for (int i = 0; i < 8; ++i)
            {
                data64[totalLen - 8 + i] = (byte)(bitLen >> (i * 8));
            }

            uint a0 = 0x67452301u;
            uint b0 = 0xefcdab89u;
            uint c0 = 0x98badcfeu;
            uint d0 = 0x10325476u;

            for (int i = 0; i < data64.Length; i += 64)
            {
                ComputeMd5Block(data64.AsSpan(i, 64), ref a0, ref b0, ref c0, ref d0);
            }

            WriteLeUInt32(a0, sum.Slice(0, 4));
            WriteLeUInt32(b0, sum.Slice(4, 4));
            WriteLeUInt32(c0, sum.Slice(8, 4));
            WriteLeUInt32(d0, sum.Slice(12, 4));
        }

        /// <summary>
        /// Returns a string representation of the <see cref="JxlColorEncoding"/> fields (e.g. <c>"RGB_D65_SRG_Rel_Lin"</c>).
        /// </summary>
        /// <param name="c">Color encoding.</param>
        /// <param name="uniqueName">If <c>false</c>, returns short names like <c>"sRGB"</c> for common profiles.</param>
        /// <returns>Description string.</returns>
        public static string ColorEncodingDescription(JxlColorEncoding c, bool uniqueName = true)
        {
            if (c.ColorSpace == JxlColorSpace.Rgb && !uniqueName && TryGetShortRgbName(c, out string shortName))
            {
                return shortName;
            }

            StringBuilder d   = new StringBuilder(ColorSpaceToString(c.ColorSpace));
            bool explicitWpTf = c.ColorSpace != JxlColorSpace.Xyb;

            if (explicitWpTf)
            {
                d.Append('_');
                AppendWhitePointDescription(c, d);
            }

            if (c.ColorSpace != JxlColorSpace.Gray && c.ColorSpace != JxlColorSpace.Xyb)
            {
                d.Append('_');
                AppendPrimariesDescription(c, d);
            }

            d.Append('_');
            d.Append(RenderingIntentToString(c.RenderingIntent));

            if (explicitWpTf)
            {
                d.Append('_');
                AppendTransferFunctionDescription(c, d);
            }

            return d.ToString();
        }

        /// <summary>
        /// Synthesizes an ICC profile for the given <see cref="JxlColorEncoding"/> if possible.
        /// </summary>
        /// <param name="c">Color encoding.</param>
        /// <param name="icc">Generated ICC profile bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> if created; <c>false</c> if unknown or invalid.</returns>
        public static Status MaybeCreateProfile(JxlColorEncoding c, out byte[] icc)
        {
            icc = Array.Empty<byte>();

            if (!ValidateProfileColorEncoding(c, out bool isUnknown))
            {
                return isUnknown ? new Status(StatusCode.GenericError) : Status.Failure("Invalid ColorEncoding for ICC");
            }

            List<byte> header   = new List<byte>(128);
            List<byte> tagtable = new List<byte>();
            List<byte> tags     = new List<byte>();
            List<int> offsets   = new List<int>();

            CreateIccHeader(c, header);
            WriteIccUInt32(0, tagtable.Count, tagtable); // tag count placeholder

            int tagOffset = 0;
            int tagSize   = 0;

            CreateIccMlucTag(ColorEncodingDescription(c, uniqueName: false), tags);
            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("desc", tagOffset, tagSize, tagtable, offsets);

            CreateIccMlucTag("CC0", tags);
            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("cprt", tagOffset, tagSize, tagtable, offsets);

            if (!AppendWhitePointAndChromaticities(c, tags, ref tagOffset, ref tagSize, tagtable, offsets))
            {
                return Status.Failure("Failed to write white point or chromaticities");
            }

            if (!AppendToneResponseOrLutTags(c, tags, ref tagOffset, ref tagSize, tagtable, offsets))
            {
                return Status.Failure("Failed to write TRC or LUT tags");
            }

            icc = AssembleAndSignIccProfile(header, tagtable, tags, offsets);

            return true;
        }

        private static Vector3 DecodeAndToneMapPixel(
            JxlTransferFunction tf,
            ReadOnlySpan<float> input,
            Vector3 luminances)
        {
            Vector3 linear = default;

            if (tf == JxlTransferFunction.Pq)
            {
                for (int i = 0; i < 3; ++i)
                {
                    linear[i] = (float)TfPqBase.DisplayFromEncoded(10000.0f, input[i]);
                }

                Rec2408ToneMapper toneMapper = new Rec2408ToneMapper(0.0f, 10000.0f, 0.0f, 250.0f, luminances);
                toneMapper.ToneMap(ref linear);
            }
            else
            {
                for (int i = 0; i < 3; ++i)
                {
                    linear[i] = (float)TfHlgBase.DisplayFromEncoded(input[i]);
                }

                HlgOotf ootf = new HlgOotf(300.0f, 80.0f, luminances);
                ootf.Apply(ref linear);
            }

            return linear;
        }

        private static float LabF(float x)
        {
            const float kDelta = 6.0f / 29.0f;

            return x <= kDelta * kDelta * kDelta
                ? (x * (1.0f / (3.0f * kDelta * kDelta))) + (4.0f / 29.0f)
                : MathF.Cbrt(x);
        }

        private static void ComputeMd5Block(ReadOnlySpan<byte> block, ref uint a0, ref uint b0, ref uint c0, ref uint d0)
        {
            uint a = a0;
            uint b = b0;
            uint c = c0;
            uint d = d0;

            for (int j = 0; j < 64; ++j)
            {
                uint f;
                int g;

                if (j < 16)
                {
                    f = (b & c) | ((~b) & d);
                    g = j;
                }
                else if (j < 32)
                {
                    f = (d & b) | ((~d) & c);
                    g = ((5 * j) + 1) & 0xf;
                }
                else if (j < 48)
                {
                    f = b ^ c ^ d;
                    g = ((3 * j) + 5) & 0xf;
                }
                else
                {
                    f = c ^ (b | (~d));
                    g = (7 * j) & 0xf;
                }

                int wordOffset = g * 4;
                uint u         = (uint)block[wordOffset] |
                                 ((uint)block[wordOffset + 1] << 8) |
                                 ((uint)block[wordOffset + 2] << 16) |
                                 ((uint)block[wordOffset + 3] << 24);

                f += a + Md5SineParts[j] + u;
                a  = d;
                d  = c;
                c  = b;
                int s = Md5Shifts[j];
                b += (f << s) | (f >> (32 - s));
            }

            a0 += a;
            b0 += b;
            c0 += c;
            d0 += d;
        }

        private static void WriteLeUInt32(uint val, Span<byte> dst)
        {
            dst[0] = (byte)val;
            dst[1] = (byte)(val >> 8);
            dst[2] = (byte)(val >> 16);
            dst[3] = (byte)(val >> 24);
        }

        private static bool CloseEnough(double a, double b)
        {
            return Math.Abs(a - b) < 3e-5;
        }

        private static string FormatNumber(double n)
        {
            return n.ToString("g6", CultureInfo.InvariantCulture);
        }

        private static bool TryGetShortRgbName(JxlColorEncoding c, out string shortName)
        {
            if (c.WhitePoint == JxlWhitePoint.D65 && TryGetD65ShortRgbName(c, out shortName))
            {
                return true;
            }

            if (IsProPhoto(c))
            {
                shortName = "ProPhoto";

                return true;
            }

            shortName = string.Empty;

            return false;
        }

        private static bool TryGetD65ShortRgbName(JxlColorEncoding c, out string shortName)
        {
            if (c.TransferFunction == JxlTransferFunction.Srgb && c.Primaries == JxlPrimaries.Srgb)
            {
                shortName = "sRGB";

                return true;
            }

            if (c.TransferFunction == JxlTransferFunction.Srgb && c.Primaries == JxlPrimaries.P3)
            {
                shortName = "DisplayP3";

                return true;
            }

            if (c.Primaries == JxlPrimaries.P2100 && c.TransferFunction == JxlTransferFunction.Pq)
            {
                shortName = "Rec2100PQ";

                return true;
            }

            if (c.Primaries == JxlPrimaries.P2100 && c.TransferFunction == JxlTransferFunction.Hlg)
            {
                shortName = "Rec2100HLG";

                return true;
            }

            if (IsAdobe98(c))
            {
                shortName = "Adobe98";

                return true;
            }

            shortName = string.Empty;

            return false;
        }

        private static bool IsAdobe98(JxlColorEncoding c)
        {
            return c.Primaries == JxlPrimaries.Custom &&
                   CloseEnough(c.PrimariesRedXy.X, 0.6400) &&
                   CloseEnough(c.PrimariesRedXy.Y, 0.3300) &&
                   CloseEnough(c.PrimariesGreenXy.X, 0.2100) &&
                   CloseEnough(c.PrimariesGreenXy.Y, 0.7100) &&
                   CloseEnough(c.PrimariesBlueXy.X, 0.1500) &&
                   CloseEnough(c.PrimariesBlueXy.Y, 0.0600) &&
                   c.TransferFunction == JxlTransferFunction.Gamma &&
                   CloseEnough(c.Gamma, 256.0 / 563.0);
        }

        private static bool IsProPhoto(JxlColorEncoding c)
        {
            return c.WhitePoint == JxlWhitePoint.Custom &&
                   CloseEnough(c.WhitePointXy.X, 0.345669) &&
                   CloseEnough(c.WhitePointXy.Y, 0.358496) &&
                   c.Primaries == JxlPrimaries.Custom &&
                   CloseEnough(c.PrimariesRedXy.X, 0.734699) &&
                   CloseEnough(c.PrimariesRedXy.Y, 0.265301) &&
                   CloseEnough(c.PrimariesGreenXy.X, 0.159597) &&
                   CloseEnough(c.PrimariesGreenXy.Y, 0.840403) &&
                   CloseEnough(c.PrimariesBlueXy.X, 0.036598) &&
                   CloseEnough(c.PrimariesBlueXy.Y, 0.000105) &&
                   c.TransferFunction == JxlTransferFunction.Gamma &&
                   CloseEnough(c.Gamma, 1.0 / 1.8);
        }

        private static string ColorSpaceToString(JxlColorSpace colorSpace)
        {
            return colorSpace switch
            {
                JxlColorSpace.Rgb     => "RGB",
                JxlColorSpace.Gray    => "Gra",
                JxlColorSpace.Xyb     => "XYB",
                JxlColorSpace.Unknown => "CS?",
                _                     => "Invalid",
            };
        }

        private static string WhitePointToString(JxlWhitePoint whitePoint)
        {
            return whitePoint switch
            {
                JxlWhitePoint.D65    => "D65",
                JxlWhitePoint.Custom => "Cst",
                JxlWhitePoint.E      => "EER",
                JxlWhitePoint.Dci    => "DCI",
                _                    => "Invalid",
            };
        }

        private static string PrimariesToString(JxlPrimaries primaries)
        {
            return primaries switch
            {
                JxlPrimaries.Srgb   => "SRG",
                JxlPrimaries.P2100  => "202",
                JxlPrimaries.P3     => "DCI",
                JxlPrimaries.Custom => "Cst",
                _                   => "Invalid",
            };
        }

        private static string TransferFunctionToString(JxlTransferFunction tf)
        {
            return tf switch
            {
                JxlTransferFunction.Srgb    => "SRG",
                JxlTransferFunction.Linear  => "Lin",
                JxlTransferFunction.Bt709   => "709",
                JxlTransferFunction.Pq      => "PeQ",
                JxlTransferFunction.Hlg     => "HLG",
                JxlTransferFunction.Dci     => "DCI",
                JxlTransferFunction.Unknown => "TF?",
                _                           => "Invalid",
            };
        }

        private static string RenderingIntentToString(JxlRenderingIntent ri)
        {
            return ri switch
            {
                JxlRenderingIntent.Perceptual => "Per",
                JxlRenderingIntent.Relative   => "Rel",
                JxlRenderingIntent.Saturation => "Sat",
                JxlRenderingIntent.Absolute   => "Abs",
                _                             => "Invalid",
            };
        }

        private static void AppendWhitePointDescription(JxlColorEncoding c, StringBuilder d)
        {
            if (c.WhitePoint == JxlWhitePoint.Custom)
            {
                d.Append(FormatNumber(c.WhitePointXy.X));
                d.Append(';');
                d.Append(FormatNumber(c.WhitePointXy.Y));
            }
            else
            {
                d.Append(WhitePointToString(c.WhitePoint));
            }
        }

        private static void AppendPrimariesDescription(JxlColorEncoding c, StringBuilder d)
        {
            if (c.Primaries == JxlPrimaries.Custom)
            {
                d.Append(FormatNumber(c.PrimariesRedXy.X)).Append(';');
                d.Append(FormatNumber(c.PrimariesRedXy.Y)).Append(';');
                d.Append(FormatNumber(c.PrimariesGreenXy.X)).Append(';');
                d.Append(FormatNumber(c.PrimariesGreenXy.Y)).Append(';');
                d.Append(FormatNumber(c.PrimariesBlueXy.X)).Append(';');
                d.Append(FormatNumber(c.PrimariesBlueXy.Y));
            }
            else
            {
                d.Append(PrimariesToString(c.Primaries));
            }
        }

        private static void AppendTransferFunctionDescription(JxlColorEncoding c, StringBuilder d)
        {
            if (c.TransferFunction == JxlTransferFunction.Gamma)
            {
                d.Append('g');
                d.Append(FormatNumber(c.Gamma));
            }
            else
            {
                d.Append(TransferFunctionToString(c.TransferFunction));
            }
        }

        private static bool ValidateProfileColorEncoding(JxlColorEncoding c, out bool isUnknown)
        {
            isUnknown = false;

            if (c.ColorSpace == JxlColorSpace.Unknown || c.TransferFunction == JxlTransferFunction.Unknown)
            {
                isUnknown = true;

                return false;
            }

            if (c.ColorSpace != JxlColorSpace.Rgb &&
                c.ColorSpace != JxlColorSpace.Gray &&
                c.ColorSpace != JxlColorSpace.Xyb)
            {
                return false;
            }

            if (c.ColorSpace == JxlColorSpace.Xyb && c.RenderingIntent != JxlRenderingIntent.Perceptual)
            {
                return false;
            }

            return true;
        }

        private static void EnsureSize(List<byte> icc, int minSize)
        {
            while (icc.Count < minSize)
            {
                icc.Add(0);
            }
        }

        private static void WriteIccUInt32(uint value, int pos, List<byte> icc)
        {
            EnsureSize(icc, pos + 4);
            icc[pos]     = (byte)((value >> 24) & 255);
            icc[pos + 1] = (byte)((value >> 16) & 255);
            icc[pos + 2] = (byte)((value >> 8) & 255);
            icc[pos + 3] = (byte)(value & 255);
        }

        private static void WriteIccUInt16(ushort value, int pos, List<byte> icc)
        {
            EnsureSize(icc, pos + 2);
            icc[pos]     = (byte)((value >> 8) & 255);
            icc[pos + 1] = (byte)(value & 255);
        }

        private static void WriteIccUInt8(byte value, int pos, List<byte> icc)
        {
            EnsureSize(icc, pos + 1);
            icc[pos] = value;
        }

        private static void WriteIccTag(string value, int pos, List<byte> icc)
        {
            EnsureSize(icc, pos + 4);
            icc[pos]     = (byte)value[0];
            icc[pos + 1] = (byte)value[1];
            icc[pos + 2] = (byte)value[2];
            icc[pos + 3] = (byte)value[3];
        }

        private static Status WriteIccS15Fixed16(float value, int pos, List<byte> icc)
        {
            bool ok = value >= -32767.995f && value <= 32767.995f;

            if (!ok)
            {
                return Status.Failure("ICC value is out of range / NaN");
            }

            int i  = (int)MathF.Round(value * 65536.0f, MidpointRounding.AwayFromZero);
            uint u = (uint)i;
            WriteIccUInt32(u, pos, icc);

            return true;
        }

        private static void CreateIccHeader(JxlColorEncoding c, List<byte> header)
        {
            const string kCmm = "jxl ";
            EnsureSize(header, 128);

            WriteIccUInt32(0, 0, header);
            WriteIccTag(kCmm, 4, header);
            WriteIccUInt32(0x04400000u, 8, header);
            WriteIccTag(c.ColorSpace == JxlColorSpace.Xyb ? "scnr" : "mntr", 12, header);
            WriteIccTag(c.ColorSpace == JxlColorSpace.Gray ? "GRAY" : "RGB ", 16, header);
            WriteIccTag(KEnable3DToneMapping && CanToneMap(c) ? "Lab " : "XYZ ", 20, header);

            WriteIccUInt16(2019, 24, header);
            WriteIccUInt16(12, 26, header);
            WriteIccUInt16(1, 28, header);
            WriteIccUInt16(0, 30, header);
            WriteIccUInt16(0, 32, header);
            WriteIccUInt16(0, 34, header);

            WriteIccTag("acsp", 36, header);
            WriteIccTag("APPL", 40, header);
            WriteIccUInt32(0, 44, header);
            WriteIccUInt32(0, 48, header);
            WriteIccUInt32(0, 52, header);
            WriteIccUInt32(0, 56, header);
            WriteIccUInt32(0, 60, header);
            WriteIccUInt32((uint)c.RenderingIntent, 64, header);

            WriteIccUInt32(0x0000f6d6u, 68, header);
            WriteIccUInt32(0x00010000u, 72, header);
            WriteIccUInt32(0x0000d32du, 76, header);
            WriteIccTag(kCmm, 80, header);
        }

        private static void AddToIccTagTable(
            string tag,
            int offset,
            int size,
            List<byte> tagtable,
            List<int> offsets)
        {
            WriteIccTag(tag, tagtable.Count, tagtable);
            WriteIccUInt32(0, tagtable.Count, tagtable);
            offsets.Add(offset);
            WriteIccUInt32((uint)size, tagtable.Count, tagtable);
        }

        private static void FinalizeIccTag(List<byte> tags, ref int offset, ref int size)
        {
            while ((tags.Count & 3) != 0)
            {
                tags.Add(0);
            }

            offset += size;
            size    = tags.Count - offset;
        }

        private static void CreateIccMlucTag(string text, List<byte> tags)
        {
            WriteIccTag("mluc", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt32(1, tags.Count, tags);
            WriteIccUInt32(12, tags.Count, tags);
            WriteIccTag("enUS", tags.Count, tags);
            WriteIccUInt32((uint)(text.Length * 2), tags.Count, tags);
            WriteIccUInt32(28, tags.Count, tags);

            foreach (char ch in text)
            {
                tags.Add(0);
                tags.Add((byte)ch);
            }
        }

        private static Status CreateIccXyzTag(Vector3 xyz, List<byte> tags)
        {
            WriteIccTag("XYZ ", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);

            for (int i = 0; i < 3; ++i)
            {
                if (!WriteIccS15Fixed16(xyz[i], tags.Count, tags))
                {
                    return Status.Failure("WriteIccS15Fixed16 failed");
                }
            }

            return true;
        }

        private static Status CreateIccChadTag(Matrix3x3 chad, List<byte> tags)
        {
            WriteIccTag("sf32", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);

            for (int j = 0; j < 3; ++j)
            {
                for (int i = 0; i < 3; ++i)
                {
                    if (!WriteIccS15Fixed16(chad[j][i], tags.Count, tags))
                    {
                        return Status.Failure("WriteIccS15Fixed16 failed");
                    }
                }
            }

            return true;
        }

        private static void MaybeCreateIccCicpTag(
            JxlColorEncoding c,
            List<byte> tags,
            ref int offset,
            ref int size,
            List<byte> tagtable,
            List<int> offsets)
        {
            if (!TryGetCicpPrimariesCode(c, out byte primariesCode))
            {
                return;
            }

            JxlTransferFunction tf = c.TransferFunction;

            if (tf == JxlTransferFunction.Unknown || tf == JxlTransferFunction.Gamma)
            {
                return;
            }

            WriteIccTag("cicp", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt8(primariesCode, tags.Count, tags);
            WriteIccUInt8((byte)tf, tags.Count, tags);
            WriteIccUInt8(0, tags.Count, tags);
            WriteIccUInt8(1, tags.Count, tags);
            FinalizeIccTag(tags, ref offset, ref size);
            AddToIccTagTable("cicp", offset, size, tagtable, offsets);
        }

        private static bool TryGetCicpPrimariesCode(JxlColorEncoding c, out byte primariesCode)
        {
            primariesCode = 0;

            if (c.ColorSpace != JxlColorSpace.Rgb)
            {
                return false;
            }

            if (c.Primaries == JxlPrimaries.P3)
            {
                if (c.WhitePoint == JxlWhitePoint.D65)
                {
                    primariesCode = 12;

                    return true;
                }

                if (c.WhitePoint == JxlWhitePoint.Dci)
                {
                    primariesCode = 11;

                    return true;
                }

                return false;
            }

            if (c.Primaries != JxlPrimaries.Custom && c.WhitePoint == JxlWhitePoint.D65)
            {
                primariesCode = (byte)c.Primaries;

                return true;
            }

            return false;
        }

        private static void CreateIccCurvCurvTag(ReadOnlySpan<ushort> curve, List<byte> tags)
        {
            int pos = tags.Count;
            EnsureSize(tags, pos + 12 + (curve.Length * 2));
            WriteIccTag("curv", pos, tags);
            WriteIccUInt32(0, pos + 4, tags);
            WriteIccUInt32((uint)curve.Length, pos + 8, tags);

            for (int i = 0; i < curve.Length; ++i)
            {
                WriteIccUInt16(curve[i], pos + 12 + (i * 2), tags);
            }
        }

        private static Status CreateIccCurvParaTag(ReadOnlySpan<float> parameters, ushort curveType, List<byte> tags)
        {
            WriteIccTag("para", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt16(curveType, tags.Count, tags);
            WriteIccUInt16(0, tags.Count, tags);

            for (int i = 0; i < parameters.Length; ++i)
            {
                if (!WriteIccS15Fixed16(parameters[i], tags.Count, tags))
                {
                    return Status.Failure("WriteIccS15Fixed16 failed");
                }
            }

            return true;
        }

        private static Status CreateIccLutAtoBTagForXyb(List<byte> tags)
        {
            WriteIccTag("mAB ", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt16(0, tags.Count, tags);
            WriteIccUInt32(32, tags.Count, tags);
            WriteIccUInt32(244, tags.Count, tags);
            WriteIccUInt32(148, tags.Count, tags);
            WriteIccUInt32(80, tags.Count, tags);
            WriteIccUInt32(32, tags.Count, tags);

            ReadOnlySpan<float> oneParam = stackalloc float[] { 1.0f };

            for (int i = 0; i < 3; ++i)
            {
                if (!CreateIccCurvParaTag(oneParam, 0, tags))
                {
                    return Status.Failure("Failed to create B curve");
                }
            }

            for (int i = 0; i < 16; ++i)
            {
                WriteIccUInt8((byte)(i < 3 ? 2 : 0), tags.Count, tags);
            }

            WriteIccUInt8(2, tags.Count, tags);
            WriteIccUInt8(0, tags.Count, tags);
            WriteIccUInt16(0, tags.Count, tags);

            AppendXybClutAndMCurves(tags);

            return AppendXybMatrix(tags);
        }

        private static void AppendXybClutAndMCurves(List<byte> tags)
        {
            Vector3[,,] cube = OpsinParams.UnscaledA2BCube();

            for (int ix = 0; ix < 2; ++ix)
            {
                for (int iy = 0; iy < 2; ++iy)
                {
                    for (int ib = 0; ib < 2; ++ib)
                    {
                        Vector3 outF = cube[ix, iy, ib];

                        for (int i = 0; i < 3; ++i)
                        {
                            ushort val = (ushort)MathF.Round(65535.0f * outF[i], MidpointRounding.AwayFromZero);
                            WriteIccUInt16(val, tags.Count, tags);
                        }
                    }
                }
            }

            Span<float> mParams = stackalloc float[5];

            for (int i = 0; i < 3; ++i)
            {
                float b = -OpsinParams.KXybOffset[i] - MathF.Cbrt(OpsinParams.KNegOpsinAbsorbanceBiasRgb[i]);
                mParams[0] = 3.0f;
                mParams[1] = 1.0f / OpsinParams.KXybScale[i];
                mParams[2] = b;
                mParams[3] = 0.0f;
                mParams[4] = Math.Max(0.0f, -b * OpsinParams.KXybScale[i]);
                CreateIccCurvParaTag(mParams, 3, tags);
            }
        }

        private static Status AppendXybMatrix(List<byte> tags)
        {
            for (int i = 0; i < XybAtoBMatrix.Length; ++i)
            {
                if (!WriteIccS15Fixed16((float)XybAtoBMatrix[i], tags.Count, tags))
                {
                    return Status.Failure("WriteIccS15Fixed16 failed");
                }
            }

            for (int i = 0; i < 3; ++i)
            {
                float intercept = 0.0f;

                for (int j = 0; j < 3; ++j)
                {
                    intercept += (float)(XybAtoBMatrix[(i * 3) + j] * OpsinParams.KNegOpsinAbsorbanceBiasRgb[j]);
                }

                if (!WriteIccS15Fixed16(intercept, tags.Count, tags))
                {
                    return Status.Failure("WriteIccS15Fixed16 failed");
                }
            }

            return true;
        }

        private static Status CreateIccLutAtoBTagForHdr(JxlColorEncoding colorEncoding, List<byte> tags)
        {
            const int k3DLutDim = 9;
            WriteIccTag("mft1", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt8(k3DLutDim, tags.Count, tags);
            WriteIccUInt8(0, tags.Count, tags);

            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    WriteIccS15Fixed16(i == j ? 1.0f : 0.0f, tags.Count, tags);
                }
            }

            AppendIdentityTables256(tags);

            if (!AppendHdr3DLutEntries(colorEncoding, k3DLutDim, tags))
            {
                return Status.Failure("ToneMapPixel failed");
            }

            AppendIdentityTables256(tags);

            return true;
        }

        private static void AppendIdentityTables256(List<byte> tags)
        {
            for (int c = 0; c < 3; ++c)
            {
                for (int i = 0; i < 256; ++i)
                {
                    WriteIccUInt8((byte)i, tags.Count, tags);
                }
            }
        }

        private static Status AppendHdr3DLutEntries(JxlColorEncoding colorEncoding, int lutDim, List<byte> tags)
        {
            Span<float> f         = stackalloc float[3];
            Span<byte> pcslabOut  = stackalloc byte[3];
            float invLutDimMinus1 = 1.0f / (lutDim - 1);

            for (int ix = 0; ix < lutDim; ++ix)
            {
                for (int iy = 0; iy < lutDim; ++iy)
                {
                    for (int ib = 0; ib < lutDim; ++ib)
                    {
                        f[0] = ix * invLutDimMinus1;
                        f[1] = iy * invLutDimMinus1;
                        f[2] = ib * invLutDimMinus1;

                        if (!ToneMapPixel(colorEncoding, f, pcslabOut))
                        {
                            return Status.Failure("ToneMapPixel failed");
                        }

                        WriteIccUInt8(pcslabOut[0], tags.Count, tags);
                        WriteIccUInt8(pcslabOut[1], tags.Count, tags);
                        WriteIccUInt8(pcslabOut[2], tags.Count, tags);
                    }
                }
            }

            return true;
        }

        private static Status CreateIccNoOpBToATag(List<byte> tags)
        {
            WriteIccTag("mBA ", tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt8(3, tags.Count, tags);
            WriteIccUInt16(0, tags.Count, tags);
            WriteIccUInt32(32, tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);
            WriteIccUInt32(0, tags.Count, tags);

            ReadOnlySpan<float> oneParam = stackalloc float[] { 1.0f };
            CreateIccCurvParaTag(oneParam, 0, tags);
            CreateIccCurvParaTag(oneParam, 0, tags);
            CreateIccCurvParaTag(oneParam, 0, tags);

            return true;
        }

        private static Status AppendWhitePointAndChromaticities(
            JxlColorEncoding c,
            List<byte> tags,
            ref int tagOffset,
            ref int tagSize,
            List<byte> tagtable,
            List<int> offsets)
        {
            if (c.ColorSpace == JxlColorSpace.Gray)
            {
                if (!CIEXYZFromWhiteCIExy(c.WhitePointXy.X, c.WhitePointXy.Y, out Vector3 wtpt) ||
                    !CreateIccXyzTag(wtpt, tags))
                {
                    return Status.Failure("Invalid Gray white point");
                }
            }
            else
            {
                CreateIccXyzTag(new Vector3(0.964203f, 1.0f, 0.824905f), tags);
            }

            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("wtpt", tagOffset, tagSize, tagtable, offsets);

            if (c.ColorSpace != JxlColorSpace.Gray)
            {
                if (c.WhitePointXy.Y == 0.0 ||
                    !AdaptToXYZD50((float)c.WhitePointXy.X, (float)c.WhitePointXy.Y, out Matrix3x3 chad) ||
                    !CreateIccChadTag(chad, tags))
                {
                    return Status.Failure("Invalid chad");
                }

                FinalizeIccTag(tags, ref tagOffset, ref tagSize);
                AddToIccTagTable("chad", tagOffset, tagSize, tagtable, offsets);
            }

            if (c.ColorSpace == JxlColorSpace.Rgb)
            {
                return AppendRgbColorantTags(c, tags, ref tagOffset, ref tagSize, tagtable, offsets);
            }

            return true;
        }

        private static Status AppendRgbColorantTags(
            JxlColorEncoding c,
            List<byte> tags,
            ref int tagOffset,
            ref int tagSize,
            List<byte> tagtable,
            List<int> offsets)
        {
            MaybeCreateIccCicpTag(c, tags, ref tagOffset, ref tagSize, tagtable, offsets);

            PrimariesCIExy p = new PrimariesCIExy(
                new CIExy(c.PrimariesRedXy.X, c.PrimariesRedXy.Y),
                new CIExy(c.PrimariesGreenXy.X, c.PrimariesGreenXy.Y),
                new CIExy(c.PrimariesBlueXy.X, c.PrimariesBlueXy.Y));
            CIExy wp = new CIExy(c.WhitePointXy.X, c.WhitePointXy.Y);

            if (!PrimariesToXYZD50(p, wp, out Matrix3x3 m))
            {
                return Status.Failure("PrimariesToXYZD50 failed");
            }

            Vector3 r = new Vector3(m[0][0], m[1][0], m[2][0]);
            Vector3 g = new Vector3(m[0][1], m[1][1], m[2][1]);
            Vector3 b = new Vector3(m[0][2], m[1][2], m[2][2]);

            if (!CreateIccXyzTag(r, tags))
            {
                return Status.Failure("Failed rXYZ");
            }

            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("rXYZ", tagOffset, tagSize, tagtable, offsets);

            if (!CreateIccXyzTag(g, tags))
            {
                return Status.Failure("Failed gXYZ");
            }

            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("gXYZ", tagOffset, tagSize, tagtable, offsets);

            if (!CreateIccXyzTag(b, tags))
            {
                return Status.Failure("Failed bXYZ");
            }

            FinalizeIccTag(tags, ref tagOffset, ref tagSize);
            AddToIccTagTable("bXYZ", tagOffset, tagSize, tagtable, offsets);

            return true;
        }

        private static Status AppendToneResponseOrLutTags(
            JxlColorEncoding c,
            List<byte> tags,
            ref int tagOffset,
            ref int tagSize,
            List<byte> tagtable,
            List<int> offsets)
        {
            if (c.ColorSpace == JxlColorSpace.Xyb)
            {
                CreateIccLutAtoBTagForXyb(tags);
                FinalizeIccTag(tags, ref tagOffset, ref tagSize);
                AddToIccTagTable("A2B0", tagOffset, tagSize, tagtable, offsets);
                CreateIccNoOpBToATag(tags);
                FinalizeIccTag(tags, ref tagOffset, ref tagSize);
                AddToIccTagTable("B2A0", tagOffset, tagSize, tagtable, offsets);

                return true;
            }

            if (KEnable3DToneMapping && CanToneMap(c))
            {
                if (!CreateIccLutAtoBTagForHdr(c, tags))
                {
                    return Status.Failure("Failed HDR LUT");
                }

                FinalizeIccTag(tags, ref tagOffset, ref tagSize);
                AddToIccTagTable("A2B0", tagOffset, tagSize, tagtable, offsets);
                CreateIccNoOpBToATag(tags);
                FinalizeIccTag(tags, ref tagOffset, ref tagSize);
                AddToIccTagTable("B2A0", tagOffset, tagSize, tagtable, offsets);

                return true;
            }

            if (!WriteTrcTagData(c, tags))
            {
                return Status.Failure("Failed TRC tag");
            }

            FinalizeIccTag(tags, ref tagOffset, ref tagSize);

            if (c.ColorSpace == JxlColorSpace.Gray)
            {
                AddToIccTagTable("kTRC", tagOffset, tagSize, tagtable, offsets);
            }
            else
            {
                AddToIccTagTable("rTRC", tagOffset, tagSize, tagtable, offsets);
                AddToIccTagTable("gTRC", tagOffset, tagSize, tagtable, offsets);
                AddToIccTagTable("bTRC", tagOffset, tagSize, tagtable, offsets);
            }

            return true;
        }

        private static Status WriteTrcTagData(JxlColorEncoding c, List<byte> tags)
        {
            JxlTransferFunction tf = c.TransferFunction;

            if (tf == JxlTransferFunction.Gamma)
            {
                float gamma = (float)(1.0 / c.Gamma);

                return CreateIccCurvParaTag(stackalloc float[] { gamma }, 0, tags);
            }

            return tf switch
            {
                JxlTransferFunction.Hlg => WriteTableTrc(ExtraTf.HLG, CanToneMap(c), tags),
                JxlTransferFunction.Pq  => WriteTableTrc(ExtraTf.PQ, CanToneMap(c), tags),
                JxlTransferFunction.Srgb => CreateIccCurvParaTag(
                    stackalloc float[] { 2.4f, (float)(1.0 / 1.055), (float)(0.055 / 1.055), (float)(1.0 / 12.92), 0.04045f },
                    3,
                    tags),
                JxlTransferFunction.Bt709 => CreateIccCurvParaTag(
                    stackalloc float[] { (float)(1.0 / 0.45), (float)(1.0 / 1.099), (float)(0.099 / 1.099), (float)(1.0 / 4.5), 0.081f },
                    3,
                    tags),
                JxlTransferFunction.Linear => CreateIccCurvParaTag(
                    stackalloc float[] { 1.0f, 1.0f, 0.0f, 1.0f, 0.0f },
                    3,
                    tags),
                JxlTransferFunction.Dci => CreateIccCurvParaTag(
                    stackalloc float[] { 2.6f, 1.0f, 0.0f, 1.0f, 0.0f },
                    3,
                    tags),
                _ => Status.Failure("Unknown TF"),
            };
        }

        private static Status WriteTableTrc(ExtraTf tf, bool toneMap, List<byte> tags)
        {
            CreateIccCurvCurvTag(CreateTableCurve(64, tf, toneMap), tags);

            return true;
        }

        private static byte[] AssembleAndSignIccProfile(
            List<byte> header,
            List<byte> tagtable,
            List<byte> tags,
            List<int> offsets)
        {
            WriteIccUInt32((uint)offsets.Count, 0, tagtable);

            for (int i = 0; i < offsets.Count; ++i)
            {
                WriteIccUInt32((uint)(offsets[i] + header.Count + tagtable.Count), 4 + (12 * i) + 4, tagtable);
            }

            int totalSize = header.Count + tagtable.Count + tags.Count;
            WriteIccUInt32((uint)totalSize, 0, header);

            byte[] icc = new byte[totalSize];
            header.CopyTo(icc, 0);
            tagtable.CopyTo(icc, header.Count);
            tags.CopyTo(icc, header.Count + tagtable.Count);

            byte[] iccSum = (byte[])icc.Clone();

            if (iccSum.Length >= 68)
            {
                Array.Clear(iccSum, 44, 4);
                Array.Clear(iccSum, 64, 4);
            }

            Span<byte> checksum = stackalloc byte[16];
            IccComputeMd5(iccSum, checksum);
            checksum.CopyTo(icc.AsSpan(84, 16));

            return icc;
        }
    }
}
