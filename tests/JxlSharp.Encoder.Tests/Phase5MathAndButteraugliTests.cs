// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Unit tests for <see cref="EncLinalg"/> ported from <c>lib/jxl/enc_linalg_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncLinalgTests
    {
        private static void VerifyOrthogonalAndDiagonal(in Matrix2x2 a, in Vector2D d, in Matrix2x2 u)
        {
            double uTu00 = (u[0, 0] * u[0, 0]) + (u[1, 0] * u[1, 0]);
            double uTu01 = (u[0, 0] * u[0, 1]) + (u[1, 0] * u[1, 1]);
            double uTu11 = (u[0, 1] * u[0, 1]) + (u[1, 1] * u[1, 1]);

            uTu00.ShouldBe(1.0, 1e-10);
            uTu01.ShouldBe(0.0, 1e-10);
            uTu11.ShouldBe(1.0, 1e-10);

            double r00 = (u[0, 0] * d.X * u[0, 0]) + (u[1, 0] * d.Y * u[1, 0]);
            double r01 = (u[0, 0] * d.X * u[0, 1]) + (u[1, 0] * d.Y * u[1, 1]);
            double r11 = (u[0, 1] * d.X * u[0, 1]) + (u[1, 1] * d.Y * u[1, 1]);

            r00.ShouldBe(a[0, 0], 1e-10);
            r01.ShouldBe(a[0, 1], 1e-10);
            r11.ShouldBe(a[1, 1], 1e-10);
        }

        /// <summary>
        /// Verifies diagonalization of symmetric 2x2 matrices.
        /// </summary>
        [Test]
        public void ConvertToDiagonal_RandomSymmetricMatrices_SatisfiesDecomposition()
        {
            Matrix2x2 identity = new Matrix2x2(1.0, 0.0, 0.0, 1.0);

            EncLinalg.ConvertToDiagonal(in identity, out Vector2D dId, out Matrix2x2 uId);
            VerifyOrthogonalAndDiagonal(in identity, in dId, in uId);

            Rng rng = new Rng(0);

            for (int k = 0; k < 200; k++)
            {
                double    a00 = rng.UniformF(-10.0f, 10.0f);
                double    a01 = rng.UniformF(-10.0f, 10.0f);
                double    a11 = rng.UniformF(-10.0f, 10.0f);
                Matrix2x2 mat = new Matrix2x2(a00, a01, a01, a11);

                EncLinalg.ConvertToDiagonal(in mat, out Vector2D diag, out Matrix2x2 u);
                VerifyOrthogonalAndDiagonal(in mat, in diag, in u);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncOptimize"/> ported from <c>lib/jxl/enc_optimize_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncOptimizeTests
    {
        private readonly struct SimpleQuadraticFunction : IScaledConjugateGradientFunction
        {
            public double Compute(ReadOnlySpan<double> w, Span<double> df)
            {
                double x0 = w[0] - 1.0;
                double x1 = w[1] - 2.0;

                df[0] = -2.0 * x0;
                df[1] = -2.0 * x1;

                return (x0 * x0) + (x1 * x1);
            }
        }

        private sealed class PowerFunction : IScaledConjugateGradientFunction
        {
            private readonly double[] _x;
            private readonly double[] _y;

            public PowerFunction(double[] x, double[] y)
            {
                _x = x;
                _y = y;
            }

            public double Compute(ReadOnlySpan<double> w, Span<double> df)
            {
                double lossFunction = 0.0;
                df[0]               = 0.0;
                df[1]               = 0.0;
                df[2]               = 0.0;

                for (int ind = 0; ind < _y.Length; ind++)
                {
                    if (_x[ind] != 0.0)
                    {
                        double xPow = Math.Pow(_x[ind], w[1]);
                        double lf   = _y[ind] - ((w[0] * xPow) + w[2]);
                        df[0]       += 2.0 * lf * xPow;
                        df[1]       += 2.0 * lf * w[0] * xPow * Math.Log(_x[ind]);
                        df[2]       += 2.0 * lf;
                        lossFunction += lf * lf;
                    }
                }

                return lossFunction;
            }
        }

        /// <summary>
        /// Verifies optimization of a simple quadratic function.
        /// </summary>
        [Test]
        public void OptimizeWithScaledConjugateGradientMethod_Quadratic_FindsMinimum()
        {
            SimpleQuadraticFunction fn      = new SimpleQuadraticFunction();
            double[]                minimum = EncOptimize.OptimizeWithScaledConjugateGradientMethod(
                fn,
                new double[] { 0.0, 0.0 },
                1e-8,
                100000);

            minimum[0].ShouldBe(1.0, 1e-8);
            minimum[1].ShouldBe(2.0, 1e-8);
        }

        /// <summary>
        /// Verifies optimization of the nonlinear power function from <c>lib/jxl/enc_optimize_test.cc</c>.
        /// </summary>
        [Test]
        public void OptimizeWithScaledConjugateGradientMethod_PowerFunction_FindsMinimum()
        {
            double[] x = new double[10];
            double[] y = new double[10];

            for (int ind = 0; ind < 10; ind++)
            {
                x[ind] = 1.0 * ind;
                y[ind] = (2.0 * Math.Pow(x[ind], 3.0)) + 5.0;
            }

            PowerFunction fn      = new PowerFunction(x, y);
            double[]      minimum = EncOptimize.OptimizeWithScaledConjugateGradientMethod(
                fn,
                new double[] { 0.0, 0.0, 0.0 },
                0.01,
                100000);

            minimum[0].ShouldBe(2.0, 0.01);
            minimum[1].ShouldBe(3.0, 0.01);
            minimum[2].ShouldBe(5.0, 0.01);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncGammaCorrect"/> ported from <c>lib/jxl/gamma_correct_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncGammaCorrectTests
    {
        /// <summary>
        /// Verifies sRGB [0, 1] to linear [0, 1] roundtrip conversion.
        /// </summary>
        [Test]
        public void Srgb8AndLinearDirect_BoundaryAndContinuousValues_Roundtrips()
        {
            EncGammaCorrect.LinearToSrgb8Direct(-1e-4).ShouldBe(0.0, 1e-5);
            EncGammaCorrect.LinearToSrgb8Direct(0.0).ShouldBe(0.0, 1e-5);
            EncGammaCorrect.Srgb8ToLinearDirect(0.0).ShouldBe(0.0, 1e-5);
            EncGammaCorrect.Srgb8ToLinearDirect(1.0).ShouldBe(1.0, 1e-5);

            for (double srgb = 0.0; srgb <= 1.0; srgb += 0.01)
            {
                double linear    = EncGammaCorrect.Srgb8ToLinearDirect(srgb);
                double roundtrip = EncGammaCorrect.LinearToSrgb8Direct(linear);

                roundtrip.ShouldBe(srgb, 1e-4);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncConvolve"/>.
    /// </summary>
    [TestFixture]
    public class EncConvolveTests
    {
        /// <summary>
        /// Verifies 5x5 separable convolution on a constant plane.
        /// </summary>
        [Test]
        public unsafe void Separable5_IdentityWeights_PreservesConstantPlane()
        {
            StatusOr<ImagePlane<float>> inStatus  = ImagePlane<float>.Create(null, 16, 16);
            StatusOr<ImagePlane<float>> outStatus = ImagePlane<float>.Create(null, 16, 16);

            inStatus.IsOk.ShouldBeTrue();
            outStatus.IsOk.ShouldBeTrue();

            using ImagePlane<float> inPlane  = inStatus.Value!;
            using ImagePlane<float> outPlane = outStatus.Value!;

            for (nuint y = 0; y < 16; y++)
            {
                float* row = inPlane.Row(y);

                for (int x = 0; x < 16; x++)
                {
                    row[x] = 3.5f;
                }
            }

            WeightsSeparable5 weights = new WeightsSeparable5(0.4f, 0.25f, 0.05f, 0.4f, 0.25f, 0.05f);

            EncConvolve.Separable5(inPlane, new Rect(0, 0, 16, 16), weights, null, outPlane).IsOk.ShouldBeTrue();
            outPlane.Row(8)[8].ShouldBe(3.5f, 1e-3f);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PsychoImage"/>.
    /// </summary>
    [TestFixture]
    public class PsychoImageTests
    {
        /// <summary>
        /// Verifies creation and disposal of <see cref="PsychoImage"/>.
        /// </summary>
        [Test]
        public void Create_ValidDimensions_AllocatesFrequencyBands()
        {
            using PsychoImage pi = new PsychoImage();

            pi.Hf.ShouldNotBeNull();
            pi.Uhf.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="BlurTemp"/>.
    /// </summary>
    [TestFixture]
    public class BlurTempTests
    {
        /// <summary>
        /// Verifies creation and disposal of <see cref="BlurTemp"/>.
        /// </summary>
        [Test]
        public void Dispose_ReleasesTemporaryPlane()
        {
            using BlurTemp temp = new BlurTemp();

            temp.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ButteraugliComparator"/>.
    /// </summary>
    [TestFixture]
    public class ButteraugliComparatorTests
    {
        /// <summary>
        /// Verifies creating a <see cref="ButteraugliComparator"/> from a linear RGB reference image.
        /// </summary>
        [Test]
        public unsafe void Make_ValidLinearRgb_CreatesComparator()
        {
            StatusOr<Image3<float>> imgStatus = Image3<float>.Create(null, 16, 16);
            imgStatus.IsOk.ShouldBeTrue();

            using Image3<float> rgb = imgStatus.Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 16; y++)
                {
                    float* row = rgb.PlaneRow(c, y);

                    for (int x = 0; x < 16; x++)
                    {
                        row[x] = 0.5f;
                    }
                }
            }

            ButteraugliParams               baParams = new ButteraugliParams();
            StatusOr<ButteraugliComparator> compOr   = ButteraugliComparator.Make(rgb, in baParams);

            compOr.IsOk.ShouldBeTrue();

            using ButteraugliComparator comp = compOr.Value!;
            comp.Mask(out ImagePlane<float> mask).IsOk.ShouldBeTrue();
            mask.Dispose();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Butteraugli"/> ported from <c>lib/jxl/butteraugli/butteraugli_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class ButteraugliTests
    {
        /// <summary>
        /// Verifies that identical images yield zero Butteraugli distance and positive distance when perturbed.
        /// </summary>
        [Test]
        public unsafe void ButteraugliInterface_IdenticalAndPerturbedImages_ReturnsExpectedDistance()
        {
            StatusOr<Image3<float>> rgb0Status = Image3<float>.Create(null, 32, 32);
            StatusOr<Image3<float>> rgb1Status = Image3<float>.Create(null, 32, 32);

            rgb0Status.IsOk.ShouldBeTrue();
            rgb1Status.IsOk.ShouldBeTrue();

            using Image3<float> rgb0 = rgb0Status.Value!;
            using Image3<float> rgb1 = rgb1Status.Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 32; y++)
                {
                    float* r0 = rgb0.PlaneRow(c, y);
                    float* r1 = rgb1.PlaneRow(c, y);

                    for (int x = 0; x < 32; x++)
                    {
                        float v = ((x + (int)y) % 16) / 16.0f;
                        r0[x]   = v;
                        r1[x]   = v;
                    }
                }
            }

            ButteraugliParams baParams = new ButteraugliParams();
            ImagePlane<float> diffmap  = new ImagePlane<float>();

            Butteraugli.ButteraugliInterface(rgb0, rgb1, in baParams, ref diffmap, out double diffval).ShouldBeTrue();

            using (diffmap)
            {
                diffval.ShouldBe(0.0, 1e-5);
            }

            rgb1.PlaneRow(1, 16)[16] = 1.0f;

            ImagePlane<float> diffmap2 = new ImagePlane<float>();
            Butteraugli.ButteraugliInterface(rgb0, rgb1, in baParams, ref diffmap2, out double diffval2).ShouldBeTrue();

            using (diffmap2)
            {
                diffval2.ShouldBeGreaterThan(0.0);

                StatusOr<Image3<float>> heatStatus = Butteraugli.CreateHeatMapImage(diffmap2, 1.0, 3.0);
                heatStatus.IsOk.ShouldBeTrue();
                heatStatus.Value!.Dispose();
            }

            double fuzzy = Butteraugli.ButteraugliFuzzyClass(1.0);
            fuzzy.ShouldBeGreaterThan(0.0);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlButteraugliComparator"/>.
    /// </summary>
    [TestFixture]
    public class JxlButteraugliComparatorTests
    {
        /// <summary>
        /// Verifies quality score thresholds and reference comparison.
        /// </summary>
        [Test]
        public void GoodAndBadQualityScore_ReturnsStandardThresholds()
        {
            ButteraugliParams              baParams = new ButteraugliParams();
            using JxlButteraugliComparator comp     = new JxlButteraugliComparator(in baParams, JxlCms.GetDefaultCms());

            comp.GoodQualityScore().ShouldBeLessThan(comp.BadQualityScore());
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncComparator"/>.
    /// </summary>
    [TestFixture]
    public class EncComparatorTests
    {
        /// <summary>
        /// Verifies computing Butteraugli score between two <see cref="ImageBundle"/> instances.
        /// </summary>
        [Test]
        public unsafe void ComputeScore_IdenticalImageBundles_ReturnsZeroScore()
        {
            ImageMetadata metadata = new ImageMetadata();
            metadata.SetUintSamples(8);
            metadata.ColorEncoding = ColorEncoding.SRGB(false);

            using ImageBundle ib0 = new ImageBundle(null, metadata);
            using ImageBundle ib1 = new ImageBundle(null, metadata);

            StatusOr<Image3<float>> img0 = Image3<float>.Create(null, 16, 16);
            StatusOr<Image3<float>> img1 = Image3<float>.Create(null, 16, 16);

            img0.IsOk.ShouldBeTrue();
            img1.IsOk.ShouldBeTrue();

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 16; y++)
                {
                    float* r0 = img0.Value!.PlaneRow(c, y);
                    float* r1 = img1.Value!.PlaneRow(c, y);

                    for (int x = 0; x < 16; x++)
                    {
                        r0[x] = 0.25f;
                        r1[x] = 0.25f;
                    }
                }
            }

            ib0.SetFromImage(img0.Value!, ColorEncoding.SRGB(false)).IsOk.ShouldBeTrue();
            ib1.SetFromImage(img1.Value!, ColorEncoding.SRGB(false)).IsOk.ShouldBeTrue();

            ButteraugliParams              baParams = new ButteraugliParams();
            using JxlButteraugliComparator comp     = new JxlButteraugliComparator(in baParams, JxlCms.GetDefaultCms());
            ImagePlane<float>?             diffmap  = null;

            EncComparator.ComputeScore(ib0, ib1, comp, JxlCms.GetDefaultCms(), out float score, ref diffmap).IsOk.ShouldBeTrue();
            diffmap?.Dispose();
            score.ShouldBe(0.0f, 1e-4f);
        }
    }
}
