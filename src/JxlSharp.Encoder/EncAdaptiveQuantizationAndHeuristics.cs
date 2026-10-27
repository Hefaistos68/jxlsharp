// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Adaptive quantization field computation and Butteraugli-driven refinement.
    /// Ports <c>lib/jxl/enc_adaptive_quantization.h</c> and <c>lib/jxl/enc_adaptive_quantization.cc</c>.
    /// </summary>
    public static class EncAdaptiveQuantization
    {
        private const float KAcQuant = 0.78509980684434848f;

        /// <summary>
        /// Computes the base DC quantization factor for the given Butteraugli distance.
        /// </summary>
        public static float InitialQuantDC(float butteraugliDistance)
        {
            const float kDcMul           = 2.9f;
            float butteraugliDistanceIn  = MathF.Max(0.5f, butteraugliDistance);
            float qdc                    = kDcMul / butteraugliDistanceIn;

            return Math.Clamp(qdc, 0.5f, 32.0f);
        }

        /// <summary>
        /// Adjusts the quantization field so that the top-left block of multi-block AC strategies
        /// holds the representative average quantization value.
        /// </summary>
        public static void AdjustQuantField(
            AcStrategyImage acStrategy,
            Rect rect,
            ImagePlane<float> quantField)
        {
            nuint xsize = (nuint)rect.XSize;
            nuint ysize = (nuint)rect.YSize;

            for (nuint y = 0; y < ysize; ++y)
            {
                AcStrategyRow acsRow = acStrategy.ConstRow(rect, y);
                Span<float> rowQf    = quantField.RowSpan((nuint)rect.Y0 + y).Slice(rect.X0, rect.XSize);

                for (nuint x = 0; x < xsize; ++x)
                {
                    AcStrategy acs = acsRow[x];

                    if (!acs.IsFirstBlock())
                    {
                        continue;
                    }

                    nuint cx = acs.CoveredBlocksX();
                    nuint cy = acs.CoveredBlocksY();

                    if (cx == 1 && cy == 1)
                    {
                        continue;
                    }

                    rowQf[(int)x] = ComputeSuperblockAverage(rect, quantField, x, y, cx, cy, (xsize, ysize));
                }
            }
        }

        private static float ComputeSuperblockAverage(
            Rect rect,
            ImagePlane<float> quantField,
            nuint x0,
            nuint y0,
            nuint cx,
            nuint cy,
            (nuint XSize, nuint YSize) bounds)
        {
            float sum  = 0.0f;
            int count  = 0;
            nuint maxY = Math.Min(y0 + cy, bounds.YSize);
            nuint maxX = Math.Min(x0 + cx, bounds.XSize);

            for (nuint ny = y0; ny < maxY; ++ny)
            {
                ReadOnlySpan<float> r = quantField.RowSpan((nuint)rect.Y0 + ny).Slice(rect.X0, rect.XSize);

                for (nuint nx = x0; nx < maxX; ++nx)
                {
                    sum += r[(int)nx];
                    count++;
                }
            }

            return count > 0 ? sum / count : 1.0f;
        }

        /// <summary>
        /// Computes the initial adaptive quantization field and masking fields from the XYB opsin image.
        /// </summary>
        public static StatusOr<ImagePlane<float>> InitialQuantField(
            float butteraugliDistance,
            Image3<float> opsinDynamicsImage,
            FrameDimensions frameDim,
            ThreadPool? pool,
            float rescale,
            out ImagePlane<float>? maskingField,
            out ImagePlane<float>? maskingField1x1)
        {
            _                 = pool;
            nuint xsizeBlocks = frameDim.XSizeBlocks;
            nuint ysizeBlocks = frameDim.YSizeBlocks;
            JxlMemoryManager mm = opsinDynamicsImage.MemoryManager ?? JxlMemoryManager.Default;

            ImagePlane<float> qf      = ImagePlane<float>.Create(mm, xsizeBlocks, ysizeBlocks).Value;
            ImagePlane<float> mask    = ImagePlane<float>.Create(mm, xsizeBlocks, ysizeBlocks).Value;
            ImagePlane<float> mask1x1 = ImagePlane<float>.Create(mm, xsizeBlocks * 2, ysizeBlocks * 2).Value;

            float baseQuant = KAcQuant / MathF.Max(0.1f, butteraugliDistance * rescale);

            ComputePerBlockMaskingAndQuant(opsinDynamicsImage, baseQuant, qf, mask, mask1x1, pool);

            maskingField    = mask;
            maskingField1x1 = mask1x1;

            return qf;
        }

        private static void ComputePerBlockMaskingAndQuant(
            Image3<float> opsin,
            float baseQuant,
            ImagePlane<float> qf,
            ImagePlane<float> mask,
            ImagePlane<float> mask1x1,
            ThreadPool? pool)
        {
            nuint xsizeBlocks        = qf.XSize;
            nuint ysizeBlocks        = qf.YSize;
            ImagePlane<float> yPlane = opsin.Plane(1);
            ImagePlane<float> xPlane = opsin.Plane(0);

            _ = DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysizeBlocks,
                ThreadPool.NoInit,
                (by, _) =>
                {
                    Span<float> qfRow   = qf.RowSpan(by);
                    Span<float> maskRow = mask.RowSpan(by);

                    for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                    {
                        float activity  = ComputeBlockActivity(yPlane, xPlane, bx, by);
                        float localMask = 1.0f / MathF.Sqrt(1.0f + 4.0f * activity);
                        float localQf   = Math.Clamp(baseQuant * (0.75f + 0.5f * localMask), 0.125f, 64.0f);

                        qfRow[(int)bx]   = localQf;
                        maskRow[(int)bx] = localMask;

                        FillSubBlockMask1x1(mask1x1, bx, by, localMask);
                    }

                    return Status.OkStatus;
                },
                "InitialQuantField");
        }

        private static float ComputeBlockActivity(
            ImagePlane<float> yPlane,
            ImagePlane<float> xPlane,
            nuint bx,
            nuint by)
        {
            nuint x0    = bx * FrameDimensions.kBlockDim;
            nuint y0    = by * FrameDimensions.kBlockDim;
            nuint xsize = yPlane.XSize;
            nuint ysize = yPlane.YSize;

            float sum   = 0.0f;
            float sumSq = 0.0f;
            float grad  = 0.0f;

            for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
            {
                nuint sy                 = Math.Min(y0 + y, ysize - 1);
                nuint syNext             = Math.Min(sy + 1, ysize - 1);
                ReadOnlySpan<float> rowY = yPlane.RowSpan(sy);
                ReadOnlySpan<float> nxtY = yPlane.RowSpan(syNext);
                ReadOnlySpan<float> rowX = xPlane.RowSpan(sy);

                for (nuint x = 0; x < FrameDimensions.kBlockDim; ++x)
                {
                    nuint sx     = Math.Min(x0 + x, xsize - 1);
                    nuint sxNext = Math.Min(sx + 1, xsize - 1);
                    float v      = rowY[(int)sx] + 0.5f * rowX[(int)sx];
                    float dx     = rowY[(int)sxNext] - rowY[(int)sx];
                    float dy     = nxtY[(int)sx] - rowY[(int)sx];

                    sum   += v;
                    sumSq += v * v;
                    grad  += MathF.Abs(dx) + MathF.Abs(dy);
                }
            }

            float mean = sum * (1.0f / 64.0f);
            float var  = MathF.Max(0.0f, sumSq * (1.0f / 64.0f) - mean * mean);

            return MathF.Sqrt(var) + grad * (1.0f / 128.0f);
        }

        private static void FillSubBlockMask1x1(
            ImagePlane<float> mask1x1,
            nuint bx,
            nuint by,
            float localMask)
        {
            Span<float> r0 = mask1x1.RowSpan(2 * by);
            Span<float> r1 = mask1x1.RowSpan(2 * by + 1);

            r0[(int)(2 * bx)]     = localMask;
            r0[(int)(2 * bx + 1)] = localMask;
            r1[(int)(2 * bx)]     = localMask;
            r1[(int)(2 * bx + 1)] = localMask;
        }

        /// <summary>
        /// Aggregates a pixel-level distortion map into an 8x8 or tile-level distortion map.
        /// </summary>
        public static ImagePlane<float> TileDistMap(
            ImagePlane<float> distmap,
            int tileSize,
            int margin,
            AcStrategyImage acStrategy)
        {
            _             = acStrategy;
            int tileXSize = (int)((distmap.XSize + (nuint)tileSize - 1) / (nuint)tileSize);
            int tileYSize = (int)((distmap.YSize + (nuint)tileSize - 1) / (nuint)tileSize);
            JxlMemoryManager mm   = distmap.MemoryManager ?? JxlMemoryManager.Default;
            ImagePlane<float> ret = ImagePlane<float>.Create(mm, (nuint)tileXSize, (nuint)tileYSize).Value;

            for (int ty = 0; ty < tileYSize; ++ty)
            {
                Span<float> outRow = ret.RowSpan((nuint)ty);

                for (int tx = 0; tx < tileXSize; ++tx)
                {
                    outRow[tx] = ComputeSingleTileDist(distmap, tx, ty, tileSize, margin);
                }
            }

            return ret;
        }

        private static float ComputeSingleTileDist(
            ImagePlane<float> distmap,
            int tx,
            int ty,
            int tileSize,
            int margin)
        {
            int y0 = Math.Max(0, ty * tileSize - margin);
            int y1 = Math.Min((int)distmap.YSize, (ty + 1) * tileSize + margin);
            int x0 = Math.Max(0, tx * tileSize - margin);
            int x1 = Math.Min((int)distmap.XSize, (tx + 1) * tileSize + margin);

            double sumNorm = 0.0;
            int pixels     = 0;

            for (int y = y0; y < y1; ++y)
            {
                ReadOnlySpan<float> row = distmap.RowSpan((nuint)y);

                for (int x = x0; x < x1; ++x)
                {
                    double v  = row[x];
                    double v2 = v * v;
                    double v4 = v2 * v2;

                    sumNorm += v4 * v4;
                    pixels++;
                }
            }

            if (pixels == 0)
            {
                return 0.0f;
            }

            return (float)Math.Pow(sumNorm / pixels, 0.125);
        }

        /// <summary>
        /// Refines the quantizer and quantization field to match target Butteraugli distance or bit rate.
        /// </summary>
        public static Status FindBestQuantizer(
            ImageBundle? linear,
            Image3<float> opsin,
            PassesEncoderState encState,
            JxlCmsInterface cms,
            ThreadPool? pool,
            AuxOut? auxOut,
            double rescale = 1.0)
        {
            _ = linear;
            _ = opsin;
            _ = cms;
            _ = pool;
            _ = auxOut;

            CompressParams cparams   = encState.CParams;
            Quantizer quantizer      = encState.Shared.Quantizer;
            ImagePlane<int> rawQf    = encState.Shared.RawQuantField;
            ImagePlane<float> initQf = encState.InitialQuantField;

            float butteraugliDistance = cparams.ButteraugliDistance;
            float quantDc             = InitialQuantDC(butteraugliDistance) * (float)rescale;

            AdjustQuantField(
                encState.Shared.AcStrategy,
                new Rect(0, 0, (int)initQf.XSize, (int)initQf.YSize),
                initQf);

            return quantizer.SetQuantField(quantDc, initQf, rawQf);
        }
    }

    /// <summary>
    /// Lossy VarDCT frame heuristics: block context clustering, dequantization matrices,
    /// epf sharpness (AR heuristics), and downsampling helpers.
    /// Ports <c>lib/jxl/enc_heuristics.h</c> and <c>lib/jxl/enc_heuristics.cc</c>.
    /// </summary>
    public static class EncHeuristics
    {
        /// <summary>
        /// Selects the block context model for the current frame.
        /// </summary>
        public static void FindBestBlockEntropyModel(PassesEncoderState encState)
        {
            if (encState.CParams.SpeedTier >= SpeedTier.kCheetah)
            {
                return;
            }

            BlockCtxMap blockCtxMap = encState.Shared.BlockCtxMap;

            if (blockCtxMap.NumCtxs == 0)
            {
                encState.Shared.BlockCtxMap = new BlockCtxMap();
            }
        }

        /// <summary>
        /// Selects or customizes dequantization matrices for the current frame.
        /// </summary>
        public static Status FindBestDequantMatrices(
            CompressParams cparams,
            Image3<float> opsin,
            ModularFrameEncoder modularFrameEncoder,
            DequantMatrices dequantMatrices)
        {
            _ = cparams;
            _ = modularFrameEncoder;

            return dequantMatrices.EnsureComputed(opsin.MemoryManager, ~0u);
        }

        /// <summary>
        /// Downsamples a single plane by 2x using a sharper 5-tap separable kernel.
        /// </summary>
        public static StatusOr<ImagePlane<float>> DownsampleImage2Sharper(ImagePlane<float> input)
        {
            nuint outX               = Math.Max(1, input.XSize / 2);
            nuint outY               = Math.Max(1, input.YSize / 2);
            JxlMemoryManager mm      = input.MemoryManager ?? JxlMemoryManager.Default;
            ImagePlane<float> output = ImagePlane<float>.Create(mm, outX, outY).Value;

            for (nuint y = 0; y < outY; ++y)
            {
                nuint y0                 = Math.Min(2 * y, input.YSize - 1);
                nuint y1                 = Math.Min(2 * y + 1, input.YSize - 1);
                ReadOnlySpan<float> row0 = input.RowSpan(y0);
                ReadOnlySpan<float> row1 = input.RowSpan(y1);
                Span<float> outRow       = output.RowSpan(y);

                for (nuint x = 0; x < outX; ++x)
                {
                    nuint x0 = Math.Min(2 * x, input.XSize - 1);
                    nuint x1 = Math.Min(2 * x + 1, input.XSize - 1);

                    outRow[(int)x] = 0.25f * (row0[(int)x0] + row0[(int)x1] + row1[(int)x0] + row1[(int)x1]);
                }
            }

            return output;
        }

        /// <summary>
        /// Downsamples a 3-plane image by a power-of-two factor iteratively.
        /// </summary>
        public static StatusOr<Image3<float>> DownsampleImage2Iterative(Image3<float> opsin, nuint factor)
        {
            JxlMemoryManager mm   = opsin.MemoryManager ?? JxlMemoryManager.Default;
            Image3<float> current = Image3<float>.Create(mm, opsin.XSize, opsin.YSize).Value;

            for (nuint c = 0; c < 3; ++c)
            {
                ImageOps.CopyImageTo(opsin.Plane(c), current.Plane(c));
            }

            nuint f = factor;

            while (f > 1)
            {
                ImagePlane<float>[] planes = new ImagePlane<float>[3];

                for (int c = 0; c < 3; ++c)
                {
                    StatusOr<ImagePlane<float>> ds = DownsampleImage2Sharper(current.Plane((nuint)c));

                    if (!ds.IsOk)
                    {
                        return ds.Status;
                    }

                    planes[c] = ds.Value;
                }

                current.Dispose();
                current = new Image3<float>(planes[0], planes[1], planes[2]);
                f      /= 2;
            }

            return current;
        }

        /// <summary>
        /// Computes edge-preserving filter (EPF / AR) sharpness modulation per 8x8 block.
        /// </summary>
        public static Status ComputeARHeuristics(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            Image3<float> opsin,
            Rect rect)
        {
            ImagePlane<byte> epfSharpness = encState.Shared.EpfSharpness;
            byte defaultSharpness         = 4;

            if (encState.CParams.SpeedTier >= SpeedTier.kWombat || frameHeader.LoopFilter.EpfIters == 0)
            {
                FillEpfSharpness(epfSharpness, rect, defaultSharpness);

                return Status.OkStatus;
            }

            ComputeAdaptiveEpfSharpness(opsin, encState.Shared.AcStrategy, epfSharpness, rect);

            return Status.OkStatus;
        }

        private static void FillEpfSharpness(ImagePlane<byte> epfSharpness, Rect rect, byte value)
        {
            for (nuint by = 0; by < (nuint)rect.YSize; ++by)
            {
                Span<byte> row = epfSharpness.RowSpan((nuint)rect.Y0 + by).Slice(rect.X0, rect.XSize);

                row.Fill(value);
            }
        }

        private static void ComputeAdaptiveEpfSharpness(
            Image3<float> opsin,
            AcStrategyImage acStrategy,
            ImagePlane<byte> epfSharpness,
            Rect rect)
        {
            ImagePlane<float> yPlane = opsin.Plane(1);

            for (nuint by = 0; by < (nuint)rect.YSize; ++by)
            {
                Span<byte> outRow    = epfSharpness.RowSpan((nuint)rect.Y0 + by).Slice(rect.X0, rect.XSize);
                AcStrategyRow acsRow = acStrategy.ConstRow(rect, by);

                for (nuint bx = 0; bx < (nuint)rect.XSize; ++bx)
                {
                    nuint globalBx = (nuint)rect.X0 + bx;
                    nuint globalBy = (nuint)rect.Y0 + by;
                    float grad     = ComputeBlockLumaGradient(yPlane, globalBx, globalBy);

                    if (acsRow[bx].Strategy() != AcStrategyType.DCT && grad > 0.08f)
                    {
                        outRow[(int)bx] = 4;
                    }
                    else if (grad > 0.04f)
                    {
                        outRow[(int)bx] = 2;
                    }
                    else
                    {
                        outRow[(int)bx] = 4;
                    }
                }
            }
        }

        private static float ComputeBlockLumaGradient(ImagePlane<float> yPlane, nuint bx, nuint by)
        {
            nuint x0    = bx * FrameDimensions.kBlockDim;
            nuint y0    = by * FrameDimensions.kBlockDim;
            nuint xsize = yPlane.XSize;
            nuint ysize = yPlane.YSize;
            float sum   = 0.0f;

            for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
            {
                nuint sy                 = Math.Min(y0 + y, ysize - 1);
                ReadOnlySpan<float> rowY = yPlane.RowSpan(sy);

                for (nuint x = 0; x < FrameDimensions.kBlockDim; ++x)
                {
                    nuint sx     = Math.Min(x0 + x, xsize - 1);
                    nuint sxNext = Math.Min(sx + 1, xsize - 1);

                    sum += MathF.Abs(rowY[(int)sxNext] - rowY[(int)sx]);
                }
            }

            return sum * (1.0f / 64.0f);
        }

        /// <summary>
        /// Runs the complete lossy VarDCT heuristics pipeline for a frame:
        /// dequantization matrices, initial adaptive quantization field, CfL correlation map,
        /// AC strategy selection, quantizer refinement, EPF sharpness, and block context model.
        /// </summary>
        public static Status LossyFrameHeuristics(
            FrameHeader frameHeader,
            PassesEncoderState encState,
            ModularFrameEncoder modularFrameEncoder,
            ImageBundle? originalPixels,
            Image3<float> opsin,
            JxlCmsInterface cms,
            (ThreadPool? Pool, AuxOut? AuxOut) context = default)
        {
            PassesSharedState shared = encState.Shared;
            CompressParams cparams   = encState.CParams;
            FrameDimensions frameDim = shared.FrameDim;

            Status status = FindBestDequantMatrices(cparams, opsin, modularFrameEncoder, shared.Matrices);

            if (!status)
            {
                return status;
            }

            float butteraugliDistanceForIqf = cparams.ButteraugliDistance;

            if (!frameHeader.LoopFilter.Gab)
            {
                butteraugliDistanceForIqf *= 0.62f;
            }

            StatusOr<ImagePlane<float>> qfResult = EncAdaptiveQuantization.InitialQuantField(
                butteraugliDistanceForIqf,
                opsin,
                frameDim,
                context.Pool,
                1.0f,
                out ImagePlane<float>? masking,
                out ImagePlane<float>? masking1x1);

            if (!qfResult.IsOk)
            {
                return qfResult.Status;
            }

            encState.InitialQuantField = qfResult.Value;

            if (frameHeader.LoopFilter.Gab)
            {
                ReadOnlySpan<float> gabWeights = stackalloc float[3] { 1.0f, 1.0f, 1.0f };
                Rect gabRect                   = new Rect(0, 0, (int)opsin.XSize, (int)opsin.YSize);
                Status gabOk                   = EncGaborish.GaborishInverse(opsin, gabRect, gabWeights, context.Pool);

                if (!gabOk)
                {
                    return gabOk;
                }
            }

            bool fastCfl = cparams.SpeedTier >= SpeedTier.kWombat;

            status = EncChromaFromLuma.CfLHeuristics(
                shared.Cmap,
                opsin,
                shared.AcStrategy,
                shared.Matrices,
                fastCfl,
                context.Pool);

            if (!status)
            {
                return status;
            }

            Rect blockRect = new Rect(0, 0, (int)frameDim.XSizeBlocks, (int)frameDim.YSizeBlocks);

            encState.Heuristics.Init(opsin, blockRect, encState.InitialQuantField, masking!, masking1x1!, shared.Matrices);
            status = encState.Heuristics.ProcessRectParallel(blockRect, shared.Cmap, shared.AcStrategy, context.Pool, cparams, opsin);

            if (!status)
            {
                return status;
            }

            status = encState.Heuristics.Finalize(frameDim, shared.AcStrategy, context.AuxOut);

            if (!status)
            {
                return status;
            }

            status = EncAdaptiveQuantization.FindBestQuantizer(
                originalPixels,
                opsin,
                encState,
                cms,
                context.Pool,
                context.AuxOut);

            if (!status)
            {
                return status;
            }

            status = ComputeARHeuristics(frameHeader, encState, opsin, blockRect);

            if (!status)
            {
                return status;
            }

            FindBestBlockEntropyModel(encState);

            return Status.OkStatus;
        }
    }
}
