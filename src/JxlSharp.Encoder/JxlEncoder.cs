// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using ThreadPool = JxlSharp.Base.ThreadPool;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Return value for <see cref="JxlEncoder"/> API calls.
    /// Ported from <c>JxlEncoderStatus</c> in <c>lib/include/jxl/encode.h</c>.
    /// </summary>
    public enum JxlEncoderStatus
    {
        /// <summary>Function call finished successfully, or encoding is finished.</summary>
        Success = 0,

        /// <summary>An error occurred, for example out of memory or invalid input.</summary>
        Error = 1,

        /// <summary>The encoder needs more output buffer space to continue encoding.</summary>
        NeedMoreOutput = 2,
    }

    /// <summary>
    /// Error conditions for <see cref="JxlEncoder.GetError"/>.
    /// Ported from <c>JxlEncoderError</c> in <c>lib/include/jxl/encode.h</c>.
    /// </summary>
    public enum JxlEncoderError
    {
        /// <summary>No error.</summary>
        OK = 0,

        /// <summary>Generic encoder error.</summary>
        Generic = 1,

        /// <summary>Out of memory error.</summary>
        OutOfMemory = 2,

        /// <summary>JPEG bitstream reconstruction data could not be represented.</summary>
        Jbrd = 3,

        /// <summary>Input is invalid (corrupted data or invalid dimensions/options).</summary>
        BadInput = 4,

        /// <summary>The encoder doesn't support this feature yet.</summary>
        NotSupported = 0x80,

        /// <summary>The encoder API is used in an incorrect way.</summary>
        ApiUsage = 0x81,
    }

    /// <summary>
    /// Ids of encoder options for a frame.
    /// Ported from <c>JxlEncoderFrameSettingId</c> in <c>lib/include/jxl/encode.h</c>.
    /// </summary>
    public enum JxlEncoderFrameSettingId
    {
        /// <summary>Encoder effort/speed level (<c>1..11</c>, default 7).</summary>
        Effort = 0,

        /// <summary>Sets the decoding speed tier for the encoded image (<c>0..4</c>, default 0).</summary>
        DecodingSpeed = 1,

        /// <summary>Sets resampling option (<c>1, 2, 4, 8</c>).</summary>
        Resampling = 2,

        /// <summary>Sets resampling option for extra channels (<c>1, 2, 4, 8</c>).</summary>
        ExtraChannelResampling = 3,

        /// <summary>Indicates the input frame is already downsampled.</summary>
        AlreadyDownsampled = 4,

        /// <summary>Adds simulated photon noise to the image.</summary>
        PhotonNoise = 5,

        /// <summary>Enables adaptive noise generation (<c>-1, 0, 1</c>).</summary>
        Noise = 6,

        /// <summary>Enables or disables dots generation (<c>-1, 0, 1</c>).</summary>
        Dots = 7,

        /// <summary>Enables or disables patches generation (<c>-1, 0, 1</c>).</summary>
        Patches = 8,

        /// <summary>Edge preserving filter level (<c>-1..3</c>).</summary>
        Epf = 9,

        /// <summary>Gaborish filter option (<c>-1, 0, 1</c>).</summary>
        Gaborish = 10,

        /// <summary>Enables modular encoding (<c>-1, 0, 1</c>).</summary>
        Modular = 11,

        /// <summary>Enables or disables preserving color of invisible pixels (<c>-1, 0, 1</c>).</summary>
        KeepInvisible = 12,

        /// <summary>Determines the order in which 256x256 regions are stored (<c>-1, 0, 1</c>).</summary>
        GroupOrder = 13,

        /// <summary>Horizontal position of center for center-first group order.</summary>
        GroupOrderCenterX = 14,

        /// <summary>Vertical position of center for center-first group order.</summary>
        GroupOrderCenterY = 15,

        /// <summary>Enables or disables progressive encoding for modular mode (<c>-1, 0, 1</c>).</summary>
        Responsive = 16,

        /// <summary>Sets the progressive mode for AC (<c>-1, 0, 1</c>).</summary>
        ProgressiveAc = 17,

        /// <summary>Sets the quantized progressive mode for AC (<c>-1, 0, 1</c>).</summary>
        QProgressiveAc = 18,

        /// <summary>Sets the progressive mode using lower-resolution DC images (<c>-1..2</c>).</summary>
        ProgressiveDc = 19,

        /// <summary>Use Global channel palette if the number of colors is smaller than this percentage.</summary>
        ChannelColorsGlobalPercent = 20,

        /// <summary>Use Local channel palette if the number of colors is smaller than this percentage.</summary>
        ChannelColorsGroupPercent = 21,

        /// <summary>Use color palette if the amount of colors is smaller than or equal to this amount.</summary>
        PaletteColors = 22,

        /// <summary>Enables or disables delta palette (<c>-1, 0, 1</c>).</summary>
        LossyPalette = 23,

        /// <summary>Color transform for internal encoding (<c>-1, 0=XYB, 1=None, 2=YCbCr</c>).</summary>
        ColorTransform = 24,

        /// <summary>Reversible color transform for modular encoding (<c>-1..41</c>).</summary>
        ModularColorSpace = 25,

        /// <summary>Group size for modular encoding (<c>-1, 0=128, 1=256, 2=512, 3=1024</c>).</summary>
        ModularGroupSize = 26,

        /// <summary>Predictor for modular encoding (<c>-1..15</c>).</summary>
        ModularPredictor = 27,

        /// <summary>Fraction of pixels used to learn MA trees as a percentage.</summary>
        ModularMaTreeLearningPercent = 28,

        /// <summary>Number of extra (previous-channel) MA tree properties to use.</summary>
        ModularNbPrevChannels = 29,

        /// <summary>Enable or disable CFL for lossless JPEG recompression (<c>-1, 0, 1</c>).</summary>
        JpegReconCfl = 30,

        /// <summary>Prepare the frame for indexing in the frame index box (<c>0, 1</c>).</summary>
        FrameIndexBox = 31,

        /// <summary>Sets brotli encode effort for boxes (<c>-1..11</c>).</summary>
        BrotliEffort = 32,

        /// <summary>Enables or disables brotli compression of metadata boxes derived from JPEG (<c>-1, 0, 1</c>).</summary>
        JpegCompressBoxes = 33,

        /// <summary>Control what kind of buffering is used (<c>-1..3</c>).</summary>
        Buffering = 34,

        /// <summary>Keep oromer discard Exif metadata boxes derived from a JPEG frame (<c>0, 1</c>).</summary>
        JpegKeepExif = 35,

        /// <summary>Keep or discard XMP metadata boxes derived from a JPEG frame (<c>0, 1</c>).</summary>
        JpegKeepXmp = 36,

        /// <summary>Keep or discard JUMBF metadata boxes derived from a JPEG frame (<c>0, 1</c>).</summary>
        JpegKeepJumbf = 37,

        /// <summary>If zero, disables all perceptual heuristics (<c>0, 1</c>).</summary>
        UseFullImageHeuristics = 38,

        /// <summary>Disable perceptual optimizations (<c>0, 1</c>).</summary>
        DisablePerceptualOptimizations = 39,

        /// <summary>Fill enum sentinel.</summary>
        FillEnum = 65535,
    }

    /// <summary>
    /// Keys for querying <see cref="JxlEncoderStats"/>.
    /// Ported from <c>JxlEncoderStatsKey</c> in <c>lib/include/jxl/stats.h</c>.
    /// </summary>
    public enum JxlEncoderStatsKey
    {
        /// <summary>Header bits.</summary>
        HeaderBits = 0,

        /// <summary>TOC bits.</summary>
        TocBits = 1,

        /// <summary>Dictionary bits.</summary>
        DictionaryBits = 2,

        /// <summary>Splines bits.</summary>
        SplinesBits = 3,

        /// <summary>Noise bits.</summary>
        NoiseBits = 4,

        /// <summary>Quant bits.</summary>
        QuantBits = 5,

        /// <summary>Modular tree bits.</summary>
        ModularTreeBits = 6,

        /// <summary>Modular global bits.</summary>
        ModularGlobalBits = 7,

        /// <summary>DC bits.</summary>
        DcBits = 8,

        /// <summary>Modular DC group bits.</summary>
        ModularDcGroupBits = 9,

        /// <summary>Control fields bits.</summary>
        ControlFieldsBits = 10,

        /// <summary>Coef order bits.</summary>
        CoefOrderBits = 11,

        /// <summary>AC histogram bits.</summary>
        AcHistogramBits = 12,

        /// <summary>AC bits.</summary>
        AcBits = 13,

        /// <summary>Modular AC group bits.</summary>
        ModularAcGroupBits = 14,

        /// <summary>Number of small blocks.</summary>
        NumSmallBlocks = 15,

        /// <summary>Number of DCT4x8 blocks.</summary>
        NumDct4x8Blocks = 16,

        /// <summary>Number of AFV blocks.</summary>
        NumAfvBlocks = 17,

        /// <summary>Number of DCT8 blocks.</summary>
        NumDct8Blocks = 18,

        /// <summary>Number of DCT8x32 blocks.</summary>
        NumDct8x32Blocks = 19,

        /// <summary>Number of DCT16 blocks.</summary>
        NumDct16Blocks = 20,

        /// <summary>Number of DCT16x32 blocks.</summary>
        NumDct16x32Blocks = 21,

        /// <summary>Number of DCT32 blocks.</summary>
        NumDct32Blocks = 22,

        /// <summary>Number of DCT32x64 blocks.</summary>
        NumDct32x64Blocks = 23,

        /// <summary>Number of DCT64 blocks.</summary>
        NumDct64Blocks = 24,

        /// <summary>Butteraugli distance.</summary>
        ButteraugliDistance = 25,

        /// <summary>Number of stats keys.</summary>
        NumStats = 26,
    }

    /// <summary>
    /// Collects encoder statistics across frames.
    /// Ported from <c>JxlEncoderStats</c> in <c>lib/include/jxl/stats.h</c> and <c>lib/jxl/encode.cc</c>.
    /// </summary>
    public sealed class JxlEncoderStats
    {
        private readonly nuint[] _values = new nuint[(int)JxlEncoderStatsKey.NumStats];

        /// <summary>Auxiliary statistics accumulator populated during frame encoding.</summary>
        public readonly AuxOut Stats = new AuxOut();

        /// <summary>Gets the value for <paramref name="key"/>.</summary>
        /// <param name="key">Statistics key.</param>
        /// <returns>Recorded statistic value.</returns>
        public nuint Get(JxlEncoderStatsKey key)
        {
            int idx = (int)key;

            return (uint)idx < (uint)_values.Length ? _values[idx] : 0;
        }

        /// <summary>Sets the value for <paramref name="key"/>.</summary>
        /// <param name="key">Statistics key.</param>
        /// <param name="value">Value to store.</param>
        public void Set(JxlEncoderStatsKey key, nuint value)
        {
            int idx = (int)key;

            if ((uint)idx < (uint)_values.Length)
            {
                _values[idx] = value;
            }
        }

        /// <summary>Merges statistics from <paramref name="other"/> into this instance.</summary>
        /// <param name="other">Other statistics object.</param>
        public void Merge(JxlEncoderStats other)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] += other._values[i];
            }

            Stats.Assimilate(other.Stats);
        }

        internal void SyncFromAuxOut()
        {
            _values[(int)JxlEncoderStatsKey.HeaderBits]         = Stats.Layer(LayerType.Header).TotalBits;
            _values[(int)JxlEncoderStatsKey.TocBits]            = Stats.Layer(LayerType.Toc).TotalBits;
            _values[(int)JxlEncoderStatsKey.DictionaryBits]     = Stats.Layer(LayerType.Dictionary).TotalBits;
            _values[(int)JxlEncoderStatsKey.SplinesBits]        = Stats.Layer(LayerType.Splines).TotalBits;
            _values[(int)JxlEncoderStatsKey.NoiseBits]          = Stats.Layer(LayerType.Noise).TotalBits;
            _values[(int)JxlEncoderStatsKey.QuantBits]          = Stats.Layer(LayerType.Quant).TotalBits;
            _values[(int)JxlEncoderStatsKey.ModularTreeBits]    = Stats.Layer(LayerType.ModularTree).TotalBits;
            _values[(int)JxlEncoderStatsKey.ModularGlobalBits]  = Stats.Layer(LayerType.ModularGlobal).TotalBits;
            _values[(int)JxlEncoderStatsKey.DcBits]             = Stats.Layer(LayerType.Dc).TotalBits;
            _values[(int)JxlEncoderStatsKey.ModularDcGroupBits] = Stats.Layer(LayerType.ModularDcGroup).TotalBits;
            _values[(int)JxlEncoderStatsKey.ControlFieldsBits]  = Stats.Layer(LayerType.ControlFields).TotalBits;
            _values[(int)JxlEncoderStatsKey.CoefOrderBits]      = Stats.Layer(LayerType.Order).TotalBits;
            _values[(int)JxlEncoderStatsKey.AcBits]             = Stats.Layer(LayerType.Ac).TotalBits;
            _values[(int)JxlEncoderStatsKey.ModularAcGroupBits] = Stats.Layer(LayerType.ModularAcGroup).TotalBits;
        }
    }

    /// <summary>
    /// Single entry in the JXL Frame Index Box (<c>jxli</c>).
    /// Ported from <c>jxl::JxlEncoderFrameIndexBoxEntry</c> in <c>lib/jxl/encode_internal.h</c>.
    /// </summary>
    public struct JxlEncoderFrameIndexBoxEntry
    {
        /// <summary>Whether this frame should be indexed in the <c>jxli</c> box.</summary>
        public bool ToBeIndexed;

        /// <summary>Frame duration in ticks.</summary>
        public uint Duration;

        /// <summary>Byte offset from the previous indexed frame in the codestream.</summary>
        public ulong Offi;
    }

    /// <summary>
    /// Builder for the JXL Frame Index Box (<c>jxli</c>).
    /// Ported from <c>jxl::JxlEncoderFrameIndexBox</c> in <c>lib/jxl/encode_internal.h</c>.
    /// </summary>
    public sealed class JxlEncoderFrameIndexBox
    {
        /// <summary>Numerator of tick duration.</summary>
        public uint TpsNumerator = 1;

        /// <summary>Denominator of tick duration.</summary>
        public uint TpsDenominator = 1;

        /// <summary>Recorded frame entries.</summary>
        public readonly List<JxlEncoderFrameIndexBoxEntry> Entries = new List<JxlEncoderFrameIndexBoxEntry>();

        /// <summary>Returns the number of indexed frames.</summary>
        public int NumIndexedFrames()
        {
            int count = 0;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].ToBeIndexed || i == 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Per-frame compression parameters and header overrides stored in <see cref="JxlEncoderFrameSettings"/>.
    /// Ported from <c>jxl::JxlEncoderFrameSettingsValues</c> in <c>lib/jxl/encode_internal.h</c>.
    /// </summary>
    public sealed class JxlEncoderFrameSettingsValues
    {
        /// <summary>Internal compression parameters.</summary>
        public CompressParams CParams = new CompressParams();

        /// <summary>Public frame header settings.</summary>
        public JxlFrameHeader Header = new JxlFrameHeader
        {
            LayerInfo = new JxlLayerInfo
            {
                BlendInfo = new JxlBlendInfo
                {
                    BlendMode = JxlBlendMode.Replace,
                },
            },
        };

        /// <summary>Per-extra-channel blend info overrides.</summary>
        public List<JxlBlendInfo> ExtraChannelBlendInfo = new List<JxlBlendInfo>();

        /// <summary>Frame name string.</summary>
        public string FrameName = string.Empty;

        /// <summary>Bit depth of the input pixel buffer.</summary>
        public JxlBitDepth ImageBitDepth = new JxlBitDepth { Type = JxlBitDepthType.FromPixelFormat };

        /// <summary>Whether the frame is lossless.</summary>
        public bool Lossless;

        /// <summary>Whether this frame should be indexed in the <c>jxli</c> box.</summary>
        public bool FrameIndexBox;

        /// <summary>Creates a deep copy of these frame settings values.</summary>
        public JxlEncoderFrameSettingsValues Clone()
        {
            return new JxlEncoderFrameSettingsValues
            {
                CParams               = CParams.Clone(),
                Header                = Header,
                ExtraChannelBlendInfo = new List<JxlBlendInfo>(ExtraChannelBlendInfo),
                FrameName             = FrameName,
                ImageBitDepth         = ImageBitDepth,
                Lossless              = Lossless,
                FrameIndexBox         = FrameIndexBox,
            };
        }
    }

    /// <summary>
    /// Frame settings handle bound to a parent <see cref="JxlEncoder"/>.
    /// Ported from <c>JxlEncoderFrameSettings</c> in <c>lib/jxl/encode_internal.h</c> and <c>lib/jxl/encode.cc</c>.
    /// </summary>
    public sealed class JxlEncoderFrameSettings
    {
        /// <summary>Owning encoder instance.</summary>
        public readonly JxlEncoder Enc;

        /// <summary>Settings values.</summary>
        public JxlEncoderFrameSettingsValues Values;

        internal JxlEncoderFrameSettings(JxlEncoder enc, JxlEncoderFrameSettingsValues values)
        {
            Enc    = enc;
            Values = values;
        }
    }

    /// <summary>
    /// Buffers and emits output bytes and ISO BMFF container boxes for <see cref="JxlEncoder"/>.
    /// Ported from <c>jxl::JxlEncoderOutputProcessorWrapper</c> in <c>lib/jxl/encode_internal.h</c>.
    /// </summary>
    public sealed class JxlEncoderOutputProcessorWrapper
    {
        private readonly List<byte> _buffer = new List<byte>();
        private int _readOffset;

        /// <summary>Gets the number of unread bytes waiting in the output queue.</summary>
        public int AvailableBytes => _buffer.Count - _readOffset;

        /// <summary>Clears all buffered output bytes.</summary>
        public void Reset()
        {
            _buffer.Clear();
            _readOffset = 0;
        }

        /// <summary>Appends <paramref name="data"/> to the output queue.</summary>
        /// <param name="data">Bytes to append.</param>
        public void Append(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                _buffer.Add(data[i]);
            }
        }

        /// <summary>
        /// Appends an ISO BMFF container box with the given 4-byte <paramref name="boxType"/> and <paramref name="payload"/>.
        /// </summary>
        /// <param name="boxType">4-character box type.</param>
        /// <param name="payload">Box payload bytes.</param>
        public void AppendBox(ReadOnlySpan<byte> boxType, ReadOnlySpan<byte> payload)
        {
            Span<byte> hdr = stackalloc byte[8];
            uint totalLen  = (uint)(8 + payload.Length);
            BinaryPrimitives.WriteUInt32BigEndian(hdr.Slice(0, 4), totalLen);
            boxType.Slice(0, 4).CopyTo(hdr.Slice(4, 4));
            Append(hdr);
            Append(payload);
        }

        /// <summary>
        /// Copies as many queued bytes as fit into <paramref name="output"/> and advances <paramref name="written"/>.
        /// </summary>
        /// <param name="output">Destination buffer.</param>
        /// <param name="written">Number of bytes written to <paramref name="output"/>.</param>
        /// <returns><c>true</c> if all queued bytes were drained; <c>false</c> if more bytes remain.</returns>
        public bool DrainTo(Span<byte> output, out int written)
        {
            int avail  = AvailableBytes;
            int toCopy = Math.Min(avail, output.Length);

            for (int i = 0; i < toCopy; i++)
            {
                output[i] = _buffer[_readOffset + i];
            }

            _readOffset += toCopy;
            written      = toCopy;

            if (_readOffset == _buffer.Count)
            {
                _buffer.Clear();
                _readOffset = 0;

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Public JPEG XL encoder state machine supporting single-frame and multi-frame VarDCT, Modular,
    /// FastLossless, lossless JPEG transcoding, extra channels, and ISO BMFF container boxes.
    /// Ported from <c>JxlEncoder</c> in <c>lib/jxl/encode_internal.h</c> and <c>lib/jxl/encode.cc</c>.
    /// </summary>
    public sealed class JxlEncoder : IDisposable
    {
        /// <summary>12-byte ISO BMFF JPEG XL container signature box (<c>JXL </c>) + <c>ftyp</c> box.</summary>
        public static ReadOnlySpan<byte> ContainerHeader => new byte[]
        {
            0x00, 0x00, 0x00, 0x0c, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0d, 0x0a, 0x87, 0x0a,
            0x00, 0x00, 0x00, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'j', (byte)'x', (byte)'l', (byte)' ',
            0x00, 0x00, 0x00, 0x00, (byte)'j', (byte)'x', (byte)'l', (byte)' ',
        };

        private readonly JxlMemoryManager _memoryManager;
        private readonly List<JxlEncoderFrameSettings> _frameSettings = new List<JxlEncoderFrameSettings>();
        private readonly List<QueuedFrame> _inputQueue = new List<QueuedFrame>();
        private readonly List<QueuedBox> _boxQueue = new List<QueuedBox>();
        private readonly JxlEncoderOutputProcessorWrapper _output = new JxlEncoderOutputProcessorWrapper();
        private readonly List<byte> _codestreamAccumulator = new List<byte>();
        private readonly List<FrameIndexBoxEntry> _frameIndexEntries = new List<FrameIndexBoxEntry>();

        private ThreadPool? _threadPool;
        private JxlCmsInterface _cms = JxlCms.GetDefaultCms();
        private CodecMetadata _metadata = new CodecMetadata();
        private JxlBasicInfo _basicInfo;
        private ulong _codestreamBytesWritten;
        private bool _basicInfoSet;
        private bool _colorEncodingSet;
        private bool _wroteHeaders;
        private bool _wroteContainerHeader;
        private bool _framesClosed;
        private bool _boxesClosed;
        private bool _useContainer;
        private bool _useBoxes;
        private bool _storeJpegMetadata;
        private int _codestreamLevel = -1;
        private JxlEncoderError _error = JxlEncoderError.OK;
        private JxlEncoderStats? _stats;
        private JxlDebugImageCallback? _debugImageCallback;
        private object? _debugImageOpaque;

        private readonly record struct FrameIndexBoxEntry(ulong Offi, uint Duration, bool ToBeIndexed);

        private sealed class QueuedFrame
        {
            public required JxlEncoderFrameSettingsValues OptionValues;
            public ImageBundle? Bundle;
            public byte[]? JpegBytes;
            public readonly Dictionary<int, byte[]> ExtraChannelBuffers = new Dictionary<int, byte[]>();
            public readonly Dictionary<int, JxlPixelFormat> ExtraChannelFormats = new Dictionary<int, JxlPixelFormat>();
        }

        private sealed class QueuedBox
        {
            public required byte[] BoxType;
            public required byte[] Contents;
            public bool CompressBox;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlEncoder"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        public JxlEncoder(JxlMemoryManager? memoryManager = null)
        {
            _memoryManager = memoryManager ?? JxlMemoryManager.Default;
            Reset();
        }

        /// <summary>
        /// Creates a new <see cref="JxlEncoder"/> instance.
        /// Ported from <c>JxlEncoderCreate</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>New <see cref="JxlEncoder"/>.</returns>
        public static JxlEncoder Create(JxlMemoryManager? memoryManager = null)
        {
            return new JxlEncoder(memoryManager);
        }

        /// <summary>
        /// Returns the encoder library version (<c>13000</c> for v0.13.0).
        /// Ported from <c>JxlEncoderVersion</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <returns>Integer version number.</returns>
        public static uint Version() => 13000u;

        /// <summary>
        /// Resets the encoder to a clean initial state so it can be reused for another image.
        /// Ported from <c>JxlEncoderReset</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void Reset()
        {
            _frameSettings.Clear();
            DisposeQueuedFrames();
            _boxQueue.Clear();
            _output.Reset();
            _codestreamAccumulator.Clear();
            _frameIndexEntries.Clear();

            _threadPool             = null;
            _cms                    = JxlCms.GetDefaultCms();
            _metadata               = new CodecMetadata();
            JxlEncoderInitBasicInfo(out _basicInfo);
            _codestreamBytesWritten = 0;
            _basicInfoSet           = false;
            _colorEncodingSet       = false;
            _wroteHeaders           = false;
            _wroteContainerHeader   = false;
            _framesClosed           = false;
            _boxesClosed            = false;
            _useContainer           = false;
            _useBoxes               = false;
            _storeJpegMetadata      = false;
            _codestreamLevel        = -1;
            _error                  = JxlEncoderError.OK;
            _stats                  = null;
        }

        /// <summary>
        /// Destroys and disposes this encoder instance.
        /// Ported from <c>JxlEncoderDestroy</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void Destroy()
        {
            Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeQueuedFrames();
            _output.Reset();
        }

        /// <summary>
        /// Gets the last error code recorded on this encoder.
        /// Ported from <c>JxlEncoderGetError</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <returns><see cref="JxlEncoderError"/> value.</returns>
        public JxlEncoderError GetError() => _error;

        /// <summary>
        /// Sets the color management system interface used during encoding.
        /// Ported from <c>JxlEncoderSetCms</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="cms">CMS interface.</param>
        public void SetCms(JxlCmsInterface cms)
        {
            _cms = cms;
        }

        /// <summary>
        /// Sets the parallel runner / thread pool used during encoding.
        /// Ported from <c>JxlEncoderSetParallelRunner</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="pool">Thread pool instance.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetParallelRunner(ThreadPool? pool)
        {
            if (_wroteHeaders)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            _threadPool = pool;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Initializes a <see cref="JxlBasicInfo"/> struct to default values.
        /// Ported from <c>JxlEncoderInitBasicInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="info">Structure to initialize.</param>
        public static void JxlEncoderInitBasicInfo(out JxlBasicInfo info)
        {
            info = new JxlBasicInfo
            {
                HaveContainer         = false,
                XSize                 = 0,
                YSize                 = 0,
                BitsPerSample         = 8,
                ExponentBitsPerSample = 0,
                IntensityTarget       = 0.0f,
                MinNits               = 0.0f,
                RelativeToMaxDisplay  = false,
                LinearBelow           = 0.0f,
                UsesOriginalProfile   = false,
                HavePreview           = false,
                HaveAnimation         = false,
                Orientation           = JxlOrientation.Identity,
                NumColorChannels      = 3,
                NumExtraChannels      = 0,
                AlphaBits             = 0,
                AlphaExponentBits     = 0,
                AlphaPremultiplied    = false,
            };
        }

        /// <summary>
        /// Initializes a <see cref="JxlFrameHeader"/> struct to default values.
        /// Ported from <c>JxlEncoderInitFrameHeader</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Structure to initialize.</param>
        public static void JxlEncoderInitFrameHeader(out JxlFrameHeader frameHeader)
        {
            frameHeader = new JxlFrameHeader
            {
                Duration   = 0,
                Timecode   = 0,
                NameLength = 0,
                IsLast     = false,
                LayerInfo  = new JxlLayerInfo
                {
                    HaveCrop     = false,
                    CropX0       = 0,
                    CropY0       = 0,
                    XSize        = 0,
                    YSize        = 0,
                    SaveAsReference = 0,
                    BlendInfo    = new JxlBlendInfo
                    {
                        BlendMode = JxlBlendMode.Replace,
                        Source    = 0,
                        Alpha     = 0,
                        Clamp     = false,
                    },
                },
            };
        }

        /// <summary>
        /// Initializes a <see cref="JxlBlendInfo"/> struct to default values.
        /// Ported from <c>JxlEncoderInitBlendInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="blendInfo">Structure to initialize.</param>
        public static void JxlEncoderInitBlendInfo(out JxlBlendInfo blendInfo)
        {
            blendInfo = new JxlBlendInfo
            {
                BlendMode = JxlBlendMode.Replace,
                Source    = 0,
                Alpha     = 0,
                Clamp     = false,
            };
        }

        /// <summary>
        /// Initializes a <see cref="JxlExtraChannelInfo"/> struct to default values for the given <paramref name="type"/>.
        /// Ported from <c>JxlEncoderInitExtraChannelInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="type">Extra channel type.</param>
        /// <param name="info">Structure to initialize.</param>
        public static void JxlEncoderInitExtraChannelInfo(JxlExtraChannelType type, out JxlExtraChannelInfo info)
        {
            info = new JxlExtraChannelInfo
            {
                Type                  = type,
                BitsPerSample         = 8,
                ExponentBitsPerSample = 0,
                DimShift              = 0,
                NameLength            = 0,
                AlphaPremultiplied    = false,
                SpotColorR            = 0.0f,
                SpotColorG            = 0.0f,
                SpotColorB            = 0.0f,
                SpotColorA            = 0.0f,
                CfaChannel            = 0,
            };
        }

        /// <summary>
        /// Converts a JPEG-style quality value (<c>0..100</c>) into a Butteraugli distance.
        /// Ported from <c>JxlEncoderDistanceFromQuality</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="quality">Quality value in <c>[0, 100]</c>.</param>
        /// <returns>Corresponding Butteraugli distance.</returns>
        public static float DistanceFromQuality(float quality)
        {
            if (quality >= 100.0f)
            {
                return 0.0f;
            }

            if (quality >= 30.0f)
            {
                return 0.1f + ((100.0f - quality) * 0.09f);
            }

            return 53.0f / 3000.0f * quality * quality - (23.0f / 20.0f * quality) + 25.0f;
        }

        /// <summary>
        /// Sets the global basic info metadata for the codestream.
        /// Ported from <c>JxlEncoderSetBasicInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="info">Basic info structure.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetBasicInfo(in JxlBasicInfo info)
        {
            if (_basicInfoSet)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            if (info.XSize == 0 || info.YSize == 0 || info.BitsPerSample == 0 || info.BitsPerSample > 32)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (info.NumColorChannels != 1 && info.NumColorChannels != 3)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (!VerifyDimensionsForCodestreamLevel(info.XSize, info.YSize))
            {
                return SetError(JxlEncoderError.NotSupported);
            }

            if (!ApplyBasicInfoToMetadata(in info))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            _basicInfo    = info;
            _basicInfoSet = true;

            if (info.HaveContainer)
            {
                _useContainer = true;
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the codestream color encoding from a <see cref="JxlColorEncoding"/> description.
        /// Ported from <c>JxlEncoderSetColorEncoding</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="color">Color encoding description.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetColorEncoding(in JxlColorEncoding color)
        {
            if (!_basicInfoSet || _colorEncodingSet)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            Status s = _metadata.M.ColorEncoding.FromExternal(color);

            if (!s)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            _colorEncodingSet = true;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the codestream color encoding from a raw ICC profile.
        /// Ported from <c>JxlEncoderSetICCProfile</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="iccProfile">Raw ICC profile bytes.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetICCProfile(ReadOnlySpan<byte> iccProfile)
        {
            if (!_basicInfoSet || _colorEncodingSet)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            if (iccProfile.IsEmpty)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            Status s = _metadata.M.ColorEncoding.SetICC(iccProfile.ToArray(), _cms);

            if (!s)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            _colorEncodingSet = true;

            if (!_basicInfo.UsesOriginalProfile)
            {
                _metadata.M.ColorEncoding.DecideIfWantICC(_cms);
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Configures extra channel metadata at <paramref name="index"/>.
        /// Ported from <c>JxlEncoderSetExtraChannelInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="index">Extra channel index.</param>
        /// <param name="info">Extra channel info.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetExtraChannelInfo(nuint index, in JxlExtraChannelInfo info)
        {
            if (!_basicInfoSet || _wroteHeaders)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            if (index >= _basicInfo.NumExtraChannels || info.BitsPerSample == 0 || info.BitsPerSample > 32)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            ExtraChannelInfo dst               = _metadata.M.ExtraChannelInfo[(int)index];
            dst.Type                           = (ExtraChannel)info.Type;
            dst.BitDepth.BitsPerSample         = info.BitsPerSample;
            dst.BitDepth.ExponentBitsPerSample = info.ExponentBitsPerSample;
            dst.BitDepth.FloatingPointSample   = info.ExponentBitsPerSample != 0;
            dst.DimShift                       = info.DimShift;
            dst.AlphaAssociated                = info.AlphaPremultiplied;
            dst.CfaChannel                     = info.CfaChannel;
            dst.SpotColor[0]                   = info.SpotColorR;
            dst.SpotColor[1]                   = info.SpotColorG;
            dst.SpotColor[2]                   = info.SpotColorB;
            dst.SpotColor[3]                   = info.SpotColorA;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the UTF-8 name for extra channel <paramref name="index"/>.
        /// Ported from <c>JxlEncoderSetExtraChannelName</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="index">Extra channel index.</param>
        /// <param name="name">Extra channel name.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetExtraChannelName(nuint index, string name)
        {
            if (!_basicInfoSet || _wroteHeaders)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            if (index >= _basicInfo.NumExtraChannels || name == null)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            _metadata.M.ExtraChannelInfo[(int)index].Name = name;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Configures whether the ISO BMFF container format is used.
        /// Ported from <c>JxlEncoderUseContainer</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="useContainer">True to wrap the codestream in a container.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus UseContainer(bool useContainer)
        {
            if (_wroteContainerHeader || _wroteHeaders)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            _useContainer = useContainer;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Indicates that the user will add metadata boxes via <see cref="AddBox"/>.
        /// Ported from <c>JxlEncoderUseBoxes</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus UseBoxes()
        {
            if (_wroteHeaders || _boxesClosed)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            _useBoxes     = true;
            _useContainer = true;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Adds a metadata box to the ISO BMFF container.
        /// Ported from <c>JxlEncoderAddBox</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="boxType">4-character box type string (e.g., <c>"Exif"</c>, <c>"xml "</c>).</param>
        /// <param name="contents">Box payload bytes.</param>
        /// <param name="compressBox">Whether to wrap the box in a Brotli-compressed <c>brob</c> box.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus AddBox(string boxType, ReadOnlySpan<byte> contents, bool compressBox = false)
        {
            if (!_useBoxes || _boxesClosed || string.IsNullOrEmpty(boxType) || boxType.Length != 4)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            byte[] typeBytes = Encoding.ASCII.GetBytes(boxType);

            if (boxType == "jxlc" || boxType == "jxlp" || boxType == "JXL " || boxType == "ftyp" || boxType == "brob")
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            _boxQueue.Add(new QueuedBox
            {
                BoxType     = typeBytes,
                Contents    = contents.ToArray(),
                CompressBox = compressBox,
            });

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Declares that no more boxes will be added via <see cref="AddBox"/>.
        /// Ported from <c>JxlEncoderCloseBoxes</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void CloseBoxes()
        {
            _boxesClosed = true;
        }

        /// <summary>
        /// Declares that no more frames will be added.
        /// Ported from <c>JxlEncoderCloseFrames</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void CloseFrames()
        {
            _framesClosed = true;
        }

        /// <summary>
        /// Closes both frames and boxes.
        /// Ported from <c>JxlEncoderCloseInput</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void CloseInput()
        {
            CloseFrames();
            CloseBoxes();
        }

        /// <summary>
        /// Configures whether lossless JPEG reconstruction metadata (<c>jbrd</c> box) is stored when transcoding JPEG frames.
        /// Ported from <c>JxlEncoderStoreJPEGMetadata</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="storeJpegMetadata">True to emit the <c>jbrd</c> box.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus StoreJPEGMetadata(bool storeJpegMetadata)
        {
            if (_wroteHeaders)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            _storeJpegMetadata = storeJpegMetadata;

            if (storeJpegMetadata)
            {
                _useContainer = true;
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the codestream feature level (<c>5</c> or <c>10</c>).
        /// Ported from <c>JxlEncoderSetCodestreamLevel</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="level">Level (5 or 10).</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetCodestreamLevel(int level)
        {
            if (level != 5 && level != 10 && level != -1)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (_basicInfoSet && !VerifyDimensionsForCodestreamLevel(_basicInfo.XSize, _basicInfo.YSize, level))
            {
                return SetError(JxlEncoderError.NotSupported);
            }

            _codestreamLevel = level;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Gets the minimum codestream level required for the configured basic info (<c>5</c>, <c>10</c>, or <c>-1</c>).
        /// Ported from <c>JxlEncoderGetRequiredCodestreamLevel</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <returns>5, 10, or -1 if dimensions exceed level 10.</returns>
        public int GetRequiredCodestreamLevel()
        {
            if (!_basicInfoSet)
            {
                return -1;
            }

            if (VerifyDimensionsForCodestreamLevel(_basicInfo.XSize, _basicInfo.YSize, 5))
            {
                return 5;
            }

            if (VerifyDimensionsForCodestreamLevel(_basicInfo.XSize, _basicInfo.YSize, 10))
            {
                return 10;
            }

            return -1;
        }

        /// <summary>
        /// Allows expert encoder options such as Effort 10 (Tortoise).
        /// Ported from <c>JxlEncoderAllowExpertOptions</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        public void AllowExpertOptions()
        {
            // Expert options are allowed by default in JxlSharp.Encoder.
        }

        /// <summary>
        /// Configures the upsampling mode for already-downsampled frames.
        /// Ported from <c>JxlEncoderSetUpsamplingMode</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="factor">Upsampling factor (1, 2, 4, or 8).</param>
        /// <param name="mode">Upsampling mode (-1 default, 0 k-nearest, 1 pixel-free).</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetUpsamplingMode(long factor, long mode)
        {
            if ((factor != 1 && factor != 2 && factor != 4 && factor != 8) ||
                (mode != -1 && mode != 0 && mode != 1))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Registers a callback for receiving intermediate debug images during encoding.
        /// Ported from <c>JxlEncoderSetDebugImageCallback</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Target frame settings.</param>
        /// <param name="callback">Debug image callback.</param>
        /// <param name="opaque">User-supplied opaque context.</param>
        public void SetDebugImageCallback(
            JxlEncoderFrameSettings frameSettings,
            JxlDebugImageCallback? callback,
            object? opaque = null)
        {
            _debugImageCallback                          = callback;
            _debugImageOpaque                            = opaque;
            frameSettings.Values.CParams.DebugImage       = callback;
            frameSettings.Values.CParams.DebugImageOpaque = opaque;
        }

        /// <summary>
        /// Attaches a <see cref="JxlEncoderStats"/> collector to <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderCollectStats</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="stats">Statistics collector.</param>
        public void CollectStats(JxlEncoderFrameSettings frameSettings, JxlEncoderStats stats)
        {
            _      = frameSettings;
            _stats = stats;
        }

        /// <summary>
        /// Creates a new <see cref="JxlEncoderFrameSettings"/> optionally cloned from <paramref name="source"/>.
        /// Ported from <c>JxlEncoderFrameSettingsCreate</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="source">Optional source frame settings to clone.</param>
        /// <returns>New <see cref="JxlEncoderFrameSettings"/>.</returns>
        public JxlEncoderFrameSettings FrameSettingsCreate(JxlEncoderFrameSettings? source = null)
        {
            JxlEncoderFrameSettingsValues vals = source != null
                ? source.Values.Clone()
                : new JxlEncoderFrameSettingsValues();

            if (_debugImageCallback != null)
            {
                vals.CParams.DebugImage       = _debugImageCallback;
                vals.CParams.DebugImageOpaque = _debugImageOpaque;
            }

            JxlEncoderFrameSettings settings = new JxlEncoderFrameSettings(this, vals);
            _frameSettings.Add(settings);

            return settings;
        }

        /// <summary>
        /// Sets the Butteraugli distance for <paramref name="frameSettings"/> (<c>0.0</c> = lossless, <c>1.0</c> = visually lossless).
        /// Ported from <c>JxlEncoderSetFrameDistance</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="distance">Butteraugli distance in <c>[0.0, 25.0]</c>.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetFrameDistance(JxlEncoderFrameSettings frameSettings, float distance)
        {
            if (distance < 0.0f || distance > 25.0f || float.IsNaN(distance))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (distance > 0.0f && distance < CompressParams.KMinButteraugliDistance)
            {
                distance = CompressParams.KMinButteraugliDistance;
            }

            frameSettings.Values.CParams.ButteraugliDistance = distance;

            if (distance == 0.0f)
            {
                frameSettings.Values.Lossless = true;
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Configures mathematically lossless encoding on <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetFrameLossless</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="lossless">True for lossless compression.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetFrameLossless(JxlEncoderFrameSettings frameSettings, bool lossless)
        {
            frameSettings.Values.Lossless = lossless;

            if (lossless)
            {
                frameSettings.Values.CParams.SetLossless();
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the bit depth interpretation of input pixel buffers for <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetFrameBitDepth</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="bitDepth">Input bit depth descriptor.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetFrameBitDepth(JxlEncoderFrameSettings frameSettings, in JxlBitDepth bitDepth)
        {
            frameSettings.Values.ImageBitDepth = bitDepth;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the frame header properties for <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetFrameHeader</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="frameHeader">Frame header descriptor.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetFrameHeader(JxlEncoderFrameSettings frameSettings, in JxlFrameHeader frameHeader)
        {
            frameSettings.Values.Header = frameHeader;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the UTF-8 frame name for <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetFrameName</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="frameName">UTF-8 frame name (at most 1071 bytes).</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetFrameName(JxlEncoderFrameSettings frameSettings, string frameName)
        {
            if (frameName == null || Encoding.UTF8.GetByteCount(frameName) > 1071)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            frameSettings.Values.FrameName = frameName;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the Butteraugli distance for an extra channel on <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetExtraChannelDistance</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="index">Extra channel index.</param>
        /// <param name="distance">Extra channel distance (<c>0.0</c> = lossless, <c>-1.0</c> = default).</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetExtraChannelDistance(JxlEncoderFrameSettings frameSettings, nuint index, float distance)
        {
            if (!_basicInfoSet || index >= _basicInfo.NumExtraChannels || (distance < 0.0f && distance != -1.0f) || distance > 25.0f)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            List<float> ecDist = frameSettings.Values.CParams.EcDistance;

            while ((nuint)ecDist.Count <= index)
            {
                ecDist.Add(-1.0f);
            }

            ecDist[(int)index] = distance;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets the blending info for extra channel <paramref name="index"/> on <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderSetExtraChannelBlendInfo</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="index">Extra channel index.</param>
        /// <param name="blendInfo">Blending configuration.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetExtraChannelBlendInfo(
            JxlEncoderFrameSettings frameSettings,
            nuint index,
            in JxlBlendInfo blendInfo)
        {
            if (!_basicInfoSet || index >= _basicInfo.NumExtraChannels)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            List<JxlBlendInfo> list = frameSettings.Values.ExtraChannelBlendInfo;

            while ((nuint)list.Count <= index)
            {
                JxlEncoderInitBlendInfo(out JxlBlendInfo defBlend);
                list.Add(defBlend);
            }

            list[(int)index] = blendInfo;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Sets an integer frame option on <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderFrameSettingsSetOption</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="option">Option identifier.</param>
        /// <param name="value">Integer option value.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus FrameSettingsSetOption(
            JxlEncoderFrameSettings frameSettings,
            JxlEncoderFrameSettingId option,
            long value)
        {
            CompressParams cp = frameSettings.Values.CParams;

            if (TryApplyCoreOption(cp, option, value, out JxlEncoderStatus status))
            {
                return status;
            }

            if (TryApplyModularOrJpegOption(frameSettings.Values, cp, option, value, out status))
            {
                return status;
            }

            return SetError(JxlEncoderError.BadInput);
        }

        /// <summary>
        /// Sets a floating-point frame option on <paramref name="frameSettings"/>.
        /// Ported from <c>JxlEncoderFrameSettingsSetFloatOption</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="option">Option identifier.</param>
        /// <param name="value">Floating-point option value.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus FrameSettingsSetFloatOption(
            JxlEncoderFrameSettings frameSettings,
            JxlEncoderFrameSettingId option,
            float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            CompressParams cp = frameSettings.Values.CParams;

            switch (option)
            {
                case JxlEncoderFrameSettingId.PhotonNoise:
                    if (value < 0.0f)
                    {
                        return SetError(JxlEncoderError.BadInput);
                    }

                    cp.PhotonNoiseIso = value;

                    return JxlEncoderStatus.Success;

                case JxlEncoderFrameSettingId.ChannelColorsGlobalPercent:
                    if (value < -1.0f || value > 100.0f)
                    {
                        return SetError(JxlEncoderError.BadInput);
                    }

                    cp.ChannelColorsPreTransformPercent = value < 0.0f ? 95.0f : value;

                    return JxlEncoderStatus.Success;

                case JxlEncoderFrameSettingId.ChannelColorsGroupPercent:
                    if (value < -1.0f || value > 100.0f)
                    {
                        return SetError(JxlEncoderError.BadInput);
                    }

                    cp.ChannelColorsPercent = value < 0.0f ? 80.0f : value;

                    return JxlEncoderStatus.Success;

                case JxlEncoderFrameSettingId.ModularMaTreeLearningPercent:
                    if (value < -1.0f || value > 100.0f)
                    {
                        return SetError(JxlEncoderError.BadInput);
                    }

                    cp.Options.NbRepeats = value < 0.0f ? 0.5f : value * 0.01f;

                    return JxlEncoderStatus.Success;

                default:
                    return SetError(JxlEncoderError.BadInput);
            }
        }

        /// <summary>
        /// Queues a planar or interleaved pixel buffer as the next image frame.
        /// Ported from <c>JxlEncoderAddImageFrame</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="pixelFormat">Format of <paramref name="buffer"/>.</param>
        /// <param name="buffer">Input pixel bytes.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus AddImageFrame(
            JxlEncoderFrameSettings frameSettings,
            in JxlPixelFormat pixelFormat,
            ReadOnlySpan<byte> buffer)
        {
            if (!_basicInfoSet || _framesClosed)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            EnsureDefaultColorEncoding();

            nuint xsize = frameSettings.Values.Header.LayerInfo.HaveCrop
                ? frameSettings.Values.Header.LayerInfo.XSize
                : _basicInfo.XSize;
            nuint ysize = frameSettings.Values.Header.LayerInfo.HaveCrop
                ? frameSettings.Values.Header.LayerInfo.YSize
                : _basicInfo.YSize;

            if (frameSettings.Values.CParams.AlreadyDownsampled && frameSettings.Values.CParams.Resampling > 1)
            {
                nuint factor = (nuint)frameSettings.Values.CParams.Resampling;
                xsize        = Common.DivCeil(xsize, factor);
                ysize        = Common.DivCeil(ysize, factor);
            }

            if (xsize == 0 || ysize == 0)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            ImageBundle ib = new ImageBundle(_memoryManager, _metadata.M);
            Status convOk  = ConvertBufferToImageBundle(frameSettings, in pixelFormat, buffer, xsize, ysize, ib);

            if (!convOk)
            {
                ib.Dispose();

                return SetError(JxlEncoderError.BadInput);
            }

            _inputQueue.Add(new QueuedFrame
            {
                OptionValues = frameSettings.Values.Clone(),
                Bundle       = ib,
            });

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Attaches a planar extra-channel pixel buffer to the most recently queued image frame (or next frame).
        /// Ported from <c>JxlEncoderSetExtraChannelBuffer</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="pixelFormat">Pixel format of <paramref name="buffer"/>.</param>
        /// <param name="buffer">Extra channel pixel bytes.</param>
        /// <param name="index">Extra channel index.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus SetExtraChannelBuffer(
            JxlEncoderFrameSettings frameSettings,
            in JxlPixelFormat pixelFormat,
            ReadOnlySpan<byte> buffer,
            uint index)
        {
            if (!_basicInfoSet || index >= _basicInfo.NumExtraChannels || _inputQueue.Count == 0)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            QueuedFrame last = _inputQueue[_inputQueue.Count - 1];

            if (last.Bundle == null)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            ExtraChannelInfo eci = _metadata.M.ExtraChannelInfo[(int)index];
            nuint ecX            = eci.DimShift > 0 ? Common.DivCeil(last.Bundle.XSize(), (nuint)(1u << (int)eci.DimShift)) : last.Bundle.XSize();
            nuint ecY            = eci.DimShift > 0 ? Common.DivCeil(last.Bundle.YSize(), (nuint)(1u << (int)eci.DimShift)) : last.Bundle.YSize();

            StatusOr<ImagePlane<float>> planeOr = ImagePlane<float>.Create(_memoryManager, ecX, ecY);

            if (!planeOr.IsOk)
            {
                return SetError(JxlEncoderError.OutOfMemory);
            }

            ImagePlane<float> ecPlane = planeOr.Value;
            JxlPixelFormat ecFormat   = pixelFormat;
            ecFormat.NumChannels      = 1;
            uint bitsPerSample        = frameSettings.Values.ImageBitDepth.Type == JxlBitDepthType.FromCodestream
                ? eci.BitDepth.BitsPerSample
                : GetEffectiveBitsPerSample(frameSettings.Values.ImageBitDepth, in ecFormat);
            ExternalBufferSpec spec   = new ExternalBufferSpec(ecX, ecY, 0, bitsPerSample, ecFormat, 0);

            Status convOk = EncExternalImage.ConvertFromExternalNoSizeCheck(
                buffer,
                in spec,
                _threadPool,
                ecPlane);

            if (!convOk)
            {
                ecPlane.Dispose();

                return SetError(JxlEncoderError.BadInput);
            }

            EnsureExtraChannelSlots(last.Bundle, (int)index);
            last.Bundle.ExtraChannels[(int)index].Dispose();
            last.Bundle.ExtraChannels[(int)index] = ecPlane;

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Queues a JPEG codestream for lossless JPEG-to-JXL transcoding.
        /// Ported from <c>JxlEncoderAddJPEGFrame</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="frameSettings">Frame settings.</param>
        /// <param name="buffer">Raw JPEG file bytes.</param>
        /// <returns><see cref="JxlEncoderStatus.Success"/> on success.</returns>
        public JxlEncoderStatus AddJPEGFrame(
            JxlEncoderFrameSettings frameSettings,
            ReadOnlySpan<byte> buffer)
        {
            if (_framesClosed || buffer.IsEmpty)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            Status parseOk = EncJpegData.ParseJPG(_memoryManager, buffer, out JPEGData? jpg);

            if (!parseOk || jpg == null)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (!_basicInfoSet)
            {
                JxlEncoderInitBasicInfo(out JxlBasicInfo info);
                info.XSize               = (uint)jpg.Width;
                info.YSize               = (uint)jpg.Height;
                info.BitsPerSample       = 8;
                info.UsesOriginalProfile = true;
                info.NumColorChannels    = (uint)jpg.Components.Count;

                if (SetBasicInfo(in info) != JxlEncoderStatus.Success)
                {
                    return JxlEncoderStatus.Error;
                }
            }

            if (!_colorEncodingSet)
            {
                Status ceOk = EncJpegData.SetColorEncodingFromJpegData(jpg, _metadata.M.ColorEncoding);

                if (!ceOk)
                {
                    return SetError(JxlEncoderError.BadInput);
                }

                _colorEncodingSet = true;
            }

            if (_metadata.M.XybEncoded)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            JxlEncoderStatus blobStatus = ProcessJpegBlobs(frameSettings, jpg);

            if (blobStatus != JxlEncoderStatus.Success)
            {
                return blobStatus;
            }

            ImageBundle ib = new ImageBundle(_memoryManager, _metadata.M)
            {
                JpegData = jpg,
            };

            Status ctOk = EncJpegData.SetColorTransformFromJpegData(jpg, out ColorTransform ct);
            _           = ctOk;
            ib.ColorTransform = ct;

            _inputQueue.Add(new QueuedFrame
            {
                OptionValues = frameSettings.Values.Clone(),
                Bundle       = ib,
                JpegBytes    = buffer.ToArray(),
            });

            return JxlEncoderStatus.Success;
        }

        private JxlEncoderStatus ProcessJpegBlobs(JxlEncoderFrameSettings frameSettings, JPEGData jpg)
        {
            JpegBlobs blobs = new JpegBlobs();

            if (!EncJpegData.SetBlobsFromJpegData(jpg, blobs))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            CompressParams cparams = frameSettings.Values.CParams;

            if (blobs.Exif.Length > 0)
            {
                JxlOrientation orient = (JxlOrientation)_metadata.M.Orientation;
                Exif.InterpretExif(blobs.Exif, ref orient);
                _metadata.M.Orientation = (uint)orient;
                _basicInfo.Orientation  = orient;
            }

            if (blobs.Exif.Length > 0 && cparams.JpegKeepExif)
            {
                if (blobs.Exif.Length > 0xFFFF)
                {
                    return SetError(JxlEncoderError.Generic);
                }

                byte[] exifBox = new byte[blobs.Exif.Length + 4];
                blobs.Exif.CopyTo(exifBox.AsSpan(4));
                UseBoxes();
                AddBox("Exif", exifBox, cparams.JpegCompressBoxes);
            }

            if (blobs.Xmp.Length > 0 && cparams.JpegKeepXmp)
            {
                UseBoxes();
                AddBox("xml ", blobs.Xmp, cparams.JpegCompressBoxes);
            }

            if (blobs.Jumbf.Length > 0 && cparams.JpegKeepJumbf)
            {
                UseBoxes();
                AddBox("jumb", blobs.Jumbf, cparams.JpegCompressBoxes);
            }

            if (_storeJpegMetadata && (!cparams.JpegKeepExif || !cparams.JpegKeepXmp))
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            return JxlEncoderStatus.Success;
        }

        /// <summary>
        /// Encodes queued frames/boxes and writes compressed JXL output into <paramref name="output"/>,
        /// advancing <paramref name="offset"/> by the number of bytes written.
        /// Ported from <c>JxlEncoderProcessOutput</c> in <c>lib/jxl/encode.cc</c>.
        /// </summary>
        /// <param name="output">Output buffer.</param>
        /// <param name="offset">Current write offset into <paramref name="output"/> (updated on return).</param>
        /// <returns>
        /// <see cref="JxlEncoderStatus.Success"/> when all input has been encoded and written;
        /// <see cref="JxlEncoderStatus.NeedMoreOutput"/> if the caller must provide more output space;
        /// or <see cref="JxlEncoderStatus.Error"/> on failure.
        /// </returns>
        public JxlEncoderStatus ProcessOutput(Span<byte> output, ref int offset)
        {
            if (offset < 0 || offset > output.Length)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            JxlEncoderStatus status = FlushPendingToOutputQueue();

            if (status != JxlEncoderStatus.Success)
            {
                return status;
            }

            Span<byte> remaining = output.Slice(offset);
            bool drained         = _output.DrainTo(remaining, out int written);
            offset              += written;

            if (!drained)
            {
                return JxlEncoderStatus.NeedMoreOutput;
            }

            return JxlEncoderStatus.Success;
        }

        private JxlEncoderStatus FlushPendingToOutputQueue()
        {
            if (_useContainer && !_wroteContainerHeader)
            {
                _output.Append(ContainerHeader);
                _wroteContainerHeader = true;
            }

            while (_boxQueue.Count > 0)
            {
                QueuedBox box = _boxQueue[0];
                _boxQueue.RemoveAt(0);
                EmitQueuedBox(box);
            }

            if (_inputQueue.Count == 0)
            {
                return JxlEncoderStatus.Success;
            }

            if (!_wroteHeaders)
            {
                JxlEncoderStatus hdrStatus = WriteCodestreamHeaders();

                if (hdrStatus != JxlEncoderStatus.Success)
                {
                    return hdrStatus;
                }

                _wroteHeaders = true;
            }

            while (_inputQueue.Count > 0)
            {
                QueuedFrame frame = _inputQueue[0];
                _inputQueue.RemoveAt(0);

                bool isLastFrame           = _framesClosed && _inputQueue.Count == 0;
                JxlEncoderStatus frmStatus = EncodeQueuedFrame(frame, isLastFrame);
                frame.Bundle?.Dispose();

                if (frmStatus != JxlEncoderStatus.Success)
                {
                    return frmStatus;
                }
            }

            if (_useContainer && _framesClosed && _codestreamAccumulator.Count > 0)
            {
                ReadOnlySpan<byte> jxlcType = "jxlc"u8;
                _output.AppendBox(jxlcType, _codestreamAccumulator.ToArray());
                _codestreamAccumulator.Clear();
            }

            if (_useContainer && _framesClosed && HasIndexedFrames())
            {
                byte[] jxliPayload      = EncodeFrameIndexBoxPayload();
                ReadOnlySpan<byte> jxli = "jxli"u8;
                _output.AppendBox(jxli, jxliPayload);
                _frameIndexEntries.Clear();
            }

            return JxlEncoderStatus.Success;
        }

        private void EmitQueuedBox(QueuedBox box)
        {
            if (!box.CompressBox)
            {
                _output.AppendBox(box.BoxType, box.Contents);

                return;
            }

            using MemoryStream ms = new MemoryStream();
            ms.Write(box.BoxType, 0, 4);

            using (BrotliStream bs = new BrotliStream(ms, new BrotliCompressionOptions { Quality = 9 }, leaveOpen: true))
            {
                bs.Write(box.Contents, 0, box.Contents.Length);
            }

            ReadOnlySpan<byte> brobType = "brob"u8;
            _output.AppendBox(brobType, ms.ToArray());
        }

        private JxlEncoderStatus WriteCodestreamHeaders()
        {
            if (!_basicInfoSet)
            {
                return SetError(JxlEncoderError.ApiUsage);
            }

            EnsureDefaultColorEncoding();

            BitWriter writer = new BitWriter(_memoryManager);
            Status sigOk     = WriteCodestreamSignatureAndMetadata(writer);

            if (!sigOk)
            {
                return SetError(JxlEncoderError.Generic);
            }

            AppendCodestreamBytes(writer.GetSpan());

            return JxlEncoderStatus.Success;
        }

        private Status WriteCodestreamSignatureAndMetadata(BitWriter writer)
        {
            Status sigOk = writer.WithMaxBits(
                16,
                LayerType.Header,
                _stats?.Stats,
                () =>
                {
                    writer.Write(8, 0xFF);
                    writer.Write(8, 0x0A);

                    return true;
                });

            if (!sigOk)
            {
                return sigOk;
            }

            Status sizeOk = Headers.WriteSizeHeader(_metadata.Size, writer, LayerType.Header, _stats?.Stats);

            if (!sizeOk)
            {
                return sizeOk;
            }

            Status metaOk = ImageMetadataMethods.WriteImageMetadata(_metadata.M, writer, LayerType.Header, _stats?.Stats);

            if (!metaOk)
            {
                return metaOk;
            }

            _metadata.TransformData.NonserializedXybEncoded = _metadata.M.XybEncoded;

            Status tdOk = Bundle.Write(_metadata.TransformData, writer, LayerType.Header, _stats?.Stats);

            if (!tdOk)
            {
                return tdOk;
            }

            if (_metadata.M.ColorEncoding.WantICC())
            {
                Status iccOk = IccCodec.WriteICC(_metadata.M.ColorEncoding.ICC(), writer, LayerType.Header, _stats?.Stats);

                if (!iccOk)
                {
                    return iccOk;
                }
            }

            writer.ZeroPadToByte();

            return Status.OkStatus;
        }

        private JxlEncoderStatus EncodeQueuedFrame(QueuedFrame frame, bool isLastFrame)
        {
            if (frame.Bundle == null)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (_storeJpegMetadata && frame.Bundle.JpegData != null)
            {
                Status jbrdOk = EncJpegData.EncodeJPEGData(
                    _memoryManager,
                    frame.Bundle.JpegData,
                    out byte[] jbrdBytes,
                    frame.OptionValues.CParams);

                if (!jbrdOk)
                {
                    return SetError(JxlEncoderError.Jbrd);
                }

                ReadOnlySpan<byte> jbrdType = "jbrd"u8;
                _output.AppendBox(jbrdType, jbrdBytes);
            }

            uint duration = _metadata.M.HaveAnimation ? frame.OptionValues.Header.Duration : 0u;
            _frameIndexEntries.Add(new FrameIndexBoxEntry(_codestreamBytesWritten, duration, frame.OptionValues.FrameIndexBox));

            FrameInfo frameInfo = BuildFrameInfo(frame.OptionValues, isLastFrame);
            BitWriter writer    = new BitWriter(_memoryManager);

            Status encOk = EncFrame.EncodeFrame(
                _memoryManager,
                frame.OptionValues.CParams,
                frameInfo,
                _metadata,
                frame.Bundle,
                _cms,
                (_threadPool, writer, _stats?.Stats));

            if (!encOk)
            {
                return SetError(JxlEncoderError.Generic);
            }

            _stats?.SyncFromAuxOut();
            AppendCodestreamBytes(writer.GetSpan());

            return JxlEncoderStatus.Success;
        }

        private bool HasIndexedFrames()
        {
            for (int i = 0; i < _frameIndexEntries.Count; i++)
            {
                if (_frameIndexEntries[i].ToBeIndexed)
                {
                    return true;
                }
            }

            return false;
        }

        private byte[] EncodeFrameIndexBoxPayload()
        {
            using MemoryStream ms = new MemoryStream();
            int nf                = 0;

            for (int i = 0; i < _frameIndexEntries.Count; i++)
            {
                if (i == 0 || _frameIndexEntries[i].ToBeIndexed)
                {
                    nf++;
                }
            }

            WriteVarInt(ms, (ulong)nf);
            WriteBe32(ms, 1u);
            WriteBe32(ms, 1000u);

            int prevPrevIx = -1;
            int prevIx     = 0;
            uint tPrev     = 0;
            uint t         = 0;

            for (int i = 1; i < _frameIndexEntries.Count; i++)
            {
                if (_frameIndexEntries[i].ToBeIndexed)
                {
                    ulong offi = prevPrevIx == -1
                        ? _frameIndexEntries[prevIx].Offi
                        : _frameIndexEntries[prevIx].Offi - _frameIndexEntries[prevPrevIx].Offi;
                    WriteVarInt(ms, offi);
                    WriteVarInt(ms, tPrev);
                    WriteVarInt(ms, (ulong)(i - prevIx));

                    prevPrevIx = prevIx;
                    prevIx     = i;
                    tPrev      = t;
                    t         += _frameIndexEntries[i].Duration;
                }
            }

            ulong lastOffi = prevPrevIx == -1
                ? _frameIndexEntries[prevIx].Offi
                : _frameIndexEntries[prevIx].Offi - _frameIndexEntries[prevPrevIx].Offi;
            WriteVarInt(ms, lastOffi);
            WriteVarInt(ms, tPrev);
            WriteVarInt(ms, (ulong)(_frameIndexEntries.Count - prevIx));

            return ms.ToArray();
        }

        private static void WriteVarInt(MemoryStream ms, ulong value)
        {
            while (value > 127)
            {
                ms.WriteByte((byte)((value & 127u) | 128u));
                value >>= 7;
            }

            ms.WriteByte((byte)(value & 127u));
        }

        private static void WriteBe32(MemoryStream ms, uint value)
        {
            ms.WriteByte((byte)(value >> 24));
            ms.WriteByte((byte)(value >> 16));
            ms.WriteByte((byte)(value >> 8));
            ms.WriteByte((byte)value);
        }

        private FrameInfo BuildFrameInfo(JxlEncoderFrameSettingsValues vals, bool isLastFrame)
        {
            FrameInfo info = new FrameInfo
            {
                IsLast                = isLastFrame || vals.Header.IsLast,
                Name                  = vals.FrameName,
                Duration              = _metadata.M.HaveAnimation ? vals.Header.Duration : 0u,
                Timecode              = _metadata.M.HaveAnimation ? vals.Header.Timecode : 0u,
                SaveAsReference       = vals.Header.LayerInfo.SaveAsReference,
                AlphaIsPremultiplied  = _basicInfo.AlphaPremultiplied,
            };

            info.BlendInfo.Mode         = (BlendMode)vals.Header.LayerInfo.BlendInfo.BlendMode;
            info.BlendInfo.Source       = vals.Header.LayerInfo.BlendInfo.Source;
            info.BlendInfo.AlphaChannel = vals.Header.LayerInfo.BlendInfo.Alpha;
            info.BlendInfo.Clamp        = vals.Header.LayerInfo.BlendInfo.Clamp;

            if (vals.Header.LayerInfo.HaveCrop)
            {
                info.CustomSizeOrOrigin = true;
                info.OriginX0           = vals.Header.LayerInfo.CropX0;
                info.OriginY0           = vals.Header.LayerInfo.CropY0;
            }

            for (int i = 0; i < vals.ExtraChannelBlendInfo.Count; i++)
            {
                JxlBlendInfo src = vals.ExtraChannelBlendInfo[i];
                info.ExtraChannelBlendingInfo.Add(new BlendingInfo
                {
                    Mode         = (BlendMode)src.BlendMode,
                    Source       = src.Source,
                    AlphaChannel = src.Alpha,
                    Clamp        = src.Clamp,
                });
            }

            return info;
        }

        private void AppendCodestreamBytes(ReadOnlySpan<byte> bytes)
        {
            _codestreamBytesWritten += (ulong)bytes.Length;

            if (_useContainer)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    _codestreamAccumulator.Add(bytes[i]);
                }
            }
            else
            {
                _output.Append(bytes);
            }
        }

        private bool ApplyBasicInfoToMetadata(in JxlBasicInfo info)
        {
            if (!_metadata.Size.Set(info.XSize, info.YSize))
            {
                return false;
            }

            ImageMetadata m                  = _metadata.M;
            m.BitDepth.BitsPerSample         = info.BitsPerSample;
            m.BitDepth.ExponentBitsPerSample = info.ExponentBitsPerSample;
            m.BitDepth.FloatingPointSample   = info.ExponentBitsPerSample != 0;
            m.XybEncoded                     = !info.UsesOriginalProfile;
            m.Orientation                    = (uint)info.Orientation;
            m.HavePreview                    = info.HavePreview;
            m.HaveAnimation                  = info.HaveAnimation;

            if (info.HaveAnimation)
            {
                if (info.Animation.TpsNumerator < 1 || info.Animation.TpsDenominator < 1)
                {
                    return false;
                }

                m.Animation.TpsNumerator   = info.Animation.TpsNumerator;
                m.Animation.TpsDenominator = info.Animation.TpsDenominator;
                m.Animation.NumLoops       = info.Animation.NumLoops;
                m.Animation.HaveTimecodes  = info.Animation.HaveTimecodes;
            }

            if (info.IntensityTarget > 0.0f)
            {
                m.SetIntensityTarget(info.IntensityTarget);
            }

            m.ToneMapping.MinNits              = info.MinNits;
            m.ToneMapping.RelativeToMaxDisplay = info.RelativeToMaxDisplay;
            m.ToneMapping.LinearBelow          = info.LinearBelow;

            m.ExtraChannelInfo.Clear();

            for (uint i = 0; i < info.NumExtraChannels; i++)
            {
                ExtraChannelInfo ec = new ExtraChannelInfo();

                if (i == 0 && info.AlphaBits > 0)
                {
                    ec.Type                           = ExtraChannel.Alpha;
                    ec.BitDepth.BitsPerSample         = info.AlphaBits;
                    ec.BitDepth.ExponentBitsPerSample = info.AlphaExponentBits;
                    ec.BitDepth.FloatingPointSample   = info.AlphaExponentBits != 0;
                    ec.AlphaAssociated                = info.AlphaPremultiplied;
                }
                else
                {
                    ec.Type                   = ExtraChannel.Unknown;
                    ec.BitDepth.BitsPerSample = info.BitsPerSample;
                }

                m.ExtraChannelInfo.Add(ec);
            }

            return true;
        }

        private void EnsureDefaultColorEncoding()
        {
            if (_colorEncodingSet)
            {
                return;
            }

            bool isGray = _basicInfo.NumColorChannels == 1;
            _metadata.M.ColorEncoding.FromExternal(ColorEncoding.SRGB(isGray).ToExternal());
            _colorEncodingSet = true;
        }

        private Status ConvertBufferToImageBundle(
            JxlEncoderFrameSettings frameSettings,
            in JxlPixelFormat pixelFormat,
            ReadOnlySpan<byte> buffer,
            nuint xsize,
            nuint ysize,
            ImageBundle ib)
        {
            uint bitsPerSample = GetEffectiveBitsPerSample(frameSettings.Values.ImageBitDepth, in pixelFormat);
            bool hasAlpha      = (pixelFormat.NumChannels == 2 || pixelFormat.NumChannels == 4) && _basicInfo.AlphaBits > 0;
            _                  = bitsPerSample;

            Status convOk = EncExternalImage.BufferToImageBundle(
                pixelFormat,
                xsize,
                ysize,
                buffer,
                _threadPool,
                ib);

            if (!convOk)
            {
                return convOk;
            }

            if (frameSettings.Values.Lossless)
            {
                frameSettings.Values.CParams.SetLossless();
            }

            if (!_metadata.M.XybEncoded)
            {
                frameSettings.Values.CParams.ColorTransform = ColorTransform.None;
            }

            _ = hasAlpha;

            return Status.OkStatus;
        }

        private uint GetEffectiveBitsPerSample(in JxlBitDepth bitDepth, in JxlPixelFormat format)
        {
            if (bitDepth.Type == JxlBitDepthType.FromCodestream)
            {
                return _basicInfo.BitsPerSample;
            }

            if (bitDepth.Type == JxlBitDepthType.Custom)
            {
                return bitDepth.BitsPerSample;
            }

            return format.DataType switch
            {
                JxlDataType.UInt8   => 8u,
                JxlDataType.UInt16  => 16u,
                JxlDataType.Float16 => 16u,
                JxlDataType.Float   => 32u,
                _                   => _basicInfo.BitsPerSample,
            };
        }

        private void EnsureExtraChannelSlots(ImageBundle ib, int targetIndex)
        {
            while (ib.ExtraChannels.Count <= targetIndex)
            {
                ExtraChannelInfo eci = _metadata.M.ExtraChannelInfo[ib.ExtraChannels.Count];
                nuint ecX            = eci.DimShift > 0 ? Common.DivCeil(ib.XSize(), (nuint)(1u << (int)eci.DimShift)) : ib.XSize();
                nuint ecY            = eci.DimShift > 0 ? Common.DivCeil(ib.YSize(), (nuint)(1u << (int)eci.DimShift)) : ib.YSize();
                ib.ExtraChannels.Add(ImagePlane<float>.Create(_memoryManager, ecX, ecY).Value);
            }
        }

        private bool VerifyDimensionsForCodestreamLevel(nuint xsize, nuint ysize, int explicitLevel = 0)
        {
            int level = explicitLevel != 0 ? explicitLevel : _codestreamLevel;

            if (level == 5)
            {
                ulong pixels = (ulong)xsize * ysize;

                return xsize <= (1u << 18) && ysize <= (1u << 18) && pixels <= (1ul << 28);
            }

            if (level == 10)
            {
                ulong pixels = (ulong)xsize * ysize;

                return xsize <= (1u << 30) && ysize <= (1u << 30) && pixels <= (1ul << 40);
            }

            return true;
        }

        private bool TryApplyCoreOption(
            CompressParams cp,
            JxlEncoderFrameSettingId option,
            long value,
            out JxlEncoderStatus status)
        {
            status = JxlEncoderStatus.Success;

            switch (option)
            {
                case JxlEncoderFrameSettingId.Effort:
                    if (value < 1 || value > 11)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.SpeedTier = (SpeedTier)(10 - (int)value);

                    return true;

                case JxlEncoderFrameSettingId.DecodingSpeed:
                    if (value < 0 || value > 4)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.DecodingSpeedTier = (nuint)value;

                    return true;

                case JxlEncoderFrameSettingId.Resampling:
                    if (value != -1 && value != 1 && value != 2 && value != 4 && value != 8)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Resampling = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.ExtraChannelResampling:
                    if (value != -1 && value != 1 && value != 2 && value != 4 && value != 8)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.EcResampling = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.AlreadyDownsampled:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.AlreadyDownsampled = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.Noise:
                case JxlEncoderFrameSettingId.Dots:
                case JxlEncoderFrameSettingId.Patches:
                case JxlEncoderFrameSettingId.Gaborish:
                case JxlEncoderFrameSettingId.KeepInvisible:
                case JxlEncoderFrameSettingId.ProgressiveAc:
                case JxlEncoderFrameSettingId.QProgressiveAc:
                    return ApplyOverrideOption(cp, option, value, out status);

                case JxlEncoderFrameSettingId.Epf:
                    if (value < -1 || value > 3)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Epf = (int)value;

                    return true;

                default:
                    return false;
            }
        }

        private bool ApplyOverrideOption(
            CompressParams cp,
            JxlEncoderFrameSettingId option,
            long value,
            out JxlEncoderStatus status)
        {
            if (value < -1 || value > 1)
            {
                status = SetError(JxlEncoderError.BadInput);

                return true;
            }

            Override ov = (Override)value;
            status      = JxlEncoderStatus.Success;

            switch (option)
            {
                case JxlEncoderFrameSettingId.Noise:
                    cp.Noise = ov;

                    break;

                case JxlEncoderFrameSettingId.Dots:
                    cp.Dots = ov;

                    break;

                case JxlEncoderFrameSettingId.Patches:
                    cp.Patches = ov;

                    break;

                case JxlEncoderFrameSettingId.Gaborish:
                    cp.Gaborish = ov;

                    break;

                case JxlEncoderFrameSettingId.KeepInvisible:
                    cp.KeepInvisible = ov;

                    break;

                case JxlEncoderFrameSettingId.ProgressiveAc:
                    cp.ProgressiveMode = ov;

                    break;

                case JxlEncoderFrameSettingId.QProgressiveAc:
                    cp.QProgressiveMode = ov;

                    break;
            }

            return true;
        }

        private bool TryApplyModularOrJpegOption(
            JxlEncoderFrameSettingsValues vals,
            CompressParams cp,
            JxlEncoderFrameSettingId option,
            long value,
            out JxlEncoderStatus status)
        {
            status = JxlEncoderStatus.Success;

            switch (option)
            {
                case JxlEncoderFrameSettingId.Modular:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.ModularMode = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.GroupOrder:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.CenterFirst = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.GroupOrderCenterX:
                    if (value < -1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.CenterX = value == -1 ? nuint.MaxValue : (nuint)value;

                    return true;

                case JxlEncoderFrameSettingId.GroupOrderCenterY:
                    if (value < -1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.CenterY = value == -1 ? nuint.MaxValue : (nuint)value;

                    return true;

                case JxlEncoderFrameSettingId.Responsive:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Responsive = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.ProgressiveDc:
                    if (value < -1 || value > 2)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.ProgressiveDc = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.PaletteColors:
                    if (value < -1 || value > 70913)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.PaletteColors = value == -1 ? (1 << 10) : (int)value;

                    return true;

                case JxlEncoderFrameSettingId.LossyPalette:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.LossyPalette = value == 1;

                    return true;

                default:
                    return TryApplyExtendedOption(vals, cp, option, value, out status);
            }
        }

        private bool TryApplyExtendedOption(
            JxlEncoderFrameSettingsValues vals,
            CompressParams cp,
            JxlEncoderFrameSettingId option,
            long value,
            out JxlEncoderStatus status)
        {
            status = JxlEncoderStatus.Success;

            switch (option)
            {
                case JxlEncoderFrameSettingId.ColorTransform:
                    if (value < -1 || value > 2)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.ColorTransform = value == -1 ? ColorTransform.XYB : (ColorTransform)value;

                    return true;

                case JxlEncoderFrameSettingId.ModularColorSpace:
                    if (value < -1 || value > 41)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Colorspace = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.ModularGroupSize:
                    if (value < -1 || value > 3)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.ModularGroupSizeShift = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.ModularPredictor:
                    if (value < -1 || value > 15)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Options.Predictor = value == -1 ? Predictor.Undefined : (Predictor)value;

                    return true;

                case JxlEncoderFrameSettingId.ModularNbPrevChannels:
                    if (value < -1 || value > 11)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Options.MaxProperties = value == -1 ? 0 : (int)value;

                    return true;

                case JxlEncoderFrameSettingId.JpegReconCfl:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.ForceCflJpegRecompression = value != 0;

                    return true;

                case JxlEncoderFrameSettingId.FrameIndexBox:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    vals.FrameIndexBox = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.BrotliEffort:
                    if (value < -1 || value > 11)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.BrotliEffort = (int)value;

                    return true;

                default:
                    return TryApplyMetadataFlagOption(cp, option, value, out status);
            }
        }

        private bool TryApplyMetadataFlagOption(
            CompressParams cp,
            JxlEncoderFrameSettingId option,
            long value,
            out JxlEncoderStatus status)
        {
            status = JxlEncoderStatus.Success;

            switch (option)
            {
                case JxlEncoderFrameSettingId.JpegCompressBoxes:
                    if (value < -1 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.JpegCompressBoxes = value != 0;

                    return true;

                case JxlEncoderFrameSettingId.Buffering:
                    if (value < -1 || value > 3)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.Buffering = (int)value;

                    return true;

                case JxlEncoderFrameSettingId.JpegKeepExif:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.JpegKeepExif = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.JpegKeepXmp:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.JpegKeepXmp = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.JpegKeepJumbf:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.JpegKeepJumbf = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.UseFullImageHeuristics:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.UseFullImageHeuristics = value == 1;

                    return true;

                case JxlEncoderFrameSettingId.DisablePerceptualOptimizations:
                    if (value < 0 || value > 1)
                    {
                        status = SetError(JxlEncoderError.BadInput);

                        return true;
                    }

                    cp.DisablePerceptualOptimizations = value == 1;

                    return true;

                default:
                    return false;
            }
        }

        private void DisposeQueuedFrames()
        {
            for (int i = 0; i < _inputQueue.Count; i++)
            {
                _inputQueue[i].Bundle?.Dispose();
            }

            _inputQueue.Clear();
        }

        private JxlEncoderStatus SetError(JxlEncoderError error)
        {
            _error = error;

            return JxlEncoderStatus.Error;
        }
    }
}
