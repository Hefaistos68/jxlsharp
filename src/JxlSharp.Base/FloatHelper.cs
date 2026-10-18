// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// IEEE 754 float16 conversion and row-level sample loading helpers.
    /// </summary>
    public static class FloatHelper
    {
        /// <summary>
        /// Converts a 16-bit IEEE 754 half-precision floating point bit pattern to a 32-bit float.
        /// </summary>
        /// <param name="bits16">Raw 16-bit half-precision value.</param>
        /// <returns>Converted 32-bit single-precision float.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LoadFloat16(ushort bits16)
        {
            uint sign      = (uint)(bits16 >> 15);
            uint biasedExp = (uint)((bits16 >> 10) & 0x1F);
            uint mantissa  = (uint)(bits16 & 0x3FF);

            // Subnormal or zero
            if (biasedExp == 0)
            {
                float subnormal = (1.0f / 16384.0f) * (mantissa * (1.0f / 1024.0f));

                return sign != 0 ? -subnormal : subnormal;
            }

            // Normalized: convert the representation directly (faster than ldexp/tables).
            uint biasedExp32 = biasedExp == 0b11111u ? 0b11111111u : biasedExp + (127u - 15u);
            uint mantissa32  = mantissa << (23 - 10);
            uint bits32      = (sign << 31) | (biasedExp32 << 23) | mantissa32;

            return BitConverter.UInt32BitsToSingle(bits32);
        }

        /// <summary>
        /// Loads a row of samples of the specified <see cref="JxlDataType"/> as 32-bit floats.
        /// </summary>
        /// <param name="src">Source byte span.</param>
        /// <param name="count">Number of samples to read.</param>
        /// <param name="stride">Byte stride between consecutive samples.</param>
        /// <param name="type">Sample data type.</param>
        /// <param name="littleEndian">Whether multi-byte values are little-endian.</param>
        /// <param name="scale">Multiplier applied to integer sample types.</param>
        /// <param name="destination">Destination float span of length at least <paramref name="count"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise a failure status.</returns>
        public static Status LoadFloatRow(
            ReadOnlySpan<byte> src,
            int count,
            int stride,
            JxlDataType type,
            bool littleEndian,
            float scale,
            Span<float> destination)
        {
            switch (type)
            {
                case JxlDataType.Float:
                    LoadFloat32Row(src, count, stride, littleEndian, destination);

                    return true;

                case JxlDataType.UInt8:
                    LoadUInt8Row(src, count, stride, scale, destination);

                    return true;

                case JxlDataType.UInt16:
                    LoadUInt16Row(src, count, stride, littleEndian, scale, destination);

                    return true;

                case JxlDataType.Float16:
                    LoadFloat16Row(src, count, stride, littleEndian, destination);

                    return true;

                default:
                    return Status.Failure("Unsupported sample format");
            }
        }

        private static void LoadFloat32Row(
            ReadOnlySpan<byte> src,
            int count,
            int stride,
            bool littleEndian,
            Span<float> destination)
        {
            for (int i = 0; i < count; ++i)
            {
                ReadOnlySpan<byte> slice = src.Slice(stride * i);

                destination[i] = littleEndian
                    ? ByteOrder.LoadLEFloat(slice)
                    : ByteOrder.LoadBEFloat(slice);
            }
        }

        private static void LoadUInt8Row(
            ReadOnlySpan<byte> src,
            int count,
            int stride,
            float scale,
            Span<float> destination)
        {
            for (int i = 0; i < count; ++i)
            {
                destination[i] = src[stride * i] * scale;
            }
        }

        private static void LoadUInt16Row(
            ReadOnlySpan<byte> src,
            int count,
            int stride,
            bool littleEndian,
            float scale,
            Span<float> destination)
        {
            for (int i = 0; i < count; ++i)
            {
                ReadOnlySpan<byte> slice = src.Slice(stride * i);
                uint raw                 = littleEndian ? ByteOrder.LoadLE16(slice) : ByteOrder.LoadBE16(slice);

                destination[i] = raw * scale;
            }
        }

        private static void LoadFloat16Row(
            ReadOnlySpan<byte> src,
            int count,
            int stride,
            bool littleEndian,
            Span<float> destination)
        {
            for (int i = 0; i < count; ++i)
            {
                ReadOnlySpan<byte> slice = src.Slice(stride * i);
                ushort raw               = (ushort)(littleEndian ? ByteOrder.LoadLE16(slice) : ByteOrder.LoadBE16(slice));

                destination[i] = LoadFloat16(raw);
            }
        }
    }
}
