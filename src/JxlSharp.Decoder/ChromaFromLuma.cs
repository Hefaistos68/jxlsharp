// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Chroma-from-luma, computed using heuristics to determine the best linear
// model for the X and B channels from the Y channel.

using System;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Constants and encoding distribution for Chroma-from-Luma (CfL) color correlation.
    /// Ported from <c>lib/jxl/chroma_from_luma.h</c>.
    /// </summary>
    public static class ChromaFromLumaConstants
    {
        // Tile is the rectangular grid of blocks that share color correlation
        // parameters ("factor_x/b" such that residual_b = blue - Y * factor_b).

        /// <summary>Dimension of a CfL color tile in pixels (64).</summary>
        public const nuint kColorTileDim = 64;

        /// <summary>Dimension of a CfL color tile in 8x8 blocks (8).</summary>
        public const nuint kColorTileDimInBlocks = kColorTileDim / FrameDimensions.kBlockDim;

        /// <summary>Default CfL denominator factor (84).</summary>
        public const byte kDefaultColorFactor = 84;

        // JPEG DCT coefficients are at most 1024. CfL constants are at most 127, and
        // the ratio of two entries in a JPEG quantization table is at most 255. Thus,
        // since the CfL denominator is 84, this leaves 12 bits of mantissa to be used.
        // For extra caution, we use 11.

        /// <summary>Fixed-point bit precision for JPEG CfL reconstruction (11).</summary>
        public const byte kCFLFixedPointPrecision = 11;

        /// <summary>Maximum fixed-point ratio for JPEG CfL reconstruction.</summary>
        public const int kCFLFixedPointRatioMax = (256 << kCFLFixedPointPrecision) - 1;

        /// <summary>U32 encoding distribution for the color factor denominator.</summary>
        public static readonly U32Enc kColorFactorDist = new U32Enc(
            U32Distr.Val(kDefaultColorFactor),
            U32Distr.Val(256),
            U32Distr.BitsOffset(8, 2),
            U32Distr.BitsOffset(16, 258));
    }

    /// <summary>
    /// Base linear correlation parameters mapping luma (Y) to X and B channels.
    /// Ported from <c>jxl::ColorCorrelation</c> in <c>lib/jxl/chroma_from_luma.h</c> and <c>lib/jxl/chroma_from_luma.cc</c>.
    /// </summary>
    public sealed class ColorCorrelation
    {
        private readonly float[] _dcFactors = new float[4];

        // range of factor: -1.51 to +1.52
        private uint _colorFactor         = ChromaFromLumaConstants.kDefaultColorFactor;
        private float _colorScale         = 1.0f / ChromaFromLumaConstants.kDefaultColorFactor;
        private float _baseCorrelationX   = 0.0f;
        private float _baseCorrelationB   = OpsinParams.kYToBRatio;
        private int _ytoxDc               = 0;
        private int _ytobDc               = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorCorrelation"/> class.
        /// </summary>
        public ColorCorrelation()
        {
            RecomputeDCFactors();
        }

        /// <summary>
        /// Computes the Y-to-X correlation ratio for a signed tile factor <paramref name="xFactor"/>.
        /// </summary>
        /// <param name="xFactor">Signed 8-bit tile factor for X.</param>
        /// <returns><c>BaseCorrelationX + xFactor * ColorScale</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float YtoXRatio(int xFactor)
        {
            return _baseCorrelationX + xFactor * _colorScale;
        }

        /// <summary>
        /// Computes the Y-to-B correlation ratio for a signed tile factor <paramref name="bFactor"/>.
        /// </summary>
        /// <param name="bFactor">Signed 8-bit tile factor for B.</param>
        /// <returns><c>BaseCorrelationB + bFactor * ColorScale</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float YtoBRatio(int bFactor)
        {
            return _baseCorrelationB + bFactor * _colorScale;
        }

        /// <summary>
        /// Decodes the global DC color correlation parameters from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeDC(BitReader br)
        {
            if (br.ReadFixedBits(1) == 1)
            {
                // All default.
                return true;
            }

            SetColorFactor(U32Coder.Read(ChromaFromLumaConstants.kColorFactorDist, br));

            if (!F16Coder.Read(br, out _baseCorrelationX))
            {
                return false;
            }

            if (MathF.Abs(_baseCorrelationX) > 4.0f)
            {
                return Status.Failure("Base X correlation is out of range");
            }

            if (!F16Coder.Read(br, out _baseCorrelationB))
            {
                return false;
            }

            if (MathF.Abs(_baseCorrelationB) > 4.0f)
            {
                return Status.Failure("Base B correlation is out of range");
            }

            _ytoxDc = (int)br.ReadFixedBits(Common.kBitsPerByte) + sbyte.MinValue;
            _ytobDc = (int)br.ReadFixedBits(Common.kBitsPerByte) + sbyte.MinValue;
            RecomputeDCFactors();

            return true;
        }

        // We consider a CfL map to be JPEG-reconstruction-compatible if base
        // correlation is 0, no DC correlation is used, and we use the default color
        // factor.

        /// <summary>
        /// Returns whether the correlation parameters are compatible with lossless JPEG reconstruction.
        /// </summary>
        /// <returns><see langword="true"/> if JPEG-compatible.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsJPEGCompatible()
        {
            return _baseCorrelationX == 0.0f &&
                   _baseCorrelationB == 0.0f &&
                   _ytobDc == 0 &&
                   _ytoxDc == 0 &&
                   _colorFactor == ChromaFromLumaConstants.kDefaultColorFactor;
        }

        /// <summary>
        /// Computes the fixed-point CfL ratio for JPEG reconstruction from <paramref name="factor"/>.
        /// </summary>
        /// <param name="factor">Signed tile factor.</param>
        /// <returns>Fixed-point JPEG CfL ratio.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RatioJPEG(int factor)
        {
            return (factor * (1 << ChromaFromLumaConstants.kCFLFixedPointPrecision)) /
                   ChromaFromLumaConstants.kDefaultColorFactor;
        }

        /// <summary>
        /// Sets the color factor denominator and recomputes DC correlation factors.
        /// </summary>
        /// <param name="factor">Positive color factor denominator.</param>
        public void SetColorFactor(uint factor)
        {
            _colorFactor = factor;
            _colorScale  = 1.0f / _colorFactor;
            RecomputeDCFactors();
        }

        /// <summary>
        /// Sets the global Y-to-B DC correlation factor and recomputes DC factors.
        /// </summary>
        /// <param name="ytobDc">Signed Y-to-B DC factor.</param>
        public void SetYToBDC(int ytobDc)
        {
            _ytobDc = ytobDc;
            RecomputeDCFactors();
        }

        /// <summary>
        /// Sets the global Y-to-X DC correlation factor and recomputes DC factors.
        /// </summary>
        /// <param name="ytoxDc">Signed Y-to-X DC factor.</param>
        public void SetYToXDC(int ytoxDc)
        {
            _ytoxDc = ytoxDc;
            RecomputeDCFactors();
        }

        /// <summary>Gets the global Y-to-X DC correlation factor.</summary>
        /// <returns>Signed Y-to-X DC factor.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetYToXDC() => _ytoxDc;

        /// <summary>Gets the global Y-to-B DC correlation factor.</summary>
        /// <returns>Signed Y-to-B DC factor.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetYToBDC() => _ytobDc;

        /// <summary>Gets the color factor denominator as a float.</summary>
        /// <returns>Color factor denominator.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetColorFactor() => _colorFactor;

        /// <summary>Gets the base Y-to-X correlation offset.</summary>
        /// <returns>Base X correlation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBaseCorrelationX() => _baseCorrelationX;

        /// <summary>Gets the base Y-to-B correlation offset.</summary>
        /// <returns>Base B correlation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBaseCorrelationB() => _baseCorrelationB;

        /// <summary>Gets the 4-element span of DC correlation factors <c>[YtoX, 0, YtoB, 0]</c>.</summary>
        /// <returns>Read-only span of DC correlation factors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> DCFactors() => _dcFactors;

        /// <summary>
        /// Recomputes <see cref="DCFactors"/> from current correlation parameters.
        /// </summary>
        public void RecomputeDCFactors()
        {
            _dcFactors[0] = YtoXRatio(_ytoxDc);
            _dcFactors[2] = YtoBRatio(_ytobDc);
        }

        internal void ClearBaseCorrelationB()
        {
            _baseCorrelationB = 0.0f;
            RecomputeDCFactors();
        }
    }

    /// <summary>
    /// Stores the per-tile Y-to-X and Y-to-B correlation maps and the global <see cref="ColorCorrelation"/>.
    /// Ported from <c>jxl::ColorCorrelationMap</c> in <c>lib/jxl/chroma_from_luma.h</c> and <c>lib/jxl/chroma_from_luma.cc</c>.
    /// </summary>
    public sealed class ColorCorrelationMap : IDisposable
    {
        private readonly ColorCorrelation _base = new ColorCorrelation();

        /// <summary>Per-tile signed 8-bit Y-to-X correlation factors.</summary>
        public ImagePlane<sbyte>? YtoXMap;

        /// <summary>Per-tile signed 8-bit Y-to-B correlation factors.</summary>
        public ImagePlane<sbyte>? YtoBMap;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="ColorCorrelationMap"/> class.
        /// </summary>
        public ColorCorrelationMap()
        {
        }

        /// <summary>
        /// Gets the global <see cref="ColorCorrelation"/> parameters.
        /// </summary>
        public ColorCorrelation Base => _base;

        /// <summary>
        /// Creates a new <see cref="ColorCorrelationMap"/> for an image of size <paramref name="xsize"/> by <paramref name="ysize"/> pixels.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate the tile maps.</param>
        /// <param name="xsize">Image width in pixels.</param>
        /// <param name="ysize">Image height in pixels.</param>
        /// <param name="xyb">Whether the frame uses XYB color space.</param>
        /// <returns>A <see cref="StatusOr{ColorCorrelationMap}"/> containing the created map or an error.</returns>
        public static StatusOr<ColorCorrelationMap> Create(
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize,
            bool xyb = true)
        {
            JxlMemoryManager mm  = memoryManager ?? JxlMemoryManager.Default;
            nuint xblocks        = Common.DivCeil(xsize, ChromaFromLumaConstants.kColorTileDim);
            nuint yblocks        = Common.DivCeil(ysize, ChromaFromLumaConstants.kColorTileDim);

            StatusOr<ImagePlane<sbyte>> xResult = ImagePlane<sbyte>.Create(mm, xblocks, yblocks);

            if (!xResult.IsOk)
            {
                return xResult.Status;
            }

            StatusOr<ImagePlane<sbyte>> bResult = ImagePlane<sbyte>.Create(mm, xblocks, yblocks);

            if (!bResult.IsOk)
            {
                xResult.Value.Dispose();

                return bResult.Status;
            }

            ColorCorrelationMap result = new ColorCorrelationMap
            {
                YtoXMap = xResult.Value,
                YtoBMap = bResult.Value,
            };

            ImageOps.ZeroFillImage(result.YtoXMap);
            ImageOps.ZeroFillImage(result.YtoBMap);

            if (!xyb)
            {
                result._base.ClearBaseCorrelationB();
            }
            else
            {
                result._base.RecomputeDCFactors();
            }

            return result;
        }

        /// <summary>
        /// Decodes the global DC color correlation parameters from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">Source <see cref="BitReader"/>.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DecodeDC(BitReader br) => _base.DecodeDC(br);

        /// <inheritdoc />
        public void Dispose()
        {
            YtoXMap?.Dispose();
            YtoBMap?.Dispose();
            YtoXMap = null;
            YtoBMap = null;
        }
    }
}
