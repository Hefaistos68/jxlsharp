// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Threads;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp
{
    /// <summary>
    /// Unit tests for the <see cref="JxlCodec"/> unified facade.
    /// </summary>
    [TestFixture]
    public sealed class JxlCodecTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlCodec.Version"/> returns the v0.13.0 version number.
        /// </summary>
        [Test]
        public void Version_ReturnsExpectedVersion()
        {
            uint version = JxlCodec.Version;

            version.ShouldBe(0x000D00u);
        }

        /// <summary>
        /// Verifies that <see cref="JxlCodec.CheckSignature"/> identifies codestream, container, and invalid signatures.
        /// </summary>
        [Test]
        public void CheckSignature_DetectsValidAndInvalidSignatures()
        {
            ReadOnlySpan<byte> codestreamSig = stackalloc byte[] { 0xFF, 0x0A };
            ReadOnlySpan<byte> invalidSig    = stackalloc byte[] { 0x00, 0x01, 0x02, 0x03 };

            JxlCodec.CheckSignature(codestreamSig).ShouldBe(JxlSignature.Codestream);
            JxlCodec.CheckSignature(invalidSig).ShouldBe(JxlSignature.Invalid);
        }

        /// <summary>
        /// Verifies that <see cref="JxlCodec.CreateDecoder"/>, <see cref="JxlCodec.CreateEncoder"/>,
        /// <see cref="JxlCodec.GetDefaultCms"/>, and <see cref="JxlCodec.CreateThreadParallelRunner"/> return valid instances.
        /// </summary>
        [Test]
        public void FactoryMethods_ReturnValidInstances()
        {
            using JxlDecoder dec = JxlCodec.CreateDecoder();
            using JxlEncoder enc = JxlCodec.CreateEncoder();
            JxlCmsInterface cms  = JxlCodec.GetDefaultCms();

            using ThreadParallelRunner? runnerDefault = JxlCodec.CreateThreadParallelRunner();
            using ThreadParallelRunner? runnerCustom  = JxlCodec.CreateThreadParallelRunner(null, 2);

            dec.ShouldNotBeNull();
            enc.ShouldNotBeNull();
            cms.ShouldNotBeNull();
            runnerDefault.ShouldNotBeNull();
            runnerCustom.ShouldNotBeNull();
            runnerCustom!.NumThreads.ShouldBe((nuint)2);
        }

        /// <summary>
        /// Verifies that <see cref="JxlCodec.Simple"/>, <see cref="JxlCodec.Extended"/>, and static
        /// encode/decode methods operate as expected.
        /// </summary>
        [Test]
        public void StaticEncodeAndDecode_RoundTripsLosslessImage()
        {
            byte[] pixels = new byte[4 * 4 * 3];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 3);
            }

            byte[] encoded       = JxlCodec.EncodeLossless(pixels, 4, 4, 3, effort: 3);
            JxlBasicInfo info    = JxlCodec.GetInfo(encoded);
            JxlImageData decoded = JxlCodec.Decode(encoded);

            JxlCodec.Simple.ShouldNotBeNull();
            JxlCodec.Extended.ShouldNotBeNull();
            info.XSize.ShouldBe(4u);
            info.YSize.ShouldBe(4u);
            decoded.Pixels.ShouldBe(pixels);
        }
    }
}
