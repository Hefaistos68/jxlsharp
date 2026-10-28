// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// BitWriter class: unbuffered writes using unaligned 64-bit stores.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Writes bits into bytes in increasing addresses, and within a byte least-significant-bit first.
    /// Ported from <c>jxl::BitWriter</c> in <c>lib/jxl/enc_bit_writer.h</c> and <c>enc_bit_writer.cc</c>.
    /// </summary>
    public sealed class BitWriter
    {
        /// <summary>
        /// Upper bound on <c>nBits</c> in each call to <see cref="Write"/>.
        /// </summary>
        public const int KMaxBitsPerCall = 56;

        private nuint _bitsWritten;
        private PaddedBytes _storage;
        private Allotment? _currentAllotment;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitWriter"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager (uses default if <c>null</c>).</param>
        public BitWriter(JxlMemoryManager? memoryManager = null)
        {
            _bitsWritten = 0;
            _storage     = new PaddedBytes(memoryManager ?? JxlMemoryManager.Default);
        }

        /// <summary>
        /// Gets the total number of bits written so far.
        /// </summary>
        public nuint BitsWritten { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _bitsWritten; }

        /// <summary>
        /// Gets the memory manager associated with this writer.
        /// </summary>
        public JxlMemoryManager MemoryManager => _storage.MemoryManager ?? JxlMemoryManager.Default;

        /// <summary>
        /// Returns a read-only span over the written bytes (must be byte-aligned).
        /// </summary>
        /// <returns>Written bytes span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetSpan()
        {
            nuint byteCount = Common.DivCeil(_bitsWritten, Common.BitsPerByte);

            return _storage.Span.Slice(0, (int)byteCount);
        }

        /// <summary>
        /// Extracts the underlying <see cref="PaddedBytes"/> buffer (must be byte-aligned).
        /// </summary>
        /// <returns>Underlying <see cref="PaddedBytes"/> resized to the written byte count.</returns>
        public PaddedBytes TakeBytes()
        {
            nuint byteCount = Common.DivCeil(_bitsWritten, Common.BitsPerByte);
            _storage.Resize(byteCount);
            PaddedBytes result = _storage;
            _storage           = new PaddedBytes(result.MemoryManager);
            _bitsWritten       = 0;

            return result;
        }

        /// <summary>
        /// Shrinks the internal storage to fit.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Shrink()
        {
            return _storage.ShrinkToFit();
        }

        /// <summary>
        /// Appends byte-aligned data from <paramref name="span"/>. Must be byte-aligned before calling.
        /// </summary>
        /// <param name="span">Bytes to append.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status AppendByteAligned(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty)
            {
                return true;
            }

            nuint spanLen = (nuint)span.Length;

            if (!_storage.Resize(_storage.Size + spanLen + 1u))
            {
                return Status.Failure("Failed to resize BitWriter storage");
            }

            if (_bitsWritten % Common.BitsPerByte != 0)
            {
                return Status.Failure("BitWriter must be byte-aligned before AppendByteAligned");
            }

            nuint pos = _bitsWritten / Common.BitsPerByte;
            span.CopyTo(_storage.Span.Slice((int)pos, span.Length));
            pos += spanLen;
            _storage[pos] = 0; // for next Write
            _bitsWritten += spanLen * Common.BitsPerByte;

            return true;
        }

        /// <summary>
        /// Appends byte-aligned data from multiple <see cref="BitWriter"/> instances.
        /// </summary>
        /// <param name="others">List of byte-aligned <see cref="BitWriter"/> instances.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status AppendByteAligned(IReadOnlyList<BitWriter> others)
        {
            nuint otherBytes = 0;

            for (int i = 0; i < others.Count; ++i)
            {
                BitWriter writer = others[i];

                if (writer.BitsWritten % Common.BitsPerByte != 0)
                {
                    return Status.Failure("Source BitWriter not byte-aligned");
                }

                otherBytes += Common.DivCeil(writer.BitsWritten, Common.BitsPerByte);
            }

            if (otherBytes == 0)
            {
                return true;
            }

            if (!_storage.Resize(_storage.Size + otherBytes + 1u))
            {
                return Status.Failure("Failed to resize BitWriter storage");
            }

            if (_bitsWritten % Common.BitsPerByte != 0)
            {
                return Status.Failure("Destination BitWriter not byte-aligned");
            }

            nuint pos = Common.DivCeil(_bitsWritten, Common.BitsPerByte);

            for (int i = 0; i < others.Count; ++i)
            {
                ReadOnlySpan<byte> span = others[i].GetSpan();
                span.CopyTo(_storage.Span.Slice((int)pos, span.Length));
                pos += (nuint)span.Length;
            }

            _storage[pos] = 0; // for next Write
            _bitsWritten += otherBytes * Common.BitsPerByte;

            return true;
        }

        /// <summary>
        /// Appends unaligned bits from <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Source <see cref="BitWriter"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status AppendUnaligned(BitWriter other)
        {
            return WithMaxBits(
                other.BitsWritten,
                LayerType.Header,
                null,
                () =>
                {
                    nuint fullBytes     = other.BitsWritten / Common.BitsPerByte;
                    nuint remainingBits = other.BitsWritten % Common.BitsPerByte;

                    for (nuint i = 0; i < fullBytes; ++i)
                    {
                        Write(8, other._storage[i]);
                    }

                    if (remainingBits > 0)
                    {
                        ulong mask = (1UL << (int)remainingBits) - 1UL;
                        Write(remainingBits, other._storage[fullBytes] & mask);
                    }

                    return true;
                });
        }

        /// <summary>
        /// Writes <paramref name="nBits"/> (at most 56) least-significant bits of <paramref name="bits"/>.
        /// </summary>
        /// <param name="nBits">Number of bits to write (0..56).</param>
        /// <param name="bits">Bits to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(nuint nBits, ulong bits)
        {
            if (nBits == 0)
            {
                return;
            }

            nuint bytesWritten   = _bitsWritten / Common.BitsPerByte;
            int bitsInFirstByte  = (int)(_bitsWritten % Common.BitsPerByte);
            bits               <<= bitsInFirstByte;
            nuint minRequired    = bytesWritten + 8u;

            if (_storage.Size < minRequired)
            {
                _storage.Resize(minRequired);
            }

            Span<byte> dst = _storage.Span.Slice((int)bytesWritten, 8);
            ulong v        = dst[0];
            v             |= bits;
            ByteOrder.StoreLE64(v, dst);
            _bitsWritten  += nBits;
        }

        /// <summary>
        /// Pads the bit stream with zero bits up to the next byte boundary.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ZeroPadToByte()
        {
            nuint remainderBits = Common.RoundUpBitsToByteMultiple(_bitsWritten) - _bitsWritten;

            if (remainderBits == 0)
            {
                return;
            }

            Write(remainderBits, 0);
        }

        /// <summary>
        /// Reserves storage for up to <paramref name="maxBits"/> bits, executes <paramref name="function"/>,
        /// reclaims unused whole bytes, and charges the used bits to <paramref name="auxOut"/>.
        /// </summary>
        /// <param name="maxBits">Maximum bits that <paramref name="function"/> may write.</param>
        /// <param name="layer">Codestream layer to charge.</param>
        /// <param name="auxOut">Optional statistics collector.</param>
        /// <param name="function">Write callback.</param>
        /// <param name="finishedHistogram">Whether the bits written represent a histogram.</param>
        /// <returns>Status returned by <paramref name="function"/>.</returns>
        public Status WithMaxBits(
            nuint maxBits,
            LayerType layer,
            AuxOut? auxOut,
            Func<Status> function,
            bool finishedHistogram = false)
        {
            Allotment allotment = new Allotment(maxBits);

            if (!allotment.Init(this))
            {
                return Status.Failure("BitWriter allotment Init failed");
            }

            Status result = function();

            if (result && finishedHistogram)
            {
                if (!allotment.FinishedHistogram(this))
                {
                    return Status.Failure("FinishedHistogram failed");
                }
            }

            if (!allotment.ReclaimAndCharge(this, layer, auxOut))
            {
                return Status.Failure("ReclaimAndCharge failed");
            }

            return result;
        }

        private sealed class Allotment
        {
            private readonly nuint _maxBits;
            private nuint _prevBitsWritten;
            private nuint _histogramBits;
            private bool _called;
            private Allotment? _parent;

            public Allotment(nuint maxBits)
            {
                _maxBits = maxBits;
            }

            public Status Init(BitWriter writer)
            {
                _prevBitsWritten = writer.BitsWritten;
                nuint prevBytes  = writer._storage.Size;
                nuint nextBytes  = Common.DivCeil(_maxBits, Common.BitsPerByte) + 8u;

                if (!writer._storage.Resize(prevBytes + nextBytes))
                {
                    _called = true;

                    return false;
                }

                _parent                  = writer._currentAllotment;
                writer._currentAllotment = this;

                return true;
            }

            public Status FinishedHistogram(BitWriter? writer)
            {
                if (writer == null)
                {
                    return true;
                }

                if (_called || _histogramBits != 0 || writer.BitsWritten < _prevBitsWritten)
                {
                    return Status.Failure("Invalid FinishedHistogram state");
                }

                _histogramBits = writer.BitsWritten - _prevBitsWritten;

                return true;
            }

            public Status ReclaimAndCharge(BitWriter? writer, LayerType layer, AuxOut? auxOut)
            {
                if (!PrivateReclaim(writer, out nuint usedBits, out _))
                {
                    return false;
                }

                if (auxOut != null)
                {
                    LayerTotals totals    = auxOut.Layer(layer);
                    totals.TotalBits     += usedBits;
                    totals.HistogramBits += _histogramBits;
                }

                return true;
            }

            private Status PrivateReclaim(BitWriter? writer, out nuint usedBits, out nuint unusedBits)
            {
                usedBits   = 0;
                unusedBits = 0;
                _called    = true;

                if (writer == null)
                {
                    return true;
                }

                usedBits       = writer.BitsWritten - _prevBitsWritten;
                unusedBits     = _maxBits >= usedBits ? _maxBits - usedBits : 0;
                nuint minBytes = Common.DivCeil(writer.BitsWritten, Common.BitsPerByte) + 8u;

                if (writer._storage.Size > minBytes)
                {
                    if (!writer._storage.Resize(minBytes))
                    {
                        return false;
                    }
                }

                writer._currentAllotment = _parent;
                Allotment? parent        = _parent;

                while (parent != null)
                {
                    parent._prevBitsWritten += usedBits;
                    parent                   = parent._parent;
                }

                return true;
            }
        }
    }
}
