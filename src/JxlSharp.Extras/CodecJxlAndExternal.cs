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
using System.Runtime.InteropServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Interface for parallel runner adapters used by <see cref="JXLCompressParams"/> and <see cref="JXLDecompressParams"/>.
    /// </summary>
    public interface IParallelRunner
    {
        /// <summary>Gets the parallel runner delegate.</summary>
        JxlParallelRunner? Runner { get; }

        /// <summary>Gets the opaque runner state.</summary>
        object? RunnerOpaque { get; }
    }

    /// <summary>
    /// JPEG XL image decoder and encoder wrapper around <see cref="JxlDecoder"/> and <see cref="JxlEncoder"/>.
    /// Ported from <c>lib/extras/dec/jxl.cc</c> and <c>lib/extras/enc/jxl.cc</c>.
    /// </summary>
    public static class CodecJxl
    {
        /// <summary>
        /// Decodes a JPEG XL codestream or container from <paramref name="bytes"/> into <paramref name="ppf"/>.
        /// Ported from <c>jxl::extras::DecodeImageJXL</c> in <c>lib/extras/dec/jxl.cc</c>.
        /// </summary>
        /// <param name="bytes">Input JPEG XL bytes.</param>
        /// <param name="dparams">Decompression parameters.</param>
        /// <param name="decodedBytes">Outputs the number of input bytes consumed.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="jpegBytes">Optional output buffer for byte-exact JPEG reconstruction.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool DecodeImageJXL(
            ReadOnlySpan<byte> bytes,
            JXLDecompressParams dparams,
            out nuint decodedBytes,
            PackedPixelFile ppf,
            List<byte>? jpegBytes = null,
            SizeConstraints? constraints = null)
        {
            Status.ClearLastFailure();
            decodedBytes = 0;

            using JxlDecoder dec = JxlDecoder.Create(dparams.MemoryManager);

            if (!ConfigureDecoderOptions(dec, dparams, jpegBytes is not null))
            {
                return false;
            }

            byte[] inputBytes = bytes.ToArray();

            if (dec.SetInput(inputBytes) != JxlDecoderStatus.Success)
            {
                return false;
            }

            JxlDecodeLoopState state = new JxlDecodeLoopState(dec, dparams, ppf, jpegBytes, constraints, inputBytes.Length);

            if (!RunDecoderEventLoop(state))
            {
                return false;
            }

            state.FinalizeBuffers();

            if (!dparams.KeepOrientation && ppf.Metadata.Exif.Length > 0)
            {
                ExtrasExif.ResetExifOrientation(ppf.Metadata.Exif.AsSpan());
            }

            nuint remaining = dec.ReleaseInput();
            decodedBytes    = (nuint)inputBytes.Length - remaining;

            return ppf.Frames.Count > 0 || (jpegBytes is not null && jpegBytes.Count > 0);
        }

        /// <summary>
        /// Encodes a <see cref="PackedPixelFile"/> (or lossless JPEG bitstream) into JPEG XL format.
        /// Ported from <c>jxl::extras::EncodeImageJXL</c> in <c>lib/extras/enc/jxl.cc</c>.
        /// </summary>
        /// <param name="cparams">Compression parameters.</param>
        /// <param name="ppf">Input packed pixel file.</param>
        /// <param name="jpegBytes">Optional raw JPEG bytes for lossless JPEG transcoding.</param>
        /// <param name="compressed">Receives the encoded JPEG XL byte array.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool EncodeImageJXL(
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            byte[]? jpegBytes,
            out byte[] compressed)
        {
            compressed = Array.Empty<byte>();

            using JxlEncoder enc = new JxlEncoder(cparams.MemoryManager);

            if (!ConfigureEncoderInstance(enc, cparams, ppf, jpegBytes, out JxlEncoderFrameSettings? settings) || settings is null)
            {
                return false;
            }

            bool hasBoxes = HasMetadataBoxes(ppf);

            if (jpegBytes is not null)
            {
                if (!EncodeJpegTranscode(enc, settings, cparams, ppf, jpegBytes, hasBoxes))
                {
                    return false;
                }
            }
            else
            {
                if (!EncodePpfFrames(enc, settings, cparams, ppf, hasBoxes))
                {
                    return false;
                }
            }

            enc.CloseInput();

            return ReadCompressedOutput(enc, out compressed);
        }

        private static bool ConfigureDecoderOptions(
            JxlDecoder dec,
            JXLDecompressParams dparams,
            bool wantJpeg)
        {
            if (dparams.Runner is not null &&
                dparams.Runner.Runner is not null &&
                dec.SetParallelRunner(dparams.Runner.Runner, dparams.Runner.RunnerOpaque) != JxlDecoderStatus.Success)
            {
                return false;
            }

            int events = (int)(JxlDecoderStatus.BasicInfo | JxlDecoderStatus.FullImage);
            events    |= wantJpeg
                ? (int)(JxlDecoderStatus.JpegReconstruction | JxlDecoderStatus.Box)
                : (int)(JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.Frame | JxlDecoderStatus.PreviewImage | JxlDecoderStatus.Box);

            bool maxPassesDefined = dparams.MaxPasses < uint.MaxValue;

            if (!wantJpeg && (maxPassesDefined || dparams.MaxDownsampling > 1))
            {
                events |= (int)JxlDecoderStatus.FrameProgression;
                JxlProgressiveDetail progDetail = maxPassesDefined
                    ? JxlProgressiveDetail.Passes
                    : JxlProgressiveDetail.LastPasses;

                if (dec.SetProgressiveDetail(progDetail) != JxlDecoderStatus.Success)
                {
                    return false;
                }
            }

            if (dec.SubscribeEvents(events) != JxlDecoderStatus.Success ||
                dec.SetRenderSpotcolors(dparams.RenderSpotcolors) != JxlDecoderStatus.Success ||
                dec.SetKeepOrientation(dparams.KeepOrientation) != JxlDecoderStatus.Success ||
                dec.SetUnpremultiplyAlpha(dparams.UnpremultiplyAlpha) != JxlDecoderStatus.Success ||
                dec.SetCoalescing(dparams.Coalescing) != JxlDecoderStatus.Success ||
                dec.SetDecompressBoxes(true) != JxlDecoderStatus.Success)
            {
                return false;
            }

            if (dparams.DisplayNits > 0.0f &&
                dec.SetDesiredIntensityTarget(dparams.DisplayNits) != JxlDecoderStatus.Success)
            {
                return false;
            }

            return true;
        }

        private static bool RunDecoderEventLoop(JxlDecodeLoopState state)
        {
            List<JxlDecoderStatus> history = new List<JxlDecoderStatus>();

            while (!state.Done)
            {
                JxlDecoderStatus status = state.Dec.ProcessInput();
                history.Add(status);

                if (!state.HandleEvent(status))
                {
                    string prev = Status.LastFailureMessage ?? "none";
                    _ = Status.Failure($"DecodeImageJXL event loop failed at status={status} (history={string.Join("->", history)}, prev={prev})");

                    return false;
                }
            }

            return true;
        }

        private static bool ConfigureEncoderInstance(
            JxlEncoder enc,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            byte[]? jpegBytes,
            out JxlEncoderFrameSettings? settings)
        {
            settings = null;

            if (cparams.AllowExpertOptions)
            {
                enc.AllowExpertOptions();
            }

            if (cparams.Runner is not null &&
                cparams.Runner.Runner is not null &&
                enc.SetParallelRunner(new ThreadPool(cparams.Runner.Runner, cparams.Runner.RunnerOpaque)) != JxlEncoderStatus.Success)
            {
                return false;
            }

            settings = enc.FrameSettingsCreate(null);
            int optIdx = 0;

            if (!ApplyFrameOptions(enc, settings, cparams.Options, 0, ref optIdx))
            {
                return false;
            }

            if (enc.SetFrameDistance(settings, cparams.Distance) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (cparams.Stats is not null)
            {
                enc.CollectStats(settings, cparams.Stats);
            }

            bool hasBoxes     = HasMetadataBoxes(ppf);
            bool useContainer = cparams.UseContainer || hasBoxes || (jpegBytes is not null && cparams.JpegStoreMetadata);

            return enc.UseContainer(useContainer) == JxlEncoderStatus.Success;
        }

        private static bool ApplyFrameOptions(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            List<JXLOption> options,
            nuint frameIndex,
            ref int optIdx)
        {
            while (optIdx < options.Count && options[optIdx].FrameIndex == frameIndex)
            {
                JXLOption opt           = options[optIdx];
                JxlEncoderStatus status = opt.IsFloat
                    ? enc.FrameSettingsSetFloatOption(settings, opt.Id, opt.FVal)
                    : enc.FrameSettingsSetOption(settings, opt.Id, opt.IVal);

                if (status != JxlEncoderStatus.Success)
                {
                    return false;
                }

                optIdx++;
            }

            return true;
        }

        private static bool HasMetadataBoxes(PackedPixelFile ppf)
        {
            return ppf.Metadata.Exif.Length > 0 ||
                   ppf.Metadata.Xmp.Length > 0 ||
                   ppf.Metadata.Jhgm.Length > 0 ||
                   ppf.Metadata.Jumbf.Length > 0 ||
                   ppf.Metadata.Iptc.Length > 0;
        }

        private static bool EncodeJpegTranscode(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            byte[] jpegBytes,
            bool hasBoxes)
        {
            if (cparams.JpegStoreMetadata && enc.StoreJPEGMetadata(true) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (hasBoxes && !WriteMetadataBoxes(enc, ppf, cparams.CompressBoxes))
            {
                return false;
            }

            return enc.AddJPEGFrame(settings, jpegBytes) == JxlEncoderStatus.Success;
        }

        private static bool EncodePpfFrames(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            bool hasBoxes)
        {
            if (ppf.Frames.Count == 0 && ppf.ChunkedFrames.Count == 0)
            {
                return false;
            }

            bool lossless        = cparams.Distance == 0.0f;
            JxlBasicInfo info    = BuildEncoderBasicInfo(cparams, ppf, lossless, out uint numAlphaChannels);

            if (cparams.CodestreamLevel != -1 && enc.SetCodestreamLevel(cparams.CodestreamLevel) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (cparams.UpsamplingMode != -1 &&
                enc.SetUpsamplingMode(cparams.AlreadyDownsampled, cparams.UpsamplingMode) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (enc.SetBasicInfo(in info) != JxlEncoderStatus.Success ||
                enc.SetFrameBitDepth(settings, in ppf.InputBitdepth) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (numAlphaChannels != 0 &&
                enc.SetExtraChannelDistance(settings, 0, cparams.AlphaDistance) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (lossless && enc.SetFrameLossless(settings, true) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (!ConfigureEncoderColorSpace(enc, ppf))
            {
                return false;
            }

            if (hasBoxes && !WriteMetadataBoxes(enc, ppf, cparams.CompressBoxes))
            {
                return false;
            }

            return AddAllPpfFrames(enc, settings, cparams, ppf, numAlphaChannels);
        }

        private static JxlBasicInfo BuildEncoderBasicInfo(
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            bool lossless,
            out uint numAlphaChannels)
        {
            JxlBasicInfo info = ppf.Info;

            if (cparams.IntensityTarget > 0.0f)
            {
                info.IntensityTarget = cparams.IntensityTarget;
            }

            if (cparams.OverrideBitdepth != 0)
            {
                info.BitsPerSample = (uint)cparams.OverrideBitdepth;
                info.AlphaBits     = info.AlphaBits > 0 ? (uint)cparams.OverrideBitdepth : 0u;
            }

            if (cparams.StripAlpha == 1)
            {
                info.AlphaBits = 0;
            }

            numAlphaChannels      = info.AlphaBits > 0 ? 1u : 0u;
            info.NumExtraChannels = numAlphaChannels + (uint)ppf.ExtraChannelsInfo.Count;

            if (cparams.Premultiply != -1)
            {
                info.AlphaPremultiplied = cparams.Premultiply != 0;
            }

            if (cparams.AlreadyDownsampled > 1)
            {
                info.XSize *= (uint)cparams.AlreadyDownsampled;
                info.YSize *= (uint)cparams.AlreadyDownsampled;
            }

            if (lossless)
            {
                info.UsesOriginalProfile = true;
            }

            return info;
        }

        private static bool ConfigureEncoderColorSpace(JxlEncoder enc, PackedPixelFile ppf)
        {
            if (ppf.PrimaryColorRepresentation == PrimaryColorRepresentation.IccIsPrimary && ppf.Icc.Length > 0)
            {
                return enc.SetICCProfile(ppf.Icc) == JxlEncoderStatus.Success;
            }

            return enc.SetColorEncoding(in ppf.ColorEncoding) == JxlEncoderStatus.Success;
        }

        private static bool WriteMetadataBoxes(JxlEncoder enc, PackedPixelFile ppf, bool compressBoxes)
        {
            if (enc.UseBoxes() != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (ppf.Metadata.Exif.Length > 0 && Exif.IsExif(ppf.Metadata.Exif, out _))
            {
                byte[] exifBox = new byte[ppf.Metadata.Exif.Length + 4];
                Buffer.BlockCopy(ppf.Metadata.Exif, 0, exifBox, 4, ppf.Metadata.Exif.Length);

                if (enc.AddBox("Exif", exifBox, compressBoxes) != JxlEncoderStatus.Success)
                {
                    return false;
                }
            }

            if (!AddSingleBoxIfNonEmpty(enc, "xml ", ppf.Metadata.Xmp, compressBoxes) ||
                !AddSingleBoxIfNonEmpty(enc, "jumb", ppf.Metadata.Jumbf, compressBoxes) ||
                !AddSingleBoxIfNonEmpty(enc, "xml ", ppf.Metadata.Iptc, compressBoxes) ||
                !AddSingleBoxIfNonEmpty(enc, "jhgm", ppf.Metadata.Jhgm, compressBoxes))
            {
                return false;
            }

            enc.CloseBoxes();

            return true;
        }

        private static bool AddSingleBoxIfNonEmpty(
            JxlEncoder enc,
            string boxTypeStr,
            byte[] payload,
            bool compressBoxes)
        {
            if (payload.Length == 0)
            {
                return true;
            }

            return enc.AddBox(boxTypeStr, payload, compressBoxes) == JxlEncoderStatus.Success;
        }

        private static bool AddAllPpfFrames(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            uint numAlphaChannels)
        {
            int optIdx = 0;

            while (optIdx < cparams.Options.Count && cparams.Options[optIdx].FrameIndex == 0)
            {
                optIdx++;
            }

            for (int fIdx = 0; fIdx < ppf.Frames.Count; fIdx++)
            {
                PackedFrame pframe       = ppf.Frames[fIdx];
                PackedImage pimage       = pframe.Color;
                JxlPixelFormat format    = pimage.Format;
                nuint interleavedAlpha   = format.NumChannels - ppf.Info.NumColorChannels;

                if (!SetupEncoderFrame(enc, settings, pframe, cparams, ppf, (nuint)fIdx, numAlphaChannels, interleavedAlpha, ref optIdx))
                {
                    return false;
                }

                if (enc.AddImageFrame(settings, in format, pimage.Pixels().Slice(0, (int)pimage.PixelsSize)) != JxlEncoderStatus.Success)
                {
                    return false;
                }

                if (!AddExtraChannelBuffers(enc, settings, pframe, interleavedAlpha))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SetupEncoderFrame(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            PackedFrame pframe,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            nuint frameIdx,
            uint numAlphaChannels,
            nuint interleavedAlpha,
            ref int optIdx)
        {
            if (!ApplyFrameOptions(enc, settings, cparams.Options, frameIdx, ref optIdx))
            {
                return false;
            }

            JxlFrameHeader frameHeader = pframe.FrameInfo;

            if (enc.SetFrameHeader(settings, in frameHeader) != JxlEncoderStatus.Success)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(pframe.Name) &&
                enc.SetFrameName(settings, pframe.Name) != JxlEncoderStatus.Success)
            {
                return false;
            }

            return ConfigureFrameExtraChannels(enc, settings, in frameHeader, cparams, ppf, numAlphaChannels, interleavedAlpha);
        }

        private static bool ConfigureFrameExtraChannels(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            in JxlFrameHeader frameHeader,
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            uint numAlphaChannels,
            nuint interleavedAlpha)
        {
            if (numAlphaChannels > 0)
            {
                JxlEncoder.JxlEncoderInitExtraChannelInfo(JxlExtraChannelType.Alpha, out JxlExtraChannelInfo alphaInfo);
                alphaInfo.BitsPerSample         = ppf.Info.AlphaBits;
                alphaInfo.ExponentBitsPerSample = ppf.Info.AlphaExponentBits;
                alphaInfo.AlphaPremultiplied    = cparams.Premultiply != -1
                    ? cparams.Premultiply != 0
                    : ppf.Info.AlphaPremultiplied;

                if (enc.SetExtraChannelInfo(0, in alphaInfo) != JxlEncoderStatus.Success)
                {
                    return false;
                }

                JxlBlendInfo blendInfo = frameHeader.LayerInfo.BlendInfo;
                blendInfo.Clamp        = false;
                enc.SetExtraChannelBlendInfo(settings, 0, in blendInfo);
            }

            for (int i = 0; i < ppf.ExtraChannelsInfo.Count; i++)
            {
                nuint index            = interleavedAlpha + (nuint)i;
                JxlExtraChannelInfo ec = ppf.ExtraChannelsInfo[i].EcInfo;

                if (enc.SetExtraChannelInfo(index, in ec) != JxlEncoderStatus.Success)
                {
                    return false;
                }

                string ecName = ppf.ExtraChannelsInfo[i].Name;

                if (!string.IsNullOrEmpty(ecName) &&
                    enc.SetExtraChannelName(index, ecName) != JxlEncoderStatus.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AddExtraChannelBuffers(
            JxlEncoder enc,
            JxlEncoderFrameSettings settings,
            PackedFrame pframe,
            nuint interleavedAlpha)
        {
            for (int i = 0; i < pframe.ExtraChannels.Count; i++)
            {
                PackedImage ec           = pframe.ExtraChannels[i];
                JxlPixelFormat ecFormat  = ec.Format;
                int byteLen              = (int)(ec.Stride * ec.YSize);
                uint index               = (uint)(interleavedAlpha + (nuint)i);

                if (enc.SetExtraChannelBuffer(settings, in ecFormat, ec.Pixels().Slice(0, byteLen), index) != JxlEncoderStatus.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ReadCompressedOutput(JxlEncoder enc, out byte[] compressed)
        {
            byte[] buffer          = new byte[4096];
            int offset             = 0;
            JxlEncoderStatus state = JxlEncoderStatus.NeedMoreOutput;

            while (state == JxlEncoderStatus.NeedMoreOutput)
            {
                state = enc.ProcessOutput(buffer.AsSpan(), ref offset);

                if (state == JxlEncoderStatus.NeedMoreOutput)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }
            }

            if (state != JxlEncoderStatus.Success)
            {
                compressed = Array.Empty<byte>();

                return false;
            }

            compressed = new byte[offset];
            Buffer.BlockCopy(buffer, 0, compressed, 0, offset);

            return true;
        }

        private sealed class JxlDecodeLoopState
        {
            public readonly JxlDecoder Dec;
            public readonly JXLDecompressParams DParams;
            public readonly PackedPixelFile Ppf;
            public readonly List<byte>? JpegBytes;
            public readonly SizeConstraints? Constraints;
            public bool Done;

            private JxlPixelFormat _format;
            private bool _cmyk;
            private bool _codestreamDone;
            private uint _numPasses;
            private byte[]? _jpegBuffer;
            private string? _activeBoxType;
            private byte[]? _boxBuffer;

            public JxlDecodeLoopState(
                JxlDecoder dec,
                JXLDecompressParams dparams,
                PackedPixelFile ppf,
                List<byte>? jpegBytes,
                SizeConstraints? constraints,
                int inputLength)
            {
                Dec         = dec;
                DParams     = dparams;
                Ppf         = ppf;
                JpegBytes   = jpegBytes;
                Constraints = constraints;
                _jpegBuffer = jpegBytes is not null ? new byte[Math.Max(128, inputLength)] : null;
            }

            public bool HandleEvent(JxlDecoderStatus status)
            {
                switch (status)
                {
                    case JxlDecoderStatus.Error:
                        return false;

                    case JxlDecoderStatus.NeedMoreInput:
                        return HandleNeedMoreInput();

                    case JxlDecoderStatus.JpegReconstruction:
                        return HandleJpegReconstruction();

                    case JxlDecoderStatus.JpegNeedMoreOutput:
                        return HandleJpegNeedMoreOutput();

                    case JxlDecoderStatus.Box:
                        return HandleBox();

                    case JxlDecoderStatus.BoxNeedMoreOutput:
                        return HandleBoxNeedMoreOutput();

                    case JxlDecoderStatus.BasicInfo:
                        return HandleBasicInfo();

                    case JxlDecoderStatus.ColorEncoding:
                        return HandleColorEncoding();

                    case JxlDecoderStatus.NeedPreviewOutBuffer:
                        return HandlePreviewImage();

                    case JxlDecoderStatus.PreviewImage:
                        return true;

                    case JxlDecoderStatus.Frame:
                        return HandleFrame();

                    case JxlDecoderStatus.FrameProgression:
                        return HandleFrameProgression();

                    case JxlDecoderStatus.NeedImageOutBuffer:
                        return HandleNeedImageOutBuffer();

                    case JxlDecoderStatus.FullImage:
                        if (JpegBytes is not null || (Ppf.Frames.Count > 0 && Ppf.Frames[Ppf.Frames.Count - 1].FrameInfo.IsLast))
                        {
                            _codestreamDone = true;
                        }

                        if (JpegBytes is not null)
                        {
                            Done = true;
                        }

                        return true;

                    case JxlDecoderStatus.Success:
                        Done = true;

                        return true;

                    default:
                        return false;
                }
            }

            public void FinalizeBuffers()
            {
                CloseCurrentBox();

                if (JpegBytes is not null && _jpegBuffer is not null)
                {
                    nuint unconsumed = Dec.ReleaseJPEGBuffer();
                    int written      = _jpegBuffer.Length - (int)unconsumed;

                    if (written > 0)
                    {
                        JpegBytes.Clear();

                        for (int i = 0; i < written; i++)
                        {
                            JpegBytes.Add(_jpegBuffer[i]);
                        }
                    }
                }
            }

            private bool HandleNeedMoreInput()
            {
                if (_codestreamDone)
                {
                    Done = true;

                    return true;
                }

                if (DParams.AllowPartialInput && Dec.FlushImage() == JxlDecoderStatus.Success)
                {
                    Done = true;

                    return true;
                }

                return false;
            }

            private bool HandleJpegReconstruction()
            {
                if (_jpegBuffer is null)
                {
                    return false;
                }

                return Dec.SetJPEGBuffer(_jpegBuffer, 0, _jpegBuffer.Length) == JxlDecoderStatus.Success;
            }

            private bool HandleJpegNeedMoreOutput()
            {
                if (_jpegBuffer is null)
                {
                    return false;
                }

                nuint unconsumed = Dec.ReleaseJPEGBuffer();
                int written      = _jpegBuffer.Length - (int)unconsumed;
                Array.Resize(ref _jpegBuffer, _jpegBuffer.Length * 2);

                return Dec.SetJPEGBuffer(_jpegBuffer, written, _jpegBuffer.Length - written) == JxlDecoderStatus.Success;
            }

            private bool HandleBox()
            {
                CloseCurrentBox();

                if (Dec.GetBoxType(out JxlBoxType boxType, true) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                string typeName = boxType.ToString();

                if (typeName != "Exif" && typeName != "xml " && typeName != "jumb" && typeName != "jhgm" && typeName != "iptc")
                {
                    return true;
                }

                _activeBoxType = typeName;
                _boxBuffer     = new byte[1024];

                return Dec.SetBoxBuffer(_boxBuffer, 0, _boxBuffer.Length) == JxlDecoderStatus.Success;
            }

            private bool HandleBoxNeedMoreOutput()
            {
                if (_boxBuffer is null)
                {
                    return false;
                }

                nuint remaining = Dec.ReleaseBoxBuffer();
                int written     = _boxBuffer.Length - (int)remaining;
                Array.Resize(ref _boxBuffer, _boxBuffer.Length * 2);

                return Dec.SetBoxBuffer(_boxBuffer, written, _boxBuffer.Length - written) == JxlDecoderStatus.Success;
            }

            private void CloseCurrentBox()
            {
                if (_activeBoxType is null || _boxBuffer is null)
                {
                    return;
                }

                nuint remaining = Dec.ReleaseBoxBuffer();
                int written     = _boxBuffer.Length - (int)remaining;
                byte[] data     = new byte[Math.Max(0, written)];

                if (written > 0)
                {
                    Buffer.BlockCopy(_boxBuffer, 0, data, 0, written);
                }

                StoreBoxMetadata(_activeBoxType, data);
                _activeBoxType = null;
                _boxBuffer     = null;
            }

            private void StoreBoxMetadata(string typeName, byte[] data)
            {
                switch (typeName)
                {
                    case "Exif":
                        if (data.Length >= 4)
                        {
                            uint tiffOffset = ByteOrder.LoadBE32(data);

                            if (4u + tiffOffset < (uint)data.Length)
                            {
                                int start         = (int)(4u + tiffOffset);
                                byte[] stripped   = new byte[data.Length - start];
                                Buffer.BlockCopy(data, start, stripped, 0, stripped.Length);
                                Ppf.Metadata.Exif = stripped;
                            }
                        }

                        break;

                    case "xml ":
                        Ppf.Metadata.Xmp = data;
                        break;

                    case "jumb":
                        Ppf.Metadata.Jumbf = data;
                        break;

                    case "jhgm":
                        Ppf.Metadata.Jhgm = data;
                        break;

                    case "iptc":
                        Ppf.Metadata.Iptc = data;
                        break;
                }
            }

            private bool HandleBasicInfo()
            {
                if (Dec.GetBasicInfo(out JxlBasicInfo info) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                Ppf.Info = info;

                if (!ExtrasCommon.VerifyDimensions(Constraints, info.XSize, info.YSize))
                {
                    return false;
                }

                if (!ReadExtraChannelsInfo(in info))
                {
                    return false;
                }

                if (JpegBytes is not null)
                {
                    return true;
                }

                IReadOnlyList<JxlPixelFormat> formats = DParams.AcceptedFormats.Count > 0
                    ? DParams.AcceptedFormats
                    : BuildDefaultAcceptedFormats(in info);

                return ExtrasCommon.SelectFormat(formats, in info, out _format);
            }

            private bool ReadExtraChannelsInfo(in JxlBasicInfo info)
            {
                Ppf.ExtraChannelsInfo.Clear();
                bool seenAlpha = false;

                for (nuint i = 0; i < info.NumExtraChannels; i++)
                {
                    if (Dec.GetExtraChannelInfo(i, out JxlExtraChannelInfo ec) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    string ecName = string.Empty;

                    if (ec.NameLength > 0)
                    {
                        byte[] nameBytes = new byte[ec.NameLength + 1];

                        if (Dec.GetExtraChannelName(i, nameBytes) != JxlDecoderStatus.Success)
                        {
                            return false;
                        }

                        ecName = Encoding.UTF8.GetString(nameBytes, 0, (int)ec.NameLength);
                    }

                    if (ec.Type == JxlExtraChannelType.Black)
                    {
                        _cmyk = true;
                    }

                    if (ec.Type == JxlExtraChannelType.Alpha &&
                        !seenAlpha &&
                        ec.BitsPerSample == info.AlphaBits &&
                        ec.ExponentBitsPerSample == info.AlphaExponentBits &&
                        ec.AlphaPremultiplied == info.AlphaPremultiplied)
                    {
                        seenAlpha = true;
                        continue;
                    }

                    if (DParams.RenderSpotcolors && ec.Type == JxlExtraChannelType.SpotColor)
                    {
                        continue;
                    }

                    Ppf.ExtraChannelsInfo.Add(new PackedExtraChannel
                    {
                        EcInfo = ec,
                        Index  = i,
                        Name   = ecName,
                    });
                }

                return true;
            }

            private static List<JxlPixelFormat> BuildDefaultAcceptedFormats(in JxlBasicInfo info)
            {
                List<JxlPixelFormat> list = new List<JxlPixelFormat>();
                uint numChannels          = info.NumColorChannels + (info.AlphaBits != 0 ? 1u : 0u);
                JxlDataType[] types       = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };

                foreach (JxlDataType dt in types)
                {
                    list.Add(new JxlPixelFormat
                    {
                        NumChannels = numChannels,
                        DataType    = dt,
                        Endianness  = JxlEndianness.NativeEndian,
                        Align       = 0,
                    });
                }

                return list;
            }

            private bool HandleColorEncoding()
            {
                if (Dec.SetCms(JxlCms.GetDefaultCms()) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                string requestedCs = _cmyk && !string.IsNullOrEmpty(DParams.ColorSpaceForCmyk)
                    ? DParams.ColorSpaceForCmyk
                    : DParams.ColorSpace;

                if (!string.IsNullOrEmpty(requestedCs))
                {
                    if (!ColorDescription.ParseDescription(requestedCs, out JxlColorEncoding colorSpace) ||
                        Dec.SetOutputColorProfile(colorSpace, ReadOnlySpan<byte>.Empty) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }
                }

                if (!ExtractIccProfiles())
                {
                    return false;
                }

                if (Dec.GetColorAsEncodedProfile(JxlColorProfileTarget.Data, out JxlColorEncoding encProfile) == JxlDecoderStatus.Success)
                {
                    Ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.ColorEncodingIsPrimary;
                    Ppf.ColorEncoding              = encProfile;
                }
                else
                {
                    Ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
                }

                return true;
            }

            private bool ExtractIccProfiles()
            {
                if (Dec.GetICCProfileSize(JxlColorProfileTarget.Data, out nuint iccSize) == JxlDecoderStatus.Success &&
                    iccSize > 0)
                {
                    byte[] icc = new byte[iccSize];

                    if (Dec.GetColorAsICCProfile(JxlColorProfileTarget.Data, icc) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    Ppf.Icc = icc;
                }

                if (Dec.GetICCProfileSize(JxlColorProfileTarget.Original, out nuint origIccSize) == JxlDecoderStatus.Success &&
                    origIccSize > 0)
                {
                    byte[] origIcc = new byte[origIccSize];

                    if (Dec.GetColorAsICCProfile(JxlColorProfileTarget.Original, origIcc) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    Ppf.OrigIcc = origIcc;
                }

                return true;
            }

            private bool HandlePreviewImage()
            {
                uint xsize = Ppf.Info.Preview.XSize;
                uint ysize = Ppf.Info.Preview.YSize;

                StatusOr<PackedFrame> previewOr = PackedFrame.Create(xsize, ysize, in _format);

                if (!previewOr.IsOk)
                {
                    return false;
                }

                PackedFrame preview = previewOr.Value;
                Ppf.PreviewFrame    = preview;

                if (Dec.PreviewOutBufferSize(_format, out nuint previewSize) != JxlDecoderStatus.Success ||
                    previewSize != preview.Color.PixelsSize)
                {
                    return false;
                }

                return Dec.SetPreviewOutBuffer(_format, preview.Color.PixelsBuffer) == JxlDecoderStatus.Success;
            }

            private bool HandleFrame()
            {
                if (Dec.GetFrameHeader(out JxlFrameHeader frameHeader) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                string frameName = string.Empty;

                if (frameHeader.NameLength > 0)
                {
                    byte[] nameBuf = new byte[frameHeader.NameLength + 1];

                    if (Dec.GetFrameName(nameBuf) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    frameName = Encoding.UTF8.GetString(nameBuf, 0, (int)frameHeader.NameLength);
                }

                uint xsize = frameHeader.LayerInfo.XSize > 0 ? frameHeader.LayerInfo.XSize : Ppf.Info.XSize;
                uint ysize = frameHeader.LayerInfo.YSize > 0 ? frameHeader.LayerInfo.YSize : Ppf.Info.YSize;

                StatusOr<PackedFrame> frameOr = PackedFrame.Create(xsize, ysize, in _format);

                if (!frameOr.IsOk)
                {
                    return false;
                }

                PackedFrame frame = frameOr.Value;
                frame.FrameInfo   = frameHeader;
                frame.Name        = frameName;
                Ppf.Frames.Add(frame);
                _numPasses        = 0;

                return true;
            }

            private bool HandleFrameProgression()
            {
                nuint downsampling    = Dec.GetIntendedDownsamplingRatio();
                bool maxPassesDefined = DParams.MaxPasses < uint.MaxValue;

                if ((maxPassesDefined && _numPasses >= DParams.MaxPasses) ||
                    (!maxPassesDefined && downsampling <= DParams.MaxDownsampling))
                {
                    if (Dec.FlushImage() != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    if (Ppf.Frames.Count > 0 && Ppf.Frames[Ppf.Frames.Count - 1].FrameInfo.IsLast)
                    {
                        Done = true;

                        return true;
                    }

                    if (Dec.SkipCurrentFrame() != JxlDecoderStatus.Success)
                    {
                        return false;
                    }
                }

                _numPasses++;

                return true;
            }

            private unsafe bool HandleNeedImageOutBuffer()
            {
                if (Ppf.Frames.Count == 0)
                {
                    return false;
                }

                PackedFrame frame = Ppf.Frames[Ppf.Frames.Count - 1];

                if (Dec.ImageOutBufferSize(_format, out nuint frameSize) != JxlDecoderStatus.Success ||
                    frameSize != frame.Color.PixelsSize)
                {
                    return false;
                }

                if (DParams.UseImageCallback)
                {
                    JxlDecoderStatus cbStatus = Dec.SetImageOutCallback(
                        _format,
                        static (opaque, x, y, numPixels, pixels) =>
                        {
                            if (opaque is PackedImage img)
                            {
                                Span<byte> dst = img.Pixels(y, x, 0);
                                new ReadOnlySpan<byte>(pixels, (int)(numPixels * img.PixelStride)).CopyTo(dst);
                            }
                        },
                        frame.Color);

                    if (cbStatus != JxlDecoderStatus.Success)
                    {
                        return false;
                    }
                }
                else if (Dec.SetImageOutBuffer(_format, frame.Color.PixelsBuffer) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                if (Dec.SetImageOutBitDepth(in DParams.OutputBitdepth) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                uint bps  = Ppf.Info.BitsPerSample;
                uint ebps = Ppf.Info.ExponentBitsPerSample;
                UpdateBitDepth(in DParams.OutputBitdepth, _format.DataType, ref bps, ref ebps);
                Ppf.Info.BitsPerSample         = bps;
                Ppf.Info.ExponentBitsPerSample = ebps;

                if (_format.NumChannels == 2 || _format.NumChannels == 4)
                {
                    Ppf.Info.AlphaBits         = Ppf.Info.BitsPerSample;
                    Ppf.Info.AlphaExponentBits = Ppf.Info.ExponentBitsPerSample;
                }

                return AllocateDecoderExtraChannels(frame);
            }

            private static void UpdateBitDepth(
                in JxlBitDepth bitDepth,
                JxlDataType dataType,
                ref uint bitsPerSample,
                ref uint exponentBitsPerSample)
            {
                if (bitDepth.Type == JxlBitDepthType.FromPixelFormat)
                {
                    switch (dataType)
                    {
                        case JxlDataType.UInt8:
                            bitsPerSample         = 8;
                            exponentBitsPerSample = 0;
                            break;

                        case JxlDataType.UInt16:
                            bitsPerSample         = 16;
                            exponentBitsPerSample = 0;
                            break;

                        case JxlDataType.Float16:
                            bitsPerSample         = 16;
                            exponentBitsPerSample = 5;
                            break;

                        case JxlDataType.Float:
                            bitsPerSample         = 32;
                            exponentBitsPerSample = 8;
                            break;
                    }
                }
                else if (bitDepth.Type == JxlBitDepthType.Custom)
                {
                    bitsPerSample         = bitDepth.BitsPerSample;
                    exponentBitsPerSample = bitDepth.ExponentBitsPerSample;
                }
            }

            private bool AllocateDecoderExtraChannels(PackedFrame frame)
            {
                JxlPixelFormat ecFormat = _format;
                ecFormat.NumChannels    = 1;

                for (int i = 0; i < Ppf.ExtraChannelsInfo.Count; i++)
                {
                    PackedExtraChannel pec      = Ppf.ExtraChannelsInfo[i];
                    StatusOr<PackedImage> ecOr  = PackedImage.Create(frame.Color.XSize, frame.Color.YSize, in ecFormat);

                    if (!ecOr.IsOk)
                    {
                        return false;
                    }

                    PackedImage ecImg = ecOr.Value;

                    if (Dec.ExtraChannelBufferSize(ecFormat, out nuint ecSize, (uint)pec.Index) != JxlDecoderStatus.Success ||
                        ecSize != ecImg.PixelsSize ||
                        Dec.SetExtraChannelBuffer(ecFormat, ecImg.PixelsBuffer, (uint)pec.Index) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }

                    uint ecBps  = pec.EcInfo.BitsPerSample;
                    uint ecEbps = pec.EcInfo.ExponentBitsPerSample;
                    UpdateBitDepth(in DParams.OutputBitdepth, ecFormat.DataType, ref ecBps, ref ecEbps);
                    pec.EcInfo.BitsPerSample         = ecBps;
                    pec.EcInfo.ExponentBitsPerSample = ecEbps;

                    frame.ExtraChannels.Add(ecImg);
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Pure C# PNG / APNG image decoder and encoder implementation using <see cref="ZLibStream"/>.
    /// Ported from <c>lib/extras/dec/apng.cc</c> and <c>lib/extras/enc/apng.cc</c>.
    /// </summary>
    public static class CodecApng
    {
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        };

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        /// <summary>
        /// Returns whether PNG / APNG decoding is supported (<c>true</c>).
        /// </summary>
        /// <returns><c>true</c>.</returns>
        public static bool CanDecodeAPNG() => true;

        /// <summary>
        /// Decodes a PNG or APNG image from <paramref name="bytes"/> into <paramref name="ppf"/>.
        /// Ported from <c>jxl::extras::DecodeImageAPNG</c> in <c>lib/extras/dec/apng.cc</c>.
        /// </summary>
        /// <param name="bytes">Input PNG/APNG bytes.</param>
        /// <param name="colorHints">Color space hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional dimension constraints.</param>
        /// <returns><see cref="Status.Ok"/> on success; otherwise failure.</returns>
        public static Status DecodeImageAPNG(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            if (bytes.Length < 8 || !bytes.Slice(0, 8).SequenceEqual(PngSignature))
            {
                return false;
            }

            PngDecodeState state = new PngDecodeState();
            Status chunkStatus   = ParsePngChunks(bytes.Slice(8), state, ppf, constraints);

            if (!chunkStatus)
            {
                return chunkStatus;
            }

            if (!state.SeenIhdr || state.IdatStream.Length == 0)
            {
                return Status.Failure("PNG: missing IHDR or IDAT");
            }

            Status colorStatus = FinalizePngColorAndHints(state, colorHints, ppf);

            if (!colorStatus)
            {
                return colorStatus;
            }

            return DecodePngPrimaryFrame(state, ppf);
        }

        /// <summary>
        /// Creates a PNG / APNG (<c>.png</c>) encoder.
        /// Ported from <c>jxl::extras::GetAPNGEncoder</c> in <c>lib/extras/enc/apng.cc</c>.
        /// </summary>
        /// <returns>New <see cref="Encoder"/> instance.</returns>
        public static Encoder? GetAPNGEncoder() => new ApngEncoder();

        private static Status ParsePngChunks(
            ReadOnlySpan<byte> data,
            PngDecodeState state,
            PackedPixelFile ppf,
            SizeConstraints? constraints)
        {
            int pos = 0;

            while (pos + 12 <= data.Length)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
                uint type   = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos + 4, 4));
                pos        += 8;

                if (length > (uint)(data.Length - pos - 4))
                {
                    return Status.Failure("PNG: truncated chunk");
                }

                ReadOnlySpan<byte> payload = data.Slice(pos, (int)length);
                pos                       += (int)length + 4;

                if (type == 0x49454E44u)
                {
                    state.SeenIend = true;
                    break;
                }

                Status st = ProcessSinglePngChunk(type, payload, state, ppf, constraints);

                if (!st)
                {
                    return st;
                }
            }

            return state.SeenIend ? Status.Ok : Status.Failure("PNG: missing IEND");
        }

        private static Status ProcessSinglePngChunk(
            uint type,
            ReadOnlySpan<byte> payload,
            PngDecodeState state,
            PackedPixelFile ppf,
            SizeConstraints? constraints)
        {
            switch (type)
            {
                case 0x49484452u: // IHDR
                    return ParseIhdrChunk(payload, state, constraints);

                case 0x504C5445u: // PLTE
                    return ParsePlteChunk(payload, state);

                case 0x74524E53u: // tRNS
                    state.Trns = payload.ToArray();

                    return Status.Ok;

                case 0x49444154u: // IDAT
                    state.IdatStream.Write(payload);

                    return Status.Ok;

                case 0x63494350u: // cICP
                    return ParseCicpChunk(payload, state, ppf);

                case 0x69434350u: // iCCP
                    return ParseIccpChunk(payload, state, ppf);

                case 0x73524742u: // sRGB
                    return ParseSrgbChunk(payload, state);

                case 0x67414D41u: // gAMA
                    return ParseGamaChunk(payload, state);

                case 0x6348524Du: // cHRM
                    return ParseChrmChunk(payload, state);

                case 0x65584966u: // eXIf
                    ppf.Metadata.Exif = payload.ToArray();

                    return Status.Ok;

                default:
                    return Status.Ok;
            }
        }

        private static Status ParseIhdrChunk(
            ReadOnlySpan<byte> payload,
            PngDecodeState state,
            SizeConstraints? constraints)
        {
            if (payload.Length != 13 || state.SeenIhdr)
            {
                return Status.Failure("PNG: invalid IHDR");
            }

            uint width  = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4));
            uint height = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));

            Status dimStatus = ExtrasCommon.VerifyDimensions(constraints, width, height);

            if (!dimStatus)
            {
                return dimStatus;
            }

            byte bitDepth   = payload[8];
            byte colorType  = payload[9];
            byte compMethod = payload[10];
            byte filtMethod = payload[11];
            byte interlace  = payload[12];

            if (compMethod != 0 || filtMethod != 0 || interlace > 1 || !IsValidBitDepthAndColorType(bitDepth, colorType))
            {
                return Status.Failure("PNG: unsupported IHDR parameters");
            }

            state.SeenIhdr  = true;
            state.Width     = width;
            state.Height    = height;
            state.BitDepth  = bitDepth;
            state.ColorType = colorType;
            state.Interlace = interlace;

            return Status.Ok;
        }

        private static bool IsValidBitDepthAndColorType(byte bitDepth, byte colorType)
        {
            return colorType switch
            {
                0 => bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16,
                2 => bitDepth == 8 || bitDepth == 16,
                3 => bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8,
                4 => bitDepth == 8 || bitDepth == 16,
                6 => bitDepth == 8 || bitDepth == 16,
                _ => false,
            };
        }

        private static Status ParsePlteChunk(ReadOnlySpan<byte> payload, PngDecodeState state)
        {
            if (payload.Length == 0 || payload.Length % 3 != 0 || payload.Length > 768)
            {
                return Status.Failure("PNG: invalid PLTE");
            }

            state.Palette = payload.ToArray();

            return Status.Ok;
        }

        private static Status ParseCicpChunk(
            ReadOnlySpan<byte> payload,
            PngDecodeState state,
            PackedPixelFile ppf)
        {
            if (payload.Length < 4)
            {
                return Status.Ok;
            }

            JxlColorEncoding cEnc = default;
            cEnc.ColorSpace       = JxlColorSpace.Rgb;
            cEnc.RenderingIntent  = JxlRenderingIntent.Relative;

            if (!MapCicpPrimaries(payload[0], ref cEnc) || !MapCicpTransfer(payload[1], ref cEnc))
            {
                return Status.Ok;
            }

            if (payload[2] != 0 || payload[3] != 1)
            {
                return Status.Ok;
            }

            ppf.ColorEncoding              = cEnc;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.ColorEncodingIsPrimary;
            state.HasCicp                  = true;
            state.ColorAlreadySet          = true;

            return Status.Ok;
        }

        private static bool MapCicpPrimaries(byte primariesCode, ref JxlColorEncoding cEnc)
        {
            if (primariesCode == 1)
            {
                cEnc.Primaries  = JxlPrimaries.Srgb;
                cEnc.WhitePoint = JxlWhitePoint.D65;

                return true;
            }

            if (primariesCode == 9)
            {
                cEnc.Primaries  = JxlPrimaries.P2100;
                cEnc.WhitePoint = JxlWhitePoint.D65;

                return true;
            }

            if (primariesCode == 11)
            {
                cEnc.Primaries  = JxlPrimaries.P3;
                cEnc.WhitePoint = JxlWhitePoint.Dci;

                return true;
            }

            if (primariesCode == 12)
            {
                cEnc.Primaries  = JxlPrimaries.P3;
                cEnc.WhitePoint = JxlWhitePoint.D65;

                return true;
            }

            return false;
        }

        private static bool MapCicpTransfer(byte transferCode, ref JxlColorEncoding cEnc)
        {
            switch (transferCode)
            {
                case 1:
                case 6:
                case 14:
                case 15:
                    cEnc.TransferFunction = JxlTransferFunction.Bt709;
                    return true;

                case 8:
                    cEnc.TransferFunction = JxlTransferFunction.Linear;
                    return true;

                case 13:
                    cEnc.TransferFunction = JxlTransferFunction.Srgb;
                    return true;

                case 16:
                    cEnc.TransferFunction = JxlTransferFunction.Pq;
                    return true;

                case 17:
                    cEnc.TransferFunction = JxlTransferFunction.Dci;
                    return true;

                case 18:
                    cEnc.TransferFunction = JxlTransferFunction.Hlg;
                    return true;

                default:
                    return false;
            }
        }

        private static Status ParseIccpChunk(
            ReadOnlySpan<byte> payload,
            PngDecodeState state,
            PackedPixelFile ppf)
        {
            if (state.HasCicp)
            {
                return Status.Ok;
            }

            int nullIdx = payload.IndexOf((byte)0);

            if (nullIdx < 0 || nullIdx + 2 >= payload.Length || payload[nullIdx + 1] != 0)
            {
                return Status.Failure("PNG: invalid iCCP chunk");
            }

            ReadOnlySpan<byte> compressedIcc = payload.Slice(nullIdx + 2);

            if (!TryDecompressZlib(compressedIcc, out byte[] iccBytes) || iccBytes.Length == 0)
            {
                return Status.Failure("PNG: corrupt iCCP zlib stream");
            }

            ppf.Icc                        = iccBytes;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.IccIsPrimary;
            state.HasIccp                  = true;
            state.ColorAlreadySet          = true;

            return Status.Ok;
        }

        private static Status ParseSrgbChunk(ReadOnlySpan<byte> payload, PngDecodeState state)
        {
            if (payload.Length >= 1 && payload[0] <= 3)
            {
                state.HasSrgb    = true;
                state.SrgbIntent = (JxlRenderingIntent)payload[0];
            }

            return Status.Ok;
        }

        private static Status ParseGamaChunk(ReadOnlySpan<byte> payload, PngDecodeState state)
        {
            if (payload.Length >= 4)
            {
                uint gammaFixed = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4));

                if (gammaFixed > 0)
                {
                    state.HasGama = true;
                    state.Gamma   = gammaFixed * 1e-5;
                }
            }

            return Status.Ok;
        }

        private static Status ParseChrmChunk(ReadOnlySpan<byte> payload, PngDecodeState state)
        {
            if (payload.Length >= 32)
            {
                state.HasChrm = true;

                for (int i = 0; i < 8; i++)
                {
                    state.Chrm[i] = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(i * 4, 4)) * 1e-5;
                }
            }

            return Status.Ok;
        }

        private static Status FinalizePngColorAndHints(
            PngDecodeState state,
            ColorHints colorHints,
            PackedPixelFile ppf)
        {
            bool isGray = (state.ColorType == 0 || state.ColorType == 4);

            if (!state.HasCicp && !state.HasIccp)
            {
                ApplyPngSrgbOrChrmGama(state, isGray, ppf);
            }

            return ColorHintsHelper.ApplyColorHints(colorHints, state.ColorAlreadySet, isGray, ppf);
        }

        private static void ApplyPngSrgbOrChrmGama(
            PngDecodeState state,
            bool isGray,
            PackedPixelFile ppf)
        {
            if (state.HasSrgb)
            {
                JxlColorEncoding cEnc          = ColorEncoding.SRGB(isGray).ToExternal();
                cEnc.RenderingIntent           = state.SrgbIntent;
                ppf.ColorEncoding              = cEnc;
                ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.ColorEncodingIsPrimary;
                state.ColorAlreadySet          = true;

                return;
            }

            if (!state.HasGama && !state.HasChrm)
            {
                return;
            }

            JxlColorEncoding custom = ColorEncoding.SRGB(isGray).ToExternal();

            if (state.HasGama)
            {
                if (Math.Abs(state.Gamma - 1.0) < 1e-4)
                {
                    custom.TransferFunction = JxlTransferFunction.Linear;
                }
                else if (Math.Abs(state.Gamma - (1.0 / 2.2)) > 2e-3)
                {
                    custom.TransferFunction = JxlTransferFunction.Gamma;
                    custom.Gamma            = state.Gamma;
                }
            }

            if (state.HasChrm && !isGray)
            {
                PopulateCustomChromaticities(state.Chrm, ref custom);
            }

            ppf.ColorEncoding              = custom;
            ppf.PrimaryColorRepresentation = PrimaryColorRepresentation.ColorEncodingIsPrimary;
            state.ColorAlreadySet          = true;
        }

        private static void PopulateCustomChromaticities(double[] chrm, ref JxlColorEncoding custom)
        {
            custom.WhitePoint      = JxlWhitePoint.Custom;
            custom.WhitePointX     = chrm[0];
            custom.WhitePointY     = chrm[1];
            custom.Primaries       = JxlPrimaries.Custom;
            custom.PrimariesRedX   = chrm[2];
            custom.PrimariesRedY   = chrm[3];
            custom.PrimariesGreenX = chrm[4];
            custom.PrimariesGreenY = chrm[5];
            custom.PrimariesBlueX  = chrm[6];
            custom.PrimariesBlueY  = chrm[7];

            ColorEncoding internalCe = new ColorEncoding();

            if (internalCe.FromExternal(custom))
            {
                custom = internalCe.ToExternal();
            }
        }

        private static Status DecodePngPrimaryFrame(PngDecodeState state, PackedPixelFile ppf)
        {
            byte[] compressedIdat = state.IdatStream.ToArray();

            if (!TryDecompressZlib(compressedIdat, out byte[] rawFiltered))
            {
                return Status.Failure("PNG: failed to decompress IDAT");
            }

            DeterminePngOutputLayout(state, out uint numColorChannels, out bool hasAlpha, out uint outBitsPerSample);

            JxlBasicInfo info          = ppf.Info;
            info.XSize                 = state.Width;
            info.YSize                 = state.Height;
            info.BitsPerSample         = outBitsPerSample;
            info.ExponentBitsPerSample = 0;
            info.Orientation           = JxlOrientation.Identity;
            info.NumColorChannels      = numColorChannels;
            info.AlphaBits             = hasAlpha ? outBitsPerSample : 0u;
            info.AlphaExponentBits     = 0;
            info.NumExtraChannels      = hasAlpha ? 1u : 0u;
            ppf.Info                   = info;

            JxlDataType dataType  = outBitsPerSample > 8 ? JxlDataType.UInt16 : JxlDataType.UInt8;
            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = numColorChannels + (hasAlpha ? 1u : 0u),
                DataType    = dataType,
                Endianness  = JxlEndianness.BigEndian,
                Align       = 0,
            };

            StatusOr<PackedFrame> frameOr = PackedFrame.Create(state.Width, state.Height, in format);

            if (!frameOr.IsOk)
            {
                return frameOr.Status;
            }

            PackedFrame frame   = frameOr.Value;
            Status unpackStatus = UnfilterAndUnpackPng(rawFiltered, state, frame.Color, hasAlpha);

            if (!unpackStatus)
            {
                return unpackStatus;
            }

            ppf.Frames.Clear();
            ppf.Frames.Add(frame);

            return Status.Ok;
        }

        private static void DeterminePngOutputLayout(
            PngDecodeState state,
            out uint numColorChannels,
            out bool hasAlpha,
            out uint outBitsPerSample)
        {
            bool hasTrns = state.Trns is not null && state.Trns.Length > 0;

            numColorChannels = (state.ColorType == 0 || state.ColorType == 4) ? 1u : 3u;
            hasAlpha         = state.ColorType == 4 || state.ColorType == 6 || hasTrns;
            outBitsPerSample = state.BitDepth == 16 ? 16u : 8u;
        }

        private static Status UnfilterAndUnpackPng(
            ReadOnlySpan<byte> rawFiltered,
            PngDecodeState state,
            PackedImage destImage,
            bool hasAlpha)
        {
            if (state.Interlace == 1)
            {
                return UnfilterAndUnpackAdam7(rawFiltered, state, destImage, hasAlpha);
            }

            int rawChannels   = GetRawChannelCount(state.ColorType);
            int bitsPerPixel  = rawChannels * state.BitDepth;
            int bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
            int rowBytes      = (int)(((long)state.Width * bitsPerPixel + 7) / 8);
            int expectedSize  = (int)state.Height * (1 + rowBytes);

            if (rawFiltered.Length < expectedSize)
            {
                return Status.Failure("PNG: decompressed IDAT too small");
            }

            byte[] prevRow = new byte[rowBytes];
            byte[] currRow = new byte[rowBytes];

            for (uint y = 0; y < state.Height; y++)
            {
                int rowStart    = (int)y * (1 + rowBytes);
                byte filterType = rawFiltered[rowStart];
                ReadOnlySpan<byte> srcRow = rawFiltered.Slice(rowStart + 1, rowBytes);

                if (!UnfilterScanline(filterType, srcRow, prevRow, currRow, bytesPerPixel))
                {
                    return Status.Failure("PNG: invalid scanline filter");
                }

                Status rowStatus = UnpackScanlinePixels(currRow, state, destImage, y, 0, 1, (int)state.Width, hasAlpha);

                if (!rowStatus)
                {
                    return rowStatus;
                }

                Buffer.BlockCopy(currRow, 0, prevRow, 0, rowBytes);
            }

            return Status.Ok;
        }

        private static Status UnfilterAndUnpackAdam7(
            ReadOnlySpan<byte> rawFiltered,
            PngDecodeState state,
            PackedImage destImage,
            bool hasAlpha)
        {
            ReadOnlySpan<int> xOrig = stackalloc int[7] { 0, 4, 0, 2, 0, 1, 0 };
            ReadOnlySpan<int> yOrig = stackalloc int[7] { 0, 0, 4, 0, 2, 0, 1 };
            ReadOnlySpan<int> xStep = stackalloc int[7] { 8, 8, 4, 4, 2, 2, 1 };
            ReadOnlySpan<int> yStep = stackalloc int[7] { 8, 8, 8, 4, 4, 2, 2 };

            int rawChannels   = GetRawChannelCount(state.ColorType);
            int bitsPerPixel  = rawChannels * state.BitDepth;
            int bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
            int offset        = 0;

            for (int pass = 0; pass < 7; pass++)
            {
                int passW = ((int)state.Width - xOrig[pass] + xStep[pass] - 1) / xStep[pass];
                int passH = ((int)state.Height - yOrig[pass] + yStep[pass] - 1) / yStep[pass];

                if (passW <= 0 || passH <= 0)
                {
                    continue;
                }

                Status passStatus = ProcessAdam7Pass(
                    rawFiltered,
                    ref offset,
                    state,
                    destImage,
                    hasAlpha,
                    new Adam7PassSpec(passW, passH, xOrig[pass], yOrig[pass], xStep[pass], yStep[pass], bitsPerPixel, bytesPerPixel));

                if (!passStatus)
                {
                    return passStatus;
                }
            }

            return Status.Ok;
        }

        private static Status ProcessAdam7Pass(
            ReadOnlySpan<byte> rawFiltered,
            ref int offset,
            PngDecodeState state,
            PackedImage destImage,
            bool hasAlpha,
            Adam7PassSpec spec)
        {
            int rowBytes   = (spec.PassW * spec.BitsPerPixel + 7) / 8;
            byte[] prevRow = new byte[rowBytes];
            byte[] currRow = new byte[rowBytes];

            for (int py = 0; py < spec.PassH; py++)
            {
                if (offset + 1 + rowBytes > rawFiltered.Length)
                {
                    return Status.Failure("PNG: truncated Adam7 pass");
                }

                byte filterType           = rawFiltered[offset];
                ReadOnlySpan<byte> srcRow = rawFiltered.Slice(offset + 1, rowBytes);
                offset                   += 1 + rowBytes;

                if (!UnfilterScanline(filterType, srcRow, prevRow, currRow, spec.BytesPerPixel))
                {
                    return Status.Failure("PNG: invalid Adam7 scanline filter");
                }

                uint destY       = (uint)(spec.YOrig + (py * spec.YStep));
                Status rowStatus = UnpackScanlinePixels(currRow, state, destImage, destY, spec.XOrig, spec.XStep, spec.PassW, hasAlpha);

                if (!rowStatus)
                {
                    return rowStatus;
                }

                Buffer.BlockCopy(currRow, 0, prevRow, 0, rowBytes);
            }

            return Status.Ok;
        }

        private static int GetRawChannelCount(byte colorType)
        {
            return colorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                6 => 4,
                _ => 1,
            };
        }

        private static bool UnfilterScanline(
            byte filterType,
            ReadOnlySpan<byte> src,
            ReadOnlySpan<byte> prev,
            Span<byte> dst,
            int bpp)
        {
            switch (filterType)
            {
                case 0:
                    src.CopyTo(dst);
                    return true;

                case 1:
                    for (int i = 0; i < src.Length; i++)
                    {
                        byte left = i >= bpp ? dst[i - bpp] : (byte)0;
                        dst[i]    = unchecked((byte)(src[i] + left));
                    }

                    return true;

                case 2:
                    for (int i = 0; i < src.Length; i++)
                    {
                        dst[i] = unchecked((byte)(src[i] + prev[i]));
                    }

                    return true;

                case 3:
                    for (int i = 0; i < src.Length; i++)
                    {
                        int left = i >= bpp ? dst[i - bpp] : 0;
                        int up   = prev[i];
                        dst[i]   = unchecked((byte)(src[i] + ((left + up) >> 1)));
                    }

                    return true;

                case 4:
                    for (int i = 0; i < src.Length; i++)
                    {
                        int a  = i >= bpp ? dst[i - bpp] : 0;
                        int b  = prev[i];
                        int c  = i >= bpp ? prev[i - bpp] : 0;
                        dst[i] = unchecked((byte)(src[i] + PaethPredictor(a, b, c)));
                    }

                    return true;

                default:
                    return false;
            }
        }

        private static int PaethPredictor(int a, int b, int c)
        {
            int p  = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc)
            {
                return a;
            }

            return pb <= pc ? b : c;
        }

        private static Status UnpackScanlinePixels(
            ReadOnlySpan<byte> rowData,
            PngDecodeState state,
            PackedImage destImage,
            uint y,
            int xStart,
            int xStep,
            int count,
            bool hasAlpha)
        {
            if (state.ColorType == 3)
            {
                return UnpackPaletteScanline(rowData, state, destImage, y, xStart, xStep, count, hasAlpha);
            }

            if (state.BitDepth < 8)
            {
                UnpackSubByteGrayScanline(rowData, state, destImage, y, xStart, xStep, count, hasAlpha);

                return Status.Ok;
            }

            if (xStart == 0 && xStep == 1 && (state.Trns is null || state.Trns.Length == 0))
            {
                int rowByteLen = (int)destImage.Stride;
                rowData.Slice(0, rowByteLen).CopyTo(destImage.Pixels(y, 0, 0));

                return Status.Ok;
            }

            UnpackByteOrWordScanline(rowData, state, destImage, y, xStart, xStep, count, hasAlpha);

            return Status.Ok;
        }

        private static Status UnpackPaletteScanline(
            ReadOnlySpan<byte> rowData,
            PngDecodeState state,
            PackedImage destImage,
            uint y,
            int xStart,
            int xStep,
            int count,
            bool hasAlpha)
        {
            if (state.Palette is null)
            {
                return Status.Failure("PNG: missing PLTE chunk for indexed image");
            }

            int paletteEntries = state.Palette.Length / 3;
            int outChannels    = hasAlpha ? 4 : 3;

            for (int i = 0; i < count; i++)
            {
                int index = ExtractSubByteSample(rowData, i, state.BitDepth);

                if (index >= paletteEntries)
                {
                    return Status.Failure("PNG: palette index out of range");
                }

                uint destX     = (uint)(xStart + (i * xStep));
                Span<byte> dst = destImage.Pixels(y, destX, 0);
                dst[0]         = state.Palette[(index * 3) + 0];
                dst[1]         = state.Palette[(index * 3) + 1];
                dst[2]         = state.Palette[(index * 3) + 2];

                if (outChannels == 4)
                {
                    dst[3] = (state.Trns is not null && index < state.Trns.Length)
                        ? state.Trns[index]
                        : (byte)255;
                }
            }

            return Status.Ok;
        }

        private static void UnpackSubByteGrayScanline(
            ReadOnlySpan<byte> rowData,
            PngDecodeState state,
            PackedImage destImage,
            uint y,
            int xStart,
            int xStep,
            int count,
            bool hasAlpha)
        {
            int maxSample = (1 << state.BitDepth) - 1;
            int scale     = 255 / maxSample;
            int trnsKey   = (state.Trns is not null && state.Trns.Length >= 2)
                ? BinaryPrimitives.ReadUInt16BigEndian(state.Trns.AsSpan(0, 2))
                : -1;

            for (int i = 0; i < count; i++)
            {
                int raw        = ExtractSubByteSample(rowData, i, state.BitDepth);
                uint destX     = (uint)(xStart + (i * xStep));
                Span<byte> dst = destImage.Pixels(y, destX, 0);
                dst[0]         = (byte)(raw * scale);

                if (hasAlpha)
                {
                    dst[1] = raw == trnsKey ? (byte)0 : (byte)255;
                }
            }
        }

        private static void UnpackByteOrWordScanline(
            ReadOnlySpan<byte> rowData,
            PngDecodeState state,
            PackedImage destImage,
            uint y,
            int xStart,
            int xStep,
            int count,
            bool hasAlpha)
        {
            int rawChannels   = GetRawChannelCount(state.ColorType);
            int bytesPerChan  = state.BitDepth == 16 ? 2 : 1;
            int rawPixStride  = rawChannels * bytesPerChan;
            int dstPixStride  = (int)destImage.PixelStride;
            bool addTrnsAlpha = hasAlpha && state.ColorType != 4 && state.ColorType != 6;

            for (int i = 0; i < count; i++)
            {
                uint destX                = (uint)(xStart + (i * xStep));
                ReadOnlySpan<byte> srcPix = rowData.Slice(i * rawPixStride, rawPixStride);
                Span<byte> dstPix         = destImage.Pixels(y, destX, 0);
                srcPix.CopyTo(dstPix);

                if (addTrnsAlpha)
                {
                    WriteTrnsAlpha(srcPix, state.Trns, dstPix.Slice(rawPixStride), bytesPerChan);
                }
            }
        }

        private static void WriteTrnsAlpha(
            ReadOnlySpan<byte> srcPix,
            byte[]? trns,
            Span<byte> alphaDst,
            int bytesPerChan)
        {
            bool transparent = IsTrnsMatch(srcPix, trns, bytesPerChan);

            if (bytesPerChan == 1)
            {
                alphaDst[0] = transparent ? (byte)0 : (byte)255;
            }
            else
            {
                alphaDst[0] = transparent ? (byte)0 : (byte)255;
                alphaDst[1] = transparent ? (byte)0 : (byte)255;
            }
        }

        private static bool IsTrnsMatch(ReadOnlySpan<byte> srcPix, byte[]? trns, int bytesPerChan)
        {
            if (trns is null)
            {
                return false;
            }

            if (bytesPerChan == 2)
            {
                return trns.Length >= srcPix.Length && srcPix.SequenceEqual(trns.AsSpan(0, srcPix.Length));
            }

            int channels = srcPix.Length;

            if (trns.Length < channels * 2)
            {
                return false;
            }

            for (int c = 0; c < channels; c++)
            {
                if (srcPix[c] != trns[(c * 2) + 1])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ExtractSubByteSample(ReadOnlySpan<byte> rowData, int pixelIndex, int bitDepth)
        {
            int bitOffset = pixelIndex * bitDepth;
            int byteIndex = bitOffset >> 3;
            int shift     = 8 - bitDepth - (bitOffset & 7);
            int mask      = (1 << bitDepth) - 1;

            return (rowData[byteIndex] >> shift) & mask;
        }

        private static bool TryDecompressZlib(ReadOnlySpan<byte> compressed, out byte[] decompressed)
        {
            try
            {
                using MemoryStream input  = new MemoryStream(compressed.ToArray());
                using ZLibStream zlib     = new ZLibStream(input, CompressionMode.Decompress);
                using MemoryStream output = new MemoryStream();
                zlib.CopyTo(output);
                decompressed = output.ToArray();

                return true;
            }
            catch
            {
                decompressed = Array.Empty<byte>();

                return false;
            }
        }

        private static byte[] CompressZlib(ReadOnlySpan<byte> raw)
        {
            using MemoryStream output = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            return output.ToArray();
        }

        private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header.Slice(0, 4), (uint)data.Length);
            chunkType.Slice(0, 4).CopyTo(header.Slice(4, 4));
            stream.Write(header);

            if (data.Length > 0)
            {
                stream.Write(data);
            }

            uint crc = ComputeCrc32(chunkType.Slice(0, 4), data);
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            stream.Write(crcBytes);
        }

        private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;

            for (int i = 0; i < type.Length; i++)
            {
                crc = Crc32Table[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
            }

            for (int i = 0; i < data.Length; i++)
            {
                crc = Crc32Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }

                table[n] = c;
            }

            return table;
        }

        private readonly struct Adam7PassSpec
        {
            public readonly int PassW;
            public readonly int PassH;
            public readonly int XOrig;
            public readonly int YOrig;
            public readonly int XStep;
            public readonly int YStep;
            public readonly int BitsPerPixel;
            public readonly int BytesPerPixel;

            public Adam7PassSpec(int passW, int passH, int xOrig, int yOrig, int xStep, int yStep, int bitsPerPixel, int bytesPerPixel)
            {
                PassW         = passW;
                PassH         = passH;
                XOrig         = xOrig;
                YOrig         = yOrig;
                XStep         = xStep;
                YStep         = yStep;
                BitsPerPixel  = bitsPerPixel;
                BytesPerPixel = bytesPerPixel;
            }
        }

        private sealed class PngDecodeState
        {
            public bool SeenIhdr;
            public bool SeenIend;
            public uint Width;
            public uint Height;
            public byte BitDepth;
            public byte ColorType;
            public byte Interlace;
            public byte[]? Palette;
            public byte[]? Trns;
            public bool HasCicp;
            public bool HasIccp;
            public bool HasSrgb;
            public JxlRenderingIntent SrgbIntent;
            public bool HasGama;
            public double Gamma;
            public bool HasChrm;
            public readonly double[] Chrm = new double[8];
            public bool ColorAlreadySet;
            public readonly MemoryStream IdatStream = new MemoryStream();
        }

        private sealed class ApngEncoder : Encoder
        {
            private static readonly JxlPixelFormat[] Formats = BuildAcceptedFormats();

            public override IReadOnlyList<JxlPixelFormat> AcceptedFormats() => Formats;

            public override Status Encode(PackedPixelFile ppf, EncodedImage encodedImage, ThreadPool? pool = null)
            {
                Status infoStatus = VerifyBasicInfo(ppf.Info);

                if (!infoStatus)
                {
                    return infoStatus;
                }

                encodedImage.Icc = Array.Empty<byte>();
                encodedImage.Bitstreams.Clear();
                encodedImage.ExtraChannelBitstreams.Clear();

                for (int i = 0; i < ppf.Frames.Count; i++)
                {
                    Status st = EncodeSinglePngFrame(ppf, ppf.Frames[i].Color, ppf.Info.BitsPerSample, isExtraChannel: false, out byte[] pngBytes);

                    if (!st)
                    {
                        return st;
                    }

                    encodedImage.Bitstreams.Add(pngBytes);
                }

                for (int ecIdx = 0; ecIdx < ppf.ExtraChannelsInfo.Count; ecIdx++)
                {
                    List<byte[]> ecStreams = new List<byte[]>();
                    uint ecBits            = ppf.ExtraChannelsInfo[ecIdx].EcInfo.BitsPerSample;

                    for (int fIdx = 0; fIdx < ppf.Frames.Count; fIdx++)
                    {
                        PackedImage ecImg = ppf.Frames[fIdx].ExtraChannels[ecIdx];
                        Status ecSt       = EncodeSinglePngFrame(ppf, ecImg, ecBits, isExtraChannel: true, out byte[] ecBytes);

                        if (!ecSt)
                        {
                            return ecSt;
                        }

                        ecStreams.Add(ecBytes);
                    }

                    encodedImage.ExtraChannelBitstreams.Add(ecStreams);
                }

                return Status.Ok;
            }

            private Status EncodeSinglePngFrame(
                PackedPixelFile ppf,
                PackedImage image,
                uint bitsPerSample,
                bool isExtraChannel,
                out byte[] pngBytes)
            {
                pngBytes = Array.Empty<byte>();

                Status validStatus = isExtraChannel
                    ? VerifyFormat(image.Format)
                    : VerifyPackedImage(image, in ppf.Info);

                if (!validStatus)
                {
                    return validStatus;
                }

                uint numChannels    = image.Format.NumChannels;
                bool isGray         = numChannels == 1 || numChannels == 2;
                bool hasAlpha       = numChannels == 2 || numChannels == 4;
                byte colorType      = (byte)((isGray ? 0 : 2) | (hasAlpha ? 4 : 0));
                int outBytesPerChan = image.Format.DataType == JxlDataType.UInt8 ? 1 : 2;
                byte pngBitDepth    = (byte)(outBytesPerChan * 8);

                byte[] convertedPixels = ConvertPixelsToPngBigEndian(image, bitsPerSample, outBytesPerChan);
                byte[] filteredStream  = BuildFilteredScanlines(convertedPixels, (int)image.XSize, (int)image.YSize, (int)numChannels * outBytesPerChan);
                byte[] compressedIdat  = CompressZlib(filteredStream);

                using MemoryStream ms = new MemoryStream();
                ms.Write(PngSignature);
                WriteIhdr(ms, (uint)image.XSize, (uint)image.YSize, pngBitDepth, colorType);

                if (!isExtraChannel)
                {
                    WriteColorChunks(ms, ppf);
                }

                WritePngChunk(ms, "IDAT"u8, compressedIdat);
                WritePngChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
                pngBytes = ms.ToArray();

                return Status.Ok;
            }

            private static void WriteIhdr(Stream stream, uint width, uint height, byte bitDepth, byte colorType)
            {
                Span<byte> ihdr = stackalloc byte[13];
                BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(0, 4), width);
                BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), height);
                ihdr[8]  = bitDepth;
                ihdr[9]  = colorType;
                ihdr[10] = 0;
                ihdr[11] = 0;
                ihdr[12] = 0;
                WritePngChunk(stream, "IHDR"u8, ihdr);
            }

            private static void WriteColorChunks(Stream stream, PackedPixelFile ppf)
            {
                JxlColorEncoding cEnc = ppf.ColorEncoding;

                if (ppf.PrimaryColorRepresentation != PrimaryColorRepresentation.IccIsPrimary &&
                    IsSrgbColorEncoding(in cEnc))
                {
                    Span<byte> srgb = stackalloc byte[1] { (byte)cEnc.RenderingIntent };
                    WritePngChunk(stream, "sRGB"u8, srgb);

                    return;
                }

                if (ppf.Icc.Length > 0)
                {
                    byte[] zIcc      = CompressZlib(ppf.Icc);
                    byte[] iccpChunk = new byte[3 + zIcc.Length];
                    iccpChunk[0]     = (byte)'1';
                    iccpChunk[1]     = 0;
                    iccpChunk[2]     = 0;
                    Buffer.BlockCopy(zIcc, 0, iccpChunk, 3, zIcc.Length);
                    WritePngChunk(stream, "iCCP"u8, iccpChunk);
                }
            }

            private static bool IsSrgbColorEncoding(in JxlColorEncoding cEnc)
            {
                if (cEnc.TransferFunction != JxlTransferFunction.Srgb)
                {
                    return false;
                }

                return cEnc.ColorSpace == JxlColorSpace.Gray ||
                       (cEnc.ColorSpace == JxlColorSpace.Rgb &&
                        cEnc.Primaries == JxlPrimaries.Srgb &&
                        cEnc.WhitePoint == JxlWhitePoint.D65);
            }

            private static byte[] ConvertPixelsToPngBigEndian(
                PackedImage image,
                uint bitsPerSample,
                int outBytesPerChan)
            {
                int numSamples          = (int)(image.XSize * image.YSize * image.Format.NumChannels);
                ReadOnlySpan<byte> src  = image.Pixels();
                byte[] dst              = new byte[numSamples * outBytesPerChan];

                if (image.Format.DataType == JxlDataType.UInt8)
                {
                    ConvertUInt8Samples(src, dst, numSamples, bitsPerSample);
                }
                else if (image.Format.DataType == JxlDataType.UInt16)
                {
                    ConvertUInt16Samples(src, dst, numSamples, bitsPerSample, image.Format.Endianness);
                }
                else
                {
                    ConvertFloatSamples(src, dst, numSamples, image.Format.Endianness);
                }

                return dst;
            }

            private static void ConvertUInt8Samples(
                ReadOnlySpan<byte> src,
                 byte[] dst,
                int numSamples,
                uint bitsPerSample)
            {
                if (bitsPerSample < 8 && bitsPerSample > 0)
                {
                    float mul = 255.0f / ((1u << (int)bitsPerSample) - 1u);

                    for (int i = 0; i < numSamples; i++)
                    {
                        dst[i] = (byte)MathF.Round(src[i] * mul);
                    }
                }
                else
                {
                    src.Slice(0, numSamples).CopyTo(dst);
                }
            }

            private static void ConvertUInt16Samples(
                ReadOnlySpan<byte> src,
                byte[] dst,
                int numSamples,
                uint bitsPerSample,
                JxlEndianness endianness)
            {
                bool isBig = endianness == JxlEndianness.BigEndian ||
                             (endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);

                if (bitsPerSample == 16 && isBig)
                {
                    src.Slice(0, numSamples * 2).CopyTo(dst);

                    return;
                }

                float mul = (bitsPerSample > 0 && bitsPerSample < 16)
                    ? 65535.0f / ((1u << (int)bitsPerSample) - 1u)
                    : 1.0f;

                for (int i = 0; i < numSamples; i++)
                {
                    ushort val = isBig
                        ? BinaryPrimitives.ReadUInt16BigEndian(src.Slice(i * 2, 2))
                        : BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i * 2, 2));
                    ushort scaled = (ushort)Math.Clamp((int)MathF.Round(val * mul), 0, 65535);
                    BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(i * 2, 2), scaled);
                }
            }

            private static void ConvertFloatSamples(
                ReadOnlySpan<byte> src,
                byte[] dst,
                int numSamples,
                JxlEndianness endianness)
            {
                bool isBig = endianness == JxlEndianness.BigEndian ||
                             (endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);

                for (int i = 0; i < numSamples; i++)
                {
                    float val = isBig
                        ? BinaryPrimitives.ReadSingleBigEndian(src.Slice(i * 4, 4))
                        : BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i * 4, 4));
                    float clamped = Common.Clamp1(val, 0.0f, 1.0f);
                    ushort u16    = (ushort)MathF.Round(clamped * 65535.0f);
                    BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(i * 2, 2), u16);
                }
            }

            private static byte[] BuildFilteredScanlines(byte[] pixels, int width, int height, int bytesPerPixel)
            {
                int rowBytes    = width * bytesPerPixel;
                byte[] filtered = new byte[height * (1 + rowBytes)];

                for (int y = 0; y < height; y++)
                {
                    int srcOffset        = y * rowBytes;
                    int dstOffset        = y * (1 + rowBytes);
                    filtered[dstOffset]  = 1; // Sub filter

                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte left               = i >= bytesPerPixel ? pixels[srcOffset + i - bytesPerPixel] : (byte)0;
                        filtered[dstOffset + 1 + i] = unchecked((byte)(pixels[srcOffset + i] - left));
                    }
                }

                return filtered;
            }

            private static JxlPixelFormat[] BuildAcceptedFormats()
            {
                List<JxlPixelFormat> list = new List<JxlPixelFormat>();
                uint[] channels           = { 1u, 2u, 3u, 4u };
                JxlDataType[] types       = { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };
                JxlEndianness[] endians   = { JxlEndianness.BigEndian, JxlEndianness.LittleEndian };

                foreach (uint c in channels)
                {
                    foreach (JxlDataType dt in types)
                    {
                        foreach (JxlEndianness end in endians)
                        {
                            list.Add(new JxlPixelFormat { NumChannels = c, DataType = dt, Endianness = end, Align = 0 });
                        }
                    }
                }

                return list.ToArray();
            }
        }
    }

    /// <summary>
    /// Stubbed external JPEG codec (returns unsupported when libjpeg is not linked).
    /// Ported from <c>lib/extras/dec/jpg.cc</c> and <c>lib/extras/enc/jpg.cc</c>.
    /// </summary>
    public static class CodecJpg
    {
        /// <summary>Returns whether external JPEG decoding is supported (<c>false</c>).</summary>
        /// <returns><c>false</c>.</returns>
        public static bool CanDecodeJPG() => false;

        /// <summary>
        /// Returns failure indicating external JPEG decoding is not enabled.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        /// <param name="colorHints">Color hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <returns>Failure <see cref="Status"/>.</returns>
        public static Status DecodeImageJPG(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            return Status.Failure("JPEG decoding is not supported");
        }

        /// <summary>Returns <c>null</c> as external JPEG encoding is not enabled.</summary>
        /// <returns><c>null</c>.</returns>
        public static Encoder? GetJPEGEncoder() => null;
    }

    /// <summary>
    /// Stubbed external GIF codec (returns unsupported when giflib is not linked).
    /// Ported from <c>lib/extras/dec/gif.cc</c>.
    /// </summary>
    public static class CodecGif
    {
        /// <summary>Returns whether external GIF decoding is supported (<c>false</c>).</summary>
        /// <returns><c>false</c>.</returns>
        public static bool CanDecodeGIF() => false;

        /// <summary>
        /// Returns failure indicating external GIF decoding is not enabled.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        /// <param name="colorHints">Color hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <returns>Failure <see cref="Status"/>.</returns>
        public static Status DecodeImageGIF(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            return Status.Failure("GIF decoding is not supported");
        }
    }

    /// <summary>
    /// Stubbed external OpenEXR codec (returns unsupported when OpenEXR is not linked).
    /// Ported from <c>lib/extras/dec/exr.cc</c> and <c>lib/extras/enc/exr.cc</c>.
    /// </summary>
    public static class CodecExr
    {
        /// <summary>Returns whether external OpenEXR decoding is supported (<c>false</c>).</summary>
        /// <returns><c>false</c>.</returns>
        public static bool CanDecodeEXR() => false;

        /// <summary>
        /// Returns failure indicating external OpenEXR decoding is not enabled.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        /// <param name="colorHints">Color hints.</param>
        /// <param name="ppf">Destination packed pixel file.</param>
        /// <param name="constraints">Optional size constraints.</param>
        /// <returns>Failure <see cref="Status"/>.</returns>
        public static Status DecodeImageEXR(
            ReadOnlySpan<byte> bytes,
            ColorHints colorHints,
            PackedPixelFile ppf,
            SizeConstraints? constraints = null)
        {
            return Status.Failure("OpenEXR decoding is not supported");
        }

        /// <summary>Returns <c>null</c> as external OpenEXR encoding is not enabled.</summary>
        /// <returns><c>null</c>.</returns>
        public static Encoder? GetEXREncoder() => null;
    }
}
