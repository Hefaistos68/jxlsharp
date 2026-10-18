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
    /// Additional high-coverage unit tests for <c>JxlSharp.Decoder</c> covering <see cref="CoeffOrder"/>,
    /// <see cref="DecGroup"/>, <see cref="PassesDecoderState"/>, <see cref="RenderPipelineStages"/>,
    /// <see cref="FrameDecoder"/>, <see cref="JPEGData"/>, <see cref="JpegDataDecoder"/>,
    /// <see cref="JpegDataWriter"/>, <see cref="JxlToJpegDecoder"/>, and <see cref="JxlDecoder"/>.
    /// </summary>
    [TestFixture]
    public unsafe class DecoderAdditionalCoverageTests1
    {
        // =====================================================================
        // 1. CoeffOrder & LehmerCode Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="LehmerCode"/> edge cases and <see cref="CoeffOrder.DecodePermutation"/>
        /// and <see cref="CoeffOrder.DecodeCoeffOrders"/> paths.
        /// </summary>
        [Test]
        public void TestCoeffOrderAndLehmerCodePaths()
        {
            uint[] dupPerm = { 0u, 0u };
            uint[] temp    = new uint[4];
            uint[] code    = new uint[2];

            LehmerCode.ComputeLehmerCode(dupPerm, temp, 2, code);
            code[1].ShouldBe(0u);

            LehmerCode.DecodeLehmerCode(ReadOnlySpan<uint>.Empty, temp, 0, Span<uint>.Empty).IsOk.ShouldBeFalse();

            uint[] badCode = { 2u, 0u };
            uint[] permOut = new uint[2];

            LehmerCode.DecodeLehmerCode(badCode, temp, 2, permOut).IsOk.ShouldBeFalse();

            ExerciseDecodePermutationPaths();
            ExerciseDecodeCoeffOrdersPaths();
        }

        // =====================================================================
        // 2. DecGroup & GetBlockFromEncoder Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="GetBlockFromEncoder"/> and <see cref="DecGroup"/> validation and roundtrip paths.
        /// </summary>
        [Test]
        public void TestDecGroupValidationAndRoundtripPaths()
        {
            using ACImageT<short> ac16 = ACImageT<short>.Make(JxlMemoryManager.Default, 64, 1).Value;
            uint[] shifts              = { 0u };

            GetBlockFromEncoder.Create(new ACImage[] { ac16 }, 0, shifts).IsOk.ShouldBeFalse();

            using ACImageT<int> ac32            = ACImageT<int>.Make(JxlMemoryManager.Default, 256, 4).Value;
            StatusOr<GetBlockFromEncoder> blkOr = GetBlockFromEncoder.Create(new ACImage[] { ac32 }, 0, shifts);

            ac32.ZeroFill();
            blkOr.IsOk.ShouldBeTrue();

            short* s16      = stackalloc short[192];
            ACPtr* blockPtr = stackalloc ACPtr[3];
            blockPtr[0]     = new ACPtr(s16);
            blockPtr[1]     = new ACPtr(s16 + 64);
            blockPtr[2]     = new ACPtr(s16 + 128);

            blkOr.Value.LoadBlock(0, 0, AcStrategy.FromRawStrategy(AcStrategyType.DCT), 64, 0, blockPtr, ACType.k16).IsOk.ShouldBeFalse();

            ExerciseDecGroupErrorGuards();
            ExerciseDecGroupForRoundtripVarDct(ac16, ac32);
            ExerciseDecGroupForRoundtripJpeg(ac32);
        }

        // =====================================================================
        // 3. PassesStateAndOutputStages Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="PassesSharedState"/>, <see cref="PassesDecoderState.PreparePipeline"/>,
        /// <c>BlendingStage</c>, and output stages (<c>WriteToOutputStage</c>,
        /// <c>WriteToImageBundleStage</c>, <c>WriteToImage3FStage</c>).
        /// </summary>
        [Test]
        public void TestPassesStateAndOutputStagesPaths()
        {
            FrameHeader nullMetaHeader = new FrameHeader(null);
            PassesSharedState shared   = new PassesSharedState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(nullMetaHeader, shared, encoder: false).IsOk.ShouldBeFalse();

            ExercisePreparePipelineFastXybPaths();
            ExercisePreparePipelineLinearAndToneMappingPaths();
            ExerciseBlendingStageEdgeCases();
            ExerciseWriteToOutputAndBundleStages();
        }

        // =====================================================================
        // 4. DecFrame (FrameDecoder) Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="FrameDecoder"/> stride calculations, output validation, reference tracking,
        /// stage guards, and <see cref="FrameDecoder.DecodeFrame"/> static helper paths.
        /// </summary>
        [Test]
        public void TestFrameDecoderValidationAndStaticDecodePaths()
        {
            ExerciseFrameDecoderStrideAndOutputValidation();
            ExerciseFrameDecoderSavedAsAndReferences();
            ExerciseFrameDecoderStageGuardsAndJpegErrors();
            ExerciseFrameDecoderStaticDecodeFrame();
        }

        // =====================================================================
        // 5. JpegData, JpegDataDecoder, JpegDataWriter, and JxlToJpegDecoder Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="JPEGData"/> header validation, <see cref="JpegDataDecoder"/> error handling,
        /// <see cref="JpegDataWriter"/> progressive refinement and padding bit paths, and
        /// <see cref="JxlToJpegDecoder"/> box and metadata reconstruction paths.
        /// </summary>
        [Test]
        public void TestJpegDataHeaderAndDecoderPaths()
        {
            ExerciseJpegDataHeaderValidationErrors();
            ExerciseJpegDataComponentAndQuantValidation();
            ExerciseJpegDataHuffmanAndScanValidation();
            ExerciseJpegDataZeroPaddingRoundTrip();
            ExerciseJpegDataDecoderErrorBranches();
        }

        /// <summary>
        /// Verifies <see cref="JpegDataWriter"/> refinement bit encoding, restart markers, padding bit checks,
        /// and <see cref="JxlToJpegDecoder"/> container and metadata reconstruction paths.
        /// </summary>
        [Test]
        public void TestJpegDataWriterAndJxlToJpegDecoderPaths()
        {
            ExerciseJpegDataWriterPaddingAndRestartMarkers();
            ExerciseJpegDataWriterRefinementAndErrorBranches();
            ExerciseJxlToJpegDecoderPaths();
        }

        // =====================================================================
        // 6. JxlDecoder API Guard Clauses & Container State Machine Tests
        // =====================================================================

        /// <summary>
        /// Verifies <see cref="JxlDecoder"/> lifecycle helpers, stage guard clauses, and invalid parameter checks.
        /// </summary>
        [Test]
        public void TestJxlDecoderApiGuardsAndLifecycle()
        {
            JxlDecoder.CheckSignature(null, 0).ShouldBe(JxlSignature.NotEnoughBytes);
            JxlDecoder.CheckSignature(null, 4).ShouldBe(JxlSignature.Invalid);

            using JxlDecoder dec = JxlDecoder.Create(JxlMemoryManager.Default);

            JxlDecoder.JxlDecoderReset(dec);
            dec.SkipFrames(0);
            dec.SkipCurrentFrame().ShouldBe(JxlDecoderStatus.Error);

            ExerciseJxlDecoderPreBasicInfoGuards(dec);
            ExerciseJxlDecoderPostStartGuards();
            ExerciseJxlDecoderCustomMetadataAndFrameQueries();

            JxlDecoder tempDec = JxlDecoder.Create(JxlMemoryManager.Default);

            JxlDecoder.Destroy(tempDec);
            JxlDecoder.Destroy(null);
        }

        /// <summary>
        /// Verifies <see cref="JxlDecoder"/> container box parsing including 64-bit box headers, <c>brob</c>
        /// boxes, <c>jxlp</c> partial codestream boxes, and <c>jbrd</c> JPEG reconstruction metadata boxes.
        /// </summary>
        [Test]
        public void TestJxlDecoderContainerBoxesAndJpegReconstruction()
        {
            ExerciseContainer64BitAndBrobBoxes();
            ExerciseContainerJxlpAndSkipFramePaths();
            ExerciseContainerJbrdAndExifXmpReconstruction();
        }

        // =====================================================================
        // Helper Methods: Section 1 (CoeffOrder)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="CoeffOrder.DecodePermutation"/> with valid, empty-span, and invalid streams.
        /// </summary>
        private static void ExerciseDecodePermutationPaths()
        {
            BitReader truncated = new BitReader(new byte[] { 0x00 });

            CoeffOrder.DecodePermutation(JxlMemoryManager.Default, 0, 4, new uint[4], truncated).IsOk.ShouldBeFalse();
            truncated.Close();

            byte[] sym0Bytes = BuildSingleSymbolAnsStream(8, 0);
            uint[] order     = new uint[4];
            BitReader br1    = new BitReader(sym0Bytes);

            CoeffOrder.DecodePermutation(JxlMemoryManager.Default, 1, 4, order, br1).IsOk.ShouldBeTrue();
            br1.Close();
            order[0].ShouldBe(0u);
            order[3].ShouldBe(3u);

            BitReader brEmpty = new BitReader(sym0Bytes);

            CoeffOrder.DecodePermutation(JxlMemoryManager.Default, 0, 4, Span<uint>.Empty, brEmpty).IsOk.ShouldBeTrue();
            brEmpty.Close();

            byte[] sym5Bytes = BuildSingleSymbolAnsStream(8, 5);
            BitReader brEnd  = new BitReader(sym5Bytes);

            CoeffOrder.DecodePermutation(JxlMemoryManager.Default, 0, 4, order, brEnd).IsOk.ShouldBeFalse();
            brEnd.Close();

            byte[] sym3Bytes = BuildSingleSymbolAnsStream(8, 3);
            BitReader brVal  = new BitReader(sym3Bytes);

            CoeffOrder.DecodePermutation(JxlMemoryManager.Default, 0, 4, order, brVal).IsOk.ShouldBeFalse();
            brVal.Close();
        }

        /// <summary>
        /// Exercises <see cref="CoeffOrder.DecodeCoeffOrders"/> with active and inactive AC strategies.
        /// </summary>
        private static void ExerciseDecodeCoeffOrdersPaths()
        {
            uint[] coeffOrder = new uint[CoeffOrder.kCoeffOrderMaxSize];
            BitReader brBad   = new BitReader(new byte[] { 0x00 });

            CoeffOrder.DecodeCoeffOrders(JxlMemoryManager.Default, 1, 1, coeffOrder, brBad).IsOk.ShouldBeFalse();
            brBad.Close();

            byte[] validUsed = BuildSingleSymbolAnsStream(8, 0);
            BitReader brUsed = new BitReader(validUsed);

            CoeffOrder.DecodeCoeffOrders(JxlMemoryManager.Default, 1, 1, coeffOrder, brUsed).IsOk.ShouldBeTrue();
            brUsed.Close();

            BitReader brUnused = new BitReader(validUsed);

            CoeffOrder.DecodeCoeffOrders(JxlMemoryManager.Default, 1, 0, coeffOrder, brUnused).IsOk.ShouldBeTrue();
            brUnused.Close();

            byte[] validPerm = BuildSingleSymbolAnsStream(8, 15);
            BitReader brPerm = new BitReader(validPerm);

            CoeffOrder.DecodeCoeffOrders(JxlMemoryManager.Default, 1, 1, coeffOrder, brPerm).IsOk.ShouldBeTrue();
            brPerm.Close();

            byte[] invalidEnd = BuildSingleSymbolAnsStream(8, 20);
            BitReader brInv   = new BitReader(invalidEnd);

            CoeffOrder.DecodeCoeffOrders(JxlMemoryManager.Default, 1, 1, coeffOrder, brInv).IsOk.ShouldBeFalse();
            brInv.Close();
        }

        /// <summary>
        /// Builds a bitstream containing a 1-histogram ANS stream that deterministically emits <paramref name="symbol"/>.
        /// </summary>
        private static byte[] BuildSingleSymbolAnsStream(nuint numContexts, nuint symbol)
        {
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            bw.Write(1, 0);

            if (numContexts > 1)
            {
                bw.Write(1, 1);
                bw.Write(2, 0);
            }

            bw.Write(1, 0);
            bw.Write(2, 0);
            DecAns.EncodeUintConfig(new HybridUintConfig(4, 0, 0), bw, 5);
            bw.Write(1, 1);
            bw.Write(1, 0);
            DecAns.StoreVarLenUint8(symbol, bw);
            bw.Write(32, (ulong)AnsParams.AnsSignature << 16);
            bw.ZeroPadToByte();

            return bw.GetSpan().ToArray();
        }

        // =====================================================================
        // Helper Methods: Section 2 (DecGroup)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="DecGroup.DecodeGroup(in DecGroup.DecodeGroupArgs)"/> parameter validation guards.
        /// </summary>
        private static void ExerciseDecGroupErrorGuards()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            FrameHeader fh              = new FrameHeader(meta) { Encoding = FrameEncoding.VarDCT };
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();

            BitReader?[] readers                = { new BitReader(new byte[16]) };
            DecGroup.DecodeGroupArgs dcBad      = new DecGroup.DecodeGroupArgs
            {
                FrameHeader         = fh,
                Readers             = readers,
                GroupIdx            = 0,
                DecState            = decState,
                GroupDecCache       = new GroupDecCache(),
                Thread              = 0,
                RenderPipelineInput = null!,
                JpegData            = null,
                FirstPass           = 0,
                NumPasses           = 1,
                ForceDraw           = false,
                DcOnly              = true,
            };

            DecGroup.DecodeGroup(in dcBad).IsOk.ShouldBeFalse();

            DecGroup.DecodeGroupArgs histBad    = new DecGroup.DecodeGroupArgs
            {
                FrameHeader         = fh,
                Readers             = readers,
                GroupIdx            = 0,
                DecState            = decState,
                GroupDecCache       = new GroupDecCache(),
                Thread              = 0,
                RenderPipelineInput = null!,
                JpegData            = null,
                FirstPass           = 0,
                NumPasses           = 1,
                ForceDraw           = false,
                DcOnly              = false,
            };

            DecGroup.DecodeGroup(in histBad).IsOk.ShouldBeFalse();
            readers[0]!.Close();
        }

        /// <summary>
        /// Exercises <see cref="DecGroup.DecodeGroupForRoundtrip(in DecGroup.DecodeGroupRoundtripArgs)"/> with VarDCT
        /// including multi-block <c>DCT16X16</c> and empty <c>k16</c> coefficients.
        /// </summary>
        private static void ExerciseDecGroupForRoundtripVarDct(ACImageT<short> ac16, ACImageT<int> ac32)
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            FrameHeader fh              = new FrameHeader(meta) { Encoding = FrameEncoding.VarDCT };
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.Init(fh).IsOk.ShouldBeTrue();
            decState.Shared.Matrices.EnsureComputed(
                JxlMemoryManager.Default,
                (1u << (int)AcStrategyType.DCT) | (1u << (int)AcStrategyType.DCT16X16)).IsOk.ShouldBeTrue();
            ImageOps.FillImage(1, decState.Shared.RawQuantField);
            decState.Shared.AcStrategy.Set(0, 0, AcStrategyType.DCT16X16).IsOk.ShouldBeTrue();
            decState.Coefficients = new ACImageT<short>();

            using Image3<float> idct       = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;
            RenderPipeline.Builder builder = new RenderPipeline.Builder(JxlMemoryManager.Default, 3);

            builder.AddStage(RenderPipelineStages.GetWriteToImage3FStage(JxlMemoryManager.Default, idct)).IsOk.ShouldBeTrue();

            using RenderPipeline pipe  = builder.Finalize(decState.Shared.FrameDim).Value;

            pipe.PrepareForThreads(1, useGroupIds: false).IsOk.ShouldBeTrue();

            RenderPipelineInput pipeIn  = pipe.GetInputBuffers(0, 0);
            GroupDecCache groupDecCache = new GroupDecCache();

            DecGroup.DecodeGroupRoundtripArgs badAcArgs = new DecGroup.DecodeGroupRoundtripArgs
            {
                FrameHeader         = fh,
                Ac                  = new ACImage[] { ac16 },
                GroupIdx            = 0,
                DecState            = decState,
                GroupDecCache       = groupDecCache,
                Thread              = 0,
                RenderPipelineInput = pipeIn,
                JpegData            = null,
            };

            DecGroup.DecodeGroupForRoundtrip(in badAcArgs).IsOk.ShouldBeFalse();

            decState.Coefficients = new ACImageT<int>();

            DecGroup.DecodeGroupRoundtripArgs okArgs = new DecGroup.DecodeGroupRoundtripArgs
            {
                FrameHeader         = fh,
                Ac                  = new ACImage[] { ac32 },
                GroupIdx            = 0,
                DecState            = decState,
                GroupDecCache       = groupDecCache,
                Thread              = 0,
                RenderPipelineInput = pipeIn,
                JpegData            = null,
            };

            DecGroup.DecodeGroupForRoundtrip(in okArgs).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Exercises <see cref="DecGroup.DecodeGroupForRoundtrip(in DecGroup.DecodeGroupRoundtripArgs)"/> with
        /// <see cref="JPEGData"/> validation, <see cref="ColorTransform.None"/>, and YCbCr chroma-from-luma.
        /// </summary>
        private static void ExerciseDecGroupForRoundtripJpeg(ACImageT<int> ac32)
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            FrameHeader fh              = new FrameHeader(meta) { Encoding = FrameEncoding.VarDCT, ColorTransform = ColorTransform.YCbCr };
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.Init(fh).IsOk.ShouldBeTrue();
            decState.Coefficients = new ACImageT<int>();

            for (nuint by = 0; by < 2; by++)
            {
                for (nuint bx = 0; bx < 2; bx++)
                {
                    decState.Shared.AcStrategy.Set(bx, by, AcStrategyType.DCT).IsOk.ShouldBeTrue();
                }
            }

            ImageOps.FillImage(1, decState.Shared.RawQuantField);

            using Image3<float> idct       = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;
            RenderPipeline.Builder builder = new RenderPipeline.Builder(JxlMemoryManager.Default, 3);

            builder.AddStage(RenderPipelineStages.GetWriteToImage3FStage(JxlMemoryManager.Default, idct)).IsOk.ShouldBeTrue();

            using RenderPipeline pipe  = builder.Finalize(decState.Shared.FrameDim).Value;

            pipe.PrepareForThreads(1, useGroupIds: false).IsOk.ShouldBeTrue();

            RenderPipelineInput pipeIn  = pipe.GetInputBuffers(0, 0);
            GroupDecCache groupDecCache = new GroupDecCache();
            JPEGData jpg                = CreateSampleJpegData(progressive: false);

            DecGroup.DecodeGroupRoundtripArgs rt = new DecGroup.DecodeGroupRoundtripArgs
            {
                FrameHeader         = fh,
                Ac                  = new ACImage[] { ac32 },
                GroupIdx            = 0,
                DecState            = decState,
                GroupDecCache       = groupDecCache,
                Thread              = 0,
                RenderPipelineInput = pipeIn,
                JpegData            = jpg,
            };

            DecGroup.DecodeGroupForRoundtrip(in rt).IsOk.ShouldBeFalse();

            ConfigureValidRawQuantForJpeg(decState.Shared.Matrices, decState.Shared.Cmap.Base);
            decState.Shared.Cmap.YtoXMap!.Row(0)[0] = 8;
            decState.Shared.Cmap.YtoBMap!.Row(0)[0] = 8;

            DecGroup.DecodeGroupForRoundtrip(in rt).IsOk.ShouldBeTrue();

            fh.ColorTransform = ColorTransform.None;
            DecGroup.DecodeGroupForRoundtrip(in rt).IsOk.ShouldBeTrue();

            fh.ColorTransform = ColorTransform.XYB;
            DecGroup.DecodeGroupForRoundtrip(in rt).IsOk.ShouldBeFalse();

            fh.ColorTransform = ColorTransform.YCbCr;
            decState.Shared.Cmap.Base.SetColorFactor(100);
            DecGroup.DecodeGroupForRoundtrip(in rt).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Configures 3 raw <c>8x8</c> quantization tables on <paramref name="matrices"/> and JPEG-compatible
        /// zero base correlations on <paramref name="cmapBase"/> for JPEG roundtrip tests.
        /// </summary>
        private static void ConfigureValidRawQuantForJpeg(DequantMatrices matrices, ColorCorrelation cmapBase)
        {
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            bw.Write(1, 0);
            bw.Write(2, 0);
            bw.Write(16, 0);
            bw.Write(16, 0);
            bw.Write(8, 128);
            bw.Write(8, 128);
            bw.ZeroPadToByte();

            BitReader br = new BitReader(bw.GetSpan());

            cmapBase.DecodeDC(br).IsOk.ShouldBeTrue();
            br.Close();

            int[] qtable = new int[192];

            Array.Fill(qtable, 4);
            matrices.Encodings[0].Mode         = QuantEncoding.QuantMode.kQuantModeRAW;
            matrices.Encodings[0].QrawTableDen = 1.0f / (8.0f * 255.0f);
            matrices.Encodings[0].QrawTable    = qtable;
        }

        // =====================================================================
        // Helper Methods: Section 3 (PassesStateAndOutputStages)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="PassesDecoderState.PreparePipeline"/> fast XYB-to-sRGB8 conversion guards and success path.
        /// </summary>
        private static void ExercisePreparePipelineFastXybPaths()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            FrameHeader fh              = new FrameHeader(meta) { Encoding = FrameEncoding.VarDCT, IsLast = false };
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.Init(fh).IsOk.ShouldBeTrue();

            byte[] outBuf = new byte[16 * 16 * 4];

            fixed (byte* pOut = outBuf)
            {
                ImageOutput mainOut = new ImageOutput
                {
                    Format        = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8, Align = 0 },
                    BitsPerSample = 8,
                    Buffer        = pOut,
                    BufferSize    = (nuint)outBuf.Length,
                    Stride        = 16 * 4,
                    Callback      = default,
                };
                PassesDecoderState.PipelineOptions opts = new PassesDecoderState.PipelineOptions
                {
                    UseSlowRenderPipeline = false,
                    Coalescing            = true,
                    RenderSpotcolors      = true,
                    RenderNoise           = false,
                };

                decState.Width                  = 16;
                decState.Height                 = 16;
                decState.FastXybSrgb8Conversion = true;
                decState.MainOutput             = mainOut;
                decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

                fh.BlendingInfo.Mode = BlendMode.Blend;
                decState.PreparePipeline(fh, meta.M, null, opts).IsOk.ShouldBeFalse();
                fh.BlendingInfo.Mode = BlendMode.Replace;

                fh.SaveAsReference          = 1;
                fh.SaveBeforeColorTransform = false;
                decState.PreparePipeline(fh, meta.M, null, opts).IsOk.ShouldBeFalse();
                fh.SaveAsReference = 0;
                fh.IsLast          = true;

                meta.M.ExtraChannelInfo.Add(new ExtraChannelInfo { Type = ExtraChannel.SpotColor });
                decState.PreparePipeline(fh, meta.M, null, opts).IsOk.ShouldBeFalse();
                meta.M.ExtraChannelInfo.Clear();

                decState.PreparePipeline(fh, meta.M, null, opts).IsOk.ShouldBe(DecXyb.HasFastXYBTosRGB8());
            }
        }

        /// <summary>
        /// Exercises <see cref="PassesDecoderState.PreparePipeline"/> linear color space blending, reference saving,
        /// and non-linear PQ tone mapping paths.
        /// </summary>
        private static void ExercisePreparePipelineLinearAndToneMappingPaths()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            FrameHeader fh              = new FrameHeader(meta) { Encoding = FrameEncoding.VarDCT, IsLast = false };
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.Init(fh).IsOk.ShouldBeTrue();

            PassesDecoderState.PipelineOptions opts = new PassesDecoderState.PipelineOptions
            {
                UseSlowRenderPipeline = false,
                Coalescing            = true,
                RenderSpotcolors      = false,
                RenderNoise           = false,
            };

            decState.PreparePipeline(fh, meta.M, null, opts).IsOk.ShouldBeFalse();

            using ImageBundle decoded = new ImageBundle(JxlMemoryManager.Default, meta.M);
            using Image3<float> refC  = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;

            decState.Shared.ReferenceFrames[0].Frame!.SetFromImage(refC, meta.M.ColorEncoding).IsOk.ShouldBeTrue();

            decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();
            decState.OutputEncodingInfo.ColorEncodingIsOriginal = true;
            decState.OutputEncodingInfo.ColorEncoding.Tf.SetTransferFunction(TransferFunction.SRGB);

            fh.BlendingInfo.Mode        = BlendMode.Add;
            fh.BlendingInfo.Source      = 0;
            fh.SaveAsReference          = 1;
            fh.SaveBeforeColorTransform = false;

            decState.PreparePipeline(fh, meta.M, decoded, opts).IsOk.ShouldBeTrue();

            fh.ColorTransform    = ColorTransform.None;
            fh.BlendingInfo.Mode = BlendMode.Replace;
            fh.SaveAsReference   = 0;

            meta.M.ColorEncoding.Tf.SetTransferFunction(TransferFunction.PQ);
            decState.OutputEncodingInfo.OrigColorEncoding.Tf.SetTransferFunction(TransferFunction.PQ);
            decState.OutputEncodingInfo.OrigIntensityTarget    = 4000.0f;
            decState.OutputEncodingInfo.DesiredIntensityTarget = 250.0f;
            decState.OutputEncodingInfo.ColorEncoding.Tf.SetTransferFunction(TransferFunction.PQ);

            decState.PreparePipeline(fh, meta.M, decoded, opts).IsOk.ShouldBeTrue();

            decState.OutputEncodingInfo.ColorEncoding.Tf.SetTransferFunction(TransferFunction.Unknown);
            decState.PreparePipeline(fh, meta.M, decoded, opts).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <c>BlendingStage</c> edge cases including XYB reference frames, cropped origins,
        /// non-original color encoding, and empty extra channel reference planes.
        /// </summary>
        private static void ExerciseBlendingStageEdgeCases()
        {
            CodecMetadata meta = CreateDefaultVarDctMetadata(16, 16);

            meta.M.ExtraChannelInfo.Add(new ExtraChannelInfo
            {
                Type     = ExtraChannel.Alpha,
                BitDepth = new BitDepth { BitsPerSample = 8 },
            });

            FrameHeader fh                 = new FrameHeader(meta);
            fh.BlendingInfo.Mode           = BlendMode.Add;
            fh.BlendingInfo.Source         = 0;
            fh.ExtraChannelBlendingInfo[0] = new BlendingInfo { Mode = BlendMode.Add, Source = 1 };

            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            PassesSharedState.InitializePassesSharedState(fh, decState.Shared, encoder: false).IsOk.ShouldBeTrue();
            decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

            using RenderPipelineStage stage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);

            stage.GetName().ShouldBe("Blending");
            stage.IsInitialized().IsOk.ShouldBeTrue();

            using Image3<float> bgColor = Image3<float>.Create(JxlMemoryManager.Default, 16, 16).Value;

            decState.Shared.ReferenceFrames[0].Frame!.SetFromImage(bgColor, meta.M.ColorEncoding).IsOk.ShouldBeTrue();
            decState.Shared.ReferenceFrames[0].IbIsInXyb = true;

            using RenderPipelineStage xybBgStage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);

            xybBgStage.IsInitialized().IsOk.ShouldBeFalse();
            decState.Shared.ReferenceFrames[0].IbIsInXyb = false;

            decState.Shared.ReferenceFrames[0].Frame!.Origin = new FrameOrigin { X0 = 4, Y0 = 4 };

            using RenderPipelineStage cropBgStage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);

            cropBgStage.IsInitialized().IsOk.ShouldBeFalse();
            decState.Shared.ReferenceFrames[0].Frame!.Origin = new FrameOrigin { X0 = 0, Y0 = 0 };

            using RenderPipelineStage validBgStage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);

            validBgStage.IsInitialized().IsOk.ShouldBeTrue();

            float* r0           = stackalloc float[128];
            float* r1           = stackalloc float[128];
            float* r2           = stackalloc float[128];
            float* r3           = stackalloc float[128];
            float*[][] rows     = CreateRowPointers4(r0, r1, r2, r3);
            float*[][] emptyOut = Array.Empty<float*[]>();

            validBgStage.ProcessRow(rows, emptyOut, (0, 0), 16, 0, 2, 0).IsOk.ShouldBeTrue();
            xybBgStage.ProcessRow(rows, emptyOut, (0, 0), 16, 0, 2, 0).IsOk.ShouldBeFalse();
            validBgStage.ProcessRow(rows, emptyOut, (1, 0), 16, 0, 2, 0).IsOk.ShouldBeFalse();

            decState.OutputEncodingInfo.ColorEncodingIsOriginal = false;

            using RenderPipelineStage badEncStage = RenderPipelineStages.GetBlendingStage(fh, decState, meta.M.ColorEncoding);

            badEncStage.IsInitialized().IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <c>WriteToOutputStage</c>, <c>WriteToImageBundleStage</c>, and
        /// <c>WriteToImage3FStage</c> orientation, callback, and guard paths.
        /// </summary>
        private static void ExerciseWriteToOutputAndBundleStages()
        {
            byte[] buf           = new byte[8 * 8 * 4];
            int callbackRowCount = 0;

            fixed (byte* pBuf = buf)
            {
                PixelCallback cb = new PixelCallback(
                    (_, _, _) => new object(),
                    (_, _, _, _, numPixels, _) => { callbackRowCount += (int)numPixels; },
                    _ => { },
                    null);
                ImageOutput cbOut = new ImageOutput
                {
                    Format        = new JxlPixelFormat { NumChannels = 3, DataType = JxlDataType.UInt8, Align = 0 },
                    BitsPerSample = 8,
                    Buffer        = null,
                    BufferSize    = (nuint)buf.Length,
                    Stride        = 8 * 3,
                    Callback      = cb,
                };
                ImageOutput bufOut = new ImageOutput
                {
                    Format        = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8, Align = 0 },
                    BitsPerSample = 8,
                    Buffer        = pBuf,
                    BufferSize    = (nuint)buf.Length,
                    Stride        = 8 * 4,
                    Callback      = default,
                };

                using RenderPipelineStage flipStage = RenderPipelineStages.GetWriteToOutputStage(
                    bufOut, (8, 8), (true, false, 3), Orientation.FlipHorizontal, Array.Empty<ImageOutput>(), JxlMemoryManager.Default);

                flipStage.GetName().ShouldBe("WritePixelCB");

                float* c0           = stackalloc float[128];
                float* c1           = stackalloc float[128];
                float* c2           = stackalloc float[128];
                float* c3           = stackalloc float[128];
                float*[][] rows     = CreateRowPointers4(c0, c1, c2, c3);
                float*[][] emptyOut = Array.Empty<float*[]>();

                flipStage.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (8, 8), (8, 8) }).IsOk.ShouldBeTrue();
                flipStage.PrepareForThreads(1).IsOk.ShouldBeTrue();
                flipStage.ProcessRow(rows, emptyOut, (1, 0), 8, 0, 0, 0).IsOk.ShouldBeFalse();
                flipStage.ProcessRow(rows, emptyOut, (0, 0), 8, 0, 0, 0).IsOk.ShouldBeTrue();
                flipStage.ProcessRow(rows, emptyOut, (0, 0), 8, 0, 8, 0).IsOk.ShouldBeTrue();

                using RenderPipelineStage rotStage = RenderPipelineStages.GetWriteToOutputStage(
                    cbOut, (8, 8), (false, false, 0), Orientation.Rotate270, Array.Empty<ImageOutput>(), JxlMemoryManager.Default);

                rotStage.ProcessRow(rows, emptyOut, (0, 0), 8, 0, 0, 0).IsOk.ShouldBeFalse();
                rotStage.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (8, 8) }).IsOk.ShouldBeTrue();
                rotStage.PrepareForThreads(1).IsOk.ShouldBeTrue();
                rotStage.ProcessRow(rows, emptyOut, (0, 0), 8, 0, 0, 0).IsOk.ShouldBeTrue();
                callbackRowCount.ShouldBe(8);
            }

            ExerciseBundleAndImage3FStageGuards();
        }

        /// <summary>
        /// Exercises <c>WriteToImageBundleStage</c> and <c>WriteToImage3FStage</c> validation guards.
        /// </summary>
        private static void ExerciseBundleAndImage3FStageGuards()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(8, 8);
            using ImageBundle ib        = new ImageBundle(JxlMemoryManager.Default, meta.M);
            using RenderPipelineStage b = RenderPipelineStages.GetWriteToImageBundleStage(ib, meta.M.ColorEncoding);

            b.GetName().ShouldBe("WriteIB");
            b.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8) }).IsOk.ShouldBeFalse();
            b.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (4, 8) }).IsOk.ShouldBeFalse();
            b.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (8, 8) }).IsOk.ShouldBeTrue();

            float* r0           = stackalloc float[128];
            float*[][] rows     = CreateRowPointers4(r0, r0, r0, r0);
            float*[][] emptyOut = Array.Empty<float*[]>();

            b.ProcessRow(rows, emptyOut, (1, 0), 8, 0, 0, 0).IsOk.ShouldBeFalse();

            using Image3<float> img3     = Image3<float>.Create(JxlMemoryManager.Default, 8, 8).Value;
            using RenderPipelineStage s3 = RenderPipelineStages.GetWriteToImage3FStage(JxlMemoryManager.Default, img3);

            s3.GetName().ShouldBe("WriteI3F");
            s3.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8) }).IsOk.ShouldBeFalse();
            s3.SetInputSizes(new (nuint, nuint)[] { (8, 8), (8, 8), (4, 8) }).IsOk.ShouldBeFalse();
        }

        // =====================================================================
        // Helper Methods: Section 4 (DecFrame / FrameDecoder)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="FrameDecoder"/> stride calculation and image/extra-channel output validation guards.
        /// </summary>
        private static void ExerciseFrameDecoderStrideAndOutputValidation()
        {
            CodecMetadata meta = CreateDefaultVarDctMetadata(16, 16);

            meta.M.ExtraChannelInfo.Add(new ExtraChannelInfo
            {
                Type            = ExtraChannel.Alpha,
                BitDepth        = new BitDepth { BitsPerSample = 8 },
                AlphaAssociated = true,
            });

            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            decState.Shared.Metadata = meta;

            FrameDecoder fd          = new FrameDecoder(decState, meta, null, useSlowRenderingPipeline: false);
            JxlPixelFormat badFormat = new JxlPixelFormat { NumChannels = 4, DataType = (JxlDataType)99, Align = 0 };

            FrameDecoder.GetStride(16, badFormat).IsOk.ShouldBeFalse();
            FrameDecoder.GetStride(nuint.MaxValue, new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.Float }).IsOk.ShouldBeFalse();
            FrameDecoder.GetStride(nuint.MaxValue / 16, new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.Float, Align = 32 }).IsOk.ShouldBeFalse();

            fd.SetImageOutput(default, null, 0, (16, 16), badFormat, 8, (false, false)).IsOk.ShouldBeFalse();
            fd.AddExtraChannelOutput(null, 0, 16, badFormat, 8).IsOk.ShouldBeFalse();

            byte[] buf = new byte[16 * 16 * 4];

            fixed (byte* pBuf = buf)
            {
                JxlPixelFormat okFormat = new JxlPixelFormat { NumChannels = 4, DataType = JxlDataType.UInt8, Align = 0 };

                fd.SetImageOutput(default, pBuf, (nuint)buf.Length, (16, 16), okFormat, 8, (true, true)).IsOk.ShouldBeTrue();
            }
        }

        /// <summary>
        /// Exercises <see cref="FrameDecoder.SavedAs"/> and <see cref="FrameDecoder.References"/> across frame types and flags.
        /// </summary>
        private static void ExerciseFrameDecoderSavedAsAndReferences()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            decState.Shared.Metadata    = meta;
            decState.Shared.DcFrames[0] = Image3<float>.Create(JxlMemoryManager.Default, 2, 2).Value;

            using FrameDecoder fd = new FrameDecoder(decState, meta, null, useSlowRenderingPipeline: false);
            FrameHeader fh        = fd.GetFrameHeader();

            fh.FrameType = FrameType.DCFrame;
            fh.DcLevel   = 1;
            FrameDecoder.SavedAs(fh).ShouldBe(16);

            fh.FrameType       = FrameType.RegularFrame;
            fh.SaveAsReference = 2;
            fh.IsLast          = false;
            FrameDecoder.SavedAs(fh).ShouldBe(4);

            fh.IsLast = true;
            FrameDecoder.SavedAs(fh).ShouldBe(0);

            fd.References().ShouldBe(0);

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            fh.AllDefault          = false;
            fh.Flags               = FrameHeader.KUseDcFrame;
            fh.DcLevel             = 0;
            fh.BlendingInfo.Mode   = BlendMode.Add;
            fh.BlendingInfo.Source = 1;
            Bundle.Write(fh, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();
            bw.Write(32, 0);

            BitReader br         = new BitReader(bw.GetSpan());
            using ImageBundle ib = new ImageBundle(JxlMemoryManager.Default, meta.M);

            fd.InitFrame(br, ib, isPreview: false).IsOk.ShouldBeTrue();
            fd.InitFrameOutput().IsOk.ShouldBeTrue();
            fd.References().ShouldBe(-1);
            br.Close();
        }

        /// <summary>
        /// Exercises <see cref="FrameDecoder"/> stage guard checks and JPEG transcoding validation errors.
        /// </summary>
        private static void ExerciseFrameDecoderStageGuardsAndJpegErrors()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(16, 16);
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            decState.Shared.Metadata = meta;

            using FrameDecoder fd = new FrameDecoder(decState, meta, null, useSlowRenderingPipeline: false);

            fd.SetPauseAtProgressive(JxlProgressiveDetail.Passes);
            fd.ProcessSections(Array.Empty<SectionInfo>(), 0, Array.Empty<SectionStatus>()).IsOk.ShouldBeTrue();

            SectionInfo[] dummySec   = { new SectionInfo { Id = 0, Index = 0 } };
            SectionStatus[] dummySts = new SectionStatus[1];

            fd.ProcessSections(dummySec, 1, dummySts).IsOk.ShouldBeFalse();
            fd.FinalizeFrame().IsOk.ShouldBeFalse();

            BitWriter bw   = new BitWriter(JxlMemoryManager.Default);
            FrameHeader fh = fd.GetFrameHeader();

            fh.AllDefault     = false;
            fh.Encoding       = FrameEncoding.Modular;
            fh.ColorTransform = ColorTransform.None;
            Bundle.Write(fh, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();
            bw.Write(32, 0);

            BitReader br         = new BitReader(bw.GetSpan());
            using ImageBundle ib = new ImageBundle(JxlMemoryManager.Default, meta.M);

            ib.JpegData = CreateSampleJpegData(progressive: false);
            fd.InitFrame(br, ib, isPreview: false).IsOk.ShouldBeTrue();
            fd.InitFrameOutput().IsOk.ShouldBeFalse();
            br.Close();
        }

        /// <summary>
        /// Exercises <see cref="FrameDecoder.DecodeFrame"/> static helper on both valid JXL frames and truncated inputs.
        /// </summary>
        private static void ExerciseFrameDecoderStaticDecodeFrame()
        {
            CodecMetadata meta          = CreateDefaultVarDctMetadata(1, 1);
            PassesDecoderState decState = new PassesDecoderState(JxlMemoryManager.Default);

            decState.Shared.Metadata = meta;

            using ImageBundle decoded = new ImageBundle(JxlMemoryManager.Default, meta.M);

            fixed (byte* pJxl = Sample1x1JxlCodestream)
            {
                BitReader br = new BitReader(Sample1x1JxlCodestream);

                br.ReadFixedBits(16);
                Headers.ReadSizeHeader(br, meta.Size).IsOk.ShouldBeTrue();
                ImageMetadataMethods.ReadImageMetadata(br, meta.M).IsOk.ShouldBeTrue();
                meta.TransformData.NonserializedXybEncoded = meta.M.XybEncoded;
                Bundle.Read(br, meta.TransformData).IsOk.ShouldBeTrue();
                br.JumpToByteBoundary().IsOk.ShouldBeTrue();
                decState.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

                nuint frameOffset = br.TotalBitsConsumed() / 8;
                br.Close();

                byte* nextInBad  = pJxl + frameOffset;
                nuint availInBad = 1;
                FrameHeader fh   = new FrameHeader(meta);

                FrameDecoder.DecodeFrame(
                    decState,
                    null,
                    ref nextInBad,
                    ref availInBad,
                    fh,
                    decoded,
                    meta).IsOk.ShouldBeFalse();

                PassesDecoderState decStateOk = new PassesDecoderState(JxlMemoryManager.Default);

                decStateOk.Shared.Metadata = meta;
                decStateOk.OutputEncodingInfo.SetFromMetadata(meta).IsOk.ShouldBeTrue();

                FrameHeader fhOk = new FrameHeader(meta);
                byte* nextInOk   = pJxl + frameOffset;
                nuint availInOk  = (nuint)Sample1x1JxlCodestream.Length - frameOffset;

                FrameDecoder.DecodeFrame(
                    decStateOk,
                    null,
                    ref nextInOk,
                    ref availInOk,
                    fhOk,
                    decoded,
                    meta).IsOk.ShouldBeTrue();
            }
        }

        // =====================================================================
        // Helper Methods: Section 5 (JpegData / Decoder / Writer / JxlToJpeg)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="JPEGData"/> marker, APP, COM, and quantization header validation error branches.
        /// </summary>
        private static void ExerciseJpegDataHeaderValidationErrors()
        {
            JPEGData noEoi = CreateSampleJpegData(progressive: false);

            noEoi.MarkerOrder[noEoi.MarkerOrder.Count - 1] = 0xD8;
            TryRoundTripJpegDataHeader(noEoi).ShouldBeFalse();

            JPEGData noScans = CreateSampleJpegData(progressive: false);

            noScans.MarkerOrder.Remove(0xDA);
            noScans.ScanInfo.Clear();
            TryRoundTripJpegDataHeader(noScans).ShouldBeFalse();

            JPEGData badAppType = CreateSampleJpegData(progressive: false);

            badAppType.AppMarkerTypes[0] = (AppMarkerType)99;
            TryRoundTripJpegDataHeader(badAppType).ShouldBeFalse();

            JPEGData shortApp = CreateSampleJpegData(progressive: false);

            shortApp.AppData[0] = new byte[] { 0xE0, 0x00 };
            TryRoundTripJpegDataHeader(shortApp).ShouldBeFalse();

            JPEGData shortCom = CreateSampleJpegData(progressive: false);

            shortCom.ComData[0] = new byte[] { 0xFE, 0x00 };
            TryRoundTripJpegDataHeader(shortCom).ShouldBeFalse();

            JPEGData badQuantPrec = CreateSampleJpegData(progressive: false);

            badQuantPrec.Quant[0].Precision = 2;
            TryRoundTripJpegDataHeader(badQuantPrec).ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="JPEGData"/> component ID schemes (Gray, RGB, Custom) and quantization table usage checks.
        /// </summary>
        private static void ExerciseJpegDataComponentAndQuantValidation()
        {
            JPEGData rgbJpg = CreateSampleJpegData(progressive: false);

            rgbJpg.Components[0].Id = 'R';
            rgbJpg.Components[1].Id = 'G';
            rgbJpg.Components[2].Id = 'B';
            TryRoundTripJpegDataHeader(rgbJpg).ShouldBeTrue();

            JPEGData customJpg = CreateSampleJpegData(progressive: false);

            customJpg.Components[0].Id = 10;
            customJpg.Components[1].Id = 20;
            customJpg.Components[2].Id = 30;
            TryRoundTripJpegDataHeader(customJpg).ShouldBeTrue();

            JPEGData grayJpg = CreateSampleJpegData(progressive: false);

            grayJpg.Components.RemoveAt(2);
            grayJpg.Components.RemoveAt(1);
            grayJpg.Quant.RemoveAt(1);
            grayJpg.Quant[0].IsLast           = true;
            grayJpg.ScanInfo[0].NumComponents = 1;
            TryRoundTripJpegDataHeader(grayJpg).ShouldBeTrue();

            JPEGData badQuantIdx = CreateSampleJpegData(progressive: false);

            badQuantIdx.Components[1].QuantIdx = 3;
            TryRoundTripJpegDataHeader(badQuantIdx).ShouldBeFalse();

            JPEGData unusedQ0 = CreateSampleJpegData(progressive: false);

            unusedQ0.Components[0].QuantIdx = 1;
            TryRoundTripJpegDataHeader(unusedQ0).ShouldBeFalse();

            JPEGData unusedQ1 = CreateSampleJpegData(progressive: false);

            unusedQ1.Components[1].QuantIdx = 0;
            unusedQ1.Components[2].QuantIdx = 0;
            TryRoundTripJpegDataHeader(unusedQ1).ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="JPEGData"/> Huffman table, scan component, reset point, and extra zero run validation.
        /// </summary>
        private static void ExerciseJpegDataHuffmanAndScanValidation()
        {
            JPEGData emptyHuff = CreateSampleJpegData(progressive: false);

            Array.Clear(emptyHuff.HuffmanCode[0].Counts);
            TryRoundTripJpegDataHeader(emptyHuff).ShouldBeTrue();

            JPEGData missingEoiHuff = CreateSampleJpegData(progressive: false);

            missingEoiHuff.HuffmanCode[0].Values[12] = 12;
            TryRoundTripJpegDataHeader(missingEoiHuff).ShouldBeFalse();

            JPEGData dupSymHuff = CreateSampleJpegData(progressive: false);

            dupSymHuff.HuffmanCode[0].Values[1] = dupSymHuff.HuffmanCode[0].Values[0];
            TryRoundTripJpegDataHeader(dupSymHuff).ShouldBeFalse();

            JPEGData badDcSym = CreateSampleJpegData(progressive: false);

            badDcSym.HuffmanCode[0].Values[0] = 20;
            TryRoundTripJpegDataHeader(badDcSym).ShouldBeFalse();

            JPEGData badScanComp = CreateSampleJpegData(progressive: false);

            badScanComp.ScanInfo[0].Components[0].CompIdx = 3;
            TryRoundTripJpegDataHeader(badScanComp).ShouldBeFalse();

            JPEGData badExtraRun = CreateSampleJpegData(progressive: false);

            badExtraRun.ScanInfo[0].ExtraZeroRuns[0] = new JPEGScanInfo.ExtraZeroRunInfo { BlockIdx = 0, NumExtraZeroRuns = 5 };
            TryRoundTripJpegDataHeader(badExtraRun).ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="JPEGData.HasZeroPaddingBit"/> and <see cref="JPEGData.PaddingBits"/> serialization.
        /// </summary>
        private static void ExerciseJpegDataZeroPaddingRoundTrip()
        {
            JPEGData jpg = CreateSampleJpegData(progressive: false);

            jpg.HasZeroPaddingBit = true;
            jpg.PaddingBits.Add(1);
            jpg.PaddingBits.Add(0);
            jpg.PaddingBits.Add(1);

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            Bundle.Write(jpg, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            JPEGData outJpg = new JPEGData();
            BitReader br    = new BitReader(bw.GetSpan());

            Bundle.Read(br, outJpg).IsOk.ShouldBeTrue();
            br.Close();
            outJpg.HasZeroPaddingBit.ShouldBeTrue();
            outJpg.PaddingBits.Count.ShouldBe(3);
        }

        /// <summary>
        /// Exercises <see cref="JpegDataDecoder.DecodeJPEGData"/> error branches for short ICC/Exif/XMP/APP/COM markers
        /// and corrupted Brotli streams.
        /// </summary>
        private static void ExerciseJpegDataDecoderErrorBranches()
        {
            JPEGData badIcc = CreateSampleJpegData(progressive: false);

            badIcc.AppData[1] = new byte[10];
            DecodeFromModifiedJpegData(badIcc, corruptBrotli: false).ShouldBeFalse();

            JPEGData badExif = CreateSampleJpegData(progressive: false);

            badExif.AppData[2] = new byte[6];
            DecodeFromModifiedJpegData(badExif, corruptBrotli: false).ShouldBeFalse();

            JPEGData badXmp = CreateSampleJpegData(progressive: false);

            badXmp.AppData[3] = new byte[10];
            DecodeFromModifiedJpegData(badXmp, corruptBrotli: false).ShouldBeFalse();

            JPEGData validJpg = CreateSampleJpegData(progressive: false);

            DecodeFromModifiedJpegData(validJpg, corruptBrotli: true).ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="JpegDataWriter.WriteJpeg"/> padding bit validation, restart markers (<c>0xD0</c>),
        /// and multi-MCU DRI emission.
        /// </summary>
        private static void ExerciseJpegDataWriterPaddingAndRestartMarkers()
        {
            JPEGData jpgPadErr = CreateSampleJpegData(progressive: false);

            jpgPadErr.HasZeroPaddingBit = true;
            jpgPadErr.PaddingBits.Add(2);

            Status padStatus = JpegDataWriter.WriteJpeg(jpgPadErr, buf => (nuint)buf.Length);

            padStatus.IsOk.ShouldBeFalse();

            JPEGData jpgPadLeftover = CreateSampleJpegData(progressive: false);

            jpgPadLeftover.HasZeroPaddingBit = true;

            for (int i = 0; i < 64; i++)
            {
                jpgPadLeftover.PaddingBits.Add(1);
            }

            JpegDataWriter.WriteJpeg(jpgPadLeftover, buf => (nuint)buf.Length).IsOk.ShouldBeFalse();

            JPEGData jpgRst = CreateSampleJpegData(progressive: false);

            jpgRst.MarkerOrder.Insert(0, 0xD0);
            JpegDataWriter.WriteJpeg(jpgRst, buf => (nuint)buf.Length).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Exercises <see cref="JpegDataWriter.WriteJpeg"/> progressive AC refinement bit buffer flushing and
        /// marker serialization error branches.
        /// </summary>
        private static void ExerciseJpegDataWriterRefinementAndErrorBranches()
        {
            JPEGData prog = CreateSampleJpegData(progressive: true);

            for (int i = 1; i < 64; i++)
            {
                prog.Components[0].Coeffs[i]      = (short)((i % 3 == 0) ? 6 : 0);
                prog.Components[0].Coeffs[64 + i] = (short)((i == 40) ? -4 : 0);
            }

            JpegDataWriter.WriteJpeg(prog, buf => (nuint)buf.Length).IsOk.ShouldBeTrue();

            JPEGData shortApp = CreateSampleJpegData(progressive: false);

            shortApp.AppData.Clear();
            JpegDataWriter.WriteJpeg(shortApp, buf => (nuint)buf.Length).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Exercises <see cref="JxlToJpegDecoder"/> box handling, chunked <c>jbrd</c> input, and Exif/XMP patching.
        /// </summary>
        private static void ExerciseJxlToJpegDecoderPaths()
        {
            JxlToJpegDecoder dec = new JxlToJpegDecoder();
            byte* pNull          = null;
            nuint len            = 0;

            dec.Process(ref pNull, ref len).ShouldBe(JxlDecoderStatus.Error);

            JPEGData jpg    = CreateSampleJpegData(progressive: false);
            byte[] jbrdData = EncodeJpegDataForTest(jpg);

            fixed (byte* pJbrd = jbrdData)
            {
                dec.StartBox(boxUntilEof: false, (nuint)jbrdData.Length);

                byte* pChunk1 = pJbrd;
                nuint len1    = 4;

                dec.Process(ref pChunk1, ref len1).ShouldBe(JxlDecoderStatus.NeedMoreInput);

                byte* pChunk2 = pJbrd + 4;
                nuint len2    = (nuint)(jbrdData.Length - 4);

                dec.Process(ref pChunk2, ref len2).ShouldBe(JxlDecoderStatus.JpegReconstruction);
            }

            JxlToJpegDecoder.NumExifMarkers(jpg).ShouldBe((nuint)1);
            JxlToJpegDecoder.NumXmpMarkers(jpg).ShouldBe((nuint)1);

            JxlToJpegDecoder.ExifBoxContentSize(jpg, out nuint exifSize).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.XmlBoxContentSize(jpg, out nuint xmlSize).ShouldBe(JxlDecoderStatus.Success);

            JxlToJpegDecoder.SetExif(new byte[exifSize], jpg).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.SetXmp(new byte[xmlSize], jpg).ShouldBe(JxlDecoderStatus.Success);
            JxlToJpegDecoder.SetExif(new byte[2], jpg).ShouldBe(JxlDecoderStatus.Error);
            JxlToJpegDecoder.SetXmp(new byte[2], jpg).ShouldBe(JxlDecoderStatus.Error);

            CodecMetadata meta   = CreateDefaultVarDctMetadata(8, 8);
            using ImageBundle ib = new ImageBundle(JxlMemoryManager.Default, meta.M);

            dec.SetImageBundleJpegData(ib).IsOk.ShouldBeTrue();
        }

        // =====================================================================
        // Helper Methods: Section 6 (JxlDecoder)
        // =====================================================================

        /// <summary>
        /// Exercises <see cref="JxlDecoder"/> API methods when called before basic info or frame stages.
        /// </summary>
        private static void ExerciseJxlDecoderPreBasicInfoGuards(JxlDecoder dec)
        {
            dec.GetBasicInfo(out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.GetExtraChannelInfo(0, out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.GetExtraChannelName(0, Span<byte>.Empty).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.GetColorAsEncodedProfile(JxlColorProfileTarget.Data, out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.GetICCProfileSize(JxlColorProfileTarget.Data, out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.GetColorAsICCProfile(JxlColorProfileTarget.Data, Span<byte>.Empty).ShouldBe(JxlDecoderStatus.NeedMoreInput);

            JxlColorEncoding badEnc = new JxlColorEncoding { ColorSpace = JxlColorSpace.Unknown };

            dec.SetPreferredColorProfile(in badEnc).ShouldBe(JxlDecoderStatus.Error);
            dec.SetOutputColorProfile(badEnc, new byte[] { 1 }).ShouldBe(JxlDecoderStatus.Error);
            dec.SetOutputColorProfile(null, ReadOnlySpan<byte>.Empty).ShouldBe(JxlDecoderStatus.Error);
            dec.PreviewOutBufferSize(default, out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.SetPreviewOutBuffer(default, null, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetPreviewOutBuffer(default, null!).ShouldBe(JxlDecoderStatus.Error);
            dec.GetFrameHeader(out _).ShouldBe(JxlDecoderStatus.Error);
            dec.GetFrameName(Span<byte>.Empty).ShouldBe(JxlDecoderStatus.Error);
            dec.GetExtraChannelBlendInfo(0, out _).ShouldBe(JxlDecoderStatus.Error);
            dec.ImageOutBufferSize(default, out _).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.SetImageOutBuffer(default, null, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetImageOutBuffer(default, null!).ShouldBe(JxlDecoderStatus.Error);
            dec.SetImageOutCallback(default, null!, null).ShouldBe(JxlDecoderStatus.Error);
            dec.SetMultithreadedImageOutCallback(default, null!, null!, null, null).ShouldBe(JxlDecoderStatus.Error);
            dec.ExtraChannelBufferSize(default, out _, 0).ShouldBe(JxlDecoderStatus.NeedMoreInput);
            dec.SetExtraChannelBuffer(default, null, 0, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetExtraChannelBuffer(default, null!, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetJPEGBuffer(null, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetJPEGBuffer(null!, 0, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetBoxBuffer(null, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.SetBoxBuffer(null!, 0, 0).ShouldBe(JxlDecoderStatus.Error);
            dec.ReleaseBoxBuffer().ShouldBe((nuint)0);
            dec.GetBoxType(out _, decompressed: false).ShouldBe(JxlDecoderStatus.Error);
            dec.GetBoxSizeRaw(out _).ShouldBe(JxlDecoderStatus.Error);
            dec.GetBoxSizeContents(out _).ShouldBe(JxlDecoderStatus.Error);
            dec.GetIntendedDownsamplingRatio().ShouldBe((nuint)0);
            dec.FlushImage().ShouldBe(JxlDecoderStatus.Error);

            JxlBitDepth customDepth = new JxlBitDepth { Type = JxlBitDepthType.Custom, BitsPerSample = 10 };

            dec.SetImageOutBitDepth(in customDepth).ShouldBe(JxlDecoderStatus.Success);
        }

        /// <summary>
        /// Exercises <see cref="JxlDecoder"/> configuration guards after decoding has transitioned to <c>Started</c>.
        /// </summary>
        private static void ExerciseJxlDecoderPostStartGuards()
        {
            using JxlDecoder dec = JxlDecoder.Create(JxlMemoryManager.Default);

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.Frame));
            dec.SetInput(Sample1x1JxlCodestream).ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(Sample1x1JxlCodestream).ShouldBe(JxlDecoderStatus.Error);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);

            dec.SetParallelRunner(null, null).ShouldBe(JxlDecoderStatus.Error);
            dec.SubscribeEvents((int)JxlDecoderStatus.FullImage).ShouldBe(JxlDecoderStatus.Error);
            dec.SetKeepOrientation(true).ShouldBe(JxlDecoderStatus.Error);
            dec.SetUnpremultiplyAlpha(true).ShouldBe(JxlDecoderStatus.Success);
            dec.SetRenderSpotcolors(true).ShouldBe(JxlDecoderStatus.Error);
            dec.SetCoalescing(true).ShouldBe(JxlDecoderStatus.Error);
            dec.SetDesiredIntensityTarget(-1.0f).ShouldBe(JxlDecoderStatus.Error);
            dec.SetDesiredIntensityTarget(250.0f).ShouldBe(JxlDecoderStatus.Success);
            dec.SetDecompressBoxes(true).ShouldBe(JxlDecoderStatus.Success);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Frame);
            dec.SetUnpremultiplyAlpha(false).ShouldBe(JxlDecoderStatus.Success);
            dec.GetFrameName(Span<byte>.Empty).ShouldBe(JxlDecoderStatus.Error);
            dec.GetExtraChannelBlendInfo(0, out _).ShouldBe(JxlDecoderStatus.Error);
        }

        /// <summary>
        /// Exercises <see cref="JxlDecoder"/> metadata, extra channel, preview, and cropped/oriented frame header queries
        /// using a synthetic codestream with <see cref="Orientation.AntiTranspose"/>.
        /// </summary>
        private static void ExerciseJxlDecoderCustomMetadataAndFrameQueries()
        {
            byte[] customStream  = BuildCustomMetadataAndFrameCodestream();
            using JxlDecoder dec = JxlDecoder.Create(JxlMemoryManager.Default);

            dec.SetCoalescing(false).ShouldBe(JxlDecoderStatus.Success);
            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.Frame | JxlDecoderStatus.FullImage)).ShouldBe(JxlDecoderStatus.Success);
            dec.SetInput(customStream).ShouldBe(JxlDecoderStatus.Success);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);

            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.HaveAnimation.ShouldBeTrue();
            info.IntrinsicXSize.ShouldBe(20u);
            info.IntrinsicYSize.ShouldBe(10u);

            dec.GetExtraChannelInfo(0, out JxlExtraChannelInfo ecInfo).ShouldBe(JxlDecoderStatus.Success);
            ecInfo.NameLength.ShouldBe(5u);

            Span<byte> ecName = stackalloc byte[8];

            dec.GetExtraChannelName(0, ecName.Slice(0, 2)).ShouldBe(JxlDecoderStatus.Error);
            dec.GetExtraChannelName(0, ecName).ShouldBe(JxlDecoderStatus.Success);
            dec.GetExtraChannelName(5, ecName).ShouldBe(JxlDecoderStatus.Error);

            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Frame);
            dec.GetFrameHeader(out JxlFrameHeader fh).ShouldBe(JxlDecoderStatus.Success);
            fh.LayerInfo.HaveCrop.ShouldBeTrue();
            fh.NameLength.ShouldBe(4u);

            Span<byte> frameName = stackalloc byte[8];

            dec.GetFrameName(frameName).ShouldBe(JxlDecoderStatus.Success);
            dec.GetExtraChannelBlendInfo(0, out JxlBlendInfo blend).ShouldBe(JxlDecoderStatus.Success);
            blend.BlendMode.ShouldBe(JxlBlendMode.Add);

            JxlPixelFormat ecFmt = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.UInt8 };

            dec.ExtraChannelBufferSize(ecFmt, out nuint ecSize, 0).ShouldBe(JxlDecoderStatus.Success);
            dec.SetExtraChannelBuffer(ecFmt, new byte[ecSize], 0).ShouldBe(JxlDecoderStatus.Success);
        }

        /// <summary>
        /// Builds a synthetic JXL codestream with <see cref="Orientation.AntiTranspose"/>, intrinsic size, animation,
        /// named alpha channel, and a cropped/named <see cref="FrameHeader"/>.
        /// </summary>
        private static byte[] BuildCustomMetadataAndFrameCodestream()
        {
            CodecMetadata meta = new CodecMetadata();

            meta.Size.Set(16, 8).IsOk.ShouldBeTrue();
            meta.M.ColorEncoding.SetSRGB(ColorSpace.RGB).IsOk.ShouldBeTrue();
            meta.M.XybEncoded        = true;
            meta.M.Orientation       = (uint)Orientation.AntiTranspose;
            meta.M.HaveIntrinsicSize = true;
            meta.M.IntrinsicSize.Set(10, 20).IsOk.ShouldBeTrue();
            meta.M.HaveAnimation            = true;
            meta.M.Animation.TpsNumerator   = 100;
            meta.M.Animation.TpsDenominator = 1;
            meta.M.Animation.HaveTimecodes  = true;
            meta.M.ExtraChannelInfo.Add(new ExtraChannelInfo
            {
                Type            = ExtraChannel.Alpha,
                BitDepth        = new BitDepth { BitsPerSample = 8 },
                AlphaAssociated = true,
                Name            = "Alpha",
            });

            FrameHeader fh                 = new FrameHeader(meta);
            fh.Encoding                    = FrameEncoding.VarDCT;
            fh.Name                        = "Test";
            fh.CustomSizeOrOrigin          = true;
            fh.FrameSize                   = new FrameSize { XSize = 8, YSize = 8 };
            fh.FrameOrigin                 = new FrameOrigin { X0 = 2, Y0 = 1 };
            fh.AnimationFrame.Duration     = 10;
            fh.AnimationFrame.Timecode     = 5;
            fh.BlendingInfo.Mode           = BlendMode.Add;
            fh.ExtraChannelBlendingInfo[0] = new BlendingInfo { Mode = BlendMode.Add, Source = 1 };

            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            meta.TransformData.NonserializedXybEncoded = meta.M.XybEncoded;

            bw.Write(8, 0xFF);
            bw.Write(8, 0x0A);
            Bundle.Write(meta.Size, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            Bundle.Write(meta.M, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            Bundle.Write(meta.TransformData, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();
            Bundle.Write(fh, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();
            bw.Write(32, 0);

            return bw.GetSpan().ToArray();
        }

        /// <summary>
        /// Exercises 64-bit container box headers and <c>brob</c> boxes wrapping valid and forbidden inner types.
        /// </summary>
        private static void ExerciseContainer64BitAndBrobBoxes()
        {
            List<byte> badBrobContainer = CreateContainerHeaderBytes();

            AppendBox32(badBrobContainer, "brob", CreateFourCcAndPayload("jxlc", new byte[] { 1, 2, 3, 4 }));

            using JxlDecoder decBad = JxlDecoder.Create(JxlMemoryManager.Default);

            decBad.SubscribeEvents((int)JxlDecoderStatus.Box);
            decBad.SetInput(badBrobContainer.ToArray());
            decBad.ProcessInput().ShouldBe(JxlDecoderStatus.Error);

            List<byte> ext64Container = CreateContainerHeaderBytes();

            AppendBox64(ext64Container, "unkn", new byte[] { 0x10, 0x20, 0x30, 0x40 });
            AppendBox32(ext64Container, "jxlc", Sample1x1JxlCodestream);

            using JxlDecoder dec64 = JxlDecoder.Create(JxlMemoryManager.Default);

            dec64.SubscribeEvents((int)(JxlDecoderStatus.Box | JxlDecoderStatus.BasicInfo));
            dec64.SetInput(ext64Container.ToArray());
            dec64.ProcessInput().ShouldBe(JxlDecoderStatus.Box);
            dec64.GetBoxSizeRaw(out ulong rawSize).ShouldBe(JxlDecoderStatus.Success);
            rawSize.ShouldBe(20UL);
            dec64.GetBoxSizeContents(out ulong contentSize).ShouldBe(JxlDecoderStatus.Success);
            contentSize.ShouldBe(4UL);
            dec64.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
        }

        /// <summary>
        /// Exercises <c>jxlp</c> partial codestream boxes, out-of-order <c>jxlp</c> sequence numbers, and
        /// <see cref="JxlDecoder.SkipCurrentFrame"/> during frame progression.
        /// </summary>
        private static void ExerciseContainerJxlpAndSkipFramePaths()
        {
            BitReader hdrBr   = new BitReader(Sample1x1JxlCodestream);
            CodecMetadata tmp = new CodecMetadata();

            hdrBr.ReadFixedBits(16);
            Headers.ReadSizeHeader(hdrBr, tmp.Size).IsOk.ShouldBeTrue();
            ImageMetadataMethods.ReadImageMetadata(hdrBr, tmp.M).IsOk.ShouldBeTrue();
            tmp.TransformData.NonserializedXybEncoded = tmp.M.XybEncoded;
            Bundle.Read(hdrBr, tmp.TransformData).IsOk.ShouldBeTrue();
            hdrBr.JumpToByteBoundary().IsOk.ShouldBeTrue();

            int splitPoint = (int)(hdrBr.TotalBitsConsumed() / 8);

            hdrBr.Close();

            byte[] part0 = new byte[4 + splitPoint];
            byte[] part1 = new byte[4 + (Sample1x1JxlCodestream.Length - splitPoint)];

            ByteOrder.StoreBE32(0u, part0);
            Sample1x1JxlCodestream.AsSpan(0, splitPoint).CopyTo(part0.AsSpan(4));
            ByteOrder.StoreBE32(0x80000001u, part1);
            Sample1x1JxlCodestream.AsSpan(splitPoint).CopyTo(part1.AsSpan(4));

            List<byte> jxlpContainer = CreateContainerHeaderBytes();

            AppendBox32(jxlpContainer, "jxlp", part0);
            AppendBox32(jxlpContainer, "jxlp", part1);

            using JxlDecoder dec = JxlDecoder.Create(JxlMemoryManager.Default);

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.Frame | JxlDecoderStatus.FullImage));
            dec.SetInput(jxlpContainer.ToArray());
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.Frame);
            dec.SkipCurrentFrame().ShouldBe(JxlDecoderStatus.Success);

            List<byte> badSeqContainer = CreateContainerHeaderBytes();

            AppendBox32(badSeqContainer, "jxlp", part0);
            AppendBox32(badSeqContainer, "jxlp", part0);

            using JxlDecoder decBadSeq = JxlDecoder.Create(JxlMemoryManager.Default);

            decBadSeq.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.FullImage));
            decBadSeq.SetInput(badSeqContainer.ToArray());
            decBadSeq.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
            decBadSeq.ProcessInput().ShouldBe(JxlDecoderStatus.Error);
        }

        /// <summary>
        /// Exercises <c>jbrd</c>, <c>Exif</c>, and <c>xml </c> box pumping when <c>JpegReconstruction</c> is subscribed.
        /// </summary>
        private static void ExerciseContainerJbrdAndExifXmpReconstruction()
        {
            JPEGData jpg    = CreateSampleJpegData(progressive: false);
            byte[] jbrdData = EncodeJpegDataForTest(jpg);

            JxlToJpegDecoder.ExifBoxContentSize(jpg, out nuint exifSize);
            JxlToJpegDecoder.XmlBoxContentSize(jpg, out nuint xmlSize);

            List<byte> container = CreateContainerHeaderBytes();

            AppendBox32(container, "jbrd", jbrdData);
            AppendBox32(container, "Exif", new byte[exifSize]);
            AppendBox32(container, "xml ", new byte[xmlSize]);
            AppendBox32(container, "jxlc", Sample1x1JxlCodestream);

            byte[] jpegOut       = new byte[4096];
            using JxlDecoder dec = JxlDecoder.Create(JxlMemoryManager.Default);

            dec.SubscribeEvents((int)(JxlDecoderStatus.JpegReconstruction | JxlDecoderStatus.BasicInfo));
            dec.SetInput(container.ToArray());
            dec.ProcessInput().ShouldBe(JxlDecoderStatus.JpegReconstruction);

            fixed (byte* pJpeg = jpegOut)
            {
                dec.SetJPEGBuffer(pJpeg, (nuint)jpegOut.Length).ShouldBe(JxlDecoderStatus.Success);
                dec.ProcessInput().ShouldBe(JxlDecoderStatus.BasicInfo);
            }
        }

        // =====================================================================
        // Shared Utility Builders
        // =====================================================================

        private static readonly byte[] Sample1x1JxlCodestream =
            JxlSharp.Encoder.EncFastLossless.FastLosslessEncode(
                new byte[] { 128, 64, 32 },
                (1u, 3u, 1u, 3u, 8u)).Value;

        /// <summary>
        /// Creates a 4-channel row pointer table suitable for <see cref="RenderPipelineStage.ProcessRow"/>.
        /// </summary>
        private static float*[][] CreateRowPointers4(float* c0, float* c1, float* c2, float* c3)
        {
            return new float*[][]
            {
                new float*[] { c0 },
                new float*[] { c1 },
                new float*[] { c2 },
                new float*[] { c3 },
            };
        }

        /// <summary>
        /// Creates a default <see cref="CodecMetadata"/> with the specified dimensions.
        /// </summary>
        private static CodecMetadata CreateDefaultVarDctMetadata(nuint xsize, nuint ysize)
        {
            CodecMetadata meta = new CodecMetadata();

            meta.Size.Set(xsize, ysize).IsOk.ShouldBeTrue();
            meta.M.ColorEncoding.SetSRGB(ColorSpace.RGB).IsOk.ShouldBeTrue();
            meta.M.XybEncoded = true;

            return meta;
        }

        /// <summary>
        /// Creates a valid 3-component <see cref="JPEGData"/> instance for header and writer testing.
        /// </summary>
        private static JPEGData CreateSampleJpegData(bool progressive)
        {
            JPEGData jpg        = new JPEGData();
            jpg.Width           = 16;
            jpg.Height          = 16;
            jpg.RestartInterval = 1;

            jpg.AppData.Add(new byte[] { 0xE0, 0x00, 0x04, 0x01, 0x02 });
            jpg.AppMarkerTypes.Add(AppMarkerType.Unknown);
            jpg.AppData.Add(new byte[17 + 4]);
            jpg.AppMarkerTypes.Add(AppMarkerType.ICC);
            jpg.AppData.Add(new byte[3 + JpegConstants.KExifTag.Length + 4]);
            jpg.AppMarkerTypes.Add(AppMarkerType.Exif);
            jpg.AppData.Add(new byte[3 + JpegConstants.KXMPTag.Length + 4]);
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

            PopulateComponentsAndHuffman(jpg);
            PopulateScansAndMarkers(jpg, progressive);

            return jpg;
        }

        /// <summary>
        /// Populates 3 components and DC/AC Huffman tables on <paramref name="jpg"/>.
        /// </summary>
        private static void PopulateComponentsAndHuffman(JPEGData jpg)
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

                comp.Coeffs[0] = 8;
                comp.Coeffs[1] = 4;
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
        /// Populates sequential or progressive scan info and marker order on <paramref name="jpg"/>.
        /// </summary>
        private static void PopulateScansAndMarkers(JPEGData jpg, bool progressive)
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
                JPEGScanInfo acFirst  = new JPEGScanInfo { Ss = 1, Se = 63, Ah = 0, Al = 1, NumComponents = 1 };
                acFirst.Components[0] = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
                JPEGScanInfo acRef    = new JPEGScanInfo { Ss = 1, Se = 63, Ah = 1, Al = 0, NumComponents = 1 };
                acRef.Components[0]   = new JPEGComponentScanInfo { CompIdx = 0, DcTblIdx = 0, AcTblIdx = 0 };
                acRef.ResetPoints.Add(0);
                acRef.ExtraZeroRuns.Add(new JPEGScanInfo.ExtraZeroRunInfo { BlockIdx = 0, NumExtraZeroRuns = 1 });
                jpg.ScanInfo.Add(acFirst);
                jpg.ScanInfo.Add(acRef);
                jpg.MarkerOrder.Add(0xDA);
                jpg.MarkerOrder.Add(0xDA);
            }

            jpg.MarkerOrder.Add(0xD9);
        }

        /// <summary>
        /// Attempts to write and read <paramref name="jpg"/> via <see cref="Bundle"/> and returns whether both succeed.
        /// </summary>
        private static bool TryRoundTripJpegDataHeader(JPEGData jpg)
        {
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            if (!Bundle.Write(jpg, bw, LayerType.Header, null).IsOk)
            {
                return false;
            }

            bw.ZeroPadToByte();

            JPEGData decoded = new JPEGData();
            BitReader br     = new BitReader(bw.GetSpan());
            bool readOk      = Bundle.Read(br, decoded).IsOk;

            br.Close();

            return readOk;
        }

        /// <summary>
        /// Encodes <paramref name="jpg"/> to <c>jbrd</c> bytes and invokes <see cref="JpegDataDecoder.DecodeJPEGData"/>.
        /// </summary>
        private static bool DecodeFromModifiedJpegData(JPEGData jpg, bool corruptBrotli)
        {
            byte[] jbrd = EncodeJpegDataForTest(jpg);

            if (corruptBrotli)
            {
                jbrd[jbrd.Length - 1] ^= 0xFF;
            }

            JPEGData outJpg = new JPEGData();

            return JpegDataDecoder.DecodeJPEGData(jbrd, outJpg).IsOk;
        }

        /// <summary>
        /// Encodes <paramref name="jpg"/> header bits and Brotli stream into a <c>jbrd</c> buffer.
        /// </summary>
        private static byte[] EncodeJpegDataForTest(JPEGData jpg)
        {
            BitWriter bw = new BitWriter(JxlMemoryManager.Default);

            Bundle.Write(jpg, bw, LayerType.Header, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            List<byte> raw = new List<byte>();

            for (int i = 0; i < jpg.AppData.Count; i++)
            {
                if (jpg.AppMarkerTypes[i] == AppMarkerType.Unknown)
                {
                    raw.AddRange(jpg.AppData[i]);
                }
            }

            foreach (byte[] com in jpg.ComData)
            {
                raw.AddRange(com);
            }

            foreach (byte[] inter in jpg.InterMarkerData)
            {
                raw.AddRange(inter);
            }

            raw.AddRange(jpg.TailData);

            byte[] rawArr  = raw.ToArray();
            byte[] compArr = new byte[BrotliEncoder.GetMaxCompressedLength(rawArr.Length) + 16];

            BrotliEncoder.TryCompress(rawArr, compArr, out int written, 4, 10).ShouldBeTrue();

            ReadOnlySpan<byte> hdr = bw.GetSpan();
            byte[] res             = new byte[hdr.Length + written];

            hdr.CopyTo(res);
            compArr.AsSpan(0, written).CopyTo(res.AsSpan(hdr.Length));

            return res;
        }

        /// <summary>
        /// Creates a list initialized with the 32-byte JXL container signature (<c>JXL </c> + <c>ftyp</c>).
        /// </summary>
        private static List<byte> CreateContainerHeaderBytes()
        {
            byte[] header =
            {
                0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A,
                0x00, 0x00, 0x00, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'j', (byte)'x', (byte)'l', (byte)' ',
                0x00, 0x00, 0x00, 0x00, (byte)'j', (byte)'x', (byte)'l', (byte)' ',
            };

            return new List<byte>(header);
        }

        /// <summary>
        /// Appends a standard 32-bit ISO BMFF box to <paramref name="container"/>.
        /// </summary>
        private static void AppendBox32(List<byte> container, string type, byte[] payload)
        {
            byte[] hdr = new byte[8];

            ByteOrder.StoreBE32((uint)(8 + payload.Length), hdr);
            hdr[4] = (byte)type[0];
            hdr[5] = (byte)type[1];
            hdr[6] = (byte)type[2];
            hdr[7] = (byte)type[3];
            container.AddRange(hdr);
            container.AddRange(payload);
        }

        /// <summary>
        /// Appends a 64-bit extended-size ISO BMFF box (<c>size == 1</c>) to <paramref name="container"/>.
        /// </summary>
        private static void AppendBox64(List<byte> container, string type, byte[] payload)
        {
            byte[] hdr = new byte[16];

            ByteOrder.StoreBE32(1u, hdr);
            hdr[4] = (byte)type[0];
            hdr[5] = (byte)type[1];
            hdr[6] = (byte)type[2];
            hdr[7] = (byte)type[3];
            ByteOrder.StoreBE64((ulong)(16 + payload.Length), hdr.AsSpan(8, 8));
            container.AddRange(hdr);
            container.AddRange(payload);
        }

        /// <summary>
        /// Creates a buffer prefixed with a 4-byte ASCII box type followed by <paramref name="payload"/>.
        /// </summary>
        private static byte[] CreateFourCcAndPayload(string innerType, byte[] payload)
        {
            byte[] res = new byte[4 + payload.Length];

            res[0] = (byte)innerType[0];
            res[1] = (byte)innerType[1];
            res[2] = (byte)innerType[2];
            res[3] = (byte)innerType[3];
            payload.CopyTo(res, 4);

            return res;
        }
    }
}
