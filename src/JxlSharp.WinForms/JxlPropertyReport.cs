// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.Globalization;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Represents a single categorized property row for displaying JXL metadata in the UI.
    /// </summary>
    /// <param name="Category">Metadata category (e.g., Basic Info, Color Encoding, Frame, Box).</param>
    /// <param name="Name">Property name.</param>
    /// <param name="Value">Formatted property value.</param>
    public readonly record struct JxlPropertyRow(string Category, string Name, string Value);

    /// <summary>
    /// Summarizes a single extra channel in a JPEG XL codestream.
    /// </summary>
    /// <param name="Index">Zero-based extra channel index.</param>
    /// <param name="Name">Optional UTF-8 channel name.</param>
    /// <param name="Info">Underlying <see cref="JxlExtraChannelInfo"/> structure.</param>
    public readonly record struct JxlExtraChannelSummary(uint Index, string Name, JxlExtraChannelInfo Info);

    /// <summary>
    /// Summarizes a single frame header in a JPEG XL codestream.
    /// </summary>
    /// <param name="Index">Zero-based frame index.</param>
    /// <param name="Name">Optional UTF-8 frame name.</param>
    /// <param name="Header">Underlying <see cref="JxlFrameHeader"/> structure.</param>
    /// <param name="DurationMs">Computed frame duration in milliseconds when animation is enabled.</param>
    public readonly record struct JxlFrameSummary(int Index, string Name, JxlFrameHeader Header, float DurationMs);

    /// <summary>
    /// Summarizes a single ISO BMFF container box in a JPEG XL container file.
    /// </summary>
    /// <param name="Index">Zero-based box index.</param>
    /// <param name="BoxType">Four-character box type string.</param>
    /// <param name="InnerBoxType">Unwrapped inner four-character type when <paramref name="BoxType"/> is <c>brob</c>.</param>
    /// <param name="PayloadLength">Payload length in bytes.</param>
    /// <param name="Description">Human-readable box description.</param>
    public readonly record struct JxlContainerBoxSummary(
        int Index,
        string BoxType,
        string InnerBoxType,
        int PayloadLength,
        string Description);

    /// <summary>
    /// Aggregates all inspected properties of a JPEG XL image for display in the WinForms UI.
    /// </summary>
    public sealed class JxlPropertyReport
    {
        /// <summary>Gets or sets the source file name or label.</summary>
        public string SourceLabel { get; set; } = string.Empty;

        /// <summary>Gets or sets the total file size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Gets or sets the detected JPEG XL signature type.</summary>
        public JxlSignature Signature { get; set; }

        /// <summary>Gets or sets a value indicating whether the JXL file was valid and decoded metadata successfully.</summary>
        public bool IsValid { get; set; }

        /// <summary>Gets or sets the decoded <see cref="JxlBasicInfo"/> header.</summary>
        public JxlBasicInfo BasicInfo { get; set; }

        /// <summary>Gets or sets a value indicating whether an encoded <see cref="JxlColorEncoding"/> is available.</summary>
        public bool HasColorEncoding { get; set; }

        /// <summary>Gets or sets the original <see cref="JxlColorEncoding"/> when available.</summary>
        public JxlColorEncoding ColorEncoding { get; set; }

        /// <summary>Gets or sets the canonical color description string.</summary>
        public string ColorDescriptionString { get; set; } = string.Empty;

        /// <summary>Gets or sets the original ICC profile byte length.</summary>
        public nuint OriginalIccSize { get; set; }

        /// <summary>Gets or sets the data ICC profile byte length.</summary>
        public nuint DataIccSize { get; set; }

        /// <summary>Gets or sets a value indicating whether a <c>jbrd</c> lossless JPEG reconstruction box is present.</summary>
        public bool HasJpegReconstruction { get; set; }

        /// <summary>Gets or sets the total animation duration in seconds.</summary>
        public float AnimationDurationSeconds { get; set; }

        /// <summary>Gets or sets the full text output produced by <c>jxlinfo -v</c>.</summary>
        public string VerboseInfoText { get; set; } = string.Empty;

        /// <summary>Gets the list of extra channel summaries.</summary>
        public List<JxlExtraChannelSummary> ExtraChannels { get; } = new List<JxlExtraChannelSummary>();

        /// <summary>Gets the list of frame header summaries.</summary>
        public List<JxlFrameSummary> Frames { get; } = new List<JxlFrameSummary>();

        /// <summary>Gets the list of ISO BMFF container box summaries.</summary>
        public List<JxlContainerBoxSummary> Boxes { get; } = new List<JxlContainerBoxSummary>();

        /// <summary>Gets the flattened list of categorized property rows for grid display.</summary>
        public List<JxlPropertyRow> PropertyRows { get; } = new List<JxlPropertyRow>();

        /// <summary>
        /// Rebuilds <see cref="PropertyRows"/> from all populated metadata structures.
        /// </summary>
        public void BuildPropertyRows()
        {
            PropertyRows.Clear();

            AddGeneralRows();
            AddBasicInfoRows();
            AddPreviewAndAnimationRows();
            AddColorEncodingRows();
            AddExtraChannelRows();
            AddFrameRows();
            AddContainerBoxRows();
        }

        private void AddGeneralRows()
        {
            PropertyRows.Add(new JxlPropertyRow("File", "Source", SourceLabel));
            PropertyRows.Add(new JxlPropertyRow("File", "File Size (bytes)", FileSize.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("File", "Signature", Signature.ToString()));
            PropertyRows.Add(new JxlPropertyRow("File", "Valid JXL", IsValid.ToString()));
            PropertyRows.Add(new JxlPropertyRow("File", "Has JPEG Reconstruction (jbrd)", HasJpegReconstruction.ToString()));
        }

        private void AddBasicInfoRows()
        {
            JxlBasicInfo info = BasicInfo;

            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "HaveContainer", info.HaveContainer.ToString()));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "Dimensions (XSize x YSize)", $"{info.XSize} x {info.YSize}"));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "Intrinsic Dimensions", $"{info.IntrinsicXSize} x {info.IntrinsicYSize}"));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "BitsPerSample", info.BitsPerSample.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "ExponentBitsPerSample", info.ExponentBitsPerSample.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "NumColorChannels", info.NumColorChannels.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "NumExtraChannels", info.NumExtraChannels.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "AlphaBits", info.AlphaBits.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "AlphaExponentBits", info.AlphaExponentBits.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "AlphaPremultiplied", info.AlphaPremultiplied.ToString()));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "Orientation", $"{info.Orientation} ({(int)info.Orientation})"));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "UsesOriginalProfile", info.UsesOriginalProfile.ToString()));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "IntensityTarget (nits)", info.IntensityTarget.ToString("F2", CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "MinNits", info.MinNits.ToString("F4", CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "RelativeToMaxDisplay", info.RelativeToMaxDisplay.ToString()));
            PropertyRows.Add(new JxlPropertyRow("BasicInfo", "LinearBelow", info.LinearBelow.ToString("F4", CultureInfo.InvariantCulture)));
        }

        private void AddPreviewAndAnimationRows()
        {
            JxlBasicInfo info = BasicInfo;

            PropertyRows.Add(new JxlPropertyRow("Preview", "HavePreview", info.HavePreview.ToString()));

            if (info.HavePreview)
            {
                PropertyRows.Add(new JxlPropertyRow("Preview", "Preview Dimensions", $"{info.Preview.XSize} x {info.Preview.YSize}"));
            }

            PropertyRows.Add(new JxlPropertyRow("Animation", "HaveAnimation", info.HaveAnimation.ToString()));

            if (info.HaveAnimation)
            {
                PropertyRows.Add(new JxlPropertyRow("Animation", "TicksPerSecond", $"{info.Animation.TpsNumerator}/{info.Animation.TpsDenominator}"));
                PropertyRows.Add(new JxlPropertyRow("Animation", "NumLoops", info.Animation.NumLoops.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow("Animation", "HaveTimecodes", info.Animation.HaveTimecodes.ToString()));
                PropertyRows.Add(new JxlPropertyRow("Animation", "Total Duration (s)", AnimationDurationSeconds.ToString("F3", CultureInfo.InvariantCulture)));
            }
        }

        private void AddColorEncodingRows()
        {
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "HasEncodedProfile", HasColorEncoding.ToString()));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Original ICC Size (bytes)", OriginalIccSize.ToString(CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Data ICC Size (bytes)", DataIccSize.ToString(CultureInfo.InvariantCulture)));

            if (!HasColorEncoding)
            {
                return;
            }

            JxlColorEncoding ce = ColorEncoding;

            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Description", ColorDescriptionString));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "ColorSpace", ce.ColorSpace.ToString()));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "WhitePoint", ce.WhitePoint.ToString()));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "WhitePoint XY", FormatXy(ce.WhitePointXy.X, ce.WhitePointXy.Y)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Primaries", ce.Primaries.ToString()));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Primaries Red XY", FormatXy(ce.PrimariesRedXy.X, ce.PrimariesRedXy.Y)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Primaries Green XY", FormatXy(ce.PrimariesGreenXy.X, ce.PrimariesGreenXy.Y)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Primaries Blue XY", FormatXy(ce.PrimariesBlueXy.X, ce.PrimariesBlueXy.Y)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "TransferFunction", ce.TransferFunction.ToString()));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "Gamma", ce.Gamma.ToString("F6", CultureInfo.InvariantCulture)));
            PropertyRows.Add(new JxlPropertyRow("ColorEncoding", "RenderingIntent", ce.RenderingIntent.ToString()));
        }

        private void AddExtraChannelRows()
        {
            foreach (JxlExtraChannelSummary ec in ExtraChannels)
            {
                string cat  = $"ExtraChannel[{ec.Index}]";
                string name = string.IsNullOrEmpty(ec.Name) ? "(unnamed)" : ec.Name;

                PropertyRows.Add(new JxlPropertyRow(cat, "Name", name));
                PropertyRows.Add(new JxlPropertyRow(cat, "Type", ec.Info.Type.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "BitsPerSample", ec.Info.BitsPerSample.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "ExponentBitsPerSample", ec.Info.ExponentBitsPerSample.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "DimShift", ec.Info.DimShift.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "AlphaPremultiplied", ec.Info.AlphaPremultiplied.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "CfaChannel", ec.Info.CfaChannel.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private void AddFrameRows()
        {
            foreach (JxlFrameSummary frame in Frames)
            {
                string cat             = $"Frame[{frame.Index}]";
                string name            = string.IsNullOrEmpty(frame.Name) ? "(unnamed)" : frame.Name;
                JxlLayerInfo layerInfo = frame.Header.LayerInfo;

                PropertyRows.Add(new JxlPropertyRow(cat, "Name", name));
                PropertyRows.Add(new JxlPropertyRow(cat, "IsLast", frame.Header.IsLast.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "Layer Dimensions", $"{layerInfo.XSize} x {layerInfo.YSize}"));
                PropertyRows.Add(new JxlPropertyRow(cat, "HaveCrop", layerInfo.HaveCrop.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "Crop Offset (X0, Y0)", $"({layerInfo.CropX0}, {layerInfo.CropY0})"));
                PropertyRows.Add(new JxlPropertyRow(cat, "BlendMode", layerInfo.BlendInfo.BlendMode.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "BlendSource", layerInfo.BlendInfo.Source.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "BlendAlphaChannel", layerInfo.BlendInfo.Alpha.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "BlendClamp", layerInfo.BlendInfo.Clamp.ToString()));
                PropertyRows.Add(new JxlPropertyRow(cat, "SaveAsReference", layerInfo.SaveAsReference.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "Duration (ticks / ms)", $"{frame.Header.Duration} ({frame.DurationMs:F2} ms)"));
                PropertyRows.Add(new JxlPropertyRow(cat, "Timecode", frame.Header.Timecode.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private void AddContainerBoxRows()
        {
            foreach (JxlContainerBoxSummary box in Boxes)
            {
                string cat  = $"Box[{box.Index}]";
                string type = string.IsNullOrEmpty(box.InnerBoxType)
                    ? box.BoxType
                    : $"{box.BoxType} ({box.InnerBoxType})";

                PropertyRows.Add(new JxlPropertyRow(cat, "Type", type));
                PropertyRows.Add(new JxlPropertyRow(cat, "Payload Size (bytes)", box.PayloadLength.ToString(CultureInfo.InvariantCulture)));
                PropertyRows.Add(new JxlPropertyRow(cat, "Description", box.Description));
            }
        }

        private static string FormatXy(double x, double y)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F4}, {1:F4})", x, y);
        }
    }
}
