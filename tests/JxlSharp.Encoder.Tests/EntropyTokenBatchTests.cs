using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Decoder;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Compare optimized token loops to the frozen encoder with separate word/length arrays.</summary>
    [TestFixture]
    public sealed class EntropyTokenBatchTests
    {
        /// <summary>Cover list/array/fallback input, 31 extra bits, contexts, empty streams and LZ77 flags.</summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void TokenBytesMatchOriginal(bool prefix, bool lz77)
        {
            foreach (int contexts in new[] { 1, 4 })
            foreach (HistogramParams.HybridUintMethod method in new[] { HistogramParams.HybridUintMethod.k000, HistogramParams.HybridUintMethod.kNone })
            {
                Random random = new Random(123);
                List<Token> stream = new List<Token>();
                for (int i = 0; i < 4096; i++)
                {
                    uint value = lz77 ? (uint)(i / 512) : i % 5 == 0 ? uint.MaxValue :
                        i % 5 == 1 ? 1u << 31 : (uint)random.Next(0, 1000);
                    stream.Add(new Token((uint)(lz77 ? (i / 512) % contexts : i % contexts), value));
                }
                Token[] original = stream.ToArray();
                BitWriter header = new BitWriter();
                EntropyEncodingData codes = new EntropyEncodingData { UsePrefixCode = prefix };
                HistogramParams parameters = new HistogramParams
                {
                    ForceHuffman = prefix,
                    InitializeGlobalState = false,
                    UintMethod = method,
                    Lz77Method = lz77 ? HistogramParams.LZ77Method.kRLE : HistogramParams.LZ77Method.kNone,
                };
                List<List<Token>> streams = new List<List<Token>> { stream };
                Assert.That(EncAns.BuildAndEncodeHistograms(null, parameters, (nuint)contexts,
                    streams, codes, header).IsOk, Is.True);
                stream = streams[0];
                Assert.That(codes.UsePrefixCode, Is.EqualTo(prefix));
                if (lz77) Assert.That(stream.Exists(t => t.IsLz77Length), Is.True);

                foreach (int length in new[] { 0, 1, 3, 7, 16, 63, 64, 65, 257, stream.Count })
                {
                    if (length > stream.Count) continue;
                    List<Token> subset = stream.GetRange(0, length);
                    IReadOnlyList<Token>[] inputs = { subset, subset.ToArray(), subset.AsReadOnly(), new ReverseTokens(subset) };
                    foreach (IReadOnlyList<Token> input in inputs)
                    {
                        BitWriter expected = new BitWriter(), actual = new BitWriter();
                        expected.Write(3, 5); actual.Write(3, 5);
                        nuint extra = codes.UsePrefixCode ? ScalarEntropyReference.WritePrefixTokens(input, codes, 0, expected) :
                            ScalarEntropyReference.WriteAnsTokens(input, codes, 0, expected);
                        Assert.That(EncAns.WriteTokens(input, codes, 0, actual), Is.EqualTo(extra));
                        Assert.That(actual.BitsWritten, Is.EqualTo(expected.BitsWritten));
                        Assert.That(actual.GetSpan().ToArray(), Is.EqualTo(expected.GetSpan().ToArray()));
                        using var a = actual.TakeBytes();
                        using var e = expected.TakeBytes();
                    }
                }

                Assert.That(EncAns.WriteTokens(stream, codes, 0, header, LayerType.Ac, null).IsOk, Is.True);
                header.ZeroPadToByte();
                using BitReader reader = new BitReader(header.GetSpan());
                ANSCode decoded = new ANSCode();
                Assert.That(DecAns.DecodeHistograms(null, reader, (nuint)contexts, decoded, out byte[] map).IsOk, Is.True);
                ANSSymbolReader symbols = ANSSymbolReader.Create(decoded, reader).Value;
                foreach (Token token in original)
                    Assert.That(symbols.ReadHybridUint(token.Context, reader, map), Is.EqualTo(token.Value));
                Assert.That(symbols.CheckANSFinalState(), Is.True);
                Assert.That(reader.Close().IsOk, Is.True);
                using var storage = header.TakeBytes();
            }
        }

        /// <summary>The context offset still indexes the full context map for each container path.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ContextOffsetMatchesOriginal(bool prefix)
        {
            List<Token> training = new List<Token>();
            for (int i = 0; i < 1024; i++) training.Add(new Token((uint)(1 + i % 2), (uint)(i % 17)));
            EntropyEncodingData codes = new EntropyEncodingData { UsePrefixCode = prefix };
            BitWriter header = new BitWriter();
            Assert.That(EncAns.BuildAndEncodeHistograms(null, new HistogramParams
            {
                ForceHuffman = prefix, Lz77Method = HistogramParams.LZ77Method.kNone,
            }, 4, new List<List<Token>> { training }, codes, header).IsOk, Is.True);
            List<Token> local = training.ConvertAll(t => new Token(t.Context - 1, t.Value));
            BitWriter expected = new BitWriter(), actual = new BitWriter();
            if (prefix) ScalarEntropyReference.WritePrefixTokens(local, codes, 1, expected);
            else ScalarEntropyReference.WriteAnsTokens(local, codes, 1, expected);
            EncAns.WriteTokens(local, codes, 1, actual);
            Assert.That(actual.BitsWritten, Is.EqualTo(expected.BitsWritten));
            Assert.That(actual.GetSpan().ToArray(), Is.EqualTo(expected.GetSpan().ToArray()));
            using var a = actual.TakeBytes();
            using var e = expected.TakeBytes();
            using var h = header.TakeBytes();
        }

        private sealed class ReverseTokens(IEnumerable<Token> tokens) : List<Token>(tokens), IReadOnlyList<Token>
        {
            Token IReadOnlyList<Token>.this[int index] => base[Count - index - 1];
        }
    }
}
