// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using JxlSharp;
using JxlSharp.Base;

namespace JxlSharp.ConcurrencyBench
{
    /// <summary>
    /// Entry point for the multi-image concurrency and allocation benchmark.
    /// </summary>
    internal static class Program
    {
        private readonly record struct BenchConfig(
            string Operation,
            string Coding,
            bool Encode,
            bool Lossless,
            int Concurrency,
            int Threads,
            int Size,
            int Rounds);

        private readonly record struct BenchInputs(
            JxlImageData[] Images,
            byte[][] Compressed,
            byte[][] Expected,
            JxlEncodeOptions EncodeOptions,
            JxlDecodeOptions DecodeOptions);

        private readonly record struct BenchMetrics(
            double ElapsedMs,
            double CpuMs,
            long AllocatedBytes,
            long PeakWorkingSetBytes,
            long BaseWorkingSetBytes,
            long PeakOccupiedManagedBytes,
            int Gc0,
            int Gc1,
            int Gc2);

        private static int Main(string[] args)
        {
            // One configuration per process keeps sampled memory independent of earlier cases.
            if (!TryParseConfig(args, out BenchConfig config))
            {
                return 2;
            }

            var codec           = new JxlSharpCodec();
            BenchInputs inputs  = CreateInputs(codec, config);
            byte[][] results    = new byte[config.Concurrency][];
            Exception?[] errors = new Exception?[config.Concurrency];
            double[] latencies  = new double[config.Rounds * config.Concurrency];
            BenchMetrics metric = ExecuteTimedRun(codec, config, inputs, results, errors, latencies);

            VerifyResults(config.Concurrency, errors, results, inputs.Expected);
            WriteJsonReport(config, metric, latencies);

            return 0;
        }

        private static bool TryParseConfig(string[] args, out BenchConfig config)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: <encode|decode> <lossless|lossy> <concurrent-images> <threads-per-image; 0=auto> [size=1024] [rounds=5]");
                config = default;

                return false;
            }

            bool encode     = ParseOperation(args[0]);
            bool lossless   = ParseCoding(args[1]);
            int concurrency = int.Parse(args[2]);
            int threads     = int.Parse(args[3]);
            int size        = args.Length > 4 ? int.Parse(args[4]) : 1024;
            int rounds      = args.Length > 5 ? int.Parse(args[5]) : 5;

            ValidateBounds(concurrency, threads, size, rounds);
            config = new BenchConfig(args[0], args[1], encode, lossless, concurrency, threads, size, rounds);

            return true;
        }

        private static bool ParseOperation(string value)
        {
            return value switch
            {
                "encode" => true,
                "decode" => false,
                _        => throw new ArgumentException("Invalid operation"),
            };
        }

        private static bool ParseCoding(string value)
        {
            return value switch
            {
                "lossless" => true,
                "lossy"    => false,
                _          => throw new ArgumentException("Invalid coding mode"),
            };
        }

        private static void ValidateBounds(int concurrency, int threads, int size, int rounds)
        {
            if (concurrency < 1 || concurrency > 64 || threads < 0 || size < 1 || rounds < 1)
            {
                throw new ArgumentOutOfRangeException("args");
            }
        }

        private static BenchInputs CreateInputs(JxlSharpCodec codec, BenchConfig config)
        {
            int c              = config.Concurrency;
            int size           = config.Size;
            var images         = new JxlImageData[c];
            var compressed     = new byte[c][];
            var expected       = new byte[c][];
            var encodeOptions  = new JxlEncodeOptions { Lossless = config.Lossless, Distance = config.Lossless ? 0 : 1, Effort = 3, NumThreads = config.Threads };
            var decodeOptions  = new JxlDecodeOptions { NumThreads = config.Threads, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };
            var serialEncode   = new JxlEncodeOptions { Lossless = config.Lossless, Distance = config.Lossless ? 0 : 1, Effort = 3, NumThreads = 1 };
            var serialDecode   = new JxlDecodeOptions { NumThreads = 1, RequestedChannels = 3, RequestedDataType = JxlDataType.UInt8 };

            for (int i = 0; i < c; i++)
            {
                byte[] pixels = GeneratePatternPixels(size, i);

                images[i]     = new JxlImageData(pixels, size, size, 3);
                compressed[i] = codec.Encode(images[i], serialEncode);
                expected[i]   = config.Encode ? compressed[i] : codec.Decode(compressed[i], serialDecode).PrimaryImage!.Pixels;

                if (!config.Encode && config.Lossless && !expected[i].AsSpan().SequenceEqual(pixels))
                {
                    throw new InvalidOperationException("Serial round trip failed");
                }
            }

            return new BenchInputs(images, compressed, expected, encodeOptions, decodeOptions);
        }

        private static byte[] GeneratePatternPixels(int size, int seedIndex)
        {
            byte[] pixels = new byte[checked(size * size * 3)];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int p = (y * size + x) * 3;

                    pixels[p]     = (byte)(x * 3 + y + seedIndex * 7);
                    pixels[p + 1] = (byte)(x + y * 5 + seedIndex * 11);
                    pixels[p + 2] = (byte)((x ^ y) + seedIndex * 13);
                }
            }

            return pixels;
        }

        private static BenchMetrics ExecuteTimedRun(
            JxlSharpCodec codec,
            BenchConfig config,
            BenchInputs inputs,
            byte[][] results,
            Exception?[] errors,
            double[] latencies)
        {
            const int warmups  = 2;
            using var start    = new Barrier(config.Concurrency + 1);
            using var done     = new Barrier(config.Concurrency + 1);
            Thread[] workers   = StartWorkers(codec, config, inputs, (start, done), (results, errors, latencies), warmups);

            for (int i = 0; i < warmups; i++)
            {
                start.SignalAndWait();
                done.SignalAndWait();
            }

            // Sample current process working set and occupied managed heap, without walking
            // Process.Threads or forcing GC during the timed rounds. Sampling can miss brief peaks.
            long baseWorkingSet                    = Environment.WorkingSet;
            long baseHeap                          = GC.GetTotalMemory(false);
            using var samplingDone                 = new ManualResetEventSlim();
            (Thread Sampler, long[] Peaks) sampler = StartMemorySampler(samplingDone, baseWorkingSet, baseHeap);
            using var process                      = Process.GetCurrentProcess();
            TimeSpan cpuBefore                     = process.TotalProcessorTime;
            int g0                                 = GC.CollectionCount(0);
            int g1                                 = GC.CollectionCount(1);
            int g2                                 = GC.CollectionCount(2);
            long allocatedBefore                   = GC.GetTotalAllocatedBytes(true);
            long wallStart                         = Stopwatch.GetTimestamp();

            for (int i = 0; i < config.Rounds; i++)
            {
                start.SignalAndWait();
                done.SignalAndWait();
            }

            double elapsedMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;
            long allocated   = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
            double cpuMs     = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            int c0           = GC.CollectionCount(0) - g0;
            int c1           = GC.CollectionCount(1) - g1;
            int c2           = GC.CollectionCount(2) - g2;

            samplingDone.Set();
            sampler.Sampler.Join();

            foreach (Thread worker in workers)
            {
                worker.Join();
            }

            long peakWorkingSet = Volatile.Read(ref sampler.Peaks[0]);
            long peakHeap       = Volatile.Read(ref sampler.Peaks[1]);

            return new BenchMetrics(elapsedMs, cpuMs, allocated, peakWorkingSet, baseWorkingSet, peakHeap, c0, c1, c2);
        }

        private static Thread[] StartWorkers(
            JxlSharpCodec codec,
            BenchConfig config,
            BenchInputs inputs,
            (Barrier Start, Barrier Done) barriers,
            (byte[][] Results, Exception?[] Errors, double[] Latencies) outputs,
            int warmups)
        {
            var workers = new Thread[config.Concurrency];

            for (int i = 0; i < config.Concurrency; i++)
            {
                int index = i;

                workers[i] = new Thread(() => RunWorkerIterations(codec, config, inputs, barriers, outputs, index, warmups))
                {
                    IsBackground = true,
                    Name         = $"CodecBench-{index}",
                };
                workers[i].Start();
            }

            return workers;
        }

        private static void RunWorkerIterations(
            JxlSharpCodec codec,
            BenchConfig config,
            BenchInputs inputs,
            (Barrier Start, Barrier Done) barriers,
            (byte[][] Results, Exception?[] Errors, double[] Latencies) outputs,
            int index,
            int warmups)
        {
            int total = warmups + config.Rounds;

            for (int iteration = 0; iteration < total; iteration++)
            {
                barriers.Start.SignalAndWait();

                long before = Stopwatch.GetTimestamp();

                try
                {
                    outputs.Results[index] = config.Encode
                        ? codec.Encode(inputs.Images[index], inputs.EncodeOptions)
                        : codec.Decode(inputs.Compressed[index], inputs.DecodeOptions).PrimaryImage!.Pixels;
                }
                catch (Exception error)
                {
                    outputs.Errors[index] = error;
                }

                if (iteration >= warmups)
                {
                    int slot = (iteration - warmups) * config.Concurrency + index;

                    outputs.Latencies[slot] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
                }

                barriers.Done.SignalAndWait();
            }
        }

        private static (Thread Sampler, long[] Peaks) StartMemorySampler(
            ManualResetEventSlim done,
            long initialWorkingSet,
            long initialHeap)
        {
            long[] peaks = { initialWorkingSet, initialHeap };
            var sampler  = new Thread(() =>
            {
                while (!done.Wait(5))
                {
                    long ws = Environment.WorkingSet;
                    long hp = GC.GetTotalMemory(false);

                    if (ws > Volatile.Read(ref peaks[0]))
                    {
                        Volatile.Write(ref peaks[0], ws);
                    }

                    if (hp > Volatile.Read(ref peaks[1]))
                    {
                        Volatile.Write(ref peaks[1], hp);
                    }
                }
            })
            {
                IsBackground = true,
                Name         = "CodecBench-Memory",
            };

            sampler.Start();

            return (sampler, peaks);
        }

        private static void VerifyResults(int concurrency, Exception?[] errors, byte[][] results, byte[][] expected)
        {
            for (int i = 0; i < concurrency; i++)
            {
                if (errors[i] is not null)
                {
                    throw new InvalidOperationException("Concurrent operation failed", errors[i]);
                }

                if (!results[i].AsSpan().SequenceEqual(expected[i]))
                {
                    throw new InvalidOperationException($"Concurrent result differs from serial result, image {i}");
                }
            }
        }

        private static void WriteJsonReport(BenchConfig config, BenchMetrics m, double[] latencies)
        {
            Array.Sort(latencies);
            int totalOps = config.Rounds * config.Concurrency;

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                operation                       = config.Operation,
                coding                          = config.Coding,
                size                            = config.Size,
                concurrency                     = config.Concurrency,
                threads                         = config.Threads,
                rounds                          = config.Rounds,
                processorCount                  = Environment.ProcessorCount,
                runtime                         = RuntimeInformation.FrameworkDescription,
                architecture                    = RuntimeInformation.ProcessArchitecture.ToString(),
                serverGc                        = GCSettings.IsServerGC,
                gcLatencyMode                   = GCSettings.LatencyMode.ToString(),
                tieredCompilation               = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                elapsedMs                       = m.ElapsedMs,
                imagesPerSecond                 = totalOps * 1000.0 / m.ElapsedMs,
                megapixelsPerSecond             = totalOps * (double)config.Size * config.Size / (m.ElapsedMs * 1000.0),
                latencyMedianMs                 = latencies[latencies.Length / 2],
                latencyP95Ms                    = latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1],
                allocatedBytesPerImage          = m.AllocatedBytes / totalOps,
                cpuMsPerImage                   = m.CpuMs / totalOps,
                sampledPeakWorkingSetBytes      = m.PeakWorkingSetBytes,
                workingSetBeforeRoundsBytes     = m.BaseWorkingSetBytes,
                sampledPeakOccupiedManagedBytes = m.PeakOccupiedManagedBytes,
                gc0                             = m.Gc0,
                gc1                             = m.Gc1,
                gc2                             = m.Gc2,
                verified                        = true,
            }));
        }
    }
}
