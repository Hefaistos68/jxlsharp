using System;
using System.Runtime.CompilerServices;
using System.Threading;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Threads.Tests;

/// <summary>Ensures completed work does not keep image state alive.</summary>
[TestFixture]
public class RunnerLifetimeTests
{
    /// <summary>Both runners release opaque state and closure targets after a run.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void CompletedWorkIsCollectible(bool resizable)
    {
        using var fixedRunner = new ThreadParallelRunner(2);
        using var resizingRunner = ResizableParallelRunner.Create();
        resizingRunner.SetNumThreads(3);
        for (int i = 0; i < 20; i++)
        {
            var references = RunWork(resizable ? ResizableParallelRunner.Runner : ThreadParallelRunner.Runner,
                resizable ? resizingRunner : fixedRunner);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            references.Opaque.IsAlive.ShouldBeFalse();
            references.Callback.IsAlive.ShouldBeFalse();
        }
        GC.KeepAlive(fixedRunner);
        GC.KeepAlive(resizingRunner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Opaque, WeakReference Callback) RunWork(JxlParallelRunner runner, object state)
    {
        var opaque = new byte[4096];
        var visited = new int[101];
        nuint threads = 0;
        JxlParallelRunFunction callback = (data, task, thread) =>
        {
            if (thread >= threads || !ReferenceEquals(data, opaque)) throw new InvalidOperationException();
            Interlocked.Increment(ref visited[task]);
        };
        runner(state, opaque, (_, n) => { threads = n; return 0; }, callback, 0, 101).ShouldBe(0);
        foreach (int count in visited) count.ShouldBe(1);
        return (new WeakReference(opaque), new WeakReference(callback));
    }

    /// <summary>RunOnEachThread also releases its closure after all workers finish.</summary>
    [Test]
    public void PerThreadWorkIsCollectible()
    {
        using var runner = new ThreadParallelRunner(2);
        WeakReference callback = RunPerThreadWork(runner);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        callback.IsAlive.ShouldBeFalse();
        GC.KeepAlive(runner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunPerThreadWork(ThreadParallelRunner runner)
    {
        var visited = new int[2];
        Action<uint, nuint> callback = (_, thread) => Interlocked.Increment(ref visited[(int)thread]);
        runner.RunOnEachThread(callback);
        foreach (int count in visited) count.ShouldBe(1);
        return new WeakReference(callback);
    }
}
