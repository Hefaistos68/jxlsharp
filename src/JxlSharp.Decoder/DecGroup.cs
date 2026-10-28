// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    // Controls whether DecodeGroupImpl renders to pixels or not.

    /// <summary>
    /// Controls whether <see cref="DecGroup"/> renders decoded AC coefficients to pixels or only accumulates them.
    /// Ported from <c>jxl::DrawMode</c> in <c>lib/jxl/dec_group.cc</c>.
    /// </summary>
    public enum DrawMode
    {
        /// <summary>Render to pixels.</summary>
        Draw = 0,

        /// <summary>Don't render to pixels.</summary>
        DontDraw = 1,
    }

    // Interface for reading groups for DecodeGroupImpl.

    /// <summary>
    /// Abstract source of quantized AC blocks for <see cref="DecGroup"/>.
    /// Ported from <c>jxl::GetBlock</c> in <c>lib/jxl/dec_group.cc</c>.
    /// </summary>
    public abstract class GetBlock
    {
        /// <summary>
        /// Prepares row state at the start of block row <paramref name="by"/>.
        /// </summary>
        /// <param name="by">Block row index within the group.</param>
        public abstract void StartRow(nuint by);

        /// <summary>
        /// Loads or decodes the quantized AC coefficients for the transform block at (<paramref name="bx"/>, <paramref name="by"/>).
        /// </summary>
        /// <param name="bx">Horizontal block index within the group.</param>
        /// <param name="by">Vertical block index within the group.</param>
        /// <param name="acs">AC transform strategy for the block.</param>
        /// <param name="size">Number of coefficients per channel (<c>covered_blocks * 64</c>).</param>
        /// <param name="log2CoveredBlocks">Base-2 logarithm of the number of covered 8x8 blocks.</param>
        /// <param name="block">Span of 3 channel <see cref="ACPtr"/> destinations.</param>
        /// <param name="acType">Coefficient storage width (<see cref="ACType.k16"/> or <see cref="ACType.k32"/>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status LoadBlock(
            nuint bx,
            nuint by,
            AcStrategy acs,
            nuint size,
            nuint log2CoveredBlocks,
            Span<ACPtr> block,
            ACType acType);
    }

    // Structs used by DecodeGroupImpl to get a quantized block.
    // GetBlockFromBitstream uses ANS decoding (and thus keeps track of row
    // pointers in row_nzeros), GetBlockFromEncoder simply reads the coefficient
    // image provided by the encoder.

    /// <summary>
    /// Decodes quantized AC blocks from per-pass ANS bitstreams.
    /// Ported from <c>jxl::GetBlockFromBitstream</c> in <c>lib/jxl/dec_group.cc</c>.
    /// </summary>
    public sealed class GetBlockFromBitstream : GetBlock
    {
        /// <summary>
        /// Initialization arguments for <see cref="Init"/>.
        /// </summary>
        public struct InitArgs
        {
            /// <summary>Current frame header.</summary>
            public required FrameHeader FrameHeader;

            /// <summary>Per-pass bit readers.</summary>
            public required BitReader?[] Readers;

            /// <summary>Number of passes to decode in this call.</summary>
            public nuint NumPasses;

            /// <summary>AC group index.</summary>
            public nuint GroupIdx;

            /// <summary>Number of bits used to select the histogram cluster per pass.</summary>
            public nuint HistoSelectorBits;

            /// <summary>Block rectangle for this group.</summary>
            public Rect Rect;

            /// <summary>Per-thread group decoder scratch cache.</summary>
            public required GroupDecCache GroupDecCache;

            /// <summary>Per-frame decoder state.</summary>
            public required PassesDecoderState DecState;

            /// <summary>Index of the first pass being decoded in this call.</summary>
            public nuint FirstPass;
        }

        /// <summary>ANS symbol readers for each active pass.</summary>
        public readonly ANSSymbolReader[] Decoders = new ANSSymbolReader[FrameDimensions.KMaxNumPasses];

        /// <summary>Context base offsets for each active pass.</summary>
        public readonly nuint[] CtxOffset = new nuint[FrameDimensions.KMaxNumPasses];

        /// <summary>Current block row non-zero count memory slices (<c>[pass, channel]</c>).</summary>
        public readonly Memory<int>[,] RowNzeros = new Memory<int>[FrameDimensions.KMaxNumPasses, 3];

        /// <summary>Previous block row non-zero count memory slices (<c>[pass, channel]</c>).</summary>
        public readonly ReadOnlyMemory<int>[,] RowNzerosTop = new ReadOnlyMemory<int>[FrameDimensions.KMaxNumPasses, 3];

        /// <summary>Horizontal chroma subsampling shift per channel.</summary>
        public readonly nuint[] HShift = new nuint[3];

        /// <summary>Vertical chroma subsampling shift per channel.</summary>
        public readonly nuint[] VShift = new nuint[3];

        private static readonly int[] ChannelOrder = { 1, 0, 2 };

        private uint[] _shiftForPass = Array.Empty<uint>();
        private uint[] _coeffOrders = Array.Empty<uint>();
        private nuint _coeffOrderSize;
        private nuint _firstPass;
        private List<byte[]> _contextMap = new List<byte[]>();
        private BitReader?[] _readers = Array.Empty<BitReader?>();
        private nuint _numPasses;
        private nuint _nzerosStride;
        private GroupDecCache? _groupDecCache;
        private BlockCtxMap? _blockCtxMap;
        private ImagePlane<int>? _qf;
        private ImagePlane<byte>? _quantDc;
        private ReadOnlyMemory<int> _qfRow;
        private ReadOnlyMemory<byte> _quantDcRow;
        private Rect _rect;

        /// <summary>
        /// Initializes this <see cref="GetBlockFromBitstream"/> for the specified group and pass range.
        /// </summary>
        /// <param name="args">Initialization arguments.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Init(in InitArgs args)
        {
            for (nuint i = 0; i < 3; i++)
            {
                HShift[i] = args.FrameHeader.ChromaSubsampling.HShift(i);
                VShift[i] = args.FrameHeader.ChromaSubsampling.VShift(i);
            }

            _coeffOrderSize = args.DecState.Shared.CoeffOrderSize;
            _coeffOrders    = args.DecState.Shared.CoeffOrders;
            _firstPass      = args.FirstPass;
            _contextMap     = args.DecState.ContextMap;
            _readers        = args.Readers;
            _numPasses      = args.NumPasses;
            _shiftForPass   = args.FrameHeader.Passes.Shift;
            _groupDecCache  = args.GroupDecCache;
            _rect           = args.Rect;
            _blockCtxMap    = args.DecState.Shared.BlockCtxMap;
            _qf             = args.DecState.Shared.RawQuantField;
            _quantDc        = args.DecState.Shared.QuantDc;

            Status passInit = InitPassDecoders(in args);

            if (!passInit.IsOk)
            {
                return passInit;
            }

            _nzerosStride = (nuint)args.GroupDecCache.NumNzeroes[0].PixelsPerRow;

            for (nuint i = 0; i < _numPasses; i++)
            {
                if (_nzerosStride != (nuint)args.GroupDecCache.NumNzeroes[i].PixelsPerRow)
                {
                    return Status.Failure("Non-zero stride mismatch across passes");
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override void StartRow(nuint by)
        {
            _qfRow      = _qf!.RowMemory((nuint)_rect.Y0 + by).Slice(_rect.X0);
            _quantDcRow = _quantDc!.RowMemory((nuint)_rect.Y0 + by).Slice(_rect.X0);

            for (int c = 0; c < 3; c++)
            {
                nuint sby = by >> (int)VShift[c];

                for (nuint i = 0; i < _numPasses; i++)
                {
                    ImagePlane<int> nzPlane = _groupDecCache!.NumNzeroes[i].Plane((nuint)c);
                    RowNzeros[i, c]         = nzPlane.RowMemory(sby);
                    RowNzerosTop[i, c]      = sby == 0
                        ? ReadOnlyMemory<int>.Empty
                        : nzPlane.RowMemory(sby - 1);
                }
            }
        }

        /// <inheritdoc />
        public override Status LoadBlock(
            nuint bx,
            nuint by,
            AcStrategy acs,
            nuint size,
            nuint log2CoveredBlocks,
            Span<ACPtr> block,
            ACType acType)
        {
            _ = size;

            ReadOnlySpan<uint> baseOrders = _coeffOrders.AsSpan((int)(_firstPass * _coeffOrderSize));

            foreach (int c in ChannelOrder)
            {
                nuint sbx = bx >> (int)HShift[c];
                nuint sby = by >> (int)VShift[c];

                if ((sbx << (int)HShift[c]) != bx || (sby << (int)VShift[c]) != by)
                {
                    continue;
                }

                Status passStatus = DecodeChannelPasses(
                    c,
                    sbx,
                    sby,
                    bx,
                    acs,
                    log2CoveredBlocks,
                    baseOrders,
                    block[c],
                    acType);

                if (!passStatus.IsOk)
                {
                    return passStatus;
                }
            }

            return true;
        }

        private Status InitPassDecoders(in InitArgs args)
        {
            for (nuint pass = 0; pass < _numPasses; pass++)
            {
                // Select which histogram set to use among those of the current pass.
                nuint curHistogram = 0;

                if (args.HistoSelectorBits != 0)
                {
                    curHistogram = (nuint)_readers[pass]!.ReadBits(args.HistoSelectorBits);
                }

                if (curHistogram >= args.DecState.Shared.NumHistograms)
                {
                    return Status.Failure("Invalid histogram selector");
                }

                CtxOffset[pass] = curHistogram * _blockCtxMap!.NumACContexts();

                StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(
                    args.DecState.Code[(int)(pass + _firstPass)],
                    _readers[pass]!);

                if (!readerOr.IsOk)
                {
                    return readerOr.Status;
                }

                Decoders[pass] = readerOr.Value;
            }

            return true;
        }

        private Status DecodeChannelPasses(
            int c,
            nuint sbx,
            nuint sby,
            nuint bx,
            AcStrategy acs,
            nuint log2CoveredBlocks,
            ReadOnlySpan<uint> baseOrders,
            ACPtr channelBlock,
            ACType acType)
        {
            for (nuint pass = 0; pass < _numPasses; pass++)
            {
                ANSSymbolReader decoder         = Decoders[pass];
                DecGroup.AcVarBlockArgs varArgs = new DecGroup.AcVarBlockArgs
                {
                    CtxOffset         = CtxOffset[pass],
                    Log2CoveredBlocks = log2CoveredBlocks,
                    RowNzeros         = RowNzeros[pass, c].Span,
                    RowNzerosTop      = RowNzerosTop[pass, c].Span,
                    NzerosStride      = _nzerosStride,
                    C                 = (nuint)c,
                    Bx                = sbx,
                    By                = sby,
                    Lbx               = bx,
                    Acs               = acs,
                    CoeffOrder        = baseOrders.Slice((int)(pass * _coeffOrderSize)),
                    Br                = _readers[pass]!,
                    Decoder           = decoder,
                    ContextMap        = _contextMap[(int)(pass + _firstPass)],
                    QdcRow            = _quantDcRow.Span,
                    QfRow             = _qfRow.Span,
                    BlockCtxMap       = _blockCtxMap!,
                    Block             = channelBlock,
                    Shift             = _shiftForPass[_firstPass + pass],
                    AcType            = acType,
                    UsesLz77          = decoder.UsesLZ77(),
                };

                Status status = DecGroup.DecodeACVarBlock(in varArgs);

                if (!status.IsOk)
                {
                    return status;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Reads pre-quantized AC blocks provided directly by the encoder for roundtrip decoding.
    /// Ported from <c>jxl::GetBlockFromEncoder</c> in <c>lib/jxl/dec_group.cc</c>.
    /// </summary>
    public sealed class GetBlockFromEncoder : GetBlock
    {
        private readonly IReadOnlyList<ACImage> _quantizedAc;
        private readonly Memory<int>[,] _rows = new Memory<int>[FrameDimensions.KMaxNumPasses, 3];
        private readonly uint[] _shiftForPass;
        private nuint _offset;

        private GetBlockFromEncoder(IReadOnlyList<ACImage> ac, ReadOnlySpan<uint> shiftForPass)
        {
            _quantizedAc  = ac;
            _shiftForPass = shiftForPass.ToArray();
        }

        /// <summary>
        /// Creates a new <see cref="GetBlockFromEncoder"/> for <paramref name="groupIdx"/>.
        /// </summary>
        /// <param name="ac">Per-pass quantized 32-bit AC coefficient images.</param>
        /// <param name="groupIdx">Group index.</param>
        /// <param name="shiftForPass">Per-pass quantization bit shifts.</param>
        /// <returns><see cref="StatusOr{GetBlockFromEncoder}"/> containing the initialized reader.</returns>
        public static StatusOr<GetBlockFromEncoder> Create(
            IReadOnlyList<ACImage> ac,
            nuint groupIdx,
            ReadOnlySpan<uint> shiftForPass)
        {
            GetBlockFromEncoder result = new GetBlockFromEncoder(ac, shiftForPass);

            // TODO(veluca): not supported with chroma subsampling.
            for (int i = 0; i < ac.Count; i++)
            {
                if (ac[i].Type != ACType.k32)
                {
                    return Status.Failure("GetBlockFromEncoder requires 32-bit ACImage");
                }

                for (int c = 0; c < 3; c++)
                {
                    result._rows[i, c] = ac[i].PlaneRow((nuint)c, groupIdx, 0).Mem32;
                }
            }

            return result;
        }

        /// <inheritdoc />
        public override void StartRow(nuint by)
        {
        }

        /// <inheritdoc />
        public override Status LoadBlock(
            nuint bx,
            nuint by,
            AcStrategy acs,
            nuint size,
            nuint log2CoveredBlocks,
            Span<ACPtr> block,
            ACType acType)
        {
            _ = bx;
            _ = by;
            _ = acs;
            _ = log2CoveredBlocks;

            if (acType != ACType.k32)
            {
                return Status.Failure("GetBlockFromEncoder requires ACType.k32");
            }

            int passCount = _quantizedAc.Count;

            for (int c = 0; c < 3; c++)
            {
                Span<int> dst = block[c].Span32;

                // for each pass
                for (int i = 0; i < passCount; i++)
                {
                    ReadOnlySpan<int> srcRow = _rows[i, c].Span.Slice((int)_offset);
                    int mul                  = 1 << (int)_shiftForPass[i];

                    for (int k = 0; k < (int)size; k++)
                    {
                        dst[k] += srcRow[k] * mul;
                    }
                }
            }

            _offset += size;

            return true;
        }
    }

    /// <summary>
    /// Decodes VarDCT AC groups, dequantizes transform blocks, and transforms coefficients to pixels or JPEG DCT blocks.
    /// Ported from <c>lib/jxl/dec_group.h</c> and <c>lib/jxl/dec_group.cc</c>.
    /// </summary>
    public static class DecGroup
    {
        private static readonly int[] ChannelOrder = { 1, 0, 2 };

        /// <summary>
        /// Arguments for <see cref="DecodeGroup(in DecodeGroupArgs, out bool)"/>.
        /// </summary>
        public struct DecodeGroupArgs
        {
            /// <summary>Current frame header.</summary>
            public required FrameHeader FrameHeader;

            /// <summary>Array of per-pass bit readers.</summary>
            public required BitReader?[] Readers;

            /// <summary>Number of new passes to decode.</summary>
            public nuint NumPasses;

            /// <summary>AC group index.</summary>
            public nuint GroupIdx;

            /// <summary>Per-frame passes decoder state.</summary>
            public required PassesDecoderState DecState;

            /// <summary>Per-thread group decoding scratch cache.</summary>
            public required GroupDecCache GroupDecCache;

            /// <summary>Worker thread index.</summary>
            public nuint Thread;

            /// <summary>Render pipeline input buffer handle for this group.</summary>
            public required RenderPipelineInput RenderPipelineInput;

            /// <summary>Optional target <see cref="JPEGData"/> when reconstructing to JPEG.</summary>
            public JPEGData? JpegData;

            /// <summary>Index of the first pass being decoded in this call.</summary>
            public nuint FirstPass;

            /// <summary>Whether to force rendering to pixels even if not all passes are present.</summary>
            public bool ForceDraw;

            /// <summary>Whether only DC is available (no AC global/passes decoded yet).</summary>
            public bool DcOnly;
        }

        /// <summary>
        /// Arguments for <see cref="DecodeGroupForRoundtrip"/>.
        /// </summary>
        public struct DecodeGroupRoundtripArgs
        {
            /// <summary>Current frame header.</summary>
            public required FrameHeader FrameHeader;

            /// <summary>Pre-quantized per-pass AC coefficient images from the encoder.</summary>
            public required IReadOnlyList<ACImage> Ac;

            /// <summary>AC group index.</summary>
            public nuint GroupIdx;

            /// <summary>Per-frame passes decoder state.</summary>
            public required PassesDecoderState DecState;

            /// <summary>Per-thread group decoding scratch cache.</summary>
            public required GroupDecCache GroupDecCache;

            /// <summary>Worker thread index.</summary>
            public nuint Thread;

            /// <summary>Render pipeline input buffer handle for this group.</summary>
            public required RenderPipelineInput RenderPipelineInput;

            /// <summary>Optional target <see cref="JPEGData"/> when reconstructing to JPEG.</summary>
            public JPEGData? JpegData;
        }

        /// <summary>
        /// Parameters for <see cref="DecodeACVarBlock"/>.
        /// </summary>
        internal ref struct AcVarBlockArgs
        {
            public nuint CtxOffset;
            public nuint Log2CoveredBlocks;
            public Span<int> RowNzeros;
            public ReadOnlySpan<int> RowNzerosTop;
            public nuint NzerosStride;
            public nuint C;
            public nuint Bx;
            public nuint By;
            public nuint Lbx;
            public AcStrategy Acs;
            public ReadOnlySpan<uint> CoeffOrder;
            public required BitReader Br;
            public required ANSSymbolReader Decoder;
            public required byte[] ContextMap;
            public ReadOnlySpan<byte> QdcRow;
            public ReadOnlySpan<int> QfRow;
            public required BlockCtxMap BlockCtxMap;
            public ACPtr Block;
            public uint Shift;
            public ACType AcType;
            public bool UsesLz77;
        }

        private ref struct DequantBlockArgs
        {
            public float InvGlobalScale;
            public int Quant;
            public float XDmMultiplier;
            public float BDmMultiplier;
            public float XCcMul;
            public float BCcMul;
            public AcStrategyType Kind;
            public nuint Size;
            public required Quantizer Quantizer;
            public nuint CoveredBlocks;
            public ReadOnlySpan<nuint> Sbx;
            public ReadOnlyMemory<float>[] DcRows;
            public nuint DcStride;
            public ReadOnlySpan<float> Biases;
            public ReadOnlySpan<ACPtr> Qblock;
            public Span<float> Block;
            public Span<float> Scratch;
            public ACType AcType;
        }

        private struct GroupImplState
        {
            public required FrameHeader FrameHeader;
            public required GetBlock GetBlock;
            public required GroupDecCache GroupDecCache;
            public required PassesDecoderState DecState;
            public nuint GroupIdx;
            public required RenderPipelineInput RenderPipelineInput;
            public JPEGData? JpegData;
            public DrawMode Draw;
            public Rect BlockRect;
            public nuint XsizeBlocks;
            public nuint YsizeBlocks;
            public nuint DcStride;
            public float InvGlobalScale;
            public ACType AcType;
            public bool Accumulate;
            public nuint Offset;
            public bool JpegIsGray;
            public int[] JpegCMap;
            public int[] DcOff;
            public int[] ScaledQtable;
            public nuint[] HShift;
            public nuint[] VShift;
            public Rect[] ChannelRects;
            public nuint[] IdctStride;
            public ACPtr[] QBlock;
        }

        private struct RowLoopContext
        {
            public nuint By;
            public nuint[] Sby;
            public ReadOnlyMemory<int> RowQuant;
            public ReadOnlyMemory<float>[] DcRows;
            public AcStrategyRow AcsRow;
            public ReadOnlyMemory<sbyte> RowCmapX;
            public ReadOnlyMemory<sbyte> RowCmapB;
            public Memory<float>[] IdctRow;
        }

        /// <summary>
        /// Returns the mapping from JXL channel index (0=X, 1=Y, 2=B) to JPEG component index.
        /// Ported from <c>jxl::JpegOrder</c> in <c>lib/jxl/frame_header.h</c>.
        /// </summary>
        /// <param name="ct">Frame color transform.</param>
        /// <param name="isGray">Whether the JPEG image is single-channel grayscale.</param>
        /// <returns>3-element array mapping JXL channel index to JPEG component index.</returns>
        public static int[] JpegOrder(ColorTransform ct, bool isGray)
        {
            if (isGray)
            {
                return new int[] { 0, 0, 0 };
            }

            if (ct == ColorTransform.YCbCr)
            {
                return new int[] { 1, 0, 2 };
            }

            if (ct == ColorTransform.None)
            {
                return new int[] { 0, 1, 2 };
            }

            Debug.Fail("Internal logic error: JpegOrder called with XYB transform");

            return new int[] { 0, 1, 2 };
        }

        /// <summary>
        /// Transposes an 8x8 block of 32-bit integers in-place.
        /// Ported from <c>jxl::Transpose8x8InPlace</c> in <c>lib/jxl/dec_group.cc</c>.
        /// </summary>
        /// <param name="block">Span of at least 64 32-bit integers.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Transpose8x8InPlace(Span<int> block)
        {
            for (int x = 0; x < 8; x++)
            {
                for (int y = x + 1; y < 8; y++)
                {
                    int idx1    = (y * 8) + x;
                    int idx2    = (x * 8) + y;
                    int tmp     = block[idx1];
                    block[idx1] = block[idx2];
                    block[idx2] = tmp;
                }
            }
        }

        /// <summary>
        /// Applies the non-uniform quantization bias adjustment for channel <paramref name="c"/> to <paramref name="quantI"/>.
        /// Ported from <c>jxl::AdjustQuantBias</c> in <c>lib/jxl/quantizer-inl.h</c>.
        /// </summary>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <param name="quantI">Quantized integer coefficient.</param>
        /// <param name="biases">Span of 4-element bias array (<c>[biasX, biasY, biasB, numerator]</c>).</param>
        /// <returns>Bias-adjusted floating-point coefficient before dequantization scaling.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AdjustQuantBias(int c, int quantI, ReadOnlySpan<float> biases)
        {
            // If |x| is 1, kZeroBias creates a different bias for each channel.
            // We're implementing the following:
            // if (quant == 0) return 0;
            // if (quant == 1) return biases[c];
            // if (quant == -1) return -biases[c];
            // return quant - biases[3] / quant;
            if (quantI == 0)
            {
                return 0.0f;
            }

            if (quantI == 1)
            {
                return biases[c];
            }

            if (quantI == -1)
            {
                return -biases[c];
            }

            float quant = quantI;

            return quant - (biases[3] / quant);
        }

        /// <summary>
        /// Decodes a VarDCT AC group across <see cref="DecodeGroupArgs.NumPasses"/> passes and optionally renders to the pipeline.
        /// Ported from <c>jxl::DecodeGroup</c> in <c>lib/jxl/dec_group.cc</c>.
        /// </summary>
        /// <param name="args">Group decoding arguments.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeGroup(in DecodeGroupArgs args)
        {
            return DecodeGroup(in args, out _);
        }

        /// <summary>
        /// Decodes a VarDCT AC group across <see cref="DecodeGroupArgs.NumPasses"/> passes and outputs whether the render pipeline should run.
        /// Ported from <c>jxl::DecodeGroup</c> in <c>lib/jxl/dec_group.cc</c>.
        /// </summary>
        /// <param name="args">Group decoding arguments.</param>
        /// <param name="shouldRunPipeline">Outputs <c>true</c> if the render pipeline should be executed for this group.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeGroup(in DecodeGroupArgs args, out bool shouldRunPipeline)
        {
            DrawMode draw = ((args.NumPasses + args.FirstPass) == args.FrameHeader.Passes.NumPasses) || args.ForceDraw
                ? DrawMode.Draw
                : DrawMode.DontDraw;

            shouldRunPipeline = draw != DrawMode.DontDraw;

            if (draw == DrawMode.Draw && args.NumPasses == 0 && args.FirstPass == 0)
            {
                return UpsampleDcOnlyGroup(in args);
            }

            nuint histoSelectorBits = 0;

            if (args.DcOnly)
            {
                if (args.NumPasses != 0)
                {
                    return Status.Failure("num_passes must be 0 when dc_only is true");
                }
            }
            else
            {
                if (args.DecState.Shared.NumHistograms == 0)
                {
                    return Status.Failure("num_histograms must be > 0");
                }

                histoSelectorBits = (nuint)Bits.CeilLog2Nonzero(args.DecState.Shared.NumHistograms);
            }

            GetBlockFromBitstream getBlock          = new GetBlockFromBitstream();
            GetBlockFromBitstream.InitArgs initArgs = new GetBlockFromBitstream.InitArgs
            {
                FrameHeader       = args.FrameHeader,
                Readers           = args.Readers,
                NumPasses         = args.NumPasses,
                GroupIdx          = args.GroupIdx,
                HistoSelectorBits = histoSelectorBits,
                Rect              = args.DecState.Shared.FrameDim.BlockGroupRect(args.GroupIdx),
                GroupDecCache     = args.GroupDecCache,
                DecState          = args.DecState,
                FirstPass         = args.FirstPass,
            };

            Status initStatus = getBlock.Init(in initArgs);

            if (!initStatus.IsOk)
            {
                return initStatus;
            }

            Status implStatus = DecodeGroupImpl(
                args.FrameHeader,
                getBlock,
                args.GroupDecCache,
                args.DecState,
                args.GroupIdx,
                args.RenderPipelineInput,
                (args.JpegData, draw));

            if (!implStatus.IsOk)
            {
                return implStatus;
            }

            for (nuint pass = 0; pass < args.NumPasses; pass++)
            {
                if (!getBlock.Decoders[pass].CheckANSFinalState())
                {
                    return Status.Failure("ANS checksum failure.");
                }
            }

            return true;
        }

        /// <summary>
        /// Decodes a VarDCT AC group from encoder-supplied quantized AC coefficients for encoder roundtrip.
        /// Ported from <c>jxl::DecodeGroupForRoundtrip</c> in <c>lib/jxl/dec_group.cc</c>.
        /// </summary>
        /// <param name="args">Roundtrip group decoding arguments.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeGroupForRoundtrip(in DecodeGroupRoundtripArgs args)
        {
            JxlMemoryManager memoryManager = args.DecState.MemoryManager;

            StatusOr<GetBlockFromEncoder> getBlockOr = GetBlockFromEncoder.Create(
                args.Ac,
                args.GroupIdx,
                args.FrameHeader.Passes.Shift);

            if (!getBlockOr.IsOk)
            {
                return getBlockOr.Status;
            }

            uint allStrategiesMask = (1u << AcStrategy.kNumValidStrategies) - 1u;
            Status cacheStatus     = args.GroupDecCache.InitOnce(memoryManager, 0, allStrategiesMask);

            if (!cacheStatus.IsOk)
            {
                return cacheStatus;
            }

            return DecodeGroupImpl(
                args.FrameHeader,
                getBlockOr.Value,
                args.GroupDecCache,
                args.DecState,
                args.GroupIdx,
                args.RenderPipelineInput,
                (args.JpegData, DrawMode.Draw));
        }

        // Decode quantized AC coefficients of DCT blocks.
        // LLF components in the output block will not be modified.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        internal static Status DecodeACVarBlock(in AcVarBlockArgs args)
        {
            // Equal to number of LLF coefficients.
            nuint coveredBlocks = 1u << (int)args.Log2CoveredBlocks;
            nuint size          = coveredBlocks * FrameDimensions.kDCTBlockSize;
            int predictedNzeros = PredictFromTopAndLeft(args.RowNzerosTop, args.RowNzeros, args.Bx, 32);

            nuint ord                = CoeffOrder.StrategyOrder[args.Acs.RawStrategy()];
            ReadOnlySpan<uint> order = args.CoeffOrder.Slice((int)CoeffOrder.CoeffOrderOffset((int)ord, (int)args.C));

            nuint blockCtx = args.BlockCtxMap.Context(
                args.QdcRow[(int)args.Lbx],
                (uint)args.QfRow[(int)args.Lbx],
                ord,
                args.C);
            nuint nzeroCtx = args.BlockCtxMap.NonZeroContext((uint)predictedNzeros, (uint)blockCtx) + args.CtxOffset;

            nuint nzeros = args.Decoder.ReadHybridUintInlined(args.UsesLz77, nzeroCtx, args.Br, args.ContextMap);

            if (nzeros > size - coveredBlocks)
            {
                return Status.Failure($"Invalid AC: nzeros {nzeros} too large for {coveredBlocks} 8x8 blocks");
            }

            FillBlockNonZeros(in args, coveredBlocks, nzeros);

            nuint histoOffset = args.CtxOffset + args.BlockCtxMap.ZeroDensityContextsOffset((uint)blockCtx);

            return ReadAcCoefficientsLoop(in args, coveredBlocks, size, order, histoOffset, nzeros);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FillBlockNonZeros(in AcVarBlockArgs args, nuint coveredBlocks, nuint nzeros)
        {
            int shiftedNzeros = (int)((nzeros + coveredBlocks - 1u) >> (int)args.Log2CoveredBlocks);
            nuint covY        = args.Acs.CoveredBlocksY();
            nuint covX        = args.Acs.CoveredBlocksX();

            for (nuint y = 0; y < covY; y++)
            {
                Span<int> row = args.RowNzeros.Slice((int)(args.Bx + (y * args.NzerosStride)));

                for (int x = 0; x < (int)covX; x++)
                {
                    row[x] = shiftedNzeros;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status ReadAcCoefficientsLoop(
            in AcVarBlockArgs args,
            nuint coveredBlocks,
            nuint size,
            ReadOnlySpan<uint> order,
            nuint histoOffset,
            nuint nzeros)
        {
            nuint prev        = nzeros > (size / 16u) ? 0u : 1u;
            int shift         = (int)args.Shift;
            bool is16         = args.AcType == ACType.k16;
            Span<short> ptr16 = is16 ? args.Block.Span16 : Span<short>.Empty;
            Span<int> ptr32   = is16 ? Span<int>.Empty : args.Block.Span32;
            byte[] contextMap = args.ContextMap;

            for (nuint k = coveredBlocks; k < size && nzeros != 0; ++k)
            {
                nuint ctx = histoOffset + AcContext.ZeroDensityContext(
                    nzeros,
                    k,
                    coveredBlocks,
                    args.Log2CoveredBlocks,
                    prev);
                uint uCoeff = args.Decoder.ReadHybridUintInlined(args.UsesLz77, ctx, args.Br, contextMap);

                if (uCoeff == 0u)
                {
                    prev = 0u;
                    continue;
                }

                // Hand-rolled version of UnpackSigned, shifting before the conversion to
                // signed integer to avoid undefined behavior of shifting negative numbers.
                nint magnitude = (nint)(uCoeff >> 1);
                nint negSign   = (nint)((~uCoeff) & 1u);
                int coeff      = (int)((magnitude ^ (negSign - 1)) << shift);

                int pos = (int)order[(int)k];

                if (is16)
                {
                    ptr16[pos] = (short)(ptr16[pos] + coeff);
                }
                else
                {
                    ptr32[pos] += coeff;
                }

                prev    = uCoeff != 0u ? 1u : 0u;
                nzeros -= prev;
            }

            if (nzeros != 0)
            {
                return Status.Failure(
                    $"Invalid AC: nzeros at end of block is {nzeros}, should be 0. Block ({args.Bx}, {args.By}), channel {args.C}");
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PredictFromTopAndLeft(ReadOnlySpan<int> rowTop, ReadOnlySpan<int> row, nuint x, int defaultVal)
        {
            if (x == 0)
            {
                return rowTop.IsEmpty ? defaultVal : rowTop[0];
            }

            if (rowTop.IsEmpty)
            {
                return row[(int)x - 1];
            }

            return (rowTop[(int)x] + row[(int)x - 1] + 1) / 2;
        }

        private static Status UpsampleDcOnlyGroup(in DecodeGroupArgs args)
        {
            JxlMemoryManager memoryManager = args.DecState.MemoryManager;
            Status initStatus              = args.GroupDecCache.InitDCBufferOnce(memoryManager);

            if (!initStatus.IsOk)
            {
                return initStatus;
            }

            YCbCrChromaSubsampling cs = args.FrameHeader.ChromaSubsampling;
            Rect srcRectPrecs         = args.DecState.Shared.FrameDim.BlockGroupRect(args.GroupIdx);

            for (nuint c = 0; c < 3; c++)
            {
                Status chStatus = UpsampleDcOnlyChannel(in args, cs, srcRectPrecs, c);

                if (!chStatus.IsOk)
                {
                    return chStatus;
                }
            }

            return true;
        }

        private static Status UpsampleDcOnlyChannel(
            in DecodeGroupArgs args,
            YCbCrChromaSubsampling cs,
            Rect srcRectPrecs,
            nuint c)
        {
            int hs = (int)cs.HShift(c);
            int vs = (int)cs.VShift(c);

            // We reuse filter_input_storage here as it is not currently in use.
            Rect srcRect = new Rect(
                srcRectPrecs.X0 >> hs,
                srcRectPrecs.Y0 >> vs,
                srcRectPrecs.XSize >> hs,
                srcRectPrecs.YSize >> vs);
            Rect copyRect = new Rect(
                RenderPipelineStage.KRenderPipelineXOffset,
                2,
                srcRect.XSize,
                srcRect.YSize);

            ImagePlane<float> dcPlane  = args.DecState.Shared.Dc.Plane(c);
            ImagePlane<float> dcBuffer = args.GroupDecCache.DcBuffer;

            Status copyStatus = ImageOps.CopyImageToWithPadding(srcRect, dcPlane, 2, copyRect, dcBuffer);

            if (!copyStatus.IsOk)
            {
                return copyStatus;
            }

            MirrorPadDcBuffer(srcRect, dcPlane, args.DecState.Shared.Dc.XSize, hs, dcBuffer);

            return RunUpsampler8xRows(in args, srcRect, dcPlane, vs, c, dcBuffer);
        }

        private static void MirrorPadDcBuffer(
            Rect srcRect,
            ImagePlane<float> dcPlane,
            nuint fullDcXSize,
            int hs,
            ImagePlane<float> dcBuffer)
        {
            // Mirrorpad. Interleaving left and right padding ensures that padding
            // works out correctly even for images with DC size of 1.
            nuint totalRows = (nuint)srcRect.YSize + 4u;
            int xend        = RenderPipelineStage.KRenderPipelineXOffset +
                              (int)(dcPlane.XSize >> hs) -
                              srcRect.X0;
            bool padLeft    = srcRect.X0 == 0;
            bool padRight   = (nuint)(srcRect.X0 + srcRect.XSize + 2) >= (fullDcXSize >> hs);

            for (nuint y = 0; y < totalRows; y++)
            {
                Span<float> row = dcBuffer.Row(y);

                for (int ix = 0; ix < 2; ix++)
                {
                    if (padLeft)
                    {
                        row[RenderPipelineStage.KRenderPipelineXOffset - ix - 1] =
                            row[RenderPipelineStage.KRenderPipelineXOffset + ix];
                    }

                    if (padRight)
                    {
                        row[xend + ix] = row[xend - ix - 1];
                    }
                }
            }
        }

        private static Status RunUpsampler8xRows(
            in DecodeGroupArgs args,
            Rect srcRect,
            ImagePlane<float> dcPlane,
            int vs,
            nuint c,
            ImagePlane<float> dcBuffer)
        {
            (ImagePlane<float> upsamplingDst, Rect dstRect) = args.RenderPipelineInput.GetBuffer(c);

            if (!dstRect.IsInside(upsamplingDst))
            {
                return Status.Failure("Upsampling destination rect outside buffer");
            }

            PipelineRow[][] inputRows  = new PipelineRow[][] { new PipelineRow[5] };
            PipelineRow[][] outputRows = new PipelineRow[][] { new PipelineRow[8] };
            long dcPlaneYSize          = (long)(dcPlane.YSize >> vs);
            nuint yStart               = (nuint)srcRect.Y0;
            nuint yEnd                 = (nuint)(srcRect.Y0 + srcRect.YSize);

            for (nuint y = yStart; y < yEnd; y++)
            {
                for (long iy = 0; iy < 5; iy++)
                {
                    long mirroredY   = ImageOps.Mirror((long)y + iy - 2L, dcPlaneYSize);
                    nuint bufferRowY = (nuint)(mirroredY + 2L - srcRect.Y0);
                    inputRows[0][iy] = new PipelineRow(dcBuffer, bufferRowY);
                }

                for (nuint iy = 0; iy < 8; iy++)
                {
                    nuint dstRowY     = ((y - yStart) << 3) + iy;
                    outputRows[0][iy] = new PipelineRow(
                        upsamplingDst,
                        (nuint)dstRect.Y0 + dstRowY,
                        dstRect.X0 - RenderPipelineStage.KRenderPipelineXOffset);
                }

                // Arguments set to 0/nullptr are not used.
                Status procStatus = args.DecState.Upsampler8x!.ProcessRow(
                    inputRows,
                    outputRows,
                    (0, 0),
                    (nuint)srcRect.XSize,
                    0,
                    0,
                    args.Thread);

                if (!procStatus.IsOk)
                {
                    return procStatus;
                }
            }

            return true;
        }

        private static Status DecodeGroupImpl(
            FrameHeader frameHeader,
            GetBlock getBlock,
            GroupDecCache groupDecCache,
            PassesDecoderState decState,
            nuint groupIdx,
            RenderPipelineInput renderPipelineInput,
            (JPEGData? JpegData, DrawMode Draw) mode)
        {
            Rect blockRect = decState.Shared.FrameDim.BlockGroupRect(groupIdx);

            GroupImplState state = new GroupImplState
            {
                FrameHeader         = frameHeader,
                GetBlock            = getBlock,
                GroupDecCache       = groupDecCache,
                DecState            = decState,
                GroupIdx            = groupIdx,
                RenderPipelineInput = renderPipelineInput,
                JpegData            = mode.JpegData,
                Draw                = mode.Draw,
                BlockRect           = blockRect,
                XsizeBlocks         = (nuint)blockRect.XSize,
                YsizeBlocks         = (nuint)blockRect.YSize,
                DcStride            = (nuint)decState.Shared.Dc.PixelsPerRow,
                InvGlobalScale      = decState.Shared.Quantizer.InvGlobalScale(),
                AcType              = decState.Coefficients.Type,
                Accumulate          = !decState.Coefficients.IsEmpty,
                Offset              = 0,
                JpegCMap            = new int[3],
                DcOff               = new int[3],
                ScaledQtable        = new int[64 * 3],
                HShift              = new nuint[3],
                VShift              = new nuint[3],
                ChannelRects        = new Rect[3],
                IdctStride          = new nuint[3],
                QBlock              = new ACPtr[3],
            };

            if (state.JpegData == null && state.Draw != DrawMode.DontDraw)
            {
                for (nuint c = 0; c < 3; c++)
                {
                    state.IdctStride[c] = (nuint)renderPipelineInput.GetBuffer(c).Plane.PixelsPerRow;
                }
            }

            if (state.JpegData != null)
            {
                Status jpegInit = InitJpegGroupState(ref state);

                if (!jpegInit.IsOk)
                {
                    return jpegInit;
                }
            }

            Status rectStatus = InitChannelSubsampledRects(ref state);

            if (!rectStatus.IsOk)
            {
                return rectStatus;
            }

            return ProcessGroupBlockRows(ref state);
        }

        private static Status InitJpegGroupState(ref GroupImplState state)
        {
            ColorCorrelation colorCorrelation = state.DecState.Shared.Cmap.Base;

            if (!colorCorrelation.IsJPEGCompatible())
            {
                return Status.Failure("The CfL map is not JPEG-compatible");
            }

            state.JpegIsGray = state.JpegData!.Components.Count == 1;

            if (state.FrameHeader.ColorTransform == ColorTransform.XYB)
            {
                return Status.Failure("Cannot decode XYB frame to JPEG");
            }

            state.JpegCMap = JpegOrder(state.FrameHeader.ColorTransform, state.JpegIsGray);
            IReadOnlyList<QuantEncoding> qe = state.DecState.Shared.Matrices.Encodings;

            if (qe.Count == 0 ||
                qe[0].Mode != QuantEncoding.QuantMode.kQuantModeRAW ||
                MathF.Abs(qe[0].QrawTableDen - (1.0f / (8.0f * 255.0f))) > 1e-8f)
            {
                return Status.Failure("Quantization table is not a JPEG quantization table.");
            }

            if (qe[0].QrawTable == null || qe[0].QrawTable!.Length != 3 * 8 * 8)
            {
                return Status.Failure("Invalid raw quantization table size for JPEG.");
            }

            return PopulateScaledJpegQtable(ref state, qe[0].QrawTable!);
        }

        private static Status PopulateScaledJpegQtable(ref GroupImplState state, int[] qtable)
        {
            for (int c = 0; c < 3; c++)
            {
                if (state.FrameHeader.ColorTransform == ColorTransform.None)
                {
                    state.DcOff[c] = 1024 / qtable[64 * c];
                }

                for (int i = 0; i < 64; i++)
                {
                    // Transpose the matrix, as it will be used on the transposed block.
                    int num = qtable[64 + i];
                    int den = qtable[(64 * c) + i];

                    if (num <= 0 || den <= 0 || num >= 65536 || den >= 65536)
                    {
                        return Status.Failure("Invalid JPEG quantization table");
                    }

                    state.ScaledQtable[(64 * c) + ((i % 8) * 8) + (i / 8)] =
                        (1 << ChromaFromLumaConstants.kCFLFixedPointPrecision) * num / den;
                }
            }

            return true;
        }

        private static Status InitChannelSubsampledRects(ref GroupImplState state)
        {
            YCbCrChromaSubsampling cs = state.FrameHeader.ChromaSubsampling;

            for (nuint i = 0; i < 3; i++)
            {
                int hs             = (int)cs.HShift(i);
                int vs             = (int)cs.VShift(i);
                state.HShift[i]    = (nuint)hs;
                state.VShift[i]    = (nuint)vs;
                Rect r             = new Rect(
                    state.BlockRect.X0 >> hs,
                    state.BlockRect.Y0 >> vs,
                    state.BlockRect.XSize >> hs,
                    state.BlockRect.YSize >> vs);
                state.ChannelRects[i] = r;

                ImagePlane<float> dcPlane = state.DecState.Shared.Dc.Plane(i);
                Rect dcBounds             = new Rect(0, 0, (int)dcPlane.XSize, (int)dcPlane.YSize);

                if (!r.IsInside(dcBounds))
                {
                    return Status.Failure("Frame dimensions are too big for the image.");
                }
            }

            return true;
        }

        private static Status ProcessGroupBlockRows(ref GroupImplState state)
        {
            nuint[] sby                  = new nuint[3];
            ReadOnlyMemory<float>[] dcRows = new ReadOnlyMemory<float>[3];
            Memory<float>[] idctRow      = new Memory<float>[3];

            for (nuint by = 0; by < state.YsizeBlocks; ++by)
            {
                state.GetBlock.StartRow(by);

                for (int c = 0; c < 3; c++)
                {
                    sby[c]            = by >> (int)state.VShift[c];
                    Rect chRect       = state.ChannelRects[c];
                    dcRows[c]         = state.DecState.Shared.Dc.Plane((nuint)c)
                        .RowMemory((nuint)chRect.Y0 + sby[c])
                        .Slice(chRect.X0);

                    if (state.JpegData == null && state.Draw != DrawMode.DontDraw)
                    {
                        (ImagePlane<float> plane, Rect rect) = state.RenderPipelineInput.GetBuffer((nuint)c);
                        idctRow[c] = plane
                            .RowMemory((nuint)rect.Y0 + (sby[c] * FrameDimensions.kBlockDim))
                            .Slice(rect.X0);
                    }
                }

                nuint ty = ((nuint)state.BlockRect.Y0 + by) / ChromaFromLumaConstants.kColorTileDimInBlocks;

                RowLoopContext rowCtx = new RowLoopContext
                {
                    By       = by,
                    Sby      = sby,
                    RowQuant = state.DecState.Shared.RawQuantField
                        .RowMemory((nuint)state.BlockRect.Y0 + by)
                        .Slice(state.BlockRect.X0),
                    DcRows   = dcRows,
                    AcsRow   = state.DecState.Shared.AcStrategy.ConstRow(state.BlockRect, by),
                    RowCmapX = state.DecState.Shared.Cmap.YtoXMap!.RowMemory(ty),
                    RowCmapB = state.DecState.Shared.Cmap.YtoBMap!.RowMemory(ty),
                    IdctRow  = idctRow,
                };

                Status rowStatus = ProcessSingleBlockRow(ref state, in rowCtx);

                if (!rowStatus.IsOk)
                {
                    return rowStatus;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status ProcessSingleBlockRow(ref GroupImplState state, in RowLoopContext rowCtx)
        {
            ColorCorrelation colorCorrelation = state.DecState.Shared.Cmap.Base;
            nuint numTilesX                   = Common.DivCeil(state.XsizeBlocks, ChromaFromLumaConstants.kColorTileDimInBlocks);
            nuint baseTileX                   = (nuint)state.BlockRect.X0 / ChromaFromLumaConstants.kColorTileDimInBlocks;
            nuint bx                          = 0;
            ReadOnlySpan<sbyte> rowCmapX      = rowCtx.RowCmapX.Span;
            ReadOnlySpan<sbyte> rowCmapB      = rowCtx.RowCmapB.Span;

            for (nuint tx = 0; tx < numTilesX; tx++)
            {
                nuint absTx   = tx + baseTileX;
                float xCcMul  = colorCorrelation.YtoXRatio(rowCmapX[(int)absTx]);
                float bCcMul  = colorCorrelation.YtoBRatio(rowCmapB[(int)absTx]);
                nuint tileEnd = (tx + 1u) * ChromaFromLumaConstants.kColorTileDimInBlocks;

                // Increment bx by llf_x because those iterations would otherwise
                // immediately continue (!IsFirstBlock). Reduces mispredictions.
                while (bx < state.XsizeBlocks && bx < tileEnd)
                {
                    AcStrategy acs = rowCtx.AcsRow[bx];
                    nuint llfX     = acs.CoveredBlocksX();

                    // Can only happen in the second or lower rows of a varblock.
                    if (!acs.IsFirstBlock())
                    {
                        bx += llfX;
                        continue;
                    }

                    Status blkStatus = ProcessSingleVarBlock(ref state, in rowCtx, bx, absTx, acs, xCcMul, bCcMul);

                    if (!blkStatus.IsOk)
                    {
                        return blkStatus;
                    }

                    bx += llfX;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status ProcessSingleVarBlock(
            ref GroupImplState state,
            in RowLoopContext rowCtx,
            nuint bx,
            nuint absTx,
            AcStrategy acs,
            float xCcMul,
            float bCcMul)
        {
            nuint log2CoveredBlocks = acs.Log2CoveredBlocks();
            nuint coveredBlocks     = 1u << (int)log2CoveredBlocks;
            nuint size              = coveredBlocks * FrameDimensions.kDCTBlockSize;

            ACPtr[] qblock  = state.QBlock;
            Status prepQblk = PrepareQBlockPointers(ref state, size, qblock);

            if (!prepQblk.IsOk)
            {
                return prepQblk;
            }

            Status loadStatus = state.GetBlock.LoadBlock(
                bx,
                rowCtx.By,
                acs,
                size,
                log2CoveredBlocks,
                qblock,
                state.AcType);

            if (!loadStatus.IsOk)
            {
                return loadStatus;
            }

            state.Offset += size;

            if (state.Draw == DrawMode.DontDraw)
            {
                return true;
            }

            Span<nuint> sbx = stackalloc nuint[3];
            sbx[0]          = bx >> (int)state.HShift[0];
            sbx[1]          = bx >> (int)state.HShift[1];
            sbx[2]          = bx >> (int)state.HShift[2];

            if (state.JpegData != null)
            {
                return ReconstructJpegBlock(ref state, in rowCtx, bx, absTx, acs, sbx, qblock);
            }

            DequantAndTransformToPixels(ref state, in rowCtx, (bx, size), acs, (xCcMul, bCcMul), sbx, qblock);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Status PrepareQBlockPointers(ref GroupImplState state, nuint size, Span<ACPtr> qblock)
        {
            if (state.Accumulate)
            {
                for (nuint c = 0; c < 3; c++)
                {
                    qblock[(int)c] = state.DecState.Coefficients.PlaneRow(c, state.GroupIdx, state.Offset);
                }

                return true;
            }

            // No point in reading from bitstream without accumulating and not drawing.
            if (state.Draw != DrawMode.Draw)
            {
                return Status.Failure("Cannot skip drawing when not accumulating coefficients");
            }

            if (state.AcType == ACType.k16)
            {
                state.GroupDecCache.DecGroupQBlock16.AsSpan(0, (int)(size * 3)).Clear();

                for (nuint c = 0; c < 3; c++)
                {
                    qblock[(int)c] = new ACPtr(state.GroupDecCache.DecGroupQBlock16.AsMemory((int)(c * size), (int)size));
                }
            }
            else
            {
                state.GroupDecCache.DecGroupQBlock.AsSpan(0, (int)(size * 3)).Clear();

                for (nuint c = 0; c < 3; c++)
                {
                    qblock[(int)c] = new ACPtr(state.GroupDecCache.DecGroupQBlock.AsMemory((int)(c * size), (int)size));
                }
            }

            return true;
        }

        private static Status ReconstructJpegBlock(
            ref GroupImplState state,
            in RowLoopContext rowCtx,
            nuint bx,
            nuint absTx,
            AcStrategy acs,
            ReadOnlySpan<nuint> sbx,
            ReadOnlySpan<ACPtr> qblock)
        {
            if (acs.Strategy() != AcStrategyType.DCT)
            {
                return Status.Failure("Can only decode to JPEG if only DCT-8 is used.");
            }

            Span<int> transposedDctY   = stackalloc int[64];
            YCbCrChromaSubsampling cs  = state.FrameHeader.ChromaSubsampling;
            ReadOnlySpan<sbyte> cmapX  = rowCtx.RowCmapX.Span;
            ReadOnlySpan<sbyte> cmapB  = rowCtx.RowCmapB.Span;
            bool noCfl                 = !cs.Is444() || (cmapX[(int)absTx] == 0 && cmapB[(int)absTx] == 0);

            foreach (int c in ChannelOrder)
            {
                // Propagate only Y for grayscale.
                if (state.JpegIsGray && c != 1)
                {
                    continue;
                }

                if ((sbx[c] << (int)state.HShift[c]) != bx || (rowCtx.Sby[c] << (int)state.VShift[c]) != rowCtx.By)
                {
                    continue;
                }

                Status chStatus = ReconstructJpegChannelBlock(
                    ref state,
                    in rowCtx,
                    (c, noCfl),
                    (absTx, sbx[c]),
                    qblock[c].Span32,
                    transposedDctY);

                if (!chStatus.IsOk)
                {
                    return chStatus;
                }
            }

            return true;
        }

        private static Status ReconstructJpegChannelBlock(
            ref GroupImplState state,
            in RowLoopContext rowCtx,
            (int C, bool NoCfl) chInfo,
            (nuint AbsTx, nuint SbxC) coords,
            Span<int> transposedDct,
            Span<int> transposedDctY)
        {
            int c                   = chInfo.C;
            JPEGComponent component = state.JpegData!.Components[state.JpegCMap[c]];
            Rect rc                 = state.ChannelRects[c];
            nuint blockOffset       = ((component.WidthInBlocks * ((nuint)rc.Y0 + rowCtx.Sby[c])) + (nuint)rc.X0 + coords.SbxC) *
                                      FrameDimensions.kDCTBlockSize;

            // JPEG XL is transposed, JPEG is not.
            Transpose8x8InPlace(transposedDct);

            Span<short> jpegPos = component.Coeffs.AsSpan((int)blockOffset, 64);

            if (chInfo.NoCfl)
            {
                for (int i = 0; i < 64; i++)
                {
                    jpegPos[i] = (short)transposedDct[i];
                }
            }
            else if (c == 1)
            {
                // Y channel: save for restoring X/B, but nothing else to do.
                for (int i = 0; i < 64; i++)
                {
                    int v             = transposedDct[i];
                    transposedDctY[i] = v;
                    jpegPos[i]        = (short)v;
                }
            }
            else
            {
                ApplyJpegCflToChannel(ref state, in rowCtx, c, coords.AbsTx, transposedDct, transposedDctY, jpegPos);
            }

            float dcVal = Math.Clamp(rowCtx.DcRows[c].Span[(int)coords.SbxC] - state.DcOff[c], -2047.0f, 2047.0f);
            jpegPos[0]  = (short)dcVal;

            for (int i = 0; i < 64; i++)
            {
                if (jpegPos[i] < -4095 || jpegPos[i] > 4095)
                {
                    return Status.Failure("JPEG DCT coefficients out of range");
                }
            }

            return true;
        }

        private static void ApplyJpegCflToChannel(
            ref GroupImplState state,
            in RowLoopContext rowCtx,
            int c,
            nuint absTx,
            ReadOnlySpan<int> transposedDct,
            ReadOnlySpan<int> transposedDctY,
            Span<short> jpegPos)
        {
            // transposed_dct_y contains the y channel block, transposed.
            sbyte cmapFactor = c == 0
                ? rowCtx.RowCmapX.Span[(int)absTx]
                : rowCtx.RowCmapB.Span[(int)absTx];
            int scale        = ColorCorrelation.RatioJPEG(cmapFactor);
            int round        = 1 << (ChromaFromLumaConstants.kCFLFixedPointPrecision - 1);
            int shift        = ChromaFromLumaConstants.kCFLFixedPointPrecision;
            int qOffset      = c * 64;

            for (int i = 0; i < 64; i++)
            {
                int inVal      = transposedDct[i];
                int inY        = transposedDctY[i];
                int qt         = state.ScaledQtable[qOffset + i];
                int coeffScale = ((qt * scale) + round) >> shift;
                int cflFactor  = ((inY * coeffScale) + round) >> shift;
                jpegPos[i]     = (short)(inVal + cflFactor);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DequantAndTransformToPixels(
            ref GroupImplState state,
            in RowLoopContext rowCtx,
            (nuint Bx, nuint Size) blkCoords,
            AcStrategy acs,
            (float XCcMul, float BCcMul) ccMul,
            ReadOnlySpan<nuint> sbx,
            ReadOnlySpan<ACPtr> qblock)
        {
            Span<float> block    = state.GroupDecCache.DecGroupBlock.AsSpan();
            Span<float> scratch  = state.GroupDecCache.ScratchSpace.AsSpan();
            nuint bx             = blkCoords.Bx;
            nuint size           = blkCoords.Size;

            DequantBlockArgs dqArgs = new DequantBlockArgs
            {
                InvGlobalScale = state.InvGlobalScale,
                Quant          = rowCtx.RowQuant.Span[(int)bx],
                XDmMultiplier  = state.DecState.XDmMultiplier,
                BDmMultiplier  = state.DecState.BDmMultiplier,
                XCcMul         = ccMul.XCcMul,
                BCcMul         = ccMul.BCcMul,
                Kind           = acs.Strategy(),
                Size           = size,
                Quantizer      = state.DecState.Shared.Quantizer,
                CoveredBlocks  = acs.CoveredBlocksY() * acs.CoveredBlocksX(),
                Sbx            = sbx,
                DcRows         = rowCtx.DcRows,
                DcStride       = state.DcStride,
                Biases         = state.DecState.OutputEncodingInfo.OpsinParams.QuantBiases,
                Qblock         = qblock,
                Block          = block,
                Scratch        = scratch,
                AcType         = state.AcType,
            };

            DequantBlock(in dqArgs);

            foreach (int c in ChannelOrder)
            {
                if ((sbx[c] << (int)state.HShift[c]) != bx ||
                    (rowCtx.Sby[c] << (int)state.VShift[c]) != rowCtx.By)
                {
                    continue;
                }

                // IDCT
                Span<float> idctPos = rowCtx.IdctRow[c].Span.Slice((int)(sbx[c] * FrameDimensions.kBlockDim));
                DecTransforms.TransformToPixels(
                    acs.Strategy(),
                    block.Slice((int)((nuint)c * size), (int)size),
                    idctPos,
                    state.IdctStride[c],
                    scratch);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DequantBlock(in DequantBlockArgs args)
        {
            float scaledDequantS = args.InvGlobalScale / args.Quant;
            float scaledDequantX = scaledDequantS * args.XDmMultiplier;
            float scaledDequantY = scaledDequantS;
            float scaledDequantB = scaledDequantS * args.BDmMultiplier;

            ReadOnlySpan<float> dequantMatrices = args.Quantizer.DequantMatrix(args.Kind, 0);
            int totalCoeffs                     = (int)(args.CoveredBlocks * FrameDimensions.kDCTBlockSize);
            int size                            = (int)args.Size;
            Span<float> block                   = args.Block;
            ReadOnlySpan<float> biases          = args.Biases;
            float xCcMul                        = args.XCcMul;
            float bCcMul                        = args.BCcMul;
            bool is16                           = args.AcType == ACType.k16;
            ReadOnlySpan<short> q0_16           = is16 ? args.Qblock[0].Span16 : ReadOnlySpan<short>.Empty;
            ReadOnlySpan<short> q1_16           = is16 ? args.Qblock[1].Span16 : ReadOnlySpan<short>.Empty;
            ReadOnlySpan<short> q2_16           = is16 ? args.Qblock[2].Span16 : ReadOnlySpan<short>.Empty;
            ReadOnlySpan<int> q0_32             = is16 ? ReadOnlySpan<int>.Empty : args.Qblock[0].Span32;
            ReadOnlySpan<int> q1_32             = is16 ? ReadOnlySpan<int>.Empty : args.Qblock[1].Span32;
            ReadOnlySpan<int> q2_32             = is16 ? ReadOnlySpan<int>.Empty : args.Qblock[2].Span32;

            for (int k = 0; k < totalCoeffs; k++)
            {
                int qx = is16 ? q0_16[k] : q0_32[k];
                int qy = is16 ? q1_16[k] : q1_32[k];
                int qb = is16 ? q2_16[k] : q2_32[k];

                if ((qx | qy | qb) == 0)
                {
                    block[k]              = 0.0f;
                    block[size + k]       = 0.0f;
                    block[(2 * size) + k] = 0.0f;
                    continue;
                }

                float xMul = dequantMatrices[k] * scaledDequantX;
                float yMul = dequantMatrices[size + k] * scaledDequantY;
                float bMul = dequantMatrices[(2 * size) + k] * scaledDequantB;

                float dequantXCc = AdjustQuantBias(0, qx, biases) * xMul;
                float dequantY   = AdjustQuantBias(1, qy, biases) * yMul;
                float dequantBCc = AdjustQuantBias(2, qb, biases) * bMul;

                block[k]              = MathF.FusedMultiplyAdd(xCcMul, dequantY, dequantXCc);
                block[size + k]       = dequantY;
                block[(2 * size) + k] = MathF.FusedMultiplyAdd(bCcMul, dequantY, dequantBCc);
            }

            for (int c = 0; c < 3; c++)
            {
                DecTransforms.LowestFrequenciesFromDC(
                    args.Kind,
                    args.DcRows[c].Span.Slice((int)args.Sbx[c]),
                    args.DcStride,
                    block.Slice(c * size, size),
                    args.Scratch);
            }
        }
    }
}
