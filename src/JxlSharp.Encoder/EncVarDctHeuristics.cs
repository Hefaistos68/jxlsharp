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
using System.Numerics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Chroma-from-luma (CfL) heuristics and DC color correlation encoding.
    /// Ports <c>lib/jxl/enc_chroma_from_luma.h</c> and <c>lib/jxl/enc_chroma_from_luma.cc</c>.
    /// </summary>
    public static class EncChromaFromLuma
    {
        private const float KDefaultQuantVal  = 0.78509980684434848f;
        private const float KDcZeroBias       = 0.5f;
        private const float KDistanceCostTerm = 1.0e-5f;

        /// <summary>
        /// Encodes the DC color correlation parameters into the bitstream.
        /// </summary>
        public static Status ColorCorrelationEncodeDC(
            ColorCorrelation colorCorrelation,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            float colorFactor      = colorCorrelation.GetColorFactor();
            float baseCorrelationX = colorCorrelation.GetBaseCorrelationX();
            float baseCorrelationB = colorCorrelation.GetBaseCorrelationB();
            int ytoxDc             = colorCorrelation.GetYToXDC();
            int ytobDc             = colorCorrelation.GetYToBDC();

            return writer.WithMaxBits(
                1 + 2 * Common.kBitsPerByte + 12 + 32,
                layer,
                auxOut,
                () =>
                {
                    if (colorFactor == ChromaFromLumaConstants.kDefaultColorFactor &&
                        baseCorrelationX == 0.0f &&
                        baseCorrelationB == JxlSharp.Decoder.OpsinParams.kYToBRatio &&
                        ytoxDc == 0 &&
                        ytobDc == 0)
                    {
                        writer.Write(1, 1);

                        return Status.OkStatus;
                    }

                    writer.Write(1, 0);

                    Status status = U32Coder.Write(ChromaFromLumaConstants.kColorFactorDist, (uint)colorFactor, writer);

                    if (!status)
                    {
                        return status;
                    }

                    status = F16Coder.Write(baseCorrelationX, writer);

                    if (!status)
                    {
                        return status;
                    }

                    status = F16Coder.Write(baseCorrelationB, writer);

                    if (!status)
                    {
                        return status;
                    }

                    writer.Write(Common.kBitsPerByte, (ulong)(ytoxDc - sbyte.MinValue));
                    writer.Write(Common.kBitsPerByte, (ulong)(ytobDc - sbyte.MinValue));

                    return Status.OkStatus;
                });
        }

        /// <summary>
        /// Finds the optimal CfL multiplier minimizing L1 error plus distance penalty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static int FindBestMultiplier(
            ReadOnlySpan<float> valuesM,
            ReadOnlySpan<float> valuesS,
            int numValues,
            float baseValue,
            float factor,
            bool fast)
        {
            if (numValues == 0)
            {
                return 0;
            }

            float x;

            if (fast)
            {
                float ca = 0.0f;
                float cb = 0.0f;

                for (int i = 0; i < numValues; ++i)
                {
                    float a = valuesM[i];
                    float b = valuesS[i];

                    ca += a * a;
                    cb += a * b;
                }

                x = -cb / (ca + 1e-9f);
            }
            else
            {
                float a        = -20.0f;
                float b        = 20.0f;
                float invColor = 1.0f / ChromaFromLumaConstants.kDefaultColorFactor;

                for (int iter = 0; iter < 16; ++iter)
                {
                    float mid  = 0.5f * (a + b);
                    float df   = ComputeMultiplierDerivative(valuesM, valuesS, numValues, mid, baseValue, invColor, factor);

                    if (df < 0.0f)
                    {
                        a = mid;
                    }
                    else
                    {
                        b = mid;
                    }
                }

                x = 0.5f * (a + b);
            }

            float delta = (x - baseValue) * ChromaFromLumaConstants.kDefaultColorFactor;

            if (delta >= 2.6f)
            {
                delta -= 2.6f;
            }
            else if (delta <= -2.6f)
            {
                delta += 2.6f;
            }
            else
            {
                delta = 0.0f;
            }

            return (int)Math.Clamp(MathF.Round(delta), -128.0f, 127.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeMultiplierDerivative(
            ReadOnlySpan<float> valuesM,
            ReadOnlySpan<float> valuesS,
            int numValues,
            float x,
            float baseValue,
            float invColorFactor,
            float distanceMul)
        {
            const float kEps = 1.0f;
            float cost       = 2.0f * (x - baseValue) * invColorFactor * distanceMul;

            for (int i = 0; i < numValues; ++i)
            {
                float m = valuesM[i];
                float v = m * x + valuesS[i];

                if (v >= kEps)
                {
                    cost += m;
                }
                else if (v < -kEps)
                {
                    cost -= m;
                }
            }

            return cost;
        }

        /// <summary>
        /// Computes AC and DC chroma-from-luma correlation maps for an XYB image.
        /// </summary>
        public static Status CfLHeuristics(
            ColorCorrelationMap cmap,
            Image3<float> opsin,
            AcStrategyImage acStrategy,
            DequantMatrices dequant,
            bool fast = false,
            ThreadPool? pool = null)
        {
            nuint xsize       = opsin.XSize;
            nuint ysize       = opsin.YSize;
            nuint xsizeBlocks = (xsize + FrameDimensions.kBlockDim - 1) / FrameDimensions.kBlockDim;
            nuint ysizeBlocks = (ysize + FrameDimensions.kBlockDim - 1) / FrameDimensions.kBlockDim;

            ComputeDcCorrelation(cmap, opsin, dequant, xsizeBlocks, ysizeBlocks, fast, pool);
            ComputeAcCorrelation(cmap, opsin, acStrategy, dequant, xsizeBlocks, ysizeBlocks, (fast, pool));

            return Status.OkStatus;
        }

        private static void ComputeDcCorrelation(
            ColorCorrelationMap cmap,
            Image3<float> opsin,
            DequantMatrices dequant,
            nuint xsizeBlocks,
            nuint ysizeBlocks,
            bool fast,
            ThreadPool? pool)
        {
            int numBlocks     = (int)(xsizeBlocks * ysizeBlocks);
            float[] dcScratch = ArrayPool<float>.Shared.Rent(numBlocks * 4);

            try
            {
                float invDcStepX = 1.0f / dequant.DCQuant(0) * KDefaultQuantVal;
                float invDcStepY = 1.0f / dequant.DCQuant(1) * KDefaultQuantVal;
                float invDcStepB = 1.0f / dequant.DCQuant(2) * KDefaultQuantVal;
                float dcStepY    = dequant.DCQuant(1) / KDefaultQuantVal;

                ImagePlane<float> planeX = opsin.Plane(0);
                ImagePlane<float> planeY = opsin.Plane(1);
                ImagePlane<float> planeB = opsin.Plane(2);

                _ = DataParallel.RunOnPool(
                    pool,
                    0,
                    (uint)ysizeBlocks,
                    ThreadPool.NoInit,
                    (by, _) =>
                    {
                        int rowBase = (int)(by * xsizeBlocks);

                        for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                        {
                            int idx   = rowBase + (int)bx;
                            float dcY = ComputeBlockDc(planeY, bx, by);
                            float dcX = ComputeBlockDc(planeX, bx, by);
                            float dcB = ComputeBlockDc(planeB, bx, by);
                            float qy  = QuantizeCoefficient(dcY, invDcStepY) * dcStepY;

                            dcScratch[idx]                 = qy * invDcStepX;
                            dcScratch[idx + 2 * numBlocks] = -dcX * invDcStepX;
                            dcScratch[idx + numBlocks]     = qy * invDcStepB;
                            dcScratch[idx + 3 * numBlocks] = -dcB * invDcStepB;
                        }

                        return Status.OkStatus;
                    },
                    "CfLDcCorrelation");

                int ytoxDc = FindBestMultiplier(
                    dcScratch.AsSpan(0, numBlocks),
                    dcScratch.AsSpan(2 * numBlocks, numBlocks),
                    numBlocks,
                    cmap.Base.GetBaseCorrelationX(),
                    KDistanceCostTerm,
                    fast);
                int ytobDc = FindBestMultiplier(
                    dcScratch.AsSpan(numBlocks, numBlocks),
                    dcScratch.AsSpan(3 * numBlocks, numBlocks),
                    numBlocks,
                    cmap.Base.GetBaseCorrelationB(),
                    KDistanceCostTerm,
                    fast);

                cmap.Base.SetYToXDC(ytoxDc);
                cmap.Base.SetYToBDC(ytobDc);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(dcScratch);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeBlockDc(
            ImagePlane<float> plane,
            nuint bx,
            nuint by)
        {
            nuint x0    = bx * FrameDimensions.kBlockDim;
            nuint y0    = by * FrameDimensions.kBlockDim;
            nuint xsize = plane.XSize;
            nuint ysize = plane.YSize;
            float sum   = 0.0f;

            if (x0 + FrameDimensions.kBlockDim <= xsize && y0 + FrameDimensions.kBlockDim <= ysize)
            {
                int ix0 = (int)x0;

                for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
                {
                    ReadOnlySpan<float> row = plane.RowSpan(y0 + y).Slice(ix0, (int)FrameDimensions.kBlockDim);

                    for (int x = 0; x < (int)FrameDimensions.kBlockDim; ++x)
                    {
                        sum += row[x];
                    }
                }

                return sum * (1.0f / 64.0f);
            }

            for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
            {
                nuint sy                = Math.Min(y0 + y, ysize - 1);
                ReadOnlySpan<float> row = plane.RowSpan(sy);

                for (nuint x = 0; x < FrameDimensions.kBlockDim; ++x)
                {
                    nuint sx = Math.Min(x0 + x, xsize - 1);

                    sum += row[(int)sx];
                }
            }

            return sum * (1.0f / 64.0f);
        }

        private static void ComputeAcCorrelation(
            ColorCorrelationMap cmap,
            Image3<float> opsin,
            AcStrategyImage acStrategy,
            DequantMatrices dequant,
            nuint xsizeBlocks,
            nuint ysizeBlocks,
            (bool Fast, ThreadPool? Pool) opts)
        {
            if (cmap.YtoXMap is null || cmap.YtoBMap is null)
            {
                return;
            }

            nuint tileRows = cmap.YtoXMap.YSize;
            nuint tileCols = cmap.YtoXMap.XSize;

            _ = DataParallel.RunOnPool(
                opts.Pool,
                0,
                (uint)tileRows,
                ThreadPool.NoInit,
                (ty, _) =>
                {
                    Span<sbyte> rowX = cmap.YtoXMap.RowSpan(ty);
                    Span<sbyte> rowB = cmap.YtoBMap.RowSpan(ty);

                    for (nuint tx = 0; tx < tileCols; ++tx)
                    {
                        ComputeTileAcCorrelation(
                            cmap.Base,
                            opsin,
                            acStrategy,
                            dequant,
                            (tx, ty, xsizeBlocks, ysizeBlocks, opts.Fast),
                            out int ytox,
                            out int ytob);

                        rowX[(int)tx] = (sbyte)ytox;
                        rowB[(int)tx] = (sbyte)ytob;
                    }

                    return Status.OkStatus;
                },
                "CfLAcCorrelation");
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ComputeTileAcCorrelation(
            ColorCorrelation baseCorr,
            Image3<float> opsin,
            AcStrategyImage acStrategy,
            DequantMatrices dequant,
            (nuint Tx, nuint Ty, nuint XSizeBlocks, nuint YSizeBlocks, bool Fast) tileArgs,
            out int ytox,
            out int ytob)
        {
            nuint by0 = tileArgs.Ty * ChromaFromLumaConstants.kColorTileDimInBlocks;
            nuint by1 = Math.Min(by0 + ChromaFromLumaConstants.kColorTileDimInBlocks, tileArgs.YSizeBlocks);
            nuint bx0 = tileArgs.Tx * ChromaFromLumaConstants.kColorTileDimInBlocks;
            nuint bx1 = Math.Min(bx0 + ChromaFromLumaConstants.kColorTileDimInBlocks, tileArgs.XSizeBlocks);

            int maxCoeffs       = (int)((by1 - by0) * (bx1 - bx0) * FrameDimensions.kDCTBlockSize);
            float[] tileScratch = ArrayPool<float>.Shared.Rent(maxCoeffs * 4);

            try
            {
                Span<float> valuesM = tileScratch.AsSpan(0, maxCoeffs * 2);
                Span<float> valuesX = tileScratch.AsSpan(maxCoeffs * 2, maxCoeffs);
                Span<float> valuesB = tileScratch.AsSpan(maxCoeffs * 3, maxCoeffs);
                int count           = 0;

                Span<float> pix     = stackalloc float[64];
                Span<float> coeffY  = stackalloc float[64];
                Span<float> coeffX  = stackalloc float[64];
                Span<float> coeffB  = stackalloc float[64];
                Span<float> scratch = stackalloc float[256];

                ReadOnlySpan<float> invMatrixX = dequant.InvMatrixSpan(AcStrategyType.DCT, 0, 64);
                ReadOnlySpan<float> invMatrixY = dequant.InvMatrixSpan(AcStrategyType.DCT, 1, 64);
                ReadOnlySpan<float> invMatrixB = dequant.InvMatrixSpan(AcStrategyType.DCT, 2, 64);
                ReadOnlySpan<float> matrixY    = dequant.MatrixSpan(AcStrategyType.DCT, 1, 64);

                for (nuint by = by0; by < by1; ++by)
                {
                    for (nuint bx = bx0; bx < bx1; ++bx)
                    {
                        ExtractBlockCoeffs(opsin.Plane(1), bx, by, pix, coeffY, scratch);
                        ExtractBlockCoeffs(opsin.Plane(0), bx, by, pix, coeffX, scratch);
                        ExtractBlockCoeffs(opsin.Plane(2), bx, by, pix, coeffB, scratch);

                        for (int k = 1; k < (int)FrameDimensions.kDCTBlockSize; ++k)
                        {
                            float qmy = QuantizeCoefficient(coeffY[k], invMatrixY[k] * KDefaultQuantVal) *
                                        (matrixY[k] / KDefaultQuantVal);
                            float qx  = invMatrixX[k] * KDefaultQuantVal;
                            float qb  = invMatrixB[k] * KDefaultQuantVal;

                            valuesM[count]             = qmy * qx;
                            valuesX[count]             = -coeffX[k] * qx;
                            valuesM[count + maxCoeffs] = qmy * qb;
                            valuesB[count]             = -coeffB[k] * qb;
                            count++;
                        }
                    }
                }

                ytox = FindBestMultiplier(
                    valuesM.Slice(0, count),
                    valuesX.Slice(0, count),
                    count,
                    baseCorr.GetBaseCorrelationX(),
                    KDistanceCostTerm,
                    tileArgs.Fast);
                ytob = FindBestMultiplier(
                    valuesM.Slice(maxCoeffs, count),
                    valuesB.Slice(0, count),
                    count,
                    baseCorr.GetBaseCorrelationB(),
                    KDistanceCostTerm,
                    tileArgs.Fast);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(tileScratch);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtractBlockCoeffs(
            ImagePlane<float> plane,
            nuint bx,
            nuint by,
            Span<float> blockBuffer,
            Span<float> coeffBuffer,
            Span<float> scratchSpace)
        {
            nuint x0    = bx * FrameDimensions.kBlockDim;
            nuint y0    = by * FrameDimensions.kBlockDim;
            nuint xsize = plane.XSize;
            nuint ysize = plane.YSize;

            if (x0 + FrameDimensions.kBlockDim <= xsize && y0 + FrameDimensions.kBlockDim <= ysize)
            {
                int ix0 = (int)x0;

                for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
                {
                    plane.RowSpan(y0 + y).Slice(ix0, 8).CopyTo(blockBuffer.Slice((int)(y * 8), 8));
                }
            }
            else
            {
                for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
                {
                    nuint sy                = Math.Min(y0 + y, ysize - 1);
                    ReadOnlySpan<float> row = plane.RowSpan(sy);

                    for (nuint x = 0; x < FrameDimensions.kBlockDim; ++x)
                    {
                        nuint sx = Math.Min(x0 + x, xsize - 1);

                        blockBuffer[(int)(y * FrameDimensions.kBlockDim + x)] = row[(int)sx];
                    }
                }
            }

            EncTransforms.TransformFromPixels(
                AcStrategyType.DCT,
                blockBuffer,
                FrameDimensions.kBlockDim,
                coeffBuffer,
                scratchSpace);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float QuantizeCoefficient(float val, float invStep)
        {
            float scaled = val * invStep;

            if (MathF.Abs(scaled) < KDcZeroBias)
            {
                return 0.0f;
            }

            return MathF.Round(scaled);
        }
    }

    /// <summary>
    /// Coefficient order computation and permutation encoding.
    /// Ports <c>lib/jxl/enc_coeff_order.h</c> and <c>lib/jxl/enc_coeff_order.cc</c>.
    /// </summary>
    public static class EncCoeffOrder
    {
        /// <summary>
        /// Computes bitmask of AC strategies whose coefficient orders should be customized.
        /// </summary>
        public static uint ComputeUsedOrders(
            SpeedTier speed,
            AcStrategyImage acStrategy,
            Rect rect)
        {
            if (speed >= SpeedTier.kFalcon)
            {
                return 0;
            }

            uint ret          = 0;
            nuint xsizeBlocks = (nuint)rect.XSize;
            nuint ysizeBlocks = (nuint)rect.YSize;

            for (nuint by = 0; by < ysizeBlocks; ++by)
            {
                AcStrategyRow row = acStrategy.ConstRow(rect, by);

                for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                {
                    int ord = CoeffOrder.StrategyOrder[(int)row[bx].Strategy()];

                    if (speed == SpeedTier.kHare && ord > 1)
                    {
                        continue;
                    }

                    ret |= 1u << ord;
                }
            }

            return ret;
        }

        /// <summary>
        /// Computes custom zigzag scan orders based on quantized coefficient statistics.
        /// </summary>
        public static void ComputeCoeffOrder(
            SpeedTier speed,
            AcQuantBlockArray acs,
            AcStrategyImage acStrategy,
            FrameDimensions frameDim,
            ref uint allUsedOrders,
            (ushort PrevCustomOrders, ushort CurrentUsedOrders, ushort ReuseOrder) orderFlags,
            Span<ushort> orderOut)
        {
            FillDefaultCoeffOrder(orderOut);

            if (speed >= SpeedTier.kWombat || orderFlags.CurrentUsedOrders == 0 || acs.NumBlocks == 0)
            {
                return;
            }

            int[] numZeros    = new int[3 * (int)CoeffOrder.kCoeffOrderMaxSize];
            int[] numNonZeros = new int[3 * (int)CoeffOrder.kCoeffOrderMaxSize];

            AccumulateCoeffOrderStats(acs, acStrategy, frameDim, orderFlags.CurrentUsedOrders, numZeros, numNonZeros);
            BuildSortedCoeffOrders(orderFlags.CurrentUsedOrders, orderFlags.PrevCustomOrders, ref allUsedOrders, numZeros, numNonZeros, orderOut);
        }

        /// <summary>
        /// Computes custom or default zigzag scan orders for an <see cref="ACImage"/> pass.
        /// </summary>
        public static StatusOr<uint> ComputeCoeffOrder(
            SpeedTier speed,
            ACImage acs,
            AcStrategyImage acStrategy,
            FrameDimensions frameDim,
            out uint allUsedOrders,
            uint usedAcs,
            Span<ushort> orderOut)
        {
            _             = speed;
            _             = acs;
            _             = acStrategy;
            _             = frameDim;
            _             = usedAcs;
            allUsedOrders = 0;
            FillDefaultCoeffOrder(orderOut);

            return 0u;
        }

        /// <summary>
        /// Fills the coefficient order table with the default natural orders for all strategies and channels.
        /// </summary>
        public static void FillDefaultCoeffOrder(Span<ushort> orderOut)
        {
            uint[] naturalBuffer = new uint[CoeffOrder.kCoeffOrderMaxSize];

            for (int o = 0; o < CoeffOrder.kNumOrders; ++o)
            {
                AcStrategyType strategy = GetRepresentativeStrategy(o);
                AcStrategy acs          = AcStrategy.FromRawStrategy(strategy);
                nuint size              = (CoeffOrder.CoeffOrderOffsets[3 * o + 1] - CoeffOrder.CoeffOrderOffsets[3 * o]) * FrameDimensions.kDCTBlockSize;
                Span<uint> natural      = naturalBuffer.AsSpan(0, (int)size);

                acs.ComputeNaturalCoeffOrder(natural);

                for (int c = 0; c < 3; ++c)
                {
                    int offset = (int)CoeffOrder.CoeffOrderOffset(o, c);

                    for (int i = 0; i < (int)size; ++i)
                    {
                        orderOut[offset + i] = (ushort)natural[i];
                    }
                }
            }
        }

        private static AcStrategyType GetRepresentativeStrategy(int orderIdx)
        {
            for (int s = 0; s < (int)AcStrategy.kNumValidStrategies; ++s)
            {
                if (CoeffOrder.StrategyOrder[s] == orderIdx)
                {
                    return (AcStrategyType)s;
                }
            }

            return AcStrategyType.DCT;
        }

        private static void AccumulateCoeffOrderStats(
            AcQuantBlockArray acs,
            AcStrategyImage acStrategy,
            FrameDimensions frameDim,
            ushort currentUsedOrders,
            int[] numZeros,
            int[] numNonZeros)
        {
            nuint xsizeBlocks = frameDim.XSizeBlocks;
            nuint ysizeBlocks = frameDim.YSizeBlocks;
            int blockIndex    = 0;

            for (nuint by = 0; by < ysizeBlocks; ++by)
            {
                AcStrategyRow row = acStrategy.ConstRow(by);

                for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                {
                    AcStrategy s = row[bx];

                    if (!s.IsFirstBlock())
                    {
                        continue;
                    }

                    int ord = CoeffOrder.StrategyOrder[(int)s.Strategy()];

                    if ((currentUsedOrders & (1 << ord)) != 0 && blockIndex < acs.NumBlocks)
                    {
                        nuint size = (CoeffOrder.CoeffOrderOffsets[3 * ord + 1] - CoeffOrder.CoeffOrderOffsets[3 * ord]) * FrameDimensions.kDCTBlockSize;

                        for (int c = 0; c < 3; ++c)
                        {
                            int baseOffset        = (int)CoeffOrder.CoeffOrderOffset(ord, c);
                            ReadOnlySpan<int> blk = acs.Block(blockIndex, c, (int)size);

                            for (int k = 0; k < (int)size; ++k)
                            {
                                if (blk[k] == 0)
                                {
                                    numZeros[baseOffset + k]++;
                                }
                                else
                                {
                                    numNonZeros[baseOffset + k]++;
                                }
                            }
                        }
                    }

                    blockIndex += (int)s.CoveredBlocksX() * (int)s.CoveredBlocksY();
                }
            }
        }

        private static void BuildSortedCoeffOrders(
            ushort currentUsedOrders,
            ushort prevCustomOrders,
            ref uint allUsedOrders,
            int[] numZeros,
            int[] numNonZeros,
            Span<ushort> orderOut)
        {
            _ = prevCustomOrders;
            _ = numNonZeros;

            ushort actuallyCustomized = 0;
            uint[] naturalBuffer      = new uint[CoeffOrder.kCoeffOrderMaxSize];

            for (int o = 0; o < CoeffOrder.kNumOrders; ++o)
            {
                if ((currentUsedOrders & (1 << o)) == 0)
                {
                    continue;
                }

                AcStrategy acs     = AcStrategy.FromRawStrategy(GetRepresentativeStrategy(o));
                nuint size         = (CoeffOrder.CoeffOrderOffsets[3 * o + 1] - CoeffOrder.CoeffOrderOffsets[3 * o]) * FrameDimensions.kDCTBlockSize;
                nuint llf          = acs.CoveredBlocksX() * acs.CoveredBlocksY();
                Span<uint> natural = naturalBuffer.AsSpan(0, (int)size);

                acs.ComputeNaturalCoeffOrder(natural);

                bool isOrderCustom = false;

                for (int c = 0; c < 3; ++c)
                {
                    int offset                   = (int)CoeffOrder.CoeffOrderOffset(o, c);
                    (int Pos, int Zeros)[] items = new (int Pos, int Zeros)[(int)size];

                    for (int i = 0; i < (int)size; ++i)
                    {
                        ushort coeffIdx = (ushort)natural[i];
                        int z           = i < (int)llf ? -1 : numZeros[offset + coeffIdx];

                        items[i] = (i, z);
                    }

                    Array.Sort(items, static (a, b) =>
                    {
                        int cmp = a.Zeros.CompareTo(b.Zeros);

                        return cmp != 0 ? cmp : a.Pos.CompareTo(b.Pos);
                    });

                    for (int i = 0; i < (int)size; ++i)
                    {
                        ushort coeffIdx      = (ushort)natural[items[i].Pos];
                        orderOut[offset + i] = coeffIdx;

                        if (items[i].Pos != i)
                        {
                            isOrderCustom = true;
                        }
                    }
                }

                if (isOrderCustom)
                {
                    actuallyCustomized |= (ushort)(1 << o);
                }
            }

            allUsedOrders |= actuallyCustomized;
        }

        /// <summary>
        /// Encodes a permutation of size <paramref name="size"/> into the token stream using Lehmer coding.
        /// </summary>
        public static void EncodePermutation(
            ReadOnlySpan<ushort> order,
            nuint skip,
            nuint size,
            List<Token> tokens,
            int inOutOffset = 0)
        {
            _             = inOutOffset;
            uint[] lehmer = new uint[size];
            uint[] temp   = new uint[size + 1];
            uint[] ord32  = new uint[size];

            for (nuint i = 0; i < size; ++i)
            {
                ord32[i] = order[(int)i];
            }

            JxlSharp.Decoder.LehmerCode.ComputeLehmerCode(ord32, temp, size, lehmer);

            nuint end = size;

            while (end > skip && lehmer[end - 1] == 0)
            {
                --end;
            }

            tokens.Add(new Token((uint)CoeffOrder.CoeffOrderContext((uint)size), (uint)(end - skip)));

            uint last = 0;

            for (nuint i = skip; i < end; ++i)
            {
                tokens.Add(new Token((uint)CoeffOrder.CoeffOrderContext(last), lehmer[i]));
                last = lehmer[i];
            }
        }

        /// <summary>
        /// Encodes custom coefficient orders into the bitstream.
        /// </summary>
        public static Status EncodeCoeffOrders(
            ushort usedOrders,
            ReadOnlySpan<ushort> order,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (usedOrders == 0)
            {
                return Status.OkStatus;
            }

            List<List<Token>> tokens = new List<List<Token>> { new List<Token>() };
            uint[] naturalOrder      = new uint[CoeffOrder.kCoeffOrderMaxSize];
            ushort[] naturalOrderLut = new ushort[CoeffOrder.kCoeffOrderMaxSize];
            ushort[] permuted        = new ushort[CoeffOrder.kCoeffOrderMaxSize];

            for (int o = 0; o < CoeffOrder.kNumOrders; ++o)
            {
                if ((usedOrders & (1 << o)) == 0)
                {
                    continue;
                }

                AcStrategy acs = AcStrategy.FromRawStrategy(GetRepresentativeStrategy(o));
                nuint size     = (CoeffOrder.CoeffOrderOffsets[3 * o + 1] - CoeffOrder.CoeffOrderOffsets[3 * o]) * FrameDimensions.kDCTBlockSize;
                nuint llf      = acs.CoveredBlocksX() * acs.CoveredBlocksY();

                acs.ComputeNaturalCoeffOrder(naturalOrder.AsSpan(0, (int)size));

                for (nuint i = 0; i < size; ++i)
                {
                    naturalOrderLut[naturalOrder[i]] = (ushort)i;
                }

                for (int c = 0; c < 3; ++c)
                {
                    int offset = (int)CoeffOrder.CoeffOrderOffset(o, c);

                    for (nuint i = 0; i < size; ++i)
                    {
                        permuted[i] = naturalOrderLut[order[offset + (int)i]];
                    }

                    EncodePermutation(permuted, llf, size, tokens[0]);
                }
            }

            EntropyEncodingData codes  = new EntropyEncodingData();
            HistogramParams histParams = new HistogramParams();
            StatusOr<nuint> buildRes   = EncAns.BuildAndEncodeHistograms(
                null,
                histParams,
                CoeffOrder.kPermutationContexts,
                tokens,
                codes,
                writer,
                (layer, auxOut));

            if (!buildRes.IsOk)
            {
                return buildRes.Status;
            }

            return EncAns.WriteTokens(tokens[0], codes, 0, writer, layer, auxOut);
        }
    }

    /// <summary>
    /// Simple container for per-block quantized AC coefficients used during order statistics.
    /// </summary>
    public sealed class AcQuantBlockArray
    {
        private readonly int[][] _channelData;

        /// <summary>
        /// Initializes a new instance of the <see cref="AcQuantBlockArray"/> class.
        /// </summary>
        public AcQuantBlockArray(int numBlocks)
        {
            NumBlocks    = numBlocks;
            _channelData = new int[3][];

            for (int c = 0; c < 3; ++c)
            {
                _channelData[c] = new int[numBlocks * (int)FrameDimensions.kDCTBlockSize];
            }
        }

        /// <summary>
        /// Gets the number of 8x8 blocks stored.
        /// </summary>
        public int NumBlocks { get; }

        /// <summary>
        /// Returns a span over the coefficients starting at <paramref name="blockIndex"/> for channel <paramref name="c"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<int> Block(int blockIndex, int c, int count)
        {
            return _channelData[c].AsSpan(blockIndex * (int)FrameDimensions.kDCTBlockSize, count);
        }
    }

    /// <summary>
    /// Configuration buffers and tables for AC strategy selection heuristics.
    /// Ports <c>jxl::ACSConfig</c> from <c>lib/jxl/enc_ac_strategy.h</c>.
    /// </summary>
    public struct ACSConfig
    {
        /// <summary>Dequantization matrices.</summary>
        public DequantMatrices? Dequant;

        /// <summary>Base information-loss multiplier.</summary>
        public float InfoLossMultiplier;

        /// <summary>Base cost delta.</summary>
        public float CostDelta;

        /// <summary>Memory of row 0 of quantization field.</summary>
        public Memory<float> QuantFieldRow;

        /// <summary>Stride of quantization field.</summary>
        public nuint QuantFieldStride;

        /// <summary>Memory of row 0 of masking field.</summary>
        public Memory<float> MaskingFieldRow;

        /// <summary>Stride of masking field.</summary>
        public nuint MaskingFieldStride;

        /// <summary>Memory of row 0 of 1x1 masking field.</summary>
        public Memory<float> Masking1x1FieldRow;

        /// <summary>Stride of 1x1 masking field.</summary>
        public nuint Masking1x1FieldStride;

        /// <summary>Row memory for X channel.</summary>
        public Memory<float> SrcRowX;

        /// <summary>Row memory for Y channel.</summary>
        public Memory<float> SrcRowY;

        /// <summary>Row memory for B channel.</summary>
        public Memory<float> SrcRowB;

        /// <summary>Stride of XYB source planes.</summary>
        public nuint SrcStride;

        /// <summary>Returns a span starting at the pixel at (x, y) in channel c.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Span<float> Pixel(nuint c, nuint x, nuint y)
        {
            Memory<float> baseRow = c == 0 ? SrcRowX : (c == 1 ? SrcRowY : SrcRowB);

            return baseRow.Span.Slice((int)(y * SrcStride + x));
        }

        /// <summary>Returns the masking value at block (bx, by).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Masking(nuint bx, nuint by)
        {
            return MaskingFieldRow.Span[(int)(by * MaskingFieldStride + bx)];
        }

        /// <summary>Returns the span of the 2x2 masking values at block (bx, by).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Span<float> MaskingPtr1x1(nuint bx, nuint by)
        {
            return Masking1x1FieldRow.Span.Slice((int)(2 * by * Masking1x1FieldStride + 2 * bx));
        }

        /// <summary>Returns the quantization value at block (bx, by).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float Quant(nuint bx, nuint by)
        {
            return QuantFieldRow.Span[(int)(by * QuantFieldStride + bx)];
        }
    }

    /// <summary>
    /// Heuristics for selecting optimal AC transform strategies per block/superblock.
    /// Ports <c>jxl::AcStrategyHeuristics</c> from <c>lib/jxl/enc_ac_strategy.h</c> and <c>enc_ac_strategy.cc</c>.
    /// </summary>
    public sealed class AcStrategyHeuristics
    {
        private ACSConfig _config;
        private ImagePlane<float>? _qf;
        private ImagePlane<float>? _masking;
        private ImagePlane<float>? _mask1x1;

        /// <summary>
        /// Gets the underlying configuration structure.
        /// </summary>
        public ref ACSConfig Config => ref _config;

        /// <summary>
        /// Initializes the AC strategy heuristics state for the given XYB image and fields.
        /// </summary>
        public void Init(
            Image3<float> src,
            Rect rectIn,
            ImagePlane<float> quantField,
            ImagePlane<float> mask,
            ImagePlane<float> mask1x1,
            DequantMatrices matrices)
        {
            _config.Dequant = matrices;
            _qf             = quantField;
            _masking        = mask;
            _mask1x1        = mask1x1;

            _config.InfoLossMultiplier = 1.2f;
            _config.CostDelta          = 10.833273317067883f;
        }

        /// <summary>
        /// Prepares temporary workspaces for parallel strategy search.
        /// </summary>
        public Status PrepareForThreads(nuint numThreads)
        {
            return Status.OkStatus;
        }

        /// <summary>
        /// Processes a block rect and fills the best AC strategy into <paramref name="acStrategy"/>.
        /// </summary>
        public Status ProcessRect(
            Rect rect,
            ColorCorrelationMap cmap,
            AcStrategyImage acStrategy,
            nuint thread,
            CompressParams cparams,
            Image3<float> src)
        {
            _ = thread;

            return ProcessRectParallel(rect, cmap, acStrategy, null, cparams, src);
        }

        /// <summary>
        /// Processes a block rect in parallel across <paramref name="pool"/> and fills the best AC strategy into <paramref name="acStrategy"/>.
        /// </summary>
        public Status ProcessRectParallel(
            Rect rect,
            ColorCorrelationMap cmap,
            AcStrategyImage acStrategy,
            ThreadPool? pool,
            CompressParams cparams,
            Image3<float> src)
        {
            if (cparams.SpeedTier >= SpeedTier.kCheetah || _qf is null || _masking is null || _config.Dequant is null)
            {
                acStrategy.FillDCT8(rect);

                return Status.OkStatus;
            }

            const float kBias         = 0.13731742964354549f;
            float butteraugliDistance = cparams.ButteraugliDistance;
            float ratio               = MathF.Max(0.05f, (butteraugliDistance + kBias) / (1.0f + kBias));
            float infoLossMul         = _config.InfoLossMultiplier * MathF.Pow(ratio, 0.33677807f);
            float zerosMul            = 9.3089059f * MathF.Pow(ratio, 0.50990927f);
            float costDelta           = _config.CostDelta * MathF.Pow(ratio, 0.36702941f);
            nuint xsizeBlocks         = (nuint)rect.XSize;
            nuint ysizeBlocks         = (nuint)rect.YSize;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysizeBlocks,
                ThreadPool.NoInit,
                (by, _) =>
                {
                    nuint globalBy = (nuint)rect.Y0 + by;

                    for (nuint bx = 0; bx < xsizeBlocks; ++bx)
                    {
                        nuint globalBx      = (nuint)rect.X0 + bx;
                        AcStrategyType best = FindBest8x8Strategy(
                            src,
                            cmap,
                            globalBx,
                            globalBy,
                            butteraugliDistance,
                            cparams.SpeedTier,
                            (infoLossMul, zerosMul, costDelta));

                        acStrategy.Set(globalBx, globalBy, best);
                    }

                    return Status.OkStatus;
                },
                "AcStrategyHeuristics");
        }

        /// <summary>
        /// Finalizes AC strategy selection and optionally records debugging information.
        /// </summary>
        public Status Finalize(
            FrameDimensions frameDim,
            AcStrategyImage acStrategy,
            AuxOut? auxOut)
        {
            if (auxOut is not null)
            {
                CountStrategyTypes(frameDim, acStrategy, auxOut);
            }

            return Status.OkStatus;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private AcStrategyType FindBest8x8Strategy(
            Image3<float> src,
            ColorCorrelationMap cmap,
            nuint bx,
            nuint by,
            float butteraugliDistance,
            SpeedTier speedTier,
            (float InfoLossMul, float ZerosMul, float CostDelta) weights)
        {
            ReadOnlySpan<AcStrategyType> candidates = stackalloc AcStrategyType[]
            {
                AcStrategyType.DCT,
                AcStrategyType.DCT4X4,
                AcStrategyType.DCT2X2,
                AcStrategyType.IDENTITY,
                AcStrategyType.DCT4X8,
                AcStrategyType.DCT8X4,
                AcStrategyType.AFV0,
                AcStrategyType.AFV1,
                AcStrategyType.AFV2,
                AcStrategyType.AFV3,
            };

            ReadOnlySpan<float> entropyMul = stackalloc float[]
            {
                1.0f,
                1.08f / 0.8f,
                0.95f / 0.8f,
                1.04275425f / 0.8f,
                0.85931637f / 0.8f,
                0.85931637f / 0.8f,
                0.81779490f / 0.8f,
                0.81779490f / 0.8f,
                0.81779490f / 0.8f,
                0.81779490f / 0.8f,
            };

            float quant   = _qf!.RowSpan(by)[(int)bx];
            float masking = _masking!.RowSpan(by)[(int)bx];
            nuint tx      = bx / ChromaFromLumaConstants.kColorTileDimInBlocks;
            nuint ty      = by / ChromaFromLumaConstants.kColorTileDimInBlocks;
            float cmapX   = cmap.YtoXMap is not null ? cmap.Base.YtoXRatio(cmap.YtoXMap.RowSpan(ty)[(int)tx]) : 0.0f;
            float cmapB   = cmap.YtoBMap is not null ? cmap.Base.YtoBRatio(cmap.YtoBMap.RowSpan(ty)[(int)tx]) : 0.0f;

            Span<float> block  = stackalloc float[3 * 64];
            Span<float> coeffs = stackalloc float[3 * 64];
            Span<float> recon  = stackalloc float[3 * 64];

            Load8x8Block(src, bx, by, block);

            int numCandidates       = speedTier <= SpeedTier.kWombat ? candidates.Length : 4;
            float bestCost          = float.MaxValue;
            AcStrategyType bestType = AcStrategyType.DCT;

            for (int i = 0; i < numCandidates; ++i)
            {
                AcStrategyType cand = candidates[i];
                float mul           = AdjustEntropyMul8x8(cand, entropyMul[i], butteraugliDistance);
                float entropyCost   = ForwardAndRoundtrip8x8(cand, quant, (cmapX, cmapB), (weights.CostDelta, weights.ZerosMul), block, coeffs, recon);
                float lossScalar    = Compute8x8LossScalar(recon, masking, quant);
                float cost          = (entropyCost * mul) + (weights.InfoLossMul * lossScalar);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestType = cand;
                }
            }

            return bestType;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AdjustEntropyMul8x8(AcStrategyType type, float entropyMul, float butteraugliTarget)
        {
            if ((type == AcStrategyType.DCT2X2 || type == AcStrategyType.IDENTITY) && butteraugliTarget < 5.0f)
            {
                float weight = (5.0f - butteraugliTarget) * 0.2f;
                entropyMul  -= 0.4f * weight * weight;
            }

            if (type != AcStrategyType.DCT &&
                type != AcStrategyType.DCT2X2 &&
                type != AcStrategyType.IDENTITY &&
                butteraugliTarget > 4.0f)
            {
                float mul = butteraugliTarget < 12.0f ? 8.0f / (butteraugliTarget - 4.0f) : 1.0f;
                entropyMul += 0.5f * mul;
            }

            return entropyMul;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Load8x8Block(Image3<float> src, nuint bx, nuint by, Span<float> block)
        {
            nuint x0    = bx * FrameDimensions.kBlockDim;
            nuint y0    = by * FrameDimensions.kBlockDim;
            nuint xsize = src.XSize;
            nuint ysize = src.YSize;

            if (x0 + FrameDimensions.kBlockDim <= xsize && y0 + FrameDimensions.kBlockDim <= ysize)
            {
                int ix0 = (int)x0;

                for (int c = 0; c < 3; ++c)
                {
                    ImagePlane<float> plane = src.Plane((nuint)c);
                    int cBase               = c * 64;

                    for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
                    {
                        plane.RowSpan(y0 + y).Slice(ix0, 8).CopyTo(block.Slice(cBase + (int)(y * 8), 8));
                    }
                }

                return;
            }

            for (int c = 0; c < 3; ++c)
            {
                ImagePlane<float> plane = src.Plane((nuint)c);

                for (nuint y = 0; y < FrameDimensions.kBlockDim; ++y)
                {
                    nuint sy                = Math.Min(y0 + y, ysize - 1);
                    ReadOnlySpan<float> row = plane.RowSpan(sy);

                    for (nuint x = 0; x < FrameDimensions.kBlockDim; ++x)
                    {
                        nuint sx = Math.Min(x0 + x, xsize - 1);

                        block[c * 64 + (int)(y * FrameDimensions.kBlockDim + x)] = row[(int)sx];
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private float ForwardAndRoundtrip8x8(
            AcStrategyType strategy,
            float quant,
            (float CmapX, float CmapB) cmap,
            (float CostDelta, float ZerosMul) costs,
            Span<float> block,
            Span<float> coeffs,
            Span<float> recon)
        {
            Span<float> scratch = stackalloc float[256];

            for (int c = 0; c < 3; ++c)
            {
                EncTransforms.TransformFromPixels(
                    strategy,
                    block.Slice(c * 64, 64),
                    FrameDimensions.kBlockDim,
                    coeffs.Slice(c * 64, 64),
                    scratch);

                coeffs[c * 64] = 0.0f;
            }

            float entropyCost = QuantizeAndDequantize8x8(strategy, quant, cmap.CmapX, cmap.CmapB, costs.CostDelta, costs.ZerosMul, coeffs);

            for (int c = 0; c < 3; ++c)
            {
                DecTransforms.TransformToPixels(
                    strategy,
                    coeffs.Slice(c * 64, 64),
                    recon.Slice(c * 64, 64),
                    FrameDimensions.kBlockDim,
                    scratch);
            }

            return entropyCost;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float QuantizeAndDequantize8x8(
            AcStrategyType strategy,
            float quant,
            float cmapX,
            float cmapB,
            float costDelta,
            float zerosMul,
            Span<float> coeffs)
        {
            float entropy = 0.0f;

            for (int c = 0; c < 3; ++c)
            {
                ReadOnlySpan<float> invMat = _config.Dequant!.InvMatrixSpan(strategy, (nuint)c, 64);
                ReadOnlySpan<float> mat    = _config.Dequant.MatrixSpan(strategy, (nuint)c, 64);
                float cflFactor            = c == 0 ? cmapX : (c == 2 ? cmapB : 0.0f);
                float entropySum           = 0.0f;
                uint numNzeros             = 0;

                for (int k = 0; k < 64; ++k)
                {
                    float val  = (coeffs[c * 64 + k] - cflFactor * coeffs[64 + k]) * (invMat[k] * quant);
                    float rval = MathF.Round(val);
                    float diff = val - rval;
                    float q    = MathF.Abs(rval);

                    entropySum += MathF.Sqrt(q);

                    if (q != 0.0f)
                    {
                        numNzeros++;
                    }

                    coeffs[c * 64 + k] = mat[k] * diff;
                }

                entropy   += costDelta * entropySum;
                uint nbits = (uint)Bits.CeilLog2Nonzero(numNzeros + 1) + 1;
                entropy   += zerosMul * ((uint)Bits.CeilLog2Nonzero(nbits + 17) + nbits);
            }

            return entropy;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float Compute8x8LossScalar(
            ReadOnlySpan<float> reconError,
            float masking,
            float quant)
        {
            ReadOnlySpan<float> maskuLut   = stackalloc float[] { 12.0f, 0.0f, 4.0f };
            ReadOnlySpan<float> channelMul = stackalloc float[] { 20441408.0f, 1.0f, 1.2667701f };
            float loss                     = 0.0f;

            for (int c = 0; c < 3; ++c)
            {
                float masku = masking + maskuLut[c];
                float lossc = 0.0f;

                for (int i = 0; i < 64; ++i)
                {
                    float v  = masku * reconError[c * 64 + i];
                    float v2 = v * v;
                    float v4 = v2 * v2;

                    lossc += v4 * v4;
                }

                loss += channelMul[c] * lossc;
            }

            return MathF.Pow(loss * (1.0f / 64.0f), 0.125f) * 64.0f / MathF.Max(1e-4f, quant);
        }

        private static void CountStrategyTypes(
            FrameDimensions frameDim,
            AcStrategyImage acStrategy,
            AuxOut auxOut)
        {
            for (nuint by = 0; by < frameDim.YSizeBlocks; ++by)
            {
                AcStrategyRow row = acStrategy.ConstRow(by);

                for (nuint bx = 0; bx < frameDim.XSizeBlocks; ++bx)
                {
                    AcStrategy acs = row[bx];

                    if (!acs.IsFirstBlock())
                    {
                        continue;
                    }

                    auxOut.NumBlocks += acs.CoveredBlocksX() * acs.CoveredBlocksY();
                }
            }
        }
    }
}
