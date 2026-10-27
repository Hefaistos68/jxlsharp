// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Result of <see cref="JxlDecoder.CheckSignature(ReadOnlySpan{byte})"/>.
    /// Ported from <c>JxlSignature</c> in <c>lib/include/jxl/decode.h</c>.
    /// </summary>
    public enum JxlSignature
    {
        /// <summary>Not enough bytes were passed to determine if a valid signature was found.</summary>
        NotEnoughBytes = 0,

        /// <summary>No valid JPEG XL header was found.</summary>
        Invalid = 1,

        /// <summary>A valid JPEG XL codestream signature was found.</summary>
        Codestream = 2,

        /// <summary>A valid JPEG XL container signature was found.</summary>
        Container = 3,
    }

    /// <summary>
    /// Defines which color profile to query or configure in <see cref="JxlDecoder"/>.
    /// Ported from <c>JxlColorProfileTarget</c> in <c>lib/include/jxl/decode.h</c>.
    /// </summary>
    public enum JxlColorProfileTarget
    {
        /// <summary>The color profile of the original image from the metadata.</summary>
        Original = 0,

        /// <summary>The color profile of the pixel data the decoder outputs.</summary>
        Data = 1,
    }

    /// <summary>
    /// Top-level streaming JPEG XL decoder supporting both raw codestreams and ISO BMFF container boxes.
    /// Ported from <c>JxlDecoderStruct</c> in <c>lib/include/jxl/decode.h</c> and <c>lib/jxl/decode.cc</c>.
    /// </summary>
    public sealed class JxlDecoder : IDisposable
    {
        /// <summary>Library version constant (0.13.0 -> <c>0x000D00</c>).</summary>
        public const uint NumericVersion = 0x000D00u;

        private const int AllSubscribedEventsMask =
            (int)(JxlDecoderStatus.BasicInfo |
                  JxlDecoderStatus.ColorEncoding |
                  JxlDecoderStatus.PreviewImage |
                  JxlDecoderStatus.Frame |
                  JxlDecoderStatus.FullImage |
                  JxlDecoderStatus.JpegReconstruction |
                  JxlDecoderStatus.Box |
                  JxlDecoderStatus.FrameProgression |
                  JxlDecoderStatus.BoxComplete);

        private enum DecoderStage
        {
            Inited,
            Started,
            CodestreamFinished,
            Error,
        }

        private enum FrameStage
        {
            Header,
            TOC,
            Full,
        }

        private enum BoxLoopResult
        {
            Continue,
            ReturnStatus,
        }

        private static readonly byte[] ContainerSignatureBytes =
        {
            0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A,
        };

        private static readonly byte[] FtypHeaderPrefix =
        {
            0x00, 0x00, 0x00, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'j', (byte)'x', (byte)'l', (byte)' ', 0x00, 0x00, 0x00, 0x00,
            (byte)'j', (byte)'x', (byte)'l', (byte)' ',
        };

        private readonly JxlMemoryManager _memoryManager;
        private ThreadPool? _threadPool;

        private DecoderStage _stage;
        private bool _keepOrientation;
        private bool _unpremulAlpha;
        private bool _renderSpotcolors = true;
        private bool _coalescing       = true;
        private float _desiredIntensityTarget;
        private JxlProgressiveDetail _progDetail = JxlProgressiveDetail.Frames;
        private JxlProgressiveDetail _frameProgDetail;

        private int _eventsSubscribed;
        private int _eventsProcessed;
        private bool _gotBasicInfo;
        private bool _gotAllHeaders;
        private bool _gotCodestreamSignature;
        private bool _gotSignature;
        private bool _isLastTotal;
        private nuint _basicInfoSizeHint;

        private bool _haveContainer;
        private bool _seenJxlc;
        private bool _seenJxlp;
        private bool _seenJbrd;
        private bool _reconOutputJpeg;
        private uint _lastCodestreamIndex;

        private JxlToJpegDecoder _jpegDecoder = new JxlToJpegDecoder();
        private readonly JxlBoxContentDecoder _metadataDecoder = new JxlBoxContentDecoder();
        private byte[] _exifMetadata = Array.Empty<byte>();
        private byte[] _xmpMetadata  = Array.Empty<byte>();
        private int _storeExif;
        private int _storeXmp;
        private nuint _reconOutBufferPos;
        private nuint _reconExifSize;
        private nuint _reconXmpSize;
        private bool _reconMetadataSet;
        private ICCReader _iccReader;
        private bool _decompressBoxes;
        private readonly JxlBoxContentDecoder _boxContentDecoder = new JxlBoxContentDecoder();
        private bool _boxOutBufferSet;
        private bool _boxOutBufferSetCurrentBox;
        private bool _boxEvent;
        private Memory<byte> _boxOutBuffer;
        private bool _hasBoxOutBuffer;
        private nuint _boxOutBufferSize;
        private nuint _boxOutBufferPos;
        private nuint _headerSize;
        private nuint _boxSize;
        private bool _boxUnbounded;
        private nuint _boxContentsBegin;
        private nuint _boxContentsEnd;
        private nuint _boxContentsSize;
        private JxlBoxType _boxType;
        private JxlBoxType _rawBoxType;
        private bool _inBrob;

        private CodecMetadata _metadata = new CodecMetadata();
        private ImageBundle? _ib;
        private PassesDecoderState? _passesState;
        private FrameDecoder? _frameDecoder;

        private Memory<byte> _previewOutBuffer;
        private bool _hasPreviewOutBuffer;
        private nuint _previewOutBufferSize;
        private Memory<byte> _imageOutBuffer;
        private bool _hasImageOutBuffer;
        private nuint _imageOutBufferSize;
        private JxlPixelFormat _imageOutFormat;
        private JxlBitDepth _imageOutBitDepth;
        private PixelCallback _pixelCallback;
        private JxlImageOutCallback? _simpleImageOutCallback;
        private object? _simpleImageOutOpaque;

        private readonly List<Memory<byte>> _extraChannelOutBuffers = new List<Memory<byte>>();
        private readonly List<bool> _extraChannelOutHasBuffers = new List<bool>();
        private readonly List<nuint> _extraChannelOutSizes = new List<nuint>();
        private readonly List<JxlPixelFormat> _extraChannelOutFormats = new List<JxlPixelFormat>();

        private nuint _codestreamPos;
        private nuint _frameStart;
        private FrameStage _frameStage;
        private bool _isFramePreview;
        private nuint _skipFrames;
        private bool _skippingFrame;
        private int _LastDecodedPass = -1;

        private byte[] _codestreamCopy = Array.Empty<byte>();
        private int _codestreamCopyLen;
        private nuint _codestreamBitsAhead;

        private ReadOnlyMemory<byte> _nextIn;
        private nuint _availIn;
        private bool _hasInput;
        private bool _inputClosed;
        private bool _inCodestreamBox;
        private nuint _codestreamBoxRem;
        private nuint _filePos;
        private nuint _bleed;
        private bool _noMoreCodestreamInput;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlDecoder"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public JxlDecoder(JxlMemoryManager? memoryManager = null)
        {
            _memoryManager = memoryManager ?? JxlMemoryManager.Default;
            _iccReader     = new ICCReader(_memoryManager);
            Reset();
        }

        /// <summary>
        /// Returns the numeric library version (<c>JPEGXL_NUMERIC_VERSION</c>).
        /// Ported from <c>JxlDecoderVersion</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Library version integer.</returns>
        public static uint Version() => NumericVersion;

        /// <summary>
        /// Checks whether <paramref name="buf"/> begins with a valid JPEG XL codestream or container signature.
        /// Ported from <c>JxlSignatureCheck</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="buf">Input byte array.</param>
        /// <param name="len">Number of available bytes.</param>
        /// <returns>Detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(byte[]? buf, nuint len)
        {
            if (len == 0)
            {
                return JxlSignature.NotEnoughBytes;
            }

            if (buf == null)
            {
                return JxlSignature.Invalid;
            }

            int clampedLen = (int)Math.Min(Math.Min(len, (nuint)buf.Length), 12u);

            return CheckSignature(buf.AsSpan(0, clampedLen));
        }

        /// <summary>
        /// Checks whether <paramref name="buf"/> begins with a valid JPEG XL codestream or container signature.
        /// </summary>
        /// <param name="buf">Input byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="buf"/>.</param>
        /// <param name="count">Number of available bytes.</param>
        /// <returns>Detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(byte[]? buf, int offset, int count)
        {
            if (count == 0)
            {
                return JxlSignature.NotEnoughBytes;
            }

            if (buf == null || offset < 0 || count < 0 || offset + count > buf.Length)
            {
                return JxlSignature.Invalid;
            }

            return CheckSignature(buf.AsSpan(offset, count));
        }

        /// <summary>
        /// Checks whether <paramref name="buf"/> begins with a valid JPEG XL codestream or container signature.
        /// </summary>
        /// <param name="buf">Input byte array.</param>
        /// <returns>Detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(byte[]? buf)
        {
            if (buf == null)
            {
                return JxlSignature.Invalid;
            }

            return CheckSignature(buf.AsSpan());
        }

        /// <summary>
        /// Checks whether <paramref name="buf"/> begins with a valid JPEG XL codestream or container signature.
        /// </summary>
        /// <param name="buf">Input read-only memory buffer.</param>
        /// <returns>Detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(ReadOnlyMemory<byte> buf) =>
            CheckSignature(buf.Span);

        /// <summary>
        /// Checks whether <paramref name="buf"/> begins with a valid JPEG XL codestream or container signature.
        /// Ported from <c>JxlSignatureCheck</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="buf">Input byte span.</param>
        /// <returns>Detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(ReadOnlySpan<byte> buf)
        {
            if (buf.IsEmpty)
            {
                return JxlSignature.NotEnoughBytes;
            }

            // JPEG XL codestream: 0xff 0x0a
            if (buf[0] == 0xFF)
            {
                if (buf.Length < 2)
                {
                    return JxlSignature.NotEnoughBytes;
                }

                return buf[1] == Headers.KCodestreamMarker
                    ? JxlSignature.Codestream
                    : JxlSignature.Invalid;
            }

            // JPEG XL container
            ReadOnlySpan<byte> magic = ContainerSignatureBytes;
            int count                = Math.Min(buf.Length, magic.Length);

            for (int i = 0; i < count; i++)
            {
                if (buf[i] != magic[i])
                {
                    return JxlSignature.Invalid;
                }
            }

            return buf.Length < magic.Length
                ? JxlSignature.NotEnoughBytes
                : JxlSignature.Container;
        }

        /// <summary>
        /// Creates a new <see cref="JxlDecoder"/> instance.
        /// Ported from <c>JxlDecoderCreate</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <returns>New <see cref="JxlDecoder"/>.</returns>
        public static JxlDecoder Create(JxlMemoryManager? memoryManager = null)
        {
            return new JxlDecoder(memoryManager);
        }

        /// <summary>
        /// Resets <paramref name="dec"/> to its initial newly-created state.
        /// Ported from <c>JxlDecoderReset</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="dec">Decoder instance to reset.</param>
        public static void JxlDecoderReset(JxlDecoder dec) => dec.Reset();

        /// <summary>
        /// Destroys <paramref name="dec"/> and releases its resources.
        /// Ported from <c>JxlDecoderDestroy</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="dec">Decoder instance to dispose.</param>
        public static void Destroy(JxlDecoder? dec) => dec?.Dispose();

        /// <summary>
        /// Resets the decoder completely, releasing internal buffers and thread pool settings.
        /// Ported from <c>JxlDecoderReset</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        public void Reset()
        {
            _threadPool             = null;
            _keepOrientation        = false;
            _unpremulAlpha          = false;
            _renderSpotcolors       = true;
            _coalescing             = true;
            _desiredIntensityTarget = 0.0f;
            _progDetail             = JxlProgressiveDetail.Frames;
            _eventsSubscribed       = 0;
            _decompressBoxes        = false;

            Rewind();
        }

        /// <summary>
        /// Rewinds the decoder to the beginning of the file while preserving user settings and subscriptions.
        /// Ported from <c>JxlDecoderRewind</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        public void Rewind()
        {
            _stage                    = DecoderStage.Inited;
            _eventsProcessed          = 0;
            _gotBasicInfo             = false;
            _gotAllHeaders            = false;
            _gotCodestreamSignature   = false;
            _gotSignature             = false;
            _isLastTotal              = false;
            _basicInfoSizeHint        = InitialBasicInfoSizeHint();
            _haveContainer            = false;
            _seenJxlc                 = false;
            _seenJxlp                 = false;
            _seenJbrd                 = false;
            _reconOutputJpeg          = false;
            _lastCodestreamIndex      = 0;

            _jpegDecoder               = new JxlToJpegDecoder();
            _exifMetadata              = Array.Empty<byte>();
            _xmpMetadata               = Array.Empty<byte>();
            _storeExif                 = 0;
            _storeXmp                  = 0;
            _reconOutBufferPos         = 0;
            _reconExifSize             = 0;
            _reconXmpSize              = 0;
            _reconMetadataSet          = false;
            _iccReader.Reset();
            _boxOutBufferSet           = false;
            _boxOutBufferSetCurrentBox = false;
            _boxEvent                  = false;
            _boxOutBuffer              = default;
            _hasBoxOutBuffer           = false;
            _boxOutBufferSize          = 0;
            _boxOutBufferPos           = 0;
            _headerSize                = 0;
            _boxSize                   = 0;
            _boxUnbounded              = false;
            _boxContentsBegin          = 0;
            _boxContentsEnd            = 0;
            _boxContentsSize           = 0;
            _boxType                   = default;
            _rawBoxType                = default;
            _inBrob                    = false;

            _metadata                 = new CodecMetadata();
            _frameDecoder?.Dispose();
            _frameDecoder             = null;
            _passesState?.Dispose();
            _passesState              = new PassesDecoderState(_memoryManager);
            _ib?.Dispose();
            _ib                       = null;

            _previewOutBuffer         = default;
            _hasPreviewOutBuffer      = false;
            _previewOutBufferSize     = 0;
            _imageOutBuffer           = default;
            _hasImageOutBuffer        = false;
            _imageOutBufferSize       = 0;
            _imageOutBitDepth         = default;
            _pixelCallback            = default;
            _simpleImageOutCallback   = null;
            _simpleImageOutOpaque     = null;
            _extraChannelOutBuffers.Clear();
            _extraChannelOutHasBuffers.Clear();
            _extraChannelOutSizes.Clear();
            _extraChannelOutFormats.Clear();

            _codestreamPos            = 0;
            _frameStart               = 0;
            _frameStage               = FrameStage.Header;
            _isFramePreview           = false;
            _skipFrames               = 0;
            _skippingFrame            = false;
            _LastDecodedPass          = -1;

            _codestreamCopyLen        = 0;
            _codestreamBitsAhead      = 0;

            _nextIn                   = default;
            _availIn                  = 0;
            _hasInput                 = false;
            _inputClosed              = false;
            _inCodestreamBox          = false;
            _codestreamBoxRem         = 0;
            _filePos                  = 0;
            _bleed                    = 0;
            _noMoreCodestreamInput    = false;
        }

        /// <summary>
        /// Instructs the decoder to skip the next <paramref name="amount"/> visible frames.
        /// Ported from <c>JxlDecoderSkipFrames</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="amount">Number of frames to skip.</param>
        public void SkipFrames(nuint amount)
        {
            _skippingFrame = false;
            _skipFrames   += amount;
        }

        /// <summary>
        /// Skips the current frame being processed.
        /// Ported from <c>JxlDecoderSkipCurrentFrame</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SkipCurrentFrame()
        {
            if (_frameStage != FrameStage.Full || _frameDecoder == null)
            {
                return JxlDecoderStatus.Error;
            }

            ResetFrameBuffersAndFinalizeSkip();

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the parallel runner callback for multithreaded decoding.
        /// Ported from <c>JxlDecoderSetParallelRunner</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="parallelRunner">Parallel runner delegate.</param>
        /// <param name="parallelRunnerOpaque">Opaque state passed to <paramref name="parallelRunner"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetParallelRunner(
            JxlParallelRunner? parallelRunner,
            object? parallelRunnerOpaque = null)
        {
            if (_stage != DecoderStage.Inited)
            {
                return JxlDecoderStatus.Error;
            }

            _threadPool = parallelRunner != null
                ? new ThreadPool(parallelRunner, parallelRunnerOpaque)
                : null;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Returns a hint indicating how many more bytes the decoder expects before <see cref="JxlDecoderStatus.BasicInfo"/> is available.
        /// Ported from <c>JxlDecoderSizeHintBasicInfo</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Number of bytes suggested to feed next, or 0 if basic info is already available.</returns>
        public nuint SizeHintBasicInfo()
        {
            return _gotBasicInfo ? 0u : _basicInfoSizeHint;
        }

        /// <summary>
        /// Subscribes to informative decoder events (<see cref="JxlDecoderStatus"/> bitmask).
        /// Ported from <c>JxlDecoderSubscribeEvents</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="eventsWanted">Bitwise OR of <see cref="JxlDecoderStatus"/> informative events.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SubscribeEvents(int eventsWanted)
        {
            if (_stage != DecoderStage.Inited)
            {
                return JxlDecoderStatus.Error;
            }

            if ((eventsWanted & 63) != 0 || (eventsWanted & ~AllSubscribedEventsMask) != 0)
            {
                return JxlDecoderStatus.Error;
            }

            _eventsSubscribed = eventsWanted;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Subscribes to informative decoder events.
        /// </summary>
        /// <param name="eventsWanted">Bitwise OR of <see cref="JxlDecoderStatus"/> informative events.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SubscribeEvents(JxlDecoderStatus eventsWanted)
        {
            return SubscribeEvents((int)eventsWanted);
        }

        /// <summary>
        /// Configures whether the decoder skips applying EXIF orientation (keeping encoded pixel orientation).
        /// Ported from <c>JxlDecoderSetKeepOrientation</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="skipRotation"><c>true</c> to keep codestream orientation; <c>false</c> to apply orientation.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetKeepOrientation(bool skipRotation)
        {
            if (_stage != DecoderStage.Inited)
            {
                return JxlDecoderStatus.Error;
            }

            _keepOrientation = skipRotation;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures whether associated (premultiplied) alpha channels are unpremultiplied on output.
        /// Ported from <c>JxlDecoderSetUnpremultiplyAlpha</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="unpremulAlpha"><c>true</c> to unpremultiply associated alpha.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetUnpremultiplyAlpha(bool unpremulAlpha)
        {
            _unpremulAlpha = unpremulAlpha;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures whether spot color extra channels are rendered onto the RGB output.
        /// Ported from <c>JxlDecoderSetRenderSpotcolors</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="renderSpotcolors"><c>true</c> to render spot colors.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetRenderSpotcolors(bool renderSpotcolors)
        {
            if (_stage != DecoderStage.Inited)
            {
                return JxlDecoderStatus.Error;
            }

            _renderSpotcolors = renderSpotcolors;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures whether frames are coalesced (blended onto canvas dimensions).
        /// Ported from <c>JxlDecoderSetCoalescing</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="coalescing"><c>true</c> to coalesce frames; <c>false</c> to output raw layer frames.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetCoalescing(bool coalescing)
        {
            if (_stage != DecoderStage.Inited)
            {
                return JxlDecoderStatus.Error;
            }

            _coalescing = coalescing;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the input memory buffer for the next <see cref="ProcessInput"/> call.
        /// Ported from <c>JxlDecoderSetInput</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="data">Input read-only memory buffer.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetInput(ReadOnlyMemory<byte> data)
        {
            if (_hasInput || _inputClosed)
            {
                return JxlDecoderStatus.Error;
            }

            _nextIn   = data;
            _availIn  = (nuint)data.Length;
            _hasInput = true;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the input byte span for the next <see cref="ProcessInput"/> call.
        /// </summary>
        /// <param name="data">Input byte span.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetInput(ReadOnlySpan<byte> data)
        {
            if (_hasInput || _inputClosed)
            {
                return JxlDecoderStatus.Error;
            }

            return SetInput((ReadOnlyMemory<byte>)data.ToArray());
        }

        /// <summary>
        /// Sets the input byte array for the next <see cref="ProcessInput"/> call.
        /// </summary>
        /// <param name="data">Input byte array.</param>
        /// <param name="size">Number of bytes at <paramref name="data"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetInput(byte[] data, nuint size)
        {
            if (_hasInput || _inputClosed || data == null || size > (nuint)data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetInput(data.AsMemory(0, (int)size));
        }

        /// <summary>
        /// Sets the input byte array for the next <see cref="ProcessInput"/> call.
        /// </summary>
        /// <param name="data">Input byte array.</param>
        /// <param name="offset">Starting byte offset in <paramref name="data"/>.</param>
        /// <param name="count">Number of bytes available from <paramref name="offset"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetInput(byte[] data, int offset, int count)
        {
            if (_hasInput || _inputClosed || data == null || offset < 0 || count < 0 || offset + count > data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetInput(data.AsMemory(offset, count));
        }

        /// <summary>
        /// Sets the input byte array for the next <see cref="ProcessInput"/> call.
        /// </summary>
        /// <param name="data">Input byte array.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetInput(byte[] data)
        {
            if (data == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetInput(data, 0, data.Length);
        }

        /// <summary>
        /// Releases the current input buffer and returns the number of unconsumed bytes remaining.
        /// Ported from <c>JxlDecoderReleaseInput</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Number of unused bytes remaining in the input buffer.</returns>
        public nuint ReleaseInput()
        {
            nuint remaining = _availIn >= _bleed ? _availIn - _bleed : 0u;

            if (remaining > 0 && _inCodestreamBox && _codestreamBoxRem > 0)
            {
                nuint rewind  = Math.Min(remaining, _codestreamBoxRem);
                nuint advance = remaining - rewind;
                _nextIn       = _nextIn.Slice((int)advance);
                _availIn     -= advance;
            }

            nuint result = _availIn;
            _nextIn      = default;
            _availIn     = 0;
            _bleed       = 0;
            _hasInput    = false;

            return result;
        }

        /// <summary>
        /// Marks the input stream as finished (no further bytes will be provided).
        /// Ported from <c>JxlDecoderCloseInput</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        public void CloseInput()
        {
            _inputClosed = true;
        }

        /// <summary>
        /// Processes available input bytes and advances decoding until an event or <see cref="JxlDecoderStatus.NeedMoreInput"/> occurs.
        /// Ported from <c>JxlDecoderProcessInput</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Current <see cref="JxlDecoderStatus"/>.</returns>
        public JxlDecoderStatus ProcessInput()
        {
            if (_stage == DecoderStage.Inited)
            {
                _stage = DecoderStage.Started;
            }

            if (_stage == DecoderStage.Error)
            {
                return JxlDecoderStatus.Error;
            }

            if (!_gotSignature)
            {
                JxlDecoderStatus sigStatus = DetectStreamSignature();

                if (sigStatus != JxlDecoderStatus.Success)
                {
                    return sigStatus;
                }
            }

            JxlDecoderStatus status = _haveContainer
                ? ProcessContainerInput()
                : ProcessRawCodestreamInput();

            if (status == JxlDecoderStatus.NeedMoreInput && _inputClosed)
            {
                return JxlDecoderStatus.Error;
            }

            return status;
        }

        /// <summary>
        /// Retrieves the parsed <see cref="JxlBasicInfo"/> after the <see cref="JxlDecoderStatus.BasicInfo"/> event.
        /// Ported from <c>JxlDecoderGetBasicInfo</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="info">Output basic info structure.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetBasicInfo(out JxlBasicInfo info)
        {
            info = default;

            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            ImageMetadata meta         = _metadata.M;
            info.HaveContainer         = _haveContainer;
            info.XSize                 = (uint)_metadata.Size.XSize();
            info.YSize                 = (uint)_metadata.Size.YSize();
            info.UsesOriginalProfile   = !meta.XybEncoded;
            info.BitsPerSample         = meta.BitDepth.BitsPerSample;
            info.ExponentBitsPerSample = meta.BitDepth.FloatingPointSample ? meta.BitDepth.ExponentBitsPerSample : 0u;
            info.IntensityTarget       = meta.ToneMapping.IntensityTarget;
            info.MinNits               = meta.ToneMapping.MinNits;
            info.RelativeToMaxDisplay  = meta.ToneMapping.RelativeToMaxDisplay;
            info.LinearBelow           = meta.ToneMapping.LinearBelow;
            info.HavePreview           = meta.HavePreview;
            info.HaveAnimation         = meta.HaveAnimation;
            info.Orientation           = _keepOrientation
                ? JxlOrientation.Identity
                : (JxlOrientation)meta.Orientation;

            if (!_keepOrientation && meta.Orientation > 4)
            {
                (info.XSize, info.YSize) = (info.YSize, info.XSize);
            }

            info.NumColorChannels = meta.ColorEncoding.GetColorSpace() == ColorSpace.Gray ? 1u : 3u;
            info.NumExtraChannels = (uint)meta.ExtraChannelInfo.Count;

            PopulateBasicInfoAlphaFields(meta, ref info);
            PopulateBasicInfoPreviewAndAnimation(meta, ref info);
            PopulateBasicInfoIntrinsicSize(meta, in info, ref info);

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves information about extra channel at <paramref name="index"/>.
        /// Ported from <c>JxlDecoderGetExtraChannelInfo</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="index">Extra channel index (<c>0 .. NumExtraChannels - 1</c>).</param>
        /// <param name="info">Output extra channel info.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetExtraChannelInfo(nuint index, out JxlExtraChannelInfo info)
        {
            info = default;

            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            List<ExtraChannelInfo> channels = _metadata.M.ExtraChannelInfo;

            if (index >= (nuint)channels.Count)
            {
                return JxlDecoderStatus.Error;
            }

            ExtraChannelInfo ch        = channels[(int)index];
            info.Type                  = (JxlExtraChannelType)ch.Type;
            info.BitsPerSample         = ch.BitDepth.BitsPerSample;
            info.ExponentBitsPerSample = ch.BitDepth.FloatingPointSample ? ch.BitDepth.ExponentBitsPerSample : 0u;
            info.DimShift              = ch.DimShift;
            info.NameLength            = (uint)Encoding.UTF8.GetByteCount(ch.Name);
            info.AlphaPremultiplied    = ch.AlphaAssociated;
            info.SpotColorR            = ch.SpotColor[0];
            info.SpotColorG            = ch.SpotColor[1];
            info.SpotColorB            = ch.SpotColor[2];
            info.SpotColorA            = ch.SpotColor[3];
            info.CfaChannel            = ch.CfaChannel;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves the UTF-8 null-terminated name of extra channel at <paramref name="index"/>.
        /// Ported from <c>JxlDecoderGetExtraChannelName</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="index">Extra channel index.</param>
        /// <param name="buffer">Destination buffer of size at least <c>NameLength + 1</c>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetExtraChannelName(nuint index, Span<byte> buffer)
        {
            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            List<ExtraChannelInfo> channels = _metadata.M.ExtraChannelInfo;

            if (index >= (nuint)channels.Count)
            {
                return JxlDecoderStatus.Error;
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(channels[(int)index].Name);

            if (buffer.Length < utf8.Length + 1)
            {
                return JxlDecoderStatus.Error;
            }

            utf8.CopyTo(buffer);
            buffer[utf8.Length] = 0;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves the color profile as a <see cref="JxlColorEncoding"/> if representable via enum values.
        /// Ported from <c>JxlDecoderGetColorAsEncodedProfile</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="target">Whether to query the original metadata profile or the data output profile.</param>
        /// <param name="colorEncoding">Output color encoding struct.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> if available as an encoded profile.</returns>
        public JxlDecoderStatus GetColorAsEncodedProfile(
            JxlColorProfileTarget target,
            out JxlColorEncoding colorEncoding)
        {
            colorEncoding = default;

            if (!_gotAllHeaders)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (target == JxlColorProfileTarget.Original && _metadata.M.ColorEncoding.WantICC())
            {
                return JxlDecoderStatus.Error;
            }

            ColorEncoding enc = GetTargetColorEncoding(target);

            if (!enc.HaveFields())
            {
                return JxlDecoderStatus.Error;
            }

            colorEncoding = enc.ToExternal();

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves the byte size of the ICC profile for <paramref name="target"/>.
        /// Ported from <c>JxlDecoderGetICCProfileSize</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="target">Profile target.</param>
        /// <param name="size">Output size in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetICCProfileSize(JxlColorProfileTarget target, out nuint size)
        {
            size = 0;

            if (!_gotAllHeaders)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (target == JxlColorProfileTarget.Original && _metadata.M.ColorEncoding.WantICC())
            {
                ColorSpace cs = _metadata.M.ColorEncoding.GetColorSpace();

                if (cs == ColorSpace.Unknown || cs == ColorSpace.XYB)
                {
                    return JxlDecoderStatus.Error;
                }
            }

            ColorEncoding enc = GetTargetColorEncoding(target);
            byte[] icc        = enc.ICC();

            if (icc.Length == 0)
            {
                return JxlDecoderStatus.Error;
            }

            size = (nuint)icc.Length;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Copies the ICC profile bytes for <paramref name="target"/> into <paramref name="iccProfile"/>.
        /// Ported from <c>JxlDecoderGetColorAsICCProfile</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="target">Profile target.</param>
        /// <param name="iccProfile">Destination span of length at least <see cref="GetICCProfileSize"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetColorAsICCProfile(
            JxlColorProfileTarget target,
            Span<byte> iccProfile)
        {
            JxlDecoderStatus status = GetICCProfileSize(target, out nuint size);

            if (status != JxlDecoderStatus.Success)
            {
                return status;
            }

            if ((nuint)iccProfile.Length < size)
            {
                return JxlDecoderStatus.Error;
            }

            ColorEncoding enc = GetTargetColorEncoding(target);
            enc.ICC().AsSpan(0, (int)size).CopyTo(iccProfile);

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the preferred output color profile before decoding frames.
        /// Ported from <c>JxlDecoderSetPreferredColorProfile</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="colorEncoding">Desired <see cref="JxlColorEncoding"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetPreferredColorProfile(in JxlColorEncoding colorEncoding)
        {
            if (!_gotAllHeaders || _passesState == null)
            {
                return JxlDecoderStatus.Error;
            }

            if (_metadata.M.ColorEncoding.IsCMYK())
            {
                return JxlDecoderStatus.Error;
            }

            ColorEncoding cDesired = new ColorEncoding();

            if (!cDesired.FromExternal(colorEncoding))
            {
                return JxlDecoderStatus.Error;
            }

            bool isGray = _metadata.M.ColorEncoding.IsGray();

            if (isGray != cDesired.IsGray())
            {
                return JxlDecoderStatus.Error;
            }

            if (!_metadata.M.XybEncoded && !_passesState.OutputEncodingInfo.CmsSet)
            {
                return JxlDecoderStatus.Error;
            }

            if (!DecXyb.CanOutputToColorEncoding(cDesired) && !_passesState.OutputEncodingInfo.CmsSet)
            {
                return JxlDecoderStatus.Error;
            }

            if (!_passesState.OutputEncodingInfo.MaybeSetColorEncoding(cDesired))
            {
                return JxlDecoderStatus.Error;
            }

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the desired intensity target in nits for tone mapping.
        /// Ported from <c>JxlDecoderSetDesiredIntensityTarget</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="desiredIntensityTarget">Target intensity in nits (or 0 to keep original).</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetDesiredIntensityTarget(float desiredIntensityTarget)
        {
            if (desiredIntensityTarget < 0.0f)
            {
                return JxlDecoderStatus.Error;
            }

            _desiredIntensityTarget = desiredIntensityTarget;

            if (_gotAllHeaders && _passesState != null)
            {
                _passesState.OutputEncodingInfo.DesiredIntensityTarget = desiredIntensityTarget == 0.0f
                    ? _passesState.OutputEncodingInfo.OrigIntensityTarget
                    : desiredIntensityTarget;
            }

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the output color profile using either a <see cref="JxlColorEncoding"/> or an ICC profile.
        /// Ported from <c>JxlDecoderSetOutputColorProfile</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="colorEncoding">Optional <see cref="JxlColorEncoding"/>.</param>
        /// <param name="iccData">Optional ICC profile bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetOutputColorProfile(
            JxlColorEncoding? colorEncoding,
            ReadOnlySpan<byte> iccData = default)
        {
            if ((colorEncoding.HasValue && !iccData.IsEmpty) ||
                (!colorEncoding.HasValue && iccData.IsEmpty))
            {
                return JxlDecoderStatus.Error;
            }

            if (colorEncoding.HasValue)
            {
                JxlColorEncoding ce = colorEncoding.Value;

                return SetPreferredColorProfile(in ce);
            }

            if (!_gotAllHeaders || _passesState == null || !_passesState.OutputEncodingInfo.CmsSet)
            {
                return JxlDecoderStatus.Error;
            }

            ColorEncoding iccEnc = new ColorEncoding();

            if (!iccEnc.SetICC(iccData.ToArray(), _passesState.OutputEncodingInfo.ColorManagementSystem))
            {
                return JxlDecoderStatus.Error;
            }

            if (!_passesState.OutputEncodingInfo.MaybeSetColorEncoding(iccEnc))
            {
                return JxlDecoderStatus.Error;
            }

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures the Color Management System (<see cref="JxlCmsInterface"/>) used for ICC color conversions.
        /// Ported from <c>JxlDecoderSetCms</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="cms">CMS interface implementation.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetCms(JxlCmsInterface cms)
        {
            if (_passesState == null)
            {
                return JxlDecoderStatus.Error;
            }

            _passesState.OutputEncodingInfo.ColorManagementSystem = cms;
            _passesState.OutputEncodingInfo.CmsSet                = true;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Computes the minimum buffer size in bytes required for the preview image in <paramref name="format"/>.
        /// Ported from <c>JxlDecoderPreviewOutBufferSize</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired pixel format.</param>
        /// <param name="size">Minimum buffer size in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus PreviewOutBufferSize(JxlPixelFormat format, out nuint size)
        {
            size = 0;

            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (format.NumChannels > 4 || format.NumChannels < 1)
            {
                return JxlDecoderStatus.Error;
            }

            if (format.NumChannels < 3 && !_metadata.M.ColorEncoding.IsGray())
            {
                return JxlDecoderStatus.Error;
            }

            nuint xsize = _metadata.M.PreviewSize.XSize();
            nuint ysize = _metadata.M.PreviewSize.YSize();

            if (!_keepOrientation && _metadata.M.Orientation > 4)
            {
                (xsize, ysize) = (ysize, xsize);
            }

            return ComputeMinBufferSize(xsize, ysize, format, out size);
        }

        /// <summary>
        /// Sets the output buffer to receive the preview image pixels.
        /// Ported from <c>JxlDecoderSetPreviewOutBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Output pixel format.</param>
        /// <param name="buffer">Destination memory buffer.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetPreviewOutBuffer(JxlPixelFormat format, Memory<byte> buffer)
        {
            if (!_gotBasicInfo || !_metadata.M.HavePreview || (_eventsSubscribed & (int)JxlDecoderStatus.PreviewImage) == 0)
            {
                return JxlDecoderStatus.Error;
            }

            JxlDecoderStatus minStatus = PreviewOutBufferSize(format, out nuint minSize);

            if (minStatus != JxlDecoderStatus.Success || (nuint)buffer.Length < minSize)
            {
                return JxlDecoderStatus.Error;
            }

            _previewOutBuffer     = buffer;
            _hasPreviewOutBuffer  = true;
            _previewOutBufferSize = (nuint)buffer.Length;
            _imageOutFormat       = format;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the output byte array to receive the preview image pixels.
        /// </summary>
        /// <param name="format">Output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="size">Size of <paramref name="buffer"/> in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetPreviewOutBuffer(JxlPixelFormat format, byte[] buffer, nuint size)
        {
            if (buffer == null || size > (nuint)buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetPreviewOutBuffer(format, buffer.AsMemory(0, (int)size));
        }

        /// <summary>
        /// Sets the output byte array slice to receive the preview image pixels.
        /// </summary>
        /// <param name="format">Output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="buffer"/>.</param>
        /// <param name="count">Available bytes from <paramref name="offset"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetPreviewOutBuffer(JxlPixelFormat format, byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetPreviewOutBuffer(format, buffer.AsMemory(offset, count));
        }

        /// <summary>
        /// Sets the output byte array to receive the preview image pixels.
        /// </summary>
        /// <param name="format">Output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetPreviewOutBuffer(JxlPixelFormat format, byte[] buffer)
        {
            if (buffer == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetPreviewOutBuffer(format, buffer.AsMemory());
        }

        /// <summary>
        /// Retrieves the <see cref="JxlFrameHeader"/> of the current frame after <see cref="JxlDecoderStatus.Frame"/>.
        /// Ported from <c>JxlDecoderGetFrameHeader</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="header">Output frame header.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetFrameHeader(out JxlFrameHeader header)
        {
            header = default;

            if (_frameDecoder == null || _frameStage == FrameStage.Header)
            {
                return JxlDecoderStatus.Error;
            }

            FrameHeader fh = _frameDecoder.GetFrameHeader();

            if (_metadata.M.HaveAnimation)
            {
                header.Duration = fh.AnimationFrame.Duration;

                if (_metadata.M.Animation.HaveTimecodes)
                {
                    header.Timecode = fh.AnimationFrame.Timecode;
                }
            }

            header.NameLength = (uint)Encoding.UTF8.GetByteCount(fh.Name);
            header.IsLast     = fh.IsLast;

            uint xsize  = (uint)_metadata.Size.XSize();
            uint ysize  = (uint)_metadata.Size.YSize();
            int x0      = 0;
            int y0      = 0;
            bool crop   = false;

            if (!_coalescing && fh.CustomSizeOrOrigin)
            {
                xsize = fh.FrameSize.XSize;
                ysize = fh.FrameSize.YSize;
                x0    = fh.FrameOrigin.X0;
                y0    = fh.FrameOrigin.Y0;
                crop  = true;
            }

            if (!_keepOrientation)
            {
                OrientLayerDimensions(ref xsize, ref ysize, ref x0, ref y0, crop);
            }

            JxlBlendInfo blendInfo = default;

            if (!_coalescing)
            {
                blendInfo.BlendMode = (JxlBlendMode)fh.BlendingInfo.Mode;
                blendInfo.Source    = fh.BlendingInfo.Source;
                blendInfo.Alpha     = fh.BlendingInfo.AlphaChannel;
                blendInfo.Clamp     = fh.BlendingInfo.Clamp;
            }
            else
            {
                blendInfo.BlendMode = JxlBlendMode.Replace;
            }

            header.LayerInfo = new JxlLayerInfo
            {
                HaveCrop        = crop,
                CropX0          = x0,
                CropY0          = y0,
                XSize           = xsize,
                YSize           = ysize,
                SaveAsReference = fh.SaveAsReference,
                BlendInfo       = blendInfo,
            };

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves the UTF-8 null-terminated name of the current frame.
        /// Ported from <c>JxlDecoderGetFrameName</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="buffer">Destination buffer of length at least <c>NameLength + 1</c>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetFrameName(Span<byte> buffer)
        {
            if (_frameDecoder == null || _frameStage == FrameStage.Header)
            {
                return JxlDecoderStatus.Error;
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(_frameDecoder.GetFrameHeader().Name);

            if (buffer.Length < utf8.Length + 1)
            {
                return JxlDecoderStatus.Error;
            }

            utf8.CopyTo(buffer);
            buffer[utf8.Length] = 0;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Retrieves the blend information for extra channel <paramref name="index"/> of the current frame.
        /// Ported from <c>JxlDecoderGetExtraChannelBlendInfo</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="index">Extra channel index.</param>
        /// <param name="blendInfo">Output blend info.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetExtraChannelBlendInfo(nuint index, out JxlBlendInfo blendInfo)
        {
            blendInfo = default;

            if (!_gotBasicInfo || _frameDecoder == null || _frameStage == FrameStage.Header)
            {
                return JxlDecoderStatus.Error;
            }

            if (index >= (nuint)_metadata.M.ExtraChannelInfo.Count)
            {
                return JxlDecoderStatus.Error;
            }

            FrameHeader fh = _frameDecoder.GetFrameHeader();

            if (!_coalescing && index < (nuint)fh.ExtraChannelBlendingInfo.Count)
            {
                BlendingInfo bi     = fh.ExtraChannelBlendingInfo[(int)index];
                blendInfo.BlendMode = (JxlBlendMode)bi.Mode;
                blendInfo.Source    = bi.Source;
                blendInfo.Alpha     = bi.AlphaChannel;
                blendInfo.Clamp     = bi.Clamp;
            }
            else
            {
                blendInfo.BlendMode = JxlBlendMode.Replace;
            }

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Computes the minimum buffer size in bytes required for the current frame's main pixel output.
        /// Ported from <c>JxlDecoderImageOutBufferSize</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="size">Minimum buffer size in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus ImageOutBufferSize(JxlPixelFormat format, out nuint size)
        {
            size = 0;

            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (format.NumChannels > 4 || format.NumChannels < 1)
            {
                return JxlDecoderStatus.Error;
            }

            if (format.NumChannels < 3 && !_metadata.M.ColorEncoding.IsGray())
            {
                return JxlDecoderStatus.Error;
            }

            GetCurrentFrameOrientedDimensions(out nuint xsize, out nuint ysize);

            return ComputeMinBufferSize(xsize, ysize, format, out size);
        }

        /// <summary>
        /// Sets the output buffer to receive the current frame's decoded pixels.
        /// Ported from <c>JxlDecoderSetImageOutBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination memory buffer.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutBuffer(JxlPixelFormat format, Memory<byte> buffer)
        {
            if (!_gotBasicInfo || (_eventsSubscribed & (int)JxlDecoderStatus.FullImage) == 0)
            {
                return JxlDecoderStatus.Error;
            }

            if (_pixelCallback.IsPresent() && _simpleImageOutCallback == null)
            {
                return JxlDecoderStatus.Error;
            }

            JxlDecoderStatus minStatus = ImageOutBufferSize(format, out nuint minSize);

            if (minStatus != JxlDecoderStatus.Success || (nuint)buffer.Length < minSize)
            {
                return JxlDecoderStatus.Error;
            }

            _pixelCallback          = default;
            _simpleImageOutCallback = null;
            _simpleImageOutOpaque   = null;
            _imageOutBuffer         = buffer;
            _hasImageOutBuffer      = true;
            _imageOutBufferSize     = (nuint)buffer.Length;
            _imageOutFormat         = format;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the output byte array to receive the current frame's decoded pixels.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="size">Size of <paramref name="buffer"/> in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutBuffer(JxlPixelFormat format, byte[] buffer, nuint size)
        {
            if (buffer == null || size > (nuint)buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetImageOutBuffer(format, buffer.AsMemory(0, (int)size));
        }

        /// <summary>
        /// Sets the output byte array slice to receive the current frame's decoded pixels.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="buffer"/>.</param>
        /// <param name="count">Available bytes from <paramref name="offset"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutBuffer(JxlPixelFormat format, byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetImageOutBuffer(format, buffer.AsMemory(offset, count));
        }

        /// <summary>
        /// Sets the output byte array to receive the current frame's decoded pixels.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutBuffer(JxlPixelFormat format, byte[] buffer)
        {
            if (buffer == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetImageOutBuffer(format, buffer.AsMemory());
        }

        /// <summary>
        /// Sets a line-by-line output callback to receive the current frame's decoded pixels.
        /// Ported from <c>JxlDecoderSetImageOutCallback</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="callback">Simple line output callback.</param>
        /// <param name="opaque">Opaque user state passed to <paramref name="callback"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutCallback(
            JxlPixelFormat format,
            JxlImageOutCallback callback,
            object? opaque = null)
        {
            if (callback == null)
            {
                return JxlDecoderStatus.Error;
            }

            _simpleImageOutCallback = callback;
            _simpleImageOutOpaque   = opaque;

            JxlImageOutInitCallback initCb = (_, _, _) => this;
            JxlImageOutRunCallback runCb   = (runOpaque, _, x, y, numPixels, pixels) =>
            {
                JxlDecoder self = (JxlDecoder)runOpaque!;
                self._simpleImageOutCallback!(self._simpleImageOutOpaque, x, y, numPixels, pixels);
            };
            JxlImageOutDestroyCallback destroyCb = _ => { };

            return SetMultithreadedImageOutCallback(format, initCb, runCb, destroyCb, this);
        }

        /// <summary>
        /// Sets a multithreaded line-by-line output callback to receive the current frame's decoded pixels.
        /// Ported from <c>JxlDecoderSetMultithreadedImageOutCallback</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="initCallback">Initialization callback.</param>
        /// <param name="runCallback">Per-stripe execution callback.</param>
        /// <param name="destroyCallback">Cleanup callback.</param>
        /// <param name="initOpaque">Opaque user state passed to <paramref name="initCallback"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetMultithreadedImageOutCallback(
            JxlPixelFormat format,
            JxlImageOutInitCallback initCallback,
            JxlImageOutRunCallback runCallback,
            JxlImageOutDestroyCallback? destroyCallback,
            object? initOpaque = null)
        {
            if (!_gotBasicInfo || (_eventsSubscribed & (int)JxlDecoderStatus.FullImage) == 0)
            {
                return JxlDecoderStatus.Error;
            }

            if (_hasImageOutBuffer || initCallback == null || runCallback == null)
            {
                return JxlDecoderStatus.Error;
            }

            if (format.NumChannels > 4 || format.NumChannels < 1)
            {
                return JxlDecoderStatus.Error;
            }

            if (format.NumChannels < 3 && !_metadata.M.ColorEncoding.IsGray())
            {
                return JxlDecoderStatus.Error;
            }

            _pixelCallback      = new PixelCallback(initCallback, runCallback, destroyCallback, initOpaque);
            _imageOutFormat     = format;
            _imageOutBuffer     = default;
            _hasImageOutBuffer  = false;
            _imageOutBufferSize = 0;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Computes the minimum buffer size in bytes required for extra channel <paramref name="index"/>.
        /// Ported from <c>JxlDecoderExtraChannelBufferSize</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format (<c>NumChannels</c> ignored, treated as 1).</param>
        /// <param name="size">Minimum buffer size in bytes.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus ExtraChannelBufferSize(JxlPixelFormat format, out nuint size, uint index)
        {
            size = 0;

            if (!_gotBasicInfo)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            if (index >= (uint)_metadata.M.ExtraChannelInfo.Count)
            {
                return JxlDecoderStatus.Error;
            }

            GetCurrentFrameOrientedDimensions(out nuint xsize, out nuint ysize);
            JxlPixelFormat singleFormat = format;
            singleFormat.NumChannels    = 1;

            return ComputeMinBufferSize(xsize, ysize, singleFormat, out size);
        }

        /// <summary>
        /// Sets the output buffer for extra channel <paramref name="index"/>.
        /// Ported from <c>JxlDecoderSetExtraChannelBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination memory buffer.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetExtraChannelBuffer(
            JxlPixelFormat format,
            Memory<byte> buffer,
            uint index)
        {
            if (!_gotBasicInfo || (_eventsSubscribed & (int)JxlDecoderStatus.FullImage) == 0)
            {
                return JxlDecoderStatus.Error;
            }

            JxlDecoderStatus minStatus = ExtraChannelBufferSize(format, out nuint minSize, index);

            if (minStatus != JxlDecoderStatus.Success || (nuint)buffer.Length < minSize)
            {
                return JxlDecoderStatus.Error;
            }

            EnsureExtraChannelSlots((int)index + 1);
            JxlPixelFormat ecFormat                = format;
            ecFormat.NumChannels                   = 1;
            _extraChannelOutBuffers[(int)index]    = buffer;
            _extraChannelOutHasBuffers[(int)index] = true;
            _extraChannelOutSizes[(int)index]      = (nuint)buffer.Length;
            _extraChannelOutFormats[(int)index]    = ecFormat;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the output byte array for extra channel <paramref name="index"/>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="size">Size of <paramref name="buffer"/> in bytes.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetExtraChannelBuffer(
            JxlPixelFormat format,
            byte[] buffer,
            nuint size,
            uint index)
        {
            if (buffer == null || size > (nuint)buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetExtraChannelBuffer(format, buffer.AsMemory(0, (int)size), index);
        }

        /// <summary>
        /// Sets the output byte array slice for extra channel <paramref name="index"/>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="buffer"/>.</param>
        /// <param name="count">Available bytes from <paramref name="offset"/>.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetExtraChannelBuffer(
            JxlPixelFormat format,
            byte[] buffer,
            int offset,
            int count,
            uint index)
        {
            if (buffer == null || offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetExtraChannelBuffer(format, buffer.AsMemory(offset, count), index);
        }

        /// <summary>
        /// Sets the output byte array for extra channel <paramref name="index"/>.
        /// </summary>
        /// <param name="format">Desired output pixel format.</param>
        /// <param name="buffer">Destination byte array.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetExtraChannelBuffer(JxlPixelFormat format, byte[] buffer, uint index)
        {
            if (buffer == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetExtraChannelBuffer(format, buffer.AsMemory(), index);
        }

        /// <summary>
        /// Sets the output buffer for lossless JPEG reconstruction.
        /// Ported from <c>JxlDecoderSetJPEGBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="data">Destination memory buffer.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetJPEGBuffer(Memory<byte> data)
        {
            if ((_eventsSubscribed & (int)JxlDecoderStatus.JpegReconstruction) == 0)
            {
                return JxlDecoderStatus.Error;
            }

            return _jpegDecoder.SetOutputBuffer(data);
        }

        /// <summary>
        /// Sets the output byte array for lossless JPEG reconstruction.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <param name="size">Size of <paramref name="data"/> in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetJPEGBuffer(byte[] data, nuint size)
        {
            if (data == null || size > (nuint)data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetJPEGBuffer(data.AsMemory(0, (int)size));
        }

        /// <summary>
        /// Sets the output byte array for lossless JPEG reconstruction.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="data"/>.</param>
        /// <param name="count">Available bytes from <paramref name="offset"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetJPEGBuffer(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count < 0 || offset + count > data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetJPEGBuffer(data.AsMemory(offset, count));
        }

        /// <summary>
        /// Sets the output byte array for lossless JPEG reconstruction.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetJPEGBuffer(byte[] data)
        {
            if (data == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetJPEGBuffer(data.AsMemory());
        }

        /// <summary>
        /// Releases the JPEG reconstruction output buffer and returns the number of unwritten bytes remaining.
        /// Ported from <c>JxlDecoderReleaseJPEGBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Unwritten bytes remaining in the JPEG buffer.</returns>
        public nuint ReleaseJPEGBuffer()
        {
            return _jpegDecoder.ReleaseOutputBuffer();
        }

        /// <summary>
        /// Sets the output buffer for extracting container box contents.
        /// Ported from <c>JxlDecoderSetBoxBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="data">Destination memory buffer.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetBoxBuffer(Memory<byte> data)
        {
            if (_boxOutBufferSet || !_boxEvent)
            {
                return JxlDecoderStatus.Error;
            }

            _boxOutBufferSet           = true;
            _boxOutBufferSetCurrentBox = true;
            _boxOutBuffer              = data;
            _hasBoxOutBuffer           = true;
            _boxOutBufferSize          = (nuint)data.Length;
            _boxOutBufferPos           = 0;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Sets the output byte array for extracting container box contents.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <param name="size">Size of <paramref name="data"/> in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetBoxBuffer(byte[] data, nuint size)
        {
            if (data == null || size > (nuint)data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetBoxBuffer(data.AsMemory(0, (int)size));
        }

        /// <summary>
        /// Sets the output byte array for extracting container box contents.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <param name="offset">Starting offset in <paramref name="data"/>.</param>
        /// <param name="count">Available bytes from <paramref name="offset"/>.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetBoxBuffer(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count < 0 || offset + count > data.Length)
            {
                return JxlDecoderStatus.Error;
            }

            return SetBoxBuffer(data.AsMemory(offset, count));
        }

        /// <summary>
        /// Sets the output byte array for extracting container box contents.
        /// </summary>
        /// <param name="data">Destination byte array.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetBoxBuffer(byte[] data)
        {
            if (data == null)
            {
                return JxlDecoderStatus.Error;
            }

            return SetBoxBuffer(data.AsMemory());
        }

        /// <summary>
        /// Releases the box output buffer and returns the number of unwritten bytes remaining.
        /// Ported from <c>JxlDecoderReleaseBoxBuffer</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Unwritten bytes remaining in the box buffer.</returns>
        public nuint ReleaseBoxBuffer()
        {
            if (!_boxOutBufferSet)
            {
                return 0;
            }

            nuint rem                  = _boxOutBufferSize - _boxOutBufferPos;
            _boxOutBufferSet           = false;
            _boxOutBufferSetCurrentBox = false;
            _boxOutBuffer              = default;
            _hasBoxOutBuffer           = false;
            _boxOutBufferSize          = 0;
            _boxOutBufferPos           = 0;

            return rem;
        }

        /// <summary>
        /// Configures whether <c>brob</c> Brotli-compressed boxes should be automatically decompressed.
        /// Ported from <c>JxlDecoderSetDecompressBoxes</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="decompress"><c>true</c> to decompress <c>brob</c> boxes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetDecompressBoxes(bool decompress)
        {
            _decompressBoxes = decompress;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Gets the 4-character box type of the current box after <see cref="JxlDecoderStatus.Box"/>.
        /// Ported from <c>JxlDecoderGetBoxType</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="boxType">Output 4-character box type.</param>
        /// <param name="decompressed"><c>true</c> to get the underlying type of a <c>brob</c> box.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetBoxType(out JxlBoxType boxType, bool decompressed)
        {
            boxType = default;

            if (!_boxEvent)
            {
                return JxlDecoderStatus.Error;
            }

            boxType = decompressed ? _boxType : _rawBoxType;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Gets the raw total size of the current box (including header), or 0 if unbounded until EOF.
        /// Ported from <c>JxlDecoderGetBoxSizeRaw</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="size">Output raw box size in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetBoxSizeRaw(out ulong size)
        {
            size = 0;

            if (!_boxEvent)
            {
                return JxlDecoderStatus.Error;
            }

            size = _boxUnbounded ? 0UL : _boxSize;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Gets the payload size of the current box (excluding box header and 4-byte brob inner type if decompressed).
        /// Ported from <c>JxlDecoderGetBoxSizeContents</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="size">Output payload size in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus GetBoxSizeContents(out ulong size)
        {
            size = 0;

            if (!_boxEvent || (_inBrob && _decompressBoxes))
            {
                return JxlDecoderStatus.Error;
            }

            size = _boxUnbounded ? 0UL : _boxContentsSize;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures the progressive detail level at which <see cref="JxlDecoderStatus.FrameProgression"/> events are emitted.
        /// Ported from <c>JxlDecoderSetProgressiveDetail</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="detail">Desired progressive detail level.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetProgressiveDetail(JxlProgressiveDetail detail)
        {
            _progDetail = detail;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Returns the intended downsampling ratio of the current progressive step.
        /// Ported from <c>JxlDecoderGetIntendedDownsamplingRatio</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns>Downsampling ratio (e.g., 8 for DC, or 4/2/1 for AC passes).</returns>
        public nuint GetIntendedDownsamplingRatio()
        {
            if (_frameStage != FrameStage.Full || _frameDecoder == null)
            {
                return 0;
            }

            FrameHeader fh = _frameDecoder.GetFrameHeader();

            if (!_frameDecoder.HasDecodedDC())
            {
                return 0;
            }

            uint completePasses = _frameDecoder.NumCompletePasses();

            if (completePasses == 0)
            {
                return 8;
            }

            return fh.Passes.GetDownsamplingTargetForCompletedPasses(completePasses);
        }

        /// <summary>
        /// Flushes the progressive image decoded so far into the configured output buffer or callback.
        /// Ported from <c>JxlDecoderFlushImage</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus FlushImage()
        {
            if (_frameStage != FrameStage.Full || _frameDecoder == null || _ib == null)
            {
                return JxlDecoderStatus.Error;
            }

            if (!_hasImageOutBuffer && !_pixelCallback.IsPresent())
            {
                return JxlDecoderStatus.Error;
            }

            if (_ib.IsJPEG() || !_frameDecoder.HasDecodedDC())
            {
                return JxlDecoderStatus.Error;
            }

            if (!_frameDecoder.Flush().IsOk)
            {
                return JxlDecoderStatus.Error;
            }

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Configures the bit depth interpretation of integer output buffers/callbacks.
        /// Ported from <c>JxlDecoderSetImageOutBitDepth</c> in <c>lib/jxl/decode.cc</c>.
        /// </summary>
        /// <param name="bitDepth">Desired bit depth settings.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> on success.</returns>
        public JxlDecoderStatus SetImageOutBitDepth(in JxlBitDepth bitDepth)
        {
            _imageOutBitDepth = bitDepth;

            return JxlDecoderStatus.Success;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _iccReader.Reset();
            _frameDecoder?.Dispose();
            _frameDecoder = null;
            _passesState?.Dispose();
            _passesState  = null;
            _ib?.Dispose();
            _ib           = null;
        }

        private static nuint InitialBasicInfoSizeHint()
        {
            return 2u + 1u + 4u + 11u;
        }

        private JxlDecoderStatus DetectStreamSignature()
        {
            JxlSignature sig = CheckSignature(_nextIn.Span.Slice(0, (int)_availIn));

            if (sig == JxlSignature.Invalid)
            {
                _stage = DecoderStage.Error;

                return JxlDecoderStatus.Error;
            }

            if (sig == JxlSignature.NotEnoughBytes)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            _gotSignature  = true;
            _haveContainer = sig == JxlSignature.Container;

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus ProcessRawCodestreamInput()
        {
            if (_jpegDecoder.IsParsingBox)
            {
                _stage = DecoderStage.Error;

                return JxlDecoderStatus.Error;
            }

            return ProcessCodestreamSlice(ref _nextIn, ref _availIn, isLastSlice: _inputClosed);
        }

        private JxlDecoderStatus ProcessContainerInput()
        {
            while (true)
            {
                BoxLoopResult stepResult = StepContainerStateMachine(out JxlDecoderStatus status);

                if (stepResult == BoxLoopResult.ReturnStatus)
                {
                    return status;
                }
            }
        }

        private BoxLoopResult StepContainerStateMachine(out JxlDecoderStatus status)
        {
            if (_jpegDecoder.IsParsingBox)
            {
                nuint prevAvail = _availIn;
                status          = _jpegDecoder.Process(ref _nextIn, ref _availIn);
                _filePos       += prevAvail - _availIn;

                if (status == JxlDecoderStatus.JpegReconstruction)
                {
                    status = OnJbrdBoxDecoded();
                }

                if (status != JxlDecoderStatus.Success)
                {
                    return BoxLoopResult.ReturnStatus;
                }
            }

            if ((_storeExif == 1 || _storeXmp == 1) && (_boxUnbounded || _filePos < _boxContentsEnd))
            {
                JxlDecoderStatus metaStatus = PumpReconMetadataBoxPayload();

                if (metaStatus != JxlDecoderStatus.Success)
                {
                    status = metaStatus;

                    return BoxLoopResult.ReturnStatus;
                }
            }

            if (_reconOutputJpeg && !JbrdNeedMoreBoxes())
            {
                status = FlushJpegReconstructionOutput();

                return BoxLoopResult.ReturnStatus;
            }

            if (_boxEvent && !_boxOutBufferSetCurrentBox)
            {
                _boxEvent = false;
            }

            if (_boxOutBufferSetCurrentBox && _filePos < _boxContentsEnd)
            {
                JxlDecoderStatus boxStatus = PumpActiveUserBoxPayload();

                if (boxStatus != JxlDecoderStatus.Success)
                {
                    status = boxStatus;

                    return BoxLoopResult.ReturnStatus;
                }
            }

            if (_inCodestreamBox)
            {
                return StepInsideCodestreamBox(out status);
            }

            return ParseNextContainerBoxHeader(out status);
        }

        private bool JbrdNeedMoreBoxes()
        {
            if (_storeExif < 2 && _reconExifSize > 0)
            {
                return true;
            }

            if (_storeXmp < 2 && _reconXmpSize > 0)
            {
                return true;
            }

            return false;
        }

        private JxlDecoderStatus OnJbrdBoxDecoded()
        {
            JPEGData? jpegData = _jpegDecoder.GetJpegData();

            if (jpegData != null)
            {
                nuint numExif = JxlToJpegDecoder.NumExifMarkers(jpegData);
                nuint numXmp  = JxlToJpegDecoder.NumXmpMarkers(jpegData);

                if (numExif > 0 &&
                    (numExif > 1 || JxlToJpegDecoder.ExifBoxContentSize(jpegData, out _reconExifSize) != JxlDecoderStatus.Success))
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }

                if (numXmp > 0 &&
                    (numXmp > 1 || JxlToJpegDecoder.XmlBoxContentSize(jpegData, out _reconXmpSize) != JxlDecoderStatus.Success))
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }
            }

            _eventsProcessed |= (int)JxlDecoderStatus.JpegReconstruction;

            return JxlDecoderStatus.JpegReconstruction;
        }

        private JxlDecoderStatus PumpReconMetadataBoxPayload()
        {
            nuint remInBox = _boxUnbounded
                ? _availIn
                : (_boxContentsEnd > _filePos ? _boxContentsEnd - _filePos : 0u);
            nuint availNow = Math.Min(_availIn, remInBox);

            if (availNow == 0)
            {
                return JxlDecoderStatus.Success;
            }

            ref byte[] metadata = ref (_storeExif == 1 ? ref _exifMetadata : ref _xmpMetadata);

            if (metadata.Length == 0)
            {
                metadata = new byte[64];
            }

            ReadOnlySpan<byte> inSpan = _nextIn.Span.Slice(0, (int)availNow);

            return PumpReconMetadataLoop(inSpan, ref metadata);
        }

        private JxlDecoderStatus PumpReconMetadataLoop(ReadOnlySpan<byte> inSpan, ref byte[] metadata)
        {
            const int kBlockSizeLimit = 64 << 20;

            while (true)
            {
                Span<byte> outSpan         = metadata.AsSpan((int)_reconOutBufferPos);
                JxlDecoderStatus boxResult = _metadataDecoder.Process(
                    inSpan,
                    _filePos - _boxContentsBegin,
                    outSpan,
                    out nuint produced);

                _reconOutBufferPos += produced;

                if (boxResult == JxlDecoderStatus.BoxNeedMoreOutput)
                {
                    if (metadata.Length >= kBlockSizeLimit)
                    {
                        _stage = DecoderStage.Error;

                        return JxlDecoderStatus.Error;
                    }

                    Array.Resize(ref metadata, metadata.Length * 2);
                }
                else if (boxResult == JxlDecoderStatus.NeedMoreInput)
                {
                    return JxlDecoderStatus.Success;
                }
                else if (boxResult == JxlDecoderStatus.BoxComplete)
                {
                    CompleteReconMetadataBox(ref metadata);

                    return JxlDecoderStatus.Success;
                }
                else
                {
                    _stage = DecoderStage.Error;

                    return boxResult;
                }
            }
        }

        private void CompleteReconMetadataBox(ref byte[] metadata)
        {
            nuint neededSize = _storeExif == 1 ? _reconExifSize : _reconXmpSize;

            if (_boxUnbounded && neededSize > 0 && _reconOutBufferPos < neededSize)
            {
                return;
            }

            Array.Resize(ref metadata, (int)_reconOutBufferPos);

            if (_storeExif == 1)
            {
                _storeExif = 2;
            }

            if (_storeXmp == 1)
            {
                _storeXmp = 2;
            }
        }

        private JxlDecoderStatus PumpActiveUserBoxPayload()
        {
            nuint remInBox            = _boxUnbounded
                ? _availIn
                : (_boxContentsEnd > _filePos ? _boxContentsEnd - _filePos : 0u);
            nuint availNow            = Math.Min(_availIn, remInBox);
            ReadOnlySpan<byte> inSpan = _nextIn.Span.Slice(0, (int)availNow);
            Span<byte> outSpan        = _hasBoxOutBuffer && _boxOutBufferSize > _boxOutBufferPos
                ? _boxOutBuffer.Span.Slice((int)_boxOutBufferPos, (int)(_boxOutBufferSize - _boxOutBufferPos))
                : Span<byte>.Empty;

            JxlDecoderStatus status = _boxContentDecoder.Process(
                inSpan,
                _filePos - _boxContentsBegin,
                outSpan,
                out nuint bytesWritten);

            _boxOutBufferPos += bytesWritten;

            if (status == JxlDecoderStatus.BoxNeedMoreOutput || status == JxlDecoderStatus.Error)
            {
                return status;
            }

            AdvanceFileInput(availNow);

            if (status == JxlDecoderStatus.NeedMoreInput && !_boxUnbounded && _filePos >= _boxContentsEnd)
            {
                return JxlDecoderStatus.Error;
            }

            if (status == JxlDecoderStatus.BoxComplete && !SubscribedTo(JxlDecoderStatus.BoxComplete))
            {
                return JxlDecoderStatus.Success;
            }

            return status == JxlDecoderStatus.NeedMoreInput ? JxlDecoderStatus.Success : status;
        }

        private BoxLoopResult StepInsideCodestreamBox(out JxlDecoderStatus status)
        {
            if (_codestreamBoxRem == 0 && !_boxUnbounded && _codestreamCopyLen == 0)
            {
                _inCodestreamBox = false;
                status           = JxlDecoderStatus.Success;

                return BoxLoopResult.Continue;
            }

            nuint sliceAvail            = _boxUnbounded ? _availIn : Math.Min(_availIn, _codestreamBoxRem);
            ReadOnlyMemory<byte> slice  = _nextIn.Slice(0, (int)sliceAvail);
            nuint origAvail             = sliceAvail;
            bool isLastSlice            = (_seenJxlc && (_codestreamBoxRem <= _availIn || _inputClosed)) ||
                                          (_noMoreCodestreamInput && _codestreamBoxRem <= _availIn);

            status = ProcessCodestreamSlice(ref slice, ref sliceAvail, isLastSlice);

            nuint consumed = origAvail - sliceAvail;

            AdvanceFileInput(consumed);

            if (!_boxUnbounded)
            {
                _codestreamBoxRem -= consumed;

                if (_codestreamBoxRem == 0)
                {
                    _inCodestreamBox = false;
                }
            }

            if (status == JxlDecoderStatus.NeedMoreInput && !_inCodestreamBox && _availIn > 0)
            {
                return BoxLoopResult.Continue;
            }

            return BoxLoopResult.ReturnStatus;
        }

        private BoxLoopResult ParseNextContainerBoxHeader(out JxlDecoderStatus status)
        {
            if (!_boxUnbounded && _filePos < _boxContentsEnd)
            {
                nuint skip = Math.Min(_availIn, _boxContentsEnd - _filePos);

                AdvanceFileInput(skip);

                if (_filePos < _boxContentsEnd)
                {
                    status = FlushBufferedCodestreamIfReady();

                    return BoxLoopResult.ReturnStatus;
                }
            }

            if (_availIn == 0)
            {
                status = CheckEndOfContainerOrNeedMoreInput();

                return BoxLoopResult.ReturnStatus;
            }

            if (_filePos == 0)
            {
                return ConsumeInitialSignatureAndFtypBoxes(out status);
            }

            return ReadAndDispatchGeneralBoxHeader(out status);
        }

        private JxlDecoderStatus FlushBufferedCodestreamIfReady()
        {
            if (_codestreamCopyLen > 0 && !_isLastTotal)
            {
                ReadOnlyMemory<byte> emptySlice = ReadOnlyMemory<byte>.Empty;
                nuint emptyAvail                = 0;

                return ProcessCodestreamSlice(ref emptySlice, ref emptyAvail, isLastSlice: _noMoreCodestreamInput);
            }

            return JxlDecoderStatus.NeedMoreInput;
        }

        private JxlDecoderStatus CheckEndOfContainerOrNeedMoreInput()
        {
            if (_codestreamCopyLen > 0 && !_isLastTotal)
            {
                ReadOnlyMemory<byte> emptySlice = ReadOnlyMemory<byte>.Empty;
                nuint emptyAvail                = 0;
                JxlDecoderStatus csStatus       = ProcessCodestreamSlice(ref emptySlice, ref emptyAvail, isLastSlice: _noMoreCodestreamInput);

                if (csStatus != JxlDecoderStatus.NeedMoreInput)
                {
                    return csStatus;
                }
            }

            if (_isLastTotal &&
                (!SubscribedTo(JxlDecoderStatus.JpegReconstruction) || (!_seenJbrd && !JbrdNeedMoreBoxes())) &&
                !SubscribedTo(JxlDecoderStatus.Box))
            {
                if (_boxOutBufferSet && _boxUnbounded)
                {
                    return JxlDecoderStatus.BoxComplete;
                }

                _stage = DecoderStage.CodestreamFinished;

                return JxlDecoderStatus.Success;
            }

            if (_inputClosed && _isLastTotal && !JbrdNeedMoreBoxes())
            {
                _stage = DecoderStage.CodestreamFinished;

                return JxlDecoderStatus.Success;
            }

            return JxlDecoderStatus.NeedMoreInput;
        }

        private BoxLoopResult ConsumeInitialSignatureAndFtypBoxes(out JxlDecoderStatus status)
        {
            const nuint sigAndFtypLen = 12u + 20u;

            if (_availIn < sigAndFtypLen)
            {
                status = JxlDecoderStatus.NeedMoreInput;

                return BoxLoopResult.ReturnStatus;
            }

            ReadOnlySpan<byte> ftypSpan = _nextIn.Span.Slice(12, 20);

            if (!ftypSpan.SequenceEqual(FtypHeaderPrefix))
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            AdvanceFileInput(sigAndFtypLen);
            _boxContentsEnd = _filePos;
            status          = JxlDecoderStatus.Success;

            return BoxLoopResult.Continue;
        }

        private BoxLoopResult ReadAndDispatchGeneralBoxHeader(out JxlDecoderStatus status)
        {
            if (_availIn < 8)
            {
                status = JxlDecoderStatus.NeedMoreInput;

                return BoxLoopResult.ReturnStatus;
            }

            ReadOnlySpan<byte> inSpan = _nextIn.Span;
            ulong totalSize           = BinaryPrimitives.ReadUInt32BigEndian(inSpan.Slice(0, 4));
            JxlBoxType rawType        = new JxlBoxType(inSpan[4], inSpan[5], inSpan[6], inSpan[7]);
            nuint hdrSize             = 8;

            if (totalSize == 1)
            {
                if (_availIn < 16)
                {
                    status = JxlDecoderStatus.NeedMoreInput;

                    return BoxLoopResult.ReturnStatus;
                }

                totalSize = BinaryPrimitives.ReadUInt64BigEndian(inSpan.Slice(8, 8));
                hdrSize   = 16;
            }

            bool isBrob = IsBoxType(rawType, "brob");

            if (isBrob && _availIn < hdrSize + 4)
            {
                status = JxlDecoderStatus.NeedMoreInput;

                return BoxLoopResult.ReturnStatus;
            }

            if (totalSize != 0 && totalSize < hdrSize + (isBrob ? 4u : 0u))
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            int h                = (int)hdrSize;
            JxlBoxType innerType = isBrob
                ? new JxlBoxType(inSpan[h], inSpan[h + 1], inSpan[h + 2], inSpan[h + 3])
                : rawType;

            return DispatchParsedBox(rawType, innerType, isBrob, hdrSize, (nuint)totalSize, out status);
        }

        private BoxLoopResult DispatchParsedBox(
            JxlBoxType rawType,
            JxlBoxType innerType,
            bool isBrob,
            nuint hdrSize,
            nuint totalSize,
            out JxlDecoderStatus status)
        {
            if (isBrob && (IsBoxType(innerType, "jxlc") || IsBoxType(innerType, "jxlp") ||
                           IsBoxType(innerType, "JXL ") || IsBoxType(innerType, "ftyp") ||
                           IsBoxType(innerType, "brob")))
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            _rawBoxType                = rawType;
            _boxType                   = innerType;
            _inBrob                    = isBrob;
            _headerSize                = hdrSize;
            _boxSize                   = totalSize;
            _boxUnbounded              = totalSize == 0;
            _boxContentsBegin          = _filePos + hdrSize;
            _boxContentsEnd            = _boxUnbounded ? nuint.MaxValue : _filePos + totalSize;
            _boxContentsSize           = _boxUnbounded ? 0u : totalSize - hdrSize;
            _boxOutBufferSetCurrentBox = false;

            if (IsBoxType(rawType, "jxlc") || IsBoxType(rawType, "jxlp"))
            {
                return HandleCodestreamBoxHeader(rawType, hdrSize, totalSize, out status);
            }

            AdvanceFileInput(hdrSize);
            StartReconMetadataBoxIfNeeded(innerType);

            if (IsBoxType(innerType, "jbrd") && SubscribedTo(JxlDecoderStatus.JpegReconstruction))
            {
                return HandleJbrdBoxPayload(isBrob, out status);
            }

            if (SubscribedTo(JxlDecoderStatus.Box))
            {
                _boxEvent = true;
                _boxContentDecoder.StartBox(_inBrob && _decompressBoxes, _boxUnbounded, _boxContentsSize);
                status    = JxlDecoderStatus.Box;

                return BoxLoopResult.ReturnStatus;
            }

            status = JxlDecoderStatus.Success;

            return BoxLoopResult.Continue;
        }

        private void StartReconMetadataBoxIfNeeded(JxlBoxType innerType)
        {
            if (!SubscribedTo(JxlDecoderStatus.JpegReconstruction))
            {
                return;
            }

            if (_storeExif == 0 && IsBoxType(innerType, "Exif"))
            {
                _storeExif         = 1;
                _reconOutBufferPos = 0;
                _metadataDecoder.StartBox(_inBrob, _boxUnbounded, _boxContentsSize);
            }
            else if (_storeXmp == 0 && IsBoxType(innerType, "xml "))
            {
                _storeXmp          = 1;
                _reconOutBufferPos = 0;
                _metadataDecoder.StartBox(_inBrob, _boxUnbounded, _boxContentsSize);
            }
        }

        private BoxLoopResult HandleCodestreamBoxHeader(
            JxlBoxType rawType,
            nuint hdrSize,
            nuint totalSize,
            out JxlDecoderStatus status)
        {
            bool isJxlc = IsBoxType(rawType, "jxlc");

            if (!isJxlc && !_boxUnbounded && totalSize < hdrSize + 4u)
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            if (!isJxlc && _availIn < hdrSize + 4u)
            {
                status = JxlDecoderStatus.NeedMoreInput;

                return BoxLoopResult.ReturnStatus;
            }

            if (_seenJxlc || (isJxlc && _seenJxlp) || _noMoreCodestreamInput)
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            if (isJxlc)
            {
                _seenJxlc              = true;
                _noMoreCodestreamInput = true;
                AdvanceFileInput(hdrSize);
                _inCodestreamBox       = true;
                _codestreamBoxRem      = _boxUnbounded ? 0u : totalSize - hdrSize;
                status                 = JxlDecoderStatus.Success;

                return BoxLoopResult.Continue;
            }

            uint jxlpIndex = BinaryPrimitives.ReadUInt32BigEndian(_nextIn.Span.Slice((int)hdrSize, 4));
            bool isLast    = (jxlpIndex & 0x80000000u) != 0;
            uint seqIndex  = jxlpIndex & 0x7FFFFFFFu;

            if (_seenJxlp && seqIndex <= _lastCodestreamIndex)
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            _seenJxlp              = true;
            _lastCodestreamIndex   = seqIndex;
            _noMoreCodestreamInput = isLast;
            AdvanceFileInput(hdrSize + 4u);
            _inCodestreamBox       = true;
            _codestreamBoxRem      = _boxUnbounded ? 0u : totalSize - hdrSize - 4u;
            status                 = JxlDecoderStatus.Success;

            return BoxLoopResult.Continue;
        }

        private BoxLoopResult HandleJbrdBoxPayload(bool isBrob, out JxlDecoderStatus status)
        {
            if (isBrob)
            {
                _stage = DecoderStage.Error;
                status = JxlDecoderStatus.Error;

                return BoxLoopResult.ReturnStatus;
            }

            _seenJbrd = true;
            _jpegDecoder.StartBox(_boxUnbounded, _boxContentsSize);

            nuint prevAvail = _availIn;
            status          = _jpegDecoder.Process(ref _nextIn, ref _availIn);
            _filePos       += prevAvail - _availIn;

            if (status == JxlDecoderStatus.JpegReconstruction)
            {
                status = OnJbrdBoxDecoded();
            }

            if (status != JxlDecoderStatus.Success)
            {
                return BoxLoopResult.ReturnStatus;
            }

            return BoxLoopResult.Continue;
        }

        private static bool IsBoxType(JxlBoxType boxType, string fourcc)
        {
            return boxType == new JxlBoxType(fourcc);
        }

        private void AdvanceFileInput(nuint bytes)
        {
            _nextIn   = _nextIn.Slice((int)bytes);
            _availIn -= bytes;
            _filePos += bytes;
        }

        private JxlDecoderStatus ProcessCodestreamSlice(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            bool isLastSlice)
        {
            if (_stage == DecoderStage.CodestreamFinished)
            {
                return JxlDecoderStatus.Success;
            }

            if (_codestreamCopyLen > 0 || (_inCodestreamBox && !_seenJxlc))
            {
                AppendToCodestreamCopy(ref nextIn, ref availIn);

                ReadOnlyMemory<byte> copyMem = _codestreamCopy.AsMemory(0, _codestreamCopyLen);
                nuint avail                  = (nuint)_codestreamCopyLen;
                JxlDecoderStatus status      = ProcessContiguousCodestream(ref copyMem, ref avail, isLastSlice);
                int consumed                 = _codestreamCopyLen - (int)avail;

                if (consumed > 0)
                {
                    Buffer.BlockCopy(_codestreamCopy, consumed, _codestreamCopy, 0, _codestreamCopyLen - consumed);
                    _codestreamCopyLen -= consumed;
                }

                return status;
            }

            JxlDecoderStatus directStatus = ProcessContiguousCodestream(ref nextIn, ref availIn, isLastSlice);

            if (directStatus == JxlDecoderStatus.NeedMoreInput && availIn > 0 && !_haveContainer)
            {
                _bleed = availIn;
            }
            else if (directStatus == JxlDecoderStatus.NeedMoreInput && availIn > 0 && _haveContainer)
            {
                AppendToCodestreamCopy(ref nextIn, ref availIn);
            }

            return directStatus;
        }

        private void AppendToCodestreamCopy(ref ReadOnlyMemory<byte> nextIn, ref nuint availIn)
        {
            if (availIn == 0 || nextIn.IsEmpty)
            {
                return;
            }

            int add = (int)availIn;

            if (_codestreamCopyLen + add > _codestreamCopy.Length)
            {
                int newCap = Math.Max(_codestreamCopy.Length * 2, _codestreamCopyLen + add + 4096);
                Array.Resize(ref _codestreamCopy, newCap);
            }

            nextIn.Span.Slice(0, add).CopyTo(_codestreamCopy.AsSpan(_codestreamCopyLen, add));
            _codestreamCopyLen += add;
            nextIn              = nextIn.Slice(add);
            availIn             = 0;
        }

        private JxlDecoderStatus ProcessContiguousCodestream(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            bool isLastSlice)
        {
            if (!_gotCodestreamSignature)
            {
                JxlDecoderStatus csSigStatus = ReadCodestreamSignature(ref nextIn, ref availIn);

                if (csSigStatus != JxlDecoderStatus.Success)
                {
                    return csSigStatus;
                }
            }

            if (!_gotBasicInfo)
            {
                JxlDecoderStatus biStatus = ReadBasicInfoHeader(ref nextIn, ref availIn);

                if (biStatus != JxlDecoderStatus.Success)
                {
                    return biStatus;
                }
            }

            if (!_gotAllHeaders)
            {
                JxlDecoderStatus allHdrStatus = ReadRemainingCodestreamHeaders(ref nextIn, ref availIn);

                if (allHdrStatus != JxlDecoderStatus.Success)
                {
                    return allHdrStatus;
                }
            }

            return ProcessFramesLoop(ref nextIn, ref availIn, isLastSlice);
        }

        private JxlDecoderStatus ReadCodestreamSignature(ref ReadOnlyMemory<byte> nextIn, ref nuint availIn)
        {
            if (availIn < 2)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            ReadOnlySpan<byte> inSpan = nextIn.Span;

            if (inSpan[0] != 0xFF || inSpan[1] != Headers.KCodestreamMarker)
            {
                _stage = DecoderStage.Error;

                return JxlDecoderStatus.Error;
            }

            nextIn                  = nextIn.Slice(2);
            availIn                -= 2;
            _codestreamPos         += 2;
            _gotCodestreamSignature = true;

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus ReadBasicInfoHeader(ref ReadOnlyMemory<byte> nextIn, ref nuint availIn)
        {
            if (availIn == 0)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            BitReader br = new BitReader(nextIn.Slice(0, (int)availIn));

            try
            {
                if (!Headers.ReadSizeHeader(br, _metadata.Size).IsOk)
                {
                    return HandleHeaderBitReaderFailure(br, availIn);
                }

                if (!ImageMetadataMethods.ReadImageMetadata(br, _metadata.M).IsOk)
                {
                    return HandleHeaderBitReaderFailure(br, availIn);
                }

                if (!br.AllReadsWithinBounds())
                {
                    _basicInfoSizeHint = availIn * 2u;

                    return JxlDecoderStatus.NeedMoreInput;
                }

                _codestreamBitsAhead = br.TotalBitsConsumed();
                _gotBasicInfo        = true;
                _basicInfoSizeHint   = 0;

                if (!CheckSizeConstraints((uint)_metadata.Size.XSize(), (uint)_metadata.Size.YSize()))
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }

                if (SubscribedTo(JxlDecoderStatus.BasicInfo))
                {
                    _eventsProcessed |= (int)JxlDecoderStatus.BasicInfo;

                    return JxlDecoderStatus.BasicInfo;
                }

                return JxlDecoderStatus.Success;
            }
            finally
            {
                br.Close();
            }
        }

        private JxlDecoderStatus ReadRemainingCodestreamHeaders(ref ReadOnlyMemory<byte> nextIn, ref nuint availIn)
        {
            if (availIn * 8u <= _codestreamBitsAhead && availIn == 0)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            BitReader br = new BitReader(nextIn.Slice(0, (int)availIn));

            try
            {
                br.SkipBits(_codestreamBitsAhead);

                _metadata.TransformData.NonserializedXybEncoded = _metadata.M.XybEncoded;

                if (!Bundle.Read(br, _metadata.TransformData).IsOk)
                {
                    return HandleHeaderBitReaderFailure(br, availIn);
                }

                if (_metadata.M.ColorEncoding.WantICC())
                {
                    PaddedBytes iccPadded = new PaddedBytes(_memoryManager);
                    Status iccInit        = _iccReader.Init(br);

                    if (!iccInit.IsOk)
                    {
                        return HandleHeaderBitReaderFailure(br, availIn);
                    }

                    Status iccProc = _iccReader.Process(br, iccPadded);

                    if (!iccProc.IsOk)
                    {
                        return HandleHeaderBitReaderFailure(br, availIn);
                    }

                    byte[] iccBytes = iccPadded.Span.ToArray();

                    if (iccBytes.Length == 0)
                    {
                        _stage = DecoderStage.Error;

                        return JxlDecoderStatus.Error;
                    }

                    _metadata.M.ColorEncoding.SetICCRaw(iccBytes);
                }

                if (!br.JumpToByteBoundary() || !br.AllReadsWithinBounds())
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                nuint consumedBytes  = br.TotalBitsConsumed() / 8u;
                nextIn               = nextIn.Slice((int)consumedBytes);
                availIn             -= consumedBytes;
                _codestreamPos      += consumedBytes;
                _codestreamBitsAhead = 0;

                return FinalizeAllHeaders();
            }
            finally
            {
                br.Close();
            }
        }

        private JxlDecoderStatus FinalizeAllHeaders()
        {
            if (_passesState == null || !_passesState.OutputEncodingInfo.SetFromMetadata(_metadata).IsOk)
            {
                _stage = DecoderStage.Error;

                return JxlDecoderStatus.Error;
            }

            if (_desiredIntensityTarget > 0.0f)
            {
                _passesState.OutputEncodingInfo.DesiredIntensityTarget = _desiredIntensityTarget;
            }

            _gotAllHeaders  = true;
            _isFramePreview = _metadata.M.HavePreview;

            if (SubscribedTo(JxlDecoderStatus.ColorEncoding))
            {
                _eventsProcessed |= (int)JxlDecoderStatus.ColorEncoding;

                return JxlDecoderStatus.ColorEncoding;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus HandleHeaderBitReaderFailure(BitReader br, nuint availIn)
        {
            if (!br.AllReadsWithinBounds())
            {
                _basicInfoSizeHint = Math.Max(availIn * 2u, 64u);

                return JxlDecoderStatus.NeedMoreInput;
            }

            _stage = DecoderStage.Error;

            return JxlDecoderStatus.Error;
        }

        private static bool CheckSizeConstraints(uint xsize, uint ysize)
        {
            return xsize > 0 && ysize > 0 && xsize <= (1u << 30) && ysize <= (1u << 30);
        }

        private JxlDecoderStatus ProcessFramesLoop(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            bool isLastSlice)
        {
            while (true)
            {
                if (_isLastTotal)
                {
                    return HandlePostFramesCompletion();
                }

                JxlDecoderStatus frameStatus = ProcessSingleFrameStep(ref nextIn, ref availIn, isLastSlice);

                if (frameStatus != JxlDecoderStatus.Success)
                {
                    return frameStatus;
                }
            }
        }

        private JxlDecoderStatus HandlePostFramesCompletion()
        {
            if (_seenJbrd && SubscribedTo(JxlDecoderStatus.JpegReconstruction) && _ib?.JpegData != null)
            {
                if (JbrdNeedMoreBoxes())
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                return FlushJpegReconstructionOutput();
            }

            if (!_haveContainer)
            {
                _stage = DecoderStage.CodestreamFinished;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus ProcessSingleFrameStep(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            bool isLastSlice)
        {
            if (_frameStage == FrameStage.Header)
            {
                JxlDecoderStatus hdrStatus = StartAndReadFrameHeader(ref nextIn, ref availIn);

                if (hdrStatus != JxlDecoderStatus.Success)
                {
                    return hdrStatus;
                }
            }

            if (_frameStage == FrameStage.TOC)
            {
                JxlDecoderStatus tocStatus = HandleFrameTocStage();

                if (tocStatus != JxlDecoderStatus.Success)
                {
                    return tocStatus;
                }
            }

            if (_frameStage == FrameStage.Full)
            {
                return ProcessFrameSectionsStage(ref nextIn, ref availIn, isLastSlice);
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus StartAndReadFrameHeader(ref ReadOnlyMemory<byte> nextIn, ref nuint availIn)
        {
            if (availIn == 0)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            _ib ??= new ImageBundle(_memoryManager, _metadata.M);

            if (_seenJbrd && SubscribedTo(JxlDecoderStatus.JpegReconstruction) && !_isFramePreview)
            {
                if (!_jpegDecoder.SetImageBundleJpegData(_ib))
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }
            }

            _frameDecoder?.Dispose();
            _frameDecoder = new FrameDecoder(_passesState!, _metadata, _threadPool, useSlowRenderingPipeline: false);
            _frameDecoder.SetCoalescing(_coalescing);
            _frameDecoder.SetRenderSpotcolors(_renderSpotcolors);

            BitReader br = new BitReader(nextIn.Slice(0, (int)availIn));

            try
            {
                Status initStatus = _frameDecoder.InitFrame(br, _ib, _isFramePreview);

                if (!initStatus.IsOk)
                {
                    if (!br.AllReadsWithinBounds())
                    {
                        return JxlDecoderStatus.NeedMoreInput;
                    }

                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }

                if (!br.JumpToByteBoundary() || !br.AllReadsWithinBounds())
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                nuint consumed  = br.TotalBitsConsumed() / 8u;
                nextIn          = nextIn.Slice((int)consumed);
                availIn        -= consumed;
                _codestreamPos += consumed;
                _frameStart     = _codestreamPos;

                if (!_frameDecoder.InitFrameOutput().IsOk)
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }

                JxlProgressiveDetail effectiveDetail = (!_isFramePreview && SubscribedTo(JxlDecoderStatus.FrameProgression))
                    ? _progDetail
                    : JxlProgressiveDetail.Frames;

                _frameProgDetail = _frameDecoder.SetPauseAtProgressive(effectiveDetail);
                _LastDecodedPass = -1;
                _frameStage      = FrameStage.TOC;

                return CheckJpegReconstructionEventAfterFrameInit();
            }
            finally
            {
                br.Close();
            }
        }

        private JxlDecoderStatus CheckJpegReconstructionEventAfterFrameInit()
        {
            if (_seenJbrd && SubscribedTo(JxlDecoderStatus.JpegReconstruction) && !_isFramePreview &&
                (_eventsProcessed & (int)JxlDecoderStatus.JpegReconstruction) == 0)
            {
                _eventsProcessed |= (int)JxlDecoderStatus.JpegReconstruction;

                return JxlDecoderStatus.JpegReconstruction;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus HandleFrameTocStage()
        {
            FrameHeader fh = _frameDecoder!.GetFrameHeader();

            if (_isFramePreview)
            {
                _frameStage = FrameStage.Full;

                if (!SubscribedTo(JxlDecoderStatus.PreviewImage))
                {
                    _skippingFrame = true;
                }

                return JxlDecoderStatus.Success;
            }

            bool isVisible = (fh.IsLast || fh.AnimationFrame.Duration > 0) &&
                             (fh.FrameType == FrameType.RegularFrame || fh.FrameType == FrameType.SkipProgressive);

            if (_skipFrames > 0)
            {
                if (isVisible)
                {
                    _skipFrames--;
                }

                if (FrameDecoder.SavedAs(fh) == 0)
                {
                    _skippingFrame = true;
                    _frameStage    = FrameStage.Full;

                    return JxlDecoderStatus.Success;
                }
            }

            _frameStage = FrameStage.Full;

            if ((isVisible || !_coalescing) && SubscribedTo(JxlDecoderStatus.Frame))
            {
                return JxlDecoderStatus.Frame;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus ProcessFrameSectionsStage(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            bool isLastSlice)
        {
            nuint totalSize      = _frameDecoder!.TotalSize();
            bool needFrameDecode = (_isFramePreview && SubscribedTo(JxlDecoderStatus.PreviewImage)) ||
                                   SubscribedTo(JxlDecoderStatus.FullImage) ||
                                   SubscribedTo(JxlDecoderStatus.FrameProgression) ||
                                   (SubscribedTo(JxlDecoderStatus.JpegReconstruction) && _seenJbrd);

            if (_skippingFrame || !needFrameDecode)
            {
                return SkipRemainingFrameBytes(ref nextIn, ref availIn, totalSize);
            }

            JxlDecoderStatus outCheck = PrepareFrameOutputTargetIfNeeded();

            if (outCheck != JxlDecoderStatus.Success)
            {
                return outCheck;
            }

            if (availIn < totalSize && !isLastSlice && _frameProgDetail == JxlProgressiveDetail.Frames)
            {
                return JxlDecoderStatus.NeedMoreInput;
            }

            return ExecuteAvailableFrameSections(ref nextIn, ref availIn, totalSize);
        }

        private JxlDecoderStatus SkipRemainingFrameBytes(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            nuint totalSize)
        {
            nuint alreadySkipped = _codestreamPos - _frameStart;
            nuint remaining      = totalSize > alreadySkipped ? totalSize - alreadySkipped : 0u;

            if (availIn < remaining)
            {
                nextIn          = nextIn.Slice((int)availIn);
                _codestreamPos += availIn;
                availIn         = 0;

                return JxlDecoderStatus.NeedMoreInput;
            }

            nextIn          = nextIn.Slice((int)remaining);
            availIn        -= remaining;
            _codestreamPos += remaining;

            AdvanceToNextFrameAfterSkip();

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus PrepareFrameOutputTargetIfNeeded()
        {
            FrameHeader fh = _frameDecoder!.GetFrameHeader();
            bool isVisible = !_isFramePreview &&
                             ((fh.IsLast || fh.AnimationFrame.Duration > 0) &&
                              (fh.FrameType == FrameType.RegularFrame || fh.FrameType == FrameType.SkipProgressive) ||
                              !_coalescing);

            if (_isFramePreview && SubscribedTo(JxlDecoderStatus.PreviewImage))
            {
                if (!_hasPreviewOutBuffer)
                {
                    return JxlDecoderStatus.NeedPreviewOutBuffer;
                }

                Status setPrevStatus = ConfigureFrameDecoderOutput(_previewOutBuffer, _previewOutBufferSize);

                if (!setPrevStatus.IsOk)
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }
            }
            else if (isVisible && SubscribedTo(JxlDecoderStatus.FullImage) && !_ib!.IsJPEG())
            {
                if (!_hasImageOutBuffer && !_pixelCallback.IsPresent())
                {
                    return JxlDecoderStatus.NeedImageOutBuffer;
                }

                Status setImgStatus = ConfigureFrameDecoderOutput(_imageOutBuffer, _imageOutBufferSize);

                if (!setImgStatus.IsOk)
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }
            }

            return JxlDecoderStatus.Success;
        }

        private Status ConfigureFrameDecoderOutput(Memory<byte> buffer, nuint bufferSize)
        {
            FrameHeader fh  = _frameDecoder!.GetFrameHeader();
            nuint xsize     = _isFramePreview ? _metadata.M.PreviewSize.XSize() : fh.DefaultXSize();
            nuint ysize     = _isFramePreview ? _metadata.M.PreviewSize.YSize() : fh.DefaultYSize();
            nuint bitDepth  = ResolveOutputBitsPerSample(_imageOutFormat);
            bool undoOrient = !_keepOrientation;

            Status status = _frameDecoder.SetImageOutput(
                _pixelCallback,
                buffer,
                bufferSize,
                (xsize, ysize),
                _imageOutFormat,
                bitDepth,
                (_unpremulAlpha, undoOrient));

            if (!status.IsOk)
            {
                return status;
            }

            nuint orientedXSize = (!undoOrient || _metadata.M.Orientation <= 4) ? xsize : ysize;

            for (int i = 0; i < _metadata.M.ExtraChannelInfo.Count; i++)
            {
                if (i < _extraChannelOutBuffers.Count && _extraChannelOutHasBuffers[i])
                {
                    JxlPixelFormat ecFmt = _extraChannelOutFormats[i];
                    nuint ecBitDepth     = ResolveExtraChannelOutputBitsPerSample(ecFmt, i);
                    Status ecStatus      = _frameDecoder.AddExtraChannelOutput(
                        _extraChannelOutBuffers[i],
                        _extraChannelOutSizes[i],
                        orientedXSize,
                        ecFmt,
                        ecBitDepth);

                    if (!ecStatus.IsOk)
                    {
                        return ecStatus;
                    }
                }
                else if (_extraChannelOutBuffers.Count > 0)
                {
                    JxlPixelFormat dummyFmt = new JxlPixelFormat { NumChannels = 1, DataType = JxlDataType.Float };

                    _frameDecoder.AddExtraChannelOutput(Memory<byte>.Empty, 0, orientedXSize, dummyFmt, 32);
                }
            }

            return true;
        }

        private nuint ResolveOutputBitsPerSample(JxlPixelFormat format)
        {
            if (_imageOutBitDepth.Type == JxlBitDepthType.Custom)
            {
                return _imageOutBitDepth.BitsPerSample;
            }

            if (_imageOutBitDepth.Type == JxlBitDepthType.FromCodestream)
            {
                return _metadata.M.BitDepth.BitsPerSample;
            }

            return format.DataType switch
            {
                JxlDataType.UInt8   => 8u,
                JxlDataType.UInt16  => 16u,
                JxlDataType.Float16 => 16u,
                _                   => 32u,
            };
        }

        private nuint ResolveExtraChannelOutputBitsPerSample(JxlPixelFormat format, int ecIndex)
        {
            if (_imageOutBitDepth.Type == JxlBitDepthType.Custom)
            {
                return _imageOutBitDepth.BitsPerSample;
            }

            if (_imageOutBitDepth.Type == JxlBitDepthType.FromCodestream)
            {
                return _metadata.M.ExtraChannelInfo[ecIndex].BitDepth.BitsPerSample;
            }

            return format.DataType switch
            {
                JxlDataType.UInt8   => 8u,
                JxlDataType.UInt16  => 16u,
                JxlDataType.Float16 => 16u,
                _                   => 32u,
            };
        }

        private JxlDecoderStatus ExecuteAvailableFrameSections(
            ref ReadOnlyMemory<byte> nextIn,
            ref nuint availIn,
            nuint totalSize)
        {
            _ = totalSize;

            ReadOnlySpan<TocEntry> toc = _frameDecoder!.Toc;
            List<SectionInfo> sections = new List<SectionInfo>(toc.Length);
            nuint alreadyConsumed      = _codestreamPos - _frameStart;

            try
            {
                for (int i = 0; i < toc.Length; i++)
                {
                    if (toc[i].Offset < alreadyConsumed)
                    {
                        continue;
                    }

                    nuint relOffset = toc[i].Offset - alreadyConsumed;

                    if (relOffset + toc[i].Size > availIn)
                    {
                        break;
                    }

                    sections.Add(new SectionInfo
                    {
                        Br    = new BitReader(nextIn.Slice((int)relOffset, (int)toc[i].Size)),
                        Id    = toc[i].Id,
                        Index = (nuint)i,
                    });
                }

                if (sections.Count == 0)
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                SectionInfo[] secArray      = sections.ToArray();
                SectionStatus[] statusArray = new SectionStatus[secArray.Length];
                bool hadDcBefore            = _frameDecoder.HasDecodedDC();
                nuint nextPassesToPause     = _frameDecoder.NextNumPassesToPause();

                Status procStatus = _frameDecoder.ProcessSections(secArray, secArray.Length, statusArray);

                if (!procStatus.IsOk)
                {
                    _stage = DecoderStage.Error;

                    return JxlDecoderStatus.Error;
                }

                nuint newOffset = _frameDecoder.NextFrameOffset();
                nuint advance   = newOffset > alreadyConsumed ? newOffset - alreadyConsumed : 0u;
                nextIn          = nextIn.Slice((int)advance);
                availIn        -= advance;
                _codestreamPos += advance;

                if (!_frameDecoder.HasDecodedAll())
                {
                    return HandlePartialFrameProgress(hadDcBefore, nextPassesToPause);
                }

                return CompleteCurrentFrame();
            }
            finally
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    sections[i].Br?.Close();
                }
            }
        }

        private JxlDecoderStatus HandlePartialFrameProgress(bool hadDcBefore, nuint nextPassesToPause)
        {
            if (SubscribedTo(JxlDecoderStatus.FrameProgression))
            {
                bool dcJustDone  = !hadDcBefore && _frameDecoder!.HasDecodedDC() && _frameProgDetail >= JxlProgressiveDetail.DC;
                uint curPasses   = _frameDecoder!.NumCompletePasses();
                bool passStepped = curPasses >= nextPassesToPause && (int)curPasses > _LastDecodedPass &&
                                   _frameProgDetail >= JxlProgressiveDetail.LastPasses;

                if (dcJustDone || passStepped)
                {
                    if (passStepped)
                    {
                        _LastDecodedPass = (int)curPasses;
                    }

                    return JxlDecoderStatus.FrameProgression;
                }
            }

            return JxlDecoderStatus.NeedMoreInput;
        }

        private JxlDecoderStatus CompleteCurrentFrame()
        {
            if (!_frameDecoder!.FinalizeFrame().IsOk)
            {
                _stage = DecoderStage.Error;

                return JxlDecoderStatus.Error;
            }

            if (_isFramePreview)
            {
                _isFramePreview       = false;
                _previewOutBuffer     = Memory<byte>.Empty;
                _hasPreviewOutBuffer  = false;
                _previewOutBufferSize = 0;
                _frameStage           = FrameStage.Header;

                if (SubscribedTo(JxlDecoderStatus.PreviewImage))
                {
                    return JxlDecoderStatus.PreviewImage;
                }

                return JxlDecoderStatus.Success;
            }

            FrameHeader fh = _frameDecoder.GetFrameHeader();
            bool isVisible = ((fh.IsLast || fh.AnimationFrame.Duration > 0) &&
                              (fh.FrameType == FrameType.RegularFrame || fh.FrameType == FrameType.SkipProgressive)) ||
                             !_coalescing;

            if (fh.IsLast)
            {
                _isLastTotal = true;
            }

            ClearCompletedFrameOutputBuffers();
            _frameStage = FrameStage.Header;

            if (_jpegDecoder.IsOutputSet && _ib?.JpegData != null)
            {
                _reconOutputJpeg = true;

                if (JbrdNeedMoreBoxes())
                {
                    return JxlDecoderStatus.NeedMoreInput;
                }

                return FlushJpegReconstructionOutput();
            }

            if (isVisible && SubscribedTo(JxlDecoderStatus.FullImage))
            {
                return JxlDecoderStatus.FullImage;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus FlushJpegReconstructionOutput()
        {
            if (_ib?.JpegData == null)
            {
                _reconOutputJpeg = false;

                return JxlDecoderStatus.Error;
            }

            if (!_reconMetadataSet)
            {
                JxlDecoderStatus metaStatus = ApplyReconMetadataToJpegData(_ib.JpegData);

                if (metaStatus != JxlDecoderStatus.Success)
                {
                    _stage = DecoderStage.Error;

                    return metaStatus;
                }

                _reconMetadataSet = true;
            }

            JxlDecoderStatus jpegStatus = _jpegDecoder.WriteOutput(_ib.JpegData);

            if (jpegStatus != JxlDecoderStatus.Success)
            {
                return jpegStatus;
            }

            _reconOutputJpeg = false;
            _seenJbrd        = false;
            _ib.Dispose();
            _ib              = null;

            if (SubscribedTo(JxlDecoderStatus.FullImage))
            {
                return JxlDecoderStatus.FullImage;
            }

            return JxlDecoderStatus.Success;
        }

        private JxlDecoderStatus ApplyReconMetadataToJpegData(JPEGData jpegData)
        {
            if (_reconExifSize > 0)
            {
                JxlDecoderStatus status = JxlToJpegDecoder.SetExif(_exifMetadata, jpegData);

                if (status != JxlDecoderStatus.Success)
                {
                    return status;
                }
            }

            if (_reconXmpSize > 0)
            {
                JxlDecoderStatus status = JxlToJpegDecoder.SetXmp(_xmpMetadata, jpegData);

                if (status != JxlDecoderStatus.Success)
                {
                    return status;
                }
            }

            return JxlDecoderStatus.Success;
        }

        private void ClearCompletedFrameOutputBuffers()
        {
            _imageOutBuffer         = Memory<byte>.Empty;
            _hasImageOutBuffer      = false;
            _imageOutBufferSize     = 0;
            _pixelCallback          = default;
            _simpleImageOutCallback = null;
            _simpleImageOutOpaque   = null;
            _extraChannelOutBuffers.Clear();
            _extraChannelOutHasBuffers.Clear();
            _extraChannelOutSizes.Clear();
            _extraChannelOutFormats.Clear();
        }

        private void ResetFrameBuffersAndFinalizeSkip()
        {
            _skippingFrame = true;
            ClearCompletedFrameOutputBuffers();
        }

        private void AdvanceToNextFrameAfterSkip()
        {
            _skippingFrame = false;

            if (_isFramePreview)
            {
                _isFramePreview = false;
            }
            else if (_frameDecoder != null && _frameDecoder.GetFrameHeader().IsLast)
            {
                _isLastTotal = true;
            }

            _frameStage = FrameStage.Header;
        }

        private bool SubscribedTo(JxlDecoderStatus evt)
        {
            return (_eventsSubscribed & (int)evt) != 0;
        }

        private ColorEncoding GetTargetColorEncoding(JxlColorProfileTarget target)
        {
            if (target == JxlColorProfileTarget.Data && _passesState != null)
            {
                return _passesState.OutputEncodingInfo.ColorEncoding;
            }

            return _metadata.M.ColorEncoding;
        }

        private void GetCurrentFrameOrientedDimensions(out nuint xsize, out nuint ysize)
        {
            if (_frameDecoder != null && _frameStage != FrameStage.Header)
            {
                FrameHeader fh = _frameDecoder.GetFrameHeader();
                xsize          = !_coalescing && fh.CustomSizeOrOrigin ? fh.FrameSize.XSize : fh.DefaultXSize();
                ysize          = !_coalescing && fh.CustomSizeOrOrigin ? fh.FrameSize.YSize : fh.DefaultYSize();
            }
            else
            {
                xsize = _metadata.Size.XSize();
                ysize = _metadata.Size.YSize();
            }

            if (!_keepOrientation && _metadata.M.Orientation > 4)
            {
                (xsize, ysize) = (ysize, xsize);
            }
        }

        private static JxlDecoderStatus ComputeMinBufferSize(
            nuint xsize,
            nuint ysize,
            JxlPixelFormat format,
            out nuint size)
        {
            size = 0;

            StatusOr<nuint> strideOr = FrameDecoder.GetStride(xsize, format);

            if (!strideOr.IsOk)
            {
                return JxlDecoderStatus.Error;
            }

            if (ysize == 0)
            {
                return JxlDecoderStatus.Success;
            }

            nuint stride          = strideOr.Value;
            nuint bytesPerChannel = format.DataType switch
            {
                JxlDataType.Float   => 4u,
                JxlDataType.Float16 => 2u,
                JxlDataType.UInt8   => 1u,
                JxlDataType.UInt16  => 2u,
                _                   => 0u,
            };

            nuint lastRowSize = xsize * format.NumChannels * bytesPerChannel;

            if (ysize - 1u > nuint.MaxValue / stride)
            {
                return JxlDecoderStatus.Error;
            }

            nuint prevRows = (ysize - 1u) * stride;

            if (prevRows > nuint.MaxValue - lastRowSize)
            {
                return JxlDecoderStatus.Error;
            }

            size = prevRows + lastRowSize;

            return JxlDecoderStatus.Success;
        }

        private void EnsureExtraChannelSlots(int count)
        {
            while (_extraChannelOutBuffers.Count < count)
            {
                _extraChannelOutBuffers.Add(Memory<byte>.Empty);
                _extraChannelOutHasBuffers.Add(false);
                _extraChannelOutSizes.Add(0);
                _extraChannelOutFormats.Add(default);
            }
        }

        private static void PopulateBasicInfoAlphaFields(ImageMetadata meta, ref JxlBasicInfo info)
        {
            ExtraChannelInfo? alpha = meta.Find(ExtraChannel.Alpha);

            if (alpha != null)
            {
                info.AlphaBits          = alpha.BitDepth.BitsPerSample;
                info.AlphaExponentBits  = alpha.BitDepth.FloatingPointSample ? alpha.BitDepth.ExponentBitsPerSample : 0u;
                info.AlphaPremultiplied = alpha.AlphaAssociated;
            }
            else
            {
                info.AlphaBits          = 0;
                info.AlphaExponentBits  = 0;
                info.AlphaPremultiplied = false;
            }
        }

        private static void PopulateBasicInfoPreviewAndAnimation(ImageMetadata meta, ref JxlBasicInfo info)
        {
            if (meta.HavePreview)
            {
                info.Preview = new JxlPreviewHeader
                {
                    XSize = (uint)meta.PreviewSize.XSize(),
                    YSize = (uint)meta.PreviewSize.YSize(),
                };
            }

            if (meta.HaveAnimation)
            {
                info.Animation = new JxlAnimationHeader
                {
                    TpsNumerator   = meta.Animation.TpsNumerator,
                    TpsDenominator = meta.Animation.TpsDenominator,
                    NumLoops       = meta.Animation.NumLoops,
                    HaveTimecodes  = meta.Animation.HaveTimecodes,
                };
            }
        }

        private void PopulateBasicInfoIntrinsicSize(
            ImageMetadata meta,
            in JxlBasicInfo current,
            ref JxlBasicInfo info)
        {
            if (meta.HaveIntrinsicSize)
            {
                info.IntrinsicXSize = (uint)meta.IntrinsicSize.XSize();
                info.IntrinsicYSize = (uint)meta.IntrinsicSize.YSize();

                if (!_keepOrientation && meta.Orientation > 4)
                {
                    (info.IntrinsicXSize, info.IntrinsicYSize) = (info.IntrinsicYSize, info.IntrinsicXSize);
                }
            }
            else
            {
                info.IntrinsicXSize = current.XSize;
                info.IntrinsicYSize = current.YSize;
            }
        }

        private void OrientLayerDimensions(
            ref uint xsize,
            ref uint ysize,
            ref int x0,
            ref int y0,
            bool crop)
        {
            Orientation orient = _metadata.M.GetOrientation();

            if ((int)orient > 4)
            {
                (xsize, ysize) = (ysize, xsize);
            }

            if (!crop)
            {
                return;
            }

            int origW = (int)_metadata.Size.XSize();
            int origH = (int)_metadata.Size.YSize();

            if ((int)orient > 4)
            {
                (origW, origH) = (origH, origW);
                (x0, y0)       = (y0, x0);
            }

            if (orient == Orientation.FlipHorizontal ||
                orient == Orientation.Rotate180 ||
                orient == Orientation.Rotate270 ||
                orient == Orientation.AntiTranspose)
            {
                x0 = origW - (int)xsize - x0;
            }

            if (orient == Orientation.FlipVertical ||
                orient == Orientation.Rotate180 ||
                orient == Orientation.Rotate90 ||
                orient == Orientation.AntiTranspose)
            {
                y0 = origH - (int)ysize - y0;
            }
        }
    }
}
