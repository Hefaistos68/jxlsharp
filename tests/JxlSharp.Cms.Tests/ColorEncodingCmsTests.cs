// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Cms.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CmsColorEncoding"/>, <see cref="Customxy"/>, and <see cref="CustomTransferFunction"/>
    /// (ported from <c>lib/jxl/color_encoding_internal_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class ColorEncodingCmsTests
    {
        /// <summary>
        /// Returns all 216 synthesizable color encoding descriptors matching <c>jxl::test::AllEncodings()</c>.
        /// </summary>
        /// <returns>List of <see cref="CmsColorEncoding"/> instances.</returns>
        public static List<CmsColorEncoding> AllEncodings()
        {
            List<CmsColorEncoding> list    = new List<CmsColorEncoding>(216);
            WhitePoint[] whitePoints       = { WhitePoint.D65, WhitePoint.E, WhitePoint.DCI };
            Primaries[] primariesList      = { Primaries.SRGB, Primaries.P2100, Primaries.P3 };
            TransferFunction[] tfs         = { TransferFunction.Tf709, TransferFunction.Linear, TransferFunction.SRGB, TransferFunction.PQ, TransferFunction.DCI, TransferFunction.HLG };
            RenderingIntent[] intents      = { RenderingIntent.Perceptual, RenderingIntent.Relative, RenderingIntent.Saturation, RenderingIntent.Absolute };

            foreach (WhitePoint wp in whitePoints)
            {
                foreach (Primaries pr in primariesList)
                {
                    foreach (TransferFunction tf in tfs)
                    {
                        foreach (RenderingIntent ri in intents)
                        {
                            CmsColorEncoding c = new CmsColorEncoding
                            {
                                ColorSpace      = ColorSpace.RGB,
                                WhitePoint      = wp,
                                Primaries       = pr,
                                RenderingIntent = ri,
                            };
                            c.Tf.SetTransferFunction(tf);
                            c.CreateICC().IsOk.ShouldBeTrue();
                            list.Add(c);
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Ported from <c>ColorEncodingTest.RoundTripAll</c>.
        /// </summary>
        [Test]
        public void RoundTripAll()
        {
            foreach (CmsColorEncoding cOriginal in AllEncodings())
            {
                CmsColorEncoding cWp = new CmsColorEncoding();
                cWp.SetWhitePoint(cOriginal.GetWhitePoint()).IsOk.ShouldBeTrue();
                cWp.WhitePoint.ShouldBe(cOriginal.WhitePoint);

                CmsColorEncoding cPr = new CmsColorEncoding();
                cOriginal.GetPrimaries(out PrimariesCIExy p).IsOk.ShouldBeTrue();
                cPr.SetPrimaries(p).IsOk.ShouldBeTrue();
                cPr.Primaries.ShouldBe(cOriginal.Primaries);

                if (cOriginal.Tf.HaveGamma)
                {
                    CmsColorEncoding cTf = new CmsColorEncoding();
                    cTf.Tf.SetGamma(cOriginal.Tf.GetGamma()).IsOk.ShouldBeTrue();
                    cOriginal.Tf.IsSame(cTf.Tf).ShouldBeTrue();
                }
            }
        }

        /// <summary>
        /// Ported from <c>ColorEncodingTest.CustomWhitePoint</c>.
        /// </summary>
        [Test]
        public void CustomWhitePoint()
        {
            CmsColorEncoding c = new CmsColorEncoding();
            CIExy xyIn         = new CIExy(0.8, 0.01);
            c.SetWhitePoint(xyIn).IsOk.ShouldBeTrue();
            CIExy xy = c.GetWhitePoint();

            CmsColorEncoding c2 = new CmsColorEncoding();
            c2.SetWhitePoint(xy).IsOk.ShouldBeTrue();
            c.SameColorSpace(c2).ShouldBeTrue();
        }

        /// <summary>
        /// Ported from <c>ColorEncodingTest.CustomPrimaries</c>.
        /// </summary>
        [Test]
        public void CustomPrimaries()
        {
            CmsColorEncoding c   = new CmsColorEncoding();
            PrimariesCIExy xyIn  = new PrimariesCIExy(
                new CIExy(-0.01, 0.2),
                new CIExy(0.4, 0.401),
                new CIExy(1.1, -1.2));

            c.SetPrimaries(xyIn).IsOk.ShouldBeTrue();
            c.GetPrimaries(out PrimariesCIExy xy).IsOk.ShouldBeTrue();

            CmsColorEncoding c2 = new CmsColorEncoding();
            c2.SetPrimaries(xy).IsOk.ShouldBeTrue();
            c.SameColorSpace(c2).ShouldBeTrue();
        }

        /// <summary>
        /// Ported from <c>ColorEncodingTest.CustomGamma</c>.
        /// </summary>
        [Test]
        public void CustomGamma()
        {
            CmsColorEncoding c = new CmsColorEncoding();
            c.Tf.SetGamma(0.0).IsOk.ShouldBeFalse();
            c.Tf.SetGamma(-1e-6).IsOk.ShouldBeFalse();
            c.Tf.SetGamma(1.001).IsOk.ShouldBeFalse();

            c.Tf.SetGamma(1.0).IsOk.ShouldBeTrue();
            c.Tf.HaveGamma.ShouldBeFalse();
            c.Tf.IsLinear().ShouldBeTrue();

            c.Tf.SetGamma(0.123).IsOk.ShouldBeTrue();
            c.Tf.HaveGamma.ShouldBeTrue();
            double gamma = c.Tf.GetGamma();

            CmsColorEncoding c2 = new CmsColorEncoding();
            c2.Tf.SetGamma(gamma).IsOk.ShouldBeTrue();
            c.SameColorEncoding(c2).ShouldBeTrue();
            c2.Tf.HaveGamma.ShouldBeTrue();
        }

        /// <summary>
        /// Ported from <c>ColorEncodingTest.InternalExternalConversion</c>.
        /// </summary>
        [Test]
        public void InternalExternalConversion()
        {
            Random rng = new Random(12345);

            for (int i = 0; i < 100; ++i)
            {
                CmsColorEncoding src = new CmsColorEncoding();
                CmsColorEncoding dst = new CmsColorEncoding();

                src.ColorSpace = (ColorSpace)rng.Next(0, 4);
                CIExy wp       = new CIExy((rng.NextDouble() * 0.5) + 0.25, (rng.NextDouble() * 0.5) + 0.25);
                src.SetWhitePoint(wp).IsOk.ShouldBeTrue();

                if (src.HasPrimaries())
                {
                    PrimariesCIExy primaries = new PrimariesCIExy(
                        new CIExy((rng.NextDouble() * 0.5) + 0.25, (rng.NextDouble() * 0.5) + 0.25),
                        new CIExy((rng.NextDouble() * 0.5) + 0.25, (rng.NextDouble() * 0.5) + 0.25),
                        new CIExy((rng.NextDouble() * 0.5) + 0.25, (rng.NextDouble() * 0.5) + 0.25));
                    src.SetPrimaries(primaries).IsOk.ShouldBeTrue();
                }

                CustomTransferFunction tf = CustomTransferFunction.CreateDefault();
                tf.SetGamma((rng.NextDouble() * 0.5) + 0.25).IsOk.ShouldBeTrue();
                src.Tf              = tf;
                src.RenderingIntent = (RenderingIntent)rng.Next(0, 4);

                JxlColorEncoding external = src.ToExternal();
                dst.FromExternal(external).IsOk.ShouldBeTrue();

                dst.ColorSpace.ShouldBe(src.ColorSpace);
                dst.WhitePoint.ShouldBe(src.WhitePoint);
                CIExy srcWp = src.GetWhitePoint();
                CIExy dstWp = dst.GetWhitePoint();
                dstWp.X.ShouldBe(srcWp.X);
                dstWp.Y.ShouldBe(srcWp.Y);

                if (src.HasPrimaries())
                {
                    src.GetPrimaries(out PrimariesCIExy srcP).IsOk.ShouldBeTrue();
                    dst.GetPrimaries(out PrimariesCIExy dstP).IsOk.ShouldBeTrue();
                    dstP.ShouldBe(srcP);
                }

                dst.Tf.HaveGamma.ShouldBe(src.Tf.HaveGamma);

                if (src.Tf.HaveGamma)
                {
                    dst.Tf.GetGamma().ShouldBe(src.Tf.GetGamma());
                }
                else
                {
                    dst.Tf.GetTransferFunction().ShouldBe(src.Tf.GetTransferFunction());
                }

                dst.RenderingIntent.ShouldBe(src.RenderingIntent);
            }
        }
    }
}
