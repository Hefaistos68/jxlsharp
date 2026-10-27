// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Decoder;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Unit tests for <see cref="ProgressiveSplitter"/>.
    /// </summary>
    [TestFixture]
    public class ProgressiveSplitterTests
    {
        /// <summary>
        /// Verifies default progressive splitter configuration and coefficient splitting.
        /// </summary>
        [Test]
        public void SplitACCoefficients_DefaultSinglePass_PreservesCoefficients()
        {
            ProgressiveSplitter splitter = new ProgressiveSplitter();

            splitter.GetNumPasses().ShouldBe((nuint)1);

            int[] coeffs = new int[64];
            int[] dst0   = new int[64];

            for (int i = 0; i < 64; i++)
            {
                coeffs[i] = (i + 1) * 4;
            }

            AcStrategy    acs     = AcStrategy.FromRawStrategy(AcStrategyType.DCT);
            Memory<int>[] outPtrs = { dst0 };

            splitter.SplitACCoefficients(coeffs, acs, 0, 0, outPtrs);

            dst0[0].ShouldBe(4);
            dst0[63].ShouldBe(256);
        }

        /// <summary>
        /// Verifies multi-pass progressive mode configuration and coefficient splitting.
        /// </summary>
        [Test]
        public void SetProgressiveMode_TwoPassProgressive_SplitsPasses()
        {
            ProgressiveSplitter splitter = new ProgressiveSplitter();
            PassDefinition[]    defs     =
            {
                new PassDefinition(2, 0, 4),
                new PassDefinition(8, 0, 1),
            };

            splitter.SetProgressiveMode(new ProgressiveMode(defs));
            splitter.GetNumPasses().ShouldBe((nuint)2);

            Passes passes = new Passes();

            splitter.InitPasses(passes).IsOk.ShouldBeTrue();
            passes.NumPasses.ShouldBe(2u);
            passes.Downsample[0].ShouldBe(4u);

            int[]         coeffs  = new int[64];
            int[]         dst0    = new int[64];
            int[]         dst1    = new int[64];
            AcStrategy    acs     = AcStrategy.FromRawStrategy(AcStrategyType.DCT);
            Memory<int>[] outPtrs = { dst0, dst1 };

            coeffs[0]  = 7;
            coeffs[1]  = 9;
            coeffs[63] = 11;

            splitter.SplitACCoefficients(coeffs, acs, 0, 0, outPtrs);

            dst0[0].ShouldBe(0);
            dst0[1].ShouldBe(9);
            dst1[1].ShouldBe(0);
            dst1[63].ShouldBe(11);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="CompressParams"/>.
    /// </summary>
    [TestFixture]
    public class CompressParamsTests
    {
        /// <summary>
        /// Verifies default compression parameter values and lossless detection.
        /// </summary>
        [Test]
        public void IsLossless_ZeroButteraugliAndModular_ReturnsTrue()
        {
            CompressParams cparams = new CompressParams
            {
                ModularMode         = true,
                ButteraugliDistance = 0.0f,
                ColorTransform      = ColorTransform.None,
            };

            cparams.IsLossless().ShouldBeTrue();

            Mock<JxlDebugImageCallback> mockCallback = new Mock<JxlDebugImageCallback>();
            cparams.DebugImage                       = mockCallback.Object;
            cparams.DebugImage.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ANSCoder"/>.
    /// </summary>
    [TestFixture]
    public class ANSCoderTests
    {
        /// <summary>
        /// Verifies ANS state transitions during PutSymbol and GetState.
        /// </summary>
        [Test]
        public void PutSymbolAndGetState_ValidSymbol_UpdatesState()
        {
            ANSCoder         coder = ANSCoder.Create();
            ANSEncSymbolInfo info  = new ANSEncSymbolInfo
            {
                Freq       = 512,
                IFreq      = ((1UL << ANSEncSymbolInfo.KReciprocalPrecision) + 511UL) / 512UL,
                ReverseMap = new ushort[AnsParams.AnsTabSize],
            };

            uint bits = coder.PutSymbol(info, out byte nbits);

            nbits.ShouldBe((byte)0);
            bits.ShouldBe(0u);

            uint finalState = coder.GetState();

            finalState.ShouldNotBe(0u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="SizeWriter"/>.
    /// </summary>
    [TestFixture]
    public class SizeWriterTests
    {
        /// <summary>
        /// Verifies bit accumulation in <see cref="SizeWriter"/>.
        /// </summary>
        [Test]
        public void Write_AccumulatesBitCount()
        {
            SizeWriter writer = new SizeWriter();

            writer.Write(5, 0x1F);
            writer.Write(11, 0x3FF);

            writer.Size.ShouldBe((nuint)16);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="HistogramParams"/>.
    /// </summary>
    [TestFixture]
    public class HistogramParamsTests
    {
        /// <summary>
        /// Verifies construction of <see cref="HistogramParams"/> from <see cref="CompressParams"/>.
        /// </summary>
        [Test]
        public void FromCompressParams_FalconSpeed_SelectsFastClustering()
        {
            CompressParams cparams = new CompressParams
            {
                SpeedTier           = SpeedTier.kFalcon,
                DecodingSpeedTier   = 2,
                ButteraugliDistance = 1.0f,
            };

            HistogramParams histParams = new HistogramParams(cparams.SpeedTier, 8);

            histParams.Clustering.ShouldBe(HistogramParams.ClusteringType.kFast);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PassesEncoderState"/>.
    /// </summary>
    [TestFixture]
    public class PassesEncoderStateTests
    {
        /// <summary>
        /// Verifies initialization of <see cref="PassesEncoderState"/>.
        /// </summary>
        [Test]
        public void Constructor_InitializesSharedStateAndPasses()
        {
            PassesEncoderState state = new PassesEncoderState();

            state.Shared.ShouldNotBeNull();
            state.Passes.ShouldNotBeNull();
            state.HistogramIdx.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncGaborish"/> ported from <c>lib/jxl/enc_gaborish_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncGaborishTests
    {
        /// <summary>
        /// Verifies that GaborishInverse sharpens an impulse signal in-place.
        /// </summary>
        [Test]
        public void GaborishInverse_DiracDelta_SharpensCenterImpulse()
        {
            StatusOr<Image3<float>> inStatus = Image3<float>.Create(null, 25, 25);

            inStatus.IsOk.ShouldBeTrue();

            using Image3<float> input = inStatus.Value!;

            for (nuint c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 25; y++)
                {
                    Span<float> row = input.PlaneRow(c, y);

                    for (int x = 0; x < 25; x++)
                    {
                        row[x] = 0.0f;
                    }
                }

                input.PlaneRow(c, 12)[12] = 10.0f;
            }

            Rect rect = new Rect(0, 0, 25, 25);

            EncGaborish.GaborishInverse(input, rect, new float[] { 1.0f, 1.0f, 1.0f }, null).IsOk.ShouldBeTrue();

            float centerAfterInverse = input.PlaneRow(0, 12)[12];

            centerAfterInverse.ShouldBeGreaterThan(10.0f);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncQuantWeights"/>.
    /// </summary>
    [TestFixture]
    public class EncQuantWeightsTests
    {
        /// <summary>
        /// Verifies encoding and decoding of default dequantization matrices.
        /// </summary>
        [Test]
        public void DequantMatricesEncode_DefaultOk_RoundtripsWithDecode()
        {
            DequantMatrices matrices = new DequantMatrices();
            BitWriter       writer   = new BitWriter(null);

            EncQuantWeights.DequantMatricesEncode(null, matrices, writer, LayerType.Quant, null, null).IsOk.ShouldBeTrue();
            EncQuantWeights.DequantMatricesEncodeDC(matrices, writer, LayerType.Quant, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader       reader  = new BitReader(writer.GetSpan());
            DequantMatrices decoded = new DequantMatrices();

            decoded.Decode(null, reader).IsOk.ShouldBeTrue();
            decoded.DecodeDC(reader).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PatchDictionaryEncoder"/>.
    /// </summary>
    [TestFixture]
    public class PatchDictionaryEncoderTests
    {
        /// <summary>
        /// Verifies setting positions and encoding a patch dictionary.
        /// </summary>
        [Test]
        public void SetPositions_EmptyDictionary_HasAnyReturnsFalse()
        {
            PatchDictionary dict = new PatchDictionary(null);

            PatchDictionaryEncoder.SetPositions(
                dict,
                new List<PatchPosition>(),
                new List<PatchReferencePosition>(),
                new List<PatchBlending>(),
                0);

            dict.HasAny().ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="BitWriter"/> ported from <c>lib/jxl/enc_bit_writer_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncBitWriterTests
    {
        /// <summary>
        /// Verifies random bit sequence writing and reading roundtrip.
        /// </summary>
        [Test]
        public void HitAllBranches_RandomBitWrites_MatchesBitReader()
        {
            Rng         rng    = new Rng(1);
            BitWriter   writer = new BitWriter(null);
            List<uint>  nbits  = new List<uint>(200);
            List<ulong> vals   = new List<ulong>(200);

            bool ok = writer.WithMaxBits(200 * 64, LayerType.Header, null, () =>
            {
                for (int i = 0; i < 200; i++)
                {
                    uint  n = (uint)rng.UniformU(1, 57);
                    ulong v = rng.UniformU(0, uint.MaxValue) & ((1UL << (int)n) - 1UL);

                    nbits.Add(n);
                    vals.Add(v);
                    writer.Write((nuint)n, v);
                }

                writer.ZeroPadToByte();

                return new Status(true);
            }).IsOk;

            ok.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());

            for (int i = 0; i < 200; i++)
            {
                ulong actual = reader.ReadBits((nuint)nbits[i]);
                actual.ShouldBe(vals[i]);
            }

            reader.JumpToByteBoundary().IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncHuffman"/>.
    /// </summary>
    [TestFixture]
    public class EncHuffmanTests
    {
        /// <summary>
        /// Verifies building and storing a Huffman tree and decoding it with <see cref="HuffmanDecodingData"/>.
        /// </summary>
        [Test]
        public void BuildAndStoreHuffmanTree_SkewedHistogram_DecodesCorrectly()
        {
            uint[]    histogram = new uint[] { 100, 50, 25, 12, 6, 3, 2, 1 };
            byte[]    depth     = new byte[8];
            ushort[]  bits      = new ushort[8];
            BitWriter writer    = new BitWriter(null);

            writer.WithMaxBits(1024, LayerType.Header, null, () =>
            {
                Status st = EncHuffman.BuildAndStoreHuffmanTree(histogram, 8, depth, bits, writer);
                writer.ZeroPadToByte();

                return st;
            }).IsOk.ShouldBeTrue();

            depth[0].ShouldBeLessThan(depth[7]);

            BitReader           reader = new BitReader(writer.GetSpan());
            HuffmanDecodingData dec    = new HuffmanDecodingData();

            dec.ReadFromBitStream(8, reader).ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Histogram"/> encoder extensions.
    /// </summary>
    [TestFixture]
    public class HistogramEncoderTests
    {
        /// <summary>
        /// Verifies flat histogram generation, conditioning, and entropy calculation.
        /// </summary>
        [Test]
        public void FlatAndShannonEntropy_ValidCounts_ComputesPositiveCost()
        {
            Histogram hist = Histogram.Flat(8, 64);

            hist.TotalCount.ShouldBe((nuint)64);
            hist.ShannonEntropy().ShouldBeGreaterThan(0.0f);

            StatusOr<float> ansCost = hist.ANSPopulationCost();
            ansCost.IsOk.ShouldBeTrue();
            ansCost.Value.ShouldBeGreaterThan(0.0f);

            hist.Condition();
            hist.Counts.Count.ShouldBeGreaterThanOrEqualTo(8);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncCluster"/>.
    /// </summary>
    [TestFixture]
    public class EncClusterTests
    {
        /// <summary>
        /// Verifies clustering of identical and distinct histograms.
        /// </summary>
        [Test]
        public void ClusterHistograms_DuplicateHistograms_MergesIntoOneCluster()
        {
            Histogram h1 = new Histogram();
            Histogram h2 = new Histogram();

            for (int i = 0; i < 4; i++)
            {
                h1.Add((nuint)i);
                h2.Add((nuint)i);
            }

            List<Histogram> inHists  = new List<Histogram> { h1, h2 };
            List<Histogram> outHists = new List<Histogram>();
            List<uint>      symbols  = new List<uint>();
            HistogramParams hparams  = new HistogramParams();

            EncCluster.ClusterHistograms(hparams, inHists, 4, outHists, symbols).IsOk.ShouldBeTrue();
            outHists.Count.ShouldBe(1);
            symbols.Count.ShouldBe(2);
            symbols[0].ShouldBe(0u);
            symbols[1].ShouldBe(0u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncContextMap"/>.
    /// </summary>
    [TestFixture]
    public class EncContextMapTests
    {
        /// <summary>
        /// Verifies context map encoding and decoding roundtrip.
        /// </summary>
        [Test]
        public void EncodeContextMap_MultiClusterMap_DecodesIdentically()
        {
            byte[]    contextMap = new byte[] { 0, 0, 1, 2, 2, 2, 1, 0, 3, 3 };
            BitWriter writer     = new BitWriter(null);

            EncContextMap.EncodeContextMap(contextMap, 4, writer, LayerType.Header, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader  = new BitReader(writer.GetSpan());
            byte[]    decoded = new byte[contextMap.Length];

            DecContextMap.DecodeContextMap(null, decoded, out nuint numHistos, reader).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            numHistos.ShouldBe((nuint)4);
            decoded.ShouldBe(contextMap);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncEntropyCoder"/> ported from <c>lib/jxl/entropy_coder_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncEntropyCoderTests
    {
        /// <summary>
        /// Verifies hybrid uint pack/unpack signed roundtrip.
        /// </summary>
        [Test]
        public void PackAndUnpackSigned_AllSmallIntegers_Roundtrips()
        {
            for (int i = -1000; i <= 1000; i++)
            {
                uint packed   = PackSigned.Pack(i);
                int  unpacked = PackSigned.Unpack(packed);

                unpacked.ShouldBe(i);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncLz77"/>.
    /// </summary>
    [TestFixture]
    public class EncLz77Tests
    {
        /// <summary>
        /// Verifies RLE and LZ77 compression of repeated token sequences.
        /// </summary>
        [Test]
        public void ApplyLZ77_RepeatedTokens_ReducesTokenCount()
        {
            List<Token> stream = new List<Token>();

            for (int r = 0; r < 8; r++)
            {
                uint sym = (uint)(r + 1);

                for (int i = 0; i < 32; i++)
                {
                    stream.Add(new Token(0, sym));
                }
            }

            List<List<Token>> tokens  = new List<List<Token>> { stream };
            LZ77Params        lz77    = new LZ77Params { MinSymbol = 224, NonserializedDistanceContext = 1 };
            HistogramParams   hparams = new HistogramParams
            {
                Lz77Method = HistogramParams.LZ77Method.kRLE,
            };

            List<List<Token>> tokensLz77 = EncLz77.ApplyLZ77(hparams, 1, tokens, lz77);

            tokensLz77.Count.ShouldBe(1);
            tokensLz77[0].Count.ShouldBeLessThan(stream.Count);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EntropyEncodingData"/>.
    /// </summary>
    [TestFixture]
    public class EntropyEncodingDataTests
    {
        /// <summary>
        /// Verifies building ANS encoding data for a histogram.
        /// </summary>
        [Test]
        public void BuildAndStoreANSEncodingData_ValidHistogram_CreatesSymbolTable()
        {
            EntropyEncodingData codes = new EntropyEncodingData
            {
                LogAlphaSize = 7,
            };

            ANSEncSymbolInfo[] infoTable = new ANSEncSymbolInfo[4];

            for (int i = 0; i < 4; i++)
            {
                infoTable[i] = new ANSEncSymbolInfo();
            }

            codes.EncodingInfo.Add(infoTable);

            Histogram hist = Histogram.Flat(4, AnsParams.AnsTabSize);
            BitWriter bw   = new BitWriter(null);

            bool ok = bw.WithMaxBits(1024, LayerType.Header, null, () =>
            {
                StatusOr<nuint> cost = codes.BuildAndStoreANSEncodingData(
                    null,
                    HistogramParams.ANSHistogramStrategy.kPrecise,
                    hist,
                    bw);

                return cost.Status;
            }).IsOk;

            ok.ShouldBeTrue();
            codes.EncodingInfo.Count.ShouldBe(1);
            codes.EncodingInfo[0].Length.ShouldBeGreaterThanOrEqualTo(4);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncAns"/> ported from <c>lib/jxl/ans_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncAnsTests
    {
        /// <summary>
        /// Verifies full ANS/Prefix histogram and token encoding and decoding roundtrip.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void RoundtripTokens_PrefixAndAns_DecodesExactValues(bool usePrefixCode)
        {
            Rng         rng      = new Rng(12345);
            List<Token> stream   = new List<Token>();
            List<uint>  expected = new List<uint>();

            for (int i = 0; i < 500; i++)
            {
                uint context = (uint)rng.UniformU(0, 2);
                uint value   = (i % 7 == 0) ? (uint)rng.UniformU(0, 1000) : (uint)rng.UniformU(0, 16);

                stream.Add(new Token(context, value));
                expected.Add(value);
            }

            List<List<Token>>   tokens  = new List<List<Token>> { stream };
            EntropyEncodingData codes   = new EntropyEncodingData { UsePrefixCode = usePrefixCode };
            BitWriter           writer  = new BitWriter(null);
            HistogramParams     hparams = new HistogramParams
            {
                ForceHuffman = usePrefixCode,
                Lz77Method   = HistogramParams.LZ77Method.kNone,
            };

            StatusOr<nuint> buildStatus = EncAns.BuildAndEncodeHistograms(
                null,
                hparams,
                2,
                tokens,
                codes,
                writer);

            buildStatus.IsOk.ShouldBeTrue();
            EncAns.WriteTokens(tokens[0], codes, 0, writer, LayerType.Ac, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader     = new BitReader(writer.GetSpan());
            ANSCode   decodedAns = new ANSCode();

            DecAns.DecodeHistograms(null, reader, 2, decodedAns, out byte[] contextMap).IsOk.ShouldBeTrue();

            StatusOr<ANSSymbolReader> symReaderOr = ANSSymbolReader.Create(decodedAns, reader);
            symReaderOr.IsOk.ShouldBeTrue();

            ANSSymbolReader symReader = symReaderOr.Value!;

            for (int i = 0; i < stream.Count; i++)
            {
                uint sym = symReader.ReadHybridUint(stream[i].Context, reader, contextMap);
                sym.ShouldBe(expected[i]);
            }

            symReader.CheckANSFinalState().ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();
        }
    }
}
