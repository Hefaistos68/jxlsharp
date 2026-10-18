// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PaddedBytes"/> (ported from <c>lib/jxl/padded_bytes_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class PaddedBytesTests
    {
        /// <summary>
        /// Ported from <c>PaddedBytesTest.TestNonEmptyFirstByteZero</c>.
        /// </summary>
        [Test]
        public void TestNonEmptyFirstByteZero()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            StatusOr<PaddedBytes> pbResult = PaddedBytes.WithInitialSpace(memoryManager, 1);
            pbResult.IsOk.ShouldBeTrue();

            using PaddedBytes pb = pbResult.Value;
            pb[0].ShouldBe((byte)0);

            // Even after resizing..
            pb.Resize(20).IsOk.ShouldBeTrue();
            pb[0].ShouldBe((byte)0);

            // And reserving.
            pb.Reserve(200).IsOk.ShouldBeTrue();
            pb[0].ShouldBe((byte)0);
        }

        /// <summary>
        /// Ported from <c>PaddedBytesTest.TestEmptyFirstByteZero</c>.
        /// </summary>
        [Test]
        public void TestEmptyFirstByteZero()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            StatusOr<PaddedBytes> pbResult = PaddedBytes.WithInitialSpace(memoryManager, 0);
            pbResult.IsOk.ShouldBeTrue();

            using PaddedBytes pb = pbResult.Value;

            // After resizing - new zero is written despite there being nothing to copy.
            pb.Resize(20).IsOk.ShouldBeTrue();
            pb[0].ShouldBe((byte)0);
        }

        /// <summary>
        /// Ported from <c>PaddedBytesTest.TestFillWithoutReserve</c>.
        /// </summary>
        [Test]
        public void TestFillWithoutReserve()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            using PaddedBytes pb           = new PaddedBytes(memoryManager);

            for (nuint i = 0; i < 170u; ++i)
            {
                pb.PushBack((byte)i).IsOk.ShouldBeTrue();
            }

            pb.Size.ShouldBe((nuint)170u);
            pb.Capacity.ShouldBeGreaterThanOrEqualTo((nuint)170u);
        }

        /// <summary>
        /// Ported from <c>PaddedBytesTest.TestFillWithExactReserve</c>.
        /// </summary>
        [Test]
        public void TestFillWithExactReserve()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            using PaddedBytes pb           = new PaddedBytes(memoryManager);

            pb.Reserve(170).IsOk.ShouldBeTrue();

            for (nuint i = 0; i < 170u; ++i)
            {
                pb.PushBack((byte)i).IsOk.ShouldBeTrue();
            }

            pb.Size.ShouldBe((nuint)170u);
            pb.Capacity.ShouldBe((nuint)170u);
        }

        /// <summary>
        /// Ported from <c>PaddedBytesTest.TestFillWithMoreReserve</c>.
        /// </summary>
        [Test]
        public void TestFillWithMoreReserve()
        {
            JxlMemoryManager memoryManager = JxlMemoryManager.Default;
            using PaddedBytes pb           = new PaddedBytes(memoryManager);

            pb.Reserve(171).IsOk.ShouldBeTrue();

            for (nuint i = 0; i < 170u; ++i)
            {
                pb.PushBack((byte)i).IsOk.ShouldBeTrue();
            }

            pb.Size.ShouldBe((nuint)170u);
            pb.Capacity.ShouldBeGreaterThan((nuint)170u);
        }

        /// <summary>
        /// Verifies <see cref="PaddedBytes.Assign"/>, <see cref="PaddedBytes.Append"/>, <see cref="PaddedBytes.Swap"/>,
        /// and <see cref="PaddedBytes.Move"/>.
        /// </summary>
        [Test]
        public void AssignAppendSwapAndMoveWork()
        {
            using PaddedBytes pb1 = new PaddedBytes(JxlMemoryManager.Default);
            using PaddedBytes pb2 = new PaddedBytes(JxlMemoryManager.Default);

            pb1.Assign([1, 2, 3]).IsOk.ShouldBeTrue();
            pb1.Append([4, 5]).IsOk.ShouldBeTrue();
            pb1.Resize(7, 9).IsOk.ShouldBeTrue();

            pb1.Size.ShouldBe((nuint)7);
            pb1.Span.ToArray().ShouldBe(new byte[] { 1, 2, 3, 4, 5, 9, 9 });

            pb1.Swap(pb2);
            pb1.Empty.ShouldBeTrue();
            pb2.Size.ShouldBe((nuint)7);

            using PaddedBytes pb3 = pb2.Move();
            pb2.Empty.ShouldBeTrue();
            pb3.Size.ShouldBe((nuint)7);
            pb3.ShrinkToFit().IsOk.ShouldBeTrue();
            pb3.Clear();
            pb3.Empty.ShouldBeTrue();
        }
    }
}
