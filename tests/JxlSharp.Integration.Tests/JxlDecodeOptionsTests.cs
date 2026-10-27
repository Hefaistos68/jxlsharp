// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using JxlSharp.Base;
using JxlSharp.Extras;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for <see cref="JxlDecodeOptions"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlDecodeOptionsTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlDecodeOptions.CreateDefault"/> populates default accepted formats
        /// and decompression flags.
        /// </summary>
        [Test]
        public void CreateDefault_ToDecompressParams_PopulatesAcceptedFormats()
        {
            JxlDecodeOptions sut        = JxlDecodeOptions.CreateDefault();
            JXLDecompressParams dparams = sut.ToDecompressParams();

            dparams.Coalescing.ShouldBeTrue();
            dparams.RenderSpotcolors.ShouldBeTrue();
            dparams.MaxDownsampling.ShouldBe((nuint)1);
            dparams.AcceptedFormats.Count.ShouldBe(12);
        }

        /// <summary>
        /// Verifies that explicit channel, data type, bit depth, and downsampling settings on
        /// <see cref="JxlDecodeOptions"/> are mapped to <see cref="JXLDecompressParams"/>.
        /// </summary>
        [Test]
        public void ToDecompressParams_WithCustomBitDepthAndChannels_ConfiguresParamsCorrectly()
        {
            JxlDecodeOptions sut = new JxlDecodeOptions
            {
                RequestedChannels  = 4,
                RequestedDataType  = JxlDataType.UInt16,
                BitsPerSample      = 16,
                ColorSpace         = "RGB_D65_SRG_Rel_SRG",
                DisplayNits        = 250.0f,
                KeepOrientation    = true,
                UnpremultiplyAlpha = true,
                MaxDownsampling    = 4,
            };

            JXLDecompressParams dparams = sut.ToDecompressParams();

            dparams.AcceptedFormats.Count.ShouldBe(1);
            dparams.AcceptedFormats[0].NumChannels.ShouldBe(4u);
            dparams.AcceptedFormats[0].DataType.ShouldBe(JxlDataType.UInt16);
            dparams.OutputBitdepth.Type.ShouldBe(JxlBitDepthType.Custom);
            dparams.OutputBitdepth.BitsPerSample.ShouldBe(16u);
            dparams.DisplayNits.ShouldBe(250.0f);
            dparams.KeepOrientation.ShouldBeTrue();
            dparams.UnpremultiplyAlpha.ShouldBeTrue();
            dparams.MaxDownsampling.ShouldBe((nuint)4);
        }
    }
}
