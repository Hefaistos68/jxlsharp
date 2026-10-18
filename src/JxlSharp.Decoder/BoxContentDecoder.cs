// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers;
using System.IO.Compression;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Return status codes for JPEG XL streaming decoding operations.
    /// Ported from <c>JxlDecoderStatus</c> in <c>include/jxl/decode.h</c>.
    /// </summary>
    public enum JxlDecoderStatus
    {
        /// <summary>Function call finished successfully, or decoding is finished.</summary>
        Success = 0,

        /// <summary>An error occurred, for example invalid input or out of memory.</summary>
        Error = 1,

        /// <summary>The decoder needs more input bytes to continue.</summary>
        NeedMoreInput = 2,

        /// <summary>The decoder requests setting a preview output buffer.</summary>
        NeedPreviewOutBuffer = 3,

        /// <summary>The decoder requests an output buffer to store the full resolution image.</summary>
        NeedImageOutBuffer = 5,

        /// <summary>The JPEG reconstruction buffer is too small.</summary>
        JpegNeedMoreOutput = 6,

        /// <summary>The box contents output buffer is too small.</summary>
        BoxNeedMoreOutput = 7,

        /// <summary>Basic information such as image dimensions and extra channels is available.</summary>
        BasicInfo = 0x40,

        /// <summary>Color encoding or ICC profile from the codestream header is available.</summary>
        ColorEncoding = 0x100,

        /// <summary>Preview image frame has been decoded.</summary>
        PreviewImage = 0x200,

        /// <summary>Beginning of a frame.</summary>
        Frame = 0x400,

        /// <summary>Full frame has been decoded.</summary>
        FullImage = 0x1000,

        /// <summary>JPEG reconstruction data has been decoded.</summary>
        JpegReconstruction = 0x2000,

        /// <summary>The header of a container (BMFF) box has been decoded.</summary>
        Box = 0x4000,

        /// <summary>A progressive step in decoding the frame has been reached.</summary>
        FrameProgression = 0x8000,

        /// <summary>The box being decoded is now complete.</summary>
        BoxComplete = 0x10000,
    }

    /// <summary>
    /// Outputs the contents of a BMFF container box in a streaming fashion, either directly
    /// or optionally decompressing with Brotli in the case of a <c>brob</c> box.
    /// Ported from <c>jxl::JxlBoxContentDecoder</c> in <c>lib/jxl/box_content_decoder.h</c> and <c>lib/jxl/box_content_decoder.cc</c>.
    /// </summary>
    public sealed class JxlBoxContentDecoder : IDisposable
    {
        private BrotliDecoder _brotliDec;
        private bool _hasBrotliDec;
        private bool _headerDone;
        private bool _brobDecode;
        private bool _boxUntilEof;
        private nuint _remaining;
        private nuint _pos;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlBoxContentDecoder"/> class.
        /// </summary>
        public JxlBoxContentDecoder()
        {
        }

        /// <summary>
        /// Starts decoding a new box payload.
        /// </summary>
        /// <param name="brobDecode">Whether to skip the 4-byte inner box type and Brotli-decompress the payload.</param>
        /// <param name="boxUntilEof">Whether the box extends until the end of the file.</param>
        /// <param name="contentsSize">Size of the box payload in bytes (ignored if <paramref name="boxUntilEof"/> is <c>true</c>).</param>
        public void StartBox(bool brobDecode, bool boxUntilEof, nuint contentsSize)
        {
            ResetBrotli();
            _headerDone  = false;
            _brobDecode  = brobDecode;
            _boxUntilEof = boxUntilEof;
            _remaining   = boxUntilEof ? 0 : contentsSize;
            _pos         = 0;
        }

        /// <summary>
        /// Outputs decoded bytes from the box, decompressing with Brotli if configured.
        /// </summary>
        /// <param name="nextIn">Available input bytes starting at <paramref name="boxPos"/> in the box content.</param>
        /// <param name="boxPos">Position in the box content corresponding to the start of <paramref name="nextIn"/>.</param>
        /// <param name="output">Output buffer slice corresponding to remaining available output space.</param>
        /// <param name="bytesWritten">Number of bytes written into <paramref name="output"/> during this call.</param>
        /// <returns><see cref="JxlDecoderStatus"/> indicating completion, need for more input/output, or error.</returns>
        public JxlDecoderStatus Process(
            ReadOnlySpan<byte> nextIn,
            nuint boxPos,
            Span<byte> output,
            out nuint bytesWritten)
        {
            bytesWritten = 0;

            if (boxPos > _pos)
            {
                return JxlDecoderStatus.Error;
            }

            nuint offset = _pos - boxPos;

            if (offset > (nuint)nextIn.Length)
            {
                return JxlDecoderStatus.Error;
            }

            ReadOnlySpan<byte> availIn = nextIn.Slice((int)offset);

            if (_brobDecode)
            {
                return ProcessBrob(availIn, output, out bytesWritten);
            }

            return ProcessRaw(availIn, output, out bytesWritten);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            ResetBrotli();
        }

        private void ResetBrotli()
        {
            if (_hasBrotliDec)
            {
                _brotliDec.Dispose();
                _brotliDec    = default;
                _hasBrotliDec = false;
            }
        }

        private JxlDecoderStatus ProcessBrob(
            ReadOnlySpan<byte> availIn,
            Span<byte> output,
            out nuint bytesWritten)
        {
            bytesWritten = 0;

            if (!_headerDone)
            {
                if (availIn.Length < 4)
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                if (!_boxUntilEof)
                {
                    if (_remaining < 4)
                    {
                        return JxlDecoderStatus.Error;
                    }

                    _remaining -= 4;
                }

                availIn     = availIn.Slice(4);
                _pos       += 4;
                _headerDone = true;
            }

            if (!_hasBrotliDec)
            {
                _brotliDec    = new BrotliDecoder();
                _hasBrotliDec = true;
            }

            nuint availInBrotli = (nuint)availIn.Length;

            if (!_boxUntilEof && _remaining < availInBrotli)
            {
                availInBrotli = _remaining;
            }

            OperationStatus res = _brotliDec.Decompress(
                availIn.Slice(0, (int)availInBrotli),
                output,
                out int consumed,
                out int produced);

            bytesWritten = (nuint)produced;

            if (res == OperationStatus.InvalidData)
            {
                return JxlDecoderStatus.Error;
            }

            _pos += (nuint)consumed;

            if (!_boxUntilEof)
            {
                _remaining -= (nuint)consumed;

                if (res == OperationStatus.NeedMoreData && _remaining == 0)
                {
                    return JxlDecoderStatus.Error;
                }
            }

            return MapBrotliStatus(res);
        }

        private JxlDecoderStatus MapBrotliStatus(OperationStatus res)
        {
            if (res == OperationStatus.NeedMoreData)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (res == OperationStatus.DestinationTooSmall)
            {
                return JxlDecoderStatus.BoxNeedMoreOutput;
            }

            if (res == OperationStatus.Done)
            {
                if (!_boxUntilEof && _remaining != 0)
                {
                    return JxlDecoderStatus.Error;
                }

                return JxlDecoderStatus.BoxComplete;
            }

            return JxlDecoderStatus.Error;
        }

        private JxlDecoderStatus ProcessRaw(
            ReadOnlySpan<byte> availIn,
            Span<byte> output,
            out nuint bytesWritten)
        {
            nuint canRead = (nuint)availIn.Length;

            if (!_boxUntilEof && _remaining < canRead)
            {
                canRead = _remaining;
            }

            nuint toWrite = Math.Min(canRead, (nuint)output.Length);
            availIn.Slice(0, (int)toWrite).CopyTo(output);
            bytesWritten = toWrite;

            if (!_boxUntilEof)
            {
                _remaining -= toWrite;
            }

            _pos += toWrite;

            if (toWrite < canRead)
            {
                return JxlDecoderStatus.BoxNeedMoreOutput;
            }

            if (!_boxUntilEof && _remaining > 0)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            return JxlDecoderStatus.BoxComplete;
        }
    }
}
