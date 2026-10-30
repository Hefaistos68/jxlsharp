# Modular weighted predictor investigation

Measured on 2026-10-29, Windows x64, Intel Core i5-1340P, .NET 10.0.14,
Release JIT, AVX2 enabled, one thread, `DOTNET_TieredCompilation=0`.

## Changes retained

- `WeightedState.Predict` stores the four sub-predictions in a `Vector256<long>`
  on AVX2 machines. `UpdateErrors` calculates four differences, absolute values,
  rounding biases and shifts together. Error arrays retain their existing layout.
- If all four differences are zero, clear the recycled row entries and omit the
  additions of zero to the NE error accumulators. This removed the regression
  initially observed on flat data.
- Retain the original scalar path when AVX2 is unavailable. Preserve the
  `Math.Abs(long.MinValue)` exception and integer wrapping/rounding semantics.
- In the encoder's WP LUT loop, bypass division for multiplier 1. For other
  multipliers, calculate one quotient and validate `quotient * multiplier == residual`.
  The previous remainder followed by division generated two `idiv` instructions.
- Reserve the token list once and write through a span. A `finally` block restores
  the exact appended count, including partial results on failure or exception.

## Measurements

These measure **WP `ModularCompress`, not full image encoding or decoding**.
Input: three 1024 x 512 integer planes, nominal depth 16, seeded synthetic data.
The tree splits on `ModularPredictors.KWPProp`, with weighted leaves, offsets -3
and 7, multiplier 1. Presets 0 and 1 are measured separately.

Baseline: a frozen copy of the original Modular encoder dispatch and original
scalar `WeightedState`, compiled into the same executable. Each implementation
reuses its token list. Image generation is outside timing; LUT construction,
predictor-state allocation and token generation are included. After warmup,
report the median of nine batches of three calls, alternating execution order.
Every call produces 1,572,864 tokens.

| Input | Preset | Original, ms | Updated, ms | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Smooth with low-amplitude noise | 0 | 72.716 | 62.335 | 1.167x |
| Smooth with low-amplitude noise | 1 | 71.884 | 63.379 | 1.134x |
| Random signed 16-bit | 0 | 123.103 | 107.236 | 1.148x |
| Random signed 16-bit | 1 | 120.877 | 104.826 | 1.153x |
| All zero | 0 | 73.407 | 69.445 | 1.057x |
| All zero | 1 | 75.337 | 72.158 | 1.044x |

An additional zero-input fixture with leaf offsets 0 and multiplier 2 measured
36.762 -> 33.319 ms (preset 0, 1.103x) and 36.381 -> 32.337 ms (preset 1,
1.125x). This fixture checks the non-unit-divisor path; it is not representative
of general image content. Compare implementations within each run, not absolute
timings between different runs or fixtures on this mobile CPU.

## LUT and arithmetic findings

- The reciprocal LUT is already 64 unsigned integers (256 bytes). `ErrorWeight`
  uses scalar log2 and a reciprocal lookup instead of integer division.
- A prototype packing context, signed offset and signed multiplier into one
  `uint` LUT showed only small gains in isolated runs. It also required a new
  64 KiB table per channel instead of the existing three 16 KiB tables. No
  additional LUT conversion/allocation was retained.
- Replacing the reciprocal array with an RVA-backed constant span did not
  establish a useful advantage. The original reciprocal values and lookup
  arithmetic remain unchanged.
- The four prediction formulas and weighted sum still use scalar 64-bit
  multiplication. AVX2 does not provide packed 64 x 64 integer multiplication;
  narrowing these values to 32 bits would be incorrect for full-range Modular
  samples. No arithmetic approximation was introduced.
- A gather-based weight implementation was not added: it would still need four
  scalar log2 operations (or an additional vector log2 algorithm), plus lane
  assembly from the existing four separate error arrays. This remains an
  unmeasured possibility, not a demonstrated improvement.
- Adjacent pixels remain serial. `Predict` reads the signed error of the
  previous pixel, and `UpdateErrors` modifies previous-row accumulators used by
  subsequent pixels. Processing neighbouring pixels independently would change
  predictions and the encoded stream.

## Validation

- Full decoder suite: 273 passed. Full encoder suite: 159 passed.
- Twenty new tests also passed with `DOTNET_EnableAVX2=0`.
- Frozen-baseline versus updated `ModularCompress`: identical token streams in
  360 cases, both with AVX2 enabled and disabled. Cases cover widths
  1/2/7/8/17/257, depths 8/16/32, all five presets, flat/smooth/random/extreme
  inputs, three channels and multiple rows.
- Decoder tests independently calculate the scalar predictions and all four
  updated error lanes, including uint wrapping, property selection, row borders,
  recycled entries, full-range signed pixels and absolute-value overflow.
- Encoder tests cover positive/negative/mixed divisors, offsets, contexts,
  pre-existing tokens, non-divisible residuals, division-by-zero exceptions and
  equivalence with general MA-tree traversal.
- Final JIT disassembly contains `vpsubq`, `vptest`, `vpcmpgtq`, `vpaddq` and
  `vpsrlq` for the error lanes, and one `idiv` in the non-unit-divisor branch.

## Reproduction

The local investigation files are in `artifacts/profiles/weighted/` (ignored by
Git): frozen baseline sources and experimental kernels in `harness/`, raw timing
results, `wp-final-jit.asm`, and TRX files in `tests/`. The report and regression
tests are outside that ignored directory; preserving or copying the harness is necessary to
reproduce the exact old/new benchmark in a fresh checkout. The harness project
references the local Release DLLs under `D:/Work/jxlsharp`.

Run from the repository root in PowerShell:

```powershell
dotnet build src/JxlSharp.Encoder/JxlSharp.Encoder.csproj -c Release --source https://api.nuget.org/v3/index.json
dotnet build artifacts/profiles/weighted/harness/perf.csproj -c Release --source https://api.nuget.org/v3/index.json
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/weighted/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll validate
dotnet artifacts/profiles/weighted/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll
dotnet artifacts/profiles/weighted/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll nonunit
$env:DOTNET_EnableAVX2 = '0'
dotnet artifacts/profiles/weighted/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll validate
```

No full-codec throughput claim or comparison with C++ follows from these results.
Decoder callers share the improved state update, but decoder throughput was not
benchmarked in this investigation.
