// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using JxlSharp.Extras;

namespace JxlSharp
{
    /// <summary>
    /// Comprehensive configuration options exposing all available JPEG XL (<c>libjxl</c>) decoder parameters,
    /// pixel format negotiation, color management, progressive decoding, alpha blending, and JPEG reconstruction flags.
    /// </summary>
    public sealed class JxlDecodeOptions
    {
        /// <summary>
        /// Gets the list of explicit accepted pixel formats. When empty, accepted formats are automatically
        /// derived from <see cref="RequestedChannels"/>, <see cref="RequestedDataType"/>, and <see cref="BitsPerSample"/>.
        /// </summary>
        public List<JxlPixelFormat> AcceptedFormats { get; } = new List<JxlPixelFormat>();

        /// <summary>
        /// Gets or sets the optional explicit output channel count (<c>1, 2, 3, 4</c>, or <c>null</c> to match image channels).
        /// </summary>
        public uint? RequestedChannels { get; set; }

        /// <summary>
        /// Gets or sets the optional explicit output sample data type (<see cref="JxlDataType.UInt8"/>,
        /// <see cref="JxlDataType.UInt16"/>, <see cref="JxlDataType.Float"/>, or <c>null</c> for automatic).
        /// </summary>
        public JxlDataType? RequestedDataType { get; set; }

        /// <summary>
        /// Gets or sets the output byte ordering for multi-byte sample formats.
        /// </summary>
        public JxlEndianness Endianness { get; set; } = JxlEndianness.NativeEndian;

        /// <summary>
        /// Gets or sets the scanline byte alignment (<c>0</c> = tightly packed).
        /// </summary>
        public nuint Align { get; set; }

        /// <summary>
        /// Gets or sets the custom output bit depth per sample (<c>0</c> = automatic from pixel format/codestream,
        /// or explicit <c>8</c>, <c>16</c>, <c>32</c>).
        /// </summary>
        public uint BitsPerSample { get; set; }

        /// <summary>
        /// Gets or sets the explicit output bit depth descriptor when <see cref="BitsPerSample"/> is <c>0</c>.
        /// </summary>
        public JxlBitDepth OutputBitdepth { get; set; } = new JxlBitDepth
        {
            Type                  = JxlBitDepthType.FromPixelFormat,
            BitsPerSample         = 0,
            ExponentBitsPerSample = 0,
        };

        /// <summary>
        /// Gets or sets the requested output color space description (e.g., <c>"RGB_D65_SRG_Rel_SRG"</c>, or empty for default).
        /// </summary>
        public string ColorSpace { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested output color space description for CMYK images.
        /// </summary>
        public string ColorSpaceForCmyk { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the desired display intensity target in nits for HDR tone mapping (<c>0</c> = no tone mapping).
        /// </summary>
        public float DisplayNits { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether spot color channels are rendered onto the color channels.
        /// </summary>
        public bool RenderSpotcolors { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the codestream/EXIF orientation is kept untouched instead of applied.
        /// </summary>
        public bool KeepOrientation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether animation and composite frames are coalesced.
        /// </summary>
        public bool Coalescing { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether truncated or partial JXL files are decoded as far as possible.
        /// </summary>
        public bool AllowPartialInput { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of progressive passes to decode (<see cref="uint.MaxValue"/> = all passes).
        /// </summary>
        public uint MaxPasses { get; set; } = uint.MaxValue;

        /// <summary>
        /// Gets or sets the maximum tolerable downsampling factor (<c>1, 2, 4, 8</c>).
        /// </summary>
        public nuint MaxDownsampling { get; set; } = 1;

        /// <summary>
        /// Gets or sets a value indicating whether the streaming image row callback is used during decoding.
        /// </summary>
        public bool UseImageCallback { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether premultiplied (associated) alpha channels are unpremultiplied.
        /// </summary>
        public bool UnpremultiplyAlpha { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether byte-exact JPEG reconstruction (<c>jbrd</c>) should be extracted when available.
        /// </summary>
        public bool ReconstructJpegIfAvailable { get; set; }

        /// <summary>
        /// Gets or sets an optional 3-element RGB background color (<c>[R, G, B]</c> in <c>0.0..1.0</c>) onto which
        /// alpha channels are blended after decoding.
        /// </summary>
        public float[]? AlphaBlendBackground { get; set; }

        /// <summary>
        /// Gets or sets the optional dimension and pixel count security constraints.
        /// </summary>
        public SizeConstraints? SizeConstraints { get; set; }

        /// <summary>
        /// Gets or sets the number of worker threads (<c>-1</c> or <c>0</c> = processor default, <c>1</c> = single-threaded).
        /// This limit applies to each decode operation, not to the whole process.
        /// For simultaneous images, bound outer concurrency and tune this limit per image.
        /// </summary>
        public int NumThreads { get; set; } = -1;

        /// <summary>
        /// Gets or sets the optional custom memory manager.
        /// </summary>
        public JxlMemoryManager? MemoryManager { get; set; }

        /// <summary>
        /// Creates a default <see cref="JxlDecodeOptions"/> instance.
        /// </summary>
        /// <returns>A new <see cref="JxlDecodeOptions"/> instance.</returns>
        public static JxlDecodeOptions CreateDefault()
        {
            return new JxlDecodeOptions();
        }

        /// <summary>
        /// Builds a <see cref="JXLDecompressParams"/> instance populated with all configured decoder options.
        /// </summary>
        /// <returns>A fully configured <see cref="JXLDecompressParams"/> instance.</returns>
        public JXLDecompressParams ToDecompressParams()
        {
            JxlBitDepth effectiveBitDepth = ResolveOutputBitDepth();

            JXLDecompressParams dparams = new JXLDecompressParams
            {
                ColorSpace         = ColorSpace ?? string.Empty,
                ColorSpaceForCmyk  = ColorSpaceForCmyk ?? string.Empty,
                DisplayNits        = DisplayNits,
                RenderSpotcolors   = RenderSpotcolors,
                KeepOrientation    = KeepOrientation,
                Coalescing         = Coalescing,
                AllowPartialInput  = AllowPartialInput,
                MaxPasses          = MaxPasses,
                MaxDownsampling    = NormalizeDownsampling(MaxDownsampling),
                UseImageCallback   = UseImageCallback,
                UnpremultiplyAlpha = UnpremultiplyAlpha,
                MemoryManager      = MemoryManager,
                OutputBitdepth     = effectiveBitDepth,
            };

            PopulateAcceptedFormats(dparams, effectiveBitDepth);

            return dparams;
        }

        private JxlBitDepth ResolveOutputBitDepth()
        {
            if (BitsPerSample > 0)
            {
                return new JxlBitDepth
                {
                    Type                  = JxlBitDepthType.Custom,
                    BitsPerSample         = BitsPerSample,
                    ExponentBitsPerSample = BitsPerSample == 32 ? 8u : 0u,
                };
            }

            return OutputBitdepth;
        }

        private void PopulateAcceptedFormats(JXLDecompressParams dparams, in JxlBitDepth bitDepth)
        {
            if (AcceptedFormats.Count > 0)
            {
                foreach (JxlPixelFormat fmt in AcceptedFormats)
                {
                    dparams.AcceptedFormats.Add(fmt);
                }

                return;
            }

            uint[] channels     = RequestedChannels.HasValue
                ? new[] { Math.Clamp(RequestedChannels.Value, 1u, 4u) }
                : new[] { 1u, 2u, 3u, 4u };
            JxlDataType[] types = SelectAcceptedDataTypes(in bitDepth);

            foreach (uint c in channels)
            {
                foreach (JxlDataType dt in types)
                {
                    dparams.AcceptedFormats.Add(new JxlPixelFormat(c, dt, Endianness, Align));
                }
            }
        }

        private JxlDataType[] SelectAcceptedDataTypes(in JxlBitDepth bitDepth)
        {
            if (RequestedDataType.HasValue)
            {
                return new[] { RequestedDataType.Value };
            }

            if (bitDepth.Type != JxlBitDepthType.Custom)
            {
                return new[] { JxlDataType.UInt8, JxlDataType.UInt16, JxlDataType.Float };
            }

            if (bitDepth.BitsPerSample > 16)
            {
                return new[] { JxlDataType.Float };
            }

            return bitDepth.BitsPerSample > 8
                ? new[] { JxlDataType.UInt16 }
                : new[] { JxlDataType.UInt8 };
        }

        private static nuint NormalizeDownsampling(nuint ratio)
        {
            if (ratio <= 1)
            {
                return 1u;
            }

            if (ratio <= 3)
            {
                return 2u;
            }

            return ratio <= 6 ? (nuint)4u : 8u;
        }
    }
}
