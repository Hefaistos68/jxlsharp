// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Exif"/>.
    /// </summary>
    [TestFixture]
    public class ExifTests
    {
        /// <summary>
        /// Verifies little-endian and big-endian Exif orientation parsing.
        /// </summary>
        [Test]
        public void InterpretExifParsesOrientationForLittleAndBigEndian()
        {
            // Little-endian TIFF header ("II*\0", IFD offset = 8, 1 tag = 0x0112 (274), type = 3 (SHORT), count = 1, value = 6)
            byte[] littleEndianExif =
            [
                0x49, 0x49, 0x2A, 0x00,
                0x08, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x12, 0x01,
                0x03, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x06, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            ];

            Exif.IsExif(littleEndianExif, out bool isBigLe).ShouldBeTrue();
            isBigLe.ShouldBeFalse();

            JxlOrientation orientationLe = JxlOrientation.Identity;
            Exif.InterpretExif(littleEndianExif, ref orientationLe);
            orientationLe.ShouldBe(JxlOrientation.Rotate90Cw);

            // Big-endian TIFF header ("MM\0*", IFD offset = 8, 1 tag = 0x0112 (274), type = 3 (SHORT), count = 1, value = 3)
            byte[] bigEndianExif =
            [
                0x4D, 0x4D, 0x00, 0x2A,
                0x00, 0x00, 0x00, 0x08,
                0x00, 0x01,
                0x01, 0x12,
                0x00, 0x03,
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x03, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            ];

            Exif.IsExif(bigEndianExif, out bool isBigBe).ShouldBeTrue();
            isBigBe.ShouldBeTrue();

            JxlOrientation orientationBe = JxlOrientation.Identity;
            Exif.InterpretExif(bigEndianExif, ref orientationBe);
            orientationBe.ShouldBe(JxlOrientation.Rotate180);

            // Invalid/short blob is a no-op
            byte[] invalid = [1, 2, 3];
            Exif.IsExif(invalid, out _).ShouldBeFalse();
            Exif.InterpretExif(invalid, ref orientationBe);
            orientationBe.ShouldBe(JxlOrientation.Rotate180);
        }
    }
}
