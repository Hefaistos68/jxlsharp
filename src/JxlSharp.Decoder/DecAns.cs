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
    /// <summary>
    /// Hybrid unsigned integer encoding configuration.
    /// Ported from <c>jxl::HybridUintConfig</c> in <c>lib/jxl/dec_ans.h</c>.
    /// </summary>
    public readonly struct HybridUintConfig
    {
        /// <summary>Split exponent (<c>split_token = 1 &lt;&lt; split_exponent</c>).</summary>
        public readonly uint SplitExponent;

        /// <summary>Threshold below which values are represented directly as tokens.</summary>
        public readonly uint SplitToken;

        /// <summary>Number of most-significant mantissa bits stored in the token.</summary>
        public readonly uint MsbInToken;

        /// <summary>Number of least-significant mantissa bits stored in the token.</summary>
        public readonly uint LsbInToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="HybridUintConfig"/> struct with default values (<c>4, 2, 0</c>).
        /// </summary>
        public HybridUintConfig()
            : this(4, 2, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HybridUintConfig"/> struct.
        /// </summary>
        /// <param name="splitExponent">Split exponent.</param>
        /// <param name="msbInToken">Number of MSB bits in token.</param>
        /// <param name="lsbInToken">Number of LSB bits in token.</param>
        public HybridUintConfig(uint splitExponent = 4, uint msbInToken = 2, uint lsbInToken = 0)
        {
            Debug.Assert(splitExponent >= msbInToken + lsbInToken);
            SplitExponent = splitExponent;
            SplitToken    = 1u << (int)splitExponent;
            MsbInToken    = msbInToken;
            LsbInToken    = lsbInToken;
        }

        /// <summary>
        /// Encodes <paramref name="value"/> into a <paramref name="token"/> and extra <paramref name="bits"/>.
        /// </summary>
        /// <param name="value">Input unsigned value.</param>
        /// <param name="token">Output token index.</param>
        /// <param name="nbits">Output number of extra bits.</param>
        /// <param name="bits">Output extra bits value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encode(uint value, out uint token, out uint nbits, out uint bits)
        {
            if (value < SplitToken)
            {
                token = value;
                nbits = 0;
                bits  = 0;
            }
            else
            {
                uint n = (uint)Bits.FloorLog2Nonzero(value);
                uint m = value - (1u << (int)n);
                token  = SplitToken +
                         ((n - SplitExponent) << (int)(MsbInToken + LsbInToken)) +
                         ((m >> (int)(n - MsbInToken)) << (int)LsbInToken) +
                         (m & ((1u << (int)LsbInToken) - 1u));
                nbits  = n - MsbInToken - LsbInToken;
                bits   = (value >> (int)LsbInToken) & ((1u << (int)nbits) - 1u);
            }
        }

        /// <summary>
        /// Returns the bitmask for the LSB bits stored in the token.
        /// </summary>
        /// <returns><c>(1 &lt;&lt; LsbInToken) - 1</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint LsbMask() => (1u << (int)LsbInToken) - 1u;
    }

    /// <summary>
    /// LZ77 parameters for entropy streams.
    /// Ported from <c>jxl::LZ77Params</c> in <c>lib/jxl/dec_ans.h</c> and <c>lib/jxl/dec_ans.cc</c>.
    /// </summary>
    public sealed class LZ77Params : Fields
    {
        /// <summary>Whether LZ77 backward references are enabled.</summary>
        public bool Enabled;

        /// <summary>Symbols above <see cref="MinSymbol"/> represent a copy length.</summary>
        public uint MinSymbol;

        /// <summary>Base copy length added to the decoded length value.</summary>
        public uint MinLength;

        /// <summary>Hybrid uint configuration for copy lengths (not serialized by <see cref="VisitFields"/>).</summary>
        public HybridUintConfig LengthUintConfig = new HybridUintConfig(0, 0, 0);

        /// <summary>Clustered context index used for distance codes (not serialized by <see cref="VisitFields"/>).</summary>
        public nuint NonserializedDistanceContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="LZ77Params"/> class with default values.
        /// </summary>
        public LZ77Params()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "LZ77Params";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.Bool(false, ref Enabled))
            {
                return false;
            }

            if (!visitor.Conditional(Enabled))
            {
                return true;
            }

            if (!visitor.U32(
                U32Distr.Val(224),
                U32Distr.Val(512),
                U32Distr.Val(4096),
                U32Distr.BitsOffset(15, 8),
                224,
                ref MinSymbol))
            {
                return false;
            }

            return visitor.U32(
                U32Distr.Val(3),
                U32Distr.Val(4),
                U32Distr.BitsOffset(2, 5),
                U32Distr.BitsOffset(8, 9),
                3,
                ref MinLength);
        }
    }

    /// <summary>
    /// Decoded ANS / Prefix entropy tables and hybrid integer configurations.
    /// Ported from <c>jxl::ANSCode</c> in <c>lib/jxl/dec_ans.h</c>.
    /// </summary>
    public sealed class ANSCode
    {
        /// <summary>Alias tables across all histograms.</summary>
        public AliasTable.Entry[] AliasTables = Array.Empty<AliasTable.Entry>();

        /// <summary>Huffman decoding tables across all histograms.</summary>
        public HuffmanDecodingData[] HuffmanData = Array.Empty<HuffmanDecodingData>();

        /// <summary>Hybrid integer configurations per histogram.</summary>
        public HybridUintConfig[] UintConfig = Array.Empty<HybridUintConfig>();

        /// <summary>Degenerate single-symbol value per histogram (<c>-1</c> if non-degenerate).</summary>
        public int[] DegenerateSymbols = Array.Empty<int>();

        /// <summary>Whether Prefix (Huffman) coding is used instead of ANS.</summary>
        public bool UsePrefixCode;

        /// <summary>Log2 of the ANS alphabet size.</summary>
        public byte LogAlphaSize;

        /// <summary>LZ77 backward-reference parameters.</summary>
        public LZ77Params Lz77 = new LZ77Params();

        /// <summary>Maximum number of bits necessary to represent a <c>ReadHybridUint</c> result.</summary>
        public nuint MaxNumBits;

        /// <summary>Memory manager associated with this code.</summary>
        public JxlMemoryManager? MemoryManager;

        /// <summary>
        /// Updates <see cref="MaxNumBits"/> for a given <paramref name="ctx"/> and <paramref name="symbol"/>.
        /// </summary>
        /// <param name="ctx">Histogram index.</param>
        /// <param name="symbol">Symbol index.</param>
        public void UpdateMaxNumBits(nuint ctx, nuint symbol)
        {
            HybridUintConfig cfg = UintConfig[(int)ctx];

            if (Lz77.Enabled &&
                Lz77.NonserializedDistanceContext != ctx &&
                symbol >= Lz77.MinSymbol)
            {
                symbol -= Lz77.MinSymbol;
                cfg     = Lz77.LengthUintConfig;
            }

            nuint splitToken    = cfg.SplitToken;
            nuint msbInToken    = cfg.MsbInToken;
            nuint lsbInToken    = cfg.LsbInToken;
            nuint splitExponent = cfg.SplitExponent;

            if (symbol < splitToken)
            {
                if (splitExponent > MaxNumBits)
                {
                    MaxNumBits = splitExponent;
                }

                return;
            }

            uint nExtraBits = (uint)splitExponent - (uint)(msbInToken + lsbInToken) +
                              ((uint)(symbol - splitToken) >> (int)(msbInToken + lsbInToken));
            nuint totalBits = msbInToken + lsbInToken + nExtraBits + 1u;

            if (totalBits > MaxNumBits)
            {
                MaxNumBits = totalBits;
            }
        }
    }

    /// <summary>
    /// Stateful ANS / Huffman symbol and hybrid integer reader with LZ77 sliding window support.
    /// Ported from <c>jxl::ANSSymbolReader</c> in <c>lib/jxl/dec_ans.h</c> and <c>lib/jxl/dec_ans.cc</c>.
    /// </summary>
    public sealed class ANSSymbolReader
    {
        /// <summary>LZ77 sliding window size (<c>1 &lt;&lt; 20</c>).</summary>
        public const int KWindowSize = 1 << 20;

        /// <summary>Number of WebP lossless special distance codes (120).</summary>
        public const int KNumSpecialDistances = 120;

        /// <summary>Maximum checkpoint interval (512).</summary>
        public const int KMaxCheckpointInterval = 512;

        private const uint KWindowMask = KWindowSize - 1;

        // Table of special distance codes from WebP lossless.
        private static readonly sbyte[,] SpecialDistancesTable =
        {
            { 0, 1 },  { 1, 0 },  { 1, 1 },  { -1, 1 }, { 0, 2 },  { 2, 0 },  { 1, 2 },  { -1, 2 },
            { 2, 1 },  { -2, 1 }, { 2, 2 },  { -2, 2 }, { 0, 3 },  { 3, 0 },  { 1, 3 },  { -1, 3 },
            { 3, 1 },  { -3, 1 }, { 2, 3 },  { -2, 3 }, { 3, 2 },  { -3, 2 }, { 0, 4 },  { 4, 0 },
            { 1, 4 },  { -1, 4 }, { 4, 1 },  { -4, 1 }, { 3, 3 },  { -3, 3 }, { 2, 4 },  { -2, 4 },
            { 4, 2 },  { -4, 2 }, { 0, 5 },  { 3, 4 },  { -3, 4 }, { 4, 3 },  { -4, 3 }, { 5, 0 },
            { 1, 5 },  { -1, 5 }, { 5, 1 },  { -5, 1 }, { 2, 5 },  { -2, 5 }, { 5, 2 },  { -5, 2 },
            { 4, 4 },  { -4, 4 }, { 3, 5 },  { -3, 5 }, { 5, 3 },  { -5, 3 }, { 0, 6 },  { 6, 0 },
            { 1, 6 },  { -1, 6 }, { 6, 1 },  { -6, 1 }, { 2, 6 },  { -2, 6 }, { 6, 2 },  { -6, 2 },
            { 4, 5 },  { -4, 5 }, { 5, 4 },  { -5, 4 }, { 3, 6 },  { -3, 6 }, { 6, 3 },  { -6, 3 },
            { 0, 7 },  { 7, 0 },  { 1, 7 },  { -1, 7 }, { 5, 5 },  { -5, 5 }, { 7, 1 },  { -7, 1 },
            { 4, 6 },  { -4, 6 }, { 6, 4 },  { -6, 4 }, { 2, 7 },  { -2, 7 }, { 7, 2 },  { -7, 2 },
            { 3, 7 },  { -3, 7 }, { 7, 3 },  { -7, 3 }, { 5, 6 },  { -5, 6 }, { 6, 5 },  { -6, 5 },
            { 8, 0 },  { 4, 7 },  { -4, 7 }, { 7, 4 },  { -7, 4 }, { 8, 1 },  { 8, 2 },  { 6, 6 },
            { -6, 6 }, { 8, 3 },  { 5, 7 },  { -5, 7 }, { 7, 5 },  { -7, 5 }, { 8, 4 },  { 6, 7 },
            { -6, 7 }, { 7, 6 },  { -7, 6 }, { 8, 5 },  { 7, 7 },  { -7, 7 }, { 8, 6 },  { 8, 7 },
        };

        private AliasTable.Entry[] _aliasTables = Array.Empty<AliasTable.Entry>();
        private HuffmanDecodingData[] _huffmanData = Array.Empty<HuffmanDecodingData>();
        private bool _usePrefixCode;
        private uint _state = AnsParams.AnsSignature << 16;
        private HybridUintConfig[] _configs = Array.Empty<HybridUintConfig>();
        private uint _logAlphaSize;
        private uint _logEntrySize;
        private uint _entrySizeMinus1;

        private uint[]? _lz77Window;
        private uint _numDecoded;
        private uint _numToCopy;
        private uint _copyPos;
        private uint _lz77Ctx;
        private uint _lz77MinLength;
        private uint _lz77Threshold = 1u << 20;
        private HybridUintConfig _lz77LengthUint;
        private readonly uint[] _specialDistances = new uint[KNumSpecialDistances];
        private uint _numSpecialDistances;

        /// <summary>
        /// Snapshot of an <see cref="ANSSymbolReader"/> state for speculative decoding or rollback.
        /// </summary>
        public sealed class Checkpoint
        {
            /// <summary>Saved ANS state.</summary>
            public uint State;

            /// <summary>Saved remaining LZ77 copy count.</summary>
            public uint NumToCopy;

            /// <summary>Saved LZ77 copy position.</summary>
            public uint CopyPos;

            /// <summary>Saved total decoded symbol count.</summary>
            public uint NumDecoded;

            /// <summary>Saved LZ77 window slice.</summary>
            public readonly uint[] Lz77Window = new uint[KMaxCheckpointInterval];
        }

        /// <summary>
        /// Initializes a new default instance of the <see cref="ANSSymbolReader"/> class.
        /// </summary>
        public ANSSymbolReader()
        {
        }

        private ANSSymbolReader(ANSCode code, BitReader br, nuint distanceMultiplier, uint[]? lz77Window)
        {
            _aliasTables   = code.AliasTables;
            _huffmanData   = code.HuffmanData;
            _usePrefixCode = code.UsePrefixCode;
            _configs       = code.UintConfig;
            _lz77Window    = lz77Window;

            if (!_usePrefixCode)
            {
                _state           = (uint)br.ReadFixedBits(32);
                _logAlphaSize    = code.LogAlphaSize;
                _logEntrySize    = AnsParams.AnsLogTabSize - code.LogAlphaSize;
                _entrySizeMinus1 = (1u << (int)_logEntrySize) - 1u;
            }
            else
            {
                _state = AnsParams.AnsSignature << 16;
            }

            if (!code.Lz77.Enabled)
            {
                return;
            }

            _lz77Ctx             = (uint)code.Lz77.NonserializedDistanceContext;
            _lz77LengthUint      = code.Lz77.LengthUintConfig;
            _lz77Threshold       = code.Lz77.MinSymbol;
            _lz77MinLength       = code.Lz77.MinLength;
            _numSpecialDistances = distanceMultiplier == 0 ? 0u : (uint)KNumSpecialDistances;

            for (int i = 0; i < (int)_numSpecialDistances; i++)
            {
                _specialDistances[i] = (uint)SpecialDistance(i, (int)distanceMultiplier);
            }
        }

        private const int KInitialWindowSize = 4096;

        /// <summary>
        /// Creates a new <see cref="ANSSymbolReader"/> initialized from <paramref name="code"/> and <paramref name="br"/>.
        /// </summary>
        /// <param name="code">Decoded ANS / Prefix code tables.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="distanceMultiplier">2D distance multiplier for special distances.</param>
        /// <returns><see cref="StatusOr{ANSSymbolReader}"/> containing the initialized reader.</returns>
        public static StatusOr<ANSSymbolReader> Create(ANSCode code, BitReader br, nuint distanceMultiplier = 0)
        {
            uint[]? lz77Window = code.Lz77.Enabled ? new uint[KInitialWindowSize] : null;

            return new ANSSymbolReader(code, br, distanceMultiplier, lz77Window);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowLz77Window(uint requiredIndex)
        {
            int newSize = _lz77Window!.Length;

            while ((uint)newSize <= requiredIndex && newSize < KWindowSize)
            {
                newSize <<= 1;
            }

            if (newSize > KWindowSize)
            {
                newSize = KWindowSize;
            }

            Array.Resize(ref _lz77Window, newSize);
        }

        /// <summary>
        /// Computes the 2D special distance for <paramref name="index"/> and <paramref name="multiplier"/>.
        /// </summary>
        /// <param name="index">Special distance index (<c>0..119</c>).</param>
        /// <param name="multiplier">Row width multiplier.</param>
        /// <returns>Computed 1D distance (<c>&gt;= 1</c>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SpecialDistance(int index, int multiplier)
        {
            int dist = SpecialDistancesTable[index, 0] + (multiplier * SpecialDistancesTable[index, 1]);

            return dist > 1 ? dist : 1;
        }

        /// <summary>
        /// Decodes a hybrid unsigned integer given <paramref name="config"/> and <paramref name="token"/>.
        /// </summary>
        /// <param name="config">Hybrid uint configuration.</param>
        /// <param name="token">Decoded symbol token.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadHybridUintConfig(in HybridUintConfig config, nuint token, BitReader br)
        {
            nuint splitToken    = config.SplitToken;
            nuint msbInToken    = config.MsbInToken;
            nuint lsbInToken    = config.LsbInToken;
            nuint splitExponent = config.SplitExponent;

            if (token < splitToken)
            {
                return (uint)token;
            }

            uint nbits = (uint)splitExponent - (uint)(msbInToken + lsbInToken) +
                         ((uint)(token - splitToken) >> (int)(msbInToken + lsbInToken));
            nbits &= 31u;

            uint low   = (uint)token & ((1u << (int)lsbInToken) - 1u);
            token    >>= (int)lsbInToken;
            nuint bits = (nuint)br.PeekBits(nbits);
            br.Consume(nbits);

            nuint ret = (((((1u << (int)msbInToken) | (token & ((1u << (int)msbInToken) - 1u)))
                          << (int)nbits) | bits)
                         << (int)lsbInToken) | low;

            return (uint)ret;
        }

        /// <summary>
        /// Reads the next ANS symbol from histogram <paramref name="histoIdx"/> without refilling <paramref name="br"/>.
        /// </summary>
        /// <param name="histoIdx">Clustered histogram index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded symbol value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint ReadSymbolANSWithoutRefill(nuint histoIdx, BitReader br)
        {
            uint res                 = _state & (uint)AnsParams.AnsTabMask;
            AliasTable.Symbol symbol = AliasTable.LookupAtOffset(
                _aliasTables,
                histoIdx << (int)_logAlphaSize,
                res,
                _logEntrySize,
                _entrySizeMinus1);
            _state                   = ((uint)symbol.Freq * (_state >> (int)AnsParams.AnsLogTabSize)) + (uint)symbol.Offset;

            uint newState  = (_state << 16) | (uint)br.PeekFixedBits(16);
            bool normalize = _state < (1u << 16);
            _state         = normalize ? newState : _state;
            br.Consume(normalize ? 16u : 0u);

            return symbol.Value;
        }

        /// <summary>
        /// Reads the next Huffman symbol from histogram <paramref name="histoIdx"/> without refilling <paramref name="br"/>.
        /// </summary>
        /// <param name="histoIdx">Clustered histogram index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded symbol value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint ReadSymbolHuffWithoutRefill(nuint histoIdx, BitReader br)
        {
            return _huffmanData[(int)histoIdx].ReadSymbol(br);
        }

        /// <summary>
        /// Reads the next symbol from histogram <paramref name="histoIdx"/> without refilling <paramref name="br"/>.
        /// </summary>
        /// <param name="histoIdx">Clustered histogram index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded symbol value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint ReadSymbolWithoutRefill(nuint histoIdx, BitReader br)
        {
            if (_usePrefixCode)
            {
                return ReadSymbolHuffWithoutRefill(histoIdx, br);
            }

            return ReadSymbolANSWithoutRefill(histoIdx, br);
        }

        /// <summary>
        /// Refills <paramref name="br"/> and reads the next symbol from histogram <paramref name="histoIdx"/>.
        /// </summary>
        /// <param name="histoIdx">Clustered histogram index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded symbol value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint ReadSymbol(nuint histoIdx, BitReader br)
        {
            br.Refill();

            return ReadSymbolWithoutRefill(histoIdx, br);
        }

        /// <summary>
        /// Verifies that the final ANS state matches <see cref="AnsParams.AnsSignature"/>.
        /// </summary>
        /// <returns><c>true</c> if the final state is valid.</returns>
        public bool CheckANSFinalState() => _state == (AnsParams.AnsSignature << 16);

        /// <summary>
        /// Returns whether this reader uses LZ77 backward references.
        /// </summary>
        /// <returns><c>true</c> if LZ77 is enabled.</returns>
        public bool UsesLZ77() => _lz77Window != null;

        /// <summary>
        /// Returns whether this reader uses Huffman coding with RLE-only LZ77 distances (always distance 1).
        /// </summary>
        /// <returns><c>true</c> if Huffman RLE-only mode is active.</returns>
        public bool IsHuffRleOnly()
        {
            if (_lz77Window == null || !_usePrefixCode)
            {
                return false;
            }

            HuffmanCode[] table = _huffmanData[(int)_lz77Ctx].Table;

            for (int i = 0; i < HuffmanDecodingData.KHuffmanTableBits; i++)
            {
                if (table[i].Bits != 0 || table[i].Value != 1)
                {
                    return false;
                }
            }

            return _configs[(int)_lz77Ctx].SplitToken <= 1;
        }

        /// <summary>
        /// Reads a hybrid unsigned integer or RLE run length when <see cref="IsHuffRleOnly"/> is <c>true</c>.
        /// </summary>
        /// <param name="ctx">Clustered context index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="value">Decoded value if <paramref name="run"/> is unchanged.</param>
        /// <param name="run">Remaining run length if an RLE token was decoded.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadHybridUintClusteredHuffRleOnly(nuint ctx, BitReader br, ref uint value, ref uint run)
        {
            Debug.Assert(IsHuffRleOnly());
            br.Refill();
            nuint token = ReadSymbolHuffWithoutRefill(ctx, br);

            if (token >= _lz77Threshold)
            {
                run = ReadHybridUintConfig(in _lz77LengthUint, token - _lz77Threshold, br) + _lz77MinLength - 1u;

                return;
            }

            value = ReadHybridUintConfig(in _configs[(int)ctx], token, br);
        }

        /// <summary>
        /// Reads a hybrid unsigned integer for a clustered context <paramref name="ctx"/>.
        /// </summary>
        /// <param name="usesLz77">Whether LZ77 decoding is active.</param>
        /// <param name="ctx">Clustered context index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadHybridUintClustered(bool usesLz77, nuint ctx, BitReader br)
        {
            if (usesLz77 && _numToCopy > 0)
            {
                uint writeIdx = _numDecoded++ & KWindowMask;

                if (writeIdx >= (uint)_lz77Window!.Length)
                {
                    GrowLz77Window(writeIdx);
                }

                uint cached = _lz77Window[_copyPos++ & KWindowMask];
                _numToCopy--;
                _lz77Window[writeIdx] = cached;

                return cached;
            }

            br.Refill();
            nuint token = ReadSymbolWithoutRefill(ctx, br);

            if (usesLz77 && token >= _lz77Threshold)
            {
                return HandleLz77Match(token, br);
            }

            uint ret = ReadHybridUintConfig(in _configs[(int)ctx], token, br);

            if (usesLz77 && _lz77Window != null)
            {
                uint writeIdx = _numDecoded++ & KWindowMask;

                if (writeIdx >= (uint)_lz77Window.Length)
                {
                    GrowLz77Window(writeIdx);
                }

                _lz77Window[writeIdx] = ret;
            }

            return ret;
        }

        /// <summary>
        /// Reads a hybrid unsigned integer for unclustered context <paramref name="ctx"/> using <paramref name="contextMap"/>.
        /// </summary>
        /// <param name="usesLz77">Whether LZ77 decoding is active.</param>
        /// <param name="ctx">Unclustered context index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="contextMap">Context map mapping unclustered contexts to histogram indices.</param>
        /// <returns>Decoded unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadHybridUintInlined(bool usesLz77, nuint ctx, BitReader br, ReadOnlySpan<byte> contextMap)
        {
            return ReadHybridUintClustered(usesLz77, contextMap[(int)ctx], br);
        }

        /// <summary>
        /// Reads a hybrid unsigned integer for unclustered context <paramref name="ctx"/> with LZ77 enabled.
        /// </summary>
        /// <param name="ctx">Unclustered context index.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="contextMap">Context map mapping unclustered contexts to histogram indices.</param>
        /// <returns>Decoded unsigned integer.</returns>
        public uint ReadHybridUint(nuint ctx, BitReader br, ReadOnlySpan<byte> contextMap)
        {
            return ReadHybridUintClustered(usesLz77: true, contextMap[(int)ctx], br);
        }

        /// <summary>
        /// Checks if the clustered context <paramref name="ctx"/> only produces a single value and advances the LZ77 window by <paramref name="count"/>.
        /// </summary>
        /// <param name="ctx">Clustered context index.</param>
        /// <param name="value">Output single symbol value.</param>
        /// <param name="count">Number of symbols to advance.</param>
        /// <returns><c>true</c> if the context is a single constant value.</returns>
        public bool IsSingleValueAndAdvance(nuint ctx, out uint value, nuint count)
        {
            value = 0;

            if (_usePrefixCode || _numToCopy != 0)
            {
                return false;
            }

            uint res                 = _state & (uint)AnsParams.AnsTabMask;
            AliasTable.Symbol symbol = AliasTable.LookupAtOffset(
                _aliasTables,
                ctx << (int)_logAlphaSize,
                res,
                _logEntrySize,
                _entrySizeMinus1);

            if (symbol.Freq != AnsParams.AnsTabSize ||
                _configs[(int)ctx].SplitToken <= symbol.Value ||
                symbol.Value >= _lz77Threshold)
            {
                return false;
            }

            value = (uint)symbol.Value;

            if (_lz77Window != null)
            {
                for (nuint i = 0; i < count; i++)
                {
                    uint writeIdx = _numDecoded++ & KWindowMask;

                    if (writeIdx >= (uint)_lz77Window.Length)
                    {
                        GrowLz77Window(writeIdx);
                    }

                    _lz77Window[writeIdx] = value;
                }
            }

            return true;
        }

        /// <summary>
        /// Saves the current reader state into <paramref name="checkpoint"/>.
        /// </summary>
        /// <param name="checkpoint">Destination checkpoint.</param>
        public void Save(Checkpoint checkpoint)
        {
            checkpoint.State      = _state;
            checkpoint.NumDecoded = _numDecoded;
            checkpoint.NumToCopy  = _numToCopy;
            checkpoint.CopyPos    = _copyPos;

            if (_lz77Window != null)
            {
                int winStart = (int)(_numDecoded & KWindowMask);
                int winEnd   = (int)((_numDecoded + KMaxCheckpointInterval) & KWindowMask);

                if (winEnd > winStart)
                {
                    if (winEnd > _lz77Window.Length)
                    {
                        GrowLz77Window((uint)(winEnd - 1));
                    }

                    Array.Copy(_lz77Window, winStart, checkpoint.Lz77Window, 0, winEnd - winStart);
                }
                else
                {
                    if (_lz77Window.Length < KWindowSize)
                    {
                        GrowLz77Window(KWindowSize - 1);
                    }

                    int firstPart = KWindowSize - winStart;
                    Array.Copy(_lz77Window, winStart, checkpoint.Lz77Window, 0, firstPart);
                    Array.Copy(_lz77Window, 0, checkpoint.Lz77Window, firstPart, winEnd);
                }
            }
        }

        /// <summary>
        /// Restores the reader state from <paramref name="checkpoint"/>.
        /// </summary>
        /// <param name="checkpoint">Source checkpoint.</param>
        public void Restore(Checkpoint checkpoint)
        {
            _state      = checkpoint.State;
            Debug.Assert(_numDecoded <= checkpoint.NumDecoded + KMaxCheckpointInterval);
            _numDecoded = checkpoint.NumDecoded;
            _numToCopy  = checkpoint.NumToCopy;
            _copyPos    = checkpoint.CopyPos;

            if (_lz77Window != null)
            {
                int winStart = (int)(_numDecoded & KWindowMask);
                int winEnd   = (int)((_numDecoded + KMaxCheckpointInterval) & KWindowMask);

                if (winEnd > winStart)
                {
                    if (winEnd > _lz77Window.Length)
                    {
                        GrowLz77Window((uint)(winEnd - 1));
                    }

                    Array.Copy(checkpoint.Lz77Window, 0, _lz77Window, winStart, winEnd - winStart);
                }
                else
                {
                    if (_lz77Window.Length < KWindowSize)
                    {
                        GrowLz77Window(KWindowSize - 1);
                    }

                    int firstPart = KWindowSize - winStart;
                    Array.Copy(checkpoint.Lz77Window, 0, _lz77Window, winStart, firstPart);
                    Array.Copy(checkpoint.Lz77Window, firstPart, _lz77Window, 0, winEnd);
                }
            }
        }

        private uint HandleLz77Match(nuint token, BitReader br)
        {
            _numToCopy = ReadHybridUintConfig(in _lz77LengthUint, token - _lz77Threshold, br) + _lz77MinLength;
            br.Refill();

            nuint dToken  = ReadSymbolWithoutRefill(_lz77Ctx, br);
            uint distance = ReadHybridUintConfig(in _configs[(int)_lz77Ctx], dToken, br);

            if (distance < _numSpecialDistances)
            {
                distance = _specialDistances[distance];
            }
            else
            {
                distance = distance + 1u - _numSpecialDistances;
            }

            if (distance > _numDecoded)
            {
                distance = _numDecoded;
            }

            if (distance > KWindowSize)
            {
                distance = KWindowSize;
            }

            _copyPos = _numDecoded - distance;

            if (distance == 0)
            {
                int toFill = (int)Math.Min(_numToCopy, (uint)_lz77Window!.Length);
                Array.Clear(_lz77Window, 0, toFill);
            }

            if (_numToCopy < _lz77MinLength)
            {
                _numToCopy = 0;
                br.MarkUnhealthy();

                return 0;
            }

            uint writeIdx = _numDecoded++ & KWindowMask;

            if (writeIdx >= (uint)_lz77Window!.Length)
            {
                GrowLz77Window(writeIdx);
            }

            uint ret = _lz77Window[_copyPos++ & KWindowMask];
            _numToCopy--;
            _lz77Window[writeIdx] = ret;

            return ret;
        }
    }

    /// <summary>
    /// Decodes ANS and Prefix histograms and hybrid integer configurations from the bitstream.
    /// Ported from <c>lib/jxl/dec_ans.cc</c>.
    /// </summary>
    public static class DecAns
    {
        private static readonly byte[,] HistHuff =
        {
            { 3, 10 }, { 7, 12 }, { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 5, 0 },  { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 6, 11 }, { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 5, 0 },  { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 7, 13 }, { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 5, 0 },  { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 6, 11 }, { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
            { 3, 10 }, { 5, 0 },  { 3, 7 }, { 4, 3 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 5 },
            { 3, 10 }, { 4, 4 },  { 3, 7 }, { 4, 1 }, { 3, 6 }, { 3, 8 }, { 3, 9 }, { 4, 2 },
        };

        /// <summary>
        /// Decodes a variable-length 8-bit integer in <c>[0..255]</c> by reading 1 to 11 bits.
        /// </summary>
        /// <param name="input">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded integer in <c>[0..255]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DecodeVarLenUint8(BitReader input)
        {
            if (input.ReadFixedBits(1) != 0)
            {
                int nbits = (int)input.ReadFixedBits(3);

                return nbits == 0 ? 1 : (int)input.ReadBits((nuint)nbits) + (1 << nbits);
            }

            return 0;
        }

        /// <summary>
        /// Decodes a variable-length 16-bit integer in <c>[0..65535]</c> by reading 1 to 21 bits.
        /// </summary>
        /// <param name="input">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded integer in <c>[0..65535]</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DecodeVarLenUint16(BitReader input)
        {
            if (input.ReadFixedBits(1) != 0)
            {
                int nbits = (int)input.ReadFixedBits(4);

                return nbits == 0 ? 1 : (int)input.ReadBits((nuint)nbits) + (1 << nbits);
            }

            return 0;
        }

        /// <summary>
        /// Decodes a single <see cref="HybridUintConfig"/> from <paramref name="br"/>.
        /// </summary>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        /// <param name="uintConfig">Output hybrid uint configuration.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeUintConfig(nuint logAlphaSize, out HybridUintConfig uintConfig, BitReader br)
        {
            uintConfig = default;
            br.Refill();

            uint splitExponent = (uint)br.ReadBits((nuint)Bits.CeilLog2Nonzero((uint)logAlphaSize + 1u));
            uint msbInToken    = 0;
            uint lsbInToken    = 0;

            if (splitExponent != (uint)logAlphaSize)
            {
                nuint nbits = (nuint)Bits.CeilLog2Nonzero(splitExponent + 1u);
                msbInToken  = (uint)br.ReadBits(nbits);

                if (msbInToken > splitExponent)
                {
                    return Status.Failure("Invalid HybridUintConfig");
                }

                nbits      = (nuint)Bits.CeilLog2Nonzero(splitExponent - msbInToken + 1u);
                lsbInToken = (uint)br.ReadBits(nbits);
            }

            if (lsbInToken + msbInToken > splitExponent)
            {
                return Status.Failure("Invalid HybridUintConfig");
            }

            uintConfig = new HybridUintConfig(splitExponent, msbInToken, lsbInToken);

            return true;
        }

        /// <summary>
        /// Encodes a variable-length 8-bit integer in <c>[0..255]</c> into <paramref name="writer"/>.
        /// </summary>
        /// <param name="n">Value in <c>[0..255]</c>.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVarLenUint8(nuint n, BitWriter writer)
        {
            Debug.Assert(n <= 255);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero((uint)n);
                writer.Write(3, nbits);
                writer.Write(nbits, n - (1u << (int)nbits));
            }
        }

        /// <summary>
        /// Encodes a variable-length 16-bit integer in <c>[0..65535]</c> into <paramref name="writer"/>.
        /// </summary>
        /// <param name="n">Value in <c>[0..65535]</c>.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreVarLenUint16(nuint n, BitWriter writer)
        {
            Debug.Assert(n <= 65535);

            if (n == 0)
            {
                writer.Write(1, 0);
            }
            else
            {
                writer.Write(1, 1);
                nuint nbits = (nuint)Bits.FloorLog2Nonzero((uint)n);
                writer.Write(4, nbits);
                writer.Write(nbits, n - (1u << (int)nbits));
            }
        }

        /// <summary>
        /// Encodes a single <see cref="HybridUintConfig"/> into <paramref name="writer"/>.
        /// </summary>
        /// <param name="uintConfig">Configuration to encode.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfig(in HybridUintConfig uintConfig, BitWriter writer, nuint logAlphaSize)
        {
            writer.Write((nuint)Bits.CeilLog2Nonzero((uint)logAlphaSize + 1u), uintConfig.SplitExponent);

            if (uintConfig.SplitExponent == (uint)logAlphaSize)
            {
                return;
            }

            nuint nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent + 1u);
            writer.Write(nbits, uintConfig.MsbInToken);
            nbits = (nuint)Bits.CeilLog2Nonzero(uintConfig.SplitExponent - uintConfig.MsbInToken + 1u);
            writer.Write(nbits, uintConfig.LsbInToken);
        }

        /// <summary>
        /// Encodes a span of <see cref="HybridUintConfig"/> instances into <paramref name="writer"/>.
        /// </summary>
        /// <param name="uintConfig">Configurations to encode.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        public static void EncodeUintConfigs(
            ReadOnlySpan<HybridUintConfig> uintConfig,
            BitWriter writer,
            nuint logAlphaSize)
        {
            for (int i = 0; i < uintConfig.Length; i++)
            {
                EncodeUintConfig(in uintConfig[i], writer, logAlphaSize);
            }
        }

        /// <summary>
        /// Decodes an array of <see cref="HybridUintConfig"/> instances from <paramref name="br"/>.
        /// </summary>
        /// <param name="logAlphaSize">Log2 of alphabet size.</param>
        /// <param name="uintConfig">Span of configurations to populate.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeUintConfigs(
            nuint logAlphaSize,
            Span<HybridUintConfig> uintConfig,
            BitReader br)
        {
            for (int i = 0; i < uintConfig.Length; i++)
            {
                if (!DecodeUintConfig(logAlphaSize, out uintConfig[i], br))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Decodes histograms, context map, and ANS/Huffman tables from <paramref name="br"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="numContexts">Number of entropy contexts.</param>
        /// <param name="code">Output <see cref="ANSCode"/>.</param>
        /// <param name="contextMap">Output context map.</param>
        /// <param name="disallowLz77">Whether LZ77 must be rejected.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeHistograms(
            JxlMemoryManager? memoryManager,
            BitReader br,
            nuint numContexts,
            ANSCode code,
            out byte[] contextMap,
            bool disallowLz77 = false)
        {
            contextMap = Array.Empty<byte>();

            if (!Bundle.Read(br, code.Lz77))
            {
                return false;
            }

            if (code.Lz77.Enabled)
            {
                numContexts++;

                if (!DecodeUintConfig(8, out code.Lz77.LengthUintConfig, br))
                {
                    return false;
                }
            }

            if (code.Lz77.Enabled && disallowLz77)
            {
                return Status.Failure("Using LZ77 when explicitly disallowed");
            }

            nuint numHistograms = 1;
            contextMap          = new byte[(int)numContexts];

            if (numContexts > 1 &&
                !DecContextMap.DecodeContextMap(memoryManager, contextMap, out numHistograms, br))
            {
                return false;
            }

            code.Lz77.NonserializedDistanceContext = contextMap[contextMap.Length - 1];
            code.UsePrefixCode                     = br.ReadFixedBits(1) != 0;
            code.LogAlphaSize                      = code.UsePrefixCode
                ? (byte)AnsParams.PrefixMaxBits
                : (byte)(br.ReadFixedBits(2) + 5);

            code.UintConfig = new HybridUintConfig[(int)numHistograms];

            if (!DecodeUintConfigs(code.LogAlphaSize, code.UintConfig, br))
            {
                return false;
            }

            nuint maxAlphabetSize = 1u << code.LogAlphaSize;

            return DecodeANSCodes(memoryManager, numHistograms, maxAlphabetSize, br, code);
        }

        /// <summary>
        /// Decodes the ANS or Huffman tables for <paramref name="numHistograms"/> histograms.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="numHistograms">Number of histograms.</param>
        /// <param name="maxAlphabetSize">Maximum alphabet size.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <param name="result">Destination <see cref="ANSCode"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeANSCodes(
            JxlMemoryManager? memoryManager,
            nuint numHistograms,
            nuint maxAlphabetSize,
            BitReader br,
            ANSCode result)
        {
            result.MemoryManager     = memoryManager;
            result.DegenerateSymbols = new int[(int)numHistograms];
            Array.Fill(result.DegenerateSymbols, -1);

            if (result.UsePrefixCode)
            {
                return DecodePrefixCodes((int)numHistograms, maxAlphabetSize, br, result);
            }

            return DecodeAnsAliasTables((int)numHistograms, maxAlphabetSize, br, result);
        }

        private static Status DecodePrefixCodes(
            int numHistograms,
            nuint maxAlphabetSize,
            BitReader br,
            ANSCode result)
        {
            if (maxAlphabetSize > (1u << AnsParams.PrefixMaxBits))
            {
                return false;
            }

            result.HuffmanData      = new HuffmanDecodingData[numHistograms];
            ushort[] alphabetSizes  = new ushort[numHistograms];

            for (int c = 0; c < numHistograms; c++)
            {
                result.HuffmanData[c] = new HuffmanDecodingData();
                alphabetSizes[c]      = (ushort)(DecodeVarLenUint16(br) + 1);

                if (alphabetSizes[c] > maxAlphabetSize)
                {
                    return Status.Failure($"Alphabet size is too long: {alphabetSizes[c]}");
                }
            }

            for (int c = 0; c < numHistograms; c++)
            {
                if (alphabetSizes[c] > 1)
                {
                    if (!result.HuffmanData[c].ReadFromBitStream(alphabetSizes[c], br))
                    {
                        if (!br.AllReadsWithinBounds())
                        {
                            return Status.NotEnoughBytes("Not enough bytes for huffman code");
                        }

                        return Status.Failure($"Invalid huffman tree number {c}, alphabet size {alphabetSizes[c]}");
                    }
                }
                else
                {
                    result.HuffmanData[c].Table = new HuffmanCode[1 << HuffmanDecodingData.KHuffmanTableBits];
                }

                UpdateHistogramMaxBitsFromPrefix(result, (nuint)c, result.HuffmanData[c].Table);
            }

            return true;
        }

        private static void UpdateHistogramMaxBitsFromPrefix(ANSCode result, nuint c, ReadOnlySpan<HuffmanCode> table)
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].Bits <= HuffmanDecodingData.KHuffmanTableBits)
                {
                    result.UpdateMaxNumBits(c, table[i].Value);
                }
            }
        }

        private static Status DecodeAnsAliasTables(
            int numHistograms,
            nuint maxAlphabetSize,
            BitReader br,
            ANSCode result)
        {
            if (maxAlphabetSize > AnsParams.AnsMaxAlphabetSize)
            {
                return false;
            }

            int stride         = 1 << result.LogAlphaSize;
            result.AliasTables = new AliasTable.Entry[numHistograms * stride];

            for (int c = 0; c < numHistograms; ++c)
            {
                if (!ReadHistogram((int)AnsParams.AnsLogTabSize, out List<int> counts, br))
                {
                    return Status.Failure("Invalid histogram bitstream.");
                }

                if ((nuint)counts.Count > maxAlphabetSize)
                {
                    return Status.Failure($"Alphabet size is too long: {counts.Count}");
                }

                while (counts.Count > 0 && counts[counts.Count - 1] == 0)
                {
                    counts.RemoveAt(counts.Count - 1);
                }

                int[] countsArray                 = counts.ToArray();
                result.DegenerateSymbols[c]       = FindDegenerateSymbol(result, (nuint)c, countsArray);
                Span<AliasTable.Entry> tableSlice = result.AliasTables.AsSpan(c * stride, stride);

                if (!AliasTable.InitAliasTable(countsArray, AnsParams.AnsLogTabSize, result.LogAlphaSize, tableSlice))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindDegenerateSymbol(ANSCode result, nuint c, ReadOnlySpan<int> counts)
        {
            for (int s = 0; s < counts.Length; s++)
            {
                if (counts[s] != 0)
                {
                    result.UpdateMaxNumBits(c, (nuint)s);
                }
            }

            int degenerateSymbol = counts.Length == 0 ? 0 : counts.Length - 1;

            for (int s = 0; s < degenerateSymbol; ++s)
            {
                if (counts[s] != 0)
                {
                    return -1;
                }
            }

            return degenerateSymbol;
        }

        private static Status ReadHistogram(int precisionBits, out List<int> counts, BitReader input)
        {
            counts         = new List<int>();
            int range      = 1 << precisionBits;
            int simpleCode = (int)input.ReadBits(1);

            if (simpleCode == 1)
            {
                return ReadSimpleHistogram(precisionBits, range, counts, input);
            }

            if (input.ReadBits(1) == 1)
            {
                int alphabetSize = DecodeVarLenUint8(input) + 1;

                if (alphabetSize > range)
                {
                    return false;
                }

                counts.AddRange(AliasTable.CreateFlatHistogram(alphabetSize, range));

                return true;
            }

            return ReadGeneralHistogram(range, counts, input);
        }

        private static Status ReadSimpleHistogram(int precisionBits, int range, List<int> counts, BitReader input)
        {
            Span<int> symbols = stackalloc int[2];
            int maxSymbol     = 0;
            int numSymbols    = (int)input.ReadBits(1) + 1;

            for (int i = 0; i < numSymbols; ++i)
            {
                symbols[i] = DecodeVarLenUint8(input);

                if (symbols[i] > maxSymbol)
                {
                    maxSymbol = symbols[i];
                }
            }

            for (int i = 0; i <= maxSymbol; i++)
            {
                counts.Add(0);
            }

            if (numSymbols == 1)
            {
                counts[symbols[0]] = range;

                return true;
            }

            if (symbols[0] == symbols[1])
            {
                return false;
            }

            counts[symbols[0]] = (int)input.ReadBits((nuint)precisionBits);
            counts[symbols[1]] = range - counts[symbols[0]];

            return true;
        }

        private static Status ReadGeneralHistogram(int range, List<int> counts, BitReader input)
        {
            if (!ReadHistogramShift(input, out uint shift))
            {
                return false;
            }

            int length = DecodeVarLenUint8(input) + 3;

            for (int i = 0; i < length; i++)
            {
                counts.Add(0);
            }

            int[] logcounts = new int[length];
            int[] same      = new int[length];

            if (!ReadLogCounts(length, logcounts, same, input, out int omitPos))
            {
                return false;
            }

            return PopulateCountsFromLogCounts(range, shift, omitPos, logcounts, same, counts, input);
        }

        private static Status ReadHistogramShift(BitReader input, out uint shift)
        {
            int upperBoundLog = (int)Bits.FloorLog2Nonzero(AnsParams.AnsLogTabSize + 1u);
            int log           = 0;

            for (; log < upperBoundLog; log++)
            {
                if (input.ReadFixedBits(1) == 0)
                {
                    break;
                }
            }

            shift = ((uint)input.ReadBits((nuint)log) | (1u << log)) - 1u;

            return shift <= AnsParams.AnsLogTabSize + 1u;
        }

        private static Status ReadLogCounts(
            int length,
            int[] logcounts,
            int[] same,
            BitReader input,
            out int omitPos)
        {
            int omitLog = -1;
            omitPos     = -1;

            for (int i = 0; i < length; ++i)
            {
                input.Refill();
                int idx      = (int)input.PeekFixedBits(7);
                input.Consume(HistHuff[idx, 0]);
                logcounts[i] = HistHuff[idx, 1] - 1;

                if (logcounts[i] == (int)AnsParams.AnsLogTabSize)
                {
                    int rleLength = DecodeVarLenUint8(input);
                    same[i]       = rleLength + 5;
                    i            += rleLength + 3;

                    continue;
                }

                if (logcounts[i] > omitLog)
                {
                    omitLog = logcounts[i];
                    omitPos = i;
                }
            }

            if (omitPos < 0)
            {
                return Status.Failure("Invalid histogram.");
            }

            if (omitPos + 1 < length && logcounts[omitPos + 1] == (int)AnsParams.AnsLogTabSize)
            {
                return Status.Failure("Invalid histogram.");
            }

            return true;
        }

        private static Status PopulateCountsFromLogCounts(
            int range,
            uint shift,
            int omitPos,
            int[] logcounts,
            int[] same,
            List<int> counts,
            BitReader input)
        {
            int length     = logcounts.Length;
            int totalCount = 0;
            int prev       = 0;
            int numsame    = 0;

            for (int i = 0; i < length; ++i)
            {
                if (same[i] != 0)
                {
                    numsame = same[i] - 1;
                    prev    = i > 0 ? counts[i - 1] : 0;
                }

                if (numsame > 0)
                {
                    counts[i] = prev;
                    numsame--;
                }
                else
                {
                    int code = logcounts[i];

                    if (i == omitPos || code < 0)
                    {
                        continue;
                    }

                    if (shift == 0 || code == 0)
                    {
                        counts[i] = 1 << code;
                    }
                    else
                    {
                        int bitcount = (int)AliasTable.GetPopulationCountPrecision((uint)code, shift);
                        counts[i]    = (1 << code) + ((int)input.ReadBits((nuint)bitcount) << (code - bitcount));
                    }
                }

                totalCount += counts[i];
            }

            counts[omitPos] = range - totalCount;

            return counts[omitPos] > 0 ? Status.OkStatus : Status.Failure("Invalid histogram count.");
        }
    }
}
