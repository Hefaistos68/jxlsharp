// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Gain map bundle structure used to serialize gain map data (<c>jhgm</c> box) to and from a buffer
    /// (ported from <c>include/jxl/gain_map.h</c>).
    /// </summary>
    public sealed class JxlGainMapBundle
    {
        /// <summary>
        /// Version number of the gain map bundle.
        /// </summary>
        public byte JhgmVersion { get; set; }

        /// <summary>
        /// Gain map metadata binary blob following ISO 21496-1.
        /// </summary>
        public byte[] GainMapMetadata { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets the size of the gain map metadata in bytes.
        /// </summary>
        public ushort GainMapMetadataSize => (ushort)GainMapMetadata.Length;

        /// <summary>
        /// Indicates whether a color encoding is present.
        /// </summary>
        public bool HasColorEncoding { get; set; }

        /// <summary>
        /// If <see cref="HasColorEncoding"/> is <c>true</c>, this field contains the uncompressed color encoding data.
        /// </summary>
        public JxlColorEncoding ColorEncoding { get; set; }

        /// <summary>
        /// Compressed alternative ICC profile bytes.
        /// </summary>
        public byte[] AltIcc { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets the size of the alternative ICC profile in bytes.
        /// </summary>
        public uint AltIccSize => (uint)AltIcc.Length;

        /// <summary>
        /// Gain map data, which is a JPEG XL naked codestream.
        /// </summary>
        public byte[] GainMap { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets the size of the gain map in bytes.
        /// </summary>
        public uint GainMapSize => (uint)GainMap.Length;
    }
}
