// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Threading;
using JxlSharp.Base;

namespace JxlSharp.Threads
{
    /// <summary>
    /// Implementation of <see cref="JxlParallelRunner"/> that can be used to enable
    /// multithreading when using the JPEG XL library. The number of threads created
    /// is fixed at construction time and the threads are re-used for every
    /// <see cref="Runner"/> call.
    /// Ported from <c>lib/threads/thread_parallel_runner.cc</c> and <c>lib/threads/thread_parallel_runner_internal.cc</c>.
    /// </summary>
    public sealed class ThreadParallelRunner : IDisposable
    {
        private const ulong WorkerWait = ~1UL;
        private const ulong WorkerOnce = ~2UL;
        private const ulong WorkerExit = ~3UL;

        private readonly Thread[] _threads;
        private readonly uint _numWorkerThreads;
        private readonly uint _numThreads;
        private readonly object _mutex = new object();

        private int _depth;
        private uint _workersReady;
        private ulong _workerStartCommand;
        private ulong _generation;
        private JxlParallelRunFunction? _dataFunc;
        private object? _jpegxlOpaque;
        private int _numReserved;
        private uint _activeWorkers;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadParallelRunner"/> class.
        /// Starts the given number of worker threads and blocks until they are ready.
        /// If zero, all tasks run on the main thread.
        /// </summary>
        /// <param name="numWorkerThreads">Number of worker threads, or negative to use <see cref="DefaultNumWorkerThreads"/>.</param>
        public ThreadParallelRunner(int numWorkerThreads = -1)
        {
            int actualWorkers   = numWorkerThreads < 0 ? (int)DefaultNumWorkerThreads() : numWorkerThreads;
            _numWorkerThreads   = (uint)Math.Max(0, actualWorkers);
            _numThreads         = Math.Max(_numWorkerThreads, 1u);
            _workerStartCommand = WorkerWait;
            _threads            = new Thread[_numWorkerThreads];

            for (uint i = 0; i < _numWorkerThreads; ++i)
            {
                int threadIndex = (int)i;
                Thread worker   = new Thread(() => ThreadFunc(this, threadIndex))
                {
                    IsBackground = true,
                    Name         = $"JxlWorker-{threadIndex}",
                };

                _threads[i] = worker;
                worker.Start();
            }

            if (_numWorkerThreads != 0)
            {
                WorkersReadyBarrier();
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="JxlMemoryManager"/> associated with this runner.
        /// </summary>
        public JxlMemoryManager MemoryManager { get; set; } = JxlMemoryManager.Default;

        /// <summary>
        /// Gets the maximum number of main/worker threads that may call <c>func</c>.
        /// Useful for allocating per-thread storage.
        /// </summary>
        public nuint NumThreads => _numThreads;

        /// <summary>
        /// Returns a default <c>numWorkerThreads</c> value for <see cref="Create"/>.
        /// </summary>
        /// <returns>Processor count of the current machine.</returns>
        public static nuint DefaultNumWorkerThreads()
        {
            return (nuint)Math.Max(1, Environment.ProcessorCount);
        }

        /// <summary>
        /// Creates a new <see cref="ThreadParallelRunner"/> instance.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="numWorkerThreads">Number of worker threads.</param>
        /// <returns>Initialized <see cref="ThreadParallelRunner"/>, or <c>null</c> if memory manager initialization fails.</returns>
        public static ThreadParallelRunner? Create(JxlMemoryManager? memoryManager, nuint numWorkerThreads)
        {
            JxlMemoryManager localMm = new JxlMemoryManager();

            if (!JxlMemoryManager.Init(localMm, memoryManager))
            {
                return null;
            }

            return new ThreadParallelRunner((int)numWorkerThreads)
            {
                MemoryManager = localMm,
            };
        }

        /// <summary>
        /// Destroys the runner created by <see cref="Create"/>.
        /// </summary>
        /// <param name="runnerOpaque">Runner instance to destroy.</param>
        public static void Destroy(object? runnerOpaque)
        {
            if (runnerOpaque is ThreadParallelRunner runner)
            {
                runner.Dispose();
            }
        }

        /// <summary>
        /// <see cref="JxlParallelRunner"/> callback implementation.
        /// </summary>
        /// <param name="runnerOpaque">The <see cref="ThreadParallelRunner"/> instance.</param>
        /// <param name="jpegxlOpaque">Opaque state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
        /// <param name="init">Initialization callback.</param>
        /// <param name="func">Per-task worker callback.</param>
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
            return Run(runnerOpaque, jpegxlOpaque, init, func, startRange, endRange, limitToTasks: false);
        }

        // Used by the facade; public Runner keeps its configured-thread initialization contract.
        internal static int RunnerWithTaskLimit(object? runnerOpaque, object? jpegxlOpaque,
            JxlParallelRunInit init, JxlParallelRunFunction func, uint startRange, uint endRange)
        {
            return Run(runnerOpaque, jpegxlOpaque, init, func, startRange, endRange, limitToTasks: true);
        }

        private static int Run(object? runnerOpaque, object? jpegxlOpaque,
            JxlParallelRunInit init, JxlParallelRunFunction func, uint startRange, uint endRange, bool limitToTasks)
        {
            if (runnerOpaque is not ThreadParallelRunner self)
            {
                return DataParallel.ParallelRetRunnerError;
            }

            if (startRange > endRange)
            {
                return DataParallel.ParallelRetRunnerError;
            }

            if (startRange == endRange)
            {
                return DataParallel.ParallelRetSuccess;
            }

            uint activeWorkers = limitToTasks ? Math.Min(self._numWorkerThreads, endRange - startRange) : self._numWorkerThreads;
            int ret = init(jpegxlOpaque, Math.Max((nuint)activeWorkers, 1u));

            if (ret != DataParallel.ParallelRetSuccess)
            {
                return ret;
            }

            // Use a sequential run when num_worker_threads_ is zero since we have no
            // worker threads.
            if (self._numWorkerThreads == 0)
            {
                for (uint task = startRange; task < endRange; ++task)
                {
                    func(jpegxlOpaque, task, 0);
                }

                return DataParallel.ParallelRetSuccess;
            }

            return self.ExecuteParallelRange(jpegxlOpaque, func, startRange, endRange, activeWorkers);
        }

        /// <summary>
        /// Runs <paramref name="func"/>(<c>thread, thread</c>) on all thread(s) that may participate in <see cref="Runner"/>.
        /// </summary>
        /// <param name="func">Callback to run once on each worker thread.</param>
        public void RunOnEachThread(Action<uint, nuint> func)
        {
            if (_numWorkerThreads == 0)
            {
                func(0, 0);

                return;
            }

            _dataFunc     = static (opaque, task, thread) => ((Action<uint, nuint>)opaque!)(task, thread);
            _jpegxlOpaque = func;
            StartWorkers(WorkerOnce);
            WorkersReadyBarrier();
            _dataFunc = null;
            _jpegxlOpaque = null;
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

            if (_numWorkerThreads != 0)
            {
                StartWorkers(WorkerExit);
            }

            foreach (Thread thread in _threads)
            {
                if (thread.IsAlive)
                {
                    thread.Join();
                }
            }
        }

        private static void RunRange(ThreadParallelRunner self, ulong command, int thread)
        {
            if ((uint)thread >= self._activeWorkers) return;
            uint begin            = (uint)(command >> 32);
            uint end              = (uint)(command & 0xFFFFFFFFu);
            uint numTasks         = end - begin;
            uint numWorkerThreads = self._activeWorkers;

            for (; ;)
            {
                // guided schedule
                uint numReserved  = (uint)Volatile.Read(ref self._numReserved);
                uint numRemaining = numTasks - Math.Min(numReserved, numTasks);
                uint mySize       = Math.Max(numRemaining / (numWorkerThreads * 4u), 1u);

                uint reservedAfter = (uint)Interlocked.Add(ref self._numReserved, (int)mySize);
                uint myBegin       = begin + reservedAfter - mySize;
                uint myEnd         = Math.Min(myBegin + mySize, begin + numTasks);

                // Another thread already reserved the last task.
                if (myBegin >= myEnd)
                {
                    break;
                }

                for (uint task = myBegin; task < myEnd; ++task)
                {
                    self._dataFunc!(self._jpegxlOpaque, task, (nuint)thread);
                }
            }
        }

        private static void ThreadFunc(ThreadParallelRunner self, int thread)
        {
            ulong generation = 0;

            for (; ;)
            {
                ulong command = self.WaitForNextCommand(ref generation);

                if (command == WorkerExit)
                {
                    return;
                }

                if (command == WorkerOnce)
                {
                    self._dataFunc!(self._jpegxlOpaque, (uint)thread, (nuint)thread);
                }
                else
                {
                    RunRange(self, command, thread);
                }
            }
        }

        private int ExecuteParallelRange(
            object? jpegxlOpaque,
            JxlParallelRunFunction func,
            uint startRange,
            uint endRange,
            uint activeWorkers)
        {
            if (Interlocked.Increment(ref _depth) != 1)
            {
                Interlocked.Decrement(ref _depth);

                return DataParallel.ParallelRetRunnerError; // Must not re-enter.
            }

            ulong workerCommand = ((ulong)startRange << 32) + endRange;

            if (workerCommand == WorkerWait || workerCommand == WorkerOnce || workerCommand == WorkerExit)
            {
                Interlocked.Decrement(ref _depth);

                return DataParallel.ParallelRetRunnerError;
            }

            _dataFunc     = func;
            _jpegxlOpaque = jpegxlOpaque;
            _activeWorkers = activeWorkers;
            Volatile.Write(ref _numReserved, 0);

            StartWorkers(workerCommand);
            WorkersReadyBarrier();
            _dataFunc = null;
            _jpegxlOpaque = null;

            if (Interlocked.Decrement(ref _depth) != 0)
            {
                return DataParallel.ParallelRetRunnerError;
            }

            return DataParallel.ParallelRetSuccess;
        }

        private ulong WaitForNextCommand(ref ulong generation)
        {
            lock (_mutex)
            {
                if (++_workersReady == _numThreads)
                {
                    Monitor.PulseAll(_mutex);
                }

                while (_generation == generation)
                {
                    Monitor.Wait(_mutex);
                }

                generation = _generation;

                return _workerStartCommand;
            }
        }

        private void WorkersReadyBarrier()
        {
            lock (_mutex)
            {
                while (_workersReady != _threads.Length)
                {
                    Monitor.Wait(_mutex);
                }

                _workersReady       = 0;
                _workerStartCommand = WorkerWait;
            }
        }

        private void StartWorkers(ulong workerCommand)
        {
            lock (_mutex)
            {
                _workerStartCommand = workerCommand;
                ++_generation;
                Monitor.PulseAll(_mutex);
            }
        }
    }
}
