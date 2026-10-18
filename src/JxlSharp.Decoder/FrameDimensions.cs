// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Encoder speed tiers.
    /// Ported from <c>jxl::SpeedTier</c> in <c>lib/jxl/common.h</c>.
    /// </summary>
    public enum SpeedTier
    {
        /// <summary>Try multiple combinations of Glacier flags for modular mode. Otherwise like <see cref="Glacier"/>.</summary>
        TectonicPlate = -1,

        /// <summary>Learn a global tree in Modular mode.</summary>
        Glacier = 0,

        /// <summary>Turns on FindBestQuantizationHQ loop. Equivalent to "guetzli" mode.</summary>
        Tortoise = 1,

        /// <summary>Turns on FindBestQuantization butteraugli loop.</summary>
        Kitten = 2,

        /// <summary>Turns on dots, patches, and spline detection by default, as well as full context clustering. Default.</summary>
        Squirrel = 3,

        /// <summary>Turns on error diffusion and full AC strategy heuristics. Equivalent to "fast" mode.</summary>
        Wombat = 4,

        /// <summary>Turns on simple heuristics for AC strategy, quant field, gaborish by default, non-default cmap, initial quant field, non-default CFL.</summary>
        Hare = 5,

        /// <summary>Turns on clustering and enables coefficient reordering.</summary>
        Cheetah = 6,

        /// <summary>Turns off most encoder features. Does context clustering. Modular: uses fixed tree with Weighted predictor.</summary>
        Falcon = 7,

        /// <summary>Currently fastest possible setting for VarDCT. Modular: uses fixed tree with Gradient predictor.</summary>
        Thunder = 8,

        /// <summary>VarDCT: same as <see cref="Thunder"/>. Modular: no tree, Gradient predictor, fast histograms.</summary>
        Lightning = 9,
    }

    /// <summary>
    /// Dimensions of a frame in pixels and other derived block/group dimensions.
    /// Ported from <c>jxl::FrameDimensions</c> in <c>lib/jxl/frame_dimensions.h</c>.
    /// </summary>
    public struct FrameDimensions
    {
        // Block is the square grid of pixels to which an "energy compaction"
        // transformation (e.g. DCT) is applied. Each block has its own AC quantizer.

        /// <summary>Dimension of a single 8x8 block in pixels.</summary>
        public const nuint KBlockDim = 8;

        /// <summary>Dimension of a single 8x8 block in pixels (C++ naming alias).</summary>
        public const nuint kBlockDim = KBlockDim;

        /// <summary>Number of pixels in a single 8x8 DCT block (64).</summary>
        public const nuint KDctBlockSize = KBlockDim * KBlockDim;

        /// <summary>Number of pixels in a single 8x8 DCT block (64, C++ naming alias).</summary>
        public const nuint kDCTBlockSize = KDctBlockSize;

        /// <summary>Default group dimension in pixels (256).</summary>
        public const nuint KGroupDim = 256;

        /// <summary>Default group dimension in 8x8 blocks (32).</summary>
        public const nuint KGroupDimInBlocks = KGroupDim / KBlockDim;

        /// <summary>Maximum number of progressive passes in an image (11).</summary>
        public const int KMaxNumPasses = 11;

        /// <summary>Maximum number of reference frames (4).</summary>
        public const int KMaxNumReferenceFrames = 4;

        /// <summary>Maximum number of coefficient orders per channel (13).</summary>
        public const byte KNumOrders = 13;

        /// <summary>The 12-byte JXL container signature box.</summary>
        public static ReadOnlySpan<byte> JxlSignatureBox => new byte[]
        {
            0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A,
        };

        /// <summary>Image width without any upsampling (<c>original_size / upsampling</c>).</summary>
        public nuint XSize;

        /// <summary>Image height without any upsampling (<c>original_size / upsampling</c>).</summary>
        public nuint YSize;

        /// <summary>Original image width.</summary>
        public nuint XSizeUpsampled;

        /// <summary>Original image height.</summary>
        public nuint YSizeUpsampled;

        /// <summary>Image width after upsampling the padded image.</summary>
        public nuint XSizeUpsampledPadded;

        /// <summary>Image height after upsampling the padded image.</summary>
        public nuint YSizeUpsampledPadded;

        /// <summary>Image width after padding to a multiple of <see cref="KBlockDim"/> (if VarDCT mode).</summary>
        public nuint XSizePadded;

        /// <summary>Image height after padding to a multiple of <see cref="KBlockDim"/> (if VarDCT mode).</summary>
        public nuint YSizePadded;

        /// <summary>Image width in <see cref="KBlockDim"/> blocks.</summary>
        public nuint XSizeBlocks;

        /// <summary>Image height in <see cref="KBlockDim"/> blocks.</summary>
        public nuint YSizeBlocks;

        /// <summary>Image width in number of AC groups.</summary>
        public nuint XSizeGroups;

        /// <summary>Image height in number of AC groups.</summary>
        public nuint YSizeGroups;

        /// <summary>Image width in number of DC groups.</summary>
        public nuint XSizeDcGroups;

        /// <summary>Image height in number of DC groups.</summary>
        public nuint YSizeDcGroups;

        /// <summary>Total number of AC groups.</summary>
        public nuint NumGroups;

        /// <summary>Total number of DC groups.</summary>
        public nuint NumDcGroups;

        /// <summary>Size of an AC group in pixels.</summary>
        public nuint GroupDim;

        /// <summary>Size of a DC group in pixels.</summary>
        public nuint DcGroupDim;

        /// <summary>
        /// Computes all derived frame dimensions from the given parameters.
        /// </summary>
        /// <param name="xsizePx">Frame width in pixels.</param>
        /// <param name="ysizePx">Frame height in pixels.</param>
        /// <param name="groupSizeShift">Group size shift (0..3).</param>
        /// <param name="maxHshift">Maximum horizontal chroma subsampling shift.</param>
        /// <param name="maxVshift">Maximum vertical chroma subsampling shift.</param>
        /// <param name="modularMode">Whether the frame uses Modular encoding.</param>
        /// <param name="upsampling">Upsampling factor (1, 2, 4, or 8).</param>
        public void Set(
            nuint xsizePx,
            nuint ysizePx,
            nuint groupSizeShift,
            nuint maxHshift,
            nuint maxVshift,
            bool modularMode,
            nuint upsampling)
        {
            GroupDim       = (KGroupDim >> 1) << (int)groupSizeShift;
            DcGroupDim     = GroupDim * KBlockDim;
            XSizeUpsampled = xsizePx;
            YSizeUpsampled = ysizePx;
            XSize          = Common.DivCeil(xsizePx, upsampling);
            YSize          = Common.DivCeil(ysizePx, upsampling);
            XSizeBlocks    = Common.DivCeil(XSize, KBlockDim << (int)maxHshift) << (int)maxHshift;
            YSizeBlocks    = Common.DivCeil(YSize, KBlockDim << (int)maxVshift) << (int)maxVshift;
            XSizePadded    = XSizeBlocks * KBlockDim;
            YSizePadded    = YSizeBlocks * KBlockDim;

            if (modularMode)
            {
                // Modular mode doesn't have any padding.
                XSizePadded = XSize;
                YSizePadded = YSize;
            }

            XSizeUpsampledPadded = XSizePadded * upsampling;
            YSizeUpsampledPadded = YSizePadded * upsampling;
            XSizeGroups          = Common.DivCeil(XSize, GroupDim);
            YSizeGroups          = Common.DivCeil(YSize, GroupDim);
            XSizeDcGroups        = Common.DivCeil(XSizeBlocks, GroupDim);
            YSizeDcGroups        = Common.DivCeil(YSizeBlocks, GroupDim);
            NumGroups            = XSizeGroups * YSizeGroups;
            NumDcGroups          = XSizeDcGroups * YSizeDcGroups;
        }

        /// <summary>
        /// Returns the pixel bounding rectangle for the AC group at <paramref name="groupIndex"/>.
        /// </summary>
        /// <param name="groupIndex">AC group index.</param>
        /// <returns>Clipped pixel <see cref="Rect"/>.</returns>
        public readonly Rect GroupRect(nuint groupIndex)
        {
            nuint gx = groupIndex % XSizeGroups;
            nuint gy = groupIndex / XSizeGroups;

            return new Rect(
                (int)(gx * GroupDim),
                (int)(gy * GroupDim),
                (int)GroupDim,
                (int)GroupDim,
                (int)XSize,
                (int)YSize);
        }

        /// <summary>
        /// Returns the 8x8 block bounding rectangle for the AC group at <paramref name="groupIndex"/>.
        /// </summary>
        /// <param name="groupIndex">AC group index.</param>
        /// <returns>Clipped block <see cref="Rect"/>.</returns>
        public readonly Rect BlockGroupRect(nuint groupIndex)
        {
            nuint gx       = groupIndex % XSizeGroups;
            nuint gy       = groupIndex / XSizeGroups;
            nuint blockDim = GroupDim >> 3;

            return new Rect(
                (int)(gx * blockDim),
                (int)(gy * blockDim),
                (int)blockDim,
                (int)blockDim,
                (int)XSizeBlocks,
                (int)YSizeBlocks);
        }

        /// <summary>
        /// Returns the 8x8 block bounding rectangle for the DC group at <paramref name="groupIndex"/>.
        /// </summary>
        /// <param name="groupIndex">DC group index.</param>
        /// <returns>Clipped DC block <see cref="Rect"/>.</returns>
        public readonly Rect DCGroupRect(nuint groupIndex)
        {
            nuint gx = groupIndex % XSizeDcGroups;
            nuint gy = groupIndex / XSizeDcGroups;

            return new Rect(
                (int)(gx * GroupDim),
                (int)(gy * GroupDim),
                (int)GroupDim,
                (int)GroupDim,
                (int)XSizeBlocks,
                (int)YSizeBlocks);
        }
    }
}
