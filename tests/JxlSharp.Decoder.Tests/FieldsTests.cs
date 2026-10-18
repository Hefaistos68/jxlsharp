// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Fields"/>, <see cref="U32Coder"/>, <see cref="U64Coder"/>, <see cref="F16Coder"/>,
    /// <see cref="Bundle"/>, <see cref="SizeHeader"/>, <see cref="PreviewHeader"/>, <see cref="ImageMetadata"/>,
    /// and <see cref="FrameHeader"/>.
    /// Ported from <c>lib/jxl/fields_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class FieldsTests
    {
        /// <summary>
        /// Verifies <see cref="U32Coder"/> round-trips and encoded bit counts.
        /// Ported from <c>FieldsTest.U32CoderTest</c>.
        /// </summary>
        [Test]
        public void U32CoderTest()
        {
            VerifyU32Coder(0, 2);
            VerifyU32Coder(1, 6);
            VerifyU32Coder(15, 6);
            VerifyU32Coder(0x7FFFFFFFu, 2);
            VerifyU32Coder(128, 34);
            VerifyU32Coder(0x7FFFFFFEu, 34);
            VerifyU32Coder(0x80000000u, 34);
            VerifyU32Coder(0xFFFFFFFFu, 34);
        }

        /// <summary>
        /// Verifies <see cref="U64Coder"/> round-trips and encoded bit counts across all varint boundaries.
        /// Ported from <c>FieldsTest.U64CoderTest</c>.
        /// </summary>
        [Test]
        public void U64CoderTest()
        {
            VerifyU64Coder(0, 2);

            VerifyU64Coder(1, 6);
            VerifyU64Coder(2, 6);
            VerifyU64Coder(8, 6);
            VerifyU64Coder(15, 6);
            VerifyU64Coder(16, 6);

            VerifyU64Coder(17, 10);
            VerifyU64Coder(18, 10);
            VerifyU64Coder(100, 10);
            VerifyU64Coder(271, 10);
            VerifyU64Coder(272, 10);

            VerifyU64Coder(273, 15);
            VerifyU64Coder(274, 15);
            VerifyU64Coder(1000, 15);
            VerifyU64Coder(4094, 15);
            VerifyU64Coder(4095, 15);

            VerifyU64Coder(4096, 24);
            VerifyU64Coder(4097, 24);
            VerifyU64Coder(10000, 24);
            VerifyU64Coder(1048574, 24);
            VerifyU64Coder(1048575, 24);

            VerifyU64Coder(1048576, 33);
            VerifyU64Coder(1048577, 33);
            VerifyU64Coder(10000000, 33);
            VerifyU64Coder(268435454, 33);
            VerifyU64Coder(268435455, 33);

            VerifyU64Coder(268435456UL, 42);
            VerifyU64Coder(268435457UL, 42);
            VerifyU64Coder(1000000000UL, 42);
            VerifyU64Coder(68719476734UL, 42);
            VerifyU64Coder(68719476735UL, 42);

            VerifyU64Coder(68719476736UL, 51);
            VerifyU64Coder(68719476737UL, 51);
            VerifyU64Coder(1000000000000UL, 51);
            VerifyU64Coder(17592186044414UL, 51);
            VerifyU64Coder(17592186044415UL, 51);

            VerifyU64Coder(17592186044416UL, 60);
            VerifyU64Coder(17592186044417UL, 60);
            VerifyU64Coder(100000000000000UL, 60);
            VerifyU64Coder(4503599627370494UL, 60);
            VerifyU64Coder(4503599627370495UL, 60);

            VerifyU64Coder(4503599627370496UL, 69);
            VerifyU64Coder(4503599627370497UL, 69);
            VerifyU64Coder(10000000000000000UL, 69);
            VerifyU64Coder(1152921504606846974UL, 69);
            VerifyU64Coder(1152921504606846975UL, 69);

            VerifyU64Coder(1152921504606846976UL, 73);
            VerifyU64Coder(1152921504606846977UL, 73);
            VerifyU64Coder(10000000000000000000UL, 73);
            VerifyU64Coder(18446744073709551614UL, 73);
            VerifyU64Coder(18446744073709551615UL, 73);
        }

        /// <summary>
        /// Verifies <see cref="F16Coder"/> round-trips for normal, subnormal, and out-of-range values.
        /// Ported from <c>FieldsTest.F16CoderTest</c>.
        /// </summary>
        [Test]
        public void F16CoderTest()
        {
            float[] signs = { -1.0f, 1.0f };
            float[] mags  =
            {
                0.0f,
                0.5f,
                1.0f,
                2.0f,
                2.5f,
                16.015625f,
                1.0f / 4096.0f,
                1.0f / 16384.0f,
                65504.0f,
            };

            foreach (float sign in signs)
            {
                foreach (float mag in mags)
                {
                    VerifyF16Coder(sign * mag).IsOk.ShouldBeTrue();
                }
            }

            VerifyF16Coder(65504.01f).IsOk.ShouldBeFalse();
            VerifyF16Coder(-65505.0f).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="SizeHeader"/> round-trips via <see cref="Headers.WriteSizeHeader"/> and <see cref="Headers.ReadSizeHeader"/>.
        /// Ported from <c>FieldsTest.TestRoundtripSize</c>.
        /// </summary>
        [Test]
        public void TestRoundtripSize()
        {
            for (int i = 0; i < 8; i++)
            {
                SizeHeader size = new SizeHeader();
                size.Set((nuint)(123 + (77 * i)), (nuint)(7 + i)).IsOk.ShouldBeTrue();

                Bundle.CanEncode(size, out nuint extensionBits, out nuint totalBits).IsOk.ShouldBeTrue();
                extensionBits.ShouldBe((nuint)0);

                BitWriter writer = new BitWriter();
                Headers.WriteSizeHeader(size, writer, LayerType.Header, null).IsOk.ShouldBeTrue();
                writer.BitsWritten.ShouldBe(totalBits);
                writer.ZeroPadToByte();

                SizeHeader size2 = new SizeHeader();
                BitReader reader = new BitReader(writer.GetSpan());
                Headers.ReadSizeHeader(reader, size2).IsOk.ShouldBeTrue();
                reader.TotalBitsConsumed().ShouldBe(totalBits);
                reader.Close().IsOk.ShouldBeTrue();

                size2.XSize().ShouldBe(size.XSize());
                size2.YSize().ShouldBe(size.YSize());
            }
        }

        /// <summary>
        /// Verifies that crop origins and sizes in <see cref="FrameHeader"/> can be encoded across <c>[-999, 19000)</c>.
        /// Ported from <c>FieldsTest.TestCropRect</c>.
        /// </summary>
        [Test]
        public void TestCropRect()
        {
            CodecMetadata metadata = new CodecMetadata();

            for (int i = -999; i < 19000; i += 37)
            {
                FrameHeader f        = new FrameHeader(metadata);
                f.CustomSizeOrOrigin = true;
                f.FrameOrigin.X0     = i;
                f.FrameOrigin.Y0     = i;
                f.FrameSize.XSize    = (uint)(1000 + i);
                f.FrameSize.YSize    = (uint)(1000 + i);

                Bundle.CanEncode(f, out nuint extensionBits, out nuint totalBits).IsOk.ShouldBeTrue();
                extensionBits.ShouldBe((nuint)0);
                totalBits.ShouldBeGreaterThanOrEqualTo((nuint)9);
            }
        }

        /// <summary>
        /// Verifies that <see cref="PreviewHeader"/> can encode dimensions up to 4359x4359.
        /// Ported from <c>FieldsTest.TestPreview</c>.
        /// </summary>
        [Test]
        public void TestPreview()
        {
            for (uint i = 1; i < 4360; i += 17)
            {
                PreviewHeader p = new PreviewHeader();
                p.Set(i, i).IsOk.ShouldBeTrue();

                Bundle.CanEncode(p, out nuint extensionBits, out nuint totalBits).IsOk.ShouldBeTrue();
                extensionBits.ShouldBe((nuint)0);
                totalBits.ShouldBeGreaterThanOrEqualTo((nuint)6);
            }
        }

        /// <summary>
        /// Verifies <see cref="FrameHeader"/> round-trips with extensions.
        /// Ported from <c>FieldsTest.TestRoundtripFrame</c>.
        /// </summary>
        [Test]
        public void TestRoundtripFrame()
        {
            CodecMetadata metadata = new CodecMetadata();
            FrameHeader h          = new FrameHeader(metadata)
            {
                Extensions = 0x800,
            };

            Bundle.CanEncode(h, out nuint extensionBits, out nuint totalBits).IsOk.ShouldBeTrue();
            extensionBits.ShouldBe((nuint)0);

            BitWriter writer = new BitWriter();
            FrameHeaderMethods.WriteFrameHeader(h, writer, null).IsOk.ShouldBeTrue();
            writer.BitsWritten.ShouldBe(totalBits);
            writer.ZeroPadToByte();

            FrameHeader h2   = new FrameHeader(metadata);
            BitReader reader = new BitReader(writer.GetSpan());
            FrameHeaderMethods.ReadFrameHeader(reader, h2).IsOk.ShouldBeTrue();
            reader.TotalBitsConsumed().ShouldBe(totalBits);
            reader.Close().IsOk.ShouldBeTrue();

            h2.Extensions.ShouldBe(h.Extensions);
            h2.Flags.ShouldBe(h.Flags);
        }

        /// <summary>
        /// Verifies that out-of-range <see cref="SizeHeader"/> dimensions are rejected by <see cref="Bundle.CanEncode"/>.
        /// Ported from <c>FieldsTest.TestOutOfRange</c>.
        /// </summary>
        [Test]
        public void TestOutOfRange()
        {
            SizeHeader h = new SizeHeader();
            h.Set(0xFFFFFFFFu, 0xFFFFFFFFu).IsOk.ShouldBeTrue();
            Bundle.CanEncode(h, out _, out _).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies forward compatibility when a newer decoder reads data written by an older bundle without extensions.
        /// Ported from <c>FieldsTest.TestNewDecoderOldData</c>.
        /// </summary>
        [Test]
        public void TestNewDecoderOldData()
        {
            OldBundle oldBundle = new OldBundle
            {
                OldLarge   = 123,
                OldF       = 3.75f,
                Extensions = 0,
            };

            const nuint kMaxOutBytes = 999;
            BitWriter writer         = new BitWriter();
            Bundle.CanEncode(oldBundle, out nuint extensionBits, out nuint totalBits).IsOk.ShouldBeTrue();
            totalBits.ShouldBeLessThanOrEqualTo(kMaxOutBytes * Common.BitsPerByte);
            extensionBits.ShouldBe((nuint)0);

            AuxOut auxOut = new AuxOut();
            Bundle.Write(oldBundle, writer, LayerType.Header, auxOut).IsOk.ShouldBeTrue();

            writer.WithMaxBits(
                (kMaxOutBytes * Common.BitsPerByte) - totalBits,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(20, 0xA55A);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader    = new BitReader(writer.GetSpan());
            NewBundle newBundle = new NewBundle();
            Bundle.Read(reader, newBundle).IsOk.ShouldBeTrue();
            reader.TotalBitsConsumed().ShouldBe(auxOut.Layer(LayerType.Header).TotalBits);
            reader.ReadBits(20).ShouldBe(0xA55AUL);
            reader.Close().IsOk.ShouldBeTrue();

            newBundle.Extensions.ShouldBe(oldBundle.Extensions);
            newBundle.OldSmall.ShouldBe(oldBundle.OldSmall);
            newBundle.OldF.ShouldBe(oldBundle.OldF);
            newBundle.OldLarge.ShouldBe(oldBundle.OldLarge);
            newBundle.NewSmall.ShouldBe(2u);
            newBundle.NewF.ShouldBe(-2.0f);
            newBundle.NewLarge.ShouldBe(0u);
        }

        /// <summary>
        /// Verifies backward compatibility when an older decoder skips extension fields written by a newer bundle.
        /// Ported from <c>FieldsTest.TestOldDecoderNewData</c>.
        /// </summary>
        [Test]
        public void TestOldDecoderNewData()
        {
            NewBundle newBundle = new NewBundle
            {
                OldLarge   = 123,
                Extensions = 3,
                NewF       = 999.0f,
                NewLarge   = 456,
            };

            const nuint kMaxOutBytes = 999;
            BitWriter writer         = new BitWriter();
            Bundle.CanEncode(newBundle, out nuint extensionBits, out _).IsOk.ShouldBeTrue();
            extensionBits.ShouldNotBe((nuint)0);

            AuxOut auxOut = new AuxOut();
            Bundle.Write(newBundle, writer, LayerType.Header, auxOut).IsOk.ShouldBeTrue();

            writer.WithMaxBits(
                (kMaxOutBytes * Common.BitsPerByte) - auxOut.Layer(LayerType.Header).TotalBits,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(20, 0xA55A);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader    = new BitReader(writer.GetSpan());
            OldBundle oldBundle = new OldBundle();
            Bundle.Read(reader, oldBundle).IsOk.ShouldBeTrue();
            reader.TotalBitsConsumed().ShouldBe(auxOut.Layer(LayerType.Header).TotalBits);
            reader.ReadBits(20).ShouldBe(0xA55AUL);
            reader.Close().IsOk.ShouldBeTrue();

            oldBundle.Extensions.ShouldBe(newBundle.Extensions);
            oldBundle.OldSmall.ShouldBe(newBundle.OldSmall);
            oldBundle.OldF.ShouldBe(newBundle.OldF);
            oldBundle.OldLarge.ShouldBe(newBundle.OldLarge);
        }

        /// <summary>
        /// Verifies <see cref="CodecMetadata"/> and <see cref="ImageMetadataMethods.WriteCodestreamHeaders"/> round-trip.
        /// </summary>
        [Test]
        public void TestWriteAndReadCodestreamHeaders()
        {
            CodecMetadata meta = new CodecMetadata();
            meta.Size.Set(640, 480).IsOk.ShouldBeTrue();
            meta.M.SetUintSamples(10);
            meta.M.SetAlphaBits(8, alphaIsPremultiplied: true);
            meta.M.ColorEncoding.SetSRGB(ColorSpace.RGB).IsOk.ShouldBeTrue();

            BitWriter writer = new BitWriter();
            AuxOut auxOut    = new AuxOut();
            ImageMetadataMethods.WriteCodestreamHeaders(meta, writer, auxOut).IsOk.ShouldBeTrue();
            writer.ZeroPadToByte();

            BitReader reader = new BitReader(writer.GetSpan());
            reader.ReadFixedBits(8).ShouldBe(0xFFUL);
            reader.ReadFixedBits(8).ShouldBe((ulong)Headers.KCodestreamMarker);

            CodecMetadata decoded = new CodecMetadata();
            Headers.ReadSizeHeader(reader, decoded.Size).IsOk.ShouldBeTrue();
            ImageMetadataMethods.ReadImageMetadata(reader, decoded.M).IsOk.ShouldBeTrue();
            decoded.TransformData.NonserializedXybEncoded = decoded.M.XybEncoded;
            Bundle.Read(reader, decoded.TransformData).IsOk.ShouldBeTrue();
            reader.JumpToByteBoundary().IsOk.ShouldBeTrue();
            reader.Close().IsOk.ShouldBeTrue();

            decoded.XSize().ShouldBe((nuint)640);
            decoded.YSize().ShouldBe((nuint)480);
            decoded.M.BitDepth.BitsPerSample.ShouldBe(10u);
            decoded.M.GetAlphaBits().ShouldBe(8u);
            decoded.M.HasAlpha().ShouldBeTrue();
        }

        private static void VerifyU32Coder(uint value, nuint expectedBitsWritten)
        {
            U32Enc enc = new U32Enc(
                U32Distr.Val(0),
                U32Distr.Bits(4),
                U32Distr.Val(0x7FFFFFFFu),
                U32Distr.Bits(32));

            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                Common.RoundUpBitsToByteMultiple(U32Coder.MaxEncodedBits(enc)),
                LayerType.Header,
                null,
                () =>
                {
                    U32Coder.CanEncode(enc, value, out nuint precheckPos).IsOk.ShouldBeTrue();
                    precheckPos.ShouldBe(expectedBitsWritten);

                    U32Coder.Write(enc, value, writer).IsOk.ShouldBeTrue();
                    writer.BitsWritten.ShouldBe(expectedBitsWritten);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader  = new BitReader(writer.GetSpan());
            uint decodedValue = U32Coder.Read(enc, reader);
            decodedValue.ShouldBe(value);
            reader.Close().IsOk.ShouldBeTrue();
        }

        private static void VerifyU64Coder(ulong value, nuint expectedBitsWritten)
        {
            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                Common.RoundUpBitsToByteMultiple(U64Coder.MaxEncodedBits()),
                LayerType.Header,
                null,
                () =>
                {
                    U64Coder.CanEncode(value, out nuint precheckPos).IsOk.ShouldBeTrue();
                    precheckPos.ShouldBe(expectedBitsWritten);

                    U64Coder.Write(value, writer).IsOk.ShouldBeTrue();
                    writer.BitsWritten.ShouldBe(expectedBitsWritten);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader   = new BitReader(writer.GetSpan());
            ulong decodedValue = U64Coder.Read(reader);
            decodedValue.ShouldBe(value);
            reader.Close().IsOk.ShouldBeTrue();
        }

        private static Status VerifyF16Coder(float value)
        {
            if (!F16Coder.CanEncode(value, out nuint maxEncodedBits))
            {
                return false;
            }

            maxEncodedBits.ShouldBe(F16Coder.MaxEncodedBits());

            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                Common.RoundUpBitsToByteMultiple(maxEncodedBits),
                LayerType.Header,
                null,
                () =>
                {
                    F16Coder.Write(value, writer).IsOk.ShouldBeTrue();
                    writer.BitsWritten.ShouldBe(F16Coder.MaxEncodedBits());
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());
            F16Coder.Read(reader, out float decodedValue).IsOk.ShouldBeTrue();
            decodedValue.ShouldBe(value);
            reader.Close().IsOk.ShouldBeTrue();

            return true;
        }

        private sealed class OldBundle : Fields
        {
            public uint OldSmall;
            public float OldF;
            public uint OldLarge;
            public ulong Extensions;

            public OldBundle()
            {
                Bundle.Init(this);
            }

            public override string BundleName => "OldBundle";

            public override Status VisitFields(Visitor visitor)
            {
                if (!visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Bits(2),
                    U32Distr.Bits(3),
                    U32Distr.Bits(4),
                    1,
                    ref OldSmall))
                {
                    return false;
                }

                if (!visitor.F16(1.125f, ref OldF))
                {
                    return false;
                }

                if (!visitor.U32(
                    U32Distr.Bits(7),
                    U32Distr.Bits(12),
                    U32Distr.Bits(16),
                    U32Distr.Bits(32),
                    0,
                    ref OldLarge))
                {
                    return false;
                }

                if (!visitor.BeginExtensions(ref Extensions))
                {
                    return false;
                }

                return visitor.EndExtensions();
            }
        }

        private sealed class NewBundle : Fields
        {
            public uint OldSmall;
            public float OldF;
            public uint OldLarge;
            public ulong Extensions;
            public uint NewSmall = 2;
            public float NewF = -2.0f;
            public uint NewLarge;

            public NewBundle()
            {
                Bundle.Init(this);
            }

            public override string BundleName => "NewBundle";

            public override Status VisitFields(Visitor visitor)
            {
                if (!VisitOldPart(visitor))
                {
                    return false;
                }

                if (!visitor.BeginExtensions(ref Extensions))
                {
                    return false;
                }

                if (visitor.Conditional((Extensions & 1UL) != 0))
                {
                    if (!visitor.U32(
                        U32Distr.Val(2),
                        U32Distr.Bits(2),
                        U32Distr.Bits(3),
                        U32Distr.Bits(4),
                        2,
                        ref NewSmall))
                    {
                        return false;
                    }

                    if (!visitor.F16(-2.0f, ref NewF))
                    {
                        return false;
                    }
                }

                if (visitor.Conditional((Extensions & 2UL) != 0) &&
                    !visitor.U32(
                        U32Distr.Bits(9),
                        U32Distr.Bits(12),
                        U32Distr.Bits(16),
                        U32Distr.Bits(32),
                        0,
                        ref NewLarge))
                {
                    return false;
                }

                return visitor.EndExtensions();
            }

            private Status VisitOldPart(Visitor visitor)
            {
                if (!visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Bits(2),
                    U32Distr.Bits(3),
                    U32Distr.Bits(4),
                    1,
                    ref OldSmall))
                {
                    return false;
                }

                if (!visitor.F16(1.125f, ref OldF))
                {
                    return false;
                }

                return visitor.U32(
                    U32Distr.Bits(7),
                    U32Distr.Bits(12),
                    U32Distr.Bits(16),
                    U32Distr.Bits(32),
                    0,
                    ref OldLarge);
            }
        }
    }
}
