// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Frame header with backward and forward-compatible extension capability and
// compressed integer fields.

using System;
using System.Collections.Generic;
using System.Text;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Frame encoding mode.
    /// Ported from <c>jxl::FrameEncoding</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public enum FrameEncoding : uint
    {
        /// <summary>Variable-blocksize DCT mode.</summary>
        VarDCT = 0,

        /// <summary>Modular mode.</summary>
        Modular = 1,
    }

    /// <summary>
    /// Frame color transform mode.
    /// Ported from <c>jxl::ColorTransform</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public enum ColorTransform : uint
    {
        /// <summary>Values are encoded with XYB (may only be used if <see cref="ImageMetadata.XybEncoded"/> is <c>true</c>).</summary>
        XYB = 0,

        /// <summary>Values are encoded according to the attached color profile (may only be used if <c>!XybEncoded</c>).</summary>
        None = 1,

        /// <summary>Values are transformed to YCbCr (may only be used if <c>!XybEncoded</c>).</summary>
        YCbCr = 2,
    }

    /// <summary>
    /// Frame blending mode.
    /// Ported from <c>jxl::BlendMode</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public enum BlendMode : uint
    {
        /// <summary>New values replace old values: <c>sample = new</c>.</summary>
        Replace = 0,

        /// <summary>New values are added to old values: <c>sample = old + new</c>.</summary>
        Add = 1,

        /// <summary>Alpha blending over old values.</summary>
        Blend = 2,

        /// <summary>Alpha-weighted addition to old values.</summary>
        AlphaWeightedAdd = 3,

        /// <summary>New values are multiplied by old values: <c>sample = old * new</c>.</summary>
        Mul = 4,
    }

    /// <summary>
    /// Type of a frame in the codestream.
    /// Ported from <c>jxl::FrameType</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public enum FrameType : uint
    {
        /// <summary>A regular frame: might be a crop, and will be blended and displayed.</summary>
        RegularFrame = 0,

        /// <summary>A downsampled DC frame used only as the DC of a future frame.</summary>
        DCFrame = 1,

        /// <summary>A reference-only frame used as a source for patches or blending.</summary>
        ReferenceOnly = 2,

        /// <summary>Same as <see cref="RegularFrame"/>, but not used for progressive rendering.</summary>
        SkipProgressive = 3,
    }

    /// <summary>
    /// YCbCr chroma subsampling configuration.
    /// Ported from <c>jxl::YCbCrChromaSubsampling</c> in <c>lib/jxl/frame_header.h</c> and <c>lib/jxl/frame_header.cc</c>.
    /// </summary>
    public sealed class YCbCrChromaSubsampling : Fields
    {
        private static readonly byte[] KHShift = { 0, 1, 1, 0 };
        private static readonly byte[] KVShift = { 0, 1, 0, 1 };

        private readonly uint[] _channelMode = new uint[3];
        private byte _maxhs;
        private byte _maxvs;

        /// <summary>
        /// Initializes a new instance of the <see cref="YCbCrChromaSubsampling"/> class with default values.
        /// </summary>
        public YCbCrChromaSubsampling()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "YCbCrChromaSubsampling";

        /// <summary>Returns the horizontal subsampling shift for channel <paramref name="c"/>.</summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <returns>Horizontal shift.</returns>
        public nuint HShift(nuint c) => (nuint)(_maxhs - KHShift[_channelMode[c]]);

        /// <summary>Returns the vertical subsampling shift for channel <paramref name="c"/>.</summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <returns>Vertical shift.</returns>
        public nuint VShift(nuint c) => (nuint)(_maxvs - KVShift[_channelMode[c]]);

        /// <summary>Returns the maximum horizontal shift across all channels.</summary>
        /// <returns>Maximum horizontal shift.</returns>
        public byte MaxHShift() => _maxhs;

        /// <summary>Returns the maximum vertical shift across all channels.</summary>
        /// <returns>Maximum vertical shift.</returns>
        public byte MaxVShift() => _maxvs;

        /// <summary>Returns the raw horizontal shift for channel <paramref name="c"/>.</summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <returns>Raw horizontal shift.</returns>
        public byte RawHShift(nuint c) => KHShift[_channelMode[c]];

        /// <summary>Returns the raw vertical shift for channel <paramref name="c"/>.</summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <returns>Raw vertical shift.</returns>
        public byte RawVShift(nuint c) => KVShift[_channelMode[c]];

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!visitor.Bits(2, 0, ref _channelMode[i]))
                {
                    return false;
                }
            }

            Recompute();

            return true;
        }

        /// <summary>
        /// Sets subsampling from JPEG sampling factors in JPEG channel order (Y, Cb, Cr).
        /// </summary>
        /// <param name="hsample">Horizontal sampling factors (3 elements).</param>
        /// <param name="vsample">Vertical sampling factors (3 elements).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Set(ReadOnlySpan<byte> hsample, ReadOnlySpan<byte> vsample)
        {
            for (int c = 0; c < 3; c++)
            {
                int cjpeg = c < 2 ? c ^ 1 : c;
                int mode  = FindSubsampleMode(hsample[cjpeg], vsample[cjpeg]);

                if (mode < 0)
                {
                    return Status.Failure("Invalid subsample mode");
                }

                _channelMode[c] = (uint)mode;
            }

            Recompute();

            return true;
        }

        /// <summary>Returns whether subsampling is 4:4:4.</summary>
        /// <returns><c>true</c> if 4:4:4.</returns>
        public bool Is444()
        {
            return HShift(0) == 0 && VShift(0) == 0 &&
                   HShift(2) == 0 && VShift(2) == 0 &&
                   HShift(1) == 0 && VShift(1) == 0;
        }

        /// <summary>Returns whether subsampling is 4:2:0.</summary>
        /// <returns><c>true</c> if 4:2:0.</returns>
        public bool Is420()
        {
            return HShift(0) == 1 && VShift(0) == 1 &&
                   HShift(2) == 1 && VShift(2) == 1 &&
                   HShift(1) == 0 && VShift(1) == 0;
        }

        /// <summary>Returns whether subsampling is 4:2:2.</summary>
        /// <returns><c>true</c> if 4:2:2.</returns>
        public bool Is422()
        {
            return HShift(0) == 1 && VShift(0) == 0 &&
                   HShift(2) == 1 && VShift(2) == 0 &&
                   HShift(1) == 0 && VShift(1) == 0;
        }

        /// <summary>Returns whether subsampling is 4:4:0.</summary>
        /// <returns><c>true</c> if 4:4:0.</returns>
        public bool Is440()
        {
            return HShift(0) == 0 && VShift(0) == 1 &&
                   HShift(2) == 0 && VShift(2) == 1 &&
                   HShift(1) == 0 && VShift(1) == 0;
        }

        /// <summary>Returns a debug string representation of the subsampling mode.</summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            if (Is444())
            {
                return "444";
            }

            if (Is420())
            {
                return "420";
            }

            if (Is422())
            {
                return "422";
            }

            if (Is440())
            {
                return "440";
            }

            return $"cs{_channelMode[0]}{_channelMode[1]}{_channelMode[2]}";
        }

        private static int FindSubsampleMode(byte hs, byte vs)
        {
            for (int i = 0; i < 4; i++)
            {
                if ((1 << KHShift[i]) == hs && (1 << KVShift[i]) == vs)
                {
                    return i;
                }
            }

            return -1;
        }

        private void Recompute()
        {
            _maxhs = 0;
            _maxvs = 0;

            foreach (uint ch in _channelMode)
            {
                _maxhs = Math.Max(_maxhs, KHShift[ch]);
                _maxvs = Math.Max(_maxvs, KVShift[ch]);
            }
        }
    }

    /// <summary>
    /// Indicates how to combine the current frame with a previously saved reference frame.
    /// Ported from <c>jxl::BlendingInfo</c> in <c>lib/jxl/frame_header.h</c> and <c>lib/jxl/frame_header.cc</c>.
    /// </summary>
    public sealed class BlendingInfo : Fields
    {
        /// <summary>Blending mode.</summary>
        public BlendMode Mode;

        /// <summary>Which extra channel to use as alpha channel for blending.</summary>
        public uint AlphaChannel;

        /// <summary>Whether to clamp alpha or channel values to [0, 1].</summary>
        public bool Clamp;

        /// <summary>Reference frame ID to copy from (0..3).</summary>
        public uint Source;

        /// <summary>Number of extra channels in the image (not serialized directly).</summary>
        public nuint NonserializedNumExtraChannels;

        /// <summary>Whether the enclosing frame is a partial crop (not serialized directly).</summary>
        public bool NonserializedIsPartialFrame;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlendingInfo"/> class with default values.
        /// </summary>
        public BlendingInfo()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "BlendingInfo";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!VisitBlendModeField(visitor, BlendMode.Replace, ref Mode))
            {
                return false;
            }

            bool usesAlpha = NonserializedNumExtraChannels > 0 &&
                             (Mode == BlendMode.Blend || Mode == BlendMode.AlphaWeightedAdd);

            if (visitor.Conditional(usesAlpha))
            {
                if (!visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.BitsOffset(3, 3),
                    0,
                    ref AlphaChannel))
                {
                    return false;
                }

                if (visitor.IsReading && AlphaChannel >= NonserializedNumExtraChannels)
                {
                    return Status.Failure("Invalid alpha channel for blending");
                }
            }

            if (visitor.Conditional(usesAlpha || Mode == BlendMode.Mul) &&
                !visitor.Bool(false, ref Clamp))
            {
                return false;
            }

            if (visitor.Conditional(Mode != BlendMode.Replace || NonserializedIsPartialFrame))
            {
                return visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Val(3),
                    0,
                    ref Source);
            }

            return true;
        }

        /// <summary>Returns a debug string summary of this <see cref="BlendingInfo"/>.</summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            string modeStr   = Mode switch
            {
                BlendMode.Replace          => "Replace",
                BlendMode.Add              => "Add",
                BlendMode.Blend            => "Blend",
                BlendMode.AlphaWeightedAdd => "AlphaWeightedAdd",
                _                          => "Mul",
            };
            sb.Append(modeStr);

            if (NonserializedNumExtraChannels > 0 &&
                (Mode == BlendMode.Blend || Mode == BlendMode.AlphaWeightedAdd))
            {
                sb.Append($",alpha={AlphaChannel},clamp={Clamp}");
            }
            else if (Mode == BlendMode.Mul)
            {
                sb.Append($",clamp={Clamp}");
            }

            if (Mode != BlendMode.Replace || NonserializedIsPartialFrame)
            {
                sb.Append($",source={Source}");
            }

            return sb.ToString();
        }

        internal static Status VisitBlendModeField(
            Visitor visitor,
            BlendMode defaultValue,
            ref BlendMode blendMode)
        {
            uint encoded = (uint)blendMode;

            if (!visitor.U32(
                U32Distr.Val((uint)BlendMode.Replace),
                U32Distr.Val((uint)BlendMode.Add),
                U32Distr.Val((uint)BlendMode.Blend),
                U32Distr.BitsOffset(2, 3),
                (uint)defaultValue,
                ref encoded))
            {
                return false;
            }

            if (encoded > (uint)BlendMode.Mul)
            {
                return Status.Failure("Invalid blend_mode");
            }

            blendMode = (BlendMode)encoded;

            return true;
        }
    }

    /// <summary>
    /// Origin of a cropped frame in pixels (can be negative).
    /// Ported from <c>jxl::FrameOrigin</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public struct FrameOrigin
    {
        /// <summary>Horizontal frame origin offset (can be negative).</summary>
        public int X0;

        /// <summary>Vertical frame origin offset (can be negative).</summary>
        public int Y0;
    }

    /// <summary>
    /// Size of a cropped frame in pixels.
    /// Ported from <c>jxl::FrameSize</c> in <c>lib/jxl/frame_header.h</c>.
    /// </summary>
    public struct FrameSize
    {
        /// <summary>Frame width in pixels.</summary>
        public uint XSize;

        /// <summary>Frame height in pixels.</summary>
        public uint YSize;
    }

    /// <summary>
    /// Animation timing metadata for a single frame.
    /// Ported from <c>jxl::AnimationFrame</c> in <c>lib/jxl/frame_header.h</c> and <c>lib/jxl/frame_header.cc</c>.
    /// </summary>
    public sealed class AnimationFrame : Fields
    {
        /// <summary>Duration of the frame in ticks.</summary>
        public uint Duration;

        /// <summary>SMPTE timecode in <c>0xHHMMSSFF</c> format.</summary>
        public uint Timecode;

        /// <summary>Full codestream metadata context (not serialized directly).</summary>
        public CodecMetadata? NonserializedMetadata;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationFrame"/> class.
        /// </summary>
        /// <param name="metadata">Codestream metadata context.</param>
        public AnimationFrame(CodecMetadata? metadata)
        {
            NonserializedMetadata = metadata;
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "AnimationFrame";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.Conditional(NonserializedMetadata != null && NonserializedMetadata.M.HaveAnimation) &&
                !visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.Bits(8),
                    U32Distr.Bits(32),
                    0,
                    ref Duration))
            {
                return false;
            }

            if (visitor.Conditional(NonserializedMetadata != null && NonserializedMetadata.M.Animation.HaveTimecodes))
            {
                return visitor.Bits(32, 0, ref Timecode);
            }

            return true;
        }
    }

    /// <summary>
    /// Progressive pass schedule for a frame.
    /// Ported from <c>jxl::Passes</c> in <c>lib/jxl/frame_header.h</c> and <c>lib/jxl/frame_header.cc</c>.
    /// </summary>
    public sealed class Passes : Fields
    {
        /// <summary>Number of passes (<c>&lt;= KMaxNumPasses</c>).</summary>
        public uint NumPasses;

        /// <summary>Number of downsampling brackets (<c>&lt;= NumPasses</c>).</summary>
        public uint NumDownsample;

        /// <summary>Downsampling factor for each bracket.</summary>
        public uint[] Downsample = new uint[FrameDimensions.KMaxNumPasses];

        /// <summary>Last pass index for each downsampling bracket.</summary>
        public uint[] LastPass = new uint[FrameDimensions.KMaxNumPasses];

        /// <summary>Quantization shift for each pass.</summary>
        public uint[] Shift = new uint[FrameDimensions.KMaxNumPasses];

        /// <summary>
        /// Initializes a new instance of the <see cref="Passes"/> class with default values.
        /// </summary>
        public Passes()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "Passes";

        /// <summary>
        /// Computes the minimum and maximum bit shift bracket for <paramref name="pass"/>.
        /// </summary>
        /// <param name="pass">Pass index.</param>
        /// <param name="minShift">Output minimum shift.</param>
        /// <param name="maxShift">Output maximum shift.</param>
        public void GetDownsamplingBracket(nuint pass, out int minShift, out int maxShift)
        {
            maxShift = 2;
            minShift = 3;

            for (nuint i = 0; ; i++)
            {
                UpdateMinShiftForPass(i, ref minShift);

                if (i == NumPasses - 1)
                {
                    minShift = 0;
                }

                if (i == pass)
                {
                    return;
                }

                maxShift = minShift - 1;
            }
        }

        /// <summary>
        /// Returns the downsampling target factor achieved after <paramref name="numP"/> completed passes.
        /// </summary>
        /// <param name="numP">Number of completed passes.</param>
        /// <returns>Downsampling factor (1, 2, 4, or 8).</returns>
        public uint GetDownsamplingTargetForCompletedPasses(uint numP)
        {
            if (numP >= NumPasses)
            {
                return 1;
            }

            uint retval = 8;

            for (uint i = 0; i < NumDownsample; ++i)
            {
                if (numP > LastPass[i])
                {
                    retval = Math.Min(retval, Downsample[i]);
                }
            }

            return retval;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.U32(
                U32Distr.Val(1),
                U32Distr.Val(2),
                U32Distr.Val(3),
                U32Distr.BitsOffset(3, 4),
                1,
                ref NumPasses))
            {
                return false;
            }

            if (NumPasses > FrameDimensions.KMaxNumPasses)
            {
                return Status.Failure("num_passes exceeds kMaxNumPasses");
            }

            if (!visitor.Conditional(NumPasses != 1))
            {
                return true;
            }

            return VisitMultiPassFields(visitor);
        }

        /// <summary>Returns a debug string summary of this <see cref="Passes"/> schedule.</summary>
        /// <returns>Debug string.</returns>
        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"p={NumPasses}");

            if (NumDownsample != 0)
            {
                sb.Append(",ds=");

                for (uint i = 0; i < NumDownsample; ++i)
                {
                    sb.Append($"{LastPass[i]}:{Downsample[i]}");

                    if (i + 1 < NumDownsample)
                    {
                        sb.Append(';');
                    }
                }
            }

            AppendShiftsDebug(sb);

            return sb.ToString();
        }

        private static int DownsampleToShift(uint ds)
        {
            return ds switch
            {
                8 => 3,
                4 => 2,
                2 => 1,
                1 => 0,
                _ => -1,
            };
        }

        private void UpdateMinShiftForPass(nuint passIndex, ref int minShift)
        {
            for (uint j = 0; j < NumDownsample; ++j)
            {
                if (passIndex == LastPass[j])
                {
                    int mapped = DownsampleToShift(Downsample[j]);

                    if (mapped >= 0)
                    {
                        minShift = mapped;
                    }
                }
            }
        }

        private Status VisitMultiPassFields(Visitor visitor)
        {
            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Val(1),
                U32Distr.Val(2),
                U32Distr.BitsOffset(1, 3),
                0,
                ref NumDownsample))
            {
                return false;
            }

            if (NumDownsample > 4 || NumDownsample > NumPasses)
            {
                return Status.Failure($"num_downsample {NumDownsample} > num_passes {NumPasses}");
            }

            for (uint i = 0; i < NumPasses - 1; i++)
            {
                if (!visitor.Bits(2, 0, ref Shift[i]))
                {
                    return false;
                }
            }

            Shift[NumPasses - 1] = 0;

            return VisitDownsampleAndLastPassArrays(visitor);
        }

        private Status VisitDownsampleAndLastPassArrays(Visitor visitor)
        {
            for (uint i = 0; i < NumDownsample; ++i)
            {
                if (!visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Val(4),
                    U32Distr.Val(8),
                    1,
                    ref Downsample[i]))
                {
                    return false;
                }

                if (i > 0 && Downsample[i] >= Downsample[i - 1])
                {
                    return Status.Failure("downsample sequence should be decreasing");
                }
            }

            for (uint i = 0; i < NumDownsample; ++i)
            {
                if (!visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Bits(3),
                    0,
                    ref LastPass[i]))
                {
                    return false;
                }

                if ((i > 0 && LastPass[i] <= LastPass[i - 1]) || LastPass[i] >= NumPasses)
                {
                    return Status.Failure("invalid last_pass sequence");
                }
            }

            return true;
        }

        private void AppendShiftsDebug(StringBuilder sb)
        {
            bool haveShifts = false;

            for (uint i = 0; i < NumPasses; ++i)
            {
                if (Shift[i] != 0)
                {
                    haveShifts = true;
                }
            }

            if (!haveShifts)
            {
                return;
            }

            sb.Append(",shifts=");

            for (uint i = 0; i < NumPasses; ++i)
            {
                sb.Append(Shift[i]);

                if (i + 1 < NumPasses)
                {
                    sb.Append(';');
                }
            }
        }
    }

    /// <summary>
    /// Header of a single frame in a JPEG XL codestream.
    /// Ported from <c>jxl::FrameHeader</c> in <c>lib/jxl/frame_header.h</c> and <c>lib/jxl/frame_header.cc</c>.
    /// </summary>
    public sealed class FrameHeader : Fields
    {
        /// <summary>Flag: inject noise into decoded output.</summary>
        public const ulong KNoise = 1;

        /// <summary>Flag: overlay patches.</summary>
        public const ulong KPatches = 2;

        /// <summary>Flag: overlay splines.</summary>
        public const ulong KSplines = 16;

        /// <summary>Flag: use previously decoded DC frame (implies <see cref="KSkipAdaptiveDCSmoothing"/>).</summary>
        public const ulong KUseDcFrame = 32;

        /// <summary>Flag: skip adaptive DC smoothing.</summary>
        public const ulong KSkipAdaptiveDCSmoothing = 128;

        /// <summary>Whether all fields in this bundle have their default values.</summary>
        public bool AllDefault;

        /// <summary>Frame encoding mode (VarDCT or Modular).</summary>
        public FrameEncoding Encoding = FrameEncoding.Modular;

        /// <summary>Frame type.</summary>
        public FrameType FrameType = FrameType.RegularFrame;

        /// <summary>Postprocessing flags bitmask.</summary>
        public ulong Flags;

        /// <summary>Color transform mode.</summary>
        public ColorTransform ColorTransform = ColorTransform.XYB;

        /// <summary>YCbCr chroma subsampling configuration.</summary>
        public YCbCrChromaSubsampling ChromaSubsampling = new YCbCrChromaSubsampling();

        /// <summary>Modular group size shift (only if <see cref="Encoding"/> is <see cref="FrameEncoding.Modular"/>).</summary>
        public uint GroupSizeShift;

        /// <summary>X quantization matrix scale (only if VarDCT and XYB).</summary>
        public uint XQmScale;

        /// <summary>B quantization matrix scale (only if VarDCT and XYB).</summary>
        public uint BQmScale;

        /// <summary>UTF-8 frame name.</summary>
        public string Name = string.Empty;

        /// <summary>Progressive pass schedule.</summary>
        public Passes Passes = new Passes();

        /// <summary>Whether a custom frame size or origin is specified.</summary>
        public bool CustomSizeOrOrigin;

        /// <summary>Custom frame size.</summary>
        public FrameSize FrameSize;

        /// <summary>Color channel upsampling factor (1, 2, 4, or 8).</summary>
        public uint Upsampling;

        /// <summary>Extra channel upsampling factors.</summary>
        public List<uint> ExtraChannelUpsampling = new List<uint>();

        /// <summary>Custom frame origin.</summary>
        public FrameOrigin FrameOrigin;

        /// <summary>Color channel blending info.</summary>
        public BlendingInfo BlendingInfo = new BlendingInfo();

        /// <summary>Per-extra-channel blending info.</summary>
        public List<BlendingInfo> ExtraChannelBlendingInfo = new List<BlendingInfo>();

        /// <summary>Animation frame timing info.</summary>
        public AnimationFrame AnimationFrame;

        /// <summary>Whether this is the last frame in the codestream.</summary>
        public bool IsLast;

        /// <summary>Reference slot ID (0..3) to save this frame into.</summary>
        public uint SaveAsReference;

        /// <summary>Whether to save this frame before or after the color transform.</summary>
        public bool SaveBeforeColorTransform;

        /// <summary>DC pyramid level (1..4 if <see cref="FrameType.DCFrame"/>, 0 otherwise).</summary>
        public uint DcLevel;

        /// <summary>Codestream metadata context (not serialized directly).</summary>
        public CodecMetadata? NonserializedMetadata;

        /// <summary>Loop filter parameters.</summary>
        public LoopFilter LoopFilter = new LoopFilter();

        /// <summary>Whether this frame is the preview frame (not serialized directly).</summary>
        public bool NonserializedIsPreview;

        /// <summary>Extension bitmask.</summary>
        public ulong Extensions;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrameHeader"/> class.
        /// </summary>
        /// <param name="metadata">Codestream metadata context.</param>
        public FrameHeader(CodecMetadata? metadata)
        {
            AnimationFrame        = new AnimationFrame(metadata);
            NonserializedMetadata = metadata;
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "FrameHeader";

        /// <summary>
        /// Sets or clears <paramref name="flag"/> in <see cref="Flags"/> according to <paramref name="condition"/>.
        /// </summary>
        /// <param name="condition">Whether the flag should be set.</param>
        /// <param name="flag">Bit flag mask.</param>
        public void UpdateFlag(bool condition, ulong flag)
        {
            if (condition)
            {
                Flags |= flag;
            }
            else
            {
                Flags &= ~flag;
            }
        }

        /// <summary>
        /// Returns whether this frame can be referenced by future frames.
        /// </summary>
        /// <returns><c>true</c> if referenceable.</returns>
        public bool CanBeReferenced()
        {
            return !IsLast &&
                   FrameType != FrameType.DCFrame &&
                   (AnimationFrame.Duration == 0 || SaveAsReference != 0);
        }

        /// <summary>
        /// Returns the default width of this frame from <see cref="NonserializedMetadata"/>.
        /// </summary>
        /// <returns>Default width in pixels.</returns>
        public nuint DefaultXSize()
        {
            if (NonserializedMetadata == null)
            {
                return 0;
            }

            return NonserializedIsPreview
                ? NonserializedMetadata.M.PreviewSize.XSize()
                : NonserializedMetadata.XSize();
        }

        /// <summary>
        /// Returns the default height of this frame from <see cref="NonserializedMetadata"/>.
        /// </summary>
        /// <returns>Default height in pixels.</returns>
        public nuint DefaultYSize()
        {
            if (NonserializedMetadata == null)
            {
                return 0;
            }

            return NonserializedIsPreview
                ? NonserializedMetadata.M.PreviewSize.YSize()
                : NonserializedMetadata.YSize();
        }

        /// <summary>
        /// Computes the <see cref="FrameDimensions"/> for this frame.
        /// </summary>
        /// <returns>Computed <see cref="FrameDimensions"/>.</returns>
        public FrameDimensions ToFrameDimensions()
        {
            nuint xsize = FrameSize.XSize != 0 ? FrameSize.XSize : DefaultXSize();
            nuint ysize = FrameSize.YSize != 0 ? FrameSize.YSize : DefaultYSize();

            if (DcLevel != 0)
            {
                nuint div = 1u << (int)(3 * DcLevel);
                xsize     = Common.DivCeil(xsize, div);
                ysize     = Common.DivCeil(ysize, div);
            }

            FrameDimensions frameDim = default;
            frameDim.Set(
                xsize,
                ysize,
                GroupSizeShift,
                ChromaSubsampling.MaxHShift(),
                ChromaSubsampling.MaxVShift(),
                Encoding == FrameEncoding.Modular,
                Upsampling);

            return frameDim;
        }

        /// <summary>
        /// Returns whether an inverse color transform should be applied to this frame.
        /// </summary>
        /// <returns><c>true</c> if color transform is needed.</returns>
        public bool NeedsColorTransform()
        {
            return !SaveBeforeColorTransform ||
                   FrameType == FrameType.RegularFrame ||
                   FrameType == FrameType.SkipProgressive;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (visitor.AllDefault(this, ref AllDefault))
            {
                visitor.SetDefault(this);

                return true;
            }

            if (!VisitBasicModeAndColorTransform(visitor))
            {
                return false;
            }

            nuint numExtraChannels = NonserializedMetadata != null
                ? (nuint)NonserializedMetadata.M.ExtraChannelInfo.Count
                : 0;

            if (!VisitUpsamplingAndScaleFields(visitor, numExtraChannels))
            {
                return false;
            }

            if (!VisitPassesAndCropFields(visitor, out bool isPartialFrame))
            {
                return false;
            }

            if (!VisitBlendingAndReferenceFields(visitor, numExtraChannels, isPartialFrame))
            {
                return false;
            }

            return VisitTailFields(visitor);
        }

        private static Status VisitFrameTypeField(
            Visitor visitor,
            FrameType defaultValue,
            ref FrameType frameType)
        {
            uint encoded = (uint)frameType;

            if (!visitor.U32(
                U32Distr.Val((uint)FrameType.RegularFrame),
                U32Distr.Val((uint)FrameType.DCFrame),
                U32Distr.Val((uint)FrameType.ReferenceOnly),
                U32Distr.Val((uint)FrameType.SkipProgressive),
                (uint)defaultValue,
                ref encoded))
            {
                return false;
            }

            frameType = (FrameType)encoded;

            return true;
        }

        private Status VisitBasicModeAndColorTransform(Visitor visitor)
        {
            if (!VisitFrameTypeField(visitor, FrameType.RegularFrame, ref FrameType))
            {
                return false;
            }

            if (visitor.IsReading && NonserializedIsPreview && FrameType != FrameType.RegularFrame)
            {
                return Status.Failure("Only regular frame could be a preview");
            }

            bool isModular = Encoding == FrameEncoding.Modular;

            if (!visitor.Bool(false, ref isModular))
            {
                return false;
            }

            Encoding = isModular ? FrameEncoding.Modular : FrameEncoding.VarDCT;

            if (!visitor.U64(0, ref Flags))
            {
                return false;
            }

            return VisitColorTransformFields(visitor);
        }

        private Status VisitColorTransformFields(Visitor visitor)
        {
            bool xybEncoded = NonserializedMetadata == null || NonserializedMetadata.M.XybEncoded;

            if (xybEncoded)
            {
                ColorTransform = ColorTransform.XYB;
            }
            else
            {
                bool alternate = ColorTransform == ColorTransform.YCbCr;

                if (!visitor.Bool(false, ref alternate))
                {
                    return false;
                }

                ColorTransform = alternate ? ColorTransform.YCbCr : ColorTransform.None;
            }

            if (visitor.Conditional(ColorTransform == ColorTransform.YCbCr && (Flags & KUseDcFrame) == 0))
            {
                return visitor.VisitNested(ChromaSubsampling);
            }

            return true;
        }

        private Status VisitUpsamplingAndScaleFields(Visitor visitor, nuint numExtraChannels)
        {
            if (visitor.Conditional((Flags & KUseDcFrame) == 0) &&
                !VisitUpsamplingFields(visitor, numExtraChannels))
            {
                return false;
            }

            if (visitor.Conditional(Encoding == FrameEncoding.Modular) &&
                !visitor.Bits(2, 1, ref GroupSizeShift))
            {
                return false;
            }

            if (visitor.Conditional(Encoding == FrameEncoding.VarDCT && ColorTransform == ColorTransform.XYB))
            {
                if (!visitor.Bits(3, 3, ref XQmScale))
                {
                    return false;
                }

                return visitor.Bits(3, 2, ref BQmScale);
            }

            XQmScale = 2;
            BQmScale = 2;

            return true;
        }

        private Status VisitUpsamplingFields(Visitor visitor, nuint numExtraChannels)
        {
            if (!visitor.U32(
                U32Distr.Val(1),
                U32Distr.Val(2),
                U32Distr.Val(4),
                U32Distr.Val(8),
                1,
                ref Upsampling))
            {
                return false;
            }

            if (NonserializedMetadata == null || !visitor.Conditional(numExtraChannels != 0))
            {
                ExtraChannelUpsampling.Clear();

                return true;
            }

            return VisitExtraChannelUpsampling(visitor, NonserializedMetadata.M.ExtraChannelInfo);
        }

        private Status VisitExtraChannelUpsampling(Visitor visitor, List<ExtraChannelInfo> extraChannels)
        {
            while (ExtraChannelUpsampling.Count < extraChannels.Count)
            {
                ExtraChannelUpsampling.Add(1);
            }

            if (ExtraChannelUpsampling.Count > extraChannels.Count)
            {
                ExtraChannelUpsampling.RemoveRange(
                    extraChannels.Count,
                    ExtraChannelUpsampling.Count - extraChannels.Count);
            }

            for (int i = 0; i < extraChannels.Count; ++i)
            {
                uint dimShift     = extraChannels[i].DimShift;
                uint ecUpsampling = ExtraChannelUpsampling[i] >> (int)dimShift;

                if (!visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Val(4),
                    U32Distr.Val(8),
                    1,
                    ref ecUpsampling))
                {
                    return false;
                }

                ecUpsampling <<= (int)dimShift;
                ExtraChannelUpsampling[i] = ecUpsampling;

                if (ecUpsampling < Upsampling || ecUpsampling > 8)
                {
                    return Status.Failure($"Invalid EC upsampling ({ecUpsampling})");
                }
            }

            return true;
        }

        private Status VisitPassesAndCropFields(Visitor visitor, out bool isPartialFrame)
        {
            isPartialFrame = false;

            if (visitor.Conditional(FrameType != FrameType.ReferenceOnly) && !visitor.VisitNested(Passes))
            {
                return false;
            }

            if (visitor.Conditional(FrameType == FrameType.DCFrame) &&
                !visitor.U32(
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Val(3),
                    U32Distr.Val(4),
                    1,
                    ref DcLevel))
            {
                return false;
            }

            if (FrameType != FrameType.DCFrame)
            {
                DcLevel = 0;
            }

            if (!visitor.Conditional(FrameType != FrameType.DCFrame))
            {
                return true;
            }

            if (!visitor.Bool(false, ref CustomSizeOrOrigin))
            {
                return false;
            }

            if (visitor.Conditional(CustomSizeOrOrigin))
            {
                return VisitCustomCropFields(visitor, out isPartialFrame);
            }

            return true;
        }

        private Status VisitCustomCropFields(Visitor visitor, out bool isPartialFrame)
        {
            isPartialFrame = false;
            U32Enc enc     = new U32Enc(
                U32Distr.Bits(8),
                U32Distr.BitsOffset(11, 256),
                U32Distr.BitsOffset(14, 2304),
                U32Distr.BitsOffset(30, 18688));

            bool isRegOrSkip = FrameType == FrameType.RegularFrame || FrameType == FrameType.SkipProgressive;

            if (visitor.Conditional(isRegOrSkip))
            {
                uint ux0 = PackSigned.Pack(FrameOrigin.X0);
                uint uy0 = PackSigned.Pack(FrameOrigin.Y0);

                if (!visitor.U32(enc, 0, ref ux0) || !visitor.U32(enc, 0, ref uy0))
                {
                    return false;
                }

                FrameOrigin.X0 = PackSigned.Unpack(ux0);
                FrameOrigin.Y0 = PackSigned.Unpack(uy0);
            }

            if (!visitor.U32(enc, 0, ref FrameSize.XSize) || !visitor.U32(enc, 0, ref FrameSize.YSize))
            {
                return false;
            }

            if (CustomSizeOrOrigin && (FrameSize.XSize == 0 || FrameSize.YSize == 0))
            {
                return Status.Failure("Invalid crop dimensions for frame: zero width or height");
            }

            if (isRegOrSkip)
            {
                int imageXSize = (int)DefaultXSize();
                int imageYSize = (int)DefaultYSize();
                isPartialFrame = FrameOrigin.X0 > 0 ||
                                 FrameOrigin.Y0 > 0 ||
                                 ((int)FrameSize.XSize + FrameOrigin.X0) < imageXSize ||
                                 ((int)FrameSize.YSize + FrameOrigin.Y0) < imageYSize;
            }

            return true;
        }

        private Status VisitBlendingAndReferenceFields(
            Visitor visitor,
            nuint numExtraChannels,
            bool isPartialFrame)
        {
            bool isRegOrSkip = FrameType == FrameType.RegularFrame || FrameType == FrameType.SkipProgressive;

            if (visitor.Conditional(isRegOrSkip))
            {
                if (!VisitRegularBlendingAndAnimation(visitor, numExtraChannels, isPartialFrame))
                {
                    return false;
                }
            }
            else
            {
                IsLast = false;
            }

            if (visitor.Conditional(FrameType != FrameType.DCFrame && !IsLast) &&
                !visitor.U32(
                    U32Distr.Val(0),
                    U32Distr.Val(1),
                    U32Distr.Val(2),
                    U32Distr.Val(3),
                    0,
                    ref SaveAsReference))
            {
                return false;
            }

            return VisitSaveBeforeColorTransform(visitor, isPartialFrame, isRegOrSkip);
        }

        private Status VisitRegularBlendingAndAnimation(
            Visitor visitor,
            nuint numExtraChannels,
            bool isPartialFrame)
        {
            BlendingInfo.NonserializedNumExtraChannels = numExtraChannels;
            BlendingInfo.NonserializedIsPartialFrame   = isPartialFrame;

            if (!visitor.VisitNested(BlendingInfo))
            {
                return false;
            }

            if (!VisitExtraChannelBlending(visitor, numExtraChannels, isPartialFrame, out bool replaceAll))
            {
                return false;
            }

            if (visitor.IsReading && NonserializedIsPreview && (!replaceAll || CustomSizeOrOrigin))
            {
                return Status.Failure("Preview is not compatible with blending");
            }

            if (visitor.Conditional(NonserializedMetadata != null && NonserializedMetadata.M.HaveAnimation))
            {
                AnimationFrame.NonserializedMetadata = NonserializedMetadata;

                if (!visitor.VisitNested(AnimationFrame))
                {
                    return false;
                }
            }

            return visitor.Bool(true, ref IsLast);
        }

        private Status VisitExtraChannelBlending(
            Visitor visitor,
            nuint numExtraChannels,
            bool isPartialFrame,
            out bool replaceAll)
        {
            replaceAll = BlendingInfo.Mode == BlendMode.Replace;

            while ((nuint)ExtraChannelBlendingInfo.Count < numExtraChannels)
            {
                ExtraChannelBlendingInfo.Add(new BlendingInfo());
            }

            if ((nuint)ExtraChannelBlendingInfo.Count > numExtraChannels)
            {
                ExtraChannelBlendingInfo.RemoveRange(
                    (int)numExtraChannels,
                    ExtraChannelBlendingInfo.Count - (int)numExtraChannels);
            }

            for (int i = 0; i < (int)numExtraChannels; i++)
            {
                BlendingInfo ecBlend                  = ExtraChannelBlendingInfo[i];
                ecBlend.NonserializedIsPartialFrame   = isPartialFrame;
                ecBlend.NonserializedNumExtraChannels = numExtraChannels;

                if (!visitor.VisitNested(ecBlend))
                {
                    return false;
                }

                replaceAll &= ecBlend.Mode == BlendMode.Replace;
            }

            return true;
        }

        private Status VisitSaveBeforeColorTransform(
            Visitor visitor,
            bool isPartialFrame,
            bool isRegOrSkip)
        {
            if (FrameType == FrameType.DCFrame)
            {
                SaveBeforeColorTransform = true;

                return true;
            }

            bool canSaveBefore = CanBeReferenced() &&
                                 BlendingInfo.Mode == BlendMode.Replace &&
                                 !isPartialFrame &&
                                 isRegOrSkip;

            if (visitor.Conditional(canSaveBefore))
            {
                return visitor.Bool(false, ref SaveBeforeColorTransform);
            }

            if (!visitor.Conditional(FrameType == FrameType.ReferenceOnly))
            {
                return true;
            }

            if (!visitor.Bool(true, ref SaveBeforeColorTransform))
            {
                return false;
            }

            return ValidateReferenceOnlyCrop();
        }

        private Status ValidateReferenceOnlyCrop()
        {
            if (SaveBeforeColorTransform || NonserializedMetadata == null)
            {
                return true;
            }

            nuint xsize = CustomSizeOrOrigin ? FrameSize.XSize : NonserializedMetadata.XSize();
            nuint ysize = CustomSizeOrOrigin ? FrameSize.YSize : NonserializedMetadata.YSize();

            if (xsize < NonserializedMetadata.XSize() ||
                ysize < NonserializedMetadata.YSize() ||
                FrameOrigin.X0 != 0 ||
                FrameOrigin.Y0 != 0)
            {
                return Status.Failure("non-patch reference frame with invalid crop");
            }

            return true;
        }

        private Status VisitTailFields(Visitor visitor)
        {
            if (!ImageMetadataMethods.VisitNameString(visitor, ref Name))
            {
                return false;
            }

            LoopFilter.NonserializedIsModular = Encoding == FrameEncoding.Modular;

            if (!visitor.VisitNested(LoopFilter))
            {
                return false;
            }

            if (!visitor.BeginExtensions(ref Extensions))
            {
                return false;
            }

            return visitor.EndExtensions();
        }
    }

    /// <summary>
    /// Helper methods and constants for <see cref="FrameHeader"/>.
    /// Ported from <c>lib/jxl/frame_header.h</c>, <c>lib/jxl/frame_header.cc</c>, and <c>lib/jxl/enc_fields.cc</c>.
    /// </summary>
    public static class FrameHeaderMethods
    {
        /// <summary>
        /// Shared U32 encoding for coefficient order permutation mask.
        /// </summary>
        public static readonly U32Enc KOrderEnc = new U32Enc(
            U32Distr.Val(0x5F),
            U32Distr.Val(0x13),
            U32Distr.Val(0),
            U32Distr.Bits(FrameDimensions.KNumOrders));

        /// <summary>
        /// Returns the JPEG channel order mapping for the given <paramref name="ct"/> and <paramref name="isGray"/>.
        /// </summary>
        /// <param name="ct">Color transform.</param>
        /// <param name="isGray">Whether the image is grayscale.</param>
        /// <returns>3-element channel index array.</returns>
        public static int[] JpegOrder(ColorTransform ct, bool isGray)
        {
            if (isGray)
            {
                return new[] { 0, 0, 0 };
            }

            if (ct == ColorTransform.YCbCr)
            {
                return new[] { 1, 0, 2 };
            }

            return new[] { 0, 1, 2 };
        }

        /// <summary>
        /// Reads a <see cref="FrameHeader"/> from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="frame">Destination frame header.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadFrameHeader(BitReader reader, FrameHeader frame)
        {
            return Bundle.Read(reader, frame);
        }

        /// <summary>
        /// Writes a <see cref="FrameHeader"/> to <paramref name="writer"/>.
        /// </summary>
        /// <param name="frame">Frame header to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteFrameHeader(FrameHeader frame, BitWriter writer, AuxOut? auxOut)
        {
            return Bundle.Write(frame, writer, LayerType.Header, auxOut);
        }
    }
}
