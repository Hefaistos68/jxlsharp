// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// 3x3 matrix operations.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// 3x3 matrix multiplication, matrix-vector multiplication, and matrix inversion operations.
    /// </summary>
    public static class MatrixOps
    {
        /// <summary>
        /// Computes <c>c = a * b</c>, where <paramref name="a"/>, <paramref name="b"/>, and <paramref name="c"/> are 3x3 row-major matrices (length 9).
        /// </summary>
        /// <param name="a">Left 3x3 matrix (9 elements, row-major).</param>
        /// <param name="b">Right 3x3 matrix (9 elements, row-major).</param>
        /// <param name="c">Output 3x3 matrix (9 elements, row-major).</param>
        public static void Mul3x3Matrix(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> c)
        {
            Span<double> temp = stackalloc double[3];

            for (int x = 0; x < 3; x++)
            {
                temp[0] = b[x];
                temp[1] = b[3 + x];
                temp[2] = b[6 + x];

                for (int y = 0; y < 3; y++)
                {
                    int rowOffset   = y * 3;
                    c[rowOffset + x] = (float)(
                        (a[rowOffset] * temp[0]) +
                        (a[rowOffset + 1] * temp[1]) +
                        (a[rowOffset + 2] * temp[2]));
                }
            }
        }

        /// <summary>
        /// Computes <c>c = a * b</c>, where <paramref name="a"/>, <paramref name="b"/>, and <paramref name="c"/> are double-precision 3x3 row-major matrices (length 9).
        /// </summary>
        /// <param name="a">Left 3x3 matrix (9 elements, row-major).</param>
        /// <param name="b">Right 3x3 matrix (9 elements, row-major).</param>
        /// <param name="c">Output 3x3 matrix (9 elements, row-major).</param>
        public static void Mul3x3Matrix(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> c)
        {
            Span<double> temp = stackalloc double[3];

            for (int x = 0; x < 3; x++)
            {
                temp[0] = b[x];
                temp[1] = b[3 + x];
                temp[2] = b[6 + x];

                for (int y = 0; y < 3; y++)
                {
                    int rowOffset    = y * 3;
                    c[rowOffset + x] =
                        (a[rowOffset] * temp[0]) +
                        (a[rowOffset + 1] * temp[1]) +
                        (a[rowOffset + 2] * temp[2]);
                }
            }
        }

        /// <summary>
        /// Computes <c>c = a * b</c>, where <paramref name="a"/> is a 3x3 row-major matrix and <paramref name="b"/> is a 3-element vector.
        /// </summary>
        /// <param name="a">3x3 matrix (9 elements, row-major).</param>
        /// <param name="b">Input 3-element vector.</param>
        /// <param name="c">Output 3-element vector.</param>
        public static void Mul3x3Vector(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> c)
        {
            for (int y = 0; y < 3; y++)
            {
                double e      = 0.0;
                int rowOffset = y * 3;

                for (int x = 0; x < 3; x++)
                {
                    e += (double)a[rowOffset + x] * b[x];
                }

                c[y] = (float)e;
            }
        }

        /// <summary>
        /// Computes <c>c = a * b</c>, where <paramref name="a"/> is a 3x3 row-major matrix and <paramref name="b"/> is a 3-element double vector.
        /// </summary>
        /// <param name="a">3x3 matrix (9 elements, row-major).</param>
        /// <param name="b">Input 3-element vector.</param>
        /// <param name="c">Output 3-element vector.</param>
        public static void Mul3x3Vector(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> c)
        {
            for (int y = 0; y < 3; y++)
            {
                double e      = 0.0;
                int rowOffset = y * 3;

                for (int x = 0; x < 3; x++)
                {
                    e += a[rowOffset + x] * b[x];
                }

                c[y] = e;
            }
        }

        /// <summary>
        /// Inverts a 3x3 row-major single-precision matrix in place.
        /// </summary>
        /// <param name="matrix">9-element row-major 3x3 matrix.</param>
        /// <returns><see cref="Status.OkStatus"/> if invertible; otherwise a failure status.</returns>
        public static Status Inv3x3Matrix(Span<float> matrix)
        {
            Span<double> temp = stackalloc double[9];

            for (int i = 0; i < 9; i++)
            {
                temp[i] = matrix[i];
            }

            Status status = Inv3x3Matrix(temp);

            if (!status)
            {
                return status;
            }

            for (int i = 0; i < 9; i++)
            {
                matrix[i] = (float)temp[i];
            }

            return true;
        }

        /// <summary>
        /// Inverts a 3x3 row-major double-precision matrix in place.
        /// </summary>
        /// <param name="matrix">9-element row-major 3x3 matrix.</param>
        /// <returns><see cref="Status.OkStatus"/> if invertible; otherwise a failure status.</returns>
        public static Status Inv3x3Matrix(Span<double> matrix)
        {
            // Intermediate computation is done in double precision.
            Span<double> temp = stackalloc double[9];

            temp[0] = (matrix[4] * matrix[8]) - (matrix[5] * matrix[7]);
            temp[1] = (matrix[2] * matrix[7]) - (matrix[1] * matrix[8]);
            temp[2] = (matrix[1] * matrix[5]) - (matrix[2] * matrix[4]);
            temp[3] = (matrix[5] * matrix[6]) - (matrix[3] * matrix[8]);
            temp[4] = (matrix[0] * matrix[8]) - (matrix[2] * matrix[6]);
            temp[5] = (matrix[2] * matrix[3]) - (matrix[0] * matrix[5]);
            temp[6] = (matrix[3] * matrix[7]) - (matrix[4] * matrix[6]);
            temp[7] = (matrix[1] * matrix[6]) - (matrix[0] * matrix[7]);
            temp[8] = (matrix[0] * matrix[4]) - (matrix[1] * matrix[3]);

            double det = (matrix[0] * temp[0]) + (matrix[1] * temp[3]) + (matrix[2] * temp[6]);

            if (Math.Abs(det) < 1e-10)
            {
                return Status.Failure("Matrix determinant is too close to 0");
            }

            double idet = 1.0 / det;

            for (int i = 0; i < 9; i++)
            {
                matrix[i] = temp[i] * idet;
            }

            return true;
        }

        /// <summary>
        /// Computes <c>c = a * b</c> for two <see cref="Matrix3x3"/> matrices.
        /// </summary>
        /// <param name="a">Left 3x3 matrix.</param>
        /// <param name="b">Right 3x3 matrix.</param>
        /// <param name="c">Output 3x3 matrix.</param>
        public static void Mul3x3Matrix(in Matrix3x3 a, in Matrix3x3 b, out Matrix3x3 c)
        {
            Span<float> sa = stackalloc float[9];
            Span<float> sb = stackalloc float[9];
            Span<float> sc = stackalloc float[9];

            a.CopyTo(sa);
            b.CopyTo(sb);
            Mul3x3Matrix(sa, sb, sc);
            c = Matrix3x3.FromSpan(sc);
        }

        /// <summary>
        /// Computes <c>c = a * b</c> for two <see cref="Matrix3x3d"/> matrices.
        /// </summary>
        /// <param name="a">Left 3x3 double matrix.</param>
        /// <param name="b">Right 3x3 double matrix.</param>
        /// <param name="c">Output 3x3 double matrix.</param>
        public static void Mul3x3Matrix(in Matrix3x3d a, in Matrix3x3d b, out Matrix3x3d c)
        {
            Span<double> sa = stackalloc double[9];
            Span<double> sb = stackalloc double[9];
            Span<double> sc = stackalloc double[9];

            a.CopyTo(sa);
            b.CopyTo(sb);
            Mul3x3Matrix(sa, sb, sc);
            c = Matrix3x3d.FromSpan(sc);
        }

        /// <summary>
        /// Computes <c>c = a * b</c> where <paramref name="a"/> is a <see cref="Matrix3x3"/> and <paramref name="b"/> is a <see cref="Vector3"/>.
        /// </summary>
        /// <param name="a">3x3 matrix.</param>
        /// <param name="b">Input 3-element vector.</param>
        /// <param name="c">Output 3-element vector.</param>
        public static void Mul3x3Vector(in Matrix3x3 a, in Vector3 b, out Vector3 c)
        {
            Span<float> sa = stackalloc float[9];
            Span<float> sb = stackalloc float[3] { b.V0, b.V1, b.V2 };
            Span<float> sc = stackalloc float[3];

            a.CopyTo(sa);
            Mul3x3Vector(sa, sb, sc);
            c = new Vector3(sc[0], sc[1], sc[2]);
        }

        /// <summary>
        /// Computes <c>c = a * b</c> where <paramref name="a"/> is a <see cref="Matrix3x3d"/> and <paramref name="b"/> is a <see cref="Vector3d"/>.
        /// </summary>
        /// <param name="a">3x3 double matrix.</param>
        /// <param name="b">Input 3-element double vector.</param>
        /// <param name="c">Output 3-element double vector.</param>
        public static void Mul3x3Vector(in Matrix3x3d a, in Vector3d b, out Vector3d c)
        {
            Span<double> sa = stackalloc double[9];
            Span<double> sb = stackalloc double[3] { b.V0, b.V1, b.V2 };
            Span<double> sc = stackalloc double[3];

            a.CopyTo(sa);
            Mul3x3Vector(sa, sb, sc);
            c = new Vector3d(sc[0], sc[1], sc[2]);
        }

        /// <summary>
        /// Inverts a <see cref="Matrix3x3"/> in place.
        /// </summary>
        /// <param name="matrix">3x3 matrix to invert.</param>
        /// <returns><see cref="Status.OkStatus"/> if invertible; otherwise a failure status.</returns>
        public static Status Inv3x3Matrix(ref Matrix3x3 matrix)
        {
            Span<float> s = stackalloc float[9];
            matrix.CopyTo(s);

            Status status = Inv3x3Matrix(s);

            if (!status)
            {
                return status;
            }

            matrix = Matrix3x3.FromSpan(s);

            return true;
        }

        /// <summary>
        /// Inverts a <see cref="Matrix3x3d"/> in place.
        /// </summary>
        /// <param name="matrix">3x3 double matrix to invert.</param>
        /// <returns><see cref="Status.OkStatus"/> if invertible; otherwise a failure status.</returns>
        public static Status Inv3x3Matrix(ref Matrix3x3d matrix)
        {
            Span<double> s = stackalloc double[9];
            matrix.CopyTo(s);

            Status status = Inv3x3Matrix(s);

            if (!status)
            {
                return status;
            }

            matrix = Matrix3x3d.FromSpan(s);

            return true;
        }
    }

    /// <summary>
    /// 3-element single-precision vector (<c>jxl::Vector3</c>).
    /// </summary>
    public struct Vector3
    {
        /// <summary>First element.</summary>
        public float V0;

        /// <summary>Second element.</summary>
        public float V1;

        /// <summary>Third element.</summary>
        public float V2;

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3"/> struct.
        /// </summary>
        /// <param name="v0">First element.</param>
        /// <param name="v1">Second element.</param>
        /// <param name="v2">Third element.</param>
        public Vector3(float v0, float v1, float v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        /// <summary>
        /// Gets or sets the element at the specified index (0..2).
        /// </summary>
        /// <param name="index">Element index.</param>
        /// <returns>Element value.</returns>
        public float this[int index]
        {
            readonly get => index switch
            {
                0 => V0,
                1 => V1,
                2 => V2,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
            set
            {
                switch (index)
                {
                    case 0:
                        V0 = value;
                        break;
                    case 1:
                        V1 = value;
                        break;
                    case 2:
                        V2 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }
    }

    /// <summary>
    /// 3-element double-precision vector (<c>jxl::Vector3d</c>).
    /// </summary>
    public struct Vector3d
    {
        /// <summary>First element.</summary>
        public double V0;

        /// <summary>Second element.</summary>
        public double V1;

        /// <summary>Third element.</summary>
        public double V2;

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3d"/> struct.
        /// </summary>
        /// <param name="v0">First element.</param>
        /// <param name="v1">Second element.</param>
        /// <param name="v2">Third element.</param>
        public Vector3d(double v0, double v1, double v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        /// <summary>
        /// Gets or sets the element at the specified index (0..2).
        /// </summary>
        /// <param name="index">Element index.</param>
        /// <returns>Element value.</returns>
        public double this[int index]
        {
            readonly get => index switch
            {
                0 => V0,
                1 => V1,
                2 => V2,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
            set
            {
                switch (index)
                {
                    case 0:
                        V0 = value;
                        break;
                    case 1:
                        V1 = value;
                        break;
                    case 2:
                        V2 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }
    }

    /// <summary>
    /// 3x3 single-precision matrix (<c>jxl::Matrix3x3</c>).
    /// </summary>
    public struct Matrix3x3
    {
        /// <summary>Row 0.</summary>
        public Vector3 R0;

        /// <summary>Row 1.</summary>
        public Vector3 R1;

        /// <summary>Row 2.</summary>
        public Vector3 R2;

        /// <summary>
        /// Initializes a new instance of the <see cref="Matrix3x3"/> struct.
        /// </summary>
        /// <param name="r0">Row 0.</param>
        /// <param name="r1">Row 1.</param>
        /// <param name="r2">Row 2.</param>
        public Matrix3x3(Vector3 r0, Vector3 r1, Vector3 r2)
        {
            R0 = r0;
            R1 = r1;
            R2 = r2;
        }

        /// <summary>
        /// Gets or sets the row vector at the specified row index (0..2).
        /// </summary>
        /// <param name="row">Row index.</param>
        /// <returns>Row vector.</returns>
        public Vector3 this[int row]
        {
            readonly get => row switch
            {
                0 => R0,
                1 => R1,
                2 => R2,
                _ => throw new ArgumentOutOfRangeException(nameof(row)),
            };
            set
            {
                switch (row)
                {
                    case 0:
                        R0 = value;
                        break;
                    case 1:
                        R1 = value;
                        break;
                    case 2:
                        R2 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(row));
                }
            }
        }

        /// <summary>
        /// Creates a <see cref="Matrix3x3"/> from a 9-element row-major span.
        /// </summary>
        /// <param name="span">9-element row-major span.</param>
        /// <returns>Constructed <see cref="Matrix3x3"/>.</returns>
        public static Matrix3x3 FromSpan(ReadOnlySpan<float> span)
        {
            return new Matrix3x3(
                new Vector3(span[0], span[1], span[2]),
                new Vector3(span[3], span[4], span[5]),
                new Vector3(span[6], span[7], span[8]));
        }

        /// <summary>
        /// Copies the 9 matrix elements in row-major order into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">Destination span of length at least 9.</param>
        public readonly void CopyTo(Span<float> destination)
        {
            destination[0] = R0.V0;
            destination[1] = R0.V1;
            destination[2] = R0.V2;
            destination[3] = R1.V0;
            destination[4] = R1.V1;
            destination[5] = R1.V2;
            destination[6] = R2.V0;
            destination[7] = R2.V1;
            destination[8] = R2.V2;
        }
    }

    /// <summary>
    /// 3x3 double-precision matrix (<c>jxl::Matrix3x3d</c>).
    /// </summary>
    public struct Matrix3x3d
    {
        /// <summary>Row 0.</summary>
        public Vector3d R0;

        /// <summary>Row 1.</summary>
        public Vector3d R1;

        /// <summary>Row 2.</summary>
        public Vector3d R2;

        /// <summary>
        /// Initializes a new instance of the <see cref="Matrix3x3d"/> struct.
        /// </summary>
        /// <param name="r0">Row 0.</param>
        /// <param name="r1">Row 1.</param>
        /// <param name="r2">Row 2.</param>
        public Matrix3x3d(Vector3d r0, Vector3d r1, Vector3d r2)
        {
            R0 = r0;
            R1 = r1;
            R2 = r2;
        }

        /// <summary>
        /// Gets or sets the row vector at the specified row index (0..2).
        /// </summary>
        /// <param name="row">Row index.</param>
        /// <returns>Row vector.</returns>
        public Vector3d this[int row]
        {
            readonly get => row switch
            {
                0 => R0,
                1 => R1,
                2 => R2,
                _ => throw new ArgumentOutOfRangeException(nameof(row)),
            };
            set
            {
                switch (row)
                {
                    case 0:
                        R0 = value;
                        break;
                    case 1:
                        R1 = value;
                        break;
                    case 2:
                        R2 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(row));
                }
            }
        }

        /// <summary>
        /// Creates a <see cref="Matrix3x3d"/> from a 9-element row-major span.
        /// </summary>
        /// <param name="span">9-element row-major span.</param>
        /// <returns>Constructed <see cref="Matrix3x3d"/>.</returns>
        public static Matrix3x3d FromSpan(ReadOnlySpan<double> span)
        {
            return new Matrix3x3d(
                new Vector3d(span[0], span[1], span[2]),
                new Vector3d(span[3], span[4], span[5]),
                new Vector3d(span[6], span[7], span[8]));
        }

        /// <summary>
        /// Copies the 9 matrix elements in row-major order into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">Destination span of length at least 9.</param>
        public readonly void CopyTo(Span<double> destination)
        {
            destination[0] = R0.V0;
            destination[1] = R0.V1;
            destination[2] = R0.V2;
            destination[3] = R1.V0;
            destination[4] = R1.V1;
            destination[5] = R1.V2;
            destination[6] = R2.V0;
            destination[7] = R2.V1;
            destination[8] = R2.V2;
        }
    }
}
