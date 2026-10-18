// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Numerics;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;

namespace JxlSharp.Extras
{
    /// <summary>
    /// Encodes and decodes ICC profiles using the JPEG XL ICC compression format.
    /// Ported from <c>lib/extras/compressed_icc.cc</c>.
    /// </summary>
    public static class CompressedIcc
    {
        /// <summary>
        /// Compresses an ICC profile into JPEG XL compressed ICC format.
        /// Ported from <c>JxlICCProfileEncode</c> in <c>lib/extras/compressed_icc.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="icc">Uncompressed ICC profile bytes.</param>
        /// <param name="compressedIcc">Output compressed ICC profile bytes.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool JxlICCProfileEncode(
            JxlMemoryManager? memoryManager,
            ReadOnlySpan<byte> icc,
            out byte[] compressedIcc)
        {
            compressedIcc = Array.Empty<byte>();

            JxlMemoryManager mm = memoryManager ?? JxlMemoryManager.Default;
            BitWriter writer    = new BitWriter(mm);

            if (!IccCodec.WriteICC(icc, writer, LayerType.Header, null))
            {
                return false;
            }

            writer.ZeroPadToByte();
            compressedIcc = writer.GetSpan().ToArray();

            return true;
        }

        /// <summary>
        /// Decompresses a JPEG XL compressed ICC profile back into raw ICC profile bytes.
        /// Ported from <c>JxlICCProfileDecode</c> in <c>lib/extras/compressed_icc.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="compressedIcc">Compressed ICC profile bytes.</param>
        /// <param name="icc">Output decompressed ICC profile bytes.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool JxlICCProfileDecode(
            JxlMemoryManager? memoryManager,
            ReadOnlySpan<byte> compressedIcc,
            out byte[] icc)
        {
            icc = Array.Empty<byte>();

            JxlMemoryManager mm      = memoryManager ?? JxlMemoryManager.Default;
            ICCReader iccReader      = new ICCReader(mm);
            PaddedBytes decompressed = new PaddedBytes(mm);
            using BitReader reader   = new BitReader(compressedIcc);

            if (!iccReader.Init(reader) ||
                !iccReader.Process(reader, decompressed) ||
                !reader.Close())
            {
                return false;
            }

            icc = decompressed.Span.ToArray();

            return true;
        }
    }

    /// <summary>
    /// Serializes and deserializes <see cref="JxlGainMapBundle"/> (`jhgm` box) structures.
    /// Ported from <c>lib/extras/gain_map.cc</c>.
    /// </summary>
    public static class GainMapCodec
    {
        /// <summary>
        /// Calculates the total byte size required to serialize <paramref name="mapBundle"/> into a binary buffer.
        /// Ported from <c>JxlGainMapGetBundleSize</c> in <c>lib/extras/gain_map.cc</c>.
        /// </summary>
        /// <param name="mapBundle">Gain map bundle to measure.</param>
        /// <param name="bundleSize">Output serialized byte size.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool JxlGainMapGetBundleSize(in JxlGainMapBundle mapBundle, out nuint bundleSize)
        {
            bundleSize = 0;

            if (mapBundle == null)
            {
                return false;
            }

            nuint metadataLen = (nuint)(mapBundle.GainMapMetadata?.Length ?? 0);

            if (metadataLen > ushort.MaxValue)
            {
                return false;
            }

            nuint colorEncodingSize = 0;

            if (mapBundle.HasColorEncoding)
            {
                ColorEncoding internalColorEncoding = new ColorEncoding();

                if (!internalColorEncoding.FromExternal(mapBundle.ColorEncoding) ||
                    !Bundle.CanEncode(internalColorEncoding, out _, out colorEncodingSize))
                {
                    return false;
                }
            }

            nuint altIccLen  = (nuint)(mapBundle.AltIcc?.Length ?? 0);
            nuint gainMapLen = (nuint)(mapBundle.GainMap?.Length ?? 0);

            bundleSize =
                1u +
                2u +
                metadataLen +
                1u +
                Common.DivCeil(colorEncodingSize, 8u) +
                4u +
                altIccLen +
                gainMapLen;

            return true;
        }

        /// <summary>
        /// Serializes <paramref name="mapBundle"/> into <paramref name="outputBuffer"/>.
        /// Ported from <c>JxlGainMapWriteBundle</c> in <c>lib/extras/gain_map.cc</c>.
        /// </summary>
        /// <param name="mapBundle">Gain map bundle to serialize.</param>
        /// <param name="outputBuffer">Destination buffer (must match the exact bundle size).</param>
        /// <param name="bytesWritten">Output number of bytes written.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool JxlGainMapWriteBundle(
            in JxlGainMapBundle mapBundle,
            Span<byte> outputBuffer,
            out nuint bytesWritten)
        {
            bytesWritten = 0;

            if (mapBundle == null || !EncodeBundleColorEncoding(mapBundle, out ReadOnlySpan<byte> colorEncBytes))
            {
                return false;
            }

            ReadOnlySpan<byte> metadata = mapBundle.GainMapMetadata ?? Array.Empty<byte>();
            ReadOnlySpan<byte> altIcc   = mapBundle.AltIcc ?? Array.Empty<byte>();
            ReadOnlySpan<byte> gainMap  = mapBundle.GainMap ?? Array.Empty<byte>();

            if (metadata.Length > ushort.MaxValue || colorEncBytes.Length > byte.MaxValue)
            {
                return false;
            }

            ulong cursor     = 0;
            ulong nextCursor = 0;
            ulong bufLen     = (ulong)outputBuffer.Length;

            if (!TryWriteByte(mapBundle.JhgmVersion, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBE16((ushort)metadata.Length, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBytes(metadata, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteByte((byte)colorEncBytes.Length, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBytes(colorEncBytes, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBE32((uint)altIcc.Length, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBytes(altIcc, outputBuffer, ref cursor, ref nextCursor, bufLen) ||
                !TryWriteBytes(gainMap, outputBuffer, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            cursor       = nextCursor;
            bytesWritten = (nuint)cursor;

            return cursor == bufLen;
        }

        /// <summary>
        /// Deserializes a <see cref="JxlGainMapBundle"/> from <paramref name="inputBuffer"/>.
        /// Ported from <c>JxlGainMapReadBundle</c> in <c>lib/extras/gain_map.cc</c>.
        /// </summary>
        /// <param name="mapBundle">Output deserialized gain map bundle.</param>
        /// <param name="inputBuffer">Source binary buffer.</param>
        /// <param name="bytesRead">Output number of bytes read.</param>
        /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
        public static bool JxlGainMapReadBundle(
            out JxlGainMapBundle mapBundle,
            ReadOnlySpan<byte> inputBuffer,
            out nuint bytesRead)
        {
            mapBundle = new JxlGainMapBundle();
            bytesRead = 0;

            if (inputBuffer.IsEmpty)
            {
                return false;
            }

            ulong cursor     = 0;
            ulong nextCursor = 0;
            ulong bufLen     = (ulong)inputBuffer.Length;

            if (!AdvanceCursor(1, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            mapBundle.JhgmVersion = inputBuffer[(int)cursor];

            if (!AdvanceCursor(2, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            ushort metadataSize = (ushort)ByteOrder.LoadBE16(inputBuffer.Slice((int)cursor, 2));

            if (!AdvanceCursor(metadataSize, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            mapBundle.GainMapMetadata = inputBuffer.Slice((int)cursor, metadataSize).ToArray();

            if (!ReadBundleColorEncoding(inputBuffer, ref cursor, ref nextCursor, bufLen, mapBundle) ||
                !ReadBundleAltIccAndGainMap(inputBuffer, ref cursor, ref nextCursor, bufLen, mapBundle))
            {
                return false;
            }

            bytesRead = (nuint)nextCursor;

            return true;
        }

        private static bool EncodeBundleColorEncoding(
            JxlGainMapBundle mapBundle,
            out ReadOnlySpan<byte> colorEncBytes)
        {
            colorEncBytes                   = ReadOnlySpan<byte>.Empty;
            BitWriter colorEncodingWriter   = new BitWriter();

            if (mapBundle.HasColorEncoding)
            {
                ColorEncoding internalColorEncoding = new ColorEncoding();

                if (!internalColorEncoding.FromExternal(mapBundle.ColorEncoding) ||
                    !Bundle.Write(internalColorEncoding, colorEncodingWriter, LayerType.Header, null))
                {
                    return false;
                }
            }

            colorEncodingWriter.ZeroPadToByte();
            colorEncBytes = colorEncodingWriter.GetSpan();

            return true;
        }

        private static bool ReadBundleColorEncoding(
            ReadOnlySpan<byte> inputBuffer,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong bufLen,
            JxlGainMapBundle mapBundle)
        {
            if (!AdvanceCursor(1, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            byte compressedColorEncodingSize = inputBuffer[(int)cursor];
            mapBundle.HasColorEncoding       = compressedColorEncodingSize > 0;

            if (!mapBundle.HasColorEncoding)
            {
                return true;
            }

            if (!AdvanceCursor(compressedColorEncodingSize, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            ReadOnlySpan<byte> span             = inputBuffer.Slice((int)cursor, compressedColorEncodingSize);
            using BitReader colorEncodingReader = new BitReader(span);
            ColorEncoding internalColorEncoding = new ColorEncoding();

            if (!Bundle.Read(colorEncodingReader, internalColorEncoding) ||
                !colorEncodingReader.Close())
            {
                return false;
            }

            mapBundle.ColorEncoding = internalColorEncoding.ToExternal();

            return true;
        }

        private static bool ReadBundleAltIccAndGainMap(
            ReadOnlySpan<byte> inputBuffer,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong bufLen,
            JxlGainMapBundle mapBundle)
        {
            if (!AdvanceCursor(4, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            uint compressedIccSize = ByteOrder.LoadBE32(inputBuffer.Slice((int)cursor, 4));

            if (!AdvanceCursor(compressedIccSize, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            mapBundle.AltIcc = inputBuffer.Slice((int)cursor, (int)compressedIccSize).ToArray();
            cursor           = nextCursor;

            ulong remaining = bufLen - cursor;

            if (remaining > uint.MaxValue || !AdvanceCursor(remaining, ref cursor, ref nextCursor, bufLen))
            {
                return false;
            }

            mapBundle.GainMap = inputBuffer.Slice((int)cursor, (int)remaining).ToArray();

            return true;
        }

        private static bool AdvanceCursor(
            ulong n,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong limit)
        {
            cursor = nextCursor;

            return Common.SafeAdd(cursor, n, out nextCursor) && nextCursor <= limit;
        }

        private static bool TryWriteByte(
            byte value,
            Span<byte> dst,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong limit)
        {
            if (!AdvanceCursor(1, ref cursor, ref nextCursor, limit))
            {
                return false;
            }

            dst[(int)cursor] = value;

            return true;
        }

        private static bool TryWriteBE16(
            ushort value,
            Span<byte> dst,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong limit)
        {
            if (!AdvanceCursor(2, ref cursor, ref nextCursor, limit))
            {
                return false;
            }

            ByteOrder.StoreBE16(value, dst.Slice((int)cursor, 2));

            return true;
        }

        private static bool TryWriteBE32(
            uint value,
            Span<byte> dst,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong limit)
        {
            if (!AdvanceCursor(4, ref cursor, ref nextCursor, limit))
            {
                return false;
            }

            ByteOrder.StoreBE32(value, dst.Slice((int)cursor, 4));

            return true;
        }

        private static bool TryWriteBytes(
            ReadOnlySpan<byte> src,
            Span<byte> dst,
            ref ulong cursor,
            ref ulong nextCursor,
            ulong limit)
        {
            if (!AdvanceCursor((ulong)src.Length, ref cursor, ref nextCursor, limit))
            {
                return false;
            }

            src.CopyTo(dst.Slice((int)cursor, src.Length));

            return true;
        }
    }

    /// <summary>
    /// Alpha blending utilities for <see cref="PackedPixelFile"/>.
    /// Ported from <c>lib/extras/alpha_blend.h</c> and <c>lib/extras/alpha_blend.cc</c>.
    /// </summary>
    public static class ExtrasAlphaBlend
    {
        /// <summary>
        /// Composites the alpha channel of <paramref name="ppf"/> onto <paramref name="background"/> in-place
        /// and clears <c>ppf.Info.AlphaBits</c>.
        /// Ported from <c>jxl::extras::AlphaBlend</c> in <c>lib/extras/alpha_blend.cc</c>.
        /// </summary>
        /// <param name="ppf">Packed pixel file to alpha-blend.</param>
        /// <param name="background">Background RGB (or grayscale in index 0) values (3 floats in <c>[0, 1]</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status AlphaBlend(PackedPixelFile ppf, ReadOnlySpan<float> background)
        {
            if (ppf == null || ppf.Info.AlphaBits == 0)
            {
                return true;
            }

            if (background.Length < 3)
            {
                return Status.Failure("Background span must contain at least 3 elements");
            }

            ppf.Info.AlphaBits = 0;

            if (ppf.PreviewFrame != null && !AlphaBlendFrame(ppf.PreviewFrame, background))
            {
                return false;
            }

            foreach (PackedFrame frame in ppf.Frames)
            {
                if (!AlphaBlendFrame(frame, background))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status AlphaBlendFrame(PackedFrame? frame, ReadOnlySpan<float> background)
        {
            if (frame == null)
            {
                return true;
            }

            PackedImage im        = frame.Color;
            JxlPixelFormat format = im.Format;

            if (format.NumChannels != 2 && format.NumChannels != 4)
            {
                return true;
            }

            bool isGrayAlpha = format.NumChannels == 2;

            format.NumChannels--;

            StatusOr<PackedImage> blendedOr = PackedImage.Create(im.XSize, im.YSize, format);

            if (!blendedOr.IsOk)
            {
                return blendedOr.Status;
            }

            PackedImage blended = blendedOr.Value;

            BlendImagePixels(im, blended, isGrayAlpha, background);

            StatusOr<PackedImage> copyOr = blended.Copy();

            if (!copyOr.IsOk)
            {
                return copyOr.Status;
            }

            frame.Color = copyOr.Value;

            return true;
        }

        private static void BlendImagePixels(
            PackedImage im,
            PackedImage blended,
            bool isGrayAlpha,
            ReadOnlySpan<float> background)
        {
            float bg0 = background[0];
            float bg1 = background[1];
            float bg2 = background[2];

            for (nuint y = 0; y < im.YSize; ++y)
            {
                for (nuint x = 0; x < im.XSize; ++x)
                {
                    if (isGrayAlpha)
                    {
                        float g    = im.GetPixelValue(y, x, 0);
                        float a    = im.GetPixelValue(y, x, 1);
                        float outG = (g * a) + (bg0 * (1.0f - a));

                        blended.SetPixelValue(y, x, 0, outG);
                    }
                    else
                    {
                        float r     = im.GetPixelValue(y, x, 0);
                        float g     = im.GetPixelValue(y, x, 1);
                        float b     = im.GetPixelValue(y, x, 2);
                        float a     = im.GetPixelValue(y, x, 3);
                        float oneMa = 1.0f - a;

                        blended.SetPixelValue(y, x, 0, (r * a) + (bg0 * oneMa));
                        blended.SetPixelValue(y, x, 1, (g * a) + (bg1 * oneMa));
                        blended.SetPixelValue(y, x, 2, (b * a) + (bg2 * oneMa));
                    }
                }
            }
        }
    }

    /// <summary>
    /// EXIF blob manipulation helpers for <c>JxlSharp.Extras</c>.
    /// Ported from <c>lib/extras/exif.h</c> and <c>lib/extras/exif.cc</c>.
    /// </summary>
    public static class ExtrasExif
    {
        private const ushort KExifOrientationTag = 274;

        /// <summary>
        /// Resets the EXIF orientation tag (tag 274) in <paramref name="exif"/> to 1 (normal) if present.
        /// Ported from <c>jxl::ResetExifOrientation</c> in <c>lib/extras/exif.cc</c>.
        /// </summary>
        /// <param name="exif">Raw TIFF/EXIF byte span.</param>
        public static void ResetExifOrientation(Span<byte> exif)
        {
            if (exif.Length < 12)
            {
                return;
            }

            uint header = ByteOrder.LoadLE32(exif);
            bool bigendian;

            if (header == 0x2A004D4Du)
            {
                bigendian = true;
            }
            else if (header == 0x002A4949u)
            {
                bigendian = false;
            }
            else
            {
                return;
            }

            ulong offset = bigendian
                ? ByteOrder.LoadBE32(exif.Slice(4))
                : ByteOrder.LoadLE32(exif.Slice(4));

            if ((ulong)exif.Length < 12UL + offset + 2UL || offset < 8UL)
            {
                return;
            }

            int pos       = (int)offset;
            ushort nbTags = LoadU16(exif.Slice(pos), bigendian);

            ResetOrientationInTagTable(exif, pos + 2, nbTags, bigendian);
        }

        private static void ResetOrientationInTagTable(
            Span<byte> exif,
            int pos,
            ushort nbTags,
            bool bigendian)
        {
            while (nbTags > 0)
            {
                if (pos + 12 >= exif.Length)
                {
                    return;
                }

                ushort tag = LoadU16(exif.Slice(pos), bigendian);

                pos += 2;

                if (tag == KExifOrientationTag)
                {
                    ushort type = LoadU16(exif.Slice(pos), bigendian);
                    uint count  = bigendian
                        ? ByteOrder.LoadBE32(exif.Slice(pos + 2))
                        : ByteOrder.LoadLE32(exif.Slice(pos + 2));

                    pos += 6;

                    if (type == 3 && count == 1)
                    {
                        if (bigendian)
                        {
                            ByteOrder.StoreBE16(1, exif.Slice(pos));
                        }
                        else
                        {
                            ByteOrder.StoreLE16(1, exif.Slice(pos));
                        }
                    }

                    return;
                }

                pos += 10;
                nbTags--;
            }
        }

        private static ushort LoadU16(ReadOnlySpan<byte> span, bool bigendian)
        {
            return (ushort)(bigendian ? ByteOrder.LoadBE16(span) : ByteOrder.LoadLE16(span));
        }
    }

    /// <summary>
    /// Hybrid Log-Gamma (HLG) OOTF and inverse OOTF transforms.
    /// Ported from <c>lib/extras/hlg.h</c> and <c>lib/extras/hlg.cc</c>.
    /// </summary>
    public static unsafe class ExtrasHlg
    {
        /// <summary>
        /// Returns the system gamma for the HLG OOTF corresponding to <paramref name="peakLuminance"/> and <paramref name="surroundLuminance"/>.
        /// Ported from <c>jxl::GetHlgGamma</c> in <c>lib/extras/hlg.cc</c>.
        /// </summary>
        /// <param name="peakLuminance">Peak display luminance in nits (e.g. 1000).</param>
        /// <param name="surroundLuminance">Surround luminance in nits (default 5).</param>
        /// <returns>HLG system gamma.</returns>
        public static float GetHlgGamma(float peakLuminance, float surroundLuminance = 5.0f)
        {
            return 1.2f *
                   MathF.Pow(1.111f, MathF.Log2(peakLuminance / 1000.0f)) *
                   MathF.Pow(0.98f, MathF.Log2(surroundLuminance / 5.0f));
        }

        /// <summary>
        /// Applies the HLG opto-optical transfer function (OOTF) with exponent <paramref name="gamma"/> to <paramref name="ib"/>,
        /// converting <paramref name="ib"/> to linear Rec. 2020 in-place.
        /// Ported from <c>jxl::HlgOOTF</c> in <c>lib/extras/hlg.cc</c>.
        /// </summary>
        /// <param name="ib">Image bundle to transform.</param>
        /// <param name="gamma">System gamma exponent.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status HlgOOTF(ImageBundle ib, float gamma, ThreadPool? pool = null)
        {
            if (ib == null)
            {
                throw new ArgumentNullException(nameof(ib));
            }

            if (!ExtrasToneMapping.TransformToLinearRec2020(ib, pool))
            {
                return false;
            }

            nuint xsize = ib.XSize();
            nuint ysize = ib.YSize();

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                _ => Status.OkStatus,
                (uint y, nuint _) =>
                {
                    float* rowR = ib.Color().PlaneRow(0, y);
                    float* rowG = ib.Color().PlaneRow(1, y);
                    float* rowB = ib.Color().PlaneRow(2, y);

                    for (nuint x = 0; x < xsize; ++x)
                    {
                        float red       = rowR[x];
                        float green     = rowG[x];
                        float blue      = rowB[x];
                        float luminance = (0.2627f * red) + (0.6780f * green) + (0.0593f * blue);
                        float ratio     = MathF.Pow(luminance, gamma - 1.0f);

                        if (float.IsFinite(ratio))
                        {
                            rowR[x] = red * ratio;
                            rowG[x] = green * ratio;
                            rowB[x] = blue * ratio;
                        }
                    }

                    return true;
                },
                "HlgOOTF");
        }

        /// <summary>
        /// Applies the inverse HLG OOTF (<c>1 / gamma</c>) to <paramref name="ib"/>,
        /// converting <paramref name="ib"/> to linear Rec. 2020 in-place.
        /// Ported from <c>jxl::HlgInverseOOTF</c> in <c>lib/extras/hlg.cc</c>.
        /// </summary>
        /// <param name="ib">Image bundle to transform.</param>
        /// <param name="gamma">System gamma exponent.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status HlgInverseOOTF(ImageBundle ib, float gamma, ThreadPool? pool = null)
        {
            return HlgOOTF(ib, 1.0f / gamma, pool);
        }
    }

    /// <summary>
    /// Image quality and distance metrics (Butteraugli p-norm, YUV L2 distance, PSNR).
    /// Ported from <c>lib/extras/metrics.h</c> and <c>lib/extras/metrics.cc</c>.
    /// </summary>
    public static unsafe class ExtrasMetrics
    {
        /// <summary>
        /// Computes the Butteraugli multi-scale p-norm distance over <paramref name="distmap"/>.
        /// Ported from <c>jxl::ComputeDistanceP</c> in <c>lib/extras/metrics.cc</c>.
        /// </summary>
        /// <param name="distmap">Butteraugli difference map.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="p">Exponent <c>p</c> (typically 3.0).</param>
        /// <returns>Computed p-norm distance.</returns>
        public static StatusOr<double> ComputeDistanceP(
            ImagePlane<float> distmap,
            in ButteraugliParams baParams,
            double p)
        {
            _ = baParams;

            if (distmap == null)
            {
                throw new ArgumentNullException(nameof(distmap));
            }

            if (distmap.XSize == 0 || distmap.YSize == 0)
            {
                return 0.0;
            }

            double onePerPixels = 1.0 / ((double)distmap.YSize * distmap.XSize);
            Span<double> sum1   = stackalloc double[3];
            bool isCubic        = Math.Abs(p - 3.0) < 1E-6;

            sum1.Clear();

            for (nuint y = 0; y < distmap.YSize; ++y)
            {
                float* row = distmap.ConstRow(y);

                for (nuint x = 0; x < distmap.XSize; ++x)
                {
                    double d1 = row[x];
                    double d2 = isCubic ? (d1 * d1 * d1) : Math.Pow(d1, p);

                    sum1[0] += d2;
                    d2      *= d2;
                    sum1[1] += d2;
                    d2      *= d2;
                    sum1[2] += d2;
                }
            }

            double v = 0.0;

            for (int i = 0; i < 3; ++i)
            {
                v += Math.Pow(onePerPixels * sum1[i], 1.0 / (p * (1 << i)));
            }

            return v / 3.0;
        }

        /// <summary>
        /// Computes the squared weighted YUV L2 distance between <paramref name="ib1"/> and <paramref name="ib2"/> in sRGB space.
        /// Ported from <c>jxl::ComputeDistance2</c> in <c>lib/extras/metrics.cc</c>.
        /// </summary>
        /// <param name="ib1">First image bundle.</param>
        /// <param name="ib2">Second image bundle.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <returns>Squared weighted L2 distance.</returns>
        public static double ComputeDistance2(
            ImageBundle ib1,
            ImageBundle ib2,
            JxlCmsInterface cms)
        {
            if (ib1 == null)
            {
                throw new ArgumentNullException(nameof(ib1));
            }

            if (ib2 == null)
            {
                throw new ArgumentNullException(nameof(ib2));
            }

            Span<double> sumOfSquares   = stackalloc double[3];
            ReadOnlySpan<float> weights = stackalloc float[3] { 6.0f / 8.0f, 1.0f / 8.0f, 1.0f / 8.0f };
            double norm                 = 0.0;

            ComputeSumOfSquares(ib1, ib2, cms, sumOfSquares);

            for (int i = 0; i < 3; ++i)
            {
                norm += Math.Sqrt(sumOfSquares[i]) * weights[i];
            }

            return norm * norm;
        }

        /// <summary>
        /// Computes the weighted YUV Peak Signal-to-Noise Ratio (PSNR) between <paramref name="ib1"/> and <paramref name="ib2"/> in sRGB space.
        /// Ported from <c>jxl::ComputePSNR</c> in <c>lib/extras/metrics.cc</c>.
        /// </summary>
        /// <param name="ib1">First image bundle.</param>
        /// <param name="ib2">Second image bundle.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <returns>Weighted average PSNR in dB (up to 99.99 for identical images).</returns>
        public static double ComputePSNR(
            ImageBundle ib1,
            ImageBundle ib2,
            JxlCmsInterface cms)
        {
            if (ib1 == null)
            {
                throw new ArgumentNullException(nameof(ib1));
            }

            if (ib2 == null)
            {
                throw new ArgumentNullException(nameof(ib2));
            }

            if (ib1.XSize() != ib2.XSize() || ib1.YSize() != ib2.YSize())
            {
                return 0.0;
            }

            Span<double> sumOfSquares    = stackalloc double[3];
            ReadOnlySpan<double> weights = stackalloc double[3] { 6.0 / 8.0, 1.0 / 8.0, 1.0 / 8.0 };
            double avgPsnr               = 0.0;
            double inputPixels           = (double)ib1.XSize() * ib1.YSize();

            ComputeSumOfSquares(ib1, ib2, cms, sumOfSquares);

            for (int i = 0; i < 3; ++i)
            {
                double rmse = Math.Sqrt(sumOfSquares[i] / inputPixels);
                double psnr = sumOfSquares[i] == 0.0 ? 99.99 : (20.0 * Math.Log10(1.0 / rmse));
                avgPsnr    += weights[i] * psnr;
            }

            return avgPsnr;
        }

        private static void ComputeSumOfSquares(
            ImageBundle ib1,
            ImageBundle ib2,
            JxlCmsInterface cms,
            Span<double> sumOfSquares)
        {
            sumOfSquares[0] = double.MaxValue;
            sumOfSquares[1] = double.MaxValue;
            sumOfSquares[2] = double.MaxValue;

            Image3<float>? copy1 = null;
            Image3<float>? copy2 = null;

            try
            {
                if (!GetSrgbImage(ib1, cms, out Image3<float> srgb1, out copy1) ||
                    !GetSrgbImage(ib2, cms, out Image3<float> srgb2, out copy2) ||
                    !ImageOps.SameSize(srgb1, srgb2))
                {
                    return;
                }

                AccumulateYuvSumOfSquares(srgb1, srgb2, sumOfSquares);
            }
            finally
            {
                copy1?.Dispose();
                copy2?.Dispose();
            }
        }

        private static bool GetSrgbImage(
            ImageBundle ib,
            JxlCmsInterface cms,
            out Image3<float> srgb,
            out Image3<float>? ownedCopy)
        {
            if (ib.IsSRGB())
            {
                srgb      = ib.Color();
                ownedCopy = null;

                return true;
            }

            Rect rect         = new Rect(ib.Color());
            Image3<float> copy = new Image3<float>();

            if (!ib.CopyTo(rect, ColorEncoding.SRGB(ib.IsGray()), cms, ref copy))
            {
                copy.Dispose();
                srgb      = ib.Color();
                ownedCopy = null;

                return false;
            }

            srgb      = copy;
            ownedCopy = copy;

            return true;
        }

        private static void AccumulateYuvSumOfSquares(
            Image3<float> srgb1,
            Image3<float> srgb2,
            Span<double> sumOfSquares)
        {
            sumOfSquares[0] = 0.0;
            sumOfSquares[1] = 0.0;
            sumOfSquares[2] = 0.0;

            for (nuint y = 0; y < srgb1.YSize; ++y)
            {
                float* r1 = srgb1.ConstPlaneRow(0, y);
                float* g1 = srgb1.ConstPlaneRow(1, y);
                float* b1 = srgb1.ConstPlaneRow(2, y);
                float* r2 = srgb2.ConstPlaneRow(0, y);
                float* g2 = srgb2.ConstPlaneRow(1, y);
                float* b2 = srgb2.ConstPlaneRow(2, y);

                for (nuint x = 0; x < srgb1.XSize; ++x)
                {
                    float dr   = r1[x] - r2[x];
                    float dg   = g1[x] - g2[x];
                    float db   = b1[x] - b2[x];
                    float yuv0 = (0.299f * dr) + (0.587f * dg) + (0.114f * db);
                    float yuv1 = (-0.14713f * dr) + (-0.28886f * dg) + (0.436f * db);
                    float yuv2 = (0.615f * dr) + (-0.51499f * dg) + (-0.10001f * db);

                    sumOfSquares[0] += (double)yuv0 * yuv0;
                    sumOfSquares[1] += (double)yuv1 * yuv1;
                    sumOfSquares[2] += (double)yuv2 * yuv2;
                }
            }
        }
    }

    /// <summary>
    /// ITU-R BT.2408 / BT.2390 tone mapping and gamut mapping over <see cref="CodecInOut"/>.
    /// Ported from <c>lib/extras/tone_mapping.h</c> and <c>lib/extras/tone_mapping.cc</c>.
    /// </summary>
    public static unsafe class ExtrasToneMapping
    {
        private static readonly JxlSharp.Base.Vector3 Rec2020Luminances = new JxlSharp.Base.Vector3(0.2627f, 0.6780f, 0.0593f);

        /// <summary>
        /// Represents a minimum and maximum luminance range in nits (<c>cd/m^2</c>).
        /// </summary>
        /// <param name="Min">Minimum luminance in nits.</param>
        /// <param name="Max">Maximum luminance in nits.</param>
        public readonly record struct LuminanceRange(float Min, float Max);

        /// <summary>
        /// Tone-maps all frames in <paramref name="io"/> to the target <paramref name="displayNits"/> range
        /// in linear Rec. 2020 space and updates <c>io.Metadata.M.IntensityTarget</c>.
        /// Ported from <c>jxl::ToneMapTo</c> in <c>lib/extras/tone_mapping.cc</c>.
        /// </summary>
        /// <param name="displayNits">Target display luminance range in nits.</param>
        /// <param name="io">Codec container whose frames will be tone-mapped in-place.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ToneMapTo(
            LuminanceRange displayNits,
            CodecInOut io,
            ThreadPool? pool = null)
        {
            if (io == null)
            {
                throw new ArgumentNullException(nameof(io));
            }

            foreach (ImageBundle ib in io.Frames)
            {
                if (!ToneMapFrame(displayNits, ib, pool))
                {
                    return false;
                }
            }

            io.Metadata.M.SetIntensityTarget(displayNits.Max);

            return true;
        }

        /// <summary>
        /// Gamut-maps all frames in <paramref name="io"/> in linear Rec. 2020 space.
        /// Ported from <c>jxl::GamutMap</c> in <c>lib/extras/tone_mapping.cc</c>.
        /// </summary>
        /// <param name="io">Codec container whose frames will be gamut-mapped in-place.</param>
        /// <param name="preserveSaturation">Balance between saturation preservation and luminance preservation.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status GamutMap(
            CodecInOut io,
            float preserveSaturation,
            ThreadPool? pool = null)
        {
            if (io == null)
            {
                throw new ArgumentNullException(nameof(io));
            }

            foreach (ImageBundle ib in io.Frames)
            {
                if (!GamutMapFrame(ib, preserveSaturation, pool))
                {
                    return false;
                }
            }

            return true;
        }

        internal static Status TransformToLinearRec2020(ImageBundle ib, ThreadPool? pool)
        {
            ColorEncoding linearRec2020 = new ColorEncoding();

            linearRec2020.SetColorSpace(ColorSpace.RGB);

            if (!linearRec2020.SetPrimariesType(Primaries.P2100) ||
                !linearRec2020.SetWhitePointType(WhitePoint.D65))
            {
                return false;
            }

            linearRec2020.Tf.SetTransferFunction(TransferFunction.Linear);

            if (!linearRec2020.CreateICC())
            {
                return false;
            }

            return ib.TransformTo(linearRec2020, JxlCms.GetDefaultCms(), pool);
        }

        private static Status ToneMapFrame(
            LuminanceRange displayNits,
            ImageBundle ib,
            ThreadPool? pool)
        {
            if (!TransformToLinearRec2020(ib, pool))
            {
                return false;
            }

            ImageMetadata metadata       = ib.Metadata ?? new ImageMetadata();
            Rec2408ToneMapper toneMapper = new Rec2408ToneMapper(
                metadata.ToneMapping.MinNits,
                metadata.IntensityTarget(),
                displayNits.Min,
                displayNits.Max,
                Rec2020Luminances);
            nuint xsize = ib.XSize();
            nuint ysize = ib.YSize();

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                _ => Status.OkStatus,
                (uint y, nuint _) =>
                {
                    float* rowR = ib.Color().PlaneRow(0, y);
                    float* rowG = ib.Color().PlaneRow(1, y);
                    float* rowB = ib.Color().PlaneRow(2, y);

                    for (nuint x = 0; x < xsize; ++x)
                    {
                        JxlSharp.Base.Vector3 rgb = new JxlSharp.Base.Vector3(rowR[x], rowG[x], rowB[x]);

                        toneMapper.ToneMap(ref rgb);
                        rowR[x] = rgb[0];
                        rowG[x] = rgb[1];
                        rowB[x] = rgb[2];
                    }

                    return true;
                },
                "ToneMap");
        }

        private static Status GamutMapFrame(
            ImageBundle ib,
            float preserveSaturation,
            ThreadPool? pool)
        {
            if (!TransformToLinearRec2020(ib, pool))
            {
                return false;
            }

            nuint xsize = ib.XSize();
            nuint ysize = ib.YSize();

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)ysize,
                _ => Status.OkStatus,
                (uint y, nuint _) =>
                {
                    float* rowR = ib.Color().PlaneRow(0, y);
                    float* rowG = ib.Color().PlaneRow(1, y);
                    float* rowB = ib.Color().PlaneRow(2, y);

                    for (nuint x = 0; x < xsize; ++x)
                    {
                        JxlSharp.Base.Vector3 rgb = new JxlSharp.Base.Vector3(rowR[x], rowG[x], rowB[x]);

                        JxlSharp.Cms.ToneMapping.GamutMapScalar(ref rgb, Rec2020Luminances, preserveSaturation);
                        rowR[x] = rgb[0];
                        rowG[x] = rgb[1];
                        rowB[x] = rgb[2];
                    }

                    return true;
                },
                "GamutMap");
        }
    }
}
