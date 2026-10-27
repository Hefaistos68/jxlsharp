// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Specialized instructions for processing register-sized bit arrays.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Specialized bit-manipulation operations for processing register-sized bit arrays.
    /// </summary>
    public static class Bits
    {
        /// <summary>
        /// Returns the number of leading zero bits above the most-significant 1-bit for a non-zero 32-bit unsigned integer.
        /// Undefined results for x == 0.
        /// </summary>
        /// <param name="x">Non-zero 32-bit unsigned integer.</param>
        /// <returns>Number of leading zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsAboveMS1BitNonzero(uint x)
        {
            Debug.Assert(x != 0);

            return BitOperations.LeadingZeroCount(x);
        }

        /// <summary>
        /// Returns the number of leading zero bits above the most-significant 1-bit for a non-zero 64-bit unsigned integer.
        /// Undefined results for x == 0.
        /// </summary>
        /// <param name="x">Non-zero 64-bit unsigned integer.</param>
        /// <returns>Number of leading zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsAboveMS1BitNonzero(ulong x)
        {
            Debug.Assert(x != 0);

            return BitOperations.LeadingZeroCount(x);
        }

        /// <summary>
        /// Returns the number of trailing zero bits below the least-significant 1-bit for a non-zero 32-bit unsigned integer.
        /// Undefined results for x == 0.
        /// </summary>
        /// <param name="x">Non-zero 32-bit unsigned integer.</param>
        /// <returns>Number of trailing zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsBelowLS1BitNonzero(uint x)
        {
            Debug.Assert(x != 0);

            return BitOperations.TrailingZeroCount(x);
        }

        /// <summary>
        /// Returns the number of trailing zero bits below the least-significant 1-bit for a non-zero 64-bit unsigned integer.
        /// Undefined results for x == 0.
        /// </summary>
        /// <param name="x">Non-zero 64-bit unsigned integer.</param>
        /// <returns>Number of trailing zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsBelowLS1BitNonzero(ulong x)
        {
            Debug.Assert(x != 0);

            return BitOperations.TrailingZeroCount(x);
        }

        /// <summary>
        /// Returns the number of leading zero bits above the most-significant 1-bit for a 32-bit unsigned integer.
        /// Returns bit width (32) for x == 0.
        /// </summary>
        /// <param name="x">32-bit unsigned integer.</param>
        /// <returns>Number of leading zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsAboveMS1Bit(uint x)
        {
            return x == 0 ? 32 : Num0BitsAboveMS1BitNonzero(x);
        }

        /// <summary>
        /// Returns the number of leading zero bits above the most-significant 1-bit for a 64-bit unsigned integer.
        /// Returns bit width (64) for x == 0.
        /// </summary>
        /// <param name="x">64-bit unsigned integer.</param>
        /// <returns>Number of leading zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsAboveMS1Bit(ulong x)
        {
            return x == 0 ? 64 : Num0BitsAboveMS1BitNonzero(x);
        }

        /// <summary>
        /// Returns the number of trailing zero bits below the least-significant 1-bit for a 32-bit unsigned integer.
        /// Returns bit width (32) for x == 0.
        /// </summary>
        /// <param name="x">32-bit unsigned integer.</param>
        /// <returns>Number of trailing zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsBelowLS1Bit(uint x)
        {
            return x == 0 ? 32 : Num0BitsBelowLS1BitNonzero(x);
        }

        /// <summary>
        /// Returns the number of trailing zero bits below the least-significant 1-bit for a 64-bit unsigned integer.
        /// Returns bit width (64) for x == 0.
        /// </summary>
        /// <param name="x">64-bit unsigned integer.</param>
        /// <returns>Number of trailing zero bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Num0BitsBelowLS1Bit(ulong x)
        {
            return x == 0 ? 64 : Num0BitsBelowLS1BitNonzero(x);
        }

        /// <summary>
        /// Returns base-2 logarithm, rounded down, for a non-zero 32-bit unsigned integer.
        /// </summary>
        /// <param name="x">Non-zero 32-bit unsigned integer.</param>
        /// <returns>Floor of log2(x).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorLog2Nonzero(uint x)
        {
            return 31 ^ Num0BitsAboveMS1BitNonzero(x);
        }

        /// <summary>
        /// Returns base-2 logarithm, rounded down, for a non-zero 64-bit unsigned integer.
        /// </summary>
        /// <param name="x">Non-zero 64-bit unsigned integer.</param>
        /// <returns>Floor of log2(x).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorLog2Nonzero(ulong x)
        {
            return 63 ^ Num0BitsAboveMS1BitNonzero(x);
        }

        /// <summary>
        /// Returns base-2 logarithm, rounded up, for a non-zero 32-bit unsigned integer.
        /// </summary>
        /// <param name="x">Non-zero 32-bit unsigned integer.</param>
        /// <returns>Ceiling of log2(x).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilLog2Nonzero(uint x)
        {
            int floorLog2 = FloorLog2Nonzero(x);

            if ((x & (x - 1)) == 0)
            {
                return floorLog2; // power of two
            }

            return floorLog2 + 1;
        }

        /// <summary>
        /// Returns base-2 logarithm, rounded up, for a non-zero 64-bit unsigned integer.
        /// </summary>
        /// <param name="x">Non-zero 64-bit unsigned integer.</param>
        /// <returns>Ceiling of log2(x).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilLog2Nonzero(ulong x)
        {
            int floorLog2 = FloorLog2Nonzero(x);

            if ((x & (x - 1)) == 0)
            {
                return floorLog2; // power of two
            }

            return floorLog2 + 1;
        }
    }
}
