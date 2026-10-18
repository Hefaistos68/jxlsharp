// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Random number generator + distributions.
// We don't use <random> because the implementation (and thus results) differs
// between libstdc++ and libc++.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Deterministic Xorshift128+ pseudo-random number generator and distribution helpers.
    /// </summary>
    public sealed class Rng
    {
        private ulong _s0;
        private ulong _s1;

        /// <summary>
        /// Initializes a new instance of the <see cref="Rng"/> class with the specified 64-bit seed.
        /// </summary>
        /// <param name="seed">Initial seed value.</param>
        public Rng(ulong seed)
        {
            _s0 = 0x94D049BB133111EBul;
            _s1 = unchecked(0xBF58476D1CE4E5B9ul + seed);
        }

        /// <summary>
        /// Generates the next 64-bit pseudo-random unsigned integer using Xorshift128+.
        /// </summary>
        /// <returns>Next 64-bit random value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextUInt64()
        {
            ulong s1   = _s0;
            ulong s0   = _s1;
            ulong bits = unchecked(s1 + s0); // b, c

            _s0 = s0;
            s1 ^= s1 << 23;
            s1 ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
            _s1 = s1;

            return bits;
        }

        /// <summary>
        /// Uniformly distributed int64_t in [begin, end), under the assumption that
        /// <c>end - begin</c> is significantly smaller than 1&lt;&lt;64, otherwise there is some
        /// bias.
        /// </summary>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        /// <returns>Random 64-bit signed integer in <c>[begin, end)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long UniformI(long begin, long end)
        {
            Debug.Assert(end > begin);

            return (long)(NextUInt64() % (ulong)(end - begin)) + begin;
        }

        /// <summary>
        /// Same as <see cref="UniformI"/>, but for uint64_t.
        /// </summary>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        /// <returns>Random 64-bit unsigned integer in <c>[begin, end)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong UniformU(ulong begin, ulong end)
        {
            Debug.Assert(end > begin);

            return (NextUInt64() % (end - begin)) + begin;
        }

        /// <summary>
        /// Uniformly distributed float in [begin, end) range. Note: only 23 bits of
        /// randomness.
        /// </summary>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        /// <returns>Random float in <c>[begin, end)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float UniformF(float begin, float end)
        {
            // Bits of a random [1, 2) float.
            uint u  = (uint)(NextUInt64() >> (64 - 23)) | 0x3F800000u;
            float f = BitConverter.UInt32BitsToSingle(u);

            // Note: (end-begin) * f + (2*begin-end) may fail to return a number >=
            // begin.
            return ((end - begin) * (f - 1.0f)) + begin;
        }

        /// <summary>
        /// Performs a Bernoulli trial with success probability <paramref name="p"/>.
        /// </summary>
        /// <param name="p">Success probability in <c>[0, 1]</c>.</param>
        /// <returns><c>true</c> with probability <paramref name="p"/>; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Bernoulli(float p)
        {
            return UniformF(0.0f, 1.0f) < p;
        }

        /// <summary>
        /// Computes the state (<c>1.0f / log(1 - p)</c>) for geometric distributions.
        /// </summary>
        /// <param name="p">Parameter <paramref name="p"/>.</param>
        /// <returns>Precomputed inverse log value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MakeGeometric(float p)
        {
            return 1.0f / MathF.Log(1.0f - p);
        }

        /// <summary>
        /// Samples a geometric distribution given the state produced by <see cref="MakeGeometric"/>.
        /// </summary>
        /// <param name="dist">Precomputed inverse log value.</param>
        /// <returns>Sampled unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Geometric(float dist)
        {
            float f         = UniformF(0.0f, 1.0f);
            float invLog1mp = dist;
            float log       = MathF.Log(1.0f - f) * invLog1mp;

            return (uint)log;
        }

        /// <summary>
        /// Shuffles the elements of <paramref name="span"/> in place.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="span">Span to shuffle.</param>
        public void Shuffle<T>(Span<T> span)
        {
            ulong n = (ulong)span.Length;

            for (ulong i = 0; i + 1 < n; i++)
            {
                int a   = (int)UniformU(i, n);
                int idx = (int)i;
                T temp  = span[a];

                span[a]   = span[idx];
                span[idx] = temp;
            }
        }
    }
}
