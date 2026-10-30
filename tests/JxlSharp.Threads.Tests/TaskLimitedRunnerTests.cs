using System;
using System.Threading;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Threads.Tests;

/// <summary>Verifies task-limited scratch slots without changing the public runner contract.</summary>
[TestFixture]
public sealed class TaskLimitedRunnerTests
{
    /// <summary>Alternating small and large ranges keep thread IDs within the initialized slots.</summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    public void LimitedRangesRespectInitializationAndRemainReusable(int workers)
    {
        using var runner = new ThreadParallelRunner(workers);
        for (int iteration = 0; iteration < 15; iteration++)
        foreach (int count in new[] { 0, 1, 2, 3, 5, 8, 17, 257 })
        {
            int[] seen = new int[count];
            int[]? slots = null;
            int initialized = 0;
            ThreadParallelRunner.RunnerWithTaskLimit(runner, null, (_, n) =>
            {
                initialized++;
                n.ShouldBe((nuint)Math.Max(1, Math.Min(workers, count)));
                slots = new int[(int)n];
                return 0;
            }, (_, task, thread) =>
            {
                Interlocked.Increment(ref slots![(int)thread]);
                Interlocked.Increment(ref seen[task - 19]);
            }, 19, (uint)(19 + count)).ShouldBe(0);
            initialized.ShouldBe(count == 0 ? 0 : 1);
            foreach (int visits in seen) visits.ShouldBe(1);
        }

        // The public runner still initializes all configured slots after a limited run.
        ThreadParallelRunner.Runner(runner, null, (_, n) =>
        {
            n.ShouldBe((nuint)Math.Max(1, workers));
            return 0;
        }, static (_, _, _) => { }, 0, 2).ShouldBe(0);
        int[] perThread = new int[Math.Max(1, workers)];
        runner.RunOnEachThread((_, thread) => Interlocked.Increment(ref perThread[(int)thread]));
        foreach (int visits in perThread) visits.ShouldBe(1);
    }

    /// <summary>Initialization failure prevents task execution and leaves the runner usable.</summary>
    [Test]
    public void FailedInitializationDoesNotStartWorkers()
    {
        using var runner = new ThreadParallelRunner(8);
        int calls = 0;
        ThreadParallelRunner.RunnerWithTaskLimit(runner, null, static (_, _) => -7,
            (_, _, _) => Interlocked.Increment(ref calls), 0, 3).ShouldBe(-7);
        calls.ShouldBe(0);
        ThreadParallelRunner.RunnerWithTaskLimit(runner, null, static (_, _) => 0,
            (_, _, _) => Interlocked.Increment(ref calls), 0, 3).ShouldBe(0);
        calls.ShouldBe(3);
    }
}
