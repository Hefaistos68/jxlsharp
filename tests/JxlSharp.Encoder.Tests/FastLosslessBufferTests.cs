using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Encoder;

/// <summary>Checks bit-writer reuse and direct output queue writes.</summary>
[TestFixture]
public sealed class FastLosslessBufferTests
{
    /// <summary>Dirty storage must not affect emitted bits when buffers are reused.</summary>
    [Test]
    public void ReusedWriter_OverwritesDirtyBytesAndResetsState()
    {
        var writer = new FastLosslessBitWriter();
        try
        {
            writer.Allocate(2_000_000).ShouldBeTrue();
            byte[] storage = writer.Data!;
            Array.Fill(storage, (byte)0xA5);
            var random = new Random(58);
            byte[] expected = new byte[30000];
            int bitIndex = 0;
            for (int i = 0; i < 10000; i++)
            {
                int count = random.Next(1, 25);
                uint bits = (uint)random.Next() & ((1u << count) - 1);
                writer.Write((uint)count, bits);
                for (int bit = 0; bit < count; bit++, bitIndex++)
                    expected[bitIndex / 8] |= (byte)(((bits >> bit) & 1) << (bitIndex % 8));
            }
            writer.ZeroPadToByte();
            writer.Data!.AsSpan(0, (int)writer.BytesWritten)
                .SequenceEqual(expected.AsSpan(0, (bitIndex + 7) / 8)).ShouldBeTrue();
            writer.Allocate(1024).ShouldBeTrue();
            ReferenceEquals(writer.Data, storage).ShouldBeTrue();
            writer.Write(3, 5);
            writer.ZeroPadToByte();
            writer.BytesWritten.ShouldBe((nuint)1);
            writer.Data![0].ShouldBe((byte)5);
        }
        finally { writer.Release(); }
        writer.Release(); // Repeated disposal must not return the same array twice.
        writer.Data.ShouldBeNull();
    }

    /// <summary>Growth must preserve emitted data and a pending partial byte.</summary>
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(7)]
    public void GrowableWriter_PreservesBitsAcrossCapacityChanges(int pendingBits)
    {
        var writer = new FastLosslessBitWriter();
        try
        {
            writer.Allocate(2_000_000, growable: true).ShouldBeTrue();
            writer.Data!.Length.ShouldBe(32768);
            byte[] expected = new byte[200_000];
            var random = new Random(631);
            int index = 0;
            for (int row = 0; row < 1000; row++)
            {
                writer.EnsureCapacity(256);
                for (int x = 0; x < 253; x++)
                {
                    byte value = (byte)random.Next(256);
                    writer.Write(8, value);
                    for (int bit = 0; bit < 8; bit++, index++)
                        if (((value >> bit) & 1) != 0)
                            expected[index / 8] |= (byte)(1 << (index % 8));
                }
                if (row == 0 && pendingBits != 0)
                {
                    writer.Write((uint)pendingBits, (1UL << pendingBits) - 1);
                    for (int bit = 0; bit < pendingBits; bit++, index++)
                        expected[index / 8] |= (byte)(1 << (index % 8));
                }
                if (row == 700) break;
            }
            writer.ZeroPadToByte();
            writer.Data!.Length.ShouldBeGreaterThan(32768);
            writer.BytesWritten.ShouldBe((nuint)((index + 7) / 8));
            writer.Data.AsSpan(0, (int)writer.BytesWritten)
                .SequenceEqual(expected.AsSpan(0, (index + 7) / 8)).ShouldBeTrue();
        }
        finally { writer.Release(); }
    }

    /// <summary>Integer RGB conversion must preserve signs, alpha, short tails and row padding.</summary>
    [TestCase(3)]
    [TestCase(4)]
    public void ByteRows_MatchIntegerYCoCg(int channels)
    {
        foreach (int width in new[] { 0, 1, 7, 8, 9, 15, 16, 17, 255, 256, 257 })
        {
            byte[] input = new byte[width * channels];
            new Random(width + channels).NextBytes(input);
            int[][] rows = new int[channels][];
            for (int c = 0; c < channels; c++)
            {
                rows[c] = new int[width + 7];
                Array.Fill(rows[c], int.MinValue);
            }
            EncFastLossless.FillRowPixels8(input, width, channels, rows, 3);
            for (int x = 0; x < width; x++)
            {
                int r = input[x * channels], g = input[x * channels + 1], b = input[x * channels + 2];
                int co = r - b;
                int tmp = b + (co >> 1);
                int cg = g - tmp;
                rows[0][3 + x].ShouldBe(tmp + (cg >> 1));
                rows[1][3 + x].ShouldBe(co);
                rows[2][3 + x].ShouldBe(cg);
                if (channels == 4) rows[3][3 + x].ShouldBe(input[x * channels + 3]);
            }
            foreach (int[] row in rows)
            {
                row[2].ShouldBe(int.MinValue);
                row[3 + width].ShouldBe(int.MinValue);
            }
        }
    }

    /// <summary>Direct writes preserve prefixes and omit the writer's required padding.</summary>
    [TestCase(1)]
    [TestCase(257)]
    public void DirectOutput_MatchesChunkedOutputAndPreservesQueue(int width)
    {
        const int height = 257;
        byte[] pixels = new byte[width * height * 3];
        new Random(37).NextBytes(pixels);
        byte[] expected = EncFastLossless.FastLosslessEncode(pixels,
            ((nuint)width, (nuint)(width * 3), height, 3, 8)).Value;
        var input = EncFastLossless.CreateMemoryInputSource(pixels, (nuint)(width * 3), 3, 8);
        var args = new FastLosslessPrepareArgs(input, (nuint)width, height, 3, 8, false, 2);
        using var state = EncFastLossless.JxlFastLosslessPrepareFrame(in args)!;
        EncFastLossless.JxlFastLosslessProcessFrame(state, true).ShouldBeTrue();
        EncFastLossless.JxlFastLosslessPrepareHeader(state, 1, 1).ShouldBeTrue();
        var queue = new JxlEncoderOutputProcessorWrapper();
        byte[] prefix = { 1, 2, 3, 4, 5 };
        queue.Append(prefix);
        queue.DrainTo(new byte[3], out int first).ShouldBeFalse();
        first.ShouldBe(3);
        queue.AppendFastLosslessFrame(state).ShouldBe(expected.Length);
        queue.AvailableBytes.ShouldBe(expected.Length + 2);
        var actual = new List<byte>();
        byte[] chunk = new byte[127];
        bool done;
        do
        {
            done = queue.DrainTo(chunk, out int written);
            actual.AddRange(chunk.AsSpan(0, written));
        } while (!done);
        actual[0].ShouldBe((byte)4);
        actual[1].ShouldBe((byte)5);
        actual.RemoveRange(0, 2);
        actual.ToArray().ShouldBe(expected);
    }
}
