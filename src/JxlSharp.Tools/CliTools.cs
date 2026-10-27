// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Return codes for <see cref="CjxlTool"/>.
    /// Ported from <c>CjxlRetCode</c> in <c>tools/cjxl_main.cc</c>.
    /// </summary>
    public enum CjxlRetCode : int
    {
        /// <summary>Encoding succeeded with no warnings.</summary>
        Ok = 0,

        /// <summary>Encoding or argument validation failed.</summary>
        Err = 1,

        /// <summary>Encoding succeeded, but JPEG reconstruction metadata could not be created.</summary>
        DroppedJbrd = 2,
    }

    /// <summary>
    /// Command-line arguments for the <c>cjxl</c> encoder tool.
    /// Ported from <c>CompressArgs</c> in <c>tools/cjxl_main.cc</c>.
    /// </summary>
    public sealed class CompressArgs
    {
        /// <summary>Gets or sets the input file path.</summary>
        public string? FileIn { get; set; }

        /// <summary>Gets or sets the output file path.</summary>
        public string? FileOut { get; set; }

        /// <summary>Gets or sets the color hints passed to input decoders.</summary>
        public ColorHints ColorHints { get; } = new ColorHints();

        /// <summary>Gets or sets the frame indexing box spec.</summary>
        public string FrameIndexing { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether to decode JPEG to pixels instead of lossless transcoding.</summary>
        public bool DecodeToPixels { get; set; }

        /// <summary>Gets or sets the number of encoding repetitions for benchmarking.</summary>
        public nuint NumReps { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether output file writing is disabled.</summary>
        public bool DisableOutput { get; set; }

        /// <summary>Gets or sets the profile printing override.</summary>
        public Override PrintProfile { get; set; } = Override.Default;

        /// <summary>Gets or sets a value indicating whether version was requested.</summary>
        public bool Version { get; set; }

        /// <summary>Gets or sets a value indicating whether quiet mode is enabled.</summary>
        public bool Quiet { get; set; }

        /// <summary>Gets or sets a value indicating whether help was requested.</summary>
        public bool Help { get; set; }

        /// <summary>Gets or sets the verbosity level.</summary>
        public int Verbosity { get; set; }

        /// <summary>Gets or sets a value indicating whether expert encoder options are allowed.</summary>
        public bool AllowExpertOptions { get; set; }

        /// <summary>Gets or sets the container format override.</summary>
        public Override Container { get; set; } = Override.Default;

        /// <summary>Gets or sets the modular mode override.</summary>
        public Override Modular { get; set; } = Override.Default;

        /// <summary>Gets or sets the keep-invisible pixels override.</summary>
        public Override KeepInvisible { get; set; } = Override.Default;

        /// <summary>Gets or sets the dots generation override.</summary>
        public Override Dots { get; set; } = Override.Default;

        /// <summary>Gets or sets the patches generation override.</summary>
        public Override Patches { get; set; } = Override.Default;

        /// <summary>Gets or sets the gaborish filter override.</summary>
        public Override Gaborish { get; set; } = Override.Default;

        /// <summary>Gets or sets the group order override.</summary>
        public Override GroupOrder { get; set; } = Override.Default;

        /// <summary>Gets or sets the progressive mode override.</summary>
        public bool Progressive { get; set; }

        /// <summary>Gets or sets the progressive AC override.</summary>
        public Override ProgressiveAc { get; set; } = Override.Default;

        /// <summary>Gets or sets the quantized progressive AC override.</summary>
        public Override QProgressiveAc { get; set; } = Override.Default;

        /// <summary>Gets or sets the progressive DC level (-1 for default).</summary>
        public long ProgressiveDc { get; set; } = -1;

        /// <summary>Gets or sets the squeeze responsive mode (-1 for default).</summary>
        public long Responsive { get; set; } = -1;

        /// <summary>Gets or sets the lossless JPEG transcoding override.</summary>
        public Override LosslessJpeg { get; set; } = Override.Default;

        /// <summary>Gets or sets the JPEG reconstruction CfL override.</summary>
        public Override JpegReconCfl { get; set; } = Override.Default;

        /// <summary>Gets or sets the number of worker threads (-1 for default).</summary>
        public long NumThreads { get; set; } = -1;

        /// <summary>Gets or sets the photon noise ISO setting.</summary>
        public float PhotonNoiseIso { get; set; }

        /// <summary>Gets or sets a value indicating whether input is already downsampled.</summary>
        public bool AlreadyDownsampled { get; set; }

        /// <summary>Gets or sets the faster decoding level (0..4).</summary>
        public long FasterDecoding { get; set; }

        /// <summary>Gets or sets the color resampling factor (-1, 1, 2, 4, 8).</summary>
        public long Resampling { get; set; } = -1;

        /// <summary>Gets or sets the extra channel resampling factor (-1, 1, 2, 4, 8).</summary>
        public long EcResampling { get; set; } = -1;

        /// <summary>Gets or sets the edge-preserving filter level (-1..3).</summary>
        public long Epf { get; set; } = -1;

        /// <summary>Gets or sets the Brotli effort level (-1..11).</summary>
        public long BrotliEffort { get; set; } = -1;

        /// <summary>Gets or sets the streaming buffering mode (-1..3).</summary>
        public long Buffering { get; set; } = -1;

        /// <summary>Gets or sets the encoder effort level (1..10 or 11 with expert options).</summary>
        public long Effort { get; set; } = 7;

        /// <summary>Gets or sets a value indicating whether streaming input is enabled.</summary>
        public bool StreamingInput { get; set; }

        /// <summary>Gets or sets a value indicating whether streaming output is enabled.</summary>
        public bool StreamingOutput { get; set; }

        /// <summary>Gets or sets a value indicating whether metadata boxes are compressed with Brotli.</summary>
        public bool CompressBoxes { get; set; } = true;

        /// <summary>Gets or sets the Butteraugli distance (-1 if unset).</summary>
        public float Distance { get; set; } = -1.0f;

        /// <summary>Gets or sets the alpha channel distance (-1 if unset).</summary>
        public float AlphaDistance { get; set; } = -1.0f;

        /// <summary>Gets or sets the quality setting (-1000 if unset).</summary>
        public float Quality { get; set; } = -1000.0f;

        /// <summary>Gets or sets the intensity target in nits (0 for default).</summary>
        public float IntensityTarget { get; set; }

        /// <summary>Gets or sets the center-first group order X coordinate.</summary>
        public long CenterX { get; set; } = -1;

        /// <summary>Gets or sets the center-first group order Y coordinate.</summary>
        public long CenterY { get; set; } = -1;

        /// <summary>Gets or sets the upsampling mode (-1, 0, 1).</summary>
        public long UpsamplingMode { get; set; } = -1;

        /// <summary>Gets or sets the override bit depth (0 for none).</summary>
        public nuint OverrideBitdepth { get; set; }

        /// <summary>Gets or sets the codestream level (-1, 5, 10).</summary>
        public int CodestreamLevel { get; set; } = -1;

        /// <summary>Gets or sets the premultiply alpha flag (-1, 0, 1).</summary>
        public int Premultiply { get; set; } = -1;

        /// <summary>Gets or sets the strip alpha mode (-1, 0, 1, 2).</summary>
        public int StripAlpha { get; set; } = -1;

        /// <summary>Gets or sets a value indicating whether Distance was explicitly provided.</summary>
        public bool GotDistance { get; set; }

        /// <summary>Gets or sets a value indicating whether AlphaDistance was explicitly provided.</summary>
        public bool GotAlphaDistance { get; set; }

        /// <summary>Gets or sets a value indicating whether Quality was explicitly provided.</summary>
        public bool GotQuality { get; set; }

        /// <summary>Gets or sets a value indicating whether ProgressiveDc was explicitly provided.</summary>
        public bool GotProgressiveDc { get; set; }

        /// <summary>Gets or sets a value indicating whether LosslessJpeg was explicitly provided.</summary>
        public bool GotLosslessJpeg { get; set; }

        /// <summary>Gets or sets a value indicating whether Effort was explicitly provided.</summary>
        public bool GotEffort { get; set; }
    }

    /// <summary>
    /// JPEG XL command-line encoder (<c>cjxl</c>).
    /// Ported from <c>tools/cjxl_main.cc</c>.
    /// </summary>
    public static class CjxlTool
    {
        /// <summary>
        /// Runs the <c>cjxl</c> command-line tool with the specified arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code (<see cref="CjxlRetCode"/>).</returns>
        public static int Run(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Runs the <c>cjxl</c> command-line tool with custom output writers.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Standard output writer.</param>
        /// <param name="stderr">Standard error writer.</param>
        /// <returns>Process exit code (<see cref="CjxlRetCode"/>).</returns>
        public static int Run(string[] args, TextWriter? stdout, TextWriter? stderr)
        {
            TextWriter outWriter = stdout ?? Console.Out;
            TextWriter errWriter = stderr ?? Console.Error;
            CompressArgs cargs   = new CompressArgs();

            if (!ParseCompressArgs(args ?? Array.Empty<string>(), cargs, errWriter))
            {
                PrintCjxlHelp(outWriter, 0);

                return (int)CjxlRetCode.Err;
            }

            if (cargs.Version)
            {
                outWriter.WriteLine($"cjxl {FormatConfigVersion(JxlEncoder.Version())}");

                return (int)CjxlRetCode.Ok;
            }

            if (!cargs.Quiet)
            {
                errWriter.WriteLine($"JPEG XL encoder {FormatConfigVersion(JxlEncoder.Version())}");
            }

            if (cargs.Help || string.IsNullOrEmpty(cargs.FileIn))
            {
                PrintCjxlHelp(outWriter, cargs.Verbosity);

                return (int)CjxlRetCode.Ok;
            }

            if (string.IsNullOrEmpty(cargs.FileOut) && !cargs.DisableOutput)
            {
                errWriter.WriteLine("No output file specified and --disable_output flag was not used.");

                return (int)CjxlRetCode.Err;
            }

            return ExecuteCompression(cargs, errWriter);
        }

        private static int ExecuteCompression(CompressArgs cargs, TextWriter errWriter)
        {
            if (!TryReadInputFile(cargs.FileIn!, out byte[] imageData, errWriter))
            {
                return (int)CjxlRetCode.Err;
            }

            JXLCompressParams jparams = new JXLCompressParams();
            PackedPixelFile ppf       = new PackedPixelFile();
            Codec codec               = ExtrasDecodeFacade.DetectCodec(imageData);

            if (!PrepareInputForCompression(cargs, imageData, codec, ppf, errWriter))
            {
                return (int)CjxlRetCode.Err;
            }

            if (!PopulateCompressParams(cargs, jparams, ppf, codec, errWriter))
            {
                return (int)CjxlRetCode.Err;
            }

            return RunCompressionLoop(cargs, jparams, ppf, imageData, codec, errWriter);
        }

        private static bool PrepareInputForCompression(
            CompressArgs cargs,
            byte[] imageData,
            Codec codec,
            PackedPixelFile ppf,
            TextWriter errWriter)
        {
            if (codec != Codec.Jpg)
            {
                cargs.LosslessJpeg = Override.Off;
            }

            if (cargs.LosslessJpeg == Override.On && cargs.GotDistance)
            {
                errWriter.WriteLine("Lossy encoding options cannot be used with lossless JPEG recompression.");
                errWriter.WriteLine("Consider adding -j 0 to allow lossy encoding.");

                return false;
            }

            bool decodePixels = cargs.LosslessJpeg != Override.On || cargs.DecodeToPixels || cargs.StreamingInput;

            if (!decodePixels)
            {
                return true;
            }

            Status status = ExtrasDecodeFacade.DecodeBytes(
                imageData,
                cargs.ColorHints,
                ppf,
                null,
                out Codec decodedCodec);

            if (!status)
            {
                errWriter.WriteLine($"Getting pixel data failed: {status}");

                return false;
            }

            if (decodedCodec == Codec.Jpg && cargs.LosslessJpeg != Override.On && !cargs.GotQuality && !cargs.GotDistance)
            {
                cargs.Distance    = 1.0f;
                cargs.GotDistance = true;
            }

            return true;
        }

        private static int RunCompressionLoop(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf,
            byte[] imageData,
            Codec codec,
            TextWriter errWriter)
        {
            using ThreadPoolInternal pool = new ThreadPoolInternal((int)cargs.NumThreads);
            jparams.Runner                = pool;

            byte[] compressed = Array.Empty<byte>();
            byte[]? jpegBytes = cargs.LosslessJpeg == Override.On && codec == Codec.Jpg ? imageData : null;
            CjxlRetCode ret   = CjxlRetCode.Ok;
            nuint reps        = cargs.NumReps > 0 ? cargs.NumReps : 1;
            SpeedStats stats  = new SpeedStats();

            for (nuint i = 0; i < reps; i++)
            {
                double t0 = ExtrasTime.Now();
                bool ok   = CodecJxl.EncodeImageJXL(jparams, ppf, jpegBytes, out compressed);
                double t1 = ExtrasTime.Now();

                if (!ok && jpegBytes is not null)
                {
                    ret = CjxlRetCode.DroppedJbrd;
                    errWriter.WriteLine("Warning: could not create JPEG reconstruction data, encoding pixels instead.");

                    Status decStatus = ExtrasDecodeFacade.DecodeBytes(imageData, cargs.ColorHints, ppf, null, out _);

                    if (!decStatus)
                    {
                        errWriter.WriteLine("Failed to decode JPEG pixels.");

                        return (int)CjxlRetCode.Err;
                    }

                    jpegBytes = null;
                    t0        = ExtrasTime.Now();
                    ok        = CodecJxl.EncodeImageJXL(jparams, ppf, null, out compressed);
                    t1        = ExtrasTime.Now();
                }

                if (!ok)
                {
                    errWriter.WriteLine("EncodeImageJXL() failed.");

                    return (int)CjxlRetCode.Err;
                }

                stats.NotifyElapsed(t1 - t0);
                stats.SetImageSize(ppf.Info.XSize, ppf.Info.YSize);
                stats.SetFileSize((nuint)compressed.Length);

                if (!cargs.Quiet && i + 1 == reps)
                {
                    PrintEncodeSummary(ppf, compressed.Length, t1 - t0, errWriter);
                    stats.Print(pool.NumWorkerThreads, errWriter);
                }
            }

            if (!cargs.DisableOutput && !string.IsNullOrEmpty(cargs.FileOut))
            {
                if (!TryWriteOutputFile(cargs.FileOut!, compressed, errWriter))
                {
                    return (int)CjxlRetCode.Err;
                }
            }

            return (int)ret;
        }

        private static void PrintEncodeSummary(
            PackedPixelFile ppf,
            int compressedBytes,
            double elapsedSeconds,
            TextWriter errWriter)
        {
            nuint pixels = ppf.XSize * ppf.YSize;

            if (pixels == 0)
            {
                errWriter.WriteLine($"Compressed to {compressedBytes} bytes ({elapsedSeconds * 1000.0:F2} ms).");

                return;
            }

            double bpp = compressedBytes * 8.0 / pixels;
            double mps = pixels / 1e6 / Math.Max(elapsedSeconds, 1e-9);

            errWriter.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Compressed to {0} bytes ({1:F3} bpp, {2:F2} MP/s).",
                    compressedBytes,
                    bpp,
                    mps));
        }

        private static bool PopulateCompressParams(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf,
            Codec codec,
            TextWriter errWriter)
        {
            if (!ValidateRangeFlags(cargs, errWriter))
            {
                return false;
            }

            if (!ResolveDistanceFromFlags(cargs, codec, errWriter))
            {
                return false;
            }

            jparams.Distance           = cargs.Distance;
            jparams.AlphaDistance      = cargs.GotAlphaDistance ? cargs.AlphaDistance : (cargs.Distance == 0.0f ? 0.0f : -1.0f);
            jparams.UseContainer       = cargs.Container == Override.On;
            jparams.CompressBoxes      = cargs.CompressBoxes;
            jparams.IntensityTarget    = cargs.IntensityTarget;
            jparams.UpsamplingMode     = (int)cargs.UpsamplingMode;
            jparams.OverrideBitdepth   = cargs.OverrideBitdepth;
            jparams.CodestreamLevel    = cargs.CodestreamLevel;
            jparams.Premultiply        = cargs.Premultiply;
            jparams.StripAlpha         = cargs.StripAlpha;
            jparams.AllowExpertOptions = cargs.AllowExpertOptions;

            if (cargs.AlreadyDownsampled && cargs.Resampling > 0)
            {
                jparams.AlreadyDownsampled = (int)cargs.Resampling;
            }

            AddEncoderOptionsToParams(cargs, jparams, ppf);

            return true;
        }

        private static bool ValidateRangeFlags(CompressArgs cargs, TextWriter errWriter)
        {
            long maxEffort = cargs.AllowExpertOptions ? 11 : 10;

            if (cargs.Effort < 1 || cargs.Effort > maxEffort)
            {
                errWriter.WriteLine($"Invalid flag value for --effort: valid range is {{1, ..., {maxEffort}}}.");

                return false;
            }

            if (cargs.BrotliEffort < -1 || cargs.BrotliEffort > 11)
            {
                errWriter.WriteLine("Invalid flag value for --brotli_effort: valid range is {-1, 0, ..., 11}.");

                return false;
            }

            if (cargs.Epf < -1 || cargs.Epf > 3)
            {
                errWriter.WriteLine("Invalid flag value for --epf: valid range is {-1, 0, 1, 2, 3}.");

                return false;
            }

            if (cargs.FasterDecoding < 0 || cargs.FasterDecoding > 4)
            {
                errWriter.WriteLine("Invalid flag value for --faster_decoding: valid range is {0, 1, 2, 3, 4}.");

                return false;
            }

            if (!IsValidResampling(cargs.Resampling) || !IsValidResampling(cargs.EcResampling))
            {
                errWriter.WriteLine("Invalid resampling factor: valid values are {-1, 1, 2, 4, 8}.");

                return false;
            }

            return true;
        }

        private static bool IsValidResampling(long value)
        {
            return value == -1 || value == 1 || value == 2 || value == 4 || value == 8;
        }

        private static bool ResolveDistanceFromFlags(CompressArgs cargs, Codec codec, TextWriter errWriter)
        {
            if (cargs.GotDistance && cargs.GotQuality)
            {
                errWriter.WriteLine("MustSpecifyAtMostOneOf --distance and --quality.");

                return false;
            }

            if (cargs.GotQuality)
            {
                if (cargs.Quality < 100.0f && cargs.LosslessJpeg == Override.Default)
                {
                    cargs.LosslessJpeg = Override.Off;
                }

                cargs.Distance    = JxlEncoder.DistanceFromQuality(cargs.Quality);
                cargs.GotDistance = true;
            }

            if (!cargs.GotDistance)
            {
                bool defaultLossless = cargs.LosslessJpeg == Override.On && codec == Codec.Jpg;

                cargs.Distance = defaultLossless ? 0.0f : 1.0f;
            }

            if (cargs.Distance < 0.0f || cargs.Distance > 25.0f)
            {
                errWriter.WriteLine("Invalid --distance value: valid range is [0.0 .. 25.0].");

                return false;
            }

            if (cargs.Distance == 0.0f)
            {
                cargs.Modular = Override.On;
            }

            return true;
        }

        private static void AddEncoderOptionsToParams(
            CompressArgs cargs,
            JXLCompressParams jparams,
            PackedPixelFile ppf)
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

            if (cargs.Progressive)
            {
                cargs.QProgressiveAc = Override.On;
                cargs.Responsive     = 1;

                if (!cargs.GotProgressiveDc)
                {
                    cargs.ProgressiveDc = 1;
                }
            }

            if (cargs.Modular == Override.On || cargs.Distance == 0.0f)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
            }
            else if (cargs.Modular == Override.Off)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Modular, 0);
            }

            AddSecondaryEncoderOptions(cargs, jparams, ppf);
        }

        private static void AddSecondaryEncoderOptions(
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

            if (cargs.ProgressiveDc >= -1)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, cargs.ProgressiveDc);
            }

            if (cargs.Responsive >= -1)
            {
                jparams.AddOption(JxlEncoderFrameSettingId.Responsive, cargs.Responsive);
            }

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

        private static bool ParseCompressArgs(string[] args, CompressArgs cargs, TextWriter errWriter)
        {
            int positionalIndex = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal) || arg == "-")
                {
                    if (!AssignCjxlPositional(arg, ref positionalIndex, cargs, errWriter))
                    {
                        return false;
                    }

                    continue;
                }

                if (!ParseSingleCjxlOption(args, ref i, cargs, errWriter))
                {
                    return false;
                }
            }

            if (!cargs.GotLosslessJpeg)
            {
                cargs.LosslessJpeg = Override.On;
            }

            return true;
        }

        private static bool AssignCjxlPositional(
            string arg,
            ref int positionalIndex,
            CompressArgs cargs,
            TextWriter errWriter)
        {
            if (positionalIndex == 0)
            {
                cargs.FileIn = arg;
                positionalIndex++;

                return true;
            }

            if (positionalIndex == 1)
            {
                cargs.FileOut = arg;
                positionalIndex++;

                return true;
            }

            errWriter.WriteLine($"Unexpected positional argument: {arg}");

            return false;
        }

        private static bool ParseSingleCjxlOption(
            string[] args,
            ref int index,
            CompressArgs cargs,
            TextWriter errWriter)
        {
            SplitOptionToken(args[index], out string key, out string? inlineVal);

            if (TryHandleCjxlFlagOption(key, cargs))
            {
                return true;
            }

            if (!TryConsumeOptionValue(args, ref index, inlineVal, out string val, errWriter, key))
            {
                return false;
            }

            return ApplyCjxlValueOption(key, val, cargs, errWriter);
        }

        private static bool TryHandleCjxlFlagOption(string key, CompressArgs cargs)
        {
            if (key == "-V" || key == "--version")
            {
                cargs.Version = true;

                return true;
            }

            if (key == "-h" || key == "--help")
            {
                cargs.Help = true;

                return true;
            }

            if (key == "--quiet")
            {
                cargs.Quiet = true;

                return true;
            }

            if (key == "-v" || key == "--verbose")
            {
                cargs.Verbosity++;

                return true;
            }

            if (key == "-p" || key == "--progressive")
            {
                cargs.Progressive = true;

                return true;
            }

            return TryHandleCjxlExtendedFlag(key, cargs);
        }

        private static bool TryHandleCjxlExtendedFlag(string key, CompressArgs cargs)
        {
            if (key == "--allow_expert_options")
            {
                cargs.AllowExpertOptions = true;

                return true;
            }

            if (key == "--already_downsampled")
            {
                cargs.AlreadyDownsampled = true;

                return true;
            }

            if (key == "--streaming_input")
            {
                cargs.StreamingInput = true;

                return true;
            }

            if (key == "--streaming_output")
            {
                cargs.StreamingOutput = true;

                return true;
            }

            if (key == "--disable_output")
            {
                cargs.DisableOutput = true;

                return true;
            }

            return false;
        }

        private static bool ApplyCjxlValueOption(
            string key,
            string val,
            CompressArgs cargs,
            TextWriter errWriter)
        {
            if (TryApplyCjxlPrimaryValue(key, val, cargs))
            {
                return true;
            }

            if (TryApplyCjxlOverrideValue(key, val, cargs))
            {
                return true;
            }

            if (TryApplyCjxlNumericValue(key, val, cargs))
            {
                return true;
            }

            errWriter.WriteLine($"Unknown or invalid option: {key} {val}");

            return false;
        }

        private static bool TryApplyCjxlPrimaryValue(string key, string val, CompressArgs cargs)
        {
            if (key == "-d" || key == "--distance")
            {
                if (!TryParseFloat(val, out float dist))
                {
                    return false;
                }

                cargs.Distance    = dist;
                cargs.GotDistance = true;

                return true;
            }

            if (key == "-a" || key == "--alpha_distance")
            {
                if (!TryParseFloat(val, out float adist))
                {
                    return false;
                }

                cargs.AlphaDistance    = adist;
                cargs.GotAlphaDistance = true;

                return true;
            }

            if (key == "-q" || key == "--quality" || key == "--modular_lossy_palette")
            {
                if (!TryParseFloat(val, out float q))
                {
                    return false;
                }

                cargs.Quality    = q;
                cargs.GotQuality = true;

                return true;
            }

            return TryApplyCjxlSecondaryValue(key, val, cargs);
        }

        private static bool TryApplyCjxlSecondaryValue(string key, string val, CompressArgs cargs)
        {
            if (key == "-e" || key == "--effort")
            {
                if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long eff))
                {
                    return false;
                }

                cargs.Effort    = eff;
                cargs.GotEffort = true;

                return true;
            }

            if (key == "-x" || key == "--dec-hints")
            {
                return TryParseAndAddHint(val, cargs.ColorHints);
            }

            return false;
        }

        private static bool TryApplyCjxlOverrideValue(string key, string val, CompressArgs cargs)
        {
            if (!TryParseOverride(val, out Override ov))
            {
                return false;
            }

            if (key == "--container")
            {
                cargs.Container = ov;

                return true;
            }

            if (key == "-m" || key == "--modular")
            {
                cargs.Modular = ov;

                return true;
            }

            if (key == "-j" || key == "--lossless_jpeg")
            {
                cargs.LosslessJpeg    = ov;
                cargs.GotLosslessJpeg = true;

                return true;
            }

            if (key == "--jpeg_reconstruction_cfl")
            {
                cargs.JpegReconCfl = ov;

                return true;
            }

            if (key == "--compress_boxes")
            {
                cargs.CompressBoxes = ov != Override.Off;

                return true;
            }

            return TryApplyCjxlFeatureOverride(key, ov, cargs);
        }

        private static bool TryApplyCjxlFeatureOverride(string key, Override ov, CompressArgs cargs)
        {
            if (key == "--keep_invisible")
            {
                cargs.KeepInvisible = ov;

                return true;
            }

            if (key == "--dots")
            {
                cargs.Dots = ov;

                return true;
            }

            if (key == "--patches")
            {
                cargs.Patches = ov;

                return true;
            }

            if (key == "--gaborish")
            {
                cargs.Gaborish = ov;

                return true;
            }

            if (key == "--group_order")
            {
                cargs.GroupOrder = ov;

                return true;
            }

            if (key == "--progressive_ac")
            {
                cargs.ProgressiveAc = ov;

                return true;
            }

            if (key == "--qprogressive_ac")
            {
                cargs.QProgressiveAc = ov;

                return true;
            }

            return false;
        }

        private static bool TryApplyCjxlNumericValue(string key, string val, CompressArgs cargs)
        {
            if (key == "--photon_noise_iso" && TryParseFloat(val, out float iso))
            {
                cargs.PhotonNoiseIso = iso;

                return true;
            }

            if (key == "--intensity_target" && TryParseFloat(val, out float nits))
            {
                cargs.IntensityTarget = nits;

                return true;
            }

            if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lval))
            {
                return false;
            }

            return AssignCjxlLongOption(key, lval, cargs) || AssignCjxlExtraLongOption(key, lval, cargs);
        }

        private static bool AssignCjxlLongOption(string key, long lval, CompressArgs cargs)
        {
            if (key == "--brotli_effort")
            {
                cargs.BrotliEffort = lval;

                return true;
            }

            if (key == "--epf")
            {
                cargs.Epf = lval;

                return true;
            }

            if (key == "--faster_decoding")
            {
                cargs.FasterDecoding = lval;

                return true;
            }

            if (key == "--resampling")
            {
                cargs.Resampling = lval;

                return true;
            }

            if (key == "--ec_resampling")
            {
                cargs.EcResampling = lval;

                return true;
            }

            if (key == "--progressive_dc")
            {
                cargs.ProgressiveDc    = lval;
                cargs.GotProgressiveDc = true;

                return true;
            }

            if (key == "-R" || key == "--responsive")
            {
                cargs.Responsive = lval;

                return true;
            }

            return false;
        }

        private static bool AssignCjxlExtraLongOption(string key, long lval, CompressArgs cargs)
        {
            if (key == "--num_threads")
            {
                cargs.NumThreads = lval;

                return true;
            }

            if (key == "--num_reps")
            {
                cargs.NumReps = lval > 0 ? (nuint)lval : 1;

                return true;
            }

            if (key == "--codestream_level")
            {
                cargs.CodestreamLevel = (int)lval;

                return true;
            }

            if (key == "--override_bitdepth")
            {
                cargs.OverrideBitdepth = lval >= 0 ? (nuint)lval : 0;

                return true;
            }

            if (key == "--upsampling_mode")
            {
                cargs.UpsamplingMode = lval;

                return true;
            }

            if (key == "--premultiply")
            {
                cargs.Premultiply = (int)lval;

                return true;
            }

            return AssignCjxlCenterAndAlphaOption(key, lval, cargs);
        }

        private static bool AssignCjxlCenterAndAlphaOption(string key, long lval, CompressArgs cargs)
        {
            if (key == "--strip_alpha")
            {
                cargs.StripAlpha = (int)lval;

                return true;
            }

            if (key == "--center_x")
            {
                cargs.CenterX = lval;

                return true;
            }

            if (key == "--center_y")
            {
                cargs.CenterY = lval;

                return true;
            }

            if (key == "--buffering")
            {
                cargs.Buffering = lval;

                return true;
            }

            return false;
        }

        private static void PrintCjxlHelp(TextWriter writer, int verbosity)
        {
            _ = verbosity;
            writer.WriteLine("Usage: cjxl INPUT OUTPUT [OPTIONS...]");
            writer.WriteLine("  INPUT                      the input can be " + ExtrasDecodeFacade.ListOfDecodeCodecs());
            writer.WriteLine("  OUTPUT                     the compressed JXL output file");
            writer.WriteLine("  -d, --distance=DISTANCE    Max. butteraugli distance (0 = lossless, default = 1.0)");
            writer.WriteLine("  -q, --quality=QUALITY      Quality setting (100 = lossless, default = 90)");
            writer.WriteLine("  -e, --effort=EFFORT        Encoder effort setting (1..10, default = 7)");
            writer.WriteLine("  -m, --modular=0|1          Use modular mode (0 = varDCT, 1 = modular)");
            writer.WriteLine("  -j, --lossless_jpeg=0|1    Losslessly transcode JPEG (default = 1)");
            writer.WriteLine("  -p, --progressive          Enable progressive/responsive decoding");
            writer.WriteLine("  --num_threads=N            Number of worker threads (-1 = default)");
            writer.WriteLine("  --quiet                    Be more silent");
            writer.WriteLine("  -v, --verbose              Verbose output; can be repeated");
            writer.WriteLine("  -V, --version              Print encoder library version and exit");
            writer.WriteLine("  -h, --help                 Prints this help message");
        }

        internal static string FormatConfigVersion(uint version)
        {
            uint major = version / 1000000;
            uint minor = (version / 1000) % 1000;
            uint patch = version % 1000;

            return $"v{major}.{minor}.{patch} [JxlSharp]";
        }

        internal static bool TryReadInputFile(string path, out byte[] bytes, TextWriter errWriter)
        {
            bytes = Array.Empty<byte>();

            try
            {
                if (path == "-")
                {
                    using MemoryStream ms = new MemoryStream();

                    Console.OpenStandardInput().CopyTo(ms);
                    bytes = ms.ToArray();

                    return true;
                }

                bytes = File.ReadAllBytes(path);

                return true;
            }
            catch (Exception ex)
            {
                errWriter.WriteLine($"Failed to read file \"{path}\": {ex.Message}");

                return false;
            }
        }

        internal static bool TryWriteOutputFile(string path, ReadOnlySpan<byte> bytes, TextWriter errWriter)
        {
            try
            {
                if (path == "-")
                {
                    using Stream stdOut = Console.OpenStandardOutput();

                    stdOut.Write(bytes);
                    stdOut.Flush();

                    return true;
                }

                string? dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

                fs.Write(bytes);

                return true;
            }
            catch (Exception ex)
            {
                errWriter.WriteLine($"Failed to write file \"{path}\": {ex.Message}");

                return false;
            }
        }

        internal static void SplitOptionToken(string token, out string key, out string? inlineValue)
        {
            int eqIndex = token.IndexOf('=');

            if (eqIndex > 0)
            {
                key         = token.Substring(0, eqIndex);
                inlineValue = token.Substring(eqIndex + 1);

                return;
            }

            key         = token;
            inlineValue = null;
        }

        internal static bool TryConsumeOptionValue(
            string[] args,
            ref int index,
            string? inlineVal,
            out string value,
            TextWriter errWriter,
            string optionName)
        {
            if (inlineVal is not null)
            {
                value = inlineVal;

                return true;
            }

            if (index + 1 < args.Length)
            {
                index++;
                value = args[index];

                return true;
            }

            value = string.Empty;
            errWriter.WriteLine($"Option {optionName} requires an argument.");

            return false;
        }

        internal static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value);
        }

        internal static bool TryParseOverride(string text, out Override value)
        {
            if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = Override.On;

                return true;
            }

            if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                value = Override.Off;

                return true;
            }

            if (text == "-1" || string.Equals(text, "default", StringComparison.OrdinalIgnoreCase))
            {
                value = Override.Default;

                return true;
            }

            value = Override.Default;

            return false;
        }

        internal static bool TryParseAndAddHint(string kv, ColorHints hints)
        {
            int eq = kv.IndexOf('=');

            if (eq <= 0)
            {
                return false;
            }

            hints.Add(kv.Substring(0, eq), kv.Substring(eq + 1));

            return true;
        }
    }

    /// <summary>
    /// Command-line arguments for the <c>djxl</c> decoder tool.
    /// Ported from <c>DecompressArgs</c> in <c>tools/djxl_main.cc</c>.
    /// </summary>
    public sealed class DecompressArgs
    {
        /// <summary>Gets or sets the input JXL file path.</summary>
        public string? FileIn { get; set; }

        /// <summary>Gets or sets the output image file path.</summary>
        public string? FileOut { get; set; }

        /// <summary>Gets or sets the number of decoding repetitions for benchmarking.</summary>
        public nuint NumReps { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether output file writing is disabled.</summary>
        public bool DisableOutput { get; set; }

        /// <summary>Gets or sets the number of worker threads (-1 for default).</summary>
        public int NumThreads { get; set; } = -1;

        /// <summary>Gets or sets the requested output bit depth (0 for original).</summary>
        public int BitsPerSample { get; set; }

        /// <summary>Gets or sets the tone-mapping target luminance in nits (0 for disabled).</summary>
        public double DisplayNits { get; set; }

        /// <summary>Gets or sets the desired output color space description.</summary>
        public string ColorSpace { get; set; } = string.Empty;

        /// <summary>Gets or sets the alpha blend background specification.</summary>
        public string AlphaBlend { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether associated alpha should be unpremultiplied.</summary>
        public bool UnpremultiplyAlpha { get; set; }

        /// <summary>Gets or sets the maximum number of passes to decode.</summary>
        public uint MaxPasses { get; set; } = uint.MaxValue;

        /// <summary>Gets or sets the maximum downsampling factor to decode.</summary>
        public nuint MaxDownsampling { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether partial/truncated files are allowed.</summary>
        public bool AllowPartialFiles { get; set; }

        /// <summary>Gets or sets a value indicating whether more progressive steps are allowed.</summary>
        public bool AllowMoreProgressiveSteps { get; set; }

        /// <summary>Gets or sets a value indicating whether JPEG output should decode to pixels instead of lossless reconstruction.</summary>
        public bool PixelsToJpeg { get; set; }

        /// <summary>Gets or sets the JPEG output quality when encoding from pixels.</summary>
        public nuint JpegQuality { get; set; } = 95;

        /// <summary>Gets or sets a value indicating whether sjpeg is used for JPEG encoding.</summary>
        public bool UseSjpeg { get; set; }

        /// <summary>Gets or sets a value indicating whether spot colors are rendered.</summary>
        public bool RenderSpotcolors { get; set; } = true;

        /// <summary>Gets or sets the optional preview output file path.</summary>
        public string PreviewOut { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional ICC profile output file path.</summary>
        public string IccOut { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional original ICC profile output file path.</summary>
        public string OrigIccOut { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional metadata output file path.</summary>
        public string MetadataOut { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether total decoded bytes should be printed.</summary>
        public bool PrintReadBytes { get; set; }

        /// <summary>Gets or sets a value indicating whether quiet mode is enabled.</summary>
        public bool Quiet { get; set; }

        /// <summary>Gets or sets a value indicating whether all animation frames are output.</summary>
        public bool OutputFrames { get; set; }

        /// <summary>Gets or sets a value indicating whether extra channels are output.</summary>
        public bool OutputExtraChannels { get; set; }

        /// <summary>Gets or sets a value indicating whether frames are coalesced.</summary>
        public bool Coalescing { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether the image callback API is used.</summary>
        public bool UseImageCallback { get; set; } = true;

        /// <summary>Gets or sets the profile printing override.</summary>
        public Override PrintProfile { get; set; } = Override.Default;

        /// <summary>Gets or sets a value indicating whether version was requested.</summary>
        public bool Version { get; set; }

        /// <summary>Gets or sets a value indicating whether help was requested.</summary>
        public bool Help { get; set; }
    }

    /// <summary>
    /// JPEG XL command-line decoder (<c>djxl</c>).
    /// Ported from <c>tools/djxl_main.cc</c>.
    /// </summary>
    public static class DjxlTool
    {
        /// <summary>
        /// Runs the <c>djxl</c> command-line tool with the specified arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code (<c>0</c> on success, <c>1</c> on error).</returns>
        public static int Run(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Runs the <c>djxl</c> command-line tool with custom output writers.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Standard output writer.</param>
        /// <param name="stderr">Standard error writer.</param>
        /// <returns>Process exit code (<c>0</c> on success, <c>1</c> on error).</returns>
        public static int Run(string[] args, TextWriter? stdout, TextWriter? stderr)
        {
            TextWriter outWriter = stdout ?? Console.Out;
            TextWriter errWriter = stderr ?? Console.Error;
            DecompressArgs dargs = new DecompressArgs();

            if (!ParseDecompressArgs(args ?? Array.Empty<string>(), dargs, errWriter))
            {
                PrintDjxlHelp(outWriter);

                return 1;
            }

            if (dargs.Version)
            {
                outWriter.WriteLine($"djxl {CjxlTool.FormatConfigVersion(JxlDecoder.Version())}");

                return 0;
            }

            if (dargs.Help)
            {
                PrintDjxlHelp(outWriter);

                return 0;
            }

            if (string.IsNullOrEmpty(dargs.FileIn))
            {
                errWriter.WriteLine("Missing input filename.");

                return 1;
            }

            return ExecuteDecompression(dargs, errWriter);
        }

        private static int ExecuteDecompression(DecompressArgs dargs, TextWriter errWriter)
        {
            if (!CjxlTool.TryReadInputFile(dargs.FileIn!, out byte[] compressed, errWriter))
            {
                return 1;
            }

            if (string.IsNullOrEmpty(dargs.FileOut) && !dargs.DisableOutput)
            {
                errWriter.WriteLine("No output file specified, testing decompression only.");
                dargs.DisableOutput = true;
            }

            string extension                 = !string.IsNullOrEmpty(dargs.FileOut) ? Path.GetExtension(dargs.FileOut!) : ".ppm";
            JxlSharp.Extras.Encoder? encoder = (!string.IsNullOrEmpty(dargs.FileOut) && !dargs.DisableOutput)
                ? ExtrasEncodeFacade.FromExtension(extension)
                : null;
            using ThreadPoolInternal pool    = new ThreadPoolInternal(dargs.NumThreads);
            JXLDecompressParams dparams      = BuildDecompressParams(dargs, encoder);
            dparams.Runner                   = pool;
            PackedPixelFile ppf              = new PackedPixelFile();
            List<byte>? jpegBytes            = ShouldReconstructJpeg(dargs, extension) ? new List<byte>() : null;
            nuint totalDecodedBytes          = 0;
            nuint reps                       = dargs.NumReps > 0 ? dargs.NumReps : 1;
            SpeedStats stats                 = new SpeedStats();

            for (nuint i = 0; i < reps; i++)
            {
                double t0 = ExtrasTime.Now();
                bool ok   = CodecJxl.DecodeImageJXL(compressed, dparams, out totalDecodedBytes, ppf, jpegBytes);
                double t1 = ExtrasTime.Now();

                if (!ok)
                {
                    errWriter.WriteLine("Failed to decode JXL image.");

                    return 1;
                }

                stats.NotifyElapsed(t1 - t0);
                stats.SetImageSize(ppf.Info.XSize, ppf.Info.YSize);

                if (jpegBytes is not null && jpegBytes.Count > 0)
                {
                    stats.SetFileSize((nuint)jpegBytes.Count);
                }

                if (!dargs.Quiet && i + 1 == reps)
                {
                    PrintDecodeSummary(ppf, jpegBytes, t1 - t0, errWriter);
                    stats.Print(pool.NumWorkerThreads, errWriter);
                }
            }

            if (dargs.PrintReadBytes)
            {
                errWriter.WriteLine($"Decoded bytes: {totalDecodedBytes}");
            }

            return WriteDecompressedOutputs(dargs, ppf, jpegBytes, encoder, extension, errWriter);
        }

        private static bool ShouldReconstructJpeg(DecompressArgs dargs, string extension)
        {
            if (dargs.PixelsToJpeg)
            {
                return false;
            }

            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static JXLDecompressParams BuildDecompressParams(
            DecompressArgs dargs,
            JxlSharp.Extras.Encoder? encoder)
        {
            JXLDecompressParams dparams = new JXLDecompressParams
            {
                ColorSpace         = dargs.ColorSpace,
                DisplayNits        = (float)dargs.DisplayNits,
                RenderSpotcolors   = dargs.RenderSpotcolors,
                Coalescing         = dargs.Coalescing,
                AllowPartialInput  = dargs.AllowPartialFiles,
                MaxPasses          = dargs.MaxPasses,
                MaxDownsampling    = dargs.MaxDownsampling,
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

            PopulateAcceptedFormats(dparams, encoder);

            return dparams;
        }

        private static void PopulateAcceptedFormats(
            JXLDecompressParams dparams,
            JxlSharp.Extras.Encoder? encoder)
        {
            if (encoder is not null)
            {
                foreach (JxlPixelFormat fmt in encoder.AcceptedFormats())
                {
                    dparams.AcceptedFormats.Add(fmt);
                }

                return;
            }

            uint[] channels         = { 1u, 2u, 3u, 4u };
            JxlEndianness[] endians = { JxlEndianness.LittleEndian, JxlEndianness.BigEndian };

            foreach (uint c in channels)
            {
                foreach (JxlEndianness end in endians)
                {
                    dparams.AcceptedFormats.Add(new JxlPixelFormat
                    {
                        NumChannels = c,
                        DataType    = JxlDataType.Float,
                        Endianness  = end,
                        Align       = 0,
                    });
                }
            }
        }

        private static int WriteDecompressedOutputs(
            DecompressArgs dargs,
            PackedPixelFile ppf,
            List<byte>? jpegBytes,
            JxlSharp.Extras.Encoder? encoder,
            string extension,
            TextWriter errWriter)
        {
            if (dargs.DisableOutput || string.IsNullOrEmpty(dargs.FileOut))
            {
                return 0;
            }

            if (jpegBytes is not null && jpegBytes.Count > 0)
            {
                return CjxlTool.TryWriteOutputFile(dargs.FileOut!, jpegBytes.ToArray(), errWriter) ? 0 : 1;
            }

            if (!string.IsNullOrEmpty(dargs.AlphaBlend))
            {
                if (!ApplyAlphaBlendOption(ppf, dargs.AlphaBlend, errWriter))
                {
                    return 1;
                }
            }

            if (encoder is null)
            {
                errWriter.WriteLine($"Cannot encode to the file format of \"{dargs.FileOut}\".");

                return 1;
            }

            return EncodeAndWriteFrames(dargs, ppf, encoder, extension, errWriter);
        }

        private static bool ApplyAlphaBlendOption(
            PackedPixelFile ppf,
            string blendSpec,
            TextWriter errWriter)
        {
            Span<float> bg = stackalloc float[3];

            if (string.Equals(blendSpec, "black", StringComparison.OrdinalIgnoreCase))
            {
                bg[0] = 0.0f;
                bg[1] = 0.0f;
                bg[2] = 0.0f;
            }
            else if (string.Equals(blendSpec, "white", StringComparison.OrdinalIgnoreCase))
            {
                bg[0] = 1.0f;
                bg[1] = 1.0f;
                bg[2] = 1.0f;
            }
            else
            {
                errWriter.WriteLine($"Unsupported --alpha_blend spec: {blendSpec}");

                return false;
            }

            return ExtrasAlphaBlend.AlphaBlend(ppf, bg);
        }

        private static int EncodeAndWriteFrames(
            DecompressArgs dargs,
            PackedPixelFile ppf,
            JxlSharp.Extras.Encoder encoder,
            string extension,
            TextWriter errWriter)
        {
            EncodedImage encoded = new EncodedImage();

            encoder.SetOption("q", dargs.JpegQuality.ToString(CultureInfo.InvariantCulture));

            Status encStatus = encoder.Encode(ppf, encoded);

            if (!encStatus || encoded.Bitstreams.Count == 0)
            {
                errWriter.WriteLine($"Encode failed: {encStatus}");

                return 1;
            }

            int nframes = (dargs.OutputFrames || !dargs.Coalescing) ? encoded.Bitstreams.Count : 1;

            for (int j = 0; j < nframes; j++)
            {
                string outPath = FormatFrameFilename(dargs.FileOut!, extension, j, nframes);

                if (!CjxlTool.TryWriteOutputFile(outPath, encoded.Bitstreams[j], errWriter))
                {
                    return 1;
                }
            }

            if (!WriteOptionalBlob(dargs.PreviewOut, encoded.PreviewBitstream, errWriter) ||
                !WriteOptionalBlob(dargs.IccOut, ppf.Icc, errWriter) ||
                !WriteOptionalBlob(dargs.OrigIccOut, ppf.OrigIcc, errWriter) ||
                !WriteOptionalBlob(dargs.MetadataOut, encoded.Metadata, errWriter))
            {
                return 1;
            }

            return 0;
        }

        private static string FormatFrameFilename(string baseName, string extension, int frameIdx, int numFrames)
        {
            if (numFrames <= 1)
            {
                return baseName;
            }

            string stem = baseName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? baseName.Substring(0, baseName.Length - extension.Length)
                : baseName;

            return string.Format(CultureInfo.InvariantCulture, "{0}-{1:D2}{2}", stem, frameIdx, extension);
        }

        private static bool WriteOptionalBlob(string path, byte[] data, TextWriter errWriter)
        {
            if (string.IsNullOrEmpty(path) || data.Length == 0)
            {
                return true;
            }

            return CjxlTool.TryWriteOutputFile(path, data, errWriter);
        }

        private static void PrintDecodeSummary(
            PackedPixelFile ppf,
            List<byte>? jpegBytes,
            double elapsedSeconds,
            TextWriter errWriter)
        {
            if (jpegBytes is not null && jpegBytes.Count > 0)
            {
                errWriter.WriteLine($"Reconstructed to JPEG ({jpegBytes.Count} bytes, {elapsedSeconds * 1000.0:F2} ms).");

                return;
            }

            errWriter.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Decoded {0}x{1} image ({2:F2} ms).",
                    ppf.XSize,
                    ppf.YSize,
                    elapsedSeconds * 1000.0));
        }

        private static bool ParseDecompressArgs(string[] args, DecompressArgs dargs, TextWriter errWriter)
        {
            int positionalIndex = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal) || arg == "-")
                {
                    if (!AssignDjxlPositional(arg, ref positionalIndex, dargs, errWriter))
                    {
                        return false;
                    }

                    continue;
                }

                if (!ParseSingleDjxlOption(args, ref i, dargs, errWriter))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AssignDjxlPositional(
            string arg,
            ref int positionalIndex,
            DecompressArgs dargs,
            TextWriter errWriter)
        {
            if (positionalIndex == 0)
            {
                dargs.FileIn = arg;
                positionalIndex++;

                return true;
            }

            if (positionalIndex == 1)
            {
                dargs.FileOut = arg;
                positionalIndex++;

                return true;
            }

            errWriter.WriteLine($"Unexpected positional argument: {arg}");

            return false;
        }

        private static bool ParseSingleDjxlOption(
            string[] args,
            ref int index,
            DecompressArgs dargs,
            TextWriter errWriter)
        {
            CjxlTool.SplitOptionToken(args[index], out string key, out string? inlineVal);

            if (TryHandleDjxlFlagOption(key, dargs))
            {
                return true;
            }

            if (!CjxlTool.TryConsumeOptionValue(args, ref index, inlineVal, out string val, errWriter, key))
            {
                return false;
            }

            return ApplyDjxlValueOption(key, val, dargs, errWriter);
        }

        private static bool TryHandleDjxlFlagOption(string key, DecompressArgs dargs)
        {
            if (key == "-V" || key == "--version")
            {
                dargs.Version = true;

                return true;
            }

            if (key == "-h" || key == "--help")
            {
                dargs.Help = true;

                return true;
            }

            if (key == "-q" || key == "--quiet")
            {
                dargs.Quiet = true;

                return true;
            }

            if (key == "-j" || key == "--pixels_to_jpeg")
            {
                dargs.PixelsToJpeg = true;

                return true;
            }

            if (key == "--allow_partial_files")
            {
                dargs.AllowPartialFiles = true;

                return true;
            }

            return TryHandleDjxlSecondaryFlagOption(key, dargs);
        }

        private static bool TryHandleDjxlSecondaryFlagOption(string key, DecompressArgs dargs)
        {
            if (key == "--allow_more_progressive_steps")
            {
                dargs.AllowMoreProgressiveSteps = true;

                return true;
            }

            if (key == "--disable_output")
            {
                dargs.DisableOutput = true;

                return true;
            }

            if (key == "--print_read_bytes")
            {
                dargs.PrintReadBytes = true;

                return true;
            }

            if (key == "--unpremultiply_alpha")
            {
                dargs.UnpremultiplyAlpha = true;

                return true;
            }

            if (key == "--output_frames")
            {
                dargs.OutputFrames = true;

                return true;
            }

            if (key == "--output_extra_channels")
            {
                dargs.OutputExtraChannels = true;

                return true;
            }

            if (key == "--use_sjpeg")
            {
                dargs.UseSjpeg = true;

                return true;
            }

            return false;
        }

        private static bool ApplyDjxlValueOption(
            string key,
            string val,
            DecompressArgs dargs,
            TextWriter errWriter)
        {
            if (TryApplyDjxlStringOption(key, val, dargs))
            {
                return true;
            }

            if (TryApplyDjxlNumericOption(key, val, dargs))
            {
                return true;
            }

            errWriter.WriteLine($"Unknown or invalid option: {key} {val}");

            return false;
        }

        private static bool TryApplyDjxlStringOption(string key, string val, DecompressArgs dargs)
        {
            if (key == "--color_space")
            {
                dargs.ColorSpace = val;

                return true;
            }

            if (key == "--alpha_blend")
            {
                dargs.AlphaBlend = val;

                return true;
            }

            if (key == "--preview_out")
            {
                dargs.PreviewOut = val;

                return true;
            }

            if (key == "--icc_out")
            {
                dargs.IccOut = val;

                return true;
            }

            if (key == "--orig_icc_out")
            {
                dargs.OrigIccOut = val;

                return true;
            }

            if (key == "--metadata_out")
            {
                dargs.MetadataOut = val;

                return true;
            }

            return false;
        }

        private static bool TryApplyDjxlNumericOption(string key, string val, DecompressArgs dargs)
        {
            if (key == "--display_nits" && CjxlTool.TryParseFloat(val, out float nits))
            {
                dargs.DisplayNits = nits;

                return true;
            }

            if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lval))
            {
                return false;
            }

            if (key == "--bits_per_sample")
            {
                dargs.BitsPerSample = (int)lval;

                return true;
            }

            if (key == "--num_threads")
            {
                dargs.NumThreads = (int)lval;

                return true;
            }

            if (key == "--num_reps")
            {
                dargs.NumReps = lval > 0 ? (nuint)lval : 1;

                return true;
            }

            return TryApplyDjxlExtraNumericOption(key, lval, dargs);
        }

        private static bool TryApplyDjxlExtraNumericOption(string key, long lval, DecompressArgs dargs)
        {
            if (key == "--downsampling")
            {
                dargs.MaxDownsampling = lval > 0 ? (nuint)lval : 1;

                return true;
            }

            if (key == "--num_passes")
            {
                dargs.MaxPasses = lval >= 0 ? (uint)lval : uint.MaxValue;

                return true;
            }

            if (key == "--jpeg_quality")
            {
                dargs.JpegQuality = lval >= 0 ? (nuint)lval : 95;

                return true;
            }

            if (key == "--render_spotcolors")
            {
                dargs.RenderSpotcolors = lval != 0;

                return true;
            }

            if (key == "--coalescing")
            {
                dargs.Coalescing = lval != 0;

                return true;
            }

            return false;
        }

        private static void PrintDjxlHelp(TextWriter writer)
        {
            writer.WriteLine("Usage: djxl INPUT [OUTPUT] [OPTIONS...]");
            writer.WriteLine("  INPUT                      the compressed JXL input file");
            writer.WriteLine("  OUTPUT                     the output can be " + ExtrasEncodeFacade.ListOfEncodeCodecs());
            writer.WriteLine("  -j, --pixels_to_jpeg       By default, JPEG XL reconstructs lossless JPEG if possible;");
            writer.WriteLine("                             this flag decodes to pixels instead");
            writer.WriteLine("  --jpeg_quality=N           JPEG output quality (default = 95)");
            writer.WriteLine("  --bits_per_sample=N        Output bit depth (0 = original)");
            writer.WriteLine("  --color_space=DESC         Desired output color space");
            writer.WriteLine("  --downsampling=N           Maximum downsampling factor (1, 2, 4, 8)");
            writer.WriteLine("  --allow_partial_files      Allow decoding of truncated files");
            writer.WriteLine("  -q, --quiet                Be more silent");
            writer.WriteLine("  -V, --version              Print decoder library version and exit");
            writer.WriteLine("  -h, --help                 Prints this help message");
        }
    }

    /// <summary>
    /// JPEG XL image metadata inspection tool (<c>jxlinfo</c>).
    /// Ported from <c>tools/jxlinfo.cc</c>.
    /// </summary>
    public static class JxlInfoTool
    {
        private static readonly string[] CsTypeNames = { "RGB", "Grayscale", "XYB", "Unknown" };
        private static readonly string[] WpNames     = { "", "D65", "Custom", "", "", "", "", "", "", "", "E", "P3" };
        private static readonly string[] PrNames     = { "", "sRGB", "Custom", "", "", "", "", "", "", "Rec.2100", "", "P3" };
        private static readonly string[] TfNames     =
        {
            "", "709", "Unknown", "", "", "", "", "", "Linear", "", "", "", "", "sRGB", "", "", "PQ", "DCI", "HLG",
        };
        private static readonly string[] RiNames          = { "Perceptual", "Relative", "Saturation", "Absolute" };
        private static readonly string[] OrientationNames =
        {
            "Normal",
            "Flipped horizontally",
            "Upside down",
            "Flipped vertically",
            "Transposed",
            "Rotated 90 degrees clockwise",
            "Anti-transposed",
            "Rotated 90 degrees counter-clockwise",
        };
        private static readonly string[] BlendNames       = { "Replace", "Add", "Blend", "MulAdd", "Mul" };

        /// <summary>
        /// Runs the <c>jxlinfo</c> tool with the specified command-line arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Optional standard output writer; defaults to <see cref="Console.Out"/>.</param>
        /// <param name="stderr">Optional standard error writer; defaults to <see cref="Console.Error"/>.</param>
        /// <returns>Process exit code (<c>0</c> on success, non-zero on failure).</returns>
        public static int Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
        {
            TextWriter outWriter = stdout ?? Console.Out;
            TextWriter errWriter = stderr ?? Console.Error;
            List<string> files   = new List<string>();
            bool verbose         = false;
            bool help            = false;

            ParseJxlInfoArgs(args ?? Array.Empty<string>(), files, ref verbose, ref help);

            if (help || files.Count == 0)
            {
                PrintJxlInfoUsage(outWriter);

                return help ? 0 : 1;
            }

            int status = 0;

            foreach (string file in files)
            {
                if (files.Count > 1)
                {
                    outWriter.WriteLine($"{file}:");
                }

                status |= InspectSingleFile(file, verbose, outWriter, errWriter);
            }

            return status;
        }

        private static void ParseJxlInfoArgs(
            string[] args,
            List<string> files,
            ref bool verbose,
            ref bool help)
        {
            foreach (string arg in args)
            {
                if (arg == "-v" || arg == "--verbose")
                {
                    verbose = true;
                }
                else if (arg == "-h" || arg == "--help")
                {
                    help = true;
                }
                else if (!string.IsNullOrEmpty(arg))
                {
                    files.Add(arg);
                }
            }
        }

        private static void PrintJxlInfoUsage(TextWriter outWriter)
        {
            outWriter.WriteLine($"jxlinfo (libjxl {CjxlTool.FormatConfigVersion(JxlDecoder.Version())})");
            outWriter.WriteLine("Usage: jxlinfo [-v] [-h] INPUT.jxl...");
            outWriter.WriteLine("  -v, --verbose   Print more verbose frame and box information");
            outWriter.WriteLine("  -h, --help      Print this help message");
        }

        private static int InspectSingleFile(
            string path,
            bool verbose,
            TextWriter outWriter,
            TextWriter errWriter)
        {
            if (!CjxlTool.TryReadInputFile(path, out byte[] data, errWriter))
            {
                return 1;
            }

            JxlSignature sig = JxlDecoder.CheckSignature(data);

            if (sig != JxlSignature.Codestream && sig != JxlSignature.Container)
            {
                errWriter.WriteLine($"Not a valid JPEG XL image: {path}");

                return 1;
            }

            using JxlDecoder dec = new JxlDecoder();

            dec.SetKeepOrientation(true);
            dec.SetCoalescing(false);

            JxlDecoderStatus subStatus = dec.SubscribeEvents(
                JxlDecoderStatus.BasicInfo |
                JxlDecoderStatus.ColorEncoding |
                JxlDecoderStatus.Frame |
                JxlDecoderStatus.Box);

            if (subStatus != JxlDecoderStatus.Success)
            {
                errWriter.WriteLine("JxlDecoderSubscribeEvents failed");

                return 1;
            }

            dec.SetInput(data);
            dec.CloseInput();

            int result = RunDecoderInfoLoop(dec, verbose, outWriter, errWriter);

            if (sig == JxlSignature.Container)
            {
                InspectContainerBoxes(data, verbose, outWriter);
            }

            return result;
        }

        private static int RunDecoderInfoLoop(
            JxlDecoder dec,
            bool verbose,
            TextWriter outWriter,
            TextWriter errWriter)
        {
            JxlBasicInfo info   = default;
            float totalDuration = 0.0f;
            int frameCount      = 0;

            while (true)
            {
                JxlDecoderStatus status = dec.ProcessInput();

                if (status == JxlDecoderStatus.Success)
                {
                    break;
                }

                if (status == JxlDecoderStatus.Error || status == JxlDecoderStatus.NeedMoreInput)
                {
                    errWriter.WriteLine("Decoder error while reading JXL metadata.");

                    return 1;
                }

                HandleInfoEvent(dec, status, ref info, ref totalDuration, ref frameCount, verbose, outWriter);
            }

            if (info.HaveAnimation && info.Animation.TpsNumerator > 0)
            {
                outWriter.Write(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Animation length: {0:F3} seconds",
                        totalDuration));

                if (info.Animation.NumLoops > 1)
                {
                    outWriter.Write(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            " ({0:F3} seconds total in {1} loops)",
                            totalDuration * info.Animation.NumLoops,
                            info.Animation.NumLoops));
                }

                outWriter.WriteLine();
            }

            return 0;
        }

        private static void HandleInfoEvent(
            JxlDecoder dec,
            JxlDecoderStatus status,
            ref JxlBasicInfo info,
            ref float totalDuration,
            ref int frameCount,
            bool verbose,
            TextWriter outWriter)
        {
            if (status == JxlDecoderStatus.BasicInfo && dec.GetBasicInfo(out info) == JxlDecoderStatus.Success)
            {
                PrintBasicInfo(dec, in info, verbose, outWriter);

                return;
            }

            if (status == JxlDecoderStatus.ColorEncoding)
            {
                PrintColorEncoding(dec, outWriter);

                return;
            }

            if (status == JxlDecoderStatus.Frame && dec.GetFrameHeader(out JxlFrameHeader header) == JxlDecoderStatus.Success)
            {
                frameCount++;
                PrintFrameHeader(dec, in info, in header, ref totalDuration, verbose, outWriter);
            }
        }

        private static void PrintBasicInfo(
            JxlDecoder dec,
            in JxlBasicInfo info,
            bool verbose,
            TextWriter outWriter)
        {
            string kind     = info.HaveContainer ? "JPEG XL container" : "JPEG XL codestream";
            string lossy    = info.UsesOriginalProfile ? "(possibly) lossless" : "lossy";
            string colorStr = info.NumColorChannels == 1 ? "grayscale" : "RGB";
            string alphaStr = info.AlphaBits > 0 ? "+Alpha" : string.Empty;

            outWriter.WriteLine(
                $"{kind}, {info.XSize}x{info.YSize}, {lossy}, {info.BitsPerSample}-bit {colorStr}{alphaStr}");

            if (info.ExponentBitsPerSample > 0)
            {
                outWriter.WriteLine($"float with {info.ExponentBitsPerSample} exponent bits");
            }

            if (info.Orientation > JxlOrientation.Identity && (int)info.Orientation <= OrientationNames.Length)
            {
                outWriter.WriteLine($"Orientation: {(int)info.Orientation} ({OrientationNames[(int)info.Orientation - 1]})");
            }

            if (verbose && info.IntensityTarget > 0)
            {
                outWriter.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Intensity target: {0:F2} nits",
                        info.IntensityTarget));
            }

            PrintExtraChannels(dec, in info, outWriter);
        }

        private static void PrintExtraChannels(
            JxlDecoder dec,
            in JxlBasicInfo info,
            TextWriter outWriter)
        {
            for (uint i = 0; i < info.NumExtraChannels; i++)
            {
                if (dec.GetExtraChannelInfo(i, out JxlExtraChannelInfo ec) != JxlDecoderStatus.Success)
                {
                    continue;
                }

                string ecName = ReadExtraChannelName(dec, i, ec.NameLength);
                string label  = string.IsNullOrEmpty(ecName) ? ec.Type.ToString() : $"{ec.Type} (\"{ecName}\")";

                outWriter.WriteLine($"Extra channel {i}: {ec.BitsPerSample}-bit {label}");
            }
        }

        private static string ReadExtraChannelName(JxlDecoder dec, uint index, uint nameLength)
        {
            if (nameLength == 0)
            {
                return string.Empty;
            }

            byte[] buf = new byte[nameLength + 1];

            if (dec.GetExtraChannelName(index, buf) != JxlDecoderStatus.Success)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buf, 0, (int)nameLength);
        }

        private static string ReadFrameName(JxlDecoder dec, uint nameLength)
        {
            if (nameLength == 0)
            {
                return string.Empty;
            }

            byte[] buf = new byte[nameLength + 1];

            if (dec.GetFrameName(buf) != JxlDecoderStatus.Success)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buf, 0, (int)nameLength);
        }

        private static void PrintColorEncoding(JxlDecoder dec, TextWriter outWriter)
        {
            JxlDecoderStatus ceStatus = dec.GetColorAsEncodedProfile(
                JxlColorProfileTarget.Original,
                out JxlColorEncoding ce);

            if (ceStatus == JxlDecoderStatus.Success)
            {
                string cs = (uint)ce.ColorSpace < (uint)CsTypeNames.Length ? CsTypeNames[(int)ce.ColorSpace] : ce.ColorSpace.ToString();
                string wp = (uint)ce.WhitePoint < (uint)WpNames.Length ? WpNames[(int)ce.WhitePoint] : ce.WhitePoint.ToString();
                string pr = (uint)ce.Primaries < (uint)PrNames.Length ? PrNames[(int)ce.Primaries] : ce.Primaries.ToString();
                string tf = (uint)ce.TransferFunction < (uint)TfNames.Length ? TfNames[(int)ce.TransferFunction] : ce.TransferFunction.ToString();
                string ri = (uint)ce.RenderingIntent < (uint)RiNames.Length ? RiNames[(int)ce.RenderingIntent] : ce.RenderingIntent.ToString();

                outWriter.WriteLine(
                    $"Color space: {cs}, white point: {wp}, primaries: {pr}, transfer function: {tf}, rendering intent: {ri}");

                return;
            }

            if (dec.GetICCProfileSize(JxlColorProfileTarget.Original, out nuint iccSize) == JxlDecoderStatus.Success && iccSize > 0)
            {
                outWriter.WriteLine($"{iccSize}-byte ICC profile");
            }
        }

        private static void PrintFrameHeader(
            JxlDecoder dec,
            in JxlBasicInfo info,
            in JxlFrameHeader header,
            ref float totalDuration,
            bool verbose,
            TextWriter outWriter)
        {
            float ms         = 0.0f;
            string frameName = ReadFrameName(dec, header.NameLength);

            if (info.HaveAnimation && info.Animation.TpsNumerator > 0)
            {
                float secs = header.Duration * (float)info.Animation.TpsDenominator / info.Animation.TpsNumerator;

                totalDuration += secs;
                ms             = secs * 1000.0f;
            }

            if (!verbose && string.IsNullOrEmpty(frameName) && !info.HaveAnimation)
            {
                return;
            }

            uint bm     = (uint)header.LayerInfo.BlendInfo.BlendMode;
            string bstr = bm < BlendNames.Length ? BlendNames[bm] : bm.ToString(CultureInfo.InvariantCulture);

            outWriter.Write(
                $"layer: {header.LayerInfo.XSize}x{header.LayerInfo.YSize} at ({header.LayerInfo.CropX0},{header.LayerInfo.CropY0}), {bstr}");

            if (info.HaveAnimation)
            {
                outWriter.Write(string.Format(CultureInfo.InvariantCulture, ", duration: {0:F2} ms", ms));
            }

            if (!string.IsNullOrEmpty(frameName))
            {
                outWriter.Write($", name: \"{frameName}\"");
            }

            outWriter.WriteLine();
        }

        private static void InspectContainerBoxes(
            ReadOnlySpan<byte> data,
            bool verbose,
            TextWriter outWriter)
        {
            int pos = 0;

            while (TryReadNextBox(data, ref pos, out string boxType, out ReadOnlySpan<byte> payload))
            {
                PrintContainerBoxInfo(boxType, payload, verbose, outWriter);
            }
        }

        private static void PrintContainerBoxInfo(
            string boxType,
            ReadOnlySpan<byte> payload,
            bool verbose,
            TextWriter outWriter)
        {
            if (boxType == "jbrd")
            {
                outWriter.WriteLine($"JPEG bitstream reconstruction data available ({payload.Length} bytes)");

                return;
            }

            if (boxType == "Exif" || boxType == "xml " || boxType == "jumb")
            {
                outWriter.WriteLine($"Uncompressed {boxType} metadata: {payload.Length} bytes");

                return;
            }

            if (boxType == "brob" && payload.Length >= 4)
            {
                string inner = Encoding.ASCII.GetString(payload.Slice(0, 4));

                outWriter.WriteLine($"Brotli-compressed {inner} metadata: {payload.Length - 4} compressed bytes");

                return;
            }

            if (boxType == "jhgm")
            {
                PrintGainMapBox(payload, verbose, outWriter);
            }
        }

        private static void PrintGainMapBox(
            ReadOnlySpan<byte> payload,
            bool verbose,
            TextWriter outWriter)
        {
            if (!GainMapCodec.JxlGainMapReadBundle(out JxlGainMapBundle bundle, payload, out _))
            {
                outWriter.WriteLine($"Gain map box (jhgm): {payload.Length} bytes");

                return;
            }

            outWriter.WriteLine(
                $"Gain map (jhgm): version {bundle.JhgmVersion}, {bundle.GainMap.Length} gain map bytes");

            if (verbose && bundle.AltIcc.Length > 0 &&
                CompressedIcc.JxlICCProfileDecode(null, bundle.AltIcc, out byte[] rawIcc))
            {
                outWriter.WriteLine($"Gain map alternate ICC profile: {rawIcc.Length} bytes");
            }
        }

        internal static bool TryReadNextBox(
            ReadOnlySpan<byte> data,
            ref int pos,
            out string boxType,
            out ReadOnlySpan<byte> payload)
        {
            boxType = string.Empty;
            payload = ReadOnlySpan<byte>.Empty;

            if (pos + 8 > data.Length)
            {
                return false;
            }

            uint size32   = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
            boxType       = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            int headerLen = 8;
            ulong boxSize = size32;

            if (size32 == 1)
            {
                if (pos + 16 > data.Length)
                {
                    return false;
                }

                boxSize   = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(pos + 8, 8));
                headerLen = 16;
            }
            else if (size32 == 0)
            {
                boxSize = (ulong)(data.Length - pos);
            }

            if (boxSize < (ulong)headerLen || pos + (long)boxSize > data.Length)
            {
                return false;
            }

            int payloadLen = (int)boxSize - headerLen;

            payload = data.Slice(pos + headerLen, payloadLen);
            pos    += (int)boxSize;

            return true;
        }
    }

    /// <summary>
    /// Command-line arguments for the <c>jxltran</c> lossless container tool.
    /// Ported from <c>tools/jxltran.cc</c>.
    /// </summary>
    public sealed class JxlTranArgs
    {
        /// <summary>Gets or sets the input JXL file path.</summary>
        public string? FileIn { get; set; }

        /// <summary>Gets or sets the output JXL file path.</summary>
        public string? FileOut { get; set; }

        /// <summary>Gets or sets a value indicating whether to pack a raw codestream into a container.</summary>
        public bool Pack { get; set; }

        /// <summary>Gets or sets a value indicating whether to extract a raw codestream from a container.</summary>
        public bool Extract { get; set; }

        /// <summary>Gets the list of <c>type=filename</c> box specifications to add.</summary>
        public List<string> AddBoxSpecs { get; } = new List<string>();

        /// <summary>Gets the list of 4-character box types to remove.</summary>
        public List<string> RemoveBoxTypes { get; } = new List<string>();

        /// <summary>Gets or sets a value indicating whether quiet mode is enabled.</summary>
        public bool Quiet { get; set; }

        /// <summary>Gets or sets a value indicating whether version was requested.</summary>
        public bool Version { get; set; }

        /// <summary>Gets or sets a value indicating whether help was requested.</summary>
        public bool Help { get; set; }
    }

    /// <summary>
    /// JPEG XL lossless container and box manipulation tool (<c>jxltran</c>).
    /// Ported from <c>tools/jxltran.cc</c>.
    /// </summary>
    public static class JxlTranTool
    {
        private static readonly byte[] ContainerSignatureAndFtyp =
        {
            0x00, 0x00, 0x00, 0x0c, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0d, 0x0a, 0x87, 0x0a,
            0x00, 0x00, 0x00, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'j', (byte)'x', (byte)'l', (byte)' ', 0x00, 0x00, 0x00, 0x00,
            (byte)'j', (byte)'x', (byte)'l', (byte)' ',
        };

        /// <summary>
        /// Runs the <c>jxltran</c> command-line tool with the specified arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code (<c>0</c> on success, <c>1</c> on error).</returns>
        public static int Run(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Runs the <c>jxltran</c> command-line tool with custom output writers.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Standard output writer.</param>
        /// <param name="stderr">Standard error writer.</param>
        /// <returns>Process exit code (<c>0</c> on success, <c>1</c> on error).</returns>
        public static int Run(string[] args, TextWriter? stdout, TextWriter? stderr)
        {
            TextWriter outWriter = stdout ?? Console.Out;
            TextWriter errWriter = stderr ?? Console.Error;
            JxlTranArgs targs    = new JxlTranArgs();

            if (!ParseJxlTranArgs(args ?? Array.Empty<string>(), targs, errWriter))
            {
                PrintJxlTranHelp(outWriter);

                return 1;
            }

            if (targs.Version)
            {
                outWriter.WriteLine($"jxltran {CjxlTool.FormatConfigVersion(JxlDecoder.Version())}");

                return 0;
            }

            if (targs.Help)
            {
                PrintJxlTranHelp(outWriter);

                return 0;
            }

            if (!ValidateJxlTranArgs(targs, errWriter))
            {
                return 1;
            }

            return ExecuteJxlTran(targs, errWriter);
        }

        private static bool ValidateJxlTranArgs(JxlTranArgs targs, TextWriter errWriter)
        {
            if (string.IsNullOrEmpty(targs.FileIn) || string.IsNullOrEmpty(targs.FileOut))
            {
                errWriter.WriteLine("Both INPUT and OUTPUT filenames are required.");

                return false;
            }

            if (targs.Extract && (targs.Pack || targs.AddBoxSpecs.Count > 0 || targs.RemoveBoxTypes.Count > 0))
            {
                errWriter.WriteLine("Cannot combine --extract with --pack, --add_box, or --remove_box.");

                return false;
            }

            foreach (string rtype in targs.RemoveBoxTypes)
            {
                if (rtype.Length != 4)
                {
                    errWriter.WriteLine($"Invalid 4-character box type: \"{rtype}\"");

                    return false;
                }
            }

            return true;
        }

        private static int ExecuteJxlTran(JxlTranArgs targs, TextWriter errWriter)
        {
            if (!CjxlTool.TryReadInputFile(targs.FileIn!, out byte[] input, errWriter))
            {
                return 1;
            }

            JxlSignature sig = JxlDecoder.CheckSignature(input);

            if (sig != JxlSignature.Codestream && sig != JxlSignature.Container)
            {
                errWriter.WriteLine("Invalid JPEG XL input file.");

                return 1;
            }

            if (targs.Pack && sig != JxlSignature.Codestream)
            {
                errWriter.WriteLine("--pack requires a raw JPEG XL codestream input.");

                return 1;
            }

            if (targs.Extract)
            {
                return ExtractCodestreamFromContainer(input, sig, targs.FileOut!, errWriter);
            }

            return TransformContainerOrCodestream(input, sig, targs, errWriter);
        }

        private static int ExtractCodestreamFromContainer(
            ReadOnlySpan<byte> input,
            JxlSignature sig,
            string fileOut,
            TextWriter errWriter)
        {
            if (sig != JxlSignature.Container)
            {
                errWriter.WriteLine("--extract requires a JPEG XL container input.");

                return 1;
            }

            using MemoryStream codestream = new MemoryStream();
            int pos                       = 0;

            while (JxlInfoTool.TryReadNextBox(input, ref pos, out string boxType, out ReadOnlySpan<byte> payload))
            {
                if (!AppendCodestreamBoxSlice(boxType, payload, codestream, errWriter))
                {
                    return 1;
                }
            }

            if (codestream.Length == 0)
            {
                errWriter.WriteLine("No jxlc or jxlp codestream boxes found in container.");

                return 1;
            }

            return CjxlTool.TryWriteOutputFile(fileOut, codestream.ToArray(), errWriter) ? 0 : 1;
        }

        private static bool AppendCodestreamBoxSlice(
            string boxType,
            ReadOnlySpan<byte> payload,
            MemoryStream codestream,
            TextWriter errWriter)
        {
            if (boxType == "jxll" && payload.Length >= 1 && payload[0] > 5)
            {
                errWriter.WriteLine($"Warning: codestream level {payload[0]} > 5.");
            }

            if (boxType == "jxlc")
            {
                codestream.Write(payload);

                return true;
            }

            if (boxType == "jxlp")
            {
                if (payload.Length < 4)
                {
                    errWriter.WriteLine("Corrupt jxlp box.");

                    return false;
                }

                codestream.Write(payload.Slice(4));
            }

            return true;
        }

        private static int TransformContainerOrCodestream(
            ReadOnlySpan<byte> input,
            JxlSignature sig,
            JxlTranArgs targs,
            TextWriter errWriter)
        {
            using MemoryStream output = new MemoryStream();

            output.Write(ContainerSignatureAndFtyp);

            if (sig == JxlSignature.Codestream)
            {
                WriteBoxToStream(output, "jxlc", input);
            }
            else if (!CopyFilteredBoxes(input, targs.RemoveBoxTypes, output))
            {
                errWriter.WriteLine("Failed to parse container boxes.");

                return 1;
            }

            if (!AppendUserBoxes(targs.AddBoxSpecs, output, errWriter))
            {
                return 1;
            }

            return CjxlTool.TryWriteOutputFile(targs.FileOut!, output.ToArray(), errWriter) ? 0 : 1;
        }

        private static bool CopyFilteredBoxes(
            ReadOnlySpan<byte> input,
            List<string> removeTypes,
            MemoryStream output)
        {
            int pos = 0;

            while (JxlInfoTool.TryReadNextBox(input, ref pos, out string boxType, out ReadOnlySpan<byte> payload))
            {
                if (boxType == "JXL " || boxType == "ftyp")
                {
                    continue;
                }

                string effectiveType = GetEffectiveBoxType(boxType, payload);

                if (ShouldRemoveBox(boxType, effectiveType, removeTypes))
                {
                    continue;
                }

                WriteBoxToStream(output, boxType, payload);
            }

            return pos == input.Length;
        }

        private static string GetEffectiveBoxType(string boxType, ReadOnlySpan<byte> payload)
        {
            if (boxType == "brob" && payload.Length >= 4)
            {
                return Encoding.ASCII.GetString(payload.Slice(0, 4));
            }

            return boxType;
        }

        private static bool ShouldRemoveBox(string boxType, string effectiveType, List<string> removeTypes)
        {
            foreach (string rem in removeTypes)
            {
                if (rem == boxType || rem == effectiveType)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AppendUserBoxes(
            List<string> addBoxSpecs,
            MemoryStream output,
            TextWriter errWriter)
        {
            foreach (string spec in addBoxSpecs)
            {
                int eq = spec.IndexOf('=');

                if (eq != 4 && eq != 5)
                {
                    errWriter.WriteLine($"Invalid --add_box specification \"{spec}\"; expected TYPE=FILENAME.");

                    return false;
                }

                string typeSpec   = spec.Substring(0, eq);
                string boxPath    = spec.Substring(eq + 1);
                bool compressBrob = typeSpec.StartsWith("*", StringComparison.Ordinal);
                string boxType    = compressBrob ? typeSpec.Substring(1) : typeSpec;

                if (boxType.Length != 4 || !CjxlTool.TryReadInputFile(boxPath, out byte[] boxBytes, errWriter))
                {
                    return false;
                }

                if (compressBrob)
                {
                    byte[] brobPayload = CreateBrobPayload(boxType, boxBytes);

                    WriteBoxToStream(output, "brob", brobPayload);
                }
                else
                {
                    WriteBoxToStream(output, boxType, boxBytes);
                }
            }

            return true;
        }

        private static byte[] CreateBrobPayload(string innerBoxType, ReadOnlySpan<byte> rawPayload)
        {
            using MemoryStream ms = new MemoryStream();
            Span<byte> typeBytes  = stackalloc byte[4];

            Encoding.ASCII.GetBytes(innerBoxType, typeBytes);
            ms.Write(typeBytes);

            using (BrotliStream brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                brotli.Write(rawPayload);
            }

            return ms.ToArray();
        }

        private static void WriteBoxToStream(MemoryStream stream, string boxType, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[8];
            ulong totalSize   = 8ul + (ulong)payload.Length;

            BinaryPrimitives.WriteUInt32BigEndian(header.Slice(0, 4), (uint)totalSize);
            Encoding.ASCII.GetBytes(boxType.AsSpan(0, 4), header.Slice(4, 4));
            stream.Write(header);
            stream.Write(payload);
        }

        private static bool ParseJxlTranArgs(string[] args, JxlTranArgs targs, TextWriter errWriter)
        {
            int positionalIndex = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal) || arg == "-")
                {
                    if (!AssignJxlTranPositional(arg, ref positionalIndex, targs, errWriter))
                    {
                        return false;
                    }

                    continue;
                }

                if (!ParseSingleJxlTranOption(args, ref i, targs, errWriter))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AssignJxlTranPositional(
            string arg,
            ref int positionalIndex,
            JxlTranArgs targs,
            TextWriter errWriter)
        {
            if (positionalIndex == 0)
            {
                targs.FileIn = arg;
                positionalIndex++;

                return true;
            }

            if (positionalIndex == 1)
            {
                targs.FileOut = arg;
                positionalIndex++;

                return true;
            }

            errWriter.WriteLine($"Unexpected positional argument: {arg}");

            return false;
        }

        private static bool ParseSingleJxlTranOption(
            string[] args,
            ref int index,
            JxlTranArgs targs,
            TextWriter errWriter)
        {
            CjxlTool.SplitOptionToken(args[index], out string key, out string? inlineVal);

            if (TryHandleJxlTranFlagOption(key, targs))
            {
                return true;
            }

            if (key == "-a" || key == "--add_box")
            {
                if (!CjxlTool.TryConsumeOptionValue(args, ref index, inlineVal, out string addSpec, errWriter, key))
                {
                    return false;
                }

                targs.AddBoxSpecs.Add(addSpec);

                return true;
            }

            if (key == "-r" || key == "--remove_box")
            {
                if (!CjxlTool.TryConsumeOptionValue(args, ref index, inlineVal, out string remType, errWriter, key))
                {
                    return false;
                }

                targs.RemoveBoxTypes.Add(remType);

                return true;
            }

            errWriter.WriteLine($"Unknown option: {key}");

            return false;
        }

        private static bool TryHandleJxlTranFlagOption(string key, JxlTranArgs targs)
        {
            if (key == "-p" || key == "--pack")
            {
                targs.Pack = true;

                return true;
            }

            if (key == "-x" || key == "--extract")
            {
                targs.Extract = true;

                return true;
            }

            if (key == "-q" || key == "--quiet")
            {
                targs.Quiet = true;

                return true;
            }

            if (key == "-V" || key == "--version")
            {
                targs.Version = true;

                return true;
            }

            if (key == "-h" || key == "--help")
            {
                targs.Help = true;

                return true;
            }

            return false;
        }

        private static void PrintJxlTranHelp(TextWriter writer)
        {
            writer.WriteLine("Usage: jxltran [OPTIONS...] INPUT OUTPUT");
            writer.WriteLine("  -p, --pack                        Turn a raw JXL codestream into a container");
            writer.WriteLine("  -x, --extract                     Extract the raw JXL codestream from a container");
            writer.WriteLine("  -a, --add_box=TYPE=FILENAME       Add a metadata box (prefix TYPE with * for brob)");
            writer.WriteLine("  -r, --remove_box=TYPE             Remove all boxes of the given 4-char type");
            writer.WriteLine("  -q, --quiet                       Be more silent");
            writer.WriteLine("  -V, --version                     Print version and exit");
            writer.WriteLine("  -h, --help                        Print this help message");
        }
    }
}
