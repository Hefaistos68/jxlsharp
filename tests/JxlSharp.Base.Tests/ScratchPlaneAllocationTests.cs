using System;
using System.Buffers;
using System.Collections.Generic;
using NUnit.Framework;

namespace JxlSharp.Base.Tests;

/// <summary>Keep public zero initialization and scratch ownership/allocation failure behavior.</summary>
[TestFixture]
public sealed class ScratchPlaneAllocationTests
{
    /// <summary>Scratch array ownership follows swaps/moves and each rental is returned once.</summary>
    [Test]
    public void PooledStorageFollowsOwnershipAndCanBeReused()
    {
        var pool = new TrackingPool();
        using var scratch = ImagePlane<float>.CreateUninitialized(null, 257, 17, 16, pool).Value;
        float[] storage = scratch.BackingArray;
        Array.Fill(storage, float.NaN);
        using var ordinary = ImagePlane<float>.Create(null, 257, 17, 16).Value;
        scratch.Swap(ordinary);
        scratch.Dispose();
        Assert.That(pool.ReturnCount, Is.Zero);
        using var moved = ordinary.Move();
        ordinary.Dispose();
        Assert.That(moved.BackingArray, Is.SameAs(storage));
        moved.Dispose();
        moved.Dispose();
        Assert.That(pool.ReturnCount, Is.EqualTo(1));
        using var reused = ImagePlane<float>.CreateUninitialized(null, 257, 17, 16, pool).Value;
        Assert.That(reused.BackingArray, Is.SameAs(storage));
        reused.RowSpan(16).Fill(.375f);
        Assert.That(reused.RowSpan(16).ToArray(), Is.All.EqualTo(.375f));
        reused.Dispose();
        Assert.That(pool.ReturnCount, Is.EqualTo(2));
    }

    private sealed class TrackingPool : ArrayPool<float>
    {
        private float[]? _available;
        private readonly HashSet<float[]> _rented = new();
        public int ReturnCount { get; private set; }

        public override float[] Rent(int minimumLength)
        {
            float[] result = _available ?? new float[minimumLength + 16];
            _available = null;
            Assert.That(_rented.Add(result), Is.True);
            return result;
        }

        public override void Return(float[] array, bool clearArray = false)
        {
            Assert.That(_rented.Remove(array), Is.True);
            _available = array;
            ReturnCount++;
        }
    }

    /// <summary>Public planes still start with zeroes, including stride and pre-padding.</summary>
    [Test]
    public void PublicPlanesRemainZeroInitialized()
    {
        foreach (nuint width in new nuint[] { 0, 1, 17, 257 })
        foreach (nuint height in new nuint[] { 1, 17, 257 })
        {
            using var plane = ImagePlane<float>.Create(null, width, height, 16).Value;
            Assert.That(plane.BackingArray, Is.All.Zero);
        }
    }

    /// <summary>Moving scratch planes transfers storage once; failed allocations do not retain handles.</summary>
    [Test]
    public void ScratchPlanesRespectCustomAllocatorAndMove()
    {
        var live = new HashSet<nint>();
        int next = 0, frees = 0;
        bool fail = false;
        var manager = new JxlMemoryManager
        {
            Alloc = (_, _) => { if (fail) return 0; nint handle = ++next; live.Add(handle); return handle; },
            Free = (_, handle) => { Assert.That(live.Remove(handle), Is.True); frees++; },
        };
        using var original = ImagePlane<float>.CreateUninitialized(manager, 257, 17, 16).Value;
        Array.Fill(original.BackingArray, .375f);
        using (var moved = original.Move())
        {
            original.Dispose();
            Assert.That(live.Count, Is.EqualTo(1));
            Assert.That(moved.RowSpan(0)[0], Is.EqualTo(.375f));
        }
        Assert.That(live.Count, Is.Zero);
        Assert.That(frees, Is.EqualTo(1));
        fail = true;
        Assert.That(ImagePlane<float>.CreateUninitialized(manager, 257, 17).IsOk, Is.False);
        Assert.That(live.Count, Is.Zero);
    }
}
