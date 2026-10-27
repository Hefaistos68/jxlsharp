// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Fast but weak random generator.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// 8-lane Xorshift128+ random number generator.
    /// Adapted from https://github.com/vpxyz/xorshift/blob/master/xorshift128plus/ (MIT-license).
    /// </summary>
    public sealed class Xorshift128Plus
    {
        /// <summary>
        /// 8 independent generators (= single iteration for AVX-512).
        /// </summary>
        public const int N = 8;

        private readonly ulong[] _s0 = new ulong[N];
        private readonly ulong[] _s1 = new ulong[N];

        /// <summary>
        /// Initializes a new instance of the <see cref="Xorshift128Plus"/> class with a single 64-bit seed.
        /// </summary>
        /// <param name="seed">Initial 64-bit seed.</param>
        public Xorshift128Plus(ulong seed)
        {
            // Init state using SplitMix64 generator
            _s0[0] = SplitMix64(unchecked(seed + 0x9E3779B97F4A7C15ul));
            _s1[0] = SplitMix64(_s0[0]);

            for (int i = 1; i < N; ++i)
            {
                _s0[i] = SplitMix64(_s1[i - 1]);
                _s1[i] = SplitMix64(_s0[i]);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Xorshift128Plus"/> class with four 32-bit seeds.
        /// </summary>
        /// <param name="seed1">First 32-bit seed.</param>
        /// <param name="seed2">Second 32-bit seed.</param>
        /// <param name="seed3">Third 32-bit seed.</param>
        /// <param name="seed4">Fourth 32-bit seed.</param>
        public Xorshift128Plus(uint seed1, uint seed2, uint seed3, uint seed4)
        {
            // Init state using SplitMix64 generator
            _s0[0] = SplitMix64(unchecked((((ulong)seed1 << 32) + seed2) + 0x9E3779B97F4A7C15ul));
            _s1[0] = SplitMix64(unchecked((((ulong)seed3 << 32) + seed4) + 0x9E3779B97F4A7C15ul));

            for (int i = 1; i < N; ++i)
            {
                _s0[i] = SplitMix64(_s0[i - 1]);
                _s1[i] = SplitMix64(_s1[i - 1]);
            }
        }

        /// <summary>
        /// Fills <paramref name="randomBits"/> (length at least <see cref="N"/>) with 8 generated 64-bit random numbers.
        /// </summary>
        /// <param name="randomBits">Destination span of length at least 8.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Fill(Span<ulong> randomBits)
        {
            for (int i = 0; i < N; ++i)
            {
                ulong s1   = _s0[i];
                ulong s0   = _s1[i];
                ulong bits = unchecked(s1 + s0); // b, c

                _s0[i]        = s0;
                s1           ^= s1 << 23;
                randomBits[i] = bits;
                s1           ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
                _s1[i]        = s1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SplitMix64(ulong z)
        {
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBul);

            return z ^ (z >> 31);
        }
    }
}
