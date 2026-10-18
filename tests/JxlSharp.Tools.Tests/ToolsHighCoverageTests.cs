using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Tools
{
    /// <summary>
    /// High-coverage unit tests for <c>JxlSharp.Tools</c> classes including CLI tools,
    /// command-line parsers, memory tracking, speed statistics, SSIMULACRA metrics, and fuzzers.
    /// </summary>
    [TestFixture]
    public unsafe class ToolsHighCoverageTests
    {
        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "JxlToolsHighCov_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            return dir;
        }

        private static void CleanupTempDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup locks.
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="CmdLineParsers"/> numeric, boolean, string, override, and key-value parsing.
        /// </summary>
        [Test]
        public void CmdLineParsers_AllMethods_CoverValidAndInvalidInputs()
        {
            CmdLineParsers.ParseSigned("42", out int signedVal).ShouldBeTrue();
            signedVal.ShouldBe(42);
            CmdLineParsers.ParseSigned("-15", out signedVal).ShouldBeTrue();
            signedVal.ShouldBe(-15);
            CmdLineParsers.ParseSigned("0x1A", out signedVal).ShouldBeTrue();
            signedVal.ShouldBe(26);
            CmdLineParsers.ParseSigned("-0x10", out signedVal).ShouldBeTrue();
            signedVal.ShouldBe(-16);
            CmdLineParsers.ParseSigned("+0X20", out signedVal).ShouldBeTrue();
            signedVal.ShouldBe(32);
            CmdLineParsers.ParseSigned("", out _).ShouldBeFalse();
            CmdLineParsers.ParseSigned("abc", out _).ShouldBeFalse();
            CmdLineParsers.ParseSigned("999999999999999999999999999", out _).ShouldBeFalse();

            CmdLineParsers.ParseUnsigned("100", out nuint unsignedVal).ShouldBeTrue();
            unsignedVal.ShouldBe((nuint)100);
            CmdLineParsers.ParseUnsigned("+0x10", out unsignedVal).ShouldBeTrue();
            unsignedVal.ShouldBe((nuint)16);
            CmdLineParsers.ParseUnsigned("-1", out _).ShouldBeFalse();
            CmdLineParsers.ParseUnsigned("", out _).ShouldBeFalse();
            CmdLineParsers.ParseUnsigned("xyz", out _).ShouldBeFalse();
            CmdLineParsers.ParseUnsigned("999999999999999999999999999", out _).ShouldBeFalse();

            CmdLineParsers.ParseInt64("-9223372036854775808", out long i64Val).ShouldBeTrue();
            i64Val.ShouldBe(long.MinValue);
            CmdLineParsers.ParseInt64("12345", out i64Val).ShouldBeTrue();
            CmdLineParsers.ParseInt64("0x100", out i64Val).ShouldBeTrue();
            CmdLineParsers.ParseInt64("bad", out _).ShouldBeFalse();

            CmdLineParsers.ParseUint32("256", out uint u32Val).ShouldBeTrue();
            u32Val.ShouldBe(256u);
            CmdLineParsers.ParseUint32("0xFF", out u32Val).ShouldBeTrue();
            u32Val.ShouldBe(255u);
            CmdLineParsers.ParseUint32("-5", out _).ShouldBeFalse();
            CmdLineParsers.ParseUint32("999999999999", out _).ShouldBeFalse();

            CmdLineParsers.ParseFloat("1.5", out float fVal).ShouldBeTrue();
            fVal.ShouldBe(1.5f);
            CmdLineParsers.ParseFloat("bad", out _).ShouldBeFalse();

            CmdLineParsers.ParseDouble("2.75", out double dVal).ShouldBeTrue();
            dVal.ShouldBe(2.75);
            CmdLineParsers.ParseDouble("bad", out _).ShouldBeFalse();

            CmdLineParsers.ParseString("hello", out string strVal).ShouldBeTrue();
            strVal.ShouldBe("hello");

            CmdLineParsers.SetBooleanTrue(out bool bVal).ShouldBeTrue();
            bVal.ShouldBeTrue();
            CmdLineParsers.SetBooleanFalse(out bVal).ShouldBeTrue();
            bVal.ShouldBeFalse();

            CmdLineParsers.ParseOverride("1", out Override ovrVal).ShouldBeTrue();
            ovrVal.ShouldBe(Override.On);
            CmdLineParsers.ParseOverride("0", out ovrVal).ShouldBeTrue();
            ovrVal.ShouldBe(Override.Off);
            CmdLineParsers.ParseOverride("2", out _).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="ColorHintsProxy"/> and key-value parsing in <see cref="CmdLineParsers"/>.
        /// </summary>
        [Test]
        public void ColorHintsProxy_And_ParseAndAppendKeyValue_CoverAllBranches()
        {
            string tempDir        = CreateTempDir();
            ColorHintsProxy proxy = new ColorHintsProxy();
            string tempBlob       = Path.Combine(tempDir, "blob.bin");

            File.WriteAllBytes(tempBlob, new byte[] { 1, 2, 3, 4 });

            CmdLineParsers.ParseAndAppendKeyValue("color_space=sRGB", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue($"icc_pathname={tempBlob}", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue($"exif={tempBlob}", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue($"xmp={tempBlob}", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue($"jumbf={tempBlob}", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue("strip=1", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue($"exif={Path.Combine(tempDir, "missing.bin")}", proxy).ShouldBeFalse();
            CmdLineParsers.ParseAndAppendKeyValue("no_equals_sign", proxy).ShouldBeFalse();
            CmdLineParsers.ParseAndAppendKeyValue("k=v", (ColorHintsProxy)null!).ShouldBeFalse();
            CmdLineParsers.ParseAndAppendKeyValue("k=v", (Func<string, string, bool>)null!).ShouldBeFalse();
            proxy.Target.ShouldNotBeNull();
            CleanupTempDir(tempDir);
        }

        /// <summary>
        /// Verifies <see cref="CommandLineParser"/> option registration, parsing, help printing, and error cases.
        /// </summary>
        [Test]
        public void CommandLineParser_AllOverloadsAndEdgeCases_Succeed()
        {
            CommandLineParser parser = new CommandLineParser();
            StrongBox<string?> pos1  = new StrongBox<string?>();
            List<string> pos2List    = new List<string>();
            string strOpt            = "";
            int intOpt               = 0;
            bool flagOpt             = false;
            bool callbackFlag        = false;

            parser.AddHelpText("General help text", 0);
            parser.AddHelpText("Hidden expert help text", 2);

            int idPos1 = parser.AddPositionalOption("input", true, "Input file", pos1, 0);

            parser.AddPositionalOption("extra", false, "Extra positional", s => pos2List.Add(s), 1);

            int idStr = parser.AddOptionValue<string>('s', "str", "STR", "String option", v => strOpt = v, CmdLineParsers.ParseString, 0);

            parser.AddOptionValue<int>('i', "int", "INT", "Int option", v => intOpt = v, CmdLineParsers.ParseSigned, 0);
            parser.AddOptionFlag<bool>('f', "flag", "Bool flag", v => flagOpt = v, CmdLineParsers.SetBooleanTrue, 0);
            parser.AddOptionFlag<bool>('k', "kflag", "Callback flag", v => callbackFlag = v, CmdLineParsers.SetBooleanTrue, 2);
            parser.AddOptionFlag<bool>('v', "verbose", "Verbose flag", _ => { }, CmdLineParsers.SetBooleanTrue, 0);

            parser.Parse(new[] { "cjxl", "-s", "hello", "--int=42", "-f", "-k", "pos_one", "--", "-pos_two" }).ShouldBeTrue();
            parser.GetOption(idPos1).Matched.ShouldBeTrue();
            parser.GetOption(idStr).Matched.ShouldBeTrue();
            pos1.Value.ShouldBe("pos_one");
            pos2List.Count.ShouldBe(1);
            strOpt.ShouldBe("hello");
            intOpt.ShouldBe(42);
            flagOpt.ShouldBeTrue();
            callbackFlag.ShouldBeTrue();

            parser.PrintHelp();
            parser.Verbosity = 3;
            parser.PrintHelp();
            parser.VerbosePrintf(1, "Verbose output {0}\n", 123);
            parser.VerbosePrintf(5, "Suppressed output\n");

            parser.Parse(Array.Empty<string>()).ShouldBeTrue();
            parser.Parse(new[] { "cjxl", "-h" }).ShouldBeTrue();
            parser.HelpFlagPassed().ShouldBeTrue();
            parser.Parse(new[] { "cjxl", "-v", "--verbose" }).ShouldBeTrue();
            parser.Parse(new[] { "cjxl", "--flag=unexpected" }).ShouldBeFalse();
            parser.Parse(new[] { "cjxl", "-s" }).ShouldBeFalse();
            parser.Parse(new[] { "cjxl", "--str" }).ShouldBeFalse();
            parser.Parse(new[] { "cjxl", "--unknown_opt" }).ShouldBeFalse();
            parser.Parse(new[] { "cjxl", "-z" }).ShouldBeFalse();
            parser.Parse(new[] { "cjxl", "--int=not_an_int" }).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="CodecConfig"/>, <see cref="NoMemoryManager"/>, <see cref="TrackingMemoryManager"/>,
        /// <see cref="FileWrapper"/>, <see cref="FileIo"/>, and <see cref="SpeedStats"/>.
        /// </summary>
        [Test]
        public void ToolsCommon_UtilitiesMemoryFileAndSpeedStats_CoverAllBranches()
        {
            string tempDir = CreateTempDir();

            CodecConfig.CodecConfigString(0).ShouldNotBeEmpty();
            CodecConfig.CodecConfigString(1013000).ShouldContain("1.13.0");
            CodecConfig.CodecConfigString(0x000102).ShouldContain("0.1.2");

            NoMemoryManager.Get().ShouldBeSameAs(NoMemoryManager.Instance);
            NoMemoryManager.Instance.Alloc.ShouldNotBeNull();

            TrackingMemoryManager tmm = new TrackingMemoryManager(cap: 100, totalCap: 150);
            JxlMemoryManager outer    = tmm.Get();

            outer.Alloc.ShouldNotBeNull();
            outer.Free.ShouldNotBeNull();

            nint zeroBlock = outer.Alloc!(outer.Opaque, 0);

            outer.Free!(outer.Opaque, zeroBlock);

            nint block1 = outer.Alloc!(outer.Opaque, 80);

            block1.ShouldNotBe(nint.Zero);
            tmm.MaxBytesInUse.ShouldBe(80UL);
            tmm.TotalBytesAllocated.ShouldBe(80UL);
            tmm.TotalAllocations.ShouldBeGreaterThanOrEqualTo(1UL);

            // Exceed current cap (80 + 40 > 100)
            outer.Alloc!(outer.Opaque, 40).ShouldBe(nint.Zero);
            tmm.SeenOom.ShouldBeTrue();

            // Leak detection when Reset is called before freeing block1
            tmm.Reset().IsOk.ShouldBeFalse();

            outer.Free!(outer.Opaque, block1);
            outer.Free!(outer.Opaque, nint.Zero);
            tmm.Reset().IsOk.ShouldBeTrue();

            // Exceed totalCap (80 + 80 > 150)
            nint block2 = outer.Alloc!(outer.Opaque, 80);

            outer.Free!(outer.Opaque, block2);
            outer.Alloc!(outer.Opaque, 80).ShouldBe(nint.Zero);
            tmm.Reset().IsOk.ShouldBeTrue();

            // FileWrapper & FileIo
            FileWrapper emptyFw = new FileWrapper("", "rb");

            emptyFw.IsValid.ShouldBeFalse();
            emptyFw.Stream.ShouldBeNull();
            emptyFw.Size.ShouldBe(-1L);
            emptyFw.Dispose();

            string missingPath    = Path.Combine(tempDir, "does_not_exist.bin");
            FileWrapper missingFw = new FileWrapper(missingPath, "rb");

            missingFw.IsValid.ShouldBeFalse();
            missingFw.Dispose();

            FileIo.ReadFile(missingPath, out _).ShouldBeFalse();
            FileIo.WriteFile("", new byte[] { 1, 2 }).ShouldBeFalse();

            string validFile = Path.Combine(tempDir, "sub", "io.bin");

            Directory.CreateDirectory(Path.GetDirectoryName(validFile)!);
            FileIo.WriteFile(validFile, new byte[] { 10, 20, 30 }).ShouldBeTrue();
            FileIo.ReadFile(validFile, out byte[] readBack).ShouldBeTrue();
            readBack.Length.ShouldBe(3);

            // SpeedStats: 0, 1, 2, 3 normal, and 3 huge runs
            SpeedStats stats0 = new SpeedStats();

            stats0.Print(1).ShouldBeFalse();

            SpeedStats stats1 = new SpeedStats();

            stats1.SetImageSize(100, 100);
            stats1.SetFileSize(500);
            stats1.NotifyElapsed(0.01);
            stats1.NotifyElapsed(0.0);
            stats1.NotifyElapsed(-1.0);
            stats1.Print(2).ShouldBeTrue();

            SpeedStats stats2 = new SpeedStats();

            stats2.SetImageSize(100, 100);
            stats2.NotifyElapsed(0.02);
            stats2.NotifyElapsed(0.01);
            stats2.Print(1).ShouldBeTrue();

            SpeedStats stats3 = new SpeedStats();

            stats3.SetImageSize(100, 100);
            stats3.NotifyElapsed(0.01);
            stats3.NotifyElapsed(0.02);
            stats3.NotifyElapsed(0.03);
            stats3.Print(0).ShouldBeTrue();

            SpeedStats statsHuge = new SpeedStats();

            statsHuge.SetImageSize(10, 10);
            statsHuge.NotifyElapsed(1e150);
            statsHuge.NotifyElapsed(2e150);
            statsHuge.NotifyElapsed(3e150);
            statsHuge.Print(1).ShouldBeTrue();
            CleanupTempDir(tempDir);
        }

        /// <summary>
        /// Verifies <see cref="Ssimulacra"/>, <see cref="Ssimulacra2"/>, and <see cref="Msssim"/> computations.
        /// </summary>
        [Test]
        public void Ssimulacra_And_Ssimulacra2_ComputeMetricsAndDetails()
        {
            JxlMemoryManager mm      = NoMemoryManager.Get();
            using Image3<float> img1 = Image3<float>.Create(mm, 64, 64).Value!;
            using Image3<float> img2 = Image3<float>.Create(mm, 64, 64).Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 64; y++)
                {
                    float* row1 = img1.PlaneRow(c, y);
                    float* row2 = img2.PlaneRow(c, y);

                    for (nuint x = 0; x < 64; x++)
                    {
                        float val = ((x + y) % 64u) / 63.0f;

                        row1[x] = val;
                        row2[x] = val * 0.9f;
                    }
                }
            }

            StatusOr<Ssimulacra> ssimFull   = Ssimulacra.ComputeDiff(img1, img2, simple: false);
            StatusOr<Ssimulacra> ssimSimple = Ssimulacra.ComputeDiff(img1, img2, simple: true);

            ssimFull.IsOk.ShouldBeTrue();
            ssimSimple.IsOk.ShouldBeTrue();
            ssimFull.Value!.Score().ShouldBeGreaterThanOrEqualTo(0.0);
            ssimSimple.Value!.Score().ShouldBeGreaterThanOrEqualTo(0.0);
            ssimFull.Value.PrintDetails();

            ImageMetadata metadata = new ImageMetadata();

            metadata.SetFloat32Samples();
            metadata.ColorEncoding = ColorEncoding.SRGB();
            metadata.SetAlphaBits(8);

            using ImageBundle bundle1 = new ImageBundle(mm, metadata);
            using ImageBundle bundle2 = new ImageBundle(mm, metadata);

            bundle1.SetFromImage(img1, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            bundle2.SetFromImage(img2, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            ImagePlane<float> alpha1 = ImagePlane<float>.Create(mm, 64, 64).Value!;
            ImagePlane<float> alpha2 = ImagePlane<float>.Create(mm, 64, 64).Value!;

            ImageOps.FillImage(0.8f, alpha1);
            ImageOps.FillImage(0.75f, alpha2);
            bundle1.SetAlpha(alpha1);
            bundle2.SetAlpha(alpha2);

            StatusOr<Msssim> resDefault = Ssimulacra2.ComputeSSIMULACRA2(bundle1, bundle2);

            resDefault.IsOk.ShouldBeTrue();
            resDefault.Value!.Score().ShouldBeLessThanOrEqualTo(100.0);

            StatusOr<Msssim> resBg = Ssimulacra2.ComputeSSIMULACRA2(bundle1, bundle2, 0.5f);

            resBg.IsOk.ShouldBeTrue();
            resBg.Value!.Score().ShouldBeLessThanOrEqualTo(100.0);
        }

        /// <summary>
        /// Verifies <see cref="DjxlFuzzer"/> entry points and <c>DjxlFuzzer.DecodeJpegXl</c> configurations.
        /// </summary>
        [Test]
        public void DjxlFuzzer_AllEntryPointsAndFuzzSpecs_ExecuteCleanly()
        {
            DjxlFuzzer.TestOneInput(Array.Empty<byte>());
            DjxlFuzzer.TestOneInput(new byte[] { 1, 2 });
            DjxlFuzzer.DoTestOneInput(ReadOnlySpan<byte>.Empty).ShouldBe(0);
            DjxlFuzzer.DoTestOneInput((byte*)null, 2).ShouldBe(0);

            byte[] richContainerJxl = CreateRichContainerJxl(includePreviewAndAlpha: false);
            byte[] fuzzerInput      = new byte[richContainerJxl.Length + 4];

            Buffer.BlockCopy(richContainerJxl, 0, fuzzerInput, 0, richContainerJxl.Length);
            fuzzerInput[richContainerJxl.Length + 0] = 0x55;
            fuzzerInput[richContainerJxl.Length + 1] = 0x03;
            fuzzerInput[richContainerJxl.Length + 2] = 0x01;
            fuzzerInput[richContainerJxl.Length + 3] = 0x00;

            DjxlFuzzer.TestOneInput(fuzzerInput);

            fixed (byte* ptr = fuzzerInput)
            {
                DjxlFuzzer.DoTestOneInput(ptr, (nuint)fuzzerInput.Length).ShouldBe(0);
            }

            TrackingMemoryManager tmm  = new TrackingMemoryManager(cap: 50 * 1024 * 1024, totalCap: 500 * 1024 * 1024);
            FuzzDecodeResult res1      = new FuzzDecodeResult();
            FuzzSpec specCallbackBoxes = new FuzzSpec
            {
                OutputAlign      = 2,
                OutputEndianness = JxlEndianness.LittleEndian,
                OutputType       = JxlDataType.UInt16,
                GetAlpha         = true,
                GetGrayscale     = false,
                UseStreaming     = false,
                JpegToPixels     = true,
                Coalescing       = true,
                UseCallback      = true,
                KeepOrientation  = true,
                DecodeBoxes      = true,
                RandomSeed       = 42,
            };

            DjxlFuzzer.DecodeJpegXl(
                richContainerJxl,
                tmm.Get(),
                1u << 20,
                5u << 20,
                in specCallbackBoxes,
                res1).ShouldBeTrue();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            tmm.Reset().IsOk.ShouldBeTrue();
            res1.XSize.ShouldBeGreaterThan(0u);
            res1.YSize.ShouldBeGreaterThan(0u);

            FuzzDecodeResult res2   = new FuzzDecodeResult();
            FuzzSpec specBufferJbrd = new FuzzSpec
            {
                OutputAlign      = 1,
                OutputEndianness = JxlEndianness.BigEndian,
                OutputType       = JxlDataType.UInt8,
                GetAlpha         = false,
                GetGrayscale     = true,
                UseStreaming     = false,
                JpegToPixels     = false,
                Coalescing       = true,
                UseCallback      = false,
                KeepOrientation  = false,
                DecodeBoxes      = false,
                RandomSeed       = 7,
            };

            DjxlFuzzer.DecodeJpegXl(
                CreateGrayscaleJxl(),
                tmm.Get(),
                1u << 20,
                5u << 20,
                in specBufferJbrd,
                res2).ShouldBeTrue();
            res2.Pixels.ShouldNotBeEmpty();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            tmm.Reset().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="CjxlTool"/> option parsing, validation errors, and encoding workflows.
        /// </summary>
        [Test]
        public void CjxlTool_OptionsValidationAndEncoding_CoverAllPaths()
        {
            string tempDir      = CreateTempDir();
            string inputPpm     = Path.Combine(tempDir, "in.ppm");
            string outputJxl    = Path.Combine(tempDir, "sub_out", "out.jxl");
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();

            File.WriteAllBytes(inputPpm, CreateSamplePpm(8, 8));

            // 1-arg overload & version/help
            CjxlTool.Run(new[] { "--version" }).ShouldBe(0);
            CjxlTool.Run(new[] { "--help" }, stdout, stderr).ShouldBe(0);
            CjxlTool.Run(Array.Empty<string>(), stdout, stderr).ShouldBe(0);

            // Successful encode with rich flags
            int rc = CjxlTool.Run(
                new[]
                {
                    inputPpm,
                    outputJxl,
                    "-d", "1.0",
                    "-a", "0.0",
                    "-e", "3",
                    "-p",
                    "--container=1",
                    "--compress_boxes=1",
                    "--keep_invisible=1",
                    "--dots=0",
                    "--patches=0",
                    "--gaborish=1",
                    "--group_order=0",
                    "--progressive_ac=1",
                    "--qprogressive_ac=0",
                    "--progressive_dc=1",
                    "--photon_noise_iso=50",
                    "--intensity_target=250",
                    "--brotli_effort=3",
                    "--epf=1",
                    "--faster_decoding=1",
                    "--resampling=1",
                    "--ec_resampling=1",
                    "--responsive=0",
                    "--num_threads=1",
                    "--num_reps=1",
                    "--codestream_level=5",
                    "--override_bitdepth=8",
                    "--upsampling_mode=1",
                    "--premultiply=-1",
                    "--strip_alpha=0",
                    "--center_x=2",
                    "--center_y=2",
                    "--buffering=0",
                    "--already_downsampled",
                    "--streaming_input",
                    "--streaming_output",
                    "-x", "color_space=sRGB",
                    "-v",
                },
                stdout,
                stderr);

            rc.ShouldBe(0);
            File.Exists(outputJxl).ShouldBeTrue();

            // Modular + quality + disable_output + quiet + expert effort 10
            CjxlTool.Run(
                new[]
                {
                    inputPpm,
                    "--disable_output",
                    "--quiet",
                    "-m", "true",
                    "-q", "95",
                    "--allow_expert_options",
                    "-e", "10",
                    "--lossless_jpeg=0",
                    "--jpeg_reconstruction_cfl=default",
                },
                stdout,
                stderr).ShouldBe(0);

            // Quality < 100 non-modular branch
            CjxlTool.Run(new[] { inputPpm, "--disable_output", "-q", "85" }, stdout, stderr).ShouldBe(0);
            CjxlTool.Run(new[] { inputPpm, "--disable_output", "-q", "100" }, stdout, stderr).ShouldBe(0);

            // Error branches in CjxlTool
            string[][] errorCases =
            {
                new[] { inputPpm },
                new[] { inputPpm, outputJxl, "extra_arg" },
                new[] { Path.Combine(tempDir, "missing.ppm"), outputJxl },
                new[] { inputPpm, outputJxl, "-d", "1.0", "-q", "90" },
                new[] { inputPpm, outputJxl, "-d", "-1" },
                new[] { inputPpm, outputJxl, "-d", "30" },
                new[] { inputPpm, outputJxl, "-d", "bad" },
                new[] { inputPpm, outputJxl, "-a", "bad" },
                new[] { inputPpm, outputJxl, "-q", "bad" },
                new[] { inputPpm, outputJxl, "-e", "0" },
                new[] { inputPpm, outputJxl, "-e", "11" },
                new[] { inputPpm, outputJxl, "-e", "bad" },
                new[] { inputPpm, outputJxl, "--brotli_effort=15" },
                new[] { inputPpm, outputJxl, "--epf=9" },
                new[] { inputPpm, outputJxl, "--faster_decoding=9" },
                new[] { inputPpm, outputJxl, "--resampling=3" },
                new[] { inputPpm, outputJxl, "--ec_resampling=3" },
                new[] { inputPpm, outputJxl, "--progressive_dc=5" },
                new[] { inputPpm, outputJxl, "--modular=invalid_override" },
                new[] { inputPpm, outputJxl, "--photon_noise_iso=bad" },
                new[] { inputPpm, outputJxl, "--num_threads=bad" },
                new[] { inputPpm, outputJxl, "--center_x=bad" },
                new[] { inputPpm, outputJxl, "-x", "bad_hint_without_equals" },
                new[] { inputPpm, outputJxl, "--unknown_flag" },
            };

            foreach (string[] errArgs in errorCases)
            {
                CjxlTool.Run(errArgs, stdout, stderr).ShouldBe(1);
            }

            // JPEG input branches (-j 1 + -d error, and -j 1 fallback on minimal JPEG header)
            string fakeJpg = Path.Combine(tempDir, "fake.jpg");

            File.WriteAllBytes(fakeJpg, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            CjxlTool.Run(new[] { fakeJpg, outputJxl, "-j", "1", "-d", "1.0" }, stdout, stderr).ShouldBe(1);
            CjxlTool.Run(new[] { fakeJpg, outputJxl, "-j", "1" }, stdout, stderr).ShouldBe(1);
            CleanupTempDir(tempDir);
        }

        /// <summary>
        /// Verifies <see cref="DjxlTool"/> option parsing, output formats, metadata extraction, and error handling.
        /// </summary>
        [Test]
        public void DjxlTool_OptionsFormatsAndErrors_CoverAllPaths()
        {
            string tempDir      = CreateTempDir();
            string jxlPath      = Path.Combine(tempDir, "rich.jxl");
            byte[] richBytes    = CreateRichContainerJxl(includePreviewAndAlpha: true, includeExtraInfoBoxes: false, multiFrame: false);
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();

            File.WriteAllBytes(jxlPath, richBytes);

            DjxlTool.Run(new[] { "--version" }).ShouldBe(0);
            DjxlTool.Run(new[] { "--help" }, stdout, stderr).ShouldBe(0);

            // Decode without output file (benchmark/validation mode)
            DjxlTool.Run(
                new[]
                {
                    jxlPath,
                    "--num_reps=1",
                    "--num_threads=1",
                    "--print_read_bytes",
                    "--quiet",
                },
                stdout,
                stderr).ShouldBe(0);

            // Decode to various supported output formats and sidecars
            string outPpm  = Path.Combine(tempDir, "out.ppm");
            string outPgm  = Path.Combine(tempDir, "out.pgm");
            string outPam  = Path.Combine(tempDir, "out.pam");
            string outPfm  = Path.Combine(tempDir, "out.pfm");
            string outPgx  = Path.Combine(tempDir, "out.pgx");
            string outPng  = Path.Combine(tempDir, "out.png");
            string outNpy  = Path.Combine(tempDir, "out.npy");
            string prevOut = Path.Combine(tempDir, "prev.ppm");
            string iccOut  = Path.Combine(tempDir, "out.icc");
            string origIcc = Path.Combine(tempDir, "orig.icc");
            string metaOut = Path.Combine(tempDir, "meta.json");

            DjxlTool.Run(
                new[]
                {
                    jxlPath,
                    outPng,
                    "--alpha_blend=black",
                    "--bits_per_sample=8",
                    "--display_nits=250",
                    "--downsampling=1",
                    "--num_passes=1",
                    "--render_spotcolors=1",
                    "--coalescing=1",
                    "--unpremultiply_alpha",
                    "--allow_partial_files",
                    "--allow_more_progressive_steps",
                    "--use_sjpeg",
                    "--jpeg_quality=90",
                    $"--preview_out={prevOut}",
                    $"--icc_out={iccOut}",
                    $"--orig_icc_out={origIcc}",
                    $"--metadata_out={metaOut}",
                },
                stdout,
                stderr).ShouldBe(0);

            DjxlTool.Run(new[] { jxlPath, outPpm, "--alpha_blend=white", "-j" }, stdout, stderr).ShouldBe(0);
            DjxlTool.Run(new[] { jxlPath, outPam, "--output_frames", "--output_extra_channels" }, stdout, stderr).ShouldBe(0);
            DjxlTool.Run(new[] { jxlPath, outPfm, "--alpha_blend=black", "--bits_per_sample=32", "--color_space=RGB_D65_SRG_Rel_Lin" }, stdout, stderr).ShouldBe(0);
            DjxlTool.Run(new[] { jxlPath, outNpy }, stdout, stderr).ShouldBe(0);

            // Grayscale JXL for PGM and PGX output
            string grayJxl = Path.Combine(tempDir, "gray.jxl");

            File.WriteAllBytes(grayJxl, CreateGrayscaleJxl());
            DjxlTool.Run(new[] { grayJxl, outPgm }, stdout, stderr).ShouldBe(0);
            DjxlTool.Run(new[] { grayJxl, outPgx }, stdout, stderr).ShouldBe(0);

            // Error cases
            DjxlTool.Run(Array.Empty<string>(), stdout, stderr).ShouldBe(1);
            DjxlTool.Run(new[] { Path.Combine(tempDir, "missing.jxl") }, stdout, stderr).ShouldBe(1);
            DjxlTool.Run(new[] { jxlPath, outPpm, "third_arg" }, stdout, stderr).ShouldBe(1);
            DjxlTool.Run(new[] { jxlPath, outPpm, "--alpha_blend=invalid" }, stdout, stderr).ShouldBe(1);
            DjxlTool.Run(new[] { jxlPath, outPpm, "--num_threads=bad" }, stdout, stderr).ShouldBe(1);
            DjxlTool.Run(new[] { jxlPath, Path.Combine(tempDir, "out.bmp") }, stdout, stderr).ShouldBe(1);
            CleanupTempDir(tempDir);
        }

        /// <summary>
        /// Verifies <see cref="JxlInfoTool"/> on codestreams, rich containers, animations, and invalid files.
        /// </summary>
        [Test]
        public void JxlInfoTool_ContainerBoxesAnimationAndErrors_CoverAllPaths()
        {
            string tempDir      = CreateTempDir();
            string richJxl      = Path.Combine(tempDir, "info_rich.jxl");
            string grayJxl      = Path.Combine(tempDir, "info_gray.jxl");
            string badFile      = Path.Combine(tempDir, "bad.jxl");
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();

            File.WriteAllBytes(richJxl, CreateRichContainerJxl(includePreviewAndAlpha: true, includeExtraInfoBoxes: true));
            File.WriteAllBytes(grayJxl, CreateGrayscaleJxl());
            File.WriteAllBytes(badFile, new byte[] { 1, 2, 3, 4, 5 });

            JxlInfoTool.Run(new[] { "--help" }, stdout, stderr).ShouldBe(0);
            JxlInfoTool.Run(Array.Empty<string>(), stdout, stderr).ShouldBe(1);
            JxlInfoTool.Run(new[] { "--unknown_opt" }, stdout, stderr).ShouldBe(1);
            JxlInfoTool.Run(new[] { "-v", richJxl, grayJxl }, stdout, stderr).ShouldBe(0);
            stdout.ToString().ShouldContain("JPEG XL");

            JxlInfoTool.Run(new[] { Path.Combine(tempDir, "missing.jxl") }, stdout, stderr).ShouldBe(1);
            JxlInfoTool.Run(new[] { badFile }, stdout, stderr).ShouldBe(1);
            CleanupTempDir(tempDir);
        }

        /// <summary>
        /// Verifies <see cref="JxlTranTool"/> pack, extract, box manipulation, and validation paths.
        /// </summary>
        [Test]
        public void JxlTranTool_PackExtractModifyBoxesAndErrors_CoverAllPaths()
        {
            string tempDir        = CreateTempDir();
            string codestreamPath = Path.Combine(tempDir, "cs.jxl");
            string packedPath     = Path.Combine(tempDir, "packed.jxl");
            string extractedPath  = Path.Combine(tempDir, "extracted.jxl");
            string richPath       = Path.Combine(tempDir, "rich_tran.jxl");
            string modifiedPath   = Path.Combine(tempDir, "modified_tran.jxl");
            string extractedRich  = Path.Combine(tempDir, "extracted_rich.jxl");
            string boxPayloadPath = Path.Combine(tempDir, "box_payload.bin");
            StringWriter stdout   = new StringWriter();
            StringWriter stderr   = new StringWriter();

            File.WriteAllBytes(codestreamPath, CreateGrayscaleJxl());
            File.WriteAllBytes(richPath, CreateRichContainerJxl(includePreviewAndAlpha: false, includeExtraInfoBoxes: true));
            File.WriteAllBytes(boxPayloadPath, new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 });

            JxlTranTool.Run(new[] { "--version" }).ShouldBe(0);
            JxlTranTool.Run(new[] { "--help" }, stdout, stderr).ShouldBe(0);

            // Pack raw codestream into container with added uncompressed and brotli boxes
            JxlTranTool.Run(
                new[]
                {
                    "-p",
                    $"-a=Exif={boxPayloadPath}",
                    $"-a=*xml ={boxPayloadPath}",
                    codestreamPath,
                    packedPath,
                },
                stdout,
                stderr).ShouldBe(0);
            File.Exists(packedPath).ShouldBeTrue();

            // Extract codestream back from packed container
            JxlTranTool.Run(new[] { "-x", "-q", packedPath, extractedPath }, stdout, stderr).ShouldBe(0);
            File.Exists(extractedPath).ShouldBeTrue();

            // Extract from rich container with jxll (level > 5) and jxlp partial codestream boxes
            JxlTranTool.Run(new[] { "-x", richPath, extractedRich }, stdout, stderr).ShouldBe(0);

            // Transform container: remove Exif (including brob-wrapped Exif) and add a new box
            JxlTranTool.Run(
                new[]
                {
                    "-r=Exif",
                    $"-a=jumb={boxPayloadPath}",
                    richPath,
                    modifiedPath,
                },
                stdout,
                stderr).ShouldBe(0);

            // Validation & error paths
            string badFile = Path.Combine(tempDir, "not_jxl.bin");

            File.WriteAllBytes(badFile, new byte[] { 9, 9, 9, 9 });

            string[][] errorCases =
            {
                Array.Empty<string>(),
                new[] { codestreamPath },
                new[] { codestreamPath, packedPath, "extra_arg" },
                new[] { "--unknown", codestreamPath, packedPath },
                new[] { "-x", "-p", packedPath, extractedPath },
                new[] { "-x", "-r=Exif", packedPath, extractedPath },
                new[] { "-r=BadLen", packedPath, modifiedPath },
                new[] { "-a=BadSpecWithoutEquals", packedPath, modifiedPath },
                new[] { "-a=BadTypeLen=file", packedPath, modifiedPath },
                new[] { $"-a=Exif={Path.Combine(tempDir, "missing_box.bin")}", packedPath, modifiedPath },
                new[] { Path.Combine(tempDir, "missing_in.jxl"), modifiedPath },
                new[] { badFile, modifiedPath },
                new[] { "-p", packedPath, modifiedPath },
                new[] { "-x", codestreamPath, extractedPath },
            };

            foreach (string[] errArgs in errorCases)
            {
                JxlTranTool.Run(errArgs, stdout, stderr).ShouldBe(1);
            }

            CleanupTempDir(tempDir);
        }

        private static byte[] CreateSamplePpm(int width, int height)
        {
            string header = $"P6\n{width} {height}\n255\n";
            byte[] hBytes = Encoding.ASCII.GetBytes(header);
            byte[] result = new byte[hBytes.Length + (width * height * 3)];

            Buffer.BlockCopy(hBytes, 0, result, 0, hBytes.Length);

            for (int i = hBytes.Length; i < result.Length; i++)
            {
                result[i] = (byte)((i * 37) & 0xFF);
            }

            return result;
        }

        private static byte[] CreateGrayscaleJxl()
        {
            PackedPixelFile ppf       = new PackedPixelFile();
            ppf.Info.XSize            = 8;
            ppf.Info.YSize            = 8;
            ppf.Info.BitsPerSample    = 8;
            ppf.Info.NumColorChannels = 1;
            ppf.ColorEncoding         = ColorEncoding.SRGB(isGray: true).ToExternal();

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = 1,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            StatusOr<PackedFrame> frameRes = PackedFrame.Create(8, 8, in fmt);
            Span<byte> pixels              = frameRes.Value.Color.Pixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 3);
            }

            ppf.Frames.Add(frameRes.Value);
            JXLCompressParams cparams = new JXLCompressParams { Distance = 1.0f };

            CodecJxl.EncodeImageJXL(cparams, ppf, null, out byte[] compressed);

            return compressed;
        }

        private static byte[] CreateRichContainerJxl(bool includePreviewAndAlpha, bool includeExtraInfoBoxes = false, bool multiFrame = true)
        {
            PackedPixelFile ppf            = new PackedPixelFile();
            ppf.Info.XSize                 = 8;
            ppf.Info.YSize                 = 8;
            ppf.Info.BitsPerSample         = 8;
            ppf.Info.ExponentBitsPerSample = 0;
            ppf.Info.NumColorChannels      = 3;
            ppf.Info.Orientation           = JxlOrientation.Identity;
            ppf.Info.IntensityTarget       = 400.0f;
            ppf.Info.MinNits               = 0.1f;
            ppf.Info.LinearBelow           = 1.0f;
            ppf.Info.HaveAnimation         = multiFrame;
            ppf.Info.Animation             = new JxlAnimationHeader
            {
                TpsNumerator   = 10,
                TpsDenominator = 1,
                NumLoops       = 2,
                HaveTimecodes  = false,
            };

            if (includePreviewAndAlpha)
            {
                ppf.Info.AlphaBits          = 8;
                ppf.Info.AlphaExponentBits  = 0;
                ppf.Info.AlphaPremultiplied = false;
                ppf.Info.NumExtraChannels   = 1;
            }

            ppf.ColorEncoding = ColorEncoding.SRGB().ToExternal();

            JxlPixelFormat fmt = new JxlPixelFormat
            {
                NumChannels = includePreviewAndAlpha ? 4u : 3u,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            StatusOr<PackedFrame> f1 = PackedFrame.Create(8, 8, in fmt);

            f1.Value.Name               = "frame_one";
            f1.Value.FrameInfo.Duration = multiFrame ? 5u : 0u;
            f1.Value.FrameInfo.IsLast   = !multiFrame;
            ppf.Frames.Add(f1.Value);

            if (multiFrame)
            {
                StatusOr<PackedFrame> f2 = PackedFrame.Create(8, 8, in fmt);

                f2.Value.FrameInfo.Duration = 5u;
                f2.Value.FrameInfo.IsLast   = true;
                ppf.Frames.Add(f2.Value);
            }

            JXLCompressParams cparams = new JXLCompressParams
            {
                Distance     = 1.0f,
                UseContainer = false,
            };

            CodecJxl.EncodeImageJXL(cparams, ppf, null, out byte[] cs).ShouldBeTrue();

            using MemoryStream ms = new MemoryStream();

            ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A });
            ms.Write(new byte[] { 0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'j', (byte)'x', (byte)'l', (byte)' ', 0, 0, 0, 0, (byte)'j', (byte)'x', (byte)'l', (byte)' ' });
            ms.Write(new byte[] { 0, 0, 0, 9, (byte)'j', (byte)'x', (byte)'l', (byte)'l', 10 });

            byte[] exifPayload = new byte[16];

            if (includeExtraInfoBoxes)
            {
                WriteBox64(ms, "Exif", exifPayload);
            }
            else
            {
                WriteBox32(ms, "Exif", exifPayload);
            }

            byte[] xmlPayload = Encoding.ASCII.GetBytes("<xmp>test</xmp>");

            WriteBox32(ms, "xml ", xmlPayload);

            WriteBox32(ms, "jumb", new byte[] { 1, 2, 3, 4 });

            if (includeExtraInfoBoxes)
            {
                byte[] brobPayload = new byte[] { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 1, 2, 3, 4 };

                WriteBox32(ms, "brob", brobPayload);
                WriteBox32(ms, "jbrd", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            }

            // Valid jhgm box via GainMapCodec
            JxlGainMapBundle gmBundle = new JxlGainMapBundle
            {
                JhgmVersion      = 0,
                GainMapMetadata  = new byte[] { 1, 2 },
                HasColorEncoding = true,
                ColorEncoding    = ColorEncoding.SRGB().ToExternal(),
                AltIcc           = Array.Empty<byte>(),
                GainMap          = new byte[] { 0xFF, 0x0A },
            };

            if (GainMapCodec.JxlGainMapGetBundleSize(in gmBundle, out nuint gmSize) && gmSize > 0)
            {
                byte[] gmBytes = new byte[gmSize];

                GainMapCodec.JxlGainMapWriteBundle(in gmBundle, gmBytes, out nuint gmWritten);
                WriteBox32(ms, "jhgm", gmBytes.AsSpan(0, (int)gmWritten).ToArray());
            }

            if (includeExtraInfoBoxes)
            {
                WriteBox32(ms, "jhgm", new byte[] { 0xFF });

                // Split codestream into 2 jxlp boxes; second box uses size32 == 0 (to EOF)
                int half     = cs.Length / 2;
                byte[] jxlp1 = new byte[4 + half];

                BinaryPrimitives.WriteUInt32BigEndian(jxlp1.AsSpan(0, 4), 0u);
                Buffer.BlockCopy(cs, 0, jxlp1, 4, half);
                WriteBox32(ms, "jxlp", jxlp1);

                int rem         = cs.Length - half;
                byte[] jxlp2Hdr = new byte[8];

                BinaryPrimitives.WriteUInt32BigEndian(jxlp2Hdr.AsSpan(0, 4), 0u);
                Encoding.ASCII.GetBytes("jxlp", jxlp2Hdr.AsSpan(4, 4));
                ms.Write(jxlp2Hdr);

                byte[] jxlp2Idx = new byte[4];

                BinaryPrimitives.WriteUInt32BigEndian(jxlp2Idx, 0x80000001u);
                ms.Write(jxlp2Idx);
                ms.Write(cs, half, rem);
            }
            else
            {
                WriteBox32(ms, "jxlc", cs);
            }

            return ms.ToArray();
        }

        private static void WriteBox32(Stream s, string type, byte[] payload)
        {
            Span<byte> hdr = stackalloc byte[8];

            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), (uint)(8 + payload.Length));
            Encoding.ASCII.GetBytes(type, hdr.Slice(4, 4));
            s.Write(hdr);
            s.Write(payload);
        }

        private static void WriteBox64(Stream s, string type, byte[] payload)
        {
            Span<byte> hdr = stackalloc byte[16];

            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), 1u);
            Encoding.ASCII.GetBytes(type, hdr.Slice(4, 4));
            BinaryPrimitives.WriteUInt64BigEndian(hdr.Slice(8, 8), (ulong)(16 + payload.Length));
            s.Write(hdr);
            s.Write(payload);
        }
    }
}
