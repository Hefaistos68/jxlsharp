# ANS, Huffman and bit I/O investigation

Measured on 2026-10-29: Windows x64, Intel Core i5-1340P, .NET 10.0.14,
Release JIT, one thread, `DOTNET_TieredCompilation=0`.

## Changes retained

`EncAns.WriteTokens` selects a contiguous token source once for ordinary
`List<Token>` and `Token[]` inputs. The generic span specialization eliminates
interface indexing in the hot loops without an extra selection branch per
token. Other `IReadOnlyList<Token>` implementations retain interface semantics,
including derived lists that implement their own indexer.

ANS reverse output now uses one `ulong` per chunk: 56 data bits and an 8-bit
length. The separate byte array of lengths is gone. At most one chunk can be
flushed per token: a valid hybrid uint contributes at most 31 extra bits and
ANS contributes either 0 or 16 renormalization bits. After either flush, these
two pieces fit together in a 56-bit chunk. Therefore N words suffice instead
of the previous 2N+1 words and 2N+1 length bytes.

For 262,144 tokens, the measured shared-pool rentals fell from **9 MiB to
2 MiB** (9,437,184 -> 2,097,152 bytes). This is working-buffer capacity, not
allocation on every warmed-up call. The exact reduction depends on input size
and pool bucket rounding; logical requested capacity drops from roughly 18N
to 8N bytes.

`BitWriter.AppendUnaligned` copies byte-aligned streams with `Span.CopyTo`.
Otherwise it transfers seven bytes per 56-bit write, retaining byte-wise and
partial-bit tails. Loads stay within the source's logical bytes. Source spans
are obtained after reservation, so self-appending remains safe when the buffer
reallocates. The aligned path also clears the next output byte for subsequent
bit writes and keeps the source owner alive across the copy.

No hand-written AVX2 kernel was added: entropy states and bit positions are
serial; aligned bulk copying uses the runtime's optimized memory-copy path.

## Measurements

These are **isolated entropy and copy benchmarks**, not whole-codec throughput.
Token input: 262,144 seeded tokens, four contexts, mostly values 0..15 with
every seventh value sampled from 0..999. Histogram construction is outside the
timer. The old encoder loops are compiled alongside the updated library.
Output bytes and bit counts are compared before timing.

Both implementations reuse preallocated output storage, avoiding output-array
copies and native buffer allocation inside the timed loops. Each result is the
median of nine batches of twenty calls; execution order alternates by batch.
The three repeated runs below each start a fresh process.

| Operation | Original ms, runs 1/2/3 | Updated ms, runs 1/2/3 | Speedup range |
| --- | --- | --- | --- |
| ANS token write | 2.599 / 2.789 / 2.490 | 2.517 / 2.713 / 2.425 | 1.027–1.033x |
| Huffman token write | 1.982 / 1.905 / 1.943 | 1.710 / 1.660 / 1.693 | 1.148–1.159x |
| Append 1 MiB, destination offset 3 bits | 2.990 / 3.009 / 2.861 | 0.488 / 0.502 / 0.462 | 5.99–6.19x |
| Append 1 MiB, byte-aligned | 3.174 / 2.960 / 2.928 | 0.053 / 0.041 / 0.044 | 59.5–71.5x |

Large-block copy ratios should not be applied to tiny histogram fragments.
The ANS speed gain is small; its main retained benefit is the smaller working
buffer. Repeated measurements are in
`artifacts/profiles/entropy/repeated-final-results.txt`.

## Decoder and profiling findings

- The ANS alias table is already compact (8 bytes per entry), and lookup and
  renormalization already use conditional selection. ANS state updates depend
  on the preceding symbol; independent SIMD lanes would require independent
  streams, not just batching symbols from the existing codestream.
- Huffman decoding already uses a root lookup with an optional second level.
  Removing that second-level branch would add unnecessary work to short codes.
- A measured prototype refilled only when fewer than 48 bits remained before
  a hybrid symbol (enough for 16 code/renormalization bits plus 31 extra bits).
  It regressed this decoder workload, particularly Huffman. It was removed;
  `BitReader`, `ANSSymbolReader` and Huffman decoding remain unchanged.
- The decoder sampled-thread-time trace attributed about 97% exclusively to
  the enclosing decode loop because the entropy primitives were inlined.
  This does not establish individual costs for refill, lookup or renormalization.
- The initial allocation-heavy encoder harness showed substantial GC/finalizer
  activity. Final kernel timing therefore reuses storage and excludes result
  copies. Such timing does not measure the codec's allocation/GC overhead.
- Final JIT output confirms a `SpanTokenInput` specialization of the Huffman
  write loop without token `IReadOnlyList` indexer/count calls.

## Validation

- Full decoder suite: 291 passed. Full encoder suite: 165 passed.
- Four `JxlSharpCodecTests` integration tests passed.
- All 24 new test cases also passed with `DOTNET_EnableAVX2=0`.
- Bit-copy tests cover all source/destination bit alignments, byte lengths
  around 7/8/14/15/56/57, empty sources, large sources, self-appending,
  reallocation, unchanged source data and subsequent bit writes. Expected
  output is constructed independently one bit at a time.
- Entropy tests compare exact bytes, bit counts and extra-bit counts with the
  frozen original loops. They cover list/array/custom collection inputs,
  overridden derived-list indexers, context offsets, empty/short streams,
  pool-boundary sizes, uint.MaxValue, 31 extra bits, forced ANS/Huffman and
  actual LZ77 length/distance tokens. Full streams are decoded and checked
  against original values and the ANS final-state signature.

The regression tests and scalar reference are outside the ignored artifacts
directory and can be added to Git with the implementation changes.

## Reproduction

Local harness, frozen pre-change sources, traces, JIT assembly, timing logs and
TRX results are in `artifacts/profiles/entropy/`, which is ignored by Git.
Preserve that folder to reproduce the exact benchmark in another checkout;
its project references the local DLLs under `D:/Work/jxlsharp`.

```powershell
dotnet build src/JxlSharp.Encoder/JxlSharp.Encoder.csproj -c Release --source https://api.nuget.org/v3/index.json
dotnet build artifacts/profiles/entropy/harness/perf.csproj -c Release --source https://api.nuget.org/v3/index.json
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/entropy/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll
dotnet test tests/JxlSharp.Decoder.Tests/JxlSharp.Decoder.Tests.csproj -c Release --no-restore
dotnet test tests/JxlSharp.Encoder.Tests/JxlSharp.Encoder.Tests.csproj -c Release --no-restore
```

Remaining candidates require fresh full-codec profiles: avoiding repeated
construction of small temporary writers, allocation/finalization costs, the
ANS reverse-map working set with many histograms, and batching across existing
independent streams. No ANS/Huffman decoder speedup or full-codec speedup is
claimed by this investigation.
