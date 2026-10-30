# Float16, grayscale and uncommon external pixel formats

Investigation and measurements: 2026-10-29, Windows x64, Intel Core i5-1340P,
.NET 10.0.14, Release JIT, AVX2, one thread, `DOTNET_TieredCompilation=0`.

## Implemented paths

- Input: eight-pixel Gray/Gray+Alpha conversion for uint8, uint16, float16 and
  float32; RGB/RGBA float16 conversion. Only the requested half channel is
  converted after deinterleaving its raw 16-bit encodings.
- Output: Gray/Gray+Alpha packing for common integer and float formats;
  float16 conversion and packing for all one-to-four-channel layouts.
- Integer output: fused quantization and packing for nominal depths 17..30,
  including three-byte 17..24-bit output in both endian orders.
- Native-endian single-channel float32 uses span copying, preserving raw float
  bits without per-pixel arithmetic.
- These output paths write directly to the destination instead of allocating a
  temporary ushort/uint array for every scanline. Short rows and SIMD tails use
  scalar stores without row temporaries. Input rows shorter than 16 pixels keep
  the compact scalar path.
- Existing RGB/RGBA uint8/uint16/float32 kernels remain separate: putting all
  formats in one large kernel initially caused regressions. The first Gray8
  load using a Vector64-to-Vector128 conversion also regressed; a scalar 64-bit
  load into Vector128 removed that issue.

The half kernels use AVX2 integer operations rather than depending on an F16C
API. Half-to-float subnormals are converted as exact multiples of 2^-24.
Float-to-half uses integer round-to-nearest, ties-to-even arithmetic, including
subnormals, and therefore does not depend on a floating-point rounding trick.
NaN sign/payload, NaN quieting, infinities and signed zeros match the existing
.NET scalar casts exactly.

Integer output at 31/32 bits retains the old path. Extending SIMD there needs
separate handling of unsigned conversion and saturation near 2^31 and 2^32.
The scalar conversion semantics were preserved and are included in the tests.

## Measurements

These measure external pixel conversion, **not JPEG XL encoding/decoding**.
Each call processes 4096 x 4 pixels through the public conversion API. Input
imports all channels; output exports all planes. Histogram/codec work is absent.
Row stride includes five padding bytes. Timed cases use little-endian buffers.

The baseline is a frozen copy of the pre-change input/output classes and their
previous packing helpers, compiled alongside the updated DLLs. Input uses seeded
random bytes, including arbitrary half encodings. Output uses values in [0,1],
with selected NaNs and small subnormal-half values. Output bytes and imported
float bits are compared before timing.

After warmup, execution order alternates between implementations. Results are
medians of seven batches of 500 calls. Allocations are per public API call,
measured with `GC.GetAllocatedBytesForCurrentThread` outside timing.

| Format | Import original -> updated, ms | Speedup | Export original -> updated, ms | Speedup |
| --- | --- | ---: | --- | ---: |
| Gray uint8 | 0.0092 -> 0.0031 | 2.96x | 0.0118 -> 0.0068 | 1.75x |
| Gray+Alpha uint8 | 0.0178 -> 0.0058 | 3.05x | 0.0217 -> 0.0102 | 2.12x |
| Gray uint16 | 0.0134 -> 0.0034 | 3.92x | 0.0159 -> 0.0064 | 2.48x |
| Gray+Alpha uint16 | 0.0263 -> 0.0097 | 2.71x | 0.0287 -> 0.0102 | 2.80x |
| Gray float32 | 0.0096 -> 0.0013 | 7.48x | 0.0204 -> 0.0013 | 15.70x |
| Gray+Alpha float32 | 0.0195 -> 0.0095 | 2.05x | 0.0390 -> 0.0057 | 6.78x |
| Gray float16 | 0.0305 -> 0.0060 | 5.07x | 0.0468 -> 0.0223 | 2.10x |
| Gray+Alpha float16 | 0.0611 -> 0.0156 | 3.91x | 0.0972 -> 0.0420 | 2.31x |
| RGB float16 | 0.0994 -> 0.0302 | 3.29x | 0.1466 -> 0.0583 | 2.52x |
| RGBA float16 | 0.1302 -> 0.0447 | 2.91x | 0.1915 -> 0.0574 | 3.34x |
| Gray uint24 | — | — | 0.0395 -> 0.0076 | 5.20x |
| RGBA uint24 | — | — | 0.1466 -> 0.0208 | 7.05x |
| RGBA uint30 | — | — | 0.1324 -> 0.0156 | 8.46x |

Control cases: RGB uint8 import/export 1.06x/0.98x; RGBA float32
import/export 1.02x/1.01x. Absolute timings vary between runs on this mobile CPU;
the controls do not establish an additional speedup for already optimized formats.

For this four-row fixture, managed output allocations fell from 33,288 to
424 bytes for float16, and from 66,056 to 424 bytes for grayscale/rare integer
paths. The remaining allocation is API setup, not a scanline temporary. Imports
allocate no managed memory per call in either implementation. Existing common
packed output remains at 424 bytes per call.

## Validation

- Full Base suite: 90 passed; decoder: 307 passed; encoder: 170 passed.
- Four `JxlSharpCodecTests` integration tests passed: 571 tests total.
- All 34 selected external-input/output tests also passed with AVX2 disabled.
- Every one of the 65,536 binary16 encodings is checked against the scalar
  half-to-float cast, comparing raw float32 bits.
- Float-to-half is checked at every finite positive half rounding boundary,
  immediately above/below it, with both signs, plus 400,000 random float32
  encodings. This covers tie-to-even rounding, underflow, overflow and NaNs.
- Public input tests cover all channel counts, both byte orders, nominal uint8
  depths 1/7/8 and uint16 depths 9/10/12/16, half/float payloads, row padding,
  and widths around both the eight-pixel kernel and 16-pixel input dispatch.
- Output tests compare complete byte buffers with independent scalar casts and
  explicit byte packing, including row-padding/buffer sentinels. Widths include
  1/7/8/9/15/16/17/31/32/33/257; depths include half16 and integer
  1/7/17/24/25/30/31/32. Public FloatToF16 also checks zero count and an entire
  65,536-value row with surrounding sentinels.

## Reproduction and scope

Local benchmark sources, frozen baselines, intermediate/final results and TRX
files are under `artifacts/profiles/rare-pixels/` (ignored by Git). Preserve the
folder to reproduce these exact old/new measurements in another checkout. Its
project references the local Release DLLs under `D:/Work/jxlsharp`.

```powershell
dotnet build src/JxlSharp.Encoder/JxlSharp.Encoder.csproj -c Release --source https://api.nuget.org/v3/index.json
dotnet build artifacts/profiles/rare-pixels/harness/perf.csproj -c Release --source https://api.nuget.org/v3/index.json
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/rare-pixels/harness/bin/Release/net10.0/JxlSharp.Encoder.Tests.dll
```

The regression tests and this report are outside the ignored directory. No
whole-codec throughput gain is inferred from these cached conversion fixtures.
