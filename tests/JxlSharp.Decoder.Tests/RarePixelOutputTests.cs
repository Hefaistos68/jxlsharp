using System;
using System.Buffers.Binary;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks half, grayscale and uncommon integer output at exact buffer boundaries.</summary>
    [TestFixture]
    public sealed class RarePixelOutputTests
    {
        /// <summary>All channel counts, endian modes, row padding and vector/scalar tails match scalar casts.</summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(257)]
        public void RareRowsMatchScalar(int width)
        {
            foreach (int count in new[] { 1, 2, 3, 4 })
            foreach (int bits in new[] { 1, 7, 16, 17, 24, 25, 30, 31, 32 })
            foreach (bool little in new[] { false, true })
            {
                bool half = bits == 16;
                int bytes = (bits + 7) / 8, stride = width * count * bytes + 7;
                byte[] actual = new byte[stride + width * count * bytes + 2];
                Array.Fill(actual, (byte)0xcd);
                byte[] expected = (byte[])actual.Clone();
                ImagePlane<float>[] planes = new ImagePlane<float>[count];
                float[] values = { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -0f, -1f,
                    0f, MathF.Pow(2, -25), MathF.Pow(2, -24), 0.003f, 0.5f, 0.99999994f, 1f, 65520f,
                    BitConverter.UInt32BitsToSingle(0x7fa12345), BitConverter.UInt32BitsToSingle(0xffc12345) };
                try
                {
                    for (int c = 0; c < count; c++)
                    {
                        planes[c] = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, 2).Value;
                        for (nuint y = 0; y < 2; y++)
                        for (int x = 0; x < width; x++) planes[c].Row(y)[x] = values[(x + c * 3 + (int)y) % values.Length];
                    }
                    ExternalConvertOptions options = new ExternalConvertOptions((nuint)bits, half,
                        little ? JxlEndianness.LittleEndian : JxlEndianness.BigEndian, (nuint)stride, null, actual.AsMemory(1, actual.Length - 2));
                    Assert.That(DecExternalImage.ConvertChannelsToExternal(planes, (nuint)count, in options).IsOk, Is.True);
                    float mul = bits == 32 ? 4294967295f : (1u << bits) - 1u;
                    for (nuint y = 0; y < 2; y++)
                    for (int x = 0; x < width; x++)
                    for (int c = 0; c < count; c++)
                    {
                        float value = planes[c].ConstRow(y)[x];
                        uint raw = half ? BitConverter.HalfToUInt16Bits((Half)value) : !(value >= 0f) ? 0u :
                            value >= 1f ? (uint)mul : (uint)(value * mul + 0.5f);
                        int offset = 1 + (int)y * stride + (x * count + c) * bytes;
                        for (int b = 0; b < bytes; b++) expected[offset + (little ? b : bytes - b - 1)] = (byte)(raw >> (8 * b));
                    }
                    Assert.That(actual, Is.EqualTo(expected), $"width={width}, channels={count}, bits={bits}, little={little}");
                }
                finally { foreach (var plane in planes) plane?.Dispose(); }
            }
        }

        /// <summary>The public row converter honours its count and surrounding output sentinels.</summary>
        [TestCase(0)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(65536)]
        public void FloatToF16MatchesScalar(int count)
        {
            float[] input = new float[count];
            for (int i = 0; i < count; i++) input[i] = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            ushort[] output = new ushort[count + 2];
            Array.Fill(output, (ushort)0xcdcd);
            DecExternalImage.FloatToF16(input, output.AsSpan(1, count), (nuint)count);
            for (int i = 0; i < count; i++) Assert.That(output[i + 1], Is.EqualTo(BitConverter.HalfToUInt16Bits((Half)input[i])));
            Assert.That(output[0], Is.EqualTo(0xcdcd));
            Assert.That(output[^1], Is.EqualTo(0xcdcd));
        }
    }
}
