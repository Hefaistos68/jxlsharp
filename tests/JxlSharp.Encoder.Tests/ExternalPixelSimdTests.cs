using System;
using System.Buffers.Binary;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Checks packed input conversion against scalar byte decoding at exact row boundaries.</summary>
    [TestFixture]
    public sealed class ExternalPixelSimdTests
    {
        /// <summary>RGB/RGBA, endian conversion, unaligned scanlines and short tails preserve all samples.</summary>
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
        public void InputRowsMatchScalar(int width)
        {
            foreach (int channels in new[] { 1, 2, 3, 4 })
            foreach (JxlDataType type in new[] { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float16, JxlDataType.Float })
            foreach (bool little in new[] { false, true })
            {
                int bytes = type == JxlDataType.UInt8 ? 1 : type == JxlDataType.UInt16 || type == JxlDataType.Float16 ? 2 : 4;
                int stride = width * channels * bytes + 7;
                byte[] input = new byte[stride + width * channels * bytes];
                new Random(123).NextBytes(input);
                if (type == JxlDataType.Float)
                {
                    uint[] patterns = { 0x80000000, 0x7fa12345, 0x7fc12345, 0x7f800000, 0xff800000, 0x3f000000, 0xbf800000, 0x00000001 };
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < width * channels; x++)
                        {
                            Span<byte> dst = input.AsSpan(y * stride + x * 4, 4);
                            if (little) BinaryPrimitives.WriteUInt32LittleEndian(dst, patterns[x % patterns.Length]);
                            else BinaryPrimitives.WriteUInt32BigEndian(dst, patterns[x % patterns.Length]);
                        }
                }
                foreach (int bits in type == JxlDataType.UInt8 ? new[] { 1, 7, 8 } : type == JxlDataType.UInt16 ? new[] { 9, 10, 12, 16 } : new[] { bytes * 8 })
                for (int c = 0; c < channels; c++)
                {
                    using ImagePlane<float> plane = ImagePlane<float>.Create(JxlMemoryManager.Default, (nuint)width, 2).Value;
                    JxlPixelFormat format = new JxlPixelFormat { NumChannels = (uint)channels, DataType = type, Endianness = little ? JxlEndianness.LittleEndian : JxlEndianness.BigEndian };
                    ExternalBufferSpec spec = new ExternalBufferSpec((nuint)width, 2, (nuint)stride, (nuint)bits, format, (nuint)c);
                    Assert.That(EncExternalImage.ConvertFromExternal(input, in spec, null, plane).IsOk, Is.True);
                    float mul = (float)(1.0 / ((1UL << bits) - 1));
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < width; x++)
                        {
                            ReadOnlySpan<byte> sample = input.AsSpan(y * stride + (x * channels + c) * bytes, bytes);
                            float expected = type == JxlDataType.UInt8 ? sample[0] * mul
                                : type == JxlDataType.UInt16 ? (little ? BinaryPrimitives.ReadUInt16LittleEndian(sample) : BinaryPrimitives.ReadUInt16BigEndian(sample)) * mul
                                : type == JxlDataType.Float16 ? (float)BitConverter.UInt16BitsToHalf(little ? BinaryPrimitives.ReadUInt16LittleEndian(sample) : BinaryPrimitives.ReadUInt16BigEndian(sample))
                                : BitConverter.UInt32BitsToSingle(little ? BinaryPrimitives.ReadUInt32LittleEndian(sample) : BinaryPrimitives.ReadUInt32BigEndian(sample));
                            Assert.That(BitConverter.SingleToInt32Bits(plane.ConstRow((nuint)y)[x]), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"{type}, channels={channels}, width={width}, c={c}, x={x}");
                        }
                }
            }
        }
    }
}
