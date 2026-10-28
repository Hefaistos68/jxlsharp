// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Rectangular region in image(s). Factoring this out of Image instead of
    /// shifting the pointer by x0/y0 allows this to apply to multiple images with
    /// different resolutions (e.g. color transform and quantization field).
    /// </summary>
    public readonly struct Rect : IEquatable<Rect>
    {
        private readonly int _x0;
        private readonly int _y0;
        private readonly int _xsize;
        private readonly int _ysize;

        /// <summary>
        /// Initializes a new instance of the <see cref="Rect"/> struct with clamped size.
        /// Most windows are xsizeMax * ysizeMax, except those on the borders where
        /// begin + sizeMax &gt; end.
        /// </summary>
        /// <param name="xbegin">Starting X coordinate.</param>
        /// <param name="ybegin">Starting Y coordinate.</param>
        /// <param name="xsizeMax">Maximum width.</param>
        /// <param name="ysizeMax">Maximum height.</param>
        /// <param name="xend">Ending X bound.</param>
        /// <param name="yend">Ending Y bound.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect(int xbegin, int ybegin, int xsizeMax, int ysizeMax, int xend, int yend)
        {
            _x0    = xbegin;
            _y0    = ybegin;
            _xsize = ClampedSize(xbegin, xsizeMax, xend);
            _ysize = ClampedSize(ybegin, ysizeMax, yend);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rect"/> struct with origin and known size.
        /// </summary>
        /// <param name="xbegin">Starting X coordinate.</param>
        /// <param name="ybegin">Starting Y coordinate.</param>
        /// <param name="xsize">Width.</param>
        /// <param name="ysize">Height.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect(int xbegin, int ybegin, int xsize, int ysize)
        {
            _x0    = xbegin;
            _y0    = ybegin;
            _xsize = xsize;
            _ysize = ysize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rect"/> struct covering the entire <paramref name="image"/>.
        /// </summary>
        /// <param name="image">Image or plane dimensions.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect(IImageDimensions image)
            : this(0, 0, (int)image.XSize, (int)image.YSize)
        {
        }

        /// <summary>
        /// Gets the inclusive starting X coordinate.
        /// </summary>
        public int X0
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _x0;
        }

        /// <summary>
        /// Gets the inclusive starting Y coordinate.
        /// </summary>
        public int Y0
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _y0;
        }

        /// <summary>
        /// Gets the horizontal width.
        /// </summary>
        public int XSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _xsize;
        }

        /// <summary>
        /// Gets the vertical height.
        /// </summary>
        public int YSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _ysize;
        }

        /// <summary>
        /// Gets the exclusive ending X coordinate (<c>X0 + XSize</c>).
        /// </summary>
        public int X1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _x0 + _xsize;
        }

        /// <summary>
        /// Gets the exclusive ending Y coordinate (<c>Y0 + YSize</c>).
        /// </summary>
        public int Y1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _y0 + _ysize;
        }

        /// <summary>
        /// Construct a subrect that resides in the given <paramref name="image"/>.
        /// </summary>
        /// <param name="image">Image or plane dimensions.</param>
        /// <returns>Cropped rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Crop(IImageDimensions image)
        {
            return Intersection(new Rect(image));
        }

        /// <summary>
        /// Construct a subrect that resides in the [0, areaYSize) x [0, areaXSize) region of
        /// the current rect.
        /// </summary>
        /// <param name="areaXSize">Area width.</param>
        /// <param name="areaYSize">Area height.</param>
        /// <returns>Cropped rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Crop(int areaXSize, int areaYSize)
        {
            return Intersection(new Rect(0, 0, areaXSize, areaYSize));
        }

        /// <summary>
        /// Returns the intersection of this rectangle with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other rectangle.</param>
        /// <returns>Intersection rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Intersection(Rect other)
        {
            return new Rect(
                Math.Max(_x0, other._x0),
                Math.Max(_y0, other._y0),
                _xsize,
                _ysize,
                Math.Min(X1, other.X1),
                Math.Min(Y1, other.Y1));
        }

        /// <summary>
        /// Returns a new rectangle translated by the specified offsets.
        /// </summary>
        /// <param name="xOffset">Horizontal offset.</param>
        /// <param name="yOffset">Vertical offset.</param>
        /// <returns>Translated rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Translate(int xOffset, int yOffset)
        {
            return new Rect(_x0 + xOffset, _y0 + yOffset, _xsize, _ysize);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Target plane.</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Row<T>(ImagePlane<T> image, nuint y)
            where T : unmanaged
        {
            return image.RowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Target plane.</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Row<T>(ImagePlane<T> image, int y)
            where T : unmanaged
        {
            return image.RowAt((nuint)(y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Source plane.</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRow<T>(ImagePlane<T> image, nuint y)
            where T : unmanaged
        {
            return image.ConstRowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Source plane.</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstRow<T>(ImagePlane<T> image, int y)
            where T : unmanaged
        {
            return image.ConstRowAt((nuint)(y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Target 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow<T>(Image3<T> image, nuint c, nuint y)
            where T : unmanaged
        {
            return image.Plane(c).RowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Target 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow<T>(Image3<T> image, int c, nuint y)
            where T : unmanaged
        {
            return image.Plane(c).RowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Target 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> PlaneRow<T>(Image3<T> image, int c, int y)
            where T : unmanaged
        {
            return image.Plane(c).RowAt((nuint)(y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Source 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow<T>(Image3<T> image, nuint c, nuint y)
            where T : unmanaged
        {
            return image.Plane(c).ConstRowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Source 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow<T>(Image3<T> image, int c, nuint y)
            where T : unmanaged
        {
            return image.Plane(c).ConstRowAt((nuint)((int)y + _y0), _x0);
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{T}"/> for row <c>y + Y0</c> offset by <c>X0</c> in plane <paramref name="c"/> of <paramref name="image"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="image">Source 3-plane image.</param>
        /// <param name="c">Plane index (0..2).</param>
        /// <param name="y">Relative row index within this rectangle.</param>
        /// <returns>Offset read-only row span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> ConstPlaneRow<T>(Image3<T> image, int c, int y)
            where T : unmanaged
        {
            return image.Plane(c).ConstRowAt((nuint)(y + _y0), _x0);
        }

        /// <summary>
        /// Returns whether this rectangle is completely inside <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Enclosing rectangle candidate.</param>
        /// <returns><c>true</c> if inside; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInside(Rect other)
        {
            return _x0 >= other.X0 && X1 <= other.X1 && _y0 >= other.Y0 && Y1 <= other.Y1;
        }

        /// <summary>
        /// Returns whether this rectangle fully resides in the given <paramref name="image"/>.
        /// </summary>
        /// <param name="image">Image or plane dimensions.</param>
        /// <returns><c>true</c> if inside; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInside(IImageDimensions image)
        {
            return IsInside(new Rect(image));
        }

        /// <summary>
        /// Returns whether this rectangle has identical coordinates and size as <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other rectangle.</param>
        /// <returns><c>true</c> if identical; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSame(Rect other)
        {
            return _x0 == other._x0 && _xsize == other._xsize && _y0 == other._y0 && _ysize == other._ysize;
        }

        /// <summary>
        /// Scales the rectangle coordinates and dimensions left by <paramref name="shiftX"/> and <paramref name="shiftY"/>.
        /// </summary>
        /// <param name="shiftX">Horizontal bit shift.</param>
        /// <param name="shiftY">Vertical bit shift.</param>
        /// <returns>Shifted rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect ShiftLeft(int shiftX, int shiftY)
        {
            return new Rect(
                _x0 * (1 << shiftX),
                _y0 * (1 << shiftY),
                _xsize << shiftX,
                _ysize << shiftY);
        }

        /// <summary>
        /// Scales the rectangle coordinates and dimensions left by <paramref name="shift"/> in both axes.
        /// </summary>
        /// <param name="shift">Bit shift.</param>
        /// <returns>Shifted rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect ShiftLeft(int shift)
        {
            return ShiftLeft(shift, shift);
        }

        /// <summary>
        /// Scales the rectangle right with ceiling division on dimensions.
        /// Requires X0, Y0 to be multiples of <c>1 &lt;&lt; shiftX</c> and <c>1 &lt;&lt; shiftY</c>.
        /// </summary>
        /// <param name="shiftX">Horizontal bit shift.</param>
        /// <param name="shiftY">Vertical bit shift.</param>
        /// <returns><see cref="StatusOr{Rect}"/> containing the shifted rectangle or an error status.</returns>
        public StatusOr<Rect> CeilShiftRight(int shiftX, int shiftY)
        {
            int scaleX = 1 << shiftX;
            int scaleY = 1 << shiftY;

            if ((_x0 % scaleX != 0) || (_y0 % scaleY != 0))
            {
                return Status.Failure("X0 and Y0 must be multiples of shift scale.");
            }

            return new Rect(
                _x0 / scaleX,
                _y0 / scaleY,
                Common.DivCeil(_xsize, scaleX),
                Common.DivCeil(_ysize, scaleY));
        }

        /// <summary>
        /// Extends the rectangle by <paramref name="border"/> pixels in all directions, clamped to <paramref name="parent"/>.
        /// </summary>
        /// <param name="border">Border thickness in pixels.</param>
        /// <param name="parent">Bounding parent rectangle.</param>
        /// <returns>Extended rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(int border, Rect parent)
        {
            int newX0 = _x0 > parent.X0 + border ? _x0 - border : parent.X0;
            int newY0 = _y0 > parent.Y0 + border ? _y0 - border : parent.Y0;
            int newX1 = X1 + border > parent.X1 ? parent.X1 : X1 + border;
            int newY1 = Y1 + border > parent.Y1 ? parent.Y1 : Y1 + border;

            return new Rect(newX0, newY0, newX1 - newX0, newY1 - newY0);
        }

        /// <summary>
        /// Returns a formatted description of the rectangle in the form <c>[x0..x1)x[y0..y1)</c>.
        /// </summary>
        /// <returns>Formatted description string.</returns>
        public string Description()
        {
            return $"[{_x0}..{X1})x[{_y0}..{Y1})";
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Description();
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Rect other)
        {
            return IsSame(other);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is Rect other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(_x0, _y0, _xsize, _ysize);
        }

        /// <summary>
        /// Determines whether two <see cref="Rect"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Rect left, Rect right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="Rect"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Rect left, Rect right)
        {
            return !left.Equals(right);
        }

        // Returns sizeMax, or whatever is left in [begin, end).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ClampedSize(int begin, int sizeMax, int end)
        {
            if ((long)begin + sizeMax <= end)
            {
                return sizeMax;
            }

            return end > begin ? end - begin : 0;
        }
    }
}
