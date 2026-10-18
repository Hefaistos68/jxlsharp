// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Constants and helper methods that define the XYB color space inverse opsin matrix.
    /// Ported from <c>lib/jxl/opsin_params.h</c> and <c>lib/jxl/opsin_params.cc</c>.
    /// </summary>
    public sealed class OpsinParams
    {
        /// <summary>
        /// Default Y-to-B ratio in XYB color space (1.0f).
        /// </summary>
        public const float kYToBRatio = 1.0f;

        /// <summary>Broadcasted 3x3 inverse opsin absorbance matrix (9 * 4 = 36 floats).</summary>
        public readonly float[] InverseOpsinMatrix = new float[9 * 4];

        /// <summary>Negative opsin absorbance biases (4 floats).</summary>
        public readonly float[] OpsinBiases = new float[4];

        /// <summary>Cube roots of <see cref="OpsinBiases"/> (4 floats).</summary>
        public readonly float[] OpsinBiasesCbrt = new float[4];

        /// <summary>AC dequantization biases (4 floats).</summary>
        public readonly float[] QuantBiases = new float[4];

        /// <summary>
        /// Negative opsin absorbance bias RGB + 1.0f.
        /// </summary>
        public static float[] KNegOpsinAbsorbanceBiasRgb => Cms.OpsinParams.KNegOpsinAbsorbanceBiasRgb;

        /// <summary>
        /// Returns the default inverse opsin absorbance matrix.
        /// </summary>
        /// <returns>The default inverse opsin absorbance <see cref="Matrix3x3"/>.</returns>
        public static Matrix3x3 DefaultInverseOpsinAbsorbanceMatrix()
        {
            return Cms.OpsinParams.DefaultInverseOpsinAbsorbanceMatrix();
        }

        /// <summary>
        /// Returns the 3x3 row-major matrix inverse of <see cref="Cms.OpsinParams.KOpsinAbsorbanceMatrix"/>.
        /// </summary>
        /// <returns>The default inverse opsin absorbance <see cref="Matrix3x3"/>.</returns>
        public static Matrix3x3 GetOpsinAbsorbanceInverseMatrix()
        {
            return Cms.OpsinParams.DefaultInverseOpsinAbsorbanceMatrix();
        }

        /// <summary>
        /// Populates a 36-element (9 x 4) SIMD-Broadcast inverse opsin matrix scaled by <c>255.0f / intensityTarget</c>.
        /// </summary>
        /// <param name="inverse">3x3 inverse opsin absorbance matrix.</param>
        /// <param name="simdInverse">Destination span of at least 36 floats.</param>
        /// <param name="intensityTarget">Intensity target in nits.</param>
        public static void InitSIMDInverseMatrix(
            in Matrix3x3 inverse,
            Span<float> simdInverse,
            float intensityTarget)
        {
            float scale = 255.0f / intensityTarget;

            for (int j = 0; j < 3; ++j)
            {
                for (int i = 0; i < 3; ++i)
                {
                    int idx              = ((j * 3) + i) * 4;
                    float val            = inverse[j][i] * scale;
                    simdInverse[idx]     = val;
                    simdInverse[idx + 1] = val;
                    simdInverse[idx + 2] = val;
                    simdInverse[idx + 3] = val;
                }
            }
        }

        /// <summary>
        /// Initializes this <see cref="OpsinParams"/> with default specification matrices and biases for <paramref name="intensityTarget"/>.
        /// </summary>
        /// <param name="intensityTarget">Intensity target in nits (e.g., 255.0f).</param>
        public void Init(float intensityTarget)
        {
            Matrix3x3 inv = GetOpsinAbsorbanceInverseMatrix();
            InitSIMDInverseMatrix(in inv, InverseOpsinMatrix, intensityTarget);
            Array.Copy(KNegOpsinAbsorbanceBiasRgb, OpsinBiases, 4);
            Quantizer.DefaultQuantBias.CopyTo(QuantBiases);

            for (int c = 0; c < 4; c++)
            {
                OpsinBiasesCbrt[c] = MathF.Cbrt(OpsinBiases[c]);
            }
        }
    }
}
