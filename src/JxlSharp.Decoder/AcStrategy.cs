// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    // Defines the different kinds of transforms, and heuristics to choose between
    // them.
    // `AcStrategy` represents what transform should be used, and which sub-block of
    // that transform we are currently in. Note that DCT4x4 is applied on all four
    // 4x4 sub-blocks of an 8x8 block.
    // `AcStrategyImage` defines which strategy should be used for each 8x8 block
    // of the image. The highest 4 bits represent the strategy to be used, the
    // lowest 4 represent the index of the block inside that strategy.

    /// <summary>
    /// Raw AC transform strategy types.
    /// Ported from <c>jxl::AcStrategyType</c> in <c>lib/jxl/ac_strategy.h</c>.
    /// </summary>
    public enum AcStrategyType : uint
    {
        /// <summary>Regular block size DCT (8x8).</summary>
        DCT = 0,

        /// <summary>Encode pixels without transforming.</summary>
        IDENTITY = 1,

        /// <summary>Use 2-by-2 DCT.</summary>
        DCT2X2 = 2,

        /// <summary>Use 4-by-4 DCT.</summary>
        DCT4X4 = 3,

        /// <summary>Use 16-by-16 DCT.</summary>
        DCT16X16 = 4,

        /// <summary>Use 32-by-32 DCT.</summary>
        DCT32X32 = 5,

        /// <summary>Use 16-by-8 DCT.</summary>
        DCT16X8 = 6,

        /// <summary>Use 8-by-16 DCT.</summary>
        DCT8X16 = 7,

        /// <summary>Use 32-by-8 DCT.</summary>
        DCT32X8 = 8,

        /// <summary>Use 8-by-32 DCT.</summary>
        DCT8X32 = 9,

        /// <summary>Use 32-by-16 DCT.</summary>
        DCT32X16 = 10,

        /// <summary>Use 16-by-32 DCT.</summary>
        DCT16X32 = 11,

        /// <summary>Use 4-by-8 DCT.</summary>
        DCT4X8 = 12,

        /// <summary>Use 8-by-4 DCT.</summary>
        DCT8X4 = 13,

        /// <summary>Corner-DCT variant 0.</summary>
        AFV0 = 14,

        /// <summary>Corner-DCT variant 1.</summary>
        AFV1 = 15,

        /// <summary>Corner-DCT variant 2.</summary>
        AFV2 = 16,

        /// <summary>Corner-DCT variant 3.</summary>
        AFV3 = 17,

        /// <summary>Use 64-by-64 DCT.</summary>
        DCT64X64 = 18,

        /// <summary>Use 64-by-32 DCT.</summary>
        DCT64X32 = 19,

        /// <summary>Use 32-by-64 DCT.</summary>
        DCT32X64 = 20,

        // No transforms smaller than 64x64 are allowed below.

        /// <summary>Use 128-by-128 DCT.</summary>
        DCT128X128 = 21,

        /// <summary>Use 128-by-64 DCT.</summary>
        DCT128X64 = 22,

        /// <summary>Use 64-by-128 DCT.</summary>
        DCT64X128 = 23,

        /// <summary>Use 256-by-256 DCT.</summary>
        DCT256X256 = 24,

        /// <summary>Use 256-by-128 DCT.</summary>
        DCT256X128 = 25,

        /// <summary>Use 128-by-256 DCT.</summary>
        DCT128X256 = 26,
    }

    /// <summary>
    /// Represents the AC transform strategy and whether this 8x8 block is the first (top-left) sub-block.
    /// Ported from <c>jxl::AcStrategy</c> in <c>lib/jxl/ac_strategy.h</c> and <c>lib/jxl/ac_strategy.cc</c>.
    /// </summary>
    public readonly struct AcStrategy
    {
        // Extremal values for the number of blocks/coefficients of a single strategy.

        /// <summary>Maximum number of 8x8 blocks along one dimension of a single strategy (32).</summary>
        public const nuint kMaxCoeffBlocks = 32;

        /// <summary>Maximum pixel dimension of a single strategy block (256).</summary>
        public const nuint kMaxBlockDim = FrameDimensions.kBlockDim * kMaxCoeffBlocks;

        // Maximum number of coefficients in a block. Guaranteed to be a multiple of
        // the vector size.

        /// <summary>Maximum number of coefficients in a single strategy block (65536).</summary>
        public const nuint kMaxCoeffArea = kMaxBlockDim * kMaxBlockDim;

        /// <summary>Total number of valid <see cref="AcStrategyType"/> values (27).</summary>
        public const byte kNumValidStrategies = (byte)AcStrategyType.DCT128X256 + 1;

        private const uint MultiblockMask =
            (1u << (int)AcStrategyType.DCT16X16) |
            (1u << (int)AcStrategyType.DCT32X32) |
            (1u << (int)AcStrategyType.DCT16X8) |
            (1u << (int)AcStrategyType.DCT8X16) |
            (1u << (int)AcStrategyType.DCT32X8) |
            (1u << (int)AcStrategyType.DCT8X32) |
            (1u << (int)AcStrategyType.DCT16X32) |
            (1u << (int)AcStrategyType.DCT32X16) |
            (1u << (int)AcStrategyType.DCT32X64) |
            (1u << (int)AcStrategyType.DCT64X32) |
            (1u << (int)AcStrategyType.DCT64X64) |
            (1u << (int)AcStrategyType.DCT64X128) |
            (1u << (int)AcStrategyType.DCT128X64) |
            (1u << (int)AcStrategyType.DCT128X128) |
            (1u << (int)AcStrategyType.DCT128X256) |
            (1u << (int)AcStrategyType.DCT256X128) |
            (1u << (int)AcStrategyType.DCT256X256);

        private static readonly byte[] CoveredBlocksXLut =
        {
            1, 1, 1, 1, 2, 4, 1, 2, 1,
            4, 2, 4, 1, 1, 1, 1, 1, 1,
            8, 4, 8, 16, 8, 16, 32, 16, 32,
        };

        private static readonly byte[] CoveredBlocksYLut =
        {
            1, 1, 1, 1, 2, 4, 2, 1, 4,
            1, 4, 2, 1, 1, 1, 1, 1, 1,
            8, 8, 4, 16, 16, 8, 32, 32, 16,
        };

        private static readonly byte[] Log2CoveredBlocksLut =
        {
            0, 0, 0, 0, 2, 4, 1, 1, 2,
            2, 3, 3, 0, 0, 0, 0, 0, 0,
            6, 5, 5, 8, 7, 7, 10, 9, 9,
        };

        private readonly AcStrategyType _strategy;
        private readonly bool _isFirst;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal AcStrategy(AcStrategyType strategy, bool isFirst)
        {
            _strategy = strategy;
            _isFirst  = isFirst;
            Debug.Assert(IsMultiblock() || isFirst);
        }

        /// <summary>
        /// Returns the bitmask corresponding to <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The AC strategy type.</param>
        /// <returns><c>1u &lt;&lt; (int)type</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint TypeBit(AcStrategyType type)
        {
            return 1u << (int)type;
        }

        /// <summary>
        /// Returns true if this block is the first 8x8 block (i.e. top-left) of a
        /// possibly multi-block strategy.
        /// </summary>
        /// <returns><see langword="true"/> if this is the top-left 8x8 block of the transform.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsFirstBlock() => _isFirst;

        /// <summary>
        /// Returns true if this strategy spans more than one 8x8 block.
        /// </summary>
        /// <returns><see langword="true"/> if the strategy spans multiple 8x8 blocks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMultiblock()
        {
            return ((1u << (int)_strategy) & MultiblockMask) != 0;
        }

        /// <summary>
        /// Returns the raw strategy value. Should only be used for tokenization.
        /// </summary>
        /// <returns>Raw strategy byte value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte RawStrategy() => (byte)_strategy;

        /// <summary>
        /// Returns the <see cref="AcStrategyType"/> of this strategy.
        /// </summary>
        /// <returns>The <see cref="AcStrategyType"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AcStrategyType Strategy() => _strategy;

        /// <summary>
        /// Checks whether <paramref name="rawStrategy"/> is within the valid strategy range.
        /// </summary>
        /// <param name="rawStrategy">Raw strategy integer value.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRawStrategyValid(int rawStrategy)
        {
            return rawStrategy < kNumValidStrategies && rawStrategy >= 0;
        }

        /// <summary>
        /// Creates a top-left <see cref="AcStrategy"/> from a raw strategy byte.
        /// </summary>
        /// <param name="rawStrategy">Raw strategy index.</param>
        /// <returns>An <see cref="AcStrategy"/> instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AcStrategy FromRawStrategy(byte rawStrategy)
        {
            return FromRawStrategy((AcStrategyType)rawStrategy);
        }

        /// <summary>
        /// Creates a top-left <see cref="AcStrategy"/> from an <see cref="AcStrategyType"/>.
        /// </summary>
        /// <param name="rawStrategy">Strategy type.</param>
        /// <returns>An <see cref="AcStrategy"/> instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AcStrategy FromRawStrategy(AcStrategyType rawStrategy)
        {
            Debug.Assert(IsRawStrategyValid((int)rawStrategy));

            return new AcStrategy(rawStrategy, isFirst: true);
        }

        // "Natural order" means the order of increasing of "anisotropic" frequency of
        // continuous version of DCT basis.
        // Round-trip, for any given strategy s:
        //  X = NaturalCoeffOrder(s)[NaturalCoeffOrderLutN(s)[X]]
        //  X = NaturalCoeffOrderLut(s)[NaturalCoeffOrderN(s)[X]]

        /// <summary>
        /// Computes the natural coefficient order for this strategy into <paramref name="order"/>.
        /// </summary>
        /// <param name="order">Destination span receiving the natural coefficient indices.</param>
        public void ComputeNaturalCoeffOrder(Span<uint> order)
        {
            CoeffOrderAndLut(this, isLut: false, order);
        }

        /// <summary>
        /// Computes the natural coefficient order lookup table (inverse of <see cref="ComputeNaturalCoeffOrder"/>) into <paramref name="lut"/>.
        /// </summary>
        /// <param name="lut">Destination span receiving the natural coefficient LUT.</param>
        public void ComputeNaturalCoeffOrderLut(Span<uint> lut)
        {
            CoeffOrderAndLut(this, isLut: true, lut);
        }

        /// <summary>
        /// Number of 8x8 blocks along X that this strategy covers.
        /// </summary>
        /// <returns>Number of 8x8 blocks along X.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint CoveredBlocksX()
        {
            return CoveredBlocksXLut[(int)_strategy];
        }

        /// <summary>
        /// Number of 8x8 blocks along Y that this strategy covers.
        /// </summary>
        /// <returns>Number of 8x8 blocks along Y.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint CoveredBlocksY()
        {
            return CoveredBlocksYLut[(int)_strategy];
        }

        /// <summary>
        /// Base-2 logarithm of the total number of 8x8 blocks covered by this strategy.
        /// </summary>
        /// <returns><c>log2(CoveredBlocksX() * CoveredBlocksY())</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint Log2CoveredBlocks()
        {
            return Log2CoveredBlocksLut[(int)_strategy];
        }

        // Tries to generalize zig-zag order to non-square blocks. Surprisingly, in
        // square block frequency along the (i + j == const) diagonals is roughly the
        // same. For historical reasons, consecutive diagonals are traversed
        // in alternating directions - so called "zig-zag" (or "snake") order.
        private static void CoeffOrderAndLut(AcStrategy acs, bool isLut, Span<uint> output)
        {
            nuint cx = acs.CoveredBlocksX();
            nuint cy = acs.CoveredBlocksY();
            CoeffOrder.CoefficientLayout(ref cy, ref cx);

            // CoefficientLayout ensures cx >= cy.
            // We compute the zigzag order for a cx x cx block, then discard all the
            // lines that are not multiple of the ratio between cx and cy.
            nuint xs  = cx / cy;
            nuint xsm = xs - 1;
            int xss   = (int)Bits.CeilLog2Nonzero((ulong)xs);
            nuint cur = cx * cy;

            ComputeFirstHalfOrder(cx, cy, xsm, xss, isLut, output, ref cur);
            ComputeSecondHalfOrder(cx, xsm, xss, isLut, output, ref cur);
        }

        private static void ComputeFirstHalfOrder(
            nuint cx,
            nuint cy,
            nuint xsm,
            int xss,
            bool isLut,
            Span<uint> output,
            ref nuint cur)
        {
            nuint dim = cx * FrameDimensions.kBlockDim;

            for (nuint i = 0; i < dim; i++)
            {
                for (nuint j = 0; j <= i; j++)
                {
                    nuint x = j;
                    nuint y = i - j;

                    if ((i & 1) != 0)
                    {
                        (x, y) = (y, x);
                    }

                    if ((y & xsm) == 0)
                    {
                        StoreFirstHalfEntry(x, y >> xss, cx, cy, isLut, output, ref cur);
                    }
                }
            }
        }

        private static void StoreFirstHalfEntry(
            nuint x,
            nuint y,
            nuint cx,
            nuint cy,
            bool isLut,
            Span<uint> output,
            ref nuint cur)
        {
            nuint val = (x < cx && y < cy) ? y * cx + x : cur++;
            nuint pos = y * cx * FrameDimensions.kBlockDim + x;

            if (isLut)
            {
                output[(int)pos] = (uint)val;
            }
            else
            {
                output[(int)val] = (uint)pos;
            }
        }

        private static void ComputeSecondHalfOrder(
            nuint cx,
            nuint xsm,
            int xss,
            bool isLut,
            Span<uint> output,
            ref nuint cur)
        {
            nuint dim = cx * FrameDimensions.kBlockDim;

            for (nuint ip = dim - 1; ip > 0; ip--)
            {
                nuint i = ip - 1;

                for (nuint j = 0; j <= i; j++)
                {
                    nuint x = dim - 1 - (i - j);
                    nuint y = dim - 1 - j;

                    if ((i & 1) != 0)
                    {
                        (x, y) = (y, x);
                    }

                    if ((y & xsm) == 0)
                    {
                        StoreSecondHalfEntry((y >> xss) * dim + x, cur++, isLut, output);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreSecondHalfEntry(nuint pos, nuint val, bool isLut, Span<uint> output)
        {
            if (isLut)
            {
                output[(int)pos] = (uint)val;
            }
            else
            {
                output[(int)val] = (uint)pos;
            }
        }
    }

    /// <summary>
    /// Provides read-only indexed access to a single row of an <see cref="AcStrategyImage"/>.
    /// Ported from <c>jxl::AcStrategyRow</c> in <c>lib/jxl/ac_strategy.h</c>.
    /// </summary>
    public readonly unsafe struct AcStrategyRow
    {
        private readonly byte* _row;

        /// <summary>
        /// Initializes a new instance of the <see cref="AcStrategyRow"/> struct.
        /// </summary>
        /// <param name="row">Pointer to the start of the row in the underlying byte plane.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AcStrategyRow(byte* row)
        {
            _row = row;
        }

        /// <summary>
        /// Gets the <see cref="AcStrategy"/> at horizontal block index <paramref name="x"/>.
        /// </summary>
        /// <param name="x">Horizontal block index.</param>
        /// <returns>The <see cref="AcStrategy"/> at index <paramref name="x"/>.</returns>
        public AcStrategy this[nuint x]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                byte raw                = _row[x];
                AcStrategyType strategy = (AcStrategyType)(raw >> 1);
                bool isFirst            = (raw & 1) != 0;

                return new AcStrategy(strategy, isFirst);
            }
        }
    }

    /// <summary>
    /// Stores the AC strategy for each 8x8 block of a frame or group.
    /// Ported from <c>jxl::AcStrategyImage</c> in <c>lib/jxl/ac_strategy.h</c> and <c>lib/jxl/ac_strategy.cc</c>.
    /// </summary>
    public sealed unsafe class AcStrategyImage : IDisposable
    {
        // A value that does not represent a valid combined AC strategy
        // value. Used as a sentinel.
        private const byte INVALID = 0xFF;

        private ImagePlane<byte>? _layers;
        private byte* _row;
        private nuint _stride;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="AcStrategyImage"/> class.
        /// </summary>
        public AcStrategyImage()
        {
        }

        /// <summary>
        /// Creates a new <see cref="AcStrategyImage"/> with dimensions <paramref name="xsize"/> by <paramref name="ysize"/> blocks.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation.</param>
        /// <param name="xsize">Width in 8x8 blocks.</param>
        /// <param name="ysize">Height in 8x8 blocks.</param>
        /// <returns>A <see cref="StatusOr{AcStrategyImage}"/> containing the created image or an error.</returns>
        public static StatusOr<AcStrategyImage> Create(
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize)
        {
            JxlMemoryManager mm = memoryManager ?? JxlMemoryManager.Default;
            StatusOr<ImagePlane<byte>> planeResult = ImagePlane<byte>.Create(mm, xsize, ysize);

            if (!planeResult.IsOk)
            {
                return planeResult.Status;
            }

            AcStrategyImage img = new AcStrategyImage();
            img._layers = planeResult.Value;
            img._row    = img._layers.xsize > 0 && img._layers.ysize > 0 ? img._layers.Row(0) : null;
            img._stride = (nuint)img._layers.PixelsPerRow;

            return img;
        }

        /// <summary>
        /// Gets the width in 8x8 blocks.
        /// </summary>
        public nuint xsize => _layers?.xsize ?? 0;

        /// <summary>
        /// Gets the height in 8x8 blocks.
        /// </summary>
        public nuint ysize => _layers?.ysize ?? 0;

        /// <summary>
        /// Gets the stride in bytes/blocks per row.
        /// </summary>
        public nuint PixelsPerRow => (nuint)(_layers?.PixelsPerRow ?? 0);

        /// <summary>
        /// Gets the memory manager associated with this image.
        /// </summary>
        public JxlMemoryManager? MemoryManager => _layers?.MemoryManager;

        /// <summary>
        /// Fills the specified <paramref name="rect"/> with 8x8 DCT strategies.
        /// </summary>
        /// <param name="rect">Rectangle in block coordinates to fill.</param>
        public void FillDCT8(Rect rect)
        {
            if (_layers is null)
            {
                return;
            }

            ImageOps.FillPlane((byte)(((byte)AcStrategyType.DCT << 1) | 1), _layers, rect);
        }

        /// <summary>
        /// Fills the entire image with 8x8 DCT strategies.
        /// </summary>
        public void FillDCT8()
        {
            if (_layers is null)
            {
                return;
            }

            FillDCT8(new Rect(_layers));
        }

        /// <summary>
        /// Fills the entire image with the invalid sentinel value.
        /// </summary>
        public void FillInvalid()
        {
            if (_layers is null)
            {
                return;
            }

            ImageOps.FillImage(INVALID, _layers);
        }

        /// <summary>
        /// Sets the AC strategy at block position (<paramref name="x"/>, <paramref name="y"/>) without overlap checking.
        /// </summary>
        /// <param name="x">Block X coordinate.</param>
        /// <param name="y">Block Y coordinate.</param>
        /// <param name="type">Strategy type to set.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Set(nuint x, nuint y, AcStrategyType type)
        {
            AcStrategy acs = AcStrategy.FromRawStrategy(type);
            Debug.Assert(y + acs.CoveredBlocksY() <= ysize);
            Debug.Assert(x + acs.CoveredBlocksX() <= xsize);

            return SetNoBoundsCheck(x, y, type, check: false);
        }

        /// <summary>
        /// Sets the AC strategy at block position (<paramref name="x"/>, <paramref name="y"/>), optionally checking for block overlap.
        /// </summary>
        /// <param name="x">Block X coordinate.</param>
        /// <param name="y">Block Y coordinate.</param>
        /// <param name="type">Strategy type to set.</param>
        /// <param name="check">If <see langword="true"/>, verifies that covered blocks are currently marked invalid.</param>
        /// <returns><see cref="Status.OkStatus"/> on success, or failure on overlap.</returns>
        public Status SetNoBoundsCheck(nuint x, nuint y, AcStrategyType type, bool check = true)
        {
            AcStrategy acs = AcStrategy.FromRawStrategy(type);
            nuint covY     = acs.CoveredBlocksY();
            nuint covX     = acs.CoveredBlocksX();

            for (nuint iy = 0; iy < covY; iy++)
            {
                for (nuint ix = 0; ix < covX; ix++)
                {
                    nuint pos = (y + iy) * _stride + x + ix;

                    if (check && _row[pos] != INVALID)
                    {
                        return Status.Failure("Invalid AC strategy: block overlap");
                    }

                    _row[pos] = (byte)(((byte)type << 1) | ((iy | ix) == 0 ? 1 : 0));
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether the block at (<paramref name="x"/>, <paramref name="y"/>) has been assigned a valid strategy.
        /// </summary>
        /// <param name="x">Block X coordinate.</param>
        /// <param name="y">Block Y coordinate.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(nuint x, nuint y)
        {
            return _row[y * _stride + x] != INVALID;
        }

        /// <summary>
        /// Gets a read-only <see cref="AcStrategyRow"/> for row <paramref name="y"/> starting at <paramref name="xPrefix"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="xPrefix">Horizontal offset in blocks.</param>
        /// <returns>An <see cref="AcStrategyRow"/> for the requested row.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AcStrategyRow ConstRow(nuint y, nuint xPrefix = 0)
        {
            Debug.Assert(_layers is not null);

            return new AcStrategyRow(_layers.ConstRow(y) + xPrefix);
        }

        /// <summary>
        /// Gets a read-only <see cref="AcStrategyRow"/> for row <paramref name="y"/> relative to <paramref name="rect"/>.
        /// </summary>
        /// <param name="rect">Bounding rectangle in block coordinates.</param>
        /// <param name="y">Row offset within <paramref name="rect"/>.</param>
        /// <returns>An <see cref="AcStrategyRow"/> for the requested row.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AcStrategyRow ConstRow(Rect rect, nuint y)
        {
            return ConstRow((nuint)rect.Y0 + y, (nuint)rect.X0);
        }

        /// <summary>
        /// Counts the number of top-left blocks of a given <paramref name="type"/> in the image.
        /// </summary>
        /// <param name="type">Strategy type to count.</param>
        /// <returns>Number of blocks of the specified strategy type.</returns>
        public nuint CountBlocks(AcStrategyType type)
        {
            if (_layers is null)
            {
                return 0;
            }

            nuint ret   = 0;
            byte target = (byte)(((byte)type << 1) | 1);
            nuint h     = _layers.ysize;
            nuint w     = _layers.xsize;

            for (nuint y = 0; y < h; y++)
            {
                byte* row = _layers.ConstRow(y);

                for (nuint x = 0; x < w; x++)
                {
                    if (row[x] == target)
                    {
                        ret++;
                    }
                }
            }

            return ret;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _layers?.Dispose();
            _layers = null;
            _row    = null;
        }
    }
}
