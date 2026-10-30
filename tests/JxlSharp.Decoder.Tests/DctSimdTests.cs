using System;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks SIMD butterflies against the independent single-column scalar path.</summary>
    [TestFixture]
    public sealed class DctSimdTests
    {
        /// <summary>Strided, unaligned source and destination with scalar tails and guard elements.</summary>
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public void ColumnsMatchScalarBits(int n)
        {
            foreach (int columns in new[] { 1, 4, 7, 8, 9, 16, 17, 32 })
            foreach (bool inverse in new[] { false, true })
            {
                int fs = columns + 3, ts = columns + 5;
                float[] source = new float[(n - 1) * fs + columns + 2];
                float[] actual = new float[(n - 1) * ts + columns + 2];
                Array.Fill(actual, -12345f);
                float[] expected = (float[])actual.Clone();
                Random random = new Random(123);
                for (int i = 0; i < source.Length; i++) source[i] = (float)(random.NextDouble() * 20 - 10);
                float[] scratch = new float[3 * n * columns];
                Run(n, columns, new DctFrom(source.AsSpan(1), (nuint)fs), new DctTo(actual.AsSpan(1), (nuint)ts), scratch, inverse);
                for (int col = 0; col < columns; col++)
                    Run(n, 1, new DctFrom(source.AsSpan(1 + col), (nuint)fs), new DctTo(expected.AsSpan(1 + col), (nuint)ts), scratch, inverse);
                Compare(actual, expected, $"n={n}, columns={columns}, inverse={inverse}");
            }
        }

        /// <summary>Scaled rectangular 2D transforms retain orientation, scaling and fused rounding.</summary>
        [TestCase(4, 8)]
        [TestCase(8, 4)]
        [TestCase(8, 8)]
        [TestCase(8, 16)]
        [TestCase(16, 8)]
        [TestCase(16, 16)]
        [TestCase(16, 32)]
        [TestCase(32, 16)]
        [TestCase(32, 32)]
        public void TwoDimensionalTransformsMatchScalar(int rows, int cols)
        {
            int size = rows * cols;
            float[] input = new float[size];
            Random random = new Random(456);
            for (int i = 0; i < size; i++) input[i] = (float)(random.NextDouble() * 2 - 1);
            float[] actual = new float[size], expected = new float[size], scratch = new float[size * 4];
            DecTransforms.ComputeScaledDCT(rows, cols, new DctFrom(input, (nuint)cols), actual, scratch);
            Scalar(rows, cols, input, expected, scratch, false);
            Compare(actual, expected, "forward");
            // Feed identical arbitrary coefficients to both inverse implementations.
            DecTransforms.ComputeScaledIDCT(rows, cols, (float[])input.Clone(), new DctTo(actual, (nuint)cols), scratch);
            Scalar(rows, cols, input, expected, scratch, true);
            Compare(actual, expected, "inverse");
        }

        private static void Scalar(int rows, int cols, float[] input, float[] output, float[] scratch, bool inverse)
        {
            float[] block = new float[rows * cols];
            float[] work = (float[])input.Clone();
            if (!inverse && rows < cols)
            {
                Columns(rows, cols, work, cols, block, cols, scratch, false);
                Transpose(block, output, rows, cols);
                Columns(cols, rows, output, rows, block, rows, scratch, false);
                Transpose(block, output, cols, rows);
            }
            else if (!inverse)
            {
                Columns(rows, cols, work, cols, output, cols, scratch, false);
                Transpose(output, block, rows, cols);
                Columns(cols, rows, block, rows, output, rows, scratch, false);
            }
            else if (rows < cols)
            {
                Transpose(work, block, rows, cols);
                Columns(cols, rows, block, rows, work, rows, scratch, true);
                Transpose(work, block, cols, rows);
                Columns(rows, cols, block, cols, output, cols, scratch, true);
            }
            else
            {
                Columns(cols, rows, work, rows, block, rows, scratch, true);
                Transpose(block, work, cols, rows);
                Columns(rows, cols, work, cols, output, cols, scratch, true);
            }
        }

        private static void Columns(int n, int m, float[] from, int fs, float[] to, int ts, float[] scratch, bool inverse)
        {
            for (int col = 0; col < m; col++)
                Run(n, 1, new DctFrom(from.AsSpan(col), (nuint)fs), new DctTo(to.AsSpan(col), (nuint)ts), scratch, inverse);
        }

        private static void Run(int n, int m, DctFrom from, DctTo to, float[] scratch, bool inverse)
        {
            if (inverse) DecTransforms.Idct1D(n, m, from, to, scratch);
            else DecTransforms.Dct1D(n, m, from, to, scratch);
        }

        private static void Transpose(float[] from, float[] to, int rows, int cols)
        {
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++) to[x * rows + y] = from[y * cols + x];
        }

        private static void Compare(float[] actual, float[] expected, string context)
        {
            for (int i = 0; i < actual.Length; i++)
                Assert.That(BitConverter.SingleToInt32Bits(actual[i]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected[i])), $"{context}, element={i}");
        }
    }
}
