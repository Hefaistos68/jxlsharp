// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Compact representation of image dimensions (best case: 9 bits) so decoders can preallocate early.
    /// Ported from <c>jxl::SizeHeader</c> in <c>lib/jxl/headers.h</c> and <c>lib/jxl/headers.cc</c>.
    /// </summary>
    public sealed class SizeHeader : Fields
    {
        private bool _small; // xsize and ysize <= 256 and divisible by 8.
        private uint _ysizeDiv8Minus1;
        private uint _ysize;
        private uint _ratio;
        private uint _xsizeDiv8Minus1;
        private uint _xsize;

        /// <summary>
        /// Initializes a new instance of the <see cref="SizeHeader"/> class with default values.
        /// </summary>
        public SizeHeader()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "SizeHeader";

        /// <summary>
        /// Gets the decoded image width in pixels.
        /// </summary>
        /// <returns>Image width in pixels.</returns>
        public nuint XSize()
        {
            if (_ratio != 0)
            {
                return Headers.FixedAspectRatioMulTruncate(_ratio, (uint)YSize());
            }

            return _small ? ((_xsizeDiv8Minus1 + 1u) * 8u) : _xsize;
        }

        /// <summary>
        /// Gets the decoded image height in pixels.
        /// </summary>
        /// <returns>Image height in pixels.</returns>
        public nuint YSize()
        {
            return _small ? ((_ysizeDiv8Minus1 + 1u) * 8u) : _ysize;
        }

        /// <summary>
        /// Sets the image dimensions to <paramref name="xsize64"/> by <paramref name="ysize64"/>.
        /// </summary>
        /// <param name="xsize64">Image width in pixels.</param>
        /// <param name="ysize64">Image height in pixels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Set(nuint xsize64, nuint ysize64)
        {
            const nuint kDimensionCap = uint.MaxValue;

            if (xsize64 > kDimensionCap || ysize64 > kDimensionCap)
            {
                return Status.Failure("Image too large");
            }

            if (xsize64 == 0 || ysize64 == 0)
            {
                return Status.Failure("Empty image");
            }

            uint xsize32 = (uint)xsize64;
            uint ysize32 = (uint)ysize64;
            _ratio       = Headers.FindAspectRatio(xsize32, ysize32);
            _small       = ysize64 <= 256 &&
                           (ysize64 % FrameDimensions.KBlockDim) == 0 &&
                           (_ratio != 0 || (xsize64 <= 256 && (xsize64 % FrameDimensions.KBlockDim) == 0));

            if (_small)
            {
                _ysizeDiv8Minus1 = (ysize32 / 8u) - 1u;
            }
            else
            {
                _ysize = ysize32;
            }

            SetXSizeFields(xsize32);

            return XSize() == xsize64 && YSize() == ysize64;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!VisitYSize(visitor))
            {
                return false;
            }

            return VisitXSize(visitor);
        }

        private void SetXSizeFields(uint xsize32)
        {
            if (_ratio != 0)
            {
                return;
            }

            if (_small)
            {
                _xsizeDiv8Minus1 = (xsize32 / 8u) - 1u;
            }
            else
            {
                _xsize = xsize32;
            }
        }

        private Status VisitYSize(Visitor visitor)
        {
            if (!visitor.Bool(false, ref _small))
            {
                return false;
            }

            if (visitor.Conditional(_small) && !visitor.Bits(5, 0, ref _ysizeDiv8Minus1))
            {
                return false;
            }

            if (visitor.Conditional(!_small))
            {
                // (Could still be small, but non-multiple of 8.)
                return visitor.U32(
                    U32Distr.BitsOffset(9, 1),
                    U32Distr.BitsOffset(13, 1),
                    U32Distr.BitsOffset(18, 1),
                    U32Distr.BitsOffset(30, 1),
                    1,
                    ref _ysize);
            }

            return true;
        }

        private Status VisitXSize(Visitor visitor)
        {
            if (!visitor.Bits(3, 0, ref _ratio))
            {
                return false;
            }

            if (visitor.Conditional(_ratio == 0 && _small) && !visitor.Bits(5, 0, ref _xsizeDiv8Minus1))
            {
                return false;
            }

            if (visitor.Conditional(_ratio == 0 && !_small))
            {
                return visitor.U32(
                    U32Distr.BitsOffset(9, 1),
                    U32Distr.BitsOffset(13, 1),
                    U32Distr.BitsOffset(18, 1),
                    U32Distr.BitsOffset(30, 1),
                    1,
                    ref _xsize);
            }

            return true;
        }
    }

    /// <summary>
    /// Compact representation of preview image dimensions (similar to <see cref="SizeHeader"/>,
    /// but uses a different encoding optimized for smaller preview sizes).
    /// Ported from <c>jxl::PreviewHeader</c> in <c>lib/jxl/headers.h</c> and <c>lib/jxl/headers.cc</c>.
    /// </summary>
    public sealed class PreviewHeader : Fields
    {
        private bool _div8; // xsize and ysize divisible by 8.
        private uint _ysizeDiv8;
        private uint _ysize;
        private uint _ratio;
        private uint _xsizeDiv8;
        private uint _xsize;

        /// <summary>
        /// Initializes a new instance of the <see cref="PreviewHeader"/> class with default values.
        /// </summary>
        public PreviewHeader()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "PreviewHeader";

        /// <summary>
        /// Gets the decoded preview width in pixels.
        /// </summary>
        /// <returns>Preview width in pixels.</returns>
        public nuint XSize()
        {
            if (_ratio != 0)
            {
                return Headers.FixedAspectRatioMulTruncate(_ratio, (uint)YSize());
            }

            return _div8 ? (_xsizeDiv8 * 8u) : _xsize;
        }

        /// <summary>
        /// Gets the decoded preview height in pixels.
        /// </summary>
        /// <returns>Preview height in pixels.</returns>
        public nuint YSize()
        {
            return _div8 ? (_ysizeDiv8 * 8u) : _ysize;
        }

        /// <summary>
        /// Sets the preview dimensions to <paramref name="xsize64"/> by <paramref name="ysize64"/>.
        /// </summary>
        /// <param name="xsize64">Preview width in pixels.</param>
        /// <param name="ysize64">Preview height in pixels.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Set(nuint xsize64, nuint ysize64)
        {
            if (xsize64 == 0 || ysize64 == 0)
            {
                return Status.Failure("Empty preview");
            }

            uint xsize32 = (uint)xsize64;
            uint ysize32 = (uint)ysize64;
            _div8        = (xsize64 % FrameDimensions.KBlockDim) == 0 && (ysize64 % FrameDimensions.KBlockDim) == 0;

            if (_div8)
            {
                _ysizeDiv8 = ysize32 / 8u;
            }
            else
            {
                _ysize = ysize32;
            }

            _ratio = Headers.FindAspectRatio(xsize32, ysize32);

            if (_ratio == 0)
            {
                if (_div8)
                {
                    _xsizeDiv8 = xsize32 / 8u;
                }
                else
                {
                    _xsize = xsize32;
                }
            }

            return XSize() == xsize64 && YSize() == ysize64;
        }

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!VisitYSize(visitor))
            {
                return false;
            }

            return VisitXSize(visitor);
        }

        private Status VisitYSize(Visitor visitor)
        {
            if (!visitor.Bool(false, ref _div8))
            {
                return false;
            }

            if (visitor.Conditional(_div8) &&
                !visitor.U32(
                    U32Distr.Val(16),
                    U32Distr.Val(32),
                    U32Distr.BitsOffset(5, 1),
                    U32Distr.BitsOffset(9, 33),
                    1,
                    ref _ysizeDiv8))
            {
                return false;
            }

            if (visitor.Conditional(!_div8))
            {
                return visitor.U32(
                    U32Distr.BitsOffset(6, 1),
                    U32Distr.BitsOffset(8, 65),
                    U32Distr.BitsOffset(10, 321),
                    U32Distr.BitsOffset(12, 1345),
                    1,
                    ref _ysize);
            }

            return true;
        }

        private Status VisitXSize(Visitor visitor)
        {
            if (!visitor.Bits(3, 0, ref _ratio))
            {
                return false;
            }

            if (visitor.Conditional(_ratio == 0 && _div8) &&
                !visitor.U32(
                    U32Distr.Val(16),
                    U32Distr.Val(32),
                    U32Distr.BitsOffset(5, 1),
                    U32Distr.BitsOffset(9, 33),
                    1,
                    ref _xsizeDiv8))
            {
                return false;
            }

            if (visitor.Conditional(_ratio == 0 && !_div8))
            {
                return visitor.U32(
                    U32Distr.BitsOffset(6, 1),
                    U32Distr.BitsOffset(8, 65),
                    U32Distr.BitsOffset(10, 321),
                    U32Distr.BitsOffset(12, 1345),
                    1,
                    ref _xsize);
            }

            return true;
        }
    }

    /// <summary>
    /// Codestream animation header defining frame rate (ticks per second), loop count, and timecodes.
    /// Ported from <c>jxl::AnimationHeader</c> in <c>lib/jxl/headers.h</c> and <c>lib/jxl/headers.cc</c>.
    /// </summary>
    public sealed class AnimationHeader : Fields
    {
        /// <summary>Ticks per second numerator (expressed as rational number to support NTSC).</summary>
        public uint TpsNumerator;

        /// <summary>Ticks per second denominator.</summary>
        public uint TpsDenominator;

        /// <summary>Number of animation loops (0 means to repeat infinitely).</summary>
        public uint NumLoops;

        /// <summary>Whether per-frame timecodes are present.</summary>
        public bool HaveTimecodes;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationHeader"/> class with default values.
        /// </summary>
        public AnimationHeader()
        {
            Bundle.Init(this);
        }

        /// <inheritdoc />
        public override string BundleName => "AnimationHeader";

        /// <inheritdoc />
        public override Status VisitFields(Visitor visitor)
        {
            if (!visitor.U32(
                U32Distr.Val(100),
                U32Distr.Val(1000),
                U32Distr.BitsOffset(10, 1),
                U32Distr.BitsOffset(30, 1),
                1,
                ref TpsNumerator))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Val(1),
                U32Distr.Val(1001),
                U32Distr.BitsOffset(8, 1),
                U32Distr.BitsOffset(10, 1),
                1,
                ref TpsDenominator))
            {
                return false;
            }

            if (!visitor.U32(
                U32Distr.Val(0),
                U32Distr.Bits(3),
                U32Distr.Bits(16),
                U32Distr.Bits(32),
                0,
                ref NumLoops))
            {
                return false;
            }

            return visitor.Bool(false, ref HaveTimecodes);
        }
    }

    /// <summary>
    /// Codestream header marker and helper functions for reading and writing <see cref="SizeHeader"/>.
    /// Ported from <c>lib/jxl/headers.h</c>, <c>lib/jxl/headers.cc</c>, and <c>lib/jxl/enc_fields.cc</c>.
    /// </summary>
    public static class Headers
    {
        // Reserved by ISO/IEC 10918-1. LF causes files opened in text mode to be
        // rejected because the marker changes to 0x0D instead. The 0xFF prefix also
        // ensures there were no 7-bit transmission limitations.

        /// <summary>Second byte of the 2-byte JPEG XL codestream signature (<c>0xFF, 0x0A</c>).</summary>
        public const byte KCodestreamMarker = 0x0A;

        private static readonly (uint Num, uint Den)[] FixedRatios =
        {
            (1, 1),   // square
            (12, 10), // 1.2:1
            (4, 3),   // camera
            (3, 2),   // mobile camera
            (16, 9),  // camera/display
            (5, 4),   // 1.25:1
            (2, 1),   // 2:1
        };

        /// <summary>
        /// Reads a <see cref="SizeHeader"/> from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">Source bit reader.</param>
        /// <param name="size">Destination size header.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ReadSizeHeader(BitReader reader, SizeHeader size)
        {
            return Bundle.Read(reader, size);
        }

        /// <summary>
        /// Writes a <see cref="SizeHeader"/> to <paramref name="writer"/>.
        /// </summary>
        /// <param name="size">Size header to write.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status WriteSizeHeader(SizeHeader size, BitWriter writer, LayerType layer, AuxOut? auxOut)
        {
            return Bundle.Write(size, writer, layer, auxOut);
        }

        internal static uint FixedAspectRatioMulTruncate(uint ratio, uint multiplicand)
        {
            (uint num, uint den) = FixedRatios[ratio - 1];

            return (uint)((ulong)multiplicand * num / den);
        }

        internal static uint FindAspectRatio(uint xsize, uint ysize)
        {
            for (uint r = 1; r < 8; ++r)
            {
                if (xsize == FixedAspectRatioMulTruncate(r, ysize))
                {
                    return r;
                }
            }

            return 0; // Must send xsize instead
        }
    }
}
