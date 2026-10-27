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
    /// 4-byte ICC tag value.
    /// Ported from <c>jxl::Tag</c> in <c>lib/jxl/icc_codec_common.h</c>.
    /// </summary>
    public readonly struct IccTag : IEquatable<IccTag>
    {
        /// <summary>First tag byte.</summary>
        public readonly byte B0;

        /// <summary>Second tag byte.</summary>
        public readonly byte B1;

        /// <summary>Third tag byte.</summary>
        public readonly byte B2;

        /// <summary>Fourth tag byte.</summary>
        public readonly byte B3;

        /// <summary>
        /// Initializes a new instance of the <see cref="IccTag"/> struct from 4 bytes.
        /// </summary>
        /// <param name="b0">First byte.</param>
        /// <param name="b1">Second byte.</param>
        /// <param name="b2">Third byte.</param>
        /// <param name="b3">Fourth byte.</param>
        public IccTag(byte b0, byte b1, byte b2, byte b3)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            B3 = b3;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IccTag"/> struct from 4 ASCII characters.
        /// </summary>
        /// <param name="c0">First character.</param>
        /// <param name="c1">Second character.</param>
        /// <param name="c2">Third character.</param>
        /// <param name="c3">Fourth character.</param>
        public IccTag(char c0, char c1, char c2, char c3)
        {
            B0 = (byte)c0;
            B1 = (byte)c1;
            B2 = (byte)c2;
            B3 = (byte)c3;
        }

        /// <summary>Gets the byte at <paramref name="index"/> (<c>0..3</c>).</summary>
        public byte this[int index] => index switch
        {
            0 => B0,
            1 => B1,
            2 => B2,
            _ => B3,
        };

        /// <inheritdoc />
        public bool Equals(IccTag other) => B0 == other.B0 && B1 == other.B1 && B2 == other.B2 && B3 == other.B3;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is IccTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(B0, B1, B2, B3);

        /// <summary>Checks equality of two <see cref="IccTag"/> values.</summary>
        public static bool operator ==(IccTag left, IccTag right) => left.Equals(right);

        /// <summary>Checks inequality of two <see cref="IccTag"/> values.</summary>
        public static bool operator !=(IccTag left, IccTag right) => !left.Equals(right);
    }

    /// <summary>
    /// Shared constants and prediction helpers for ICC profile compression and decompression.
    /// Ported from <c>lib/jxl/icc_codec_common.h</c> and <c>lib/jxl/icc_codec_common.cc</c>.
    /// </summary>
    public static class IccCodecCommon
    {
        /// <summary>ICC profile header size in bytes (128).</summary>
        public const int KIccHeaderSize = 128;

        /// <summary>Number of standard ICC tag strings (17).</summary>
        public const int KNumTagStrings = 17;

        /// <summary>Tag command for unknown 4-byte tag.</summary>
        public const byte KCommandTagUnknown = 1;

        /// <summary>Tag command for rTRC/gTRC/bTRC triplet.</summary>
        public const byte KCommandTagTrc = 2;

        /// <summary>Tag command for rXYZ/gXYZ/bXYZ triplet.</summary>
        public const byte KCommandTagXyz = 3;

        /// <summary>First tag command corresponding to <see cref="TagStrings"/>.</summary>
        public const byte KCommandTagStringFirst = 4;

        /// <summary>Number of standard ICC type strings (8).</summary>
        public const int KNumTypeStrings = 8;

        /// <summary>Content command for literal byte insertion.</summary>
        public const byte KCommandInsert = 1;

        /// <summary>Content command for 2-byte shuffle.</summary>
        public const byte KCommandShuffle2 = 2;

        /// <summary>Content command for 4-byte shuffle.</summary>
        public const byte KCommandShuffle4 = 3;

        /// <summary>Content command for linear prediction + shuffle.</summary>
        public const byte KCommandPredict = 4;

        /// <summary>Content command for 20-byte XYZ tag payload.</summary>
        public const byte KCommandXyz = 10;

        /// <summary>First content command corresponding to <see cref="TypeStrings"/>.</summary>
        public const byte KCommandTypeStartFirst = 16;

        /// <summary>Tag command flag bit indicating an explicit offset varint.</summary>
        public const byte KFlagBitOffset = 64;

        /// <summary>Tag command flag bit indicating an explicit size varint.</summary>
        public const byte KFlagBitSize = 128;

        /// <summary>Number of ANS contexts used for ICC streams (41).</summary>
        public const int KNumIccContexts = 41;

        /// <summary>Tag <c>acsp</c>.</summary>
        public static readonly IccTag KAcspTag = new IccTag('a', 'c', 's', 'p');

        /// <summary>Tag <c>bkpt</c>.</summary>
        public static readonly IccTag KBkptTag = new IccTag('b', 'k', 'p', 't');

        /// <summary>Tag <c>bTRC</c>.</summary>
        public static readonly IccTag KBtrcTag = new IccTag('b', 'T', 'R', 'C');

        /// <summary>Tag <c>bXYZ</c>.</summary>
        public static readonly IccTag KBxyzTag = new IccTag('b', 'X', 'Y', 'Z');

        /// <summary>Tag <c>chad</c>.</summary>
        public static readonly IccTag KChadTag = new IccTag('c', 'h', 'a', 'd');

        /// <summary>Tag <c>chrm</c>.</summary>
        public static readonly IccTag KChrmTag = new IccTag('c', 'h', 'r', 'm');

        /// <summary>Tag <c>cprt</c>.</summary>
        public static readonly IccTag KCprtTag = new IccTag('c', 'p', 'r', 't');

        /// <summary>Tag <c>curv</c>.</summary>
        public static readonly IccTag KCurvTag = new IccTag('c', 'u', 'r', 'v');

        /// <summary>Tag <c>desc</c>.</summary>
        public static readonly IccTag KDescTag = new IccTag('d', 'e', 's', 'c');

        /// <summary>Tag <c>dmdd</c>.</summary>
        public static readonly IccTag KDmddTag = new IccTag('d', 'm', 'd', 'd');

        /// <summary>Tag <c>dmnd</c>.</summary>
        public static readonly IccTag KDmndTag = new IccTag('d', 'm', 'n', 'd');

        /// <summary>Tag <c>gbd </c>.</summary>
        public static readonly IccTag KGbdTag = new IccTag('g', 'b', 'd', ' ');

        /// <summary>Tag <c>gTRC</c>.</summary>
        public static readonly IccTag KGtrcTag = new IccTag('g', 'T', 'R', 'C');

        /// <summary>Tag <c>gXYZ</c>.</summary>
        public static readonly IccTag KGxyzTag = new IccTag('g', 'X', 'Y', 'Z');

        /// <summary>Tag <c>kTRC</c>.</summary>
        public static readonly IccTag KKtrcTag = new IccTag('k', 'T', 'R', 'C');

        /// <summary>Tag <c>kXYZ</c>.</summary>
        public static readonly IccTag KKxyzTag = new IccTag('k', 'X', 'Y', 'Z');

        /// <summary>Tag <c>lumi</c>.</summary>
        public static readonly IccTag KLumiTag = new IccTag('l', 'u', 'm', 'i');

        /// <summary>Tag <c>mAB </c>.</summary>
        public static readonly IccTag KMabTag = new IccTag('m', 'A', 'B', ' ');

        /// <summary>Tag <c>mBA </c>.</summary>
        public static readonly IccTag KMbaTag = new IccTag('m', 'B', 'A', ' ');

        /// <summary>Tag <c>mluc</c>.</summary>
        public static readonly IccTag KMlucTag = new IccTag('m', 'l', 'u', 'c');

        /// <summary>Tag <c>mntr</c>.</summary>
        public static readonly IccTag KMntrTag = new IccTag('m', 'n', 't', 'r');

        /// <summary>Tag <c>para</c>.</summary>
        public static readonly IccTag KParaTag = new IccTag('p', 'a', 'r', 'a');

        /// <summary>Tag <c>RGB </c>.</summary>
        public static readonly IccTag KRgbTag = new IccTag('R', 'G', 'B', ' ');

        /// <summary>Tag <c>rTRC</c>.</summary>
        public static readonly IccTag KRtrcTag = new IccTag('r', 'T', 'R', 'C');

        /// <summary>Tag <c>rXYZ</c>.</summary>
        public static readonly IccTag KRxyzTag = new IccTag('r', 'X', 'Y', 'Z');

        /// <summary>Tag <c>sf32</c>.</summary>
        public static readonly IccTag KSf32Tag = new IccTag('s', 'f', '3', '2');

        /// <summary>Tag <c>text</c>.</summary>
        public static readonly IccTag KTextTag = new IccTag('t', 'e', 'x', 't');

        /// <summary>Tag <c>vcgt</c>.</summary>
        public static readonly IccTag KVcgtTag = new IccTag('v', 'c', 'g', 't');

        /// <summary>Tag <c>wtpt</c>.</summary>
        public static readonly IccTag KWtptTag = new IccTag('w', 't', 'p', 't');

        /// <summary>Tag <c>XYZ </c>.</summary>
        public static readonly IccTag KXyzTag = new IccTag('X', 'Y', 'Z', ' ');

        /// <summary>Standard ICC tag strings table.</summary>
        public static readonly IccTag[] TagStrings =
        {
            KCprtTag, KWtptTag, KBkptTag, KRxyzTag, KGxyzTag, KBxyzTag,
            KKxyzTag, KRtrcTag, KGtrcTag, KBtrcTag, KKtrcTag, KChadTag,
            KDescTag, KChrmTag, KDmndTag, KDmddTag, KLumiTag,
        };

        /// <summary>Standard ICC type strings table.</summary>
        public static readonly IccTag[] TypeStrings =
        {
            KXyzTag, KDescTag, KTextTag, KMlucTag,
            KParaTag, KCurvTag, KSf32Tag, KGbdTag,
        };

        private static readonly byte[] InitialHeaderPredictionTemplate =
        {
            0,          0,          0,          0,          0,          0,          0,          0,
            4,          0,          0,          0,          (byte)'m',  (byte)'n',  (byte)'t',  (byte)'r',
            (byte)'R',  (byte)'G',  (byte)'B',  (byte)' ',  (byte)'X',  (byte)'Y',  (byte)'Z',  (byte)' ',
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          (byte)'a',  (byte)'c',  (byte)'s',  (byte)'p',
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          246,        214,
            0,          1,          0,          0,          0,          0,          211,        45,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
            0,          0,          0,          0,          0,          0,          0,          0,
        };

        /// <summary>
        /// Decodes a big-endian 32-bit unsigned integer at <paramref name="pos"/>, or returns 0 if out of bounds.
        /// </summary>
        /// <param name="data">Source byte span.</param>
        /// <param name="size">Valid byte count in <paramref name="data"/>.</param>
        /// <param name="pos">Byte offset.</param>
        /// <returns>Decoded 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint DecodeUint32(ReadOnlySpan<byte> data, nuint size, nuint pos)
        {
            return pos + 4u > size ? 0u : ByteOrder.LoadBE32(data.Slice((int)pos, 4));
        }

        /// <summary>
        /// Appends a big-endian 32-bit unsigned integer to <paramref name="data"/>.
        /// </summary>
        /// <param name="value">Value to append.</param>
        /// <param name="data">Destination <see cref="PaddedBytes"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status AppendUint32(uint value, PaddedBytes data)
        {
            nuint pos = data.Size;

            if (!data.Resize(pos + 4u))
            {
                return false;
            }

            ByteOrder.StoreBE32(value, data.Span.Slice((int)pos, 4));

            return true;
        }

        /// <summary>
        /// Decodes a 4-byte <see cref="IccTag"/> at <paramref name="pos"/>, or returns 4 spaces if out of bounds.
        /// </summary>
        /// <param name="data">Source byte span.</param>
        /// <param name="size">Valid byte count in <paramref name="data"/>.</param>
        /// <param name="pos">Byte offset.</param>
        /// <returns>Decoded <see cref="IccTag"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IccTag DecodeKeyword(ReadOnlySpan<byte> data, nuint size, nuint pos)
        {
            if (pos + 4u > size)
            {
                return new IccTag(' ', ' ', ' ', ' ');
            }

            int p = (int)pos;

            return new IccTag(data[p], data[p + 1], data[p + 2], data[p + 3]);
        }

        /// <summary>
        /// Writes a 4-byte <see cref="IccTag"/> into <paramref name="data"/> at <paramref name="pos"/>.
        /// </summary>
        /// <param name="keyword">Tag to write.</param>
        /// <param name="data">Destination byte span.</param>
        /// <param name="size">Valid byte count in <paramref name="data"/>.</param>
        /// <param name="pos">Byte offset.</param>
        public static void EncodeKeyword(IccTag keyword, Span<byte> data, nuint size, nuint pos)
        {
            if (pos + 3u >= size)
            {
                return;
            }

            int p       = (int)pos;
            data[p]     = keyword.B0;
            data[p + 1] = keyword.B1;
            data[p + 2] = keyword.B2;
            data[p + 3] = keyword.B3;
        }

        /// <summary>
        /// Appends a 4-byte <see cref="IccTag"/> to <paramref name="data"/>.
        /// </summary>
        /// <param name="keyword">Tag to append.</param>
        /// <param name="data">Destination <see cref="PaddedBytes"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status AppendKeyword(IccTag keyword, PaddedBytes data)
        {
            Span<byte> buf = stackalloc byte[4] { keyword.B0, keyword.B1, keyword.B2, keyword.B3 };

            return data.Append(buf);
        }

        /// <summary>
        /// Checks whether <c>a + b &gt; size</c>, accounting for 64-bit unsigned overflow.
        /// </summary>
        /// <param name="a">Start offset.</param>
        /// <param name="b">Length.</param>
        /// <param name="size">Total size.</param>
        /// <returns><see cref="Status.OkStatus"/> if within bounds.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Status CheckOutOfBounds(ulong a, ulong b, ulong size)
        {
            ulong pos = unchecked(a + b);

            if (pos > size || pos < a)
            {
                return Status.Failure("Out of bounds");
            }

            return true;
        }

        /// <summary>
        /// Checks whether <paramref name="v"/> fits in a 32-bit unsigned integer.
        /// </summary>
        /// <param name="v">64-bit unsigned value.</param>
        /// <returns><see cref="Status.OkStatus"/> if <c>v &lt;= 0xFFFFFFFF</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Status CheckIs32Bit(ulong v)
        {
            const ulong kUpper32 = ~0xFFFFFFFFUL;

            if ((v & kUpper32) != 0)
            {
                return Status.Failure("32-bit value expected");
            }

            return true;
        }

        /// <summary>
        /// Returns the 128-byte initial ICC header prediction for a profile of <paramref name="size"/> bytes.
        /// </summary>
        /// <param name="size">Uncompressed ICC profile size.</param>
        /// <returns>128-byte predicted header array.</returns>
        public static byte[] ICCInitialHeaderPrediction(uint size)
        {
            byte[] copy = (byte[])InitialHeaderPredictionTemplate.Clone();
            ByteOrder.StoreBE32(size, copy);

            return copy;
        }

        /// <summary>
        /// Updates <paramref name="header"/> predictions at position <paramref name="pos"/> based on already decoded <paramref name="icc"/> bytes.
        /// </summary>
        /// <param name="icc">Decoded ICC bytes so far.</param>
        /// <param name="size">Number of valid bytes in <paramref name="icc"/>.</param>
        /// <param name="header">Mutable 128-byte header prediction buffer.</param>
        /// <param name="pos">Current header byte index.</param>
        public static void ICCPredictHeader(ReadOnlySpan<byte> icc, nuint size, Span<byte> header, nuint pos)
        {
            if (pos == 8 && size >= 8)
            {
                header[80] = icc[4];
                header[81] = icc[5];
                header[82] = icc[6];
                header[83] = icc[7];
            }

            if (pos == 41 && size >= 41)
            {
                if (icc[40] == (byte)'A')
                {
                    header[41] = (byte)'P';
                    header[42] = (byte)'P';
                    header[43] = (byte)'L';
                }

                if (icc[40] == (byte)'M')
                {
                    header[41] = (byte)'S';
                    header[42] = (byte)'F';
                    header[43] = (byte)'T';
                }
            }

            if (pos == 42 && size >= 42)
            {
                if (icc[40] == (byte)'S' && icc[41] == (byte)'G')
                {
                    header[42] = (byte)'I';
                    header[43] = (byte)' ';
                }

                if (icc[40] == (byte)'S' && icc[41] == (byte)'U')
                {
                    header[42] = (byte)'N';
                    header[43] = (byte)'W';
                }
            }
        }

        /// <summary>
        /// Predicts an ICC byte value using linear prediction of <paramref name="order"/> (0..2).
        /// </summary>
        /// <param name="data">ICC data buffer.</param>
        /// <param name="start">Start offset of the predicted block.</param>
        /// <param name="i">Byte index within the predicted block.</param>
        /// <param name="stride">Byte stride between consecutive values.</param>
        /// <param name="width">Element width in bytes (1, 2, or 4).</param>
        /// <param name="order">Prediction order (0, 1, or 2).</param>
        /// <returns>Predicted byte value.</returns>
        public static byte LinearPredictICCValue(
            ReadOnlySpan<byte> data,
            nuint start,
            nuint i,
            nuint stride,
            nuint width,
            int order)
        {
            nuint pos = start + i;

            if (width == 1)
            {
                byte p1 = data[(int)(pos - stride)];
                byte p2 = data[(int)(pos - (stride * 2u))];
                byte p3 = data[(int)(pos - (stride * 3u))];

                return PredictByte(p1, p2, p3, order);
            }

            if (width == 2)
            {
                nuint p     = start + (i & ~1u);
                int idx1    = (int)(p - stride);
                int idx2    = (int)(p - (stride * 2u));
                int idx3    = (int)(p - (stride * 3u));
                ushort p1   = (ushort)((data[idx1] << 8) + data[idx1 + 1]);
                ushort p2   = (ushort)((data[idx2] << 8) + data[idx2 + 1]);
                ushort p3   = (ushort)((data[idx3] << 8) + data[idx3 + 1]);
                ushort pred = PredictUshort(p1, p2, p3, order);

                return (i & 1u) != 0 ? (byte)(pred & 255) : (byte)((pred >> 8) & 255);
            }

            nuint p4       = start + (i & ~3u);
            uint u1        = DecodeUint32(data, pos, p4 - stride);
            uint u2        = DecodeUint32(data, pos, p4 - (stride * 2u));
            uint u3        = DecodeUint32(data, pos, p4 - (stride * 3u));
            uint pred32    = PredictUint(u1, u2, u3, order);
            int shiftbytes = 3 - (int)(i & 3u);

            return (byte)((pred32 >> (shiftbytes * 8)) & 255u);
        }

        /// <summary>
        /// Computes the ANS context index in <c>[0, 41)</c> for ICC byte index <paramref name="i"/>.
        /// </summary>
        /// <param name="i">Current byte index.</param>
        /// <param name="b1">Previous byte (<c>i - 1</c>).</param>
        /// <param name="b2">Second previous byte (<c>i - 2</c>).</param>
        /// <returns>ANS context index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint ICCANSContext(nuint i, byte b1, byte b2)
        {
            if (i <= KIccHeaderSize)
            {
                return 0;
            }

            return 1u + ByteKind1(b1) + ((nuint)ByteKind2(b2) * 8u);
        }

        private static byte ByteKind1(byte b)
        {
            if ((b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z'))
            {
                return 0;
            }

            if ((b >= (byte)'0' && b <= (byte)'9') || b == (byte)'.' || b == (byte)',')
            {
                return 1;
            }

            if (b < 16)
            {
                return b <= 1 ? (byte)(b + 2) : (byte)4;
            }

            if (b > 240)
            {
                return b == 255 ? (byte)6 : (byte)5;
            }

            return 7;
        }

        private static byte ByteKind2(byte b)
        {
            if ((b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z'))
            {
                return 0;
            }

            if ((b >= (byte)'0' && b <= (byte)'9') || b == (byte)'.' || b == (byte)',')
            {
                return 1;
            }

            if (b < 16)
            {
                return 2;
            }

            return b > 240 ? (byte)3 : (byte)4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte PredictByte(byte p1, byte p2, byte p3, int order)
        {
            return order switch
            {
                0 => p1,
                1 => unchecked((byte)((2 * p1) - p2)),
                2 => unchecked((byte)((3 * p1) - (3 * p2) + p3)),
                _ => 0,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort PredictUshort(ushort p1, ushort p2, ushort p3, int order)
        {
            return order switch
            {
                0 => p1,
                1 => unchecked((ushort)((2 * p1) - p2)),
                2 => unchecked((ushort)((3 * p1) - (3 * p2) + p3)),
                _ => 0,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PredictUint(uint p1, uint p2, uint p3, int order)
        {
            return order switch
            {
                0 => p1,
                1 => unchecked((2u * p1) - p2),
                2 => unchecked((3u * p1) - (3u * p2) + p3),
                _ => 0u,
            };
        }
    }
}
