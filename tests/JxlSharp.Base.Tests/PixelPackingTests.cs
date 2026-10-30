using System;
using NUnit.Framework;

namespace JxlSharp.Base.Tests
{
    /// <summary>Checks CMS row interleaving without floating-point arithmetic.</summary>
    [TestFixture]
    public sealed class PixelPackingTests
    {
        /// <summary>Vector and scalar tails preserve NaN payloads, signed zero and surrounding memory.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(17)]
        [TestCase(33)]
        public void InterleavingPreservesBits(int count)
        {
            float[][] planes = new float[4][];
            Random random = new Random(123);
            for (int c = 0; c < 4; c++)
            {
                planes[c] = new float[count];
                for (int x = 0; x < count; x++) planes[c][x] = BitConverter.Int32BitsToSingle((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1));
            }
            foreach (int channels in new[] { 3, 4 })
            {
                float[] packed = new float[count * channels + 2];
                Array.Fill(packed, -12345f);
                if (channels == 3) PixelPacking.Interleave3(planes[0], planes[1], planes[2], packed.AsSpan(1, count * channels), count);
                else PixelPacking.Interleave4(planes[0], planes[1], planes[2], planes[3], packed.AsSpan(1, count * channels), count);
                Assert.That(packed[0], Is.EqualTo(-12345f));
                Assert.That(packed[packed.Length - 1], Is.EqualTo(-12345f));
                for (int x = 0; x < count; x++)
                    for (int c = 0; c < channels; c++)
                        Assert.That(BitConverter.SingleToInt32Bits(packed[1 + x * channels + c]), Is.EqualTo(BitConverter.SingleToInt32Bits(planes[c][x])));
                if (channels == 3)
                {
                    float[] r = new float[count], g = new float[count], b = new float[count];
                    PixelPacking.Deinterleave3(packed.AsSpan(1, count * 3), r, g, b, count);
                    float[][] output = { r, g, b };
                    for (int c = 0; c < 3; c++)
                        for (int x = 0; x < count; x++)
                            Assert.That(BitConverter.SingleToInt32Bits(output[c][x]), Is.EqualTo(BitConverter.SingleToInt32Bits(planes[c][x])));
                }
            }
        }
    }
}
