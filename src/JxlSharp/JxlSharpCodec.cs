// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.IO;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Extras;
using JxlSharp.Threads;

namespace JxlSharp
{
    /// <summary>
    /// Default implementation of <see cref="IJxlSimpleCodec"/> and <see cref="IJxlExtendedCodec"/>
    /// providing both simple one-call JPEG XL encoding/decoding with common defaults and full-parameter
    /// extended encoding/decoding.
    /// </summary>
    public sealed class JxlSharpCodec : IJxlExtendedCodec
    {
        private sealed class RunnerAdapter : IParallelRunner, IDisposable
        {
            private readonly int _workers;
            private ThreadParallelRunner? _runner;

            public RunnerAdapter(int numThreads)
            {
                _workers = numThreads <= 0 ? -1 : numThreads;
            }

            public JxlParallelRunner? Runner => Run;

            private static int Run(object? runner, object? opaque, JxlParallelRunInit init,
                JxlParallelRunFunction func, uint begin, uint end)
            {
                if (begin > end) return DataParallel.ParallelRetRunnerError;
                if (begin == end) return DataParallel.ParallelRetSuccess;

                // A single group needs one scratch slot and no worker wake-up.
                if (end - begin == 1)
                {
                    int result = init(opaque, 1);
                    if (result == DataParallel.ParallelRetSuccess) func(opaque, begin, 0);
                    return result;
                }

                var adapter = (RunnerAdapter)runner!;
                // Avoid creating OS threads for operations containing only single-task ranges.
                adapter._runner ??= new ThreadParallelRunner(adapter._workers);
                return ThreadParallelRunner.RunnerWithTaskLimit(adapter._runner, opaque, init, func, begin, end);
            }

            public object? RunnerOpaque => this;

            public void Dispose()
            {
                _runner?.Dispose();
            }
        }

        /// <inheritdoc />
        public JxlBasicInfo GetInfo(ReadOnlySpan<byte> jxlBytes)
        {
            if (jxlBytes.IsEmpty)
            {
                throw new ArgumentException("Input JXL buffer must not be empty.", nameof(jxlBytes));
            }

            using JxlDecoder dec = JxlDecoder.Create(null);
            byte[] input         = jxlBytes.ToArray();

            if (dec.SubscribeEvents((int)JxlDecoderStatus.BasicInfo) != JxlDecoderStatus.Success ||
                dec.SetInput(input) != JxlDecoderStatus.Success)
            {
                throw new InvalidOperationException("Failed to initialize JPEG XL decoder for header inspection.");
            }

            JxlDecoderStatus status = dec.ProcessInput();

            if (status != JxlDecoderStatus.BasicInfo ||
                dec.GetBasicInfo(out JxlBasicInfo info) != JxlDecoderStatus.Success)
            {
                throw new InvalidOperationException($"Failed to read JPEG XL basic info (status: {status}).");
            }

            return info;
        }

        /// <inheritdoc />
        public byte[] Encode(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            float quality = 90.0f,
            int effort = 7)
        {
            JxlImageData image = JxlImageData.FromPixels(pixels, width, height, channels);

            JxlEncodeOptions options = new JxlEncodeOptions
            {
                Quality = Math.Clamp(quality, 0.0f, 100.0f),
                Lossless = quality >= 100.0f,
                Effort = Math.Clamp(effort, 1, 11),
            };
            return Encode(image.ToPackedPixelFile(copyPixels: false), options, null);
        }

        /// <inheritdoc />
        public byte[] Encode(JxlImageData image, float quality = 90.0f, int effort = 7)
        {
            ArgumentNullException.ThrowIfNull(image);

            JxlEncodeOptions options = new JxlEncodeOptions
            {
                Quality  = Math.Clamp(quality, 0.0f, 100.0f),
                Lossless = quality >= 100.0f,
                Effort   = Math.Clamp(effort, 1, 11),
            };

            return Encode(image, options);
        }

        /// <inheritdoc />
        public byte[] EncodeLossless(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            int channels = 4,
            int effort = 7)
        {
            JxlImageData image = JxlImageData.FromPixels(pixels, width, height, channels);

            JxlEncodeOptions options = JxlEncodeOptions.CreateLossless(Math.Clamp(effort, 1, 11));
            return Encode(image.ToPackedPixelFile(copyPixels: false), options, null);
        }

        /// <inheritdoc />
        public byte[] EncodeLossless(JxlImageData image, int effort = 7)
        {
            ArgumentNullException.ThrowIfNull(image);

            JxlEncodeOptions options = JxlEncodeOptions.CreateLossless(Math.Clamp(effort, 1, 11));

            return Encode(image, options);
        }

        /// <inheritdoc />
        public byte[] EncodeFromFormat(ReadOnlySpan<byte> imageFileBytes, float quality = 90.0f, int effort = 7)
        {
            JxlEncodeOptions options = new JxlEncodeOptions
            {
                Quality  = Math.Clamp(quality, 0.0f, 100.0f),
                Lossless = quality >= 100.0f,
                Effort   = Math.Clamp(effort, 1, 11),
            };

            return EncodeFromFormat(imageFileBytes, options);
        }

        /// <inheritdoc />
        public void EncodeFile(string inputImagePath, string outputJxlPath, float quality = 90.0f, int effort = 7)
        {
            JxlEncodeOptions options = new JxlEncodeOptions
            {
                Quality  = Math.Clamp(quality, 0.0f, 100.0f),
                Lossless = quality >= 100.0f,
                Effort   = Math.Clamp(effort, 1, 11),
            };

            EncodeFile(inputImagePath, outputJxlPath, options);
        }

        /// <inheritdoc />
        public void EncodeToStream(JxlImageData image, Stream outputStream, float quality = 90.0f, int effort = 7)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(outputStream);

            byte[] encoded = Encode(image, quality, effort);

            outputStream.Write(encoded, 0, encoded.Length);
        }

        /// <inheritdoc />
        public JxlImageData Decode(ReadOnlySpan<byte> jxlBytes)
        {
            if (jxlBytes.IsEmpty) throw new ArgumentException("Input JXL buffer must not be empty.", nameof(jxlBytes));
            JxlDecodeResult result = DecodeCore(jxlBytes.ToArray(), JxlDecodeOptions.CreateDefault(), PrimaryImageHandling.TakePixels);

            if (result.PrimaryImage is null)
            {
                throw new InvalidOperationException("Decoded JPEG XL image did not produce any pixel frames.");
            }

            return result.PrimaryImage;
        }

        /// <inheritdoc />
        public JxlImageData Decode(Stream inputStream)
        {
            ArgumentNullException.ThrowIfNull(inputStream);

            byte[] bytes = ReadStreamToBytes(inputStream);

            if (bytes.Length == 0) throw new ArgumentException("Input JXL buffer must not be empty.", "jxlBytes");
            JxlDecodeResult result = DecodeCore(bytes, JxlDecodeOptions.CreateDefault(), PrimaryImageHandling.TakePixels);
            return result.PrimaryImage ?? throw new InvalidOperationException("Decoded JPEG XL image did not produce any pixel frames.");
        }

        /// <inheritdoc />
        public JxlImageData DecodeFile(string filePath)
        {
            JxlDecodeResult result = DecodeFileCore(filePath, JxlDecodeOptions.CreateDefault(), PrimaryImageHandling.TakePixels);

            if (result.PrimaryImage is null)
            {
                throw new InvalidOperationException("Decoded image file did not produce any pixel frames.");
            }

            return result.PrimaryImage;
        }

        /// <inheritdoc />
        public byte[] DecodeToFormat(ReadOnlySpan<byte> jxlBytes, string targetExtension)
        {
            return DecodeToFormat(jxlBytes, targetExtension, JxlDecodeOptions.CreateDefault());
        }

        /// <inheritdoc />
        public void DecodeToFile(string inputJxlPath, string outputImagePath)
        {
            ValidateFilePaths(inputJxlPath, outputImagePath);

            byte[] jxlBytes     = File.ReadAllBytes(inputJxlPath);
            string ext          = Path.GetExtension(outputImagePath);
            byte[] encodedBytes = DecodeToFormat(jxlBytes, ext);

            File.WriteAllBytes(outputImagePath, encodedBytes);
        }

        /// <inheritdoc />
        public byte[] Encode(JxlImageData image, JxlEncodeOptions options)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(options);

            PackedPixelFile ppf = image.ToPackedPixelFile();

            return Encode(ppf, options, null);
        }

        /// <inheritdoc />
        public byte[] Encode(PackedPixelFile pixelFile, JxlEncodeOptions options, byte[]? losslessJpegBytes = null)
        {
            ArgumentNullException.ThrowIfNull(pixelFile);
            ArgumentNullException.ThrowIfNull(options);

            bool useJpegTranscode     = options.ShouldUseLosslessJpegTranscode(losslessJpegBytes is not null);
            byte[]? jpegToTranscode   = useJpegTranscode ? losslessJpegBytes : null;
            JXLCompressParams cparams = options.ToCompressParams(pixelFile, losslessJpegBytes is not null);
            using IDisposable? scope  = AttachParallelRunner(cparams, options.NumThreads);
            bool ok                   = EncodeWithFallback(cparams, pixelFile, jpegToTranscode, out byte[] compressed);

            if (!ok || compressed.Length == 0)
            {
                throw new InvalidOperationException("Failed to encode image to JPEG XL.");
            }

            return compressed;
        }

        /// <inheritdoc />
        public byte[] EncodeFromFormat(ReadOnlySpan<byte> imageFileBytes, JxlEncodeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (imageFileBytes.IsEmpty)
            {
                throw new ArgumentException("Input image buffer must not be empty.", nameof(imageFileBytes));
            }

            Codec detected      = ExtrasDecodeFacade.DetectCodec(imageFileBytes);
            byte[]? rawJpeg     = detected == Codec.Jpg ? imageFileBytes.ToArray() : null;
            PackedPixelFile ppf = new PackedPixelFile();
            Status decodeStatus = ExtrasDecodeFacade.DecodeBytes(imageFileBytes, options.ColorHints, ppf);

            if (!decodeStatus && rawJpeg is null)
            {
                throw new InvalidOperationException($"Failed to decode input image buffer ({decodeStatus}).");
            }

            return Encode(ppf, options, rawJpeg);
        }

        /// <inheritdoc />
        public void EncodeFile(string inputImagePath, string outputJxlPath, JxlEncodeOptions options)
        {
            ValidateFilePaths(inputImagePath, outputJxlPath);
            ArgumentNullException.ThrowIfNull(options);

            byte[] inputBytes = File.ReadAllBytes(inputImagePath);
            byte[] jxlBytes   = EncodeFromFormat(inputBytes, options);

            File.WriteAllBytes(outputJxlPath, jxlBytes);
        }

        /// <inheritdoc />
        public void EncodeToStream(
            PackedPixelFile pixelFile,
            Stream outputStream,
            JxlEncodeOptions options,
            byte[]? losslessJpegBytes = null)
        {
            ArgumentNullException.ThrowIfNull(pixelFile);
            ArgumentNullException.ThrowIfNull(outputStream);
            ArgumentNullException.ThrowIfNull(options);

            byte[] encoded = Encode(pixelFile, options, losslessJpegBytes);

            outputStream.Write(encoded, 0, encoded.Length);
        }

        /// <inheritdoc />
        public JxlDecodeResult Decode(ReadOnlySpan<byte> jxlBytes, JxlDecodeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (jxlBytes.IsEmpty)
            {
                throw new ArgumentException("Input JXL buffer must not be empty.", nameof(jxlBytes));
            }

            return DecodeCore(jxlBytes.ToArray(), options, PrimaryImageHandling.Copy);
        }

        private static JxlDecodeResult DecodeCore(ReadOnlyMemory<byte> jxlBytes, JxlDecodeOptions options,
            PrimaryImageHandling primaryImageHandling)
        {
            JXLDecompressParams dparams = options.ToDecompressParams();
            using IDisposable? scope    = AttachParallelRunner(dparams, options.NumThreads);
            PackedPixelFile ppf         = new PackedPixelFile();
            byte[]? reconJpeg           = options.ReconstructJpegIfAvailable
                ? TryReconstructJpeg(jxlBytes, dparams, options.SizeConstraints)
                : null;

            bool ok = DecodePixelsWithColorSpaceFallback(jxlBytes, dparams, options.SizeConstraints, ppf, out nuint consumed);

            if (!ok && reconJpeg is null)
            {
                throw new InvalidOperationException("Failed to decode JPEG XL input.");
            }

            ApplyAlphaBlendIfRequested(ppf, options.AlphaBlendBackground);

            return new JxlDecodeResult(ppf, reconJpeg, consumed, primaryImageHandling);
        }

        /// <inheritdoc />
        public JxlDecodeResult Decode(Stream inputStream, JxlDecodeOptions options)
        {
            ArgumentNullException.ThrowIfNull(inputStream);
            ArgumentNullException.ThrowIfNull(options);

            byte[] bytes = ReadStreamToBytes(inputStream);

            if (bytes.Length == 0) throw new ArgumentException("Input JXL buffer must not be empty.", "jxlBytes");
            return DecodeCore(bytes, options, PrimaryImageHandling.Copy);
        }

        /// <inheritdoc />
        public JxlDecodeResult DecodeFile(string filePath, JxlDecodeOptions options)
        {
            return DecodeFileCore(filePath, options, PrimaryImageHandling.Copy);
        }

        private static JxlDecodeResult DecodeFileCore(string filePath, JxlDecodeOptions options,
            PrimaryImageHandling primaryImageHandling)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Image file not found.", filePath);
            }

            ArgumentNullException.ThrowIfNull(options);

            byte[] fileBytes = File.ReadAllBytes(filePath);
            Codec codec      = ExtrasDecodeFacade.DetectCodec(fileBytes);

            if (codec == Codec.Jxl || codec == Codec.Unknown)
            {
                if (fileBytes.Length == 0) throw new ArgumentException("Input JXL buffer must not be empty.", "jxlBytes");
                return DecodeCore(fileBytes, options, primaryImageHandling);
            }

            return DecodeExternalFormatBytes(fileBytes, options, primaryImageHandling);
        }

        /// <inheritdoc />
        public byte[] DecodeToFormat(ReadOnlySpan<byte> jxlBytes, string targetExtension, JxlDecodeOptions options)
        {
            if (string.IsNullOrWhiteSpace(targetExtension))
            {
                throw new ArgumentException("Target extension must not be empty.", nameof(targetExtension));
            }

            ArgumentNullException.ThrowIfNull(options);

            string normalizedExt = NormalizeExtension(targetExtension);
            if (jxlBytes.IsEmpty) throw new ArgumentException("Input JXL buffer must not be empty.", nameof(jxlBytes));
            JxlDecodeResult res = DecodeCore(jxlBytes.ToArray(), options, PrimaryImageHandling.Skip);

            if ((normalizedExt == ".jpg" || normalizedExt == ".jpeg") &&
                res.ReconstructedJpegBytes is not null &&
                res.ReconstructedJpegBytes.Length > 0)
            {
                return res.ReconstructedJpegBytes;
            }

            Status status = ExtrasCodec.Encode(res.PixelFile, $"output{normalizedExt}", out byte[] encodedBytes);

            if (!status)
            {
                throw new InvalidOperationException($"Failed to encode decoded JXL into format '{normalizedExt}': {status}");
            }

            return encodedBytes;
        }

        private static bool EncodeWithFallback(
            JXLCompressParams cparams,
            PackedPixelFile ppf,
            byte[]? jpegToTranscode,
            out byte[] compressed)
        {
            bool ok = CodecJxl.EncodeImageJXL(cparams, ppf, jpegToTranscode, out compressed);

            if (!ok && jpegToTranscode is not null && ppf.Frames.Count > 0)
            {
                ok = CodecJxl.EncodeImageJXL(cparams, ppf, null, out compressed);
            }

            return ok;
        }

        private static bool DecodePixelsWithColorSpaceFallback(
            ReadOnlyMemory<byte> jxlBytes,
            JXLDecompressParams dparams,
            SizeConstraints? constraints,
            PackedPixelFile ppf,
            out nuint consumed)
        {
            if (CodecJxl.DecodeImageJXLFromMemory(jxlBytes, dparams, out consumed, ppf, null, constraints))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(dparams.ColorSpace))
            {
                dparams.ColorSpace = string.Empty;
                ppf.Frames.Clear();

                return CodecJxl.DecodeImageJXLFromMemory(jxlBytes, dparams, out consumed, ppf, null, constraints);
            }

            return false;
        }

        private static byte[]? TryReconstructJpeg(
            ReadOnlyMemory<byte> jxlBytes,
            JXLDecompressParams dparams,
            SizeConstraints? constraints)
        {
            PackedPixelFile tempPpf = new PackedPixelFile();
            List<byte> reconBuffer  = new List<byte>();

            if (CodecJxl.DecodeImageJXLFromMemory(jxlBytes, dparams, out _, tempPpf, reconBuffer, constraints) &&
                reconBuffer.Count > 0)
            {
                return reconBuffer.ToArray();
            }

            return null;
        }

        private static JxlDecodeResult DecodeExternalFormatBytes(byte[] fileBytes, JxlDecodeOptions options,
            PrimaryImageHandling primaryImageHandling)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            ColorHints hints    = new ColorHints();

            if (!string.IsNullOrWhiteSpace(options.ColorSpace))
            {
                hints.Add("color_space", options.ColorSpace);
            }

            Status status = ExtrasDecodeFacade.DecodeBytes(
                fileBytes,
                hints,
                ppf,
                options.SizeConstraints,
                out _,
                options.MemoryManager,
                options.Coalescing);

            if (!status)
            {
                throw new InvalidOperationException($"Failed to decode external image file ({status}).");
            }

            ApplyAlphaBlendIfRequested(ppf, options.AlphaBlendBackground);

            return new JxlDecodeResult(ppf, null, (nuint)fileBytes.Length, primaryImageHandling);
        }

        private static void ApplyAlphaBlendIfRequested(PackedPixelFile ppf, float[]? background)
        {
            if (background is not null && background.Length >= 3 && ppf.Frames.Count > 0)
            {
                ExtrasAlphaBlend.AlphaBlend(ppf, background.AsSpan(0, 3));
            }
        }

        private static IDisposable? AttachParallelRunner(JXLCompressParams cparams, int numThreads)
        {
            if (numThreads == 1 || cparams.Runner is not null)
            {
                return null;
            }

            RunnerAdapter adapter = new RunnerAdapter(numThreads);
            cparams.Runner        = adapter;

            return adapter;
        }

        private static IDisposable? AttachParallelRunner(JXLDecompressParams dparams, int numThreads)
        {
            if (numThreads == 1 || dparams.Runner is not null)
            {
                return null;
            }

            RunnerAdapter adapter = new RunnerAdapter(numThreads);
            dparams.Runner        = adapter;

            return adapter;
        }

        private static byte[] ReadStreamToBytes(Stream stream)
        {
            if (stream is MemoryStream ms)
            {
                return ms.ToArray();
            }

            using MemoryStream copy = new MemoryStream();
            stream.CopyTo(copy);

            return copy.ToArray();
        }

        private static void ValidateFilePaths(string inputPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                throw new FileNotFoundException("Input image file not found.", inputPath);
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output file path must not be empty.", nameof(outputPath));
            }
        }

        private static string NormalizeExtension(string extension)
        {
            string trimmed = extension.Trim();

            return trimmed.StartsWith('.') ? trimmed.ToLowerInvariant() : $".{trimmed.ToLowerInvariant()}";
        }
    }
}
