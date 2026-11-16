using System;
using System.Collections.Generic;
using System.Reflection;
using JxlSharp.Base;
using JxlSharp.Decoder;
using NUnit.Framework;

namespace JxlSharp.Encoder
{
    /// <summary>Checks WP LUT tokens, append semantics, exceptional exits and tree fallback equivalence.</summary>
    [TestFixture]
    public sealed class WeightedPredictorEncodingTests
    {
        private static readonly Func<Channel, WeightedHeader, byte[], sbyte[], sbyte[], List<Token>, Status> Encode =
            typeof(EncModularEncoding).GetMethod("EncodeWpLutChannel", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Func<Channel, WeightedHeader, byte[], sbyte[], sbyte[], List<Token>, Status>>();

        /// <summary>Compare tokens to the original append loop across offsets, multipliers and predictor presets.</summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(17)]
        [TestCase(257)]
        public void LutTokensMatchScalar(int width)
        {
            using ModularImage image = ModularImage.Create(JxlMemoryManager.Default, (nuint)width, 9, 16, 1).Value;
            Channel channel = image.Channels[0];
            Random random = new Random(456);
            byte[] contexts = new byte[2 * TreeLut.KPropRangeFast];
            sbyte[] offsets = new sbyte[contexts.Length];
            sbyte[] multipliers = new sbyte[contexts.Length];
            foreach (int multiplier in new[] { 1, -1, 2, 3, 127, -128 })
            for (int mode = 0; mode < 5; mode++)
            {
                for (nuint y = 0; y < channel.H; y++)
                {
                    Span<int> row = channel.Row(y);
                    for (int x = 0; x < width; x++) row[x] = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                }
                for (int i = 0; i < contexts.Length; i++)
                {
                    contexts[i] = (byte)(i % 251);
                    offsets[i] = (sbyte)(i % 255 - 127);
                    multipliers[i] = (sbyte)multiplier;
                }
                WeightedHeader header = new WeightedHeader();
                header.SetPredictorMode(mode);
                List<Token> expected = Prefix(), actual = Prefix();
                Status baseline = Scalar(channel, header, contexts, offsets, multipliers, expected);
                Status result = Encode(channel, header, contexts, offsets, multipliers, actual);
                Assert.That(result.IsOk, Is.EqualTo(baseline.IsOk));
                Assert.That(actual, Is.EqualTo(expected));
                actual.Add(new Token(99, 123));
                Assert.That(actual.Count, Is.EqualTo(expected.Count + 1));
            }
        }

        /// <summary>A non-divisible residual at the last pixel keeps every preceding token.</summary>
        [Test]
        public void InvalidMultiplierKeepsWrittenPrefix()
        {
            using ModularImage image = ModularImage.Create(JxlMemoryManager.Default, 17, 3, 16, 1).Value;
            Channel channel = image.Channels[0];
            for (nuint y = 0; y < channel.H; y++) channel.Row(y).Clear();
            channel.Row(2)[16] = 1;
            byte[] contexts = new byte[2 * TreeLut.KPropRangeFast];
            sbyte[] offsets = new sbyte[contexts.Length], multipliers = new sbyte[contexts.Length];
            Array.Fill(multipliers, (sbyte)2);
            List<Token> expected = Prefix(), actual = Prefix();
            Assert.That(Scalar(channel, new WeightedHeader(), contexts, offsets, multipliers, expected).IsOk, Is.False);
            Assert.That(Encode(channel, new WeightedHeader(), contexts, offsets, multipliers, actual).IsOk, Is.False);
            Assert.That(actual.Count, Is.EqualTo(53));
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>Exceptions leave the list usable and remove its uninitialised reserved tail.</summary>
        [Test]
        public void DivisionByZeroKeepsOriginalPrefix()
        {
            using ModularImage image = ModularImage.Create(JxlMemoryManager.Default, 8, 2, 16, 1).Value;
            byte[] contexts = new byte[2 * TreeLut.KPropRangeFast];
            sbyte[] offsets = new sbyte[contexts.Length], multipliers = new sbyte[contexts.Length];
            List<Token> actual = Prefix();
            Assert.Throws<DivideByZeroException>(() => Encode(image.Channels[0], new WeightedHeader(), contexts, offsets, multipliers, actual));
            Assert.That(actual, Is.EqualTo(Prefix()));
            actual.Add(new Token(9, 10));
            Assert.That(actual.Count, Is.EqualTo(4));
        }

        /// <summary>Valid non-unit signed divisors retain the original symbols throughout a channel.</summary>
        [Test]
        public void MixedMultipliersEncodeEveryPixel()
        {
            using ModularImage image = ModularImage.Create(JxlMemoryManager.Default, 33, 9, 16, 1).Value;
            Channel channel = image.Channels[0];
            byte[] contexts = new byte[2 * TreeLut.KPropRangeFast];
            sbyte[] offsets = new sbyte[contexts.Length], multipliers = new sbyte[contexts.Length];
            sbyte[] choices = { 1, -1, 2, -3, 127, -128 };
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i] = (byte)(i % 251);
                offsets[i] = (sbyte)(i % 5 - 2);
                multipliers[i] = choices[i % choices.Length];
            }
            WeightedHeader header = new WeightedHeader();
            header.SetPredictorMode(1);
            WeightedState generator = new WeightedState(header, channel.W, channel.H);
            Span<int> properties = stackalloc int[1];
            Random random = new Random(123);
            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> row = channel.Row(y);
                ReadOnlySpan<int> top = y > 0 ? channel.ConstRow(y - 1) : row;
                ReadOnlySpan<int> top2 = y > 1 ? channel.ConstRow(y - 2) : top;
                for (int x = 0; x < (int)channel.W; x++)
                {
                    NeighborPixels neighbors = ModularPredictors.GatherNeighbors(row, top, top2, x, (int)y);
                    long pred = generator.Predict((nuint)x, y, channel.W, in neighbors, properties, 0, true);
                    int index = TreeLut.KPropRangeFast + Math.Clamp(properties[0], -TreeLut.KPropRangeFast, TreeLut.KPropRangeFast - 1);
                    row[x] = checked((int)(pred + offsets[index] + multipliers[index] * random.Next(-7, 8)));
                    generator.UpdateErrors(row[x], (nuint)x, y, channel.W);
                }
            }
            List<Token> expected = Prefix(), actual = Prefix();
            Assert.That(Scalar(channel, header, contexts, offsets, multipliers, expected).IsOk, Is.True);
            Assert.That(Encode(channel, header, contexts, offsets, multipliers, actual).IsOk, Is.True);
            Assert.That(actual.Count, Is.EqualTo(300));
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>Real Modular dispatch produces the same contexts and symbols as general tree traversal.</summary>
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(257)]
        public void ModularFastPathMatchesGeneralTree(int width)
        {
            using ModularImage image = ModularImage.Create(JxlMemoryManager.Default, (nuint)width, 9, 16, 3).Value;
            Random random = new Random(789);
            foreach (Channel channel in image.Channels)
            for (nuint y = 0; y < channel.H; y++)
            {
                Span<int> row = channel.Row(y);
                for (int x = 0; x < width; x++) row[x] = random.Next(-32768, 32768);
            }
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(ModularPredictors.KWPProp, 0, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Weighted, -3, 1),
                PropertyDecisionNode.Leaf(Predictor.Weighted, 7, 1),
            };
            for (int mode = 0; mode < 5; mode++)
            {
                ModularOptions options = ModularOptions.Default();
                options.Predictor = Predictor.Weighted;
                options.WpMode = mode;
                List<Token> actual = Prefix(), expected = Prefix();
                Assert.That(EncModularEncoding.ModularCompress(image, options, 0, tree, new GroupHeader(), actual, out _).IsOk, Is.True);
                options.SkipEncoderFastPath = true;
                Assert.That(EncModularEncoding.ModularCompress(image, options, 0, tree, new GroupHeader(), expected, out _).IsOk, Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        private static List<Token> Prefix() => new List<Token>
        {
            new Token(12, 34), new Token(56, 78, true), new Token(90, 123),
        };

        private static Status Scalar(Channel channel, WeightedHeader header, byte[] contexts, sbyte[] offsets, sbyte[] multipliers, List<Token> tokens)
        {
            WeightedState state = new WeightedState(header, channel.W, channel.H);
            Span<int> properties = stackalloc int[1];
            for (nuint y = 0; y < channel.H; y++)
            {
                ReadOnlySpan<int> row = channel.ConstRow(y);
                ReadOnlySpan<int> top = y > 0 ? channel.ConstRow(y - 1) : row;
                ReadOnlySpan<int> top2 = y > 1 ? channel.ConstRow(y - 2) : top;
                for (int x = 0; x < (int)channel.W; x++)
                {
                    NeighborPixels neighbors = ModularPredictors.GatherNeighbors(row, top, top2, x, (int)y);
                    long pred = state.Predict((nuint)x, y, channel.W, in neighbors, properties, 0, true);
                    int index = TreeLut.KPropRangeFast + Math.Clamp(properties[0], -TreeLut.KPropRangeFast, TreeLut.KPropRangeFast - 1);
                    long residual = row[x] - (pred + offsets[index]);
                    sbyte multiplier = multipliers[index];
                    if (residual % multiplier != 0) return Status.Failure("Invalid multiplier");
                    tokens.Add(new Token(contexts[index], PackSigned.Pack((int)(residual / multiplier))));
                    state.UpdateErrors(row[x], (nuint)x, y, channel.W);
                }
            }
            return true;
        }
    }
}
