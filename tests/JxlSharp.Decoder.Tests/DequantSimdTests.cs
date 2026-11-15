using System;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Checks vector dequantization against independent scalar coefficient evaluation.</summary>
[TestFixture]
public sealed class DequantSimdTests
{
    /// <summary>Zero runs, unit coefficients, full-range samples, tails and padding stay bit exact.</summary>
    [TestCase(ACType.k16)]
    [TestCase(ACType.k32)]
    public void CoefficientsMatchScalar(ACType type)
    {
        var random = new Random(871);
        foreach (int count in new[] { 0, 1, 7, 8, 9, 64, 256 })
        for (int pattern = 0; pattern < 5; pattern++)
        {
            int stride = count + 5;
            int[][] q = new int[3][];
            var pointers = new ACPtr[3];
            for (int c = 0; c < 3; c++)
            {
                q[c] = new int[count];
                short[] small = new short[count + 3];
                int[] wide = new int[count + 3];
                for (int k = 0; k < count; k++)
                {
                    int value = pattern switch
                    {
                        0 => 0, 1 => random.Next(-1, 2),
                        2 => random.Next(-2000, 2001),
                        3 => (k % 4) switch { 0 => type == ACType.k16 ? short.MinValue : int.MinValue,
                            1 => type == ACType.k16 ? short.MaxValue : int.MaxValue, 2 => -1, _ => 0 },
                        _ => k % 13 == 0 ? random.Next(-20, 21) : 0,
                    };
                    q[c][k] = wide[k + 3] = value; small[k + 3] = (short)value;
                }
                pointers[c] = type == ACType.k16 ? new ACPtr(small.AsMemory(3, count)) : new ACPtr(wide.AsMemory(3, count));
            }
            float[] matrix = new float[3 * stride], actual = new float[3 * stride + 4], expected = new float[actual.Length];
            Array.Fill(actual, -12345f); Array.Fill(expected, -12345f);
            for (int k = 0; k < matrix.Length; k++) matrix[k] = (float)(random.NextDouble() * 2 - 1);
            float[] biases = { .945f, .88f, .91f, .145f };
            float[] scales = { .17f, -.7f, 1.2f };
            for (int k = 0; k < count; k++)
            {
                if ((q[0][k] | q[1][k] | q[2][k]) == 0)
                {
                    for (int c = 0; c < 3; c++) expected[2 + c * stride + k] = 0;
                    continue;
                }
                float Adjust(int c)
                {
                    int v = q[c][k];
                    return v == 0 ? 0f : v == 1 ? biases[c] : v == -1 ? -biases[c] : (float)v - biases[3] / (float)v;
                }
                float x = Adjust(0) * (matrix[k] * scales[0]);
                float y = Adjust(1) * (matrix[stride + k] * scales[1]);
                float b = Adjust(2) * (matrix[2 * stride + k] * scales[2]);
                expected[2 + k] = MathF.FusedMultiplyAdd(-.5f, y, x);
                expected[2 + stride + k] = y;
                expected[2 + 2 * stride + k] = MathF.FusedMultiplyAdd(.25f, y, b);
            }
            DecGroup.DequantizeCoefficients(pointers, type, matrix, biases, actual.AsSpan(2), count, stride,
                (scales[0], scales[1], scales[2]), (-.5f, .25f));
            for (int k = 0; k < actual.Length; k++)
                Assert.That(BitConverter.SingleToInt32Bits(actual[k]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected[k])), $"{type}, count={count}, pattern={pattern}, k={k}");
        }
    }
}
