// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Provides a subset of the <c>std::vector</c> interface with some differences:
    /// - allows BitWriter to write 64 bits at a time without bounds checking;
    /// - ONLY zero-initializes the first byte (required by BitWriter);
    /// - ensures cache-line alignment.
    /// Ported from <c>lib/jxl/padded_bytes.h</c>.
    /// </summary>
    public sealed class PaddedBytes : IDisposable
    {
        private JxlMemoryManager? _memoryManager;
        private nuint _size;
        private nuint _capacity;
        private AlignedMemory _data;

        /// <summary>
        /// Initializes a new instance of the <see cref="PaddedBytes"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocations (defaults to <see cref="JxlMemoryManager.Default"/> if null).</param>
        public PaddedBytes(JxlMemoryManager? memoryManager)
        {
            _memoryManager = memoryManager ?? JxlMemoryManager.Default;
            _size          = 0;
            _capacity      = 0;
            _data          = new AlignedMemory();
        }

        /// <summary>
        /// Gets the <see cref="JxlMemoryManager"/> used by this buffer.
        /// </summary>
        public JxlMemoryManager? MemoryManager
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _memoryManager;
        }

        /// <summary>
        /// Gets the current logical size in bytes.
        /// </summary>
        public nuint Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _size;
        }

        /// <summary>
        /// Gets the current reserved capacity in bytes (excluding the 8-byte BitWriter padding).
        /// </summary>
        public nuint Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _capacity;
        }

        /// <summary>
        /// Gets a value indicating whether the buffer is empty (<c>Size == 0</c>).
        /// </summary>
        public bool Empty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _size == 0;
        }

        /// <summary>
        /// Gets a <see cref="Span{Byte}"/> over the active bytes (<c>0..Size</c>).
        /// </summary>
        public Span<byte> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _size == 0 ? Span<byte>.Empty : _data.Span.Slice(0, (int)_size);
        }

        /// <summary>
        /// Gets a <see cref="Span{Byte}"/> over the full allocated buffer including the 8-byte BitWriter padding.
        /// </summary>
        public Span<byte> PaddedSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data.Span;
        }

        /// <summary>
        /// Gets or sets the byte at index <paramref name="i"/>.
        /// Note: <c>i &lt;= Size</c> is permitted due to padding and required by BitWriter.
        /// </summary>
        /// <param name="i">Byte index.</param>
        /// <returns>Reference to the byte at index <paramref name="i"/>.</returns>
        public ref byte this[nuint i]
        {
            get
            {
                BoundsCheck(i);

                return ref _data.Span[(int)i];
            }
        }

        /// <summary>
        /// Creates a new <see cref="PaddedBytes"/> with initial space of <paramref name="size"/> bytes.
        /// </summary>
        /// <param name="memoryManager">Memory manager to use.</param>
        /// <param name="size">Initial size in bytes.</param>
        /// <returns><see cref="StatusOr{PaddedBytes}"/> containing the initialized buffer or failure status.</returns>
        public static StatusOr<PaddedBytes> WithInitialSpace(JxlMemoryManager? memoryManager, nuint size)
        {
            PaddedBytes result = new PaddedBytes(memoryManager);
            Status status      = result.Init(size);

            if (!status)
            {
                result.Dispose();

                return status;
            }

            return result;
        }

        /// <summary>
        /// Swaps the contents of this <see cref="PaddedBytes"/> with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other instance to swap with.</param>
        public void Swap(PaddedBytes other)
        {
            JxlMemoryManager? tempMm = _memoryManager;
            nuint tempSize           = _size;
            nuint tempCapacity       = _capacity;
            AlignedMemory tempData   = _data;

            _memoryManager = other._memoryManager;
            _size          = other._size;
            _capacity      = other._capacity;
            _data          = other._data;

            other._memoryManager = tempMm;
            other._size          = tempSize;
            other._capacity      = tempCapacity;
            other._data          = tempData;
        }

        /// <summary>
        /// Moves ownership of the buffer into a new <see cref="PaddedBytes"/> instance, resetting this instance's size and capacity to 0.
        /// </summary>
        /// <returns>Moved <see cref="PaddedBytes"/> instance.</returns>
        public PaddedBytes Move()
        {
            PaddedBytes moved = new PaddedBytes(_memoryManager)
            {
                _size     = _size,
                _capacity = _capacity,
                _data     = _data.Move(),
            };

            _size     = 0;
            _capacity = 0;

            return moved;
        }

        /// <summary>
        /// If current capacity is greater than requested, then no-op. Otherwise
        /// copies existing data to newly allocated buffer.
        /// The new capacity will be at least 1.5 times the old capacity to avoid quadratic behavior.
        /// </summary>
        /// <param name="capacity">Requested minimum capacity in bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Status Reserve(nuint capacity)
        {
            if (capacity <= _capacity)
            {
                return true;
            }

            if (!Common.SafeAdd(_capacity, _capacity / 2, out nuint newCapacity))
            {
                return Status.Failure("PaddedBytes reserve: capacity overflow");
            }

            newCapacity = Math.Max(capacity, newCapacity);

            return ChangeCapacity(newCapacity);
        }

        /// <summary>
        /// Shrinks the capacity to fit the current size (minimum 64 bytes).
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status ShrinkToFit()
        {
            return ChangeCapacity(_size);
        }

        /// <summary>
        /// Resizes the buffer to <paramref name="size"/> bytes without initializing new bytes
        /// (except zero-initializing the first newly exposed byte for BitWriter).
        /// </summary>
        /// <param name="size">New size in bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Status Resize(nuint size)
        {
            Status status = Reserve(size);

            if (!status)
            {
                return status;
            }

            _size = size;

            return true;
        }

        /// <summary>
        /// Resizes the buffer to <paramref name="size"/> bytes and initializes any newly added bytes to <paramref name="value"/>.
        /// </summary>
        /// <param name="size">New size in bytes.</param>
        /// <param name="value">Fill value for newly added bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status Resize(nuint size, byte value)
        {
            nuint oldSize = _size;
            Status status = Resize(size);

            if (!status)
            {
                return status;
            }

            if (_size > oldSize)
            {
                _data.Span.Slice((int)oldSize, (int)(_size - oldSize)).Fill(value);
            }

            return true;
        }

        /// <summary>
        /// Appends a single byte with amortized constant complexity.
        /// </summary>
        /// <param name="x">Byte value to append.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Status PushBack(byte x)
        {
            if (_size == _capacity)
            {
                Status status = Reserve(_capacity + 1);

                if (!status)
                {
                    return status;
                }
            }

            _data.Span[(int)_size++] = x;

            return true;
        }

        /// <summary>
        /// Resets the size of the buffer to 0 without releasing memory.
        /// </summary>
        public void Clear()
        {
            Resize(0);
        }

        /// <summary>
        /// Replaces the contents of the buffer with <paramref name="source"/>.
        /// </summary>
        /// <param name="source">Bytes to copy into the buffer.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status Assign(ReadOnlySpan<byte> source)
        {
            Status status = Resize((nuint)source.Length);

            if (!status)
            {
                return status;
            }

            if (!source.IsEmpty)
            {
                source.CopyTo(_data.Span.Slice(0, source.Length));
            }

            return true;
        }

        /// <summary>
        /// Appends the bytes in <paramref name="source"/> to the end of this buffer.
        /// </summary>
        /// <param name="source">Bytes to append.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status Append(ReadOnlySpan<byte> source)
        {
            if (!source.IsEmpty)
            {
                nuint oldSize = _size;

                if (!Common.SafeAdd(oldSize, (nuint)source.Length, out nuint newSize))
                {
                    return Status.Failure("PaddedBytes append: overflow");
                }

                Status status = Resize(newSize);

                if (!status)
                {
                    return status;
                }

                source.CopyTo(_data.Span.Slice((int)oldSize, source.Length));
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _data.Dispose();
            _size     = 0;
            _capacity = 0;
        }

        private Status Init(nuint size)
        {
            _size = size;

            return Reserve(size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BoundsCheck(nuint i)
        {
            // <= is safe due to padding and required by BitWriter.
            if (i > _size)
            {
                throw new IndexOutOfRangeException($"Index {i} exceeds size {_size}.");
            }
        }

        private Status ChangeCapacity(nuint newCapacity)
        {
            newCapacity = Math.Max((nuint)64, newCapacity);

            // BitWriter writes up to 7 bytes past the end.
            if (!Common.SafeAdd(newCapacity, (nuint)8, out nuint paddedCapacity))
            {
                return Status.Failure("PaddedBytes capacity too large");
            }

            StatusOr<AlignedMemory> newDataResult = AlignedMemory.Create(_memoryManager, paddedCapacity);

            if (!newDataResult.IsOk)
            {
                return newDataResult.Status;
            }

            AlignedMemory newData = newDataResult.Value;

            if (!_data.IsValid)
            {
                // First allocation: ensure first byte is initialized (won't be copied).
                newData.Span[0] = 0;
            }
            else
            {
                // Subsequent resize: copy existing data to new location.
                if (_size > 0)
                {
                    _data.Span.Slice(0, (int)_size).CopyTo(newData.Span);
                }

                // Ensure that the first new byte is initialized, to allow write_bits to
                // safely append to the newly-resized PaddedBytes.
                newData.Span[(int)_size] = 0;
            }

            _data.Dispose();
            _capacity = newCapacity;
            _data     = newData;

            return true;
        }
    }
}
