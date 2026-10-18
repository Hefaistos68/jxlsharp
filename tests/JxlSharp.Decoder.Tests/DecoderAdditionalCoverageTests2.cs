// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Additional coverage tests for <c>JxlSharp.Decoder</c> subsystems.
    /// </summary>
    [TestFixture]
    public unsafe class DecoderAdditionalCoverageTests2
    {
        // --------------------------------------------------------------------
        // Group 1: DecModular & QuantWeights Raw Quant Table
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder.IntToFloat"/> across special float bit patterns and error cases.
        /// </summary>
        [Test]
        public void ModularIntToFloatSpecialAndErrorCases()
        {
            int[] samples  = { 0, unchecked((int)0xFFFF8000), 0x7C00, 0x7E00, 0x0001 };
            float[] output = new float[5];

            fixed (int* pIn = samples)
            fixed (float* pOut = output)
            {
                Status err32 = ModularFrameDecoder.IntToFloat(pIn, pOut, 1, 32, 7);

                err32.IsOk.ShouldBeFalse();

                Status ok32 = ModularFrameDecoder.IntToFloat(pIn, pOut, 1, 32, 8);

                ok32.IsOk.ShouldBeTrue();

                Status ok16 = ModularFrameDecoder.IntToFloat(pIn, pOut, 5, 16, 5);

                ok16.IsOk.ShouldBeTrue();
                output[0].ShouldBe(0.0f);
                BitConverter.SingleToInt32Bits(output[1]).ShouldBe(unchecked((int)0x80000000));
                float.IsPositiveInfinity(output[2]).ShouldBeTrue();
                float.IsNaN(output[3]).ShouldBeTrue();
                output[4].ShouldBeGreaterThan(0.0f);

                int expNegSample = 1 << 6;
                Status errExpNeg = ModularFrameDecoder.IntToFloat(&expNegSample, pOut, 1, 16, 9);

                errExpNeg.IsOk.ShouldBeFalse();
            }
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder.DecodeQuantTable"/> and <see cref="DequantMatrices.DecodeQuantTable"/>.
        /// </summary>
        [Test]
        public void DecodeQuantTableErrorAndSuccessPaths()
        {
            byte[] zeroDenBytes    = CreateQuantTableBitstream(0x0000, 1);
            using BitReader brZero = new BitReader(zeroDenBytes);
            QuantEncoding encZero  = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW);
            Status sZero           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brZero, encZero, 0, null);

            brZero.Close().IsOk.ShouldBeTrue();
            sZero.IsOk.ShouldBeFalse();

            using BitReader brZero2 = new BitReader(zeroDenBytes);
            Status sZero2           = DequantMatrices.DecodeQuantTable(null, 2, 2, brZero2, encZero, 0);

            brZero2.Close().IsOk.ShouldBeTrue();
            sZero2.IsOk.ShouldBeFalse();

            byte[] validBytes       = CreateQuantTableBitstream(0x3C00, 4);
            using BitReader brValid = new BitReader(validBytes);
            QuantEncoding encValid  = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW);
            Status sValid           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brValid, encValid, 0, null);

            brValid.Close().IsOk.ShouldBeTrue();
            sValid.IsOk.ShouldBeTrue();
            encValid.QrawTable.ShouldNotBeNull();
            encValid.QrawTable!.Length.ShouldBe(12);
            encValid.QrawTable[0].ShouldBe(4);

            using BitReader brValid2  = new BitReader(validBytes);
            QuantEncoding encPrealloc = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW)
            {
                QrawTable = new int[4],
            };
            Status sValid2            = DequantMatrices.DecodeQuantTable(null, 2, 2, brValid2, encPrealloc, 0);

            brValid2.Close().IsOk.ShouldBeTrue();
            sValid2.IsOk.ShouldBeTrue();
            encPrealloc.QrawTable!.Length.ShouldBe(12);
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder.DecodeQuantTable"/> with non-positive values and active decoder context.
        /// </summary>
        [Test]
        public void DecodeQuantTableNonPositiveAndWithDecoderContext()
        {
            byte[] negValBytes    = CreateQuantTableBitstream(0x3C00, 0);
            using BitReader brNeg = new BitReader(negValBytes);
            QuantEncoding encNeg  = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW);
            Status sNeg           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brNeg, encNeg, 0, null);

            brNeg.Close().IsOk.ShouldBeTrue();
            sNeg.IsOk.ShouldBeFalse();

            using ModularFrameDecoder dec = new ModularFrameDecoder(null);
            FrameDimensions frameDim      = new FrameDimensions();

            dec.MemoryManager.ShouldNotBeNull();
            frameDim.Set(8, 8, 1, 0, 0, true, 1);
            dec.Init(frameDim);

            byte[] validBytes        = CreateQuantTableBitstream(0x3C00, 2);
            using BitReader brBadIdx = new BitReader(validBytes);
            QuantEncoding encCtx     = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW)
            {
                QrawTable = new int[4],
            };
            Status sBadIdx           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brBadIdx, encCtx, 99, dec);

            brBadIdx.Close().IsOk.ShouldBeTrue();
            sBadIdx.IsOk.ShouldBeFalse();

            using BitReader brMismatch = new BitReader(validBytes);
            Status sMismatch           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brMismatch, encCtx, 0, dec);

            brMismatch.Close().IsOk.ShouldBeTrue();
            sMismatch.IsOk.ShouldBeFalse();

            encCtx.QrawTable = new int[12];

            using BitReader brGoodIdx = new BitReader(validBytes);
            Status sGoodIdx           = ModularFrameDecoder.DecodeQuantTable(null, 2, 2, brGoodIdx, encCtx, 0, dec);

            brGoodIdx.Close().IsOk.ShouldBeTrue();
            sGoodIdx.IsOk.ShouldBeTrue();
            encCtx.QrawTable.Length.ShouldBe(12);
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder"/> global info bit-depth guards, zerofill group decoding, and non-inplace finalization.
        /// </summary>
        [Test]
        public void ModularFrameDecoderBitDepthZerofillAndNonInplaceFinalize()
        {
            CodecMetadata codecMeta = CreateBasicCodecMetadata(8, 8, false);

            codecMeta.M.BitDepth.BitsPerSample       = 33;
            codecMeta.M.BitDepth.FloatingPointSample = false;

            FrameHeader fhInvalidBits            = new FrameHeader(codecMeta)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            using ModularFrameDecoder decInvalid = new ModularFrameDecoder(null);

            decInvalid.Init(fhInvalidBits.ToFrameDimensions());

            byte[] globalBytes        = CreateGlobalModularInfoStreamBytes();
            using BitReader brInvalid = new BitReader(globalBytes);
            Status sInvalid           = decInvalid.DecodeGlobalInfo(brInvalid, fhInvalidBits, true);

            brInvalid.Close().IsOk.ShouldBeTrue();
            sInvalid.IsOk.ShouldBeFalse();

            CodecMetadata codecMeta32 = CreateBasicCodecMetadata(8, 8, false);

            codecMeta32.M.BitDepth.BitsPerSample       = 32;
            codecMeta32.M.BitDepth.FloatingPointSample = false;

            FrameHeader fh32Uint                = new FrameHeader(codecMeta32)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            using ModularFrameDecoder dec32Uint = new ModularFrameDecoder(null);

            dec32Uint.Init(fh32Uint.ToFrameDimensions());

            using BitReader br32Uint = new BitReader(globalBytes);
            Status s32Uint           = dec32Uint.DecodeGlobalInfo(br32Uint, fh32Uint, true);

            br32Uint.Close().IsOk.ShouldBeTrue();
            s32Uint.IsOk.ShouldBeFalse();

            codecMeta32.M.BitDepth.FloatingPointSample   = true;
            codecMeta32.M.BitDepth.ExponentBitsPerSample = 8;

            FrameHeader fh32                = new FrameHeader(codecMeta32)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            using ModularFrameDecoder dec32 = new ModularFrameDecoder(null);
            FrameDimensions fd32            = fh32.ToFrameDimensions();

            dec32.Init(fd32);

            using BitReader br32 = new BitReader(globalBytes);
            Status s32           = dec32.DecodeGlobalInfo(br32, fh32, true);

            br32.Close().IsOk.ShouldBeTrue();
            s32.IsOk.ShouldBeTrue();

            ModularFrameDecoder.DecodeGroupArgs groupArgs = new ModularFrameDecoder.DecodeGroupArgs
            {
                FrameHeader = fh32,
                Rect        = new Rect(0, 0, 8, 8),
                Reader      = null,
                MinShift    = 0,
                MaxShift    = 2,
                Stream      = ModularStreamId.ModularDC(0),
                Zerofill    = true,
            };

            dec32.DecodeGroup(in groupArgs).IsOk.ShouldBeTrue();

            using RenderPipeline pipeline     = CreateSimpleSavePipeline(8, 8, out ImagePlane<float> savedPlane);
            using PassesDecoderState decState = new PassesDecoderState(null);

            using (savedPlane)
            {
                decState.Shared.FrameDim = fd32;
                decState.RenderPipeline  = pipeline;

                Status finStatus = dec32.FinalizeDecoding(fh32, decState, null, inplace: false);

                finStatus.IsOk.ShouldBeTrue();
            }
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder.MaybeDropFullImage"/> and subsequent zerofill group decoding.
        /// </summary>
        [Test]
        public void ModularFrameDecoderDropFullImageAndZerofillGroup()
        {
            CodecMetadata codecMeta = CreateBasicCodecMetadata(8, 8, true);
            ExtraChannelInfo eci    = new ExtraChannelInfo
            {
                Type = ExtraChannel.Alpha,
            };

            eci.BitDepth.BitsPerSample = 8;
            codecMeta.M.ExtraChannelInfo.Add(eci);

            FrameHeader fh                = new FrameHeader(codecMeta)
            {
                Encoding       = FrameEncoding.VarDCT,
                ColorTransform = ColorTransform.XYB,
            };
            using ModularFrameDecoder dec = new ModularFrameDecoder(null);
            FrameDimensions fd            = fh.ToFrameDimensions();

            dec.Init(fd);

            byte[] globalBytes = CreateGlobalModularInfoStreamBytes();
            using BitReader br = new BitReader(globalBytes);
            Status sGlobal     = dec.DecodeGlobalInfo(br, fh, true);

            br.Close().IsOk.ShouldBeTrue();
            sGlobal.IsOk.ShouldBeTrue();
            dec.MaybeDropFullImage();

            using RenderPipeline pipeline     = CreateSimpleSavePipeline(8, 8, out ImagePlane<float> savedPlane);
            using PassesDecoderState decState = new PassesDecoderState(null);

            using (savedPlane)
            {
                decState.Shared.FrameDim = fd;
                decState.RenderPipeline  = pipeline;
                dec.FinalizeDecoding(fh, decState, null, inplace: true).IsOk.ShouldBeTrue();

                RenderPipelineInput input                     = pipeline.GetInputBuffers(0, 0);
                ModularFrameDecoder.DecodeGroupArgs groupArgs = new ModularFrameDecoder.DecodeGroupArgs
                {
                    FrameHeader         = fh,
                    Rect                = new Rect(0, 0, 8, 8),
                    Reader              = null,
                    MinShift            = 0,
                    MaxShift            = 2,
                    Stream              = ModularStreamId.ModularDC(0),
                    Zerofill            = true,
                    DecState            = decState,
                    RenderPipelineInput = input,
                };

                dec.DecodeGroup(in groupArgs).IsOk.ShouldBeTrue();
            }
        }

        /// <summary>
        /// Verifies <see cref="ModularFrameDecoder"/> float, high-bit-depth, and error paths in <see cref="ModularFrameDecoder.ModularImageToDecodedRect"/>.
        /// </summary>
        [Test]
        public void ModularFrameDecoderFloatHighBitDepthAndErrorRectConversion()
        {
            RunModularFinalizeTest(isGray: true, fp: true, bits: 16, expBits: 5, extraBits: 0);
            RunModularFinalizeTest(isGray: true, fp: false, bits: 24, expBits: 0, extraBits: 0);
            RunModularFinalizeTest(isGray: false, fp: false, bits: 24, expBits: 0, extraBits: 32);

            CodecMetadata meta            = CreateBasicCodecMetadata(4, 4, false);
            FrameHeader fh                = new FrameHeader(meta)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            using ModularFrameDecoder dec = new ModularFrameDecoder(null);

            dec.Init(fh.ToFrameDimensions());

            byte[] globalBytes = CreateGlobalModularInfoStreamBytes();
            using BitReader br = new BitReader(globalBytes);

            dec.DecodeGlobalInfo(br, fh, true).IsOk.ShouldBeTrue();
            br.Close().IsOk.ShouldBeTrue();

            using RenderPipeline pipeline = CreateSimpleSavePipeline(4, 4, out ImagePlane<float> savedPlane);

            using (savedPlane)
            {
                RenderPipelineInput input             = pipeline.GetInputBuffers(0, 0);
                StatusOr<ModularImage> giTransformsOr = ModularImage.Create(null, 4, 4, 8, 3);

                giTransformsOr.IsOk.ShouldBeTrue();

                using ModularImage giTransforms = giTransformsOr.Value;

                giTransforms.Transforms.Add(new Transform(TransformId.RCT));
                dec.ModularImageToDecodedRect(fh, giTransforms, null, null, input, new Rect(0, 0, 4, 4)).IsOk.ShouldBeFalse();

                StatusOr<ModularImage> giTooFewOr = ModularImage.Create(null, 4, 4, 8, 1);

                giTooFewOr.IsOk.ShouldBeTrue();

                using ModularImage giTooFew = giTooFewOr.Value;

                dec.ModularImageToDecodedRect(fh, giTooFew, null, null, input, new Rect(0, 0, 4, 4)).IsOk.ShouldBeFalse();

                StatusOr<ModularImage> giShiftOr = ModularImage.Create(null, 4, 4, 8, 3);

                giShiftOr.IsOk.ShouldBeTrue();

                using ModularImage giShift = giShiftOr.Value;

                giShift.Channels[0].HShift = 4;
                dec.ModularImageToDecodedRect(fh, giShift, null, null, input, new Rect(0, 0, 4, 4)).IsOk.ShouldBeFalse();
            }
        }

        /// <summary>
        /// Verifies <see cref="DequantMatrices.EnsureComputed"/> across raw, 1-band DCT, and invalid block size modes.
        /// </summary>
        [Test]
        public void DequantMatricesEnsureComputedRawAndErrorModes()
        {
            using DequantMatrices matrices = new DequantMatrices();
            List<QuantEncoding> encodings  = new List<QuantEncoding>();

            for (int i = 0; i < DequantMatrices.kNumQuantTables; i++)
            {
                encodings.Add(QuantEncoding.Library(0));
            }

            int[] rawWeights = new int[3 * 64];

            Array.Fill(rawWeights, 2);
            encodings[0] = QuantEncoding.RAW(rawWeights, 1);

            float[] oneBand = new float[DctQuantWeightParams.kMaxDistanceBands];

            oneBand[0]   = 100.0f;
            encodings[1] = QuantEncoding.DCT(new DctQuantWeightParams(oneBand, oneBand, oneBand, 1));
            matrices.SetEncodings(encodings);

            uint mask = (1u << (int)AcStrategyType.DCT) | (1u << (int)AcStrategyType.IDENTITY);
            Status ok = matrices.EnsureComputed(null, mask);

            ok.IsOk.ShouldBeTrue();

            encodings[0] = new QuantEncoding(QuantEncoding.QuantMode.kQuantModeRAW)
            {
                QrawTable = null,
            };
            matrices.SetEncodings(encodings);
            matrices.EnsureComputed(null, 1u << (int)AcStrategyType.DCT).IsOk.ShouldBeFalse();

            encodings[0] = QuantEncoding.Library(0);
            encodings[4] = QuantEncoding.Identity(new float[3], new float[3], new float[3]);
            matrices.SetEncodings(encodings);
            matrices.EnsureComputed(null, 1u << (int)AcStrategyType.DCT16X16).IsOk.ShouldBeFalse();
        }

        // --------------------------------------------------------------------
        // Group 2: IccCodec
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="IccCodec.PredictICC"/> and <see cref="IccCodec.UnpredictICC"/> with mAB, curv, CLUT, and gbd tags.
        /// </summary>
        [Test]
        public void IccCodecPredictAndUnpredictMabCurvClutAndGbdTags()
        {
            byte[] icc            = BuildSyntheticMabAndGbdIccProfile();
            PaddedBytes predicted = new PaddedBytes(null);
            Status predStatus     = IccCodec.PredictICC(icc, predicted);

            predStatus.IsOk.ShouldBeTrue();
            predicted.Size.ShouldBeGreaterThan((nuint)0);

            Status preambleStatus     = IccCodec.CheckPreamble(predicted, predicted.Size);
            PaddedBytes reconstructed = new PaddedBytes(null);
            Status unpredStatus       = IccCodec.UnpredictICC(predicted.Span, reconstructed);

            preambleStatus.IsOk.ShouldBeTrue();
            unpredStatus.IsOk.ShouldBeTrue();
            reconstructed.Span.ToArray().ShouldBe(icc);
        }

        /// <summary>
        /// Verifies <see cref="IccCodec"/> short profile, empty write, and invalid preamble/stream error paths.
        /// </summary>
        [Test]
        public void IccCodecShortProfileAndErrorGuards()
        {
            byte[] shortIcc = new byte[64];

            for (int i = 0; i < shortIcc.Length; i++)
            {
                shortIcc[i] = (byte)(i + 1);
            }

            PaddedBytes shortEnc = new PaddedBytes(null);
            Status shortPred     = IccCodec.PredictICC(shortIcc, shortEnc);
            PaddedBytes shortOut = new PaddedBytes(null);
            Status shortUnpred   = IccCodec.UnpredictICC(shortEnc.Span, shortOut);

            shortPred.IsOk.ShouldBeTrue();
            shortUnpred.IsOk.ShouldBeTrue();
            shortOut.Span.ToArray().ShouldBe(shortIcc);

            PaddedBytes nonEmptyOut = new PaddedBytes(null);
            PaddedBytes emptyPre    = new PaddedBytes(null);
            PaddedBytes truncPre    = new PaddedBytes(null);
            PaddedBytes hugePre     = new PaddedBytes(null);

            nonEmptyOut.PushBack(1);
            truncPre.PushBack(0x80);
            hugePre.Append(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x00 });

            IccCodec.UnpredictICC(shortEnc.Span, nonEmptyOut).IsOk.ShouldBeFalse();
            IccCodec.CheckPreamble(emptyPre, 0).IsOk.ShouldBeFalse();
            IccCodec.CheckPreamble(truncPre, 1).IsOk.ShouldBeFalse();
            IccCodec.CheckPreamble(hugePre, 6).IsOk.ShouldBeFalse();

            BitWriter bw = new BitWriter();

            IccCodec.WriteICC(ReadOnlySpan<byte>.Empty, bw, 0, null).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="IccCodec.UnpredictICC"/> command stream edge cases (Shuffle4, invalid Predict parameters, unknown command).
        /// </summary>
        [Test]
        public void IccCodecUnpredictCommandEdgeCases()
        {
            byte[] shuffle4Stream  = BuildCustomIccCommandStream(cmd: 3, extraCmdByte: 0, payloadBytes: 8);
            PaddedBytes outShuffle = new PaddedBytes(null);
            Status sShuffle        = IccCodec.UnpredictICC(shuffle4Stream, outShuffle);

            sShuffle.IsOk.ShouldBeTrue();
            outShuffle.Size.ShouldBe((nuint)(128 + 8));

            byte[] invalidWidthStream = BuildCustomIccCommandStream(cmd: 1, extraCmdByte: 0x0C, payloadBytes: 8);
            PaddedBytes outBadWidth   = new PaddedBytes(null);

            IccCodec.UnpredictICC(invalidWidthStream, outBadWidth).IsOk.ShouldBeFalse();

            byte[] invalidOrderStream = BuildCustomIccCommandStream(cmd: 1, extraCmdByte: 0x30, payloadBytes: 8);
            PaddedBytes outBadOrder   = new PaddedBytes(null);

            IccCodec.UnpredictICC(invalidOrderStream, outBadOrder).IsOk.ShouldBeFalse();

            byte[] unknownCmdStream = BuildCustomIccCommandStream(cmd: 99, extraCmdByte: 0, payloadBytes: 4);
            PaddedBytes outUnknown  = new PaddedBytes(null);

            IccCodec.UnpredictICC(unknownCmdStream, outUnknown).IsOk.ShouldBeFalse();
        }

        // --------------------------------------------------------------------
        // Group 3: FrameHeader & ImageMetadata
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="YCbCrChromaSubsampling"/> presets, custom factors, and debug formatting.
        /// </summary>
        [Test]
        public void YCbCrChromaSubsamplingPresetsAndValidation()
        {
            YCbCrChromaSubsampling cs = new YCbCrChromaSubsampling();

            cs.BundleName.ShouldBe("YCbCrChromaSubsampling");
            cs.DebugString().ShouldBe("444");

            cs.Set(new byte[] { 2, 1, 1 }, new byte[] { 2, 1, 1 }).IsOk.ShouldBeTrue();
            cs.Is420().ShouldBeTrue();
            cs.DebugString().ShouldBe("420");

            cs.Set(new byte[] { 2, 1, 1 }, new byte[] { 1, 1, 1 }).IsOk.ShouldBeTrue();
            cs.Is422().ShouldBeTrue();
            cs.DebugString().ShouldBe("422");

            cs.Set(new byte[] { 1, 1, 1 }, new byte[] { 2, 1, 1 }).IsOk.ShouldBeTrue();
            cs.Is440().ShouldBeTrue();
            cs.DebugString().ShouldBe("440");

            cs.Set(new byte[] { 2, 1, 2 }, new byte[] { 1, 1, 1 }).IsOk.ShouldBeTrue();
            cs.DebugString().ShouldBe("cs022");

            cs.Set(new byte[] { 3, 1, 1 }, new byte[] { 1, 1, 1 }).IsOk.ShouldBeFalse();
            cs.Set(new byte[] { 0, 1, 1 }, new byte[] { 1, 1, 1 }).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="BlendingInfo"/> and <see cref="Passes"/> properties, debug strings, and validation guards.
        /// </summary>
        [Test]
        public void BlendingInfoAndPassesMethodsAndValidation()
        {
            BlendingInfo bi = new BlendingInfo();

            bi.BundleName.ShouldBe("BlendingInfo");
            bi.Mode = BlendMode.Replace;
            bi.DebugString().ShouldContain("Replace");
            bi.Mode = BlendMode.Add;
            bi.DebugString().ShouldContain("Add");
            bi.Mode = BlendMode.Blend;
            bi.DebugString().ShouldContain("Blend");
            bi.Mode = BlendMode.AlphaWeightedAdd;
            bi.DebugString().ShouldContain("AlphaWeightedAdd");
            bi.Mode = BlendMode.Mul;
            bi.DebugString().ShouldContain("Mul");

            Passes passes = new Passes
            {
                NumPasses     = 3,
                NumDownsample = 2,
            };

            passes.LastPass[0]   = 0;
            passes.Downsample[0] = 4;
            passes.LastPass[1]   = 1;
            passes.Downsample[1] = 2;
            passes.Shift[0]      = 1;

            passes.BundleName.ShouldBe("Passes");
            passes.GetDownsamplingTargetForCompletedPasses(0).ShouldBe(8u);
            passes.GetDownsamplingTargetForCompletedPasses(1).ShouldBe(4u);
            passes.GetDownsamplingTargetForCompletedPasses(2).ShouldBe(2u);
            passes.GetDownsamplingTargetForCompletedPasses(3).ShouldBe(1u);

            passes.GetDownsamplingBracket(0, out int minShift, out int maxShift);
            minShift.ShouldBe(2);
            maxShift.ShouldBe(2);

            passes.GetDownsamplingBracket(1, out minShift, out maxShift);
            minShift.ShouldBe(1);
            maxShift.ShouldBe(1);

            passes.DebugString().ShouldContain("ds=");
        }

        /// <summary>
        /// Verifies <see cref="FrameHeader"/> helper methods, <see cref="FrameHeaderMethods.JpegOrder"/>, and validation guards.
        /// </summary>
        [Test]
        public void FrameHeaderMethodsAndValidationGuards()
        {
            for (int ct = 0; ct < 3; ct++)
            {
                FrameHeaderMethods.JpegOrder((ColorTransform)ct, false).Length.ShouldBe(3);
                FrameHeaderMethods.JpegOrder((ColorTransform)ct, true).Length.ShouldBe(3);
            }

            FrameHeader fhNull = new FrameHeader(null);

            fhNull.BundleName.ShouldBe("FrameHeader");
            fhNull.DefaultXSize().ShouldBe((nuint)0);
            fhNull.DefaultYSize().ShouldBe((nuint)0);

            CodecMetadata meta = CreateBasicCodecMetadata(16, 16, true);
            FrameHeader fh     = new FrameHeader(meta)
            {
                AllDefault               = false,
                IsLast                   = false,
                FrameType                = FrameType.ReferenceOnly,
                ColorTransform           = ColorTransform.XYB,
                SaveBeforeColorTransform = true,
            };

            fh.UpdateFlag(true, FrameHeader.KNoise);
            (fh.Flags & FrameHeader.KNoise).ShouldNotBe(0UL);
            fh.UpdateFlag(false, FrameHeader.KNoise);
            (fh.Flags & FrameHeader.KNoise).ShouldBe(0UL);
            fh.CanBeReferenced().ShouldBeTrue();
            fh.NeedsColorTransform().ShouldBeFalse();

            BitWriter bw = new BitWriter();

            FrameHeaderMethods.WriteFrameHeader(fh, bw, null).IsOk.ShouldBeTrue();
            bw.ZeroPadToByte();

            using BitReader br     = new BitReader(bw.GetSpan());
            FrameHeader fhDecoded  = new FrameHeader(meta);
            FrameHeader fhReadCrop = new FrameHeader(meta)
            {
                AllDefault               = false,
                FrameType                = FrameType.ReferenceOnly,
                CustomSizeOrOrigin       = true,
                SaveBeforeColorTransform = false,
            };

            FrameHeaderMethods.ReadFrameHeader(br, fhDecoded).IsOk.ShouldBeTrue();
            br.Close().IsOk.ShouldBeTrue();

            fhReadCrop.FrameSize.XSize = 8;
            fhReadCrop.FrameSize.YSize = 8;

            RoundTripFieldsStatus(fhReadCrop, new FrameHeader(meta)).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="BitDepth"/>, <see cref="ExtraChannelInfo"/>, <see cref="OpsinInverseMatrix"/>, <see cref="ToneMapping"/>, <see cref="ImageMetadata"/>, and <see cref="CodecMetadata"/>.
        /// </summary>
        [Test]
        public void ImageMetadataAndSubBundlesCoverage()
        {
            BitDepth bd = new BitDepth
            {
                FloatingPointSample   = true,
                BitsPerSample         = 16,
                ExponentBitsPerSample = 5,
            };

            bd.BundleName.ShouldBe("BitDepth");
            bd.DebugString().ShouldContain("F16.5");

            ExtraChannelInfo eci = new ExtraChannelInfo
            {
                AllDefault = false,
                Type       = ExtraChannel.SpotColor,
                DimShift   = 1,
                Name       = "Spot1",
            };

            eci.SpotColor[0] = 0.5f;
            eci.BundleName.ShouldBe("ExtraChannelInfo");

            ExtraChannelInfo clonedEci = eci.Clone();

            clonedEci.Name.ShouldBe("Spot1");
            clonedEci.DebugString().ShouldContain("Spot");
            RoundTripFieldsStatus(eci, new ExtraChannelInfo()).IsOk.ShouldBeTrue();

            ExtraChannelInfo eciDepth   = new ExtraChannelInfo { Type = ExtraChannel.Depth };
            ExtraChannelInfo eciThermal = new ExtraChannelInfo { Type = ExtraChannel.Thermal };
            ExtraChannelInfo eciBlack   = new ExtraChannelInfo { Type = ExtraChannel.Black };
            ExtraChannelInfo eciOpt     = new ExtraChannelInfo { Type = ExtraChannel.Optional };
            ExtraChannelInfo eciUnk     = new ExtraChannelInfo { Type = ExtraChannel.Unknown };

            eciDepth.DebugString().ShouldContain("Depth");
            eciThermal.DebugString().ShouldContain("Thermal");
            eciBlack.DebugString().ShouldContain("Black");
            eciOpt.DebugString().ShouldContain("Unknown");
            eciUnk.DebugString().ShouldContain("Unknown");

            OpsinInverseMatrix oim  = new OpsinInverseMatrix();
            OpsinParams opsinParams = new OpsinParams();

            oim.BundleName.ShouldBe("OpsinInverseMatrix");
            oim.ToOpsinParams(255.0f, opsinParams);
            opsinParams.OpsinBiases[3].ShouldBe(1.0f);

            ToneMapping tm          = new ToneMapping { AllDefault = false, IntensityTarget = 0.0f };
            CustomTransformData ctd = new CustomTransformData { AllDefault = false };
            ImageMetadata im        = new ImageMetadata();

            tm.BundleName.ShouldBe("ToneMapping");
            RoundTripFieldsStatus(tm, new ToneMapping()).IsOk.ShouldBeFalse();
            ctd.BundleName.ShouldBe("CustomTransformData");
            RoundTripFieldsStatus(ctd, new CustomTransformData()).IsOk.ShouldBeTrue();

            im.BundleName.ShouldBe("ImageMetadata");
            im.SetAlphaBits(8, true);
            im.GetAlphaBits().ShouldBe(8u);
            im.SetAlphaBits(0, false);
            im.GetAlphaBits().ShouldBe(0u);
            im.ExtraChannelInfo.Add(eci);
            im.NumExtraChannels = 1;
            im.HaveAnimation    = true;
            im.DebugString().ShouldContain("extra channels:");

            CodecMetadata cm = CreateBasicCodecMetadata(32, 16, true);

            cm.M.Orientation = 5;
            cm.M.HavePreview = true;
            cm.M.PreviewSize.Set(8, 4).IsOk.ShouldBeTrue();
            cm.OrientedXSize(false).ShouldBe((nuint)16);
            cm.OrientedYSize(false).ShouldBe((nuint)32);
            cm.OrientedPreviewXSize(false).ShouldBe((nuint)4);
            cm.OrientedPreviewYSize(false).ShouldBe((nuint)8);
            cm.DebugString().ShouldContain("32x16");
        }

        // --------------------------------------------------------------------
        // Group 4: ModularTransform, ModularEncoding & ModularImage
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="ModularTransformOps"/> error paths, meta-channel checks, and empty squeeze edge cases.
        /// </summary>
        [Test]
        public void ModularTransformErrorAndEmptySqueezeCases()
        {
            SqueezeParams sp             = new SqueezeParams();
            Transform tr                 = new Transform(TransformId.Invalid);
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 4, 8, 3);

            sp.BundleName.ShouldBe("SqueezeParams");
            tr.BundleName.ShouldBe("Transform");
            imgOr.IsOk.ShouldBeTrue();

            using ModularImage img = imgOr.Value;
            Transform palTr        = new Transform(TransformId.Palette) { BeginC = 10 };

            tr.MetaApply(img).IsOk.ShouldBeFalse();
            tr.Inverse(img, new WeightedHeader()).IsOk.ShouldBeFalse();
            ModularTransformOps.InvRCT(img, 0, 42, null).IsOk.ShouldBeFalse();

            img.NbMetaChannels = 1;
            ModularTransformOps.CheckEqualChannels(img, 0, 1).IsOk.ShouldBeFalse();
            ModularTransformOps.MetaPalette(img, 0, 1, 4, 0, false).IsOk.ShouldBeFalse();
            ModularTransformOps.InvPalette(img, palTr, new WeightedHeader(), null).IsOk.ShouldBeFalse();

            StatusOr<Channel> emptyResOr = Channel.Create(null, 0, 4);

            emptyResOr.IsOk.ShouldBeTrue();
            img.Channels.Add(emptyResOr.Value);
            img.NbMetaChannels = 0;

            List<SqueezeParams> sqList = new List<SqueezeParams>
            {
                new SqueezeParams
                {
                    Horizontal = true,
                    InPlace    = false,
                    BeginC     = 0,
                    NumC       = 1,
                },
            };

            ModularTransformOps.InvSqueeze(img, sqList, null).IsOk.ShouldBeTrue();

            StatusOr<Channel> emptyVResOr = Channel.Create(null, 4, 0);

            emptyVResOr.IsOk.ShouldBeTrue();
            img.Channels[img.Channels.Count - 1].Dispose();
            img.Channels[img.Channels.Count - 1] = emptyVResOr.Value;
            sqList[0].Horizontal = false;
            ModularTransformOps.InvSqueeze(img, sqList, null).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ModularImage.DebugString"/>, <see cref="ModularImage.UndoTransforms"/>, <see cref="ModularMa.ValidateTree"/>, and gradient LUT channel decompression.
        /// </summary>
        [Test]
        public void ModularImageTreeAndGradientLutCoverage()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 4, 8, 2);

            imgOr.IsOk.ShouldBeTrue();

            using ModularImage img = imgOr.Value;

            img.NbMetaChannels = 1;
            img.DebugString().ShouldContain("*");
            img.Transforms.Add(new Transform(TransformId.Invalid));
            img.UndoTransforms(new WeightedHeader(), null);
            img.Error.ShouldBeTrue();

            List<PropertyDecisionNode> invalidTree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(0, 10, 1, 2),
                PropertyDecisionNode.Split(0, 5, 3, 4),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
            };

            ModularMa.ValidateTree(invalidTree).IsOk.ShouldBeFalse();

            StatusOr<ModularImage> gradImgOr = ModularImage.Create(null, 4, 2, 8, 1);

            gradImgOr.IsOk.ShouldBeTrue();

            using ModularImage gradImg      = gradImgOr.Value;
            byte[] streamBytes              = CreateGlobalTreeGroupStreamBytes(out ANSCode code, out byte[] ctxMap);
            using BitReader br              = new BitReader(streamBytes);
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(10, 0, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Gradient),
                PropertyDecisionNode.Leaf(Predictor.Gradient),
            };
            GlobalModularContext globalCtx  = new GlobalModularContext
            {
                Tree           = tree,
                Code           = code,
                ContextMap     = ctxMap,
                UndoTransforms = true,
            };
            Status gradStatus               = ModularEncoding.ModularGenericDecompress(
                br,
                gradImg,
                null,
                0,
                ModularOptions.Default(),
                globalCtx);

            br.Close().IsOk.ShouldBeTrue();
            gradStatus.IsOk.ShouldBeTrue();
            gradImg.Channels[0].ConstRow(0)[0].ShouldBe(0);
        }

        // --------------------------------------------------------------------
        // Group 5: RenderPipeline, RenderPipelineStages & ImageBundle
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="SimpleRenderPipeline"/> and <see cref="LowMemoryRenderPipeline"/> with stages that switch to image dimensions.
        /// </summary>
        [Test]
        public void RenderPipelinesWithSwitchToImageDimensionsStage()
        {
            RunPipelineWithImageDimStage(useSimple: true, x0: -2, y0: -2, imgW: 4, imgH: 4);
            RunPipelineWithImageDimStage(useSimple: false, x0: 2, y0: 2, imgW: 16, imgH: 16);
            RunPipelineWithImageDimStage(useSimple: false, x0: 32, y0: 32, imgW: 8, imgH: 8);
        }

        /// <summary>
        /// Verifies <see cref="RenderPipelineStages.GetFastXYBTosRGB8Stage"/>, <see cref="RenderPipelineStages.GetSpotColorStage"/>, and <see cref="EpfStageHelpers.CopyUnfilteredPixel"/>.
        /// </summary>
        [Test]
        public void FastXybSpotColorAndEpfHelperStagesCoverage()
        {
            byte[] rgbBuf = new byte[4 * 8 * 2];

            fixed (byte* rgbPtr = rgbBuf)
            {
                using RenderPipelineStage? fastStage = RenderPipelineStages.GetFastXYBTosRGB8Stage(
                    rgbPtr,
                    stride: 32,
                    width: 8,
                    height: 2,
                    rgba: true,
                    hasAlpha: true,
                    alphaC: 3);

                if (fastStage != null)
                {
                    fastStage.GetName().ShouldBe("FastXYB");
                    fastStage.GetChannelMode(0).ShouldBe(RenderPipelineChannelMode.Input);
                    fastStage.GetChannelMode(3).ShouldBe(RenderPipelineChannelMode.Input);
                    fastStage.GetChannelMode(4).ShouldBe(RenderPipelineChannelMode.Ignored);

                    RunSingleRowOnStage(fastStage, numChannels: 4, xsize: 8, ypos: 0).IsOk.ShouldBeTrue();
                    RunSingleRowOnStage(fastStage, numChannels: 4, xsize: 8, ypos: 5).IsOk.ShouldBeTrue();
                }
            }

            using RenderPipelineStage spotStage = RenderPipelineStages.GetSpotColorStage(0, new float[] { 1.0f, 0.0f, 0.5f, 0.5f });

            spotStage.GetName().ShouldBe("Spot");
            spotStage.GetChannelMode(0).ShouldBe(RenderPipelineChannelMode.InPlace);
            spotStage.GetChannelMode(3).ShouldBe(RenderPipelineChannelMode.Input);
            spotStage.GetChannelMode(4).ShouldBe(RenderPipelineChannelMode.Ignored);
            RunSingleRowOnStage(spotStage, numChannels: 4, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="RenderPipelineStages"/> transfer functions (HLG, 709, Gamma), tone mapping, and CMS stages (CMYK and Gray).
        /// </summary>
        [Test]
        public void ToFromLinearToneMappingAndCmsStageCoverage()
        {
            TestTransferFunctionStages(TransferFunction.HLG, haveGamma: false);
            TestTransferFunctionStages(TransferFunction.Tf709, haveGamma: false);
            TestTransferFunctionStages(TransferFunction.Unknown, haveGamma: true);

            OutputEncodingInfo pqToPqInfo = new OutputEncodingInfo
            {
                OrigIntensityTarget    = 4000.0f,
                DesiredIntensityTarget = 1000.0f,
                Luminances             = new Vector3(0.2126f, 0.7152f, 0.0722f),
            };

            pqToPqInfo.OrigColorEncoding.Tf.TransferFunction = TransferFunction.PQ;
            pqToPqInfo.ColorEncoding.Tf.TransferFunction     = TransferFunction.PQ;

            using RenderPipelineStage? tmStage = RenderPipelineStages.GetToneMappingStage(pqToPqInfo);

            tmStage.ShouldNotBeNull();
            tmStage!.GetName().ShouldBe("ToneMapping");
            RunSingleRowOnStage(tmStage, numChannels: 3, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();

            OutputEncodingInfo cmsGrayInfo          = new OutputEncodingInfo
            {
                CmsSet                 = true,
                ColorManagementSystem  = CreateMockPassThroughCms(),
                OrigColorEncoding      = ColorEncoding.LinearSRGB(isGray: true),
                LinearColorEncoding    = ColorEncoding.LinearSRGB(isGray: true),
                ColorEncoding          = ColorEncoding.SRGB(isGray: true),
                DesiredIntensityTarget = 255.0f,
            };
            using RenderPipelineStage? cmsGrayStage = RenderPipelineStages.GetCmsStage(cmsGrayInfo, linear: true);

            cmsGrayStage.ShouldNotBeNull();
            cmsGrayStage!.SetInputSizes(new (nuint, nuint)[] { (4, 1), (4, 1), (4, 1) }).IsOk.ShouldBeTrue();
            cmsGrayStage.PrepareForThreads(1).IsOk.ShouldBeTrue();
            RunSingleRowOnStage(cmsGrayStage, numChannels: 3, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();

            ColorEncoding cmykEnc                   = CreateMockCmykColorEncoding();
            OutputEncodingInfo cmsCmykInfo          = new OutputEncodingInfo
            {
                CmsSet                 = true,
                ColorManagementSystem  = CreateMockPassThroughCms(),
                OrigColorEncoding      = cmykEnc,
                LinearColorEncoding    = cmykEnc,
                ColorEncoding          = ColorEncoding.SRGB(isGray: false),
                DesiredIntensityTarget = 255.0f,
            };
            using RenderPipelineStage? cmsCmykStage = RenderPipelineStages.GetCmsStage(cmsCmykInfo, linear: false);

            cmsCmykStage.ShouldNotBeNull();
            cmsCmykStage!.GetChannelMode(3).ShouldBe(RenderPipelineChannelMode.InPlace);
            cmsCmykStage.SetInputSizes(new (nuint, nuint)[] { (4, 1), (4, 1), (4, 1), (4, 1) }).IsOk.ShouldBeTrue();
            cmsCmykStage.PrepareForThreads(1).IsOk.ShouldBeTrue();
            RunSingleRowOnStage(cmsCmykStage, numChannels: 4, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ImageBundle"/> copy, orientation, extra channels, grayscale/CMYK transforms, and metadata verification.
        /// </summary>
        [Test]
        public void ImageBundleCopyTransformAndVerifyCoverage()
        {
            ImageMetadata meta        = new ImageMetadata();
            ExtraChannelInfo blackEci = new ExtraChannelInfo { Type = ExtraChannel.Black };

            meta.ColorEncoding = ColorEncoding.SRGB(isGray: true);
            meta.Orientation   = 6;
            meta.SetAlphaBits(8, true);
            meta.ExtraChannelInfo.Add(blackEci);

            using ImageBundle ib              = new ImageBundle(null, meta);
            StatusOr<Image3<float>> grayImgOr = Image3<float>.Create(null, 4, 2);

            ib.DetectRealBitdepth().ShouldBe(8u);
            ib.AlphaIsPremultiplied().ShouldBeTrue();
            ib.VerifyMetadata().IsOk.ShouldBeFalse();
            grayImgOr.IsOk.ShouldBeTrue();
            ib.SetFromImage(grayImgOr.Value, ColorEncoding.SRGB(isGray: true)).IsOk.ShouldBeTrue();

            StatusOr<ImagePlane<float>> alphaOr  = ImagePlane<float>.Create(null, 4, 2);
            StatusOr<ImagePlane<float>> alpha2Or = ImagePlane<float>.Create(null, 4, 2);
            StatusOr<ImagePlane<float>> blackOr  = ImagePlane<float>.Create(null, 4, 2);

            alphaOr.IsOk.ShouldBeTrue();
            alpha2Or.IsOk.ShouldBeTrue();
            blackOr.IsOk.ShouldBeTrue();
            ib.SetAlpha(alphaOr.Value).IsOk.ShouldBeTrue();
            ib.SetExtraChannels(new List<ImagePlane<float>> { alpha2Or.Value, blackOr.Value }).IsOk.ShouldBeTrue();
            ib.OrientedXSize().ShouldBe((nuint)2);
            ib.OrientedYSize().ShouldBe((nuint)4);
            ib.Alpha().ShouldNotBeNull();
            ib.Black().ShouldNotBeNull();
            ib.VerifyMetadata().IsOk.ShouldBeTrue();

            StatusOr<ImageBundle> copyOr = ib.Copy();

            copyOr.IsOk.ShouldBeTrue();

            using ImageBundle copy  = copyOr.Value;
            using ImageBundle store = new ImageBundle(null, meta);
            JxlCmsInterface cms     = CreateMockPassThroughCms();

            copy.ShrinkTo(2, 2).IsOk.ShouldBeTrue();

            Status tfStatus = ImageBundle.TransformIfNeeded(
                ib,
                ColorEncoding.LinearSRGB(isGray: true),
                cms,
                null,
                store,
                out ImageBundle transformed);

            tfStatus.IsOk.ShouldBeTrue();
            ReferenceEquals(transformed, store).ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ImageBundle.ApplyColorTransform"/> for CMYK with and without a black plane.
        /// </summary>
        [Test]
        public void ImageBundleApplyColorTransformCmykPaths()
        {
            ColorEncoding cmykEnc               = CreateMockCmykColorEncoding();
            StatusOr<Image3<float>> colorOr     = Image3<float>.Create(null, 4, 2);
            StatusOr<ImagePlane<float>> blackOr = ImagePlane<float>.Create(null, 4, 2);

            colorOr.IsOk.ShouldBeTrue();
            blackOr.IsOk.ShouldBeTrue();

            using Image3<float> color     = colorOr.Value;
            using ImagePlane<float> black = blackOr.Value;
            Image3<float> output          = new Image3<float>();

            try
            {
                JxlCmsInterface cms                  = CreateMockPassThroughCms();
                ColorTransformSource srcMissingBlack = new ColorTransformSource(
                    cmykEnc,
                    255.0f,
                    color,
                    null,
                    new Rect(0, 0, 4, 2));

                ImageBundle.ApplyColorTransform(in srcMissingBlack, ColorEncoding.SRGB(), cms, null, ref output).IsOk.ShouldBeFalse();

                ColorTransformSource srcWithBlack = new ColorTransformSource(
                    cmykEnc,
                    255.0f,
                    color,
                    black,
                    new Rect(0, 0, 4, 2));

                ImageBundle.ApplyColorTransform(in srcWithBlack, ColorEncoding.SRGB(), cms, null, ref output).IsOk.ShouldBeTrue();
                output.XSize.ShouldBe((nuint)4);
            }
            finally
            {
                output.Dispose();
            }
        }

        // --------------------------------------------------------------------
        // Group 6: DecAns, ColorEncodingInternal, Fields & LoopFilter
        // --------------------------------------------------------------------

        /// <summary>
        /// Verifies <see cref="DecAns"/> encoding helpers, LZ77 RLE-only Huffman mode, single-value advance, and checkpoint wrap-around.
        /// </summary>
        [Test]
        public void DecAnsRleOnlyCheckpointAndHistogramPaths()
        {
            BitWriter bw                   = new BitWriter();
            HybridUintConfig[] uintConfigs =
            {
                new HybridUintConfig(4, 1, 1),
                new HybridUintConfig(8, 0, 0),
            };

            DecAns.StoreVarLenUint8(0, bw);
            DecAns.StoreVarLenUint8(12, bw);
            DecAns.StoreVarLenUint16(0, bw);
            DecAns.StoreVarLenUint16(300, bw);
            DecAns.EncodeUintConfigs(uintConfigs, bw, 8);
            bw.ZeroPadToByte();

            using BitReader br                    = new BitReader(bw.GetSpan());
            Span<HybridUintConfig> decodedConfigs = stackalloc HybridUintConfig[2];

            DecAns.DecodeVarLenUint8(br).ShouldBe(0);
            DecAns.DecodeVarLenUint8(br).ShouldBe(12);
            DecAns.DecodeVarLenUint16(br).ShouldBe(0);
            DecAns.DecodeVarLenUint16(br).ShouldBe(300);
            DecAns.DecodeUintConfigs(8, decodedConfigs, br).IsOk.ShouldBeTrue();
            br.Close().IsOk.ShouldBeTrue();

            byte[] ansBytes                    = CreateAnsLz77SingleSymbolStream(out ANSCode ansCode);
            using BitReader brAns              = new BitReader(ansBytes);
            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(ansCode, brAns);

            readerOr.IsOk.ShouldBeTrue();

            ANSSymbolReader reader        = readerOr.Value;
            bool singleOk                 = reader.IsSingleValueAndAdvance(0, out uint symVal, ANSSymbolReader.KWindowSize - 8);
            ANSSymbolReader.Checkpoint cp = new ANSSymbolReader.Checkpoint();

            reader.UsesLZ77().ShouldBeTrue();
            reader.IsHuffRleOnly().ShouldBeFalse();
            singleOk.ShouldBeTrue();
            symVal.ShouldBe(0u);
            reader.Save(cp);
            reader.Restore(cp);
            brAns.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="ANSSymbolReader.IsHuffRleOnly"/>, <see cref="ANSSymbolReader.ReadHybridUintClusteredHuffRleOnly"/>, and flat/general ANS histograms.
        /// </summary>
        [Test]
        public void DecAnsHuffRleOnlyAndFlatHistogramCoverage()
        {
            ANSCode huffCode = new ANSCode
            {
                UsePrefixCode = true,
                LogAlphaSize  = 15,
                UintConfig    = new HybridUintConfig[]
                {
                    new HybridUintConfig(0, 0, 0),
                    new HybridUintConfig(0, 0, 0),
                },
                HuffmanData = new HuffmanDecodingData[]
                {
                    new HuffmanDecodingData(),
                    new HuffmanDecodingData(),
                },
            };
            int tableLen     = 1 << HuffmanDecodingData.KHuffmanTableBits;

            huffCode.Lz77.Enabled                      = true;
            huffCode.Lz77.MinSymbol                    = 4;
            huffCode.Lz77.MinLength                    = 3;
            huffCode.Lz77.LengthUintConfig             = new HybridUintConfig(0, 0, 0);
            huffCode.Lz77.NonserializedDistanceContext = 1;
            huffCode.HuffmanData[0].Table              = new HuffmanCode[tableLen];
            huffCode.HuffmanData[1].Table              = new HuffmanCode[tableLen];

            for (int i = 0; i < tableLen; i++)
            {
                huffCode.HuffmanData[0].Table[i] = new HuffmanCode { Bits = 0, Value = 2 };
                huffCode.HuffmanData[1].Table[i] = new HuffmanCode { Bits = 0, Value = 1 };
            }

            byte[] dummyBytes                  = new byte[16];
            using BitReader br                 = new BitReader(dummyBytes);
            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(huffCode, br);

            readerOr.IsOk.ShouldBeTrue();

            ANSSymbolReader reader = readerOr.Value;
            uint val               = 0;
            uint run               = 0;

            reader.IsHuffRleOnly().ShouldBeTrue();
            reader.ReadHybridUintClusteredHuffRleOnly(0, br, ref val, ref run);
            val.ShouldBe(2u);

            for (int i = 0; i < tableLen; i++)
            {
                huffCode.HuffmanData[0].Table[i] = new HuffmanCode { Bits = 0, Value = 4 };
            }

            reader.ReadHybridUintClusteredHuffRleOnly(0, br, ref val, ref run);
            run.ShouldBe(2u);

            byte[] ctxMap = { 0 };

            reader.ReadHybridUintInlined(true, 0, br, ctxMap);
            reader.ReadHybridUint(0, br, ctxMap);
            br.Close().IsOk.ShouldBeTrue();
            TestFlatAndDuplicateSimpleAnsHistograms();
        }

        /// <summary>
        /// Verifies <see cref="ColorEncoding"/>, <see cref="CustomxyBundle"/>, <see cref="CustomTransferFunctionBundle"/>, <see cref="U64Coder"/>, and <see cref="LoopFilter"/>.
        /// </summary>
        [Test]
        public void ColorEncodingFieldsAndLoopFilterCoverage()
        {
            CustomxyBundle xyBundle               = new CustomxyBundle();
            CustomTransferFunctionBundle tfBundle = new CustomTransferFunctionBundle
            {
                NonserializedColorSpace = ColorSpace.XYB,
            };
            ColorEncoding ce                      = new ColorEncoding();

            xyBundle.BundleName.ShouldBe("Customxy");
            tfBundle.BundleName.ShouldBe("CustomTransferFunction");
            tfBundle.SetImplicit().ShouldBeTrue();

            ce.BundleName.ShouldBe("ColorEncoding");
            ce.SetColorSpace(ColorSpace.XYB);
            ce.ImplicitWhitePoint().ShouldBeTrue();
            ce.SetSRGB(ColorSpace.XYB).IsOk.ShouldBeFalse();
            ce.SetSRGB(ColorSpace.RGB).IsOk.ShouldBeTrue();
            ce.SetWhitePointType(WhitePoint.E).IsOk.ShouldBeTrue();
            ce.SetPrimariesType(Primaries.P3).IsOk.ShouldBeTrue();
            ce.DecideIfWantICC(JxlCms.GetDefaultCms());
            ce.ToString().ShouldNotBeEmpty();

            ulong largeVal = 0xF000_0000_0000_0000UL;
            BitWriter bw64 = new BitWriter();

            U64Coder.MaxEncodedBits().ShouldBe((nuint)73);
            U64Coder.CanEncode(largeVal, out nuint encBits).IsOk.ShouldBeTrue();
            encBits.ShouldBeGreaterThan((nuint)60);
            U64Coder.Write(largeVal, bw64).IsOk.ShouldBeTrue();
            bw64.ZeroPadToByte();

            using BitReader br64 = new BitReader(bw64.GetSpan());
            LoopFilter lf        = new LoopFilter
            {
                AllDefault  = false,
                Gab         = true,
                GabCustom   = true,
                GabXWeight1 = -0.125f,
                GabXWeight2 = -0.125f,
            };
            LoopFilter lfEpf     = new LoopFilter
            {
                AllDefault             = false,
                Gab                    = false,
                EpfIters               = 2,
                EpfSharpCustom         = true,
                EpfWeightCustom        = true,
                EpfSigmaCustom         = true,
                NonserializedIsModular = true,
                EpfSigmaForModular     = 0.0f,
            };

            U64Coder.Read(br64).ShouldBe(largeVal);
            br64.Close().IsOk.ShouldBeTrue();
            lf.BundleName.ShouldBe("LoopFilter");
            RoundTripFieldsStatus(lf, new LoopFilter()).IsOk.ShouldBeFalse();
            RoundTripFieldsStatus(lfEpf, new LoopFilter { NonserializedIsModular = true }).IsOk.ShouldBeFalse();
        }

        // --------------------------------------------------------------------
        // Private Helper Methods
        // --------------------------------------------------------------------

        /// <summary>
        /// Creates a raw quant table bitstream with a 16-bit float denominator and constant modular value.
        /// </summary>
        private static byte[] CreateQuantTableBitstream(ushort f16DenBits, int constantValue)
        {
            BitWriter bw = new BitWriter();

            bw.Write(16, f16DenBits);
            WriteMinimalModularStreamWithConstantValue(bw, constantValue);
            bw.ZeroPadToByte();

            return bw.GetSpan().ToArray();
        }

        /// <summary>
        /// Creates a minimal global modular info stream.
        /// </summary>
        private static byte[] CreateGlobalModularInfoStreamBytes()
        {
            BitWriter bw = new BitWriter();

            bw.Write(1, 0);
            WriteMinimalModularStreamWithConstantValue(bw, 0);
            bw.ZeroPadToByte();

            return bw.GetSpan().ToArray();
        }

        /// <summary>
        /// Writes a minimal modular sub-bitstream with a constant channel value.
        /// </summary>
        private static void WriteMinimalModularStreamWithConstantValue(BitWriter bw, int constantValue)
        {
            bw.Write(1, 0);
            bw.Write(1, 1);
            bw.Write(2, 0);

            bw.Write(1, 0);
            bw.Write(1, 1);
            bw.Write(2, 0);
            bw.Write(1, 1);
            DecAns.EncodeUintConfig(new HybridUintConfig(0, 0, 0), bw, 15);
            DecAns.StoreVarLenUint16(0, bw);

            bw.Write(1, 0);
            bw.Write(1, 0);
            bw.Write(2, 0);
            DecAns.EncodeUintConfig(new HybridUintConfig(4, 0, 0), bw, 5);
            bw.Write(1, 1);
            bw.Write(1, 0);
            DecAns.StoreVarLenUint8(PackSigned.Pack(constantValue), bw);
            bw.Write(32, AnsParams.AnsSignature << 16);
        }

        /// <summary>
        /// Creates a modular group bitstream that uses a global tree and a single-zero histogram.
        /// </summary>
        private static byte[] CreateGlobalTreeGroupStreamBytes(out ANSCode code, out byte[] contextMap)
        {
            BitWriter histBw = new BitWriter();

            histBw.Write(1, 0);
            histBw.Write(1, 1);
            histBw.Write(2, 0);
            histBw.Write(1, 1);
            DecAns.EncodeUintConfig(new HybridUintConfig(0, 0, 0), histBw, 15);
            DecAns.StoreVarLenUint16(0, histBw);
            histBw.ZeroPadToByte();

            using BitReader brHist = new BitReader(histBw.GetSpan());
            BitWriter groupBw      = new BitWriter();

            code = new ANSCode();
            DecAns.DecodeHistograms(null, brHist, 2, code, out contextMap).IsOk.ShouldBeTrue();
            brHist.Close().IsOk.ShouldBeTrue();

            groupBw.Write(1, 1);
            groupBw.Write(1, 1);
            groupBw.Write(2, 0);
            groupBw.Write(64, 0);
            groupBw.ZeroPadToByte();

            return groupBw.GetSpan().ToArray();
        }

        /// <summary>
        /// Creates an ANS LZ77 single-symbol stream.
        /// </summary>
        private static byte[] CreateAnsLz77SingleSymbolStream(out ANSCode code)
        {
            BitWriter bw = new BitWriter();

            bw.Write(1, 1);
            bw.Write(2, 0);
            bw.Write(2, 0);
            DecAns.EncodeUintConfig(new HybridUintConfig(0, 0, 0), bw, 8);

            bw.Write(1, 1);
            bw.Write(2, 0);
            bw.Write(1, 0);
            bw.Write(2, 0);
            DecAns.EncodeUintConfig(new HybridUintConfig(0, 0, 0), bw, 5);

            bw.Write(1, 1);
            bw.Write(1, 0);
            DecAns.StoreVarLenUint8(0, bw);
            bw.ZeroPadToByte();

            using BitReader br = new BitReader(bw.GetSpan());
            BitWriter stateBw  = new BitWriter();

            code = new ANSCode();
            DecAns.DecodeHistograms(null, br, 1, code, out _).IsOk.ShouldBeTrue();
            br.Close().IsOk.ShouldBeTrue();
            stateBw.Write(32, AnsParams.AnsSignature << 16);
            stateBw.ZeroPadToByte();

            return stateBw.GetSpan().ToArray();
        }

        /// <summary>
        /// Exercises flat and duplicate simple ANS histogram decoding paths.
        /// </summary>
        private static void TestFlatAndDuplicateSimpleAnsHistograms()
        {
            BitWriter bwFlat = new BitWriter();
            ANSCode flatCode = new ANSCode
            {
                UsePrefixCode = false,
                LogAlphaSize  = 5,
                UintConfig    = new HybridUintConfig[]
                {
                    new HybridUintConfig(0, 0, 0),
                },
            };

            bwFlat.Write(1, 0);
            bwFlat.Write(1, 1);
            DecAns.StoreVarLenUint8(3, bwFlat);
            bwFlat.ZeroPadToByte();

            using BitReader brFlat = new BitReader(bwFlat.GetSpan());
            BitWriter bwDup        = new BitWriter();
            ANSCode dupCode        = new ANSCode
            {
                UsePrefixCode = false,
                LogAlphaSize  = 5,
                UintConfig    = new HybridUintConfig[]
                {
                    new HybridUintConfig(0, 0, 0),
                },
            };

            DecAns.DecodeANSCodes(null, 1, 32, brFlat, flatCode).IsOk.ShouldBeTrue();
            brFlat.Close().IsOk.ShouldBeTrue();

            bwDup.Write(1, 1);
            bwDup.Write(1, 1);
            DecAns.StoreVarLenUint8(2, bwDup);
            DecAns.StoreVarLenUint8(2, bwDup);
            bwDup.ZeroPadToByte();

            using BitReader brDup = new BitReader(bwDup.GetSpan());

            DecAns.DecodeANSCodes(null, 1, 32, brDup, dupCode).IsOk.ShouldBeFalse();
            brDup.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Creates a basic <see cref="CodecMetadata"/> instance for testing.
        /// </summary>
        private static CodecMetadata CreateBasicCodecMetadata(uint width, uint height, bool xybEncoded)
        {
            CodecMetadata meta = new CodecMetadata();

            meta.Size.Set(width, height).IsOk.ShouldBeTrue();
            meta.M.XybEncoded             = xybEncoded;
            meta.M.BitDepth.BitsPerSample = 8;

            return meta;
        }

        /// <summary>
        /// Creates a simple render pipeline that saves channel 0 into an <see cref="ImagePlane{T}"/>.
        /// </summary>
        private static RenderPipeline CreateSimpleSavePipeline(nuint width, nuint height, out ImagePlane<float> plane)
        {
            StatusOr<ImagePlane<float>> planeOr = ImagePlane<float>.Create(null, width, height);
            FrameDimensions fd                  = new FrameDimensions();
            RenderPipeline.Builder builder      = new RenderPipeline.Builder(null, 4);

            planeOr.IsOk.ShouldBeTrue();
            plane = planeOr.Value;
            fd.Set(width, height, 1, 0, 0, false, 1);
            builder.UseSimpleImplementation();
            builder.AddStage(new CopyChannelZeroStage(plane));

            StatusOr<RenderPipeline> pipeOr = builder.Finalize(fd);

            pipeOr.IsOk.ShouldBeTrue();

            RenderPipeline pipe = pipeOr.Value!;

            pipe.PrepareForThreads(1, false).IsOk.ShouldBeTrue();

            return pipe;
        }

        /// <summary>
        /// Exercises <see cref="ModularFrameDecoder.FinalizeDecoding"/> for given bit-depth and sample configurations.
        /// </summary>
        private static void RunModularFinalizeTest(bool isGray, bool fp, uint bits, uint expBits, uint extraBits)
        {
            CodecMetadata meta = CreateBasicCodecMetadata(4, 4, false);

            meta.M.ColorEncoding                  = ColorEncoding.SRGB(isGray);
            meta.M.BitDepth.FloatingPointSample   = fp;
            meta.M.BitDepth.BitsPerSample         = bits;
            meta.M.BitDepth.ExponentBitsPerSample = expBits;

            if (extraBits > 0)
            {
                ExtraChannelInfo eci = new ExtraChannelInfo
                {
                    Type = ExtraChannel.Alpha,
                };

                eci.BitDepth.BitsPerSample       = extraBits;
                eci.BitDepth.FloatingPointSample = false;
                meta.M.ExtraChannelInfo.Add(eci);
            }

            FrameHeader fh                = new FrameHeader(meta)
            {
                Encoding       = FrameEncoding.Modular,
                ColorTransform = ColorTransform.None,
            };
            using ModularFrameDecoder dec = new ModularFrameDecoder(null);
            FrameDimensions fd            = fh.ToFrameDimensions();
            byte[] globalBytes            = CreateGlobalModularInfoStreamBytes();
            using BitReader br            = new BitReader(globalBytes);

            dec.Init(fd);
            dec.DecodeGlobalInfo(br, fh, true).IsOk.ShouldBeTrue();
            br.Close().IsOk.ShouldBeTrue();

            ModularFrameDecoder.DecodeGroupArgs groupArgs = new ModularFrameDecoder.DecodeGroupArgs
            {
                FrameHeader = fh,
                Rect        = new Rect(0, 0, 4, 4),
                Reader      = null,
                MinShift    = 0,
                MaxShift    = 2,
                Stream      = ModularStreamId.ModularDC(0),
                Zerofill    = true,
            };
            using RenderPipeline pipeline                 = CreateSimpleSavePipeline(4, 4, out ImagePlane<float> savedPlane);
            using PassesDecoderState decState             = new PassesDecoderState(null);

            dec.DecodeGroup(in groupArgs).IsOk.ShouldBeTrue();

            using (savedPlane)
            {
                decState.Shared.FrameDim = fd;
                decState.RenderPipeline  = pipeline;
                dec.FinalizeDecoding(fh, decState, null, inplace: true).IsOk.ShouldBeTrue();
            }
        }

        /// <summary>
        /// Builds a synthetic ICC profile containing mAB (with curv and CLUT) and gbd tags.
        /// </summary>
        private static byte[] BuildSyntheticMabAndGbdIccProfile()
        {
            const int headerSize = 128;
            const int tagCount   = 2;
            const int dirSize    = 4 + (tagCount * 12);
            const int mabOffset  = headerSize + dirSize;
            const int mabSize    = 256;
            const int gbdOffset  = mabOffset + mabSize;
            const int gbdSize    = 32;
            const int totalSize  = gbdOffset + gbdSize;
            const int t0         = headerSize + 4;
            const int t1         = t0 + 12;
            const int curvPos    = mabOffset + 32;
            const int clutPos    = mabOffset + 64;
            byte[] icc           = new byte[totalSize];

            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(0, 4), (uint)totalSize);
            WriteTag4(icc, 4, "lcms");
            WriteTag4(icc, 12, "mntr");
            WriteTag4(icc, 16, "RGB ");
            WriteTag4(icc, 20, "XYZ ");
            WriteTag4(icc, 36, "acsp");
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(headerSize, 4), tagCount);

            WriteTag4(icc, t0, "A2B0");
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(t0 + 4, 4), (uint)mabOffset);
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(t0 + 8, 4), (uint)mabSize);

            WriteTag4(icc, t1, "gbd0");
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(t1 + 4, 4), (uint)gbdOffset);
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(t1 + 8, 4), (uint)gbdSize);

            WriteTag4(icc, mabOffset, "mAB ");
            icc[mabOffset + 8] = 3;
            icc[mabOffset + 9] = 3;
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(mabOffset + 12, 4), 32u);
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(mabOffset + 24, 4), 64u);

            WriteTag4(icc, curvPos, "curv");
            BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(curvPos + 8, 4), 10u);

            for (int i = 0; i < 10; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(icc.AsSpan(curvPos + 12 + (i * 2), 2), (ushort)(i * 1000));
            }

            icc[clutPos + 0]  = 3;
            icc[clutPos + 1]  = 3;
            icc[clutPos + 2]  = 3;
            icc[clutPos + 16] = 2;

            for (int i = 0; i < 81; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(icc.AsSpan(clutPos + 20 + (i * 2), 2), (ushort)(i * 200));
            }

            WriteTag4(icc, gbdOffset, "gbd ");
            BinaryPrimitives.WriteUInt16BigEndian(icc.AsSpan(gbdOffset + 8, 2), 3);
            BinaryPrimitives.WriteUInt16BigEndian(icc.AsSpan(gbdOffset + 10, 2), 3);

            return icc;
        }

        /// <summary>
        /// Builds a custom ICC predicted stream with a single command.
        /// </summary>
        private static byte[] BuildCustomIccCommandStream(byte cmd, byte extraCmdByte, int payloadBytes)
        {
            List<byte> cmds = new List<byte>();
            List<byte> enc  = new List<byte>();
            int osize       = 128 + payloadBytes;

            cmds.Add(0);
            cmds.Add(cmd);

            if (cmd == 1)
            {
                cmds.Add(extraCmdByte);
            }

            AppendVarInt(cmds, (ulong)payloadBytes);
            AppendVarInt(enc, (ulong)osize);
            AppendVarInt(enc, (ulong)cmds.Count);
            enc.AddRange(cmds);

            for (int i = 0; i < 128 + payloadBytes; i++)
            {
                enc.Add((byte)(i & 0xFF));
            }

            return enc.ToArray();
        }

        /// <summary>
        /// Appends a base-128 varint to the destination byte list.
        /// </summary>
        private static void AppendVarInt(List<byte> dst, ulong value)
        {
            while (value > 127)
            {
                dst.Add((byte)((value & 127) | 128));
                value >>= 7;
            }

            dst.Add((byte)(value & 127));
        }

        /// <summary>
        /// Writes a 4-character ASCII tag into a byte buffer.
        /// </summary>
        private static void WriteTag4(byte[] buf, int offset, string tag)
        {
            for (int i = 0; i < 4; i++)
            {
                buf[offset + i] = (byte)tag[i];
            }
        }

        /// <summary>
        /// Writes a <see cref="Fields"/> bundle to bits and reads it back into another instance.
        /// </summary>
        private static Status RoundTripFieldsStatus(Fields writeBundle, Fields readBundle)
        {
            BitWriter bw = new BitWriter();
            Status ws    = Bundle.Write(writeBundle, bw, 0, null);

            if (!ws.IsOk)
            {
                return ws;
            }

            bw.ZeroPadToByte();

            using BitReader br = new BitReader(bw.GetSpan());
            Status rs          = Bundle.Read(br, readBundle);

            br.Close().IsOk.ShouldBeTrue();

            return rs;
        }

        /// <summary>
        /// Runs a render pipeline containing a stage that switches to image dimensions.
        /// </summary>
        private static void RunPipelineWithImageDimStage(bool useSimple, int x0, int y0, nuint imgW, nuint imgH)
        {
            FrameDimensions fd             = new FrameDimensions();
            RenderPipeline.Builder builder = new RenderPipeline.Builder(null, 3);

            fd.Set(8, 8, 1, 0, 0, false, 1);

            if (useSimple)
            {
                builder.UseSimpleImplementation();
            }

            builder.AddStage(new MockSwitchDimensionsStage(imgW, imgH, new FrameOrigin { X0 = x0, Y0 = y0 }));
            builder.AddStage(new NoOpInPlaceStage());

            StatusOr<RenderPipeline> pipeOr = builder.Finalize(fd);

            pipeOr.IsOk.ShouldBeTrue();

            using RenderPipeline pipe = pipeOr.Value!;

            pipe.PrepareForThreads(1, false).IsOk.ShouldBeTrue();

            RenderPipelineInput input = pipe.GetInputBuffers(0, 0);

            input.Done().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Exercises both <see cref="RenderPipelineStages.GetToLinearStage"/> and <see cref="RenderPipelineStages.GetFromLinearStage"/>.
        /// </summary>
        private static void TestTransferFunctionStages(TransferFunction tf, bool haveGamma)
        {
            OutputEncodingInfo info = new OutputEncodingInfo
            {
                OrigIntensityTarget    = 1000.0f,
                DesiredIntensityTarget = 1000.0f,
                Luminances             = new Vector3(0.2126f, 0.7152f, 0.0722f),
                InverseGamma           = 0.45f,
            };

            info.ColorEncoding.Tf.TransferFunction = tf;
            info.ColorEncoding.Tf.HaveGamma        = haveGamma;

            using RenderPipelineStage toLin    = RenderPipelineStages.GetToLinearStage(info);
            using RenderPipelineStage? fromLin = RenderPipelineStages.GetFromLinearStage(info);

            toLin.IsInitialized().IsOk.ShouldBeTrue();
            RunSingleRowOnStage(toLin, numChannels: 3, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();
            fromLin.ShouldNotBeNull();
            RunSingleRowOnStage(fromLin!, numChannels: 3, xsize: 4, ypos: 0).IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Executes a single row on a <see cref="RenderPipelineStage"/> with allocated row buffers.
        /// </summary>
        private static Status RunSingleRowOnStage(RenderPipelineStage stage, int numChannels, nuint xsize, nuint ypos)
        {
            int offset         = RenderPipelineStage.KRenderPipelineXOffset;
            int rowLen         = (int)xsize + (offset * 2) + 16;
            float[] backing    = new float[numChannels * rowLen];
            float*[][] inRows  = new float*[numChannels][];
            float*[][] outRows = new float*[numChannels][];

            fixed (float* basePtr = backing)
            {
                for (int c = 0; c < numChannels; c++)
                {
                    float* chPtr = basePtr + (c * rowLen);

                    for (int x = 0; x < rowLen; x++)
                    {
                        chPtr[x] = 0.25f;
                    }

                    inRows[c]  = new float*[] { chPtr };
                    outRows[c] = new float*[] { chPtr };
                }

                EpfStageHelpers.CopyUnfilteredPixel(inRows, outRows, 0);

                return stage.ProcessRow(inRows, outRows, (0, 0), xsize, 0, ypos, 0);
            }
        }

        /// <summary>
        /// Creates a mock CMYK <see cref="ColorEncoding"/> via <see cref="ColorEncoding.SetICC"/>.
        /// </summary>
        private static ColorEncoding CreateMockCmykColorEncoding()
        {
            ColorEncoding cmykEnc = new ColorEncoding();
            JxlCmsInterface cms   = CreateMockPassThroughCms();

            cmykEnc.SetICC(new byte[] { 1, 2, 3, 4 }, cms).IsOk.ShouldBeTrue();

            return cmykEnc;
        }

        /// <summary>
        /// Creates a mock pass-through <see cref="JxlCmsInterface"/> for testing CMS stages and transforms.
        /// </summary>
        private static JxlCmsInterface CreateMockPassThroughCms()
        {
            return new JxlCmsInterface
            {
                SetFieldsFromIcc = (object? _, ReadOnlySpan<byte> _, ref JxlColorEncoding c, out bool cmyk) =>
                {
                    c.ColorSpace       = JxlColorSpace.Rgb;
                    c.WhitePoint       = JxlWhitePoint.D65;
                    c.Primaries        = JxlPrimaries.Srgb;
                    c.TransferFunction = JxlTransferFunction.Srgb;
                    c.RenderingIntent  = JxlRenderingIntent.Relative;
                    cmyk               = true;

                    return true;
                },
                Init = (_, _, xsize, _, _, _) => new float[2][]
                {
                    new float[(int)xsize * 4],
                    new float[(int)xsize * 4],
                },
                GetSrcBuf = (data, _) => ((float[][])data!)[0],
                GetDstBuf = (data, _) => ((float[][])data!)[1],
                Run       = (_, _, src, dst, _) =>
                {
                    int len = Math.Min(src.Length, dst.Length);

                    src.Slice(0, len).CopyTo(dst.Slice(0, len));

                    return true;
                },
                Destroy = _ => { },
            };
        }

        /// <summary>
        /// Minimal stage that copies channel 0 into a destination <see cref="ImagePlane{T}"/>.
        /// </summary>
        private sealed class CopyChannelZeroStage : RenderPipelineStage
        {
            private readonly ImagePlane<float> _dst;

            /// <summary>
            /// Initializes a new instance of the <see cref="CopyChannelZeroStage"/> class.
            /// </summary>
            public CopyChannelZeroStage(ImagePlane<float> dst)
                : base(new Settings())
            {
                _dst = dst;
            }

            /// <inheritdoc />
            public override string GetName() => "CopyCh0";

            /// <inheritdoc />
            public override RenderPipelineChannelMode GetChannelMode(nuint c)
            {
                return c == 0 ? RenderPipelineChannelMode.Input : RenderPipelineChannelMode.Ignored;
            }

            /// <inheritdoc />
            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                if (ypos >= _dst.YSize)
                {
                    return true;
                }

                float* src = GetInputRow(inputRows, 0, 0);
                float* dst = _dst.Row(ypos) + xpos;
                nuint span = Math.Min(xsize, _dst.XSize - xpos);

                for (nuint x = 0; x < span; x++)
                {
                    dst[x] = src[x];
                }

                return true;
            }
        }

        /// <summary>
        /// Mock stage that switches pipeline execution to oriented image dimensions.
        /// </summary>
        private sealed class MockSwitchDimensionsStage : RenderPipelineStage
        {
            private readonly nuint _imgX;
            private readonly nuint _imgY;
            private readonly FrameOrigin _origin;

            /// <summary>
            /// Initializes a new instance of the <see cref="MockSwitchDimensionsStage"/> class.
            /// </summary>
            public MockSwitchDimensionsStage(nuint imgX, nuint imgY, FrameOrigin origin)
                : base(new Settings())
            {
                _imgX   = imgX;
                _imgY   = imgY;
                _origin = origin;
            }

            /// <inheritdoc />
            public override string GetName() => "MockSwitchDim";

            /// <inheritdoc />
            public override RenderPipelineChannelMode GetChannelMode(nuint c) => RenderPipelineChannelMode.InPlace;

            /// <inheritdoc />
            public override bool SwitchToImageDimensions() => true;

            /// <inheritdoc />
            public override void GetImageDimensions(out nuint xsize, out nuint ysize, out FrameOrigin frameOrigin)
            {
                xsize       = _imgX;
                ysize       = _imgY;
                frameOrigin = _origin;
            }

            /// <inheritdoc />
            public override void ProcessPaddingRow(float*[][] outputRows, nuint xsize, nuint xpos, nuint ypos)
            {
                float* row0 = GetInputRow(outputRows, 0, 0);

                for (nuint x = 0; x < xsize; x++)
                {
                    row0[x] = 0.5f;
                }
            }

            /// <inheritdoc />
            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                return true;
            }
        }

        /// <summary>
        /// Minimal no-op in-place stage used to follow an image-dimension switching stage.
        /// </summary>
        private sealed class NoOpInPlaceStage : RenderPipelineStage
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NoOpInPlaceStage"/> class.
            /// </summary>
            public NoOpInPlaceStage()
                : base(new Settings())
            {
            }

            /// <inheritdoc />
            public override string GetName() => "NoOpInPlace";

            /// <inheritdoc />
            public override RenderPipelineChannelMode GetChannelMode(nuint c) => RenderPipelineChannelMode.InPlace;

            /// <inheritdoc />
            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                return true;
            }
        }

        /// <summary>
        /// Verifies uncovered paths in <see cref="BitReader"/>, <see cref="BitReaderScopedCloser"/>,
        /// <see cref="BitWriter"/>, <see cref="ColorEncoding"/>, <see cref="ColorSpaceTransform"/>,
        /// <see cref="ImageBundle"/>, <see cref="ModularStreamId"/>, and <see cref="ModularMa"/>.
        /// </summary>
        [Test]
        public void BitReaderBitWriterColorAndModularAdditionalCoverage()
        {
            ExerciseBitReaderAndScopedCloser();
            ExerciseBitWriterAllotmentAndAlignment();
            ExerciseColorEncodingAndTransformGuards();
            ExerciseImageBundleGuardsAndMetadata();
            ExerciseModularStreamIdAndGradientLut();
        }

        private static void ExerciseBitReaderAndScopedCloser()
        {
            using BitReader uninit = new BitReader();

            uninit.Close().IsOk.ShouldBeTrue();

            using BitReader nullArr = new BitReader(null!, 0, 0);

            nullArr.TotalBytes().ShouldBe((nuint)0);
            nullArr.Close().IsOk.ShouldBeTrue();

            byte[] sample          = { 0x11, 0x22, 0x33, 0x44 };
            using BitReader sliced = new BitReader(sample, 1, 2);

            sliced.GetSpan().Length.ShouldBe(2);
            sliced.Reset(new byte[] { 0xAA, 0xBB });
            sliced.GetSpan().Length.ShouldBe(2);
            sliced.SkipBits(500);
            sliced.MarkUnhealthy();

            BitReaderScopedCloser closer = new BitReaderScopedCloser(sliced);
            Status closeStatus           = Status.OkStatus;

            closer.CloseStatus.IsOk.ShouldBeTrue();
            closer.Close(ref closeStatus);
            closeStatus.IsOk.ShouldBeFalse();
            closer.CloseStatus.IsOk.ShouldBeFalse();

            BitReader dispReader = new BitReader(sample);

            using (BitReaderScopedCloser dispCloser = new BitReaderScopedCloser(dispReader))
            {
                dispCloser.CloseStatus.IsOk.ShouldBeTrue();
            }
        }

        private static void ExerciseBitWriterAllotmentAndAlignment()
        {
            BitWriter bw = new BitWriter();

            bw.Write(8, 0x5A);

            using PaddedBytes taken = bw.TakeBytes();

            taken.Size.ShouldBe((nuint)1);
            bw.Shrink().IsOk.ShouldBeTrue();
            bw.AppendByteAligned(ReadOnlySpan<byte>.Empty).IsOk.ShouldBeTrue();
            bw.AppendByteAligned(Array.Empty<BitWriter>()).IsOk.ShouldBeTrue();

            bw.Write(3, 0x05);
            bw.AppendByteAligned(new byte[] { 1, 2 }).IsOk.ShouldBeFalse();

            BitWriter alignedOther = new BitWriter();

            alignedOther.Write(8, 0x33);
            bw.AppendByteAligned(new BitWriter[] { alignedOther }).IsOk.ShouldBeFalse();

            BitWriter alignedDest = new BitWriter();

            alignedDest.AppendByteAligned(new BitWriter[] { bw }).IsOk.ShouldBeFalse();

            AuxOut aux       = new AuxOut();
            BitWriter nested = new BitWriter();

            nested.WithMaxBits(
                64,
                LayerType.Header,
                aux,
                () => nested.WithMaxBits(
                    32,
                    LayerType.Header,
                    aux,
                    () =>
                    {
                        nested.Write(8, 0x7F);

                        return true;
                    },
                    finishedHistogram: true),
                finishedHistogram: true).IsOk.ShouldBeTrue();
        }

        private static void ExerciseColorEncodingAndTransformGuards()
        {
            ColorEncoding ce = ColorEncoding.SRGB();

            ce.View.ShouldNotBeNull();

            ColorEncoding clone = new ColorEncoding();

            clone.CopyFrom(ce);
            clone.DecideIfWantICC(JxlCms.GetDefaultCms());

            ColorEncoding emptyIcc = new ColorEncoding();

            emptyIcc.SetICCRaw(Array.Empty<byte>());
            emptyIcc.DecideIfWantICC(JxlCms.GetDefaultCms());
            emptyIcc.IsSRGB().ShouldBeFalse();
            emptyIcc.IsLinearSRGB().ShouldBeFalse();
            emptyIcc.SetWhitePointType(WhitePoint.D65).IsOk.ShouldBeFalse();
            emptyIcc.SetPrimariesType(Primaries.SRGB).IsOk.ShouldBeFalse();

            ColorEncoding xyb = ColorEncoding.SRGB();

            xyb.SetColorSpace(ColorSpace.XYB);
            xyb.IsSRGB().ShouldBeFalse();
            xyb.IsLinearSRGB().ShouldBeFalse();
            xyb.SetPrimariesType(Primaries.SRGB).IsOk.ShouldBeFalse();

            CustomTransferFunctionBundle tfb = new CustomTransferFunctionBundle
            {
                NonserializedColorSpace = ColorSpace.XYB,
            };
            BitWriter bw = new BitWriter();

            Bundle.Write(tfb, bw, LayerType.Header, null).IsOk.ShouldBeTrue();

            CustomTransferFunctionBundle badGamma = new CustomTransferFunctionBundle();

            badGamma.Storage.HaveGamma = true;
            badGamma.Storage.Gamma     = 0;
            Bundle.Write(badGamma, bw, LayerType.Header, null).IsOk.ShouldBeFalse();

            ColorEncoding cmykDst = ColorEncoding.SRGB();

            cmykDst.Storage.Cmyk = true;

            using ColorSpaceTransform cst = new ColorSpaceTransform(new JxlCmsInterface());

            cst.Init(ce, cmykDst, 255.0f, 4, 1).IsOk.ShouldBeFalse();
            cst.Init(ce, ce, 255.0f, 4, 1).IsOk.ShouldBeFalse();
            cst.Run(0, ReadOnlySpan<float>.Empty, Span<float>.Empty, 0).IsOk.ShouldBeFalse();
        }

        private static void ExerciseImageBundleGuardsAndMetadata()
        {
            ImageMetadata meta = new ImageMetadata();

            meta.ColorEncoding = ColorEncoding.SRGB(isGray: false);

            using ImageBundle ibNoMeta = new ImageBundle(null);

            ibNoMeta.OrientedXSize().ShouldBe((nuint)0);
            ibNoMeta.OrientedYSize().ShouldBe((nuint)0);
            ibNoMeta.Alpha().ShouldBeNull();

            using ImagePlane<float> dummyPlane = ImagePlane<float>.Create(null, 4, 4).Value;

            ibNoMeta.SetAlpha(dummyPlane).IsOk.ShouldBeFalse();

            using ImageBundle ib = new ImageBundle(null, meta);

            ib.SetAlpha(dummyPlane).IsOk.ShouldBeFalse();
            ib.OverrideProfile(ColorEncoding.SRGB(isGray: false));

            using Image3<float> grayImg = Image3<float>.Create(null, 4, 4).Value;

            ib.SetFromImage(grayImg, ColorEncoding.SRGB(isGray: true)).IsOk.ShouldBeFalse();
            ib.SetFromImage(grayImg, ColorEncoding.SRGB(isGray: false)).IsOk.ShouldBeTrue();
            ib.SetExtraChannels(new List<ImagePlane<float>> { new ImagePlane<float>() }).IsOk.ShouldBeFalse();

            meta.ExtraChannelInfo.Add(new ExtraChannelInfo
            {
                Type     = ExtraChannel.Alpha,
                BitDepth = new BitDepth { BitsPerSample = 8 },
            });
            ib.SetAlpha(dummyPlane).IsOk.ShouldBeTrue();
            ib.SetAlpha(dummyPlane).IsOk.ShouldBeFalse();

            Image3<float> outImg     = new Image3<float>();
            ColorTransformSource src = new ColorTransformSource(
                ColorEncoding.SRGB(isGray: false),
                255.0f,
                grayImg,
                null,
                new Rect(0, 0, 4, 4));

            ImageBundle.ApplyColorTransform(
                in src,
                ColorEncoding.SRGB(isGray: true),
                JxlCms.GetDefaultCms(),
                null,
                ref outImg).IsOk.ShouldBeFalse();
        }

        private static void ExerciseModularStreamIdAndGradientLut()
        {
            GroupHeader gh = new GroupHeader();

            gh.BundleName.ShouldBe("GroupHeader");
            ModularMa.ValidateTree(Array.Empty<PropertyDecisionNode>()).IsOk.ShouldBeTrue();

            FrameDimensions fd = new FrameDimensions();

            fd.Set(16, 16, 1, 0, 0, false, 1);
            ModularStreamId.Global().DebugString().ShouldContain("ModularGlobal");
            ModularStreamId.VarDCTDC(0).DebugString().ShouldContain("VarDCTDC");
            ModularStreamId.ModularDC(0).DebugString().ShouldContain("ModularDC");
            ModularStreamId.ACMetadata(0).DebugString().ShouldContain("ACMeta");
            ModularStreamId.QuantTable(0).Value.DebugString().ShouldContain("QuantTable");
            ModularStreamId.ModularAC(0, 0).DebugString().ShouldContain("ModularAC");

            ModularStreamId unknownKind = new ModularStreamId { Kind = (ModularStreamId.StreamKind)99 };

            unknownKind.ID(fd).ShouldBe((nuint)0);
            unknownKind.DebugString().ShouldBeEmpty();
        }
    }
}

