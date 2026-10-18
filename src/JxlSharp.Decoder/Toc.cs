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
    /// Table of Contents (TOC) and Lehmer permutation reading and writing.
    /// Ported from <c>lib/jxl/toc.h</c>, <c>lib/jxl/toc.cc</c>, <c>lib/jxl/enc_toc.h</c>,
    /// <c>lib/jxl/enc_toc.cc</c>, and <c>lib/jxl/coeff_order.cc</c>.
    /// </summary>
    public static class Toc
    {
        /// <summary>Number of contexts used for Lehmer permutation coding (8).</summary>
        public const uint KPermutationContexts = 8;

        /// <summary>
        /// U32 distribution for TOC group byte sizes.
        /// (2+bits) = 2,3,4 bytes so encoders can patch TOC after encoding.
        /// 30 is sufficient for 4K channels of uncompressed 16-bit samples.
        /// </summary>
        public static readonly U32Enc KTocDist = new U32Enc(
            U32Distr.Bits(10),
            U32Distr.BitsOffset(14, 1024),
            U32Distr.BitsOffset(22, 17408),
            U32Distr.BitsOffset(30, 4211712));

        /// <summary>
        /// Returns the maximum number of bits required to encode <paramref name="numSizes"/> TOC entries
        /// (excluding permutation tokens).
        /// </summary>
        /// <param name="numSizes">Number of TOC size entries.</param>
        /// <returns>Maximum bit count.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint MaxBits(nuint numSizes)
        {
            nuint entryBits = U32Coder.MaxEncodedBits(KTocDist) * numSizes;

            // permutation bit (not its tokens!), padding, entries, padding.
            return 1u + Common.BitsPerByte + entryBits + Common.BitsPerByte;
        }

        /// <summary>
        /// Computes the TOC entry index for AC group <paramref name="group"/> in <paramref name="pass"/>.
        /// </summary>
        /// <param name="pass">Pass index.</param>
        /// <param name="group">AC group index.</param>
        /// <param name="numGroups">Total number of AC groups.</param>
        /// <param name="numDcGroups">Total number of DC groups.</param>
        /// <returns>TOC entry index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint AcGroupIndex(nuint pass, nuint group, nuint numGroups, nuint numDcGroups)
        {
            return 2u + numDcGroups + (pass * numGroups) + group;
        }

        /// <summary>
        /// Computes the total number of TOC entries for a frame.
        /// </summary>
        /// <param name="numGroups">Number of AC groups.</param>
        /// <param name="numDcGroups">Number of DC groups.</param>
        /// <param name="numPasses">Number of passes.</param>
        /// <returns>Total number of TOC entries.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint NumTocEntries(nuint numGroups, nuint numDcGroups, nuint numPasses)
        {
            if (numGroups == 1 && numPasses == 1)
            {
                return 1;
            }

            return AcGroupIndex(0, 0, numGroups, numDcGroups) + (numGroups * numPasses);
        }

        /// <summary>
        /// Computes the context index in <c>[0, KPermutationContexts)</c> for a Lehmer code value.
        /// </summary>
        /// <param name="val">Previous Lehmer value or permutation size.</param>
        /// <returns>Context index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CoeffOrderContext(uint val)
        {
            new HybridUintConfig(0, 0, 0).Encode(val, out uint token, out _, out _);

            return Math.Min(token, KPermutationContexts - 1u);
        }

        /// <summary>
        /// Decodes a permutation of <paramref name="size"/> elements from <paramref name="br"/>.
        /// Ported from <c>jxl::DecodePermutation</c> in <c>lib/jxl/coeff_order.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="skip">Number of leading elements fixed to identity.</param>
        /// <param name="size">Total permutation size.</param>
        /// <param name="order">Output permutation span (or empty if only skipping the bitstream).</param>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodePermutation(
            JxlMemoryManager? memoryManager,
            nuint skip,
            nuint size,
            Span<uint> order,
            BitReader br)
        {
            ANSCode code = new ANSCode();

            if (!DecAns.DecodeHistograms(memoryManager, br, KPermutationContexts, code, out byte[] contextMap))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(code, br);

            if (!readerOr.IsOk)
            {
                return readerOr.Status;
            }

            ANSSymbolReader reader = readerOr.Value;

            if (!ReadPermutation(skip, size, order, br, reader, contextMap))
            {
                return false;
            }

            if (!reader.CheckANSFinalState())
            {
                return Status.Failure("Invalid ANS stream");
            }

            return true;
        }

        /// <summary>
        /// Encodes a permutation of <paramref name="size"/> elements into <paramref name="writer"/>.
        /// Ported from <c>jxl::EncodePermutation</c> in <c>lib/jxl/enc_coeff_order.cc</c>.
        /// </summary>
        /// <param name="order">Input permutation of length <paramref name="size"/>.</param>
        /// <param name="skip">Number of leading elements to skip.</param>
        /// <param name="size">Total permutation size.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="layer">Statistics layer type.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodePermutation(
            ReadOnlySpan<uint> order,
            nuint skip,
            nuint size,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            uint[] lehmer = new uint[(int)size];
            uint[] temp   = new uint[(int)size + 1];

            if (!LehmerCode.ComputeLehmerCode(order, temp, size, lehmer))
            {
                return false;
            }

            nuint end = size;

            while (end > skip && lehmer[(int)(end - 1)] == 0)
            {
                --end;
            }

            int numValues = (int)(end - skip) + 1;
            uint[] values = new uint[numValues];
            values[0]     = (uint)(end - skip);

            for (nuint i = skip; i < end; ++i)
            {
                values[(int)(i - skip) + 1] = lehmer[(int)i];
            }

            return WritePermutationValues(values, writer, layer, auxOut);
        }

        /// <summary>
        /// Reads the TOC sizes and optional permutation from <paramref name="reader"/>.
        /// Ported from <c>jxl::ReadToc</c> in <c>lib/jxl/toc.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="tocEntries">Number of TOC entries.</param>
        /// <param name="reader">Source <see cref="BitReader"/>.</param>
        /// <param name="sizes">Output array of group byte sizes.</param>
        /// <param name="permutation">Output array of permuted indices (empty if no permutation).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadToc(
            JxlMemoryManager? memoryManager,
            nuint tocEntries,
            BitReader reader,
            out uint[] sizes,
            out uint[] permutation)
        {
            sizes       = Array.Empty<uint>();
            permutation = Array.Empty<uint>();

            if (tocEntries > 65536)
            {
                return Status.Failure("too many toc entries");
            }

            if (tocEntries == 0)
            {
                return Status.Failure("zero toc entries");
            }

            sizes = new uint[(int)tocEntries];

            if (reader.TotalBitsConsumed() >= reader.TotalBytes() * Common.BitsPerByte)
            {
                return Status.NotEnoughBytes("Not enough bytes for TOC");
            }

            if (reader.ReadFixedBits(1) == 1)
            {
                if (!CheckBitBudget(reader, tocEntries))
                {
                    return Status.NotEnoughBytes("Not enough bytes for TOC");
                }

                permutation = new uint[(int)tocEntries];

                if (!DecodePermutation(memoryManager, 0, tocEntries, permutation, reader))
                {
                    return false;
                }
            }

            if (!reader.JumpToByteBoundary())
            {
                return false;
            }

            if (!CheckBitBudget(reader, tocEntries))
            {
                return Status.NotEnoughBytes("Not enough bytes for TOC");
            }

            for (int i = 0; i < (int)tocEntries; ++i)
            {
                sizes[i] = U32Coder.Read(KTocDist, reader);
            }

            if (!reader.JumpToByteBoundary())
            {
                return false;
            }

            return CheckBitBudget(reader, 0);
        }

        /// <summary>
        /// Reads the TOC group offsets, sizes, and total byte size from <paramref name="reader"/>,
        /// applying any TOC permutation.
        /// Ported from <c>jxl::ReadGroupOffsets</c> in <c>lib/jxl/toc.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="tocEntries">Number of TOC entries.</param>
        /// <param name="reader">Source <see cref="BitReader"/>.</param>
        /// <param name="offsets">Output byte offsets per group.</param>
        /// <param name="sizes">Output byte sizes per group.</param>
        /// <param name="totalSize">Output sum of all group byte sizes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadGroupOffsets(
            JxlMemoryManager? memoryManager,
            nuint tocEntries,
            BitReader reader,
            out ulong[] offsets,
            out uint[] sizes,
            out ulong totalSize)
        {
            offsets   = Array.Empty<ulong>();
            totalSize = 0;

            Status status = ReadToc(memoryManager, tocEntries, reader, out sizes, out uint[] permutation);

            if (!status)
            {
                return status;
            }

            offsets      = new ulong[(int)tocEntries];
            ulong offset = 0;

            for (int i = 0; i < (int)tocEntries; ++i)
            {
                if (offset + sizes[i] < offset)
                {
                    return Status.Failure("group offset overflow");
                }

                offsets[i] = offset;
                offset    += sizes[i];
            }

            totalSize = offset;

            if (permutation.Length > 0)
            {
                ulong[] permutedOffsets = new ulong[(int)tocEntries];
                uint[] permutedSizes    = new uint[(int)tocEntries];

                for (int i = 0; i < permutation.Length; i++)
                {
                    uint index         = permutation[i];
                    permutedOffsets[i] = offsets[(int)index];
                    permutedSizes[i]   = sizes[(int)index];
                }

                offsets = permutedOffsets;
                sizes   = permutedSizes;
            }

            return true;
        }

        /// <summary>
        /// Writes the TOC permutation header (permutation flag + optional encoded permutation + byte padding).
        /// Ported from <c>jxl::WriteTocPermutation</c> in <c>lib/jxl/enc_toc.cc</c>.
        /// </summary>
        /// <param name="permutation">Permutation indices (empty if no permutation).</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteTocPermutation(
            ReadOnlySpan<uint> permutation,
            BitWriter writer,
            AuxOut? auxOut)
        {
            if (!permutation.IsEmpty)
            {
                uint[] permCopy = permutation.ToArray();

                return writer.WithMaxBits(
                    MaxBits(0),
                    LayerType.Toc,
                    auxOut,
                    () =>
                    {
                        writer.Write(1, 1);

                        if (!EncodePermutation(permCopy, 0, (nuint)permCopy.Length, writer, LayerType.Header, auxOut))
                        {
                            return false;
                        }

                        writer.ZeroPadToByte();

                        return true;
                    });
            }

            return writer.WithMaxBits(
                MaxBits(0),
                LayerType.Toc,
                auxOut,
                () =>
                {
                    writer.Write(1, 0);
                    writer.ZeroPadToByte();

                    return true;
                });
        }

        /// <summary>
        /// Writes the TOC group byte sizes and pads to the next byte boundary.
        /// Ported from <c>jxl::WriteTocSizes</c> in <c>lib/jxl/enc_toc.cc</c>.
        /// </summary>
        /// <param name="groupSizes">Group byte sizes.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteTocSizes(
            ReadOnlySpan<nuint> groupSizes,
            BitWriter writer,
            AuxOut? auxOut)
        {
            nuint[] sizesCopy = groupSizes.ToArray();

            return writer.WithMaxBits(
                MaxBits((nuint)sizesCopy.Length),
                LayerType.Toc,
                auxOut,
                () =>
                {
                    for (int i = 0; i < sizesCopy.Length; i++)
                    {
                        if (!U32Coder.Write(KTocDist, (uint)sizesCopy[i], writer))
                        {
                            return false;
                        }
                    }

                    writer.ZeroPadToByte();

                    return true;
                });
        }

        private static Status CheckBitBudget(BitReader reader, nuint numEntries)
        {
            nuint minimalBitCost = numEntries * (2u + 10u);
            nuint bitBudget      = reader.TotalBytes() * 8u;
            nuint expenses       = reader.TotalBitsConsumed();

            if (expenses <= bitBudget && minimalBitCost <= bitBudget - expenses)
            {
                return true;
            }

            return Status.NotEnoughBytes("Not enough bytes for TOC");
        }

        private static Status ReadPermutation(
            nuint skip,
            nuint size,
            Span<uint> order,
            BitReader br,
            ANSSymbolReader reader,
            ReadOnlySpan<byte> contextMap)
        {
            uint[] lehmer = new uint[(int)size];
            uint[] temp   = new uint[(int)size * 2];
            uint end      = reader.ReadHybridUint(CoeffOrderContext((uint)size), br, contextMap) + (uint)skip;

            if (end > (uint)size)
            {
                return Status.Failure("Invalid permutation size");
            }

            uint last = 0;

            for (nuint i = skip; i < end; ++i)
            {
                lehmer[(int)i] = reader.ReadHybridUint(CoeffOrderContext(last), br, contextMap);
                last           = lehmer[(int)i];

                if (lehmer[(int)i] >= (uint)(size - i))
                {
                    return Status.Failure("Invalid lehmer code");
                }
            }

            if (order.IsEmpty)
            {
                return true;
            }

            return LehmerCode.DecodeLehmerCode(lehmer, temp, size, order);
        }

        private static Status WritePermutationValues(
            uint[] values,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            HybridUintConfig cfg = new HybridUintConfig(0, 0, 0);
            uint[] tokens        = new uint[values.Length];
            uint[] nbitsArr      = new uint[values.Length];
            uint[] bitsArr       = new uint[values.Length];
            uint maxToken        = 0;

            for (int i = 0; i < values.Length; i++)
            {
                cfg.Encode(values[i], out tokens[i], out nbitsArr[i], out bitsArr[i]);

                if (tokens[i] > maxToken)
                {
                    maxToken = tokens[i];
                }
            }

            int alphabetSize = (int)maxToken + 1;
            uint[] histo     = new uint[alphabetSize];

            for (int i = 0; i < tokens.Length; i++)
            {
                histo[tokens[i]]++;
            }

            nuint maxOutBits = 512u + ((nuint)alphabetSize * 24u) + ((nuint)values.Length * 48u);

            return writer.WithMaxBits(
                maxOutBits,
                layer,
                auxOut,
                () =>
                {
                    writer.Write(1, 0); // LZ77 disabled
                    writer.Write(1, 1); // Simple context map
                    writer.Write(2, 0); // 0 bits per entry (1 histogram)
                    writer.Write(1, 1); // use_prefix_code = true
                    DecAns.EncodeUintConfig(in cfg, writer, AnsParams.PrefixMaxBits);
                    DecAns.StoreVarLenUint16((nuint)(alphabetSize - 1), writer);

                    byte[] depths   = new byte[alphabetSize];
                    ushort[] cwBits = new ushort[alphabetSize];

                    if (alphabetSize > 1 &&
                        !HuffmanTable.BuildAndStoreHuffmanTree(histo, alphabetSize, depths, cwBits, writer))
                    {
                        return false;
                    }

                    for (int i = 0; i < tokens.Length; i++)
                    {
                        uint tok = tokens[i];

                        if (depths[tok] > 0)
                        {
                            writer.Write(depths[tok], cwBits[tok]);
                        }

                        if (nbitsArr[i] > 0)
                        {
                            writer.Write(nbitsArr[i], bitsArr[i]);
                        }
                    }

                    return true;
                });
        }
    }
}
