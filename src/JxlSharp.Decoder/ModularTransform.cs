// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Modular transform identifiers.
    /// Ported from <c>jxl::TransformId</c> in <c>lib/jxl/modular/transform/transform.h</c>.
    /// </summary>
    public enum TransformId : uint
    {
        /// <summary>Reversible Color Transform: G, R-G, B-G and variants (including YCoCg).</summary>
        RCT = 0,

        /// <summary>Color palette transform: [begin_c] [num_c] [nb_colors] [nb_deltas].</summary>
        Palette = 1,

        /// <summary>Haar-style squeezing transform.</summary>
        Squeeze = 2,

        /// <summary>Invalid transform sentinel.</summary>
        Invalid = 3,
    }

    /// <summary>
    /// Parameters for a single Haar-like squeeze step.
    /// Ported from <c>jxl::SqueezeParams</c> in <c>lib/jxl/modular/transform/squeeze_params.h</c> and <c>squeeze_params.cc</c>.
    /// </summary>
    public sealed class SqueezeParams : Fields
    {
        /// <summary>Whether the squeeze is horizontal (<c>true</c>) or vertical (<c>false</c>).</summary>
        public bool Horizontal;

        /// <summary>Whether residual channels are placed immediately after the squeezed channels (<c>true</c>) or at the end (<c>false</c>).</summary>
        public bool InPlace;

        /// <summary>First channel index to squeeze.</summary>
        public uint BeginC;

        /// <summary>Number of consecutive channels to squeeze.</summary>
        public uint NumC;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqueezeParams"/> class with default values.
        /// </summary>
        public SqueezeParams()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "SqueezeParams";

        /// <summary>
        /// Creates a copy of this <see cref="SqueezeParams"/> instance.
        /// </summary>
        /// <returns>Cloned <see cref="SqueezeParams"/>.</returns>
        public SqueezeParams Clone()
        {
            return new SqueezeParams
            {
                Horizontal = Horizontal,
                InPlace    = InPlace,
                BeginC     = BeginC,
                NumC       = NumC,
            };
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.Bool(false, ref Horizontal))
            {
                return false;
            }

            if (!visitor.Bool(false, ref InPlace))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Bits(3),
                U32Distr.BitsOffset(6, 8),
                U32Distr.BitsOffset(10, 72),
                U32Distr.BitsOffset(13, 1096),
                0,
                ref BeginC))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Val(1),
                U32Distr.Val(2),
                U32Distr.Val(3),
                U32Distr.BitsOffset(4, 4),
                2,
                ref NumC))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Represents a reversible Modular transform (RCT, Palette, or Squeeze).
    /// Ported from <c>jxl::Transform</c> in <c>lib/jxl/modular/transform/transform.h</c> and <c>transform.cc</c>.
    /// </summary>
    public sealed class Transform : Fields
    {
        /// <summary>Transform type ID.</summary>
        public TransformId Id;

        /// <summary>Starting channel index (for Palette and RCT).</summary>
        public uint BeginC;

        /// <summary>RCT variant (0..41, default 6 = YCoCg).</summary>
        public uint RctType;

        /// <summary>Number of channels transformed (for Palette and NearLossless).</summary>
        public uint NumC;

        /// <summary>Number of palette colors.</summary>
        public uint NbColors;

        /// <summary>Number of delta palette entries.</summary>
        public uint NbDeltas;

        /// <summary>Squeeze step parameters (default squeeze if empty).</summary>
        public List<SqueezeParams> Squeezes = new List<SqueezeParams>();

        /// <summary>Maximum delta error for NearLossless (not serialized).</summary>
        public int MaxDeltaError;

        /// <summary>Predictor used for delta palette entries.</summary>
        public Predictor Predictor;

        /// <summary>Whether the palette is ordered (encoder-only, not serialized).</summary>
        public bool OrderedPalette = true;

        /// <summary>Whether the palette is lossy (encoder-only, not serialized).</summary>
        public bool LossyPalette;

        /// <summary>
        /// Initializes a new instance of the <see cref="Transform"/> class with <see cref="TransformId.Invalid"/>.
        /// </summary>
        public Transform()
            : this(TransformId.Invalid)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Transform"/> class with the specified <paramref name="id"/>.
        /// </summary>
        /// <param name="id">Transform type ID.</param>
        public Transform(TransformId id)
        {
            Bundle.Init(this);
            Id = id;
        }

        /// <inheritdoc />
        public override string BundleName => "Transform";

        /// <summary>
        /// Creates a deep copy of this <see cref="Transform"/>.
        /// </summary>
        /// <returns>Cloned <see cref="Transform"/>.</returns>
        public Transform Clone()
        {
            Transform copy = new Transform(Id)
            {
                BeginC         = BeginC,
                RctType        = RctType,
                NumC           = NumC,
                NbColors       = NbColors,
                NbDeltas       = NbDeltas,
                MaxDeltaError  = MaxDeltaError,
                Predictor      = Predictor,
                OrderedPalette = OrderedPalette,
                LossyPalette   = LossyPalette,
            };

            foreach (SqueezeParams sp in Squeezes)
            {
                copy.Squeezes.Add(sp.Clone());
            }

            return copy;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            uint idVal = (uint)Id;

            if (!visitor.U32(
                U32Distr.Val((uint)TransformId.RCT),
                U32Distr.Val((uint)TransformId.Palette),
                U32Distr.Val((uint)TransformId.Squeeze),
                U32Distr.Val((uint)TransformId.Invalid),
                (uint)TransformId.RCT,
                ref idVal))
            {
                return false;
            }

            Id = (TransformId)idVal;

            if (Id == TransformId.Invalid)
            {
                return Status.Failure("Invalid transform ID");
            }

            if (visitor.Conditional(Id == TransformId.RCT || Id == TransformId.Palette) &&
                !visitor.U32(
                    U32Distr.Bits(3),
                    U32Distr.BitsOffset(6, 8),
                    U32Distr.BitsOffset(10, 72),
                    U32Distr.BitsOffset(13, 1096),
                    0,
                    ref BeginC))
            {
                return false;
            }

            if (visitor.Conditional(Id == TransformId.RCT) && !VisitRctFields(visitor))
            {
                return false;
            }

            if (visitor.Conditional(Id == TransformId.Palette) && !VisitPaletteFields(visitor))
            {
                return false;
            }

            if (visitor.Conditional(Id == TransformId.Squeeze))
            {
                return VisitSqueezeFields(visitor);
            }

            return true;
        }

        /// <summary>
        /// Applies the inverse of this transform to <paramref name="input"/>.
        /// </summary>
        /// <param name="input">Modular image to transform in-place.</param>
        /// <param name="wpHeader">Weighted predictor header.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Inverse(ModularImage input, WeightedHeader wpHeader, ThreadPool? pool = null)
        {
            switch (Id)
            {
                case TransformId.RCT:
                    return ModularTransformOps.InvRCT(input, BeginC, RctType, pool);
                case TransformId.Squeeze:
                    return ModularTransformOps.InvSqueeze(input, Squeezes, pool);
                case TransformId.Palette:
                    return ModularTransformOps.InvPalette(input, this, wpHeader, pool);
                default:
                    return Status.Failure($"Unknown transformation (ID={(uint)Id})");
            }
        }

        /// <summary>
        /// Applies the metadata/channel-shape effect of this transform prior to entropy decoding.
        /// </summary>
        /// <param name="input">Modular image to update.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status MetaApply(ModularImage input)
        {
            switch (Id)
            {
                case TransformId.RCT:
                    return ModularTransformOps.CheckEqualChannels(input, BeginC, BeginC + 2u);
                case TransformId.Squeeze:
                    return ModularTransformOps.MetaSqueeze(input, Squeezes);
                case TransformId.Palette:
                    return ModularTransformOps.MetaPalette(
                        input,
                        BeginC,
                        BeginC + NumC - 1u,
                        NbColors,
                        NbDeltas,
                        LossyPalette);
                default:
                    return Status.Failure($"Unknown transformation (ID={(uint)Id})");
            }
        }

        private Status VisitRctFields(Visitor visitor)
        {
            // 0-41, default YCoCg.
            if (!visitor.U32(
                U32Distr.Val(6),
                U32Distr.Bits(2),
                U32Distr.BitsOffset(4, 2),
                U32Distr.BitsOffset(6, 10),
                6,
                ref RctType))
            {
                return false;
            }

            if (RctType >= 42)
            {
                return Status.Failure("Invalid transform RCT type");
            }

            return true;
        }

        private Status VisitPaletteFields(Visitor visitor)
        {
            if (!visitor.U32(
                U32Distr.Val(1),
                U32Distr.Val(3),
                U32Distr.Val(4),
                U32Distr.BitsOffset(13, 1),
                3,
                ref NumC))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.BitsOffset(8, 0),
                U32Distr.BitsOffset(10, 256),
                U32Distr.BitsOffset(12, 1280),
                U32Distr.BitsOffset(16, 5376),
                256,
                ref NbColors))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.BitsOffset(8, 1),
                U32Distr.BitsOffset(10, 257),
                U32Distr.BitsOffset(16, 1281),
                0,
                ref NbDeltas))
            {
                return false;
            }

            uint predVal = (uint)Predictor;

            if (!visitor.Bits(4, (uint)Predictor.Zero, ref predVal))
            {
                return false;
            }

            Predictor = (Predictor)predVal;

            if (Predictor >= Predictor.Best)
            {
                return Status.Failure("Invalid predictor");
            }

            return true;
        }

        private Status VisitSqueezeFields(Visitor visitor)
        {
            uint numSqueezes = (uint)Squeezes.Count;

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.BitsOffset(4, 1),
                U32Distr.BitsOffset(6, 9),
                U32Distr.BitsOffset(8, 41),
                0,
                ref numSqueezes))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                Squeezes.Clear();

                for (uint i = 0; i < numSqueezes; i++)
                {
                    Squeezes.Add(new SqueezeParams());
                }
            }

            for (int i = 0; i < (int)numSqueezes; i++)
            {
                if (!visitor.VisitNested(Squeezes[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Implementations of Modular transforms: Reversible Color Transform (RCT), Palette, and Squeeze.
    /// Ported from <c>lib/jxl/modular/transform/transform.cc</c>, <c>rct.cc</c>, <c>palette.cc</c>, and <c>squeeze.cc</c>.
    /// </summary>
    public static class ModularTransformOps
    {
        /// <summary>Maximum preview dimension produced by default squeeze parameters (8).</summary>
        public const nuint KMaxFirstPreviewSize = 8;

        private const int KRgbChannels = 3;
        private const int KLargeCube = 5;
        private const int KSmallCube = 4;
        private const int KSmallCubeBits = 2;
        private const int KLargeCubeOffset = KSmallCube * KSmallCube * KSmallCube;
        private const int KDeltaPaletteEntries = 72;

        private static readonly int[] KDeltaPalette =
        {
            0,   0,   0,   4,   4,   4,   11,  0,   0,   0,   0,   -13, 0,   -12, 0,   -10, -10, -10,
            -18, -18, -18, -27, -27, -27, -18, -18, 0,   0,   0,   -32, -32, 0,   0,   -37, -37, -37,
            0,   -32, -32, 24,  24,  45,  50,  50,  50,  -45, -24, -24, -24, -45, -45, 0,   -24, -24,
            -34, -34, 0,   -24, 0,   -24, -45, -45, -24, 64,  64,  64,  -32, 0,   -32, 0,   -32, 0,
            -32, 0,   32,  -24, -45, -24, 45,  24,  45,  24,  -24, -45, -45, -24, 24,  80,  80,  80,
            64,  0,   0,   0,   0,   -64, 0,   -64, -64, -24, -24, 45,  96,  96,  96,  64,  64,  0,
            45,  -24, -24, 34,  -34, 0,   112, 112, 112, 24,  -45, -45, 45,  45,  -24, 0,   -32, 32,
            24,  -24, 45,  0,   96,  96,  45,  -24, 24,  24,  -45, -24, -24, -45, 24,  0,   -64, 0,
            96,  0,   0,   128, 128, 128, 64,  0,   64,  144, 144, 144, 96,  96,  0,   -36, -36, 36,
            45,  -24, -45, 45,  -45, -24, 0,   0,   -96, 0,   128, 128, 0,   96,  0,   45,  24,  -45,
            -128, 0,  0,   24,  -45, 24,  -45, 24,  -45, 64,  0,   -64, 64,  -64, -64, 96,  0,   96,
            45,  -45, 24,  24,  45,  -45, 64,  64,  -64, 128, 128, 0,   0,   0,   -128, -24, 45, -45,
        };

        /// <summary>
        /// Performs wrapping 32-bit signed integer addition matching <c>jxl::PixelAdd</c>.
        /// </summary>
        /// <param name="a">First operand.</param>
        /// <param name="b">Second operand.</param>
        /// <returns><c>unchecked(a + b)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PixelAdd(int a, int b)
        {
            return unchecked((int)((uint)a + (uint)b));
        }

        /// <summary>
        /// Verifies that channels <paramref name="c1"/> through <paramref name="c2"/> exist, belong to the same
        /// meta/non-meta group, and have identical dimensions and shifts.
        /// Ported from <c>jxl::CheckEqualChannels</c>.
        /// </summary>
        /// <param name="image">Modular image.</param>
        /// <param name="c1">Start channel index.</param>
        /// <param name="c2">End channel index (inclusive).</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status CheckEqualChannels(ModularImage image, uint c1, uint c2)
        {
            int count = image.Channels.Count;

            if (c1 > (uint)count || c2 >= (uint)count || c2 < c1)
            {
                return Status.Failure($"Invalid channel range: {c1}..{c2} (there are only {count} channels)");
            }

            if (c1 < image.NbMetaChannels && c2 >= image.NbMetaChannels)
            {
                return Status.Failure("Invalid: transforming mix of meta and nonmeta");
            }

            Channel ch1 = image.Channels[(int)c1];

            for (uint c = c1 + 1u; c <= c2; c++)
            {
                Channel ch2 = image.Channels[(int)c];

                if (ch1.W != ch2.W || ch1.H != ch2.H || ch1.HShift != ch2.HShift || ch1.VShift != ch2.VShift)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Inverts a Reversible Color Transform (RCT) on channels <c>beginC..beginC+2</c>.
        /// Ported from <c>jxl::InvRCT</c> in <c>lib/jxl/modular/transform/rct.cc</c>.
        /// </summary>
        /// <param name="input">Modular image.</param>
        /// <param name="beginC">First channel index.</param>
        /// <param name="rctType">RCT type (0..41).</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InvRCT(ModularImage input, uint beginC, uint rctType, ThreadPool? pool = null)
        {
            Status checkOk = CheckEqualChannels(input, beginC, beginC + 2u);

            if (!checkOk.IsOk)
            {
                return checkOk;
            }

            if (rctType == 0)
            {
                return true;
            }

            int m           = (int)beginC;
            int permutation = (int)(rctType / 7u);

            if (permutation >= 6)
            {
                return Status.Failure("Invalid RCT permutation");
            }

            int custom = (int)(rctType % 7u);
            int idx0   = m + (permutation % 3);
            int idx1   = m + ((permutation + 1 + (permutation / 3)) % 3);
            int idx2   = m + ((permutation + 2 - (permutation / 3)) % 3);

            if (custom == 0)
            {
                Channel ch0          = input.Channels[m];
                Channel ch1          = input.Channels[m + 1];
                Channel ch2          = input.Channels[m + 2];
                input.Channels[idx0] = ch0;
                input.Channels[idx1] = ch1;
                input.Channels[idx2] = ch2;

                return true;
            }

            nuint h = input.Channels[m].H;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)h,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    InvRCTRow(input, m, idx0, idx1, idx2, custom, (nuint)task);

                    return true;
                },
                "InvRCT");
        }

        /// <summary>
        /// Evaluates a palette entry (including implicit cube and delta entries).
        /// Ported from <c>jxl::palette_internal::GetPaletteValue</c> in <c>lib/jxl/modular/transform/palette.h</c>.
        /// </summary>
        /// <param name="palette">Palette channel.</param>
        /// <param name="index">Palette index (may be negative or exceed explicit palette width).</param>
        /// <param name="c">Channel offset within the palette.</param>
        /// <param name="paletteSize">Explicit palette width (<c>palette.W</c>).</param>
        /// <param name="bitDepth">Clamped sample bit depth (<c>min(bitdepth, 24)</c>).</param>
        /// <returns>Palette sample value.</returns>
        public static int GetPaletteValue(
            Channel palette,
            int index,
            int c,
            int paletteSize,
            int bitDepth)
        {
            if (index < 0)
            {
                return GetDeltaPaletteValue(index, c, bitDepth);
            }

            if (index >= paletteSize)
            {
                return GetImplicitPaletteValue(index, c, paletteSize, bitDepth);
            }

            return palette.ConstRow((nuint)c)[index];
        }

        /// <summary>
        /// Applies the metadata/channel-shape update for a Palette transform.
        /// Ported from <c>jxl::MetaPalette</c> in <c>lib/jxl/modular/transform/palette.cc</c>.
        /// </summary>
        /// <param name="input">Modular image.</param>
        /// <param name="beginC">Start channel index.</param>
        /// <param name="endC">End channel index (inclusive).</param>
        /// <param name="nbColors">Number of explicit palette colors.</param>
        /// <param name="nbDeltas">Number of delta palette entries.</param>
        /// <param name="lossy">Whether the palette is lossy.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status MetaPalette(
            ModularImage input,
            uint beginC,
            uint endC,
            uint nbColors,
            uint nbDeltas,
            bool lossy)
        {
            _              = lossy;
            Status checkOk = CheckEqualChannels(input, beginC, endC);

            if (!checkOk.IsOk)
            {
                return checkOk;
            }

            nuint nb = endC - beginC + 1u;

            if (beginC >= input.NbMetaChannels)
            {
                input.NbMetaChannels++;
            }
            else
            {
                if (endC >= input.NbMetaChannels)
                {
                    return Status.Failure("Invalid MetaPalette channel range");
                }

                input.NbMetaChannels = (nuint)((long)input.NbMetaChannels + 2L - (long)nb);
            }

            for (uint idx = beginC + 1u; idx <= endC; idx++)
            {
                input.Channels[(int)(beginC + 1u)].Dispose();
                input.Channels.RemoveAt((int)(beginC + 1u));
            }

            StatusOr<Channel> pchOr = Channel.Create(input.MemoryManager, nbColors + nbDeltas, nb, -1, -1);

            if (!pchOr.IsOk)
            {
                return pchOr.Status;
            }

            input.Channels.Insert(0, pchOr.Value);

            return true;
        }

        /// <summary>
        /// Inverts a Palette transform on <paramref name="input"/>.
        /// Ported from <c>jxl::InvPalette</c> in <c>lib/jxl/modular/transform/palette.cc</c>.
        /// </summary>
        /// <param name="input">Modular image.</param>
        /// <param name="transform">Palette transform parameters.</param>
        /// <param name="wpHeader">Weighted predictor header.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InvPalette(
            ModularImage input,
            Transform transform,
            WeightedHeader wpHeader,
            ThreadPool? pool = null)
        {
            if (input.NbMetaChannels < 1)
            {
                return Status.Failure("Error: Palette transform without palette.");
            }

            int nb  = (int)input.Channels[0].H;
            int c0  = (int)transform.BeginC + 1;

            if (c0 >= input.Channels.Count || nb < 1)
            {
                return Status.Failure("Channel is out of range or corrupted palette.");
            }

            nuint w = input.Channels[c0].W;
            nuint h = input.Channels[c0].H;

            for (int i = 1; i < nb; i++)
            {
                StatusOr<Channel> cOr = Channel.Create(
                    input.MemoryManager,
                    w,
                    h,
                    input.Channels[c0].HShift,
                    input.Channels[c0].VShift);

                if (!cOr.IsOk)
                {
                    return cOr.Status;
                }

                input.Channels.Insert(c0 + 1, cOr.Value);
            }

            Status execOk = ExecuteInvPalette(input, transform, wpHeader, pool, c0, nb);

            if (!execOk.IsOk)
            {
                return execOk;
            }

            return FinalizeInvPaletteMetaChannels(input, transform.BeginC, c0, nb);
        }

        /// <summary>
        /// Computes the smooth tendency term for Haar-like unsqueezing.
        /// Ported from <c>jxl::SmoothTendency</c> in <c>lib/jxl/modular/transform/squeeze.h</c>.
        /// </summary>
        /// <param name="b">Previous reconstructed pixel (B).</param>
        /// <param name="a">Current average (a).</param>
        /// <param name="n">Next average (n).</param>
        /// <returns>Estimated smooth difference <c>C - D</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long SmoothTendency(long b, long a, long n)
        {
            long diff = 0;

            if (b >= a && a >= n)
            {
                diff = ((4 * b) - (3 * n) - a + 6) / 12;

                if (diff - (diff & 1) > 2 * (b - a))
                {
                    diff = (2 * (b - a)) + 1;
                }

                if (diff + (diff & 1) > 2 * (a - n))
                {
                    diff = 2 * (a - n);
                }
            }
            else if (b <= a && a <= n)
            {
                diff = ((4 * b) - (3 * n) - a - 6) / 12;

                if (diff + (diff & 1) < 2 * (b - a))
                {
                    diff = (2 * (b - a)) - 1;
                }

                if (diff - (diff & 1) < 2 * (a - n))
                {
                    diff = 2 * (a - n);
                }
            }

            return diff;
        }

        /// <summary>
        /// Populates <paramref name="parameters"/> with the default progressive Haar squeeze schedule for <paramref name="image"/>.
        /// Ported from <c>jxl::DefaultSqueezeParameters</c> in <c>lib/jxl/modular/transform/squeeze.cc</c>.
        /// </summary>
        /// <param name="parameters">Destination list of <see cref="SqueezeParams"/>.</param>
        /// <param name="image">Source modular image.</param>
        public static void DefaultSqueezeParameters(List<SqueezeParams> parameters, ModularImage image)
        {
            int metaCount  = (int)image.NbMetaChannels;
            int nbChannels = image.Channels.Count - metaCount;
            parameters.Clear();

            nuint w   = image.Channels[metaCount].W;
            nuint h   = image.Channels[metaCount].H;
            bool wide = w > h;

            if (nbChannels > 2 &&
                image.Channels[metaCount + 1].W == w &&
                image.Channels[metaCount + 1].H == h)
            {
                parameters.Add(new SqueezeParams
                {
                    Horizontal = true,
                    InPlace    = false,
                    BeginC     = (uint)(metaCount + 1),
                    NumC       = 2,
                });
                parameters.Add(new SqueezeParams
                {
                    Horizontal = false,
                    InPlace    = false,
                    BeginC     = (uint)(metaCount + 1),
                    NumC       = 2,
                });
            }

            if (!wide && h > KMaxFirstPreviewSize)
            {
                parameters.Add(CreateInPlaceSqueeze(false, (uint)metaCount, (uint)nbChannels));
                h = (h + 1u) / 2u;
            }

            while (w > KMaxFirstPreviewSize || h > KMaxFirstPreviewSize)
            {
                if (w > KMaxFirstPreviewSize)
                {
                    parameters.Add(CreateInPlaceSqueeze(true, (uint)metaCount, (uint)nbChannels));
                    w = (w + 1u) / 2u;
                }

                if (h > KMaxFirstPreviewSize)
                {
                    parameters.Add(CreateInPlaceSqueeze(false, (uint)metaCount, (uint)nbChannels));
                    h = (h + 1u) / 2u;
                }
            }
        }

        /// <summary>
        /// Checks whether <paramref name="parameter"/> specifies a valid channel range within <paramref name="numChannels"/>.
        /// Ported from <c>jxl::CheckMetaSqueezeParams</c>.
        /// </summary>
        /// <param name="parameter">Squeeze step parameters.</param>
        /// <param name="numChannels">Current number of channels in the image.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid.</returns>
        public static Status CheckMetaSqueezeParams(SqueezeParams parameter, int numChannels)
        {
            long c1 = parameter.BeginC;
            long c2 = (long)parameter.BeginC + parameter.NumC - 1L;

            if (c1 < 0 || c1 >= numChannels || c2 < 0 || c2 >= numChannels || c2 < c1)
            {
                return Status.Failure("Invalid channel range");
            }

            return true;
        }

        /// <summary>
        /// Applies the metadata/channel-shape update for a Squeeze transform.
        /// Ported from <c>jxl::MetaSqueeze</c> in <c>lib/jxl/modular/transform/squeeze.cc</c>.
        /// </summary>
        /// <param name="image">Modular image.</param>
        /// <param name="parameters">Squeeze parameters (populated with defaults if empty).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status MetaSqueeze(ModularImage image, List<SqueezeParams> parameters)
        {
            if (parameters.Count == 0)
            {
                DefaultSqueezeParameters(parameters, image);
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                Status stepOk = ApplyMetaSqueezeStep(image, parameters[i]);

                if (!stepOk.IsOk)
                {
                    return stepOk;
                }
            }

            return true;
        }

        /// <summary>
        /// Inverts a sequence of Haar-like squeeze transforms in reverse order.
        /// Ported from <c>jxl::InvSqueeze</c> in <c>lib/jxl/modular/transform/squeeze.cc</c>.
        /// </summary>
        /// <param name="input">Modular image.</param>
        /// <param name="parameters">Squeeze steps.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InvSqueeze(
            ModularImage input,
            IReadOnlyList<SqueezeParams> parameters,
            ThreadPool? pool = null)
        {
            for (int i = parameters.Count - 1; i >= 0; i--)
            {
                Status stepOk = ApplyInvSqueezeStep(input, parameters[i], pool);

                if (!stepOk.IsOk)
                {
                    return stepOk;
                }
            }

            return true;
        }

        private static void InvRCTRow(
            ModularImage input,
            int m,
            int idx0,
            int idx1,
            int idx2,
            int custom,
            nuint y)
        {
            int w                 = (int)input.Channels[m].W;
            ReadOnlySpan<int> in0 = input.Channels[m].ConstRow(y);
            ReadOnlySpan<int> in1 = input.Channels[m + 1].ConstRow(y);
            ReadOnlySpan<int> in2 = input.Channels[m + 2].ConstRow(y);
            Span<int> out0        = input.Channels[idx0].Row(y);
            Span<int> out1        = input.Channels[idx1].Row(y);
            Span<int> out2        = input.Channels[idx2].Row(y);

            if (custom == 6)
            {
                for (int x = 0; x < w; x++)
                {
                    int yVal = in0[x];
                    int co   = in1[x];
                    int cg   = in2[x];
                    int tmp  = PixelAdd(yVal, -(cg >> 1));
                    int g    = PixelAdd(cg, tmp);
                    int b    = PixelAdd(tmp, -(co >> 1));
                    int r    = PixelAdd(b, co);
                    out0[x]  = r;
                    out1[x]  = g;
                    out2[x]  = b;
                }

                return;
            }

            int second = custom >> 1;
            int third  = custom & 1;

            for (int x = 0; x < w; x++)
            {
                int firstVal  = in0[x];
                int secondVal = in1[x];
                int thirdVal  = in2[x];

                if (third != 0)
                {
                    thirdVal = PixelAdd(thirdVal, firstVal);
                }

                if (second == 1)
                {
                    secondVal = PixelAdd(secondVal, firstVal);
                }
                else if (second == 2)
                {
                    secondVal = PixelAdd(secondVal, PixelAdd(firstVal, thirdVal) >> 1);
                }

                out0[x] = firstVal;
                out1[x] = secondVal;
                out2[x] = thirdVal;
            }
        }

        private static int GetDeltaPaletteValue(int index, int c, int bitDepth)
        {
            if (c >= KRgbChannels)
            {
                return 0;
            }

            index      = -(index + 1);
            index     %= 1 + (2 * (KDeltaPaletteEntries - 1));
            int sign   = (index & 1) != 0 ? 1 : -1;
            int result = KDeltaPalette[(((index + 1) >> 1) * 3) + c] * sign;

            if (bitDepth > 8)
            {
                result *= 1 << (bitDepth - 8);
            }

            return result;
        }

        private static int GetImplicitPaletteValue(int index, int c, int paletteSize, int bitDepth)
        {
            if (c >= KRgbChannels)
            {
                return 0;
            }

            if (index < paletteSize + KLargeCubeOffset)
            {
                index -= paletteSize;
                index >>= c * KSmallCubeBits;
                int scaled = (int)(((ulong)(index % KSmallCube) * ((1UL << bitDepth) - 1UL)) >> 2);

                return scaled + (1 << Math.Max(0, bitDepth - 3));
            }

            index -= paletteSize + KLargeCubeOffset;

            if (c == 1)
            {
                index /= KLargeCube;
            }
            else if (c == 2)
            {
                index /= KLargeCube * KLargeCube;
            }

            return (int)(((ulong)(index % KLargeCube) * ((1UL << bitDepth) - 1UL)) >> 2);
        }

        private static Status ExecuteInvPalette(
            ModularImage input,
            Transform transform,
            WeightedHeader wpHeader,
            ThreadPool? pool,
            int c0,
            int nb)
        {
            if (input.Channels[c0].W == 0)
            {
                return true;
            }

            if (transform.NbDeltas == 0 && transform.Predictor == Predictor.Zero)
            {
                return UndoSimplePalette(input, pool, c0, nb);
            }

            return UndoDeltaPalette(input, transform, wpHeader, pool, c0, nb);
        }

        private static Status UndoSimplePalette(
            ModularImage input,
            ThreadPool? pool,
            int c0,
            int nb)
        {
            Channel palette = input.Channels[0];
            int palW        = (int)palette.W;
            int w           = (int)input.Channels[c0].W;
            uint h          = (uint)input.Channels[c0].H;
            int bitDepth    = Math.Min(input.BitDepth, 24);

            if (nb == 1)
            {
                return DataParallel.RunOnPool(
                    pool,
                    0,
                    h,
                    ThreadPool.NoInit,
                    (task, _) =>
                    {
                        Span<int> p = input.Channels[c0].Row(task);

                        for (int x = 0; x < w; x++)
                        {
                            int index = Math.Clamp(p[x], 0, palW - 1);
                            p[x]      = GetPaletteValue(palette, index, 0, palW, bitDepth);
                        }

                        return true;
                    },
                    "UndoChannelPalette");
            }

            return DataParallel.RunOnPool(
                pool,
                0,
                h,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    ReadOnlySpan<int> pIndex = input.Channels[c0].ConstRow(task);

                    for (int x = 0; x < w; x++)
                    {
                        int index = pIndex[x];

                        for (int c = 0; c < nb; c++)
                        {
                            input.Channels[c0 + c].Row(task)[x] = GetPaletteValue(palette, index, c, palW, bitDepth);
                        }
                    }

                    return true;
                },
                "UndoPalette");
        }

        private static Status UndoDeltaPalette(
            ModularImage input,
            Transform transform,
            WeightedHeader wpHeader,
            ThreadPool? pool,
            int c0,
            int nb)
        {
            Channel srcChan         = input.Channels[c0];
            StatusOr<Channel> idxOr = Channel.Create(input.MemoryManager, srcChan.W, srcChan.H);

            if (!idxOr.IsOk)
            {
                return idxOr.Status;
            }

            using Channel indices = idxOr.Value;
            ImageOps.CopyImageTo(srcChan.Plane, indices.Plane);

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)nb,
                ThreadPool.NoInit,
                (c, _) =>
                {
                    UndoDeltaPaletteChannel(input, indices, transform, wpHeader, c0, (int)c);

                    return true;
                },
                "UndoDeltaPalette");
        }

        private static void UndoDeltaPaletteChannel(
            ModularImage input,
            Channel indices,
            Transform transform,
            WeightedHeader wpHeader,
            int c0,
            int c)
        {
            Channel palette     = input.Channels[0];
            Channel channel     = input.Channels[c0 + c];
            int palW            = (int)palette.W;
            int nbDeltas        = (int)transform.NbDeltas;
            int bitDepth        = Math.Min(input.BitDepth, 24);
            Predictor predictor = transform.Predictor;

            WeightedState? wpState = predictor == Predictor.Weighted
                ? new WeightedState(wpHeader, channel.W, channel.H)
                : null;

            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> p           = channel.Row(y);
                ReadOnlySpan<int> idx = indices.ConstRow(y);

                for (nuint x = 0; x < channel.W; x++)
                {
                    int index        = idx[(int)x];
                    int paletteEntry = GetPaletteValue(palette, index, c, palW, bitDepth);
                    long val         = paletteEntry;

                    if (wpState != null)
                    {
                        PredictionResult pred = ModularPredictors.PredictNoTreeWP(
                            channel,
                            (int)x,
                            (int)y,
                            predictor,
                            wpState);

                        if (index < nbDeltas)
                        {
                            val = pred.Guess + paletteEntry;
                        }

                        p[(int)x] = (int)val;
                        wpState.UpdateErrors(p[(int)x], x, y, channel.W);
                    }
                    else
                    {
                        if (index < nbDeltas)
                        {
                            PredictionResult pred = ModularPredictors.PredictNoTreeNoWP(
                                channel,
                                (int)x,
                                (int)y,
                                predictor);
                            val = pred.Guess + paletteEntry;
                        }

                        p[(int)x] = (int)val;
                    }
                }
            }
        }

        private static Status FinalizeInvPaletteMetaChannels(ModularImage input, uint beginC, int c0, int nb)
        {
            if ((nuint)c0 >= input.NbMetaChannels)
            {
                input.NbMetaChannels--;
            }
            else
            {
                if ((int)input.NbMetaChannels < 2 - nb)
                {
                    return Status.Failure("Invalid meta-channel count in InvPalette");
                }

                input.NbMetaChannels = (nuint)((int)input.NbMetaChannels - (2 - nb));

                if (beginC + (uint)nb - 1u >= input.NbMetaChannels)
                {
                    return Status.Failure("Invalid meta-channel range after InvPalette");
                }
            }

            input.Channels[0].Dispose();
            input.Channels.RemoveAt(0);

            return true;
        }

        private static SqueezeParams CreateInPlaceSqueeze(bool horizontal, uint beginC, uint numC)
        {
            return new SqueezeParams
            {
                Horizontal = horizontal,
                InPlace    = true,
                BeginC     = beginC,
                NumC       = numC,
            };
        }

        private static Status ApplyMetaSqueezeStep(ModularImage image, SqueezeParams parameter)
        {
            Status checkOk = CheckMetaSqueezeParams(parameter, image.Channels.Count);

            if (!checkOk.IsOk)
            {
                return checkOk;
            }

            bool horizontal = parameter.Horizontal;
            bool inPlace    = parameter.InPlace;
            uint beginc     = parameter.BeginC;
            uint endc       = parameter.BeginC + parameter.NumC - 1u;

            if (beginc < image.NbMetaChannels)
            {
                if (endc >= image.NbMetaChannels)
                {
                    return Status.Failure("Invalid squeeze: mix of meta and nonmeta channels");
                }

                if (!inPlace)
                {
                    return Status.Failure("Invalid squeeze: meta channels require in-place residuals");
                }

                image.NbMetaChannels += parameter.NumC;
            }

            uint offset = inPlace ? endc + 1u : (uint)image.Channels.Count;

            for (uint c = beginc; c <= endc; c++)
            {
                Status chanOk = ApplyMetaSqueezeChannel(image, (int)c, (int)(offset + (c - beginc)), horizontal);

                if (!chanOk.IsOk)
                {
                    return chanOk;
                }
            }

            return true;
        }

        private static Status ApplyMetaSqueezeChannel(
            ModularImage image,
            int c,
            int insertIndex,
            bool horizontal)
        {
            Channel ch = image.Channels[c];

            if (ch.HShift > 30 || ch.VShift > 30)
            {
                return Status.Failure("Too many squeezes: shift > 30");
            }

            nuint w = ch.W;
            nuint h = ch.H;

            if (w == 0 || h == 0)
            {
                return Status.Failure("Squeezing empty channel");
            }

            if (horizontal)
            {
                ch.W = (w + 1u) / 2u;

                if (ch.HShift >= 0)
                {
                    ch.HShift++;
                }

                w -= (w + 1u) / 2u;
            }
            else
            {
                ch.H = (h + 1u) / 2u;

                if (ch.VShift >= 0)
                {
                    ch.VShift++;
                }

                h -= (h + 1u) / 2u;
            }

            Status shrinkOk = ch.Shrink();

            if (!shrinkOk.IsOk)
            {
                return shrinkOk;
            }

            StatusOr<Channel> placeholderOr = Channel.Create(image.MemoryManager, w, h, ch.HShift, ch.VShift);

            if (!placeholderOr.IsOk)
            {
                return placeholderOr.Status;
            }

            Channel placeholder   = placeholderOr.Value;
            placeholder.Component = ch.Component;
            image.Channels.Insert(insertIndex, placeholder);

            return true;
        }

        private static Status ApplyInvSqueezeStep(
            ModularImage input,
            SqueezeParams parameter,
            ThreadPool? pool)
        {
            Status checkOk = CheckMetaSqueezeParams(parameter, input.Channels.Count);

            if (!checkOk.IsOk)
            {
                return checkOk;
            }

            bool horizontal = parameter.Horizontal;
            bool inPlace    = parameter.InPlace;
            uint beginc     = parameter.BeginC;
            uint endc       = parameter.BeginC + parameter.NumC - 1u;
            uint offset     = inPlace ? endc + 1u : (uint)input.Channels.Count + beginc - endc - 1u;

            if (beginc < input.NbMetaChannels)
            {
                if (input.NbMetaChannels <= parameter.NumC)
                {
                    return Status.Failure("Invalid meta-channel count in InvSqueeze");
                }

                input.NbMetaChannels -= parameter.NumC;
            }

            for (uint c = beginc; c <= endc; c++)
            {
                uint rc = offset + c - beginc;

                if (rc >= (uint)input.Channels.Count)
                {
                    return Status.Failure("Residual channel out of bounds in InvSqueeze");
                }

                if (input.Channels[(int)c].W < input.Channels[(int)rc].W ||
                    input.Channels[(int)c].H < input.Channels[(int)rc].H)
                {
                    return Status.Failure("Corrupted squeeze transform");
                }

                Status invOk = horizontal
                    ? InvHSqueeze(input, (int)c, (int)rc, pool)
                    : InvVSqueeze(input, (int)c, (int)rc, pool);

                if (!invOk.IsOk)
                {
                    return invOk;
                }
            }

            int count = (int)(endc - beginc + 1u);

            for (int i = 0; i < count; i++)
            {
                input.Channels[(int)offset].Dispose();
                input.Channels.RemoveAt((int)offset);
            }

            return true;
        }

        private static Status InvHSqueeze(ModularImage input, int c, int rc, ThreadPool? pool)
        {
            Channel chin         = input.Channels[c];
            Channel chinResidual = input.Channels[rc];

            if (chinResidual.W == 0)
            {
                chin.HShift--;

                return true;
            }

            StatusOr<Channel> choutOr = Channel.Create(
                input.MemoryManager,
                chin.W + chinResidual.W,
                chin.H,
                chin.HShift - 1,
                chin.VShift);

            if (!choutOr.IsOk)
            {
                return choutOr.Status;
            }

            Channel chout    = choutOr.Value;
            chout.Component  = chin.Component;

            if (chinResidual.H == 0)
            {
                chin.Dispose();
                input.Channels[c] = chout;

                return true;
            }

            Status poolOk = DataParallel.RunOnPool(
                pool,
                0,
                (uint)chin.H,
                ThreadPool.NoInit,
                (y, _) =>
                {
                    UnsqueezeHRow(chin, chinResidual, chout, y);

                    return true;
                },
                "InvHorizontalSqueeze");

            if (!poolOk.IsOk)
            {
                chout.Dispose();

                return poolOk;
            }

            chin.Dispose();
            input.Channels[c] = chout;

            return true;
        }

        private static void UnsqueezeHRow(Channel chin, Channel chinResidual, Channel chout, nuint y)
        {
            ReadOnlySpan<int> pResidual = chinResidual.ConstRow(y);
            ReadOnlySpan<int> pAvg      = chin.ConstRow(y);
            Span<int> pOut              = chout.Row(y);

            for (nuint x = 0; x < chinResidual.W; x++)
            {
                int ix                 = (int)x;
                long diffMinusTendency = pResidual[ix];
                long avg               = pAvg[ix];
                long nextAvg           = x + 1u < chin.W ? pAvg[ix + 1] : avg;
                long left              = x > 0 ? pOut[(ix << 1) - 1] : avg;
                long tendency          = SmoothTendency(left, avg, nextAvg);
                long diff              = diffMinusTendency + tendency;
                long a                 = avg + (diff / 2);
                pOut[ix << 1]          = (int)a;
                pOut[(ix << 1) + 1]    = (int)(a - diff);
            }

            if ((chout.W & 1u) != 0)
            {
                pOut[(int)chout.W - 1] = pAvg[(int)chin.W - 1];
            }
        }

        private static Status InvVSqueeze(ModularImage input, int c, int rc, ThreadPool? pool)
        {
            Channel chin         = input.Channels[c];
            Channel chinResidual = input.Channels[rc];

            if (chinResidual.H == 0)
            {
                chin.VShift--;

                return true;
            }

            StatusOr<Channel> choutOr = Channel.Create(
                input.MemoryManager,
                chin.W,
                chin.H + chinResidual.H,
                chin.HShift,
                chin.VShift - 1);

            if (!choutOr.IsOk)
            {
                return choutOr.Status;
            }

            Channel chout   = choutOr.Value;
            chout.Component = chin.Component;

            if (chinResidual.W == 0)
            {
                chin.Dispose();
                input.Channels[c] = chout;

                return true;
            }

            const nuint kColsPerThread = 64;
            uint numTasks              = (uint)Common.DivCeil(chin.W, kColsPerThread);

            Status poolOk = DataParallel.RunOnPool(
                pool,
                0,
                numTasks,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    nuint x0 = task * kColsPerThread;
                    nuint x1 = Math.Min((nuint)(task + 1u) * kColsPerThread, chin.W);
                    UnsqueezeVSlice(chin, chinResidual, chout, x0, x1);

                    return true;
                },
                "InvVertSqueeze");

            if (!poolOk.IsOk)
            {
                chout.Dispose();

                return poolOk;
            }

            if ((chout.H & 1u) != 0)
            {
                nuint lastY            = chin.H - 1u;
                ReadOnlySpan<int> pAvg = chin.ConstRow(lastY);
                Span<int> pOut         = chout.Row(lastY << 1);
                pAvg.Slice(0, (int)chin.W).CopyTo(pOut.Slice(0, (int)chin.W));
            }

            chin.Dispose();
            input.Channels[c] = chout;

            return true;
        }

        private static void UnsqueezeVSlice(
            Channel chin,
            Channel chinResidual,
            Channel chout,
            nuint x0,
            nuint x1)
        {
            for (nuint y = 0; y < chinResidual.H; y++)
            {
                ReadOnlySpan<int> pResidual = chinResidual.ConstRow(y);
                ReadOnlySpan<int> pAvg      = chin.ConstRow(y);
                ReadOnlySpan<int> pNAvg     = chin.ConstRow(y + 1u < chin.H ? y + 1u : y);
                ReadOnlySpan<int> pPOut     = y > 0 ? chout.ConstRow((y << 1) - 1u) : pAvg;
                Span<int> pOut              = chout.Row(y << 1);
                Span<int> pNOut             = chout.Row((y << 1) + 1u);

                for (nuint x = x0; x < x1; x++)
                {
                    int ix                 = (int)x;
                    long avg               = pAvg[ix];
                    long nextAvg           = pNAvg[ix];
                    long top               = pPOut[ix];
                    long tendency          = SmoothTendency(top, avg, nextAvg);
                    long diffMinusTendency = pResidual[ix];
                    long diff              = diffMinusTendency + tendency;
                    long outVal            = avg + (diff / 2);
                    pOut[ix]               = (int)outVal;
                    pNOut[ix]              = (int)(outVal - diff);
                }
            }
        }
    }
}
