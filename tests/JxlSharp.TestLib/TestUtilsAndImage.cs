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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Extras;

namespace JxlSharp.TestLib
{
    /// <summary>
    /// Provides the default <see cref="JxlMemoryManager"/> instance for unit tests.
    /// Ported from <c>lib/jxl/test_memory_manager.h</c> and <c>lib/jxl/test_memory_manager.cc</c>.
    /// </summary>
    public static class TestMemoryManager
    {
        /// <summary>
        /// Returns the shared test <see cref="JxlMemoryManager"/> instance.
        /// Ported from <c>jxl::test::MemoryManager()</c>.
        /// </summary>
        /// <returns>The default <see cref="JxlMemoryManager"/>.</returns>
        public static JxlMemoryManager MemoryManager()
        {
            return JxlMemoryManager.Default;
        }
    }

    /// <summary>
    /// Descriptor of a parametric color encoding used for parameterized color space tests.
    /// Ported from <c>jxl::test::ColorEncodingDescriptor</c> in <c>lib/jxl/test_utils.h</c>.
    /// </summary>
    /// <param name="ColorSpace">Color space.</param>
    /// <param name="WhitePoint">White point preset.</param>
    /// <param name="Primaries">Color primaries preset.</param>
    /// <param name="Tf">Transfer function preset.</param>
    /// <param name="RenderingIntent">Rendering intent.</param>
    public readonly record struct ColorEncodingDescriptor(
        ColorSpace ColorSpace,
        WhitePoint WhitePoint,
        Primaries Primaries,
        TransferFunction Tf,
        RenderingIntent RenderingIntent)
    {
        /// <inheritdoc />
        public override string ToString()
        {
            return $"{ColorSpace}, {WhitePoint}, {Primaries}, {Tf}, {RenderingIntent}";
        }
    }

    /// <summary>
    /// Shared test utilities for loading test data, comparing pixels, constructing color encodings,
    /// and running encode/decode roundtrips.
    /// Ported from <c>lib/jxl/test_utils.h</c> and <c>lib/jxl/test_utils.cc</c>.
    /// </summary>
    public static class TestUtils
    {
        private static readonly ColorSpace[] AllColorSpaces =
        {
            ColorSpace.RGB,
            ColorSpace.Gray,
            ColorSpace.XYB,
            ColorSpace.Unknown,
        };

        private static readonly WhitePoint[] AllWhitePoints =
        {
            WhitePoint.D65,
            WhitePoint.Custom,
            WhitePoint.E,
            WhitePoint.DCI,
        };

        private static readonly Primaries[] AllPrimaries =
        {
            Primaries.SRGB,
            Primaries.Custom,
            Primaries.P2100,
            Primaries.P3,
        };

        private static readonly TransferFunction[] AllTransferFunctions =
        {
            TransferFunction.Tf709,
            TransferFunction.Unknown,
            TransferFunction.Linear,
            TransferFunction.SRGB,
            TransferFunction.PQ,
            TransferFunction.DCI,
            TransferFunction.HLG,
        };

        private static readonly RenderingIntent[] AllRenderingIntents =
        {
            RenderingIntent.Perceptual,
            RenderingIntent.Relative,
            RenderingIntent.Saturation,
            RenderingIntent.Absolute,
        };

        /// <summary>
        /// Asserts that <paramref name="ok"/> is true, throwing an <see cref="InvalidOperationException"/> on failure.
        /// Ported from <c>Check(OK)</c> in <c>lib/jxl/test_utils.h</c>.
        /// </summary>
        /// <param name="ok">Condition to verify.</param>
        /// <param name="condition">Caller expression string.</param>
        /// <param name="file">Caller source file path.</param>
        /// <param name="line">Caller source line number.</param>
        public static void Check(
            bool ok,
            [CallerArgumentExpression(nameof(ok))] string? condition = null,
            [CallerFilePath] string? file = null,
            [CallerLineNumber] int line = 0)
        {
            if (!ok)
            {
                throw new InvalidOperationException(
                    $"Check({condition}) failed at {file}:{line} (LastFailure: {Status.LastFailureMessage})");
            }
        }

        /// <summary>
        /// Converts a boolean value to its lowercase C-string representation (<c>"true"</c> or <c>"false"</c>).
        /// Ported from <c>jxl::test::BoolToCStr</c> in <c>lib/jxl/test_utils.h</c>.
        /// </summary>
        /// <param name="b">Boolean value.</param>
        /// <returns><c>"true"</c> if <paramref name="b"/> is true; otherwise <c>"false"</c>.</returns>
        public static string BoolToCStr(bool b)
        {
            return b ? "true" : "false";
        }

        /// <summary>
        /// Resolves the full filesystem path to a relative file inside <c>libjxl/testdata</c>.
        /// Ported from <c>jxl::test::GetTestDataPath</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="filename">Relative path under <c>testdata</c>.</param>
        /// <returns>Resolved absolute path to the test data file.</returns>
        public static string GetTestDataPath(string filename)
        {
            string normalized = filename.Replace('/', Path.DirectorySeparatorChar);
            string root       = ResolveTestDataRoot();

            return Path.Combine(root, normalized);
        }

        /// <summary>
        /// Reads all bytes of a file inside <c>libjxl/testdata</c>.
        /// Ported from <c>jxl::test::ReadTestData</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="filename">Relative path under <c>testdata</c>.</param>
        /// <returns>Contents of the file as a byte array.</returns>
        public static byte[] ReadTestData(string filename)
        {
            string fullPath = GetTestDataPath(filename);

            Check(File.Exists(fullPath));

            return File.ReadAllBytes(fullPath);
        }

        /// <summary>
        /// Returns the raw uncompressed <c>scRGB-v2.icc</c> test profile from <c>testdata</c>.
        /// Ported from <c>jxl::test::GetIccTestProfile</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <returns>Raw ICC profile bytes (372 bytes).</returns>
        public static byte[] GetIccTestProfile()
        {
            return ReadTestData("external/Compact-ICC-Profiles/profiles/scRGB-v2.icc");
        }

        /// <summary>
        /// Returns the JPEG XL compressed <c>scRGB-v2.icc</c> test profile (136 bytes).
        /// Ported from <c>jxl::test::GetCompressedIccTestProfile</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <returns>Compressed ICC profile bytes (136 bytes).</returns>
        public static byte[] GetCompressedIccTestProfile()
        {
            BitWriter writer = new BitWriter(TestMemoryManager.MemoryManager());
            byte[] icc       = GetIccTestProfile();
            Status status    = WriteICC(icc, writer, LayerType.Header, null);

            Check((bool)status);
            writer.ZeroPadToByte();

            return writer.GetSpan().ToArray();
        }

        /// <summary>
        /// Compresses <paramref name="icc"/> into <paramref name="writer"/> using <see cref="IccCodec.PredictICC"/>,
        /// <see cref="IccCodecCommon.ICCANSContext"/>, <see cref="HistogramParams.LZ77Method.kOptc256"/>, and forced Huffman coding.
        /// Ported from <c>jxl::WriteICC</c> in <c>lib/jxl/enc_icc_codec.cc</c>.
        /// </summary>
        /// <param name="icc">Input ICC profile bytes.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer type.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteICC(
            ReadOnlySpan<byte> icc,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut = null)
        {
            if (icc.IsEmpty)
            {
                return Status.Failure("ICC must be non-empty");
            }

            JxlMemoryManager? mm = writer.MemoryManager;
            PaddedBytes enc      = new PaddedBytes(mm);

            if (!IccCodec.PredictICC(icc, enc))
            {
                return false;
            }

            if (!writer.WithMaxBits(128, layer, auxOut, () => U64Coder.Write((ulong)enc.Size, writer)))
            {
                return false;
            }

            List<List<Token>> tokens   = BuildIccTokens(enc.Span);
            HistogramParams histParams = new HistogramParams
            {
                Lz77Method   = enc.Size < 16384
                    ? HistogramParams.LZ77Method.kOptc256
                    : HistogramParams.LZ77Method.kLZ77b3w3f,
                ForceHuffman = true,
            };

            EntropyEncodingData code = new EntropyEncodingData();
            StatusOr<nuint> costOr   = EncAns.BuildAndEncodeHistograms(
                mm,
                histParams,
                IccCodecCommon.KNumIccContexts,
                tokens,
                code,
                writer,
                (layer, auxOut));

            if (!costOr.IsOk)
            {
                return costOr.Status;
            }

            return EncAns.WriteTokens(tokens[0], code, 0, writer, layer, auxOut);
        }

        /// <summary>
        /// Decompresses an ICC profile from <paramref name="reader"/> into <paramref name="icc"/>.
        /// Ported from <c>jxl::test::ReadICC</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="icc">Receives the decompressed ICC profile bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadICC(BitReader reader, out byte[] icc)
        {
            icc                         = Array.Empty<byte>();
            JxlMemoryManager mm         = TestMemoryManager.MemoryManager();
            ICCReader iccReader         = new ICCReader(mm);
            PaddedBytes iccBuffer       = new PaddedBytes(mm);

            if (!iccReader.Init(reader) || !iccReader.Process(reader, iccBuffer))
            {
                return false;
            }

            icc = iccBuffer.Span.ToArray();

            return true;
        }

        /// <summary>
        /// Initializes <paramref name="basicInfo"/> fields from <paramref name="pixelFormat"/>.
        /// Ported from <c>jxl::test::JxlBasicInfoSetFromPixelFormat</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="basicInfo">Basic info structure to populate.</param>
        /// <param name="pixelFormat">Source pixel format.</param>
        public static void JxlBasicInfoSetFromPixelFormat(
            ref JxlBasicInfo basicInfo,
            in JxlPixelFormat pixelFormat)
        {
            JxlEncoder.JxlEncoderInitBasicInfo(out basicInfo);
            SetSampleBitsFromDataType(ref basicInfo, pixelFormat.DataType);

            basicInfo.NumColorChannels = pixelFormat.NumChannels < 3 ? 1u : 3u;

            if (pixelFormat.NumChannels == 2 || pixelFormat.NumChannels == 4)
            {
                basicInfo.AlphaExponentBits = basicInfo.ExponentBitsPerSample;
                basicInfo.AlphaBits         = basicInfo.BitsPerSample;
                basicInfo.NumExtraChannels  = 1;
            }
            else
            {
                basicInfo.AlphaExponentBits = 0;
                basicInfo.AlphaBits         = 0;
            }
        }

        /// <summary>
        /// Populates <paramref name="dparams"/>.<see cref="JXLDecompressParams.AcceptedFormats"/> with
        /// 1..4 channel little-endian <see cref="JxlDataType.Float"/> formats if currently empty.
        /// Ported from <c>jxl::test::DefaultAcceptedFormats</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="dparams">Decompression parameters to update.</param>
        public static void DefaultAcceptedFormats(JXLDecompressParams dparams)
        {
            if (dparams.AcceptedFormats.Count == 0)
            {
                for (uint numChannels = 1; numChannels <= 4; numChannels++)
                {
                    dparams.AcceptedFormats.Add(new JxlPixelFormat
                    {
                        NumChannels = numChannels,
                        DataType    = JxlDataType.Float,
                        Endianness  = JxlEndianness.LittleEndian,
                        Align       = 0,
                    });
                }
            }
        }

        /// <summary>
        /// Returns all valid RGB <see cref="ColorEncodingDescriptor"/> combinations for parameterized tests.
        /// Ported from <c>jxl::test::AllEncodings</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <returns>List of all valid RGB color encoding descriptors.</returns>
        public static IReadOnlyList<ColorEncodingDescriptor> AllEncodings()
        {
            List<ColorEncodingDescriptor> allEncodings = new List<ColorEncodingDescriptor>(300);

            foreach (ColorSpace cs in AllColorSpaces)
            {
                if (cs == ColorSpace.Unknown || cs == ColorSpace.XYB || cs == ColorSpace.Gray)
                {
                    continue;
                }

                AppendEncodingsForColorSpace(cs, allEncodings);
            }

            return allEncodings;
        }

        /// <summary>
        /// Constructs an internal <see cref="ColorEncoding"/> and its ICC profile from <paramref name="desc"/>.
        /// Ported from <c>jxl::test::ColorEncodingFromDescriptor</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="desc">Color encoding descriptor.</param>
        /// <returns>Constructed <see cref="ColorEncoding"/> with populated ICC profile.</returns>
        public static ColorEncoding ColorEncodingFromDescriptor(ColorEncodingDescriptor desc)
        {
            ColorEncoding c = new ColorEncoding();

            c.SetColorSpace(desc.ColorSpace);

            if (desc.ColorSpace != ColorSpace.XYB)
            {
                Check((bool)c.SetWhitePointType(desc.WhitePoint));

                if (desc.ColorSpace != ColorSpace.Gray)
                {
                    Check((bool)c.SetPrimariesType(desc.Primaries));
                }

                c.Tf.SetTransferFunction(desc.Tf);
            }

            c.SetRenderingIntent(desc.RenderingIntent);
            Check((bool)c.CreateICC());

            return c;
        }

        /// <summary>
        /// Returns the mantissa/integer precision in bits for <paramref name="dataType"/> (8, 16, 24, or 11).
        /// Ported from <c>jxl::test::GetPrecision</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="dataType">Sample data type.</param>
        /// <returns>Precision in bits.</returns>
        public static nuint GetPrecision(JxlDataType dataType)
        {
            switch (dataType)
            {
                case JxlDataType.UInt8:
                    return 8;

                case JxlDataType.UInt16:
                    return 16;

                case JxlDataType.Float:
                    return 24;

                case JxlDataType.Float16:
                    return 11;

                default:
                    Check(false);

                    return 0;
            }
        }

        /// <summary>
        /// Returns the storage size in bits for <paramref name="dataType"/> (8, 16, 32, or 16).
        /// Ported from <c>jxl::test::GetDataBits</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="dataType">Sample data type.</param>
        /// <returns>Storage size in bits.</returns>
        public static nuint GetDataBits(JxlDataType dataType)
        {
            switch (dataType)
            {
                case JxlDataType.UInt8:
                    return 8;

                case JxlDataType.UInt16:
                    return 16;

                case JxlDataType.Float:
                    return 32;

                case JxlDataType.Float16:
                    return 16;

                default:
                    Check(false);

                    return 0;
            }
        }

        /// <summary>
        /// Checks whether <paramref name="value"/> is within <paramref name="maxDist"/> of <paramref name="expected"/>.
        /// Ported from <c>jxl::test::Near</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="expected">Expected value.</param>
        /// <param name="value">Actual value.</param>
        /// <param name="maxDist">Maximum allowed absolute difference.</param>
        /// <returns><c>true</c> if <c>|expected - value| &lt;= maxDist</c>; otherwise <c>false</c>.</returns>
        public static bool Near(double expected, double value, double maxDist)
        {
            double dist = expected > value ? expected - value : value - expected;

            return dist <= maxDist;
        }

        /// <summary>
        /// Converts a packed image buffer of arbitrary <see cref="JxlPixelFormat"/> to a 4-channel
        /// double-precision RGBA buffer in range <c>[0, 1]</c>.
        /// Ported from <c>jxl::test::ConvertToRGBA32</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="pixels">Input packed pixel buffer.</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="format">Pixel storage format of <paramref name="pixels"/>.</param>
        /// <param name="factor">Optional custom scale factor for integer types (0.0 uses <c>1 / max</c>).</param>
        /// <returns>Interleaved RGBA double array of length <c>xsize * ysize * 4</c>.</returns>
        public static double[] ConvertToRGBA32(
            ReadOnlySpan<byte> pixels,
            nuint xsize,
            nuint ysize,
            in JxlPixelFormat format,
            double factor = 0.0)
        {
            double[] result          = new double[(int)(xsize * ysize * 4)];
            nuint numChannels        = format.NumChannels;
            JxlEndianness endianness = ResolveEndianness(format.Endianness);
            nuint stride             = xsize * Common.DivCeil(GetDataBits(format.DataType) * numChannels, Common.KBitsPerByte);

            Check(Common.SafeRoundUpTo(stride, format.Align, out stride));

            if (format.DataType == JxlDataType.UInt8)
            {
                ConvertUInt8ToRgba(pixels, xsize, ysize, stride, numChannels, factor, result);
            }
            else if (format.DataType == JxlDataType.UInt16)
            {
                ConvertUInt16ToRgba(pixels, (xsize, ysize, stride), numChannels, endianness, factor, result);
            }
            else if (format.DataType == JxlDataType.Float)
            {
                ConvertFloat32ToRgba(pixels, (xsize, ysize, stride), numChannels, endianness, result);
            }
            else if (format.DataType == JxlDataType.Float16)
            {
                ConvertFloat16ToRgba(pixels, (xsize, ysize, stride), numChannels, endianness, result);
            }
            else
            {
                Check(false);
            }

            return result;
        }

        /// <summary>
        /// Compares two packed pixel buffers and returns the number of differing pixels.
        /// Ported from <c>jxl::test::ComparePixels</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First pixel buffer.</param>
        /// <param name="b">Second pixel buffer.</param>
        /// <param name="dims">Image width and height in pixels.</param>
        /// <param name="formatA">Pixel format of <paramref name="a"/>.</param>
        /// <param name="formatB">Pixel format of <paramref name="b"/>.</param>
        /// <param name="thresholdMultiplier">Multiplier applied to the bit-depth precision threshold.</param>
        /// <returns>Number of pixels exceeding the comparison threshold.</returns>
        public static nuint ComparePixels(
            ReadOnlySpan<byte> a,
            ReadOnlySpan<byte> b,
            (nuint XSize, nuint YSize) dims,
            in JxlPixelFormat formatA,
            in JxlPixelFormat formatB,
            double thresholdMultiplier = 1.0)
        {
            double[] aFull  = ConvertToRGBA32(a, dims.XSize, dims.YSize, in formatA);
            double[] bFull  = ConvertToRGBA32(b, dims.XSize, dims.YSize, in formatB);
            bool grayA      = formatA.NumChannels < 3;
            bool grayB      = formatB.NumChannels < 3;
            bool alphaA     = (formatA.NumChannels & 1) == 0;
            bool alphaB     = (formatB.NumChannels & 1) == 0;
            double prec     = ComputeComparisonPrecision(in formatA, in formatB, thresholdMultiplier);
            nuint numDiff   = 0;
            nuint totalPx   = dims.XSize * dims.YSize;

            for (nuint px = 0; px < totalPx; px++)
            {
                int i = (int)(px * 4);

                if (!IsPixelMatch(aFull, bFull, i, (grayA, grayB, alphaA, alphaB), prec))
                {
                    numDiff++;
                }
            }

            return numDiff;
        }

        /// <summary>
        /// Computes the Root-Mean-Square (RMS) distance between two packed pixel buffers of identical format.
        /// Ported from <c>jxl::test::DistanceRMS</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First pixel buffer.</param>
        /// <param name="b">Second pixel buffer.</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="format">Pixel storage format.</param>
        /// <returns>RMS distance across all pixels.</returns>
        public static double DistanceRMS(
            ReadOnlySpan<byte> a,
            ReadOnlySpan<byte> b,
            nuint xsize,
            nuint ysize,
            in JxlPixelFormat format)
        {
            double[] aFull = ConvertToRGBA32(a, xsize, ysize, in format);
            double[] bFull = ConvertToRGBA32(b, xsize, ysize, in format);
            double sum     = 0.0;

            for (nuint y = 0; y < ysize; y++)
            {
                double rowSum = 0.0;

                for (nuint x = 0; x < xsize; x++)
                {
                    int i = (int)((y * xsize + x) * 4);

                    for (uint c = 0; c < format.NumChannels; ++c)
                    {
                        double diff = aFull[i + (int)c] - bFull[i + (int)c];
                        rowSum     += diff * diff;
                    }
                }

                sum += rowSum;
            }

            sum /= (double)(xsize * ysize);

            return Math.Sqrt(sum);
        }

        /// <summary>
        /// Checks whether two <see cref="PackedPixelFile"/> instances have byte-identical alpha channels across all frames.
        /// Ported from <c>jxl::test::SameAlpha</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <returns><c>true</c> if all alpha samples are byte-identical; otherwise <c>false</c>.</returns>
        public static bool SameAlpha(PackedPixelFile a, PackedPixelFile b)
        {
            Check(a.Info.XSize == b.Info.XSize);
            Check(a.Info.YSize == b.Info.YSize);
            Check(a.Info.AlphaBits == b.Info.AlphaBits);
            Check(a.Info.AlphaExponentBits == b.Info.AlphaExponentBits);
            Check(a.Info.AlphaBits > 0);
            Check(a.Frames.Count == b.Frames.Count);

            for (int i = 0; i < a.Frames.Count; ++i)
            {
                if (!SameFrameAlpha(a.Frames[i].Color, b.Frames[i].Color, a.Info.XSize, a.Info.YSize))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks whether two <see cref="PackedImage"/> instances have identical dimensions, formats, and pixel bytes.
        /// Ported from <c>jxl::test::SamePixels(const PackedImage&amp;, const PackedImage&amp;)</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed image.</param>
        /// <param name="b">Second packed image.</param>
        /// <returns><c>true</c> if all pixels are byte-identical; otherwise <c>false</c>.</returns>
        public static bool SamePixels(PackedImage a, PackedImage b)
        {
            Check(a.XSize == b.XSize);
            Check(a.YSize == b.YSize);
            Check(a.Format.NumChannels == b.Format.NumChannels);
            Check(a.Format.DataType == b.Format.DataType);
            Check(a.Format.Endianness == b.Format.Endianness);
            Check(a.PixelsSize == b.PixelsSize);

            ReadOnlySpan<byte> pA = a.Pixels();
            ReadOnlySpan<byte> pB = b.Pixels();
            int strideBytes       = (int)a.PixelStride;
            nuint totalPixels     = a.XSize * a.YSize;

            for (nuint px = 0; px < totalPixels; ++px)
            {
                int idx = (int)px * strideBytes;

                if (!pA.Slice(idx, strideBytes).SequenceEqual(pB.Slice(idx, strideBytes)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks whether two <see cref="PackedPixelFile"/> instances have identical pixels across all frames and extra channels.
        /// Ported from <c>jxl::test::SamePixels(const PackedPixelFile&amp;, const PackedPixelFile&amp;)</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <returns><c>true</c> if all color and extra channel pixels match; otherwise <c>false</c>.</returns>
        public static bool SamePixels(PackedPixelFile a, PackedPixelFile b)
        {
            Check(a.Info.XSize == b.Info.XSize);
            Check(a.Info.YSize == b.Info.YSize);
            Check(a.Info.BitsPerSample == b.Info.BitsPerSample);
            Check(a.Info.ExponentBitsPerSample == b.Info.ExponentBitsPerSample);
            Check(a.Frames.Count == b.Frames.Count);

            for (int i = 0; i < a.Frames.Count; ++i)
            {
                PackedFrame frameA = a.Frames[i];
                PackedFrame frameB = b.Frames[i];

                if (!SamePixels(frameA.Color, frameB.Color))
                {
                    return false;
                }

                Check(frameA.ExtraChannels.Count == frameB.ExtraChannels.Count);

                for (int j = 0; j < frameA.ExtraChannels.Count; ++j)
                {
                    if (!SamePixels(frameA.ExtraChannels[j], frameB.ExtraChannels[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns a <see cref="JXLCompressParams"/> configured for lossless Modular encoding.
        /// Ported from <c>jxl::test::CompressParamsForLossless</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <returns>Lossless <see cref="JXLCompressParams"/>.</returns>
        public static JXLCompressParams CompressParamsForLossless()
        {
            JXLCompressParams cparams = new JXLCompressParams();

            cparams.AddOption(JxlEncoderFrameSettingId.Modular, 1);
            cparams.AddOption(JxlEncoderFrameSettingId.ColorTransform, 1);
            cparams.AddOption(JxlEncoderFrameSettingId.ModularPredictor, 6);
            cparams.Distance = 0.0f;

            return cparams;
        }

        /// <summary>
        /// Generates a deterministic 16-bit big-endian synthetic test image buffer with a background gradient,
        /// a circle, and a noise rectangle.
        /// Ported from <c>jxl::test::GetSomeTestImage</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <param name="numChannels">Number of channels (1..4).</param>
        /// <param name="seed">Deterministic PRNG seed.</param>
        /// <returns>Big-endian 16-bit interleaved pixel bytes of length <c>xsize * ysize * numChannels * 2</c>.</returns>
        public static byte[] GetSomeTestImage(nuint xsize, nuint ysize, nuint numChannels, ushort seed)
        {
            Rng generator                                = new Rng(seed);
            ushort[] grad                                = SampleUInt16Gradient(ref generator);
            (nuint cx, nuint cy, nuint cr)               = SampleCircleGeometry(ref generator, xsize, ysize);
            (nuint rx0, nuint ry0, nuint rx1, nuint ry1) = SampleRectGeometry(ref generator, xsize, ysize);

            byte[] pixels = new byte[(int)(xsize * ysize * numChannels * 2)];

            for (nuint y = 0; y < ysize; y++)
            {
                for (nuint x = 0; x < xsize; x++)
                {
                    ComputeUInt16TestPixel(
                        ref generator,
                        (x, y, xsize, ysize, seed),
                        grad,
                        (cx, cy, cr),
                        (rx0, ry0, rx1, ry1),
                        out (ushort R, ushort G, ushort B, ushort A) rgba);

                    int offset = (int)((y * xsize + x) * 2 * numChannels);

                    WriteBigEndianUInt16Pixel(pixels, offset, numChannels, rgba);
                }
            }

            return pixels;
        }

        /// <summary>
        /// Converts a 16-bit big-endian pixel buffer generated by <see cref="GetSomeTestImage"/> into a <see cref="CodecInOut"/>.
        /// Ported from <c>jxl::test::SomeTestImageToCodecInOut</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="buf">16-bit big-endian pixel buffer.</param>
        /// <param name="numChannels">Number of channels (1..4).</param>
        /// <param name="xsize">Width in pixels.</param>
        /// <param name="ysize">Height in pixels.</param>
        /// <returns>Populated <see cref="CodecInOut"/>.</returns>
        public static CodecInOut SomeTestImageToCodecInOut(
            ReadOnlySpan<byte> buf,
            nuint numChannels,
            nuint xsize,
            nuint ysize)
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io       = new CodecInOut(mm);

            Check((bool)io.SetSize((uint)xsize, (uint)ysize));
            io.Metadata.M.SetUintSamples(16);
            io.Metadata.M.SetAlphaBits(16);
            io.Metadata.M.ColorEncoding = ColorEncoding.SRGB(isGray: numChannels == 1 || numChannels == 2);

            JxlPixelFormat format = new JxlPixelFormat
            {
                NumChannels = (uint)numChannels,
                DataType    = JxlDataType.UInt16,
                Endianness  = JxlEndianness.BigEndian,
                Align       = 0,
            };

            Check((bool)EncExternalImage.BufferToImageBundle(format, xsize, ysize, buf, null, io.Main));

            if (numChannels == 1 || numChannels == 3)
            {
                StatusOr<ImagePlane<float>> alphaOr = ImagePlane<float>.Create(mm, xsize, ysize);

                Check(alphaOr.IsOk);

                ImagePlane<float> alpha = alphaOr.Value;

                ImageOps.FillImage(1.0f, alpha);
                Check((bool)io.Main.SetAlpha(alpha));
            }

            return io;
        }

        /// <summary>
        /// Decodes a JPEG XL file into <paramref name="io"/> using <see cref="CodecJxl.DecodeImageJXL"/> and
        /// <see cref="PackedImageConvert.ConvertPackedPixelFileToCodecInOut"/>.
        /// Ported from <c>jxl::test::DecodeFile</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="dparams">Decompression parameters.</param>
        /// <param name="file">Input JPEG XL bitstream bytes.</param>
        /// <param name="io">Destination <see cref="CodecInOut"/>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeFile(
            JXLDecompressParams dparams,
            ReadOnlySpan<byte> file,
            CodecInOut io,
            ThreadPool? pool = null)
        {
            JXLDecompressParams localDparams = CloneDecompressParams(dparams);

            DefaultAcceptedFormats(localDparams);
            ApplyPoolRunner(localDparams, pool);

            PackedPixelFile ppf = new PackedPixelFile();

            if (!CodecJxl.DecodeImageJXL(file, localDparams, out _, ppf))
            {
                return Status.Failure("DecodeImageJXL failed");
            }

            return PackedImageConvert.ConvertPackedPixelFileToCodecInOut(ppf, pool, io);
        }

        /// <summary>
        /// Encodes <paramref name="io"/> to a raw JPEG XL codestream using internal encoder primitives.
        /// Ported from <c>jxl::test::EncodeFile</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="cparamsOrig">Compression parameters.</param>
        /// <param name="io">Source <see cref="CodecInOut"/>.</param>
        /// <param name="compressed">Receives the encoded JPEG XL codestream bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status EncodeFile(
            CompressParams cparamsOrig,
            CodecInOut io,
            out byte[] compressed,
            ThreadPool? pool = null)
        {
            compressed           = Array.Empty<byte>();
            JxlMemoryManager mm  = TestMemoryManager.MemoryManager();
            JxlCmsInterface cms  = JxlCms.GetDefaultCms();

            if (!io.CheckMetadata())
            {
                return false;
            }

            BitWriter writer       = new BitWriter(mm);
            CompressParams cparams = cparamsOrig.Clone();

            if (io.Main.ColorTransform != ColorTransform.None)
            {
                cparams.ColorTransform = io.Main.ColorTransform;
            }

            if (!PrepareAndWriteCodestreamHeaders(cparams, io, cms, writer, out CodecMetadata metadata))
            {
                return false;
            }

            if (!EncodePreviewAndFrames(mm, cparams, metadata, io, cms, pool, writer))
            {
                return false;
            }

            compressed = writer.GetSpan().ToArray();

            return true;
        }

        /// <summary>
        /// Encodes <paramref name="io"/> with <see cref="EncodeFile"/> and decodes the resulting codestream into <paramref name="io2"/> with <see cref="DecodeFile"/>.
        /// Ported from <c>jxl::test::Roundtrip(CodecInOut*, const CompressParams&amp;, const JXLDecompressParams&amp;, CodecInOut*, ...)</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="io">Input <see cref="CodecInOut"/>.</param>
        /// <param name="cparams">Internal compression parameters.</param>
        /// <param name="dparams">Decompression parameters.</param>
        /// <param name="io2">Output decoded <see cref="CodecInOut"/>.</param>
        /// <param name="compressedSize">Receives the size of the compressed codestream in bytes.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><c>true</c> if roundtrip succeeded and frame counts matched; otherwise <c>false</c>.</returns>
        public static bool Roundtrip(
            CodecInOut io,
            CompressParams cparams,
            JXLDecompressParams dparams,
            CodecInOut io2,
            out nuint compressedSize,
            ThreadPool? pool = null)
        {
            compressedSize                   = 0;
            JXLDecompressParams localDparams = CloneDecompressParams(dparams);

            DefaultAcceptedFormats(localDparams);
            Check((bool)EncodeFile(cparams, io, out byte[] compressed, pool));
            Check((bool)DecodeFile(localDparams, compressed, io2, pool));
            Check(io2.Frames.Count == io.Frames.Count);

            compressedSize = (nuint)compressed.Length;

            return true;
        }

        /// <summary>
        /// Encodes <paramref name="ppfIn"/> with <see cref="CodecJxl.EncodeImageJXL"/> and decodes the result
        /// into <paramref name="ppfOut"/> with <see cref="CodecJxl.DecodeImageJXL"/>.
        /// Ported from <c>jxl::test::Roundtrip</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="ppfIn">Input packed pixel file.</param>
        /// <param name="cparams">Compression parameters.</param>
        /// <param name="dparams">Decompression parameters.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="ppfOut">Output decoded packed pixel file.</param>
        /// <returns>Size of the compressed JPEG XL bitstream in bytes.</returns>
        public static nuint Roundtrip(
            PackedPixelFile ppfIn,
            JXLCompressParams cparams,
            JXLDecompressParams dparams,
            ThreadPool? pool,
            PackedPixelFile ppfOut)
        {
            nuint compressedSize = Roundtrip(ppfIn, cparams, dparams, pool, ppfOut, out nuint decodedSize);

            Check(decodedSize == compressedSize);

            return compressedSize;
        }

        /// <summary>
        /// Encodes <paramref name="ppfIn"/> with <see cref="CodecJxl.EncodeImageJXL"/> and decodes the result
        /// into <paramref name="ppfOut"/>, outputting the number of decoded bytes.
        /// Ported from <c>jxl::test::Roundtrip</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="ppfIn">Input packed pixel file.</param>
        /// <param name="cparams">Compression parameters.</param>
        /// <param name="dparams">Decompression parameters.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="ppfOut">Output decoded packed pixel file.</param>
        /// <param name="decodedSize">Receives the number of bytes consumed by the decoder.</param>
        /// <returns>Size of the compressed JPEG XL bitstream in bytes.</returns>
        public static nuint Roundtrip(
            PackedPixelFile ppfIn,
            JXLCompressParams cparams,
            JXLDecompressParams dparams,
            ThreadPool? pool,
            PackedPixelFile ppfOut,
            out nuint decodedSize)
        {
            JXLDecompressParams localDparams = CloneDecompressParams(dparams);

            DefaultAcceptedFormats(localDparams);
            ApplyPoolRunner(cparams, pool);
            ApplyPoolRunner(localDparams, pool);

            Check(CodecJxl.EncodeImageJXL(cparams, ppfIn, null, out byte[] compressed));
            Check(CodecJxl.DecodeImageJXL(compressed, localDparams, out decodedSize, ppfOut));

            return (nuint)compressed.Length;
        }

        /// <summary>
        /// Computes the maximum Butteraugli distance across all frames of <paramref name="a"/> and <paramref name="b"/>.
        /// Ported from <c>jxl::test::ButteraugliDistance</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Butteraugli distance score.</returns>
        public static float ButteraugliDistance(
            PackedPixelFile a,
            PackedPixelFile b,
            ThreadPool? pool = null)
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io0      = new CodecInOut(mm);
            CodecInOut io1      = new CodecInOut(mm);

            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(a, pool, io0));
            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(b, pool, io1));

            ButteraugliParams baParams = new ButteraugliParams();

            return ButteraugliDistance(io0.Frames, io1.Frames, in baParams, JxlCms.GetDefaultCms(), null, pool);
        }

        /// <summary>
        /// Computes the Butteraugli distance between two <see cref="ImageBundle"/> instances.
        /// Ported from <c>jxl::test::ButteraugliDistance</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="rgb0">Reference image bundle.</param>
        /// <param name="rgb1">Distorted image bundle.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="cms">CMS interface.</param>
        /// <param name="distmap">Optional destination difference map plane.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="ignoreAlpha">Whether to ignore alpha channel differences.</param>
        /// <returns>Butteraugli distance score.</returns>
        public static float ButteraugliDistance(
            ImageBundle rgb0,
            ImageBundle rgb1,
            in ButteraugliParams baParams,
            JxlCmsInterface cms,
            ImagePlane<float>? distmap = null,
            ThreadPool? pool = null,
            bool ignoreAlpha = false)
        {
            JxlButteraugliComparator comparator = new JxlButteraugliComparator(in baParams, cms);
            ImagePlane<float>? localDistmap     = distmap;

            Check((bool)EncComparator.ComputeScore(
                rgb0,
                rgb1,
                comparator,
                cms,
                out float distance,
                ref localDistmap,
                (pool, ignoreAlpha)));

            CopyDistmapIfRequested(localDistmap, distmap);

            return distance;
        }

        /// <summary>
        /// Computes the maximum Butteraugli distance across two lists of frame <see cref="ImageBundle"/>s.
        /// Ported from <c>jxl::test::ButteraugliDistance</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="frames0">Reference frames.</param>
        /// <param name="frames1">Distorted frames.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="cms">CMS interface.</param>
        /// <param name="distmap">Optional destination difference map plane.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Maximum Butteraugli score across all frames.</returns>
        public static float ButteraugliDistance(
            IReadOnlyList<ImageBundle> frames0,
            IReadOnlyList<ImageBundle> frames1,
            in ButteraugliParams baParams,
            JxlCmsInterface cms,
            ImagePlane<float>? distmap = null,
            ThreadPool? pool = null)
        {
            JxlButteraugliComparator comparator = new JxlButteraugliComparator(in baParams, cms);

            Check(frames0.Count == frames1.Count);

            float maxDist = 0.0f;

            for (int i = 0; i < frames0.Count; ++i)
            {
                ImagePlane<float>? localDistmap = distmap;

                Check((bool)EncComparator.ComputeScore(
                    frames0[i],
                    frames1[i],
                    comparator,
                    cms,
                    out float frameScore,
                    ref localDistmap,
                    (pool, false)));

                CopyDistmapIfRequested(localDistmap, distmap);
                maxDist = Math.Max(maxDist, frameScore);
            }

            return maxDist;
        }

        /// <summary>
        /// Computes the Butteraugli 3-norm distance between two <see cref="PackedPixelFile"/> instances.
        /// Ported from <c>jxl::test::Butteraugli3Norm</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns>Butteraugli 3-norm distance.</returns>
        public static float Butteraugli3Norm(
            PackedPixelFile a,
            PackedPixelFile b,
            ThreadPool? pool = null)
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io0      = new CodecInOut(mm);
            CodecInOut io1      = new CodecInOut(mm);

            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(a, pool, io0));
            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(b, pool, io1));

            ButteraugliParams baParams     = new ButteraugliParams();
            JxlButteraugliComparator comp  = new JxlButteraugliComparator(in baParams, JxlCms.GetDefaultCms());
            ImagePlane<float>? distmap     = null;

            for (int i = 0; i < io0.Frames.Count; ++i)
            {
                Check((bool)EncComparator.ComputeScore(
                    io0.Frames[i],
                    io1.Frames[i],
                    comp,
                    JxlCms.GetDefaultCms(),
                    out _,
                    ref distmap,
                    (pool, false)));
            }

            Check(distmap != null);

            StatusOr<double> pnormOr = ExtrasMetrics.ComputeDistanceP(distmap!, in baParams, 3.0);

            Check(pnormOr.IsOk);

            return (float)pnormOr.Value;
        }

        /// <summary>
        /// Computes the weighted YUV L2 distance between two <see cref="PackedPixelFile"/> instances.
        /// Ported from <c>jxl::test::ComputeDistance2</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <returns>Squared weighted L2 distance.</returns>
        public static float ComputeDistance2(PackedPixelFile a, PackedPixelFile b)
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io0      = new CodecInOut(mm);
            CodecInOut io1      = new CodecInOut(mm);

            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(a, null, io0));
            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(b, null, io1));

            return (float)ExtrasMetrics.ComputeDistance2(io0.Main, io1.Main, JxlCms.GetDefaultCms());
        }

        /// <summary>
        /// Computes the weighted YUV PSNR between two <see cref="PackedPixelFile"/> instances.
        /// Ported from <c>jxl::test::ComputePSNR</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="a">First packed pixel file.</param>
        /// <param name="b">Second packed pixel file.</param>
        /// <returns>Weighted average PSNR in dB.</returns>
        public static float ComputePSNR(PackedPixelFile a, PackedPixelFile b)
        {
            JxlMemoryManager mm = TestMemoryManager.MemoryManager();
            CodecInOut io0      = new CodecInOut(mm);
            CodecInOut io1      = new CodecInOut(mm);

            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(a, null, io0));
            Check((bool)PackedImageConvert.ConvertPackedPixelFileToCodecInOut(b, null, io1));

            return (float)ExtrasMetrics.ComputePSNR(io0.Main, io1.Main, JxlCms.GetDefaultCms());
        }

        /// <summary>
        /// Extracts a single-channel <see cref="ImagePlane{T}"/> of <see cref="float"/> from the first frame of <paramref name="ppf"/>.
        /// Ported from <c>jxl::test::GetImage</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="ppf">Source 1-channel packed pixel file.</param>
        /// <returns>Converted <see cref="ImagePlane{T}"/> on success.</returns>
        public static StatusOr<ImagePlane<float>> GetImage(PackedPixelFile ppf)
        {
            if (ppf.Frames.Count == 0)
            {
                return Status.Failure("PackedPixelFile has no frames");
            }

            PackedImage image     = ppf.Frames[0].Color;
            JxlPixelFormat format = image.Format;

            if (format.NumChannels != 1)
            {
                return Status.Failure("Expected 1-channel image");
            }

            StatusOr<ImagePlane<float>> grayOr = ImagePlane<float>.Create(
                TestMemoryManager.MemoryManager(),
                image.XSize,
                image.YSize);

            if (!grayOr.IsOk)
            {
                return grayOr.Status;
            }

            ImagePlane<float> gray  = grayOr.Value;
            ExternalBufferSpec spec = new ExternalBufferSpec(
                image.XSize,
                image.YSize,
                0,
                ppf.Info.BitsPerSample,
                format,
                0);

            if (!EncExternalImage.ConvertFromExternal(image.Pixels(), in spec, null, gray))
            {
                gray.Dispose();

                return Status.Failure("ConvertFromExternal failed");
            }

            return gray;
        }

        /// <summary>
        /// Extracts a 3-channel <see cref="Image3{T}"/> of <see cref="float"/> from the first frame of <paramref name="ppf"/>.
        /// Ported from <c>jxl::test::GetColorImage</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="ppf">Source 3-channel packed pixel file.</param>
        /// <returns>Converted <see cref="Image3{T}"/> on success.</returns>
        public static StatusOr<Image3<float>> GetColorImage(PackedPixelFile ppf)
        {
            if (ppf.Frames.Count == 0)
            {
                return Status.Failure("PackedPixelFile has no frames");
            }

            PackedImage image     = ppf.Frames[0].Color;
            JxlPixelFormat format = image.Format;

            if (format.NumChannels != 3)
            {
                return Status.Failure("Expected 3-channel image");
            }

            StatusOr<Image3<float>> colorOr = Image3<float>.Create(
                TestMemoryManager.MemoryManager(),
                image.XSize,
                image.YSize);

            if (!colorOr.IsOk)
            {
                return colorOr.Status;
            }

            Image3<float> color = colorOr.Value;

            for (nuint c = 0; c < format.NumChannels; ++c)
            {
                ExternalBufferSpec spec = new ExternalBufferSpec(
                    image.XSize,
                    image.YSize,
                    0,
                    ppf.Info.BitsPerSample,
                    format,
                    c);

                if (!EncExternalImage.ConvertFromExternal(image.Pixels(), in spec, null, color.Plane(c)))
                {
                    color.Dispose();

                    return Status.Failure("ConvertFromExternal failed");
                }
            }

            return color;
        }

        /// <summary>
        /// Populates <paramref name="io"/> from a parsed <see cref="JPEGData"/> object for lossless JPEG transcoding tests.
        /// Ported from <c>jxl::test::JpegDataToCodecInOut</c> in <c>lib/jxl/test_utils.cc</c>.
        /// </summary>
        /// <param name="data">Parsed JPEG coefficients and markers.</param>
        /// <param name="io">Destination <see cref="CodecInOut"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status JpegDataToCodecInOut(JPEGData data, CodecInOut io)
        {
            JxlMemoryManager mm = io.MemoryManager;

            io.Frames.Clear();
            io.Frames.Add(new ImageBundle(mm, io.Metadata.M));
            io.Main.JpegData = data;

            if (!EncJpegData.SetColorEncodingFromJpegData(data, io.Metadata.M.ColorEncoding) ||
                !EncJpegData.SetChromaSubsamplingFromJpegData(data, io.Main.ChromaSubsampling) ||
                !EncJpegData.SetColorTransformFromJpegData(data, out ColorTransform ct))
            {
                return false;
            }

            io.Main.ColorTransform = ct;
            io.Metadata.M.SetIntensityTarget(255.0f);
            io.Metadata.M.SetUintSamples(8);

            StatusOr<Image3<float>> tmpOr = Image3<float>.Create(mm, (nuint)data.Width, (nuint)data.Height);

            if (!tmpOr.IsOk)
            {
                return tmpOr.Status;
            }

            if (!io.SetFromImage(tmpOr.Value, io.Metadata.M.ColorEncoding))
            {
                return false;
            }

            Luminance.SetIntensityTarget(io.Metadata.M);

            return true;
        }

        private static string ResolveTestDataRoot()
        {
            string? envPath = Environment.GetEnvironmentVariable("TEST_DATA_PATH");

            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string? foundFromBase = ProbeUpwardForTestData(AppContext.BaseDirectory);

            if (foundFromBase != null)
            {
                return foundFromBase;
            }

            string? foundFromCwd = ProbeUpwardForTestData(Environment.CurrentDirectory);

            if (foundFromCwd != null)
            {
                return foundFromCwd;
            }

            return @"p:\source\jxl#\libjxl\testdata";
        }

        private static string? ProbeUpwardForTestData(string? startDir)
        {
            DirectoryInfo? dir = string.IsNullOrEmpty(startDir) ? null : new DirectoryInfo(startDir);

            while (dir != null)
            {
                string candidate1 = Path.Combine(dir.FullName, "libjxl", "testdata");

                if (Directory.Exists(candidate1))
                {
                    return candidate1;
                }

                string candidate2 = Path.Combine(dir.FullName, "testdata");

                if (Directory.Exists(candidate2))
                {
                    return candidate2;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private static List<List<Token>> BuildIccTokens(ReadOnlySpan<byte> enc)
        {
            List<Token> stream       = new List<Token>(enc.Length);
            List<List<Token>> tokens = new List<List<Token>>(1) { stream };

            for (int i = 0; i < enc.Length; i++)
            {
                byte b1  = i > 0 ? enc[i - 1] : (byte)0;
                byte b2  = i > 1 ? enc[i - 2] : (byte)0;
                uint ctx = (uint)IccCodecCommon.ICCANSContext((nuint)i, b1, b2);

                stream.Add(new Token(ctx, enc[i]));
            }

            return tokens;
        }

        private static void SetSampleBitsFromDataType(ref JxlBasicInfo basicInfo, JxlDataType dataType)
        {
            switch (dataType)
            {
                case JxlDataType.Float:
                    basicInfo.BitsPerSample         = 32;
                    basicInfo.ExponentBitsPerSample = 8;
                    break;

                case JxlDataType.Float16:
                    basicInfo.BitsPerSample         = 16;
                    basicInfo.ExponentBitsPerSample = 5;
                    break;

                case JxlDataType.UInt8:
                    basicInfo.BitsPerSample         = 8;
                    basicInfo.ExponentBitsPerSample = 0;
                    break;

                case JxlDataType.UInt16:
                    basicInfo.BitsPerSample         = 16;
                    basicInfo.ExponentBitsPerSample = 0;
                    break;

                default:
                    Check(false);
                    break;
            }
        }

        private static void AppendEncodingsForColorSpace(
            ColorSpace cs,
            List<ColorEncodingDescriptor> allEncodings)
        {
            foreach (WhitePoint wp in AllWhitePoints)
            {
                if (wp == WhitePoint.Custom)
                {
                    continue;
                }

                foreach (Primaries primaries in AllPrimaries)
                {
                    if (primaries == Primaries.Custom)
                    {
                        continue;
                    }

                    AppendEncodingsForPrimaries(cs, wp, primaries, allEncodings);
                }
            }
        }

        private static void AppendEncodingsForPrimaries(
            ColorSpace cs,
            WhitePoint wp,
            Primaries primaries,
            List<ColorEncodingDescriptor> allEncodings)
        {
            foreach (TransferFunction tf in AllTransferFunctions)
            {
                if (tf == TransferFunction.Unknown)
                {
                    continue;
                }

                foreach (RenderingIntent ri in AllRenderingIntents)
                {
                    allEncodings.Add(new ColorEncodingDescriptor(cs, wp, primaries, tf, ri));
                }
            }
        }

        private static JxlEndianness ResolveEndianness(JxlEndianness endianness)
        {
            if (endianness == JxlEndianness.NativeEndian)
            {
                return ByteOrder.IsLittleEndian() ? JxlEndianness.LittleEndian : JxlEndianness.BigEndian;
            }

            return endianness;
        }

        private static void ConvertUInt8ToRgba(
            ReadOnlySpan<byte> pixels,
            nuint xsize,
            nuint ysize,
            nuint stride,
            nuint numChannels,
            double factor,
            double[] result)
        {
            bool gray  = numChannels == 1 || numChannels == 2;
            bool alpha = numChannels == 2 || numChannels == 4;
            double mul = factor > 0.0 ? factor : 1.0 / 255.0;

            for (nuint y = 0; y < ysize; ++y)
            {
                for (nuint x = 0; x < xsize; ++x)
                {
                    int j    = (int)((y * xsize + x) * 4);
                    int i    = (int)(y * stride + x * numChannels);
                    double r = pixels[i];
                    double g = gray ? r : pixels[i + 1];
                    double b = gray ? r : pixels[i + 2];
                    double a = alpha ? pixels[i + (int)numChannels - 1] : 255.0;

                    result[j + 0] = r * mul;
                    result[j + 1] = g * mul;
                    result[j + 2] = b * mul;
                    result[j + 3] = a * mul;
                }
            }
        }

        private static void ConvertUInt16ToRgba(
            ReadOnlySpan<byte> pixels,
            (nuint XSize, nuint YSize, nuint Stride) dims,
            nuint numChannels,
            JxlEndianness endianness,
            double factor,
            double[] result)
        {
            Check(endianness != JxlEndianness.NativeEndian);

            bool gray  = numChannels == 1 || numChannels == 2;
            bool alpha = numChannels == 2 || numChannels == 4;
            bool isBe  = endianness == JxlEndianness.BigEndian;
            double mul = factor > 0.0 ? factor : 1.0 / 65535.0;

            for (nuint y = 0; y < dims.YSize; ++y)
            {
                for (nuint x = 0; x < dims.XSize; ++x)
                {
                    int j    = (int)((y * dims.XSize + x) * 4);
                    int i    = (int)(y * dims.Stride + x * numChannels * 2);
                    double r = ReadU16Sample(pixels, i, isBe);
                    double g = gray ? r : ReadU16Sample(pixels, i + 2, isBe);
                    double b = gray ? r : ReadU16Sample(pixels, i + 4, isBe);
                    double a = alpha ? ReadU16Sample(pixels, i + ((int)numChannels * 2) - 2, isBe) : 65535.0;

                    result[j + 0] = r * mul;
                    result[j + 1] = g * mul;
                    result[j + 2] = b * mul;
                    result[j + 3] = a * mul;
                }
            }
        }

        private static void ConvertFloat32ToRgba(
            ReadOnlySpan<byte> pixels,
            (nuint XSize, nuint YSize, nuint Stride) dims,
            nuint numChannels,
            JxlEndianness endianness,
            double[] result)
        {
            Check(endianness != JxlEndianness.NativeEndian);

            bool gray  = numChannels == 1 || numChannels == 2;
            bool alpha = numChannels == 2 || numChannels == 4;
            bool isBe  = endianness == JxlEndianness.BigEndian;

            for (nuint y = 0; y < dims.YSize; ++y)
            {
                for (nuint x = 0; x < dims.XSize; ++x)
                {
                    int j    = (int)((y * dims.XSize + x) * 4);
                    int i    = (int)(y * dims.Stride + x * numChannels * 4);
                    double r = ReadF32Sample(pixels, i, isBe);
                    double g = gray ? r : ReadF32Sample(pixels, i + 4, isBe);
                    double b = gray ? r : ReadF32Sample(pixels, i + 8, isBe);
                    double a = alpha ? ReadF32Sample(pixels, i + ((int)numChannels * 4) - 4, isBe) : 1.0;

                    result[j + 0] = r;
                    result[j + 1] = g;
                    result[j + 2] = b;
                    result[j + 3] = a;
                }
            }
        }

        private static void ConvertFloat16ToRgba(
            ReadOnlySpan<byte> pixels,
            (nuint XSize, nuint YSize, nuint Stride) dims,
            nuint numChannels,
            JxlEndianness endianness,
            double[] result)
        {
            Check(endianness != JxlEndianness.NativeEndian);

            bool gray  = numChannels == 1 || numChannels == 2;
            bool alpha = numChannels == 2 || numChannels == 4;
            bool isBe  = endianness == JxlEndianness.BigEndian;

            for (nuint y = 0; y < dims.YSize; ++y)
            {
                for (nuint x = 0; x < dims.XSize; ++x)
                {
                    int j    = (int)((y * dims.XSize + x) * 4);
                    int i    = (int)(y * dims.Stride + x * numChannels * 2);
                    double r = ReadF16Sample(pixels, i, isBe);
                    double g = gray ? r : ReadF16Sample(pixels, i + 2, isBe);
                    double b = gray ? r : ReadF16Sample(pixels, i + 4, isBe);
                    double a = alpha ? ReadF16Sample(pixels, i + ((int)numChannels * 2) - 2, isBe) : 1.0;

                    result[j + 0] = r;
                    result[j + 1] = g;
                    result[j + 2] = b;
                    result[j + 3] = a;
                }
            }
        }

        private static double ReadU16Sample(ReadOnlySpan<byte> p, int offset, bool isBigEndian)
        {
            return isBigEndian
                ? ByteOrder.LoadBE16(p.Slice(offset, 2))
                : ByteOrder.LoadLE16(p.Slice(offset, 2));
        }

        private static double ReadF32Sample(ReadOnlySpan<byte> p, int offset, bool isBigEndian)
        {
            return isBigEndian
                ? ByteOrder.LoadBEFloat(p.Slice(offset, 4))
                : ByteOrder.LoadLEFloat(p.Slice(offset, 4));
        }

        private static double ReadF16Sample(ReadOnlySpan<byte> p, int offset, bool isBigEndian)
        {
            ushort bits16 = (ushort)(isBigEndian
                ? ByteOrder.LoadBE16(p.Slice(offset, 2))
                : ByteOrder.LoadLE16(p.Slice(offset, 2)));

            return FloatHelper.LoadFloat16(bits16);
        }

        private static double ComputeComparisonPrecision(
            in JxlPixelFormat formatA,
            in JxlPixelFormat formatB,
            double thresholdMultiplier)
        {
            nuint bitsA      = GetPrecision(formatA.DataType);
            nuint bitsB      = GetPrecision(formatB.DataType);
            int bits         = (int)Math.Min(bitsA, bitsB);
            double precision = (0.5 * thresholdMultiplier) / ((1UL << bits) - 1UL);

            if (formatA.DataType == JxlDataType.Float16 || formatB.DataType == JxlDataType.Float16)
            {
                precision = (0.5 * thresholdMultiplier) / ((1UL << (bits - 1)) - 1UL);
            }

            if (formatB.DataType == JxlDataType.UInt8)
            {
                precision += 63.0 / 128.0;
            }

            return precision;
        }

        private static bool IsPixelMatch(
            double[] aFull,
            double[] bFull,
            int i,
            (bool GrayA, bool GrayB, bool AlphaA, bool AlphaB) flags,
            double precision)
        {
            bool colorOk;

            if (flags.GrayA || flags.GrayB)
            {
                colorOk = Near(aFull[i + 0], bFull[i + 0], precision) &&
                          !(flags.GrayA && bFull[i + 0] != bFull[i + 1] && bFull[i + 2] != bFull[i + 2]);
            }
            else
            {
                colorOk = Near(aFull[i + 0], bFull[i + 0], precision) &&
                          Near(aFull[i + 1], bFull[i + 1], precision) &&
                          Near(aFull[i + 2], bFull[i + 2], precision);
            }

            bool alphaOk = flags.AlphaA && flags.AlphaB
                ? Near(aFull[i + 3], bFull[i + 3], precision)
                : (!flags.AlphaB || Near(1.0, bFull[i + 3], precision));

            return colorOk && alphaOk;
        }

        private static bool SameFrameAlpha(
            PackedImage colorA,
            PackedImage colorB,
            uint xsize,
            uint ysize)
        {
            Check(colorA.Format.NumChannels == colorB.Format.NumChannels);
            Check(colorA.Format.DataType == colorB.Format.DataType);
            Check(colorA.Format.Endianness == colorB.Format.Endianness);
            Check(colorA.PixelsSize == colorB.PixelsSize);

            int pwidth            = (int)(PackedImage.BitsPerChannel(colorA.Format.DataType) / 8);
            uint numColor         = colorA.Format.NumChannels < 3 ? 1u : 3u;
            ReadOnlySpan<byte> pA = colorA.Pixels();
            ReadOnlySpan<byte> pB = colorB.Pixels();
            uint totalPixels      = xsize * ysize;

            for (uint px = 0; px < totalPixels; ++px)
            {
                int idx = (int)(((px * colorA.Format.NumChannels) + numColor) * (uint)pwidth);

                if (!pA.Slice(idx, pwidth).SequenceEqual(pB.Slice(idx, pwidth)))
                {
                    return false;
                }
            }

            return true;
        }

        private static ushort[] SampleUInt16Gradient(ref Rng generator)
        {
            ushort[] grad = new ushort[8];

            for (int i = 0; i < 8; i++)
            {
                grad[i] = (ushort)generator.UniformU(0, 32768);
            }

            return grad;
        }

        private static (nuint Cx, nuint Cy, nuint Cr) SampleCircleGeometry(
            ref Rng generator,
            nuint xsize,
            nuint ysize)
        {
            nuint cx = (nuint)generator.UniformU(0, xsize);
            nuint cy = (nuint)generator.UniformU(0, ysize);
            nuint cr = (nuint)generator.UniformU(0, Math.Min(xsize, ysize));

            return (cx, cy, cr);
        }

        private static (nuint Rx0, nuint Ry0, nuint Rx1, nuint Ry1) SampleRectGeometry(
            ref Rng generator,
            nuint xsize,
            nuint ysize)
        {
            nuint rx0 = (nuint)generator.UniformU(0, xsize);
            nuint ry0 = (nuint)generator.UniformU(0, ysize);
            nuint rx1 = (nuint)generator.UniformU(0, xsize);
            nuint ry1 = (nuint)generator.UniformU(0, ysize);

            if (rx1 < rx0)
            {
                (rx0, rx1) = (rx1, rx0);
            }

            if (ry1 < ry0)
            {
                (ry0, ry1) = (ry1, ry0);
            }

            return (rx0, ry0, rx1, ry1);
        }

        private static void ComputeUInt16TestPixel(
            ref Rng generator,
            (nuint X, nuint Y, nuint XSize, nuint YSize, ushort Seed) pos,
            ushort[] grad,
            (nuint Cx, nuint Cy, nuint Cr) circle,
            (nuint Rx0, nuint Ry0, nuint Rx1, nuint Ry1) rect,
            out (ushort R, ushort G, ushort B, ushort A) rgba)
        {
            nuint x     = pos.X;
            nuint y     = pos.Y;
            nuint ysize = pos.YSize;
            ushort r    = (ushort)(((grad[0] * (ysize - y - 1)) / ysize) + ((grad[4] * y) / ysize));
            ushort g    = (ushort)(((grad[1] * (ysize - y - 1)) / ysize) + ((grad[5] * y) / ysize));
            ushort b    = (ushort)(((grad[2] * (ysize - y - 1)) / ysize) + ((grad[6] * y) / ysize));
            ushort a    = (ushort)(((grad[3] * (ysize - y - 1)) / ysize) + ((grad[7] * y) / ysize));

            nuint dx = unchecked(x - circle.Cx);
            nuint dy = unchecked(y - circle.Cy);

            if (unchecked((dx * dx) + (dy * dy)) < circle.Cr * circle.Cr)
            {
                r = unchecked((ushort)((65535u - (x * y)) ^ pos.Seed));
                g = unchecked((ushort)((x << 8) + y + pos.Seed));
                b = unchecked((ushort)((y << 8) + (x * pos.Seed)));
                a = unchecked((ushort)(32768u + (x * 256u) - y));
            }
            else if (x > rect.Rx0 && x < rect.Rx1 && y > rect.Ry0 && y < rect.Ry1)
            {
                r = (ushort)generator.UniformU(0, 65536);
                g = (ushort)generator.UniformU(0, 65536);
                b = (ushort)generator.UniformU(0, 65536);
                a = (ushort)generator.UniformU(0, 65536);
            }

            rgba = (r, g, b, a);
        }

        private static void WriteBigEndianUInt16Pixel(
            byte[] pixels,
            int offset,
            nuint numChannels,
            (ushort R, ushort G, ushort B, ushort A) rgba)
        {
            pixels[offset + 0] = (byte)(rgba.R >> 8);
            pixels[offset + 1] = (byte)(rgba.R & 255);

            if (numChannels >= 2)
            {
                pixels[offset + 2] = (byte)(rgba.G >> 8);
                pixels[offset + 3] = (byte)(rgba.G & 255);
            }

            if (numChannels >= 3)
            {
                pixels[offset + 4] = (byte)(rgba.B >> 8);
                pixels[offset + 5] = (byte)(rgba.B & 255);
            }

            if (numChannels >= 4)
            {
                pixels[offset + 6] = (byte)(rgba.A >> 8);
                pixels[offset + 7] = (byte)(rgba.A & 255);
            }
        }

        private static JXLDecompressParams CloneDecompressParams(JXLDecompressParams src)
        {
            JXLDecompressParams copy = new JXLDecompressParams
            {
                ColorSpace         = src.ColorSpace,
                ColorSpaceForCmyk  = src.ColorSpaceForCmyk,
                DisplayNits        = src.DisplayNits,
                RenderSpotcolors   = src.RenderSpotcolors,
                KeepOrientation    = src.KeepOrientation,
                Coalescing         = src.Coalescing,
                Runner             = src.Runner,
                MemoryManager      = src.MemoryManager,
                AllowPartialInput  = src.AllowPartialInput,
                MaxPasses          = src.MaxPasses,
                MaxDownsampling    = src.MaxDownsampling,
                UseImageCallback   = src.UseImageCallback,
                UnpremultiplyAlpha = src.UnpremultiplyAlpha,
                OutputBitdepth     = src.OutputBitdepth,
            };

            copy.AcceptedFormats.AddRange(src.AcceptedFormats);

            return copy;
        }

        private static void ApplyPoolRunner(JXLCompressParams cparams, ThreadPool? pool)
        {
            if (cparams.Runner == null && pool != null && pool.Runner != null)
            {
                cparams.Runner = new PoolRunnerAdapter(pool);
            }
        }

        private static void ApplyPoolRunner(JXLDecompressParams dparams, ThreadPool? pool)
        {
            if (dparams.Runner == null && pool != null && pool.Runner != null)
            {
                dparams.Runner = new PoolRunnerAdapter(pool);
            }
        }

        private static Status PrepareAndWriteCodestreamHeaders(
            CompressParams cparams,
            CodecInOut io,
            JxlCmsInterface cms,
            BitWriter writer,
            out CodecMetadata metadata)
        {
            metadata  = io.Metadata;
            nuint ups = cparams.AlreadyDownsampled ? (nuint)cparams.Resampling : 1u;

            if (!metadata.Size.Set((nuint)io.XSize * ups, (nuint)io.YSize * ups))
            {
                return false;
            }

            if (!cparams.IsLossless() && !io.Main.IsJPEG() && cparams.CmsSet && cparams.Cms != null)
            {
                metadata.M.ColorEncoding.DecideIfWantICC(cparams.Cms);
            }
            else if (!cparams.IsLossless() && !io.Main.IsJPEG())
            {
                metadata.M.ColorEncoding.DecideIfWantICC(cms);
            }

            metadata.M.XybEncoded                          = cparams.ColorTransform == ColorTransform.XYB;
            metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;

            bool sigOk = writer.WithMaxBits(
                16,
                LayerType.Header,
                null,
                () =>
                {
                    writer.Write(8, 0xFF);
                    writer.Write(8, 0x0A);

                    return true;
                });

            if (!sigOk ||
                !Headers.WriteSizeHeader(metadata.Size, writer, LayerType.Header, null) ||
                !ImageMetadataMethods.WriteImageMetadata(metadata.M, writer, LayerType.Header, null) ||
                !Bundle.Write(metadata.TransformData, writer, LayerType.Header, null))
            {
                return false;
            }

            if (metadata.M.ColorEncoding.WantICC() &&
                !WriteICC(metadata.M.ColorEncoding.ICC(), writer, LayerType.Header, null))
            {
                return false;
            }

            return writer.WithMaxBits(
                8,
                LayerType.Header,
                null,
                () =>
                {
                    writer.ZeroPadToByte();

                    return true;
                });
        }

        private static Status EncodePreviewAndFrames(
            JxlMemoryManager mm,
            CompressParams cparams,
            CodecMetadata metadata,
            CodecInOut io,
            JxlCmsInterface cms,
            ThreadPool? pool,
            BitWriter writer)
        {
            if (metadata.M.HavePreview && io.PreviewFrame.HasColor())
            {
                BitWriter previewWriter = new BitWriter(mm);
                FrameInfo previewInfo   = new FrameInfo { IsPreview = true };

                if (!EncFrame.EncodeFrame(mm, cparams, previewInfo, metadata, io.PreviewFrame, cms, (pool, previewWriter, null)))
                {
                    return false;
                }

                previewWriter.ZeroPadToByte();
                writer.ZeroPadToByte();

                if (!writer.AppendByteAligned(previewWriter.GetSpan()))
                {
                    return false;
                }
            }

            for (int i = 0; i < io.Frames.Count; i++)
            {
                FrameInfo info = new FrameInfo
                {
                    IsLast          = i == io.Frames.Count - 1,
                    SaveAsReference = io.Frames[i].UseForNextFrame ? 1u : 0u,
                };

                if (!EncFrame.EncodeFrame(mm, cparams, info, metadata, io.Frames[i], cms, (pool, writer, null)))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CopyDistmapIfRequested(ImagePlane<float>? src, ImagePlane<float>? dst)
        {
            if (src != null && dst != null && !ReferenceEquals(src, dst))
            {
                _ = ImageOps.CopyImageTo(src, dst);
            }
        }

        private sealed class PoolRunnerAdapter : IParallelRunner
        {
            private readonly ThreadPool _pool;

            public PoolRunnerAdapter(ThreadPool pool)
            {
                _pool = pool;
            }

            public JxlParallelRunner? Runner => _pool.Runner;

            public object? RunnerOpaque => _pool.RunnerOpaque;
        }
    }

    /// <summary>
    /// Fluent builder and manipulator for <see cref="PackedPixelFile"/> test images and animation frames.
    /// Ported from <c>lib/jxl/test_image.h</c> and <c>lib/jxl/test_image.cc</c>.
    /// </summary>
    public sealed class TestImage
    {
        private readonly PackedPixelFile _ppf = new PackedPixelFile();
        private JxlPixelFormat _format        = new JxlPixelFormat
        {
            NumChannels = 3,
            DataType    = JxlDataType.UInt8,
            Endianness  = JxlEndianness.NativeEndian,
            Align       = 0,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="TestImage"/> class with 3 channels, 8 bits per sample, and sRGB color encoding.
        /// Ported from <c>jxl::test::TestImage::TestImage</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        public TestImage()
        {
            JxlEncoder.JxlEncoderInitBasicInfo(out _ppf.Info);
            TestUtils.Check((bool)SetChannels(3));
            SetAllBitDepths(8);
            TestUtils.Check((bool)SetColorEncoding("RGB_D65_SRG_Rel_SRG"));
        }

        /// <summary>Gets the underlying <see cref="PackedPixelFile"/>.</summary>
        public PackedPixelFile Ppf => _ppf;

        /// <summary>
        /// Returns the underlying <see cref="PackedPixelFile"/> (matching C++ <c>ppf()</c>).
        /// </summary>
        /// <returns>The underlying <see cref="PackedPixelFile"/>.</returns>
        public PackedPixelFile GetPpf()
        {
            return _ppf;
        }

        /// <summary>
        /// Decodes an image from <paramref name="bytes"/> into this <see cref="TestImage"/> using <see cref="ExtrasDecodeFacade.DecodeBytes(ReadOnlySpan{byte}, ColorHints, PackedPixelFile, SizeConstraints?)"/>.
        /// Ported from <c>jxl::test::TestImage::DecodeFromBytes</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="bytes">Encoded image bytes.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeFromBytes(ReadOnlySpan<byte> bytes)
        {
            ColorEncoding cEnc = new ColorEncoding();

            if (!cEnc.FromExternal(_ppf.ColorEncoding))
            {
                return false;
            }

            ColorHints colorHints = new ColorHints();

            colorHints.Add("color_space", ColorDescription.Description(cEnc));

            return ExtrasDecodeFacade.DecodeBytes(bytes, colorHints, _ppf);
        }

        /// <summary>
        /// Resets all metadata boxes on the underlying <see cref="PackedPixelFile"/>.
        /// Ported from <c>jxl::test::TestImage::ClearMetadata</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <returns>This <see cref="TestImage"/> instance.</returns>
        public TestImage ClearMetadata()
        {
            _ppf.Metadata.Exif  = Array.Empty<byte>();
            _ppf.Metadata.Iptc  = Array.Empty<byte>();
            _ppf.Metadata.Jhgm  = Array.Empty<byte>();
            _ppf.Metadata.Jumbf = Array.Empty<byte>();
            _ppf.Metadata.Xmp   = Array.Empty<byte>();

            return this;
        }

        /// <summary>
        /// Sets or crops the dimensions of the image and all existing frames.
        /// Ported from <c>jxl::test::TestImage::SetDimensions</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="xsize">New width in pixels.</param>
        /// <param name="ysize">New height in pixels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetDimensions(nuint xsize, nuint ysize)
        {
            if (xsize <= _ppf.Info.XSize && ysize <= _ppf.Info.YSize)
            {
                for (int i = 0; i < _ppf.Frames.Count; i++)
                {
                    PackedFrame frame        = _ppf.Frames[i];
                    JxlFrameHeader frameInfo = frame.FrameInfo;
                    JxlLayerInfo layerInfo   = frameInfo.LayerInfo;

                    CropLayerInfo(xsize, ysize, ref layerInfo);
                    frameInfo.LayerInfo = layerInfo;
                    frame.FrameInfo     = frameInfo;
                    frame.Color         = CropImage(xsize, ysize, frame.Color);

                    for (int j = 0; j < frame.ExtraChannels.Count; j++)
                    {
                        frame.ExtraChannels[j] = CropImage(xsize, ysize, frame.ExtraChannels[j]);
                    }
                }
            }
            else if (_ppf.Info.XSize != 0 || _ppf.Info.YSize != 0)
            {
                return Status.Failure("Cannot enlarge non-empty TestImage");
            }

            _ppf.Info.XSize = (uint)xsize;
            _ppf.Info.YSize = (uint)ysize;

            return true;
        }

        /// <summary>
        /// Configures the number of color and extra channels prior to adding frames.
        /// Ported from <c>jxl::test::TestImage::SetChannels</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="numChannels">Total number of channels (color + extra).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetChannels(nuint numChannels)
        {
            if (_ppf.Frames.Count != 0 || _ppf.PreviewFrame != null)
            {
                return Status.Failure("SetChannels requires empty frames and preview");
            }

            _ppf.Info.NumColorChannels = numChannels < 3 ? 1u : 3u;
            _ppf.Info.NumExtraChannels = (uint)numChannels - _ppf.Info.NumColorChannels;

            if (_ppf.Info.NumExtraChannels > 0 && _ppf.Info.AlphaBits == 0)
            {
                _ppf.Info.AlphaBits         = _ppf.Info.BitsPerSample;
                _ppf.Info.AlphaExponentBits = _ppf.Info.ExponentBitsPerSample;
            }

            PopulateExtraChannelInfos(_ppf);

            _format.NumChannels = (uint)Math.Min((nuint)4, numChannels);

            if (_ppf.Info.NumColorChannels == 1 &&
                _ppf.ColorEncoding.ColorSpace != JxlColorSpace.Gray)
            {
                return SetColorEncoding("Gra_D65_Rel_SRG");
            }

            return true;
        }

        /// <summary>
        /// Sets the bit depth and exponent bit depth across color, alpha, and all extra channels.
        /// Ported from <c>jxl::test::TestImage::SetAllBitDepths</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="exponentBitsPerSample">Exponent bits per sample (0 for integer formats).</param>
        /// <returns>This <see cref="TestImage"/> instance.</returns>
        public TestImage SetAllBitDepths(uint bitsPerSample, uint exponentBitsPerSample = 0)
        {
            _ppf.Info.BitsPerSample         = bitsPerSample;
            _ppf.Info.ExponentBitsPerSample = exponentBitsPerSample;

            if (_ppf.Info.NumExtraChannels > 0)
            {
                _ppf.Info.AlphaBits         = bitsPerSample;
                _ppf.Info.AlphaExponentBits = exponentBitsPerSample;
            }

            foreach (PackedExtraChannel ec in _ppf.ExtraChannelsInfo)
            {
                ec.EcInfo.BitsPerSample         = bitsPerSample;
                ec.EcInfo.ExponentBitsPerSample = exponentBitsPerSample;
            }

            _format.DataType = DefaultDataType(in _ppf.Info);

            return this;
        }

        /// <summary>
        /// Sets the pixel storage data type used for subsequent frames.
        /// Ported from <c>jxl::test::TestImage::SetDataType</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="dataType">Target <see cref="JxlDataType"/>.</param>
        /// <returns>This <see cref="TestImage"/> instance.</returns>
        public TestImage SetDataType(JxlDataType dataType)
        {
            _format.DataType = dataType;

            return this;
        }

        /// <summary>
        /// Sets the byte order used for multi-byte pixel samples in subsequent frames.
        /// Ported from <c>jxl::test::TestImage::SetEndianness</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="endianness">Target <see cref="JxlEndianness"/>.</param>
        /// <returns>This <see cref="TestImage"/> instance.</returns>
        public TestImage SetEndianness(JxlEndianness endianness)
        {
            _format.Endianness = endianness;

            return this;
        }

        /// <summary>
        /// Sets the row stride alignment in bytes for subsequent frames.
        /// Ported from <c>jxl::test::TestImage::SetRowAlignment</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="align">Row alignment in bytes.</param>
        /// <returns>This <see cref="TestImage"/> instance.</returns>
        public TestImage SetRowAlignment(nuint align)
        {
            _format.Align = align;

            return this;
        }

        /// <summary>
        /// Sets the color encoding and corresponding ICC profile from a textual description string.
        /// Ported from <c>jxl::test::TestImage::SetColorEncoding</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <param name="description">Color description string (e.g. <c>"RGB_D65_SRG_Rel_SRG"</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetColorEncoding(string description)
        {
            if (!ColorDescription.ParseDescription(description, out _ppf.ColorEncoding))
            {
                return false;
            }

            ColorEncoding cEnc = new ColorEncoding();

            if (!cEnc.FromExternal(_ppf.ColorEncoding))
            {
                return false;
            }

            byte[] icc = cEnc.ICC();

            if (icc.Length == 0)
            {
                return Status.Failure("Generated ICC is empty");
            }

            _ppf.Icc = (byte[])icc.Clone();

            return true;
        }

        /// <summary>
        /// Coalesces a multi-frame GIF animation with a separate alpha extra channel in-place.
        /// Ported from <c>jxl::test::TestImage::CoalesceGIFAnimationWithAlpha</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status CoalesceGIFAnimationWithAlpha()
        {
            StatusOr<PackedFrame> canvasOr = _ppf.Frames[0].Copy();

            if (!canvasOr.IsOk)
            {
                return canvasOr.Status;
            }

            PackedFrame canvas = canvasOr.Value;

            if (canvas.Color.Format.NumChannels != 3 ||
                canvas.Color.Format.DataType != JxlDataType.UInt8 ||
                canvas.ExtraChannels.Count != 1)
            {
                return Status.Failure("Invalid GIF canvas format");
            }

            for (int i = 1; i < _ppf.Frames.Count; i++)
            {
                if (!CoalesceSingleGifFrame(ref canvas, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Allocates and appends a new frame with the current dimensions, format, and extra channels.
        /// Ported from <c>jxl::test::TestImage::AddFrame</c> in <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        /// <returns>A <see cref="Frame"/> wrapper around the newly added frame.</returns>
        public StatusOr<Frame> AddFrame()
        {
            int index                     = _ppf.Frames.Count;
            StatusOr<PackedFrame> frameOr = PackedFrame.Create(_ppf.Info.XSize, _ppf.Info.YSize, in _format);

            if (!frameOr.IsOk)
            {
                return frameOr.Status;
            }

            PackedFrame frame = frameOr.Value;

            for (int i = 0; i < _ppf.ExtraChannelsInfo.Count; ++i)
            {
                JxlPixelFormat ecFormat = new JxlPixelFormat
                {
                    NumChannels = 1,
                    DataType    = _format.DataType,
                    Endianness  = _format.Endianness,
                    Align       = 0,
                };

                StatusOr<PackedImage> imageOr = PackedImage.Create(_ppf.Info.XSize, _ppf.Info.YSize, in ecFormat);

                if (!imageOr.IsOk)
                {
                    return imageOr.Status;
                }

                frame.ExtraChannels.Add(imageOr.Value);
            }

            _ppf.Frames.Add(frame);

            return new Frame(this, false, index);
        }

        private static void PopulateExtraChannelInfos(PackedPixelFile ppf)
        {
            ppf.ExtraChannelsInfo.Clear();

            for (uint i = 1; i < ppf.Info.NumExtraChannels; ++i)
            {
                PackedExtraChannel ec = new PackedExtraChannel { Index = i };

                JxlEncoder.JxlEncoderInitExtraChannelInfo(JxlExtraChannelType.Alpha, out ec.EcInfo);

                if (ec.EcInfo.BitsPerSample == 0)
                {
                    ec.EcInfo.BitsPerSample         = ppf.Info.BitsPerSample;
                    ec.EcInfo.ExponentBitsPerSample = ppf.Info.ExponentBitsPerSample;
                }

                ppf.ExtraChannelsInfo.Add(ec);
            }
        }

        private static void CropLayerInfo(nuint xsize, nuint ysize, ref JxlLayerInfo info)
        {
            if (info.CropX0 < (nint)xsize)
            {
                nuint maxW = (nuint)((nint)xsize - info.CropX0);
                info.XSize = (uint)Math.Min((nuint)info.XSize, maxW);
            }
            else
            {
                info.XSize = 0;
            }

            if (info.CropY0 < (nint)ysize)
            {
                nuint maxH = (nuint)((nint)ysize - info.CropY0);
                info.YSize = (uint)Math.Min((nuint)info.YSize, maxH);
            }
            else
            {
                info.YSize = 0;
            }
        }

        private static PackedImage CropImage(nuint xsize, nuint ysize, PackedImage image)
        {
            StatusOr<PackedImage> croppedOr = PackedImage.Create(xsize, ysize, image.Format);

            TestUtils.Check(croppedOr.IsOk);

            PackedImage cropped    = croppedOr.Value;
            ReadOnlySpan<byte> src = image.Pixels();
            Span<byte> dst         = cropped.Pixels();
            int rowBytes           = (int)Math.Min(image.Stride, cropped.Stride);

            for (nuint y = 0; y < ysize; ++y)
            {
                src.Slice((int)(y * image.Stride), rowBytes)
                   .CopyTo(dst.Slice((int)(y * cropped.Stride), rowBytes));
            }

            return cropped;
        }

        private static JxlDataType DefaultDataType(in JxlBasicInfo info)
        {
            if (info.BitsPerSample == 16 && info.ExponentBitsPerSample == 5)
            {
                return JxlDataType.Float16;
            }

            if (info.ExponentBitsPerSample > 0 || info.BitsPerSample > 16)
            {
                return JxlDataType.Float;
            }

            if (info.BitsPerSample > 8)
            {
                return JxlDataType.UInt16;
            }

            return JxlDataType.UInt8;
        }

        private static bool SwapEndianness(JxlEndianness endianness)
        {
            return (endianness == JxlEndianness.BigEndian && ByteOrder.IsLittleEndian()) ||
                   (endianness == JxlEndianness.LittleEndian && !ByteOrder.IsLittleEndian());
        }

        private static void StoreValue(
            float val,
            nuint bitsPerSample,
            in JxlPixelFormat format,
            Span<byte> rowSpan,
            ref int offset)
        {
            float mul = (float)((1UL << (int)bitsPerSample) - 1UL);

            if (format.DataType == JxlDataType.UInt8)
            {
                rowSpan[offset] = (byte)(val * mul);
            }
            else if (format.DataType == JxlDataType.UInt16)
            {
                ushort uval = (ushort)(val * mul);

                if (SwapEndianness(format.Endianness))
                {
                    uval = BinaryPrimitives.ReverseEndianness(uval);
                }

                MemoryMarshal.Write(rowSpan.Slice(offset, 2), in uval);
            }
            else if (format.DataType == JxlDataType.Float)
            {
                uint raw = BitConverter.SingleToUInt32Bits(val);

                if (SwapEndianness(format.Endianness))
                {
                    raw = BinaryPrimitives.ReverseEndianness(raw);
                }

                MemoryMarshal.Write(rowSpan.Slice(offset, 4), in raw);
            }

            offset += (int)(PackedImage.BitsPerChannel(format.DataType) / 8);
        }

        private static void FillPackedImage(nuint bitsPerSample, ushort seed, PackedImage image)
        {
            nuint xsize           = image.XSize;
            nuint ysize           = image.YSize;
            JxlPixelFormat format = image.Format;
            Rng generator         = new Rng(seed);
            float[] grad          = SampleFloatGradient(ref generator);

            nuint circleX = (nuint)generator.UniformU(0, xsize);
            nuint circleY = (nuint)generator.UniformU(0, ysize);
            nuint circleR = (nuint)generator.UniformU(0, Math.Min(xsize, ysize));

            (nuint rx0, nuint ry0, nuint rx1, nuint ry1) = SampleFloatRect(ref generator, xsize, ysize);
            Span<byte> allPixels                         = image.Pixels();

            for (nuint y = 0; y < ysize; y++)
            {
                Span<byte> rowSpan = allPixels.Slice((int)(y * image.Stride));
                int rowOffset      = 0;

                for (nuint x = 0; x < xsize; x++)
                {
                    ComputeFloatTestPixel(
                        ref generator,
                        (x, y, xsize, ysize, seed),
                        grad,
                        (circleX, circleY, circleR),
                        (rx0, ry0, rx1, ry1),
                        out (float R, float G, float B, float A) rgba);

                    WritePackedPixelChannels(rgba, bitsPerSample, in format, rowSpan, ref rowOffset);
                }
            }
        }

        private static float[] SampleFloatGradient(ref Rng generator)
        {
            float[] grad = new float[8];

            for (int i = 0; i < 8; i++)
            {
                grad[i] = generator.UniformF(0.0f, 0.5f);
            }

            return grad;
        }

        private static (nuint Rx0, nuint Ry0, nuint Rx1, nuint Ry1) SampleFloatRect(
            ref Rng generator,
            nuint xsize,
            nuint ysize)
        {
            nuint rx0 = (nuint)generator.UniformU(0, xsize);
            nuint ry0 = (nuint)generator.UniformU(0, ysize);
            nuint rx1 = (nuint)generator.UniformU(0, xsize);
            nuint ry1 = (nuint)generator.UniformU(0, ysize);

            if (rx1 < rx0)
            {
                (rx0, rx1) = (rx1, rx0);
            }

            if (ry1 < ry0)
            {
                (ry0, ry1) = (ry1, ry0);
            }

            return (rx0, ry0, rx1, ry1);
        }

        private static void ComputeFloatTestPixel(
            ref Rng generator,
            (nuint X, nuint Y, nuint XSize, nuint YSize, ushort Seed) pos,
            float[] grad,
            (nuint Cx, nuint Cy, nuint Cr) circle,
            (nuint Rx0, nuint Ry0, nuint Rx1, nuint Ry1) rect,
            out (float R, float G, float B, float A) rgba)
        {
            const float imul16 = 1.0f / 65536.0f;
            nuint x            = pos.X;
            nuint y            = pos.Y;
            nuint ysize        = pos.YSize;
            float r            = ((grad[0] * (ysize - y - 1)) / ysize) + ((grad[4] * y) / ysize);
            float g            = ((grad[1] * (ysize - y - 1)) / ysize) + ((grad[5] * y) / ysize);
            float b            = ((grad[2] * (ysize - y - 1)) / ysize) + ((grad[6] * y) / ysize);
            float a            = ((grad[3] * (ysize - y - 1)) / ysize) + ((grad[7] * y) / ysize);

            nuint dx = unchecked(x - circle.Cx);
            nuint dy = unchecked(y - circle.Cy);

            if (unchecked((dx * dx) + (dy * dy)) < circle.Cr * circle.Cr)
            {
                r = Math.Min(1.0f, unchecked((65535u - (x * y)) ^ pos.Seed) * imul16);
                g = Math.Min(1.0f, unchecked((x << 8) + y + pos.Seed) * imul16);
                b = Math.Min(1.0f, unchecked((y << 8) + (x * pos.Seed)) * imul16);
                a = Math.Min(1.0f, unchecked(32768u + (x * 256u) - y) * imul16);
            }
            else if (x > rect.Rx0 && x < rect.Rx1 && y > rect.Ry0 && y < rect.Ry1)
            {
                r = generator.UniformF(0.0f, 1.0f);
                g = generator.UniformF(0.0f, 1.0f);
                b = generator.UniformF(0.0f, 1.0f);
                a = generator.UniformF(0.0f, 1.0f);
            }

            rgba = (r, g, b, a);
        }

        private static void WritePackedPixelChannels(
            (float R, float G, float B, float A) rgba,
            nuint bitsPerSample,
            in JxlPixelFormat format,
            Span<byte> rowSpan,
            ref int rowOffset)
        {
            if (format.NumChannels == 1)
            {
                StoreValue(rgba.G, bitsPerSample, in format, rowSpan, ref rowOffset);
            }
            else if (format.NumChannels == 2)
            {
                StoreValue(rgba.G, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.A, bitsPerSample, in format, rowSpan, ref rowOffset);
            }
            else if (format.NumChannels == 3)
            {
                StoreValue(rgba.R, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.G, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.B, bitsPerSample, in format, rowSpan, ref rowOffset);
            }
            else if (format.NumChannels == 4)
            {
                StoreValue(rgba.R, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.G, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.B, bitsPerSample, in format, rowSpan, ref rowOffset);
                StoreValue(rgba.A, bitsPerSample, in format, rowSpan, ref rowOffset);
            }
        }

        private Status CoalesceSingleGifFrame(ref PackedFrame canvas, int frameIndex)
        {
            PackedFrame frame = _ppf.Frames[frameIndex];

            if (frame.ExtraChannels.Count != 1)
            {
                return Status.Failure("Expected 1 extra channel on GIF frame");
            }

            StatusOr<PackedFrame> renderedOr = canvas.Copy();

            if (!renderedOr.IsOk)
            {
                return renderedOr.Status;
            }

            PackedFrame rendered = renderedOr.Value;
            BlendGifFrameOntoCanvas(frame, rendered);

            if (frame.FrameInfo.LayerInfo.SaveAsReference != 0)
            {
                StatusOr<PackedFrame> nextCanvasOr = rendered.Copy();

                if (!nextCanvasOr.IsOk)
                {
                    return nextCanvasOr.Status;
                }

                canvas = nextCanvasOr.Value;
            }

            _ppf.Frames[frameIndex] = rendered;

            return true;
        }

        private static void BlendGifFrameOntoCanvas(PackedFrame frame, PackedFrame rendered)
        {
            JxlLayerInfo layerInfo         = frame.FrameInfo.LayerInfo;
            Span<byte> pixelsRendered      = rendered.Color.Pixels();
            ReadOnlySpan<byte> pixelsFrame = frame.Color.Pixels();
            Span<byte> alphaRendered       = rendered.ExtraChannels[0].Pixels();
            ReadOnlySpan<byte> alphaFrame  = frame.ExtraChannels[0].Pixels();

            for (nuint y = 0; y < frame.Color.YSize; y++)
            {
                for (nuint x = 0; x < frame.Color.XSize; x++)
                {
                    int idxFrame    = (int)((y * frame.Color.XSize) + x);
                    nuint dstY      = (nuint)(layerInfo.CropY0 + (int)y);
                    nuint dstX      = (nuint)(layerInfo.CropX0 + (int)x);
                    int idxRendered = (int)((dstY * rendered.Color.XSize) + dstX);

                    if (alphaFrame[idxFrame] != 0)
                    {
                        pixelsFrame.Slice(idxFrame * 3, 3).CopyTo(pixelsRendered.Slice(idxRendered * 3, 3));
                        alphaRendered[idxRendered] = alphaFrame[idxFrame];
                    }
                }
            }
        }

        /// <summary>
        /// Represents a single frame or preview frame within a <see cref="TestImage"/>.
        /// Ported from <c>jxl::test::TestImage::Frame</c> in <c>lib/jxl/test_image.h</c> and <c>lib/jxl/test_image.cc</c>.
        /// </summary>
        public sealed class Frame
        {
            private readonly TestImage _parent;
            private readonly bool _isPreview;
            private readonly int _index;

            /// <summary>
            /// Initializes a new instance of the <see cref="Frame"/> class.
            /// </summary>
            /// <param name="parent">Owning <see cref="TestImage"/>.</param>
            /// <param name="isPreview">True if this wraps the preview frame; false for a regular frame.</param>
            /// <param name="index">Frame index within <see cref="PackedPixelFile.Frames"/>.</param>
            public Frame(TestImage parent, bool isPreview, int index)
            {
                _parent    = parent;
                _isPreview = isPreview;
                _index     = index;
            }

            /// <summary>Gets the underlying <see cref="PackedFrame"/>.</summary>
            public PackedFrame PackedFrame => _isPreview ? _parent._ppf.PreviewFrame! : _parent._ppf.Frames[_index];

            /// <summary>
            /// Fills the color image and all extra channels of this frame with zeros.
            /// Ported from <c>jxl::test::TestImage::Frame::ZeroFill</c> in <c>lib/jxl/test_image.cc</c>.
            /// </summary>
            public void ZeroFill()
            {
                PackedFrame frame = PackedFrame;

                frame.Color.Pixels().Clear();

                foreach (PackedImage ec in frame.ExtraChannels)
                {
                    ec.Pixels().Clear();
                }
            }

            /// <summary>
            /// Fills the color image and all extra channels of this frame with deterministic synthetic test content.
            /// Ported from <c>jxl::test::TestImage::Frame::RandomFill</c> in <c>lib/jxl/test_image.cc</c>.
            /// </summary>
            /// <param name="seed">Base PRNG seed (defaults to 177).</param>
            public void RandomFill(ushort seed = 177)
            {
                PackedFrame frame   = PackedFrame;
                PackedPixelFile ppf = _parent._ppf;

                FillPackedImage(ppf.Info.BitsPerSample, seed, frame.Color);

                for (int i = 0; i < ppf.ExtraChannelsInfo.Count; ++i)
                {
                    FillPackedImage(
                        ppf.ExtraChannelsInfo[i].EcInfo.BitsPerSample,
                        (ushort)(seed + 1 + i),
                        frame.ExtraChannels[i]);
                }
            }

            /// <summary>
            /// Sets a single channel value in <c>[0, 1]</c> at <c>(y, x, c)</c> in the frame's color image.
            /// Ported from <c>jxl::test::TestImage::Frame::SetValue</c> in <c>lib/jxl/test_image.cc</c>.
            /// </summary>
            /// <param name="y">Row coordinate.</param>
            /// <param name="x">Column coordinate.</param>
            /// <param name="c">Channel index.</param>
            /// <param name="val">Normalized sample value.</param>
            /// <returns><see cref="Status.OkStatus"/> on success.</returns>
            public Status SetValue(nuint y, nuint x, nuint c, float val)
            {
                PackedFrame frame   = PackedFrame;
                PackedPixelFile ppf = _parent._ppf;
                PackedImage color   = frame.Color;
                JxlPixelFormat fmt  = color.Format;

                if (y >= ppf.Info.YSize || x >= ppf.Info.XSize || c >= fmt.NumChannels)
                {
                    return Status.Failure("Coordinates out of bounds in Frame.SetValue");
                }

                nuint pwidth = PackedImage.BitsPerChannel(fmt.DataType) / 8;
                int idx      = (int)((((y * color.XSize + x) * fmt.NumChannels) + c) * pwidth);
                int offset   = idx;

                StoreValue(val, ppf.Info.BitsPerSample, in fmt, color.Pixels(), ref offset);

                return true;
            }
        }
    }
}
