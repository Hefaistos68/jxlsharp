using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Decoder;
using NUnit.Framework;

namespace JxlSharp.Encoder;

/// <summary>Decode independently generated AC token streams, including progressive accumulation.</summary>
[TestFixture]
public sealed class AcCoefficientDecodeTests
{
    private sealed record Block(AcStrategy Strategy, uint Channel, uint Shift, uint[] Order, uint[] Values, uint Nonzeros);

    /// <summary>Keep scanning, signed coefficients, LLF slots, row counts and stream state identical.</summary>
    [TestCase(false, false, ACType.k16)]
    [TestCase(false, false, ACType.k32)]
    [TestCase(true, false, ACType.k16)]
    [TestCase(true, false, ACType.k32)]
    [TestCase(false, true, ACType.k16)]
    [TestCase(false, true, ACType.k32)]
    [TestCase(true, true, ACType.k16)]
    [TestCase(true, true, ACType.k32)]
    public void DecodeGeneratedBlocks(bool prefix, bool lz77, ACType type)
    {
        const uint contextOffset = 3;
        var contexts = new BlockCtxMap { NumCtxs = 1, CtxMap = new byte[39] };
        uint count = contexts.NumACContexts() + contextOffset + 16;
        var blocks = new List<Block>();
        var tokens = new List<Token>();
        var random = new Random(765);
        foreach (var strategy in new[] { AcStrategyType.DCT, AcStrategyType.DCT16X16, AcStrategyType.DCT16X8 })
        foreach (uint shift in new[] { 0u, 3u, 31u })
        for (int pattern = 0; pattern < 4; pattern++)
        {
            var acs = AcStrategy.FromRawStrategy(strategy);
            int covered = 1 << (int)acs.Log2CoveredBlocks();
            int size = covered * 64;
            uint[] order = new uint[size], values = new uint[size];
            uint nonzeros = 0;
            for (int k = 0; k < size; k++)
            {
                // Reverse the AC order; leave all LLF entries at the start untouched.
                order[k] = (uint)(k < covered ? k : size + covered - 1 - k);
                if (k < covered) continue;
                values[k] = pattern switch
                {
                    0 => 0,
                    1 => 2,
                    2 => k == size - 1 ? uint.MaxValue : k % 13 == 0 ? 1u : 0,
                    _ => k % 7 == 0 ? 0 : (uint)random.Next(1, 100000),
                };
                if (values[k] != 0) nonzeros++;
            }
            uint channel = (uint)(pattern % 3);
            uint nzeroCtx = contexts.NonZeroContext(8, 0) + contextOffset;
            tokens.Add(new Token(nzeroCtx, nonzeros));
            nuint left = nonzeros, prev = nonzeros > size / 16 ? 0u : 1u;
            for (int k = covered; k < size && left != 0; k++)
            {
                nuint ctx = contextOffset + contexts.ZeroDensityContextsOffset(0) +
                    AcContext.ZeroDensityContext(left, (nuint)k, (nuint)covered, acs.Log2CoveredBlocks(), prev);
                tokens.Add(new Token((uint)ctx, values[k]));
                prev = values[k] == 0 ? 0u : 1u;
                left -= prev;
            }
            blocks.Add(new Block(acs, channel, shift, order, values, nonzeros));
        }
        var writer = new BitWriter();
        var codes = new EntropyEncodingData { UsePrefixCode = prefix };
        var streams = new List<List<Token>> { tokens };
        Assert.That(EncAns.BuildAndEncodeHistograms(null, new HistogramParams
        {
            ForceHuffman = prefix, InitializeGlobalState = false,
            Lz77Method = lz77 ? HistogramParams.LZ77Method.kRLE : HistogramParams.LZ77Method.kNone,
        }, count, streams, codes, writer).IsOk, Is.True);
        Assert.That(EncAns.WriteTokens(streams[0], codes, 0, writer, LayerType.Ac, null).IsOk, Is.True);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan());
        var decoded = new ANSCode();
        Assert.That(DecAns.DecodeHistograms(null, reader, count, decoded, out byte[] map).IsOk, Is.True);
        Assert.That(decoded.UsePrefixCode, Is.EqualTo(prefix));
        Assert.That(decoded.Lz77.Enabled, Is.EqualTo(lz77));
        var symbols = ANSSymbolReader.Create(decoded, reader).Value;
        foreach (var block in blocks)
        {
            int size = block.Values.Length;
            uint[] order = new uint[checked((int)CoeffOrder.CoeffOrderOffset(
                CoeffOrder.StrategyOrder[block.Strategy.RawStrategy()], (int)block.Channel)) + size];
            block.Order.CopyTo(order, order.Length - size);
            int[] wide = new int[size + 6], expected = new int[size + 6];
            short[] small = new short[size + 6];
            for (int i = 0; i < wide.Length; i++)
                wide[i] = expected[i] = small[i] = (short)(i * 197);
            int covered = 1 << (int)block.Strategy.Log2CoveredBlocks();
            for (int k = covered; k < size; k++)
            {
                uint packed = block.Values[k];
                int signed = unchecked((int)(packed >> 1) ^ -((int)packed & 1));
                int pos = (int)block.Order[k] + 3;
                int value = unchecked(expected[pos] + (signed << (int)block.Shift));
                expected[pos] = type == ACType.k16 ? unchecked((short)value) : value;
            }
            int stride = (int)block.Strategy.CoveredBlocksX() + 3;
            int[] row = new int[stride * (int)block.Strategy.CoveredBlocksY()];
            Array.Fill(row, 7);
            int[] top = new int[stride];
            Array.Fill(top, 9);
            var args = new DecGroup.AcVarBlockArgs
            {
                CtxOffset = contextOffset, Log2CoveredBlocks = block.Strategy.Log2CoveredBlocks(),
                RowNzeros = row, RowNzerosTop = top, NzerosStride = (nuint)stride,
                C = block.Channel, Bx = 1, By = 2, Lbx = 0, Acs = block.Strategy, CoeffOrder = order,
                Br = reader, Decoder = symbols, ContextMap = map, QdcRow = new byte[1], QfRow = new int[] { 1 },
                BlockCtxMap = contexts, Shift = block.Shift, AcType = type, UsesLz77 = decoded.Lz77.Enabled,
                Block = type == ACType.k16 ? new ACPtr(small.AsMemory(3, size)) : new ACPtr(wide.AsMemory(3, size)),
            };
            Assert.That(DecGroup.DecodeACVarBlock(in args).IsOk, Is.True);
            for (int i = 0; i < expected.Length; i++)
                Assert.That(type == ACType.k16 ? small[i] : wide[i], Is.EqualTo(expected[i]));
            int scaledNonzeros = (int)((block.Nonzeros + (uint)covered - 1) >> (int)block.Strategy.Log2CoveredBlocks());
            for (int y = 0; y < (int)block.Strategy.CoveredBlocksY(); y++)
            for (int x = 0; x < stride; x++)
                Assert.That(row[y * stride + x], Is.EqualTo(x >= 1 && x < stride - 2 ? scaledNonzeros : 7));
        }
        Assert.That(symbols.CheckANSFinalState(), Is.True);
        Assert.That(reader.Close().IsOk, Is.True);
        using var storage = writer.TakeBytes();
    }
}
