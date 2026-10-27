// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Forward spatial-to-frequency VarDCT transforms, LLF/DC extraction, and AFV corner-DCT.
    /// Ported from <c>lib/jxl/enc_transforms.h</c>, <c>lib/jxl/enc_transforms-inl.h</c>, and <c>lib/jxl/enc_transforms.cc</c>.
    /// </summary>
    public static class EncTransforms
    {
        private static readonly float[][] Afv4x4Basis =
        {
            new float[]
            {
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
            },
            new float[]
            {
                0.876902929799142f,   0.2206518106944235f,  -0.10140050393753763f, -0.1014005039375375f,
                0.2206518106944236f,  -0.10140050393753777f, -0.10140050393753772f, -0.10140050393753763f,
                -0.10140050393753758f, -0.10140050393753769f, -0.1014005039375375f,  -0.10140050393753768f,
                -0.10140050393753768f, -0.10140050393753759f, -0.10140050393753763f, -0.10140050393753741f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  0.40670075830260755f, 0.44444816619734445f,
                0.0f,                  0.0f,                  0.19574399372042936f, 0.2929100136981264f,
                -0.40670075830260716f, -0.19574399372042872f, 0.0f,                 0.11379074460448091f,
                -0.44444816619734384f, -0.29291001369812636f, -0.1137907446044814f, 0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.21255748058288748f, 0.3085497062849767f,
                0.0f,                  0.4706702258572536f,   -0.1621205195722993f,  0.0f,
                -0.21255748058287047f, -0.16212051957228327f, -0.47067022585725277f, -0.1464291867126764f,
                0.3085497062849487f,   0.0f,                  -0.14642918671266536f, 0.4251149611657548f,
            },
            new float[]
            {
                0.0f, -0.7071067811865474f, 0.0f, 0.0f,
                0.7071067811865476f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
            },
            new float[]
            {
                -0.4105377591765233f,  0.6235485373547691f,   -0.06435071657946274f, -0.06435071657946266f,
                0.6235485373547694f,   -0.06435071657946284f, -0.0643507165794628f,  -0.06435071657946274f,
                -0.06435071657946272f, -0.06435071657946279f, -0.06435071657946266f, -0.06435071657946277f,
                -0.06435071657946277f, -0.06435071657946273f, -0.06435071657946274f, -0.0643507165794626f,
            },
            new float[]
            {
                0.0f,                  0.0f,                   -0.4517556589999482f, 0.15854503551840063f,
                0.0f,                  -0.04038515160822202f,  0.0074182263792423875f, 0.39351034269210167f,
                -0.45175565899994635f, 0.007418226379244351f,  0.1107416575309343f,  0.08298163094882051f,
                0.15854503551839705f,  0.3935103426921022f,    0.0829816309488214f,  -0.45175565899994796f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.304684750724869f,  0.5112616136591823f,
                0.0f,                  0.0f,                  -0.290480129728998f,  -0.06578701549142804f,
                0.304684750724884f,    0.2904801297290076f,   0.0f,                 -0.23889773523344604f,
                -0.5112616136592012f,  0.06578701549142545f,  0.23889773523345467f, 0.0f,
            },
            new float[]
            {
                0.0f,                 0.0f,                  0.3017929516615495f,   0.25792362796341184f,
                0.0f,                 0.16272340142866204f,  0.09520022653475037f,  0.0f,
                0.3017929516615503f,  0.09520022653475055f,  -0.16272340142866173f, -0.35312385449816297f,
                0.25792362796341295f, 0.0f,                  -0.3531238544981624f,  -0.6035859033230976f,
            },
            new float[]
            {
                0.0f,                  0.0f, 0.40824829046386274f, 0.0f,
                0.0f,                  0.0f, 0.0f,                 -0.4082482904638628f,
                -0.4082482904638635f,  0.0f, 0.0f,                 -0.40824829046386296f,
                0.0f,                  0.4082482904638634f, 0.408248290463863f, 0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 0.1747866975480809f,   0.0812611176717539f,
                0.0f,                  0.0f,                 -0.3675398009862027f,  -0.307882213957909f,
                -0.17478669754808135f, 0.3675398009862011f,  0.0f,                  0.4826689115059883f,
                -0.08126111767175039f, 0.30788221395790305f, -0.48266891150598584f, 0.0f,
            },
            new float[]
            {
                0.0f,                 0.0f,                  -0.21105601049335784f, 0.18567180916109802f,
                0.0f,                 0.0f,                  0.49215859013738733f,  -0.38525013709251915f,
                0.21105601049335806f, -0.49215859013738905f, 0.0f,                  0.17419412659916217f,
                -0.18567180916109904f, 0.3852501370925211f,  -0.1741941265991621f,  0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 -0.14266084808807264f, -0.3416446842253372f,
                0.0f,                  0.7367497537172237f,  0.24627107722075148f,  -0.08574019035519306f,
                -0.14266084808807344f, 0.24627107722075137f, 0.14883399227113567f,  -0.04768680350229251f,
                -0.3416446842253373f,  -0.08574019035519267f, -0.047686803502292804f, -0.14266084808807242f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.13813540350758585f, 0.3302282550303788f,
                0.0f,                  0.08755115000587084f,  -0.07946706605909573f, -0.4613374887461511f,
                -0.13813540350758294f, -0.07946706605910261f, 0.49724647109535086f,  0.12538059448563663f,
                0.3302282550303805f,   -0.4613374887461554f,  0.12538059448564315f,  -0.13813540350758452f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 -0.17437602599651067f, 0.0702790691196284f,
                0.0f,                  -0.2921026642334881f, 0.3623817333531167f,   0.0f,
                -0.1743760259965108f,  0.36238173335311646f, 0.29210266423348785f,  -0.4326608024727445f,
                0.07027906911962818f,  0.0f,                 -0.4326608024727457f,  0.34875205199302267f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  0.11354987314994337f,  -0.07417504595810355f,
                0.0f,                  0.19402893032594343f,  -0.435190496523228f,   0.21918684838857466f,
                0.11354987314994257f,  -0.4351904965232251f,  0.5550443808910661f,   -0.25468277124066463f,
                -0.07417504595810233f, 0.2191868483885728f,   -0.25468277124066413f, 0.1135498731499429f,
            },
        };

        /// <summary>
        /// Computes the 4x4 forward AFV transform from 16 <paramref name="pixels"/> into 16 <paramref name="coeffs"/>.
        /// Ported from <c>jxl::AFVDCT4x4</c> in <c>lib/jxl/enc_transforms-inl.h</c>.
        /// </summary>
        /// <param name="pixels">Span of 16 input spatial pixels.</param>
        /// <param name="coeffs">Span of 16 output AFV coefficients.</param>
        public static void AFVDCT4x4(ReadOnlySpan<float> pixels, Span<float> coeffs)
        {
            for (int j = 0; j < 16; ++j)
            {
                float[] basisRow = Afv4x4Basis[j];
                float sum        = 0.0f;

                for (int i = 0; i < 16; ++i)
                {
                    sum = MathF.FusedMultiplyAdd(pixels[i], basisRow[i], sum);
                }

                coeffs[j] = sum;
            }
        }

        /// <summary>
        /// Transforms spatial <paramref name="pixels"/> into frequency <paramref name="coefficients"/> according to <paramref name="strategy"/>.
        /// Ported from <c>jxl::TransformFromPixels</c> in <c>lib/jxl/enc_transforms-inl.h</c> and <c>lib/jxl/enc_transforms.cc</c>.
        /// </summary>
        /// <param name="strategy">AC strategy type for the block.</param>
        /// <param name="pixels">Source pixel buffer.</param>
        /// <param name="pixelsStride">Row stride of <paramref name="pixels"/> in floats.</param>
        /// <param name="coefficients">Destination coefficient buffer.</param>
        /// <param name="scratchSpace">Scratch buffer of length at least <c>4 * max_block_area</c>.</param>
        public static void TransformFromPixels(
            AcStrategyType strategy,
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients,
            Span<float> scratchSpace)
        {
            switch (strategy)
            {
                case AcStrategyType.IDENTITY:
                    IdentityFromPixels(pixels, pixelsStride, coefficients);
                    break;

                case AcStrategyType.DCT8X4:
                    Dct8x4FromPixels(pixels, pixelsStride, coefficients, scratchSpace);
                    break;

                case AcStrategyType.DCT4X8:
                    Dct4x8FromPixels(pixels, pixelsStride, coefficients, scratchSpace);
                    break;

                case AcStrategyType.DCT4X4:
                    Dct4x4FromPixels(pixels, pixelsStride, coefficients, scratchSpace);
                    break;

                case AcStrategyType.DCT2X2:
                    Dct2x2FromPixels(pixels, pixelsStride, coefficients);
                    break;

                case AcStrategyType.AFV0:
                case AcStrategyType.AFV1:
                case AcStrategyType.AFV2:
                case AcStrategyType.AFV3:
                    AfvFromPixels((int)strategy - (int)AcStrategyType.AFV0, pixels, pixelsStride, coefficients, scratchSpace);
                    break;

                default:
                    AcStrategy acs = AcStrategy.FromRawStrategy(strategy);
                    int rows       = (int)(acs.CoveredBlocksY() * FrameDimensions.kBlockDim);
                    int cols       = (int)(acs.CoveredBlocksX() * FrameDimensions.kBlockDim);
                    DecTransforms.ComputeScaledDCT(rows, cols, new DctFrom(pixels, pixelsStride), coefficients, scratchSpace);
                    break;
            }
        }

        /// <summary>
        /// Computes 8x8 block DC values in <paramref name="dc"/> from the lowest-frequency (LLF) coefficients in <paramref name="block"/>.
        /// Ported from <c>jxl::DCFromLowestFrequencies</c> in <c>lib/jxl/enc_transforms-inl.h</c> and <c>lib/jxl/enc_transforms.cc</c>.
        /// </summary>
        /// <param name="strategy">AC strategy type.</param>
        /// <param name="block">Source coefficient buffer containing LLF values.</param>
        /// <param name="dc">Destination top-left DC span in the DC plane.</param>
        /// <param name="dcStride">Row stride of the DC plane in floats.</param>
        /// <param name="scratch">Scratch buffer for large multiblock strategies.</param>
        public static void DCFromLowestFrequencies(
            AcStrategyType strategy,
            ReadOnlySpan<float> block,
            Span<float> dc,
            nuint dcStride,
            Span<float> scratch)
        {
            AcStrategy acs = AcStrategy.FromRawStrategy(strategy);

            if (!acs.IsMultiblock())
            {
                dc[0] = block[0];

                return;
            }

            int rows       = (int)acs.CoveredBlocksY();
            int cols       = (int)acs.CoveredBlocksX();
            nuint inCols   = CoeffOrder.CoefficientColumns((nuint)rows, (nuint)cols) * FrameDimensions.kBlockDim;
            DctFrom inFrom = new DctFrom(block, inCols);
            DctTo outTo    = new DctTo(dc, dcStride);

            if (rows * cols <= 16)
            {
                Span<float> warmBlock   = stackalloc float[16];
                Span<float> warmScratch = stackalloc float[64];
                ReinterpretingIDCT(rows, cols, inFrom, outTo, warmBlock, warmScratch);
            }
            else
            {
                ReinterpretingIDCT(rows, cols, inFrom, outTo, scratch, scratch.Slice(rows * cols));
            }
        }

        private static void ReinterpretingIDCT(
            int rows,
            int cols,
            DctFrom input,
            DctTo output,
            Span<float> block,
            Span<float> scratchSpace)
        {
            int dctRows = rows * (int)FrameDimensions.kBlockDim;
            int dctCols = cols * (int)FrameDimensions.kBlockDim;

            if (rows < cols)
            {
                ReadOnlySpan<float> scaleY = DctScales.GetResampleScales(dctRows, rows);
                ReadOnlySpan<float> scaleX = DctScales.GetResampleScales(dctCols, cols);

                for (int y = 0; y < rows; ++y)
                {
                    float sy = scaleY[y];

                    for (int x = 0; x < cols; ++x)
                    {
                        block[y * cols + x] = input.Read((nuint)y, (nuint)x) * sy * scaleX[x];
                    }
                }
            }
            else
            {
                ReadOnlySpan<float> scaleY = DctScales.GetResampleScales(dctCols, cols);
                ReadOnlySpan<float> scaleX = DctScales.GetResampleScales(dctRows, rows);

                for (int y = 0; y < cols; ++y)
                {
                    float sy = scaleY[y];

                    for (int x = 0; x < rows; ++x)
                    {
                        block[y * rows + x] = input.Read((nuint)y, (nuint)x) * sy * scaleX[x];
                    }
                }
            }

            DecTransforms.ComputeScaledIDCT(rows, cols, block, output, scratchSpace);
        }

        private static void IdentityFromPixels(ReadOnlySpan<float> pixels, nuint pixelsStride, Span<float> coefficients)
        {
            Span<float> dcs = stackalloc float[4];

            for (int y = 0; y < 2; ++y)
            {
                for (int x = 0; x < 2; ++x)
                {
                    dcs[y * 2 + x] = IdentitySubblockFromPixels(y, x, pixels, pixelsStride, coefficients);
                }
            }

            float block00 = dcs[0];
            float block01 = dcs[1];
            float block10 = dcs[2];
            float block11 = dcs[3];

            coefficients[0] = (block00 + block01 + block10 + block11) * 0.25f;
            coefficients[1] = (block00 + block01 - block10 - block11) * 0.25f;
            coefficients[8] = (block00 - block01 + block10 - block11) * 0.25f;
            coefficients[9] = (block00 - block01 - block10 + block11) * 0.25f;
        }

        private static float IdentitySubblockFromPixels(
            int y,
            int x,
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients)
        {
            float blockDc   = 0.0f;
            float centerVal = pixels[(int)((nuint)(4 * y + 1) * pixelsStride + (nuint)(4 * x + 1))];

            for (int iy = 0; iy < 4; ++iy)
            {
                for (int ix = 0; ix < 4; ++ix)
                {
                    float pix = pixels[(int)((nuint)(y * 4 + iy) * pixelsStride + (nuint)(x * 4 + ix))];
                    blockDc  += pix;

                    if (ix != 0 || iy != 0)
                    {
                        coefficients[(y + iy * 2) * 8 + x + ix * 2] = pix - centerVal;
                    }
                }
            }

            coefficients[(y + 2) * 8 + x + 2] =
                pixels[(int)((nuint)(y * 4) * pixelsStride + (nuint)(x * 4))] - centerVal;

            return blockDc * (1.0f / 16.0f);
        }

        private static void Dct8x4FromPixels(
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients,
            Span<float> scratchSpace)
        {
            Span<float> dcs   = stackalloc float[2];
            Span<float> block = stackalloc float[32];

            for (int x = 0; x < 2; ++x)
            {
                DecTransforms.ComputeScaledDCT(8, 4, new DctFrom(pixels.Slice(x * 4), pixelsStride), block, scratchSpace);
                StoreHalfSubblock4x8(x, block, coefficients, out dcs[x]);
            }

            coefficients[0] = (dcs[0] + dcs[1]) * 0.5f;
            coefficients[8] = (dcs[0] - dcs[1]) * 0.5f;
        }

        private static void Dct4x8FromPixels(
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients,
            Span<float> scratchSpace)
        {
            Span<float> dcs   = stackalloc float[2];
            Span<float> block = stackalloc float[32];

            for (int y = 0; y < 2; ++y)
            {
                DecTransforms.ComputeScaledDCT(
                    4,
                    8,
                    new DctFrom(pixels.Slice((int)((nuint)(y * 4) * pixelsStride)), pixelsStride),
                    block,
                    scratchSpace);
                StoreHalfSubblock4x8(y, block, coefficients, out dcs[y]);
            }

            coefficients[0] = (dcs[0] + dcs[1]) * 0.5f;
            coefficients[8] = (dcs[0] - dcs[1]) * 0.5f;
        }

        private static void StoreHalfSubblock4x8(int offset, ReadOnlySpan<float> block, Span<float> coefficients, out float dcVal)
        {
            dcVal = block[0];

            for (int iy = 0; iy < 4; ++iy)
            {
                for (int ix = 0; ix < 8; ++ix)
                {
                    if (ix != 0 || iy != 0)
                    {
                        coefficients[(offset + iy * 2) * 8 + ix] = block[iy * 8 + ix];
                    }
                }
            }
        }

        private static void Dct4x4FromPixels(
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients,
            Span<float> scratchSpace)
        {
            Span<float> dcs   = stackalloc float[4];
            Span<float> block = stackalloc float[16];

            for (int y = 0; y < 2; ++y)
            {
                for (int x = 0; x < 2; ++x)
                {
                    DecTransforms.ComputeScaledDCT(
                        4,
                        4,
                        new DctFrom(pixels.Slice((int)((nuint)(y * 4) * pixelsStride + (nuint)(x * 4))), pixelsStride),
                        block,
                        scratchSpace);
                    StoreQuarterSubblock4x4(y, x, block, coefficients, out dcs[y * 2 + x]);
                }
            }

            float block00   = dcs[0];
            float block01   = dcs[1];
            float block10   = dcs[2];
            float block11   = dcs[3];
            coefficients[0] = (block00 + block01 + block10 + block11) * 0.25f;
            coefficients[1] = (block00 + block01 - block10 - block11) * 0.25f;
            coefficients[8] = (block00 - block01 + block10 - block11) * 0.25f;
            coefficients[9] = (block00 - block01 - block10 + block11) * 0.25f;
        }

        private static void StoreQuarterSubblock4x4(
            int y,
            int x,
            ReadOnlySpan<float> block,
            Span<float> coefficients,
            out float dcVal)
        {
            dcVal = block[0];

            for (int iy = 0; iy < 4; ++iy)
            {
                for (int ix = 0; ix < 4; ++ix)
                {
                    if (ix != 0 || iy != 0)
                    {
                        coefficients[(y + iy * 2) * 8 + x + ix * 2] = block[iy * 4 + ix];
                    }
                }
            }
        }

        private static void Dct2x2FromPixels(ReadOnlySpan<float> pixels, nuint pixelsStride, Span<float> coefficients)
        {
            const int kDim     = (int)FrameDimensions.kBlockDim;
            Span<float> coeffs = stackalloc float[(int)FrameDimensions.kDCTBlockSize];

            for (int y = 0; y < kDim; ++y)
            {
                for (int x = 0; x < kDim; ++x)
                {
                    coeffs[y * kDim + x] = pixels[(int)((nuint)y * pixelsStride + (nuint)x)];
                }
            }

            DCT2TopBlock(8, coeffs, kDim, coeffs);
            DCT2TopBlock(4, coeffs, kDim, coeffs);
            DCT2TopBlock(2, coeffs, kDim, coeffs);

            for (int i = 0; i < (int)FrameDimensions.kDCTBlockSize; ++i)
            {
                coefficients[i] = coeffs[i];
            }
        }

        private static void DCT2TopBlock(int s, ReadOnlySpan<float> block, int strideIn, Span<float> output)
        {
            const int kDim   = (int)FrameDimensions.kBlockDim;
            Span<float> temp = stackalloc float[(int)FrameDimensions.kDCTBlockSize];
            int num2x2       = s / 2;

            for (int y = 0; y < num2x2; ++y)
            {
                for (int x = 0; x < num2x2; ++x)
                {
                    float p00 = block[(y * 2) * strideIn + x * 2];
                    float p01 = block[(y * 2) * strideIn + x * 2 + 1];
                    float p10 = block[(y * 2 + 1) * strideIn + x * 2];
                    float p11 = block[(y * 2 + 1) * strideIn + x * 2 + 1];

                    temp[y * kDim + x]                      = (p00 + p01 + p10 + p11) * 0.25f;
                    temp[y * kDim + num2x2 + x]             = (p00 + p01 - p10 - p11) * 0.25f;
                    temp[(y + num2x2) * kDim + x]           = (p00 - p01 + p10 - p11) * 0.25f;
                    temp[(y + num2x2) * kDim + num2x2 + x]  = (p00 - p01 - p10 + p11) * 0.25f;
                }
            }

            for (int y = 0; y < s; ++y)
            {
                for (int x = 0; x < s; ++x)
                {
                    output[y * kDim + x] = temp[y * kDim + x];
                }
            }
        }

        private static void AfvFromPixels(
            int afvKind,
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> coefficients,
            Span<float> scratchSpace)
        {
            int afvX          = afvKind & 1;
            int afvY          = afvKind / 2;
            Span<float> pix   = stackalloc float[16];
            Span<float> coeff = stackalloc float[32];

            ReadAfvCornerSubblock(afvX, afvY, pixels, pixelsStride, pix);
            AFVDCT4x4(pix, coeff);
            StoreQuarterSubblock4x4(0, 0, coeff, coefficients, out float dc0);
            dc0 *= 0.25f;

            // DCT4x4 in (odd, even) positions.
            DecTransforms.ComputeScaledDCT(
                4,
                4,
                new DctFrom(pixels.Slice((int)((nuint)(afvY * 4) * pixelsStride + (nuint)(afvX == 1 ? 0 : 4))), pixelsStride),
                coeff,
                scratchSpace);
            StoreQuarterSubblock4x4(0, 1, coeff, coefficients, out float dc1);

            // DCT4x8 in bottom/top half.
            DecTransforms.ComputeScaledDCT(
                4,
                8,
                new DctFrom(pixels.Slice((int)((nuint)(afvY == 1 ? 0 : 4) * pixelsStride)), pixelsStride),
                coeff,
                scratchSpace);
            StoreHalfSubblock4x8(1, coeff, coefficients, out float dc2);

            coefficients[0] = dc0 * 0.25f + dc1 * 0.25f + dc2 * 0.5f;
            coefficients[1] = dc0 * 0.5f - dc1 * 0.5f;
            coefficients[8] = dc0 * 0.25f + dc1 * 0.25f - dc2 * 0.5f;
        }

        private static void ReadAfvCornerSubblock(
            int afvX,
            int afvY,
            ReadOnlySpan<float> pixels,
            nuint pixelsStride,
            Span<float> block)
        {
            for (int iy = 0; iy < 4; ++iy)
            {
                int dstY = afvY == 1 ? 3 - iy : iy;

                for (int ix = 0; ix < 4; ++ix)
                {
                    int dstX               = afvX == 1 ? 3 - ix : ix;
                    block[dstY * 4 + dstX] = pixels[(int)((nuint)(iy + afvY * 4) * pixelsStride + (nuint)(afvX * 4 + ix))];
                }
            }
        }
    }
}
