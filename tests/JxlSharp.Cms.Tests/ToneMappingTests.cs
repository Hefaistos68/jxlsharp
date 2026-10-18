// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Runtime.Intrinsics;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Cms.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Rec2408ToneMapper"/>, <see cref="HlgOotf"/>, and <see cref="ToneMapping"/>.
    /// </summary>
    [TestFixture]
    public class ToneMappingTests
    {
        /// <summary>
        /// Verifies that <see cref="Rec2408ToneMapper"/> produces matching results for scalar and SIMD inputs.
        /// </summary>
        [Test]
        public void Rec2408ToneMapperScalarMatchesSimd()
        {
            Vector3 luminances       = new Vector3(0.2627f, 0.6780f, 0.0593f);
            Rec2408ToneMapper mapper = new Rec2408ToneMapper(0.0f, 10000.0f, 0.0f, 250.0f, luminances);

            Vector3 c0 = new Vector3(0.0f, 0.0f, 0.0f);
            Vector3 c1 = new Vector3(0.01f, 0.02f, 0.03f);
            Vector3 c2 = new Vector3(0.1f, 0.2f, 0.3f);
            Vector3 c3 = new Vector3(0.8f, 0.9f, 1.0f);

            Vector128<float> r = Vector128.Create(c0[0], c1[0], c2[0], c3[0]);
            Vector128<float> g = Vector128.Create(c0[1], c1[1], c2[1], c3[1]);
            Vector128<float> b = Vector128.Create(c0[2], c1[2], c2[2], c3[2]);

            mapper.ToneMap(ref c0);
            mapper.ToneMap(ref c1);
            mapper.ToneMap(ref c2);
            mapper.ToneMap(ref c3);
            mapper.ToneMap(ref r, ref g, ref b);

            r.GetElement(1).ShouldBe(c1[0], 1e-3f);
            g.GetElement(1).ShouldBe(c1[1], 1e-3f);
            b.GetElement(1).ShouldBe(c1[2], 1e-3f);

            r.GetElement(3).ShouldBe(c3[0], 1e-3f);
            g.GetElement(3).ShouldBe(c3[1], 1e-3f);
            b.GetElement(3).ShouldBe(c3[2], 1e-3f);
        }

        /// <summary>
        /// Verifies that <see cref="HlgOotf"/> works in scalar and SIMD modes and round-trips between scene and display light.
        /// </summary>
        [Test]
        public void HlgOotfScalarMatchesSimdAndRoundTrips()
        {
            Vector3 luminances = new Vector3(0.2627f, 0.6780f, 0.0593f);
            HlgOotf fwd        = HlgOotf.FromSceneLight(1000.0f, luminances);
            HlgOotf inv        = HlgOotf.ToSceneLight(1000.0f, luminances);

            fwd.WarrantsGamutMapping.ShouldBeFalse();
            inv.WarrantsGamutMapping.ShouldBeTrue();

            Vector3 rgb = new Vector3(0.5f, 0.5f, 0.5f);
            Vector128<float> r = Vector128.Create(0.5f);
            Vector128<float> g = Vector128.Create(0.5f);
            Vector128<float> b = Vector128.Create(0.5f);

            fwd.Apply(ref rgb);
            fwd.Apply(ref r, ref g, ref b);
            r.GetElement(0).ShouldBe(rgb[0], 1e-4f);

            inv.Apply(ref rgb);
            inv.Apply(ref r, ref g, ref b);
            rgb[0].ShouldBe(0.5f, 1e-4f);
            r.GetElement(0).ShouldBe(0.5f, 1e-4f);
        }

        /// <summary>
        /// Verifies that <see cref="ToneMapping.GamutMapScalar"/> and <see cref="ToneMapping.GamutMap"/> bring out-of-gamut colors into <c>[0, 1]</c>.
        /// </summary>
        [Test]
        public void GamutMapClampsToUnitCubeAndMatchesSimd()
        {
            Vector3 luminances = new Vector3(0.2627f, 0.6780f, 0.0593f);
            Vector3 rgb        = new Vector3(-0.2f, 1.4f, 0.5f);

            Vector128<float> r = Vector128.Create(rgb[0]);
            Vector128<float> g = Vector128.Create(rgb[1]);
            Vector128<float> b = Vector128.Create(rgb[2]);

            ToneMapping.GamutMapScalar(ref rgb, luminances, 0.3f);
            ToneMapping.GamutMap(ref r, ref g, ref b, luminances, 0.3f);

            for (int i = 0; i < 3; ++i)
            {
                rgb[i].ShouldBeGreaterThanOrEqualTo(0.0f);
                rgb[i].ShouldBeLessThanOrEqualTo(1.0f);
            }

            r.GetElement(0).ShouldBe(rgb[0], 1e-5f);
            g.GetElement(0).ShouldBe(rgb[1], 1e-5f);
            b.GetElement(0).ShouldBe(rgb[2], 1e-5f);
        }
    }
}
