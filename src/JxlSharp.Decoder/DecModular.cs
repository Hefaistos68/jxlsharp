// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using ThreadPool = JxlSharp.Base.ThreadPool;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Frame-level Modular decoder handling VarDCT DC, VarDCT AC metadata, quantization tables,
    /// Modular color channels, and Modular extra channels.
    /// Ported from <c>jxl::ModularFrameDecoder</c> in <c>lib/jxl/dec_modular.h</c> and <c>lib/jxl/dec_modular.cc</c>.
    /// </summary>
    public sealed unsafe class ModularFrameDecoder : IDisposable
    {
        private readonly JxlMemoryManager _memoryManager;
        private ModularImage _fullImage;
        private List<Transform> _globalTransform = new List<Transform>();
        private GroupHeader _globalHeader = new GroupHeader();
        private FrameDimensions _frameDim = new FrameDimensions();
        private bool _doColor;
        private bool _haveSomething;
        private bool _useFullImage = true;
        private bool _allSameShift;
        private readonly List<PropertyDecisionNode> _tree = new List<PropertyDecisionNode>();
        private readonly ANSCode _code = new ANSCode();
        private byte[] _contextMap = Array.Empty<byte>();

        /// <summary>
        /// Bundles parameters for <see cref="DecodeGroup(in DecodeGroupArgs)"/>.
        /// </summary>
        public struct DecodeGroupArgs
        {
            /// <summary>Current frame header.</summary>
            public required FrameHeader FrameHeader;

            /// <summary>Group rectangle in frame coordinates.</summary>
            public Rect Rect;

            /// <summary>BitReader for the group substream (may be <c>null</c> when <see cref="Zerofill"/> is <c>true</c>).</summary>
            public BitReader? Reader;

            /// <summary>Minimum squeeze shift to decode in this pass.</summary>
            public int MinShift;

            /// <summary>Maximum squeeze shift to decode in this pass.</summary>
            public int MaxShift;

            /// <summary>Modular substream identifier.</summary>
            public ModularStreamId Stream;

            /// <summary>Whether to zero-fill the group instead of reading from <see cref="Reader"/>.</summary>
            public bool Zerofill;

            /// <summary>Per-frame passes decoder state.</summary>
            public PassesDecoderState? DecState;

            /// <summary>Render pipeline input buffers for this group.</summary>
            public RenderPipelineInput? RenderPipelineInput;

            /// <summary>Whether truncated group bitstreams are allowed.</summary>
            public bool AllowTruncated;

            /// <summary>Optional output flag receiving whether the render pipeline should run for this group.</summary>
            public bool* ShouldRunPipeline;
        }

        private struct ColorCopyParams
        {
            public required FrameHeader FrameHeader;
            public required ImageMetadata Metadata;
            public required ModularImage Gi;
            public required PassesDecoderState? DecState;
            public required RenderPipelineInput RenderPipelineInput;
            public Rect ModularRect;
            public ThreadPool? Pool;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModularFrameDecoder"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public ModularFrameDecoder(JxlMemoryManager? memoryManager = null)
        {
            _memoryManager = memoryManager ?? JxlMemoryManager.Default;
            _fullImage     = new ModularImage(_memoryManager);
        }

        /// <summary>
        /// Gets the memory manager associated with this decoder.
        /// </summary>
        public JxlMemoryManager MemoryManager => _memoryManager;

        /// <summary>
        /// Gets a value indicating whether global DC or squeeze information is present.
        /// </summary>
        public bool HaveDc() => _haveSomething;

        /// <summary>
        /// Gets a value indicating whether full-frame Modular buffering is active.
        /// </summary>
        public bool UsesFullImage() => _useFullImage;

        /// <summary>
        /// Initializes the decoder with frame dimensions.
        /// </summary>
        /// <param name="frameDim">Frame dimensions.</param>
        public void Init(FrameDimensions frameDim)
        {
            _frameDim = frameDim;
        }

        /// <summary>
        /// Creates a <see cref="GlobalModularContext"/> wrapping the global MA tree and ANS histograms.
        /// </summary>
        /// <returns>A configured <see cref="GlobalModularContext"/>.</returns>
        public GlobalModularContext CreateGlobalModularContext()
        {
            return new GlobalModularContext
            {
                Tree           = _tree,
                Code           = _code,
                ContextMap     = _contextMap,
                UndoTransforms = true,
            };
        }

        /// <summary>
        /// Drops the full-frame Modular image plane buffers if global transforms and progressive passes do not require retaining them.
        /// Ported from <c>jxl::ModularFrameDecoder::MaybeDropFullImage</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        public void MaybeDropFullImage()
        {
            if (_fullImage.Transforms.Count == 0 && !_haveSomething && _allSameShift)
            {
                _useFullImage = false;

                for (int i = 0; i < _fullImage.Channels.Count; i++)
                {
                    // keep metadata on channels around, but dealloc their planes
                    _fullImage.Channels[i].Plane.Dispose();
                    _fullImage.Channels[i].Plane = new ImagePlane<int>();
                }
            }
        }

        /// <summary>
        /// Decodes global Modular metadata, global MA tree/histograms, and global transforms/meta-channels.
        /// Ported from <c>jxl::ModularFrameDecoder::DecodeGlobalInfo</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="allowTruncatedGroup">Whether truncated group bitstreams are allowed.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeGlobalInfo(BitReader reader, FrameHeader frameHeader, bool allowTruncatedGroup)
        {
            bool decodeColor       = frameHeader.Encoding == FrameEncoding.Modular;
            ImageMetadata metadata = frameHeader.NonserializedMetadata!.M;
            bool isGray            = metadata.ColorEncoding.IsGray();
            nuint nbChans          = (isGray && frameHeader.ColorTransform == ColorTransform.None) ? 1u : 3u;
            _doColor               = decodeColor;
            nuint nbExtra          = (nuint)metadata.ExtraChannelInfo.Count;

            Status treeOk = DecodeGlobalTreeIfPresent(reader, nbChans, nbExtra, allowTruncatedGroup);

            if (!treeOk.IsOk)
            {
                return treeOk;
            }

            if (!_doColor)
            {
                nbChans = 0;
            }

            Status bitDepthOk = ValidateColorBitDepth(frameHeader, metadata);

            if (!bitDepthOk.IsOk)
            {
                return bitDepthOk;
            }

            StatusOr<ModularImage> giOr = CreateAndConfigureGlobalImage(frameHeader, metadata, nbChans, nbExtra);

            if (!giOr.IsOk)
            {
                return giOr.Status;
            }

            ModularImage gi        = giOr.Value;
            ModularOptions options = new ModularOptions
            {
                MaxChanSize = _frameDim.GroupDim,
                GroupDim    = _frameDim.GroupDim,
            };

            GlobalModularContext globalCtx = new GlobalModularContext
            {
                Tree                = _tree,
                Code                = _code,
                ContextMap          = _contextMap,
                AllowTruncatedGroup = allowTruncatedGroup,
                UndoTransforms      = false,
            };

            Status decStatus = ModularEncoding.ModularGenericDecompress(
                reader,
                gi,
                _globalHeader,
                ModularStreamId.Global().ID(_frameDim),
                options,
                globalCtx);

            if (!allowTruncatedGroup && !decStatus.IsOk)
            {
                gi.Dispose();
                return decStatus;
            }

            if (decStatus.IsFatalError())
            {
                gi.Dispose();
                return Status.Failure("Failed to decode global modular info");
            }

            ClassifyDecodedGlobalImage(gi);

            return decStatus;
        }

        /// <summary>
        /// Decodes a Modular DC or AC group into either <c>_fullImage</c> or directly into <see cref="RenderPipelineInput"/>.
        /// Ported from <c>jxl::ModularFrameDecoder::DecodeGroup</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="args">Group decoding arguments.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeGroup(in DecodeGroupArgs args)
        {
            Debug.Assert(args.Stream.Kind == ModularStreamId.StreamKind.ModularDC ||
                         args.Stream.Kind == ModularStreamId.StreamKind.ModularAC);

            nuint xsize = (nuint)args.Rect.XSize;
            nuint ysize = (nuint)args.Rect.YSize;

            StatusOr<ModularImage> giOr = ModularImage.Create(_memoryManager, xsize, ysize, _fullImage.BitDepth, 0);

            if (!giOr.IsOk)
            {
                return giOr.Status;
            }

            using ModularImage gi = giOr.Value;
            int beginc            = FindFirstGroupChannelIndex();

            Status buildOk = BuildOrZeroFillGroupChannels(in args, beginc, gi);

            if (!buildOk.IsOk)
            {
                return buildOk;
            }

            if (args.Zerofill && _useFullImage)
            {
                return true;
            }

            if (gi.Channels.Count == 0)
            {
                NotifyEmptyGroupChannelList(in args);
                return true;
            }

            if (!args.Zerofill)
            {
                Status decOk = DecompressGroupImage(in args, gi);

                if (!decOk.IsOk)
                {
                    return decOk;
                }
            }

            return FinishDecodedGroup(in args, beginc, gi);
        }

        /// <summary>
        /// Decodes a VarDCT DC group and dequantizes it into <c>decState.SharedStorage.DcStorage</c>.
        /// Ported from <c>jxl::ModularFrameDecoder::DecodeVarDCTDC</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="groupId">DC group index.</param>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="decState">Passes decoder state.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeVarDCTDC(
            FrameHeader frameHeader,
            nuint groupId,
            BitReader reader,
            PassesDecoderState decState)
        {
            JxlMemoryManager mm = decState.MemoryManager;
            Rect r              = decState.Shared.FrameDim.DCGroupRect(groupId);

            StatusOr<ModularImage> imageOr = ModularImage.Create(
                mm,
                (nuint)r.XSize,
                (nuint)r.YSize,
                _fullImage.BitDepth,
                3);

            if (!imageOr.IsOk)
            {
                return imageOr.Status;
            }

            using ModularImage image = imageOr.Value;
            nuint streamId           = ModularStreamId.VarDCTDC(groupId).ID(_frameDim);
            reader.Refill();
            nuint extraPrecision = (nuint)reader.ReadFixedBits(2);
            float mul            = 1.0f / (1 << (int)extraPrecision);

            for (nuint c = 0; c < 3; c++)
            {
                Channel ch   = image.Channels[(int)(c < 2 ? c ^ 1 : c)];
                ch.W       >>= (int)frameHeader.ChromaSubsampling.HShift(c);
                ch.H       >>= (int)frameHeader.ChromaSubsampling.VShift(c);
                Status shOk  = ch.Shrink();

                if (!shOk.IsOk)
                {
                    return shOk;
                }
            }

            ModularOptions options         = new ModularOptions();
            GlobalModularContext globalCtx = new GlobalModularContext
            {
                Tree           = _tree,
                Code           = _code,
                ContextMap     = _contextMap,
                UndoTransforms = true,
            };

            if (!ModularEncoding.ModularGenericDecompress(reader, image, header: null, streamId, options, globalCtx).IsOk)
            {
                return Status.Failure($"Failed to decode VarDCT DC group (DC group id {groupId})");
            }

            RunDequantDC(frameHeader, in r, image, mul, decState);

            return true;
        }

        /// <summary>
        /// Decodes VarDCT AC metadata (YToX/YToB color correlation, AC strategy, raw quant field, EPF sharpness) for a DC group.
        /// Ported from <c>jxl::ModularFrameDecoder::DecodeAcMetadata</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="groupId">DC group index.</param>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="decState">Passes decoder state.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeAcMetadata(
            FrameHeader frameHeader,
            nuint groupId,
            BitReader reader,
            PassesDecoderState decState)
        {
            JxlMemoryManager mm = decState.MemoryManager;
            Rect r              = decState.Shared.FrameDim.DCGroupRect(groupId);
            nuint upperBound    = (nuint)r.XSize * (nuint)r.YSize;

            reader.Refill();
            nuint count    = (nuint)reader.ReadBits((nuint)Bits.CeilLog2Nonzero(upperBound)) + 1u;
            nuint streamId = ModularStreamId.ACMetadata(groupId).ID(_frameDim);

            Rect cr = new Rect(
                r.X0 >> 3,
                r.Y0 >> 3,
                (r.XSize + 7) >> 3,
                (r.YSize + 7) >> 3);

            StatusOr<ModularImage> imageOr = CreateAcMetadataImage(mm, in r, in cr, count);

            if (!imageOr.IsOk)
            {
                return imageOr.Status;
            }

            using ModularImage image       = imageOr.Value;
            ModularOptions options         = new ModularOptions();
            GlobalModularContext globalCtx = new GlobalModularContext
            {
                Tree           = _tree,
                Code           = _code,
                ContextMap     = _contextMap,
                UndoTransforms = true,
            };

            if (!ModularEncoding.ModularGenericDecompress(reader, image, header: null, streamId, options, globalCtx).IsOk)
            {
                return Status.Failure("Failed to decode AC metadata");
            }

            ConvertPlaneAndClamp(new Rect(image.Channels[0].Plane), image.Channels[0].Plane, in cr, decState.SharedStorage.Cmap.YtoXMap!);
            ConvertPlaneAndClamp(new Rect(image.Channels[1].Plane), image.Channels[1].Plane, in cr, decState.SharedStorage.Cmap.YtoBMap!);

            return UnpackControlFieldsFromAcMetadata(frameHeader, in r, count, image, decState);
        }

        /// <summary>
        /// Decodes a raw quantization table encoded as a 3-channel Modular image.
        /// Ported from <c>jxl::ModularFrameDecoder::DecodeQuantTable</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager for temporary allocation.</param>
        /// <param name="requiredSizeX">Required width of the quantization table.</param>
        /// <param name="requiredSizeY">Required height of the quantization table.</param>
        /// <param name="br">Source bit reader.</param>
        /// <param name="encoding">Target <see cref="QuantEncoding"/> to populate.</param>
        /// <param name="idx">Quantization table index (<c>0..16</c>).</param>
        /// <param name="modularFrameDecoder">Optional active <see cref="ModularFrameDecoder"/> providing global tree/histograms.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeQuantTable(
            JxlMemoryManager? memoryManager,
            nuint requiredSizeX,
            nuint requiredSizeY,
            BitReader br,
            QuantEncoding encoding,
            nuint idx,
            ModularFrameDecoder? modularFrameDecoder)
        {
            Status f16Ok = F16Coder.Read(br, out encoding.QrawTableDen);

            if (!f16Ok.IsOk)
            {
                return f16Ok;
            }

            if (encoding.QrawTableDen < 1e-8f)
            {
                // qtable[] values are already checked for <= 0 so the denominator may not
                // be negative.
                return Status.Failure("Invalid qtable_den: value too small");
            }

            StatusOr<ModularImage> imageOr = ModularImage.Create(memoryManager, requiredSizeX, requiredSizeY, 8, 3);

            if (!imageOr.IsOk)
            {
                return imageOr.Status;
            }

            using ModularImage image = imageOr.Value;
            ModularOptions options   = new ModularOptions();
            Status decOk             = DecompressQuantTableImage(br, image, options, idx, modularFrameDecoder);

            if (!decOk.IsOk)
            {
                return decOk;
            }

            return CopyDecodedQuantTable(image, requiredSizeX, requiredSizeY, encoding);
        }

        /// <summary>
        /// Finalizes Modular frame decoding when full-image transforms or progressive passes are used,
        /// undoing global transforms and feeding groups into the render pipeline.
        /// Ported from <c>jxl::ModularFrameDecoder::FinalizeDecoding</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="decState">Passes decoder state.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="inplace">Whether transforms may be undone in-place in <c>_fullImage</c>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status FinalizeDecoding(
            FrameHeader frameHeader,
            PassesDecoderState decState,
            ThreadPool? pool,
            bool inplace)
        {
            if (!_useFullImage)
            {
                return true;
            }

            ModularImage gi = _fullImage;
            bool ownsGi     = false;

            if (!inplace)
            {
                StatusOr<ModularImage> cloneOr = ModularImage.Clone(_fullImage);

                if (!cloneOr.IsOk)
                {
                    return cloneOr.Status;
                }

                gi     = cloneOr.Value;
                ownsGi = true;
            }

            try
            {
                nuint xsize = gi.W;
                nuint ysize = gi.H;

                // Don't use threads if total image size is smaller than a group
                if (xsize * ysize < _frameDim.GroupDim * _frameDim.GroupDim)
                {
                    pool = null;
                }

                // Undo the global transforms
                gi.UndoTransforms(_globalHeader.WpHeader, pool);

                if (_globalTransform.Count != 0 || gi.Error)
                {
                    return Status.Failure("Undoing transforms failed");
                }

                return DispatchFinalizedGroupsToPipeline(frameHeader, decState, pool, gi);
            }
            finally
            {
                if (ownsGi)
                {
                    gi.Dispose();
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _fullImage.Dispose();
        }

        /// <summary>
        /// Converts custom <paramref name="bits"/>-bit floating-point values (with <paramref name="expBits"/> exponent bits)
        /// stored as 32-bit integers back into IEEE 754 binary32 floats.
        /// Ported from <c>jxl::int_to_float</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        internal static Status IntToFloat(
            int* rowIn,
            float* rowOut,
            nuint xsize,
            int bits,
            int expBits)
        {
            if (bits == 32)
            {
                if (expBits != 8)
                {
                    return Status.Failure("exp_bits == 8 required for 32-bit float");
                }

                nuint byteCount = xsize * sizeof(float);
                Buffer.MemoryCopy(rowIn, rowOut, byteCount, byteCount);

                return true;
            }

            int expBias   = (1 << (expBits - 1)) - 1;
            int signShift = bits - 1;
            int mantBits  = bits - expBits - 1;
            int mantShift = 23 - mantBits;

            for (nuint x = 0; x < xsize; ++x)
            {
                Status s = ConvertCustomFloatSample((uint)rowIn[x], signShift, mantBits, mantShift, expBits, expBias, out rowOut[x]);

                if (!s.IsOk)
                {
                    return s;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Status ConvertCustomFloatSample(
            uint f,
            int signShift,
            int mantBits,
            int mantShift,
            int expBits,
            int expBias,
            out float result)
        {
            int signbit = (int)(f >> signShift);
            f          &= (1u << signShift) - 1u;

            if (f == 0)
            {
                result = signbit != 0 ? -0.0f : 0.0f;

                return true;
            }

            int exp      = (int)(f >> mantBits);
            int mantissa = (int)(f & ((1u << mantBits) - 1u));

            if (exp == (1 << expBits) - 1)
            {
                // NaN or infinity
                uint nanInf  = signbit != 0 ? 0x80000000u : 0u;
                nanInf      |= 0b11111111u << 23;
                nanInf      |= (uint)mantissa << mantShift;
                result       = BitConverter.UInt32BitsToSingle(nanInf);

                return true;
            }

            mantissa <<= mantShift;

            // Try to normalize only if there is space for maneuver.
            if (exp == 0 && expBits < 8)
            {
                // subnormal number
                while ((mantissa & 0x800000) == 0)
                {
                    mantissa <<= 1;
                    exp--;
                }

                exp++;
                // remove leading 1 because it is implicit now
                mantissa &= 0x7fffff;
            }

            exp -= expBias;
            exp += 127;

            if (exp < 0)
            {
                result = 0.0f;
                return Status.Failure("Invalid float exponent");
            }

            uint bitsOut = (signbit != 0 ? 0x80000000u : 0u) | ((uint)exp << 23) | (uint)mantissa;
            result       = BitConverter.UInt32BitsToSingle(bitsOut);

            return true;
        }

        private Status DecodeGlobalTreeIfPresent(
            BitReader reader,
            nuint nbChans,
            nuint nbExtra,
            bool allowTruncatedGroup)
        {
            bool hasTree = reader.ReadBits(1) != 0;

            if (!allowTruncatedGroup ||
                reader.TotalBitsConsumed() < reader.TotalBytes() * Common.BitsPerByte)
            {
                if (hasTree)
                {
                    nuint treeSizeLimit = Math.Min(
                        (nuint)(1u << 22),
                        1024u + ((_frameDim.XSize * _frameDim.YSize * (nbChans + nbExtra)) / 16u));

                    Status treeOk = ModularMa.DecodeTree(_memoryManager, reader, _tree, treeSizeLimit);

                    if (!treeOk.IsOk)
                    {
                        return treeOk;
                    }

                    Status histOk = DecAns.DecodeHistograms(
                        _memoryManager,
                        reader,
                        (nuint)((_tree.Count + 1) / 2),
                        _code,
                        out _contextMap);

                    if (!histOk.IsOk)
                    {
                        return histOk;
                    }
                }
            }

            return true;
        }

        private Status ValidateColorBitDepth(FrameHeader frameHeader, ImageMetadata metadata)
        {
            bool fp = metadata.BitDepth.FloatingPointSample;

            // bits_per_sample is just metadata for XYB images.
            if (metadata.BitDepth.BitsPerSample >= 32 &&
                _doColor &&
                frameHeader.ColorTransform != ColorTransform.XYB)
            {
                if (metadata.BitDepth.BitsPerSample == 32 && !fp)
                {
                    return Status.Failure("uint32_t not supported in dec_modular");
                }

                if (metadata.BitDepth.BitsPerSample > 32)
                {
                    return Status.Failure("bits_per_sample > 32 not supported");
                }
            }

            return true;
        }

        private StatusOr<ModularImage> CreateAndConfigureGlobalImage(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            nuint nbChans,
            nuint nbExtra)
        {
            StatusOr<ModularImage> giOr = ModularImage.Create(
                _memoryManager,
                _frameDim.XSize,
                _frameDim.YSize,
                (int)metadata.BitDepth.BitsPerSample,
                (int)(nbChans + nbExtra));

            if (!giOr.IsOk)
            {
                return giOr.Status;
            }

            ModularImage gi = giOr.Value;
            _allSameShift   = true;

            if (frameHeader.ColorTransform == ColorTransform.YCbCr)
            {
                Status ycbcrOk = ConfigureGlobalYCbCrChannels(frameHeader, gi, nbChans);

                if (!ycbcrOk.IsOk)
                {
                    gi.Dispose();
                    return ycbcrOk;
                }
            }

            Status ecOk = ConfigureGlobalExtraChannels(frameHeader, gi, nbChans, nbExtra);

            if (!ecOk.IsOk)
            {
                gi.Dispose();
                return ecOk;
            }

            return gi;
        }

        private Status ConfigureGlobalYCbCrChannels(FrameHeader frameHeader, ModularImage gi, nuint nbChans)
        {
            for (nuint c = 0; c < nbChans; c++)
            {
                Channel ch       = gi.Channels[(int)c];
                ch.HShift        = (int)frameHeader.ChromaSubsampling.HShift(c);
                ch.VShift        = (int)frameHeader.ChromaSubsampling.VShift(c);
                nuint xsizeShift = Common.DivCeil(_frameDim.XSize, 1u << ch.HShift);
                nuint ysizeShift = Common.DivCeil(_frameDim.YSize, 1u << ch.VShift);
                Status shOk      = ch.Shrink((int)xsizeShift, (int)ysizeShift);

                if (!shOk.IsOk)
                {
                    return shOk;
                }

                if (ch.HShift != gi.Channels[0].HShift || ch.VShift != gi.Channels[0].VShift)
                {
                    _allSameShift = false;
                }
            }

            return true;
        }

        private Status ConfigureGlobalExtraChannels(
            FrameHeader frameHeader,
            ModularImage gi,
            nuint nbChans,
            nuint nbExtra)
        {
            for (nuint ec = 0, c = nbChans; ec < nbExtra; ec++, c++)
            {
                nuint ecups = frameHeader.ExtraChannelUpsampling[(int)ec];
                Channel ch  = gi.Channels[(int)c];
                Status shOk = ch.Shrink(
                    (int)Common.DivCeil(_frameDim.XSizeUpsampled, ecups),
                    (int)Common.DivCeil(_frameDim.YSizeUpsampled, ecups));

                if (!shOk.IsOk)
                {
                    return shOk;
                }

                int shift = (int)Bits.CeilLog2Nonzero(ecups) - (int)Bits.CeilLog2Nonzero(frameHeader.Upsampling);
                ch.HShift = shift;
                ch.VShift = shift;

                if (ch.HShift != gi.Channels[0].HShift || ch.VShift != gi.Channels[0].VShift)
                {
                    _allSameShift = false;
                }
            }

            return true;
        }

        private void ClassifyDecodedGlobalImage(ModularImage gi)
        {
            _haveSomething = false;

            for (int c = 0; c < gi.Channels.Count; c++)
            {
                Channel gic = gi.Channels[c];

                if ((nuint)c >= gi.NbMetaChannels &&
                    gic.W <= _frameDim.GroupDim &&
                    gic.H <= _frameDim.GroupDim)
                {
                    _haveSomething = true;
                }
            }

            // move global transforms to groups if possible
            if (!_haveSomething && _allSameShift)
            {
                if (gi.Transforms.Count == 1 && gi.Transforms[0].Id == TransformId.RCT)
                {
                    _globalTransform = new List<Transform>(gi.Transforms);
                    gi.Transforms.Clear();
                }
            }

            _fullImage.Dispose();
            _fullImage = gi;
        }

        private int FindFirstGroupChannelIndex()
        {
            int c = (int)_fullImage.NbMetaChannels;

            for (; c < _fullImage.Channels.Count; c++)
            {
                Channel fc = _fullImage.Channels[c];

                if (fc.W > _frameDim.GroupDim || fc.H > _frameDim.GroupDim)
                {
                    break;
                }
            }

            return c;
        }

        private Status BuildOrZeroFillGroupChannels(in DecodeGroupArgs args, int beginc, ModularImage gi)
        {
            for (int c = beginc; c < _fullImage.Channels.Count; c++)
            {
                Channel fc = _fullImage.Channels[c];
                int shift  = Math.Min(fc.HShift, fc.VShift);

                if (shift > args.MaxShift || shift < args.MinShift)
                {
                    continue;
                }

                Rect r = new Rect(
                    args.Rect.X0 >> fc.HShift,
                    args.Rect.Y0 >> fc.VShift,
                    args.Rect.XSize >> fc.HShift,
                    args.Rect.YSize >> fc.VShift,
                    (int)fc.W,
                    (int)fc.H);

                if (r.XSize == 0 || r.YSize == 0)
                {
                    continue;
                }

                if (args.Zerofill && _useFullImage)
                {
                    for (nuint y = 0; y < (nuint)r.YSize; ++y)
                    {
                        int* rowOut = r.Row(fc.Plane, y);
                        new Span<int>(rowOut, r.XSize).Clear();
                    }
                }
                else
                {
                    StatusOr<Channel> gcOr = Channel.Create(_memoryManager, (nuint)r.XSize, (nuint)r.YSize, fc.HShift, fc.VShift);

                    if (!gcOr.IsOk)
                    {
                        return gcOr.Status;
                    }

                    Channel gc = gcOr.Value;

                    if (args.Zerofill)
                    {
                        ImageOps.ZeroFillImage(gc.Plane);
                    }

                    gi.Channels.Add(gc);
                }
            }

            return true;
        }

        private void NotifyEmptyGroupChannelList(in DecodeGroupArgs args)
        {
            if (args.DecState != null && args.ShouldRunPipeline != null)
            {
                CodecMetadata metadata = args.FrameHeader.NonserializedMetadata!;

                if (_doColor || metadata.M.NumExtraChannels > 0)
                {
                    // Signal to FrameDecoder that we do not have some of the required input
                    // for the render pipeline.
                    *args.ShouldRunPipeline = false;
                }
            }
        }

        private Status DecompressGroupImage(in DecodeGroupArgs args, ModularImage gi)
        {
            ModularOptions options         = new ModularOptions();
            GlobalModularContext globalCtx = new GlobalModularContext
            {
                Tree                = _tree,
                Code                = _code,
                ContextMap          = _contextMap,
                AllowTruncatedGroup = args.AllowTruncated,
                UndoTransforms      = true,
            };

            Status status = ModularEncoding.ModularGenericDecompress(
                args.Reader!,
                gi,
                header: null,
                args.Stream.ID(_frameDim),
                options,
                globalCtx);

            if (!args.AllowTruncated && !status.IsOk)
            {
                return status;
            }

            if (status.IsFatalError())
            {
                return status;
            }

            return true;
        }

        private Status FinishDecodedGroup(in DecodeGroupArgs args, int beginc, ModularImage gi)
        {
            // Undo global transforms that have been pushed to the group level
            if (!_useFullImage)
            {
                if (args.RenderPipelineInput == null)
                {
                    return Status.Failure("render_pipeline_input required when use_full_image is false");
                }

                for (int i = 0; i < _globalTransform.Count; i++)
                {
                    Status invOk = _globalTransform[i].Inverse(gi, _globalHeader.WpHeader);

                    if (!invOk.IsOk)
                    {
                        return invOk;
                    }
                }

                return ModularImageToDecodedRect(
                    args.FrameHeader,
                    gi,
                    args.DecState,
                    pool: null,
                    args.RenderPipelineInput,
                    new Rect(0, 0, (int)gi.W, (int)gi.H));
            }

            int gic = 0;

            for (int c = beginc; c < _fullImage.Channels.Count; c++)
            {
                Channel fc = _fullImage.Channels[c];
                int shift  = Math.Min(fc.HShift, fc.VShift);

                if (shift > args.MaxShift || shift < args.MinShift)
                {
                    continue;
                }

                Rect r = new Rect(
                    args.Rect.X0 >> fc.HShift,
                    args.Rect.Y0 >> fc.VShift,
                    args.Rect.XSize >> fc.HShift,
                    args.Rect.YSize >> fc.VShift,
                    (int)fc.W,
                    (int)fc.H);

                if (r.XSize == 0 || r.YSize == 0)
                {
                    continue;
                }

                Status copyOk = ImageOps.CopyImageTo(
                    new Rect(0, 0, r.XSize, r.YSize),
                    gi.Channels[gic].Plane,
                    r,
                    fc.Plane);

                if (!copyOk.IsOk)
                {
                    return copyOk;
                }

                gic++;
            }

            return true;
        }

        private static void RunDequantDC(
            FrameHeader frameHeader,
            in Rect r,
            ModularImage image,
            float mul,
            PassesDecoderState decState)
        {
            CompressedDc.DequantDC(
                in r,
                decState.SharedStorage.DcStorage,
                decState.SharedStorage.QuantDc,
                image,
                decState.Shared.Quantizer.MulDC(),
                mul,
                decState.Shared.Cmap.Base.DCFactors(),
                frameHeader.ChromaSubsampling,
                decState.Shared.BlockCtxMap);
        }

        private StatusOr<ModularImage> CreateAcMetadataImage(
            JxlMemoryManager mm,
            in Rect r,
            in Rect cr,
            nuint count)
        {
            // YToX, YToB, ACS + QF, EPF
            StatusOr<ModularImage> imageOr = ModularImage.Create(
                mm,
                (nuint)r.XSize,
                (nuint)r.YSize,
                _fullImage.BitDepth,
                4);

            if (!imageOr.IsOk)
            {
                return imageOr.Status;
            }

            ModularImage image      = imageOr.Value;
            StatusOr<Channel> ch0Or = Channel.Create(mm, (nuint)cr.XSize, (nuint)cr.YSize, 3, 3);
            StatusOr<Channel> ch1Or = ch0Or.IsOk ? Channel.Create(mm, (nuint)cr.XSize, (nuint)cr.YSize, 3, 3) : ch0Or.Status;
            StatusOr<Channel> ch2Or = ch1Or.IsOk ? Channel.Create(mm, count, 2, 0, 0) : ch1Or.Status;

            if (!ch2Or.IsOk)
            {
                if (ch0Or.IsOk)
                {
                    ch0Or.Value.Dispose();
                }

                if (ch1Or.IsOk)
                {
                    ch1Or.Value.Dispose();
                }

                image.Dispose();

                return ch2Or.Status;
            }

            image.Channels[0].Dispose();
            image.Channels[1].Dispose();
            image.Channels[2].Dispose();
            image.Channels[0] = ch0Or.Value;
            image.Channels[1] = ch1Or.Value;
            image.Channels[2] = ch2Or.Value;

            return image;
        }

        private static void ConvertPlaneAndClamp(
            Rect rectFrom,
            ImagePlane<int> from,
            in Rect rectTo,
            ImagePlane<sbyte> to)
        {
            Debug.Assert(rectFrom.IsInside(from));
            Debug.Assert(rectTo.IsInside(to));
            Debug.Assert(rectFrom.XSize == rectTo.XSize);
            Debug.Assert(rectFrom.YSize == rectTo.YSize);

            for (nuint y = 0; y < (nuint)rectTo.YSize; y++)
            {
                int* rowFrom = rectFrom.ConstRow(from, y);
                sbyte* rowTo = rectTo.Row(to, y);

                for (nuint x = 0; x < (nuint)rectTo.XSize; x++)
                {
                    rowTo[x] = (sbyte)Math.Clamp(rowFrom[x], sbyte.MinValue, sbyte.MaxValue);
                }
            }
        }

        private static Status UnpackControlFieldsFromAcMetadata(
            FrameHeader frameHeader,
            in Rect r,
            nuint count,
            ModularImage image,
            PassesDecoderState decState)
        {
            nuint num             = 0;
            bool is444            = frameHeader.ChromaSubsampling.Is444();
            AcStrategyImage acs   = decState.SharedStorage.AcStrategy;
            nuint xlim            = Math.Min(acs.xsize, (nuint)(r.X0 + r.XSize));
            nuint ylim            = Math.Min(acs.ysize, (nuint)(r.Y0 + r.YSize));
            uint localUsedAcs     = 0;
            int* rowIn1           = image.Channels[2].Plane.Row(0);
            int* rowIn2           = image.Channels[2].Plane.Row(1);

            for (nuint iy = 0; iy < (nuint)r.YSize; iy++)
            {
                nuint y       = (nuint)r.Y0 + iy;
                int* rowQf    = r.Row(decState.SharedStorage.RawQuantField, iy);
                byte* rowEpf  = r.Row(decState.SharedStorage.EpfSharpness, iy);
                int* rowIn3   = image.Channels[3].Plane.Row(iy);

                for (nuint ix = 0; ix < (nuint)r.XSize; ix++)
                {
                    int sharpness = rowIn3[ix];

                    if (sharpness < 0 || sharpness >= LoopFilter.KEpfSharpEntries)
                    {
                        return Status.Failure("Corrupted sharpness field");
                    }

                    rowEpf[ix] = (byte)sharpness;
                    nuint x    = (nuint)r.X0 + ix;

                    if (acs.IsValid(x, y))
                    {
                        continue;
                    }

                    if (num >= count)
                    {
                        return Status.Failure("Corrupted stream");
                    }

                    Status blkOk = ValidateAndSetBlockAcStrategy(
                        acs,
                        x,
                        y,
                        xlim,
                        ylim,
                        is444,
                        rowIn1[num],
                        ref localUsedAcs);

                    if (!blkOk.IsOk)
                    {
                        return blkOk;
                    }

                    rowQf[ix] = 1 + Math.Clamp(rowIn2[num], 0, Quantizer.kQuantMax - 1);
                    num++;
                }
            }

            decState.OrUsedAcs(localUsedAcs);

            if (frameHeader.LoopFilter.EpfIters > 0)
            {
                return Epf.ComputeSigma(
                    frameHeader.LoopFilter,
                    in r,
                    decState.Shared.AcStrategy,
                    decState.Shared.Quantizer.Scale(),
                    decState.Shared.RawQuantField,
                    decState.Shared.EpfSharpness,
                    decState.Sigma);
            }

            return true;
        }

        private static Status ValidateAndSetBlockAcStrategy(
            AcStrategyImage acStrategy,
            nuint x,
            nuint y,
            nuint xlim,
            nuint ylim,
            bool is444,
            int rawStrategy,
            ref uint localUsedAcs)
        {
            if (!AcStrategy.IsRawStrategyValid(rawStrategy))
            {
                return Status.Failure("Invalid AC strategy");
            }

            localUsedAcs  |= 1u << rawStrategy;
            AcStrategy acs = AcStrategy.FromRawStrategy((byte)rawStrategy);

            if ((acs.CoveredBlocksX() > 1 || acs.CoveredBlocksY() > 1) && !is444)
            {
                return Status.Failure("AC strategy not compatible with chroma subsampling");
            }

            // Ensure that blocks do not overflow *AC* groups.
            nuint nextXAcBlock  = ((x / FrameDimensions.KGroupDimInBlocks) + 1u) * FrameDimensions.KGroupDimInBlocks;
            nuint nextYAcBlock  = ((y / FrameDimensions.KGroupDimInBlocks) + 1u) * FrameDimensions.KGroupDimInBlocks;
            nuint nextXDctBlock = x + acs.CoveredBlocksX();
            nuint nextYDctBlock = y + acs.CoveredBlocksY();

            if (nextXDctBlock > nextXAcBlock || nextXDctBlock > xlim)
            {
                return Status.Failure("Invalid AC strategy, x overflow");
            }

            if (nextYDctBlock > nextYAcBlock || nextYDctBlock > ylim)
            {
                return Status.Failure("Invalid AC strategy, y overflow");
            }

            return acStrategy.SetNoBoundsCheck(x, y, (AcStrategyType)rawStrategy);
        }

        private static Status DecompressQuantTableImage(
            BitReader br,
            ModularImage image,
            ModularOptions options,
            nuint idx,
            ModularFrameDecoder? modularFrameDecoder)
        {
            if (modularFrameDecoder != null)
            {
                StatusOr<ModularStreamId> qtOr = ModularStreamId.QuantTable(idx);

                if (!qtOr.IsOk)
                {
                    return qtOr.Status;
                }

                GlobalModularContext globalCtx = new GlobalModularContext
                {
                    Tree           = modularFrameDecoder._tree,
                    Code           = modularFrameDecoder._code,
                    ContextMap     = modularFrameDecoder._contextMap,
                    UndoTransforms = true,
                };

                return ModularEncoding.ModularGenericDecompress(
                    br,
                    image,
                    header: null,
                    qtOr.Value.ID(modularFrameDecoder._frameDim),
                    options,
                    globalCtx);
            }

            return ModularEncoding.ModularGenericDecompress(
                br,
                image,
                header: null,
                0,
                options,
                new GlobalModularContext { UndoTransforms = true });
        }

        private static Status CopyDecodedQuantTable(
            ModularImage image,
            nuint requiredSizeX,
            nuint requiredSizeY,
            QuantEncoding encoding)
        {
            int totalSize = (int)(requiredSizeX * requiredSizeY * 3);

            if (encoding.QrawTable == null)
            {
                encoding.QrawTable = new int[totalSize];
            }
            else if (encoding.QrawTable.Length != totalSize)
            {
                return Status.Failure("Quant table size mismatch");
            }

            int[] qtable  = encoding.QrawTable;
            int planeSize = (int)(requiredSizeX * requiredSizeY);
            int width     = (int)requiredSizeX;

            for (int c = 0; c < 3; c++)
            {
                for (nuint y = 0; y < requiredSizeY; y++)
                {
                    int* row      = image.Channels[c].Plane.Row(y);
                    int rowOffset = (c * planeSize) + ((int)y * width);

                    for (int x = 0; x < width; x++)
                    {
                        int val               = row[x];
                        qtable[rowOffset + x] = val;

                        if (val <= 0)
                        {
                            return Status.Failure("Invalid raw quantization table");
                        }
                    }
                }
            }

            return true;
        }

        private Status DispatchFinalizedGroupsToPipeline(
            FrameHeader frameHeader,
            PassesDecoderState decState,
            ThreadPool? pool,
            ModularImage gi)
        {
            nuint numGroups = decState.Shared.FrameDim.NumGroups;

            for (nuint i = 0; i < numGroups; i++)
            {
                decState.RenderPipeline!.ClearDone(i);
            }

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)numGroups,
                (nuint numThreads) => decState.RenderPipeline!.PrepareForThreads(numThreads, useGroupIds: false),
                (uint groupIndex, nuint threadId) =>
                {
                    RenderPipelineInput input = decState.RenderPipeline!.GetInputBuffers(groupIndex, threadId);
                    Status copyOk             = ModularImageToDecodedRect(
                        frameHeader,
                        gi,
                        decState,
                        pool: null,
                        input,
                        decState.Shared.FrameDim.GroupRect(groupIndex));

                    if (!copyOk.IsOk)
                    {
                        return copyOk;
                    }

                    return input.Done();
                },
                "ModularToRect");
        }

        /// <summary>
        /// Converts decoded 32-bit signed integer modular channels into floating-point render pipeline buffers.
        /// Ported from <c>jxl::ModularFrameDecoder::ModularImageToDecodedRect</c> in <c>lib/jxl/dec_modular.cc</c>.
        /// </summary>
        public Status ModularImageToDecodedRect(
            FrameHeader frameHeader,
            ModularImage gi,
            PassesDecoderState? decState,
            ThreadPool? pool,
            RenderPipelineInput renderPipelineInput,
            Rect modularRect)
        {
            CodecMetadata metadata = frameHeader.NonserializedMetadata!;

            if (gi.Transforms.Count != 0)
            {
                return Status.Failure("Logic error: transforms still remaining");
            }

            int c = 0;

            if (_doColor)
            {
                ColorCopyParams colorParams = new ColorCopyParams
                {
                    FrameHeader         = frameHeader,
                    Metadata            = metadata.M,
                    Gi                  = gi,
                    DecState            = decState,
                    RenderPipelineInput = renderPipelineInput,
                    ModularRect         = modularRect,
                    Pool                = pool,
                };

                Status colorOk = CopyModularColorChannelsToPipeline(in colorParams, ref c);

                if (!colorOk.IsOk)
                {
                    return colorOk;
                }
            }

            return CopyModularExtraChannelsToPipeline(gi, metadata.M, renderPipelineInput, in modularRect, c);
        }

        private Status CopyModularColorChannelsToPipeline(in ColorCopyParams p, ref int c)
        {
            bool rgbFromGray = p.Metadata.ColorEncoding.IsGray() &&
                               p.FrameHeader.ColorTransform == ColorTransform.None;
            bool fp          = p.Metadata.BitDepth.FloatingPointSample &&
                               p.FrameHeader.ColorTransform != ColorTransform.XYB;

            for (; c < 3; c++)
            {
                double factor = _fullImage.BitDepth < 32
                    ? 1.0 / ((1u << _fullImage.BitDepth) - 1)
                    : 0.0;
                int cIn       = c;

                if (p.FrameHeader.ColorTransform == ColorTransform.XYB)
                {
                    factor = p.DecState!.Shared.Matrices.DCQuants()[c];

                    // XYB is encoded as YX(B-Y)
                    if (c < 2)
                    {
                        cIn = 1 - c;
                    }
                }
                else if (rgbFromGray)
                {
                    cIn = 0;
                }

                if (cIn >= p.Gi.Channels.Count)
                {
                    return Status.Failure("Channel index out of range in ModularImageToDecodedRect");
                }

                Channel chIn = p.Gi.Channels[cIn];

                if (chIn.W == 0 || chIn.H == 0)
                {
                    return Status.Failure("Empty image");
                }

                if (chIn.HShift > 3 || chIn.VShift > 3)
                {
                    return Status.Failure("Invalid channel shift in ModularImageToDecodedRect");
                }

                Status chOk = CopySingleColorChannelRows(in p, chIn, c, factor, rgbFromGray, fp);

                if (!chOk.IsOk)
                {
                    return chOk;
                }

                if (rgbFromGray)
                {
                    break;
                }
            }

            if (rgbFromGray)
            {
                c = 1;
            }

            return true;
        }

        private Status CopySingleColorChannelRows(
            in ColorCopyParams p,
            Channel chIn,
            int c,
            double factor,
            bool rgbFromGray,
            bool fp)
        {
            Rect r  = p.RenderPipelineInput.GetBuffer((nuint)c).Rect;
            Rect mr = new Rect(
                p.ModularRect.X0 >> chIn.HShift,
                p.ModularRect.Y0 >> chIn.VShift,
                (int)Common.DivCeil((nuint)p.ModularRect.XSize, 1u << chIn.HShift),
                (int)Common.DivCeil((nuint)p.ModularRect.YSize, 1u << chIn.VShift));

            mr = mr.Crop(chIn.Plane);

            if (r.YSize != mr.YSize || r.XSize != mr.XSize)
            {
                return Status.Failure("Dimension mismatch in ModularImageToDecodedRect");
            }

            nuint xsizeShifted = (nuint)r.XSize;
            nuint ysizeShifted = (nuint)r.YSize;

            if (p.FrameHeader.ColorTransform == ColorTransform.XYB && c == 2)
            {
                if (fp)
                {
                    return Status.Failure("XYB cannot be floating point sample");
                }

                return CopyXybBlueChannel(in p, chIn, in mr, xsizeShifted, ysizeShifted, (float)factor);
            }

            if (fp)
            {
                return CopyFloatColorChannel(in p, chIn, in mr, c, xsizeShifted, ysizeShifted, rgbFromGray);
            }

            return CopyIntegerColorChannel(in p, chIn, in mr, c, xsizeShifted, ysizeShifted, factor, rgbFromGray);
        }

        private static Status CopyXybBlueChannel(
            in ColorCopyParams p,
            Channel chIn,
            in Rect mr,
            nuint xsizeShifted,
            nuint ysizeShifted,
            float factor)
        {
            RenderPipelineInput input = p.RenderPipelineInput;
            ModularImage gi           = p.Gi;
            Rect mrLocal              = mr;

            return DataParallel.RunOnPool(
                p.Pool,
                0,
                (uint)ysizeShifted,
                ThreadPool.NoInit,
                (uint task, nuint _) =>
                {
                    int* rowIn   = mrLocal.ConstRow(chIn.Plane, task);
                    int* rowInY  = mrLocal.ConstRow(gi.Channels[0].Plane, task);
                    (ImagePlane<float> plane, Rect rect) = input.GetBuffer(2);
                    float* rowOut = rect.Row(plane, task);

                    for (nuint x = 0; x < xsizeShifted; x++)
                    {
                        rowOut[x] = (rowIn[x] + rowInY[x]) * factor;
                    }

                    return Status.OkStatus;
                },
                "ModularIntToFloat");
        }

        private static Status CopyFloatColorChannel(
            in ColorCopyParams p,
            Channel chIn,
            in Rect mr,
            int c,
            nuint xsizeShifted,
            nuint ysizeShifted,
            bool rgbFromGray)
        {
            int bits                  = (int)p.Metadata.BitDepth.BitsPerSample;
            int expBits               = (int)p.Metadata.BitDepth.ExponentBitsPerSample;
            RenderPipelineInput input = p.RenderPipelineInput;
            Rect mrLocal              = mr;

            return DataParallel.RunOnPool(
                p.Pool,
                0,
                (uint)ysizeShifted,
                ThreadPool.NoInit,
                (uint task, nuint _) =>
                {
                    int* rowIn = mrLocal.ConstRow(chIn.Plane, task);

                    if (rgbFromGray)
                    {
                        for (nuint cc = 0; cc < 3; cc++)
                        {
                            (ImagePlane<float> plane, Rect rect) = input.GetBuffer(cc);
                            float* rowOut = rect.Row(plane, task);
                            Status s      = IntToFloat(rowIn, rowOut, xsizeShifted, bits, expBits);

                            if (!s.IsOk)
                            {
                                return s;
                            }
                        }

                        return Status.OkStatus;
                    }

                    (ImagePlane<float> planeC, Rect rectC) = input.GetBuffer((nuint)c);
                    return IntToFloat(rowIn, rectC.Row(planeC, task), xsizeShifted, bits, expBits);
                },
                "ModularIntToFloat_losslessfloat");
        }

        private Status CopyIntegerColorChannel(
            in ColorCopyParams p,
            Channel chIn,
            in Rect mr,
            int c,
            nuint xsizeShifted,
            nuint ysizeShifted,
            double factor,
            bool rgbFromGray)
        {
            RenderPipelineInput input = p.RenderPipelineInput;
            Rect mrLocal              = mr;
            bool fastFloat            = _fullImage.BitDepth < 23;
            float factorF             = (float)factor;

            return DataParallel.RunOnPool(
                p.Pool,
                0,
                (uint)ysizeShifted,
                ThreadPool.NoInit,
                (uint task, nuint _) =>
                {
                    int* rowIn = mrLocal.ConstRow(chIn.Plane, task);

                    if (rgbFromGray)
                    {
                        float* outR = GetBufferRow(input, 0, task);
                        float* outG = GetBufferRow(input, 1, task);
                        float* outB = GetBufferRow(input, 2, task);
                        ConvertGrayRowToRgb(rowIn, outR, outG, outB, xsizeShifted, factor, factorF, fastFloat);
                    }
                    else
                    {
                        float* rowOut = GetBufferRow(input, (nuint)c, task);
                        ConvertSingleRow(rowIn, rowOut, xsizeShifted, factor, factorF, fastFloat);
                    }

                    return Status.OkStatus;
                },
                "ModularIntToFloat");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float* GetBufferRow(RenderPipelineInput input, nuint c, nuint y)
        {
            (ImagePlane<float> plane, Rect rect) = input.GetBuffer(c);

            return rect.Row(plane, y);
        }

        private static void ConvertGrayRowToRgb(
            int* rowIn,
            float* outR,
            float* outG,
            float* outB,
            nuint xsize,
            double factor,
            float factorF,
            bool fastFloat)
        {
            if (fastFloat)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    float v = rowIn[x] * factorF;
                    outR[x] = v;
                    outG[x] = v;
                    outB[x] = v;
                }
            }
            else
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    float v = (float)(rowIn[x] * factor);
                    outR[x] = v;
                    outG[x] = v;
                    outB[x] = v;
                }
            }
        }

        private static void ConvertSingleRow(
            int* rowIn,
            float* rowOut,
            nuint xsize,
            double factor,
            float factorF,
            bool fastFloat)
        {
            if (fastFloat)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    rowOut[x] = rowIn[x] * factorF;
                }
            }
            else
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    rowOut[x] = (float)(rowIn[x] * factor);
                }
            }
        }

        private Status CopyModularExtraChannelsToPipeline(
            ModularImage gi,
            ImageMetadata metadata,
            RenderPipelineInput renderPipelineInput,
            in Rect modularRect,
            int startC)
        {
            int numExtraChannels = (int)metadata.NumExtraChannels;
            bool fastFloat       = _fullImage.BitDepth < 23;

            for (int ec = 0, c = startC; ec < numExtraChannels; ec++, c++)
            {
                ExtraChannelInfo eci = metadata.ExtraChannelInfo[ec];
                int bits             = (int)eci.BitDepth.BitsPerSample;
                int expBits          = (int)eci.BitDepth.ExponentBitsPerSample;
                bool fp              = eci.BitDepth.FloatingPointSample;

                if (!fp && bits >= 32)
                {
                    return Status.Failure("Invalid extra channel bit depth");
                }

                if (c >= gi.Channels.Count)
                {
                    return Status.Failure("Missing extra channel in ModularImage");
                }

                double factor   = fp ? 0.0 : (1.0 / ((1u << bits) - 1));
                float factorF   = (float)factor;
                Channel chIn    = gi.Channels[c];
                (ImagePlane<float> planeEc, Rect r) = renderPipelineInput.GetBuffer((nuint)(3 + ec));

                Rect mr = new Rect(
                    modularRect.X0 >> chIn.HShift,
                    modularRect.Y0 >> chIn.VShift,
                    (int)Common.DivCeil((nuint)modularRect.XSize, 1u << chIn.HShift),
                    (int)Common.DivCeil((nuint)modularRect.YSize, 1u << chIn.VShift));

                mr = mr.Crop(chIn.Plane);

                if (r.YSize != mr.YSize || r.XSize != mr.XSize)
                {
                    return Status.Failure("Extra channel dimension mismatch in ModularImageToDecodedRect");
                }

                for (nuint y = 0; y < (nuint)r.YSize; ++y)
                {
                    float* rowOut = r.Row(planeEc, y);
                    int* rowIn    = mr.ConstRow(chIn.Plane, y);

                    if (fp)
                    {
                        Status s = IntToFloat(rowIn, rowOut, (nuint)r.XSize, bits, expBits);

                        if (!s.IsOk)
                        {
                            return s;
                        }
                    }
                    else
                    {
                        ConvertSingleRow(rowIn, rowOut, (nuint)r.XSize, factor, factorF, fastFloat);
                    }
                }
            }

            return true;
        }
    }
}
