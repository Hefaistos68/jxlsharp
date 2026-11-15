using System;
using System.Buffers.Binary;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests
{
    /// <summary>Checks fused quantization/packing against scalar results and output buffer sentinels.</summary>
    [TestFixture]
    public sealed class ExternalPixelSimdTests
    {
        /// <summary>All channel counts, endian modes, float payloads and integer boundaries.</summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(17)]
        [TestCase(33)]
        public void OutputRowsMatchScalar(int width)
        {
            foreach (int channels in new[] { 1, 2, 3, 4 })
            foreach (int bits in new[] { 8, 10, 16, 32 })
            foreach (bool little in new[] { false, true })
            {
                bool isFloat = bits == 32;
                int bytes = isFloat ? 4 : bits <= 8 ? 1 : 2;
                int stride = width * channels * bytes + 7;
                byte[] actual = new byte[stride + width * channels * bytes + 2];
                Array.Fill(actual, (byte)0xcd);
                byte[] expected = (byte[])actual.Clone();
                ImagePlane<float>[] planes = new ImagePlane<float>[channels];
                float[] values = { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -0f, -0.5f, 0f, 0.003f, 0.5f, 0.99999994f, 1f, 2f, BitConverter.UInt32BitsToSingle(0x7fa12345) };
                try
                {
                    for (int c = 0; c < channels; c++)
                    {
                        planes[c] = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, 2).Value;
                        for (nuint y = 0; y < 2; y++)
                            for (int x = 0; x < width; x++) planes[c].Row(y)[x] = values[(x + c * 3 + (int)y) % values.Length];
                    }
                    ExternalConvertOptions options = new ExternalConvertOptions((nuint)bits, isFloat, little ? JxlEndianness.LittleEndian : JxlEndianness.BigEndian, (nuint)stride, null, actual.AsMemory(1, actual.Length - 2));
                    Assert.That(DecExternalImage.ConvertChannelsToExternal(planes, (nuint)channels, in options).IsOk, Is.True);
                    float mul = (1u << bits) - 1u;
                    for (nuint y = 0; y < 2; y++)
                        for (int x = 0; x < width; x++)
                            for (int c = 0; c < channels; c++)
                            {
                                float v = planes[c].ConstRow(y)[x];
                                uint sample = isFloat ? BitConverter.SingleToUInt32Bits(v) : !(v >= 0f) ? 0u : v >= 1f ? (uint)mul : (uint)(v * mul + 0.5f);
                                Span<byte> dst = expected.AsSpan(1 + (int)y * stride + (x * channels + c) * bytes, bytes);
                                if (bytes == 1) dst[0] = (byte)sample;
                                else if (bytes == 2) { if (little) BinaryPrimitives.WriteUInt16LittleEndian(dst, (ushort)sample); else BinaryPrimitives.WriteUInt16BigEndian(dst, (ushort)sample); }
                                else if (little) BinaryPrimitives.WriteUInt32LittleEndian(dst, sample);
                                else BinaryPrimitives.WriteUInt32BigEndian(dst, sample);
                            }
                    Assert.That(actual, Is.EqualTo(expected), $"width={width}, channels={channels}, bits={bits}, little={little}");
                }
                finally { foreach (ImagePlane<float> plane in planes) plane?.Dispose(); }
            }
        }

        /// <summary>Public conversion keeps clipping independent of a caller-supplied scale.</summary>
        [Test]
        public void FloatToU32PreservesCustomScale()
        {
            float[] source = { 0f, 0.5f, 1f, 2f, float.NaN, -1f, float.PositiveInfinity, 0.999f, 0.25f };
            uint[] actual = new uint[source.Length];
            DecExternalImage.FloatToU32(source, actual, (nuint)source.Length, 100f, 8);
            for (int x = 0; x < source.Length; x++)
            {
                uint[] expected = new uint[1];
                DecExternalImage.FloatToU32(source.AsSpan(x, 1), expected, 1, 100f, 8);
                Assert.That(actual[x], Is.EqualTo(expected[0]));
            }
        }
    }
}
