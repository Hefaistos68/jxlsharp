// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO.Compression;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// High-coverage unit tests for <c>JxlSharp.Decoder</c> subsystems including patch dictionaries,
    /// external image conversion, render pipeline stages, JPEG reconstruction, ICC coding, blending,
    /// splines, loop filters, and decoder state management.
    /// </summary>
    [TestFixture]
    public unsafe class DecoderHighCoverageTests
    {
        // =====================================================================
        // 1. PatchDictionary, ReferenceFrame, and PatchBlending Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="PatchBlending"/>, <see cref="ReferenceFrame"/>, and basic <see cref="PatchDictionary"/> properties.
        /// </summary>
        [Test]
        public void TestPatchDictionaryPropertiesAndClear()
        {
            PatchBlending.UsesAlpha(PatchBlendMode.kNone).ShouldBeFalse();
            PatchBlending.UsesAlpha(PatchBlendMode.kReplace).ShouldBeFalse();
            PatchBlending.UsesAlpha(PatchBlendMode.kAdd).ShouldBeFalse();
            PatchBlending.UsesAlpha(PatchBlendMode.kMul).ShouldBeFalse();
            PatchBlending.UsesAlpha(PatchBlendMode.kBlendAbove).ShouldBeTrue();
            PatchBlending.UsesAlpha(PatchBlendMode.kBlendBelow).ShouldBeTrue();
            PatchBlending.UsesAlpha(PatchBlendMode.kAlphaWeightedAddAbove).ShouldBeTrue();
            PatchBlending.UsesAlpha(PatchBlendMode.kAlphaWeightedAddBelow).ShouldBeTrue();

            PatchBlending.UsesClamp(PatchBlendMode.kNone).ShouldBeFalse();
            PatchBlending.UsesClamp(PatchBlendMode.kMul).ShouldBeTrue();
            PatchBlending.UsesClamp(PatchBlendMode.kBlendAbove).ShouldBeTrue();

            using ReferenceFrame refFrame = new ReferenceFrame
            {
                Frame     = new ImageBundle(JxlMemoryManager.Default),
                IbIsInXyb = true,
            };

            refFrame.Frame.ShouldNotBeNull();
            refFrame.Dispose();
            refFrame.Frame.ShouldBeNull();

            PatchDictionary dict    = new PatchDictionary(JxlMemoryManager.Default);
            ReferenceFrame[] shared = CreateReferenceFrames(8, 8, true);

            dict.SetShared(shared);
            dict.ReferenceFrames.ShouldBeSameAs(shared);
            dict.HasAny().ShouldBeFalse();

            PatchPosition[] pos          = { new PatchPosition { X = 1, Y = 1, RefPosIdx = 0 } };
            PatchReferencePosition[] refs = { new PatchReferencePosition { Ref = 2, X0 = 0, Y0 = 0, XSize = 2, YSize = 2 } };
            PatchBlending[] blendings    = { new PatchBlending { Mode = PatchBlendMode.kAdd, AlphaChannel = 0, Clamp = false } };

            dict.SetPositions(pos, refs, blendings, 1);
            dict.HasAny().ShouldBeTrue();
            dict.Positions.Count.ShouldBe(1);
            dict.RefPositions.Count.ShouldBe(1);
            dict.Blendings.Count.ShouldBe(1);
            dict.BlendingsStride.ShouldBe((nuint)1);
            dict.GetReferences().ShouldBe(1 << 2);

            dict.Clear();
            dict.HasAny().ShouldBeFalse();
            dict.Positions.Count.ShouldBe(0);
            DisposeReferenceFrames(shared);
        }

        /// <summary>
        /// Verifies the interval tree construction and row query logic in <see cref="PatchDictionary.GetPatchesForRow"/>.
        /// </summary>
        [Test]
        public void TestPatchDictionaryIntervalTreeAndGetPatchesForRow()
        {
            PatchDictionary dict              = new PatchDictionary(JxlMemoryManager.Default);
            List<PatchPosition> posList       = new List<PatchPosition>();
            List<PatchReferencePosition> refs = new List<PatchReferencePosition>();
            List<PatchBlending> blendings     = new List<PatchBlending>();

            for (nuint i = 0; i < 16; i++)
            {
                refs.Add(new PatchReferencePosition
                {
                    Ref   = i & 3u,
                    X0    = 0,
                    Y0    = 0,
                    XSize = 3,
                    YSize = (i % 5u) + 2u,
                });
                posList.Add(new PatchPosition
                {
                    X         = i,
                    Y         = i * 2u,
                    RefPosIdx = i,
                });
                blendings.Add(new PatchBlending
                {
                    Mode         = PatchBlendMode.kAdd,
                    AlphaChannel = 0,
                    Clamp        = false,
                });
            }

            dict.SetPositions(posList, refs, blendings, 1);
            dict.GetReferences().ShouldBe(0b1111);

            for (nuint y = 0; y < 40; y++)
            {
                List<nuint> actual = dict.GetPatchesForRow(y);

                VerifyRowPatches(posList, refs, y, actual);
            }
        }

        /// <summary>
        /// Verifies <see cref="PatchDictionary.AddOneRow"/> across all <see cref="PatchBlendMode"/> modes and error conditions.
        /// </summary>
        [Test]
        public void TestPatchDictionaryAddOneRowAllBlendModes()
        {
            PatchDictionary dict = new PatchDictionary(JxlMemoryManager.Default);
            ExtraChannelInfo eci = CreateAlphaExtraChannelInfo(alphaAssociated: true);
            List<ExtraChannelInfo> ecList = new List<ExtraChannelInfo> { eci };

            float* dummyPtr = stackalloc float[16];
            float** ptrs    = stackalloc float*[4];
            ptrs[0] = dummyPtr;
            ptrs[1] = dummyPtr;
            ptrs[2] = dummyPtr;
            ptrs[3] = dummyPtr;

            dict.AddOneRow(ptrs, 0, 0, 4, ecList).IsOk.ShouldBeFalse();

            PatchPosition[] pos           = { new PatchPosition { X = 0, Y = 0, RefPosIdx = 0 } };
            PatchReferencePosition[] refs = { new PatchReferencePosition { Ref = 0, X0 = 0, Y0 = 0, XSize = 4, YSize = 2 } };
            PatchBlending[] blendings     =
            {
                new PatchBlending { Mode = PatchBlendMode.kAdd },
                new PatchBlending { Mode = PatchBlendMode.kAdd },
            };

            dict.SetPositions(pos, refs, blendings, 2);
            dict.AddOneRow(ptrs, 0, 0, 4, ecList).IsOk.ShouldBeFalse();

            ReferenceFrame[] shared = CreateReferenceFramesWithAlpha(8, 8, alphaAssociated: true);
            dict.SetShared(shared);

            dict.AddOneRow(ptrs, 10, 0, 4, ecList).IsOk.ShouldBeTrue();
            dict.AddOneRow(ptrs, 0, 6, 2, ecList).IsOk.ShouldBeTrue();

            PatchBlendMode[] modes =
            {
                PatchBlendMode.kNone,
                PatchBlendMode.kReplace,
                PatchBlendMode.kAdd,
                PatchBlendMode.kMul,
                PatchBlendMode.kBlendAbove,
                PatchBlendMode.kBlendBelow,
                PatchBlendMode.kAlphaWeightedAddAbove,
                PatchBlendMode.kAlphaWeightedAddBelow,
            };

            foreach (PatchBlendMode mode in modes)
            {
                RunAddOneRowForMode(dict, refs, ecList, mode, clamp: false);
                RunAddOneRowForMode(dict, refs, ecList, mode, clamp: true);
            }

            ExtraChannelInfo unpremulEci = CreateAlphaExtraChannelInfo(alphaAssociated: false);
            List<ExtraChannelInfo> unpremulList = new List<ExtraChannelInfo> { unpremulEci };

            RunAddOneRowForMode(dict, refs, unpremulList, PatchBlendMode.kBlendAbove, clamp: true);
            RunAddOneRowForMode(dict, refs, unpremulList, PatchBlendMode.kBlendBelow, clamp: false);

            DisposeReferenceFrames(shared);
        }

        /// <summary>
        /// Verifies <see cref="PatchDictionary.Decode"/> on valid and malformed ANS prefix-coded streams.
        /// </summary>
        [Test]
        public void TestPatchDictionaryDecodeValidAndErrorStreams()
        {
            ReferenceFrame[] shared = CreateReferenceFramesWithAlpha(16, 16, alphaAssociated: true);
            PatchDictionary dict    = new PatchDictionary(JxlMemoryManager.Default);
            dict.SetShared(shared);

            uint[] validSymbols =
            {
                1u,                                // numRefPatch = 1
                0u,                                // ref = 0
                1u,                                // x0 = 1
                1u,                                // y0 = 1
                2u,                                // xsize - 1 = 2 (xsize = 3)
                2u,                                // ysize - 1 = 2 (ysize = 3)
                1u,                                // idCount - 1 = 1 (2 occurrences)
                2u,                                // occ 0: x = 2
                2u,                                // occ 0: y = 2
                (uint)PatchBlendMode.kAdd,         // occ 0 color mode
                (uint)PatchBlendMode.kBlendAbove,  // occ 0 ec0 mode
                0u,                                // occ 0 ec0 alpha channel
                1u,                                // occ 0 ec0 clamp
                (uint)PatchBlendMode.kMul,         // occ 0 ec1 mode
                1u,                                // occ 0 ec1 clamp
                PackSigned.Pack(2),                // occ 1: deltaX = +2
                PackSigned.Pack(1),                // occ 1: deltaY = +1
                (uint)PatchBlendMode.kReplace,     // occ 1 color mode
                (uint)PatchBlendMode.kAlphaWeightedAddBelow, // occ 1 ec0 mode
                1u,                                // occ 1 ec0 alpha channel
                0u,                                // occ 1 ec0 clamp
                (uint)PatchBlendMode.kNone,        // occ 1 ec1 mode
            };

            byte[] validBytes      = BuildSingleHistogramPrefixStream(10, validSymbols);
            bool usesExtraChannels = false;
            BitReader br           = new BitReader(validBytes);
            Status decodeStatus    = dict.Decode(JxlMemoryManager.Default, br, 16, 16, 2, ref usesExtraChannels);
            br.Close();

            decodeStatus.IsOk.ShouldBeTrue();
            usesExtraChannels.ShouldBeTrue();
            dict.HasAny().ShouldBeTrue();
            dict.Positions.Count.ShouldBe(2);
            dict.RefPositions.Count.ShouldBe(1);

            CheckPatchDecodeFailure(dict, new uint[] { 2000u }, 8, 8, 0);
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 4u }, 8, 8, 0);
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 0u, 14u, 0u, 4u, 4u }, 16, 16, 0);
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 0u, 0u, 0u, 2u, 2u, 0u, 15u, 0u }, 16, 16, 0);
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 0u, 0u, 0u, 1u, 1u, 0u, 0u, 0u, 9u }, 16, 16, 0);
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 0u, 0u, 0u, 1u, 1u, 0u, 0u, 0u, 4u, 5u, 0u }, 16, 16, 2);

            shared[0].IbIsInXyb = false;
            CheckPatchDecodeFailure(dict, new uint[] { 1u, 0u, 0u, 0u, 1u, 1u }, 16, 16, 0);

            DisposeReferenceFrames(shared);
        }

        // =====================================================================
        // 2. DecExternalImage and ExternalConvertOptions Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="ExternalConvertOptions"/>, <see cref="DecExternalImage.FloatToU32"/>,
        /// and <see cref="DecExternalImage.FloatToF16"/>.
        /// </summary>
        [Test]
        public void TestExternalConvertOptionsAndFloatConversions()
        {
            byte* dummyBuf = stackalloc byte[64];
            ExternalConvertOptions opts = new ExternalConvertOptions(
                16,
                false,
                JxlEndianness.BigEndian,
                32,
                null,
                dummyBuf,
                64);

            PixelCallback cb = CreateNoOpPixelCallback();
            ExternalConvertOptions updated = opts.WithCallbackAndOrientation(cb, Orientation.Rotate90);

            updated.BitsPerSample.ShouldBe((nuint)16);
            updated.Endianness.ShouldBe(JxlEndianness.BigEndian);
            updated.UndoOrientation.ShouldBe(Orientation.Rotate90);
            updated.OutCallback.IsPresent().ShouldBeTrue();

            float* inFloats = stackalloc float[4] { float.NaN, -0.25f, 0.5f, 1.5f };
            uint* outU32    = stackalloc uint[4];
            ushort* outF16  = stackalloc ushort[4];

            DecExternalImage.FloatToU32(inFloats, outU32, 4, 255.0f, 8);
            outU32[0].ShouldBe(0u);
            outU32[1].ShouldBe(0u);
            outU32[2].ShouldBe(128u);
            outU32[3].ShouldBe(255u);

            DecExternalImage.FloatToU32(inFloats, outU32, 4, 4294967295.0f, 32);
            outU32[0].ShouldBe(0u);
            outU32[3].ShouldBe(uint.MaxValue);

            DecExternalImage.FloatToF16(inFloats, outF16, 4);
            BitConverter.UInt16BitsToHalf(outF16[2]).ShouldBe((Half)0.5f);
        }

        /// <summary>
        /// Verifies validation error paths and null-channel duplication in <see cref="DecExternalImage.ConvertChannelsToExternal"/>.
        /// </summary>
        [Test]
        public void TestConvertChannelsToExternalValidationAndDuplication()
        {
            using ImagePlane<float> p0 = CreateFilledPlane(4, 3, 0.25f);
            using ImagePlane<float> p3 = CreateFilledPlane(4, 3, 0.75f);
            byte[] dst                 = new byte[4 * 3 * 4];

            fixed (byte* dstPtr = dst)
            {
                ExternalConvertOptions validOpts = new ExternalConvertOptions(
                    8,
                    false,
                    JxlEndianness.LittleEndian,
                    16,
                    null,
                    dstPtr,
                    (nuint)dst.Length);

                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 0, in validOpts).IsOk.ShouldBeFalse();
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 5, in validOpts).IsOk.ShouldBeFalse();
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { null }, 1, in validOpts).IsOk.ShouldBeFalse();

                ExternalConvertOptions badFloatOpts = new ExternalConvertOptions(8, true, JxlEndianness.LittleEndian, 16, null, dstPtr, 48);
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 1, in badFloatOpts).IsOk.ShouldBeFalse();

                ExternalConvertOptions badIntOpts = new ExternalConvertOptions(48, false, JxlEndianness.LittleEndian, 16, null, dstPtr, 48);
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 1, in badIntOpts).IsOk.ShouldBeFalse();

                ExternalConvertOptions smallStrideOpts = new ExternalConvertOptions(8, false, JxlEndianness.LittleEndian, 2, null, dstPtr, 48);
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 1, in smallStrideOpts).IsOk.ShouldBeFalse();

                ExternalConvertOptions smallSizeOpts = new ExternalConvertOptions(8, false, JxlEndianness.LittleEndian, 16, null, dstPtr, 20);
                DecExternalImage.ConvertChannelsToExternal(new ImagePlane<float>?[] { p0 }, 1, in smallSizeOpts).IsOk.ShouldBeFalse();

                ImagePlane<float>?[] dupChannels = { p0, null, null, p3 };
                DecExternalImage.ConvertChannelsToExternal(dupChannels, 4, in validOpts).IsOk.ShouldBeTrue();
                dst[0].ShouldBe(dst[1]);
                dst[1].ShouldBe(dst[2]);
                dst[3].ShouldBe((byte)191);
            }
        }

        /// <summary>
        /// Verifies <see cref="DecExternalImage.ConvertChannelsToExternal"/> across all bit depths, endiannesses,
        /// orientations, and buffer/callback outputs.
        /// </summary>
        [Test]
        public void TestConvertChannelsToExternalAllFormatsAndOrientations()
        {
            using ImagePlane<float> c0 = CreateGradientPlane(4, 3, 0.1f);
            using ImagePlane<float> c1 = CreateGradientPlane(4, 3, 0.2f);
            using ImagePlane<float> c2 = CreateGradientPlane(4, 3, 0.3f);
            ImagePlane<float>?[] chans = { c0, c1, c2 };

            Orientation[] orientations =
            {
                Orientation.Identity,
                Orientation.FlipHorizontal,
                Orientation.Rotate180,
                Orientation.FlipVertical,
                Orientation.Transpose,
                Orientation.Rotate90,
                Orientation.AntiTranspose,
                Orientation.Rotate270,
            };

            nuint[] intBits      = { 8, 16, 24, 32 };
            nuint[] floatBits    = { 16, 32 };
            JxlEndianness[] ends = { JxlEndianness.LittleEndian, JxlEndianness.BigEndian };

            foreach (Orientation orientation in orientations)
            {
                foreach (JxlEndianness end in ends)
                {
                    RunExternalIntFormats(chans, intBits, end, orientation);
                    RunExternalFloatFormats(chans, floatBits, end, orientation);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="DecExternalImage.ConvertToExternal"/> on <see cref="ImageBundle"/> with premultiplied
        /// and synthesized opaque alpha planes.
        /// </summary>
        [Test]
        public void TestConvertToExternalImageBundle()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            ImageMetadata meta  = new ImageMetadata();
            meta.ColorEncoding  = ColorEncoding.SRGB(isGray: false);
            ExtraChannelInfo alphaInfo = CreateAlphaExtraChannelInfo(alphaAssociated: true);
            meta.ExtraChannelInfo.Add(alphaInfo);

            using ImageBundle bundle = new ImageBundle(mm, meta);
            Image3<float> color      = Image3<float>.Create(mm, 4, 3).Value;
            ImageOps.FillImage(0.4f, color);
            bundle.SetFromImage(color, meta.ColorEncoding).IsOk.ShouldBeTrue();

            ImagePlane<float> alpha = CreateFilledPlane(4, 3, 0.5f);
            bundle.SetAlpha(alpha).IsOk.ShouldBeTrue();

            byte[] outBuf = new byte[4 * 3 * 4];

            fixed (byte* ptr = outBuf)
            {
                ExternalConvertOptions opts = new ExternalConvertOptions(
                    8,
                    false,
                    JxlEndianness.NativeEndian,
                    16,
                    null,
                    ptr,
                    (nuint)outBuf.Length);

                DecExternalImage.ConvertToExternal(bundle, 4, unpremulAlpha: true, in opts).IsOk.ShouldBeTrue();
                outBuf[0].ShouldBe((byte)204);
                outBuf[3].ShouldBe((byte)128);
            }

            ImageMetadata noAlphaMeta = new ImageMetadata();
            noAlphaMeta.ColorEncoding = ColorEncoding.SRGB(isGray: false);
            using ImageBundle noAlphaBundle = new ImageBundle(mm, noAlphaMeta);
            Image3<float> color2            = Image3<float>.Create(mm, 4, 3).Value;
            ImageOps.FillImage(0.5f, color2);
            noAlphaBundle.SetFromImage(color2, noAlphaMeta.ColorEncoding).IsOk.ShouldBeTrue();

            fixed (byte* ptr = outBuf)
            {
                ExternalConvertOptions opts = new ExternalConvertOptions(
                    8,
                    false,
                    JxlEndianness.LittleEndian,
                    16,
                    null,
                    ptr,
                    (nuint)outBuf.Length);

                DecExternalImage.ConvertToExternal(noAlphaBundle, 4, unpremulAlpha: false, in opts).IsOk.ShouldBeTrue();
                outBuf[3].ShouldBe((byte)255);
            }
        }

        // =====================================================================
        // 3. RenderPipelineStages Tests
        // =====================================================================

        /// <summary>
        /// Verifies Gaborish, EPF (0/1/2), chroma upsampling, and general upsampling pipeline stages.
        /// </summary>
        [Test]
        public void TestGaborishEpfAndUpsamplingStages()
        {
            LoopFilter lf = new LoopFilter { Gab = false, EpfIters = 0 };
            RenderPipelineStages.GetGaborishStage(lf).ShouldBeNull();

            using ImagePlane<float> sigma = CreateFilledPlane(8, 8, -0.5f);
            RenderPipelineStages.GetEPFStage(lf, sigma, EpfStage.Zero).ShouldBeNull();

            lf.Gab      = true;
            lf.EpfIters = 3;
            using RenderPipelineStage gab  = RenderPipelineStages.GetGaborishStage(lf)!;
            using RenderPipelineStage epf0 = RenderPipelineStages.GetEPFStage(lf, sigma, EpfStage.Zero)!;
            using RenderPipelineStage epf1 = RenderPipelineStages.GetEPFStage(lf, sigma, EpfStage.One)!;
            using RenderPipelineStage epf2 = RenderPipelineStages.GetEPFStage(lf, sigma, EpfStage.Two)!;
            RenderPipelineStages.GetEPFStage(lf, sigma, (EpfStage)99).ShouldBeNull();

            gab.GetName().ShouldBe("Gab");
            RunStageOnSyntheticBuffers(gab, 4, 8, borderY: 1, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(epf0, 4, 8, borderY: 3, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(epf1, 4, 8, borderY: 2, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(epf2, 4, 8, borderY: 1, shiftY: 0).IsOk.ShouldBeTrue();

            ImageOps.FillImage(-10.0f, sigma);
            RunStageOnSyntheticBuffers(epf0, 4, 8, borderY: 3, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(epf1, 4, 8, borderY: 2, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(epf2, 4, 8, borderY: 1, shiftY: 0).IsOk.ShouldBeTrue();

            using RenderPipelineStage hChroma = RenderPipelineStages.GetChromaUpsamplingStage(0, horizontal: true);
            using RenderPipelineStage vChroma = RenderPipelineStages.GetChromaUpsamplingStage(2, horizontal: false);
            RunStageOnSyntheticBuffers(hChroma, 3, 8, borderY: 0, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(vChroma, 3, 8, borderY: 1, shiftY: 1).IsOk.ShouldBeTrue();

            CustomTransformData transformData = new CustomTransformData();
            RenderPipelineStages.GetUpsamplingStage(JxlMemoryManager.Default, transformData, 0, 0).ShouldBeNull();
            RenderPipelineStages.GetUpsamplingStage(JxlMemoryManager.Default, transformData, 0, 4).ShouldBeNull();

            for (nuint shift = 1; shift <= 3; shift++)
            {
                using RenderPipelineStage ups = RenderPipelineStages.GetUpsamplingStage(
                    JxlMemoryManager.Default,
                    transformData,
                    0,
                    shift)!;
                ups.PrepareForThreads(1).IsOk.ShouldBeTrue();
                RunStageOnSyntheticBuffers(ups, 3, 8, borderY: 2, shiftY: (int)shift).IsOk.ShouldBeTrue();
                RunStageOnSyntheticBuffers(ups, 3, 8, borderY: 2, shiftY: (int)shift, xextra: 1).IsOk.ShouldBeFalse();
            }
        }

        /// <summary>
        /// Verifies patches, splines, noise, YCbCr, XYB, fast XYB, and spot color pipeline stages.
        /// </summary>
        [Test]
        public void TestPatchesSplineNoiseYCbCrXybAndSpotStages()
        {
            PatchDictionary patches = new PatchDictionary(JxlMemoryManager.Default);
            ReferenceFrame[] shared = CreateReferenceFrames(8, 8, true);
            patches.SetShared(shared);
            patches.SetPositions(Array.Empty<PatchPosition>(), Array.Empty<PatchReferencePosition>(), Array.Empty<PatchBlending>(), 1);
            using RenderPipelineStage patchStage = RenderPipelineStages.GetPatchesStage(patches, Array.Empty<ExtraChannelInfo>());
            patchStage.GetName().ShouldNotBeEmpty();
            RunStageOnSyntheticBuffers(patchStage, 3, 8, 0, 0).IsOk.ShouldBeTrue();
            DisposeReferenceFrames(shared);

            using Splines splines = CreateSampleSplines();
            splines.InitializeDrawCache(16, 16, new ColorCorrelation()).IsOk.ShouldBeTrue();
            using RenderPipelineStage splineStage = RenderPipelineStages.GetSplineStage(splines);
            RunStageOnSyntheticBuffers(splineStage, 3, 8, 0, 0).IsOk.ShouldBeTrue();

            NoiseParams emptyNoise  = new NoiseParams();
            NoiseParams activeNoise = new NoiseParams();
            activeNoise.Lut[0] = 0.01f;
            activeNoise.Lut[1] = 0.02f;

            using RenderPipelineStage convNoise = RenderPipelineStages.GetConvolveNoiseStage(3);
            using RenderPipelineStage addEmpty  = RenderPipelineStages.GetAddNoiseStage(emptyNoise, new ColorCorrelation(), 3);
            using RenderPipelineStage addActive = RenderPipelineStages.GetAddNoiseStage(activeNoise, new ColorCorrelation(), 3);

            RunStageOnSyntheticBuffers(convNoise, 6, 8, borderY: 2, shiftY: 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(convNoise, 6, 8, borderY: 2, shiftY: 0, xextra: 1).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(addEmpty, 6, 8, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(addActive, 6, 8, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(addActive, 6, 8, 0, 0, xextra: 1).IsOk.ShouldBeFalse();

            using RenderPipelineStage ycbcr = RenderPipelineStages.GetYCbCrStage();
            RunStageOnSyntheticBuffers(ycbcr, 3, 10, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(ycbcr, 3, 10, 0, 0, xextra: 1).IsOk.ShouldBeFalse();

            CodecMetadata meta = CreateDefaultCodecMetadata(16, 16, xybEncoded: true);
            OutputEncodingInfo outInfo = new OutputEncodingInfo();
            outInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

            using RenderPipelineStage xybLinear = RenderPipelineStages.GetXYBStage(outInfo);
            RunStageOnSyntheticBuffers(xybLinear, 3, 7, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(xybLinear, 3, 7, 0, 0, xextra: 1).IsOk.ShouldBeFalse();

            ColorEncoding xybEnc = new ColorEncoding();
            xybEnc.SetColorSpace(ColorSpace.XYB);
            outInfo.MaybeSetColorEncoding(xybEnc).IsOk.ShouldBeTrue();
            using RenderPipelineStage xybScaled = RenderPipelineStages.GetXYBStage(outInfo);
            xybScaled.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (8, 8) }).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(xybScaled, 3, 7, 0, 0).IsOk.ShouldBeTrue();

            using RenderPipelineStage spot = RenderPipelineStages.GetSpotColorStage(0, new float[] { 1.0f, 0.0f, 0.0f, 0.5f });
            RunStageOnSyntheticBuffers(spot, 4, 7, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(spot, 4, 7, 0, 0, xextra: 1).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="RenderPipelineStages.GetToLinearStage"/>, <see cref="RenderPipelineStages.GetFromLinearStage"/>,
        /// <see cref="RenderPipelineStages.GetToneMappingStage"/>, and <see cref="RenderPipelineStages.GetCmsStage"/>.
        /// </summary>
        [Test]
        public void TestToLinearFromLinearToneMappingAndCmsStages()
        {
            TransferFunction[] tfs =
            {
                TransferFunction.Linear,
                TransferFunction.SRGB,
                TransferFunction.PQ,
                TransferFunction.HLG,
                TransferFunction.Tf709,
                TransferFunction.DCI,
            };

            foreach (TransferFunction tf in tfs)
            {
                OutputEncodingInfo info = CreateOutputEncodingWithTf(tf);
                using RenderPipelineStage toLin   = RenderPipelineStages.GetToLinearStage(info);
                using RenderPipelineStage fromLin = RenderPipelineStages.GetFromLinearStage(info)!;

                toLin.IsInitialized().IsOk.ShouldBeTrue();
                RunStageOnSyntheticBuffers(toLin, 3, 7, 0, 0).IsOk.ShouldBeTrue();
                RunStageOnSyntheticBuffers(toLin, 3, 7, 0, 0, xextra: 1).IsOk.ShouldBeFalse();
                RunStageOnSyntheticBuffers(fromLin, 3, 7, 0, 0).IsOk.ShouldBeTrue();
                RunStageOnSyntheticBuffers(fromLin, 3, 7, 0, 0, xextra: 1).IsOk.ShouldBeFalse();
            }

            OutputEncodingInfo gammaInfo = new OutputEncodingInfo();
            gammaInfo.ColorEncoding      = ColorEncoding.SRGB(isGray: false);
            gammaInfo.ColorEncoding.Tf.SetGamma(0.5);
            gammaInfo.InverseGamma       = 0.5f;
            using RenderPipelineStage gammaToLin   = RenderPipelineStages.GetToLinearStage(gammaInfo);
            using RenderPipelineStage gammaFromLin = RenderPipelineStages.GetFromLinearStage(gammaInfo)!;
            RunStageOnSyntheticBuffers(gammaToLin, 3, 7, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(gammaFromLin, 3, 7, 0, 0).IsOk.ShouldBeTrue();

            OutputEncodingInfo unknownTfInfo = new OutputEncodingInfo();
            unknownTfInfo.ColorEncoding.Tf.TransferFunction = TransferFunction.Unknown;
            unknownTfInfo.ColorEncoding.Tf.HaveGamma        = false;
            RenderPipelineStages.GetFromLinearStage(unknownTfInfo).ShouldBeNull();
            using RenderPipelineStage badToLin = RenderPipelineStages.GetToLinearStage(unknownTfInfo);
            badToLin.IsInitialized().IsOk.ShouldBeFalse();

            ExerciseToneMappingStages();
            ExerciseCmsStages();
        }

        // =====================================================================
        // 4. PassesStateAndOutputStages and RenderPipeline Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="ACImageT{T}"/> and <see cref="GroupDecCache"/> allocation and methods.
        /// </summary>
        [Test]
        public void TestACImageAndGroupDecCache()
        {
            Should.Throw<NotSupportedException>(() => new ACImageT<byte>());

            using ACImageT<short> empty16 = new ACImageT<short>();
            empty16.IsEmpty.ShouldBeTrue();
            empty16.Type.ShouldBe(ACType.k16);

            using ACImageT<short> ac16 = ACImageT<short>.Make(JxlMemoryManager.Default, 16, 4).Value;
            ac16.IsEmpty.ShouldBeFalse();
            ac16.PixelsPerRow.ShouldBeGreaterThanOrEqualTo((nuint)16);
            ac16.ZeroFill();
            ac16.ZeroFillPlane(0);
            ac16.PlaneRow(0, 0, 0).Ptr16[0] = 42;
            ac16.ConstPlaneRow(0, 0, 0).Ptr16[0].ShouldBe((short)42);

            using ACImageT<int> ac32 = ACImageT<int>.Make(JxlMemoryManager.Default, 16, 4).Value;
            ac32.Type.ShouldBe(ACType.k32);
            ac32.ZeroFill();
            ac32.ZeroFillPlane(1);
            ac32.PlaneRow(1, 0, 2).Ptr32[0] = 12345;
            ac32.ConstPlaneRow(1, 0, 2).Ptr32[0].ShouldBe(12345);

            using GroupDecCache cache = new GroupDecCache();
            uint acsMask              = (1u << (int)AcStrategyType.DCT) | (1u << (int)AcStrategyType.DCT16X16);
            cache.InitOnce(JxlMemoryManager.Default, 2, acsMask).IsOk.ShouldBeTrue();
            cache.InitDCBufferOnce(JxlMemoryManager.Default).IsOk.ShouldBeTrue();
            cache.DcBuffer.XSize.ShouldBeGreaterThan((nuint)0);
        }

        /// <summary>
        /// Verifies <see cref="PassesSharedState"/>, <see cref="PassesDecoderState"/>, and <see cref="PassesDecoderState.PreparePipeline"/>.
        /// </summary>
        [Test]
        public void TestPassesSharedStateAndPassesDecoderState()
        {
            using PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);
            CodecMetadata meta                = CreateDefaultCodecMetadata(16, 16, xybEncoded: true);
            ExtraChannelInfo alphaEci         = CreateAlphaExtraChannelInfo(alphaAssociated: false);
            ExtraChannelInfo spotEci          = new ExtraChannelInfo { Type = ExtraChannel.SpotColor };
            spotEci.SpotColor[0]              = 0.8f;
            spotEci.SpotColor[3]              = 0.5f;
            meta.M.ExtraChannelInfo.Add(alphaEci);
            meta.M.ExtraChannelInfo.Add(spotEci);
            meta.M.NumExtraChannels = 2;

            FrameHeader fh = new FrameHeader(meta)
            {
                Encoding = FrameEncoding.VarDCT,
                Flags    = FrameHeader.KPatches | FrameHeader.KSplines | FrameHeader.KNoise,
                DcLevel  = 1,
            };
            fh.LoopFilter.Gab      = true;
            fh.LoopFilter.EpfIters = 3;
            fh.Upsampling          = 2;
            fh.ExtraChannelUpsampling.Clear();
            fh.ExtraChannelUpsampling.Add(2);
            fh.ExtraChannelUpsampling.Add(2);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: true).IsOk.ShouldBeTrue();
            decState.OrUsedAcs(1u << (int)AcStrategyType.DCT);
            decState.Init(fh).IsOk.ShouldBeTrue();
            decState.InitForAC(1, null).IsOk.ShouldBeTrue();
            decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();
            decState.Width  = 16;
            decState.Height = 16;

            using ImageBundle decoded = new ImageBundle(JxlMemoryManager.Default, meta.M);
            PassesDecoderState.PipelineOptions pipeOpts = new PassesDecoderState.PipelineOptions
            {
                UseSlowRenderPipeline = true,
                Coalescing            = true,
                RenderSpotcolors      = true,
                RenderNoise           = true,
            };

            decState.PreparePipeline(fh, meta.M, decoded, pipeOpts).IsOk.ShouldBeTrue();
            decState.RenderPipeline.ShouldNotBeNull();

            fh.Flags   = FrameHeader.KUseDcFrame;
            fh.DcLevel = 4;
            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeFalse();

            fh.DcLevel = 0;
            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeFalse();

            decState.Shared.DcFrames[0] = Image3<float>.Create(JxlMemoryManager.Default, 2, 2).Value;
            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="RenderPipelineStages.GetBlendingStage"/>, <see cref="RenderPipelineStages.GetWriteToImage3FStage(JxlMemoryManager, Image3{float})"/>,
        /// and <see cref="RenderPipelineStages.GetWriteToOutputStage"/>.
        /// </summary>
        [Test]
        public void TestBlendingStageAndWriteToOutputStages()
        {
            using Image3<float> targetImg3 = Image3<float>.Create(JxlMemoryManager.Default, 8, 4).Value;
            using RenderPipelineStage write3F = RenderPipelineStages.GetWriteToImage3FStage(JxlMemoryManager.Default, targetImg3);
            write3F.SetInputSizes(new (nuint, nuint)[] { (8, 4), (8, 4), (8, 4) }).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(write3F, 3, 8, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(write3F, 3, 8, 0, 0, xextra: 1).IsOk.ShouldBeFalse();

            ExerciseBlendingStage();
            ExerciseWriteToOutputStages();
        }

        /// <summary>
        /// Verifies <see cref="GroupBorderAssigner"/> and multi-group <see cref="RenderPipeline"/> execution
        /// in both simple and low-memory modes.
        /// </summary>
        [Test]
        public void TestRenderPipelineAndGroupBorderAssigner()
        {
            FrameDimensions fdim = new FrameDimensions();
            fdim.Set(600, 600, 1, 0, 0, false, 1);

            GroupBorderAssigner assigner = new GroupBorderAssigner();
            assigner.Init(fdim);
            Span<Rect> rects = stackalloc Rect[GroupBorderAssigner.KMaxToFinalize];

            for (nuint g = 0; g < fdim.NumGroups; g++)
            {
                assigner.GroupDone(g, 2, 2, rects, out nuint count);
                count.ShouldBeLessThanOrEqualTo((nuint)GroupBorderAssigner.KMaxToFinalize);
            }

            assigner.ClearDone(0);
            assigner.GroupDone(0, 2, 2, rects, out nuint recount);
            recount.ShouldBeGreaterThan((nuint)0);

            RenderPipelineInput orphanInput = new RenderPipelineInput();
            orphanInput.Done().IsOk.ShouldBeFalse();

            RenderPipeline.Builder emptyBuilder = new RenderPipeline.Builder(JxlMemoryManager.Default, 3);
            emptyBuilder.AddStage(null).IsOk.ShouldBeFalse();
            emptyBuilder.Finalize(fdim).IsOk.ShouldBeFalse();

            RunMultiGroupPipelineWithBlending(useSimple: true);
            RunMultiGroupPipelineWithBlending(useSimple: false);
        }

        // =====================================================================
        // 5. JpegData, JpegDataDecoder, JpegDataWriter, JxlToJpegDecoder Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="JPEGData.VisitFields"/>, <see cref="JPEGData.SetJPEGDataFromICC"/>,
        /// and <see cref="JpegDataDecoder.DecodeJPEGData"/> with Brotli-compressed marker streams.
        /// </summary>
        [Test]
        public void TestJpegDataDecodeAndSetIcc()
        {
            JPEGData src     = CreateSampleJpegData(progressive: false);
            byte[] jbrdBytes = EncodeJpegDataToJbrdBytes(src);

            src.CalculateMcuSize(src.ScanInfo[0], out int mcusPerRow, out int mcuRows);
            mcusPerRow.ShouldBe(2);
            mcuRows.ShouldBe(2);

            JPEGData dst = new JPEGData();
            dst.BundleName.ShouldBe("JPEGData");
            JpegDataDecoder.DecodeJPEGData(jbrdBytes, dst).IsOk.ShouldBeTrue();

            dst.RestartInterval.ShouldBe(1u);
            dst.Components.Count.ShouldBe(3);
            dst.AppData.Count.ShouldBe(4);
            dst.ComData.Count.ShouldBe(1);

            byte[] iccBytes = { 10, 20, 30, 40 };
            JPEGData.SetJPEGDataFromICC(iccBytes, dst).IsOk.ShouldBeTrue();
            JPEGData.SetJPEGDataFromICC(new byte[] { 1, 2 }, dst).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="JpegDataWriter.WriteJpeg"/> for both sequential and progressive scans.
        /// </summary>
        [Test]
        public void TestJpegDataWriterSequentialAndProgressive()
        {
            JPEGData emptyJpg = new JPEGData();
            JpegDataWriter.WriteJpeg(emptyJpg, _ => 0).IsOk.ShouldBeFalse();

            JPEGData seqJpg      = CreateSampleJpegData(progressive: false);
            List<byte> seqOutput = new List<byte>();

            Status seqStatus = JpegDataWriter.WriteJpeg(
                seqJpg,
                chunk =>
                {
                    seqOutput.AddRange(chunk.ToArray());

                    return (nuint)chunk.Length;
                });

            seqStatus.IsOk.ShouldBeTrue();
            seqOutput.Count.ShouldBeGreaterThan(64);
            seqOutput[0].ShouldBe((byte)0xFF);
            seqOutput[1].ShouldBe((byte)0xD8);

            JpegDataWriter.WriteJpeg(seqJpg, _ => 0).IsOk.ShouldBeFalse();

            JPEGData progJpg      = CreateSampleJpegData(progressive: true);
            List<byte> progOutput = new List<byte>();

            Status progStatus = JpegDataWriter.WriteJpeg(
                progJpg,
                chunk =>
                {
                    progOutput.AddRange(chunk.ToArray());

                    return (nuint)chunk.Length;
                });

            progStatus.IsOk.ShouldBeTrue();
            progOutput.Count.ShouldBeGreaterThan(64);
        }

        /// <summary>
        /// Verifies <see cref="JxlToJpegDecoder"/> box parsing, Exif/XMP box injection, and output writing.
        /// </summary>
        [Test]
        public void TestJxlToJpegDecoder()
        {
            JPEGData src          = CreateSampleJpegData(progressive: false);
            byte[] jbrdBytes      = EncodeJpegDataToJbrdBytes(src);
            JxlToJpegDecoder jbrd = new JxlToJpegDecoder();

            byte* dummyPtr = null;
            nuint dummyLen = 0;
            jbrd.Process(ref dummyPtr, ref dummyLen).ShouldBe(JxlDecoderStatus.Error);

            jbrd.StartBox(boxUntilEof: false, (nuint)jbrdBytes.Length);
            jbrd.IsParsingBox.ShouldBeTrue();

            fixed (byte* inPtr = jbrdBytes)
            {
                byte* cur1 = inPtr;
                nuint rem1 = 4;
                jbrd.Process(ref cur1, ref rem1).ShouldBe(JxlDecoderStatus.NeedMoreInput);

                nuint rem2 = (nuint)(jbrdBytes.Length - 4);
                jbrd.Process(ref cur1, ref rem2).ShouldBe(JxlDecoderStatus.JpegReconstruction);
            }

            JPEGData decoded = jbrd.GetJpegData()!;
            decoded.ShouldNotBeNull();
            decoded.Width  = 16;
            decoded.Height = 16;

            for (int c = 0; c < decoded.Components.Count; c++)
            {
                decoded.Components[c].HSampFactor    = 1;
                decoded.Components[c].VSampFactor    = 1;
                decoded.Components[c].WidthInBlocks  = 2;
                decoded.Components[c].HeightInBlocks = 2;
                decoded.Components[c].Coeffs         = new short[4 * 64];
            }

            JxlToJpegDecoder.NumExifMarkers(decoded).ShouldBe((nuint)1);
            JxlToJpegDecoder.NumXmpMarkers(decoded).ShouldBe((nuint)1);

            JxlToJpegDecoder.ExifBoxContentSize(decoded, out nuint exifSize).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.XmlBoxContentSize(decoded, out nuint xmlSize).ShouldBe(JxlDecoderStatus.Success);

            byte[] exifBox = new byte[exifSize];
            byte[] xmlBox  = new byte[xmlSize];
            JxlToJpegDecoder.SetExif(exifBox, decoded).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.SetXmp(xmlBox, decoded).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.SetExif(new byte[2], decoded).ShouldBe(JxlDecoderStatus.Error);
            JxlToJpegDecoder.SetXmp(new byte[1], decoded).ShouldBe(JxlDecoderStatus.Error);

            byte[] outJpeg = new byte[4096];

            fixed (byte* outPtr = outJpeg)
            {
                jbrd.SetOutputBuffer(outPtr, 8).ShouldBe(JxlDecoderStatus.Success);
                jbrd.SetOutputBuffer(outPtr, 8).ShouldBe(JxlDecoderStatus.Error);
                jbrd.WriteOutput(decoded).ShouldBe(JxlDecoderStatus.JpegNeedMoreOutput);
                jbrd.ReleaseOutputBuffer().ShouldBe((nuint)8);

                jbrd.SetOutputBuffer(outPtr, (nuint)outJpeg.Length).ShouldBe(JxlDecoderStatus.Success);

                ImageMetadata meta = new ImageMetadata();
                meta.ColorEncoding.SetSRGB(ColorSpace.RGB);
                using ImageBundle ib = new ImageBundle(JxlMemoryManager.Default, meta);
                byte[] icc           = { 1, 2, 3, 4 };
                JPEGData.SetJPEGDataFromICC(icc, decoded).IsOk.ShouldBeTrue();

                jbrd.WriteOutput(decoded).ShouldBe(JxlDecoderStatus.Success);
                nuint unused = jbrd.ReleaseOutputBuffer();
                unused.ShouldBeLessThan((nuint)outJpeg.Length);
            }
        }

        // =====================================================================
        // 6. IccCodec, ICCReader, IccCodecCommon, and IccTag Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="IccTag"/> and <see cref="IccCodecCommon"/> prediction and context functions.
        /// </summary>
        [Test]
        public void TestIccTagAndIccCodecCommon()
        {
            IccTag tagA = new IccTag('r', 'X', 'Y', 'Z');
            IccTag tagB = new IccTag((byte)'r', (byte)'X', (byte)'Y', (byte)'Z');
            IccTag tagC = new IccTag('g', 'X', 'Y', 'Z');

            (tagA == tagB).ShouldBeTrue();
            (tagA != tagC).ShouldBeTrue();
            tagA.Equals((object)tagB).ShouldBeTrue();
            tagA.Equals((object)"not a tag").ShouldBeFalse();
            tagA.GetHashCode().ShouldBe(tagB.GetHashCode());
            tagA[0].ShouldBe((byte)'r');
            tagA[1].ShouldBe((byte)'X');
            tagA[2].ShouldBe((byte)'Y');
            tagA[3].ShouldBe((byte)'Z');

            using PaddedBytes pb = new PaddedBytes(JxlMemoryManager.Default);
            IccCodecCommon.AppendUint32(0x12345678u, pb).IsOk.ShouldBeTrue();
            IccCodecCommon.AppendKeyword(tagA, pb).IsOk.ShouldBeTrue();

            IccCodecCommon.DecodeUint32(pb.Span, pb.Size, 0).ShouldBe(0x12345678u);
            IccCodecCommon.DecodeUint32(pb.Span, pb.Size, 7).ShouldBe(0u);
            IccCodecCommon.DecodeKeyword(pb.Span, pb.Size, 4).ShouldBe(tagA);
            IccCodecCommon.DecodeKeyword(pb.Span, pb.Size, 7).ShouldBe(new IccTag(' ', ' ', ' ', ' '));

            Span<byte> kwBuf = stackalloc byte[6];
            IccCodecCommon.EncodeKeyword(tagC, kwBuf, 6, 0);
            IccCodecCommon.EncodeKeyword(tagC, kwBuf, 6, 4);
            IccCodecCommon.DecodeKeyword(kwBuf, 6, 0).ShouldBe(tagC);

            IccCodecCommon.CheckOutOfBounds(10, 20, 30).IsOk.ShouldBeTrue();
            IccCodecCommon.CheckOutOfBounds(10, 25, 30).IsOk.ShouldBeFalse();
            IccCodecCommon.CheckOutOfBounds(ulong.MaxValue, 2, 30).IsOk.ShouldBeFalse();
            IccCodecCommon.CheckIs32Bit(0xFFFFFFFFUL).IsOk.ShouldBeTrue();
            IccCodecCommon.CheckIs32Bit(0x100000000UL).IsOk.ShouldBeFalse();

            ExerciseIccPredictionPrimitives();
        }

        /// <summary>
        /// Verifies <see cref="IccCodec.PredictICC"/>, <see cref="IccCodec.UnpredictICC"/>,
        /// <see cref="IccCodec.WriteICC"/>, and <see cref="ICCReader"/> on short and multi-tag ICC profiles.
        /// </summary>
        [Test]
        public void TestIccCodecPredictUnpredictAndReader()
        {
            byte[] shortIcc = IccCodecCommon.ICCInitialHeaderPrediction(64);
            Array.Resize(ref shortIcc, 64);
            VerifyIccRoundTrip(shortIcc);

            byte[] richIcc = BuildComprehensiveSyntheticIccProfile();
            VerifyIccRoundTrip(richIcc);

            BitWriter emptyWriter = new BitWriter(JxlMemoryManager.Default);
            IccCodec.WriteICC(ReadOnlySpan<byte>.Empty, emptyWriter, LayerType.Header, null).IsOk.ShouldBeFalse();

            using PaddedBytes nonEmptyOut = new PaddedBytes(JxlMemoryManager.Default);
            nonEmptyOut.Resize(4);
            IccCodec.UnpredictICC(shortIcc, nonEmptyOut).IsOk.ShouldBeFalse();

            using PaddedBytes badPreamble = new PaddedBytes(JxlMemoryManager.Default);
            badPreamble.Resize(20);
            badPreamble[0] = 10;
            badPreamble[1] = 200;
            badPreamble[2] = 200;
            IccCodec.CheckPreamble(badPreamble, 100000).IsOk.ShouldBeFalse();
        }

        // =====================================================================
        // 7. Alpha, Blending, ImageBundle, AuxOut, LoopFilter, Splines, Xyb
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="Alpha"/> and <see cref="Blending"/> primitives across all modes and flags.
        /// </summary>
        [Test]
        public void TestAlphaAndBlendingPrimitives()
        {
            float* bgR = stackalloc float[4] { 0.2f, 0.4f, 0.6f, 0.8f };
            float* bgG = stackalloc float[4] { 0.1f, 0.3f, 0.5f, 0.7f };
            float* bgB = stackalloc float[4] { 0.3f, 0.5f, 0.7f, 0.9f };
            float* bgA = stackalloc float[4] { 0.5f, 0.5f, 0.5f, 0.5f };

            float* fgR = stackalloc float[4] { 0.8f, 0.6f, 0.4f, 0.2f };
            float* fgG = stackalloc float[4] { 0.7f, 0.5f, 0.3f, 0.1f };
            float* fgB = stackalloc float[4] { 0.9f, 0.7f, 0.5f, 0.3f };
            float* fgA = stackalloc float[4] { -0.2f, 0.5f, 0.8f, 1.4f };

            float* outR = stackalloc float[4];
            float* outG = stackalloc float[4];
            float* outB = stackalloc float[4];
            float* outA = stackalloc float[4];

            AlphaBlendingInputLayer bgLayer = new AlphaBlendingInputLayer(bgR, bgG, bgB, bgA);
            AlphaBlendingInputLayer fgLayer = new AlphaBlendingInputLayer(fgR, fgG, fgB, fgA);
            AlphaBlendingOutput outLayer    = new AlphaBlendingOutput(outR, outG, outB, outA);

            Alpha.PerformAlphaBlending(in bgLayer, in fgLayer, in outLayer, 4, alphaIsPremultiplied: true, clamp: true);
            Alpha.PerformAlphaBlending(in bgLayer, in fgLayer, in outLayer, 4, alphaIsPremultiplied: false, clamp: true);

            Alpha.PerformAlphaBlending(bgA, bgA, fgA, fgA, outA, 4, (true, true));
            Alpha.PerformAlphaBlending(bgR, bgA, fgR, fgA, outR, 4, (true, true));
            Alpha.PerformAlphaBlending(bgR, bgA, fgR, fgA, outR, 4, (false, true));

            Alpha.PerformAlphaWeightedAdd(bgR, fgA, fgA, outR, 4, clamp: true);
            Alpha.PerformAlphaWeightedAdd(bgR, fgR, fgA, outR, 4, clamp: true);
            Alpha.PerformAlphaWeightedAdd(bgR, fgR, fgA, outR, 4, clamp: false);

            Alpha.PerformMulBlending(bgR, fgA, outR, 4, clamp: true);
            Alpha.PerformMulBlending(bgR, fgA, outR, 4, clamp: false);

            Alpha.PremultiplyAlpha(outR, outG, outB, bgA, 4);
            Alpha.UnpremultiplyAlpha(outR, outG, outB, bgA, 4);

            CodecMetadata meta = CreateDefaultCodecMetadata(8, 8, xybEncoded: false);
            FrameHeader fh     = new FrameHeader(meta) { FrameType = FrameType.DCFrame };
            Blending.NeedsBlending(fh).ShouldBeFalse();

            fh.FrameType         = FrameType.RegularFrame;
            fh.BlendingInfo.Mode = BlendMode.Replace;
            Blending.NeedsBlending(fh).ShouldBeFalse();

            fh.CustomSizeOrOrigin = true;
            Blending.NeedsBlending(fh).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ImageBundle"/> metadata verification, cloning, shrinking, orientation,
        /// and color space transformations (RGB, Gray, CMYK).
        /// </summary>
        [Test]
        public void TestImageBundleOperations()
        {
            JxlMemoryManager mm = JxlMemoryManager.Default;
            ImageMetadata meta  = new ImageMetadata();
            meta.ColorEncoding  = ColorEncoding.SRGB(isGray: false);
            meta.Orientation    = 6; // Rotate90 (> 4)
            meta.ExtraChannelInfo.Add(CreateAlphaExtraChannelInfo(alphaAssociated: true));
            meta.ExtraChannelInfo.Add(new ExtraChannelInfo { Type = ExtraChannel.Black });

            using ImageBundle emptyIb = new ImageBundle(mm);
            emptyIb.VerifyMetadata().IsOk.ShouldBeFalse();

            using ImageBundle ib = new ImageBundle(mm, meta);

            Image3<float> color = Image3<float>.Create(mm, 6, 4).Value;
            ImageOps.FillImage(0.5f, color);
            ib.SetFromImage(color, meta.ColorEncoding).IsOk.ShouldBeTrue();

            List<ImagePlane<float>> ecs = new List<ImagePlane<float>>
            {
                CreateFilledPlane(6, 4, 0.8f),
                CreateFilledPlane(6, 4, 0.2f),
            };
            ib.SetExtraChannels(ecs).IsOk.ShouldBeTrue();

            ib.OrientedXSize().ShouldBe((nuint)4);
            ib.OrientedYSize().ShouldBe((nuint)6);
            ib.HasAlpha().ShouldBeTrue();
            ib.AlphaIsPremultiplied().ShouldBeTrue();
            ib.Alpha().ShouldNotBeNull();
            ib.HasBlack().ShouldBeTrue();
            ib.Black().ShouldNotBeNull();
            ib.IsSRGB().ShouldBeTrue();
            ib.IsLinearSRGB().ShouldBeFalse();
            ib.DetectRealBitdepth().ShouldBeGreaterThan((nuint)0);
            ib.SetDecodedBytes(123);
            ib.DecodedBytes.ShouldBe((nuint)123);
            ib.VerifyMetadata().IsOk.ShouldBeTrue();

            using ImageBundle copy = ib.Copy().Value;
            copy.ShrinkTo(4, 2).IsOk.ShouldBeTrue();
            copy.XSize().ShouldBe((nuint)4);
            copy.YSize().ShouldBe((nuint)2);

            using ImageBundle store = new ImageBundle(mm, meta);
            ColorEncoding linSrgb   = ColorEncoding.LinearSRGB(isGray: false);
            ImageBundle.TransformIfNeeded(copy, linSrgb, JxlCms.GetDefaultCms(), null, store, out ImageBundle transformed).IsOk.ShouldBeTrue();
            transformed.IsLinearSRGB().ShouldBeTrue();

            copy.RemoveColor();
            copy.HasColor().ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="AuxOut"/>, <see cref="LoopFilter"/>, <see cref="SizeHeader"/>, and <see cref="PreviewHeader"/>.
        /// </summary>
        [Test]
        public void TestAuxOutLoopFilterAndHeaders()
        {
            AuxOut aux1 = new AuxOut();
            AuxOut aux2 = new AuxOut();

            for (int i = 0; i < AuxOut.KNumImageLayers; i++)
            {
                LayerType lt = (LayerType)i;
                AuxOut.LayerName(lt).ShouldNotBe("Invalid");
                aux2.Layer(lt).TotalBits = 10;
            }

            AuxOut.LayerName((LayerType)99).ShouldBe("Invalid");
            aux2.NumBlocks = 5;
            aux1.Assimilate(aux2);
            aux1.TotalBits().ShouldBe((nuint)(AuxOut.KNumImageLayers * 10));
            aux1.NumBlocks.ShouldBe((nuint)5);

            LoopFilter lf = new LoopFilter
            {
                AllDefault      = false,
                Gab             = true,
                GabCustom       = true,
                GabXWeight1     = 0.1f,
                GabXWeight2     = 0.05f,
                GabYWeight1     = 0.1f,
                GabYWeight2     = 0.05f,
                GabBWeight1     = 0.1f,
                GabBWeight2     = 0.05f,
                EpfIters        = 2,
                EpfSharpCustom  = true,
                EpfWeightCustom = true,
                EpfSigmaCustom  = true,
            };

            lf.Padding().ShouldBe((nuint)4);
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);
            Bundle.Write(lf, bw, LayerType.Header, aux1).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            LoopFilter lfRead = new LoopFilter();
            BitReader br      = new BitReader(bw.GetSpan());
            Bundle.Read(br, lfRead).IsOk.ShouldBeTrue();
            br.Close();
            lfRead.EpfIters.ShouldBe(2u);
            lfRead.GabCustom.ShouldBeTrue();

            ExerciseSizeAndPreviewHeaders();
        }

        /// <summary>
        /// Verifies <see cref="Spline"/>, <see cref="QuantizedSpline"/>, and <see cref="Splines"/> rendering and subtraction.
        /// </summary>
        [Test]
        public void TestSplinesAndQuantizedSplines()
        {
            Spline.Point p1 = new Spline.Point(1.0f, 2.0f);
            Spline.Point p2 = new Spline.Point(1.0001f, 2.0001f);
            Spline.Point p3 = new Spline.Point(5.0f, 6.0f);

            (p1 == p2).ShouldBeTrue();
            (p1 != p3).ShouldBeTrue();
            p1.Equals((object)p2).ShouldBeTrue();
            p1.GetHashCode().ShouldNotBe(0);

            Spline emptySpline = new Spline();
            QuantizedSpline.Create(emptySpline, 0, 0.0f, 0.0f).IsOk.ShouldBeFalse();

            using Splines splines = CreateSampleSplines();
            splines.HasAny().ShouldBeTrue();
            splines.QuantizedSplines.Count.ShouldBe(1);
            splines.StartingPoints.Count.ShouldBe(1);
            splines.GetQuantizationAdjustment().ShouldBe(0);
            splines.InitializeDrawCache(16, 16, new ColorCorrelation()).IsOk.ShouldBeTrue();

            using Image3<float> opsin = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;
            ImageOps.ZeroFillImage(opsin);
            splines.AddTo(opsin, new Rect(opsin));
            splines.SubtractFrom(opsin);
        }

        /// <summary>
        /// Verifies <see cref="Epf.ComputeSigma"/>, <see cref="OutputEncodingInfo"/>, <see cref="DecXyb"/>,
        /// and <see cref="CompressedDc"/> functions in <c>FiltersAndXyb.cs</c>.
        /// </summary>
        [Test]
        public void TestFiltersXybAndCompressedDc()
        {
            LoopFilter lf = new LoopFilter { EpfIters = 0 };
            using ImagePlane<float> sigma = CreateFilledPlane(8, 8, 0.0f);
            using AcStrategyImage acsImg  = AcStrategyImage.Create(JxlMemoryManager.Default, 4, 4).Value;
            using ImagePlane<int> qf      = ImagePlane<int>.Create(JxlMemoryManager.Default, 4, 4).Value;
            using ImagePlane<byte> sharp  = ImagePlane<byte>.Create(JxlMemoryManager.Default, 4, 4).Value;
            ImageOps.FillImage(2, qf);
            ImageOps.FillImage((byte)1, sharp);

            Epf.ComputeSigma(lf, new Rect(0, 0, 4, 4), acsImg, 1.0f, qf, sharp, sigma).IsOk.ShouldBeFalse();

            lf.EpfIters = 2;
            acsImg.FillDCT8();
            acsImg.Set(0, 0, AcStrategyType.DCT16X16);
            Epf.ComputeSigma(lf, new Rect(0, 0, 4, 4), acsImg, 1.0f, qf, sharp, sigma).IsOk.ShouldBeTrue();

            using Image3<float> opsin  = Image3<float>.Create(JxlMemoryManager.Default, 7, 3).Value;
            using Image3<float> linear = Image3<float>.Create(JxlMemoryManager.Default, 7, 3).Value;
            ImageOps.FillImage(0.1f, opsin);
            OpsinParams opsinParams = new OpsinParams();
            opsinParams.Init(255.0f);

            DecXyb.OpsinToLinear(opsin, new Rect(0, 0, 4, 3), null, linear, opsinParams).IsOk.ShouldBeFalse();
            DecXyb.OpsinToLinear(opsin, new Rect(0, 0, 7, 3), null, linear, opsinParams).IsOk.ShouldBeTrue();

            float* rX   = stackalloc float[4] { 0.0f, 0.01f, -0.01f, 0.0f };
            float* rY   = stackalloc float[4] { 0.5f, 0.6f, 0.7f, 0.8f };
            float* rB   = stackalloc float[4] { 0.5f, 0.6f, 0.7f, 0.8f };
            float* rA   = stackalloc float[4] { 0.25f, 0.5f, 0.75f, 1.0f };
            byte* rgba8 = stackalloc byte[16];

            DecXyb.FastXYBTosRGB8(rX, rY, rB, rA, rgba8, isRgba: true, 4).IsOk.ShouldBeTrue();
            DecXyb.FastXYBTosRGB8(rX, rY, rB, null, rgba8, isRgba: false, 4).IsOk.ShouldBeTrue();

            ExerciseCompressedDc();
        }

        /// <summary>
        /// Verifies <see cref="Blending.PerformBlending"/> across all <see cref="PatchBlendMode"/> modes,
        /// clamping flags, and extra channel configurations.
        /// </summary>
        [Test]
        public void TestPerformBlendingAllModes()
        {
            ExtraChannelInfo premulEci   = CreateAlphaExtraChannelInfo(alphaAssociated: true);
            ExtraChannelInfo unpremulEci = CreateAlphaExtraChannelInfo(alphaAssociated: false);

            List<ExtraChannelInfo> premulList   = new List<ExtraChannelInfo> { premulEci };
            List<ExtraChannelInfo> unpremulList = new List<ExtraChannelInfo> { unpremulEci };

            PatchBlendMode[] modes =
            {
                PatchBlendMode.kNone,
                PatchBlendMode.kReplace,
                PatchBlendMode.kAdd,
                PatchBlendMode.kMul,
                PatchBlendMode.kBlendAbove,
                PatchBlendMode.kBlendBelow,
                PatchBlendMode.kAlphaWeightedAddAbove,
                PatchBlendMode.kAlphaWeightedAddBelow,
            };

            foreach (PatchBlendMode mode in modes)
            {
                RunPerformBlendingCase(mode, clamp: false, premulList);
                RunPerformBlendingCase(mode, clamp: true, premulList);
                RunPerformBlendingCase(mode, clamp: true, unpremulList);
            }
        }

        /// <summary>
        /// Verifies <see cref="DequantMatrices"/> decoding, library and raw table computation across all AC strategies.
        /// </summary>
        [Test]
        public void TestDequantMatricesAllStrategies()
        {
            using DequantMatrices dequant = new DequantMatrices();
            dequant.SetDCQuant(new float[] { 64.0f, 128.0f, 32.0f });
            dequant.InvDCQuant(0).ShouldBe(64.0f);
            dequant.DCQuant(0).ShouldBe(1.0f / 64.0f);
            dequant.DCQuants().Length.ShouldBe(3);

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);
            bw.WithMaxBits(
                128,
                LayerType.Header,
                null,
                () =>
                {
                    bw.Write(1, 1); // allDefault = true for DecodeDC
                    bw.Write(1, 1); // allDefault = true for Decode
                    bw.ZeroPadToByte();

                    return true;
                });

            BitReader br = new BitReader(bw.GetSpan());
            dequant.DecodeDC(br).IsOk.ShouldBeTrue();
            dequant.Decode(JxlMemoryManager.Default, br).IsOk.ShouldBeTrue();
            br.Close();

            uint allStrategiesMask = (1u << AcStrategy.kNumValidStrategies) - 1u;
            dequant.EnsureComputed(JxlMemoryManager.Default, allStrategiesMask).IsOk.ShouldBeTrue();

            for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
            {
                AcStrategyType strategy = (AcStrategyType)i;
                dequant.MatrixSpan(strategy, 0, 64).Length.ShouldBe(64);
                dequant.InvMatrixSpan(strategy, 0, 64).Length.ShouldBe(64);
            }

            List<QuantEncoding> customEncodings = new List<QuantEncoding>();

            foreach (QuantEncoding libEnc in DequantMatrices.Library())
            {
                customEncodings.Add(libEnc.Clone());
            }

            int[] rawWeights          = new int[8 * 8 * 3];
            Array.Fill(rawWeights, 4);
            customEncodings[0]        = QuantEncoding.RAW(rawWeights, 2);
            dequant.SetEncodings(customEncodings);
            dequant.EnsureComputed(JxlMemoryManager.Default, 1u << (int)AcStrategyType.DCT).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="Transform"/> and <see cref="ModularTransformOps"/> across all 42 RCT permutations,
        /// Squeeze transforms, and Palette transforms.
        /// </summary>
        [Test]
        public void TestModularTransformsRctSqueezeAndPalette()
        {
            WeightedHeader wpHeader = new WeightedHeader();

            for (uint rctType = 0; rctType < 42; rctType++)
            {
                using ModularImage img = ModularImage.Create(JxlMemoryManager.Default, 6, 4, 8, 3).Value;
                Transform rct          = new Transform(TransformId.RCT) { BeginC = 0, RctType = rctType };
                Transform rctClone     = rct.Clone();
                rctClone.MetaApply(img).IsOk.ShouldBeTrue();
                rctClone.Inverse(img, wpHeader).IsOk.ShouldBeTrue();
            }

            using ModularImage sqImg = ModularImage.Create(JxlMemoryManager.Default, 8, 8, 8, 3).Value;
            Transform sqTransform    = new Transform(TransformId.Squeeze);
            sqTransform.MetaApply(sqImg).IsOk.ShouldBeTrue();
            sqTransform.Inverse(sqImg, wpHeader).IsOk.ShouldBeTrue();

            ExercisePaletteTransform(wpHeader);
        }

        // =====================================================================
        // Private Helper Methods
        // =====================================================================

        /// <summary>
        /// Creates a 4-slot <see cref="ReferenceFrame"/> array with allocated 3-plane images.
        /// </summary>
        private static ReferenceFrame[] CreateReferenceFrames(nuint xsize, nuint ysize, bool inXyb)
        {
            ReferenceFrame[] frames = new ReferenceFrame[4];
            ImageMetadata meta      = new ImageMetadata();
            meta.ColorEncoding      = ColorEncoding.SRGB(isGray: false);

            for (int i = 0; i < 4; i++)
            {
                ImageBundle ib  = new ImageBundle(JxlMemoryManager.Default, meta);
                Image3<float> c = Image3<float>.Create(JxlMemoryManager.Default, xsize, ysize).Value;
                ImageOps.FillImage(0.25f, c);
                ib.SetFromImage(c, meta.ColorEncoding);
                frames[i] = new ReferenceFrame { Frame = ib, IbIsInXyb = inXyb };
            }

            return frames;
        }

        /// <summary>
        /// Creates a 4-slot <see cref="ReferenceFrame"/> array with 3 color planes and 2 alpha extra channels.
        /// </summary>
        private static ReferenceFrame[] CreateReferenceFramesWithAlpha(nuint xsize, nuint ysize, bool alphaAssociated)
        {
            ReferenceFrame[] frames = new ReferenceFrame[4];
            ImageMetadata meta      = new ImageMetadata();
            meta.ColorEncoding      = ColorEncoding.SRGB(isGray: false);
            meta.ExtraChannelInfo.Add(CreateAlphaExtraChannelInfo(alphaAssociated));
            meta.ExtraChannelInfo.Add(CreateAlphaExtraChannelInfo(alphaAssociated));

            for (int i = 0; i < 4; i++)
            {
                ImageBundle ib  = new ImageBundle(JxlMemoryManager.Default, meta);
                Image3<float> c = Image3<float>.Create(JxlMemoryManager.Default, xsize, ysize).Value;
                ImageOps.FillImage(0.3f, c);
                ib.SetFromImage(c, meta.ColorEncoding);

                List<ImagePlane<float>> ecs = new List<ImagePlane<float>>
                {
                    CreateFilledPlane(xsize, ysize, 0.5f),
                    CreateFilledPlane(xsize, ysize, 0.7f),
                };
                ib.SetExtraChannels(ecs);
                frames[i] = new ReferenceFrame { Frame = ib, IbIsInXyb = true };
            }

            return frames;
        }

        /// <summary>
        /// Disposes all <see cref="ReferenceFrame"/> entries in <paramref name="frames"/>.
        /// </summary>
        private static void DisposeReferenceFrames(ReferenceFrame[] frames)
        {
            foreach (ReferenceFrame rf in frames)
            {
                rf.Dispose();
            }
        }

        /// <summary>
        /// Creates an <see cref="ExtraChannelInfo"/> configured as an alpha channel.
        /// </summary>
        private static ExtraChannelInfo CreateAlphaExtraChannelInfo(bool alphaAssociated)
        {
            ExtraChannelInfo eci = new ExtraChannelInfo
            {
                Type            = ExtraChannel.Alpha,
                AlphaAssociated = alphaAssociated,
            };
            eci.BitDepth.BitsPerSample = 8;

            return eci;
        }

        /// <summary>
        /// Verifies that the patch indices returned by <see cref="PatchDictionary.GetPatchesForRow"/> match expected intervals.
        /// </summary>
        private static void VerifyRowPatches(
            List<PatchPosition> posList,
            List<PatchReferencePosition> refs,
            nuint y,
            List<nuint> actual)
        {
            List<nuint> expected = new List<nuint>();

            for (int i = 0; i < posList.Count; i++)
            {
                nuint y0 = posList[i].Y;
                nuint y1 = y0 + refs[(int)posList[i].RefPosIdx].YSize;

                if (y >= y0 && y < y1)
                {
                    expected.Add((nuint)i);
                }
            }

            actual.ShouldBe(expected);
        }

        /// <summary>
        /// Executes <see cref="PatchDictionary.AddOneRow"/> for a specific <paramref name="mode"/> and <paramref name="clamp"/> setting.
        /// </summary>
        private static void RunAddOneRowForMode(
            PatchDictionary dict,
            PatchReferencePosition[] refs,
            List<ExtraChannelInfo> ecList,
            PatchBlendMode mode,
            bool clamp)
        {
            PatchPosition[] pos       = { new PatchPosition { X = 1, Y = 1, RefPosIdx = 0 } };
            PatchBlending[] blendings =
            {
                new PatchBlending { Mode = mode, AlphaChannel = 0, Clamp = clamp },
                new PatchBlending { Mode = mode, AlphaChannel = 0, Clamp = clamp },
            };

            dict.SetPositions(pos, refs, blendings, 2);

            float* r0 = stackalloc float[8] { 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f };
            float* r1 = stackalloc float[8] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f };
            float* r2 = stackalloc float[8] { 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f };
            float* r3 = stackalloc float[8] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };

            float** rows = stackalloc float*[4];
            rows[0] = r0;
            rows[1] = r1;
            rows[2] = r2;
            rows[3] = r3;

            dict.AddOneRow(rows, 1, 0, 8, ecList).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Builds a prefix-coded ANS stream with a single histogram shared across <paramref name="numContexts"/> contexts
        /// and <c>HybridUintConfig(0, 0, 0)</c> so raw integer symbols are decoded directly.
        /// </summary>
        private static byte[] BuildSingleHistogramPrefixStream(nuint numContexts, ReadOnlySpan<uint> symbols)
        {
            _ = numContexts;
            HybridUintConfig cfg = new HybridUintConfig(0, 0, 0);
            BitWriter bw         = new BitWriter(JxlMemoryManager.Default);
            uint[] symArr        = symbols.ToArray();

            bw.WithMaxBits(
                4096 + (nuint)(symArr.Length * 32),
                LayerType.Header,
                null,
                () =>
                {
                    bw.Write(1, 0); // LZ77 disabled
                    bw.Write(1, 1); // Simple context map
                    bw.Write(2, 0); // 1 histogram
                    bw.Write(1, 1); // Use prefix code
                    DecAns.EncodeUintConfig(in cfg, bw, AnsParams.PrefixMaxBits);

                    // Encode a 16-symbol uniform 4-bit Huffman code (alphabetSize = 16):
                    DecAns.StoreVarLenUint16(15, bw);
                    bw.Write(2, 3);      // simpleCodeOrSkip = 3 (starts at codeLenIdx = 4)
                    bw.Write(4, 7);      // codeLengthCodeLengths[4] = 1
                    bw.Write(4, 7);      // codeLengthCodeLengths[0] = 1 (space reaches 0)
                    bw.Write(16, 0xFFFF); // 16 symbols each given code length 4

                    for (int i = 0; i < symArr.Length; i++)
                    {
                        cfg.Encode(symArr[i], out uint tok, out uint nbits, out uint bits);
                        uint rev4 = ((tok & 1u) << 3) | ((tok & 2u) << 1) | ((tok & 4u) >> 1) | ((tok & 8u) >> 3);
                        bw.Write(4, rev4);
                        bw.Write((nuint)nbits, bits);
                    }

                    bw.ZeroPadToByte();

                    return true;
                });

            return bw.GetSpan().ToArray();
        }

        /// <summary>
        /// Verifies that <see cref="PatchDictionary.Decode"/> fails on the given invalid symbol sequence.
        /// </summary>
        private static void CheckPatchDecodeFailure(
            PatchDictionary dict,
            uint[] symbols,
            nuint xsize,
            nuint ysize,
            nuint numEc)
        {
            byte[] bytes = BuildSingleHistogramPrefixStream(10, symbols);
            bool usesEc  = false;
            BitReader br = new BitReader(bytes);
            Status st    = dict.Decode(JxlMemoryManager.Default, br, xsize, ysize, numEc, ref usesEc);
            br.Close();
            st.IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Creates a <see cref="PixelCallback"/> that records stripe calls into a shared counter.
        /// </summary>
        private static PixelCallback CreateNoOpPixelCallback()
        {
            return new PixelCallback(
                (opaque, _, _) => opaque ?? new object(),
                (_, _, _, _, _, _) => { },
                _ => { },
                new object());
        }

        /// <summary>
        /// Creates an <see cref="ImagePlane{Single}"/> filled with <paramref name="value"/>.
        /// </summary>
        private static ImagePlane<float> CreateFilledPlane(nuint xsize, nuint ysize, float value)
        {
            ImagePlane<float> plane = ImagePlane<float>.Create(JxlMemoryManager.Default, xsize, ysize).Value;
            ImageOps.FillImage(value, plane);

            return plane;
        }

        /// <summary>
        /// Creates an <see cref="ImagePlane{Single}"/> populated with a horizontal/vertical gradient.
        /// </summary>
        private static ImagePlane<float> CreateGradientPlane(nuint xsize, nuint ysize, float baseVal)
        {
            ImagePlane<float> plane = ImagePlane<float>.Create(JxlMemoryManager.Default, xsize, ysize).Value;

            for (nuint y = 0; y < ysize; y++)
            {
                float* row = plane.Row(y);

                for (nuint x = 0; x < xsize; x++)
                {
                    row[x] = Math.Clamp(baseVal + ((x + y) * 0.05f), 0.0f, 1.0f);
                }
            }

            return plane;
        }

        /// <summary>
        /// Executes integer format exports on <paramref name="chans"/> for both buffer and callback targets.
        /// </summary>
        private static void RunExternalIntFormats(
            ImagePlane<float>?[] chans,
            nuint[] intBits,
            JxlEndianness end,
            Orientation orientation)
        {
            byte[] buf       = new byte[1024];
            PixelCallback cb = CreateNoOpPixelCallback();

            fixed (byte* ptr = buf)
            {
                foreach (nuint bits in intBits)
                {
                    ExternalConvertOptions bufOpts = new ExternalConvertOptions(
                        bits,
                        false,
                        end,
                        64,
                        null,
                        ptr,
                        (nuint)buf.Length).WithCallbackAndOrientation(default, orientation);

                    DecExternalImage.ConvertChannelsToExternal(chans, 3, in bufOpts).IsOk.ShouldBeTrue();

                    ExternalConvertOptions cbOpts = bufOpts.WithCallbackAndOrientation(cb, orientation);
                    DecExternalImage.ConvertChannelsToExternal(chans, 3, in cbOpts).IsOk.ShouldBeTrue();
                }
            }
        }

        /// <summary>
        /// Executes floating-point format exports on <paramref name="chans"/> for both buffer and callback targets.
        /// </summary>
        private static void RunExternalFloatFormats(
            ImagePlane<float>?[] chans,
            nuint[] floatBits,
            JxlEndianness end,
            Orientation orientation)
        {
            byte[] buf       = new byte[1024];
            PixelCallback cb = CreateNoOpPixelCallback();

            fixed (byte* ptr = buf)
            {
                foreach (nuint bits in floatBits)
                {
                    ExternalConvertOptions bufOpts = new ExternalConvertOptions(
                        bits,
                        true,
                        end,
                        64,
                        null,
                        ptr,
                        (nuint)buf.Length).WithCallbackAndOrientation(default, orientation);

                    DecExternalImage.ConvertChannelsToExternal(chans, 3, in bufOpts).IsOk.ShouldBeTrue();

                    ExternalConvertOptions cbOpts = bufOpts.WithCallbackAndOrientation(cb, orientation);
                    DecExternalImage.ConvertChannelsToExternal(chans, 3, in cbOpts).IsOk.ShouldBeTrue();
                }
            }
        }

        /// <summary>
        /// Runs <see cref="RenderPipelineStage.ProcessRow"/> on synthetic padded row buffers.
        /// </summary>
        private static Status RunStageOnSyntheticBuffers(
            RenderPipelineStage stage,
            int numChannels,
            nuint xsize,
            int borderY,
            int shiftY,
            nuint xextra = 0)
        {
            int inRowCount  = (2 * borderY) + 1;
            int outRowCount = 1 << shiftY;
            int rowLen      = 256;
            int padLeft     = 32;

            float[] backingIn  = new float[numChannels * inRowCount * rowLen];
            float[] backingOut = new float[numChannels * outRowCount * rowLen];
            Array.Fill(backingIn, 0.25f);

            fixed (float* baseIn = backingIn)
            fixed (float* baseOut = backingOut)
            {
                float*[][] inRows  = new float*[numChannels][];
                float*[][] outRows = new float*[numChannels][];

                for (int c = 0; c < numChannels; c++)
                {
                    inRows[c]  = new float*[inRowCount];
                    outRows[c] = new float*[outRowCount];

                    for (int r = 0; r < inRowCount; r++)
                    {
                        inRows[c][r] = baseIn + (((c * inRowCount) + r) * rowLen) + padLeft;
                    }

                    for (int r = 0; r < outRowCount; r++)
                    {
                        outRows[c][r] = baseOut + (((c * outRowCount) + r) * rowLen) + padLeft;
                    }
                }

                return stage.ProcessRow(inRows, outRows, (xextra, xextra), xsize, 0, 0, 0);
            }
        }

        /// <summary>
        /// Creates a <see cref="Splines"/> collection containing a single quantized Catmull-Rom spline.
        /// </summary>
        private static Splines CreateSampleSplines()
        {
            Spline spline = new Spline();
            spline.ControlPoints.Add(new Spline.Point(1.0f, 1.0f));
            spline.ControlPoints.Add(new Spline.Point(4.0f, 3.0f));
            spline.ControlPoints.Add(new Spline.Point(7.0f, 6.0f));
            spline.ColorDct[0][0] = 0.2f;
            spline.ColorDct[1][0] = 0.5f;
            spline.ColorDct[2][0] = 0.3f;
            spline.SigmaDct[0]    = 1.5f;

            QuantizedSpline qSpline = QuantizedSpline.Create(spline, 0, 0.0f, 0.0f).Value;

            return new Splines(
                0,
                new List<QuantizedSpline> { qSpline },
                new List<Spline.Point> { new Spline.Point(1.0f, 1.0f) });
        }

        /// <summary>
        /// Creates a <see cref="CodecMetadata"/> with the specified dimensions and XYB setting.
        /// </summary>
        private static CodecMetadata CreateDefaultCodecMetadata(nuint xsize, nuint ysize, bool xybEncoded)
        {
            CodecMetadata meta   = new CodecMetadata();
            meta.M.XybEncoded    = xybEncoded;
            meta.M.ColorEncoding = ColorEncoding.SRGB(isGray: false);
            meta.Size.Set(xsize, ysize);

            return meta;
        }

        /// <summary>
        /// Creates an <see cref="OutputEncodingInfo"/> configured with the given <paramref name="tf"/>.
        /// </summary>
        private static OutputEncodingInfo CreateOutputEncodingWithTf(TransferFunction tf)
        {
            CodecMetadata meta      = CreateDefaultCodecMetadata(16, 16, xybEncoded: true);
            OutputEncodingInfo info = new OutputEncodingInfo();
            info.SetFromMetadata(meta);

            ColorEncoding enc = ColorEncoding.SRGB(isGray: false);
            enc.Tf.SetTransferFunction(tf);
            info.MaybeSetColorEncoding(enc);

            return info;
        }

        /// <summary>
        /// Exercises PQ, HLG, and range-scaling tone mapping stages.
        /// </summary>
        private static void ExerciseToneMappingStages()
        {
            OutputEncodingInfo eqInfo     = CreateOutputEncodingWithTf(TransferFunction.PQ);
            eqInfo.OrigIntensityTarget    = 1000.0f;
            eqInfo.DesiredIntensityTarget = 1000.0f;
            RenderPipelineStages.GetToneMappingStage(eqInfo).ShouldBeNull();

            OutputEncodingInfo pqInfo = CreateOutputEncodingWithTf(TransferFunction.PQ);
            pqInfo.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.PQ);
            pqInfo.OrigIntensityTarget    = 4000.0f;
            pqInfo.DesiredIntensityTarget = 250.0f;
            using RenderPipelineStage pqStage = RenderPipelineStages.GetToneMappingStage(pqInfo)!;
            RunStageOnSyntheticBuffers(pqStage, 3, 7, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(pqStage, 3, 7, 0, 0, xextra: 1).IsOk.ShouldBeFalse();

            OutputEncodingInfo hlgInfo = CreateOutputEncodingWithTf(TransferFunction.SRGB);
            hlgInfo.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.HLG);
            hlgInfo.OrigIntensityTarget    = 1000.0f;
            hlgInfo.DesiredIntensityTarget = 250.0f;
            using RenderPipelineStage hlgStage = RenderPipelineStages.GetToneMappingStage(hlgInfo)!;
            RunStageOnSyntheticBuffers(hlgStage, 3, 7, 0, 0).IsOk.ShouldBeTrue();

            OutputEncodingInfo hlgToPqInfo = CreateOutputEncodingWithTf(TransferFunction.PQ);
            hlgToPqInfo.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.HLG);
            hlgToPqInfo.OrigIntensityTarget    = 1000.0f;
            hlgToPqInfo.DesiredIntensityTarget = 250.0f;
            using RenderPipelineStage hlgToPqStage = RenderPipelineStages.GetToneMappingStage(hlgToPqInfo)!;
            RunStageOnSyntheticBuffers(hlgToPqStage, 3, 7, 0, 0).IsOk.ShouldBeTrue();

            OutputEncodingInfo rangeInfo = CreateOutputEncodingWithTf(TransferFunction.PQ);
            rangeInfo.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.Linear);
            rangeInfo.OrigIntensityTarget    = 1000.0f;
            rangeInfo.DesiredIntensityTarget = 250.0f;
            RenderPipelineStages.GetToneMappingStage(rangeInfo).ShouldBeNull();
        }

        /// <summary>
        /// Exercises <see cref="RenderPipelineStages.GetCmsStage"/> for RGB and Gray transforms.
        /// </summary>
        private static void ExerciseCmsStages()
        {
            CodecMetadata meta      = CreateDefaultCodecMetadata(16, 16, xybEncoded: false);
            OutputEncodingInfo info = new OutputEncodingInfo();
            info.SetFromMetadata(meta);
            RenderPipelineStages.GetCmsStage(info, linear: false).ShouldBeNull();

            info.OrigColorEncoding.SetColorSpace(ColorSpace.RGB);
            info.OrigColorEncoding.SetPrimariesType(Primaries.P3);
            info.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.SRGB);
            info.OrigColorEncoding.CreateICC().IsOk.ShouldBeTrue();

            info.ColorEncoding = ColorEncoding.SRGB(isGray: false);
            info.ColorEncoding.CreateICC().IsOk.ShouldBeTrue();
            info.LinearColorEncoding = ColorEncoding.LinearSRGB(isGray: false);
            info.LinearColorEncoding.CreateICC().IsOk.ShouldBeTrue();
            info.ColorEncodingIsOriginal = false;
            info.CmsSet                  = true;
            info.ColorManagementSystem   = JxlCms.GetDefaultCms();

            using RenderPipelineStage cmsStage = RenderPipelineStages.GetCmsStage(info, linear: false)!;
            cmsStage.ShouldNotBeNull();
            cmsStage.SetInputSizes(new (nuint, nuint)[] { (8, 4), (4, 4), (8, 4) }).IsOk.ShouldBeFalse();
            cmsStage.SetInputSizes(new (nuint, nuint)[] { (8, 4), (8, 4), (8, 4) }).IsOk.ShouldBeTrue();
            cmsStage.PrepareForThreads(1).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(cmsStage, 3, 8, 0, 0).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(cmsStage, 3, 8, 0, 0, xextra: 1).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="RenderPipelineStages.GetBlendingStage"/> across blend modes and padding rows.
        /// </summary>
        private static void ExerciseBlendingStage()
        {
            CodecMetadata meta = CreateDefaultCodecMetadata(8, 8, xybEncoded: false);
            meta.M.ExtraChannelInfo.Add(CreateAlphaExtraChannelInfo(alphaAssociated: false));
            meta.M.NumExtraChannels = 1;

            using PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);
            FrameHeader fh = new FrameHeader(meta)
            {
                CustomSizeOrOrigin = true,
                FrameOrigin        = new FrameOrigin { X0 = -1, Y0 = -1 },
                FrameSize          = new FrameSize { XSize = 6, YSize = 6 },
            };
            fh.BlendingInfo.Mode   = BlendMode.Blend;
            fh.BlendingInfo.Source = 0;
            fh.ExtraChannelBlendingInfo.Add(new BlendingInfo { Mode = BlendMode.Add, Source = 0 });

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

            ImageBundle bg  = decState.Shared.ReferenceFrames[0].Frame!;
            Image3<float> c = Image3<float>.Create(JxlMemoryManager.Default, 8, 8).Value;
            ImageOps.FillImage(0.3f, c);
            bg.SetFromImage(c, meta.M.ColorEncoding).IsOk.ShouldBeTrue();
            bg.SetExtraChannels(new List<ImagePlane<float>> { CreateFilledPlane(8, 8, 0.8f) }).IsOk.ShouldBeTrue();

            using RenderPipelineStage blendStage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);
            blendStage.IsInitialized().IsOk.ShouldBeTrue();
            blendStage.SwitchToImageDimensions().ShouldBeTrue();
            blendStage.GetImageDimensions(out nuint imgX, out nuint imgY, out FrameOrigin origin);
            imgX.ShouldBe((nuint)8);
            imgY.ShouldBe((nuint)8);
            origin.X0.ShouldBe(-1);

            RunStageOnSyntheticBuffers(blendStage, 4, 6, 0, 0).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Exercises <see cref="RenderPipelineStages.GetWriteToOutputStage"/> across pixel data types, orientations, and extra outputs.
        /// </summary>
        private static void ExerciseWriteToOutputStages()
        {
            JxlDataType[] types =
            {
                JxlDataType.UInt8,
                JxlDataType.UInt16,
                JxlDataType.Float16,
                JxlDataType.Float,
            };

            byte[] mainBuf  = new byte[16 * 16 * 4 * 4];
            byte[] extraBuf = new byte[16 * 16 * 4];

            fixed (byte* mainPtr = mainBuf)
            fixed (byte* extraPtr = extraBuf)
            {
                foreach (JxlDataType dt in types)
                {
                    RunWriteToOutputForType(dt, mainPtr, (nuint)mainBuf.Length, extraPtr, (nuint)extraBuf.Length);
                }
            }
        }

        /// <summary>
        /// Runs <see cref="RenderPipelineStages.GetWriteToOutputStage"/> for a specific <see cref="JxlDataType"/>.
        /// </summary>
        private static void RunWriteToOutputForType(
            JxlDataType dataType,
            byte* mainPtr,
            nuint mainLen,
            byte* extraPtr,
            nuint extraLen)
        {
            nuint bits = dataType == JxlDataType.UInt8 ? 8u : (dataType == JxlDataType.UInt16 || dataType == JxlDataType.Float16 ? 16u : 32u);

            ImageOutput mainOut = new ImageOutput
            {
                Format        = new JxlPixelFormat { NumChannels = 4, DataType = dataType, Endianness = JxlEndianness.BigEndian },
                BitsPerSample = bits,
                Buffer        = mainPtr,
                BufferSize    = mainLen,
                Stride        = 64,
            };

            ImageOutput extraOut = new ImageOutput
            {
                Format        = new JxlPixelFormat { NumChannels = 1, DataType = dataType, Endianness = JxlEndianness.LittleEndian },
                BitsPerSample = bits,
                Buffer        = extraPtr,
                BufferSize    = extraLen,
                Stride        = 16,
            };

            using RenderPipelineStage stage = RenderPipelineStages.GetWriteToOutputStage(
                mainOut,
                (4, 3),
                (HasAlpha: true, UnpremulAlpha: true, AlphaC: 3),
                Orientation.Rotate90,
                new List<ImageOutput> { extraOut },
                JxlMemoryManager.Default);

            stage.IsInitialized().IsOk.ShouldBeTrue();
            stage.SetInputSizes(new (nuint, nuint)[] { (4, 3), (4, 3), (4, 3), (4, 3) }).IsOk.ShouldBeTrue();
            stage.PrepareForThreads(1).IsOk.ShouldBeTrue();
            RunStageOnSyntheticBuffers(stage, 4, 4, 0, 0).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Runs a multi-group <see cref="RenderPipeline"/> containing a Gaborish border stage, a cropped
        /// <c>BlendingStage</c> that switches to image dimensions, and a terminal <c>WriteToImageBundleStage</c>.
        /// </summary>
        private static void RunMultiGroupPipelineWithBlending(bool useSimple)
        {
            CodecMetadata meta                = CreateDefaultCodecMetadata(300, 300, xybEncoded: false);
            using PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            FrameHeader fh = new FrameHeader(meta)
            {
                CustomSizeOrOrigin = true,
                FrameOrigin        = new FrameOrigin { X0 = 16, Y0 = 16 },
                FrameSize          = new FrameSize { XSize = 200, YSize = 200 },
                GroupSizeShift     = 0,
            };
            fh.LoopFilter.Gab      = true;
            fh.BlendingInfo.Mode   = BlendMode.Add;
            fh.BlendingInfo.Source = 0;

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

            ImageBundle bg  = decState.Shared.ReferenceFrames[0].Frame!;
            Image3<float> c = Image3<float>.Create(JxlMemoryManager.Default, 300, 300).Value;
            ImageOps.FillImage(0.1f, c);
            bg.SetFromImage(c, meta.M.ColorEncoding).IsOk.ShouldBeTrue();

            using ImageBundle dstBundle    = new ImageBundle(JxlMemoryManager.Default, meta.M);
            RenderPipeline.Builder builder = new RenderPipeline.Builder(JxlMemoryManager.Default, 3);

            if (useSimple)
            {
                builder.UseSimpleImplementation();
            }

            builder.AddStage(RenderPipelineStages.GetGaborishStage(fh.LoopFilter)).IsOk.ShouldBeTrue();
            builder.AddStage(RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding)).IsOk.ShouldBeTrue();
            builder.AddStage(RenderPipelineStages.GetWriteToImageBundleStage(dstBundle, meta.M.ColorEncoding)).IsOk.ShouldBeTrue();

            using RenderPipeline pipe = builder.Finalize(decState.Shared.FrameDim).Value;
            pipe.IsInitialized().IsOk.ShouldBeTrue();
            pipe.PrepareForThreads(1, useGroupIds: false).IsOk.ShouldBeTrue();
            pipe.PassesWithAllInput().ShouldBe((nuint)0);

            for (nuint g = 0; g < decState.Shared.FrameDim.NumGroups; g++)
            {
                RenderPipelineInput input       = pipe.GetInputBuffers(g, 0);
                (ImagePlane<float> p0, Rect r0) = input.GetBuffer(0);
                ImageOps.FillImage(0.2f, p0);
                _ = r0;
                input.Done().IsOk.ShouldBeTrue();
            }

            pipe.PassesWithAllInput().ShouldBe((nuint)1);
            pipe.ClearDone(0);
            dstBundle.XSize().ShouldBe((nuint)300);
            dstBundle.YSize().ShouldBe((nuint)300);
        }

        /// <summary>
        /// Creates a comprehensive <see cref="JPEGData"/> instance for sequential or progressive JPEG testing.
        /// </summary>
        private static JPEGData CreateSampleJpegData(bool progressive)
        {
            JPEGData jpg        = new JPEGData();
            jpg.Width           = 16;
            jpg.Height          = 16;
            jpg.RestartInterval = 1;

            jpg.AppData.Add(new byte[] { 0xE0, 0x00, 0x04, 0x01, 0x02 });
            jpg.AppMarkerTypes.Add(AppMarkerType.Unknown);

            byte[] iccMarker = new byte[17 + 4];
            jpg.AppData.Add(iccMarker);
            jpg.AppMarkerTypes.Add(AppMarkerType.ICC);

            byte[] exifMarker = new byte[3 + JpegConstants.KExifTag.Length + 4];
            jpg.AppData.Add(exifMarker);
            jpg.AppMarkerTypes.Add(AppMarkerType.Exif);

            byte[] xmpMarker = new byte[3 + JpegConstants.KXMPTag.Length + 4];
            jpg.AppData.Add(xmpMarker);
            jpg.AppMarkerTypes.Add(AppMarkerType.XMP);

            jpg.ComData.Add(new byte[] { 0xFE, 0x00, 0x04, 0x41, 0x42 });
            jpg.InterMarkerData.Add(new byte[] { 0x11, 0x22 });
            jpg.TailData = new byte[] { 0x99, 0xAA };

            JPEGQuantTable q0 = new JPEGQuantTable { Index = 0, Precision = 0, IsLast = false };
            JPEGQuantTable q1 = new JPEGQuantTable { Index = 1, Precision = 1, IsLast = true };
            Array.Fill(q0.Values, 4);
            Array.Fill(q1.Values, 300);
            jpg.Quant.Add(q0);
            jpg.Quant.Add(q1);

            PopulateSampleComponentsAndHuffman(jpg);
            PopulateSampleScansAndMarkers(jpg, progressive);

            return jpg;
        }

        /// <summary>
        /// Populates 3 YCbCr components and complete DC/AC Huffman tables on <paramref name="jpg"/>.
        /// </summary>
        private static void PopulateSampleComponentsAndHuffman(JPEGData jpg)
        {
            for (uint c = 0; c < 3; c++)
            {
                JPEGComponent comp = new JPEGComponent
                {
                    Id             = c + 1,
                    HSampFactor    = 1,
                    VSampFactor    = 1,
                    QuantIdx       = c == 0 ? 0u : 1u,
                    WidthInBlocks  = 2,
                    HeightInBlocks = 2,
                    Coeffs         = new short[4 * 64],
                };
                comp.Coeffs[0]  = 8;
                comp.Coeffs[1]  = 4;
                comp.Coeffs[20] = -2;
                comp.Coeffs[64] = -4;
                jpg.Components.Add(comp);
            }

            JPEGHuffmanCode dcHuff = new JPEGHuffmanCode { SlotId = 0, IsLast = false };
            dcHuff.Counts[4] = 13;

            for (uint i = 0; i < 12; i++)
            {
                dcHuff.Values[i] = i;
            }

            dcHuff.Values[12] = JpegConstants.KJpegHuffmanAlphabetSize;

            JPEGHuffmanCode acHuff = new JPEGHuffmanCode { SlotId = 0x10, IsLast = true };
            acHuff.Counts[8] = 128;
            acHuff.Counts[9] = 129;

            for (uint i = 0; i < 256; i++)
            {
                acHuff.Values[i] = i;
            }

            acHuff.Values[256] = JpegConstants.KJpegHuffmanAlphabetSize;

            jpg.HuffmanCode.Add(dcHuff);
            jpg.HuffmanCode.Add(acHuff);
        }

        /// <summary>
        /// Populates sequential or progressive scan descriptors and marker order on <paramref name="jpg"/>.
        /// </summary>
        private static void PopulateSampleScansAndMarkers(JPEGData jpg, bool progressive)
        {
            jpg.MarkerOrder.Add(0xE0);
            jpg.MarkerOrder.Add(0xE2);
            jpg.MarkerOrder.Add(0xE1);
            jpg.MarkerOrder.Add(0xE1);
            jpg.MarkerOrder.Add(0xFE);
            jpg.MarkerOrder.Add(0xFF);
            jpg.MarkerOrder.Add(0xDB);
            jpg.MarkerOrder.Add(progressive ? (byte)0xC2 : (byte)0xC0);
            jpg.MarkerOrder.Add(0xDD);
            jpg.MarkerOrder.Add(0xC4);

            if (!progressive)
            {
                JPEGScanInfo scan  = new JPEGScanInfo { Ss = 0, Se = 63, Ah = 0, Al = 0, NumComponents = 3 };
                scan.Components[0] = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
                scan.Components[1] = new JPEGComponentScanInfo { CompIdx = 1, DcTblIdx = 0, AcTblIdx = 0 };
                scan.Components[2] = new JPEGComponentScanInfo { CompIdx = 2, DcTblIdx = 0, AcTblIdx = 0 };
                scan.ResetPoints.Add(0);
                scan.ExtraZeroRuns.Add(new JPEGScanInfo.ExtraZeroRunInfo { BlockIdx = 0, NumExtraZeroRuns = 1 });
                jpg.ScanInfo.Add(scan);
                jpg.MarkerOrder.Add(0xDA);
            }
            else
            {
                AddProgressiveScans(jpg);
            }

            jpg.MarkerOrder.Add(0xD9);
        }

        /// <summary>
        /// Adds progressive DC, AC first, DC refinement, and AC refinement scans to <paramref name="jpg"/>.
        /// </summary>
        private static void AddProgressiveScans(JPEGData jpg)
        {
            JPEGScanInfo dcFirst  = new JPEGScanInfo { Ss = 0, Se = 0, Ah = 0, Al = 1, NumComponents = 3 };
            dcFirst.Components[0] = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
            dcFirst.Components[1] = new JPEGComponentScanInfo { CompIdx = 1, DcTblIdx = 0, AcTblIdx = 0 };
            dcFirst.Components[2] = new JPEGComponentScanInfo { CompIdx = 2, DcTblIdx = 0, AcTblIdx = 0 };

            JPEGScanInfo acFirst  = new JPEGScanInfo { Ss = 1, Se = 63, Ah = 0, Al = 1, NumComponents = 1 };
            acFirst.Components[0] = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
            acFirst.ResetPoints.Add(1);
            acFirst.ExtraZeroRuns.Add(new JPEGScanInfo.ExtraZeroRunInfo { BlockIdx = 0, NumExtraZeroRuns = 1 });

            JPEGScanInfo dcRef    = new JPEGScanInfo { Ss = 0, Se = 0, Ah = 1, Al = 0, NumComponents = 3 };
            dcRef.Components[0]   = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
            dcRef.Components[1]   = new JPEGComponentScanInfo { CompIdx = 1, DcTblIdx = 0, AcTblIdx = 0 };
            dcRef.Components[2]   = new JPEGComponentScanInfo { CompIdx = 2, DcTblIdx = 0, AcTblIdx = 0 };

            JPEGScanInfo acRef    = new JPEGScanInfo { Ss = 1, Se = 63, Ah = 1, Al = 0, NumComponents = 1 };
            acRef.Components[0]   = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };

            jpg.ScanInfo.Add(dcFirst);
            jpg.ScanInfo.Add(acFirst);
            jpg.ScanInfo.Add(dcRef);
            jpg.ScanInfo.Add(acRef);
            jpg.MarkerOrder.Add(0xDA);
            jpg.MarkerOrder.Add(0xDA);
            jpg.MarkerOrder.Add(0xDA);
            jpg.MarkerOrder.Add(0xDA);
        }

        /// <summary>
        /// Serializes <paramref name="jpg"/> headers and Brotli-compressed marker payloads into a <c>jbrd</c> buffer.
        /// </summary>
        private static byte[] EncodeJpegDataToJbrdBytes(JPEGData jpg)
        {
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);
            Bundle.Write(jpg, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            List<byte> uncompressed = new List<byte>();

            for (int i = 0; i < jpg.AppData.Count; i++)
            {
                if (jpg.AppMarkerTypes[i] == AppMarkerType.Unknown)
                {
                    uncompressed.AddRange(jpg.AppData[i]);
                }
            }

            foreach (byte[] com in jpg.ComData)
            {
                uncompressed.AddRange(com);
            }

            foreach (byte[] inter in jpg.InterMarkerData)
            {
                uncompressed.AddRange(inter);
            }

            uncompressed.AddRange(jpg.TailData);

            byte[] rawBytes  = uncompressed.ToArray();
            byte[] compBytes = new byte[BrotliEncoder.GetMaxCompressedLength(rawBytes.Length) + 16];
            BrotliEncoder.TryCompress(rawBytes, compBytes, out int written, 4, 10).ShouldBeTrue();

            ReadOnlySpan<byte> hdrSpan = bw.GetSpan();
            byte[] result              = new byte[hdrSpan.Length + written];
            hdrSpan.CopyTo(result);
            compBytes.AsSpan(0, written).CopyTo(result.AsSpan(hdrSpan.Length));

            return result;
        }

        /// <summary>
        /// Exercises <see cref="IccCodecCommon.ICCPredictHeader"/>, <see cref="IccCodecCommon.LinearPredictICCValue"/>,
        /// and <see cref="IccCodecCommon.ICCANSContext"/>.
        /// </summary>
        private static void ExerciseIccPredictionPrimitives()
        {
            byte[] hdr = IccCodecCommon.ICCInitialHeaderPrediction(256);
            byte[] icc = new byte[128];
            icc[4]  = 1;
            icc[5]  = 2;
            icc[6]  = 3;
            icc[7]  = 4;
            icc[40] = (byte)'A';
            IccCodecCommon.ICCPredictHeader(icc, 128, hdr, 8);
            IccCodecCommon.ICCPredictHeader(icc, 128, hdr, 41);
            hdr[41].ShouldBe((byte)'P');

            icc[40] = (byte)'M';
            IccCodecCommon.ICCPredictHeader(icc, 128, hdr, 41);
            hdr[41].ShouldBe((byte)'S');

            icc[40] = (byte)'S';
            icc[41] = (byte)'G';
            IccCodecCommon.ICCPredictHeader(icc, 128, hdr, 42);
            hdr[42].ShouldBe((byte)'I');

            icc[41] = (byte)'U';
            IccCodecCommon.ICCPredictHeader(icc, 128, hdr, 42);
            hdr[42].ShouldBe((byte)'N');

            byte[] predData = new byte[64];

            for (int i = 0; i < predData.Length; i++)
            {
                predData[i] = (byte)(i * 3);
            }

            nuint[] widths = { 1, 2, 4 };

            foreach (nuint w in widths)
            {
                for (int order = 0; order <= 3; order++)
                {
                    _ = IccCodecCommon.LinearPredictICCValue(predData, 24, 5, w, w, order);
                }
            }

            IccCodecCommon.ICCANSContext(10, 0, 0).ShouldBe((nuint)0);
            byte[] testBytes = { (byte)'a', (byte)'5', 0, 5, 100, 245, 255 };

            foreach (byte b1 in testBytes)
            {
                foreach (byte b2 in testBytes)
                {
                    IccCodecCommon.ICCANSContext(200, b1, b2).ShouldBeLessThan((nuint)IccCodecCommon.KNumIccContexts);
                }
            }
        }

        /// <summary>
        /// Verifies that <paramref name="iccBytes"/> round-trips through <see cref="IccCodec"/> and <see cref="ICCReader"/>.
        /// </summary>
        private static void VerifyIccRoundTrip(ReadOnlySpan<byte> iccBytes)
        {
            using PaddedBytes predicted   = new PaddedBytes(JxlMemoryManager.Default);
            using PaddedBytes unpredicted = new PaddedBytes(JxlMemoryManager.Default);

            IccCodec.PredictICC(iccBytes, predicted).IsOk.ShouldBeTrue();
            IccCodec.UnpredictICC(predicted.Span, unpredicted).IsOk.ShouldBeTrue();
            unpredicted.Span.SequenceEqual(iccBytes).ShouldBeTrue();

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);
            AuxOut aux   = new AuxOut();
            IccCodec.WriteICC(iccBytes, bw, LayerType.Header, aux).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            ICCReader reader             = new ICCReader(JxlMemoryManager.Default);
            using PaddedBytes decodedIcc = new PaddedBytes(JxlMemoryManager.Default);

            BitReader br1 = new BitReader(bw.GetSpan());
            reader.Init(br1).IsOk.ShouldBeTrue();
            br1.Close();

            BitReader br2 = new BitReader(bw.GetSpan());
            reader.Init(br2).IsOk.ShouldBeTrue();
            reader.Process(br2, decodedIcc).IsOk.ShouldBeTrue();
            br2.Close();

            decodedIcc.Span.SequenceEqual(iccBytes).ShouldBeTrue();
            reader.Reset();
        }

        /// <summary>
        /// Builds a synthetic ICC profile exercising TRC/XYZ triplets, <c>mluc</c>, <c>curv</c>, <c>sf32</c>,
        /// <c>gbd </c>, and <c>mAB </c> tags.
        /// </summary>
        private static byte[] BuildComprehensiveSyntheticIccProfile()
        {
            using PaddedBytes icc = new PaddedBytes(JxlMemoryManager.Default);
            byte[] hdr            = IccCodecCommon.ICCInitialHeaderPrediction(512);
            icc.Append(hdr);

            const uint numTags = 9;
            IccCodecCommon.AppendUint32(numTags, icc);

            uint tableEnd   = 128 + 4 + (numTags * 12);
            uint trcOffset  = tableEnd;
            uint trcSize    = 32;
            uint rXyzOffset = trcOffset + trcSize;
            uint gXyzOffset = rXyzOffset + 20;
            uint bXyzOffset = gXyzOffset + 20;
            uint mlucOffset = bXyzOffset + 20;
            uint mlucSize   = 28;
            uint sf32Offset = mlucOffset + mlucSize;
            uint sf32Size   = 28;
            uint mabOffset  = sf32Offset + sf32Size;
            uint mabSize    = 64;

            AppendIccTagEntry(icc, IccCodecCommon.KRtrcTag, trcOffset, trcSize);
            AppendIccTagEntry(icc, IccCodecCommon.KGtrcTag, trcOffset, trcSize);
            AppendIccTagEntry(icc, IccCodecCommon.KBtrcTag, trcOffset, trcSize);
            AppendIccTagEntry(icc, IccCodecCommon.KRxyzTag, rXyzOffset, 20);
            AppendIccTagEntry(icc, IccCodecCommon.KGxyzTag, gXyzOffset, 20);
            AppendIccTagEntry(icc, IccCodecCommon.KBxyzTag, bXyzOffset, 20);
            AppendIccTagEntry(icc, IccCodecCommon.KDescTag, mlucOffset, mlucSize);
            AppendIccTagEntry(icc, IccCodecCommon.KChadTag, sf32Offset, sf32Size);
            AppendIccTagEntry(icc, new IccTag('A', '2', 'B', '0'), mabOffset, mabSize);

            AppendSyntheticTagPayloads(icc, trcSize, mlucSize, sf32Size, mabSize);
            ByteOrder.StoreBE32((uint)icc.Size, icc.Span.Slice(0, 4));

            return icc.Span.ToArray();
        }

        /// <summary>
        /// Appends a 12-byte ICC tag directory entry (<paramref name="tag"/>, <paramref name="offset"/>, <paramref name="size"/>).
        /// </summary>
        private static void AppendIccTagEntry(PaddedBytes icc, IccTag tag, uint offset, uint size)
        {
            IccCodecCommon.AppendKeyword(tag, icc);
            IccCodecCommon.AppendUint32(offset, icc);
            IccCodecCommon.AppendUint32(size, icc);
        }

        /// <summary>
        /// Appends tag payloads for <c>curv</c>, <c>XYZ </c>, <c>mluc</c>, <c>sf32</c>, and <c>mAB </c>.
        /// </summary>
        private static void AppendSyntheticTagPayloads(
            PaddedBytes icc,
            uint trcSize,
            uint mlucSize,
            uint sf32Size,
            uint mabSize)
        {
            IccCodecCommon.AppendKeyword(IccCodecCommon.KCurvTag, icc);
            IccCodecCommon.AppendUint32(0, icc);
            IccCodecCommon.AppendUint32(10, icc);
            icc.Resize(icc.Size + (trcSize - 12));

            for (int i = 0; i < 3; i++)
            {
                IccCodecCommon.AppendKeyword(IccCodecCommon.KXyzTag, icc);
                IccCodecCommon.AppendUint32(0, icc);
                IccCodecCommon.AppendUint32(0x0000F6D6u, icc);
                IccCodecCommon.AppendUint32(0x00010000u, icc);
                IccCodecCommon.AppendUint32(0x0000D32Du, icc);
            }

            IccCodecCommon.AppendKeyword(IccCodecCommon.KMlucTag, icc);
            IccCodecCommon.AppendUint32(0, icc);
            icc.Resize(icc.Size + (mlucSize - 8));

            IccCodecCommon.AppendKeyword(IccCodecCommon.KSf32Tag, icc);
            IccCodecCommon.AppendUint32(0, icc);
            icc.Resize(icc.Size + (sf32Size - 8));

            nuint mabStart = icc.Size;
            IccCodecCommon.AppendKeyword(IccCodecCommon.KMabTag, icc);
            IccCodecCommon.AppendUint32(0, icc);
            icc.Resize(mabStart + mabSize);
            Span<byte> mabSpan = icc.Span.Slice((int)mabStart, (int)mabSize);
            mabSpan[8]  = 3;
            mabSpan[9]  = 3;
            ByteOrder.StoreBE32(32, mabSpan.Slice(12, 4));
            IccCodecCommon.EncodeKeyword(IccCodecCommon.KCurvTag, mabSpan, mabSize, 32);
            ByteOrder.StoreBE32(1, mabSpan.Slice(40, 4));
        }

        /// <summary>
        /// Exercises <see cref="SizeHeader"/> and <see cref="PreviewHeader"/> round-trips and validation errors.
        /// </summary>
        private static void ExerciseSizeAndPreviewHeaders()
        {
            SizeHeader sz = new SizeHeader();
            sz.BundleName.ShouldBe("SizeHeader");
            sz.Set(0, 10).IsOk.ShouldBeFalse();
            sz.Set(unchecked((nuint)uint.MaxValue + 10u), 10).IsOk.ShouldBeFalse();

            (nuint X, nuint Y)[] testSizes =
            {
                (64, 64),
                (80, 60),
                (17, 23),
                (300, 200),
            };

            foreach ((nuint x, nuint y) in testSizes)
            {
                sz.Set(x, y).IsOk.ShouldBeTrue();
                BitWriter bw = new BitWriter(JxlMemoryManager.Default);
                Bundle.Write(sz, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
                bw.ZeroPadToByte();

                SizeHeader szOut = new SizeHeader();
                BitReader br     = new BitReader(bw.GetSpan());
                Bundle.Read(br, szOut).IsOk.ShouldBeTrue();
                br.Close();
                szOut.XSize().ShouldBe(x);
                szOut.YSize().ShouldBe(y);

                PreviewHeader prev = new PreviewHeader();
                prev.BundleName.ShouldBe("PreviewHeader");
                prev.Set(0, 8).IsOk.ShouldBeFalse();
                prev.Set(x, y).IsOk.ShouldBeTrue();

                BitWriter pbw = new BitWriter(JxlMemoryManager.Default);
                Bundle.Write(prev, pbw, LayerType.Header, null).IsOk.ShouldBeTrue();
                pbw.ZeroPadToByte();

                PreviewHeader prevOut = new PreviewHeader();
                BitReader pbr         = new BitReader(pbw.GetSpan());
                Bundle.Read(pbr, prevOut).IsOk.ShouldBeTrue();
                pbr.Close();
                prevOut.XSize().ShouldBe(x);
                prevOut.YSize().ShouldBe(y);
            }
        }

        /// <summary>
        /// Exercises <see cref="CompressedDc.AdaptiveDCSmoothing"/> and <see cref="CompressedDc.DequantDC"/>.
        /// </summary>
        private static void ExerciseCompressedDc()
        {
            float[] dcFactors = { 0.1f, 0.1f, 0.1f };
            float[] cfl       = { 0.05f, 0.0f, 0.05f };

            using Image3<float> smallDc = Image3<float>.Create(JxlMemoryManager.Default, 2, 2).Value;
            CompressedDc.AdaptiveDCSmoothing(JxlMemoryManager.Default, dcFactors, smallDc, null).IsOk.ShouldBeTrue();

            using Image3<float> dc = Image3<float>.Create(JxlMemoryManager.Default, 5, 5).Value;
            ImageOps.FillImage(0.5f, dc);
            CompressedDc.AdaptiveDCSmoothing(JxlMemoryManager.Default, dcFactors, dc, null).IsOk.ShouldBeTrue();

            using ImagePlane<byte> quantDc = ImagePlane<byte>.Create(JxlMemoryManager.Default, 4, 4).Value;
            using ModularImage modImg      = ModularImage.Create(JxlMemoryManager.Default, 4, 4, 8, 3).Value;
            BlockCtxMap bctx               = new BlockCtxMap();
            bctx.DcThresholds[0]           = new int[] { 0 };
            bctx.DcThresholds[1]           = new int[] { 0 };
            bctx.DcThresholds[2]           = new int[] { 0 };
            bctx.NumDcCtxs                 = 8;

            YCbCrChromaSubsampling cs444 = new YCbCrChromaSubsampling();
            CompressedDc.DequantDC(new Rect(0, 0, 4, 4), dc, quantDc, modImg, dcFactors, 1.0f, cfl, cs444, bctx);
        }

        /// <summary>
        /// Runs <see cref="Blending.PerformBlending"/> for a single <paramref name="mode"/>, <paramref name="clamp"/>,
        /// and <paramref name="ecInfo"/> configuration.
        /// </summary>
        private static void RunPerformBlendingCase(
            PatchBlendMode mode,
            bool clamp,
            IReadOnlyList<ExtraChannelInfo> ecInfo)
        {
            float* bg0 = stackalloc float[4] { 0.2f, 0.3f, 0.4f, 0.5f };
            float* bg1 = stackalloc float[4] { 0.2f, 0.3f, 0.4f, 0.5f };
            float* bg2 = stackalloc float[4] { 0.2f, 0.3f, 0.4f, 0.5f };
            float* bg3 = stackalloc float[4] { 0.6f, 0.6f, 0.6f, 0.6f };

            float* fg0 = stackalloc float[4] { 0.5f, 0.4f, 0.3f, 0.2f };
            float* fg1 = stackalloc float[4] { 0.5f, 0.4f, 0.3f, 0.2f };
            float* fg2 = stackalloc float[4] { 0.5f, 0.4f, 0.3f, 0.2f };
            float* fg3 = stackalloc float[4] { 0.4f, 0.5f, 0.7f, 0.8f };

            float* dst0 = stackalloc float[4];
            float* dst1 = stackalloc float[4];
            float* dst2 = stackalloc float[4];
            float* dst3 = stackalloc float[4];

            float** bg  = stackalloc float*[4] { bg0, bg1, bg2, bg3 };
            float** fg  = stackalloc float*[4] { fg0, fg1, fg2, fg3 };
            float** dst = stackalloc float*[4] { dst0, dst1, dst2, dst3 };

            PatchBlending colorBlend          = new PatchBlending { Mode = mode, AlphaChannel = 0, Clamp = clamp };
            PatchBlending[] ecBlend           = { new PatchBlending { Mode = mode, AlphaChannel = 0, Clamp = clamp } };
            Blending.BlendingRowPointers ptrs = new Blending.BlendingRowPointers(bg, fg, dst, 0, 4);

            Blending.PerformBlending(
                JxlMemoryManager.Default,
                in ptrs,
                colorBlend,
                ecBlend,
                ecInfo).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Exercises <see cref="TransformId.Palette"/> serialization, <see cref="Transform.MetaApply"/>,
        /// and <see cref="Transform.Inverse"/> across delta predictors.
        /// </summary>
        private static void ExercisePaletteTransform(WeightedHeader wpHeader)
        {
            Transform pal = new Transform(TransformId.Palette)
            {
                BeginC    = 0,
                NumC      = 3,
                NbColors  = 8,
                NbDeltas  = 4,
                Predictor = Predictor.Gradient,
            };

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);
            Bundle.Write(pal, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            Transform palRead = new Transform();
            BitReader br      = new BitReader(bw.GetSpan());
            Bundle.Read(br, palRead).IsOk.ShouldBeTrue();
            br.Close();
            palRead.Id.ShouldBe(TransformId.Palette);

            Predictor[] predictors =
            {
                Predictor.Zero,
                Predictor.Left,
                Predictor.Top,
                Predictor.Average0,
                Predictor.Select,
                Predictor.Gradient,
                Predictor.Weighted,
                Predictor.TopRight,
                Predictor.TopLeft,
                Predictor.LeftLeft,
                Predictor.Average1,
                Predictor.Average2,
                Predictor.Average3,
                Predictor.Average4,
            };

            foreach (Predictor pred in predictors)
            {
                using ModularImage img = ModularImage.Create(JxlMemoryManager.Default, 4, 4, 8, 3).Value;
                Transform t            = new Transform(TransformId.Palette)
                {
                    BeginC    = 0,
                    NumC      = 3,
                    NbColors  = 4,
                    NbDeltas  = 2,
                    Predictor = pred,
                };
                t.MetaApply(img).IsOk.ShouldBeTrue();

                Span<int> idxRow = img.Channels[1].Row(0);
                idxRow[0] = -1;
                idxRow[1] = 0;
                idxRow[2] = 2;
                idxRow[3] = 10;

                t.Inverse(img, wpHeader).IsOk.ShouldBeTrue();
            }
        }
    }
}
