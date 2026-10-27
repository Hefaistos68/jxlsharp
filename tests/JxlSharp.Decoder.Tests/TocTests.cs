// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Toc"/>.
    /// Ported from <c>lib/jxl/toc_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class TocTests
    {
        /// <summary>
        /// Verifies round-trip encoding and decoding of TOC sizes and permutations across 1..9 entries.
        /// Ported from <c>TocTest.Test</c>.
        /// </summary>
        [Test]
        public void TocRoundtrip_ShouldPreserveGroupOffsetsAndSizes()
        {
            Rng rng = new Rng(0);

            for (nuint numEntries = 1; numEntries < 10; ++numEntries)
            {
                Roundtrip(numEntries, permute: false, rng);
                Roundtrip(numEntries, permute: true, rng);
            }
        }

        private static void Roundtrip(nuint numEntries, bool permute, Rng rng)
        {
            uint[] permutation    = Array.Empty<uint>();
            uint[] invPermutation = new uint[(int)numEntries];

            for (uint i = 0; i < (uint)numEntries; i++)
            {
                invPermutation[i] = i;
            }

            if (permute)
            {
                permutation = new uint[(int)numEntries];

                for (uint i = 0; i < (uint)numEntries; i++)
                {
                    permutation[i] = i;
                }

                rng.Shuffle(permutation);

                for (uint i = 0; i < (uint)numEntries; i++)
                {
                    invPermutation[permutation[i]] = i;
                }
            }

            nuint[] sizes = GenerateGroupByteSizes((int)numEntries, rng);

            BitWriter writer = new BitWriter();
            AuxOut auxOut    = new AuxOut();

            Toc.WriteTocPermutation(permutation, writer, auxOut).IsOk.ShouldBeTrue();
            Toc.WriteTocSizes(sizes, writer, auxOut).IsOk.ShouldBeTrue();

            using BitReader reader = new BitReader(writer.GetSpan());
            Status readStatus      = Toc.ReadGroupOffsets(
                null,
                numEntries,
                reader,
                out ulong[] groupOffsets,
                out uint[] groupSizes,
                out ulong totalSize);

            readStatus.IsOk.ShouldBeTrue();
            groupOffsets.Length.ShouldBe((int)numEntries);
            groupSizes.Length.ShouldBe((int)numEntries);
            reader.Close().IsOk.ShouldBeTrue();

            VerifyOffsets(numEntries, sizes, invPermutation, groupOffsets, groupSizes, totalSize);
        }

        private static nuint[] GenerateGroupByteSizes(int numEntries, Rng rng)
        {
            nuint[] sizes = new nuint[numEntries];

            for (int idx = 0; idx < numEntries; ++idx)
            {
                BitWriter groupWriter = new BitWriter();
                nuint maxBits         = (nuint)(rng.NextUInt64() & 0xFFFu);
                AuxOut auxOut         = new AuxOut();

                Status ok = groupWriter.WithMaxBits(
                    maxBits + Common.BitsPerByte,
                    LayerType.Header,
                    auxOut,
                    () =>
                    {
                        nuint i = 0;

                        for (; i + BitWriter.KMaxBitsPerCall < maxBits; i += BitWriter.KMaxBitsPerCall)
                        {
                            groupWriter.Write(BitWriter.KMaxBitsPerCall, 0);
                        }

                        for (; i < maxBits; i += 1)
                        {
                            groupWriter.Write(1, 0);
                        }

                        groupWriter.ZeroPadToByte();

                        return true;
                    });

                ok.IsOk.ShouldBeTrue();
                sizes[idx] = groupWriter.BitsWritten / Common.BitsPerByte;
            }

            return sizes;
        }

        private static void VerifyOffsets(
            nuint numEntries,
            nuint[] sizes,
            uint[] invPermutation,
            ulong[] groupOffsets,
            uint[] groupSizes,
            ulong totalSize)
        {
            ulong prefixSum = 0;

            for (int i = 0; i < (int)numEntries; ++i)
            {
                int invIdx = (int)invPermutation[i];
                groupOffsets[invIdx].ShouldBe(prefixSum);
                prefixSum += sizes[i];

                if (i + 1 < (int)numEntries)
                {
                    int nextInvIdx = (int)invPermutation[i + 1];
                    (groupOffsets[invIdx] + groupSizes[invIdx]).ShouldBe(groupOffsets[nextInvIdx]);
                }
            }

            totalSize.ShouldBe(prefixSum);
        }
    }
}
