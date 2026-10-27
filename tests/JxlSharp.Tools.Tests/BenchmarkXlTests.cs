// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Unit tests for <see cref="ThreadPoolInternal"/>.
    /// </summary>
    [TestFixture]
    public class ThreadPoolInternalTests
    {
        /// <summary>
        /// Verifies that <see cref="ThreadPoolInternal"/> initializes default and explicit thread counts and disposes cleanly.
        /// </summary>
        [Test]
        public void ThreadPoolInternal_DefaultAndExplicitThreads_ConfigureRunnerAndPool()
        {
            using ThreadPoolInternal poolDefault = new ThreadPoolInternal(-1);

            poolDefault.Pool.ShouldNotBeNull();
            poolDefault.Runner.ShouldNotBeNull();
            poolDefault.RunnerOpaque.ShouldNotBeNull();
            poolDefault.NumWorkerThreads.ShouldBeGreaterThanOrEqualTo((nuint)0);

            using ThreadPoolInternal poolZero = new ThreadPoolInternal(0);

            poolZero.NumWorkerThreads.ShouldBe((nuint)0);
            poolZero.Dispose();
            poolZero.Dispose();

            Mock<IParallelRunner> mockRunner = new Mock<IParallelRunner>();

            mockRunner.SetupGet(r => r.Runner).Returns(poolDefault.Runner);
            mockRunner.SetupGet(r => r.RunnerOpaque).Returns(poolDefault.RunnerOpaque);
            mockRunner.Object.Runner.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="BenchmarkArgs"/>.
    /// </summary>
    [TestFixture]
    public class BenchmarkArgsTests
    {
        /// <summary>
        /// Verifies option registration, parsing, and validation in <see cref="BenchmarkArgs"/>.
        /// </summary>
        [Test]
        public void BenchmarkArgs_ParseAndValidate_AllOptionsWork()
        {
            BenchmarkArgs args   = new BenchmarkArgs();
            CommandLineParser cl = new CommandLineParser();

            args.AddCommandLineOptions(cl).IsOk.ShouldBeTrue();

            bool parsed = cl.Parse(
                new string[]
                {
                    "benchmark_xl",
                    "--input=*.png",
                    "--codec=jxl:wombat:d1,jxl:cheetah:d2",
                    "--print_details",
                    "--print_details_csv",
                    "--print_more_stats",
                    "--print_distance_percentiles",
                    "--silent_errors",
                    "--skip_butteraugli",
                    "--num_threads=2",
                    "--inner_threads=4",
                    "--encode_reps=3",
                    "--decode_reps=5",
                    "--error_pnorm=4.0",
                    "--intensity_target=250",
                    "-x", "color_space=sRGB",
                });

            parsed.ShouldBeTrue();
            args.Input.ShouldBe("*.png");
            args.Codec.ShouldBe("jxl:wombat:d1,jxl:cheetah:d2");
            args.PrintDetails.ShouldBeTrue();
            args.PrintDetailsCsv.ShouldBeTrue();
            args.PrintMoreStats.ShouldBeTrue();
            args.PrintDistancePercentiles.ShouldBeTrue();
            args.SilentErrors.ShouldBeTrue();
            args.SkipButteraugli.ShouldBeTrue();
            args.NumThreads.ShouldBe(2);
            args.InnerThreads.ShouldBe(4);
            args.EncodeReps.ShouldBe(3);
            args.DecodeReps.ShouldBe(5);
            args.ErrorPnorm.ShouldBe(4.0);
            args.IntensityTarget.ShouldBe(250.0);
            args.ValidateArgsAfterParse().IsOk.ShouldBeTrue();

            BenchmarkArgs invalidSave = new BenchmarkArgs { SaveCompressed = true, OutputDir = string.Empty };

            invalidSave.ValidateArgsAfterParse().IsOk.ShouldBeFalse();

            BenchmarkArgs invalidEnc = new BenchmarkArgs { OutputEncoding = "INVALID_ENCODING" };

            invalidEnc.ValidateArgsAfterParse().IsOk.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlStats"/>, <see cref="ColumnValue"/>, <see cref="ColumnDescriptor"/>, and <see cref="BenchmarkStats"/>.
    /// </summary>
    [TestFixture]
    public class BenchmarkStatsTests
    {
        /// <summary>
        /// Verifies assimilation, percentile computation, column calculation, and table formatting in <see cref="BenchmarkStats"/>.
        /// </summary>
        [Test]
        public void BenchmarkStats_AssimilateAndFormatTable_ProducesValidColumnsAndAggregate()
        {
            BenchmarkStats s1 = new BenchmarkStats
            {
                TotalInputFiles        = 1,
                TotalInputPixels       = 100000,
                TotalCompressedSize    = 25000,
                TotalAdjCompressedSize = 30000,
                TotalTimeEncode        = 0.05,
                TotalTimeDecode        = 0.02,
                MaxDistance            = 1.25,
                DistancePNorm          = 0.5 * 100000,
                Ssimulacra2            = 88.5 * 100000,
                MinSsimulacra2         = 88.5,
                Psnr                   = 42.0 * 100000,
                MaxAbpp                = 2.5,
            };

            s1.Distances.Add(1.25f);
            s1.Pnorms.Add(0.5f);
            s1.Ssimulacra2Scores.Add(88.5f);
            s1.ExtraChannelDistances.Add(0.25f);
            s1.JxlStats.NumInputs = 1;

            BenchmarkStats total = new BenchmarkStats();

            total.Assimilate(s1);
            total.TotalInputFiles.ShouldBe((nuint)1);
            total.TotalInputPixels.ShouldBe((nuint)100000);
            total.ExtraChannelDistances.Count.ShouldBe(1);

            List<ColumnValue> cols1 = total.ComputeColumns("jxl:wombat:d1", 1);
            List<ColumnValue> cols2 = total.ComputeColumns("jxl:cheetah:d2", 1);

            cols1.Count.ShouldBeGreaterThan(10);
            cols1[0].S.ShouldBe("jxl:wombat:d1");

            List<List<ColumnValue>> rows = new List<List<ColumnValue>> { cols1, cols2 };
            string table                 = BenchmarkStats.FormatTable(rows, printMoreStats: true, printDistancePercentiles: true);

            table.ShouldContain("jxl:wombat:d1");
            table.ShouldContain("jxl:cheetah:d2");
            table.ShouldContain("Aggregate:");

            BenchmarkStats.PrintHeader(15, false, false).ShouldContain("Encoding");
            BenchmarkStats.PrintRow(cols1, 15, false, false).ShouldContain("jxl:wombat:d1");
            BenchmarkStats.PrintAggregate(rows, 15, false, false).ShouldContain("Aggregate:");

            using StringWriter sw = new StringWriter();

            total.PrintMoreStats(sw);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="BenchmarkUtils"/>.
    /// </summary>
    [TestFixture]
    public class BenchmarkUtilsTests
    {
        /// <summary>
        /// Verifies string splitting, joining, glob matching, and file discovery in <see cref="BenchmarkUtils"/>.
        /// </summary>
        [Test]
        public void BenchmarkUtils_SplitJoinAndGlobMatch_WorkCorrectly()
        {
            List<string> parts = BenchmarkUtils.SplitString("a:b:c", ':');

            parts.Count.ShouldBe(3);
            BenchmarkUtils.JoinStrings(parts, ",").ShouldBe("a,b,c");
            BenchmarkUtils.SplitString(string.Empty, ':').ShouldBeEmpty();
            BenchmarkUtils.JoinStrings(Array.Empty<string>(), ",").ShouldBe(string.Empty);

            BenchmarkUtils.MatchPattern("*.png", "flower.png").ShouldBeTrue();
            BenchmarkUtils.MatchPattern("fl?wer.*", "flower.png").ShouldBeTrue();
            BenchmarkUtils.MatchPattern("*.jpg", "flower.png").ShouldBeFalse();

            string samplePath               = TestUtils.GetTestDataPath("jxl/flower/flower_small.rgb.depth8.ppm");
            StatusOr<List<string>> foundRes = BenchmarkUtils.GetMatchingFiles(samplePath);

            foundRes.IsOk.ShouldBeTrue();
            foundRes.Value.Count.ShouldBe(1);

            BenchmarkUtils.GetMatchingFiles(string.Empty).IsOk.ShouldBeFalse();
            BenchmarkUtils.GetMatchingFiles("nonexistent_dir_xyz/*.png").IsOk.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ImageCodec"/>, <see cref="JxlBenchmarkCodec"/>, <see cref="PngBenchmarkCodec"/>,
    /// <see cref="JpegBenchmarkCodec"/>, <see cref="NoneBenchmarkCodec"/>, and <see cref="BenchmarkXlTool"/>.
    /// </summary>
    [TestFixture]
    public class BenchmarkCodecsAndToolTests
    {
        /// <summary>
        /// Verifies parameter parsing and roundtrip encode/decode across <see cref="JxlBenchmarkCodec"/>,
        /// <see cref="PngBenchmarkCodec"/>, <see cref="NoneBenchmarkCodec"/>, and <see cref="JpegBenchmarkCodec"/>.
        /// </summary>
        [Test]
        public void BenchmarkCodecs_ParseParametersAndRoundtrip_Succeed()
        {
            BenchmarkArgs bargs = new BenchmarkArgs();

            ImageCodec? jxlCodec = ImageCodec.CreateImageCodec(
                "jxl:wombat:d1:D0:uint8:faster_decoding=2:epf=1:gab=1:p:g1:plt=16:C1:P5:I50:E2:R1:resampling=1:ec_resampling=1:downsampling=1:max_passes=2:noperc:n50:expert",
                bargs);

            jxlCodec.ShouldNotBeNull();

            ImageCodec? modCodec = ImageCodec.CreateImageCodec("jxl:m:d0:glacier:c0:lp:already_downsampled:br1.5:q90", bargs);

            modCodec.ShouldNotBeNull();

            ImageCodec.CreateImageCodec("unknown_codec", bargs).ShouldBeNull();
            ImageCodec.CreateImageCodec("jxl:invalid_token_xyz", bargs).ShouldBeNull();

            ImageCodec? pngCodec  = ImageCodec.CreateImageCodec("png", bargs);
            ImageCodec? noneCodec = ImageCodec.CreateImageCodec("none", bargs);
            ImageCodec? jpegCodec = ImageCodec.CreateImageCodec("jpeg:q90:p", bargs);

            pngCodec.ShouldNotBeNull();
            noneCodec.ShouldNotBeNull();
            jpegCodec.ShouldNotBeNull();

            CodecInOut io       = CreateTestCodecInOut(16, 16);
            SpeedStats encStats = new SpeedStats();
            SpeedStats decStats = new SpeedStats();

            using ThreadPoolInternal pool = new ThreadPoolInternal(0);

            pngCodec!.Compress("test.png", io, pool, out byte[] pngBytes, encStats).IsOk.ShouldBeTrue();
            CodecInOut pngDec = new CodecInOut();
            pngCodec.Decompress("test.png", pngBytes, pool, pngDec, decStats).IsOk.ShouldBeTrue();
            pngDec.Main.XSize().ShouldBe((nuint)16);

            noneCodec!.Compress("test.raw", io, pool, out byte[] noneBytes, encStats).IsOk.ShouldBeTrue();
            CodecInOut noneDec = new CodecInOut();
            noneCodec.Decompress("test.raw", noneBytes, pool, noneDec, decStats).IsOk.ShouldBeTrue();
            noneDec.Main.XSize().ShouldBe((nuint)16);
        }

        /// <summary>
        /// Verifies end-to-end execution of <see cref="BenchmarkXlTool.Run(string[], TextWriter?, TextWriter?)"/>.
        /// </summary>
        [Test]
        public void BenchmarkXlTool_RunEndToEnd_ProducesComparativeTable()
        {
            string inputPpm = TestUtils.GetTestDataPath("jxl/flower/flower_small.rgb.depth8.ppm");

            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();

            BenchmarkXlTool.Run(new string[] { "--help" }, outSw, errSw).ShouldBe(0);
            BenchmarkXlTool.Run(new string[] { "--data-usage" }, outSw, errSw).ShouldBe(0);

            outSw.GetStringBuilder().Clear();
            int rc = BenchmarkXlTool.Run(
                new string[]
                {
                    $"--input={inputPpm}",
                    "--codec=jxl:lightning:d1,jxl:cheetah:d2",
                    "--num_threads=1",
                    "--inner_threads=0",
                    "--encode_reps=1",
                    "--decode_reps=1",
                    "--print_details",
                    "--print_distance_percentiles",
                },
                outSw,
                errSw);

            rc.ShouldBe(0, errSw.ToString());
            string output = outSw.ToString();
            output.ShouldContain("jxl:lightning:d1");
            output.ShouldContain("jxl:cheetah:d2");
            output.ShouldContain("Aggregate:");
        }

        private static CodecInOut CreateTestCodecInOut(nuint width, nuint height)
        {
            CodecInOut io       = new CodecInOut();
            Image3<float> color = Image3<float>.Create(io.MemoryManager, width, height).Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < height; y++)
                {
                    Span<float> row = color.PlaneRow(c, y);

                    for (nuint x = 0; x < width; x++)
                    {
                        row[(int)x] = (float)(x + y) / (width + height);
                    }
                }
            }

            io.Metadata.M.SetUintSamples(8);
            io.Metadata.M.ColorEncoding = ColorEncoding.SRGB();
            io.SetFromImage(color, ColorEncoding.SRGB());

            return io;
        }
    }
}
