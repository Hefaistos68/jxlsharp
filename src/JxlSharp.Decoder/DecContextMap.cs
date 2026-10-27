// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Context map bitstream decoding and verification.
    /// Ported from <c>lib/jxl/dec_context_map.h</c> and <c>lib/jxl/dec_context_map.cc</c>.
    /// </summary>
    public static class DecContextMap
    {
        private const int KMaxClusters = 256;

        /// <summary>
        /// Reads the context map from <paramref name="input"/>.
        /// On entry, <paramref name="contextMap"/> length must equal the number of possible context IDs.
        /// Sets <paramref name="numHtrees"/> to the number of distinct histogram IDs in <paramref name="contextMap"/>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="contextMap">Span of context map entries to populate.</param>
        /// <param name="numHtrees">Output number of distinct histograms.</param>
        /// <param name="input">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeContextMap(
            JxlMemoryManager? memoryManager,
            Span<byte> contextMap,
            out nuint numHtrees,
            BitReader input)
        {
            numHtrees     = 0;
            bool isSimple = input.ReadFixedBits(1) != 0;

            if (isSimple)
            {
                ReadSimpleContextMap(contextMap, input);
            }
            else if (!ReadAnsContextMap(memoryManager, contextMap, input))
            {
                return false;
            }

            byte maxElement = 0;

            for (int i = 0; i < contextMap.Length; i++)
            {
                if (contextMap[i] > maxElement)
                {
                    maxElement = contextMap[i];
                }
            }

            numHtrees = (nuint)maxElement + 1u;

            return VerifyContextMap(contextMap, numHtrees);
        }

        private static void ReadSimpleContextMap(Span<byte> contextMap, BitReader input)
        {
            int bitsPerEntry = (int)input.ReadFixedBits(2);

            if (bitsPerEntry != 0)
            {
                for (int i = 0; i < contextMap.Length; i++)
                {
                    contextMap[i] = (byte)input.ReadBits((nuint)bitsPerEntry);
                }
            }
            else
            {
                contextMap.Clear();
            }
        }

        private static Status ReadAnsContextMap(
            JxlMemoryManager? memoryManager,
            Span<byte> contextMap,
            BitReader input)
        {
            bool useMtf  = input.ReadFixedBits(1) != 0;
            ANSCode code = new ANSCode();

            if (!DecAns.DecodeHistograms(
                memoryManager,
                input,
                1,
                code,
                out byte[] sinkCtxMap,
                disallowLz77: contextMap.Length <= 2))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(code, input);

            if (!readerOr.IsOk)
            {
                return readerOr.Status;
            }

            ANSSymbolReader reader = readerOr.Value;
            uint maxsym            = 0;

            for (int i = 0; i < contextMap.Length; i++)
            {
                uint sym = reader.ReadHybridUintInlined(usesLz77: true, 0, input, sinkCtxMap);

                if (sym > maxsym)
                {
                    maxsym = sym;
                }

                contextMap[i] = (byte)sym;
            }

            if (maxsym >= KMaxClusters)
            {
                return Status.Failure("Invalid cluster ID");
            }

            if (!reader.CheckANSFinalState())
            {
                return Status.Failure("Invalid context map");
            }

            if (useMtf)
            {
                InverseMtf.InverseMoveToFrontTransform(contextMap, contextMap.Length);
            }

            return true;
        }

        private static Status VerifyContextMap(ReadOnlySpan<byte> contextMap, nuint numHtrees)
        {
            Span<bool> haveHtree = stackalloc bool[(int)numHtrees];
            haveHtree.Clear();
            nuint numFound       = 0;

            for (int i = 0; i < contextMap.Length; i++)
            {
                byte htree = contextMap[i];

                if (htree >= numHtrees)
                {
                    return Status.Failure("Invalid histogram index in context map.");
                }

                if (!haveHtree[htree])
                {
                    haveHtree[htree] = true;
                    ++numFound;
                }
            }

            if (numFound != numHtrees)
            {
                return Status.Failure("Incomplete context map.");
            }

            return true;
        }
    }
}
