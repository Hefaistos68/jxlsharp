using System;
using System.Numerics;
using NUnit.Framework;

namespace JxlSharp.Decoder
{
    /// <summary>Checks the four error lanes against scalar predictor arithmetic and state updates.</summary>
    [TestFixture]
    public sealed class WeightedPredictorSimdTests
    {
        /// <summary>Exercise row boundaries, all presets, full-range pixels and both property modes.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(17)]
        [TestCase(257)]
        public void PredictionAndErrorsMatchScalar(int width)
        {
            Random random = new Random(123);
            Span<long> predictions = stackalloc long[4];
            Span<uint> weights = stackalloc uint[4];
            Span<uint> previous = stackalloc uint[4];
            int[] properties = new int[3];
            const int height = 9;
            for (int mode = 0; mode < 5; mode++)
            for (int pattern = 0; pattern < 3; pattern++)
            {
                WeightedHeader header = new WeightedHeader();
                header.SetPredictorMode(mode);
                WeightedState state = new WeightedState(header, (nuint)width, height);
                int[][] rows = new int[height][];
                for (int y = 0; y < height; y++)
                {
                    rows[y] = new int[width];
                    for (int x = 0; x < width; x++)
                    {
                        rows[y][x] = pattern == 0 ? 0 : pattern == 1 ? x * 17 + y * 11 :
                            x % 7 == 0 ? int.MinValue : x % 7 == 1 ? int.MaxValue :
                            (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                    }
                }

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    NeighborPixels neighbors = ModularPredictors.GatherNeighbors(rows[y], rows[Math.Max(0, y - 1)], rows[Math.Max(0, y - 2)], x, y);
                    int current = ((y & 1) == 0 ? width + 2 : 0) + x;
                    int north = ((y & 1) != 0 ? width + 2 : 0) + x;
                    int nw = north - (x > 0 ? 1 : 0);
                    int ne = north + (x + 1 < width ? 1 : 0);
                    long teW = x == 0 ? 0 : state.Error[current - 1];
                    long teN = state.Error[north], teNW = state.Error[nw], teNE = state.Error[ne];
                    long n = neighbors.Top << 3, w = neighbors.Left << 3, right = neighbors.TopRight << 3;
                    long left = neighbors.TopLeft << 3, nn = neighbors.TopTop << 3;
                    predictions[0] = w + right - n;
                    predictions[1] = n - (((teN + teW + teNE) * header.P1C) >> 5);
                    predictions[2] = w - (((teN + teW + teNW) * header.P2C) >> 5);
                    predictions[3] = n - ((teNW * header.P3Ca + teN * header.P3Cb + teNE * header.P3Cc +
                        (nn - n) * header.P3Cd + (left - w) * header.P3Ce) >> 5);
                    for (int i = 0; i < 4; i++)
                    {
                        uint[] errors = state.PredErrors[i];
                        weights[i] = ScalarWeight(unchecked(errors[north] + errors[ne] + errors[nw]), header.W[i]);
                        previous[i] = errors[north + 1];
                    }

                    uint total = weights[0] + weights[1] + weights[2] + weights[3];
                    int shift = BitOperations.Log2(total) - 4;
                    total = 0;
                    for (int i = 0; i < 4; i++) { weights[i] >>= shift; total += weights[i]; }
                    long sum = (total >> 1) - 1;
                    for (int i = 0; i < 4; i++) sum += predictions[i] * weights[i];
                    long expected = (sum * ((1u << 24) / total)) >> 24;
                    if (((teN ^ teW) | (teN ^ teNW)) <= 0)
                        expected = Math.Clamp(expected, Math.Min(w, Math.Min(right, n)), Math.Max(w, Math.Max(right, n)));
                    long property = teW;
                    if (Math.Abs(teN) > Math.Abs(property)) property = teN;
                    if (Math.Abs(teNW) > Math.Abs(property)) property = teNW;
                    if (Math.Abs(teNE) > Math.Abs(property)) property = teNE;
                    Array.Fill(properties, 1234567);
                    bool compute = (x & 1) == 0;
                    long result = state.Predict((nuint)x, (nuint)y, (nuint)width, in neighbors, properties, 1, compute);
                    Assert.That(result, Is.EqualTo((expected + 3) >> 3));
                    Assert.That(state.Pred, Is.EqualTo(expected));
                    Assert.That(properties[1], Is.EqualTo(compute ? (int)property : 1234567));
                    Assert.That(properties[0], Is.EqualTo(1234567));
                    Assert.That(properties[2], Is.EqualTo(1234567));
                    long value = (long)rows[y][x] << 3;
                    state.UpdateErrors(rows[y][x], (nuint)x, (nuint)y, (nuint)width);
                    Assert.That(state.Error[current], Is.EqualTo((int)(expected - value)));
                    for (int i = 0; i < 4; i++)
                    {
                        uint error = (uint)((Math.Abs(predictions[i] - value) + 3) >> 3);
                        Assert.That(state.PredErrors[i][current], Is.EqualTo(error));
                        Assert.That(state.PredErrors[i][north + 1], Is.EqualTo(unchecked(previous[i] + error)));
                    }
                }
            }
        }

        /// <summary>The public predictor retains the scalar absolute-value overflow exception.</summary>
        [Test]
        public void AbsoluteValueOverflowStillThrows()
        {
            WeightedState state = new WeightedState(new WeightedHeader(), 1, 1);
            NeighborPixels neighbors = new NeighborPixels { Left = long.MinValue >> 3 };
            state.Predict(0, 0, 1, in neighbors, Span<int>.Empty, 0, false);
            Assert.Throws<OverflowException>(() => state.UpdateErrors(0, 0, 0, 1));
            foreach (uint[] errors in state.PredErrors) Assert.That(errors, Is.All.Zero);
        }

        /// <summary>Zero error clears recycled entries without changing the accumulated NE errors.</summary>
        [Test]
        public void ExactPredictionClearsRecycledErrors()
        {
            WeightedState state = new WeightedState(new WeightedHeader(), 1, 1);
            state.Error[3] = 123;
            for (int i = 0; i < 4; i++)
            {
                state.PredErrors[i][3] = (uint)(100 + i);
                state.PredErrors[i][1] = uint.MaxValue - (uint)i;
            }
            state.UpdateErrors(0, 0, 0, 1);
            Assert.That(state.Error[3], Is.Zero);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(state.PredErrors[i][3], Is.Zero);
                Assert.That(state.PredErrors[i][1], Is.EqualTo(uint.MaxValue - (uint)i));
            }
        }

        private static uint ScalarWeight(ulong error, uint weight)
        {
            int shift = Math.Max(0, BitOperations.Log2(error + 1) - 5);
            uint reciprocal = (1u << 24) / ((uint)(error >> shift) + 1);
            return 4u + (unchecked(weight * reciprocal) >> shift);
        }
    }
}
