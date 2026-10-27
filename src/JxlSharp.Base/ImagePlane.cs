// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Common interface exposing image width and height in pixels.
    /// </summary>
    public interface IImageDimensions
    {
        /// <summary>
        /// Gets the width in valid pixels.
        /// </summary>
        nuint XSize { get; }

        /// <summary>
        /// Gets the height in valid pixels.
        /// </summary>
        nuint YSize { get; }
    }

    /// <summary>
    /// Type-independent base class of <see cref="ImagePlane{T}"/>
    /// (ported from <c>jxl::detail::PlaneBase</c> in <c>lib/jxl/image.h</c> and <c>lib/jxl/image.cc</c>).
    /// </summary>
    public abstract class PlaneBase : IImageDimensions, IDisposable
    {
        private uint _xsize;
        private uint _ysize;
        private uint _origXSize;
        private uint _origYSize;
        private nuint _bytesPerRow;
        private JxlMemoryManager? _memoryManager;
        private nint _allocation;
        private nuint _sizeofT;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="PlaneBase"/> class.
        /// </summary>
        protected PlaneBase()
        {
            _xsize         = 0;
            _ysize         = 0;
            _origXSize     = 0;
            _origYSize     = 0;
            _bytesPerRow   = 0;
            _memoryManager = null;
            _allocation    = 0;
            _sizeofT       = 0;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaneBase"/> class with dimensions and element size.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="sizeofT">Element size in bytes.</param>
        protected PlaneBase(uint xsize, uint ysize, nuint sizeofT)
        {
            _xsize         = xsize;
            _ysize         = ysize;
            _origXSize     = xsize;
            _origYSize     = ysize;
            _bytesPerRow   = 0;
            _memoryManager = null;
            _allocation    = 0;
            _sizeofT       = sizeofT;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="PlaneBase"/> class.
        /// </summary>
        ~PlaneBase()
        {
            if (_memoryManager is not null && _allocation != 0)
            {
                JxlMemoryManager.MemoryManagerFree(_memoryManager, _allocation);
            }
        }

        /// <inheritdoc />
        public nuint XSize => _xsize;

        /// <summary>Gets the width in valid pixels (C++ naming alias).</summary>
        public nuint xsize => _xsize;

        /// <inheritdoc />
        public nuint YSize => _ysize;

        /// <summary>Gets the height in valid pixels (C++ naming alias).</summary>
        public nuint ysize => _ysize;

        /// <summary>
        /// Gets the row stride in bytes (including alignment padding).
        /// NOTE: do not use this for copying rows - the valid <see cref="XSize"/> may be much less.
        /// </summary>
        public nuint BytesPerRow => _bytesPerRow;

        /// <summary>
        /// Gets the <see cref="JxlMemoryManager"/> associated with this plane.
        /// </summary>
        public JxlMemoryManager? MemoryManager => _memoryManager;

        /// <summary>
        /// Useful for pre-allocating image with some padding for alignment purposes
        /// and later reporting the actual valid dimensions. May also be used to
        /// un-shrink the image. Caller is responsible for ensuring <paramref name="xsize"/>/<paramref name="ysize"/>
        /// are &lt;= the original dimensions.
        /// </summary>
        /// <param name="xsize">New logical width in pixels.</param>
        /// <param name="ysize">New logical height in pixels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status ShrinkTo(nuint xsize, nuint ysize)
        {
            if (xsize > _origXSize || ysize > _origYSize)
            {
                return Status.Failure("ShrinkTo dimensions exceed original allocation");
            }

            _xsize = (uint)xsize;
            _ysize = (uint)ysize;

            return true;
        }

        /// <summary>
        /// Swaps the contents of this plane with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other plane to swap with.</param>
        public void Swap(PlaneBase other)
        {
            uint tempXSize            = _xsize;
            uint tempYSize            = _ysize;
            uint tempOrigX            = _origXSize;
            uint tempOrigY            = _origYSize;
            nuint tempBytesPerRow     = _bytesPerRow;
            JxlMemoryManager? tempMgr = _memoryManager;
            nint tempAlloc            = _allocation;
            nuint tempSizeofT         = _sizeofT;

            _xsize         = other._xsize;
            _ysize         = other._ysize;
            _origXSize     = other._origXSize;
            _origYSize     = other._origYSize;
            _bytesPerRow   = other._bytesPerRow;
            _memoryManager = other._memoryManager;
            _allocation    = other._allocation;
            _sizeofT       = other._sizeofT;

            other._xsize         = tempXSize;
            other._ysize         = tempYSize;
            other._origXSize     = tempOrigX;
            other._origYSize     = tempOrigY;
            other._bytesPerRow   = tempBytesPerRow;
            other._memoryManager = tempMgr;
            other._allocation    = tempAlloc;
            other._sizeofT       = tempSizeofT;

            UpdateFinalization();
            other.UpdateFinalization();
            SwapDerived(other);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_memoryManager is not null && _allocation != 0)
            {
                JxlMemoryManager.MemoryManagerFree(_memoryManager, _allocation);
            }

            _allocation    = 0;
            _memoryManager = null;
            _xsize         = 0;
            _ysize         = 0;
            _origXSize     = 0;
            _origYSize     = 0;
            _bytesPerRow   = 0;
            DisposeDerived();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Allocates the underlying aligned memory buffer.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation.</param>
        /// <param name="prePadding">Pre-padding in elements.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        protected Status Allocate(JxlMemoryManager? memoryManager, nuint prePadding)
        {
            if (_allocation != 0 || _bytesPerRow != 0)
            {
                return Status.Failure("Plane already allocated");
            }

            StatusOr<nuint> bprResult = JxlMemoryManager.BytesPerRow(_xsize, _sizeofT);

            if (!bprResult.IsOk)
            {
                return bprResult.Status;
            }

            _bytesPerRow = bprResult.Value;

            // Dimensions can be zero, e.g. for lazily-allocated images. Only allocate
            // if nonzero, because "zero" bytes still have padding/bookkeeping overhead.
            if (_xsize == 0 || _ysize == 0)
            {
                return true;
            }

            if (!Common.SafeMul(_ysize, _bytesPerRow, out nuint totalBytes))
            {
                return Status.Failure("Image dimensions are too large");
            }

            nuint prePaddingBytes = prePadding * _sizeofT;

            if (prePaddingBytes > JxlMemoryManager.Alias)
            {
                return Status.Failure("Pre-padding exceeds kAlias");
            }

            if (!Common.SafeAdd(totalBytes, prePaddingBytes, out nuint allocationSize) ||
                !Common.SafeAdd(allocationSize, JxlMemoryManager.Alias, out allocationSize))
            {
                return Status.Failure("Requested allocation is too large");
            }

            JxlMemoryManager mgr = memoryManager ?? JxlMemoryManager.Default;
            nint allocated       = JxlMemoryManager.MemoryManagerAlloc(mgr, allocationSize);

            if (allocated == 0)
            {
                return Status.Failure("Allocation failed");
            }

            Status status = AllocateTypedStorage(prePadding);

            if (!status)
            {
                JxlMemoryManager.MemoryManagerFree(mgr, allocated);

                return status;
            }

            _memoryManager = mgr;
            _allocation    = allocated;
            UpdateFinalization();

            return true;
        }

        private void UpdateFinalization()
        {
            if (_memoryManager is not null && !_memoryManager.IsDefault && _allocation != 0)
            {
                GC.ReRegisterForFinalize(this);
            }
            else
            {
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Allocates typed element storage for the derived plane.
        /// </summary>
        /// <param name="prePadding">Pre-padding in elements.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        protected abstract Status AllocateTypedStorage(nuint prePadding);

        /// <summary>
        /// Swaps derived storage fields with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other plane instance.</param>
        protected abstract void SwapDerived(PlaneBase other);

        /// <summary>
        /// Releases derived storage fields on disposal.
        /// </summary>
        protected abstract void DisposeDerived();
    }

    /// <summary>
    /// Single channel, aligned rows separated by padding
    /// (ported from <c>jxl::Plane&lt;T&gt;</c> in <c>lib/jxl/image.h</c>).
    /// </summary>
    /// <typeparam name="T">1, 2, 4, or 8-byte unmanaged sample type.</typeparam>
    public sealed class ImagePlane<T> : PlaneBase
        where T : unmanaged
    {
        /// <summary>
        /// Number of planes in a single-channel <see cref="ImagePlane{T}"/>.
        /// </summary>
        public const int NumPlanes = 1;

        private const int DefaultPrePaddingElements = 64;

        private T[] _elements = Array.Empty<T>();
        private int _originOffset;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="ImagePlane{T}"/> class.
        /// </summary>
        public ImagePlane()
        {
        }

        private ImagePlane(uint xsize, uint ysize)
            : base(xsize, ysize, (nuint)Unsafe.SizeOf<T>())
        {
        }

        /// <summary>
        /// Gets the number of elements (including padding) per row.
        /// WARNING: this must NOT be used to determine <see cref="PlaneBase.XSize"/>.
        /// </summary>
        public nint PixelsPerRow => (nint)(BytesPerRow / (nuint)Unsafe.SizeOf<T>());

        /// <summary>
        /// Gets the underlying managed element array.
        /// </summary>
        public T[] BackingArray => _elements;

        /// <summary>
        /// Creates and allocates a new <see cref="ImagePlane{T}"/> of dimensions <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation (defaults to <see cref="JxlMemoryManager.Default"/> if null).</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="prePadding">Optional pre-padding in elements.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the allocated plane or an error status.</returns>
        public static StatusOr<ImagePlane<T>> Create(
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize,
            nuint prePadding = 0)
        {
            int elemSize = Unsafe.SizeOf<T>();

            if (elemSize != 1 && elemSize != 2 && elemSize != 4 && elemSize != 8)
            {
                return Status.Failure("Only 1/2/4/8-byte samples are supported");
            }

            uint xsize32 = (uint)xsize;
            uint ysize32 = (uint)ysize;

            if (xsize32 != xsize || ysize32 != ysize)
            {
                return Status.Failure("Plane dimensions exceed uint32 range");
            }

            ImagePlane<T> plane = new ImagePlane<T>(xsize32, ysize32);
            Status status       = plane.Allocate(memoryManager, prePadding);

            if (!status)
            {
                plane.Dispose();

                return status;
            }

            return plane;
        }

        /// <summary>
        /// Returns the element index in <see cref="BackingArray"/> where row <paramref name="y"/> begins.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Element index in <see cref="BackingArray"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int RowOffset(nuint y)
        {
            return _originOffset + (int)(y * (nuint)PixelsPerRow);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> starting at row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Span starting at row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Row(nuint y)
        {
            if (_elements.Length == 0)
            {
                return Span<T>.Empty;
            }

            return _elements.AsSpan(RowOffset(y));
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> starting at row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Span starting at row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Row(int y)
        {
            return Row((nuint)y);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> starting at row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span starting at row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRow(nuint y)
        {
            if (_elements.Length == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            int offset = RowOffset(y);

            return new ReadOnlySpan<T>(_elements, offset, _elements.Length - offset);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> starting at row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span starting at row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRow(int y)
        {
            return ConstRow((nuint)y);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> starting at row <paramref name="y"/> and horizontal <paramref name="xOffset"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="xOffset">Horizontal offset in elements.</param>
        /// <returns>Span starting at (<paramref name="xOffset"/>, <paramref name="y"/>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> RowAt(nuint y, int xOffset)
        {
            if (_elements.Length == 0)
            {
                return Span<T>.Empty;
            }

            return _elements.AsSpan(RowOffset(y) + xOffset);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> starting at row <paramref name="y"/> and horizontal <paramref name="xOffset"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <param name="xOffset">Horizontal offset in elements.</param>
        /// <returns>Read-only span starting at (<paramref name="xOffset"/>, <paramref name="y"/>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRowAt(nuint y, int xOffset)
        {
            if (_elements.Length == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            int offset = RowOffset(y) + xOffset;

            return new ReadOnlySpan<T>(_elements, offset, _elements.Length - offset);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> of length <see cref="PlaneBase.XSize"/> for row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Span over valid pixels in row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> RowSpan(nuint y)
        {
            if (_elements.Length == 0 || XSize == 0)
            {
                return Span<T>.Empty;
            }

            return _elements.AsSpan(RowOffset(y), (int)XSize);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> of length <see cref="PlaneBase.XSize"/> for row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span over valid pixels in row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRowSpan(nuint y)
        {
            if (_elements.Length == 0 || XSize == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            return new ReadOnlySpan<T>(_elements, RowOffset(y), (int)XSize);
        }

        /// <summary>
        /// Returns a <see cref="Memory{T}"/> starting at row <paramref name="y"/>.
        /// </summary>
        /// <param name="y">Row index.</param>
        /// <returns>Memory starting at row <paramref name="y"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> RowMemory(nuint y)
        {
            if (_elements.Length == 0)
            {
                return Memory<T>.Empty;
            }

            return _elements.AsMemory(RowOffset(y));
        }

        /// <summary>
        /// Moves ownership of this plane into a new <see cref="ImagePlane{T}"/> instance.
        /// </summary>
        /// <returns>New <see cref="ImagePlane{T}"/> owning the buffer.</returns>
        public ImagePlane<T> Move()
        {
            ImagePlane<T> moved = new ImagePlane<T>();
            moved.Swap(this);

            return moved;
        }

        /// <inheritdoc />
        protected override Status AllocateTypedStorage(nuint prePadding)
        {
            nuint strideElements = (nuint)PixelsPerRow;
            nuint padElements    = Math.Max(prePadding, (nuint)DefaultPrePaddingElements);

            if (!Common.SafeMul(YSize, strideElements, out nuint bodyElements) ||
                !Common.SafeAdd(bodyElements, padElements * 2, out nuint totalElements) ||
                totalElements > (nuint)Array.MaxLength)
            {
                return Status.Failure("Image dimensions are too large");
            }

            try
            {
                _elements     = new T[(int)totalElements];
                _originOffset = (int)padElements;
            }
            catch (OutOfMemoryException)
            {
                return Status.Failure("Allocation failed");
            }

            return true;
        }

        /// <inheritdoc />
        protected override void SwapDerived(PlaneBase other)
        {
            if (other is ImagePlane<T> typedOther)
            {
                T[] tempElements = _elements;
                int tempOrigin   = _originOffset;

                _elements     = typedOther._elements;
                _originOffset = typedOther._originOffset;

                typedOther._elements     = tempElements;
                typedOther._originOffset = tempOrigin;
            }
        }

        /// <inheritdoc />
        protected override void DisposeDerived()
        {
            _elements     = Array.Empty<T>();
            _originOffset = 0;
        }
    }

    /// <summary>
    /// A bundle of 3 same-sized image planes
    /// (ported from <c>jxl::Image3&lt;T&gt;</c> in <c>lib/jxl/image.h</c>).
    /// </summary>
    /// <typeparam name="T">1, 2, 4, or 8-byte unmanaged sample type.</typeparam>
    public sealed class Image3<T> : IImageDimensions, IDisposable
        where T : unmanaged
    {
        /// <summary>
        /// Number of planes in an <see cref="Image3{T}"/>.
        /// </summary>
        public const int NumPlanes = 3;

        private readonly ImagePlane<T>[] _planes;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="Image3{T}"/> class.
        /// </summary>
        public Image3()
        {
            _planes =
            [
                new ImagePlane<T>(),
                new ImagePlane<T>(),
                new ImagePlane<T>(),
            ];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image3{T}"/> class by taking ownership of three planes.
        /// </summary>
        /// <param name="plane0">First plane.</param>
        /// <param name="plane1">Second plane.</param>
        /// <param name="plane2">Third plane.</param>
        public Image3(ImagePlane<T> plane0, ImagePlane<T> plane1, ImagePlane<T> plane2)
        {
            _planes = [plane0, plane1, plane2];
        }

        /// <summary>
        /// Gets the <see cref="JxlMemoryManager"/> associated with this image.
        /// </summary>
        public JxlMemoryManager? MemoryManager => _planes[0].MemoryManager;

        /// <inheritdoc />
        public nuint XSize => _planes[0].XSize;

        /// <inheritdoc />
        public nuint YSize => _planes[0].YSize;

        /// <summary>
        /// Gets the row stride in bytes (including alignment padding).
        /// </summary>
        public nuint BytesPerRow => _planes[0].BytesPerRow;

        /// <summary>
        /// Gets the number of elements (including padding) per row.
        /// </summary>
        public nint PixelsPerRow => _planes[0].PixelsPerRow;

        /// <summary>
        /// Creates and allocates a new 3-plane <see cref="Image3{T}"/> of dimensions <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation (defaults to <see cref="JxlMemoryManager.Default"/> if null).</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the allocated 3-plane image or an error status.</returns>
        public static StatusOr<Image3<T>> Create(JxlMemoryManager? memoryManager, nuint xsize, nuint ysize)
        {
            StatusOr<ImagePlane<T>> p0 = ImagePlane<T>.Create(memoryManager, xsize, ysize);

            if (!p0.IsOk)
            {
                return p0.Status;
            }

            StatusOr<ImagePlane<T>> p1 = ImagePlane<T>.Create(memoryManager, xsize, ysize);

            if (!p1.IsOk)
            {
                p0.Value.Dispose();

                return p1.Status;
            }

            StatusOr<ImagePlane<T>> p2 = ImagePlane<T>.Create(memoryManager, xsize, ysize);

            if (!p2.IsOk)
            {
                p0.Value.Dispose();
                p1.Value.Dispose();

                return p2.Status;
            }

            return new Image3<T>(p0.Value, p1.Value, p2.Value);
        }

        /// <summary>
        /// Returns a row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow(nuint c, nuint y)
        {
            return _planes[c].Row(y);
        }

        /// <summary>
        /// Returns a row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow(int c, nuint y)
        {
            return _planes[c].Row(y);
        }

        /// <summary>
        /// Returns a row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow(int c, int y)
        {
            return _planes[c].Row((nuint)y);
        }

        /// <summary>
        /// Returns a read-only row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow(nuint c, nuint y)
        {
            return _planes[c].ConstRow(y);
        }

        /// <summary>
        /// Returns a read-only row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow(int c, nuint y)
        {
            return _planes[c].ConstRow(y);
        }

        /// <summary>
        /// Returns a read-only row span for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Read-only span starting at row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow(int c, int y)
        {
            return _planes[c].ConstRow((nuint)y);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> of length <see cref="XSize"/> for plane <paramref name="c"/> and row <paramref name="y"/>.
        /// </summary>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <returns>Span over valid pixels in row <paramref name="y"/> of plane <paramref name="c"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRowSpan(nuint c, nuint y)
        {
            return _planes[c].RowSpan(y);
        }

        /// <summary>
        /// Gets the <see cref="ImagePlane{T}"/> at plane index <paramref name="idx"/> (0..2).
        /// </summary>
        /// <param name="idx">Plane index (0..2).</param>
        /// <returns>Plane at <paramref name="idx"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ImagePlane<T> Plane(nuint idx)
        {
            return _planes[idx];
        }

        /// <summary>
        /// Gets the <see cref="ImagePlane{T}"/> at plane index <paramref name="idx"/> (0..2).
        /// </summary>
        /// <param name="idx">Plane index (0..2).</param>
        /// <returns>Plane at <paramref name="idx"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ImagePlane<T> Plane(int idx)
        {
            return _planes[idx];
        }

        /// <summary>
        /// Swaps all three planes with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other <see cref="Image3{T}"/> to swap with.</param>
        public void Swap(Image3<T> other)
        {
            for (int c = 0; c < NumPlanes; ++c)
            {
                other._planes[c].Swap(_planes[c]);
            }
        }

        /// <summary>
        /// Shrinks (or un-shrinks within original allocation) all three planes to <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        /// <param name="xsize">New logical width in pixels.</param>
        /// <param name="ysize">New logical height in pixels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success; otherwise <see cref="StatusCode.GenericError"/>.</returns>
        public Status ShrinkTo(nuint xsize, nuint ysize)
        {
            for (int c = 0; c < NumPlanes; ++c)
            {
                Status status = _planes[c].ShrinkTo(xsize, ysize);

                if (!status)
                {
                    return status;
                }
            }

            return true;
        }

        /// <summary>
        /// Moves ownership of all three planes into a new <see cref="Image3{T}"/> instance.
        /// </summary>
        /// <returns>New <see cref="Image3{T}"/> owning the buffers.</returns>
        public Image3<T> Move()
        {
            Image3<T> moved = new Image3<T>();
            moved.Swap(this);

            return moved;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int c = 0; c < NumPlanes; ++c)
            {
                _planes[c].Dispose();
            }
        }
    }
}
