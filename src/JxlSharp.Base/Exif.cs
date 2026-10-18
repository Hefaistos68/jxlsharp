// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Basic parsing of Exif (just enough for the render-impacting things
    /// like orientation) (ported from <c>lib/jxl/base/exif.h</c>).
    /// </summary>
    public static class Exif
    {
        /// <summary>
        /// TIFF/Exif tag ID for image orientation.
        /// </summary>
        public const ushort ExifOrientationTag = 274;

        /// <summary>
        /// Checks if a blob looks like Exif, and if so, sets <paramref name="bigEndian"/>
        /// according to the TIFF endianness.
        /// </summary>
        /// <param name="exif">Exif byte span.</param>
        /// <param name="bigEndian">Receives <c>true</c> if big-endian TIFF header; <c>false</c> if little-endian.</param>
        /// <returns><c>true</c> if valid Exif TIFF header; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsExif(ReadOnlySpan<byte> exif, out bool bigEndian)
        {
            bigEndian = false;

            if (exif.Length < 12)
            {
                // not enough bytes for a valid exif blob
                return false;
            }

            uint magic = ByteOrder.LoadLE32(exif);

            if (magic == 0x2A004D4Du)
            {
                bigEndian = true;

                return true;
            }

            if (magic == 0x002A4949u)
            {
                bigEndian = false;

                return true;
            }

            // not a valid tiff header
            return false;
        }

        /// <summary>
        /// Finds the position of an Exif tag (pointing right after the 2-byte tag ID),
        /// or 0 if it is not found.
        /// </summary>
        /// <param name="exif">Exif byte span.</param>
        /// <param name="tagName">Tag ID to search for.</param>
        /// <returns>Byte offset immediately following the 2-byte tag ID, or 0 if not found.</returns>
        public static nuint FindExifTagPosition(ReadOnlySpan<byte> exif, ushort tagName)
        {
            if (!IsExif(exif, out bool bigEndian))
            {
                return 0;
            }

            ReadOnlySpan<byte> offsetSlice = exif.Slice(4);
            ulong offset                   = bigEndian ? ByteOrder.LoadBE32(offsetSlice) : ByteOrder.LoadLE32(offsetSlice);

            if ((ulong)exif.Length < 12 + offset + 2 || offset < 8)
            {
                return 0;
            }

            if (offset + 2 >= (ulong)exif.Length)
            {
                return 0;
            }

            nuint t       = (nuint)offset;
            ushort nbTags = ReadUInt16(exif.Slice((int)t), bigEndian);
            t += 2;

            while (nbTags > 0)
            {
                if (t + 12 >= (nuint)exif.Length)
                {
                    return 0;
                }

                ushort tag = ReadUInt16(exif.Slice((int)t), bigEndian);
                t += 2;

                if (tag == tagName)
                {
                    return t;
                }

                t += 10;
                nbTags--;
            }

            return 0;
        }

        /// <summary>
        /// Parses the Exif data just enough to extract any render-impacting info.
        /// If the Exif data is invalid or could not be parsed, then it is treated
        /// as a no-op.
        /// </summary>
        /// <param name="exif">Exif byte span.</param>
        /// <param name="orientation">Orientation updated if valid orientation tag is found.</param>
        public static void InterpretExif(ReadOnlySpan<byte> exif, ref JxlOrientation orientation)
        {
            if (!IsExif(exif, out bool bigEndian))
            {
                return;
            }

            nuint oPos = FindExifTagPosition(exif, ExifOrientationTag);

            if (oPos != 0)
            {
                int pos     = (int)oPos;
                ushort type = ReadUInt16(exif.Slice(pos), bigEndian);
                pos += 2;
                uint count = ReadUInt32(exif.Slice(pos), bigEndian);
                pos += 4;
                ushort value = ReadUInt16(exif.Slice(pos), bigEndian);

                if (type == 3 && count == 1 && value >= 1 && value <= 8)
                {
                    orientation = (JxlOrientation)value;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool bigEndian)
        {
            return (ushort)(bigEndian ? ByteOrder.LoadBE16(source) : ByteOrder.LoadLE16(source));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadUInt32(ReadOnlySpan<byte> source, bool bigEndian)
        {
            return bigEndian ? ByteOrder.LoadBE32(source) : ByteOrder.LoadLE32(source);
        }
    }
}
