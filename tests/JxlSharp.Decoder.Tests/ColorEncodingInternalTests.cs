// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ColorEncoding"/>, <see cref="ColorSpaceTransform"/>,
    /// <see cref="PackSigned"/>, <see cref="FrameDimensions"/>, and <see cref="LoopFilter"/>.
    /// Ported from <c>lib/jxl/color_encoding_internal_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class ColorEncodingInternalTests
    {
        /// <summary>
        /// Verifies that setting and getting white point, primaries, and gamma round-trip across standard encodings.
        /// Ported from <c>ColorEncodingTest.RoundTripAll</c>.
        /// </summary>
        [Test]
        public void RoundTripAll()
        {
            ColorEncoding[] presets =
            {
                ColorEncoding.SRGB(isGray: false),
                ColorEncoding.SRGB(isGray: true),
                ColorEncoding.LinearSRGB(isGray: false),
                ColorEncoding.LinearSRGB(isGray: true),
            };

            foreach (ColorEncoding cOriginal in presets)
            {
                ColorEncoding cWp = new ColorEncoding();
                cWp.SetWhitePoint(cOriginal.GetWhitePoint()).IsOk.ShouldBeTrue();
                cWp.GetWhitePointType().ShouldBe(cOriginal.GetWhitePointType());

                if (cOriginal.HasPrimaries())
                {
                    ColorEncoding cPrim = new ColorEncoding();
                    cOriginal.GetPrimaries(out PrimariesCIExy p).IsOk.ShouldBeTrue();
                    cPrim.SetPrimaries(p).IsOk.ShouldBeTrue();
                    cPrim.GetPrimariesType().ShouldBe(cOriginal.GetPrimariesType());
                }
            }
        }

        /// <summary>
        /// Verifies custom white point round-tripping.
        /// Ported from <c>ColorEncodingTest.CustomWhitePoint</c>.
        /// </summary>
        [Test]
        public void CustomWhitePoint()
        {
            ColorEncoding c = new ColorEncoding();
            CIExy xyIn      = new CIExy
            {
                X = 0.8,
                Y = 0.01,
            };

            c.SetWhitePoint(xyIn).IsOk.ShouldBeTrue();
            CIExy xy = c.GetWhitePoint();

            ColorEncoding c2 = new ColorEncoding();
            c2.SetWhitePoint(xy).IsOk.ShouldBeTrue();
            c.SameColorSpace(c2).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies custom primaries round-tripping.
        /// Ported from <c>ColorEncodingTest.CustomPrimaries</c>.
        /// </summary>
        [Test]
        public void CustomPrimaries()
        {
            ColorEncoding c      = new ColorEncoding();
            PrimariesCIExy xyIn  = default;
            xyIn.R.X             = -0.01;
            xyIn.R.Y             = 0.2;
            xyIn.G.X             = 0.4;
            xyIn.G.Y             = 0.401;
            xyIn.B.X             = 1.1;
            xyIn.B.Y             = -1.2;

            c.SetPrimaries(xyIn).IsOk.ShouldBeTrue();
            c.GetPrimaries(out PrimariesCIExy xy).IsOk.ShouldBeTrue();

            ColorEncoding c2 = new ColorEncoding();
            c2.SetPrimaries(xy).IsOk.ShouldBeTrue();
            c.SameColorSpace(c2).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies custom gamma validation and round-tripping.
        /// Ported from <c>ColorEncodingTest.CustomGamma</c>.
        /// </summary>
        [Test]
        public void CustomGamma()
        {
            ColorEncoding c = new ColorEncoding();
            c.Tf.SetGamma(0.0).IsOk.ShouldBeFalse();
            c.Tf.SetGamma(-1e-6).IsOk.ShouldBeFalse();
            c.Tf.SetGamma(1.001).IsOk.ShouldBeFalse();

            c.Tf.SetGamma(1.0).IsOk.ShouldBeTrue();
            c.Tf.HaveGamma.ShouldBeFalse();
            c.Tf.IsLinear().ShouldBeTrue();

            c.Tf.SetGamma(0.123).IsOk.ShouldBeTrue();
            c.Tf.HaveGamma.ShouldBeTrue();
            double gamma = c.Tf.GetGamma();

            ColorEncoding c2 = new ColorEncoding();
            c2.Tf.SetGamma(gamma).IsOk.ShouldBeTrue();
            c.SameColorEncoding(c2).ShouldBeTrue();
            c2.Tf.HaveGamma.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies conversion between internal <see cref="ColorEncoding"/> and external <see cref="JxlColorEncoding"/>.
        /// Ported from <c>ColorEncodingTest.InternalExternalConversion</c>.
        /// </summary>
        [Test]
        public void InternalExternalConversion()
        {
            Rng rng = new Rng(12345);

            for (int i = 0; i < 100; i++)
            {
                VerifySingleInternalExternalConversion(rng);
            }
        }

        /// <summary>
        /// Verifies <see cref="ColorEncoding"/> bundle serialization with custom white point, custom primaries, and custom gamma.
        /// </summary>
        [Test]
        public void BundleSerializationRoundTrip()
        {
            ColorEncoding src = new ColorEncoding();
            src.SetColorSpace(ColorSpace.RGB);
            src.SetWhitePoint(new CIExy { X = 0.31, Y = 0.32 }).IsOk.ShouldBeTrue();
            src.SetPrimaries(new PrimariesCIExy
            {
                R = new CIExy { X = 0.64, Y = 0.33 },
                G = new CIExy { X = 0.30, Y = 0.60 },
                B = new CIExy { X = 0.15, Y = 0.06 },
            }).IsOk.ShouldBeTrue();
            src.Tf.SetGamma(0.454545).IsOk.ShouldBeTrue();
            src.SetRenderingIntent(RenderingIntent.Perceptual);
            src.CreateICC().IsOk.ShouldBeTrue();

            BitWriter writer = new BitWriter();
            Bundle.Write(src, writer, LayerType.Header, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            ColorEncoding dst = new ColorEncoding();
            BitReader reader  = new BitReader(writer.GetSpan());
            Bundle.Read(reader, dst).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            dst.SameColorEncoding(src).ShouldBeTrue();
            dst.Description().ShouldNotBeNullOrEmpty();
        }

        /// <summary>
        /// Verifies <see cref="ColorSpaceTransform"/> execution between sRGB and Linear sRGB.
        /// </summary>
        [Test]
        public void ColorSpaceTransformRunTest()
        {
            JxlCmsInterface cms  = JxlCms.GetDefaultCms();
            ColorEncoding cSrc   = ColorEncoding.SRGB(isGray: false);
            ColorEncoding cDst   = ColorEncoding.LinearSRGB(isGray: false);

            using ColorSpaceTransform transform = new ColorSpaceTransform(cms);
            transform.Init(cSrc, cDst, 255.0f, 4, 1).IsOk.ShouldBeTrue();

            float[]? srcBuf = transform.BufSrc(0);
            float[]? dstBuf = transform.BufDst(0);
            srcBuf.ShouldNotBeNull();
            dstBuf.ShouldNotBeNull();

            for (int i = 0; i < 12; i++)
            {
                srcBuf[i] = 0.5f;
            }

            transform.Run(0, srcBuf, dstBuf, 4).IsOk.ShouldBeTrue();
            dstBuf[0].ShouldBeInRange(0.20f, 0.23f);
        }

        /// <summary>
        /// Verifies <see cref="PackSigned.Pack"/> and <see cref="PackSigned.Unpack"/> across negative, zero, and positive integers.
        /// </summary>
        [Test]
        public void PackSignedRoundTripTest()
        {
            PackSigned.Pack(0).ShouldBe(0u);
            PackSigned.Pack(-1).ShouldBe(1u);
            PackSigned.Pack(1).ShouldBe(2u);
            PackSigned.Pack(-2).ShouldBe(3u);
            PackSigned.Pack(2).ShouldBe(4u);

            for (int v = -10000; v <= 10000; v++)
            {
                uint packed  = PackSigned.Pack(v);
                int unpacked = PackSigned.Unpack(packed);
                unpacked.ShouldBe(v);
            }
        }

        /// <summary>
        /// Verifies <see cref="FrameDimensions"/> computation and group rectangle helpers.
        /// </summary>
        [Test]
        public void FrameDimensionsTest()
        {
            FrameDimensions dims = default;
            dims.Set(
                xsizePx: 300,
                ysizePx: 200,
                groupSizeShift: 1,
                maxHshift: 0,
                maxVshift: 0,
                modularMode: false,
                upsampling: 1);

            dims.XSize.ShouldBe((nuint)300);
            dims.YSize.ShouldBe((nuint)200);
            dims.GroupDim.ShouldBe((nuint)256);
            dims.DcGroupDim.ShouldBe((nuint)2048);
            dims.XSizeBlocks.ShouldBe((nuint)38);
            dims.YSizeBlocks.ShouldBe((nuint)25);
            dims.XSizePadded.ShouldBe((nuint)304);
            dims.YSizePadded.ShouldBe((nuint)200);
            dims.XSizeGroups.ShouldBe((nuint)2);
            dims.YSizeGroups.ShouldBe((nuint)1);
            dims.NumGroups.ShouldBe((nuint)2);
            dims.NumDcGroups.ShouldBe((nuint)1);

            Rect g0 = dims.GroupRect(0);
            g0.X0.ShouldBe(0);
            g0.XSize.ShouldBe(256);

            Rect g1 = dims.GroupRect(1);
            g1.X0.ShouldBe(256);
            g1.XSize.ShouldBe(44);

            Rect bg1 = dims.BlockGroupRect(1);
            bg1.X0.ShouldBe(32);
            bg1.XSize.ShouldBe(6);

            Rect dcg0 = dims.DCGroupRect(0);
            dcg0.XSize.ShouldBe(38);
            dcg0.YSize.ShouldBe(25);
        }

        /// <summary>
        /// Verifies <see cref="LoopFilter"/> serialization and <see cref="LoopFilter.Padding"/>.
        /// </summary>
        [Test]
        public void LoopFilterRoundTripTest()
        {
            LoopFilter lf          = new LoopFilter();
            lf.Padding().ShouldBe((nuint)4);

            lf.Gab                 = true;
            lf.GabCustom           = true;
            lf.GabXWeight1         = 0.1f;
            lf.GabXWeight2         = 0.05f;
            lf.GabYWeight1         = 0.1f;
            lf.GabYWeight2         = 0.05f;
            lf.GabBWeight1         = 0.1f;
            lf.GabBWeight2         = 0.05f;
            lf.EpfIters            = 3;
            lf.EpfSharpCustom      = true;
            lf.EpfWeightCustom     = true;
            lf.EpfSigmaCustom      = true;
            lf.Padding().ShouldBe((nuint)7);

            BitWriter writer = new BitWriter();
            Bundle.Write(lf, writer, LayerType.Header, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            LoopFilter decoded = new LoopFilter();
            BitReader reader   = new BitReader(writer.GetSpan());
            Bundle.Read(reader, decoded).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            decoded.Gab.ShouldBeTrue();
            decoded.GabCustom.ShouldBeTrue();
            decoded.EpfIters.ShouldBe(3u);
            decoded.EpfSharpCustom.ShouldBeTrue();
            decoded.EpfWeightCustom.ShouldBeTrue();
            decoded.EpfSigmaCustom.ShouldBeTrue();
        }

        private static void VerifySingleInternalExternalConversion(Rng rng)
        {
            ColorEncoding sourceInternal      = new ColorEncoding();
            ColorEncoding destinationInternal = new ColorEncoding();

            sourceInternal.SetColorSpace((ColorSpace)(rng.NextUInt64() % 4));
            CIExy wp = new CIExy
            {
                X = rng.UniformF(0.25f, 0.75f),
                Y = rng.UniformF(0.25f, 0.75f),
            };
            sourceInternal.SetWhitePoint(wp).IsOk.ShouldBeTrue();

            if (sourceInternal.HasPrimaries())
            {
                PrimariesCIExy primaries = new PrimariesCIExy
                {
                    R = new CIExy { X = rng.UniformF(0.25f, 0.75f), Y = rng.UniformF(0.25f, 0.75f) },
                    G = new CIExy { X = rng.UniformF(0.25f, 0.75f), Y = rng.UniformF(0.25f, 0.75f) },
                    B = new CIExy { X = rng.UniformF(0.25f, 0.75f), Y = rng.UniformF(0.25f, 0.75f) },
                };
                sourceInternal.SetPrimaries(primaries).IsOk.ShouldBeTrue();
            }

            sourceInternal.Tf.SetGamma(rng.UniformF(0.25f, 0.75f)).IsOk.ShouldBeTrue();
            sourceInternal.SetRenderingIntent((RenderingIntent)(rng.NextUInt64() % 4));

            JxlColorEncoding external = sourceInternal.ToExternal();
            destinationInternal.Storage.FromExternal(external).IsOk.ShouldBeTrue();

            destinationInternal.GetColorSpace().ShouldBe(sourceInternal.GetColorSpace());
            destinationInternal.GetWhitePointType().ShouldBe(sourceInternal.GetWhitePointType());

            CIExy srcWp = sourceInternal.GetWhitePoint();
            CIExy dstWp = destinationInternal.GetWhitePoint();
            dstWp.X.ShouldBe(srcWp.X);
            dstWp.Y.ShouldBe(srcWp.Y);
            destinationInternal.Tf.HaveGamma.ShouldBe(sourceInternal.Tf.HaveGamma);
            destinationInternal.GetRenderingIntent().ShouldBe(sourceInternal.GetRenderingIntent());
        }
    }
}
