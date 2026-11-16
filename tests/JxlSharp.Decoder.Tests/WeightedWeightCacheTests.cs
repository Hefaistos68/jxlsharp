using System;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Verify the shared weighted-predictor table against the original arithmetic.</summary>
[TestFixture]
public sealed class WeightedWeightCacheTests
{
    /// <summary>Exhaust the cached range and its boundary with valid and extended header weights.</summary>
    [Test]
    public void SmallErrorsMatchOriginal()
    {
        for (uint weight = 0; weight < 32; weight++)
        for (ulong error = 0; error < 4096; error++)
            Assert.That(WeightedState.ErrorWeightCached(error, weight), Is.EqualTo(WeightedState.ErrorWeight(error, weight)));
    }

    /// <summary>Large errors, uint overflow in multiplication and non-codec weights keep the fallback.</summary>
    [Test]
    public void LargeErrorsAndWeightsMatchOriginal()
    {
        var random = new Random(419);
        foreach (uint weight in new[] { 0u, 1u, 15u, 16u, 255u, 65535u, uint.MaxValue })
        {
            for (int bit = 0; bit < 64; bit++)
            foreach (ulong error in new[] { (1UL << bit) - 1, 1UL << bit, (1UL << bit) + 1 })
                Assert.That(WeightedState.ErrorWeightCached(error, weight), Is.EqualTo(WeightedState.ErrorWeight(error, weight)));
            for (int i = 0; i < 10000; i++)
            {
                ulong error = (ulong)random.NextInt64(0, long.MaxValue);
                Assert.That(WeightedState.ErrorWeightCached(error, weight), Is.EqualTo(WeightedState.ErrorWeight(error, weight)));
            }
        }
    }
}
