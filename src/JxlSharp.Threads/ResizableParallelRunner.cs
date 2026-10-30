// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Threading;
using JxlSharp.Base;

namespace JxlSharp.Threads
{
    /// <summary>
    /// A thread pool that allows changing the number of threads it runs. It also
    /// runs tasks on the calling thread, which can work better on schedulers for
    /// heterogeneous architectures.
    /// Ported from <c>lib/threads/resizable_parallel_runner.cc</c>.
    /// </summary>
    public sealed class ResizableParallelRunner : IDisposable
    {
        private readonly List<Thread> _workers = new List<Thread>();
        private readonly object _stateMutex = new object();

        private int _nextTask;
        private uint _endTask;
        private JxlParallelRunFunction? _func;
        private object? _jxlOpaque;
        private nuint _numDesiredWorkers;
        private nuint _maxRunningWorkers;
        private nuint _numRunningWorkers;
        private bool _workAvailable;
        private bool _disposed;

        /// <summary>
        /// Creates a new <see cref="ResizableParallelRunner"/> instance.
        /// The runner will execute tasks on the calling thread until <see cref="SetThreads"/> is called.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager (unused, kept for API parity).</param>
        /// <returns>New <see cref="ResizableParallelRunner"/> instance.</returns>
        public static ResizableParallelRunner Create(JxlMemoryManager? memoryManager = null)
        {
            return new ResizableParallelRunner();
        }

        /// <summary>
        /// Destroys the runner created by <see cref="Create"/>.
        /// </summary>
        /// <param name="runnerOpaque">Runner instance to destroy.</param>
        public static void Destroy(object? runnerOpaque)
        {
            if (runnerOpaque is ResizableParallelRunner runner)
            {
                runner.Dispose();
            }
        }

        /// <summary>
        /// Changes the number of threads for <paramref name="runnerOpaque"/>.
        /// </summary>
        /// <param name="runnerOpaque">The <see cref="ResizableParallelRunner"/> instance.</param>
        /// <param name="numThreads">Desired number of threads.</param>
        public static void SetThreads(object? runnerOpaque, nuint numThreads)
        {
            if (runnerOpaque is ResizableParallelRunner runner)
            {
                runner.SetNumThreads(numThreads);
            }
        }

        /// <summary>
        /// Suggests a number of threads to use for an image of given size.
        /// </summary>
        /// <param name="xsize">Image width in pixels.</param>
        /// <param name="ysize">Image height in pixels.</param>
        /// <returns>Suggested thread count (~one thread per 256x256 group).</returns>
        public static uint SuggestThreads(ulong xsize, ulong ysize)
        {
            ulong hw = (ulong)Math.Max(1, Environment.ProcessorCount);

            return (uint)Math.Min(hw, (xsize * ysize) / (256UL * 256UL));
        }

        /// <summary>
        /// <see cref="JxlParallelRunner"/> callback implementation.
        /// </summary>
        /// <param name="runnerOpaque">The <see cref="ResizableParallelRunner"/> instance.</param>
        /// <param name="jpegxlOpaque">Opaque state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
        /// <param name="init">Initialization callback.</param>
        /// <param name="func">Worker callback.</param>
        /// <param name="startRange">Inclusive start of range.</param>
        /// <param name="endRange">Exclusive end of range.</param>
        /// <returns><see cref="DataParallel.ParallelRetSuccess"/> on success; error code otherwise.</returns>
        public static int Runner(
            object? runnerOpaque,
            object? jpegxlOpaque,
            JxlParallelRunInit init,
            JxlParallelRunFunction func,
            uint startRange,
            uint endRange)
        {
            if (runnerOpaque is not ResizableParallelRunner runner)
            {
                return DataParallel.ParallelRetRunnerError;
            }

            return runner.Run(jpegxlOpaque, init, func, startRange, endRange);
        }

        /// <summary>
        /// Changes the number of threads used by this runner (including the calling thread).
        /// </summary>
        /// <param name="num">Total number of threads.</param>
        public void SetNumThreads(nuint num)
        {
            if (num > 0)
            {
                num -= 1;
            }

            lock (_stateMutex)
            {
                _numDesiredWorkers = num;
                Monitor.PulseAll(_stateMutex);
            }

            int targetWorkers = (int)num;

            if (_workers.Count < targetWorkers)
            {
                for (int i = _workers.Count; i < targetWorkers; i++)
                {
                    nuint workerId = (nuint)i;
                    Thread worker  = new Thread(() => WorkerBody(workerId))
                    {
                        IsBackground = true,
                        Name         = $"JxlResizableWorker-{workerId}",
                    };

                    _workers.Add(worker);
                    worker.Start();
                }
            }

            if (_workers.Count > targetWorkers)
            {
                for (int i = targetWorkers; i < _workers.Count; i++)
                {
                    _workers[i].Join();
                }

                _workers.RemoveRange(targetWorkers, _workers.Count - targetWorkers);
            }
        }

        /// <summary>
        /// Executes the parallel range <c>[start, end)</c>.
        /// </summary>
        /// <param name="jxlOpaque">Opaque state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
        /// <param name="init">Initialization callback.</param>
        /// <param name="func">Worker callback.</param>
        /// <param name="start">Inclusive start of range.</param>
        /// <param name="end">Exclusive end of range.</param>
        /// <returns><see cref="DataParallel.ParallelRetSuccess"/> on success; error code otherwise.</returns>
        public int Run(
            object? jxlOpaque,
            JxlParallelRunInit init,
            JxlParallelRunFunction func,
            uint start,
            uint end)
        {
            if (start > end)
            {
                return DataParallel.ParallelRetRunnerError;
            }

            if (start == end)
            {
                return DataParallel.ParallelRetSuccess;
            }

            if (start + 1 == end)
            {
                int singleRet = init(jxlOpaque, 1);

                if (singleRet != 0)
                {
                    return singleRet;
                }

                func(jxlOpaque, start, 0);

                return singleRet;
            }

            nuint numWorkers = Math.Min((nuint)_workers.Count + 1, end - start);
            int ret          = init(jxlOpaque, numWorkers);

            if (ret != 0)
            {
                return ret;
            }

            lock (_stateMutex)
            {
                // Avoid waking up more workers than needed.
                _maxRunningWorkers = end - start - 1;
                Volatile.Write(ref _nextTask, (int)start);
                _endTask      = end;
                _func         = func;
                _jxlOpaque    = jxlOpaque;
                _workAvailable = true;
                _numRunningWorkers++;
                Monitor.PulseAll(_stateMutex);
            }

            DequeueTasks(0);

            lock (_stateMutex)
            {
                while (_numRunningWorkers != 0)
                {
                    Monitor.Wait(_stateMutex);
                }

                _func = null;
                _jxlOpaque = null;
            }

            return ret;
        }

        /// <summary>
        /// Creates a <see cref="JxlSharp.Base.ThreadPool"/> bound to this runner.
        /// </summary>
        /// <returns>Configured <see cref="JxlSharp.Base.ThreadPool"/>.</returns>
        public JxlSharp.Base.ThreadPool CreateThreadPool()
        {
            return new JxlSharp.Base.ThreadPool(Runner, this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SetNumThreads(0);
        }

        private void WorkerBody(nuint workerId)
        {
            while (true)
            {
                lock (_stateMutex)
                {
                    // Worker pool was reduced, resize down.
                    if (workerId >= _numDesiredWorkers)
                    {
                        return;
                    }

                    // Nothing to do this time.
                    if (!_workAvailable || workerId >= _maxRunningWorkers)
                    {
                        Monitor.Wait(_stateMutex);
                        continue;
                    }

                    _numRunningWorkers++;
                }

                DequeueTasks(workerId + 1);
            }
        }

        private void DequeueTasks(nuint threadId)
        {
            while (true)
            {
                uint task = (uint)Interlocked.Increment(ref _nextTask) - 1u;

                if (task >= _endTask)
                {
                    lock (_stateMutex)
                    {
                        _numRunningWorkers--;
                        _workAvailable = false;

                        if (_numRunningWorkers == 0)
                        {
                            Monitor.PulseAll(_stateMutex);
                        }
                    }

                    break;
                }

                _func!(_jxlOpaque, task, threadId);
            }
        }
    }
}
