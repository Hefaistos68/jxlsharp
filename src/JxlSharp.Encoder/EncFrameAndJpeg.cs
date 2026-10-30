// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using ThreadPool = JxlSharp.Base.ThreadPool;

namespace JxlSharp.Encoder
{
    public sealed partial class PassesEncoderState
    {
        /// <summary>Initial adaptive quantization field computed during heuristics.</summary>
        public ImagePlane<float> InitialQuantField = new ImagePlane<float>();

        /// <summary>Initial 8x8 adaptive quantization masking field.</summary>
        public ImagePlane<float> InitialQuantMasking = new ImagePlane<float>();

        /// <summary>AC strategy selection heuristics instance.</summary>
        public readonly AcStrategyHeuristics Heuristics = new AcStrategyHeuristics();
    }

    /// <summary>
    /// Initializes per-frame encoder state, computes VarDCT AC metadata, and tokenizes AC groups.
    /// Ported from <c>lib/jxl/enc_cache.h</c> and <c>lib/jxl/enc_cache.cc</c>.
    /// </summary>
    public static class EncCache
    {
        /// <summary>
        /// Packs the VarDCT AC metadata (CfL correlation map, AC strategy, raw quant field, EPF sharpness)
        /// for all DC groups into <paramref name="modularFrameEncoder"/>.
        /// Ported from <c>jxl::ComputeACMetadata</c> in <c>lib/jxl/enc_cache.cc</c>.
        /// </summary>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="encState">Passes encoder state.</param>
        /// <param name="modularFrameEncoder">Modular frame encoder.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ComputeACMetadata(
            ThreadPool? pool,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder)
        {
            PassesSharedState shared = encState.Shared;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)shared.FrameDim.NumDcGroups,
                ThreadPool.NoInit,
                (groupIndex, _) =>
                {
                    nuint dcGroupIdx  = encState.StreamingMode ? encState.DcGroupIndex : groupIndex;
                    Rect blockDcRect  = shared.FrameDim.DCGroupRect(groupIndex);

                    return modularFrameEncoder.AddACMetadata(
                        blockDcRect,
                        dcGroupIdx,
                        jpegTranscode: false,
                        encState);
                },
                "ComputeACMetadata");
        }

        /// <summary>
        /// Initializes per-frame VarDCT passes encoder state: computes DC from LLF, runs VarDCT DC and AC metadata
        /// Modular preparation, quantizes AC groups, computes coefficient orders, and tokenizes AC coefficients.
        /// Ported from <c>jxl::InitializePassesEncoder</c> in <c>lib/jxl/enc_cache.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="opsin">XYB opsin image (or pixel-domain image before DCT).</param>
        /// <param name="rect">Block-aligned pixel rectangle within <paramref name="opsin"/>.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="passesEncState">Passes encoder state to populate.</param>
        /// <param name="context">Modular frame encoder and optional auxiliary statistics sink.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InitializePassesEncoder(
            FrameHeader frameHeader,
            Image3<float> opsin,
            Rect rect,
            JxlCmsInterface cms,
            ThreadPool? pool,
            PassesEncoderState passesEncState,
            (ModularFrameEncoder ModularFrameEncoder, AuxOut? AuxOut) context)
        {
            _ = cms;
            PassesSharedState shared                = passesEncState.Shared;
            JxlMemoryManager mm                     = passesEncState.MemoryManager ?? JxlMemoryManager.Default;
            ModularFrameEncoder modularFrameEncoder = context.ModularFrameEncoder;

            passesEncState.XQmMultiplier = MathF.Pow(1.25f, (float)frameHeader.XQmScale - 2.0f);
            passesEncState.BQmMultiplier = MathF.Pow(1.25f, (float)frameHeader.BQmScale - 2.0f);

            Status coeffAllocOk = AllocatePassCoeffImages(frameHeader, passesEncState, mm);

            if (!coeffAllocOk)
            {
                return coeffAllocOk;
            }

            StatusOr<Image3<float>> dcOr = Image3<float>.Create(mm, shared.FrameDim.XSizeBlocks, shared.FrameDim.YSizeBlocks);

            if (!dcOr.IsOk)
            {
                return dcOr.Status;
            }

            using Image3<float> dc = dcOr.Value;
            Status dcOk            = ComputeVarDctDcAndMetadata(frameHeader, opsin, rect, dc, pool, passesEncState, modularFrameEncoder);

            if (!dcOk)
            {
                return dcOk;
            }

            Status acOk = ComputeAllGroupAcCoefficients(opsin, dc, pool, passesEncState);

            if (!acOk)
            {
                return acOk;
            }

            return BuildOrdersAndTokenizePasses(frameHeader, pool, passesEncState, context.AuxOut);
        }

        private static Status AllocatePassCoeffImages(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            JxlMemoryManager mm)
        {
            PassesSharedState shared = encState.Shared;
            nuint numPasses          = encState.ProgressiveSplitter.GetNumPasses();
            nuint groupElements      = FrameDimensions.KGroupDim * FrameDimensions.KGroupDim;

            while ((nuint)encState.Coeffs.Count > numPasses)
            {
                int last = encState.Coeffs.Count - 1;
                encState.Coeffs[last].Dispose();
                encState.Coeffs.RemoveAt(last);
            }

            while ((nuint)encState.Coeffs.Count < numPasses)
            {
                if (frameHeader.Encoding == FrameEncoding.VarDCT)
                {
                    StatusOr<ACImageT<int>> acOr = ACImageT<int>.Make(mm, groupElements, shared.FrameDim.NumGroups);

                    if (!acOr.IsOk)
                    {
                        return acOr.Status;
                    }

                    encState.Coeffs.Add(acOr.Value);
                }
                else
                {
                    encState.Coeffs.Add(new ACImageT<int>());
                }
            }

            return Status.OkStatus;
        }

        private static Status ComputeVarDctDcAndMetadata(
            FrameHeader frameHeader,
            Image3<float> opsin,
            Rect rect,
            Image3<float> dc,
            ThreadPool? pool,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder)
        {
            if (frameHeader.Encoding != FrameEncoding.VarDCT)
            {
                return Status.OkStatus;
            }

            PassesSharedState shared = encState.Shared;
            JxlMemoryManager mm      = encState.MemoryManager ?? JxlMemoryManager.Default;

            Status llfOk = ExtractDcFromOpsinBlocks(opsin, rect, dc, pool, shared, mm);

            if (!llfOk)
            {
                return llfOk;
            }

            bool nlDc    = encState.CParams.SpeedTier <= SpeedTier.kKitten && shared.FrameDim.NumDcGroups == 1;
            Status addDc = DataParallel.RunOnPool(
                pool,
                0,
                (uint)shared.FrameDim.NumDcGroups,
                ThreadPool.NoInit,
                (groupIndex, _) =>
                {
                    nuint dcGroupIdx = encState.StreamingMode ? encState.DcGroupIndex : groupIndex;

                    return modularFrameEncoder.AddVarDCTDC(
                        frameHeader,
                        dc,
                        shared.FrameDim.DCGroupRect(groupIndex),
                        dcGroupIdx,
                        nlDc,
                        encState,
                        jpegTranscode: false);
                },
                "AddVarDCTDC");

            if (!addDc)
            {
                return addDc;
            }

            if ((frameHeader.Flags & FrameHeader.KSkipAdaptiveDCSmoothing) == 0)
            {
                Status smoothOk = CompressedDc.AdaptiveDCSmoothing(mm, shared.Quantizer.MulDC(), shared.DcStorage, pool);

                if (!smoothOk)
                {
                    return smoothOk;
                }
            }

            return ComputeACMetadata(pool, encState, modularFrameEncoder);
        }

        private static Status ExtractDcFromOpsinBlocks(
            Image3<float> opsin,
            Rect rect,
            Image3<float> dc,
            ThreadPool? pool,
            PassesSharedState shared,
            JxlMemoryManager mm)
        {
            float[]? scratchMem = null;
            int maxArea = MaxTransformArea(shared.AcStrategy,
                new Rect(0, 0, (int)shared.FrameDim.XSizeBlocks, (int)shared.FrameDim.YSizeBlocks));

            Status runOk = DataParallel.RunOnPool(
                pool,
                0,
                (uint)shared.FrameDim.YSizeBlocks,
                numThreads =>
                {
                    scratchMem = new float[checked((int)numThreads * 5 * maxArea)];

                    return true;
                },
                (by, threadId) =>
                {
                    int threadOffset        = (int)threadId * 5 * maxArea;
                    Span<float> coeffBlock  = scratchMem.AsSpan(threadOffset, maxArea);
                    Span<float> scratchSpan = scratchMem.AsSpan(threadOffset + maxArea, 4 * maxArea);
                    ExtractDcBlockRow(opsin, rect, dc, shared, by, coeffBlock, scratchSpan);

                    return Status.OkStatus;
                },
                "Compute DC");

            _ = mm;

            return runOk;
        }

        // Scan the actual map: public encoder stages may be called before UsedAcs is updated.
        internal static int MaxTransformArea(AcStrategyImage strategies, Rect blockRect)
        {
            nuint maxArea = FrameDimensions.kDCTBlockSize;
            for (nuint y = 0; y < (nuint)blockRect.YSize; y++)
            {
                AcStrategyRow row = strategies.ConstRow(blockRect, y);
                for (nuint x = 0; x < (nuint)blockRect.XSize; x++)
                {
                    AcStrategy acs = row[x];
                    if (!acs.IsFirstBlock()) continue;
                    nuint area = acs.CoveredBlocksX() * acs.CoveredBlocksY() * FrameDimensions.kDCTBlockSize;
                    maxArea = Math.Max(maxArea, area);
                }
            }
            return (int)maxArea;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ExtractDcBlockRow(
            Image3<float> opsin,
            Rect rect,
            Image3<float> dc,
            PassesSharedState shared,
            uint by,
            Span<float> coeffBlock,
            Span<float> scratchSpace)
        {
            nuint pixelsPerRow  = (nuint)opsin.PixelsPerRow;
            nuint dcStride      = (nuint)dc.PixelsPerRow;
            AcStrategyRow acRow = shared.AcStrategy.ConstRow(by);

            for (nuint c = 0; c < 3; c++)
            {
                ReadOnlySpan<float> opsinRow = rect.ConstRow(opsin.Plane(c), (nuint)by * FrameDimensions.kBlockDim);
                Span<float> dcRow            = dc.PlaneRow(c, by);

                for (nuint bx = 0; bx < shared.FrameDim.XSizeBlocks; bx++)
                {
                    AcStrategy acs = acRow[bx];

                    if (!acs.IsFirstBlock())
                    {
                        continue;
                    }

                    EncTransforms.TransformFromPixels(
                        acs.Strategy(),
                        opsinRow.Slice((int)(bx * FrameDimensions.kBlockDim)),
                        pixelsPerRow,
                        coeffBlock,
                        scratchSpace);
                    EncTransforms.DCFromLowestFrequencies(
                        acs.Strategy(),
                        coeffBlock,
                        dcRow.Slice((int)bx),
                        dcStride,
                        scratchSpace);
                }
            }
        }

        private static Status ComputeAllGroupAcCoefficients(
            Image3<float> opsin,
            Image3<float> dc,
            ThreadPool? pool,
            PassesEncoderState encState)
        {
            PassesSharedState shared = encState.Shared;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)shared.FrameDim.NumGroups,
                ThreadPool.NoInit,
                (groupIdx, _) => EncGroup.ComputeCoefficients(groupIdx, encState, opsin, dc),
                "ComputeCoefficients");
        }

        /// <summary>
        /// Computes AC coefficient scan orders, tokenizes AC groups across all passes, and builds ANS histograms.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="encState">Passes encoder state.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status BuildOrdersAndTokenizePasses(
            FrameHeader frameHeader,
            ThreadPool? pool,
            PassesEncoderState encState,
            AuxOut? auxOut)
        {
            PassesSharedState shared = encState.Shared;
            JxlMemoryManager mm      = encState.MemoryManager ?? JxlMemoryManager.Default;
            nuint numPasses          = encState.ProgressiveSplitter.GetNumPasses();

            while ((nuint)encState.Passes.Count < numPasses)
            {
                encState.Passes.Add(new PassesEncoderState.PassData());
            }

            while ((nuint)encState.Passes.Count > numPasses)
            {
                encState.Passes.RemoveAt(encState.Passes.Count - 1);
            }

            if (encState.UsedOrders.Count < (int)numPasses)
            {
                while (encState.UsedOrders.Count < (int)numPasses)
                {
                    encState.UsedOrders.Add(0);
                }
            }

            Status ordersOk = ComputePassCoeffOrders(frameHeader, encState, numPasses);

            if (!ordersOk)
            {
                return ordersOk;
            }

            return TokenizeAndBuildPassHistograms(frameHeader, pool, encState, mm, numPasses, auxOut);
        }

        private static Status ComputePassCoeffOrders(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            nuint numPasses)
        {
            PassesSharedState shared = encState.Shared;

            if (!encState.InitializeGlobalState)
            {
                return Status.OkStatus;
            }

            Rect frameBlockRect = new Rect(0, 0, (int)shared.FrameDim.XSizeBlocks, (int)shared.FrameDim.YSizeBlocks);
            ushort[] order16    = ArrayPool<ushort>.Shared.Rent((int)CoeffOrder.kCoeffOrderMaxSize);

            try
            {
                for (nuint pass = 0; pass < numPasses; pass++)
                {
                    Span<uint> passOrders    = shared.CoeffOrders.AsSpan((int)(pass * shared.CoeffOrderSize));
                    Span<ushort> order16Span = order16.AsSpan(0, (int)CoeffOrder.kCoeffOrderMaxSize);

                    if (frameHeader.Encoding == FrameEncoding.VarDCT)
                    {
                        StatusOr<uint> usedOr = EncCoeffOrder.ComputeCoeffOrder(
                            encState.CParams.SpeedTier,
                            encState.Coeffs[(int)pass],
                            shared.AcStrategy,
                            shared.FrameDim,
                            out uint usedOrdersMask,
                            encState.UsedAcs,
                            order16Span);

                        if (!usedOr.IsOk)
                        {
                            return usedOr.Status;
                        }

                        encState.UsedOrders[(int)pass] = usedOrdersMask;

                        for (int i = 0; i < order16Span.Length && i < (int)shared.CoeffOrderSize; i++)
                        {
                            passOrders[i] = order16Span[i];
                        }
                    }
                    else
                    {
                        encState.UsedOrders[(int)pass] = 0;
                    }

                    _ = frameBlockRect;
                }
            }
            finally
            {
                ArrayPool<ushort>.Shared.Return(order16);
            }

            return Status.OkStatus;
        }

        private static Status TokenizeAndBuildPassHistograms(
            FrameHeader frameHeader,
            ThreadPool? pool,
            PassesEncoderState encState,
            JxlMemoryManager mm,
            nuint numPasses,
            AuxOut? auxOut)
        {
            PassesSharedState shared = encState.Shared;
            nuint numGroups          = shared.FrameDim.NumGroups;

            if (frameHeader.Encoding != FrameEncoding.VarDCT)
            {
                shared.NumHistograms = 1;

                return Status.OkStatus;
            }

            for (nuint pass = 0; pass < numPasses; pass++)
            {
                List<List<Token>> acTokens = encState.Passes[(int)pass].AcTokens;

                while ((nuint)acTokens.Count < numGroups)
                {
                    acTokens.Add(new List<Token>(4096));
                }

                for (nuint g = 0; g < numGroups; g++)
                {
                    acTokens[(int)g].Clear();
                }
            }

            List<Image3<int>> nzeroScratch = new List<Image3<int>>();

            Status tokOk = DataParallel.RunOnPool(
                pool,
                0,
                (uint)numGroups,
                numThreads => AllocateTokenizeScratch(mm, numThreads, nzeroScratch),
                (groupIdx, threadId) =>
                {
                    TokenizeGroupAcrossPasses(
                        frameHeader,
                        encState,
                        numPasses,
                        groupIdx,
                        nzeroScratch[(int)threadId]);

                    return Status.OkStatus;
                },
                "TokenizeCoefficients");

            foreach (Image3<int> img in nzeroScratch)
            {
                img.Dispose();
            }

            if (!tokOk)
            {
                return tokOk;
            }

            return EncodePassHistograms(encState, mm, numPasses, numGroups, auxOut);
        }

        private static Status AllocateTokenizeScratch(
            JxlMemoryManager mm,
            nuint numThreads,
            List<Image3<int>> nzeroScratch)
        {
            for (nuint i = 0; i < numThreads; i++)
            {
                StatusOr<Image3<int>> imgOr = Image3<int>.Create(
                    mm,
                    FrameDimensions.KGroupDimInBlocks,
                    FrameDimensions.KGroupDimInBlocks);

                if (!imgOr.IsOk)
                {
                    return imgOr.Status;
                }

                nzeroScratch.Add(imgOr.Value);
            }

            for (nuint p = 0; p < (nuint)nzeroScratch.Count; p++)
            {
                _ = p;
            }

            return Status.OkStatus;
        }

        private static void TokenizeGroupAcrossPasses(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            nuint numPasses,
            uint groupIdx,
            Image3<int> tmpNumNzeroes)
        {
            PassesSharedState shared = encState.Shared;
            Rect blockGroupRect      = shared.FrameDim.BlockGroupRect(groupIdx);
            ACPtr[] acRows           = new ACPtr[3];

            for (nuint pass = 0; pass < numPasses; pass++)
            {
                ReadOnlySpan<uint> passOrders = shared.CoeffOrders.AsSpan((int)(pass * shared.CoeffOrderSize));
                ACImage coeffImg              = encState.Coeffs[(int)pass];

                for (nuint c = 0; c < 3; c++)
                {
                    acRows[c] = coeffImg.PlaneRow(c, groupIdx, 0);
                }

                EncEntropyCoder.TokenizeCoefficients(
                    passOrders,
                    blockGroupRect,
                    acRows,
                    shared.AcStrategy,
                    frameHeader.ChromaSubsampling,
                    tmpNumNzeroes,
                    (encState.Passes[(int)pass].AcTokens[(int)groupIdx], shared.QuantDc, shared.RawQuantField, shared.BlockCtxMap));
            }
        }

        private static Status EncodePassHistograms(
            PassesEncoderState encState,
            JxlMemoryManager mm,
            nuint numPasses,
            nuint numGroups,
            AuxOut? auxOut)
        {
            PassesSharedState shared = encState.Shared;
            encState.HistogramIdx.Clear();

            for (nuint g = 0; g < numGroups; g++)
            {
                encState.HistogramIdx.Add(0);
            }

            shared.NumHistograms = 1;

            if (!encState.InitializeGlobalState)
            {
                return Status.OkStatus;
            }

            nuint numContexts = shared.BlockCtxMap.NumACContexts();

            for (nuint pass = 0; pass < numPasses; pass++)
            {
                PassesEncoderState.PassData passData = encState.Passes[(int)pass];
                HistogramParams histParams           = new HistogramParams(encState.CParams.SpeedTier, numContexts)
                {
                    StreamingMode     = encState.StreamingMode,
                    AddMissingSymbols = encState.StreamingMode,
                };

                BitWriter histWriter     = new BitWriter(mm);
                StatusOr<nuint> buildRes = EncAns.BuildAndEncodeHistograms(
                    mm,
                    histParams,
                    numContexts,
                    passData.AcTokens,
                    passData.Codes,
                    histWriter,
                    (LayerType.Ac, auxOut));

                if (!buildRes.IsOk)
                {
                    return buildRes.Status;
                }

                passData.ContextMap = passData.Codes.ContextMap;
                passData.Codes.EncodedHistograms.Clear();
                passData.Codes.EncodedHistograms.Add(histWriter);
            }

            return Status.OkStatus;
        }
    }

    /// <summary>
    /// Quantizes VarDCT AC coefficient groups, applies error diffusion / roundtrip Y dequantization,
    /// and writes tokenized group AC bitstreams.
    /// Ported from <c>lib/jxl/enc_group.h</c> and <c>lib/jxl/enc_group.cc</c>.
    /// </summary>
    public static class EncGroup
    {
        /// <summary>
        /// Arguments for <see cref="QuantizeBlockAC"/> and <see cref="QuantizeRoundtripYBlockAC"/>.
        /// </summary>
        public struct QuantBlockArgs
        {
            /// <summary>Quantizer instance.</summary>
            public required Quantizer Quantizer;

            /// <summary>Channel index (0 = X, 1 = Y, 2 = B).</summary>
            public nuint Channel;

            /// <summary>Quantization matrix multiplier for this channel.</summary>
            public float QmMultiplier;

            /// <summary>AC transform strategy type.</summary>
            public AcStrategyType Kind;

            /// <summary>Transform block width in 8x8 blocks.</summary>
            public nuint XSize;

            /// <summary>Transform block height in 8x8 blocks.</summary>
            public nuint YSize;

            /// <summary>Raw quantization factor for this block.</summary>
            public int Quant;
        }

        /// <summary>
        /// Quantizes a single channel's AC transform block into 32-bit integer coefficients.
        /// Ported from <c>QuantizeBlockAC</c> in <c>lib/jxl/enc_group.cc</c>.
        /// </summary>
        /// <param name="args">Quantization parameters.</param>
        /// <param name="errorDiffusion">Whether error diffusion is enabled per channel (length 3).</param>
        /// <param name="blockIn">Input floating-point frequency coefficients.</param>
        /// <param name="blockOut">Output quantized integer coefficients.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void QuantizeBlockAC(
            in QuantBlockArgs args,
            ReadOnlySpan<bool> errorDiffusion,
            ReadOnlySpan<float> blockIn,
            Span<int> blockOut)
        {
            _                      = errorDiffusion;
            nuint coveredBlocks    = args.XSize * args.YSize;
            nuint size             = coveredBlocks * FrameDimensions.kDCTBlockSize;
            float scale            = args.Quantizer.Scale() * args.Quant * args.QmMultiplier;
            ReadOnlySpan<float> qm = args.Quantizer.InvDequantMatrix(args.Kind, args.Channel);
            float thres            = args.Channel == 1 ? 0.58f : 0.62f;

            for (nuint k = 0; k < size; k++)
            {
                if (IsLowestFrequencyCoord(k, args.XSize, args.YSize))
                {
                    blockOut[(int)k] = 0;

                    continue;
                }

                float val        = blockIn[(int)k] * qm[(int)k] * scale;
                float absV       = MathF.Abs(val);
                int q            = absV < thres ? 0 : (int)MathF.Round(val);
                blockOut[(int)k] = q;
            }
        }

        /// <summary>
        /// Adjusts quantized AC coefficients for butteraugli-oriented zero-biasing heuristics.
        /// Ported from <c>AdjustQuantBlockAC</c> in <c>lib/jxl/enc_group.cc</c>.
        /// </summary>
        /// <param name="quantizer">Quantizer instance.</param>
        /// <param name="kind">AC transform strategy type.</param>
        /// <param name="xsize">Transform block width in 8x8 blocks.</param>
        /// <param name="ysize">Transform block height in 8x8 blocks.</param>
        /// <param name="block">Scaled floating-point DCT coefficients.</param>
        /// <param name="qblock">Quantized integer DCT coefficients to adjust in-place.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void AdjustQuantBlockAC(
            Quantizer quantizer,
            AcStrategyType kind,
            nuint xsize,
            nuint ysize,
            ReadOnlySpan<float> block,
            Span<int> qblock)
        {
            _ = quantizer;
            _ = kind;
            nuint size = xsize * ysize * FrameDimensions.kDCTBlockSize;

            for (nuint k = 0; k < size && k < (nuint)qblock.Length; k++)
            {
                if (IsLowestFrequencyCoord(k, xsize, ysize))
                {
                    qblock[(int)k] = 0;

                    continue;
                }

                if (qblock[(int)k] != 0 && MathF.Abs(block[(int)k]) < 0.52f)
                {
                    qblock[(int)k] = 0;
                }
            }
        }

        /// <summary>
        /// Quantizes the Y channel AC block and writes the dequantized Y coefficients into <paramref name="blockOut"/>
        /// for Chroma-from-Luma (CfL) prediction of X and B channels.
        /// Ported from <c>QuantizeRoundtripYBlockAC</c> in <c>lib/jxl/enc_group.cc</c>.
        /// </summary>
        /// <param name="args">Quantization parameters (for channel 1).</param>
        /// <param name="errorDiffusion">Per-channel error diffusion flags.</param>
        /// <param name="biases">Quantization bias constants (length 4).</param>
        /// <param name="blockIn">Input Y DCT coefficients.</param>
        /// <param name="blockOut">Output roundtrip dequantized Y DCT coefficients.</param>
        /// <param name="qblock">Output quantized integer Y DCT coefficients.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void QuantizeRoundtripYBlockAC(
            in QuantBlockArgs args,
            ReadOnlySpan<bool> errorDiffusion,
            ReadOnlySpan<float> biases,
            ReadOnlySpan<float> blockIn,
            Span<float> blockOut,
            Span<int> qblock)
        {
            QuantizeBlockAC(in args, errorDiffusion, blockIn, qblock);

            nuint size                = args.XSize * args.YSize * FrameDimensions.kDCTBlockSize;
            float invScale            = args.Quantizer.InvGlobalScale() / args.Quant;
            ReadOnlySpan<float> dqMat = args.Quantizer.DequantMatrix(args.Kind, 1);

            for (nuint k = 0; k < size; k++)
            {
                if (IsLowestFrequencyCoord(k, args.XSize, args.YSize))
                {
                    blockOut[(int)k] = blockIn[(int)k];

                    continue;
                }

                int q            = qblock[(int)k];
                float dq         = DequantBias(q, biases) * dqMat[(int)k] * invScale;
                blockOut[(int)k] = dq;
            }
        }

        /// <summary>
        /// Computes forward DCT transforms, CfL subtraction, AC quantization, and progressive splitting
        /// for AC group <paramref name="groupIdx"/>.
        /// Ported from <c>jxl::ComputeCoefficients</c> in <c>lib/jxl/enc_group.cc</c>.
        /// </summary>
        /// <param name="groupIdx">AC group index.</param>
        /// <param name="encState">Passes encoder state.</param>
        /// <param name="opsin">Input XYB opsin image.</param>
        /// <param name="dc">Optional DC scratch image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Status ComputeCoefficients(
            nuint groupIdx,
            PassesEncoderState encState,
            Image3<float> opsin,
            Image3<float>? dc = null)
        {
            _ = dc;
            PassesSharedState shared = encState.Shared;
            Rect blockGroupRect      = shared.FrameDim.BlockGroupRect(groupIdx);
            int maxArea              = EncCache.MaxTransformArea(shared.AcStrategy, blockGroupRect);

            float[] floatScratch  = ArrayPool<float>.Shared.Rent(8 * maxArea);
            int[] qblockScratch   = ArrayPool<int>.Shared.Rent(3 * maxArea);
            Memory<int>[] passPtr = new Memory<int>[encState.Coeffs.Count];

            try
            {
                Span<float> coeffScratch = floatScratch.AsSpan(0, 3 * maxArea);
                Span<float> yRoundtrip   = floatScratch.AsSpan(3 * maxArea, maxArea);
                Span<float> fftScratch   = floatScratch.AsSpan(4 * maxArea, 4 * maxArea);
                Span<int> qblockSpan     = qblockScratch.AsSpan(0, 3 * maxArea);

                Span<bool> errDiff = stackalloc bool[3];
                bool enableErrDiff = encState.CParams.SpeedTier <= SpeedTier.kWombat;
                errDiff[0]         = enableErrDiff;
                errDiff[1]         = enableErrDiff;
                errDiff[2]         = enableErrDiff;

                nuint offset = 0;

                for (nuint by = 0; by < (nuint)blockGroupRect.YSize; by++)
                {
                    AcStrategyRow acsRow    = shared.AcStrategy.ConstRow(blockGroupRect, by);
                    ReadOnlySpan<int> rowQf = blockGroupRect.ConstRow(shared.RawQuantField, by);

                    for (nuint bx = 0; bx < (nuint)blockGroupRect.XSize; bx++)
                    {
                        AcStrategy acs = acsRow[bx];

                        if (!acs.IsFirstBlock())
                        {
                            continue;
                        }

                        nuint cx   = acs.CoveredBlocksX();
                        nuint cy   = acs.CoveredBlocksY();
                        nuint size = cx * cy * FrameDimensions.kDCTBlockSize;

                        ForwardTransformBlock(opsin, blockGroupRect, (bx, by, size), acs, coeffScratch, fftScratch);
                        QuantizeTransformBlock(
                            encState,
                            blockGroupRect,
                            (bx, by, cx, cy, size),
                            acs,
                            rowQf[(int)bx],
                            errDiff,
                            new QuantizePtrs(coeffScratch, yRoundtrip, qblockSpan));
                        SplitBlockAcrossPasses(encState, (groupIdx, offset, size), acs, (bx, by), qblockSpan, passPtr);

                        offset += size;
                    }
                }

                return Status.OkStatus;
            }
            finally
            {
                ArrayPool<float>.Shared.Return(floatScratch);
                ArrayPool<int>.Shared.Return(qblockScratch);
            }
        }

        private readonly ref struct QuantizePtrs
        {
            public readonly Span<float> Coeffs;
            public readonly Span<float> YRound;
            public readonly Span<int> Qblock;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public QuantizePtrs(Span<float> coeffs, Span<float> yRound, Span<int> qblock)
            {
                Coeffs = coeffs;
                YRound = yRound;
                Qblock = qblock;
            }
        }

        /// <summary>
        /// Writes the tokenized AC coefficients for group <paramref name="groupIdx"/> and pass <paramref name="passIdx"/>
        /// into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodeGroupTokenizedCoefficients</c> in <c>lib/jxl/enc_group.cc</c>.
        /// </summary>
        /// <param name="groupIdx">AC group index.</param>
        /// <param name="passIdx">Progressive pass index.</param>
        /// <param name="histogramIdx">Histogram cluster set index.</param>
        /// <param name="encState">Passes encoder state.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="auxOut">Optional auxiliary statistics sink.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodeGroupTokenizedCoefficients(
            nuint groupIdx,
            nuint passIdx,
            int histogramIdx,
            PassesEncoderState encState,
            BitWriter writer,
            AuxOut? auxOut = null)
        {
            PassesSharedState shared             = encState.Shared;
            PassesEncoderState.PassData passData = encState.Passes[(int)passIdx];
            nuint histoSelectorBits              = CeilLog2Nonzero(shared.NumHistograms);

            if (histoSelectorBits != 0)
            {
                Status bitOk = writer.WithMaxBits(
                    histoSelectorBits,
                    LayerType.Ac,
                    auxOut,
                    () =>
                    {
                        writer.Write(histoSelectorBits, (ulong)histogramIdx);

                        return true;
                    });

                if (!bitOk)
                {
                    return bitOk;
                }
            }

            nuint contextOffset = (nuint)histogramIdx * shared.BlockCtxMap.NumACContexts();

            return EncAns.WriteTokens(
                passData.AcTokens[(int)groupIdx],
                passData.Codes,
                contextOffset,
                writer,
                LayerType.Ac,
                auxOut);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLowestFrequencyCoord(nuint k, nuint xsize, nuint ysize)
        {
            if (xsize == 1 && ysize == 1)
            {
                return k == 0;
            }

            nuint lx = xsize;
            nuint ly = ysize;
            CoeffOrder.CoefficientLayout(ref ly, ref lx);

            nuint stride = lx * FrameDimensions.kBlockDim;
            nuint y      = k / stride;
            nuint x      = k % stride;

            return y < ly && x < lx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DequantBias(int q, ReadOnlySpan<float> biases)
        {
            if (q == 0)
            {
                return 0.0f;
            }

            if (q == 1)
            {
                return biases[1];
            }

            if (q == -1)
            {
                return -biases[1];
            }

            return q - (biases[3] / q);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardTransformBlock(
            Image3<float> opsin,
            Rect blockGroupRect,
            (nuint Bx, nuint By, nuint Size) geom,
            AcStrategy acs,
            Span<float> coeffs,
            Span<float> scratch)
        {
            nuint absPx        = ((nuint)blockGroupRect.X0 + geom.Bx) * FrameDimensions.kBlockDim;
            nuint absPy        = ((nuint)blockGroupRect.Y0 + geom.By) * FrameDimensions.kBlockDim;
            nuint pixelsPerRow = (nuint)opsin.PixelsPerRow;

            for (nuint c = 0; c < 3; c++)
            {
                ReadOnlySpan<float> src = opsin.ConstPlaneRow(c, absPy).Slice((int)absPx);
                EncTransforms.TransformFromPixels(
                    acs.Strategy(),
                    src,
                    pixelsPerRow,
                    coeffs.Slice((int)(c * geom.Size)),
                    scratch);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void QuantizeTransformBlock(
            PassesEncoderState encState,
            Rect blockGroupRect,
            (nuint Bx, nuint By, nuint Cx, nuint Cy, nuint Size) geom,
            AcStrategy acs,
            int quant,
            ReadOnlySpan<bool> errDiff,
            QuantizePtrs ptrs)
        {
            PassesSharedState shared   = encState.Shared;
            nuint size                 = geom.Size;
            ReadOnlySpan<float> biases = Quantizer.DefaultQuantBias;

            QuantBlockArgs yArgs = new QuantBlockArgs
            {
                Quantizer    = shared.Quantizer,
                Channel      = 1,
                QmMultiplier = 1.0f,
                Kind         = acs.Strategy(),
                XSize        = geom.Cx,
                YSize        = geom.Cy,
                Quant        = quant,
            };

            QuantizeRoundtripYBlockAC(
                in yArgs,
                errDiff,
                biases,
                ptrs.Coeffs.Slice((int)size),
                ptrs.YRound,
                ptrs.Qblock.Slice((int)size));

            nuint absTx = ((nuint)blockGroupRect.X0 + geom.Bx) / ChromaFromLumaConstants.kColorTileDimInBlocks;
            nuint absTy = ((nuint)blockGroupRect.Y0 + geom.By) / ChromaFromLumaConstants.kColorTileDimInBlocks;
            sbyte ytox  = shared.Cmap.YtoXMap!.ConstRow(absTy)[(int)absTx];
            sbyte ytob  = shared.Cmap.YtoBMap!.ConstRow(absTy)[(int)absTx];
            float xFac  = shared.Cmap.Base.YtoXRatio(ytox);
            float bFac  = shared.Cmap.Base.YtoBRatio(ytob);

            for (nuint k = 0; k < size; k++)
            {
                ptrs.Coeffs[(int)k]                -= xFac * ptrs.YRound[(int)k];
                ptrs.Coeffs[(int)((2 * size) + k)] -= bFac * ptrs.YRound[(int)k];
            }

            QuantBlockArgs xArgs = yArgs;
            xArgs.Channel        = 0;
            xArgs.QmMultiplier   = encState.XQmMultiplier;
            QuantizeBlockAC(in xArgs, errDiff, ptrs.Coeffs, ptrs.Qblock);

            QuantBlockArgs bArgs = yArgs;
            bArgs.Channel        = 2;
            bArgs.QmMultiplier   = encState.BQmMultiplier;
            QuantizeBlockAC(in bArgs, errDiff, ptrs.Coeffs.Slice((int)(2 * size)), ptrs.Qblock.Slice((int)(2 * size)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SplitBlockAcrossPasses(
            PassesEncoderState encState,
            (nuint GroupIdx, nuint Offset, nuint Size) blockLoc,
            AcStrategy acs,
            (nuint Bx, nuint By) coords,
            ReadOnlySpan<int> qblock,
            Span<Memory<int>> passPtrs)
        {
            int numPasses = encState.Coeffs.Count;

            for (nuint c = 0; c < 3; c++)
            {
                for (int p = 0; p < numPasses; p++)
                {
                    passPtrs[p] = encState.Coeffs[p].PlaneRow(c, blockLoc.GroupIdx, blockLoc.Offset).Mem32;
                }

                encState.ProgressiveSplitter.SplitACCoefficients(
                    qblock.Slice((int)(c * blockLoc.Size), (int)blockLoc.Size),
                    acs,
                    coords.Bx,
                    coords.By,
                    passPtrs.Slice(0, numPasses));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint CeilLog2Nonzero(nuint n)
        {
            return n <= 1 ? 0 : (nuint)BitOperations.Log2((uint)(n - 1)) + 1u;
        }
    }

    /// <summary>
    /// Frame-level metadata and control flags passed to <see cref="EncFrame.EncodeFrame"/>.
    /// Ported from <c>jxl::FrameInfo</c> in <c>lib/jxl/enc_frame.h</c>.
    /// </summary>
    public sealed class FrameInfo
    {
        /// <summary>Whether to save the frame in a reference slot before applying the color transform.</summary>
        public bool SaveBeforeColorTransform;

        /// <summary>Frame type (<see cref="FrameType.RegularFrame"/>, <see cref="FrameType.DCFrame"/>, etc.).</summary>
        public FrameType FrameType = FrameType.RegularFrame;

        /// <summary>Progressive DC level (<c>0..3</c>) when <see cref="FrameType"/> is <see cref="FrameType.DCFrame"/>.</summary>
        public nuint DcLevel;

        /// <summary>Whether the input <see cref="ImageBundle"/> needs color transformation into XYB/YCbCr.</summary>
        public bool IbNeedsColorTransform = true;

        /// <summary>Whether alpha is already premultiplied in the input image.</summary>
        public bool AlphaIsPremultiplied;

        /// <summary>Whether this frame is the preview frame.</summary>
        public bool IsPreview;

        /// <summary>Whether to defer flushing until the last pass.</summary>
        public bool WaitForLastPass;

        /// <summary>Reference frame slot (<c>0..3</c>) when saving a reference frame.</summary>
        public nuint SaveAsReference;

        /// <summary>Source blending information for this frame.</summary>
        public BlendingInfo BlendInfo = new BlendingInfo();

        /// <summary>Per-extra-channel blending information.</summary>
        public List<BlendingInfo> ExtraChannelBlendingInfo = new List<BlendingInfo>();

        /// <summary>Frame origin X0 when cropped.</summary>
        public int OriginX0;

        /// <summary>Frame origin Y0 when cropped.</summary>
        public int OriginY0;

        /// <summary>Whether this frame has a custom size or origin.</summary>
        public bool CustomSizeOrOrigin;

        /// <summary>Animation duration in ticks.</summary>
        public uint Duration;

        /// <summary>Animation timecode.</summary>
        public uint Timecode;

        /// <summary>Whether this is the last frame in the codestream.</summary>
        public bool IsLast = true;

        /// <summary>Optional frame name.</summary>
        public string Name = string.Empty;
    }

    /// <summary>
    /// Top-level frame encoder supporting VarDCT, Modular, Progressive DC, and lossless JPEG transcoding.
    /// Ported from <c>lib/jxl/enc_frame.h</c> and <c>lib/jxl/enc_frame.cc</c>.
    /// </summary>
    public static class EncFrame
    {
        /// <summary>
        /// Adjusts <paramref name="cparams"/> and <paramref name="frameHeader"/> flags prior to encoding a frame.
        /// Ported from <c>ParamsPostInit</c> in <c>lib/jxl/enc_frame.cc</c>.
        /// </summary>
        public static Status ParamsPostInit(
            FrameHeader frameHeader,
            CompressParams cparams,
            ImageBundle? ib,
            ref bool hasJpegMetadata,
            ref bool initializeGlobalState)
        {
            _ = hasJpegMetadata;
            _ = initializeGlobalState;

            if (cparams.Resampling <= 0)
            {
                cparams.Resampling = 1;
            }

            if (cparams.EcResampling <= 0)
            {
                cparams.EcResampling = cparams.Resampling;
            }

            if (cparams.ButteraugliDistance < 0.0f)
            {
                return Status.Failure("Invalid negative butteraugli distance");
            }

            if (cparams.ButteraugliDistance == 0.0f && !cparams.ModularMode)
            {
                cparams.SetLossless();
            }
            else if (cparams.ButteraugliDistance > 0.0f &&
                     cparams.ButteraugliDistance < CompressParams.KMinButteraugliDistance)
            {
                cparams.ButteraugliDistance = CompressParams.KMinButteraugliDistance;
            }

            if (cparams.ModularMode)
            {
                frameHeader.Encoding = FrameEncoding.Modular;
            }
            else
            {
                frameHeader.Encoding = FrameEncoding.VarDCT;
            }

            if (ib != null && ib.IsJPEG())
            {
                frameHeader.Encoding       = FrameEncoding.VarDCT;
                frameHeader.ColorTransform = ib.ColorTransform;
            }

            if (cparams.ProgressiveMode == Override.On ||
                cparams.QProgressiveMode == Override.On)
            {
                cparams.Responsive = 1;

                if (cparams.IsLossless())
                {
                    cparams.QProgressiveMode = Override.On;
                }
            }

            return Status.OkStatus;
        }

        /// <summary>
        /// Populates a <see cref="FrameHeader"/> for the current frame from <paramref name="cparams"/> and <paramref name="frameInfo"/>.
        /// Ported from <c>MakeFrameHeader</c> in <c>lib/jxl/enc_frame.cc</c>.
        /// </summary>
        public static Status MakeFrameHeader(
            nuint xsize,
            nuint ysize,
            CompressParams cparams,
            ProgressiveSplitter progressiveSplitter,
            FrameInfo frameInfo,
            JPEGData? jpegData,
            FrameHeader frameHeader)
        {
            frameHeader.NonserializedIsPreview   = frameInfo.IsPreview;
            frameHeader.IsLast                   = frameInfo.IsLast;
            frameHeader.SaveBeforeColorTransform = frameInfo.SaveBeforeColorTransform;
            frameHeader.FrameType                = frameInfo.FrameType;
            frameHeader.Name                     = frameInfo.Name;
            frameHeader.Encoding                 = cparams.ModularMode ? FrameEncoding.Modular : FrameEncoding.VarDCT;
            frameHeader.ColorTransform           = cparams.ColorTransform;
            frameHeader.Flags                    = 0;

            ConfigureProgressiveMode(cparams, progressiveSplitter, frameHeader);

            Status passOk = progressiveSplitter.InitPasses(frameHeader.Passes);

            if (!passOk)
            {
                return passOk;
            }

            ConfigureFrameDimensionsAndBlending(xsize, ysize, cparams, frameInfo, frameHeader);
            ConfigureLoopFilter(cparams, jpegData, frameHeader);

            if (jpegData != null)
            {
                Status jpgOk = ConfigureJpegFrameHeader(cparams, jpegData, frameHeader);

                if (!jpgOk)
                {
                    return jpgOk;
                }
            }

            return Status.OkStatus;
        }

        /// <summary>
        /// Serializes <paramref name="frameHeader"/> to <paramref name="writer"/> and zero-pads to a byte boundary.
        /// Ported from <c>WriteFrameHeader</c> in <c>lib/jxl/enc_frame.cc</c>.
        /// </summary>
        public static Status WriteFrameHeader(
            FrameHeader frameHeader,
            BitWriter writer,
            AuxOut? auxOut = null)
        {
            Status writeOk = Bundle.Write(frameHeader, writer, LayerType.Header, auxOut);

            if (!writeOk)
            {
                return writeOk;
            }

            return Status.OkStatus;
        }

        /// <summary>
        /// Encodes a complete frame (VarDCT, Modular, or JPEG transcode) into <paramref name="io"/>.<c>Writer</c>.
        /// Ported from <c>jxl::EncodeFrame</c> in <c>lib/jxl/enc_frame.cc</c>.
        /// </summary>
        public static Status EncodeFrame(
            JxlMemoryManager? memoryManager,
            CompressParams cparamsOrig,
            FrameInfo frameInfo,
            CodecMetadata metadata,
            ImageBundle ib,
            JxlCmsInterface cms,
            (ThreadPool? Pool, BitWriter Writer, AuxOut? AuxOut) io)
        {
            JxlMemoryManager mm    = memoryManager ?? JxlMemoryManager.Default;
            CompressParams cparams = cparamsOrig.Clone();
            FrameHeader frameHdr   = new FrameHeader(metadata);

            bool hasJpegMeta = ib.JpegData != null;
            bool initGlobal  = true;
            Status postOk    = ParamsPostInit(frameHdr, cparams, ib, ref hasJpegMeta, ref initGlobal);

            if (!postOk)
            {
                return postOk;
            }

            using PassesEncoderState encState = new PassesEncoderState(mm)
            {
                CParams               = cparams,
                InitializeGlobalState = initGlobal,
            };

            nuint xsize = ib.JpegData != null ? (nuint)ib.JpegData.Width : ib.XSize();
            nuint ysize = ib.JpegData != null ? (nuint)ib.JpegData.Height : ib.YSize();

            Status hdrOk = MakeFrameHeader(
                xsize,
                ysize,
                cparams,
                encState.ProgressiveSplitter,
                frameInfo,
                ib.JpegData,
                frameHdr);

            if (!hdrOk)
            {
                return hdrOk;
            }

            Status sharedOk = PassesSharedState.InitializePassesSharedState(frameHdr, encState.Shared, encoder: true);

            if (!sharedOk)
            {
                return sharedOk;
            }

            StatusOr<ModularFrameEncoder> mfeOr = ModularFrameEncoder.Create(mm, frameHdr, cparams, encState.StreamingMode);

            if (!mfeOr.IsOk)
            {
                return mfeOr.Status;
            }

            using ModularFrameEncoder modularFrameEncoder = mfeOr.Value;

            if (ib.JpegData != null)
            {
                return EncodeJpegTranscodeFrame(frameHdr, ib.JpegData, encState, modularFrameEncoder, io);
            }

            return EncodePixelFrame(mm, frameHdr, frameInfo, metadata, ib, cms, encState, (modularFrameEncoder, io.Pool, io.Writer, io.AuxOut));
        }

        private static void ConfigureProgressiveMode(
            CompressParams cparams,
            ProgressiveSplitter progressiveSplitter,
            FrameHeader frameHeader)
        {
            if (cparams.CustomProgressiveMode != null)
            {
                progressiveSplitter.SetProgressiveMode(cparams.CustomProgressiveMode);

                return;
            }

            if (cparams.ModularMode)
            {
                progressiveSplitter.SetProgressiveMode(new ProgressiveMode());

                return;
            }

            if (cparams.QProgressiveMode == Override.On)
            {
                Span<PassDefinition> qPasses = stackalloc PassDefinition[2]
                {
                    new PassDefinition(8, 1, 2),
                    new PassDefinition(8, 0, 0),
                };

                progressiveSplitter.SetProgressiveMode(new ProgressiveMode(qPasses));
            }
            else if (cparams.ProgressiveMode == Override.On)
            {
                Span<PassDefinition> pPasses = stackalloc PassDefinition[3]
                {
                    new PassDefinition(2, 0, 4),
                    new PassDefinition(3, 0, 2),
                    new PassDefinition(8, 0, 0),
                };

                progressiveSplitter.SetProgressiveMode(new ProgressiveMode(pPasses));
            }
            else
            {
                progressiveSplitter.SetProgressiveMode(new ProgressiveMode());
            }

            _ = frameHeader;
        }

        private static void ConfigureFrameDimensionsAndBlending(
            nuint xsize,
            nuint ysize,
            CompressParams cparams,
            FrameInfo frameInfo,
            FrameHeader frameHeader)
        {
            frameHeader.DcLevel    = (uint)frameInfo.DcLevel;
            frameHeader.Upsampling = (uint)Math.Max(1, cparams.Resampling);

            if (frameHeader.NonserializedMetadata != null)
            {
                frameHeader.ExtraChannelUpsampling.Clear();
                uint ecUps = (uint)Math.Max(1, cparams.EcResampling);

                for (int i = 0; i < frameHeader.NonserializedMetadata.M.ExtraChannelInfo.Count; i++)
                {
                    frameHeader.ExtraChannelUpsampling.Add(ecUps);
                }
            }

            if (frameHeader.FrameType != FrameType.DCFrame)
            {
                frameHeader.FrameOrigin.X0  = frameInfo.OriginX0;
                frameHeader.FrameOrigin.Y0  = frameInfo.OriginY0;
                nuint ups                   = cparams.AlreadyDownsampled ? (nuint)frameHeader.Upsampling : 1u;
                frameHeader.FrameSize.XSize = (uint)(xsize * ups);
                frameHeader.FrameSize.YSize = (uint)(ysize * ups);

                if (frameInfo.CustomSizeOrOrigin ||
                    frameInfo.OriginX0 != 0 ||
                    frameInfo.OriginY0 != 0 ||
                    frameHeader.FrameSize.XSize != frameHeader.DefaultXSize() ||
                    frameHeader.FrameSize.YSize != frameHeader.DefaultYSize())
                {
                    frameHeader.CustomSizeOrOrigin = true;
                }
            }

            frameHeader.BlendingInfo.Mode         = frameInfo.BlendInfo.Mode;
            frameHeader.BlendingInfo.AlphaChannel = frameInfo.BlendInfo.AlphaChannel;
            frameHeader.BlendingInfo.Clamp        = frameInfo.BlendInfo.Clamp;
            frameHeader.BlendingInfo.Source       = frameInfo.BlendInfo.Source;

            if (frameHeader.NonserializedMetadata != null && frameHeader.NonserializedMetadata.M.HaveAnimation)
            {
                frameHeader.AnimationFrame.Duration = frameInfo.Duration;
                frameHeader.AnimationFrame.Timecode = frameInfo.Timecode;
            }

            if (frameInfo.FrameType == FrameType.ReferenceOnly || frameInfo.SaveAsReference != 0)
            {
                frameHeader.SaveAsReference = (uint)frameInfo.SaveAsReference;
            }
        }

        private static void ConfigureLoopFilter(
            CompressParams cparams,
            JPEGData? jpegData,
            FrameHeader frameHeader)
        {
            LoopFilter lf = frameHeader.LoopFilter;

            if (cparams.ModularMode || jpegData != null)
            {
                lf.Gab      = false;
                lf.EpfIters = 0;

                return;
            }

            if (cparams.Gaborish == Override.On)
            {
                lf.Gab = true;
            }
            else if (cparams.Gaborish == Override.Off)
            {
                lf.Gab = false;
            }
            else
            {
                lf.Gab = cparams.ButteraugliDistance >= CompressParams.KMinButteraugliForDynamicAR;
            }

            if (cparams.Epf >= 0 && cparams.Epf <= 3)
            {
                lf.EpfIters = (uint)cparams.Epf;
            }
            else
            {
                lf.EpfIters = cparams.ButteraugliDistance >= CompressParams.KMinButteraugliForDynamicAR ? 1u : 0u;
            }
        }

        private static Status ConfigureJpegFrameHeader(
            CompressParams cparams,
            JPEGData jpegData,
            FrameHeader frameHeader)
        {
            frameHeader.Encoding = FrameEncoding.VarDCT;

            Status csOk = EncJpegData.SetChromaSubsamplingFromJpegData(jpegData, frameHeader.ChromaSubsampling);

            if (!csOk)
            {
                return csOk;
            }

            Status ctOk = EncJpegData.SetColorTransformFromJpegData(jpegData, out ColorTransform ct);

            if (!ctOk)
            {
                return ctOk;
            }

            frameHeader.ColorTransform      = ct;
            frameHeader.XQmScale            = 2;
            frameHeader.BQmScale            = 2;
            frameHeader.LoopFilter.Gab      = false;
            frameHeader.LoopFilter.EpfIters = 0;

            frameHeader.UpdateFlag(false, FrameHeader.KUseDcFrame);

            if (!((cparams.ForceLfsJpegRecompression == -1 && frameHeader.ChromaSubsampling.Is444()) ||
                  cparams.ForceLfsJpegRecompression == 1))
            {
                frameHeader.UpdateFlag(true, FrameHeader.KSkipAdaptiveDCSmoothing);
            }

            return Status.OkStatus;
        }

        private static Status EncodePixelFrame(
            JxlMemoryManager mm,
            FrameHeader frameHeader,
            FrameInfo frameInfo,
            CodecMetadata metadata,
            ImageBundle ib,
            JxlCmsInterface cms,
            PassesEncoderState encState,
            (ModularFrameEncoder Mfe, ThreadPool? Pool, BitWriter Writer, AuxOut? AuxOut) ctx)
        {
            FrameDimensions frameDim = encState.Shared.FrameDim;

            if (!encState.CParams.AlreadyDownsampled && encState.CParams.EcResampling > 1)
            {
                Status ecDsOk = DownsampleExtraChannelsInPlace(mm, ib, (nuint)encState.CParams.EcResampling);

                if (!ecDsOk)
                {
                    return ecDsOk;
                }
            }

            if (frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status varOk = PrepareVarDctFrame(mm, frameHeader, frameInfo, ib, cms, encState, (ctx.Mfe, ctx.Pool, ctx.AuxOut));

                if (!varOk)
                {
                    return varOk;
                }
            }
            else
            {
                Status modOk = PrepareModularFrame(mm, frameHeader, metadata, ib, cms, encState, (ctx.Mfe, ctx.Pool, ctx.AuxOut));

                if (!modOk)
                {
                    return modOk;
                }
            }

            Status treeOk = ctx.Mfe.ComputeTree(ctx.Pool);

            if (!treeOk)
            {
                return treeOk;
            }

            Status tokOk = ctx.Mfe.ComputeTokens(ctx.Pool);

            if (!tokOk)
            {
                return tokOk;
            }

            return AssembleAndWriteFrameSections(mm, frameHeader, frameDim, encState, ctx.Mfe, (ctx.Writer, ctx.Pool, ctx.AuxOut));
        }

        private static Status PrepareVarDctFrame(
            JxlMemoryManager mm,
            FrameHeader frameHeader,
            FrameInfo frameInfo,
            ImageBundle ib,
            JxlCmsInterface cms,
            PassesEncoderState encState,
            (ModularFrameEncoder Mfe, ThreadPool? Pool, AuxOut? AuxOut) ctx)
        {
            FrameDimensions frameDim     = encState.Shared.FrameDim;
            StatusOr<Image3<float>> opOr = Image3<float>.Create(mm, frameDim.XSizePadded, frameDim.YSizePadded);

            if (!opOr.IsOk)
            {
                return opOr.Status;
            }

            using Image3<float> opsin = opOr.Value;
            Status xybOk              = ConvertBundleToOpsin(mm, frameHeader, frameInfo, ib, cms, (ctx.Pool, encState.CParams.AlreadyDownsampled), opsin);

            if (!xybOk)
            {
                return xybOk;
            }

            Status heurOk = EncHeuristics.LossyFrameHeuristics(
                frameHeader,
                encState,
                ctx.Mfe,
                ib,
                opsin,
                cms,
                (ctx.Pool, ctx.AuxOut));

            if (!heurOk)
            {
                return heurOk;
            }

            UpdateUsedAcsMask(encState);

            Rect paddedRect = new Rect(0, 0, (int)frameDim.XSizePadded, (int)frameDim.YSizePadded);
            Status initOk   = EncCache.InitializePassesEncoder(
                frameHeader,
                opsin,
                paddedRect,
                cms,
                ctx.Pool,
                encState,
                (ctx.Mfe, ctx.AuxOut));

            if (!initOk)
            {
                return initOk;
            }

            if (ib.ExtraChannels.Count > 0 && frameHeader.NonserializedMetadata != null)
            {
                ModularEncodingDataArgs ecArgs = new ModularEncodingDataArgs
                {
                    FrameHeader   = frameHeader,
                    Metadata      = frameHeader.NonserializedMetadata.M,
                    Color         = null,
                    ExtraChannels = ib.ExtraChannels,
                    GroupRect     = new Rect(0, 0, (int)frameDim.XSize, (int)frameDim.YSize),
                    PatchDim      = frameDim,
                    FrameAreaRect = new Rect(0, 0, (int)frameDim.XSize, (int)frameDim.YSize),
                    EncState      = encState,
                    Cms           = cms,
                    Pool          = ctx.Pool,
                    AuxOut        = ctx.AuxOut,
                    DoColor       = false,
                };

                return ctx.Mfe.ComputeEncodingData(ecArgs);
            }

            return Status.OkStatus;
        }

        private static Status ConvertBundleToOpsin(
            JxlMemoryManager mm,
            FrameHeader frameHeader,
            FrameInfo frameInfo,
            ImageBundle ib,
            JxlCmsInterface cms,
            (ThreadPool? Pool, bool AlreadyDownsampled) opts,
            Image3<float> opsin)
        {
            bool needDownsample = frameHeader.Upsampling > 1 && !opts.AlreadyDownsampled;

            if (needDownsample)
            {
                StatusOr<Image3<float>> fullOr = Image3<float>.Create(mm, ib.XSize(), ib.YSize());

                if (!fullOr.IsOk)
                {
                    return fullOr.Status;
                }

                using Image3<float> fullOpsin = fullOr.Value;
                Status fullOk                 = ConvertOrCopyBundleToImage3(frameHeader, frameInfo, ib, cms, opts.Pool, fullOpsin);

                if (!fullOk)
                {
                    return fullOk;
                }

                nuint dsX = Common.DivCeil(ib.XSize(), frameHeader.Upsampling);
                nuint dsY = Common.DivCeil(ib.YSize(), frameHeader.Upsampling);
                DownsampleImage3Into(fullOpsin, frameHeader.Upsampling, opsin, dsX, dsY);
                PadOpsinToMultipleOf8(opsin, dsX, dsY);

                return Status.OkStatus;
            }

            Status directOk = ConvertOrCopyBundleToImage3(frameHeader, frameInfo, ib, cms, opts.Pool, opsin);

            if (!directOk)
            {
                return directOk;
            }

            PadOpsinToMultipleOf8(opsin, ib.XSize(), ib.YSize());

            return Status.OkStatus;
        }

        private static Status ConvertOrCopyBundleToImage3(
            FrameHeader frameHeader,
            FrameInfo frameInfo,
            ImageBundle ib,
            JxlCmsInterface cms,
            ThreadPool? pool,
            Image3<float> dst)
        {
            if (frameHeader.ColorTransform == ColorTransform.XYB && frameInfo.IbNeedsColorTransform)
            {
                return EncXyb.ToXYB(ib, pool, dst, cms, linear: null);
            }

            Image3<float> ibColor = ib.Color();

            for (nuint c = 0; c < 3; c++)
            {
                CopyPlaneClamped(ibColor.Plane(c), dst.Plane(c));
            }

            return Status.OkStatus;
        }

        private static void CopyPlaneClamped(ImagePlane<float> srcPlane, ImagePlane<float> dstPlane)
        {
            for (nuint y = 0; y < dstPlane.YSize; y++)
            {
                nuint sy               = Math.Min(y, srcPlane.YSize - 1);
                ReadOnlySpan<float> sr = srcPlane.RowSpan(sy);
                Span<float> dr         = dstPlane.RowSpan(y);

                for (nuint x = 0; x < dstPlane.XSize; x++)
                {
                    nuint sx   = Math.Min(x, srcPlane.XSize - 1);
                    dr[(int)x] = sr[(int)sx];
                }
            }
        }

        private static Status DownsampleExtraChannelsInPlace(JxlMemoryManager mm, ImageBundle ib, nuint factor)
        {
            for (int i = 0; i < ib.ExtraChannels.Count; i++)
            {
                ImagePlane<float> src           = ib.ExtraChannels[i];
                nuint outX                      = Common.DivCeil(src.XSize, factor);
                nuint outY                      = Common.DivCeil(src.YSize, factor);
                StatusOr<ImagePlane<float>> dOr = ImagePlane<float>.Create(mm, outX, outY);

                if (!dOr.IsOk)
                {
                    return dOr.Status;
                }

                DownsamplePlaneInto(src, factor, dOr.Value, outX, outY);
                src.Dispose();
                ib.ExtraChannels[i] = dOr.Value;
            }

            return Status.OkStatus;
        }

        private static void DownsampleImage3Into(Image3<float> src, nuint factor, Image3<float> dst, nuint outX, nuint outY)
        {
            for (nuint c = 0; c < 3; c++)
            {
                DownsamplePlaneInto(src.Plane(c), factor, dst.Plane(c), outX, outY);
            }
        }

        private static void DownsamplePlaneInto(ImagePlane<float> src, nuint factor, ImagePlane<float> dst, nuint outX, nuint outY)
        {
            float scale = 1.0f / (factor * factor);

            for (nuint oy = 0; oy < outY; oy++)
            {
                Span<float> dstRow = dst.RowSpan(oy);

                for (nuint ox = 0; ox < outX; ox++)
                {
                    dstRow[(int)ox] = AverageBlock(src, ox * factor, oy * factor, factor) * scale;
                }
            }
        }

        private static float AverageBlock(ImagePlane<float> src, nuint baseX, nuint baseY, nuint factor)
        {
            float sum = 0.0f;

            for (nuint dy = 0; dy < factor; dy++)
            {
                nuint sy                   = Math.Min(src.YSize - 1, baseY + dy);
                ReadOnlySpan<float> srcRow = src.RowSpan(sy);

                for (nuint dx = 0; dx < factor; dx++)
                {
                    nuint sx = Math.Min(src.XSize - 1, baseX + dx);
                    sum     += srcRow[(int)sx];
                }
            }

            return sum;
        }

        private static void PadOpsinToMultipleOf8(Image3<float> opsin, nuint validX, nuint validY)
        {
            if (validX == 0 || validY == 0)
            {
                return;
            }

            for (nuint c = 0; c < 3; c++)
            {
                ImagePlane<float> plane = opsin.Plane(c);

                for (nuint y = 0; y < validY; y++)
                {
                    Span<float> row = plane.RowSpan(y);
                    float edgeVal   = row[(int)validX - 1];

                    for (nuint x = validX; x < plane.XSize; x++)
                    {
                        row[(int)x] = edgeVal;
                    }
                }

                for (nuint y = validY; y < plane.YSize; y++)
                {
                    ReadOnlySpan<float> srcRow = plane.RowSpan(validY - 1);
                    Span<float> dstRow         = plane.RowSpan(y);
                    srcRow.Slice(0, (int)plane.XSize).CopyTo(dstRow.Slice(0, (int)plane.XSize));
                }
            }
        }

        private static void UpdateUsedAcsMask(PassesEncoderState encState)
        {
            PassesSharedState shared = encState.Shared;
            uint mask                = 0;

            for (nuint y = 0; y < shared.FrameDim.YSizeBlocks; y++)
            {
                AcStrategyRow row = shared.AcStrategy.ConstRow(y);

                for (nuint x = 0; x < shared.FrameDim.XSizeBlocks; x++)
                {
                    if (row[x].IsFirstBlock())
                    {
                        mask |= 1u << (int)row[x].RawStrategy();
                    }
                }
            }

            encState.UsedAcs = mask;
        }

        private static Status PrepareModularFrame(
            JxlMemoryManager mm,
            FrameHeader frameHeader,
            CodecMetadata metadata,
            ImageBundle ib,
            JxlCmsInterface cms,
            PassesEncoderState encState,
            (ModularFrameEncoder Mfe, ThreadPool? Pool, AuxOut? AuxOut) ctx)
        {
            FrameDimensions frameDim = encState.Shared.FrameDim;
            Rect fullRect            = new Rect(0, 0, (int)frameDim.XSize, (int)frameDim.YSize);
            Image3<float> colorToUse = ib.Color();
            Image3<float>? dsTemp    = null;

            if (frameHeader.Upsampling > 1 && !encState.CParams.AlreadyDownsampled)
            {
                StatusOr<Image3<float>> dsOr = Image3<float>.Create(mm, frameDim.XSize, frameDim.YSize);

                if (!dsOr.IsOk)
                {
                    return dsOr.Status;
                }

                dsTemp = dsOr.Value;
                DownsampleImage3Into(ib.Color(), frameHeader.Upsampling, dsTemp, frameDim.XSize, frameDim.YSize);
                colorToUse = dsTemp;
            }

            try
            {
                ModularEncodingDataArgs ma = new ModularEncodingDataArgs
                {
                    FrameHeader   = frameHeader,
                    Metadata      = metadata.M,
                    Color         = colorToUse,
                    ExtraChannels = ib.ExtraChannels,
                    GroupRect     = fullRect,
                    PatchDim      = frameDim,
                    FrameAreaRect = fullRect,
                    EncState      = encState,
                    Cms           = cms,
                    Pool          = ctx.Pool,
                    AuxOut        = ctx.AuxOut,
                    DoColor       = true,
                };

                return ctx.Mfe.ComputeEncodingData(ma);
            }
            finally
            {
                dsTemp?.Dispose();
            }
        }

        private static Status EncodeJpegTranscodeFrame(
            FrameHeader frameHeader,
            JPEGData jpegData,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            (ThreadPool? Pool, BitWriter Writer, AuxOut? AuxOut) io)
        {
            JxlMemoryManager mm      = encState.MemoryManager ?? JxlMemoryManager.Default;
            PassesSharedState shared = encState.Shared;
            FrameDimensions frameDim = shared.FrameDim;

            Status populateOk = PopulateStateFromJpegData(frameHeader, jpegData, encState, modularFrameEncoder, io.Pool);

            if (!populateOk)
            {
                return populateOk;
            }

            Status treeOk = modularFrameEncoder.ComputeTree(io.Pool);

            if (!treeOk)
            {
                return treeOk;
            }

            Status tokOk = modularFrameEncoder.ComputeTokens(io.Pool);

            if (!tokOk)
            {
                return tokOk;
            }

            return AssembleAndWriteFrameSections(mm, frameHeader, frameDim, encState, modularFrameEncoder, (io.Writer, io.Pool, io.AuxOut));
        }

        private static Status PopulateStateFromJpegData(
            FrameHeader frameHeader,
            JPEGData jpegData,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            ThreadPool? pool)
        {
            PassesSharedState shared = encState.Shared;
            FrameDimensions frameDim = shared.FrameDim;
            JxlMemoryManager mm      = encState.MemoryManager ?? JxlMemoryManager.Default;

            StatusOr<ColorCorrelationMap> cmapOr = ColorCorrelationMap.Create(
                mm,
                frameDim.XSizePadded,
                frameDim.YSizePadded,
                xyb: false);

            if (!cmapOr.IsOk)
            {
                return cmapOr.Status;
            }

            shared.Cmap.Dispose();
            shared.Cmap = cmapOr.Value;

            Status qtOk = ConfigureJpegQuantTables(frameHeader, jpegData, encState, modularFrameEncoder, out float[] dcQuantR);

            if (!qtOk)
            {
                return qtOk;
            }

            int invGlobalScale = (int)shared.Quantizer.InvGlobalScale();

            for (nuint by = 0; by < frameDim.YSizeBlocks; by++)
            {
                Span<int> qfRow   = shared.RawQuantField.RowSpan(by);
                Span<byte> epfRow = shared.EpfSharpness.RowSpan(by);
                Span<byte> qdcRow = shared.QuantDc.RowSpan(by);

                qfRow.Slice(0, (int)frameDim.XSizeBlocks).Fill(invGlobalScale);
                epfRow.Slice(0, (int)frameDim.XSizeBlocks).Clear();
                qdcRow.Slice(0, (int)frameDim.XSizeBlocks).Fill(1);

                for (nuint bx = 0; bx < frameDim.XSizeBlocks; bx++)
                {
                    shared.AcStrategy.Set(bx, by, AcStrategyType.DCT);
                }
            }

            encState.UsedAcs = 1u << (int)AcStrategyType.DCT;

            StatusOr<Image3<float>> dcOr = Image3<float>.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

            if (!dcOr.IsOk)
            {
                return dcOr.Status;
            }

            using Image3<float> dc = dcOr.Value;
            Status copyOk          = CopyJpegCoeffsAndDc(frameHeader, jpegData, encState, dc, dcQuantR, mm);

            if (!copyOk)
            {
                return copyOk;
            }

            for (nuint g = 0; g < frameDim.NumDcGroups; g++)
            {
                Rect dcRect  = frameDim.DCGroupRect(g);
                Status addDc = modularFrameEncoder.AddVarDCTDC(frameHeader, dc, dcRect, g, nlDc: false, encState, jpegTranscode: true);

                if (!addDc)
                {
                    return addDc;
                }

                Status addMeta = modularFrameEncoder.AddACMetadata(dcRect, g, jpegTranscode: true, encState);

                if (!addMeta)
                {
                    return addMeta;
                }
            }

            return EncCache.BuildOrdersAndTokenizePasses(frameHeader, pool, encState, auxOut: null);
        }

        private static Status ConfigureJpegQuantTables(
            FrameHeader frameHeader,
            JPEGData jpegData,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            out float[] dcQuantR)
        {
            dcQuantR                 = new float[3];
            PassesSharedState shared = encState.Shared;
            JxlMemoryManager mm      = encState.MemoryManager ?? JxlMemoryManager.Default;
            bool isGray              = jpegData.Components.Count == 1;
            int[] cMap               = DecGroup.JpegOrder(frameHeader.ColorTransform, isGray);
            int[] rawTable           = new int[3 * 64];
            float[] dcQuantization   = new float[3];

            for (int c = 0; c < 3; c++)
            {
                JPEGComponent comp = jpegData.Components[cMap[c]];
                int qIdx           = (int)comp.QuantIdx;

                if (qIdx >= jpegData.Quant.Count)
                {
                    return Status.Failure("Invalid JPEG quant table index");
                }

                int[] values      = jpegData.Quant[qIdx].Values;
                int dcStep        = Math.Max(1, values[0]);
                dcQuantization[c] = (255.0f * 8.0f) / dcStep;
                dcQuantR[c]       = 1.0f / dcQuantization[c];

                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        rawTable[(c * 64) + (x * 8) + y] = Math.Max(1, values[(y * 8) + x]);
                    }
                }
            }

            QuantEncoding[] encodings = new QuantEncoding[DequantMatrices.kNumQuantTables];

            for (int i = 0; i < encodings.Length; i++)
            {
                encodings[i] = QuantEncoding.Library(0);
            }

            encodings[0] = QuantEncoding.RAW(rawTable, 0);

            Status setCustomOk = EncQuantWeights.DequantMatricesSetCustom(shared.Matrices, encodings, modularFrameEncoder);

            if (!setCustomOk)
            {
                return setCustomOk;
            }

            Status setDcOk = EncQuantWeights.DequantMatricesSetCustomDC(mm, shared.Matrices, dcQuantization);

            if (!setDcOk)
            {
                return setDcOk;
            }

            shared.Quantizer = new Quantizer(shared.Matrices, 1, Quantizer.kGlobalScaleDenom);
            shared.Quantizer.RecomputeFromGlobalScale();

            return Status.OkStatus;
        }

        private static Status CopyJpegCoeffsAndDc(
            FrameHeader frameHeader,
            JPEGData jpegData,
            PassesEncoderState encState,
            Image3<float> dc,
            float[] dcQuantR,
            JxlMemoryManager mm)
        {
            PassesSharedState shared = encState.Shared;
            FrameDimensions frameDim = shared.FrameDim;
            nuint groupElements      = FrameDimensions.KGroupDim * FrameDimensions.KGroupDim;

            encState.Coeffs.Clear();
            StatusOr<ACImageT<int>> acOr = ACImageT<int>.Make(mm, groupElements, frameDim.NumGroups);

            if (!acOr.IsOk)
            {
                return acOr.Status;
            }

            ACImageT<int> coeffImg = acOr.Value;
            coeffImg.ZeroFill();
            encState.Coeffs.Add(coeffImg);

            bool isGray   = jpegData.Components.Count == 1;
            int[] cMap    = DecGroup.JpegOrder(frameHeader.ColorTransform, isGray);
            float[] dcOff = new float[3];

            if (frameHeader.ColorTransform == ColorTransform.None)
            {
                IReadOnlyList<QuantEncoding> qe = shared.Matrices.Encodings;

                if (qe.Count > 0 && qe[0].QrawTable != null && qe[0].QrawTable!.Length >= 3 * 64)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        dcOff[c] = 1024 / Math.Max(1, qe[0].QrawTable![64 * c]);
                    }
                }
            }

            ImageOps.ZeroFillImage(dc);

            for (nuint g = 0; g < frameDim.NumGroups; g++)
            {
                Rect groupRect = frameDim.BlockGroupRect(g);
                CopyJpegGroupCoeffs(frameHeader, jpegData, coeffImg, dc, g, groupRect, (isGray, cMap, dcOff, dcQuantR));
            }

            return Status.OkStatus;
        }

        private static void CopyJpegGroupCoeffs(
            FrameHeader frameHeader,
            JPEGData jpegData,
            ACImageT<int> coeffImg,
            Image3<float> dc,
            nuint groupIdx,
            Rect groupRect,
            (bool IsGray, int[] CMap, float[] DcOff, float[] DcQuantR) info)
        {
            YCbCrChromaSubsampling cs = frameHeader.ChromaSubsampling;

            for (int c = 0; c < 3; c++)
            {
                if (info.IsGray && c != 1)
                {
                    continue;
                }

                CopyJpegChannelGroupCoeffs(cs, jpegData.Components[info.CMap[c]], coeffImg, dc, (nuint)c, (groupIdx, groupRect), (info.DcOff[c], info.DcQuantR[c]));
            }
        }

        private static void CopyJpegChannelGroupCoeffs(
            YCbCrChromaSubsampling cs,
            JPEGComponent comp,
            ACImageT<int> coeffImg,
            Image3<float> dc,
            nuint c,
            (nuint GroupIdx, Rect GroupRect) grp,
            (float DcOff, float DcQuantR) quant)
        {
            int hs       = (int)cs.HShift(c);
            int vs       = (int)cs.VShift(c);
            nuint offset = 0;

            for (nuint by = 0; by < (nuint)grp.GroupRect.YSize; by++)
            {
                nuint absY = (nuint)grp.GroupRect.Y0 + by;

                if (((absY >> vs) << vs) != absY)
                {
                    continue;
                }

                nuint sby       = absY >> vs;
                Span<float> dcR = dc.PlaneRow(c, sby);

                for (nuint bx = 0; bx < (nuint)grp.GroupRect.XSize; bx++)
                {
                    nuint absX = (nuint)grp.GroupRect.X0 + bx;

                    if (((absX >> hs) << hs) != absX)
                    {
                        continue;
                    }

                    nuint sbx = absX >> hs;

                    if (sbx < comp.WidthInBlocks && sby < comp.HeightInBlocks)
                    {
                        Span<int> dstAc = coeffImg.PlaneRow(c, grp.GroupIdx, offset).Span32;
                        nuint blkIdx    = ((sby * comp.WidthInBlocks) + sbx) * 64;
                        dcR[(int)sbx]   = (comp.Coeffs[blkIdx] + quant.DcOff) * quant.DcQuantR;

                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                dstAc[(x * 8) + y] = comp.Coeffs[blkIdx + (uint)((y * 8) + x)];
                            }
                        }

                        dstAc[0] = 0;
                    }

                    offset += 64;
                }
            }
        }

        private static Status AssembleAndWriteFrameSections(
            JxlMemoryManager mm,
            FrameHeader frameHeader,
            FrameDimensions frameDim,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            (BitWriter Writer, ThreadPool? Pool, AuxOut? AuxOut) io)
        {
            nuint numPasses   = frameHeader.Passes.NumPasses;
            nuint numGroups   = frameDim.NumGroups;
            nuint numDcGroups = frameDim.NumDcGroups;
            nuint numSections = Toc.NumTocEntries(numGroups, numDcGroups, numPasses);

            BitWriter[] sections = new BitWriter[numSections];

            for (nuint i = 0; i < numSections; i++)
            {
                sections[i] = new BitWriter(mm);
            }

            if (numGroups == 1 && numPasses == 1)
            {
                Status singleOk = EncodeSingleSection(frameHeader, encState, modularFrameEncoder, sections[0], io.AuxOut);

                if (!singleOk)
                {
                    return singleOk;
                }
            }
            else
            {
                Status multiOk = EncodeMultipleSections(frameHeader, frameDim, encState, modularFrameEncoder, sections, (io.Pool, io.AuxOut));

                if (!multiOk)
                {
                    return multiOk;
                }
            }

            return FinalizeFrameBitstream(frameHeader, sections, io.Writer, io.AuxOut);
        }

        private static Status EncodeSingleSection(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            BitWriter secWriter,
            AuxOut? auxOut)
        {
            Status dcGlobalOk = WriteDcGlobalSection(frameHeader, encState, modularFrameEncoder, secWriter, auxOut);

            if (!dcGlobalOk)
            {
                return dcGlobalOk;
            }

            Status dcGroupOk = WriteDcGroupSection(frameHeader, encState, modularFrameEncoder, 0, secWriter, auxOut);

            if (!dcGroupOk)
            {
                return dcGroupOk;
            }

            Status acGlobalOk = WriteAcGlobalSection(frameHeader, encState, modularFrameEncoder, secWriter, auxOut);

            if (!acGlobalOk)
            {
                return acGlobalOk;
            }

            return WriteAcGroupSection(frameHeader, encState, modularFrameEncoder, 0, 0, secWriter, auxOut);
        }

        private static Status EncodeMultipleSections(
            FrameHeader frameHeader,
            FrameDimensions frameDim,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            BitWriter[] sections,
            (ThreadPool? Pool, AuxOut? AuxOut) io)
        {
            Status dcGlobalOk = WriteDcGlobalSection(frameHeader, encState, modularFrameEncoder, sections[0], io.AuxOut);

            if (!dcGlobalOk)
            {
                return dcGlobalOk;
            }

            nuint acGlobalIdx = 1 + frameDim.NumDcGroups;
            Status acGlobalOk = WriteAcGlobalSection(frameHeader, encState, modularFrameEncoder, sections[acGlobalIdx], io.AuxOut);

            if (!acGlobalOk)
            {
                return acGlobalOk;
            }

            if (io.AuxOut == null && io.Pool != null)
            {
                return EncodeGroupSectionsParallel(frameHeader, frameDim, encState, modularFrameEncoder, sections, io.Pool);
            }

            for (nuint g = 0; g < frameDim.NumDcGroups; g++)
            {
                Status dcGroupOk = WriteDcGroupSection(frameHeader, encState, modularFrameEncoder, g, sections[1 + g], io.AuxOut);

                if (!dcGroupOk)
                {
                    return dcGroupOk;
                }
            }

            for (nuint pass = 0; pass < frameHeader.Passes.NumPasses; pass++)
            {
                for (nuint g = 0; g < frameDim.NumGroups; g++)
                {
                    nuint secIdx     = Toc.AcGroupIndex(pass, g, frameDim.NumGroups, frameDim.NumDcGroups);
                    Status acGroupOk = WriteAcGroupSection(frameHeader, encState, modularFrameEncoder, pass, g, sections[secIdx], io.AuxOut);

                    if (!acGroupOk)
                    {
                        return acGroupOk;
                    }
                }
            }

            return Status.OkStatus;
        }

        private static Status EncodeGroupSectionsParallel(
            FrameHeader frameHeader,
            FrameDimensions frameDim,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            BitWriter[] sections,
            ThreadPool pool)
        {
            nuint numDcGroups = frameDim.NumDcGroups;
            nuint numGroups   = frameDim.NumGroups;
            nuint totalTasks  = numDcGroups + (frameHeader.Passes.NumPasses * numGroups);

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)totalTasks,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    nuint t = task;

                    if (t < numDcGroups)
                    {
                        return WriteDcGroupSection(frameHeader, encState, modularFrameEncoder, t, sections[1 + t], null);
                    }

                    nuint acTask = t - numDcGroups;
                    nuint pass   = acTask / numGroups;
                    nuint g      = acTask % numGroups;
                    nuint secIdx = Toc.AcGroupIndex(pass, g, numGroups, numDcGroups);

                    return WriteAcGroupSection(frameHeader, encState, modularFrameEncoder, pass, g, sections[secIdx], null);
                },
                "EncodeFrameSections");
        }

        private static Status WriteDcGlobalSection(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            BitWriter writer,
            AuxOut? auxOut)
        {
            PassesSharedState shared = encState.Shared;

            if ((frameHeader.Flags & FrameHeader.KPatches) != 0)
            {
                Status patchOk = PatchDictionaryEncoder.Encode(
                    shared.ImageFeatures.Patches,
                    writer,
                    LayerType.Dictionary,
                    auxOut);

                if (!patchOk)
                {
                    return patchOk;
                }
            }

            if ((frameHeader.Flags & FrameHeader.KSplines) != 0)
            {
                Status splineOk = EncSplines.EncodeSplines(
                    shared.ImageFeatures.Splines,
                    writer,
                    LayerType.Splines,
                    new HistogramParams(),
                    auxOut);

                if (!splineOk)
                {
                    return splineOk;
                }
            }

            if ((frameHeader.Flags & FrameHeader.KNoise) != 0)
            {
                Status noiseOk = EncNoise.EncodeNoise(
                    shared.ImageFeatures.NoiseParams,
                    writer,
                    LayerType.Noise,
                    auxOut);

                if (!noiseOk)
                {
                    return noiseOk;
                }
            }

            Status dqDcOk = EncQuantWeights.DequantMatricesEncodeDC(shared.Matrices, writer, LayerType.Quant, auxOut);

            if (!dqDcOk)
            {
                return dqDcOk;
            }

            if (frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status qOk = Bundle.Write(shared.Quantizer.GetParams(), writer, LayerType.Quant, auxOut);

                if (!qOk)
                {
                    return qOk;
                }

                Status ctxOk = EncContextMap.EncodeBlockCtxMap(shared.BlockCtxMap, writer, auxOut);

                if (!ctxOk)
                {
                    return ctxOk;
                }

                Status cmapOk = EncChromaFromLuma.ColorCorrelationEncodeDC(shared.Cmap.Base, writer, LayerType.ControlFields, auxOut);

                if (!cmapOk)
                {
                    return cmapOk;
                }
            }

            Status globalInfoOk = modularFrameEncoder.EncodeGlobalInfo(encState.StreamingMode, writer, auxOut);

            if (!globalInfoOk)
            {
                return globalInfoOk;
            }

            return modularFrameEncoder.EncodeStream(writer, auxOut, LayerType.ModularGlobal, ModularStreamId.Global());
        }

        private static Status WriteDcGroupSection(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            nuint dcGroupIdx,
            BitWriter writer,
            AuxOut? auxOut)
        {
            if (frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                if ((frameHeader.Flags & FrameHeader.KUseDcFrame) == 0)
                {
                    Status precOk = writer.WithMaxBits(
                        2,
                        LayerType.Dc,
                        auxOut,
                        () =>
                        {
                            writer.Write(2, modularFrameEncoder.ExtraDcPrecision[(int)dcGroupIdx]);

                            return true;
                        });

                    if (!precOk)
                    {
                        return precOk;
                    }

                    Status dcOk = modularFrameEncoder.EncodeStream(
                        writer,
                        auxOut,
                        LayerType.Dc,
                        ModularStreamId.VarDCTDC(dcGroupIdx));

                    if (!dcOk)
                    {
                        return dcOk;
                    }
                }
            }

            Status modDcOk = modularFrameEncoder.EncodeStream(
                writer,
                auxOut,
                LayerType.ModularDcGroup,
                ModularStreamId.ModularDC(dcGroupIdx));

            if (!modDcOk)
            {
                return modDcOk;
            }

            if (frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Rect rect          = encState.Shared.FrameDim.DCGroupRect(dcGroupIdx);
                nuint nbBits       = CeilLog2Nonzero((nuint)(rect.XSize * rect.YSize));
                Status metaCountOk = Status.OkStatus;

                if (nbBits != 0)
                {
                    metaCountOk = writer.WithMaxBits(
                        nbBits,
                        LayerType.ControlFields,
                        auxOut,
                        () =>
                        {
                            writer.Write(nbBits, modularFrameEncoder.AcMetadataSize[(int)dcGroupIdx] - 1u);

                            return true;
                        });
                }

                if (!metaCountOk)
                {
                    return metaCountOk;
                }

                return modularFrameEncoder.EncodeStream(
                    writer,
                    auxOut,
                    LayerType.ControlFields,
                    ModularStreamId.ACMetadata(dcGroupIdx));
            }

            return Status.OkStatus;
        }

        private static Status WriteAcGlobalSection(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            BitWriter writer,
            AuxOut? auxOut)
        {
            if (frameHeader.Encoding != FrameEncoding.VarDCT)
            {
                return Status.OkStatus;
            }

            PassesSharedState shared = encState.Shared;

            Status dqOk = EncQuantWeights.DequantMatricesEncode(
                encState.MemoryManager,
                shared.Matrices,
                writer,
                LayerType.Quant,
                auxOut,
                modularFrameEncoder);

            if (!dqOk)
            {
                return dqOk;
            }

            nuint numHistoBits = CeilLog2Nonzero(shared.FrameDim.NumGroups);

            if (numHistoBits != 0)
            {
                Status histBitsOk = writer.WithMaxBits(
                    numHistoBits,
                    LayerType.Ac,
                    auxOut,
                    () =>
                    {
                        writer.Write(numHistoBits, shared.NumHistograms - 1u);

                        return true;
                    });

                if (!histBitsOk)
                {
                    return histBitsOk;
                }
            }

            return WritePassOrdersAndHistograms(frameHeader, encState, writer, auxOut);
        }

        private static Status WritePassOrdersAndHistograms(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            BitWriter writer,
            AuxOut? auxOut)
        {
            PassesSharedState shared = encState.Shared;
            ushort[] order16         = new ushort[CoeffOrder.kCoeffOrderMaxSize];

            for (nuint pass = 0; pass < frameHeader.Passes.NumPasses; pass++)
            {
                uint usedOrders = encState.UsedOrders[(int)pass];
                Status uoOk     = writer.WithMaxBits(
                    U32Coder.MaxEncodedBits(FrameHeaderMethods.KOrderEnc),
                    LayerType.Order,
                    auxOut,
                    () => U32Coder.Write(FrameHeaderMethods.KOrderEnc, usedOrders, writer));

                if (!uoOk)
                {
                    return uoOk;
                }

                nuint baseIdx = pass * shared.CoeffOrderSize;

                for (nuint i = 0; i < shared.CoeffOrderSize && i < (nuint)order16.Length; i++)
                {
                    order16[i] = (ushort)shared.CoeffOrders[baseIdx + i];
                }

                Status ordOk = EncCoeffOrder.EncodeCoeffOrders((ushort)usedOrders, order16, writer, LayerType.Order, auxOut);

                if (!ordOk)
                {
                    return ordOk;
                }

                List<BitWriter> encodedHists = encState.Passes[(int)pass].Codes.EncodedHistograms;

                for (int h = 0; h < encodedHists.Count; h++)
                {
                    Status appendOk = writer.AppendUnaligned(encodedHists[h]);

                    if (!appendOk)
                    {
                        return appendOk;
                    }
                }
            }

            return Status.OkStatus;
        }

        private static Status WriteAcGroupSection(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            nuint pass,
            nuint groupIdx,
            BitWriter writer,
            AuxOut? auxOut)
        {
            if (frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                int histIdx       = (int)encState.HistogramIdx[(int)groupIdx];
                Status varGroupOk = EncGroup.EncodeGroupTokenizedCoefficients(
                    groupIdx,
                    pass,
                    histIdx,
                    encState,
                    writer,
                    auxOut);

                if (!varGroupOk)
                {
                    return varGroupOk;
                }
            }

            return modularFrameEncoder.EncodeStream(
                writer,
                auxOut,
                LayerType.ModularAcGroup,
                ModularStreamId.ModularAC(groupIdx, pass));
        }

        private static Status FinalizeFrameBitstream(
            FrameHeader frameHeader,
            BitWriter[] sections,
            BitWriter writer,
            AuxOut? auxOut)
        {
            nuint[] groupSizes = new nuint[sections.Length];

            for (int i = 0; i < sections.Length; i++)
            {
                sections[i].ZeroPadToByte();
                groupSizes[i] = sections[i].BitsWritten / Common.kBitsPerByte;
            }

            Status hdrOk = WriteFrameHeader(frameHeader, writer, auxOut);

            if (!hdrOk)
            {
                return hdrOk;
            }

            Status permOk = Toc.WriteTocPermutation(ReadOnlySpan<uint>.Empty, writer, auxOut);

            if (!permOk)
            {
                return permOk;
            }

            Status tocOk = Toc.WriteTocSizes(groupSizes, writer, auxOut);

            if (!tocOk)
            {
                return tocOk;
            }

            writer.ZeroPadToByte();

            return writer.AppendByteAligned(sections);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint CeilLog2Nonzero(nuint n)
        {
            return n <= 1 ? 0 : (nuint)BitOperations.Log2((uint)(n - 1)) + 1u;
        }
    }

    /// <summary>
    /// Lookup table entry for JPEG Huffman decoding.
    /// Ported from <c>jxl::jpeg::HuffmanTableEntry</c> in <c>lib/jxl/jpeg/enc_jpeg_huffman_decode.h</c>.
    /// </summary>
    public struct JpegHuffmanTableEntry
    {
        /// <summary>Bit length of the code or subtable index width.</summary>
        public byte Bits;

        /// <summary>Decoded symbol value or offset into subtable.</summary>
        public ushort Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="JpegHuffmanTableEntry"/> struct with sentinel values.
        /// </summary>
        /// <param name="bits">Code length in bits.</param>
        /// <param name="value">Decoded symbol or subtable offset.</param>
        public JpegHuffmanTableEntry(byte bits = 255, ushort value = 0xffff)
        {
            Bits  = bits;
            Value = value;
        }
    }

    /// <summary>
    /// Builds 2-level lookup tables for fast JPEG Huffman bitstream decoding.
    /// Ported from <c>lib/jxl/jpeg/enc_jpeg_huffman_decode.h</c> and <c>lib/jxl/jpeg/enc_jpeg_huffman_decode.cc</c>.
    /// </summary>
    public static class EncJpegHuffmanDecode
    {
        /// <summary>Root lookup table log2 size (8 bits = 256 entries).</summary>
        public const int KJpegHuffmanRootTableBits = 8;

        /// <summary>Maximum total lookup table size across root and subtables.</summary>
        public const int KJpegHuffmanLutSize = 758;

        /// <summary>
        /// Builds the JPEG Huffman lookup table from <paramref name="count"/> and <paramref name="symbols"/>.
        /// Ported from <c>jxl::jpeg::BuildJpegHuffmanTable</c> in <c>lib/jxl/jpeg/enc_jpeg_huffman_decode.cc</c>.
        /// </summary>
        /// <param name="count">Per-length code counts (indices 1..16).</param>
        /// <param name="symbols">Sorted symbol values.</param>
        /// <param name="lut">Output lookup table of at least <see cref="KJpegHuffmanLutSize"/> entries.</param>
        public static void BuildJpegHuffmanTable(
            ReadOnlySpan<uint> count,
            ReadOnlySpan<uint> symbols,
            Span<JpegHuffmanTableEntry> lut)
        {
            Span<int> tmpCount = stackalloc int[JpegConstants.KJpegHuffmanMaxBitLength + 1];
            int totalCount     = 0;

            for (int len = 1; len <= JpegConstants.KJpegHuffmanMaxBitLength; len++)
            {
                tmpCount[len] = (int)count[len];
                totalCount   += tmpCount[len];
            }

            int tableBits = KJpegHuffmanRootTableBits;
            int tableSize = 1 << tableBits;

            if (totalCount == 1)
            {
                JpegHuffmanTableEntry single = new JpegHuffmanTableEntry(0, (ushort)symbols[0]);

                for (int k = 0; k < tableSize; k++)
                {
                    lut[k] = single;
                }

                return;
            }

            int key = 0;
            int idx = 0;

            for (int len = 1; len <= KJpegHuffmanRootTableBits; len++)
            {
                for (; tmpCount[len] > 0; tmpCount[len]--)
                {
                    JpegHuffmanTableEntry code = new JpegHuffmanTableEntry((byte)len, (ushort)symbols[idx++]);
                    int reps                   = 1 << (KJpegHuffmanRootTableBits - len);

                    while (reps-- > 0)
                    {
                        lut[key++] = code;
                    }
                }
            }

            BuildSubtables(tmpCount, symbols, lut, ref idx, ref key, tableSize);
        }

        private static void BuildSubtables(
            Span<int> tmpCount,
            ReadOnlySpan<uint> symbols,
            Span<JpegHuffmanTableEntry> lut,
            ref int idx,
            ref int key,
            int tableOffset)
        {
            int tableSize = 0;
            int tableBits = 0;
            int low       = 0;

            for (int len = KJpegHuffmanRootTableBits + 1; len <= JpegConstants.KJpegHuffmanMaxBitLength; len++)
            {
                for (; tmpCount[len] > 0; tmpCount[len]--)
                {
                    if (low >= tableSize)
                    {
                        tableOffset += tableSize;
                        tableBits    = NextSubtableBitSize(tmpCount, len);
                        tableSize    = 1 << tableBits;
                        low          = 0;
                        lut[key]     = new JpegHuffmanTableEntry(
                            (byte)(tableBits + KJpegHuffmanRootTableBits),
                            (ushort)(tableOffset - key));
                        key++;
                    }

                    byte codeBits              = (byte)(len - KJpegHuffmanRootTableBits);
                    JpegHuffmanTableEntry code = new JpegHuffmanTableEntry(codeBits, (ushort)symbols[idx++]);
                    int reps                   = 1 << (tableBits - codeBits);

                    while (reps-- > 0)
                    {
                        lut[tableOffset + low++] = code;
                    }
                }
            }
        }

        private static int NextSubtableBitSize(ReadOnlySpan<int> count, int len)
        {
            int left = 1 << (len - KJpegHuffmanRootTableBits);

            while (len < JpegConstants.KJpegHuffmanMaxBitLength)
            {
                left -= count[len];

                if (left <= 0)
                {
                    break;
                }

                len++;
                left <<= 1;
            }

            return len - KJpegHuffmanRootTableBits;
        }
    }

    /// <summary>
    /// Mode flags for <see cref="EncJpegDataReader.ReadJpeg"/>.
    /// Ported from <c>jxl::jpeg::JpegReadMode</c> in <c>lib/jxl/jpeg/enc_jpeg_data_reader.h</c>.
    /// </summary>
    public enum JpegReadMode
    {
        /// <summary>Only parse JPEG headers (SOF, DQT, APP markers).</summary>
        kReadHeader = 0,

        /// <summary>Parse headers and tables without decoding entropy scan data.</summary>
        kReadTables = 1,

        /// <summary>Parse headers, tables, and decode all DCT coefficient scans.</summary>
        kReadAll = 2,
    }

    /// <summary>
    /// Parses a binary JPEG codestream into a <see cref="JPEGData"/> structure for lossless JPEG-to-JXL transcoding.
    /// Ported from <c>lib/jxl/jpeg/enc_jpeg_data_reader.h</c> and <c>lib/jxl/jpeg/enc_jpeg_data_reader.cc</c>.
    /// </summary>
    public static class EncJpegDataReader
    {
        private static readonly byte[] ValidMarkerMask =
        {
            1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
        };

        /// <summary>
        /// Parses a JPEG byte stream into <paramref name="jpg"/> according to <paramref name="mode"/>.
        /// Ported from <c>jxl::jpeg::ReadJpeg</c> in <c>lib/jxl/jpeg/enc_jpeg_data_reader.cc</c>.
        /// </summary>
        /// <param name="data">Input JPEG bytes.</param>
        /// <param name="mode">Read depth mode.</param>
        /// <param name="jpg">Destination <see cref="JPEGData"/>.</param>
        /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
        public static bool ReadJpeg(ReadOnlySpan<byte> data, JpegReadMode mode, JPEGData jpg)
        {
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                return false;
            }

            JpegReaderContext ctx = new JpegReaderContext();
            int pos               = 2;
            jpg.PaddingBits.Clear();

            while (true)
            {
                if (!ReadNextMarkerWithSkip(data, jpg, ref pos, out byte marker))
                {
                    return false;
                }

                if (!DispatchJpegMarker(data, mode, jpg, marker, ref pos, ctx))
                {
                    return false;
                }

                jpg.MarkerOrder.Add(marker);

                if (mode == JpegReadMode.kReadHeader && ctx.FoundSof)
                {
                    break;
                }

                if (marker == 0xD9)
                {
                    break;
                }
            }

            return FinalizeReadJpeg(data, mode, jpg, pos, ctx);
        }

        private sealed class JpegReaderContext
        {
            public readonly JpegHuffmanTableEntry[][] DcLuts = new JpegHuffmanTableEntry[4][];
            public readonly JpegHuffmanTableEntry[][] AcLuts = new JpegHuffmanTableEntry[4][];
            public readonly ushort[,] ScanProgression        = new ushort[4, 64];
            public bool FoundSof;
            public bool FoundSos;
            public bool FoundDri;
            public bool IsProgressive;

            public JpegReaderContext()
            {
                for (int i = 0; i < 4; i++)
                {
                    DcLuts[i] = new JpegHuffmanTableEntry[EncJpegHuffmanDecode.KJpegHuffmanLutSize];
                    AcLuts[i] = new JpegHuffmanTableEntry[EncJpegHuffmanDecode.KJpegHuffmanLutSize];
                }
            }
        }

        private static bool ReadNextMarkerWithSkip(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            ref int pos,
            out byte marker)
        {
            marker          = 0;
            int numSkipped  = FindNextMarker(data, pos);

            if (numSkipped > 0)
            {
                jpg.MarkerOrder.Add(0xFF);
                jpg.InterMarkerData.Add(data.Slice(pos, numSkipped).ToArray());
                pos += numSkipped;
            }

            if (pos + 2 > data.Length || data[pos] != 0xFF)
            {
                return false;
            }

            marker = data[pos + 1];
            pos   += 2;

            return true;
        }

        private static int FindNextMarker(ReadOnlySpan<byte> data, int pos)
        {
            int skipped = 0;

            while (pos + 1 < data.Length &&
                   (data[pos] != 0xFF || data[pos + 1] < 0xC0 || ValidMarkerMask[data[pos + 1] - 0xC0] == 0))
            {
                pos++;
                skipped++;
            }

            return skipped;
        }

        private static bool DispatchJpegMarker(
            ReadOnlySpan<byte> data,
            JpegReadMode mode,
            JPEGData jpg,
            byte marker,
            ref int pos,
            JpegReaderContext ctx)
        {
            if (marker >= 0xE0 && marker <= 0xEF)
            {
                return mode == JpegReadMode.kReadTables || ProcessAppMarker(data, jpg, marker, ref pos);
            }

            if (marker == 0xFE)
            {
                return mode == JpegReadMode.kReadTables || ProcessComMarker(data, jpg, ref pos);
            }

            if (marker == 0xDB)
            {
                return ProcessDqtMarker(data, jpg, ref pos);
            }

            if (marker == 0xC4)
            {
                return ProcessDhtMarker(data, mode, jpg, ref pos, ctx.DcLuts, ctx.AcLuts);
            }

            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
            {
                ctx.IsProgressive = marker == 0xC2;
                ctx.FoundSof      = true;

                return ProcessSofMarker(data, jpg, ref pos);
            }

            return DispatchControlOrScanMarker(data, mode, jpg, marker, ref pos, ctx);
        }

        private static bool DispatchControlOrScanMarker(
            ReadOnlySpan<byte> data,
            JpegReadMode mode,
            JPEGData jpg,
            byte marker,
            ref int pos,
            JpegReaderContext ctx)
        {
            if (marker == 0xDD)
            {
                return ProcessDriMarker(data, jpg, ref pos, ref ctx.FoundDri);
            }

            if (marker == 0xDA)
            {
                ctx.FoundSos = true;

                return mode == JpegReadMode.kReadAll
                    ? ProcessScan(data, jpg, ref pos, ctx)
                    : SkipSosMarkerHeader(data, ref pos);
            }

            if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0xD9)
            {
                return true;
            }

            return false;
        }

        private static bool FinalizeReadJpeg(
            ReadOnlySpan<byte> data,
            JpegReadMode mode,
            JPEGData jpg,
            int pos,
            JpegReaderContext ctx)
        {
            if (!ctx.FoundSof)
            {
                return false;
            }

            if (mode == JpegReadMode.kReadHeader)
            {
                return true;
            }

            if (!ctx.FoundSos)
            {
                return false;
            }

            if (mode == JpegReadMode.kReadAll)
            {
                if (pos < data.Length)
                {
                    jpg.TailData = data.Slice(pos).ToArray();
                }

                if (!FixupQuantIndexes(jpg))
                {
                    return false;
                }

                if (jpg.HuffmanCode.Count == 0 || jpg.HuffmanCode.Count >= JpegConstants.KMaxDHTMarkers)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixupQuantIndexes(JPEGData jpg)
        {
            for (int i = 0; i < jpg.Components.Count; i++)
            {
                JPEGComponent comp = jpg.Components[i];
                bool found         = false;

                for (int j = 0; j < jpg.Quant.Count; j++)
                {
                    if ((int)jpg.Quant[j].Index == comp.QuantIdx)
                    {
                        comp.QuantIdx = (uint)j;
                        found         = true;

                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ProcessAppMarker(ReadOnlySpan<byte> data, JPEGData jpg, byte marker, ref int pos)
        {
            if (pos + 2 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];

            if (len < 2 || pos + len > data.Length)
            {
                return false;
            }

            byte[] blob = new byte[len + 1];
            blob[0]     = marker;
            data.Slice(pos, len).CopyTo(blob.AsSpan(1));
            jpg.AppData.Add(blob);
            jpg.AppMarkerTypes.Add(ClassifyAppMarker(blob));
            pos += len;

            return true;
        }

        private static AppMarkerType ClassifyAppMarker(ReadOnlySpan<byte> blob)
        {
            if (blob.Length >= 9 && blob[0] == 0xE1 &&
                blob[3] == (byte)'E' && blob[4] == (byte)'x' && blob[5] == (byte)'i' && blob[6] == (byte)'f')
            {
                return AppMarkerType.Exif;
            }

            if (blob.Length >= 17 && blob[0] == 0xE2 &&
                blob[3] == (byte)'I' && blob[4] == (byte)'C' && blob[5] == (byte)'C' && blob[6] == (byte)'_')
            {
                return AppMarkerType.ICC;
            }

            return AppMarkerType.Unknown;
        }

        private static bool ProcessComMarker(ReadOnlySpan<byte> data, JPEGData jpg, ref int pos)
        {
            if (pos + 2 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];

            if (len < 2 || pos + len > data.Length)
            {
                return false;
            }

            byte[] blob = new byte[len + 1];
            blob[0]     = 0xFE;
            data.Slice(pos, len).CopyTo(blob.AsSpan(1));
            jpg.ComData.Add(blob);
            pos += len;

            return true;
        }

        private static bool ProcessDqtMarker(ReadOnlySpan<byte> data, JPEGData jpg, ref int pos)
        {
            if (pos + 2 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];
            int end = pos + len;

            if (len <= 2 || end > data.Length)
            {
                return false;
            }

            pos += 2;

            while (pos < end && jpg.Quant.Count < JpegConstants.KMaxQuantTables)
            {
                if (!ReadOneQuantTable(data, end, ref pos, jpg))
                {
                    return false;
                }
            }

            return pos == end;
        }

        private static bool ReadOneQuantTable(ReadOnlySpan<byte> data, int end, ref int pos, JPEGData jpg)
        {
            byte info = data[pos++];
            int prec  = info >> 4;
            int idx   = info & 0x0F;

            if (idx >= 4 || prec > 1 || pos + (64 * (prec + 1)) > end)
            {
                return false;
            }

            JPEGQuantTable qt = new JPEGQuantTable
            {
                Index     = (uint)idx,
                Precision = (uint)prec,
            };

            for (int k = 0; k < 64; k++)
            {
                int val = prec != 0 ? ((data[pos] << 8) | data[pos + 1]) : data[pos];
                pos    += prec + 1;

                if (val < 1 || val > 65535)
                {
                    return false;
                }

                qt.Values[(int)JpegConstants.KJPEGNaturalOrder[k]] = val;
            }

            qt.IsLast = pos == end;
            jpg.Quant.Add(qt);

            return true;
        }

        private static bool ProcessDhtMarker(
            ReadOnlySpan<byte> data,
            JpegReadMode mode,
            JPEGData jpg,
            ref int pos,
            JpegHuffmanTableEntry[][] dcLuts,
            JpegHuffmanTableEntry[][] acLuts)
        {
            if (pos + 2 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];
            int end = pos + len;

            if (len < 2 || end > data.Length)
            {
                return false;
            }

            pos += 2;

            if (len == 2)
            {
                jpg.HuffmanCode.Add(new JPEGHuffmanCode { IsLast = true });

                return true;
            }

            while (pos < end)
            {
                if (!ReadOneHuffmanTable(data, mode, end, ref pos, jpg, dcLuts, acLuts))
                {
                    return false;
                }
            }

            return pos == end;
        }

        private static bool ReadOneHuffmanTable(
            ReadOnlySpan<byte> data,
            JpegReadMode mode,
            int end,
            ref int pos,
            JPEGData jpg,
            JpegHuffmanTableEntry[][] dcLuts,
            JpegHuffmanTableEntry[][] acLuts)
        {
            if (pos + 1 + JpegConstants.KJpegHuffmanMaxBitLength > end)
            {
                return false;
            }

            byte slotInfo    = data[pos++];
            bool isAcTable   = (slotInfo & 0x10) != 0;
            int huffmanIndex = isAcTable ? slotInfo - 0x10 : slotInfo;

            if (huffmanIndex < 0 || huffmanIndex > 3)
            {
                return false;
            }

            JPEGHuffmanCode huff = new JPEGHuffmanCode { SlotId = slotInfo };

            if (!ReadHuffmanCountsAndValues(data, end, isAcTable, ref pos, huff))
            {
                return false;
            }

            huff.IsLast = pos == end;

            if (mode == JpegReadMode.kReadAll)
            {
                JpegHuffmanTableEntry[] lut = isAcTable ? acLuts[huffmanIndex] : dcLuts[huffmanIndex];
                EncJpegHuffmanDecode.BuildJpegHuffmanTable(huff.Counts, huff.Values, lut);
            }

            jpg.HuffmanCode.Add(huff);

            return true;
        }

        private static bool ReadHuffmanCountsAndValues(
            ReadOnlySpan<byte> data,
            int end,
            bool isAcTable,
            ref int pos,
            JPEGHuffmanCode huff)
        {
            int totalCount = 0;
            int space      = 1 << JpegConstants.KJpegHuffmanMaxBitLength;
            int maxDepth   = 1;

            for (int i = 1; i <= JpegConstants.KJpegHuffmanMaxBitLength; i++)
            {
                int count = data[pos++];

                if (count != 0)
                {
                    maxDepth = i;
                }

                huff.Counts[i] = (uint)count;
                totalCount    += count;
                space         -= count * (1 << (JpegConstants.KJpegHuffmanMaxBitLength - i));
            }

            int maxAlphabet = isAcTable ? JpegConstants.KJpegHuffmanAlphabetSize : JpegConstants.KJpegDCAlphabetSize;

            if (totalCount < 0 || totalCount > maxAlphabet || pos + totalCount > end)
            {
                return false;
            }

            if (!ReadAndValidateHuffmanValues(data, isAcTable, totalCount, ref pos, huff))
            {
                return false;
            }

            huff.Counts[maxDepth]++;
            huff.Values[totalCount] = JpegConstants.KJpegHuffmanAlphabetSize;
            space                  -= 1 << (JpegConstants.KJpegHuffmanMaxBitLength - maxDepth);

            return space >= 0;
        }

        private static bool ReadAndValidateHuffmanValues(
            ReadOnlySpan<byte> data,
            bool isAcTable,
            int totalCount,
            ref int pos,
            JPEGHuffmanCode huff)
        {
            Span<bool> seen = stackalloc bool[256];

            for (int i = 0; i < totalCount; i++)
            {
                byte value = data[pos++];

                if (!isAcTable && value >= JpegConstants.KJpegDCAlphabetSize)
                {
                    return false;
                }

                if (seen[value])
                {
                    return false;
                }

                seen[value]    = true;
                huff.Values[i] = value;
            }

            return true;
        }

        private static bool ProcessSofMarker(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            ref int pos)
        {
            if (jpg.Width != 0 || pos + 8 > data.Length)
            {
                return false;
            }

            int len       = (data[pos] << 8) | data[pos + 1];
            int precision = data[pos + 2];
            jpg.Height    = (data[pos + 3] << 8) | data[pos + 4];
            jpg.Width     = (data[pos + 5] << 8) | data[pos + 6];
            int numComps  = data[pos + 7];

            if (precision != 8 || jpg.Width <= 0 || jpg.Height <= 0 || numComps < 1 || numComps > 4)
            {
                return false;
            }

            if (len != 8 + (3 * numComps) || pos + len > data.Length)
            {
                return false;
            }

            pos += 8;

            return PopulateSofComponents(data, jpg, numComps, ref pos);
        }

        private static bool PopulateSofComponents(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            int numComps,
            ref int pos)
        {
            int maxH            = 1;
            int maxV            = 1;
            Span<bool> idsSeen  = stackalloc bool[256];
            jpg.Components.Clear();

            for (int i = 0; i < numComps; i++)
            {
                byte id = data[pos];

                if (idsSeen[id])
                {
                    return false;
                }

                idsSeen[id] = true;
                int hSamp   = data[pos + 1] >> 4;
                int vSamp   = data[pos + 1] & 0x0F;

                if (hSamp < 1 || hSamp > 15 || vSamp < 1 || vSamp > 15)
                {
                    return false;
                }

                JPEGComponent comp = new JPEGComponent
                {
                    Id          = id,
                    HSampFactor = hSamp,
                    VSampFactor = vSamp,
                    QuantIdx    = data[pos + 2],
                };
                pos += 3;
                maxH = Math.Max(maxH, hSamp);
                maxV = Math.Max(maxV, vSamp);
                jpg.Components.Add(comp);
            }

            return ComputeSofBlockDimensions(jpg, maxH, maxV);
        }

        private static bool ComputeSofBlockDimensions(JPEGData jpg, int maxH, int maxV)
        {
            int mcuCols = (jpg.Width + (maxH * 8) - 1) / (maxH * 8);
            int mcuRows = (jpg.Height + (maxV * 8) - 1) / (maxV * 8);

            for (int i = 0; i < jpg.Components.Count; i++)
            {
                JPEGComponent comp = jpg.Components[i];

                if ((maxH % comp.HSampFactor) != 0 || (maxV % comp.VSampFactor) != 0)
                {
                    return false;
                }

                comp.WidthInBlocks  = (uint)(mcuCols * comp.HSampFactor);
                comp.HeightInBlocks = (uint)(mcuRows * comp.VSampFactor);
            }

            return true;
        }

        private static bool ProcessDriMarker(ReadOnlySpan<byte> data, JPEGData jpg, ref int pos, ref bool foundDri)
        {
            if (foundDri || pos + 4 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];

            if (len != 4)
            {
                return false;
            }

            foundDri            = true;
            jpg.RestartInterval = (uint)((data[pos + 2] << 8) | data[pos + 3]);
            pos                += 4;

            return true;
        }

        private static bool SkipSosMarkerHeader(ReadOnlySpan<byte> data, ref int pos)
        {
            if (pos + 2 > data.Length)
            {
                return false;
            }

            int len = (data[pos] << 8) | data[pos + 1];

            if (len < 6 || pos + len > data.Length)
            {
                return false;
            }

            pos += len;

            return true;
        }

        private static bool ProcessSosHeader(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            ref int pos,
            out JPEGScanInfo scanInfo)
        {
            scanInfo = new JPEGScanInfo();

            if (pos + 3 > data.Length)
            {
                return false;
            }

            int len      = (data[pos] << 8) | data[pos + 1];
            int numComps = data[pos + 2];

            if (len != 6 + (2 * numComps) || pos + len > data.Length || numComps < 1 || numComps > jpg.Components.Count)
            {
                return false;
            }

            scanInfo.NumComponents = (uint)numComps;
            pos                   += 3;

            if (!ParseSosComponentRefs(data, jpg, numComps, ref pos, scanInfo))
            {
                return false;
            }

            scanInfo.Ss = data[pos++];
            scanInfo.Se = data[pos++];
            byte ahAl   = data[pos++];
            scanInfo.Ah = (uint)(ahAl >> 4);
            scanInfo.Al = (uint)(ahAl & 0x0F);

            if (scanInfo.Ss > 63 || scanInfo.Se < scanInfo.Ss || scanInfo.Se > 63)
            {
                return false;
            }

            jpg.ScanInfo.Add(scanInfo);

            return true;
        }

        private static bool ParseSosComponentRefs(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            int numComps,
            ref int pos,
            JPEGScanInfo scanInfo)
        {
            Span<bool> idsSeen = stackalloc bool[256];

            for (int i = 0; i < numComps; i++)
            {
                byte compId = data[pos++];
                byte tbls   = data[pos++];
                int compIdx = FindComponentIndex(jpg, compId);

                if (idsSeen[compId] || compIdx < 0)
                {
                    return false;
                }

                idsSeen[compId] = true;
                uint dcIdx      = (uint)(tbls >> 4);
                uint acIdx      = (uint)(tbls & 0x0F);

                if (dcIdx > 3 || acIdx > 3)
                {
                    return false;
                }

                scanInfo.Components[i] = new JPEGComponentScanInfo
                {
                    CompIdx  = (uint)compIdx,
                    DcTblIdx = dcIdx,
                    AcTblIdx = acIdx,
                };
            }

            return true;
        }

        private static int FindComponentIndex(JPEGData jpg, uint compId)
        {
            for (int i = 0; i < jpg.Components.Count; i++)
            {
                if (jpg.Components[i].Id == compId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ProcessScan(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            ref int pos,
            JpegReaderContext ctx)
        {
            if (!ProcessSosHeader(data, jpg, ref pos, out JPEGScanInfo scanInfo))
            {
                return false;
            }

            if (!ValidateAndMarkScanProgression(scanInfo, ctx) || !EnsureComponentCoeffsAllocated(jpg))
            {
                return false;
            }

            ComputeScanMcuGrid(jpg, scanInfo, out int mcusPerRow, out int mcuRows);

            ScanDecodeState scanState = new ScanDecodeState(data.Length, pos, (int)jpg.RestartInterval);
            scanState.Br.Reset(data, pos);

            for (int mcuY = 0; mcuY < mcuRows; mcuY++)
            {
                for (int mcuX = 0; mcuX < mcusPerRow; mcuX++)
                {
                    if (!HandleMcuRestartIfNeeded(data, jpg, ref scanState))
                    {
                        return false;
                    }

                    if (!DecodeOneMcuInScan(data, jpg, scanInfo, (mcuX, mcuY), ctx, ref scanState))
                    {
                        return false;
                    }
                }
            }

            if (scanState.EobRun > 0 || !scanState.Br.FinishStream(data, jpg, out pos) || pos > data.Length)
            {
                return false;
            }

            return true;
        }

        private static bool ValidateAndMarkScanProgression(JPEGScanInfo scanInfo, JpegReaderContext ctx)
        {
            int al = ctx.IsProgressive ? (int)scanInfo.Al : 0;
            int ah = ctx.IsProgressive ? (int)scanInfo.Ah : 0;
            int ss = ctx.IsProgressive ? (int)scanInfo.Ss : 0;
            int se = ctx.IsProgressive ? (int)scanInfo.Se : 63;

            if (al > 10)
            {
                return false;
            }

            ushort scanBitmask       = ah == 0 ? (ushort)(0xFFFFu << al) : (ushort)(1u << al);
            ushort refinementBitmask = (ushort)((1 << al) - 1);

            for (int i = 0; i < (int)scanInfo.NumComponents; i++)
            {
                int compIdx = (int)scanInfo.Components[i].CompIdx;

                for (int k = ss; k <= se; k++)
                {
                    if ((ctx.ScanProgression[compIdx, k] & (scanBitmask | refinementBitmask)) != 0)
                    {
                        return false;
                    }

                    ctx.ScanProgression[compIdx, k] |= scanBitmask;
                }
            }

            return true;
        }

        private static bool EnsureComponentCoeffsAllocated(JPEGData jpg)
        {
            for (int i = 0; i < jpg.Components.Count; i++)
            {
                JPEGComponent comp = jpg.Components[i];

                if (comp.Coeffs.Length == 0)
                {
                    comp.Coeffs = new short[comp.WidthInBlocks * comp.HeightInBlocks * 64];
                }
            }

            return true;
        }

        private static void ComputeScanMcuGrid(
            JPEGData jpg,
            JPEGScanInfo scanInfo,
            out int mcusPerRow,
            out int mcuRows)
        {
            int maxH = 1;
            int maxV = 1;

            for (int i = 0; i < jpg.Components.Count; i++)
            {
                maxH = Math.Max(maxH, jpg.Components[i].HSampFactor);
                maxV = Math.Max(maxV, jpg.Components[i].VSampFactor);
            }

            if (scanInfo.NumComponents > 1)
            {
                mcusPerRow = (jpg.Width + (maxH * 8) - 1) / (maxH * 8);
                mcuRows    = (jpg.Height + (maxV * 8) - 1) / (maxV * 8);
            }
            else
            {
                JPEGComponent c = jpg.Components[(int)scanInfo.Components[0].CompIdx];
                int divH        = 8 * maxH;
                int divV        = 8 * maxV;
                mcusPerRow      = ((jpg.Width * c.HSampFactor) + divH - 1) / divH;
                mcuRows         = ((jpg.Height * c.VSampFactor) + divV - 1) / divV;
            }
        }

        private struct ScanDecodeState
        {
            public JpegScanBitReader Br;
            public readonly short[] DcPred;
            public int RestartsToGo;
            public int NextRestartMarker;
            public int EobRun;
            public int BlockScanIndex;

            public ScanDecodeState(int dataLen, int startPos, int restartInterval)
            {
                Br                = new JpegScanBitReader(dataLen, startPos);
                DcPred            = new short[4];
                RestartsToGo      = restartInterval;
                NextRestartMarker = 0;
                EobRun            = -1;
                BlockScanIndex    = 0;
            }

            public readonly ref short GetDcPred(int compIdx) => ref DcPred[compIdx];

            public readonly void ClearDcPreds() => Array.Clear(DcPred, 0, DcPred.Length);
        }

        private static bool HandleMcuRestartIfNeeded(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            ref ScanDecodeState state)
        {
            if (jpg.RestartInterval == 0)
            {
                return true;
            }

            if (state.RestartsToGo == 0)
            {
                if (!state.Br.FinishStream(data, jpg, out int pos))
                {
                    return false;
                }

                int expected = 0xD0 + state.NextRestartMarker;

                if (pos + 2 > data.Length || data[pos] != 0xFF || data[pos + 1] != expected || state.EobRun > 0)
                {
                    return false;
                }

                state.Br.Reset(data, pos + 2);
                state.NextRestartMarker = (state.NextRestartMarker + 1) & 0x07;
                state.RestartsToGo      = (int)jpg.RestartInterval;
                state.ClearDcPreds();
                state.EobRun = -1;
            }

            state.RestartsToGo--;

            return true;
        }

        private static bool DecodeOneMcuInScan(
            ReadOnlySpan<byte> data,
            JPEGData jpg,
            JPEGScanInfo scanInfo,
            (int McuX, int McuY) mcu,
            JpegReaderContext ctx,
            ref ScanDecodeState state)
        {
            bool isInterleaved = scanInfo.NumComponents > 1;
            int al             = ctx.IsProgressive ? (int)scanInfo.Al : 0;
            int ah             = ctx.IsProgressive ? (int)scanInfo.Ah : 0;
            int ss             = ctx.IsProgressive ? (int)scanInfo.Ss : 0;
            int se             = ctx.IsProgressive ? (int)scanInfo.Se : 63;

            for (int i = 0; i < (int)scanInfo.NumComponents; i++)
            {
                JPEGComponentScanInfo si = scanInfo.Components[i];
                JPEGComponent comp       = jpg.Components[(int)si.CompIdx];
                int nblocksY             = isInterleaved ? comp.VSampFactor : 1;
                int nblocksX             = isInterleaved ? comp.HSampFactor : 1;

                for (int iy = 0; iy < nblocksY; iy++)
                {
                    for (int ix = 0; ix < nblocksX; ix++)
                    {
                        int bx      = (mcu.McuX * nblocksX) + ix;
                        int by      = (mcu.McuY * nblocksY) + iy;
                        int blkBase = (int)(((by * comp.WidthInBlocks) + bx) * 64);

                        if (!DecodeOrRefineSingleBlock(data, scanInfo, si, (ss, se, ah, al), ctx, ref state, comp.Coeffs.AsSpan(blkBase, 64)))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool DecodeOrRefineSingleBlock(
            ReadOnlySpan<byte> data,
            JPEGScanInfo scanInfo,
            JPEGComponentScanInfo si,
            (int Ss, int Se, int Ah, int Al) band,
            JpegReaderContext ctx,
            ref ScanDecodeState state,
            Span<short> coeffs)
        {
            bool resetState = false;
            int numZeroRuns = 0;
            bool ok         = band.Ah == 0
                ? DecodeDctBlock(
                    data,
                    ctx.DcLuts[si.DcTblIdx],
                    ctx.AcLuts[si.AcTblIdx],
                    (band.Ss, band.Se, band.Al),
                    ref state,
                    (int)si.CompIdx,
                    coeffs,
                    out resetState,
                    out numZeroRuns)
                : RefineDctBlock(
                    data,
                    ctx.AcLuts[si.AcTblIdx],
                    (band.Ss, band.Se, band.Al),
                    ref state,
                    coeffs,
                    out resetState);

            if (!ok)
            {
                return false;
            }

            if (resetState)
            {
                scanInfo.ResetPoints.Add((uint)state.BlockScanIndex);
            }

            if (numZeroRuns > 0)
            {
                scanInfo.ExtraZeroRuns.Add(new JPEGScanInfo.ExtraZeroRunInfo
                {
                    BlockIdx         = (uint)state.BlockScanIndex,
                    NumExtraZeroRuns = (uint)numZeroRuns,
                });
            }

            state.BlockScanIndex++;

            return true;
        }

        private static bool DecodeDctBlock(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> dcHuff,
            ReadOnlySpan<JpegHuffmanTableEntry> acHuff,
            (int Ss, int Se, int Al) band,
            ref ScanDecodeState state,
            int compIdx,
            Span<short> coeffs,
            out bool resetState,
            out int numZeroRuns)
        {
            resetState        = false;
            numZeroRuns       = 0;
            int ss            = band.Ss;
            int se            = band.Se;
            int al            = band.Al;
            int am            = 1 << al;
            bool eobAllowed   = ss > 0;

            if (ss == 0)
            {
                if (!DecodeDcCoefficient(data, dcHuff, am, ref state.Br, ref state.GetDcPred(compIdx), coeffs))
                {
                    return false;
                }

                ss++;
            }

            if (ss > se)
            {
                return true;
            }

            if (state.EobRun > 0)
            {
                state.EobRun--;

                return true;
            }

            return DecodeAcFirstBand(data, acHuff, (ss, se, al, am, eobAllowed), ref state, coeffs, out resetState, out numZeroRuns);
        }

        private static bool DecodeDcCoefficient(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> dcHuff,
            int am,
            ref JpegScanBitReader br,
            ref short lastDcCoeff,
            Span<short> coeffs)
        {
            int s = ReadSymbol(data, dcHuff, ref br);

            if (s >= JpegConstants.KJpegDCAlphabetSize)
            {
                return false;
            }

            int diff = 0;

            if (s > 0)
            {
                int bits = br.ReadBits(data, s);
                diff     = HuffExtend(bits, s);
            }

            int coeff   = diff + lastDcCoeff;
            int dcCoeff = coeff * am;
            coeffs[0]   = (short)dcCoeff;

            if (dcCoeff != coeffs[0])
            {
                return false;
            }

            lastDcCoeff = (short)coeff;

            return true;
        }

        private static bool DecodeAcFirstBand(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> acHuff,
            (int Ss, int Se, int Al, int Am, bool EobAllowed) band,
            ref ScanDecodeState state,
            Span<short> coeffs,
            out bool resetState,
            out int numZeroRuns)
        {
            resetState  = false;
            numZeroRuns = 0;

            for (int k = band.Ss; k <= band.Se; k++)
            {
                int sr = ReadSymbol(data, acHuff, ref state.Br);

                if (sr >= JpegConstants.KJpegHuffmanAlphabetSize)
                {
                    return false;
                }

                int r = sr >> 4;
                int s = sr & 15;

                if (s > 0)
                {
                    k += r;

                    if (k > band.Se || s + band.Al >= JpegConstants.KJpegDCAlphabetSize)
                    {
                        return false;
                    }

                    int bits                                        = state.Br.ReadBits(data, s);
                    int coeff                                       = HuffExtend(bits, s);
                    coeffs[(int)JpegConstants.KJPEGNaturalOrder[k]] = (short)(coeff * band.Am);
                    numZeroRuns                                     = 0;
                }
                else if (r == 15)
                {
                    k += 15;
                    numZeroRuns++;
                }
                else
                {
                    if (!ReadEobRun(data, r, k == band.Ss, band.EobAllowed, ref state, ref resetState))
                    {
                        return false;
                    }

                    break;
                }
            }

            state.EobRun--;

            return true;
        }

        private static bool ReadEobRun(
            ReadOnlySpan<byte> data,
            int r,
            bool atBandStart,
            bool eobAllowed,
            ref ScanDecodeState state,
            ref bool resetState)
        {
            if (eobAllowed && atBandStart && state.EobRun == 0)
            {
                resetState = true;
            }

            state.EobRun = 1 << r;

            if (r > 0)
            {
                if (!eobAllowed)
                {
                    return false;
                }

                state.EobRun += state.Br.ReadBits(data, r);
            }

            return true;
        }

        private static bool RefineDctBlock(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> acHuff,
            (int Ss, int Se, int Al) band,
            ref ScanDecodeState state,
            Span<short> coeffs,
            out bool resetState)
        {
            resetState      = false;
            int ss          = band.Ss;
            int se          = band.Se;
            int am          = 1 << band.Al;
            bool eobAllowed = ss > 0;

            if (ss == 0)
            {
                int bit    = state.Br.ReadBits(data, 1);
                coeffs[0] |= (short)(bit * am);
                ss++;
            }

            if (ss > se)
            {
                return true;
            }

            int k = ss;

            if (state.EobRun <= 0 &&
                !RefineAcSymbolsLoop(data, acHuff, (ss, se, am, eobAllowed), ref k, ref state, coeffs, ref resetState))
            {
                return false;
            }

            if (state.EobRun > 0)
            {
                RefineRemainingInBand(data, k, se, am, -am, ref state.Br, coeffs);
            }

            state.EobRun--;

            return true;
        }

        private static bool RefineAcSymbolsLoop(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> acHuff,
            (int Ss, int Se, int Am, bool EobAllowed) band,
            ref int k,
            ref ScanDecodeState state,
            Span<short> coeffs,
            ref bool resetState)
        {
            int p1          = band.Am;
            int m1          = -band.Am;
            bool inZeroRun  = false;

            for (; k <= band.Se; k++)
            {
                int sym = ReadSymbol(data, acHuff, ref state.Br);

                if (sym >= JpegConstants.KJpegHuffmanAlphabetSize)
                {
                    return false;
                }

                int r = sym >> 4;
                int s = sym & 15;

                if (s != 0)
                {
                    if (s != 1)
                    {
                        return false;
                    }

                    s         = state.Br.ReadBits(data, 1) != 0 ? p1 : m1;
                    inZeroRun = false;
                }
                else if (r != 15)
                {
                    return ReadEobRun(data, r, k == band.Ss, band.EobAllowed, ref state, ref resetState) && !inZeroRun;
                }
                else
                {
                    inZeroRun = true;
                }

                AdvanceRefinementRun(data, band.Se, p1, m1, ref k, ref r, ref state.Br, coeffs);

                if (s != 0)
                {
                    if (k > band.Se)
                    {
                        return false;
                    }

                    coeffs[(int)JpegConstants.KJPEGNaturalOrder[k]] = (short)s;
                }
            }

            return !inZeroRun;
        }

        private static void AdvanceRefinementRun(
            ReadOnlySpan<byte> data,
            int se,
            int p1,
            int m1,
            ref int k,
            ref int r,
            ref JpegScanBitReader br,
            Span<short> coeffs)
        {
            do
            {
                int natIdx     = (int)JpegConstants.KJPEGNaturalOrder[k];
                short thisCoef = coeffs[natIdx];

                if (thisCoef != 0)
                {
                    if (br.ReadBits(data, 1) != 0 && (thisCoef & p1) == 0)
                    {
                        thisCoef = (short)(thisCoef >= 0 ? thisCoef + p1 : thisCoef + m1);
                    }

                    coeffs[natIdx] = thisCoef;
                }
                else if (--r < 0)
                {
                    break;
                }

                k++;
            }
            while (k <= se);
        }

        private static void RefineRemainingInBand(
            ReadOnlySpan<byte> data,
            int startK,
            int se,
            int p1,
            int m1,
            ref JpegScanBitReader br,
            Span<short> coeffs)
        {
            for (int k = startK; k <= se; k++)
            {
                int natIdx     = (int)JpegConstants.KJPEGNaturalOrder[k];
                short thisCoef = coeffs[natIdx];

                if (thisCoef != 0)
                {
                    if (br.ReadBits(data, 1) != 0 && (thisCoef & p1) == 0)
                    {
                        thisCoef = (short)(thisCoef >= 0 ? thisCoef + p1 : thisCoef + m1);
                    }

                    coeffs[natIdx] = thisCoef;
                }
            }
        }

        private static int ReadSymbol(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<JpegHuffmanTableEntry> table,
            ref JpegScanBitReader br)
        {
            br.FillBitWindow(data);
            int val                     = (int)((br.Val >> (br.BitsLeft - 8)) & 0xFFul);
            int tableIdx                = val;
            JpegHuffmanTableEntry entry = table[tableIdx];
            int nbits                   = entry.Bits - 8;

            if (nbits > 0)
            {
                br.BitsLeft -= 8;
                tableIdx    += entry.Value;
                val          = (int)((br.Val >> (br.BitsLeft - nbits)) & ((1ul << nbits) - 1ul));
                entry        = table[tableIdx + val];
            }

            br.BitsLeft -= entry.Bits;

            return entry.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HuffExtend(int x, int s)
        {
            int half = 1 << (s - 1);

            return x >= half ? x : x - (1 << s) + 1;
        }

        private struct JpegScanBitReader
        {
            public readonly int Len;
            public int Pos;
            public ulong Val;
            public int BitsLeft;
            public int NextMarkerPos;

            public JpegScanBitReader(int len, int startPos)
            {
                Len           = len;
                Pos           = startPos;
                Val           = 0;
                BitsLeft      = 0;
                NextMarkerPos = len - 2;
            }

            public void Reset(ReadOnlySpan<byte> data, int pos)
            {
                Pos           = pos;
                Val           = 0;
                BitsLeft      = 0;
                NextMarkerPos = Len - 2;
                FillBitWindow(data);
            }

            private byte GetNextByte(ReadOnlySpan<byte> data)
            {
                if (Pos >= NextMarkerPos)
                {
                    Pos++;

                    return 0;
                }

                byte c = data[Pos++];

                if (c == 0xFF)
                {
                    byte escape = data[Pos];

                    if (escape == 0)
                    {
                        Pos++;
                    }
                    else
                    {
                        NextMarkerPos = Pos - 1;
                    }
                }

                return c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void FillBitWindow(ReadOnlySpan<byte> data)
            {
                if (BitsLeft <= 16)
                {
                    while (BitsLeft <= 56)
                    {
                        Val       = (Val << 8) | GetNextByte(data);
                        BitsLeft += 8;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ReadBits(ReadOnlySpan<byte> data, int nbits)
            {
                FillBitWindow(data);
                int val   = (int)((Val >> (BitsLeft - nbits)) & ((1ul << nbits) - 1ul));
                BitsLeft -= nbits;

                return val;
            }

            public bool FinishStream(ReadOnlySpan<byte> data, JPEGData jpg, out int nextPos)
            {
                int npadbits = BitsLeft & 7;

                if (npadbits > 0)
                {
                    ulong padmask = (1ul << npadbits) - 1ul;
                    ulong padbits = (Val >> (BitsLeft - npadbits)) & padmask;

                    if (padbits != padmask)
                    {
                        jpg.HasZeroPaddingBit = true;
                    }

                    for (int i = npadbits - 1; i >= 0; i--)
                    {
                        jpg.PaddingBits.Add((byte)((padbits >> i) & 1ul));
                    }
                }

                int unusedBytesLeft = BitsLeft >> 3;

                while (unusedBytesLeft-- > 0)
                {
                    Pos--;

                    if (Pos < NextMarkerPos && data[Pos] == 0 && data[Pos - 1] == 0xFF)
                    {
                        Pos--;
                    }
                }

                nextPos = Pos;

                return Pos <= NextMarkerPos;
            }
        }
    }

    /// <summary>
    /// Extracted Exif, XMP, and JUMBF metadata blobs from a JPEG codestream.
    /// Ported from <c>jxl::jpeg::Blobs</c> in <c>lib/jxl/jpeg/enc_jpeg_data.h</c>.
    /// </summary>
    public sealed class JpegBlobs
    {
        /// <summary>Exif payload bytes.</summary>
        public byte[] Exif = Array.Empty<byte>();

        /// <summary>XMP payload bytes.</summary>
        public byte[] Xmp = Array.Empty<byte>();

        /// <summary>JUMBF payload bytes.</summary>
        public byte[] Jumbf = Array.Empty<byte>();
    }

    /// <summary>
    /// Serializes <see cref="JPEGData"/> into a JXL <c>jbrd</c> reconstruction box and maps JPEG metadata to JXL headers.
    /// Ported from <c>lib/jxl/jpeg/enc_jpeg_data.h</c> and <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
    /// </summary>
    public static class EncJpegData
    {
        private static readonly byte[] IccProfileTag =
        {
            (byte)'I', (byte)'C', (byte)'C', (byte)'_', (byte)'P', (byte)'R',
            (byte)'O', (byte)'F', (byte)'I', (byte)'L', (byte)'E', 0,
        };

        private static readonly byte[] ExifTag =
        {
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
        };

        private static readonly byte[] XmpTag =
        {
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)':', (byte)'/', (byte)'/',
            (byte)'n', (byte)'s', (byte)'.', (byte)'a', (byte)'d', (byte)'o', (byte)'b',
            (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m', (byte)'/', (byte)'x',
            (byte)'a', (byte)'p', (byte)'/', (byte)'1', (byte)'.', (byte)'0', (byte)'/', 0,
        };

        /// <summary>
        /// Serializes <paramref name="jpegData"/> into a <c>jbrd</c> bitstream (optionally Brotli-compressed).
        /// Ported from <c>jxl::jpeg::EncodeJPEGData</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status EncodeJPEGData(
            JxlMemoryManager? memoryManager,
            JPEGData jpegData,
            out byte[] bytes,
            CompressParams cparams)
        {
            bytes = Array.Empty<byte>();
            jpegData.AppMarkerTypes.Clear();

            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                jpegData.AppMarkerTypes.Add(AppMarkerType.Unknown);
            }

            Status iccOk = DetectIccProfile(jpegData);

            if (!iccOk)
            {
                return iccOk;
            }

            DetectBlobs(jpegData);

            JxlMemoryManager mm = memoryManager ?? JxlMemoryManager.Default;
            BitWriter writer    = new BitWriter(mm);
            Status visitOk      = Bundle.Write(jpegData, writer, LayerType.Header, null);

            if (!visitOk)
            {
                return visitOk;
            }

            List<byte> unbrotli = new List<byte>();
            GatherUnbrotliBytes(jpegData, unbrotli);
            writer.ZeroPadToByte();

            ReadOnlySpan<byte> headerSpan = writer.GetSpan();
            int effort                    = cparams.BrotliEffort < 0 ? 11 - (int)cparams.SpeedTier : cparams.BrotliEffort;

            if (!CompressBrotliPayload(unbrotli, effort, out byte[] brotliBytes))
            {
                return Status.Failure("Brotli compression of JPEG data failed");
            }

            bytes = new byte[headerSpan.Length + brotliBytes.Length];
            headerSpan.CopyTo(bytes);
            brotliBytes.CopyTo(bytes.AsSpan(headerSpan.Length));

            return Status.OkStatus;
        }

        private static Status DetectIccProfile(JPEGData jpegData)
        {
            int numIcc     = 0;
            int numIccJpeg = 0;

            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                byte[] app = jpegData.AppData[i];

                if (app.Length < 3 || app[0] != 0xE2)
                {
                    continue;
                }

                int tagLength = (app[1] << 8) + app[2];

                if (tagLength < 2 + IccProfileTag.Length + 2 ||
                    !app.AsSpan(3, IccProfileTag.Length).SequenceEqual(IccProfileTag))
                {
                    continue;
                }

                byte chunkId   = app[3 + IccProfileTag.Length];
                byte numChunks = app[3 + IccProfileTag.Length + 1];

                if (chunkId != numIcc + 1 || (numIccJpeg != 0 && numIccJpeg != numChunks))
                {
                    continue;
                }

                numIccJpeg                 = numChunks;
                numIcc++;
                jpegData.AppMarkerTypes[i] = AppMarkerType.ICC;
            }

            return numIcc == numIccJpeg ? Status.OkStatus : Status.Failure("Invalid ICC chunks");
        }

        private static void DetectBlobs(JPEGData jpegData)
        {
            bool haveExif = false;
            bool haveXmp  = false;

            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                byte[] marker = jpegData.AppData[i];

                if (marker.Length < 3 || marker[0] != 0xE1)
                {
                    continue;
                }

                ReadOnlySpan<byte> payload = marker.AsSpan(3);

                if (!haveExif && payload.Length > ExifTag.Length && payload.Slice(0, ExifTag.Length).SequenceEqual(ExifTag))
                {
                    jpegData.AppMarkerTypes[i] = AppMarkerType.Exif;
                    haveExif                   = true;
                }

                if (!haveXmp && payload.Length >= XmpTag.Length && payload.Slice(0, XmpTag.Length).SequenceEqual(XmpTag))
                {
                    jpegData.AppMarkerTypes[i] = AppMarkerType.XMP;
                    haveXmp                    = true;
                }
            }
        }

        /// <summary>
        /// Parses a JPEG byte span into a newly allocated <see cref="JPEGData"/> and prepares it for JXL reconstruction.
        /// Ported from <c>jxl::jpeg::ParseJPG</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status ParseJPG(
            JxlMemoryManager? memoryManager,
            ReadOnlySpan<byte> bytes,
            out JPEGData? jpgOut)
        {
            _      = memoryManager;
            jpgOut = null;

            JPEGData jpg = new JPEGData();

            if (!EncJpegDataReader.ReadJpeg(bytes, JpegReadMode.kReadAll, jpg))
            {
                return Status.Failure("Error reading JPEG data");
            }

            jpgOut = jpg;

            return Status.OkStatus;
        }

        /// <summary>
        /// Configures <paramref name="colorEncoding"/> from the ICC profile or component count in <paramref name="jpg"/>.
        /// Ported from <c>jxl::jpeg::SetColorEncodingFromJpegData</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status SetColorEncodingFromJpegData(JPEGData jpg, ColorEncoding colorEncoding)
        {
            List<byte> icc = new List<byte>();

            for (int i = 0; i < jpg.AppData.Count; i++)
            {
                byte[] app = jpg.AppData[i];

                if (app.Length > 3 + IccProfileTag.Length + 2 &&
                    app[0] == 0xE2 &&
                    app.AsSpan(3, IccProfileTag.Length).SequenceEqual(IccProfileTag))
                {
                    for (int k = 3 + IccProfileTag.Length + 2; k < app.Length; k++)
                    {
                        icc.Add(app[k]);
                    }
                }
            }

            if (icc.Count == 0)
            {
                bool isGray = jpg.Components.Count == 1;

                return colorEncoding.FromExternal(ColorEncoding.SRGB(isGray).ToExternal());
            }

            return colorEncoding.SetICC(icc.ToArray(), JxlCms.GetDefaultCms());
        }

        /// <summary>
        /// Configures <paramref name="chromaSubsampling"/> from the component sampling factors in <paramref name="jpg"/>.
        /// Ported from <c>jxl::jpeg::SetChromaSubsamplingFromJpegData</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status SetChromaSubsamplingFromJpegData(
            JPEGData jpg,
            YCbCrChromaSubsampling chromaSubsampling)
        {
            Span<byte> hs = stackalloc byte[3];
            Span<byte> vs = stackalloc byte[3];

            if (jpg.Components.Count == 1)
            {
                hs.Fill((byte)jpg.Components[0].HSampFactor);
                vs.Fill((byte)jpg.Components[0].VSampFactor);
            }
            else if (jpg.Components.Count == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    hs[i] = (byte)jpg.Components[i].HSampFactor;
                    vs[i] = (byte)jpg.Components[i].VSampFactor;
                }
            }
            else
            {
                return Status.Failure("Unsupported JPEG component count");
            }

            return chromaSubsampling.Set(hs, vs);
        }

        /// <summary>
        /// Determines the JXL <see cref="ColorTransform"/> (<see cref="ColorTransform.YCbCr"/> or <see cref="ColorTransform.None"/>)
        /// for <paramref name="jpg"/>.
        /// Ported from <c>jxl::jpeg::SetColorTransformFromJpegData</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status SetColorTransformFromJpegData(JPEGData jpg, out ColorTransform colorTransform)
        {
            int nbComp = jpg.Components.Count;

            if (nbComp != 1 && nbComp != 3)
            {
                colorTransform = ColorTransform.YCbCr;

                return Status.Failure("Unsupported JPEG component count");
            }

            bool isRgb = DetectIsRgbJpeg(jpg);
            colorTransform = (!isRgb || nbComp == 1) ? ColorTransform.YCbCr : ColorTransform.None;

            return Status.OkStatus;
        }

        private static bool DetectIsRgbJpeg(JPEGData jpg)
        {
            if (jpg.MarkerOrder.Contains(0xE0))
            {
                return false;
            }

            int appMarkers = 0;

            for (int i = 0; i < jpg.MarkerOrder.Count; i++)
            {
                byte m = jpg.MarkerOrder[i];

                if ((m & 0xF0) == 0xE0 && appMarkers < jpg.AppData.Count)
                {
                    byte[] app = jpg.AppData[appMarkers++];

                    if (m == 0xEE && app.Length == 15 &&
                        app[3] == (byte)'A' && app[4] == (byte)'d' && app[5] == (byte)'o' &&
                        app[6] == (byte)'b' && app[7] == (byte)'e')
                    {
                        return app[14] == 0;
                    }
                }
            }

            return jpg.Components.Count == 3 &&
                   jpg.Components[0].Id == 'R' &&
                   jpg.Components[1].Id == 'G' &&
                   jpg.Components[2].Id == 'B';
        }

        /// <summary>
        /// Extracts Exif, XMP, and JUMBF metadata payloads from <paramref name="jpg"/> into <paramref name="blobs"/>.
        /// Ported from <c>jxl::jpeg::SetBlobsFromJpegData</c> in <c>lib/jxl/jpeg/enc_jpeg_data.cc</c>.
        /// </summary>
        public static Status SetBlobsFromJpegData(JPEGData jpg, JpegBlobs blobs)
        {
            for (int i = 0; i < jpg.AppData.Count; i++)
            {
                byte[] app = jpg.AppData[i];

                if (app.Length <= 3 || app[0] != 0xE1)
                {
                    continue;
                }

                ReadOnlySpan<byte> payload = app.AsSpan(3);

                if (payload.Length >= ExifTag.Length &&
                    payload.Slice(0, ExifTag.Length).SequenceEqual(ExifTag) &&
                    blobs.Exif.Length == 0)
                {
                    blobs.Exif = payload.Slice(ExifTag.Length).ToArray();
                }

                if (payload.Length >= XmpTag.Length &&
                    payload.Slice(0, XmpTag.Length).SequenceEqual(XmpTag) &&
                    blobs.Xmp.Length == 0)
                {
                    blobs.Xmp = payload.Slice(XmpTag.Length).ToArray();
                }
            }

            return Status.OkStatus;
        }

        private static void GatherUnbrotliBytes(JPEGData jpegData, List<byte> unbrotli)
        {
            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                AppMarkerType t = i < jpegData.AppMarkerTypes.Count ? jpegData.AppMarkerTypes[i] : AppMarkerType.Unknown;

                if (t == AppMarkerType.Unknown)
                {
                    unbrotli.AddRange(jpegData.AppData[i]);
                }
            }

            for (int i = 0; i < jpegData.ComData.Count; i++)
            {
                unbrotli.AddRange(jpegData.ComData[i]);
            }

            for (int i = 0; i < jpegData.InterMarkerData.Count; i++)
            {
                unbrotli.AddRange(jpegData.InterMarkerData[i]);
            }

            unbrotli.AddRange(jpegData.TailData);
        }

        private static bool CompressBrotliPayload(List<byte> input, int effort, out byte[] compressed)
        {
            int quality = Math.Clamp(effort, 0, 11);
            byte[] raw  = input.ToArray();

            using MemoryStream ms = new MemoryStream();

            using (BrotliStream bs = new BrotliStream(ms, new BrotliCompressionOptions { Quality = quality }, leaveOpen: true))
            {
                bs.Write(raw, 0, raw.Length);
            }

            compressed = ms.ToArray();

            return true;
        }
    }
}
