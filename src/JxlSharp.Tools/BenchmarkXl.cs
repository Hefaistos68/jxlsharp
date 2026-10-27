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
using System.IO;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.Threads;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Helper class that instantiates a <see cref="ThreadParallelRunner"/> and wraps it in a
    /// <see cref="ThreadPool"/> and <see cref="IParallelRunner"/>.
    /// Ported from <c>tools/thread_pool_internal.h</c>.
    /// </summary>
    public sealed class ThreadPoolInternal : IParallelRunner, IDisposable
    {
        private readonly ThreadParallelRunner _runner;
        private readonly ThreadPool _pool;
        private readonly nuint _numWorkerThreads;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadPoolInternal"/> class.
        /// </summary>
        /// <param name="numThreads">Number of worker threads, or negative to use the processor default.</param>
        public ThreadPoolInternal(int numThreads = -1)
        {
            nuint resolved    = numThreads < 0
                ? ThreadParallelRunner.DefaultNumWorkerThreads()
                : (nuint)numThreads;
            _numWorkerThreads = resolved;
            _runner           = new ThreadParallelRunner((int)resolved);
            _pool             = new ThreadPool(ThreadParallelRunner.Runner, _runner);
        }

        /// <summary>
        /// Gets the underlying <see cref="ThreadPool"/> instance.
        /// </summary>
        public ThreadPool Pool => _pool;

        /// <summary>
        /// Gets the number of worker threads configured on this pool.
        /// </summary>
        public nuint NumWorkerThreads => _numWorkerThreads;

        /// <inheritdoc/>
        public JxlParallelRunner? Runner => ThreadParallelRunner.Runner;

        /// <inheritdoc/>
        public object? RunnerOpaque => _runner;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runner.Dispose();
        }
    }

    /// <summary>
    /// Command-line arguments for the <c>benchmark_xl</c> suite.
    /// Ported from <c>tools/benchmark/benchmark_args.h</c> and <c>tools/benchmark/benchmark_args.cc</c>.
    /// </summary>
    public sealed class BenchmarkArgs
    {
        private readonly ColorHintsProxy _colorHintsProxy = new ColorHintsProxy();

        /// <summary>Gets or sets the filename or glob pattern matching input images.</summary>
        public string Input { get; set; } = string.Empty;

        /// <summary>Gets or sets the comma-separated list of codec descriptions to benchmark.</summary>
        public string Codec { get; set; } = "jxl:d1:7,jxl:d2:7,jxl:d4:7";

        /// <summary>Gets or sets a value indicating whether per-image size and distortion details are printed.</summary>
        public bool PrintDetails { get; set; }

        /// <summary>Gets or sets a value indicating whether per-image details are printed as CSV.</summary>
        public bool PrintDetailsCsv { get; set; }

        /// <summary>Gets or sets a value indicating whether codec-specific extended stats are printed.</summary>
        public bool PrintMoreStats { get; set; }

        /// <summary>Gets or sets a value indicating whether 5%/95% Butteraugli distance percentiles are printed.</summary>
        public bool PrintDistancePercentiles { get; set; }

        /// <summary>Gets or sets a value indicating whether error messages are suppressed.</summary>
        public bool SilentErrors { get; set; }

        /// <summary>Gets or sets a value indicating whether compressed bitstreams are saved to disk.</summary>
        public bool SaveCompressed { get; set; }

        /// <summary>Gets or sets a value indicating whether decoded images and heatmaps are saved to disk.</summary>
        public bool SaveDecompressed { get; set; }

        /// <summary>Gets or sets a value indicating whether inputs are already-compressed files for decode-only benchmarking.</summary>
        public bool DecodeOnly { get; set; }

        /// <summary>Gets or sets a value indicating whether Butteraugli quality metrics are skipped.</summary>
        public bool SkipButteraugli { get; set; }

        /// <summary>Gets or sets the multiplier applied to decoded pixel values before saving.</summary>
        public double MulOutput { get; set; }

        /// <summary>Gets or sets the heatmap lower ("good") Butteraugli threshold (0 for default).</summary>
        public double HeatmapGood { get; set; }

        /// <summary>Gets or sets the heatmap upper ("bad") Butteraugli threshold (0 for default).</summary>
        public double HeatmapBad { get; set; }

        /// <summary>Gets or sets the display intensity target in nits for Butteraugli evaluation (0 for default).</summary>
        public double IntensityTarget { get; set; }

        /// <summary>Gets or sets a value indicating whether tar generation is skipped in the HTML report.</summary>
        public bool Notar { get; set; }

        /// <summary>Gets or sets a value indicating whether an HTML report is generated.</summary>
        public bool WriteHtmlReport { get; set; }

        /// <summary>Gets or sets a value indicating whether the HTML report embeds images as base64 data URIs.</summary>
        public bool HtmlReportSelfContained { get; set; }

        /// <summary>Gets or sets a value indicating whether the HTML report uses decoded PNGs instead of compressed files.</summary>
        public bool HtmlReportUseDecompressed { get; set; }

        /// <summary>Gets or sets a value indicating whether heatmaps are included in the HTML report.</summary>
        public bool HtmlReportAddHeatmap { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether an original-image toggle is added to the HTML report.</summary>
        public bool OriginalView { get; set; }

        /// <summary>Gets or sets a value indicating whether a progress bar is shown during execution.</summary>
        public bool ShowProgress { get; set; }

        /// <summary>Gets or sets the input bit depth override (0 for none).</summary>
        public uint OverrideBitdepth { get; set; }

        /// <summary>Gets or sets the number of outer parallel benchmark tasks (-1 for automatic).</summary>
        public int NumThreads { get; set; } = -1;

        /// <summary>Gets or sets the number of inner worker threads per codec task (-1 for automatic).</summary>
        public int InnerThreads { get; set; } = -1;

        /// <summary>Gets or sets the number of decoding repetitions per task (0 for default).</summary>
        public int DecodeReps { get; set; }

        /// <summary>Gets or sets the number of encoding repetitions per task (0 for default).</summary>
        public int EncodeReps { get; set; }

        /// <summary>Gets or sets the directory used by external command-line codecs for temporary files.</summary>
        public string SampleTmpDir { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether a synthetic zoneplate image is written and exited.</summary>
        public bool WriteZoneplate { get; set; }

        /// <summary>Gets or sets the Butteraugli p-norm exponent (default 3.0).</summary>
        public double ErrorPnorm { get; set; } = 3.0;

        /// <summary>Gets the decoder color space hints.</summary>
        public ColorHints ColorHints => _colorHintsProxy.Target;

        /// <summary>Gets or sets a value indicating whether data usage disclaimers are printed.</summary>
        public bool DataUsage { get; set; }

        /// <summary>Gets or sets the output directory for saved compressed/decompressed artifacts.</summary>
        public string OutputDir { get; set; } = string.Empty;

        /// <summary>Gets or sets the desired color encoding description for saved decompressed images.</summary>
        public string OutputEncoding { get; set; } = string.Empty;

        /// <summary>Gets or sets the file extension for saved decompressed images (default <c>.png</c>).</summary>
        public string OutputExtension { get; set; } = ".png";

        /// <summary>Gets or sets the parsed <see cref="ColorEncoding"/> corresponding to <see cref="OutputEncoding"/>.</summary>
        public ColorEncoding ColorEncoding { get; set; } = new ColorEncoding();

        /// <summary>
        /// Registers all <c>benchmark_xl</c> command-line options on <paramref name="cmdline"/>.
        /// </summary>
        /// <param name="cmdline">Command-line parser.</param>
        /// <returns><see cref="Status.Ok"/>.</returns>
        public Status AddCommandLineOptions(CommandLineParser cmdline)
        {
            RegisterPrimaryCommandLineOptions(cmdline);
            RegisterReportAndThreadingOptions(cmdline);
            RegisterOutputAndMiscOptions(cmdline);

            return Status.Ok;
        }

        /// <summary>
        /// Validates parsed arguments and resolves output color encoding if specified.
        /// </summary>
        /// <returns><see cref="Status.Ok"/> if valid; otherwise failure.</returns>
        public Status ValidateArgsAfterParse()
        {
            if (WriteHtmlReport)
            {
                SaveCompressed   = true;
                SaveDecompressed = true;
            }

            if (string.IsNullOrEmpty(OutputEncoding))
            {
                ColorEncoding = ColorEncoding.SRGB();
            }
            else
            {
                if (!ColorDescription.ParseDescription(OutputEncoding, out JxlColorEncoding cExternal) ||
                    !ColorEncoding.FromExternal(cExternal))
                {
                    return Status.Failure($"Failed to parse output color encoding \"{OutputEncoding}\".");
                }
            }

            return ValidateOutputDirRequirements();
        }

        private void RegisterPrimaryCommandLineOptions(CommandLineParser cmdline)
        {
            cmdline.AddOptionValue<string>(
                '\0',
                "input",
                "FILENAME",
                "Filename or glob pattern matching input images.",
                v => Input = v,
                CmdLineParsers.ParseString);

            cmdline.AddOptionValue<string>(
                '\0',
                "codec",
                "CODECS",
                "Comma separated list of image codec descriptions to benchmark.",
                v => Codec = v,
                CmdLineParsers.ParseString);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "print_details",
                "Prints size and distortion for each image.",
                v => PrintDetails = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "print_details_csv",
                "Prints size and distortion for each image in CSV format.",
                v => PrintDetailsCsv = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "print_more_stats",
                "Prints codec-specific stats.",
                v => PrintMoreStats = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "print_distance_percentiles",
                "Prints 5 % and 95 % percentiles of butteraugli max distance.",
                v => PrintDistancePercentiles = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "silent_errors",
                "Do not print error messages.",
                v => SilentErrors = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "save_compressed",
                "Saves the compressed images.",
                v => SaveCompressed = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "save_decompressed",
                "Saves the decompressed images as PNG and heatmaps.",
                v => SaveDecompressed = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "decode_only",
                "Decode-only mode where input images are compressed files.",
                v => DecodeOnly = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "skip_butteraugli",
                "Do not compute butteraugli score.",
                v => SkipButteraugli = v,
                CmdLineParsers.SetBooleanTrue);
        }

        private void RegisterReportAndThreadingOptions(CommandLineParser cmdline)
        {
            cmdline.AddOptionValue<double>(
                '\0',
                "mul_output",
                "F",
                "Multiplies the decompressed output image by this amount before saving.",
                v => MulOutput = v,
                CmdLineParsers.ParseDouble);

            cmdline.AddOptionValue<double>(
                '\0',
                "heatmap_good",
                "F",
                "If greater than zero, use this as the good threshold for creating heatmaps.",
                v => HeatmapGood = v,
                CmdLineParsers.ParseDouble);

            cmdline.AddOptionValue<double>(
                '\0',
                "heatmap_bad",
                "F",
                "If greater than zero, use this as the bad threshold for creating heatmaps.",
                v => HeatmapBad = v,
                CmdLineParsers.ParseDouble);

            cmdline.AddOptionValue<double>(
                '\0',
                "intensity_target",
                "NITS",
                "If greater than zero, overarching intensity target in nits.",
                v => IntensityTarget = v,
                CmdLineParsers.ParseDouble);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "notar",
                "Do not create a tar file for the HTML report.",
                v => Notar = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "write_html_report",
                "Creates an HTML report with side-by-side comparisons.",
                v => WriteHtmlReport = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "html_report_self_contained",
                "Use data URLs in the HTML report.",
                v => HtmlReportSelfContained = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "html_report_use_decompressed",
                "Use the decoded image in the HTML report.",
                v => HtmlReportUseDecompressed = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "html_report_no_heatmap",
                "Do not add heatmaps to the HTML report.",
                v => HtmlReportAddHeatmap = v,
                CmdLineParsers.SetBooleanFalse);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "original_view",
                "Adds toggle button in HTML report to show original image.",
                v => OriginalView = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "show_progress",
                "Show progress bar during benchmark.",
                v => ShowProgress = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionValue<uint>(
                '\0',
                "override_bitdepth",
                "N",
                "If nonzero, override the input bit depth.",
                v => OverrideBitdepth = v,
                CmdLineParsers.ParseUint32);

            cmdline.AddOptionValue<int>(
                '\0',
                "num_threads",
                "N",
                "The number of tasks to run concurrently.",
                v => NumThreads = v,
                CmdLineParsers.ParseSigned);

            cmdline.AddOptionValue<int>(
                '\0',
                "inner_threads",
                "N",
                "The number of threads to use per task.",
                v => InnerThreads = v,
                CmdLineParsers.ParseSigned);

            cmdline.AddOptionValue<int>(
                '\0',
                "decode_reps",
                "N",
                "How many times to decode each image in the benchmark loop.",
                v => DecodeReps = v,
                CmdLineParsers.ParseSigned);

            cmdline.AddOptionValue<int>(
                '\0',
                "encode_reps",
                "N",
                "How many times to encode each image in the benchmark loop.",
                v => EncodeReps = v,
                CmdLineParsers.ParseSigned);
        }

        private void RegisterOutputAndMiscOptions(CommandLineParser cmdline)
        {
            cmdline.AddOptionValue<string>(
                '\0',
                "sample_tmp_dir",
                "DIR",
                "Directory to put samples by external codecs.",
                v => SampleTmpDir = v,
                CmdLineParsers.ParseString);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "write_zoneplate",
                "Write synthetic zoneplate image and exit.",
                v => WriteZoneplate = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionValue<double>(
                '\0',
                "error_pnorm",
                "P",
                "Norm p for computing butteraugli p-norm distance.",
                v => ErrorPnorm = v,
                CmdLineParsers.ParseDouble);

            cmdline.AddOptionValue(
                'x',
                "dec-hints",
                "key=value",
                "Color space or metadata hints for input decoding.",
                _colorHintsProxy,
                CmdLineParsers.ParseAndAppendKeyValue);

            cmdline.AddOptionFlag<bool>(
                '\0',
                "data-usage",
                "Print data usage disclaimer.",
                v => DataUsage = v,
                CmdLineParsers.SetBooleanTrue);

            cmdline.AddOptionValue<string>(
                '\0',
                "output_dir",
                "DIR",
                "Directory where to save compressed or decompressed files.",
                v => OutputDir = v,
                CmdLineParsers.ParseString);

            cmdline.AddOptionValue<string>(
                '\0',
                "output_encoding",
                "ENCODING",
                "Color encoding in which to save decompressed images.",
                v => OutputEncoding = v,
                CmdLineParsers.ParseString);

            cmdline.AddOptionValue<string>(
                '\0',
                "output_extension",
                "EXT",
                "Extension with which to save decompressed images (default .png).",
                v => OutputExtension = v,
                CmdLineParsers.ParseString);
        }

        private Status ValidateOutputDirRequirements()
        {
            if ((SaveCompressed || SaveDecompressed) && string.IsNullOrEmpty(OutputDir))
            {
                Console.Error.WriteLine(
                    "Need to specify --output_dir path when using any of --save_compressed, --save_decompressed or --write_html_report.");

                return Status.Failure("Missing --output_dir");
            }

            return Status.Ok;
        }
    }

    /// <summary>
    /// Aggregated JPEG XL encoder statistics across benchmarked images.
    /// Ported from <c>JxlStats</c> in <c>tools/benchmark/benchmark_stats.h</c>.
    /// </summary>
    public sealed class JxlStats
    {
        /// <summary>Gets or sets the number of input images assimilated.</summary>
        public nuint NumInputs { get; set; }

        /// <summary>Gets or sets the accumulated <see cref="JxlSharp.Encoder.JxlEncoderStats"/>.</summary>
        public JxlSharp.Encoder.JxlEncoderStats Stats { get; set; } = new JxlSharp.Encoder.JxlEncoderStats();

        /// <summary>
        /// Merges another <see cref="JxlStats"/> instance into this one.
        /// </summary>
        /// <param name="victim">Source statistics to assimilate.</param>
        public void Assimilate(JxlStats victim)
        {
            NumInputs += victim.NumInputs;
            Stats.Merge(victim.Stats);
        }

        /// <summary>
        /// Prints the accumulated encoder statistics.
        /// </summary>
        /// <param name="writer">Optional output writer; defaults to <see cref="Console.Out"/>.</param>
        public void Print(TextWriter? writer = null)
        {
            TextWriter outWriter = writer ?? Console.Out;
            nuint divisor        = Math.Max(1u, NumInputs);

            for (int i = 0; i < (int)JxlSharp.Encoder.JxlEncoderStatsKey.NumStats; i++)
            {
                JxlSharp.Encoder.JxlEncoderStatsKey key = (JxlSharp.Encoder.JxlEncoderStatsKey)i;
                nuint val                               = Stats.Get(key);

                if (val != 0)
                {
                    outWriter.WriteLine($"{key}: {val / divisor}");
                }
            }
        }
    }

    /// <summary>
    /// Column value type for benchmark result tables.
    /// Ported from <c>ColumnType</c> in <c>tools/benchmark/benchmark_stats.cc</c>.
    /// </summary>
    public enum ColumnType
    {
        /// <summary>Formatted string value.</summary>
        String = 0,

        /// <summary>Positive count or byte size.</summary>
        Size = 1,

        /// <summary>Floating-point metric value.</summary>
        PositiveFloat = 2,
    }

    /// <summary>
    /// Holds a single cell value in a benchmark result row.
    /// Ported from <c>ColumnValue</c> in <c>tools/benchmark/benchmark_stats.h</c>.
    /// </summary>
    public sealed class ColumnValue
    {
        /// <summary>Gets or sets the string value when <see cref="ColumnType.String"/>.</summary>
        public string S { get; set; } = string.Empty;

        /// <summary>Gets or sets the integer size/count value when <see cref="ColumnType.Size"/>.</summary>
        public nuint I { get; set; }

        /// <summary>Gets or sets the floating-point value when <see cref="ColumnType.PositiveFloat"/>.</summary>
        public double F { get; set; }
    }

    /// <summary>
    /// Descriptor for a single column in the <c>benchmark_xl</c> output table.
    /// Ported from <c>ColumnDescriptor</c> in <c>tools/benchmark/benchmark_stats.cc</c>.
    /// </summary>
    public readonly struct ColumnDescriptor
    {
        /// <summary>Column header label (two lines).</summary>
        public readonly string Label0;

        /// <summary>Second line of column header label.</summary>
        public readonly string Label1;

        /// <summary>Total column character width.</summary>
        public readonly uint Width;

        /// <summary>Formatting precision for floating-point values.</summary>
        public readonly uint Precision;

        /// <summary>Column value type.</summary>
        public readonly ColumnType Type;

        /// <summary>Whether the column is only shown when <c>--print_more_stats</c> is enabled.</summary>
        public readonly bool More;

        /// <summary>Whether the column is only shown when <c>--print_distance_percentiles</c> is enabled.</summary>
        public readonly bool Percentiles;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnDescriptor"/> struct.
        /// </summary>
        /// <param name="label0">First header line label.</param>
        /// <param name="label1">Second header line label.</param>
        /// <param name="width">Total column character width.</param>
        /// <param name="precision">Floating-point formatting precision.</param>
        /// <param name="type">Column value type.</param>
        /// <param name="more">Whether column is shown only with <c>--print_more_stats</c>.</param>
        /// <param name="percentiles">Whether column is shown only with <c>--print_distance_percentiles</c>.</param>
        public ColumnDescriptor(
            string label0,
            string label1,
            uint width,
            uint precision,
            ColumnType type,
            bool more,
            bool percentiles = false)
        {
            Label0      = label0;
            Label1      = label1;
            Width       = width;
            Precision   = precision;
            Type        = type;
            More        = more;
            Percentiles = percentiles;
        }
    }

    /// <summary>
    /// Aggregates compression, speed, and quality metrics across a benchmark corpus and formats result tables.
    /// Ported from <c>BenchmarkStats</c> in <c>tools/benchmark/benchmark_stats.h</c> and <c>tools/benchmark/benchmark_stats.cc</c>.
    /// </summary>
    public sealed class BenchmarkStats
    {
        private static readonly ColumnDescriptor[] Descriptors = new ColumnDescriptor[]
        {
            new ColumnDescriptor("Encoding",     "kPixels",     10, 0, ColumnType.Size,          false),
            new ColumnDescriptor("        ",     "  Bytes",     10, 0, ColumnType.Size,          false),
            new ColumnDescriptor("        ",     "    BPP",     13, 7, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("   ",          "E MP/s",       9, 3, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("   ",          "D MP/s",       9, 3, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("     Max",     "    norm",    13, 8, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("        ",     "SSIMULACRA2", 13, 8, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("        ",     "   PSNR",      9, 2, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("       ",      "  pnorm",     13, 8, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("       ",      "BPP*pnorm",   16, 12, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("       ",      "QABPP",        9, 3, ColumnType.PositiveFloat, false),
            new ColumnDescriptor("     ",        "Bugs",         8, 5, ColumnType.Size,          false),
            new ColumnDescriptor("    ",         "5%",           9, 4, ColumnType.PositiveFloat, false, true),
            new ColumnDescriptor("    ",         "95%",          9, 4, ColumnType.PositiveFloat, false, true),
            new ColumnDescriptor("     Max",     "  dBPP",      13, 7, ColumnType.PositiveFloat, true),
            new ColumnDescriptor("     Min",     "SSIMULACRA2", 13, 8, ColumnType.PositiveFloat, true),
            new ColumnDescriptor("   ",          "D KB/s",       9, 1, ColumnType.PositiveFloat, true),
        };

        /// <summary>Gets or sets the total number of input files processed.</summary>
        public nuint TotalInputFiles { get; set; }

        /// <summary>Gets or sets the total number of input pixels across all frames.</summary>
        public nuint TotalInputPixels { get; set; }

        /// <summary>Gets or sets the total compressed size in bytes.</summary>
        public nuint TotalCompressedSize { get; set; }

        /// <summary>Gets or sets the total adj-compressed size in bytes.</summary>
        public nuint TotalAdjCompressedSize { get; set; }

        /// <summary>Gets or sets the total encoding time in seconds.</summary>
        public double TotalTimeEncode { get; set; }

        /// <summary>Gets or sets the total decoding time in seconds.</summary>
        public double TotalTimeDecode { get; set; }

        /// <summary>Gets or sets the sum of squared Butteraugli max distances.</summary>
        public double MaxDistance { get; set; }

        /// <summary>Gets or sets the pixel-weighted sum of Butteraugli p-norms.</summary>
        public double DistancePNorm { get; set; }

        /// <summary>Gets or sets the pixel-weighted sum of SSIMULACRA2 scores.</summary>
        public double Ssimulacra2 { get; set; }

        /// <summary>Gets or sets the minimum SSIMULACRA2 score observed.</summary>
        public double MinSsimulacra2 { get; set; } = double.MaxValue;

        /// <summary>Gets or sets the pixel-weighted sum of PSNR values.</summary>
        public double Psnr { get; set; }

        /// <summary>Gets or sets the maximum quality-adjusted BPP observed.</summary>
        public double MaxAbpp { get; set; }

        /// <summary>Gets the per-image Butteraugli max distances.</summary>
        public List<float> Distances { get; } = new List<float>();

        /// <summary>Gets the per-image p-norm distances.</summary>
        public List<float> Pnorms { get; } = new List<float>();

        /// <summary>Gets the per-image SSIMULACRA2 scores.</summary>
        public List<float> Ssimulacra2Scores { get; } = new List<float>();

        /// <summary>Gets or sets the total number of codec errors encountered.</summary>
        public nuint TotalErrors { get; set; }

        /// <summary>Gets or sets the aggregated JPEG XL encoder statistics.</summary>
        public JxlStats JxlStats { get; set; } = new JxlStats();

        /// <summary>Gets the extra channels distance sums.</summary>
        public List<float> ExtraChannelDistances { get; } = new List<float>();

        /// <summary>
        /// Merges another <see cref="BenchmarkStats"/> instance into this instance.
        /// </summary>
        /// <param name="victim">Source stats to assimilate.</param>
        public void Assimilate(BenchmarkStats victim)
        {
            TotalInputFiles        += victim.TotalInputFiles;
            TotalInputPixels       += victim.TotalInputPixels;
            TotalCompressedSize    += victim.TotalCompressedSize;
            TotalAdjCompressedSize += victim.TotalAdjCompressedSize;
            TotalTimeEncode        += victim.TotalTimeEncode;
            TotalTimeDecode        += victim.TotalTimeDecode;
            MaxDistance             = Math.Max(MaxDistance, victim.MaxDistance);
            DistancePNorm          += victim.DistancePNorm;
            Ssimulacra2            += victim.Ssimulacra2;
            MinSsimulacra2          = Math.Min(MinSsimulacra2, victim.MinSsimulacra2);
            Psnr                   += victim.Psnr;
            MaxAbpp                 = Math.Max(MaxAbpp, victim.MaxAbpp);
            Distances.AddRange(victim.Distances);
            Pnorms.AddRange(victim.Pnorms);
            Ssimulacra2Scores.AddRange(victim.Ssimulacra2Scores);
            TotalErrors            += victim.TotalErrors;
            JxlStats.Assimilate(victim.JxlStats);
            AssimilateExtraChannelDistances(victim.ExtraChannelDistances);
        }

        /// <summary>
        /// Prints extended statistics (<see cref="JxlStats"/> and extra channel distances).
        /// </summary>
        /// <param name="writer">Optional output writer; defaults to <see cref="Console.Out"/>.</param>
        public void PrintMoreStats(TextWriter? writer = null)
        {
            JxlStats.Print(writer);
        }

        /// <summary>
        /// Computes all table column values for this codec run.
        /// </summary>
        /// <param name="codecDesc">Codec description label.</param>
        /// <param name="numThreads">Number of outer benchmark threads.</param>
        /// <returns>List of <see cref="ColumnValue"/> entries corresponding to <see cref="Descriptors"/>.</returns>
        public List<ColumnValue> ComputeColumns(string codecDesc, nuint numThreads)
        {
            double compBpp    = TotalInputPixels > 0 ? TotalCompressedSize * 8.0 / TotalInputPixels : 0.0;
            double adjCompBpp = TotalInputPixels > 0 ? TotalAdjCompressedSize * 8.0 / TotalInputPixels : 0.0;
            double mpsEncode  = TotalTimeEncode > 0.0
                ? TotalInputPixels * numThreads / (1000000.0 * TotalTimeEncode)
                : 0.0;
            double mpsDecode  = TotalTimeDecode > 0.0
                ? TotalInputPixels * numThreads / (1000000.0 * TotalTimeDecode)
                : 0.0;
            double kbsDecode  = TotalTimeDecode > 0.0
                ? TotalCompressedSize * numThreads / (1000.0 * TotalTimeDecode)
                : 0.0;
            double distAvg    = TotalInputPixels > 0 ? DistancePNorm / TotalInputPixels : 0.0;
            double ssim2Avg   = TotalInputPixels > 0 ? Ssimulacra2 / TotalInputPixels : 0.0;
            double psnrAvg    = TotalInputPixels > 0 ? Psnr / TotalInputPixels : 0.0;
            double bppPnorm   = distAvg * compBpp;

            ComputePercentiles(out double p5, out double p95);

            return BuildColumnValueList(
                codecDesc,
                compBpp,
                mpsEncode,
                mpsDecode,
                ssim2Avg,
                psnrAvg,
                distAvg,
                bppPnorm,
                adjCompBpp,
                p5,
                p95,
                kbsDecode);
        }

        /// <summary>
        /// Formats the two-line table header and separator line.
        /// </summary>
        /// <param name="maxCodecDescWidth">Width of the first (Encoding) column.</param>
        /// <param name="printMoreStats">Whether <c>--print_more_stats</c> columns are included.</param>
        /// <param name="printDistancePercentiles">Whether <c>--print_distance_percentiles</c> columns are included.</param>
        /// <returns>Formatted header string.</returns>
        public static string PrintHeader(
            int maxCodecDescWidth,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            return FormatHeaderLines(maxCodecDescWidth, printMoreStats, printDistancePercentiles);
        }

        /// <summary>
        /// Formats a single codec row in the benchmark table.
        /// </summary>
        /// <param name="values">Computed column values for the row.</param>
        /// <param name="maxCodecDescWidth">Width of the first (Encoding) column.</param>
        /// <param name="printMoreStats">Whether <c>--print_more_stats</c> columns are included.</param>
        /// <param name="printDistancePercentiles">Whether <c>--print_distance_percentiles</c> columns are included.</param>
        /// <returns>Formatted row string.</returns>
        public static string PrintRow(
            IReadOnlyList<ColumnValue> values,
            int maxCodecDescWidth,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            return FormatDataRow(values, maxCodecDescWidth, printMoreStats, printDistancePercentiles);
        }

        /// <summary>
        /// Formats the aggregate geometric mean row when multiple codecs are compared.
        /// </summary>
        /// <param name="rows">All codec rows.</param>
        /// <param name="maxCodecDescWidth">Width of the first column.</param>
        /// <param name="printMoreStats">Whether <c>--print_more_stats</c> columns are included.</param>
        /// <param name="printDistancePercentiles">Whether <c>--print_distance_percentiles</c> columns are included.</param>
        /// <returns>Formatted aggregate row string, or empty if fewer than 2 rows.</returns>
        public static string PrintAggregate(
            IReadOnlyList<List<ColumnValue>> rows,
            int maxCodecDescWidth,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            if (rows.Count <= 1)
            {
                return string.Empty;
            }

            List<ColumnValue> agg = BuildAggregateRow(rows);

            return FormatDataRow(agg, maxCodecDescWidth, printMoreStats, printDistancePercentiles);
        }

        private void AssimilateExtraChannelDistances(List<float> other)
        {
            if (ExtraChannelDistances.Count < other.Count)
            {
                while (ExtraChannelDistances.Count < other.Count)
                {
                    ExtraChannelDistances.Add(0.0f);
                }
            }

            for (int i = 0; i < other.Count; i++)
            {
                ExtraChannelDistances[i] = Math.Max(ExtraChannelDistances[i], other[i]);
            }
        }

        private void ComputePercentiles(out double p5, out double p95)
        {
            p5  = 0.0;
            p95 = 0.0;

            if (Distances.Count == 0)
            {
                return;
            }

            List<float> sorted = new List<float>(Distances);
            sorted.Sort();

            int idx5  = (int)Math.Min((nuint)(sorted.Count - 1), (nuint)(0.05 * sorted.Count));
            int idx95 = (int)Math.Min((nuint)(sorted.Count - 1), (nuint)(0.95 * sorted.Count));
            p5        = sorted[idx5];
            p95       = sorted[idx95];
        }

        private List<ColumnValue> BuildColumnValueList(
            string codecDesc,
            double compBpp,
            double mpsEncode,
            double mpsDecode,
            double ssim2Avg,
            double psnrAvg,
            double distAvg,
            double bppPnorm,
            double adjCompBpp,
            double p5,
            double p95,
            double kbsDecode)
        {
            double minSsim2 = MinSsimulacra2 == double.MaxValue ? 0.0 : MinSsimulacra2;

            return new List<ColumnValue>
            {
                new ColumnValue { S = codecDesc },
                new ColumnValue { I = TotalInputPixels / 1000u },
                new ColumnValue { I = TotalCompressedSize },
                new ColumnValue { F = compBpp },
                new ColumnValue { F = mpsEncode },
                new ColumnValue { F = mpsDecode },
                new ColumnValue { F = MaxDistance },
                new ColumnValue { F = ssim2Avg },
                new ColumnValue { F = psnrAvg },
                new ColumnValue { F = distAvg },
                new ColumnValue { F = bppPnorm },
                new ColumnValue { F = adjCompBpp },
                new ColumnValue { I = TotalErrors },
                new ColumnValue { F = p5 },
                new ColumnValue { F = p95 },
                new ColumnValue { F = MaxAbpp },
                new ColumnValue { F = minSsim2 },
                new ColumnValue { F = kbsDecode },
            };
        }

        private static ColumnValue ComputeAggregateCell(
            IReadOnlyList<List<ColumnValue>> rows,
            int colIdx,
            ColumnType type)
        {
            if (type == ColumnType.String)
            {
                return new ColumnValue { S = "---" };
            }

            double logSum = 0.0;
            int count     = 0;

            foreach (List<ColumnValue> r in rows)
            {
                double val = type == ColumnType.Size ? r[colIdx].I : r[colIdx].F;

                if (val > 0.0)
                {
                    logSum += Math.Log(val);
                    count++;
                }
            }

            double geomean = count > 0 ? Math.Exp(logSum / count) : 0.0;

            return type == ColumnType.Size
                ? new ColumnValue { I = (nuint)Math.Round(geomean) }
                : new ColumnValue { F = geomean };
        }

        /// <summary>
        /// Formats the complete table (header, codec rows, and optional aggregate row).
        /// </summary>
        /// <param name="rows">Computed column value lists for each codec.</param>
        /// <param name="printMoreStats">Whether <c>--print_more_stats</c> is enabled.</param>
        /// <param name="printDistancePercentiles">Whether <c>--print_distance_percentiles</c> is enabled.</param>
        /// <returns>Complete formatted table string.</returns>
        public static string FormatTable(
            IReadOnlyList<List<ColumnValue>> rows,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            int maxDescWidth = "Aggregate:".Length;

            foreach (List<ColumnValue> r in rows)
            {
                if (r.Count > 0 && r[0].S.Length > maxDescWidth)
                {
                    maxDescWidth = r[0].S.Length;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(FormatHeaderLines(maxDescWidth, printMoreStats, printDistancePercentiles));

            foreach (List<ColumnValue> r in rows)
            {
                sb.Append(FormatDataRow(r, maxDescWidth, printMoreStats, printDistancePercentiles));
            }

            if (rows.Count > 1)
            {
                List<ColumnValue> agg = BuildAggregateRow(rows);
                sb.Append(FormatDataRow(agg, maxDescWidth, printMoreStats, printDistancePercentiles));
            }

            return sb.ToString();
        }

        private static string FormatHeaderLines(
            int maxDescWidth,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            StringBuilder l0  = new StringBuilder();
            StringBuilder l1  = new StringBuilder();
            StringBuilder sep = new StringBuilder();

            l0.Append("Encoding".PadRight(maxDescWidth));
            l1.Append(new string(' ', maxDescWidth));
            sep.Append(new string('-', maxDescWidth));

            for (int i = 0; i < Descriptors.Length; i++)
            {
                ColumnDescriptor d = Descriptors[i];

                if ((d.More && !printMoreStats) || (d.Percentiles && !printDistancePercentiles))
                {
                    continue;
                }

                int w    = (int)d.Width;
                string top = i == 0 ? string.Empty : d.Label0.Trim();
                l0.Append(" | ").Append(top.PadLeft(w));
                l1.Append(" | ").Append(d.Label1.Trim().PadLeft(w));
                sep.Append("-|-").Append(new string('-', w));
            }

            return $"{l0}\n{l1}\n{sep}\n";
        }

        private static string FormatDataRow(
            IReadOnlyList<ColumnValue> row,
            int maxDescWidth,
            bool printMoreStats,
            bool printDistancePercentiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(row[0].S.PadRight(maxDescWidth));

            for (int i = 0; i < Descriptors.Length; i++)
            {
                ColumnDescriptor d = Descriptors[i];

                if ((d.More && !printMoreStats) || (d.Percentiles && !printDistancePercentiles))
                {
                    continue;
                }

                ColumnValue cv = row[i + 1];
                string text    = FormatSingleValue(d, cv);
                sb.Append(" | ").Append(text.PadLeft((int)d.Width));
            }

            sb.Append('\n');

            return sb.ToString();
        }

        private static string FormatSingleValue(ColumnDescriptor d, ColumnValue cv)
        {
            if (d.Type == ColumnType.String)
            {
                return cv.S;
            }

            if (d.Type == ColumnType.Size)
            {
                return cv.I.ToString(CultureInfo.InvariantCulture);
            }

            string fmt = $"F{d.Precision}";

            return cv.F.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private static List<ColumnValue> BuildAggregateRow(IReadOnlyList<List<ColumnValue>> rows)
        {
            List<ColumnValue> agg = new List<ColumnValue>(Descriptors.Length + 1)
            {
                new ColumnValue { S = "Aggregate:" },
            };

            for (int i = 0; i < Descriptors.Length; i++)
            {
                agg.Add(ComputeAggregateCell(rows, i + 1, Descriptors[i].Type));
            }

            return agg;
        }
    }

    /// <summary>
    /// String splitting, joining, and file-glob matching utilities for <c>benchmark_xl</c>.
    /// Ported from <c>tools/benchmark/benchmark_utils.cc</c> and <c>tools/benchmark/benchmark_file_io.cc</c>.
    /// </summary>
    public static class BenchmarkUtils
    {
        /// <summary>
        /// Splits <paramref name="s"/> by <paramref name="split"/>.
        /// Ported from <c>jxl::SplitString</c> in <c>tools/benchmark/benchmark_utils.cc</c>.
        /// </summary>
        /// <param name="s">Input string.</param>
        /// <param name="split">Delimiter character.</param>
        /// <returns>List of substrings.</returns>
        public static List<string> SplitString(string s, char split)
        {
            List<string> result = new List<string>();

            if (string.IsNullOrEmpty(s))
            {
                return result;
            }

            string[] parts = s.Split(split);

            foreach (string p in parts)
            {
                result.Add(p);
            }

            return result;
        }

        /// <summary>
        /// Joins <paramref name="strings"/> using <paramref name="joiner"/>.
        /// Ported from <c>jxl::JoinStrings</c> in <c>tools/benchmark/benchmark_utils.cc</c>.
        /// </summary>
        /// <param name="strings">Sequence of strings to join.</param>
        /// <param name="joiner">Separator string.</param>
        /// <returns>Joined string.</returns>
        public static string JoinStrings(IReadOnlyList<string> strings, string joiner)
        {
            if (strings == null || strings.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(joiner, strings);
        }

        /// <summary>
        /// Matches a path or filename against a glob pattern containing <c>*</c> and <c>?</c>.
        /// Ported from <c>MatchPattern</c> in <c>tools/benchmark/benchmark_file_io.cc</c>.
        /// </summary>
        /// <param name="pattern">Glob pattern.</param>
        /// <param name="path">Candidate path.</param>
        /// <param name="p">Starting index in <paramref name="pattern"/>.</param>
        /// <param name="f">Starting index in <paramref name="path"/>.</param>
        /// <returns><c>true</c> if <paramref name="path"/> matches <paramref name="pattern"/>.</returns>
        public static bool MatchPattern(string pattern, string path, int p = 0, int f = 0)
        {
            while (p < pattern.Length || f < path.Length)
            {
                if (p < pattern.Length && pattern[p] == '*')
                {
                    if (MatchStarWildcard(pattern, path, p, f))
                    {
                        return true;
                    }

                    return false;
                }

                if (p >= pattern.Length || f >= path.Length)
                {
                    return false;
                }

                if (pattern[p] != '?' && NormalizeSlash(pattern[p]) != NormalizeSlash(path[f]))
                {
                    return false;
                }

                p++;
                f++;
            }

            return true;
        }

        /// <summary>
        /// Resolves all files matching a comma-separated list of file paths or glob patterns.
        /// Ported from <c>GetMatchingFiles</c> in <c>tools/benchmark/benchmark_file_io.cc</c>.
        /// </summary>
        /// <param name="patternList">Single pattern or comma-separated list of patterns.</param>
        /// <returns>Sorted list of matching file paths.</returns>
        public static StatusOr<List<string>> GetMatchingFiles(string patternList)
        {
            if (string.IsNullOrWhiteSpace(patternList))
            {
                return Status.Failure("Empty input pattern.");
            }

            List<string> allFiles = new List<string>();
            List<string> patterns = SplitString(patternList, ',');

            foreach (string rawPattern in patterns)
            {
                string pat = rawPattern.Trim();

                if (string.IsNullOrEmpty(pat))
                {
                    continue;
                }

                List<string> matched = CollectFilesForSinglePattern(pat);

                if (matched.Count == 0)
                {
                    return Status.Failure($"{pat}: no matching files found");
                }

                matched.Sort(StringComparer.Ordinal);
                allFiles.AddRange(matched);
            }

            return allFiles;
        }

        private static bool MatchStarWildcard(string pattern, string path, int p, int f)
        {
            while (p < pattern.Length && pattern[p] == '*')
            {
                p++;
            }

            if (p == pattern.Length)
            {
                return true;
            }

            for (int i = f; i <= path.Length; i++)
            {
                if (MatchPattern(pattern, path, p, i))
                {
                    return true;
                }
            }

            return false;
        }

        private static char NormalizeSlash(char c)
        {
            return c == '\\' ? '/' : c;
        }

        private static List<string> CollectFilesForSinglePattern(string pattern)
        {
            List<string> results = new List<string>();

            if (File.Exists(pattern))
            {
                results.Add(pattern);

                return results;
            }

            if (Directory.Exists(pattern))
            {
                foreach (string file in Directory.GetFiles(pattern, "*", SearchOption.AllDirectories))
                {
                    results.Add(file);
                }

                return results;
            }

            string normalized = pattern.Replace('\\', '/');
            int firstWild     = normalized.IndexOfAny(new char[] { '*', '?' });

            if (firstWild < 0)
            {
                return results;
            }

            int lastSlash  = normalized.LastIndexOf('/', firstWild);
            string rootDir = lastSlash > 0 ? normalized.Substring(0, lastSlash) : ".";

            if (!Directory.Exists(rootDir))
            {
                return results;
            }

            foreach (string candidate in Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                string normCand = candidate.Replace('\\', '/');

                if (MatchPattern(normalized, normCand) ||
                    MatchPattern(Path.GetFileName(normalized), Path.GetFileName(normCand)))
                {
                    results.Add(candidate);
                }
            }

            return results;
        }
    }

    /// <summary>
    /// Base class for benchmarked image codecs (<c>jxl</c>, <c>png</c>, <c>jpeg</c>, <c>none</c>).
    /// Ported from <c>ImageCodec</c> in <c>tools/benchmark/benchmark_codec.h</c> and <c>tools/benchmark/benchmark_codec.cc</c>.
    /// </summary>
    public abstract class ImageCodec
    {
        /// <summary>Gets the benchmark arguments.</summary>
        public BenchmarkArgs Args { get; }

        /// <summary>Gets or sets the full codec description string (e.g. <c>jxl:wombat:d1</c>).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the target Butteraugli distance (default 1.0).</summary>
        public float ButteraugliTarget { get; protected set; } = 1.0f;

        /// <summary>Gets or sets the target quality (default 90.0).</summary>
        public float QTarget { get; protected set; } = 90.0f;

        /// <summary>Gets or sets the target bitrate (default 0.0).</summary>
        public float BitrateTarget { get; protected set; }

        /// <summary>Gets or sets the colon-separated codec parameter string.</summary>
        protected string JxlArgs { get; set; } = string.Empty;

        /// <summary>Gets or sets the accumulated codec error message.</summary>
        public string ErrorMessage { get; protected set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageCodec"/> class.
        /// </summary>
        /// <param name="args">Benchmark arguments.</param>
        protected ImageCodec(BenchmarkArgs args)
        {
            Args = args ?? throw new ArgumentNullException(nameof(args));
        }

        /// <summary>
        /// Factory method creating an <see cref="ImageCodec"/> from a codec specification such as <c>"jxl:cheetah:d2"</c>.
        /// Ported from <c>CreateImageCodec</c> in <c>tools/benchmark/benchmark_codec.cc</c>.
        /// </summary>
        /// <param name="description">Codec specification string.</param>
        /// <param name="args">Benchmark arguments.</param>
        /// <returns>Configured <see cref="ImageCodec"/> or null on invalid syntax.</returns>
        public static ImageCodec? CreateImageCodec(string description, BenchmarkArgs args)
        {
            string name  = description;
            string param = string.Empty;
            int colonIdx = description.IndexOf(':');

            if (colonIdx >= 0)
            {
                name  = description.Substring(0, colonIdx);
                param = description.Substring(colonIdx + 1);
            }

            ImageCodec? codec = InstantiateCodecByName(name, args);

            if (codec == null)
            {
                Console.Error.WriteLine($"Unknown image codec: {name}");

                return null;
            }

            codec.Description = description;

            if (!string.IsNullOrEmpty(param) && !codec.ParseParameters(param))
            {
                Console.Error.WriteLine($"Invalid parameters for codec {description}: {codec.ErrorMessage}");

                return null;
            }

            return codec;
        }

        /// <summary>
        /// Parses colon-separated parameters following the codec name.
        /// </summary>
        /// <param name="parameters">Colon-separated parameter string.</param>
        /// <returns><see cref="Status.Ok"/> if all parameters were valid.</returns>
        public virtual Status ParseParameters(string parameters)
        {
            JxlArgs            = parameters;
            List<string> parts = BenchmarkUtils.SplitString(parameters, ':');

            foreach (string part in parts)
            {
                if (!ParseParam(part))
                {
                    ErrorMessage = $"Invalid parameter {part}";

                    return Status.Failure(ErrorMessage);
                }
            }

            return Status.Ok;
        }

        /// <summary>
        /// Parses a single parameter token (<c>d&lt;float&gt;</c>, <c>q&lt;float&gt;</c>, or <c>br&lt;float&gt;</c>).
        /// </summary>
        /// <param name="param">Single parameter token.</param>
        /// <returns><see cref="Status.Ok"/> if recognized.</returns>
        public virtual Status ParseParam(string param)
        {
            if (string.IsNullOrEmpty(param))
            {
                return Status.Failure("Empty parameter");
            }

            if (param[0] == 'q' && !param.StartsWith("q_", StringComparison.Ordinal))
            {
                if (CjxlTool.TryParseFloat(param.Substring(1), out float q))
                {
                    QTarget = q;

                    return Status.Ok;
                }

                return Status.Failure($"Invalid quality parameter {param}");
            }

            if (param[0] == 'd' && CjxlTool.TryParseFloat(param.Substring(1), out float d))
            {
                ButteraugliTarget = d;

                return Status.Ok;
            }

            if (param.StartsWith("br", StringComparison.Ordinal) &&
                CjxlTool.TryParseFloat(param.Substring(2), out float br))
            {
                BitrateTarget = br;

                return Status.Ok;
            }

            return Status.Failure($"Unrecognized parameter {param}");
        }

        /// <summary>
        /// Compresses a <see cref="CodecInOut"/> image into <paramref name="compressed"/>.
        /// </summary>
        /// <param name="filename">Source filename.</param>
        /// <param name="io">Input codec container.</param>
        /// <param name="pool">Optional inner thread pool.</param>
        /// <param name="compressed">Receives compressed bytes.</param>
        /// <param name="speedStats">Speed statistics collector.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public abstract Status Compress(
            string filename,
            CodecInOut io,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats);

        /// <summary>
        /// Compresses a <see cref="PackedPixelFile"/> into <paramref name="compressed"/>.
        /// </summary>
        /// <param name="filename">Source filename.</param>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="pool">Optional inner thread pool.</param>
        /// <param name="compressed">Receives compressed bytes.</param>
        /// <param name="speedStats">Speed statistics collector.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public virtual Status Compress(
            string filename,
            PackedPixelFile ppf,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            compressed    = Array.Empty<byte>();
            CodecInOut io = new CodecInOut();

            if (!PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool?.Pool, io))
            {
                return Status.Failure("ConvertPackedPixelFileToCodecInOut failed");
            }

            return Compress(filename, io, pool, out compressed, speedStats);
        }

        /// <summary>
        /// Decompresses <paramref name="compressed"/> into a <see cref="CodecInOut"/> image.
        /// </summary>
        /// <param name="filename">Source filename.</param>
        /// <param name="compressed">Compressed input bytes.</param>
        /// <param name="pool">Optional inner thread pool.</param>
        /// <param name="io">Destination codec container.</param>
        /// <param name="speedStats">Speed statistics collector.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public abstract Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            CodecInOut io,
            SpeedStats speedStats);

        /// <summary>
        /// Decompresses <paramref name="compressed"/> into a <see cref="PackedPixelFile"/>.
        /// </summary>
        /// <param name="filename">Source filename.</param>
        /// <param name="compressed">Compressed input bytes.</param>
        /// <param name="pool">Optional inner thread pool.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="speedStats">Speed statistics collector.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public virtual Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            PackedPixelFile ppf,
            SpeedStats speedStats)
        {
            _ = filename;
            _ = compressed;
            _ = pool;
            _ = ppf;
            _ = speedStats;

            return Status.Failure("Decompress to PackedPixelFile not supported by this codec");
        }

        /// <summary>
        /// Summarizes codec-specific statistics into <paramref name="stats"/>.
        /// </summary>
        /// <param name="stats">Destination benchmark stats.</param>
        public virtual void SummarizeStats(BenchmarkStats stats)
        {
            _ = stats;
        }

        /// <summary>
        /// Gets a value indicating whether lossless JPEG transcoding is supported.
        /// </summary>
        /// <returns><c>true</c> if supported.</returns>
        public virtual bool CanRecompressJpeg() => false;

        /// <summary>
        /// Losslessly transcodes a raw JPEG bitstream into <paramref name="compressed"/>.
        /// </summary>
        /// <param name="filename">Source filename.</param>
        /// <param name="data">Raw JPEG bytes.</param>
        /// <param name="pool">Optional inner thread pool.</param>
        /// <param name="compressed">Receives compressed bytes.</param>
        /// <param name="speedStats">Speed statistics collector.</param>
        /// <returns><see cref="Status.Ok"/> on success.</returns>
        public virtual Status RecompressJpeg(
            string filename,
            byte[] data,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            _          = data;
            _          = pool;
            _          = speedStats;
            compressed = Array.Empty<byte>();

            return Status.Failure("JPEG recompression not supported");
        }

        /// <summary>
        /// Prints extended codec-specific stats when <c>--print_more_stats</c> is enabled.
        /// </summary>
        /// <param name="stats">Benchmark stats.</param>
        /// <param name="writer">Optional output writer.</param>
        public virtual void PrintMoreStats(BenchmarkStats stats, TextWriter? writer = null)
        {
            _ = stats;
            _ = writer;
        }

        private static ImageCodec? InstantiateCodecByName(string name, BenchmarkArgs args)
        {
            return name switch
            {
                "jxl"  => new JxlBenchmarkCodec(args),
                "png"  => new PngBenchmarkCodec(args),
                "jpeg" => new JpegBenchmarkCodec(args),
                "none" => new NoneBenchmarkCodec(args),
                _      => null,
            };
        }
    }

    /// <summary>
    /// JPEG XL (<c>jxl:...</c>) benchmark codec implementation.
    /// Ported from <c>JxlCodec</c> in <c>tools/benchmark/benchmark_codec_jxl.h</c> and <c>tools/benchmark/benchmark_codec_jxl.cc</c>.
    /// </summary>
    public sealed class JxlBenchmarkCodec : ImageCodec
    {
        private static readonly Dictionary<string, int> EffortNames = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["lightning"]      = 1,
            ["thunder"]        = 2,
            ["falcon"]         = 3,
            ["cheetah"]        = 4,
            ["hare"]           = 5,
            ["wombat"]         = 6,
            ["squirrel"]       = 7,
            ["kitten"]         = 8,
            ["tortoise"]       = 9,
            ["glacier"]        = 10,
            ["tectonic_plate"] = 11,
        };

        private readonly JXLCompressParams _cparams = new JXLCompressParams();
        private readonly JXLDecompressParams _dparams = new JXLDecompressParams();
        private readonly JxlStats _stats = new JxlStats();
        private bool _uint8;
        private bool _hasCparams;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlBenchmarkCodec"/> class.
        /// </summary>
        /// <param name="args">Benchmark arguments.</param>
        public JxlBenchmarkCodec(BenchmarkArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public override Status ParseParam(string param)
        {
            if (base.ParseParam(param))
            {
                return Status.Ok;
            }

            if (TryParseEffortOrModeParam(param))
            {
                return Status.Ok;
            }

            if (TryParseEncoderSettingParam(param))
            {
                return Status.Ok;
            }

            if (TryParseDecoderOrAdvancedParam(param))
            {
                return Status.Ok;
            }

            return Status.Failure($"Unrecognized jxl parameter: {param}");
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            CodecInOut io,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            compressed = Array.Empty<byte>();

            uint numChannels      = (uint)io.Metadata.M.ColorEncoding.Channels() + (io.Main.HasAlpha() ? 1u : 0u);
            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = JxlDataType.Float,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            PackedPixelFile ppf = new PackedPixelFile();

            if (!PackedImageConvert.ConvertCodecInOutToPackedPixelFile(
                io,
                in format,
                io.Metadata.M.ColorEncoding,
                pool?.Pool,
                ppf))
            {
                return Status.Failure("ConvertCodecInOutToPackedPixelFile failed");
            }

            return Compress(filename, ppf, pool, out compressed, speedStats);
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            PackedPixelFile ppf,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            compressed = Array.Empty<byte>();

            PrepareCompressParams(pool);

            double start = ExtrasTime.Now();
            bool ok      = CodecJxl.EncodeImageJXL(_cparams, ppf, null, out compressed);
            double end   = ExtrasTime.Now();

            if (!ok)
            {
                return Status.Failure("EncodeImageJXL failed");
            }

            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override bool CanRecompressJpeg()
        {
            return !_hasCparams;
        }

        /// <inheritdoc/>
        public override Status RecompressJpeg(
            string filename,
            byte[] data,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            compressed = Array.Empty<byte>();

            PrepareCompressParams(pool);

            PackedPixelFile ppf = new PackedPixelFile();
            double start        = ExtrasTime.Now();
            bool ok             = CodecJxl.EncodeImageJXL(_cparams, ppf, data, out compressed);
            double end          = ExtrasTime.Now();

            if (!ok)
            {
                return Status.Failure("RecompressJpeg failed");
            }

            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            CodecInOut io,
            SpeedStats speedStats)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            Status st           = Decompress(filename, compressed, pool, ppf, speedStats);

            if (!st)
            {
                return st;
            }

            return PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool?.Pool, io);
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            PackedPixelFile ppf,
            SpeedStats speedStats)
        {
            _                        = filename;
            _dparams.Runner          = pool;
            _dparams.KeepOrientation = true;
            _dparams.AcceptedFormats.Clear();

            JxlDataType dataType = _uint8 ? JxlDataType.UInt8 : JxlDataType.Float;

            for (uint numChannels = 1; numChannels <= 4; numChannels++)
            {
                _dparams.AcceptedFormats.Add(new JxlPixelFormat
                {
                    NumChannels = numChannels,
                    DataType    = dataType,
                    Endianness  = JxlEndianness.LittleEndian,
                    Align       = 0,
                });
            }

            double start = ExtrasTime.Now();
            bool ok      = CodecJxl.DecodeImageJXL(compressed, _dparams, out _, ppf);
            double end   = ExtrasTime.Now();

            if (!ok)
            {
                return Status.Failure("DecodeImageJXL failed");
            }

            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override void SummarizeStats(BenchmarkStats stats)
        {
            if (Args.PrintMoreStats)
            {
                stats.JxlStats.Assimilate(_stats);
            }
        }

        /// <inheritdoc/>
        public override void PrintMoreStats(BenchmarkStats stats, TextWriter? writer = null)
        {
            stats.PrintMoreStats(writer);
        }

        private void PrepareCompressParams(ThreadPoolInternal? pool)
        {
            if (Args.PrintMoreStats)
            {
                _stats.NumInputs = 1;
                _cparams.Stats   = _stats.Stats;
            }

            _cparams.Distance = ButteraugliTarget;
            _cparams.Runner   = pool;
        }

        private bool TryParseEffortOrModeParam(string param)
        {
            if (EffortNames.TryGetValue(param, out int namedEffort))
            {
                if (namedEffort >= 10)
                {
                    _cparams.AllowExpertOptions = true;
                }

                _cparams.AddOption(JxlEncoderFrameSettingId.Effort, namedEffort);

                return true;
            }

            if (int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numEffort) &&
                numEffort >= 1 && numEffort <= 11)
            {
                if (numEffort >= 10)
                {
                    _cparams.AllowExpertOptions = true;
                }

                _cparams.AddOption(JxlEncoderFrameSettingId.Effort, numEffort);

                return true;
            }

            if (param == "m")
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
                _cparams.AddOption(JxlEncoderFrameSettingId.ColorTransform, 1);
                _hasCparams = true;

                return true;
            }

            if (param == "uint8")
            {
                _uint8 = true;

                return true;
            }

            if (param == "expert")
            {
                _cparams.AllowExpertOptions = true;

                return true;
            }

            return false;
        }

        private bool TryParseEncoderSettingParam(string param)
        {
            if (param.StartsWith("D", StringComparison.Ordinal) &&
                CjxlTool.TryParseFloat(param.Substring(1), out float alphaDist))
            {
                _cparams.AlphaDistance = alphaDist;
                _hasCparams            = true;

                return true;
            }

            if (param.StartsWith("faster_decoding=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(16), NumberStyles.Integer, CultureInfo.InvariantCulture, out long fd))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.DecodingSpeed, fd);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("epf=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out long epf))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Epf, epf);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("gab=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out long gab))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Gaborish, gab);
                _hasCparams = true;

                return true;
            }

            return TryParseModularAndProgressiveParam(param);
        }

        private bool TryParseModularAndProgressiveParam(string param)
        {
            if (param == "p")
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Responsive, 1);
                _cparams.AddOption(JxlEncoderFrameSettingId.QProgressiveAc, 1);
                _hasCparams = true;

                return true;
            }

            if (param == "lp")
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ProgressiveDc, 1);
                _cparams.AddOption(JxlEncoderFrameSettingId.QProgressiveAc, 1);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("g", StringComparison.Ordinal) && !param.StartsWith("gab", StringComparison.Ordinal))
            {
                long gval = 1;

                if (param.Length > 1 &&
                    !long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out gval))
                {
                    return false;
                }

                _cparams.AddOption(JxlEncoderFrameSettingId.ModularGroupSize, gval);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("plt=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out long plt))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.PaletteColors, plt);
                _hasCparams = true;

                return true;
            }

            return TryParseModularPredictorAndColorspaceParam(param);
        }

        private bool TryParseModularPredictorAndColorspaceParam(string param)
        {
            if (param.StartsWith("C", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long cval))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ModularColorSpace, cval);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("c", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long cval2))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ModularColorSpace, cval2);
                _cparams.AddOption(JxlEncoderFrameSettingId.ModularGroupSize, 0);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("P", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long pval))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ModularPredictor, pval);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("I", StringComparison.Ordinal) &&
                CjxlTool.TryParseFloat(param.Substring(1), out float ival))
            {
                _cparams.AddFloatOption(JxlEncoderFrameSettingId.ModularMaTreeLearningPercent, ival < 1.0f ? ival * 100.0f : ival);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("E", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long eval))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ModularNbPrevChannels, eval);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("R", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long rval))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Responsive, rval);
                _hasCparams = true;

                return true;
            }

            return false;
        }

        private bool TryParseDecoderOrAdvancedParam(string param)
        {
            if (param.StartsWith("resampling=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out long res))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.Resampling, res);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("ec_resampling=", StringComparison.Ordinal) &&
                long.TryParse(param.Substring(14), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ecRes))
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ExtraChannelResampling, ecRes);
                _hasCparams = true;

                return true;
            }

            if (param == "already_downsampled")
            {
                _cparams.AlreadyDownsampled = 2;
                _hasCparams                 = true;

                return true;
            }

            if (param.StartsWith("downsampling=", StringComparison.Ordinal) &&
                nuint.TryParse(param.Substring(13), NumberStyles.Integer, CultureInfo.InvariantCulture, out nuint ds))
            {
                _dparams.MaxDownsampling = ds;

                return true;
            }

            if (param.StartsWith("max_passes=", StringComparison.Ordinal) &&
                uint.TryParse(param.Substring(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint mp))
            {
                _dparams.MaxPasses = mp;

                return true;
            }

            if (param == "noperc")
            {
                _cparams.AddOption(JxlEncoderFrameSettingId.ColorTransform, 1);
                _hasCparams = true;

                return true;
            }

            if (param.StartsWith("n", StringComparison.Ordinal) &&
                CjxlTool.TryParseFloat(param.Substring(1), out float noiseIso))
            {
                _cparams.AddFloatOption(JxlEncoderFrameSettingId.PhotonNoise, noiseIso);
                _hasCparams = true;

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// PNG (<c>png</c>) benchmark codec implementation.
    /// Ported from <c>PNGCodec</c> in <c>tools/benchmark/benchmark_codec_png.cc</c>.
    /// </summary>
    public sealed class PngBenchmarkCodec : ImageCodec
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PngBenchmarkCodec"/> class.
        /// </summary>
        /// <param name="args">Benchmark arguments.</param>
        public PngBenchmarkCodec(BenchmarkArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            CodecInOut io,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            compressed = Array.Empty<byte>();

            JxlSharp.Extras.Encoder? enc = CodecApng.GetAPNGEncoder();

            if (enc == null)
            {
                return Status.Failure("PNG encoder unavailable");
            }

            uint numChannels      = (uint)io.Metadata.M.ColorEncoding.Channels() + (io.Main.HasAlpha() ? 1u : 0u);
            uint bitsPerSample    = Math.Min(16u, io.Metadata.M.BitDepth.BitsPerSample);
            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = numChannels,
                DataType    = bitsPerSample > 8 ? JxlDataType.UInt16 : JxlDataType.UInt8,
                Endianness  = JxlEndianness.BigEndian,
                Align       = 0,
            };

            PackedPixelFile ppf = new PackedPixelFile();

            if (!PackedImageConvert.ConvertCodecInOutToPackedPixelFile(
                io,
                in format,
                io.Metadata.M.ColorEncoding,
                pool?.Pool,
                ppf))
            {
                return Status.Failure("ConvertCodecInOutToPackedPixelFile failed for PNG");
            }

            return Compress(filename, ppf, pool, out compressed, speedStats);
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            PackedPixelFile ppf,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            compressed = Array.Empty<byte>();

            JxlSharp.Extras.Encoder? enc = CodecApng.GetAPNGEncoder();

            if (enc == null)
            {
                return Status.Failure("PNG encoder unavailable");
            }

            EncodedImage encoded = new EncodedImage();
            double start         = ExtrasTime.Now();
            Status st            = enc.Encode(ppf, encoded, pool?.Pool);
            double end           = ExtrasTime.Now();

            if (!st || encoded.Bitstreams.Count == 0)
            {
                return Status.Failure("PNG encode failed");
            }

            compressed = encoded.Bitstreams[0];
            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            CodecInOut io,
            SpeedStats speedStats)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            Status st           = Decompress(filename, compressed, pool, ppf, speedStats);

            if (!st)
            {
                return st;
            }

            return PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool?.Pool, io);
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            PackedPixelFile ppf,
            SpeedStats speedStats)
        {
            _                = filename;
            _                = pool;
            ColorHints hints = new ColorHints();
            double start     = ExtrasTime.Now();
            Status st        = CodecApng.DecodeImageAPNG(compressed, hints, ppf);

            if (!st)
            {
                return Status.Failure("PNG decode failed");
            }

            double end = ExtrasTime.Now();
            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }
    }

    /// <summary>
    /// JPEG (<c>jpeg</c>) benchmark codec implementation.
    /// Ported from <c>JPEGCodec</c> in <c>tools/benchmark/benchmark_codec_jpeg.cc</c>.
    /// </summary>
    public sealed class JpegBenchmarkCodec : ImageCodec
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JpegBenchmarkCodec"/> class.
        /// </summary>
        /// <param name="args">Benchmark arguments.</param>
        public JpegBenchmarkCodec(BenchmarkArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public override Status ParseParam(string param)
        {
            if (base.ParseParam(param))
            {
                return Status.Ok;
            }

            if (param.StartsWith("enc=", StringComparison.Ordinal) ||
                param.StartsWith("chroma=", StringComparison.Ordinal) ||
                param.StartsWith("p", StringComparison.Ordinal))
            {
                return Status.Ok;
            }

            return Status.Failure($"Unrecognized jpeg parameter {param}");
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            CodecInOut io,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _          = filename;
            _          = io;
            _          = pool;
            _          = speedStats;
            compressed = Array.Empty<byte>();

            return Status.Failure("JPEG encoding is not enabled in this build");
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            CodecInOut io,
            SpeedStats speedStats)
        {
            _                   = filename;
            PackedPixelFile ppf = new PackedPixelFile();
            double start        = ExtrasTime.Now();
            Status st           = CodecJpg.DecodeImageJPG(compressed, Args.ColorHints, ppf);

            if (!st || !PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool?.Pool, io))
            {
                return Status.Failure("JPEG decoding is not enabled in this build");
            }

            double end = ExtrasTime.Now();
            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }
    }

    /// <summary>
    /// No-op pass-through (<c>none</c>) benchmark codec implementation.
    /// Ported from <c>NoneCodec</c> in <c>tools/benchmark/benchmark_codec_none.cc</c>.
    /// </summary>
    public sealed class NoneBenchmarkCodec : ImageCodec
    {
        private CodecInOut? _cachedImage;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoneBenchmarkCodec"/> class.
        /// </summary>
        /// <param name="args">Benchmark arguments.</param>
        public NoneBenchmarkCodec(BenchmarkArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public override Status ParseParam(string param)
        {
            _ = param;

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override Status Compress(
            string filename,
            CodecInOut io,
            ThreadPoolInternal? pool,
            out byte[] compressed,
            SpeedStats speedStats)
        {
            _            = filename;
            _            = pool;
            double start = ExtrasTime.Now();
            _cachedImage = io;
            compressed   = new byte[1];
            double end   = ExtrasTime.Now();
            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }

        /// <inheritdoc/>
        public override Status Decompress(
            string filename,
            ReadOnlySpan<byte> compressed,
            ThreadPoolInternal? pool,
            CodecInOut io,
            SpeedStats speedStats)
        {
            _            = filename;
            _            = compressed;
            _            = pool;
            double start = ExtrasTime.Now();

            if (_cachedImage != null && _cachedImage.Main.HasColor())
            {
                StatusOr<Image3<float>> copyOr = Image3<float>.Create(
                    io.MemoryManager,
                    _cachedImage.Main.XSize(),
                    _cachedImage.Main.YSize());

                if (!copyOr.IsOk)
                {
                    return copyOr.Status;
                }

                ImageOps.CopyImageTo(_cachedImage.Main.Color(), copyOr.Value);
                io.Metadata.M = _cachedImage.Metadata.M;
                io.SetFromImage(copyOr.Value, _cachedImage.Main.CCurrent);
            }

            double end = ExtrasTime.Now();
            speedStats.NotifyElapsed(end - start);

            return Status.Ok;
        }
    }

    /// <summary>
    /// Main entry point and runner for the <c>benchmark_xl</c> benchmark suite.
    /// Ported from <c>tools/benchmark/benchmark_xl.cc</c>.
    /// </summary>
    public static class BenchmarkXlTool
    {
        /// <summary>
        /// Runs the <c>benchmark_xl</c> command-line tool with the specified arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code (<c>0</c> on success, non-zero on error).</returns>
        public static int Run(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Runs the <c>benchmark_xl</c> command-line tool with custom output writers.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Standard output writer.</param>
        /// <param name="stderr">Standard error writer.</param>
        /// <returns>Process exit code (<c>0</c> on success, non-zero on error).</returns>
        public static int Run(string[] args, TextWriter? stdout, TextWriter? stderr)
        {
            TextWriter outWriter = stdout ?? Console.Out;
            TextWriter errWriter = stderr ?? Console.Error;
            BenchmarkArgs bargs  = new BenchmarkArgs();
            CommandLineParser cl = new CommandLineParser();

            bargs.AddCommandLineOptions(cl);

            string[] fullArgs = PrependProgramName("benchmark_xl", args ?? Array.Empty<string>());

            if (!cl.Parse(fullArgs))
            {
                errWriter.WriteLine("Use 'benchmark_xl -h' for more information");

                return 1;
            }

            if (cl.HelpFlagPassed())
            {
                cl.PrintHelp();

                return 0;
            }

            if (bargs.DataUsage)
            {
                outWriter.WriteLine("Note: benchmark_xl processes local image files only.");

                return 0;
            }

            if (!bargs.ValidateArgsAfterParse())
            {
                return 1;
            }

            if (bargs.WriteZoneplate)
            {
                return WriteZoneplateImage(outWriter, errWriter);
            }

            return ExecuteBenchmarkSuite(bargs, outWriter, errWriter);
        }

        private static string[] PrependProgramName(string progName, string[] args)
        {
            string[] full = new string[args.Length + 1];
            full[0]       = progName;
            Array.Copy(args, 0, full, 1, args.Length);

            return full;
        }

        private static int ExecuteBenchmarkSuite(
            BenchmarkArgs bargs,
            TextWriter outWriter,
            TextWriter errWriter)
        {
            List<string> methods = BenchmarkUtils.SplitString(bargs.Codec, ',');

            if (methods.Count == 0)
            {
                errWriter.WriteLine("No codecs specified.");

                return 1;
            }

            StatusOr<List<string>> fnamesOr = BenchmarkUtils.GetMatchingFiles(bargs.Input);

            if (!fnamesOr.IsOk || fnamesOr.Value.Count == 0)
            {
                errWriter.WriteLine($"Failed to match input files for \"{bargs.Input}\": {fnamesOr.Status}");

                return 1;
            }

            List<string> fnames = fnamesOr.Value;
            ResolveThreadCounts(
                bargs,
                methods.Count * fnames.Count,
                out int outerThreads,
                out int innerThreads);

            BenchmarkCorpus corpus = new BenchmarkCorpus(methods, fnames, outerThreads, innerThreads);

            if (!LoadInputCorpus(bargs, corpus, errWriter))
            {
                return 1;
            }

            return RunAllBenchmarkTasks(bargs, corpus, outWriter, errWriter);
        }

        private static void ResolveThreadCounts(
            BenchmarkArgs bargs,
            int numTasks,
            out int outerThreads,
            out int innerThreads)
        {
            int hwThreads = Math.Max(1, Environment.ProcessorCount);
            outerThreads  = bargs.NumThreads >= 0 ? bargs.NumThreads : hwThreads;
            outerThreads  = Math.Min(outerThreads, 4 * hwThreads);
            outerThreads  = Math.Min(outerThreads, numTasks);

            if (outerThreads == 1)
            {
                outerThreads = 0;
            }

            if (bargs.InnerThreads < 0)
            {
                if (outerThreads == 0)
                {
                    innerThreads = hwThreads;
                }
                else if (hwThreads <= outerThreads)
                {
                    innerThreads = 1;
                }
                else
                {
                    innerThreads = (hwThreads - outerThreads) / outerThreads;
                }
            }
            else
            {
                innerThreads = bargs.InnerThreads;
            }

            if (innerThreads == 1)
            {
                innerThreads = 0;
            }
        }

        private static bool LoadInputCorpus(
            BenchmarkArgs bargs,
            BenchmarkCorpus corpus,
            TextWriter errWriter)
        {
            int count = corpus.Fnames.Count;

            for (int i = 0; i < count; i++)
            {
                corpus.LoadedBytes.Add(Array.Empty<byte>());
                corpus.LoadedPpfs.Add(new PackedPixelFile());
                corpus.LoadedImages.Add(new CodecInOut());
            }

            bool allOk                    = true;
            using ThreadPoolInternal pool = new ThreadPoolInternal(corpus.OuterThreads);

            DataParallel.RunOnPool(
                pool.Pool,
                0,
                (uint)count,
                _ => Status.Ok,
                (task, _) =>
                {
                    int idx = (int)task;

                    if (!LoadSingleInput(
                        bargs,
                        corpus.Fnames[idx],
                        out byte[] raw,
                        corpus.LoadedPpfs[idx],
                        corpus.LoadedImages[idx],
                        errWriter))
                    {
                        allOk = false;

                        return Status.Ok;
                    }

                    corpus.LoadedBytes[idx] = raw;

                    return Status.Ok;
                },
                "LoadImages");

            return allOk;
        }

        private static bool LoadSingleInput(
            BenchmarkArgs bargs,
            string fname,
            out byte[] rawBytes,
            PackedPixelFile ppf,
            CodecInOut io,
            TextWriter errWriter)
        {
            if (!FileIo.ReadFile(fname, out rawBytes))
            {
                if (!bargs.SilentErrors)
                {
                    errWriter.WriteLine($"Failed to read input file: {fname}");
                }

                return false;
            }

            if (bargs.DecodeOnly)
            {
                return true;
            }

            string ext = Path.GetExtension(fname);

            if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Status decSt = ExtrasDecodeFacade.DecodeBytes(rawBytes, bargs.ColorHints, ppf, null, out _);

            if (!decSt || !PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, null, io))
            {
                if (!bargs.SilentErrors)
                {
                    errWriter.WriteLine($"Failed to decode input image: {fname}");
                }

                return false;
            }

            if (bargs.OverrideBitdepth != 0)
            {
                io.Metadata.M.SetUintSamples(bargs.OverrideBitdepth);
            }

            return true;
        }

        private static int RunAllBenchmarkTasks(
            BenchmarkArgs bargs,
            BenchmarkCorpus corpus,
            TextWriter outWriter,
            TextWriter errWriter)
        {
            int numMethods              = corpus.Methods.Count;
            int numImages               = corpus.Fnames.Count;
            int totalTasks              = numMethods * numImages;
            BenchmarkTaskEntry[] tasks  = new BenchmarkTaskEntry[totalTasks];

            for (int m = 0; m < numMethods; m++)
            {
                for (int idx = 0; idx < numImages; idx++)
                {
                    int t        = (m * numImages) + idx;
                    ImageCodec? c = ImageCodec.CreateImageCodec(corpus.Methods[m], bargs);

                    if (c == null)
                    {
                        return 1;
                    }

                    tasks[t] = new BenchmarkTaskEntry(c, m, idx);
                }
            }

            ExecuteTasksOnPool(bargs, corpus, tasks);

            return SummarizeAndPrintResults(bargs, corpus, tasks, outWriter, errWriter);
        }

        private static void ExecuteTasksOnPool(
            BenchmarkArgs bargs,
            BenchmarkCorpus corpus,
            BenchmarkTaskEntry[] tasks)
        {
            int effectiveOuter            = Math.Max(1, corpus.OuterThreads);
            using ThreadPoolInternal pool = new ThreadPoolInternal(corpus.OuterThreads);
            ThreadPoolInternal[] inners   = new ThreadPoolInternal[effectiveOuter];

            for (int i = 0; i < effectiveOuter; i++)
            {
                inners[i] = new ThreadPoolInternal(corpus.InnerThreads);
            }

            try
            {
                DataParallel.RunOnPool(
                    pool.Pool,
                    0,
                    (uint)tasks.Length,
                    _ => Status.Ok,
                    (i, threadIdx) =>
                    {
                        BenchmarkTaskEntry t     = tasks[i];
                        int slot                 = (int)(threadIdx % (nuint)inners.Length);
                        ThreadPoolInternal inner = inners[slot];

                        DoCompressDecompressTask(bargs, corpus, t, inner);

                        return Status.Ok;
                    },
                    "Benchmarkste");
            }
            finally
            {
                for (int i = 0; i < inners.Length; i++)
                {
                    inners[i].Dispose();
                }
            }
        }

        private static void DoCompressDecompressTask(
            BenchmarkArgs bargs,
            BenchmarkCorpus corpus,
            BenchmarkTaskEntry t,
            ThreadPoolInternal innerPool)
        {
            t.Stats.TotalInputFiles = 1;
            string filename         = corpus.Fnames[t.IdxImage];
            string imageShortName   = Path.GetFileName(filename);
            byte[] rawBytes         = corpus.LoadedBytes[t.IdxImage];
            PackedPixelFile origPpf = corpus.LoadedPpfs[t.IdxImage];
            CodecInOut origIo       = corpus.LoadedImages[t.IdxImage];

            if (!RunEncodePhase(bargs, t, imageShortName, rawBytes, origPpf, innerPool, out byte[] compressed))
            {
                t.Stats.TotalErrors++;

                return;
            }

            PackedPixelFile decPpf = new PackedPixelFile();

            if (!RunDecodePhase(bargs, t, imageShortName, compressed, innerPool, decPpf))
            {
                t.Stats.TotalErrors++;

                return;
            }

            for (int f = 0; f < decPpf.Frames.Count; f++)
            {
                PackedFrame frame          = decPpf.Frames[f];
                t.Stats.TotalInputPixels  += frame.Color.XSize * frame.Color.YSize;
            }

            t.Stats.TotalCompressedSize += (nuint)compressed.Length;

            if (bargs.DecodeOnly && !bargs.SaveCompressed && !bargs.SaveDecompressed)
            {
                return;
            }

            CodecInOut decIo = new CodecInOut();

            if (!PackedImageConvert.ConvertPackedPixelFileToCodecInOut(decPpf, innerPool.Pool, decIo))
            {
                t.Stats.TotalErrors++;

                return;
            }

            SaveOptionalArtifacts(bargs, t.Codec.Description, imageShortName, compressed, decIo, innerPool);

            if (!bargs.DecodeOnly && origIo.Main.HasColor())
            {
                ComputeQualityMetrics(bargs, t, origIo, decIo, compressed.Length, innerPool);
            }
        }

        private static bool RunEncodePhase(
            BenchmarkArgs bargs,
            BenchmarkTaskEntry t,
            string imageShortName,
            byte[] rawBytes,
            PackedPixelFile origPpf,
            ThreadPoolInternal innerPool,
            out byte[] compressed)
        {
            compressed = Array.Empty<byte>();

            if (bargs.DecodeOnly)
            {
                compressed = rawBytes;

                return true;
            }

            SpeedStats encodeSpeed = new SpeedStats();
            string ext             = Path.GetExtension(imageShortName);
            bool isJpeg            = string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);

            int maxReps = bargs.EncodeReps > 0 ? bargs.EncodeReps : 1;

            for (int rep = 0; rep < maxReps; rep++)
            {
                Status st = isJpeg && t.Codec.CanRecompressJpeg()
                    ? t.Codec.RecompressJpeg(imageShortName, rawBytes, innerPool, out compressed, encodeSpeed)
                    : t.Codec.Compress(imageShortName, origPpf, innerPool, out compressed, encodeSpeed);

                if (!st)
                {
                    return false;
                }
            }

            if (encodeSpeed.GetSummary(out SpeedStats.Summary summary))
            {
                t.Stats.TotalTimeEncode += summary.CentralTendency;
            }

            return true;
        }

        private static bool RunDecodePhase(
            BenchmarkArgs bargs,
            BenchmarkTaskEntry t,
            string imageShortName,
            byte[] compressed,
            ThreadPoolInternal innerPool,
            PackedPixelFile decPpf)
        {
            SpeedStats decodeSpeed = new SpeedStats();
            int maxReps            = bargs.DecodeReps > 0 ? bargs.DecodeReps : 1;

            for (int rep = 0; rep < maxReps; rep++)
            {
                Status st = t.Codec.Decompress(imageShortName, compressed, innerPool, decPpf, decodeSpeed);

                if (!st)
                {
                    return false;
                }
            }

            if (decodeSpeed.GetSummary(out SpeedStats.Summary summary))
            {
                t.Stats.TotalTimeDecode += summary.CentralTendency;
            }

            return decPpf.Frames.Count > 0;
        }

        private static void ComputeQualityMetrics(
            BenchmarkArgs bargs,
            BenchmarkTaskEntry t,
            CodecInOut origIo,
            CodecInOut decIo,
            int compressedLen,
            ThreadPoolInternal innerPool)
        {
            if (origIo.Frames.Count != decIo.Frames.Count)
            {
                t.Stats.TotalErrors++;

                return;
            }

            nuint pixels = origIo.Main.XSize() * origIo.Main.YSize();
            double psnr  = ExtrasMetrics.ComputePSNR(origIo.Main, decIo.Main, JxlCms.GetDefaultCms());
            t.Stats.Psnr += psnr * pixels;

            StatusOr<Msssim> ssim2Or = Ssimulacra2.ComputeSSIMULACRA2(origIo.Main, decIo.Main);
            double ssim2             = ssim2Or.IsOk ? ssim2Or.Value.Score() : 0.0;
            t.Stats.Ssimulacra2     += ssim2 * pixels;
            t.Stats.MinSsimulacra2   = Math.Min(t.Stats.MinSsimulacra2, ssim2);
            t.Stats.Ssimulacra2Scores.Add((float)ssim2);

            if (bargs.SkipButteraugli)
            {
                t.Stats.Distances.Add(0.0f);
                t.Stats.Pnorms.Add(0.0f);

                return;
            }

            ComputeButteraugliMetrics(bargs, t, origIo, decIo, compressedLen, innerPool);
        }

        private static void ComputeButteraugliMetrics(
            BenchmarkArgs bargs,
            BenchmarkTaskEntry t,
            CodecInOut origIo,
            CodecInOut decIo,
            int compressedLen,
            ThreadPoolInternal innerPool)
        {
            float intensityTarget = bargs.IntensityTarget > 0.0
                ? (float)bargs.IntensityTarget
                : origIo.Metadata.M.IntensityTarget();

            ButteraugliParams baParams = new ButteraugliParams
            {
                HfAsymmetry     = 0.8f,
                Xmul            = 1.0f,
                IntensityTarget = intensityTarget > 0.0f ? intensityTarget : 80.0f,
            };

            JxlButteraugliComparator comparator = new JxlButteraugliComparator(baParams, JxlCms.GetDefaultCms());
            float maxDistance                   = 0.0f;
            double distancePnorm                = 0.0;

            for (int f = 0; f < origIo.Frames.Count; f++)
            {
                ImageBundle ib0 = origIo.Frames[f];
                ImageBundle ib1 = decIo.Frames[f];

                if (ib0.XSize() != ib1.XSize() || ib0.YSize() != ib1.YSize())
                {
                    t.Stats.TotalErrors++;

                    return;
                }

                StatusOr<ImagePlane<float>> distmapOr = ImagePlane<float>.Create(
                    JxlMemoryManager.Default,
                    ib0.XSize(),
                    ib0.YSize());

                if (!distmapOr.IsOk)
                {
                    t.Stats.TotalErrors++;

                    return;
                }

                ImagePlane<float>? distmap = distmapOr.Value;

                try
                {
                    Status scoreSt = EncComparator.ComputeScore(
                        ib0,
                        ib1,
                        comparator,
                        JxlCms.GetDefaultCms(),
                        out float frameScore,
                        ref distmap,
                        (innerPool.Pool, false));

                    if (!scoreSt || distmap == null)
                    {
                        t.Stats.TotalErrors++;

                        return;
                    }

                    StatusOr<double> pnormOr = ExtrasMetrics.ComputeDistanceP(distmap, baParams, bargs.ErrorPnorm);
                    double framePnorm        = pnormOr.IsOk ? pnormOr.Value : 0.0;
                    nuint framePixels        = ib0.XSize() * ib0.YSize();
                    distancePnorm           += framePnorm * framePixels;
                    maxDistance              = Math.Max(maxDistance, frameScore);
                }
                finally
                {
                    distmap?.Dispose();
                }
            }

            t.Stats.DistancePNorm          += distancePnorm;
            t.Stats.MaxDistance             = Math.Max(t.Stats.MaxDistance, maxDistance);
            t.Stats.TotalAdjCompressedSize += (nuint)Math.Round(compressedLen * Math.Max(1.0f, maxDistance));
            t.Stats.Distances.Add(maxDistance);
            t.Stats.Pnorms.Add((float)(distancePnorm / Math.Max(1u, t.Stats.TotalInputPixels)));
        }

        private static void SaveOptionalArtifacts(
            BenchmarkArgs bargs,
            string codecDesc,
            string imageShortName,
            byte[] compressed,
            CodecInOut decIo,
            ThreadPoolInternal innerPool)
        {
            if (!bargs.SaveCompressed && !bargs.SaveDecompressed)
            {
                return;
            }

            string safeCodec = codecDesc.Replace(':', '_');
            Directory.CreateDirectory(bargs.OutputDir);

            if (bargs.SaveCompressed)
            {
                string compPath = Path.Combine(bargs.OutputDir, $"{imageShortName}.{safeCodec}");
                FileIo.WriteFile(compPath, compressed);
            }

            if (bargs.SaveDecompressed)
            {
                string decPath = Path.Combine(bargs.OutputDir, $"{imageShortName}.{safeCodec}{bargs.OutputExtension}");
                WriteCodecInOutToFile(decIo, bargs.ColorEncoding, bargs.OutputExtension, innerPool.Pool, decPath);
            }
        }

        private static void WriteCodecInOutToFile(
            CodecInOut io,
            ColorEncoding cDesired,
            string ext,
            ThreadPool? pool,
            string outPath)
        {
            JxlSharp.Extras.Encoder? enc = ExtrasEncodeFacade.FromExtension(ext);

            if (enc == null || enc.AcceptedFormats().Count == 0)
            {
                return;
            }

            JxlPixelFormat fmt  = enc.AcceptedFormats()[0];
            PackedPixelFile ppf = new PackedPixelFile();

            if (!PackedImageConvert.ConvertCodecInOutToPackedPixelFile(io, in fmt, cDesired, pool, ppf))
            {
                return;
            }

            EncodedImage encoded = new EncodedImage();

            if (enc.Encode(ppf, encoded, pool) && encoded.Bitstreams.Count > 0)
            {
                FileIo.WriteFile(outPath, encoded.Bitstreams[0]);
            }
        }

        private static int SummarizeAndPrintResults(
            BenchmarkArgs bargs,
            BenchmarkCorpus corpus,
            BenchmarkTaskEntry[] tasks,
            TextWriter outWriter,
            TextWriter errWriter)
        {
            int numMethods               = corpus.Methods.Count;
            int numImages                = corpus.Fnames.Count;
            List<List<ColumnValue>> rows = new List<List<ColumnValue>>(numMethods);
            ulong totalErrors            = 0;

            for (int m = 0; m < numMethods; m++)
            {
                BenchmarkStats methodStats = new BenchmarkStats();

                for (int idx = 0; idx < numImages; idx++)
                {
                    BenchmarkTaskEntry t = tasks[(m * numImages) + idx];
                    t.Codec.SummarizeStats(t.Stats);
                    PrintOptionalPerImageDetails(bargs, corpus.Fnames[idx], t, outWriter);
                    methodStats.Assimilate(t.Stats);
                }

                totalErrors += methodStats.TotalErrors;
                rows.Add(methodStats.ComputeColumns(corpus.Methods[m], (nuint)Math.Max(1, corpus.OuterThreads)));

                if (bargs.PrintMoreStats)
                {
                    tasks[m * numImages].Codec.PrintMoreStats(methodStats, outWriter);
                }
            }

            string table = BenchmarkStats.FormatTable(rows, bargs.PrintMoreStats, bargs.PrintDistancePercentiles);
            outWriter.Write(table);

            if (totalErrors > 0 && !bargs.SilentErrors)
            {
                errWriter.WriteLine($"Total errors: {totalErrors}");

                return 1;
            }

            return 0;
        }

        private static void PrintOptionalPerImageDetails(
            BenchmarkArgs bargs,
            string fname,
            BenchmarkTaskEntry t,
            TextWriter outWriter)
        {
            if (!bargs.PrintDetails && !bargs.PrintDetailsCsv)
            {
                return;
            }

            string shortName = Path.GetFileName(fname);
            double bpp       = t.Stats.TotalInputPixels > 0
                ? t.Stats.TotalCompressedSize * 8.0 / t.Stats.TotalInputPixels
                : 0.0;
            double pnorm     = t.Stats.Pnorms.Count > 0 ? t.Stats.Pnorms[0] : 0.0;
            double ssim2     = t.Stats.Ssimulacra2Scores.Count > 0 ? t.Stats.Ssimulacra2Scores[0] : 0.0;
            double psnr      = t.Stats.TotalInputPixels > 0 ? t.Stats.Psnr / t.Stats.TotalInputPixels : 0.0;

            if (bargs.PrintDetailsCsv)
            {
                outWriter.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5:F8},{6:F8},{7:F8},{8:F8},{9:F8}",
                        t.Codec.Description,
                        shortName,
                        t.Stats.TotalErrors,
                        t.Stats.TotalCompressedSize,
                        t.Stats.TotalInputPixels,
                        t.Stats.TotalTimeEncode,
                        t.Stats.TotalTimeDecode,
                        t.Stats.MaxDistance,
                        ssim2,
                        psnr));
            }
            else
            {
                outWriter.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-30} {1,10} {2,9:F4} {3,11:F8} {4,11:F8} {5,11:F8} {6,7:F2}",
                        shortName,
                        t.Stats.TotalCompressedSize,
                        bpp,
                        t.Stats.MaxDistance,
                        ssim2,
                        pnorm,
                        psnr));
            }
        }

        private static int WriteZoneplateImage(TextWriter outWriter, TextWriter errWriter)
        {
            const nuint N                  = 256;
            const nuint D                  = 2 * N;
            StatusOr<Image3<float>> imgOr  = Image3<float>.Create(JxlMemoryManager.Default, D, D);

            if (!imgOr.IsOk)
            {
                errWriter.WriteLine("Failed to allocate zoneplate image.");

                return 1;
            }

            using Image3<float> img = imgOr.Value;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < D; y++)
                {
                    Span<float> row = img.PlaneRow(c, y);
                    float dy        = (y > N ? (float)(y - N) : (float)(N - y)) / N;

                    for (nuint x = 0; x < D; x++)
                    {
                        float dx       = (x > N ? (float)(x - N) : (float)(N - x)) / N;
                        float r2       = (dx * dx) + (dy * dy);
                        row[(int)x]    = 0.5f + (0.5f * MathF.Cos(MathF.PI * r2 * 64.0f));
                    }
                }
            }

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = 3,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.BigEndian,
                Align       = 0,
            };

            StatusOr<PackedPixelFile> ppfOr = PackedImageConvert.ConvertImage3FToPackedPixelFile(
                img,
                ColorEncoding.SRGB(),
                fmt,
                null);

            if (!ppfOr.IsOk)
            {
                return 1;
            }

            JxlSharp.Extras.Encoder? pngEnc = CodecApng.GetAPNGEncoder();
            EncodedImage encoded            = new EncodedImage();

            if (pngEnc == null || !pngEnc.Encode(ppfOr.Value, encoded) || encoded.Bitstreams.Count == 0)
            {
                return 1;
            }

            FileIo.WriteFile("zoneplate.png", encoded.Bitstreams[0]);
            outWriter.WriteLine("Wrote zoneplate.png");

            return 0;
        }

        private sealed class BenchmarkCorpus
        {
            public readonly List<string> Methods;
            public readonly List<string> Fnames;
            public readonly int OuterThreads;
            public readonly int InnerThreads;
            public readonly List<byte[]> LoadedBytes;
            public readonly List<PackedPixelFile> LoadedPpfs;
            public readonly List<CodecInOut> LoadedImages;

            public BenchmarkCorpus(
                List<string> methods,
                List<string> fnames,
                int outerThreads,
                int innerThreads)
            {
                Methods      = methods;
                Fnames       = fnames;
                OuterThreads = outerThreads;
                InnerThreads = innerThreads;
                LoadedBytes  = new List<byte[]>(fnames.Count);
                LoadedPpfs   = new List<PackedPixelFile>(fnames.Count);
                LoadedImages = new List<CodecInOut>(fnames.Count);
            }
        }

        private sealed class BenchmarkTaskEntry
        {
            public readonly ImageCodec Codec;
            public readonly int IdxMethod;
            public readonly int IdxImage;
            public readonly BenchmarkStats Stats = new BenchmarkStats();

            public BenchmarkTaskEntry(ImageCodec codec, int idxMethod, int idxImage)
            {
                Codec     = codec;
                IdxMethod = idxMethod;
                IdxImage  = idxImage;
            }
        }
    }
}
