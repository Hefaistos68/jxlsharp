# Simultaneous codec operations and worker budgets

Investigation: 2026-10-30, Windows x64, Intel Core i5-1340P,
.NET 10.0.14, 16 logical processors, default workstation GC/Interactive latency,
Release, AVX2, `DOTNET_TieredCompilation=0`. This follows `scratch-threads.md`.

## Outcome

There is no single best split between concurrent images and workers per image
in this workload. One image benefits from automatic threading. With four images
in flight, lossy decoding consistently benefits from limiting each operation to
four workers. Lossless operations retain a small throughput advantage from auto
in some comparisons. Lossy encoding is noisy: limiting workers reduces CPU and
allocation costs, but the throughput advantage is not consistent across trials.

The existing NumThreads option is sufficient to express the useful limits.
Library automatic defaults and scheduling policy are unchanged in this stage.
Applications should bound outer concurrency and tune NumThreads together for
their workload. No global semaphore or shared runner was added: either would
need to account for other application work, latency requirements and ownership.

## Added tools and checks

- `benchmarks/JxlSharp.ConcurrencyBench` is a standalone project using only
  repository project references. It measures complete encode/decode calls and
  writes one JSON record per configuration.
- `Run-Matrix.ps1` launches one process per configuration, records trials in
  JSONL, reverses worker-limit order in alternate trials, and restores the
  caller's tiering environment variable. It is independent of the main solution
  and must be built explicitly. See its README for commands and metric limits.
- Encode/decode NumThreads XML docs clarify that the limit applies per operation.
- The main README demonstrates a bounded Parallel.For batch with an explicit
  per-image worker count. The count is a tuning starting point, not a new default.
- Six new tests run lossless/lossy encode and decode concurrently with 1/4/auto
  workers, using a shared codec and read-only options. Each of four distinct
  images runs three iterations and compares both bytes and pixels with serial
  references. Existing ownership and group-boundary tests remain included.

## Measurement design

The timed workload uses distinct deterministic 1024x1024 RGB8 images in each
lane, Effort=3, lossy Distance=1 or lossless, and RGB8 decode without JPEG
reconstruction. It uses the source code after the previous scratch optimization.

Dedicated caller threads synchronize at the start/end of each wave. After two
warmup waves, five, ten or fifteen waves are measured. API timings include pixel
snapshots, worker setup/disposal, temporary allocations and processing. Serial
reference generation, outer-thread creation and output verification are outside
the measured region. A shared facade and options are used without mutation.

The first matrix covers all four operation/coding combinations at concurrency
1/2/4 and worker counts 1/4/auto: 36 cases, five waves per case. Another 16 cases
cover 2/8 workers with 2/4 simultaneous images, ten waves each. The focused
comparison runs four simultaneous images at 4/auto workers, all four operation
types, with three fresh-process trials and fifteen waves: 24 cases.

Total: **76 measured process configurations**. Every configuration verifies its
final result in each lane against serial encoded bytes or decoded pixels. It
does not compare every intermediate timed output. Across all cases, 2,340 API
calls are timed, in addition to setup and warmups.

These are synchronized batches; a continuously replenished queue can have
different throughput and waiting time. Latency here is time inside each codec
call, not time waiting in an application queue.

## Focused results: four simultaneous images

Each table entry is the median of that metric across three process trials,
with fifteen waves (60 completed images) per trial. Median latency and p95 are
computed within each trial, then summarized across trials. MB = 1,000,000 bytes.

| Operation | Images/s auto -> 4 workers | Allocated MB/image auto -> 4 | Sampled working-set peak MB auto -> 4 |
|---|---:|---:|---:|
| Lossless encode | 25.19 -> 24.76 | 126.69 -> 107.48 | 1375 -> 1150 |
| Lossy encode | 18.32 -> 19.05 | 99.44 -> 93.44 | 694 -> 675 |
| Lossless decode | 34.47 -> 34.20 | 51.53 -> 40.59 | 632 -> 607 |
| Lossy decode | **80.53 -> 96.96** | **30.86 -> 16.10** | **359 -> 320** |

Lossy decode is the clearest result:

- Four workers improve throughput by 17.4%, 20.4% and 26.5% in the respective
  trials; the median summarized gain is about 20%.
- Median call latency falls from 45.64 to 37.81 ms, and summarized p95 from
  52.05 to 44.28 ms.
- Allocation per image falls by about 48%. Process working-set samples are lower
  in these trials, but that is not a hard memory bound.

For lossy encode, four workers are faster in two trials and slower in the third.
It would be incorrect to describe the median's small advantage as a repeatable
encoder speedup. For both lossless operations the throughput differences are
small relative to observed variation; auto can remain faster.

In the exploratory one-image cases, auto gives 18.21 versus 15.18 images/s for
lossless encode and 34.07 versus 18.65 for lossless decode compared with four
workers. Those are single short trials, but they show why a blanket four-worker
default is not justified by the batch result.

## Memory and CPU interpretation

NumThreads is per call. Four automatic operations can provision up to 64 codec
workers on this machine, plus callers and runtime threads. Not all are runnable
simultaneously: stages have different task counts, serial portions and waits.
The earlier stage task limits reduce scratch slots, but fixed pools still have
their configured OS thread capacity. More workers can hide serial-stage gaps
between images, or can add scheduling and allocation costs; measurement is needed.

The benchmark counts managed allocations across process threads and samples
Environment.WorkingSet and GC.GetTotalMemory(false) roughly every 5 ms. Working
set includes code, stacks, runtime, retained pools and heap. Occupied managed
memory includes garbage not yet collected; it is not a retained-live-size metric.
Samples can miss brief peaks. Setup/warmup garbage and runtime GC policy affect
the region, even though configurations use separate processes. No forced GC is
performed during the timed rounds.

Working set is not monotonic in thread count. For example, in the initial short
lossy encode run with four images, auto sampled about 670 MB and four workers
about 764 MB; later long-trial medians were about 694 and 675 MB respectively.
Allocation reductions therefore must not be presented as equal peak-memory
reductions. Process CPU timing also has coarse accounting granularity and is
most useful over the longer trials. Changing server/workstation GC, dimensions,
image content, effort or background CPU load can change the split that wins.

## Practical use

- For an interactive single image, retain auto as the first configuration to
  measure when latency matters.
- For this machine's lossy decode batch, four simultaneous calls with
  NumThreads=4 are a useful measured configuration. For a different workload,
  compare 1/2/4 outer operations and 1/2/4/8/auto workers.
- Treat ProcessorCount / concurrentImages as a starting budget, not a guarantee
  of optimal throughput. Fixed worker counts exclude the waiting caller and
  runtime threads.
- Do not mutate shared options or input buffers during operations. A codec
  instance can serve independent calls as tested here; an individual underlying
  runner is mutable and must not be shared by overlapping invocations.
- A long batch should load/save within a bounded pipeline if total memory is
  constrained. Holding all inputs and outputs, as in a small array example, adds
  memory regardless of the number of active codec calls.

No photography/HDR/animation/JPEG reconstruction corpus was profiled here.
These synthetic results do not establish a universal production worker budget.

## Validation and reproduction

The Release benchmark builds with zero warnings/errors. **32 selected
integration/model tests pass**, including six new concurrent cases. The matrix
script's repeat loop and restoration of DOTNET_TieredCompilation were checked
with two tiny smoke cases. The full external-fixture integration suite was not
run; production codec logic was not changed in this stage.

Run the focused comparison from the repository root:

```powershell
dotnet build benchmarks/JxlSharp.ConcurrencyBench/JxlSharp.ConcurrencyBench.csproj -c Release --source https://api.nuget.org/v3/index.json
& benchmarks/JxlSharp.ConcurrencyBench/Run-Matrix.ps1 -ConcurrentImages 4 -ThreadsPerImage 4,0 -Rounds 15 -Repeats 3
```

Ignored raw outputs are in `artifacts/profiles/concurrency/`: `encode-1024.jsonl`,
`decode-1024.jsonl`, `intermediate-1024.jsonl`, and the focused comparison
`matrix-20261030-093540-1009121.jsonl`. Runtime/GC metadata was subsequently added
to the output schema outside the measured region; a smoke run confirmed
.NET 10.0.14 X64, workstation GC, Interactive latency and disabled tiering.

The next implementation target is reducing fixed-worker wake-ups and repeated
pool creation, after profiling how much of short-call time they actually occupy.
Persistent reuse would need explicit disposal and non-overlap rules rather than
sharing one runner across active images.
