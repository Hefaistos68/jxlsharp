// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.IO;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Cms.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlCms"/>, <see cref="JxlCmsInternal"/>, and <see cref="ColorSpaceTransform"/>
    /// (ported from <c>lib/jxl/color_management_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class ColorManagementTests
    {
        private const int KWidth      = 16;
        private const uint KNumThreads = 1;

        /// <summary>
        /// Ported from <c>ColorManagementTest.VerifyAllProfiles</c>.
        /// Exercises the ColorManagement interface for ALL ColorEncoding synthesizable via enums.
        /// </summary>
        [Test]
        public void VerifyAllProfiles()
        {
            float[] inColor  = GenerateColor();
            float[] outColor = new float[KWidth * 3];
            CmsColorEncoding cNative = CmsColorEncoding.LinearSRGB(isGray: false);

            foreach (CmsColorEncoding actual in ColorEncodingCmsTests.AllEncodings())
            {
                actual.CreateICC().IsOk.ShouldBeTrue();

                CmsColorEncoding expected = new CmsColorEncoding();
                expected.SetICC(actual.Icc, JxlCms.GetDefaultCms()).IsOk.ShouldBeTrue();

                expected.RenderingIntent.ShouldBe(actual.RenderingIntent);
                expected.ColorSpace.ShouldBe(actual.ColorSpace);
                expected.WhitePoint.ShouldBe(actual.WhitePoint);
                expected.HasPrimaries().ShouldBe(actual.HasPrimaries());

                if (actual.HasPrimaries())
                {
                    expected.Primaries.ShouldBe(actual.Primaries);
                }

                actual.Tf.IsSame(expected.Tf).ShouldBeTrue();
                VerifyPixelRoundTrip(actual, cNative, inColor, outColor);
            }
        }

        /// <summary>
        /// Ported from <c>ColorManagementTest.sRGBChromaticity</c>.
        /// </summary>
        [Test]
        public void SrgbChromaticity()
        {
            CmsColorEncoding srgb = CmsColorEncoding.SRGB();
            CIExy wp              = srgb.GetWhitePoint();
            wp.X.ShouldBe(0.3127, 1e-4);
            wp.Y.ShouldBe(0.3290, 1e-4);

            srgb.GetPrimaries(out PrimariesCIExy p).IsOk.ShouldBeTrue();
            p.R.X.ShouldBe(0.64, 1e-4);
            p.R.Y.ShouldBe(0.33, 1e-4);
            p.G.X.ShouldBe(0.30, 1e-4);
            p.G.Y.ShouldBe(0.60, 1e-4);
            p.B.X.ShouldBe(0.15, 1e-4);
            p.B.Y.ShouldBe(0.06, 1e-4);
        }

        /// <summary>
        /// Ported from <c>ColorManagementTest.D2700Chromaticity</c> and <c>ColorManagementTest.D2700ToSRGB</c>.
        /// </summary>
        [Test]
        public void D2700ChromaticityAndToSrgb()
        {
            byte[] iccData = JxlSharp.TestLib.TestUtils.ReadTestData("jxl/color_management/sRGB-D2700.icc");

            CmsColorEncoding srgbD2700 = new CmsColorEncoding();
            srgbD2700.SetICC(iccData, JxlCms.GetDefaultCms()).IsOk.ShouldBeTrue();

            CIExy wp = srgbD2700.GetWhitePoint();
            wp.X.ShouldBe(0.45986, 1e-4);
            wp.Y.ShouldBe(0.41060, 1e-4);

            srgbD2700.GetPrimaries(out PrimariesCIExy p).IsOk.ShouldBeTrue();
            p.R.X.ShouldBe(0.64, 1e-4);
            p.R.Y.ShouldBe(0.33, 1e-4);
            p.G.X.ShouldBe(0.30, 1e-4);
            p.G.Y.ShouldBe(0.60, 1e-4);
            p.B.X.ShouldBe(0.15, 1e-4);
            p.B.Y.ShouldBe(0.06, 1e-4);

            using ColorSpaceTransform transform = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            transform.Init(srgbD2700, CmsColorEncoding.SRGB(), Common.kDefaultIntensityTarget, 1, 1).IsOk.ShouldBeTrue();

            float[] src = { 0.863f, 0.737f, 0.490f };
            float[] dst = new float[3];
            transform.Run(0, src, dst, 1).IsOk.ShouldBeTrue();

            dst[0].ShouldBe(0.914f, 1e-3f);
            dst[1].ShouldBe(0.745f, 1e-3f);
            dst[2].ShouldBe(0.601f, 1e-3f);
        }

        /// <summary>
        /// Ported from <c>ColorManagementTest.P3HlgTo2020Hlg</c>.
        /// </summary>
        [Test]
        public void P3HlgTo2020Hlg()
        {
            CmsColorEncoding p3Hlg = new CmsColorEncoding
            {
                ColorSpace = ColorSpace.RGB,
                WhitePoint = WhitePoint.D65,
                Primaries  = Primaries.P3,
            };
            p3Hlg.Tf.SetTransferFunction(TransferFunction.HLG);
            p3Hlg.CreateICC().IsOk.ShouldBeTrue();

            CmsColorEncoding rec2020Hlg = p3Hlg.Clone();
            rec2020Hlg.Primaries = Primaries.P2100;
            rec2020Hlg.CreateICC().IsOk.ShouldBeTrue();

            using ColorSpaceTransform transform = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            transform.Init(p3Hlg, rec2020Hlg, 1000.0f, 1, 1).IsOk.ShouldBeTrue();

            float[] p3Values      = { 0.0f, 0.75f, 0.0f };
            float[] rec2020Values = new float[3];
            transform.Run(0, p3Values, rec2020Values, 1).IsOk.ShouldBeTrue();

            rec2020Values[0].ShouldBe(0.3973f, 1e-4f);
            rec2020Values[1].ShouldBe(0.7382f, 1e-4f);
            rec2020Values[2].ShouldBe(0.1183f, 1e-4f);
        }

        /// <summary>
        /// Ported from <c>ColorManagementTest.HlgOotf</c>.
        /// </summary>
        [Test]
        public void HlgOotfTransform()
        {
            CmsColorEncoding p3Hlg = new CmsColorEncoding
            {
                ColorSpace = ColorSpace.RGB,
                WhitePoint = WhitePoint.D65,
                Primaries  = Primaries.P3,
            };
            p3Hlg.Tf.SetTransferFunction(TransferFunction.HLG);
            p3Hlg.CreateICC().IsOk.ShouldBeTrue();

            using ColorSpaceTransform transformTo1000 = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            transformTo1000.Init(p3Hlg, CmsColorEncoding.LinearSRGB(), 1000.0f, 1, 1).IsOk.ShouldBeTrue();

            float[] p3HlgValues      = { 0.75f, 0.75f, 0.75f };
            float[] linearSrgbValues = new float[3];
            transformTo1000.Run(0, p3HlgValues, linearSrgbValues, 1).IsOk.ShouldBeTrue();
            linearSrgbValues[0].ShouldBe(0.203f, 1e-3f);
            linearSrgbValues[1].ShouldBe(0.203f, 1e-3f);
            linearSrgbValues[2].ShouldBe(0.203f, 1e-3f);

            using ColorSpaceTransform transformTo400 = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            transformTo400.Init(p3Hlg, CmsColorEncoding.LinearSRGB(), 400.0f, 1, 1).IsOk.ShouldBeTrue();
            transformTo400.Run(0, p3HlgValues, linearSrgbValues, 1).IsOk.ShouldBeTrue();
            linearSrgbValues[0].ShouldBe(0.250f, 1e-3f);
            linearSrgbValues[1].ShouldBe(0.250f, 1e-3f);
            linearSrgbValues[2].ShouldBe(0.250f, 1e-3f);

            p3HlgValues[2] = 0.50f;
            transformTo1000.Run(0, p3HlgValues, linearSrgbValues, 1).IsOk.ShouldBeTrue();
            linearSrgbValues[0].ShouldBe(0.201f, 1e-3f);
            linearSrgbValues[1].ShouldBe(0.201f, 1e-3f);
            linearSrgbValues[2].ShouldBe(0.050f, 1e-3f);

            using ColorSpaceTransform transformFrom400 = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            transformFrom400.Init(CmsColorEncoding.LinearSRGB(), p3Hlg, 400.0f, 1, 1).IsOk.ShouldBeTrue();
            linearSrgbValues[0] = 0.250f;
            linearSrgbValues[1] = 0.250f;
            linearSrgbValues[2] = 0.250f;
            transformFrom400.Run(0, linearSrgbValues, p3HlgValues, 1).IsOk.ShouldBeTrue();
            p3HlgValues[0].ShouldBe(0.75f, 1e-3f);
            p3HlgValues[1].ShouldBe(0.75f, 1e-3f);
            p3HlgValues[2].ShouldBe(0.75f, 1e-3f);

            CmsColorEncoding grayscaleHlg = new CmsColorEncoding
            {
                ColorSpace = ColorSpace.Gray,
                WhitePoint = WhitePoint.D65,
            };
            grayscaleHlg.Tf.SetTransferFunction(TransferFunction.HLG);
            grayscaleHlg.CreateICC().IsOk.ShouldBeTrue();

            using ColorSpaceTransform grayscaleTransform = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            grayscaleTransform.Init(grayscaleHlg, CmsColorEncoding.LinearSRGB(isGray: true), 1000.0f, 1, 1).IsOk.ShouldBeTrue();
            float[] grayIn  = { 0.75f };
            float[] grayOut = new float[1];
            grayscaleTransform.Run(0, grayIn, grayOut, 1).IsOk.ShouldBeTrue();
            grayOut[0].ShouldBe(0.203f, 1e-3f);
        }

        /// <summary>
        /// Ported from <c>ColorManagementTest.XYBProfile</c>.
        /// </summary>
        [Test]
        public void XybProfile()
        {
            CmsColorEncoding cXyb = new CmsColorEncoding
            {
                ColorSpace      = ColorSpace.XYB,
                RenderingIntent = RenderingIntent.Perceptual,
            };
            cXyb.CreateICC().IsOk.ShouldBeTrue();
            CmsColorEncoding cNative = CmsColorEncoding.LinearSRGB(isGray: false);

            const int kGridDim   = 17;
            const int kNumColors = kGridDim * kGridDim * kGridDim;

            using ColorSpaceTransform xform = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            xform.Init(cXyb, cNative, Common.kDefaultIntensityTarget, (nuint)kNumColors, 1).IsOk.ShouldBeTrue();

            float[] nativeR = new float[kNumColors];
            float[] nativeG = new float[kNumColors];
            float[] nativeB = new float[kNumColors];
            float mul       = 1.0f / (kGridDim - 1);

            for (int ir = 0, x = 0; ir < kGridDim; ++ir)
            {
                for (int ig = 0; ig < kGridDim; ++ig)
                {
                    for (int ib = 0; ib < kGridDim; ++ib, ++x)
                    {
                        nativeR[x] = ir * mul;
                        nativeG[x] = ig * mul;
                        nativeB[x] = ib * mul;
                    }
                }
            }

            Span<float> src = xform.BufSrc(0);
            ConvertLinearSrgbToScaledXyb(nativeR, nativeG, nativeB, src);

            Span<float> dst = xform.BufDst(0);
            xform.Run(0, src, dst, (nuint)kNumColors).IsOk.ShouldBeTrue();

            float maxErrR = 0.0f;
            float maxErrG = 0.0f;
            float maxErrB = 0.0f;

            for (int i = 0; i < kNumColors; ++i)
            {
                maxErrR = Math.Max(maxErrR, Math.Abs(nativeR[i] - dst[(3 * i) + 0]));
                maxErrG = Math.Max(maxErrG, Math.Abs(nativeG[i] - dst[(3 * i) + 1]));
                maxErrB = Math.Max(maxErrB, Math.Abs(nativeB[i] - dst[(3 * i) + 2]));
            }

            maxErrR.ShouldBeLessThan(8.7e-4f);
            maxErrG.ShouldBeLessThan(4.4e-4f);
            maxErrB.ShouldBeLessThan(5.2e-4f);
        }

        private static void ConvertLinearSrgbToScaledXyb(
            float[] nativeR,
            float[] nativeG,
            float[] nativeB,
            Span<float> interleavedScaledXyb)
        {
            Matrix3x3 m = OpsinParams.KOpsinAbsorbanceMatrix;
            Vector3 bias = OpsinParams.KOpsinAbsorbanceBias;

            for (int i = 0; i < nativeR.Length; ++i)
            {
                float r = nativeR[i];
                float g = nativeG[i];
                float b = nativeB[i];

                float mixed0 = Math.Max(0.0f, (m[0][0] * r) + (m[0][1] * g) + (m[0][2] * b) + bias[0]);
                float mixed1 = Math.Max(0.0f, (m[1][0] * r) + (m[1][1] * g) + (m[1][2] * b) + bias[1]);
                float mixed2 = Math.Max(0.0f, (m[2][0] * r) + (m[2][1] * g) + (m[2][2] * b) + bias[2]);

                mixed0 = MathF.Cbrt(mixed0) - MathF.Cbrt(bias[0]);
                mixed1 = MathF.Cbrt(mixed1) - MathF.Cbrt(bias[1]);
                mixed2 = MathF.Cbrt(mixed2) - MathF.Cbrt(bias[2]);

                float xybX = 0.5f * (mixed0 - mixed1);
                float xybY = 0.5f * (mixed0 + mixed1);
                float xybB = mixed2;

                interleavedScaledXyb[(3 * i) + 0] = (xybX + OpsinParams.KScaledXybOffset0) * OpsinParams.KScaledXybScale0;
                interleavedScaledXyb[(3 * i) + 1] = (xybY + OpsinParams.KScaledXybOffset1) * OpsinParams.KScaledXybScale1;
                interleavedScaledXyb[(3 * i) + 2] = (xybB - xybY + OpsinParams.KScaledXybOffset2) * OpsinParams.KScaledXybScale2;
            }
        }

        private static void VerifyPixelRoundTrip(
            CmsColorEncoding c,
            CmsColorEncoding cNative,
            float[] input,
            float[] output)
        {
            using ColorSpaceTransform xformFwd = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            using ColorSpaceTransform xformRev = new ColorSpaceTransform(JxlCms.GetDefaultCms());
            float intensityTarget = c.Tf.IsHLG() ? 1000.0f : Common.kDefaultIntensityTarget;

            xformFwd.Init(cNative, c, intensityTarget, KWidth, KNumThreads).IsOk.ShouldBeTrue();
            xformRev.Init(c, cNative, intensityTarget, KWidth, KNumThreads).IsOk.ShouldBeTrue();

            xformFwd.Run(0, input, xformFwd.BufDst(0), KWidth).IsOk.ShouldBeTrue();
            xformRev.Run(0, xformFwd.BufDst(0), output, KWidth).IsOk.ShouldBeTrue();

            for (int i = 0; i < input.Length; ++i)
            {
                Math.Abs(input[i] - output[i]).ShouldBeLessThan(7e-4f);
            }
        }

        private static float[] GenerateColor()
        {
            float[] interleaved = new float[KWidth * 3];

            for (int x = 0; x < 4; ++x)
            {
                float v = x / 3.0f;
                interleaved[(3 * x) + 0] = v;
                interleaved[(3 * x) + 1] = v;
                interleaved[(3 * x) + 2] = v;
            }

            for (int c = 0; c < 3; ++c)
            {
                interleaved[(3 * (4 + c)) + c]  = 0.08f + (c * 0.01f);
                interleaved[(3 * (7 + c)) + c]  = 0.75f + (c * 0.01f);
                interleaved[(3 * (10 + c)) + c] = 1.0f;
            }

            interleaved[(3 * 13) + 0] = 0.86f;
            interleaved[(3 * 13) + 1] = 0.16f;
            interleaved[(3 * 13) + 2] = 0.16f;
            interleaved[(3 * 14) + 0] = 0.16f;
            interleaved[(3 * 14) + 1] = 0.87f;
            interleaved[(3 * 14) + 2] = 0.16f;
            interleaved[(3 * 15) + 0] = 0.16f;
            interleaved[(3 * 15) + 1] = 0.16f;
            interleaved[(3 * 15) + 2] = 0.88f;

            return interleaved;
        }
    }
}
