// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Modular channel compression, MA tree learning, predefined trees, and cost estimation.
    /// Ported from <c>lib/jxl/modular/encoding/enc_encoding.h</c>, <c>enc_encoding.cc</c>, and <c>lib/jxl/enc_modular_simd.cc</c>.
    /// </summary>
    public static class EncModularEncoding
    {
        private static readonly int[] FixedDcCutoffs =
        {
            -500, -392, -255, -191, -127, -95, -63, -47, -31, -23, -15,
            -11,  -7,   -4,   -3,   -1,   0,   1,   3,   5,   7,   11,
            15,   23,   31,   47,   63,   95,  127, 191, 255, 392, 500,
        };

        private static readonly uint[] EstimateCostCutoffs =
        {
            0, 1, 3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500,
        };

        private struct NodeRangeInfo
        {
            public int Begin;
            public int End;
            public int Pos;
        }

        /// <summary>
        /// Constructs a predefined MA decision tree of <paramref name="treeKind"/>.
        /// Ported from <c>jxl::PredefinedTree</c> in <c>lib/jxl/modular/encoding/enc_encoding.cc</c>.
        /// </summary>
        /// <param name="treeKind">Predefined tree kind.</param>
        /// <param name="totalPixels">Total pixel count in the stream(s).</param>
        /// <param name="bitdepth">Sample bit depth.</param>
        /// <param name="prevprop">Number of previous-channel properties enabled.</param>
        /// <returns>Predefined MA decision tree.</returns>
        public static List<PropertyDecisionNode> PredefinedTree(
            ModularOptions.TreeKind treeKind,
            nuint totalPixels,
            int bitdepth,
            int prevprop)
        {
            switch (treeKind)
            {
                case ModularOptions.TreeKind.JpegTranscodeACMeta:
                case ModularOptions.TreeKind.TrivialTreeNoPredictor:
                    return new List<PropertyDecisionNode> { PropertyDecisionNode.Leaf(Predictor.Zero) };

                case ModularOptions.TreeKind.FalconACMeta:
                    return new List<PropertyDecisionNode> { PropertyDecisionNode.Leaf(Predictor.Left) };

                case ModularOptions.TreeKind.ACMeta:
                    return BuildPredefinedAcMetaTree(totalPixels);

                case ModularOptions.TreeKind.WPFixedDC:
                    return MakeFixedTree(
                        ModularPredictors.KWPProp,
                        FixedDcCutoffs,
                        Predictor.Weighted,
                        totalPixels,
                        bitdepth);

                case ModularOptions.TreeKind.GradientFixedDC:
                    int prop = prevprop > 0
                        ? ModularPredictors.KNumNonrefProperties + 2
                        : ModularPredictors.KGradientProp;

                    return MakeFixedTree(prop, FixedDcCutoffs, Predictor.Gradient, totalPixels, bitdepth);

                default:
                    return new List<PropertyDecisionNode>();
            }
        }

        /// <summary>
        /// Learns an MA decision tree over <paramref name="images"/> in range <c>[<paramref name="start"/>, <paramref name="stop"/>)</c>.
        /// Ported from <c>jxl::LearnTree</c> in <c>lib/jxl/modular/encoding/enc_encoding.cc</c>.
        /// </summary>
        public static StatusOr<List<PropertyDecisionNode>> LearnTree(
            IReadOnlyList<ModularImage> images,
            IReadOnlyList<ModularOptions> options,
            uint start,
            uint stop,
            IReadOnlyList<ModularMultiplierInfo>? multiplierInfo = null)
        {
            IReadOnlyList<ModularMultiplierInfo> mulInfo = multiplierInfo ?? Array.Empty<ModularMultiplierInfo>();
            TreeSamples treeSamples                      = new TreeSamples();
            ModularOptions startOpts                     = options[(int)start];

            Status predOk = treeSamples.SetPredictor(startOpts.Predictor, startOpts.WpTreeMode);

            if (!predOk.IsOk)
            {
                return predOk;
            }

            Status propOk = treeSamples.SetProperties(startOpts.SplittingHeuristicsProperties, startOpts.WpTreeMode);

            if (!propOk.IsOk)
            {
                return propOk;
            }

            PreQuantizeAndGatherSamples(
                images,
                options,
                start,
                stop,
                mulInfo,
                treeSamples,
                out StaticPropRange range,
                out nuint totalPixels,
                out Status gatherStatus);

            if (!gatherStatus.IsOk)
            {
                return gatherStatus;
            }

            return FinalizeLearnedTree(treeSamples, totalPixels, startOpts, mulInfo, range);
        }

        /// <summary>
        /// Compresses <paramref name="image"/> using a given <paramref name="tree"/> into <paramref name="tokens"/>.
        /// Ported from <c>jxl::ModularCompress</c> in <c>lib/jxl/modular/encoding/enc_encoding.cc</c>.
        /// </summary>
        public static Status ModularCompress(
            ModularImage image,
            ModularOptions options,
            nuint groupId,
            IReadOnlyList<PropertyDecisionNode> tree,
            GroupHeader header,
            List<Token> tokens,
            out nuint width)
        {
            width           = 0;
            int nbChannels  = image.Channels.Count;

            if (image.W == 0 || image.H == 0 || nbChannels < 1)
            {
                return true;
            }

            if (image.Error)
            {
                return Status.Failure("Invalid image");
            }

            Bundle.Init(header);

            if (ModularPredictors.PredictorHasWeighted(options.Predictor))
            {
                header.WpHeader.SetPredictorMode(options.WpMode);
            }

            header.Transforms.Clear();

            for (int i = 0; i < image.Transforms.Count; i++)
            {
                header.Transforms.Add(image.Transforms[i].Clone());
            }

            header.UseGlobalTree = true;

            nuint imageWidth  = 0;
            nuint totalTokens = CountGroupTokens(image, options, ref imageWidth);
            width             = imageWidth;

            if (options.ZeroTokens)
            {
                for (nuint i = 0; i < totalTokens; i++)
                {
                    tokens.Add(new Token(0, 0));
                }

                return true;
            }

            return EncodeImageChannels(image, options, groupId, tree, header.WpHeader, tokens);
        }

        /// <summary>
        /// Compresses a single <paramref name="image"/> and writes its header, local MA tree, histograms, and tokens into <paramref name="writer"/>.
        /// Ported from <c>jxl::ModularGenericCompress</c> in <c>lib/jxl/modular/encoding/enc_encoding.cc</c>.
        /// </summary>
        public static Status ModularGenericCompress(
            ModularImage image,
            ModularOptions opts,
            BitWriter writer,
            AuxOut? auxOut = null,
            LayerType layer = LayerType.Header,
            nuint groupId = 0)
        {
            int nbChannels = image.Channels.Count;

            if (image.W == 0 || image.H == 0 || nbChannels < 1)
            {
                return true;
            }

            if (image.Error)
            {
                return Status.Failure("Invalid image");
            }

            ModularOptions options = opts.Clone();

            if (options.Predictor == Predictor.Undefined)
            {
                options.Predictor = Predictor.Gradient;
            }

            GroupHeader header = new GroupHeader();
            Bundle.Init(header);

            if (ModularPredictors.PredictorHasWeighted(options.Predictor))
            {
                header.WpHeader.SetPredictorMode(options.WpMode);
            }

            for (int i = 0; i < image.Transforms.Count; i++)
            {
                header.Transforms.Add(image.Transforms[i].Clone());
            }

            Status hdrOk = Bundle.Write(header, writer, layer, auxOut);

            if (!hdrOk.IsOk)
            {
                return hdrOk;
            }

            return EncodeGenericTreeAndData(image, options, writer, auxOut, layer, groupId, header);
        }

        /// <summary>
        /// Estimates the bit cost of encoding <paramref name="img"/> with a fixed ClampedGradient tree.
        /// Ported from <c>jxl::EstimateCost</c> in <c>lib/jxl/enc_modular_simd.cc</c>.
        /// </summary>
        /// <param name="img">Modular image to evaluate.</param>
        /// <returns>Estimated bit cost.</returns>
        public static StatusOr<float> EstimateCost(ModularImage img)
        {
            nuint histoCost     = 0;
            float histoCostFrac = 0.0f;
            nuint extraBits     = 0;
            HybridUintConfig config = new HybridUintConfig();
            int nc              = EstimateCostCutoffs.Length + 1;
            Histogram[] histo   = new Histogram[nc];

            for (int i = 0; i < nc; i++)
            {
                histo[i] = new Histogram();
            }

            for (int cIdx = 0; cIdx < img.Channels.Count; cIdx++)
            {
                Channel ch = img.Channels[cIdx];
                AccumulateChannelCost(ch, config, histo, ref extraBits);

                for (int i = 0; i < nc; i++)
                {
                    float fCost    = histo[i].ShannonEntropy();
                    nuint iCost    = (nuint)fCost;
                    histoCost     += iCost;
                    histoCostFrac += fCost - iCost;
                    histo[i].Clear();
                }
            }

            nuint totalCost = extraBits + histoCost + (nuint)histoCostFrac;

            return (float)totalCost;
        }

        private static void AccumulateChannelCost(
            Channel ch,
            HybridUintConfig config,
            Histogram[] histo,
            ref nuint extraBits)
        {
            for (nuint y = 0; y < ch.H; y++)
            {
                ReadOnlySpan<int> r     = ch.ConstRow(y);
                ReadOnlySpan<int> rPrev = y > 0 ? ch.ConstRow(y - 1) : r;

                for (int x = 0; x < (int)ch.W; x++)
                {
                    int left    = x > 0 ? r[x - 1] : (y > 0 ? rPrev[x] : 0);
                    int top     = y > 0 ? rPrev[x] : left;
                    int topleft = x > 0 && y > 0 ? rPrev[x - 1] : left;
                    int maxVal  = Math.Max(left, Math.Max(top, topleft));
                    int minVal  = Math.Min(left, Math.Min(top, topleft));
                    uint maxDiff = (uint)((long)maxVal - minVal);

                    int ctx = 0;

                    for (int k = 0; k < EstimateCostCutoffs.Length; k++)
                    {
                        if (maxDiff < EstimateCostCutoffs[k])
                        {
                            ctx++;
                        }
                    }

                    int res = r[x] - ModularPredictors.ClampedGradient(top, left, topleft);
                    config.Encode(PackSigned.Pack(res), out uint token, out uint nbits, out _);
                    histo[ctx].Add(token);
                    extraBits += nbits;
                }
            }
        }

        private static List<PropertyDecisionNode> BuildPredefinedAcMetaTree(nuint totalPixels)
        {
            if (totalPixels < 1024)
            {
                return new List<PropertyDecisionNode> { PropertyDecisionNode.Leaf(Predictor.Left) };
            }

            return new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(0, 1, 1),
                PropertyDecisionNode.Split(0, 2, 3),
                PropertyDecisionNode.Split(0, 0, 5),
                PropertyDecisionNode.Split(6, 3, 21),
                PropertyDecisionNode.Split(2, 0, 7),
                PropertyDecisionNode.Leaf(Predictor.Gradient),
                PropertyDecisionNode.Leaf(Predictor.Gradient),
                PropertyDecisionNode.Split(7, 5, 9),
                PropertyDecisionNode.Split(7, 5, 15),
                PropertyDecisionNode.Split(7, 11, 11),
                PropertyDecisionNode.Split(7, 3, 13),
                PropertyDecisionNode.Leaf(Predictor.Left),
                PropertyDecisionNode.Leaf(Predictor.Left),
                PropertyDecisionNode.Leaf(Predictor.Left),
                PropertyDecisionNode.Leaf(Predictor.Left),
                PropertyDecisionNode.Split(7, 11, 17),
                PropertyDecisionNode.Split(7, 3, 19),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Split(7, 3, 23),
                PropertyDecisionNode.Split(7, 3, 25),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
            };
        }

        private static List<PropertyDecisionNode> MakeFixedTree(
            int property,
            ReadOnlySpan<int> cutoffs,
            Predictor pred,
            nuint numPixels,
            int bitdepth)
        {
            nuint logPx   = numPixels > 0 ? (nuint)Bits.CeilLog2Nonzero(numPixels) : 0;
            int minGap    = logPx < 14 ? (int)(8 * (14 - logPx)) : 0;
            int shift     = bitdepth > 11 ? Math.Min(4, bitdepth - 11) : 0;
            int mul       = 1 << shift;

            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Leaf(pred),
            };

            Queue<NodeRangeInfo> q = new Queue<NodeRangeInfo>();
            q.Enqueue(new NodeRangeInfo { Begin = 0, End = cutoffs.Length, Pos = 0 });

            while (q.Count > 0)
            {
                NodeRangeInfo info = q.Dequeue();

                if (info.Begin + minGap >= info.End)
                {
                    continue;
                }

                int split     = (info.Begin + info.End) / 2;
                int cutoff    = cutoffs[split] * mul;
                tree[info.Pos] = PropertyDecisionNode.Split(property, cutoff, tree.Count);

                q.Enqueue(new NodeRangeInfo { Begin = split + 1, End = info.End, Pos = tree.Count });
                tree.Add(PropertyDecisionNode.Leaf(pred));

                q.Enqueue(new NodeRangeInfo { Begin = info.Begin, End = split, Pos = tree.Count });
                tree.Add(PropertyDecisionNode.Leaf(pred));
            }

            return tree;
        }

        private static void PreQuantizeAndGatherSamples(
            IReadOnlyList<ModularImage> images,
            IReadOnlyList<ModularOptions> options,
            uint start,
            uint stop,
            IReadOnlyList<ModularMultiplierInfo> mulInfo,
            TreeSamples treeSamples,
            out StaticPropRange range,
            out nuint totalPixels,
            out Status status)
        {
            uint maxC                    = 0;
            List<int> pixelSamples       = new List<int>();
            List<int> diffSamples        = new List<int>();
            List<uint> groupPixelCount   = new List<uint>();
            List<uint> channelPixelCount = new List<uint>();

            for (uint i = start; i < stop; i++)
            {
                maxC = Math.Max((uint)images[(int)i].Channels.Count, maxC);
                EncMa.CollectPixelSamples(
                    images[(int)i],
                    options[(int)i],
                    i,
                    groupPixelCount,
                    channelPixelCount,
                    pixelSamples,
                    diffSamples);
            }

            range          = default;
            range[0]       = (0, maxC);
            range[1]       = (start, stop);

            treeSamples.PreQuantizeProperties(
                range,
                mulInfo,
                groupPixelCount,
                channelPixelCount,
                pixelSamples,
                diffSamples,
                options[(int)start].MaxPropertyValues);

            totalPixels = ComputeInitialTotalPixels(images[(int)start], options[(int)start]);
            status      = GatherAllGroupTreeData(images, options, start, stop, treeSamples, ref totalPixels);
        }

        private static nuint ComputeInitialTotalPixels(ModularImage image, ModularOptions opts)
        {
            nuint totalPixels = 0;

            for (int i = 0; i < image.Channels.Count; i++)
            {
                Channel ch = image.Channels[i];

                if ((nuint)i >= image.NbMetaChannels &&
                    (ch.W > opts.MaxChanSize || ch.H > opts.MaxChanSize))
                {
                    break;
                }

                totalPixels += ch.W * ch.H;
            }

            return Math.Max(totalPixels, (nuint)1);
        }

        private static Status GatherAllGroupTreeData(
            IReadOnlyList<ModularImage> images,
            IReadOnlyList<ModularOptions> options,
            uint start,
            uint stop,
            TreeSamples treeSamples,
            ref nuint totalPixels)
        {
            WeightedHeader wpHeader = new WeightedHeader();

            for (uint i = start; i < stop; i++)
            {
                ModularImage img    = images[(int)i];
                ModularOptions opts = options[(int)i];

                if (img.W == 0 || img.H == 0 || img.Channels.Count < 1)
                {
                    continue;
                }

                if (img.Error)
                {
                    return Status.Failure("Invalid image");
                }

                Bundle.Init(wpHeader);

                if (ModularPredictors.PredictorHasWeighted(opts.Predictor))
                {
                    wpHeader.SetPredictorMode(opts.WpMode);
                }

                for (int c = 0; c < img.Channels.Count; c++)
                {
                    Channel ch = img.Channels[c];

                    if ((nuint)c >= img.NbMetaChannels &&
                        (ch.W > opts.MaxChanSize || ch.H > opts.MaxChanSize))
                    {
                        break;
                    }

                    if (ch.W == 0 || ch.H == 0)
                    {
                        continue;
                    }

                    Status gatherOk = GatherChannelTreeData(img, c, i, wpHeader, opts, treeSamples, ref totalPixels);

                    if (!gatherOk.IsOk)
                    {
                        return gatherOk;
                    }
                }
            }

            return true;
        }

        private static Status GatherChannelTreeData(
            ModularImage image,
            int chan,
            uint groupId,
            WeightedHeader wpHeader,
            ModularOptions options,
            TreeSamples treeSamples,
            ref nuint totalPixels)
        {
            Channel channel = image.Channels[chan];
            Span<int> staticProps = stackalloc int[ModularOptions.KNumStaticProperties]
            {
                chan,
                (int)groupId,
            };

            int numProps         = ModularPredictors.KNumNonrefProperties +
                                   (ModularPredictors.KExtraPropsPerChannel * options.MaxProperties);
            int[] properties     = new int[numProps];
            double pixelFraction = Math.Min(1.0, options.NbRepeats);

            if (pixelFraction > 0)
            {
                pixelFraction = Math.Max(pixelFraction, Math.Min(1.0, 1024.0 / (channel.W * channel.H)));
            }

            ulong threshold             = (ulong)(uint.MaxValue * pixelFraction);
            ulong s0                    = 0x94D049BB133111EBUL;
            ulong s1                    = 0xBF58476D1CE4E5B9UL;
            StatusOr<Channel> refsOr    = Channel.Create(
                channel.MemoryManager,
                (nuint)(numProps - ModularPredictors.KNumNonrefProperties),
                channel.W);

            if (!refsOr.IsOk)
            {
                return refsOr.Status;
            }

            using Channel references = refsOr.Value;
            WeightedState wpState    = new WeightedState(wpHeader, channel.W, channel.H);
            treeSamples.PrepareForSamples((nuint)((pixelFraction * channel.H * channel.W) + 64));

            Span<long> pred = stackalloc long[ModularOptions.KNumModularPredictors];

            for (nuint y = 0; y < channel.H; y++)
            {
                ReadOnlySpan<int> p = channel.ConstRow(y);
                ModularPredictors.PrecomputeReferences(channel, y, image, (uint)chan, references);
                ModularPredictors.InitPropsRow(properties, staticProps, (int)y);

                for (int x = 0; x < (int)channel.W; x++)
                {
                    EvaluateLearnSample(channel, x, (int)y, properties, references, wpState, treeSamples, pred);
                    totalPixels++;

                    ulong a    = s0;
                    ulong b    = s1;
                    ulong bits = unchecked(a + b);
                    s0         = b;
                    a         ^= a << 23;
                    a         ^= b ^ (a >> 18) ^ (b >> 5);
                    s1         = a;

                    if ((bits >> 32) <= threshold)
                    {
                        treeSamples.AddSample(p[x], properties, pred);
                    }

                    wpState.UpdateErrors(p[x], (nuint)x, y, channel.W);
                }
            }

            return true;
        }

        private static void EvaluateLearnSample(
            Channel channel,
            int x,
            int y,
            Span<int> properties,
            Channel references,
            WeightedState wpState,
            TreeSamples treeSamples,
            Span<long> predictions)
        {
            NeighborPixels neighbors = ModularPredictors.GatherNeighbors(channel, x, y, noEdgeCases: false);
            ModularPredictors.ComputeSpatialProperties(properties, x, in neighbors);

            long wpPred = wpState.Predict(
                (nuint)x,
                (nuint)y,
                channel.W,
                in neighbors,
                properties,
                ModularPredictors.KWPProp,
                computeProperties: true);

            if (references.W > 0)
            {
                ReadOnlySpan<int> rp = references.ConstRow((nuint)x);

                for (int i = 0; i < (int)references.W; i++)
                {
                    properties[ModularPredictors.KNumNonrefProperties + i] = rp[i];
                }
            }

            if (treeSamples.NumPredictors != 1)
            {
                for (int i = 0; i < ModularOptions.KNumModularPredictors; i++)
                {
                    predictions[i] = ModularPredictors.PredictOne((Predictor)i, in neighbors, wpPred);
                }
            }
            else
            {
                Predictor single = treeSamples.PredictorFromIndex(0);
                predictions[(int)single] = ModularPredictors.PredictOne(single, in neighbors, wpPred);
            }
        }

        private static StatusOr<List<PropertyDecisionNode>> FinalizeLearnedTree(
            TreeSamples treeSamples,
            nuint totalPixels,
            ModularOptions options,
            IReadOnlyList<ModularMultiplierInfo> multiplierInfo,
            StaticPropRange staticPropRange)
        {
            for (int i = 0; i < ModularOptions.KNumStaticProperties; i++)
            {
                if (staticPropRange[i, 1] == 0)
                {
                    staticPropRange[i, 1] = uint.MaxValue;
                }
            }

            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>();

            if (!treeSamples.HasSamples)
            {
                tree.Add(PropertyDecisionNode.Leaf(treeSamples.PredictorFromIndex(0)));

                return tree;
            }

            float pixelFraction = (float)treeSamples.NumSamples / totalPixels;
            float requiredCost  = (pixelFraction * 0.9f) + 0.1f;
            treeSamples.AllSamplesDone();

            Status ok = EncMa.ComputeBestTree(
                treeSamples,
                options.SplittingHeuristicsNodeThreshold * requiredCost,
                multiplierInfo,
                staticPropRange,
                options.FastDecodeMultiplier,
                tree);

            if (!ok.IsOk)
            {
                return ok;
            }

            return tree;
        }

        private static nuint CountGroupTokens(ModularImage image, ModularOptions options, ref nuint imageWidth)
        {
            nuint totalTokens = 0;

            for (int i = 0; i < image.Channels.Count; i++)
            {
                Channel ch = image.Channels[i];

                if ((nuint)i >= image.NbMetaChannels &&
                    (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize))
                {
                    break;
                }

                if (ch.W > imageWidth)
                {
                    imageWidth = ch.W;
                }

                totalTokens += ch.W * ch.H;
            }

            return totalTokens;
        }

        private static Status EncodeImageChannels(
            ModularImage image,
            ModularOptions options,
            nuint groupId,
            IReadOnlyList<PropertyDecisionNode> tree,
            WeightedHeader wpHeader,
            List<Token> tokens)
        {
            for (int i = 0; i < image.Channels.Count; i++)
            {
                Channel ch = image.Channels[i];

                if ((nuint)i >= image.NbMetaChannels &&
                    (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize))
                {
                    break;
                }

                if (ch.W == 0 || ch.H == 0)
                {
                    continue;
                }

                Status chOk = EncodeModularChannelMAANS(
                    image,
                    i,
                    wpHeader,
                    tree,
                    tokens,
                    groupId,
                    options.SkipEncoderFastPath);

                if (!chOk.IsOk)
                {
                    return chOk;
                }
            }

            return true;
        }

        private static Status EncodeModularChannelMAANS(
            ModularImage image,
            int chan,
            WeightedHeader wpHeader,
            IReadOnlyList<PropertyDecisionNode> globalTree,
            List<Token> tokens,
            nuint groupId,
            bool skipEncoderFastPath)
        {
            Channel channel = image.Channels[chan];

            if (channel.W == 0 || channel.H == 0)
            {
                return Status.Failure("Empty modular channel");
            }

            Span<int> staticProps = stackalloc int[ModularOptions.KNumStaticProperties]
            {
                chan,
                (int)groupId,
            };

            FlatDecisionNode[] tree = ModularEncoding.FilterTree(
                globalTree,
                staticProps,
                out nuint numProps,
                out bool useWp,
                out bool isWpOnly,
                out bool isGradientOnly);

            _ = isWpOnly;
            _ = isGradientOnly;

            if (tree.Length == 1 &&
                tree[0].Predictor == Predictor.Zero &&
                tree[0].Multiplier == 1 &&
                tree[0].PredictorOffset == 0 &&
                !skipEncoderFastPath)
            {
                EncodeZeroPredictorChannel(channel, tree[0].ChildID, tokens);

                return true;
            }

            if (tree.Length == 1 &&
                tree[0].Predictor == Predictor.Gradient &&
                tree[0].Multiplier == 1 &&
                tree[0].PredictorOffset == 0 &&
                !skipEncoderFastPath)
            {
                return EncodeSingleGradientChannel(channel, tree[0].ChildID, tokens);
            }

            return EncodeTreeChannelGeneral(image, channel, chan, wpHeader, tree, staticProps, (int)numProps, useWp, tokens);
        }

        private static void EncodeZeroPredictorChannel(Channel channel, uint ctxId, List<Token> tokens)
        {
            for (nuint y = 0; y < channel.H; y++)
            {
                ReadOnlySpan<int> p = channel.ConstRow(y);

                for (int x = 0; x < (int)channel.W; x++)
                {
                    tokens.Add(new Token(ctxId, PackSigned.Pack(p[x])));
                }
            }
        }

        private static Status EncodeSingleGradientChannel(Channel channel, uint ctxId, List<Token> tokens)
        {
            bool unhealthy = false;

            for (nuint y = 0; y < channel.H; y++)
            {
                ReadOnlySpan<int> r     = channel.ConstRow(y);
                ReadOnlySpan<int> rPrev = y > 0 ? channel.ConstRow(y - 1) : r;

                for (int x = 0; x < (int)channel.W; x++)
                {
                    long left    = x > 0 ? r[x - 1] : (y > 0 ? rPrev[x] : 0);
                    long top     = y > 0 ? rPrev[x] : left;
                    long topleft = x > 0 && y > 0 ? rPrev[x - 1] : left;
                    int guess    = ModularPredictors.ClampedGradient(top, left, topleft);
                    long diff    = (long)r[x] - guess;

                    if (diff < int.MinValue || diff > int.MaxValue)
                    {
                        unhealthy = true;
                    }

                    tokens.Add(new Token(ctxId, PackSigned.Pack((int)diff)));
                }
            }

            return unhealthy ? Status.Failure("Residual overflow") : Status.OkStatus;
        }

        private static Status EncodeTreeChannelGeneral(
            ModularImage image,
            Channel channel,
            int chan,
            WeightedHeader wpHeader,
            FlatDecisionNode[] tree,
            ReadOnlySpan<int> staticProps,
            int numProps,
            bool useWp,
            List<Token> tokens)
        {
            MATreeLookup treeLookup  = new MATreeLookup(tree);
            int[] properties         = new int[numProps];
            nuint extraProps         = (nuint)(numProps - ModularPredictors.KNumNonrefProperties);
            StatusOr<Channel> refsOr = Channel.Create(channel.MemoryManager, extraProps, channel.W);

            if (!refsOr.IsOk)
            {
                return refsOr.Status;
            }

            using Channel references = refsOr.Value;
            WeightedState? wpState   = useWp ? new WeightedState(wpHeader, channel.W, channel.H) : null;

            for (nuint y = 0; y < channel.H; y++)
            {
                ReadOnlySpan<int> p = channel.ConstRow(y);
                ModularPredictors.PrecomputeReferences(channel, y, image, (uint)chan, references);
                ModularPredictors.InitPropsRow(properties, staticProps, (int)y);

                for (int x = 0; x < (int)channel.W; x++)
                {
                    PredictionResult res = wpState != null
                        ? ModularPredictors.PredictTreeWP(properties, channel, x, (int)y, treeLookup, references, wpState)
                        : ModularPredictors.PredictTreeNoWP(properties, channel, x, (int)y, treeLookup, references);

                    long residual = (p[x] - res.Guess) / res.Multiplier;
                    tokens.Add(new Token((uint)res.Context, PackSigned.Pack((int)residual)));
                    wpState?.UpdateErrors(p[x], (nuint)x, y, channel.W);
                }
            }

            return true;
        }

        private static Status EncodeGenericTreeAndData(
            ModularImage image,
            ModularOptions options,
            BitWriter writer,
            AuxOut? auxOut,
            LayerType layer,
            nuint groupId,
            GroupHeader header)
        {
            List<PropertyDecisionNode> tree;

            if (options.Kind == ModularOptions.TreeKind.Learn)
            {
                StatusOr<List<PropertyDecisionNode>> treeOr = LearnTree(
                    new[] { image },
                    new[] { options },
                    0,
                    1);

                if (!treeOr.IsOk)
                {
                    return treeOr.Status;
                }

                tree = treeOr.Value;
            }
            else
            {
                nuint totalPixels = ComputeInitialTotalPixels(image, options);
                tree              = PredefinedTree(options.Kind, totalPixels, image.BitDepth, options.MaxProperties);
            }

            List<PropertyDecisionNode> decodedTree = new List<PropertyDecisionNode>();
            List<List<Token>> treeTokens           = new List<List<Token>> { new List<Token>() };

            Status tokOk = EncMa.TokenizeTree(tree, treeTokens[0], decodedTree);

            if (!tokOk.IsOk)
            {
                return tokOk;
            }

            HistogramParams histParams = (options.HistogramParams as HistogramParams) ?? new HistogramParams();
            EntropyEncodingData code   = new EntropyEncodingData();

            StatusOr<nuint> treeCostOr = EncAns.BuildAndEncodeHistograms(
                image.MemoryManager,
                histParams,
                (nuint)MATreeContext.NumTreeContexts,
                treeTokens,
                code,
                writer,
                (LayerType.ModularTree, auxOut));

            if (!treeCostOr.IsOk)
            {
                return treeCostOr.Status;
            }

            Status writeTreeOk = EncAns.WriteTokens(treeTokens[0], code, 0, writer, LayerType.ModularTree, auxOut);

            if (!writeTreeOk.IsOk)
            {
                return writeTreeOk;
            }

            List<List<Token>> tokens = new List<List<Token>> { new List<Token>() };
            Status compOk            = ModularCompress(image, options, groupId, decodedTree, header, tokens[0], out nuint imageWidth);

            if (!compOk.IsOk)
            {
                return compOk;
            }

            EntropyEncodingData dataCode = new EntropyEncodingData();
            HistogramParams dataParams   = new HistogramParams
            {
                Clustering             = histParams.Clustering,
                UintMethod             = histParams.UintMethod,
                Lz77Method             = histParams.Lz77Method,
                AnsHistogramStrategy   = histParams.AnsHistogramStrategy,
                MaxHistograms          = histParams.MaxHistograms,
                ForceHuffman           = histParams.ForceHuffman,
                InitializeGlobalState  = histParams.InitializeGlobalState,
                StreamingMode          = histParams.StreamingMode,
                AddMissingSymbols      = histParams.AddMissingSymbols,
                AddFixedHistograms     = histParams.AddFixedHistograms,
                ImageWidths            = new List<nuint>(histParams.ImageWidths) { imageWidth },
            };

            StatusOr<nuint> dataCostOr = EncAns.BuildAndEncodeHistograms(
                image.MemoryManager,
                dataParams,
                ((nuint)decodedTree.Count + 1u) / 2u,
                tokens,
                dataCode,
                writer,
                (layer, auxOut));

            if (!dataCostOr.IsOk)
            {
                return dataCostOr.Status;
            }

            return EncAns.WriteTokens(tokens[0], dataCode, 0, writer, layer, auxOut);
        }
    }

    /// <summary>
    /// Arguments for <see cref="ModularFrameEncoder.ComputeEncodingData"/>.
    /// </summary>
    public sealed class ModularEncodingDataArgs
    {
        /// <summary>Current frame header.</summary>
        public required FrameHeader FrameHeader;

        /// <summary>Image metadata.</summary>
        public required ImageMetadata Metadata;

        /// <summary>Optional 3-plane color image.</summary>
        public Image3<float>? Color;

        /// <summary>Extra channel planes.</summary>
        public IReadOnlyList<ImagePlane<float>> ExtraChannels = Array.Empty<ImagePlane<float>>();

        /// <summary>Group rectangle within <see cref="Color"/>.</summary>
        public Rect GroupRect;

        /// <summary>Patch dimensions.</summary>
        public FrameDimensions PatchDim = new FrameDimensions();

        /// <summary>Frame area rectangle.</summary>
        public Rect FrameAreaRect;

        /// <summary>Passes encoder state.</summary>
        public required PassesEncoderState EncState;

        /// <summary>CMS interface.</summary>
        public JxlCmsInterface Cms = new JxlCmsInterface();

        /// <summary>Optional thread pool.</summary>
        public ThreadPool? Pool;

        /// <summary>Optional auxiliary statistics output.</summary>
        public AuxOut? AuxOut;

        /// <summary>Whether color channels are encoded by Modular.</summary>
        public bool DoColor = true;
    }

    /// <summary>
    /// Frame-level Modular encoder orchestrating VarDCT DC, AC metadata, quantization tables,
    /// Modular color/extra channels, global MA tree learning, and group token emission.
    /// Ported from <c>jxl::ModularFrameEncoder</c> in <c>lib/jxl/enc_modular.h</c> and <c>lib/jxl/enc_modular.cc</c>.
    /// </summary>
    public sealed unsafe class ModularFrameEncoder : IDisposable
    {
        private const float KQDeadzone             = 0.62f;
        private const float KSqueezeQualityFactor  = 0.35f;
        private const float KSqueezeLumaFactor     = 1.1f;
        private const float KSqueezeQualityXyb     = 4.0f;
        private const float KSqueezeQualityY       = 1.5f;

        private static readonly float[][] SqueezeXybQTable =
        {
            new[]
            {
                163.84f, 81.92f, 40.96f, 20.48f, 10.24f, 5.12f, 2.56f, 1.28f,
                0.64f, 0.32f, 0.16f, 0.08f, 0.04f, 0.02f, 0.01f, 0.005f,
            },
            new[]
            {
                1024f, 512f, 256f, 128f, 64f, 32f, 16f, 8f,
                4f, 2f, 1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f,
            },
            new[]
            {
                2048f, 1024f, 512f, 256f, 128f, 64f, 32f, 16f,
                8f, 4f, 2f, 1f, 0.5f, 0.5f, 0.5f, 0.5f,
            },
        };

        private static readonly float[] SqueezeLumaQTable =
        {
            163.84f, 81.92f, 40.96f, 20.48f, 10.24f, 5.12f, 2.56f, 1.28f,
            0.64f, 0.32f, 0.16f, 0.08f, 0.04f, 0.02f, 0.01f, 0.005f,
        };

        private static readonly float[] SqueezeChromaQTable =
        {
            1024f, 512f, 256f, 128f, 64f, 32f, 16f, 8f,
            4f, 2f, 1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f,
        };

        private static readonly int[] RctCandidates =
        {
            (0 * 7) + 0, (0 * 7) + 6, (0 * 7) + 5, (1 * 7) + 3, (3 * 7) + 5,
            (5 * 7) + 5, (1 * 7) + 5, (2 * 7) + 5, (1 * 7) + 1, (0 * 7) + 4,
            (1 * 7) + 2, (2 * 7) + 1, (2 * 7) + 2, (2 * 7) + 3, (4 * 7) + 4,
            (4 * 7) + 5, (0 * 7) + 2, (0 * 7) + 1, (0 * 7) + 3,
        };

        private readonly JxlMemoryManager? _memoryManager;
        private readonly List<ModularImage> _streamImages      = new List<ModularImage>();
        private readonly List<ModularOptions> _streamOptions   = new List<ModularOptions>();
        private readonly List<uint> _quants                    = new List<uint>();
        private List<PropertyDecisionNode> _tree               = new List<PropertyDecisionNode>();
        private readonly List<List<Token>> _treeTokens         = new List<List<Token>>();
        private readonly List<GroupHeader> _streamHeaders      = new List<GroupHeader>();
        private readonly List<List<Token>> _tokens             = new List<List<Token>>();
        private readonly EntropyEncodingData _code             = new EntropyEncodingData();
        private FrameDimensions _frameDim                      = new FrameDimensions();
        private CompressParams _cparams                        = new CompressParams();
        private readonly List<nuint> _treeSplits               = new List<nuint>();
        private readonly List<List<uint>> _giChannel           = new List<List<uint>>();
        private readonly List<nuint> _imageWidths              = new List<nuint>();
        private readonly List<GroupParams> _streamParams       = new List<GroupParams>();

        static ModularFrameEncoder()
        {
            EncQuantWeights.RawQuantTableEncoder = (mm, sx, sy, w, enc, idx, mfe) =>
                EncodeQuantTable(mm, sx, sy, w, enc, idx, mfe as ModularFrameEncoder);
        }

        private struct GroupParams
        {
            public Rect Rect;
            public int MinShift;
            public int MaxShift;
            public ModularStreamId Id;
        }

        private ModularFrameEncoder(JxlMemoryManager? memoryManager)
        {
            _memoryManager = memoryManager;
        }

        /// <summary>Per-DC-group count of AC metadata entries.</summary>
        public readonly List<nuint> AcMetadataSize = new List<nuint>();

        /// <summary>Per-DC-group extra DC precision shift.</summary>
        public readonly List<byte> ExtraDcPrecision = new List<byte>();

        /// <summary>Gets the memory manager associated with this encoder.</summary>
        public JxlMemoryManager? MemoryManager => _memoryManager;

        /// <summary>
        /// Creates and initializes a new <see cref="ModularFrameEncoder"/>.
        /// Ported from <c>jxl::ModularFrameEncoder::Create</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public static StatusOr<ModularFrameEncoder> Create(
            JxlMemoryManager? memoryManager,
            FrameHeader frameHeader,
            CompressParams cparamsOrig,
            bool streamingMode)
        {
            EnsureStaticInit();
            ModularFrameEncoder self = new ModularFrameEncoder(memoryManager);
            Status initOk            = self.Init(frameHeader, cparamsOrig, streamingMode);

            if (!initOk.IsOk)
            {
                self.Dispose();

                return initOk;
            }

            return self;
        }

        /// <summary>
        /// Ensures the static constructor has registered <see cref="EncQuantWeights.RawQuantTableEncoder"/>.
        /// </summary>
        public static void EnsureStaticInit()
        {
        }

        /// <summary>
        /// Prepares all Modular substreams for the frame or streaming patch.
        /// Ported from <c>jxl::ModularFrameEncoder::ComputeEncodingData</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status ComputeEncodingData(ModularEncodingDataArgs args)
        {
            FrameHeader frameHeader                     = args.FrameHeader;
            ImageMetadata metadata                      = args.Metadata;
            Image3<float>? color                        = args.Color;
            IReadOnlyList<ImagePlane<float>> extraChans = args.ExtraChannels;
            PassesEncoderState encState                 = args.EncState;
            bool doColor                                = args.DoColor;
            bool groupwise                              = encState.StreamingMode;

            Status preOk = PreprocessColorFeatures(args, groupwise);

            if (!preOk.IsOk)
            {
                return preOk;
            }

            Status convOk = ConvertInputToGlobalModularImage(
                frameHeader,
                metadata,
                color,
                extraChans,
                args.GroupRect,
                args.PatchDim,
                encState,
                args.Pool,
                doColor,
                out int maxBitdepth,
                out int maxval);

            if (!convOk.IsOk)
            {
                return convOk;
            }

            Status transOk = ApplyGlobalTransformsAndQuantization(
                frameHeader,
                metadata,
                extraChans.Count,
                doColor,
                groupwise,
                args.Pool,
                ref maxBitdepth,
                ref maxval);

            if (!transOk.IsOk)
            {
                return transOk;
            }

            return PopulateAndPrepareGroups(frameHeader, args.PatchDim, args.FrameAreaRect, encState, doColor, groupwise, args.Pool);
        }

        /// <summary>
        /// Learns or constructs the global MA decision tree across all non-empty substreams.
        /// Ported from <c>jxl::ModularFrameEncoder::ComputeTree</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status ComputeTree(ThreadPool? pool = null)
        {
            List<ModularMultiplierInfo> multiplierInfo = BuildMultiplierInfo();

            if (_cparams.CustomFixedTree.Count > 0)
            {
                _tree = new List<PropertyDecisionNode>(_cparams.CustomFixedTree);
            }
            else if (_cparams.SpeedTier < SpeedTier.kFalcon || !_cparams.ModularMode)
            {
                Status learnOk = LearnMergedStreamTrees(multiplierInfo, pool);

                if (!learnOk.IsOk)
                {
                    return learnOk;
                }

                if (_tree.Count == 0)
                {
                    return true;
                }
            }
            else
            {
                BuildFastFixedTree();
            }

            _treeTokens.Clear();
            _treeTokens.Add(new List<Token>());
            List<PropertyDecisionNode> decodedTree = new List<PropertyDecisionNode>();

            Status tokOk = EncMa.TokenizeTree(_tree, _treeTokens[0], decodedTree);

            if (!tokOk.IsOk)
            {
                return tokOk;
            }

            _tree = decodedTree;

            return true;
        }

        /// <summary>
        /// Tokenizes all Modular substreams using the learned global MA tree.
        /// Ported from <c>jxl::ModularFrameEncoder::ComputeTokens</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status ComputeTokens(ThreadPool? pool = null)
        {
            int numStreams = _streamImages.Count;
            _streamHeaders.Clear();
            _tokens.Clear();
            _imageWidths.Clear();

            for (int i = 0; i < numStreams; i++)
            {
                _streamHeaders.Add(new GroupHeader());
                _tokens.Add(new List<Token>());
                _imageWidths.Add(0);
            }

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)numStreams,
                ThreadPool.NoInit,
                (streamId, _) =>
                {
                    int idx = (int)streamId;
                    _tokens[idx].Clear();
                    Status s = EncModularEncoding.ModularCompress(
                        _streamImages[idx],
                        _streamOptions[idx],
                        streamId,
                        _tree,
                        _streamHeaders[idx],
                        _tokens[idx],
                        out nuint w);
                    _imageWidths[idx] = w;

                    return s;
                },
                "ComputeTokens");
        }

        /// <summary>
        /// Writes the global MA tree and global ANS histograms into <paramref name="writer"/>.
        /// Ported from <c>jxl::ModularFrameEncoder::EncodeGlobalInfo</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status EncodeGlobalInfo(bool streamingMode, BitWriter writer, AuxOut? auxOut = null)
        {
            bool skipRest  = false;
            Status bitOk   = writer.WithMaxBits(
                1,
                LayerType.ModularTree,
                auxOut,
                () =>
                {
                    if (_treeTokens.Count == 0 || _treeTokens[0].Count == 0)
                    {
                        writer.Write(1, 0);
                        skipRest = true;
                    }
                    else
                    {
                        writer.Write(1, 1);
                    }

                    return true;
                });

            if (!bitOk.IsOk || skipRest)
            {
                return bitOk;
            }

            HistogramParams histParams = HistogramParams.ForModular(_cparams, ExtraDcPrecision, streamingMode);
            EntropyEncodingData treeCode = new EntropyEncodingData();

            StatusOr<nuint> treeCostOr = EncAns.BuildAndEncodeHistograms(
                writer.MemoryManager,
                histParams,
                (nuint)MATreeContext.NumTreeContexts,
                _treeTokens,
                treeCode,
                writer,
                (LayerType.ModularTree, auxOut));

            if (!treeCostOr.IsOk)
            {
                return treeCostOr.Status;
            }

            Status writeTreeOk = EncAns.WriteTokens(_treeTokens[0], treeCode, 0, writer, LayerType.ModularTree, auxOut);

            if (!writeTreeOk.IsOk)
            {
                return writeTreeOk;
            }

            histParams.StreamingMode      = streamingMode;
            histParams.AddMissingSymbols  = streamingMode;
            histParams.ImageWidths        = new List<nuint>(_imageWidths);

            StatusOr<nuint> globalCostOr = EncAns.BuildAndEncodeHistograms(
                writer.MemoryManager,
                histParams,
                ((nuint)_tree.Count + 1u) / 2u,
                _tokens,
                _code,
                writer,
                (LayerType.ModularGlobal, auxOut));

            return globalCostOr.Status;
        }

        /// <summary>
        /// Encodes a specific Modular substream identified by <paramref name="stream"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::ModularFrameEncoder::EncodeStream</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status EncodeStream(
            BitWriter writer,
            AuxOut? auxOut,
            LayerType layer,
            ModularStreamId stream)
        {
            int streamId = (int)stream.ID(_frameDim);

            if (_streamImages[streamId].Channels.Count == 0)
            {
                return true;
            }

            if (_tokens.Count == 0)
            {
                return EncModularEncoding.ModularGenericCompress(
                    _streamImages[streamId],
                    _streamOptions[streamId],
                    writer,
                    auxOut,
                    layer,
                    (nuint)streamId);
            }

            Status hdrOk = Bundle.Write(_streamHeaders[streamId], writer, layer, auxOut);

            if (!hdrOk.IsOk)
            {
                return hdrOk;
            }

            return EncAns.WriteTokens(_tokens[streamId], _code, 0, writer, layer, auxOut);
        }

        /// <summary>
        /// Releases the image channels of substream <paramref name="stream"/>.
        /// </summary>
        public void ClearStreamData(ModularStreamId stream)
        {
            int streamId = (int)stream.ID(_frameDim);
            _streamImages[streamId].Dispose();
            _streamImages[streamId] = new ModularImage(_memoryManager);
        }

        /// <summary>
        /// Releases all group substream images and clears the active group parameter list.
        /// </summary>
        public void ClearModularStreamData()
        {
            for (int i = 0; i < _streamParams.Count; i++)
            {
                ClearStreamData(_streamParams[i].Id);
            }

            _streamParams.Clear();
        }

        /// <summary>
        /// Computes the absolute AC group index from a streaming DC group index and patch-local AC group index.
        /// </summary>
        public nuint ComputeStreamingAbsoluteAcGroupId(
            nuint dcGroupId,
            nuint acGroupId,
            FrameDimensions patchDim)
        {
            nuint dcGroupX = dcGroupId % _frameDim.XSizeDcGroups;
            nuint dcGroupY = dcGroupId / _frameDim.XSizeDcGroups;
            nuint acGroupX = acGroupId % patchDim.XSizeGroups;
            nuint acGroupY = acGroupId / patchDim.XSizeGroups;

            return ((dcGroupX * 8u) + acGroupX) + (((dcGroupY * 8u) + acGroupY) * _frameDim.XSizeGroups);
        }

        /// <summary>
        /// Quantizes a VarDCT DC group and stores it as a Modular substream, then roundtrip dequantizes into <c>encState.Shared.DcStorage</c>.
        /// Ported from <c>jxl::ModularFrameEncoder::AddVarDCTDC</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status AddVarDCTDC(
            FrameHeader frameHeader,
            Image3<float> dc,
            Rect r,
            nuint groupIndex,
            bool nlDc,
            PassesEncoderState encState,
            bool jpegTranscode)
        {
            _ = jpegTranscode;
            ExtraDcPrecision[(int)groupIndex] = (byte)(nlDc ? 1 : 0);
            float mul                         = 1 << ExtraDcPrecision[(int)groupIndex];
            int streamId                      = (int)ModularStreamId.VarDCTDC(groupIndex).ID(_frameDim);

            ConfigureVarDctDcStreamOptions(streamId, nlDc);

            StatusOr<ModularImage> imgOr = ModularImage.Create(dc.MemoryManager, (nuint)r.XSize, (nuint)r.YSize, 8, 3);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            _streamImages[streamId].Dispose();
            _streamImages[streamId] = imgOr.Value;

            Status quantOk = QuantizeVarDctDcGroup(frameHeader, dc, r, streamId, nlDc, mul, encState);

            if (!quantOk.IsOk)
            {
                return quantOk;
            }

            CompressedDc.DequantDC(
                in r,
                encState.Shared.DcStorage,
                encState.Shared.QuantDc,
                _streamImages[streamId],
                encState.Shared.Quantizer.MulDC(),
                1.0f / mul,
                encState.Shared.Cmap.Base.DCFactors(),
                frameHeader.ChromaSubsampling,
                encState.Shared.BlockCtxMap);

            return true;
        }

        /// <summary>
        /// Packs the VarDCT AC metadata (CfL maps, AC strategy, quant field, EPF sharpness) for <paramref name="groupIndex"/> into a Modular substream.
        /// Ported from <c>jxl::ModularFrameEncoder::AddACMetadata</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status AddACMetadata(
            Rect r,
            nuint groupIndex,
            bool jpegTranscode,
            PassesEncoderState encState)
        {
            int streamId         = (int)ModularStreamId.ACMetadata(groupIndex).ID(_frameDim);
            ModularOptions opts  = _streamOptions[streamId];
            opts.MaxChanSize     = 0xFFFFFF;

            if (opts.Predictor != Predictor.Weighted)
            {
                opts.WpTreeMode = ModularOptions.TreeMode.NoWP;
            }

            if (jpegTranscode)
            {
                opts.Kind = ModularOptions.TreeKind.JpegTranscodeACMeta;
            }
            else if (_cparams.SpeedTier >= SpeedTier.kFalcon)
            {
                opts.Kind = ModularOptions.TreeKind.FalconACMeta;
            }
            else if (_cparams.SpeedTier > SpeedTier.kKitten)
            {
                opts.Kind = ModularOptions.TreeKind.ACMeta;
            }

            if (_cparams.SpeedTier < SpeedTier.kSquirrel && _cparams.ForceCflJpegRecompression)
            {
                opts.Kind = ModularOptions.TreeKind.Learn;
            }

            opts.HistogramParams = _streamOptions[0].HistogramParams;

            return BuildAcMetadataChannels(r, groupIndex, streamId, encState);
        }

        /// <summary>
        /// Encodes a RAW quantization table into <paramref name="writer"/>.
        /// Ported from <c>jxl::ModularFrameEncoder::EncodeQuantTable</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public static Status EncodeQuantTable(
            JxlMemoryManager? memoryManager,
            nuint sizeX,
            nuint sizeY,
            BitWriter writer,
            QuantEncoding encoding,
            nuint idx,
            ModularFrameEncoder? modularFrameEncoder = null)
        {
            if (encoding.QrawTable == null ||
                (sizeX * sizeY * 3u) != (nuint)encoding.QrawTable.Length ||
                idx >= DequantMatrices.kNumQuantTables)
            {
                return Status.Failure("Invalid RAW quantization table");
            }

            if (!F16Coder.Write(encoding.QrawTableDen, writer))
            {
                return false;
            }

            if (modularFrameEncoder != null)
            {
                StatusOr<ModularStreamId> qtOr = ModularStreamId.QuantTable(idx);

                if (!qtOr.IsOk)
                {
                    return qtOr.Status;
                }

                return modularFrameEncoder.EncodeStream(writer, null, LayerType.Header, qtOr.Value);
            }

            StatusOr<ModularImage> imgOr = CreateQuantTableImage(memoryManager, sizeX, sizeY, encoding.QrawTable);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            using ModularImage image = imgOr.Value;
            ModularOptions cfopts    = new ModularOptions();

            return EncModularEncoding.ModularGenericCompress(image, cfopts, writer);
        }

        /// <summary>
        /// Stores a RAW quantization table in this <see cref="ModularFrameEncoder"/> for global tree learning and later emission.
        /// Ported from <c>jxl::ModularFrameEncoder::AddQuantTable</c> in <c>lib/jxl/enc_modular.cc</c>.
        /// </summary>
        public Status AddQuantTable(nuint sizeX, nuint sizeY, QuantEncoding encoding, nuint idx)
        {
            StatusOr<ModularStreamId> qtOr = ModularStreamId.QuantTable(idx);

            if (!qtOr.IsOk)
            {
                return qtOr.Status;
            }

            if (encoding.QrawTable == null || (sizeX * sizeY * 3u) != (nuint)encoding.QrawTable.Length)
            {
                return Status.Failure("Invalid RAW quantization table dimensions");
            }

            int streamId                 = (int)qtOr.Value.ID(_frameDim);
            StatusOr<ModularImage> imgOr = CreateQuantTableImage(
                _streamImages[streamId].MemoryManager,
                sizeX,
                sizeY,
                encoding.QrawTable);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            _streamImages[streamId].Dispose();
            _streamImages[streamId] = imgOr.Value;

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < _streamImages.Count; i++)
            {
                _streamImages[i].Dispose();
            }

            _streamImages.Clear();
        }

        private static StatusOr<ModularImage> CreateQuantTableImage(
            JxlMemoryManager? memoryManager,
            nuint sizeX,
            nuint sizeY,
            int[] qtable)
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(memoryManager, sizeX, sizeY, 8, 3);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            ModularImage image = imgOr.Value;
            int sx             = (int)sizeX;
            int sy             = (int)sizeY;

            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < sy; y++)
                {
                    Span<int> row = image.Channels[c].Row((nuint)y);
                    int baseIdx   = (c * sx * sy) + (y * sx);

                    for (int x = 0; x < sx; x++)
                    {
                        row[x] = qtable[baseIdx + x];
                    }
                }
            }

            return image;
        }

        private Status Init(FrameHeader frameHeader, CompressParams cparamsOrig, bool streamingMode)
        {
            _frameDim      = frameHeader.ToFrameDimensions();
            _cparams       = cparamsOrig.Clone();
            nuint nStreams = ModularStreamId.Num(_frameDim, frameHeader.Passes.NumPasses);

            ConfigureResponsiveAndDecodingTier();

            for (nuint i = 0; i < nStreams; i++)
            {
                _streamImages.Add(new ModularImage(_memoryManager));
            }

            ConfigureSplittingHeuristics(frameHeader, cparamsOrig, nStreams);
            ConfigureDefaultPredictor();
            Status splitsOk = InitializeTreeSplits(nStreams);

            if (!splitsOk.IsOk)
            {
                return splitsOk;
            }

            _cparams.Options.MaxChanSize = _frameDim.GroupDim;
            _cparams.Options.GroupDim    = _frameDim.GroupDim;

            for (nuint i = 0; i < nStreams; i++)
            {
                _streamOptions.Add(_cparams.Options.Clone());
            }

            if (_cparams.SpeedTier == SpeedTier.kFalcon)
            {
                _streamOptions[0].Kind = ModularOptions.TreeKind.WPFixedDC;
            }
            else if (_cparams.SpeedTier == SpeedTier.kThunder)
            {
                _streamOptions[0].Kind = ModularOptions.TreeKind.GradientFixedDC;
            }

            _streamOptions[0].HistogramParams = HistogramParams.ForModular(_cparams, Array.Empty<byte>(), streamingMode);

            return true;
        }

        private void ConfigureResponsiveAndDecodingTier()
        {
            if (_cparams.Responsive == 1 && _cparams.IsLossless() && _cparams.DecodingSpeedTier == 1)
            {
                _cparams.DecodingSpeedTier = 2;
            }

            if (_cparams.Responsive == 1 && _cparams.IsLossless() && _cparams.Colorspace < 0)
            {
                _cparams.Colorspace = 6;
            }

            if (_cparams.ModularPartIsLossless())
            {
                ApplyLosslessDecodingTierOptions();
            }

            if (_cparams.Responsive < 0)
            {
                _cparams.Responsive = _cparams.ModularPartIsLossless() ? 0 : 1;
            }

            _cparams.Options.SplittingHeuristicsNodeThreshold =
                75f + (14f * (int)_cparams.SpeedTier) + (10f * _cparams.DecodingSpeedTier);
        }

        private void ApplyLosslessDecodingTierOptions()
        {
            void DisableWp()
            {
                _cparams.Options.WpTreeMode = ModularOptions.TreeMode.NoWP;

                if (_cparams.Options.Predictor == Predictor.Weighted)
                {
                    _cparams.Options.Predictor = Predictor.Gradient;
                }
            }

            switch (_cparams.DecodingSpeedTier)
            {
                case 0:
                    _cparams.Options.FastDecodeMultiplier = 1.001f;
                    break;
                case 1:
                    _cparams.Options.FastDecodeMultiplier = 1.005f;
                    DisableWp();
                    break;
                case 2:
                    _cparams.Options.FastDecodeMultiplier = 1.015f;
                    DisableWp();
                    break;
                case 3:
                    _cparams.Options.WpTreeMode = ModularOptions.TreeMode.GradientOnly;
                    _cparams.Options.Predictor  = Predictor.Gradient;
                    break;
                default:
                    _cparams.Options.WpTreeMode = ModularOptions.TreeMode.GradientOnly;
                    _cparams.Options.Predictor  = Predictor.Gradient;
                    _cparams.Options.NbRepeats  = 0.0f;
                    break;
            }
        }

        private void ConfigureSplittingHeuristics(
            FrameHeader frameHeader,
            CompressParams cparamsOrig,
            nuint numStreams)
        {
            List<uint> propOrder = _cparams.Responsive != 0
                ? new List<uint> { 0, 1, 4, 5, 6, 7, 8, 15, 9, 10, 11, 12, 13, 14, 2, 3 }
                : new List<uint> { 0, 1, 15, 9, 10, 11, 12, 13, 14, 2, 3, 4, 5, 6, 7, 8 };

            if (_cparams.Responsive == 0 &&
                numStreams < 30 &&
                _cparams.SpeedTier > SpeedTier.kTortoise &&
                cparamsOrig.ModularPartIsLossless())
            {
                propOrder.RemoveAt(1);
            }

            ImageMetadata? meta = frameHeader.NonserializedMetadata?.M;
            int extraPropsCap   = meta != null
                ? (int)meta.NumExtraChannels + (meta.ColorEncoding.IsGray() ? 0 : 2)
                : 2;
            int maxProperties   = Math.Min(_cparams.Options.MaxProperties, extraPropsCap);

            AssignSpeedTierSplittingProps(propOrder, maxProperties);
            _cparams.Options.NbRepeats = Math.Min(1.0f, _cparams.Options.NbRepeats);
        }

        private void AssignSpeedTierSplittingProps(List<uint> propOrder, int maxProperties)
        {
            int count = _cparams.SpeedTier switch
            {
                SpeedTier.kHare                           => 4,
                SpeedTier.kWombat                         => 5,
                SpeedTier.kSquirrel                       => 7,
                SpeedTier.kKitten                         => 10,
                SpeedTier.kGlacier or SpeedTier.kTortoise => propOrder.Count,
                _                                         => 3,
            };

            _cparams.Options.SplittingHeuristicsProperties.Clear();

            for (int i = 0; i < Math.Min(count, propOrder.Count); i++)
            {
                _cparams.Options.SplittingHeuristicsProperties.Add(propOrder[i]);
            }

            switch (_cparams.SpeedTier)
            {
                case SpeedTier.kHare:
                    _cparams.Options.MaxPropertyValues  = 48;
                    _cparams.Options.NbRepeats         *= 0.5f;
                    break;
                case SpeedTier.kWombat:
                    _cparams.Options.MaxPropertyValues  = 64;
                    _cparams.Options.NbRepeats         *= 0.7f;
                    break;
                case SpeedTier.kSquirrel:
                    _cparams.Options.MaxPropertyValues  = 96;
                    break;
                case SpeedTier.kKitten:
                    _cparams.Options.MaxPropertyValues  = 128;
                    _cparams.Options.NbRepeats         *= 1.1f;
                    break;
                case SpeedTier.kGlacier:
                case SpeedTier.kTortoise:
                    _cparams.Options.MaxPropertyValues  = 256;
                    _cparams.Options.NbRepeats         *= 1.3f;
                    break;
                default:
                    _cparams.Options.MaxPropertyValues  = 32;
                    _cparams.Options.NbRepeats         *= 0.3f;
                    break;
            }

            if (_cparams.SpeedTier > SpeedTier.kTortoise)
            {
                for (int i = 0; i < maxProperties; i++)
                {
                    _cparams.Options.SplittingHeuristicsProperties.Add(
                        (uint)(ModularPredictors.KNumNonrefProperties + (i * 4) + 3));
                }
            }
            else
            {
                for (int i = 0; i < maxProperties * 4; i++)
                {
                    _cparams.Options.SplittingHeuristicsProperties.Add(
                        (uint)(ModularPredictors.KNumNonrefProperties + i));
                }
            }
        }

        private void ConfigureDefaultPredictor()
        {
            Predictor p = _cparams.Options.Predictor;

            if ((p == Predictor.Average0 ||
                 p == Predictor.Average1 ||
                 p == Predictor.Average2 ||
                 p == Predictor.Average3 ||
                 p == Predictor.Average4 ||
                 p == Predictor.Weighted) &&
                !_cparams.ModularPartIsLossless())
            {
                _cparams.Options.Predictor = Predictor.Undefined;
            }

            if (_cparams.Options.Predictor == Predictor.Undefined)
            {
                if ((_cparams.SpeedTier <= SpeedTier.kGlacier || !_cparams.ModularMode) &&
                    _cparams.IsLossless() &&
                    _cparams.Responsive == 0)
                {
                    _cparams.Options.Predictor = Predictor.Variable;
                }
                else if (_cparams.Responsive != 0 || _cparams.LossyPalette)
                {
                    _cparams.Options.Predictor = Predictor.Zero;
                }
                else if (!_cparams.IsLossless())
                {
                    _cparams.Options.Predictor = Predictor.Gradient;
                }
                else if (_cparams.SpeedTier < SpeedTier.kFalcon)
                {
                    _cparams.Options.Predictor = Predictor.Best;
                }
                else if (_cparams.SpeedTier == SpeedTier.kFalcon)
                {
                    _cparams.Options.Predictor = Predictor.Weighted;
                }
                else
                {
                    _cparams.Options.Predictor = Predictor.Gradient;
                }
            }
            else if (_cparams.LossyPalette)
            {
                _cparams.Options.Predictor = Predictor.Zero;
            }

            if (!_cparams.ModularPartIsLossless() &&
                (_cparams.Options.Predictor == Predictor.Weighted ||
                 _cparams.Options.Predictor == Predictor.Variable ||
                 _cparams.Options.Predictor == Predictor.Best))
            {
                _cparams.Options.Predictor = Predictor.Zero;
            }
        }

        private Status InitializeTreeSplits(nuint numStreams)
        {
            _treeSplits.Clear();
            _treeSplits.Add(0);

            if (!_cparams.ModularMode)
            {
                StatusOr<ModularStreamId> qt0Or = ModularStreamId.QuantTable(0);

                if (!qt0Or.IsOk)
                {
                    return qt0Or.Status;
                }

                _cparams.Options.FastDecodeMultiplier = 1.0f;
                _treeSplits.Add(ModularStreamId.VarDCTDC(0).ID(_frameDim));
                _treeSplits.Add(ModularStreamId.ModularDC(0).ID(_frameDim));
                _treeSplits.Add(ModularStreamId.ACMetadata(0).ID(_frameDim));
                _treeSplits.Add(qt0Or.Value.ID(_frameDim));
                _treeSplits.Add(ModularStreamId.ModularAC(0, 0).ID(_frameDim));

                AcMetadataSize.Clear();
                ExtraDcPrecision.Clear();

                for (nuint i = 0; i < _frameDim.NumDcGroups; i++)
                {
                    AcMetadataSize.Add(0);
                    ExtraDcPrecision.Add(0);
                }
            }

            _treeSplits.Add(numStreams);

            return true;
        }

        private Status PreprocessColorFeatures(ModularEncodingDataArgs args, bool groupwise)
        {
            if (args.DoColor && args.Color != null && args.FrameHeader.LoopFilter.Gab && !groupwise)
            {
                const float w       = 0.9908511000000001f;
                Span<float> weights = stackalloc float[3] { w, w, w };
                Status gabOk        = EncGaborish.GaborishInverse(args.Color, new Rect(args.Color), weights, args.Pool);

                if (!gabOk.IsOk)
                {
                    return gabOk;
                }
            }

            if (args.DoColor &&
                args.Color != null &&
                args.Metadata.BitDepth.BitsPerSample <= 16 &&
                _cparams.SpeedTier < SpeedTier.kCheetah &&
                _cparams.DecodingSpeedTier < 2 &&
                !groupwise)
            {
                Status patchOk = PatchDictionaryEncoder.FindBestPatchDictionary(
                    args.Color,
                    args.EncState,
                    args.Cms,
                    null,
                    args.AuxOut,
                    _cparams.ColorTransform == ColorTransform.XYB);

                if (!patchOk.IsOk)
                {
                    return patchOk;
                }

                Status subOk = PatchDictionaryEncoder.SubtractFrom(
                    args.EncState.Shared.ImageFeatures.Patches,
                    args.Color);

                if (!subOk.IsOk)
                {
                    return subOk;
                }
            }

            if (_cparams.CustomSplines.HasAny)
            {
                args.EncState.Shared.ImageFeatures.Splines.SetData(_cparams.CustomSplines);
            }

            return true;
        }

        private Status ConvertInputToGlobalModularImage(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            Image3<float>? color,
            IReadOnlyList<ImagePlane<float>> extraChannels,
            Rect groupRect,
            FrameDimensions patchDim,
            PassesEncoderState encState,
            ThreadPool? pool,
            bool doColor,
            out int maxBitdepth,
            out int maxval)
        {
            maxBitdepth     = 0;
            maxval          = 0;
            nuint xsize     = patchDim.XSize;
            nuint ysize     = patchDim.YSize;
            int nbChans     = !doColor
                ? 0
                : (metadata.ColorEncoding.IsGray() && _cparams.ColorTransform == ColorTransform.None ? 1 : 3);
            nbChans        += extraChannels.Count;
            bool fp         = metadata.BitDepth.FloatingPointSample && _cparams.ColorTransform != ColorTransform.XYB;

            if (metadata.BitDepth.BitsPerSample >= 32 && doColor && _cparams.ColorTransform != ColorTransform.XYB)
            {
                if (!fp || metadata.BitDepth.BitsPerSample > 32)
                {
                    return Status.Failure("Unsupported bit_depth in enc_modular");
                }
            }

            maxBitdepth                  = doColor ? (int)metadata.BitDepth.BitsPerSample + (fp ? 0 : 1) : 0;
            StatusOr<ModularImage> giOr  = ModularImage.Create(
                encState.MemoryManager,
                xsize,
                ysize,
                (int)metadata.BitDepth.BitsPerSample,
                nbChans);

            if (!giOr.IsOk)
            {
                return giOr.Status;
            }

            _streamImages[0].Dispose();
            _streamImages[0] = giOr.Value;
            ModularImage gi  = _streamImages[0];

            Status xybOk = ConfigureXybModularFactors(encState, ref maxBitdepth);

            if (!xybOk.IsOk)
            {
                return xybOk;
            }

            maxval = gi.BitDepth < 32 ? (int)((1u << gi.BitDepth) - 1u) : 0;
            int c  = 0;

            if (doColor && color != null)
            {
                Status colorOk = ConvertColorPlanesToModular(
                    frameHeader,
                    metadata,
                    color,
                    groupRect,
                    encState,
                    pool,
                    gi,
                    fp,
                    maxval,
                    ref c);

                if (!colorOk.IsOk)
                {
                    return colorOk;
                }
            }

            return ConvertExtraChannelsToModular(
                frameHeader,
                metadata,
                extraChannels,
                groupRect,
                patchDim,
                pool,
                gi,
                ref c,
                ref maxBitdepth);
        }

        private Status ConfigureXybModularFactors(PassesEncoderState encState, ref int maxBitdepth)
        {
            if (_cparams.ColorTransform != ColorTransform.XYB || !_cparams.ModularMode)
            {
                return true;
            }

            Span<float> encFactors = stackalloc float[3] { 65536.0f, 4096.0f, 4096.0f };

            if (_cparams.ButteraugliDistance > 0 && _cparams.Responsive == 0)
            {
                encFactors[0]               *= 1.0f / (1.0f + (23.0f * _cparams.ButteraugliDistance));
                encFactors[1]               *= 1.0f / (1.0f + (14.0f * _cparams.ButteraugliDistance));
                encFactors[2]               *= 1.0f / (1.0f + (14.0f * _cparams.ButteraugliDistance));
                _cparams.ButteraugliDistance = 0.0f;
            }

            if (_cparams.ManualXybFactors.Count == 3)
            {
                Span<float> manual = stackalloc float[3]
                {
                    _cparams.ManualXybFactors[0],
                    _cparams.ManualXybFactors[1],
                    _cparams.ManualXybFactors[2],
                };

                return EncQuantWeights.DequantMatricesSetCustomDC(
                    encState.MemoryManager,
                    encState.Shared.Matrices,
                    manual);
            }

            maxBitdepth = 12;

            return EncQuantWeights.DequantMatricesSetCustomDC(
                encState.MemoryManager,
                encState.Shared.Matrices,
                encFactors);
        }

        private Status ConvertColorPlanesToModular(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            Image3<float> color,
            Rect groupRect,
            PassesEncoderState encState,
            ThreadPool? pool,
            ModularImage gi,
            bool fp,
            int maxval,
            ref int c)
        {
            bool isGrayNone = metadata.ColorEncoding.IsGray() && _cparams.ColorTransform == ColorTransform.None;
            nuint xsize     = gi.W;
            nuint ysize     = gi.H;

            for (; c < 3; c++)
            {
                if (isGrayNone && c != (_cparams.ColorTransform == ColorTransform.XYB ? 1 : 0))
                {
                    continue;
                }

                int cOut      = (_cparams.ColorTransform == ColorTransform.XYB && c < 2) ? 1 - c : c;
                double factor = _cparams.ColorTransform == ColorTransform.XYB
                    ? encState.Shared.Matrices.InvDCQuant((nuint)c)
                    : maxval;

                if (c == 2 && _cparams.ColorTransform == ColorTransform.XYB)
                {
                    ConvertXybBluePlane(color, gi, cOut, xsize, ysize, (float)factor);
                }
                else
                {
                    Status chOk = ConvertSingleColorPlane(
                        frameHeader,
                        metadata,
                        color,
                        groupRect,
                        pool,
                        gi.Channels[cOut],
                        (nuint)c,
                        xsize,
                        ysize,
                        fp,
                        factor);

                    if (!chOk.IsOk)
                    {
                        return chOk;
                    }
                }
            }

            if (isGrayNone)
            {
                c = 1;
            }

            return true;
        }

        private static void ConvertXybBluePlane(
            Image3<float> color,
            ModularImage gi,
            int cOut,
            nuint xsize,
            nuint ysize,
            float factor)
        {
            for (nuint y = 0; y < ysize; y++)
            {
                float* rowIn      = color.ConstPlaneRow(2, y);
                Span<int> rowOut  = gi.Channels[cOut].Row(y);
                ReadOnlySpan<int> rowY = gi.Channels[0].ConstRow(y);

                for (int x = 0; x < (int)xsize; x++)
                {
                    int val   = (int)((rowIn[x] * factor) + 0.5f);
                    rowOut[x] = val - rowY[x];
                }
            }
        }

        private static Status ConvertSingleColorPlane(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            Image3<float> color,
            Rect groupRect,
            ThreadPool? pool,
            Channel chOut,
            nuint c,
            nuint xsize,
            nuint ysize,
            bool fp,
            double factor)
        {
            uint bits          = metadata.BitDepth.BitsPerSample;
            uint expBits       = metadata.BitDepth.ExponentBitsPerSample;
            chOut.HShift       = (int)frameHeader.ChromaSubsampling.HShift(c);
            chOut.VShift       = (int)frameHeader.ChromaSubsampling.VShift(c);
            nuint xsizeShifted = Common.DivCeil(xsize, 1u << chOut.HShift);
            nuint ysizeShifted = Common.DivCeil(ysize, 1u << chOut.VShift);

            Status shrinkOk = chOut.Shrink(xsizeShifted, ysizeShifted);

            if (!shrinkOk.IsOk)
            {
                return shrinkOk;
            }

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysizeShifted,
                ThreadPool.NoInit,
                (uint y, nuint _) =>
                {
                    float* rowIn = color.ConstPlaneRow(c, y + (nuint)groupRect.Y0) + groupRect.X0;
                    int* rowOut  = chOut.Plane.Row(y);

                    return FloatToInt(rowIn, rowOut, xsizeShifted, bits, expBits, fp, factor);
                },
                "float2int");
        }

        private static Status ConvertExtraChannelsToModular(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            IReadOnlyList<ImagePlane<float>> extraChannels,
            Rect groupRect,
            FrameDimensions patchDim,
            ThreadPool? pool,
            ModularImage gi,
            ref int c,
            ref int maxBitdepth)
        {
            for (int ec = 0; ec < extraChannels.Count; ec++, c++)
            {
                ExtraChannelInfo eci = metadata.ExtraChannelInfo[ec];
                nuint ecups          = frameHeader.ExtraChannelUpsampling[ec];
                Channel chOut        = gi.Channels[c];

                Status shrinkOk = chOut.Shrink(
                    Common.DivCeil(patchDim.XSizeUpsampled, ecups),
                    Common.DivCeil(patchDim.YSizeUpsampled, ecups));

                if (!shrinkOk.IsOk)
                {
                    return shrinkOk;
                }

                int shift    = (int)Bits.CeilLog2Nonzero(ecups) - (int)Bits.CeilLog2Nonzero(frameHeader.Upsampling);
                chOut.HShift = shift;
                chOut.VShift = shift;

                uint bits     = eci.BitDepth.BitsPerSample;
                uint expBits  = eci.BitDepth.ExponentBitsPerSample;
                bool ecFp     = eci.BitDepth.FloatingPointSample;
                double factor = ecFp ? 1.0 : ((1u << (int)bits) - 1u);
                int chDepth   = (int)bits + (ecFp ? 0 : 1);

                if (chDepth > maxBitdepth)
                {
                    maxBitdepth = chDepth;
                }

                ImagePlane<float> ecPlane = extraChannels[ec];
                Status poolOk             = DataParallel.RunOnPool(
                    pool,
                    0,
                    (uint)chOut.Plane.YSize,
                    ThreadPool.NoInit,
                    (uint y, nuint _) =>
                    {
                        float* rowIn = ecPlane.ConstRow(y + (nuint)groupRect.Y0) + groupRect.X0;
                        int* rowOut  = chOut.Plane.Row(y);

                        return FloatToInt(rowIn, rowOut, chOut.Plane.XSize, bits, expBits, ecFp, factor);
                    },
                    "float2int");

                if (!poolOk.IsOk)
                {
                    return poolOk;
                }
            }

            return true;
        }

        private Status ApplyGlobalTransformsAndQuantization(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            int numExtraChannels,
            bool doColor,
            bool groupwise,
            ThreadPool? pool,
            ref int maxBitdepth,
            ref int maxval)
        {
            int levelMaxBitdepth = _cparams.Level == 5 ? 16 : 32;

            if (maxBitdepth > levelMaxBitdepth)
            {
                return Status.Failure("Bitdepth too high for codestream level");
            }

            ModularImage gi = _streamImages[0];

            if (!_cparams.ModularPartIsLossless())
            {
                if (_cparams.ColorTransform == ColorTransform.XYB)
                {
                    _cparams.ChannelColorsPreTransformPercent = 0.0f;
                }

                _cparams.ChannelColorsPercent = 0.0f;
                _cparams.PaletteColors        = 0;
                _cparams.LossyPalette         = false;
            }

            float channelColorsPercent = 0.0f;

            if (!_cparams.LossyPalette &&
                (_cparams.SpeedTier <= SpeedTier.kThunder ||
                 (doColor && metadata.BitDepth.BitsPerSample > 8)))
            {
                channelColorsPercent = _cparams.ChannelColorsPreTransformPercent;
            }

            if (!groupwise && (_cparams.LossyPalette || !(_cparams.Responsive != 0 && _cparams.ModularPartIsLossless())))
            {
                Status palOk = TryPalettes(gi, ref maxBitdepth, ref maxval, _cparams, channelColorsPercent, pool);

                if (!palOk.IsOk)
                {
                    return palOk;
                }
            }

            ApplyGlobalRctAndSqueeze(frameHeader, gi, doColor, groupwise, levelMaxBitdepth, pool, ref maxBitdepth);

            if (maxBitdepth + 1 > levelMaxBitdepth)
            {
                _cparams.Colorspace = 0;
            }

            if (!_cparams.ModularPartIsLossless())
            {
                return QuantizeLossyModularChannels(metadata, numExtraChannels, doColor, maxval, gi);
            }

            return true;
        }

        private void ApplyGlobalRctAndSqueeze(
            FrameHeader frameHeader,
            ModularImage gi,
            bool doColor,
            bool groupwise,
            int levelMaxBitdepth,
            ThreadPool? pool,
            ref int maxBitdepth)
        {
            if (_cparams.ColorTransform == ColorTransform.None &&
                doColor &&
                gi.Channels.Count - (int)gi.NbMetaChannels >= 3 &&
                maxBitdepth + 1 < levelMaxBitdepth)
            {
                if (_cparams.Colorspace < 0 &&
                    (!_cparams.ModularPartIsLossless() || _cparams.SpeedTier > SpeedTier.kHare))
                {
                    Transform ycocg = new Transform(TransformId.RCT)
                    {
                        RctType = 6,
                        BeginC  = (uint)gi.NbMetaChannels,
                    };
                    DoTransform(gi, ycocg, new WeightedHeader(), pool);
                    maxBitdepth++;
                }
                else if (_cparams.Colorspace > 0)
                {
                    Transform sg = new Transform(TransformId.RCT)
                    {
                        BeginC  = (uint)gi.NbMetaChannels,
                        RctType = (uint)_cparams.Colorspace,
                    };
                    DoTransform(gi, sg, new WeightedHeader(), pool);
                    maxBitdepth++;
                }
            }

            if (!groupwise &&
                _cparams.Responsive != 0 &&
                gi.Channels.Count > 0 &&
                maxBitdepth + 2 < levelMaxBitdepth)
            {
                ApplyBoundedGlobalSqueeze(frameHeader, gi, pool);
                maxBitdepth += 2;
            }
        }

        private static void ApplyBoundedGlobalSqueeze(FrameHeader frameHeader, ModularImage gi, ThreadPool? pool)
        {
            Transform t               = new Transform(TransformId.Squeeze);
            List<SqueezeParams> sqs   = new List<SqueezeParams>();
            EncSqueeze.DefaultSqueezeParameters(sqs, gi);

            nuint shiftCap = 7u + frameHeader.GroupSizeShift;
            nuint hshift   = 0;
            nuint vshift   = 0;

            for (int i = 0; i < sqs.Count; i++)
            {
                if (sqs[i].Horizontal)
                {
                    hshift++;
                }
                else
                {
                    vshift++;
                }

                nuint dcBoost = Math.Min(hshift, vshift) >= 3 ? 3u : 0u;

                if (Math.Max(hshift, vshift) > shiftCap + dcBoost)
                {
                    sqs.RemoveRange(Math.Max(0, i - 1), sqs.Count - Math.Max(0, i - 1));
                    t.Squeezes = sqs;
                    break;
                }
            }

            DoTransform(gi, t, new WeightedHeader(), pool);
        }

        private Status QuantizeLossyModularChannels(
            ImageMetadata metadata,
            int numExtraChannels,
            bool doColor,
            int maxval,
            ModularImage gi)
        {
            if (_cparams.Options.NbRepeats == 0)
            {
                return Status.Failure("nb_repeats = 0 not supported with modular lossy!");
            }

            _quants.Clear();

            for (int i = 0; i < gi.Channels.Count; i++)
            {
                _quants.Add(1);
            }

            float quantizer          = _cparams.Responsive != 0 ? 0.25f : 0.025f;
            float bitdepthCorrection = _cparams.ColorTransform != ColorTransform.XYB ? maxval / 255.0f : 1.0f;
            List<float> quantizers   = new List<float>();

            for (int i = 0; i < 3; i++)
            {
                float dist = _cparams.ButteraugliDistance;
                quantizers.Add(quantizer * MathF.Pow(dist, 1.2f) * bitdepthCorrection);
            }

            for (int i = 0; i < numExtraChannels; i++)
            {
                int ecBitdepth     = (int)metadata.ExtraChannelInfo[i].BitDepth.BitsPerSample;
                int ecMaxval       = ecBitdepth < 32 ? (int)((1u << ecBitdepth) - 1u) : 0;
                bitdepthCorrection = ecMaxval / 255.0f;
                float dist         = i < _cparams.EcDistance.Count ? _cparams.EcDistance[i] : 0.0f;

                if (dist < 0)
                {
                    dist = _cparams.ButteraugliDistance;
                }

                quantizers.Add(quantizer * dist * bitdepthCorrection);
            }

            for (int i = (int)gi.NbMetaChannels; i < gi.Channels.Count; i++)
            {
                Channel ch    = gi.Channels[i];
                int shift     = Math.Min(ch.HShift + ch.VShift, 16);
                shift         = shift > 0 ? shift - 1 : 0;
                int component = (doColor ? 0 : 3) + ch.Component;
                int q         = ComputeChannelQuant(component, shift, quantizers);
                QuantizeChannel(ch, q);
                _quants[i]    = (uint)q;
            }

            return true;
        }

        private int ComputeChannelQuant(int component, int shift, List<float> quantizers)
        {
            float qf;

            if (_cparams.ColorTransform == ColorTransform.XYB && component < 3)
            {
                qf = quantizers[component] * KSqueezeQualityXyb * SqueezeXybQTable[component][shift];

                if (component == 0)
                {
                    qf *= KSqueezeQualityY;
                }
            }
            else if (_cparams.Colorspace != 0 && component > 0 && component < 3)
            {
                qf = quantizers[component] * KSqueezeQualityFactor * SqueezeChromaQTable[shift];
            }
            else
            {
                qf = quantizers[component] * KSqueezeQualityFactor * KSqueezeLumaFactor * SqueezeLumaQTable[shift];
            }

            return Math.Max(1, (int)qf);
        }

        private Status PopulateAndPrepareGroups(
            FrameHeader frameHeader,
            FrameDimensions patchDim,
            Rect frameAreaRect,
            PassesEncoderState encState,
            bool doColor,
            bool groupwise,
            ThreadPool? pool)
        {
            for (nuint groupId = 0; groupId < patchDim.NumDcGroups; groupId++)
            {
                nuint rgx        = groupId % patchDim.XSizeDcGroups;
                nuint rgy        = groupId / patchDim.XSizeDcGroups;
                Rect rect        = new Rect(
                    (int)(rgx * patchDim.DcGroupDim),
                    (int)(rgy * patchDim.DcGroupDim),
                    (int)patchDim.DcGroupDim,
                    (int)patchDim.DcGroupDim);
                nuint gx          = rgx + ((nuint)frameAreaRect.X0 / 2048u);
                nuint gy          = rgy + ((nuint)frameAreaRect.Y0 / 2048u);
                nuint realGroupId = (gy * _frameDim.XSizeDcGroups) + gx;

                _streamParams.Add(new GroupParams
                {
                    Rect     = rect,
                    MinShift = 3,
                    MaxShift = 1000,
                    Id       = ModularStreamId.ModularDC(realGroupId),
                });
            }

            for (nuint groupId = 0; groupId < patchDim.NumGroups; groupId++)
            {
                nuint rgx        = groupId % patchDim.XSizeGroups;
                nuint rgy        = groupId / patchDim.XSizeGroups;
                Rect mrect       = new Rect(
                    (int)(rgx * patchDim.GroupDim),
                    (int)(rgy * patchDim.GroupDim),
                    (int)patchDim.GroupDim,
                    (int)patchDim.GroupDim);
                nuint gx          = rgx + ((nuint)frameAreaRect.X0 / _frameDim.GroupDim);
                nuint gy          = rgy + ((nuint)frameAreaRect.Y0 / _frameDim.GroupDim);
                nuint realGroupId = (gy * _frameDim.XSizeGroups) + gx;

                for (nuint i = 0; i < encState.ProgressiveSplitter.GetNumPasses(); i++)
                {
                    frameHeader.Passes.GetDownsamplingBracket(i, out int minShift, out int maxShift);
                    _streamParams.Add(new GroupParams
                    {
                        Rect     = mrect,
                        MinShift = minShift,
                        MaxShift = maxShift,
                        Id       = ModularStreamId.ModularAC(realGroupId, i),
                    });
                }
            }

            if (_streamParams.Count == 2)
            {
                _streamParams.Add(new GroupParams
                {
                    Rect     = new Rect(0, 0, (int)patchDim.XSize, (int)patchDim.YSize),
                    MinShift = 0,
                    MaxShift = 1000,
                    Id       = ModularStreamId.Global(),
                });
            }

            return ExecuteStreamParamsPreparation(doColor, groupwise, pool);
        }

        private Status ExecuteStreamParamsPreparation(bool doColor, bool groupwise, ThreadPool? pool)
        {
            while (_giChannel.Count < _streamImages.Count)
            {
                _giChannel.Add(new List<uint>());
            }

            Status prepOk = DataParallel.RunOnPool(
                pool,
                0,
                (uint)_streamParams.Count,
                ThreadPool.NoInit,
                (uint i, nuint _) =>
                {
                    GroupParams gp = _streamParams[(int)i];
                    int stream     = (int)gp.Id.ID(_frameDim);

                    if (stream != 0)
                    {
                        _streamOptions[stream] = _streamOptions[0].Clone();
                    }

                    return PrepareStreamParams(gp.Rect, _cparams, gp.MinShift, gp.MaxShift, gp.Id, doColor, groupwise);
                },
                "ChooseParams");

            if (!prepOk.IsOk)
            {
                return prepOk;
            }

            ModularImage fullImage = _streamImages[0];
            int ch                 = (int)fullImage.NbMetaChannels;

            for (; ch < fullImage.Channels.Count; ch++)
            {
                Channel fc = fullImage.Channels[ch];

                if (fc.W > _frameDim.GroupDim || fc.H > _frameDim.GroupDim)
                {
                    break;
                }
            }

            for (; ch < fullImage.Channels.Count; ch++)
            {
                fullImage.Channels[ch].Plane.Dispose();
                fullImage.Channels[ch].Plane = new ImagePlane<int>();
            }

            return ModularEncoding.ValidateChannelDimensions(fullImage, _streamOptions[0]);
        }

        private Status PrepareStreamParams(
            Rect rect,
            CompressParams cparams,
            int minShift,
            int maxShift,
            ModularStreamId stream,
            bool doColor,
            bool groupwise)
        {
            int streamId = (int)stream.ID(_frameDim);

            if (streamId == 0 && _frameDim.NumGroups != 1)
            {
                return true;
            }

            if (streamId > 0)
            {
                Status copyOk = ExtractGroupChannels(rect, cparams, minShift, maxShift, streamId, groupwise);

                if (!copyOk.IsOk || _streamImages[streamId].Channels.Count == 0)
                {
                    return copyOk;
                }
            }

            ModularImage gi = _streamImages[streamId];

            if (cparams.ColorTransform == ColorTransform.None &&
                cparams.IsLossless() &&
                cparams.Colorspace < 0 &&
                gi.Channels.Count - (int)gi.NbMetaChannels >= 3 &&
                cparams.Responsive == 0 &&
                doColor &&
                cparams.SpeedTier <= SpeedTier.kHare)
            {
                Status rctOk = SelectBestGroupRct(gi, cparams.SpeedTier);

                if (!rctOk.IsOk)
                {
                    return rctOk;
                }
            }

            SelectBestGroupWpMode(gi, streamId, cparams.SpeedTier);

            return true;
        }

        private Status ExtractGroupChannels(
            Rect rect,
            CompressParams cparams,
            int minShift,
            int maxShift,
            int streamId,
            bool groupwise)
        {
            ModularImage fullImage       = _streamImages[0];
            JxlMemoryManager? mm         = fullImage.MemoryManager;
            StatusOr<ModularImage> giOr  = ModularImage.Create(mm, (nuint)rect.XSize, (nuint)rect.YSize, fullImage.BitDepth, 0);

            if (!giOr.IsOk)
            {
                return giOr.Status;
            }

            _streamImages[streamId].Dispose();
            _streamImages[streamId] = giOr.Value;
            ModularImage gi         = _streamImages[streamId];

            int c = (int)fullImage.NbMetaChannels;

            if (!groupwise)
            {
                for (; c < fullImage.Channels.Count; c++)
                {
                    Channel fc = fullImage.Channels[c];

                    if (fc.W > _frameDim.GroupDim || fc.H > _frameDim.GroupDim)
                    {
                        break;
                    }
                }
            }

            for (; c < fullImage.Channels.Count; c++)
            {
                Channel fc = fullImage.Channels[c];
                int shift  = Math.Min(fc.HShift, fc.VShift);

                if (shift > maxShift || shift < minShift)
                {
                    continue;
                }

                Rect r = new Rect(
                    rect.X0 >> fc.HShift,
                    rect.Y0 >> fc.VShift,
                    rect.XSize >> fc.HShift,
                    rect.YSize >> fc.VShift,
                    (int)fc.W,
                    (int)fc.H);

                if (r.XSize == 0 || r.YSize == 0)
                {
                    continue;
                }

                _giChannel[streamId].Add((uint)c);
                StatusOr<Channel> gcOr = Channel.Create(mm, (nuint)r.XSize, (nuint)r.YSize, fc.HShift, fc.VShift);

                if (!gcOr.IsOk)
                {
                    return gcOr.Status;
                }

                Channel gc = gcOr.Value;

                for (nuint y = 0; y < (nuint)r.YSize; y++)
                {
                    new ReadOnlySpan<int>(r.ConstRow(fc.Plane, y), r.XSize).CopyTo(gc.Row(y));
                }

                gi.Channels.Add(gc);
            }

            if (gi.Channels.Count > 0 &&
                _cparams.ModularPartIsLossless() &&
                cparams.Responsive == 0 &&
                !cparams.LossyPalette &&
                cparams.SpeedTier < SpeedTier.kCheetah)
            {
                int dummyDepth = 0;
                int dummyMax   = 0;

                return TryPalettes(gi, ref dummyDepth, ref dummyMax, cparams, cparams.ChannelColorsPercent, null);
            }

            return true;
        }

        private static Status SelectBestGroupRct(ModularImage gi, SpeedTier speedTier)
        {
            int nbRctsToTry = speedTier switch
            {
                SpeedTier.kHare     => 4,
                SpeedTier.kWombat   => 5,
                SpeedTier.kSquirrel => 7,
                SpeedTier.kKitten   => 9,
                SpeedTier.kTectonicPlate or SpeedTier.kGlacier or SpeedTier.kTortoise => 19,
                _                   => 0,
            };

            if (nbRctsToTry <= 1)
            {
                return true;
            }

            StatusOr<float> baseCostOr = EncModularEncoding.EstimateCost(gi);

            if (!baseCostOr.IsOk)
            {
                return baseCostOr.Status;
            }

            float bestCost     = baseCostOr.Value;
            uint bestRct       = 0;
            List<Channel> orig = new List<Channel>(3);
            int meta           = (int)gi.NbMetaChannels;

            for (int c = 0; c < 3; c++)
            {
                Channel genuine        = gi.Channels[meta + c];
                StatusOr<Channel> chOr = Channel.Create(genuine.MemoryManager, genuine.W, genuine.H, genuine.HShift, genuine.VShift);

                if (!chOr.IsOk)
                {
                    return chOr.Status;
                }

                Channel copy = chOr.Value;
                genuine.Plane.Swap(copy.Plane);
                orig.Add(copy);
            }

            Channel[] outChans = { gi.Channels[meta], gi.Channels[meta + 1], gi.Channels[meta + 2] };

            for (int idx = 1; idx < nbRctsToTry && idx < RctCandidates.Length; idx++)
            {
                uint rctType = (uint)RctCandidates[idx];
                Status rctOk = EncRct.FwdRct(orig, outChans, rctType, null);

                if (!rctOk.IsOk)
                {
                    return rctOk;
                }

                StatusOr<float> costOr = EncModularEncoding.EstimateCost(gi);

                if (costOr.IsOk && costOr.Value < bestCost)
                {
                    bestCost = costOr.Value;
                    bestRct  = rctType;
                }
            }

            for (int c = 0; c < 3; c++)
            {
                gi.Channels[meta + c].Plane.Swap(orig[c].Plane);
                orig[c].Dispose();
            }

            if (bestRct != 0)
            {
                Transform sg = new Transform(TransformId.RCT)
                {
                    BeginC  = (uint)meta,
                    RctType = bestRct,
                };
                DoTransform(gi, sg, new WeightedHeader(), null);
            }

            return true;
        }

        private void SelectBestGroupWpMode(ModularImage gi, int streamId, SpeedTier speedTier)
        {
            int nbWpModes = speedTier <= SpeedTier.kTortoise ? 5 : (speedTier <= SpeedTier.kKitten ? 2 : 1);

            if (nbWpModes <= 1 || !ModularPredictors.PredictorHasWeighted(_streamOptions[streamId].Predictor))
            {
                return;
            }

            float bestCost                   = float.MaxValue;
            _streamOptions[streamId].WpMode = 0;

            for (int i = 0; i < nbWpModes; i++)
            {
                float cost = EstimateWPCost(gi, i);

                if (cost < bestCost)
                {
                    bestCost                         = cost;
                    _streamOptions[streamId].WpMode = i;
                }
            }
        }

        private List<ModularMultiplierInfo> BuildMultiplierInfo()
        {
            List<ModularMultiplierInfo> multiplierInfo = new List<ModularMultiplierInfo>();

            if (_quants.Count == 0)
            {
                return multiplierInfo;
            }

            for (uint streamId = 0; streamId < (uint)_streamImages.Count; streamId++)
            {
                if (streamId > 0 && _giChannel[(int)streamId].Count == 0)
                {
                    continue;
                }

                ModularImage image     = _streamImages[(int)streamId];
                ModularOptions options = _streamOptions[(int)streamId];

                for (uint i = (uint)image.NbMetaChannels; i < (uint)image.Channels.Count; i++)
                {
                    Channel ch = image.Channels[(int)i];

                    if (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize)
                    {
                        continue;
                    }

                    int chId = streamId == 0
                        ? (int)i
                        : (int)_giChannel[(int)streamId][(int)(i - image.NbMetaChannels)];
                    uint q   = _quants[chId];

                    if (multiplierInfo.Count == 0 ||
                        multiplierInfo[multiplierInfo.Count - 1].Range[1, 0] != streamId ||
                        multiplierInfo[multiplierInfo.Count - 1].Multiplier != q)
                    {
                        StaticPropRange range = default;
                        range[0]              = (i, i + 1u);
                        range[1]              = (streamId, streamId + 1u);
                        multiplierInfo.Add(new ModularMultiplierInfo { Range = range, Multiplier = q });
                    }
                    else
                    {
                        ModularMultiplierInfo last = multiplierInfo[multiplierInfo.Count - 1];
                        last.Range[0, 1]           = i + 1u;
                        multiplierInfo[multiplierInfo.Count - 1] = last;
                    }
                }
            }

            return CompactMultiplierInfo(multiplierInfo);
        }

        private static List<ModularMultiplierInfo> CompactMultiplierInfo(List<ModularMultiplierInfo> multiplierInfo)
        {
            if (multiplierInfo.Count <= 1)
            {
                return multiplierInfo;
            }

            multiplierInfo.Sort((a, b) =>
            {
                int cmp = a.Range.CompareTo(b.Range);

                return cmp != 0 ? cmp : a.Multiplier.CompareTo(b.Multiplier);
            });

            int newNum = 1;

            for (int i = 1; i < multiplierInfo.Count; i++)
            {
                ModularMultiplierInfo prev = multiplierInfo[newNum - 1];
                ModularMultiplierInfo cur  = multiplierInfo[i];

                if (prev.Range[0] == cur.Range[0] &&
                    prev.Multiplier == cur.Multiplier &&
                    prev.Range[1, 1] == cur.Range[1, 0])
                {
                    prev.Range[1, 1]           = cur.Range[1, 1];
                    multiplierInfo[newNum - 1] = prev;
                }
                else
                {
                    multiplierInfo[newNum++] = cur;
                }
            }

            multiplierInfo.RemoveRange(newNum, multiplierInfo.Count - newNum);

            return multiplierInfo;
        }

        private Status LearnMergedStreamTrees(List<ModularMultiplierInfo> multiplierInfo, ThreadPool? pool)
        {
            List<nuint> usefulSplits = new List<nuint>();

            for (int chunk = 0; chunk < _treeSplits.Count - 1; chunk++)
            {
                bool hasPixels = false;
                nuint start    = _treeSplits[chunk];
                nuint stop     = _treeSplits[chunk + 1];

                for (nuint i = start; i < stop; i++)
                {
                    if (!_streamImages[(int)i].Empty)
                    {
                        hasPixels = true;
                        break;
                    }
                }

                if (hasPixels)
                {
                    usefulSplits.Add(start);
                }
            }

            if (usefulSplits.Count == 0)
            {
                return true;
            }

            usefulSplits.Add(_treeSplits[_treeSplits.Count - 1]);
            List<PropertyDecisionNode>[] trees = new List<PropertyDecisionNode>[usefulSplits.Count - 1];

            Status poolOk = DataParallel.RunOnPool(
                pool,
                0,
                (uint)(usefulSplits.Count - 1),
                ThreadPool.NoInit,
                (uint chunk, nuint _) => LearnSingleChunkTree(usefulSplits, multiplierInfo, trees, (int)chunk),
                "LearnTrees");

            if (!poolOk.IsOk)
            {
                return poolOk;
            }

            _tree.Clear();

            return MergeTrees(trees, usefulSplits, 0, usefulSplits.Count - 1, _tree);
        }

        private Status LearnSingleChunkTree(
            List<nuint> usefulSplits,
            List<ModularMultiplierInfo> multiplierInfo,
            List<PropertyDecisionNode>[] trees,
            int chunk)
        {
            uint start = (uint)usefulSplits[chunk];
            uint stop  = (uint)usefulSplits[chunk + 1];

            while (start < stop && _streamImages[(int)start].Empty)
            {
                start++;
            }

            while (start < stop && _streamImages[(int)stop - 1].Empty)
            {
                stop--;
            }

            if (_streamOptions[(int)start].Kind == ModularOptions.TreeKind.Learn)
            {
                StatusOr<List<PropertyDecisionNode>> learnedOr = EncModularEncoding.LearnTree(
                    _streamImages,
                    _streamOptions,
                    start,
                    stop,
                    multiplierInfo);

                if (!learnedOr.IsOk)
                {
                    return learnedOr.Status;
                }

                trees[chunk] = learnedOr.Value;

                return true;
            }

            nuint totalPixels = 0;

            for (uint i = start; i < stop; i++)
            {
                foreach (Channel ch in _streamImages[(int)i].Channels)
                {
                    totalPixels += ch.W * ch.H;
                }
            }

            totalPixels  = Math.Max(totalPixels, (nuint)1);
            trees[chunk] = EncModularEncoding.PredefinedTree(_streamOptions[(int)start].Kind, totalPixels, 8, 0);

            return true;
        }

        private void BuildFastFixedTree()
        {
            nuint totalPixels = 0;
            int maxBitdepth   = 0;

            for (int i = 0; i < _streamImages.Count; i++)
            {
                ModularImage img = _streamImages[i];
                maxBitdepth      = Math.Max(maxBitdepth, img.BitDepth);

                for (int c = 0; c < img.Channels.Count; c++)
                {
                    totalPixels += img.Channels[c].W * img.Channels[c].H;
                }
            }

            if (_cparams.SpeedTier <= SpeedTier.kFalcon)
            {
                _tree = EncModularEncoding.PredefinedTree(
                    ModularOptions.TreeKind.WPFixedDC,
                    totalPixels,
                    maxBitdepth,
                    _streamOptions[0].MaxProperties);
            }
            else if (_cparams.SpeedTier <= SpeedTier.kThunder)
            {
                _tree = EncModularEncoding.PredefinedTree(
                    ModularOptions.TreeKind.GradientFixedDC,
                    totalPixels,
                    maxBitdepth,
                    _streamOptions[0].MaxProperties);
            }
            else
            {
                _tree = new List<PropertyDecisionNode> { PropertyDecisionNode.Leaf(Predictor.Gradient) };
            }
        }

        private void ConfigureVarDctDcStreamOptions(int streamId, bool nlDc)
        {
            ModularOptions opts = _streamOptions[streamId];
            opts.MaxChanSize    = 0xFFFFFF;
            opts.Predictor      = Predictor.Weighted;
            opts.WpTreeMode     = ModularOptions.TreeMode.WPOnly;

            if (_cparams.SpeedTier >= SpeedTier.kSquirrel)
            {
                opts.Kind = ModularOptions.TreeKind.WPFixedDC;
            }

            if (_cparams.SpeedTier < SpeedTier.kSquirrel && !nlDc)
            {
                opts.Predictor  = _cparams.SpeedTier < SpeedTier.kKitten ? Predictor.Variable : Predictor.Best;
                opts.WpTreeMode = ModularOptions.TreeMode.Default;
                opts.Kind       = ModularOptions.TreeKind.Learn;
            }

            if (_cparams.DecodingSpeedTier >= 1)
            {
                opts.Kind = ModularOptions.TreeKind.GradientFixedDC;
            }

            opts.HistogramParams = _streamOptions[0].HistogramParams;
        }

        private Status QuantizeVarDctDcGroup(
            FrameHeader frameHeader,
            Image3<float> dc,
            Rect r,
            int streamId,
            bool nlDc,
            float mul,
            PassesEncoderState encState)
        {
            ModularImage img        = _streamImages[streamId];
            ReadOnlySpan<float> cfl = encState.Shared.Cmap.Base.DCFactors();
            ReadOnlySpan<int> order = stackalloc int[3] { 1, 0, 2 };
            bool hasOutliers        = false;

            if (nlDc)
            {
                if (!frameHeader.ChromaSubsampling.Is444())
                {
                    return Status.Failure("Near-lossless DC requires 4:4:4 chroma subsampling");
                }

                bool gradMode = _streamOptions[streamId].Kind == ModularOptions.TreeKind.GradientFixedDC;
                QuantizeVarDctDcNearLossless(dc, r, img, encState, cfl, order, mul, gradMode, ref hasOutliers);
            }
            else if (frameHeader.ChromaSubsampling.Is444())
            {
                QuantizeVarDctDc444(dc, r, img, encState, cfl, order, mul);
            }
            else
            {
                Status subOk = QuantizeVarDctDcSubsampled(frameHeader, dc, r, img, encState, order, mul);

                if (!subOk.IsOk)
                {
                    return subOk;
                }
            }

            return hasOutliers ? Status.Failure("Unsupported range of DC values") : Status.OkStatus;
        }

        private static void QuantizeVarDctDcNearLossless(
            Image3<float> dc,
            Rect r,
            ModularImage img,
            PassesEncoderState encState,
            ReadOnlySpan<float> cfl,
            ReadOnlySpan<int> order,
            float mul,
            bool gradMode,
            ref bool hasOutliers)
        {
            for (int idx = 0; idx < 3; idx++)
            {
                int c            = order[idx];
                float invFactor  = encState.Shared.Quantizer.GetInvDcStep((nuint)c) * mul;
                float yFactor    = encState.Shared.Quantizer.GetDcStep(1) / mul;
                float cflFactor  = cfl[c];
                Channel ch       = img.Channels[c < 2 ? c ^ 1 : c];
                WeightedState? wpState = gradMode
                    ? null
                    : new WeightedState(new WeightedHeader(), (nuint)r.XSize, (nuint)r.YSize);

                for (nuint y = 0; y < (nuint)r.YSize; y++)
                {
                    Span<int> quantRow     = ch.Row(y);
                    float* row             = r.ConstPlaneRow(dc, (nuint)c, y);
                    ReadOnlySpan<int> rowY = img.Channels[0].ConstRow(y);

                    for (int x = 0; x < r.XSize; x++)
                    {
                        float val = c == 1 ? row[x] : row[x] - (rowY[x] * yFactor * cflFactor);

                        if (gradMode)
                        {
                            quantRow[x] = QuantizeGradient(ch, x, (int)y, val, invFactor);
                        }
                        else
                        {
                            quantRow[x] = QuantizeWP(ch, x, (int)y, wpState!, val, invFactor, ref hasOutliers);
                            wpState!.UpdateErrors(quantRow[x], (nuint)x, y, (nuint)r.XSize);
                        }
                    }
                }
            }
        }

        private static void QuantizeVarDctDc444(
            Image3<float> dc,
            Rect r,
            ModularImage img,
            PassesEncoderState encState,
            ReadOnlySpan<float> cfl,
            ReadOnlySpan<int> order,
            float mul)
        {
            for (int idx = 0; idx < 3; idx++)
            {
                int c           = order[idx];
                float invFactor = encState.Shared.Quantizer.GetInvDcStep((nuint)c) * mul;
                float yFactor   = encState.Shared.Quantizer.GetDcStep(1) / mul;
                float cflFactor = cfl[c];
                Channel ch      = img.Channels[c < 2 ? c ^ 1 : c];

                for (nuint y = 0; y < (nuint)r.YSize; y++)
                {
                    Span<int> quantRow     = ch.Row(y);
                    float* row             = r.ConstPlaneRow(dc, (nuint)c, y);
                    ReadOnlySpan<int> rowY = img.Channels[0].ConstRow(y);

                    for (int x = 0; x < r.XSize; x++)
                    {
                        float val   = c == 1 ? row[x] : row[x] - (rowY[x] * yFactor * cflFactor);
                        quantRow[x] = (int)MathF.Round(val * invFactor);
                    }
                }
            }
        }

        private static Status QuantizeVarDctDcSubsampled(
            FrameHeader frameHeader,
            Image3<float> dc,
            Rect r,
            ModularImage img,
            PassesEncoderState encState,
            ReadOnlySpan<int> order,
            float mul)
        {
            for (int idx = 0; idx < 3; idx++)
            {
                int c       = order[idx];
                int hshift  = (int)frameHeader.ChromaSubsampling.HShift((nuint)c);
                int vshift  = (int)frameHeader.ChromaSubsampling.VShift((nuint)c);
                Rect rect   = new Rect(r.X0 >> hshift, r.Y0 >> vshift, r.XSize >> hshift, r.YSize >> vshift);
                float invF  = encState.Shared.Quantizer.GetInvDcStep((nuint)c) * mul;
                Channel ch  = img.Channels[c < 2 ? c ^ 1 : c];

                Status shrinkOk = ch.Shrink(rect.XSize, rect.YSize);

                if (!shrinkOk.IsOk)
                {
                    return shrinkOk;
                }

                for (nuint y = 0; y < (nuint)rect.YSize; y++)
                {
                    Span<int> quantRow = ch.Row(y);
                    float* row         = rect.ConstPlaneRow(dc, (nuint)c, y);

                    for (int x = 0; x < rect.XSize; x++)
                    {
                        quantRow[x] = (int)MathF.Round(row[x] * invF);
                    }
                }
            }

            return true;
        }

        private Status BuildAcMetadataChannels(
            Rect r,
            nuint groupIndex,
            int streamId,
            PassesEncoderState encState)
        {
            JxlMemoryManager? mm         = encState.MemoryManager;
            StatusOr<ModularImage> imgOr = ModularImage.Create(mm, (nuint)r.XSize, (nuint)r.YSize, 8, 4);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            _streamImages[streamId].Dispose();
            _streamImages[streamId] = imgOr.Value;
            ModularImage image      = _streamImages[streamId];

            Rect cr                 = new Rect(r.X0 >> 3, r.Y0 >> 3, (r.XSize + 7) >> 3, (r.YSize + 7) >> 3);
            StatusOr<Channel> ch0Or = Channel.Create(mm, (nuint)cr.XSize, (nuint)cr.YSize, 3, 3);
            StatusOr<Channel> ch1Or = ch0Or.IsOk ? Channel.Create(mm, (nuint)cr.XSize, (nuint)cr.YSize, 3, 3) : ch0Or.Status;
            StatusOr<Channel> ch2Or = ch1Or.IsOk
                ? Channel.Create(mm, (nuint)(r.XSize * r.YSize), 2, 0, 0)
                : ch1Or.Status;

            if (!ch2Or.IsOk)
            {
                return ch2Or.Status;
            }

            image.Channels[0].Dispose();
            image.Channels[1].Dispose();
            image.Channels[2].Dispose();
            image.Channels[0] = ch0Or.Value;
            image.Channels[1] = ch1Or.Value;
            image.Channels[2] = ch2Or.Value;

            CopySBytePlaneToChannel(cr, encState.Shared.Cmap.YtoXMap!, image.Channels[0].Plane);
            CopySBytePlaneToChannel(cr, encState.Shared.Cmap.YtoBMap!, image.Channels[1].Plane);

            nuint num        = PackAcStrategyAndQf(r, encState, image);
            image.Channels[2].W              = num;
            AcMetadataSize[(int)groupIndex]  = num;

            return true;
        }

        private static void CopySBytePlaneToChannel(Rect cr, ImagePlane<sbyte> src, ImagePlane<int> dst)
        {
            for (nuint y = 0; y < (nuint)cr.YSize; y++)
            {
                sbyte* rowIn = cr.ConstRow(src, y);
                int* rowOut  = dst.Row(y);

                for (int x = 0; x < cr.XSize; x++)
                {
                    rowOut[x] = rowIn[x];
                }
            }
        }

        private static nuint PackAcStrategyAndQf(Rect r, PassesEncoderState encState, ModularImage image)
        {
            nuint num    = 0;
            int* outAcs  = image.Channels[2].Plane.Row(0);
            int* outQf   = image.Channels[2].Plane.Row(1);

            for (nuint y = 0; y < (nuint)r.YSize; y++)
            {
                AcStrategyRow rowAcs = encState.Shared.AcStrategy.ConstRow(r, y);
                int* rowQf           = r.ConstRow(encState.Shared.RawQuantField, y);
                byte* rowEpf         = r.ConstRow(encState.Shared.EpfSharpness, y);
                int* rowOutEpf       = image.Channels[3].Plane.Row(y);

                for (int x = 0; x < r.XSize; x++)
                {
                    rowOutEpf[x] = rowEpf[x];

                    if (!rowAcs[(nuint)x].IsFirstBlock())
                    {
                        continue;
                    }

                    outAcs[num] = rowAcs[(nuint)x].RawStrategy();
                    outQf[num]  = rowQf[x] - 1;
                    num++;
                }
            }

            return num;
        }

        private static int QuantizeWP(
            Channel ch,
            int x,
            int y,
            WeightedState wpState,
            float value,
            float invFactor,
            ref bool hasOutliers)
        {
            float svalue          = value * invFactor;
            PredictionResult pred = ModularPredictors.PredictNoTreeWP(ch, x, y, Predictor.Weighted, wpState);
            svalue               -= pred.Guess;

            if (svalue > -KQDeadzone && svalue < KQDeadzone)
            {
                svalue = 0.0f;
            }

            int residual = 0;

            if (float.IsNaN(svalue) || svalue > int.MaxValue || svalue < int.MinValue)
            {
                hasOutliers = true;
            }
            else
            {
                residual = (int)MathF.Round(svalue, MidpointRounding.AwayFromZero);
            }

            if (residual > 2 || residual < -2)
            {
                residual = (int)MathF.Round(svalue * 0.5f, MidpointRounding.AwayFromZero) * 2;
            }

            return residual + (int)pred.Guess;
        }

        private static int QuantizeGradient(Channel ch, int x, int y, float value, float invFactor)
        {
            float svalue          = value * invFactor;
            PredictionResult pred = ModularPredictors.PredictNoTreeNoWP(ch, x, y, Predictor.Gradient);
            svalue               -= pred.Guess;

            if (svalue > -KQDeadzone && svalue < KQDeadzone)
            {
                svalue = 0.0f;
            }

            int residual = (int)MathF.Round(svalue, MidpointRounding.AwayFromZero);

            if (residual > 2 || residual < -2)
            {
                residual = (int)MathF.Round(svalue * 0.5f, MidpointRounding.AwayFromZero) * 2;
            }

            return residual + (int)pred.Guess;
        }

        private static Status MergeTrees(
            IReadOnlyList<List<PropertyDecisionNode>> trees,
            IReadOnlyList<nuint> treeSplits,
            int begin,
            int end,
            List<PropertyDecisionNode> tree)
        {
            if (trees.Count + 1 != treeSplits.Count || end <= begin || end > trees.Count)
            {
                return Status.Failure("Invalid tree merge range");
            }

            if (end == begin + 1)
            {
                int sz = tree.Count;
                tree.AddRange(trees[begin]);

                for (int i = sz; i < tree.Count; i++)
                {
                    PropertyDecisionNode n = tree[i];
                    n.LChild              += (uint)sz;
                    n.RChild              += (uint)sz;
                    tree[i]                = n;
                }

                return true;
            }

            int mid       = (begin + end) / 2;
            int splitval  = (int)treeSplits[mid] - 1;
            int cur       = tree.Count;
            tree.Add(new PropertyDecisionNode(1, splitval, 0, 0, Predictor.Zero, 0, 1));

            PropertyDecisionNode curNode = tree[cur];
            curNode.LChild               = (uint)tree.Count;
            tree[cur]                    = curNode;

            Status leftOk = MergeTrees(trees, treeSplits, mid, end, tree);

            if (!leftOk.IsOk)
            {
                return leftOk;
            }

            curNode        = tree[cur];
            curNode.RChild = (uint)tree.Count;
            tree[cur]      = curNode;

            return MergeTrees(trees, treeSplits, begin, mid, tree);
        }

        private static void QuantizeChannel(Channel ch, int q)
        {
            if (q == 1)
            {
                return;
            }

            for (nuint y = 0; y < ch.Plane.YSize; y++)
            {
                int* row = ch.Plane.Row(y);

                for (nuint x = 0; x < ch.Plane.XSize; x++)
                {
                    if (row[x] < 0)
                    {
                        row[x] = -(((-row[x] + (q / 2)) / q) * q);
                    }
                    else
                    {
                        row[x] = ((row[x] + (q / 2)) / q) * q;
                    }
                }
            }
        }

        private static Status FloatToInt(
            float* rowIn,
            int* rowOut,
            nuint xsize,
            uint bits,
            uint expBits,
            bool fp,
            double dfactor)
        {
            if (bits > 32)
            {
                return Status.Failure("bits > 32 not supported");
            }

            if (!fp)
            {
                ConvertNormalizedFloatRowToInt(rowIn, rowOut, xsize, bits, dfactor);

                return true;
            }

            if (bits == 32)
            {
                if (expBits != 8)
                {
                    return Status.Failure("32-bit float requires 8 exponent bits");
                }

                Buffer.MemoryCopy(rowIn, rowOut, xsize * 4u, xsize * 4u);

                return true;
            }

            return ConvertCustomFloatRowToInt(rowIn, rowOut, xsize, (int)bits, (int)expBits);
        }

        private static void ConvertNormalizedFloatRowToInt(
            float* rowIn,
            int* rowOut,
            nuint xsize,
            uint bits,
            double dfactor)
        {
            if (bits > 22)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    rowOut[x] = (int)((rowIn[x] * dfactor) + (rowIn[x] < 0 ? -0.5 : 0.5));
                }
            }
            else
            {
                float factor = (float)dfactor;

                for (nuint x = 0; x < xsize; x++)
                {
                    rowOut[x] = (int)((rowIn[x] * factor) + (rowIn[x] < 0 ? -0.5f : 0.5f));
                }
            }
        }

        private static Status ConvertCustomFloatRowToInt(
            float* rowIn,
            int* rowOut,
            nuint xsize,
            int bits,
            int expBits)
        {
            if (bits <= 0)
            {
                return Status.Failure("Invalid float bit depth");
            }

            int expBias   = (1 << (expBits - 1)) - 1;
            int maxExp    = (1 << expBits) - 1;
            uint sign     = 1u << (bits - 1);
            int mantBits  = bits - expBits - 1;
            int mantShift = 23 - mantBits;

            for (nuint x = 0; x < xsize; x++)
            {
                uint f      = Unsafe.As<float, uint>(ref rowIn[x]);
                uint sBit   = f >> 31;
                f          &= 0x7fffffffu;

                if (f == 0)
                {
                    rowOut[x] = (int)(sBit != 0 ? sign : 0u);
                    continue;
                }

                int exp      = (int)(f >> 23) - 127;
                int mantissa = (int)(f & 0x007fffffu);

                if (exp == 128)
                {
                    uint nanInf = (sBit != 0 ? sign : 0u) |
                                  ((uint)maxExp << mantBits) |
                                  ((uint)mantissa >> mantShift);
                    rowOut[x]   = (int)nanInf;
                    continue;
                }

                exp += expBias;

                if (exp <= 0)
                {
                    mantissa |= 0x00800000;

                    if (exp < -mantBits)
                    {
                        return Status.Failure("Subnormal float cannot be represented");
                    }

                    mantissa >>= 1 - exp;
                    exp        = 0;
                }

                if (exp >= maxExp || (mantissa & ((1 << mantShift) - 1)) != 0)
                {
                    return Status.Failure("Custom float precision/exponent error");
                }

                mantissa >>= mantShift;
                uint res   = (sBit != 0 ? sign : 0u) | ((uint)exp << mantBits) | (uint)mantissa;
                rowOut[x]  = (int)res;
            }

            return true;
        }

        private static float EstimateWPCost(ModularImage img, int wpMode)
        {
            nuint extraBits         = 0;
            float histoCost         = 0.0f;
            HybridUintConfig config = new HybridUintConfig();
            ReadOnlySpan<int> cuts  = stackalloc int[9] { -500, -192, -80, -32, 0, 32, 80, 192, 500 };
            int nc                  = cuts.Length + 1;
            Histogram[] histo       = new Histogram[nc];

            for (int i = 0; i < nc; i++)
            {
                histo[i] = new Histogram();
            }

            WeightedHeader wpHeader = new WeightedHeader();
            wpHeader.SetPredictorMode(wpMode);

            for (int cIdx = 0; cIdx < img.Channels.Count; cIdx++)
            {
                if (!AccumulateChannelWpCost(img.Channels[cIdx], wpHeader, config, cuts, histo, ref extraBits))
                {
                    return float.MaxValue;
                }

                for (int i = 0; i < nc; i++)
                {
                    histoCost += histo[i].ShannonEntropy();
                    histo[i].Clear();
                }
            }

            return histoCost + extraBits;
        }

        private static bool AccumulateChannelWpCost(
            Channel ch,
            WeightedHeader wpHeader,
            HybridUintConfig config,
            ReadOnlySpan<int> cuts,
            Histogram[] histo,
            ref nuint extraBits)
        {
            WeightedState wpState = new WeightedState(wpHeader, ch.W, ch.H);
            Span<int> properties  = stackalloc int[1];
            bool unhealthy        = false;

            for (nuint y = 0; y < ch.H; y++)
            {
                ReadOnlySpan<int> r = ch.ConstRow(y);

                for (int x = 0; x < (int)ch.W; x++)
                {
                    NeighborPixels neighbors = ModularPredictors.GatherNeighbors(ch, x, (int)y, noEdgeCases: false);
                    int guess                = (int)wpState.Predict(
                        (nuint)x,
                        y,
                        ch.W,
                        in neighbors,
                        properties,
                        0,
                        computeProperties: true);

                    int ctx = 0;

                    for (int k = 0; k < cuts.Length; k++)
                    {
                        if (cuts[k] >= properties[0])
                        {
                            ctx++;
                        }
                    }

                    long diff = (long)r[x] - guess;

                    if (diff < int.MinValue || diff > int.MaxValue)
                    {
                        unhealthy = true;
                    }

                    config.Encode(PackSigned.Pack((int)diff), out uint token, out uint nbits, out _);
                    histo[ctx].Add(token);
                    extraBits += nbits;
                    wpState.UpdateErrors(r[x], (nuint)x, y, ch.W);
                }
            }

            return !unhealthy;
        }

        private static bool DoTransform(
            ModularImage image,
            Transform tr,
            WeightedHeader wpHeader,
            ThreadPool? pool = null,
            bool forceJxlArt = false)
        {
            Transform t = tr.Clone();
            bool didIt;

            if (forceJxlArt)
            {
                if (!t.MetaApply(image).IsOk)
                {
                    return false;
                }

                didIt = true;
            }
            else
            {
                didIt = EncTransform.TransformForward(t, image, wpHeader, pool).IsOk;
            }

            if (didIt)
            {
                image.Transforms.Add(t);
            }

            return didIt;
        }

        private static StatusOr<bool> MaybeDoTransform(
            ModularImage image,
            Transform tr,
            CompressParams cparams,
            WeightedHeader wpHeader,
            float costBefore,
            ThreadPool? pool = null,
            bool forceJxlArt = false)
        {
            if (forceJxlArt || cparams.SpeedTier >= SpeedTier.kSquirrel)
            {
                return DoTransform(image, tr, wpHeader, pool, forceJxlArt);
            }

            bool didIt = DoTransform(image, tr, wpHeader, pool);

            if (didIt)
            {
                StatusOr<float> costAfterOr = EncModularEncoding.EstimateCost(image);

                if (!costAfterOr.IsOk)
                {
                    return costAfterOr.Status;
                }

                if (costAfterOr.Value > costBefore)
                {
                    Transform t = image.Transforms[image.Transforms.Count - 1];

                    if (!t.Inverse(image, wpHeader, pool).IsOk)
                    {
                        return false;
                    }

                    image.Transforms.RemoveAt(image.Transforms.Count - 1);
                    didIt = false;
                }
            }

            return didIt;
        }

        private static Status TryPalettes(
            ModularImage gi,
            ref int maxBitdepth,
            ref int maxval,
            CompressParams cparams,
            float channelColorsPercent,
            ThreadPool? pool)
        {
            float costBefore      = 0.0f;
            int didPalette        = 0;
            float nbPixels        = gi.Channels[0].W * gi.Channels[0].H;
            int nbChans           = gi.Channels.Count - (int)gi.NbMetaChannels;
            float arbBppEstimate  = 0.2f * gi.BitDepth * nbChans;

            if (cparams.PaletteColors != 0 || cparams.LossyPalette)
            {
                if (cparams.SpeedTier <= SpeedTier.kSquirrel)
                {
                    StatusOr<float> costOr = EncModularEncoding.EstimateCost(gi);

                    if (!costOr.IsOk)
                    {
                        return costOr.Status;
                    }

                    costBefore = costOr.Value;
                }
                else
                {
                    costBefore = nbPixels * arbBppEstimate;
                }

                Status multiOk = TryMultiChannelPalettes(gi, cparams, nbPixels, nbChans, costBefore, pool, out didPalette);

                if (!multiOk.IsOk)
                {
                    return multiOk;
                }
            }

            if (channelColorsPercent > 0)
            {
                return TrySingleChannelPalettes(
                    gi,
                    cparams,
                    channelColorsPercent,
                    nbPixels,
                    didPalette,
                    costBefore,
                    pool,
                    ref maxBitdepth,
                    ref maxval);
            }

            return true;
        }

        private static Status TryMultiChannelPalettes(
            ModularImage gi,
            CompressParams cparams,
            float nbPixels,
            int nbChans,
            float costBefore,
            ThreadPool? pool,
            out int didPalette)
        {
            didPalette     = 0;
            int maxColors  = Math.Min(
                (int)((costBefore * 0.0005f) + (nbPixels / 128.0f) + 128.0f),
                Math.Abs(cparams.PaletteColors));

            if (nbChans > 1)
            {
                Transform maybePalette = new Transform(TransformId.Palette)
                {
                    BeginC         = (uint)gi.NbMetaChannels,
                    NumC           = (uint)nbChans,
                    NbColors       = (uint)Math.Max(0, maxColors),
                    OrderedPalette = cparams.PaletteColors >= 0,
                    LossyPalette   = cparams.LossyPalette && nbChans == 3,
                };

                if (maybePalette.LossyPalette)
                {
                    maybePalette.Predictor = Predictor.Average4;
                }

                StatusOr<bool> didOr = MaybeDoTransform(
                    gi,
                    maybePalette,
                    cparams,
                    new WeightedHeader(),
                    costBefore,
                    pool,
                    cparams.Options.ZeroTokens);

                if (!didOr.IsOk)
                {
                    return didOr.Status;
                }

                didPalette = didOr.Value ? 1 : 0;
            }

            if (didPalette == 0 && nbChans > 3)
            {
                Transform maybePalette3 = new Transform(TransformId.Palette)
                {
                    BeginC         = (uint)gi.NbMetaChannels,
                    NumC           = (uint)(nbChans - 1),
                    NbColors       = (uint)Math.Max(0, maxColors),
                    OrderedPalette = cparams.PaletteColors >= 0,
                    LossyPalette   = cparams.LossyPalette,
                };

                if (maybePalette3.LossyPalette)
                {
                    maybePalette3.Predictor = Predictor.Average4;
                }

                StatusOr<bool> did3Or = MaybeDoTransform(
                    gi,
                    maybePalette3,
                    cparams,
                    new WeightedHeader(),
                    costBefore,
                    pool,
                    cparams.Options.ZeroTokens);

                if (!did3Or.IsOk)
                {
                    return did3Or.Status;
                }

                didPalette = did3Or.Value ? 1 : 0;
            }

            return true;
        }

        private static Status TrySingleChannelPalettes(
            ModularImage gi,
            CompressParams cparams,
            float channelColorsPercent,
            float nbPixels,
            int didPalette,
            float costBefore,
            ThreadPool? pool,
            ref int maxBitdepth,
            ref int maxval)
        {
            int nbChannels   = gi.Channels.Count - (int)gi.NbMetaChannels - didPalette;
            int origBitdepth = maxBitdepth;
            maxBitdepth      = 0;

            if (nbChannels > 0 && (didPalette != 0 || costBefore == 0.0f))
            {
                if (cparams.SpeedTier < SpeedTier.kSquirrel)
                {
                    StatusOr<float> costOr = EncModularEncoding.EstimateCost(gi);

                    if (!costOr.IsOk)
                    {
                        return costOr.Status;
                    }

                    costBefore = costOr.Value;
                }
                else
                {
                    costBefore = 0.0f;
                }
            }

            for (int i = didPalette; i < nbChannels + didPalette; i++)
            {
                Channel ch = gi.Channels[(int)gi.NbMetaChannels + i];
                EncTransform.ComputeMinMax(ch, out int min, out int max);
                long colors = (long)max - min + 1L;

                Transform maybePalette1 = new Transform(TransformId.Palette)
                {
                    BeginC   = (uint)((int)gi.NbMetaChannels + i),
                    NumC     = 1,
                    NbColors = (uint)Math.Max(0, Math.Min((int)(nbPixels / 16.0f), (int)((channelColorsPercent / 100.0) * colors))),
                };

                StatusOr<bool> didChOr = MaybeDoTransform(gi, maybePalette1, cparams, new WeightedHeader(), costBefore, pool);

                if (!didChOr.IsOk)
                {
                    return didChOr.Status;
                }

                if (didChOr.Value)
                {
                    EncTransform.ComputeMinMax(gi.Channels[(int)gi.NbMetaChannels + i], out min, out max);

                    if (max < maxval)
                    {
                        maxval = max;
                    }

                    int chBitdepth = max > 0 ? (int)Bits.CeilLog2Nonzero((uint)max) : 0;

                    if (chBitdepth > maxBitdepth)
                    {
                        maxBitdepth = chBitdepth;
                    }
                }
                else
                {
                    maxBitdepth = origBitdepth;
                }
            }

            return true;
        }
    }

    public static partial class EncQuantWeights
    {
        /// <summary>
        /// Sets custom AC quantization encodings on <paramref name="matrices"/>, registers RAW tables with <paramref name="encoder"/>,
        /// and roundtrips <paramref name="matrices"/>.
        /// Ported from <c>jxl::DequantMatricesSetCustom</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="matrices">Target dequantization matrices.</param>
        /// <param name="encodings">List of 17 <see cref="QuantEncoding"/> entries.</param>
        /// <param name="encoder">Modular frame encoder to receive any RAW quant tables.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesSetCustom(
            DequantMatrices matrices,
            IReadOnlyList<QuantEncoding> encodings,
            ModularFrameEncoder encoder)
        {
            ModularFrameEncoder.EnsureStaticInit();

            if (encodings.Count != DequantMatrices.kNumQuantTables)
            {
                return Status.Failure("encodings.Count != kNumQuantTables");
            }

            matrices.SetEncodings(encodings);
            ReadOnlySpan<int> rx = DequantMatrices.RequiredSizeX;
            ReadOnlySpan<int> ry = DequantMatrices.RequiredSizeY;

            for (int i = 0; i < encodings.Count; i++)
            {
                if (encodings[i].Mode == QuantEncoding.QuantMode.kQuantModeRAW)
                {
                    Status addOk = encoder.AddQuantTable(
                        (nuint)rx[i] * FrameDimensions.kBlockDim,
                        (nuint)ry[i] * FrameDimensions.kBlockDim,
                        encodings[i],
                        (nuint)i);

                    if (!addOk.IsOk)
                    {
                        return addOk;
                    }
                }
            }

            return DequantMatricesRoundtrip(encoder.MemoryManager, matrices);
        }
    }
}
