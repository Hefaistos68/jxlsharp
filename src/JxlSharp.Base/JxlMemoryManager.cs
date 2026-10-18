// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JxlSharp.Base
{
    /// <summary>
    /// Allocating function for a memory region of a given size.
    /// Allocates a contiguous memory region of size <paramref name="size"/> bytes.
    /// </summary>
    /// <param name="opaque">Custom memory manager handle provided by the caller.</param>
    /// <param name="size">Size in bytes of the requested memory region.</param>
    /// <returns><c>0</c> if the memory cannot be allocated, pointer to the memory otherwise.</returns>
    public delegate nint JxlAllocFunc(object? opaque, nuint size);

    /// <summary>
    /// Deallocating function pointer type.
    /// This function MUST do nothing if <paramref name="address"/> is <c>0</c>.
    /// </summary>
    /// <param name="opaque">Custom memory manager handle provided by the caller.</param>
    /// <param name="address">Memory region pointer returned by <see cref="JxlAllocFunc"/>, or <c>0</c>.</param>
    public delegate void JxlFreeFunc(object? opaque, nint address);

    /// <summary>
    /// Memory Manager class and internal memory allocation helpers
    /// (ported from <c>include/jxl/memory_manager.h</c>, <c>lib/jxl/memory_manager_internal.h</c>,
    /// and <c>lib/jxl/memory_manager_internal.cc</c>).
    /// </summary>
    public sealed class JxlMemoryManager
    {
        /// <summary>
        /// To avoid RFOs, match L2 fill size (pairs of lines); 2 x cache line size.
        /// </summary>
        public const nuint Alignment = 2 * 64;

        /// <summary>
        /// Minimum multiple for which cache set conflicts and/or loads blocked by
        /// preceding stores can occur.
        /// </summary>
        public const nuint NumAlignmentGroups = 16;

        /// <summary>
        /// Cache alias stride in bytes (<c>NumAlignmentGroups * Alignment</c>).
        /// </summary>
        public const nuint Alias = NumAlignmentGroups * Alignment;

        /// <summary>
        /// Gets a default initialized <see cref="JxlMemoryManager"/> using native heap allocation.
        /// </summary>
        public static JxlMemoryManager Default { get; } = new JxlMemoryManager
        {
            Opaque = null,
            Alloc  = DefaultAlloc,
            Free   = DefaultFree,
        };

        /// <summary>
        /// The opaque reference that will be passed as the first parameter to all the
        /// functions in this instance.
        /// </summary>
        public object? Opaque { get; set; }

        /// <summary>
        /// Memory allocation function. This can be null if and only if also the
        /// <see cref="Free"/> member in this class is null.
        /// </summary>
        public JxlAllocFunc? Alloc { get; set; }

        /// <summary>
        /// Free function matching the <see cref="Alloc"/> member.
        /// </summary>
        public JxlFreeFunc? Free { get; set; }

        /// <summary>
        /// Initializes the memory manager instance with the passed one. The
        /// MemoryManager passed in <paramref name="memoryManager"/> may be null or contain null
        /// functions which will be initialized with the default ones. If either alloc
        /// or free are null, then both must be null, otherwise this function returns an error.
        /// </summary>
        /// <param name="self">Target memory manager instance to initialize.</param>
        /// <param name="memoryManager">Optional source memory manager configuration.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; <see cref="StatusCode.GenericError"/> if only one of Alloc/Free is provided.</returns>
        public static Status Init(JxlMemoryManager self, JxlMemoryManager? memoryManager)
        {
            if (self is null)
            {
                return Status.Failure("self is null");
            }

            if (memoryManager is not null)
            {
                self.Opaque = memoryManager.Opaque;
                self.Alloc  = memoryManager.Alloc;
                self.Free   = memoryManager.Free;
            }
            else
            {
                self.Opaque = null;
                self.Alloc  = null;
                self.Free   = null;
            }

            bool isDefaultAlloc = self.Alloc is null;
            bool isDefaultFree  = self.Free is null;

            if (isDefaultAlloc != isDefaultFree)
            {
                return false;
            }

            if (isDefaultAlloc)
            {
                self.Alloc = DefaultAlloc;
            }

            if (isDefaultFree)
            {
                self.Free = DefaultFree;
            }

            return true;
        }

        /// <summary>
        /// Allocates <paramref name="size"/> bytes using <paramref name="memoryManager"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager to use.</param>
        /// <param name="size">Number of bytes to allocate.</param>
        /// <returns>Allocated address or <c>0</c> on failure.</returns>
        public static nint MemoryManagerAlloc(JxlMemoryManager memoryManager, nuint size)
        {
            JxlAllocFunc alloc = memoryManager.Alloc ?? DefaultAlloc;

            return alloc(memoryManager.Opaque, size);
        }

        /// <summary>
        /// Frees <paramref name="address"/> using <paramref name="memoryManager"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager to use.</param>
        /// <param name="address">Address to free.</param>
        public static void MemoryManagerFree(JxlMemoryManager memoryManager, nint address)
        {
            JxlFreeFunc free = memoryManager.Free ?? DefaultFree;
            free(memoryManager.Opaque, address);
        }

        /// <summary>
        /// Returns recommended distance in bytes between the start of two consecutive rows.
        /// </summary>
        /// <param name="xsize">Number of elements per row.</param>
        /// <param name="sizeofT">Size of each element in bytes.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the row stride in bytes.</returns>
        public static StatusOr<nuint> BytesPerRow(nuint xsize, nuint sizeofT)
        {
            // Special case: we don't allow any ops -> don't need extra padding
            if (xsize == 0)
            {
                return (nuint)0;
            }

            nuint vecSize = SimdUtil.MaxVectorSize();

            if (!Common.SafeMul(xsize, sizeofT, out nuint validBytes))
            {
                return Status.Failure("Image dimensions are too large");
            }

            // Allow unaligned accesses starting at the last valid value.
            // NB: in scalar build `vec_size` is `4` (one `float`); still worth checking.
            if (vecSize != 0)
            {
                if (vecSize < sizeofT)
                {
                    return Status.Failure("Vector size smaller than element size");
                }

                nuint extra = vecSize - sizeofT;

                if (!Common.SafeAdd(validBytes, extra, out validBytes))
                {
                    return Status.Failure("Image dimensions are too large");
                }
            }

            // Round up to vector and cache line size.
            nuint align = Math.Max(vecSize, Alignment);

            if ((align & (align - 1)) != 0)
            {
                return Status.Failure("Alignment must be power of two");
            }

            if (!Common.SafeRoundUpTo(validBytes, align, out nuint bytesPerRow))
            {
                return Status.Failure("Image dimensions are too large");
            }

            // During the lengthy window before writes are committed to memory, CPUs
            // guard against read after write hazards by checking the address, but
            // only the lower 11 bits. We avoid a false dependency between writes to
            // consecutive rows by ensuring their sizes are not multiples of 2 KiB.
            // Avoid2K prevents the same problem for the planes of an Image3.
            if (bytesPerRow % Alias == 0)
            {
                if (!Common.SafeAdd(bytesPerRow, align, out bytesPerRow))
                {
                    return Status.Failure("Image dimensions are too large");
                }
            }

            return bytesPerRow;
        }

        private static unsafe nint DefaultAlloc(object? opaque, nuint size)
        {
            try
            {
                return (nint)NativeMemory.Alloc(size == 0 ? 1 : size);
            }
            catch (OutOfMemoryException)
            {
                return 0;
            }
        }

        private static unsafe void DefaultFree(object? opaque, nint address)
        {
            if (address != 0)
            {
                NativeMemory.Free((void*)address);
            }
        }
    }

    /// <summary>
    /// Cache-aligned memory allocation with anti-aliasing group offset
    /// (ported from <c>jxl::AlignedMemory</c> in <c>lib/jxl/memory_manager_internal.h</c>).
    /// </summary>
    public sealed class AlignedMemory : IDisposable
    {
        private static int _nextGroup;

        private nint _allocation;
        private JxlMemoryManager? _memoryManager;
        private nint _address;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="AlignedMemory"/> class.
        /// </summary>
        public AlignedMemory()
        {
            _allocation    = 0;
            _memoryManager = null;
            _address       = 0;
        }

        private AlignedMemory(JxlMemoryManager memoryManager, nint allocation, nuint prePadding)
        {
            // Congruence to `offset` (mod kAlias) reduces cache conflicts and load/store
            // stalls, especially with large allocations that would otherwise have similar
            // alignments.
            uint groupIdx = (uint)Interlocked.Increment(ref _nextGroup) - 1u;
            nuint group   = groupIdx & (JxlMemoryManager.NumAlignmentGroups - 1);
            nuint offset  = JxlMemoryManager.Alignment * group;

            // Actual allocation.
            nuint address = (nuint)allocation + prePadding;

            // Aligned address, but might land before allocation (50%/50%) or not have
            // enough pre-padding.
            nuint alignedAddress = (address & ~(JxlMemoryManager.Alias - 1)) + offset;

            if (alignedAddress < address)
            {
                alignedAddress += JxlMemoryManager.Alias;
            }

            _allocation    = allocation;
            _memoryManager = memoryManager;
            _address       = (nint)alignedAddress;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="AlignedMemory"/> class.
        /// </summary>
        ~AlignedMemory()
        {
            ReleaseUnmanagedResources();
        }

        /// <summary>
        /// Gets the aligned memory address, or <c>0</c> if unallocated.
        /// </summary>
        public nint Address => _address;

        /// <summary>
        /// Gets a value indicating whether this instance holds a valid allocation.
        /// </summary>
        public bool IsValid => _address != 0;

        /// <summary>
        /// Gets the <see cref="JxlMemoryManager"/> associated with this allocation.
        /// </summary>
        public JxlMemoryManager? MemoryManager => _memoryManager;

        /// <summary>
        /// Creates a new <see cref="AlignedMemory"/> block of at least <paramref name="size"/> bytes.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate memory.</param>
        /// <param name="size">Requested payload size in bytes.</param>
        /// <param name="prePadding">Optional pre-padding in bytes (at most <see cref="JxlMemoryManager.Alias"/>).</param>
        /// <returns><see cref="StatusOr{AlignedMemory}"/> containing the allocated block or failure status.</returns>
        public static StatusOr<AlignedMemory> Create(
            JxlMemoryManager? memoryManager,
            nuint size,
            nuint prePadding = 0)
        {
            if (prePadding > JxlMemoryManager.Alias)
            {
                return Status.Failure("Pre-padding exceeds kAlias");
            }

            if (!Common.SafeAdd(size, prePadding, out nuint allocationSize) ||
                !Common.SafeAdd(allocationSize, JxlMemoryManager.Alias, out allocationSize))
            {
                return Status.Failure("Requested allocation is too large");
            }

            if (memoryManager is null)
            {
                return Status.Failure("Memory manager is null");
            }

            nint allocated = JxlMemoryManager.MemoryManagerAlloc(memoryManager, allocationSize);

            if (allocated == 0)
            {
                return Status.Failure("Allocation failed");
            }

            return new AlignedMemory(memoryManager, allocated, prePadding);
        }

        /// <summary>
        /// Returns a typed pointer to the aligned memory address.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <returns>Typed pointer to the start of the aligned buffer.</returns>
        public unsafe T* GetPointer<T>()
            where T : unmanaged
        {
            return (T*)_address;
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> over the aligned memory region.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="length">Number of elements of type <typeparamref name="T"/>.</param>
        /// <returns>Span over the aligned buffer.</returns>
        public unsafe Span<T> GetSpan<T>(int length)
            where T : unmanaged
        {
            return new Span<T>((void*)_address, length);
        }

        /// <summary>
        /// Transfers ownership of the underlying allocation to a new <see cref="AlignedMemory"/> instance,
        /// leaving this instance empty.
        /// </summary>
        /// <returns>A new <see cref="AlignedMemory"/> owning the buffer.</returns>
        public AlignedMemory Move()
        {
            AlignedMemory moved = new AlignedMemory
            {
                _allocation    = _allocation,
                _memoryManager = _memoryManager,
                _address       = _address,
            };

            _allocation    = 0;
            _memoryManager = null;
            _address       = 0;

            return moved;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        private void ReleaseUnmanagedResources()
        {
            if (_memoryManager is not null && _allocation != 0)
            {
                JxlMemoryManager.MemoryManagerFree(_memoryManager, _allocation);
            }

            _allocation    = 0;
            _memoryManager = null;
            _address       = 0;
        }
    }
}
