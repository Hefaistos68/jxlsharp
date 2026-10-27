// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.Intrinsics;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Cms.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TfHlgBase"/>, <see cref="TfPqBase"/>, <see cref="TfHlg"/>,
    /// <see cref="Tf709"/>, <see cref="TfPq"/>, and <see cref="TfSrgb"/>.
    /// </summary>
    [TestFixture]
    public class TransferFunctionsTests
    {
        /// <summary>
        /// Verifies that <see cref="TfHlgBase"/> and <see cref="TfHlg"/> agree within 5e-7 and round-trip.
        /// </summary>
        [Test]
        public void TfHlgRoundTripsAndMatchesBase()
        {
            TfHlg tfHlg = new TfHlg();

            for (int i = -10; i <= 100; ++i)
            {
                double x       = i / 100.0;
                double baseEnc = TfHlgBase.EncodedFromDisplay(x);
                double baseDec = TfHlgBase.DisplayFromEncoded(baseEnc);
                baseDec.ShouldBe(x, 1e-6);

                float simdEnc = tfHlg.EncodedFromDisplay((float)x);
                simdEnc.ShouldBe((float)baseEnc, 1e-5f);

                float simdDec = tfHlg.DisplayFromEncoded(simdEnc);
                simdDec.ShouldBe((float)x, 1e-5f);
            }
        }

        /// <summary>
        /// Verifies that <see cref="TfPqBase"/> and <see cref="TfPq"/> agree and round-trip.
        /// </summary>
        [Test]
        public void TfPqRoundTripsAndMatchesBase()
        {
            TfPq tfPq = new TfPq(255.0f);

            for (int i = 0; i <= 100; ++i)
            {
                double x       = i / 100.0;
                double baseEnc = TfPqBase.EncodedFromDisplay(255.0f, x);
                double baseDec = TfPqBase.DisplayFromEncoded(255.0f, baseEnc);
                baseDec.ShouldBe(x, 1e-6);

                float simdEnc = tfPq.EncodedFromDisplay((float)x);
                simdEnc.ShouldBe((float)baseEnc, 2e-5f);

                float simdDec = tfPq.DisplayFromEncoded(simdEnc);
                simdDec.ShouldBe((float)x, 5e-5f);
            }
        }

        /// <summary>
        /// Verifies that <see cref="Tf709"/> round-trips in scalar and SIMD modes.
        /// </summary>
        [Test]
        public void Tf709RoundTrips()
        {
            Tf709 tf709 = new Tf709();
            Vector128<float> vIn  = Vector128.Create(0.005f, 0.05f, 0.25f, 0.9f);
            Vector128<float> vEnc = tf709.EncodedFromDisplay(vIn);
            Vector128<float> vDec = tf709.DisplayFromEncoded(vEnc);

            for (int i = 0; i < 4; ++i)
            {
                float x = vIn.GetElement(i);
                vEnc.GetElement(i).ShouldBe((float)Tf709.EncodedFromDisplay(x), 1e-5f);
                vDec.GetElement(i).ShouldBe(x, 1e-5f);
                tf709.DisplayFromEncoded(tf709.EncodedFromDisplay(x)).ShouldBe(x, 1e-5f);
            }
        }

        /// <summary>
        /// Verifies that <see cref="TfSrgb"/> and <see cref="TfSrgb.FastLinearToSrgb(float)"/> work accurately.
        /// </summary>
        [Test]
        public void TfSrgbRoundTripsAndMatchesFastLinearToSrgb()
        {
            TfSrgb tfSrgb = new TfSrgb();
            Vector128<float> vIn  = Vector128.Create(-0.2f, 0.001f, 0.25f, 0.95f);
            Vector128<float> vEnc = tfSrgb.EncodedFromDisplay(vIn);
            Vector128<float> vDec = tfSrgb.DisplayFromEncoded(vEnc);

            for (int i = 0; i < 4; ++i)
            {
                float x = vIn.GetElement(i);
                vDec.GetElement(i).ShouldBe(x, 1e-5f);
                tfSrgb.DisplayFromEncoded(tfSrgb.EncodedFromDisplay(x)).ShouldBe(x, 1e-5f);
            }

            Vector128<float> posIn   = Vector128.Create(0.001f, 0.05f, 0.5f, 0.9f);
            Vector128<float> fastSimd = TfSrgb.FastLinearToSrgb(posIn);
            Vector128<float> exact    = tfSrgb.EncodedFromDisplay(posIn);

            for (int i = 0; i < 4; ++i)
            {
                Math.Abs(fastSimd.GetElement(i) - exact.GetElement(i)).ShouldBeLessThan(2e-4f);
            }
        }
    }
}
