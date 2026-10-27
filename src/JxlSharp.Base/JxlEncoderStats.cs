// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Data type for querying <see cref="JxlEncoderStats"/> object
    /// (ported from <c>include/jxl/stats.h</c>).
    /// </summary>
    public enum JxlEncoderStatsKey
    {
        /// <summary>Header bits count.</summary>
        HeaderBits = 0,

        /// <summary>Table of contents bits count.</summary>
        TocBits = 1,

        /// <summary>Patch dictionary bits count.</summary>
        DictionaryBits = 2,

        /// <summary>Splines bits count.</summary>
        SplinesBits = 3,

        /// <summary>Noise synthesis bits count.</summary>
        NoiseBits = 4,

        /// <summary>Quantization weights bits count.</summary>
        QuantBits = 5,

        /// <summary>Modular tree bits count.</summary>
        ModularTreeBits = 6,

        /// <summary>Modular global data bits count.</summary>
        ModularGlobalBits = 7,

        /// <summary>DC coefficients bits count.</summary>
        DcBits = 8,

        /// <summary>Modular DC group bits count.</summary>
        ModularDcGroupBits = 9,

        /// <summary>AC control fields bits count.</summary>
        ControlFieldsBits = 10,

        /// <summary>Coefficient order bits count.</summary>
        CoefOrderBits = 11,

        /// <summary>AC histogram bits count.</summary>
        AcHistogramBits = 12,

        /// <summary>AC tokenized coefficients bits count.</summary>
        AcBits = 13,

        /// <summary>Modular AC group bits count.</summary>
        ModularAcGroupBits = 14,

        /// <summary>Number of small transform blocks.</summary>
        NumSmallBlocks = 15,

        /// <summary>Number of DCT4x8 blocks.</summary>
        NumDct4x8Blocks = 16,

        /// <summary>Number of AFV blocks.</summary>
        NumAfvBlocks = 17,

        /// <summary>Number of DCT8x8 blocks.</summary>
        NumDct8Blocks = 18,

        /// <summary>Number of DCT8x32 blocks.</summary>
        NumDct8x32Blocks = 19,

        /// <summary>Number of DCT16x16 blocks.</summary>
        NumDct16Blocks = 20,

        /// <summary>Number of DCT16x32 blocks.</summary>
        NumDct16x32Blocks = 21,

        /// <summary>Number of DCT32x32 blocks.</summary>
        NumDct32Blocks = 22,

        /// <summary>Number of DCT32x64 blocks.</summary>
        NumDct32x64Blocks = 23,

        /// <summary>Number of DCT64x64 blocks.</summary>
        NumDct64Blocks = 24,

        /// <summary>Number of Butteraugli psychovisual loop iterations.</summary>
        NumButteraugliIters = 25,

        /// <summary>Total number of encoder statistics entries.</summary>
        NumStats = 26,
    }

    /// <summary>
    /// Structure that holds the encoder statistics
    /// (ported from <c>include/jxl/stats.h</c>).
    /// </summary>
    public sealed class JxlEncoderStats
    {
        private readonly nuint[] _stats = new nuint[(int)JxlEncoderStatsKey.NumStats];

        /// <summary>
        /// Creates an instance of <see cref="JxlEncoderStats"/> and initializes it.
        /// </summary>
        /// <returns>Initialized <see cref="JxlEncoderStats"/> instance.</returns>
        public static JxlEncoderStats Create()
        {
            return new JxlEncoderStats();
        }

        /// <summary>
        /// Returns the value of the statistic corresponding to the given <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The particular statistic to query.</param>
        /// <returns>The value of the statistic, or 0 if <paramref name="key"/> is out of range.</returns>
        public nuint Get(JxlEncoderStatsKey key)
        {
            int index = (int)key;

            if ((uint)index >= (uint)_stats.Length)
            {
                return 0;
            }

            return _stats[index];
        }

        /// <summary>
        /// Sets the value of the statistic corresponding to the given <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Statistic key.</param>
        /// <param name="value">New value.</param>
        public void Set(JxlEncoderStatsKey key, nuint value)
        {
            int index = (int)key;

            if ((uint)index < (uint)_stats.Length)
            {
                _stats[index] = value;
            }
        }

        /// <summary>
        /// Updates the values of this stats object by adding those of <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other stats object whose values will be merged into this instance.</param>
        public void Merge(JxlEncoderStats? other)
        {
            if (other is null)
            {
                return;
            }

            for (int i = 0; i < _stats.Length; i++)
            {
                _stats[i] += other._stats[i];
            }
        }
    }
}
