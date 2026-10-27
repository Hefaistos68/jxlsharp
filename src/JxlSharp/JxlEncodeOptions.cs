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
using JxlSharp.Encoder;
using JxlSharp.Extras;

namespace JxlSharp
{
    /// <summary>
    /// Comprehensive configuration options exposing all available JPEG XL (<c>libjxl</c>) encoder parameters,
    /// frame settings (<see cref="JxlEncoderFrameSettingId"/>), container options, and lossless JPEG transcoding flags.
    /// </summary>
    public sealed class JxlEncodeOptions
    {
        private readonly List<JXLOption> _customOptions = new List<JXLOption>();

        /// <summary>
        /// Gets or sets the target quality in the range <c>0..100</c> (<c>100</c> = mathematically lossless,
        /// <c>90</c> = visually lossless Butteraugli distance <c>1.0</c>). When set and <see cref="Distance"/> is
        /// not explicitly overridden, <see cref="JxlEncoder.DistanceFromQuality(float)"/> is used.
        /// </summary>
        public float? Quality { get; set; }

        /// <summary>
        /// Gets or sets the target Butteraugli psychovisual distance (<c>0.0..25.0</c>; <c>0.0</c> = lossless,
        /// <c>1.0</c> = visually lossless default).
        /// </summary>
        public float? Distance { get; set; }

        /// <summary>
        /// Gets or sets the target Butteraugli distance for the alpha channel (<c>0.0</c> = lossless; <c>null</c> = automatic).
        /// </summary>
        public float? AlphaDistance { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether mathematically lossless encoding (<c>Distance = 0.0</c>) is enabled.
        /// </summary>
        public bool Lossless { get; set; }

        /// <summary>
        /// Gets or sets the encoder effort/speed tier (<c>1..11</c>, default <c>7</c> Squirrel;
        /// values <c>10..11</c> automatically enable <see cref="AllowExpertOptions"/>).
        /// </summary>
        public int Effort { get; set; } = 7;

        /// <summary>
        /// Gets or sets the decoding speed tier (<c>0..4</c>, default <c>0</c> highest quality).
        /// </summary>
        public int DecodingSpeed { get; set; }

        /// <summary>
        /// Gets or sets the Brotli compression effort for metadata boxes (<c>-1..11</c>, default <c>-1</c> automatic).
        /// </summary>
        public int BrotliEffort { get; set; } = -1;

        /// <summary>
        /// Gets or sets the number of worker threads (<c>-1</c> or <c>0</c> = processor default, <c>1</c> = single-threaded).
        /// </summary>
        public int NumThreads { get; set; } = -1;

        /// <summary>
        /// Gets or sets a value indicating whether expert encoder options (such as effort 10/11) are allowed.
        /// </summary>
        public bool AllowExpertOptions { get; set; }

        /// <summary>
        /// Gets or sets the internal encoder buffering mode (<c>-1..3</c>, default <c>-1</c> automatic).
        /// </summary>
        public int Buffering { get; set; } = -1;

        /// <summary>
        /// Gets or sets the Modular encoding mode override (<see cref="Override.On"/> = Modular, <see cref="Override.Off"/> = VarDCT).
        /// </summary>
        public Override Modular { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets whether color values of completely transparent pixels are preserved.
        /// </summary>
        public Override KeepInvisible { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the dots synthesis override.
        /// </summary>
        public Override Dots { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the patch dictionary synthesis override.
        /// </summary>
        public Override Patches { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the Gaborish inverse-sharpening filter override.
        /// </summary>
        public Override Gaborish { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the adaptive noise synthesis override.
        /// </summary>
        public Override Noise { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the edge-preserving filter level (<c>-1..3</c>, default <c>-1</c> automatic).
        /// </summary>
        public int Epf { get; set; } = -1;

        /// <summary>
        /// Gets or sets the simulated film grain photon noise ISO value (<c>0</c> = disabled).
        /// </summary>
        public float PhotonNoiseIso { get; set; }

        /// <summary>
        /// Gets or sets the full-image perceptual heuristics override.
        /// </summary>
        public Override UseFullImageHeuristics { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the override disabling perceptual optimizations.
        /// </summary>
        public Override DisablePerceptualOptimizations { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets a value indicating whether the standard progressive preset
        /// (<c>QProgressiveAc = On</c>, <c>Responsive = On</c>, <c>ProgressiveDc = 1</c>) is enabled.
        /// </summary>
        public bool Progressive { get; set; }

        /// <summary>
        /// Gets or sets the Modular responsive (squeeze) progressive mode override.
        /// </summary>
        public Override Responsive { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the spectral AC progressive mode override.
        /// </summary>
        public Override ProgressiveAc { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the quantized AC progressive mode override.
        /// </summary>
        public Override QProgressiveAc { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the progressive DC frame mode (<c>-1..2</c>, default <c>-1</c> automatic).
        /// </summary>
        public int ProgressiveDc { get; set; } = -1;

        /// <summary>
        /// Gets or sets the group ordering override (<see cref="Override.Off"/> = scanline, <see cref="Override.On"/> = center-first).
        /// </summary>
        public Override GroupOrder { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the horizontal center coordinate for center-first group ordering (<c>-1</c> = image center).
        /// </summary>
        public long CenterX { get; set; } = -1;

        /// <summary>
        /// Gets or sets the vertical center coordinate for center-first group ordering (<c>-1</c> = image center).
        /// </summary>
        public long CenterY { get; set; } = -1;

        /// <summary>
        /// Gets or sets the color channel resampling factor (<c>-1, 1, 2, 4, 8</c>, default <c>-1</c>).
        /// </summary>
        public int Resampling { get; set; } = -1;

        /// <summary>
        /// Gets or sets the extra channel resampling factor (<c>-1, 1, 2, 4, 8</c>, default <c>-1</c>).
        /// </summary>
        public int ExtraChannelResampling { get; set; } = -1;

        /// <summary>
        /// Gets or sets a value indicating whether the input image is already downsampled by <see cref="Resampling"/>.
        /// </summary>
        public bool AlreadyDownsampled { get; set; }

        /// <summary>
        /// Gets or sets the upsampling mode (<c>-1</c> = default, <c>0</c> = default separable, <c>1</c> = nearest neighbor).
        /// </summary>
        public int UpsamplingMode { get; set; } = -1;

        /// <summary>
        /// Gets or sets the internal color transform (<c>-1</c> = automatic, <c>0</c> = XYB, <c>1</c> = None, <c>2</c> = YCbCr).
        /// </summary>
        public int ColorTransform { get; set; } = -1;

        /// <summary>
        /// Gets or sets the reversible color transform for Modular mode (<c>-1..41</c>, default <c>-1</c> automatic).
        /// </summary>
        public int ModularColorSpace { get; set; } = -1;

        /// <summary>
        /// Gets or sets the Modular group size (<c>-1</c> = automatic, <c>0</c> = 128, <c>1</c> = 256, <c>2</c> = 512, <c>3</c> = 1024).
        /// </summary>
        public int ModularGroupSize { get; set; } = -1;

        /// <summary>
        /// Gets or sets the Modular predictor (<c>-1..15</c>, default <c>-1</c> automatic).
        /// </summary>
        public int ModularPredictor { get; set; } = -1;

        /// <summary>
        /// Gets or sets the fraction of pixels used to learn Modular MA trees (<c>0..100</c>, or <c>null</c> for automatic).
        /// </summary>
        public float? ModularMaTreeLearningPercent { get; set; }

        /// <summary>
        /// Gets or sets the number of extra previous-channel MA tree properties (<c>-1..11</c>, default <c>-1</c> automatic).
        /// </summary>
        public int ModularNbPrevChannels { get; set; } = -1;

        /// <summary>
        /// Gets or sets the maximum number of colors for palette transforms (<c>null</c> = default).
        /// </summary>
        public int? PaletteColors { get; set; }

        /// <summary>
        /// Gets or sets the lossy delta palette override.
        /// </summary>
        public Override LossyPalette { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the global channel palette threshold percentage (<c>null</c> = default).
        /// </summary>
        public float? ChannelColorsGlobalPercent { get; set; }

        /// <summary>
        /// Gets or sets the local group channel palette threshold percentage (<c>null</c> = default).
        /// </summary>
        public float? ChannelColorsGroupPercent { get; set; }

        /// <summary>
        /// Gets or sets the ISO BMFF container wrapping override.
        /// </summary>
        public Override Container { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets a value indicating whether metadata boxes are compressed using Brotli (<c>brob</c>).
        /// </summary>
        public bool CompressBoxes { get; set; } = true;

        /// <summary>
        /// Gets or sets the frame index box (<c>jxli</c>) generation override.
        /// </summary>
        public Override FrameIndexBox { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets whether JPEG inputs should be losslessly transcoded when distance is 0 or unspecified.
        /// </summary>
        public Override LosslessJpegTranscode { get; set; } = Override.On;

        /// <summary>
        /// Gets or sets a value indicating whether byte-exact JPEG reconstruction metadata (<c>jbrd</c>) is stored.
        /// </summary>
        public bool JpegStoreMetadata { get; set; } = true;

        /// <summary>
        /// Gets or sets the chroma-from-luma (CfL) override during lossless JPEG transcoding.
        /// </summary>
        public Override JpegReconCfl { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the Brotli box compression override for JPEG-derived metadata boxes.
        /// </summary>
        public Override JpegCompressBoxes { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets whether EXIF metadata from JPEG frames is preserved.
        /// </summary>
        public Override JpegKeepExif { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets whether XMP metadata from JPEG frames is preserved.
        /// </summary>
        public Override JpegKeepXmp { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets whether JUMBF metadata from JPEG frames is preserved.
        /// </summary>
        public Override JpegKeepJumbf { get; set; } = Override.Default;

        /// <summary>
        /// Gets or sets the display intensity target in nits (<c>0</c> = library default).
        /// </summary>
        public float IntensityTarget { get; set; }

        /// <summary>
        /// Gets or sets the bit depth override (<c>0</c> = use input bit depth).
        /// </summary>
        public nuint OverrideBitdepth { get; set; }

        /// <summary>
        /// Gets or sets the codestream level (<c>-1</c> = automatic, <c>5</c> or <c>10</c>).
        /// </summary>
        public int CodestreamLevel { get; set; } = -1;

        /// <summary>
        /// Gets or sets the alpha premultiplication override (<c>-1</c> = automatic, <c>0</c> = unpremultiplied, <c>1</c> = premultiplied).
        /// </summary>
        public int Premultiply { get; set; } = -1;

        /// <summary>
        /// Gets or sets the alpha stripping mode (<c>-1</c> = default, <c>0</c> = keep, <c>1</c> = strip).
        /// </summary>
        public int StripAlpha { get; set; } = -1;

        /// <summary>
        /// Gets the color space hints applied when decoding external input formats prior to encoding.
        /// </summary>
        public ColorHints ColorHints { get; } = new ColorHints();

        /// <summary>
        /// Gets or sets the optional custom memory manager.
        /// </summary>
        public JxlMemoryManager? MemoryManager { get; set; }

        /// <summary>
        /// Gets or sets the optional encoder statistics collector.
        /// </summary>
        public JxlSharp.Encoder.JxlEncoderStats? Stats { get; set; }

        /// <summary>
        /// Gets the list of additional per-frame options registered via <see cref="AddCustomOption(JxlEncoderFrameSettingId, long, nuint)"/>
        /// or <see cref="AddCustomFloatOption(JxlEncoderFrameSettingId, float, nuint)"/>.
        /// </summary>
        public IReadOnlyList<JXLOption> CustomOptions => _customOptions;

        /// <summary>
        /// Creates a default <see cref="JxlEncodeOptions"/> preset for visually lossless encoding (<c>Distance = 1.0</c>, <c>Effort = 7</c>).
        /// </summary>
        /// <returns>A new <see cref="JxlEncodeOptions"/> instance.</returns>
        public static JxlEncodeOptions CreateDefault()
        {
            return new JxlEncodeOptions();
        }

        /// <summary>
        /// Creates a mathematically lossless <see cref="JxlEncodeOptions"/> preset (<c>Lossless = true</c>, <c>Distance = 0.0</c>, <c>Modular = On</c>).
        /// </summary>
        /// <param name="effort">Encoder effort (<c>1..11</c>, default <c>7</c>).</param>
        /// <returns>A lossless <see cref="JxlEncodeOptions"/> instance.</returns>
        public static JxlEncodeOptions CreateLossless(int effort = 7)
        {
            return new JxlEncodeOptions
            {
                Lossless      = true,
                Distance      = 0.0f,
                AlphaDistance = 0.0f,
                Modular       = Override.On,
                Effort        = effort,
            };
        }

        /// <summary>
        /// Registers an explicit integer frame setting option for a specific frame index.
        /// </summary>
        /// <param name="id">Frame setting ID.</param>
        /// <param name="value">Integer option value.</param>
        /// <param name="frameIndex">Zero-based frame index.</param>
        public void AddCustomOption(JxlEncoderFrameSettingId id, long value, nuint frameIndex = 0)
        {
            _customOptions.Add(new JXLOption(id, value, frameIndex));
        }

        /// <summary>
        /// Registers an explicit floating-point frame setting option for a specific frame index.
        /// </summary>
        /// <param name="id">Frame setting ID.</param>
        /// <param name="value">Floating-point option value.</param>
        /// <param name="frameIndex">Zero-based frame index.</param>
        public void AddCustomFloatOption(JxlEncoderFrameSettingId id, float value, nuint frameIndex = 0)
        {
            _customOptions.Add(new JXLOption(id, value, frameIndex));
        }

        /// <summary>
        /// Resolves the effective Butteraugli distance based on <see cref="Lossless"/>, <see cref="Distance"/>,
        /// <see cref="Quality"/>, and whether the source is a JPEG being losslessly transcoded.
        /// </summary>
        /// <param name="isJpegInput">Whether the input is a JPEG image.</param>
        /// <returns>Effective Butteraugli distance (<c>0.0..25.0</c>).</returns>
        public float ResolveEffectiveDistance(bool isJpegInput = false)
        {
            if (Lossless)
            {
                return 0.0f;
            }

            if (Distance.HasValue)
            {
                return Math.Clamp(Distance.Value, 0.0f, 25.0f);
            }

            if (Quality.HasValue)
            {
                return JxlEncoder.DistanceFromQuality(Quality.Value);
            }

            bool defaultLosslessJpeg = isJpegInput && LosslessJpegTranscode == Override.On;

            return defaultLosslessJpeg ? 0.0f : 1.0f;
        }

        /// <summary>
        /// Determines whether lossless JPEG bitstream transcoding should be used for a JPEG input.
        /// </summary>
        /// <param name="hasJpegBytes">Whether raw JPEG bytes are available.</param>
        /// <returns><c>true</c> if lossless JPEG transcoding should be performed; otherwise <c>false</c>.</returns>
        public bool ShouldUseLosslessJpegTranscode(bool hasJpegBytes)
        {
            if (!hasJpegBytes || LosslessJpegTranscode != Override.On)
            {
                return false;
            }

            if (Distance.HasValue && Distance.Value > 0.0f)
            {
                return false;
            }

            if (Quality.HasValue && Quality.Value < 100.0f && !Lossless)
            {
                return false;
            }

            return Modular != Override.On && Resampling <= 1;
        }

        /// <summary>
        /// Builds a <see cref="JXLCompressParams"/> instance populated with all configured encoder options.
        /// </summary>
        /// <param name="ppf">Optional target <see cref="PackedPixelFile"/> for dimension-dependent options.</param>
        /// <param name="isJpegInput">Whether the source is a JPEG image.</param>
        /// <returns>A fully configured <see cref="JXLCompressParams"/> instance.</returns>
        public JXLCompressParams ToCompressParams(PackedPixelFile? ppf = null, bool isJpegInput = false)
        {
            float effectiveDistance = ResolveEffectiveDistance(isJpegInput);
            int clampedEffort       = Math.Clamp(Effort, 1, 11);
            long resampling         = NormalizeResampling(Resampling);

            JXLCompressParams jparams = new JXLCompressParams
            {
                Distance           = effectiveDistance,
                AlphaDistance      = AlphaDistance ?? (effectiveDistance == 0.0f ? 0.0f : -1.0f),
                UseContainer       = Container == Override.On,
                JpegStoreMetadata  = JpegStoreMetadata,
                JpegStripExif      = JpegKeepExif == Override.Off,
                JpegStripXmp       = JpegKeepXmp == Override.Off,
                JpegStripJumbf     = JpegKeepJumbf == Override.Off,
                CompressBoxes      = CompressBoxes,
                IntensityTarget    = IntensityTarget,
                UpsamplingMode     = Math.Clamp(UpsamplingMode, -1, 1),
                OverrideBitdepth   = OverrideBitdepth,
                CodestreamLevel    = NormalizeCodestreamLevel(CodestreamLevel),
                Premultiply        = Premultiply,
                StripAlpha         = StripAlpha,
                AllowExpertOptions = AllowExpertOptions || clampedEffort >= 10,
                AlreadyDownsampled = (AlreadyDownsampled && resampling > 1) ? (int)resampling : 1,
                MemoryManager      = MemoryManager,
                Stats              = Stats,
            };

            PopulateCoreFrameOptions(jparams, clampedEffort, effectiveDistance, resampling);
            PopulatePerceptualFrameOptions(jparams);
            PopulateProgressiveAndOrderOptions(jparams, ppf);
            PopulateModularFrameOptions(jparams);
            PopulateJpegAndContainerFrameOptions(jparams);
            PopulateCustomFrameOptions(jparams);

            return jparams;
        }

        private void PopulateCoreFrameOptions(
            JXLCompressParams jparams,
            int clampedEffort,
            float effectiveDistance,
            long resampling)
        {
            long ecResampling = NormalizeResampling(ExtraChannelResampling);

            jparams.AddOption(JxlEncoderFrameSettingId.Effort, clampedEffort);
            jparams.AddOption(JxlEncoderFrameSettingId.DecodingSpeed, Math.Clamp(DecodingSpeed, 0, 4));
            jparams.AddOption(JxlEncoderFrameSettingId.BrotliEffort, Math.Clamp(BrotliEffort, -1, 11));
            jparams.AddOption(JxlEncoderFrameSettingId.Buffering, Math.Clamp(Buffering, -1, 3));
            jparams.AddOption(JxlEncoderFrameSettingId.Resampling, resampling);
            jparams.AddOption(JxlEncoderFrameSettingId.ExtraChannelResampling, ecResampling);

            if (AlreadyDownsampled && resampling > 1)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.AlreadyDownsampled, 1);
            }

            if (Modular == Override.On || effectiveDistance == 0.0f)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
            }
            else if (Modular == Override.Off)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 0);
            }
        }

        private void PopulatePerceptualFrameOptions(JXLCompressParams jparams)
        {
            jparams.AddOption(JxlEncoderFrameSettingId.Epf, Math.Clamp(Epf, -1, 3));

            if (PhotonNoiseIso > 0.0f)
            {
                jparams.AddFloatOption(JxlEncoderFrameSettingId.PhotonNoise, PhotonNoiseIso);
            }

            AddOverrideOption(jparams, JxlEncoderFrameSettingId.KeepInvisible, KeepInvisible);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Dots, Dots);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Patches, Patches);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Gaborish, Gaborish);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Noise, Noise);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.UseFullImageHeuristics, UseFullImageHeuristics);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.DisablePerceptualOptimizations, DisablePerceptualOptimizations);
        }

        private void PopulateProgressiveAndOrderOptions(JXLCompressParams jparams, PackedPixelFile? ppf)
        {
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.GroupOrder, GroupOrder);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.ProgressiveAc, ProgressiveAc);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.QProgressiveAc, QProgressiveAc);

            if (Progressive)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.QProgressiveAc, 1);
                jparams.AddOption(JxlEncoderFrameSettingId.Responsive, 1);
                jparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, ProgressiveDc >= 0 ? ProgressiveDc : 1);
            }
            else
            {
                AddOverrideOption(jparams, JxlEncoderFrameSettingId.Responsive, Responsive);

                if (ProgressiveDc >= -1)
                {
                    jparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, Math.Clamp(ProgressiveDc, -1, 2));
                }
            }

            if (CenterX >= -1 && (ppf is null || ppf.XSize == 0 || (nuint)CenterX < ppf.XSize))
            {
                jparams.AddOption(JxlEncoderFrameSettingId.GroupOrderCenterX, CenterX);
            }

            if (CenterY >= -1 && (ppf is null || ppf.YSize == 0 || (nuint)CenterY < ppf.YSize))
            {
                jparams.AddOption(JxlEncoderFrameSettingId.GroupOrderCenterY, CenterY);
            }
        }

        private void PopulateModularFrameOptions(JXLCompressParams jparams)
        {
            if (ColorTransform >= -1 && ColorTransform <= 2)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ColorTransform, ColorTransform);
            }

            if (ModularColorSpace >= -1 && ModularColorSpace <= 41)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ModularColorSpace, ModularColorSpace);
            }

            if (ModularGroupSize >= -1 && ModularGroupSize <= 3)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ModularGroupSize, ModularGroupSize);
            }

            if (ModularPredictor >= -1 && ModularPredictor <= 15)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ModularPredictor, ModularPredictor);
            }

            if (ModularNbPrevChannels >= -1 && ModularNbPrevChannels <= 11)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ModularNbPrevChannels, ModularNbPrevChannels);
            }

            PopulateModularPaletteAndTreeOptions(jparams);
        }

        private void PopulateModularPaletteAndTreeOptions(JXLCompressParams jparams)
        {
            if (ModularMaTreeLearningPercent.HasValue)
            {
                jparams.AddFloatOption(
                    JxlEncoderFrameSettingId.ModularMaTreeLearningPercent,
                    Math.Clamp(ModularMaTreeLearningPercent.Value, 0.0f, 100.0f));
            }

            if (PaletteColors.HasValue)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.PaletteColors, PaletteColors.Value);
            }

            AddOverrideOption(jparams, JxlEncoderFrameSettingId.LossyPalette, LossyPalette);

            if (ChannelColorsGlobalPercent.HasValue)
            {
                jparams.AddFloatOption(JxlEncoderFrameSettingId.ChannelColorsGlobalPercent, ChannelColorsGlobalPercent.Value);
            }

            if (ChannelColorsGroupPercent.HasValue)
            {
                jparams.AddFloatOption(JxlEncoderFrameSettingId.ChannelColorsGroupPercent, ChannelColorsGroupPercent.Value);
            }
        }

        private void PopulateJpegAndContainerFrameOptions(JXLCompressParams jparams)
        {
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegReconCfl, JpegReconCfl);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.FrameIndexBox, FrameIndexBox);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegCompressBoxes, JpegCompressBoxes);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegKeepExif, JpegKeepExif);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegKeepXmp, JpegKeepXmp);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegKeepJumbf, JpegKeepJumbf);
        }

        private void PopulateCustomFrameOptions(JXLCompressParams jparams)
        {
            foreach (JXLOption opt in _customOptions)
            {
                jparams.Options.Add(opt);
            }
        }

        private static void AddOverrideOption(JXLCompressParams jparams, JxlEncoderFrameSettingId id, Override value)
        {
            if (value == Override.On)
            {
                jparams.AddOption(id, 1);
            }
            else if (value == Override.Off)
            {
                jparams.AddOption(id, 0);
            }
        }

        private static long NormalizeResampling(int value)
        {
            if (value <= 0)
            {
                return -1;
            }

            if (value == 1)
            {
                return 1;
            }

            if (value <= 3)
            {
                return 2;
            }

            return value <= 6 ? 4 : 8;
        }

        private static int NormalizeCodestreamLevel(int level)
        {
            if (level < 0)
            {
                return -1;
            }

            return level <= 5 ? 5 : 10;
        }
    }
}
