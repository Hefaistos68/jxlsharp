// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Encoder speed/effort tiers.
    /// Ported from <c>jxl::SpeedTier</c> in <c>lib/jxl/common.h</c>.
    /// </summary>
    public enum SpeedTier
    {
        /// <summary>
        /// Try multiple combinations of Glacier flags for modular mode. Otherwise like <see cref="kGlacier"/>.
        /// </summary>
        kTectonicPlate = -1,

        /// <summary>
        /// Learn a global tree in Modular mode.
        /// </summary>
        kGlacier = 0,

        /// <summary>
        /// Turns on FindBestQuantizationHQ loop. Equivalent to "guetzli" mode.
        /// </summary>
        kTortoise = 1,

        /// <summary>
        /// Turns on FindBestQuantization butteraugli loop.
        /// </summary>
        kKitten = 2,

        /// <summary>
        /// Turns on dots, patches, and spline detection by default, as well as full context clustering. Default.
        /// </summary>
        kSquirrel = 3,

        /// <summary>
        /// Turns on error diffusion and full AC strategy heuristics. Equivalent to "fast" mode.
        /// </summary>
        kWombat = 4,

        /// <summary>
        /// Turns on simple heuristics for AC strategy, quant field, gaborish by default, non-default cmap, initial quant field, non-default CFL.
        /// </summary>
        kHare = 5,

        /// <summary>
        /// Turns on clustering and enables coefficient reordering.
        /// </summary>
        kCheetah = 6,

        /// <summary>
        /// Turns off most encoder features. Does context clustering. Modular: uses fixed tree with Weighted predictor.
        /// </summary>
        kFalcon = 7,

        /// <summary>
        /// Currently fastest possible setting for VarDCT. Modular: uses fixed tree with Gradient predictor.
        /// </summary>
        kThunder = 8,

        /// <summary>
        /// VarDCT: same as <see cref="kThunder"/>. Modular: no tree, Gradient predictor, fast histograms.
        /// </summary>
        kLightning = 9,
    }

    /// <summary>
    /// Callback delegate for dumping intermediate encoder debug images.
    /// </summary>
    /// <param name="opaque">User-supplied opaque context.</param>
    /// <param name="label">Debug image label.</param>
    /// <param name="xsize">Image width in pixels.</param>
    /// <param name="ysize">Image height in pixels.</param>
    /// <param name="color">Color encoding of the debug image.</param>
    /// <param name="pixels">Interleaved 16-bit unsigned pixel data.</param>
    public delegate void JxlDebugImageCallback(
        object? opaque,
        string label,
        nuint xsize,
        nuint ysize,
        JxlColorEncoding color,
        ReadOnlySpan<ushort> pixels);

    /// <summary>
    /// Configuration for a single progressive pass in <see cref="ProgressiveMode"/>.
    /// Ported from <c>jxl::PassDefinition</c> in <c>lib/jxl/enc_progressive_split.h</c>.
    /// </summary>
    public struct PassDefinition
    {
        /// <summary>
        /// Sentinel indicating a pass is not marked suitable for any downsampling factor.
        /// </summary>
        public static readonly nuint KNoDownsamplingFactor = nuint.MaxValue;

        /// <summary>
        /// Side of the square of the coefficients that should be kept in each 8x8 block (in <c>[2..8]</c>).
        /// </summary>
        public nuint NumCoefficients;

        /// <summary>
        /// How much to shift the encoded values by, with rounding.
        /// </summary>
        public nuint Shift;

        /// <summary>
        /// If specified, indicates that if the requested downsampling factor is sufficiently high,
        /// decoding can stop after this pass.
        /// </summary>
        public nuint SuitableForDownsamplingOfAtLeast;

        /// <summary>
        /// Initializes a new instance of the <see cref="PassDefinition"/> struct without a downsampling target.
        /// </summary>
        /// <param name="numCoefficients">Number of coefficients per 8x8 side.</param>
        /// <param name="shift">Quantization bit shift.</param>
        public PassDefinition(nuint numCoefficients, nuint shift)
        {
            NumCoefficients                  = numCoefficients;
            Shift                            = shift;
            SuitableForDownsamplingOfAtLeast = KNoDownsamplingFactor;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PassDefinition"/> struct.
        /// </summary>
        /// <param name="numCoefficients">Number of coefficients per 8x8 side.</param>
        /// <param name="shift">Quantization bit shift.</param>
        /// <param name="suitableForDownsamplingOfAtLeast">Minimum downsampling factor.</param>
        public PassDefinition(
            nuint numCoefficients,
            nuint shift,
            nuint suitableForDownsamplingOfAtLeast)
        {
            NumCoefficients                  = numCoefficients;
            Shift                            = shift;
            SuitableForDownsamplingOfAtLeast = suitableForDownsamplingOfAtLeast;
        }
    }

    /// <summary>
    /// Schedule of progressive AC passes for a frame.
    /// Ported from <c>jxl::ProgressiveMode</c> in <c>lib/jxl/enc_progressive_split.h</c>.
    /// </summary>
    public sealed class ProgressiveMode
    {
        /// <summary>
        /// Number of passes (<c>1..KMaxNumPasses</c>).
        /// </summary>
        public nuint NumPasses = 1;

        /// <summary>
        /// Per-pass definitions.
        /// </summary>
        public readonly PassDefinition[] Passes = new PassDefinition[FrameDimensions.KMaxNumPasses];

        /// <summary>
        /// Initializes a new instance of the <see cref="ProgressiveMode"/> class with a single full pass.
        /// </summary>
        public ProgressiveMode()
        {
            Passes[0] = new PassDefinition(8, 0, 1);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProgressiveMode"/> class from a span of pass definitions.
        /// </summary>
        /// <param name="passes">Pass definitions in progressive order.</param>
        public ProgressiveMode(ReadOnlySpan<PassDefinition> passes)
        {
            Debug.Assert(passes.Length <= FrameDimensions.KMaxNumPasses);
            NumPasses = (nuint)passes.Length;

            PassDefinition previousPass    = new PassDefinition(1, 0, PassDefinition.KNoDownsamplingFactor);
            nuint lastDownsamplingFactor   = PassDefinition.KNoDownsamplingFactor;

            for (int i = 0; i < passes.Length; i++)
            {
                PassDefinition p = passes[i];
                Debug.Assert(
                    p.NumCoefficients > previousPass.NumCoefficients ||
                    (p.NumCoefficients == previousPass.NumCoefficients && p.Shift < previousPass.Shift));
                Debug.Assert(
                    p.SuitableForDownsamplingOfAtLeast == PassDefinition.KNoDownsamplingFactor ||
                    p.SuitableForDownsamplingOfAtLeast <= lastDownsamplingFactor);

                if (p.SuitableForDownsamplingOfAtLeast != PassDefinition.KNoDownsamplingFactor)
                {
                    lastDownsamplingFactor = p.SuitableForDownsamplingOfAtLeast;
                }

                previousPass = p;
                Passes[i]    = p;
            }
        }
    }

    /// <summary>
    /// Splits quantized DCT coefficients across multiple progressive passes.
    /// Ported from <c>jxl::ProgressiveSplitter</c> in <c>lib/jxl/enc_progressive_split.h</c> and <c>lib/jxl/enc_progressive_split.cc</c>.
    /// </summary>
    public sealed class ProgressiveSplitter
    {
        private ProgressiveMode _mode = new ProgressiveMode();

        /// <summary>
        /// Sets the active <see cref="ProgressiveMode"/>.
        /// </summary>
        /// <param name="mode">Progressive pass schedule.</param>
        public void SetProgressiveMode(ProgressiveMode mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Gets the number of passes in the current schedule.
        /// </summary>
        /// <returns>Number of passes.</returns>
        public nuint GetNumPasses()
        {
            return _mode.NumPasses;
        }

        /// <summary>
        /// Populates a <see cref="Passes"/> header bundle from the current schedule.
        /// </summary>
        /// <param name="passes">Target <see cref="Passes"/> bundle.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitPasses(Passes passes)
        {
            passes.NumPasses     = (uint)GetNumPasses();
            passes.NumDownsample = 0;

            if (passes.NumPasses == 0)
            {
                return Status.Failure("num_passes cannot be 0");
            }

            passes.Shift[passes.NumPasses - 1] = 0;

            if (passes.NumPasses == 1)
            {
                return true;
            }

            for (uint i = 0; i < _mode.NumPasses - 1; ++i)
            {
                nuint minDownsamplingFactor = _mode.Passes[i].SuitableForDownsamplingOfAtLeast;
                passes.Shift[i]             = (uint)_mode.Passes[i].Shift;

                if (minDownsamplingFactor > 1 && minDownsamplingFactor != PassDefinition.KNoDownsamplingFactor)
                {
                    passes.Downsample[passes.NumDownsample] = (uint)minDownsamplingFactor;
                    passes.LastPass[passes.NumDownsample]   = i;

                    if (_mode.Passes[i + 1].SuitableForDownsamplingOfAtLeast < minDownsamplingFactor)
                    {
                        passes.NumDownsample += 1;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Splits a 32-bit quantized AC transform block across the configured passes.
        /// </summary>
        /// <param name="block">Input quantized coefficient block.</param>
        /// <param name="acs">Transform strategy.</param>
        /// <param name="bx">Horizontal block index.</param>
        /// <param name="by">Vertical block index.</param>
        /// <param name="output">Per-pass output block buffers.</param>
        public void SplitACCoefficients(
            ReadOnlySpan<int> block,
            AcStrategy acs,
            nuint bx,
            nuint by,
            Span<Memory<int>> output)
        {
            _ = bx;
            _ = by;
            nuint size = acs.CoveredBlocksX() * acs.CoveredBlocksY() * FrameDimensions.kDCTBlockSize;

            if (_mode.NumPasses == 1)
            {
                block.Slice(0, (int)size).CopyTo(output[0].Span);

                return;
            }

            nuint ncoeffsAllDoneFromEarlierPasses = 1;
            int previousPassShift                 = 0;

            for (nuint numPass = 0; numPass < _mode.NumPasses; numPass++)
            {
                Span<int> dstPass = output[(int)numPass].Span.Slice(0, (int)size);
                dstPass.Clear();

                int passShift      = (int)_mode.Passes[numPass].Shift;
                nuint frameNcoeffs = _mode.Passes[numPass].NumCoefficients;
                nuint xsize        = acs.CoveredBlocksX();
                nuint ysize        = acs.CoveredBlocksY();
                CoeffOrder.CoefficientLayout(ref ysize, ref xsize);

                SplitPassInt32(
                    block,
                    dstPass,
                    xsize,
                    ysize,
                    (frameNcoeffs, ncoeffsAllDoneFromEarlierPasses),
                    (previousPassShift, passShift));

                if (_mode.Passes[numPass].Shift == 0)
                {
                    ncoeffsAllDoneFromEarlierPasses = frameNcoeffs;
                }

                previousPassShift = passShift;
            }
        }

        /// <summary>
        /// Splits a 16-bit quantized AC transform block across the configured passes.
        /// </summary>
        /// <param name="block">Input quantized coefficient block.</param>
        /// <param name="acs">Transform strategy.</param>
        /// <param name="bx">Horizontal block index.</param>
        /// <param name="by">Vertical block index.</param>
        /// <param name="output">Per-pass output block buffers.</param>
        public void SplitACCoefficients(
            ReadOnlySpan<short> block,
            AcStrategy acs,
            nuint bx,
            nuint by,
            Span<Memory<short>> output)
        {
            _ = bx;
            _ = by;
            nuint size = acs.CoveredBlocksX() * acs.CoveredBlocksY() * FrameDimensions.kDCTBlockSize;

            if (_mode.NumPasses == 1)
            {
                block.Slice(0, (int)size).CopyTo(output[0].Span);

                return;
            }

            nuint ncoeffsAllDoneFromEarlierPasses = 1;
            int previousPassShift                 = 0;

            for (nuint numPass = 0; numPass < _mode.NumPasses; numPass++)
            {
                Span<short> dstPass = output[(int)numPass].Span.Slice(0, (int)size);
                dstPass.Clear();

                int passShift      = (int)_mode.Passes[numPass].Shift;
                nuint frameNcoeffs = _mode.Passes[numPass].NumCoefficients;
                nuint xsize        = acs.CoveredBlocksX();
                nuint ysize        = acs.CoveredBlocksY();
                CoeffOrder.CoefficientLayout(ref ysize, ref xsize);

                SplitPassInt16(
                    block,
                    dstPass,
                    xsize,
                    ysize,
                    (frameNcoeffs, ncoeffsAllDoneFromEarlierPasses),
                    (previousPassShift, passShift));

                if (_mode.Passes[numPass].Shift == 0)
                {
                    ncoeffsAllDoneFromEarlierPasses = frameNcoeffs;
                }

                previousPassShift = passShift;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ShiftRightRound0(int v, int shift)
        {
            int oneIfNegative = (int)((uint)v >> 31);
            int add           = (oneIfNegative << shift) - oneIfNegative;

            return (v + add) >> shift;
        }

        private static void SplitPassInt32(
            ReadOnlySpan<int> block,
            Span<int> dstPass,
            nuint xsize,
            nuint ysize,
            (nuint FrameNcoeffs, nuint NcoeffsDone) ncoeffs,
            (int PreviousPassShift, int PassShift) shifts)
        {
            nuint maxY  = ysize * ncoeffs.FrameNcoeffs;
            nuint maxX  = xsize * ncoeffs.FrameNcoeffs;
            nuint doneY = ysize * ncoeffs.NcoeffsDone;
            nuint doneX = xsize * ncoeffs.NcoeffsDone;

            for (nuint y = 0; y < maxY; y++)
            {
                for (nuint x = 0; x < maxX; x++)
                {
                    if (x < doneX && y < doneY)
                    {
                        continue;
                    }

                    int pos = (int)((y * xsize * FrameDimensions.kBlockDim) + x);
                    int v   = block[pos];

                    if (shifts.PreviousPassShift != 0)
                    {
                        int previousV = ShiftRightRound0(v, shifts.PreviousPassShift) * (1 << shifts.PreviousPassShift);
                        v            -= previousV;
                    }

                    dstPass[pos] = ShiftRightRound0(v, shifts.PassShift);
                }
            }
        }

        private static void SplitPassInt16(
            ReadOnlySpan<short> block,
            Span<short> dstPass,
            nuint xsize,
            nuint ysize,
            (nuint FrameNcoeffs, nuint NcoeffsDone) ncoeffs,
            (int PreviousPassShift, int PassShift) shifts)
        {
            nuint maxY  = ysize * ncoeffs.FrameNcoeffs;
            nuint maxX  = xsize * ncoeffs.FrameNcoeffs;
            nuint doneY = ysize * ncoeffs.NcoeffsDone;
            nuint doneX = xsize * ncoeffs.NcoeffsDone;

            for (nuint y = 0; y < maxY; y++)
            {
                for (nuint x = 0; x < maxX; x++)
                {
                    if (x < doneX && y < doneY)
                    {
                        continue;
                    }

                    int pos = (int)((y * xsize * FrameDimensions.kBlockDim) + x);
                    int v   = block[pos];

                    if (shifts.PreviousPassShift != 0)
                    {
                        int previousV = ShiftRightRound0(v, shifts.PreviousPassShift) * (1 << shifts.PreviousPassShift);
                        v            -= previousV;
                    }

                    dstPass[pos] = (short)ShiftRightRound0(v, shifts.PassShift);
                }
            }
        }
    }

    /// <summary>
    /// Parameters and flags that govern JXL compression.
    /// Ported from <c>jxl::CompressParams</c> in <c>lib/jxl/enc_params.h</c>.
    /// </summary>
    public sealed class CompressParams
    {
        /// <summary>Minimum Butteraugli distance for dynamic AR (Gaborish/EPF).</summary>
        public const float KMinButteraugliForDynamicAR = 0.5f;

        /// <summary>Minimum Butteraugli distance for dot detection.</summary>
        public const float KMinButteraugliForDots = 3.0f;

        /// <summary>Minimum Butteraugli distance to subtract original patches.</summary>
        public const float KMinButteraugliToSubtractOriginalPatches = 3.0f;

        /// <summary>Minimum Butteraugli distance for automatic noise synthesis (always off by default).</summary>
        public const float KMinButteraugliForNoise = 99.0f;

        /// <summary>Minimum non-zero Butteraugli distance the encoder accepts.</summary>
        public const float KMinButteraugliDistance = 0.05f;

        /// <summary>Tile size for encoder-side processing (64).</summary>
        public const nuint KEncTileDim = 64;

        /// <summary>Tile size in 8x8 blocks for encoder-side processing (8).</summary>
        public const nuint KEncTileDimInBlocks = KEncTileDim / FrameDimensions.kBlockDim;

        /// <summary>Target Butteraugli distance (<c>0.0f</c> = lossless, <c>1.0f</c> = visually lossless).</summary>
        public float ButteraugliDistance = 1.0f;

        /// <summary>Explicit distances for extra channels (value of -1 represents default).</summary>
        public List<float> EcDistance = new List<float>();

        /// <summary>Try to achieve a maximum pixel-by-pixel error on each channel.</summary>
        public bool MaxErrorMode;

        /// <summary>Maximum per-channel error when <see cref="MaxErrorMode"/> is enabled.</summary>
        public readonly float[] MaxError = new float[3];

        /// <summary>Whether perceptual optimizations are disabled.</summary>
        public bool DisablePerceptualOptimizations;

        /// <summary>Encoder speed/effort tier.</summary>
        public SpeedTier SpeedTier = SpeedTier.kSquirrel;

        /// <summary>Brotli compression effort (-1 = default).</summary>
        public int BrotliEffort = -1;

        /// <summary>Decoding speed tier (0 = default, 4 = fastest decoding).</summary>
        public nuint DecodingSpeedTier;

        /// <summary>Color transform mode.</summary>
        public ColorTransform ColorTransform = ColorTransform.XYB;

        /// <summary>Whether Modular encoding mode is enabled for the frame.</summary>
        public bool ModularMode;

        /// <summary>Modular group size shift (0=128, 1=256, 2=512, 3=1024, -1=encoder chooses).</summary>
        public int ModularGroupSizeShift = -1;

        /// <summary>Preview generation override.</summary>
        public Override Preview = Override.Default;

        /// <summary>Noise synthesis override.</summary>
        public Override Noise = Override.Default;

        /// <summary>Dot detection override.</summary>
        public Override Dots = Override.Default;

        /// <summary>Patch detection override.</summary>
        public Override Patches = Override.Default;

        /// <summary>Gaborish inverse filter override.</summary>
        public Override Gaborish = Override.Default;

        /// <summary>Edge-preserving filter iterations (-1 = auto, 0..3 = explicit).</summary>
        public int Epf = -1;

        /// <summary>Progressive mode override.</summary>
        public Override ProgressiveMode = Override.Default;

        /// <summary>Quantized-progressive mode override.</summary>
        public Override QProgressiveMode = Override.Default;

        /// <summary>Put center groups first in the bitstream.</summary>
        public bool CenterFirst;

        /// <summary>Horizontal pixel coordinate of the center.</summary>
        public nuint CenterX = nuint.MaxValue;

        /// <summary>Vertical pixel coordinate of the center.</summary>
        public nuint CenterY = nuint.MaxValue;

        /// <summary>Progressive DC level (-1 = auto, 0 = off, 1..2 = DC frames).</summary>
        public int ProgressiveDc = -1;

        /// <summary>Preserve color of invisible (alpha = 0) pixels.</summary>
        public Override KeepInvisible = Override.Default;

        /// <summary>Configured CMS interface.</summary>
        public JxlCmsInterface? Cms;

        /// <summary>Whether <see cref="Cms"/> has been explicitly configured.</summary>
        public bool CmsSet;

        /// <summary>Force usage of CfL when doing JPEG recompression.</summary>
        public bool ForceCflJpegRecompression = true;

        /// <summary>Apply LF smoothing when doing JPEG recompression.</summary>
        public int ForceLfsJpegRecompression = -1;

        /// <summary>Use Brotli compression for metadata boxes derived from a JPEG frame.</summary>
        public bool JpegCompressBoxes = true;

        /// <summary>Preserve Exif metadata when doing JPEG recompression.</summary>
        public bool JpegKeepExif = true;

        /// <summary>Preserve XMP metadata when doing JPEG recompression.</summary>
        public bool JpegKeepXmp = true;

        /// <summary>Preserve JUMBF metadata when doing JPEG recompression.</summary>
        public bool JpegKeepJumbf = true;

        /// <summary>Simulated 35mm camera ISO photon noise level.</summary>
        public float PhotonNoiseIso;

        /// <summary>Modular encoding options.</summary>
        public ModularOptions Options = ModularOptions.Default();

        /// <summary>Responsive (Squeeze) transform flag (-1 = auto, 0 = off, 1 = on).</summary>
        public int Responsive = -1;

        /// <summary>Modular RCT colorspace index (-1 = auto).</summary>
        public int Colorspace = -1;

        /// <summary>Move-to-front starting channel index (-1 = disabled).</summary>
        public int MoveToFrontFromChannel = -1;

        /// <summary>Use global channel palette if #colors is less than this percentage of range.</summary>
        public float ChannelColorsPreTransformPercent = 95.0f;

        /// <summary>Use local channel palette if #colors is less than this percentage of range.</summary>
        public float ChannelColorsPercent = 80.0f;

        /// <summary>Maximum number of palette colors.</summary>
        public int PaletteColors = 1 << 10;

        /// <summary>Whether lossy palette quantization is enabled.</summary>
        public bool LossyPalette;

        /// <summary>Resampling factor (-1 = auto, 1, 2, 4, 8).</summary>
        public int Resampling = -1;

        /// <summary>Extra channel resampling factor (-1 = auto, 1, 2, 4, 8).</summary>
        public int EcResampling = -1;

        /// <summary>Skip downsampling before encoding if input is already downsampled.</summary>
        public bool AlreadyDownsampled;

        /// <summary>Original target Butteraugli distance before resampling adjustment.</summary>
        public float OriginalButteraugliDistance = -1.0f;

        /// <summary>Quantization AC rescaling factor.</summary>
        public float QuantAcRescale = 1.0f;

        /// <summary>Codestream level to conform to (-1 = don't care, 5 or 10).</summary>
        public int Level = -1;

        /// <summary>Buffering mode (-1 = auto).</summary>
        public int Buffering = -1;

        /// <summary>Output streaming mode (0 = buffered, 1 = seek-based streaming, 2 = OOO jxlp).</summary>
        public int OutputMode;

        /// <summary>Whether full-image heuristics are used.</summary>
        public bool UseFullImageHeuristics = true;

        /// <summary>Manual noise LUT overrides (8 floats if set).</summary>
        public List<float> ManualNoise = new List<float>();

        /// <summary>Manual XYB quantization weight factors (3 floats if set).</summary>
        public List<float> ManualXybFactors = new List<float>();

        /// <summary>Custom fixed MA decision tree for DC global section.</summary>
        public List<PropertyDecisionNode> CustomFixedTree = new List<PropertyDecisionNode>();

        /// <summary>Custom splines override.</summary>
        public SplineDataView CustomSplines;

        /// <summary>Custom progressive mode schedule override.</summary>
        public ProgressiveMode? CustomProgressiveMode;

        /// <summary>Debug image callback.</summary>
        public JxlDebugImageCallback? DebugImage;

        /// <summary>Opaque user state for <see cref="DebugImage"/>.</summary>
        public object? DebugImageOpaque;

        /// <summary>
        /// Sets the CMS interface for this compression parameter set.
        /// </summary>
        /// <param name="newCms">CMS interface instance.</param>
        public void SetCms(JxlCmsInterface newCms)
        {
            Cms    = newCms;
            CmsSet = true;
        }

        /// <summary>
        /// Returns whether these parameters specify mathematically lossless compression.
        /// </summary>
        /// <returns><c>true</c> if lossless.</returns>
        public bool IsLossless()
        {
            return ModularMode && ModularPartIsLossless();
        }

        /// <summary>
        /// Returns whether the Modular portion of the frame is mathematically lossless.
        /// </summary>
        /// <returns><c>true</c> if the Modular part is lossless.</returns>
        public bool ModularPartIsLossless()
        {
            if (ModularMode)
            {
                if (ButteraugliDistance != 0.0f || ColorTransform == ColorTransform.XYB)
                {
                    return false;
                }
            }

            foreach (float f in EcDistance)
            {
                if (f > 0.0f)
                {
                    return false;
                }

                if (f < 0.0f && ButteraugliDistance != 0.0f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Configures these parameters for mathematically lossless Modular compression.
        /// </summary>
        public void SetLossless()
        {
            ModularMode         = true;
            ButteraugliDistance = 0.0f;

            for (int i = 0; i < EcDistance.Count; i++)
            {
                EcDistance[i] = 0.0f;
            }

            ColorTransform = ColorTransform.None;
        }

        /// <summary>
        /// Creates a shallow copy of this <see cref="CompressParams"/> with independent list/array copies.
        /// </summary>
        /// <returns>Cloned <see cref="CompressParams"/>.</returns>
        public CompressParams Clone()
        {
            CompressParams copy = (CompressParams)MemberwiseClone();
            copy.EcDistance       = new List<float>(EcDistance);
            copy.ManualNoise      = new List<float>(ManualNoise);
            copy.ManualXybFactors = new List<float>(ManualXybFactors);
            copy.CustomFixedTree  = new List<PropertyDecisionNode>(CustomFixedTree);
            copy.Options          = Options.Clone();
            Array.Copy(MaxError, copy.MaxError, 3);

            return copy;
        }
    }

    /// <summary>
    /// Integer symbol to be encoded by an entropy coder (ANS or Huffman).
    /// Ported from <c>jxl::Token</c> in <c>lib/jxl/enc_ans.h</c>.
    /// </summary>
    public struct Token
    {
        private uint _packedContextAndFlag;

        /// <summary>
        /// Unpacked unsigned integer value or LZ77 length/distance symbol.
        /// </summary>
        public uint Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Token"/> struct.
        /// </summary>
        /// <param name="context">Entropy context index (31 bits).</param>
        /// <param name="value">Symbol value.</param>
        /// <param name="isLz77Length">Whether this token is an LZ77 length token.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Token(uint context, uint value, bool isLz77Length = false)
        {
            _packedContextAndFlag = (context & 0x7FFFFFFFu) | (isLz77Length ? 0x80000000u : 0u);
            Value                 = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this token represents an LZ77 length symbol.
        /// </summary>
        public bool IsLz77Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (_packedContextAndFlag & 0x80000000u) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _packedContextAndFlag = (_packedContextAndFlag & 0x7FFFFFFFu) | (value ? 0x80000000u : 0u);
        }

        /// <summary>
        /// Gets or sets the 31-bit entropy context index.
        /// </summary>
        public uint Context
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => _packedContextAndFlag & 0x7FFFFFFFu;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _packedContextAndFlag = (_packedContextAndFlag & 0x80000000u) | (value & 0x7FFFFFFFu);
        }
    }

    /// <summary>
    /// One element of the ANS or Prefix encoding table built from a symbol distribution.
    /// Ported from <c>jxl::ANSEncSymbolInfo</c> in <c>lib/jxl/enc_ans.h</c>.
    /// </summary>
    public sealed class ANSEncSymbolInfo
    {
        /// <summary>Precision for reciprocal multiplication (<c>32 + ANS_LOG_TAB_SIZE = 44</c>).</summary>
        public const int KReciprocalPrecision = 32 + (int)AnsParams.AnsLogTabSize;

        /// <summary>ANS normalized frequency.</summary>
        public ushort Freq;

        /// <summary>ANS reverse alias map.</summary>
        public ushort[] ReverseMap = Array.Empty<ushort>();

        /// <summary>Reciprocal of <see cref="Freq"/> for 64-bit multiplication.</summary>
        public ulong IFreq;

        /// <summary>Prefix code bit depth.</summary>
        public byte Depth;

        /// <summary>Prefix code bit pattern.</summary>
        public ushort Bits;
    }

    /// <summary>
    /// Stateful rANS encoder.
    /// Ported from <c>jxl::ANSCoder</c> in <c>lib/jxl/enc_ans.h</c>.
    /// </summary>
    public struct ANSCoder
    {
        private uint _state;

        /// <summary>
        /// Initializes a new instance of the <see cref="ANSCoder"/> struct with the standard ANS signature.
        /// </summary>
        /// <param name="initSignature">Unused dummy parameter to invoke default initialization.</param>
        public ANSCoder(bool initSignature = true)
        {
            _ = initSignature;
            _state = AnsParams.AnsSignature << 16;
        }

        /// <summary>
        /// Creates a new <see cref="ANSCoder"/> initialized to <c>ANS_SIGNATURE &lt;&lt; 16</c>.
        /// </summary>
        /// <returns>Initialized <see cref="ANSCoder"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ANSCoder Create()
        {
            return new ANSCoder(true);
        }

        /// <summary>
        /// Encodes symbol <paramref name="t"/> into the rANS state and returns any flushed 16-bit word.
        /// </summary>
        /// <param name="t">Symbol encoding info.</param>
        /// <param name="nbits">Outputs 16 if 16 bits were flushed, or 0 otherwise.</param>
        /// <returns>Flushed 16-bit value (if <paramref name="nbits"/> is 16).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PutSymbol(ANSEncSymbolInfo t, out byte nbits)
        {
            if (_state == 0)
            {
                _state = AnsParams.AnsSignature << 16;
            }

            uint bits = 0;
            nbits     = 0;

            if ((_state >> (32 - (int)AnsParams.AnsLogTabSize)) >= t.Freq)
            {
                bits     = _state & 0xffffu;
                _state >>= 16;
                nbits    = 16;
            }

            uint v      = (uint)((_state * t.IFreq) >> ANSEncSymbolInfo.KReciprocalPrecision);
            uint offset = t.ReverseMap[_state - (v * t.Freq)];
            _state      = (v << (int)AnsParams.AnsLogTabSize) + offset;

            return bits;
        }

        /// <summary>
        /// Gets the current 32-bit rANS state.
        /// </summary>
        /// <returns>32-bit state.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly uint GetState()
        {
            return _state == 0 ? (AnsParams.AnsSignature << 16) : _state;
        }
    }

    /// <summary>
    /// Lightweight bit counter matching <see cref="BitWriter"/>'s <c>Write</c> interface for cost estimation.
    /// Ported from <c>jxl::SizeWriter</c> in <c>lib/jxl/enc_ans.h</c>.
    /// </summary>
    public struct SizeWriter
    {
        /// <summary>Total bits accumulated.</summary>
        public nuint Size;

        /// <summary>
        /// Accumulates <paramref name="num"/> bits.
        /// </summary>
        /// <param name="num">Number of bits.</param>
        /// <param name="bits">Bit payload (ignored).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(nuint num, ulong bits)
        {
            _ = bits;
            Size += num;
        }
    }

    /// <summary>
    /// Parameters controlling ANS/Huffman histogram clustering, LZ77, and HybridUint configuration selection.
    /// Ported from <c>jxl::HistogramParams</c> in <c>lib/jxl/enc_ans_params.h</c> and <c>lib/jxl/enc_ans.cc</c>.
    /// </summary>
    public sealed class HistogramParams
    {
        /// <summary>Histogram clustering effort level.</summary>
        public enum ClusteringType
        {
            /// <summary>Only 4 clusters.</summary>
            kFastest,

            /// <summary>Fast clustering.</summary>
            kFast,

            /// <summary>Full two-phase clustering.</summary>
            kBest,
        }

        /// <summary>HybridUint configuration search method.</summary>
        public enum HybridUintMethod
        {
            /// <summary>Just use default <c>HybridUintConfig(4, 2, 0)</c>.</summary>
            kNone,

            /// <summary>Force fastest <c>HybridUintConfig(0, 0, 0)</c>.</summary>
            k000,

            /// <summary>Try a small subset of options.</summary>
            kFast,

            /// <summary>Fast choice for context map (<c>HybridUintConfig(2, 0, 1)</c>).</summary>
            kContextMap,

            /// <summary>Try all candidate configurations.</summary>
            kBest,
        }

        /// <summary>LZ77 / RLE backward-reference search method.</summary>
        public enum LZ77Method
        {
            /// <summary>Do not try LZ77.</summary>
            kNone,

            /// <summary>Only try RLE (distance 1).</summary>
            kRLE,

            /// <summary>LZ77 fast without runtime cost comparison (b=1).</summary>
            kLZ77b1w3f,

            /// <summary>LZ77 without runtime cost comparison (b=3).</summary>
            kLZ77b3w3f,

            /// <summary>LZ77 without runtime cost comparison (b=7).</summary>
            kLZ77b7w3f,

            /// <summary>LZ77 without runtime cost comparison (b=15).</summary>
            kLZ77b15w3f,

            /// <summary>LZ77 slow without runtime cost comparison (b=31).</summary>
            kLZ77b31w3f,

            /// <summary>LZ77 fast with runtime cost comparison (b=1).</summary>
            kLZ77b1w3t,

            /// <summary>LZ77 with runtime cost comparison (b=3).</summary>
            kLZ77b3w3t,

            /// <summary>LZ77 with runtime cost comparison (b=7).</summary>
            kLZ77b7w3t,

            /// <summary>LZ77 with runtime cost comparison (b=15).</summary>
            kLZ77b15w3t,

            /// <summary>LZ77 slow with runtime cost comparison (b=31).</summary>
            kLZ77b31w3t,

            /// <summary>Optimal-matching LZ77 fast (c=1).</summary>
            kOptc1,

            /// <summary>Optimal-matching LZ77 (c=3).</summary>
            kOptc3,

            /// <summary>Optimal-matching LZ77 (c=8).</summary>
            kOptc8,

            /// <summary>Optimal-matching LZ77 big chain length (c=256).</summary>
            kOptc256,
        }

        /// <summary>ANS histogram quantization precision strategy.</summary>
        public enum ANSHistogramStrategy
        {
            /// <summary>Only try 3 shifts.</summary>
            kFast,

            /// <summary>Try even shifts.</summary>
            kApproximate,

            /// <summary>Try all shifts.</summary>
            kPrecise,
        }

        /// <summary>Clustering mode.</summary>
        public ClusteringType Clustering = ClusteringType.kBest;

        /// <summary>HybridUint selection mode.</summary>
        public HybridUintMethod UintMethod = HybridUintMethod.kBest;

        /// <summary>LZ77 selection mode.</summary>
        public LZ77Method Lz77Method = LZ77Method.kRLE;

        /// <summary>ANS histogram quantization strategy.</summary>
        public ANSHistogramStrategy AnsHistogramStrategy = ANSHistogramStrategy.kPrecise;

        /// <summary>Channel widths for 2D LZ77 special distance calculation.</summary>
        public List<nuint> ImageWidths = new List<nuint>();

        /// <summary>Maximum number of clustered histograms.</summary>
        public nuint MaxHistograms = nuint.MaxValue;

        /// <summary>Force prefix (Huffman) coding instead of ANS.</summary>
        public bool ForceHuffman;

        /// <summary>Initialize global state.</summary>
        public bool InitializeGlobalState = true;

        /// <summary>Streaming mode flag.</summary>
        public bool StreamingMode;

        /// <summary>Add missing symbols to histograms.</summary>
        public bool AddMissingSymbols;

        /// <summary>Add fixed flat histograms.</summary>
        public bool AddFixedHistograms;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistogramParams"/> class with default settings.
        /// </summary>
        public HistogramParams()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HistogramParams"/> class for a given <paramref name="tier"/> and <paramref name="numCtx"/>.
        /// </summary>
        /// <param name="tier">Encoder speed tier.</param>
        /// <param name="numCtx">Number of contexts.</param>
        public HistogramParams(SpeedTier tier, nuint numCtx)
        {
            _ = numCtx;

            if (tier > SpeedTier.kFalcon)
            {
                Clustering = ClusteringType.kFastest;
                Lz77Method = LZ77Method.kNone;
            }
            else if (tier > SpeedTier.kTortoise)
            {
                Clustering = ClusteringType.kFast;
            }
            else
            {
                Clustering = ClusteringType.kBest;
            }

            if (tier > SpeedTier.kTortoise)
            {
                UintMethod = HybridUintMethod.kNone;
            }

            if (tier >= SpeedTier.kSquirrel)
            {
                AnsHistogramStrategy = ANSHistogramStrategy.kApproximate;
            }
        }

        /// <summary>
        /// Constructs <see cref="HistogramParams"/> tailored for Modular streams.
        /// Ported from <c>jxl::HistogramParams::ForModular</c> in <c>lib/jxl/enc_ans.cc</c>.
        /// </summary>
        /// <param name="cparams">Compression parameters.</param>
        /// <param name="extraDcPrecision">Per-group extra DC precision flags.</param>
        /// <param name="streamingMode">Whether streaming mode is active.</param>
        /// <returns>Configured <see cref="HistogramParams"/>.</returns>
        public static HistogramParams ForModular(
            CompressParams cparams,
            IReadOnlyList<byte> extraDcPrecision,
            bool streamingMode)
        {
            HistogramParams result = new HistogramParams
            {
                StreamingMode = streamingMode,
            };

            if (cparams.SpeedTier > SpeedTier.kKitten)
            {
                result.Clustering           = ClusteringType.kFast;
                result.AnsHistogramStrategy = cparams.SpeedTier > SpeedTier.kThunder
                    ? ANSHistogramStrategy.kFast
                    : ANSHistogramStrategy.kApproximate;
                result.Lz77Method           = cparams.ModularMode && cparams.SpeedTier <= SpeedTier.kHare
                    ? LZ77Method.kRLE
                    : LZ77Method.kNone;

                if ((extraDcPrecision.Count > 0 && extraDcPrecision[0] != 0) ||
                    (cparams.ModularMode && cparams.SpeedTier < SpeedTier.kCheetah))
                {
                    result.UintMethod = HybridUintMethod.kFast;
                }
                else
                {
                    result.UintMethod = HybridUintMethod.kNone;
                }
            }
            else if (cparams.SpeedTier <= SpeedTier.kTortoise)
            {
                result.Lz77Method = LZ77Method.kOptc256;
            }
            else
            {
                result.Lz77Method = LZ77Method.kLZ77b3w3f;
            }

            if (cparams.DecodingSpeedTier >= 2)
            {
                result.MaxHistograms = 12;
            }

            if (cparams.Options.Predictor == Predictor.Zero && cparams.ModularMode)
            {
                result.Lz77Method = cparams.SpeedTier >= SpeedTier.kFalcon
                    ? LZ77Method.kNone
                    : cparams.SpeedTier >= SpeedTier.kHare
                        ? LZ77Method.kRLE
                        : cparams.SpeedTier >= SpeedTier.kKitten
                            ? LZ77Method.kLZ77b3w3f
                            : LZ77Method.kOptc256;
            }

            return result;
        }

        /// <summary>
        /// Returns the default <see cref="HybridUintConfig"/> used during histogram clustering.
        /// </summary>
        /// <returns>Clustering <see cref="HybridUintConfig"/>.</returns>
        public HybridUintConfig UintConfig()
        {
            if (UintMethod == HybridUintMethod.kContextMap)
            {
                return new HybridUintConfig(2, 0, 1);
            }

            if (UintMethod == HybridUintMethod.k000)
            {
                return new HybridUintConfig(0, 0, 0);
            }

            return new HybridUintConfig(4, 2, 0);
        }
    }

    /// <summary>
    /// Symbol frequency histogram used during ANS/Prefix clustering and table construction.
    /// Ported from <c>jxl::Histogram</c> in <c>lib/jxl/enc_ans_params.h</c>.
    /// </summary>
    public sealed partial class Histogram
    {
        /// <summary>Alignment rounding granularity for <see cref="Counts"/> (8).</summary>
        public const int KRounding = 8;

        /// <summary>Per-symbol frequency counts.</summary>
        public List<int> Counts = new List<int>();

        /// <summary>Sum of all counts in <see cref="Counts"/>.</summary>
        public nuint TotalCount;

        /// <summary>Cached entropy estimate (not automatically kept up to date).</summary>
        public float Entropy;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="Histogram"/> class.
        /// </summary>
        public Histogram()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Histogram"/> class with preallocated capacity.
        /// </summary>
        /// <param name="length">Initial alphabet capacity.</param>
        public Histogram(nuint length)
        {
            EnsureCapacity(length);
        }

        /// <summary>
        /// Clears all symbol counts and resets <see cref="TotalCount"/> and <see cref="Entropy"/>.
        /// </summary>
        public void Clear()
        {
            Counts.Clear();
            TotalCount = 0;
            Entropy    = 0.0f;
        }

        /// <summary>
        /// Increments the frequency count for <paramref name="symbol"/>, growing <see cref="Counts"/> as needed.
        /// </summary>
        /// <param name="symbol">Symbol index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(nuint symbol)
        {
            if ((nuint)Counts.Count <= symbol)
            {
                EnsureCapacity(symbol + 1);
            }

            Counts[(int)symbol]++;
            TotalCount++;
        }

        /// <summary>
        /// Ensures <see cref="Counts"/> has at least <paramref name="length"/> elements rounded up to <see cref="KRounding"/>.
        /// </summary>
        /// <param name="length">Minimum length.</param>
        public void EnsureCapacity(nuint length)
        {
            int target = (int)(Common.DivCeil(length, (nuint)KRounding) * (nuint)KRounding);

            while (Counts.Count < target)
            {
                Counts.Add(0);
            }
        }

        /// <summary>
        /// Increments the count for <paramref name="symbol"/> without bounds checking or updating <see cref="TotalCount"/>.
        /// Call <see cref="EnsureCapacity"/> beforehand and <c>Condition()</c> afterwards.
        /// </summary>
        /// <param name="symbol">Symbol index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FastAdd(nuint symbol)
        {
            Counts[(int)symbol]++;
        }

        /// <summary>
        /// Adds all symbol counts from <paramref name="other"/> into this histogram.
        /// </summary>
        /// <param name="other">Histogram to add.</param>
        public void AddHistogram(Histogram other)
        {
            if (other.Counts.Count > Counts.Count)
            {
                EnsureCapacity((nuint)other.Counts.Count);
            }

            for (int i = 0; i < other.Counts.Count; i++)
            {
                Counts[i] += other.Counts[i];
            }

            TotalCount += other.TotalCount;
        }

        /// <summary>
        /// Gets the effective alphabet size (1 + highest symbol with a positive count, or 0 if empty).
        /// </summary>
        /// <returns>Alphabet size.</returns>
        public nuint AlphabetSize()
        {
            for (int i = Counts.Count - 1; i >= 0; i--)
            {
                if (Counts[i] > 0)
                {
                    return (nuint)(i + 1);
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the largest symbol with a non-zero count, or 0 if empty.
        /// </summary>
        /// <returns>Maximum symbol index.</returns>
        public nuint MaxSymbol()
        {
            if (TotalCount == 0)
            {
                return 0;
            }

            for (int i = Counts.Count - 1; i > 0; i--)
            {
                if (Counts[i] != 0)
                {
                    return (nuint)i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Swaps the contents of this histogram with <paramref name="other"/>.
        /// </summary>
        /// <param name="other">Other histogram.</param>
        public void Swap(Histogram other)
        {
            (Counts, other.Counts)         = (other.Counts, Counts);
            (TotalCount, other.TotalCount) = (other.TotalCount, TotalCount);
            (Entropy, other.Entropy)       = (other.Entropy, Entropy);
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="Histogram"/>.
        /// </summary>
        /// <returns>Cloned <see cref="Histogram"/>.</returns>
        public Histogram Clone()
        {
            return new Histogram
            {
                Counts     = new List<int>(Counts),
                TotalCount = TotalCount,
                Entropy    = Entropy,
            };
        }
    }

    /// <summary>
    /// Compiled entropy coding tables, HybridUint configurations, LZ77 parameters, and context map.
    /// Ported from <c>jxl::EntropyEncodingData</c> in <c>lib/jxl/enc_ans.h</c>.
    /// </summary>
    public sealed partial class EntropyEncodingData
    {
        /// <summary>Number of fixed histograms in streaming mode (1).</summary>
        public const int KNumFixedHistograms = 1;

        /// <summary>Per-cluster symbol encoding tables.</summary>
        public List<ANSEncSymbolInfo[]> EncodingInfo = new List<ANSEncSymbolInfo[]>();

        /// <summary>Whether prefix (Huffman) coding is used instead of rANS.</summary>
        public bool UsePrefixCode;

        /// <summary>Per-cluster <see cref="HybridUintConfig"/>.</summary>
        public List<HybridUintConfig> UintConfig = new List<HybridUintConfig>();

        /// <summary>Log2 of alphabet size for ANS/Prefix tables.</summary>
        public nuint LogAlphaSize;

        /// <summary>LZ77 parameters and distance multiplier.</summary>
        public LZ77Params Lz77 = new LZ77Params();

        /// <summary>Encoded histogram bitstreams (used in streaming mode).</summary>
        public List<BitWriter> EncodedHistograms = new List<BitWriter>();

        /// <summary>Clustered context map mapping context ID to histogram cluster ID.</summary>
        public byte[] ContextMap = Array.Empty<byte>();
    }

    /// <summary>
    /// Per-frame encoder state shared across VarDCT and Modular encoding passes.
    /// Ported from <c>jxl::PassesEncoderState</c> in <c>lib/jxl/enc_cache.h</c>.
    /// </summary>
    public sealed partial class PassesEncoderState : IDisposable
    {
        /// <summary>
        /// Per-pass quantized AC tokens and entropy coding data.
        /// Ported from <c>jxl::PassesEncoderState::PassData</c> in <c>lib/jxl/enc_cache.h</c>.
        /// </summary>
        public sealed class PassData
        {
            /// <summary>Per-group AC token streams.</summary>
            public List<List<Token>> AcTokens = new List<List<Token>>();

            /// <summary>Encoded context map for this pass.</summary>
            public byte[] ContextMap = Array.Empty<byte>();

            /// <summary>Entropy encoding tables for this pass.</summary>
            public EntropyEncodingData Codes = new EntropyEncodingData();
        }

        /// <summary>State shared with decoder structures.</summary>
        public readonly PassesSharedState Shared;

        /// <summary>Whether group-by-group streaming mode is enabled.</summary>
        public bool StreamingMode;

        /// <summary>Whether global state should be initialized.</summary>
        public bool InitializeGlobalState = true;

        /// <summary>Active DC group index in streaming mode.</summary>
        public nuint DcGroupIndex;

        /// <summary>Per-pass quantized DCT coefficient images.</summary>
        public readonly List<ACImage> Coeffs = new List<ACImage>();

        /// <summary>Serialized bitstreams for special (reference/DC) frames.</summary>
        public readonly List<BitWriter> SpecialFrames = new List<BitWriter>();

        /// <summary>Progressive coefficient splitter.</summary>
        public readonly ProgressiveSplitter ProgressiveSplitter = new ProgressiveSplitter();

        /// <summary>Active compression parameters for the frame.</summary>
        public CompressParams CParams = new CompressParams();

        /// <summary>Per-pass token and entropy data.</summary>
        public readonly List<PassData> Passes = new List<PassData>();

        /// <summary>Per-group histogram index.</summary>
        public readonly List<nuint> HistogramIdx = new List<nuint>();

        /// <summary>Bitmask of AC transform strategies used in the frame.</summary>
        public uint UsedAcs;

        /// <summary>Per-pass bitmask of non-default coefficient orders.</summary>
        public readonly List<uint> UsedOrders = new List<uint>();

        /// <summary>Multiplier applied to X channel quantization matrices.</summary>
        public float XQmMultiplier = 1.0f;

        /// <summary>Multiplier applied to B channel quantization matrices.</summary>
        public float BQmMultiplier = 1.0f;

        /// <summary>Initial 1x1 quantization masking field.</summary>
        public ImagePlane<float> InitialQuantMasking1x1 = new ImagePlane<float>();

        /// <summary>
        /// Initializes a new instance of the <see cref="PassesEncoderState"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public PassesEncoderState(JxlMemoryManager? memoryManager = null)
        {
            Shared = new PassesSharedState(memoryManager);
        }

        /// <summary>Gets the memory manager associated with this encoder state.</summary>
        public JxlMemoryManager? MemoryManager => Shared.MemoryManager;

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (ACImage coeff in Coeffs)
            {
                coeff.Dispose();
            }

            Coeffs.Clear();
            InitialQuantMasking1x1.Dispose();
            Shared.Dispose();
        }
    }

    /// <summary>
    /// Encoder-side inverse Gaborish 5x5 sharpening filter.
    /// Ported from <c>lib/jxl/enc_gaborish.h</c> and <c>lib/jxl/enc_gaborish.cc</c>.
    /// </summary>
    public static class EncGaborish
    {
        private static readonly float[] KGaborish =
        {
            -0.09495815671340026f,
            -0.041031725066768575f,
            0.013710004822696948f,
            0.006510206083837737f,
            -0.0014789063378272242f,
        };

        /// <summary>
        /// Applies the 5x5 inverse Gaborish sharpening convolution in-place to reduce the impact of decoder smoothing.
        /// Ported from <c>jxl::GaborishInverse</c> in <c>lib/jxl/enc_gaborish.cc</c>.
        /// </summary>
        /// <param name="inOut">3-plane XYB image to sharpen in-place.</param>
        /// <param name="rect">Region to process.</param>
        /// <param name="mul">Per-channel Gaborish strength multipliers (length 3).</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status GaborishInverse(
            Image3<float> inOut,
            Rect rect,
            ReadOnlySpan<float> mul,
            JxlSharp.Base.ThreadPool? pool)
        {
            JxlMemoryManager? memoryManager = inOut.MemoryManager;
            WeightsSymmetric5[] weights     = new WeightsSymmetric5[3];

            for (int i = 0; i < 3; i++)
            {
                double sum = 1.0 + (mul[i] * 4.0 *
                    (KGaborish[0] + KGaborish[1] + KGaborish[2] + KGaborish[4] + (2.0 * KGaborish[3])));

                if (sum < 1e-5)
                {
                    sum = 1e-5;
                }

                float normalize    = (float)(1.0 / sum);
                float normalizeMul = mul[i] * normalize;
                weights[i]         = new WeightsSymmetric5(
                    normalize,
                    normalizeMul * KGaborish[0],
                    normalizeMul * KGaborish[2],
                    normalizeMul * KGaborish[1],
                    normalizeMul * KGaborish[4],
                    normalizeMul * KGaborish[3]);
            }

            StatusOr<ImagePlane<float>> tempOr = ImagePlane<float>.Create(
                memoryManager,
                inOut.Plane(2).xsize,
                inOut.Plane(2).ysize);

            if (!tempOr.IsOk)
            {
                return tempOr.Status;
            }

            using ImagePlane<float> temp = tempOr.Value;

            if (!ImageOps.CopyImageTo(inOut.Plane(2), temp))
            {
                return false;
            }

            Rect xrect = rect.Extend(3, new Rect(inOut));

            if (!Convolve.Symmetric5(inOut.Plane(0), xrect, weights[0], pool, inOut.Plane(2), xrect))
            {
                return false;
            }

            if (!Convolve.Symmetric5(inOut.Plane(1), xrect, weights[1], pool, inOut.Plane(0), xrect))
            {
                return false;
            }

            if (!Convolve.Symmetric5(temp, xrect, weights[2], pool, inOut.Plane(1), xrect))
            {
                return false;
            }

            // Now planes are 1, 2, 0 -> swap(0, 1) -> 2, 1, 0 -> swap(0, 2) -> 0, 1, 2.
            inOut.Plane(0).Swap(inOut.Plane(1));
            inOut.Plane(0).Swap(inOut.Plane(2));

            return true;
        }
    }

    /// <summary>
    /// Encoder methods for serializing and roundtripping <see cref="DequantMatrices"/>.
    /// Ported from <c>lib/jxl/enc_quant_weights.h</c> and <c>lib/jxl/enc_quant_weights.cc</c>.
    /// </summary>
    public static partial class EncQuantWeights
    {
        /// <summary>
        /// Delegate for encoding a RAW quantization table via <c>ModularFrameEncoder.EncodeQuantTable</c>.
        /// </summary>
        public delegate Status RawQuantTableEncodeFunc(
            JxlMemoryManager? memoryManager,
            nuint sizeX,
            nuint sizeY,
            BitWriter writer,
            QuantEncoding encoding,
            nuint idx,
            object? modularFrameEncoder);

        /// <summary>
        /// Hook populated by <c>ModularFrameEncoder</c> to encode RAW quantization tables.
        /// </summary>
        public static RawQuantTableEncodeFunc? RawQuantTableEncoder;

        /// <summary>
        /// Encodes the DC quantization factors of <paramref name="matrices"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::DequantMatricesEncodeDC</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="matrices">Source dequantization matrices.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesEncodeDC(
            DequantMatrices matrices,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut)
        {
            bool allDefault                = true;
            ReadOnlySpan<float> dcQuant    = matrices.DCQuants();
            ReadOnlySpan<float> dcDefaults = DequantMatrices.DcQuantDefault;

            for (int c = 0; c < 3; c++)
            {
                if (dcQuant[c] != dcDefaults[c])
                {
                    allDefault = false;
                }
            }

            return writer.WithMaxBits(
                1 + (sizeof(float) * 8 * 3),
                layer,
                auxOut,
                () =>
                {
                    writer.Write(1, allDefault ? 1UL : 0UL);

                    if (!allDefault)
                    {
                        ReadOnlySpan<float> dc = matrices.DCQuants();

                        for (int c = 0; c < 3; c++)
                        {
                            if (!F16Coder.Write(dc[c] * 128.0f, writer))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                });
        }

        /// <summary>
        /// Sets custom inverse DC quantization values on <paramref name="matrices"/> and roundtrips them through F16 encoding.
        /// Ported from <c>jxl::DequantMatricesSetCustomDC</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager.</param>
        /// <param name="matrices">Target dequantization matrices.</param>
        /// <param name="dc">Span of 3 inverse DC quantization factors.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesSetCustomDC(
            JxlMemoryManager? memoryManager,
            DequantMatrices matrices,
            ReadOnlySpan<float> dc)
        {
            matrices.SetDCQuant(dc);
            BitWriter writer = new BitWriter(memoryManager);

            if (!DequantMatricesEncodeDC(matrices, writer, LayerType.Header, null))
            {
                return false;
            }

            writer.ZeroPadToByte();
            BitReader br = new BitReader(writer.GetSpan());

            if (!matrices.DecodeDC(br))
            {
                return false;
            }

            return br.Close();
        }

        /// <summary>
        /// Scales the DC quantization factors of <paramref name="matrices"/> by <paramref name="scale"/> and roundtrips them.
        /// Ported from <c>jxl::DequantMatricesScaleDC</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager.</param>
        /// <param name="matrices">Target dequantization matrices.</param>
        /// <param name="scale">Scale factor.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesScaleDC(
            JxlMemoryManager? memoryManager,
            DequantMatrices matrices,
            float scale)
        {
            Span<float> dc = stackalloc float[3];

            for (nuint c = 0; c < 3; c++)
            {
                dc[(int)c] = matrices.InvDCQuant(c) * (1.0f / scale);
            }

            return DequantMatricesSetCustomDC(memoryManager, matrices, dc);
        }

        /// <summary>
        /// Encodes all AC quantization weight tables of <paramref name="matrices"/> into <paramref name="writer"/>.
        /// Ported from <c>jxl::DequantMatricesEncode</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager.</param>
        /// <param name="matrices">Source dequantization matrices.</param>
        /// <param name="writer">Destination bit writer.</param>
        /// <param name="layer">Statistics layer.</param>
        /// <param name="auxOut">Optional auxiliary statistics output.</param>
        /// <param name="modularFrameEncoder">Optional <c>ModularFrameEncoder</c> instance for RAW tables.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesEncode(
            JxlMemoryManager? memoryManager,
            DequantMatrices matrices,
            BitWriter writer,
            LayerType layer,
            AuxOut? auxOut,
            object? modularFrameEncoder = null)
        {
            bool allDefault                         = true;
            IReadOnlyList<QuantEncoding> encodings  = matrices.Encodings;

            for (int i = 0; i < encodings.Count; i++)
            {
                if (encodings[i].Mode != QuantEncoding.QuantMode.kQuantModeLibrary ||
                    encodings[i].Predefined != 0)
                {
                    allDefault = false;
                }
            }

            return writer.WithMaxBits(
                512 * 1024,
                layer,
                auxOut,
                () =>
                {
                    writer.Write(1, allDefault ? 1UL : 0UL);

                    if (!allDefault)
                    {
                        ReadOnlySpan<int> rx = DequantMatrices.RequiredSizeX;
                        ReadOnlySpan<int> ry = DequantMatrices.RequiredSizeY;

                        for (int i = 0; i < encodings.Count; i++)
                        {
                            if (!EncodeQuant(
                                    memoryManager,
                                    encodings[i],
                                    (nuint)i,
                                    (nuint)rx[i],
                                    (nuint)ry[i],
                                    writer,
                                    modularFrameEncoder))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                });
        }

        /// <summary>
        /// Roundtrip encodes and decodes <paramref name="matrices"/> to ensure identical values to the decoder.
        /// Ported from <c>jxl::DequantMatricesRoundtrip</c> in <c>lib/jxl/enc_quant_weights.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager.</param>
        /// <param name="matrices">Target dequantization matrices.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DequantMatricesRoundtrip(
            JxlMemoryManager? memoryManager,
            DequantMatrices matrices)
        {
            BitWriter writer = new BitWriter(memoryManager);

            if (!DequantMatricesEncode(memoryManager, matrices, writer, LayerType.Header, null, null))
            {
                return false;
            }

            writer.ZeroPadToByte();
            BitReader br = new BitReader(writer.GetSpan());

            if (!matrices.Decode(memoryManager, br))
            {
                return false;
            }

            return br.Close();
        }

        private static Status EncodeDctParams(DctQuantWeightParams dctParams, BitWriter writer)
        {
            if (dctParams.NumDistanceBands < 1)
            {
                return Status.Failure("num_distance_bands < 1");
            }

            writer.Write(DctQuantWeightParams.kLog2MaxDistanceBands, dctParams.NumDistanceBands - 1);

            for (int c = 0; c < 3; c++)
            {
                for (nuint i = 0; i < dctParams.NumDistanceBands; i++)
                {
                    float scale = i == 0 ? (1.0f / 64.0f) : 1.0f;

                    if (!F16Coder.Write(dctParams.DistanceBands[c][(int)i] * scale, writer))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static Status WriteWeightMatrix(float[][] weights, int count, float scale, BitWriter writer)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!F16Coder.Write(weights[c][i] * scale, writer))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static Status EncodeQuant(
            JxlMemoryManager? memoryManager,
            QuantEncoding encoding,
            nuint idx,
            nuint sizeX,
            nuint sizeY,
            BitWriter writer,
            object? modularFrameEncoder)
        {
            writer.Write(DequantMatrices.kLog2NumQuantModes, (ulong)encoding.Mode);
            sizeX *= FrameDimensions.kBlockDim;
            sizeY *= FrameDimensions.kBlockDim;

            switch (encoding.Mode)
            {
                case QuantEncoding.QuantMode.kQuantModeLibrary:
                    writer.Write(DequantMatrices.kCeilLog2NumPredefinedTables, encoding.Predefined);
                    return true;

                case QuantEncoding.QuantMode.kQuantModeID:
                    return WriteWeightMatrix(encoding.IdWeights, 3, 1.0f / 64.0f, writer);

                case QuantEncoding.QuantMode.kQuantModeDCT2:
                    return WriteWeightMatrix(encoding.Dct2Weights, 6, 1.0f / 64.0f, writer);

                case QuantEncoding.QuantMode.kQuantModeDCT4X8:
                    for (int c = 0; c < 3; c++)
                    {
                        if (!F16Coder.Write(encoding.Dct4x8Multipliers[c], writer))
                        {
                            return false;
                        }
                    }

                    return EncodeDctParams(encoding.DctParams, writer);

                case QuantEncoding.QuantMode.kQuantModeDCT4:
                    if (!WriteWeightMatrix(encoding.Dct4Multipliers, 2, 1.0f, writer))
                    {
                        return false;
                    }

                    return EncodeDctParams(encoding.DctParams, writer);

                case QuantEncoding.QuantMode.kQuantModeDCT:
                    return EncodeDctParams(encoding.DctParams, writer);

                case QuantEncoding.QuantMode.kQuantModeRAW:
                    if (RawQuantTableEncoder == null)
                    {
                        return Status.Failure("RawQuantTableEncoder not registered");
                    }

                    return RawQuantTableEncoder(memoryManager, sizeX, sizeY, writer, encoding, idx, modularFrameEncoder);

                case QuantEncoding.QuantMode.kQuantModeAFV:
                    for (int c = 0; c < 3; c++)
                    {
                        for (int i = 0; i < 9; i++)
                        {
                            float scale = i < 6 ? (1.0f / 64.0f) : 1.0f;

                            if (!F16Coder.Write(encoding.AfvWeights[c][i] * scale, writer))
                            {
                                return false;
                            }
                        }
                    }

                    return EncodeDctParams(encoding.DctParams, writer) &&
                           EncodeDctParams(encoding.DctParamsAfv4x4, writer);

                default:
                    return Status.Failure("Unknown QuantMode");
            }
        }
    }

    /// <summary>
    /// Encoder methods for <see cref="PatchDictionary"/>.
    /// Ported from <c>jxl::PatchDictionaryEncoder</c> in <c>lib/jxl/enc_patch_dictionary.h</c> and <c>lib/jxl/enc_patch_dictionary.cc</c>.
    /// </summary>
    public static partial class PatchDictionaryEncoder
    {
        /// <summary>
        /// Delegate for finding the best patch dictionary for a frame.
        /// </summary>
        public delegate Status FindBestPatchDictionaryFunc(
            Image3<float> opsin,
            PassesEncoderState state,
            JxlCmsInterface cms,
            JxlSharp.Base.ThreadPool? pool,
            AuxOut? auxOut,
            bool isXyb);

        /// <summary>
        /// Hook populated by <c>EncPatchDictionary</c> to run full patch dictionary search.
        /// </summary>
        public static FindBestPatchDictionaryFunc? FindBestPatchDictionaryHook;

        /// <summary>
        /// Populates <paramref name="pdic"/> with the given positions, reference positions, and blendings.
        /// </summary>
        /// <param name="pdic">Target patch dictionary.</param>
        /// <param name="positions">Patch occurrence positions.</param>
        /// <param name="refPositions">Patch reference crop positions.</param>
        /// <param name="blendings">Patch blending modes.</param>
        /// <param name="blendingsStride">Stride per patch in <paramref name="blendings"/>.</param>
        public static void SetPositions(
            PatchDictionary pdic,
            IEnumerable<PatchPosition> positions,
            IEnumerable<PatchReferencePosition> refPositions,
            IEnumerable<PatchBlending> blendings,
            nuint blendingsStride)
        {
            pdic.SetPositions(positions, refPositions, blendings, blendingsStride);
        }

        /// <summary>
        /// Subtracts reference patches from <paramref name="opsin"/> in-place before encoding.
        /// Ported from <c>jxl::PatchDictionaryEncoder::SubtractFrom</c> in <c>lib/jxl/enc_patch_dictionary.cc</c>.
        /// </summary>
        /// <param name="pdic">Active patch dictionary.</param>
        /// <param name="opsin">3-plane image to subtract patches from.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status SubtractFrom(PatchDictionary pdic, Image3<float> opsin)
        {
            ReferenceFrame[]? refFrames = pdic.ReferenceFrames;

            if (refFrames == null || !pdic.HasAny())
            {
                return true;
            }

            nuint blendingStride                       = pdic.BlendingsStride;
            IReadOnlyList<PatchPosition> positions     = pdic.Positions;
            IReadOnlyList<PatchReferencePosition> refs = pdic.RefPositions;
            IReadOnlyList<PatchBlending> blendings     = pdic.Blendings;

            for (nuint y = 0; y < opsin.YSize; y++)
            {
                List<nuint> rowPatches = pdic.GetPatchesForRow(y);

                if (rowPatches.Count == 0)
                {
                    continue;
                }

                foreach (nuint posIdx in rowPatches)
                {
                    nuint blendingIdx         = posIdx * blendingStride;
                    PatchPosition pos         = positions[(int)posIdx];
                    PatchReferencePosition rp = refs[(int)pos.RefPosIdx];
                    PatchBlendMode mode       = blendings[(int)blendingIdx].Mode;

                    if (y < pos.Y || y >= pos.Y + rp.YSize)
                    {
                        return Status.Failure("Patch row out of range");
                    }

                    nuint iy          = y - pos.Y;
                    ImageBundle refIb = refFrames[(int)rp.Ref].Frame!;

                    ApplyPatchRowSubtraction(opsin, y, refIb.Color(), rp, iy, pos.X, mode);
                }
            }

            return true;
        }

        /// <summary>
        /// Searches for repeating patches in <paramref name="opsin"/> and populates <c>state.Shared.ImageFeatures.Patches</c>.
        /// </summary>
        /// <param name="opsin">Input image.</param>
        /// <param name="state">Encoder state.</param>
        /// <param name="cms">CMS interface.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="auxOut">Optional auxiliary statistics.</param>
        /// <param name="isXyb">Whether <paramref name="opsin"/> is in XYB color space.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status FindBestPatchDictionary(
            Image3<float> opsin,
            PassesEncoderState state,
            JxlCmsInterface cms,
            JxlSharp.Base.ThreadPool? pool,
            AuxOut? auxOut,
            bool isXyb = true)
        {
            if (FindBestPatchDictionaryHook != null)
            {
                return FindBestPatchDictionaryHook(opsin, state, cms, pool, auxOut, isXyb);
            }

            return true;
        }

        private static void ApplyPatchRowSubtraction(
            Image3<float> opsin,
            nuint y,
            Image3<float> refColor,
            PatchReferencePosition rp,
            nuint iy,
            nuint bx,
            PatchBlendMode mode)
        {
            for (int c = 0; c < 3; c++)
            {
                Span<float> row            = opsin.PlaneRow((nuint)c, y);
                ReadOnlySpan<float> refRow = refColor.ConstPlaneRow((nuint)c, rp.Y0 + iy).Slice((int)rp.X0);

                for (nuint ix = 0; ix < rp.XSize; ix++)
                {
                    if (mode == PatchBlendMode.kAdd)
                    {
                        row[(int)(bx + ix)] -= refRow[(int)ix];
                    }
                    else if (mode == PatchBlendMode.kReplace)
                    {
                        row[(int)(bx + ix)] = 0.0f;
                    }
                }
            }
        }
    }
}
