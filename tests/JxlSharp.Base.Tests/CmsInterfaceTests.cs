// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlCmsInterface"/> and <see cref="JxlColorProfile"/>.
    /// </summary>
    [TestFixture]
    public class CmsInterfaceTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlCmsInterface"/> callbacks and <see cref="JxlColorProfile"/> properties function properly.
        /// </summary>
        [Test]
        public void CmsInterfaceCallbacksExecuteCorrectly()
        {
            bool destroyed = false;
            float[] srcBuf = new float[6];
            float[] dstBuf = new float[6];

            JxlCmsInterface cms = new JxlCmsInterface
            {
                SetFieldsData    = "fields",
                SetFieldsFromIcc = static (object? _, ReadOnlySpan<byte> icc, ref JxlColorEncoding c, out bool cmyk) =>
                {
                    c.ColorSpace = JxlColorSpace.Rgb;
                    cmyk         = icc.Length == 4;

                    return true;
                },
                InitData         = "init",
                Init             = static (initData, _, _, _, _, _) => initData,
                GetSrcBuf        = (_, _) => srcBuf,
                GetDstBuf        = (_, _) => dstBuf,
                Run              = static (_, _, input, output, numPixels) =>
                {
                    input.Slice(0, (int)numPixels * 3).CopyTo(output);

                    return true;
                },
                Destroy          = _ => destroyed = true,
            };

            JxlColorProfile inProfile = new JxlColorProfile
            {
                IccData       = [1, 2, 3, 4],
                ColorEncoding = new JxlColorEncoding { ColorSpace = JxlColorSpace.Rgb },
                NumChannels   = 3,
            };

            JxlColorEncoding parsed = default;
            cms.SetFieldsFromIcc!(cms.SetFieldsData, inProfile.IccData, ref parsed, out bool isCmyk).ShouldBeTrue();
            isCmyk.ShouldBeTrue();
            parsed.ColorSpace.ShouldBe(JxlColorSpace.Rgb);

            object? state = cms.Init!(cms.InitData, 1, 2, inProfile, inProfile, 255.0f);
            state.ShouldBe("init");

            float[]? s = cms.GetSrcBuf!(state, 0);
            float[]? d = cms.GetDstBuf!(state, 0);
            s![0]      = 0.5f;

            cms.Run!(state, 0, s, d, 2).ShouldBeTrue();
            d![0].ShouldBe(0.5f);

            cms.Destroy!(state);
            destroyed.ShouldBeTrue();
        }
    }
}
