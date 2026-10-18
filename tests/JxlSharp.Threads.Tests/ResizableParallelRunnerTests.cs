// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Threads.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ResizableParallelRunner"/>.
    /// </summary>
    [TestFixture]
    public class ResizableParallelRunnerTests
    {
        /// <summary>
        /// Verifies <see cref="ResizableParallelRunner"/> creation, dynamic thread resizing (up and down),
        /// task execution, and <see cref="ResizableParallelRunner.SuggestThreads"/>.
        /// </summary>
        [Test]
        public void ResizableRunnerScalesThreadsUpAndDown()
        {
            ResizableParallelRunner runner = ResizableParallelRunner.Create();
            JxlSharp.Base.ThreadPool pool  = runner.CreateThreadPool();

            ResizableParallelRunner.SuggestThreads(2048, 2048).ShouldBeGreaterThanOrEqualTo(1u);

            foreach (nuint threads in new nuint[] { 0, 1, 4, 8, 2, 0 })
            {
                ResizableParallelRunner.SetThreads(runner, threads);

                int[] results = new int[50];
                Status status = pool.Run(
                    0,
                    (uint)results.Length,
                    static _ => true,
                    (task, _) =>
                    {
                        results[task] = (int)(task * 3);

                        return true;
                    },
                    "ResizableTest");

                status.IsOk.ShouldBeTrue();

                for (int i = 0; i < results.Length; i++)
                {
                    results[i].ShouldBe(i * 3);
                }
            }

            // Single-element range fast path
            int singleValue = 0;
            pool.Run(5, 6, static _ => true, (task, _) =>
            {
                singleValue = (int)task;

                return true;
            }).IsOk.ShouldBeTrue();
            singleValue.ShouldBe(5);

            ResizableParallelRunner.Destroy(runner);
        }
    }
}
