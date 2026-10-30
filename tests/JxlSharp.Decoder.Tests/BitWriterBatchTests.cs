using System;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks bulk bit copying against an independent bit-by-bit concatenation.</summary>
    [TestFixture]
    public sealed class BitWriterBatchTests
    {
        /// <summary>Cover bulk-load boundaries, all alignments, tails, reallocation and following writes.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(55)]
        [TestCase(56)]
        [TestCase(57)]
        [TestCase(257)]
        [TestCase(65537)]
        public void AppendMatchesBitConcatenation(int bytes)
        {
            byte[] data = new byte[bytes];
            new Random(123).NextBytes(data);
            for (int tail = 0; tail < 8; tail++)
            {
                BitWriter source = new BitWriter();
                source.AppendByteAligned(data);
                source.Write((nuint)tail, (1UL << tail) - 1);
                byte[] original = source.GetSpan().ToArray();
                int count = checked((int)source.BitsWritten);
                for (int prefix = 0; prefix < 8; prefix++)
                {
                    BitWriter target = new BitWriter();
                    target.Write((nuint)prefix, (1UL << prefix) - 1);
                    Assert.That(target.AppendUnaligned(source).IsOk, Is.True);
                    target.Write(5, 21);
                    byte[] expected = new byte[(prefix + count + 5 + 7) / 8];
                    if (prefix != 0) expected[0] = (byte)((1 << prefix) - 1);
                    CopyBits(original, count, expected, prefix);
                    CopyBits(new byte[] { 21 }, 5, expected, prefix + count);
                    Assert.That(target.BitsWritten, Is.EqualTo((nuint)(prefix + count + 5)));
                    Assert.That(target.GetSpan().ToArray(), Is.EqualTo(expected));
                    Assert.That(source.GetSpan().ToArray(), Is.EqualTo(original));
                    using var targetStorage = target.TakeBytes();
                }
                using var sourceStorage = source.TakeBytes();
            }
        }

        /// <summary>Self-appending constructs source spans after reservation, including when it reallocates.</summary>
        [TestCase(0)]
        [TestCase(8)]
        [TestCase(57)]
        [TestCase(65537)]
        public void SelfAppendSurvivesReallocation(int bytes)
        {
            byte[] data = new byte[bytes];
            new Random(456).NextBytes(data);
            for (int tail = 0; tail < 8; tail++)
            {
                BitWriter writer = new BitWriter();
                writer.AppendByteAligned(data);
                writer.Write((nuint)tail, (1UL << tail) - 1);
                byte[] original = writer.GetSpan().ToArray();
                int count = checked((int)writer.BitsWritten);
                Assert.That(writer.AppendUnaligned(writer).IsOk, Is.True);
                writer.Write(5, 21);
                byte[] expected = new byte[(2 * count + 5 + 7) / 8];
                CopyBits(original, count, expected, 0);
                CopyBits(original, count, expected, count);
                CopyBits(new byte[] { 21 }, 5, expected, 2 * count);
                Assert.That(writer.BitsWritten, Is.EqualTo((nuint)(2 * count + 5)));
                Assert.That(writer.GetSpan().ToArray(), Is.EqualTo(expected));
                using var storage = writer.TakeBytes();
            }
        }

        private static void CopyBits(byte[] source, int bits, byte[] target, int offset)
        {
            for (int i = 0; i < bits; i++)
                target[(i + offset) >> 3] |= (byte)(((source[i >> 3] >> (i & 7)) & 1) << ((i + offset) & 7));
        }
    }
}
