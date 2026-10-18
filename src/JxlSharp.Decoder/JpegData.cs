// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Data structures that represent the non-pixel contents of a jpeg file,
// plus JPEG metadata decoding and JPEG bitstream reconstruction.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Constants and zigzag tables for JPEG reconstruction.
    /// Ported from <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public static class JpegConstants
    {
        /// <summary>Maximum number of components in a JPEG image (4).</summary>
        public const int KMaxComponents = 4;

        /// <summary>Maximum number of quantization tables in a JPEG image (4).</summary>
        public const int KMaxQuantTables = 4;

        /// <summary>Maximum number of Huffman tables per class (4).</summary>
        public const int KMaxHuffmanTables = 4;

        /// <summary>Maximum bit length of a JPEG Huffman codeword (16).</summary>
        public const int KJpegHuffmanMaxBitLength = 16;

        /// <summary>Size of the JPEG Huffman symbol alphabet (256).</summary>
        public const int KJpegHuffmanAlphabetSize = 256;

        /// <summary>Size of the JPEG DC Huffman symbol alphabet (12).</summary>
        public const int KJpegDCAlphabetSize = 12;

        /// <summary>Maximum number of DHT markers (512).</summary>
        public const int KMaxDHTMarkers = 512;

        /// <summary>Maximum dimension in pixels of a JPEG image (65535).</summary>
        public const int KMaxDimPixels = 65535;

        /// <summary>JPEG APP1 marker byte (<c>0xE1</c>).</summary>
        public const byte KApp1 = 0xE1;

        /// <summary>JPEG APP2 marker byte (<c>0xE2</c>).</summary>
        public const byte KApp2 = 0xE2;

        /// <summary>12-byte <c>ICC_PROFILE\0</c> tag in APP2 markers.</summary>
        public static ReadOnlySpan<byte> KIccProfileTag => new byte[12]
        {
            (byte)'I', (byte)'C', (byte)'C', (byte)'_',
            (byte)'P', (byte)'R', (byte)'O', (byte)'F',
            (byte)'I', (byte)'L', (byte)'E', 0,
        };

        /// <summary>6-byte <c>Exif\0\0</c> tag in APP1 markers.</summary>
        public static ReadOnlySpan<byte> KExifTag => new byte[6]
        {
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
        };

        /// <summary>29-byte <c>http://ns.adobe.com/xap/1.0/\0</c> tag in APP1 markers.</summary>
        public static ReadOnlySpan<byte> KXMPTag => new byte[29]
        {
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)':', (byte)'/', (byte)'/',
            (byte)'n', (byte)'s', (byte)'.', (byte)'a', (byte)'d', (byte)'o', (byte)'b', (byte)'e', (byte)'.',
            (byte)'c', (byte)'o', (byte)'m', (byte)'/', (byte)'x', (byte)'a', (byte)'p', (byte)'/',
            (byte)'1', (byte)'.', (byte)'0', (byte)'/', 0,
        };

        /// <summary>
        /// Zig-zag to natural 8x8 raster index lookup table (with 16 extra safety entries).
        /// Ported from <c>jxl::jpeg::kJPEGNaturalOrder</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
        /// </summary>
        public static ReadOnlySpan<uint> KJPEGNaturalOrder => new uint[80]
        {
            0,  1,  8,  16, 9,  2,  3,  10,
            17, 24, 32, 25, 18, 11, 4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13, 6,  7,  14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
            // extra entries for safety in decoder
            63, 63, 63, 63, 63, 63, 63, 63,
            63, 63, 63, 63, 63, 63, 63, 63,
        };

        /// <summary>
        /// Natural 8x8 raster to zig-zag index lookup table.
        /// Ported from <c>jxl::jpeg::kJPEGZigZagOrder</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
        /// </summary>
        public static ReadOnlySpan<uint> KJPEGZigZagOrder => new uint[64]
        {
            0,  1,  5,  6,  14, 15, 27, 28,
            2,  4,  7,  13, 16, 26, 29, 42,
            3,  8,  12, 17, 25, 30, 41, 43,
            9,  11, 18, 24, 31, 40, 44, 53,
            10, 19, 23, 32, 39, 45, 52, 54,
            20, 22, 33, 38, 46, 51, 55, 60,
            21, 34, 37, 47, 50, 56, 59, 61,
            35, 36, 48, 49, 57, 58, 62, 63,
        };
    }

    /// <summary>
    /// Quantization values for an 8x8 pixel block.
    /// Ported from <c>jxl::jpeg::JPEGQuantTable</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public sealed class JPEGQuantTable
    {
        /// <summary>64 quantization values in natural 8x8 order.</summary>
        public readonly int[] Values = new int[FrameDimensions.KDctBlockSize];

        /// <summary>Precision of quantization values (0 = 8-bit, 1 = 16-bit).</summary>
        public uint Precision;

        /// <summary>
        /// The index of this quantization table as it was parsed from the input JPEG.
        /// Each DQT marker segment contains an 'index' field, and we save this index
        /// here. Valid values are 0 to 3.
        /// </summary>
        public uint Index;

        /// <summary>Set to true if this table is the last one within its marker segment.</summary>
        public bool IsLast = true;
    }

    /// <summary>
    /// Huffman code and decoding lookup table used for DC and AC coefficients.
    /// Ported from <c>jxl::jpeg::JPEGHuffmanCode</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public sealed class JPEGHuffmanCode
    {
        /// <summary>Bit length histogram (<c>0..16</c>).</summary>
        public readonly uint[] Counts = new uint[JpegConstants.KJpegHuffmanMaxBitLength + 1];

        /// <summary>Symbol values sorted by increasing bit lengths.</summary>
        public readonly uint[] Values = new uint[JpegConstants.KJpegHuffmanAlphabetSize + 1];

        /// <summary>
        /// The index of the Huffman code in the current set of Huffman codes. For AC
        /// component Huffman codes, <c>0x10</c> is added to the index.
        /// </summary>
        public int SlotId;

        /// <summary>Set to true if this Huffman code is the last one within its marker segment.</summary>
        public bool IsLast = true;
    }

    /// <summary>
    /// Huffman table indexes used for one component of one scan.
    /// Ported from <c>jxl::jpeg::JPEGComponentScanInfo</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public struct JPEGComponentScanInfo
    {
        /// <summary>Component index (<c>0..3</c>).</summary>
        public uint CompIdx;

        /// <summary>DC Huffman table index (<c>0..3</c>).</summary>
        public uint DcTblIdx;

        /// <summary>AC Huffman table index (<c>0..3</c>).</summary>
        public uint AcTblIdx;
    }

    /// <summary>
    /// Contains information that is used in one JPEG scan.
    /// Ported from <c>jxl::jpeg::JPEGScanInfo</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public sealed class JPEGScanInfo
    {
        /// <summary>Start of spectral band in zig-zag sequence.</summary>
        public uint Ss;

        /// <summary>End of spectral band in zig-zag sequence.</summary>
        public uint Se;

        /// <summary>Successive approximation bit position, high.</summary>
        public uint Ah;

        /// <summary>Successive approximation bit position, low.</summary>
        public uint Al;

        /// <summary>Number of components in this scan.</summary>
        public uint NumComponents;

        /// <summary>Per-component scan parameters (up to 4 components).</summary>
        public readonly JPEGComponentScanInfo[] Components = new JPEGComponentScanInfo[4];

        /// <summary>Last codestream pass that is needed to write this scan.</summary>
        public uint LastNeededPass;

        /// <summary>
        /// Set of block indexes where the JPEG encoder has to flush the end-of-block
        /// runs and refinement bits.
        /// </summary>
        public readonly List<uint> ResetPoints = new List<uint>();

        /// <summary>
        /// The number of extra zero runs (Huffman symbol <c>0xf0</c>) before the end of
        /// block (if nonzero), indexed by block index.
        /// </summary>
        public readonly List<ExtraZeroRunInfo> ExtraZeroRuns = new List<ExtraZeroRunInfo>();

        /// <summary>
        /// Extra zero run count before EOB at a specific block index.
        /// </summary>
        public struct ExtraZeroRunInfo
        {
            /// <summary>Block index within the scan.</summary>
            public uint BlockIdx;

            /// <summary>Number of extra <c>0xf0</c> zero-run symbols.</summary>
            public uint NumExtraZeroRuns;
        }
    }

    /// <summary>
    /// Represents one component of a JPEG file.
    /// Ported from <c>jxl::jpeg::JPEGComponent</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public sealed class JPEGComponent
    {
        /// <summary>One-byte id of the component.</summary>
        public uint Id;

        /// <summary>Horizontal sampling factor.</summary>
        public int HSampFactor = 1;

        /// <summary>Vertical sampling factor.</summary>
        public int VSampFactor = 1;

        /// <summary>The index of the quantization table used for this component.</summary>
        public uint QuantIdx;

        /// <summary>The width of the component measured in 8x8 blocks.</summary>
        public uint WidthInBlocks;

        /// <summary>The height of the component measured in 8x8 blocks.</summary>
        public uint HeightInBlocks;

        /// <summary>
        /// The DCT coefficients of this component, laid out block-by-block, divided
        /// through the quantization matrix values.
        /// </summary>
        public short[] Coeffs = Array.Empty<short>();
    }

    /// <summary>
    /// Type of JPEG APP marker segment.
    /// Ported from <c>jxl::jpeg::AppMarkerType</c> in <c>lib/jxl/jpeg/jpeg_data.h</c>.
    /// </summary>
    public enum AppMarkerType : uint
    {
        /// <summary>Unrecognized or generic APP marker.</summary>
        Unknown = 0,

        /// <summary>APP2 ICC profile marker.</summary>
        ICC = 1,

        /// <summary>APP1 Exif marker.</summary>
        Exif = 2,

        /// <summary>APP1 XMP marker.</summary>
        XMP = 3,
    }

    /// <summary>
    /// Represents a parsed JPEG file for bit-exact JPEG reconstruction.
    /// Ported from <c>jxl::jpeg::JPEGData</c> in <c>lib/jxl/jpeg/jpeg_data.h</c> and <c>lib/jxl/jpeg/jpeg_data.cc</c>.
    /// </summary>
    public sealed class JPEGData : Fields
    {
        private enum JPEGComponentType : uint
        {
            Gray   = 0,
            YCbCr  = 1,
            RGB    = 2,
            Custom = 3,
        }

        private struct JPEGInfo
        {
            public nuint NumAppMarkers;
            public nuint NumComMarkers;
            public nuint NumScans;
            public nuint NumIntermarker;
            public bool HasDri;
        }

        /// <summary>Image width in pixels.</summary>
        public int Width;

        /// <summary>Image height in pixels.</summary>
        public int Height;

        /// <summary>Restart interval in MCUs.</summary>
        public uint RestartInterval;

        /// <summary>APP marker payloads.</summary>
        public readonly List<byte[]> AppData = new List<byte[]>();

        /// <summary>APP marker types.</summary>
        public readonly List<AppMarkerType> AppMarkerTypes = new List<AppMarkerType>();

        /// <summary>COM marker payloads.</summary>
        public readonly List<byte[]> ComData = new List<byte[]>();

        /// <summary>Quantization tables.</summary>
        public readonly List<JPEGQuantTable> Quant = new List<JPEGQuantTable>();

        /// <summary>Huffman code tables.</summary>
        public readonly List<JPEGHuffmanCode> HuffmanCode = new List<JPEGHuffmanCode>();

        /// <summary>Image components.</summary>
        public readonly List<JPEGComponent> Components = new List<JPEGComponent>();

        /// <summary>Scan headers and extra bitstream reconstruction info.</summary>
        public readonly List<JPEGScanInfo> ScanInfo = new List<JPEGScanInfo>();

        /// <summary>Sequence of JPEG markers in file order.</summary>
        public readonly List<byte> MarkerOrder = new List<byte>();

        /// <summary>Raw bytes present between markers.</summary>
        public readonly List<byte[]> InterMarkerData = new List<byte[]>();

        /// <summary>Trailing bytes after EOI marker.</summary>
        public byte[] TailData = Array.Empty<byte>();

        /// <summary>Whether non-1 padding bits are present at byte alignment points.</summary>
        public bool HasZeroPaddingBit;

        /// <summary>Recorded padding bits (each element is 0 or 1).</summary>
        public readonly List<byte> PaddingBits = new List<byte>();

        /// <inheritdoc />
        public override string BundleName => "JPEGData";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            bool isGray = Components.Count == 1;

            if (!visitor.Bool(false, ref isGray))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                ResizeList(Components, isGray ? 1 : 3, static () => new JPEGComponent());
            }

            JPEGInfo info = default;

            if (!VisitMarkers(visitor, ref info))
            {
                return false;
            }

            if (!VisitAppAndComMarkers(visitor, in info))
            {
                return false;
            }

            if (!VisitQuantAndComponents(visitor))
            {
                return false;
            }

            if (!VisitHuffmanTables(visitor))
            {
                return false;
            }

            if (!VisitScanInfos(visitor, in info))
            {
                return false;
            }

            if (!VisitInterMarkerAndPadding(visitor, in info))
            {
                return false;
            }

            return ValidateHuffmanTableUsage();
        }

        /// <summary>
        /// Calculates the number of MCUs per row and number of MCU rows for <paramref name="scan"/>.
        /// Ported from <c>jxl::jpeg::JPEGData::CalculateMcuSize</c> in <c>lib/jxl/jpeg/jpeg_data.cc</c>.
        /// </summary>
        /// <param name="scan">Scan descriptor.</param>
        /// <param name="mcusPerRow">Outputs MCUs per row.</param>
        /// <param name="mcuRows">Outputs MCU rows.</param>
        public void CalculateMcuSize(JPEGScanInfo scan, out int mcusPerRow, out int mcuRows)
        {
            bool isInterleaved          = scan.NumComponents > 1;
            JPEGComponent baseComponent = Components[(int)scan.Components[0].CompIdx];

            // h_group / v_group act as numerators for converting number of blocks to
            // number of MCU. In interleaved mode it is 1, so MCU is represented with
            // max_*_samp_factor blocks. In non-interleaved mode we choose numerator to
            // be the sampling factor, consequently MCU is always represented with single
            // block.
            int hGroup         = isInterleaved ? 1 : baseComponent.HSampFactor;
            int vGroup         = isInterleaved ? 1 : baseComponent.VSampFactor;
            int maxHSampFactor = 1;
            int maxVSampFactor = 1;

            foreach (JPEGComponent c in Components)
            {
                maxHSampFactor = Math.Max(c.HSampFactor, maxHSampFactor);
                maxVSampFactor = Math.Max(c.VSampFactor, maxVSampFactor);
            }

            mcusPerRow = Common.DivCeil(Width * hGroup, 8 * maxHSampFactor);
            mcuRows    = Common.DivCeil(Height * vGroup, 8 * maxVSampFactor);
        }

        /// <summary>
        /// Populates ICC APP2 marker segments in <paramref name="jpegData"/> from <paramref name="icc"/>.
        /// Ported from <c>jxl::jpeg::SetJPEGDataFromICC</c> in <c>lib/jxl/jpeg/jpeg_data.cc</c>.
        /// </summary>
        /// <param name="icc">ICC profile bytes.</param>
        /// <param name="jpegData">Target <see cref="JPEGData"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status SetJPEGDataFromICC(ReadOnlySpan<byte> icc, JPEGData jpegData)
        {
            int iccPos = 0;

            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                if (jpegData.AppMarkerTypes[i] != AppMarkerType.ICC)
                {
                    continue;
                }

                byte[] app = jpegData.AppData[i];

                if (app.Length < 17)
                {
                    return Status.Failure("ICC APP marker too small");
                }

                int len = app.Length - 17;

                if (iccPos + len > icc.Length)
                {
                    return Status.Failure("ICC length is less than APP markers");
                }

                icc.Slice(iccPos, len).CopyTo(app.AsSpan(17, len));
                iccPos += len;
            }

            if (iccPos != icc.Length && iccPos != 0)
            {
                return Status.Failure("ICC length is more than APP markers");
            }

            return true;
        }

        private static Status VisitMarker(ref byte marker, Visitor visitor, ref JPEGInfo info)
        {
            uint marker32 = (uint)(marker - 0xc0);

            if (!visitor.Bits(6, 0x00, ref marker32))
            {
                return false;
            }

            marker = (byte)(marker32 + 0xc0);

            if ((marker & 0xf0) == 0xe0)
            {
                info.NumAppMarkers++;
            }

            if (marker == 0xfe)
            {
                info.NumComMarkers++;
            }

            if (marker == 0xda)
            {
                info.NumScans++;
            }

            // We use a fake 0xff marker to signal intermarker data.
            if (marker == 0xff)
            {
                info.NumIntermarker++;
            }

            if (marker == 0xdd)
            {
                info.HasDri = true;
            }

            return true;
        }

        private Status VisitMarkers(Visitor visitor, ref JPEGInfo info)
        {
            if (visitor.IsReading)
            {
                MarkerOrder.Clear();
                byte marker = 0xc0;

                do
                {
                    if (!VisitMarker(ref marker, visitor, ref info))
                    {
                        return false;
                    }

                    MarkerOrder.Add(marker);

                    if (MarkerOrder.Count > 16384)
                    {
                        return Status.Failure("Too many markers");
                    }
                }
                while (marker != 0xd9);
            }
            else
            {
                if (MarkerOrder.Count > 16384)
                {
                    return Status.Failure("Too many markers");
                }

                for (int i = 0; i < MarkerOrder.Count; i++)
                {
                    byte marker = MarkerOrder[i];

                    if (!VisitMarker(ref marker, visitor, ref info))
                    {
                        return false;
                    }

                    MarkerOrder[i] = marker;
                }

                if (MarkerOrder.Count > 0 && MarkerOrder[MarkerOrder.Count - 1] != 0xd9)
                {
                    return Status.Failure("Last marker must be EOI (0xd9)");
                }
            }

            if (info.NumScans == 0)
            {
                return Status.Failure("JPEG: no scans");
            }

            return true;
        }

        private Status VisitAppAndComMarkers(Visitor visitor, in JPEGInfo info)
        {
            if (visitor.IsReading)
            {
                ResizeList(AppData, (int)info.NumAppMarkers, static () => Array.Empty<byte>());
                ResizeList(AppMarkerTypes, (int)info.NumAppMarkers, static () => AppMarkerType.Unknown);
                ResizeList(ComData, (int)info.NumComMarkers, static () => Array.Empty<byte>());
                ResizeList(ScanInfo, (int)info.NumScans, static () => new JPEGScanInfo());
            }

            for (int i = 0; i < AppData.Count; i++)
            {
                uint markerType = (uint)AppMarkerTypes[i];

                if (!visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.BitsOffset(1, 2),
                    U32Distr.BitsOffset(2, 4),
                    0,
                    ref markerType))
                {
                    return false;
                }

                AppMarkerTypes[i] = (AppMarkerType)markerType;

                if (markerType > (uint)AppMarkerType.XMP)
                {
                    return Status.Failure("Unknown app marker type");
                }

                uint len = (uint)(AppData[i].Length - 1);

                if (!visitor.Bits(16, 0, ref len))
                {
                    return false;
                }

                if (visitor.IsReading)
                {
                    AppData[i] = new byte[len + 1];
                }

                if (AppData[i].Length < 3)
                {
                    return Status.Failure("Invalid marker size");
                }
            }

            for (int i = 0; i < ComData.Count; i++)
            {
                uint len = (uint)(ComData[i].Length - 1);

                if (!visitor.Bits(16, 0, ref len))
                {
                    return false;
                }

                if (visitor.IsReading)
                {
                    ComData[i] = new byte[len + 1];
                }

                if (ComData[i].Length < 3)
                {
                    return Status.Failure("Invalid marker size");
                }
            }

            return true;
        }

        private Status VisitQuantAndComponents(Visitor visitor)
        {
            uint numQuantTables = (uint)Quant.Count;

            if (!visitor.U32(U32Distr.Val(1), U32Distr.Val(2), U32Distr.Val(3), U32Distr.Val(4), 2, ref numQuantTables))
            {
                return false;
            }

            if (numQuantTables == 4)
            {
                return Status.Failure("Invalid number of quant tables");
            }

            if (visitor.IsReading)
            {
                ResizeList(Quant, (int)numQuantTables, static () => new JPEGQuantTable());
            }

            for (int i = 0; i < (int)numQuantTables; i++)
            {
                if (Quant[i].Precision > 1)
                {
                    return Status.Failure("Quant tables with more than 16 bits are not supported");
                }

                if (!visitor.Bits(1, 0, ref Quant[i].Precision) ||
                    !visitor.Bits(2, (uint)i, ref Quant[i].Index) ||
                    !visitor.Bool(true, ref Quant[i].IsLast))
                {
                    return false;
                }
            }

            return VisitComponentsConfig(visitor);
        }

        private Status VisitComponentsConfig(Visitor visitor)
        {
            JPEGComponentType componentType = DetermineComponentType();
            uint compTypeU32                = (uint)componentType;

            if (!visitor.Bits(2, (uint)JPEGComponentType.YCbCr, ref compTypeU32))
            {
                return false;
            }

            componentType = (JPEGComponentType)compTypeU32;
            uint numComponents;

            if (componentType == JPEGComponentType.Gray)
            {
                numComponents = 1;
            }
            else if (componentType != JPEGComponentType.Custom)
            {
                numComponents = 3;
            }
            else
            {
                numComponents = (uint)Components.Count;

                if (!visitor.U32(U32Distr.Val(1), U32Distr.Val(2), U32Distr.Val(3), U32Distr.Val(4), 3, ref numComponents))
                {
                    return false;
                }

                if (numComponents != 1 && numComponents != 3)
                {
                    return Status.Failure("Invalid number of components");
                }
            }

            if (visitor.IsReading)
            {
                ResizeList(Components, (int)numComponents, static () => new JPEGComponent());
            }

            if (!AssignOrVisitComponentIds(visitor, componentType))
            {
                return false;
            }

            return ValidateComponentQuantTables(visitor);
        }

        private JPEGComponentType DetermineComponentType()
        {
            if (Components.Count == 1 && Components[0].Id == 1)
            {
                return JPEGComponentType.Gray;
            }

            if (Components.Count == 3 && Components[0].Id == 1 && Components[1].Id == 2 && Components[2].Id == 3)
            {
                return JPEGComponentType.YCbCr;
            }

            if (Components.Count == 3 && Components[0].Id == 'R' && Components[1].Id == 'G' && Components[2].Id == 'B')
            {
                return JPEGComponentType.RGB;
            }

            return JPEGComponentType.Custom;
        }

        private Status AssignOrVisitComponentIds(Visitor visitor, JPEGComponentType componentType)
        {
            if (componentType == JPEGComponentType.Custom)
            {
                foreach (JPEGComponent component in Components)
                {
                    if (!visitor.Bits(8, 0, ref component.Id))
                    {
                        return false;
                    }
                }
            }
            else if (componentType == JPEGComponentType.Gray)
            {
                Components[0].Id = 1;
            }
            else if (componentType == JPEGComponentType.RGB)
            {
                Components[0].Id = 'R';
                Components[1].Id = 'G';
                Components[2].Id = 'B';
            }
            else
            {
                Components[0].Id = 1;
                Components[1].Id = 2;
                Components[2].Id = 3;
            }

            return true;
        }

        private Status ValidateComponentQuantTables(Visitor visitor)
        {
            uint usedTables = 0;

            for (int i = 0; i < Components.Count; i++)
            {
                if (!visitor.Bits(2, 0, ref Components[i].QuantIdx))
                {
                    return false;
                }

                if (Components[i].QuantIdx >= (uint)Quant.Count)
                {
                    return Status.Failure("Invalid quant table for component");
                }

                usedTables |= 1u << (int)Components[i].QuantIdx;
            }

            for (int i = 0; i < Quant.Count; i++)
            {
                if ((usedTables & (1u << i)) != 0)
                {
                    continue;
                }

                if (i == 0)
                {
                    return Status.Failure("First quant table unused.");
                }

                for (int j = 0; j < 64; j++)
                {
                    if (Quant[i].Values[j] != Quant[i - 1].Values[j])
                    {
                        return Status.Failure("Non-trivial unused quant table");
                    }
                }
            }

            return true;
        }

        private Status VisitHuffmanTables(Visitor visitor)
        {
            uint numHuff = (uint)HuffmanCode.Count;

            if (!visitor.U32(
                U32Distr.Val(4),
                U32Distr.BitsOffset(3, 2),
                U32Distr.BitsOffset(4, 10),
                U32Distr.BitsOffset(6, 26),
                4,
                ref numHuff))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                ResizeList(HuffmanCode, (int)numHuff, static () => new JPEGHuffmanCode());
            }

            foreach (JPEGHuffmanCode hc in HuffmanCode)
            {
                if (!VisitSingleHuffmanTable(visitor, hc))
                {
                    return false;
                }
            }

            return true;
        }

        private static unsafe Status VisitSingleHuffmanTable(Visitor visitor, JPEGHuffmanCode hc)
        {
            bool isAc = (hc.SlotId >> 4) != 0;
            uint id   = (uint)(hc.SlotId & 0xF);

            if (!visitor.Bool(false, ref isAc) ||
                !visitor.Bits(2, 0, ref id) ||
                !visitor.Bool(true, ref hc.IsLast))
            {
                return false;
            }

            hc.SlotId        = ((isAc ? 1 : 0) << 4) | (int)id;
            nuint numSymbols = 0;

            for (int i = 0; i <= 16; i++)
            {
                if (!visitor.U32(U32Distr.Val(0), U32Distr.Val(1), U32Distr.BitsOffset(3, 2), U32Distr.Bits(8), 0, ref hc.Counts[i]))
                {
                    return false;
                }

                numSymbols += hc.Counts[i];
            }

            if (numSymbols == 0)
            {
                return true;
            }

            if (numSymbols > (nuint)hc.Values.Length)
            {
                return Status.Failure("Huffman code too large");
            }

            ulong* valueSlots = stackalloc ulong[5];
            valueSlots[0] = valueSlots[1] = valueSlots[2] = valueSlots[3] = valueSlots[4] = 0;

            for (nuint i = 0; i < numSymbols; i++)
            {
                if (!visitor.U32(U32Distr.Bits(2), U32Distr.BitsOffset(2, 4), U32Distr.BitsOffset(4, 8), U32Distr.BitsOffset(8, 1), 0, ref hc.Values[i]))
                {
                    return false;
                }

                uint val               = hc.Values[i];
                valueSlots[val >> 6] |= 1UL << (int)(val & 0x3Fu);
            }

            return ValidateHuffmanSymbols(hc, numSymbols, isAc, valueSlots);
        }

        private static unsafe Status ValidateHuffmanSymbols(
            JPEGHuffmanCode hc,
            nuint numSymbols,
            bool isAc,
            ulong* valueSlots)
        {
            if (hc.Values[numSymbols - 1] != JpegConstants.KJpegHuffmanAlphabetSize)
            {
                return Status.Failure("Missing EOI symbol");
            }

            if (valueSlots[4] != 1UL)
            {
                return Status.Failure("Invalid EOI slot");
            }

            nuint numValues = 1;

            for (int i = 0; i < 4; ++i)
            {
                numValues += (nuint)BitOperations.PopCount(valueSlots[i]);
            }

            if (numValues != numSymbols)
            {
                return Status.Failure("Duplicate Huffman symbols");
            }

            if (!isAc)
            {
                bool onlyDc = ((valueSlots[0] >> JpegConstants.KJpegDCAlphabetSize) | valueSlots[1] | valueSlots[2] | valueSlots[3]) == 0;

                if (!onlyDc)
                {
                    return Status.Failure("Huffman symbols out of DC range");
                }
            }

            return true;
        }

        private Status VisitScanInfos(Visitor visitor, in JPEGInfo info)
        {
            foreach (JPEGScanInfo scan in ScanInfo)
            {
                if (!VisitSingleScanHeader(visitor, scan))
                {
                    return false;
                }
            }

            if (info.HasDri && !visitor.Bits(16, 0, ref RestartInterval))
            {
                return false;
            }

            foreach (JPEGScanInfo scan in ScanInfo)
            {
                if (!VisitSingleScanExtraInfo(visitor, scan))
                {
                    return false;
                }
            }

            return true;
        }

        private Status VisitSingleScanHeader(Visitor visitor, JPEGScanInfo scan)
        {
            if (!visitor.U32(U32Distr.Val(1), U32Distr.Val(2), U32Distr.Val(3), U32Distr.Val(4), 1, ref scan.NumComponents))
            {
                return false;
            }

            if (scan.NumComponents >= 4)
            {
                return Status.Failure("Invalid number of components in SOS marker");
            }

            if (!visitor.Bits(6, 0, ref scan.Ss) ||
                !visitor.Bits(6, 63, ref scan.Se) ||
                !visitor.Bits(4, 0, ref scan.Al) ||
                !visitor.Bits(4, 0, ref scan.Ah))
            {
                return false;
            }

            for (int i = 0; i < (int)scan.NumComponents; i++)
            {
                if (!visitor.Bits(2, 0, ref scan.Components[i].CompIdx))
                {
                    return false;
                }

                if (scan.Components[i].CompIdx >= (uint)Components.Count)
                {
                    return Status.Failure("Invalid component idx in SOS marker");
                }

                if (!visitor.Bits(2, 0, ref scan.Components[i].AcTblIdx) ||
                    !visitor.Bits(2, 0, ref scan.Components[i].DcTblIdx))
                {
                    return false;
                }
            }

            return visitor.U32(
                U32Distr.Val(0),
                U32Distr.Val(1),
                U32Distr.Val(2),
                U32Distr.BitsOffset(3, 3),
                (uint)(FrameDimensions.KMaxNumPasses - 1),
                ref scan.LastNeededPass);
        }

        private static Status VisitSingleScanExtraInfo(Visitor visitor, JPEGScanInfo scan)
        {
            uint numResetPoints = (uint)scan.ResetPoints.Count;

            if (!visitor.U32(U32Distr.Val(0), U32Distr.BitsOffset(2, 1), U32Distr.BitsOffset(4, 4), U32Distr.BitsOffset(16, 20), 0, ref numResetPoints))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                ResizeList(scan.ResetPoints, (int)numResetPoints, static () => 0u);
            }

            int lastBlockIdx = -1;

            for (int i = 0; i < scan.ResetPoints.Count; i++)
            {
                uint blockIdx = (uint)(scan.ResetPoints[i] - (uint)(lastBlockIdx + 1));

                if (!visitor.U32(U32Distr.Val(0), U32Distr.BitsOffset(3, 1), U32Distr.BitsOffset(5, 9), U32Distr.BitsOffset(28, 41), 0, ref blockIdx))
                {
                    return false;
                }

                blockIdx += (uint)(lastBlockIdx + 1);

                if (blockIdx >= (3u << 26))
                {
                    return Status.Failure("Invalid block ID");
                }

                scan.ResetPoints[i] = blockIdx;
                lastBlockIdx        = (int)blockIdx;
            }

            return VisitExtraZeroRuns(visitor, scan);
        }

        private static Status VisitExtraZeroRuns(Visitor visitor, JPEGScanInfo scan)
        {
            uint numExtraZeroRuns = (uint)scan.ExtraZeroRuns.Count;

            if (!visitor.U32(U32Distr.Val(0), U32Distr.BitsOffset(2, 1), U32Distr.BitsOffset(4, 4), U32Distr.BitsOffset(16, 20), 0, ref numExtraZeroRuns))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                ResizeList(scan.ExtraZeroRuns, (int)numExtraZeroRuns, static () => default);
            }

            int lastBlockIdx = -1;

            for (int i = 0; i < scan.ExtraZeroRuns.Count; i++)
            {
                JPEGScanInfo.ExtraZeroRunInfo info = scan.ExtraZeroRuns[i];
                uint extraZeroRuns                 = info.NumExtraZeroRuns;
                uint blockIdx                      = (uint)(info.BlockIdx - (uint)(lastBlockIdx + 1));

                if (!visitor.U32(U32Distr.Val(1), U32Distr.BitsOffset(2, 2), U32Distr.BitsOffset(4, 5), U32Distr.BitsOffset(8, 20), 1, ref extraZeroRuns) ||
                    !visitor.U32(U32Distr.Val(0), U32Distr.BitsOffset(3, 1), U32Distr.BitsOffset(5, 9), U32Distr.BitsOffset(28, 41), 0, ref blockIdx))
                {
                    return false;
                }

                blockIdx += (uint)(lastBlockIdx + 1);

                if (extraZeroRuns > 4 || blockIdx > (3u << 26))
                {
                    return Status.Failure("Invalid extra zero run info");
                }

                info.BlockIdx         = blockIdx;
                info.NumExtraZeroRuns = extraZeroRuns;
                scan.ExtraZeroRuns[i] = info;
                lastBlockIdx          = (int)blockIdx;
            }

            return true;
        }

        private Status VisitInterMarkerAndPadding(Visitor visitor, in JPEGInfo info)
        {
            uint[] interMarkerSizes = new uint[info.NumIntermarker];

            for (nuint i = 0; i < info.NumIntermarker; ++i)
            {
                uint len = visitor.IsReading ? 0u : (uint)InterMarkerData[(int)i].Length;

                if (!visitor.Bits(16, 0, ref len))
                {
                    return false;
                }

                interMarkerSizes[i] = len;
            }

            uint tailDataLen = (uint)TailData.Length;

            if (!visitor.IsReading && tailDataLen > 4260096u)
            {
                return Status.Failure("Tail data too large");
            }

            if (!visitor.U32(U32Distr.Val(0), U32Distr.BitsOffset(8, 1), U32Distr.BitsOffset(16, 257), U32Distr.BitsOffset(22, 65793), 0, ref tailDataLen) ||
                !visitor.Bool(false, ref HasZeroPaddingBit))
            {
                return false;
            }

            if (HasZeroPaddingBit && !VisitPaddingBits(visitor))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                TailData = new byte[tailDataLen];
                InterMarkerData.Clear();

                for (nuint i = 0; i < info.NumIntermarker; ++i)
                {
                    InterMarkerData.Add(new byte[interMarkerSizes[i]]);
                }
            }

            return true;
        }

        private Status VisitPaddingBits(Visitor visitor)
        {
            uint nbit = (uint)PaddingBits.Count;

            if (!visitor.Bits(24, 0, ref nbit))
            {
                return false;
            }

            if (visitor.IsReading)
            {
                if (!visitor.CheckHasEnoughBits(nbit))
                {
                    return false;
                }

                PaddingBits.Clear();

                for (uint i = 0; i < nbit; i++)
                {
                    bool bbit = false;

                    if (!visitor.Bool(false, ref bbit))
                    {
                        return false;
                    }

                    PaddingBits.Add(bbit ? (byte)1 : (byte)0);
                }
            }
            else
            {
                for (int i = 0; i < PaddingBits.Count; i++)
                {
                    bool bbit = PaddingBits[i] != 0;

                    if (!visitor.Bool(false, ref bbit))
                    {
                        return false;
                    }

                    PaddingBits[i] = bbit ? (byte)1 : (byte)0;
                }
            }

            return true;
        }

        private Status ValidateHuffmanTableUsage()
        {
            int dhtIndex       = 0;
            int scanIndex      = 0;
            bool isProgressive = false;
            Span<bool> acOk    = stackalloc bool[JpegConstants.KMaxHuffmanTables];
            Span<bool> dcOk    = stackalloc bool[JpegConstants.KMaxHuffmanTables];
            acOk.Clear();
            dcOk.Clear();

            foreach (byte marker in MarkerOrder)
            {
                if (marker == 0xC2)
                {
                    isProgressive = true;
                }
                else if (marker == 0xC4)
                {
                    MarkDefinedHuffmanTables(ref dhtIndex, acOk, dcOk);
                }
                else if (marker == 0xDA)
                {
                    JPEGScanInfo si = ScanInfo[scanIndex++];

                    if (!CheckScanHuffmanTables(si, isProgressive, dcOk, acOk))
                    {
                        return Status.Failure("Huffman table used before defined");
                    }
                }
            }

            return true;
        }

        private void MarkDefinedHuffmanTables(ref int dhtIndex, Span<bool> acOk, Span<bool> dcOk)
        {
            while (dhtIndex < HuffmanCode.Count)
            {
                JPEGHuffmanCode huff = HuffmanCode[dhtIndex++];
                int index            = huff.SlotId;

                if ((index & 0x10) != 0)
                {
                    acOk[index - 0x10] = true;
                }
                else
                {
                    dcOk[index] = true;
                }

                if (huff.IsLast)
                {
                    break;
                }
            }
        }

        private static bool CheckScanHuffmanTables(
            JPEGScanInfo si,
            bool isProgressive,
            ReadOnlySpan<bool> dcOk,
            ReadOnlySpan<bool> acOk)
        {
            for (int i = 0; i < (int)si.NumComponents; ++i)
            {
                JPEGComponentScanInfo csi = si.Components[i];
                bool wantDc               = !isProgressive || (si.Ss == 0);
                bool wantAc               = !isProgressive || (si.Ss != 0) || (si.Se != 0);

                if ((wantDc && !dcOk[(int)csi.DcTblIdx]) ||
                    (wantAc && !acOk[(int)csi.AcTblIdx]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ResizeList<T>(List<T> list, int count, Func<T> factory)
        {
            while (list.Count > count)
            {
                list.RemoveAt(list.Count - 1);
            }

            while (list.Count < count)
            {
                list.Add(factory());
            }
        }
    }

    /// <summary>
    /// Decodes a <see cref="JPEGData"/> structure from a <c>jbrd</c> JPEG reconstruction box payload.
    /// Ported from <c>jxl::jpeg::DecodeJPEGData</c> in <c>lib/jxl/jpeg/dec_jpeg_data.h</c> and <c>lib/jxl/jpeg/dec_jpeg_data.cc</c>.
    /// </summary>
    public static class JpegDataDecoder
    {
        /// <summary>
        /// Decodes the field-coded header and Brotli-compressed marker streams into <paramref name="jpegData"/>.
        /// </summary>
        /// <param name="encoded">Raw <c>jbrd</c> box content bytes.</param>
        /// <param name="jpegData">Destination <see cref="JPEGData"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeJPEGData(ReadOnlySpan<byte> encoded, JPEGData jpegData)
        {
            BitReader br = new BitReader(encoded);
            Status readOk = Bundle.Read(br, jpegData);

            if (readOk.IsOk)
            {
                readOk = br.JumpToByteBoundary();
            }

            int bytesConsumed = (int)(br.TotalBitsConsumed() / 8u);
            Status closeOk    = br.Close();

            if (!readOk.IsOk)
            {
                return readOk;
            }

            if (!closeOk.IsOk)
            {
                return closeOk;
            }

            ReadOnlySpan<byte> brotliInput = encoded.Slice(bytesConsumed);
            using BrotliDecoder brotliDec  = new BrotliDecoder();

            return DecompressMarkerData(brotliDec, brotliInput, jpegData);
        }

        private static Status DecompressMarkerData(
            BrotliDecoder brotliDec,
            ReadOnlySpan<byte> input,
            JPEGData jpegData)
        {
            int inPos     = 0;
            nuint numIcc  = 0;

            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                Status appOk = ProcessAppMarker(ref brotliDec, input, ref inPos, jpegData, i, ref numIcc);

                if (!appOk.IsOk)
                {
                    return appOk;
                }
            }

            Status tagsOk = PopulateAppMarkerTags(jpegData, (byte)numIcc);

            if (!tagsOk.IsOk)
            {
                return tagsOk;
            }

            for (int i = 0; i < jpegData.ComData.Count; i++)
            {
                byte[] marker = jpegData.ComData[i];

                if (!BrotliReadExact(ref brotliDec, input, ref inPos, marker).IsOk)
                {
                    return Status.Failure("Failed to decompress COM marker");
                }

                if ((marker[1] * 256) + marker[2] + 1 != marker.Length)
                {
                    return Status.Failure("Incorrect marker size");
                }
            }

            for (int i = 0; i < jpegData.InterMarkerData.Count; i++)
            {
                if (!BrotliReadExact(ref brotliDec, input, ref inPos, jpegData.InterMarkerData[i]).IsOk)
                {
                    return Status.Failure("Failed to decompress inter-marker data");
                }
            }

            if (!BrotliReadExact(ref brotliDec, input, ref inPos, jpegData.TailData).IsOk)
            {
                return Status.Failure("Failed to decompress tail data");
            }

            return VerifyBrotliEnd(ref brotliDec, input, inPos);
        }

        private static Status ProcessAppMarker(
            ref BrotliDecoder brotliDec,
            ReadOnlySpan<byte> input,
            ref int inPos,
            JPEGData jpegData,
            int i,
            ref nuint numIcc)
        {
            byte[] marker = jpegData.AppData[i];

            if (marker.Length < 3)
            {
                return Status.Failure("APP marker too short");
            }

            if (jpegData.AppMarkerTypes[i] != AppMarkerType.Unknown)
            {
                int sizeMinus1 = marker.Length - 1;
                marker[1]      = (byte)(sizeMinus1 >> 8);
                marker[2]      = (byte)(sizeMinus1 & 0xFF);

                if (jpegData.AppMarkerTypes[i] == AppMarkerType.ICC)
                {
                    if (marker.Length < 17)
                    {
                        return Status.Failure("ICC markers must be at least 17 bytes");
                    }

                    marker[0] = JpegConstants.KApp2;
                    JpegConstants.KIccProfileTag.CopyTo(marker.AsSpan(3, 12));
                    marker[15] = (byte)(++numIcc);
                }

                return true;
            }

            Status readOk = BrotliReadExact(ref brotliDec, input, ref inPos, marker);

            if (!readOk.IsOk)
            {
                return readOk;
            }

            if ((marker[1] * 256) + marker[2] + 1 != marker.Length)
            {
                return Status.Failure("Incorrect marker size");
            }

            return true;
        }

        private static Status PopulateAppMarkerTags(JPEGData jpegData, byte numIcc)
        {
            for (int i = 0; i < jpegData.AppData.Count; i++)
            {
                byte[] marker      = jpegData.AppData[i];
                AppMarkerType type = jpegData.AppMarkerTypes[i];

                if (type == AppMarkerType.ICC)
                {
                    marker[16] = numIcc;
                }
                else if (type == AppMarkerType.Exif)
                {
                    if (marker.Length < 3 + JpegConstants.KExifTag.Length)
                    {
                        return Status.Failure("Incorrect Exif marker size");
                    }

                    marker[0] = JpegConstants.KApp1;
                    JpegConstants.KExifTag.CopyTo(marker.AsSpan(3, JpegConstants.KExifTag.Length));
                }
                else if (type == AppMarkerType.XMP)
                {
                    if (marker.Length < 3 + JpegConstants.KXMPTag.Length)
                    {
                        return Status.Failure("Incorrect XMP marker size");
                    }

                    marker[0] = JpegConstants.KApp1;
                    JpegConstants.KXMPTag.CopyTo(marker.AsSpan(3, JpegConstants.KXMPTag.Length));
                }
            }

            return true;
        }

        private static Status BrotliReadExact(
            ref BrotliDecoder brotliDec,
            ReadOnlySpan<byte> input,
            ref int inPos,
            Span<byte> destination)
        {
            int outPos = 0;

            while (outPos < destination.Length)
            {
                OperationStatus status = brotliDec.Decompress(
                    input.Slice(inPos),
                    destination.Slice(outPos),
                    out int consumed,
                    out int written);

                inPos  += consumed;
                outPos += written;

                if (outPos < destination.Length &&
                    status != OperationStatus.DestinationTooSmall)
                {
                    return Status.Failure("Not enough decompressed output");
                }

                if (status == OperationStatus.InvalidData)
                {
                    return Status.Failure("Brotli decoding error");
                }
            }

            return true;
        }

        private static Status VerifyBrotliEnd(
            ref BrotliDecoder brotliDec,
            ReadOnlySpan<byte> input,
            int inPos)
        {
            Span<byte> sink        = stackalloc byte[1];
            OperationStatus status = brotliDec.Decompress(
                input.Slice(inPos),
                sink,
                out int consumed,
                out int written);

            inPos += consumed;

            if (written > 0 || status == OperationStatus.DestinationTooSmall)
            {
                return Status.Failure("Excess data in compressed stream");
            }

            if (status == OperationStatus.NeedMoreData)
            {
                return Status.Failure("Incomplete brotli-stream");
            }

            if (status != OperationStatus.Done)
            {
                return Status.Failure("Corrupted brotli-stream");
            }

            if (inPos != input.Length)
            {
                return Status.Failure("Unused data after brotli stream");
            }

            return true;
        }
    }

    /// <summary>
    /// Delegate used by <see cref="JpegDataWriter.WriteJpeg"/> to emit reconstructed JPEG bytes.
    /// Returns the number of bytes actually written.
    /// </summary>
    /// <param name="buf">Chunk of JPEG bytes to write.</param>
    /// <returns>Number of bytes written.</returns>
    public delegate nuint JpegOutputDelegate(ReadOnlySpan<byte> buf);

    /// <summary>
    /// Serializes a <see cref="JPEGData"/> instance and its DCT coefficients into a bit-exact JPEG file stream.
    /// Ported from <c>lib/jxl/jpeg/dec_jpeg_data_writer.h</c> and <c>lib/jxl/jpeg/dec_jpeg_data_writer.cc</c>.
    /// </summary>
    public static class JpegDataWriter
    {
        private const int KJpegPrecision           = 8;
        private const int KJpegBitWriterChunkSize  = 16384;

        private sealed class OutputChunk
        {
            public readonly byte[] Buffer;
            public int Offset;
            public int Length;

            public OutputChunk(byte[] buffer, int offset, int length)
            {
                Buffer = buffer;
                Offset = offset;
                Length = length;
            }
        }

        private sealed class HuffmanCodeTable
        {
            public readonly sbyte[] Depth = new sbyte[256];
            public readonly ushort[] Code = new ushort[256];
            public bool Initialized;

            public void InitDepths(sbyte value)
            {
                Array.Fill(Depth, value);
            }
        }

        private sealed class JpegBitWriter
        {
            public bool Healthy = true;
            public Queue<OutputChunk>? Output;
            public byte[] Data = new byte[KJpegBitWriterChunkSize];
            public int Pos;
            public ulong PutBuffer;
            public int PutBits = 64;
        }

        private sealed class DCTCodingState
        {
            public int EobRun;
            public HuffmanCodeTable? CurAcHuff;
            public readonly List<ushort> RefinementBits = new List<ushort>(64);
            public int RefinementBitsCount;
        }

        private sealed class SerializationState
        {
            public readonly Queue<OutputChunk> OutputQueue = new Queue<OutputChunk>();
            public int SectionIndex;
            public int DhtIndex;
            public int DqtIndex;
            public int AppIndex;
            public int ComIndex;
            public int DataIndex;
            public int ScanIndex;
            public readonly HuffmanCodeTable[] DcHuffTable = new HuffmanCodeTable[JpegConstants.KMaxHuffmanTables];
            public readonly HuffmanCodeTable[] AcHuffTable = new HuffmanCodeTable[JpegConstants.KMaxHuffmanTables];
            public byte[]? PadBits;
            public int PadBitsPos;
            public bool SeenDriMarker;
            public bool IsProgressive;

            public readonly JpegBitWriter Bw = new JpegBitWriter();
            public readonly short[] LastDcCoeff = new short[JpegConstants.KMaxComponents];
            public int RestartsToGo;
            public int NextRestartMarker;
            public int BlockScanIndex;
            public readonly DCTCodingState CodingState = new DCTCodingState();
            public int ExtraZeroRunsPos;
            public int NextExtraZeroRunIndex;
            public int NextResetPointPos;
            public int NextResetPoint;

            public SerializationState()
            {
                for (int i = 0; i < JpegConstants.KMaxHuffmanTables; i++)
                {
                    DcHuffTable[i] = new HuffmanCodeTable();
                    AcHuffTable[i] = new HuffmanCodeTable();
                }
            }
        }

        /// <summary>
        /// Writes a complete JPEG file represented by <paramref name="jpg"/> to <paramref name="output"/>.
        /// Ported from <c>jxl::jpeg::WriteJpeg</c> in <c>lib/jxl/jpeg/dec_jpeg_data_writer.cc</c>.
        /// </summary>
        /// <param name="jpg">Parsed JPEG data and DCT coefficients.</param>
        /// <param name="output">Output callback receiving byte chunks.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteJpeg(JPEGData jpg, JpegOutputDelegate output)
        {
            if (jpg.MarkerOrder.Count == 0)
            {
                return Status.Failure("Empty JPEG marker order");
            }

            SerializationState ss = new SerializationState();

            if (jpg.HasZeroPaddingBit)
            {
                ss.PadBits    = jpg.PaddingBits.ToArray();
                ss.PadBitsPos = 0;
            }

            EnqueueBytes(ss, new byte[] { 0xFF, 0xD8 });

            if (!FlushOutputQueue(ss, output))
            {
                return Status.NotEnoughBytes("Failed to write output");
            }

            for (ss.SectionIndex = 0; ss.SectionIndex < jpg.MarkerOrder.Count; ss.SectionIndex++)
            {
                byte marker = jpg.MarkerOrder[ss.SectionIndex];

                if (!SerializeSection(marker, ss, jpg))
                {
                    return Status.Failure("JPEG serialization error");
                }

                if (!FlushOutputQueue(ss, output))
                {
                    return Status.NotEnoughBytes("Failed to write output");
                }
            }

            if (ss.PadBits != null && ss.PadBitsPos != ss.PadBits.Length)
            {
                return Status.Failure("Invalid number of padding bits.");
            }

            return true;
        }

        private static bool FlushOutputQueue(SerializationState ss, JpegOutputDelegate output)
        {
            while (ss.OutputQueue.Count > 0)
            {
                OutputChunk chunk = ss.OutputQueue.Peek();
                nuint written     = output(chunk.Buffer.AsSpan(chunk.Offset, chunk.Length));

                if (written == 0 && chunk.Length > 0)
                {
                    return false;
                }

                chunk.Offset += (int)written;
                chunk.Length -= (int)written;

                if (chunk.Length == 0)
                {
                    ss.OutputQueue.Dequeue();
                }
            }

            return true;
        }

        private static void EnqueueBytes(SerializationState ss, byte[] bytes, int length = -1)
        {
            int actualLen = length < 0 ? bytes.Length : length;
            ss.OutputQueue.Enqueue(new OutputChunk(bytes, 0, actualLen));
        }

        private static bool SerializeSection(byte marker, SerializationState state, JPEGData jpg)
        {
            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC9 || marker == 0xCA)
            {
                return EncodeSOF(jpg, marker, state);
            }

            if (marker == 0xC4)
            {
                return EncodeDHT(jpg, state);
            }

            if (marker >= 0xD0 && marker <= 0xD7)
            {
                EnqueueBytes(state, new byte[] { 0xFF, marker });

                return true;
            }

            if (marker == 0xD9)
            {
                EnqueueBytes(state, new byte[] { 0xFF, 0xD9 });
                EnqueueBytes(state, jpg.TailData);

                return true;
            }

            if (marker == 0xDA)
            {
                return EncodeScan(jpg, state);
            }

            return SerializeMetadataMarker(marker, state, jpg);
        }

        private static bool SerializeMetadataMarker(byte marker, SerializationState state, JPEGData jpg)
        {
            if (marker == 0xDB)
            {
                return EncodeDQT(jpg, state);
            }

            if (marker == 0xDD)
            {
                state.SeenDriMarker = true;
                EnqueueBytes(
                    state,
                    new byte[]
                    {
                        0xFF,
                        0xDD,
                        0,
                        4,
                        (byte)(jpg.RestartInterval >> 8),
                        (byte)(jpg.RestartInterval & 0xFF),
                    });

                return true;
            }

            if (marker >= 0xE0 && marker <= 0xEF)
            {
                int appIndex = state.AppIndex++;

                if (appIndex >= jpg.AppData.Count)
                {
                    return false;
                }

                EnqueueBytes(state, new byte[] { 0xFF });
                EnqueueBytes(state, jpg.AppData[appIndex]);

                return true;
            }

            if (marker == 0xFE)
            {
                int comIndex = state.ComIndex++;

                if (comIndex >= jpg.ComData.Count)
                {
                    return false;
                }

                EnqueueBytes(state, new byte[] { 0xFF });
                EnqueueBytes(state, jpg.ComData[comIndex]);

                return true;
            }

            if (marker == 0xFF)
            {
                int index = state.DataIndex++;

                if (index >= jpg.InterMarkerData.Count)
                {
                    return false;
                }

                EnqueueBytes(state, jpg.InterMarkerData[index]);

                return true;
            }

            return false;
        }

        private static bool EncodeSOF(JPEGData jpg, byte marker, SerializationState state)
        {
            if (marker <= 0xC2)
            {
                state.IsProgressive = marker == 0xC2;
            }

            int nComps    = jpg.Components.Count;
            int markerLen = 8 + (3 * nComps);
            byte[] data   = new byte[markerLen + 2];
            int pos       = 0;
            data[pos++]   = 0xFF;
            data[pos++]   = marker;
            data[pos++]   = (byte)(markerLen >> 8);
            data[pos++]   = (byte)(markerLen & 0xFF);
            data[pos++]   = KJpegPrecision;
            data[pos++]   = (byte)(jpg.Height >> 8);
            data[pos++]   = (byte)(jpg.Height & 0xFF);
            data[pos++]   = (byte)(jpg.Width >> 8);
            data[pos++]   = (byte)(jpg.Width & 0xFF);
            data[pos++]   = (byte)nComps;

            for (int i = 0; i < nComps; ++i)
            {
                data[pos++]   = (byte)jpg.Components[i].Id;
                data[pos++]   = (byte)((jpg.Components[i].HSampFactor << 4) | jpg.Components[i].VSampFactor);
                int quantIdx  = (int)jpg.Components[i].QuantIdx;

                if (quantIdx >= jpg.Quant.Count)
                {
                    return false;
                }

                data[pos++] = (byte)jpg.Quant[quantIdx].Index;
            }

            EnqueueBytes(state, data);

            return true;
        }

        private static bool EncodeSOS(JPEGData jpg, JPEGScanInfo scanInfo, SerializationState state)
        {
            int nScans    = (int)scanInfo.NumComponents;
            int markerLen = 6 + (2 * nScans);
            byte[] data   = new byte[markerLen + 2];
            int pos       = 0;
            data[pos++]   = 0xFF;
            data[pos++]   = 0xDA;
            data[pos++]   = (byte)(markerLen >> 8);
            data[pos++]   = (byte)(markerLen & 0xFF);
            data[pos++]   = (byte)nScans;

            for (int i = 0; i < nScans; ++i)
            {
                JPEGComponentScanInfo si = scanInfo.Components[i];

                if (si.CompIdx >= (uint)jpg.Components.Count)
                {
                    return false;
                }

                data[pos++] = (byte)jpg.Components[(int)si.CompIdx].Id;
                data[pos++] = (byte)((si.DcTblIdx << 4) + si.AcTblIdx);
            }

            data[pos++] = (byte)scanInfo.Ss;
            data[pos++] = (byte)scanInfo.Se;
            data[pos++] = (byte)((scanInfo.Ah << 4) | scanInfo.Al);
            EnqueueBytes(state, data);

            return true;
        }

        private static bool EncodeDHT(JPEGData jpg, SerializationState state)
        {
            int markerLen = ComputeDhtMarkerLen(jpg.HuffmanCode, state.DhtIndex);
            byte[] data   = new byte[markerLen + 2];
            int pos       = 0;
            data[pos++]   = 0xFF;
            data[pos++]   = 0xC4;
            data[pos++]   = (byte)(markerLen >> 8);
            data[pos++]   = (byte)(markerLen & 0xFF);

            while (true)
            {
                int huffIndex = state.DhtIndex++;

                if (huffIndex >= jpg.HuffmanCode.Count)
                {
                    return false;
                }

                JPEGHuffmanCode huff = jpg.HuffmanCode[huffIndex];

                if (!EncodeSingleHuffmanTable(huff, state, data, ref pos, out bool isEmpty))
                {
                    return false;
                }

                if (isEmpty || huff.IsLast)
                {
                    break;
                }
            }

            EnqueueBytes(state, data);

            return true;
        }

        private static int ComputeDhtMarkerLen(List<JPEGHuffmanCode> huffmanCode, int startIdx)
        {
            int markerLen = 2;

            for (int i = startIdx; i < huffmanCode.Count; ++i)
            {
                JPEGHuffmanCode huff = huffmanCode[i];

                for (int j = 0; j < huff.Counts.Length; j++)
                {
                    markerLen += (int)huff.Counts[j];
                }

                if (markerLen == 2)
                {
                    break;
                }

                markerLen += JpegConstants.KJpegHuffmanMaxBitLength;

                if (huff.IsLast)
                {
                    break;
                }
            }

            return markerLen;
        }

        private static bool EncodeSingleHuffmanTable(
            JPEGHuffmanCode huff,
            SerializationState state,
            byte[] data,
            ref int pos,
            out bool isEmpty)
        {
            int totalCount = 0;
            int maxLength  = 0;

            for (int i = 0; i < huff.Counts.Length; ++i)
            {
                if (huff.Counts[i] != 0)
                {
                    maxLength = i;
                }

                totalCount += (int)huff.Counts[i];
            }

            if (totalCount == 0)
            {
                isEmpty = true;

                return true;
            }

            isEmpty                  = false;
            int index                = huff.SlotId;
            HuffmanCodeTable table   = (index & 0x10) != 0
                ? state.AcHuffTable[index - 0x10]
                : state.DcHuffTable[index];

            table.InitDepths(127);

            if (!BuildHuffmanCodeTable(huff, table))
            {
                return false;
            }

            table.Initialized = true;
            --totalCount;
            data[pos++] = (byte)huff.SlotId;

            for (int i = 1; i <= JpegConstants.KJpegHuffmanMaxBitLength; ++i)
            {
                data[pos++] = (byte)(i == maxLength ? huff.Counts[i] - 1u : huff.Counts[i]);
            }

            for (int i = 0; i < totalCount; ++i)
            {
                data[pos++] = (byte)huff.Values[i];
            }

            return true;
        }

        private static bool BuildHuffmanCodeTable(JPEGHuffmanCode huff, HuffmanCodeTable table)
        {
            Span<int> huffCode  = stackalloc int[JpegConstants.KJpegHuffmanAlphabetSize];
            Span<uint> huffSize = stackalloc uint[JpegConstants.KJpegHuffmanAlphabetSize + 1];
            int p               = 0;

            for (uint l = 1; l <= JpegConstants.KJpegHuffmanMaxBitLength; ++l)
            {
                int i = (int)huff.Counts[l];

                if (p + i > JpegConstants.KJpegHuffmanAlphabetSize + 1)
                {
                    return false;
                }

                while (i-- > 0)
                {
                    huffSize[p++] = l;
                }
            }

            if (p == 0)
            {
                return true;
            }

            int lastP       = p - 1;
            huffSize[lastP] = 0;
            int code        = 0;
            uint si         = huffSize[0];
            p               = 0;

            while (huffSize[p] != 0)
            {
                while (huffSize[p] == si)
                {
                    huffCode[p++] = code;
                    code++;
                }

                code <<= 1;
                si++;
            }

            for (p = 0; p < lastP; p++)
            {
                int idx          = (int)huff.Values[p];
                table.Depth[idx] = (sbyte)huffSize[p];
                table.Code[idx]  = (ushort)huffCode[p];
            }

            return true;
        }

        private static bool EncodeDQT(JPEGData jpg, SerializationState state)
        {
            int markerLen = 2;

            for (int i = state.DqtIndex; i < jpg.Quant.Count; ++i)
            {
                JPEGQuantTable table = jpg.Quant[i];
                markerLen           += 1 + ((table.Precision != 0 ? 2 : 1) * (int)FrameDimensions.KDctBlockSize);

                if (table.IsLast)
                {
                    break;
                }
            }

            byte[] data                   = new byte[markerLen + 2];
            int pos                       = 0;
            data[pos++]                   = 0xFF;
            data[pos++]                   = 0xDB;
            data[pos++]                   = (byte)(markerLen >> 8);
            data[pos++]                   = (byte)(markerLen & 0xFF);
            ReadOnlySpan<uint> naturalOrd = JpegConstants.KJPEGNaturalOrder;

            while (true)
            {
                int idx = state.DqtIndex++;

                if (idx >= jpg.Quant.Count)
                {
                    return false;
                }

                JPEGQuantTable table = jpg.Quant[idx];
                data[pos++]          = (byte)((table.Precision << 4) + table.Index);

                for (int i = 0; i < (int)FrameDimensions.KDctBlockSize; ++i)
                {
                    int val = table.Values[naturalOrd[i]];

                    if (table.Precision != 0)
                    {
                        data[pos++] = (byte)(val >> 8);
                    }

                    data[pos++] = (byte)(val & 0xFF);
                }

                if (table.IsLast)
                {
                    break;
                }
            }

            EnqueueBytes(state, data);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong HasZeroByte(ulong x)
        {
            return (x - 0x0101010101010101UL) & ~x & 0x8080808080808080UL;
        }

        private static void JpegBitWriterInit(JpegBitWriter bw, Queue<OutputChunk> outputQueue)
        {
            bw.Output    = outputQueue;
            bw.Data      = new byte[KJpegBitWriterChunkSize];
            bw.Pos       = 0;
            bw.PutBuffer = 0;
            bw.PutBits   = 64;
            bw.Healthy   = true;
        }

        private static void SwapBuffer(JpegBitWriter bw)
        {
            bw.Output!.Enqueue(new OutputChunk(bw.Data, 0, bw.Pos));
            bw.Data = new byte[KJpegBitWriterChunkSize];
            bw.Pos  = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Reserve(JpegBitWriter bw, int nBytes)
        {
            if (bw.Pos + nBytes > KJpegBitWriterChunkSize)
            {
                SwapBuffer(bw);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitByte(JpegBitWriter bw, int b)
        {
            bw.Data[bw.Pos]     = (byte)b;
            bw.Data[bw.Pos + 1] = 0;
            bw.Pos             += b != 0xFF ? 1 : 2;
        }

        private static void DischargeBitBuffer(JpegBitWriter bw, ulong bits)
        {
            bw.PutBuffer |= bits >> -bw.PutBits;

            if (HasZeroByte(~bw.PutBuffer) != 0)
            {
                EmitByte(bw, (int)((bw.PutBuffer >> 56) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 48) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 40) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 32) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 24) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 16) & 0xFF));
                EmitByte(bw, (int)((bw.PutBuffer >> 8) & 0xFF));
                EmitByte(bw, (int)(bw.PutBuffer & 0xFF));
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(bw.Data.AsSpan(bw.Pos, 8), bw.PutBuffer);
                bw.Pos += 8;
            }

            bw.PutBits  += 64;
            bw.PutBuffer = bits << bw.PutBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBits(JpegBitWriter bw, int nbits, ulong bits)
        {
            bw.PutBits -= nbits;

            if (bw.PutBits < 0)
            {
                if (nbits > 64)
                {
                    bw.PutBits += nbits;
                    bw.Healthy  = false;
                }
                else
                {
                    DischargeBitBuffer(bw, bits);
                }
            }
            else
            {
                bw.PutBuffer |= bits << bw.PutBits;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSymbol(int symbol, HuffmanCodeTable table, JpegBitWriter bw)
        {
            WriteBits(bw, table.Depth[symbol], table.Code[symbol]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSymbolBits(int symbol, HuffmanCodeTable table, JpegBitWriter bw, int nbits, ulong bits)
        {
            WriteBits(bw, nbits + table.Depth[symbol], bits | ((ulong)table.Code[symbol] << nbits));
        }

        private static void EmitMarker(JpegBitWriter bw, int marker)
        {
            Reserve(bw, 2);
            bw.Data[bw.Pos++] = 0xFF;
            bw.Data[bw.Pos++] = (byte)marker;
        }

        private static bool JumpToByteBoundary(JpegBitWriter bw, SerializationState state)
        {
            int nBits          = bw.PutBits & 7;
            byte danglingBits  = 0;
            byte padPattern;

            if (state.PadBits == null)
            {
                padPattern = (byte)((1u << nBits) - 1u);
            }
            else
            {
                padPattern = 0;

                while (nBits-- > 0)
                {
                    padPattern <<= 1;

                    if (state.PadBitsPos >= state.PadBits.Length)
                    {
                        return false;
                    }

                    byte bit      = state.PadBits[state.PadBitsPos++];
                    danglingBits |= bit;
                    padPattern   |= bit;
                }
            }

            if ((danglingBits & ~1) != 0)
            {
                return false;
            }

            Reserve(bw, 16);

            while (bw.PutBits <= 56)
            {
                int c = (int)((bw.PutBuffer >> 56) & 0xFF);
                EmitByte(bw, c);
                bw.PutBuffer <<= 8;
                bw.PutBits    += 8;
            }

            if (bw.PutBits < 64)
            {
                int padMask = 0xFF >> (64 - bw.PutBits);
                int c       = ((int)((bw.PutBuffer >> 56) & (ulong)~padMask)) | padPattern;
                EmitByte(bw, c);
            }

            bw.PutBuffer = 0;
            bw.PutBits   = 64;

            return true;
        }

        private static void JpegBitWriterFinish(JpegBitWriter bw)
        {
            if (bw.Pos == 0)
            {
                return;
            }

            bw.Output!.Enqueue(new OutputChunk(bw.Data, 0, bw.Pos));
            bw.Pos = 0;
        }

        private static void Flush(DCTCodingState s, JpegBitWriter bw)
        {
            if (s.EobRun > 0)
            {
                Reserve(bw, 16);
                int nbits  = Bits.FloorLog2Nonzero((uint)s.EobRun);
                int symbol = nbits << 4;
                WriteSymbol(symbol, s.CurAcHuff!, bw);

                if (nbits > 0)
                {
                    WriteBits(bw, nbits, (ulong)(s.EobRun & ((1 << nbits) - 1)));
                }

                s.EobRun = 0;
            }

            const int kStride = 124;
            int numWords      = s.RefinementBitsCount >> 4;
            int i             = 0;

            while (i < numWords)
            {
                int limit = Math.Min(i + kStride, numWords);
                Reserve(bw, 512);

                for (; i < limit; ++i)
                {
                    WriteBits(bw, 16, s.RefinementBits[i]);
                }
            }

            Reserve(bw, 16);
            int tail = s.RefinementBitsCount & 0xF;

            if (tail != 0)
            {
                WriteBits(bw, tail, s.RefinementBits[s.RefinementBits.Count - 1]);
            }

            s.RefinementBits.Clear();
            s.RefinementBitsCount = 0;
        }

        private static void BufferEndOfBand(
            DCTCodingState s,
            HuffmanCodeTable acHuff,
            ReadOnlySpan<int> newBitsArray,
            JpegBitWriter bw)
        {
            if (s.EobRun == 0)
            {
                s.CurAcHuff = acHuff;
            }

            ++s.EobRun;
            int newBitsCount = newBitsArray.Length;

            if (newBitsCount > 0)
            {
                ulong newBits = 0;

                for (int i = 0; i < newBitsCount; ++i)
                {
                    newBits = (newBits << 1) | (uint)newBitsArray[i];
                }

                AppendRefinementBits(s, newBits, newBitsCount);
            }

            if (s.EobRun == 0x7FFF)
            {
                Flush(s, bw);
            }
        }

        private static void AppendRefinementBits(DCTCodingState s, ulong newBits, int newBitsCount)
        {
            int tail = s.RefinementBitsCount & 0xF;

            if (tail != 0)
            {
                int stuffBitsCount = Math.Min(16 - tail, newBitsCount);
                ushort stuffBits   = (ushort)((newBits >> (newBitsCount - stuffBitsCount)) & ((1UL << stuffBitsCount) - 1UL));
                int lastIdx        = s.RefinementBits.Count - 1;
                s.RefinementBits[lastIdx] = (ushort)((s.RefinementBits[lastIdx] << stuffBitsCount) | stuffBits);
                newBitsCount          -= stuffBitsCount;
                s.RefinementBitsCount += stuffBitsCount;
            }

            while (newBitsCount >= 16)
            {
                s.RefinementBits.Add((ushort)(newBits >> (newBitsCount - 16)));
                newBitsCount          -= 16;
                s.RefinementBitsCount += 16;
            }

            if (newBitsCount > 0)
            {
                s.RefinementBits.Add((ushort)(newBits & ((1UL << newBitsCount) - 1UL)));
                s.RefinementBitsCount += newBitsCount;
            }
        }

        private static bool EncodeDCTBlockSequential(
            ReadOnlySpan<short> coeffs,
            HuffmanCodeTable dcHuff,
            HuffmanCodeTable acHuff,
            int numZeroRuns,
            ref short lastDcCoeff,
            JpegBitWriter bw)
        {
            short temp2  = coeffs[0];
            short temp   = (short)(temp2 - lastDcCoeff);
            lastDcCoeff  = temp2;
            temp2        = (short)(temp >> 15);
            temp        += temp2;
            temp2       ^= temp;

            int dcNbits = temp2 == 0 ? 0 : (Bits.FloorLog2Nonzero((uint)(ushort)temp2) + 1);
            WriteSymbol(dcNbits, dcHuff, bw);

            if (dcNbits > 0)
            {
                WriteBits(bw, dcNbits, (ulong)(temp & ((1 << dcNbits) - 1)));
            }

            short litmus                  = 0;
            short r                       = 0;
            ReadOnlySpan<uint> naturalOrd = JpegConstants.KJPEGNaturalOrder;

            for (int i = 1; i < 64; i++)
            {
                temp = coeffs[(int)naturalOrd[i]];

                if (temp == 0)
                {
                    r++;
                    continue;
                }

                temp2  = (short)(temp >> 15);
                temp  += temp2;
                temp2 ^= temp;

                while (r > 15)
                {
                    WriteSymbol(0xf0, acHuff, bw);
                    r -= 16;
                }

                litmus      |= temp2;
                int acNbits  = Bits.FloorLog2Nonzero((uint)(ushort)temp2) + 1;
                int symbol   = (r << 4) + acNbits;
                WriteSymbolBits(symbol, acHuff, bw, acNbits, (ulong)(temp & ((1 << acNbits) - 1)));
                r = 0;
            }

            for (int i = 0; i < numZeroRuns; ++i)
            {
                WriteSymbol(0xf0, acHuff, bw);
                r -= 16;
            }

            if (r > 0)
            {
                WriteSymbol(0, acHuff, bw);
            }

            return litmus >= 0;
        }

        private static bool EncodeDCTBlockProgressive(
            ReadOnlySpan<short> coeffs,
            HuffmanCodeTable dcHuff,
            HuffmanCodeTable acHuff,
            JPEGScanInfo scanInfo,
            int numZeroRuns,
            SerializationState state,
            ref short lastDcCoeff)
        {
            int ss             = (int)scanInfo.Ss;
            int se             = (int)scanInfo.Se;
            int al             = (int)scanInfo.Al;
            bool eobRunAllowed = ss > 0;
            JpegBitWriter bw   = state.Bw;

            if (ss == 0)
            {
                if (!EncodeProgressiveDc(coeffs[0], al, dcHuff, ref lastDcCoeff, bw))
                {
                    return false;
                }

                ++ss;
            }

            if (ss > se)
            {
                return true;
            }

            return EncodeProgressiveAcBand(coeffs, acHuff, ss, se, al, numZeroRuns, eobRunAllowed, state);
        }

        private static bool EncodeProgressiveDc(
            short coeff0,
            int al,
            HuffmanCodeTable dcHuff,
            ref short lastDcCoeff,
            JpegBitWriter bw)
        {
            short temp2 = (short)(coeff0 >> al);
            short temp  = (short)(temp2 - lastDcCoeff);
            lastDcCoeff = temp2;
            temp2       = temp;

            if (temp < 0)
            {
                temp = (short)(-temp);

                if (temp < 0)
                {
                    return false;
                }

                temp2--;
            }

            int nbits = temp == 0 ? 0 : (Bits.FloorLog2Nonzero((uint)temp) + 1);
            WriteSymbol(nbits, dcHuff, bw);

            if (nbits > 0)
            {
                WriteBits(bw, nbits, (ulong)(temp2 & ((1 << nbits) - 1)));
            }

            return true;
        }

        private static bool EncodeProgressiveAcBand(
            ReadOnlySpan<short> coeffs,
            HuffmanCodeTable acHuff,
            int ss,
            int se,
            int al,
            int numZeroRuns,
            bool eobRunAllowed,
            SerializationState state)
        {
            JpegBitWriter bw              = state.Bw;
            DCTCodingState codingState    = state.CodingState;
            ReadOnlySpan<uint> naturalOrd = JpegConstants.KJPEGNaturalOrder;
            int r                         = 0;

            for (int k = ss; k <= se; ++k)
            {
                short temp = coeffs[(int)naturalOrd[k]];
                short temp2;

                if (temp < 0)
                {
                    temp = (short)(-temp);

                    if (temp < 0)
                    {
                        return false;
                    }

                    temp >>= al;
                    temp2  = (short)~temp;
                }
                else
                {
                    temp >>= al;
                    temp2  = temp;
                }

                if (temp == 0)
                {
                    r++;
                    continue;
                }

                Flush(codingState, bw);

                while (r > 15)
                {
                    WriteSymbol(0xf0, acHuff, bw);
                    r -= 16;
                }

                int nbits  = Bits.FloorLog2Nonzero((uint)temp) + 1;
                int symbol = (r << 4) + nbits;
                WriteSymbol(symbol, acHuff, bw);
                WriteBits(bw, nbits, (ulong)(temp2 & ((1 << nbits) - 1)));
                r = 0;
            }

            FinishProgressiveAcBand(acHuff, numZeroRuns, eobRunAllowed, ref r, codingState, bw);

            return true;
        }

        private static void FinishProgressiveAcBand(
            HuffmanCodeTable acHuff,
            int numZeroRuns,
            bool eobRunAllowed,
            ref int r,
            DCTCodingState codingState,
            JpegBitWriter bw)
        {
            if (numZeroRuns > 0)
            {
                Flush(codingState, bw);

                for (int i = 0; i < numZeroRuns; ++i)
                {
                    WriteSymbol(0xf0, acHuff, bw);
                    r -= 16;
                }
            }

            if (r > 0)
            {
                BufferEndOfBand(codingState, acHuff, ReadOnlySpan<int>.Empty, bw);

                if (!eobRunAllowed)
                {
                    Flush(codingState, bw);
                }
            }
        }

        private static bool EncodeRefinementBits(
            ReadOnlySpan<short> coeffs,
            HuffmanCodeTable acHuff,
            JPEGScanInfo scanInfo,
            DCTCodingState codingState,
            JpegBitWriter bw)
        {
            int ss             = (int)scanInfo.Ss;
            int se             = (int)scanInfo.Se;
            int al             = (int)scanInfo.Al;
            bool eobRunAllowed = ss > 0;

            if (ss == 0)
            {
                WriteBits(bw, 1, (ulong)((coeffs[0] >> al) & 1));
                ++ss;
            }

            if (ss > se)
            {
                return true;
            }

            Span<int> absValues           = stackalloc int[64];
            ReadOnlySpan<uint> naturalOrd = JpegConstants.KJPEGNaturalOrder;
            int eob                       = 0;

            for (int k = ss; k <= se; k++)
            {
                int absVal   = Math.Abs((int)coeffs[(int)naturalOrd[k]]);
                absValues[k] = absVal >> al;

                if (absValues[k] == 1)
                {
                    eob = k;
                }
            }

            EncodeRefinementAcLoop(coeffs, acHuff, ss, se, eob, eobRunAllowed, absValues, codingState, bw);

            return true;
        }

        private static void EncodeRefinementAcLoop(
            ReadOnlySpan<short> coeffs,
            HuffmanCodeTable acHuff,
            int ss,
            int se,
            int eob,
            bool eobRunAllowed,
            ReadOnlySpan<int> absValues,
            DCTCodingState codingState,
            JpegBitWriter bw)
        {
            Span<int> refinementBits      = stackalloc int[64];
            int refinementBitsCount       = 0;
            int r                         = 0;
            ReadOnlySpan<uint> naturalOrd = JpegConstants.KJPEGNaturalOrder;

            for (int k = ss; k <= se; k++)
            {
                if (absValues[k] == 0)
                {
                    r++;
                    continue;
                }

                while (r > 15 && k <= eob)
                {
                    Flush(codingState, bw);
                    WriteSymbol(0xf0, acHuff, bw);
                    r -= 16;
                    WriteBufferedRefinementBits(bw, refinementBits.Slice(0, refinementBitsCount));
                    refinementBitsCount = 0;
                }

                if (absValues[k] > 1)
                {
                    refinementBits[refinementBitsCount++] = absValues[k] & 1;
                    continue;
                }

                Flush(codingState, bw);
                int symbol        = (r << 4) + 1;
                int newNonZeroBit = coeffs[(int)naturalOrd[k]] < 0 ? 0 : 1;
                WriteSymbol(symbol, acHuff, bw);
                WriteBits(bw, 1, (ulong)newNonZeroBit);
                WriteBufferedRefinementBits(bw, refinementBits.Slice(0, refinementBitsCount));
                refinementBitsCount = 0;
                r                   = 0;
            }

            if (r > 0 || refinementBitsCount > 0)
            {
                BufferEndOfBand(codingState, acHuff, refinementBits.Slice(0, refinementBitsCount), bw);

                if (!eobRunAllowed)
                {
                    Flush(codingState, bw);
                }
            }
        }

        private static void WriteBufferedRefinementBits(JpegBitWriter bw, ReadOnlySpan<int> bits)
        {
            for (int i = 0; i < bits.Length; ++i)
            {
                WriteBits(bw, 1, (ulong)bits[i]);
            }
        }

        private static bool EncodeScan(JPEGData jpg, SerializationState state)
        {
            JPEGScanInfo scanInfo = jpg.ScanInfo[state.ScanIndex];

            if (!EncodeSOS(jpg, scanInfo, state))
            {
                return false;
            }

            JpegBitWriterInit(state.Bw, state.OutputQueue);
            state.CodingState.EobRun              = 0;
            state.CodingState.CurAcHuff           = null;
            state.CodingState.RefinementBits.Clear();
            state.CodingState.RefinementBitsCount = 0;

            int restartInterval          = state.SeenDriMarker ? (int)jpg.RestartInterval : 0;
            state.RestartsToGo           = restartInterval;
            state.NextRestartMarker      = 0;
            state.BlockScanIndex         = 0;
            state.ExtraZeroRunsPos       = 0;
            state.NextExtraZeroRunIndex  = scanInfo.ExtraZeroRuns.Count > 0 ? (int)scanInfo.ExtraZeroRuns[0].BlockIdx : -1;
            state.NextResetPointPos      = 0;
            state.NextResetPoint         = scanInfo.ResetPoints.Count > 0 ? (int)scanInfo.ResetPoints[state.NextResetPointPos++] : -1;
            Array.Clear(state.LastDcCoeff, 0, state.LastDcCoeff.Length);

            int mode = ChooseScanMode(scanInfo, state.IsProgressive);
            jpg.CalculateMcuSize(scanInfo, out int mcusPerRow, out int mcuRows);

            if (!EncodeScanMcus(jpg, scanInfo, state, mode, restartInterval, mcusPerRow, mcuRows))
            {
                return false;
            }

            Flush(state.CodingState, state.Bw);

            if (!JumpToByteBoundary(state.Bw, state))
            {
                return false;
            }

            JpegBitWriterFinish(state.Bw);
            state.ScanIndex++;

            return state.Bw.Healthy;
        }

        private static int ChooseScanMode(JPEGScanInfo scanInfo, bool isProgressive)
        {
            int al = isProgressive ? (int)scanInfo.Al : 0;
            int ah = isProgressive ? (int)scanInfo.Ah : 0;
            int ss = isProgressive ? (int)scanInfo.Ss : 0;
            int se = isProgressive ? (int)scanInfo.Se : 63;

            if (!isProgressive || (ah == 0 && al == 0 && ss == 0 && se == 63))
            {
                return 0;
            }

            return ah == 0 ? 1 : 2;
        }

        private static bool EncodeScanMcus(
            JPEGData jpg,
            JPEGScanInfo scanInfo,
            SerializationState state,
            int mode,
            int restartInterval,
            int mcusPerRow,
            int mcuRows)
        {
            for (int mcuY = 0; mcuY < mcuRows; ++mcuY)
            {
                for (int mcuX = 0; mcuX < mcusPerRow; ++mcuX)
                {
                    if (restartInterval > 0 && state.RestartsToGo == 0)
                    {
                        if (!EmitRestartMarker(state, restartInterval))
                        {
                            return false;
                        }
                    }

                    if (!EncodeSingleMcu(jpg, scanInfo, state, mode, mcuX, mcuY))
                    {
                        return false;
                    }

                    --state.RestartsToGo;
                }
            }

            return true;
        }

        private static bool EmitRestartMarker(SerializationState state, int restartInterval)
        {
            Flush(state.CodingState, state.Bw);

            if (!JumpToByteBoundary(state.Bw, state))
            {
                return false;
            }

            EmitMarker(state.Bw, 0xD0 + state.NextRestartMarker);
            state.NextRestartMarker = (state.NextRestartMarker + 1) & 0x7;
            state.RestartsToGo      = restartInterval;
            Array.Clear(state.LastDcCoeff, 0, state.LastDcCoeff.Length);

            return true;
        }

        private static bool EncodeSingleMcu(
            JPEGData jpg,
            JPEGScanInfo scanInfo,
            SerializationState state,
            int mode,
            int mcuX,
            int mcuY)
        {
            bool isInterleaved = scanInfo.NumComponents > 1;
            int ss             = state.IsProgressive ? (int)scanInfo.Ss : 0;
            int se             = state.IsProgressive ? (int)scanInfo.Se : 63;
            bool wantDc        = ss == 0;
            bool wantAc        = (ss != 0) || (se != 0);

            for (int i = 0; i < (int)scanInfo.NumComponents; ++i)
            {
                JPEGComponentScanInfo si = scanInfo.Components[i];
                JPEGComponent c          = jpg.Components[(int)si.CompIdx];
                HuffmanCodeTable dcHuff  = state.DcHuffTable[si.DcTblIdx];
                HuffmanCodeTable acHuff  = state.AcHuffTable[si.AcTblIdx];

                if ((wantDc && !dcHuff.Initialized) || (wantAc && !acHuff.Initialized))
                {
                    return false;
                }

                int nBlocksY = isInterleaved ? c.VSampFactor : 1;
                int nBlocksX = isInterleaved ? c.HSampFactor : 1;

                for (int iy = 0; iy < nBlocksY; ++iy)
                {
                    for (int ix = 0; ix < nBlocksX; ++ix)
                    {
                        int blockY   = (mcuY * nBlocksY) + iy;
                        int blockX   = (mcuX * nBlocksX) + ix;
                        int blockIdx = (blockY * (int)c.WidthInBlocks) + blockX;

                        if (!EncodeSingleBlockInMcu(c, si.CompIdx, dcHuff, acHuff, scanInfo, state, mode, blockIdx))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool EncodeSingleBlockInMcu(
            JPEGComponent c,
            uint compIdx,
            HuffmanCodeTable dcHuff,
            HuffmanCodeTable acHuff,
            JPEGScanInfo scanInfo,
            SerializationState state,
            int mode,
            int blockIdx)
        {
            if (state.BlockScanIndex == state.NextResetPoint)
            {
                Flush(state.CodingState, state.Bw);
                state.NextResetPoint = state.NextResetPointPos < scanInfo.ResetPoints.Count
                    ? (int)scanInfo.ResetPoints[state.NextResetPointPos++]
                    : -1;
            }

            int numZeroRuns = 0;

            if (state.BlockScanIndex == state.NextExtraZeroRunIndex)
            {
                numZeroRuns = (int)scanInfo.ExtraZeroRuns[state.ExtraZeroRunsPos].NumExtraZeroRuns;
                ++state.ExtraZeroRunsPos;
                state.NextExtraZeroRunIndex = state.ExtraZeroRunsPos < scanInfo.ExtraZeroRuns.Count
                    ? (int)scanInfo.ExtraZeroRuns[state.ExtraZeroRunsPos].BlockIdx
                    : -1;
            }

            ReadOnlySpan<short> coeffs = c.Coeffs.AsSpan(blockIdx << 6, 64);
            Reserve(state.Bw, 512);
            bool ok;

            if (mode == 0)
            {
                ok = EncodeDCTBlockSequential(coeffs, dcHuff, acHuff, numZeroRuns, ref state.LastDcCoeff[compIdx], state.Bw);
            }
            else if (mode == 1)
            {
                ok = EncodeDCTBlockProgressive(coeffs, dcHuff, acHuff, scanInfo, numZeroRuns, state, ref state.LastDcCoeff[compIdx]);
            }
            else
            {
                ok = EncodeRefinementBits(coeffs, acHuff, scanInfo, state.CodingState, state.Bw);
            }

            ++state.BlockScanIndex;

            return ok;
        }
    }

    /// <summary>
    /// Tracks decoder state needed to parse a <c>jbrd</c> JPEG reconstruction box and write reconstructed JPEG bytes.
    /// Ported from <c>jxl::JxlToJpegDecoder</c> in <c>lib/jxl/decode_to_jpeg.h</c> and <c>lib/jxl/decode_to_jpeg.cc</c>.
    /// </summary>
    public sealed unsafe class JxlToJpegDecoder
    {
        private readonly List<byte> _buffer = new List<byte>();
        private JPEGData? _jpegData;
        private bool _insideBox;
        private bool _boxUntilEof;
        private nuint _boxSize;
        private byte* _nextOut;
        private nuint _availSize;

        /// <summary>Gets a value indicating whether a JPEG output buffer is currently set.</summary>
        public bool IsOutputSet => _nextOut != null;

        /// <summary>Gets a value indicating whether the decoder is currently parsing a JPEG reconstruction box.</summary>
        public bool IsParsingBox => _insideBox;

        /// <summary>
        /// Sets the output buffer used when producing JPEG output.
        /// </summary>
        /// <param name="data">Pointer to output buffer.</param>
        /// <param name="size">Size of output buffer in bytes.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public JxlDecoderStatus SetOutputBuffer(byte* data, nuint size)
        {
            if (_nextOut != null)
            {
                return JxlDecoderStatus.Error;
            }

            _nextOut   = data;
            _availSize = size;

            return JxlDecoderStatus.Success;
        }

        /// <summary>
        /// Releases the buffer set with <see cref="SetOutputBuffer"/> and returns the number of remaining unused bytes.
        /// </summary>
        /// <returns>Remaining available bytes in the buffer.</returns>
        public nuint ReleaseOutputBuffer()
        {
            nuint result = _availSize;
            _nextOut     = null;
            _availSize   = 0;

            return result;
        }

        /// <summary>
        /// Begins parsing a new <c>jbrd</c> JPEG reconstruction box.
        /// </summary>
        /// <param name="boxUntilEof">Whether the box extends to EOF.</param>
        /// <param name="contentsSize">Size of the box contents in bytes.</param>
        public void StartBox(bool boxUntilEof, nuint contentsSize)
        {
            _buffer.Clear();
            _insideBox   = true;
            _boxUntilEof = boxUntilEof;

            if (!boxUntilEof)
            {
                _boxSize = contentsSize;
            }
        }

        /// <summary>
        /// Consumes bytes from <paramref name="nextIn"/>/<paramref name="availIn"/> to decode the <c>jbrd</c> box.
        /// </summary>
        /// <param name="nextIn">Input pointer reference.</param>
        /// <param name="availIn">Available input byte count reference.</param>
        /// <returns>Decoder status code.</returns>
        public JxlDecoderStatus Process(ref byte* nextIn, ref nuint availIn)
        {
            if (!_insideBox)
            {
                return JxlDecoderStatus.Error;
            }

            nuint toConsume = _boxUntilEof
                ? availIn
                : Math.Min(availIn, _boxSize - (nuint)_buffer.Count);

            ReadOnlySpan<byte> incoming = new ReadOnlySpan<byte>(nextIn, (int)toConsume);
            nextIn                     += toConsume;
            availIn                    -= toConsume;

            bool oldDataExists = _buffer.Count > 0;
            byte[]? decodeArray = null;
            ReadOnlySpan<byte> toDecode;

            if (oldDataExists)
            {
                AppendSpan(_buffer, incoming);
                decodeArray = _buffer.ToArray();
                toDecode    = decodeArray;
            }
            else
            {
                toDecode = incoming;
            }

            if (!_boxUntilEof && (nuint)toDecode.Length > _boxSize)
            {
                return JxlDecoderStatus.Error;
            }

            if (_boxUntilEof || (nuint)toDecode.Length == _boxSize)
            {
                _jpegData     = new JPEGData();
                Status status = JpegDataDecoder.DecodeJPEGData(toDecode, _jpegData);

                if (status.IsFatalError())
                {
                    return JxlDecoderStatus.Error;
                }

                if (status.IsOk)
                {
                    _insideBox = false;

                    return JxlDecoderStatus.JpegReconstruction;
                }

                if (!_boxUntilEof)
                {
                    return JxlDecoderStatus.Error;
                }
            }

            if (!oldDataExists)
            {
                AppendSpan(_buffer, incoming);
            }

            return JxlDecoderStatus.NeedMoreInput;
        }

        /// <summary>Gets the decoded <see cref="JPEGData"/> if available.</summary>
        /// <returns>The decoded <see cref="JPEGData"/> or <c>null</c>.</returns>
        public JPEGData? GetJpegData() => _jpegData;

        /// <summary>Returns the number of Exif APP1 markers in <paramref name="jpegData"/>.</summary>
        /// <param name="jpegData">Parsed JPEG data.</param>
        /// <returns>Count of Exif markers.</returns>
        public static nuint NumExifMarkers(JPEGData jpegData)
        {
            nuint num = 0;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.Exif)
                {
                    num++;
                }
            }

            return num;
        }

        /// <summary>Returns the number of XMP APP1 markers in <paramref name="jpegData"/>.</summary>
        /// <param name="jpegData">Parsed JPEG data.</param>
        /// <returns>Count of XMP markers.</returns>
        public static nuint NumXmpMarkers(JPEGData jpegData)
        {
            nuint num = 0;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.XMP)
                {
                    num++;
                }
            }

            return num;
        }

        /// <summary>Gets the expected Exif box content size for <paramref name="jpegData"/>.</summary>
        /// <param name="jpegData">Parsed JPEG data.</param>
        /// <param name="size">Outputs expected Exif box content size.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public static JxlDecoderStatus ExifBoxContentSize(JPEGData jpegData, out nuint size)
        {
            size        = 0;
            int tagSize = JpegConstants.KExifTag.Length;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.Exif)
                {
                    if (jpegData.AppData[i].Length < 3 + tagSize)
                    {
                        return JxlDecoderStatus.Error;
                    }

                    size = (nuint)(jpegData.AppData[i].Length + 4 - 3 - tagSize);

                    return JxlDecoderStatus.Success;
                }
            }

            return JxlDecoderStatus.Error;
        }

        /// <summary>Gets the expected XML/XMP box content size for <paramref name="jpegData"/>.</summary>
        /// <param name="jpegData">Parsed JPEG data.</param>
        /// <param name="size">Outputs expected XML box content size.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public static JxlDecoderStatus XmlBoxContentSize(JPEGData jpegData, out nuint size)
        {
            size        = 0;
            int tagSize = JpegConstants.KXMPTag.Length;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.XMP)
                {
                    if (jpegData.AppData[i].Length < 3 + tagSize)
                    {
                        return JxlDecoderStatus.Error;
                    }

                    size = (nuint)(jpegData.AppData[i].Length - 3 - tagSize);

                    return JxlDecoderStatus.Success;
                }
            }

            return JxlDecoderStatus.Error;
        }

        /// <summary>Copies Exif box contents into the corresponding JPEG APP1 marker in <paramref name="jpegData"/>.</summary>
        /// <param name="data">Exif box content bytes (including 4-byte TIFF header offset).</param>
        /// <param name="jpegData">Target JPEG data.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public static JxlDecoderStatus SetExif(ReadOnlySpan<byte> data, JPEGData jpegData)
        {
            int tagSize = JpegConstants.KExifTag.Length;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.Exif)
                {
                    if (data.Length < 4 || jpegData.AppData[i].Length != data.Length + 3 + tagSize - 4)
                    {
                        return JxlDecoderStatus.Error;
                    }

                    jpegData.AppData[i][0] = JpegConstants.KApp1;
                    JpegConstants.KExifTag.CopyTo(jpegData.AppData[i].AsSpan(3, tagSize));
                    data.Slice(4).CopyTo(jpegData.AppData[i].AsSpan(3 + tagSize));

                    return JxlDecoderStatus.Success;
                }
            }

            return JxlDecoderStatus.Error;
        }

        /// <summary>Copies XMP box contents into the corresponding JPEG APP1 marker in <paramref name="jpegData"/>.</summary>
        /// <param name="data">XMP box content bytes.</param>
        /// <param name="jpegData">Target JPEG data.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/> or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public static JxlDecoderStatus SetXmp(ReadOnlySpan<byte> data, JPEGData jpegData)
        {
            int tagSize = JpegConstants.KXMPTag.Length;

            for (int i = 0; i < jpegData.AppData.Count; ++i)
            {
                if (jpegData.AppMarkerTypes[i] == AppMarkerType.XMP)
                {
                    if (jpegData.AppData[i].Length != data.Length + 3 + tagSize)
                    {
                        return JxlDecoderStatus.Error;
                    }

                    jpegData.AppData[i][0] = JpegConstants.KApp1;
                    JpegConstants.KXMPTag.CopyTo(jpegData.AppData[i].AsSpan(3, tagSize));
                    data.CopyTo(jpegData.AppData[i].AsSpan(3 + tagSize));

                    return JxlDecoderStatus.Success;
                }
            }

            return JxlDecoderStatus.Error;
        }

        /// <summary>
        /// Transfers the decoded <see cref="JPEGData"/> to <paramref name="ib"/> and populates its ICC profile.
        /// </summary>
        /// <param name="ib">Target <see cref="ImageBundle"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetImageBundleJpegData(ImageBundle ib)
        {
            if (IsOutputSet && _jpegData != null)
            {
                ReadOnlySpan<byte> icc = ib.Metadata != null ? ib.Metadata.ColorEncoding.ICC() : ReadOnlySpan<byte>.Empty;

                if (!JPEGData.SetJPEGDataFromICC(icc, _jpegData))
                {
                    return false;
                }

                ib.JpegData = _jpegData;
                _jpegData   = null;
            }

            return true;
        }

        /// <summary>
        /// Serializes <paramref name="jpegData"/> into the configured output buffer.
        /// </summary>
        /// <param name="jpegData">JPEG data to write.</param>
        /// <returns><see cref="JxlDecoderStatus.Success"/>, <see cref="JxlDecoderStatus.JpegNeedMoreOutput"/>, or <see cref="JxlDecoderStatus.Error"/>.</returns>
        public JxlDecoderStatus WriteOutput(JPEGData jpegData)
        {
            byte* tmpNextOut   = _nextOut;
            nuint tmpAvailSize = _availSize;

            nuint WriteChunk(ReadOnlySpan<byte> buf)
            {
                nuint toWrite = Math.Min(tmpAvailSize, (nuint)buf.Length);

                if (toWrite != 0)
                {
                    buf.Slice(0, (int)toWrite).CopyTo(new Span<byte>(tmpNextOut, (int)toWrite));
                }

                tmpNextOut   += toWrite;
                tmpAvailSize -= toWrite;

                return toWrite;
            }

            Status writeResult = JpegDataWriter.WriteJpeg(jpegData, WriteChunk);

            if (!writeResult.IsOk)
            {
                if (tmpAvailSize == 0)
                {
                    return JxlDecoderStatus.JpegNeedMoreOutput;
                }

                return JxlDecoderStatus.Error;
            }

            _nextOut   = tmpNextOut;
            _availSize = tmpAvailSize;

            return JxlDecoderStatus.Success;
        }

        private static void AppendSpan(List<byte> dst, ReadOnlySpan<byte> src)
        {
            for (int i = 0; i < src.Length; i++)
            {
                dst.Add(src[i]);
            }
        }
    }
}
