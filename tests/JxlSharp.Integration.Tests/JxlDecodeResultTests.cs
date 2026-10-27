// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using JxlSharp.Base;
using JxlSharp.Extras;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for <see cref="JxlDecodeResult"/>.
    /// </summary>
    [TestFixture]
    public sealed class JxlDecodeResultTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlDecodeResult"/> exposes the primary image, basic info, metadata,
        /// and reconstructed JPEG bytes.
        /// </summary>
        [Test]
        public void Constructor_WithPopulatedPixelFile_ExposesPrimaryImageAndMetadata()
        {
            byte[] pixels       = new byte[2 * 2 * 3];
            JxlImageData img    = new JxlImageData(pixels, 2, 2, 3, JxlDataType.UInt8, 8);
            PackedPixelFile ppf = img.ToPackedPixelFile();
            byte[] fakeJpeg     = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };

            JxlDecodeResult sut = new JxlDecodeResult(ppf, fakeJpeg, 42);

            sut.PixelFile.ShouldBeSameAs(ppf);
            sut.PrimaryImage.ShouldNotBeNull();
            sut.PrimaryImage!.Width.ShouldBe(2);
            sut.PrimaryImage.Height.ShouldBe(2);
            sut.BasicInfo.XSize.ShouldBe(2u);
            sut.ReconstructedJpegBytes.ShouldBe(fakeJpeg);
            sut.ConsumedBytes.ShouldBe((nuint)42);
        }
    }
}
