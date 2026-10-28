// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Jyrki Alakuijala (jyrki.alakuijala@gmail.com)
//
// The physical architecture of butteraugli is based on the following naming
// convention:
//   * Opsin - dynamics of the photosensitive chemicals in the retina
//             with their immediate electrical processing
//   * Xyb - hybrid opponent/trichromatic color space
//     x is roughly red-subtract-green.
//     y is yellow.
//     b is blue.
//     Xyb values are computed from Opsin mixing, not directly from rgb.
//   * Mask - for visual masking
//   * Hf - color modeling for spatially high-frequency features
//   * Lf - color modeling for spatially low-frequency features
//   * Diffmap - to cluster and build an image of error between the images
//   * Blur - to hold the smoothing code

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using JxlSharp.Base;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Parameters controlling the Butteraugli psychovisual similarity metric.
    /// Ported from <c>jxl::ButteraugliParams</c> in <c>lib/jxl/butteraugli/butteraugli.h</c>.
    /// </summary>
    public struct ButteraugliParams
    {
        /// <summary>
        /// Multiplier for penalizing new HF artifacts more than blurring away features. 1.0=neutral.
        /// </summary>
        public float HfAsymmetry;

        /// <summary>
        /// Multiplier for the psychovisual difference in the X channel.
        /// </summary>
        public float Xmul;

        /// <summary>
        /// Number of nits that correspond to 1.0f input values.
        /// </summary>
        public float IntensityTarget;

        /// <summary>
        /// Initializes a new instance of the <see cref="ButteraugliParams"/> struct with default values.
        /// </summary>
        public ButteraugliParams()
        {
            HfAsymmetry     = 1.0f;
            Xmul            = 1.0f;
            IntensityTarget = 80.0f;
        }

        /// <summary>
        /// Gets the default <see cref="ButteraugliParams"/> configuration.
        /// </summary>
        public static ButteraugliParams Default => new ButteraugliParams
        {
            HfAsymmetry     = 1.0f,
            Xmul            = 1.0f,
            IntensityTarget = 80.0f,
        };
    }

    /// <summary>
    /// Multi-frequency psychovisual image decomposition (UHF, HF, MF, LF).
    /// Ported from <c>jxl::PsychoImage</c> in <c>lib/jxl/butteraugli/butteraugli.h</c>.
    /// </summary>
    public sealed class PsychoImage : IDisposable
    {
        /// <summary>Ultra-high frequency XY planes (2 planes).</summary>
        public readonly ImagePlane<float>[] Uhf = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };

        /// <summary>High frequency XY planes (2 planes).</summary>
        public readonly ImagePlane<float>[] Hf  = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };

        /// <summary>Medium frequency XYB 3-plane image.</summary>
        public Image3<float> Mf = new Image3<float>();

        /// <summary>Low frequency XYB 3-plane image.</summary>
        public Image3<float> Lf = new Image3<float>();

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < 2; ++i)
            {
                Uhf[i].Dispose();
                Uhf[i] = new ImagePlane<float>();
                Hf[i].Dispose();
                Hf[i] = new ImagePlane<float>();
            }

            Mf.Dispose();
            Mf = new Image3<float>();
            Lf.Dispose();
            Lf = new Image3<float>();
        }
    }

    /// <summary>
    /// Holds a temporary transposed image allocated on demand for 2D separable blur.
    /// Ported from <c>jxl::BlurTemp</c> in <c>lib/jxl/butteraugli/butteraugli.h</c>.
    /// </summary>
    public sealed class BlurTemp : IDisposable
    {
        /// <summary>Transposed temporary buffer.</summary>
        public ImagePlane<float> TransposedTemp = new ImagePlane<float>();

        /// <summary>
        /// Gets or allocates a transposed temporary buffer matching the transposed dimensions of <paramref name="inPlane"/>.
        /// </summary>
        /// <param name="inPlane">Input image plane.</param>
        /// <param name="outPlane">Transposed buffer plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status GetTransposed(ImagePlane<float> inPlane, out ImagePlane<float> outPlane)
        {
            JxlMemoryManager? memoryManager = inPlane.MemoryManager;

            if (TransposedTemp.XSize != inPlane.YSize || TransposedTemp.YSize != inPlane.XSize)
            {
                TransposedTemp.Dispose();
                StatusOr<ImagePlane<float>> created = ImagePlane<float>.Create(
                    memoryManager,
                    inPlane.YSize,
                    inPlane.XSize);

                if (!created.IsOk)
                {
                    outPlane = new ImagePlane<float>();

                    return created.Status;
                }

                TransposedTemp = created.Value;
            }

            outPlane = TransposedTemp;

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            TransposedTemp.Dispose();
            TransposedTemp = new ImagePlane<float>();
        }
    }

    /// <summary>
    /// Stateful Butteraugli comparator that precomputes the reference image decomposition.
    /// Ported from <c>jxl::ButteraugliComparator</c> in <c>lib/jxl/butteraugli/butteraugli.h</c>.
    /// </summary>
    public sealed class ButteraugliComparator : IDisposable
    {
        private readonly nuint _xsize;
        private readonly nuint _ysize;
        private readonly ButteraugliParams _params;
        private readonly PsychoImage _pi0 = new PsychoImage();
        private readonly BlurTemp _blurTemp = new BlurTemp();
        private Image3<float> _temp = new Image3<float>();
        private int _tempInUse;
        private ButteraugliComparator? _sub;

        private ButteraugliComparator(nuint xsize, nuint ysize, in ButteraugliParams baParams)
        {
            _xsize  = xsize;
            _ysize  = ysize;
            _params = baParams;
        }

        /// <summary>
        /// Creates and initializes a <see cref="ButteraugliComparator"/> for reference image <paramref name="rgb0"/>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <returns>Initialized comparator on success.</returns>
        public static StatusOr<ButteraugliComparator> Make(Image3<float> rgb0, in ButteraugliParams baParams)
        {
            nuint xsize                     = rgb0.XSize;
            nuint ysize                     = rgb0.YSize;
            JxlMemoryManager? memoryManager = rgb0.MemoryManager;
            ButteraugliComparator result    = new ButteraugliComparator(xsize, ysize, in baParams);

            StatusOr<Image3<float>> tempOr = Image3<float>.Create(memoryManager, xsize, ysize);

            if (!tempOr.IsOk)
            {
                result.Dispose();

                return tempOr.Status;
            }

            result._temp = tempOr.Value;

            if (xsize < 8 || ysize < 8)
            {
                return result;
            }

            Status initStatus = InitializeReference(result, rgb0, in baParams);

            if (!initStatus)
            {
                result.Dispose();

                return initStatus;
            }

            return result;
        }

        /// <summary>
        /// Computes the visual masking map for the reference image.
        /// </summary>
        /// <param name="mask">Output masking image plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Mask(out ImagePlane<float> mask)
        {
            return Butteraugli.MaskPsychoImage(
                _pi0,
                _pi0,
                _xsize,
                _ysize,
                in _params,
                _blurTemp,
                out mask);
        }

        /// <summary>
        /// Computes the Butteraugli difference map between the reference image and <paramref name="rgb1"/>.
        /// </summary>
        /// <param name="rgb1">Distorted linear RGB image.</param>
        /// <param name="result">Output difference map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Diffmap(Image3<float> rgb1, ref ImagePlane<float> result)
        {
            JxlMemoryManager? memoryManager = rgb1.MemoryManager;

            if (_xsize < 8 || _ysize < 8)
            {
                return EnsureAndZeroFill(memoryManager, _xsize, _ysize, ref result);
            }

            StatusOr<Image3<float>> xyb1Or = Image3<float>.Create(memoryManager, _xsize, _ysize);

            if (!xyb1Or.IsOk)
            {
                return xyb1Or.Status;
            }

            using Image3<float> xyb1 = xyb1Or.Value;
            Image3<float>? temp      = AcquireTemp(memoryManager, _xsize, _ysize, out bool ownedTemp);

            try
            {
                if (temp == null || !Butteraugli.OpsinDynamicsImage(rgb1, in _params, temp, _blurTemp, xyb1))
                {
                    return false;
                }
            }
            finally
            {
                ReleaseAcquiredTemp(temp, ownedTemp);
            }

            if (!DiffmapOpsinDynamicsImage(xyb1, out result))
            {
                return false;
            }

            return AccumulateSubsampledDiffmap(rgb1, ref result);
        }

        /// <summary>
        /// Computes the Butteraugli difference map from an already OpsinDynamics-transformed XYB image.
        /// </summary>
        /// <param name="xyb1">Opsin-dynamics XYB image.</param>
        /// <param name="result">Output difference map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DiffmapOpsinDynamicsImage(Image3<float> xyb1, out ImagePlane<float> result)
        {
            JxlMemoryManager? memoryManager = xyb1.MemoryManager;
            result                          = new ImagePlane<float>();

            if (_xsize < 8 || _ysize < 8)
            {
                return EnsureAndZeroFill(memoryManager, _xsize, _ysize, ref result);
            }

            using PsychoImage pi1 = new PsychoImage();

            if (!Butteraugli.SeparateFrequencies(_xsize, _ysize, in _params, _blurTemp, xyb1, pi1))
            {
                return false;
            }

            StatusOr<ImagePlane<float>> resOr = ImagePlane<float>.Create(memoryManager, _xsize, _ysize);

            if (!resOr.IsOk)
            {
                return resOr.Status;
            }

            result = resOr.Value;

            return DiffmapPsychoImage(pi1, result);
        }

        /// <summary>
        /// Computes the Butteraugli difference map from a pre-decomposed <see cref="PsychoImage"/>.
        /// </summary>
        /// <param name="pi1">Distorted psychovisual image decomposition.</param>
        /// <param name="diffmap">Destination difference map (must be allocated to <c>(_xsize, _ysize)</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DiffmapPsychoImage(PsychoImage pi1, ImagePlane<float> diffmap)
        {
            JxlMemoryManager? memoryManager = diffmap.MemoryManager;

            if (_xsize < 8 || _ysize < 8)
            {
                ImageOps.ZeroFillImage(diffmap);

                return true;
            }

            StatusOr<ImagePlane<float>> diffsOr  = ImagePlane<float>.Create(memoryManager, _xsize, _ysize);
            StatusOr<Image3<float>> blockAcOr    = Image3<float>.Create(memoryManager, _xsize, _ysize);
            StatusOr<Image3<float>> blockDcOr    = Image3<float>.Create(memoryManager, _xsize, _ysize);

            if (!diffsOr.IsOk || !blockAcOr.IsOk || !blockDcOr.IsOk)
            {
                Butteraugli.DisposeIfOk(diffsOr);
                Butteraugli.DisposeIfOk(blockAcOr);
                Butteraugli.DisposeIfOk(blockDcOr);

                return Status.Failure("Failed to allocate DiffmapPsychoImage buffers");
            }

            using ImagePlane<float> diffs = diffsOr.Value;
            using Image3<float> blockAc   = blockAcOr.Value;
            using Image3<float> blockDc   = blockDcOr.Value;
            ImageOps.ZeroFillImage(blockAc);

            if (!Butteraugli.AccumulatePsychoAcAndDc(_pi0, pi1, _params.HfAsymmetry, diffs, blockAc, blockDc))
            {
                return false;
            }

            if (!Butteraugli.MaskPsychoImageWithAc(_pi0, pi1, in _params, _blurTemp, out ImagePlane<float> mask, blockAc.Plane(1)))
            {
                return false;
            }

            using (mask)
            {
                return Butteraugli.CombineChannelsToDiffmap(mask, blockDc, blockAc, _params.Xmul, diffmap);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _pi0.Dispose();
            _blurTemp.Dispose();
            _temp.Dispose();
            _temp = new Image3<float>();
            _sub?.Dispose();
            _sub = null;
        }

        private static Status InitializeReference(
            ButteraugliComparator comp,
            Image3<float> rgb0,
            in ButteraugliParams baParams)
        {
            StatusOr<Image3<float>> xyb0Or = Image3<float>.Create(rgb0.MemoryManager, comp._xsize, comp._ysize);

            if (!xyb0Or.IsOk)
            {
                return xyb0Or.Status;
            }

            using Image3<float> xyb0 = xyb0Or.Value;

            if (!Butteraugli.OpsinDynamicsImage(rgb0, in baParams, comp._temp, comp._blurTemp, xyb0))
            {
                return false;
            }

            if (!Butteraugli.SeparateFrequencies(comp._xsize, comp._ysize, in baParams, comp._blurTemp, xyb0, comp._pi0))
            {
                return false;
            }

            StatusOr<Image3<float>> subRgb0Or = Butteraugli.SubSample2x(rgb0);

            if (!subRgb0Or.IsOk)
            {
                return subRgb0Or.Status;
            }

            using Image3<float> subRgb0          = subRgb0Or.Value;
            StatusOr<ButteraugliComparator> subOr = Make(subRgb0, in baParams);

            if (!subOr.IsOk)
            {
                return subOr.Status;
            }

            comp._sub = subOr.Value;

            return true;
        }

        private static Status EnsureAndZeroFill(
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize,
            ref ImagePlane<float> result)
        {
            if (result.XSize != xsize || result.YSize != ysize)
            {
                result.Dispose();
                StatusOr<ImagePlane<float>> created = ImagePlane<float>.Create(memoryManager, xsize, ysize);

                if (!created.IsOk)
                {
                    return created.Status;
                }

                result = created.Value;
            }

            ImageOps.ZeroFillImage(result);

            return true;
        }

        private Image3<float>? AcquireTemp(JxlMemoryManager? memoryManager, nuint xsize, nuint ysize, out bool owned)
        {
            if (Interlocked.Exchange(ref _tempInUse, 1) == 0)
            {
                owned = false;

                return _temp;
            }

            StatusOr<Image3<float>> created = Image3<float>.Create(memoryManager, xsize, ysize);
            owned                           = created.IsOk;

            return created.IsOk ? created.Value : null;
        }

        private void ReleaseAcquiredTemp(Image3<float>? temp, bool owned)
        {
            if (owned)
            {
                temp?.Dispose();
            }
            else if (temp != null)
            {
                Volatile.Write(ref _tempInUse, 0);
            }
        }

        private Status AccumulateSubsampledDiffmap(Image3<float> rgb1, ref ImagePlane<float> result)
        {
            if (_sub == null || _sub._xsize < 8 || _sub._ysize < 8)
            {
                return true;
            }

            JxlMemoryManager? memoryManager  = rgb1.MemoryManager;
            StatusOr<Image3<float>> subXybOr = Image3<float>.Create(memoryManager, _sub._xsize, _sub._ysize);
            StatusOr<Image3<float>> subRgbOr = Butteraugli.SubSample2x(rgb1);

            if (!subXybOr.IsOk || !subRgbOr.IsOk)
            {
                Butteraugli.DisposeIfOk(subXybOr);
                Butteraugli.DisposeIfOk(subRgbOr);

                return false;
            }

            using Image3<float> subXyb = subXybOr.Value;
            using Image3<float> subRgb = subRgbOr.Value;
            Image3<float>? subTemp     = _sub.AcquireTemp(memoryManager, _sub._xsize, _sub._ysize, out bool owned);

            try
            {
                if (subTemp == null || !Butteraugli.OpsinDynamicsImage(subRgb, in _sub._params, subTemp, _sub._blurTemp, subXyb))
                {
                    return false;
                }
            }
            finally
            {
                _sub.ReleaseAcquiredTemp(subTemp, owned);
            }

            if (!_sub.DiffmapOpsinDynamicsImage(subXyb, out ImagePlane<float> subResult))
            {
                return false;
            }

            using (subResult)
            {
                Butteraugli.AddSupersampled2x(subResult, 0.5f, result);
            }

            return true;
        }
    }

    /// <summary>
    /// Butteraugli psychovisual image similarity metric functions and kernels.
    /// Ported from <c>lib/jxl/butteraugli/butteraugli.h</c> and <c>lib/jxl/butteraugli/butteraugli.cc</c>.
    /// </summary>
    public static class Butteraugli
    {
        private const double WMfMalta   = 37.0819870399;
        private const double Norm1Mf    = 130262059.556;
        private const double WMfMaltaX  = 8246.75321353;
        private const double Norm1MfX   = 1009002.70582;
        private const double WHfMalta   = 18.7237414387;
        private const double Norm1Hf    = 4498534.45232;
        private const double WHfMaltaX  = 6923.99476109;
        private const double Norm1HfX   = 8051.15833247;
        private const double WUhfMalta  = 1.10039032555;
        private const double Norm1Uhf   = 71.7800275169;
        private const double WUhfMaltaX = 173.5;
        private const double Norm1UhfX  = 5.0;

        // std::log(80.0) / std::log(255.0);
        private const float KIntensityTargetNormalizationHack = 0.79079917404f;
        private const float KInternalGoodQualityThreshold     = 17.83f * KIntensityTargetNormalizationHack;
        private const float KGlobalScale                      = 1.0f / KInternalGoodQualityThreshold;

        private static readonly double[] Wmul = new double[9]
        {
            400.0,         1.50815703118,  0.0,
            2150.0,        10.6195433239,  16.2176043152,
            29.2353797994, 0.844626970982, 0.703646627719,
        };

        private static readonly double[,] HeatmapColors = new double[12, 3]
        {
            { 0.0, 0.0, 0.0 },
            { 0.0, 0.0, 1.0 },
            { 0.0, 1.0, 1.0 },
            { 0.0, 1.0, 0.0 }, // Good level
            { 1.0, 1.0, 0.0 },
            { 1.0, 0.0, 0.0 }, // Bad level
            { 1.0, 0.0, 1.0 },
            { 0.5, 0.5, 1.0 },
            { 1.0, 0.5, 0.5 }, // Pastel colors for the very bad quality range.
            { 1.0, 1.0, 0.5 },
            { 1.0, 1.0, 1.0 },
            { 1.0, 1.0, 1.0 }, // Last color repeated to have a solid range of white.
        };

        /// <summary>
        /// Computes the Butteraugli difference map and global score between two linear RGB images.
        /// Ported from <c>jxl::ButteraugliInterface</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image.</param>
        /// <param name="rgb1">Distorted linear RGB image.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="diffmap">Output localized difference map.</param>
        /// <param name="diffvalue">Output global similarity score.</param>
        /// <returns><c>true</c> on success.</returns>
        public static bool ButteraugliInterface(
            Image3<float> rgb0,
            Image3<float> rgb1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap,
            out double diffvalue)
        {
            diffvalue = 0.0;

            if (!ButteraugliDiffmap(rgb0, rgb1, in baParams, ref diffmap))
            {
                return false;
            }

            diffvalue = ButteraugliScoreFromDiffmap(diffmap, in baParams);

            return true;
        }

        /// <summary>
        /// Computes the Butteraugli difference map and global score using explicit <paramref name="hfAsymmetry"/> and <paramref name="xmul"/>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image.</param>
        /// <param name="rgb1">Distorted linear RGB image.</param>
        /// <param name="hfAsymmetry">HF asymmetry multiplier.</param>
        /// <param name="xmul">X channel multiplier.</param>
        /// <param name="diffmap">Output localized difference map.</param>
        /// <param name="diffvalue">Output global similarity score.</param>
        /// <returns><c>true</c> on success.</returns>
        public static bool ButteraugliInterface(
            Image3<float> rgb0,
            Image3<float> rgb1,
            float hfAsymmetry,
            float xmul,
            ref ImagePlane<float> diffmap,
            out double diffvalue)
        {
            ButteraugliParams baParams = new ButteraugliParams
            {
                HfAsymmetry     = hfAsymmetry,
                Xmul            = xmul,
                IntensityTarget = 80.0f,
            };

            return ButteraugliInterface(rgb0, rgb1, in baParams, ref diffmap, out diffvalue);
        }

        /// <summary>
        /// Same as <see cref="ButteraugliInterface(Image3{float},Image3{float},in ButteraugliParams,ref ImagePlane{float},out double)"/>,
        /// but reuses <paramref name="rgb0"/> and <paramref name="rgb1"/> in-place and ignores <c>Xmul</c>.
        /// Ported from <c>jxl::ButteraugliInterfaceInPlace</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image (consumed in-place).</param>
        /// <param name="rgb1">Distorted linear RGB image (consumed in-place).</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="diffmap">Output difference map.</param>
        /// <param name="diffvalue">Output global score.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ButteraugliInterfaceInPlace(
            Image3<float> rgb0,
            Image3<float> rgb1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap,
            out double diffvalue)
        {
            diffvalue   = 0.0;
            nuint xsize = rgb0.XSize;
            nuint ysize = rgb0.YSize;

            if (xsize < 1 || ysize < 1 || !ImageOps.SameSize(rgb0, rgb1))
            {
                return Status.Failure("Invalid or mismatched dimensions in ButteraugliInterfaceInPlace");
            }

            if (xsize < 8 || ysize < 8)
            {
                bool ok   = ButteraugliDiffmapSmall(rgb0, rgb1, in baParams, ref diffmap);
                diffvalue = ButteraugliScoreFromDiffmap(diffmap, in baParams);

                return ok;
            }

            return ExecuteButteraugliInPlaceLarge(rgb0, rgb1, in baParams, ref diffmap, out diffvalue);
        }

        /// <summary>
        /// Computes the Butteraugli difference map between <paramref name="rgb0"/> and <paramref name="rgb1"/>.
        /// Ported from <c>jxl::ButteraugliDiffmap</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image.</param>
        /// <param name="rgb1">Distorted linear RGB image.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="diffmap">Output difference map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ButteraugliDiffmap(
            Image3<float> rgb0,
            Image3<float> rgb1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap)
        {
            nuint xsize = rgb0.XSize;
            nuint ysize = rgb0.YSize;

            if (xsize < 1 || ysize < 1)
            {
                return Status.Failure("Zero-sized image");
            }

            if (!ImageOps.SameSize(rgb0, rgb1))
            {
                return Status.Failure("Size mismatch");
            }

            if (xsize < 8 || ysize < 8)
            {
                return ButteraugliDiffmapSmall(rgb0, rgb1, in baParams, ref diffmap);
            }

            StatusOr<ButteraugliComparator> compOr = ButteraugliComparator.Make(rgb0, in baParams);

            if (!compOr.IsOk)
            {
                return compOr.Status;
            }

            using ButteraugliComparator comp = compOr.Value;

            return comp.Diffmap(rgb1, ref diffmap);
        }

        /// <summary>
        /// Computes the Butteraugli difference map using explicit <paramref name="hfAsymmetry"/> and <paramref name="xmul"/>.
        /// </summary>
        /// <param name="rgb0">Reference linear RGB image.</param>
        /// <param name="rgb1">Distorted linear RGB image.</param>
        /// <param name="hfAsymmetry">HF asymmetry multiplier.</param>
        /// <param name="xmul">X channel multiplier.</param>
        /// <param name="diffmap">Output difference map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ButteraugliDiffmap(
            Image3<float> rgb0,
            Image3<float> rgb1,
            double hfAsymmetry,
            double xmul,
            ref ImagePlane<float> diffmap)
        {
            ButteraugliParams baParams = new ButteraugliParams
            {
                HfAsymmetry     = (float)hfAsymmetry,
                Xmul            = (float)xmul,
                IntensityTarget = 80.0f,
            };

            return ButteraugliDiffmap(rgb0, rgb1, in baParams, ref diffmap);
        }

        /// <summary>
        /// Computes the scalar Butteraugli distance score (maximum value) from a difference map.
        /// Ported from <c>jxl::ButteraugliScoreFromDiffmap</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="diffmap">Difference map plane.</param>
        /// <param name="baParams">Optional Butteraugli parameters (unused, retained for C++ parity).</param>
        /// <returns>Maximum difference score across the image.</returns>
        public static double ButteraugliScoreFromDiffmap(
            ImagePlane<float> diffmap,
            in ButteraugliParams baParams = default)
        {
            float retval = 0.0f;

            for (nuint y = 0; y < diffmap.YSize; ++y)
            {
                ReadOnlySpan<float> row = diffmap.ConstRow(y);

                for (nuint x = 0; x < diffmap.XSize; ++x)
                {
                    int ix = (int)x;

                    if (row[ix] > retval)
                    {
                        retval = row[ix];
                    }
                }
            }

            return retval;
        }

        /// <summary>
        /// Converts a Butteraugli score into a continuous fuzzy class value (2.0 = perfect, 1.0 = ok, 0.0 = bad).
        /// Ported from <c>jxl::ButteraugliFuzzyClass</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="score">Butteraugli distance score.</param>
        /// <returns>Fuzzy class value in <c>[0.0, 2.0]</c>.</returns>
        public static double ButteraugliFuzzyClass(double score)
        {
            const double fuzzyWidthUp   = 4.8;
            const double fuzzyWidthDown = 4.8;
            const double m0             = 2.0;
            const double scaler         = 0.7777;

            if (score < 1.0)
            {
                double val = m0 / (1.0 + Math.Exp((score - 1.0) * fuzzyWidthDown));
                val       -= 1.0;
                val       *= 2.0 - scaler;
                val       += scaler;

                return val;
            }
            else
            {
                double val = m0 / (1.0 + Math.Exp((score - 1.0) * fuzzyWidthUp));

                return val * scaler;
            }
        }

        /// <summary>
        /// Computes the inverse of <see cref="ButteraugliFuzzyClass"/> for a target fuzzy class in <c>[0, 2]</c>.
        /// Ported from <c>jxl::ButteraugliFuzzyInverse</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="seek">Target fuzzy class value.</param>
        /// <returns>Corresponding Butteraugli score.</returns>
        public static double ButteraugliFuzzyInverse(double seek)
        {
            double pos = 0.0;

            for (double range = 1.0; range >= 1e-10; range *= 0.5)
            {
                double cur = ButteraugliFuzzyClass(pos);

                if (cur < seek)
                {
                    pos -= range;
                }
                else
                {
                    pos += range;
                }
            }

            return pos;
        }

        /// <summary>
        /// Generates an RGB heatmap representation of a Butteraugli distance map.
        /// Ported from <c>jxl::CreateHeatMapImage</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="distmap">Input distance map.</param>
        /// <param name="goodThreshold">Threshold for good quality.</param>
        /// <param name="badThreshold">Threshold for bad quality.</param>
        /// <returns>3-plane RGB heatmap image.</returns>
        public static StatusOr<Image3<float>> CreateHeatMapImage(
            ImagePlane<float> distmap,
            double goodThreshold,
            double badThreshold)
        {
            JxlMemoryManager? memoryManager = distmap.MemoryManager;
            StatusOr<Image3<float>> imageOr = Image3<float>.Create(memoryManager, distmap.XSize, distmap.YSize);

            if (!imageOr.IsOk)
            {
                return imageOr.Status;
            }

            Image3<float> heatmap = imageOr.Value;
            Span<float> rgb       = stackalloc float[3];

            for (nuint y = 0; y < distmap.YSize; ++y)
            {
                ReadOnlySpan<float> rowDistmap = distmap.ConstRow(y);
                Span<float> rowH0              = heatmap.PlaneRow(0, y);
                Span<float> rowH1              = heatmap.PlaneRow(1, y);
                Span<float> rowH2              = heatmap.PlaneRow(2, y);

                for (nuint x = 0; x < distmap.XSize; ++x)
                {
                    int ix = (int)x;
                    ScoreToRgb(rowDistmap[ix], goodThreshold, badThreshold, rgb);
                    rowH0[ix] = rgb[0];
                    rowH1[ix] = rgb[1];
                    rowH2[ix] = rgb[2];
                }
            }

            return heatmap;
        }

        /// <summary>
        /// Computes a 1D Gaussian blur kernel for the given <paramref name="sigma"/>.
        /// Ported from <c>jxl::ComputeKernel</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="sigma">Standard deviation.</param>
        /// <returns>Unnormalized 1D Gaussian kernel array.</returns>
        public static float[] ComputeKernel(float sigma)
        {
            const float m      = 2.25f;
            double scaler      = -1.0 / (2.0 * sigma * sigma);
            int diff           = Math.Max(1, (int)(m * MathF.Abs(sigma)));
            float[] kernel     = new float[(2 * diff) + 1];

            for (int i = -diff; i <= diff; ++i)
            {
                kernel[i + diff] = (float)Math.Exp(scaler * i * i);
            }

            return kernel;
        }

        /// <summary>
        /// Applies a 2D separable Gaussian-like blur to <paramref name="inPlane"/> into <paramref name="outPlane"/>.
        /// Ported from <c>jxl::Blur</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="inPlane">Input image plane.</param>
        /// <param name="sigma">Blur sigma.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="temp">Temporary transpose buffer holder.</param>
        /// <param name="outPlane">Output blurred image plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status Blur(
            ImagePlane<float> inPlane,
            float sigma,
            in ButteraugliParams baParams,
            BlurTemp temp,
            ImagePlane<float> outPlane)
        {
            float[] kernel = ComputeKernel(sigma);

            if (kernel.Length == 5 && !ReferenceEquals(inPlane, outPlane))
            {
                float sumWeights = kernel[0] + kernel[1] + kernel[2] + kernel[3] + kernel[4];
                float scale      = 1.0f / sumWeights;
                float w0         = kernel[2] * scale;
                float w1         = kernel[1] * scale;
                float w2         = kernel[0] * scale;

                WeightsSeparable5 weights = new WeightsSeparable5(w0, w1, w2, w0, w1, w2);

                return Convolve.Separable5(inPlane, new Rect(inPlane), weights, null, outPlane);
            }

            if (!temp.GetTransposed(inPlane, out ImagePlane<float> tempT))
            {
                return false;
            }

            if (!ConvolutionWithTranspose(inPlane, kernel, tempT))
            {
                return false;
            }

            return ConvolutionWithTranspose(tempT, kernel, outPlane);
        }

        /// <summary>
        /// Converts linear RGB to psychovisual XYB using Opsin absorbance and dynamic adaptation.
        /// Ported from <c>jxl::OpsinDynamicsImage</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="rgb">Input linear RGB image.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="blurred">Scratch 3-plane image for blurred RGB.</param>
        /// <param name="blurTemp">Scratch transpose buffer.</param>
        /// <param name="xyb">Output XYB image (may alias <paramref name="rgb"/>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status OpsinDynamicsImage(
            Image3<float> rgb,
            in ButteraugliParams baParams,
            Image3<float> blurred,
            BlurTemp blurTemp,
            Image3<float> xyb)
        {
            const float kSigma = 1.2f;

            if (!Blur(rgb.Plane(0), kSigma, in baParams, blurTemp, blurred.Plane(0)) ||
                !Blur(rgb.Plane(1), kSigma, in baParams, blurTemp, blurred.Plane(1)) ||
                !Blur(rgb.Plane(2), kSigma, in baParams, blurTemp, blurred.Plane(2)))
            {
                return false;
            }

            float intensityTarget = baParams.IntensityTarget;

            for (nuint y = 0; y < rgb.YSize; ++y)
            {
                OpsinDynamicsRow(rgb, blurred, xyb, y, intensityTarget);
            }

            return true;
        }

        /// <summary>
        /// Separates an XYB image into LF, MF, HF, and UHF frequency bands in <paramref name="ps"/>.
        /// Ported from <c>jxl::SeparateFrequencies</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="xsize">Image width.</param>
        /// <param name="ysize">Image height.</param>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="blurTemp">Scratch transpose buffer.</param>
        /// <param name="xyb">Input Opsin-dynamics XYB image.</param>
        /// <param name="ps">Output psychovisual frequency decomposition.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status SeparateFrequencies(
            nuint xsize,
            nuint ysize,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            Image3<float> xyb,
            PsychoImage ps)
        {
            JxlMemoryManager? memoryManager = xyb.MemoryManager;
            ps.Lf.Dispose();
            ps.Mf.Dispose();

            StatusOr<Image3<float>> lfOr = Image3<float>.Create(memoryManager, xyb.XSize, xyb.YSize);
            StatusOr<Image3<float>> mfOr = Image3<float>.Create(memoryManager, xyb.XSize, xyb.YSize);

            if (!lfOr.IsOk || !mfOr.IsOk)
            {
                DisposeIfOk(lfOr);
                DisposeIfOk(mfOr);

                return Status.Failure("Failed to allocate PsychoImage LF/MF planes");
            }

            ps.Lf = lfOr.Value;
            ps.Mf = mfOr.Value;

            if (!SeparateLFAndMF(in baParams, xyb, ps.Lf, ps.Mf, blurTemp))
            {
                return false;
            }

            if (!SeparateMFAndHF(in baParams, ps.Mf, ps.Hf, blurTemp))
            {
                return false;
            }

            return SeparateHFAndUHF(in baParams, ps.Hf, ps.Uhf, blurTemp);
        }

        /// <summary>
        /// Computes the high-frequency Malta directional difference map and accumulates into <paramref name="blockDiffAc"/>.
        /// Ported from <c>jxl::MaltaDiffMap</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="lum0">Reference plane.</param>
        /// <param name="lum1">Distorted plane.</param>
        /// <param name="w0Gt1">Weight when original &gt; distorted.</param>
        /// <param name="w0Lt1">Weight when original &lt; distorted.</param>
        /// <param name="norm1">Normalization constant.</param>
        /// <param name="diffs">Scratch scaled difference plane.</param>
        /// <param name="blockDiffAc">Accumulated AC difference plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status MaltaDiffMap(
            ImagePlane<float> lum0,
            ImagePlane<float> lum1,
            double w0Gt1,
            double w0Lt1,
            double norm1,
            ImagePlane<float> diffs,
            ImagePlane<float> blockDiffAc)
        {
            const double len   = 3.75;
            const double mulli = 0.39905817637;

            return MaltaDiffMapImpl(false, lum0, lum1, (w0Gt1, w0Lt1, norm1, len, mulli), diffs, blockDiffAc);
        }

        /// <summary>
        /// Computes the low-frequency Malta directional difference map and accumulates into <paramref name="blockDiffAc"/>.
        /// Ported from <c>jxl::MaltaDiffMapLF</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c>.
        /// </summary>
        /// <param name="lum0">Reference plane.</param>
        /// <param name="lum1">Distorted plane.</param>
        /// <param name="w0Gt1">Weight when original &gt; distorted.</param>
        /// <param name="w0Lt1">Weight when original &lt; distorted.</param>
        /// <param name="norm1">Normalization constant.</param>
        /// <param name="diffs">Scratch scaled difference plane.</param>
        /// <param name="blockDiffAc">Accumulated AC difference plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status MaltaDiffMapLF(
            ImagePlane<float> lum0,
            ImagePlane<float> lum1,
            double w0Gt1,
            double w0Lt1,
            double norm1,
            ImagePlane<float> diffs,
            ImagePlane<float> blockDiffAc)
        {
            const double len   = 3.75;
            const double mulli = 0.611612573796;

            return MaltaDiffMapImpl(true, lum0, lum1, (w0Gt1, w0Lt1, norm1, len, mulli), diffs, blockDiffAc);
        }

        internal static void DisposeIfOk<T>(StatusOr<T> statusOr)
            where T : IDisposable
        {
            if (statusOr.IsOk)
            {
                statusOr.Value.Dispose();
            }
        }

        internal static Status MaskPsychoImage(
            PsychoImage pi0,
            PsychoImage pi1,
            nuint xsize,
            nuint ysize,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            out ImagePlane<float> mask)
        {
            return MaskPsychoImageWithAc(pi0, pi1, in baParams, blurTemp, out mask, null);
        }

        internal static Status MaskPsychoImageWithAc(
            PsychoImage pi0,
            PsychoImage pi1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            out ImagePlane<float> mask,
            ImagePlane<float>? diffAc)
        {
            mask                            = new ImagePlane<float>();
            nuint xsize                     = pi0.Hf[0].XSize;
            nuint ysize                     = pi0.Hf[0].YSize;
            JxlMemoryManager? memoryManager = pi0.Hf[0].MemoryManager;

            StatusOr<ImagePlane<float>> mask0Or = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> mask1Or = ImagePlane<float>.Create(memoryManager, xsize, ysize);

            if (!mask0Or.IsOk || !mask1Or.IsOk)
            {
                DisposeIfOk(mask0Or);
                DisposeIfOk(mask1Or);

                return Status.Failure("Failed to allocate MaskPsychoImage planes");
            }

            using ImagePlane<float> mask0 = mask0Or.Value;
            using ImagePlane<float> mask1 = mask1Or.Value;

            CombineChannelsForMasking(pi0.Hf, pi0.Uhf, mask0);
            CombineChannelsForMasking(pi1.Hf, pi1.Uhf, mask1);

            return ComputeMask(mask0, mask1, in baParams, blurTemp, out mask, diffAc);
        }

        internal static Status AccumulatePsychoAcAndDc(
            PsychoImage pi0,
            PsychoImage pi1,
            float hfAsymmetry,
            ImagePlane<float> diffs,
            Image3<float> blockDiffAc,
            Image3<float> blockDiffDc)
        {
            double sqrtAsym = Math.Sqrt(hfAsymmetry);

            if (!MaltaDiffMap(pi0.Uhf[1], pi1.Uhf[1], WUhfMalta * hfAsymmetry, WUhfMalta / hfAsymmetry, Norm1Uhf, diffs, blockDiffAc.Plane(1)) ||
                !MaltaDiffMap(pi0.Uhf[0], pi1.Uhf[0], WUhfMaltaX * hfAsymmetry, WUhfMaltaX / hfAsymmetry, Norm1UhfX, diffs, blockDiffAc.Plane(0)) ||
                !MaltaDiffMapLF(pi0.Hf[1], pi1.Hf[1], WHfMalta * sqrtAsym, WHfMalta / sqrtAsym, Norm1Hf, diffs, blockDiffAc.Plane(1)) ||
                !MaltaDiffMapLF(pi0.Hf[0], pi1.Hf[0], WHfMaltaX * sqrtAsym, WHfMaltaX / sqrtAsym, Norm1HfX, diffs, blockDiffAc.Plane(0)) ||
                !MaltaDiffMapLF(pi0.Mf.Plane(1), pi1.Mf.Plane(1), WMfMalta, WMfMalta, Norm1Mf, diffs, blockDiffAc.Plane(1)) ||
                !MaltaDiffMapLF(pi0.Mf.Plane(0), pi1.Mf.Plane(0), WMfMaltaX, WMfMaltaX, Norm1MfX, diffs, blockDiffAc.Plane(0)))
            {
                return false;
            }

            for (nuint c = 0; c < 3; ++c)
            {
                if (c < 2)
                {
                    L2DiffAsymmetric(
                        pi0.Hf[c],
                        pi1.Hf[c],
                        (float)(Wmul[c] * hfAsymmetry),
                        (float)(Wmul[c] / hfAsymmetry),
                        blockDiffAc.Plane(c));
                }

                L2Diff(pi0.Mf.Plane(c), pi1.Mf.Plane(c), (float)Wmul[3 + c], blockDiffAc.Plane(c));
                SetL2Diff(pi0.Lf.Plane(c), pi1.Lf.Plane(c), (float)Wmul[6 + c], blockDiffDc.Plane(c));
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        internal static Status CombineChannelsToDiffmap(
            ImagePlane<float> mask,
            Image3<float> blockDiffDc,
            Image3<float> blockDiffAc,
            float xmul,
            ImagePlane<float> result)
        {
            if (!ImageOps.SameSize(mask, result))
            {
                return Status.Failure("Mask and result size mismatch");
            }

            nuint xsize = mask.XSize;
            nuint ysize = mask.YSize;

            for (nuint y = 0; y < ysize; ++y)
            {
                Span<float> rowOut          = result.Row(y);
                ReadOnlySpan<float> rowMask = mask.ConstRow(y);
                ReadOnlySpan<float> dc0     = blockDiffDc.ConstPlaneRow(0, y);
                ReadOnlySpan<float> dc1     = blockDiffDc.ConstPlaneRow(1, y);
                ReadOnlySpan<float> dc2     = blockDiffDc.ConstPlaneRow(2, y);
                ReadOnlySpan<float> ac0     = blockDiffAc.ConstPlaneRow(0, y);
                ReadOnlySpan<float> ac1     = blockDiffAc.ConstPlaneRow(1, y);
                ReadOnlySpan<float> ac2     = blockDiffAc.ConstPlaneRow(2, y);

                for (nuint x = 0; x < xsize; ++x)
                {
                    int ix          = (int)x;
                    float val       = rowMask[ix];
                    float maskval   = (float)MaskY(val);
                    float dcMaskval = (float)MaskDcY(val);
                    float sumDc     = ((dc0[ix] * xmul) + dc1[ix] + dc2[ix]) * dcMaskval;
                    float sumAc     = ((ac0[ix] * xmul) + ac1[ix] + ac2[ix]) * maskval;

                    rowOut[ix] = MathF.Sqrt(sumDc + sumAc);
                }
            }

            return true;
        }

        internal static StatusOr<Image3<float>> SubSample2x(Image3<float> input)
        {
            nuint xs                        = (input.XSize + 1) / 2;
            nuint ys                        = (input.YSize + 1) / 2;
            JxlMemoryManager? memoryManager = input.MemoryManager;
            StatusOr<Image3<float>> retOr   = Image3<float>.Create(memoryManager, xs, ys);

            if (!retOr.IsOk)
            {
                return retOr.Status;
            }

            Image3<float> retval = retOr.Value;
            ImageOps.ZeroFillImage(retval);

            for (nuint c = 0; c < 3; ++c)
            {
                AccumulateSubSamplePlane(input.Plane(c), retval.Plane(c));
            }

            return retval;
        }

        internal static void AddSupersampled2x(ImagePlane<float> src, float w, ImagePlane<float> dest)
        {
            const double kHeuristicMixingValue = 0.3;
            float scale                        = (float)(1.0 - (kHeuristicMixingValue * w));

            for (nuint y = 0; y < dest.YSize; ++y)
            {
                Span<float> rowDest        = dest.Row(y);
                ReadOnlySpan<float> rowSrc = src.ConstRow(y / 2);

                for (nuint x = 0; x < dest.XSize; ++x)
                {
                    int ix      = (int)x;
                    rowDest[ix] = (rowDest[ix] * scale) + (w * rowSrc[(int)(x / 2)]);
                }
            }
        }

        private static void AccumulateSubSamplePlane(ImagePlane<float> inPlane, ImagePlane<float> outPlane)
        {
            for (nuint y = 0; y < inPlane.YSize; ++y)
            {
                ReadOnlySpan<float> rowIn = inPlane.ConstRow(y);
                Span<float> rowOut        = outPlane.Row(y / 2);

                for (nuint x = 0; x < inPlane.XSize; ++x)
                {
                    rowOut[(int)(x / 2)] += 0.25f * rowIn[(int)x];
                }
            }

            if ((inPlane.XSize & 1) != 0)
            {
                int lastColumn = (int)(outPlane.XSize - 1);

                for (nuint y = 0; y < outPlane.YSize; ++y)
                {
                    outPlane.Row(y)[lastColumn] *= 2.0f;
                }
            }

            if ((inPlane.YSize & 1) != 0)
            {
                Span<float> lastRow = outPlane.Row(outPlane.YSize - 1);

                for (nuint x = 0; x < outPlane.XSize; ++x)
                {
                    lastRow[(int)x] *= 2.0f;
                }
            }
        }

        private static Status ExecuteButteraugliInPlaceLarge(
            Image3<float> rgb0,
            Image3<float> rgb1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap,
            out double diffvalue)
        {
            diffvalue   = 0.0;
            nuint xsize = rgb0.XSize;
            nuint ysize = rgb0.YSize;

            ImagePlane<float> subdiffmap = new ImagePlane<float>();

            try
            {
                if (xsize >= 15 && ysize >= 15)
                {
                    StatusOr<Image3<float>> sub0Or = SubSample2x(rgb0);
                    StatusOr<Image3<float>> sub1Or = SubSample2x(rgb1);

                    if (!sub0Or.IsOk || !sub1Or.IsOk)
                    {
                        DisposeIfOk(sub0Or);
                        DisposeIfOk(sub1Or);

                        return false;
                    }

                    using Image3<float> sub0 = sub0Or.Value;
                    using Image3<float> sub1 = sub1Or.Value;

                    if (!ButteraugliDiffmapInPlace(sub0, sub1, in baParams, ref subdiffmap))
                    {
                        return false;
                    }
                }

                if (!ButteraugliDiffmapInPlace(rgb0, rgb1, in baParams, ref diffmap))
                {
                    return false;
                }

                if (xsize >= 15 && ysize >= 15)
                {
                    AddSupersampled2x(subdiffmap, 0.5f, diffmap);
                }

                diffvalue = ButteraugliScoreFromDiffmap(diffmap, in baParams);

                return true;
            }
            finally
            {
                subdiffmap.Dispose();
            }
        }

        private static Status ButteraugliDiffmapInPlace(
            Image3<float> image0,
            Image3<float> image1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap)
        {
            nuint xsize                     = image0.XSize;
            nuint ysize                     = image0.YSize;
            JxlMemoryManager? memoryManager = image0.MemoryManager;
            using BlurTemp blurTemp         = new BlurTemp();

            if (!ConvertPairToOpsinInPlace(image0, image1, in baParams, blurTemp))
            {
                return false;
            }

            StatusOr<ImagePlane<float>> dcOr = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> acOr = ImagePlane<float>.Create(memoryManager, xsize, ysize);

            if (!dcOr.IsOk || !acOr.IsOk)
            {
                DisposeIfOk(dcOr);
                DisposeIfOk(acOr);

                return false;
            }

            using ImagePlane<float> blockDiffDc = dcOr.Value;
            using ImagePlane<float> blockDiffAc = acOr.Value;
            ImageOps.ZeroFillImage(blockDiffDc);
            ImageOps.ZeroFillImage(blockDiffAc);

            if (!ComputeInPlaceLfAndMf(image0, image1, in baParams, blurTemp, blockDiffDc, blockDiffAc, out ImagePlane<float>[] hf0, out ImagePlane<float>[] hf1))
            {
                return false;
            }

            return FinishInPlaceHfAndMask(hf0, hf1, in baParams, blurTemp, blockDiffDc, blockDiffAc, ref diffmap);
        }

        private static Status ConvertPairToOpsinInPlace(
            Image3<float> image0,
            Image3<float> image1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp)
        {
            StatusOr<Image3<float>> tempOr = Image3<float>.Create(image0.MemoryManager, image0.XSize, image0.YSize);

            if (!tempOr.IsOk)
            {
                return tempOr.Status;
            }

            using Image3<float> temp = tempOr.Value;

            if (!OpsinDynamicsImage(image0, in baParams, temp, blurTemp, image0))
            {
                return false;
            }

            return OpsinDynamicsImage(image1, in baParams, temp, blurTemp, image1);
        }

        private static Status ComputeInPlaceLfAndMf(
            Image3<float> image0,
            Image3<float> image1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            ImagePlane<float> blockDiffDc,
            ImagePlane<float> blockDiffAc,
            out ImagePlane<float>[] hf0,
            out ImagePlane<float>[] hf1)
        {
            hf0 = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };
            hf1 = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };

            JxlMemoryManager? memoryManager = image0.MemoryManager;
            nuint xsize                     = image0.XSize;
            nuint ysize                     = image0.YSize;

            StatusOr<Image3<float>> lf0Or = Image3<float>.Create(memoryManager, xsize, ysize);
            StatusOr<Image3<float>> lf1Or = Image3<float>.Create(memoryManager, xsize, ysize);

            if (!lf0Or.IsOk || !lf1Or.IsOk)
            {
                DisposeIfOk(lf0Or);
                DisposeIfOk(lf1Or);

                return false;
            }

            using (Image3<float> lf0 = lf0Or.Value)
            using (Image3<float> lf1 = lf1Or.Value)
            {
                if (!SeparateLFAndMF(in baParams, image0, lf0, image0, blurTemp) ||
                    !SeparateLFAndMF(in baParams, image1, lf1, image1, blurTemp))
                {
                    return false;
                }

                for (nuint c = 0; c < 3; ++c)
                {
                    L2Diff(lf0.Plane(c), lf1.Plane(c), (float)Wmul[6 + c], blockDiffDc);
                }
            }

            if (!SeparateMFAndHF(in baParams, image0, hf0, blurTemp) ||
                !SeparateMFAndHF(in baParams, image1, hf1, blurTemp))
            {
                return false;
            }

            return AccumulateInPlaceMfDiffs(image0, image1, blockDiffAc);
        }

        private static Status AccumulateInPlaceMfDiffs(
            Image3<float> image0,
            Image3<float> image1,
            ImagePlane<float> blockDiffAc)
        {
            StatusOr<ImagePlane<float>> diffsOr = ImagePlane<float>.Create(image0.MemoryManager, image0.XSize, image0.YSize);

            if (!diffsOr.IsOk)
            {
                return diffsOr.Status;
            }

            using (ImagePlane<float> diffs = diffsOr.Value)
            {
                if (!MaltaDiffMapLF(image0.Plane(1), image1.Plane(1), WMfMalta, WMfMalta, Norm1Mf, diffs, blockDiffAc) ||
                    !MaltaDiffMapLF(image0.Plane(0), image1.Plane(0), WMfMaltaX, WMfMaltaX, Norm1MfX, diffs, blockDiffAc))
                {
                    return false;
                }
            }

            for (nuint c = 0; c < 3; ++c)
            {
                L2Diff(image0.Plane(c), image1.Plane(c), (float)Wmul[3 + c], blockDiffAc);
            }

            return true;
        }

        private static Status FinishInPlaceHfAndMask(
            ImagePlane<float>[] hf0,
            ImagePlane<float>[] hf1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            ImagePlane<float> blockDiffDc,
            ImagePlane<float> blockDiffAc,
            ref ImagePlane<float> diffmap)
        {
            ImagePlane<float>[] uhf0 = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };
            ImagePlane<float>[] uhf1 = new ImagePlane<float>[2] { new ImagePlane<float>(), new ImagePlane<float>() };

            try
            {
                if (!SeparateHFAndUHF(in baParams, hf0, uhf0, blurTemp) ||
                    !SeparateHFAndUHF(in baParams, hf1, uhf1, blurTemp) ||
                    !AccumulateInPlaceHfAndUhfDiffs(hf0, hf1, uhf0, uhf1, baParams.HfAsymmetry, blockDiffAc))
                {
                    return false;
                }

                if (!ComputeCombinedMaskInPlace(hf0, hf1, uhf0, uhf1, in baParams, blurTemp, out ImagePlane<float> mask, blockDiffAc))
                {
                    return false;
                }

                using (mask)
                {
                    return FinalizeInPlaceDiffmap(mask, blockDiffDc, blockDiffAc, ref diffmap);
                }
            }
            finally
            {
                DeallocateHFAndUHF(hf0, uhf0);
                DeallocateHFAndUHF(hf1, uhf1);
            }
        }

        private static Status AccumulateInPlaceHfAndUhfDiffs(
            ImagePlane<float>[] hf0,
            ImagePlane<float>[] hf1,
            ImagePlane<float>[] uhf0,
            ImagePlane<float>[] uhf1,
            float hfAsymmetry,
            ImagePlane<float> blockDiffAc)
        {
            StatusOr<ImagePlane<float>> diffsOr = ImagePlane<float>.Create(hf0[0].MemoryManager, hf0[0].XSize, hf0[0].YSize);

            if (!diffsOr.IsOk)
            {
                return diffsOr.Status;
            }

            double sqrtAsym = Math.Sqrt(hfAsymmetry);

            using (ImagePlane<float> diffs = diffsOr.Value)
            {
                if (!MaltaDiffMap(uhf0[1], uhf1[1], WUhfMalta * hfAsymmetry, WUhfMalta / hfAsymmetry, Norm1Uhf, diffs, blockDiffAc) ||
                    !MaltaDiffMap(uhf0[0], uhf1[0], WUhfMaltaX * hfAsymmetry, WUhfMaltaX / hfAsymmetry, Norm1UhfX, diffs, blockDiffAc) ||
                    !MaltaDiffMapLF(hf0[1], hf1[1], WHfMalta * sqrtAsym, WHfMalta / sqrtAsym, Norm1Hf, diffs, blockDiffAc) ||
                    !MaltaDiffMapLF(hf0[0], hf1[0], WHfMaltaX * sqrtAsym, WHfMaltaX / sqrtAsym, Norm1HfX, diffs, blockDiffAc))
                {
                    return false;
                }
            }

            for (int c = 0; c < 2; ++c)
            {
                L2DiffAsymmetric(hf0[c], hf1[c], (float)(Wmul[c] * hfAsymmetry), (float)(Wmul[c] / hfAsymmetry), blockDiffAc);
            }

            return true;
        }

        private static Status ComputeCombinedMaskInPlace(
            ImagePlane<float>[] hf0,
            ImagePlane<float>[] hf1,
            ImagePlane<float>[] uhf0,
            ImagePlane<float>[] uhf1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            out ImagePlane<float> mask,
            ImagePlane<float> blockDiffAc)
        {
            mask                            = new ImagePlane<float>();
            JxlMemoryManager? memoryManager = hf0[0].MemoryManager;
            nuint xsize                     = hf0[0].XSize;
            nuint ysize                     = hf0[0].YSize;

            StatusOr<ImagePlane<float>> m0Or = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> m1Or = ImagePlane<float>.Create(memoryManager, xsize, ysize);

            if (!m0Or.IsOk || !m1Or.IsOk)
            {
                DisposeIfOk(m0Or);
                DisposeIfOk(m1Or);

                return false;
            }

            using ImagePlane<float> mask0 = m0Or.Value;
            using ImagePlane<float> mask1 = m1Or.Value;
            CombineChannelsForMasking(hf0, uhf0, mask0);
            CombineChannelsForMasking(hf1, uhf1, mask1);
            DeallocateHFAndUHF(hf1, uhf1);
            DeallocateHFAndUHF(hf0, uhf0);

            return ComputeMask(mask0, mask1, in baParams, blurTemp, out mask, blockDiffAc);
        }

        private static Status FinalizeInPlaceDiffmap(
            ImagePlane<float> mask,
            ImagePlane<float> blockDiffDc,
            ImagePlane<float> blockDiffAc,
            ref ImagePlane<float> diffmap)
        {
            nuint xsize = mask.XSize;
            nuint ysize = mask.YSize;

            if (diffmap.XSize != xsize || diffmap.YSize != ysize)
            {
                diffmap.Dispose();
                StatusOr<ImagePlane<float>> created = ImagePlane<float>.Create(mask.MemoryManager, xsize, ysize);

                if (!created.IsOk)
                {
                    return created.Status;
                }

                diffmap = created.Value;
            }

            for (nuint y = 0; y < ysize; ++y)
            {
                ReadOnlySpan<float> rowDc = blockDiffDc.ConstRow(y);
                ReadOnlySpan<float> rowAc = blockDiffAc.ConstRow(y);
                ReadOnlySpan<float> rowM  = mask.ConstRow(y);
                Span<float> rowOut        = diffmap.Row(y);

                for (nuint x = 0; x < xsize; ++x)
                {
                    int ix     = (int)x;
                    float val  = rowM[ix];
                    rowOut[ix] = (float)Math.Sqrt((rowDc[ix] * MaskDcY(val)) + (rowAc[ix] * MaskY(val)));
                }
            }

            return true;
        }

        private static bool ButteraugliDiffmapSmall(
            Image3<float> rgb0,
            Image3<float> rgb1,
            in ButteraugliParams baParams,
            ref ImagePlane<float> diffmap)
        {
            const nuint kMax                = 8;
            nuint xsize                     = rgb0.XSize;
            nuint ysize                     = rgb0.YSize;
            JxlMemoryManager? memoryManager = rgb0.MemoryManager;
            nuint xborder                   = xsize < kMax ? (kMax - xsize) / 2 : 0;
            nuint yborder                   = ysize < kMax ? (kMax - ysize) / 2 : 0;
            nuint xscaled                   = Math.Max(kMax, xsize);
            nuint yscaled                   = Math.Max(kMax, ysize);

            StatusOr<Image3<float>> s0Or = Image3<float>.Create(memoryManager, xscaled, yscaled);
            StatusOr<Image3<float>> s1Or = Image3<float>.Create(memoryManager, xscaled, yscaled);

            if (!s0Or.IsOk || !s1Or.IsOk)
            {
                DisposeIfOk(s0Or);
                DisposeIfOk(s1Or);

                return false;
            }

            using Image3<float> scaled0 = s0Or.Value;
            using Image3<float> scaled1 = s1Or.Value;
            CopyClampedSmallBorder(rgb0, rgb1, scaled0, scaled1, xborder, yborder);

            ImagePlane<float> diffmapScaled = new ImagePlane<float>();

            try
            {
                bool ok = ButteraugliDiffmap(scaled0, scaled1, in baParams, ref diffmapScaled);

                return CropSmallDiffmap(diffmapScaled, memoryManager, xsize, ysize, xborder, yborder, ref diffmap) && ok;
            }
            finally
            {
                diffmapScaled.Dispose();
            }
        }

        private static void CopyClampedSmallBorder(
            Image3<float> rgb0,
            Image3<float> rgb1,
            Image3<float> scaled0,
            Image3<float> scaled1,
            nuint xborder,
            nuint yborder)
        {
            nuint xsize   = rgb0.XSize;
            nuint ysize   = rgb0.YSize;
            nuint xscaled = scaled0.XSize;
            nuint yscaled = scaled0.YSize;

            for (nuint i = 0; i < 3; ++i)
            {
                for (nuint y = 0; y < yscaled; ++y)
                {
                    nuint y2                = Math.Min(ysize - 1, y > yborder ? y - yborder : 0);
                    Span<float> s0R         = scaled0.PlaneRow(i, y);
                    Span<float> s1R         = scaled1.PlaneRow(i, y);
                    ReadOnlySpan<float> r0R = rgb0.ConstPlaneRow(i, y2);
                    ReadOnlySpan<float> r1R = rgb1.ConstPlaneRow(i, y2);

                    for (nuint x = 0; x < xscaled; ++x)
                    {
                        int x2  = (int)Math.Min(xsize - 1, x > xborder ? x - xborder : 0);
                        int ix  = (int)x;
                        s0R[ix] = r0R[x2];
                        s1R[ix] = r1R[x2];
                    }
                }
            }
        }

        private static bool CropSmallDiffmap(
            ImagePlane<float> diffmapScaled,
            JxlMemoryManager? memoryManager,
            nuint xsize,
            nuint ysize,
            nuint xborder,
            nuint yborder,
            ref ImagePlane<float> diffmap)
        {
            if (diffmap.XSize != xsize || diffmap.YSize != ysize)
            {
                diffmap.Dispose();
                StatusOr<ImagePlane<float>> created = ImagePlane<float>.Create(memoryManager, xsize, ysize);

                if (!created.IsOk)
                {
                    return false;
                }

                diffmap = created.Value;
            }

            for (nuint y = 0; y < ysize; ++y)
            {
                Span<float> dstRow         = diffmap.Row(y);
                ReadOnlySpan<float> srcRow = diffmapScaled.ConstRow(y + yborder);

                for (nuint x = 0; x < xsize; ++x)
                {
                    dstRow[(int)x] = srcRow[(int)(x + xborder)];
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ConvolveBorderColumn(
            ImagePlane<float> inPlane,
            float[] kernel,
            nuint x,
            Span<float> rowOut)
        {
            int offset   = kernel.Length / 2;
            int xInt     = (int)x;
            int minx     = xInt < offset ? 0 : xInt - offset;
            int maxx     = Math.Min((int)inPlane.XSize - 1, xInt + offset);
            float weight = 0.0f;

            for (int j = minx; j <= maxx; ++j)
            {
                weight += kernel[j - xInt + offset];
            }

            float scale = 1.0f / weight;

            for (nuint y = 0; y < inPlane.YSize; ++y)
            {
                ReadOnlySpan<float> rowIn = inPlane.ConstRow(y);
                float sum                 = 0.0f;

                for (int j = minx; j <= maxx; ++j)
                {
                    sum += rowIn[j] * kernel[j - xInt + offset];
                }

                rowOut[(int)y] = sum * scale;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status ConvolutionWithTranspose(
            ImagePlane<float> inPlane,
            float[] kernel,
            ImagePlane<float> outPlane)
        {
            if (outPlane.XSize != inPlane.YSize || outPlane.YSize != inPlane.XSize)
            {
                return Status.Failure("Transposed dimensions mismatch in ConvolutionWithTranspose");
            }

            nuint len          = (nuint)kernel.Length;
            nuint offset       = len / 2;
            float weightNoBord = 0.0f;

            for (nuint j = 0; j < len; ++j)
            {
                weightNoBord += kernel[j];
            }

            float scaleNoBord    = 1.0f / weightNoBord;
            nuint border1        = Math.Min(inPlane.XSize, offset);
            nuint border2        = inPlane.XSize > offset ? inPlane.XSize - offset : 0;
            float[] scaledKernel = new float[(len / 2) + 1];

            for (nuint i = 0; i <= len / 2; ++i)
            {
                scaledKernel[i] = kernel[i] * scaleNoBord;
            }

            ConvolveMiddleTranspose(inPlane, scaledKernel, offset, border1, border2, outPlane);

            for (nuint x = 0; x < border1; ++x)
            {
                ConvolveBorderColumn(inPlane, kernel, x, outPlane.Row(x));
            }

            for (nuint x = border2; x < inPlane.XSize; ++x)
            {
                ConvolveBorderColumn(inPlane, kernel, x, outPlane.Row(x));
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ConvolveMiddleTranspose(
            ImagePlane<float> inPlane,
            float[] scaledKernel,
            nuint offset,
            nuint border1,
            nuint border2,
            ImagePlane<float> outPlane)
        {
            int half = (int)offset;

            for (nuint y = 0; y < inPlane.YSize; ++y)
            {
                ReadOnlySpan<float> rowIn = inPlane.ConstRow(y);
                int iy                    = (int)y;

                for (nuint x = border1; x < border2; ++x)
                {
                    int baseIdx = (int)x - half;
                    float sum   = rowIn[baseIdx + half] * scaledKernel[half];

                    for (int i = 0; i < half; ++i)
                    {
                        sum += (rowIn[baseIdx + i] + rowIn[baseIdx + (2 * half) - i]) * scaledKernel[i];
                    }

                    outPlane.Row(x)[iy] = sum;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MaximumClamp(float v, float kMaxVal)
        {
            const float kMul = 0.724216145665f;

            if (v >= kMaxVal)
            {
                return ((v - kMaxVal) * kMul) + kMaxVal;
            }

            if (v < -kMaxVal)
            {
                return ((v + kMaxVal) * kMul) - kMaxVal;
            }

            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float RemoveRangeAroundZero(float kw, float x)
        {
            if (x > kw)
            {
                return x - kw;
            }

            if (x < -kw)
            {
                return x + kw;
            }

            return 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AmplifyRangeAroundZero(float kw, float x)
        {
            if (x > kw)
            {
                return x + kw;
            }

            if (x < -kw)
            {
                return x - kw;
            }

            return x + x;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void XybLowFreqToVals(Image3<float> xybLf)
        {
            const float xmulScalar    = 33.832837186260f;
            const float ymulScalar    = 14.458268100570f;
            const float bmulScalar    = 49.87984651440f;
            const float yToBMulScalar = -0.362267051518f;

            for (nuint y = 0; y < xybLf.YSize; ++y)
            {
                Span<float> rowX = xybLf.PlaneRow(0, y);
                Span<float> rowY = xybLf.PlaneRow(1, y);
                Span<float> rowB = xybLf.PlaneRow(2, y);

                for (nuint x = 0; x < xybLf.XSize; ++x)
                {
                    int ix    = (int)x;
                    float vx  = rowX[ix];
                    float vy  = rowY[ix];
                    float vb  = rowB[ix];
                    float b   = (yToBMulScalar * vy) + vb;
                    rowB[ix]  = b * bmulScalar;
                    rowX[ix]  = vx * xmulScalar;
                    rowY[ix]  = vy * ymulScalar;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Status SuppressXByY(ImagePlane<float> inY, ImagePlane<float> inoutX)
        {
            if (!ImageOps.SameSize(inoutX, inY))
            {
                return Status.Failure("Size mismatch in SuppressXByY");
            }

            const float suppress  = 46.0f;
            const float s         = 0.653020556257f;
            const float oneMinusS = 1.0f - s;

            for (nuint y = 0; y < inY.YSize; ++y)
            {
                ReadOnlySpan<float> rowY = inY.ConstRow(y);
                Span<float> rowX         = inoutX.Row(y);

                for (nuint x = 0; x < inY.XSize; ++x)
                {
                    int ix       = (int)x;
                    float vx     = rowX[ix];
                    float vy     = rowY[ix];
                    float scaler = ((suppress / ((vy * vy) + suppress)) * oneMinusS) + s;
                    rowX[ix]     = scaler * vx;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Subtract(ImagePlane<float> a, ImagePlane<float> b, ImagePlane<float> c)
        {
            for (nuint y = 0; y < a.YSize; ++y)
            {
                ReadOnlySpan<float> rowA = a.ConstRow(y);
                ReadOnlySpan<float> rowB = b.ConstRow(y);
                Span<float> rowC         = c.Row(y);

                for (nuint x = 0; x < a.XSize; ++x)
                {
                    int ix   = (int)x;
                    rowC[ix] = rowA[ix] - rowB[ix];
                }
            }
        }

        private static Status SeparateLFAndMF(
            in ButteraugliParams baParams,
            Image3<float> xyb,
            Image3<float> lf,
            Image3<float> mf,
            BlurTemp blurTemp)
        {
            const float kSigmaLf = 7.15593339443f;

            for (nuint i = 0; i < 3; ++i)
            {
                if (!Blur(xyb.Plane(i), kSigmaLf, in baParams, blurTemp, lf.Plane(i)))
                {
                    return false;
                }

                Subtract(xyb.Plane(i), lf.Plane(i), mf.Plane(i));
            }

            XybLowFreqToVals(lf);

            return true;
        }

        private static Status SeparateMFAndHF(
            in ButteraugliParams baParams,
            Image3<float> mf,
            ImagePlane<float>[] hf,
            BlurTemp blurTemp)
        {
            const float kSigmaHf       = 3.22489901262f;
            const float kRemoveMfRange = 0.29f;
            const float kAddMfRange    = 0.1f;

            nuint xsize                     = mf.XSize;
            nuint ysize                     = mf.YSize;
            JxlMemoryManager? memoryManager = mf.MemoryManager;

            for (int i = 0; i < 2; ++i)
            {
                hf[i].Dispose();
                StatusOr<ImagePlane<float>> hfOr = ImagePlane<float>.Create(memoryManager, xsize, ysize);

                if (!hfOr.IsOk)
                {
                    return hfOr.Status;
                }

                hf[i] = hfOr.Value;
            }

            for (nuint i = 0; i < 3; ++i)
            {
                if (i == 2)
                {
                    return Blur(mf.Plane(2), kSigmaHf, in baParams, blurTemp, mf.Plane(2));
                }

                ImageOps.CopyImageTo(mf.Plane(i), hf[i]);

                if (!Blur(mf.Plane(i), kSigmaHf, in baParams, blurTemp, mf.Plane(i)))
                {
                    return false;
                }

                AdjustMfAndHfPlane(mf.Plane(i), hf[i], i == 0, kRemoveMfRange, kAddMfRange);
            }

            return SuppressXByY(hf[1], hf[0]);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void AdjustMfAndHfPlane(
            ImagePlane<float> mfPlane,
            ImagePlane<float> hfPlane,
            bool isXChannel,
            float removeMfRange,
            float addMfRange)
        {
            for (nuint y = 0; y < mfPlane.YSize; ++y)
            {
                Span<float> rowMf = mfPlane.Row(y);
                Span<float> rowHf = hfPlane.Row(y);

                for (nuint x = 0; x < mfPlane.XSize; ++x)
                {
                    int ix    = (int)x;
                    float mfv = rowMf[ix];
                    float hfv = rowHf[ix] - mfv;
                    mfv       = isXChannel
                        ? RemoveRangeAroundZero(removeMfRange, mfv)
                        : AmplifyRangeAroundZero(addMfRange, mfv);
                    rowMf[ix] = mfv;
                    rowHf[ix] = hfv;
                }
            }
        }

        private static Status SeparateHFAndUHF(
            in ButteraugliParams baParams,
            ImagePlane<float>[] hf,
            ImagePlane<float>[] uhf,
            BlurTemp blurTemp)
        {
            const float kSigmaUhf    = 1.56416327805f;
            nuint xsize              = hf[0].XSize;
            nuint ysize              = hf[0].YSize;
            JxlMemoryManager? memory = hf[0].MemoryManager;

            for (int i = 0; i < 2; ++i)
            {
                uhf[i].Dispose();
                StatusOr<ImagePlane<float>> uhfOr = ImagePlane<float>.Create(memory, xsize, ysize);

                if (!uhfOr.IsOk)
                {
                    return uhfOr.Status;
                }

                uhf[i] = uhfOr.Value;
                ImageOps.CopyImageTo(hf[i], uhf[i]);

                if (!Blur(hf[i], kSigmaUhf, in baParams, blurTemp, hf[i]))
                {
                    return false;
                }

                AdjustHfAndUhfPlane(hf[i], uhf[i], i == 0);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void AdjustHfAndUhfPlane(ImagePlane<float> hfPlane, ImagePlane<float> uhfPlane, bool isXChannel)
        {
            const float kRemoveHfRange  = 1.5f;
            const float kAddHfRange     = 0.132f;
            const float kRemoveUhfRange = 0.04f;
            const float kMaxclampHf     = 28.4691806922f;
            const float kMaxclampUhf    = 5.19175294647f;
            const float kMulYHf         = 2.155f;
            const float kMulYUhf        = 2.69313763794f;

            for (nuint y = 0; y < hfPlane.YSize; ++y)
            {
                Span<float> rowUhf = uhfPlane.Row(y);
                Span<float> rowHf  = hfPlane.Row(y);

                for (nuint x = 0; x < hfPlane.XSize; ++x)
                {
                    int ix = (int)x;

                    if (isXChannel)
                    {
                        float hfv  = rowHf[ix];
                        float uhfv = rowUhf[ix] - hfv;
                        rowHf[ix]  = RemoveRangeAroundZero(kRemoveHfRange, hfv);
                        rowUhf[ix] = RemoveRangeAroundZero(kRemoveUhfRange, uhfv);
                    }
                    else
                    {
                        float hfv  = MaximumClamp(rowHf[ix], kMaxclampHf);
                        float uhfv = MaximumClamp(rowUhf[ix] - hfv, kMaxclampUhf) * kMulYUhf;
                        rowUhf[ix] = uhfv;
                        rowHf[ix]  = AmplifyRangeAroundZero(kAddHfRange, hfv * kMulYHf);
                    }
                }
            }
        }

        private static void DeallocateHFAndUHF(ImagePlane<float>[] hf, ImagePlane<float>[] uhf)
        {
            for (int i = 0; i < 2; ++i)
            {
                hf[i].Dispose();
                hf[i] = new ImagePlane<float>();
                uhf[i].Dispose();
                uhf[i] = new ImagePlane<float>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MaltaUnitLF(ReadOnlySpan<float> d, int centerIdx, int xs)
        {
            int xs2 = 2 * xs;
            int xs3 = 3 * xs;
            int xs4 = 4 * xs;
            float c = d[centerIdx];

            float s0  = d[centerIdx - 4] + d[centerIdx - 2] + c + d[centerIdx + 2] + d[centerIdx + 4];
            float s1  = d[centerIdx - xs4] + d[centerIdx - xs2] + c + d[centerIdx + xs2] + d[centerIdx + xs4];
            float s2  = d[centerIdx - xs3 - 3] + d[centerIdx - xs2 - 2] + c + d[centerIdx + xs2 + 2] + d[centerIdx + xs3 + 3];
            float s3  = d[centerIdx - xs3 + 3] + d[centerIdx - xs2 + 2] + c + d[centerIdx + xs2 - 2] + d[centerIdx + xs3 - 3];
            float s4  = d[centerIdx - xs4 + 1] + d[centerIdx - xs2 + 1] + c + d[centerIdx + xs2 - 1] + d[centerIdx + xs4 - 1];
            float s5  = d[centerIdx - xs4 - 1] + d[centerIdx - xs2 - 1] + c + d[centerIdx + xs2 + 1] + d[centerIdx + xs4 + 1];
            float s6  = d[centerIdx - 4 - xs] + d[centerIdx - 2 - xs] + c + d[centerIdx + 2 + xs] + d[centerIdx + 4 + xs];
            float s7  = d[centerIdx - 4 + xs] + d[centerIdx - 2 + xs] + c + d[centerIdx + 2 - xs] + d[centerIdx + 4 - xs];
            float s8  = d[centerIdx - xs3 - 2] + d[centerIdx - xs2 - 1] + c + d[centerIdx + xs2 + 1] + d[centerIdx + xs3 + 2];
            float s9  = d[centerIdx - xs3 + 2] + d[centerIdx - xs2 + 1] + c + d[centerIdx + xs2 - 1] + d[centerIdx + xs3 - 2];
            float s10 = d[centerIdx - xs2 - 3] + d[centerIdx - xs - 2] + c + d[centerIdx + xs + 2] + d[centerIdx + xs2 + 3];
            float s11 = d[centerIdx - xs2 + 3] + d[centerIdx - xs + 2] + c + d[centerIdx + xs - 2] + d[centerIdx + xs2 - 3];
            float s12 = d[centerIdx + xs2 - 4] + d[centerIdx + xs - 2] + c + d[centerIdx - xs + 2] + d[centerIdx - xs2 + 4];
            float s13 = d[centerIdx - xs2 - 4] + d[centerIdx - xs - 2] + c + d[centerIdx + xs + 2] + d[centerIdx + xs2 + 4];
            float s14 = d[centerIdx - xs4 - 2] + d[centerIdx - xs2 - 1] + c + d[centerIdx + xs2 + 1] + d[centerIdx + xs4 + 2];
            float s15 = d[centerIdx - xs4 + 2] + d[centerIdx - xs2 + 1] + c + d[centerIdx + xs2 - 1] + d[centerIdx + xs4 - 2];

            return (s0 * s0) + (s1 * s1) + (s2 * s2) + (s3 * s3) +
                   (s4 * s4) + (s5 * s5) + (s6 * s6) + (s7 * s7) +
                   (s8 * s8) + (s9 * s9) + (s10 * s10) + (s11 * s11) +
                   (s12 * s12) + (s13 * s13) + (s14 * s14) + (s15 * s15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MaltaUnitHF(ReadOnlySpan<float> d, int centerIdx, int xs)
        {
            int xs2 = 2 * xs;
            int xs3 = 3 * xs;
            int xs4 = 4 * xs;
            float c = d[centerIdx];

            float s0  = d[centerIdx - 4] + d[centerIdx - 3] + d[centerIdx - 2] + d[centerIdx - 1] + c + d[centerIdx + 1] + d[centerIdx + 2] + d[centerIdx + 3] + d[centerIdx + 4];
            float s1  = d[centerIdx - xs4] + d[centerIdx - xs3] + d[centerIdx - xs2] + d[centerIdx - xs] + c + d[centerIdx + xs] + d[centerIdx + xs2] + d[centerIdx + xs3] + d[centerIdx + xs4];
            float s2  = d[centerIdx - xs3 - 3] + d[centerIdx - xs2 - 2] + d[centerIdx - xs - 1] + c + d[centerIdx + xs + 1] + d[centerIdx + xs2 + 2] + d[centerIdx + xs3 + 3];
            float s3  = d[centerIdx - xs3 + 3] + d[centerIdx - xs2 + 2] + d[centerIdx - xs + 1] + c + d[centerIdx + xs - 1] + d[centerIdx + xs2 - 2] + d[centerIdx + xs3 - 3];
            float s4  = d[centerIdx - xs4 + 1] + d[centerIdx - xs3 + 1] + d[centerIdx - xs2 + 1] + d[centerIdx - xs] + c + d[centerIdx + xs] + d[centerIdx + xs2 - 1] + d[centerIdx + xs3 - 1] + d[centerIdx + xs4 - 1];
            float s5  = d[centerIdx - xs4 - 1] + d[centerIdx - xs3 - 1] + d[centerIdx - xs2 - 1] + d[centerIdx - xs] + c + d[centerIdx + xs] + d[centerIdx + xs2 + 1] + d[centerIdx + xs3 + 1] + d[centerIdx + xs4 + 1];
            float s6  = d[centerIdx - 4 - xs] + d[centerIdx - 3 - xs] + d[centerIdx - 2 - xs] + d[centerIdx - 1] + c + d[centerIdx + 1] + d[centerIdx + 2 + xs] + d[centerIdx + 3 + xs] + d[centerIdx + 4 + xs];
            float s7  = d[centerIdx - 4 + xs] + d[centerIdx - 3 + xs] + d[centerIdx - 2 + xs] + d[centerIdx - 1] + c + d[centerIdx + 1] + d[centerIdx + 2 - xs] + d[centerIdx + 3 - xs] + d[centerIdx + 4 - xs];
            float s8  = d[centerIdx - xs3 - 2] + d[centerIdx - xs2 - 1] + d[centerIdx - xs - 1] + c + d[centerIdx + xs + 1] + d[centerIdx + xs2 + 1] + d[centerIdx + xs3 + 2];
            float s9  = d[centerIdx - xs3 + 2] + d[centerIdx - xs2 + 1] + d[centerIdx - xs + 1] + c + d[centerIdx + xs - 1] + d[centerIdx + xs2 - 1] + d[centerIdx + xs3 - 2];
            float s10 = d[centerIdx - xs2 - 3] + d[centerIdx - xs - 2] + d[centerIdx - xs - 1] + c + d[centerIdx + xs + 1] + d[centerIdx + xs + 2] + d[centerIdx + xs2 + 3];
            float s11 = d[centerIdx - xs2 + 3] + d[centerIdx - xs + 2] + d[centerIdx - xs + 1] + c + d[centerIdx + xs - 1] + d[centerIdx + xs - 2] + d[centerIdx + xs2 - 3];
            float s12 = d[centerIdx + xs - 4] + d[centerIdx + xs - 3] + d[centerIdx + xs - 2] + d[centerIdx - 1] + c + d[centerIdx + 1] + d[centerIdx - xs + 2] + d[centerIdx - xs + 3] + d[centerIdx - xs + 4];
            float s13 = d[centerIdx - xs - 4] + d[centerIdx - xs - 3] + d[centerIdx - xs - 2] + d[centerIdx - 1] + c + d[centerIdx + 1] + d[centerIdx + xs + 2] + d[centerIdx + xs + 3] + d[centerIdx + xs + 4];
            float s14 = s5;
            float s15 = s4;

            return (s0 * s0) + (s1 * s1) + (s2 * s2) + (s3 * s3) +
                   (s4 * s4) + (s5 * s5) + (s6 * s6) + (s7 * s7) +
                   (s8 * s8) + (s9 * s9) + (s10 * s10) + (s11 * s11) +
                   (s12 * s12) + (s13 * s13) + (s14 * s14) + (s15 * s15);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float PaddedMaltaUnit(bool isLf, ImagePlane<float> diffs, nuint x0, nuint y0)
        {
            if (x0 >= 4 && y0 >= 4 && x0 < (diffs.XSize - 4) && y0 < (diffs.YSize - 4))
            {
                int centerIdx = diffs.RowOffset(y0) + (int)x0;
                int stride    = (int)diffs.PixelsPerRow;

                return isLf
                    ? MaltaUnitLF(diffs.BackingArray, centerIdx, stride)
                    : MaltaUnitHF(diffs.BackingArray, centerIdx, stride);
            }

            Span<float> borderimage = stackalloc float[12 * 9];
            borderimage.Clear();

            for (int dy = 0; dy < 9; ++dy)
            {
                nint y = (nint)y0 + dy - 4;

                if (y < 0 || (nuint)y >= diffs.YSize)
                {
                    continue;
                }

                ReadOnlySpan<float> rowDiffs = diffs.ConstRow((nuint)y);

                for (int dx = 0; dx < 9; ++dx)
                {
                    nint x = (nint)x0 + dx - 4;

                    if (x >= 0 && (nuint)x < diffs.XSize)
                    {
                        borderimage[(dy * 12) + dx] = rowDiffs[(int)x];
                    }
                }
            }

            const int center = (4 * 12) + 4;

            return isLf ? MaltaUnitLF(borderimage, center, 12) : MaltaUnitHF(borderimage, center, 12);
        }

        private static Status MaltaDiffMapImpl(
            bool isLf,
            ImagePlane<float> lum0,
            ImagePlane<float> lum1,
            (double W0Gt1, double W0Lt1, double Norm1, double Len, double Mulli) weights,
            ImagePlane<float> diffs,
            ImagePlane<float> blockDiffAc)
        {
            if (!ImageOps.SameSize(lum0, lum1) || !ImageOps.SameSize(lum0, diffs))
            {
                return Status.Failure("MaltaDiffMap size mismatch");
            }

            const double kWeight0 = 0.5;
            const double kWeight1 = 0.33;

            double wPre0Gt1  = weights.Mulli * Math.Sqrt(kWeight0 * weights.W0Gt1) / ((weights.Len * 2.0) + 1.0);
            double wPre0Lt1  = weights.Mulli * Math.Sqrt(kWeight1 * weights.W0Lt1) / ((weights.Len * 2.0) + 1.0);
            float norm20Gt1  = (float)(wPre0Gt1 * weights.Norm1);
            float norm20Lt1  = (float)(wPre0Lt1 * weights.Norm1);
            float norm1Float = (float)weights.Norm1;

            PrecomputeMaltaDiffs(lum0, lum1, norm1Float, norm20Gt1, norm20Lt1, diffs);
            AccumulateMaltaUnits(isLf, diffs, blockDiffAc);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void PrecomputeMaltaDiffs(
            ImagePlane<float> lum0,
            ImagePlane<float> lum1,
            float norm1,
            float norm20Gt1,
            float norm20Lt1,
            ImagePlane<float> diffs)
        {
            for (nuint y = 0; y < lum0.YSize; ++y)
            {
                ReadOnlySpan<float> row0 = lum0.ConstRow(y);
                ReadOnlySpan<float> row1 = lum1.ConstRow(y);
                Span<float> rowDiffs     = diffs.Row(y);

                for (nuint x = 0; x < lum0.XSize; ++x)
                {
                    int ix       = (int)x;
                    rowDiffs[ix] = ComputeSingleMaltaDiff(row0[ix], row1[ix], norm1, norm20Gt1, norm20Lt1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeSingleMaltaDiff(
            float v0,
            float v1,
            float norm1,
            float norm20Gt1,
            float norm20Lt1)
        {
            float absval   = 0.5f * (MathF.Abs(v0) + MathF.Abs(v1));
            float diff     = v0 - v1;
            float invDenom = 1.0f / (norm1 + absval);
            float result   = norm20Gt1 * invDenom * diff;
            float scaler2  = norm20Lt1 * invDenom;
            double fabs0   = Math.Abs(v0);
            double tooSm   = 0.55 * fabs0;
            double tooBg   = 1.05 * fabs0;

            if (v0 < 0.0f)
            {
                if (v1 > -tooSm)
                {
                    result -= (float)(scaler2 * (v1 + tooSm));
                }
                else if (v1 < -tooBg)
                {
                    result += (float)(scaler2 * (-v1 - tooBg));
                }
            }
            else
            {
                if (v1 < tooSm)
                {
                    result += (float)(scaler2 * (tooSm - v1));
                }
                else if (v1 > tooBg)
                {
                    result -= (float)(scaler2 * (v1 - tooBg));
                }
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void AccumulateMaltaUnits(bool isLf, ImagePlane<float> diffs, ImagePlane<float> blockDiffAc)
        {
            nuint xsize = diffs.XSize;
            nuint ysize = diffs.YSize;

            for (nuint y0 = 0; y0 < ysize; ++y0)
            {
                Span<float> rowDiff = blockDiffAc.Row(y0);

                for (nuint x0 = 0; x0 < xsize; ++x0)
                {
                    rowDiff[(int)x0] += PaddedMaltaUnit(isLf, diffs, x0, y0);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void CombineChannelsForMasking(
            ImagePlane<float>[] hf,
            ImagePlane<float>[] uhf,
            ImagePlane<float> outPlane)
        {
            const float mul0 = 2.5f;
            const float mul1 = 0.4f;
            const float mul2 = 0.4f;

            for (nuint y = 0; y < hf[0].YSize; ++y)
            {
                ReadOnlySpan<float> rowYHf  = hf[1].ConstRow(y);
                ReadOnlySpan<float> rowYUhf = uhf[1].ConstRow(y);
                ReadOnlySpan<float> rowXHf  = hf[0].ConstRow(y);
                ReadOnlySpan<float> rowXUhf = uhf[0].ConstRow(y);
                Span<float> rowOut          = outPlane.Row(y);

                for (nuint x = 0; x < hf[0].XSize; ++x)
                {
                    int ix      = (int)x;
                    float xdiff = (rowXUhf[ix] + rowXHf[ix]) * mul0;
                    float ydiff = (rowYUhf[ix] * mul1) + (rowYHf[ix] * mul2);
                    rowOut[ix]  = MathF.Sqrt((xdiff * xdiff) + (ydiff * ydiff));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void DiffPrecompute(ImagePlane<float> xyb, float mul, float biasArg, ImagePlane<float> outPlane)
        {
            float bias     = mul * biasArg;
            float sqrtBias = MathF.Sqrt(bias);

            for (nuint y = 0; y < xyb.YSize; ++y)
            {
                ReadOnlySpan<float> rowIn = xyb.ConstRow(y);
                Span<float> rowOut        = outPlane.Row(y);

                for (nuint x = 0; x < xyb.XSize; ++x)
                {
                    int ix     = (int)x;
                    rowOut[ix] = MathF.Sqrt((mul * MathF.Abs(rowIn[ix])) + bias) - sqrtBias;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreMin3(float v, ref float min0, ref float min1, ref float min2)
        {
            if (v < min2)
            {
                if (v < min0)
                {
                    min2 = min1;
                    min1 = min0;
                    min0 = v;
                }
                else if (v < min1)
                {
                    min2 = min1;
                    min1 = v;
                }
                else
                {
                    min2 = v;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void FuzzyErosion(ImagePlane<float> fromPlane, ImagePlane<float> toPlane)
        {
            nuint xsize = fromPlane.XSize;
            nuint ysize = fromPlane.YSize;

            for (nuint y = 0; y < ysize; ++y)
            {
                Span<float> rowTo = toPlane.Row(y);

                for (nuint x = 0; x < xsize; ++x)
                {
                    rowTo[(int)x] = FuzzyErodePixel(fromPlane, x, y, xsize, ysize);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FuzzyErodePixel(ImagePlane<float> fromPlane, nuint x, nuint y, nuint xsize, nuint ysize)
        {
            const nuint kStep = 3;
            int ix            = (int)x;
            float min0        = fromPlane.ConstRow(y)[ix];
            float min1        = 2.0f * min0;
            float min2        = min1;

            if (x >= kStep)
            {
                GatherVerticalMin3(fromPlane, x - kStep, y, ysize, kStep, ref min0, ref min1, ref min2);
            }

            if (x + kStep < xsize)
            {
                GatherVerticalMin3(fromPlane, x + kStep, y, ysize, kStep, ref min0, ref min1, ref min2);
            }

            if (y >= kStep)
            {
                StoreMin3(fromPlane.ConstRow(y - kStep)[ix], ref min0, ref min1, ref min2);
            }

            if (y + kStep < ysize)
            {
                StoreMin3(fromPlane.ConstRow(y + kStep)[ix], ref min0, ref min1, ref min2);
            }

            return (0.45f * min0) + (0.3f * min1) + (0.25f * min2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GatherVerticalMin3(
            ImagePlane<float> fromPlane,
            nuint xCol,
            nuint y,
            nuint ysize,
            nuint kStep,
            ref float min0,
            ref float min1,
            ref float min2)
        {
            int ixCol = (int)xCol;
            StoreMin3(fromPlane.ConstRow(y)[ixCol], ref min0, ref min1, ref min2);

            if (y >= kStep)
            {
                StoreMin3(fromPlane.ConstRow(y - kStep)[ixCol], ref min0, ref min1, ref min2);
            }

            if (y + kStep < ysize)
            {
                StoreMin3(fromPlane.ConstRow(y + kStep)[ixCol], ref min0, ref min1, ref min2);
            }
        }

        private static Status ComputeMask(
            ImagePlane<float> mask0,
            ImagePlane<float> mask1,
            in ButteraugliParams baParams,
            BlurTemp blurTemp,
            out ImagePlane<float> mask,
            ImagePlane<float>? diffAc)
        {
            mask                            = new ImagePlane<float>();
            nuint xsize                     = mask0.XSize;
            nuint ysize                     = mask0.YSize;
            JxlMemoryManager? memoryManager = mask0.MemoryManager;

            StatusOr<ImagePlane<float>> maskOr = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> d0Or   = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> d1Or   = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> b0Or   = ImagePlane<float>.Create(memoryManager, xsize, ysize);
            StatusOr<ImagePlane<float>> b1Or   = ImagePlane<float>.Create(memoryManager, xsize, ysize);

            if (!maskOr.IsOk || !d0Or.IsOk || !d1Or.IsOk || !b0Or.IsOk || !b1Or.IsOk)
            {
                DisposeIfOk(maskOr);
                DisposeIfOk(d0Or);
                DisposeIfOk(d1Or);
                DisposeIfOk(b0Or);
                DisposeIfOk(b1Or);

                return false;
            }

            mask                             = maskOr.Value;
            using ImagePlane<float> diff0    = d0Or.Value;
            using ImagePlane<float> diff1    = d1Or.Value;
            using ImagePlane<float> blurred0 = b0Or.Value;
            using ImagePlane<float> blurred1 = b1Or.Value;

            const float kMul    = 6.19424080439f;
            const float kBias   = 12.61050594197f;
            const float kRadius = 2.7f;

            DiffPrecompute(mask0, kMul, kBias, diff0);
            DiffPrecompute(mask1, kMul, kBias, diff1);

            if (!Blur(diff0, kRadius, in baParams, blurTemp, blurred0) ||
                !Blur(diff1, kRadius, in baParams, blurTemp, blurred1))
            {
                return false;
            }

            FuzzyErosion(blurred0, diff0);
            StoreMaskAndMaskError(diff0, blurred0, blurred1, mask, diffAc);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void StoreMaskAndMaskError(
            ImagePlane<float> diff0,
            ImagePlane<float> blurred0,
            ImagePlane<float> blurred1,
            ImagePlane<float> mask,
            ImagePlane<float>? diffAc)
        {
            const float kMaskToErrorMul = 10.0f;
            bool hasDiffAc              = diffAc != null;

            for (nuint y = 0; y < mask.YSize; ++y)
            {
                Span<float> rowMask       = mask.Row(y);
                ReadOnlySpan<float> rowD0 = diff0.ConstRow(y);
                ReadOnlySpan<float> rowB0 = blurred0.ConstRow(y);
                ReadOnlySpan<float> rowB1 = blurred1.ConstRow(y);
                Span<float> rowAc         = hasDiffAc ? diffAc!.Row(y) : Span<float>.Empty;

                for (nuint x = 0; x < mask.XSize; ++x)
                {
                    int ix      = (int)x;
                    rowMask[ix] = rowD0[ix];

                    if (hasDiffAc)
                    {
                        float diff = rowB0[ix] - rowB1[ix];
                        rowAc[ix] += kMaskToErrorMul * diff * diff;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double MaskY(double delta)
        {
            const double offset = 0.829591754942;
            const double scaler = 0.451936922203;
            const double mul    = 2.5485944793;
            double c            = mul / ((scaler * delta) + offset);
            double retval       = KGlobalScale * (1.0 + c);

            return retval * retval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double MaskDcY(double delta)
        {
            const double offset = 0.20025578522;
            const double scaler = 3.87449418804;
            const double mul    = 0.505054525019;
            double c            = mul / ((scaler * delta) + offset);
            double retval       = KGlobalScale * (1.0 + c);

            return retval * retval;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void L2Diff(
            ImagePlane<float> i0,
            ImagePlane<float> i1,
            float w,
            ImagePlane<float> diffmap)
        {
            if (w == 0.0f)
            {
                return;
            }

            for (nuint y = 0; y < i0.YSize; ++y)
            {
                ReadOnlySpan<float> row0 = i0.ConstRow(y);
                ReadOnlySpan<float> row1 = i1.ConstRow(y);
                Span<float> rowDiff      = diffmap.Row(y);

                for (nuint x = 0; x < i0.XSize; ++x)
                {
                    int ix       = (int)x;
                    float diff   = row0[ix] - row1[ix];
                    rowDiff[ix] += diff * diff * w;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void SetL2Diff(
            ImagePlane<float> i0,
            ImagePlane<float> i1,
            float w,
            ImagePlane<float> diffmap)
        {
            if (w == 0.0f)
            {
                ImageOps.ZeroFillImage(diffmap);

                return;
            }

            for (nuint y = 0; y < i0.YSize; ++y)
            {
                ReadOnlySpan<float> row0 = i0.ConstRow(y);
                ReadOnlySpan<float> row1 = i1.ConstRow(y);
                Span<float> rowDiff      = diffmap.Row(y);

                for (nuint x = 0; x < i0.XSize; ++x)
                {
                    int ix      = (int)x;
                    float diff  = row0[ix] - row1[ix];
                    rowDiff[ix] = diff * diff * w;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void L2DiffAsymmetric(
            ImagePlane<float> i0,
            ImagePlane<float> i1,
            float w0Gt1,
            float w0Lt1,
            ImagePlane<float> diffmap)
        {
            if (w0Gt1 == 0.0f && w0Lt1 == 0.0f)
            {
                return;
            }

            float vw0Gt1 = w0Gt1 * 0.8f;
            float vw0Lt1 = w0Lt1 * 0.8f;

            for (nuint y = 0; y < i0.YSize; ++y)
            {
                ReadOnlySpan<float> row0 = i0.ConstRow(y);
                ReadOnlySpan<float> row1 = i1.ConstRow(y);
                Span<float> rowDiff      = diffmap.Row(y);

                for (nuint x = 0; x < i0.XSize; ++x)
                {
                    int ix         = (int)x;
                    float val0     = row0[ix];
                    float val1     = row1[ix];
                    float diff     = val0 - val1;
                    float total    = rowDiff[ix] + (diff * diff * vw0Gt1);
                    float fabs0    = MathF.Abs(val0);
                    float tooSmall = 0.4f * fabs0;
                    float tooBig   = fabs0;
                    float v        = ComputeAsymmetricSecondary(val0, val1, tooSmall, tooBig);

                    rowDiff[ix] = total + (vw0Lt1 * v * v);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeAsymmetricSecondary(float val0, float val1, float tooSmall, float tooBig)
        {
            if (val0 < 0.0f)
            {
                if (val1 > -tooSmall)
                {
                    return val1 + tooSmall;
                }

                return val1 < -tooBig ? -val1 - tooBig : 0.0f;
            }

            if (val1 < tooSmall)
            {
                return tooSmall - val1;
            }

            return val1 > tooBig ? val1 - tooBig : 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Gamma(float v)
        {
            const float kRetMul = 19.245013259874995f * Common.kInvLog2e;
            const float kRetAdd = -23.16046239805755f;
            const float kBias   = 9.9710635769299145f;

            if (v < 0.0f)
            {
                v = 0.0f;
            }

            float log = FastMath.FastLog2f(v + kBias);

            return (kRetMul * log) + kRetAdd;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OpsinAbsorbance(
            float in0,
            float in1,
            float in2,
            bool clamp,
            out float out0,
            out float out1,
            out float out2)
        {
            const float mixi0  = 0.29956550340058319f;
            const float mixi1  = 0.63373087833825936f;
            const float mixi2  = 0.077705617820981968f;
            const float mixi3  = 1.7557483643287353f;
            const float mixi4  = 0.22158691104574774f;
            const float mixi5  = 0.69391388044116142f;
            const float mixi6  = 0.0987313588422f;
            const float mixi7  = 1.7557483643287353f;
            const float mixi8  = 0.02f;
            const float mixi9  = 0.02f;
            const float mixi10 = 0.20480129041026129f;
            const float mixi11 = 12.226454707163354f;

            out0 = (mixi0 * in0) + (mixi1 * in1) + (mixi2 * in2) + mixi3;
            out1 = (mixi4 * in0) + (mixi5 * in1) + (mixi6 * in2) + mixi7;
            out2 = (mixi8 * in0) + (mixi9 * in1) + (mixi10 * in2) + mixi11;

            if (clamp)
            {
                out0 = MathF.Max(out0, mixi3);
                out1 = MathF.Max(out1, mixi7);
                out2 = MathF.Max(out2, mixi11);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void OpsinDynamicsRow(
            Image3<float> rgb,
            Image3<float> blurred,
            Image3<float> xyb,
            nuint y,
            float intensityTarget)
        {
            const float minVal = 1e-4f;
            const float min01  = 1.7557483643287353f;
            const float min2   = 12.226454707163354f;

            ReadOnlySpan<float> rowR        = rgb.ConstPlaneRow(0, y);
            ReadOnlySpan<float> rowG        = rgb.ConstPlaneRow(1, y);
            ReadOnlySpan<float> rowB        = rgb.ConstPlaneRow(2, y);
            ReadOnlySpan<float> rowBlurredR = blurred.ConstPlaneRow(0, y);
            ReadOnlySpan<float> rowBlurredG = blurred.ConstPlaneRow(1, y);
            ReadOnlySpan<float> rowBlurredB = blurred.ConstPlaneRow(2, y);
            Span<float> rowOutX             = xyb.PlaneRow(0, y);
            Span<float> rowOutY             = xyb.PlaneRow(1, y);
            Span<float> rowOutB             = xyb.PlaneRow(2, y);

            for (nuint x = 0; x < rgb.XSize; ++x)
            {
                int ix = (int)x;

                OpsinAbsorbance(
                    rowBlurredR[ix] * intensityTarget,
                    rowBlurredG[ix] * intensityTarget,
                    rowBlurredB[ix] * intensityTarget,
                    true,
                    out float pre0,
                    out float pre1,
                    out float pre2);

                pre0 = MathF.Max(pre0, minVal);
                pre1 = MathF.Max(pre1, minVal);
                pre2 = MathF.Max(pre2, minVal);

                float sens0 = MathF.Max(Gamma(pre0) / pre0, minVal);
                float sens1 = MathF.Max(Gamma(pre1) / pre1, minVal);
                float sens2 = MathF.Max(Gamma(pre2) / pre2, minVal);

                OpsinAbsorbance(
                    rowR[ix] * intensityTarget,
                    rowG[ix] * intensityTarget,
                    rowB[ix] * intensityTarget,
                    false,
                    out float cur0,
                    out float cur1,
                    out float cur2);

                cur0 = MathF.Max(cur0 * sens0, min01);
                cur1 = MathF.Max(cur1 * sens1, min01);
                cur2 = MathF.Max(cur2 * sens2, min2);

                rowOutX[ix] = cur0 - cur1;
                rowOutY[ix] = cur0 + cur1;
                rowOutB[ix] = cur2;
            }
        }

        private static void ScoreToRgb(double score, double goodThreshold, double badThreshold, Span<float> rgb)
        {
            const int kTableSize = 12;

            if (score < goodThreshold)
            {
                score = (score / goodThreshold) * 0.3;
            }
            else if (score < badThreshold)
            {
                score = 0.3 + (((score - goodThreshold) / (badThreshold - goodThreshold)) * 0.15);
            }
            else
            {
                score = 0.45 + (((score - badThreshold) / (badThreshold * 12.0)) * 0.5);
            }

            score  = Common.Clamp1(score * (kTableSize - 1), 0.0, kTableSize - 2);
            int ix = Common.Clamp1((int)score, 0, kTableSize - 2);
            double mix = score - ix;

            for (int i = 0; i < 3; ++i)
            {
                double v = (mix * HeatmapColors[ix + 1, i]) + ((1.0 - mix) * HeatmapColors[ix, i]);
                rgb[i]   = (float)Math.Pow(v, 0.5);
            }
        }
    }
}
