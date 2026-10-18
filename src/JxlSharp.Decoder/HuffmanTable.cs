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
    /// Single entry in a 2-level Huffman lookup table.
    /// Ported from <c>jxl::HuffmanCode</c> in <c>lib/jxl/huffman_table.h</c>.
    /// </summary>
    public struct HuffmanCode
    {
        /// <summary>Number of bits used for this symbol (or sub-table width).</summary>
        public byte Bits;

        /// <summary>Symbol value or sub-table offset.</summary>
        public ushort Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="HuffmanCode"/> struct.
        /// </summary>
        /// <param name="bits">Number of bits.</param>
        /// <param name="value">Symbol value or table offset.</param>
        public HuffmanCode(byte bits, ushort value)
        {
            Bits  = bits;
            Value = value;
        }
    }

    /// <summary>
    /// A node of a Huffman tree used during encoder tree construction.
    /// Ported from <c>jxl::HuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.h</c>.
    /// </summary>
    public struct HuffmanTreeNode
    {
        /// <summary>Total frequency count of this subtree.</summary>
        public uint TotalCount;

        /// <summary>Index of left child (<c>-1</c> for leaf nodes).</summary>
        public short IndexLeft;

        /// <summary>Index of right child, or symbol value when <see cref="IndexLeft"/> is <c>-1</c>.</summary>
        public short IndexRightOrValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="HuffmanTreeNode"/> struct.
        /// </summary>
        /// <param name="count">Total frequency count.</param>
        /// <param name="left">Left child index.</param>
        /// <param name="right">Right child index or leaf symbol value.</param>
        public HuffmanTreeNode(uint count, short left, short right)
        {
            TotalCount        = count;
            IndexLeft         = left;
            IndexRightOrValue = right;
        }
    }

    /// <summary>
    /// Builds 2-level Huffman lookup tables and constructs/encodes canonical Huffman trees.
    /// Ported from <c>lib/jxl/huffman_table.cc</c>, <c>lib/jxl/enc_huffman_tree.cc</c>, and <c>lib/jxl/enc_huffman.cc</c>.
    /// </summary>
    public static class HuffmanTable
    {
        private const int KCodeLengthCodes = 18;

        private static readonly byte[] StorageOrder =
        {
            1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        };

        private static readonly byte[] HuffmanBitLengthSymbols = { 0, 7, 3, 2, 1, 15 };
        private static readonly byte[] HuffmanBitLengthLengths = { 2, 4, 3, 2, 2, 4 };

        private static readonly byte[] ReverseNibbleLut =
        {
            0x0, 0x8, 0x4, 0xc, 0x2, 0xa, 0x6, 0xe,
            0x1, 0x9, 0x5, 0xd, 0x3, 0xb, 0x7, 0xf,
        };

        /// <summary>
        /// Builds a Huffman lookup table assuming code lengths are in symbol order.
        /// Returns 0 in case of error (invalid tree), otherwise the populated size of the table.
        /// </summary>
        /// <param name="rootTable">Destination lookup table buffer.</param>
        /// <param name="rootBits">Number of bits in the root lookup table.</param>
        /// <param name="codeLengths">Code lengths per symbol.</param>
        /// <param name="codeLengthsSize">Number of symbols in <paramref name="codeLengths"/>.</param>
        /// <param name="count">Histogram of bit lengths (modified in place during table construction).</param>
        /// <returns>Populated size of <paramref name="rootTable"/>, or 0 on error.</returns>
        public static uint BuildHuffmanTable(
            Span<HuffmanCode> rootTable,
            int rootBits,
            ReadOnlySpan<byte> codeLengths,
            int codeLengthsSize,
            Span<ushort> count)
        {
            if (codeLengthsSize > (1 << AnsParams.PrefixMaxBits))
            {
                return 0;
            }

            Span<ushort> offset = stackalloc ushort[AnsParams.PrefixMaxBits + 1];
            int maxLength       = 1;
            ushort sum          = 0;

            for (int len = 1; len <= AnsParams.PrefixMaxBits; len++)
            {
                offset[len] = sum;

                if (count[len] != 0)
                {
                    sum       = (ushort)(sum + count[len]);
                    maxLength = len;
                }
            }

            ushort[] sorted = new ushort[codeLengthsSize];

            for (int sym = 0; sym < codeLengthsSize; sym++)
            {
                byte cl = codeLengths[sym];

                if (cl != 0)
                {
                    sorted[offset[cl]++] = (ushort)sym;
                }
            }

            int totalSize = 1 << rootBits;

            // Special case code with only one value
            if (offset[AnsParams.PrefixMaxBits] == 1)
            {
                HuffmanCode singleCode = new HuffmanCode(0, sorted[0]);
                rootTable.Slice(0, totalSize).Fill(singleCode);

                return (uint)totalSize;
            }

            FillRootTable(rootTable, rootBits, maxLength, sorted, count, out int key, out int symbol);

            return (uint)FillSecondLevelTables(rootTable, rootBits, maxLength, sorted, count, key, symbol);
        }

        /// <summary>
        /// Creates a depth-limited canonical Huffman tree from symbol frequency counts.
        /// Ported from <c>jxl::CreateHuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="data">Symbol population counts.</param>
        /// <param name="length">Number of symbols.</param>
        /// <param name="treeLimit">Maximum code bit length.</param>
        /// <param name="depth">Output bit depths per symbol.</param>
        public static void CreateHuffmanTree(
            ReadOnlySpan<uint> data,
            int length,
            int treeLimit,
            Span<byte> depth)
        {
            for (uint countLimit = 1; ; countLimit *= 2)
            {
                depth.Slice(0, length).Clear();
                List<HuffmanTreeNode> tree = BuildInitialLeaves(data, length, countLimit);
                int n                      = tree.Count;

                if (n == 1)
                {
                    depth[tree[0].IndexRightOrValue] = 1;

                    break;
                }

                tree.Sort(CompareNodes);
                MergeTreeNodes(tree, n);
                SetDepth(tree[(2 * n) - 1], tree, depth, 0);

                if (MaxDepth(depth.Slice(0, length)) <= treeLimit)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Writes a Huffman tree from bit depths into the RLE bitstream representation.
        /// Ported from <c>jxl::WriteHuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="depth">Input bit depths.</param>
        /// <param name="length">Alphabet length.</param>
        /// <param name="treeSize">Output compressed tree token count.</param>
        /// <param name="tree">Output compressed tree tokens.</param>
        /// <param name="extraBitsData">Output extra bits per token.</param>
        public static void WriteHuffmanTree(
            ReadOnlySpan<byte> depth,
            int length,
            out int treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            treeSize           = 0;
            byte previousValue = 8;
            int newLength      = TrimTrailingZeroDepths(depth, length);

            bool useRleForNonZero = false;
            bool useRleForZero    = false;

            if (length > 50)
            {
                DecideOverRleUse(depth, newLength, out useRleForNonZero, out useRleForZero);
            }

            for (int i = 0; i < newLength;)
            {
                byte value = depth[i];
                int reps   = CountRepetitions(depth, i, newLength, value, useRleForNonZero, useRleForZero);

                if (value == 0)
                {
                    WriteHuffmanTreeRepetitionsZeros(reps, ref treeSize, tree, extraBitsData);
                }
                else
                {
                    WriteHuffmanTreeRepetitions(previousValue, value, reps, ref treeSize, tree, extraBitsData);
                    previousValue = value;
                }

                i += reps;
            }
        }

        /// <summary>
        /// Converts canonical Huffman bit depths into bit-reversed codewords.
        /// Ported from <c>jxl::ConvertBitDepthsToSymbols</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="depth">Bit depths per symbol.</param>
        /// <param name="len">Number of symbols.</param>
        /// <param name="bits">Output bit-reversed codewords.</param>
        public static void ConvertBitDepthsToSymbols(
            ReadOnlySpan<byte> depth,
            int len,
            Span<ushort> bits)
        {
            const int kMaxBits   = 16;
            Span<ushort> blCount = stackalloc ushort[kMaxBits];
            blCount.Clear();

            for (int i = 0; i < len; ++i)
            {
                ++blCount[depth[i]];
            }

            blCount[0]            = 0;
            Span<ushort> nextCode = stackalloc ushort[kMaxBits];
            nextCode[0]           = 0;
            int code              = 0;

            for (int i = 1; i < kMaxBits; ++i)
            {
                code        = (code + blCount[i - 1]) << 1;
                nextCode[i] = (ushort)code;
            }

            for (int i = 0; i < len; ++i)
            {
                byte d = depth[i];

                if (d != 0)
                {
                    bits[i] = ReverseBits(d, nextCode[d]++);
                }
            }
        }

        /// <summary>
        /// Builds a Huffman tree for the given histogram and encodes it into <paramref name="writer"/>.
        /// Ported from <c>jxl::BuildAndStoreHuffmanTree</c> in <c>lib/jxl/enc_huffman.cc</c>.
        /// </summary>
        /// <param name="histogram">Symbol frequency counts.</param>
        /// <param name="length">Alphabet length.</param>
        /// <param name="depth">Output symbol bit depths.</param>
        /// <param name="bits">Output symbol codewords.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status BuildAndStoreHuffmanTree(
            ReadOnlySpan<uint> histogram,
            int length,
            Span<byte> depth,
            Span<ushort> bits,
            BitWriter writer)
        {
            int count    = 0;
            Span<int> s4 = stackalloc int[4];
            s4.Clear();

            for (int i = 0; i < length; i++)
            {
                if (histogram[i] != 0)
                {
                    if (count < 4)
                    {
                        s4[count] = i;
                    }
                    else if (count > 4)
                    {
                        break;
                    }

                    count++;
                }
            }

            nuint maxBits = CountMaxBits(length);

            if (count <= 1)
            {
                writer.Write(4, 1);
                writer.Write(maxBits, (ulong)s4[0]);

                return true;
            }

            CreateHuffmanTree(histogram, length, 15, depth);
            ConvertBitDepthsToSymbols(depth, length, bits);

            if (count <= 4)
            {
                StoreSimpleHuffmanTree(depth, s4, count, maxBits, writer);

                return true;
            }

            return StoreHuffmanTree(depth, length, writer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNextKey(int key, int len)
        {
            int step = 1 << (len - 1);

            while ((key & step) != 0)
            {
                step >>= 1;
            }

            return (key & (step - 1)) + step;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReplicateValue(Span<HuffmanCode> table, int offset, int step, int end, HuffmanCode code)
        {
            do
            {
                end                -= step;
                table[offset + end] = code;
            }
            while (end > 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextTableBitSize(ReadOnlySpan<ushort> count, int len, int rootBits)
        {
            int left = 1 << (len - rootBits);

            while (len < AnsParams.PrefixMaxBits)
            {
                if (left <= count[len])
                {
                    break;
                }

                left -= count[len];
                ++len;
                left <<= 1;
            }

            return len - rootBits;
        }

        private static void FillRootTable(
            Span<HuffmanCode> rootTable,
            int rootBits,
            int maxLength,
            ReadOnlySpan<ushort> sorted,
            Span<ushort> count,
            out int key,
            out int symbol)
        {
            int tableBits = rootBits;
            int tableSize = 1 << tableBits;
            int totalSize = tableSize;

            if (tableBits > maxLength)
            {
                tableBits = maxLength;
                tableSize = 1 << tableBits;
            }

            key              = 0;
            symbol           = 0;
            HuffmanCode code = new HuffmanCode(1, 0);
            int step         = 2;

            do
            {
                for (; count[code.Bits] != 0; --count[code.Bits])
                {
                    code.Value = sorted[symbol++];
                    ReplicateValue(rootTable, key, step, tableSize, code);
                    key = GetNextKey(key, code.Bits);
                }

                step <<= 1;
            }
            while (++code.Bits <= tableBits);

            while (totalSize != tableSize)
            {
                rootTable.Slice(0, tableSize).CopyTo(rootTable.Slice(tableSize, tableSize));
                tableSize <<= 1;
            }
        }

        private static int FillSecondLevelTables(
            Span<HuffmanCode> rootTable,
            int rootBits,
            int maxLength,
            ReadOnlySpan<ushort> sorted,
            Span<ushort> count,
            int key,
            int symbol)
        {
            int totalSize    = 1 << rootBits;
            int tableOffset  = 0;
            int tableSize    = totalSize;
            int mask         = totalSize - 1;
            int low          = -1;
            HuffmanCode code = default;

            for (int len = rootBits + 1, step = 2; len <= maxLength; ++len, step <<= 1)
            {
                for (; count[len] != 0; --count[len])
                {
                    if ((key & mask) != low)
                    {
                        tableOffset          += tableSize;
                        int tableBits         = NextTableBitSize(count, len, rootBits);
                        tableSize             = 1 << tableBits;
                        totalSize            += tableSize;
                        low                   = key & mask;
                        rootTable[low].Bits   = (byte)(tableBits + rootBits);
                        rootTable[low].Value  = (ushort)(tableOffset - low);
                    }

                    code.Bits  = (byte)(len - rootBits);
                    code.Value = sorted[symbol++];
                    ReplicateValue(rootTable, tableOffset + (key >> rootBits), step, tableSize, code);
                    key = GetNextKey(key, len);
                }
            }

            return totalSize;
        }

        private static List<HuffmanTreeNode> BuildInitialLeaves(
            ReadOnlySpan<uint> data,
            int length,
            uint countLimit)
        {
            List<HuffmanTreeNode> tree = new List<HuffmanTreeNode>((2 * length) + 1);

            for (int i = length - 1; i >= 0; --i)
            {
                if (data[i] != 0)
                {
                    uint count = Math.Max(data[i], countLimit - 1);
                    tree.Add(new HuffmanTreeNode(count, -1, (short)i));
                }
            }

            return tree;
        }

        private static int CompareNodes(HuffmanTreeNode v0, HuffmanTreeNode v1)
        {
            if (v0.TotalCount != v1.TotalCount)
            {
                return v0.TotalCount < v1.TotalCount ? -1 : 1;
            }

            return v0.IndexRightOrValue.CompareTo(v1.IndexRightOrValue) * -1;
        }

        private static void MergeTreeNodes(List<HuffmanTreeNode> tree, int n)
        {
            HuffmanTreeNode sentinel = new HuffmanTreeNode(uint.MaxValue, -1, -1);
            tree.Add(sentinel);
            tree.Add(sentinel);

            int i = 0;
            int j = n + 1;

            for (int k = n - 1; k != 0; --k)
            {
                int left  = tree[i].TotalCount <= tree[j].TotalCount ? i++ : j++;
                int right = tree[i].TotalCount <= tree[j].TotalCount ? i++ : j++;

                int jEnd = tree.Count - 1;
                tree[jEnd] = new HuffmanTreeNode(
                    tree[left].TotalCount + tree[right].TotalCount,
                    (short)left,
                    (short)right);

                tree.Add(sentinel);
            }
        }

        private static void SetDepth(
            HuffmanTreeNode p,
            List<HuffmanTreeNode> pool,
            Span<byte> depth,
            byte level)
        {
            if (p.IndexLeft >= 0)
            {
                ++level;
                SetDepth(pool[p.IndexLeft], pool, depth, level);
                SetDepth(pool[p.IndexRightOrValue], pool, depth, level);
            }
            else
            {
                depth[p.IndexRightOrValue] = level;
            }
        }

        private static byte MaxDepth(ReadOnlySpan<byte> depth)
        {
            byte max = 0;

            for (int i = 0; i < depth.Length; i++)
            {
                if (depth[i] > max)
                {
                    max = depth[i];
                }
            }

            return max;
        }

        private static int TrimTrailingZeroDepths(ReadOnlySpan<byte> depth, int length)
        {
            int newLength = length;

            for (int i = 0; i < length; ++i)
            {
                if (depth[length - i - 1] == 0)
                {
                    --newLength;
                }
                else
                {
                    break;
                }
            }

            return newLength;
        }

        private static void DecideOverRleUse(
            ReadOnlySpan<byte> depth,
            int length,
            out bool useRleForNonZero,
            out bool useRleForZero)
        {
            int totalRepsZero    = 0;
            int totalRepsNonZero = 0;
            int countRepsZero    = 1;
            int countRepsNonZero = 1;

            for (int i = 0; i < length;)
            {
                byte value = depth[i];
                int reps   = 1;

                for (int k = i + 1; k < length && depth[k] == value; ++k)
                {
                    ++reps;
                }

                if (reps >= 3 && value == 0)
                {
                    totalRepsZero += reps;
                    ++countRepsZero;
                }

                if (reps >= 4 && value != 0)
                {
                    totalRepsNonZero += reps;
                    ++countRepsNonZero;
                }

                i += reps;
            }

            useRleForNonZero = totalRepsNonZero > countRepsNonZero * 2;
            useRleForZero    = totalRepsZero > countRepsZero * 2;
        }

        private static int CountRepetitions(
            ReadOnlySpan<byte> depth,
            int startIndex,
            int newLength,
            byte value,
            bool useRleForNonZero,
            bool useRleForZero)
        {
            int reps = 1;

            if ((value != 0 && useRleForNonZero) || (value == 0 && useRleForZero))
            {
                for (int k = startIndex + 1; k < newLength && depth[k] == value; ++k)
                {
                    ++reps;
                }
            }

            return reps;
        }

        private static void WriteHuffmanTreeRepetitions(
            byte previousValue,
            byte value,
            int repetitions,
            ref int treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            Debug.Assert(repetitions > 0);

            if (previousValue != value)
            {
                tree[treeSize]          = value;
                extraBitsData[treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions == 7)
            {
                tree[treeSize]          = value;
                extraBitsData[treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions < 3)
            {
                for (int i = 0; i < repetitions; ++i)
                {
                    tree[treeSize]          = value;
                    extraBitsData[treeSize] = 0;
                    ++treeSize;
                }
            }
            else
            {
                repetitions -= 3;
                int start    = treeSize;

                while (true)
                {
                    tree[treeSize]          = 16;
                    extraBitsData[treeSize] = (byte)(repetitions & 0x3);
                    ++treeSize;
                    repetitions >>= 2;

                    if (repetitions == 0)
                    {
                        break;
                    }

                    --repetitions;
                }

                ReverseSpan(tree, start, treeSize);
                ReverseSpan(extraBitsData, start, treeSize);
            }
        }

        private static void WriteHuffmanTreeRepetitionsZeros(
            int repetitions,
            ref int treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            if (repetitions == 11)
            {
                tree[treeSize]          = 0;
                extraBitsData[treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions < 3)
            {
                for (int i = 0; i < repetitions; ++i)
                {
                    tree[treeSize]          = 0;
                    extraBitsData[treeSize] = 0;
                    ++treeSize;
                }
            }
            else
            {
                repetitions -= 3;
                int start    = treeSize;

                while (true)
                {
                    tree[treeSize]          = 17;
                    extraBitsData[treeSize] = (byte)(repetitions & 0x7);
                    ++treeSize;
                    repetitions >>= 3;

                    if (repetitions == 0)
                    {
                        break;
                    }

                    --repetitions;
                }

                ReverseSpan(tree, start, treeSize);
                ReverseSpan(extraBitsData, start, treeSize);
            }
        }

        private static void ReverseSpan(Span<byte> v, int start, int end)
        {
            v.Slice(start, end - start).Reverse();
        }

        private static ushort ReverseBits(int numBits, ushort bits)
        {
            uint retval = ReverseNibbleLut[bits & 0xF];

            for (int i = 4; i < numBits; i += 4)
            {
                retval <<= 4;
                bits     = (ushort)(bits >> 4);
                retval  |= ReverseNibbleLut[bits & 0xF];
            }

            retval >>= -numBits & 0x3;

            return (ushort)retval;
        }

        private static nuint CountMaxBits(int length)
        {
            int maxBitsCounter = length - 1;
            nuint maxBits      = 0;

            while (maxBitsCounter > 0)
            {
                maxBitsCounter >>= 1;
                ++maxBits;
            }

            return maxBits;
        }

        private static void StoreSimpleHuffmanTree(
            ReadOnlySpan<byte> depths,
            Span<int> symbols,
            int numSymbols,
            nuint maxBits,
            BitWriter writer)
        {
            writer.Write(2, 1);
            writer.Write(2, (ulong)(numSymbols - 1));

            for (int i = 0; i < numSymbols; i++)
            {
                for (int j = i + 1; j < numSymbols; j++)
                {
                    if (depths[symbols[j]] < depths[symbols[i]])
                    {
                        int tmp    = symbols[j];
                        symbols[j] = symbols[i];
                        symbols[i] = tmp;
                    }
                }
            }

            for (int i = 0; i < numSymbols; i++)
            {
                writer.Write(maxBits, (ulong)symbols[i]);
            }

            if (numSymbols == 4)
            {
                writer.Write(1, depths[symbols[0]] == 1 ? 1UL : 0UL);
            }
        }

        private static Status StoreHuffmanTree(ReadOnlySpan<byte> depths, int num, BitWriter writer)
        {
            byte[] huffmanTree          = new byte[num];
            byte[] huffmanTreeExtraBits = new byte[num];
            WriteHuffmanTree(depths, num, out int huffmanTreeSize, huffmanTree, huffmanTreeExtraBits);

            Span<uint> huffmanTreeHistogram = stackalloc uint[KCodeLengthCodes];
            huffmanTreeHistogram.Clear();

            for (int i = 0; i < huffmanTreeSize; ++i)
            {
                ++huffmanTreeHistogram[huffmanTree[i]];
            }

            CountNonZeroCodes(huffmanTreeHistogram, out int numCodes, out int singleCode);

            Span<byte> codeLengthBitdepth          = stackalloc byte[KCodeLengthCodes];
            Span<ushort> codeLengthBitdepthSymbols = stackalloc ushort[KCodeLengthCodes];
            codeLengthBitdepth.Clear();
            codeLengthBitdepthSymbols.Clear();

            CreateHuffmanTree(huffmanTreeHistogram, KCodeLengthCodes, 5, codeLengthBitdepth);
            ConvertBitDepthsToSymbols(codeLengthBitdepth, KCodeLengthCodes, codeLengthBitdepthSymbols);

            StoreHuffmanTreeOfHuffmanTreeToBitMask(numCodes, codeLengthBitdepth, writer);

            if (numCodes == 1)
            {
                codeLengthBitdepth[singleCode] = 0;
            }

            return StoreHuffmanTreeToBitMask(
                huffmanTreeSize,
                huffmanTree,
                huffmanTreeExtraBits,
                codeLengthBitdepth,
                codeLengthBitdepthSymbols,
                writer);
        }

        private static void CountNonZeroCodes(ReadOnlySpan<uint> histogram, out int numCodes, out int singleCode)
        {
            numCodes   = 0;
            singleCode = 0;

            for (int i = 0; i < KCodeLengthCodes; ++i)
            {
                if (histogram[i] != 0)
                {
                    if (numCodes == 0)
                    {
                        singleCode = i;
                        numCodes   = 1;
                    }
                    else
                    {
                        numCodes = 2;

                        break;
                    }
                }
            }
        }

        private static void StoreHuffmanTreeOfHuffmanTreeToBitMask(
            int numCodes,
            ReadOnlySpan<byte> codeLengthBitdepth,
            BitWriter writer)
        {
            int codesToStore = KCodeLengthCodes;

            if (numCodes > 1)
            {
                for (; codesToStore > 0; --codesToStore)
                {
                    if (codeLengthBitdepth[StorageOrder[codesToStore - 1]] != 0)
                    {
                        break;
                    }
                }
            }

            int skipSome = 0;

            if (codeLengthBitdepth[StorageOrder[0]] == 0 &&
                codeLengthBitdepth[StorageOrder[1]] == 0)
            {
                skipSome = 2;

                if (codeLengthBitdepth[StorageOrder[2]] == 0)
                {
                    skipSome = 3;
                }
            }

            writer.Write(2, (ulong)skipSome);

            for (int i = skipSome; i < codesToStore; ++i)
            {
                int l = codeLengthBitdepth[StorageOrder[i]];
                writer.Write(HuffmanBitLengthLengths[l], HuffmanBitLengthSymbols[l]);
            }
        }

        private static Status StoreHuffmanTreeToBitMask(
            int huffmanTreeSize,
            ReadOnlySpan<byte> huffmanTree,
            ReadOnlySpan<byte> huffmanTreeExtraBits,
            ReadOnlySpan<byte> codeLengthBitdepth,
            ReadOnlySpan<ushort> codeLengthBitdepthSymbols,
            BitWriter writer)
        {
            for (int i = 0; i < huffmanTreeSize; ++i)
            {
                int ix = huffmanTree[i];

                if (ix > 17)
                {
                    return Status.Failure("Invalid Huffman tree code length token");
                }

                writer.Write(codeLengthBitdepth[ix], codeLengthBitdepthSymbols[ix]);

                if (ix == 16)
                {
                    writer.Write(2, huffmanTreeExtraBits[i]);
                }
                else if (ix == 17)
                {
                    writer.Write(3, huffmanTreeExtraBits[i]);
                }
            }

            return true;
        }
    }
}
