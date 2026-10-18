// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Optional output information for debugging and analyzing size usage.

using System;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Codestream layer identifier for <see cref="AuxOut"/> bit-budget accounting.
    /// Ported from <c>jxl::LayerType</c> in <c>lib/jxl/enc_aux_out.h</c>.
    /// </summary>
    public enum LayerType : byte
    {
        /// <summary>Codestream and frame headers.</summary>
        Header = 0,

        /// <summary>Table of contents.</summary>
        Toc,

        /// <summary>Patch dictionary.</summary>
        Dictionary,

        /// <summary>Splines.</summary>
        Splines,

        /// <summary>Noise synthesis parameters.</summary>
        Noise,

        /// <summary>Quantizer parameters.</summary>
        Quant,

        /// <summary>Modular MA tree.</summary>
        ModularTree,

        /// <summary>Global modular data.</summary>
        ModularGlobal,

        /// <summary>DC coefficients.</summary>
        Dc,

        /// <summary>Modular DC group data.</summary>
        ModularDcGroup,

        /// <summary>AC control fields (AC strategy, quant field, EPF, CfL).</summary>
        ControlFields,

        /// <summary>Coefficient order permutations.</summary>
        Order,

        /// <summary>AC histograms.</summary>
        Ac,

        /// <summary>AC tokens.</summary>
        AcTokens,

        /// <summary>Modular AC group data.</summary>
        ModularAcGroup,
    }

    /// <summary>
    /// Per-layer bit and entropy statistics.
    /// Ported from <c>jxl::AuxOut::LayerTotals</c> in <c>lib/jxl/enc_aux_out.h</c>.
    /// </summary>
    public sealed class LayerTotals
    {
        /// <summary>Number of clustered histograms.</summary>
        public nuint NumClusteredHistograms;

        /// <summary>Extra bits outside histograms.</summary>
        public nuint ExtraBits;

        /// <summary>Bits spent encoding histograms.</summary>
        public nuint HistogramBits;

        /// <summary>Total bits written to this layer.</summary>
        public nuint TotalBits;

        /// <summary>Clustered Shannon entropy estimate.</summary>
        public double ClusteredEntropy;

        /// <summary>
        /// Accumulates statistics from <paramref name="victim"/> into this instance.
        /// </summary>
        /// <param name="victim">Source layer totals.</param>
        public void Assimilate(LayerTotals victim)
        {
            NumClusteredHistograms += victim.NumClusteredHistograms;
            HistogramBits          += victim.HistogramBits;
            ExtraBits              += victim.ExtraBits;
            TotalBits              += victim.TotalBits;
            ClusteredEntropy       += victim.ClusteredEntropy;
        }
    }

    /// <summary>
    /// Statistics gathered during compression or decompression.
    /// Ported from <c>jxl::AuxOut</c> in <c>lib/jxl/enc_aux_out.h</c> and <c>enc_aux_out.cc</c>.
    /// </summary>
    public sealed class AuxOut
    {
        /// <summary>
        /// Total number of image layers.
        /// </summary>
        public const int KNumImageLayers = (int)LayerType.ModularAcGroup + 1;

        /// <summary>
        /// Per-layer totals array of length <see cref="KNumImageLayers"/>.
        /// </summary>
        public readonly LayerTotals[] Layers = CreateLayerTotals();

        /// <summary>Total number of 8x8 blocks.</summary>
        public nuint NumBlocks;

        /// <summary>Number of sub-8x8 blocks (set by AC strategy).</summary>
        public nuint NumSmallBlocks;

        /// <summary>Number of DCT4x8 blocks.</summary>
        public nuint NumDct4x8Blocks;

        /// <summary>Number of AFV blocks.</summary>
        public nuint NumAfvBlocks;

        /// <summary>Number of DCT8x8 blocks.</summary>
        public nuint NumDct8Blocks;

        /// <summary>Number of DCT8x16 blocks.</summary>
        public nuint NumDct8x16Blocks;

        /// <summary>Number of DCT8x32 blocks.</summary>
        public nuint NumDct8x32Blocks;

        /// <summary>Number of DCT16x16 blocks.</summary>
        public nuint NumDct16Blocks;

        /// <summary>Number of DCT16x32 blocks.</summary>
        public nuint NumDct16x32Blocks;

        /// <summary>Number of DCT32x32 blocks.</summary>
        public nuint NumDct32Blocks;

        /// <summary>Number of DCT32x64 blocks.</summary>
        public nuint NumDct32x64Blocks;

        /// <summary>Number of DCT64x64 blocks.</summary>
        public nuint NumDct64Blocks;

        /// <summary>Number of Butteraugli psychovisual loop iterations.</summary>
        public int NumButteraugliIters;

        /// <summary>
        /// Returns the human-readable name of the specified <paramref name="layer"/>.
        /// </summary>
        /// <param name="layer">Layer enum value.</param>
        /// <returns>Layer name string.</returns>
        public static string LayerName(LayerType layer)
        {
            return layer switch
            {
                LayerType.Header         => "Headers",
                LayerType.Toc            => "TOC",
                LayerType.Dictionary     => "Patches",
                LayerType.Splines        => "Splines",
                LayerType.Noise          => "Noise",
                LayerType.Quant          => "Quantizer",
                LayerType.ModularTree    => "ModularTree",
                LayerType.ModularGlobal  => "ModularGlobal",
                LayerType.Dc             => "DC",
                LayerType.ModularDcGroup => "ModularDcGroup",
                LayerType.ControlFields  => "ControlFields",
                LayerType.Order          => "CoeffOrder",
                LayerType.Ac             => "ACHistograms",
                LayerType.AcTokens       => "ACTokens",
                LayerType.ModularAcGroup => "ModularAcGroup",
                _                        => "Invalid",
            };
        }

        /// <summary>
        /// Returns the <see cref="LayerTotals"/> for the given <paramref name="idx"/>.
        /// </summary>
        /// <param name="idx">Layer identifier.</param>
        /// <returns>Corresponding <see cref="LayerTotals"/>.</returns>
        public LayerTotals Layer(LayerType idx)
        {
            return Layers[(int)idx];
        }

        /// <summary>
        /// Computes the sum of <see cref="LayerTotals.TotalBits"/> across all layers.
        /// </summary>
        /// <returns>Total bits across all layers.</returns>
        public nuint TotalBits()
        {
            nuint total = 0;

            for (int i = 0; i < KNumImageLayers; ++i)
            {
                total += Layers[i].TotalBits;
            }

            return total;
        }

        /// <summary>
        /// Accumulates all layer and block counters from <paramref name="victim"/>.
        /// </summary>
        /// <param name="victim">Source <see cref="AuxOut"/>.</param>
        public void Assimilate(AuxOut victim)
        {
            for (int i = 0; i < KNumImageLayers; ++i)
            {
                Layers[i].Assimilate(victim.Layers[i]);
            }

            NumBlocks           += victim.NumBlocks;
            NumSmallBlocks      += victim.NumSmallBlocks;
            NumDct4x8Blocks     += victim.NumDct4x8Blocks;
            NumAfvBlocks        += victim.NumAfvBlocks;
            NumDct8Blocks       += victim.NumDct8Blocks;
            NumDct8x16Blocks    += victim.NumDct8x16Blocks;
            NumDct8x32Blocks    += victim.NumDct8x32Blocks;
            NumDct16Blocks      += victim.NumDct16Blocks;
            NumDct16x32Blocks   += victim.NumDct16x32Blocks;
            NumDct32Blocks      += victim.NumDct32Blocks;
            NumDct32x64Blocks   += victim.NumDct32x64Blocks;
            NumDct64Blocks      += victim.NumDct64Blocks;
            NumButteraugliIters += victim.NumButteraugliIters;
        }

        private static LayerTotals[] CreateLayerTotals()
        {
            LayerTotals[] arr = new LayerTotals[KNumImageLayers];

            for (int i = 0; i < KNumImageLayers; ++i)
            {
                arr[i] = new LayerTotals();
            }

            return arr;
        }
    }
}
