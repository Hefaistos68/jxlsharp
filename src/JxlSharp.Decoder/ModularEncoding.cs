// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Serializable Modular group header containing the global-tree flag, weighted predictor header, and transforms.
    /// Ported from <c>jxl::GroupHeader</c> in <c>lib/jxl/modular/encoding/encoding.h</c>.
    /// </summary>
    public sealed class GroupHeader : Fields
    {
        /// <summary>Whether to use the global MA tree instead of decoding a per-group tree.</summary>
        public bool UseGlobalTree;

        /// <summary>Weighted predictor parameters for this group.</summary>
        public WeightedHeader WpHeader = new WeightedHeader();

        /// <summary>Reversible transforms applied to this group.</summary>
        public List<Transform> Transforms = new List<Transform>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupHeader"/> class with default values.
        /// </summary>
        public GroupHeader()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "GroupHeader";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.Bool(false, ref UseGlobalTree))
            {
                return false;
            }

            if (!visitor.VisitNested(WpHeader))
            {
                return false;
            }

            uint numTransforms = (uint)Transforms.Count;

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Val(1),
                U32Distr.BitsOffset(4, 2),
                U32Distr.BitsOffset(8, 18),
                0,
                ref numTransforms))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                Transforms.Clear();

                for (uint i = 0; i < numTransforms; i++)
                {
                    Transforms.Add(new Transform());
                }
            }

            for (int i = 0; i < (int)numTransforms; i++)
            {
                if (!visitor.VisitNested(Transforms[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Fast lookup table for single-property MA decision trees (Gradient-only or WP-only).
    /// Ported from <c>jxl::TreeLut</c> in <c>lib/jxl/modular/encoding/encoding.h</c>.
    /// </summary>
    public sealed class TreeLut
    {
        /// <summary>Valid property range for fast lookup table indexing (<c>512 &lt;&lt; 4 = 8192</c>).</summary>
        public const int KPropRangeFast = 512 << 4;

        /// <summary>Context ID lookup table indexed by <c>prop + KPropRangeFast</c>.</summary>
        public readonly byte[] ContextLookup = new byte[2 * KPropRangeFast];

        /// <summary>Optional predictor offset lookup table.</summary>
        public readonly sbyte[]? Offsets;

        /// <summary>Optional multiplier lookup table.</summary>
        public readonly sbyte[]? Multipliers;

        /// <summary>
        /// Initializes a new instance of the <see cref="TreeLut"/> class.
        /// </summary>
        /// <param name="hasOffsets">Whether to allocate <see cref="Offsets"/>.</param>
        /// <param name="hasMultipliers">Whether to allocate <see cref="Multipliers"/>.</param>
        public TreeLut(bool hasOffsets = false, bool hasMultipliers = false)
        {
            if (hasOffsets)
            {
                Offsets = new sbyte[2 * KPropRangeFast];
            }

            if (hasMultipliers)
            {
                Multipliers = new sbyte[2 * KPropRangeFast];
            }
        }
    }

    /// <summary>
    /// Identifies a Modular substream within a frame.
    /// Ported from <c>jxl::ModularStreamId</c> in <c>lib/jxl/dec_modular.h</c>.
    /// </summary>
    public struct ModularStreamId
    {
        /// <summary>Number of quantization tables (17).</summary>
        public const nuint KNumQuantTables = 17;

        /// <summary>
        /// Kind of Modular substream.
        /// </summary>
        public enum StreamKind
        {
            /// <summary>Global modular frame data.</summary>
            GlobalData,

            /// <summary>VarDCT DC group stream.</summary>
            VarDCTDC,

            /// <summary>Modular DC group stream.</summary>
            ModularDC,

            /// <summary>VarDCT AC metadata group stream.</summary>
            ACMetadata,

            /// <summary>Raw quantization table stream.</summary>
            QuantTable,

            /// <summary>Modular AC pass/group stream.</summary>
            ModularAC,
        }

        /// <summary>Substream kind.</summary>
        public StreamKind Kind;

        /// <summary>Quantization table index (only for <see cref="StreamKind.QuantTable"/>).</summary>
        public nuint QuantTableId;

        /// <summary>DC or AC group index.</summary>
        public nuint GroupId;

        /// <summary>Pass index (only for <see cref="StreamKind.ModularAC"/>).</summary>
        public nuint PassId;

        /// <summary>
        /// Creates the global <see cref="ModularStreamId"/>.
        /// </summary>
        /// <returns>Global stream ID.</returns>
        public static ModularStreamId Global()
        {
            return new ModularStreamId { Kind = StreamKind.GlobalData };
        }

        /// <summary>
        /// Creates a VarDCT DC <see cref="ModularStreamId"/> for <paramref name="groupId"/>.
        /// </summary>
        /// <param name="groupId">DC group index.</param>
        /// <returns>VarDCT DC stream ID.</returns>
        public static ModularStreamId VarDCTDC(nuint groupId)
        {
            return new ModularStreamId { Kind = StreamKind.VarDCTDC, GroupId = groupId };
        }

        /// <summary>
        /// Creates a Modular DC <see cref="ModularStreamId"/> for <paramref name="groupId"/>.
        /// </summary>
        /// <param name="groupId">DC group index.</param>
        /// <returns>Modular DC stream ID.</returns>
        public static ModularStreamId ModularDC(nuint groupId)
        {
            return new ModularStreamId { Kind = StreamKind.ModularDC, GroupId = groupId };
        }

        /// <summary>
        /// Creates an AC Metadata <see cref="ModularStreamId"/> for <paramref name="groupId"/>.
        /// </summary>
        /// <param name="groupId">DC group index.</param>
        /// <returns>AC Metadata stream ID.</returns>
        public static ModularStreamId ACMetadata(nuint groupId)
        {
            return new ModularStreamId { Kind = StreamKind.ACMetadata, GroupId = groupId };
        }

        /// <summary>
        /// Creates a QuantTable <see cref="ModularStreamId"/> for <paramref name="quantTableId"/>.
        /// </summary>
        /// <param name="quantTableId">Quant table index (<c>&lt; 17</c>).</param>
        /// <returns>QuantTable stream ID or failure status.</returns>
        public static StatusOr<ModularStreamId> QuantTable(nuint quantTableId)
        {
            if (quantTableId >= KNumQuantTables)
            {
                return Status.Failure("Invalid quant table ID");
            }

            return new ModularStreamId { Kind = StreamKind.QuantTable, QuantTableId = quantTableId };
        }

        /// <summary>
        /// Creates a Modular AC <see cref="ModularStreamId"/> for <paramref name="groupId"/> and <paramref name="passId"/>.
        /// </summary>
        /// <param name="groupId">AC group index.</param>
        /// <param name="passId">Pass index.</param>
        /// <returns>Modular AC stream ID.</returns>
        public static ModularStreamId ModularAC(nuint groupId, nuint passId)
        {
            return new ModularStreamId { Kind = StreamKind.ModularAC, GroupId = groupId, PassId = passId };
        }

        /// <summary>
        /// Returns the total number of modular streams for <paramref name="frameDim"/> and <paramref name="passes"/>.
        /// </summary>
        /// <param name="frameDim">Frame dimensions.</param>
        /// <param name="passes">Number of passes.</param>
        /// <returns>Total stream count.</returns>
        public static nuint Num(FrameDimensions frameDim, nuint passes)
        {
            return ModularAC(0, passes).ID(frameDim);
        }

        /// <summary>
        /// Computes the numeric stream ID used as static property 1 in MA trees.
        /// </summary>
        /// <param name="frameDim">Frame dimensions.</param>
        /// <returns>Numeric stream ID.</returns>
        public readonly nuint ID(FrameDimensions frameDim)
        {
            switch (Kind)
            {
                case StreamKind.GlobalData:
                    return 0;
                case StreamKind.VarDCTDC:
                    return 1u + GroupId;
                case StreamKind.ModularDC:
                    return 1u + frameDim.NumDcGroups + GroupId;
                case StreamKind.ACMetadata:
                    return 1u + (2u * frameDim.NumDcGroups) + GroupId;
                case StreamKind.QuantTable:
                    return 1u + (3u * frameDim.NumDcGroups) + QuantTableId;
                case StreamKind.ModularAC:
                    return 1u + (3u * frameDim.NumDcGroups) + KNumQuantTables +
                           (frameDim.NumGroups * PassId) + GroupId;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Returns a debug string representation of this stream ID.
        /// </summary>
        /// <returns>Debug string.</returns>
        public readonly string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Kind switch
            {
                StreamKind.GlobalData => "ModularGlobal",
                StreamKind.VarDCTDC   => "VarDCTDC",
                StreamKind.ModularDC  => "ModularDC",
                StreamKind.ACMetadata => "ACMeta",
                StreamKind.QuantTable => "QuantTable",
                StreamKind.ModularAC  => "ModularAC",
                _                     => string.Empty,
            });

            if (Kind == StreamKind.VarDCTDC ||
                Kind == StreamKind.ModularDC ||
                Kind == StreamKind.ACMetadata ||
                Kind == StreamKind.ModularAC)
            {
                sb.Append($" group {GroupId}");
            }

            if (Kind == StreamKind.ModularAC)
            {
                sb.Append($" pass {PassId}");
            }

            if (Kind == StreamKind.QuantTable)
            {
                sb.Append($" {QuantTableId}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Optional global entropy decoding context for <see cref="ModularEncoding.ModularGenericDecompress"/>.
    /// </summary>
    public sealed class GlobalModularContext
    {
        /// <summary>Global MA tree.</summary>
        public IReadOnlyList<PropertyDecisionNode>? Tree;

        /// <summary>Global ANS/Huffman code.</summary>
        public ANSCode? Code;

        /// <summary>Global context map.</summary>
        public byte[]? ContextMap;

        /// <summary>Whether truncated group streams are permitted.</summary>
        public bool AllowTruncatedGroup;

        /// <summary>Whether transforms should be inverted after decoding.</summary>
        public bool UndoTransforms = true;
    }

    /// <summary>
    /// Modular channel entropy decoding, MA tree filtering, and generic decompression.
    /// Ported from <c>lib/jxl/modular/encoding/encoding.h</c> and <c>lib/jxl/modular/encoding/encoding.cc</c>.
    /// </summary>
    public static class ModularEncoding
    {
        private struct TreeRange
        {
            public int Begin;
            public int End;
            public int Pos;
        }

        private sealed class ChannelDecodeState
        {
            public required BitReader Br;
            public required ANSSymbolReader Reader;
            public required byte[] ContextMap;
            public required IReadOnlyList<PropertyDecisionNode> GlobalTree;
            public required WeightedHeader WpHeader;
            public required TreeLut TreeLut;
            public required ModularImage Image;
            public int Chan;
            public nuint GroupId;
            public uint FlRun;
            public uint FlV;
        }

        /// <summary>
        /// Filters out static properties (channel and group_id) from <paramref name="globalTree"/> and flattens
        /// pairs of levels into a <see cref="FlatDecisionNode"/> array.
        /// Ported from <c>jxl::FilterTree</c> in <c>lib/jxl/modular/encoding/encoding.cc</c>.
        /// </summary>
        /// <param name="globalTree">Source MA tree.</param>
        /// <param name="staticProps">Static properties (channel, group_id).</param>
        /// <param name="numProps">Output number of properties required.</param>
        /// <param name="useWp">Output whether the weighted predictor or its property is used.</param>
        /// <param name="wpOnly">Output whether only the weighted predictor is used.</param>
        /// <param name="gradientOnly">Output whether only the gradient predictor and property are used.</param>
        /// <returns>Flattened decision tree array.</returns>
        public static FlatDecisionNode[] FilterTree(
            IReadOnlyList<PropertyDecisionNode> globalTree,
            ReadOnlySpan<int> staticProps,
            out nuint numProps,
            out bool useWp,
            out bool wpOnly,
            out bool gradientOnly)
        {
            numProps     = 0;
            bool hasWp   = false;
            bool hasNonWp = false;
            gradientOnly = true;

            List<FlatDecisionNode> output = new List<FlatDecisionNode>();
            Queue<int> nodes              = new Queue<int>();
            nodes.Enqueue(0);

            while (nodes.Count > 0)
            {
                int cur = SkipStaticNodes(globalTree, staticProps, nodes.Dequeue());

                if (globalTree[cur].Property == -1)
                {
                    PropertyDecisionNode leaf = globalTree[cur];
                    FlatDecisionNode flatLeaf = new FlatDecisionNode
                    {
                        Property0       = -1,
                        ChildID         = leaf.LChild,
                        Predictor       = leaf.Predictor,
                        PredictorOffset = (int)leaf.PredictorOffset,
                        Multiplier      = (int)leaf.Multiplier,
                    };
                    gradientOnly &= flatLeaf.Predictor == Predictor.Gradient;
                    hasWp        |= flatLeaf.Predictor == Predictor.Weighted;
                    hasNonWp     |= flatLeaf.Predictor != Predictor.Weighted;
                    output.Add(flatLeaf);

                    continue;
                }

                FlatDecisionNode flat = BuildFlatInternalNode(
                    globalTree,
                    staticProps,
                    cur,
                    output.Count,
                    nodes,
                    ref numProps);

                MarkProperty(flat.PropertyChild0, ref hasWp, ref hasNonWp, ref gradientOnly);
                MarkProperty(flat.PropertyChild1, ref hasWp, ref hasNonWp, ref gradientOnly);
                MarkProperty(flat.Property0, ref hasWp, ref hasNonWp, ref gradientOnly);
                output.Add(flat);
            }

            numProps = RoundUpNumProps(numProps);
            useWp    = hasWp;
            wpOnly   = hasWp && !hasNonWp;

            return output.ToArray();
        }

        /// <summary>
        /// Converts a single-property flattened tree into a direct lookup table <paramref name="lut"/> if all splits
        /// lie within <c>[-KPropRangeFast, KPropRangeFast - 1]</c>.
        /// Ported from <c>jxl::TreeToLookupTable</c> in <c>lib/jxl/modular/encoding/encoding.h</c>.
        /// </summary>
        /// <param name="tree">Flattened decision tree.</param>
        /// <param name="lut">Destination lookup table.</param>
        /// <returns><c>true</c> if the tree could be converted to a lookup table.</returns>
        public static bool TreeToLookupTable(ReadOnlySpan<FlatDecisionNode> tree, TreeLut lut)
        {
            List<TreeRange> ranges = new List<TreeRange>
            {
                new TreeRange
                {
                    Begin = -TreeLut.KPropRangeFast - 1,
                    End   = TreeLut.KPropRangeFast - 1,
                    Pos   = 0,
                },
            };

            while (ranges.Count > 0)
            {
                int topIdx    = ranges.Count - 1;
                TreeRange cur = ranges[topIdx];
                ranges.RemoveAt(topIdx);

                if (cur.Begin < -TreeLut.KPropRangeFast - 1 ||
                    cur.Begin >= TreeLut.KPropRangeFast - 1 ||
                    cur.End > TreeLut.KPropRangeFast - 1)
                {
                    return false;
                }

                ref readonly FlatDecisionNode node = ref tree[cur.Pos];

                if (node.Property0 == -1)
                {
                    if (!PopulateLutLeaf(in node, cur, lut))
                    {
                        return false;
                    }

                    continue;
                }

                PushLutChildRanges(in node, cur, ranges);
            }

            return true;
        }

        /// <summary>
        /// Validates that channel dimensions and squeeze shifts in <paramref name="image"/> are consistent with <paramref name="options"/>.
        /// Ported from <c>jxl::ValidateChannelDimensions</c> in <c>lib/jxl/modular/encoding/encoding.cc</c>.
        /// </summary>
        /// <param name="image">Modular image.</param>
        /// <param name="options">Modular options.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status ValidateChannelDimensions(ModularImage image, ModularOptions options)
        {
            if (!ValidateChannelDimensionPass(image, options, isDc: true) ||
                !ValidateChannelDimensionPass(image, options, isDc: false))
            {
                return Status.Failure("Inconsistent transforms");
            }

            return true;
        }

        /// <summary>
        /// Decompresses a Modular image or substream from <paramref name="br"/>.
        /// Ported from <c>jxl::ModularGenericDecompress</c> in <c>lib/jxl/modular/encoding/encoding.cc</c>.
        /// </summary>
        /// <param name="br">Source bit reader.</param>
        /// <param name="image">Destination modular image.</param>
        /// <param name="header">Optional group header (allocated locally if <c>null</c>).</param>
        /// <param name="groupId">Numeric stream/group ID.</param>
        /// <param name="options">Modular options.</param>
        /// <param name="globalCtx">Optional global tree/code/context-map and transform flags.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ModularGenericDecompress(
            BitReader br,
            ModularImage image,
            GroupHeader? header,
            nuint groupId,
            ModularOptions options,
            GlobalModularContext? globalCtx = null)
        {
            bool undoTransforms      = globalCtx == null || globalCtx.UndoTransforms;
            bool allowTruncatedGroup = globalCtx != null && globalCtx.AllowTruncatedGroup;

            (nuint W, nuint H)[] reqSizes = new (nuint W, nuint H)[image.Channels.Count];

            for (int i = 0; i < image.Channels.Count; i++)
            {
                reqSizes[i] = (image.Channels[i].W, image.Channels[i].H);
            }

            GroupHeader localHeader = header ?? new GroupHeader();
            Status decStatus        = ModularDecode(br, image, localHeader, groupId, options, globalCtx);

            if (!allowTruncatedGroup && !decStatus.IsOk)
            {
                return decStatus;
            }

            if (decStatus.IsFatalError())
            {
                return decStatus;
            }

            if (undoTransforms)
            {
                image.UndoTransforms(localHeader.WpHeader);
            }

            if (image.Error)
            {
                return Status.Failure("Corrupt file. Aborting.");
            }

            if (undoTransforms)
            {
                if (image.Channels.Count != reqSizes.Length)
                {
                    return Status.Failure("Channel count mismatch after undoing transforms");
                }

                for (int c = 0; c < reqSizes.Length; c++)
                {
                    if (reqSizes[c].W != image.Channels[c].W || reqSizes[c].H != image.Channels[c].H)
                    {
                        return Status.Failure("Channel dimension mismatch after undoing transforms");
                    }
                }
            }

            return decStatus;
        }

        private static Status ModularDecode(
            BitReader br,
            ModularImage image,
            GroupHeader header,
            nuint groupId,
            ModularOptions options,
            GlobalModularContext? globalCtx)
        {
            if (image.Channels.Count == 0)
            {
                return true;
            }

            bool allowTruncatedGroup = globalCtx != null && globalCtx.AllowTruncatedGroup;
            Status status            = Bundle.Read(br, header);

            if (!allowTruncatedGroup && !status.IsOk)
            {
                return status;
            }

            if (status.IsFatalError())
            {
                return status;
            }

            if (!br.AllReadsWithinBounds())
            {
                header.Transforms.Clear();
                image.Transforms.Clear();
                ZeroFillChannels(image, 0);

                return Status.NotEnoughBytes("Read overrun before ModularDecode");
            }

            Status metaOk = ApplyMetaTransformsAndValidate(image, header, options, out nuint numChans, out nuint distMul);

            if (!metaOk.IsOk)
            {
                return metaOk;
            }

            if (numChans == 0)
            {
                return true;
            }

            return DecodeModularEntropyStream(br, image, header, groupId, options, globalCtx, distMul);
        }

        private static Status ApplyMetaTransformsAndValidate(
            ModularImage image,
            GroupHeader header,
            ModularOptions options,
            out nuint numChans,
            out nuint distanceMultiplier)
        {
            numChans           = 0;
            distanceMultiplier = 0;
            image.Transforms.Clear();

            foreach (Transform t in header.Transforms)
            {
                Transform copy = t.Clone();
                image.Transforms.Add(copy);
                Status applyOk = copy.MetaApply(image);

                if (!applyOk.IsOk)
                {
                    return applyOk;
                }
            }

            if (image.Error)
            {
                return Status.Failure("Corrupt file. Aborting.");
            }

            Status dimOk = ValidateChannelDimensions(image, options);

            if (!dimOk.IsOk)
            {
                return dimOk;
            }

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

                if (ch.W > distanceMultiplier)
                {
                    distanceMultiplier = ch.W;
                }

                numChans++;
            }

            return true;
        }

        private static Status DecodeModularEntropyStream(
            BitReader br,
            ModularImage image,
            GroupHeader header,
            nuint groupId,
            ModularOptions options,
            GlobalModularContext? globalCtx,
            nuint distanceMultiplier)
        {
            bool allowTruncatedGroup = globalCtx != null && globalCtx.AllowTruncatedGroup;
            int nextChannel          = 0;

            using ScopeGuard scopeGuard = new ScopeGuard(() =>
            {
                ZeroFillChannels(image, nextChannel);
            });

            if (allowTruncatedGroup)
            {
                scopeGuard.Disarm();
            }

            Status resolveOk = ResolveTreeAndCode(
                br,
                image,
                header,
                options,
                globalCtx,
                out IReadOnlyList<PropertyDecisionNode> tree,
                out ANSCode code,
                out byte[] contextMap);

            if (!resolveOk.IsOk)
            {
                return resolveOk;
            }

            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(code, br, distanceMultiplier);

            if (!readerOr.IsOk)
            {
                return readerOr.Status;
            }

            ChannelDecodeState state = new ChannelDecodeState
            {
                Br         = br,
                Reader     = readerOr.Value,
                ContextMap = contextMap,
                GlobalTree = tree,
                WpHeader   = header.WpHeader,
                TreeLut    = new TreeLut(),
                Image      = image,
                GroupId    = groupId,
            };

            Status loopOk = DecodeAllChannelsLoop(state, options, allowTruncatedGroup, ref nextChannel);

            if (!loopOk.IsOk)
            {
                return loopOk;
            }

            scopeGuard.Disarm();

            if (!state.Reader.CheckANSFinalState())
            {
                return Status.Failure("ANS decode final state failed");
            }

            return true;
        }

        private static Status ResolveTreeAndCode(
            BitReader br,
            ModularImage image,
            GroupHeader header,
            ModularOptions options,
            GlobalModularContext? globalCtx,
            out IReadOnlyList<PropertyDecisionNode> tree,
            out ANSCode code,
            out byte[] contextMap)
        {
            tree       = Array.Empty<PropertyDecisionNode>();
            code       = new ANSCode();
            contextMap = Array.Empty<byte>();

            if (!header.UseGlobalTree)
            {
                ulong maxTreeSize = 1024;

                for (int i = 0; i < image.Channels.Count; i++)
                {
                    Channel ch = image.Channels[i];

                    if ((nuint)i >= image.NbMetaChannels &&
                        (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize))
                    {
                        break;
                    }

                    maxTreeSize += (ulong)ch.W * ch.H;
                }

                maxTreeSize = Math.Min(1UL << 20, maxTreeSize);
                List<PropertyDecisionNode> treeStorage = new List<PropertyDecisionNode>();

                Status treeOk = ModularMa.DecodeTree(image.MemoryManager, br, treeStorage, (nuint)maxTreeSize);

                if (!treeOk.IsOk)
                {
                    return treeOk;
                }

                Status histOk = DecAns.DecodeHistograms(
                    image.MemoryManager,
                    br,
                    (nuint)((treeStorage.Count + 1) / 2),
                    code,
                    out contextMap);

                tree = treeStorage;

                return histOk;
            }

            if (globalCtx?.Tree == null ||
                globalCtx.Code == null ||
                globalCtx.ContextMap == null ||
                globalCtx.Tree.Count == 0)
            {
                return Status.Failure("No global tree available but one was requested");
            }

            tree       = globalCtx.Tree;
            code       = globalCtx.Code;
            contextMap = globalCtx.ContextMap;

            return true;
        }

        private static Status DecodeAllChannelsLoop(
            ChannelDecodeState state,
            ModularOptions options,
            bool allowTruncatedGroup,
            ref int nextChannel)
        {
            ModularImage image = state.Image;

            for (; nextChannel < image.Channels.Count; nextChannel++)
            {
                Channel channel = image.Channels[nextChannel];

                if ((nuint)nextChannel >= image.NbMetaChannels &&
                    (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
                {
                    break;
                }

                if (channel.W == 0 || channel.H == 0)
                {
                    continue;
                }

                state.Chan    = nextChannel;
                Status chanOk = DecodeModularChannelMAANS(state);

                if (!chanOk.IsOk)
                {
                    return chanOk;
                }

                if (!state.Br.AllReadsWithinBounds())
                {
                    return allowTruncatedGroup
                        ? Status.NotEnoughBytes("Read overrun in ModularDecode")
                        : Status.Failure("Truncated input");
                }
            }

            return true;
        }

        private static Status DecodeModularChannelMAANS(ChannelDecodeState state)
        {
            Channel channel = state.Image.Channels[state.Chan];

            if (channel.W == 0 || channel.H == 0)
            {
                return true;
            }

            Span<int> staticProps = stackalloc int[ModularOptions.KNumStaticProperties]
            {
                state.Chan,
                (int)state.GroupId,
            };

            FlatDecisionNode[] tree = FilterTree(
                state.GlobalTree,
                staticProps,
                out nuint numProps,
                out bool treeHasWpPropOrPred,
                out bool isWpOnly,
                out bool isGradientOnly);

            for (int i = 0; i < tree.Length; i++)
            {
                if (tree[i].Property0 == -1)
                {
                    tree[i].ChildID = state.ContextMap[tree[i].ChildID];
                }
            }

            bool usesLz77 = state.Reader.UsesLZ77();

            if (tree.Length == 1 && TryDecodeSingleLeafChannel(state, channel, in tree[0], usesLz77))
            {
                return true;
            }

            if (isWpOnly)
            {
                isWpOnly = TreeToLookupTable(tree, state.TreeLut);
            }

            if (isGradientOnly)
            {
                isGradientOnly = TreeToLookupTable(tree, state.TreeLut);
            }

            if (isGradientOnly)
            {
                DecodeGradientLutChannel(state, channel, usesLz77);

                return true;
            }

            if (!usesLz77 && isWpOnly && channel.W > 8)
            {
                DecodeWpLutChannel(state, channel);

                return true;
            }

            return DecodeGeneralTreeChannel(state, channel, tree, staticProps, (int)numProps, treeHasWpPropOrPred, usesLz77);
        }

        private static bool TryDecodeSingleLeafChannel(
            ChannelDecodeState state,
            Channel channel,
            in FlatDecisionNode leaf,
            bool usesLz77)
        {
            Predictor predictor = leaf.Predictor;
            long offset         = leaf.PredictorOffset;
            int multiplier      = leaf.Multiplier;
            nuint ctxId         = leaf.ChildID;

            if (predictor == Predictor.Zero)
            {
                DecodeZeroPredictorChannel(state, channel, ctxId, multiplier, offset, usesLz77);

                return true;
            }

            if (usesLz77 && state.Reader.IsHuffRleOnly() && IsGlobalTreeAllGradientNoop(state.GlobalTree))
            {
                DecodeGradientRleChannel(state, channel, ctxId);

                return true;
            }

            if (predictor == Predictor.Gradient && offset == 0 && multiplier == 1)
            {
                DecodeGradientSingleLeafChannel(state, channel, ctxId, usesLz77);

                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DecodeZeroPredictorChannel(
            ChannelDecodeState state,
            Channel channel,
            nuint ctxId,
            int multiplier,
            long offset,
            bool usesLz77)
        {
            if (state.Reader.IsSingleValueAndAdvance(ctxId, out uint value, channel.W * channel.H))
            {
                int v = MakePixel(value, multiplier, offset);

                for (nuint y = 0; y < channel.H; y++)
                {
                    channel.Row(y).Slice(0, (int)channel.W).Fill(v);
                }

                return;
            }

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> r                 = channel.Row(y);
                ReadOnlySpan<int> rowTop    = y > 0 ? channel.ConstRow(y - 1u) : r;
                ReadOnlySpan<int> rowTopTop = y > 1 ? channel.ConstRow(y - 2u) : rowTop;

                for (int x = 0; x < (int)channel.W; x++)
                {
                    uint v = state.Reader.ReadHybridUintClustered(usesLz77, ctxId, state.Br);
                    r[x]   = multiplier == 1 && offset == 0
                        ? PackSigned.Unpack(v)
                        : MakePixel(v, multiplier, offset);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DecodeGradientRleChannel(ChannelDecodeState state, Channel channel, nuint ctxId)
        {
            long sv = PackSigned.Unpack(state.FlV);

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> r            = channel.Row(y);
                ReadOnlySpan<int> rtop = y > 0 ? channel.ConstRow(y - 1u) : ReadOnlySpan<int>.Empty;
                long guess0            = y > 0 ? rtop[0] : 0;

                if (state.FlRun == 0)
                {
                    state.Reader.ReadHybridUintClusteredHuffRleOnly(ctxId, state.Br, ref state.FlV, ref state.FlRun);
                    sv = PackSigned.Unpack(state.FlV);
                }
                else
                {
                    state.FlRun--;
                }

                r[0] = (int)(sv + guess0);

                for (int x = 1; x < (int)channel.W; x++)
                {
                    int left    = r[x - 1];
                    int top     = y > 0 ? rtop[x] : left;
                    int topleft = y > 0 ? rtop[x - 1] : left;
                    long guess  = ModularPredictors.ClampedGradient(top, left, topleft);

                    if (state.FlRun == 0)
                    {
                        state.Reader.ReadHybridUintClusteredHuffRleOnly(ctxId, state.Br, ref state.FlV, ref state.FlRun);
                        sv = PackSigned.Unpack(state.FlV);
                    }
                    else
                    {
                        state.FlRun--;
                    }

                    r[x] = (int)(sv + guess);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DecodeGradientSingleLeafChannel(
            ChannelDecodeState state,
            Channel channel,
            nuint ctxId,
            bool usesLz77)
        {
            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> r            = channel.Row(y);
                ReadOnlySpan<int> rtop = y > 0 ? channel.ConstRow(y - 1u) : ReadOnlySpan<int>.Empty;

                for (int x = 0; x < (int)channel.W; x++)
                {
                    int left    = x > 0 ? r[x - 1] : (y > 0 ? rtop[x] : 0);
                    int top     = y > 0 ? rtop[x] : left;
                    int topleft = x > 0 && y > 0 ? rtop[x - 1] : left;
                    int guess   = ModularPredictors.ClampedGradient(top, left, topleft);
                    uint v      = state.Reader.ReadHybridUintClustered(usesLz77, ctxId, state.Br);
                    r[x]        = MakePixel(v, 1, guess);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DecodeGradientLutChannel(ChannelDecodeState state, Channel channel, bool usesLz77)
        {
            byte[] lut = state.TreeLut.ContextLookup;

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> r            = channel.Row(y);
                ReadOnlySpan<int> rtop = y > 0 ? channel.ConstRow(y - 1u) : ReadOnlySpan<int>.Empty;

                for (int x = 0; x < (int)channel.W; x++)
                {
                    long left    = x > 0 ? r[x - 1] : (y > 0 ? rtop[x] : 0);
                    long top     = y > 0 ? rtop[x] : left;
                    long topleft = x > 0 && y > 0 ? rtop[x - 1] : left;
                    int guess    = ModularPredictors.ClampedGradient(top, left, topleft);
                    long clamped = Math.Clamp(top + left - topleft, -TreeLut.KPropRangeFast, TreeLut.KPropRangeFast - 1);
                    int pos      = TreeLut.KPropRangeFast + (int)clamped;
                    nuint ctxId  = lut[pos];
                    uint v       = state.Reader.ReadHybridUintClustered(usesLz77, ctxId, state.Br);
                    r[x]         = MakePixel(v, 1, guess);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DecodeWpLutChannel(ChannelDecodeState state, Channel channel)
        {
            WeightedState wpState = new WeightedState(state.WpHeader, channel.W, channel.H);
            Span<int> properties  = stackalloc int[1];
            byte[] lut            = state.TreeLut.ContextLookup;

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> r                 = channel.Row(y);
                ReadOnlySpan<int> rowTop    = y > 0 ? channel.ConstRow(y - 1u) : r;
                ReadOnlySpan<int> rowTopTop = y > 1 ? channel.ConstRow(y - 2u) : rowTop;

                for (int x = 0; x < (int)channel.W; x++)
                {
                    NeighborPixels neighbors = ModularPredictors.GatherNeighbors(r, rowTop, rowTopTop, x, (int)y);
                    int guess                = (int)wpState.Predict(
                        (nuint)x,
                        y,
                        channel.W,
                        in neighbors,
                        properties,
                        0,
                        computeProperties: true);
                    int pos     = TreeLut.KPropRangeFast +
                                  Math.Clamp(properties[0], -TreeLut.KPropRangeFast, TreeLut.KPropRangeFast - 1);
                    nuint ctxId = lut[pos];
                    uint v      = state.Reader.ReadHybridUintClustered(usesLz77: false, ctxId, state.Br);
                    r[x]        = MakePixel(v, 1, guess);
                    wpState.UpdateErrors(r[x], (nuint)x, y, channel.W);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status DecodeGeneralTreeChannel(
            ChannelDecodeState state,
            Channel channel,
            FlatDecisionNode[] tree,
            ReadOnlySpan<int> staticProps,
            int numProps,
            bool useWp,
            bool usesLz77)
        {
            MATreeLookup treeLookup   = new MATreeLookup(tree);
            int[] properties          = new int[numProps];
            nuint extraProps          = (nuint)(numProps - ModularPredictors.KNumNonrefProperties);
            StatusOr<Channel> refsOr  = Channel.Create(state.Image.MemoryManager, extraProps, channel.W);

            if (!refsOr.IsOk)
            {
                return refsOr.Status;
            }

            using Channel references = refsOr.Value;
            WeightedState? wpState   = useWp ? new WeightedState(state.WpHeader, channel.W, channel.H) : null;

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> p                 = channel.Row(y);
                ReadOnlySpan<int> rowTop    = y > 0 ? channel.ConstRow(y - 1u) : p;
                ReadOnlySpan<int> rowTopTop = y > 1 ? channel.ConstRow(y - 2u) : rowTop;
                ModularPredictors.InitPropsRow(properties, staticProps, (int)y);
                ModularPredictors.PrecomputeReferences(channel, y, state.Image, (uint)state.Chan, references);

                for (int x = 0; x < (int)channel.W; x++)
                {
                    PredictionResult res = wpState != null
                        ? ModularPredictors.PredictTreeWP(properties, ModularPredictors.GatherNeighbors(p, rowTop, rowTopTop, x, (int)y), x, (int)y, treeLookup, references, wpState)
                        : ModularPredictors.PredictTreeNoWP(properties, ModularPredictors.GatherNeighbors(p, rowTop, rowTopTop, x, (int)y), x, treeLookup, references);

                    uint v = state.Reader.ReadHybridUintClustered(usesLz77, (nuint)res.Context, state.Br);
                    p[x]   = MakePixel(v, res.Multiplier, res.Guess);
                    wpState?.UpdateErrors(p[x], (nuint)x, y, channel.W);
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MakePixel(uint v, int multiplier, long offset)
        {
            long val = PackSigned.Unpack(v);

            return (int)((val * multiplier) + offset);
        }

        private static bool IsGlobalTreeAllGradientNoop(IReadOnlyList<PropertyDecisionNode> globalTree)
        {
            for (int i = 0; i < globalTree.Count; i++)
            {
                PropertyDecisionNode n = globalTree[i];

                if (n.Property == -1)
                {
                    if (n.Predictor != Predictor.Gradient || n.PredictorOffset != 0 || n.Multiplier != 1)
                    {
                        return false;
                    }
                }
                else if (n.Property >= ModularOptions.KNumStaticProperties)
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipStaticNodes(
            IReadOnlyList<PropertyDecisionNode> globalTree,
            ReadOnlySpan<int> staticProps,
            int cur)
        {
            while (globalTree[cur].Property < ModularOptions.KNumStaticProperties &&
                   globalTree[cur].Property != -1)
            {
                cur = staticProps[globalTree[cur].Property] > globalTree[cur].SplitVal
                    ? (int)globalTree[cur].LChild
                    : (int)globalTree[cur].RChild;
            }

            return cur;
        }

        private static FlatDecisionNode BuildFlatInternalNode(
            IReadOnlyList<PropertyDecisionNode> globalTree,
            ReadOnlySpan<int> staticProps,
            int cur,
            int outputCount,
            Queue<int> nodes,
            ref nuint numProps)
        {
            FlatDecisionNode flat = new FlatDecisionNode
            {
                ChildID   = (uint)(outputCount + nodes.Count + 1),
                Property0 = globalTree[cur].Property,
                SplitVal0 = globalTree[cur].SplitVal,
            };

            numProps = Math.Max((nuint)(flat.Property0 + 1), numProps);

            for (int i = 0; i < 2; i++)
            {
                int curChild = i == 0 ? (int)globalTree[cur].LChild : (int)globalTree[cur].RChild;
                curChild     = SkipStaticNodes(globalTree, staticProps, curChild);

                short propChild;
                int splitChild;

                if (globalTree[curChild].Property == -1)
                {
                    propChild  = 0;
                    splitChild = 0;
                    nodes.Enqueue(curChild);
                    nodes.Enqueue(curChild);
                }
                else
                {
                    propChild  = globalTree[curChild].Property;
                    splitChild = globalTree[curChild].SplitVal;
                    nodes.Enqueue((int)globalTree[curChild].LChild);
                    nodes.Enqueue((int)globalTree[curChild].RChild);
                    numProps = Math.Max((nuint)(propChild + 1), numProps);
                }

                if (i == 0)
                {
                    flat.PropertyChild0 = propChild;
                    flat.SplitValChild0 = splitChild;
                }
                else
                {
                    flat.PropertyChild1 = propChild;
                    flat.SplitValChild1 = splitChild;
                }
            }

            return flat;
        }

        private static void MarkProperty(
            int p,
            ref bool hasWp,
            ref bool hasNonWp,
            ref bool gradientOnly)
        {
            if (p == ModularPredictors.KWPProp)
            {
                hasWp = true;
            }
            else if (p >= ModularOptions.KNumStaticProperties)
            {
                hasNonWp = true;
            }

            if (p >= ModularOptions.KNumStaticProperties && p != ModularPredictors.KGradientProp)
            {
                gradientOnly = false;
            }
        }

        private static nuint RoundUpNumProps(nuint numProps)
        {
            const nuint kNonref = (nuint)ModularPredictors.KNumNonrefProperties;
            const nuint kExtra  = (nuint)ModularPredictors.KExtraPropsPerChannel;

            if (numProps > kNonref)
            {
                return (Common.DivCeil(numProps - kNonref, kExtra) * kExtra) + kNonref;
            }

            return kNonref;
        }

        private static bool PopulateLutLeaf(in FlatDecisionNode node, TreeRange cur, TreeLut lut)
        {
            if (node.ChildID > byte.MaxValue ||
                node.PredictorOffset < sbyte.MinValue ||
                node.PredictorOffset > sbyte.MaxValue ||
                node.Multiplier < sbyte.MinValue ||
                node.Multiplier > sbyte.MaxValue)
            {
                return false;
            }

            if (lut.Multipliers == null && node.Multiplier != 1)
            {
                return false;
            }

            if (lut.Offsets == null && node.PredictorOffset != 0)
            {
                return false;
            }

            for (int i = cur.Begin + 1; i < cur.End + 1; i++)
            {
                int idx                = i + TreeLut.KPropRangeFast;
                lut.ContextLookup[idx] = (byte)node.ChildID;

                if (lut.Multipliers != null)
                {
                    lut.Multipliers[idx] = (sbyte)node.Multiplier;
                }

                if (lut.Offsets != null)
                {
                    lut.Offsets[idx] = (sbyte)node.PredictorOffset;
                }
            }

            return true;
        }

        private static void PushLutChildRanges(in FlatDecisionNode node, TreeRange cur, List<TreeRange> ranges)
        {
            int childId = (int)node.ChildID;

            if (node.PropertyChild0 >= ModularOptions.KNumStaticProperties)
            {
                ranges.Add(new TreeRange { Begin = node.SplitValChild0, End = cur.End, Pos = childId });
                ranges.Add(new TreeRange { Begin = node.SplitVal0, End = node.SplitValChild0, Pos = childId + 1 });
            }
            else
            {
                ranges.Add(new TreeRange { Begin = node.SplitVal0, End = cur.End, Pos = childId });
            }

            if (node.PropertyChild1 >= ModularOptions.KNumStaticProperties)
            {
                ranges.Add(new TreeRange { Begin = node.SplitValChild1, End = node.SplitVal0, Pos = childId + 2 });
                ranges.Add(new TreeRange { Begin = cur.Begin, End = node.SplitValChild1, Pos = childId + 3 });
            }
            else
            {
                ranges.Add(new TreeRange { Begin = cur.Begin, End = node.SplitVal0, Pos = childId + 2 });
            }
        }

        private static bool ValidateChannelDimensionPass(ModularImage image, ModularOptions options, bool isDc)
        {
            int nbChannels = image.Channels.Count;
            nuint groupDim = options.GroupDim * (isDc ? FrameDimensions.KBlockDim : 1u);
            int c          = (int)image.NbMetaChannels;

            for (; c < nbChannels; c++)
            {
                Channel ch = image.Channels[c];

                if (ch.W > options.GroupDim || ch.H > options.GroupDim)
                {
                    break;
                }
            }

            for (; c < nbChannels; c++)
            {
                Channel ch = image.Channels[c];

                if (ch.W == 0 || ch.H == 0)
                {
                    continue;
                }

                bool isDcChannel = Math.Min(ch.HShift, ch.VShift) >= 3;

                if (isDcChannel != isDc)
                {
                    continue;
                }

                int maxShift   = Math.Max(ch.HShift, ch.VShift);
                nuint tileDim  = maxShift >= 64 ? 0u : groupDim >> maxShift;

                if (tileDim == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ZeroFillChannels(ModularImage image, int startChannel)
        {
            for (int c = startChannel; c < image.Channels.Count; c++)
            {
                ImageOps.ZeroFillImage(image.Channels[c].Plane);
            }
        }
    }
}
