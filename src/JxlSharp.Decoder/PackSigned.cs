// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Runtime.CompilerServices;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// ZigZag-style signed integer packing and unpacking utilities.
    /// Ported from <c>lib/jxl/pack_signed.h</c>.
    /// </summary>
    public static class PackSigned
    {
        /// <summary>
        /// Encodes non-negative <c>X</c> into <c>2 * X</c>, and negative <c>-X</c> into <c>2 * X - 1</c>.
        /// </summary>
        /// <param name="value">Signed 32-bit integer to pack.</param>
        /// <returns>Unsigned 32-bit packed representation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Pack(int value)
        {
            // Encodes non-negative (X) into (2 * X), negative (-X) into (2 * X - 1)
            return unchecked(((uint)value << 1) ^ (((uint)~value >> 31) - 1u));
        }

        /// <summary>
        /// Reverses <see cref="Pack"/>, i.e., <c>Unpack(Pack(X)) == X</c>.
        /// </summary>
        /// <param name="value">Packed unsigned value.</param>
        /// <returns>Unpacked signed 32-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Unpack(nuint value)
        {
            // Reverse to PackSigned, i.e. UnpackSigned(PackSigned(X)) == X.
            // (((~value) & 1) - 1) is either 0 or 0xFF...FF and it will have an expected
            // unsigned-integer-overflow.
            uint val32 = (uint)value;

            return unchecked((int)((val32 >> 1) ^ (((~val32) & 1u) - 1u)));
        }
    }
}
