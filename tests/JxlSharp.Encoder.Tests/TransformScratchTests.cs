using System;
using JxlSharp.Base;
using JxlSharp.Decoder;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder.Tests;

/// <summary>Checks transform scratch sizing for every strategy, including maximum rectangles.</summary>
[TestFixture]
public sealed class TransformScratchTests
{
    /// <summary>Actual map bounds select sufficient scratch for all 27 strategies.</summary>
    [Test]
    public void ActualStrategiesDetermineScratchSize()
    {
        using var map = AcStrategyImage.Create(null, 64, 64).Value;
        for (byte raw = 0; raw < AcStrategy.kNumValidStrategies; raw++)
        {
            map.FillDCT8();
            var strategy = AcStrategy.FromRawStrategy(raw);
            int area = (int)(strategy.CoveredBlocksX() * strategy.CoveredBlocksY() * 64);
            map.Set(32, 32, strategy.Strategy()).IsOk.ShouldBeTrue();
            EncCache.MaxTransformArea(map, new Rect(0, 0, 64, 64)).ShouldBe(area);
            EncCache.MaxTransformArea(map, new Rect(32, 32, 32, 32)).ShouldBe(area);
            EncCache.MaxTransformArea(map, new Rect(0, 0, 32, 32)).ShouldBe(64);
        }
    }

    /// <summary>Tightly sized coefficient and transform spans match the old maximum-size buffers.</summary>
    [Test]
    public void AllTransformsMatchWithTightScratchAndDirtyPooledStorage()
    {
        var random = new Random(31);
        for (byte raw = 0; raw < AcStrategy.kNumValidStrategies; raw++)
        {
            var strategy = AcStrategy.FromRawStrategy(raw);
            int width = (int)strategy.CoveredBlocksX() * 8;
            int height = (int)strategy.CoveredBlocksY() * 8;
            int area = width * height, stride = width + 13;
            var pixels = new float[stride * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = random.NextSingle() - 0.5f;
            var expected = new float[(int)AcStrategy.kMaxCoeffArea];
            var actual = new float[area];
            var largeScratch = new float[4 * (int)AcStrategy.kMaxCoeffArea];
            var smallScratch = new float[4 * area];
            Array.Fill(actual, float.NaN);
            Array.Fill(smallScratch, float.NaN);
            EncTransforms.TransformFromPixels(strategy.Strategy(), pixels, (nuint)stride, expected, largeScratch);
            EncTransforms.TransformFromPixels(strategy.Strategy(), pixels, (nuint)stride, actual, smallScratch);
            actual.AsSpan().SequenceEqual(expected.AsSpan(0, area)).ShouldBeTrue($"strategy {raw}");
            var dcExpected = new float[area / 64];
            var dcActual = new float[area / 64];
            EncTransforms.DCFromLowestFrequencies(strategy.Strategy(), expected, dcExpected,
                strategy.CoveredBlocksX(), largeScratch);
            EncTransforms.DCFromLowestFrequencies(strategy.Strategy(), actual, dcActual,
                strategy.CoveredBlocksX(), smallScratch);
            dcActual.ShouldBe(dcExpected);
        }
    }
}
