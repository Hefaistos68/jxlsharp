// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Extras;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for <see cref="JxlImageData"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlImageDataTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlImageData"/> initializes properties and converts to and from <see cref="PackedPixelFile"/>.
        /// </summary>
        [Test]
        public void ToPackedPixelFile_And_FromPackedPixelFile_RoundTripsCorrectly()
        {
            byte[] pixels = new byte[4 * 3 * 4];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 5);
            }

            JxlImageData sut = new JxlImageData(pixels, 4, 3, 4, JxlDataType.UInt8, 8);
            sut.Metadata.Exif = new byte[] { 0, 0, 0, 0, (byte)'M', (byte)'M' };

            PackedPixelFile ppf    = sut.ToPackedPixelFile();
            JxlImageData roundTrip = JxlImageData.FromPackedPixelFile(ppf);

            sut.HasAlpha.ShouldBeTrue();
            roundTrip.Width.ShouldBe(4);
            roundTrip.Height.ShouldBe(3);
            roundTrip.Channels.ShouldBe(4);
            roundTrip.BitsPerSample.ShouldBe(8u);
            roundTrip.Pixels.ShouldBe(pixels);
            roundTrip.Metadata.Exif.ShouldBe(sut.Metadata.Exif);
        }

        /// <summary>
        /// Verifies that <see cref="JxlImageData.FromPixels"/> validates dimensions, channels, and buffer length.
        /// </summary>
        [Test]
        public void Constructor_InvalidArguments_ThrowsArgumentException()
        {
            byte[] validBuffer = new byte[12];
            byte[] shortBuffer = new byte[4];

            Should.Throw<ArgumentNullException>(() => new JxlImageData(null!, 2, 2, 3));
            Should.Throw<ArgumentOutOfRangeException>(() => new JxlImageData(validBuffer, 0, 2, 3));
            Should.Throw<ArgumentOutOfRangeException>(() => new JxlImageData(validBuffer, 2, -1, 3));
            Should.Throw<ArgumentOutOfRangeException>(() => new JxlImageData(validBuffer, 2, 2, 5));
            Should.Throw<ArgumentException>(() => JxlImageData.FromPixels(shortBuffer, 2, 2, 3));
        }
    }
}
