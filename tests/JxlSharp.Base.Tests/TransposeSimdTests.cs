using System;
using NUnit.Framework;

namespace JxlSharp.Base.Tests
{
    /// <summary>Bit-preserving tiled transpose checks with unaligned buffers and padding.</summary>
    [TestFixture]
    public sealed class TransposeSimdTests
    {
        /// <summary>The direct tile kernel snapshots inputs before any output, including on machines without AVX.</summary>
        [Test]
        public void DirectTileSupportsInPlaceTranspose()
        {
            float[] data = new float[64];
            for (int i = 0; i < data.Length; i++) data[i] = BitConverter.Int32BitsToSingle(unchecked((int)(0x7f800001u + (uint)i)));
            float[] original = (float[])data.Clone();
            Transpose.Transpose8x8(data, 8, data, 8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Assert.That(BitConverter.SingleToInt32Bits(data[x * 8 + y]), Is.EqualTo(BitConverter.SingleToInt32Bits(original[y * 8 + x])));
        }

        /// <summary>Checks full tiles and both scalar remainder dimensions.</summary>
        [TestCase(8, 8)]
        [TestCase(8, 16)]
        [TestCase(16, 8)]
        [TestCase(9, 17)]
        [TestCase(31, 25)]
        public void TiledTransposePreservesAllBits(int rows, int cols)
        {
            int fs = cols + 3, ts = rows + 5;
            uint[] source = new uint[(rows - 1) * fs + cols + 2];
            uint[] actual = new uint[(cols - 1) * ts + rows + 2];
            Array.Fill(actual, 0xdeadbeefu);
            uint[] expected = (uint[])actual.Clone();
            Random random = new Random(789);
            for (int i = 0; i < source.Length; i++) source[i] = (uint)random.NextInt64(0, 1L << 32);
            source[1] = 0x7fa12345u;
            source[2] = 0x80000000u;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++) expected[1 + x * ts + y] = source[1 + y * fs + x];
            Transpose.GenericTransposeBlock<uint>(source.AsSpan(1), fs, actual.AsSpan(1), ts, rows, cols);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
