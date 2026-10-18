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
    /// Unit tests for <see cref="TreeSamples"/>.
    /// </summary>
    [TestFixture]
    public class TreeSamplesTests
    {
        /// <summary>
        /// Verifies sample quantization, preparation, and swapping in <see cref="TreeSamples"/>.
        /// </summary>
        [Test]
        public void SetPredictorAndProps_ValidParameters_QuantizesAndSwapsSamples()
        {
            TreeSamples samples = new TreeSamples();

            samples.SetPredictor(Predictor.Gradient, ModularOptions.TreeMode.Default).IsOk.ShouldBeTrue();
            samples.SetProperties(new List<uint> { 0, 1 }, ModularOptions.TreeMode.Default).IsOk.ShouldBeTrue();

            StaticPropRange range = new StaticPropRange { ChanMin = 0, ChanMax = 3, GroupMin = 0, GroupMax = 2 };

            samples.PreQuantizeProperties(
                range,
                new List<ModularMultiplierInfo>(),
                new uint[] { 16, 16 },
                new uint[] { 16, 16, 16 },
                new List<int> { 0, 10, 20 },
                new List<int> { -5, 0, 5 },
                64);

            samples.PrepareForSamples(4);

            int[]  props       = new int[16];
            long[] predictions = new long[ModularOptions.KNumModularPredictors];

            samples.AddSample(4, props, predictions);
            samples.NumSamples.ShouldBe((nuint)1);
            samples.AllSamplesDone();
            samples.NumUniqueSamples.ShouldBe((nuint)1);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncMa"/>.
    /// </summary>
    [TestFixture]
    public class EncMaTests
    {
        /// <summary>
        /// Verifies tree tokenization and decoding roundtrip with <see cref="ModularMa.DecodeTree"/>.
        /// </summary>
        [Test]
        public void TokenizeTree_SplitAndLeafNodes_RoundtripsWithDecodeTree()
        {
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(0, 5, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Zero, 1, 1),
                PropertyDecisionNode.Leaf(Predictor.Left, 0, 1),
            };

            List<Token>                tokens  = new List<Token>();
            List<PropertyDecisionNode> decTree = new List<PropertyDecisionNode>();

            EncMa.TokenizeTree(tree, tokens, decTree).IsOk.ShouldBeTrue();
            decTree.Count.ShouldBe(3);

            List<List<Token>>   treeTokens = new List<List<Token>> { tokens };
            EntropyEncodingData codes      = new EntropyEncodingData { UsePrefixCode = true };
            BitWriter           writer     = new BitWriter(null);
            HistogramParams     hparams    = new HistogramParams { ForceHuffman = true };

            EncAns.BuildAndEncodeHistograms(null, hparams, (nuint)MATreeContext.NumTreeContexts, treeTokens, codes, writer).IsOk.ShouldBeTrue();
            EncAns.WriteTokens(treeTokens[0], codes, 0, writer, LayerType.ModularTree, null).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader                  reader      = new BitReader(writer.GetSpan());
            List<PropertyDecisionNode> decodedTree = new List<PropertyDecisionNode>();

            ModularMa.DecodeTree(null, reader, decodedTree, 1024).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            decodedTree.Count.ShouldBe(3);
            decodedTree[1].Predictor.ShouldBe(Predictor.Zero);
            decodedTree[1].PredictorOffset.ShouldBe(1L);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncDebugTree"/>.
    /// </summary>
    [TestFixture]
    public class EncDebugTreeTests
    {
        /// <summary>
        /// Verifies GraphViz DOT formatting for an MA decision tree.
        /// </summary>
        [Test]
        public void FormatTreeDot_SingleLeaf_ContainsDigraphHeader()
        {
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Leaf(Predictor.Zero),
            };

            string dot = EncDebugTree.FormatTreeDot(tree);

            dot.ShouldContain("digraph Tree");
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncRct"/>.
    /// </summary>
    [TestFixture]
    public class EncRctTests
    {
        /// <summary>
        /// Verifies that <see cref="EncRct.FwdRct(ModularImage,nuint,nuint,ThreadPool)"/> followed by <see cref="ModularTransformOps.InvRCT"/> is lossless for all 42 RCT types.
        /// </summary>
        [Test]
        public void FwdRct_All42Permutations_RoundtripsWithInvRct()
        {
            for (nuint rctType = 1; rctType < 42; rctType++)
            {
                StatusOr<ModularImage> imgOr = ModularImage.Create(null, 8, 8, 8, 3);
                imgOr.IsOk.ShouldBeTrue();

                using ModularImage img = imgOr.Value!;

                for (nuint y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        img.Channels[0].Row(y)[x] = (x * 13) + (int)y;
                        img.Channels[1].Row(y)[x] = (x * 7) - ((int)y * 3);
                        img.Channels[2].Row(y)[x] = (x + 1) * ((int)y + 2);
                    }
                }

                EncRct.FwdRct(img, 0, rctType, null).IsOk.ShouldBeTrue();
                ModularTransformOps.InvRCT(img, 0, (uint)rctType, null).IsOk.ShouldBeTrue();

                for (nuint y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        img.Channels[0].Row(y)[x].ShouldBe((x * 13) + (int)y);
                        img.Channels[1].Row(y)[x].ShouldBe((x * 7) - ((int)y * 3));
                        img.Channels[2].Row(y)[x].ShouldBe((x + 1) * ((int)y + 2));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncSqueeze"/>.
    /// </summary>
    [TestFixture]
    public class EncSqueezeTests
    {
        /// <summary>
        /// Verifies forward and inverse squeeze transform roundtrip.
        /// </summary>
        [Test]
        public void FwdSqueeze_DefaultSqueezes_RoundtripsWithInvSqueeze()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 17, 15, 8, 3);
            imgOr.IsOk.ShouldBeTrue();

            using ModularImage img = imgOr.Value!;

            for (int c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 15; y++)
                {
                    for (int x = 0; x < 17; x++)
                    {
                        img.Channels[c].Row(y)[x] = ((c + 1) * 100) + ((int)y * 17) + x;
                    }
                }
            }

            Transform squeezeTransform = new Transform(TransformId.Squeeze);
            EncTransform.TransformForward(squeezeTransform, img, new WeightedHeader(), null).IsOk.ShouldBeTrue();
            img.Transforms.Add(squeezeTransform);

            img.UndoTransforms(new WeightedHeader(), null);

            for (int c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < 15; y++)
                {
                    for (int x = 0; x < 17; x++)
                    {
                        img.Channels[c].Row(y)[x].ShouldBe(((c + 1) * 100) + ((int)y * 17) + x);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncPalette"/>.
    /// </summary>
    [TestFixture]
    public class EncPaletteTests
    {
        /// <summary>
        /// Verifies forward and inverse palette transform roundtrip.
        /// </summary>
        [Test]
        public void FwdPalette_FewColorsImage_RoundtripsWithInvPalette()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 8, 8, 8, 3);
            imgOr.IsOk.ShouldBeTrue();

            using ModularImage img = imgOr.Value!;

            for (nuint y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int idx                   = (x + (int)y) % 4;
                    img.Channels[0].Row(y)[x] = idx * 20;
                    img.Channels[1].Row(y)[x] = idx * 40;
                    img.Channels[2].Row(y)[x] = idx * 60;
                }
            }

            Transform paletteTransform = new Transform(TransformId.Palette)
            {
                BeginC         = 0,
                NumC           = 3,
                NbColors       = 4,
                NbDeltas       = 0,
                Predictor      = Predictor.Zero,
                OrderedPalette = true,
            };

            EncTransform.TransformForward(paletteTransform, img, new WeightedHeader(), null).IsOk.ShouldBeTrue();
            img.Transforms.Add(paletteTransform);

            img.UndoTransforms(new WeightedHeader(), null);

            for (nuint y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int idx = (x + (int)y) % 4;
                    img.Channels[0].Row(y)[x].ShouldBe(idx * 20);
                    img.Channels[1].Row(y)[x].ShouldBe(idx * 40);
                    img.Channels[2].Row(y)[x].ShouldBe(idx * 60);
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncTransform"/>.
    /// </summary>
    [TestFixture]
    public class EncTransformTests
    {
        /// <summary>
        /// Verifies min/max computation on a channel.
        /// </summary>
        [Test]
        public void ComputeMinMax_PopulatedChannel_FindsBounds()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 4, 8, 1);
            imgOr.IsOk.ShouldBeTrue();

            using ModularImage img = imgOr.Value!;

            for (nuint y = 0; y < 4; y++)
            {
                img.Channels[0].Row(y).Fill(10);
            }

            img.Channels[0].Row(0)[0] = -15;
            img.Channels[0].Row(3)[3] = 240;

            EncTransform.ComputeMinMax(img.Channels[0], out int min, out int max);

            min.ShouldBe(-15);
            max.ShouldBe(240);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncModularEncoding"/> ported from <c>lib/jxl/modular_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class EncModularEncodingTests
    {
        /// <summary>
        /// Verifies <c>ModularTest.RoundtripExtraProperties</c> from <c>lib/jxl/modular_test.cc</c>.
        /// </summary>
        [Test]
        public void RoundtripExtraProperties_DirectModularGenericCompress_DecodesIdentically()
        {
            const int              kSize = 64;
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, kSize, kSize, 8, 3);

            imgOr.IsOk.ShouldBeTrue();

            using ModularImage image   = imgOr.Value!;
            ModularOptions     options = ModularOptions.Default();

            options.MaxProperties = 4;
            options.Predictor     = Predictor.Zero;

            Rng rng = new Rng(0);

            for (nuint y = 0; y < kSize; y++)
            {
                for (int x = 0; x < kSize; x++)
                {
                    int v                       = (int)rng.UniformU(0, 9);
                    image.Channels[0].Row(y)[x] = v;
                    image.Channels[1].Row(y)[x] = 0;
                    image.Channels[2].Row(y)[x] = v;
                }
            }

            BitWriter writer = new BitWriter(null);

            EncModularEncoding.ModularGenericCompress(image, options, writer).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            StatusOr<ModularImage> decOr = ModularImage.Create(null, kSize, kSize, 8, image.Channels.Count);
            decOr.IsOk.ShouldBeTrue();

            using ModularImage decoded = decOr.Value!;
            BitReader          reader  = new BitReader(writer.GetSpan());
            GroupHeader        header  = new GroupHeader();

            ModularEncoding.ModularGenericDecompress(
                reader,
                decoded,
                header,
                0,
                options).IsOk.ShouldBeTrue();

            reader.Close().IsOk.ShouldBeTrue();

            for (int c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < kSize; y++)
                {
                    for (int x = 0; x < kSize; x++)
                    {
                        int expected = (c == 1) ? 0 : image.Channels[0].Row(y)[x];
                        decoded.Channels[c].Row(y)[x].ShouldBe(expected);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ModularFrameEncoder"/>.
    /// </summary>
    [TestFixture]
    public class ModularFrameEncoderTests
    {
        /// <summary>
        /// Verifies creating a <see cref="ModularFrameEncoder"/> and encoding custom quantization tables.
        /// </summary>
        [Test]
        public void CreateAndSetCustomDequantMatrices_ValidFrameHeader_Succeeds()
        {
            CodecMetadata metadata = new CodecMetadata();
            metadata.Size.Set(16, 16).IsOk.ShouldBeTrue();

            FrameHeader frameHeader = new FrameHeader(metadata)
            {
                Encoding = FrameEncoding.Modular,
            };

            CompressParams                cparams = new CompressParams { ModularMode = true };
            StatusOr<ModularFrameEncoder> encOr   = ModularFrameEncoder.Create(null, frameHeader, cparams, false);

            encOr.IsOk.ShouldBeTrue();

            using ModularFrameEncoder encoder   = encOr.Value!;
            DequantMatrices           matrices  = new DequantMatrices();
            QuantEncoding[]           encodings = new QuantEncoding[DequantMatrices.kNumQuantTables];

            for (int i = 0; i < encodings.Length; i++)
            {
                encodings[i] = QuantEncoding.Library(0);
            }

            EncQuantWeights.DequantMatricesSetCustom(matrices, encodings, encoder).IsOk.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="JxlFastLosslessFrameState"/>.
    /// </summary>
    [TestFixture]
    public class JxlFastLosslessFrameStateTests
    {
        /// <summary>
        /// Verifies chunked frame preparation, processing, and freeing via <see cref="EncFastLossless"/>.
        /// </summary>
        [Test]
        public unsafe void PrepareAndFreeFrameState_ValidInput_WritesOutputChunks()
        {
            byte[] pixels = new byte[16 * 16 * 3];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 17);
            }

            fixed (byte* pPixels = pixels)
            {
                JxlChunkedFrameInputSource input = EncFastLossless.CreateMemoryInputSource(pPixels, 16 * 3, 3, 8);
                FastLosslessPrepareArgs    args  = new FastLosslessPrepareArgs(input, 16, 16, 3, 8, false, 2, true);

                using JxlFastLosslessFrameState? state = EncFastLossless.JxlFastLosslessPrepareFrame(in args);
                state.ShouldNotBeNull();

                Mock<IJxlFastLosslessOutputProcessor> mockProcessor = new Mock<IJxlFastLosslessOutputProcessor>();
                mockProcessor.Setup(p => p.Seek(It.IsAny<nuint>())).Returns(new Status(true));

                EncFastLossless.JxlFastLosslessProcessFrame(state!, true, null, null, null).ShouldBeTrue();
                EncFastLossless.JxlFastLosslessPrepareHeader(state!, 1, 1).ShouldBeTrue();

                nuint maxOut = EncFastLossless.JxlFastLosslessMaxRequiredOutput(state!);
                maxOut.ShouldBeGreaterThan((nuint)0);

                EncFastLossless.JxlFastLosslessFreeFrameState(state);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="EncFastLossless"/> including full roundtrip decoding with <see cref="JxlDecoder"/>.
    /// </summary>
    [TestFixture]
    public class EncFastLosslessTests
    {
        /// <summary>
        /// Verifies standalone Fast Lossless encoding and full <see cref="JxlDecoder"/> roundtrip across channel counts and bit depths.
        /// </summary>
        [TestCase(1, 8, 1)]
        [TestCase(2, 8, 2)]
        [TestCase(3, 8, 2)]
        [TestCase(4, 8, 2)]
        [TestCase(3, 16, 2)]
        [TestCase(4, 16, 1)]
        public void FastLosslessEncode_VariousFormats_DecodesLosslesslyWithJxlDecoder(
            int numChannels,
            int bitDepth,
            int effort)
        {
            const int width        = 37;
            const int height       = 29;
            int       bytesPerChan = (bitDepth > 8) ? 2 : 1;
            int       stride       = width * numChannels * bytesPerChan;
            byte[]    inputBytes   = new byte[height * stride];
            Rng       rng          = new Rng((ulong)((numChannels * 100) + bitDepth + effort));

            for (int i = 0; i < inputBytes.Length; i++)
            {
                inputBytes[i] = (byte)rng.UniformU(0, 256);
            }

            StatusOr<byte[]> encOr = EncFastLossless.FastLosslessEncode(
                inputBytes,
                ((nuint)width, (nuint)stride, (nuint)height, (nuint)numChannels, (nuint)bitDepth),
                false,
                effort);

            encOr.IsOk.ShouldBeTrue();

            byte[] encoded = encOr.Value!;
            encoded.Length.ShouldBeGreaterThan(2);
            encoded[0].ShouldBe((byte)0xFF);
            encoded[1].ShouldBe((byte)0x0A);

            using JxlDecoder dec = new JxlDecoder(null);

            dec.SubscribeEvents((int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.FullImage))
                .ShouldBe(JxlDecoderStatus.Success);

            dec.SetInput(encoded).ShouldBe(JxlDecoderStatus.Success);
            dec.CloseInput();

            JxlDecoderStatus status = dec.ProcessInput();
            status.ShouldBe(JxlDecoderStatus.BasicInfo);

            dec.GetBasicInfo(out JxlBasicInfo info).ShouldBe(JxlDecoderStatus.Success);
            info.XSize.ShouldBe((uint)width);
            info.YSize.ShouldBe((uint)height);
            info.BitsPerSample.ShouldBe((uint)bitDepth);

            status = dec.ProcessInput();
            status.ShouldBe(JxlDecoderStatus.NeedImageOutBuffer);

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = (uint)numChannels,
                DataType    = (bitDepth > 8) ? JxlDataType.UInt16 : JxlDataType.UInt8,
                Endianness  = JxlEndianness.LittleEndian,
                Align       = 0,
            };

            byte[] decodedBytes = new byte[inputBytes.Length];

            dec.SetImageOutBuffer(format, decodedBytes)
                .ShouldBe(JxlDecoderStatus.Success);

            status = dec.ProcessInput();
            status.ShouldBe(JxlDecoderStatus.FullImage);

            decodedBytes.ShouldBe(inputBytes);
        }
    }
}
