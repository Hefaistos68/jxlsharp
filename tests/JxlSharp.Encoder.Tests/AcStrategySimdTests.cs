using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder
{
    /// <summary>Checks exact arithmetic in strategy selection against sequential scalar evaluation.</summary>
    [TestFixture]
    public sealed class AcStrategySimdTests
    {
        /// <summary>Quantization preserves rounding ties, nonzero counts and the sequential entropy sum.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void QuantizationMatchesScalar(bool aliasLuma)
        {
            var random = new Random(197);

            foreach (float quant in new[] { .125f, .7f, 1f, 3.125f })
            foreach (float cfl in new[] { -.7f, 0f, .3f })
            for (int trial = 0; trial < 40; trial++)
            {
                float[] coefficients = new float[64], luma = new float[64], matrix = new float[64], inverse = new float[64];

                for (int k = 0; k < 64; k++)
                {
                    coefficients[k] = trial == 0 ? (k - 32) * .5f : (float)(random.NextDouble() * 200 - 100);
                    luma[k]         = (float)(random.NextDouble() * 20 - 10);
                    inverse[k]      = trial == 0 ? 1f : .1f + (float)random.NextDouble();
                    matrix[k]       = trial == 0 ? 1f : 1f / inverse[k];
                }

                float[] expected      = (float[])coefficients.Clone();
                float[] referenceLuma = aliasLuma ? expected : luma;
                float entropy         = 0;
                uint nonzeros         = 0;

                for (int k = 0; k < 64; k++)
                {
                    float value     = (expected[k] - cfl * referenceLuma[k]) * (inverse[k] * quant);
                    float rounded   = MathF.Round(value);
                    float magnitude = MathF.Abs(rounded);

                    entropy += MathF.Sqrt(magnitude);

                    if (magnitude != 0)
                    {
                        nonzeros++;
                    }

                    expected[k] = matrix[k] * (value - rounded);
                }

                float actual = AcStrategyHeuristics.QuantizeAndDequantizeChannel(coefficients,
                    aliasLuma ? coefficients : luma, inverse, matrix, quant, cfl, out uint actualNonzeros);

                actualNonzeros.ShouldBe(nonzeros);
                BitConverter.SingleToInt32Bits(actual).ShouldBe(BitConverter.SingleToInt32Bits(entropy));

                for (int k = 0; k < 64; k++)
                {
                    BitConverter.SingleToInt32Bits(coefficients[k]).ShouldBe(BitConverter.SingleToInt32Bits(expected[k]));
                }
            }
        }

        /// <summary>Loss evaluation matches scalar within floating-point tolerance across all 192 error samples.</summary>
        [Test]
        public void LossMatchesScalar()
        {
            var random             = new Random(68);
            float[] channelWeights = { 20441408f, 1f, 1.2667701f };
            float[] maskOffsets    = { 12f, 0f, 4f };

            for (int trial = 0; trial < 200; trial++)
            {
                float[] error = new float[192];

                for (int k = 0; k < error.Length; k++)
                {
                    error[k] = (float)(random.NextDouble() * .2 - .1);
                }

                float masking = (float)random.NextDouble();
                float quant   = .1f + trial * .02f;
                float sum     = 0;

                for (int c = 0; c < 3; c++)
                {
                    float channel = 0;

                    for (int i = 0; i < 64; i++)
                    {
                        float v  = (masking + maskOffsets[c]) * error[c * 64 + i];
                        float v2 = v * v;
                        float v4 = v2 * v2;

                        channel += v4 * v4;
                    }

                    sum += channelWeights[c] * channel;
                }

                float expected = MathF.Pow(sum * (1f / 64f), .125f) * 64f / MathF.Max(1e-4f, quant);
                float actual   = AcStrategyHeuristics.Compute8x8Loss(error, masking, quant);

                actual.ShouldBe(expected, (expected * 1e-5f) + 1e-6f);
            }
        }
    }
}
