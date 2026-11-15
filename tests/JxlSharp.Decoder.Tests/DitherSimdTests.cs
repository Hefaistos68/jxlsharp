using System;
using System.Runtime.Intrinsics;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Checks identical noise lookup, clipping and ties-to-even conversion.</summary>
[TestFixture]
public sealed class DitherSimdTests
{
    /// <summary>All table phases, channels, caps and exceptional inputs match the scalar writer.</summary>
    [Test]
    public void EightSamplesMatchScalar()
    {
        float[] values = new float[8];
        var random = new Random(272);
        foreach (float cap in new[] { 1f, 3f, 31f, 127f, 255f })
        for (nuint channel = 0; channel < 4; channel++)
        for (nuint y = 0; y < 33; y++)
        for (nuint x = 0; x < 33; x++)
        {
            for (int lane = 0; lane < 8; lane++)
                values[lane] = x == 0 ? lane switch { 0 => float.NaN, 1 => float.NegativeInfinity,
                    2 => float.PositiveInfinity, 3 => -0f, 4 => 0f, 5 => 1f, 6 => -2f, _ => 2f }
                    : (float)(random.NextDouble() * 1.4 - .2);
            var vector = RenderPipelineStages.QuantizeDither8(values, x, y, cap, channel);
            for (int lane = 0; lane < 8; lane++)
                Assert.That(vector.GetElement(lane), Is.EqualTo((uint)RenderPipelineStages.MakeUnsigned8(values[lane], x + (nuint)lane, y, cap, channel)));
        }
    }
}

