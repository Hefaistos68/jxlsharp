// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Parameters for loop filters (Gaborish convolution and Edge-Preserving Filter), stored in each frame.
    /// Ported from <c>jxl::LoopFilter</c> in <c>lib/jxl/loop_filter.h</c> and <c>lib/jxl/loop_filter.cc</c>.
    /// </summary>
    public sealed class LoopFilter : Fields
    {
        /// <summary>Number of entries in <see cref="EpfSharpLut"/> (8).</summary>
        public const int KEpfSharpEntries = 8;

        private const float KGabDefaultWeight1 = 1.1f * 0.104699568f;
        private const float KGabDefaultWeight2 = 1.1f * 0.055680538f;

        private static readonly nuint[] PaddingPerEpfIter = { 0, 2, 3, 6 };

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        // --- Gaborish convolution

        /// <summary>Whether Gaborish deblocking convolution is enabled.</summary>
        public bool Gab;

        /// <summary>Whether custom Gaborish weights are used.</summary>
        public bool GabCustom;

        /// <summary>Gaborish X channel axial weight.</summary>
        public float GabXWeight1;

        /// <summary>Gaborish X channel diagonal weight.</summary>
        public float GabXWeight2;

        /// <summary>Gaborish Y channel axial weight.</summary>
        public float GabYWeight1;

        /// <summary>Gaborish Y channel diagonal weight.</summary>
        public float GabYWeight2;

        /// <summary>Gaborish B channel axial weight.</summary>
        public float GabBWeight1;

        /// <summary>Gaborish B channel diagonal weight.</summary>
        public float GabBWeight2;

        // --- Edge-preserving filter

        /// <summary>
        /// Number of EPF stages to apply (0 = disabled, 1 = stage 1, 2 = stages 1+2, 3 = stage 0 + stage 1 + stage 2).
        /// </summary>
        public uint EpfIters;

        /// <summary>Whether custom EPF sharpness LUT entries are used.</summary>
        public bool EpfSharpCustom;

        /// <summary>EPF sharpness lookup table (8 entries).</summary>
        public float[] EpfSharpLut = new float[KEpfSharpEntries];

        /// <summary>Whether custom EPF weight parameters are used.</summary>
        public bool EpfWeightCustom;

        /// <summary>Relative EPF weight of each channel (X, Y, B).</summary>
        public float[] EpfChannelScale = new float[3];

        /// <summary>Minimum weight for first EPF pass.</summary>
        public float EpfPass1Zeroflush;

        /// <summary>Minimum weight for second EPF pass.</summary>
        public float EpfPass2Zeroflush;

        /// <summary>Whether custom EPF sigma parameters are used.</summary>
        public bool EpfSigmaCustom;

        /// <summary>Quantization multiplier for EPF sigma in VarDCT mode.</summary>
        public float EpfQuantMul;

        /// <summary>Multiplier for sigma in EPF pass 0.</summary>
        public float EpfPass0SigmaScale;

        /// <summary>Multiplier for sigma in EPF pass 2.</summary>
        public float EpfPass2SigmaScale;

        /// <summary>Inverse multiplier for sigma on block borders.</summary>
        public float EpfBorderSadMul;

        /// <summary>Fixed sigma value for Modular mode EPF.</summary>
        public float EpfSigmaForModular;

        /// <summary>Extension bitmask.</summary>
        public ulong Extensions;

        /// <summary>Whether the enclosing frame uses Modular encoding (not serialized directly).</summary>
        public bool NonserializedIsModular;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopFilter"/> class with default values.
        /// </summary>
        public LoopFilter()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "LoopFilter";

        /// <summary>
        /// Returns the required border padding in pixels for the configured loop filter stages.
        /// </summary>
        /// <returns>Padding in pixels.</returns>
        public nuint Padding()
        {
            return PaddingPerEpfIter[EpfIters] + (Gab ? 1u : 0u);
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            // Must come before AllDefault.
            if (visitor.AllDefault(this, ref AllDefault))
            {
                // Overwrite all serialized fields, but not any nonserialized_*.
                visitor.SetDefault(this);

                return true;
            }

            if (!VisitGaborish(visitor))
            {
                return false;
            }

            if (!VisitEpf(visitor))
            {
                return false;
            }

            if (!visitor.BeginExtensions(ref Extensions))
            {
                return false;
            }

            // Extensions: in chronological order of being added to the format.
            return visitor.EndExtensions();
        }

        private static Status VisitGabChannelWeights(
            Visitor visitor,
            ref float weight1,
            ref float weight2,
            string channelName)
        {
            if (!visitor.F16(KGabDefaultWeight1, ref weight1))
            {
                return false;
            }

            if (!visitor.F16(KGabDefaultWeight2, ref weight2))
            {
                return false;
            }

            if (MathF.Abs(1.0f + ((weight1 + weight2) * 4.0f)) < 1e-8f)
            {
                return Status.Failure($"Gaborish {channelName} weights lead to near 0 unnormalized kernel");
            }

            return true;
        }

        private Status VisitGaborish(Visitor visitor)
        {
            if (!visitor.Bool(true, ref Gab))
            {
                return false;
            }

            if (!visitor.Conditional(Gab))
            {
                return true;
            }

            if (!visitor.Bool(false, ref GabCustom))
            {
                return false;
            }

            if (!visitor.Conditional(GabCustom))
            {
                return true;
            }

            if (!VisitGabChannelWeights(visitor, ref GabXWeight1, ref GabXWeight2, "x"))
            {
                return false;
            }

            if (!VisitGabChannelWeights(visitor, ref GabYWeight1, ref GabYWeight2, "y"))
            {
                return false;
            }

            return VisitGabChannelWeights(visitor, ref GabBWeight1, ref GabBWeight2, "b");
        }

        private Status VisitEpf(Visitor visitor)
        {
            if (!visitor.Bits(2, 2, ref EpfIters))
            {
                return false;
            }

            if (!visitor.Conditional(EpfIters > 0))
            {
                return true;
            }

            if (visitor.Conditional(!NonserializedIsModular) && !VisitEpfSharp(visitor))
            {
                return false;
            }

            if (!VisitEpfWeight(visitor))
            {
                return false;
            }

            return VisitEpfSigma(visitor);
        }

        private Status VisitEpfSharp(Visitor visitor)
        {
            if (!visitor.Bool(false, ref EpfSharpCustom))
            {
                return false;
            }

            if (!visitor.Conditional(EpfSharpCustom))
            {
                return true;
            }

            for (int i = 0; i < KEpfSharpEntries; ++i)
            {
                float defVal = (float)i / (KEpfSharpEntries - 1);

                if (!visitor.F16(defVal, ref EpfSharpLut[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private Status VisitEpfWeight(Visitor visitor)
        {
            if (!visitor.Bool(false, ref EpfWeightCustom))
            {
                return false;
            }

            if (!visitor.Conditional(EpfWeightCustom))
            {
                return true;
            }

            if (!visitor.F16(40.0f, ref EpfChannelScale[0]) ||
                !visitor.F16(5.0f, ref EpfChannelScale[1]) ||
                !visitor.F16(3.5f, ref EpfChannelScale[2]))
            {
                return false;
            }

            if (!visitor.F16(0.45f, ref EpfPass1Zeroflush))
            {
                return false;
            }

            return visitor.F16(0.6f, ref EpfPass2Zeroflush);
        }

        private Status VisitEpfSigma(Visitor visitor)
        {
            if (!visitor.Bool(false, ref EpfSigmaCustom))
            {
                return false;
            }

            if (visitor.Conditional(EpfSigmaCustom) && !VisitEpfCustomSigmaFields(visitor))
            {
                return false;
            }

            if (visitor.Conditional(NonserializedIsModular))
            {
                if (!visitor.F16(1.0f, ref EpfSigmaForModular))
                {
                    return false;
                }

                if (EpfSigmaForModular < 1e-8f)
                {
                    return Status.Failure("EPF: sigma for modular is too small");
                }
            }

            return true;
        }

        private Status VisitEpfCustomSigmaFields(Visitor visitor)
        {
            if (visitor.Conditional(!NonserializedIsModular) && !visitor.F16(0.46f, ref EpfQuantMul))
            {
                return false;
            }

            if (!visitor.F16(0.9f, ref EpfPass0SigmaScale) ||
                !visitor.F16(6.5f, ref EpfPass2SigmaScale))
            {
                return false;
            }

            return visitor.F16(0.6666666666666666f, ref EpfBorderSadMul);
        }
    }
}
