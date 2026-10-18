// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Bounds-checked bit reader; 64-bit buffer with support for deferred refills
// and switching to reading byte-aligned words.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Reads bits previously written to memory by <see cref="BitWriter"/>.
    /// Uses unaligned 8-byte little-endian loads.
    /// Ported from <c>jxl::BitReader</c> in <c>lib/jxl/dec_bit_reader.h</c> and <c>dec_bit_reader.cc</c>.
    /// </summary>
    public sealed class BitReader : IDisposable
    {
        /// <summary>
        /// Maximum number of bits that can be peeked or read in a single call.
        /// </summary>
        public const int KMaxBitsPerCall = 56;

        private byte[] _data;
        private int _startOffset;
        private int _length;
        private int _nextByte;
        private int _endMinus8;
        private ulong _buf;
        private nuint _bitsInBuf;
        private ulong _overreadBytes;
        private bool _closeCalled;
        private bool _isInitialized;
        private ulong _checkedOutOfBoundsBits;

        /// <summary>
        /// Initializes a new uninitialized instance of the <see cref="BitReader"/> class,
        /// to be overwritten via <see cref="TransferFrom"/> or <see cref="Reset(ReadOnlySpan{byte})"/> before usage.
        /// </summary>
        public BitReader()
        {
            _data          = Array.Empty<byte>();
            _startOffset   = 0;
            _length        = 0;
            _nextByte      = 0;
            _endMinus8     = -8;
            _buf           = 0;
            _bitsInBuf     = 0;
            _isInitialized = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BitReader"/> class over a byte span.
        /// </summary>
        /// <param name="bytes">Input bytes (need not be aligned nor padded).</param>
        public BitReader(ReadOnlySpan<byte> bytes)
        {
            _data          = bytes.ToArray();
            _startOffset   = 0;
            _length        = _data.Length;
            _nextByte      = 0;
            _endMinus8     = _length - 8;
            _buf           = 0;
            _bitsInBuf     = 0;
            _isInitialized = true;
            Refill();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BitReader"/> class over a slice of a backing byte array without copying.
        /// </summary>
        /// <param name="data">Backing byte array.</param>
        /// <param name="offset">Start offset in bytes.</param>
        /// <param name="length">Length in bytes.</param>
        public BitReader(byte[] data, int offset, int length)
        {
            _data          = data ?? Array.Empty<byte>();
            _startOffset   = offset;
            _length        = length;
            _nextByte      = offset;
            _endMinus8     = offset + length - 8;
            _buf           = 0;
            _bitsInBuf     = 0;
            _isInitialized = true;
            Refill();
        }

        /// <summary>
        /// Resets this <see cref="BitReader"/> over a new byte span.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        public void Reset(ReadOnlySpan<byte> bytes)
        {
            _data                   = bytes.ToArray();
            _startOffset            = 0;
            _length                 = _data.Length;
            _nextByte               = 0;
            _endMinus8              = _length - 8;
            _buf                    = 0;
            _bitsInBuf              = 0;
            _overreadBytes          = 0;
            _closeCalled            = false;
            _checkedOutOfBoundsBits = 0;
            _isInitialized          = true;
            Refill();
        }

        /// <summary>
        /// Transfers ownership of state from <paramref name="other"/> to this instance
        /// (equivalent to C++ move assignment <c>operator=</c>), invalidating <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Source <see cref="BitReader"/> to move from.</param>
        public void TransferFrom(BitReader other)
        {
            _buf                    = other._buf;
            _bitsInBuf              = other._bitsInBuf;
            _data                   = other._data;
            _startOffset            = other._startOffset;
            _length                 = other._length;
            _nextByte               = other._nextByte;
            _endMinus8              = other._endMinus8;
            _overreadBytes          = other._overreadBytes;
            _closeCalled            = other._closeCalled;
            _checkedOutOfBoundsBits = other._checkedOutOfBoundsBits;
            _isInitialized          = other._isInitialized;

            other._isInitialized = false;
            other._closeCalled   = true;
        }

        /// <summary>
        /// Refills the 64-bit bit buffer so that at least 56 bits are available.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Refill()
        {
            if (_nextByte > _endMinus8)
            {
                BoundsCheckedRefill();
            }
            else
            {
                // It's safe to load 64 bits; insert valid (possibly nonzero) bits above
                // _bitsInBuf. The shift requires _bitsInBuf < 64.
                ulong loaded = ByteOrder.LoadLE64(_data.AsSpan(_nextByte));
                _buf |= loaded << (int)_bitsInBuf;

                // Advance by bytes fully absorbed into the buffer.
                _nextByte += (int)((63u - _bitsInBuf) >> 3);

                // We absorbed a multiple of 8 bits, so the lower 3 bits of _bitsInBuf
                // must remain unchanged, otherwise the next refill's shifted bits will
                // not align with _buf. Set the three upper bits so the result >= 56.
                _bitsInBuf |= 56u;
            }
        }

        /// <summary>
        /// Returns the <paramref name="n"/> bits that would be returned by <see cref="ReadBits"/> without advancing.
        /// It is legal to peek at more bits than present in the bitstream (required by Huffman), and those bits will be zero.
        /// </summary>
        /// <param name="n">Number of bits to peek (at most <see cref="KMaxBitsPerCall"/>).</param>
        /// <returns>Peeked bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong PeekFixedBits(int n)
        {
            return _buf & ((1UL << n) - 1UL);
        }

        /// <summary>
        /// Returns the <paramref name="nbits"/> bits that would be returned by <see cref="ReadBits"/> without advancing.
        /// </summary>
        /// <param name="nbits">Number of bits to peek (at most <see cref="KMaxBitsPerCall"/>).</param>
        /// <returns>Peeked bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong PeekBits(nuint nbits)
        {
            ulong mask = (1UL << (int)nbits) - 1UL;

            return _buf & mask;
        }

        /// <summary>
        /// Removes <paramref name="numBits"/> bits from the buffer.
        /// </summary>
        /// <param name="numBits">Number of bits to consume.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Consume(nuint numBits)
        {
            _bitsInBuf -= numBits;
            _buf      >>= (int)numBits;
        }

        /// <summary>
        /// Refills the buffer, peeks <paramref name="nbits"/> bits, and consumes them.
        /// </summary>
        /// <param name="nbits">Number of bits to read (at most <see cref="KMaxBitsPerCall"/>).</param>
        /// <returns>Read bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadBits(nuint nbits)
        {
            Refill();
            ulong bits = PeekBits(nbits);
            Consume(nbits);

            return bits;
        }

        /// <summary>
        /// Refills the buffer, peeks <paramref name="n"/> fixed bits, and consumes them.
        /// </summary>
        /// <param name="n">Number of bits to read (at most <see cref="KMaxBitsPerCall"/>).</param>
        /// <returns>Read bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixedBits(int n)
        {
            Refill();
            ulong bits = PeekFixedBits(n);
            Consume((nuint)n);

            return bits;
        }

        /// <summary>
        /// Equivalent to calling <c>ReadFixedBits(1)</c> <paramref name="skip"/> times, but much faster.
        /// </summary>
        /// <param name="skip">Number of bits to skip.</param>
        public void SkipBits(nuint skip)
        {
            // Buffer is large enough - don't zero _buf below.
            if (skip <= _bitsInBuf)
            {
                Consume(skip);

                return;
            }

            // First deduct what we can satisfy from the buffer
            skip       -= _bitsInBuf;
            _bitsInBuf  = 0;
            _buf        = 0;

            // Skip whole bytes
            nuint wholeBytes    = skip / Common.BitsPerByte;
            skip               %= Common.BitsPerByte;
            int remainingBytes  = _endMinus8 + 8 - _nextByte;

            if (remainingBytes < 0 || wholeBytes > (nuint)remainingBytes)
            {
                _nextByte = _endMinus8 + 8;
                skip     += Common.BitsPerByte;
            }
            else
            {
                _nextByte += (int)wholeBytes;
            }

            Refill();
            Consume(skip);
        }

        /// <summary>
        /// Returns the total number of bits consumed so far.
        /// </summary>
        /// <returns>Total bits consumed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint TotalBitsConsumed()
        {
            nuint bytesRead = (nuint)(_nextByte - _startOffset);

            return ((bytesRead + (nuint)_overreadBytes) * Common.BitsPerByte) - _bitsInBuf;
        }

        /// <summary>
        /// Advances to the next byte boundary, verifying that any skipped padding bits are zero.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if padding bits are zero; otherwise failure.</returns>
        public Status JumpToByteBoundary()
        {
            nuint remainder = TotalBitsConsumed() % Common.BitsPerByte;

            if (remainder == 0)
            {
                return true;
            }

            if (ReadBits(Common.BitsPerByte - remainder) != 0)
            {
                return Status.Failure("Non-zero padding bits");
            }

            return true;
        }

        /// <summary>
        /// Returns the underlying input byte span.
        /// </summary>
        /// <returns>Underlying byte span.</returns>
        public ReadOnlySpan<byte> GetSpan()
        {
            return _data.AsSpan(_startOffset, _length);
        }

        /// <summary>
        /// Returns the total number of bytes in the underlying input buffer.
        /// </summary>
        /// <returns>Total input bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint TotalBytes()
        {
            return (nuint)_length;
        }

        /// <summary>
        /// Forces the <see cref="BitReader"/> into an out-of-bounds state.
        /// </summary>
        public void MarkUnhealthy()
        {
            _nextByte       = _endMinus8 + 8;
            _overreadBytes += 8;
        }

        /// <summary>
        /// Returns whether all bits read so far have been within the input bounds.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if within bounds; otherwise failure.</returns>
        public Status AllReadsWithinBounds()
        {
            _checkedOutOfBoundsBits = TotalBitsConsumed();

            if (TotalBitsConsumed() > TotalBytes() * Common.BitsPerByte)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Closes the bit reader and returns whether all previous reads were within bounds.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Close()
        {
            _closeCalled = true;

            if (!_isInitialized)
            {
                return true;
            }

            if (TotalBitsConsumed() > _checkedOutOfBoundsBits &&
                TotalBitsConsumed() > TotalBytes() * Common.BitsPerByte)
            {
                return Status.Failure("Read more bits than available in the bit_reader");
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_closeCalled)
            {
                Close();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void BoundsCheckedRefill()
        {
            int end = _endMinus8 + 8;

            // Read whole bytes until we have [56, 64) bits (same as LoadLE64)
            for (; _bitsInBuf < 64 - Common.BitsPerByte; _bitsInBuf += Common.BitsPerByte)
            {
                if (_nextByte >= end)
                {
                    break;
                }

                _buf |= (ulong)_data[_nextByte++] << (int)_bitsInBuf;
            }

            // Add extra bytes as 0 at the end of the stream in the bit_buffer_. If
            // these bits are read, Close() will return a failure.
            nuint extraBytes = (63u - _bitsInBuf) / Common.BitsPerByte;
            _overreadBytes  += extraBytes;
            _bitsInBuf      += extraBytes * Common.BitsPerByte;
        }
    }

    /// <summary>
    /// Closes a <see cref="BitReader"/> when disposed and propagates any close failure to a status callback or reference.
    /// Ported from <c>jxl::BitReaderScopedCloser</c> in <c>lib/jxl/dec_bit_reader.h</c>.
    /// </summary>
    public struct BitReaderScopedCloser : IDisposable
    {
        private BitReader? _reader;
        private Status _closeStatus;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitReaderScopedCloser"/> struct.
        /// </summary>
        /// <param name="reader">The <see cref="BitReader"/> to close on disposal.</param>
        public BitReaderScopedCloser(BitReader reader)
        {
            _reader      = reader;
            _closeStatus = Status.OkStatus;
        }

        /// <summary>
        /// Gets the status returned by <see cref="BitReader.Close"/>.
        /// </summary>
        public readonly Status CloseStatus => _closeStatus;

        /// <summary>
        /// Closes the reader and updates <paramref name="status"/> if closing failed.
        /// </summary>
        /// <param name="status">Status variable to update on failure.</param>
        public void Close(ref Status status)
        {
            if (_reader != null)
            {
                Status closeRet = _reader.Close();
                _closeStatus = closeRet;

                if (!closeRet)
                {
                    status = closeRet;
                }

                _reader = null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_reader != null)
            {
                _closeStatus = _reader.Close();
                _reader      = null;
            }
        }
    }
}
