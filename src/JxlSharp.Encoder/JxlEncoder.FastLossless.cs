// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    public sealed partial class JxlEncoder
    {
        private sealed class FastLosslessInput
        {
            public required byte[] Pixels;
            public JxlPixelFormat Format;
            public nuint Width, Height, Stride;
        }

        private bool CanUseFastLossless(JxlEncoderFrameSettingsValues values, in JxlPixelFormat format)
        {
            CompressParams cp = values.CParams;

            if (!values.Lossless || cp.SpeedTier != SpeedTier.kLightning || _metadata.M.XybEncoded ||
                _basicInfo.HaveAnimation || _basicInfo.HavePreview || _basicInfo.ExponentBitsPerSample != 0 ||
                _basicInfo.BitsPerSample is < 1 or > 16 || values.FrameIndexBox ||
                !string.IsNullOrEmpty(values.FrameName) || values.Header.NameLength != 0 ||
                values.Header.Duration != 0 || values.Header.Timecode != 0 || values.Header.LayerInfo.HaveCrop ||
                values.Header.LayerInfo.SaveAsReference != 0 ||
                values.Header.LayerInfo.BlendInfo.BlendMode != JxlBlendMode.Replace ||
                cp.Resampling > 1 || cp.EcResampling > 1 || cp.AlreadyDownsampled || cp.Responsive > 0 ||
                cp.ProgressiveDc > 0 || cp.ProgressiveMode == Override.On || cp.QProgressiveMode == Override.On ||
                cp.ModularGroupSizeShift is not (-1 or 1) || _stats != null || _debugImageCallback != null)
            {
                return false;
            }

            if (format.DataType is not (JxlDataType.UInt8 or JxlDataType.UInt16) ||
                (_basicInfo.BitsPerSample <= 8) != (format.DataType == JxlDataType.UInt8) ||
                GetEffectiveBitsPerSample(values.ImageBitDepth, in format) != _basicInfo.BitsPerSample)
            {
                return false;
            }

            bool alpha = _basicInfo.AlphaBits != 0;

            if (format.NumChannels != _basicInfo.NumColorChannels + (alpha ? 1u : 0u) ||
                _basicInfo.NumColorChannels is not (1 or 3) || _basicInfo.NumExtraChannels != (alpha ? 1u : 0u))
            {
                return false;
            }

            if (alpha && (_basicInfo.AlphaBits != _basicInfo.BitsPerSample || _basicInfo.AlphaExponentBits != 0 ||
                _metadata.M.ExtraChannelInfo[0].DimShift != 0))
            {
                return false;
            }

            foreach (JxlBlendInfo blend in values.ExtraChannelBlendInfo)
            {
                if (blend.BlendMode != JxlBlendMode.Replace)
                {
                    return false;
                }
            }

            return true;
        }

        private JxlEncoderStatus QueueFastLossless(JxlEncoderFrameSettings settings, in JxlPixelFormat format,
            ReadOnlySpan<byte> buffer, nuint width, nuint height)
        {
            nuint bytesPerSample = format.DataType == JxlDataType.UInt8 ? 1u : 2u;

            if (!Common.SafeMul(width, format.NumChannels * bytesPerSample, out nuint rowSize))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            nuint stride = rowSize;

            if (format.Align > 1 && stride % format.Align != 0 &&
                !Common.SafeAdd(stride, format.Align - stride % format.Align, out stride))
            {
                return SetError(JxlEncoderError.BadInput);
            }

            if (!Common.SafeMul(height - 1, stride, out nuint beforeLastRow) ||
                !Common.SafeAdd(beforeLastRow, rowSize, out nuint required) || required > (nuint)buffer.Length)
            {
                return SetError(JxlEncoderError.BadInput);
            }

            // AddImageFrame consumes caller storage synchronously; keep a private snapshot.
            _inputQueue.Add(new QueuedFrame
            {
                OptionValues = settings.Values.Clone(),
                FastInput    = new FastLosslessInput
                {
                    Pixels = buffer.Slice(0, (int)required).ToArray(),
                    Format = format,
                    Width  = width,
                    Height = height,
                    Stride = stride,
                },
            });

            return JxlEncoderStatus.Success;
        }

        private bool MaterializeFastInput(QueuedFrame frame)
        {
            FastLosslessInput input = frame.FastInput!;
            var bundle              = new ImageBundle(_memoryManager, _metadata.M);
            var settings            = new JxlEncoderFrameSettings(this, frame.OptionValues);

            if (!ConvertBufferToImageBundle(settings, in input.Format, input.Pixels, input.Width, input.Height, bundle))
            {
                bundle.Dispose();

                return false;
            }

            frame.Bundle    = bundle;
            frame.FastInput = null;

            return true;
        }

        private JxlEncoderStatus EncodeFastLosslessFrame(QueuedFrame frame, bool isLast)
        {
            FastLosslessInput input = frame.FastInput!;
            bool bigEndian          = input.Format.Endianness == JxlEndianness.BigEndian ||
                (input.Format.Endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);
            var source              = EncFastLossless.CreateMemoryInputSource(input.Pixels, input.Stride,
                input.Format.NumChannels, _basicInfo.BitsPerSample);
            var args                = new FastLosslessPrepareArgs(source, input.Width, input.Height, input.Format.NumChannels,
                _basicInfo.BitsPerSample, bigEndian, effort: 2);
            using JxlFastLosslessFrameState? state = EncFastLossless.JxlFastLosslessPrepareFrame(in args);

            if (state == null)
            {
                return SetError(JxlEncoderError.Generic);
            }

            Status parallelStatus      = true;
            FJxlParallelRunner? runner = _threadPool == null ? null : (_, opaque, fun, count) =>
            {
                parallelStatus = DataParallel.RunOnPool(_threadPool, 0, checked((uint)count),
                    JxlSharp.Base.ThreadPool.NoInit, (task, _) => { fun(opaque, task); return true; }, "FastLossless");
            };

            if (!EncFastLossless.JxlFastLosslessProcessFrame(state, isLast, null, runner) || !parallelStatus ||
                !EncFastLossless.JxlFastLosslessPrepareHeader(state, addImageHeader: 0, isLast: isLast ? 1 : 0))
            {
                return SetError(JxlEncoderError.Generic);
            }

            _frameIndexEntries.Add(new FrameIndexBoxEntry(_codestreamBytesWritten, 0, false));
            int written = _useContainer
                ? EncFastLossless.AppendOutput(state, _codestreamAccumulator)
                : _output.AppendFastLosslessFrame(state);

            _codestreamBytesWritten += (ulong)written;

            return JxlEncoderStatus.Success;
        }
    }
}
