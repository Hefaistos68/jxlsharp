// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Chooses reference patches, and avoids encoding them once per occurrence.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Holds a decoded reference frame <see cref="ImageBundle"/> and whether it is stored in XYB space.
    /// Ported from <c>jxl::ReferenceFrame</c> in <c>lib/jxl/dec_patch_dictionary.h</c>.
    /// </summary>
    public sealed class ReferenceFrame : IDisposable
    {
        /// <summary>Reference frame image bundle.</summary>
        public ImageBundle? Frame;

        // ImageBundle doesn't yet have a simple way to state it is in XYB.

        /// <summary>Whether <see cref="Frame"/> is stored in XYB color space.</summary>
        public bool IbIsInXyb;

        /// <inheritdoc />
        public void Dispose()
        {
            Frame?.Dispose();
            Frame = null;
        }
    }

    /// <summary>
    /// Patch blending modes.
    /// Ported from <c>jxl::PatchBlendMode</c> in <c>lib/jxl/dec_patch_dictionary.h</c>.
    /// </summary>
    public enum PatchBlendMode : byte
    {
        // The new values are the old ones. Useful to skip some channels.

        /// <summary>Skip channel: keep existing values.</summary>
        kNone = 0,

        // The new values (in the crop) replace the old ones: sample = new

        /// <summary>Replace existing values with patch values.</summary>
        kReplace = 1,

        // The new values (in the crop) get added to the old ones: sample = old + new

        /// <summary>Add patch values to existing values.</summary>
        kAdd = 2,

        // The new values (in the crop) get multiplied by the old ones:
        // sample = old * new
        // This blend mode is only supported if BlendColorSpace is kEncoded. The
        // range of the new value matters for multiplication purposes, and its
        // nominal range of 0..1 is computed the same way as this is done for the
        // alpha values in kBlend and kAlphaWeightedAdd.

        /// <summary>Multiply existing values by patch values.</summary>
        kMul = 3,

        // The new values (in the crop) replace the old ones if alpha>0:
        // For first alpha channel:
        // alpha = old + new * (1 - old)
        // For other channels if !alpha_associated:
        // sample = ((1 - new_alpha) * old * old_alpha + new_alpha * new) / alpha
        // For other channels if alpha_associated:
        // sample = (1 - new_alpha) * old + new
        // The alpha formula applies to the alpha used for the division in the other
        // channels formula, and applies to the alpha channel itself if its
        // blend_channel value matches itself.
        // If using kBlendAbove, new is the patch and old is the original image; if
        // using kBlendBelow, the meaning is inverted.

        /// <summary>Alpha blend patch above existing image.</summary>
        kBlendAbove = 4,

        /// <summary>Alpha blend patch below existing image.</summary>
        kBlendBelow = 5,

        // The new values (in the crop) are added to the old ones if alpha>0:
        // For first alpha channel: sample = sample = old + new * (1 - old)
        // For other channels: sample = old + alpha * new

        /// <summary>Alpha-weighted add patch above existing image.</summary>
        kAlphaWeightedAddAbove = 6,

        /// <summary>Alpha-weighted add patch below existing image.</summary>
        kAlphaWeightedAddBelow = 7,
    }

    /// <summary>
    /// Blending configuration for a single patch channel.
    /// Ported from <c>jxl::PatchBlending</c> in <c>lib/jxl/dec_patch_dictionary.h</c>.
    /// </summary>
    public struct PatchBlending
    {
        /// <summary>Total number of valid <see cref="PatchBlendMode"/> values (8).</summary>
        public const byte kNumPatchBlendModes = (byte)PatchBlendMode.kAlphaWeightedAddBelow + 1;

        /// <summary>Patch blending mode.</summary>
        public PatchBlendMode Mode;

        /// <summary>Index of the extra channel used as alpha.</summary>
        public uint AlphaChannel;

        /// <summary>Whether alpha or multiplicative values are clamped to <c>[0, 1]</c>.</summary>
        public bool Clamp;

        /// <summary>
        /// Returns whether <paramref name="mode"/> uses an alpha channel.
        /// </summary>
        /// <param name="mode">Patch blend mode.</param>
        /// <returns><see langword="true"/> if <paramref name="mode"/> uses an alpha channel.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UsesAlpha(PatchBlendMode mode)
        {
            return mode == PatchBlendMode.kBlendAbove ||
                   mode == PatchBlendMode.kBlendBelow ||
                   mode == PatchBlendMode.kAlphaWeightedAddAbove ||
                   mode == PatchBlendMode.kAlphaWeightedAddBelow;
        }

        /// <summary>
        /// Returns whether <paramref name="mode"/> reads a clamp flag from the bitstream.
        /// </summary>
        /// <param name="mode">Patch blend mode.</param>
        /// <returns><see langword="true"/> if <paramref name="mode"/> supports clamping.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UsesClamp(PatchBlendMode mode)
        {
            return UsesAlpha(mode) || mode == PatchBlendMode.kMul;
        }
    }

    /// <summary>
    /// Position and size of a patch in a reference frame.
    /// Ported from <c>jxl::PatchReferencePosition</c> in <c>lib/jxl/dec_patch_dictionary.h</c>.
    /// </summary>
    public struct PatchReferencePosition
    {
        /// <summary>Reference frame slot index (0..3).</summary>
        public nuint Ref;

        /// <summary>Top-left X coordinate in the reference frame.</summary>
        public nuint X0;

        /// <summary>Top-left Y coordinate in the reference frame.</summary>
        public nuint Y0;

        /// <summary>Width of the patch in pixels.</summary>
        public nuint XSize;

        /// <summary>Height of the patch in pixels.</summary>
        public nuint YSize;
    }

    /// <summary>
    /// Target position of a patch instance in the current frame and index of its <see cref="PatchReferencePosition"/>.
    /// Ported from <c>jxl::PatchPosition</c> in <c>lib/jxl/dec_patch_dictionary.h</c>.
    /// </summary>
    public struct PatchPosition
    {
        /// <summary>Horizontal top-left coordinate of the patch in the image.</summary>
        public nuint X;

        /// <summary>Vertical top-left coordinate of the patch in the image.</summary>
        public nuint Y;

        /// <summary>Index into <c>ref_positions_</c>.</summary>
        public nuint RefPosIdx;
    }

    /// <summary>
    /// ANS context indices for patch dictionary decoding (Section C.4.5, Listing C.2).
    /// Ported from <c>lib/jxl/patch_dictionary_internal.h</c>.
    /// </summary>
    public static class PatchDictionaryContexts
    {
        /// <summary>Context for the number of reference patches (0).</summary>
        public const nuint kNumRefPatchContext = 0;

        /// <summary>Context for the reference frame slot index (1).</summary>
        public const nuint kReferenceFrameContext = 1;

        /// <summary>Context for patch width and height (2).</summary>
        public const nuint kPatchSizeContext = 2;

        /// <summary>Context for reference frame x0 and y0 coordinates (3).</summary>
        public const nuint kPatchReferencePositionContext = 3;

        /// <summary>Context for the first patch occurrence position (4).</summary>
        public const nuint kPatchPositionContext = 4;

        /// <summary>Context for the patch blend mode (5).</summary>
        public const nuint kPatchBlendModeContext = 5;

        /// <summary>Context for delta-coded subsequent patch positions (6).</summary>
        public const nuint kPatchOffsetContext = 6;

        /// <summary>Context for the number of occurrences of a reference patch (7).</summary>
        public const nuint kPatchCountContext = 7;

        /// <summary>Context for the alpha channel index (8).</summary>
        public const nuint kPatchAlphaChannelContext = 8;

        /// <summary>Context for the clamp flag (9).</summary>
        public const nuint kPatchClampContext = 9;

        /// <summary>Total number of patch dictionary ANS contexts (10).</summary>
        public const nuint kNumPatchDictionaryContexts = 10;
    }

    /// <summary>
    /// Decodes, indexes, and applies reference frame patches to decoded image rows.
    /// Ported from <c>jxl::PatchDictionary</c> in <c>lib/jxl/dec_patch_dictionary.h</c> and <c>lib/jxl/dec_patch_dictionary.cc</c>.
    /// </summary>
    public sealed unsafe class PatchDictionary
    {
        private readonly JxlMemoryManager? _memoryManager;
        private ReferenceFrame[]? _referenceFrames;
        private readonly List<PatchPosition> _positions             = new List<PatchPosition>();
        private readonly List<PatchReferencePosition> _refPositions = new List<PatchReferencePosition>();
        private readonly List<PatchBlending> _blendings             = new List<PatchBlending>();
        private nuint _blendingsStride;

        // Interval tree on the y coordinates of the patches.
        private readonly List<PatchTreeNode> _patchTree                     = new List<PatchTreeNode>();
        private readonly List<nuint> _numPatches                            = new List<nuint>();
        private readonly List<(nuint Y0, nuint Idx)> _sortedPatchesY0       = new List<(nuint Y0, nuint Idx)>();
        private readonly List<(nuint Y1, nuint Idx)> _sortedPatchesY1       = new List<(nuint Y1, nuint Idx)>();

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchDictionary"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary buffers.</param>
        public PatchDictionary(JxlMemoryManager? memoryManager)
        {
            _memoryManager = memoryManager;
        }

        /// <summary>Gets the decoded patch positions (accessible to encoder and tests).</summary>
        public IReadOnlyList<PatchPosition> Positions => _positions;

        /// <summary>Gets the decoded patch reference positions.</summary>
        public IReadOnlyList<PatchReferencePosition> RefPositions => _refPositions;

        /// <summary>Gets the decoded patch blending entries.</summary>
        public IReadOnlyList<PatchBlending> Blendings => _blendings;

        /// <summary>Gets the stride in <see cref="Blendings"/> per patch position (<c>num_extra_channels + 1</c>).</summary>
        public nuint BlendingsStride => _blendingsStride;

        /// <summary>Gets the attached 4-slot reference frame array.</summary>
        public ReferenceFrame[]? ReferenceFrames => _referenceFrames;

        /// <summary>
        /// Attaches the shared 4-slot reference frame array.
        /// </summary>
        /// <param name="referenceFrames">Array of 4 <see cref="ReferenceFrame"/> slots.</param>
        public void SetShared(ReferenceFrame[] referenceFrames)
        {
            _referenceFrames = referenceFrames;
        }

        /// <summary>
        /// Populates the patch dictionary positions, reference positions, and blendings from encoder heuristics and rebuilds the interval tree.
        /// Ported from <c>jxl::PatchDictionaryEncoder::SetPositions</c> in <c>lib/jxl/enc_patch_dictionary.h</c>.
        /// </summary>
        /// <param name="positions">Patch occurrence positions.</param>
        /// <param name="refPositions">Reference frame patch crop regions.</param>
        /// <param name="blendings">Per-occurrence blending parameters.</param>
        /// <param name="blendingsStride">Stride per occurrence in <paramref name="blendings"/>.</param>
        public void SetPositions(
            IEnumerable<PatchPosition> positions,
            IEnumerable<PatchReferencePosition> refPositions,
            IEnumerable<PatchBlending> blendings,
            nuint blendingsStride)
        {
            _positions.Clear();
            _positions.AddRange(positions);
            _refPositions.Clear();
            _refPositions.AddRange(refPositions);
            _blendings.Clear();
            _blendings.AddRange(blendings);
            _blendingsStride = blendingsStride;
            ComputePatchTree();
        }

        /// <summary>
        /// Gets a value indicating whether this dictionary contains any patches.
        /// </summary>
        /// <returns><see langword="true"/> if at least one patch position is present.</returns>
        public bool HasAny() => _positions.Count > 0;

        /// <summary>
        /// Clears all patches and resets the interval tree.
        /// </summary>
        public void Clear()
        {
            _positions.Clear();
            _refPositions.Clear();
            _blendings.Clear();
            ComputePatchTree();
        }

        /// <summary>
        /// Decodes the patch dictionary from <paramref name="br"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for histogram decoding.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="xsize">Frame width in pixels.</param>
        /// <param name="ysize">Frame height in pixels.</param>
        /// <param name="numExtraChannels">Number of extra channels in the frame.</param>
        /// <param name="usesExtraChannels">Updated to <see langword="true"/> if any patch uses or modifies extra channels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Decode(
            JxlMemoryManager? memoryManager,
            BitReader br,
            nuint xsize,
            nuint ysize,
            nuint numExtraChannels,
            ref bool usesExtraChannels)
        {
            // The dictionary is reused across frames: blendings_ is indexed by position
            // index, so stale entries from a previous frame must not remain.
            _positions.Clear();
            _refPositions.Clear();
            _blendings.Clear();
            _blendingsStride = numExtraChannels + 1;

            ANSCode code = new ANSCode();

            if (!DecAns.DecodeHistograms(
                    memoryManager,
                    br,
                    PatchDictionaryContexts.kNumPatchDictionaryContexts,
                    code,
                    out byte[] contextMap))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerResult = ANSSymbolReader.Create(code, br);

            if (!readerResult.IsOk)
            {
                return readerResult.Status;
            }

            ANSSymbolReader decoder = readerResult.Value;
            PatchDecodeContext ctx  = new PatchDecodeContext(br, decoder, contextMap, xsize, ysize, numExtraChannels);

            if (!DecodeAllRefPatches(in ctx, ref usesExtraChannels))
            {
                return false;
            }

            if (!decoder.CheckANSFinalState())
            {
                return Status.Failure("ANS checksum failure.");
            }

            ComputePatchTree();

            return true;
        }

        /// <summary>
        /// Returns dependencies of this patch dictionary on reference frame IDs as a
        /// bitmask: bits 0-3 indicate reference frames 0-3.
        /// </summary>
        /// <returns>Bitmask of referenced frame slots.</returns>
        public int GetReferences()
        {
            int result = 0;

            for (int i = 0; i < _refPositions.Count; i++)
            {
                result |= 1 << (int)_refPositions[i].Ref;
            }

            return result;
        }

        /// <summary>
        /// Returns the sorted list of patch position indices that overlap row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index in the frame.</param>
        /// <returns>List of patch indices overlapping <paramref name="y"/>.</returns>
        public List<nuint> GetPatchesForRow(nuint y)
        {
            List<nuint> result = new List<nuint>();

            if (y >= (nuint)_numPatches.Count || _numPatches[(int)y] == 0)
            {
                return result;
            }

            result.Capacity = (int)_numPatches[(int)y];

            for (nint treeIdx = 0; treeIdx != -1;)
            {
                Debug.Assert(treeIdx < _patchTree.Count);
                PatchTreeNode node = _patchTree[(int)treeIdx];

                if (y <= node.YCenter)
                {
                    CollectPatchesFromY0(y, node, result);
                    treeIdx = y < node.YCenter ? node.LeftChild : -1;
                }
                else
                {
                    CollectPatchesFromY1(y, node, result);
                    treeIdx = node.RightChild;
                }
            }

            // Ensure that the relative order of patches that affect the same pixels is
            // preserved. This is important for patches that have a blend mode
            // different from kAdd.
            result.Sort();

            return result;
        }

        /// <summary>
        /// Adds patches to a segment of <paramref name="xsize"/> pixels starting at <paramref name="inout"/>,
        /// assumed to be located at position (<paramref name="x0"/>, <paramref name="y"/>) in the frame.
        /// </summary>
        /// <param name="inout">Array of channel row pointers (3 color + extra channels).</param>
        /// <param name="y">Vertical row coordinate in the frame.</param>
        /// <param name="x0">Horizontal starting coordinate in the frame.</param>
        /// <param name="xsize">Number of horizontal pixels in the segment.</param>
        /// <param name="extraChannelInfo">Metadata for all extra channels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status AddOneRow(
            float** inout,
            nuint y,
            nuint x0,
            nuint xsize,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            nuint numEc = (nuint)extraChannelInfo.Count;

            if (numEc + 1 > _blendingsStride)
            {
                return Status.Failure("Extra channel count exceeds blendings stride");
            }

            if (_referenceFrames is null)
            {
                return Status.Failure("Reference frames not attached to PatchDictionary");
            }

            List<nuint> rowPatches = GetPatchesForRow(y);

            if (rowPatches.Count == 0)
            {
                return true;
            }

            float** fgPtrs       = stackalloc float*[(int)(3 + numEc)];
            PatchBlending[] bArr = _blendings.ToArray();

            foreach (nuint posIdx in rowPatches)
            {
                if (!ApplySinglePatchToRow(posIdx, inout, fgPtrs, (y, x0, xsize), bArr, extraChannelInfo))
                {
                    return false;
                }
            }

            return true;
        }

        private readonly ref struct PatchDecodeContext
        {
            public readonly BitReader Br;
            public readonly ANSSymbolReader Decoder;
            public readonly ReadOnlySpan<byte> ContextMap;
            public readonly nuint XSize;
            public readonly nuint YSize;
            public readonly nuint NumExtraChannels;

            public PatchDecodeContext(
                BitReader br,
                ANSSymbolReader decoder,
                ReadOnlySpan<byte> contextMap,
                nuint xsize,
                nuint ysize,
                nuint numExtraChannels)
            {
                Br               = br;
                Decoder          = decoder;
                ContextMap       = contextMap;
                XSize            = xsize;
                YSize            = ysize;
                NumExtraChannels = numExtraChannels;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public nuint ReadNum(nuint context)
            {
                return Decoder.ReadHybridUint(context, Br, ContextMap);
            }
        }

        private struct PatchInterval
        {
            public nuint Idx;
            public nuint Y0;
            public nuint Y1;
        }

        private struct PatchTreeNode
        {
            public nint LeftChild;
            public nint RightChild;
            public nuint YCenter;
            public nuint Start;
            public nuint Num;
        }

        private Status DecodeAllRefPatches(in PatchDecodeContext ctx, ref bool usesExtraChannels)
        {
            nuint numRefPatch = ctx.ReadNum(PatchDictionaryContexts.kNumRefPatchContext);

            // Limit max memory usage of patches to about 66 bytes per pixel (assuming 8
            // bytes per size_t)
            nuint numPixels        = ctx.XSize * ctx.YSize;
            nuint maxRefPatches    = 1024 + numPixels / 4;
            nuint maxPatches       = maxRefPatches * 4;
            nuint maxBlendingInfos = maxPatches * 4;

            if (numRefPatch > maxRefPatches)
            {
                return Status.Failure("Too many patches in dictionary");
            }

            nuint totalPatches = 0;
            nuint nextSize     = 1;

            for (nuint id = 0; id < numRefPatch; id++)
            {
                if (!DecodeSingleRefPatchGroup(in ctx, maxPatches, maxBlendingInfos, ref totalPatches, ref nextSize, ref usesExtraChannels))
                {
                    return false;
                }
            }

            return true;
        }

        private Status DecodeSingleRefPatchGroup(
            in PatchDecodeContext ctx,
            nuint maxPatches,
            nuint maxBlendingInfos,
            ref nuint totalPatches,
            ref nuint nextSize,
            ref bool usesExtraChannels)
        {
            if (!ReadAndValidateRefPosition(in ctx, out PatchReferencePosition refPos))
            {
                return false;
            }

            nuint idCount = ctx.ReadNum(PatchDictionaryContexts.kPatchCountContext);

            if (idCount > maxPatches)
            {
                return Status.Failure("Too many patches in dictionary");
            }

            idCount++;
            totalPatches += idCount;

            if (totalPatches > maxPatches)
            {
                return Status.Failure("Too many patches in dictionary");
            }

            if (nextSize < totalPatches)
            {
                nextSize *= 2;
                nextSize  = Math.Min(nextSize, maxPatches);
            }

            if (nextSize * _blendingsStride > maxBlendingInfos)
            {
                return Status.Failure("Too many patches in dictionary");
            }

            bool chooseAlpha = ctx.NumExtraChannels > 1;

            for (nuint i = 0; i < idCount; i++)
            {
                if (!ReadPatchInstance(in ctx, refPos, i == 0, chooseAlpha, ref usesExtraChannels))
                {
                    return false;
                }
            }

            _refPositions.Add(refPos);

            return true;
        }

        private Status ReadAndValidateRefPosition(in PatchDecodeContext ctx, out PatchReferencePosition refPos)
        {
            refPos     = default;
            refPos.Ref = ctx.ReadNum(PatchDictionaryContexts.kReferenceFrameContext);

            if (refPos.Ref >= FrameDimensions.KMaxNumReferenceFrames ||
                _referenceFrames is null ||
                _referenceFrames[(int)refPos.Ref].Frame is null ||
                _referenceFrames[(int)refPos.Ref].Frame!.XSize() == 0)
            {
                return Status.Failure("Invalid reference frame ID");
            }

            if (!_referenceFrames[(int)refPos.Ref].IbIsInXyb)
            {
                return Status.Failure("Patches cannot use frames saved post color transforms");
            }

            ImageBundle ib = _referenceFrames[(int)refPos.Ref].Frame!;
            refPos.X0      = ctx.ReadNum(PatchDictionaryContexts.kPatchReferencePositionContext);
            refPos.Y0      = ctx.ReadNum(PatchDictionaryContexts.kPatchReferencePositionContext);
            refPos.XSize   = ctx.ReadNum(PatchDictionaryContexts.kPatchSizeContext) + 1;
            refPos.YSize   = ctx.ReadNum(PatchDictionaryContexts.kPatchSizeContext) + 1;

            if (refPos.X0 + refPos.XSize > ib.XSize() || refPos.Y0 + refPos.YSize > ib.YSize())
            {
                return Status.Failure("Invalid position specified in reference frame");
            }

            return true;
        }

        private Status ReadPatchInstance(
            in PatchDecodeContext ctx,
            PatchReferencePosition refPos,
            bool isFirst,
            bool chooseAlpha,
            ref bool usesExtraChannels)
        {
            PatchPosition pos = new PatchPosition
            {
                RefPosIdx = (nuint)_refPositions.Count,
            };

            if (isFirst)
            {
                pos.X = ctx.ReadNum(PatchDictionaryContexts.kPatchPositionContext);
                pos.Y = ctx.ReadNum(PatchDictionaryContexts.kPatchPositionContext);
            }
            else if (!ReadDeltaPosition(in ctx, _positions[_positions.Count - 1], ref pos))
            {
                return false;
            }

            if (pos.X + refPos.XSize > ctx.XSize)
            {
                return Status.Failure("Invalid patch x");
            }

            if (pos.Y + refPos.YSize > ctx.YSize)
            {
                return Status.Failure("Invalid patch y");
            }

            for (nuint j = 0; j < _blendingsStride; j++)
            {
                if (!ReadPatchChannelBlending(in ctx, j, chooseAlpha, ref usesExtraChannels))
                {
                    return false;
                }
            }

            _positions.Add(pos);

            return true;
        }

        private static Status ReadDeltaPosition(
            in PatchDecodeContext ctx,
            PatchPosition prevPos,
            ref PatchPosition pos)
        {
            nint deltaX = PackSigned.Unpack((uint)ctx.ReadNum(PatchDictionaryContexts.kPatchOffsetContext));

            if (deltaX < 0 && (nuint)(-deltaX) > prevPos.X)
            {
                return Status.Failure("Invalid patch: negative x coordinate");
            }

            pos.X       = (nuint)((nint)prevPos.X + deltaX);
            nint deltaY = PackSigned.Unpack((uint)ctx.ReadNum(PatchDictionaryContexts.kPatchOffsetContext));

            if (deltaY < 0 && (nuint)(-deltaY) > prevPos.Y)
            {
                return Status.Failure("Invalid patch: negative y coordinate");
            }

            pos.Y = (nuint)((nint)prevPos.Y + deltaY);

            return true;
        }

        private Status ReadPatchChannelBlending(
            in PatchDecodeContext ctx,
            nuint channelSlot,
            bool chooseAlpha,
            ref bool usesExtraChannels)
        {
            uint blendMode = (uint)ctx.ReadNum(PatchDictionaryContexts.kPatchBlendModeContext);

            if (blendMode >= PatchBlending.kNumPatchBlendModes)
            {
                return Status.Failure("Invalid patch blend mode");
            }

            PatchBlending info = new PatchBlending
            {
                Mode = (PatchBlendMode)blendMode,
            };

            if (PatchBlending.UsesAlpha(info.Mode) || (info.Mode != PatchBlendMode.kNone && channelSlot > 0))
            {
                usesExtraChannels = true;
            }

            if (PatchBlending.UsesAlpha(info.Mode) && chooseAlpha)
            {
                info.AlphaChannel = (uint)ctx.ReadNum(PatchDictionaryContexts.kPatchAlphaChannelContext);

                if (info.AlphaChannel >= ctx.NumExtraChannels)
                {
                    return Status.Failure("Invalid alpha channel for blending");
                }
            }

            if (PatchBlending.UsesClamp(info.Mode))
            {
                info.Clamp = ctx.ReadNum(PatchDictionaryContexts.kPatchClampContext) != 0;
            }

            _blendings.Add(info);

            return true;
        }

        private void ComputePatchTree()
        {
            _patchTree.Clear();
            _numPatches.Clear();
            _sortedPatchesY0.Clear();
            _sortedPatchesY1.Clear();

            if (_positions.Count == 0)
            {
                return;
            }

            PatchInterval[] intervals = BuildIntervalsAndRowCounts();

            PatchTreeNode root = new PatchTreeNode
            {
                Start = 0,
                Num   = (nuint)intervals.Length,
            };
            _patchTree.Add(root);

            for (int next = 0; next < _patchTree.Count; ++next)
            {
                BuildPatchTreeNode(intervals, next);
            }
        }

        private PatchInterval[] BuildIntervalsAndRowCounts()
        {
            PatchInterval[] intervals = new PatchInterval[_positions.Count];

            for (int i = 0; i < _positions.Count; ++i)
            {
                PatchPosition pos       = _positions[i];
                intervals[i].Idx        = (nuint)i;
                intervals[i].Y0         = pos.Y;
                intervals[i].Y1         = pos.Y + _refPositions[(int)pos.RefPosIdx].YSize;
            }

            SortByY1(intervals, 0, intervals.Length);
            nuint maxRow = intervals[intervals.Length - 1].Y1;

            for (nuint y = 0; y < maxRow; y++)
            {
                _numPatches.Add(0);
            }

            for (int i = 0; i < intervals.Length; i++)
            {
                for (nuint y = intervals[i].Y0; y < intervals[i].Y1; ++y)
                {
                    _numPatches[(int)y]++;
                }
            }

            return intervals;
        }

        private void BuildPatchTreeNode(PatchInterval[] intervals, int next)
        {
            PatchTreeNode node = _patchTree[next];
            int start          = (int)node.Start;
            int end            = (int)(node.Start + node.Num);

            SortByY0(intervals, start, end);
            int middleIdx = start + (int)(node.Num / 2);
            node.YCenter  = intervals[middleIdx].Y0;

            int rightStart = middleIdx;

            while (rightStart < end && intervals[rightStart].Y0 == node.YCenter)
            {
                ++rightStart;
            }

            SortByY1(intervals, start, rightStart);
            int leftEnd = rightStart;

            while (leftEnd > start && intervals[leftEnd - 1].Y1 > node.YCenter)
            {
                --leftEnd;
            }

            node.Num   = (nuint)(rightStart - leftEnd);
            node.Start = (nuint)_sortedPatchesY0.Count;

            for (int i = rightStart - 1; i >= leftEnd; --i)
            {
                _sortedPatchesY1.Add((intervals[i].Y1, intervals[i].Idx));
            }

            SortByY0(intervals, leftEnd, rightStart);

            for (int i = leftEnd; i < rightStart; ++i)
            {
                _sortedPatchesY0.Add((intervals[i].Y0, intervals[i].Idx));
            }

            node.LeftChild  = -1;
            node.RightChild = -1;

            if (leftEnd > start)
            {
                node.LeftChild = _patchTree.Count;
                _patchTree.Add(new PatchTreeNode
                {
                    Start = (nuint)start,
                    Num   = (nuint)(leftEnd - start),
                });
            }

            if (rightStart < end)
            {
                node.RightChild = _patchTree.Count;
                _patchTree.Add(new PatchTreeNode
                {
                    Start = (nuint)rightStart,
                    Num   = (nuint)(end - rightStart),
                });
            }

            _patchTree[next] = node;
        }

        private static void SortByY0(PatchInterval[] intervals, int start, int end)
        {
            Array.Sort(intervals, start, end - start, Comparer<PatchInterval>.Create((i0, i1) => i0.Y0.CompareTo(i1.Y0)));
        }

        private static void SortByY1(PatchInterval[] intervals, int start, int end)
        {
            Array.Sort(intervals, start, end - start, Comparer<PatchInterval>.Create((i0, i1) => i0.Y1.CompareTo(i1.Y1)));
        }

        private void CollectPatchesFromY0(nuint y, PatchTreeNode node, List<nuint> result)
        {
            for (nuint i = 0; i < node.Num; ++i)
            {
                (nuint y0, nuint idx) = _sortedPatchesY0[(int)(node.Start + i)];

                if (y < y0)
                {
                    break;
                }

                result.Add(idx);
            }
        }

        private void CollectPatchesFromY1(nuint y, PatchTreeNode node, List<nuint> result)
        {
            for (nuint i = 0; i < node.Num; ++i)
            {
                (nuint y1, nuint idx) = _sortedPatchesY1[(int)(node.Start + i)];

                if (y >= y1)
                {
                    break;
                }

                result.Add(idx);
            }
        }

        private Status ApplySinglePatchToRow(
            nuint posIdx,
            float** inout,
            float** fgPtrs,
            (nuint Y, nuint X0, nuint XSize) rowBounds,
            PatchBlending[] blendingsArray,
            IReadOnlyList<ExtraChannelInfo> extraChannelInfo)
        {
            nuint blendingIdx             = posIdx * _blendingsStride;
            PatchPosition pos             = _positions[(int)posIdx];
            PatchReferencePosition refPos = _refPositions[(int)pos.RefPosIdx];
            nuint by                      = pos.Y;
            nuint bx                      = pos.X;
            nuint patchXSize              = refPos.XSize;

            if (rowBounds.Y < by || rowBounds.Y >= by + refPos.YSize)
            {
                return Status.Failure("Patch row out of bounds");
            }

            if (bx >= rowBounds.X0 + rowBounds.XSize || bx + patchXSize < rowBounds.X0)
            {
                return true;
            }

            nuint iy        = rowBounds.Y - by;
            ImageBundle refIb = _referenceFrames![(int)refPos.Ref].Frame!;
            nuint patchX0   = Math.Max(bx, rowBounds.X0);
            nuint patchX1   = Math.Min(bx + patchXSize, rowBounds.X0 + rowBounds.XSize);
            nint xOffset    = (nint)refPos.X0 + (nint)rowBounds.X0 - (nint)bx;

            for (nuint c = 0; c < 3; c++)
            {
                fgPtrs[c] = refIb.Color().ConstPlaneRow(c, refPos.Y0 + iy) + xOffset;
            }

            nuint numEc = (nuint)extraChannelInfo.Count;

            for (nuint i = 0; i < numEc; i++)
            {
                fgPtrs[3 + i] = refIb.ExtraChannels[(int)i].ConstRow(refPos.Y0 + iy) + xOffset;
            }

            Blending.BlendingRowPointers rowPtrs = new Blending.BlendingRowPointers(
                inout,
                fgPtrs,
                inout,
                patchX0 - rowBounds.X0,
                patchX1 - patchX0);

            ReadOnlySpan<PatchBlending> ecSlice = blendingsArray.AsSpan((int)(blendingIdx + 1), (int)numEc);

            return Blending.PerformBlending(
                _memoryManager,
                in rowPtrs,
                blendingsArray[(int)blendingIdx],
                ecSlice,
                extraChannelInfo);
        }
    }
}
