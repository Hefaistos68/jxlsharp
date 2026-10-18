// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// 2-level Huffman decoder table and bitstream reader.
    /// Ported from <c>jxl::HuffmanDecodingData</c> in <c>lib/jxl/dec_huffman.h</c> and <c>lib/jxl/dec_huffman.cc</c>.
    /// </summary>
    public sealed class HuffmanDecodingData
    {
        /// <summary>Root table bit width (8).</summary>
        public const int KHuffmanTableBits = 8;

        private const int KCodeLengthCodes       = 18;
        private const byte KDefaultCodeLength    = 8;
        private const byte KCodeLengthRepeatCode = 16;

        private static readonly byte[] CodeLengthCodeOrder =
        {
            1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        };

        // Static Huffman code for the code length code lengths
        private static readonly HuffmanCode[] StaticHuff =
        {
            new HuffmanCode(2, 0), new HuffmanCode(2, 4), new HuffmanCode(2, 3), new HuffmanCode(3, 2),
            new HuffmanCode(2, 0), new HuffmanCode(2, 4), new HuffmanCode(2, 3), new HuffmanCode(4, 1),
            new HuffmanCode(2, 0), new HuffmanCode(2, 4), new HuffmanCode(2, 3), new HuffmanCode(3, 2),
            new HuffmanCode(2, 0), new HuffmanCode(2, 4), new HuffmanCode(2, 3), new HuffmanCode(4, 5),
        };

        /// <summary>Underlying 2-level Huffman lookup table.</summary>
        public HuffmanCode[] Table = Array.Empty<HuffmanCode>();

        /// <summary>
        /// Decodes the Huffman code lengths from the bitstream and populates <see cref="Table"/>
        /// with the corresponding 2-level Huffman decoding table.
        /// </summary>
        /// <param name="alphabetSize">Maximum alphabet size.</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><c>true</c> if the Huffman table was decoded; <c>false</c> if invalid.</returns>
        public bool ReadFromBitStream(nuint alphabetSize, BitReader br)
        {
            if (alphabetSize > (1u << AnsParams.PrefixMaxBits))
            {
                return false;
            }

            // simple_code_or_skip is used as follows:
            // 1 for simple code;
            // 0 for no skipping, 2 skips 2 code lengths, 3 skips 3 code lengths
            uint simpleCodeOrSkip = (uint)br.ReadFixedBits(2);

            if (simpleCodeOrSkip == 1u)
            {
                Table = new HuffmanCode[1 << KHuffmanTableBits];

                return ReadSimpleCode((int)alphabetSize, br, Table);
            }

            Span<byte> codeLengthCodeLengths = stackalloc byte[KCodeLengthCodes];
            codeLengthCodeLengths.Clear();

            int space    = 32;
            int numCodes = 0;

            for (int i = (int)simpleCodeOrSkip; i < KCodeLengthCodes && space > 0; ++i)
            {
                int codeLenIdx    = CodeLengthCodeOrder[i];
                br.Refill();
                HuffmanCode entry = StaticHuff[(int)br.PeekFixedBits(4)];
                br.Consume(entry.Bits);
                byte v            = (byte)entry.Value;

                codeLengthCodeLengths[codeLenIdx] = v;

                if (v != 0)
                {
                    space -= 32 >> v;
                    ++numCodes;
                }
            }

            byte[] codeLengths = new byte[(int)alphabetSize];
            bool ok            = (numCodes == 1 || space == 0) &&
                                 ReadHuffmanCodeLengths(codeLengthCodeLengths, (int)alphabetSize, codeLengths, br);

            if (!ok)
            {
                return false;
            }

            Span<ushort> counts = stackalloc ushort[16];
            counts.Clear();

            for (int i = 0; i < (int)alphabetSize; ++i)
            {
                ++counts[codeLengths[i]];
            }

            Table          = new HuffmanCode[(int)alphabetSize + 376];
            uint tableSize = HuffmanTable.BuildHuffmanTable(
                Table,
                KHuffmanTableBits,
                codeLengths,
                (int)alphabetSize,
                counts);

            if (tableSize == 0)
            {
                Table = Array.Empty<HuffmanCode>();

                return false;
            }

            if (tableSize < (uint)Table.Length)
            {
                Array.Resize(ref Table, (int)tableSize);
            }

            return true;
        }

        /// <summary>
        /// Decodes the next Huffman-coded symbol from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns>Decoded symbol value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadSymbol(BitReader br)
        {
            int idx          = (int)br.PeekBits(KHuffmanTableBits);
            HuffmanCode code = Table[idx];
            int nBits        = code.Bits;

            if (nBits > KHuffmanTableBits)
            {
                br.Consume(KHuffmanTableBits);
                nBits -= KHuffmanTableBits;
                idx   += code.Value;
                idx   += (int)br.PeekBits((nuint)nBits);
                code   = Table[idx];
            }

            br.Consume(code.Bits);

            return code.Value;
        }

        private static bool ReadHuffmanCodeLengths(
            ReadOnlySpan<byte> codeLengthCodeLengths,
            int numSymbols,
            Span<byte> codeLengths,
            BitReader br)
        {
            Span<HuffmanCode> table = stackalloc HuffmanCode[32];
            Span<ushort> counts     = stackalloc ushort[16];
            counts.Clear();

            for (int i = 0; i < KCodeLengthCodes; ++i)
            {
                ++counts[codeLengthCodeLengths[i]];
            }

            if (HuffmanTable.BuildHuffmanTable(table, 5, codeLengthCodeLengths, KCodeLengthCodes, counts) == 0)
            {
                return false;
            }

            RepeatCodeState state = new RepeatCodeState
            {
                Symbol        = 0,
                PrevCodeLen   = KDefaultCodeLength,
                Repeat        = 0,
                RepeatCodeLen = 0,
                Space         = 32768,
            };

            while (state.Symbol < numSymbols && state.Space > 0)
            {
                br.Refill();
                HuffmanCode p = table[(int)br.PeekFixedBits(5)];
                br.Consume(p.Bits);
                byte codeLen  = (byte)p.Value;

                if (codeLen < KCodeLengthRepeatCode)
                {
                    state.Repeat                      = 0;
                    codeLengths[state.Symbol++]       = codeLen;

                    if (codeLen != 0)
                    {
                        state.PrevCodeLen = codeLen;
                        state.Space      -= 32768 >> codeLen;
                    }
                }
                else if (!ApplyRepeatCode(codeLen, numSymbols, br, codeLengths, ref state))
                {
                    return false;
                }
            }

            if (state.Space != 0)
            {
                return false;
            }

            codeLengths.Slice(state.Symbol, numSymbols - state.Symbol).Clear();

            return true;
        }

        private static bool ApplyRepeatCode(
            byte codeLen,
            int numSymbols,
            BitReader br,
            Span<byte> codeLengths,
            ref RepeatCodeState state)
        {
            int extraBits = codeLen - 14;
            byte newLen   = codeLen == KCodeLengthRepeatCode ? state.PrevCodeLen : (byte)0;

            if (state.RepeatCodeLen != newLen)
            {
                state.Repeat        = 0;
                state.RepeatCodeLen = newLen;
            }

            int oldRepeat = state.Repeat;

            if (state.Repeat > 0)
            {
                state.Repeat -= 2;
                state.Repeat <<= extraBits;
            }

            state.Repeat   += (int)br.ReadBits((nuint)extraBits) + 3;
            int repeatDelta = state.Repeat - oldRepeat;

            if (state.Symbol + repeatDelta > numSymbols)
            {
                return false;
            }

            codeLengths.Slice(state.Symbol, repeatDelta).Fill(state.RepeatCodeLen);
            state.Symbol += repeatDelta;

            if (state.RepeatCodeLen != 0)
            {
                state.Space -= repeatDelta << (15 - state.RepeatCodeLen);
            }

            return true;
        }

        private struct RepeatCodeState
        {
            public int Symbol;
            public byte PrevCodeLen;
            public int Repeat;
            public byte RepeatCodeLen;
            public int Space;
        }

        private static bool ReadSimpleCode(int alphabetSize, BitReader br, Span<HuffmanCode> table)
        {
            nuint maxBits   = alphabetSize > 1 ? (nuint)(Bits.FloorLog2Nonzero((uint)(alphabetSize - 1)) + 1) : 0u;
            int numSymbols  = (int)br.ReadFixedBits(2) + 1;
            Span<ushort> symbols = stackalloc ushort[4];
            symbols.Clear();

            for (int i = 0; i < numSymbols; ++i)
            {
                ushort symbol = (ushort)br.ReadBits(maxBits);

                if (symbol >= alphabetSize)
                {
                    return false;
                }

                symbols[i] = symbol;
            }

            if (HasDuplicateSymbols(symbols, numSymbols))
            {
                return false;
            }

            // 4 symbols have two options to encode.
            if (numSymbols == 4)
            {
                numSymbols += (int)br.ReadFixedBits(1);
            }

            int tableSize = PopulateSimpleTableBase(numSymbols, symbols, table);

            if (tableSize == 0)
            {
                return false;
            }

            const int goalSize = 1 << KHuffmanTableBits;

            while (tableSize != goalSize)
            {
                table.Slice(0, tableSize).CopyTo(table.Slice(tableSize, tableSize));
                tableSize <<= 1;
            }

            return true;
        }

        private static bool HasDuplicateSymbols(ReadOnlySpan<ushort> symbols, int numSymbols)
        {
            for (int i = 0; i < numSymbols - 1; ++i)
            {
                for (int j = i + 1; j < numSymbols; ++j)
                {
                    if (symbols[i] == symbols[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int PopulateSimpleTableBase(int numSymbols, Span<ushort> symbols, Span<HuffmanCode> table)
        {
            switch (numSymbols)
            {
                case 1:
                    table[0] = new HuffmanCode(0, symbols[0]);

                    return 1;

                case 2:
                    SwapIfGreater(symbols, 0, 1);
                    table[0] = new HuffmanCode(1, symbols[0]);
                    table[1] = new HuffmanCode(1, symbols[1]);

                    return 2;

                case 3:
                    SwapIfGreater(symbols, 1, 2);
                    table[0] = new HuffmanCode(1, symbols[0]);
                    table[2] = new HuffmanCode(1, symbols[0]);
                    table[1] = new HuffmanCode(2, symbols[1]);
                    table[3] = new HuffmanCode(2, symbols[2]);

                    return 4;

                case 4:
                    SortFourSymbols(symbols);
                    table[0] = new HuffmanCode(2, symbols[0]);
                    table[2] = new HuffmanCode(2, symbols[1]);
                    table[1] = new HuffmanCode(2, symbols[2]);
                    table[3] = new HuffmanCode(2, symbols[3]);

                    return 4;

                case 5:
                    SwapIfGreater(symbols, 2, 3);
                    table[0] = new HuffmanCode(1, symbols[0]);
                    table[1] = new HuffmanCode(2, symbols[1]);
                    table[2] = new HuffmanCode(1, symbols[0]);
                    table[3] = new HuffmanCode(3, symbols[2]);
                    table[4] = new HuffmanCode(1, symbols[0]);
                    table[5] = new HuffmanCode(2, symbols[1]);
                    table[6] = new HuffmanCode(1, symbols[0]);
                    table[7] = new HuffmanCode(3, symbols[3]);

                    return 8;

                default:
                    return 0;
            }
        }

        private static void SwapIfGreater(Span<ushort> symbols, int i, int j)
        {
            if (symbols[i] > symbols[j])
            {
                ushort t   = symbols[j];
                symbols[j] = symbols[i];
                symbols[i] = t;
            }
        }

        private static void SortFourSymbols(Span<ushort> symbols)
        {
            for (int i = 0; i < 3; ++i)
            {
                for (int j = i + 1; j < 4; ++j)
                {
                    SwapIfGreater(symbols, i, j);
                }
            }
        }
    }
}
