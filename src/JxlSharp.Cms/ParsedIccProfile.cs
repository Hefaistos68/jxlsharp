// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Represents a tone response curve (`curv` or `para` tag) in an ICC profile.
    /// </summary>
    internal sealed class IccToneCurve
    {
        public bool IsParametric;
        public ushort ParametricType;
        public double G = 1.0;
        public double A = 1.0;
        public double B;
        public double C;
        public double D;
        public double E;
        public double F;
        public ushort[] Table = Array.Empty<ushort>();

        /// <summary>
        /// Evaluates the forward EOTF curve for input <paramref name="x"/>.
        /// </summary>
        /// <param name="x">Encoded input value.</param>
        /// <returns>Linear output value.</returns>
        public double EvalForward(double x)
        {
            if (IsParametric)
            {
                return EvalParametricForward(x);
            }

            if (Table.Length == 0)
            {
                return x;
            }

            if (Table.Length == 1)
            {
                double gamma = Table[0] / 256.0;

                return x < 0.0 ? -Math.Pow(-x, gamma) : Math.Pow(x, gamma);
            }

            return EvalTableForward(x);
        }

        /// <summary>
        /// Evaluates the inverse OETF curve for linear input <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Linear input value.</param>
        /// <returns>Encoded output value.</returns>
        public double EvalInverse(double y)
        {
            if (IsParametric)
            {
                return EvalParametricInverse(y);
            }

            if (Table.Length == 0)
            {
                return y;
            }

            if (Table.Length == 1)
            {
                double gamma = Table[0] / 256.0;

                if (gamma <= 0.0)
                {
                    return 0.0;
                }

                double invGamma = 1.0 / gamma;

                return y < 0.0 ? -Math.Pow(-y, invGamma) : Math.Pow(y, invGamma);
            }

            return EvalTableInverse(y);
        }

        /// <summary>
        /// Attempts to estimate a pure power-law EOTF exponent <c>g</c> for this curve.
        /// </summary>
        /// <param name="exponent">Estimated EOTF exponent <c>g</c>.</param>
        /// <returns><c>true</c> if the curve is a pure power law.</returns>
        public bool TryEstimateGammaExponent(out double exponent)
        {
            exponent = 0.0;

            if (IsParametric)
            {
                if (A == 1.0 && B == 0.0 && D == 0.0 && E == 0.0 && F == 0.0 && G > 0.0)
                {
                    exponent = G;

                    return true;
                }

                return false;
            }

            if (Table.Length == 0)
            {
                exponent = 1.0;

                return true;
            }

            if (Table.Length == 1 && Table[0] > 0)
            {
                exponent = Table[0] / 256.0;

                return true;
            }

            return TryApproximateTableGamma(out exponent);
        }

        private double EvalParametricForward(double x)
        {
            if (x < 0.0 && E == 0.0 && F == 0.0)
            {
                return -EvalParametricForwardNonNegative(-x);
            }

            return EvalParametricForwardNonNegative(x);
        }

        private double EvalParametricForwardNonNegative(double x)
        {
            if (x < D)
            {
                return (C * x) + F;
            }

            double baseVal = (A * x) + B;

            if (baseVal <= 0.0)
            {
                return E;
            }

            return Math.Pow(baseVal, G) + E;
        }

        private double EvalParametricInverse(double y)
        {
            if (y < 0.0 && E == 0.0 && F == 0.0)
            {
                return -EvalParametricInverseNonNegative(-y);
            }

            return EvalParametricInverseNonNegative(y);
        }

        private double EvalParametricInverseNonNegative(double y)
        {
            double threshY = (C * D) + F;

            if (D > 0.0 && y < threshY)
            {
                return C != 0.0 ? (y - F) / C : 0.0;
            }

            if (G <= 0.0 || A == 0.0)
            {
                return 0.0;
            }

            double shifted = Math.Max(0.0, y - E);

            return (Math.Pow(shifted, 1.0 / G) - B) / A;
        }

        private double EvalTableForward(double x)
        {
            if (x <= 0.0)
            {
                return Table[0] / 65535.0;
            }

            if (x >= 1.0)
            {
                return Table[Table.Length - 1] / 65535.0;
            }

            double scaled = x * (Table.Length - 1);
            int idx       = (int)scaled;
            double frac   = scaled - idx;

            if (idx >= Table.Length - 1)
            {
                return Table[Table.Length - 1] / 65535.0;
            }

            double v0 = Table[idx] / 65535.0;
            double v1 = Table[idx + 1] / 65535.0;

            return v0 + (frac * (v1 - v0));
        }

        private double EvalTableInverse(double y)
        {
            double target = Common.Clamp1(y, 0.0, 1.0) * 65535.0;

            if (target <= Table[0])
            {
                return 0.0;
            }

            int last = Table.Length - 1;

            if (target >= Table[last])
            {
                return 1.0;
            }

            int lo = 0;
            int hi = last;

            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;

                if (Table[mid] <= target)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double v0   = Table[lo];
            double v1   = Table[hi];
            double span = v1 - v0;
            double frac = span > 0.0 ? (target - v0) / span : 0.0;

            return (lo + frac) / last;
        }

        private bool TryApproximateTableGamma(out double exponent)
        {
            exponent   = 0.0;
            double mid = EvalTableForward(0.5);

            if (mid <= 1e-6 || mid >= 1.0 - 1e-6)
            {
                return false;
            }

            double g = Math.Log(mid) / Math.Log(0.5);

            for (int i = 1; i < 8; ++i)
            {
                double x        = i / 8.0;
                double actual   = EvalTableForward(x);
                double expected = Math.Pow(x, g);

                if (Math.Abs(actual - expected) > 2e-3)
                {
                    return false;
                }
            }

            exponent = g;

            return true;
        }
    }

    /// <summary>
    /// Represents a parsed 3D A2B0 lookup table (`mAB ` or `mft1`) in an ICC profile.
    /// </summary>
    internal sealed class IccA2BLut
    {
        public bool IsMft1;
        public int Grid0;
        public int Grid1;
        public int Grid2;
        public float[] Clut = Array.Empty<float>();
        public IccToneCurve[] ACurves = Array.Empty<IccToneCurve>();
        public IccToneCurve[] MCurves = Array.Empty<IccToneCurve>();
        public IccToneCurve[] BCurves = Array.Empty<IccToneCurve>();
        public bool HasMatrix;
        public Matrix3x3d Matrix;
        public Vector3d MatrixOffset;

        /// <summary>
        /// Evaluates the A2B LUT for a 3-channel input pixel.
        /// </summary>
        /// <param name="in0">Channel 0 input.</param>
        /// <param name="in1">Channel 1 input.</param>
        /// <param name="in2">Channel 2 input.</param>
        /// <param name="outXyz">Output 3-channel PCS value.</param>
        public void Eval(double in0, double in1, double in2, out Vector3d outXyz)
        {
            double a0 = ACurves.Length == 3 ? ACurves[0].EvalForward(in0) : in0;
            double a1 = ACurves.Length == 3 ? ACurves[1].EvalForward(in1) : in1;
            double a2 = ACurves.Length == 3 ? ACurves[2].EvalForward(in2) : in2;

            InterpolateClut(a0, a1, a2, out double c0, out double c1, out double c2);

            double m0 = MCurves.Length == 3 ? MCurves[0].EvalForward(c0) : c0;
            double m1 = MCurves.Length == 3 ? MCurves[1].EvalForward(c1) : c1;
            double m2 = MCurves.Length == 3 ? MCurves[2].EvalForward(c2) : c2;

            if (HasMatrix)
            {
                Vector3d mVec = new Vector3d(m0, m1, m2);
                MatrixOps.Mul3x3Vector(in Matrix, in mVec, out Vector3d matOut);
                m0 = matOut[0] + MatrixOffset[0];
                m1 = matOut[1] + MatrixOffset[1];
                m2 = matOut[2] + MatrixOffset[2];
            }

            double b0 = BCurves.Length == 3 ? BCurves[0].EvalForward(m0) : m0;
            double b1 = BCurves.Length == 3 ? BCurves[1].EvalForward(m1) : m1;
            double b2 = BCurves.Length == 3 ? BCurves[2].EvalForward(m2) : m2;

            outXyz = new Vector3d(b0, b1, b2);
        }

        private void InterpolateClut(
            double x,
            double y,
            double z,
            out double out0,
            out double out1,
            out double out2)
        {
            ComputeGridAxis(x, Grid0, out int i0, out int i1, out double fx);
            ComputeGridAxis(y, Grid1, out int j0, out int j1, out double fy);
            ComputeGridAxis(z, Grid2, out int k0, out int k1, out double fz);

            out0 = TrilinearChannel(0, i0, i1, fx, j0, j1, fy, k0, k1, fz);
            out1 = TrilinearChannel(1, i0, i1, fx, j0, j1, fy, k0, k1, fz);
            out2 = TrilinearChannel(2, i0, i1, fx, j0, j1, fy, k0, k1, fz);
        }

        private static void ComputeGridAxis(double coord, int gridSize, out int idx0, out int idx1, out double frac)
        {
            double clamped = Common.Clamp1(coord, 0.0, 1.0) * (gridSize - 1);
            idx0           = Math.Min((int)clamped, gridSize - 1);
            idx1           = Math.Min(idx0 + 1, gridSize - 1);
            frac           = clamped - idx0;
        }

        private double TrilinearChannel(
            int ch,
            int i0,
            int i1,
            double fx,
            int j0,
            int j1,
            double fy,
            int k0,
            int k1,
            double fz)
        {
            double c000 = GetClutValue(i0, j0, k0, ch);
            double c001 = GetClutValue(i0, j0, k1, ch);
            double c010 = GetClutValue(i0, j1, k0, ch);
            double c011 = GetClutValue(i0, j1, k1, ch);
            double c100 = GetClutValue(i1, j0, k0, ch);
            double c101 = GetClutValue(i1, j0, k1, ch);
            double c110 = GetClutValue(i1, j1, k0, ch);
            double c111 = GetClutValue(i1, j1, k1, ch);

            double c00 = c000 + (fz * (c001 - c000));
            double c01 = c010 + (fz * (c011 - c010));
            double c10 = c100 + (fz * (c101 - c100));
            double c11 = c110 + (fz * (c111 - c110));

            double c0 = c00 + (fy * (c01 - c00));
            double c1 = c10 + (fy * (c11 - c10));

            return c0 + (fx * (c1 - c0));
        }

        private double GetClutValue(int i, int j, int k, int ch)
        {
            int idx = ((((i * Grid1) + j) * Grid2) + k) * 3 + ch;

            return Clut[idx];
        }
    }

    /// <summary>
    /// Parsed ICC profile supporting tag extraction and forward/inverse PCS XYZ D50 evaluation.
    /// </summary>
    internal sealed class ParsedIccProfile
    {
        public const uint SigRgb  = 0x52474220u; // 'RGB '
        public const uint SigGray = 0x47524159u; // 'GRAY'
        public const uint SigCmyk = 0x434D594Bu; // 'CMYK'
        public const uint SigXyz  = 0x58595A20u; // 'XYZ '
        public const uint SigLab  = 0x4C616220u; // 'Lab '
        public const uint SigAcsp = 0x61637370u; // 'acsp'

        private const uint TagWtpt = 0x77747074u; // 'wtpt'
        private const uint TagChad = 0x63686164u; // 'chad'
        private const uint TagCicp = 0x63696370u; // 'cicp'
        private const uint TagRXyz = 0x7258595Au; // 'rXYZ'
        private const uint TagGXyz = 0x6758595Au; // 'gXYZ'
        private const uint TagBXyz = 0x6258595Au; // 'bXYZ'
        private const uint TagRTrc = 0x72545243u; // 'rTRC'
        private const uint TagGTrc = 0x67545243u; // 'gTRC'
        private const uint TagBTrc = 0x62545243u; // 'bTRC'
        private const uint TagKTrc = 0x6B545243u; // 'kTRC'
        private const uint TagA2B0 = 0x41324230u; // 'A2B0'

        public uint DataColorSpace;
        public uint Pcs;
        public uint RenderingIntent;

        public bool HasWtpt;
        public Vector3d WtptXyz;

        public bool HasChad;
        public Matrix3x3d Chad;

        public bool HasCicp;
        public byte CicpPrimaries;
        public byte CicpTransfer;
        public byte CicpMatrix;
        public byte CicpFullRange;

        public bool HasColorants;
        public Matrix3x3d ToXyzD50;
        public bool HasInverseMatrix;
        public Matrix3x3d FromXyzD50;

        public bool HasRgbTrc;
        public IccToneCurve[] RgbTrc = new IccToneCurve[3];

        public bool HasGrayTrc;
        public IccToneCurve? GrayTrc;

        public IccA2BLut? A2B0;

        /// <summary>
        /// Parses an ICC profile from raw bytes.
        /// </summary>
        /// <param name="icc">ICC profile bytes.</param>
        /// <param name="profile">Parsed profile instance.</param>
        /// <returns><c>true</c> if parsing succeeded.</returns>
        public static bool TryParse(ReadOnlySpan<byte> icc, out ParsedIccProfile profile)
        {
            profile = new ParsedIccProfile();

            if (!ParseHeader(icc, profile))
            {
                return false;
            }

            if (icc.Length < 132)
            {
                return false;
            }

            uint tagCount = ReadBeUInt32(icc, 128);

            if (tagCount > 1024 || 132 + (int)(tagCount * 12) > icc.Length)
            {
                return false;
            }

            return ParseTagTable(icc, (int)tagCount, profile);
        }

        /// <summary>
        /// Transforms a pixel from this profile's color space to PCS XYZ D50.
        /// </summary>
        /// <param name="r">Channel 0 (or Gray).</param>
        /// <param name="g">Channel 1.</param>
        /// <param name="b">Channel 2.</param>
        /// <param name="xyzD50">Output PCS XYZ D50 coordinates.</param>
        public void TransformToXyzD50(double r, double g, double b, out Vector3d xyzD50)
        {
            if (A2B0 != null)
            {
                const double kMaxEncodeableXyz = 65535.0 / 32768.0;
                A2B0.Eval(r, g, b, out Vector3d pcsOut);
                xyzD50 = Pcs == SigLab
                    ? LabPcsToXyzD50(pcsOut)
                    : new Vector3d(
                        pcsOut[0] * kMaxEncodeableXyz,
                        pcsOut[1] * kMaxEncodeableXyz,
                        pcsOut[2] * kMaxEncodeableXyz);

                return;
            }

            if (DataColorSpace == SigGray && HasGrayTrc && GrayTrc != null)
            {
                double y = GrayTrc.EvalForward(r);
                xyzD50   = new Vector3d(y * 0.96420288, y, y * 0.82490540);

                return;
            }

            if (HasRgbTrc && HasColorants)
            {
                Vector3d lin = new Vector3d(
                    RgbTrc[0].EvalForward(r),
                    RgbTrc[1].EvalForward(g),
                    RgbTrc[2].EvalForward(b));
                MatrixOps.Mul3x3Vector(in ToXyzD50, in lin, out xyzD50);

                return;
            }

            xyzD50 = new Vector3d(r, g, b);
        }

        /// <summary>
        /// Transforms a pixel from PCS XYZ D50 into this profile's color space.
        /// </summary>
        /// <param name="xyzD50">Input PCS XYZ D50 coordinates.</param>
        /// <param name="r">Output channel 0 (or Gray).</param>
        /// <param name="g">Output channel 1.</param>
        /// <param name="b">Output channel 2.</param>
        public void TransformFromXyzD50(Vector3d xyzD50, out double r, out double g, out double b)
        {
            if (DataColorSpace == SigGray && HasGrayTrc && GrayTrc != null)
            {
                r = GrayTrc.EvalInverse(xyzD50[1]);
                g = r;
                b = r;

                return;
            }

            if (HasInverseMatrix && HasRgbTrc)
            {
                MatrixOps.Mul3x3Vector(in FromXyzD50, in xyzD50, out Vector3d lin);
                r = RgbTrc[0].EvalInverse(lin[0]);
                g = RgbTrc[1].EvalInverse(lin[1]);
                b = RgbTrc[2].EvalInverse(lin[2]);

                return;
            }

            r = xyzD50[0];
            g = xyzD50[1];
            b = xyzD50[2];
        }

        private static bool ParseHeader(ReadOnlySpan<byte> icc, ParsedIccProfile profile)
        {
            if (icc.Length < 128)
            {
                return false;
            }

            uint profileSize = ReadBeUInt32(icc, 0);

            if (profileSize < 128 || profileSize > (uint)icc.Length)
            {
                return false;
            }

            if (ReadBeUInt32(icc, 36) != SigAcsp)
            {
                return false;
            }

            profile.DataColorSpace = ReadBeUInt32(icc, 16);
            profile.Pcs            = ReadBeUInt32(icc, 20);

            uint beIntent = ReadBeUInt32(icc, 64);
            uint leIntent = ((uint)icc[67] << 24) | ((uint)icc[66] << 16) | ((uint)icc[65] << 8) | icc[64];
            profile.RenderingIntent = Math.Min(beIntent, leIntent);

            return true;
        }

        private static bool ParseTagTable(ReadOnlySpan<byte> icc, int tagCount, ParsedIccProfile profile)
        {
            bool hasR = false;
            bool hasG = false;
            bool hasB = false;
            Vector3d rCol = default;
            Vector3d gCol = default;
            Vector3d bCol = default;

            for (int i = 0; i < tagCount; ++i)
            {
                int entryPos = 132 + (i * 12);
                uint sig     = ReadBeUInt32(icc, entryPos);
                uint offset  = ReadBeUInt32(icc, entryPos + 4);
                uint size    = ReadBeUInt32(icc, entryPos + 8);

                if ((ulong)offset + size > (ulong)icc.Length)
                {
                    return false;
                }

                ReadOnlySpan<byte> tagSpan = icc.Slice((int)offset, (int)size);
                DispatchTag(sig, tagSpan, profile, ref hasR, ref hasG, ref hasB, ref rCol, ref gCol, ref bCol);
            }

            FinalizeParsedProfile(profile, hasR && hasG && hasB, rCol, gCol, bCol);

            return true;
        }

        private static void DispatchTag(
            uint sig,
            ReadOnlySpan<byte> tagSpan,
            ParsedIccProfile profile,
            ref bool hasR,
            ref bool hasG,
            ref bool hasB,
            ref Vector3d rCol,
            ref Vector3d gCol,
            ref Vector3d bCol)
        {
            switch (sig)
            {
                case TagWtpt:
                    profile.HasWtpt = TryParseXyzTag(tagSpan, out profile.WtptXyz);
                    break;
                case TagChad:
                    profile.HasChad = TryParseChadTag(tagSpan, out profile.Chad);
                    break;
                case TagCicp:
                    ParseCicpTag(tagSpan, profile);
                    break;
                case TagRXyz:
                    hasR = TryParseXyzTag(tagSpan, out rCol);
                    break;
                case TagGXyz:
                    hasG = TryParseXyzTag(tagSpan, out gCol);
                    break;
                case TagBXyz:
                    hasB = TryParseXyzTag(tagSpan, out bCol);
                    break;
                default:
                    DispatchTrcOrLutTag(sig, tagSpan, profile);
                    break;
            }
        }

        private static void DispatchTrcOrLutTag(uint sig, ReadOnlySpan<byte> tagSpan, ParsedIccProfile profile)
        {
            switch (sig)
            {
                case TagRTrc:
                    if (TryParseToneCurve(tagSpan, out IccToneCurve? rTrc) && rTrc != null)
                    {
                        profile.RgbTrc[0] = rTrc;
                    }

                    break;
                case TagGTrc:
                    if (TryParseToneCurve(tagSpan, out IccToneCurve? gTrc) && gTrc != null)
                    {
                        profile.RgbTrc[1] = gTrc;
                    }

                    break;
                case TagBTrc:
                    if (TryParseToneCurve(tagSpan, out IccToneCurve? bTrc) && bTrc != null)
                    {
                        profile.RgbTrc[2] = bTrc;
                    }

                    break;
                case TagKTrc:
                    if (TryParseToneCurve(tagSpan, out IccToneCurve? kTrc) && kTrc != null)
                    {
                        profile.GrayTrc    = kTrc;
                        profile.HasGrayTrc = true;
                    }

                    break;
                case TagA2B0:
                    if (TryParseA2BLut(tagSpan, out IccA2BLut? a2b) && a2b != null)
                    {
                        profile.A2B0 = a2b;
                    }

                    break;
            }
        }

        private static void FinalizeParsedProfile(
            ParsedIccProfile profile,
            bool hasAllColorants,
            Vector3d rCol,
            Vector3d gCol,
            Vector3d bCol)
        {
            if (hasAllColorants)
            {
                profile.HasColorants = true;
                profile.ToXyzD50     = new Matrix3x3d(
                    new Vector3d(rCol[0], gCol[0], bCol[0]),
                    new Vector3d(rCol[1], gCol[1], bCol[1]),
                    new Vector3d(rCol[2], gCol[2], bCol[2]));

                Matrix3x3d inv = profile.ToXyzD50;

                if (MatrixOps.Inv3x3Matrix(ref inv).IsOk)
                {
                    profile.FromXyzD50       = inv;
                    profile.HasInverseMatrix = true;
                }
            }

            if (profile.RgbTrc[0] != null && profile.RgbTrc[1] != null && profile.RgbTrc[2] != null)
            {
                profile.HasRgbTrc = true;
            }
        }

        private static bool TryParseXyzTag(ReadOnlySpan<byte> span, out Vector3d xyz)
        {
            xyz = default;

            if (span.Length < 20 || ReadBeUInt32(span, 0) != SigXyz)
            {
                return false;
            }

            xyz = new Vector3d(
                ReadS15Fixed16(span, 8),
                ReadS15Fixed16(span, 12),
                ReadS15Fixed16(span, 16));

            return true;
        }

        private static bool TryParseChadTag(ReadOnlySpan<byte> span, out Matrix3x3d chad)
        {
            chad = default;

            if (span.Length < 44 || ReadBeUInt32(span, 0) != 0x73663332u) // 'sf32'
            {
                return false;
            }

            chad = new Matrix3x3d(
                new Vector3d(ReadS15Fixed16(span, 8), ReadS15Fixed16(span, 12), ReadS15Fixed16(span, 16)),
                new Vector3d(ReadS15Fixed16(span, 20), ReadS15Fixed16(span, 24), ReadS15Fixed16(span, 28)),
                new Vector3d(ReadS15Fixed16(span, 32), ReadS15Fixed16(span, 36), ReadS15Fixed16(span, 40)));

            return true;
        }

        private static void ParseCicpTag(ReadOnlySpan<byte> span, ParsedIccProfile profile)
        {
            if (span.Length >= 12 && ReadBeUInt32(span, 0) == TagCicp)
            {
                profile.HasCicp       = true;
                profile.CicpPrimaries = span[8];
                profile.CicpTransfer  = span[9];
                profile.CicpMatrix    = span[10];
                profile.CicpFullRange = span[11];
            }
        }

        private static bool TryParseToneCurve(ReadOnlySpan<byte> span, out IccToneCurve? curve)
        {
            curve = null;

            if (span.Length < 12)
            {
                return false;
            }

            uint typeSig = ReadBeUInt32(span, 0);

            if (typeSig == 0x63757276u) // 'curv'
            {
                return TryParseCurvTag(span, out curve);
            }

            if (typeSig == 0x70617261u) // 'para'
            {
                return TryParseParaTag(span, out curve);
            }

            return false;
        }

        private static bool TryParseCurvTag(ReadOnlySpan<byte> span, out IccToneCurve? curve)
        {
            curve      = null;
            uint count = ReadBeUInt32(span, 8);

            if (12 + (count * 2) > (uint)span.Length)
            {
                return false;
            }

            ushort[] table = new ushort[count];

            for (int i = 0; i < (int)count; ++i)
            {
                table[i] = ReadBeUInt16(span, 12 + (i * 2));
            }

            curve = new IccToneCurve
            {
                IsParametric = false,
                Table        = table,
            };

            return true;
        }

        private static bool TryParseParaTag(ReadOnlySpan<byte> span, out IccToneCurve? curve)
        {
            curve           = null;
            ushort funcType = ReadBeUInt16(span, 8);
            int paramCount  = funcType switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => 0,
            };

            if (paramCount == 0 || span.Length < 12 + (paramCount * 4))
            {
                return false;
            }

            double g = ReadS15Fixed16(span, 12);
            double a = paramCount >= 3 ? ReadS15Fixed16(span, 16) : 1.0;
            double b = paramCount >= 3 ? ReadS15Fixed16(span, 20) : 0.0;
            double c = paramCount >= 4 ? ReadS15Fixed16(span, 24) : 0.0;
            double d = paramCount >= 5 ? ReadS15Fixed16(span, 28) : (paramCount >= 3 && a != 0.0 ? -b / a : 0.0);
            double e = paramCount >= 7 ? ReadS15Fixed16(span, 32) : (funcType == 2 ? c : 0.0);
            double f = paramCount >= 7 ? ReadS15Fixed16(span, 36) : (funcType == 2 ? c : 0.0);

            if (funcType == 2)
            {
                c = 0.0;
            }

            curve = new IccToneCurve
            {
                IsParametric   = true,
                ParametricType = funcType,
                G              = g,
                A              = a,
                B              = b,
                C              = c,
                D              = d,
                E              = e,
                F              = f,
            };

            return true;
        }

        private static bool TryParseA2BLut(ReadOnlySpan<byte> span, out IccA2BLut? lut)
        {
            lut = null;

            if (span.Length < 32)
            {
                return false;
            }

            uint typeSig = ReadBeUInt32(span, 0);

            if (typeSig == 0x6D414220u) // 'mAB '
            {
                return TryParseMabTag(span, out lut);
            }

            if (typeSig == 0x6D667431u) // 'mft1'
            {
                return TryParseMft1Tag(span, out lut);
            }

            return false;
        }

        private static bool TryParseMabTag(ReadOnlySpan<byte> span, out IccA2BLut? lut)
        {
            lut = null;

            if (span[8] != 3 || span[9] != 3)
            {
                return false;
            }

            int offB      = (int)ReadBeUInt32(span, 12);
            int offMatrix = (int)ReadBeUInt32(span, 16);
            int offM      = (int)ReadBeUInt32(span, 20);
            int offClut   = (int)ReadBeUInt32(span, 24);
            int offA      = (int)ReadBeUInt32(span, 28);

            IccA2BLut result = new IccA2BLut();

            if (offB != 0 && !TryParseThreeCurves(span, offB, out result.BCurves))
            {
                return false;
            }

            if (offM != 0 && !TryParseThreeCurves(span, offM, out result.MCurves))
            {
                return false;
            }

            if (offA != 0 && !TryParseThreeCurves(span, offA, out result.ACurves))
            {
                return false;
            }

            if (offClut != 0 && !TryParseMabClut(span, offClut, result))
            {
                return false;
            }

            if (offMatrix != 0 && !TryParseMabMatrix(span, offMatrix, result))
            {
                return false;
            }

            lut = result;

            return true;
        }

        private static bool TryParseThreeCurves(ReadOnlySpan<byte> span, int offset, out IccToneCurve[] curves)
        {
            curves = new IccToneCurve[3];
            int pos = offset;

            for (int i = 0; i < 3; ++i)
            {
                if (pos >= span.Length || !TryParseToneCurve(span.Slice(pos), out IccToneCurve? c) || c == null)
                {
                    return false;
                }

                curves[i] = c;
                int tagLen = GetCurveTagSize(span.Slice(pos));
                pos += (tagLen + 3) & ~3;
            }

            return true;
        }

        private static int GetCurveTagSize(ReadOnlySpan<byte> span)
        {
            uint sig = ReadBeUInt32(span, 0);

            if (sig == 0x63757276u) // 'curv'
            {
                uint count = ReadBeUInt32(span, 8);

                return 12 + (int)(count * 2);
            }

            ushort funcType = ReadBeUInt16(span, 8);
            int paramCount  = funcType switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => 1,
            };

            return 12 + (paramCount * 4);
        }

        private static bool TryParseMabClut(ReadOnlySpan<byte> span, int offClut, IccA2BLut result)
        {
            if (offClut + 20 > span.Length)
            {
                return false;
            }

            result.Grid0  = span[offClut + 0];
            result.Grid1  = span[offClut + 1];
            result.Grid2  = span[offClut + 2];
            int precision = span[offClut + 16];
            int numPoints = result.Grid0 * result.Grid1 * result.Grid2 * 3;
            int dataPos   = offClut + 20;

            if (numPoints <= 0 || dataPos + (numPoints * precision) > span.Length)
            {
                return false;
            }

            result.Clut = new float[numPoints];

            for (int i = 0; i < numPoints; ++i)
            {
                result.Clut[i] = precision == 2
                    ? ReadBeUInt16(span, dataPos + (i * 2)) / 65535.0f
                    : span[dataPos + i] / 255.0f;
            }

            return true;
        }

        private static bool TryParseMabMatrix(ReadOnlySpan<byte> span, int offMatrix, IccA2BLut result)
        {
            if (offMatrix + 48 > span.Length)
            {
                return false;
            }

            result.HasMatrix = true;
            result.Matrix    = new Matrix3x3d(
                new Vector3d(ReadS15Fixed16(span, offMatrix + 0), ReadS15Fixed16(span, offMatrix + 4), ReadS15Fixed16(span, offMatrix + 8)),
                new Vector3d(ReadS15Fixed16(span, offMatrix + 12), ReadS15Fixed16(span, offMatrix + 16), ReadS15Fixed16(span, offMatrix + 20)),
                new Vector3d(ReadS15Fixed16(span, offMatrix + 24), ReadS15Fixed16(span, offMatrix + 28), ReadS15Fixed16(span, offMatrix + 32)));
            result.MatrixOffset = new Vector3d(
                ReadS15Fixed16(span, offMatrix + 36),
                ReadS15Fixed16(span, offMatrix + 40),
                ReadS15Fixed16(span, offMatrix + 44));

            return true;
        }

        private static bool TryParseMft1Tag(ReadOnlySpan<byte> span, out IccA2BLut? lut)
        {
            lut = null;

            if (span.Length < 48 + 768 || span[8] != 3 || span[9] != 3)
            {
                return false;
            }

            int gridDim   = span[10];
            int numPoints = gridDim * gridDim * gridDim * 3;
            int clutStart = 48 + 768;

            if (gridDim <= 1 || clutStart + numPoints > span.Length)
            {
                return false;
            }

            float[] clut = new float[numPoints];

            for (int i = 0; i < numPoints; ++i)
            {
                clut[i] = span[clutStart + i] / 255.0f;
            }

            lut = new IccA2BLut
            {
                IsMft1 = true,
                Grid0  = gridDim,
                Grid1  = gridDim,
                Grid2  = gridDim,
                Clut   = clut,
            };

            return true;
        }

        private static Vector3d LabPcsToXyzD50(Vector3d pcsOut)
        {
            double l = pcsOut[0] * 100.0;
            double a = (pcsOut[1] * 255.0) - 128.0;
            double b = (pcsOut[2] * 255.0) - 128.0;

            double fy = (l + 16.0) / 116.0;
            double fx = fy + (a / 500.0);
            double fz = fy - (b / 200.0);

            return new Vector3d(
                0.964212 * InvLabF(fx),
                1.0 * InvLabF(fy),
                0.825188 * InvLabF(fz));
        }

        private static double InvLabF(double t)
        {
            const double kDelta = 6.0 / 29.0;

            return t > kDelta
                ? t * t * t
                : 3.0 * kDelta * kDelta * (t - (4.0 / 29.0));
        }

        private static uint ReadBeUInt32(ReadOnlySpan<byte> span, int pos)
        {
            return ((uint)span[pos] << 24) |
                   ((uint)span[pos + 1] << 16) |
                   ((uint)span[pos + 2] << 8) |
                   span[pos + 3];
        }

        private static ushort ReadBeUInt16(ReadOnlySpan<byte> span, int pos)
        {
            return (ushort)((span[pos] << 8) | span[pos + 1]);
        }

        private static double ReadS15Fixed16(ReadOnlySpan<byte> span, int pos)
        {
            int signedVal = (int)ReadBeUInt32(span, pos);

            return signedVal / 65536.0;
        }
    }
}
