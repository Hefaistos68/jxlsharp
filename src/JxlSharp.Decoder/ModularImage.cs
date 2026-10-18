// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Modular predictor modes.
    /// Ported from <c>jxl::Predictor</c> in <c>lib/jxl/modular/options.h</c>.
    /// </summary>
    public enum Predictor : uint
    {
        /// <summary>Always predicts 0.</summary>
        Zero = 0,

        /// <summary>Predicts the left neighbor (W).</summary>
        Left = 1,

        /// <summary>Predicts the top neighbor (N).</summary>
        Top = 2,

        /// <summary>Predicts <c>(W + N) / 2</c>.</summary>
        Average0 = 3,

        /// <summary>Selects between W and N based on NW.</summary>
        Select = 4,

        /// <summary>Clamped gradient predictor <c>Clamp(W + N - NW, min(W, N), max(W, N))</c>.</summary>
        Gradient = 5,

        /// <summary>Self-correcting weighted predictor combining 4 sub-predictors.</summary>
        Weighted = 6,

        /// <summary>Predicts the top-right neighbor (NE).</summary>
        TopRight = 7,

        /// <summary>Predicts the top-left neighbor (NW).</summary>
        TopLeft = 8,

        /// <summary>Predicts the second left neighbor (WW).</summary>
        LeftLeft = 9,

        /// <summary>Predicts <c>(W + NW) / 2</c>.</summary>
        Average1 = 10,

        /// <summary>Predicts <c>(NW + N) / 2</c>.</summary>
        Average2 = 11,

        /// <summary>Predicts <c>(N + NE) / 2</c>.</summary>
        Average3 = 12,

        /// <summary>Weighted average of N, NN, W, WW, NEE, NE.</summary>
        Average4 = 13,

        /// <summary>Encoder-only: best of Gradient and Weighted.</summary>
        Best = 14,

        /// <summary>Encoder-only: variable decision tree per row.</summary>
        Variable = 15,

        /// <summary>Sentinel for undefined predictor (<c>~0u</c>).</summary>
        Undefined = ~0u,
    }

    /// <summary>
    /// Context indices used when entropy-coding a Meta-Adaptive (MA) decision tree.
    /// Ported from <c>jxl::MATreeContext</c> in <c>lib/jxl/modular/encoding/ma_common.h</c>.
    /// </summary>
    public enum MATreeContext : int
    {
        /// <summary>Split threshold context.</summary>
        SplitValContext = 0,

        /// <summary>Property index context.</summary>
        PropertyContext = 1,

        /// <summary>Leaf predictor ID context.</summary>
        PredictorContext = 2,

        /// <summary>Leaf predictor offset context.</summary>
        OffsetContext = 3,

        /// <summary>Leaf multiplier logarithm context.</summary>
        MultiplierLogContext = 4,

        /// <summary>Leaf multiplier mantissa bits context.</summary>
        MultiplierBitsContext = 5,

        /// <summary>Total number of MA tree contexts (6).</summary>
        NumTreeContexts = 6,
    }

    /// <summary>
    /// Options controlling Modular encoding and decoding.
    /// Ported from <c>jxl::ModularOptions</c> in <c>lib/jxl/modular/options.h</c>.
    /// </summary>
    public sealed class ModularOptions
    {
        /// <summary>Number of standard modular predictors (14).</summary>
        public const int KNumModularPredictors = (int)Predictor.Average4 + 1;

        /// <summary>Number of modular predictors including encoder-only ones (16).</summary>
        public const int KNumModularEncoderPredictors = (int)Predictor.Variable + 1;

        /// <summary>Number of static properties: channel and group_id (2).</summary>
        public const int KNumStaticProperties = 2;

        /// <summary>Maximum allowed MA tree node count (4,194,304).</summary>
        public const nuint KMaxTreeSize = 1u << 22;

        /// <summary>Default property indices used by encoder splitting heuristics.</summary>
        private static readonly uint[] DefaultSplittingHeuristicsProperties =
        {
            0, 1, 15, 9, 10, 11, 12, 13,
        };

        /// <summary>
        /// Tree mode constraints for the encoder.
        /// </summary>
        public enum TreeMode
        {
            /// <summary>Only use the Gradient predictor.</summary>
            GradientOnly,

            /// <summary>Only use the Weighted predictor.</summary>
            WPOnly,

            /// <summary>Never use the Weighted predictor.</summary>
            NoWP,

            /// <summary>Unconstrained tree mode.</summary>
            Default,
        }

        /// <summary>
        /// Predefined or learned tree type for the encoder.
        /// </summary>
        public enum TreeKind
        {
            /// <summary>Single-leaf tree with Zero predictor.</summary>
            TrivialTreeNoPredictor,

            /// <summary>Learn MA tree from pixel statistics.</summary>
            Learn,

            /// <summary>Predefined tree for JPEG transcoding AC metadata.</summary>
            JpegTranscodeACMeta,

            /// <summary>Predefined tree for Falcon AC metadata.</summary>
            FalconACMeta,

            /// <summary>Predefined tree for general AC metadata.</summary>
            ACMeta,

            /// <summary>Predefined Weighted-predictor tree for VarDCT DC.</summary>
            WPFixedDC,

            /// <summary>Predefined Gradient-predictor tree for VarDCT DC.</summary>
            GradientFixedDC,
        }

        // Stop encoding/decoding when reaching a (non-meta) channel that has a
        // dimension bigger than max_chan_size.
        /// <summary>Maximum non-meta channel dimension to decode/encode.</summary>
        public nuint MaxChanSize = 0xFFFFFF;

        // Used during decoding for validation of transforms (squeezing) scheme.
        /// <summary>Group dimension used to validate squeeze transforms.</summary>
        public nuint GroupDim = 0x1FFFFFFF;

        /// <summary>Fraction of pixels to inspect when learning an MA tree.</summary>
        public float NbRepeats = 0.5f;

        /// <summary>Maximum number of previous-channel properties to use in MA trees.</summary>
        public int MaxProperties;

        /// <summary>Properties used by encoder splitting heuristics.</summary>
        public List<uint> SplittingHeuristicsProperties = new List<uint>(DefaultSplittingHeuristicsProperties);

        /// <summary>Node threshold for encoder splitting heuristics.</summary>
        public float SplittingHeuristicsNodeThreshold = 96.0f;

        /// <summary>Maximum property values considered during splitting.</summary>
        public nuint MaxPropertyValues = 32;

        /// <summary>Predictor to use for each channel.</summary>
        public Predictor Predictor = Predictor.Undefined;

        /// <summary>Weighted predictor preset mode (0..4).</summary>
        public int WpMode;

        /// <summary>Multiplier trade-off for fast decode trees.</summary>
        public float FastDecodeMultiplier = 1.01f;

        /// <summary>Forces encoder tree compatibility with fast decode paths.</summary>
        public TreeMode WpTreeMode = TreeMode.Default;

        /// <summary>Whether to skip fast paths in the encoder.</summary>
        public bool SkipEncoderFastPath;

        /// <summary>Kind of tree to construct in the encoder.</summary>
        public TreeKind Kind = TreeKind.Learn;

        /// <summary>Ignore the image and pretend all tokens are zeroes.</summary>
        public bool ZeroTokens;

        /// <summary>Optional encoder <c>HistogramParams</c> instance.</summary>
        public object? HistogramParams;

        /// <summary>
        /// Creates a new <see cref="ModularOptions"/> instance with default settings.
        /// </summary>
        /// <returns>Default <see cref="ModularOptions"/>.</returns>
        public static ModularOptions Default() => new ModularOptions();

        /// <summary>
        /// Creates a deep copy of this <see cref="ModularOptions"/> instance.
        /// </summary>
        /// <returns>Cloned <see cref="ModularOptions"/>.</returns>
        public ModularOptions Clone()
        {
            ModularOptions copy = (ModularOptions)MemberwiseClone();
            copy.SplittingHeuristicsProperties = new List<uint>(SplittingHeuristicsProperties);

            return copy;
        }
    }

    /// <summary>
    /// Represents a single 2D 32-bit signed integer plane in a <see cref="ModularImage"/>.
    /// Ported from <c>jxl::Channel</c> in <c>lib/jxl/modular/modular_image.h</c> and <c>modular_image.cc</c>.
    /// </summary>
    public sealed class Channel : IDisposable
    {
        /// <summary>Underlying 32-bit signed integer sample plane.</summary>
        public ImagePlane<int> Plane;

        /// <summary>Active width of the channel in pixels.</summary>
        public nuint W;

        /// <summary>Active height of the channel in pixels.</summary>
        public nuint H;

        /// <summary>Horizontal shift relative to the image width (<c>w ~= image.w &gt;&gt; hshift</c>).</summary>
        public int HShift;

        /// <summary>Vertical shift relative to the image height (<c>h ~= image.h &gt;&gt; vshift</c>).</summary>
        public int VShift;

        /// <summary>Original component index, or -1 if unassigned.</summary>
        public int Component = -1;

        private Channel(ImagePlane<int> plane, nuint iw, nuint ih, int hsh, int vsh)
        {
            Plane  = plane;
            W      = iw;
            H      = ih;
            HShift = hsh;
            VShift = vsh;
        }

        /// <summary>
        /// Gets the memory manager associated with <see cref="Plane"/>.
        /// </summary>
        public JxlMemoryManager? MemoryManager => Plane.MemoryManager;

        /// <summary>
        /// Allocates a new <see cref="Channel"/> of dimensions <paramref name="iw"/> x <paramref name="ih"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="iw">Channel width.</param>
        /// <param name="ih">Channel height.</param>
        /// <param name="hsh">Horizontal shift.</param>
        /// <param name="vsh">Vertical shift.</param>
        /// <returns>Allocated <see cref="Channel"/> or error status.</returns>
        public static StatusOr<Channel> Create(
            JxlMemoryManager? memoryManager,
            nuint iw,
            nuint ih,
            int hsh = 0,
            int vsh = 0)
        {
            StatusOr<ImagePlane<int>> planeOr = ImagePlane<int>.Create(memoryManager, iw, ih);

            if (!planeOr.IsOk)
            {
                return planeOr.Status;
            }

            return new Channel(planeOr.Value, iw, ih, hsh, vsh);
        }

        /// <summary>
        /// Reallocates <see cref="Plane"/> if its dimensions do not match <see cref="W"/> and <see cref="H"/>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Shrink()
        {
            if (Plane.XSize == W && Plane.YSize == H)
            {
                return true;
            }

            JxlMemoryManager? mm = Plane.MemoryManager;
            Plane.Dispose();

            StatusOr<ImagePlane<int>> planeOr = ImagePlane<int>.Create(mm, W, H);

            if (!planeOr.IsOk)
            {
                return planeOr.Status;
            }

            Plane = planeOr.Value;

            return true;
        }

        /// <summary>
        /// Sets <see cref="W"/> and <see cref="H"/> to <paramref name="nw"/> and <paramref name="nh"/> and reallocates <see cref="Plane"/>.
        /// </summary>
        /// <param name="nw">New width.</param>
        /// <param name="nh">New height.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Shrink(int nw, int nh)
        {
            W = (nuint)nw;
            H = (nuint)nh;

            return Shrink();
        }

        /// <summary>
        /// Sets <see cref="W"/> and <see cref="H"/> to <paramref name="nw"/> and <paramref name="nh"/> and reallocates <see cref="Plane"/>.
        /// </summary>
        /// <param name="nw">New width.</param>
        /// <param name="nh">New height.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Shrink(nuint nw, nuint nh)
        {
            W = nw;
            H = nh;

            return Shrink();
        }

        /// <summary>
        /// Returns a writable span for row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<int> Row(nuint y) => new Span<int>(Plane.Row(y), (int)W);

        /// <summary>
        /// Returns a read-only span for row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ReadOnlySpan<int> ConstRow(nuint y) => new ReadOnlySpan<int>(Plane.ConstRow(y), (int)W);

        /// <inheritdoc />
        public void Dispose()
        {
            Plane.Dispose();
        }
    }

    /// <summary>
    /// Represents a Modular image composed of arbitrary-sized 32-bit integer <see cref="Channel"/>s
    /// (including meta-channels such as palettes) and a stack of reversible <see cref="Transform"/>s.
    /// Ported from <c>jxl::Image</c> in <c>lib/jxl/modular/modular_image.h</c> and <c>modular_image.cc</c>.
    /// </summary>
    public sealed class ModularImage : IDisposable
    {
        private readonly JxlMemoryManager? _memoryManager;

        // image data, transforms can dramatically change the number of channels and
        // their semantics
        /// <summary>List of meta-channels and image channels.</summary>
        public List<Channel> Channels = new List<Channel>();

        // transforms that have been applied (and that have to be undone)
        /// <summary>Transforms that have been applied and must be inverted during decoding.</summary>
        public List<Transform> Transforms = new List<Transform>();

        // image dimensions (channels may have different dimensions due to transforms)
        /// <summary>Nominal image width in pixels.</summary>
        public nuint W;

        /// <summary>Nominal image height in pixels.</summary>
        public nuint H;

        /// <summary>Nominal sample bit depth.</summary>
        public int BitDepth;

        /// <summary>Number of leading meta-channels (e.g. palettes).</summary>
        public nuint NbMetaChannels;

        /// <summary>True if a fatal error occurred during transform inversion.</summary>
        public bool Error;

        /// <summary>
        /// Initializes a new empty <see cref="ModularImage"/> with <see cref="Error"/> set to <c>true</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public ModularImage(JxlMemoryManager? memoryManager)
        {
            W               = 0;
            H               = 0;
            BitDepth        = 8;
            NbMetaChannels  = 0;
            Error           = true;
            _memoryManager  = memoryManager;
        }

        private ModularImage(JxlMemoryManager? memoryManager, nuint iw, nuint ih, int bitDepth)
        {
            W               = iw;
            H               = ih;
            BitDepth        = bitDepth;
            NbMetaChannels  = 0;
            Error           = false;
            _memoryManager  = memoryManager;
        }

        /// <summary>
        /// Gets the memory manager associated with this image.
        /// </summary>
        public JxlMemoryManager? MemoryManager => _memoryManager;

        /// <summary>
        /// Gets a value indicating whether all channels have zero area.
        /// </summary>
        public bool Empty
        {
            get
            {
                foreach (Channel ch in Channels)
                {
                    if (ch.W != 0 && ch.H != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Creates a new <see cref="ModularImage"/> with <paramref name="nbChans"/> channels of size <paramref name="iw"/> x <paramref name="ih"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="iw">Width in pixels.</param>
        /// <param name="ih">Height in pixels.</param>
        /// <param name="bitDepth">Sample bit depth.</param>
        /// <param name="nbChans">Number of channels to allocate.</param>
        /// <returns>Allocated <see cref="ModularImage"/> or error status.</returns>
        public static StatusOr<ModularImage> Create(
            JxlMemoryManager? memoryManager,
            nuint iw,
            nuint ih,
            int bitDepth,
            int nbChans)
        {
            ModularImage result = new ModularImage(memoryManager, iw, ih, bitDepth);

            for (int i = 0; i < nbChans; i++)
            {
                StatusOr<Channel> chOr = Channel.Create(memoryManager, iw, ih);

                if (!chOr.IsOk)
                {
                    result.Dispose();

                    return chOr.Status;
                }

                Channel c   = chOr.Value;
                c.Component = i;
                result.Channels.Add(c);
            }

            return result;
        }

        /// <summary>
        /// Deep-clones <paramref name="that"/> into a new <see cref="ModularImage"/>.
        /// </summary>
        /// <param name="that">Source image to clone.</param>
        /// <returns>Cloned <see cref="ModularImage"/> or error status.</returns>
        public static StatusOr<ModularImage> Clone(ModularImage that)
        {
            JxlMemoryManager? mm = that.MemoryManager;
            ModularImage clone   = new ModularImage(mm, that.W, that.H, that.BitDepth)
            {
                NbMetaChannels = that.NbMetaChannels,
                Error          = that.Error,
            };

            foreach (Transform t in that.Transforms)
            {
                clone.Transforms.Add(t.Clone());
            }

            foreach (Channel ch in that.Channels)
            {
                StatusOr<Channel> aOr = Channel.Create(mm, ch.W, ch.H, ch.HShift, ch.VShift);

                if (!aOr.IsOk)
                {
                    clone.Dispose();

                    return aOr.Status;
                }

                Channel a = aOr.Value;

                if (!ImageOps.CopyImageTo(ch.Plane, a.Plane))
                {
                    a.Dispose();
                    clone.Dispose();

                    return Status.Failure("Failed to copy modular channel");
                }

                a.Component = ch.Component;
                clone.Channels.Add(a);
            }

            return clone;
        }

        /// <summary>
        /// Inverts all transforms in <see cref="Transforms"/> in reverse order.
        /// </summary>
        /// <param name="wpHeader">Weighted predictor header parameters.</param>
        /// <param name="pool">Optional thread pool.</param>
        public void UndoTransforms(WeightedHeader wpHeader, ThreadPool? pool = null)
        {
            while (Transforms.Count > 0)
            {
                int lastIdx     = Transforms.Count - 1;
                Transform t     = Transforms[lastIdx];
                Status result   = t.Inverse(this, wpHeader, pool);

                if (!result.IsOk)
                {
                    Error = true;

                    return;
                }

                Transforms.RemoveAt(lastIdx);
            }
        }

        /// <summary>
        /// Returns a debug description of this image and its channels.
        /// </summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{W}x{H}, depth: {BitDepth}");

            if (Channels.Count > 0)
            {
                sb.Append(", channels:");

                for (int i = 0; i < Channels.Count; ++i)
                {
                    Channel ch = Channels[i];
                    sb.Append($" {ch.W}x{ch.H}(shift: {ch.HShift},{ch.VShift})");

                    if ((nuint)i < NbMetaChannels)
                    {
                        sb.Append('*');
                    }
                }
            }

            return sb.ToString();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (Channel ch in Channels)
            {
                ch.Dispose();
            }

            Channels.Clear();
        }
    }

    /// <summary>
    /// Decision node in a Meta-Adaptive (MA) context tree.
    /// Ported from <c>jxl::PropertyDecisionNode</c> in <c>lib/jxl/modular/encoding/dec_ma.h</c>.
    /// </summary>
    public struct PropertyDecisionNode
    {
        /// <summary>Threshold value for splitting (<c>property &gt; splitval</c> goes left).</summary>
        public int SplitVal;

        /// <summary>Property index to test, or -1 if this node is a leaf.</summary>
        public short Property;

        /// <summary>Left child index (or leaf ID if <see cref="Property"/> is -1).</summary>
        public uint LChild;

        /// <summary>Right child index.</summary>
        public uint RChild;

        /// <summary>Predictor used when this node is a leaf.</summary>
        public Predictor Predictor;

        /// <summary>Additive offset applied to the prediction when this node is a leaf.</summary>
        public long PredictorOffset;

        /// <summary>Multiplicative scale applied to the residual when this node is a leaf.</summary>
        public uint Multiplier;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyDecisionNode"/> struct.
        /// </summary>
        /// <param name="p">Property index (-1 for leaf).</param>
        /// <param name="splitVal">Split value.</param>
        /// <param name="lchild">Left child index or leaf context ID.</param>
        /// <param name="rchild">Right child index.</param>
        /// <param name="predictor">Leaf predictor.</param>
        /// <param name="predictorOffset">Leaf offset.</param>
        /// <param name="multiplier">Leaf multiplier.</param>
        public PropertyDecisionNode(
            int p,
            int splitVal,
            int lchild,
            int rchild,
            Predictor predictor,
            long predictorOffset,
            uint multiplier)
        {
            SplitVal        = splitVal;
            Property        = (short)p;
            LChild          = (uint)lchild;
            RChild          = (uint)rchild;
            Predictor       = predictor;
            PredictorOffset = predictorOffset;
            Multiplier      = multiplier;
        }

        /// <summary>
        /// Creates a default leaf node with <see cref="Predictor.Zero"/>.
        /// </summary>
        /// <returns>Default leaf node.</returns>
        public static PropertyDecisionNode Default()
        {
            return new PropertyDecisionNode(-1, 0, 0, 0, Predictor.Zero, 0, 1);
        }

        /// <summary>
        /// Creates a leaf <see cref="PropertyDecisionNode"/>.
        /// </summary>
        /// <param name="predictor">Predictor mode.</param>
        /// <param name="offset">Predictor offset.</param>
        /// <param name="multiplier">Residual multiplier.</param>
        /// <returns>Leaf node.</returns>
        public static PropertyDecisionNode Leaf(Predictor predictor, long offset = 0, uint multiplier = 1)
        {
            return new PropertyDecisionNode(-1, 0, 0, 0, predictor, offset, multiplier);
        }

        /// <summary>
        /// Creates an internal split <see cref="PropertyDecisionNode"/>.
        /// </summary>
        /// <param name="p">Property index.</param>
        /// <param name="splitVal">Split threshold.</param>
        /// <param name="lchild">Left child node index.</param>
        /// <param name="rchild">Right child node index (defaults to <c>lchild + 1</c> if -1).</param>
        /// <returns>Split node.</returns>
        public static PropertyDecisionNode Split(int p, int splitVal, int lchild, int rchild = -1)
        {
            if (rchild == -1)
            {
                rchild = lchild + 1;
            }

            return new PropertyDecisionNode(p, splitVal, lchild, rchild, Predictor.Zero, 0, 1);
        }
    }

    /// <summary>
    /// Decoding and validation methods for Modular Meta-Adaptive (MA) trees.
    /// Ported from <c>lib/jxl/modular/encoding/dec_ma.cc</c>.
    /// </summary>
    public static class ModularMa
    {
        private const int KHeightLimit = 2048;

        private enum NextAction
        {
            CheckAndGoLeft,
            GoRight,
            Pop,
        }

        private struct WorkItem
        {
            public int NodeIndex;
            public int OrigL;
            public int OrigU;
            public NextAction Action;
        }

        /// <summary>
        /// Decodes an MA <paramref name="tree"/> from <paramref name="br"/> and validates its height and split ranges.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="br">Source bit reader.</param>
        /// <param name="tree">Destination list of <see cref="PropertyDecisionNode"/>.</param>
        /// <param name="treeSizeLimit">Maximum number of nodes allowed.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeTree(
            JxlMemoryManager? memoryManager,
            BitReader br,
            List<PropertyDecisionNode> tree,
            nuint treeSizeLimit)
        {
            ANSCode treeCode = new ANSCode();
            Status histOk    = DecAns.DecodeHistograms(
                memoryManager,
                br,
                (nuint)MATreeContext.NumTreeContexts,
                treeCode,
                out byte[] treeContextMap);

            if (!histOk.IsOk)
            {
                return histOk;
            }

            // TODO(eustas): investigate more infinite tree cases.
            if (treeCode.DegenerateSymbols[treeContextMap[(int)MATreeContext.PropertyContext]] > 0)
            {
                return Status.Failure("Infinite tree");
            }

            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(treeCode, br);

            if (!readerOr.IsOk)
            {
                return readerOr.Status;
            }

            ANSSymbolReader reader = readerOr.Value;
            nuint limit            = Math.Min(treeSizeLimit, ModularOptions.KMaxTreeSize);
            Status decodeOk        = DecodeTreeNodes(br, reader, treeContextMap, tree, limit);

            if (!decodeOk.IsOk)
            {
                return decodeOk;
            }

            if (!reader.CheckANSFinalState())
            {
                return Status.Failure("ANS decode final state failed");
            }

            return true;
        }

        /// <summary>
        /// Validates that <paramref name="tree"/> has depth at most <c>2048</c> and non-empty intervals at every split.
        /// </summary>
        /// <param name="tree">MA decision tree to validate.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status ValidateTree(IReadOnlyList<PropertyDecisionNode> tree)
        {
            if (tree.Count == 0)
            {
                return true;
            }

            int numProperties = 0;

            for (int i = 0; i < tree.Count; i++)
            {
                if (tree[i].Property >= numProperties)
                {
                    numProperties = tree[i].Property + 1;
                }
            }

            (int First, int Second)[] propertyRanges = new (int First, int Second)[numProperties];

            for (int i = 0; i < numProperties; i++)
            {
                propertyRanges[i] = (int.MinValue, int.MaxValue);
            }

            List<WorkItem> stack = new List<WorkItem>
            {
                new WorkItem
                {
                    NodeIndex = 0,
                    OrigL     = 0,
                    OrigU     = 0,
                    Action    = NextAction.CheckAndGoLeft,
                },
            };

            while (stack.Count > 0)
            {
                if (stack.Count >= KHeightLimit)
                {
                    return Status.Failure("Tree too tall");
                }

                Status stepOk = StepValidateTree(tree, propertyRanges, stack);

                if (!stepOk.IsOk)
                {
                    return stepOk;
                }
            }

            return true;
        }

        private static Status StepValidateTree(
            IReadOnlyList<PropertyDecisionNode> tree,
            (int First, int Second)[] propertyRanges,
            List<WorkItem> stack)
        {
            int topIdx                = stack.Count - 1;
            WorkItem item             = stack[topIdx];
            PropertyDecisionNode node = tree[item.NodeIndex];

            if (item.Action == NextAction.CheckAndGoLeft)
            {
                short p = node.Property;

                if (p == -1)
                {
                    stack.RemoveAt(topIdx);

                    return true;
                }

                int v = node.SplitVal;
                int l = propertyRanges[p].First;
                int u = propertyRanges[p].Second;

                if (l > v || u <= v)
                {
                    return Status.Failure("Invalid tree");
                }

                item.OrigL               = l;
                item.OrigU               = u;
                item.Action              = NextAction.GoRight;
                stack[topIdx]            = item;
                propertyRanges[p].First  = node.SplitVal + 1;

                stack.Add(new WorkItem
                {
                    NodeIndex = (int)node.LChild,
                    OrigL     = 0,
                    OrigU     = 0,
                    Action    = NextAction.CheckAndGoLeft,
                });

                return true;
            }

            if (item.Action == NextAction.GoRight)
            {
                item.Action                     = NextAction.Pop;
                stack[topIdx]                   = item;
                propertyRanges[node.Property]   = (item.OrigL, node.SplitVal);

                stack.Add(new WorkItem
                {
                    NodeIndex = (int)node.RChild,
                    OrigL     = 0,
                    OrigU     = 0,
                    Action    = NextAction.CheckAndGoLeft,
                });

                return true;
            }

            propertyRanges[node.Property].Second = item.OrigU;
            stack.RemoveAt(topIdx);

            return true;
        }

        private static Status DecodeTreeNodes(
            BitReader br,
            ANSSymbolReader reader,
            ReadOnlySpan<byte> contextMap,
            List<PropertyDecisionNode> tree,
            nuint treeSizeLimit)
        {
            nuint leafId   = 0;
            nuint toDecode = 1;
            tree.Clear();

            while (toDecode > 0)
            {
                if (!br.AllReadsWithinBounds())
                {
                    return Status.Failure("BitReader out of bounds while decoding MA tree");
                }

                if ((nuint)tree.Count > treeSizeLimit)
                {
                    return Status.Failure($"Tree is too large: {tree.Count} nodes vs {treeSizeLimit} max nodes");
                }

                toDecode--;
                uint prop1 = reader.ReadHybridUint((uint)MATreeContext.PropertyContext, br, contextMap);

                if (prop1 > 256)
                {
                    return Status.Failure("Invalid tree property value");
                }

                int property = (int)prop1 - 1;

                if (property == -1)
                {
                    Status leafOk = DecodeLeafNode(br, reader, contextMap, tree, ref leafId);

                    if (!leafOk.IsOk)
                    {
                        return leafOk;
                    }

                    continue;
                }

                int splitVal = PackSigned.Unpack(
                    reader.ReadHybridUint((uint)MATreeContext.SplitValContext, br, contextMap));
                int lchild   = tree.Count + (int)toDecode + 1;
                int rchild   = tree.Count + (int)toDecode + 2;

                tree.Add(new PropertyDecisionNode(property, splitVal, lchild, rchild, Predictor.Zero, 0, 1));
                toDecode += 2;
            }

            return ValidateTree(tree);
        }

        private static Status DecodeLeafNode(
            BitReader br,
            ANSSymbolReader reader,
            ReadOnlySpan<byte> contextMap,
            List<PropertyDecisionNode> tree,
            ref nuint leafId)
        {
            uint predictor = reader.ReadHybridUint((uint)MATreeContext.PredictorContext, br, contextMap);

            if (predictor >= ModularOptions.KNumModularPredictors)
            {
                return Status.Failure("Invalid predictor");
            }

            long predictorOffset = PackSigned.Unpack(
                reader.ReadHybridUint((uint)MATreeContext.OffsetContext, br, contextMap));
            uint mulLog = reader.ReadHybridUint((uint)MATreeContext.MultiplierLogContext, br, contextMap);

            if (mulLog >= 31)
            {
                return Status.Failure("Invalid multiplier logarithm");
            }

            uint mulBits = reader.ReadHybridUint((uint)MATreeContext.MultiplierBitsContext, br, contextMap);

            if (mulBits >= (1u << (int)(31u - mulLog)) - 1u)
            {
                return Status.Failure("Invalid multiplier");
            }

            uint multiplier = (mulBits + 1u) << (int)mulLog;
            Predictor p     = (Predictor)predictor;

            tree.Add(new PropertyDecisionNode(-1, 0, (int)leafId, 0, p, predictorOffset, multiplier));
            leafId++;

            return true;
        }
    }
}
