using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests;

/// <summary>Checks the sequential scheduling path and its allocation budget.</summary>
[TestFixture]
public class SerialAllocationTests
{
    /// <summary>Pre-created callbacks need no per-run heap state.</summary>
    [Test]
    public void SequentialRunsDoNotAllocate()
    {
        Func<nuint, Status> init = static n => n == 1;
        Func<uint, nuint, Status> data = static (_, thread) => thread == 0;
        var pool = new ThreadPool(null, null);
        for (int i = 0; i < 100; i++)
        {
            DataParallel.RunOnPool(null, 0, 8, init, data);
            pool.Run(0, 8, init, data);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            DataParallel.RunOnPool(null, 0, 8, init, data);
            pool.Run(0, 8, init, data);
        }
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    /// <summary>Empty ranges skip initialization; failures stop processing.</summary>
    [Test]
    public void SequentialCallbacksRespectRangeAndFailure()
    {
        int initialized = 0, visited = 0;
        Status Init(nuint _) { initialized++; return true; }
        Status Data(uint task, nuint thread) { thread.ShouldBe((nuint)0); visited++; return task != 4; }
        DataParallel.RunOnPool(null, 3, 3, Init, Data).IsOk.ShouldBeTrue();
        initialized.ShouldBe(0);
        DataParallel.RunOnPool(null, 3, 8, Init, Data).IsOk.ShouldBeFalse();
        initialized.ShouldBe(1);
        visited.ShouldBe(2);
    }
}
