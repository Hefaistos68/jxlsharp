using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using JxlSharp;
using JxlSharp.Base;

// One configuration per process keeps sampled memory independent of earlier cases.
if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: <encode|decode> <lossless|lossy> <concurrent-images> <threads-per-image; 0=auto> [size=1024] [rounds=5]");
    return 2;
}
bool encode = args[0] switch { "encode" => true, "decode" => false, _ => throw new ArgumentException("Invalid operation") };
bool lossless = args[1] switch { "lossless" => true, "lossy" => false, _ => throw new ArgumentException("Invalid coding mode") };
int concurrency = int.Parse(args[2]), threads = int.Parse(args[3]);
int size = args.Length > 4 ? int.Parse(args[4]) : 1024;
int rounds = args.Length > 5 ? int.Parse(args[5]) : 5;
if (concurrency < 1 || concurrency > 64 || threads < 0 || size < 1 || rounds < 1) throw new ArgumentOutOfRangeException(nameof(args));

var codec = new JxlSharpCodec();
var images = new JxlImageData[concurrency];
var compressed = new byte[concurrency][];
var expected = new byte[concurrency][];
var results = new byte[concurrency][];
var errors = new Exception?[concurrency];
var latencies = new double[rounds * concurrency];
var encodeOptions = new JxlEncodeOptions { Lossless = lossless, Distance = lossless ? 0 : 1, Effort = 3, NumThreads = threads };
var decodeOptions = new JxlDecodeOptions { NumThreads = threads, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };
var serialEncode = new JxlEncodeOptions { Lossless = lossless, Distance = lossless ? 0 : 1, Effort = 3, NumThreads = 1 };
var serialDecode = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };
for (int i = 0; i < concurrency; i++)
{
    byte[] pixels = new byte[checked(size * size * 3)];
    for (int y = 0; y < size; y++)
    for (int x = 0; x < size; x++)
    {
        int p = (y * size + x) * 3;
        pixels[p] = (byte)(x * 3 + y + i * 7);
        pixels[p + 1] = (byte)(x + y * 5 + i * 11);
        pixels[p + 2] = (byte)((x ^ y) + i * 13);
    }
    images[i] = new JxlImageData(pixels, size, size, 3);
    compressed[i] = codec.Encode(images[i], serialEncode);
    expected[i] = encode ? compressed[i] : codec.Decode(compressed[i], serialDecode).PrimaryImage!.Pixels;
    if (!encode && lossless && !expected[i].AsSpan().SequenceEqual(pixels)) throw new InvalidOperationException("Serial round trip failed");
}

using var start = new Barrier(concurrency + 1);
using var done = new Barrier(concurrency + 1);
var workers = new Thread[concurrency];
const int warmups = 2;
for (int i = 0; i < concurrency; i++)
{
    int index = i;
    workers[i] = new Thread(() =>
    {
        for (int iteration = 0; iteration < warmups + rounds; iteration++)
        {
            start.SignalAndWait();
            long before = Stopwatch.GetTimestamp();
            try
            {
                results[index] = encode
                    ? codec.Encode(images[index], encodeOptions)
                    : codec.Decode(compressed[index], decodeOptions).PrimaryImage!.Pixels;
            }
            catch (Exception error) { errors[index] = error; }
            if (iteration >= warmups)
                latencies[(iteration - warmups) * concurrency + index] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            done.SignalAndWait();
        }
    }) { IsBackground = true, Name = $"CodecBench-{index}" };
    workers[i].Start();
}
for (int i = 0; i < warmups; i++) { start.SignalAndWait(); done.SignalAndWait(); }

// Sample current process working set and occupied managed heap, without walking
// Process.Threads or forcing GC during the timed rounds. Sampling can miss brief peaks.
long peakWorkingSet = Environment.WorkingSet, peakHeap = GC.GetTotalMemory(false);
long baseWorkingSet = peakWorkingSet;
using var samplingDone = new ManualResetEventSlim();
var sampler = new Thread(() =>
{
    while (!samplingDone.Wait(5))
    {
        peakWorkingSet = Math.Max(peakWorkingSet, Environment.WorkingSet);
        peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
    }
}) { IsBackground = true, Name = "CodecBench-Memory" };
sampler.Start();
using var process = Process.GetCurrentProcess();
TimeSpan cpuBefore = process.TotalProcessorTime;
int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
long allocatedBefore = GC.GetTotalAllocatedBytes(true);
long wallStart = Stopwatch.GetTimestamp();
for (int i = 0; i < rounds; i++) { start.SignalAndWait(); done.SignalAndWait(); }
double elapsedMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;
long allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
int collections0 = GC.CollectionCount(0) - g0, collections1 = GC.CollectionCount(1) - g1, collections2 = GC.CollectionCount(2) - g2;
samplingDone.Set();
sampler.Join();
foreach (var worker in workers) worker.Join();
for (int i = 0; i < concurrency; i++)
{
    if (errors[i] is not null) throw new InvalidOperationException("Concurrent operation failed", errors[i]);
    if (!results[i].AsSpan().SequenceEqual(expected[i])) throw new InvalidOperationException($"Concurrent result differs from serial result, image {i}");
}
Array.Sort(latencies);
Console.WriteLine(JsonSerializer.Serialize(new
{
    operation = args[0], coding = args[1], size, concurrency, threads, rounds,
    processorCount = Environment.ProcessorCount,
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    serverGc = GCSettings.IsServerGC,
    gcLatencyMode = GCSettings.LatencyMode.ToString(),
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    elapsedMs, imagesPerSecond = rounds * concurrency * 1000.0 / elapsedMs,
    megapixelsPerSecond = rounds * concurrency * (double)size * size / (elapsedMs * 1000.0),
    latencyMedianMs = latencies[latencies.Length / 2],
    latencyP95Ms = latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1],
    allocatedBytesPerImage = allocated / (rounds * concurrency),
    cpuMsPerImage = cpuMs / (rounds * concurrency),
    sampledPeakWorkingSetBytes = peakWorkingSet, workingSetBeforeRoundsBytes = baseWorkingSet,
    sampledPeakOccupiedManagedBytes = peakHeap,
    gc0 = collections0, gc1 = collections1, gc2 = collections2,
    verified = true,
}));
return 0;
