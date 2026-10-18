// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.Globalization;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.Tools;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Builds command-line argument arrays and encoder/decoder parameter structures
    /// from strongly-typed CLI tool option objects (<see cref="CompressArgs"/>,
    /// <see cref="DecompressArgs"/>, and <see cref="JxlTranArgs"/>).
    /// </summary>
    public sealed class JxlCliOptionsBuilder
    {
        /// <summary>
        /// Builds the <c>cjxl</c> command-line argument array from <paramref name="cargs"/>.
        /// </summary>
        /// <param name="cargs">Compression arguments.</param>
        /// <returns>Array of command-line tokens for <see cref="CjxlTool.Run(string[], System.IO.TextWriter?, System.IO.TextWriter?)"/>.</returns>
        public string[] BuildCjxlArgs(CompressArgs cargs)
        {
            ArgumentNullException.ThrowIfNull(cargs);

            List<string> list = new List<string>();

            if (AddSpecialFlags(cargs.Version, cargs.Help, list))
            {
                return list.ToArray();
            }

            AddPositionalPair(cargs.FileIn, cargs.FileOut, list);
            AddCjxlRateAndEffortArgs(cargs, list);
            AddCjxlOverrideArgs(cargs, list);
            AddCjxlNumericArgs(cargs, list);
            AddCjxlBooleanAndHintArgs(cargs, list);

            return list.ToArray();
        }

        /// <summary>
        /// Builds the <c>djxl</c> command-line argument array from <paramref name="dargs"/>.
        /// </summary>
        /// <param name="dargs">Decompression arguments.</param>
        /// <returns>Array of command-line tokens for <see cref="DjxlTool.Run(string[], System.IO.TextWriter?, System.IO.TextWriter?)"/>.</returns>
        public string[] BuildDjxlArgs(DecompressArgs dargs)
        {
            ArgumentNullException.ThrowIfNull(dargs);

            List<string> list = new List<string>();

            if (AddSpecialFlags(dargs.Version, dargs.Help, list))
            {
                return list.ToArray();
            }

            AddPositionalPair(dargs.FileIn, dargs.FileOut, list);
            AddDjxlFlagArgs(dargs, list);
            AddDjxlNumericArgs(dargs, list);
            AddDjxlStringArgs(dargs, list);

            return list.ToArray();
        }

        /// <summary>
        /// Builds the <c>jxlinfo</c> command-line argument array.
        /// </summary>
        /// <param name="filePath">Input JXL file path.</param>
        /// <param name="verbose">Whether verbose output (<c>-v</c>) is enabled.</param>
        /// <param name="help">Whether help output (<c>-h</c>) is requested.</param>
        /// <returns>Array of command-line tokens for <see cref="JxlInfoTool.Run(string[], System.IO.TextWriter?, System.IO.TextWriter?)"/>.</returns>
        public string[] BuildJxlInfoArgs(string? filePath, bool verbose, bool help = false)
        {
            List<string> list = new List<string>();

            if (help)
            {
                list.Add("-h");

                return list.ToArray();
            }

            if (verbose)
            {
                list.Add("-v");
            }

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                list.Add(filePath);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Builds the <c>jxltran</c> command-line argument array from <paramref name="targs"/>.
        /// </summary>
        /// <param name="targs">Container transformation arguments.</param>
        /// <returns>Array of command-line tokens for <see cref="JxlTranTool.Run(string[], System.IO.TextWriter?, System.IO.TextWriter?)"/>.</returns>
        public string[] BuildJxlTranArgs(JxlTranArgs targs)
        {
            ArgumentNullException.ThrowIfNull(targs);

            List<string> list = new List<string>();

            if (AddSpecialFlags(targs.Version, targs.Help, list))
            {
                return list.ToArray();
            }

            AddJxlTranOperationFlags(targs, list);
            AddPositionalPair(targs.FileIn, targs.FileOut, list);

            return list.ToArray();
        }

        /// <summary>
        /// Constructs a <see cref="JXLCompressParams"/> instance from <paramref name="cargs"/>.
        /// </summary>
        /// <param name="cargs">UI compression arguments.</param>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="isJpegInput">Whether the source image is a JPEG file.</param>
        /// <returns>Configured <see cref="JXLCompressParams"/>.</returns>
        public JXLCompressParams CreateCompressParams(CompressArgs cargs, PackedPixelFile ppf, bool isJpegInput)
        {
            ArgumentNullException.ThrowIfNull(cargs);
            ArgumentNullException.ThrowIfNull(ppf);

            float distance = ResolveEffectiveDistance(cargs, isJpegInput);

            JXLCompressParams jparams = new JXLCompressParams
            {
                Distance           = distance,
                AlphaDistance      = cargs.GotAlphaDistance ? cargs.AlphaDistance : (distance == 0.0f ? 0.0f : -1.0f),
                UseContainer       = cargs.Container == Override.On,
                CompressBoxes      = cargs.CompressBoxes,
                IntensityTarget    = cargs.IntensityTarget,
                UpsamplingMode     = (int)cargs.UpsamplingMode,
                OverrideBitdepth   = cargs.OverrideBitdepth,
                CodestreamLevel    = cargs.CodestreamLevel,
                Premultiply        = cargs.Premultiply,
                StripAlpha         = cargs.StripAlpha,
                AllowExpertOptions = cargs.AllowExpertOptions,
            };

            if (cargs.AlreadyDownsampled && cargs.Resampling > 0)
            {
                jparams.AlreadyDownsampled = (int)cargs.Resampling;
            }

            PopulateFrameOptions(cargs, jparams, ppf, distance);

            return jparams;
        }

        /// <summary>
        /// Constructs a <see cref="JXLDecompressParams"/> instance from <paramref name="dargs"/>.
        /// </summary>
        /// <param name="dargs">UI decompression arguments.</param>
        /// <returns>Configured <see cref="JXLDecompressParams"/>.</returns>
        public JXLDecompressParams CreateDecompressParams(DecompressArgs dargs)
        {
            ArgumentNullException.ThrowIfNull(dargs);

            JXLDecompressParams dparams = new JXLDecompressParams
            {
                ColorSpace         = dargs.ColorSpace ?? string.Empty,
                DisplayNits        = (float)dargs.DisplayNits,
                RenderSpotcolors   = dargs.RenderSpotcolors,
                Coalescing         = dargs.Coalescing,
                AllowPartialInput  = dargs.AllowPartialFiles,
                MaxPasses          = dargs.MaxPasses,
                MaxDownsampling    = dargs.MaxDownsampling > 0 ? dargs.MaxDownsampling : 1,
                UseImageCallback   = dargs.UseImageCallback,
                UnpremultiplyAlpha = dargs.UnpremultiplyAlpha,
            };

            if (dargs.BitsPerSample > 0)
            {
                dparams.OutputBitdepth = new JxlBitDepth
                {
                    Type                  = JxlBitDepthType.Custom,
                    BitsPerSample         = (uint)dargs.BitsPerSample,
                    ExponentBitsPerSample = dargs.BitsPerSample == 32 ? 8u : 0u,
                };
            }

            PopulateDefaultAcceptedFormats(dparams);

            return dparams;
        }

        private static bool AddSpecialFlags(bool version, bool help, List<string> list)
        {
            if (version)
            {
                list.Add("--version");

                return true;
            }

            if (help)
            {
                list.Add("--help");

                return true;
            }

            return false;
        }

        private static void AddPositionalPair(string? fileIn, string? fileOut, List<string> list)
        {
            if (!string.IsNullOrWhiteSpace(fileIn))
            {
                list.Add(fileIn);
            }

            if (!string.IsNullOrWhiteSpace(fileOut))
            {
                list.Add(fileOut);
            }
        }

        private static void AddCjxlRateAndEffortArgs(CompressArgs cargs, List<string> list)
        {
            if (cargs.GotQuality && !cargs.GotDistance)
            {
                list.Add($"--quality={cargs.Quality.ToString(CultureInfo.InvariantCulture)}");
            }
            else if (cargs.GotDistance)
            {
                list.Add($"--distance={cargs.Distance.ToString(CultureInfo.InvariantCulture)}");
            }

            if (cargs.GotAlphaDistance)
            {
                list.Add($"--alpha_distance={cargs.AlphaDistance.ToString(CultureInfo.InvariantCulture)}");
            }

            list.Add($"--effort={cargs.Effort.ToString(CultureInfo.InvariantCulture)}");
        }

        private static void AddCjxlOverrideArgs(CompressArgs cargs, List<string> list)
        {
            AddOverrideArg("--modular", cargs.Modular, list);
            AddOverrideArg("--lossless_jpeg", cargs.LosslessJpeg, list);
            AddOverrideArg("--container", cargs.Container, list);
            AddOverrideArg("--keep_invisible", cargs.KeepInvisible, list);
            AddOverrideArg("--dots", cargs.Dots, list);
            AddOverrideArg("--patches", cargs.Patches, list);
            AddOverrideArg("--gaborish", cargs.Gaborish, list);
            AddOverrideArg("--group_order", cargs.GroupOrder, list);
            AddOverrideArg("--progressive_ac", cargs.ProgressiveAc, list);
            AddOverrideArg("--qprogressive_ac", cargs.QProgressiveAc, list);
            AddOverrideArg("--jpeg_reconstruction_cfl", cargs.JpegReconCfl, list);
            list.Add($"--compress_boxes={(cargs.CompressBoxes ? 1 : 0)}");
        }

        private static void AddOverrideArg(string flagName, Override value, List<string> list)
        {
            if (value == Override.On)
            {
                list.Add($"{flagName}=1");
            }
            else if (value == Override.Off)
            {
                list.Add($"{flagName}=0");
            }
        }

        private static void AddCjxlNumericArgs(CompressArgs cargs, List<string> list)
        {
            list.Add($"--brotli_effort={cargs.BrotliEffort.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--epf={cargs.Epf.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--faster_decoding={cargs.FasterDecoding.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--resampling={cargs.Resampling.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--ec_resampling={cargs.EcResampling.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--progressive_dc={cargs.ProgressiveDc.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--responsive={cargs.Responsive.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--num_threads={cargs.NumThreads.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--num_reps={cargs.NumReps.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--codestream_level={cargs.CodestreamLevel.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--override_bitdepth={cargs.OverrideBitdepth.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--upsampling_mode={cargs.UpsamplingMode.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--premultiply={cargs.Premultiply.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--strip_alpha={cargs.StripAlpha.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--center_x={cargs.CenterX.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--center_y={cargs.CenterY.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--buffering={cargs.Buffering.ToString(CultureInfo.InvariantCulture)}");
            AddCjxlFloatNumericArgs(cargs, list);
        }

        private static void AddCjxlFloatNumericArgs(CompressArgs cargs, List<string> list)
        {
            if (cargs.PhotonNoiseIso > 0.0f)
            {
                list.Add($"--photon_noise_iso={cargs.PhotonNoiseIso.ToString(CultureInfo.InvariantCulture)}");
            }

            if (cargs.IntensityTarget > 0.0f)
            {
                list.Add($"--intensity_target={cargs.IntensityTarget.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private static void AddCjxlBooleanAndHintArgs(CompressArgs cargs, List<string> list)
        {
            if (cargs.Progressive)
            {
                list.Add("--progressive");
            }

            if (cargs.AllowExpertOptions)
            {
                list.Add("--allow_expert_options");
            }

            if (cargs.AlreadyDownsampled)
            {
                list.Add("--already_downsampled");
            }

            if (cargs.StreamingInput)
            {
                list.Add("--streaming_input");
            }

            if (cargs.StreamingOutput)
            {
                list.Add("--streaming_output");
            }

            AddCjxlQuietAndHints(cargs, list);
        }

        private static void AddCjxlQuietAndHints(CompressArgs cargs, List<string> list)
        {
            if (cargs.DisableOutput)
            {
                list.Add("--disable_output");
            }

            if (cargs.Quiet)
            {
                list.Add("--quiet");
            }

            for (int i = 0; i < cargs.Verbosity; i++)
            {
                list.Add("-v");
            }

            cargs.ColorHints.Foreach((key, val) =>
            {
                list.Add($"-x");
                list.Add($"{key}={val}");

                return Status.Ok;
            });
        }

        private static void AddDjxlFlagArgs(DecompressArgs dargs, List<string> list)
        {
            if (dargs.PixelsToJpeg)
            {
                list.Add("--pixels_to_jpeg");
            }

            if (dargs.AllowPartialFiles)
            {
                list.Add("--allow_partial_files");
            }

            if (dargs.AllowMoreProgressiveSteps)
            {
                list.Add("--allow_more_progressive_steps");
            }

            if (dargs.UnpremultiplyAlpha)
            {
                list.Add("--unpremultiply_alpha");
            }

            if (dargs.OutputFrames)
            {
                list.Add("--output_frames");
            }

            AddDjxlSecondaryFlagArgs(dargs, list);
        }

        private static void AddDjxlSecondaryFlagArgs(DecompressArgs dargs, List<string> list)
        {
            if (dargs.OutputExtraChannels)
            {
                list.Add("--output_extra_channels");
            }

            if (dargs.UseSjpeg)
            {
                list.Add("--use_sjpeg");
            }

            if (dargs.PrintReadBytes)
            {
                list.Add("--print_read_bytes");
            }

            if (dargs.DisableOutput)
            {
                list.Add("--disable_output");
            }

            if (dargs.Quiet)
            {
                list.Add("--quiet");
            }
        }

        private static void AddDjxlNumericArgs(DecompressArgs dargs, List<string> list)
        {
            list.Add($"--bits_per_sample={dargs.BitsPerSample.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--num_threads={dargs.NumThreads.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--num_reps={dargs.NumReps.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--downsampling={dargs.MaxDownsampling.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--jpeg_quality={dargs.JpegQuality.ToString(CultureInfo.InvariantCulture)}");
            list.Add($"--render_spotcolors={(dargs.RenderSpotcolors ? 1 : 0)}");
            list.Add($"--coalescing={(dargs.Coalescing ? 1 : 0)}");

            if (dargs.MaxPasses < uint.MaxValue)
            {
                list.Add($"--num_passes={dargs.MaxPasses.ToString(CultureInfo.InvariantCulture)}");
            }

            if (dargs.DisplayNits > 0.0)
            {
                list.Add($"--display_nits={dargs.DisplayNits.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private static void AddDjxlStringArgs(DecompressArgs dargs, List<string> list)
        {
            AddOptionalStringArg("--color_space", dargs.ColorSpace, list);
            AddOptionalStringArg("--alpha_blend", dargs.AlphaBlend, list);
            AddOptionalStringArg("--preview_out", dargs.PreviewOut, list);
            AddOptionalStringArg("--icc_out", dargs.IccOut, list);
            AddOptionalStringArg("--orig_icc_out", dargs.OrigIccOut, list);
            AddOptionalStringArg("--metadata_out", dargs.MetadataOut, list);
        }

        private static void AddOptionalStringArg(string key, string? value, List<string> list)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add($"{key}={value}");
            }
        }

        private static void AddJxlTranOperationFlags(JxlTranArgs targs, List<string> list)
        {
            if (targs.Pack)
            {
                list.Add("--pack");
            }

            if (targs.Extract)
            {
                list.Add("--extract");
            }

            if (targs.Quiet)
            {
                list.Add("--quiet");
            }

            foreach (string addSpec in targs.AddBoxSpecs)
            {
                list.Add($"--add_box={addSpec}");
            }

            foreach (string remType in targs.RemoveBoxTypes)
            {
                list.Add($"--remove_box={remType}");
            }
        }

        private static float ResolveEffectiveDistance(CompressArgs cargs, bool isJpegInput)
        {
            if (cargs.GotQuality && !cargs.GotDistance)
            {
                return JxlEncoder.DistanceFromQuality(cargs.Quality);
            }

            if (cargs.GotDistance)
            {
                return Math.Clamp(cargs.Distance, 0.0f, 25.0f);
            }

            bool defaultLossless = cargs.LosslessJpeg == Override.On && isJpegInput;

            return defaultLossless ? 0.0f : 1.0f;
        }

        private static void PopulateFrameOptions(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf,
            float distance)
        {
            jparams.AddOption(JxlEncoderFrameSettingId.Effort, cargs.Effort);
            jparams.AddOption(JxlEncoderFrameSettingId.BrotliEffort, cargs.BrotliEffort);
            jparams.AddOption(JxlEncoderFrameSettingId.Buffering, cargs.Buffering);
            jparams.AddOption(JxlEncoderFrameSettingId.Epf, cargs.Epf);
            jparams.AddOption(JxlEncoderFrameSettingId.DecodingSpeed, cargs.FasterDecoding);
            jparams.AddOption(JxlEncoderFrameSettingId.Resampling, cargs.Resampling);
            jparams.AddOption(JxlEncoderFrameSettingId.ExtraChannelResampling, cargs.EcResampling);

            if (cargs.PhotonNoiseIso > 0.0f)
            {
                jparams.AddFloatOption(JxlEncoderFrameSettingId.PhotonNoise, cargs.PhotonNoiseIso);
            }

            if (cargs.Modular == Override.On || distance == 0.0f)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
            }
            else if (cargs.Modular == Override.Off)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 0);
            }

            PopulateSecondaryFrameOptions(cargs, jparams, ppf);
        }

        private static void PopulateSecondaryFrameOptions(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf)
        {
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.KeepInvisible, cargs.KeepInvisible);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Dots, cargs.Dots);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Patches, cargs.Patches);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.Gaborish, cargs.Gaborish);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.GroupOrder, cargs.GroupOrder);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.ProgressiveAc, cargs.ProgressiveAc);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.QProgressiveAc, cargs.QProgressiveAc);
            AddOverrideOption(jparams, JxlEncoderFrameSettingId.JpegReconCfl, cargs.JpegReconCfl);

            if (cargs.Progressive)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.QProgressiveAc, 1);
                jparams.AddOption(JxlEncoderFrameSettingId.Responsive, 1);
                jparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, cargs.GotProgressiveDc ? cargs.ProgressiveDc : 1);
            }
            else
            {
                if (cargs.ProgressiveDc >= -1)
                {
                    jparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, cargs.ProgressiveDc);
                }

                if (cargs.Responsive >= -1)
                {
                    jparams.AddOption(JxlEncoderFrameSettingId.Responsive, cargs.Responsive);
                }
            }

            AddCenterOptions(cargs, jparams, ppf);
        }

        private static void AddCenterOptions(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf)
        {
            if (cargs.CenterX >= -1 && (ppf.XSize == 0 || (nuint)cargs.CenterX < ppf.XSize))
            {
                jparams.AddOption(JxlEncoderFrameSettingId.GroupOrderCenterX, cargs.CenterX);
            }

            if (cargs.CenterY >= -1 && (ppf.YSize == 0 || (nuint)cargs.CenterY < ppf.YSize))
            {
                jparams.AddOption(JxlEncoderFrameSettingId.GroupOrderCenterY, cargs.CenterY);
            }
        }

        private static void AddOverrideOption(
            JXLCompressParams jparams,
            JxlEncoderFrameSettingId id,
            Override flag)
        {
            if (flag == Override.On)
            {
                jparams.AddOption(id, 1);
            }
            else if (flag == Override.Off)
            {
                jparams.AddOption(id, 0);
            }
        }

        private static void PopulateDefaultAcceptedFormats(JXLDecompressParams dparams)
        {
            uint[] channels     = { 1u, 2u, 3u, 4u };
            JxlDataType[] types = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };

            foreach (uint c in channels)
            {
                foreach (JxlDataType dt in types)
                {
                    dparams.AcceptedFormats.Add(new JxlPixelFormat
                    {
                        NumChannels = c,
                        DataType    = dt,
                        Endianness  = JxlEndianness.NativeEndian,
                        Align       = 0,
                    });
                }
            }
        }
    }
}
