// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Extras;
using JxlSharp.Tools;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Inspects JPEG XL files and byte buffers to extract all codestream and container properties.
    /// </summary>
    public sealed class JxlPropertyInspector
    {
        /// <summary>
        /// Inspects a JPEG XL file on disk and returns a populated <see cref="JxlPropertyReport"/>.
        /// </summary>
        /// <param name="filePath">Path to the JXL file.</param>
        /// <returns>Populated <see cref="JxlPropertyReport"/>.</returns>
        public JxlPropertyReport InspectFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                JxlPropertyReport invalid = new JxlPropertyReport
                {
                    SourceLabel = filePath ?? string.Empty,
                    IsValid     = false,
                };

                invalid.BuildPropertyRows();

                return invalid;
            }

            byte[] data              = File.ReadAllBytes(filePath);
            JxlPropertyReport report = InspectBytes(data, filePath);

            using StringWriter stdOut = new StringWriter();
            using StringWriter stdErr = new StringWriter();

            int exitCode = JxlInfoTool.Run(new[] { "-v", filePath }, stdOut, stdErr);

            if (exitCode == 0)
            {
                report.VerboseInfoText = stdOut.ToString();
            }

            return report;
        }

        /// <summary>
        /// Inspects a JPEG XL byte buffer and returns a populated <see cref="JxlPropertyReport"/>.
        /// </summary>
        /// <param name="data">JPEG XL encoded bytes.</param>
        /// <param name="sourceLabel">Optional source label or filename.</param>
        /// <returns>Populated <see cref="JxlPropertyReport"/>.</returns>
        public JxlPropertyReport InspectBytes(ReadOnlySpan<byte> data, string? sourceLabel = null)
        {
            JxlSignature signature = JxlDecoder.CheckSignature(data);

            JxlPropertyReport report = new JxlPropertyReport
            {
                SourceLabel = sourceLabel ?? "(in-memory JXL)",
                FileSize    = data.Length,
                Signature   = signature,
                IsValid     = signature == JxlSignature.Codestream || signature == JxlSignature.Container,
            };

            if (!report.IsValid)
            {
                report.BuildPropertyRows();

                return report;
            }

            byte[] buffer = data.ToArray();

            DecodeCodestreamProperties(buffer, report);

            if (signature == JxlSignature.Container)
            {
                ParseContainerBoxes(data, report);
            }

            PopulateInMemoryVerboseInfo(buffer, report);
            report.BuildPropertyRows();

            return report;
        }

        private static void DecodeCodestreamProperties(byte[] data, JxlPropertyReport report)
        {
            using JxlDecoder dec = new JxlDecoder();

            dec.SetKeepOrientation(true);
            dec.SetCoalescing(false);

            JxlDecoderStatus subStatus = dec.SubscribeEvents(
                JxlDecoderStatus.BasicInfo |
                JxlDecoderStatus.ColorEncoding |
                JxlDecoderStatus.Frame);

            if (subStatus != JxlDecoderStatus.Success)
            {
                report.IsValid = false;

                return;
            }

            dec.SetInput(data);
            dec.CloseInput();
            ProcessDecoderEvents(dec, report);
        }

        private static void ProcessDecoderEvents(JxlDecoder dec, JxlPropertyReport report)
        {
            float totalDuration = 0.0f;
            int frameIndex      = 0;

            while (true)
            {
                JxlDecoderStatus status = dec.ProcessInput();

                if (status == JxlDecoderStatus.Success ||
                    status == JxlDecoderStatus.Error ||
                    status == JxlDecoderStatus.NeedMoreInput)
                {
                    break;
                }

                HandleSingleDecoderEvent(dec, status, report, ref frameIndex, ref totalDuration);
            }

            report.AnimationDurationSeconds = totalDuration;
        }

        private static void HandleSingleDecoderEvent(
            JxlDecoder dec,
            JxlDecoderStatus status,
            JxlPropertyReport report,
            ref int frameIndex,
            ref float totalDuration)
        {
            if (status == JxlDecoderStatus.BasicInfo &&
                dec.GetBasicInfo(out JxlBasicInfo info) == JxlDecoderStatus.Success)
            {
                report.BasicInfo = info;
                ReadExtraChannels(dec, in info, report);

                return;
            }

            if (status == JxlDecoderStatus.ColorEncoding)
            {
                ReadColorEncodingInfo(dec, report);

                return;
            }

            if (status == JxlDecoderStatus.Frame &&
                dec.GetFrameHeader(out JxlFrameHeader header) == JxlDecoderStatus.Success)
            {
                AddFrameSummary(dec, in header, report, ref frameIndex, ref totalDuration);
            }
        }

        private static void ReadExtraChannels(
            JxlDecoder dec,
            in JxlBasicInfo info,
            JxlPropertyReport report)
        {
            for (uint i = 0; i < info.NumExtraChannels; i++)
            {
                if (dec.GetExtraChannelInfo(i, out JxlExtraChannelInfo ec) != JxlDecoderStatus.Success)
                {
                    continue;
                }

                string name = ReadExtraChannelName(dec, i, ec.NameLength);

                report.ExtraChannels.Add(new JxlExtraChannelSummary(i, name, ec));
            }
        }

        private static string ReadExtraChannelName(JxlDecoder dec, uint index, uint nameLength)
        {
            if (nameLength == 0)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[nameLength + 1];

            if (dec.GetExtraChannelName(index, buffer) != JxlDecoderStatus.Success)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buffer, 0, (int)nameLength);
        }

        private static void ReadColorEncodingInfo(JxlDecoder dec, JxlPropertyReport report)
        {
            if (dec.GetColorAsEncodedProfile(JxlColorProfileTarget.Original, out JxlColorEncoding ce) == JxlDecoderStatus.Success)
            {
                report.HasColorEncoding       = true;
                report.ColorEncoding          = ce;
                report.ColorDescriptionString = ColorDescription.Description(in ce);
            }

            if (dec.GetICCProfileSize(JxlColorProfileTarget.Original, out nuint origSize) == JxlDecoderStatus.Success)
            {
                report.OriginalIccSize = origSize;
            }

            if (dec.GetICCProfileSize(JxlColorProfileTarget.Data, out nuint dataSize) == JxlDecoderStatus.Success)
            {
                report.DataIccSize = dataSize;
            }
        }

        private static void AddFrameSummary(
            JxlDecoder dec,
            in JxlFrameHeader header,
            JxlPropertyReport report,
            ref int frameIndex,
            ref float totalDuration)
        {
            string frameName  = ReadFrameName(dec, header.NameLength);
            float durationMs  = 0.0f;
            JxlBasicInfo info = report.BasicInfo;

            if (info.HaveAnimation && info.Animation.TpsNumerator > 0)
            {
                float seconds = header.Duration * (float)info.Animation.TpsDenominator / info.Animation.TpsNumerator;

                totalDuration += seconds;
                durationMs     = seconds * 1000.0f;
            }

            report.Frames.Add(new JxlFrameSummary(frameIndex, frameName, header, durationMs));
            frameIndex++;
        }

        private static string ReadFrameName(JxlDecoder dec, uint nameLength)
        {
            if (nameLength == 0)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[nameLength + 1];

            if (dec.GetFrameName(buffer) != JxlDecoderStatus.Success)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buffer, 0, (int)nameLength);
        }

        private static void ParseContainerBoxes(ReadOnlySpan<byte> data, JxlPropertyReport report)
        {
            int pos      = 0;
            int boxIndex = 0;

            while (TryReadIsoBox(data, ref pos, out string boxType, out ReadOnlySpan<byte> payload))
            {
                string innerType = string.Empty;

                if (boxType == "brob" && payload.Length >= 4)
                {
                    innerType = Encoding.ASCII.GetString(payload.Slice(0, 4));
                }

                if (boxType == "jbrd" || innerType == "jbrd")
                {
                    report.HasJpegReconstruction = true;
                }

                string description = DescribeContainerBox(boxType, innerType, payload);

                report.Boxes.Add(new JxlContainerBoxSummary(boxIndex, boxType, innerType, payload.Length, description));
                boxIndex++;
            }
        }

        private static string DescribeContainerBox(string boxType, string innerType, ReadOnlySpan<byte> payload)
        {
            if (boxType == "JXL ")
            {
                return "JPEG XL container signature box";
            }

            if (boxType == "ftyp")
            {
                return "File type box (ISO BMFF)";
            }

            if (boxType == "jxll")
            {
                return payload.Length > 0 ? $"Codestream level {payload[0]}" : "Codestream level box";
            }

            if (boxType == "jxlc")
            {
                return $"Complete JPEG XL codestream ({payload.Length} bytes)";
            }

            if (boxType == "jxlp")
            {
                return $"Partial JPEG XL codestream ({payload.Length} bytes)";
            }

            if (boxType == "jbrd")
            {
                return $"Lossless JPEG bitstream reconstruction data ({payload.Length} bytes)";
            }

            return DescribeMetadataOrCustomBox(boxType, innerType, payload);
        }

        private static string DescribeMetadataOrCustomBox(string boxType, string innerType, ReadOnlySpan<byte> payload)
        {
            if (boxType == "brob")
            {
                return $"Brotli-compressed '{innerType}' box ({Math.Max(0, payload.Length - 4)} compressed bytes)";
            }

            if (boxType == "Exif")
            {
                return $"Uncompressed Exif metadata ({payload.Length} bytes)";
            }

            if (boxType == "xml ")
            {
                return $"Uncompressed XMP/XML metadata ({payload.Length} bytes)";
            }

            if (boxType == "jumb")
            {
                return $"Uncompressed JUMBF superbox ({payload.Length} bytes)";
            }

            if (boxType == "jhgm")
            {
                return DescribeGainMapBox(payload);
            }

            return $"Container box '{boxType}' ({payload.Length} bytes)";
        }

        private static string DescribeGainMapBox(ReadOnlySpan<byte> payload)
        {
            if (GainMapCodec.JxlGainMapReadBundle(out JxlGainMapBundle bundle, payload, out _))
            {
                return $"ISO 21496-1 Gain Map (version {bundle.JhgmVersion}, {bundle.GainMap.Length} gain map bytes)";
            }

            return $"Gain map box ({payload.Length} bytes)";
        }

        private static bool TryReadIsoBox(
            ReadOnlySpan<byte> data,
            ref int pos,
            out string boxType,
            out ReadOnlySpan<byte> payload)
        {
            boxType = string.Empty;
            payload = ReadOnlySpan<byte>.Empty;

            if (pos + 8 > data.Length)
            {
                return false;
            }

            uint size32   = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
            boxType       = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            int headerLen = 8;
            ulong boxSize = size32;

            if (size32 == 1)
            {
                if (pos + 16 > data.Length)
                {
                    return false;
                }

                boxSize   = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(pos + 8, 8));
                headerLen = 16;
            }
            else if (size32 == 0)
            {
                boxSize = (ulong)(data.Length - pos);
            }

            if (boxSize < (ulong)headerLen || pos + (long)boxSize > data.Length)
            {
                return false;
            }

            int payloadLen = (int)boxSize - headerLen;

            payload = data.Slice(pos + headerLen, payloadLen);
            pos    += (int)boxSize;

            return true;
        }

        private static void PopulateInMemoryVerboseInfo(byte[] data, JxlPropertyReport report)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"jxl_inspect_{Guid.NewGuid():N}.jxl");

            try
            {
                File.WriteAllBytes(tempPath, data);

                using StringWriter stdOut = new StringWriter();
                using StringWriter stdErr = new StringWriter();

                int code = JxlInfoTool.Run(new[] { "-v", tempPath }, stdOut, stdErr);

                if (code == 0)
                {
                    report.VerboseInfoText = stdOut.ToString();
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
