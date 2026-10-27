// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Shared constants and helper functions.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Shared constants and arithmetic helper functions.
    /// </summary>
    public static class Common
    {
        /// <summary>
        /// Number of bits per byte (more clear than CHAR_BIT).
        /// </summary>
        public const int BitsPerByte = 8;

        /// <summary>
        /// Number of bits per byte (C++ naming alias).
        /// </summary>
        public const int kBitsPerByte = BitsPerByte;

        /// <summary>
        /// Number of bits per byte (PascalCase alias).
        /// </summary>
        public const nuint KBitsPerByte = 8;

        /// <summary>
        /// Square root of 2 (<c>sqrt(2)</c>).
        /// </summary>
        public const float kSqrt2 = 1.4142135623730951f;

        /// <summary>
        /// Mathematical constant Pi.
        /// </summary>
        public const double kPi = 3.14159265358979323846264338327950288;

        /// <summary>
        /// Multiplier for conversion of log2(x) result to ln(x) (1.0 / log2(e)).
        /// </summary>
        public const float kInvLog2e = 0.6931471805599453f;

        /// <summary>
        /// Reasonable default for sRGB, matches common monitors. We map white to this
        /// many nits (cd/m^2) by default. Butteraugli was tuned for 250 nits, which is
        /// very close.
        /// </summary>
        public const float kDefaultIntensityTarget = 255.0f;

        /// <summary>
        /// Rounds a bit count up to the nearest multiple of 8.
        /// </summary>
        /// <param name="bits">Number of bits.</param>
        /// <returns>Bit count rounded up to a byte multiple.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundUpBitsToByteMultiple(int bits)
        {
            return (bits + 7) & ~7;
        }

        /// <summary>
        /// Rounds a bit count up to the nearest multiple of 8.
        /// </summary>
        /// <param name="bits">Number of bits.</param>
        /// <returns>Bit count rounded up to a byte multiple.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint RoundUpBitsToByteMultiple(nuint bits)
        {
            return (bits + 7u) & ~(nuint)7u;
        }

        /// <summary>
        /// Rounds a dimension up to the nearest 8x8 block dimension multiple.
        /// </summary>
        /// <param name="dim">Dimension in pixels.</param>
        /// <returns>Dimension rounded up to a multiple of 8.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundUpToBlockDim(int dim)
        {
            return (dim + 7) & ~7;
        }

        /// <summary>
        /// Rounds a dimension up to the nearest 8x8 block dimension multiple.
        /// </summary>
        /// <param name="dim">Dimension in pixels.</param>
        /// <returns>Dimension rounded up to a multiple of 8.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint RoundUpToBlockDim(nuint dim)
        {
            return (dim + 7u) & ~(nuint)7u;
        }

        /// <summary>
        /// Adds two unsigned 64-bit integers and checks for overflow.
        /// </summary>
        /// <param name="a">First addend.</param>
        /// <param name="b">Second addend.</param>
        /// <param name="sum">Resulting sum.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeAdd(ulong a, ulong b, out ulong sum)
        {
            sum = unchecked(a + b);

            return sum >= a; // no need to check b - either sum >= both or < both.
        }

        /// <summary>
        /// Adds two native unsigned integers and checks for overflow.
        /// </summary>
        /// <param name="a">First addend.</param>
        /// <param name="b">Second addend.</param>
        /// <param name="sum">Resulting sum.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeAdd(nuint a, nuint b, out nuint sum)
        {
            sum = unchecked(a + b);

            return sum >= a;
        }

        /// <summary>
        /// Multiplies two unsigned 64-bit integers and checks for overflow.
        /// </summary>
        /// <param name="a">First factor.</param>
        /// <param name="b">Second factor.</param>
        /// <param name="product">Resulting product.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeMul(ulong a, ulong b, out ulong product)
        {
            product = 0;

            if (a == 0 || b == 0)
            {
                return true;
            }

            if (b > (ulong.MaxValue / a))
            {
                return false;
            }

            product = a * b;

            return true;
        }

        /// <summary>
        /// Multiplies two native unsigned integers and checks for overflow.
        /// </summary>
        /// <param name="a">First factor.</param>
        /// <param name="b">Second factor.</param>
        /// <param name="product">Resulting product.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeMul(nuint a, nuint b, out nuint product)
        {
            product = 0;

            if (a == 0 || b == 0)
            {
                return true;
            }

            if (b > (nuint.MaxValue / a))
            {
                return false;
            }

            product = a * b;

            return true;
        }

        /// <summary>
        /// Subtracts two signed 32-bit integers and detects signed overflow.
        /// </summary>
        /// <param name="a">Minuend.</param>
        /// <param name="b">Subtrahend.</param>
        /// <param name="c">Difference (unchecked).</param>
        /// <returns><c>true</c> if signed overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SubOverflow(int a, int b, out int c)
        {
            uint ua = (uint)a;
            uint ub = (uint)b;
            uint uc = unchecked(ua - ub);

            c = unchecked((int)uc);

            return (((ua ^ ub) & (ua ^ uc)) >> 31) != 0;
        }

        /// <summary>
        /// Computes ceiling integer division <c>(a + b - 1) / b</c>.
        /// </summary>
        /// <param name="a">Dividend.</param>
        /// <param name="b">Divisor.</param>
        /// <returns>Ceiling of <c>a / b</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DivCeil(int a, int b)
        {
            return (a + b - 1) / b;
        }

        /// <summary>
        /// Computes ceiling integer division <c>(a + b - 1) / b</c>.
        /// </summary>
        /// <param name="a">Dividend.</param>
        /// <param name="b">Divisor.</param>
        /// <returns>Ceiling of <c>a / b</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long DivCeil(long a, long b)
        {
            return (a + b - 1) / b;
        }

        /// <summary>
        /// Computes ceiling integer division <c>(a + b - 1) / b</c>.
        /// </summary>
        /// <param name="a">Dividend.</param>
        /// <param name="b">Divisor.</param>
        /// <returns>Ceiling of <c>a / b</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong DivCeil(ulong a, ulong b)
        {
            return (a + b - 1) / b;
        }

        /// <summary>
        /// Computes ceiling integer division <c>(a + b - 1) / b</c>.
        /// </summary>
        /// <param name="a">Dividend.</param>
        /// <param name="b">Divisor.</param>
        /// <returns>Ceiling of <c>a / b</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint DivCeil(nuint a, nuint b)
        {
            return (a + b - 1) / b;
        }

        /// <summary>
        /// Works for any <paramref name="align"/>; rounds <paramref name="what"/> up to a multiple of <paramref name="align"/>.
        /// </summary>
        /// <param name="what">Value to align.</param>
        /// <param name="align">Alignment multiple.</param>
        /// <returns>Aligned value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundUpTo(int what, int align)
        {
            return DivCeil(what, align) * align;
        }

        /// <summary>
        /// Works for any <paramref name="align"/>; rounds <paramref name="what"/> up to a multiple of <paramref name="align"/>.
        /// </summary>
        /// <param name="what">Value to align.</param>
        /// <param name="align">Alignment multiple.</param>
        /// <returns>Aligned value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RoundUpTo(ulong what, ulong align)
        {
            return DivCeil(what, align) * align;
        }

        /// <summary>
        /// Works for any <paramref name="align"/>; rounds <paramref name="what"/> up to a multiple of <paramref name="align"/>.
        /// </summary>
        /// <param name="what">Value to align.</param>
        /// <param name="align">Alignment multiple.</param>
        /// <returns>Aligned value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint RoundUpTo(nuint what, nuint align)
        {
            return DivCeil(what, align) * align;
        }

        /// <summary>
        /// Safely rounds <paramref name="what"/> up to a multiple of <paramref name="align"/> without overflow.
        /// <c>align &lt;= 1</c> means no rounding.
        /// </summary>
        /// <param name="what">Value to align.</param>
        /// <param name="align">Alignment multiple.</param>
        /// <param name="result">Aligned output value.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeRoundUpTo(ulong what, ulong align, out ulong result)
        {
            if (align < 2)
            {
                result = what;

                return true;
            }

            ulong remainder = what % align;

            if (remainder == 0)
            {
                result = what;

                return true;
            }

            return SafeAdd(what, align - remainder, out result);
        }

        /// <summary>
        /// Safely rounds <paramref name="what"/> up to a multiple of <paramref name="align"/> without overflow.
        /// <c>align &lt;= 1</c> means no rounding.
        /// </summary>
        /// <param name="what">Value to align.</param>
        /// <param name="align">Alignment multiple.</param>
        /// <param name="result">Aligned output value.</param>
        /// <returns><c>true</c> if no overflow occurred; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SafeRoundUpTo(nuint what, nuint align, out nuint result)
        {
            if (align < 2)
            {
                result = what;

                return true;
            }

            nuint remainder = what % align;

            if (remainder == 0)
            {
                result = what;

                return true;
            }

            return SafeAdd(what, align - remainder, out result);
        }

        /// <summary>
        /// Multiplies <paramref name="multiplier"/> by Pi.
        /// </summary>
        /// <param name="multiplier">Scale factor.</param>
        /// <returns><c>multiplier * Pi</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Pi(float multiplier)
        {
            return (float)(multiplier * kPi);
        }

        /// <summary>
        /// Multiplies <paramref name="multiplier"/> by Pi.
        /// </summary>
        /// <param name="multiplier">Scale factor.</param>
        /// <returns><c>multiplier * Pi</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Pi(double multiplier)
        {
            return multiplier * kPi;
        }

        /// <summary>
        /// Clamps <paramref name="val"/> to the inclusive range <c>[low, hi]</c>.
        /// </summary>
        /// <typeparam name="T">Comparable value type.</typeparam>
        /// <param name="val">Value to clamp.</param>
        /// <param name="low">Minimum bound.</param>
        /// <param name="hi">Maximum bound.</param>
        /// <returns>Clamped value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Clamp1<T>(T val, T low, T hi)
            where T : IComparable<T>
        {
            if (val.CompareTo(low) < 0)
            {
                return low;
            }

            if (val.CompareTo(hi) > 0)
            {
                return hi;
            }

            return val;
        }

        /// <summary>
        /// Converts a numeric value to an invariant string representation.
        /// </summary>
        /// <typeparam name="T">Formattable numeric type.</typeparam>
        /// <param name="n">Numeric value.</param>
        /// <returns>Invariant culture string representation.</returns>
        public static string ToString<T>(T n)
            where T : IFormattable
        {
            return n.ToString(null, CultureInfo.InvariantCulture);
        }
    }
}
