// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Threading;

namespace JxlSharp.Base
{
    /// <summary>
    /// Parallel run initialization callback.
    /// This function MUST be called by the <see cref="JxlParallelRunner"/> only once, on the
    /// same thread that called <see cref="JxlParallelRunner"/>, before any parallel execution.
    /// </summary>
    /// <param name="jpegxlOpaque">The <c>jpegxlOpaque</c> handle provided to <see cref="JxlParallelRunner"/>.</param>
    /// <param name="numThreads">The maximum number of threads. This value must be positive.</param>
    /// <returns><c>0</c> if the initialization process was successful; an error code otherwise.</returns>
    public delegate int JxlParallelRunInit(object? jpegxlOpaque, nuint numThreads);

    /// <summary>
    /// Parallel run data processing callback.
    /// This function MUST be called once for every number in the range <c>[startRange, endRange)</c>.
    /// </summary>
    /// <param name="jpegxlOpaque">The <c>jpegxlOpaque</c> handle provided to <see cref="JxlParallelRunner"/>.</param>
    /// <param name="value">The number in the range <c>[startRange, endRange)</c> of the call.</param>
    /// <param name="threadId">The thread number where this function is being called from.</param>
    public delegate void JxlParallelRunFunction(object? jpegxlOpaque, uint value, nuint threadId);

    /// <summary>
    /// JxlParallelRunner function type. A parallel runner implementation can be
    /// provided by a JPEG XL caller to allow running computations in multiple threads.
    /// </summary>
    /// <param name="runnerOpaque">Caller-supplied runner state.</param>
    /// <param name="jpegxlOpaque">Internal JPEG XL call state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
    /// <param name="init">Initialization callback called once on the calling thread.</param>
    /// <param name="func">Worker callback invoked for each value in <c>[startRange, endRange)</c>.</param>
    /// <param name="startRange">Inclusive start of the task range.</param>
    /// <param name="endRange">Exclusive end of the task range.</param>
    /// <returns><see cref="DataParallel.ParallelRetSuccess"/> (<c>0</c>) on success; error code otherwise.</returns>
    public delegate int JxlParallelRunner(
        object? runnerOpaque,
        object? jpegxlOpaque,
        JxlParallelRunInit init,
        JxlParallelRunFunction func,
        uint startRange,
        uint endRange);

    /// <summary>
    /// Marker struct used when no thread pool initialization callback is needed.
    /// </summary>
    public readonly struct ThreadPoolNoInit
    {
    }

    /// <summary>
    /// Portable, low-overhead ThreadPool wrapper for data-parallel computations
    /// (ported from <c>lib/jxl/base/data_parallel.h</c>).
    /// </summary>
    public sealed class ThreadPool
    {
        private readonly JxlParallelRunner? _runner;
        private readonly object? _runnerOpaque;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadPool"/> class.
        /// </summary>
        /// <param name="runner">Custom parallel runner callback, or <c>null</c> for sequential execution.</param>
        /// <param name="runnerOpaque">Opaque state passed to <paramref name="runner"/>.</param>
        public ThreadPool(JxlParallelRunner? runner, object? runnerOpaque)
        {
            _runner       = runner;
            _runnerOpaque = runner is not null ? runnerOpaque : this;
        }

        /// <summary>
        /// Use this as <c>initFunc</c> when no initialization is needed.
        /// </summary>
        public static ThreadPoolNoInit NoInit => default;

        /// <summary>
        /// Gets the underlying <see cref="JxlParallelRunner"/> callback.
        /// </summary>
        public JxlParallelRunner? Runner => _runner;

        /// <summary>
        /// Gets the opaque state associated with the runner.
        /// </summary>
        public object? RunnerOpaque => _runnerOpaque;

        /// <summary>
        /// Runs <paramref name="initFunc"/>(<c>numThreads</c>) followed by <paramref name="dataFunc"/>(<c>task, thread</c>)
        /// on worker thread(s) for every task in <c>[begin, end)</c>.
        /// </summary>
        /// <param name="begin">Inclusive start of range.</param>
        /// <param name="end">Exclusive end of range.</param>
        /// <param name="initFunc">Initialization callback receiving thread count.</param>
        /// <param name="dataFunc">Per-item worker callback receiving <c>(task, threadId)</c>.</param>
        /// <param name="caller">Optional caller description for diagnostics.</param>
        /// <returns><see cref="Status.OkStatus"/> if all tasks succeeded; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status Run(
            uint begin,
            uint end,
            Func<nuint, Status> initFunc,
            Func<uint, nuint, Status> dataFunc,
            string caller = "")
        {
            if (begin > end)
            {
                return Status.Failure("begin > end");
            }

            if (begin == end)
            {
                return true;
            }

            if (_runner is null)
            {
                return RunSerial(begin, end, initFunc, dataFunc, caller);
            }

            RunCallState callState = new RunCallState(initFunc, dataFunc);
            int ret = _runner(
                _runnerOpaque,
                callState,
                RunCallState.CallInitFunc,
                RunCallState.CallDataFunc,
                begin,
                end);

            if (ret != DataParallel.ParallelRetSuccess || callState.HasError)
            {
                return Status.Failure($"[{caller}] failed");
            }

            return true;
        }

        internal static Status RunSerial(uint begin, uint end, Func<nuint, Status> initFunc,
            Func<uint, nuint, Status> dataFunc, string caller)
        {
            if (begin > end) return Status.Failure("begin > end");
            if (begin == end) return true;
            if (!initFunc(1)) return Status.Failure("Failed to initialize thread");
            for (uint i = begin; i < end; i++)
            {
                if (!dataFunc(i, 0)) return Status.Failure($"[{caller}] failed");
            }

            return true;
        }

        private sealed class RunCallState
        {
            private readonly Func<nuint, Status> _initFunc;
            private readonly Func<uint, nuint, Status> _dataFunc;
            private int _hasError;

            public RunCallState(Func<nuint, Status> initFunc, Func<uint, nuint, Status> dataFunc)
            {
                _initFunc = initFunc;
                _dataFunc = dataFunc;
            }

            public bool HasError => Volatile.Read(ref _hasError) != 0;

            public static int CallInitFunc(object? jpegxlOpaque, nuint numThreads)
            {
                RunCallState self = (RunCallState)jpegxlOpaque!;

                // Returns -1 when the internal init function returns false Status to
                // indicate an error.
                if (!self._initFunc(numThreads))
                {
                    Interlocked.Exchange(ref self._hasError, 1);

                    return DataParallel.ParallelRetRunnerError;
                }

                return DataParallel.ParallelRetSuccess;
            }

            public static void CallDataFunc(object? jpegxlOpaque, uint value, nuint threadId)
            {
                RunCallState self = (RunCallState)jpegxlOpaque!;

                if (self.HasError)
                {
                    return;
                }

                if (!self._dataFunc(value, threadId))
                {
                    Interlocked.Exchange(ref self._hasError, 1);
                }
            }
        }
    }

    /// <summary>
    /// Static helper functions and constants for data-parallel execution
    /// (ported from <c>include/jxl/parallel_runner.h</c> and <c>lib/jxl/base/data_parallel.h</c>).
    /// </summary>
    public static class DataParallel
    {
        /// <summary>
        /// Code returned by the <see cref="JxlParallelRunInit"/> function to indicate success.
        /// </summary>
        public const int ParallelRetSuccess = 0;

        /// <summary>
        /// Code returned by the <see cref="JxlParallelRunInit"/> function to indicate a general error.
        /// </summary>
        public const int ParallelRetRunnerError = -1;

        /// <summary>
        /// Runs <paramref name="initFunc"/> and <paramref name="dataFunc"/> on <paramref name="pool"/>
        /// (or sequentially if <paramref name="pool"/> is <c>null</c>).
        /// </summary>
        /// <param name="pool">ThreadPool instance, or <c>null</c> for sequential fallback.</param>
        /// <param name="begin">Inclusive start of range.</param>
        /// <param name="end">Exclusive end of range.</param>
        /// <param name="initFunc">Initialization function receiving number of threads.</param>
        /// <param name="dataFunc">Data processing function receiving <c>(task, threadId)</c>.</param>
        /// <param name="caller">Caller identifier.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status RunOnPool(
            ThreadPool? pool,
            uint begin,
            uint end,
            Func<nuint, Status> initFunc,
            Func<uint, nuint, Status> dataFunc,
            string caller = "")
        {
            if (pool is null)
            {
                return ThreadPool.RunSerial(begin, end, initFunc, dataFunc, caller);
            }

            return pool.Run(begin, end, initFunc, dataFunc, caller);
        }

        /// <summary>
        /// Runs <paramref name="dataFunc"/> on <paramref name="pool"/> without an initialization step.
        /// </summary>
        /// <param name="pool">ThreadPool instance, or <c>null</c> for sequential fallback.</param>
        /// <param name="begin">Inclusive start of range.</param>
        /// <param name="end">Exclusive end of range.</param>
        /// <param name="noInitFunc">No-init marker value.</param>
        /// <param name="dataFunc">Data processing function receiving <c>(task, threadId)</c>.</param>
        /// <param name="caller">Caller identifier.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public static Status RunOnPool(
            ThreadPool? pool,
            uint begin,
            uint end,
            ThreadPoolNoInit noInitFunc,
            Func<uint, nuint, Status> dataFunc,
            string caller = "")
        {
            return RunOnPool(pool, begin, end, static _ => true, dataFunc, caller);
        }
    }
}
