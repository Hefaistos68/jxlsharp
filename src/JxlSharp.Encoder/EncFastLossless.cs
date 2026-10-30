// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using JxlSharp.Base;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Parallel runner callback signature for fast lossless encoding.
    /// Must invoke <paramref name="fun"/> for all indices from <c>0</c> to <paramref name="count"/> - 1.
    /// </summary>
    public delegate void FJxlParallelRunner(object? runnerOpaque, object? opaque, Action<object?, nuint> fun, nuint count);

    /// <summary>
    /// Delegate for retrieving a tile of interleaved color channel data at <c>(xpos, ypos)</c> of size <c>(xsize, ysize)</c>.
    /// </summary>
    public delegate ReadOnlyMemory<byte> GetColorChannelsDataFunc(
        object? opaque,
        nuint xpos,
        nuint ypos,
        nuint xsize,
        nuint ysize,
        out nuint rowOffset);

    /// <summary>
    /// Delegate for releasing a buffer previously obtained from <see cref="GetColorChannelsDataFunc"/>.
    /// </summary>
    public delegate void ReleaseCurrentDataFunc(object? opaque, ReadOnlyMemory<byte> buffer);

    /// <summary>
    /// Streaming input source for chunked frame encoding.
    /// Ported from <c>JxlChunkedFrameInputSource</c> in <c>jxl/encode.h</c> and <c>lib/jxl/enc_fast_lossless.h</c>.
    /// </summary>
    public struct JxlChunkedFrameInputSource
    {
        /// <summary>Opaque state passed to callbacks.</summary>
        public object? Opaque;

        /// <summary>Callback to retrieve color channel pixel data for a given rectangle.</summary>
        public GetColorChannelsDataFunc? GetColorChannelDataAt;

        /// <summary>Callback to release a buffer acquired via <see cref="GetColorChannelDataAt"/>.</summary>
        public ReleaseCurrentDataFunc? ReleaseBuffer;
    }

    /// <summary>
    /// Interface for streaming output processors used by <see cref="EncFastLossless"/>.
    /// Ported from <c>JxlEncoderOutputProcessorWrapper</c> usage in <c>lib/jxl/enc_fast_lossless.cc</c>.
    /// </summary>
    public interface IJxlFastLosslessOutputProcessor
    {
        /// <summary>Gets the current byte position in the output stream.</summary>
        nuint CurrentPosition { get; }

        /// <summary>Seeks to <paramref name="position"/> in the output stream.</summary>
        Status Seek(nuint position);

        /// <summary>Appends <paramref name="data"/> at the current position and advances the position.</summary>
        Status AppendBytes(ReadOnlySpan<byte> data);
    }

    /// <summary>
    /// Internal bit writer used by <see cref="EncFastLossless"/>.
    /// Ported from <c>BitWriter</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
    /// </summary>
    internal sealed class FastLosslessBitWriter
    {
        public byte[]? Data;
        public nuint BytesWritten;
        public nuint BitsInBuffer;
        public ulong Buffer;

        public bool Allocate(nuint maximumBitSize)
        {
            Data         = new byte[(maximumBitSize / 8u) + 64u];
            BytesWritten = 0;
            BitsInBuffer = 0;
            Buffer       = 0;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint AddBits(
            uint count,
            ulong bits,
            Span<byte> dataBuf,
            ref nuint bitsInBuffer,
            ref ulong bitBuffer)
        {
            bitBuffer    |= bits << (int)bitsInBuffer;
            bitsInBuffer += count;
            BinaryPrimitives.WriteUInt64LittleEndian(dataBuf, bitBuffer);

            nuint bytesInBuffer = bitsInBuffer / 8u;
            bitsInBuffer       -= bytesInBuffer * 8u;
            int shiftBits       = (int)(bytesInBuffer * 8u);
            bitBuffer           = shiftBits >= 64 ? 0UL : (bitBuffer >> shiftBits);

            return bytesInBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(uint count, ulong bits)
        {
            BytesWritten += AddBits(
                count,
                bits,
                Data.AsSpan((int)BytesWritten),
                ref BitsInBuffer,
                ref Buffer);
        }

        public void ZeroPadToByte()
        {
            if (BitsInBuffer != 0)
            {
                Write((uint)(8u - BitsInBuffer), 0);
            }
        }
    }

    /// <summary>
    /// Canonical Huffman + LZ77 prefix code used by <see cref="EncFastLossless"/>.
    /// Ported from <c>PrefixCode</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
    /// </summary>
    internal sealed class FastLosslessPrefixCode
    {
        public const int KNumRawSymbols = 19;
        public const int KNumLZ77       = 33;
        public const int KLZ77CacheSize = 32;
        public const int KLZ77Offset    = 224;
        public const int KLZ77MinLength = 7;
        private const int KMaxNumSymbols = KNumLZ77;

        public readonly byte[] RawNbits       = new byte[KNumRawSymbols];
        public readonly byte[] RawBits        = new byte[KNumRawSymbols];
        public readonly byte[] Lz77Nbits      = new byte[KNumLZ77];
        public readonly ushort[] Lz77Bits     = new ushort[KNumLZ77];
        public readonly ulong[] Lz77CacheBits = new ulong[KLZ77CacheSize];
        public readonly byte[] Lz77CacheNbits = new byte[KLZ77CacheSize];
        public nuint NumRaw;

        public FastLosslessPrefixCode()
        {
        }

        public FastLosslessPrefixCode(
            ReadOnlySpan<byte> minRawLength,
            ReadOnlySpan<byte> maxRawLength,
            ReadOnlySpan<ulong> rawCounts,
            ReadOnlySpan<ulong> lz77Counts)
        {
            Span<ulong> level1Counts = stackalloc ulong[KNumRawSymbols + 1];
            rawCounts.Slice(0, KNumRawSymbols).CopyTo(level1Counts);
            NumRaw = KNumRawSymbols;

            while (NumRaw > 0 && level1Counts[(int)NumRaw - 1] == 0)
            {
                NumRaw--;
            }

            level1Counts[(int)NumRaw] = 0;

            for (int i = 0; i < KNumLZ77; i++)
            {
                level1Counts[(int)NumRaw] += lz77Counts[i];
            }

            Span<byte> level1Nbits = stackalloc byte[KNumRawSymbols + 1];
            ComputeCodeLengths(level1Counts, (int)NumRaw + 1, minRawLength, maxRawLength, level1Nbits);

            Span<byte> level2Nbits = stackalloc byte[KNumLZ77];
            Span<byte> minLengths  = stackalloc byte[KNumLZ77];
            Span<byte> maxLengths  = stackalloc byte[KNumLZ77];
            byte maxL              = (byte)(15 - level1Nbits[(int)NumRaw]);
            maxLengths.Fill(maxL);

            int numLz77 = KNumLZ77;

            while (numLz77 > 0 && lz77Counts[numLz77 - 1] == 0)
            {
                numLz77--;
            }

            ComputeCodeLengths(lz77Counts, numLz77, minLengths, maxLengths, level2Nbits);

            for (int i = 0; i < (int)NumRaw; i++)
            {
                RawNbits[i] = level1Nbits[i];
            }

            for (int i = 0; i < numLz77; i++)
            {
                Lz77Nbits[i] = level2Nbits[i] != 0
                    ? (byte)(level1Nbits[(int)NumRaw] + level2Nbits[i])
                    : (byte)0;
            }

            ComputeCanonicalCode(RawNbits.AsSpan(0, (int)NumRaw), RawBits.AsSpan(0, (int)NumRaw), Lz77Nbits, Lz77Bits);
            BuildLz77Cache();
        }

        private void BuildLz77Cache()
        {
            for (uint count = 0; count < KLZ77CacheSize; count++)
            {
                EncFastLossless.EncodeHybridUintLZ77(count, out uint token, out uint nbits, out uint bits);
                Lz77CacheNbits[count] = (byte)(Lz77Nbits[token] + nbits + RawNbits[0]);
                Lz77CacheBits[count]  = ((((ulong)bits << Lz77Nbits[token]) | Lz77Bits[token]) << RawNbits[0]) | RawBits[0];
            }
        }

        public static ushort BitReverse(int nbits, ushort bits)
        {
            if (nbits <= 0)
            {
                return 0;
            }

            ReadOnlySpan<ushort> kNibbleLookup = stackalloc ushort[16]
            {
                0b0000, 0b1000, 0b0100, 0b1100, 0b0010, 0b1010, 0b0110, 0b1110,
                0b0001, 0b1001, 0b0101, 0b1101, 0b0011, 0b1011, 0b0111, 0b1111,
            };

            ushort rev16 = (ushort)((kNibbleLookup[bits & 0xF] << 12) |
                                    (kNibbleLookup[(bits >> 4) & 0xF] << 8) |
                                    (kNibbleLookup[(bits >> 8) & 0xF] << 4) |
                                    kNibbleLookup[bits >> 12]);

            return (ushort)(rev16 >> (16 - nbits));
        }

        public static void ComputeCanonicalCode(
            ReadOnlySpan<byte> firstChunkNbits,
            Span<byte> firstChunkBits,
            ReadOnlySpan<byte> secondChunkNbits,
            Span<ushort> secondChunkBits)
        {
            const int kMaxCodeLength = 15;
            Span<byte> codeLengthCounts = stackalloc byte[kMaxCodeLength + 1];

            for (int i = 0; i < firstChunkNbits.Length; i++)
            {
                codeLengthCounts[firstChunkNbits[i]]++;
            }

            for (int i = 0; i < secondChunkNbits.Length; i++)
            {
                codeLengthCounts[secondChunkNbits[i]]++;
            }

            Span<ushort> nextCode = stackalloc ushort[kMaxCodeLength + 1];
            ushort code           = 0;

            for (int i = 1; i <= kMaxCodeLength; i++)
            {
                code        = (ushort)((code + codeLengthCounts[i - 1]) << 1);
                nextCode[i] = code;
            }

            for (int i = 0; i < firstChunkNbits.Length; i++)
            {
                byte len          = firstChunkNbits[i];
                firstChunkBits[i] = (byte)BitReverse(len, nextCode[len]++);
            }

            for (int i = 0; i < secondChunkNbits.Length; i++)
            {
                byte len           = secondChunkNbits[i];
                secondChunkBits[i] = BitReverse(len, nextCode[len]++);
            }
        }

        private static void ComputeCodeLengthsNonZeroImpl(
            ReadOnlySpan<ulong> freqs,
            int n,
            int precision,
            ulong infty,
            ReadOnlySpan<byte> minLimit,
            ReadOnlySpan<byte> maxLimit,
            Span<byte> nbits)
        {
            int stride  = (1 << precision) + 1;
            ulong[] dynp = new ulong[stride * (n + 1)];
            Array.Fill(dynp, infty);
            dynp[0] = 0;

            for (int sym = 0; sym < n; sym++)
            {
                for (uint bits = minLimit[sym]; bits <= maxLimit[sym]; bits++)
                {
                    int offDelta = 1 << (precision - (int)bits);

                    for (int off = 0; off + offDelta <= (1 << precision); off++)
                    {
                        ulong candidate = dynp[(sym * stride) + off] + (freqs[sym] * bits);
                        int dstIdx      = ((sym + 1) * stride) + off + offDelta;

                        if (candidate < dynp[dstIdx])
                        {
                            dynp[dstIdx] = candidate;
                        }
                    }
                }
            }

            int curSym = n;
            int curOff = 1 << precision;

            while (curSym-- > 0)
            {
                for (uint bits = minLimit[curSym]; bits <= maxLimit[curSym]; bits++)
                {
                    int offDelta = 1 << (precision - (int)bits);

                    if (offDelta <= curOff &&
                        dynp[((curSym + 1) * stride) + curOff] == dynp[(curSym * stride) + curOff - offDelta] + (freqs[curSym] * bits))
                    {
                        curOff       -= offDelta;
                        nbits[curSym] = (byte)bits;
                        break;
                    }
                }
            }
        }

        private static void ComputeCodeLengthsNonZero(
            ReadOnlySpan<ulong> freqs,
            int n,
            Span<byte> minLimit,
            ReadOnlySpan<byte> maxLimit,
            Span<byte> nbits)
        {
            if (n == 0)
            {
                return;
            }

            int precision      = 0;
            int shortestLength = 255;
            ulong freqsum      = 0;

            for (int i = 0; i < n; i++)
            {
                freqsum += freqs[i];

                if (minLimit[i] < 1)
                {
                    minLimit[i] = 1;
                }

                precision      = Math.Max((int)maxLimit[i], precision);
                shortestLength = Math.Min((int)minLimit[i], shortestLength);
            }

            precision   -= shortestLength - 1;
            ulong infty  = freqsum * (ulong)precision;
            ComputeCodeLengthsNonZeroImpl(freqs, n, precision, infty, minLimit, maxLimit, nbits);
        }

        public static void ComputeCodeLengths(
            ReadOnlySpan<ulong> freqs,
            int n,
            ReadOnlySpan<byte> minLimitIn,
            ReadOnlySpan<byte> maxLimitIn,
            Span<byte> nbits)
        {
            Span<ulong> compactFreqs = stackalloc ulong[KMaxNumSymbols];
            Span<byte> minLimit      = stackalloc byte[KMaxNumSymbols];
            Span<byte> maxLimit      = stackalloc byte[KMaxNumSymbols];
            int ni                   = 0;

            for (int i = 0; i < n; i++)
            {
                if (freqs[i] != 0)
                {
                    compactFreqs[ni] = freqs[i];
                    minLimit[ni]     = i < minLimitIn.Length ? minLimitIn[i] : (byte)0;
                    maxLimit[ni]     = i < maxLimitIn.Length ? maxLimitIn[i] : (byte)0;
                    ni++;
                }
            }

            Span<byte> numBits = stackalloc byte[KMaxNumSymbols];
            ComputeCodeLengthsNonZero(compactFreqs, ni, minLimit, maxLimit, numBits);

            ni = 0;

            for (int i = 0; i < n; i++)
            {
                nbits[i] = freqs[i] != 0 ? numBits[ni++] : (byte)0;
            }
        }

        public void WriteTo(FastLosslessBitWriter writer)
        {
            Span<ulong> codeLengthCounts = stackalloc ulong[18];
            codeLengthCounts[17]         = 3 + (2 * (KNumLZ77 - 1));

            for (int i = 0; i < KNumRawSymbols; i++)
            {
                codeLengthCounts[RawNbits[i]]++;
            }

            for (int i = 0; i < KNumLZ77; i++)
            {
                codeLengthCounts[Lz77Nbits[i]]++;
            }

            Span<byte> codeLengthNbits    = stackalloc byte[18];
            Span<byte> codeLengthNbitsMin = stackalloc byte[18];
            Span<byte> codeLengthNbitsMax = stackalloc byte[18];
            codeLengthNbitsMax.Fill(5);

            ComputeCodeLengths(codeLengthCounts, 18, codeLengthNbitsMin, codeLengthNbitsMax, codeLengthNbits);
            writer.Write(2, 0b00);

            ReadOnlySpan<byte> codeLengthOrder = stackalloc byte[18]
            {
                1, 2, 3, 4, 0, 5, 17, 6, 16,
                7, 8, 9, 10, 11, 12, 13, 14, 15,
            };

            ReadOnlySpan<byte> codeLengthLengthNbits = stackalloc byte[6] { 2, 4, 3, 2, 2, 4 };
            ReadOnlySpan<byte> codeLengthLengthBits  = stackalloc byte[6] { 0, 7, 3, 2, 1, 15 };

            int numCodeLengths = 18;

            while (numCodeLengths > 0 && codeLengthNbits[codeLengthOrder[numCodeLengths - 1]] == 0)
            {
                numCodeLengths--;
            }

            for (int i = 0; i < numCodeLengths; i++)
            {
                int symbol = codeLengthNbits[codeLengthOrder[i]];
                writer.Write(codeLengthLengthNbits[symbol], codeLengthLengthBits[symbol]);
            }

            Span<ushort> codeLengthBits = stackalloc ushort[18];
            ComputeCanonicalCode(ReadOnlySpan<byte>.Empty, Span<byte>.Empty, codeLengthNbits, codeLengthBits);

            for (int i = 0; i < KNumRawSymbols; i++)
            {
                byte rawNbit = RawNbits[i];
                writer.Write(codeLengthNbits[rawNbit], codeLengthBits[rawNbit]);
            }

            int numLz77 = KNumLZ77;

            while (numLz77 > 0 && Lz77Nbits[numLz77 - 1] == 0)
            {
                numLz77--;
            }

            writer.Write(codeLengthNbits[17], codeLengthBits[17]);
            writer.Write(3, 0b010);
            writer.Write(codeLengthNbits[17], codeLengthBits[17]);
            writer.Write(3, 0b000);
            writer.Write(codeLengthNbits[17], codeLengthBits[17]);
            writer.Write(3, 0b010);

            for (int i = 0; i < numLz77; i++)
            {
                byte lzNbit = Lz77Nbits[i];
                writer.Write(codeLengthNbits[lzNbit], codeLengthBits[lzNbit]);
            }
        }
    }

    /// <summary>
    /// Intermediate frame state for the fast lossless encoder.
    /// Ported from <c>JxlFastLosslessFrameState</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
    /// </summary>
    public sealed class JxlFastLosslessFrameState : IDisposable
    {
        internal JxlChunkedFrameInputSource Input;
        internal nuint Width;
        internal nuint Height;
        internal nuint NumGroupsX;
        internal nuint NumGroupsY;
        internal nuint NumDcGroupsX;
        internal nuint NumDcGroupsY;
        internal nuint NbChans;
        internal nuint Bitdepth;
        internal bool BigEndian;
        internal int Effort;
        internal bool Collided;
        internal readonly FastLosslessPrefixCode[] Hcode = new FastLosslessPrefixCode[4]
        {
            new FastLosslessPrefixCode(),
            new FastLosslessPrefixCode(),
            new FastLosslessPrefixCode(),
            new FastLosslessPrefixCode(),
        };

        internal short[] Lookup = Array.Empty<short>();
        internal readonly FastLosslessBitWriter Header = new FastLosslessBitWriter();
        internal FastLosslessBitWriter[][] GroupData = Array.Empty<FastLosslessBitWriter[]>();
        internal nuint[] GroupSizes = Array.Empty<nuint>();
        internal nuint AcGroupDataOffset;
        internal nuint MinDcGlobalSize;
        internal nuint CurrentBitWriter;
        internal nuint BitWriterBytePos;
        internal nuint BitsInBuffer;
        internal ulong BitBuffer;
        internal bool ProcessDone;

        internal static FastLosslessBitWriter[][] AllocateGroupData(int numGroups)
        {
            FastLosslessBitWriter[][] result = new FastLosslessBitWriter[numGroups][];

            for (int g = 0; g < numGroups; g++)
            {
                result[g] = new FastLosslessBitWriter[4]
                {
                    new FastLosslessBitWriter(),
                    new FastLosslessBitWriter(),
                    new FastLosslessBitWriter(),
                    new FastLosslessBitWriter(),
                };
            }

            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GroupData  = Array.Empty<FastLosslessBitWriter[]>();
            GroupSizes = Array.Empty<nuint>();
            Lookup     = Array.Empty<short>();
        }
    }

    /// <summary>
    /// Parameter bundle for <see cref="EncFastLossless.JxlFastLosslessPrepareFrame(in FastLosslessPrepareArgs)"/>.
    /// </summary>
    public readonly struct FastLosslessPrepareArgs
    {
        /// <summary>Chunked frame input source.</summary>
        public readonly JxlChunkedFrameInputSource Input;

        /// <summary>Frame width in pixels.</summary>
        public readonly nuint Width;

        /// <summary>Frame height in pixels.</summary>
        public readonly nuint Height;

        /// <summary>Number of channels (1..4).</summary>
        public readonly nuint NbChans;

        /// <summary>Bits per sample (1..16).</summary>
        public readonly nuint Bitdepth;

        /// <summary>Whether 16-bit samples are big-endian.</summary>
        public readonly bool BigEndian;

        /// <summary>Encoding effort.</summary>
        public readonly int Effort;

        /// <summary>Whether input is provided in one shot.</summary>
        public readonly bool OneShot;

        /// <summary>Initializes a new instance of the <see cref="FastLosslessPrepareArgs"/> struct.</summary>
        public FastLosslessPrepareArgs(
            JxlChunkedFrameInputSource input,
            nuint width,
            nuint height,
            nuint nbChans,
            nuint bitdepth,
            bool bigEndian,
            int effort,
            bool oneShot = true)
        {
            Input     = input;
            Width     = width;
            Height    = height;
            NbChans   = nbChans;
            Bitdepth  = bitdepth;
            BigEndian = bigEndian;
            Effort    = effort;
            OneShot   = oneShot;
        }
    }

    /// <summary>
    /// Fast lossless Modular encoder (<c>fjxl</c>).
    /// Ported from <c>lib/jxl/enc_fast_lossless.h</c> and <c>lib/jxl/enc_fast_lossless.cc</c>.
    /// </summary>
    public static class EncFastLossless
    {
        private const nuint KMaxFrameHeaderSize = 5;
        private const int KLogChunkSize         = 3;
        private const int KChunkSize            = 1 << KLogChunkSize;
        private const int KHashExp              = 16;
        private const int KHashSize             = 1 << KHashExp;
        private const uint KHashMultiplier      = 2654435761u;
        private const int KMaxColors            = 512;

        private static readonly nuint[] KGroupSizeOffset = { 0, 1024, 17408, 4211712 };
        private static readonly uint[] KTocBits          = { 12, 16, 24, 32 };

        private static readonly byte[] KUpTo8MinRawLength  = new byte[12];
        private static readonly byte[] KUpTo8MaxRawLength  = { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 10 };
        private static readonly byte[] K9To13MinRawLength  = new byte[17];
        private static readonly byte[] K9To13MaxRawLength  = { 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 10 };
        private static readonly byte[] K14MinRawLength     = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 8, 7 };
        private static readonly byte[] K14MaxRawLength     = { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 10 };
        private static readonly byte[] KMore14MinRawLength = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 8, 8, 8, 8, 8, 7 };
        private static readonly byte[] KMore14MaxRawLength = { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 10 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint FloorLog2(uint v)
        {
            return v != 0 ? (uint)BitOperations.Log2(v) : 0u;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void EncodeHybridUintLZ77(uint value, out uint token, out uint nbits, out uint bits)
        {
            uint n = FloorLog2(value);
            token  = value < 16u ? value : (16u + n - 4u);
            nbits  = value < 16u ? 0u : n;
            bits   = value < 16u ? 0u : (value - (1u << (int)nbits));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void EncodeHybridUint000(uint value, out uint token, out uint nbits, out uint bits)
        {
            uint n = FloorLog2(value);
            token  = value != 0 ? (n + 1u) : 0u;
            nbits  = value != 0 ? n : 0u;
            bits   = value != 0 ? (value - (1u << (int)n)) : 0u;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint PackSignedValue(int value)
        {
            return ((uint)value << 1) ^ (((uint)~value >> 31) - 1u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PixelHash(uint p)
        {
            return (p * KHashMultiplier) >> (32 - KHashExp);
        }

        private static nuint SectionSize(FastLosslessBitWriter[] groupData)
        {
            nuint sz = 0;

            for (int j = 0; j < 4; j++)
            {
                FastLosslessBitWriter writer = groupData[j];
                sz += (writer.BytesWritten * 8u) + writer.BitsInBuffer;
            }

            return (sz + 7u) / 8u;
        }

        private static int TOCBucket(nuint groupSize)
        {
            int bucket = 0;

            while (bucket < 3 && groupSize >= KGroupSizeOffset[bucket + 1])
            {
                bucket++;
            }

            return bucket;
        }

        private static nuint TOCSize(ReadOnlySpan<nuint> groupSizes)
        {
            nuint tocBits = 0;

            for (int i = 0; i < groupSizes.Length; i++)
            {
                tocBits += KTocBits[TOCBucket(groupSizes[i])];
            }

            return (tocBits + 7u) / 8u;
        }

        private static nuint FrameHeaderSize(bool haveAlpha, bool isLast)
        {
            nuint nbits = 28u + (haveAlpha ? 4u : 0u) + (isLast ? 0u : 2u);

            return (nbits + 7u) / 8u;
        }

        private static void ComputeAcGroupDataOffset(
            nuint dcGlobalSize,
            nuint numDcGroups,
            nuint numAcGroups,
            out nuint minDcGlobalSize,
            out nuint acGroupOffset)
        {
            nuint acTocMaxBits = numAcGroups * 24u;
            nuint acTocMinBits = numAcGroups * 12u;
            nuint maxPadding   = 1u + ((acTocMaxBits - acTocMinBits + 7u) / 8u);
            minDcGlobalSize    = dcGlobalSize;
            int dcGlobalBucket = TOCBucket(minDcGlobalSize);

            while (TOCBucket(minDcGlobalSize + maxPadding) > dcGlobalBucket)
            {
                dcGlobalBucket  = TOCBucket(minDcGlobalSize + maxPadding);
                minDcGlobalSize = KGroupSizeOffset[dcGlobalBucket];
            }

            nuint maxTocBits = KTocBits[dcGlobalBucket] + (12u * (1u + numDcGroups)) + acTocMaxBits;
            nuint maxTocSize = (maxTocBits + 7u) / 8u;
            acGroupOffset    = KMaxFrameHeaderSize + maxTocSize + minDcGlobalSize;
        }

        private static nuint ComputeDcGlobalPadding(
            ReadOnlySpan<nuint> groupSizes,
            nuint acGroupDataOffset,
            nuint minDcGlobalSize,
            bool haveAlpha,
            bool isLast)
        {
            nuint[] newGroupSizes = groupSizes.ToArray();
            newGroupSizes[0]      = minDcGlobalSize;
            nuint tocSize         = TOCSize(newGroupSizes);
            nuint actualOffset    = FrameHeaderSize(haveAlpha, isLast) + tocSize + groupSizes[0];

            return acGroupDataOffset - actualOffset;
        }

        /// <summary>
        /// Returns the actual encoded byte size of <paramref name="frame"/>.
        /// Ported from <c>JxlFastLosslessOutputSize</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static nuint JxlFastLosslessOutputSize(JxlFastLosslessFrameState frame)
        {
            nuint totalSizeGroups = 0;

            for (int i = 0; i < frame.GroupData.Length; i++)
            {
                totalSizeGroups += SectionSize(frame.GroupData[i]);
            }

            return frame.Header.BytesWritten + totalSizeGroups;
        }

        /// <summary>
        /// Returns an upper bound on the required output buffer size for <paramref name="frame"/>, including 32-byte padding.
        /// Ported from <c>JxlFastLosslessMaxRequiredOutput</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static nuint JxlFastLosslessMaxRequiredOutput(JxlFastLosslessFrameState frame)
        {
            return JxlFastLosslessOutputSize(frame) + 32u;
        }

        /// <summary>
        /// Frees resources associated with <paramref name="frame"/>.
        /// Ported from <c>JxlFastLosslessFreeFrameState</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static void JxlFastLosslessFreeFrameState(JxlFastLosslessFrameState? frame)
        {
            frame?.Dispose();
        }

        /// <summary>
        /// Prepares the frame (and optional standalone image) header for <paramref name="frame"/>.
        /// Ported from <c>JxlFastLosslessPrepareHeader</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static bool JxlFastLosslessPrepareHeader(
            JxlFastLosslessFrameState frame,
            int addImageHeader,
            int isLast)
        {
            FastLosslessBitWriter output = frame.Header;

            if (!output.Allocate(1000u + ((nuint)frame.GroupSizes.Length * 32u)))
            {
                return false;
            }

            bool haveAlpha = frame.NbChans == 2 || frame.NbChans == 4;

            if (addImageHeader != 0)
            {
                WriteStandaloneImageHeader(frame, output, haveAlpha);
            }

            WriteFrameHeaderBits(frame, output, haveAlpha, isLast != 0);

            return true;
        }

        private static void WriteStandaloneImageHeader(
            JxlFastLosslessFrameState frame,
            FastLosslessBitWriter output,
            bool haveAlpha)
        {
            output.Write(16, 0x0AFF);
            output.Write(1, 0);

            WriteSizeHeaderDimension(output, frame.Height);
            output.Write(3, 0);
            WriteSizeHeaderDimension(output, frame.Width);

            output.Write(1, 0);
            output.Write(1, 0);
            output.Write(1, 0);

            WriteBitDepthField(output, frame.Bitdepth);
            output.Write(1, frame.Bitdepth <= 14 ? 1UL : 0UL);

            if (haveAlpha)
            {
                WriteAlphaExtraChannelHeader(output, frame.Bitdepth);
            }
            else
            {
                output.Write(2, 0b00);
            }

            output.Write(1, 0);

            if (frame.NbChans > 2)
            {
                output.Write(1, 1);
            }
            else
            {
                output.Write(1, 0);
                output.Write(1, 0);
                output.Write(2, 1);
                output.Write(2, 1);
                output.Write(1, 0);
                output.Write(2, 0b10);
                output.Write(4, 11);
                output.Write(2, 1);
            }

            output.Write(2, 0b00);
            output.Write(1, 1);
            output.ZeroPadToByte();
        }

        private static void WriteSizeHeaderDimension(FastLosslessBitWriter output, nuint size)
        {
            nuint m1 = size - 1u;

            if (m1 < (1u << 9))
            {
                output.Write(2, 0b00);
                output.Write(9, m1);
            }
            else if (m1 < (1u << 13))
            {
                output.Write(2, 0b01);
                output.Write(13, m1);
            }
            else if (m1 < (1u << 18))
            {
                output.Write(2, 0b10);
                output.Write(18, m1);
            }
            else
            {
                output.Write(2, 0b11);
                output.Write(30, m1);
            }
        }

        private static void WriteBitDepthField(FastLosslessBitWriter output, nuint bitdepth)
        {
            if (bitdepth == 8)
            {
                output.Write(2, 0b00);
            }
            else if (bitdepth == 10)
            {
                output.Write(2, 0b01);
            }
            else if (bitdepth == 12)
            {
                output.Write(2, 0b10);
            }
            else
            {
                output.Write(2, 0b11);
                output.Write(6, bitdepth - 1u);
            }
        }

        private static void WriteAlphaExtraChannelHeader(FastLosslessBitWriter output, nuint bitdepth)
        {
            output.Write(2, 0b01);

            if (bitdepth == 8)
            {
                output.Write(1, 1);
            }
            else
            {
                output.Write(1, 0);
                output.Write(2, 0);
                output.Write(1, 0);
                WriteBitDepthField(output, bitdepth);
                output.Write(2, 0);
                output.Write(2, 0);
                output.Write(1, 0);
            }
        }

        private static void WriteFrameHeaderBits(
            JxlFastLosslessFrameState frame,
            FastLosslessBitWriter output,
            bool haveAlpha,
            bool isLast)
        {
            output.Write(1, 0);
            output.Write(2, 0b00);
            output.Write(1, 1);
            output.Write(2, 0b00);
            output.Write(1, 0);
            output.Write(2, 0b00);

            if (haveAlpha)
            {
                output.Write(2, 0b00);
            }

            output.Write(2, 0b01);
            output.Write(2, 0b00);
            output.Write(1, 0);
            output.Write(2, 0b00);

            if (haveAlpha)
            {
                output.Write(2, 0b00);
            }

            output.Write(1, isLast ? 1UL : 0UL);

            if (!isLast)
            {
                output.Write(2, 0b00);
            }

            output.Write(2, 0b00);
            output.Write(1, 0);
            output.Write(1, 0);
            output.Write(2, 0);
            output.Write(2, 0b00);
            output.Write(2, 0b00);

            output.Write(1, 0);
            output.ZeroPadToByte();

            for (int i = 0; i < frame.GroupSizes.Length; i++)
            {
                nuint groupSize = frame.GroupSizes[i];
                int bucket      = TOCBucket(groupSize);
                output.Write(2, (ulong)bucket);
                output.Write(KTocBits[bucket] - 2u, groupSize - KGroupSizeOffset[bucket]);
            }

            output.ZeroPadToByte();
        }

        /// <summary>
        /// Writes encoded frame bytes into <paramref name="output"/> (requires <c>output.Length &gt;= 32</c>).
        /// Returns the number of bytes written, or <c>0</c> when all bytes have been emitted.
        /// Ported from <c>JxlFastLosslessWriteOutput</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static nuint JxlFastLosslessWriteOutput(JxlFastLosslessFrameState frame, Span<byte> output)
        {
            nuint written    = 0;
            nuint outputSize = (nuint)output.Length;

            while (true)
            {
                nuint cur   = frame.CurrentBitWriter;
                nuint bwPos = frame.BitWriterBytePos;
                nuint nbc   = frame.NbChans;

                if (cur >= 1u + ((nuint)frame.GroupData.Length * nbc) || outputSize <= 9u)
                {
                    return written;
                }

                FastLosslessBitWriter writer = cur == 0
                    ? frame.Header
                    : frame.GroupData[(cur - 1u) / nbc][(cur - 1u) % nbc];

                nuint fullByteCount = Math.Min(outputSize - 9u, writer.BytesWritten - bwPos);
                CopyWriterBytesToOutput(frame, writer, bwPos, fullByteCount, output.Slice((int)written));

                written                  += fullByteCount;
                outputSize               -= fullByteCount;
                bwPos                    += fullByteCount;
                frame.BitWriterBytePos    = bwPos;

                if (bwPos == writer.BytesWritten)
                {
                    AdvanceToNextWriter(frame, writer, nbc, output, ref written, ref outputSize);
                }
            }
        }

        private static void CopyWriterBytesToOutput(
            JxlFastLosslessFrameState frame,
            FastLosslessBitWriter writer,
            nuint bwPos,
            nuint fullByteCount,
            Span<byte> dst)
        {
            ReadOnlySpan<byte> src = writer.Data.AsSpan((int)bwPos, (int)fullByteCount);

            if (frame.BitsInBuffer == 0)
            {
                src.CopyTo(dst);

                return;
            }

            nuint i = 0;

            for (; i + 8u < fullByteCount; i += 8u)
            {
                ulong chunk      = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice((int)i, 8));
                ulong outChunk   = frame.BitBuffer | (chunk << (int)frame.BitsInBuffer);
                BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice((int)i, 8), outChunk);
                frame.BitBuffer  = chunk >> (64 - (int)frame.BitsInBuffer);
            }

            for (; i < fullByteCount; i++)
            {
                FastLosslessBitWriter.AddBits(
                    8,
                    src[(int)i],
                    dst.Slice((int)i),
                    ref frame.BitsInBuffer,
                    ref frame.BitBuffer);
            }
        }

        private static void AdvanceToNextWriter(
            JxlFastLosslessFrameState frame,
            FastLosslessBitWriter writer,
            nuint nbc,
            Span<byte> output,
            ref nuint written,
            ref nuint outputSize)
        {
            if (writer.BitsInBuffer != 0)
            {
                nuint n = FastLosslessBitWriter.AddBits(
                    (uint)writer.BitsInBuffer,
                    writer.Buffer,
                    output.Slice((int)written),
                    ref frame.BitsInBuffer,
                    ref frame.BitBuffer);
                written    += n;
                outputSize -= n;
            }

            frame.BitWriterBytePos = 0;
            frame.CurrentBitWriter++;

            if (((frame.CurrentBitWriter - 1u) % nbc) == 0 && frame.BitsInBuffer != 0)
            {
                nuint n = FastLosslessBitWriter.AddBits(
                    (uint)(8u - frame.BitsInBuffer),
                    0,
                    output.Slice((int)written),
                    ref frame.BitsInBuffer,
                    ref frame.BitBuffer);
                written    += n;
                outputSize -= n;
            }
        }

        /// <summary>
        /// Writes the encoded frame in <paramref name="frameState"/> to <paramref name="outputProcessor"/>.
        /// Ported from <c>JxlFastLosslessOutputFrame</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static bool JxlFastLosslessOutputFrame(
            JxlFastLosslessFrameState frameState,
            IJxlFastLosslessOutputProcessor outputProcessor)
        {
            nuint flSize  = JxlFastLosslessOutputSize(frameState);
            nuint written = 0;
            int bufLen    = (int)Math.Max(64u, Math.Min(65536u, flSize + 32u));
            byte[] buffer = new byte[bufLen];

            while (written < flSize)
            {
                int req = (int)Math.Max(32u, Math.Min((nuint)buffer.Length, (flSize - written) + 32u));
                nuint n = JxlFastLosslessWriteOutput(frameState, buffer.AsSpan(0, req));

                if (n == 0)
                {
                    break;
                }

                Status st = outputProcessor.AppendBytes(buffer.AsSpan(0, (int)n));

                if (!st.IsOk)
                {
                    return false;
                }

                written += n;
            }

            return true;
        }

        private static bool OutputAlignedSection(
            FastLosslessBitWriter bw,
            IJxlFastLosslessOutputProcessor outputProcessor)
        {
            if (bw.Data == null || bw.BytesWritten == 0)
            {
                return true;
            }

            Status st = outputProcessor.AppendBytes(bw.Data.AsSpan(0, (int)bw.BytesWritten));

            return st.IsOk;
        }

        private static bool OutputHeaders(
            JxlFastLosslessFrameState frameState,
            IJxlFastLosslessOutputProcessor outputProcessor)
        {
            return OutputAlignedSection(frameState.Header, outputProcessor) &&
                   OutputAlignedSection(frameState.GroupData[0][0], outputProcessor);
        }

        private static bool PrepareDCGlobalCommon(
            bool isSingleGroup,
            nuint width,
            nuint height,
            nuint maxEncodedBitsPerSample,
            FastLosslessPrefixCode[] code,
            FastLosslessBitWriter output)
        {
            nuint numSamples = isSingleGroup ? (width * height) : 0u;

            if (!output.Allocate(100000u + (numSamples * maxEncodedBitsPerSample)))
            {
                return false;
            }

            output.Write(1, 1);
            output.Write(1, 1);
            output.Write(1, 0);

            output.Write(1, 1);
            output.Write(2, 0);
            output.Write(1, 1);
            output.Write(4, 0);
            output.Write(6, 0b100011);
            output.Write(2, 1);
            output.Write(2, 3);
            output.Write(2, 0);
            output.Write(2, 1);
            output.Write(2, 2);
            output.Write(2, 3);
            output.Write(1, 0);

            ReadOnlySpan<byte> symbolBits  = stackalloc byte[6] { 0b00, 0b10, 0b001, 0b101, 0b0011, 0b0111 };
            ReadOnlySpan<byte> symbolNbits = stackalloc byte[6] { 2, 2, 3, 3, 4, 4 };
            ReadOnlySpan<byte> treeSeq     = stackalloc byte[26]
            {
                1, 2, 1, 4, 1, 0, 0, 5, 0, 0, 0, 0, 5,
                0, 0, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0,
            };

            for (int i = 0; i < treeSeq.Length; i++)
            {
                byte v = treeSeq[i];
                output.Write(symbolNbits[v], symbolBits[v]);
            }

            output.Write(1, 1);
            output.Write(2, 0b00);
            output.Write(4, 0b1010);
            output.Write(4, 4);
            output.Write(3, 0);
            output.Write(3, 0);

            output.Write(1, 1);
            output.Write(2, 3);
            output.Write(3, 4);
            output.Write(3, 3);
            output.Write(3, 2);
            output.Write(3, 1);
            output.Write(3, 0);

            output.Write(1, 1);
            output.Write(4, 0);

            for (int i = 0; i < 4; i++)
            {
                output.Write(4, 0);
            }

            output.Write(5, 0b00001);

            for (int i = 0; i < 4; i++)
            {
                output.Write(1, 1);
                output.Write(4, 8);
                output.Write(8, 256);
            }

            output.Write(2, 1);
            output.Write(2, 0);
            output.Write(1, 1);

            for (int i = 0; i < 4; i++)
            {
                code[i].WriteTo(output);
            }

            output.Write(1, 1);
            output.Write(1, 1);

            return true;
        }

        private static bool PrepareDCGlobal(
            bool isSingleGroup,
            nuint width,
            nuint height,
            nuint maxEncodedBitsPerSample,
            nuint nbChans,
            FastLosslessPrefixCode[] code,
            FastLosslessBitWriter output)
        {
            if (!PrepareDCGlobalCommon(isSingleGroup, width, height, maxEncodedBitsPerSample, code, output))
            {
                return false;
            }

            if (nbChans > 2)
            {
                output.Write(2, 0b01);
                output.Write(2, 0b00);
                output.Write(5, 0b00000);
                output.Write(2, 0b00);
            }
            else
            {
                output.Write(2, 0b00);
            }

            if (!isSingleGroup)
            {
                output.ZeroPadToByte();
            }

            return true;
        }

        private static bool PrepareDCGlobalPalette(
            bool isSingleGroup,
            JxlFastLosslessFrameState state,
            nuint maxEncodedBitsPerSample,
            uint[] palette,
            int pcolors,
            FastLosslessBitWriter output)
        {
            if (!PrepareDCGlobalCommon(isSingleGroup, state.Width, state.Height, maxEncodedBitsPerSample, state.Hcode, output))
            {
                return false;
            }

            WritePaletteTransformHeader(output, state.NbChans, (nuint)pcolors);

            short[][] p = new short[4][]
            {
                new short[32 + 1024],
                new short[32 + 1024],
                new short[32 + 1024],
                new short[32 + 1024],
            };

            for (int i = 0; i < pcolors; i++)
            {
                p[0][17 + i] = (short)(palette[i] & 0xFF);
                p[1][17 + i] = (short)((palette[i] >> 8) & 0xFF);
                p[2][17 + i] = (short)((palette[i] >> 16) & 0xFF);
                p[3][17 + i] = (short)((palette[i] >> 24) & 0xFF);
            }

            RowEncoder16 rowEncoder = new RowEncoder16(state.Hcode[0], output);
            p[0][15]                = 0;
            rowEncoder.ProcessRow(p[0], 16, p[0], 15, p[0], 15, p[0], 15, pcolors);

            p[1][15] = p[0][16];
            p[0][15] = p[0][16];

            if (state.NbChans > 1)
            {
                rowEncoder.ProcessRow(p[1], 16, p[1], 15, p[0], 16, p[0], 15, pcolors);
            }

            p[2][15] = p[1][16];
            p[1][15] = p[1][16];

            if (state.NbChans > 2)
            {
                rowEncoder.ProcessRow(p[2], 16, p[2], 15, p[1], 16, p[1], 15, pcolors);
            }

            p[3][15] = p[2][16];
            p[2][15] = p[2][16];

            if (state.NbChans > 3)
            {
                rowEncoder.ProcessRow(p[3], 16, p[3], 15, p[2], 16, p[2], 15, pcolors);
            }

            rowEncoder.FinalizeRun();

            if (!isSingleGroup)
            {
                output.ZeroPadToByte();
            }

            return true;
        }

        private static void WritePaletteTransformHeader(FastLosslessBitWriter output, nuint nbChans, nuint pcolors)
        {
            output.Write(2, 0b01);
            output.Write(2, 0b01);
            output.Write(5, 0b00000);

            if (nbChans == 1)
            {
                output.Write(2, 0b00);
            }
            else if (nbChans == 3)
            {
                output.Write(2, 0b01);
            }
            else if (nbChans == 4)
            {
                output.Write(2, 0b10);
            }
            else
            {
                output.Write(2, 0b11);
                output.Write(13, nbChans - 1u);
            }

            if (pcolors < 256)
            {
                output.Write(2, 0b00);
                output.Write(8, pcolors);
            }
            else
            {
                output.Write(2, 0b01);
                output.Write(10, pcolors - 256u);
            }

            output.Write(2, 0b00);
            output.Write(4, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void EncodeRle(nuint count, FastLosslessPrefixCode code, FastLosslessBitWriter output)
        {
            if (count == 0)
            {
                return;
            }

            count -= FastLosslessPrefixCode.KLZ77MinLength + 1;

            if (count < FastLosslessPrefixCode.KLZ77CacheSize)
            {
                output.Write(code.Lz77CacheNbits[count], code.Lz77CacheBits[count]);
            }
            else
            {
                EncodeHybridUintLZ77((uint)count, out uint token, out uint nbits, out uint bits);
                ulong wbits = bits;
                wbits       = (wbits << code.Lz77Nbits[token]) | code.Lz77Bits[token];
                wbits       = (wbits << code.RawNbits[0]) | code.RawBits[0];
                output.Write((uint)(code.Lz77Nbits[token] + nbits + code.RawNbits[0]), wbits);
            }
        }

        // The predictor is independent across encoder input pixels. The 16-bit
        // path wraps its predictor arithmetic, then widens the final subtraction
        // before zigzag packing, exactly like the scalar implementation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ComputeResiduals(ReadOnlySpan<short> row, ReadOnlySpan<short> leftRow,
            ReadOnlySpan<short> topRow, ReadOnlySpan<short> topLeftRow, Span<ushort> residuals)
        {
            if (Avx2.IsSupported && residuals.Length == KChunkSize && row.Length >= KChunkSize &&
                leftRow.Length >= KChunkSize && topRow.Length >= KChunkSize && topLeftRow.Length >= KChunkSize)
            {
                Vector128<short> px = Vector128.Create(row);
                Vector128<short> left = Vector128.Create(leftRow);
                Vector128<short> top = Vector128.Create(topRow);
                Vector128<short> topLeft = Vector128.Create(topLeftRow);
                Vector128<short> ac = left - topLeft;
                Vector128<short> ab = left - top;
                Vector128<short> bc = top - topLeft;
                Vector128<short> clamp = Vector128.ConditionalSelect(Vector128.LessThan(ab ^ bc, Vector128<short>.Zero), top, left);
                Vector128<short> pred = Vector128.ConditionalSelect(Vector128.LessThan(ac ^ bc, Vector128<short>.Zero), ac + top, clamp);
                Vector256<int> diff = Avx2.ConvertToVector256Int32(px) - Avx2.ConvertToVector256Int32(pred);
                Vector256<uint> packed = (Vector256.ShiftLeft(diff, 1) ^ Vector256.ShiftRightArithmetic(diff, 31)).AsUInt32();
                Vector128.Narrow(packed.GetLower(), packed.GetUpper()).CopyTo(residuals);
                return;
            }
            for (int ix = 0; ix < residuals.Length; ix++)
            {
                short px = ix < row.Length ? row[ix] : (short)0;
                short left = ix < leftRow.Length ? leftRow[ix] : (short)0;
                short top = ix < topRow.Length ? topRow[ix] : (short)0;
                short topLeft = ix < topLeftRow.Length ? topLeftRow[ix] : (short)0;
                short ac = unchecked((short)(left - topLeft));
                short ab = unchecked((short)(left - top));
                short bc = unchecked((short)(top - topLeft));
                short grad = unchecked((short)((ushort)ac + (ushort)top));
                short clamp = (short)(ab ^ bc) < 0 ? top : left;
                short pred = (short)(ac ^ bc) < 0 ? grad : clamp;
                residuals[ix] = unchecked((ushort)PackSignedValue(px - pred));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ComputeResiduals(ReadOnlySpan<int> row, ReadOnlySpan<int> leftRow,
            ReadOnlySpan<int> topRow, ReadOnlySpan<int> topLeftRow, Span<uint> residuals)
        {
            if (Avx2.IsSupported && residuals.Length == KChunkSize && row.Length >= KChunkSize &&
                leftRow.Length >= KChunkSize && topRow.Length >= KChunkSize && topLeftRow.Length >= KChunkSize)
            {
                Vector256<int> px = Vector256.Create(row);
                Vector256<int> left = Vector256.Create(leftRow);
                Vector256<int> top = Vector256.Create(topRow);
                Vector256<int> topLeft = Vector256.Create(topLeftRow);
                Vector256<int> ac = left - topLeft;
                Vector256<int> ab = left - top;
                Vector256<int> bc = top - topLeft;
                Vector256<int> clamp = Vector256.ConditionalSelect(Vector256.LessThan(ab ^ bc, Vector256<int>.Zero), top, left);
                Vector256<int> pred = Vector256.ConditionalSelect(Vector256.LessThan(ac ^ bc, Vector256<int>.Zero), ac + top, clamp);
                Vector256<int> diff = px - pred;
                (Vector256.ShiftLeft(diff, 1) ^ Vector256.ShiftRightArithmetic(diff, 31)).AsUInt32().CopyTo(residuals);
                return;
            }
            for (int ix = 0; ix < residuals.Length; ix++)
            {
                int px = ix < row.Length ? row[ix] : 0;
                int left = ix < leftRow.Length ? leftRow[ix] : 0;
                int top = ix < topRow.Length ? topRow[ix] : 0;
                int topLeft = ix < topLeftRow.Length ? topLeftRow[ix] : 0;
                int ac = unchecked(left - topLeft);
                int ab = unchecked(left - top);
                int bc = unchecked(top - topLeft);
                int grad = unchecked(ac + top);
                int clamp = (ab ^ bc) < 0 ? top : left;
                int pred = (ac ^ bc) < 0 ? grad : clamp;
                residuals[ix] = PackSignedValue(unchecked(px - pred));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountZeroPrefix(ReadOnlySpan<ushort> residuals)
        {
            int prefix = 0;
            while (prefix < residuals.Length && residuals[prefix] == 0) prefix++;
            return prefix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountZeroPrefix(ReadOnlySpan<uint> residuals)
        {
            int prefix = 0;
            while (prefix < residuals.Length && residuals[prefix] == 0) prefix++;
            return prefix;
        }

        private sealed class RowEncoder16
        {
            private readonly FastLosslessPrefixCode _code;
            private readonly FastLosslessBitWriter _output;
            private nuint _run;

            public RowEncoder16(FastLosslessPrefixCode code, FastLosslessBitWriter output)
            {
                _code   = code;
                _output = output;
            }

            public void ProcessRow(
                short[] row,
                int rowOff,
                short[] rowLeft,
                int leftOff,
                short[] rowTop,
                int topOff,
                short[] rowTopLeft,
                int topLeftOff,
                int xs)
            {
                Span<ushort> residuals = stackalloc ushort[KChunkSize];

                for (int x = 0; x < xs; x += KChunkSize)
                {
                    int n = Math.Min(KChunkSize, xs - x);
                    ProcessChunk(
                        row.AsSpan(rowOff + x),
                        rowLeft.AsSpan(leftOff + x),
                        rowTop.AsSpan(topOff + x),
                        rowTopLeft.AsSpan(topLeftOff + x),
                        n,
                        residuals);
                }
            }

            private void ProcessChunk(
                ReadOnlySpan<short> row,
                ReadOnlySpan<short> rowLeft,
                ReadOnlySpan<short> rowTop,
                ReadOnlySpan<short> rowTopLeft,
                int n,
                Span<ushort> residuals)
            {
                ComputeResiduals(row, rowLeft, rowTop, rowTopLeft, residuals.Slice(0, n));
                int prefixSize = CountZeroPrefix(residuals.Slice(0, n));

                if (prefixSize == n && (_run > 0 || prefixSize > FastLosslessPrefixCode.KLZ77MinLength))
                {
                    _run += (nuint)prefixSize;
                }
                else if (prefixSize + (int)_run > FastLosslessPrefixCode.KLZ77MinLength)
                {
                    EmitChunk(_run + (nuint)prefixSize, residuals, prefixSize, n);
                    _run = 0;
                }
                else
                {
                    EmitChunk(0, residuals, 0, n);
                }
            }

            private void EmitChunk(nuint run, ReadOnlySpan<ushort> residuals, int skip, int n)
            {
                EncodeRle(run, _code, _output);

                for (int ix = skip; ix < n; ix++)
                {
                    EncodeHybridUint000(residuals[ix], out uint token, out uint nbits, out uint bits);
                    _output.Write(
                        _code.RawNbits[token] + nbits,
                        _code.RawBits[token] | ((ulong)bits << _code.RawNbits[token]));
                }
            }

            public void FinalizeRun()
            {
                EncodeRle(_run, _code, _output);
            }
        }

        private sealed class RowProcessor32
        {
            private readonly FastLosslessPrefixCode? _code;
            private readonly FastLosslessBitWriter? _output;
            private readonly ulong[]? _rawCounts;
            private readonly ulong[]? _lz77Counts;
            private nuint _run;

            public RowProcessor32(FastLosslessPrefixCode code, FastLosslessBitWriter output)
            {
                _code   = code;
                _output = output;
            }

            public RowProcessor32(ulong[] rawCounts, ulong[] lz77Counts)
            {
                _rawCounts  = rawCounts;
                _lz77Counts = lz77Counts;
            }

            public void ProcessRow(
                int[] row,
                int rowOff,
                int[] rowLeft,
                int leftOff,
                int[] rowTop,
                int topOff,
                int[] rowTopLeft,
                int topLeftOff,
                int xs)
            {
                Span<uint> residuals = stackalloc uint[KChunkSize];

                for (int x = 0; x < xs; x += KChunkSize)
                {
                    int n = Math.Min(KChunkSize, xs - x);
                    ProcessChunk(
                        row.AsSpan(rowOff + x),
                        rowLeft.AsSpan(leftOff + x),
                        rowTop.AsSpan(topOff + x),
                        rowTopLeft.AsSpan(topLeftOff + x),
                        n,
                        residuals);
                }
            }

            private void ProcessChunk(
                ReadOnlySpan<int> row,
                ReadOnlySpan<int> rowLeft,
                ReadOnlySpan<int> rowTop,
                ReadOnlySpan<int> rowTopLeft,
                int n,
                Span<uint> residuals)
            {
                ComputeResiduals(row, rowLeft, rowTop, rowTopLeft, residuals.Slice(0, n));
                int prefixSize = CountZeroPrefix(residuals.Slice(0, n));

                if (prefixSize == n && (_run > 0 || prefixSize > FastLosslessPrefixCode.KLZ77MinLength))
                {
                    _run += (nuint)prefixSize;
                }
                else if (prefixSize + (int)_run > FastLosslessPrefixCode.KLZ77MinLength)
                {
                    EmitChunk(_run + (nuint)prefixSize, residuals, prefixSize, n);
                    _run = 0;
                }
                else
                {
                    EmitChunk(0, residuals, 0, n);
                }
            }

            private void EmitChunk(nuint run, ReadOnlySpan<uint> residuals, int skip, int n)
            {
                if (_output != null && _code != null)
                {
                    EncodeRle(run, _code, _output);

                    for (int ix = skip; ix < n; ix++)
                    {
                        EncodeHybridUint000(residuals[ix], out uint token, out uint nbits, out uint bits);
                        _output.Write(
                            _code.RawNbits[token] + nbits,
                            _code.RawBits[token] | ((ulong)bits << _code.RawNbits[token]));
                    }
                }
                else if (_rawCounts != null && _lz77Counts != null)
                {
                    if (run > 0)
                    {
                        _rawCounts[0] += 1;
                        nuint count    = run - FastLosslessPrefixCode.KLZ77MinLength - 1u;
                        EncodeHybridUintLZ77((uint)count, out uint rleTok, out _, out _);
                        _lz77Counts[rleTok]++;
                    }

                    for (int ix = skip; ix < n; ix++)
                    {
                        EncodeHybridUint000(residuals[ix], out uint token, out _, out _);
                        _rawCounts[token]++;
                    }
                }
            }

            public void FinalizeRun()
            {
                if (_output != null && _code != null)
                {
                    EncodeRle(_run, _code, _output);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort LoadU16(ReadOnlySpan<byte> ptr, bool bigEndian)
        {
            return bigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(ptr)
                : BinaryPrimitives.ReadUInt16LittleEndian(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreYCoCg(int r, int g, int b, out int y, out int co, out int cg)
        {
            co      = r - b;
            int tmp = b + (co >> 1);
            cg      = g - tmp;
            y       = tmp + (cg >> 1);
        }

        private static void FillRowPixels(
            ReadOnlySpan<byte> rgbaRow,
            int xs,
            int nbChans,
            int inputBytes,
            bool bigEndian,
            int[][] crow,
            int offset)
        {
            if (inputBytes == 1)
            {
                FillRowPixels8(rgbaRow, xs, nbChans, crow, offset);
            }
            else
            {
                FillRowPixels16(rgbaRow, xs, nbChans, bigEndian, crow, offset);
            }
        }

        private static void FillRowPixels8(ReadOnlySpan<byte> rgba, int xs, int nbChans, int[][] crow, int offset)
        {
            for (int x = 0; x < xs; x++)
            {
                if (nbChans == 1)
                {
                    crow[0][offset + x] = rgba[x];
                }
                else if (nbChans == 2)
                {
                    crow[0][offset + x] = rgba[2 * x];
                    crow[1][offset + x] = rgba[(2 * x) + 1];
                }
                else if (nbChans == 3)
                {
                    StoreYCoCg(rgba[3 * x], rgba[(3 * x) + 1], rgba[(3 * x) + 2], out int y, out int co, out int cg);
                    crow[0][offset + x] = y;
                    crow[1][offset + x] = co;
                    crow[2][offset + x] = cg;
                }
                else
                {
                    StoreYCoCg(rgba[4 * x], rgba[(4 * x) + 1], rgba[(4 * x) + 2], out int y, out int co, out int cg);
                    crow[0][offset + x] = y;
                    crow[1][offset + x] = co;
                    crow[2][offset + x] = cg;
                    crow[3][offset + x] = rgba[(4 * x) + 3];
                }
            }
        }

        private static void FillRowPixels16(ReadOnlySpan<byte> rgba, int xs, int nbChans, bool bigEndian, int[][] crow, int offset)
        {
            for (int x = 0; x < xs; x++)
            {
                if (nbChans == 1)
                {
                    crow[0][offset + x] = LoadU16(rgba.Slice(2 * x), bigEndian);
                }
                else if (nbChans == 2)
                {
                    crow[0][offset + x] = LoadU16(rgba.Slice(4 * x), bigEndian);
                    crow[1][offset + x] = LoadU16(rgba.Slice((4 * x) + 2), bigEndian);
                }
                else if (nbChans == 3)
                {
                    int r = LoadU16(rgba.Slice(6 * x), bigEndian);
                    int g = LoadU16(rgba.Slice((6 * x) + 2), bigEndian);
                    int b = LoadU16(rgba.Slice((6 * x) + 4), bigEndian);
                    StoreYCoCg(r, g, b, out int y, out int co, out int cg);
                    crow[0][offset + x] = y;
                    crow[1][offset + x] = co;
                    crow[2][offset + x] = cg;
                }
                else
                {
                    int r = LoadU16(rgba.Slice(8 * x), bigEndian);
                    int g = LoadU16(rgba.Slice((8 * x) + 2), bigEndian);
                    int b = LoadU16(rgba.Slice((8 * x) + 4), bigEndian);
                    int a = LoadU16(rgba.Slice((8 * x) + 6), bigEndian);
                    StoreYCoCg(r, g, b, out int y, out int co, out int cg);
                    crow[0][offset + x] = y;
                    crow[1][offset + x] = co;
                    crow[2][offset + x] = cg;
                    crow[3][offset + x] = a;
                }
            }
        }

        private static void ProcessImageArea(
            ReadOnlySpan<byte> rgba,
            (nuint X0, nuint Y0, nuint Xs, nuint YSkip, nuint Ys, nuint RowStride) rect,
            (nuint Bitdepth, nuint NbChans, bool BigEndian) fmt,
            RowProcessor32[] processors)
        {
            const int kPadding = 32;
            const int kNumPx   = 256 + (kPadding * 2) + 32;
            int nbChans        = (int)fmt.NbChans;
            int inputBytes     = fmt.Bitdepth <= 8 ? 1 : 2;
            int[][][] groupBuf = new int[nbChans][][];

            for (int c = 0; c < nbChans; c++)
            {
                groupBuf[c] = new int[2][] { new int[kNumPx], new int[kNumPx] };
            }

            int[][] crow = new int[4][];
            int[][] prow = new int[4][];

            for (nuint y = 0; y < rect.Ys; y++)
            {
                int rowStart               = (int)((rect.RowStride * (rect.Y0 + y)) + (rect.X0 * fmt.NbChans * (nuint)inputBytes));
                ReadOnlySpan<byte> rgbaRow = rgba.Slice(rowStart);

                for (int c = 0; c < nbChans; c++)
                {
                    crow[c] = groupBuf[c][y & 1];
                    prow[c] = groupBuf[c][(y - 1u) & 1];
                }

                FillRowPixels(rgbaRow, (int)rect.Xs, nbChans, inputBytes, fmt.BigEndian, crow, kPadding);

                for (int c = 0; c < nbChans; c++)
                {
                    crow[c][kPadding - 1] = y > 0 ? prow[c][kPadding] : 0;
                    prow[c][kPadding - 1] = y > 0 ? prow[c][kPadding] : 0;
                }

                if (y < rect.YSkip)
                {
                    continue;
                }

                for (int c = 0; c < nbChans; c++)
                {
                    int[] topArr = y == 0 ? crow[c] : prow[c];
                    int topOff   = y == 0 ? (kPadding - 1) : kPadding;
                    int topLOff  = kPadding - 1;
                    processors[c].ProcessRow(crow[c], kPadding, crow[c], kPadding - 1, topArr, topOff, topArr, topLOff, (int)rect.Xs);
                }
            }

            for (int c = 0; c < nbChans; c++)
            {
                processors[c].FinalizeRun();
            }
        }

        private static void ProcessImageAreaPalette(
            ReadOnlySpan<byte> rgba,
            (nuint X0, nuint Y0, nuint Xs, nuint YSkip, nuint Ys, nuint RowStride) rect,
            short[] lookup,
            nuint nbChans,
            RowProcessor32 processor)
        {
            const int kPadding = 32;
            int[][] groupData  = new int[2][]
            {
                new int[256 + (kPadding * 2)],
                new int[256 + (kPadding * 2)],
            };

            for (nuint y = 0; y < rect.Ys; y++)
            {
                int rowStart             = (int)((rect.RowStride * (rect.Y0 + y)) + (rect.X0 * nbChans));
                ReadOnlySpan<byte> inrow = rgba.Slice(rowStart);
                int[] outrow             = groupData[y & 1];
                int[] prev               = groupData[(y - 1u) & 1];

                for (int x = 0; x < (int)rect.Xs; x++)
                {
                    uint p = 0;

                    for (int i = 0; i < (int)nbChans; i++)
                    {
                        p |= (uint)inrow[(x * (int)nbChans) + i] << (8 * i);
                    }

                    outrow[kPadding + x] = lookup[PixelHash(p)];
                }

                outrow[kPadding - 1] = y > 0 ? prev[kPadding] : 0;
                prev[kPadding - 1]   = y > 0 ? prev[kPadding] : 0;

                if (y < rect.YSkip)
                {
                    continue;
                }

                int[] topArr = y == 0 ? outrow : prev;
                int topOff   = y == 0 ? (kPadding - 1) : kPadding;
                processor.ProcessRow(outrow, kPadding, outrow, kPadding - 1, topArr, topOff, topArr, kPadding - 1, (int)rect.Xs);
            }

            processor.FinalizeRun();
        }

        private static nuint GetMaxEncodedBitsPerSample(nuint bitdepth)
        {
            if (bitdepth <= 8)
            {
                return 16;
            }

            if (bitdepth <= 13)
            {
                return 22;
            }

            return bitdepth == 14 ? 23u : 25u;
        }

        private static int GetNumRawSymbolsForBitdepth(nuint bitdepth, bool doingYcocgOrLargePalette)
        {
            if (bitdepth <= 13)
            {
                return (int)bitdepth + (doingYcocgOrLargePalette ? 3 : 2);
            }

            return bitdepth == 14 ? 17 : 19;
        }

        private static void GetMinMaxRawLengths(nuint bitdepth, out ReadOnlySpan<byte> minLen, out ReadOnlySpan<byte> maxLen)
        {
            if (bitdepth <= 8)
            {
                minLen = KUpTo8MinRawLength;
                maxLen = KUpTo8MaxRawLength;
            }
            else if (bitdepth <= 13)
            {
                minLen = K9To13MinRawLength;
                maxLen = K9To13MaxRawLength;
            }
            else if (bitdepth == 14)
            {
                minLen = K14MinRawLength;
                maxLen = K14MaxRawLength;
            }
            else
            {
                minLen = KMore14MinRawLength;
                maxLen = KMore14MaxRawLength;
            }
        }

        private static bool DetectPaletteRow(ReadOnlySpan<byte> r, nuint width, int nbChans, uint[] palette)
        {
            bool collided = false;

            for (nuint x = 0; x < width; x++)
            {
                uint p = 0;

                for (int i = 0; i < nbChans; i++)
                {
                    p |= (uint)r[((int)x * nbChans) + i] << (8 * i);
                }

                uint index     = PixelHash(p);
                collided      |= palette[index] != 0 && p != palette[index];
                palette[index] = p;
            }

            return collided;
        }

        private static bool TryBuildPalette(
            in FastLosslessPrepareArgs args,
            uint[] palette,
            short[] lookup,
            out int pcolors)
        {
            pcolors       = 0;
            bool collided = args.Effort < 2 || args.Bitdepth != 8 || !args.OneShot;

            for (nuint y0 = 0; y0 < args.Height && !collided; y0 += 256)
            {
                nuint ys = Math.Min(args.Height - y0, 256u);

                for (nuint x0 = 0; x0 < args.Width && !collided; x0 += 256)
                {
                    nuint xs                    = Math.Min(args.Width - x0, 256u);
                    ReadOnlyMemory<byte> buffer = args.Input.GetColorChannelDataAt!(args.Input.Opaque, x0, y0, xs, ys, out nuint stride);

                    if (buffer.IsEmpty)
                    {
                        return true;
                    }

                    ReadOnlySpan<byte> span = buffer.Span;

                    for (nuint y = 0; y < ys && !collided; y++)
                    {
                        collided = DetectPaletteRow(span.Slice((int)(stride * y)), xs, (int)args.NbChans, palette);
                    }

                    args.Input.ReleaseBuffer?.Invoke(args.Input.Opaque, buffer);
                }
            }

            if (collided)
            {
                return true;
            }

            return CompactAndSortPalette(palette, lookup, (int)args.NbChans, out pcolors);
        }

        private static bool CompactAndSortPalette(uint[] palette, short[] lookup, int nbChans, out int pcolors)
        {
            pcolors        = 1;
            int nbEntries  = 0;
            bool haveColor = false;
            byte minG      = 255;
            byte maxG      = 0;

            for (int k = 0; k < KHashSize; k++)
            {
                if (palette[k] == 0)
                {
                    continue;
                }

                uint pk            = palette[k];
                byte p0            = (byte)(pk & 0xFF);
                byte p1            = (byte)((pk >> 8) & 0xFF);
                byte p2            = (byte)((pk >> 16) & 0xFF);
                palette[nbEntries] = pk;

                if (p0 != p1 || p0 != p2)
                {
                    haveColor = true;
                }

                minG = Math.Min(minG, p1);
                maxG = Math.Max(maxG, p1);
                nbEntries++;

                if (nbEntries + pcolors > KMaxColors)
                {
                    return true;
                }
            }

            if (!haveColor && (maxG - minG) < (nbEntries * 1.4f))
            {
                return true;
            }

            Array.Sort(palette, 0, nbEntries, Comparer<uint>.Create((ap, bp) => ComparePaletteColors(ap, bp, nbChans)));

            for (int k = 0; k < nbEntries; k++)
            {
                if (palette[k] == 0)
                {
                    break;
                }

                lookup[PixelHash(palette[k])] = (short)pcolors++;
            }

            return false;
        }

        private static int ComparePaletteColors(uint ap, uint bp, int nbChans)
        {
            if (ap == bp)
            {
                return 0;
            }

            if (ap == 0)
            {
                return 1;
            }

            if (bp == 0)
            {
                return -1;
            }

            float ay = (0.299f * (ap & 0xFF)) + (0.587f * ((ap >> 8) & 0xFF)) + (0.114f * ((ap >> 16) & 0xFF)) + 0.01f;
            float by = (0.299f * (bp & 0xFF)) + (0.587f * ((bp >> 8) & 0xFF)) + (0.114f * ((bp >> 16) & 0xFF)) + 0.01f;

            if (nbChans == 4)
            {
                ay *= (ap >> 24) & 0xFF;
                by *= (bp >> 24) & 0xFF;
            }

            return ay.CompareTo(by);
        }

        /// <summary>
        /// Prepares a <see cref="JxlFastLosslessFrameState"/> for encoding.
        /// Ported from <c>JxlFastLosslessPrepareFrame</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static JxlFastLosslessFrameState? JxlFastLosslessPrepareFrame(in FastLosslessPrepareArgs args)
        {
            if (args.Width == 0 || args.Height == 0 || args.NbChans == 0 || args.NbChans > 4 || args.Bitdepth == 0 || args.Bitdepth > 16)
            {
                return null;
            }

            uint[] palette = new uint[KHashSize];
            short[] lookup = new short[KHashSize];
            bool collided  = TryBuildPalette(in args, palette, lookup, out int pcolors);

            ulong[][] rawCounts  = new ulong[4][];
            ulong[][] lz77Counts = new ulong[4][];

            for (int c = 0; c < 4; c++)
            {
                rawCounts[c]  = new ulong[FastLosslessPrefixCode.KNumRawSymbols];
                lz77Counts[c] = new ulong[FastLosslessPrefixCode.KNumLZ77];
            }

            if (!CollectFrameSamples(in args, collided, lookup, rawCounts, lz77Counts))
            {
                return null;
            }

            FinalizeSampleCounts(in args, collided, pcolors, rawCounts, lz77Counts);

            return CreatePreparedFrameState(in args, collided, palette, pcolors, lookup, rawCounts, lz77Counts);
        }

        private static bool CollectFrameSamples(
            in FastLosslessPrepareArgs args,
            bool collided,
            short[] lookup,
            ulong[][] rawCounts,
            ulong[][] lz77Counts)
        {
            nuint numGroupsX = (args.Width + 255u) / 256u;
            nuint numGroupsY = (args.Height + 255u) / 256u;
            bool onegroup    = numGroupsX == 1 && numGroupsY == 1;

            if (args.OneShot || args.Effort >= 64)
            {
                for (nuint g = 0; g < numGroupsY * numGroupsX; g++)
                {
                    nuint xg      = g % numGroupsX;
                    nuint yg      = g / numGroupsX;
                    nuint y0      = yg * 256u;
                    nuint ys      = Math.Min(args.Height - y0, 256u);
                    nuint numRows = (2u * (nuint)Math.Max(0, args.Effort) * ys) / 256u;

                    if (!SampleGroupRows(in args, (xg, yg, numRows), (onegroup, collided), lookup, rawCounts, lz77Counts))
                    {
                        return false;
                    }
                }
            }
            else
            {
                nuint numRows = 2u * (nuint)Math.Max(0, args.Effort) * numGroupsX * numGroupsY;

                if (!SampleGroupRows(
                        in args,
                        ((numGroupsX - 1u) / 2u, (numGroupsY - 1u) / 2u, numRows),
                        (onegroup, collided),
                        lookup,
                        rawCounts,
                        lz77Counts))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SampleGroupRows(
            in FastLosslessPrepareArgs args,
            (nuint Xg, nuint Yg, nuint NumRows) groupPos,
            (bool Onegroup, bool Collided) flags,
            short[] lookup,
            ulong[][] rawCounts,
            ulong[][] lz77Counts)
        {
            nuint y0                    = groupPos.Yg * 256u;
            nuint x0                    = groupPos.Xg * 256u;
            nuint ys                    = Math.Min(args.Height - y0, 256u);
            nuint xs                    = Math.Min(args.Width - x0, 256u);
            ReadOnlyMemory<byte> buffer = args.Input.GetColorChannelDataAt!(args.Input.Opaque, x0, y0, xs, ys, out nuint stride);

            if (buffer.IsEmpty)
            {
                return false;
            }

            ReadOnlySpan<byte> span = buffer.Span;
            int yBeginGroup         = (int)Math.Max(0L, ((long)ys - (long)groupPos.NumRows) / 2L);
            int yCount              = Math.Max(0, Math.Min((int)groupPos.NumRows, (int)ys - yBeginGroup - 1));
            nuint xMax              = (xs / (nuint)KChunkSize) * (nuint)KChunkSize;

            if (xMax > 0 && yCount > 0)
            {
                var rect = (X0: (nuint)0, Y0: (nuint)yBeginGroup, Xs: xMax, YSkip: (nuint)1, Ys: (nuint)(1 + yCount), RowStride: stride);

                if (!flags.Collided)
                {
                    int idx       = flags.Onegroup ? 1 : 0;
                    var collector = new RowProcessor32(rawCounts[idx], lz77Counts[idx]);
                    ProcessImageAreaPalette(span, rect, lookup, args.NbChans, collector);
                }
                else
                {
                    RowProcessor32[] collectors = new RowProcessor32[4];

                    for (int c = 0; c < (int)args.NbChans; c++)
                    {
                        collectors[c] = new RowProcessor32(rawCounts[c], lz77Counts[c]);
                    }

                    ProcessImageArea(span, rect, (args.Bitdepth, args.NbChans, args.BigEndian), collectors);
                }
            }

            args.Input.ReleaseBuffer?.Invoke(args.Input.Opaque, buffer);

            return true;
        }

        private static void FinalizeSampleCounts(
            in FastLosslessPrepareArgs args,
            bool collided,
            int pcolors,
            ulong[][] rawCounts,
            ulong[][] lz77Counts)
        {
            Span<ulong> baseRawCounts = stackalloc ulong[FastLosslessPrefixCode.KNumRawSymbols]
            {
                3843, 852, 1270, 1214, 1014, 727, 481, 300, 159, 51,
                5, 1, 1, 1, 1, 1, 1, 1, 1,
            };

            bool doingYcocg   = args.NbChans > 2 && collided;
            bool largePalette = !collided || pcolors >= 256;
            int numSyms       = GetNumRawSymbolsForBitdepth(args.Bitdepth, doingYcocg || largePalette);

            for (int i = numSyms; i < FastLosslessPrefixCode.KNumRawSymbols; i++)
            {
                baseRawCounts[i] = 0;
            }

            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i < FastLosslessPrefixCode.KNumRawSymbols; i++)
                {
                    rawCounts[c][i] = (rawCounts[c][i] << 8) + baseRawCounts[i];
                }
            }

            if (!collided)
            {
                EncodeHybridUint000(PackSignedValue(pcolors - 1), out uint token, out _, out _);

                for (int i = 0; i <= (int)token; i++)
                {
                    rawCounts[0][i] = Math.Max(rawCounts[0][i], 1UL);
                }

                for (int i = (int)token + 1; i < 10; i++)
                {
                    rawCounts[0][i] = 1;
                }
            }

            ReadOnlySpan<ulong> baseLz77Counts = stackalloc ulong[FastLosslessPrefixCode.KNumLZ77]
            {
                29, 27, 25, 23, 21, 21, 19, 18, 21, 17, 16, 15, 15, 14,
                13, 13, 137, 98, 61, 34, 1, 1, 1, 1, 1, 1, 1, 1,
                0, 0, 0, 0, 0,
            };

            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i < FastLosslessPrefixCode.KNumLZ77; i++)
                {
                    lz77Counts[c][i] = (lz77Counts[c][i] << 8) + baseLz77Counts[i];
                }
            }
        }

        private static JxlFastLosslessFrameState? CreatePreparedFrameState(
            in FastLosslessPrepareArgs args,
            bool collided,
            uint[] palette,
            int pcolors,
            short[] lookup,
            ulong[][] rawCounts,
            ulong[][] lz77Counts)
        {
            JxlFastLosslessFrameState state = new JxlFastLosslessFrameState();
            GetMinMaxRawLengths(args.Bitdepth, out ReadOnlySpan<byte> minRaw, out ReadOnlySpan<byte> maxRaw);

            for (int i = 0; i < 4; i++)
            {
                state.Hcode[i] = new FastLosslessPrefixCode(minRaw, maxRaw, rawCounts[i], lz77Counts[i]);
            }

            nuint numGroupsX   = (args.Width + 255u) / 256u;
            nuint numGroupsY   = (args.Height + 255u) / 256u;
            nuint numDcGroupsX = (args.Width + 2047u) / 2048u;
            nuint numDcGroupsY = (args.Height + 2047u) / 2048u;
            nuint numDcGroups  = numDcGroupsX * numDcGroupsY;
            nuint numAcGroups  = numGroupsX * numGroupsY;
            bool onegroup      = numGroupsX == 1 && numGroupsY == 1;
            nuint numGroups    = onegroup ? 1u : (2u + numDcGroups + numAcGroups);

            state.Input        = args.Input;
            state.Width        = args.Width;
            state.Height       = args.Height;
            state.NumGroupsX   = numGroupsX;
            state.NumGroupsY   = numGroupsY;
            state.NumDcGroupsX = numDcGroupsX;
            state.NumDcGroupsY = numDcGroupsY;
            state.NbChans      = args.NbChans;
            state.Bitdepth     = args.Bitdepth;
            state.BigEndian    = args.BigEndian;
            state.Effort       = args.Effort;
            state.Collided     = collided;
            state.Lookup       = lookup;
            state.GroupData    = JxlFastLosslessFrameState.AllocateGroupData((int)numGroups);
            state.GroupSizes   = new nuint[numGroups];

            nuint maxBits = GetMaxEncodedBitsPerSample(args.Bitdepth);
            bool dcOk     = collided
                ? PrepareDCGlobal(onegroup, args.Width, args.Height, maxBits, args.NbChans, state.Hcode, state.GroupData[0][0])
                : PrepareDCGlobalPalette(onegroup, state, maxBits, palette, pcolors, state.GroupData[0][0]);

            if (!dcOk)
            {
                return null;
            }

            state.GroupSizes[0] = SectionSize(state.GroupData[0]);

            if (!onegroup)
            {
                ComputeAcGroupDataOffset(
                    state.GroupSizes[0],
                    numDcGroups,
                    numAcGroups,
                    out state.MinDcGlobalSize,
                    out state.AcGroupDataOffset);
            }

            return state;
        }

        private static bool WriteACSection(
            ReadOnlySpan<byte> rgba,
            nuint xs,
            nuint ys,
            nuint rowStride,
            bool isSingleGroup,
            JxlFastLosslessFrameState state,
            FastLosslessBitWriter[] output)
        {
            nuint maxBits = GetMaxEncodedBitsPerSample(state.Bitdepth);

            for (int i = 0; i < (int)state.NbChans; i++)
            {
                if (isSingleGroup && i == 0)
                {
                    continue;
                }

                if (!output[i].Allocate((xs * ys * maxBits) + 4u))
                {
                    return false;
                }
            }

            if (!isSingleGroup)
            {
                output[0].Write(1, 1);
                output[0].Write(1, 1);
                output[0].Write(2, 0b00);
            }

            RowProcessor32[] rowEncoders = new RowProcessor32[4];

            for (int c = 0; c < (int)state.NbChans; c++)
            {
                rowEncoders[c] = new RowProcessor32(state.Hcode[c], output[c]);
            }

            ProcessImageArea(
                rgba,
                (0, 0, xs, 0, ys, rowStride),
                (state.Bitdepth, state.NbChans, state.BigEndian),
                rowEncoders);

            return true;
        }

        private static bool WriteACSectionPalette(
            ReadOnlySpan<byte> rgba,
            nuint xs,
            nuint ys,
            nuint rowStride,
            bool isSingleGroup,
            JxlFastLosslessFrameState state,
            FastLosslessBitWriter output)
        {
            if (!isSingleGroup)
            {
                if (!output.Allocate((16u * xs * ys) + 4u))
                {
                    return false;
                }

                output.Write(1, 1);
                output.Write(1, 1);
                output.Write(2, 0b00);
            }

            RowProcessor32 rowEncoder = new RowProcessor32(state.Hcode[isSingleGroup ? 1 : 0], output);
            ProcessImageAreaPalette(
                rgba,
                (0, 0, xs, 0, ys, rowStride),
                state.Lookup,
                state.NbChans,
                rowEncoder);

            return true;
        }

        /// <summary>
        /// Processes and encodes all groups of <paramref name="frameState"/>.
        /// Ported from <c>JxlFastLosslessProcessFrame</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static bool JxlFastLosslessProcessFrame(
            JxlFastLosslessFrameState frameState,
            bool isLast,
            object? runnerOpaque = null,
            FJxlParallelRunner? runner = null,
            IJxlFastLosslessOutputProcessor? outputProcessor = null)
        {
            if (frameState.ProcessDone)
            {
                if (!JxlFastLosslessPrepareHeader(frameState, 0, isLast ? 1 : 0))
                {
                    return false;
                }

                return outputProcessor == null || JxlFastLosslessOutputFrame(frameState, outputProcessor);
            }

            FJxlParallelRunner effectiveRunner = runner ?? TrivialRunner;
            bool onegroup                      = frameState.GroupSizes.Length == 1;
            bool streaming                     = !onegroup && outputProcessor != null;
            nuint totalGroups                  = frameState.NumGroupsX * frameState.NumGroupsY;
            nuint maxGroups                    = streaming ? 16u : totalGroups;
            nuint startPos                     = 0;

            if (streaming)
            {
                startPos = outputProcessor!.CurrentPosition;

                if (!outputProcessor.Seek(startPos + frameState.AcGroupDataOffset).IsOk)
                {
                    return false;
                }
            }

            if (!EncodeFrameGroupBatches(frameState, (onegroup, streaming, totalGroups, maxGroups), effectiveRunner, runnerOpaque, outputProcessor))
            {
                return false;
            }

            return FinalizeProcessedFrame(frameState, isLast, onegroup, streaming, startPos, outputProcessor);
        }

        private static void TrivialRunner(object? runnerOpaque, object? opaque, Action<object?, nuint> fun, nuint count)
        {
            for (nuint i = 0; i < count; i++)
            {
                fun(opaque, i);
            }
        }

        private static bool EncodeFrameGroupBatches(
            JxlFastLosslessFrameState frameState,
            (bool Onegroup, bool Streaming, nuint TotalGroups, nuint MaxGroups) batchCfg,
            FJxlParallelRunner runner,
            object? runnerOpaque,
            IJxlFastLosslessOutputProcessor? outputProcessor)
        {
            for (nuint offset = 0; offset < batchCfg.TotalGroups; offset += batchCfg.MaxGroups)
            {
                nuint numGroups                      = Math.Min(batchCfg.MaxGroups, batchCfg.TotalGroups - offset);
                JxlFastLosslessFrameState localState = new JxlFastLosslessFrameState();

                if (batchCfg.Streaming)
                {
                    localState.GroupData = JxlFastLosslessFrameState.AllocateGroupData((int)numGroups);
                }

                int hasError     = 0;
                nuint baseOffset = offset;

                runner(
                    runnerOpaque,
                    null,
                    (_, i) =>
                    {
                        if (!EncodeSingleAcGroup(frameState, localState, baseOffset + i, i, batchCfg.Onegroup, batchCfg.Streaming))
                        {
                            System.Threading.Interlocked.Exchange(ref hasError, 1);
                        }
                    },
                    numGroups);

                if (hasError != 0)
                {
                    return false;
                }

                if (batchCfg.Streaming)
                {
                    localState.NbChans          = frameState.NbChans;
                    localState.CurrentBitWriter = 1;

                    if (!JxlFastLosslessOutputFrame(localState, outputProcessor!))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool EncodeSingleAcGroup(
            JxlFastLosslessFrameState frameState,
            JxlFastLosslessFrameState localState,
            nuint g,
            nuint batchIndex,
            bool onegroup,
            bool streaming)
        {
            nuint xg          = g % frameState.NumGroupsX;
            nuint yg          = g / frameState.NumGroupsX;
            nuint numDcGroups = frameState.NumDcGroupsX * frameState.NumDcGroupsY;
            nuint groupId     = onegroup ? 0u : (2u + numDcGroups + g);
            nuint xs          = Math.Min(frameState.Width - (xg * 256u), 256u);
            nuint ys          = Math.Min(frameState.Height - (yg * 256u), 256u);
            nuint x0          = xg * 256u;
            nuint y0          = yg * 256u;

            ReadOnlyMemory<byte> buffer = frameState.Input.GetColorChannelDataAt!(frameState.Input.Opaque, x0, y0, xs, ys, out nuint stride);

            if (buffer.IsEmpty)
            {
                return false;
            }

            ReadOnlySpan<byte> span    = buffer.Span;
            FastLosslessBitWriter[] gd = streaming
                ? localState.GroupData[batchIndex]
                : frameState.GroupData[groupId];

            bool ok = frameState.Collided
                ? WriteACSection(span, xs, ys, stride, onegroup, frameState, gd)
                : WriteACSectionPalette(span, xs, ys, stride, onegroup, frameState, gd[0]);

            if (ok)
            {
                frameState.GroupSizes[groupId] = SectionSize(gd);
            }

            frameState.Input.ReleaseBuffer?.Invoke(frameState.Input.Opaque, buffer);

            return ok;
        }

        private static bool FinalizeProcessedFrame(
            JxlFastLosslessFrameState frameState,
            bool isLast,
            bool onegroup,
            bool streaming,
            nuint startPos,
            IJxlFastLosslessOutputProcessor? outputProcessor)
        {
            if (streaming)
            {
                nuint endPos = outputProcessor!.CurrentPosition;

                if (!outputProcessor.Seek(startPos).IsOk)
                {
                    return false;
                }

                Array.Resize(ref frameState.GroupData, 1);
                bool haveAlpha = frameState.NbChans == 2 || frameState.NbChans == 4;
                nuint padding  = ComputeDcGlobalPadding(
                    frameState.GroupSizes,
                    frameState.AcGroupDataOffset,
                    frameState.MinDcGlobalSize,
                    haveAlpha,
                    isLast);

                for (nuint i = 0; i < padding; i++)
                {
                    frameState.GroupData[0][0].Write(8, 0);
                }

                frameState.GroupSizes[0] += padding;

                if (!JxlFastLosslessPrepareHeader(frameState, 0, isLast ? 1 : 0) ||
                    !OutputHeaders(frameState, outputProcessor) ||
                    !outputProcessor.Seek(endPos).IsOk)
                {
                    return false;
                }
            }
            else if (outputProcessor != null)
            {
                _ = onegroup;

                if (!JxlFastLosslessPrepareHeader(frameState, 0, isLast ? 1 : 0) ||
                    !JxlFastLosslessOutputFrame(frameState, outputProcessor))
                {
                    return false;
                }
            }

            frameState.ProcessDone = true;

            return true;
        }

        /// <summary>
        /// Creates a <see cref="JxlChunkedFrameInputSource"/> wrapping a contiguous raw pixel buffer in memory.
        /// </summary>
        public static JxlChunkedFrameInputSource CreateMemoryInputSource(
            ReadOnlyMemory<byte> pixels,
            nuint rowStride,
            nuint numChannels,
            nuint bitdepth)
        {
            nuint bytesPerPixel = bitdepth <= 8 ? numChannels : (2u * numChannels);
            var state           = (Pixels: pixels, RowStride: rowStride, BytesPerPixel: bytesPerPixel);

            return new JxlChunkedFrameInputSource
            {
                Opaque                = state,
                GetColorChannelDataAt = static (object? opaque, nuint xpos, nuint ypos, nuint _, nuint __, out nuint rowOffset) =>
                {
                    var s      = ((ReadOnlyMemory<byte> Pixels, nuint RowStride, nuint BytesPerPixel))opaque!;
                    rowOffset  = s.RowStride;
                    int offset = (int)((ypos * s.RowStride) + (xpos * s.BytesPerPixel));

                    return offset <= s.Pixels.Length ? s.Pixels.Slice(offset) : ReadOnlyMemory<byte>.Empty;
                },
                ReleaseBuffer = static (_, _) => { },
            };
        }

        /// <summary>
        /// Creates a <see cref="JxlChunkedFrameInputSource"/> wrapping a contiguous raw pixel array in memory.
        /// </summary>
        public static JxlChunkedFrameInputSource CreateMemoryInputSource(
            byte[] pixels,
            nuint rowStride,
            nuint numChannels,
            nuint bitdepth)
        {
            return CreateMemoryInputSource((ReadOnlyMemory<byte>)pixels, rowStride, numChannels, bitdepth);
        }

        /// <summary>
        /// Standalone one-call fast lossless encoder returning a complete JPEG XL codestream.
        /// Ported from <c>JxlFastLosslessEncode</c> in <c>lib/jxl/enc_fast_lossless.cc</c>.
        /// </summary>
        public static StatusOr<byte[]> FastLosslessEncode(
            ReadOnlySpan<byte> rgba,
            (nuint Width, nuint RowStride, nuint Height, nuint NbChans, nuint Bitdepth) imageSpec,
            bool bigEndian = false,
            int effort = 2,
            object? runnerOpaque = null,
            FJxlParallelRunner? runner = null)
        {
            JxlChunkedFrameInputSource input = CreateMemoryInputSource(
                rgba.ToArray(),
                imageSpec.RowStride,
                imageSpec.NbChans,
                imageSpec.Bitdepth);

            FastLosslessPrepareArgs prepArgs = new FastLosslessPrepareArgs(
                input,
                imageSpec.Width,
                imageSpec.Height,
                imageSpec.NbChans,
                imageSpec.Bitdepth,
                bigEndian,
                effort,
                oneShot: true);

            using JxlFastLosslessFrameState? frameState = JxlFastLosslessPrepareFrame(in prepArgs);

            if (frameState == null)
            {
                return Status.Failure("JxlFastLosslessPrepareFrame failed");
            }

            if (!JxlFastLosslessProcessFrame(frameState, isLast: true, runnerOpaque, runner, null))
            {
                return Status.Failure("JxlFastLosslessProcessFrame failed");
            }

            if (!JxlFastLosslessPrepareHeader(frameState, addImageHeader: 1, isLast: 1))
            {
                return Status.Failure("JxlFastLosslessPrepareHeader failed");
            }

            nuint maxOut  = JxlFastLosslessMaxRequiredOutput(frameState);
            byte[] buffer = new byte[maxOut];
            nuint total   = 0;

            while (true)
            {
                nuint written = JxlFastLosslessWriteOutput(frameState, buffer.AsSpan((int)total));

                if (written == 0)
                {
                    break;
                }

                total += written;
            }

            byte[] result = new byte[total];
            Buffer.BlockCopy(buffer, 0, result, 0, (int)total);

            return result;
        }
    }
}
