// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Endianness inspection and multi-byte load/store operations.
    /// </summary>
    public static class ByteOrder
    {
        /// <summary>
        /// Returns whether the system is little-endian (least-significant byte first).
        /// </summary>
        /// <returns><c>true</c> if little-endian; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLittleEndian()
        {
            return BitConverter.IsLittleEndian;
        }

        /// <summary>
        /// Returns whether byte swapping is required for the specified target endianness.
        /// </summary>
        /// <param name="endianness">Target endianness.</param>
        /// <returns><c>true</c> if byte order must be swapped; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SwapEndianness(JxlEndianness endianness)
        {
            return (endianness == JxlEndianness.BigEndian && IsLittleEndian()) ||
                   (endianness == JxlEndianness.LittleEndian && !IsLittleEndian());
        }

        /// <summary>
        /// Loads a 16-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 2 bytes.</param>
        /// <returns>The loaded 16-bit value as a 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LoadBE16(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(source);
        }

        /// <summary>
        /// Loads a 16-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 2 bytes.</param>
        /// <returns>The loaded 16-bit value as a 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LoadLE16(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(source);
        }

        /// <summary>
        /// Loads a 32-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 4 bytes.</param>
        /// <returns>The loaded 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LoadBE32(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(source);
        }

        /// <summary>
        /// Loads a 64-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 8 bytes.</param>
        /// <returns>The loaded 64-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong LoadBE64(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt64BigEndian(source);
        }

        /// <summary>
        /// Loads a 32-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 4 bytes.</param>
        /// <returns>The loaded 32-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LoadLE32(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(source);
        }

        /// <summary>
        /// Loads a 64-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="source">Source buffer of at least 8 bytes.</param>
        /// <returns>The loaded 64-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong LoadLE64(ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(source);
        }

        /// <summary>
        /// Loads a 64-bit unsigned integer in Little-Endian order from <paramref name="source"/> at <paramref name="offset"/>.
        /// </summary>
        /// <param name="source">Source byte array.</param>
        /// <param name="offset">Start byte offset (caller guarantees at least 8 bytes available).</param>
        /// <returns>The loaded 64-bit unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong LoadLE64(byte[] source, int offset)
        {
            ulong value = Unsafe.ReadUnaligned<ulong>(ref source[offset]);

            return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
        }

        /// <summary>
        /// Loads a Big-Endian 32-bit single-precision float.
        /// </summary>
        /// <param name="source">Source buffer of at least 4 bytes.</param>
        /// <returns>The loaded float value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LoadBEFloat(ReadOnlySpan<byte> source)
        {
            uint u = LoadBE32(source);

            return BitConverter.UInt32BitsToSingle(u);
        }

        /// <summary>
        /// Loads a Little-Endian 32-bit single-precision float.
        /// </summary>
        /// <param name="source">Source buffer of at least 4 bytes.</param>
        /// <returns>The loaded float value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LoadLEFloat(ReadOnlySpan<byte> source)
        {
            uint u = LoadLE32(source);

            return BitConverter.UInt32BitsToSingle(u);
        }

        /// <summary>
        /// Stores a 16-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="native">Value to store (low 16 bits).</param>
        /// <param name="destination">Destination buffer of at least 2 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreBE16(uint native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(native & 0xFFFFu));
        }

        /// <summary>
        /// Stores a 16-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="native">Value to store (low 16 bits).</param>
        /// <param name="destination">Destination buffer of at least 2 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreLE16(uint native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)(native & 0xFFFFu));
        }

        /// <summary>
        /// Stores a 32-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="native">Value to store.</param>
        /// <param name="destination">Destination buffer of at least 4 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreBE32(uint native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, native);
        }

        /// <summary>
        /// Stores a 64-bit unsigned integer in Big-Endian order.
        /// </summary>
        /// <param name="native">Value to store.</param>
        /// <param name="destination">Destination buffer of at least 8 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreBE64(ulong native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, native);
        }

        /// <summary>
        /// Stores a 32-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="native">Value to store.</param>
        /// <param name="destination">Destination buffer of at least 4 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreLE32(uint native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, native);
        }

        /// <summary>
        /// Stores a 64-bit unsigned integer in Little-Endian order.
        /// </summary>
        /// <param name="native">Value to store.</param>
        /// <param name="destination">Destination buffer of at least 8 bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreLE64(ulong native, Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, native);
        }

        /// <summary>
        /// Reverses the 4 bytes of a 32-bit single-precision floating point value.
        /// </summary>
        /// <param name="x">Input float.</param>
        /// <returns>Byte-swapped float.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BSwapFloat(float x)
        {
            uint u     = BitConverter.SingleToUInt32Bits(x);
            uint uswap = BinaryPrimitives.ReverseEndianness(u);

            return BitConverter.UInt32BitsToSingle(uswap);
        }
    }
}
