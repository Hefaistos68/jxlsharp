// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.IO.Compression;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ImageBundle"/>, <see cref="Luminance"/>, <see cref="OpsinParams"/>,
    /// and <see cref="JxlBoxContentDecoder"/>.
    /// Ported from <c>lib/jxl/image_bundle_test.cc</c>.
    /// </summary>
    [TestFixture]
    public sealed class ImageBundleTests
    {
        /// <summary>
        /// Verifies round-trip serialization of <see cref="ImageMetadata"/> with a named extra channel.
        /// Ported from <c>ImageBundleTest.ExtraChannelName</c>.
        /// </summary>
        [Test]
        public void ExtraChannelName_ShouldRoundTripThroughImageMetadata()
        {
            AuxOut auxOut    = new AuxOut();
            BitWriter writer = new BitWriter();

            Status ok = writer.WithMaxBits(
                99,
                LayerType.Header,
                auxOut,
                () =>
                {
                    ImageMetadata metadata = new ImageMetadata();
                    ExtraChannelInfo eci   = new ExtraChannelInfo
                    {
                        Type = ExtraChannel.Black,
                        Name = "testK",
                    };
                    metadata.ExtraChannelInfo.Add(eci);

                    if (!ImageMetadataMethods.WriteImageMetadata(metadata, writer, LayerType.Header, auxOut))
                    {
                        return false;
                    }

                    writer.ZeroPadToByte();

                    return true;
                });

            ok.IsOk.ShouldBeTrue();

            using BitReader reader    = new BitReader(writer.GetSpan());
            ImageMetadata metadataOut = new ImageMetadata();

            ImageMetadataMethods.ReadImageMetadata(reader, metadataOut).IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            ExtraChannelInfo? found = metadataOut.Find(ExtraChannel.Black);
            found.ShouldNotBeNull();
            found.Name.ShouldBe("testK");
        }

        /// <summary>
        /// Verifies <see cref="Luminance.SetIntensityTarget"/> for SDR, PQ, and HLG transfer functions.
        /// </summary>
        [Test]
        public void LuminanceSetIntensityTarget_ShouldSelectExpectedNits()
        {
            ImageMetadata metadata = new ImageMetadata();
            Luminance.SetIntensityTarget(metadata);
            metadata.IntensityTarget().ShouldBe(Common.kDefaultIntensityTarget);

            metadata.ColorEncoding.Tf.SetTransferFunction(TransferFunction.PQ);
            Luminance.SetIntensityTarget(metadata);
            metadata.IntensityTarget().ShouldBe(10000.0f);

            metadata.ColorEncoding.Tf.SetTransferFunction(TransferFunction.HLG);
            Luminance.SetIntensityTarget(metadata);
            metadata.IntensityTarget().ShouldBe(1000.0f);
        }

        /// <summary>
        /// Verifies <see cref="OpsinParams.GetOpsinAbsorbanceInverseMatrix"/> and <see cref="OpsinParams.InitSIMDInverseMatrix"/>.
        /// </summary>
        [Test]
        public void OpsinParams_ShouldPopulateSimdInverseMatrix()
        {
            Matrix3x3 inv = OpsinParams.GetOpsinAbsorbanceInverseMatrix();
            float[] simd  = new float[36];

            OpsinParams.InitSIMDInverseMatrix(in inv, simd, 255.0f);

            simd[0].ShouldBe(inv[0][0], 1e-5f);
            simd[1].ShouldBe(inv[0][0], 1e-5f);
            simd[35].ShouldBe(inv[2][2], 1e-5f);
        }

        /// <summary>
        /// Verifies <see cref="ImageBundle"/> color, alpha, shrinking, and deep copying.
        /// </summary>
        [Test]
        public void ImageBundle_SetColorAndAlphaAndCopy_ShouldPreserveDimensionsAndMetadata()
        {
            ImageMetadata metadata = new ImageMetadata();
            metadata.SetAlphaBits(8);

            using ImageBundle bundle = new ImageBundle(null, metadata);
            StatusOr<Image3<float>> colorOr = Image3<float>.Create(null, 16, 8);
            colorOr.IsOk.ShouldBeTrue();

            StatusOr<ImagePlane<float>> alphaOr = ImagePlane<float>.Create(null, 16, 8);
            alphaOr.IsOk.ShouldBeTrue();

            bundle.SetFromImage(colorOr.Value, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            bundle.SetAlpha(alphaOr.Value).IsOk.ShouldBeTrue();
            bundle.VerifyMetadata().IsOk.ShouldBeTrue();
            bundle.XSize().ShouldBe((nuint)16);
            bundle.YSize().ShouldBe((nuint)8);

            StatusOr<ImageBundle> copyOr = bundle.Copy();
            copyOr.IsOk.ShouldBeTrue();

            using ImageBundle copy = copyOr.Value;
            copy.XSize().ShouldBe((nuint)16);
            copy.YSize().ShouldBe((nuint)8);
            copy.HasAlpha().ShouldBeTrue();
            copy.ShrinkTo(8, 4).IsOk.ShouldBeTrue();
            copy.XSize().ShouldBe((nuint)8);
            copy.YSize().ShouldBe((nuint)4);
        }

        /// <summary>
        /// Verifies <see cref="JxlBoxContentDecoder"/> for both raw and Brotli-compressed (<c>brob</c>) box payloads.
        /// </summary>
        [Test]
        public void BoxContentDecoder_RawAndBrob_ShouldDecodeBoxPayload()
        {
            byte[] payload                 = Encoding.UTF8.GetBytes("Hello JPEG XL Box Content Decoder! Repeated text for Brotli.");
            using JxlBoxContentDecoder dec = new JxlBoxContentDecoder();

            // 1. Raw box decoding
            dec.StartBox(brobDecode: false, boxUntilEof: false, (nuint)payload.Length);
            byte[] rawOut = new byte[payload.Length];
            JxlDecoderStatus rawStatus = dec.Process(payload, 0, rawOut, out nuint rawWritten);
            rawStatus.ShouldBe(JxlDecoderStatus.BoxComplete);
            rawWritten.ShouldBe((nuint)payload.Length);
            rawOut.ShouldBe(payload);

            // 2. Brob (4-byte header + Brotli stream) box decoding
            byte[] compressed = new byte[payload.Length + 64];
            BrotliEncoder.TryCompress(payload, compressed, out int compBytes).ShouldBeTrue();

            byte[] brobInput = new byte[4 + compBytes];
            brobInput[0]     = (byte)'E';
            brobInput[1]     = (byte)'x';
            brobInput[2]     = (byte)'i';
            brobInput[3]     = (byte)'f';
            Array.Copy(compressed, 0, brobInput, 4, compBytes);

            dec.StartBox(brobDecode: true, boxUntilEof: false, (nuint)brobInput.Length);
            byte[] brobOut = new byte[payload.Length];
            JxlDecoderStatus brobStatus = dec.Process(brobInput, 0, brobOut, out nuint brobWritten);
            brobStatus.ShouldBe(JxlDecoderStatus.BoxComplete);
            brobWritten.ShouldBe((nuint)payload.Length);
            brobOut.ShouldBe(payload);
        }
    }
}
