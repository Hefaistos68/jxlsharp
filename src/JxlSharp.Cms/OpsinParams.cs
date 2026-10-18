// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using JxlSharp.Base;

namespace JxlSharp.Cms
{
    /// <summary>
    /// Constants and helper methods that define the XYB color space.
    /// Ported from <c>lib/jxl/cms/opsin_params.h</c>.
    /// </summary>
    public static class OpsinParams
    {
        // Parameters for opsin absorbance.

        /// <summary>Opsin absorbance matrix element (0, 2).</summary>
        public const float KM02 = 0.078f;

        /// <summary>Opsin absorbance matrix element (0, 0).</summary>
        public const float KM00 = 0.30f;

        /// <summary>Opsin absorbance matrix element (0, 1).</summary>
        public const float KM01 = 1.0f - KM02 - KM00;

        /// <summary>Opsin absorbance matrix element (1, 2).</summary>
        public const float KM12 = 0.078f;

        /// <summary>Opsin absorbance matrix element (1, 0).</summary>
        public const float KM10 = 0.23f;

        /// <summary>Opsin absorbance matrix element (1, 1).</summary>
        public const float KM11 = 1.0f - KM12 - KM10;

        /// <summary>Opsin absorbance matrix element (2, 0).</summary>
        public const float KM20 = 0.24342268924547819f;

        /// <summary>Opsin absorbance matrix element (2, 1).</summary>
        public const float KM21 = 0.20476744424496821f;

        /// <summary>Opsin absorbance matrix element (2, 2).</summary>
        public const float KM22 = 1.0f - KM20 - KM21;

        /// <summary>XYB B channel scale.</summary>
        public const float KBScale = 1.0f;

        /// <summary>XYB Y-to-B ratio.</summary>
        public const float KYToBRatio = 1.0f;

        /// <summary>XYB B-to-Y ratio.</summary>
        public const float KBToYRatio = 1.0f / KYToBRatio;

        /// <summary>Opsin absorbance bias for channel 0.</summary>
        public const float KOpsinAbsorbanceBias0 = 0.0037930732552754493f;

        /// <summary>Opsin absorbance bias for channel 1.</summary>
        public const float KOpsinAbsorbanceBias1 = KOpsinAbsorbanceBias0;

        /// <summary>Opsin absorbance bias for channel 2.</summary>
        public const float KOpsinAbsorbanceBias2 = KOpsinAbsorbanceBias0;

        /// <summary>Scaled XYB offset for channel X.</summary>
        public const float KScaledXybOffset0 = 0.015386134f;

        /// <summary>Scaled XYB offset for channel Y.</summary>
        public const float KScaledXybOffset1 = 0.0f;

        /// <summary>Scaled XYB offset for channel B.</summary>
        public const float KScaledXybOffset2 = 0.27770459f;

        /// <summary>Scaled XYB scale for channel X.</summary>
        public const float KScaledXybScale0 = 22.995788804f;

        /// <summary>Scaled XYB scale for channel Y.</summary>
        public const float KScaledXybScale1 = 1.183000077f;

        /// <summary>Scaled XYB scale for channel B.</summary>
        public const float KScaledXybScale2 = 1.502141333f;

        /// <summary>XYB offset 0.</summary>
        public const float KXybOffset0 = KScaledXybOffset0 + KScaledXybOffset1;

        /// <summary>XYB offset 1.</summary>
        public const float KXybOffset1 = KScaledXybOffset1 - KScaledXybOffset0 + (1.0f / KScaledXybScale0);

        /// <summary>XYB offset 2.</summary>
        public const float KXybOffset2 = KScaledXybOffset1 + KScaledXybOffset2;

        /// <summary>XYB scale 0.</summary>
        public const float KXybScale0 = (KScaledXybScale0 * KScaledXybScale1) / (KScaledXybScale0 + KScaledXybScale1);

        /// <summary>XYB scale 1.</summary>
        public const float KXybScale1 = (KScaledXybScale0 * KScaledXybScale1) / (KScaledXybScale0 + KScaledXybScale1);

        /// <summary>XYB scale 2.</summary>
        public const float KXybScale2 = (KScaledXybScale1 * KScaledXybScale2) / (KScaledXybScale1 + KScaledXybScale2);

        /// <summary>
        /// Opsin absorbance matrix (frozen).
        /// </summary>
        public static readonly Matrix3x3 KOpsinAbsorbanceMatrix = new Matrix3x3(
            new Vector3(KM00, KM01, KM02),
            new Vector3(KM10, KM11, KM12),
            new Vector3(KM20, KM21, KM22));

        /// <summary>
        /// Default inverse opsin absorbance matrix matching the JPEG XL specification.
        /// </summary>
        public static readonly Matrix3x3 KDefaultInverseOpsinAbsorbanceMatrix = new Matrix3x3(
            new Vector3(11.031566901960783f, -9.866943921568629f, -0.16462299647058826f),
            new Vector3(-3.254147380392157f, 4.418770392156863f, -0.16462299647058826f),
            new Vector3(-3.6588512862745097f, 2.7129230470588235f, 1.9459282392156863f));

        /// <summary>
        /// Opsin absorbance bias vector.
        /// </summary>
        public static readonly Vector3 KOpsinAbsorbanceBias = new Vector3(
            KOpsinAbsorbanceBias0,
            KOpsinAbsorbanceBias1,
            KOpsinAbsorbanceBias2);

        /// <summary>
        /// Negative opsin absorbance bias RGB + 1.0f.
        /// </summary>
        public static readonly float[] KNegOpsinAbsorbanceBiasRgb =
        {
            -KOpsinAbsorbanceBias0,
            -KOpsinAbsorbanceBias1,
            -KOpsinAbsorbanceBias2,
            1.0f,
        };

        /// <summary>
        /// Scaled XYB offset vector.
        /// </summary>
        public static readonly Vector3 KScaledXybOffset = new Vector3(
            KScaledXybOffset0,
            KScaledXybOffset1,
            KScaledXybOffset2);

        /// <summary>
        /// Scaled XYB scale vector.
        /// </summary>
        public static readonly Vector3 KScaledXybScale = new Vector3(
            KScaledXybScale0,
            KScaledXybScale1,
            KScaledXybScale2);

        /// <summary>
        /// XYB offset array.
        /// </summary>
        public static readonly float[] KXybOffset =
        {
            KXybOffset0,
            KXybOffset1,
            KXybOffset2,
        };

        /// <summary>
        /// XYB scale array.
        /// </summary>
        public static readonly float[] KXybScale =
        {
            KXybScale0,
            KXybScale1,
            KXybScale2,
        };

        private static readonly Vector3[,,] CachedUnscaledA2BCube = BuildUnscaledA2BCube();

        /// <summary>
        /// Returns the default inverse opsin absorbance matrix.
        /// </summary>
        /// <returns>The <see cref="KDefaultInverseOpsinAbsorbanceMatrix"/>.</returns>
        public static Matrix3x3 DefaultInverseOpsinAbsorbanceMatrix()
        {
            return KDefaultInverseOpsinAbsorbanceMatrix;
        }

        /// <summary>
        /// More precise calculation of <c>1 / ((1 / r1) + (1 / r2))</c>.
        /// </summary>
        /// <param name="r1">First value.</param>
        /// <param name="r2">Second value.</param>
        /// <returns>Harmonic product <c>(r1 * r2) / (r1 + r2)</c>.</returns>
        public static float ReciprocalSum(float r1, float r2)
        {
            return (r1 * r2) / (r1 + r2);
        }

        /// <summary>
        /// Returns the 2x2x2 3D LUT cube for XYB ICC A2B0 profile tags.
        /// </summary>
        /// <returns>A <c>[2, 2, 2]</c> array of <see cref="Vector3"/> corners.</returns>
        public static Vector3[,,] UnscaledA2BCube()
        {
            return CachedUnscaledA2BCube;
        }

        private static float ScaledXybScaleAt(int idx)
        {
            return idx == 0 ? KScaledXybScale0 : (idx == 1 ? KScaledXybScale1 : KScaledXybScale2);
        }

        private static float ScaledXybOffsetAt(int idx)
        {
            return idx == 0 ? KScaledXybOffset0 : (idx == 1 ? KScaledXybOffset1 : KScaledXybOffset2);
        }

        private static float XybCorner(int x, int y, int b, int idx)
        {
            float coord = idx == 0 ? x : (idx == 1 ? y : b);

            return (coord / ScaledXybScaleAt(idx)) - ScaledXybOffsetAt(idx);
        }

        private static float ScaledA2BCorner(int x, int y, int b, int idx)
        {
            if (idx == 0)
            {
                return XybCorner(x, y, b, 1) + XybCorner(x, y, b, 0);
            }

            if (idx == 1)
            {
                return XybCorner(x, y, b, 1) - XybCorner(x, y, b, 0);
            }

            return XybCorner(x, y, b, 2) + XybCorner(x, y, b, 1);
        }

        private static Vector3 UnscaledA2BCorner(int x, int y, int b)
        {
            return new Vector3(
                (ScaledA2BCorner(x, y, b, 0) + KXybOffset0) * KXybScale0,
                (ScaledA2BCorner(x, y, b, 1) + KXybOffset1) * KXybScale1,
                (ScaledA2BCorner(x, y, b, 2) + KXybOffset2) * KXybScale2);
        }

        private static Vector3[,,] BuildUnscaledA2BCube()
        {
            Vector3[,,] cube = new Vector3[2, 2, 2];

            for (int x = 0; x < 2; ++x)
            {
                for (int y = 0; y < 2; ++y)
                {
                    for (int b = 0; b < 2; ++b)
                    {
                        cube[x, y, b] = UnscaledA2BCorner(x, y, b);
                    }
                }
            }

            return cube;
        }
    }
}
