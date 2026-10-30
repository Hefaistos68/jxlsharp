using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
internal static class ScalarEntropyReference {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static nuint WritePrefixTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer)
        {
            nuint numExtraBits = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                Token token          = tokens[i];
                int histo            = codes.ContextMap[(int)contextOffset + (int)token.Context];
                HybridUintConfig cfg = token.IsLz77Length
                    ? codes.Lz77.LengthUintConfig
                    : codes.UintConfig[histo];
                cfg.Encode(token.Value, out uint tok, out uint nbits, out uint bits);

                if (token.IsLz77Length)
                {
                    tok += codes.Lz77.MinSymbol;
                }

                ANSEncSymbolInfo info = codes.EncodingInfo[histo][tok];
                ulong data            = info.Bits | ((ulong)bits << info.Depth);
                writer.Write(info.Depth + nbits, data);
                numExtraBits += nbits;
            }

            return numExtraBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static nuint WriteAnsTokens(
            IReadOnlyList<Token> tokens,
            EntropyEncodingData codes,
            nuint contextOffset,
            BitWriter writer)
        {
            nuint numExtraBits = 0;
            int maxWords       = (tokens.Count * 2) + 1;
            ulong[] outWords   = ArrayPool<ulong>.Shared.Rent(maxWords);
            byte[] outNbits    = ArrayPool<byte>.Shared.Rent(maxWords);
            int outCount       = 0;

            try
            {
                ulong allbits    = 0;
                nuint numallbits = 0;
                ANSCoder ans     = ANSCoder.Create();
                bool multiOrLz77 = codes.Lz77.Enabled || codes.ContextMap.Length > 1;

                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    Token token = tokens[i];
                    int histo   = multiOrLz77 ? codes.ContextMap[(int)contextOffset + (int)token.Context] : 0;
                    HybridUintConfig cfg = multiOrLz77 && token.IsLz77Length
                        ? codes.Lz77.LengthUintConfig
                        : codes.UintConfig[histo];
                    cfg.Encode(token.Value, out uint tok, out uint nbits, out uint bits);

                    if (multiOrLz77 && token.IsLz77Length)
                    {
                        tok += codes.Lz77.MinSymbol;
                    }

                    ANSEncSymbolInfo info = codes.EncodingInfo[histo][tok];
                    AddReversedBits(bits, nbits, outWords, outNbits, ref outCount, ref allbits, ref numallbits);
                    numExtraBits += nbits;
                    uint ansBits  = ans.PutSymbol(info, out byte ansNbits);
                    AddReversedBits(ansBits, ansNbits, outWords, outNbits, ref outCount, ref allbits, ref numallbits);
                }

                writer.Write(32, ans.GetState());
                writer.Write(numallbits, allbits);

                for (int i = outCount - 1; i >= 0; i--)
                {
                    writer.Write(outNbits[i], outWords[i]);
                }

                return numExtraBits;
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(outWords);
                ArrayPool<byte>.Shared.Return(outNbits);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddReversedBits(
            ulong bits,
            nuint nbits,
            ulong[] outWords,
            byte[] outNbits,
            ref int outCount,
            ref ulong allbits,
            ref nuint numallbits)
        {
            if (nbits == 0)
            {
                return;
            }

            if (numallbits + nbits > BitWriter.KMaxBitsPerCall)
            {
                outWords[outCount] = allbits;
                outNbits[outCount] = (byte)numallbits;
                outCount++;
                numallbits = 0;
                allbits    = 0;
            }

            allbits     = (allbits << (int)nbits) | bits;
            numallbits += nbits;
        }
}