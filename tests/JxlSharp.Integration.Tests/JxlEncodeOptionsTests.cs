// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using JxlSharp.Base;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for <see cref="JxlEncodeOptions"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlEncodeOptionsTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlEncodeOptions.CreateDefault"/> and <see cref="JxlEncodeOptions.CreateLossless"/>
        /// configure expected defaults and convert to <see cref="JXLCompressParams"/>.
        /// </summary>
        [Test]
        public void CreateDefault_And_CreateLossless_ConfigureExpectedParameters()
        {
            JxlEncodeOptions defaultSut  = JxlEncodeOptions.CreateDefault();
            JxlEncodeOptions losslessSut = JxlEncodeOptions.CreateLossless(5);

            JXLCompressParams defaultParams  = defaultSut.ToCompressParams();
            JXLCompressParams losslessParams = losslessSut.ToCompressParams();

            defaultParams.Distance.ShouldBe(1.0f);
            defaultSut.Effort.ShouldBe(7);
            losslessParams.Distance.ShouldBe(0.0f);
            losslessParams.AlphaDistance.ShouldBe(0.0f);
            losslessSut.Modular.ShouldBe(Override.On);
            losslessSut.Effort.ShouldBe(5);
        }

        /// <summary>
        /// Verifies that all extended properties and custom options on <see cref="JxlEncodeOptions"/>
        /// populate <see cref="JXLCompressParams"/> accurately.
        /// </summary>
        [Test]
        public void ToCompressParams_PopulatesAllExtendedSettings()
        {
            JxlEncodeOptions sut = new JxlEncodeOptions
            {
                Quality                        = 85.0f,
                Effort                         = 10,
                DecodingSpeed                  = 2,
                BrotliEffort                   = 6,
                Buffering                      = 1,
                Modular                        = Override.Off,
                KeepInvisible                  = Override.On,
                Dots                           = Override.On,
                Patches                        = Override.Off,
                Gaborish                       = Override.On,
                Noise                          = Override.Off,
                Epf                            = 2,
                PhotonNoiseIso                 = 400.0f,
                UseFullImageHeuristics         = Override.On,
                DisablePerceptualOptimizations = Override.Off,
                Progressive                    = true,
                GroupOrder                     = Override.On,
                CenterX                        = 10,
                CenterY                        = 20,
                Resampling                     = 2,
                ExtraChannelResampling         = 2,
                AlreadyDownsampled             = true,
                UpsamplingMode                 = 0,
                ColorTransform                 = 0,
                ModularColorSpace              = 6,
                ModularGroupSize               = 1,
                ModularPredictor               = 5,
                ModularMaTreeLearningPercent   = 50.0f,
                ModularNbPrevChannels          = 2,
                PaletteColors                  = 256,
                LossyPalette                   = Override.Off,
                ChannelColorsGlobalPercent     = 80.0f,
                ChannelColorsGroupPercent      = 80.0f,
                Container                      = Override.On,
                CompressBoxes                  = true,
                FrameIndexBox                  = Override.On,
                JpegReconCfl                   = Override.On,
                JpegCompressBoxes              = Override.On,
                JpegKeepExif                   = Override.Off,
                JpegKeepXmp                    = Override.On,
                JpegKeepJumbf                  = Override.On,
                IntensityTarget                = 1000.0f,
                OverrideBitdepth               = 8,
                CodestreamLevel                = 5,
                Premultiply                    = 0,
                StripAlpha                     = 0,
            };

            sut.AddCustomOption(JxlEncoderFrameSettingId.Effort, 7, 1);
            sut.AddCustomFloatOption(JxlEncoderFrameSettingId.PhotonNoise, 200.0f, 1);

            JXLCompressParams jparams = sut.ToCompressParams();

            jparams.AllowExpertOptions.ShouldBeTrue();
            jparams.UseContainer.ShouldBeTrue();
            jparams.JpegStripExif.ShouldBeTrue();
            jparams.CodestreamLevel.ShouldBe(5);
            jparams.AlreadyDownsampled.ShouldBe(2);
            jparams.Options.Count.ShouldBeGreaterThan(20);
            sut.CustomOptions.Count.ShouldBe(2);
        }
    }
}
