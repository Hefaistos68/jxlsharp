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
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;
using JxlSharp.Tools;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Represents a loaded or converted image ready for preview, inspection, and export in the WinForms UI.
    /// </summary>
    /// <param name="SourcePath">Original file path or in-memory label.</param>
    /// <param name="DetectedCodec">Detected image codec (<see cref="Codec.Jxl"/>, <see cref="Codec.Jpg"/>, etc.).</param>
    /// <param name="RawBytes">Raw encoded file bytes.</param>
    /// <param name="OriginalJpegBytes">Original or reconstructed JPEG bitstream bytes, when available.</param>
    /// <param name="PixelFile">Decoded <see cref="PackedPixelFile"/> representation.</param>
    /// <param name="PreviewBitmap">Rendered GDI+ <see cref="Bitmap"/> for UI preview.</param>
    /// <param name="JxlReport">Populated <see cref="JxlPropertyReport"/> when the image is a JPEG XL file.</param>
    public readonly record struct JxlLoadedImage(
        string SourcePath,
        Codec DetectedCodec,
        byte[] RawBytes,
        byte[]? OriginalJpegBytes,
        PackedPixelFile PixelFile,
        Bitmap PreviewBitmap,
        JxlPropertyReport? JxlReport);

    /// <summary>
    /// Provides image loading, preview rendering, JPG/JXL conversion, file saving,
    /// and CLI tool execution (<c>cjxl</c>, <c>djxl</c>, <c>jxlinfo</c>, <c>jxltran</c>).
    /// </summary>
    public sealed class JxlImageService
    {
        private readonly JxlPropertyInspector _inspector;
        private readonly JxlCliOptionsBuilder _optionsBuilder;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlImageService"/> class.
        /// </summary>
        public JxlImageService()
            : this(new JxlPropertyInspector(), new JxlCliOptionsBuilder())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlImageService"/> class with custom helpers.
        /// </summary>
        /// <param name="inspector">JXL property inspector.</param>
        /// <param name="optionsBuilder">CLI options builder.</param>
        public JxlImageService(JxlPropertyInspector inspector, JxlCliOptionsBuilder optionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(inspector);
            ArgumentNullException.ThrowIfNull(optionsBuilder);

            _inspector      = inspector;
            _optionsBuilder = optionsBuilder;
        }

        /// <summary>
        /// Loads an image file from disk (JPG, JXL, PNG, PNM, PGX) and builds its preview bitmap and metadata.
        /// </summary>
        /// <param name="filePath">Path to the image file.</param>
        /// <param name="dargs">Optional JXL decompression arguments.</param>
        /// <returns>Loaded <see cref="JxlLoadedImage"/>.</returns>
        public JxlLoadedImage LoadImageFile(string filePath, DecompressArgs? dargs = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Image file not found.", filePath);
            }

            byte[] data = File.ReadAllBytes(filePath);

            return LoadImageBytes(data, filePath, dargs);
        }

        /// <summary>
        /// Loads an image from an encoded byte array and builds its preview bitmap and metadata.
        /// </summary>
        /// <param name="data">Encoded file bytes.</param>
        /// <param name="sourcePath">Source file path or extension hint.</param>
        /// <param name="dargs">Optional JXL decompression arguments.</param>
        /// <returns>Loaded <see cref="JxlLoadedImage"/>.</returns>
        public JxlLoadedImage LoadImageBytes(byte[] data, string sourcePath, DecompressArgs? dargs = null)
        {
            ArgumentNullException.ThrowIfNull(data);

            Codec codec = ExtrasDecodeFacade.DetectCodec(data);

            if (codec == Codec.Unknown && !string.IsNullOrWhiteSpace(sourcePath))
            {
                codec = ExtrasDecodeFacade.CodecFromPath(sourcePath);
            }

            if (codec == Codec.Jxl)
            {
                return LoadJxlBytesInternal(data, sourcePath, dargs ?? new DecompressArgs());
            }

            if (codec == Codec.Jpg)
            {
                return LoadJpegBytesInternal(data, sourcePath);
            }

            return LoadExtrasBytesInternal(data, sourcePath, codec);
        }

        /// <summary>
        /// Converts a GDI+ <see cref="Bitmap"/> into a <see cref="PackedPixelFile"/> (8-bit sRGB RGB or RGBA).
        /// </summary>
        /// <param name="bitmap">Source bitmap.</param>
        /// <returns>Populated <see cref="PackedPixelFile"/>.</returns>
        public PackedPixelFile ConvertBitmapToPackedPixelFile(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            int width     = bitmap.Width;
            int height    = bitmap.Height;
            bool hasAlpha = Image.IsAlphaPixelFormat(bitmap.PixelFormat);
            uint channels = hasAlpha ? 4u : 3u;

            PackedPixelFile ppf = InitializeSrgbPackedPixelFile(width, height, hasAlpha);

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = channels,
                DataType    = JxlDataType.UInt8,
                Endianness  = JxlEndianness.NativeEndian,
                Align       = 0,
            };

            StatusOr<PackedFrame> frameOr = PackedFrame.Create((nuint)width, (nuint)height, format);

            if (!frameOr.IsOk)
            {
                throw new InvalidOperationException("Failed to allocate PackedFrame for bitmap.");
            }

            PackedFrame frame = frameOr.Value;

            CopyBitmapPixelsToPackedImage(bitmap, frame.Color, hasAlpha);
            ppf.Frames.Add(frame);

            return ppf;
        }

        /// <summary>
        /// Renders a frame of a <see cref="PackedPixelFile"/> into a 32-bit ARGB <see cref="Bitmap"/> for display.
        /// </summary>
        /// <param name="ppf">Source packed pixel file.</param>
        /// <param name="frameIndex">Zero-based frame index.</param>
        /// <returns>Rendered <see cref="Bitmap"/>.</returns>
        public Bitmap ConvertPackedPixelFileToBitmap(PackedPixelFile ppf, int frameIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(ppf);

            if (ppf.Frames.Count == 0)
            {
                throw new ArgumentException("PackedPixelFile contains no frames.", nameof(ppf));
            }

            int clampedIndex  = Math.Clamp(frameIndex, 0, ppf.Frames.Count - 1);
            PackedImage color = ppf.Frames[clampedIndex].Color;
            int width         = Math.Max(1, (int)color.XSize);
            int height        = Math.Max(1, (int)color.YSize);
            Bitmap bitmap     = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            WritePackedImageToBitmap(color, ppf.Info.BitsPerSample, bitmap);

            return bitmap;
        }

        /// <summary>
        /// Encodes a <see cref="Bitmap"/> to JPEG bytes using the Windows GDI+ JPEG codec.
        /// </summary>
        /// <param name="bitmap">Source bitmap.</param>
        /// <param name="quality">JPEG quality level (<c>0..100</c>).</param>
        /// <returns>Encoded JPEG file bytes.</returns>
        public byte[] EncodeBitmapToJpegBytes(Bitmap bitmap, long quality = 95)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            long clampedQuality        = Math.Clamp(quality, 0L, 100L);
            ImageCodecInfo? jpegCodec  = FindJpegCodecInfo();
            using MemoryStream ms      = new MemoryStream();

            if (jpegCodec is null)
            {
                bitmap.Save(ms, ImageFormat.Jpeg);

                return ms.ToArray();
            }

            using EncoderParameters encParams = new EncoderParameters(1);
            encParams.Param[0]                = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, clampedQuality);
            bitmap.Save(ms, jpegCodec, encParams);

            return ms.ToArray();
        }

        /// <summary>
        /// Decodes JPEG bytes into an in-memory <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="jpegBytes">Encoded JPEG bytes.</param>
        /// <returns>Decoded <see cref="Bitmap"/>.</returns>
        public Bitmap DecodeJpegBytesToBitmap(ReadOnlySpan<byte> jpegBytes)
        {
            byte[] copy           = jpegBytes.ToArray();
            using MemoryStream ms = new MemoryStream(copy);
            using Image tempImage = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);

            Bitmap result = new Bitmap(tempImage.Width, tempImage.Height, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(tempImage, new Rectangle(0, 0, result.Width, result.Height));
            }

            return result;
        }

        /// <summary>
        /// Encodes a <see cref="PackedPixelFile"/> (or transcodes <paramref name="jpegBytes"/>) into JPEG XL bytes
        /// using the specified <see cref="CompressArgs"/> and inspects the resulting JXL properties.
        /// </summary>
        /// <param name="ppf">Input pixel data.</param>
        /// <param name="jpegBytes">Optional original JPEG bytes for lossless JPEG transcoding.</param>
        /// <param name="cargs">Compression arguments.</param>
        /// <param name="report">Receives the inspected <see cref="JxlPropertyReport"/> of the encoded JXL.</param>
        /// <param name="logOutput">Receives status and diagnostic messages.</param>
        /// <returns>Compressed JXL bytes.</returns>
        public byte[] ConvertToJxlBytes(
            PackedPixelFile ppf,
            byte[]? jpegBytes,
            CompressArgs cargs,
            out JxlPropertyReport report,
            out string logOutput)
        {
            ArgumentNullException.ThrowIfNull(ppf);
            ArgumentNullException.ThrowIfNull(cargs);

            bool useLosslessJpeg      = jpegBytes is not null && cargs.LosslessJpeg == Override.On && !cargs.GotDistance;
            byte[]? transcodeJpeg     = useLosslessJpeg ? jpegBytes : null;
            JXLCompressParams jparams = _optionsBuilder.CreateCompressParams(cargs, ppf, jpegBytes is not null);
            double t0                 = ExtrasTime.Now();
            bool ok                   = CodecJxl.EncodeImageJXL(jparams, ppf, transcodeJpeg, out byte[] compressed);

            if (!ok && transcodeJpeg is not null)
            {
                ok = CodecJxl.EncodeImageJXL(jparams, ppf, null, out compressed);
            }

            double elapsedMs = (ExtrasTime.Now() - t0) * 1000.0;

            if (!ok || compressed.Length == 0)
            {
                throw new InvalidOperationException("Failed to encode image to JPEG XL.");
            }

            report    = _inspector.InspectBytes(compressed, "(Converted JXL)");
            logOutput = string.Format(
                CultureInfo.InvariantCulture,
                "Encoded {0}x{1} to JXL: {2} bytes ({3:F2} ms).",
                ppf.XSize,
                ppf.YSize,
                compressed.Length,
                elapsedMs);

            return compressed;
        }

        /// <summary>
        /// Converts JPEG XL bytes to JPEG bytes (using lossless <c>jbrd</c> reconstruction when available
        /// and <see cref="DecompressArgs.PixelsToJpeg"/> is <c>false</c>, or encoding decoded pixels otherwise).
        /// </summary>
        /// <param name="jxlBytes">Input JXL bytes.</param>
        /// <param name="dargs">Decompression arguments.</param>
        /// <param name="ppf">Receives the decoded <see cref="PackedPixelFile"/>.</param>
        /// <param name="logOutput">Receives status and diagnostic messages.</param>
        /// <returns>Output JPEG bytes.</returns>
        public byte[] ConvertJxlToJpegBytes(
            byte[] jxlBytes,
            DecompressArgs dargs,
            out PackedPixelFile ppf,
            out string logOutput)
        {
            ArgumentNullException.ThrowIfNull(jxlBytes);
            ArgumentNullException.ThrowIfNull(dargs);

            JXLDecompressParams dparams = _optionsBuilder.CreateDecompressParams(dargs);
            ppf                         = new PackedPixelFile();
            List<byte>? reconJpeg       = dargs.PixelsToJpeg ? null : new List<byte>();
            double t0                   = ExtrasTime.Now();
            bool ok                     = CodecJxl.DecodeImageJXL(jxlBytes, dparams, out _, ppf, reconJpeg);

            if ((!ok || (reconJpeg is not null && reconJpeg.Count == 0)) && reconJpeg is not null)
            {
                reconJpeg = null;
                ppf       = new PackedPixelFile();
                ok        = CodecJxl.DecodeImageJXL(jxlBytes, dparams, out _, ppf, null);
            }

            if (!ok)
            {
                throw new InvalidOperationException("Failed to decode JPEG XL image.");
            }

            if (reconJpeg is not null && reconJpeg.Count > 0)
            {
                byte[] reconstructed = reconJpeg.ToArray();

                EnsurePixelFilePopulatedFromJpeg(reconstructed, ppf);
                double reconMs = (ExtrasTime.Now() - t0) * 1000.0;
                logOutput      = $"Reconstructed lossless JPEG from jbrd ({reconstructed.Length} bytes, {reconMs:F2} ms).";

                return reconstructed;
            }

            using Bitmap bmp = ConvertPackedPixelFileToBitmap(ppf);
            byte[] encoded   = EncodeBitmapToJpegBytes(bmp, (long)dargs.JpegQuality);
            double encMs     = (ExtrasTime.Now() - t0) * 1000.0;

            logOutput = $"Encoded JXL pixels to JPEG (quality {dargs.JpegQuality}, {encoded.Length} bytes, {encMs:F2} ms).";

            return encoded;
        }

        /// <summary>
        /// Saves the loaded image to the specified <paramref name="outputPath"/> in the format determined by its file extension.
        /// </summary>
        /// <param name="outputPath">Destination file path (<c>.jxl</c>, <c>.jpg</c>, <c>.png</c>, <c>.ppm</c>, <c>.pfm</c>, <c>.pgx</c>, <c>.npy</c>).</param>
        /// <param name="loadedImage">Currently loaded image state.</param>
        /// <param name="cargs">JXL compression arguments when saving as <c>.jxl</c>.</param>
        /// <param name="dargs">JXL decompression arguments when saving as <c>.jpg</c> or other formats.</param>
        /// <param name="logOutput">Receives a human-readable summary of the save operation.</param>
        /// <returns><c>true</c> if saved successfully; otherwise <c>false</c>.</returns>
        public bool SaveImageFile(
            string outputPath,
            in JxlLoadedImage loadedImage,
            CompressArgs cargs,
            DecompressArgs dargs,
            out string logOutput)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(cargs);
            ArgumentNullException.ThrowIfNull(dargs);

            string ext = Path.GetExtension(outputPath).ToLowerInvariant();

            if (ext == ".jxl")
            {
                byte[] jxlBytes = ConvertToJxlBytes(
                    loadedImage.PixelFile,
                    loadedImage.OriginalJpegBytes,
                    cargs,
                    out _,
                    out logOutput);

                File.WriteAllBytes(outputPath, jxlBytes);

                return true;
            }

            if (ext == ".jpg" || ext == ".jpeg")
            {
                return SaveAsJpegFile(outputPath, in loadedImage, dargs, out logOutput);
            }

            return SaveViaExtrasEncoder(outputPath, loadedImage.PixelFile, out logOutput);
        }

        /// <summary>
        /// Runs the <c>cjxl</c> CLI tool with full support for both lossless JPEG transcoding and pixel-based JPEG/PNG/PNM/PGX encoding.
        /// </summary>
        /// <param name="cargs">Compression arguments.</param>
        /// <param name="stdout">Receives standard output text.</param>
        /// <param name="stderr">Receives standard error text.</param>
        /// <returns>CLI tool exit code (<c>0</c> on success).</returns>
        public int RunCjxl(CompressArgs cargs, out string stdout, out string stderr)
        {
            ArgumentNullException.ThrowIfNull(cargs);

            string? tempPngPath = PrepareTempPngIfLossyJpegInput(cargs, out string? originalFileIn);

            try
            {
                string[] args             = _optionsBuilder.BuildCjxlArgs(cargs);
                using StringWriter outSw  = new StringWriter();
                using StringWriter errSw  = new StringWriter();
                int exitCode              = CjxlTool.Run(args, outSw, errSw);

                stdout = outSw.ToString();
                stderr = errSw.ToString();

                return exitCode;
            }
            finally
            {
                if (originalFileIn is not null)
                {
                    cargs.FileIn = originalFileIn;
                }

                DeleteTempFileIfPresent(tempPngPath);
            }
        }

        /// <summary>
        /// Runs the <c>djxl</c> CLI tool with full support for both lossless JPEG reconstruction and pixel-to-JPEG encoding.
        /// </summary>
        /// <param name="dargs">Decompression arguments.</param>
        /// <param name="stdout">Receives standard output text.</param>
        /// <param name="stderr">Receives standard error text.</param>
        /// <returns>CLI tool exit code (<c>0</c> on success).</returns>
        public int RunDjxl(DecompressArgs dargs, out string stdout, out string stderr)
        {
            ArgumentNullException.ThrowIfNull(dargs);

            if (RequiresPixelToJpegFallback(dargs))
            {
                return RunDjxlWithPixelJpegFallback(dargs, out stdout, out stderr);
            }

            string[] args            = _optionsBuilder.BuildDjxlArgs(dargs);
            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();
            int exitCode             = DjxlTool.Run(args, outSw, errSw);

            stdout = outSw.ToString();
            stderr = errSw.ToString();

            return exitCode;
        }

        /// <summary>
        /// Runs the <c>jxlinfo</c> CLI tool on the specified file.
        /// </summary>
        /// <param name="filePath">Input JXL file path.</param>
        /// <param name="verbose">Whether verbose (<c>-v</c>) output is enabled.</param>
        /// <param name="stdout">Receives standard output text.</param>
        /// <param name="stderr">Receives standard error text.</param>
        /// <returns>CLI tool exit code (<c>0</c> on success).</returns>
        public int RunJxlInfo(string? filePath, bool verbose, out string stdout, out string stderr)
        {
            string[] args            = _optionsBuilder.BuildJxlInfoArgs(filePath, verbose);
            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();
            int exitCode             = JxlInfoTool.Run(args, outSw, errSw);

            stdout = outSw.ToString();
            stderr = errSw.ToString();

            return exitCode;
        }

        /// <summary>
        /// Runs the <c>jxltran</c> lossless container tool.
        /// </summary>
        /// <param name="targs">Container transformation arguments.</param>
        /// <param name="stdout">Receives standard output text.</param>
        /// <param name="stderr">Receives standard error text.</param>
        /// <returns>CLI tool exit code (<c>0</c> on success).</returns>
        public int RunJxlTran(JxlTranArgs targs, out string stdout, out string stderr)
        {
            ArgumentNullException.ThrowIfNull(targs);

            string[] args            = _optionsBuilder.BuildJxlTranArgs(targs);
            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();
            int exitCode             = JxlTranTool.Run(args, outSw, errSw);

            stdout = outSw.ToString();
            stderr = errSw.ToString();

            return exitCode;
        }

        /// <summary>
        /// Computes PSNR and SSIMULACRA2 perceptual quality metrics between two <see cref="PackedPixelFile"/> images.
        /// </summary>
        /// <param name="referencePpf">Reference image.</param>
        /// <param name="distortedPpf">Distorted/compared image.</param>
        /// <param name="psnr">Receives the computed PSNR in dB.</param>
        /// <param name="ssimulacra2">Receives the computed SSIMULACRA2 score.</param>
        /// <returns><c>true</c> if metrics were computed successfully; otherwise <c>false</c>.</returns>
        public bool ComputeImageMetrics(
            PackedPixelFile referencePpf,
            PackedPixelFile distortedPpf,
            out double psnr,
            out double ssimulacra2)
        {
            psnr        = 0.0;
            ssimulacra2 = 0.0;

            if (referencePpf is null || distortedPpf is null ||
                referencePpf.XSize != distortedPpf.XSize ||
                referencePpf.YSize != distortedPpf.YSize ||
                referencePpf.Frames.Count == 0 || distortedPpf.Frames.Count == 0)
            {
                return false;
            }

            CodecInOut refIo  = new CodecInOut();
            CodecInOut distIo = new CodecInOut();

            if (!PackedImageConvert.ConvertPackedPixelFileToCodecInOut(referencePpf, null, refIo) ||
                !PackedImageConvert.ConvertPackedPixelFileToCodecInOut(distortedPpf, null, distIo))
            {
                return false;
            }

            JxlCmsInterface cms = JxlCms.GetDefaultCms();

            psnr = ExtrasMetrics.ComputePSNR(refIo.Main, distIo.Main, cms);

            StatusOr<Msssim> ssimOr = Ssimulacra2.ComputeSSIMULACRA2(refIo.Main, distIo.Main);

            if (ssimOr.IsOk)
            {
                ssimulacra2 = ssimOr.Value.Score();
            }

            return true;
        }

        private JxlLoadedImage LoadJxlBytesInternal(byte[] data, string sourcePath, DecompressArgs dargs)
        {
            JxlPropertyReport report    = _inspector.InspectBytes(data, sourcePath);
            JXLDecompressParams dparams = _optionsBuilder.CreateDecompressParams(dargs);
            PackedPixelFile ppf         = new PackedPixelFile();
            List<byte>? reconJpeg       = (!dargs.PixelsToJpeg && report.HasJpegReconstruction) ? new List<byte>() : null;

            bool ok = CodecJxl.DecodeImageJXL(data, dparams, out _, ppf, reconJpeg);

            if (!ok)
            {
                throw new InvalidOperationException("Failed to decode JXL image.");
            }

            byte[]? jpegBytes = (reconJpeg is not null && reconJpeg.Count > 0) ? reconJpeg.ToArray() : null;

            if (ppf.Frames.Count == 0 && jpegBytes is not null)
            {
                EnsurePixelFilePopulatedFromJpeg(jpegBytes, ppf);
            }

            if (!string.IsNullOrEmpty(dargs.AlphaBlend))
            {
                ApplyOptionalAlphaBlend(ppf, dargs.AlphaBlend);
            }

            Bitmap preview = ConvertPackedPixelFileToBitmap(ppf);

            return new JxlLoadedImage(sourcePath, Codec.Jxl, data, jpegBytes, ppf, preview, report);
        }

        private JxlLoadedImage LoadJpegBytesInternal(byte[] data, string sourcePath)
        {
            Bitmap preview      = DecodeJpegBytesToBitmap(data);
            PackedPixelFile ppf = ConvertBitmapToPackedPixelFile(preview);

            return new JxlLoadedImage(sourcePath, Codec.Jpg, data, data, ppf, preview, null);
        }

        private JxlLoadedImage LoadExtrasBytesInternal(byte[] data, string sourcePath, Codec detectedCodec)
        {
            PackedPixelFile ppf = new PackedPixelFile();
            ColorHints hints    = new ColorHints();
            Status status       = ExtrasDecodeFacade.DecodeBytes(data, hints, ppf, null, out Codec origCodec);

            if (!status)
            {
                throw new InvalidOperationException($"Failed to decode image: {status}");
            }

            Bitmap preview = ConvertPackedPixelFileToBitmap(ppf);
            Codec final    = origCodec != Codec.Unknown ? origCodec : detectedCodec;

            return new JxlLoadedImage(sourcePath, final, data, null, ppf, preview, null);
        }

        private void EnsurePixelFilePopulatedFromJpeg(byte[] jpegBytes, PackedPixelFile ppf)
        {
            using Bitmap bmp         = DecodeJpegBytesToBitmap(jpegBytes);
            PackedPixelFile converted = ConvertBitmapToPackedPixelFile(bmp);

            ppf.Info          = converted.Info;
            ppf.ColorEncoding = converted.ColorEncoding;
            ppf.Frames.Clear();

            foreach (PackedFrame frame in converted.Frames)
            {
                ppf.Frames.Add(frame);
            }
        }

        private static void ApplyOptionalAlphaBlend(PackedPixelFile ppf, string blendSpec)
        {
            if (string.Equals(blendSpec, "black", StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<float> black = stackalloc float[3] { 0.0f, 0.0f, 0.0f };
                ExtrasAlphaBlend.AlphaBlend(ppf, black);
            }
            else if (string.Equals(blendSpec, "white", StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<float> white = stackalloc float[3] { 1.0f, 1.0f, 1.0f };
                ExtrasAlphaBlend.AlphaBlend(ppf, white);
            }
        }

        private bool SaveAsJpegFile(
            string outputPath,
            in JxlLoadedImage loadedImage,
            DecompressArgs dargs,
            out string logOutput)
        {
            if (loadedImage.DetectedCodec == Codec.Jxl)
            {
                byte[] jpgBytes = ConvertJxlToJpegBytes(loadedImage.RawBytes, dargs, out _, out logOutput);

                File.WriteAllBytes(outputPath, jpgBytes);

                return true;
            }

            byte[] encoded = EncodeBitmapToJpegBytes(loadedImage.PreviewBitmap, (long)dargs.JpegQuality);

            File.WriteAllBytes(outputPath, encoded);
            logOutput = $"Saved JPEG ({encoded.Length} bytes, quality {dargs.JpegQuality}).";

            return true;
        }

        private static bool SaveViaExtrasEncoder(string outputPath, PackedPixelFile ppf, out string logOutput)
        {
            Status status = ExtrasCodec.Encode(ppf, outputPath, out byte[] encodedBytes);

            if (!status)
            {
                logOutput = $"Failed to encode {outputPath}: {status}";

                return false;
            }

            File.WriteAllBytes(outputPath, encodedBytes);
            logOutput = $"Saved {outputPath} ({encodedBytes.Length} bytes).";

            return true;
        }

        private string? PrepareTempPngIfLossyJpegInput(CompressArgs cargs, out string? originalFileIn)
        {
            originalFileIn = null;

            if (string.IsNullOrWhiteSpace(cargs.FileIn) || !File.Exists(cargs.FileIn))
            {
                return null;
            }

            Codec codec = ExtrasDecodeFacade.CodecFromPath(cargs.FileIn);

            if (codec != Codec.Jpg)
            {
                return null;
            }

            bool needsPixels = cargs.LosslessJpeg == Override.Off ||
                               cargs.DecodeToPixels ||
                               cargs.StreamingInput ||
                               (cargs.GotQuality && cargs.Quality < 100.0f && cargs.LosslessJpeg == Override.Default);

            if (!needsPixels)
            {
                return null;
            }

            byte[] jpgBytes     = File.ReadAllBytes(cargs.FileIn);
            using Bitmap bmp    = DecodeJpegBytesToBitmap(jpgBytes);
            PackedPixelFile ppf = ConvertBitmapToPackedPixelFile(bmp);
            string tempPng      = Path.Combine(Path.GetTempPath(), $"jxl_in_{Guid.NewGuid():N}.png");

            if (!ExtrasCodec.Encode(ppf, tempPng, out byte[] pngBytes))
            {
                return null;
            }

            File.WriteAllBytes(tempPng, pngBytes);
            originalFileIn     = cargs.FileIn;
            cargs.FileIn       = tempPng;
            cargs.LosslessJpeg = Override.Off;

            return tempPng;
        }

        private bool RequiresPixelToJpegFallback(DecompressArgs dargs)
        {
            if (string.IsNullOrWhiteSpace(dargs.FileIn) ||
                string.IsNullOrWhiteSpace(dargs.FileOut) ||
                !File.Exists(dargs.FileIn))
            {
                return false;
            }

            string ext = Path.GetExtension(dargs.FileOut).ToLowerInvariant();

            if (ext != ".jpg" && ext != ".jpeg")
            {
                return false;
            }

            if (dargs.PixelsToJpeg)
            {
                return true;
            }

            JxlPropertyReport report = _inspector.InspectFile(dargs.FileIn);

            return !report.HasJpegReconstruction;
        }

        private int RunDjxlWithPixelJpegFallback(DecompressArgs dargs, out string stdout, out string stderr)
        {
            string targetJpgPath = dargs.FileOut!;
            string tempPngPath   = Path.Combine(Path.GetTempPath(), $"jxl_out_{Guid.NewGuid():N}.png");
            dargs.FileOut        = tempPngPath;

            try
            {
                string[] args            = _optionsBuilder.BuildDjxlArgs(dargs);
                using StringWriter outSw = new StringWriter();
                using StringWriter errSw = new StringWriter();
                int exitCode             = DjxlTool.Run(args, outSw, errSw);

                stdout = outSw.ToString();
                stderr = errSw.ToString();

                if (exitCode != 0 || !File.Exists(tempPngPath))
                {
                    return exitCode != 0 ? exitCode : 1;
                }

                JxlLoadedImage decoded = LoadImageFile(tempPngPath);
                byte[] jpgBytes        = EncodeBitmapToJpegBytes(decoded.PreviewBitmap, (long)dargs.JpegQuality);

                decoded.PreviewBitmap.Dispose();
                File.WriteAllBytes(targetJpgPath, jpgBytes);

                return 0;
            }
            finally
            {
                dargs.FileOut = targetJpgPath;
                DeleteTempFileIfPresent(tempPngPath);
            }
        }

        private static void DeleteTempFileIfPresent(string? path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static PackedPixelFile InitializeSrgbPackedPixelFile(int width, int height, bool hasAlpha)
        {
            PackedPixelFile ppf = new PackedPixelFile();

            JxlEncoder.JxlEncoderInitBasicInfo(out JxlBasicInfo info);
            info.XSize             = (uint)width;
            info.YSize             = (uint)height;
            info.BitsPerSample     = 8;
            info.ExponentBitsPerSample = 0;
            info.NumColorChannels  = 3;
            info.NumExtraChannels  = hasAlpha ? 1u : 0u;
            info.AlphaBits         = hasAlpha ? 8u : 0u;
            info.AlphaExponentBits = 0;
            info.UsesOriginalProfile = false;
            ppf.Info               = info;

            ColorDescription.ParseDescription("RGB_D65_SRG_Rel_SRG", out JxlColorEncoding ce);
            ppf.ColorEncoding = ce;

            return ppf;
        }

        private static void CopyBitmapPixelsToPackedImage(Bitmap bitmap, PackedImage dst, bool hasAlpha)
        {
            int width          = bitmap.Width;
            int height         = bitmap.Height;
            Rectangle rect     = new Rectangle(0, 0, width, height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride           = Math.Abs(bmpData.Stride);
                byte[] bgraBuffer    = new byte[stride * height];
                Span<byte> dstPixels = dst.Pixels();
                int channels         = hasAlpha ? 4 : 3;

                Marshal.Copy(bmpData.Scan0, bgraBuffer, 0, bgraBuffer.Length);

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * stride;
                    int dstRow = y * width * channels;

                    CopySingleRowBgraToRgb(bgraBuffer, srcRow, dstPixels, dstRow, width, hasAlpha);
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        private static void CopySingleRowBgraToRgb(
            byte[] bgraBuffer,
            int srcRow,
            Span<byte> dstPixels,
            int dstRow,
            int width,
            bool hasAlpha)
        {
            int channels = hasAlpha ? 4 : 3;

            for (int x = 0; x < width; x++)
            {
                int s = srcRow + (x * 4);
                int d = dstRow + (x * channels);

                dstPixels[d]     = bgraBuffer[s + 2];
                dstPixels[d + 1] = bgraBuffer[s + 1];
                dstPixels[d + 2] = bgraBuffer[s];

                if (hasAlpha)
                {
                    dstPixels[d + 3] = bgraBuffer[s + 3];
                }
            }
        }

        private static void WritePackedImageToBitmap(PackedImage color, uint bitsPerSample, Bitmap bitmap)
        {
            int width          = bitmap.Width;
            int height         = bitmap.Height;
            Rectangle rect     = new Rectangle(0, 0, width, height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride        = Math.Abs(bmpData.Stride);
                byte[] bgraBuffer = new byte[stride * height];

                ConvertPackedImageToBgraBuffer(color, bitsPerSample, width, height, stride, bgraBuffer);
                Marshal.Copy(bgraBuffer, 0, bmpData.Scan0, bgraBuffer.Length);
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        private static void ConvertPackedImageToBgraBuffer(
            PackedImage color,
            uint bitsPerSample,
            int width,
            int height,
            int dstStride,
            byte[] bgraBuffer)
        {
            ReadOnlySpan<byte> src = color.Pixels();
            int numChannels        = Math.Max(1, (int)color.Format.NumChannels);

            for (int y = 0; y < height; y++)
            {
                int dstRow = y * dstStride;

                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = ((y * width) + x) * numChannels;
                    int d          = dstRow + (x * 4);

                    ReadNormalizedRgba(src, pixelIndex, numChannels, color.Format, bitsPerSample, out byte r, out byte g, out byte b, out byte a);
                    bgraBuffer[d]     = b;
                    bgraBuffer[d + 1] = g;
                    bgraBuffer[d + 2] = r;
                    bgraBuffer[d + 3] = a;
                }
            }
        }

        private static void ReadNormalizedRgba(
            ReadOnlySpan<byte> src,
            int sampleIndex,
            int numChannels,
            in JxlPixelFormat format,
            uint bitsPerSample,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            byte c0 = ReadSampleAsByte(src, sampleIndex, in format, bitsPerSample);

            if (numChannels == 1)
            {
                r = c0;
                g = c0;
                b = c0;
                a = 255;

                return;
            }

            if (numChannels == 2)
            {
                r = c0;
                g = c0;
                b = c0;
                a = ReadSampleAsByte(src, sampleIndex + 1, in format, bitsPerSample);

                return;
            }

            r = c0;
            g = ReadSampleAsByte(src, sampleIndex + 1, in format, bitsPerSample);
            b = ReadSampleAsByte(src, sampleIndex + 2, in format, bitsPerSample);
            a = numChannels >= 4 ? ReadSampleAsByte(src, sampleIndex + 3, in format, bitsPerSample) : (byte)255;
        }

        private static byte ReadSampleAsByte(
            ReadOnlySpan<byte> src,
            int sampleIndex,
            in JxlPixelFormat format,
            uint bitsPerSample)
        {
            if (format.DataType == JxlDataType.UInt8)
            {
                return sampleIndex < src.Length ? ScaleUInt8Sample(src[sampleIndex], bitsPerSample) : (byte)0;
            }

            if (format.DataType == JxlDataType.UInt16)
            {
                return ReadUInt16SampleAsByte(src, sampleIndex * 2, format.Endianness, bitsPerSample);
            }

            if (format.DataType == JxlDataType.Float16)
            {
                return ReadFloat16SampleAsByte(src, sampleIndex * 2, format.Endianness);
            }

            return ReadFloat32SampleAsByte(src, sampleIndex * 4, format.Endianness);
        }

        private static byte ScaleUInt8Sample(byte value, uint bitsPerSample)
        {
            if (bitsPerSample > 0 && bitsPerSample < 8)
            {
                float maxVal = (1u << (int)bitsPerSample) - 1u;

                return (byte)Math.Clamp((int)MathF.Round(value * 255.0f / maxVal), 0, 255);
            }

            return value;
        }

        private static byte ReadUInt16SampleAsByte(
            ReadOnlySpan<byte> src,
            int byteOffset,
            JxlEndianness endianness,
            uint bitsPerSample)
        {
            if (byteOffset + 2 > src.Length)
            {
                return 0;
            }

            bool isBig = endianness == JxlEndianness.BigEndian ||
                         (endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);
            ushort raw = isBig
                ? BinaryPrimitives.ReadUInt16BigEndian(src.Slice(byteOffset, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(byteOffset, 2));
            float maxVal = (bitsPerSample > 0 && bitsPerSample < 16)
                ? ((1u << (int)bitsPerSample) - 1u)
                : 65535.0f;

            return (byte)Math.Clamp((int)MathF.Round(raw * 255.0f / maxVal), 0, 255);
        }

        private static byte ReadFloat16SampleAsByte(
            ReadOnlySpan<byte> src,
            int byteOffset,
            JxlEndianness endianness)
        {
            if (byteOffset + 2 > src.Length)
            {
                return 0;
            }

            bool isBig = endianness == JxlEndianness.BigEndian ||
                         (endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);
            ushort bits = isBig
                ? BinaryPrimitives.ReadUInt16BigEndian(src.Slice(byteOffset, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(byteOffset, 2));
            float val = (float)BitConverter.UInt16BitsToHalf(bits);

            return (byte)Math.Clamp((int)MathF.Round(Common.Clamp1(val, 0.0f, 1.0f) * 255.0f), 0, 255);
        }

        private static byte ReadFloat32SampleAsByte(
            ReadOnlySpan<byte> src,
            int byteOffset,
            JxlEndianness endianness)
        {
            if (byteOffset + 4 > src.Length)
            {
                return 0;
            }

            bool isBig = endianness == JxlEndianness.BigEndian ||
                         (endianness == JxlEndianness.NativeEndian && !BitConverter.IsLittleEndian);
            float val = isBig
                ? BinaryPrimitives.ReadSingleBigEndian(src.Slice(byteOffset, 4))
                : BinaryPrimitives.ReadSingleLittleEndian(src.Slice(byteOffset, 4));

            return (byte)Math.Clamp((int)MathF.Round(Common.Clamp1(val, 0.0f, 1.0f) * 255.0f), 0, 255);
        }

        private static ImageCodecInfo? FindJpegCodecInfo()
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();

            foreach (ImageCodecInfo codec in codecs)
            {
                if (string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return codec;
                }
            }

            return null;
        }
    }
}
