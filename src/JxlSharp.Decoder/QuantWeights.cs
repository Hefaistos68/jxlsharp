// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Quantization table kinds corresponding to AC transform strategies.
    /// Ported from <c>jxl::QuantTable</c> in <c>lib/jxl/quant_weights.h</c>.
    /// </summary>
    public enum QuantTable : uint
    {
        /// <summary>8x8 DCT quantization table.</summary>
        DCT = 0,

        /// <summary>Identity transform quantization table.</summary>
        IDENTITY,

        /// <summary>2x2 DCT quantization table.</summary>
        DCT2X2,

        /// <summary>4x4 DCT quantization table.</summary>
        DCT4X4,

        /// <summary>16x16 DCT quantization table.</summary>
        DCT16X16,

        /// <summary>32x32 DCT quantization table.</summary>
        DCT32X32,

        // DCT16X8

        /// <summary>8x16 and 16x8 DCT quantization table.</summary>
        DCT8X16,

        // DCT32X8

        /// <summary>8x32 and 32x8 DCT quantization table.</summary>
        DCT8X32,

        // DCT32X16

        /// <summary>16x32 and 32x16 DCT quantization table.</summary>
        DCT16X32,

        /// <summary>4x8 and 8x4 DCT quantization table.</summary>
        DCT4X8,

        // DCT8X4

        /// <summary>AFV0..AFV3 corner-DCT quantization table.</summary>
        AFV0,

        // AFV1
        // AFV2
        // AFV3

        /// <summary>64x64 DCT quantization table.</summary>
        DCT64X64,

        // DCT64X32,

        /// <summary>32x64 and 64x32 DCT quantization table.</summary>
        DCT32X64,

        /// <summary>128x128 DCT quantization table.</summary>
        DCT128X128,

        // DCT128X64,

        /// <summary>64x128 and 128x64 DCT quantization table.</summary>
        DCT64X128,

        /// <summary>256x256 DCT quantization table.</summary>
        DCT256X256,

        // DCT256X128,

        /// <summary>128x256 and 256x128 DCT quantization table.</summary>
        DCT128X256,
    }

    /// <summary>
    /// Distance-band parameters for radially interpolated DCT quantization weight tables.
    /// Ported from <c>jxl::DctQuantWeightParams</c> in <c>lib/jxl/quant_weights.h</c>.
    /// </summary>
    public sealed class DctQuantWeightParams
    {
        /// <summary>Log2 of the maximum number of distance bands minus 1 (4).</summary>
        public const int kLog2MaxDistanceBands = 4;

        /// <summary>Maximum number of distance bands per channel (17).</summary>
        public const int kMaxDistanceBands = 1 + (1 << kLog2MaxDistanceBands);

        /// <summary>Number of active distance bands.</summary>
        public nuint NumDistanceBands;

        /// <summary>Distance bands for each of the 3 XYB channels, each of length <see cref="kMaxDistanceBands"/>.</summary>
        public readonly float[][] DistanceBands =
        {
            new float[kMaxDistanceBands],
            new float[kMaxDistanceBands],
            new float[kMaxDistanceBands],
        };

        /// <summary>
        /// Initializes a new empty instance of the <see cref="DctQuantWeightParams"/> class.
        /// </summary>
        public DctQuantWeightParams()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DctQuantWeightParams"/> class with the given per-channel bands.
        /// </summary>
        /// <param name="c0">Channel 0 (X) distance bands.</param>
        /// <param name="c1">Channel 1 (Y) distance bands.</param>
        /// <param name="c2">Channel 2 (B) distance bands.</param>
        /// <param name="numDistBands">Number of active distance bands.</param>
        public DctQuantWeightParams(
            ReadOnlySpan<float> c0,
            ReadOnlySpan<float> c1,
            ReadOnlySpan<float> c2,
            nuint numDistBands)
        {
            NumDistanceBands = numDistBands;
            c0.Slice(0, (int)numDistBands).CopyTo(DistanceBands[0]);
            c1.Slice(0, (int)numDistBands).CopyTo(DistanceBands[1]);
            c2.Slice(0, (int)numDistBands).CopyTo(DistanceBands[2]);
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="DctQuantWeightParams"/> instance.
        /// </summary>
        /// <returns>Cloned <see cref="DctQuantWeightParams"/>.</returns>
        public DctQuantWeightParams Clone()
        {
            DctQuantWeightParams copy = new DctQuantWeightParams
            {
                NumDistanceBands = NumDistanceBands,
            };

            for (int c = 0; c < 3; c++)
            {
                Array.Copy(DistanceBands[c], copy.DistanceBands[c], kMaxDistanceBands);
            }

            return copy;
        }
    }

    /// <summary>
    /// Describes the parametric or raw encoding of a single quantization weight table.
    /// Ported from <c>jxl::QuantEncodingInternal</c> and <c>jxl::QuantEncoding</c> in <c>lib/jxl/quant_weights.h</c>.
    /// </summary>
    public sealed class QuantEncoding
    {
        /// <summary>
        /// Quantization table encoding mode.
        /// </summary>
        public enum QuantMode
        {
            /// <summary>Predefined library table.</summary>
            kQuantModeLibrary = 0,

            /// <summary>Identity transform weights.</summary>
            kQuantModeID = 1,

            /// <summary>DCT2x2 transform weights.</summary>
            kQuantModeDCT2 = 2,

            /// <summary>DCT4x4 transform weights.</summary>
            kQuantModeDCT4 = 3,

            /// <summary>DCT4x8 transform weights.</summary>
            kQuantModeDCT4X8 = 4,

            /// <summary>AFV corner-DCT transform weights.</summary>
            kQuantModeAFV = 5,

            /// <summary>General radially interpolated DCT transform weights.</summary>
            kQuantModeDCT = 6,

            /// <summary>Explicit raw integer quantization table.</summary>
            kQuantModeRAW = 7,
        }

        /// <summary>Encoding mode for this quantization table.</summary>
        public QuantMode Mode;

        /// <summary>Weights for DCT4+ tables.</summary>
        public DctQuantWeightParams DctParams = new DctQuantWeightParams();

        /// <summary>Weights for 4x4 sub-block in AFV.</summary>
        public DctQuantWeightParams DctParamsAfv4x4 = new DctQuantWeightParams();

        /// <summary>Weights for identity (3 channels x 3 weights).</summary>
        public readonly float[][] IdWeights =
        {
            new float[3],
            new float[3],
            new float[3],
        };

        /// <summary>Weights for DCT2 (3 channels x 6 weights).</summary>
        public readonly float[][] Dct2Weights =
        {
            new float[6],
            new float[6],
            new float[6],
        };

        /// <summary>Extra multipliers for coefficients 01/10 and 11 for DCT4 (3 channels x 2 multipliers).</summary>
        public readonly float[][] Dct4Multipliers =
        {
            new float[2],
            new float[2],
            new float[2],
        };

        // Weights for AFV. {0, 1} are used directly for coefficients (0, 1) and (1,
        // 0); {2, 3, 4} are used directly corner DC, (1,0) - (0,1) and (0, 1) +
        // (1, 0) - (0, 0) inside the AFV block. Values from 5 to 8 are interpolated
        // as in GetQuantWeights for DC and are used for other coefficients.

        /// <summary>Weights for AFV (3 channels x 9 weights).</summary>
        public readonly float[][] AfvWeights =
        {
            new float[9],
            new float[9],
            new float[9],
        };

        /// <summary>Extra multipliers for coefficients 01 or 10 for DCT4X8 and DCT8X4 (3 channels).</summary>
        public readonly float[] Dct4x8Multipliers = new float[3];

        /// <summary>Explicit quantization table (like in JPEG), used only in <see cref="QuantMode.kQuantModeRAW"/> mode.</summary>
        public int[]? QrawTable;

        /// <summary>Denominator scale for <see cref="QrawTable"/>.</summary>
        public float QrawTableDen = 1.0f / (8.0f * 255.0f);

        /// <summary>Which predefined table to use when <see cref="Mode"/> is <see cref="QuantMode.kQuantModeLibrary"/>.</summary>
        public byte Predefined;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuantEncoding"/> class with the specified <paramref name="mode"/>.
        /// </summary>
        /// <param name="mode">Quantization encoding mode.</param>
        public QuantEncoding(QuantMode mode = QuantMode.kQuantModeLibrary)
        {
            Mode = mode;
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="QuantEncoding"/>.
        /// </summary>
        /// <returns>Cloned <see cref="QuantEncoding"/>.</returns>
        public QuantEncoding Clone()
        {
            QuantEncoding copy = new QuantEncoding(Mode)
            {
                DctParams       = DctParams.Clone(),
                DctParamsAfv4x4 = DctParamsAfv4x4.Clone(),
                QrawTableDen    = QrawTableDen,
                Predefined      = Predefined,
            };

            for (int c = 0; c < 3; c++)
            {
                Array.Copy(IdWeights[c], copy.IdWeights[c], 3);
                Array.Copy(Dct2Weights[c], copy.Dct2Weights[c], 6);
                Array.Copy(Dct4Multipliers[c], copy.Dct4Multipliers[c], 2);
                Array.Copy(AfvWeights[c], copy.AfvWeights[c], 9);
                copy.Dct4x8Multipliers[c] = Dct4x8Multipliers[c];
            }

            if (Mode == QuantMode.kQuantModeRAW && QrawTable is not null)
            {
                copy.QrawTable = (int[])QrawTable.Clone();
            }

            return copy;
        }

        /// <summary>
        /// Creates a library <see cref="QuantEncoding"/> for predefined table index <paramref name="predefined"/>.
        /// </summary>
        /// <param name="predefined">Predefined library index.</param>
        /// <returns>A library <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding Library(byte predefined = 0)
        {
            Debug.Assert(predefined < DequantMatrices.kNumPredefinedTables);

            return new QuantEncoding(QuantMode.kQuantModeLibrary) { Predefined = predefined };
        }

        /// <summary>
        /// Creates an Identity <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="c0">Channel 0 (X) weights (length 3).</param>
        /// <param name="c1">Channel 1 (Y) weights (length 3).</param>
        /// <param name="c2">Channel 2 (B) weights (length 3).</param>
        /// <returns>An Identity <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding Identity(
            ReadOnlySpan<float> c0,
            ReadOnlySpan<float> c1,
            ReadOnlySpan<float> c2)
        {
            QuantEncoding enc = new QuantEncoding(QuantMode.kQuantModeID);
            c0.Slice(0, 3).CopyTo(enc.IdWeights[0]);
            c1.Slice(0, 3).CopyTo(enc.IdWeights[1]);
            c2.Slice(0, 3).CopyTo(enc.IdWeights[2]);

            return enc;
        }

        /// <summary>
        /// Creates a DCT2 <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="c0">Channel 0 (X) weights (length 6).</param>
        /// <param name="c1">Channel 1 (Y) weights (length 6).</param>
        /// <param name="c2">Channel 2 (B) weights (length 6).</param>
        /// <returns>A DCT2 <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding DCT2(
            ReadOnlySpan<float> c0,
            ReadOnlySpan<float> c1,
            ReadOnlySpan<float> c2)
        {
            QuantEncoding enc = new QuantEncoding(QuantMode.kQuantModeDCT2);
            c0.Slice(0, 6).CopyTo(enc.Dct2Weights[0]);
            c1.Slice(0, 6).CopyTo(enc.Dct2Weights[1]);
            c2.Slice(0, 6).CopyTo(enc.Dct2Weights[2]);

            return enc;
        }

        /// <summary>
        /// Creates a DCT4 <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="params4x4">Distance band parameters.</param>
        /// <param name="m0">Channel 0 multipliers (length 2).</param>
        /// <param name="m1">Channel 1 multipliers (length 2).</param>
        /// <param name="m2">Channel 2 multipliers (length 2).</param>
        /// <returns>A DCT4 <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding DCT4(
            DctQuantWeightParams params4x4,
            ReadOnlySpan<float> m0,
            ReadOnlySpan<float> m1,
            ReadOnlySpan<float> m2)
        {
            QuantEncoding enc = new QuantEncoding(QuantMode.kQuantModeDCT4)
            {
                DctParams = params4x4.Clone(),
            };
            m0.Slice(0, 2).CopyTo(enc.Dct4Multipliers[0]);
            m1.Slice(0, 2).CopyTo(enc.Dct4Multipliers[1]);
            m2.Slice(0, 2).CopyTo(enc.Dct4Multipliers[2]);

            return enc;
        }

        /// <summary>
        /// Creates a DCT4X8 <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="params4x8">Distance band parameters.</param>
        /// <param name="xybMul">Multipliers per channel (length 3).</param>
        /// <returns>A DCT4X8 <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding DCT4X8(
            DctQuantWeightParams params4x8,
            ReadOnlySpan<float> xybMul)
        {
            QuantEncoding enc = new QuantEncoding(QuantMode.kQuantModeDCT4X8)
            {
                DctParams = params4x8.Clone(),
            };
            xybMul.Slice(0, 3).CopyTo(enc.Dct4x8Multipliers);

            return enc;
        }

        /// <summary>
        /// Creates a general DCT <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="dctParams">Distance band parameters.</param>
        /// <returns>A DCT <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding DCT(DctQuantWeightParams dctParams)
        {
            return new QuantEncoding(QuantMode.kQuantModeDCT)
            {
                DctParams = dctParams.Clone(),
            };
        }

        /// <summary>
        /// Creates an AFV <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="params4x8">4x8 sub-block distance band parameters.</param>
        /// <param name="params4x4">4x4 sub-block distance band parameters.</param>
        /// <param name="w0">Channel 0 AFV weights (length 9).</param>
        /// <param name="w1">Channel 1 AFV weights (length 9).</param>
        /// <param name="w2">Channel 2 AFV weights (length 9).</param>
        /// <returns>An AFV <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding AFV(
            DctQuantWeightParams params4x8,
            DctQuantWeightParams params4x4,
            ReadOnlySpan<float> w0,
            ReadOnlySpan<float> w1,
            ReadOnlySpan<float> w2)
        {
            QuantEncoding enc = new QuantEncoding(QuantMode.kQuantModeAFV)
            {
                DctParams       = params4x8.Clone(),
                DctParamsAfv4x4 = params4x4.Clone(),
            };
            w0.Slice(0, 9).CopyTo(enc.AfvWeights[0]);
            w1.Slice(0, 9).CopyTo(enc.AfvWeights[1]);
            w2.Slice(0, 9).CopyTo(enc.AfvWeights[2]);

            return enc;
        }

        /// <summary>
        /// Creates a RAW <see cref="QuantEncoding"/>.
        /// </summary>
        /// <param name="qtable">Explicit integer quantization weights.</param>
        /// <param name="shift">Bit shift applied to denominator.</param>
        /// <returns>A RAW <see cref="QuantEncoding"/>.</returns>
        public static QuantEncoding RAW(int[] qtable, int shift = 0)
        {
            return new QuantEncoding(QuantMode.kQuantModeRAW)
            {
                QrawTable    = (int[])qtable.Clone(),
                QrawTableDen = (1 << shift) * (1.0f / (8.0f * 255.0f)),
            };
        }
    }

    /// <summary>
    /// Stores and computes forward and inverse dequantization matrices for all AC transform strategies.
    /// Ported from <c>jxl::DequantMatrices</c> in <c>lib/jxl/quant_weights.h</c> and <c>lib/jxl/quant_weights.cc</c>.
    /// </summary>
    public sealed unsafe class DequantMatrices : IDisposable
    {
        /// <summary>Maximum number of floats in a single quantization table (65536).</summary>
        public const nuint kMaxQuantTableSize = AcStrategy.kMaxCoeffArea;

        /// <summary>Number of predefined library table sets (1).</summary>
        public const nuint kNumPredefinedTables = 1;

        /// <summary>CeilLog2 of <see cref="kNumPredefinedTables"/> (0).</summary>
        public const int kCeilLog2NumPredefinedTables = 0;

        /// <summary>Number of bits used to encode <see cref="QuantEncoding.QuantMode"/> (3).</summary>
        public const int kLog2NumQuantModes = 3;

        /// <summary>Total number of distinct <see cref="QuantTable"/> kinds (17).</summary>
        public const byte kNumQuantTables = (byte)QuantTable.DCT128X256 + 1;

        // MUST be equal `sum(dot(required_size_x, required_size_y))`.

        /// <summary>Sum of block areas across all 17 quantization tables (2056).</summary>
        public const nuint kSumRequiredXy = 2056;

        private const nuint kTotalTableSize = kSumRequiredXy * FrameDimensions.kDCTBlockSize * 3;
        private const float kAlmostZero     = 1e-8f;

        // Let's try to keep these 2**N for possible future simplicity.

        /// <summary>Default inverse DC quantization factors for X, Y, B.</summary>
        public static ReadOnlySpan<float> InvDcQuantDefault => new float[]
        {
            4096.0f,
            512.0f,
            256.0f,
        };

        /// <summary>Default DC quantization factors for X, Y, B.</summary>
        public static ReadOnlySpan<float> DcQuantDefault => new float[]
        {
            1.0f / 4096.0f,
            1.0f / 512.0f,
            1.0f / 256.0f,
        };

        /// <summary>Required block width for each of the 17 <see cref="QuantTable"/> entries.</summary>
        public static ReadOnlySpan<int> RequiredSizeX => new int[]
        {
            1, 1, 1, 1, 2, 4, 1, 1, 2, 1, 1, 8, 4, 16, 8, 32, 16,
        };

        /// <summary>Required block height for each of the 17 <see cref="QuantTable"/> entries.</summary>
        public static ReadOnlySpan<int> RequiredSizeY => new int[]
        {
            1, 1, 1, 1, 2, 4, 2, 4, 4, 1, 1, 8, 8, 16, 16, 32, 32,
        };

        /// <summary>Mapping from <see cref="AcStrategyType"/> to <see cref="QuantTable"/>.</summary>
        public static ReadOnlySpan<QuantTable> AcStrategyToQuantTableMap => new QuantTable[]
        {
            QuantTable.DCT,        QuantTable.IDENTITY,   QuantTable.DCT2X2,
            QuantTable.DCT4X4,     QuantTable.DCT16X16,   QuantTable.DCT32X32,
            QuantTable.DCT8X16,    QuantTable.DCT8X16,    QuantTable.DCT8X32,
            QuantTable.DCT8X32,    QuantTable.DCT16X32,   QuantTable.DCT16X32,
            QuantTable.DCT4X8,     QuantTable.DCT4X8,     QuantTable.AFV0,
            QuantTable.AFV0,       QuantTable.AFV0,       QuantTable.AFV0,
            QuantTable.DCT64X64,   QuantTable.DCT32X64,   QuantTable.DCT32X64,
            QuantTable.DCT128X128, QuantTable.DCT64X128,  QuantTable.DCT64X128,
            QuantTable.DCT256X256, QuantTable.DCT128X256, QuantTable.DCT128X256,
        };

        private static readonly float[] AfvFreqs =
        {
            0xBAD,
            0xBAD,
            0.8517778890324296f,
            5.37778436506804f,
            0xBAD,
            0xBAD,
            4.734747904497923f,
            5.449245381693219f,
            1.6598270267479331f,
            4.0f,
            7.275749096817861f,
            10.423227632456525f,
            2.662932286148962f,
            7.630657783650829f,
            8.962388608184032f,
            12.97166202570235f,
        };

        private static readonly QuantEncoding[] LibraryTables = LibraryInit();

        private uint _computedMask;
        private AlignedMemory? _tableStorage;
        private float* _table;
        private float* _invTable;
        private readonly float[] _dcQuant      = { DcQuantDefault[0], DcQuantDefault[1], DcQuantDefault[2] };
        private readonly float[] _invDcQuant   = { InvDcQuantDefault[0], InvDcQuantDefault[1], InvDcQuantDefault[2] };
        private readonly nuint[] _tableOffsets = new nuint[AcStrategy.kNumValidStrategies * 3];
        private readonly List<QuantEncoding> _encodings = new List<QuantEncoding>(kNumQuantTables);

        /// <summary>
        /// Initializes a new instance of the <see cref="DequantMatrices"/> class with default library encodings.
        /// </summary>
        public DequantMatrices()
        {
            for (int i = 0; i < kNumQuantTables; i++)
            {
                _encodings.Add(QuantEncoding.Library(0));
            }

            nuint pos             = 0;
            Span<nuint> offsets   = stackalloc nuint[kNumQuantTables * 3];
            ReadOnlySpan<int> rx  = RequiredSizeX;
            ReadOnlySpan<int> ry  = RequiredSizeY;

            for (int i = 0; i < kNumQuantTables; i++)
            {
                nuint numBlocks = (nuint)(rx[i] * ry[i]);
                nuint num       = numBlocks * FrameDimensions.kDCTBlockSize;

                for (nuint c = 0; c < 3; c++)
                {
                    offsets[3 * i + (int)c] = pos + c * num;
                }

                pos += 3 * num;
            }

            ReadOnlySpan<QuantTable> map = AcStrategyToQuantTableMap;

            for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
            {
                int qIdx = (int)map[i];

                for (int c = 0; c < 3; c++)
                {
                    _tableOffsets[i * 3 + c] = offsets[qIdx * 3 + c];
                }
            }
        }

        /// <summary>
        /// Returns the predefined library quantization encodings.
        /// </summary>
        /// <returns>Read-only list of library <see cref="QuantEncoding"/> definitions.</returns>
        public static IReadOnlyList<QuantEncoding> Library() => LibraryTables;

        /// <summary>
        /// Gets the current quantization table encodings.
        /// </summary>
        public IReadOnlyList<QuantEncoding> Encodings => _encodings;

        /// <summary>
        /// Returns a pointer to the dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Aligned pointer to the dequantization matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float* Matrix(AcStrategyType quantKind, nuint c)
        {
            Debug.Assert((_computedMask & (1u << (int)quantKind)) != 0);

            return _table + _tableOffsets[(int)quantKind * 3 + (int)c];
        }

        /// <summary>
        /// Returns a read-only span over the dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <param name="length">Number of floats in the matrix.</param>
        /// <returns>Read-only span of dequantization weights.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> MatrixSpan(AcStrategyType quantKind, nuint c, int length)
        {
            return new ReadOnlySpan<float>(Matrix(quantKind, c), length);
        }

        /// <summary>
        /// Returns a pointer to the inverse dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Aligned pointer to the inverse dequantization matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float* InvMatrix(AcStrategyType quantKind, nuint c)
        {
            Debug.Assert((_computedMask & (1u << (int)quantKind)) != 0);

            return _invTable + _tableOffsets[(int)quantKind * 3 + (int)c];
        }

        /// <summary>
        /// Returns a read-only span over the inverse dequantization matrix for <paramref name="quantKind"/> and channel <paramref name="c"/>.
        /// </summary>
        /// <param name="quantKind">AC strategy type.</param>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <param name="length">Number of floats in the matrix.</param>
        /// <returns>Read-only span of inverse dequantization weights.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> InvMatrixSpan(AcStrategyType quantKind, nuint c, int length)
        {
            return new ReadOnlySpan<float>(InvMatrix(quantKind, c), length);
        }

        // DC quants are used in modular mode for XYB multipliers.

        /// <summary>
        /// Gets the DC quantization factor for channel <paramref name="c"/>.
        /// </summary>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>DC quantization factor.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DCQuant(nuint c) => _dcQuant[(int)c];

        /// <summary>
        /// Gets a read-only span of the 3 DC quantization factors.
        /// </summary>
        /// <returns>Read-only span of DC quantization factors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> DCQuants() => _dcQuant;

        /// <summary>
        /// Gets the inverse DC quantization factor for channel <paramref name="c"/>.
        /// </summary>
        /// <param name="c">Channel index in <c>[0..2]</c>.</param>
        /// <returns>Inverse DC quantization factor.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float InvDCQuant(nuint c) => _invDcQuant[(int)c];

        /// <summary>
        /// Replaces the quantization encodings (used by the encoder and tests).
        /// </summary>
        /// <param name="encodings">List of 17 quantization encodings.</param>
        public void SetEncodings(IReadOnlyList<QuantEncoding> encodings)
        {
            _encodings.Clear();

            for (int i = 0; i < encodings.Count; i++)
            {
                _encodings.Add(encodings[i].Clone());
            }

            _computedMask = 0;
        }

        /// <summary>
        /// Sets the inverse DC quantization factors and derives the forward DC quantization factors.
        /// </summary>
        /// <param name="dc">Span of 3 inverse DC quantization values.</param>
        public void SetDCQuant(ReadOnlySpan<float> dc)
        {
            for (int c = 0; c < 3; c++)
            {
                _dcQuant[c]    = 1.0f / dc[c];
                _invDcQuant[c] = dc[c];
            }
        }

        /// <summary>
        /// Decodes the quantization table encodings from <paramref name="br"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary modular image allocations.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="globalModular">Optional global modular context when decoding within a frame.</param>
        /// <param name="frameDim">Optional frame dimensions when <paramref name="globalModular"/> is provided.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Decode(
            JxlMemoryManager? memoryManager,
            BitReader br,
            GlobalModularContext? globalModular = null,
            FrameDimensions? frameDim = null)
        {
            ulong allDefault = br.ReadBits(1);
            int numTables    = allDefault != 0 ? 0 : kNumQuantTables;
            _encodings.Clear();

            for (int i = 0; i < kNumQuantTables; i++)
            {
                _encodings.Add(QuantEncoding.Library(0));
            }

            ReadOnlySpan<int> rx = RequiredSizeX;
            ReadOnlySpan<int> ry = RequiredSizeY;

            for (int i = 0; i < numTables; i++)
            {
                nuint reqX = (nuint)rx[i % kNumQuantTables];
                nuint reqY = (nuint)ry[i % kNumQuantTables];

                if (!DecodeSingleEncoding(memoryManager, br, _encodings[i], reqX, reqY, (nuint)i, (globalModular, frameDim)))
                {
                    return false;
                }
            }

            _computedMask = 0;

            return true;
        }

        /// <summary>
        /// Decodes the DC quantization factors from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeDC(BitReader br)
        {
            bool allDefault = br.ReadBits(1) != 0;

            if (!br.AllReadsWithinBounds())
            {
                return Status.Failure("EOS during DecodeDC");
            }

            if (!allDefault)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (!F16Coder.Read(br, out _dcQuant[c]))
                    {
                        return false;
                    }

                    _dcQuant[c] *= 1.0f / 128.0f;

                    // Negative values and nearly zero are invalid values.
                    if (_dcQuant[c] < kAlmostZero)
                    {
                        return Status.Failure("Invalid dc_quant: coefficient is too small.");
                    }

                    _invDcQuant[c] = 1.0f / _dcQuant[c];
                }
            }

            return true;
        }

        /// <summary>
        /// Ensures that dequantization and inverse dequantization matrices are computed for all strategies in <paramref name="acsMask"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for aligned matrix storage.</param>
        /// <param name="acsMask">Bitmask of <see cref="AcStrategyType"/> values to compute.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status EnsureComputed(JxlMemoryManager? memoryManager, uint acsMask)
        {
            if (!EnsureStorageAllocated(memoryManager))
            {
                return false;
            }

            Span<nuint> offsets = stackalloc nuint[kNumQuantTables * 3 + 1];
            ComputeTableOffsets(offsets);

            uint kindMask         = MapAcsMaskToKindMask(acsMask);
            uint computedKindMask = MapAcsMaskToKindMask(_computedMask);

            for (int table = 0; table < kNumQuantTables; table++)
            {
                uint bit = 1u << table;

                if ((bit & computedKindMask) != 0 || (bit & ~kindMask) != 0)
                {
                    continue;
                }

                nuint offset      = offsets[table * 3];
                QuantEncoding enc = _encodings[table].Mode == QuantEncoding.QuantMode.kQuantModeLibrary
                    ? LibraryTables[table]
                    : _encodings[table];

                if (!ComputeQuantTable(enc, _table, _invTable, (QuantTable)table, ref offset))
                {
                    return false;
                }

                if (offset != offsets[table * 3 + 3])
                {
                    return Status.Failure("Quant table offset mismatch");
                }
            }

            _computedMask |= acsMask;

            return true;
        }

        /// <summary>
        /// Decodes a RAW quantization table using the Modular sub-bitstream decoder.
        /// Ported from <c>ModularFrameDecoder::DecodeQuantTable</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for temporary image allocation.</param>
        /// <param name="requiredSizeX">Required width in coefficients.</param>
        /// <param name="requiredSizeY">Required height in coefficients.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="encoding">Target <see cref="QuantEncoding"/> to populate.</param>
        /// <param name="idx">Quantization table index.</param>
        /// <param name="globalModular">Optional global modular context paired with frame dimensions.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeQuantTable(
            JxlMemoryManager? memoryManager,
            nuint requiredSizeX,
            nuint requiredSizeY,
            BitReader br,
            QuantEncoding encoding,
            nuint idx,
            (GlobalModularContext? Context, FrameDimensions? FrameDim) globalModular = default)
        {
            if (!F16Coder.Read(br, out encoding.QrawTableDen))
            {
                return false;
            }

            if (encoding.QrawTableDen < kAlmostZero)
            {
                // qtable[] values are already checked for <= 0 so the denominator may not
                // be negative.
                return Status.Failure("Invalid qtable_den: value too small");
            }

            StatusOr<ModularImage> imgResult = ModularImage.Create(
                memoryManager,
                requiredSizeX,
                requiredSizeY,
                bitDepth: 8,
                nbChans: 3);

            if (!imgResult.IsOk)
            {
                return imgResult.Status;
            }

            using ModularImage image = imgResult.Value;

            if (!DecompressRawQuantImage(br, image, idx, globalModular))
            {
                return false;
            }

            return ExtractRawQuantTableFromImage(image, requiredSizeX, requiredSizeY, encoding);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _tableStorage?.Dispose();
            _tableStorage = null;
            _table        = null;
            _invTable     = null;
        }

        private static Status DecompressRawQuantImage(
            BitReader br,
            ModularImage image,
            nuint idx,
            (GlobalModularContext? Context, FrameDimensions? FrameDim) globalModular)
        {
            ModularOptions options = new ModularOptions();

            if (globalModular.Context is not null && globalModular.FrameDim is not null)
            {
                StatusOr<ModularStreamId> qtResult = ModularStreamId.QuantTable(idx);

                if (!qtResult.IsOk)
                {
                    return qtResult.Status;
                }

                FrameDimensions frameDimVal = globalModular.FrameDim.Value;
                nuint streamId              = qtResult.Value.ID(frameDimVal);

                return ModularEncoding.ModularGenericDecompress(
                    br,
                    image,
                    header: null,
                    streamId,
                    options,
                    globalModular.Context);
            }

            return ModularEncoding.ModularGenericDecompress(
                br,
                image,
                header: null,
                groupId: 0,
                options);
        }

        private static Status ExtractRawQuantTableFromImage(
            ModularImage image,
            nuint requiredSizeX,
            nuint requiredSizeY,
            QuantEncoding encoding)
        {
            int totalLen = (int)(requiredSizeX * requiredSizeY * 3);

            if (encoding.QrawTable is null || encoding.QrawTable.Length != totalLen)
            {
                encoding.QrawTable = new int[totalLen];
            }

            int[] qtable = encoding.QrawTable;
            int planeLen = (int)(requiredSizeX * requiredSizeY);

            for (int c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < requiredSizeY; y++)
                {
                    ReadOnlySpan<int> row = image.Channels[c].ConstRow(y);
                    int rowOffset         = c * planeLen + (int)(y * requiredSizeX);

                    for (int x = 0; x < (int)requiredSizeX; x++)
                    {
                        int v = row[x];

                        if (v <= 0)
                        {
                            return Status.Failure("Invalid raw quantization table");
                        }

                        qtable[rowOffset + x] = v;
                    }
                }
            }

            return true;
        }

        private bool EnsureStorageAllocated(JxlMemoryManager? memoryManager)
        {
            if (_tableStorage is not null)
            {
                return true;
            }

            JxlMemoryManager mm     = memoryManager ?? JxlMemoryManager.Default;
            nuint tableStorageBytes = 2 * kTotalTableSize * sizeof(float);
            StatusOr<AlignedMemory> memResult = AlignedMemory.Create(mm, tableStorageBytes);

            if (!memResult.IsOk)
            {
                return false;
            }

            _tableStorage = memResult.Value;
            _table        = _tableStorage.GetPointer<float>();
            _invTable     = _table + kTotalTableSize;

            return true;
        }

        private static void ComputeTableOffsets(Span<nuint> offsets)
        {
            nuint pos            = 0;
            ReadOnlySpan<int> rx = RequiredSizeX;
            ReadOnlySpan<int> ry = RequiredSizeY;

            for (int i = 0; i < kNumQuantTables; i++)
            {
                nuint numBlocks = (nuint)(rx[i] * ry[i]);
                nuint num       = numBlocks * FrameDimensions.kDCTBlockSize;

                for (nuint c = 0; c < 3; c++)
                {
                    offsets[3 * i + (int)c] = pos + c * num;
                }

                pos += 3 * num;
            }

            offsets[kNumQuantTables * 3] = pos;
            Debug.Assert(pos == kTotalTableSize);
        }

        private static uint MapAcsMaskToKindMask(uint mask)
        {
            uint kindMask                = 0;
            ReadOnlySpan<QuantTable> map = AcStrategyToQuantTableMap;

            for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
            {
                if ((mask & (1u << i)) != 0)
                {
                    kindMask |= 1u << (int)map[i];
                }
            }

            return kindMask;
        }

        private static Status DecodeSingleEncoding(
            JxlMemoryManager? memoryManager,
            BitReader br,
            QuantEncoding encoding,
            nuint requiredSizeX,
            nuint requiredSizeY,
            nuint idx,
            (GlobalModularContext? Context, FrameDimensions? FrameDim) globalModular)
        {
            nuint requiredSize = requiredSizeX * requiredSizeY;
            requiredSizeX     *= FrameDimensions.kBlockDim;
            requiredSizeY     *= FrameDimensions.kBlockDim;

            QuantEncoding.QuantMode mode = (QuantEncoding.QuantMode)br.ReadFixedBits(kLog2NumQuantModes);

            Status status = mode switch
            {
                QuantEncoding.QuantMode.kQuantModeLibrary => DecodeLibraryMode(br, encoding),
                QuantEncoding.QuantMode.kQuantModeID      => DecodeIdMode(br, encoding, requiredSize),
                QuantEncoding.QuantMode.kQuantModeDCT2    => DecodeDct2Mode(br, encoding, requiredSize),
                QuantEncoding.QuantMode.kQuantModeDCT4X8  => DecodeDct4x8Mode(br, encoding, requiredSize),
                QuantEncoding.QuantMode.kQuantModeDCT4    => DecodeDct4Mode(br, encoding, requiredSize),
                QuantEncoding.QuantMode.kQuantModeAFV     => DecodeAfvMode(br, encoding, requiredSize),
                QuantEncoding.QuantMode.kQuantModeDCT     => DecodeDctParams(br, encoding.DctParams),
                QuantEncoding.QuantMode.kQuantModeRAW     => DecodeRawMode(
                    memoryManager, br, encoding, requiredSizeX, requiredSizeY, idx, globalModular),
                _ => Status.Failure("Invalid quantization table encoding"),
            };

            if (!status)
            {
                return status;
            }

            encoding.Mode = mode;

            return true;
        }

        private static Status DecodeLibraryMode(BitReader br, QuantEncoding encoding)
        {
            encoding.Predefined = (byte)br.ReadFixedBits(kCeilLog2NumPredefinedTables);

            if (encoding.Predefined >= kNumPredefinedTables)
            {
                return Status.Failure("Invalid predefined table");
            }

            return true;
        }

        private static Status DecodeIdMode(BitReader br, QuantEncoding encoding, nuint requiredSize)
        {
            if (requiredSize != 1)
            {
                return Status.Failure("Invalid mode");
            }

            return ReadScaledWeightMatrix(br, encoding.IdWeights, 3, 64.0f, "ID Quantizer is too small");
        }

        private static Status DecodeDct2Mode(BitReader br, QuantEncoding encoding, nuint requiredSize)
        {
            if (requiredSize != 1)
            {
                return Status.Failure("Invalid mode");
            }

            return ReadScaledWeightMatrix(br, encoding.Dct2Weights, 6, 64.0f, "Quantizer is too small");
        }

        private static Status DecodeDct4x8Mode(BitReader br, QuantEncoding encoding, nuint requiredSize)
        {
            if (requiredSize != 1)
            {
                return Status.Failure("Invalid mode");
            }

            for (int c = 0; c < 3; c++)
            {
                if (!F16Coder.Read(br, out encoding.Dct4x8Multipliers[c]))
                {
                    return false;
                }

                if (MathF.Abs(encoding.Dct4x8Multipliers[c]) < kAlmostZero)
                {
                    return Status.Failure("DCT4X8 multiplier is too small");
                }
            }

            return DecodeDctParams(br, encoding.DctParams);
        }

        private static Status DecodeDct4Mode(BitReader br, QuantEncoding encoding, nuint requiredSize)
        {
            if (requiredSize != 1)
            {
                return Status.Failure("Invalid mode");
            }

            if (!ReadScaledWeightMatrix(br, encoding.Dct4Multipliers, 2, 1.0f, "DCT4 multiplier is too small"))
            {
                return false;
            }

            return DecodeDctParams(br, encoding.DctParams);
        }

        private static Status DecodeAfvMode(BitReader br, QuantEncoding encoding, nuint requiredSize)
        {
            if (requiredSize != 1)
            {
                return Status.Failure("Invalid mode");
            }

            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (!F16Coder.Read(br, out encoding.AfvWeights[c][i]))
                    {
                        return false;
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    encoding.AfvWeights[c][i] *= 64.0f;
                }
            }

            if (!DecodeDctParams(br, encoding.DctParams))
            {
                return false;
            }

            return DecodeDctParams(br, encoding.DctParamsAfv4x4);
        }

        private static Status DecodeRawMode(
            JxlMemoryManager? memoryManager,
            BitReader br,
            QuantEncoding encoding,
            nuint requiredSizeX,
            nuint requiredSizeY,
            nuint idx,
            (GlobalModularContext? Context, FrameDimensions? FrameDim) globalModular)
        {
            encoding.Mode = QuantEncoding.QuantMode.kQuantModeRAW;

            return DecodeQuantTable(memoryManager, requiredSizeX, requiredSizeY, br, encoding, idx, globalModular);
        }

        private static Status ReadScaledWeightMatrix(
            BitReader br,
            float[][] weights,
            int countPerChannel,
            float scale,
            string errorMessage)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < countPerChannel; i++)
                {
                    if (!F16Coder.Read(br, out weights[c][i]))
                    {
                        return false;
                    }

                    if (MathF.Abs(weights[c][i]) < kAlmostZero)
                    {
                        return Status.Failure(errorMessage);
                    }

                    weights[c][i] *= scale;
                }
            }

            return true;
        }

        private static Status DecodeDctParams(BitReader br, DctQuantWeightParams dctParams)
        {
            dctParams.NumDistanceBands = (nuint)br.ReadFixedBits(DctQuantWeightParams.kLog2MaxDistanceBands) + 1;

            for (int c = 0; c < 3; c++)
            {
                for (nuint i = 0; i < dctParams.NumDistanceBands; i++)
                {
                    if (!F16Coder.Read(br, out dctParams.DistanceBands[c][(int)i]))
                    {
                        return false;
                    }
                }

                if (dctParams.DistanceBands[c][0] < kAlmostZero)
                {
                    return Status.Failure("Distance band seed is too small");
                }

                dctParams.DistanceBands[c][0] *= 64.0f;
            }

            return true;
        }

        // kQuantWeights[N * N * c + N * y + x] is the relative weight of the (x, y)
        // coefficient in component c. Higher weights correspond to finer quantization
        // intervals and more bits spent in encoding.
        private static void GetQuantWeightsDCT2(float[][] dct2weights, Span<float> weights)
        {
            for (int c = 0; c < 3; c++)
            {
                int start          = c * 64;
                weights[start]     = 0xBAD;
                weights[start + 1] = dct2weights[c][0];
                weights[start + 8] = dct2weights[c][0];
                weights[start + 9] = dct2weights[c][1];

                FillDct2Subquadrant(weights, start, 0, 2, 2, dct2weights[c][2], symmetric: true);
                FillDct2Subquadrant(weights, start, 2, 2, 2, dct2weights[c][3], symmetric: false);
                FillDct2Subquadrant(weights, start, 0, 4, 4, dct2weights[c][4], symmetric: true);
                FillDct2Subquadrant(weights, start, 4, 4, 4, dct2weights[c][5], symmetric: false);
            }
        }

        private static void FillDct2Subquadrant(
            Span<float> weights,
            int start,
            int yOffset,
            int xOffset,
            int size,
            float val,
            bool symmetric)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    weights[start + (y + yOffset) * 8 + x + xOffset] = val;

                    if (symmetric)
                    {
                        weights[start + (y + xOffset) * 8 + x + yOffset] = val;
                    }
                }
            }
        }

        private static void GetQuantWeightsIdentity(float[][] idweights, Span<float> weights)
        {
            for (int c = 0; c < 3; c++)
            {
                int baseIdx = 64 * c;
                weights.Slice(baseIdx, 64).Fill(idweights[c][0]);
                weights[baseIdx + 1] = idweights[c][1];
                weights[baseIdx + 8] = idweights[c][1];
                weights[baseIdx + 9] = idweights[c][2];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static StatusOr<float> Interpolate(float pos, float max, ReadOnlySpan<float> array, int len)
        {
            float scaledPos = pos * (len - 1) / max;
            int idx         = (int)scaledPos;

            if (idx < 0 || idx + 1 >= len)
            {
                return Status.Failure("Interpolation index out of bounds");
            }

            float a = array[idx];
            float b = array[idx + 1];

            return a * FastMath.FastPowf(b / a, scaledPos - idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Mult(float v)
        {
            if (v > 0.0f)
            {
                return 1.0f + v;
            }

            return 1.0f / (1.0f - v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<float> InterpolateVec(Vector128<float> scaledPos, ReadOnlySpan<float> array)
        {
            Vector128<int> idx    = Vector128.ConvertToInt32(scaledPos);
            Vector128<float> frac = scaledPos - Vector128.ConvertToSingle(idx);

            int i0 = idx.GetElement(0);
            int i1 = idx.GetElement(1);
            int i2 = idx.GetElement(2);
            int i3 = idx.GetElement(3);

            Vector128<float> a = Vector128.Create(array[i0], array[i1], array[i2], array[i3]);
            Vector128<float> b = Vector128.Create(array[i0 + 1], array[i1 + 1], array[i2 + 1], array[i3 + 1]);

            return a * FastMath.FastPowf(b / a, frac);
        }

        private static Status GetQuantWeights(
            nuint rows,
            nuint cols,
            float[][] distanceBands,
            nuint numBands,
            Span<float> output)
        {
            if (cols < 4 || numBands == 0)
            {
                return Status.Failure("Invalid quant weights dimensions or bands");
            }

            Span<float> bands = stackalloc float[DctQuantWeightParams.kMaxDistanceBands];

            for (int c = 0; c < 3; c++)
            {
                if (!BuildChannelDistanceBands(distanceBands[c], numBands, bands))
                {
                    return Status.Failure("Invalid distance bands");
                }

                PopulateChannelQuantWeights(rows, cols, numBands, bands, output.Slice(c * (int)(cols * rows), (int)(cols * rows)));
            }

            return true;
        }

        private static bool BuildChannelDistanceBands(
            ReadOnlySpan<float> srcBands,
            nuint numBands,
            Span<float> bands)
        {
            bands[0] = srcBands[0];

            if (bands[0] < kAlmostZero)
            {
                return false;
            }

            for (int i = 1; i < (int)numBands; i++)
            {
                bands[i] = bands[i - 1] * Mult(srcBands[i]);

                if (bands[i] < kAlmostZero)
                {
                    return false;
                }
            }

            return true;
        }

        private static void PopulateChannelQuantWeights(
            nuint rows,
            nuint cols,
            nuint numBands,
            ReadOnlySpan<float> bands,
            Span<float> channelOut)
        {
            float scale             = (numBands - 1) / (Common.kSqrt2 + 1e-6f);
            float rcpCol            = scale / (cols - 1);
            float rcpRow            = scale / (rows - 1);
            Vector128<float> l0123  = Vector128.Create(0.0f, 1.0f, 2.0f, 3.0f);
            Vector128<float> vRcpX  = Vector128.Create(rcpCol);
            Vector128<float> vBand0 = Vector128.Create(bands[0]);

            for (uint y = 0; y < (uint)rows; y++)
            {
                float dy               = y * rcpRow;
                Vector128<float> vDy2  = Vector128.Create(dy * dy);
                int rowBase            = (int)(y * cols);

                for (uint x = 0; x < (uint)cols; x += 4)
                {
                    Vector128<float> dx         = (Vector128.Create((float)x) + l0123) * vRcpX;
                    Vector128<float> scaledDist = Vector128.Sqrt((dx * dx) + vDy2);
                    Vector128<float> weight     = numBands == 1
                        ? vBand0
                        : InterpolateVec(scaledDist, bands);

                    weight.CopyTo(channelOut.Slice(rowBase + (int)x, 4));
                }
            }
        }

        private static Status ComputeQuantTable(
            QuantEncoding encoding,
            float* table,
            float* invTable,
            QuantTable kind,
            ref nuint pos)
        {
            int quantTableIdx = (int)kind;
            nuint wrows       = 8u * (nuint)RequiredSizeX[quantTableIdx];
            nuint wcols       = 8u * (nuint)RequiredSizeY[quantTableIdx];
            nuint num         = wrows * wcols;
            float[] weights   = new float[3 * (int)num];

            if (!PopulateEncodingWeights(encoding, wrows, wcols, num, weights))
            {
                return false;
            }

            nuint prevPos = pos;

            if (!StoreAndInvertWeights(weights, table + prevPos, invTable + prevPos))
            {
                return false;
            }

            pos += 3 * num;
            ZeroLowestFrequencyInverseWeights(quantTableIdx, invTable + prevPos);

            return true;
        }

        private static Status PopulateEncodingWeights(
            QuantEncoding encoding,
            nuint wrows,
            nuint wcols,
            nuint num,
            Span<float> weights)
        {
            switch (encoding.Mode)
            {
                case QuantEncoding.QuantMode.kQuantModeLibrary:
                    return Status.Failure("Library mode must be resolved before ComputeQuantTable");

                case QuantEncoding.QuantMode.kQuantModeID:
                    if (num != FrameDimensions.kDCTBlockSize)
                    {
                        return Status.Failure("Invalid block size for ID quant table");
                    }

                    GetQuantWeightsIdentity(encoding.IdWeights, weights);

                    return true;

                case QuantEncoding.QuantMode.kQuantModeDCT2:
                    if (num != FrameDimensions.kDCTBlockSize)
                    {
                        return Status.Failure("Invalid block size for DCT2 quant table");
                    }

                    GetQuantWeightsDCT2(encoding.Dct2Weights, weights);

                    return true;

                case QuantEncoding.QuantMode.kQuantModeDCT4:
                    return ComputeDct4Weights(encoding, num, weights);

                case QuantEncoding.QuantMode.kQuantModeDCT4X8:
                    return ComputeDct4x8Weights(encoding, num, weights);

                case QuantEncoding.QuantMode.kQuantModeDCT:
                    return GetQuantWeights(
                        wrows,
                        wcols,
                        encoding.DctParams.DistanceBands,
                        encoding.DctParams.NumDistanceBands,
                        weights);

                case QuantEncoding.QuantMode.kQuantModeRAW:
                    return ComputeRawWeights(encoding, num, weights);

                case QuantEncoding.QuantMode.kQuantModeAFV:
                    return ComputeAfvWeights(encoding, num, weights);

                default:
                    return Status.Failure("Unknown quant encoding mode");
            }
        }

        private static Status ComputeDct4Weights(QuantEncoding encoding, nuint num, Span<float> weights)
        {
            if (num != FrameDimensions.kDCTBlockSize)
            {
                return Status.Failure("Invalid block size for DCT4 quant table");
            }

            Span<float> weights4x4 = stackalloc float[3 * 4 * 4];

            if (!GetQuantWeights(
                    4,
                    4,
                    encoding.DctParams.DistanceBands,
                    encoding.DctParams.NumDistanceBands,
                    weights4x4))
            {
                return false;
            }

            const int N = (int)FrameDimensions.kBlockDim;

            for (int c = 0; c < 3; c++)
            {
                int cBase = c * (int)num;

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        weights[cBase + y * N + x] = weights4x4[c * 16 + (y / 2) * 4 + (x / 2)];
                    }
                }

                weights[cBase + 1]     /= encoding.Dct4Multipliers[c][0];
                weights[cBase + N]     /= encoding.Dct4Multipliers[c][0];
                weights[cBase + N + 1] /= encoding.Dct4Multipliers[c][1];
            }

            return true;
        }

        private static Status ComputeDct4x8Weights(QuantEncoding encoding, nuint num, Span<float> weights)
        {
            if (num != FrameDimensions.kDCTBlockSize)
            {
                return Status.Failure("Invalid block size for DCT4X8 quant table");
            }

            Span<float> weights4x8 = stackalloc float[3 * 4 * 8];

            if (!GetQuantWeights(
                    4,
                    8,
                    encoding.DctParams.DistanceBands,
                    encoding.DctParams.NumDistanceBands,
                    weights4x8))
            {
                return false;
            }

            const int N = (int)FrameDimensions.kBlockDim;

            for (int c = 0; c < 3; c++)
            {
                int cBase = c * (int)num;

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        weights[cBase + y * N + x] = weights4x8[c * 32 + (y / 2) * 8 + x];
                    }
                }

                weights[cBase + N] /= encoding.Dct4x8Multipliers[c];
            }

            return true;
        }

        private static Status ComputeRawWeights(QuantEncoding encoding, nuint num, Span<float> weights)
        {
            int total = 3 * (int)num;

            if (encoding.QrawTable is null || encoding.QrawTable.Length != total)
            {
                return Status.Failure("Invalid table encoding");
            }

            int[] qtable = encoding.QrawTable;
            float den    = encoding.QrawTableDen;

            for (int i = 0; i < total; i++)
            {
                weights[i] = 1.0f / (den * qtable[i]);
            }

            return true;
        }

        private static Status ComputeAfvWeights(QuantEncoding encoding, nuint num, Span<float> weights)
        {
            Span<float> weights4x8 = stackalloc float[3 * 4 * 8];

            if (!GetQuantWeights(
                    4,
                    8,
                    encoding.DctParams.DistanceBands,
                    encoding.DctParams.NumDistanceBands,
                    weights4x8))
            {
                return false;
            }

            Span<float> weights4x4 = stackalloc float[3 * 4 * 4];

            if (!GetQuantWeights(
                    4,
                    4,
                    encoding.DctParamsAfv4x4.DistanceBands,
                    encoding.DctParamsAfv4x4.NumDistanceBands,
                    weights4x4))
            {
                return false;
            }

            for (int c = 0; c < 3; c++)
            {
                if (!PopulateAfvChannelWeights(
                        c,
                        (int)num,
                        encoding.AfvWeights[c],
                        weights4x8.Slice(c * 32, 32),
                        weights4x4.Slice(c * 16, 16),
                        weights))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status PopulateAfvChannelWeights(
            int c,
            int num,
            ReadOnlySpan<float> afvWeightsC,
            ReadOnlySpan<float> weights4x8C,
            ReadOnlySpan<float> weights4x4C,
            Span<float> weights)
        {
            const float lo = 0.8517778890324296f;
            const float hi = 12.97166202570235f - lo + 1e-6f;

            Span<float> bands = stackalloc float[4];

            if (!BuildChannelDistanceBands(afvWeightsC.Slice(5, 4), 4, bands))
            {
                return Status.Failure("Invalid AFV bands");
            }

            int start = c * 64;
            weights[start]              = 1.0f; // Not used, but causes MSAN error otherwise.
            weights[start + 1 * 8 + 0]  = afvWeightsC[0];
            weights[start + 0 * 8 + 1]  = afvWeightsC[1];
            weights[start + 2 * 8 + 0]  = afvWeightsC[2];
            weights[start + 0 * 8 + 2]  = afvWeightsC[3];
            weights[start + 2 * 8 + 2]  = afvWeightsC[4];

            if (!InterpolateAfvEvenWeights(start, lo, hi, bands, weights))
            {
                return false;
            }

            CopyAfvSubblockWeights(c * num, weights4x8C, weights4x4C, weights);

            return true;
        }

        private static Status InterpolateAfvEvenWeights(
            int start,
            float lo,
            float hi,
            ReadOnlySpan<float> bands,
            Span<float> weights)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    if (x < 2 && y < 2)
                    {
                        continue;
                    }

                    StatusOr<float> valResult = Interpolate(AfvFreqs[y * 4 + x] - lo, hi, bands, 4);

                    if (!valResult.IsOk)
                    {
                        return valResult.Status;
                    }

                    weights[start + (2 * y) * 8 + (2 * x)] = valResult.Value;
                }
            }

            return true;
        }

        private static void CopyAfvSubblockWeights(
            int cBase,
            ReadOnlySpan<float> weights4x8C,
            ReadOnlySpan<float> weights4x4C,
            Span<float> weights)
        {
            const int N = (int)FrameDimensions.kBlockDim;

            // Put 4x8 weights in odd rows, except (1, 0).
            for (int y = 0; y < N / 2; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    if (x != 0 || y != 0)
                    {
                        weights[cBase + (2 * y + 1) * N + x] = weights4x8C[y * 8 + x];
                    }
                }
            }

            // Put 4x4 weights in even rows / odd columns, except (0, 1).
            for (int y = 0; y < N / 2; y++)
            {
                for (int x = 0; x < N / 2; x++)
                {
                    if (x != 0 || y != 0)
                    {
                        weights[cBase + (2 * y) * N + (2 * x + 1)] = weights4x4C[y * 4 + x];
                    }
                }
            }
        }

        private static Status StoreAndInvertWeights(
            ReadOnlySpan<float> weights,
            float* tableOut,
            float* invTableOut)
        {
            const float maxAllowed = 1.0f / kAlmostZero;

            for (int i = 0; i < weights.Length; i++)
            {
                float invVal = weights[i];

                if (!(invVal >= kAlmostZero && invVal < maxAllowed))
                {
                    return Status.Failure("Invalid quantization table");
                }

                tableOut[i]    = 1.0f / invVal;
                invTableOut[i] = invVal;
            }

            return true;
        }

        private static void ZeroLowestFrequencyInverseWeights(int quantTableIdx, float* invTableBase)
        {
            // Ensure that the lowest frequencies have a 0 inverse table.
            // This does not affect en/decoding, but allows AC strategy selection to be
            // slightly simpler.
            nuint xs = (nuint)RequiredSizeX[quantTableIdx];
            nuint ys = (nuint)RequiredSizeY[quantTableIdx];
            CoeffOrder.CoefficientLayout(ref ys, ref xs);

            for (nuint c = 0; c < 3; c++)
            {
                nuint cOffset = c * ys * xs * FrameDimensions.kDCTBlockSize;

                for (nuint y = 0; y < ys; y++)
                {
                    nuint rowOffset = cOffset + y * FrameDimensions.kBlockDim * xs;

                    for (nuint x = 0; x < xs; x++)
                    {
                        invTableBase[rowOffset + x] = 0.0f;
                    }
                }
            }
        }

        private static QuantEncoding[] LibraryInit()
        {
            DctQuantWeightParams dct4x8Params = new DctQuantWeightParams(
                new float[] { 2198.050556016380522f, -0.96269623020744692f, -0.76194253026666783f, -0.6551140670773547f },
                new float[] { 764.3655248643528689f, -0.92630200888366945f, -0.9675229603596517f, -0.27845290869168118f },
                new float[] { 527.107573587542228f, -1.4594385811273854f, -1.450082094097871593f, -1.5843722511996204f },
                4);

            DctQuantWeightParams dct4x4Params = new DctQuantWeightParams(
                new float[] { 2200.0f, 0.0f, 0.0f, 0.0f },
                new float[] { 392.0f, 0.0f, 0.0f, 0.0f },
                new float[] { 112.0f, -0.25f, -0.25f, -0.5f },
                4);

            return new QuantEncoding[]
            {
                // 0: DCT
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 3150.0f, 0.0f, -0.4f, -0.4f, -0.4f, -2.0f },
                    new float[] { 560.0f, 0.0f, -0.3f, -0.3f, -0.3f, -0.3f },
                    new float[] { 512.0f, -2.0f, -1.0f, 0.0f, -1.0f, -2.0f },
                    6)),

                // 1: IDENTITY
                QuantEncoding.Identity(
                    new float[] { 280.0f, 3160.0f, 3160.0f },
                    new float[] { 60.0f, 864.0f, 864.0f },
                    new float[] { 18.0f, 200.0f, 200.0f }),

                // 2: DCT2X2
                QuantEncoding.DCT2(
                    new float[] { 3840.0f, 2560.0f, 1280.0f, 640.0f, 480.0f, 300.0f },
                    new float[] { 960.0f, 640.0f, 320.0f, 180.0f, 140.0f, 120.0f },
                    new float[] { 640.0f, 320.0f, 128.0f, 64.0f, 32.0f, 16.0f }),

                // 3: DCT4X4
                QuantEncoding.DCT4(
                    dct4x4Params,
                    new float[] { 1.0f, 1.0f },
                    new float[] { 1.0f, 1.0f },
                    new float[] { 1.0f, 1.0f }),

                // 4: DCT16X16
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 8996.8725711814115328f, -1.3000777393353804f, -0.49424529824571225f, -0.439093774457103443f, -0.6350101832695744f, -0.90177264050827612f, -1.6162099239887414f },
                    new float[] { 3191.48366296844234752f, -0.67424582104194355f, -0.80745813428471001f, -0.44925837484843441f, -0.35865440981033403f, -0.31322389111877305f, -0.37615025315725483f },
                    new float[] { 1157.50408145487200256f, -2.0531423165804414f, -1.4f, -0.50687130033378396f, -0.42708730624733904f, -1.4856834539296244f, -4.9209142884401604f },
                    7)),

                // 5: DCT32X32
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 15718.40830982518931456f, -1.025f, -0.98f, -0.9012f, -0.4f, -0.48819395464f, -0.421064f, -0.27f },
                    new float[] { 7305.7636810695983104f, -0.8041958212306401f, -0.7633036457487539f, -0.55660379990111464f, -0.49785304658857626f, -0.43699592683512467f, -0.40180866526242109f, -0.27321683125358037f },
                    new float[] { 3803.53173721215041536f, -3.060733579805728f, -2.0413270132490346f, -2.0235650159727417f, -0.5495389509954993f, -0.4f, -0.4f, -0.3f },
                    8)),

                // 6: DCT8X16
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 7240.7734393502f, -0.7f, -0.7f, -0.2f, -0.2f, -0.2f, -0.5f },
                    new float[] { 1448.15468787004f, -0.5f, -0.5f, -0.5f, -0.2f, -0.2f, -0.2f },
                    new float[] { 506.854140754517f, -1.4f, -0.2f, -0.5f, -0.5f, -1.5f, -3.6f },
                    7)),

                // 7: DCT8X32
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 16283.2494710648897f, -1.7812845336559429f, -1.6309059012653515f, -1.0382179034313539f, -0.85f, -0.7f, -0.9f, -1.2360638576849587f },
                    new float[] { 5089.15750884921511936f, -0.320049391452786891f, -0.35362849922161446f, -0.30340000000000003f, -0.61f, -0.5f, -0.5f, -0.6f },
                    new float[] { 3397.77603275308720128f, -0.321327362693153371f, -0.34507619223117997f, -0.70340000000000003f, -0.9f, -1.0f, -1.0f, -1.1754605576265209f },
                    8)),

                // 8: DCT16X32
                QuantEncoding.DCT(new DctQuantWeightParams(
                    new float[] { 13844.97076442300573f, -0.97113799999999995f, -0.658f, -0.42026f, -0.22712f, -0.2206f, -0.226f, -0.6f },
                    new float[] { 4798.964084220744293f, -0.61125308982767057f, -0.83770786552491361f, -0.79014862079498627f, -0.2692727459704829f, -0.38272769465388551f, -0.22924222653091453f, -0.20719098826199578f },
                    new float[] { 1807.236946760964614f, -1.2f, -1.2f, -0.7f, -0.7f, -0.7f, -0.4f, -0.5f },
                    8)),

                // 9: DCT4X8
                QuantEncoding.DCT4X8(
                    dct4x8Params,
                    new float[] { 1.0f, 1.0f, 1.0f }),

                // 10: AFV0
                QuantEncoding.AFV(
                    dct4x8Params,
                    dct4x4Params,
                    new float[] { 3072.0f, 3072.0f, 256.0f, 256.0f, 256.0f, 414.0f, 0.0f, 0.0f, 0.0f },
                    new float[] { 1024.0f, 1024.0f, 50.0f, 50.0f, 50.0f, 58.0f, 0.0f, 0.0f, 0.0f },
                    new float[] { 384.0f, 384.0f, 12.0f, 12.0f, 12.0f, 22.0f, -0.25f, -0.25f, -0.25f }),

                // 11: DCT64X64
                CreateLargeSquareDctLibrary(0.9),

                // 12: DCT32X64
                CreateLargeRectDctLibrary(0.65),

                // 13: DCT128X128
                CreateLargeSquareDctLibrary(1.8),

                // 14: DCT64X128
                CreateLargeRectDctLibrary(1.3),

                // 15: DCT256X256
                CreateLargeSquareDctLibrary(3.6),

                // 16: DCT128X256
                CreateLargeRectDctLibrary(2.6),
            };
        }

        private static QuantEncoding CreateLargeSquareDctLibrary(double scale)
        {
            return QuantEncoding.DCT(new DctQuantWeightParams(
                new float[] { (float)(scale * 26629.073922049845), -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f },
                new float[] { (float)(scale * 9311.3238710010046), -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f },
                new float[] { (float)(scale * 4992.2486445538634), -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f },
                8));
        }

        private static QuantEncoding CreateLargeRectDctLibrary(double scale)
        {
            return QuantEncoding.DCT(new DctQuantWeightParams(
                new float[] { (float)(scale * 23629.073922049845), -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f },
                new float[] { (float)(scale * 8611.3238710010046), -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f },
                new float[] { (float)(scale * 4492.2486445538634), -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f },
                8));
        }
    }
}
