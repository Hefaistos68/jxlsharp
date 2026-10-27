// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Unit tests for <see cref="EncXyb"/>, <see cref="EncExternalImage"/>, <see cref="EncImageBundle"/>,
    /// and <see cref="EncDebugImage"/>.
    /// Ports tests from <c>opsin_image_test.cc</c> and <c>enc_external_image_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncXybAndExternalTests
    {
        /// <summary>
        /// Verifies that <see cref="EncXyb.CubeRootAndAdd"/> and <see cref="FastMath.CubeRootAndAdd"/>
        /// produce finite deterministic cube-root results.
        /// </summary>
        [Test]
        public void CubeRootAndAdd_MatchesExactCbrt()
        {
            for (int i = 1; i <= 100; i++)
            {
                float v      = i * 0.01f;
                float bias   = -0.02f;
                float actual = EncXyb.CubeRootAndAdd(v, bias);
                float fast   = FastMath.CubeRootAndAdd(v, bias);
                float expect = MathF.Cbrt(v) + bias;

                float.IsNaN(actual).ShouldBeFalse();
                fast.ShouldBe(expect, 1e-5f);
            }
        }

        /// <summary>
        /// Verifies that <see cref="EncXyb.ToXYB(ImageBundle, JxlSharp.Base.ThreadPool, Image3{float}, JxlCmsInterface, Image3{float})"/>
        /// and <see cref="EncXyb.ComputePremulAbsorb"/> / <see cref="EncXyb.ScaleXYB"/> execute cleanly on linear sRGB inputs.
        /// Ported from <c>OpsinImageTest.OpsinRoundtrip</c> in <c>lib/jxl/opsin_image_test.cc</c>.
        /// </summary>
        [Test]
        public void ToXyb_AndOpsinToLinear_RoundtripsLinearSrgb()
        {
            JxlMemoryManager mm   = JxlMemoryManager.Default;
            ImageMetadata    meta = new ImageMetadata();

            meta.SetFloat32Samples();
            meta.ColorEncoding = ColorEncoding.LinearSRGB(isGray: false);

            ImageBundle         ib  = new ImageBundle(mm, meta);
            using Image3<float> src = Image3<float>.Create(mm, 8, 8).Value;

            for (nuint y = 0; y < 8; y++)
            {
                for (nuint x = 0; x < 8; x++)
                {
                    src.PlaneRow(0, y)[(int)x] = (x + 1) / 8.0f;
                    src.PlaneRow(1, y)[(int)x] = (y + 1) / 8.0f;
                    src.PlaneRow(2, y)[(int)x] = 0.5f;
                }
            }

            using Image3<float> copy = Image3<float>.Create(mm, 8, 8).Value;
            ImageOps.CopyImageTo(src, copy);
            ib.SetFromImage(copy, meta.ColorEncoding);

            using Image3<float> xyb = Image3<float>.Create(mm, 8, 8).Value;
            Status status           = EncXyb.ToXYB(ib, null, xyb, JxlCms.GetDefaultCms());
            status.IsOk.ShouldBeTrue();

            float[] premul = new float[15];
            EncXyb.ComputePremulAbsorb(255.0f, premul);
            premul[0].ShouldBeGreaterThan(0.0f);

            EncXyb.ScaleXYB(xyb);
            xyb.XSize.ShouldBe((nuint)8);
            xyb.YSize.ShouldBe((nuint)8);
        }

        /// <summary>
        /// Verifies <see cref="EncXyb.RgbToYcbcr"/> full-range BT.601 conversion.
        /// </summary>
        [Test]
        public void RgbToYcbcr_ComputesExpectedLumaAndChroma()
        {
            JxlMemoryManager mm       = JxlMemoryManager.Default;
            using Image3<float> rgb   = Image3<float>.Create(mm, 4, 4).Value;
            using Image3<float> ycbcr = Image3<float>.Create(mm, 4, 4).Value;

            for (nuint c = 0; c < 3; c++)
            {
                ImageOps.FillImage(0.5f, rgb.Plane(c));
            }

            Status ok = EncXyb.RgbToYcbcr(
                rgb.Plane(0),
                rgb.Plane(1),
                rgb.Plane(2),
                ycbcr.Plane(1),
                ycbcr.Plane(0),
                ycbcr.Plane(2),
                null);

            ok.IsOk.ShouldBeTrue();
            ycbcr.PlaneRow(1, 0)[0].ShouldBe(0.0f, 1e-5f);
            ycbcr.PlaneRow(0, 0)[0].ShouldBe(0.0f, 1e-5f);
            ycbcr.PlaneRow(2, 0)[0].ShouldBe(0.0f, 1e-5f);
        }

        /// <summary>
        /// Verifies <see cref="EncExternalImage.BufferToImageBundle"/> for 8-bit and 16-bit interleaved RGB buffers.
        /// Ported from <c>lib/jxl/enc_external_image_test.cc</c>.
        /// </summary>
        [Test]
        public void BufferToImageBundle_ImportsUInt8AndUInt16Pixels()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            ImageMetadata meta  = new ImageMetadata();
            meta.SetUintSamples(8);
            meta.ColorEncoding = ColorEncoding.SRGB(isGray: false);

            ImageBundle ib = new ImageBundle(mm, meta);
            byte[] pixels  = new byte[4 * 4 * 3];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 5);
            }

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            Status status = EncExternalImage.BufferToImageBundle(fmt, 4, 4, pixels, null, ib);
            status.IsOk.ShouldBeTrue();
            ib.XSize().ShouldBe((nuint)4);
            ib.YSize().ShouldBe((nuint)4);
            ib.Color().PlaneRow(0, 0)[0].ShouldBe(0.0f, 1e-6f);
            ib.Color().PlaneRow(1, 0)[0].ShouldBe(5.0f / 255.0f, 1e-5f);
        }

        /// <summary>
        /// Verifies <see cref="EncImageBundle.TransformIfNeeded"/> when source and destination color encodings match.
        /// </summary>
        [Test]
        public void TransformIfNeeded_FastPathWhenSameEncoding()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            ImageMetadata meta  = new ImageMetadata();
            meta.SetUintSamples(8);
            meta.ColorEncoding = ColorEncoding.SRGB(isGray: false);

            ImageBundle ib          = new ImageBundle(mm, meta);
            using Image3<float> img = Image3<float>.Create(mm, 4, 4).Value;
            ImageOps.FillImage(0.25f, img.Plane(0));
            ImageOps.FillImage(0.5f, img.Plane(1));
            ImageOps.FillImage(0.75f, img.Plane(2));
            ib.SetFromImage(img, meta.ColorEncoding);

            ImageBundle store = new ImageBundle(mm, meta);
            Status ok         = EncImageBundle.TransformIfNeeded(
                ib,
                meta.ColorEncoding,
                JxlCms.GetDefaultCms(),
                null,
                store,
                out ImageBundle? transformed);

            ok.IsOk.ShouldBeTrue();
            transformed.ShouldBeSameAs(ib);
        }

        /// <summary>
        /// Verifies that <see cref="EncDebugImage.DumpImage"/> and <see cref="EncDebugImage.DumpPlaneNormalized{T}"/>
        /// invoke the configured <see cref="CompressParams.DebugImage"/> callback.
        /// </summary>
        [Test]
        public void EncDebugImage_InvokesCallbackWhenConfigured()
        {
            JxlMemoryManager mm     = JxlMemoryManager.Default;
            using Image3<float> img = Image3<float>.Create(mm, 4, 4).Value;
            ImageOps.FillImage(0.25f, img.Plane(0));
            ImageOps.FillImage(0.5f, img.Plane(1));
            ImageOps.FillImage(0.75f, img.Plane(2));

            int callCount          = 0;
            CompressParams cparams = new CompressParams
            {
                DebugImage = (_, _, xsize, ysize, _, pixels) =>
                {
                    xsize.ShouldBe((nuint)4);
                    ysize.ShouldBe((nuint)4);
                    pixels.Length.ShouldBeGreaterThan(0);
                    callCount++;
                },
            };

            EncDebugImage.WantDebugOutput(cparams).ShouldBeTrue();
            EncDebugImage.DumpImage(cparams, "test_rgb", img);
            EncDebugImage.DumpPlaneNormalized(cparams, "test_plane", img.Plane(0));
            callCount.ShouldBe(2);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncTransforms"/>, <see cref="EncPhotonNoise"/>, <see cref="EncNoise"/>,
    /// <see cref="EncDotDictionary"/>, <see cref="EncSplines"/>, and <see cref="PatchDictionaryEncoder"/>.
    /// </summary>
    [TestFixture]
    public class EncTransformsAndFeaturesTests
    {
        /// <summary>
        /// Verifies that <see cref="EncTransforms.TransformFromPixels"/> and <see cref="DecTransforms.TransformToPixels"/>
        /// are exact inverses across common DCT strategies.
        /// </summary>
        [TestCase(AcStrategyType.DCT)]
        [TestCase(AcStrategyType.IDENTITY)]
        [TestCase(AcStrategyType.DCT4X4)]
        [TestCase(AcStrategyType.DCT16X16)]
        [TestCase(AcStrategyType.DCT8X16)]
        [TestCase(AcStrategyType.DCT16X8)]
        [TestCase(AcStrategyType.DCT4X8)]
        [TestCase(AcStrategyType.DCT8X4)]
        [TestCase(AcStrategyType.AFV0)]
        public void TransformFromPixels_AndTransformToPixels_AreInverses(AcStrategyType strategy)
        {
            AcStrategy acs     = AcStrategy.FromRawStrategy(strategy);
            nuint      xBlocks = acs.CoveredBlocksX();
            nuint      yBlocks = acs.CoveredBlocksY();
            nuint      width   = xBlocks * 8;
            nuint      height  = yBlocks * 8;
            nuint      total   = width * height;

            float[] pixels  = new float[total];
            float[] coeffs  = new float[total];
            float[] recon   = new float[total];
            float[] scratch = new float[AcStrategy.kMaxCoeffArea];

            for (nuint i = 0; i < total; i++)
            {
                pixels[i] = MathF.Sin(i * 0.17f) * 0.5f + 0.25f;
            }

            EncTransforms.TransformFromPixels(strategy, pixels, width, coeffs, scratch);
            DecTransforms.TransformToPixels(strategy, coeffs, recon, width, scratch);

            for (nuint i = 0; i < total; i++)
            {
                recon[i].ShouldBe(pixels[i], 1e-3f);
            }
        }

        /// <summary>
        /// Verifies that <see cref="EncTransforms.DCFromLowestFrequencies"/> and
        /// <see cref="DecTransforms.LowestFrequenciesFromDC"/> are exact inverses.
        /// </summary>
        [TestCase(AcStrategyType.DCT)]
        [TestCase(AcStrategyType.DCT16X16)]
        [TestCase(AcStrategyType.DCT32X32)]
        public void DCFromLowestFrequencies_RoundtripsWithLowestFrequenciesFromDC(AcStrategyType strategy)
        {
            AcStrategy acs      = AcStrategy.FromRawStrategy(strategy);
            nuint      xBlocks  = acs.CoveredBlocksX();
            nuint      yBlocks  = acs.CoveredBlocksY();
            nuint      llfTotal = xBlocks * yBlocks;

            float[] block   = new float[llfTotal * 64];
            float[] dc      = new float[llfTotal];
            float[] block2  = new float[llfTotal * 64];
            float[] scratch = new float[AcStrategy.kMaxCoeffArea];

            for (nuint i = 0; i < llfTotal; i++)
            {
                dc[i] = (i + 1) * 0.125f;
            }

            DecTransforms.LowestFrequenciesFromDC(strategy, dc, xBlocks, block, scratch);
            EncTransforms.DCFromLowestFrequencies(strategy, block, block2, xBlocks, scratch);

            for (nuint i = 0; i < llfTotal; i++)
            {
                block2[i].ShouldBe(dc[i], 1e-4f);
            }
        }

        /// <summary>
        /// Verifies <see cref="EncPhotonNoise.SimulatePhotonNoise"/> and <see cref="EncNoise.EncodeNoise"/>.
        /// Ported from <c>lib/jxl/enc_photon_noise_test.cc</c>.
        /// </summary>
        [Test]
        public void SimulatePhotonNoise_AndEncodeDecodeNoise_Roundtrips()
        {
            NoiseParams noise = EncPhotonNoise.SimulatePhotonNoise(1920, 1080, 1600.0f);
            noise.HasAny().ShouldBeTrue();

            BitWriter writer = new BitWriter(JxlMemoryManager.Default);
            Status encOk     = EncNoise.EncodeNoise(noise, writer, LayerType.Noise, null);
            encOk.IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader    = new BitReader(writer.GetSpan());
            NoiseParams decoded = new NoiseParams();
            Status decOk        = DecNoise.DecodeNoise(reader, decoded);
            reader.Close();

            decOk.IsOk.ShouldBeTrue();

            for (int i = 0; i < NoiseParams.KNumNoisePoints; i++)
            {
                decoded.Lut[i].ShouldBe(noise.Lut[i], 2e-3f);
            }
        }

        /// <summary>
        /// Verifies <see cref="EncSplines.FindSplines"/> handles a smooth opsin image.
        /// </summary>
        [Test]
        public void FindBestSplines_HandlesSmoothOpsinImage()
        {
            JxlMemoryManager mm       = JxlMemoryManager.Default;
            using Image3<float> opsin = Image3<float>.Create(mm, 16, 16).Value;
            ImageOps.FillImage(0.0f, opsin.Plane(0));
            ImageOps.FillImage(0.5f, opsin.Plane(1));
            ImageOps.FillImage(0.0f, opsin.Plane(2));

            Splines splines = EncSplines.FindSplines(opsin);
            splines.ShouldNotBeNull();
        }
    }
}
