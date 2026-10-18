// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JxlMemoryManager"/> and <see cref="AlignedMemory"/>.
    /// </summary>
    [TestFixture]
    public class JxlMemoryManagerTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlMemoryManager.Init"/> initializes default callbacks and rejects partial callbacks.
        /// </summary>
        [Test]
        public void InitValidatesAllocAndFreePairs()
        {
            JxlMemoryManager self   = new JxlMemoryManager();
            Status defaultInitState = JxlMemoryManager.Init(self, null);

            defaultInitState.IsOk.ShouldBeTrue();
            self.Alloc.ShouldNotBeNull();
            self.Free.ShouldNotBeNull();

            JxlMemoryManager invalid = new JxlMemoryManager
            {
                Alloc = static (_, _) => 0,
                Free  = null,
            };

            Status invalidState = JxlMemoryManager.Init(self, invalid);
            invalidState.IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="JxlMemoryManager.BytesPerRow"/> alignment and alias avoidance.
        /// </summary>
        [Test]
        public void BytesPerRowComputesAlignedNonAliasedStride()
        {
            StatusOr<nuint> zeroRow = JxlMemoryManager.BytesPerRow(0, sizeof(float));
            zeroRow.IsOk.ShouldBeTrue();
            zeroRow.Value.ShouldBe((nuint)0);

            // 512 floats = 2048 bytes = kAlias, so BytesPerRow must add extra alignment padding to avoid 2 KiB aliasing.
            StatusOr<nuint> aliasRow = JxlMemoryManager.BytesPerRow(512, sizeof(float));
            aliasRow.IsOk.ShouldBeTrue();
            (aliasRow.Value % JxlMemoryManager.Alignment).ShouldBe((nuint)0);
            (aliasRow.Value % JxlMemoryManager.Alias).ShouldNotBe((nuint)0);
        }

        /// <summary>
        /// Verifies <see cref="AlignedMemory.Create"/> and <see cref="AlignedMemory.Move"/>.
        /// </summary>
        [Test]
        public unsafe void AlignedMemoryCreateAndMoveWork()
        {
            StatusOr<AlignedMemory> result = AlignedMemory.Create(JxlMemoryManager.Default, 1024, 64);
            result.IsOk.ShouldBeTrue();

            using AlignedMemory mem = result.Value;
            mem.IsValid.ShouldBeTrue();
            ((nuint)mem.Address % JxlMemoryManager.Alignment).ShouldBe((nuint)0);

            int* ptr = mem.GetPointer<int>();
            ptr[0]   = 42;
            mem.GetSpan<int>(4)[0].ShouldBe(42);

            using AlignedMemory moved = mem.Move();
            mem.IsValid.ShouldBeFalse();
            moved.IsValid.ShouldBeTrue();
            moved.GetSpan<int>(4)[0].ShouldBe(42);
        }
    }
}
