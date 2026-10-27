// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Library for creating Huffman codes from population counts.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// A node of a Huffman tree.
    /// Ported from <c>jxl::HuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.h</c>.
    /// </summary>
    public struct HuffmanTree
    {
        /// <summary>Combined symbol count for this subtree.</summary>
        public uint TotalCount;

        /// <summary>Left child index in the tree pool, or <c>-1</c> for a leaf.</summary>
        public short IndexLeft;

        /// <summary>Right child index in the tree pool, or symbol value for a leaf.</summary>
        public short IndexRightOrValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="HuffmanTree"/> struct.
        /// </summary>
        /// <param name="count">Symbol or subtree frequency count.</param>
        /// <param name="left">Left child index (<c>-1</c> if leaf).</param>
        /// <param name="right">Right child index or leaf symbol value.</param>
        public HuffmanTree(uint count, short left, short right)
        {
            TotalCount        = count;
            IndexLeft         = left;
            IndexRightOrValue = right;
        }
    }

    /// <summary>
    /// Prefix (Huffman) tree construction and bitstream serialization methods.
    /// Ported from <c>lib/jxl/enc_huffman_tree.h</c>, <c>lib/jxl/enc_huffman_tree.cc</c>,
    /// <c>lib/jxl/enc_huffman.h</c>, and <c>lib/jxl/enc_huffman.cc</c>.
    /// </summary>
    public static class EncHuffman
    {
        private const int KCodeLengthCodes = 18;

        private static readonly byte[] KStorageOrder =
        {
            1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        };

        // The bit lengths of the Huffman code over the code length alphabet
        // are compressed with the following static Huffman code:
        //   Symbol   Code
        //   ------   ----
        //   0          00
        //   1        1110
        //   2         110
        //   3          01
        //   4          10
        //   5        1111
        private static readonly byte[] KHuffmanBitLengthHuffmanCodeSymbols =
        {
            0, 7, 3, 2, 1, 15,
        };

        private static readonly byte[] KHuffmanBitLengthHuffmanCodeBitLengths =
        {
            2, 4, 3, 2, 2, 4,
        };

        private static readonly byte[] KReverse4Lut =
        {
            // Pre-reversed 4-bit values.
            0x0, 0x8, 0x4, 0xc, 0x2, 0xa, 0x6, 0xe,
            0x1, 0x9, 0x5, 0xd, 0x3, 0xb, 0x7, 0xf,
        };

        /// <summary>
        /// Recursively assigns bit depths to leaf symbols of the Huffman tree rooted at <paramref name="p"/>.
        /// Ported from <c>jxl::SetDepth</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="p">Current tree node.</param>
        /// <param name="pool">Tree node pool.</param>
        /// <param name="depth">Output per-symbol bit depths.</param>
        /// <param name="level">Current tree depth.</param>
        public static void SetDepth(in HuffmanTree p, ReadOnlySpan<HuffmanTree> pool, Span<byte> depth, byte level)
        {
            if (p.IndexLeft >= 0)
            {
                ++level;
                SetDepth(in pool[p.IndexLeft], pool, depth, level);
                SetDepth(in pool[p.IndexRightOrValue], pool, depth, level);
            }
            else
            {
                depth[p.IndexRightOrValue] = level;
            }
        }

        /// <summary>
        /// Creates a length-limited Huffman tree from population counts.
        /// Ported from <c>jxl::CreateHuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="data">Symbol population counts.</param>
        /// <param name="length">Alphabet length.</param>
        /// <param name="treeLimit">Maximum bit depth of the Huffman codes.</param>
        /// <param name="depth">Output per-symbol bit depths (must be zero-initialized by caller).</param>
        public static void CreateHuffmanTree(
            ReadOnlySpan<uint> data,
            nuint length,
            int treeLimit,
            Span<byte> depth)
        {
            List<HuffmanTree> tree = new List<HuffmanTree>((int)(2 * length + 1));

            for (uint countLimit = 1; ; countLimit *= 2)
            {
                tree.Clear();
                PopulateLeaves(data, length, countLimit, tree);
                int n = tree.Count;

                if (n == 0)
                {
                    break;
                }

                if (n == 1)
                {
                    // Fake value; will be fixed on upper level.
                    depth[tree[0].IndexRightOrValue] = 1;
                    break;
                }

                tree.Sort(CompareNodes);
                CombineHuffmanNodes(tree, n);
                HuffmanTree[] pool = tree.ToArray();
                SetDepth(in pool[(2 * n) - 1], pool, depth, 0);

                if (MaxDepth(depth.Slice(0, (int)length)) <= treeLimit)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Writes a Huffman tree from bit depths into the compact RLE bitstream representation.
        /// Ported from <c>jxl::WriteHuffmanTree</c> in <c>lib/jxl/enc_huffman_tree.cc</c>.
        /// </summary>
        /// <param name="depth">Input symbol bit depths.</param>
        /// <param name="length">Alphabet length.</param>
        /// <param name="treeSize">Outputs the number of code-length tokens written.</param>
        /// <param name="tree">Output code-length tokens.</param>
        /// <param name="extraBitsData">Output extra bits for repeat tokens 16 and 17.</param>
        public static void WriteHuffmanTree(
            ReadOnlySpan<byte> depth,
            nuint length,
            ref nuint treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            byte previousValue = 8;

            // Throw away trailing zeros.
            nuint newLength = TrimTrailingZeros(depth, length);

            // First gather statistics on if it is a good idea to do rle.
            bool useRleForNonZero = false;
            bool useRleForZero    = false;

            if (length > 50)
            {
                DecideOverRleUse(depth, newLength, out useRleForNonZero, out useRleForZero);
            }

            // Actual rle coding.
            for (nuint i = 0; i < newLength;)
            {
                byte value = depth[(int)i];
                nuint reps = CountRunLength(depth, newLength, i, value, useRleForNonZero, useRleForZero);

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
        /// <param name="depth">Symbol bit depths in <c>[0..15]</c>.</param>
        /// <param name="len">Number of symbols.</param>
        /// <param name="bits">Output bit-reversed codewords.</param>
        public static void ConvertBitDepthsToSymbols(
            ReadOnlySpan<byte> depth,
            nuint len,
            Span<ushort> bits)
        {
            // In Brotli, all bit depths are [1..15]
            // 0 bit depth means that the symbol does not exist.
            const int kMaxBits        = 16;
            Span<ushort> blCount      = stackalloc ushort[kMaxBits];
            Span<ushort> nextCode     = stackalloc ushort[kMaxBits];
            blCount.Clear();
            nextCode.Clear();

            for (nuint i = 0; i < len; ++i)
            {
                ++blCount[depth[(int)i]];
            }

            blCount[0] = 0;
            int code   = 0;

            for (int i = 1; i < kMaxBits; ++i)
            {
                code        = (code + blCount[i - 1]) << 1;
                nextCode[i] = (ushort)code;
            }

            for (nuint i = 0; i < len; ++i)
            {
                byte d = depth[(int)i];

                if (d != 0)
                {
                    bits[(int)i] = ReverseBits(d, nextCode[d]++);
                }
            }
        }

        /// <summary>
        /// Builds a Huffman tree for <paramref name="histogram"/> and encodes it into <paramref name="writer"/>
        /// in a format readable by <see cref="HuffmanDecodingData.ReadFromBitStream"/>.
        /// Ported from <c>jxl::BuildAndStoreHuffmanTree</c> in <c>lib/jxl/enc_huffman.cc</c>.
        /// </summary>
        /// <param name="histogram">Symbol frequency counts.</param>
        /// <param name="length">Alphabet length.</param>
        /// <param name="depth">Output per-symbol bit depths.</param>
        /// <param name="bits">Output per-symbol bit-reversed codewords.</param>
        /// <param name="writer">Optional destination bit writer (null to only build depth and bits).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status BuildAndStoreHuffmanTree(
            ReadOnlySpan<uint> histogram,
            nuint length,
            Span<byte> depth,
            Span<ushort> bits,
            BitWriter? writer)
        {
            nuint count    = 0;
            Span<nuint> s4 = stackalloc nuint[4];
            s4.Clear();

            for (nuint i = 0; i < length; i++)
            {
                if (histogram[(int)i] != 0)
                {
                    if (count < 4)
                    {
                        s4[(int)count] = i;
                    }
                    else if (count > 4)
                    {
                        break;
                    }

                    count++;
                }
            }

            nuint maxBitsCounter = length > 0 ? length - 1 : 0;
            nuint maxBits        = 0;

            while (maxBitsCounter != 0)
            {
                maxBitsCounter >>= 1;
                ++maxBits;
            }

            if (count <= 1)
            {
                // Output symbol bits and depths are initialized with 0, nothing to do.
                writer?.Write(4, 1);
                writer?.Write(maxBits, s4[0]);

                return true;
            }

            CreateHuffmanTree(histogram, length, 15, depth);
            ConvertBitDepthsToSymbols(depth, length, bits);

            if (writer == null)
            {
                return true;
            }

            if (count <= 4)
            {
                StoreSimpleHuffmanTree(depth, s4, count, maxBits, writer);

                return true;
            }

            return StoreHuffmanTree(depth, length, writer);
        }

        private static void PopulateLeaves(
            ReadOnlySpan<uint> data,
            nuint length,
            uint countLimit,
            List<HuffmanTree> tree)
        {
            for (nuint i = length; i != 0;)
            {
                --i;

                if (data[(int)i] != 0)
                {
                    uint count = Math.Max(data[(int)i], countLimit - 1);
                    tree.Add(new HuffmanTree(count, -1, (short)i));
                }
            }
        }

        private static int CompareNodes(HuffmanTree v0, HuffmanTree v1)
        {
            if (v0.TotalCount != v1.TotalCount)
            {
                return v0.TotalCount < v1.TotalCount ? -1 : 1;
            }

            return v0.IndexRightOrValue > v1.IndexRightOrValue
                ? -1
                : (v0.IndexRightOrValue < v1.IndexRightOrValue ? 1 : 0);
        }

        private static void CombineHuffmanNodes(List<HuffmanTree> tree, int n)
        {
            HuffmanTree sentinel = new HuffmanTree(uint.MaxValue, -1, -1);
            tree.Add(sentinel);
            tree.Add(sentinel);

            int i = 0;     // Points to the next leaf node.
            int j = n + 1; // Points to the next non-leaf node.

            for (int k = n - 1; k != 0; --k)
            {
                int left  = tree[i].TotalCount <= tree[j].TotalCount ? i++ : j++;
                int right = tree[i].TotalCount <= tree[j].TotalCount ? i++ : j++;

                int jEnd      = tree.Count - 1;
                uint sumCount = tree[left].TotalCount + tree[right].TotalCount;
                tree[jEnd]    = new HuffmanTree(sumCount, (short)left, (short)right);
                tree.Add(sentinel);
            }

            Debug.Assert(tree.Count == (2 * n) + 1);
        }

        private static int MaxDepth(ReadOnlySpan<byte> depth)
        {
            int max = 0;

            for (int i = 0; i < depth.Length; i++)
            {
                if (depth[i] > max)
                {
                    max = depth[i];
                }
            }

            return max;
        }

        private static nuint TrimTrailingZeros(ReadOnlySpan<byte> depth, nuint length)
        {
            nuint newLength = length;

            for (nuint i = 0; i < length; ++i)
            {
                if (depth[(int)(length - i - 1)] == 0)
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

        private static nuint CountRunLength(
            ReadOnlySpan<byte> depth,
            nuint newLength,
            nuint index,
            byte value,
            bool useRleForNonZero,
            bool useRleForZero)
        {
            nuint reps = 1;

            if ((value != 0 && useRleForNonZero) || (value == 0 && useRleForZero))
            {
                for (nuint k = index + 1; k < newLength && depth[(int)k] == value; ++k)
                {
                    ++reps;
                }
            }

            return reps;
        }

        private static void Reverse(Span<byte> v, nuint start, nuint end)
        {
            --end;

            while (start < end)
            {
                byte tmp        = v[(int)start];
                v[(int)start]   = v[(int)end];
                v[(int)end]     = tmp;
                ++start;
                --end;
            }
        }

        private static void WriteHuffmanTreeRepetitions(
            byte previousValue,
            byte value,
            nuint repetitions,
            ref nuint treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            Debug.Assert(repetitions > 0);

            if (previousValue != value)
            {
                tree[(int)treeSize]          = value;
                extraBitsData[(int)treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions == 7)
            {
                tree[(int)treeSize]          = value;
                extraBitsData[(int)treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions < 3)
            {
                for (nuint i = 0; i < repetitions; ++i)
                {
                    tree[(int)treeSize]          = value;
                    extraBitsData[(int)treeSize] = 0;
                    ++treeSize;
                }
            }
            else
            {
                repetitions -= 3;
                nuint start  = treeSize;

                while (true)
                {
                    tree[(int)treeSize]          = 16;
                    extraBitsData[(int)treeSize] = (byte)(repetitions & 0x3);
                    ++treeSize;
                    repetitions >>= 2;

                    if (repetitions == 0)
                    {
                        break;
                    }

                    --repetitions;
                }

                Reverse(tree, start, treeSize);
                Reverse(extraBitsData, start, treeSize);
            }
        }

        private static void WriteHuffmanTreeRepetitionsZeros(
            nuint repetitions,
            ref nuint treeSize,
            Span<byte> tree,
            Span<byte> extraBitsData)
        {
            if (repetitions == 11)
            {
                tree[(int)treeSize]          = 0;
                extraBitsData[(int)treeSize] = 0;
                ++treeSize;
                --repetitions;
            }

            if (repetitions < 3)
            {
                for (nuint i = 0; i < repetitions; ++i)
                {
                    tree[(int)treeSize]          = 0;
                    extraBitsData[(int)treeSize] = 0;
                    ++treeSize;
                }
            }
            else
            {
                repetitions -= 3;
                nuint start  = treeSize;

                while (true)
                {
                    tree[(int)treeSize]          = 17;
                    extraBitsData[(int)treeSize] = (byte)(repetitions & 0x7);
                    ++treeSize;
                    repetitions >>= 3;

                    if (repetitions == 0)
                    {
                        break;
                    }

                    --repetitions;
                }

                Reverse(tree, start, treeSize);
                Reverse(extraBitsData, start, treeSize);
            }
        }

        private static void DecideOverRleUse(
            ReadOnlySpan<byte> depth,
            nuint length,
            out bool useRleForNonZero,
            out bool useRleForZero)
        {
            nuint totalRepsZero    = 0;
            nuint totalRepsNonZero = 0;
            nuint countRepsZero    = 1;
            nuint countRepsNonZero = 1;

            for (nuint i = 0; i < length;)
            {
                byte value = depth[(int)i];
                nuint reps = 1;

                for (nuint k = i + 1; k < length && depth[(int)k] == value; ++k)
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

            useRleForNonZero = totalRepsNonZero > (countRepsNonZero * 2);
            useRleForZero    = totalRepsZero > (countRepsZero * 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReverseBits(int numBits, ushort bits)
        {
            nuint retval = KReverse4Lut[bits & 0xf];

            for (int i = 4; i < numBits; i += 4)
            {
                retval <<= 4;
                bits     = (ushort)(bits >> 4);
                retval  |= KReverse4Lut[bits & 0xf];
            }

            retval >>= -numBits & 0x3;

            return (ushort)retval;
        }

        private static void StoreHuffmanTreeOfHuffmanTreeToBitMask(
            int numCodes,
            ReadOnlySpan<byte> codeLengthBitdepth,
            BitWriter writer)
        {
            // Throw away trailing zeros:
            nuint codesToStore = KCodeLengthCodes;

            if (numCodes > 1)
            {
                for (; codesToStore > 0; --codesToStore)
                {
                    if (codeLengthBitdepth[KStorageOrder[codesToStore - 1]] != 0)
                    {
                        break;
                    }
                }
            }

            nuint skipSome = 0;

            if (codeLengthBitdepth[KStorageOrder[0]] == 0 &&
                codeLengthBitdepth[KStorageOrder[1]] == 0)
            {
                skipSome = 2;

                if (codeLengthBitdepth[KStorageOrder[2]] == 0)
                {
                    skipSome = 3;
                }
            }

            writer.Write(2, skipSome);

            for (nuint i = skipSome; i < codesToStore; ++i)
            {
                byte l = codeLengthBitdepth[KStorageOrder[i]];
                writer.Write(
                    KHuffmanBitLengthHuffmanCodeBitLengths[l],
                    KHuffmanBitLengthHuffmanCodeSymbols[l]);
            }
        }

        private static Status StoreHuffmanTreeToBitMask(
            nuint huffmanTreeSize,
            ReadOnlySpan<byte> huffmanTree,
            ReadOnlySpan<byte> huffmanTreeExtraBits,
            ReadOnlySpan<byte> codeLengthBitdepth,
            ReadOnlySpan<ushort> codeLengthBitdepthSymbols,
            BitWriter writer)
        {
            for (nuint i = 0; i < huffmanTreeSize; ++i)
            {
                byte ix = huffmanTree[(int)i];
                writer.Write(codeLengthBitdepth[ix], codeLengthBitdepthSymbols[ix]);

                if (ix > 17)
                {
                    return Status.Failure("Invalid code length symbol");
                }

                if (ix == 16)
                {
                    writer.Write(2, huffmanTreeExtraBits[(int)i]);
                }
                else if (ix == 17)
                {
                    writer.Write(3, huffmanTreeExtraBits[(int)i]);
                }
            }

            return true;
        }

        private static void StoreSimpleHuffmanTree(
            ReadOnlySpan<byte> depths,
            Span<nuint> symbols,
            nuint numSymbols,
            nuint maxBits,
            BitWriter writer)
        {
            // value of 1 indicates a simple Huffman code
            writer.Write(2, 1);
            writer.Write(2, numSymbols - 1); // NSYM - 1

            // Sort
            for (nuint i = 0; i < numSymbols; i++)
            {
                for (nuint j = i + 1; j < numSymbols; j++)
                {
                    if (depths[(int)symbols[(int)j]] < depths[(int)symbols[(int)i]])
                    {
                        (symbols[(int)j], symbols[(int)i]) = (symbols[(int)i], symbols[(int)j]);
                    }
                }
            }

            if (numSymbols == 2)
            {
                writer.Write(maxBits, symbols[0]);
                writer.Write(maxBits, symbols[1]);
            }
            else if (numSymbols == 3)
            {
                writer.Write(maxBits, symbols[0]);
                writer.Write(maxBits, symbols[1]);
                writer.Write(maxBits, symbols[2]);
            }
            else
            {
                writer.Write(maxBits, symbols[0]);
                writer.Write(maxBits, symbols[1]);
                writer.Write(maxBits, symbols[2]);
                writer.Write(maxBits, symbols[3]);
                // tree-select
                writer.Write(1, depths[(int)symbols[0]] == 1 ? 1UL : 0UL);
            }
        }

        private static Status StoreHuffmanTree(
            ReadOnlySpan<byte> depths,
            nuint num,
            BitWriter writer)
        {
            byte[] arena                 = new byte[2 * (int)num];
            Span<byte> huffmanTree       = arena.AsSpan(0, (int)num);
            Span<byte> huffmanExtraBits  = arena.AsSpan((int)num, (int)num);
            nuint huffmanTreeSize        = 0;
            WriteHuffmanTree(depths, num, ref huffmanTreeSize, huffmanTree, huffmanExtraBits);

            // Calculate the statistics of the Huffman tree in the compact representation.
            Span<uint> huffmanTreeHistogram = stackalloc uint[KCodeLengthCodes];
            huffmanTreeHistogram.Clear();

            for (nuint i = 0; i < huffmanTreeSize; ++i)
            {
                ++huffmanTreeHistogram[huffmanTree[(int)i]];
            }

            int numCodes = 0;
            int code     = 0;

            for (int i = 0; i < KCodeLengthCodes; ++i)
            {
                if (huffmanTreeHistogram[i] != 0)
                {
                    if (numCodes == 0)
                    {
                        code     = i;
                        numCodes = 1;
                    }
                    else if (numCodes == 1)
                    {
                        numCodes = 2;
                        break;
                    }
                }
            }

            Span<byte> codeLengthBitdepth          = stackalloc byte[KCodeLengthCodes];
            Span<ushort> codeLengthBitdepthSymbols = stackalloc ushort[KCodeLengthCodes];
            codeLengthBitdepth.Clear();
            codeLengthBitdepthSymbols.Clear();

            CreateHuffmanTree(huffmanTreeHistogram, KCodeLengthCodes, 5, codeLengthBitdepth);
            ConvertBitDepthsToSymbols(codeLengthBitdepth, KCodeLengthCodes, codeLengthBitdepthSymbols);

            StoreHuffmanTreeOfHuffmanTreeToBitMask(numCodes, codeLengthBitdepth, writer);

            if (numCodes == 1)
            {
                codeLengthBitdepth[code] = 0;
            }

            return StoreHuffmanTreeToBitMask(
                huffmanTreeSize,
                huffmanTree,
                huffmanExtraBits,
                codeLengthBitdepth,
                codeLengthBitdepthSymbols,
                writer);
        }
    }
}
