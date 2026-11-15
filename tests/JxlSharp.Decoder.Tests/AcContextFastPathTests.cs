using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Keep the specialized 8x8 coefficient contexts identical to the general model.</summary>
[TestFixture]
public sealed class AcContextFastPathTests
{
    /// <summary>Cover every remaining-nonzero count, scan position and previous-symbol state.</summary>
    [Test]
    public void EightByEightLookupMatchesGeneralContext()
    {
        for (nuint nonzeros = 1; nonzeros < 64; nonzeros++)
        for (nuint k = 1; k < 64; k++)
        for (nuint prev = 0; prev < 2; prev++)
            Assert.That(AcContext.ZeroDensityContext8x8(nonzeros, k, prev),
                Is.EqualTo(AcContext.ZeroDensityContext(nonzeros, k, 1, 0, prev)),
                $"nonzeros={nonzeros}, k={k}, prev={prev}");
    }
}
