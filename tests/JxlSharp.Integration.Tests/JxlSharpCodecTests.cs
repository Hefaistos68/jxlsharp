// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Encoder;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for <see cref="JxlSharpCodec"/> covering both <see cref="IJxlSimpleCodec"/>
    /// and <see cref="IJxlExtendedCodec"/> interfaces.
    /// </summary>
    [TestFixture]
    public sealed class JxlSharpCodecTests
    {
        /// <summary>
        /// Verifies that <see cref="IJxlSimpleCodec"/> lossless and lossy encoding and decoding round-trip accurately.
        /// </summary>
        [Test]
        public void SimpleCodec_EncodeLosslessAndDecode_RoundTripsExactPixels()
        {
            IJxlSimpleCodec sut = new JxlSharpCodec();
            int width           = 16;
            int height          = 12;
            int channels        = 4;
            byte[] pixels       = CreateGradientPixels(width, height, channels);

            byte[] losslessJxl   = sut.EncodeLossless(pixels, width, height, channels, effort: 3);
            JxlBasicInfo info    = sut.GetInfo(losslessJxl);
            JxlImageData decoded = sut.Decode(losslessJxl);

            info.XSize.ShouldBe((uint)width);
            info.YSize.ShouldBe((uint)height);
            decoded.Width.ShouldBe(width);
            decoded.Height.ShouldBe(height);
            decoded.Channels.ShouldBe(channels);
            decoded.Pixels.ShouldBe(pixels);
        }

        /// <summary>
        /// Verifies that <see cref="IJxlSimpleCodec"/> stream and external format methods work end-to-end.
        /// </summary>
        [Test]
        public void SimpleCodec_StreamAndFormatConversion_WorksEndToEnd()
        {
            IJxlSimpleCodec sut = new JxlSharpCodec();
            int width           = 8;
            int height          = 8;
            byte[] pixels       = CreateGradientPixels(width, height, 3);
            JxlImageData image  = new JxlImageData(pixels, width, height, 3);

            using MemoryStream ms = new MemoryStream();
            sut.EncodeToStream(image, ms, quality: 100.0f, effort: 3);
            ms.Position = 0;

            JxlImageData fromStream = sut.Decode(ms);
            byte[] ppmBytes         = sut.DecodeToFormat(ms.ToArray(), ".ppm");
            byte[] reencodedJxl     = sut.EncodeFromFormat(ppmBytes, quality: 90.0f, effort: 3);
            JxlImageData finalImg   = sut.Decode(reencodedJxl);

            fromStream.Pixels.ShouldBe(pixels);
            ppmBytes.Length.ShouldBeGreaterThan(0);
            finalImg.Width.ShouldBe(width);
            finalImg.Height.ShouldBe(height);
        }

        /// <summary>
        /// Verifies that <see cref="IJxlExtendedCodec"/> supports full <see cref="JxlEncodeOptions"/>
        /// and <see cref="JxlDecodeOptions"/>.
        /// </summary>
        [Test]
        public void ExtendedCodec_WithFullOptionsAndStats_EncodesAndDecodesSuccessfully()
        {
            IJxlExtendedCodec sut = new JxlSharpCodec();
            int width             = 16;
            int height            = 16;
            byte[] pixels         = CreateGradientPixels(width, height, 4);
            JxlImageData image    = new JxlImageData(pixels, width, height, 4);

            JxlEncodeOptions encodeOptions = new JxlEncodeOptions
            {
                Distance      = 0.5f,
                AlphaDistance = 0.0f,
                Effort        = 4,
                DecodingSpeed = 1,
                Progressive   = true,
                Gaborish      = Override.On,
                Epf           = 1,
                Container     = Override.On,
                CompressBoxes = true,
                NumThreads    = 2,
                Stats         = new JxlSharp.Encoder.JxlEncoderStats(),
            };

            byte[] encoded = sut.Encode(image, encodeOptions);

            JxlDecodeOptions decodeOptions = new JxlDecodeOptions
            {
                RequestedChannels  = 4,
                RequestedDataType  = JxlDataType.UInt8,
                Coalescing         = true,
                RenderSpotcolors   = true,
                UnpremultiplyAlpha = false,
                NumThreads         = 2,
            };

            JxlDecodeResult result = sut.Decode(encoded, decodeOptions);

            encoded.Length.ShouldBeGreaterThan(0);
            result.PrimaryImage.ShouldNotBeNull();
            result.PrimaryImage!.Width.ShouldBe(width);
            result.PrimaryImage.Height.ShouldBe(height);
            result.ConsumedBytes.ShouldBe((nuint)encoded.Length);
        }

        /// <summary>
        /// Verifies that <see cref="IJxlSimpleCodec"/> can be mocked via Moq for consumer dependency injection tests.
        /// </summary>
        [Test]
        public void SimpleCodecInterface_CanBeMockedWithMoq()
        {
            Mock<IJxlSimpleCodec> mockCodec = new Mock<IJxlSimpleCodec>();
            byte[] expectedBytes            = new byte[] { 0xFF, 0x0A, 0x01, 0x02 };

            mockCodec
                .Setup(c => c.Encode(It.IsAny<JxlImageData>(), 90.0f, 7))
                .Returns(expectedBytes);

            JxlImageData img = new JxlImageData(new byte[12], 2, 2, 3);
            byte[] actual    = mockCodec.Object.Encode(img, 90.0f, 7);

            actual.ShouldBe(expectedBytes);
        }

        private static byte[] CreateGradientPixels(int width, int height, int channels)
        {
            byte[] pixels = new byte[width * height * channels];
            int idx       = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[idx++] = (byte)(x * 15);

                    if (channels >= 2)
                    {
                        pixels[idx++] = (byte)(y * 15);
                    }

                    if (channels >= 3)
                    {
                        pixels[idx++] = (byte)((x + y) * 7);
                    }

                    if (channels == 4)
                    {
                        pixels[idx++] = 255;
                    }
                }
            }

            return pixels;
        }
    }
}
