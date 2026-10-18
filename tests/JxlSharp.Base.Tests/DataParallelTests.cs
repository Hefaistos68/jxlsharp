// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ThreadPool"/> and <see cref="DataParallel"/>
    /// (ported from <c>lib/jxl/data_parallel_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class DataParallelTests
    {
        private sealed class FakeRunnerState
        {
            public int RunnerCalled { get; set; }

            public object? JpegxlOpaque { get; set; }

            public JxlParallelRunInit? Init { get; set; }

            public JxlParallelRunFunction? Func { get; set; }

            public uint StartRange { get; set; } = uint.MaxValue;

            public uint EndRange { get; set; } = uint.MaxValue;

            public int RunnerReturn { get; set; }

            public static int FakeRunner(
                object? runnerOpaque,
                object? jpegxlOpaque,
                JxlParallelRunInit init,
                JxlParallelRunFunction func,
                uint startRange,
                uint endRange)
            {
                FakeRunnerState self = (FakeRunnerState)runnerOpaque!;
                self.RunnerCalled++;
                self.JpegxlOpaque = jpegxlOpaque;
                self.Init         = init;
                self.Func         = func;
                self.StartRange   = startRange;
                self.EndRange     = endRange;

                return self.RunnerReturn;
            }
        }

        /// <summary>
        /// Ported from <c>DataParallelTest.RunnerCalledParameters</c>.
        /// </summary>
        [Test]
        public void RunnerCalledParameters()
        {
            FakeRunnerState state = new FakeRunnerState();
            ThreadPool pool       = new ThreadPool(FakeRunnerState.FakeRunner, state);

            Status status = pool.Run(
                1234,
                5678,
                static _ => true,
                static (_, _) => true,
                "Test");

            status.IsOk.ShouldBeTrue();
            state.RunnerCalled.ShouldBe(1);
            state.Init.ShouldNotBeNull();
            state.Func.ShouldNotBeNull();
            state.JpegxlOpaque.ShouldNotBeNull();
            state.StartRange.ShouldBe(1234u);
            state.EndRange.ShouldBe(5678u);
        }

        /// <summary>
        /// Ported from <c>DataParallelTest.RunnerFailurePropagates</c>.
        /// </summary>
        [Test]
        public void RunnerFailurePropagates()
        {
            FakeRunnerState state = new FakeRunnerState { RunnerReturn = -1 };
            ThreadPool pool       = new ThreadPool(FakeRunnerState.FakeRunner, state);

            Status statusA = pool.Run(
                1234,
                5678,
                static _ => Status.Failure("init"),
                static (_, _) => true,
                "TestA");
            statusA.IsOk.ShouldBeFalse();

            Status statusB = DataParallel.RunOnPool(
                null,
                1234,
                5678,
                static _ => Status.Failure("init"),
                static (_, _) => true,
                "TestB");
            statusB.IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Ported from <c>DataParallelTest.RunnerNotCalledOnEmptyRange</c>.
        /// </summary>
        [Test]
        public void RunnerNotCalledOnEmptyRange()
        {
            FakeRunnerState state = new FakeRunnerState { RunnerReturn = -1 };
            ThreadPool pool       = new ThreadPool(FakeRunnerState.FakeRunner, state);

            Status statusA = pool.Run(
                123,
                123,
                static _ => Status.Failure("init"),
                static (_, _) => true,
                "TestA");
            statusA.IsOk.ShouldBeTrue();

            Status statusB = DataParallel.RunOnPool(
                null,
                123,
                123,
                static _ => Status.Failure("init"),
                static (_, _) => true,
                "TestB");
            statusB.IsOk.ShouldBeTrue();

            // We don't call the external runner when the range is empty. We don't even
            // need to call the init function.
            state.RunnerCalled.ShouldBe(0);
        }

        /// <summary>
        /// Verifies sequential <see cref="DataParallel.RunOnPool(ThreadPool?, uint, uint, ThreadPoolNoInit, System.Func{uint, nuint, Status}, string)"/> execution.
        /// </summary>
        [Test]
        public void SequentialRunOnPoolExecutesAllItems()
        {
            int sum = 0;

            Status status = DataParallel.RunOnPool(
                null,
                1,
                5,
                ThreadPool.NoInit,
                (task, _) =>
                {
                    sum += (int)task;

                    return true;
                },
                "SequentialSum");

            status.IsOk.ShouldBeTrue();
            sum.ShouldBe(1 + 2 + 3 + 4);
        }
    }
}
