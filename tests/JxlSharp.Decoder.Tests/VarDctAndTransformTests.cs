// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Unit tests for <see cref="AcStrategy"/>, ported from <c>ac_strategy_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed unsafe class AcStrategyTests
    {
        /// <summary>
        /// Verifies strategy properties and natural coefficient order generation across all 27 valid strategies.
        /// </summary>
        [Test]
        public void AllValidStrategies_HaveConsistentDimensionsAndNaturalOrder()
        {
            for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
            {
                AcStrategy.IsRawStrategyValid(i).ShouldBeTrue();
                AcStrategy sut = AcStrategy.FromRawStrategy((byte)i);

                sut.IsFirstBlock().ShouldBeTrue();
                sut.RawStrategy().ShouldBe((byte)i);
                sut.Strategy().ShouldBe((AcStrategyType)i);

                nuint cx   = sut.CoveredBlocksX();
                nuint cy   = sut.CoveredBlocksY();
                nuint log2 = sut.Log2CoveredBlocks();
                nuint area = cx * cy;

                area.ShouldBe(1u << (int)log2);
                sut.IsMultiblock().ShouldBe(area > 1);

                uint[] naturalOrder = new uint[area * FrameDimensions.kDCTBlockSize];
                sut.ComputeNaturalCoeffOrder(naturalOrder);

                bool[] seen = new bool[naturalOrder.Length];

                for (int k = 0; k < naturalOrder.Length; k++)
                {
                    uint idx = naturalOrder[k];
                    idx.ShouldBeLessThan((uint)naturalOrder.Length);
                    seen[idx].ShouldBeFalse();
                    seen[idx] = true;
                }
            }

            AcStrategy.IsRawStrategyValid(-1).ShouldBeFalse();
            AcStrategy.IsRawStrategyValid(AcStrategy.kNumValidStrategies).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies that <c>DC -&gt; LowestFrequenciesFromDC -&gt; TransformToPixels -&gt; Downsampled DC</c> is an identity
        /// across all 27 valid AC strategies, ported from <c>AcStrategyRoundtripDownsample</c> in <c>ac_strategy_test.cc</c>.
        /// </summary>
        /// <param name="rawStrategy">Raw strategy index in <c>[0..26]</c>.</param>
        [TestCaseSource(nameof(AllStrategies))]
        public void AcStrategyRoundtripDownsample_PreservesDcValues(int rawStrategy)
        {
            AcStrategyType type = (AcStrategyType)rawStrategy;
            AcStrategy sut      = AcStrategy.FromRawStrategy(type);
            nuint covX          = sut.CoveredBlocksX();
            nuint covY          = sut.CoveredBlocksY();
            nuint stride        = covX * 8;

            float[] coeffs  = new float[AcStrategy.kMaxCoeffArea];
            float[] idct    = new float[AcStrategy.kMaxCoeffArea];
            float[] dc      = new float[AcStrategy.kMaxCoeffArea];
            float[] scratch = new float[AcStrategy.kMaxCoeffArea * 4];
            Rng rng         = new Rng((ulong)rawStrategy * 65537UL + 13UL);

            fixed (float* pCoeffs = coeffs)
            fixed (float* pIdct = idct)
            fixed (float* pDc = dc)
            fixed (float* pScratch = scratch)
            {
                for (nuint y = 0; y < covY; y++)
                {
                    for (nuint x = 0; x < covX; x++)
                    {
                        if ((x > 2 || y > 2) && rng.Bernoulli(0.95f))
                        {
                            continue;
                        }

                        VerifySingleDcImpulse(type, (covX, covY, stride), (x, y), pCoeffs, pIdct, pDc, pScratch);
                    }
                }
            }
        }

        private static int[] AllStrategies()
        {
            int[] result = new int[AcStrategy.kNumValidStrategies];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = i;
            }

            return result;
        }

        private static void VerifySingleDcImpulse(
            AcStrategyType type,
            (nuint CovX, nuint CovY, nuint Stride) dims,
            (nuint X, nuint Y) impulsePos,
            float* coeffs,
            float* idct,
            float* dc,
            float* scratch)
        {
            int area = (int)(dims.CovX * dims.CovY * 64);
            new Span<float>(coeffs, area).Clear();
            new Span<float>(dc, area).Clear();
            dc[impulsePos.Y * dims.Stride + impulsePos.X] = 0.2f;

            DecTransforms.LowestFrequenciesFromDC(type, dc, dims.Stride, coeffs, scratch);
            DecTransforms.TransformToPixels(type, coeffs, idct, dims.Stride, scratch);

            new Span<float>(dc, area).Clear();
            dc[impulsePos.Y * dims.Stride + impulsePos.X] = 0.2f;

            for (nuint dy = 0; dy < dims.CovY; dy++)
            {
                for (nuint dx = 0; dx < dims.CovX; dx++)
                {
                    float sum = SumBlock8x8(idct, dims.Stride, dx, dy) / 64.0f;
                    sum.ShouldBe(dc[dy * dims.Stride + dx], 1e-5f);
                }
            }
        }

        private static float SumBlock8x8(float* idct, nuint stride, nuint dx, nuint dy)
        {
            float sum = 0.0f;

            for (nuint iy = 0; iy < 8; iy++)
            {
                for (nuint ix = 0; ix < 8; ix++)
                {
                    sum += idct[(dy * 8 + iy) * stride + dx * 8 + ix];
                }
            }

            return sum;
        }
    }

    /// <summary>
    /// Unit tests for <see cref="AcStrategyRow"/> and <see cref="AcStrategyImage"/>.
    /// </summary>
    [TestFixture]
    public sealed unsafe class AcStrategyImageTests
    {
        /// <summary>
        /// Verifies <see cref="AcStrategyImage.Create"/>, <see cref="AcStrategyImage.Set"/>, overlap detection, and <see cref="AcStrategyRow"/> indexing.
        /// </summary>
        [Test]
        public void CreateAndSet_TracksMultiblockAndDetectsOverlap()
        {
            StatusOr<AcStrategyImage> imgOr = AcStrategyImage.Create(null, 8, 8);

            imgOr.IsOk.ShouldBeTrue();
            using AcStrategyImage sut = imgOr.Value;

            sut.xsize.ShouldBe((nuint)8);
            sut.ysize.ShouldBe((nuint)8);
            sut.FillDCT8();
            sut.CountBlocks(AcStrategyType.DCT).ShouldBe((nuint)64);

            sut.FillInvalid();
            sut.IsValid(0, 0).ShouldBeFalse();

            sut.SetNoBoundsCheck(0, 0, AcStrategyType.DCT16X16, check: true).IsOk.ShouldBeTrue();
            sut.IsValid(0, 0).ShouldBeTrue();
            sut.IsValid(1, 1).ShouldBeTrue();

            AcStrategyRow row0 = sut.ConstRow(0);
            row0[0].Strategy().ShouldBe(AcStrategyType.DCT16X16);
            row0[0].IsFirstBlock().ShouldBeTrue();
            row0[1].Strategy().ShouldBe(AcStrategyType.DCT16X16);
            row0[1].IsFirstBlock().ShouldBeFalse();

            // Overlapping block must fail when check is true.
            sut.SetNoBoundsCheck(1, 0, AcStrategyType.DCT, check: true).IsOk.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="LehmerCode"/> and <see cref="CoeffOrder"/>, ported from <c>coeff_order_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class CoeffOrderTests
    {
        /// <summary>
        /// Verifies Lehmer code computation and decoding for identity, swaps, slides, and random permutations.
        /// </summary>
        /// <param name="kind">Permutation pattern kind (0=identity, 1=swaps, 2=slides, 3=random).</param>
        /// <param name="len">Permutation length.</param>
        [TestCase(0, 256)]
        [TestCase(1, 256)]
        [TestCase(2, 256)]
        [TestCase(3, 256)]
        [TestCase(0, 4096)]
        [TestCase(1, 4096)]
        [TestCase(2, 4096)]
        [TestCase(3, 4096)]
        public void LehmerCodeRoundtrip_ReconstructsExactPermutation(int kind, int len)
        {
            uint[] perm = new uint[len];

            for (int i = 0; i < len; i++)
            {
                perm[i] = (uint)i;
            }

            ApplyPermutationPattern(kind, perm);

            uint[] tempEnc = new uint[len + 1];
            uint[] code    = new uint[len];
            Status encOk   = LehmerCode.ComputeLehmerCode(perm, tempEnc, (nuint)len, code);

            encOk.IsOk.ShouldBeTrue();

            uint[] tempDec = new uint[len * 2];
            uint[] decoded = new uint[len];
            Status decOk   = LehmerCode.DecodeLehmerCode(code, tempDec, (nuint)len, decoded);

            decOk.IsOk.ShouldBeTrue();
            decoded.ShouldBe(perm);
        }

        /// <summary>
        /// Verifies <see cref="CoeffOrder.DecodeCoeffOrders"/> with natural orders (<c>usedOrders == 0</c>).
        /// </summary>
        [Test]
        public void DecodeCoeffOrders_NoCustomOrders_PopulatesNaturalOrder()
        {
            uint[] order       = new uint[CoeffOrder.kCoeffOrderMaxSize];
            byte[] dummyStream = new byte[16];
            using BitReader br = new BitReader(dummyStream);

            uint usedAcs  = (1u << (int)AcStrategyType.DCT) | (1u << (int)AcStrategyType.DCT16X16);
            Status status = CoeffOrder.DecodeCoeffOrders(null, 0, usedAcs, order, br);

            status.IsOk.ShouldBeTrue();
            order[0].ShouldBe(0u);
            order[1].ShouldBeGreaterThan(0u);
            br.Close().IsOk.ShouldBeTrue();
        }

        private static void ApplyPermutationPattern(int kind, uint[] perm)
        {
            const int kSwaps = 32;
            int len          = perm.Length;
            Rng rng          = new Rng(0);

            if (kind == 1)
            {
                for (int i = 0; i < kSwaps; i++)
                {
                    int a = (int)rng.UniformU(0, (uint)(len - 1));
                    int b = (int)rng.UniformU(0, (uint)(len - 1));
                    (perm[a], perm[b]) = (perm[b], perm[a]);
                }
            }
            else if (kind == 2)
            {
                for (int i = 0; i < kSwaps; i++)
                {
                    int a     = (int)rng.UniformU(0, (uint)(len - 1));
                    int b     = (int)rng.UniformU(0, (uint)(len - 1));
                    int from  = Math.Min(a, b);
                    int to    = Math.Max(a, b);
                    uint save = perm[from];

                    for (int j = from; j < to; j++)
                    {
                        perm[j] = perm[j + 1];
                    }

                    perm[to] = save;
                }
            }
            else if (kind == 3)
            {
                rng.Shuffle(perm);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="DctQuantWeightParams"/>, <see cref="QuantEncoding"/>, and <see cref="DequantMatrices"/>,
    /// ported from <c>quant_weights_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class DequantMatricesTests
    {
        /// <summary>
        /// Verifies the <c>kSumRequiredXy</c> invariant from <c>quant_weights_test.cc</c>.
        /// </summary>
        [Test]
        public void Invariant_RequiredSizesSumToExpectedTotal()
        {
            ReadOnlySpan<int> rx = DequantMatrices.RequiredSizeX;
            ReadOnlySpan<int> ry = DequantMatrices.RequiredSizeY;

            rx.Length.ShouldBe(ry.Length);
            nuint sum = 0;

            for (int i = 0; i < rx.Length; i++)
            {
                sum += (nuint)(rx[i] * ry[i]);
            }

            sum.ShouldBe(DequantMatrices.kSumRequiredXy);
        }

        /// <summary>
        /// Verifies custom DC quantization factors and matrix computation for all 27 strategies.
        /// </summary>
        [Test]
        public void SetDCQuantAndEnsureComputed_ComputesAllTables()
        {
            using DequantMatrices sut = new DequantMatrices();
            float[] dcQuant           = { 1e+5f, 1e+3f, 1e+1f };

            sut.SetDCQuant(dcQuant);

            for (nuint c = 0; c < 3; c++)
            {
                sut.InvDCQuant(c).ShouldBe(dcQuant[c], 1e-3f);
                sut.DCQuant(c).ShouldBe(1.0f / dcQuant[c], 1e-6f);
            }

            uint allStrategiesMask = (1u << AcStrategy.kNumValidStrategies) - 1u;
            Status status          = sut.EnsureComputed(null, allStrategiesMask);

            status.IsOk.ShouldBeTrue();

            ReadOnlySpan<float> dctMatrix    = sut.MatrixSpan(AcStrategyType.DCT, 1, 64);
            ReadOnlySpan<float> dctInvMatrix = sut.InvMatrixSpan(AcStrategyType.DCT, 1, 64);

            dctInvMatrix[0].ShouldBe(0.0f);

            for (int k = 1; k < 64; k++)
            {
                (dctMatrix[k] * dctInvMatrix[k]).ShouldBe(1.0f, 1e-4f);
            }
        }

        /// <summary>
        /// Verifies <see cref="DctQuantWeightParams"/> and <see cref="QuantEncoding"/> factory methods.
        /// </summary>
        [Test]
        public void QuantEncodingFactories_CloneAndPreserveMode()
        {
            float[] bands          = { 1.0f, 0.5f, 0.25f };
            DctQuantWeightParams p = new DctQuantWeightParams(bands, bands, bands, 3);
            DctQuantWeightParams c = p.Clone();

            c.NumDistanceBands.ShouldBe((nuint)3);
            c.DistanceBands[0][1].ShouldBe(0.5f);

            int[] rawTable    = { 1, 2, 3, 4 };
            QuantEncoding raw = QuantEncoding.RAW(rawTable, 1);
            QuantEncoding cln = raw.Clone();

            cln.Mode.ShouldBe(QuantEncoding.QuantMode.kQuantModeRAW);
            cln.QrawTable.ShouldNotBeNull();
            cln.QrawTable[2].ShouldBe(3);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="QuantizerParams"/> and <see cref="Quantizer"/>, ported from <c>quantizer_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class QuantizerTests
    {
        /// <summary>
        /// Verifies <see cref="Quantizer.SetQuant"/> and <see cref="Quantizer.SetQuantField"/> parameter calculation.
        /// </summary>
        [Test]
        public void SetQuantAndSetQuantField_ComputeValidScaleAndQuantDc()
        {
            using DequantMatrices dequant = new DequantMatrices();
            Quantizer sut                 = new Quantizer(dequant);

            StatusOr<ImagePlane<int>> rawOr = ImagePlane<int>.Create(null, 8, 8);
            rawOr.IsOk.ShouldBeTrue();
            using ImagePlane<int> rawQf = rawOr.Value;

            sut.SetQuant(0.17f, 0.17f, rawQf);
            QuantizerParams p = sut.GetParams();

            p.GlobalScale.ShouldBeGreaterThan(0u);
            p.QuantDc.ShouldBeGreaterThan(0u);
            sut.InvQuantDc().ShouldBeGreaterThan(0.0f);
            sut.InvQuantAc(10).ShouldBeGreaterThan(0.0f);

            StatusOr<ImagePlane<float>> qfOr = ImagePlane<float>.Create(null, 8, 8);
            qfOr.IsOk.ShouldBeTrue();
            using ImagePlane<float> qf = qfOr.Value;
            ImageOps.FillImage(0.25f, qf);

            sut.SetQuantField(0.17f, qf, rawQf).IsOk.ShouldBeTrue();
            Quantizer.ClampVal(0.0f).ShouldBe(1);
            Quantizer.ClampVal(500.0f).ShouldBe(Quantizer.kQuantMax);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ColorCorrelation"/> and <see cref="ColorCorrelationMap"/>.
    /// </summary>
    [TestFixture]
    public sealed class ColorCorrelationTests
    {
        /// <summary>
        /// Verifies <see cref="ColorCorrelation"/> ratios, JPEG compatibility, and <see cref="ColorCorrelationMap.Create"/>.
        /// </summary>
        [Test]
        public void ColorCorrelationAndMap_ComputeExpectedRatios()
        {
            ColorCorrelation sut = new ColorCorrelation();

            sut.GetColorFactor().ShouldBe(ChromaFromLumaConstants.kDefaultColorFactor);
            sut.YtoXRatio(0).ShouldBe(0.0f);
            sut.YtoBRatio(0).ShouldBe(OpsinParams.kYToBRatio, 1e-6f);
            sut.IsJPEGCompatible().ShouldBeFalse();

            StatusOr<ColorCorrelationMap> mapOr = ColorCorrelationMap.Create(null, 128, 128, xyb: false);
            mapOr.IsOk.ShouldBeTrue();
            using ColorCorrelationMap map = mapOr.Value;

            map.Base.IsJPEGCompatible().ShouldBeTrue();
            map.YtoXMap.ShouldNotBeNull();
            map.YtoXMap!.xsize.ShouldBe((nuint)2);
            map.YtoXMap.ysize.ShouldBe((nuint)2);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="DctScales"/>, <see cref="DctFrom"/>, <see cref="DctTo"/>,
    /// <see cref="DctTranspose"/>, and <see cref="DecTransforms"/>, ported from <c>dct_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed unsafe class DecTransformsTests
    {
        /// <summary>
        /// Verifies 2D block transpose for N = 8, 16, 32, ported from <c>TransposeTest</c> in <c>dct_test.cc</c>.
        /// </summary>
        /// <param name="n">Block dimension.</param>
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public void Transpose_SwapsRowsAndColumns(int n)
        {
            int blockSize = n * n;
            float[] src   = new float[blockSize];
            float[] dst   = new float[blockSize];
            nuint un      = (nuint)n;

            fixed (float* pSrc = src)
            fixed (float* pDst = dst)
            {
                DctTo toSrc = new DctTo(pSrc, un);

                for (nuint y = 0; y < un; y++)
                {
                    for (nuint x = 0; x < un; x++)
                    {
                        toSrc.Write(y * un + x, y, x);
                    }
                }

                DctTranspose.Run(un, un, new DctFrom(pSrc, un), new DctTo(pDst, un));
                DctFrom fromDst = new DctFrom(pDst, un);

                for (nuint y = 0; y < un; y++)
                {
                    for (nuint x = 0; x < un; x++)
                    {
                        fromDst.Read(y, x).ShouldBe(x * un + y, 1e-7f);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies 1D column DCT followed by 1D IDCT is an identity for N = 8, 16, 32,
        /// ported from <c>ColumnDctRoundtrip</c> in <c>dct_test.cc</c>.
        /// </summary>
        /// <param name="n">Transform dimension.</param>
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public void ColumnDctRoundtrip_ReconstructsIdentityMatrix(int n)
        {
            int blockSize   = n * n;
            float[] block   = new float[blockSize];
            float[] tmp     = new float[blockSize];
            float[] scratch = new float[3 * blockSize];
            nuint un        = (nuint)n;

            fixed (float* pBlock = block)
            fixed (float* pTmp = tmp)
            fixed (float* pScratch = scratch)
            {
                DctTo to     = new DctTo(pBlock, un);
                DctFrom from = new DctFrom(pBlock, un);

                for (nuint i = 0; i < un; i++)
                {
                    for (nuint j = 0; j < un; j++)
                    {
                        to.Write(i == j ? 1.0f : 0.0f, i, j);
                    }
                }

                DecTransforms.Dct1D(n, n, from, new DctTo(pTmp, un), pScratch);
                DecTransforms.Idct1D(n, n, new DctFrom(pTmp, un), to, pScratch);

                for (nuint i = 0; i < un; i++)
                {
                    for (nuint j = 0; j < un; j++)
                    {
                        from.Read(i, j).ShouldBe(i == j ? 1.0f : 0.0f, 1e-5f);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies 2D <see cref="DecTransforms.ComputeDCT"/> and <see cref="DecTransforms.ComputeIDCT"/> roundtrip for N = 8, 16, 32.
        /// </summary>
        /// <param name="n">Block dimension.</param>
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public void ComputeDctAndIdct_RoundtripsRandomBlock(int n)
        {
            int blockSize    = n * n;
            float[] original = new float[blockSize];
            float[] block    = new float[blockSize];
            Rng rng          = new Rng((ulong)n * 101UL);

            for (int i = 0; i < blockSize; i++)
            {
                original[i] = rng.UniformF(-1.0f, 1.0f);
                block[i]    = original[i];
            }

            DecTransforms.ComputeDCT(n, block);
            DecTransforms.ComputeIDCT(n, block);

            for (int i = 0; i < blockSize; i++)
            {
                block[i].ShouldBe(original[i], 1e-5f);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="WeightsSymmetric3"/>, <see cref="WeightsSymmetric5"/>,
    /// <see cref="WeightsSeparable5"/>, <see cref="Neighbors"/>, and <see cref="Convolve"/>,
    /// ported from <c>convolve_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed unsafe class ConvolveTests
    {
        /// <summary>
        /// Verifies <see cref="Neighbors.FirstL1"/> and <see cref="Neighbors.FirstL2"/> mirror shifting,
        /// ported from <c>TestNeighbors</c> in <c>convolve_test.cc</c>.
        /// </summary>
        [Test]
        public void Neighbors_FirstL1AndFirstL2_MatchExpectedMirroredVectors()
        {
            float[] v = new float[16];

            for (int i = 0; i < 16; i++)
            {
                v[i] = i;
            }

            float[] actualL1   = new float[16];
            float[] expectedL1 = { 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
            Neighbors.FirstL1(v, actualL1);
            actualL1.ShouldBe(expectedL1);

            float[] actualL2   = new float[16];
            float[] expectedL2 = { 1, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };
            Neighbors.FirstL2(v, actualL2);
            actualL2.ShouldBe(expectedL2);
        }

        /// <summary>
        /// Verifies that <see cref="Convolve.Symmetric5"/> and <see cref="Convolve.SlowSeparable5"/> produce identical outputs,
        /// ported from <c>VerifySymmetric5</c> and <c>VerifySeparable5</c> in <c>convolve_test.cc</c>.
        /// </summary>
        [Test]
        public void Symmetric5AndSeparable5_GiveEquivalentResults()
        {
            WeightsSymmetric5 sym5 = new WeightsSymmetric5(
                0.1740135f,
                0.1065369f,
                0.0150310f,
                0.0652254f,
                0.0012984f,
                0.0092025f);

            const float w0 = 0.41714928f;
            const float w1 = 0.25539268f;
            const float w2 = 0.03603267f;
            WeightsSeparable5 sep5 = new WeightsSeparable5(w0, w1, w2, w0, w1, w2);

            const nuint xsize = 20;
            const nuint ysize = 12;

            using ImagePlane<float> input       = ImagePlane<float>.Create(null, xsize, ysize).Value;
            using ImagePlane<float> outExpected = ImagePlane<float>.Create(null, xsize, ysize).Value;
            using ImagePlane<float> outActual   = ImagePlane<float>.Create(null, xsize, ysize).Value;

            Rng rng = new Rng(129);

            for (nuint y = 0; y < ysize; y++)
            {
                float* row = input.Row(y);

                for (nuint x = 0; x < xsize; x++)
                {
                    row[x] = rng.UniformF(0.0f, 1.0f);
                }
            }

            Rect rect = new Rect(0, 0, (int)xsize, (int)ysize);
            Convolve.SlowSeparable5(input, rect, sep5, null, outExpected, rect).IsOk.ShouldBeTrue();
            Convolve.Symmetric5(input, rect, sym5, null, outActual, rect).IsOk.ShouldBeTrue();

            for (nuint y = 0; y < ysize; y++)
            {
                float* expRow = outExpected.ConstRow(y);
                float* actRow = outActual.ConstRow(y);

                for (nuint x = 0; x < xsize; x++)
                {
                    actRow[x].ShouldBe(expRow[x], 1e-4f);
                }
            }

            WeightsSymmetric3 sym3 = new WeightsSymmetric3(0.5f, 0.075f, 0.05f);
            Convolve.SlowSymmetric3(input, rect, sym3, null, outActual).IsOk.ShouldBeTrue();
        }
    }
}
