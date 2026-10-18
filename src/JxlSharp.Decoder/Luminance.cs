// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Chooses a default intensity target based on the transfer function of the image, if known.
    /// Ported from <c>lib/jxl/luminance.h</c> and <c>lib/jxl/luminance.cc</c>.
    /// </summary>
    public static class Luminance
    {
        /// <summary>
        /// Chooses a default intensity target based on the transfer function of <paramref name="m"/>.
        /// For SDR images or images not known to be HDR, sets <see cref="Common.kDefaultIntensityTarget"/>;
        /// for images known to have PQ or HLG transfer function, sets a higher value.
        /// </summary>
        /// <param name="m">Target <see cref="ImageMetadata"/>.</param>
        public static void SetIntensityTarget(ImageMetadata m)
        {
            if (m.ColorEncoding.Tf.IsPQ())
            {
                // Peak luminance of PQ as defined by SMPTE ST 2084:2014.
                m.SetIntensityTarget(10000.0f);
            }
            else if (m.ColorEncoding.Tf.IsHLG())
            {
                // Nominal display peak luminance used as a reference by Rec. ITU-R BT.2100-2.
                m.SetIntensityTarget(1000.0f);
            }
            else
            {
                // SDR
                m.SetIntensityTarget(Common.kDefaultIntensityTarget);
            }
        }
    }
}
