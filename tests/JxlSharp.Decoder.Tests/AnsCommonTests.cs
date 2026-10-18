// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AliasTable"/>, <see cref="HuffmanTable"/>, <see cref="HuffmanDecodingData"/>,
    /// <see cref="DecAns"/>, <see cref="DecContextMap"/>, and <see cref="EntropyCoder"/>.
    /// Ported from <c>lib/jxl/ans_common_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class AnsCommonTests
    {
        /// <summary>
        /// Verifies alias table construction and lookup across standard distributions.
        /// Ported from <c>ANSCommonTest.AliasDistributionSmoke</c>.
        /// </summary>
        [Test]
        public void AliasDistributionSmoke_ShouldPopulateExactOffsets()
        {
            int half = (int)(AnsParams.AnsTabSize / 2u);
            int full = (int)AnsParams.AnsTabSize;

            VerifyAliasDistribution(new[] { half, half }, AnsParams.AnsLogTabSize);
            VerifyAliasDistribution(new[] { full }, AnsParams.AnsLogTabSize);
            VerifyAliasDistribution(new[] { 0, 0, 0, full, 0 }, AnsParams.AnsLogTabSize);
        }

        /// <summary>
        /// Verifies <see cref="AliasTable.CreateFlatHistogram"/> sums to the target range.
        /// </summary>
        [Test]
        public void CreateFlatHistogram_ShouldSumToTotalCount()
        {
            int[] flat = AliasTable.CreateFlatHistogram(7, (int)AnsParams.AnsTabSize);

            flat.Length.ShouldBe(7);

            int sum = 0;

            foreach (int c in flat)
            {
                sum += c;
            }

            sum.ShouldBe((int)AnsParams.AnsTabSize);
        }

        /// <summary>
        /// Verifies round-trip encoding and decoding of variable-length 8-bit and 16-bit integers
        /// and <see cref="HybridUintConfig"/> in <see cref="DecAns"/>.
        /// </summary>
        [Test]
        public void VarLenUintAndHybridUintConfig_ShouldRoundTrip()
        {
            BitWriter writer        = new BitWriter();
            HybridUintConfig cfgIn1 = new HybridUintConfig(4, 2, 0);
            HybridUintConfig cfgIn2 = new HybridUintConfig(8, 0, 0);

            Status writeStatus = writer.WithMaxBits(
                512,
                LayerType.Header,
                null,
                () =>
                {
                    DecAns.StoreVarLenUint8(0, writer);
                    DecAns.StoreVarLenUint8(1, writer);
                    DecAns.StoreVarLenUint8(255, writer);
                    DecAns.StoreVarLenUint16(0, writer);
                    DecAns.StoreVarLenUint16(1024, writer);
                    DecAns.StoreVarLenUint16(65535, writer);
                    DecAns.EncodeUintConfig(in cfgIn1, writer, 15);
                    DecAns.EncodeUintConfig(in cfgIn2, writer, 15);
                    writer.ZeroPadToByte();

                    return true;
                });

            writeStatus.IsOk.ShouldBeTrue();

            using BitReader reader = new BitReader(writer.GetSpan());
            DecAns.DecodeVarLenUint8(reader).ShouldBe(0);
            DecAns.DecodeVarLenUint8(reader).ShouldBe(1);
            DecAns.DecodeVarLenUint8(reader).ShouldBe(255);
            DecAns.DecodeVarLenUint16(reader).ShouldBe(0);
            DecAns.DecodeVarLenUint16(reader).ShouldBe(1024);
            DecAns.DecodeVarLenUint16(reader).ShouldBe(65535);

            DecAns.DecodeUintConfig(15, out HybridUintConfig cfgOut1, reader).IsOk.ShouldBeTrue();
            cfgOut1.SplitExponent.ShouldBe(4u);
            cfgOut1.MsbInToken.ShouldBe(2u);
            cfgOut1.LsbInToken.ShouldBe(0u);

            DecAns.DecodeUintConfig(15, out HybridUintConfig cfgOut2, reader).IsOk.ShouldBeTrue();
            cfgOut2.SplitExponent.ShouldBe(8u);
            cfgOut2.MsbInToken.ShouldBe(0u);
            cfgOut2.LsbInToken.ShouldBe(0u);

            reader.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies Huffman tree construction, serialization, and decoding via <see cref="HuffmanTable"/>
        /// and <see cref="HuffmanDecodingData"/>.
        /// </summary>
        [Test]
        public void HuffmanTreeBuildAndDecode_ShouldRoundTripSymbols()
        {
            uint[] histogram        = { 10, 1, 25, 4, 15, 2 };
            int alphabetSize        = histogram.Length;
            byte[] depths           = new byte[alphabetSize];
            ushort[] codewords      = new ushort[alphabetSize];
            ushort[] symbolSequence = { 2, 0, 4, 2, 3, 5, 1, 2, 0, 4 };
            BitWriter writer        = new BitWriter();

            Status writeStatus = writer.WithMaxBits(
                1024,
                LayerType.Header,
                null,
                () =>
                {
                    if (!HuffmanTable.BuildAndStoreHuffmanTree(histogram, alphabetSize, depths, codewords, writer))
                    {
                        return false;
                    }

                    foreach (ushort sym in symbolSequence)
                    {
                        writer.Write(depths[sym], codewords[sym]);
                    }

                    writer.ZeroPadToByte();

                    return true;
                });

            writeStatus.IsOk.ShouldBeTrue();

            using BitReader reader       = new BitReader(writer.GetSpan());
            HuffmanDecodingData huffData = new HuffmanDecodingData();
            huffData.ReadFromBitStream((nuint)alphabetSize, reader).ShouldBeTrue();

            foreach (ushort expected in symbolSequence)
            {
                reader.Refill();
                ushort actual = huffData.ReadSymbol(reader);
                actual.ShouldBe(expected);
            }

            reader.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="EntropyCoder.PredictFromTopAndLeft"/>, <see cref="BlockCtxMap"/>, and <see cref="AcContext"/>.
        /// </summary>
        [Test]
        public void EntropyCoderContextAndPrediction_ShouldComputeExpectedValues()
        {
            int[] rowTop = { 10, 20 };
            int[] row    = { 10, 0 };
            int pred     = EntropyCoder.PredictFromTopAndLeft(rowTop, row, 1, 15);
            pred.ShouldBe(15);

            BlockCtxMap blockCtxMap = new BlockCtxMap();
            uint ctx                = blockCtxMap.NonZeroContext(12, 0);
            ctx.ShouldBeLessThan((uint)(blockCtxMap.NumCtxs * AcContext.KNonZeroBuckets));

            nuint modularCtx = AcContext.ZeroDensityContext(5, 2, 1, 0, 0);
            modularCtx.ShouldBeLessThan((nuint)AcContext.KZeroDensityContextCount);
        }

        private static void VerifyAliasDistribution(int[] distribution, uint logRange)
        {
            const uint logAlphaSize  = 8;
            AliasTable.Entry[] table = new AliasTable.Entry[1 << (int)logAlphaSize];
            Status ok                = AliasTable.InitAliasTable(distribution, logRange, logAlphaSize, table);
            ok.IsOk.ShouldBeTrue();

            uint range            = 1u << (int)logRange;
            List<uint>[] offsets  = new List<uint>[distribution.Length];

            for (int i = 0; i < distribution.Length; i++)
            {
                offsets[i] = new List<uint>();
            }

            uint logEntrySize    = AnsParams.AnsLogTabSize - logAlphaSize;
            uint entrySizeMinus1 = (1u << (int)logEntrySize) - 1u;

            for (uint i = 0; i < range; i++)
            {
                AliasTable.Symbol s = AliasTable.Lookup(table, i, logEntrySize, entrySizeMinus1);
                offsets[s.Value].Add((uint)s.Offset);
            }

            for (int i = 0; i < distribution.Length; i++)
            {
                offsets[i].Count.ShouldBe(distribution[i]);
                offsets[i].Sort();

                for (int j = 0; j < offsets[i].Count; j++)
                {
                    offsets[i][j].ShouldBe((uint)j);
                }
            }
        }
    }
}
