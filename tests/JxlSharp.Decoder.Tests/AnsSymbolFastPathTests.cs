using System;
using JxlSharp.Base;
using NUnit.Framework;

namespace JxlSharp.Decoder.Tests;

/// <summary>Check the hot ANS path against the original alias-table lookup and state transition.</summary>
[TestFixture]
public sealed class AnsSymbolFastPathTests
{
    /// <summary>Redundant refills preserve unaligned reads, zero extension and end-of-input checks.</summary>
    [Test]
    public void RepeatedRefillsPreserveBitsAndBounds()
    {
        var random = new Random(956);
        foreach (int length in new[] { 0, 1, 7, 8, 9, 16, 257 })
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            using var bits = new BitReader(bytes);
            int position = 0;
            while (position < length * 8 + 64)
            {
                int count = random.Next(1, 57);
                for (int i = 0; i < 5; i++) bits.Refill();
                ulong expected = 0;
                for (int i = 0; i < count; i++)
                {
                    int bit = position + i;
                    if (bit < bytes.Length * 8)
                        expected |= (ulong)((bytes[bit / 8] >> (bit % 8)) & 1) << i;
                }
                Assert.That(bits.PeekBits((nuint)count), Is.EqualTo(expected));
                bits.Consume((nuint)count);
                position += count;
                Assert.That(bits.TotalBitsConsumed(), Is.EqualTo((nuint)position));
                Assert.That(bits.AllReadsWithinBounds().IsOk, Is.EqualTo(position <= length * 8));
            }
            // Close reports reads performed after the last explicit bounds check.
            bits.Refill();
            bits.Consume(1);
            Assert.That(bits.Close().IsOk, Is.False);
        }
    }

    /// <summary>Exercise sparse, single-symbol and flat histograms with and without renormalization.</summary>
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(8)]
    public void EveryAliasSlotPreservesSymbolStateAndBitPosition(int logAlpha)
    {
        int length = 1 << logAlpha;
        var tables = new AliasTable.Entry[3 * length];
        int[][] distributions = { new[] { 4096 }, new[] { 4093, 1, 0, 2 }, AliasTable.CreateFlatHistogram(length, 4096) };
        for (int h = 0; h < 3; h++)
            Assert.That(AliasTable.InitAliasTable(distributions[h], AnsParams.AnsLogTabSize,
                (uint)logAlpha, tables.AsSpan(h * length, length)).IsOk, Is.True);
        var code = new ANSCode { AliasTables = tables, LogAlphaSize = (byte)logAlpha };
        byte[] bytes = { 0xAD, 0x73, 0x51, 0xE2, 0x16, 0xAB, 0x89, 0x5C };
        using var bits = new BitReader(bytes);
        var decoder = ANSSymbolReader.Create(code, bits).Value;
        var checkpoint = new ANSSymbolReader.Checkpoint();
        var actual = new ANSSymbolReader.Checkpoint();
        uint logEntry = AnsParams.AnsLogTabSize - (uint)logAlpha;
        for (int h = 0; h < 3; h++)
        foreach (uint high in new[] { 16u, 1024u, 1048575u })
        for (uint slot = 0; slot < 4096; slot++)
        {
            uint state = (high << 12) | slot;
            checkpoint.State = state;
            decoder.Restore(checkpoint);
            bits.Reset(bytes);
            bits.Refill();
            var symbol = AliasTable.LookupAtOffset(tables, (nuint)(h * length), slot, logEntry, (1u << (int)logEntry) - 1u);
            uint next = (uint)symbol.Freq * high + (uint)symbol.Offset;
            bool normalize = next < 65536;
            if (normalize) next = (next << 16) | 0x73ADu;
            nuint value = decoder.ReadSymbolANSWithoutRefill((nuint)h, bits);
            decoder.Save(actual);
            Assert.That(value, Is.EqualTo(symbol.Value));
            Assert.That(actual.State, Is.EqualTo(next));
            Assert.That(bits.TotalBitsConsumed(), Is.EqualTo(normalize ? (nuint)16 : 0));
        }
    }
}
