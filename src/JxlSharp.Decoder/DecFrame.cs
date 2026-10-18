// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Represents a single entry in the frame Table of Contents (TOC).
    /// Ported from <c>jxl::FrameDecoder::TocEntry</c> in <c>lib/jxl/dec_frame.h</c>.
    /// </summary>
    public struct TocEntry
    {
        /// <summary>Size of the section in bytes.</summary>
        public nuint Size;

        /// <summary>Byte offset of the section relative to the end of the TOC.</summary>
        public nuint Offset;

        /// <summary>Permuted section identifier.</summary>
        public nuint Id;
    }

    /// <summary>
    /// Represents a single TOC section bit reader and its identifiers passed to <see cref="FrameDecoder.ProcessSections"/>.
    /// Ported from <c>jxl::FrameDecoder::SectionInfo</c> in <c>lib/jxl/dec_frame.h</c>.
    /// </summary>
    public struct SectionInfo
    {
        /// <summary>Bit reader positioned at the start of the section.</summary>
        public BitReader? Br;

        /// <summary>TOC section ID in <c>[0, FrameDecoder.Toc.Length)</c>.</summary>
        public nuint Id;

        /// <summary>Logical index in the frame section sequence before any TOC permutation.</summary>
        public nuint Index;
    }

    /// <summary>
    /// Processing outcome for a single section in <see cref="FrameDecoder.ProcessSections"/>.
    /// Ported from <c>jxl::FrameDecoder::SectionStatus</c> in <c>lib/jxl/dec_frame.h</c>.
    /// </summary>
    public enum SectionStatus
    {
        /// <summary>Processed correctly.</summary>
        Done = 0,

        /// <summary>Skipped because other required sections were not yet processed.</summary>
        Skipped = 1,

        /// <summary>Skipped because not enough data was in the bit reader.</summary>
        Incomplete = 2,

        /// <summary>Partially processed because it stopped at a progressive downsampling target.</summary>
        Partial = 3,

        /// <summary>Skipped because the section was already processed earlier.</summary>
        Duplicate = 4,
    }

    /// <summary>
    /// Defines which progressive stages would cause the decoder to pause and emit a <see cref="JxlDecoderStatus.FrameProgression"/> event.
    /// Ported from <c>JxlProgressiveDetail</c> in <c>lib/include/jxl/decode.h</c>.
    /// </summary>
    public enum JxlProgressiveDetail
    {
        /// <summary>After completed regular frames.</summary>
        Frames = 0,

        /// <summary>After completed DC (1:8).</summary>
        DC = 1,

        /// <summary>After completed AC passes that are the last pass for their resolution target.</summary>
        LastPasses = 2,

        /// <summary>After completed AC passes that are not the last pass for their resolution target.</summary>
        Passes = 3,

        /// <summary>During DC frame when lower resolution are completed (1:32, 1:16).</summary>
        DCProgressive = 4,

        /// <summary>After completed DC groups.</summary>
        DCGroups = 5,

        /// <summary>After completed groups.</summary>
        Groups = 6,
    }

    /// <summary>
    /// Decodes a single frame (Header, TOC, DC Global, DC Groups, AC Global, and AC Groups).
    /// Ported from <c>jxl::FrameDecoder</c> in <c>lib/jxl/dec_frame.h</c> and <c>lib/jxl/dec_frame.cc</c>.
    /// </summary>
    public sealed unsafe class FrameDecoder : IDisposable
    {
        private enum Stage
        {
            WaitingForInit,
            Started,
            Finalized,
            Error,
        }

        private enum Mode
        {
            Normal,
            JpegTranscode,
        }

        private readonly PassesDecoderState _decState;
        private readonly ThreadPool? _pool;
        private readonly FrameHeader _frameHeader;
        private readonly ModularFrameDecoder _modularFrameDecoder;
        private readonly List<int> _passesToPause = new List<int>();

        private TocEntry[] _toc = Array.Empty<TocEntry>();
        private uint[] _sectionSizes = Array.Empty<uint>();
        private nuint _sectionSizesSum;

        private ImageBundle? _decoded;
        private FrameDimensions _frameDim;

        private bool _renderSpotcolors = true;
        private bool _coalescing       = true;
        private JxlProgressiveDetail _progressiveDetail = JxlProgressiveDetail.Frames;

        private byte[] _decodedDcGroups = Array.Empty<byte>();
        private uint[] _numPassesForAcGroup = Array.Empty<uint>();
        private byte[] _processedSection = Array.Empty<byte>();
        private GroupDecCache[] _groupDecCaches = Array.Empty<GroupDecCache>();

        private bool _decodedDcGlobal;
        private bool _decodedAcGlobal;
        private bool _hasBeenFlushed;
        private bool _finalizedDc = true;
        private bool _isFinalized = true;
        private bool _allocated;

        private nuint _numSectionsDone;
        private Mode _mode = Mode.Normal;
        private readonly bool _useSlowRenderingPipeline;
        private Stage _stage = Stage.WaitingForInit;
        private nuint _maxPasses;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrameDecoder"/> class.
        /// </summary>
        /// <param name="decState">Per-frame decoder state.</param>
        /// <param name="metadata">Codestream metadata.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="useSlowRenderingPipeline">True to force the reference slow render pipeline.</param>
        public FrameDecoder(
            PassesDecoderState decState,
            CodecMetadata metadata,
            ThreadPool? pool,
            bool useSlowRenderingPipeline)
        {
            _decState                 = decState;
            _pool                     = pool;
            _frameHeader              = new FrameHeader(metadata);
            _modularFrameDecoder      = new ModularFrameDecoder(decState.MemoryManager);
            _useSlowRenderingPipeline = useSlowRenderingPipeline;
        }

        /// <summary>Gets the parsed TOC entries for the current frame.</summary>
        public ReadOnlySpan<TocEntry> Toc => _toc;

        /// <summary>Gets the current <see cref="FrameHeader"/>.</summary>
        /// <returns>Current frame header.</returns>
        public FrameHeader GetFrameHeader() => _frameHeader;

        /// <summary>Gets the sum of all section sizes in bytes.</summary>
        /// <returns>Sum of section sizes.</returns>
        public nuint SumSectionSizes() => _sectionSizesSum;

        /// <summary>
        /// Gets the total size of the frame in bytes after the frame header and TOC.
        /// </summary>
        /// <returns>Total section bytes.</returns>
        public nuint TotalSize()
        {
            if (_toc.Length == 0)
            {
                return 0;
            }

            TocEntry last = _toc[_toc.Length - 1];

            return last.Offset + last.Size;
        }

        /// <summary>Sets whether spot color extra channels should be rendered onto color channels.</summary>
        /// <param name="rsc">True to render spot colors.</param>
        public void SetRenderSpotcolors(bool rsc) => _renderSpotcolors = rsc;

        /// <summary>Sets whether frame coalescing (blending) is enabled.</summary>
        /// <param name="c">True to coalesce frames.</param>
        public void SetCoalescing(bool c) => _coalescing = c;

        /// <summary>Sets whether the frame should be decoded for lossless JPEG transcoding.</summary>
        /// <param name="jpegTranscode">True to enable JPEG transcode mode.</param>
        public void SetJpegTranscodeMode(bool jpegTranscode)
        {
            _mode = jpegTranscode ? Mode.JpegTranscode : Mode.Normal;
        }

        /// <summary>
        /// Configures progressive pausing for the current frame and returns the effective <see cref="JxlProgressiveDetail"/>.
        /// Ported from <c>jxl::FrameDecoder::SetPauseAtProgressive</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <param name="progDetail">Requested progressive detail level.</param>
        /// <returns>Effective progressive detail level for the current frame.</returns>
        public JxlProgressiveDetail SetPauseAtProgressive(JxlProgressiveDetail progDetail)
        {
            bool singleSection = _frameDim.NumGroups == 1 && _frameHeader.Passes.NumPasses == 1;
            bool noExtra       = _decoded?.Metadata?.ExtraChannelInfo.Count == 0;

            if (_frameHeader.FrameType != FrameType.SkipProgressive &&
                !singleSection &&
                noExtra &&
                _frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                _progressiveDetail = progDetail;
            }
            else
            {
                _progressiveDetail = JxlProgressiveDetail.Frames;
            }

            _passesToPause.Clear();

            if (_progressiveDetail >= JxlProgressiveDetail.Passes)
            {
                for (int i = 1; i < (int)_frameHeader.Passes.NumPasses; ++i)
                {
                    _passesToPause.Add(i);
                }
            }
            else if (_progressiveDetail >= JxlProgressiveDetail.LastPasses)
            {
                for (int i = 0; i < (int)_frameHeader.Passes.NumDownsample; ++i)
                {
                    _passesToPause.Add((int)_frameHeader.Passes.LastPass[i] + 1);
                }

                _passesToPause.Sort();
            }

            return _progressiveDetail;
        }

        /// <summary>
        /// Returns the next pass count at which progressive decoding should pause, or <see cref="nuint.MaxValue"/> if none.
        /// Ported from <c>jxl::FrameDecoder::NextNumPassesToPause</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <returns>Next target pass count to pause at.</returns>
        public nuint NextNumPassesToPause()
        {
            int complete = (int)NumCompletePasses();

            for (int i = 0; i < _passesToPause.Count; i++)
            {
                if (_passesToPause[i] > complete)
                {
                    return (nuint)_passesToPause[i];
                }
            }

            return nuint.MaxValue;
        }

        /// <summary>
        /// Calculates the aligned row stride in bytes for <paramref name="xsize"/> pixels of <paramref name="format"/>.
        /// Ported from <c>jxl::FrameDecoder::GetStride</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <param name="xsize">Row width in pixels.</param>
        /// <param name="format">Target pixel format.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the row stride in bytes.</returns>
        public static StatusOr<nuint> GetStride(nuint xsize, JxlPixelFormat format)
        {
            nuint bytesPerChannel = format.DataType switch
            {
                JxlDataType.Float   => 4u,
                JxlDataType.Float16 => 2u,
                JxlDataType.UInt8   => 1u,
                JxlDataType.UInt16  => 2u,
                _                   => 0u,
            };

            if (bytesPerChannel == 0)
            {
                return Status.Failure($"Unsupported JxlDataType: {format.DataType}");
            }

            nuint mul = bytesPerChannel * format.NumChannels;

            if (xsize > nuint.MaxValue / mul)
            {
                return Status.Failure("Integer overflow computing row stride");
            }

            nuint stride = xsize * mul;

            if (format.Align > 1)
            {
                nuint rem = stride % format.Align;

                if (rem != 0)
                {
                    nuint pad = format.Align - rem;

                    if (stride > nuint.MaxValue - pad)
                    {
                        return Status.Failure("Integer overflow aligning row stride");
                    }

                    stride += pad;
                }
            }

            return stride;
        }

        /// <summary>
        /// Configures the main pixel callback or image buffer where decoded pixels will be written.
        /// Ported from <c>jxl::FrameDecoder::SetImageOutput</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <param name="pixelCallback">Optional line-by-line pixel callback.</param>
        /// <param name="imageBuffer">Optional output buffer pointer.</param>
        /// <param name="imageBufferSize">Size of <paramref name="imageBuffer"/> in bytes.</param>
        /// <param name="dims">Unoriented frame dimensions (<c>XSize, YSize</c>).</param>
        /// <param name="format">Output pixel format.</param>
        /// <param name="bitsPerSample">Bits per sample for unsigned integer output.</param>
        /// <param name="flags">Tuple of (<c>UnpremulAlpha, UndoOrientation</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetImageOutput(
            PixelCallback pixelCallback,
            void* imageBuffer,
            nuint imageBufferSize,
            (nuint XSize, nuint YSize) dims,
            JxlPixelFormat format,
            nuint bitsPerSample,
            (bool UnpremulAlpha, bool UndoOrientation) flags)
        {
            _decState.Width                    = dims.XSize;
            _decState.Height                   = dims.YSize;
            _decState.MainOutput.Format        = format;
            _decState.MainOutput.BitsPerSample = bitsPerSample;
            _decState.MainOutput.Callback      = pixelCallback;
            _decState.MainOutput.Buffer        = imageBuffer;
            _decState.MainOutput.BufferSize    = imageBufferSize;

            StatusOr<nuint> strideOr = GetStride(dims.XSize, format);

            if (!strideOr.IsOk)
            {
                return strideOr.Status;
            }

            _decState.MainOutput.Stride = strideOr.Value;
            ExtraChannelInfo? alpha     = _decoded?.Metadata?.Find(ExtraChannel.Alpha);

            if (alpha != null && alpha.AlphaAssociated && flags.UnpremulAlpha)
            {
                _decState.UnpremulAlpha = true;
            }

            if (flags.UndoOrientation && _decoded?.Metadata != null)
            {
                _decState.UndoOrientation = _decoded.Metadata.GetOrientation();

                if ((int)_decState.UndoOrientation > 4)
                {
                    (_decState.Width, _decState.Height) = (_decState.Height, _decState.Width);
                }
            }

            _decState.ExtraOutput.Clear();
            ConfigureFastXybIfEligible(format);

            return true;
        }

        /// <summary>
        /// Appends an extra channel output buffer to the frame decoder state.
        /// Ported from <c>jxl::FrameDecoder::AddExtraChannelOutput</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <param name="buffer">Destination buffer pointer (or <c>null</c> if skipped).</param>
        /// <param name="bufferSize">Size of <paramref name="buffer"/> in bytes.</param>
        /// <param name="xsize">Oriented row width in pixels.</param>
        /// <param name="format">Pixel format for the extra channel.</param>
        /// <param name="bitsPerSample">Bits per sample for unsigned integer output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status AddExtraChannelOutput(
            void* buffer,
            nuint bufferSize,
            nuint xsize,
            JxlPixelFormat format,
            nuint bitsPerSample)
        {
            StatusOr<nuint> strideOr = GetStride(xsize, format);

            if (!strideOr.IsOk)
            {
                return strideOr.Status;
            }

            ImageOutput output = new ImageOutput
            {
                Format        = format,
                BitsPerSample = bitsPerSample,
                Buffer        = buffer,
                BufferSize    = bufferSize,
                Stride        = strideOr.Value,
            };

            _decState.ExtraOutput.Add(output);

            return true;
        }

        /// <summary>
        /// Reads the <see cref="FrameHeader"/> and table of contents from <paramref name="br"/>.
        /// Ported from <c>jxl::FrameDecoder::InitFrame</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <param name="br">Source bit reader.</param>
        /// <param name="decoded">Output <see cref="ImageBundle"/>.</param>
        /// <param name="isPreview">True if decoding the preview frame.</param>
        /// <param name="downsamplingTarget">Optional progressive downsampling factor target.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitFrame(
            BitReader br,
            ImageBundle decoded,
            bool isPreview,
            uint? downsamplingTarget = null)
        {
            _decoded = decoded;
            Debug.Assert(_isFinalized);

            // Reset the dequantization matrices to their default values.
            _decState.SharedStorage.Matrices.Dispose();
            _decState.SharedStorage.Matrices  = new DequantMatrices();
            _decState.SharedStorage.Quantizer = new Quantizer(_decState.SharedStorage.Matrices);

            _frameHeader.NonserializedIsPreview = isPreview;
            Status status                       = FrameHeaderMethods.ReadFrameHeader(br, _frameHeader);

            if (!status.IsOk)
            {
                return status;
            }

            _frameDim = _frameHeader.ToFrameDimensions();

            Status validateStatus = ValidateFrameDimensionsAndHeader(downsamplingTarget);

            if (!validateStatus.IsOk)
            {
                return validateStatus;
            }

            decoded.RemoveColor();
            decoded.ClearExtraChannels();
            decoded.Duration = _frameHeader.AnimationFrame.Duration;

            UpdateFrameVisibilityIndices();

            return ReadAndInitializeToc(br);
        }

        /// <summary>
        /// Initializes the output <see cref="ImageBundle"/> metadata, decoder state, and modular frame decoder.
        /// Ported from <c>jxl::FrameDecoder::InitFrameOutput</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitFrameOutput()
        {
            SetDecodedBundleMetadata();

            PassesSharedState shared = _decState.SharedStorage;
            _decState.Width          = _frameDim.XSize;
            _decState.Height         = _frameDim.YSize;

            if (!PassesSharedState.InitializePassesSharedState(_frameHeader, shared))
            {
                return Status.Failure("InitializePassesSharedState failed");
            }

            if (!_decState.Init(_frameHeader))
            {
                return Status.Failure("PassesDecoderState.Init failed");
            }

            _modularFrameDecoder.Init(_frameDim);

            if (_decoded!.IsJPEG())
            {
                Status jpegStatus = ValidateAndSetupJpegTranscode(shared);

                if (!jpegStatus.IsOk)
                {
                    return jpegStatus;
                }
            }

            // Clear the state.
            _decodedDcGlobal = false;
            _decodedAcGlobal = false;
            _isFinalized     = false;
            _finalizedDc     = false;
            _hasBeenFlushed  = false;
            _numSectionsDone = 0;

            _decodedDcGroups     = new byte[_frameDim.NumDcGroups];
            _numPassesForAcGroup = new uint[_frameDim.NumGroups];
            _processedSection    = new byte[_toc.Length];
            _allocated           = false;

            _modularFrameDecoder.MaybeDropFullImage();
            _stage = Stage.Started;

            return true;
        }

        /// <summary>
        /// Processes a batch of TOC sections.
        /// Ported from <c>jxl::FrameDecoder::ProcessSections</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <param name="sections">Input section descriptors.</param>
        /// <param name="num">Number of sections in <paramref name="sections"/>.</param>
        /// <param name="sectionStatus">Output status for each section.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status ProcessSections(
            Span<SectionInfo> sections,
            int num,
            Span<SectionStatus> sectionStatus)
        {
            if (num == 0)
            {
                return true;
            }

            if (_stage != Stage.Started)
            {
                return Status.Failure("FrameDecoder is not in Started stage");
            }

            SectionBatchState batch = new SectionBatchState(in _frameDim, _frameHeader.Passes.NumPasses, num);

            Status classifyStatus = ClassifySections(sections, num, sectionStatus, ref batch);

            if (!classifyStatus.IsOk)
            {
                return classifyStatus;
            }

            Status dcStatus = ExecuteDcSections(sections, ref batch);

            if (!dcStatus.IsOk)
            {
                return dcStatus;
            }

            bool wasFinalizedDc     = _finalizedDc;
            Status finalizeDcStatus = TryFinalizeDcAndAllocateOutput();

            if (!finalizeDcStatus.IsOk)
            {
                return finalizeDcStatus;
            }

            if (!wasFinalizedDc && _finalizedDc && _progressiveDetail >= JxlProgressiveDetail.DC)
            {
                MarkAcSectionsSkippedForDcPause(sectionStatus, ref batch);
                RecordProcessedSections(sections, num, sectionStatus);

                return true;
            }

            Status acStatus = ExecuteAcSections(sections, sectionStatus, ref batch);

            if (!acStatus.IsOk)
            {
                return acStatus;
            }

            RecordProcessedSections(sections, num, sectionStatus);

            return true;
        }

        /// <summary>
        /// Flushes all decoded data held so far to the output via the render pipeline.
        /// Ported from <c>jxl::FrameDecoder::Flush</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Flush()
        {
            bool hasBlending = HasAnyBlending();

            // No early Flush() if blending is enabled.
            if (hasBlending && !_isFinalized)
            {
                return false;
            }

            // No early Flush() - nothing to do - if the frame is a kSkipProgressive frame.
            if (_frameHeader.FrameType == FrameType.SkipProgressive && !_isFinalized)
            {
                return true;
            }

            if (_decoded!.IsJPEG())
            {
                return true;
            }

            Status status = FlushInternal();

            if (!status.IsOk)
            {
                return status;
            }

            _hasBeenFlushed = true;

            return true;
        }

        /// <summary>
        /// Finalizes frame decoding after all sections have been processed.
        /// Ported from <c>jxl::FrameDecoder::FinalizeFrame</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status FinalizeFrame()
        {
            if (_stage != Stage.Started)
            {
                return Status.Failure("FinalizeFrame: FrameDecoder is not started");
            }

            if (_isFinalized)
            {
                return Status.Failure("FinalizeFrame called multiple times");
            }

            if (_decoded!.IsJPEG())
            {
                _isFinalized = true;

                return true;
            }

            if (!_finalizedDc)
            {
                return Status.Failure("FinalizeFrame called before DC was finalized");
            }

            Status allocStatus = AllocateOutput();

            if (!allocStatus.IsOk)
            {
                return allocStatus;
            }

            if (!_hasBeenFlushed)
            {
                Status forceStatus = ForceDrawIncompleteGroupsIfNeeded();

                if (!forceStatus.IsOk)
                {
                    return forceStatus;
                }
            }

            Status modStatus = _modularFrameDecoder.FinalizeDecoding(
                _frameHeader,
                _decState,
                _pool,
                inplace: true);

            if (!modStatus.IsOk)
            {
                return modStatus;
            }

            Status refStatus = SaveFrameToReferenceSlotIfNeeded();

            if (!refStatus.IsOk)
            {
                return refStatus;
            }

            _isFinalized = true;
            _stage       = Stage.Finalized;

            return true;
        }

        /// <summary>
        /// Returns a bitmask of reference slots <c>(1 &lt;&lt; idx)</c> read by the current frame.
        /// Ported from <c>jxl::FrameDecoder::References</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <returns>Bitmask of referenced slots.</returns>
        public int References()
        {
            if (_isFinalized)
            {
                return 0;
            }

            if ((_frameHeader.Flags & (FrameHeader.KUseDcFrame | FrameHeader.KPatches)) != 0)
            {
                if (!_decodedDcGlobal)
                {
                    return -1;
                }
            }

            int result = 0;

            if ((_frameHeader.Flags & FrameHeader.KUseDcFrame) != 0)
            {
                result |= 1 << (int)(_frameHeader.DcLevel + 4);
            }

            if ((_frameHeader.Flags & FrameHeader.KPatches) != 0)
            {
                result |= _decState.Shared.ImageFeatures.Patches.GetReferences();
            }

            bool usesBlending = _frameHeader.BlendingInfo.Mode != BlendMode.Replace ||
                                _frameHeader.CustomSizeOrOrigin;

            for (int i = 0; i < _frameHeader.ExtraChannelBlendingInfo.Count; ++i)
            {
                usesBlending |= _frameHeader.ExtraChannelBlendingInfo[i].Mode != BlendMode.Replace;
            }

            if (usesBlending)
            {
                result |= 1 << (int)_frameHeader.BlendingInfo.Source;
            }

            return result;
        }

        /// <summary>
        /// Returns the reference slot bitmask <c>(1 &lt;&lt; idx)</c> in which <paramref name="header"/> is saved, or 0.
        /// Ported from <c>jxl::FrameDecoder::SavedAs</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <param name="header">Frame header to inspect.</param>
        /// <returns>Reference slot bitmask.</returns>
        public static int SavedAs(FrameHeader header)
        {
            if (header.FrameType == FrameType.DCFrame)
            {
                // Checked in FrameHeader::VisitFields.
                Debug.Assert(header.DcLevel != 0);

                // Bits 4, 5, 6 and 7 are used for DCFrame.
                return 1 << (int)(header.DcLevel + 3);
            }

            if (header.CanBeReferenced())
            {
                // Bits 0, 1, 2 and 3 are used for reference frames.
                return 1 << (int)header.SaveAsReference;
            }

            return 0;
        }

        /// <summary>Returns whether all sections in the frame have been decoded.</summary>
        /// <returns><c>true</c> if all TOC sections are done.</returns>
        public bool HasDecodedAll() => _toc.Length == (int)_numSectionsDone;

        /// <summary>Returns whether all DC sections have been decoded and finalized.</summary>
        /// <returns><c>true</c> if DC is finalized.</returns>
        public bool HasDecodedDC() => _finalizedDc;

        /// <summary>Returns the minimum number of completed AC passes across all AC groups.</summary>
        /// <returns>Completed AC pass count.</returns>
        public uint NumCompletePasses()
        {
            return _numPassesForAcGroup.Length == 0 ? 0u : MinValue(_numPassesForAcGroup);
        }

        /// <summary>
        /// Returns the offset in bytes (after the frame header and TOC) of the first unread section.
        /// Ported from <c>jxl::FrameDecoder::NextFrameOffset</c> in <c>lib/jxl/dec_frame.h</c>.
        /// </summary>
        /// <returns>Byte offset of next unread section, or <see cref="TotalSize"/> if all processed.</returns>
        public nuint NextFrameOffset()
        {
            if (_stage == Stage.Error)
            {
                return 0;
            }

            for (int t = 0; t < _toc.Length; t++)
            {
                if (_processedSection[_toc[t].Id] == 0)
                {
                    return _toc[t].Offset;
                }
            }

            return TotalSize();
        }

        /// <summary>
        /// Decodes a full frame from a contiguous buffer in <paramref name="nextIn"/>.
        /// Ported from <c>jxl::DecodeFrame</c> in <c>lib/jxl/dec_frame.cc</c>.
        /// </summary>
        /// <param name="decState">Per-frame decoder state.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="nextIn">Pointer to compressed frame bytes (updated past the consumed frame).</param>
        /// <param name="availIn">Available bytes at <paramref name="nextIn"/> (updated to remaining bytes).</param>
        /// <param name="frameHeader">Optional destination to copy the parsed <see cref="FrameHeader"/> into.</param>
        /// <param name="decoded">Destination <see cref="ImageBundle"/>.</param>
        /// <param name="metadata">Codestream metadata.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeFrame(
            PassesDecoderState decState,
            ThreadPool? pool,
            ref byte* nextIn,
            ref nuint availIn,
            FrameHeader? frameHeader,
            ImageBundle decoded,
            CodecMetadata metadata)
        {
            using FrameDecoder frameDecoder = new FrameDecoder(decState, metadata, pool, useSlowRenderingPipeline: false);
            BitReader reader                = new BitReader(new ReadOnlySpan<byte>(nextIn, (int)availIn));

            try
            {
                Status status = frameDecoder.InitFrame(reader, decoded, isPreview: false);

                if (!status.IsOk)
                {
                    return status;
                }

                if (frameHeader != null)
                {
                    CopyFrameHeaderFields(frameDecoder.GetFrameHeader(), frameHeader);
                }

                if (!reader.JumpToByteBoundary())
                {
                    return Status.Failure("JumpToByteBoundary failed after TOC");
                }

                nuint headerBytes = reader.TotalBitsConsumed() / 8u;
                nextIn           += headerBytes;
                availIn          -= headerBytes;

                Status initOutStatus = frameDecoder.InitFrameOutput();

                if (!initOutStatus.IsOk)
                {
                    return initOutStatus;
                }

                nuint totalSize = frameDecoder.TotalSize();

                if (totalSize > availIn)
                {
                    return Status.Failure("Frame sections exceed available input buffer");
                }

                Status sectionStatus = RunAllFrameSections(frameDecoder, nextIn, availIn);

                if (!sectionStatus.IsOk)
                {
                    return sectionStatus;
                }

                Status finalizeStatus = frameDecoder.FinalizeFrame();

                if (!finalizeStatus.IsOk)
                {
                    return finalizeStatus;
                }

                nextIn  += totalSize;
                availIn -= totalSize;

                return true;
            }
            finally
            {
                reader.Close();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _modularFrameDecoder.Dispose();

            for (int i = 0; i < _groupDecCaches.Length; i++)
            {
                _groupDecCaches[i]?.Dispose();
            }
        }

        private static Status RunAllFrameSections(FrameDecoder frameDecoder, byte* basePtr, nuint availIn)
        {
            ReadOnlySpan<TocEntry> toc      = frameDecoder.Toc;
            int count                       = toc.Length;
            SectionInfo[] sectionInfo       = new SectionInfo[count];
            SectionStatus[] sectionStatuses = new SectionStatus[count];

            try
            {
                for (int i = 0; i < count; i++)
                {
                    nuint offset = toc[i].Offset;
                    nuint size   = toc[i].Size;

                    if (offset + size > availIn)
                    {
                        return Status.Failure("Section out of bounds in DecodeFrame");
                    }

                    sectionInfo[i] = new SectionInfo
                    {
                        Br    = new BitReader(new ReadOnlySpan<byte>(basePtr + offset, (int)size)),
                        Id    = toc[i].Id,
                        Index = (nuint)i,
                    };
                }

                Status status = frameDecoder.ProcessSections(sectionInfo, count, sectionStatuses);

                if (!status.IsOk)
                {
                    return status;
                }

                for (int i = 0; i < count; i++)
                {
                    if (sectionStatuses[i] != SectionStatus.Done)
                    {
                        return Status.Failure($"unexpected section {i} status: {sectionStatuses[i]}");
                    }
                }

                return true;
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    sectionInfo[i].Br?.Close();
                }
            }
        }

        private static void CopyFrameHeaderFields(FrameHeader src, FrameHeader dst)
        {
            dst.AllDefault               = src.AllDefault;
            dst.Encoding                 = src.Encoding;
            dst.FrameType                = src.FrameType;
            dst.Flags                    = src.Flags;
            dst.ColorTransform           = src.ColorTransform;
            dst.GroupSizeShift           = src.GroupSizeShift;
            dst.XQmScale                 = src.XQmScale;
            dst.BQmScale                 = src.BQmScale;
            dst.Name                     = src.Name;
            dst.CustomSizeOrOrigin       = src.CustomSizeOrOrigin;
            dst.FrameSize                = src.FrameSize;
            dst.Upsampling               = src.Upsampling;
            dst.FrameOrigin              = src.FrameOrigin;
            dst.IsLast                   = src.IsLast;
            dst.SaveAsReference          = src.SaveAsReference;
            dst.SaveBeforeColorTransform = src.SaveBeforeColorTransform;
            dst.DcLevel                  = src.DcLevel;
            dst.NonserializedIsPreview   = src.NonserializedIsPreview;
        }

        private Status ValidateFrameDimensionsAndHeader(uint? downsamplingTarget)
        {
            nuint xsize = _frameDim.XSize;
            nuint ysize = _frameDim.YSize;

            if (xsize == 0 || ysize == 0)
            {
                return Status.Failure("Empty frame is not allowed");
            }

            //Check that the frame dimensions aren't too big.
            if (_frameDim.XSizeUpsampled > (uint.MaxValue >> 2) ||
                _frameDim.YSizeUpsampled > (uint.MaxValue >> 2))
            {
                return Status.Failure("Frame dimensions are too big");
            }

            Passes passes = _frameHeader.Passes;
            _maxPasses    = passes.NumPasses;

            if (downsamplingTarget.HasValue)
            {
                SetupProgressiveDownsamplingTarget(passes, downsamplingTarget.Value);
            }

            return ValidateHeaderConstraints();
        }

        private void SetupProgressiveDownsamplingTarget(Passes passes, uint target)
        {
            uint maxDownsample = Math.Min(target, 8u);

            // Find the last pass that contributes to the downsampling_target, and stop
            // there if possible.
            for (uint i = 0; i < passes.NumDownsample; ++i)
            {
                if (maxDownsample >= passes.Downsample[i] && _frameHeader.DcLevel == 0)
                {
                    _maxPasses = Math.Min(passes.LastPass[i] + 1, _maxPasses);
                }
            }
        }

        private Status ValidateHeaderConstraints()
        {
            for (int i = 0; i < _frameHeader.ExtraChannelUpsampling.Count; ++i)
            {
                if (_frameHeader.ExtraChannelUpsampling[i] != _frameHeader.Upsampling)
                {
                    // Extra channel upsampling stages handle differing factors in RenderPipeline.
                    break;
                }
            }

            return true;
        }

        private Status ReadAndInitializeToc(BitReader br)
        {
            nuint numPasses     = _frameHeader.Passes.NumPasses;
            nuint numGroups     = _frameDim.NumGroups;
            nuint numDcGroups   = _frameDim.NumDcGroups;
            nuint numTocEntries = JxlSharp.Decoder.Toc.NumTocEntries(numGroups, numDcGroups, numPasses);

            Status tocStatus = JxlSharp.Decoder.Toc.ReadToc(
                _decState.MemoryManager,
                numTocEntries,
                br,
                out _sectionSizes,
                out uint[] permutation);

            if (!tocStatus.IsOk)
            {
                return tocStatus;
            }

            Debug.Assert(_sectionSizes.Length == (int)numTocEntries);

            _toc             = new TocEntry[numTocEntries];
            _sectionSizesSum = 0;

            bool hasPermutation = permutation.Length > 0;

            for (nuint i = 0; i < numTocEntries; ++i)
            {
                _toc[i].Size     = _sectionSizes[i];
                _toc[i].Offset   = _sectionSizesSum;
                nuint index      = hasPermutation ? permutation[i] : i;
                _toc[index].Id   = i;
                _sectionSizesSum += _toc[i].Size;
            }

            if ((br.TotalBitsConsumed() / 8u) + _sectionSizesSum < _sectionSizesSum)
            {
                return Status.Failure("Integer overflow in TOC sizes");
            }

            return true;
        }

        private void UpdateFrameVisibilityIndices()
        {
            bool isVisibleRegular = !_frameHeader.NonserializedIsPreview &&
                                    (_frameHeader.IsLast || _frameHeader.AnimationFrame.Duration > 0) &&
                                    (_frameHeader.FrameType == FrameType.RegularFrame ||
                                     _frameHeader.FrameType == FrameType.SkipProgressive);

            if (isVisibleRegular)
            {
                ++_decState.VisibleFrameIndex;
                _decState.NonvisibleFrameIndex = 0;
            }
            else
            {
                ++_decState.NonvisibleFrameIndex;
            }
        }

        private void ConfigureFastXybIfEligible(JxlPixelFormat format)
        {
            if (_decoded?.Metadata == null || _decState.MainOutput.Buffer == null)
            {
                return;
            }

            OutputEncodingInfo outEnc = _decState.OutputEncodingInfo;
            bool eligible             = format.DataType == JxlDataType.UInt8 &&
                                        format.NumChannels >= 3 &&
                                        !_decState.UnpremulAlpha &&
                                        _decState.UndoOrientation == Orientation.Identity &&
                                        _decoded.Metadata.XybEncoded &&
                                        outEnc.ColorEncoding.IsSRGB() &&
                                        outEnc.AllDefaultOpsin &&
                                        outEnc.DesiredIntensityTarget == outEnc.OrigIntensityTarget &&
                                        DecXyb.HasFastXYBTosRGB8() &&
                                        _frameHeader.NeedsColorTransform();

            if (eligible)
            {
                _decState.FastXybSrgb8Conversion = true;
            }
        }

        private bool HasAnyBlending()
        {
            bool hasBlending = _frameHeader.BlendingInfo.Mode != BlendMode.Replace ||
                               _frameHeader.CustomSizeOrOrigin;

            for (int i = 0; i < _frameHeader.ExtraChannelBlendingInfo.Count; i++)
            {
                if (_frameHeader.ExtraChannelBlendingInfo[i].Mode != BlendMode.Replace)
                {
                    hasBlending = true;
                }
            }

            return hasBlending;
        }

        private static void MarkAcSectionsSkippedForDcPause(
            Span<SectionStatus> sectionStatus,
            ref SectionBatchState batch)
        {
            if (batch.AcGlobalIdx != -1)
            {
                sectionStatus[batch.AcGlobalIdx] = SectionStatus.Skipped;
            }

            for (int g = 0; g < batch.NumPassesToAdd.Length; g++)
            {
                for (int p = 0; p < batch.AcGroupPassIndices[g].Length; p++)
                {
                    int secIdx = batch.AcGroupPassIndices[g][p];

                    if (secIdx != -1 && sectionStatus[secIdx] == SectionStatus.Done)
                    {
                        sectionStatus[secIdx] = SectionStatus.Skipped;
                    }
                }
            }
        }

        private void SetDecodedBundleMetadata()
        {
            CodecMetadata meta = _frameHeader.NonserializedMetadata!;
            _decoded!.SetFromImage(new Image3<float>(), meta.M.ColorEncoding);
            _decoded.Duration = _frameHeader.AnimationFrame.Duration;
            _decoded.Origin   = _frameHeader.FrameOrigin;
            _decoded.Blend    = _frameHeader.BlendingInfo.Mode != BlendMode.Replace;
        }

        private Status ValidateAndSetupJpegTranscode(PassesSharedState shared)
        {
            if (_frameHeader.Encoding == FrameEncoding.Modular)
            {
                return Status.Failure("Cannot output JPEG from Modular");
            }

            JPEGData? jpegData = _decoded!.JpegData;

            if (jpegData == null)
            {
                return Status.Failure("Missing JPEGData in decoded bundle");
            }

            int numComponents = jpegData.Components.Count;

            if (numComponents != 1 && numComponents != 3)
            {
                return Status.Failure("Invalid number of components");
            }

            if (_frameHeader.NonserializedMetadata!.M.XybEncoded)
            {
                return Status.Failure("Cannot decode to JPEG an XYB image");
            }

            ReadOnlySpan<int> jpegCMap = DecGroup.JpegOrder(ColorTransform.YCbCr, numComponents == 1);
            jpegData.Width             = (int)_frameDim.XSize;
            jpegData.Height            = (int)_frameDim.YSize;

            for (int c = 0; c < numComponents; c++)
            {
                JPEGComponent comp = jpegData.Components[jpegCMap[c]];
                comp.WidthInBlocks  = (uint)(_frameDim.XSizeBlocks >> (int)_frameHeader.ChromaSubsampling.HShift((nuint)c));
                comp.HeightInBlocks = (uint)(_frameDim.YSizeBlocks >> (int)_frameHeader.ChromaSubsampling.VShift((nuint)c));
                comp.HSampFactor    = 1 << (int)_frameHeader.ChromaSubsampling.RawHShift((nuint)c);
                comp.VSampFactor    = 1 << (int)_frameHeader.ChromaSubsampling.RawVShift((nuint)c);
                nuint numBlocks     = (nuint)comp.WidthInBlocks * comp.HeightInBlocks;
                comp.Coeffs         = new short[numBlocks * FrameDimensions.kDCTBlockSize];
            }

            _mode                                = Mode.JpegTranscode;
            _frameHeader.UpdateFlag(false, FrameHeader.KNoise);
            _frameHeader.UpdateFlag(false, FrameHeader.KPatches);
            _frameHeader.UpdateFlag(false, FrameHeader.KSplines);
            _frameHeader.UpdateFlag(true, FrameHeader.KSkipAdaptiveDCSmoothing);
            _frameHeader.LoopFilter.EpfIters     = 0;
            _frameHeader.LoopFilter.Gab          = false;
            _decState.MainOutput.Format.DataType = JxlDataType.Float;
            _ = shared;

            return true;
        }

        private Status ProcessDCGlobal(BitReader br)
        {
            PassesSharedState shared = _decState.SharedStorage;
            Status featStatus        = DecodeDCGlobalFeatures(br, shared);

            if (!featStatus.IsOk)
            {
                return featStatus;
            }

            if (!shared.Matrices.DecodeDC(br))
            {
                return Status.Failure("Matrices.DecodeDC failed");
            }

            if (_frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status varDcStatus = DecodeGlobalDCInfo(br, shared);

                if (!varDcStatus.IsOk)
                {
                    return varDcStatus;
                }
            }

            // Splines' draw cache uses the color correlation map.
            if ((_frameHeader.Flags & FrameHeader.KSplines) != 0)
            {
                Status splineInitStatus = shared.ImageFeatures.Splines.InitializeDrawCache(
                    _frameDim.XSizeUpsampled,
                    _frameDim.YSizeUpsampled,
                    shared.Cmap.Base);

                if (!splineInitStatus.IsOk)
                {
                    return splineInitStatus;
                }
            }

            Status modStatus = _modularFrameDecoder.DecodeGlobalInfo(
                br,
                _frameHeader,
                allowTruncatedGroup: false);

            if (!modStatus.IsOk)
            {
                return modStatus;
            }

            _decodedDcGlobal = true;

            return true;
        }

        private Status DecodeGlobalDCInfo(BitReader br, PassesSharedState shared)
        {
            if (!shared.Quantizer.Decode(br))
            {
                return Status.Failure("Quantizer.Decode failed");
            }

            if (!EntropyCoder.DecodeBlockCtxMap(_decState.MemoryManager, br, shared.BlockCtxMap))
            {
                return Status.Failure("DecodeBlockCtxMap failed");
            }

            if (!shared.Cmap.DecodeDC(br))
            {
                return Status.Failure("Cmap.DecodeDC failed");
            }

            if (_decoded != null && _decoded.IsJPEG())
            {
                shared.Quantizer.ClearDCMul();
            }

            shared.AcStrategy.FillInvalid();

            return true;
        }

        private Status DecodeDCGlobalFeatures(BitReader br, PassesSharedState shared)
        {
            if ((_frameHeader.Flags & FrameHeader.KPatches) != 0)
            {
                Status patchStatus = DecodeAndValidatePatches(br, shared);

                if (!patchStatus.IsOk)
                {
                    return patchStatus;
                }
            }
            else
            {
                shared.ImageFeatures.Patches.Clear();
            }

            shared.ImageFeatures.Splines.Clear();

            if ((_frameHeader.Flags & FrameHeader.KSplines) != 0)
            {
                Status splineStatus = shared.ImageFeatures.Splines.Decode(
                    br,
                    _frameDim.XSize * _frameDim.YSize);

                if (!splineStatus.IsOk)
                {
                    return splineStatus;
                }
            }

            if ((_frameHeader.Flags & FrameHeader.KNoise) != 0)
            {
                return DecNoise.DecodeNoise(br, shared.ImageFeatures.NoiseParams);
            }

            return true;
        }

        private Status DecodeAndValidatePatches(BitReader br, PassesSharedState shared)
        {
            bool usesExtraChannels = false;
            nuint numExtra         = _frameHeader.NonserializedMetadata!.M.NumExtraChannels;
            Status patchStatus     = shared.ImageFeatures.Patches.Decode(
                _decState.MemoryManager,
                br,
                _frameDim.XSizePadded,
                _frameDim.YSizePadded,
                numExtra,
                ref usesExtraChannels);

            if (!patchStatus.IsOk)
            {
                return patchStatus;
            }

            if (usesExtraChannels && _frameHeader.Upsampling != 1)
            {
                foreach (uint ecups in _frameHeader.ExtraChannelUpsampling)
                {
                    if (ecups != _frameHeader.Upsampling)
                    {
                        return Status.Failure(
                            "Cannot use extra channels in patches if color channels are subsampled differently from extra channels");
                    }
                }
            }

            return true;
        }

        private Status ProcessDCGroup(nuint dcGroupId, BitReader br)
        {
            nuint gx   = dcGroupId % _frameDim.XSizeDcGroups;
            nuint gy   = dcGroupId / _frameDim.XSizeDcGroups;
            Rect mrect = new Rect(
                (int)(gx * _frameDim.DcGroupDim),
                (int)(gy * _frameDim.DcGroupDim),
                (int)_frameDim.DcGroupDim,
                (int)_frameDim.DcGroupDim,
                (int)_frameDim.XSize,
                (int)_frameDim.YSize);

            if (_frameHeader.Encoding == FrameEncoding.VarDCT &&
                (_frameHeader.Flags & FrameHeader.KUseDcFrame) == 0)
            {
                Status dcVarStatus = _modularFrameDecoder.DecodeVarDCTDC(
                    _frameHeader,
                    dcGroupId,
                    br,
                    _decState);

                if (!dcVarStatus.IsOk)
                {
                    return dcVarStatus;
                }
            }

            ModularFrameDecoder.DecodeGroupArgs modArgs = new ModularFrameDecoder.DecodeGroupArgs
            {
                FrameHeader         = _frameHeader,
                Rect                = mrect,
                Reader              = br,
                MinShift            = 3,
                MaxShift            = 1000,
                Stream              = ModularStreamId.ModularDC(dcGroupId),
                Zerofill            = false,
                DecState            = _decState,
                RenderPipelineInput = null,
                AllowTruncated      = false,
                ShouldRunPipeline   = null,
            };

            Status modGroupStatus = _modularFrameDecoder.DecodeGroup(in modArgs);

            if (!modGroupStatus.IsOk)
            {
                return modGroupStatus;
            }

            if (_frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status acMetaStatus = _modularFrameDecoder.DecodeAcMetadata(
                    _frameHeader,
                    dcGroupId,
                    br,
                    _decState);

                if (!acMetaStatus.IsOk)
                {
                    return acMetaStatus;
                }
            }
            else if (_frameHeader.LoopFilter.EpfIters > 0 && _decState.Sigma != null)
            {
                ImageOps.FillImage(
                    Epf.KInvSigmaNum / _frameHeader.LoopFilter.EpfSigmaForModular,
                    _decState.Sigma);
            }

            _decodedDcGroups[dcGroupId] = 1;

            return true;
        }

        private Status FinalizeDC()
        {
            PassesSharedState shared = _decState.SharedStorage;
            bool skipSmoothing       = (_frameHeader.Flags &
                                        (FrameHeader.KSkipAdaptiveDCSmoothing | FrameHeader.KUseDcFrame)) != 0 ||
                                       (_decoded != null && _decoded.IsJPEG());

            if (_frameHeader.Encoding == FrameEncoding.VarDCT && !skipSmoothing)
            {
                Status smoothStatus = CompressedDc.AdaptiveDCSmoothing(
                    _decState.MemoryManager,
                    shared.Quantizer.MulDC(),
                    shared.DcStorage,
                    _pool);

                if (!smoothStatus.IsOk)
                {
                    return smoothStatus;
                }
            }

            _finalizedDc = true;

            return true;
        }

        private Status AllocateOutput()
        {
            if (_allocated)
            {
                return true;
            }

            _modularFrameDecoder.MaybeDropFullImage();
            _decoded!.Origin = _frameHeader.FrameOrigin;

            Status initAcStatus = _decState.InitForAC(_frameHeader.Passes.NumPasses, _pool);

            if (!initAcStatus.IsOk)
            {
                return initAcStatus;
            }

            _allocated = true;

            return true;
        }

        private Status PrepareRenderPipelineForFrame()
        {
            CodecMetadata metadata = _frameHeader.NonserializedMetadata!;

            if (_decState.OutputEncodingInfo.OrigIntensityTarget == 0)
            {
                Status encStatus = _decState.OutputEncodingInfo.SetFromMetadata(metadata);

                if (!encStatus.IsOk)
                {
                    return encStatus;
                }
            }

            if (_decoded != null && _decoded.IsJPEG())
            {
                return true;
            }

            PassesDecoderState.PipelineOptions options = new PassesDecoderState.PipelineOptions
            {
                UseSlowRenderPipeline = _useSlowRenderingPipeline,
                Coalescing            = _coalescing,
                RenderSpotcolors      = _renderSpotcolors,
                RenderNoise           = (_frameHeader.Flags & FrameHeader.KNoise) != 0,
            };

            return _decState.PreparePipeline(_frameHeader, metadata.M, _decoded, options);
        }

        private Status ProcessACGlobal(BitReader br)
        {
            PassesSharedState shared = _decState.SharedStorage;

            if (_frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status matStatus = shared.Matrices.Decode(
                    _decState.MemoryManager,
                    br,
                    _modularFrameDecoder.CreateGlobalModularContext(),
                    _frameDim);

                if (!matStatus.IsOk)
                {
                    return matStatus;
                }

                Status ensureStatus = shared.Matrices.EnsureComputed(_decState.MemoryManager, (uint)_decState.UsedAcs);

                if (!ensureStatus.IsOk)
                {
                    return ensureStatus;
                }

                Status passesStatus = DecodePassOrdersAndHistograms(br, shared);

                if (!passesStatus.IsOk)
                {
                    return passesStatus;
                }
            }

            if (_decoded != null && _decoded.IsJPEG())
            {
                Status jpegQuantStatus = PopulateJpegQuantTables(shared);

                if (!jpegQuantStatus.IsOk)
                {
                    return jpegQuantStatus;
                }
            }

            _decodedAcGlobal = true;

            return true;
        }

        private Status DecodePassOrdersAndHistograms(BitReader br, PassesSharedState shared)
        {
            nuint numPasses      = _frameHeader.Passes.NumPasses;
            nuint histoBits      = (nuint)Bits.CeilLog2Nonzero(_frameDim.NumGroups);
            nuint numHistograms  = (nuint)br.ReadBits(histoBits) + 1u;
            shared.NumHistograms = numHistograms;

            uint usedAcs         = (uint)_decState.UsedAcs;
            nuint numContexts    = numHistograms * shared.BlockCtxMap.NumACContexts();

            _decState.Code.Clear();
            _decState.ContextMap.Clear();

            for (nuint pass = 0; pass < numPasses; pass++)
            {
                ushort usedOrdersMask = (ushort)U32Coder.Read(FrameHeaderMethods.KOrderEnc, br);
                Span<uint> passOrders = shared.CoeffOrders.AsSpan(
                    (int)(pass * shared.CoeffOrderSize),
                    (int)shared.CoeffOrderSize);

                Status orderStatus = CoeffOrder.DecodeCoeffOrders(
                    _decState.MemoryManager,
                    usedOrdersMask,
                    usedAcs,
                    passOrders,
                    br);

                if (!orderStatus.IsOk)
                {
                    return orderStatus;
                }

                ANSCode code = new ANSCode();

                if (!DecAns.DecodeHistograms(
                    _decState.MemoryManager,
                    br,
                    numContexts,
                    code,
                    out byte[] ctxMap))
                {
                    return Status.Failure($"DecodeHistograms failed in ACGlobal pass {pass}");
                }

                int paddedLen = (int)(numContexts + (nuint)(AcContext.KZeroDensityContextLimit - AcContext.KZeroDensityContextCount));

                if (ctxMap.Length < paddedLen)
                {
                    Array.Resize(ref ctxMap, paddedLen);
                }

                _decState.Code.Add(code);
                _decState.ContextMap.Add(ctxMap);
            }

            return AllocateAccumulatedAcCoefficients(numPasses);
        }

        private Status AllocateAccumulatedAcCoefficients(nuint numPasses)
        {
            bool store = numPasses > 1;
            nuint xs   = store ? FrameDimensions.KGroupDim * FrameDimensions.KGroupDim : 0u;
            nuint ys   = store ? _frameDim.NumGroups : 0u;

            _decState.Coefficients.Dispose();
            StatusOr<ACImageT<int>> coeffOr = ACImageT<int>.Make(_decState.MemoryManager, xs, ys);

            if (!coeffOr.IsOk)
            {
                return coeffOr.Status;
            }

            _decState.Coefficients = coeffOr.Value;

            if (store)
            {
                _decState.Coefficients.ZeroFill();
            }

            return true;
        }

        private Status PopulateJpegQuantTables(PassesSharedState shared)
        {
            _decoded!.ColorTransform    = _frameHeader.ColorTransform;
            _decoded.ChromaSubsampling  = _frameHeader.ChromaSubsampling;
            IReadOnlyList<QuantEncoding> qe = shared.Matrices.Encodings;

            if (qe.Count == 0 ||
                qe[0].Mode != QuantEncoding.QuantMode.kQuantModeRAW ||
                MathF.Abs(qe[0].QrawTableDen - (1.0f / (8.0f * 255.0f))) > 1e-8f ||
                qe[0].QrawTable == null ||
                qe[0].QrawTable!.Length != 3 * 8 * 8)
            {
                return Status.Failure("Quantization table is not a JPEG quantization table.");
            }

            if (_frameHeader.ColorTransform == ColorTransform.XYB)
            {
                return Status.Failure("XYB color transform cannot be decoded to JPEG");
            }

            JPEGData jpegData          = _decoded.JpegData!;
            int numComponents          = jpegData.Components.Count;
            bool isGray                = numComponents == 1;
            ReadOnlySpan<int> jpegCMap = DecGroup.JpegOrder(_frameHeader.ColorTransform, isGray);
            int[] qtable               = qe[0].QrawTable!;
            uint qtSet                 = 0;

            for (int c = 0; c < numComponents; c++)
            {
                int quantC = isGray ? 1 : c;
                int qpos   = (int)jpegData.Components[jpegCMap[c]].QuantIdx;

                if ((uint)qpos >= (uint)jpegData.Quant.Count)
                {
                    return Status.Failure("Invalid JPEG quant table index");
                }

                qtSet |= 1u << qpos;
                CopyTransposedQuantBlock(qtable, quantC, jpegData.Quant[qpos].Values);
            }

            return FillUnusedJpegQuantTables(jpegData, qtSet);
        }

        private static void CopyTransposedQuantBlock(int[] qtable, int quantC, int[] dstValues)
        {
            int baseIdx = quantC * 64;

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    dstValues[x * 8 + y] = qtable[baseIdx + y * 8 + x];
                }
            }
        }

        private static Status FillUnusedJpegQuantTables(JPEGData jpegData, uint qtSet)
        {
            for (int i = 0; i < jpegData.Quant.Count; i++)
            {
                if ((qtSet & (1u << i)) != 0)
                {
                    continue;
                }

                if (i == 0)
                {
                    return Status.Failure("First quant table unused.");
                }

                Array.Copy(jpegData.Quant[i - 1].Values, jpegData.Quant[i].Values, 64);
            }

            return true;
        }

        private Status ProcessACGroup(
            nuint acGroupId,
            BitReader?[] brs,
            int numPasses,
            nuint thread,
            bool forceDraw,
            bool dcOnly)
        {
            nuint gx = acGroupId % _frameDim.XSizeGroups;
            nuint gy = acGroupId / _frameDim.XSizeGroups;
            nuint x  = gx * _frameDim.GroupDim;
            nuint y  = gy * _frameDim.GroupDim;

            RenderPipelineInput? renderPipelineInput = null;

            bool shouldDraw = (!dcOnly || forceDraw) && _mode == Mode.Normal;

            if (shouldDraw && _decState.RenderPipeline != null)
            {
                renderPipelineInput = _decState.RenderPipeline.GetInputBuffers(acGroupId, thread);
            }

            bool shouldRunPipeline = true;

            if (_frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Status varDctStatus = ProcessVarDctAcGroup(
                    acGroupId,
                    brs,
                    numPasses,
                    thread,
                    (forceDraw, dcOnly),
                    renderPipelineInput,
                    out shouldRunPipeline);

                if (!varDctStatus.IsOk)
                {
                    return varDctStatus;
                }
            }

            Status modStatus = ProcessModularAcGroupPasses(
                acGroupId,
                (x, y),
                brs,
                numPasses,
                forceDraw,
                renderPipelineInput,
                out bool modularReady);

            if (!modStatus.IsOk)
            {
                return modStatus;
            }

            _numPassesForAcGroup[acGroupId] += (uint)numPasses;

            if ((_frameHeader.Flags & FrameHeader.KNoise) != 0 && _decState.RenderPipeline != null)
            {
                DecNoise.PrepareNoiseInput(
                    _decState.RenderPipeline,
                    _decState.VisibleFrameIndex,
                    _decState.NonvisibleFrameIndex,
                    in _frameDim,
                    _frameHeader,
                    acGroupId,
                    thread);
            }

            return FinishAcGroupPipeline(shouldDraw, shouldRunPipeline, modularReady, forceDraw, renderPipelineInput);
        }

        private Status FinishAcGroupPipeline(
            bool shouldDraw,
            bool shouldRunPipeline,
            bool modularReady,
            bool forceDraw,
            RenderPipelineInput? renderPipelineInput)
        {
            if (!_modularFrameDecoder.UsesFullImage() && !_decoded!.IsJPEG())
            {
                if (shouldDraw && shouldRunPipeline && modularReady && renderPipelineInput != null)
                {
                    return renderPipelineInput.Done();
                }

                if (forceDraw)
                {
                    return Status.Failure("Modular group decoding failed.");
                }
            }

            return true;
        }

        private Status ProcessVarDctAcGroup(
            nuint acGroupId,
            BitReader?[] brs,
            int numPasses,
            nuint thread,
            (bool ForceDraw, bool DcOnly) flags,
            RenderPipelineInput? renderPipelineInput,
            out bool shouldRunPipeline)
        {
            EnsureGroupDecCaches(thread + 1);

            Status initCacheStatus = _groupDecCaches[thread].InitOnce(
                _decState.MemoryManager,
                _frameHeader.Passes.NumPasses,
                (uint)_decState.UsedAcs);

            if (!initCacheStatus.IsOk)
            {
                shouldRunPipeline = false;

                return initCacheStatus;
            }

            RenderPipelineInput input = renderPipelineInput ?? new RenderPipelineInput();

            DecGroup.DecodeGroupArgs groupArgs = new DecGroup.DecodeGroupArgs
            {
                FrameHeader         = _frameHeader,
                Readers             = brs,
                NumPasses           = (nuint)numPasses,
                GroupIdx            = acGroupId,
                DecState            = _decState,
                GroupDecCache       = _groupDecCaches[thread],
                Thread              = thread,
                RenderPipelineInput = input,
                JpegData            = _mode == Mode.JpegTranscode ? _decoded?.JpegData : null,
                FirstPass           = _numPassesForAcGroup[acGroupId],
                ForceDraw           = flags.ForceDraw,
                DcOnly              = flags.DcOnly,
            };

            return DecGroup.DecodeGroup(in groupArgs, out shouldRunPipeline);
        }

        private Status ProcessModularAcGroupPasses(
            nuint acGroupId,
            (nuint X, nuint Y) pos,
            BitReader?[] brs,
            int numPasses,
            bool forceDraw,
            RenderPipelineInput? renderPipelineInput,
            out bool modularReady)
        {
            Rect mrect = new Rect(
                (int)pos.X,
                (int)pos.Y,
                (int)_frameDim.GroupDim,
                (int)_frameDim.GroupDim,
                (int)_frameDim.XSize,
                (int)_frameDim.YSize);

            uint pass0   = _numPassesForAcGroup[acGroupId];
            uint pass1   = forceDraw ? _frameHeader.Passes.NumPasses : pass0 + (uint)numPasses;
            modularReady = false;

            for (uint i = pass0; i < pass1; i++)
            {
                _frameHeader.Passes.GetDownsamplingBracket(i, out int minShift, out int maxShift);
                bool zerofill         = i >= pass0 + (uint)numPasses;
                bool modularPassReady = true;

                ModularFrameDecoder.DecodeGroupArgs modArgs = new ModularFrameDecoder.DecodeGroupArgs
                {
                    FrameHeader         = _frameHeader,
                    Rect                = mrect,
                    Reader              = zerofill ? null : brs[i - pass0],
                    MinShift            = minShift,
                    MaxShift            = maxShift,
                    Stream              = ModularStreamId.ModularAC(acGroupId, i),
                    Zerofill            = zerofill,
                    DecState            = _decState,
                    RenderPipelineInput = renderPipelineInput,
                    AllowTruncated      = false,
                    ShouldRunPipeline   = &modularPassReady,
                };

                Status status = _modularFrameDecoder.DecodeGroup(in modArgs);

                if (!status.IsOk)
                {
                    return status;
                }

                if (modularPassReady)
                {
                    modularReady = true;
                }
            }

            return true;
        }

        private void EnsureGroupDecCaches(nuint numThreads)
        {
            if ((nuint)_groupDecCaches.Length >= numThreads)
            {
                return;
            }

            int oldLen = _groupDecCaches.Length;
            Array.Resize(ref _groupDecCaches, (int)numThreads);

            for (int i = oldLen; i < (int)numThreads; i++)
            {
                _groupDecCaches[i] = new GroupDecCache();
            }
        }

        private Status PreparePipelineAndCachesForThreads(nuint numThreads)
        {
            EnsureGroupDecCaches(numThreads);

            if (_mode == Mode.Normal && _decState.RenderPipeline != null)
            {
                return _decState.RenderPipeline.PrepareForThreads(
                    numThreads,
                    useGroupIds: _frameHeader.Encoding == FrameEncoding.Modular);
            }

            return true;
        }

        private Status FlushInternal()
        {
            Status allocStatus = AllocateOutput();

            if (!allocStatus.IsOk)
            {
                return allocStatus;
            }

            Status forceStatus = ForceDrawIncompleteGroupsIfNeeded();

            if (!forceStatus.IsOk)
            {
                return forceStatus;
            }

            return _modularFrameDecoder.FinalizeDecoding(
                _frameHeader,
                _decState,
                _pool,
                inplace: _isFinalized);
        }

        private Status ForceDrawIncompleteGroupsIfNeeded()
        {
            uint completePasses = NumCompletePasses();

            if (completePasses >= _frameHeader.Passes.NumPasses)
            {
                return true;
            }

            for (int i = 0; i < _numPassesForAcGroup.Length; i++)
            {
                if (_numPassesForAcGroup[i] < _frameHeader.Passes.NumPasses)
                {
                    _decState.RenderPipeline?.ClearDone((nuint)i);
                }
            }

            BitReader?[] emptyReaders = Array.Empty<BitReader?>();
            nuint numGroups           = _frameDim.NumGroups;

            return DataParallel.RunOnPool(
                _pool,
                0,
                (uint)numGroups,
                PreparePipelineAndCachesForThreads,
                (task, thread) =>
                {
                    if (_numPassesForAcGroup[task] == _frameHeader.Passes.NumPasses)
                    {
                        return true;
                    }

                    return ProcessACGroup(
                        task,
                        emptyReaders,
                        numPasses: 0,
                        thread,
                        forceDraw: true,
                        dcOnly: !_decodedAcGlobal);
                },
                "ForceDrawGroup");
        }

        private bool NeedsRecommendationToUpsample()
        {
            return _frameHeader.Upsampling > 1 || (_frameHeader.Flags & FrameHeader.KNoise) != 0;
        }

        private Status SaveFrameToReferenceSlotIfNeeded()
        {
            if (!_frameHeader.CanBeReferenced() || _decoded == null)
            {
                return true;
            }

            uint slot                   = _frameHeader.SaveAsReference;
            ref ReferenceFrame refFrame = ref _decState.SharedStorage.ReferenceFrames[slot];
            refFrame.IbIsInXyb          = _frameHeader.SaveBeforeColorTransform;

            if (_decState.FrameStorageForReferencing.XSize() > 0)
            {
                refFrame.Frame?.Dispose();
                refFrame.Frame                       = _decState.FrameStorageForReferencing;
                _decState.FrameStorageForReferencing = new ImageBundle(_decState.MemoryManager);
            }

            return true;
        }

        private struct SectionBatchState
        {
            public int DcGlobalIdx;
            public int AcGlobalIdx;
            public readonly int[] DcGroupIndices;
            public readonly int[][] AcGroupPassIndices;
            public readonly uint[] NumPassesToAdd;
            public bool SingleSection;

            public SectionBatchState(in FrameDimensions frameDim, nuint numPasses, int num)
            {
                DcGlobalIdx        = -1;
                AcGlobalIdx        = -1;
                DcGroupIndices     = new int[frameDim.NumDcGroups];
                AcGroupPassIndices = new int[frameDim.NumGroups][];
                NumPassesToAdd     = new uint[frameDim.NumGroups];
                SingleSection      = frameDim.NumGroups == 1 && numPasses == 1;
                Array.Fill(DcGroupIndices, -1);

                for (nuint g = 0; g < frameDim.NumGroups; g++)
                {
                    AcGroupPassIndices[g] = new int[numPasses];
                    Array.Fill(AcGroupPassIndices[g], -1);
                }

                _ = num;
            }
        }

        private Status ClassifySections(
            Span<SectionInfo> sections,
            int num,
            Span<SectionStatus> sectionStatus,
            ref SectionBatchState batch)
        {
            for (int i = 0; i < num; i++)
            {
                nuint id = sections[i].Id;

                if (id >= (nuint)_processedSection.Length)
                {
                    return Status.Failure($"Section {id} out of range");
                }

                if (_processedSection[id] != 0)
                {
                    sectionStatus[i] = SectionStatus.Duplicate;
                    continue;
                }

                sectionStatus[i] = SectionStatus.Skipped;

                if (batch.SingleSection)
                {
                    batch.DcGlobalIdx              = i;
                    batch.AcGlobalIdx              = i;
                    batch.DcGroupIndices[0]        = i;
                    batch.AcGroupPassIndices[0][0] = i;
                    batch.NumPassesToAdd[0]        = 1;
                    sectionStatus[i]               = SectionStatus.Done;
                }
                else
                {
                    ClassifyMultiSectionEntry(i, id, sectionStatus, ref batch);
                }
            }

            ResolveContiguousAcPasses(sectionStatus, ref batch);

            return true;
        }

        private void ClassifyMultiSectionEntry(
            int sectionIdx,
            nuint id,
            Span<SectionStatus> sectionStatus,
            ref SectionBatchState batch)
        {
            nuint numDcGroups = _frameDim.NumDcGroups;
            nuint numGroups   = _frameDim.NumGroups;

            if (id == 0)
            {
                batch.DcGlobalIdx         = sectionIdx;
                sectionStatus[sectionIdx] = SectionStatus.Done;
            }
            else if (id < 1u + numDcGroups)
            {
                nuint dcGroupId = id - 1u;

                if (_decodedDcGlobal || batch.DcGlobalIdx != -1)
                {
                    batch.DcGroupIndices[dcGroupId] = sectionIdx;
                    sectionStatus[sectionIdx]       = SectionStatus.Done;
                }
            }
            else if (id == 1u + numDcGroups)
            {
                batch.AcGlobalIdx = sectionIdx;
            }
            else
            {
                nuint acIdx = id - 2u - numDcGroups;
                nuint pass  = acIdx / numGroups;
                nuint group = acIdx % numGroups;

                if (pass < _maxPasses)
                {
                    batch.AcGroupPassIndices[group][pass] = sectionIdx;
                }
                else
                {
                    sectionStatus[sectionIdx] = SectionStatus.Partial;
                }
            }
        }

        private void ResolveContiguousAcPasses(
            Span<SectionStatus> sectionStatus,
            ref SectionBatchState batch)
        {
            if (batch.SingleSection)
            {
                return;
            }

            bool willHaveDcGlobal = _decodedDcGlobal || batch.DcGlobalIdx != -1;
            int dcGroupsReady     = 0;

            for (int g = 0; g < _decodedDcGroups.Length; g++)
            {
                if (_decodedDcGroups[g] != 0 || batch.DcGroupIndices[g] != -1)
                {
                    dcGroupsReady++;
                }
            }

            bool allDcReady = willHaveDcGlobal && dcGroupsReady == _decodedDcGroups.Length;

            if (!allDcReady)
            {
                batch.AcGlobalIdx = -1;

                return;
            }

            if (batch.AcGlobalIdx != -1)
            {
                sectionStatus[batch.AcGlobalIdx] = SectionStatus.Done;
            }

            bool willHaveAcGlobal = _decodedAcGlobal || batch.AcGlobalIdx != -1;

            if (!willHaveAcGlobal)
            {
                return;
            }

            nuint pauseLimit = _progressiveDetail >= JxlProgressiveDetail.LastPasses
                ? Math.Min(_maxPasses, NextNumPassesToPause())
                : _maxPasses;

            for (nuint g = 0; g < _frameDim.NumGroups; g++)
            {
                uint curPass = _numPassesForAcGroup[g];

                while (curPass + batch.NumPassesToAdd[g] < pauseLimit)
                {
                    int secIdx = batch.AcGroupPassIndices[g][curPass + batch.NumPassesToAdd[g]];

                    if (secIdx == -1)
                    {
                        break;
                    }

                    sectionStatus[secIdx] = SectionStatus.Done;
                    batch.NumPassesToAdd[g]++;
                }
            }
        }

        private Status ExecuteDcSections(Span<SectionInfo> sections, ref SectionBatchState batch)
        {
            if (batch.DcGlobalIdx != -1)
            {
                Status dcGlobStatus = ProcessDCGlobal(sections[batch.DcGlobalIdx].Br!);

                if (!dcGlobStatus.IsOk)
                {
                    return dcGlobStatus;
                }
            }

            for (nuint dcGroup = 0; dcGroup < (nuint)batch.DcGroupIndices.Length; dcGroup++)
            {
                int secIdx = batch.DcGroupIndices[dcGroup];

                if (secIdx != -1)
                {
                    Status dcGroupStatus = ProcessDCGroup(dcGroup, sections[secIdx].Br!);

                    if (!dcGroupStatus.IsOk)
                    {
                        return dcGroupStatus;
                    }
                }
            }

            return true;
        }

        private Status TryFinalizeDcAndAllocateOutput()
        {
            if (_finalizedDc)
            {
                return true;
            }

            if (!_decodedDcGlobal)
            {
                return true;
            }

            for (int i = 0; i < _decodedDcGroups.Length; i++)
            {
                if (_decodedDcGroups[i] == 0)
                {
                    return true;
                }
            }

            Status pipeStatus = PrepareRenderPipelineForFrame();

            if (!pipeStatus.IsOk)
            {
                return pipeStatus;
            }

            Status finDcStatus = FinalizeDC();

            if (!finDcStatus.IsOk)
            {
                return finDcStatus;
            }

            return AllocateOutput();
        }

        private Status ExecuteAcSections(
            Span<SectionInfo> sections,
            Span<SectionStatus> sectionStatus,
            ref SectionBatchState batch)
        {
            _ = sectionStatus;

            if (batch.AcGlobalIdx != -1)
            {
                Status acGlobStatus = ProcessACGlobal(sections[batch.AcGlobalIdx].Br!);

                if (!acGlobStatus.IsOk)
                {
                    return acGlobStatus;
                }
            }

            if (!_decodedAcGlobal)
            {
                return true;
            }

            bool hasAnyAcGroup = false;

            for (int g = 0; g < batch.NumPassesToAdd.Length; g++)
            {
                if (batch.NumPassesToAdd[g] > 0)
                {
                    hasAnyAcGroup = true;
                    break;
                }
            }

            if (!hasAnyAcGroup)
            {
                return true;
            }

            Status prepStatus = PreparePipelineAndCachesForThreads(1);

            if (!prepStatus.IsOk)
            {
                return prepStatus;
            }

            ClearRenderPipelineDoneForNewPasses(batch.NumPassesToAdd);

            for (nuint g = 0; g < _frameDim.NumGroups; g++)
            {
                uint count = batch.NumPassesToAdd[g];

                if (count == 0)
                {
                    continue;
                }

                BitReader?[] groupBrs = new BitReader?[count];
                uint basePass         = _numPassesForAcGroup[g];

                for (uint p = 0; p < count; p++)
                {
                    int secIdx  = batch.AcGroupPassIndices[g][basePass + p];
                    groupBrs[p] = sections[secIdx].Br;
                }

                Status groupStatus = ProcessACGroup(
                    g,
                    groupBrs,
                    (int)count,
                    thread: 0,
                    forceDraw: false,
                    dcOnly: false);

                if (!groupStatus.IsOk)
                {
                    return groupStatus;
                }

                _hasBeenFlushed |= (basePass + count == _frameHeader.Passes.NumPasses);
            }

            return true;
        }

        private void ClearRenderPipelineDoneForNewPasses(ReadOnlySpan<uint> numPassesToAdd)
        {
            if (_decState.RenderPipeline == null)
            {
                return;
            }

            for (nuint g = 0; g < _frameDim.NumGroups; g++)
            {
                if (numPassesToAdd[(int)g] != 0)
                {
                    _decState.RenderPipeline.ClearDone(g);
                }
            }
        }

        private void RecordProcessedSections(
            Span<SectionInfo> sections,
            int num,
            Span<SectionStatus> sectionStatus)
        {
            for (int i = 0; i < num; i++)
            {
                if (sectionStatus[i] == SectionStatus.Done || sectionStatus[i] == SectionStatus.Partial)
                {
                    nuint id = sections[i].Id;

                    if (_processedSection[id] == 0)
                    {
                        _processedSection[id] = 1;
                        _numSectionsDone++;
                    }
                }
            }
        }

        private static uint MinValue(uint[] values)
        {
            uint min = values[0];

            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < min)
                {
                    min = values[i];
                }
            }

            return min;
        }
    }
}
