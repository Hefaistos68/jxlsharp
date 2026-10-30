# Allocations, intermediate copies and threading

Investigation: 2026-10-30, Windows x64, Intel Core i5-1340P,
.NET 10.0.14, Release, AVX2. Measurements below use
`DOTNET_TieredCompilation=0` for both the frozen baseline and the final build.

Subsequent copy/ownership work is documented in [copies.md](copies.md). It removes
redundant simple-API pixel copies and repeated compressed-input copies for
file/stream decode and fallback. The measurements below describe the earlier
memory/thread stage, before that follow-up.

The next completed stage is [scratch-threads.md](scratch-threads.md): actual-map
encoder DCT scratch sizes and task-limited worker slots in the default facade.

## Changes retained

- `DataParallel.RunOnPool(null, ...)` no longer creates a temporary ThreadPool
  and RunCallState. A ThreadPool without a custom runner uses the same direct
  sequential path. Initialization, empty/invalid ranges, thread ID zero and
  stopping callbacks after the first failure keep their previous behavior.
  With pre-created delegates, successful sequential scheduling allocates zero
  bytes. Capturing delegates created by callers can still allocate.
- Both runner implementations release the completed callback and opaque state
  once all participating workers are idle. The fixed runner also releases
  RunOnEachThread callbacks. Previously a persistent runner could keep an entire
  image/closure graph alive until the next invocation or the runner's own death.
- The default facade adapter executes a single-task range on the calling thread
  and initializes exactly one scratch slot. It creates its fixed OS worker pool
  lazily, on the first range containing multiple tasks. Empty ranges skip work.
  Multiple-task ranges retain the original fixed runner and its guided chunks.
  Explicitly supplied user runners are unchanged. One-task ranges can occur even
  on large images, so this is a task-count decision, not an image-size threshold.
- Extras' compressed-output staging buffer uses ArrayPool<byte> with geometric
  growth. Growth copies only the written prefix and returns the old rental.
  A finally block returns the final rental on success, error or exception.
  The public result remains a separately owned exact-length array.

Pooling lowers allocation traffic after warmup; it does not remove the final
output copy or guarantee lower resident memory. The shared pool can retain large
arrays. Image allocations and custom memory manager semantics are unchanged.

## Whole-codec measurements

Synthetic RGB input: R=x*3+y, G=x+y*5, B=x^y, modulo 256. Sizes 256x256 and
1024x1024, lossless or Distance=1, Effort=3, UInt8 RGB output, JPEG reconstruction
disabled. Each configuration warms up with three encode/decode pairs, then
measures five encodes and five decodes separately. Setup, runner creation and
disposal are inside the measured API call. Measurements include all managed
threads via GC.GetTotalAllocatedBytes(true), not just the calling thread.

Selected final observations; MB means 1,000,000 allocated bytes per call.
These are allocation traffic figures, **not peak/live memory**.

| Workload | Threads | Allocated MB before -> after | Median ms before -> after |
|---|---:|---:|---:|
| 256 lossless decode | auto | 16.35 -> 2.73 | 11.65 -> 9.73 |
| 256 lossless decode | 4 | 5.46 -> 2.73 | 9.50 -> 9.30 |
| 256 lossy encode | auto | 39.10 -> 35.50 | 26.75 -> 24.82 |
| 256 lossy decode | auto | 2.54 -> 2.42 | 5.26 -> 5.14 |
| 1024 lossless encode | 1 | 103.14 -> 98.96 | 155.74 -> 144.30 |
| 1024 lossless decode | auto | 51.50 -> 51.45 | 35.27 -> 28.41 |
| 1024 lossy encode | 1 | 91.78 -> 90.74 | 191.30 -> 181.47 |
| 1024 lossy encode | auto | 169.41 -> 170.60 | 80.26 -> 79.88 |

The strong repeatable finding is eliminating unused per-thread scratch slots for
single-task work. For 256 lossless decode, auto allocated about six times as much
as one thread before the change; after the change they allocate approximately
the same amount. This does not imply a sixfold speedup.

The 1024 single-thread lossless output is 1,165,940 bytes. The old 4 KiB growing
buffer allocated about 4.19 MB across its successive sizes before producing the
owned result. Reusing staging storage explains most of the measured 4.18 MB
allocation reduction in that case.

Wall times varied substantially across repeated runs, especially with automatic
thread counts. GC scheduling, shared-pool hits and heterogeneous CPU scheduling
also vary allocation totals for parallel encoding. Some individual configurations
were slightly slower and automatic 1024 lossy encode did not reduce allocations
in the final sample. Do not interpret this small synthetic workload as a general
throughput claim. Windows process CPU accounting is coarse for small calls; it
does not establish exact utilization. No allocation-stack trace, native-memory
trace or peak-working-set measurement was taken in this investigation.

Replacing the facade runner wholesale with ResizableParallelRunner was tested
and rejected: it reduced scratch allocation for small ranges but regressed
automatic-thread throughput on 1024 workloads. The retained change is narrower.

Local reproduction artifacts (ignored by git) are in
`artifacts/profiles/memory-threads/`: harness sources, a frozen baseline binary
set, `final-before-{256,1024}.txt`, `final-after-{256,1024}.txt`, and the rejected
`experiment-resizable-*.txt` results. Run the baseline and current executables
sequentially, with the same runtime/environment and no concurrent builds/tests:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet artifacts/profiles/memory-threads/baseline-bin/JxlSharp.Integration.Tests.dll 256 baseline
dotnet artifacts/profiles/memory-threads/harness/bin/Release/net10.0/JxlSharp.Integration.Tests.dll 256 current
```

Use 1024 instead of 256 for the larger workload. Compare .jxl and .rgb files in
the baseline/current artifact directories by content hash.

## Remaining costs and next experiments

1. **Per-thread scratch allocation is still the largest threading target.**
   `EncFrameAndJpeg.cs` allocates `numThreads * 5 * kMaxCoeffArea` floats in its
   transform stage. Decoder per-thread state in `PassesStateAndOutputStages.cs`
   allocates several max-block arrays for coefficients, transform scratch and
   quantized coefficients. Multiple-task ranges still initialize all configured
   thread slots. Experiment with stage-specific worker limits and actual strategy
   sizes, then measure across effort, image dimensions and content. Simply
   shrinking scratch arrays without limiting the worker IDs is unsafe.
2. **Facade pixel copies.** Encoding calls `JxlImageData.ToPackedPixelFile`, which
   allocates packed pixels and copies the image. Decode results eagerly build
   `PrimaryImage` using `FromPackedPixelFile`, copying the packed primary frame.
   At 1024x1024 RGB8 each copy is 3,145,728 bytes; RGBA float32 costs 16,777,216.
   An explicit internal borrowing/ownership API or an opt-in result mode could
   remove these copies. Public conversion methods currently create independent
   pixel storage; silently aliasing arrays or lazily reading a mutable PixelFile
   would change observable behavior.
3. **Compressed input copy.** `CodecJxl.DecodeImageJXL(ReadOnlySpan<byte>, ...)`
   copies the full input; `JxlSharpCodec.GetInfo` also copies the full compressed
   file to inspect its header. The lower decoder already accepts ReadOnlyMemory.
   A memory-based internal route could avoid copies for owned arrays and streams.
   Keep the span route's ownership semantics and avoid ambiguous byte[] overloads.
4. **Streams.** The facade buffers stream input and encoded output in full.
   A real streaming encoder sink and an incremental input path would reduce
   transient storage for large files; changing only the outer stream wrapper
   cannot eliminate the internal buffering.
5. **Concurrent images.** Each facade operation owns its own runner. N parallel
   codec calls can create N times the automatic worker count and scratch storage.
   Measure a corpus with 1/2/4 simultaneous images and explicit per-image limits.
   Reusing a caller-owned runner can amortize startup, but a runner must not be
   shared by overlapping invocations without synchronization. A global cached
   pool needs a defined lifetime, concurrency budget and failure handling.
6. **Selective scratch reuse.** Pool only buffers whose complete initialization,
   padding and lifetime are established. Default AlignedMemory uses managed,
   zero-initialized arrays; default allocation suppresses finalization already.
   Blanket replacement with uninitialized pooled storage can change results and
   break custom allocator ownership. Measure retained memory as well as GC
   allocation traffic before expanding pooling.

Follow-up measurement should include photographic images and larger images,
effort variation, animations/extra channels, allocation-stack traces, peak live
memory and simultaneous codec operations. This investigation completed the
targeted changes above; it did not exhaust memory/concurrency optimization.

## Validation

- Base: 92 tests; Threads: 8; Decoder: 307; Encoder: 170; selected self-contained
  facade/integration tests: 10. Total: **587 passed**. External-fixture-dependent
  integration tests were not run.
- New tests verify zero allocation with pre-created sequential callbacks, failure
  stopping semantics, collection of opaque/callback objects while runners remain
  alive, exactly-once task execution and worker ID bounds.
- Facade tests compare 1/2/4/auto threads at image sizes 1, 255, 256 and 257,
  including lossy cases on both sides of the group boundary.
- Frozen-baseline comparison: both sizes, both coding modes, all four thread
  settings; all **32 compressed/pixel files match byte for byte**. Lossless output
  also matches the source pixels. These checks do not cover every codec feature.
