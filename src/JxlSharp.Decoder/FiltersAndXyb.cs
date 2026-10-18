// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;
using JxlSharp.Cms;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Edge-preserving filter (EPF) sigma precomputation and mirroring helpers.
    /// Ported from <c>lib/jxl/epf.h</c>, <c>lib/jxl/epf.cc</c>, and <c>lib/jxl/dec_cache.h</c>.
    /// </summary>
    public static unsafe class Epf
    {
        /// <summary><c>4 * (sqrt(0.5) - 1)</c>, so that <c>Weight(sigma) = 0.5</c>.</summary>
        public const float KInvSigmaNum = -1.1715728752538099024f;

        /// <summary><c>KInvSigmaNum / 0.3</c>.</summary>
        public const float KMinSigma = -3.90524291751269967465540850526868f;

        /// <summary>Sigma border width in blocks (1).</summary>
        public const nuint KSigmaBorder = 1;

        /// <summary>Sigma padding offset in blocks (2).</summary>
        public const nuint KSigmaPadding = 2;

        /// <summary>
        /// Mirrors <paramref name="n"/> floats starting at <paramref name="p"/> and stores them immediately before <paramref name="p"/>.
        /// </summary>
        /// <param name="p">Pointer to first element to mirror leftward.</param>
        /// <param name="n">Number of elements to mirror.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LeftMirror(float* p, nuint n)
        {
            for (nuint i = 0; i < n; i++)
            {
                *(p - 1 - (nint)i) = p[i];
            }
        }

        /// <summary>
        /// Mirrors <paramref name="n"/> floats ending before <paramref name="p"/> and stores them starting at <paramref name="p"/>.
        /// </summary>
        /// <param name="p">Pointer to destination start.</param>
        /// <param name="n">Number of elements to mirror.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RightMirror(float* p, nuint n)
        {
            for (nuint i = 0; i < n; i++)
            {
                p[i] = *(p - 1 - (nint)i);
            }
        }

        /// <summary>
        /// Fills the <paramref name="sigmaImg"/> plane with precomputed <c>1 / sigma</c> values in <paramref name="blockRect"/>.
        /// Ported from <c>jxl::ComputeSigma</c> in <c>lib/jxl/epf.cc</c>.
        /// </summary>
        /// <param name="lf">Loop filter parameters.</param>
        /// <param name="blockRect">Group rectangle in 8x8 blocks.</param>
        /// <param name="acStrategy">AC strategy image.</param>
        /// <param name="quantScale">Quantizer global scale (<see cref="Quantizer.Scale"/>).</param>
        /// <param name="rawQuantField">Raw quantization field plane.</param>
        /// <param name="epfSharpness">EPF sharpness LUT index plane.</param>
        /// <param name="sigmaImg">Destination sigma plane (padded by <c>2 * KSigmaPadding</c>).</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status ComputeSigma(
            LoopFilter lf,
            in Rect blockRect,
            AcStrategyImage acStrategy,
            float quantScale,
            ImagePlane<int> rawQuantField,
            ImagePlane<byte> epfSharpness,
            ImagePlane<float> sigmaImg)
        {
            if (lf.EpfIters == 0)
            {
                return Status.Failure("ComputeSigma requires epf_iters > 0");
            }

            nuint xsizeBlocks     = sigmaImg.XSize - (2 * KSigmaPadding);
            nuint ysizeBlocks     = sigmaImg.YSize - (2 * KSigmaPadding);
            nint sigmaStride      = sigmaImg.PixelsPerRow;
            nint sharpnessStride  = epfSharpness.PixelsPerRow;

            for (nuint by = 0; by < (nuint)blockRect.YSize; ++by)
            {
                float* sigmaRow      = blockRect.Row(sigmaImg, by);
                byte* sharpnessRow   = blockRect.ConstRow(epfSharpness, by);
                AcStrategyRow acsRow = acStrategy.ConstRow(blockRect, by);
                int* rowQuant        = blockRect.ConstRow(rawQuantField, by);

                for (nuint bx = 0; bx < (nuint)blockRect.XSize; bx++)
                {
                    AcStrategy acs = acsRow[bx];

                    if (!acs.IsFirstBlock())
                    {
                        continue;
                    }

                    float sigmaQuant = lf.EpfQuantMul / (quantScale * rowQuant[bx] * KInvSigmaNum);
                    FillBlockSigma(lf, acs, bx, sigmaQuant, sharpnessRow, sharpnessStride, sigmaRow, sigmaStride);
                    MirrorBlockSigmaBorders(
                        acs,
                        in blockRect,
                        bx,
                        by,
                        xsizeBlocks,
                        ysizeBlocks,
                        sigmaRow,
                        sigmaStride);
                }
            }

            return true;
        }

        private static void FillBlockSigma(
            LoopFilter lf,
            AcStrategy acs,
            nuint bx,
            float sigmaQuant,
            byte* sharpnessRow,
            nint sharpnessStride,
            float* sigmaRow,
            nint sigmaStride)
        {
            for (nuint iy = 0; iy < acs.CoveredBlocksY(); iy++)
            {
                for (nuint ix = 0; ix < acs.CoveredBlocksX(); ix++)
                {
                    byte sharpIdx = sharpnessRow[(nint)(bx + ix) + ((nint)iy * sharpnessStride)];
                    float sigma   = sigmaQuant * lf.EpfSharpLut[sharpIdx];
                    sigma         = MathF.Min(-1e-4f, sigma);
                    sigmaRow[(nint)(bx + ix + KSigmaPadding) + ((nint)(iy + KSigmaPadding) * sigmaStride)] = 1.0f / sigma;
                }
            }
        }

        private static void MirrorBlockSigmaBorders(
            AcStrategy acs,
            in Rect blockRect,
            nuint bx,
            nuint by,
            nuint xsizeBlocks,
            nuint ysizeBlocks,
            float* sigmaRow,
            nint sigmaStride)
        {
            nuint llfX = acs.CoveredBlocksX();
            nuint llfY = acs.CoveredBlocksY();
            nuint absX = bx + (nuint)blockRect.X0;
            nuint absY = by + (nuint)blockRect.Y0;

            if (absX == 0)
            {
                for (nuint iy = 0; iy < llfY; iy++)
                {
                    LeftMirror(sigmaRow + (nint)KSigmaPadding + ((nint)(iy + KSigmaPadding) * sigmaStride), KSigmaBorder);
                }
            }

            if (absX + llfX == xsizeBlocks)
            {
                for (nuint iy = 0; iy < llfY; iy++)
                {
                    RightMirror(
                        sigmaRow + (nint)(KSigmaPadding + bx + llfX) + ((nint)(iy + KSigmaPadding) * sigmaStride),
                        KSigmaBorder);
                }
            }

            nuint offsetBefore = absX == 0 ? 1 : bx + KSigmaPadding;
            nuint offsetAfter  = absX + llfX == xsizeBlocks
                ? KSigmaPadding + llfX + bx + KSigmaBorder
                : KSigmaPadding + llfX + bx;
            nuint num          = offsetAfter - offsetBefore;

            MirrorSigmaVertical(absY, llfY, ysizeBlocks, offsetBefore, num, sigmaRow, sigmaStride);
        }

        private static void MirrorSigmaVertical(
            nuint absY,
            nuint llfY,
            nuint ysizeBlocks,
            nuint offsetBefore,
            nuint num,
            float* sigmaRow,
            nint sigmaStride)
        {
            long bytesToCopy = (long)(num * sizeof(float));

            if (absY == 0)
            {
                for (nuint iy = 0; iy < KSigmaBorder; iy++)
                {
                    float* dst = sigmaRow + (nint)offsetBefore + ((nint)(KSigmaPadding - 1 - iy) * sigmaStride);
                    float* src = sigmaRow + (nint)offsetBefore + ((nint)(KSigmaPadding + iy) * sigmaStride);
                    Buffer.MemoryCopy(src, dst, bytesToCopy, bytesToCopy);
                }
            }

            if (absY + llfY == ysizeBlocks)
            {
                for (nuint iy = 0; iy < KSigmaBorder; iy++)
                {
                    float* dst = sigmaRow + (nint)offsetBefore + (sigmaStride * (nint)(llfY + KSigmaPadding + iy));
                    float* src = sigmaRow + (nint)offsetBefore + (sigmaStride * (nint)(llfY + KSigmaPadding - 1 - iy));
                    Buffer.MemoryCopy(src, dst, bytesToCopy, bytesToCopy);
                }
            }
        }
    }

    /// <summary>
    /// Color encoding and opsin inverse transform state for frame reconstruction.
    /// Ported from <c>jxl::OutputEncodingInfo</c> in <c>lib/jxl/dec_xyb.h</c> and <c>lib/jxl/dec_xyb.cc</c>.
    /// </summary>
    public sealed class OutputEncodingInfo
    {
        private static readonly Vector3 KSRGBLuminances = new Vector3(0.2126f, 0.7152f, 0.0722f);

        /// <summary>Original color encoding from image metadata.</summary>
        public ColorEncoding OrigColorEncoding = new ColorEncoding();

        /// <summary>Original intensity target from image metadata.</summary>
        public float OrigIntensityTarget;

        /// <summary>Opsin inverse matrix from metadata.</summary>
        public Matrix3x3 OrigInverseMatrix;

        /// <summary>Whether the opsin inverse matrix in metadata is all default.</summary>
        public bool DefaultTransform;

        /// <summary>Whether the codestream is XYB encoded.</summary>
        public bool XybEncoded;

        /// <summary>Requested output color encoding.</summary>
        public ColorEncoding ColorEncoding = new ColorEncoding();

        /// <summary>Linearized version of <see cref="ColorEncoding"/>.</summary>
        public ColorEncoding LinearColorEncoding = new ColorEncoding();

        /// <summary>Whether <see cref="ColorEncoding"/> equals <see cref="OrigColorEncoding"/>.</summary>
        public bool ColorEncodingIsOriginal;

        /// <summary>Opsin parameters converting XYB to the primaries of <see cref="ColorEncoding"/>.</summary>
        public OpsinParams OpsinParams = new OpsinParams();

        /// <summary>Whether <see cref="OpsinParams"/> uses default matrix and 255 intensity target.</summary>
        public bool AllDefaultOpsin;

        /// <summary>Inverse gamma exponent for Gamma and DCI transfer functions.</summary>
        public float InverseGamma;

        /// <summary>Luminances of <see cref="ColorEncoding"/>'s primaries (defaults to sRGB).</summary>
        public Vector3 Luminances = KSRGBLuminances;

        /// <summary>Desired output intensity target for HLG inverse OOTF and PQ tone mapping.</summary>
        public float DesiredIntensityTarget;

        /// <summary>Whether a CMS interface has been configured.</summary>
        public bool CmsSet;

        /// <summary>Configured CMS interface.</summary>
        public JxlCmsInterface ColorManagementSystem = new JxlCmsInterface();

        /// <summary>
        /// Initializes this <see cref="OutputEncodingInfo"/> from <paramref name="metadata"/>.
        /// </summary>
        /// <param name="metadata">Codestream metadata.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status SetFromMetadata(CodecMetadata metadata)
        {
            OrigColorEncoding      = metadata.M.ColorEncoding.Clone();
            OrigIntensityTarget    = metadata.M.IntensityTarget();
            DesiredIntensityTarget = OrigIntensityTarget;

            OpsinInverseMatrix im = metadata.TransformData.OpsinInverseMatrix;
            OrigInverseMatrix     = im.InverseMatrix;
            DefaultTransform      = im.AllDefault;
            XybEncoded            = metadata.M.XybEncoded;

            for (int i = 0; i < 3; ++i)
            {
                OpsinParams.OpsinBiases[i]     = im.OpsinBiases[i];
                OpsinParams.OpsinBiasesCbrt[i] = MathF.Cbrt(OpsinParams.OpsinBiases[i]);
            }

            OpsinParams.OpsinBiases[3]     = 1.0f;
            OpsinParams.OpsinBiasesCbrt[3] = 1.0f;
            Array.Copy(im.QuantBiases, OpsinParams.QuantBiases, 4);

            bool origOk   = DecXyb.CanOutputToColorEncoding(OrigColorEncoding);
            bool origGrey = OrigColorEncoding.IsGray();

            return SetColorEncoding(!XybEncoded || origOk
                ? OrigColorEncoding
                : ColorEncoding.LinearSRGB(origGrey));
        }

        /// <summary>
        /// Updates the target output color encoding if supported without an external CMS.
        /// </summary>
        /// <param name="cDesired">Desired output color encoding.</param>
        /// <returns><see cref="Status.OkStatus"/> if set; otherwise failure.</returns>
        public Status MaybeSetColorEncoding(ColorEncoding cDesired)
        {
            if (cDesired.GetColorSpace() == ColorSpace.XYB &&
                ((ColorEncoding.GetColorSpace() == ColorSpace.RGB &&
                  ColorEncoding.GetPrimariesType() != Primaries.SRGB) ||
                 ColorEncoding.Tf.IsPQ()))
            {
                return false;
            }

            if (!XybEncoded && !DecXyb.CanOutputToColorEncoding(cDesired))
            {
                return false;
            }

            return SetColorEncoding(cDesired);
        }

        private Status SetColorEncoding(ColorEncoding cDesired)
        {
            ColorEncoding = cDesired.Clone();
            LinearColorEncoding = ColorEncoding.Clone();
            LinearColorEncoding.Tf.SetTransferFunction(TransferFunction.Linear);
            ColorEncodingIsOriginal = OrigColorEncoding.SameColorEncoding(cDesired);

            Matrix3x3 inverseMatrix      = OrigInverseMatrix;
            bool inverseMatrixIsDefault  = DefaultTransform;
            Luminances                   = KSRGBLuminances;

            if ((cDesired.GetPrimariesType() != Primaries.SRGB ||
                 cDesired.GetWhitePointType() != WhitePoint.D65) &&
                !cDesired.IsGray())
            {
                if (!ComputeCustomPrimariesMatrix(cDesired, ref inverseMatrix, ref inverseMatrixIsDefault))
                {
                    return false;
                }
            }

            if (cDesired.IsGray())
            {
                Matrix3x3 tmpInvMatrix = inverseMatrix;
                Matrix3x3 srgbToLuma   = new Matrix3x3(Luminances, Luminances, Luminances);
                MatrixOps.Mul3x3Matrix(in srgbToLuma, in tmpInvMatrix, out inverseMatrix);
            }

            if (XybEncoded)
            {
                OpsinParams.InitSIMDInverseMatrix(
                    in inverseMatrix,
                    OpsinParams.InverseOpsinMatrix,
                    OrigIntensityTarget);
                AllDefaultOpsin = MathF.Abs(OrigIntensityTarget - 255.0f) <= 0.1f && inverseMatrixIsDefault;
            }

            ref CustomTransferFunction tf = ref cDesired.Tf;
            InverseGamma                  = tf.HaveGamma
                ? (float)tf.GetGamma()
                : (tf.IsDCI() ? 1.0f / 2.6f : 1.0f);

            return true;
        }

        private Status ComputeCustomPrimariesMatrix(
            ColorEncoding cDesired,
            ref Matrix3x3 inverseMatrix,
            ref bool inverseMatrixIsDefault)
        {
            ColorEncoding srgb = ColorEncoding.SRGB(isGray: false);

            if (!srgb.GetPrimaries(out PrimariesCIExy p))
            {
                return false;
            }

            CIExy w = srgb.GetWhitePoint();

            if (!JxlCmsInternal.PrimariesToXYZD50(p, w, out Matrix3x3 srgbToXyzd50))
            {
                return false;
            }

            if (!cDesired.GetPrimaries(out p))
            {
                return false;
            }

            w = cDesired.GetWhitePoint();

            if (!JxlCmsInternal.PrimariesToXYZ(p, w, out Matrix3x3 originalToXyz))
            {
                return Status.Failure("PrimariesToXYZ failed");
            }

            Luminances = originalToXyz[1];

            if (!XybEncoded)
            {
                return true;
            }

            if (!JxlCmsInternal.AdaptToXYZD50((float)w.X, (float)w.Y, out Matrix3x3 adaptToD50))
            {
                return Status.Failure("AdaptToXYZD50 failed");
            }

            MatrixOps.Mul3x3Matrix(in adaptToD50, in originalToXyz, out Matrix3x3 xyzd50ToOriginal);

            if (!MatrixOps.Inv3x3Matrix(ref xyzd50ToOriginal))
            {
                return false;
            }

            MatrixOps.Mul3x3Matrix(in xyzd50ToOriginal, in srgbToXyzd50, out Matrix3x3 srgbToOriginal);
            MatrixOps.Mul3x3Matrix(in srgbToOriginal, in OrigInverseMatrix, out inverseMatrix);
            inverseMatrixIsDefault = false;

            return true;
        }
    }

    /// <summary>
    /// XYB-to-linear-RGB conversion functions.
    /// Ported from <c>lib/jxl/dec_xyb.h</c>, <c>lib/jxl/dec_xyb-inl.h</c>, and <c>lib/jxl/dec_xyb.cc</c>.
    /// </summary>
    public static unsafe class DecXyb
    {
        /// <summary>
        /// Inverts the pixel-wise RGB-to-XYB conversion for a single pixel.
        /// </summary>
        /// <param name="opsinX">Opsin X sample.</param>
        /// <param name="opsinY">Opsin Y sample.</param>
        /// <param name="opsinB">Opsin B sample.</param>
        /// <param name="opsinParams">Opsin parameters.</param>
        /// <param name="linearR">Output linear R sample.</param>
        /// <param name="linearG">Output linear G sample.</param>
        /// <param name="linearB">Output linear B sample.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void XybToRgbScalar(
            float opsinX,
            float opsinY,
            float opsinB,
            OpsinParams opsinParams,
            out float linearR,
            out float linearG,
            out float linearB)
        {
            float gammaR = (opsinY + opsinX) - opsinParams.OpsinBiasesCbrt[0];
            float gammaG = (opsinY - opsinX) - opsinParams.OpsinBiasesCbrt[1];
            float gammaB = opsinB - opsinParams.OpsinBiasesCbrt[2];

            float mixedR = (gammaR * gammaR * gammaR) + opsinParams.OpsinBiases[0];
            float mixedG = (gammaG * gammaG * gammaG) + opsinParams.OpsinBiases[1];
            float mixedB = (gammaB * gammaB * gammaB) + opsinParams.OpsinBiases[2];

            float[] m = opsinParams.InverseOpsinMatrix;
            linearR   = (m[0 * 4] * mixedR) + (m[1 * 4] * mixedG) + (m[2 * 4] * mixedB);
            linearG   = (m[3 * 4] * mixedR) + (m[4 * 4] * mixedG) + (m[5 * 4] * mixedB);
            linearB   = (m[6 * 4] * mixedR) + (m[7 * 4] * mixedG) + (m[8 * 4] * mixedB);
        }

        /// <summary>
        /// Inverts the pixel-wise RGB-to-XYB conversion for 4 pixels in parallel using <see cref="Vector128{Single}"/>.
        /// Ported from <c>jxl::HWY_NAMESPACE::XybToRgb</c> in <c>lib/jxl/dec_xyb-inl.h</c>.
        /// </summary>
        /// <param name="opsinX">4 opsin X samples.</param>
        /// <param name="opsinY">4 opsin Y samples.</param>
        /// <param name="opsinB">4 opsin B samples.</param>
        /// <param name="opsinParams">Opsin parameters.</param>
        /// <param name="linearR">Output 4 linear R samples.</param>
        /// <param name="linearG">Output 4 linear G samples.</param>
        /// <param name="linearB">Output 4 linear B samples.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void XybToRgb(
            Vector128<float> opsinX,
            Vector128<float> opsinY,
            Vector128<float> opsinB,
            OpsinParams opsinParams,
            out Vector128<float> linearR,
            out Vector128<float> linearG,
            out Vector128<float> linearB)
        {
            Vector128<float> negBiasR = Vector128.Create(opsinParams.OpsinBiases[0]);
            Vector128<float> negBiasG = Vector128.Create(opsinParams.OpsinBiases[1]);
            Vector128<float> negBiasB = Vector128.Create(opsinParams.OpsinBiases[2]);

            Vector128<float> gammaR = (opsinY + opsinX) - Vector128.Create(opsinParams.OpsinBiasesCbrt[0]);
            Vector128<float> gammaG = (opsinY - opsinX) - Vector128.Create(opsinParams.OpsinBiasesCbrt[1]);
            Vector128<float> gammaB = opsinB - Vector128.Create(opsinParams.OpsinBiasesCbrt[2]);

            Vector128<float> mixedR = (gammaR * gammaR * gammaR) + negBiasR;
            Vector128<float> mixedG = (gammaG * gammaG * gammaG) + negBiasG;
            Vector128<float> mixedB = (gammaB * gammaB * gammaB) + negBiasB;

            float[] m = opsinParams.InverseOpsinMatrix;
            linearR   = (Vector128.Create(m[0 * 4]) * mixedR) +
                        (Vector128.Create(m[1 * 4]) * mixedG) +
                        (Vector128.Create(m[2 * 4]) * mixedB);
            linearG   = (Vector128.Create(m[3 * 4]) * mixedR) +
                        (Vector128.Create(m[4 * 4]) * mixedG) +
                        (Vector128.Create(m[5 * 4]) * mixedB);
            linearB   = (Vector128.Create(m[6 * 4]) * mixedR) +
                        (Vector128.Create(m[7 * 4]) * mixedG) +
                        (Vector128.Create(m[8 * 4]) * mixedB);
        }

        /// <summary>
        /// Converts <paramref name="rect"/> of <paramref name="opsin"/> from XYB to linear RGB in <paramref name="linear"/>.
        /// Ported from <c>jxl::OpsinToLinear</c> in <c>lib/jxl/dec_xyb.cc</c>.
        /// </summary>
        /// <param name="opsin">Source XYB 3-plane float image.</param>
        /// <param name="rect">Rectangle within <paramref name="opsin"/>.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <param name="linear">Destination linear RGB 3-plane float image.</param>
        /// <param name="opsinParams">Opsin conversion parameters.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status OpsinToLinear(
            Image3<float> opsin,
            in Rect rect,
            ThreadPool? pool,
            Image3<float> linear,
            OpsinParams opsinParams)
        {
            if ((nuint)rect.XSize != linear.XSize || (nuint)rect.YSize != linear.YSize)
            {
                return Status.Failure("OpsinToLinear size mismatch");
            }

            Rect localRect = rect;

            return DataParallel.RunOnPool(
                pool,
                0,
                (uint)localRect.YSize,
                ThreadPool.NoInit,
                (uint task, nuint _) =>
                {
                    ConvertOpsinRow(opsin, in localRect, linear, opsinParams, (int)task);

                    return true;
                },
                "OpsinToLinear(Rect)");
        }

        /// <summary>
        /// Returns whether the built-in pipeline can directly output to <paramref name="cDesired"/> without an external CMS.
        /// </summary>
        /// <param name="cDesired">Desired output color encoding.</param>
        /// <returns><c>true</c> if supported.</returns>
        public static bool CanOutputToColorEncoding(ColorEncoding cDesired)
        {
            if (!cDesired.HaveFields())
            {
                return false;
            }

            ref CustomTransferFunction tf = ref cDesired.Tf;

            if (!tf.IsPQ() &&
                !tf.IsSRGB() &&
                !tf.HaveGamma &&
                !tf.IsLinear() &&
                !tf.IsHLG() &&
                !tf.IsDCI() &&
                !tf.Is709())
            {
                return false;
            }

            if (cDesired.IsGray() && cDesired.GetWhitePointType() != WhitePoint.D65)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns whether the platform-specific int16 NEON fast XYB-to-sRGB8 path is enabled.
        /// </summary>
        /// <returns><c>false</c> on non-NEON targets.</returns>
        public static bool HasFastXYBTosRGB8() => false;

        /// <summary>
        /// Converts XYB float rows (+ optional alpha) directly to 8-bit sRGB / sRGBA output.
        /// </summary>
        /// <param name="rowX">Pointer to X row.</param>
        /// <param name="rowY">Pointer to Y row.</param>
        /// <param name="rowB">Pointer to B row.</param>
        /// <param name="rowA">Optional pointer to alpha row (may be <c>null</c>).</param>
        /// <param name="output">Pointer to output byte buffer.</param>
        /// <param name="isRgba">Whether output is 4-channel RGBA (<c>true</c>) or 3-channel RGB (<c>false</c>).</param>
        /// <param name="xsize">Number of pixels in the row.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status FastXYBTosRGB8(
            float* rowX,
            float* rowY,
            float* rowB,
            float* rowA,
            byte* output,
            bool isRgba,
            nuint xsize)
        {
            OpsinParams opsinParams = new OpsinParams();
            opsinParams.Init(255.0f);

            for (nuint x = 0; x < xsize; x++)
            {
                XybToRgbScalar(rowX[x], rowY[x], rowB[x], opsinParams, out float r, out float g, out float b);
                byte r8 = LinearToSrgb8(r);
                byte g8 = LinearToSrgb8(g);
                byte b8 = LinearToSrgb8(b);

                if (isRgba)
                {
                    float a        = rowA != null ? Math.Clamp(rowA[x], 0.0f, 1.0f) : 1.0f;
                    byte* dst      = output + (4 * x);
                    dst[0]         = r8;
                    dst[1]         = g8;
                    dst[2]         = b8;
                    dst[3]         = (byte)MathF.Round(a * 255.0f);
                }
                else
                {
                    byte* dst = output + (3 * x);
                    dst[0]    = r8;
                    dst[1]    = g8;
                    dst[2]    = b8;
                }
            }

            return true;
        }

        private static void ConvertOpsinRow(
            Image3<float> opsin,
            in Rect rect,
            Image3<float> linear,
            OpsinParams opsinParams,
            int y)
        {
            float* rowOpsin0  = rect.ConstPlaneRow(opsin, 0, (nuint)y);
            float* rowOpsin1  = rect.ConstPlaneRow(opsin, 1, (nuint)y);
            float* rowOpsin2  = rect.ConstPlaneRow(opsin, 2, (nuint)y);
            float* rowLinear0 = linear.PlaneRow(0, (nuint)y);
            float* rowLinear1 = linear.PlaneRow(1, (nuint)y);
            float* rowLinear2 = linear.PlaneRow(2, (nuint)y);
            int xsize         = rect.XSize;
            int x             = 0;

            for (; x + 4 <= xsize; x += 4)
            {
                Vector128<float> inX = Vector128.Load(rowOpsin0 + x);
                Vector128<float> inY = Vector128.Load(rowOpsin1 + x);
                Vector128<float> inB = Vector128.Load(rowOpsin2 + x);
                XybToRgb(
                    inX,
                    inY,
                    inB,
                    opsinParams,
                    out Vector128<float> linR,
                    out Vector128<float> linG,
                    out Vector128<float> linB);
                linR.Store(rowLinear0 + x);
                linG.Store(rowLinear1 + x);
                linB.Store(rowLinear2 + x);
            }

            for (; x < xsize; x++)
            {
                XybToRgbScalar(
                    rowOpsin0[x],
                    rowOpsin1[x],
                    rowOpsin2[x],
                    opsinParams,
                    out rowLinear0[x],
                    out rowLinear1[x],
                    out rowLinear2[x]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte LinearToSrgb8(float v)
        {
            v        = Math.Clamp(v, 0.0f, 1.0f);
            float tf = TfSrgb.FastLinearToSrgb(v);

            return (byte)Math.Clamp(MathF.Round(tf * 255.0f), 0.0f, 255.0f);
        }
    }

    /// <summary>
    /// DC dequantization and adaptive DC smoothing functions.
    /// Ported from <c>lib/jxl/compressed_dc.h</c> and <c>lib/jxl/compressed_dc.cc</c>.
    /// </summary>
    public static unsafe class CompressedDc
    {
        private const float KW1 = 0.20345139757231578f;
        private const float KW2 = 0.0334829185968739f;
        private const float KW0 = 1.0f - (4.0f * (KW1 + KW2));

        /// <summary>
        /// Smooths DC values in already-smooth areas to counteract banding.
        /// Ported from <c>jxl::AdaptiveDCSmoothing</c> in <c>lib/jxl/compressed_dc.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate the temporary smoothed image.</param>
        /// <param name="dcFactors">3-element DC quantization factors for X, Y, B.</param>
        /// <param name="dc">DC 3-plane float image to smooth in place.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status AdaptiveDCSmoothing(
            JxlMemoryManager? memoryManager,
            ReadOnlySpan<float> dcFactors,
            Image3<float> dc,
            ThreadPool? pool)
        {
            nuint xsize = dc.XSize;
            nuint ysize = dc.YSize;

            if (ysize <= 2 || xsize <= 2)
            {
                return true;
            }

            JxlMemoryManager mm = memoryManager ?? JxlMemoryManager.Default;
            StatusOr<Image3<float>> smoothedResult = Image3<float>.Create(mm, xsize, ysize);

            if (!smoothedResult.IsOk)
            {
                return smoothedResult.Status;
            }

            using Image3<float> smoothed = smoothedResult.Value!;
            CopyTopAndBottomBorders(dc, smoothed, xsize, ysize);

            float dcf0 = dcFactors[0];
            float dcf1 = dcFactors[1];
            float dcf2 = dcFactors[2];

            Status poolStatus = DataParallel.RunOnPool(
                pool,
                1,
                (uint)(ysize - 1),
                ThreadPool.NoInit,
                (uint y, nuint _) =>
                {
                    SmoothDcRow(dc, smoothed, xsize, y, dcf0, dcf1, dcf2);

                    return true;
                },
                "DCSmoothingRow");

            if (!poolStatus)
            {
                return poolStatus;
            }

            dc.Swap(smoothed);

            return true;
        }

        /// <summary>
        /// Dequantizes modular DC channels into <paramref name="dc"/> and populates DC context buckets in <paramref name="quantDc"/>.
        /// Ported from <c>jxl::DequantDC</c> in <c>lib/jxl/compressed_dc.cc</c>.
        /// </summary>
        /// <param name="r">Group rectangle in 8x8 blocks.</param>
        /// <param name="dc">Destination floating-point DC image.</param>
        /// <param name="quantDc">Destination DC quantization context bucket plane.</param>
        /// <param name="inImage">Source modular DC image (channels in Y, X, B order).</param>
        /// <param name="dcFactors">3-element DC dequantization factors.</param>
        /// <param name="mul">Additional scale multiplier.</param>
        /// <param name="cflFactors">3-element chroma-from-luma DC factors.</param>
        /// <param name="chromaSubsampling">Chroma subsampling configuration.</param>
        /// <param name="bctx">Block context map containing DC thresholds.</param>
        public static void DequantDC(
            in Rect r,
            Image3<float> dc,
            ImagePlane<byte> quantDc,
            ModularImage inImage,
            ReadOnlySpan<float> dcFactors,
            float mul,
            ReadOnlySpan<float> cflFactors,
            YCbCrChromaSubsampling chromaSubsampling,
            BlockCtxMap bctx)
        {
            if (chromaSubsampling.Is444())
            {
                DequantDc444(in r, dc, inImage, dcFactors, mul, cflFactors);
            }
            else
            {
                DequantDcSubsampled(in r, dc, inImage, dcFactors, mul, chromaSubsampling);
            }

            FillQuantDcBuckets(in r, quantDc, inImage, chromaSubsampling, bctx);
        }

        private static void CopyTopAndBottomBorders(
            Image3<float> dc,
            Image3<float> smoothed,
            nuint xsize,
            nuint ysize)
        {
            long rowBytes = (long)(xsize * sizeof(float));

            for (nuint c = 0; c < 3; c++)
            {
                Buffer.MemoryCopy(dc.PlaneRow(c, 0), smoothed.PlaneRow(c, 0), rowBytes, rowBytes);
                Buffer.MemoryCopy(dc.PlaneRow(c, ysize - 1), smoothed.PlaneRow(c, ysize - 1), rowBytes, rowBytes);
            }
        }

        private static void SmoothDcRow(
            Image3<float> dc,
            Image3<float> smoothed,
            nuint xsize,
            uint y,
            float dcf0,
            float dcf1,
            float dcf2)
        {
            float* top0 = dc.ConstPlaneRow(0, y - 1);
            float* top1 = dc.ConstPlaneRow(1, y - 1);
            float* top2 = dc.ConstPlaneRow(2, y - 1);
            float* cur0 = dc.ConstPlaneRow(0, y);
            float* cur1 = dc.ConstPlaneRow(1, y);
            float* cur2 = dc.ConstPlaneRow(2, y);
            float* bot0 = dc.ConstPlaneRow(0, y + 1);
            float* bot1 = dc.ConstPlaneRow(1, y + 1);
            float* bot2 = dc.ConstPlaneRow(2, y + 1);
            float* out0 = smoothed.PlaneRow(0, y);
            float* out1 = smoothed.PlaneRow(1, y);
            float* out2 = smoothed.PlaneRow(2, y);

            out0[0]         = cur0[0];
            out1[0]         = cur1[0];
            out2[0]         = cur2[0];
            out0[xsize - 1] = cur0[xsize - 1];
            out1[xsize - 1] = cur1[xsize - 1];
            out2[xsize - 1] = cur2[xsize - 1];

            for (nuint x = 1; x < xsize - 1; x++)
            {
                float gap = 0.5f;
                ComputePixelChannelScalar(dcf0, top0, cur0, bot0, x, out float mcX, out float smX, ref gap);
                ComputePixelChannelScalar(dcf1, top1, cur1, bot1, x, out float mcY, out float smY, ref gap);
                ComputePixelChannelScalar(dcf2, top2, cur2, bot2, x, out float mcB, out float smB, ref gap);

                float factor = (-4.0f * gap) + 3.0f;

                if (factor < 0.0f)
                {
                    factor = 0.0f;
                }

                out0[x] = ((smX - mcX) * factor) + mcX;
                out1[x] = ((smY - mcY) * factor) + mcY;
                out2[x] = ((smB - mcB) * factor) + mcB;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputePixelChannelScalar(
            float dcFactor,
            float* rowTop,
            float* row,
            float* rowBottom,
            nuint x,
            out float mc,
            out float sm,
            ref float gap)
        {
            float tl = rowTop[x - 1];
            float tc = rowTop[x];
            float tr = rowTop[x + 1];
            float ml = row[x - 1];
            mc       = row[x];
            float mr = row[x + 1];
            float bl = rowBottom[x - 1];
            float bc = rowBottom[x];
            float br = rowBottom[x + 1];

            float corner = (tl + tr) + (bl + br);
            float side   = (ml + mr) + (tc + bc);
            sm           = (corner * KW2) + (side * KW1) + (mc * KW0);
            float diff   = MathF.Abs((mc - sm) / dcFactor);

            if (diff > gap)
            {
                gap = diff;
            }
        }

        private static void DequantDc444(
            in Rect r,
            Image3<float> dc,
            ModularImage inImage,
            ReadOnlySpan<float> dcFactors,
            float mul,
            ReadOnlySpan<float> cflFactors)
        {
            float facX    = dcFactors[0] * mul;
            float facY    = dcFactors[1] * mul;
            float facB    = dcFactors[2] * mul;
            float cflFacX = cflFactors[0];
            float cflFacB = cflFactors[2];

            for (int y = 0; y < r.YSize; y++)
            {
                float* decRowX  = r.PlaneRow(dc, 0, (nuint)y);
                float* decRowY  = r.PlaneRow(dc, 1, (nuint)y);
                float* decRowB  = r.PlaneRow(dc, 2, (nuint)y);
                int* quantRowX  = inImage.Channels[1].Plane.Row((nuint)y);
                int* quantRowY  = inImage.Channels[0].Plane.Row((nuint)y);
                int* quantRowB  = inImage.Channels[2].Plane.Row((nuint)y);

                for (int x = 0; x < r.XSize; x++)
                {
                    float inX  = quantRowX[x] * facX;
                    float inY  = quantRowY[x] * facY;
                    float inB  = quantRowB[x] * facB;
                    decRowY[x] = inY;
                    decRowX[x] = (inY * cflFacX) + inX;
                    decRowB[x] = (inY * cflFacB) + inB;
                }
            }
        }

        private static void DequantDcSubsampled(
            in Rect r,
            Image3<float> dc,
            ModularImage inImage,
            ReadOnlySpan<float> dcFactors,
            float mul,
            YCbCrChromaSubsampling chromaSubsampling)
        {
            ReadOnlySpan<int> channelOrder = stackalloc int[3] { 1, 0, 2 };

            foreach (int c in channelOrder)
            {
                int hShift = (int)chromaSubsampling.HShift((nuint)c);
                int vShift = (int)chromaSubsampling.VShift((nuint)c);
                Rect rect  = new Rect(
                    r.X0 >> hShift,
                    r.Y0 >> vShift,
                    r.XSize >> hShift,
                    r.YSize >> vShift);
                float fac  = dcFactors[c] * mul;
                Channel ch = inImage.Channels[c < 2 ? c ^ 1 : c];

                for (int y = 0; y < rect.YSize; y++)
                {
                    int* quantRow = ch.Plane.Row((nuint)y);
                    float* row    = rect.PlaneRow(dc, (nuint)c, (nuint)y);

                    for (int x = 0; x < rect.XSize; x++)
                    {
                        row[x] = quantRow[x] * fac;
                    }
                }
            }
        }

        private static void FillQuantDcBuckets(
            in Rect r,
            ImagePlane<byte> quantDc,
            ModularImage inImage,
            YCbCrChromaSubsampling chromaSubsampling,
            BlockCtxMap bctx)
        {
            if (bctx.NumDcCtxs <= 1)
            {
                for (int y = 0; y < r.YSize; y++)
                {
                    byte* qdcRow = r.Row(quantDc, (nuint)y);
                    new Span<byte>(qdcRow, r.XSize).Clear();
                }

                return;
            }

            int hShift0 = (int)chromaSubsampling.HShift(0);
            int hShift1 = (int)chromaSubsampling.HShift(1);
            int hShift2 = (int)chromaSubsampling.HShift(2);
            int vShift0 = (int)chromaSubsampling.VShift(0);
            int vShift1 = (int)chromaSubsampling.VShift(1);
            int vShift2 = (int)chromaSubsampling.VShift(2);

            for (int y = 0; y < r.YSize; y++)
            {
                byte* qdcRowVal = r.Row(quantDc, (nuint)y);
                int* quantRowX  = inImage.Channels[1].Plane.Row((nuint)(y >> vShift0));
                int* quantRowY  = inImage.Channels[0].Plane.Row((nuint)(y >> vShift1));
                int* quantRowB  = inImage.Channels[2].Plane.Row((nuint)(y >> vShift2));

                for (int x = 0; x < r.XSize; x++)
                {
                    int bucketX = CountExceededThresholds(bctx.DcThresholds[0], quantRowX[x >> hShift0]);
                    int bucketY = CountExceededThresholds(bctx.DcThresholds[1], quantRowY[x >> hShift1]);
                    int bucketB = CountExceededThresholds(bctx.DcThresholds[2], quantRowB[x >> hShift2]);

                    int bucket = bucketX;
                    bucket    *= bctx.DcThresholds[2].Length + 1;
                    bucket    += bucketB;
                    bucket    *= bctx.DcThresholds[1].Length + 1;
                    bucket    += bucketY;
                    qdcRowVal[x] = (byte)bucket;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountExceededThresholds(int[] thresholds, int val)
        {
            int bucket = 0;

            for (int i = 0; i < thresholds.Length; i++)
            {
                if (val > thresholds[i])
                {
                    bucket++;
                }
            }

            return bucket;
        }
    }
}
