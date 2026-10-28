// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Default JPEG XL Color Management System (CMS) implementation.
    /// Ported from <c>lib/jxl/cms/jxl_cms.cc</c>.
    /// </summary>
    public static class JxlCms
    {
        private const byte ColorPrimariesP3D65 = 12;

        private static readonly TransferFunction[] KnownTransferFunctions =
        {
            TransferFunction.Tf709,
            TransferFunction.Linear,
            TransferFunction.SRGB,
            TransferFunction.PQ,
            TransferFunction.DCI,
            TransferFunction.HLG,
        };

        private static readonly JxlCmsInterface DefaultInterface = CreateDefaultInterface();

        /// <summary>
        /// Returns the default <see cref="JxlCmsInterface"/> instance.
        /// Ported from <c>JxlGetDefaultCms()</c>.
        /// </summary>
        /// <returns>Default <see cref="JxlCmsInterface"/>.</returns>
        public static JxlCmsInterface GetDefaultCms()
        {
            return DefaultInterface;
        }

        /// <summary>
        /// Computes the Y (luminance) components of the RGB primaries for <paramref name="encoding"/>.
        /// </summary>
        /// <param name="encoding">Color encoding.</param>
        /// <param name="luminances">Output 3-element luminance span.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status GetPrimariesLuminances(CmsColorEncoding encoding, Span<float> luminances)
        {
            CIExy wp = encoding.GetWhitePoint();

            if (!JxlCmsInternal.CIEXYZFromWhiteCIExy(wp.X, wp.Y, out Vector3 whiteXyz))
            {
                return Status.Failure("Invalid white point");
            }

            if (!encoding.GetPrimaries(out PrimariesCIExy primaries))
            {
                return Status.Failure("Invalid primaries");
            }

            Matrix3x3d chromaticities = new Matrix3x3d(
                new Vector3d(primaries.R.X, primaries.G.X, primaries.B.X),
                new Vector3d(primaries.R.Y, primaries.G.Y, primaries.B.Y),
                new Vector3d(
                    1.0 - primaries.R.X - primaries.R.Y,
                    1.0 - primaries.G.X - primaries.G.Y,
                    1.0 - primaries.B.X - primaries.B.Y));

            if (!MatrixOps.Inv3x3Matrix(ref chromaticities))
            {
                return Status.Failure("Singular chromaticities matrix");
            }

            ReadOnlySpan<double> ys = stackalloc double[] { primaries.R.Y, primaries.G.Y, primaries.B.Y };

            for (int i = 0; i < 3; ++i)
            {
                double sum = (chromaticities[i][0] * whiteXyz[0]) +
                             (chromaticities[i][1] * whiteXyz[1]) +
                             (chromaticities[i][2] * whiteXyz[2]);
                luminances[i] = (float)(ys[i] * sum);
            }

            return true;
        }

        /// <summary>
        /// Parses an ICC profile and populates <paramref name="c"/> and <paramref name="cmyk"/>.
        /// Ported from <c>JxlCmsSetFieldsFromICC</c>.
        /// </summary>
        /// <param name="userData">Optional user data.</param>
        /// <param name="iccData">ICC profile bytes.</param>
        /// <param name="c">Output color encoding.</param>
        /// <param name="cmyk">Output flag indicating whether the profile is CMYK.</param>
        /// <returns><c>true</c> on success.</returns>
        public static bool SetFieldsFromIcc(
            object? userData,
            ReadOnlySpan<byte> iccData,
            ref JxlColorEncoding c,
            out bool cmyk)
        {
            cmyk = false;
            c    = new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Unknown,
                TransferFunction = JxlTransferFunction.Unknown,
            };

            if (iccData.Length < 128 || !ParsedIccProfile.TryParse(iccData, out ParsedIccProfile profile))
            {
                return false;
            }

            if (profile.RenderingIntent > 3)
            {
                return false;
            }

            CmsColorEncoding cEnc = new CmsColorEncoding
            {
                RenderingIntent = (RenderingIntent)profile.RenderingIntent,
            };

            if (profile.HasCicp &&
                ApplyCicp(profile.CicpPrimaries, profile.CicpTransfer, profile.CicpMatrix, profile.CicpFullRange, cEnc))
            {
                c = cEnc.ToExternal();

                return true;
            }

            cEnc.ColorSpace = ColorSpaceFromProfile(profile);
            cmyk            = profile.DataColorSpace == ParsedIccProfile.SigCmyk;

            if (cmyk)
            {
                c = cEnc.ToExternal();

                return true;
            }

            if (!UnadaptedWhitePoint(profile, out CIExy wpUnadapted) || !cEnc.SetWhitePoint(wpUnadapted))
            {
                return false;
            }

            if (!IdentifyPrimaries(profile, wpUnadapted, cEnc) || !DetectTransferFunction(profile, cEnc))
            {
                return false;
            }

            c = cEnc.ToExternal();

            return true;
        }

        private static JxlCmsInterface CreateDefaultInterface()
        {
            JxlCmsInterface cms = new JxlCmsInterface();
            cms.SetFieldsData    = null;
            cms.SetFieldsFromIcc = SetFieldsFromIcc;
            cms.InitData         = cms;
            cms.Init             = Init;
            cms.GetSrcBuf        = GetSrcBuf;
            cms.GetDstBuf        = GetDstBuf;
            cms.Run              = DoColorSpaceTransform;
            cms.Destroy          = Destroy;

            return cms;
        }

        private static ColorSpace ColorSpaceFromProfile(ParsedIccProfile profile)
        {
            return profile.DataColorSpace switch
            {
                ParsedIccProfile.SigRgb  => ColorSpace.RGB,
                ParsedIccProfile.SigCmyk => ColorSpace.RGB,
                ParsedIccProfile.SigGray => ColorSpace.Gray,
                _                        => ColorSpace.Unknown,
            };
        }

        private static CIExy CIExyFromXyz(Vector3d xyz)
        {
            double sum = xyz[0] + xyz[1] + xyz[2];

            if (Math.Abs(sum) < 1e-12)
            {
                return new CIExy(0.0, 0.0);
            }

            double factor = 1.0 / sum;

            return new CIExy(xyz[0] * factor, xyz[1] * factor);
        }

        private static Status UnadaptedWhitePoint(ParsedIccProfile profile, out CIExy outXy)
        {
            outXy = default;

            if (!profile.HasWtpt)
            {
                return Status.Failure("ICC profile does not contain WhitePoint tag");
            }

            if (!profile.HasChad)
            {
                outXy = CIExyFromXyz(profile.WtptXyz);

                return true;
            }

            Matrix3x3d inverseChad = profile.Chad;

            if (!MatrixOps.Inv3x3Matrix(ref inverseChad))
            {
                return Status.Failure("Non-invertible ChromaticAdaptation matrix");
            }

            MatrixOps.Mul3x3Vector(in inverseChad, in profile.WtptXyz, out Vector3d unadaptedXyz);
            outXy = CIExyFromXyz(unadaptedXyz);

            return true;
        }

        private static Status IdentifyPrimaries(
            ParsedIccProfile profile,
            CIExy wpUnadapted,
            CmsColorEncoding c)
        {
            if (!c.HasPrimaries() || ColorSpaceFromProfile(profile) == ColorSpace.Unknown)
            {
                return true;
            }

            if (!TryGetInverseChad(profile, wpUnadapted, out Matrix3x3d inverseChad))
            {
                return Status.Failure("Failed to invert CHAD");
            }

            Span<CIExy> chrom = stackalloc CIExy[3];

            for (int i = 0; i < 3; ++i)
            {
                double r = i == 0 ? 1.0 : 0.0;
                double g = i == 1 ? 1.0 : 0.0;
                double b = i == 2 ? 1.0 : 0.0;

                profile.TransformToXyzD50(r, g, b, out Vector3d xyz);
                MatrixOps.Mul3x3Vector(in inverseChad, in xyz, out Vector3d unadaptedXyz);
                chrom[i] = CIExyFromXyz(unadaptedXyz);
            }

            PrimariesCIExy primaries = new PrimariesCIExy(chrom[0], chrom[1], chrom[2]);

            return c.SetPrimaries(primaries);
        }

        private static bool TryGetInverseChad(
            ParsedIccProfile profile,
            CIExy wpUnadapted,
            out Matrix3x3d inverseChad)
        {
            if (profile.HasChad)
            {
                inverseChad = profile.Chad;

                return MatrixOps.Inv3x3Matrix(ref inverseChad).IsOk;
            }

            if (!JxlCmsInternal.AdaptToXYZD50((float)wpUnadapted.X, (float)wpUnadapted.Y, out Matrix3x3 chadF))
            {
                inverseChad = default;

                return false;
            }

            inverseChad = new Matrix3x3d(
                new Vector3d(chadF[0][0], chadF[0][1], chadF[0][2]),
                new Vector3d(chadF[1][0], chadF[1][1], chadF[1][2]),
                new Vector3d(chadF[2][0], chadF[2][1], chadF[2][2]));

            return MatrixOps.Inv3x3Matrix(ref inverseChad).IsOk;
        }

        private static Status DetectTransferFunction(ParsedIccProfile profile, CmsColorEncoding c)
        {
            if (c.ColorSpace == ColorSpace.XYB)
            {
                return Status.Failure("Cannot detect TF for XYB");
            }

            if (TryDetectCustomGamma(profile, c))
            {
                return true;
            }

            foreach (TransferFunction tf in KnownTransferFunctions)
            {
                c.Tf.SetTransferFunction(tf);

                if (IsProfileEquivalent(profile, c))
                {
                    return true;
                }
            }

            c.Tf.SetTransferFunction(TransferFunction.Unknown);

            return true;
        }

        private static bool TryDetectCustomGamma(ParsedIccProfile profile, CmsColorEncoding c)
        {
            double gamma = 0.0;

            if (profile.HasGrayTrc && profile.GrayTrc != null)
            {
                if (profile.GrayTrc.TryEstimateGammaExponent(out double g) && g > 0.0)
                {
                    gamma = 1.0 / g;
                }
            }
            else if (profile.HasRgbTrc)
            {
                if (profile.RgbTrc[0].TryEstimateGammaExponent(out double g0) &&
                    profile.RgbTrc[1].TryEstimateGammaExponent(out double g1) &&
                    profile.RgbTrc[2].TryEstimateGammaExponent(out double g2) &&
                    g0 > 0.0 && g1 > 0.0 && g2 > 0.0)
                {
                    double inv0 = 1.0 / g0;
                    double inv1 = 1.0 / g1;
                    double inv2 = 1.0 / g2;

                    if (Math.Abs(inv0 - inv1) < 1e-4 && Math.Abs(inv1 - inv2) < 1e-4)
                    {
                        gamma = inv0;
                    }
                }
            }

            return gamma != 0.0 && c.Tf.SetGamma(gamma).IsOk && IsProfileEquivalent(profile, c);
        }

        private static bool IsProfileEquivalent(ParsedIccProfile profile1, CmsColorEncoding c)
        {
            if (!JxlCmsInternal.MaybeCreateProfile(c.ToExternal(), out byte[] iccTest) ||
                !ParsedIccProfile.TryParse(iccTest, out ParsedIccProfile profile2))
            {
                return false;
            }

            const double kInit = 1e-3;
            const double kStep = 0.2;

            if (c.ColorSpace == ColorSpace.Gray)
            {
                return AreGrayProfilesEquivalent(profile1, profile2, kInit, kStep / 8.0);
            }

            return AreRgbProfilesEquivalent(profile1, profile2, kInit, kStep);
        }

        private static bool AreGrayProfilesEquivalent(
            ParsedIccProfile profile1,
            ParsedIccProfile profile2,
            double init,
            double step)
        {
            for (double x = init; x < 1.0; x += step)
            {
                profile1.TransformToXyzD50(x, x, x, out Vector3d out1);
                profile2.TransformToXyzD50(x, x, x, out Vector3d out2);

                if (!CmsColorEncoding.ApproxEq(out1[1], out2[1], 2e-4))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreRgbProfilesEquivalent(
            ParsedIccProfile profile1,
            ParsedIccProfile profile2,
            double init,
            double step)
        {
            for (double r = init; r < 1.0; r += step)
            {
                for (double g = init; g < 1.0; g += step)
                {
                    for (double b = init; b < 1.0; b += step)
                    {
                        profile1.TransformToXyzD50(r, g, b, out Vector3d out1);
                        profile2.TransformToXyzD50(r, g, b, out Vector3d out2);

                        if (!CmsColorEncoding.ApproxEq(out1[0], out2[0], 2e-4) ||
                            !CmsColorEncoding.ApproxEq(out1[1], out2[1], 2e-4) ||
                            !CmsColorEncoding.ApproxEq(out1[2], out2[2], 2e-4))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool IsKnownTransferFunction(TransferFunction tf)
        {
            return tf == TransferFunction.Tf709 ||
                   tf == TransferFunction.Linear ||
                   tf == TransferFunction.SRGB ||
                   tf == TransferFunction.PQ ||
                   tf == TransferFunction.DCI ||
                   tf == TransferFunction.HLG;
        }

        private static bool IsKnownColorPrimaries(byte colorPrimaries)
        {
            if (colorPrimaries == ColorPrimariesP3D65)
            {
                return true;
            }

            Primaries p = (Primaries)colorPrimaries;

            return p == Primaries.SRGB || p == Primaries.P2100 || p == Primaries.P3;
        }

        private static bool ApplyCicp(
            byte colorPrimaries,
            byte transferCharacteristics,
            byte matrixCoefficients,
            byte fullRange,
            CmsColorEncoding c)
        {
            if (matrixCoefficients != 0 || fullRange != 1)
            {
                return false;
            }

            Primaries primaries = (Primaries)colorPrimaries;
            TransferFunction tf = (TransferFunction)transferCharacteristics;

            if (!IsKnownTransferFunction(tf) || !IsKnownColorPrimaries(colorPrimaries))
            {
                return false;
            }

            c.ColorSpace = ColorSpace.RGB;
            c.Tf.SetTransferFunction(tf);

            if (primaries == Primaries.P3)
            {
                c.WhitePoint = WhitePoint.DCI;
                c.Primaries  = Primaries.P3;
            }
            else if (colorPrimaries == ColorPrimariesP3D65)
            {
                c.WhitePoint = WhitePoint.D65;
                c.Primaries  = Primaries.P3;
            }
            else
            {
                c.WhitePoint = WhitePoint.D65;
                c.Primaries  = primaries;
            }

            return true;
        }

        private static object? Init(
            object? initData,
            nuint numThreads,
            nuint xsize,
            JxlColorProfile input,
            JxlColorProfile output,
            float intensityTarget)
        {
            if (initData is not JxlCmsInterface cms ||
                input.Icc == null || input.Icc.Length == 0 ||
                output.Icc == null || output.Icc.Length == 0)
            {
                return null;
            }

            CmsColorEncoding cSrc = new CmsColorEncoding();
            CmsColorEncoding cDst = new CmsColorEncoding();

            if (!cSrc.SetFieldsFromICC(input.Icc, cms) || !cDst.SetFieldsFromICC(output.Icc, cms))
            {
                return null;
            }

            if (!ParsedIccProfile.TryParse(input.Icc, out ParsedIccProfile profileSrc) ||
                !ParsedIccProfile.TryParse(output.Icc, out ParsedIccProfile profileDst))
            {
                return null;
            }

            JxlCmsState state = new JxlCmsState
            {
                ProfileSrc      = profileSrc,
                ProfileDst      = profileDst,
                SkipCms         = cSrc.SameColorEncoding(cDst),
                IntensityTarget = intensityTarget,
            };

            if (!ConfigureHlgOotfAndExtraTf(state, ref cSrc, ref cDst))
            {
                return null;
            }

            AllocateStateBuffers(state, cSrc, cDst, xsize, numThreads);

            return state;
        }

        private static bool ConfigureHlgOotfAndExtraTf(
            JxlCmsState state,
            ref CmsColorEncoding cSrc,
            ref CmsColorEncoding cDst)
        {
            state.ApplyHlgOotf = cSrc.Tf.IsHLG() != cDst.Tf.IsHLG();

            if (state.ApplyHlgOotf)
            {
                CmsColorEncoding cHlg    = cSrc.Tf.IsHLG() ? cSrc : cDst;
                state.HlgOotfNumChannels = (int)cHlg.Channels();

                if (state.HlgOotfNumChannels == 3 &&
                    !GetPrimariesLuminances(cHlg, state.HlgOotfLuminances))
                {
                    return false;
                }
            }

            bool srcLinear = cSrc.Tf.IsLinear();
            bool dstLinear = cDst.Tf.IsLinear();

            if (!MaybeEnablePreprocessTf(state, ref cSrc, cDst, dstLinear, ref srcLinear))
            {
                return false;
            }

            if (!MaybeEnablePostprocessTf(state, cSrc, ref cDst, srcLinear))
            {
                return false;
            }

            if (cSrc.SameColorEncoding(cDst))
            {
                state.SkipCms = true;
            }

            return true;
        }

        private static bool MaybeEnablePreprocessTf(
            JxlCmsState state,
            ref CmsColorEncoding cSrc,
            CmsColorEncoding cDst,
            bool dstLinear,
            ref bool srcLinear)
        {
            if (!cSrc.Tf.IsPQ() && !cSrc.Tf.IsHLG() &&
                !(cSrc.Tf.IsSRGB() && dstLinear && cSrc.SameColorSpace(cDst)))
            {
                return true;
            }

            CmsColorEncoding cLinearSrc = cSrc.Clone();
            cLinearSrc.Tf.SetTransferFunction(TransferFunction.Linear);

            if (JxlCmsInternal.MaybeCreateProfile(cLinearSrc.ToExternal(), out byte[] iccSrc) &&
                ParsedIccProfile.TryParse(iccSrc, out ParsedIccProfile newSrc))
            {
                state.ProfileSrc = newSrc;
                state.Preprocess = cSrc.Tf.IsSRGB()
                    ? ExtraTf.SRGB
                    : (cSrc.Tf.IsPQ() ? ExtraTf.PQ : ExtraTf.HLG);
                cSrc      = cLinearSrc;
                srcLinear = true;

                return true;
            }

            return !state.ApplyHlgOotf;
        }

        private static bool MaybeEnablePostprocessTf(
            JxlCmsState state,
            CmsColorEncoding cSrc,
            ref CmsColorEncoding cDst,
            bool srcLinear)
        {
            if (!cDst.Tf.IsPQ() && !cDst.Tf.IsHLG() &&
                !(cDst.Tf.IsSRGB() && srcLinear && cSrc.SameColorSpace(cDst)))
            {
                return true;
            }

            CmsColorEncoding cLinearDst = cDst.Clone();
            cLinearDst.Tf.SetTransferFunction(TransferFunction.Linear);

            if (JxlCmsInternal.MaybeCreateProfile(cLinearDst.ToExternal(), out byte[] iccDst) &&
                ParsedIccProfile.TryParse(iccDst, out ParsedIccProfile newDst))
            {
                state.ProfileDst  = newDst;
                state.Postprocess = cDst.Tf.IsSRGB()
                    ? ExtraTf.SRGB
                    : (cDst.Tf.IsPQ() ? ExtraTf.PQ : ExtraTf.HLG);
                cDst = cLinearDst;

                return true;
            }

            return !state.ApplyHlgOotf;
        }

        private static void AllocateStateBuffers(
            JxlCmsState state,
            CmsColorEncoding cSrc,
            CmsColorEncoding cDst,
            nuint xsize,
            nuint numThreads)
        {
            int channelsSrc       = cSrc.Cmyk ? 4 : (int)cSrc.Channels();
            int channelsDst       = (int)cDst.Channels();
            int actualChannelsSrc = channelsSrc == 4 ? 4 : 3;
            int actualChannelsDst = 3;
            int threadCount       = Math.Max(1, (int)numThreads);
            int width             = Math.Max(1, (int)xsize);

            state.ChannelsSrc = channelsSrc;
            state.ChannelsDst = channelsDst;
            state.BufSrc      = new float[threadCount][];
            state.BufDst      = new float[threadCount][];

            for (int i = 0; i < threadCount; ++i)
            {
                state.BufSrc[i] = new float[width * actualChannelsSrc];
                state.BufDst[i] = new float[width * actualChannelsDst];
            }
        }

        private static float[]? GetSrcBuf(object? cmsData, nuint thread)
        {
            JxlCmsState state = (JxlCmsState)cmsData!;

            return state.BufSrc[(int)thread];
        }

        private static float[]? GetDstBuf(object? cmsData, nuint thread)
        {
            JxlCmsState state = (JxlCmsState)cmsData!;

            return state.BufDst[(int)thread];
        }

        private static void Destroy(object? cmsData)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool DoColorSpaceTransform(
            object? cmsData,
            nuint thread,
            ReadOnlySpan<float> bufSrc,
            Span<float> bufDst,
            nuint xsize)
        {
            if (cmsData is not JxlCmsState t)
            {
                return false;
            }

            int width                  = (int)xsize;
            int srcCount               = width * t.ChannelsSrc;
            ReadOnlySpan<float> xformSrc = bufSrc;

            if (t.Preprocess != ExtraTf.None)
            {
                Span<float> mutableSrc = t.BufSrc[(int)thread].AsSpan(0, srcCount);

                if (!BeforeTransform(t, bufSrc.Slice(0, srcCount), mutableSrc))
                {
                    return false;
                }

                xformSrc = mutableSrc;
            }

            ExecuteProfileTransform(t, width, xformSrc, bufDst);

            if (t.Postprocess != ExtraTf.None)
            {
                int dstCount = width * t.ChannelsDst;

                if (!AfterTransform(t, bufDst.Slice(0, dstCount)))
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ExecuteProfileTransform(
            JxlCmsState t,
            int width,
            ReadOnlySpan<float> xformSrc,
            Span<float> bufDst)
        {
            if (t.SkipCms)
            {
                int count = width * t.ChannelsSrc;
                xformSrc.Slice(0, count).CopyTo(bufDst.Slice(0, count));

                return;
            }

            for (int x = 0; x < width; ++x)
            {
                ReadSourcePixel(t, xformSrc, x, out double r, out double g, out double b);
                t.ProfileSrc.TransformToXyzD50(r, g, b, out Vector3d xyzD50);
                t.ProfileDst.TransformFromXyzD50(xyzD50, out double outR, out double outG, out double outB);
                WriteDestinationPixel(t, bufDst, x, outR, outG, outB);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReadSourcePixel(
            JxlCmsState t,
            ReadOnlySpan<float> src,
            int x,
            out double r,
            out double g,
            out double b)
        {
            if (t.ChannelsSrc == 1)
            {
                r = src[x];
                g = r;
                b = r;

                return;
            }

            int idx = x * t.ChannelsSrc;
            r = src[idx];
            g = src[idx + 1];
            b = src[idx + 2];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteDestinationPixel(
            JxlCmsState t,
            Span<float> dst,
            int x,
            double r,
            double g,
            double b)
        {
            if (t.ChannelsDst == 1)
            {
                dst[x] = (float)r;

                return;
            }

            int idx      = x * 3;
            dst[idx]     = (float)r;
            dst[idx + 1] = (float)g;
            dst[idx + 2] = (float)b;
        }

        private static Status BeforeTransform(JxlCmsState t, ReadOnlySpan<float> bufSrc, Span<float> xformSrc)
        {
            switch (t.Preprocess)
            {
                case ExtraTf.PQ:
                {
                    TfPq tfPq = new TfPq(t.IntensityTarget);

                    for (int i = 0; i < bufSrc.Length; ++i)
                    {
                        xformSrc[i] = tfPq.DisplayFromEncoded(bufSrc[i]);
                    }

                    return true;
                }

                case ExtraTf.HLG:
                {
                    for (int i = 0; i < bufSrc.Length; ++i)
                    {
                        xformSrc[i] = (float)TfHlgBase.DisplayFromEncoded(bufSrc[i]);
                    }

                    return !t.ApplyHlgOotf || ApplyHlgOotf(t, xformSrc, forward: true);
                }

                case ExtraTf.SRGB:
                {
                    TfSrgb tfSrgb = new TfSrgb();

                    for (int i = 0; i < bufSrc.Length; ++i)
                    {
                        xformSrc[i] = tfSrgb.DisplayFromEncoded(bufSrc[i]);
                    }

                    return true;
                }

                default:
                    return Status.Failure("Unexpected preprocess ExtraTF");
            }
        }

        private static Status AfterTransform(JxlCmsState t, Span<float> bufDst)
        {
            switch (t.Postprocess)
            {
                case ExtraTf.PQ:
                {
                    TfPq tfPq = new TfPq(t.IntensityTarget);

                    for (int i = 0; i < bufDst.Length; ++i)
                    {
                        bufDst[i] = tfPq.EncodedFromDisplay(bufDst[i]);
                    }

                    return true;
                }

                case ExtraTf.HLG:
                {
                    if (t.ApplyHlgOotf && !ApplyHlgOotf(t, bufDst, forward: false))
                    {
                        return Status.Failure("Inverse HLG OOTF failed");
                    }

                    for (int i = 0; i < bufDst.Length; ++i)
                    {
                        bufDst[i] = (float)TfHlgBase.EncodedFromDisplay(bufDst[i]);
                    }

                    return true;
                }

                case ExtraTf.SRGB:
                {
                    TfSrgb tfSrgb = new TfSrgb();

                    for (int i = 0; i < bufDst.Length; ++i)
                    {
                        bufDst[i] = tfSrgb.EncodedFromDisplay(bufDst[i]);
                    }

                    return true;
                }

                default:
                    return Status.Failure("Unexpected postprocess ExtraTF");
            }
        }

        private static Status ApplyHlgOotf(JxlCmsState t, Span<float> buf, bool forward)
        {
            if (t.IntensityTarget >= 295.0f && t.IntensityTarget <= 305.0f)
            {
                return true;
            }

            float gamma = 1.2f * MathF.Pow(1.111f, MathF.Log2(t.IntensityTarget * 1e-3f));

            if (!forward)
            {
                gamma = 1.0f / gamma;
            }

            if (t.HlgOotfNumChannels == 1)
            {
                for (int x = 0; x < buf.Length; ++x)
                {
                    buf[x] = MathF.Pow(buf[x], gamma);
                }

                return true;
            }

            if (t.HlgOotfNumChannels == 3)
            {
                ApplyHlgOotfRgb(t, buf, gamma, forward);

                return true;
            }

            return Status.Failure("HLG OOTF not implemented for channel count");
        }

        private static void ApplyHlgOotfRgb(JxlCmsState t, Span<float> buf, float gamma, bool forward)
        {
            float lumR = t.HlgOotfLuminances[0];
            float lumG = t.HlgOotfLuminances[1];
            float lumB = t.HlgOotfLuminances[2];

            for (int x = 0; x < buf.Length; x += 3)
            {
                float luminance = (buf[x] * lumR) + (buf[x + 1] * lumG) + (buf[x + 2] * lumB);
                float ratio     = MathF.Pow(luminance, gamma - 1.0f);

                if (!float.IsFinite(ratio))
                {
                    continue;
                }

                buf[x]     *= ratio;
                buf[x + 1] *= ratio;
                buf[x + 2] *= ratio;

                if (forward && gamma < 1.0f)
                {
                    float maximum = Math.Max(buf[x], Math.Max(buf[x + 1], buf[x + 2]));

                    if (maximum > 1.0f)
                    {
                        float normalizer = 1.0f / maximum;
                        buf[x]     *= normalizer;
                        buf[x + 1] *= normalizer;
                        buf[x + 2] *= normalizer;
                    }
                }
            }
        }

        private sealed class JxlCmsState
        {
            public ParsedIccProfile ProfileSrc = null!;
            public ParsedIccProfile ProfileDst = null!;
            public bool ApplyHlgOotf;
            public int HlgOotfNumChannels;
            public float[] HlgOotfLuminances = new float[3];
            public int ChannelsSrc;
            public int ChannelsDst;
            public float[][] BufSrc = Array.Empty<float[]>();
            public float[][] BufDst = Array.Empty<float[]>();
            public float IntensityTarget;
            public bool SkipCms;
            public ExtraTf Preprocess  = ExtraTf.None;
            public ExtraTf Postprocess = ExtraTf.None;
        }
    }

    /// <summary>
    /// High-level wrapper around <see cref="JxlCmsInterface"/> for executing color space transformations.
    /// Ported from <c>jxl::ColorSpaceTransform</c> in <c>lib/jxl/color_encoding_internal.h</c>.
    /// </summary>
    public sealed class ColorSpaceTransform : IDisposable
    {
        private readonly JxlCmsInterface _cms;
        private object? _cmsData;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorSpaceTransform"/> class.
        /// </summary>
        /// <param name="cms">CMS interface to use (or <c>null</c> to use <see cref="JxlCms.GetDefaultCms"/>).</param>
        public ColorSpaceTransform(JxlCmsInterface? cms = null)
        {
            _cms = cms ?? JxlCms.GetDefaultCms();
        }

        /// <summary>
        /// Initializes the transform between <paramref name="cSrc"/> and <paramref name="cDst"/>.
        /// </summary>
        /// <param name="cSrc">Source color encoding.</param>
        /// <param name="cDst">Destination color encoding.</param>
        /// <param name="intensityTarget">Display intensity target in nits.</param>
        /// <param name="xsize">Maximum number of pixels per row.</param>
        /// <param name="numThreads">Number of worker threads.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Init(
            CmsColorEncoding cSrc,
            CmsColorEncoding cDst,
            float intensityTarget,
            nuint xsize,
            nuint numThreads)
        {
            Dispose();

            if (cDst.IsCMYK || _cms.Init == null)
            {
                return Status.Failure("Conversion to CMYK or uninitialized CMS is not supported");
            }

            JxlColorProfile inputProfile = new JxlColorProfile
            {
                Icc           = cSrc.Icc,
                ColorEncoding = cSrc.ToExternal(),
                NumChannels   = cSrc.IsCMYK ? 4u : cSrc.Channels(),
            };

            JxlColorProfile outputProfile = new JxlColorProfile
            {
                Icc           = cDst.Icc,
                ColorEncoding = cDst.ToExternal(),
                NumChannels   = cDst.Channels(),
            };

            _cmsData = _cms.Init(_cms.InitData, numThreads, xsize, inputProfile, outputProfile, intensityTarget);

            return _cmsData != null;
        }

        /// <summary>
        /// Returns the per-thread source buffer.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <returns>Source float buffer span.</returns>
        public Span<float> BufSrc(nuint thread)
        {
            return _cms.GetSrcBuf!(_cmsData, thread);
        }

        /// <summary>
        /// Returns the per-thread destination buffer.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <returns>Destination float buffer span.</returns>
        public Span<float> BufDst(nuint thread)
        {
            return _cms.GetDstBuf!(_cmsData, thread);
        }

        /// <summary>
        /// Executes the color space transform on <paramref name="xsize"/> pixels.
        /// </summary>
        /// <param name="thread">Thread index.</param>
        /// <param name="bufSrc">Input pixel buffer.</param>
        /// <param name="bufDst">Output pixel buffer.</param>
        /// <param name="xsize">Number of pixels to transform.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Run(nuint thread, ReadOnlySpan<float> bufSrc, Span<float> bufDst, nuint xsize)
        {
            if (_cms.Run == null || _cmsData == null)
            {
                return Status.Failure("ColorSpaceTransform not initialized");
            }

            return _cms.Run(_cmsData, thread, bufSrc, bufDst, xsize);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_cmsData != null)
            {
                _cms.Destroy?.Invoke(_cmsData);
                _cmsData = null;
            }
        }
    }
}
