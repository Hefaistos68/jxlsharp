// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BitReader"/>, <see cref="BitReaderScopedCloser"/>, <see cref="BitWriter"/>, and <see cref="AuxOut"/>.
    /// Ported from <c>lib/jxl/bit_reader_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class BitReaderTests
    {
        /// <summary>
        /// Verifies that <see cref="BitReader"/> reads all declared bits and pads beyond the end with zeroes.
        /// Ported from <c>BitReaderTest.ExtendsWithZeroes</c>.
        /// </summary>
        [Test]
        public void ExtendsWithZeroes()
        {
            for (int size = 4; size < 32; ++size)
            {
                byte[] data = new byte[size];
                Array.Fill(data, (byte)0xFF);

                for (int nBytes = 0; nBytes < size; nBytes++)
                {
                    VerifyReaderZeroExtension(data.AsSpan(0, nBytes), nBytes);
                }
            }
        }

        /// <summary>
        /// Verifies that writing random bit sequences with <see cref="BitWriter"/> and reading them back with <see cref="BitReader"/> round-trips identically.
        /// Ported from <c>BitReaderTest.TestRoundTrip</c>.
        /// </summary>
        [Test]
        public void TestRoundTrip()
        {
            for (uint task = 0; task < 200; ++task)
            {
                RunSingleRoundTrip(task);
            }
        }

        /// <summary>
        /// Verifies that <see cref="BitReader.SkipBits"/> advances the bit stream identically to reading individual bits.
        /// Ported from <c>BitReaderTest.TestSkip</c>.
        /// </summary>
        [Test]
        public void TestSkip()
        {
            for (nuint task = 0; task < 32; ++task)
            {
                for (nuint skip = 0; skip < 128; ++skip)
                {
                    RunSingleSkipTest(task, skip);
                }
            }
        }

        /// <summary>
        /// Verifies byte order and bit groupings for 1-bit, 8-bit, 16-bit, and unaligned mixed writes.
        /// Ported from <c>BitReaderTest.TestOrder</c>.
        /// </summary>
        [Test]
        public void TestOrder()
        {
            VerifyOrder1Bit();
            VerifyOrder8Bit();
            VerifyOrder16Bit();
            VerifyOrderMixed();
        }

        /// <summary>
        /// Verifies <see cref="BitReader.TotalBytes"/> and <see cref="BitReader.TotalBitsConsumed"/> counters.
        /// Ported from <c>BitReaderTest.TotalCountersTest</c>.
        /// </summary>
        [Test]
        public void TotalCountersTest()
        {
            byte[] buf       = { 1, 2, 3, 4, 0, 0, 0, 0 };
            BitReader reader = new BitReader(buf);

            reader.TotalBytes().ShouldBe((nuint)buf.Length);
            reader.TotalBitsConsumed().ShouldBe((nuint)0);

            reader.ReadFixedBits(1);
            reader.TotalBitsConsumed().ShouldBe((nuint)1);

            reader.ReadFixedBits(10);
            reader.TotalBitsConsumed().ShouldBe((nuint)11);

            reader.ReadFixedBits(4);
            reader.TotalBitsConsumed().ShouldBe((nuint)15);

            reader.ReadFixedBits(1);
            reader.TotalBitsConsumed().ShouldBe((nuint)16);

            reader.ReadFixedBits(16);
            reader.TotalBitsConsumed().ShouldBe((nuint)32);

            reader.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="BitReader.TransferFrom"/> and <see cref="BitReaderScopedCloser"/>.
        /// Ported from <c>BitReaderTest.MoveTest</c>.
        /// </summary>
        [Test]
        public void MoveTest()
        {
            byte[] buf        = { 1, 2, 3, 4, 0, 0, 0, 0 };
            BitReader reader1 = new BitReader(buf);

            reader1.TotalBitsConsumed().ShouldBe((nuint)0);
            reader1.ReadFixedBits(16);
            reader1.TotalBitsConsumed().ShouldBe((nuint)16);

            BitReader reader2 = new BitReader();
            reader2.TransferFrom(reader1);

            reader2.TotalBitsConsumed().ShouldBe((nuint)16);
            reader2.ReadFixedBits(8).ShouldBe(3UL);
            reader2.TotalBitsConsumed().ShouldBe((nuint)24);

            Status closeStatus = Status.OkStatus;

            using (BitReaderScopedCloser closer = new BitReaderScopedCloser(reader2))
            {
                reader2.JumpToByteBoundary().IsOk.ShouldBeTrue();
                closer.Close(ref closeStatus);
            }

            closeStatus.IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="BitWriter.AppendByteAligned(ReadOnlySpan{byte})"/>,
        /// <see cref="BitWriter.AppendByteAligned(IReadOnlyList{BitWriter})"/>,
        /// and <see cref="BitWriter.AppendUnaligned"/>.
        /// </summary>
        [Test]
        public void BitWriterAppendAndAuxOutTest()
        {
            BitWriter w1  = new BitWriter();
            AuxOut auxOut = new AuxOut();

            w1.WithMaxBits(
                16,
                LayerType.Header,
                auxOut,
                () =>
                {
                    w1.Write(8, 0xAB);
                    w1.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            auxOut.Layer(LayerType.Header).TotalBits.ShouldBe((nuint)8);

            byte[] extra = { 0xCD, 0xEF };
            w1.AppendByteAligned(extra).IsOk.ShouldBeTrue();
            w1.BitsWritten.ShouldBe((nuint)24);

            BitWriter w2 = new BitWriter();
            w2.AppendByteAligned(new[] { w1 }).IsOk.ShouldBeTrue();
            w2.GetSpan().ToArray().ShouldBe(new byte[] { 0xAB, 0xCD, 0xEF });

            BitWriter w3 = new BitWriter();
            w3.AppendUnaligned(w1).IsOk.ShouldBeTrue();
            w3.GetSpan().ToArray().ShouldBe(new byte[] { 0xAB, 0xCD, 0xEF });

            AuxOut cloneAux = new AuxOut();
            cloneAux.Assimilate(auxOut);
            cloneAux.Assimilate(auxOut);
            cloneAux.TotalBits().ShouldBe((nuint)16);
        }

        private static void VerifyReaderZeroExtension(ReadOnlySpan<byte> span, int nBytes)
        {
            BitReader br = new BitReader(span);

            for (int i = 0; i < nBytes * (int)Common.BitsPerByte; i++)
            {
                br.ReadBits(1).ShouldBe(1UL);
            }

            for (nuint i = 0; i < BitReader.KMaxBitsPerCall; i++)
            {
                br.PeekBits(i).ShouldBe(0UL);
            }

            br.Close().IsOk.ShouldBeTrue();
        }

        private static void RunSingleRoundTrip(uint task)
        {
            const nuint kMaxBits                    = 800;
            BitWriter writer                        = new BitWriter();
            List<(uint NumBits, uint Value)> symbols = new List<(uint, uint)>();

            writer.WithMaxBits(
                kMaxBits,
                LayerType.Header,
                null,
                () =>
                {
                    Rng rng = new Rng(55537 + (129 * task));

                    while (true)
                    {
                        uint numBits = (uint)rng.UniformU(1, 33);

                        if (writer.BitsWritten + numBits > kMaxBits)
                        {
                            break;
                        }

                        ulong upper = 1UL << (int)numBits;
                        uint value  = (uint)(rng.NextUInt64() & (upper - 1UL));
                        symbols.Add((numBits, value));
                        writer.Write(numBits, value);
                    }

                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());

            foreach ((uint numBits, uint value) in symbols)
            {
                ((uint)reader.ReadBits(numBits)).ShouldBe(value);
            }

            reader.Close().IsOk.ShouldBeTrue();
        }

        private static void RunSingleSkipTest(nuint task, nuint skip)
        {
            const nuint kSize = 100;
            BitWriter writer  = new BitWriter();
            AuxOut auxOut     = new AuxOut();

            writer.WithMaxBits(
                kSize * Common.BitsPerByte,
                LayerType.Header,
                auxOut,
                () =>
                {
                    for (nuint i = 0; i < task; ++i)
                    {
                        writer.Write(1, 1);
                    }

                    for (nuint i = 0; i < skip; ++i)
                    {
                        writer.Write(1, 0);
                    }

                    writer.Write(3, 5);
                    writer.BitsWritten.ShouldBe(task + skip + 3u);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            auxOut.Layer(LayerType.Header).TotalBits.ShouldBeLessThan(kSize * 8u);

            ReadOnlySpan<byte> bytes = writer.GetSpan();
            BitReader reader1        = new BitReader(bytes);
            BitReader reader2        = new BitReader(bytes);

            for (nuint i = 0; i < task; ++i)
            {
                reader1.ReadBits(1).ShouldBe(1UL);
                reader2.ReadBits(1).ShouldBe(1UL);
            }

            reader1.SkipBits(skip);

            for (nuint i = 0; i < skip; ++i)
            {
                reader2.ReadBits(1).ShouldBe(0UL);
            }

            reader1.TotalBitsConsumed().ShouldBe(reader2.TotalBitsConsumed());
            reader1.ReadBits(3).ShouldBe(5UL);
            reader2.ReadBits(3).ShouldBe(5UL);

            reader1.Close().IsOk.ShouldBeTrue();
            reader2.Close().IsOk.ShouldBeTrue();
        }

        private static void VerifyOrder1Bit()
        {
            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                16,
                LayerType.Header,
                null,
                () =>
                {
                    for (int i = 0; i < 5; ++i)
                    {
                        writer.Write(1, 1);
                    }

                    for (int i = 0; i < 5; ++i)
                    {
                        writer.Write(1, 0);
                    }

                    for (int i = 0; i < 6; ++i)
                    {
                        writer.Write(1, 1);
                    }

                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());
            reader.ReadFixedBits(8).ShouldBe(0x1FUL);
            reader.ReadFixedBits(8).ShouldBe(0xFCUL);
            reader.Close().IsOk.ShouldBeTrue();
        }

        private static void VerifyOrder8Bit()
        {
            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                16,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(8, 0xF8);
                    writer.Write(8, 0x3F);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());
            reader.ReadFixedBits(8).ShouldBe(0xF8UL);
            reader.ReadFixedBits(8).ShouldBe(0x3FUL);
            reader.Close().IsOk.ShouldBeTrue();
        }

        private static void VerifyOrder16Bit()
        {
            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                16,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(16, 0xF83F);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());
            reader.ReadFixedBits(8).ShouldBe(0x3FUL);
            reader.ReadFixedBits(8).ShouldBe(0xF8UL);
            reader.Close().IsOk.ShouldBeTrue();
        }

        private static void VerifyOrderMixed()
        {
            BitWriter writer = new BitWriter();
            writer.WithMaxBits(
                16,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(1, 1);
                    writer.Write(3, 6);
                    writer.Write(8, 0xDB);
                    writer.Write(4, 8);
                    writer.ZeroPadToByte();

                    return true;
                }).IsOk.ShouldBeTrue();

            BitReader reader = new BitReader(writer.GetSpan());
            reader.ReadFixedBits(8).ShouldBe(0xBDUL);
            reader.ReadFixedBits(8).ShouldBe(0x8DUL);
            reader.Close().IsOk.ShouldBeTrue();
        }
    }
}
