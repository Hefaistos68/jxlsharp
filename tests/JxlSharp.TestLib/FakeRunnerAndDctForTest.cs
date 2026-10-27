// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Extras;
using JxlSharp.Threads;

namespace JxlSharp.TestLib
{
    /// <summary>
    /// Deterministic single-threaded parallel runner for unit tests that optionally
    /// shuffles task execution order and cycles simulated thread indices.
    /// Ported from <c>lib/jxl/fake_parallel_runner_testonly.h</c>.
    /// </summary>
    public sealed class FakeParallelRunner : IParallelRunner
    {
        private readonly nuint _maxThreads;
        private readonly uint _orderSeed;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeParallelRunner"/> class.
        /// </summary>
        /// <param name="orderSeed">PRNG seed for shuffling task execution order (0 preserves sequential order).</param>
        /// <param name="numThreads">Number of simulated worker threads (clamped to at least 1).</param>
        public FakeParallelRunner(uint orderSeed = 0, nuint numThreads = 8)
        {
            _maxThreads = Math.Max(numThreads, (nuint)1);
            _orderSeed  = orderSeed;
        }

        /// <inheritdoc />
        JxlParallelRunner? IParallelRunner.Runner => Runner;

        /// <inheritdoc />
        public object? RunnerOpaque => this;

        /// <summary>
        /// Static <see cref="JxlParallelRunner"/> callback matching <c>FakeParallelRunner::Runner</c>.
        /// </summary>
        /// <param name="runnerOpaque">Instance of <see cref="FakeParallelRunner"/>.</param>
        /// <param name="jpegxlOpaque">Caller state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
        /// <param name="init">Initialization callback receiving the maximum thread count.</param>
        /// <param name="func">Per-task execution callback.</param>
        /// <param name="startRange">Inclusive start of task range.</param>
        /// <param name="endRange">Exclusive end of task range.</param>
        /// <returns><c>0</c> on success, or the non-zero error code returned by <paramref name="init"/>.</returns>
        public static int Runner(
            object? runnerOpaque,
            object? jpegxlOpaque,
            JxlParallelRunInit init,
            JxlParallelRunFunction func,
            uint startRange,
            uint endRange)
        {
            if (runnerOpaque is FakeParallelRunner self)
            {
                return self.Run(jpegxlOpaque, init, func, startRange, endRange);
            }

            FakeParallelRunner fallback = new FakeParallelRunner();

            return fallback.Run(jpegxlOpaque, init, func, startRange, endRange);
        }

        /// <summary>
        /// Executes the parallel range synchronously, shuffling task order when <c>orderSeed != 0</c>.
        /// </summary>
        /// <param name="jpegxlOpaque">Caller state passed to <paramref name="init"/> and <paramref name="func"/>.</param>
        /// <param name="init">Initialization callback.</param>
        /// <param name="func">Task body callback.</param>
        /// <param name="startRange">Inclusive start of task range.</param>
        /// <param name="endRange">Exclusive end of task range.</param>
        /// <returns><c>0</c> on success, or the non-zero error code returned by <paramref name="init"/>.</returns>
        public int Run(
            object? jpegxlOpaque,
            JxlParallelRunInit init,
            JxlParallelRunFunction func,
            uint startRange,
            uint endRange)
        {
            int ret = init(jpegxlOpaque, _maxThreads);

            if (ret != 0)
            {
                return ret;
            }

            if (endRange <= startRange)
            {
                return 0;
            }

            int count    = (int)(endRange - startRange);
            uint[] tasks = new uint[count];

            for (int i = 0; i < count; i++)
            {
                tasks[i] = startRange + (uint)i;
            }

            if (_orderSeed != 0)
            {
                Rng rng = new Rng(_orderSeed);

                rng.Shuffle(tasks.AsSpan());
            }

            nuint thread = 0;

            for (int i = 0; i < count; i++)
            {
                func(jpegxlOpaque, tasks[i], thread);
                thread = (thread == _maxThreads - 1) ? 0 : (thread + 1);
            }

            return 0;
        }

        /// <summary>
        /// Creates a <see cref="ThreadPool"/> backed by this <see cref="FakeParallelRunner"/>.
        /// </summary>
        /// <returns>A new <see cref="ThreadPool"/> wrapping this runner.</returns>
        public ThreadPool CreateThreadPool()
        {
            return new ThreadPool(Runner, this);
        }
    }

    /// <summary>
    /// Helper wrapper around <see cref="ThreadParallelRunner"/> and <see cref="ThreadPool"/> for unit tests.
    /// Ported from <c>jxl::test::ThreadPoolForTests</c> in <c>lib/jxl/test_utils.h</c>.
    /// </summary>
    public sealed class ThreadPoolForTests : IDisposable, IParallelRunner
    {
        private readonly ThreadParallelRunner _runner;
        private readonly ThreadPool _pool;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadPoolForTests"/> class.
        /// </summary>
        /// <param name="numThreads">Number of worker threads.</param>
        public ThreadPoolForTests(int numThreads = 4)
        {
            _runner = new ThreadParallelRunner(Math.Max(0, numThreads));
            _pool   = _runner.CreateThreadPool();
        }

        /// <summary>Gets the underlying <see cref="ThreadPool"/>.</summary>
        public ThreadPool Pool => _pool;

        /// <summary>Gets the underlying <see cref="ThreadParallelRunner"/>.</summary>
        public ThreadParallelRunner ThreadRunner => _runner;

        /// <inheritdoc />
        JxlParallelRunner? IParallelRunner.Runner => ThreadParallelRunner.Runner;

        /// <inheritdoc />
        public object? RunnerOpaque => _runner;

        /// <summary>
        /// Returns the underlying <see cref="ThreadPool"/> instance (matching C++ <c>get()</c>).
        /// </summary>
        /// <returns>The <see cref="ThreadPool"/>.</returns>
        public ThreadPool Get()
        {
            return _pool;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _runner.Dispose();
        }
    }

    /// <summary>
    /// Unoptimized reference 1D and 2D Discrete Cosine Transform (DCT-II / IDCT) routines for unit tests.
    /// Ported from <c>lib/jxl/dct_for_test.h</c>.
    /// </summary>
    public static class DctForTest
    {
        private const double KInvSqrt2 = 0.7071067811865475;

        /// <summary>
        /// Computes the <paramref name="n"/>-point DCT-II across <paramref name="m"/> columns of <paramref name="block"/>
        /// (<c>n * m</c> elements), divided by <c>sqrt(n)</c>, storing the result in <paramref name="output"/>.
        /// Ported from <c>jxl::test::DCT1D</c> in <c>lib/jxl/dct_for_test.h</c>.
        /// </summary>
        /// <param name="block">Input block of size <c>n * m</c>.</param>
        /// <param name="output">Output block of size <c>n * m</c>.</param>
        /// <param name="n">DCT transform length (number of rows).</param>
        /// <param name="m">Number of columns (defaults to 1 for a 1D vector).</param>
        public static void Dct1D(ReadOnlySpan<double> block, Span<double> output, int n, int m = 1)
        {
            double[] matrix = BuildDctMatrix(n);

            MultiplyMatrixBlock(matrix, block, output, n, m);
        }

        /// <summary>
        /// Single-precision overload of <see cref="Dct1D(ReadOnlySpan{double}, Span{double}, int, int)"/>.
        /// </summary>
        /// <param name="block">Input block of size <c>n * m</c>.</param>
        /// <param name="output">Output block of size <c>n * m</c>.</param>
        /// <param name="n">DCT transform length (number of rows).</param>
        /// <param name="m">Number of columns (defaults to 1 for a 1D vector).</param>
        public static void Dct1D(ReadOnlySpan<float> block, Span<float> output, int n, int m = 1)
        {
            int total       = n * m;
            double[] inBuf  = ConvertFloatToDouble(block, total);
            double[] outBuf = new double[total];

            Dct1D(inBuf, outBuf, n, m);
            ConvertDoubleToFloat(outBuf, output, total);
        }

        /// <summary>
        /// Computes the <paramref name="n"/>-point IDCT across <paramref name="m"/> columns of <paramref name="block"/>
        /// (<c>n * m</c> elements), multiplied by <c>sqrt(n)</c>, storing the result in <paramref name="output"/>.
        /// Ported from <c>jxl::test::IDCT1D</c> in <c>lib/jxl/dct_for_test.h</c>.
        /// </summary>
        /// <param name="block">Input frequency block of size <c>n * m</c>.</param>
        /// <param name="output">Output spatial block of size <c>n * m</c>.</param>
        /// <param name="n">IDCT transform length (number of rows).</param>
        /// <param name="m">Number of columns (defaults to 1 for a 1D vector).</param>
        public static void Idct1D(ReadOnlySpan<double> block, Span<double> output, int n, int m = 1)
        {
            double[] matrix = BuildIdctMatrix(n);

            MultiplyMatrixBlock(matrix, block, output, n, m);
        }

        /// <summary>
        /// Single-precision overload of <see cref="Idct1D(ReadOnlySpan{double}, Span{double}, int, int)"/>.
        /// </summary>
        /// <param name="block">Input frequency block of size <c>n * m</c>.</param>
        /// <param name="output">Output spatial block of size <c>n * m</c>.</param>
        /// <param name="n">IDCT transform length (number of rows).</param>
        /// <param name="m">Number of columns (defaults to 1 for a 1D vector).</param>
        public static void Idct1D(ReadOnlySpan<float> block, Span<float> output, int n, int m = 1)
        {
            int total       = n * m;
            double[] inBuf  = ConvertFloatToDouble(block, total);
            double[] outBuf = new double[total];

            Idct1D(inBuf, outBuf, n, m);
            ConvertDoubleToFloat(outBuf, output, total);
        }

        /// <summary>
        /// Transposes an <c>n x m</c> row-major block into an <c>m x n</c> row-major block.
        /// Ported from <c>jxl::test::TransposeBlock</c> in <c>lib/jxl/dct_for_test.h</c>.
        /// </summary>
        /// <param name="input">Input matrix with <paramref name="n"/> rows and <paramref name="m"/> columns.</param>
        /// <param name="output">Output matrix with <paramref name="m"/> rows and <paramref name="n"/> columns.</param>
        /// <param name="n">Number of rows in <paramref name="input"/>.</param>
        /// <param name="m">Number of columns in <paramref name="input"/>.</param>
        public static void TransposeBlock(ReadOnlySpan<double> input, Span<double> output, int n, int m)
        {
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < m; y++)
                {
                    output[(y * n) + x] = input[(x * m) + y];
                }
            }
        }

        /// <summary>
        /// Transposes an <c>n x m</c> single-precision row-major block into an <c>m x n</c> block.
        /// </summary>
        /// <param name="input">Input matrix with <paramref name="n"/> rows and <paramref name="m"/> columns.</param>
        /// <param name="output">Output matrix with <paramref name="m"/> rows and <paramref name="n"/> columns.</param>
        /// <param name="n">Number of rows in <paramref name="input"/>.</param>
        /// <param name="m">Number of columns in <paramref name="input"/>.</param>
        public static void TransposeBlock(ReadOnlySpan<float> input, Span<float> output, int n, int m)
        {
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < m; y++)
                {
                    output[(y * n) + x] = input[(x * m) + y];
                }
            }
        }

        /// <summary>
        /// Computes the in-place untransposed 2D DCT of an <c>n x n</c> block.
        /// Ported from <c>jxl::DCTSlow</c> in <c>lib/jxl/dct_for_test.h</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void ComputeDct2D(Span<double> block, int n)
        {
            int blockSize = n * n;
            double[] g    = new double[blockSize];

            Dct1D(block, g, n, n);
            TransposeBlock(g, block, n, n);
            Dct1D(block, g, n, n);
            TransposeBlock(g, block, n, n);
        }

        /// <summary>
        /// Computes the untransposed 2D DCT of an <c>n x m</c> block from <paramref name="input"/> into <paramref name="output"/>.
        /// </summary>
        /// <param name="input">Input block of length <c>n * m</c>.</param>
        /// <param name="output">Output block of length <c>n * m</c>.</param>
        /// <param name="n">Number of rows.</param>
        /// <param name="m">Number of columns.</param>
        public static void ComputeDct2D(ReadOnlySpan<double> input, Span<double> output, int n, int m)
        {
            int blockSize = n * m;
            double[] g    = new double[blockSize];
            double[] t    = new double[blockSize];

            Dct1D(input, g, n, m);
            TransposeBlock(g, t, n, m);
            Dct1D(t, g, m, n);
            TransposeBlock(g, output, m, n);
        }

        /// <summary>
        /// Single-precision overload of <see cref="ComputeDct2D(Span{double}, int)"/>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void ComputeDct2D(Span<float> block, int n)
        {
            int total    = n * n;
            double[] buf = ConvertFloatToDouble(block, total);

            ComputeDct2D(buf, n);
            ConvertDoubleToFloat(buf, block, total);
        }

        /// <summary>
        /// Single-precision overload of <see cref="ComputeDct2D(ReadOnlySpan{double}, Span{double}, int, int)"/>.
        /// </summary>
        /// <param name="input">Input block of length <c>n * m</c>.</param>
        /// <param name="output">Output block of length <c>n * m</c>.</param>
        /// <param name="n">Number of rows.</param>
        /// <param name="m">Number of columns.</param>
        public static void ComputeDct2D(ReadOnlySpan<float> input, Span<float> output, int n, int m)
        {
            int total       = n * m;
            double[] inBuf  = ConvertFloatToDouble(input, total);
            double[] outBuf = new double[total];

            ComputeDct2D(inBuf, outBuf, n, m);
            ConvertDoubleToFloat(outBuf, output, total);
        }

        /// <summary>
        /// Alias for <see cref="ComputeDct2D(Span{double}, int)"/> matching C++ <c>jxl::DCTSlow</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void DctSlow(Span<double> block, int n)
        {
            ComputeDct2D(block, n);
        }

        /// <summary>
        /// Alias for <see cref="ComputeDct2D(Span{float}, int)"/> matching C++ <c>jxl::DCTSlow</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void DctSlow(Span<float> block, int n)
        {
            ComputeDct2D(block, n);
        }

        /// <summary>
        /// Computes the in-place untransposed 2D IDCT of an <c>n x n</c> block.
        /// Ported from <c>jxl::IDCTSlow</c> in <c>lib/jxl/dct_for_test.h</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void ComputeIdct2D(Span<double> block, int n)
        {
            int blockSize = n * n;
            double[] g    = new double[blockSize];

            Idct1D(block, g, n, n);
            TransposeBlock(g, block, n, n);
            Idct1D(block, g, n, n);
            TransposeBlock(g, block, n, n);
        }

        /// <summary>
        /// Computes the untransposed 2D IDCT of an <c>n x m</c> block from <paramref name="input"/> into <paramref name="output"/>.
        /// </summary>
        /// <param name="input">Input block of length <c>n * m</c>.</param>
        /// <param name="output">Output block of length <c>n * m</c>.</param>
        /// <param name="n">Number of rows.</param>
        /// <param name="m">Number of columns.</param>
        public static void ComputeIdct2D(ReadOnlySpan<double> input, Span<double> output, int n, int m)
        {
            int blockSize = n * m;
            double[] g    = new double[blockSize];
            double[] t    = new double[blockSize];

            Idct1D(input, g, n, m);
            TransposeBlock(g, t, n, m);
            Idct1D(t, g, m, n);
            TransposeBlock(g, output, m, n);
        }

        /// <summary>
        /// Single-precision overload of <see cref="ComputeIdct2D(Span{double}, int)"/>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void ComputeIdct2D(Span<float> block, int n)
        {
            int total    = n * n;
            double[] buf = ConvertFloatToDouble(block, total);

            ComputeIdct2D(buf, n);
            ConvertDoubleToFloat(buf, block, total);
        }

        /// <summary>
        /// Single-precision overload of <see cref="ComputeIdct2D(ReadOnlySpan{double}, Span{double}, int, int)"/>.
        /// </summary>
        /// <param name="input">Input block of length <c>n * m</c>.</param>
        /// <param name="output">Output block of length <c>n * m</c>.</param>
        /// <param name="n">Number of rows.</param>
        /// <param name="m">Number of columns.</param>
        public static void ComputeIdct2D(ReadOnlySpan<float> input, Span<float> output, int n, int m)
        {
            int total       = n * m;
            double[] inBuf  = ConvertFloatToDouble(input, total);
            double[] outBuf = new double[total];

            ComputeIdct2D(inBuf, outBuf, n, m);
            ConvertDoubleToFloat(outBuf, output, total);
        }

        /// <summary>
        /// Alias for <see cref="ComputeIdct2D(Span{double}, int)"/> matching C++ <c>jxl::IDCTSlow</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void IdctSlow(Span<double> block, int n)
        {
            ComputeIdct2D(block, n);
        }

        /// <summary>
        /// Alias for <see cref="ComputeIdct2D(Span{float}, int)"/> matching C++ <c>jxl::IDCTSlow</c>.
        /// </summary>
        /// <param name="block">In-place block of length <c>n * n</c>.</param>
        /// <param name="n">Block dimension.</param>
        public static void IdctSlow(Span<float> block, int n)
        {
            ComputeIdct2D(block, n);
        }

        private static double Alpha(int u)
        {
            return u == 0 ? KInvSqrt2 : 1.0;
        }

        private static double[] BuildDctMatrix(int n)
        {
            double[] matrix = new double[n * n];
            double scale    = Math.Sqrt(2.0) / n;
            double invNPi   = Math.PI / n;

            for (int y = 0; y < n; y++)
            {
                for (int u = 0; u < n; u++)
                {
                    matrix[(n * u) + y] = Alpha(u) * Math.Cos((y + 0.5) * u * invNPi) * scale;
                }
            }

            return matrix;
        }

        private static double[] BuildIdctMatrix(int n)
        {
            double[] matrix = new double[n * n];
            double scale    = Math.Sqrt(2.0);
            double invNPi   = Math.PI / n;

            for (int y = 0; y < n; y++)
            {
                for (int u = 0; u < n; u++)
                {
                    matrix[(n * y) + u] = Alpha(u) * Math.Cos((y + 0.5) * u * invNPi) * scale;
                }
            }

            return matrix;
        }

        private static void MultiplyMatrixBlock(
            double[] matrix,
            ReadOnlySpan<double> block,
            Span<double> output,
            int n,
            int m)
        {
            for (int x = 0; x < m; x++)
            {
                for (int u = 0; u < n; u++)
                {
                    double sum = 0.0;

                    for (int y = 0; y < n; y++)
                    {
                        sum += matrix[(n * u) + y] * block[(m * y) + x];
                    }

                    output[(m * u) + x] = sum;
                }
            }
        }

        private static double[] ConvertFloatToDouble(ReadOnlySpan<float> src, int count)
        {
            double[] dst = new double[count];

            for (int i = 0; i < count; i++)
            {
                dst[i] = src[i];
            }

            return dst;
        }

        private static void ConvertDoubleToFloat(ReadOnlySpan<double> src, Span<float> dst, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dst[i] = (float)src[i];
            }
        }
    }

    /// <summary>
    /// Image comparison, relative error verification, and deterministic random image generation helpers for unit tests.
    /// Ported from <c>lib/jxl/image_test_utils.h</c>.
    /// </summary>
    public static class ImageTestUtils
    {
        /// <summary>
        /// Checks whether two single-plane images have identical dimensions and pixel values.
        /// Ported from <c>jxl::SamePixels</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged pixel element type.</typeparam>
        /// <param name="image1">First image plane.</param>
        /// <param name="image2">Second image plane.</param>
        /// <param name="failures">Optional <see cref="StringBuilder"/> receiving mismatch diagnostics.</param>
        /// <returns><c>true</c> if all pixels match; otherwise <c>false</c>.</returns>
        public static bool SamePixels<T>(
            ImagePlane<T> image1,
            ImagePlane<T> image2,
            StringBuilder? failures = null)
            where T : unmanaged
        {
            if (!ImageOps.SameSize(image1, image2))
            {
                failures?.AppendLine("size mismatch");

                return false;
            }

            nuint mismatches = 0;

            for (nuint y = 0; y < image1.YSize; ++y)
            {
                ReadOnlySpan<T> row1 = image1.ConstRow(y);
                ReadOnlySpan<T> row2 = image2.ConstRow(y);

                if (ScanRowMismatches(row1, row2, image1.XSize, y, failures, ref mismatches))
                {
                    return false;
                }
            }

            return mismatches == 0;
        }

        /// <summary>
        /// Checks whether two 3-plane images have identical dimensions and pixel values across all 3 planes.
        /// Ported from <c>jxl::SamePixels</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged pixel element type.</typeparam>
        /// <param name="image1">First 3-plane image.</param>
        /// <param name="image2">Second 3-plane image.</param>
        /// <param name="failures">Optional <see cref="StringBuilder"/> receiving mismatch diagnostics.</param>
        /// <returns><c>true</c> if all pixels match; otherwise <c>false</c>.</returns>
        public static bool SamePixels<T>(
            Image3<T> image1,
            Image3<T> image2,
            StringBuilder? failures = null)
            where T : unmanaged
        {
            if (!ImageOps.SameSize(image1, image2))
            {
                failures?.AppendLine("size mismatch");

                return false;
            }

            for (nuint c = 0; c < 3; ++c)
            {
                if (!SamePixels(image1.Plane(c), image2.Plane(c), failures))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Verifies that <paramref name="actual"/> matches <paramref name="expected"/> within absolute (<paramref name="thresholdL1"/>)
        /// or relative (<paramref name="thresholdRelative"/>) error tolerances.
        /// Ported from <c>jxl::VerifyRelativeError</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="expected">Expected image plane.</param>
        /// <param name="actual">Actual image plane.</param>
        /// <param name="thresholdL1">Maximum tolerated L1 (absolute) error.</param>
        /// <param name="thresholdRelative">Maximum tolerated relative error when <c>|expected| &gt;= 1e-10</c>.</param>
        /// <param name="border">Border width in pixels to exclude on all sides.</param>
        /// <param name="c">Plane index for diagnostic logging.</param>
        /// <param name="failures">Optional <see cref="StringBuilder"/> receiving mismatch details.</param>
        /// <returns><c>true</c> if all interior pixels pass either tolerance; otherwise <c>false</c>.</returns>
        public static bool VerifyRelativeError<T>(
            ImagePlane<T> expected,
            ImagePlane<T> actual,
            double thresholdL1,
            double thresholdRelative,
            nint border = 0,
            int c = 0,
            StringBuilder? failures = null)
            where T : unmanaged
        {
            if (!ImageOps.SameSize(expected, actual))
            {
                failures?.AppendLine("size mismatch");

                return false;
            }

            bool anyBad = ScanPlaneErrors(
                expected,
                actual,
                thresholdL1,
                thresholdRelative,
                border,
                out double maxL1,
                out double maxRelative);

            if (!anyBad)
            {
                return true;
            }

            RecordFirstFailingPixel(
                expected,
                actual,
                thresholdL1,
                thresholdRelative,
                border,
                (c, maxL1, maxRelative),
                failures);

            return false;
        }

        /// <summary>
        /// Verifies that all 3 planes of <paramref name="actual"/> match <paramref name="expected"/> within
        /// <paramref name="thresholdL1"/> or <paramref name="thresholdRelative"/>.
        /// Ported from <c>jxl::VerifyRelativeError</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="expected">Expected 3-plane image.</param>
        /// <param name="actual">Actual 3-plane image.</param>
        /// <param name="thresholdL1">Maximum tolerated L1 (absolute) error.</param>
        /// <param name="thresholdRelative">Maximum tolerated relative error.</param>
        /// <param name="border">Border width in pixels to exclude on all sides.</param>
        /// <param name="failures">Optional <see cref="StringBuilder"/> receiving mismatch details.</param>
        /// <returns><c>true</c> if all 3 planes pass; otherwise <c>false</c>.</returns>
        public static bool VerifyRelativeError<T>(
            Image3<T> expected,
            Image3<T> actual,
            float thresholdL1,
            float thresholdRelative,
            nint border = 0,
            StringBuilder? failures = null)
            where T : unmanaged
        {
            for (int c = 0; c < 3; ++c)
            {
                bool ok = VerifyRelativeError(
                    expected.Plane((nuint)c),
                    actual.Plane((nuint)c),
                    thresholdL1,
                    thresholdRelative,
                    border,
                    c,
                    failures);

                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Fills <paramref name="image"/> with random values in <c>[begin, end)</c> using <paramref name="rng"/>.
        /// Ported from <c>jxl::GenerateImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="rng">Random number generator state.</param>
        /// <param name="image">Destination image plane.</param>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        public static void GenerateImage<T>(ref Rng rng, ImagePlane<T> image, double begin, double end)
            where T : unmanaged
        {
            for (nuint y = 0; y < image.YSize; ++y)
            {
                Span<T> row = image.Row(y);

                for (nuint x = 0; x < image.XSize; ++x)
                {
                    row[(int)x] = SampleRandom<T>(ref rng, begin, end);
                }
            }
        }

        /// <summary>
        /// Fills all 3 planes of <paramref name="image"/> with random values in <c>[begin, end)</c> using <paramref name="rng"/>.
        /// Ported from <c>jxl::GenerateImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="rng">Random number generator state.</param>
        /// <param name="image">Destination 3-plane image.</param>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        public static void GenerateImage<T>(ref Rng rng, Image3<T> image, double begin, double end)
            where T : unmanaged
        {
            for (nuint c = 0; c < 3; ++c)
            {
                GenerateImage(ref rng, image.Plane(c), begin, end);
            }
        }

        /// <summary>
        /// Fills <paramref name="image"/> with deterministic random values in <c>[begin, end)</c>.
        /// Ported from <c>jxl::RandomFillImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="image">Destination image plane.</param>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        /// <param name="seed">PRNG seed (defaults to 129).</param>
        public static void RandomFillImage<T>(ImagePlane<T> image, T begin, T end, ulong seed = 129)
            where T : unmanaged
        {
            Rng rng = new Rng(seed);

            GenerateImage(ref rng, image, ToDouble(begin), ToDouble(end));
        }

        /// <summary>
        /// Fills <paramref name="image"/> with deterministic random values across the full non-negative range of <typeparamref name="T"/>.
        /// Ported from <c>jxl::RandomFillImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="image">Destination image plane.</param>
        public static void RandomFillImage<T>(ImagePlane<T> image)
            where T : unmanaged
        {
            Rng rng = new Rng(129);
            GetDefaultRange<T>(out double begin, out double end);

            GenerateImage(ref rng, image, begin, end);
        }

        /// <summary>
        /// Fills a single-precision floating-point <paramref name="image"/> with deterministic random values in <c>[0, float.MaxValue)</c>.
        /// Ported from <c>jxl::RandomFillImage(Plane&lt;float&gt;*)</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <param name="image">Destination float image plane.</param>
        public static void RandomFillImage(ImagePlane<float> image)
        {
            Rng rng = new Rng(129);

            GenerateImage(ref rng, image, 0.0, float.MaxValue);
        }

        /// <summary>
        /// Fills all 3 planes of <paramref name="image"/> with deterministic random values in <c>[begin, end)</c>.
        /// Ported from <c>jxl::RandomFillImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="image">Destination 3-plane image.</param>
        /// <param name="begin">Inclusive lower bound.</param>
        /// <param name="end">Exclusive upper bound.</param>
        /// <param name="seed">PRNG seed (defaults to 129).</param>
        public static void RandomFillImage<T>(Image3<T> image, T begin, T end, ulong seed = 129)
            where T : unmanaged
        {
            Rng rng = new Rng(seed);

            GenerateImage(ref rng, image, ToDouble(begin), ToDouble(end));
        }

        /// <summary>
        /// Fills all 3 planes of <paramref name="image"/> with deterministic random values across the default range of <typeparamref name="T"/>.
        /// Ported from <c>jxl::RandomFillImage</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <typeparam name="T">Unmanaged numeric pixel type.</typeparam>
        /// <param name="image">Destination 3-plane image.</param>
        public static void RandomFillImage<T>(Image3<T> image)
            where T : unmanaged
        {
            Rng rng = new Rng(129);
            GetDefaultRange<T>(out double begin, out double end);

            GenerateImage(ref rng, image, begin, end);
        }

        /// <summary>
        /// Fills all 3 planes of a floating-point <paramref name="image"/> with deterministic random values in <c>[0, float.MaxValue)</c>.
        /// Ported from <c>jxl::RandomFillImage(Image3F*)</c> in <c>lib/jxl/image_test_utils.h</c>.
        /// </summary>
        /// <param name="image">Destination 3-plane float image.</param>
        public static void RandomFillImage(Image3<float> image)
        {
            Rng rng = new Rng(129);

            GenerateImage(ref rng, image, 0.0, float.MaxValue);
        }

        /// <summary>
        /// Allocates and fills a new <see cref="ImagePlane{T}"/> of <see cref="float"/> with random values in <c>[min, max)</c>.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="seed">PRNG seed (defaults to 129).</param>
        /// <param name="min">Inclusive minimum value (defaults to 0.0f).</param>
        /// <param name="max">Exclusive maximum value (defaults to 1.0f).</param>
        /// <returns>The newly allocated and populated <see cref="ImagePlane{T}"/>.</returns>
        public static ImagePlane<float> GenerateRandomImage(
            nuint xsize,
            nuint ysize,
            ulong seed = 129,
            float min = 0.0f,
            float max = 1.0f)
        {
            StatusOr<ImagePlane<float>> planeOr = ImagePlane<float>.Create(JxlMemoryManager.Default, xsize, ysize);

            if (!planeOr.IsOk)
            {
                throw new InvalidOperationException("Failed to allocate random ImagePlane<float>.");
            }

            ImagePlane<float> plane = planeOr.Value;

            RandomFillImage(plane, min, max, seed);

            return plane;
        }

        /// <summary>
        /// Allocates and fills a new <see cref="Image3{T}"/> of <see cref="float"/> with random values in <c>[min, max)</c>.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="seed">PRNG seed (defaults to 129).</param>
        /// <param name="min">Inclusive minimum value (defaults to 0.0f).</param>
        /// <param name="max">Exclusive maximum value (defaults to 1.0f).</param>
        /// <returns>The newly allocated and populated <see cref="Image3{T}"/>.</returns>
        public static Image3<float> GenerateRandomImage3(
            nuint xsize,
            nuint ysize,
            ulong seed = 129,
            float min = 0.0f,
            float max = 1.0f)
        {
            StatusOr<Image3<float>> imageOr = Image3<float>.Create(JxlMemoryManager.Default, xsize, ysize);

            if (!imageOr.IsOk)
            {
                throw new InvalidOperationException("Failed to allocate random Image3<float>.");
            }

            Image3<float> image = imageOr.Value;

            RandomFillImage(image, min, max, seed);

            return image;
        }

        private static bool ScanRowMismatches<T>(
            ReadOnlySpan<T> row1,
            ReadOnlySpan<T> row2,
            nuint xsize,
            nuint y,
            StringBuilder? failures,
            ref nuint mismatches)
            where T : unmanaged
        {
            for (nuint x = 0; x < xsize; ++x)
            {
                double v1 = ToDouble(row1[(int)x]);
                double v2 = ToDouble(row2[(int)x]);

                if (v1 != v2)
                {
                    failures?.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "pixel mismatch {0}, {1}: {2} != {3}\n",
                        x,
                        y,
                        v1,
                        v2);

                    if (++mismatches > 4)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ScanPlaneErrors<T>(
            ImagePlane<T> expected,
            ImagePlane<T> actual,
            double thresholdL1,
            double thresholdRelative,
            nint border,
            out double maxL1,
            out double maxRelative)
            where T : unmanaged
        {
            nint xsize  = (nint)expected.XSize;
            nint ysize  = (nint)expected.YSize;
            maxL1       = -1.0;
            maxRelative = -1.0;
            bool anyBad = false;

            for (nint y = border; y < ysize - border; ++y)
            {
                ReadOnlySpan<T> rowExpected = expected.ConstRow((nuint)y);
                ReadOnlySpan<T> rowActual   = actual.ConstRow((nuint)y);

                for (nint x = border; x < xsize - border; ++x)
                {
                    EvaluatePixelError(
                        ToDouble(rowExpected[(int)x]),
                        ToDouble(rowActual[(int)x]),
                        thresholdL1,
                        thresholdRelative,
                        ref anyBad,
                        ref maxL1,
                        ref maxRelative);
                }
            }

            return anyBad;
        }

        private static void EvaluatePixelError(
            double expVal,
            double actVal,
            double thresholdL1,
            double thresholdRelative,
            ref bool anyBad,
            ref double maxL1,
            ref double maxRelative)
        {
            double l1     = Math.Abs(expVal - actVal);
            double absExp = Math.Abs(expVal);

            if (absExp < 1E-10)
            {
                if (l1 > thresholdL1)
                {
                    anyBad = true;
                    maxL1  = Math.Max(maxL1, l1);
                }
            }
            else
            {
                double relative = l1 / absExp;

                if (l1 > thresholdL1 && relative > thresholdRelative)
                {
                    anyBad      = true;
                    maxL1       = Math.Max(maxL1, l1);
                    maxRelative = Math.Max(maxRelative, relative);
                }
            }
        }

        private static void RecordFirstFailingPixel<T>(
            ImagePlane<T> expected,
            ImagePlane<T> actual,
            double thresholdL1,
            double thresholdRelative,
            nint border,
            (int C, double MaxL1, double MaxRelative) summary,
            StringBuilder? failures)
            where T : unmanaged
        {
            if (failures == null)
            {
                return;
            }

            nint xsize = (nint)expected.XSize;
            nint ysize = (nint)expected.YSize;

            for (nint y = border; y < ysize - border; ++y)
            {
                ReadOnlySpan<T> rowExpected = expected.ConstRow((nuint)y);
                ReadOnlySpan<T> rowActual   = actual.ConstRow((nuint)y);

                for (nint x = border; x < xsize - border; ++x)
                {
                    double expVal = ToDouble(rowExpected[(int)x]);
                    double actVal = ToDouble(rowActual[(int)x]);

                    if (IsPixelBad(expVal, actVal, thresholdL1, thresholdRelative))
                    {
                        failures.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "c={0} (maxL1={1:E2}, maxRel={2:E2}): {3}, {4} ({5} x {6}) expected {7} actual {8}",
                            summary.C,
                            summary.MaxL1,
                            summary.MaxRelative,
                            x,
                            y,
                            expected.XSize,
                            expected.YSize,
                            expVal,
                            actVal);

                        return;
                    }
                }
            }
        }

        private static bool IsPixelBad(double expVal, double actVal, double thresholdL1, double thresholdRelative)
        {
            double l1  = Math.Abs(expVal - actVal);
            bool isBad = l1 > thresholdL1;

            if (expVal > 1E-10)
            {
                double relative = l1 / Math.Abs(expVal);
                isBad          &= relative > thresholdRelative;
            }

            return isBad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ToDouble<T>(T value)
            where T : unmanaged
        {
            if (typeof(T) == typeof(float))
            {
                return Unsafe.As<T, float>(ref value);
            }

            if (typeof(T) == typeof(double))
            {
                return Unsafe.As<T, double>(ref value);
            }

            if (typeof(T) == typeof(byte))
            {
                return Unsafe.As<T, byte>(ref value);
            }

            if (typeof(T) == typeof(ushort))
            {
                return Unsafe.As<T, ushort>(ref value);
            }

            return ConvertToDoubleFallback(ref value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ConvertToDoubleFallback<T>(ref T value)
            where T : unmanaged
        {
            if (typeof(T) == typeof(short))
            {
                return Unsafe.As<T, short>(ref value);
            }

            if (typeof(T) == typeof(int))
            {
                return Unsafe.As<T, int>(ref value);
            }

            if (typeof(T) == typeof(uint))
            {
                return Unsafe.As<T, uint>(ref value);
            }

            if (typeof(T) == typeof(sbyte))
            {
                return Unsafe.As<T, sbyte>(ref value);
            }

            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T SampleRandom<T>(ref Rng rng, double begin, double end)
            where T : unmanaged
        {
            if (typeof(T) == typeof(float))
            {
                float val = rng.UniformF((float)begin, (float)end);

                return Unsafe.As<float, T>(ref val);
            }

            if (typeof(T) == typeof(double))
            {
                double val = rng.UniformF((float)begin, (float)end);

                return Unsafe.As<double, T>(ref val);
            }

            if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(int) || typeof(T) == typeof(long))
            {
                long val = rng.UniformI((long)begin, (long)end);

                return CastSigned<T>(val);
            }

            ulong uval = rng.UniformU((ulong)begin, (ulong)end);

            return CastUnsigned<T>(uval);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T CastSigned<T>(long val)
            where T : unmanaged
        {
            if (typeof(T) == typeof(sbyte))
            {
                sbyte v = (sbyte)val;

                return Unsafe.As<sbyte, T>(ref v);
            }

            if (typeof(T) == typeof(short))
            {
                short v = (short)val;

                return Unsafe.As<short, T>(ref v);
            }

            if (typeof(T) == typeof(int))
            {
                int v = (int)val;

                return Unsafe.As<int, T>(ref v);
            }

            return Unsafe.As<long, T>(ref val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T CastUnsigned<T>(ulong val)
            where T : unmanaged
        {
            if (typeof(T) == typeof(byte))
            {
                byte v = (byte)val;

                return Unsafe.As<byte, T>(ref v);
            }

            if (typeof(T) == typeof(ushort))
            {
                ushort v = (ushort)val;

                return Unsafe.As<ushort, T>(ref v);
            }

            if (typeof(T) == typeof(uint))
            {
                uint v = (uint)val;

                return Unsafe.As<uint, T>(ref v);
            }

            return Unsafe.As<ulong, T>(ref val);
        }

        private static void GetDefaultRange<T>(out double begin, out double end)
            where T : unmanaged
        {
            begin = 0.0;

            if (typeof(T) == typeof(byte))
            {
                end = (double)byte.MaxValue + 1.0;

                return;
            }

            if (typeof(T) == typeof(ushort))
            {
                end = (double)ushort.MaxValue + 1.0;

                return;
            }

            if (typeof(T) == typeof(short))
            {
                end = (double)short.MaxValue + 1.0;

                return;
            }

            if (typeof(T) == typeof(int))
            {
                end = (double)int.MaxValue + 1.0;

                return;
            }

            if (typeof(T) == typeof(uint))
            {
                end = (double)uint.MaxValue + 1.0;

                return;
            }

            end = float.MaxValue;
        }
    }
}
