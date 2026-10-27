// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Pack/UnpackSigned utilities (ported from <c>lib/jxl/pack_signed.h</c>).
    /// </summary>
    public static class PackSignedHelper
    {
        /// <summary>
        /// Encodes non-negative (<c>X</c>) into (<c>2 * X</c>), negative (<c>-X</c>) into (<c>2 * X - 1</c>).
        /// </summary>
        /// <param name="value">Signed 32-bit integer.</param>
        /// <returns>Zigzag-encoded unsigned 32-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackSigned(int value)
        {
            unchecked
            {
                return ((uint)value << 1) ^ (((uint)~value >> 31) - 1u);
            }
        }

        /// <summary>
        /// Reverse to <see cref="PackSigned"/>, i.e. <c>UnpackSigned(PackSigned(X)) == X</c>.
        /// </summary>
        /// <param name="value">Zigzag-encoded unsigned value.</param>
        /// <returns>Decoded signed 32-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UnpackSigned(nuint value)
        {
            unchecked
            {
                // (((~value) & 1) - 1) is either 0 or 0xFF...FF and it will have an expected
                // unsigned-integer-overflow.
                uint v = (uint)value;

                return (int)((v >> 1) ^ (((~v) & 1u) - 1u));
            }
        }
    }
}
