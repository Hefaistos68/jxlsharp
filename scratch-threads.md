# Transform scratch sizes and task-limited worker slots

Follow-up to `memory-threads.md` and `copies.md`, 2026-10-30. Windows x64,
Intel Core i5-1340P, .NET 10.0.14, Release, AVX2. Measurements use
`DOTNET_TieredCompilation=0` for both versions.

## Retained changes

### Encoder DC and AC scratch

DC extraction previously allocated five maximum coefficient areas per worker,
even when the actual map used only small transforms. It now scans the frame's
AC strategy map once and allocates for its largest actual transform. The
per-worker coefficient and transform spans use that same size and stride.

AC coefficient calculation previously rented eight maximum float areas and
three maximum integer areas for every group. It now scans that group's map
and rents for its largest actual transform. The existing finally returns both
rentals. The transform, quantization and coefficient layout are unchanged.

Both scans inspect first blocks and use CoveredBlocksX * CoveredBlocksY * 64,
with a minimum of 64 coefficients. They read the actual map rather than relying
on UsedAcs, which may not yet have been updated when public encoder stages are
called separately. A group is bounded by the existing strategy/group rules.

For an all-8x8 map, DC scratch drops from 1,310,720 to 1,280 bytes per worker,
excluding the array header. AC pool requests drop from 524,288 to 512 floats
and from 196,608 to 192 ints. Shared-pool bucket rounding means approximately
3 MiB versus 3 KiB of rented storage per simultaneously processed group in this
case. A group containing a 256x256 transform still gets the old maximum size.
These are workspace sizes, not necessarily new GC allocations on every call.

Decoder GroupDecCache already sizes transform scratch from its used-strategy
mask and grows it as needed. It was inspected and left unchanged.

### Default facade worker slots

The default facade uses an internal fixed-runner entry point which initializes
min(configured workers, task count) slots for a nonempty range. Worker IDs are
restricted to that prefix, and guided chunk sizes use the active count. This
keeps per-thread arrays correctly sized when a stage has fewer tasks than
configured workers. Single-task facade ranges retain the earlier inline path.

The public ThreadParallelRunner.Runner still initializes its configured thread
count. NumThreads, RunOnEachThread and explicitly supplied user runners keep
their previous contracts. A fixed pool can alternate limited and unrestricted
ranges without changing its OS thread capacity.

This change limits participation and scratch slots, not OS thread creation.
All existing fixed-pool threads still pass through the command barrier; idle
workers return without taking tasks. Avoiding those wake-ups needs a different
scheduler and is not claimed here. Global concurrency limits were not added.

## Whole-codec measurements

Frozen baseline includes both earlier follow-ups. Same deterministic RGB8
pattern as the earlier reports, sizes 256/512/1024 square, lossless and lossy
Distance=1, Effort=3, threads 1/2/4/auto, RGB8 decode with JPEG reconstruction
disabled. Three warmup encode/decode pairs and five separately timed calls per
operation. Runner setup/disposal is inside the API measurement. Managed byte
totals include all process threads. No tests/builds ran concurrently with the
timed baseline/current runs.

Selected observations; MB = 1,000,000 allocated bytes per call, not peak/live
memory. Auto was 16 configured workers on this machine.

| Operation | Size | Threads | Allocated MB before -> after | Median ms before -> after |
|---|---:|---:|---:|---:|
| Lossy encode | 256 | 1 | 15.57 -> 14.26 | 27.20 -> 26.57 |
| Lossy encode | 256 | auto | 35.50 -> 14.54 | 26.86 -> 25.17 |
| Lossy encode | 512 | 2 | 40.65 -> 31.74 | 50.80 -> 48.29 |
| Lossy encode | 512 | auto | 67.13 -> 33.27 | 43.72 -> 41.84 |
| Lossy encode | 1024 | 1 | 90.73 -> 89.42 | 180.52 -> 184.23 |
| Lossy encode | 1024 | 4 | 109.70 -> 91.90 | 89.57 -> 89.39 |
| Lossy encode | 1024 | auto | 169.86 -> 99.14 | 74.86 -> 76.38 |
| Lossy decode | 512 | auto | 8.53 -> 8.43 | 11.44 -> 7.41 |
| Lossless encode | 1024 | auto | 132.59 -> 132.57 | 52.15 -> 52.50 |

The main confirmed improvement is allocation/workspace reduction in lossy
encoding. Allocation fell by about 59% at 256 and 42% at 1024 with automatic
threads. Wall times are noisy and some encode cases are slightly slower; this
is not evidence of a universal speedup. The 512 decode observation is a single
small workload, not a general decoder-throughput claim. Lossless encoding
benefits little because it does not execute the VarDCT scratch stages.

No allocation-stack trace or peak working-set measurement was taken. Shared
pool hit rates and scheduling can change allocation totals. The map scans add
work proportional to the number of 8x8 blocks; photographic corpora and larger
images should be measured before drawing broad throughput conclusions.

Ignored local artifacts: `artifacts/profiles/scratch/` contains `harness/`,
`baseline-bin/`, `before-{256,512,1024}.txt`, `after-{256,512,1024}.txt`, and
baseline/current encoded/pixel outputs. The baseline binaries were frozen
before these changes. Example reproduction:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/scratch/baseline-bin/JxlSharp.Integration.Tests.dll 512 baseline
dotnet artifacts/profiles/scratch/harness/bin/Release/net10.0/JxlSharp.Integration.Tests.dll 512 current
```

`validation/` is a separate output-parity harness: 257x129 flat, smooth and
deterministic noise images, effort 3/7/9, threads 1/auto. Its baseline program
uses the frozen library DLLs in `validation-baseline-bin/`; the current program
uses the updated ones.

## Validation

- Encoder: 172 passed; Threads: 12; Decoder: 307; selected self-contained
  integration/model tests: 26. **517 passed** in this follow-up.
- Both new transform scratch tests also pass with DOTNET_EnableAVX2=0.
- All 27 AC strategies are checked for sizing on full and offset rectangles,
  including the largest 256x256 and rectangular transforms. Tight forward
  transform and DC extraction spans match maximum-size buffers; tight scratch
  starts filled with NaNs to reveal accidental dependence on prior contents.
- Limited-runner stress checks alternate ranges of 0/1/2/3/5/8/17/257 tasks with
  0/1/8 configured workers, validate initialized slot counts, exactly-once
  execution and worker ID bounds, then reuse the public runner and RunOnEachThread.
  Initialization failure is also followed by successful reuse.
- Main frozen-baseline comparison: all **48 files** match byte for byte across
  three sizes, both coding modes and all four thread settings.
- Effort/content validation: all **36 files** match byte for byte. Combined:
  **84 compressed/pixel files** unchanged. Lossless pixels also match the input.
- External-fixture-dependent integration tests and photographic image corpora
  were not run in this follow-up.

## Next work

Measure 1/2/4 simultaneous codec operations with explicit per-image thread
limits. Each operation still owns its runner, so automatic threading can
oversubscribe the process. A bounded runner policy should be based on throughput,
latency and peak live memory across that workload; no process-wide semaphore
or shared mutable runner was introduced speculatively.

This follow-up is now measured in [concurrency.md](concurrency.md), with a tracked
benchmark project and a bounded-batch usage example. The measured best split
depends on coding mode; no universal automatic cap was introduced.

Other remaining costs include fixed-worker wake-ups, non-transform scratch,
deliberate public snapshot copies and full stream buffering. Reusing more
scratch across frames needs established initialization, padding, custom allocator
and lifetime rules.
