// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Error handling: Status return type + helper methods.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Status codes for JxlSharp operations.
    /// </summary>
    public enum StatusCode
    {
        // Non-fatal errors (negative values).

        /// <summary>
        /// More input bytes are required to complete the operation.
        /// </summary>
        NotEnoughBytes = -1,

        // The only non-error status code.

        /// <summary>
        /// Operation completed successfully.
        /// </summary>
        Ok = 0,

        // Fatal-errors (positive values)

        /// <summary>
        /// A generic fatal error occurred.
        /// </summary>
        GenericError = 1,

        /// <summary>
        /// The requested feature or stream is unsupported.
        /// </summary>
        Unsupported = 2,
    }

    /// <summary>
    /// Drop-in replacement for bool that carries a <see cref="StatusCode"/>.
    /// In case of error, the status can carry an extra error code in its value which
    /// is split between fatal and non-fatal error codes.
    /// </summary>
    public readonly struct Status : IEquatable<Status>
    {
        private readonly StatusCode _code;

        /// <summary>
        /// Initializes a new instance of the <see cref="Status"/> struct from a boolean value.
        /// "true" means Ok while "false" means a generic fatal error.
        /// </summary>
        /// <param name="ok">Whether the operation succeeded.</param>
        public Status(bool ok)
        {
            _code = ok ? StatusCode.Ok : StatusCode.GenericError;

            if (!ok)
            {
                _lastFailureMessage = Environment.StackTrace;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Status"/> struct from a <see cref="StatusCode"/>.
        /// </summary>
        /// <param name="code">The status code.</param>
        public Status(StatusCode code)
        {
            _code = code;

            if (code != StatusCode.Ok && string.IsNullOrEmpty(_lastFailureMessage))
            {
                _lastFailureMessage = Environment.StackTrace;
            }
        }

        /// <summary>
        /// Gets a successful <see cref="Status"/> instance.
        /// </summary>
        public static Status OkStatus => new Status(StatusCode.Ok);

        /// <summary>
        /// Gets a successful <see cref="Status"/> instance.
        /// </summary>
        public static Status Ok => new Status(StatusCode.Ok);

        /// <summary>
        /// Gets the underlying <see cref="StatusCode"/>.
        /// </summary>
        public StatusCode Code => _code;

        /// <summary>
        /// Gets a value indicating whether the status represents success (<see cref="StatusCode.Ok"/>).
        /// </summary>
        public bool IsOk => _code == StatusCode.Ok;

        /// <summary>
        /// Returns whether the status code is a fatal error.
        /// </summary>
        /// <returns><c>true</c> if fatal; otherwise, <c>false</c>.</returns>
        public bool IsFatalError()
        {
            return (int)_code > 0;
        }

        private static string? _lastFailureMessage;

        /// <summary>
        /// Gets the most recent diagnostic message passed to <see cref="Failure"/>.
        /// </summary>
        public static string? LastFailureMessage => _lastFailureMessage;

        /// <summary>
        /// Clears the most recent diagnostic message.
        /// </summary>
        public static void ClearLastFailure()
        {
            _lastFailureMessage = null;
        }

        /// <summary>
        /// Creates a generic failure <see cref="Status"/>.
        /// </summary>
        /// <param name="message">Optional diagnostic message.</param>
        /// <returns>A failure <see cref="Status"/>.</returns>
        public static Status Failure(string? message = null)
        {
            _lastFailureMessage = !string.IsNullOrEmpty(message) ? message : Environment.StackTrace;

            return new Status(StatusCode.GenericError);
        }

        /// <summary>
        /// Creates an unsupported <see cref="Status"/>.
        /// </summary>
        /// <param name="message">Optional diagnostic message.</param>
        /// <returns>An unsupported <see cref="Status"/>.</returns>
        public static Status Unsupported(string? message = null)
        {
            _ = message;

            return new Status(StatusCode.Unsupported);
        }

        /// <summary>
        /// Creates a not-enough-bytes <see cref="Status"/>.
        /// </summary>
        /// <param name="message">Optional diagnostic message.</param>
        /// <returns>A not-enough-bytes <see cref="Status"/>.</returns>
        public static Status NotEnoughBytes(string? message = null)
        {
            _ = message;

            return new Status(StatusCode.NotEnoughBytes);
        }

        /// <summary>
        /// Implicitly converts a boolean to a <see cref="Status"/>.
        /// </summary>
        /// <param name="ok">Boolean value.</param>
        public static implicit operator Status(bool ok)
        {
            return new Status(ok);
        }

        /// <summary>
        /// Implicitly converts a <see cref="StatusCode"/> to a <see cref="Status"/>.
        /// </summary>
        /// <param name="code">Status code.</param>
        public static implicit operator Status(StatusCode code)
        {
            return new Status(code);
        }

        /// <summary>
        /// Implicitly converts a <see cref="Status"/> to a boolean (<c>true</c> when <see cref="StatusCode.Ok"/>).
        /// </summary>
        /// <param name="status">Status value.</param>
        public static implicit operator bool(Status status)
        {
            return status._code == StatusCode.Ok;
        }

        /// <inheritdoc />
        public bool Equals(Status other)
        {
            return _code == other._code;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is Status other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)_code;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _code.ToString();
        }

        /// <summary>
        /// Determines whether two <see cref="Status"/> instances are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(Status left, Status right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="Status"/> instances are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(Status left, Status right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Holds either a valid result of type <typeparamref name="T"/> or an error <see cref="StatusCode"/>.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    public readonly struct StatusOr<T>
    {
        private readonly T? _value;
        private readonly StatusCode _code;

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusOr{T}"/> struct with an error <see cref="StatusCode"/>.
        /// </summary>
        /// <param name="code">Non-OK error status code.</param>
        public StatusOr(StatusCode code)
        {
            if (code == StatusCode.Ok)
            {
                throw new ArgumentException("StatusOr error constructor requires a non-Ok StatusCode.", nameof(code));
            }

            _value = default;
            _code  = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusOr{T}"/> struct with an error <see cref="Status"/>.
        /// </summary>
        /// <param name="status">Non-OK error status.</param>
        public StatusOr(Status status)
            : this(status.Code)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusOr{T}"/> struct with a valid result value.
        /// </summary>
        /// <param name="value">The result value.</param>
        public StatusOr(T value)
        {
            _value = value;
            _code  = StatusCode.Ok;
        }

        /// <summary>
        /// Gets a value indicating whether the operation succeeded.
        /// </summary>
        public bool IsOk => _code == StatusCode.Ok;

        /// <summary>
        /// Gets the <see cref="Status"/> of the operation.
        /// </summary>
        public Status Status => new Status(_code);

        /// <summary>
        /// Gets the underlying value if <see cref="IsOk"/> is <c>true</c>; otherwise throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public T Value
        {
            get
            {
                if (!IsOk)
                {
                    throw new InvalidOperationException($"Cannot access Value when Status is {_code}.");
                }

                return _value!;
            }
        }

        /// <summary>
        /// Implicitly converts a value of type <typeparamref name="T"/> into a successful <see cref="StatusOr{T}"/>.
        /// </summary>
        /// <param name="value">Result value.</param>
        public static implicit operator StatusOr<T>(T value)
        {
            return new StatusOr<T>(value);
        }

        /// <summary>
        /// Implicitly converts an error <see cref="StatusCode"/> into a failed <see cref="StatusOr{T}"/>.
        /// </summary>
        /// <param name="code">Error status code.</param>
        public static implicit operator StatusOr<T>(StatusCode code)
        {
            return new StatusOr<T>(code);
        }

        /// <summary>
        /// Implicitly converts an error <see cref="Status"/> into a failed <see cref="StatusOr{T}"/>.
        /// </summary>
        /// <param name="status">Error status.</param>
        public static implicit operator StatusOr<T>(Status status)
        {
            return new StatusOr<T>(status);
        }

        /// <summary>
        /// Implicitly converts a <see cref="StatusOr{T}"/> to a boolean (<c>true</c> when <see cref="IsOk"/>).
        /// </summary>
        /// <param name="statusOr">StatusOr value.</param>
        public static implicit operator bool(StatusOr<T> statusOr)
        {
            return statusOr.IsOk;
        }
    }
}
