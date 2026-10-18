// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    ///Incremental reader and decompressor for JXL-encoded ICC profiles.
    /// Ported from <c>jxl::ICCReader</c> in <c>lib/jxl/icc_codec.h</c> and <c>lib/jxl/icc_codec.cc</c>.
    /// </summary>
    public sealed class ICCReader
    {
        private nuint _i;
        private nuint _bitsToSkip;
        private nuint _usedBitsBase;
        private ulong _encSize;
        private byte[] _contextMap = Array.Empty<byte>();
        private readonly ANSCode _code = new ANSCode();
        private ANSSymbolReader _ansReader = new ANSSymbolReader();
        private readonly PaddedBytes _decompressed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ICCReader"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public ICCReader(JxlMemoryManager? memoryManager = null)
        {
            _decompressed = new PaddedBytes(memoryManager);
        }

        /// <summary>
        /// Initializes or resumes reading the ICC profile header and preamble from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Init(BitReader reader)
        {
            Status eoi = CheckEoi(reader);

            if (!eoi)
            {
                return eoi;
            }

            _usedBitsBase = reader.TotalBitsConsumed();

            if (_bitsToSkip == 0)
            {
                Status status = InitNewStream(reader);

                if (!status)
                {
                    return status;
                }

                _bitsToSkip = reader.TotalBitsConsumed() - _usedBitsBase;
            }
            else
            {
                reader.SkipBits(_bitsToSkip);
            }

            return true;
        }

        /// <summary>
        /// Processes the remaining ICC symbols from <paramref name="reader"/> and unpredicts into <paramref name="icc"/>.
        /// </summary>
        /// <param name="reader">Source <see cref="BitReader"/>.</param>
        /// <param name="icc">Output decompressed ICC profile bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Process(BitReader reader, PaddedBytes icc)
        {
            ANSSymbolReader.Checkpoint checkpoint = new ANSSymbolReader.Checkpoint();
            nuint savedI                          = 0;

            SaveCheckpoint(reader, checkpoint, ref savedI);

            for (; _i < (nuint)_encSize; _i++)
            {
                if (_i > 0 && (_i % (uint)ANSSymbolReader.KMaxCheckpointInterval) == 0)
                {
                    Status stepStatus = ProcessCheckpointStep(reader, checkpoint, ref savedI);

                    if (!stepStatus)
                    {
                        return stepStatus;
                    }
                }

                _decompressed[_i] = (byte)_ansReader.ReadHybridUint(
                    IccCodecCommon.ICCANSContext(_i, _decompressed[_i - 1u], _decompressed[_i - 2u]),
                    reader,
                    _contextMap);
            }

            Status finalCheck = CheckAndRestore(reader, checkpoint, savedI);

            if (!finalCheck)
            {
                return finalCheck;
            }

            _bitsToSkip = reader.TotalBitsConsumed() - _usedBitsBase;

            if (!_ansReader.CheckANSFinalState())
            {
                return Status.Failure("Corrupted ICC profile");
            }

            icc.Clear();

            return IccCodec.UnpredictICC(_decompressed.Span, icc);
        }

        /// <summary>
        /// Resets the reader state to start a new ICC stream.
        /// </summary>
        public void Reset()
        {
            _bitsToSkip = 0;
            _decompressed.Clear();
        }

        private static Status CheckEoi(BitReader reader)
        {
            if (reader.AllReadsWithinBounds())
            {
                return true;
            }

            return Status.NotEnoughBytes("Not enough bytes for reading ICC profile");
        }

        private Status InitNewStream(BitReader reader)
        {
            _encSize = U64Coder.Read(reader);

            if (_encSize > 268435456UL)
            {
                // Avoid too large memory allocation for invalid file.
                return Status.Failure("Too large encoded profile");
            }

            if (!DecAns.DecodeHistograms(
                _decompressed.MemoryManager,
                reader,
                IccCodecCommon.KNumIccContexts,
                _code,
                out _contextMap))
            {
                return false;
            }

            StatusOr<ANSSymbolReader> readerOr = ANSSymbolReader.Create(_code, reader);

            if (!readerOr.IsOk)
            {
                return readerOr.Status;
            }

            _ansReader       = readerOr.Value;
            _i               = 0;
            nuint initialCap = (nuint)Math.Min(_i + 0x400u, _encSize);

            if (!_decompressed.Resize(initialCap))
            {
                return false;
            }

            return ReadInitialPreamble(reader);
        }

        private Status ReadInitialPreamble(BitReader reader)
        {
            nuint firstLimit = (nuint)Math.Min(2UL, _encSize);

            for (; _i < firstLimit; _i++)
            {
                byte b1           = _i > 0 ? _decompressed[_i - 1u] : (byte)0;
                byte b2           = _i > 1 ? _decompressed[_i - 2u] : (byte)0;
                _decompressed[_i] = (byte)_ansReader.ReadHybridUint(
                    IccCodecCommon.ICCANSContext(_i, b1, b2),
                    reader,
                    _contextMap);
            }

            if (_encSize > IccCodec.KPreambleSize)
            {
                for (; _i < IccCodec.KPreambleSize; _i++)
                {
                    _decompressed[_i] = (byte)_ansReader.ReadHybridUint(
                        IccCodecCommon.ICCANSContext(_i, _decompressed[_i - 1u], _decompressed[_i - 2u]),
                        reader,
                        _contextMap);
                }

                Status eoi = CheckEoi(reader);

                if (!eoi)
                {
                    return eoi;
                }

                return IccCodec.CheckPreamble(_decompressed, (nuint)_encSize);
            }

            return true;
        }

        private void SaveCheckpoint(BitReader reader, ANSSymbolReader.Checkpoint checkpoint, ref nuint savedI)
        {
            _ansReader.Save(checkpoint);
            _bitsToSkip = reader.TotalBitsConsumed() - _usedBitsBase;
            savedI      = _i;
        }

        private Status CheckAndRestore(BitReader reader, ANSSymbolReader.Checkpoint checkpoint, nuint savedI)
        {
            Status status = CheckEoi(reader);

            if (!status)
            {
                _ansReader.Restore(checkpoint);
                _i = savedI;

                return status;
            }

            return true;
        }

        private Status ProcessCheckpointStep(BitReader reader, ANSSymbolReader.Checkpoint checkpoint, ref nuint savedI)
        {
            Status check = CheckAndRestore(reader, checkpoint, savedI);

            if (!check)
            {
                return check;
            }

            SaveCheckpoint(reader, checkpoint, ref savedI);

            if ((_i & 0xFFFFu) == 0)
            {
                float usedBytes = (reader.TotalBitsConsumed() - _usedBitsBase) / 8.0f;

                if (_i > usedBytes * 256.0f)
                {
                    return Status.Failure("Corrupted stream");
                }
            }

            nuint nextSize = (nuint)Math.Min(_i + 0x400u, _encSize);

            return _decompressed.Resize(nextSize);
        }
    }

    /// <summary>
    /// ICC profile prediction, unpredicting, and entropy stream encoding.
    /// Ported from <c>lib/jxl/icc_codec.cc</c> and <c>lib/jxl/enc_icc_codec.cc</c>.
    /// </summary>
    public static class IccCodec
    {
        /// <summary>Two base-128 varints at up to 10 bytes each (20).</summary>
        public const nuint KPreambleSize = 20;

        private const nuint KSizeLimit = uint.MaxValue >> 2;

        /// <summary>
        /// Mimics the beginning of <see cref="UnpredictICC"/> for a quick validity check.
        /// At least <see cref="KPreambleSize"/> bytes of <paramref name="data"/> should be valid.
        /// </summary>
        /// <param name="data">Decompressed prefix bytes.</param>
        /// <param name="encSize">Total expected encoded size.</param>
        /// <returns><see cref="Status.OkStatus"/> if preamble is valid.</returns>
        public static Status CheckPreamble(PaddedBytes data, nuint encSize)
        {
            ReadOnlySpan<byte> enc = data.Span;
            nuint size             = (nuint)enc.Length;
            nuint pos              = 0;

            if (!DecodeVarInt(enc, ref pos, out ulong osize) || !IccCodecCommon.CheckIs32Bit(osize))
            {
                return false;
            }

            if (!DecodeVarInt(enc, ref pos, out ulong csize) || !IccCodecCommon.CheckIs32Bit(csize))
            {
                return false;
            }

            if (!IccCodecCommon.CheckOutOfBounds(pos, csize, size))
            {
                return false;
            }

            if (osize + 65536UL < encSize)
            {
                return Status.Failure("Malformed ICC");
            }

            const ulong outputLimit = 1UL << 28;

            if (osize > outputLimit)
            {
                return Status.Failure("Decoded ICC is too large");
            }

            return true;
        }

        /// <summary>
        /// Decodes the result of <see cref="PredictICC"/> back to a valid ICC profile.
        /// Ported from <c>jxl::UnpredictICC</c> in <c>lib/jxl/icc_codec.cc</c>.
        /// </summary>
        /// <param name="enc">Predicted ICC stream.</param>
        /// <param name="result">Destination <see cref="PaddedBytes"/> (must be empty initially).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status UnpredictICC(ReadOnlySpan<byte> enc, PaddedBytes result)
        {
            if (result.Size != 0)
            {
                return Status.Failure("result must be empty initially");
            }

            nuint size = (nuint)enc.Length;
            nuint pos  = 0;

            if (!DecodeVarInt(enc, ref pos, out ulong osize) || !IccCodecCommon.CheckIs32Bit(osize))
            {
                return false;
            }

            if (!DecodeVarInt(enc, ref pos, out ulong csize) || !IccCodecCommon.CheckIs32Bit(csize))
            {
                return false;
            }

            nuint cpos = pos;

            if (!IccCodecCommon.CheckOutOfBounds(pos, csize, size))
            {
                return false;
            }

            nuint commandsEnd = cpos + (nuint)csize;
            pos               = commandsEnd;

            IccUnpredictState state = new IccUnpredictState
            {
                Enc         = enc,
                Osize       = osize,
                CommandsEnd = commandsEnd,
                Cpos        = cpos,
                Pos         = pos,
            };

            if (!UnpredictHeader(ref state, result, out bool finishedEarly))
            {
                return false;
            }

            if (finishedEarly)
            {
                return true;
            }

            if (!UnpredictTagList(ref state, result))
            {
                return false;
            }

            return UnpredictMainContent(ref state, result);
        }

        /// <summary>
        /// Transforms <paramref name="icc"/> into a predicted command+data representation.
        /// Ported from <c>jxl::PredictICC</c> in <c>lib/jxl/enc_icc_codec.cc</c>.
        /// </summary>
        /// <param name="icc">Source ICC profile bytes.</param>
        /// <param name="result">Destination <see cref="PaddedBytes"/> receiving the predicted stream.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status PredictICC(ReadOnlySpan<byte> icc, PaddedBytes result)
        {
            nuint size = (nuint)icc.Length;

            if (size > KSizeLimit)
            {
                return Status.Failure("ICC profile is too large");
            }

            if (!EncodeVarInt(size, result))
            {
                return false;
            }

            IccPredictState state = new IccPredictState(result.MemoryManager);
            byte[] header         = IccCodecCommon.ICCInitialHeaderPrediction((uint)size);

            for (nuint i = 0; i < (nuint)IccCodecCommon.KIccHeaderSize && i < size; i++)
            {
                IccCodecCommon.ICCPredictHeader(icc, size, header, i);

                if (!state.Data.PushBack(unchecked((byte)(icc[(int)i] - header[(int)i]))))
                {
                    return false;
                }
            }

            if (size <= (nuint)IccCodecCommon.KIccHeaderSize)
            {
                if (!EncodeVarInt(0, result))
                {
                    return false;
                }

                return result.Append(state.Data.Span);
            }

            state.Pos = IccCodecCommon.KIccHeaderSize;

            if (!PredictTagList(icc, state))
            {
                return false;
            }

            if (!PredictMainContent(icc, state))
            {
                return false;
            }

            if (!EncodeVarInt((ulong)state.Commands.Size, result) || !result.Append(state.Commands.Span))
            {
                return false;
            }

            return result.Append(state.Data.Span);
        }

        /// <summary>
        /// Encodes <paramref name="icc"/> into <paramref name="writer"/> using ICC prediction and prefix coding.
        /// Ported from <c>jxl::WriteICC</c> in <c>lib/jxl/enc_icc_codec.cc</c>.
        /// </summary>
        /// <param name="icc">Non-empty ICC profile bytes.</param>
        /// <param name="writer">Destination <see cref="BitWriter"/>.</param>
        /// <param name="layer">Statistics layer type.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteICC(
            ReadOnlySpan<byte> icc,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            if (icc.IsEmpty)
            {
                return Status.Failure("ICC must be non-empty");
            }

            PaddedBytes enc = new PaddedBytes(writer.MemoryManager);

            if (!PredictICC(icc, enc))
            {
                return false;
            }

            if (!writer.WithMaxBits(128, layer, auxOut, () => U64Coder.Write((ulong)enc.Size, writer)))
            {
                return false;
            }

            return WritePredictedIccTokens(enc.Span, writer, layer, auxOut);
        }

        private static Status WritePredictedIccTokens(
            ReadOnlySpan<byte> enc,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            int maxSymbol = 0;

            for (int i = 0; i < enc.Length; i++)
            {
                if (enc[i] > maxSymbol)
                {
                    maxSymbol = enc[i];
                }
            }

            int alphabetSize = maxSymbol + 1;
            uint[] histo     = new uint[alphabetSize];

            for (int i = 0; i < enc.Length; i++)
            {
                histo[enc[i]]++;
            }

            byte[] encCopy   = enc.ToArray();
            nuint maxOutBits = 512u + ((nuint)alphabetSize * 24u) + ((nuint)encCopy.Length * 16u);

            return writer.WithMaxBits(
                maxOutBits,
                layer,
                auxOut,
                () =>
                {
                    HybridUintConfig cfg = new HybridUintConfig(8, 0, 0);
                    writer.Write(1, 0); // LZ77 disabled
                    writer.Write(1, 1); // Simple context map
                    writer.Write(2, 0); // 0 bits per entry (1 histogram for all 41 contexts)
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

                    for (int i = 0; i < encCopy.Length; i++)
                    {
                        byte sym = encCopy[i];

                        if (depths[sym] > 0)
                        {
                            writer.Write(depths[sym], cwBits[sym]);
                        }
                    }

                    return true;
                });
        }

        private static void Shuffle(Span<byte> data, nuint width)
        {
            nuint size     = (nuint)data.Length;
            nuint height   = (size + width - 1u) / width;
            byte[] result  = new byte[(int)size];
            nuint s        = 0;
            nuint j        = 0;

            for (nuint i = 0; i < size; i++)
            {
                result[(int)i] = data[(int)j];
                j             += height;

                if (j >= size)
                {
                    j = ++s;
                }
            }

            result.CopyTo(data);
        }

        private static void Unshuffle(Span<byte> data, nuint width)
        {
            nuint size    = (nuint)data.Length;
            nuint height  = (size + width - 1u) / width;
            byte[] result = new byte[(int)size];
            nuint s       = 0;
            nuint j       = 0;

            for (nuint i = 0; i < size; i++)
            {
                result[(int)j] = data[(int)i];
                j             += height;

                if (j >= size)
                {
                    j = ++s;
                }
            }

            result.CopyTo(data);
        }

        private static Status DecodeVarInt(ReadOnlySpan<byte> input, ref nuint pos, out ulong value)
        {
            value      = 0;
            nuint size = (nuint)input.Length;

            // 9 bytes cover bits 0..62; the 10th byte may only contribute bit 63.
            for (int i = 0; i < 9; ++i)
            {
                if (pos >= size)
                {
                    return Status.Failure("DecodeVarInt: truncated input");
                }

                byte b  = input[(int)pos++];
                value  |= (ulong)(b & 0x7Fu) << (7 * i);

                if ((b & 0x80u) == 0)
                {
                    return true;
                }
            }

            if (pos >= size)
            {
                return Status.Failure("DecodeVarInt: truncated input (10th byte)");
            }

            byte tenth = input[(int)pos++];

            if ((tenth & 0x80u) != 0)
            {
                return Status.Failure("DecodeVarInt: varint exceeds 10 bytes");
            }

            if ((tenth & 0x7Eu) != 0)
            {
                return Status.Failure("DecodeVarInt: value exceeds 2^64 - 1");
            }

            value |= (ulong)(tenth & 0x01u) << 63;

            return true;
        }

        private static Status EncodeVarInt(ulong value, PaddedBytes data)
        {
            nuint pos = data.Size;

            if (!data.Resize(pos + 10u))
            {
                return false;
            }

            Span<byte> output = data.Span;

            while (value > 127)
            {
                output[(int)pos++] = (byte)((value & 127UL) | 128UL);
                value            >>= 7;
            }

            output[(int)pos++] = (byte)(value & 127UL);

            return data.Resize(pos);
        }

        private ref struct IccUnpredictState
        {
            public ReadOnlySpan<byte> Enc;
            public ulong Osize;
            public nuint CommandsEnd;
            public nuint Cpos;
            public nuint Pos;
            public ulong PrevTagStart;
            public ulong PrevTagSize;
        }

        private sealed class IccPredictState
        {
            public readonly Dictionary<nuint, int> TagMap = new Dictionary<nuint, int>();
            public readonly List<nuint> TagStarts = new List<nuint>();
            public readonly List<nuint> TagSizes = new List<nuint>();
            public readonly PaddedBytes Commands;
            public readonly PaddedBytes Data;
            public readonly PaddedBytes CommandsAdd;
            public readonly PaddedBytes DataAdd;
            public nuint Pos;
            public nuint Last0;
            public nuint Last1;
            public IccTag Tag;
            public nuint TagStart;
            public nuint TagSize;
            public nuint ClutStart;
            public ulong PrevTagStart;
            public uint PrevTagSize;

            public IccPredictState(JxlMemoryManager? memoryManager)
            {
                Commands    = new PaddedBytes(memoryManager);
                Data        = new PaddedBytes(memoryManager);
                CommandsAdd = new PaddedBytes(memoryManager);
                DataAdd     = new PaddedBytes(memoryManager);
            }
        }

        private static Status UnpredictHeader(ref IccUnpredictState state, PaddedBytes result, out bool finishedEarly)
        {
            finishedEarly = false;
            nuint size    = (nuint)state.Enc.Length;
            byte[] header = IccCodecCommon.ICCInitialHeaderPrediction((uint)state.Osize);

            for (nuint i = 0; i <= (nuint)IccCodecCommon.KIccHeaderSize; i++)
            {
                if ((ulong)result.Size == state.Osize)
                {
                    if (state.Cpos != state.CommandsEnd)
                    {
                        return Status.Failure("Not all commands used");
                    }

                    if (state.Pos != size)
                    {
                        return Status.Failure("Not all data used");
                    }

                    finishedEarly = true;

                    return true;
                }

                if (i == (nuint)IccCodecCommon.KIccHeaderSize)
                {
                    break;
                }

                IccCodecCommon.ICCPredictHeader(result.Span, (nuint)result.Size, header, i);

                if (state.Pos >= size)
                {
                    return Status.Failure("Out of bounds");
                }

                if (!result.PushBack(unchecked((byte)(state.Enc[(int)state.Pos++] + header[(int)i]))))
                {
                    return false;
                }
            }

            if (state.Cpos >= state.CommandsEnd)
            {
                return Status.Failure("Out of bounds");
            }

            return true;
        }

        private static Status UnpredictTagList(ref IccUnpredictState state, PaddedBytes result)
        {
            ReadOnlySpan<byte> cmdSlice = state.Enc.Slice(0, (int)state.CommandsEnd);

            if (!DecodeVarInt(cmdSlice, ref state.Cpos, out ulong numtags))
            {
                return false;
            }

            if (numtags == 0)
            {
                return true;
            }

            numtags--;

            if (!IccCodecCommon.CheckIs32Bit(numtags) || !IccCodecCommon.AppendUint32((uint)numtags, result))
            {
                return false;
            }

            state.PrevTagStart = (ulong)IccCodecCommon.KIccHeaderSize + (numtags * 12UL);
            state.PrevTagSize  = 0;

            for (;;)
            {
                if ((ulong)result.Size > state.Osize)
                {
                    return Status.Failure("Invalid result size");
                }

                if (state.Cpos > state.CommandsEnd)
                {
                    return Status.Failure("Out of bounds");
                }

                if (state.Cpos == state.CommandsEnd)
                {
                    break;
                }

                byte command = state.Enc[(int)state.Cpos++];
                byte tagcode = (byte)(command & 63u);

                if (tagcode == 0)
                {
                    break;
                }

                if (!UnpredictSingleTag(ref state, command, result))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status UnpredictSingleTag(ref IccUnpredictState state, byte command, PaddedBytes result)
        {
            byte tagcode = (byte)(command & 63u);

            if (!ResolveTagFromCode(state.Enc, tagcode, ref state.Pos, out IccTag tag) ||
                !IccCodecCommon.AppendKeyword(tag, result))
            {
                return false;
            }

            ulong tagsize = IsDefault20ByteXyzTag(tag) ? 20UL : state.PrevTagSize;
            ulong tagstart;
            ReadOnlySpan<byte> cmdSlice = state.Enc.Slice(0, (int)state.CommandsEnd);

            if ((command & IccCodecCommon.KFlagBitOffset) != 0)
            {
                if (!DecodeVarInt(cmdSlice, ref state.Cpos, out tagstart))
                {
                    return false;
                }
            }
            else
            {
                if (!IccCodecCommon.CheckIs32Bit(state.PrevTagStart))
                {
                    return false;
                }

                tagstart = state.PrevTagStart + state.PrevTagSize;
            }

            if (!IccCodecCommon.CheckIs32Bit(tagstart) || !IccCodecCommon.AppendUint32((uint)tagstart, result))
            {
                return false;
            }

            if ((command & IccCodecCommon.KFlagBitSize) != 0 &&
                !DecodeVarInt(cmdSlice, ref state.Cpos, out tagsize))
            {
                return false;
            }

            if (!IccCodecCommon.CheckIs32Bit(tagsize) || !IccCodecCommon.AppendUint32((uint)tagsize, result))
            {
                return false;
            }

            state.PrevTagStart = tagstart;
            state.PrevTagSize  = tagsize;

            return AppendExpandedMultiTag(tagcode, tagstart, tagsize, result);
        }

        private static bool IsDefault20ByteXyzTag(IccTag tag)
        {
            return tag == IccCodecCommon.KRxyzTag ||
                   tag == IccCodecCommon.KGxyzTag ||
                   tag == IccCodecCommon.KBxyzTag ||
                   tag == IccCodecCommon.KKxyzTag ||
                   tag == IccCodecCommon.KWtptTag ||
                   tag == IccCodecCommon.KBkptTag ||
                   tag == IccCodecCommon.KLumiTag;
        }

        private static Status ResolveTagFromCode(
            ReadOnlySpan<byte> enc,
            byte tagcode,
            ref nuint pos,
            out IccTag tag)
        {
            tag = default;

            if (tagcode == IccCodecCommon.KCommandTagUnknown)
            {
                if (!IccCodecCommon.CheckOutOfBounds(pos, 4, (ulong)enc.Length))
                {
                    return false;
                }

                tag  = IccCodecCommon.DecodeKeyword(enc, (nuint)enc.Length, pos);
                pos += 4;

                return true;
            }

            if (tagcode == IccCodecCommon.KCommandTagTrc)
            {
                tag = IccCodecCommon.KRtrcTag;

                return true;
            }

            if (tagcode == IccCodecCommon.KCommandTagXyz)
            {
                tag = IccCodecCommon.KRxyzTag;

                return true;
            }

            int idx = tagcode - IccCodecCommon.KCommandTagStringFirst;

            if (idx < 0 || idx >= IccCodecCommon.KNumTagStrings)
            {
                return Status.Failure("Unknown tagcode");
            }

            tag = IccCodecCommon.TagStrings[idx];

            return true;
        }

        private static Status AppendExpandedMultiTag(byte tagcode, ulong tagstart, ulong tagsize, PaddedBytes result)
        {
            if (tagcode == IccCodecCommon.KCommandTagTrc)
            {
                if (!IccCodecCommon.AppendKeyword(IccCodecCommon.KGtrcTag, result) ||
                    !IccCodecCommon.AppendUint32((uint)tagstart, result) ||
                    !IccCodecCommon.AppendUint32((uint)tagsize, result) ||
                    !IccCodecCommon.AppendKeyword(IccCodecCommon.KBtrcTag, result) ||
                    !IccCodecCommon.AppendUint32((uint)tagstart, result) ||
                    !IccCodecCommon.AppendUint32((uint)tagsize, result))
                {
                    return false;
                }
            }

            if (tagcode == IccCodecCommon.KCommandTagXyz)
            {
                if (!IccCodecCommon.CheckIs32Bit(tagstart + (tagsize * 2UL)))
                {
                    return false;
                }

                if (!IccCodecCommon.AppendKeyword(IccCodecCommon.KGxyzTag, result) ||
                    !IccCodecCommon.AppendUint32((uint)(tagstart + tagsize), result) ||
                    !IccCodecCommon.AppendUint32((uint)tagsize, result) ||
                    !IccCodecCommon.AppendKeyword(IccCodecCommon.KBxyzTag, result) ||
                    !IccCodecCommon.AppendUint32((uint)(tagstart + (tagsize * 2UL)), result) ||
                    !IccCodecCommon.AppendUint32((uint)tagsize, result))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status UnpredictMainContent(ref IccUnpredictState state, PaddedBytes result)
        {
            for (;;)
            {
                if ((ulong)result.Size > state.Osize)
                {
                    return Status.Failure("Invalid result size");
                }

                if (state.Cpos > state.CommandsEnd)
                {
                    return Status.Failure("Out of bounds");
                }

                if (state.Cpos == state.CommandsEnd)
                {
                    break;
                }

                byte command = state.Enc[(int)state.Cpos++];

                if (!ExecuteMainCommand(ref state, command, result))
                {
                    return false;
                }
            }

            if (state.Pos != (nuint)state.Enc.Length)
            {
                return Status.Failure("Not all data used");
            }

            if ((ulong)result.Size != state.Osize)
            {
                return Status.Failure("Invalid result size");
            }

            return true;
        }

        private static Status ExecuteMainCommand(ref IccUnpredictState state, byte command, PaddedBytes result)
        {
            if (command == IccCodecCommon.KCommandInsert)
            {
                return UnpredictInsertCommand(ref state, result);
            }

            if (command == IccCodecCommon.KCommandShuffle2 || command == IccCodecCommon.KCommandShuffle4)
            {
                return UnpredictShuffleCommand(ref state, command, result);
            }

            if (command == IccCodecCommon.KCommandPredict)
            {
                return UnpredictPredictCommand(ref state, result);
            }

            if (command == IccCodecCommon.KCommandXyz)
            {
                return UnpredictXyzCommand(state.Enc, ref state.Pos, result);
            }

            if (command >= IccCodecCommon.KCommandTypeStartFirst &&
                command < IccCodecCommon.KCommandTypeStartFirst + IccCodecCommon.KNumTypeStrings)
            {
                IccTag typeTag = IccCodecCommon.TypeStrings[command - IccCodecCommon.KCommandTypeStartFirst];

                if (!IccCodecCommon.AppendKeyword(typeTag, result))
                {
                    return false;
                }

                Span<byte> zeros = stackalloc byte[4];
                zeros.Clear();

                return result.Append(zeros);
            }

            return Status.Failure("Unknown command");
        }

        private static Status UnpredictInsertCommand(ref IccUnpredictState state, PaddedBytes result)
        {
            ReadOnlySpan<byte> cmdSlice = state.Enc.Slice(0, (int)state.CommandsEnd);

            if (!DecodeVarInt(cmdSlice, ref state.Cpos, out ulong num) ||
                !IccCodecCommon.CheckOutOfBounds(state.Pos, num, (ulong)state.Enc.Length))
            {
                return false;
            }

            if (!result.Append(state.Enc.Slice((int)state.Pos, (int)num)))
            {
                return false;
            }

            state.Pos += (nuint)num;

            return true;
        }

        private static Status UnpredictShuffleCommand(ref IccUnpredictState state, byte command, PaddedBytes result)
        {
            ReadOnlySpan<byte> cmdSlice = state.Enc.Slice(0, (int)state.CommandsEnd);

            if (!DecodeVarInt(cmdSlice, ref state.Cpos, out ulong num) ||
                !IccCodecCommon.CheckOutOfBounds(state.Pos, num, (ulong)state.Enc.Length))
            {
                return false;
            }

            byte[] shuffled = state.Enc.Slice((int)state.Pos, (int)num).ToArray();
            nuint width     = command == IccCodecCommon.KCommandShuffle2 ? (nuint)2 : 4;
            Shuffle(shuffled, width);

            if (!result.Append(shuffled))
            {
                return false;
            }

            state.Pos += (nuint)num;

            return true;
        }

        private static Status UnpredictPredictCommand(ref IccUnpredictState state, PaddedBytes result)
        {
            if (!IccCodecCommon.CheckOutOfBounds(state.Cpos, 2, state.CommandsEnd))
            {
                return false;
            }

            byte flags  = state.Enc[(int)state.Cpos++];
            nuint width = (nuint)(flags & 3u) + 1u;
            int order   = (flags & 12) >> 2;

            if (width == 3 || order == 3)
            {
                return Status.Failure("Invalid width or order");
            }

            ReadOnlySpan<byte> cmdSlice = state.Enc.Slice(0, (int)state.CommandsEnd);
            ulong stride                = width;

            if ((flags & 16u) != 0 &&
                (!DecodeVarInt(cmdSlice, ref state.Cpos, out stride) || stride < width))
            {
                return Status.Failure("Invalid stride");
            }

            if (result.Size == 0 || (((ulong)result.Size - 1UL) >> 2) < stride)
            {
                return Status.Failure("Invalid stride");
            }

            if (!DecodeVarInt(cmdSlice, ref state.Cpos, out ulong num) ||
                !IccCodecCommon.CheckOutOfBounds(state.Pos, num, (ulong)state.Enc.Length))
            {
                return false;
            }

            return ApplyLinearUnprediction(state.Enc, ref state.Pos, (nuint)stride, width, order, (nuint)num, result);
        }

        private static Status ApplyLinearUnprediction(
            ReadOnlySpan<byte> enc,
            ref nuint pos,
            nuint stride,
            nuint width,
            int order,
            nuint num,
            PaddedBytes result)
        {
            byte[] shuffled = enc.Slice((int)pos, (int)num).ToArray();

            if (width > 1)
            {
                Shuffle(shuffled, width);
            }

            nuint start = (nuint)result.Size;

            for (nuint i = 0; i < num; i++)
            {
                byte predicted = IccCodecCommon.LinearPredictICCValue(result.Span, start, i, stride, width, order);

                if (!result.PushBack(unchecked((byte)(predicted + shuffled[(int)i]))))
                {
                    return false;
                }
            }

            pos += num;

            return true;
        }

        private static Status UnpredictXyzCommand(ReadOnlySpan<byte> enc, ref nuint pos, PaddedBytes result)
        {
            if (!IccCodecCommon.AppendKeyword(IccCodecCommon.KXyzTag, result))
            {
                return false;
            }

            Span<byte> zeros = stackalloc byte[4];
            zeros.Clear();

            if (!result.Append(zeros) || !IccCodecCommon.CheckOutOfBounds(pos, 12, (ulong)enc.Length))
            {
                return false;
            }

            if (!result.Append(enc.Slice((int)pos, 12)))
            {
                return false;
            }

            pos += 12;

            return true;
        }

        private static Status PredictTagList(ReadOnlySpan<byte> icc, IccPredictState state)
        {
            nuint size = (nuint)icc.Length;

            if (state.Pos + 4u <= size)
            {
                ulong numtags = IccCodecCommon.DecodeUint32(icc, size, state.Pos);
                state.Pos    += 4;

                if (!EncodeVarInt(numtags + 1UL, state.Commands))
                {
                    return false;
                }

                state.PrevTagStart = (ulong)IccCodecCommon.KIccHeaderSize + (numtags * 12UL);
                state.PrevTagSize  = 0;

                for (nuint i = 0; i < (nuint)numtags; i++)
                {
                    if (state.Pos + 12u > size)
                    {
                        break;
                    }

                    if (!PredictSingleTagEntry(icc, state, ref i))
                    {
                        return false;
                    }
                }
            }

            return state.Commands.PushBack(0);
        }

        private static Status PredictSingleTagEntry(ReadOnlySpan<byte> icc, IccPredictState state, ref nuint i)
        {
            nuint size    = (nuint)icc.Length;
            IccTag tag    = IccCodecCommon.DecodeKeyword(icc, size, state.Pos);
            uint tagstart = IccCodecCommon.DecodeUint32(icc, size, state.Pos + 4u);
            uint tagsize  = IccCodecCommon.DecodeUint32(icc, size, state.Pos + 8u);
            state.Pos    += 12;

            state.TagStarts.Add(tagstart);
            state.TagSizes.Add(tagsize);
            state.TagMap[tagstart] = state.TagStarts.Count - 1;

            byte tagcode = LookupTagCode(tag);
            TryCollapseTrcOrXyzTag(icc, tag, tagstart, tagsize, ref state.Pos, ref i, ref tagcode);

            byte command            = tagcode;
            ulong predictedTagstart = state.PrevTagStart + state.PrevTagSize;
            nuint predictedTagsize  = IsDefault20ByteXyzTag(tag) ? 20u : state.PrevTagSize;

            if (predictedTagstart != tagstart)
            {
                command |= IccCodecCommon.KFlagBitOffset;
            }

            if (predictedTagsize != tagsize)
            {
                command |= IccCodecCommon.KFlagBitSize;
            }

            if (!state.Commands.PushBack(command))
            {
                return false;
            }

            if (tagcode == IccCodecCommon.KCommandTagUnknown && !IccCodecCommon.AppendKeyword(tag, state.Data))
            {
                return false;
            }

            if ((command & IccCodecCommon.KFlagBitOffset) != 0 && !EncodeVarInt(tagstart, state.Commands))
            {
                return false;
            }

            if ((command & IccCodecCommon.KFlagBitSize) != 0 && !EncodeVarInt(tagsize, state.Commands))
            {
                return false;
            }

            state.PrevTagStart = tagstart;
            state.PrevTagSize  = tagsize;

            return true;
        }

        private static byte LookupTagCode(IccTag tag)
        {
            for (int j = 0; j < IccCodecCommon.KNumTagStrings; j++)
            {
                if (tag == IccCodecCommon.TagStrings[j])
                {
                    return (byte)(j + IccCodecCommon.KCommandTagStringFirst);
                }
            }

            return IccCodecCommon.KCommandTagUnknown;
        }

        private static void TryCollapseTrcOrXyzTag(
            ReadOnlySpan<byte> icc,
            IccTag tag,
            uint tagstart,
            uint tagsize,
            ref nuint pos,
            ref nuint i,
            ref byte tagcode)
        {
            nuint size = (nuint)icc.Length;

            if (tag == IccCodecCommon.KRtrcTag && pos + 24u < size && IsCollapsibleTrcTriplet(icc, size, pos))
            {
                tagcode = IccCodecCommon.KCommandTagTrc;
                pos    += 24;
                i      += 2;
            }

            if (tag == IccCodecCommon.KRxyzTag && pos + 24u < size && IsCollapsibleXyzTriplet(icc, size, pos, tagstart, tagsize))
            {
                tagcode = IccCodecCommon.KCommandTagXyz;
                pos    += 24;
                i      += 2;
            }
        }

        private static bool IsCollapsibleTrcTriplet(ReadOnlySpan<byte> icc, nuint size, nuint pos)
        {
            if (IccCodecCommon.DecodeKeyword(icc, size, pos) != IccCodecCommon.KGtrcTag ||
                IccCodecCommon.DecodeKeyword(icc, size, pos + 12u) != IccCodecCommon.KBtrcTag)
            {
                return false;
            }

            for (nuint kk = 0; kk < 8; kk++)
            {
                if (icc[(int)(pos - 8u + kk)] != icc[(int)(pos + 4u + kk)] ||
                    icc[(int)(pos - 8u + kk)] != icc[(int)(pos + 16u + kk)])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCollapsibleXyzTriplet(
            ReadOnlySpan<byte> icc,
            nuint size,
            nuint pos,
            uint offsetr,
            uint sizer)
        {
            if (IccCodecCommon.DecodeKeyword(icc, size, pos) != IccCodecCommon.KGxyzTag ||
                IccCodecCommon.DecodeKeyword(icc, size, pos + 12u) != IccCodecCommon.KBxyzTag)
            {
                return false;
            }

            uint offsetg = IccCodecCommon.DecodeUint32(icc, size, pos + 4u);
            uint offsetb = IccCodecCommon.DecodeUint32(icc, size, pos + 16u);
            uint sizeg   = IccCodecCommon.DecodeUint32(icc, size, pos + 8u);
            uint sizeb   = IccCodecCommon.DecodeUint32(icc, size, pos + 20u);

            return sizer == 20u &&
                   sizeg == 20u &&
                   sizeb == 20u &&
                   offsetg == offsetr + 20u &&
                   offsetb == offsetr + 40u;
        }

        private static Status PredictMainContent(ReadOnlySpan<byte> icc, IccPredictState state)
        {
            nuint size  = (nuint)icc.Length;
            state.Last0 = state.Pos;

            while (state.Pos <= size)
            {
                state.Last1 = state.Pos;
                state.CommandsAdd.Clear();
                state.DataAdd.Clear();

                if (state.Pos > state.TagStart + state.TagSize && state.TagSize < KSizeLimit)
                {
                    state.Tag = default;
                }

                if (!PredictStepAtPosition(icc, state))
                {
                    return false;
                }

                if (!FlushMainStep(icc, size, state))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status PredictStepAtPosition(ReadOnlySpan<byte> icc, IccPredictState state)
        {
            if (state.TagMap.TryGetValue(state.Pos, out int index) && state.Pos + 4u <= (nuint)icc.Length)
            {
                state.Tag      = IccCodecCommon.DecodeKeyword(icc, (nuint)icc.Length, state.Pos);
                state.TagStart = state.TagStarts[index];
                state.TagSize  = state.TagSizes[index];

                if (!TryPredictMlucOrCurvAtTagStart(icc, ref state.Pos, state.Tag, state.TagSize, state.CommandsAdd, state.DataAdd))
                {
                    return false;
                }
            }

            if ((state.Tag == IccCodecCommon.KMabTag || state.Tag == IccCodecCommon.KMbaTag) &&
                !TryPredictMabMbaBlock(icc, state))
            {
                return false;
            }

            return TryPredictGenericBlock(icc, ref state.Pos, state.Tag, state.TagStart, state.TagSize, state.CommandsAdd, state.DataAdd);
        }

        private static Status TryPredictMlucOrCurvAtTagStart(
            ReadOnlySpan<byte> icc,
            ref nuint pos,
            IccTag tag,
            nuint tagsize,
            PaddedBytes commandsAdd,
            PaddedBytes dataAdd)
        {
            nuint size   = (nuint)icc.Length;
            bool tagSane = tagsize > 8u && tagsize < KSizeLimit;

            if (!tagSane || pos + tagsize > size || IccCodecCommon.DecodeUint32(icc, size, pos + 4u) != 0)
            {
                return true;
            }

            nuint num = tagsize - 8u;

            if (tag == IccCodecCommon.KMlucTag)
            {
                if (!commandsAdd.PushBack(IccCodecCommon.KCommandTypeStartFirst + 3) ||
                    !commandsAdd.PushBack(IccCodecCommon.KCommandShuffle2) ||
                    !EncodeVarInt(num, commandsAdd))
                {
                    return false;
                }

                pos      += 8;
                int start = (int)dataAdd.Size;

                if (!dataAdd.Append(icc.Slice((int)pos, (int)num)))
                {
                    return false;
                }

                pos += num;
                Unshuffle(dataAdd.Span.Slice(start, (int)num), 2);

                return true;
            }

            if (tag == IccCodecCommon.KCurvTag && num > 16u && num < (1u << 28) && pos + 8u + num <= size && pos > 0)
            {
                if (!commandsAdd.PushBack(IccCodecCommon.KCommandTypeStartFirst + 5) ||
                    !commandsAdd.PushBack(IccCodecCommon.KCommandPredict) ||
                    !commandsAdd.PushBack((1 << 2) | (2 - 1)) ||
                    !EncodeVarInt(num, commandsAdd))
                {
                    return false;
                }

                pos += 8;

                return PredictAndShuffle(2, 2, 1, num, icc, ref pos, dataAdd);
            }

            return true;
        }

        private static Status TryPredictMabMbaBlock(ReadOnlySpan<byte> icc, IccPredictState state)
        {
            nuint size    = (nuint)icc.Length;
            IccTag subTag = IccCodecCommon.DecodeKeyword(icc, size, state.Pos);

            if (state.Pos + 12u < size &&
                (subTag == IccCodecCommon.KCurvTag || subTag == IccCodecCommon.KVcgtTag) &&
                IccCodecCommon.DecodeUint32(icc, size, state.Pos + 4u) == 0)
            {
                nuint num = IccCodecCommon.DecodeUint32(icc, size, state.Pos + 8u) * 2u;

                if (num > 16u && num < (1u << 28) && state.Pos + 12u + num <= size)
                {
                    state.Pos  += 12;
                    state.Last1 = state.Pos;

                    if (!state.CommandsAdd.PushBack(IccCodecCommon.KCommandPredict) ||
                        !state.CommandsAdd.PushBack((1 << 2) | (2 - 1)) ||
                        !EncodeVarInt(num, state.CommandsAdd) ||
                        !PredictAndShuffle(2, 2, 1, num, icc, ref state.Pos, state.DataAdd))
                    {
                        return false;
                    }
                }
            }

            if (state.Pos == state.TagStart + 24u && state.Pos + 4u < size)
            {
                state.ClutStart = state.TagStart + IccCodecCommon.DecodeUint32(icc, size, state.Pos);
            }

            if (state.Pos == state.ClutStart && state.ClutStart + 16u < size)
            {
                return TryPredictClutBlock(icc, ref state.Pos, state.TagStart, state.ClutStart, state.CommandsAdd, state.DataAdd);
            }

            return true;
        }

        private static Status TryPredictClutBlock(
            ReadOnlySpan<byte> icc,
            ref nuint pos,
            nuint tagstart,
            nuint clutstart,
            PaddedBytes commandsAdd,
            PaddedBytes dataAdd)
        {
            nuint size   = (nuint)icc.Length;
            nuint numi   = icc[(int)(tagstart + 8u)];
            nuint numo   = icc[(int)(tagstart + 9u)];
            nuint width  = icc[(int)(clutstart + 16u)];
            nuint stride = width * numo;
            nuint num    = stride;

            for (nuint i = 0; i < numi && clutstart + i < size; i++)
            {
                num *= icc[(int)(clutstart + i)];
            }

            if ((width == 1u || width == 2u) &&
                num > 64u &&
                num < (1u << 28) &&
                pos + num <= size &&
                pos > stride * 4u)
            {
                byte flags = (byte)((1 << 2) | ((int)width - 1) | (stride == width ? 0 : 16));

                if (!commandsAdd.PushBack(IccCodecCommon.KCommandPredict) ||
                    !commandsAdd.PushBack(flags))
                {
                    return false;
                }

                if ((flags & 16u) != 0 && !EncodeVarInt(stride, commandsAdd))
                {
                    return false;
                }

                if (!EncodeVarInt(num, commandsAdd))
                {
                    return false;
                }

                return PredictAndShuffle(stride, width, 1, num, icc, ref pos, dataAdd);
            }

            return true;
        }

        private static Status TryPredictGenericBlock(
            ReadOnlySpan<byte> icc,
            ref nuint pos,
            IccTag tag,
            nuint tagstart,
            nuint tagsize,
            PaddedBytes commandsAdd,
            PaddedBytes dataAdd)
        {
            nuint size   = (nuint)icc.Length;
            bool tagSane = tagsize > 8u && tagsize < KSizeLimit;

            if (commandsAdd.Size == 0 && dataAdd.Size == 0 &&
                tag == IccCodecCommon.KGbdTag && tagSane &&
                pos == tagstart + 8u && pos + tagsize - 8u <= size && pos > 16u)
            {
                nuint num  = tagsize - 8u;
                byte flags = 3; // order = 0, width = 4, stride == width

                if (!commandsAdd.PushBack(IccCodecCommon.KCommandPredict) ||
                    !commandsAdd.PushBack(flags) ||
                    !EncodeVarInt(num, commandsAdd))
                {
                    return false;
                }

                return PredictAndShuffle(4, 4, 0, num, icc, ref pos, dataAdd);
            }

            if (commandsAdd.Size == 0 && dataAdd.Size == 0 && pos + 20u <= size)
            {
                IccTag subTag = IccCodecCommon.DecodeKeyword(icc, size, pos);

                if (subTag == IccCodecCommon.KXyzTag && IccCodecCommon.DecodeUint32(icc, size, pos + 4u) == 0)
                {
                    if (!commandsAdd.PushBack(IccCodecCommon.KCommandXyz) ||
                        !dataAdd.Append(icc.Slice((int)(pos + 8u), 12)))
                    {
                        return false;
                    }

                    pos += 20;

                    return true;
                }
            }

            return TryPredictTypeStart(icc, ref pos, commandsAdd, dataAdd);
        }

        private static Status TryPredictTypeStart(
            ReadOnlySpan<byte> icc,
            ref nuint pos,
            PaddedBytes commandsAdd,
            PaddedBytes dataAdd)
        {
            nuint size = (nuint)icc.Length;

            if (commandsAdd.Size == 0 && dataAdd.Size == 0 && pos + 8u <= size &&
                IccCodecCommon.DecodeUint32(icc, size, pos + 4u) == 0)
            {
                IccTag subTag = IccCodecCommon.DecodeKeyword(icc, size, pos);

                for (int i = 0; i < IccCodecCommon.KNumTypeStrings; i++)
                {
                    if (subTag == IccCodecCommon.TypeStrings[i])
                    {
                        if (!commandsAdd.PushBack((byte)(IccCodecCommon.KCommandTypeStartFirst + i)))
                        {
                            return false;
                        }

                        pos += 8;

                        break;
                    }
                }
            }

            return true;
        }

        private static Status FlushMainStep(ReadOnlySpan<byte> icc, nuint size, IccPredictState state)
        {
            bool hasAdded = state.CommandsAdd.Size != 0 || state.DataAdd.Size != 0;

            if (hasAdded || state.Pos == size)
            {
                if (state.Last0 < state.Last1)
                {
                    nuint insertLen = state.Last1 - state.Last0;

                    if (!state.Commands.PushBack(IccCodecCommon.KCommandInsert) ||
                        !EncodeVarInt(insertLen, state.Commands) ||
                        !state.Data.Append(icc.Slice((int)state.Last0, (int)insertLen)))
                    {
                        return false;
                    }

                    state.Last0 = state.Last1;
                }

                if (!state.Commands.Append(state.CommandsAdd.Span) || !state.Data.Append(state.DataAdd.Span))
                {
                    return false;
                }

                state.Last0 = state.Pos;
            }

            if (!hasAdded)
            {
                state.Pos++;
            }

            return true;
        }

        private static Status PredictAndShuffle(
            nuint stride,
            nuint width,
            int order,
            nuint num,
            ReadOnlySpan<byte> data,
            ref nuint pos,
            PaddedBytes result)
        {
            if (!IccCodecCommon.CheckOutOfBounds(pos, num, (ulong)data.Length))
            {
                return false;
            }

            if (pos == 0 || ((pos - 1u) >> 2) < stride || pos < stride * 4u)
            {
                return Status.Failure("Invalid stride");
            }

            int start = (int)result.Size;

            for (nuint i = 0; i < num; i++)
            {
                byte predicted = IccCodecCommon.LinearPredictICCValue(data, pos, i, stride, width, order);

                if (!result.PushBack(unchecked((byte)(data[(int)(pos + i)] - predicted))))
                {
                    return false;
                }
            }

            pos += num;

            if (width > 1)
            {
                Unshuffle(result.Span.Slice(start, (int)num), width);
            }

            return true;
        }
    }
}
