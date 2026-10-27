// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Data type for the sample values per channel per pixel.
    /// </summary>
    public enum JxlDataType
    {
        /// <summary>
        /// Use 32-bit single-precision floating point values, with range 0.0-1.0
        /// (within gamut, may go outside this range for wide color gamut). Floating
        /// point output, either JXL_TYPE_FLOAT or JXL_TYPE_FLOAT16, is recommended
        /// for HDR and wide gamut images when color profile conversion is required.
        /// </summary>
        Float = 0,

        /// <summary>
        /// Use type uint8_t. May clip wide color gamut data.
        /// </summary>
        UInt8 = 2,

        /// <summary>
        /// Use type uint16_t. May clip wide color gamut data.
        /// </summary>
        UInt16 = 3,

        /// <summary>
        /// Use 16-bit IEEE 754 half-precision floating point values.
        /// </summary>
        Float16 = 5,
    }

    /// <summary>
    /// Ordering of multi-byte data.
    /// </summary>
    public enum JxlEndianness
    {
        /// <summary>
        /// Use the endianness of the system, either little endian or big endian,
        /// without forcing either specific endianness. Do not use if pixel data
        /// should be exported to a well defined format.
        /// </summary>
        NativeEndian = 0,

        /// <summary>
        /// Force little endian.
        /// </summary>
        LittleEndian = 1,

        /// <summary>
        /// Force big endian.
        /// </summary>
        BigEndian = 2,
    }

    /// <summary>
    /// Settings for the interpretation of UINT input and output buffers.
    /// (buffers using a FLOAT data type are not affected by this)
    /// </summary>
    public enum JxlBitDepthType
    {
        /// <summary>
        /// This is the default setting, where the encoder expects the input pixels
        /// to use the full range of the pixel format data type (e.g. for UINT16, the
        /// input range is 0 .. 65535 and the value 65535 is mapped to 1.0 when
        /// converting to float), and the decoder uses the full range to output
        /// pixels. If the bit depth in the basic info is different from this, the
        /// encoder expects the values to be rescaled accordingly (e.g. multiplied by
        /// 65535/4095 for a 12-bit image using UINT16 input data type).
        /// </summary>
        FromPixelFormat = 0,

        /// <summary>
        /// If this setting is selected, the encoder expects the input pixels to be
        /// in the range defined by the bits_per_sample value of the basic info (e.g.
        /// for 12-bit images using UINT16 input data types, the allowed range is
        /// 0 .. 4095 and the value 4095 is mapped to 1.0 when converting to float),
        /// and the decoder outputs pixels in this range.
        /// </summary>
        FromCodestream = 1,

        /// <summary>
        /// This setting can only be used in the decoder to select a custom range for
        /// pixel output.
        /// </summary>
        Custom = 2,
    }

    /// <summary>
    /// Data type for the sample values per channel per pixel for the output buffer
    /// for pixels. This is not necessarily the same as the data type encoded in the
    /// codestream. The channels are interleaved per pixel. The pixels are
    /// organized row by row, left to right, top to bottom.
    /// </summary>
    public struct JxlPixelFormat : IEquatable<JxlPixelFormat>
    {
        /// <summary>
        /// Amount of channels available in a pixel buffer.
        /// 1: single-channel data, e.g. grayscale or a single extra channel
        /// 2: single-channel + alpha
        /// 3: trichromatic, e.g. RGB
        /// 4: trichromatic + alpha
        /// </summary>
        public uint NumChannels { get; set; }

        /// <summary>
        /// Data type of each channel.
        /// </summary>
        public JxlDataType DataType { get; set; }

        /// <summary>
        /// Whether multi-byte data types are represented in big endian or little
        /// endian format. This applies to JXL_TYPE_UINT16 and JXL_TYPE_FLOAT.
        /// </summary>
        public JxlEndianness Endianness { get; set; }

        /// <summary>
        /// Align scanlines to a multiple of align bytes, or 0 to require no
        /// alignment at all (which has the same effect as value 1).
        /// </summary>
        public nuint Align { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlPixelFormat"/> struct.
        /// </summary>
        /// <param name="numChannels">Number of channels.</param>
        /// <param name="dataType">Channel sample data type.</param>
        /// <param name="endianness">Byte ordering for multi-byte samples.</param>
        /// <param name="align">Row alignment in bytes.</param>
        public JxlPixelFormat(uint numChannels, JxlDataType dataType, JxlEndianness endianness, nuint align)
        {
            NumChannels = numChannels;
            DataType    = dataType;
            Endianness  = endianness;
            Align       = align;
        }

        /// <inheritdoc />
        public bool Equals(JxlPixelFormat other)
        {
            return NumChannels == other.NumChannels &&
                   DataType == other.DataType &&
                   Endianness == other.Endianness &&
                   Align == other.Align;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlPixelFormat other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(NumChannels, DataType, Endianness, Align);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlPixelFormat"/> values are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlPixelFormat left, JxlPixelFormat right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlPixelFormat"/> values are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlPixelFormat left, JxlPixelFormat right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Data type for describing the interpretation of the input and output buffers
    /// in terms of the range of allowed input and output pixel values.
    /// </summary>
    public struct JxlBitDepth : IEquatable<JxlBitDepth>
    {
        /// <summary>
        /// Bit depth setting, see comment on <see cref="JxlBitDepthType"/>.
        /// </summary>
        public JxlBitDepthType Type { get; set; }

        /// <summary>
        /// Custom bits per sample.
        /// </summary>
        public uint BitsPerSample { get; set; }

        /// <summary>
        /// Custom exponent bits per sample.
        /// </summary>
        public uint ExponentBitsPerSample { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlBitDepth"/> struct.
        /// </summary>
        /// <param name="type">Bit depth interpretation mode.</param>
        /// <param name="bitsPerSample">Bits per sample.</param>
        /// <param name="exponentBitsPerSample">Exponent bits per sample.</param>
        public JxlBitDepth(JxlBitDepthType type, uint bitsPerSample, uint exponentBitsPerSample)
        {
            Type                  = type;
            BitsPerSample         = bitsPerSample;
            ExponentBitsPerSample = exponentBitsPerSample;
        }

        /// <inheritdoc />
        public bool Equals(JxlBitDepth other)
        {
            return Type == other.Type &&
                   BitsPerSample == other.BitsPerSample &&
                   ExponentBitsPerSample == other.ExponentBitsPerSample;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlBitDepth other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(Type, BitsPerSample, ExponentBitsPerSample);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBitDepth"/> values are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlBitDepth left, JxlBitDepth right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBitDepth"/> values are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlBitDepth left, JxlBitDepth right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Data type holding the 4-character type name of an ISOBMFF box.
    /// </summary>
    public readonly struct JxlBoxType : IEquatable<JxlBoxType>
    {
        private readonly uint _packed;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlBoxType"/> struct from 4 ASCII bytes.
        /// </summary>
        /// <param name="b0">First character byte.</param>
        /// <param name="b1">Second character byte.</param>
        /// <param name="b2">Third character byte.</param>
        /// <param name="b3">Fourth character byte.</param>
        public JxlBoxType(byte b0, byte b1, byte b2, byte b3)
        {
            _packed = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlBoxType"/> struct from a 4-character string.
        /// </summary>
        /// <param name="boxType">4-character string.</param>
        public JxlBoxType(string boxType)
        {
            if (boxType is null || boxType.Length != 4)
            {
                throw new ArgumentException("Box type must be exactly 4 characters.", nameof(boxType));
            }

            _packed = ((uint)(byte)boxType[0] << 24) |
                      ((uint)(byte)boxType[1] << 16) |
                      ((uint)(byte)boxType[2] << 8) |
                      (byte)boxType[3];
        }

        /// <summary>
        /// Gets the packed 32-bit big-endian representation of the box type.
        /// </summary>
        public uint Packed => _packed;

        /// <inheritdoc />
        public bool Equals(JxlBoxType other)
        {
            return _packed == other._packed;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is JxlBoxType other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)_packed;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            char[] chars = new char[4];

            chars[0] = (char)((_packed >> 24) & 0xFF);
            chars[1] = (char)((_packed >> 16) & 0xFF);
            chars[2] = (char)((_packed >> 8) & 0xFF);
            chars[3] = (char)(_packed & 0xFF);

            return new string(chars);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBoxType"/> values are equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(JxlBoxType left, JxlBoxType right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="JxlBoxType"/> values are not equal.
        /// </summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(JxlBoxType left, JxlBoxType right)
        {
            return !left.Equals(right);
        }
    }
}
