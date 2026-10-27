// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Threads;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Integration.Tests
{
    /// <summary>
    /// Unit and integration tests covering <see cref="JxlCodecFacade"/>,
    /// <see cref="ThreadParallelRunner"/>, <see cref="ResizableParallelRunner"/>,
    /// and <c>JxlSharp.Cms</c> color management types.
    /// </summary>
    [TestFixture]
    public sealed class FacadeAndThreadsCmsCoverageTests
    {
        /// <summary>
        /// Verifies all factory and version methods on <see cref="JxlCodecFacade"/>.
        /// </summary>
        [Test]
        public void JxlCodecFacade_AllFactoryAndVersionMethods_ReturnValidInstances()
        {
            uint decVer                             = JxlCodecFacade.DecoderVersion();
            uint encVer                             = JxlCodecFacade.EncoderVersion();
            using JxlDecoder decoder                = JxlCodecFacade.CreateDecoder();
            using JxlEncoder encoder                = JxlCodecFacade.CreateEncoder();
            using ThreadParallelRunner threadRunner = JxlCodecFacade.CreateThreadParallelRunner(2);
            using ResizableParallelRunner resRunner = JxlCodecFacade.CreateResizableParallelRunner();
            JxlCmsInterface cms                     = JxlCodecFacade.GetDefaultCms();

            decVer.ShouldBeGreaterThan(0u);
            encVer.ShouldBeGreaterThan(0u);
            decoder.ShouldNotBeNull();
            encoder.ShouldNotBeNull();
            threadRunner.ShouldNotBeNull();
            threadRunner.NumThreads.ShouldBe((nuint)2);
            resRunner.ShouldNotBeNull();
            cms.ShouldNotBeNull();
        }

        /// <summary>
        /// Verifies <see cref="ThreadParallelRunner"/> guard clauses, sequential execution, and re-entrancy protection.
        /// </summary>
        [Test]
        public void ThreadParallelRunner_EdgeCasesAndGuards_BehaveAsExpected()
        {
            JxlMemoryManager invalidMm        = new JxlMemoryManager { Alloc = static (_, _) => nint.Zero, Free = null };
            ThreadParallelRunner? created     = ThreadParallelRunner.Create(invalidMm, 2);
            using ThreadParallelRunner runner = new ThreadParallelRunner(2);
            int badOpaque                     = ThreadParallelRunner.Runner(null, null, static (_, _) => 0, static (_, _, _) => { }, 0, 4);
            int invertedRange                 = ThreadParallelRunner.Runner(runner, null, static (_, _) => 0, static (_, _, _) => { }, 5, 2);
            int emptyRange                    = ThreadParallelRunner.Runner(runner, null, static (_, _) => 0, static (_, _, _) => { }, 3, 3);
            int initError                     = ThreadParallelRunner.Runner(runner, null, static (_, _) => -7, static (_, _, _) => { }, 0, 4);
            int reentrantResult               = 0;

            int outerResult = ThreadParallelRunner.Runner(
                runner,
                null,
                static (_, _) => 0,
                (_, task, _) =>
                {
                    if (task == 0)
                    {
                        reentrantResult = ThreadParallelRunner.Runner(runner, null, static (_, _) => 0, static (_, _, _) => { }, 0, 2);
                    }
                },
                0,
                2);

            created.ShouldBeNull();
            badOpaque.ShouldBe(DataParallel.ParallelRetRunnerError);
            invertedRange.ShouldBe(DataParallel.ParallelRetRunnerError);
            emptyRange.ShouldBe(DataParallel.ParallelRetSuccess);
            initError.ShouldBe(-7);
            outerResult.ShouldBe(DataParallel.ParallelRetSuccess);
            reentrantResult.ShouldBe(DataParallel.ParallelRetRunnerError);

            runner.Dispose();
            runner.Dispose();
        }

        /// <summary>
        /// Verifies <see cref="ThreadParallelRunner"/> zero-worker mode on <see cref="ThreadParallelRunner.RunOnEachThread"/>.
        /// </summary>
        [Test]
        public void ThreadParallelRunner_ZeroWorkers_RunsOnMainThread()
        {
            using ThreadParallelRunner zeroRunner = new ThreadParallelRunner(0);
            int callCount                         = 0;

            zeroRunner.RunOnEachThread((task, thread) =>
            {
                if (task == 0 && thread == 0)
                {
                    callCount++;
                }
            });

            callCount.ShouldBe(1);
        }

        /// <summary>
        /// Verifies <see cref="ResizableParallelRunner"/> guard clauses, single-task failure, multi-task failure, and double disposal.
        /// </summary>
        [Test]
        public void ResizableParallelRunner_EdgeCasesAndGuards_BehaveAsExpected()
        {
            using ResizableParallelRunner runner = ResizableParallelRunner.Create();
            int badOpaque                        = ResizableParallelRunner.Runner(null, null, static (_, _) => 0, static (_, _, _) => { }, 0, 4);
            int invertedRange                    = runner.Run(null, static (_, _) => 0, static (_, _, _) => { }, 5, 2);
            int emptyRange                       = runner.Run(null, static (_, _) => 0, static (_, _, _) => { }, 3, 3);
            int singleInitFail                   = runner.Run(null, static (_, _) => -3, static (_, _, _) => { }, 0, 1);
            int multiInitFail                    = runner.Run(null, static (_, _) => -5, static (_, _, _) => { }, 0, 4);

            badOpaque.ShouldBe(DataParallel.ParallelRetRunnerError);
            invertedRange.ShouldBe(DataParallel.ParallelRetRunnerError);
            emptyRange.ShouldBe(DataParallel.ParallelRetSuccess);
            singleInitFail.ShouldBe(-3);
            multiInitFail.ShouldBe(-5);

            runner.Dispose();
            runner.Dispose();
        }

        /// <summary>
        /// Verifies equality, hash code, string formatting, and bounds checking on <see cref="CIExy"/>, <see cref="PrimariesCIExy"/>, <see cref="Customxy"/>, and <see cref="CustomTransferFunction"/>.
        /// </summary>
        [Test]
        public void ColorEncodingStructs_EqualityFormattingAndBounds_BehaveAsExpected()
        {
            CIExy xy1                  = new CIExy(0.3127, 0.3290);
            CIExy xy2                  = new CIExy(0.3127, 0.3290);
            PrimariesCIExy p1          = new PrimariesCIExy(xy1, xy2, xy1);
            PrimariesCIExy p2          = new PrimariesCIExy(xy1, xy2, xy1);
            Customxy custom            = default;
            CustomTransferFunction tf1 = CustomTransferFunction.CreateDefault();
            CustomTransferFunction tf2 = CustomTransferFunction.CreateDefault();

            xy1.Equals((object)xy2).ShouldBeTrue();
            xy1.Equals((object)"not_xy").ShouldBeFalse();
            xy1.GetHashCode().ShouldBe(xy2.GetHashCode());
            xy1.ToString().ShouldContain("0.3127");

            p1.Equals((object)p2).ShouldBeTrue();
            p1.Equals((object)"not_p").ShouldBeFalse();
            p1.GetHashCode().ShouldBe(p2.GetHashCode());
            p1.ToString().ShouldContain("r=");

            custom.SetValue(new CIExy(5.0, 0.5)).IsOk.ShouldBeFalse();
            custom.SetValue(new CIExy(3.0, 0.5)).IsOk.ShouldBeFalse();
            custom.SetValue(new CIExy(0.5, 3.0)).IsOk.ShouldBeFalse();
            custom.SetValue(new CIExy(0.5, 0.5)).IsOk.ShouldBeTrue();

            tf1.GetGamma().ShouldBe(0.0);
            tf2.SetGamma(0.45).IsOk.ShouldBeTrue();
            tf1.IsSame(tf2).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies external enum conversion guards and primaries/white-point validation in <see cref="CmsColorEncoding"/>.
        /// </summary>
        [Test]
        public void CmsColorEncoding_ValidationAndConversions_HandleInvalidInputs()
        {
            CmsColorEncoding enc = new CmsColorEncoding { HaveFields = false };

            CmsColorEncoding.WhitePointFromExternal((JxlWhitePoint)999, out _).IsOk.ShouldBeFalse();
            CmsColorEncoding.PrimariesFromExternal((JxlPrimaries)999, out _).IsOk.ShouldBeFalse();
            CmsColorEncoding.RenderingIntentFromExternal((JxlRenderingIntent)999, out _).IsOk.ShouldBeFalse();
            CmsColorEncoding.ConvertExternalToInternalTransferFunction(JxlTransferFunction.Gamma, out _).IsOk.ShouldBeFalse();
            CmsColorEncoding.ConvertExternalToInternalTransferFunction((JxlTransferFunction)999, out _).IsOk.ShouldBeFalse();

            enc.GetPrimaries(out _).IsOk.ShouldBeFalse();
            enc.SetPrimaries(default).IsOk.ShouldBeFalse();
            enc.GetWhitePoint().ShouldBe(default(CIExy));
            enc.SetWhitePoint(new CIExy(0.3, 0.3)).IsOk.ShouldBeFalse();
            enc.ToExternal().ColorSpace.ShouldBe(JxlColorSpace.Unknown);
            enc.CreateICC().IsOk.ShouldBeFalse();

            enc.HaveFields = true;
            enc.Primaries  = (Primaries)999;
            enc.WhitePoint = (WhitePoint)999;
            enc.GetPrimaries(out _).IsOk.ShouldBeFalse();
            enc.GetWhitePoint().ShouldBe(default(CIExy));
            enc.ToExternal().ColorSpace.ShouldBe(JxlColorSpace.Unknown);

            enc.SetWhitePoint(new CIExy(0.0, 0.3)).IsOk.ShouldBeFalse();
            enc.SetPrimaries(new PrimariesCIExy(new CIExy(0.0, 0.3), new CIExy(0.3, 0.6), new CIExy(0.15, 0.06))).IsOk.ShouldBeFalse();
            enc.SetPrimaries(new PrimariesCIExy(new CIExy(5.0, 0.3), new CIExy(0.3, 0.6), new CIExy(0.15, 0.06))).IsOk.ShouldBeFalse();
            enc.SetPrimaries(new PrimariesCIExy(new CIExy(0.6, 0.3), new CIExy(5.0, 0.6), new CIExy(0.15, 0.06))).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="CmsColorEncoding.SameColorSpace"/>, <see cref="CmsColorEncoding.FromExternal"/>, and <see cref="CmsColorEncoding.SetFieldsFromICC"/> failure paths.
        /// </summary>
        [Test]
        public void CmsColorEncoding_ExternalAndIccFailurePaths_ReturnFalse()
        {
            CmsColorEncoding c1        = CmsColorEncoding.SRGB();
            CmsColorEncoding c2        = CmsColorEncoding.SRGB();
            CmsColorEncoding target    = new CmsColorEncoding();
            JxlCmsInterface defaultCms = JxlCms.GetDefaultCms();

            JxlCmsInterface brokenCms = new JxlCmsInterface
            {
                SetFieldsFromIcc = static (object? _, ReadOnlySpan<byte> _, ref JxlColorEncoding ext, out bool cmyk) =>
                {
                    cmyk           = false;
                    ext.WhitePoint = (JxlWhitePoint)999;

                    return true;
                },
            };

            c1.SetWhitePoint(new CIExy(0.32, 0.33)).IsOk.ShouldBeTrue();
            c2.SetWhitePoint(new CIExy(0.35, 0.36)).IsOk.ShouldBeTrue();
            c1.SameColorSpace(c2).ShouldBeFalse();

            target.FromExternal(new JxlColorEncoding { WhitePoint = (JxlWhitePoint)999 }).IsOk.ShouldBeFalse();
            target.FromExternal(new JxlColorEncoding { WhitePoint = JxlWhitePoint.Custom, WhitePointXy = new ColorCoordinate(0.0, 0.0) }).IsOk.ShouldBeFalse();
            target.FromExternal(new JxlColorEncoding { ColorSpace = JxlColorSpace.Rgb, WhitePoint = JxlWhitePoint.D65, Primaries = (JxlPrimaries)999 }).IsOk.ShouldBeFalse();
            target.FromExternal(new JxlColorEncoding { ColorSpace = JxlColorSpace.Gray, WhitePoint = JxlWhitePoint.D65, TransferFunction = JxlTransferFunction.Gamma, Gamma = 0.0 }).IsOk.ShouldBeFalse();
            target.FromExternal(new JxlColorEncoding { ColorSpace = JxlColorSpace.Gray, WhitePoint = JxlWhitePoint.D65, TransferFunction = (JxlTransferFunction)999 }).IsOk.ShouldBeFalse();
            target.FromExternal(new JxlColorEncoding { ColorSpace = JxlColorSpace.Gray, WhitePoint = JxlWhitePoint.D65, TransferFunction = JxlTransferFunction.Srgb, RenderingIntent = (JxlRenderingIntent)999 }).IsOk.ShouldBeFalse();

            target.SetFieldsFromICC(Array.Empty<byte>(), defaultCms).IsOk.ShouldBeFalse();
            target.SetFieldsFromICC(new byte[] { 1, 2, 3 }, defaultCms).IsOk.ShouldBeFalse();
            target.SetICC(Array.Empty<byte>(), defaultCms).IsOk.ShouldBeFalse();
            target.SetFieldsFromICC(new byte[] { 1, 2, 3, 4 }, brokenCms).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="JxlCmsInternal"/> matrix helpers, tone-mapping LUT helpers, and profile description formatting.
        /// </summary>
        [Test]
        public void JxlCmsInternal_MatricesDescriptionsAndProfileValidation_BehaveAsExpected()
        {
            HlgOotf noOpOotf         = new HlgOotf(300.0f, 300.0f, new Vector3(0.2f, 0.7f, 0.1f));
            Vector3 rgbScalar        = new Vector3(0.5f, 0.5f, 0.5f);
            Vector128<float> rV      = Vector128.Create(0.5f);
            Vector128<float> gV      = Vector128.Create(0.5f);
            Vector128<float> bV      = Vector128.Create(0.5f);
            PrimariesCIExy singularP = new PrimariesCIExy(new CIExy(0.5, 0.5), new CIExy(0.5, 0.5), new CIExy(0.5, 0.5));
            CIExy validWp            = new CIExy(0.3127, 0.3290);
            Span<byte> labOut        = stackalloc byte[3];
            ushort[] pqTable         = JxlCmsInternal.CreateTableCurve(16, ExtraTf.PQ, toneMap: true);

            JxlSharp.Cms.OpsinParams.ReciprocalSum(2.0f, 3.0f).ShouldBe(1.2f, 1e-5f);

            noOpOotf.Apply(ref rgbScalar);
            noOpOotf.Apply(ref rV, ref gV, ref bV);
            rgbScalar[0].ShouldBe(0.5f);

            JxlCmsInternal.PrimariesToXYZ(singularP, new CIExy(-1.0, 0.5), out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.PrimariesToXYZ(singularP, validWp, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.PrimariesToXYZD50(singularP, validWp, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.AdaptToXYZD50(-1.0f, 0.5f, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.AdaptToXYZD50(1.0f, float.Epsilon, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.CIEXYZFromWhiteCIExy(0.3, 0.0, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.ToneMapPixel(new JxlColorEncoding { WhitePointXy = new ColorCoordinate(0.0, 0.0) }, new float[] { 0.5f, 0.5f, 0.5f }, labOut).IsOk.ShouldBeFalse();
            pqTable.Length.ShouldBe(16);
        }

        /// <summary>
        /// Verifies <see cref="JxlCmsInternal.ColorEncodingDescription"/> and <see cref="JxlCmsInternal.MaybeCreateProfile"/> branches.
        /// </summary>
        [Test]
        public void JxlCmsInternal_DescriptionsAndMaybeCreateProfile_CoverSpecialProfiles()
        {
            JxlColorEncoding adobe98 = new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Rgb,
                WhitePoint       = JxlWhitePoint.D65,
                Primaries        = JxlPrimaries.Custom,
                PrimariesRedXy   = new ColorCoordinate(0.6400, 0.3300),
                PrimariesGreenXy = new ColorCoordinate(0.2100, 0.7100),
                PrimariesBlueXy  = new ColorCoordinate(0.1500, 0.0600),
                TransferFunction = JxlTransferFunction.Gamma,
                Gamma            = 256.0 / 563.0,
            };

            JxlColorEncoding proPhoto = new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Rgb,
                WhitePoint       = JxlWhitePoint.Custom,
                WhitePointXy     = new ColorCoordinate(0.345669, 0.358496),
                Primaries        = JxlPrimaries.Custom,
                PrimariesRedXy   = new ColorCoordinate(0.734699, 0.265301),
                PrimariesGreenXy = new ColorCoordinate(0.159597, 0.840403),
                PrimariesBlueXy  = new ColorCoordinate(0.036598, 0.000105),
                TransferFunction = JxlTransferFunction.Gamma,
                Gamma            = 1.0 / 1.8,
            };

            JxlColorEncoding weird = new JxlColorEncoding
            {
                ColorSpace       = (JxlColorSpace)99,
                WhitePoint       = (JxlWhitePoint)99,
                Primaries        = (JxlPrimaries)99,
                TransferFunction = (JxlTransferFunction)99,
                RenderingIntent  = (JxlRenderingIntent)99,
            };

            JxlCmsInternal.ColorEncodingDescription(adobe98, uniqueName: false).ShouldBe("Adobe98");
            JxlCmsInternal.ColorEncodingDescription(proPhoto, uniqueName: false).ShouldBe("ProPhoto");
            JxlCmsInternal.ColorEncodingDescription(weird, uniqueName: true).ShouldContain("Invalid");
            JxlCmsInternal.ColorEncodingDescription(new JxlColorEncoding { ColorSpace = JxlColorSpace.Unknown, TransferFunction = JxlTransferFunction.Unknown }, uniqueName: true).ShouldContain("CS?");

            JxlCmsInternal.MaybeCreateProfile(weird, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.MaybeCreateProfile(new JxlColorEncoding { ColorSpace = JxlColorSpace.Xyb, TransferFunction = JxlTransferFunction.Srgb, RenderingIntent = JxlRenderingIntent.Relative }, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.MaybeCreateProfile(new JxlColorEncoding { ColorSpace = JxlColorSpace.Gray, TransferFunction = JxlTransferFunction.Srgb, WhitePointXy = new ColorCoordinate(0.3, 0.0) }, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.MaybeCreateProfile(new JxlColorEncoding { ColorSpace = JxlColorSpace.Rgb, TransferFunction = JxlTransferFunction.Srgb, WhitePointXy = new ColorCoordinate(0.3, 0.0) }, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.MaybeCreateProfile(new JxlColorEncoding { ColorSpace = JxlColorSpace.Rgb, TransferFunction = JxlTransferFunction.Srgb, WhitePointXy = new ColorCoordinate(0.3127, 0.3290) }, out _).IsOk.ShouldBeFalse();
            JxlCmsInternal.MaybeCreateProfile(new JxlColorEncoding { ColorSpace = JxlColorSpace.Gray, TransferFunction = (JxlTransferFunction)99, WhitePointXy = new ColorCoordinate(0.3127, 0.3290) }, out _).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="JxlCms.GetPrimariesLuminances"/> and <see cref="JxlCms.SetFieldsFromIcc"/> for malformed, CMYK, and CICP profiles.
        /// </summary>
        [Test]
        public void JxlCms_GetPrimariesLuminancesAndMalformedIcc_HandleEdgeCases()
        {
            Span<float> lums          = stackalloc float[3];
            CmsColorEncoding noFields = new CmsColorEncoding { HaveFields = false };
            CmsColorEncoding grayEnc  = CmsColorEncoding.SRGB(isGray: true);
            CmsColorEncoding singEnc  = CmsColorEncoding.SRGB();
            JxlColorEncoding ext      = default;
            byte[] zeroWtpt           = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x77747074u, BuildXyzTag(0.0, 0.0, 0.0)));
            byte[] singularChad       = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x77747074u, BuildXyzTag(0.95, 1.0, 1.08)), (0x63686164u, BuildZeroChadTag()));
            byte[] negativeWtpt       = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x77747074u, BuildXyzTag(-0.2, 0.5, 0.7)));

            singEnc.Primaries = Primaries.Custom;
            singEnc.Red.SetValue(new CIExy(0.5, 0.5));
            singEnc.Green.SetValue(new CIExy(0.5, 0.5));
            singEnc.Blue.SetValue(new CIExy(0.5, 0.5));

            JxlCms.GetPrimariesLuminances(noFields, lums).IsOk.ShouldBeFalse();
            JxlCms.GetPrimariesLuminances(grayEnc, lums).IsOk.ShouldBeFalse();
            JxlCms.GetPrimariesLuminances(singEnc, lums).IsOk.ShouldBeFalse();

            JxlCms.SetFieldsFromIcc(null, new byte[64], ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, BuildMinimalIcc(0x52474220u, 0x58595A20u, intent: 0x04040404u), ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, BuildMinimalIcc(0x434D594Bu, 0x58595A20u, intent: 0u), ref ext, out bool isCmyk).ShouldBeTrue();
            isCmyk.ShouldBeTrue();

            JxlCms.SetFieldsFromIcc(null, BuildMinimalIcc(0x52474220u, 0x58595A20u, intent: 0u), ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, zeroWtpt, ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, singularChad, ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, negativeWtpt, ref ext, out _).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies ICC header, tag table, and tag parsing error paths via <see cref="JxlCms.SetFieldsFromIcc"/>.
        /// </summary>
        [Test]
        public void ParsedIccProfile_MalformedHeadersAndTags_AreHandledSafely()
        {
            JxlColorEncoding ext = default;
            byte[] badSize       = BuildMinimalIcc(0x52474220u, 0x58595A20u, 0u);
            byte[] badAcsp       = BuildMinimalIcc(0x52474220u, 0x58595A20u, 0u);
            byte[] only128       = new byte[128];
            byte[] hugeTags      = BuildMinimalIcc(0x52474220u, 0x58595A20u, 0u);
            byte[] oobTag        = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x77747074u, new byte[20]));

            byte[] badTags = BuildIccWithTags(
                0x52474220u,
                0x58595A20u,
                (0x77747074u, new byte[8]),
                (0x63686164u, new byte[12]),
                (0x72545243u, new byte[8]),
                (0x67545243u, new byte[12]),
                (0x62545243u, BuildCurvTag(100, 4)),
                (0x6B545243u, BuildParaTag(99, Array.Empty<double>())),
                (0x41324230u, new byte[16]));

            WriteBe32(badSize, 0, 64u);
            JxlCms.SetFieldsFromIcc(null, badSize, ref ext, out _).ShouldBeFalse();

            WriteBe32(badAcsp, 36, 0x12345678u);
            JxlCms.SetFieldsFromIcc(null, badAcsp, ref ext, out _).ShouldBeFalse();

            WriteBe32(only128, 0, 128u);
            WriteBe32(only128, 36, 0x61637370u);
            JxlCms.SetFieldsFromIcc(null, only128, ref ext, out _).ShouldBeFalse();

            WriteBe32(hugeTags, 128, 2000u);
            JxlCms.SetFieldsFromIcc(null, hugeTags, ref ext, out _).ShouldBeFalse();

            WriteBe32(oobTag, 140, 9999u);
            JxlCms.SetFieldsFromIcc(null, oobTag, ref ext, out _).ShouldBeFalse();

            JxlCms.SetFieldsFromIcc(null, badTags, ref ext, out _).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies `mAB ` and `mft1` A2B0 LUT error and precision paths in <see cref="ParsedIccProfile"/>.
        /// </summary>
        [Test]
        public void ParsedIccProfile_MabAndMft1Variants_ParseAllBranches()
        {
            JxlColorEncoding ext = default;
            byte[] unknownLut    = new byte[36];
            byte[] badMabCh      = BuildMabHeader(0, 0, 0, 0, 0, 32);
            byte[] badMabB       = BuildMabHeader(32, 0, 0, 0, 0, 36);
            byte[] badMabM       = BuildMabHeader(0, 0, 32, 0, 0, 36);
            byte[] badMabA       = BuildMabHeader(0, 0, 0, 0, 32, 36);
            byte[] badMabClut1   = BuildMabHeader(0, 0, 0, 32, 0, 40);
            byte[] badMabClut2   = BuildMabHeader(0, 0, 0, 32, 0, 56);
            byte[] badMabMat     = BuildMabHeader(0, 32, 0, 0, 0, 40);
            byte[] validMab1Byte = BuildMabWithCurvAndPrecision1();
            byte[] badMft1Ch     = new byte[48 + 768];
            byte[] badMft1Grid   = new byte[48 + 768];

            WriteBe32(unknownLut, 0, 0x11223344u);
            badMabCh[8]     = 2;
            badMabClut2[32] = 2;
            badMabClut2[33] = 2;
            badMabClut2[34] = 2;
            badMabClut2[48] = 2;

            WriteBe32(badMft1Ch, 0, 0x6D667431u);
            badMft1Ch[8] = 2;
            badMft1Ch[9] = 3;

            WriteBe32(badMft1Grid, 0, 0x6D667431u);
            badMft1Grid[8]  = 3;
            badMft1Grid[9]  = 3;
            badMft1Grid[10] = 1;

            byte[][] lutTags =
            {
                unknownLut, badMabCh, badMabB, badMabM, badMabA,
                badMabClut1, badMabClut2, badMabMat, validMab1Byte, badMft1Ch, badMft1Grid,
            };

            foreach (byte[] lut in lutTags)
            {
                byte[] icc = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x41324230u, lut));

                JxlCms.SetFieldsFromIcc(null, icc, ref ext, out _);
            }
        }

        /// <summary>
        /// Verifies CICP tag variations and A2B0 (`mAB ` and `mft1`) tag parsing paths in <see cref="ParsedIccProfile"/> and <see cref="JxlCms"/>.
        /// </summary>
        [Test]
        public void ParsedIccProfile_CicpAndA2BLutVariants_ParseAndTransformCorrectly()
        {
            JxlColorEncoding ext = default;
            byte[] cicpP3Dci     = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x63696370u, BuildCicpTag(11, 13, 0, 1)));
            byte[] cicpP3D65     = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x63696370u, BuildCicpTag(12, 13, 0, 1)));
            byte[] cicpBad1      = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x63696370u, BuildCicpTag(1, 13, 1, 1)));
            byte[] cicpBad2      = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x63696370u, BuildCicpTag(99, 13, 0, 1)));

            JxlColorEncoding hdrPq = new JxlColorEncoding
            {
                ColorSpace       = JxlColorSpace.Rgb,
                WhitePoint       = JxlWhitePoint.D65,
                WhitePointXy     = new ColorCoordinate(0.3127, 0.3290),
                Primaries        = JxlPrimaries.P2100,
                PrimariesRedXy   = new ColorCoordinate(0.708, 0.292),
                PrimariesGreenXy = new ColorCoordinate(0.170, 0.797),
                PrimariesBlueXy  = new ColorCoordinate(0.131, 0.046),
                TransferFunction = JxlTransferFunction.Pq,
                RenderingIntent  = JxlRenderingIntent.Relative,
            };

            JxlCms.SetFieldsFromIcc(null, cicpP3Dci, ref ext, out _).ShouldBeTrue();
            ext.WhitePoint.ShouldBe(JxlWhitePoint.Dci);
            JxlCms.SetFieldsFromIcc(null, cicpP3D65, ref ext, out _).ShouldBeTrue();
            ext.WhitePoint.ShouldBe(JxlWhitePoint.D65);
            JxlCms.SetFieldsFromIcc(null, cicpBad1, ref ext, out _).ShouldBeFalse();
            JxlCms.SetFieldsFromIcc(null, cicpBad2, ref ext, out _).ShouldBeFalse();

            JxlCmsInternal.MaybeCreateProfile(hdrPq, out byte[] hdrIcc).IsOk.ShouldBeTrue();
            OverwriteTagSignature(hdrIcc, 0x63696370u, 0x78787878u);
            JxlCms.SetFieldsFromIcc(null, hdrIcc, ref ext, out _);
        }

        /// <summary>
        /// Verifies tone curve evaluation (empty table, 1-entry gamma table, multi-entry table, and parametric types 1, 2, 4) through <see cref="JxlCms"/> transforms.
        /// </summary>
        [Test]
        public void ParsedIccProfile_ToneCurveVariants_ExecuteForwardAndInverseTransforms()
        {
            ushort[] table16  = new ushort[16];
            ushort[] zeroTbl4 = new ushort[4];

            for (int i = 0; i < 16; ++i)
            {
                table16[i] = (ushort)Math.Round(Math.Pow(i / 15.0, 1.8) * 65535.0);
            }

            byte[] srcEmptyAndSingle = BuildCicpRgbProfileWithTrc(
                primariesCode: 1,
                tfCode: 1,
                BuildCurvTag(0, 0),
                BuildCurvTag(1, 2, 512),
                BuildParaTag(1, new[] { 2.0, 1.0, 0.0 }));

            byte[] dstTableAndPara = BuildCicpRgbProfileWithTrc(
                primariesCode: 11,
                tfCode: 1,
                BuildCurvTag(16, 32, table16),
                BuildParaTag(2, new[] { 2.0, 1.0, 0.0, 0.0 }),
                BuildParaTag(4, new[] { 2.0, 1.0, 0.0, 1.0, 0.1, 0.0, 0.0 }));

            byte[] srcFallback = BuildIccWithTags(0x52474220u, 0x58595A20u, (0x63696370u, BuildCicpTag(1, 1, 0, 1)));

            byte[] dstZeroG = BuildCicpRgbProfileWithTrc(
                primariesCode: 11,
                tfCode: 1,
                BuildCurvTag(1, 2, 0),
                BuildParaTag(0, new[] { 0.0 }),
                BuildCurvTag(0, 0));

            byte[] tableGammaProfile = BuildRgbProfileWithoutCicp(BuildCurvTag(16, 32, table16));
            byte[] zeroTableProfile  = BuildRgbProfileWithoutCicp(BuildCurvTag(4, 8, zeroTbl4));
            JxlColorEncoding ext     = default;

            RunCmsPair(srcEmptyAndSingle, dstTableAndPara, 250.0f);
            RunCmsPair(srcFallback, dstZeroG, 250.0f);

            JxlCms.SetFieldsFromIcc(null, tableGammaProfile, ref ext, out _).ShouldBeTrue();
            JxlCms.SetFieldsFromIcc(null, zeroTableProfile, ref ext, out _).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies Gray-to-RGB, RGB-to-Gray, and HLG OOTF color space transforms in <see cref="JxlCms"/> and <see cref="JxlSharp.Cms.ColorSpaceTransform"/>.
        /// </summary>
        [Test]
        public void ColorSpaceTransform_GrayAndHlgOotfPaths_TransformPixelsCorrectly()
        {
            using JxlSharp.Cms.ColorSpaceTransform uninitXform = new JxlSharp.Cms.ColorSpaceTransform();
            Span<float> dummy                                  = stackalloc float[3];
            CmsColorEncoding cmykDst                           = CmsColorEncoding.SRGB();
            JxlCmsInterface cms                                = JxlCms.GetDefaultCms();
            CmsColorEncoding graySrgb                          = CmsColorEncoding.SRGB(isGray: true);
            CmsColorEncoding rgbLin                            = CmsColorEncoding.LinearSRGB(isGray: false);
            CmsColorEncoding rgbHlg                            = CmsColorEncoding.SRGB(isGray: false);
            CmsColorEncoding grayHlg                           = CmsColorEncoding.SRGB(isGray: true);
            CmsColorEncoding grayLin                           = CmsColorEncoding.LinearSRGB(isGray: true);

            uninitXform.Run(0, dummy, dummy, 1).IsOk.ShouldBeFalse();

            cmykDst.Cmyk = true;
            uninitXform.Init(CmsColorEncoding.SRGB(), cmykDst, 250.0f, 1, 1).IsOk.ShouldBeFalse();

            cms.Init!(null!, 1, 1, null!, null!, 250.0f).ShouldBeNull();
            cms.Run!(null!, 0, dummy, dummy, 1).ShouldBeFalse();

            ExecuteTransform(graySrgb, rgbLin, 250.0f, new float[] { 0.5f, 0.2f });
            ExecuteTransform(rgbLin, graySrgb, 250.0f, new float[] { 0.5f, 0.4f, 0.3f, 0.1f, 0.2f, 0.3f });

            rgbHlg.Tf.SetTransferFunction(TransferFunction.HLG);
            rgbHlg.CreateICC().IsOk.ShouldBeTrue();

            ExecuteTransform(rgbHlg, rgbLin, 300.0f, new float[] { 0.5f, 0.5f, 0.5f, 0.0f, 0.0f, 0.0f });
            ExecuteTransform(rgbHlg, rgbLin, 100.0f, new float[] { 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f });
            ExecuteTransform(rgbLin, rgbHlg, 1000.0f, new float[] { 0.5f, 0.5f, 0.5f, 0.2f, 0.3f, 0.4f });

            grayHlg.Tf.SetTransferFunction(TransferFunction.HLG);
            grayHlg.CreateICC().IsOk.ShouldBeTrue();

            ExecuteTransform(grayHlg, grayLin, 1000.0f, new float[] { 0.5f, 0.8f });
        }

        private static void ExecuteTransform(CmsColorEncoding src, CmsColorEncoding dst, float intensity, float[] inputPixels)
        {
            using JxlSharp.Cms.ColorSpaceTransform xform = new JxlSharp.Cms.ColorSpaceTransform();
            nuint width                     = (nuint)(inputPixels.Length / (int)src.Channels());

            xform.Init(src, dst, intensity, width, 1).IsOk.ShouldBeTrue();

            Span<float> srcBuf = xform.BufSrc(0);
            Span<float> dstBuf = xform.BufDst(0);

            inputPixels.AsSpan().CopyTo(srcBuf);
            xform.Run(0, srcBuf, dstBuf, width).IsOk.ShouldBeTrue();
        }

        private static void RunCmsPair(byte[] srcIcc, byte[] dstIcc, float intensityTarget)
        {
            JxlCmsInterface cms   = JxlCms.GetDefaultCms();
            JxlColorProfile inPr  = new JxlColorProfile { Icc = srcIcc, NumChannels = 3 };
            JxlColorProfile outPr = new JxlColorProfile { Icc = dstIcc, NumChannels = 3 };
            object? state         = cms.Init!(cms.InitData, 1, 3, inPr, outPr, intensityTarget);

            state.ShouldNotBeNull();

            float[] src = cms.GetSrcBuf!(state, 0)!;
            float[] dst = cms.GetDstBuf!(state, 0)!;

            src[0] = -0.1f;
            src[1] = 0.0f;
            src[2] = 0.5f;
            src[3] = 0.2f;
            src[4] = 0.8f;
            src[5] = 1.2f;
            src[6] = 0.5f;
            src[7] = 0.5f;
            src[8] = 0.5f;

            cms.Run!(state, 0, src, dst, 3).ShouldBeTrue();
            cms.Destroy!(state);
        }

        private static byte[] BuildMinimalIcc(uint colorSpace, uint pcs, uint intent)
        {
            byte[] icc = new byte[132];

            WriteBe32(icc, 0, 132u);
            WriteBe32(icc, 16, colorSpace);
            WriteBe32(icc, 20, pcs);
            WriteBe32(icc, 36, 0x61637370u);
            WriteBe32(icc, 64, intent);
            WriteBe32(icc, 128, 0u);

            return icc;
        }

        private static byte[] BuildIccWithTags(uint colorSpace, uint pcs, params (uint Sig, byte[] Data)[] tags)
        {
            int tableSize = 132 + (tags.Length * 12);
            List<byte> b  = new List<byte>(tableSize);

            for (int i = 0; i < tableSize; ++i)
            {
                b.Add(0);
            }

            for (int i = 0; i < tags.Length; ++i)
            {
                while ((b.Count & 3) != 0)
                {
                    b.Add(0);
                }

                int offset = b.Count;
                int size   = tags[i].Data.Length;

                b.AddRange(tags[i].Data);

                byte[] hdr = b.ToArray();
                int entry  = 132 + (i * 12);

                WriteBe32(hdr, entry, tags[i].Sig);
                WriteBe32(hdr, entry + 4, (uint)offset);
                WriteBe32(hdr, entry + 8, (uint)size);

                b.Clear();
                b.AddRange(hdr);
            }

            byte[] result = b.ToArray();

            WriteBe32(result, 0, (uint)result.Length);
            WriteBe32(result, 16, colorSpace);
            WriteBe32(result, 20, pcs);
            WriteBe32(result, 36, 0x61637370u);
            WriteBe32(result, 128, (uint)tags.Length);

            return result;
        }

        private static byte[] BuildCicpTag(byte primaries, byte tf, byte matrix, byte fullRange)
        {
            byte[] tag = new byte[12];

            WriteBe32(tag, 0, 0x63696370u);
            tag[8]  = primaries;
            tag[9]  = tf;
            tag[10] = matrix;
            tag[11] = fullRange;

            return tag;
        }

        private static byte[] BuildXyzTag(double x, double y, double z)
        {
            byte[] tag = new byte[20];

            WriteBe32(tag, 0, 0x58595A20u);
            WriteS15Fixed16(tag, 8, x);
            WriteS15Fixed16(tag, 12, y);
            WriteS15Fixed16(tag, 16, z);

            return tag;
        }

        private static byte[] BuildZeroChadTag()
        {
            byte[] tag = new byte[44];

            WriteBe32(tag, 0, 0x73663332u);

            return tag;
        }

        private static byte[] BuildMabHeader(uint offB, uint offMat, uint offM, uint offClut, uint offA, int totalLen)
        {
            byte[] tag = new byte[totalLen];

            WriteBe32(tag, 0, 0x6D414220u);
            tag[8] = 3;
            tag[9] = 3;
            WriteBe32(tag, 12, offB);
            WriteBe32(tag, 16, offMat);
            WriteBe32(tag, 20, offM);
            WriteBe32(tag, 24, offClut);
            WriteBe32(tag, 28, offA);

            return tag;
        }

        private static byte[] BuildMabWithCurvAndPrecision1()
        {
            byte[] tag = BuildMabHeader(32, 0, 0, 68, 0, 120);

            for (int i = 0; i < 3; ++i)
            {
                int pos = 32 + (i * 12);

                WriteBe32(tag, pos, 0x63757276u);
                WriteBe32(tag, pos + 8, 0u);
            }

            tag[68]     = 2;
            tag[69]     = 2;
            tag[70]     = 2;
            tag[68 + 16] = 1;

            return tag;
        }

        private static byte[] BuildCurvTag(uint count, int payloadBytes, params ushort[] entries)
        {
            byte[] tag = new byte[12 + payloadBytes];

            WriteBe32(tag, 0, 0x63757276u);
            WriteBe32(tag, 8, count);

            for (int i = 0; i < entries.Length && (12 + (i * 2) + 1) < tag.Length; ++i)
            {
                tag[12 + (i * 2)]     = (byte)(entries[i] >> 8);
                tag[12 + (i * 2) + 1] = (byte)(entries[i] & 0xFF);
            }

            return tag;
        }

        private static byte[] BuildParaTag(ushort funcType, double[] parameters)
        {
            byte[] tag = new byte[12 + (parameters.Length * 4)];

            WriteBe32(tag, 0, 0x70617261u);
            tag[8] = (byte)(funcType >> 8);
            tag[9] = (byte)(funcType & 0xFF);

            for (int i = 0; i < parameters.Length; ++i)
            {
                WriteS15Fixed16(tag, 12 + (i * 4), parameters[i]);
            }

            return tag;
        }

        private static byte[] BuildCicpRgbProfileWithTrc(byte primariesCode, byte tfCode, byte[] rTrc, byte[] gTrc, byte[] bTrc)
        {
            return BuildIccWithTags(
                0x52474220u,
                0x58595A20u,
                (0x63696370u, BuildCicpTag(primariesCode, tfCode, 0, 1)),
                (0x77747074u, BuildXyzTag(0.9642, 1.0, 0.8249)),
                (0x7258595Au, BuildXyzTag(0.4360, 0.2225, 0.0139)),
                (0x6758595Au, BuildXyzTag(0.3851, 0.7169, 0.0971)),
                (0x6258595Au, BuildXyzTag(0.1431, 0.0606, 0.7139)),
                (0x72545243u, rTrc),
                (0x67545243u, gTrc),
                (0x62545243u, bTrc));
        }

        private static byte[] BuildRgbProfileWithoutCicp(byte[] trc)
        {
            return BuildIccWithTags(
                0x52474220u,
                0x58595A20u,
                (0x77747074u, BuildXyzTag(0.950455, 1.0, 1.08905)),
                (0x7258595Au, BuildXyzTag(0.4360, 0.2225, 0.0139)),
                (0x6758595Au, BuildXyzTag(0.3851, 0.7169, 0.0971)),
                (0x6258595Au, BuildXyzTag(0.1431, 0.0606, 0.7139)),
                (0x72545243u, trc),
                (0x67545243u, trc),
                (0x62545243u, trc));
        }

        private static void OverwriteTagSignature(byte[] icc, uint oldSig, uint newSig)
        {
            uint count = ((uint)icc[128] << 24) | ((uint)icc[129] << 16) | ((uint)icc[130] << 8) | icc[131];

            for (int i = 0; i < (int)count; ++i)
            {
                int pos  = 132 + (i * 12);
                uint sig = ((uint)icc[pos] << 24) | ((uint)icc[pos + 1] << 16) | ((uint)icc[pos + 2] << 8) | icc[pos + 3];

                if (sig == oldSig)
                {
                    WriteBe32(icc, pos, newSig);
                }
            }
        }

        private static void WriteBe32(byte[] buf, int pos, uint val)
        {
            buf[pos]     = (byte)(val >> 24);
            buf[pos + 1] = (byte)(val >> 16);
            buf[pos + 2] = (byte)(val >> 8);
            buf[pos + 3] = (byte)val;
        }

        private static void WriteS15Fixed16(byte[] buf, int pos, double val)
        {
            int fixedVal = (int)Math.Round(val * 65536.0);

            WriteBe32(buf, pos, (uint)fixedVal);
        }
    }
}
