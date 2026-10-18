// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Forward/backward-compatible 'bundles' with auto-serialized 'fields'.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Base class for codestream header/field bundles that can be visited for initialization,
    /// default-checking, bit-size estimation, reading, and writing.
    /// Ported from <c>jxl::Fields</c> in <c>lib/jxl/field_encodings.h</c>.
    /// </summary>
    public abstract class Fields
    {
        /// <summary>
        /// Gets the debug name of this bundle type.
        /// </summary>
        public virtual string BundleName => GetType().Name;

        /// <summary>
        /// Visits all fields of this bundle using <paramref name="visitor"/>.
        /// </summary>
        /// <param name="visitor">Field visitor.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status VisitFields(Visitor visitor);
    }

    /// <summary>
    /// Distribution of <c>uint</c> values for one 2-bit selector in <see cref="U32Enc"/>.
    /// Represents either a power-of-two-sized range with an offset, or a direct 31-bit constant.
    /// Ported from <c>jxl::U32Distr</c> in <c>lib/jxl/field_encodings.h</c>.
    /// </summary>
    public readonly struct U32Distr
    {
        /// <summary>
        /// Bit flag indicating a direct constant value rather than an offset+extra-bits range.
        /// </summary>
        public const uint KDirect = 0x80000000u;

        /// <summary>
        /// Packed descriptor word.
        /// </summary>
        public readonly uint D;

        /// <summary>
        /// Initializes a new instance of the <see cref="U32Distr"/> struct.
        /// </summary>
        /// <param name="d">Packed descriptor word.</param>
        public U32Distr(uint d)
        {
            D = d;
        }

        /// <summary>
        /// Gets a value indicating whether this distribution encodes a direct constant.
        /// </summary>
        public bool IsDirect => (D & KDirect) != 0;

        /// <summary>
        /// Gets the direct constant value (only valid if <see cref="IsDirect"/> is <c>true</c>).
        /// </summary>
        public uint Direct => D & (KDirect - 1u);

        /// <summary>
        /// Gets the number of extra bits to read/write (only valid if <see cref="IsDirect"/> is <c>false</c>).
        /// </summary>
        public nuint ExtraBits => (D & 0x1Fu) + 1u;

        /// <summary>
        /// Gets the offset added to the extra bits (only valid if <see cref="IsDirect"/> is <c>false</c>).
        /// </summary>
        public uint Offset => (D >> 5) & 0x3FFFFFFu;

        /// <summary>
        /// Creates a direct-coded 31-bit value occupying 2 bits in the bitstream.
        /// </summary>
        /// <param name="value">Direct constant value.</param>
        /// <returns>Direct <see cref="U32Distr"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static U32Distr Val(uint value)
        {
            return new U32Distr(value | KDirect);
        }

        /// <summary>
        /// Creates a range distribution where <c>value - offset</c> is signaled in <paramref name="bits"/> extra bits.
        /// </summary>
        /// <param name="bits">Number of extra bits (1..32).</param>
        /// <param name="offset">Base offset.</param>
        /// <returns>Offset+bits <see cref="U32Distr"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static U32Distr BitsOffset(uint bits, uint offset)
        {
            return new U32Distr(((bits - 1u) & 0x1Fu) + ((offset & 0x3FFFFFFu) << 5));
        }

        /// <summary>
        /// Creates a range distribution where <c>value</c> is signaled in <paramref name="bits"/> extra bits (offset 0).
        /// </summary>
        /// <param name="bits">Number of extra bits (1..32).</param>
        /// <returns>Zero-offset <see cref="U32Distr"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static U32Distr Bits(uint bits)
        {
            return BitsOffset(bits, 0);
        }
    }

    /// <summary>
    /// Four-selector 32-bit unsigned integer encoding table.
    /// Ported from <c>jxl::U32Enc</c> in <c>lib/jxl/field_encodings.h</c>.
    /// </summary>
    public readonly struct U32Enc
    {
        private readonly U32Distr _d0;
        private readonly U32Distr _d1;
        private readonly U32Distr _d2;
        private readonly U32Distr _d3;

        /// <summary>
        /// Initializes a new instance of the <see cref="U32Enc"/> struct.
        /// </summary>
        /// <param name="d0">Selector 0 distribution.</param>
        /// <param name="d1">Selector 1 distribution.</param>
        /// <param name="d2">Selector 2 distribution.</param>
        /// <param name="d3">Selector 3 distribution.</param>
        public U32Enc(U32Distr d0, U32Distr d1, U32Distr d2, U32Distr d3)
        {
            _d0 = d0;
            _d1 = d1;
            _d2 = d2;
            _d3 = d3;
        }

        /// <summary>
        /// Returns the <see cref="U32Distr"/> for <paramref name="selector"/> (0..3).
        /// </summary>
        /// <param name="selector">2-bit selector index.</param>
        /// <returns>Distribution for <paramref name="selector"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public U32Distr GetDistr(uint selector)
        {
            return selector switch
            {
                0 => _d0,
                1 => _d1,
                2 => _d2,
                _ => _d3,
            };
        }
    }

    /// <summary>
    /// Helper functions for enum bit-masks and field encodings.
    /// Ported from <c>lib/jxl/field_encodings.h</c>.
    /// </summary>
    public static class FieldEncoding
    {
        /// <summary>
        /// Returns a 64-bit mask with the bit at <paramref name="index"/> set.
        /// </summary>
        /// <param name="index">Bit index (0..63).</param>
        /// <returns><c>1UL &lt;&lt; index</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MakeBit(uint index)
        {
            return 1UL << (int)index;
        }

        /// <summary>
        /// Returns a 64-bit bitmask with bit <paramref name="value"/> set.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="value">Enum value (0..63).</param>
        /// <returns><c>1UL &lt;&lt; (int)value</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MakeBit<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
        {
            uint index = Unsafe.As<TEnum, uint>(ref value);

            return 1UL << (int)index;
        }

        /// <summary>
        /// Validates that <paramref name="value"/> is less than 64 and its bit is set in <paramref name="enumBits"/>.
        /// </summary>
        /// <param name="value">Numeric enum value.</param>
        /// <param name="enumBits">Bitmask of valid enum values.</param>
        /// <returns><see cref="Status.OkStatus"/> if valid; otherwise failure.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Status EnumValid(uint value, ulong enumBits)
        {
            if (value >= 64u)
            {
                return Status.Failure("Enum value >= 64");
            }

            if ((enumBits & MakeBit(value)) == 0)
            {
                return Status.Failure("Invalid enum value");
            }

            return true;
        }
    }

    /// <summary>
    /// Reads and writes fixed-width unsigned integers (<c>bits &lt;= 32</c>).
    /// Ported from <c>jxl::BitsCoder</c> in <c>lib/jxl/fields.h</c>, <c>fields.cc</c>, and <c>enc_fields.cc</c>.
    /// </summary>
    public static class BitsCoder
    {
        /// <summary>
        /// Returns the maximum number of bits required to encode a <paramref name="bits"/>-bit field.
        /// </summary>
        /// <param name="bits">Field width in bits.</param>
        /// <returns><paramref name="bits"/>.</returns>
        public static nuint MaxEncodedBits(nuint bits)
        {
            return bits;
        }

        /// <summary>
        /// Checks whether <paramref name="value"/> fits in <paramref name="bits"/> bits.
        /// </summary>
        /// <param name="bits">Field width in bits.</param>
        /// <param name="value">Value to check.</param>
        /// <param name="encodedBits">Output bit count.</param>
        /// <returns><see cref="Status.OkStatus"/> if encodable.</returns>
        public static Status CanEncode(nuint bits, uint value, out nuint encodedBits)
        {
            encodedBits = bits;

            if (value >= (1UL << (int)bits))
            {
                return Status.Failure("Value too large for bit width");
            }

            return true;
        }

        /// <summary>
        /// Reads <paramref name="bits"/> bits from <paramref name="reader"/>.
        /// </summary>
        /// <param name="bits">Number of bits to read.</param>
        /// <param name="reader">Source bit reader.</param>
        /// <returns>Decoded 32-bit unsigned integer.</returns>
        public static uint Read(nuint bits, BitReader reader)
        {
            return (uint)reader.ReadBits(bits);
        }

        /// <summary>
        /// Writes <paramref name="value"/> in <paramref name="bits"/> bits to <paramref name="writer"/>.
        /// </summary>
        /// <param name="bits">Number of bits to write.</param>
        /// <param name="value">Value to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <returns><see cref="Status.OkStatus"/> if encodable.</returns>
        public static Status Write(nuint bits, uint value, BitWriter writer)
        {
            if (value >= (1UL << (int)bits))
            {
                return Status.Failure("Value too large to encode in specified bits");
            }

            writer.Write(bits, value);

            return true;
        }
    }

    /// <summary>
    /// Reads and writes 32-bit unsigned integers governed by a <see cref="U32Enc"/> distribution table.
    /// Ported from <c>jxl::U32Coder</c> in <c>lib/jxl/fields.h</c>, <c>fields.cc</c>, and <c>enc_fields.cc</c>.
    /// </summary>
    public static class U32Coder
    {
        /// <summary>
        /// Returns the maximum number of bits that <paramref name="enc"/> can consume.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <returns>Maximum encoded bit length.</returns>
        public static nuint MaxEncodedBits(U32Enc enc)
        {
            nuint extraBits = 0;

            for (uint selector = 0; selector < 4; ++selector)
            {
                U32Distr d = enc.GetDistr(selector);

                if (!d.IsDirect && d.ExtraBits > extraBits)
                {
                    extraBits = d.ExtraBits;
                }
            }

            return 2u + extraBits;
        }

        /// <summary>
        /// Checks whether <paramref name="value"/> can be encoded with <paramref name="enc"/>.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <param name="value">Value to check.</param>
        /// <param name="encodedBits">Exact bit length required if encodable.</param>
        /// <returns><see cref="Status.OkStatus"/> if encodable.</returns>
        public static Status CanEncode(U32Enc enc, uint value, out nuint encodedBits)
        {
            Status ok   = ChooseSelector(enc, value, out _, out nuint totalBits);
            encodedBits = ok ? totalBits : 0;

            return ok;
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer from <paramref name="reader"/> using <paramref name="enc"/>.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <param name="reader">Source bit reader.</param>
        /// <returns>Decoded value.</returns>
        public static uint Read(U32Enc enc, BitReader reader)
        {
            uint selector = (uint)reader.ReadFixedBits(2);
            U32Distr d    = enc.GetDistr(selector);

            if (d.IsDirect)
            {
                return d.Direct;
            }

            return (uint)reader.ReadBits(d.ExtraBits) + d.Offset;
        }

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="writer"/> using <paramref name="enc"/>.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <param name="value">Value to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Write(U32Enc enc, uint value, BitWriter writer)
        {
            if (!ChooseSelector(enc, value, out uint selector, out nuint totalBits))
            {
                return Status.Failure("No feasible U32 selector");
            }

            writer.Write(2, selector);
            U32Distr d = enc.GetDistr(selector);

            if (!d.IsDirect)
            {
                uint offset = d.Offset;
                writer.Write(totalBits - 2u, value - offset);
            }

            return true;
        }

        /// <summary>
        /// Selects the minimal-bit selector (0..3) in <paramref name="enc"/> capable of representing <paramref name="value"/>.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <param name="value">Value to encode.</param>
        /// <param name="selector">Chosen 2-bit selector.</param>
        /// <param name="totalBits">Total bits required (including the 2-bit selector).</param>
        /// <returns><see cref="Status.OkStatus"/> if a feasible selector exists.</returns>
        public static Status ChooseSelector(U32Enc enc, uint value, out uint selector, out nuint totalBits)
        {
            selector  = 0;
            totalBits = 64;

            for (uint s = 0; s < 4; ++s)
            {
                U32Distr d = enc.GetDistr(s);

                if (d.IsDirect)
                {
                    if (d.Direct == value)
                    {
                        selector  = s;
                        totalBits = 2;

                        return true;
                    }

                    continue;
                }

                nuint extraBits = d.ExtraBits;
                uint offset     = d.Offset;

                if (value < offset || value >= offset + (1UL << (int)extraBits))
                {
                    continue;
                }

                if (2u + extraBits < totalBits)
                {
                    selector  = s;
                    totalBits = 2u + extraBits;
                }
            }

            if (totalBits == 64)
            {
                return Status.Failure("No feasible selector for U32 value");
            }

            return true;
        }
    }

    /// <summary>
    /// Reads and writes variable-length 64-bit unsigned integers.
    /// Ported from <c>jxl::U64Coder</c> in <c>lib/jxl/fields.h</c>, <c>fields.cc</c>, and <c>enc_fields.cc</c>.
    /// </summary>
    public static class U64Coder
    {
        /// <summary>
        /// Returns the maximum number of bits required to encode any 64-bit unsigned integer (73 bits).
        /// </summary>
        /// <returns>Maximum bit count (73).</returns>
        public static nuint MaxEncodedBits()
        {
            return 2 + 12 + (6 * (8 + 1)) + (4 + 1);
        }

        /// <summary>
        /// Reads a 64-bit unsigned integer from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <returns>Decoded 64-bit unsigned integer.</returns>
        public static ulong Read(BitReader reader)
        {
            ulong selector = reader.ReadFixedBits(2);

            if (selector == 0)
            {
                return 0;
            }

            if (selector == 1)
            {
                return 1UL + reader.ReadFixedBits(4);
            }

            if (selector == 2)
            {
                return 17UL + reader.ReadFixedBits(8);
            }

            // selector 3, varint, groups have first 12, then 8, and last 4 bits.
            ulong result = reader.ReadFixedBits(12);
            int shift    = 12;

            while (reader.ReadFixedBits(1) != 0)
            {
                if (shift == 60)
                {
                    result |= reader.ReadFixedBits(4) << shift;
                    break;
                }

                result |= reader.ReadFixedBits(8) << shift;
                shift  += 8;
            }

            return result;
        }

        /// <summary>
        /// Computes the exact number of bits required to encode <paramref name="value"/>.
        /// </summary>
        /// <param name="value">Value to encode.</param>
        /// <param name="encodedBits">Output bit count.</param>
        /// <returns>Always <see cref="Status.OkStatus"/>.</returns>
        public static Status CanEncode(ulong value, out nuint encodedBits)
        {
            if (value == 0)
            {
                encodedBits = 2;

                return true;
            }

            if (value <= 16)
            {
                encodedBits = 2 + 4;

                return true;
            }

            if (value <= 272)
            {
                encodedBits = 2 + 8;

                return true;
            }

            encodedBits = 2 + 12;
            value     >>= 12;
            int shift   = 12;

            while (value > 0 && shift < 60)
            {
                encodedBits += 1 + 8;
                value      >>= 8;
                shift       += 8;
            }

            if (value > 0)
            {
                encodedBits += 1 + 4;
            }
            else
            {
                encodedBits += 1;
            }

            return true;
        }

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="writer"/>.
        /// </summary>
        /// <param name="value">64-bit unsigned integer to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <returns>Always <see cref="Status.OkStatus"/>.</returns>
        public static Status Write(ulong value, BitWriter writer)
        {
            if (value == 0)
            {
                writer.Write(2, 0);

                return true;
            }

            if (value <= 16)
            {
                writer.Write(2, 1);
                writer.Write(4, value - 1UL);

                return true;
            }

            if (value <= 272)
            {
                writer.Write(2, 2);
                writer.Write(8, value - 17UL);

                return true;
            }

            writer.Write(2, 3);
            writer.Write(12, value & 4095UL);
            value >>= 12;
            int shift = 12;

            while (value > 0 && shift < 60)
            {
                writer.Write(1, 1);
                writer.Write(8, value & 255UL);
                value >>= 8;
                shift  += 8;
            }

            if (value > 0)
            {
                writer.Write(1, 1);
                writer.Write(4, value & 15UL);
            }
            else
            {
                writer.Write(1, 0);
            }

            return true;
        }
    }

    /// <summary>
    /// Reads and writes IEEE 754 half-precision (<c>binary16</c>) floats, rejecting NaN and Infinity.
    /// Ported from <c>jxl::F16Coder</c> in <c>lib/jxl/fields.h</c>, <c>fields.cc</c>, and <c>enc_fields.cc</c>.
    /// </summary>
    public static class F16Coder
    {
        /// <summary>
        /// Returns the fixed encoded bit length of an F16 field (16 bits).
        /// </summary>
        /// <returns>16.</returns>
        public static nuint MaxEncodedBits()
        {
            return 16;
        }

        /// <summary>
        /// Reads a 16-bit half-precision float from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="value">Decoded single-precision float.</param>
        /// <returns><see cref="Status.OkStatus"/> if finite; failure if NaN or infinity.</returns>
        public static Status Read(BitReader reader, out float value)
        {
            uint bits16    = (uint)reader.ReadFixedBits(16);
            uint sign      = bits16 >> 15;
            uint biasedExp = (bits16 >> 10) & 0x1Fu;
            uint mantissa  = bits16 & 0x3FFu;

            if (biasedExp == 31u)
            {
                value = 0.0f;

                return Status.Failure("F16 infinity or NaN are not supported");
            }

            if (biasedExp == 0u)
            {
                float mag = (1.0f / 16384.0f) * (mantissa * (1.0f / 1024.0f));
                value     = sign != 0 ? -mag : mag;

                return true;
            }

            uint biasedExp32 = biasedExp + (127u - 15u);
            uint mantissa32  = mantissa << (23 - 10);
            uint bits32      = (sign << 31) | (biasedExp32 << 23) | mantissa32;
            value            = BitConverter.UInt32BitsToSingle(bits32);

            return true;
        }

        /// <summary>
        /// Checks whether <paramref name="value"/> is finite and within the <c>binary16</c> range (<c>[-65504, 65504]</c>).
        /// </summary>
        /// <param name="value">Float value to check.</param>
        /// <param name="encodedBits">Output bit count (16).</param>
        /// <returns><see cref="Status.OkStatus"/> if encodable.</returns>
        public static Status CanEncode(float value, out nuint encodedBits)
        {
            encodedBits = MaxEncodedBits();

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return Status.Failure("Should not attempt to store NaN and infinity");
            }

            return MathF.Abs(value) <= 65504.0f;
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a 16-bit half-precision float to <paramref name="writer"/>.
        /// </summary>
        /// <param name="value">Float value to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Write(float value, BitWriter writer)
        {
            uint bits32      = BitConverter.SingleToUInt32Bits(value);
            uint sign        = bits32 >> 31;
            uint biasedExp32 = (bits32 >> 23) & 0xFFu;
            uint mantissa32  = bits32 & 0x7FFFFFu;
            int exp          = (int)biasedExp32 - 127;

            if (exp > 15)
            {
                return Status.Failure("Too big to encode in F16");
            }

            if (exp < -24)
            {
                writer.Write(16, 0);

                return true;
            }

            uint biasedExp16;
            uint mantissa16;

            if (exp < -14)
            {
                biasedExp16  = 0;
                uint subExp  = (uint)(-14 - exp);
                mantissa16   = (1u << (int)(10u - subExp)) + (mantissa32 >> (int)(13u + subExp));
            }
            else
            {
                biasedExp16 = (uint)(exp + 15);
                mantissa16  = mantissa32 >> 13;
            }

            uint bits16 = (sign << 15) | (biasedExp16 << 10) | mantissa16;
            writer.Write(16, bits16);

            return true;
        }
    }

    /// <summary>
    /// Base visitor interface for traversing <see cref="Fields"/> bundles.
    /// Ported from <c>jxl::Visitor</c> and <c>jxl::fields_internal::VisitorBase</c> in <c>lib/jxl/fields.h</c>.
    /// </summary>
    public abstract class Visitor
    {
        private nuint _depth;
        private ulong _begun;
        private ulong _ended;

        /// <summary>
        /// Gets a value indicating whether this visitor is reading from a bitstream.
        /// </summary>
        public virtual bool IsReading => false;

        /// <summary>
        /// Visits a top-level or nested <see cref="Fields"/> bundle.
        /// </summary>
        /// <param name="fields">Bundle to visit.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status Visit(Fields fields)
        {
            if (_depth >= Bundle.KMaxExtensions)
            {
                return Status.Failure("Bundle nesting depth exceeded");
            }

            _depth += 1;
            _begun <<= 1;
            _ended <<= 1;

            Status ok = fields.VisitFields(this);

            _begun >>= 1;
            _ended >>= 1;
            _depth -= 1;

            return ok;
        }

        /// <summary>
        /// Visits a boolean field (1 bit).
        /// </summary>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status Bool(bool defaultValue, ref bool value)
        {
            uint bits = value ? 1u : 0u;

            if (!Bits(1, defaultValue ? 1u : 0u, ref bits))
            {
                return false;
            }

            value = bits == 1u;

            return true;
        }

        /// <summary>
        /// Visits a 32-bit unsigned integer field using <paramref name="enc"/>.
        /// </summary>
        /// <param name="enc">U32 encoding table.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status U32(U32Enc enc, uint defaultValue, ref uint value);

        /// <summary>
        /// Visits a 32-bit unsigned integer field using four <see cref="U32Distr"/> selectors.
        /// </summary>
        /// <param name="d0">Selector 0 distribution.</param>
        /// <param name="d1">Selector 1 distribution.</param>
        /// <param name="d2">Selector 2 distribution.</param>
        /// <param name="d3">Selector 3 distribution.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status U32(
            U32Distr d0,
            U32Distr d1,
            U32Distr d2,
            U32Distr d3,
            uint defaultValue,
            ref uint value)
        {
            return U32(new U32Enc(d0, d1, d2, d3), defaultValue, ref value);
        }

        /// <summary>
        /// Visits an enum field using the standard JPEG XL enum U32 distribution and validates it against <paramref name="validBits"/>.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="defaultValue">Default enum value.</param>
        /// <param name="value">Enum field reference.</param>
        /// <param name="validBits">Bitmask of valid enum values (bits 0..63).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Enum<TEnum>(TEnum defaultValue, ref TEnum value, ulong validBits = ulong.MaxValue)
            where TEnum : unmanaged, Enum
        {
            uint u32     = Unsafe.As<TEnum, uint>(ref value);
            uint defU32  = Unsafe.As<TEnum, uint>(ref defaultValue);
            Status u32Ok = U32(
                U32Distr.Val(0),
                U32Distr.Val(1),
                U32Distr.BitsOffset(4, 2),
                U32Distr.BitsOffset(6, 18),
                defU32,
                ref u32);

            if (!u32Ok)
            {
                return u32Ok;
            }

            value = Unsafe.As<uint, TEnum>(ref u32);

            return FieldEncoding.EnumValid(u32, validBits);
        }

        /// <summary>
        /// Visits a fixed-width unsigned integer field (<paramref name="bits"/> &lt;= 32).
        /// </summary>
        /// <param name="bits">Number of bits.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status Bits(nuint bits, uint defaultValue, ref uint value);

        /// <summary>
        /// Visits a 64-bit unsigned integer field.
        /// </summary>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status U64(ulong defaultValue, ref ulong value);

        /// <summary>
        /// Visits a 16-bit half-precision floating-point field.
        /// </summary>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="value">Field reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status F16(float defaultValue, ref float value);

        /// <summary>
        /// Returns whether subsequent conditional fields should be visited.
        /// </summary>
        /// <param name="condition">Condition evaluated from prior fields.</param>
        /// <returns><c>true</c> if the conditional block should be visited.</returns>
        public virtual bool Conditional(bool condition)
        {
            return condition;
        }

        /// <summary>
        /// Visits the 1-bit <c>all_default</c> flag at the start of a bundle.
        /// </summary>
        /// <param name="fields">Bundle being visited.</param>
        /// <param name="allDefault">Flag reference.</param>
        /// <returns><c>true</c> if all fields are default and the caller should short-circuit.</returns>
        public virtual bool AllDefault(Fields fields, ref bool allDefault)
        {
            if (!Bool(true, ref allDefault))
            {
                return false;
            }

            return allDefault;
        }

        /// <summary>
        /// Resets all fields in <paramref name="fields"/> to their default values when reading an <c>all_default</c> bundle.
        /// </summary>
        /// <param name="fields">Bundle to reset.</param>
        public virtual void SetDefault(Fields fields)
        {
        }

        /// <summary>
        /// Visits a nested <see cref="Fields"/> bundle.
        /// </summary>
        /// <param name="fields">Nested bundle.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status VisitNested(Fields fields)
        {
            return Visit(fields);
        }

        /// <summary>
        /// Begins the extension section of a bundle.
        /// </summary>
        /// <param name="extensions">Extensions bitmask reference.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status BeginExtensions(ref ulong extensions)
        {
            Status status = U64(0, ref extensions);

            if (!status)
            {
                return status;
            }

            _begun += 1;

            return true;
        }

        /// <summary>
        /// Ends the extension section of a bundle.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status EndExtensions()
        {
            _ended += 1;

            return true;
        }

        /// <summary>
        /// Checks whether the underlying reader has at least <paramref name="bits"/> remaining.
        /// </summary>
        /// <param name="bits">Required bits.</param>
        /// <returns><see cref="Status.OkStatus"/> if enough bits exist.</returns>
        public virtual Status CheckHasEnoughBits(nuint bits)
        {
            return false;
        }
    }

    /// <summary>
    /// Operations on <see cref="Fields"/> bundles: initialization, default checking, bit counting, reading, and writing.
    /// Ported from <c>jxl::Bundle</c> in <c>lib/jxl/fields.h</c>, <c>fields.cc</c>, and <c>enc_fields.cc</c>.
    /// </summary>
    public static class Bundle
    {
        /// <summary>
        /// Maximum number of extension flags (64 bits in <c>ulong</c>).
        /// </summary>
        public const int KMaxExtensions = 64;

        /// <summary>
        /// Initializes all direct fields of <paramref name="fields"/> to their default values (non-recursive).
        /// </summary>
        /// <param name="fields">Bundle to initialize.</param>
        public static void Init(Fields fields)
        {
            InitVisitor visitor = new InitVisitor();
            visitor.Visit(fields);
        }

        /// <summary>
        /// Recursively resets all direct and nested fields of <paramref name="fields"/> to their default values.
        /// </summary>
        /// <param name="fields">Bundle to reset.</param>
        public static void SetDefault(Fields fields)
        {
            SetDefaultVisitor visitor = new SetDefaultVisitor();
            visitor.Visit(fields);
        }

        /// <summary>
        /// Returns whether all fields in <paramref name="fields"/> equal their default values.
        /// </summary>
        /// <param name="fields">Bundle to inspect.</param>
        /// <returns><c>true</c> if all fields equal their defaults.</returns>
        public static bool AllDefault(Fields fields)
        {
            AllDefaultVisitor visitor = new AllDefaultVisitor();
            visitor.Visit(fields);

            return visitor.IsAllDefault;
        }

        /// <summary>
        /// Checks whether <paramref name="fields"/> can be encoded and computes the exact bit sizes.
        /// </summary>
        /// <param name="fields">Bundle to check.</param>
        /// <param name="extensionBits">Output number of bits used by extension fields.</param>
        /// <param name="totalBits">Output total number of bits required to encode the bundle.</param>
        /// <returns><see cref="Status.OkStatus"/> if encodable.</returns>
        public static Status CanEncode(Fields fields, out nuint extensionBits, out nuint totalBits)
        {
            extensionBits = 0;
            totalBits     = 0;

            CanEncodeVisitor visitor = new CanEncodeVisitor();
            Status visitStatus       = visitor.Visit(fields);

            if (!visitStatus)
            {
                return visitStatus;
            }

            return visitor.GetSizes(out extensionBits, out totalBits);
        }

        /// <summary>
        /// Reads <paramref name="fields"/> from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="fields">Target bundle.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Read(BitReader reader, Fields fields)
        {
            ReadVisitor visitor = new ReadVisitor(reader);
            Status status       = visitor.Visit(fields);

            if (!status)
            {
                return status;
            }

            return visitor.Ok;
        }

        /// <summary>
        /// Returns whether enough bits are available in <paramref name="reader"/> to read <paramref name="fields"/>.
        /// </summary>
        /// <param name="reader">Source bit reader (advanced by this call).</param>
        /// <param name="fields">Target bundle.</param>
        /// <returns><c>true</c> if enough bytes are present.</returns>
        public static bool CanRead(BitReader reader, Fields fields)
        {
            ReadVisitor visitor = new ReadVisitor(reader);
            Status status       = visitor.Visit(fields);

            return status.Code != StatusCode.NotEnoughBytes;
        }

        /// <summary>
        /// Writes <paramref name="fields"/> to <paramref name="writer"/>.
        /// </summary>
        /// <param name="fields">Source bundle.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Codestream layer for bit accounting.</param>
        /// <param name="auxOut">Optional statistics collector.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Write(Fields fields, BitWriter writer, LayerType layer, AuxOut? auxOut = null)
        {
            if (!CanEncode(fields, out nuint extensionBits, out nuint totalBits))
            {
                return Status.Failure("Bundle.CanEncode failed");
            }

            return writer.WithMaxBits(
                totalBits,
                layer,
                auxOut,
                () =>
                {
                    WriteVisitor visitor = new WriteVisitor(extensionBits, writer);
                    Status status        = visitor.Visit(fields);

                    if (!status)
                    {
                        return status;
                    }

                    return visitor.Ok;
                });
        }

        /// <summary>
        /// Checks whether <paramref name="visitor"/> is reading and has at least <paramref name="bits"/> available.
        /// </summary>
        /// <param name="visitor">Active visitor.</param>
        /// <param name="bits">Required bits.</param>
        /// <returns><see cref="Status.OkStatus"/> if enough bits exist.</returns>
        public static Status CheckHasEnoughBits(Visitor visitor, nuint bits)
        {
            return visitor.CheckHasEnoughBits(bits);
        }

        private sealed class InitVisitor : Visitor
        {
            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                value = defaultValue;

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                value = defaultValue;

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                value = defaultValue;

                return true;
            }

            public override Status Bool(bool defaultValue, ref bool value)
            {
                value = defaultValue;

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                value = defaultValue;

                return true;
            }

            public override bool Conditional(bool condition)
            {
                return true;
            }

            public override bool AllDefault(Fields fields, ref bool allDefault)
            {
                allDefault = true;

                return false;
            }

            public override Status VisitNested(Fields fields)
            {
                return true;
            }
        }

        private sealed class SetDefaultVisitor : Visitor
        {
            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                value = defaultValue;

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                value = defaultValue;

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                value = defaultValue;

                return true;
            }

            public override Status Bool(bool defaultValue, ref bool value)
            {
                value = defaultValue;

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                value = defaultValue;

                return true;
            }

            public override bool Conditional(bool condition)
            {
                return true;
            }

            public override bool AllDefault(Fields fields, ref bool allDefault)
            {
                allDefault = true;

                return false;
            }
        }

        private sealed class AllDefaultVisitor : Visitor
        {
            public bool IsAllDefault { get; private set; } = true;

            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                IsAllDefault &= value == defaultValue;

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                IsAllDefault &= value == defaultValue;

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                IsAllDefault &= value == defaultValue;

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                IsAllDefault &= MathF.Abs(value - defaultValue) < 1e-6f;

                return true;
            }

            public override bool AllDefault(Fields fields, ref bool allDefault)
            {
                return false;
            }
        }

        private sealed class ReadVisitor : Visitor
        {
            private readonly BitReader _reader;
            private readonly ulong[] _extensionBits = new ulong[KMaxExtensions];
            private ulong _totalExtensionBits;
            private nuint _posAfterExtSize;
            private bool _ok = true;

            public ReadVisitor(BitReader reader)
            {
                _reader = reader;
            }

            public Status Ok => _ok;

            public override bool IsReading => true;

            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                value = BitsCoder.Read(bits, _reader);

                if (!_reader.AllReadsWithinBounds())
                {
                    return Status.NotEnoughBytes("Not enough bytes for header");
                }

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                value = U32Coder.Read(enc, _reader);

                if (!_reader.AllReadsWithinBounds())
                {
                    return Status.NotEnoughBytes("Not enough bytes for header");
                }

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                value = U64Coder.Read(_reader);

                if (!_reader.AllReadsWithinBounds())
                {
                    return Status.NotEnoughBytes("Not enough bytes for header");
                }

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                _ok &= F16Coder.Read(_reader, out value).IsOk;

                if (!_reader.AllReadsWithinBounds())
                {
                    return Status.NotEnoughBytes("Not enough bytes for header");
                }

                return true;
            }

            public override void SetDefault(Fields fields)
            {
                Bundle.SetDefault(fields);
            }

            public override Status BeginExtensions(ref ulong extensions)
            {
                Status baseStatus = base.BeginExtensions(ref extensions);

                if (!baseStatus)
                {
                    return baseStatus;
                }

                if (extensions == 0)
                {
                    return true;
                }

                for (ulong remaining = extensions; remaining != 0; remaining &= remaining - 1UL)
                {
                    int idxExtension = BitOperations.TrailingZeroCount(remaining);
                    Status u64Status = U64(0, ref _extensionBits[idxExtension]);

                    if (!u64Status)
                    {
                        return u64Status;
                    }

                    if (!Common.SafeAdd(_totalExtensionBits, _extensionBits[idxExtension], out _totalExtensionBits))
                    {
                        return Status.Failure("Extension bits overflowed, invalid codestream");
                    }
                }

                _posAfterExtSize = _reader.TotalBitsConsumed();

                return true;
            }

            public override Status EndExtensions()
            {
                Status baseStatus = base.EndExtensions();

                if (!baseStatus)
                {
                    return baseStatus;
                }

                if (_posAfterExtSize == 0)
                {
                    return true;
                }

                nuint bitsRead = _reader.TotalBitsConsumed();

                if (!Common.SafeAdd(_posAfterExtSize, _totalExtensionBits, out ulong end))
                {
                    return Status.Failure("Invalid extension size, caused overflow");
                }

                if (bitsRead > end)
                {
                    return Status.Failure("Read more extension bits than budgeted");
                }

                nuint remainingBits = (nuint)(end - bitsRead);

                if (remainingBits != 0)
                {
                    _reader.SkipBits(remainingBits);

                    if (!_reader.AllReadsWithinBounds())
                    {
                        return Status.NotEnoughBytes("Not enough bytes for header");
                    }
                }

                return true;
            }

            public override Status CheckHasEnoughBits(nuint bits)
            {
                nuint haveBits = _reader.TotalBytes() * Common.BitsPerByte;
                nuint wantBits = bits + _reader.TotalBitsConsumed();

                if (haveBits < wantBits)
                {
                    return Status.NotEnoughBytes("Not enough bytes for header");
                }

                return true;
            }
        }

        private sealed class CanEncodeVisitor : Visitor
        {
            private bool _ok = true;
            private nuint _encodedBits;
            private ulong _extensions;
            private nuint _posAfterExt;

            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                _ok          &= BitsCoder.CanEncode(bits, value, out nuint encoded).IsOk;
                _encodedBits += encoded;

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                _ok          &= U32Coder.CanEncode(enc, value, out nuint encoded).IsOk;
                _encodedBits += encoded;

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                _ok          &= U64Coder.CanEncode(value, out nuint encoded).IsOk;
                _encodedBits += encoded;

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                _ok          &= F16Coder.CanEncode(value, out nuint encoded).IsOk;
                _encodedBits += encoded;

                return true;
            }

            public override bool AllDefault(Fields fields, ref bool allDefault)
            {
                allDefault = Bundle.AllDefault(fields);
                Bool(true, ref allDefault);

                return allDefault;
            }

            public override Status BeginExtensions(ref ulong extensions)
            {
                Status status = base.BeginExtensions(ref extensions);

                if (!status)
                {
                    return status;
                }

                _extensions = extensions;

                if (extensions != 0)
                {
                    _posAfterExt = _encodedBits;
                }

                return true;
            }

            public Status GetSizes(out nuint extensionBits, out nuint totalBits)
            {
                extensionBits = 0;
                totalBits     = 0;

                if (!_ok)
                {
                    return false;
                }

                totalBits = _encodedBits;

                if (_posAfterExt != 0)
                {
                    extensionBits = _encodedBits - _posAfterExt;
                    _ok          &= U64Coder.CanEncode(extensionBits, out nuint extLenBits).IsOk;
                    totalBits    += extLenBits;

                    int extCount = BitOperations.PopCount(_extensions);

                    for (int i = 1; i < extCount; ++i)
                    {
                        _ok       &= U64Coder.CanEncode(0, out nuint zeroBits).IsOk;
                        totalBits += zeroBits;
                    }
                }

                return _ok;
            }
        }

        private sealed class WriteVisitor : Visitor
        {
            private readonly nuint _extensionBits;
            private readonly BitWriter _writer;
            private bool _ok = true;

            public WriteVisitor(nuint extensionBits, BitWriter writer)
            {
                _extensionBits = extensionBits;
                _writer        = writer;
            }

            public Status Ok => _ok;

            public override Status Bits(nuint bits, uint defaultValue, ref uint value)
            {
                _ok &= BitsCoder.Write(bits, value, _writer).IsOk;

                return true;
            }

            public override Status U32(U32Enc enc, uint defaultValue, ref uint value)
            {
                _ok &= U32Coder.Write(enc, value, _writer).IsOk;

                return true;
            }

            public override Status U64(ulong defaultValue, ref ulong value)
            {
                _ok &= U64Coder.Write(value, _writer).IsOk;

                return true;
            }

            public override Status F16(float defaultValue, ref float value)
            {
                _ok &= F16Coder.Write(value, _writer).IsOk;

                return true;
            }

            public override Status BeginExtensions(ref ulong extensions)
            {
                Status status = base.BeginExtensions(ref extensions);

                if (!status)
                {
                    return status;
                }

                if (extensions == 0)
                {
                    return true;
                }

                _ok &= U64Coder.Write(_extensionBits, _writer).IsOk;

                for (ulong remaining = extensions & (extensions - 1UL);
                     remaining != 0;
                     remaining &= remaining - 1UL)
                {
                    _ok &= U64Coder.Write(0, _writer).IsOk;
                }

                return true;
            }
        }
    }
}
