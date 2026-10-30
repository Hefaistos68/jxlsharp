# Concurrent image benchmark

This standalone .NET 10 program measures full facade operations with multiple
images in flight. It uses the current project sources, without BenchmarkDotNet
or external image fixtures. Run it on an otherwise idle machine.

From the repository root:

```powershell
dotnet build benchmarks/JxlSharp.ConcurrencyBench/JxlSharp.ConcurrencyBench.csproj -c Release --source https://api.nuget.org/v3/index.json
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/JxlSharp.ConcurrencyBench/bin/Release/net10.0/JxlSharp.ConcurrencyBench.dll decode lossy 4 4 1024 15
```

Arguments are `encode|decode`, `lossless|lossy`, simultaneous image count,
threads per image (`0` = auto), square image size (default 1024), and measured
rounds (default 5). Encode effort is 3, lossy distance is 1, and decode requests
RGB8 without JPEG reconstruction. Threads=1 uses the sequential facade path.
Every lane has a distinct deterministic RGB8 image and private output.

The matrix script starts a fresh process for each configuration, saves JSONL
under `artifacts/profiles/concurrency/`, and reverses thread-count order in
alternate trials. It assumes the Release program is already built:

```powershell
& benchmarks/JxlSharp.ConcurrencyBench/Run-Matrix.ps1 -Rounds 15 -Repeats 3

# Smaller comparison:
& benchmarks/JxlSharp.ConcurrencyBench/Run-Matrix.ps1 -Operations decode -Coding lossy -ConcurrentImages 4 -ThreadsPerImage 4,0 -Rounds 15 -Repeats 3
```

The defaults test 1/2/4 simultaneous images and 1/2/4/8/auto threads per image.
Do not build, test or run another matrix concurrently with the measurements.
The script restores the caller's DOTNET_TieredCompilation value when finished.

## What is measured

- Two warmup waves followed by the requested number of timed waves. Dedicated
  outer threads share a stateless facade and read-only options. A barrier starts
  each wave together and waits for all images before starting the next wave.
  This measures synchronized batches, not a continuously replenished queue.
- Complete API calls include input/output snapshots, internal OS worker setup,
  per-stage initialization, processing and runner disposal. Serial reference
  generation, verification, outer-thread creation and JSON output are excluded.
- Images/s and MPix/s use completed images and total measured wall time.
  Per-image median and p95 use individual call times, including pauses and
  contention. With few rounds the percentile sample is small.
- Allocated bytes per image use process-wide GC.GetTotalAllocatedBytes(true).
  CPU time includes all process threads. Process CPU accounting is coarse for
  short calls and should be interpreted over longer runs.
- Working set and occupied managed heap are sampled approximately every 5 ms
  on a background thread. The samples can miss brief peaks. Working set includes
  runtime, stacks, code, pools and resident heap; GC.GetTotalMemory(false) is an
  approximate occupied-heap figure including not-yet-collected garbage, not live
  retained bytes. Neither is an allocation-stack or heap-retention profile.
- Setup and warmup garbage/pool contents can affect the timed region. There is
  no forced GC during the measurements. A fresh process isolates configurations
  from one another, but does not make GC or working set deterministic.
- Every lane's final encoded bytes or decoded pixels must match its serial
  reference. A mismatch or exception makes the process fail. This checks the
  final result per lane; it does not hash outputs inside the timed API calls.

The result is a tuning aid for this workload. Use your own corpus, dimensions,
effort, process GC configuration and latency/memory requirements before choosing
production concurrency. See [the investigation](../../concurrency.md).
