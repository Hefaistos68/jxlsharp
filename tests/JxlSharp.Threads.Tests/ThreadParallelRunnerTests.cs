// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Numerics;
using System.Threading;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Threads.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ThreadParallelRunner"/>
    /// (ported from <c>lib/threads/thread_parallel_runner_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class ThreadParallelRunnerTests
    {
        /// <summary>
        /// Ported from <c>ThreadParallelRunnerTest.TestPool</c>.
        /// Ensures task parameter is in bounds, every parameter is reached,
        /// pool can be reused (multiple consecutive Run calls), pool can be destroyed
        /// (joining with its threads), num_threads=0 works (runs on current thread).
        /// </summary>
        [Test]
        public void TestPool()
        {
            for (int numThreads = 0; numThreads <= 18; ++numThreads)
            {
                using ThreadParallelRunner runner = new ThreadParallelRunner(numThreads);
                JxlSharp.Base.ThreadPool pool     = runner.CreateThreadPool();

                for (int numTasks = 0; numTasks < 32; ++numTasks)
                {
                    int[] mementos = new int[numTasks];

                    for (int begin = 0; begin < 32; ++begin)
                    {
                        Array.Clear(mementos, 0, mementos.Length);
                        int capturedBegin    = begin;
                        int capturedNumTasks = numTasks;

                        Status status = pool.Run(
                            (uint)begin,
                            (uint)(begin + numTasks),
                            static _ => true,
                            (task, _) =>
                            {
                                int t = (int)task;

                                if (t < capturedBegin || t >= capturedBegin + capturedNumTasks)
                                {
                                    return false;
                                }

                                mementos[t - capturedBegin] = 1000 + t;

                                return true;
                            },
                            "TestPool");

                        status.IsOk.ShouldBeTrue();

                        for (int task = begin; task < begin + numTasks; ++task)
                        {
                            mementos[task - begin].ShouldBe(1000 + task);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Ported from <c>ThreadParallelRunnerTest.TestSmallAssignments</c>.
        /// Verify "thread" parameter when processing few tasks.
        /// </summary>
        [Test]
        public void TestSmallAssignments()
        {
            const int kMaxThreads = 8;

            for (int numThreads = 1; numThreads <= kMaxThreads; ++numThreads)
            {
                using ThreadParallelRunner runner = new ThreadParallelRunner(numThreads);
                JxlSharp.Base.ThreadPool pool     = runner.CreateThreadPool();

                ulong idBits = 0;
                int numCalls = 0;
                int maxT     = numThreads;

                Status status = pool.Run(
                    0,
                    (uint)numThreads,
                    static _ => true,
                    (_, thread) =>
                    {
                        Interlocked.Increment(ref numCalls);

                        if ((int)thread >= maxT)
                        {
                            return false;
                        }

                        Interlocked.Or(ref idBits, 1UL << (int)thread);

                        return true;
                    },
                    "TestSmallAssignments");

                status.IsOk.ShouldBeTrue();
                numCalls.ShouldBe(numThreads);

                int numParticipants = BitOperations.PopCount(Volatile.Read(ref idBits));
                numParticipants.ShouldBeLessThanOrEqualTo(numThreads);
            }
        }

        /// <summary>
        /// Ported from <c>ThreadParallelRunnerTest.TestCounter</c>.
        /// </summary>
        [Test]
        public void TestCounter()
        {
            const int kNumThreads = 12;
            using ThreadParallelRunner runner = new ThreadParallelRunner(kNumThreads);
            JxlSharp.Base.ThreadPool pool     = runner.CreateThreadPool();

            int[] counters = new int[kNumThreads];
            const int kNumTasks = kNumThreads * 19;

            Status status = pool.Run(
                0,
                kNumTasks,
                static _ => true,
                (task, thread) =>
                {
                    counters[thread] += (int)task;

                    return true;
                },
                "TestCounter");

            status.IsOk.ShouldBeTrue();

            int expected = 0;

            for (int i = 0; i < kNumTasks; ++i)
            {
                expected += i;
            }

            int actual = 0;

            for (int i = 0; i < kNumThreads; ++i)
            {
                actual += counters[i];
            }

            actual.ShouldBe(expected);
        }

        /// <summary>
        /// Verifies <see cref="ThreadParallelRunner.Create"/>, <see cref="ThreadParallelRunner.Destroy"/>,
        /// and <see cref="ThreadParallelRunner.RunOnEachThread"/>.
        /// </summary>
        [Test]
        public void CreateDestroyAndRunOnEachThreadWork()
        {
            ThreadParallelRunner? runner = ThreadParallelRunner.Create(null, 4);
            runner.ShouldNotBeNull();
            runner.NumThreads.ShouldBe((nuint)4);

            int visitedMask = 0;
            runner.RunOnEachThread((task, thread) =>
            {
                if (task == (uint)thread)
                {
                    Interlocked.Or(ref visitedMask, 1 << (int)thread);
                }
            });

            visitedMask.ShouldBe(0b1111);
            ThreadParallelRunner.Destroy(runner);
        }
    }
}
