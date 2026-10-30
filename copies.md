# Removing redundant facade copies

Follow-up to `memory-threads.md`, 2026-10-30. Windows x64, Intel Core i5-1340P,
.NET 10.0.14, Release, `DOTNET_TieredCompilation=0`.

## Implemented

### Simple span encoding

`Encode(ReadOnlySpan<byte>, ...)` and `EncodeLossless(ReadOnlySpan<byte>, ...)`
already create a private input snapshot through `JxlImageData.FromPixels`.
Previously `ToPackedPixelFile` then allocated and copied that snapshot again.
The private snapshot now becomes the packed frame's storage, without another
full pixel allocation or copy. Frame metadata and configuration are unchanged.

`Encode(JxlImageData, ...)` still makes its independent packed snapshot. Public
`FromPixels`, `FromPackedPixelFile` and `ToPackedPixelFile` keep their copying
behavior. Caller-owned image buffers are not borrowed by this optimization.

An internal `PackedImage.CreateWithPixels` route shares the normal dimension,
stride and overflow checks, and rejects supplied storage that is too short.
Only internal callers with privately owned storage use it.

### Simple decoding and format conversion

Simple `Decode(span)`, `Decode(stream)` and `DecodeFile` return the newly decoded
primary pixel storage directly. Their intermediate PackedPixelFile is private
to the operation and is never exposed as a second mutable view.

`DecodeToFormat` skips construction of PrimaryImage altogether: the target
format encoder already consumes PixelFile. It also avoids cloning ICC and
metadata solely for that unused PrimaryImage.

Extended decode and the public JxlDecodeResult constructor still eagerly create
an independent PrimaryImage snapshot. Modifying it does not modify PixelFile,
and modifying PixelFile does not modify it. No public overloads were added.

### Compressed input

Extras now has an internal synchronous ReadOnlyMemory decode route. The public
span route still takes a snapshot. The facade takes one span snapshot and reuses
it for JPEG reconstruction probing and color-space fallback, instead of making
another full compressed copy for each attempt.

File and stream decode already own their input arrays; those arrays go directly
to the memory route. This avoids the second compressed-input allocation.
MemoryStream snapshot/position behavior is unchanged: its logical contents are
copied through ToArray, as before. This is not incremental streaming or borrowing
the caller's mutable MemoryStream storage. The decoder is disposed before the
synchronous operation releases its private input.

## Measurements

Same deterministic RGB8 pattern as `memory-threads.md`, lossless, Effort=3,
three warmups and five measured calls per route. Managed allocation includes
all process threads; MB = 1,000,000 bytes and is allocation traffic, not live or
peak memory. Simple APIs use their default automatic threading. Extended API
measurements request one thread and RGB8 output. The reconstruction-enabled
case probes ordinary pixel codestreams without a reconstructable JPEG.

Baseline binaries include the earlier memory/thread changes and were frozen
before this follow-up. No builds or tests ran concurrently with these samples.

| 1024x1024 workload | Allocated MB before -> after | Median ms before -> after |
|---|---:|---:|
| Simple span lossless encode | 135.67 -> 132.54 | 52.08 -> 53.14 |
| JxlImageData encode, retained snapshot | 98.95 -> 98.95 | 144.59 -> 142.01 |
| Simple span decode | 51.48 -> 48.32 | 29.83 -> 27.51 |
| Simple MemoryStream decode | 52.63 -> 48.31 | 29.06 -> 27.32 |
| Extended span decode, retained snapshot | 37.78 -> 37.78 | 126.34 -> 128.07 |
| Extended MemoryStream decode | 38.95 -> 37.78 | 127.77 -> 129.22 |
| Extended span decode, JPEG probe enabled | 40.69 -> 39.53 | 126.27 -> 129.74 |
| DecodeToFormat PPM, JPEG probe enabled | 43.85 -> 39.53 | 126.47 -> 126.29 |

One RGB8 1024x1024 pixel copy is 3,145,728 bytes. The compressed input for this
case is 1,165,940 bytes. Removing both saves about 4.31 MB plus array headers.
The measured reductions agree with these sizes; parallel scheduling and pool
hits introduce some noise. Time savings are small and variable, and several
individual routes are slightly slower in this sample. The confirmed outcome is
less allocation/copy traffic, not a universal throughput increase.

At 256x256, simple span decode decreased from 2.733 to 2.536 MB, simple stream
decode from 2.806 to 2.538 MB, and format conversion from 3.269 to 2.999 MB.

Local ignored reproduction artifacts are under `artifacts/profiles/copies/`:
`harness/`, `baseline-bin/`, `before-{256,1024}.txt`,
`after-{256,1024}.txt`, and baseline/current output files. Example:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/copies/baseline-bin/JxlSharp.Integration.Tests.dll 1024 baseline
dotnet artifacts/profiles/copies/harness/bin/Release/net10.0/JxlSharp.Integration.Tests.dll 1024 current
```

## Validation and limits

- All 16 harness outputs at both sizes match the frozen baseline byte for byte:
  compressed lossless outputs, decoded pixels and PPM output.
- 26 selected integration/model tests pass, including new ownership tests for
  four sample storage types, grayscale/RGB and alpha, lossless and lossy path
  parity, slice offsets, malformed input, MemoryStream logical bounds, JXL/PPM
  file decode, JPEG probing and color-space fallback.
- Extras: 32 pass; 6 fail when ReadTestData cannot find external fixture files
  (`EncodeToPNG`, `LosslessPNMRoundtrip`, `JxlEncodeAndDecodeRoundtrip`,
  compressed ICC `Roundtrip`, and two ICC-enabled `GainMapRoundtrip` cases).
  Those failures occur at the fixture loader before codec processing. They
  prevent a claim that the whole Extras suite is green.
- Real JPEG reconstruction, externally supplied test images, animation and
  peak-memory profiling were not covered by this follow-up's synthetic tests.

Remaining targets: per-thread transform scratch sizes and stage-specific worker
limits; concurrent codec operations and oversubscription; the deliberate public
snapshot copies in extended results and JxlImageData encoding; GetInfo's full
input snapshot; and real incremental stream input/output. Removing the public
snapshots requires an explicit ownership policy rather than changing existing
mutable-view behavior.

Follow-up: [scratch-threads.md](scratch-threads.md) implements actual-map encoder
transform scratch sizes and limits default facade worker slots by task count.
Concurrent operations and the other remaining targets are still open.
