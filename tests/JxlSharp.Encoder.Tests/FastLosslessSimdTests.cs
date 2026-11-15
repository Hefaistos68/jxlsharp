using System;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Checks wrapped prediction, widened differences, short rows and independent zero-run scanning.</summary>
    [TestFixture]
    public sealed class FastLosslessSimdTests
    {
        /// <summary>Compare SIMD chunks to single-pixel scalar calls over the entire integer range.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void ResidualChunksMatchScalar(int count)
        {
            Random random = new Random(123);
            for (int trial = 0; trial < 2000; trial++)
            {
                int[][] rows32 = new int[4][];
                short[][] rows16 = new short[4][];
                for (int r = 0; r < 4; r++)
                {
                    int length = trial % 3 == 0 ? random.Next(0, 9) : 8;
                    rows32[r] = new int[length];
                    rows16[r] = new short[length];
                    for (int x = 0; x < length; x++)
                    {
                        rows32[r][x] = trial < 10 ? (x % 2 == 0 ? int.MinValue : int.MaxValue) : (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                        rows16[r][x] = trial < 10 ? ((x + (r == 0 ? 0 : 1)) % 2 == 0 ? short.MinValue : short.MaxValue) : (short)random.Next(short.MinValue, short.MaxValue + 1);
                    }
                }
                uint[] actual32 = new uint[count + 2];
                ushort[] actual16 = new ushort[count + 2];
                Array.Fill(actual32, 0xdeadbeefu);
                Array.Fill(actual16, (ushort)0xbeef);
                EncFastLossless.ComputeResiduals(rows32[0], rows32[1], rows32[2], rows32[3], actual32.AsSpan(1, count));
                EncFastLossless.ComputeResiduals(rows16[0], rows16[1], rows16[2], rows16[3], actual16.AsSpan(1, count));
                for (int x = 0; x < count; x++)
                {
                    uint[] expected32 = new uint[1];
                    ushort[] expected16 = new ushort[1];
                    EncFastLossless.ComputeResiduals(Tail(rows32[0], x), Tail(rows32[1], x), Tail(rows32[2], x), Tail(rows32[3], x), expected32);
                    EncFastLossless.ComputeResiduals(Tail(rows16[0], x), Tail(rows16[1], x), Tail(rows16[2], x), Tail(rows16[3], x), expected16);
                    Assert.That(actual32[x + 1], Is.EqualTo(expected32[0]));
                    Assert.That(actual16[x + 1], Is.EqualTo(expected16[0]));
                }
                Assert.That(actual32[0], Is.EqualTo(0xdeadbeefu));
                Assert.That(actual32[count + 1], Is.EqualTo(0xdeadbeefu));
                Assert.That(actual16[0], Is.EqualTo(0xbeef));
                Assert.That(actual16[count + 1], Is.EqualTo(0xbeef));
            }
        }

        /// <summary>Only consecutive leading zeroes contribute to RLE.</summary>
        [Test]
        public void ZeroPrefixStopsAtFirstNonzero()
        {
            for (int length = 0; length <= 8; length++)
                for (int prefix = 0; prefix <= length; prefix++)
                {
                    uint[] a = new uint[length];
                    ushort[] b = new ushort[length];
                    if (prefix < length) { a[prefix] = uint.MaxValue; b[prefix] = ushort.MaxValue; }
                    Assert.That(EncFastLossless.CountZeroPrefix(a), Is.EqualTo(prefix));
                    Assert.That(EncFastLossless.CountZeroPrefix(b), Is.EqualTo(prefix));
                }
        }

        private static ReadOnlySpan<T> Tail<T>(T[] data, int offset) => offset < data.Length ? data.AsSpan(offset) : ReadOnlySpan<T>.Empty;
    }
}
