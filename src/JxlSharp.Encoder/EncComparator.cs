// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;

namespace JxlSharp.Encoder
{
    /// <summary>
    /// Abstract image similarity comparator interface.
    /// Ported from <c>jxl::Comparator</c> in <c>lib/jxl/enc_comparator.h</c>.
    /// </summary>
    public abstract class Comparator : IDisposable
    {
        /// <summary>
        /// Sets the reference image to compare with.
        /// </summary>
        /// <param name="reference">Reference image bundle.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status SetReferenceImage(ImageBundle reference);

        /// <summary>
        /// Sets the linear sRGB reference image (0..1 range) to compare with.
        /// </summary>
        /// <param name="linearReference">Linear sRGB 3-plane image.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status SetLinearReferenceImage(Image3<float> linearReference);

        /// <summary>
        /// Compares <paramref name="actual"/> with the reference image and computes the localized <paramref name="diffmap"/> and scalar <paramref name="score"/>.
        /// </summary>
        /// <param name="actual">Distorted image bundle.</param>
        /// <param name="diffmap">Optional output difference map (set to <c>null</c> to skip copying out).</param>
        /// <param name="score">Output scalar similarity score.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status CompareWith(
            ImageBundle actual,
            ref ImagePlane<float>? diffmap,
            out float score);

        /// <summary>
        /// Gets the quality score threshold for "good" quality (smaller score = higher quality).
        /// </summary>
        /// <returns>Good quality score threshold.</returns>
        public abstract float GoodQualityScore();

        /// <summary>
        /// Gets the quality score threshold for "bad" quality (smaller score = higher quality).
        /// </summary>
        /// <returns>Bad quality score threshold.</returns>
        public abstract float BadQualityScore();

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }
    }

    /// <summary>
    /// Butteraugli-backed <see cref="Comparator"/> implementation.
    /// Ported from <c>jxl::JxlButteraugliComparator</c> in <c>lib/jxl/enc_butteraugli_comparator.h</c> and <c>enc_butteraugli_comparator.cc</c>.
    /// </summary>
    public sealed class JxlButteraugliComparator : Comparator
    {
        private readonly ButteraugliParams _params;
        private readonly JxlCmsInterface _cms;
        private ButteraugliComparator? _comparator;
        private nuint _xsize;
        private nuint _ysize;

        /// <summary>
        /// Initializes a new instance of the <see cref="JxlButteraugliComparator"/> class.
        /// </summary>
        /// <param name="baParams">Butteraugli parameters.</param>
        /// <param name="cms">Color management system interface.</param>
        public JxlButteraugliComparator(in ButteraugliParams baParams, JxlCmsInterface cms)
        {
            _params = baParams;
            _cms    = cms;
        }

        /// <inheritdoc />
        public override Status SetReferenceImage(ImageBundle reference)
        {
            ImageMetadata metadata  = reference.Metadata ?? new ImageMetadata();
            using ImageBundle store = new ImageBundle(reference.MemoryManager, metadata);

            if (!ImageBundle.TransformIfNeeded(
                reference,
                ColorEncoding.LinearSRGB(reference.IsGray()),
                _cms,
                null,
                store,
                out ImageBundle linear))
            {
                return false;
            }

            _comparator?.Dispose();
            _comparator = null;

            StatusOr<ButteraugliComparator> compOr = ButteraugliComparator.Make(linear.Color(), in _params);

            if (!compOr.IsOk)
            {
                return compOr.Status;
            }

            _comparator = compOr.Value;
            _xsize      = reference.XSize();
            _ysize      = reference.YSize();

            return true;
        }

        /// <inheritdoc />
        public override Status SetLinearReferenceImage(Image3<float> linearReference)
        {
            _comparator?.Dispose();
            _comparator = null;

            StatusOr<ButteraugliComparator> compOr = ButteraugliComparator.Make(linearReference, in _params);

            if (!compOr.IsOk)
            {
                return compOr.Status;
            }

            _comparator = compOr.Value;
            _xsize      = linearReference.XSize;
            _ysize      = linearReference.YSize;

            return true;
        }

        /// <inheritdoc />
        public override Status CompareWith(
            ImageBundle actual,
            ref ImagePlane<float>? diffmap,
            out float score)
        {
            score = 0.0f;

            if (_comparator == null)
            {
                return Status.Failure("Must SetReferenceImage before calling CompareWith");
            }

            if (actual.XSize() != _xsize || actual.YSize() != _ysize)
            {
                return Status.Failure("Must have same size");
            }

            ImageMetadata metadata  = actual.Metadata ?? new ImageMetadata();
            using ImageBundle store = new ImageBundle(actual.MemoryManager, metadata);

            if (!ImageBundle.TransformIfNeeded(
                actual,
                ColorEncoding.LinearSRGB(actual.IsGray()),
                _cms,
                null,
                store,
                out ImageBundle linear))
            {
                return false;
            }

            StatusOr<ImagePlane<float>> tempDiffOr = ImagePlane<float>.Create(actual.MemoryManager, _xsize, _ysize);

            if (!tempDiffOr.IsOk)
            {
                return tempDiffOr.Status;
            }

            ImagePlane<float> tempDiffmap = tempDiffOr.Value;

            if (!_comparator.Diffmap(linear.Color(), ref tempDiffmap))
            {
                tempDiffmap.Dispose();

                return false;
            }

            score = (float)Butteraugli.ButteraugliScoreFromDiffmap(tempDiffmap, in _params);

            if (diffmap != null)
            {
                diffmap.Dispose();
                diffmap = tempDiffmap;
            }
            else
            {
                tempDiffmap.Dispose();
            }

            return true;
        }

        /// <inheritdoc />
        public override float GoodQualityScore()
        {
            return (float)Butteraugli.ButteraugliFuzzyInverse(1.5);
        }

        /// <inheritdoc />
        public override float BadQualityScore()
        {
            return (float)Butteraugli.ButteraugliFuzzyInverse(0.5);
        }

        /// <summary>
        /// Computes the visual masking map for the reference image.
        /// </summary>
        /// <param name="mask">Output masking image plane.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Mask(out ImagePlane<float> mask)
        {
            if (_comparator == null)
            {
                mask = new ImagePlane<float>();

                return Status.Failure("Must SetReferenceImage before calling Mask");
            }

            return _comparator.Mask(out mask);
        }

        /// <summary>
        /// Compares a pre-computed Opsin-dynamics XYB image against the reference image.
        /// </summary>
        /// <param name="xyb">Input Opsin-dynamics XYB image.</param>
        /// <param name="diffmap">Output difference map.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status DiffmapOpsinDynamicsImage(Image3<float> xyb, out ImagePlane<float> diffmap)
        {
            if (_comparator == null)
            {
                diffmap = new ImagePlane<float>();

                return Status.Failure("Must SetReferenceImage before calling DiffmapOpsinDynamicsImage");
            }

            return _comparator.DiffmapOpsinDynamicsImage(xyb, out diffmap);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            _comparator?.Dispose();
            _comparator = null;
        }
    }

    /// <summary>
    /// Helper functions for computing comparator similarity scores with alpha blending support.
    /// Ported from <c>lib/jxl/enc_comparator.h</c> and <c>lib/jxl/enc_comparator.cc</c>.
    /// </summary>
    public static unsafe class EncComparator
    {
        /// <summary>
        /// Computes the score and optional diffmap between two <see cref="ImageBundle"/> instances,
        /// handling alpha blending over black and white backgrounds when alpha is present.
        /// Ported from <c>jxl::ComputeScore</c> in <c>lib/jxl/enc_comparator.cc</c>.
        /// </summary>
        /// <param name="rgb0">Reference image bundle.</param>
        /// <param name="rgb1">Distorted image bundle.</param>
        /// <param name="comparator">Similarity comparator instance.</param>
        /// <param name="cms">Color management system interface.</param>
        /// <param name="score">Output similarity score.</param>
        /// <param name="diffmap">Optional output difference map.</param>
        /// <param name="options">Optional thread pool and alpha-ignoring flag.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ComputeScore(
            ImageBundle rgb0,
            ImageBundle rgb1,
            Comparator comparator,
            JxlCmsInterface cms,
            out float score,
            ref ImagePlane<float>? diffmap,
            (JxlSharp.Base.ThreadPool? Pool, bool IgnoreAlpha) options = default)
        {
            score = 0.0f;

            ImageMetadata meta0     = rgb0.Metadata ?? new ImageMetadata();
            ImageMetadata meta1     = rgb1.Metadata ?? new ImageMetadata();
            using ImageBundle store0 = new ImageBundle(rgb0.MemoryManager, meta0);
            using ImageBundle store1 = new ImageBundle(rgb1.MemoryManager, meta1);

            if (!ImageBundle.TransformIfNeeded(rgb0, ColorEncoding.SRGB(rgb0.IsGray()), cms, options.Pool, store0, out ImageBundle in0) ||
                !ImageBundle.TransformIfNeeded(rgb1, ColorEncoding.SRGB(rgb1.IsGray()), cms, options.Pool, store1, out ImageBundle in1))
            {
                return false;
            }

            if (options.IgnoreAlpha || (!in0.HasAlpha() && !in1.HasAlpha()))
            {
                if (!comparator.SetReferenceImage(in0))
                {
                    return false;
                }

                return comparator.CompareWith(in1, ref diffmap, out score);
            }

            return ComputeAlphaBlendedScore(in0, in1, comparator, out score, ref diffmap);
        }

        private static Status ComputeAlphaBlendedScore(
            ImageBundle in0,
            ImageBundle in1,
            Comparator comparator,
            out float score,
            ref ImagePlane<float>? diffmap)
        {
            score = 0.0f;

            StatusOr<ImageBundle> bl0Or = in0.Copy();
            StatusOr<ImageBundle> bl1Or = in1.Copy();
            StatusOr<ImageBundle> wh0Or = in0.Copy();
            StatusOr<ImageBundle> wh1Or = in1.Copy();

            if (!bl0Or.IsOk || !bl1Or.IsOk || !wh0Or.IsOk || !wh1Or.IsOk)
            {
                Butteraugli.DisposeIfOk(bl0Or);
                Butteraugli.DisposeIfOk(bl1Or);
                Butteraugli.DisposeIfOk(wh0Or);
                Butteraugli.DisposeIfOk(wh1Or);

                return false;
            }

            using ImageBundle bl0 = bl0Or.Value;
            using ImageBundle bl1 = bl1Or.Value;
            using ImageBundle wh0 = wh0Or.Value;
            using ImageBundle wh1 = wh1Or.Value;

            BlendAlpha(bl0, 0.0f);
            BlendAlpha(bl1, 0.0f);
            BlendAlpha(wh0, 1.0f);
            BlendAlpha(wh1, 1.0f);

            ImagePlane<float>? diffmapBlack = diffmap != null
                ? new ImagePlane<float>()
                : null;
            ImagePlane<float>? diffmapWhite = diffmap != null
                ? new ImagePlane<float>()
                : null;

            try
            {
                if (!comparator.SetReferenceImage(bl0) ||
                    !comparator.CompareWith(bl1, ref diffmapBlack, out float scoreBlack) ||
                    !comparator.SetReferenceImage(wh0) ||
                    !comparator.CompareWith(wh1, ref diffmapWhite, out float scoreWhite))
                {
                    return false;
                }

                score = MathF.Max(scoreBlack, scoreWhite);

                if (diffmap != null && diffmapBlack != null && diffmapWhite != null)
                {
                    TakeMaxPerPixel(diffmapWhite, diffmapBlack);
                    diffmap.Dispose();
                    diffmap      = diffmapBlack;
                    diffmapBlack = null;
                }

                return true;
            }
            finally
            {
                diffmapBlack?.Dispose();
                diffmapWhite?.Dispose();
            }
        }

        private static void BlendAlpha(ImageBundle ib, float background)
        {
            if (!ib.HasAlpha())
            {
                return;
            }

            bool isPremultiplied     = ib.AlphaIsPremultiplied();
            ImagePlane<float>? alpha = ib.Alpha();

            if (alpha == null)
            {
                return;
            }

            for (nuint c = 0; c < 3; ++c)
            {
                BlendChannelAlpha(ib.Color().Plane(c), alpha, background, isPremultiplied);
            }
        }

        private static void BlendChannelAlpha(
            ImagePlane<float> plane,
            ImagePlane<float> alpha,
            float background,
            bool isPremultiplied)
        {
            for (nuint y = 0; y < plane.YSize; ++y)
            {
                float* rowA = alpha.ConstRow(y);
                float* rowI = plane.Row(y);

                for (nuint x = 0; x < plane.XSize; ++x)
                {
                    float a = rowA[x];
                    rowI[x] = isPremultiplied
                        ? rowI[x] + ((1.0f - a) * background)
                        : (a * rowI[x]) + ((1.0f - a) * background);
                }
            }
        }

        private static void TakeMaxPerPixel(ImagePlane<float> inPlane, ImagePlane<float> inoutPlane)
        {
            for (nuint y = 0; y < inPlane.YSize; ++y)
            {
                float* rowIn  = inPlane.ConstRow(y);
                float* rowOut = inoutPlane.Row(y);

                for (nuint x = 0; x < inPlane.XSize; ++x)
                {
                    rowOut[x] = MathF.Max(rowIn[x], rowOut[x]);
                }
            }
        }
    }
}
