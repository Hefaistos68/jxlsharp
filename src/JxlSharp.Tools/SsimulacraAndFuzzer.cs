// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Threads;

namespace JxlSharp.Tools
{
    /// <summary>
    /// Per-scale SSIM statistics for <see cref="Ssimulacra"/>.
    /// Port of <c>ssimulacra::SsimulacraScale</c> from <c>tools/ssimulacra.h</c>.
    /// </summary>
    public sealed class SsimulacraScale
    {
        /// <summary>Average SSIM per channel (L, A, B).</summary>
        public readonly double[] AvgSsim = new double[3];

        /// <summary>Minimum 4x4 downsampled SSIM per channel (L, A, B).</summary>
        public readonly double[] MinSsim = new double[3];
    }

    /// <summary>
    /// SSIMULACRA (v1) perceptual metric result and computation methods.
    /// Port of <c>tools/ssimulacra.h</c> and <c>tools/ssimulacra.cc</c>.
    /// </summary>
    public sealed class Ssimulacra
    {
        private const float C1        = 0.0001f;
        private const float C2        = 0.0004f;
        private const int   NumScales = 6;

        private static readonly double[][] ScaleWeights = new double[][]
        {
            new double[] { 0.04480, 0.00300, 0.00300 },
            new double[] { 0.28560, 0.00896, 0.00896 },
            new double[] { 0.30010, 0.05712, 0.05712 },
            new double[] { 0.23630, 0.06002, 0.06002 },
            new double[] { 0.13330, 0.06726, 0.06726 },
            new double[] { 0.10000, 0.05000, 0.05000 },
        };

        private static readonly double[][] MinScaleWeights = new double[][]
        {
            new double[] { 0.02000, 0.00005, 0.00005 },
            new double[] { 0.03000, 0.00025, 0.00025 },
            new double[] { 0.02500, 0.00100, 0.00100 },
            new double[] { 0.02000, 0.00150, 0.00150 },
            new double[] { 0.01200, 0.00175, 0.00175 },
            new double[] { 0.00500, 0.00175, 0.00175 },
        };

        private static readonly double[] EdgeWeight = new double[] { 1.5, 0.1, 0.1 };
        private static readonly double[] GridWeight = new double[] { 1.0, 0.1, 0.1 };

        /// <summary>Per-scale measurements.</summary>
        public readonly List<SsimulacraScale> Scales = new List<SsimulacraScale>();

        /// <summary>Average edge difference per channel.</summary>
        public readonly double[] AvgEdgediff = new double[3];

        /// <summary>Row 2nd-percentile averages (index 0: SSIM, index 1: edge diff).</summary>
        public readonly double[][] RowP2 = new double[][] { new double[3], new double[3] };

        /// <summary>Column 2nd-percentile averages (index 0: SSIM, index 1: edge diff).</summary>
        public readonly double[][] ColP2 = new double[][] { new double[3], new double[3] };

        /// <summary>Whether simple mode (excluding edge and grid penalties) was used.</summary>
        public bool Simple;

        /// <summary>
        /// Computes the final SSIMULACRA DSSIM score in the range [0, 1].
        /// </summary>
        /// <returns>Computed DSSIM score.</returns>
        public double Score()
        {
            double ssim    = 0.0;
            double ssimMax = 0.0;

            for (int c = 0; c < 3; ++c)
            {
                for (int scale = 0; scale < Scales.Count; ++scale)
                {
                    ssim    += ScaleWeights[scale][c] * Scales[scale].AvgSsim[c];
                    ssimMax += ScaleWeights[scale][c];
                    ssim    += MinScaleWeights[scale][c] * Scales[scale].MinSsim[c];
                    ssimMax += MinScaleWeights[scale][c];
                }

                if (!Simple)
                {
                    ssim    += EdgeWeight[c] * AvgEdgediff[c];
                    ssimMax += EdgeWeight[c];
                    ssim    += GridWeight[c] * (RowP2[0][c] + RowP2[1][c] + ColP2[0][c] + ColP2[1][c]);
                    ssimMax += 4.0 * GridWeight[c];
                }
            }

            double dssim = ssimMax / ssim - 1.0;

            return Math.Min(1.0, Math.Max(0.0, dssim));
        }

        /// <summary>
        /// Prints per-scale SSIMULACRA details to standard output.
        /// </summary>
        public void PrintDetails()
        {
            for (int s = 0; s < Scales.Count; ++s)
            {
                if (s < NumScales)
                {
                    PrintItem("avg ssim", s, Scales[s].AvgSsim, ScaleWeights[s]);
                    PrintItem("min ssim", s, Scales[s].MinSsim, MinScaleWeights[s]);
                }

                if (s == 0 && !Simple)
                {
                    PrintItem("avg edif", s, AvgEdgediff, EdgeWeight);
                    PrintItem("rp2 ssim", s, RowP2[0], GridWeight);
                    PrintItem("cp2 ssim", s, ColP2[0], GridWeight);
                    PrintItem("rp2 edif", s, RowP2[1], GridWeight);
                    PrintItem("cp2 edif", s, ColP2[1], GridWeight);
                }
            }
        }

        /// <summary>
        /// Computes the SSIMULACRA metric between two 3-plane RGB float images.
        /// </summary>
        /// <param name="orig">Original image.</param>
        /// <param name="distorted">Distorted image.</param>
        /// <param name="simple">Whether to omit edge and grid artifact penalties.</param>
        /// <returns><see cref="StatusOr{Ssimulacra}"/> containing the computed metric.</returns>
        public static StatusOr<Ssimulacra> ComputeDiff(
            Image3<float> orig,
            Image3<float> distorted,
            bool simple = false)
        {
            JxlMemoryManager memoryManager = NoMemoryManager.Instance;
            Ssimulacra ssimulacra          = new Ssimulacra { Simple = simple };
            var img1Result                 = Rgb2Lab(orig);

            if (!img1Result.IsOk)
            {
                return img1Result.Status;
            }

            Image3<float> img1 = img1Result.Value!;
            var img2Result     = Rgb2Lab(distorted);

            if (!img2Result.IsOk)
            {
                img1.Dispose();

                return img2Result.Status;
            }

            Image3<float> img2 = img2Result.Value!;

            try
            {
                return ComputeDiffScales(memoryManager, ref img1, ref img2, simple, ssimulacra);
            }
            finally
            {
                img1.Dispose();
                img2.Dispose();
            }
        }

        private static StatusOr<Ssimulacra> ComputeDiffScales(
            JxlMemoryManager memoryManager,
            ref Image3<float> img1,
            ref Image3<float> img2,
            bool simple,
            Ssimulacra ssimulacra)
        {
            var mulResult = Image3<float>.Create(memoryManager, img1.XSize, img1.YSize);

            if (!mulResult.IsOk)
            {
                return mulResult.Status;
            }

            using Image3<float> mul = mulResult.Value!;
            var blurResult          = SsimulacraHelper.GaussianBlur.Create(img1.XSize, img1.YSize);

            if (!blurResult.IsOk)
            {
                return blurResult.Status;
            }

            using SsimulacraHelper.GaussianBlur blur = blurResult.Value!;

            for (int scale = 0; scale < NumScales; ++scale)
            {
                if (img1.XSize < 8 || img1.YSize < 8)
                {
                    break;
                }

                if (scale > 0 && !DownsamplePair(ref img1, ref img2))
                {
                    return Status.Failure("Failed to downsample image pair.");
                }

                Status scaleStatus = ProcessSingleScale(img1, img2, mul, blur, scale, simple, ssimulacra);

                if (!scaleStatus)
                {
                    return scaleStatus;
                }
            }

            return ssimulacra;
        }

        private static bool DownsamplePair(ref Image3<float> img1, ref Image3<float> img2)
        {
            var ds1 = SsimulacraHelper.Downsample(img1, 2, 2);

            if (!ds1.IsOk)
            {
                return false;
            }

            var ds2 = SsimulacraHelper.Downsample(img2, 2, 2);

            if (!ds2.IsOk)
            {
                ds1.Value!.Dispose();

                return false;
            }

            img1.Dispose();
            img2.Dispose();
            img1 = ds1.Value!;
            img2 = ds2.Value!;

            return true;
        }

        private static Status ProcessSingleScale(
            Image3<float> img1,
            Image3<float> img2,
            Image3<float> mul,
            SsimulacraHelper.GaussianBlur blur,
            int scale,
            bool simple,
            Ssimulacra ssimulacra)
        {
            if (!mul.ShrinkTo(img1.XSize, img2.YSize) || !blur.ShrinkTo(img1.XSize, img2.YSize))
            {
                return false;
            }

            SsimulacraHelper.Multiply(img1, img1, mul);
            var s1Result = blur.Apply(mul);

            if (!s1Result.IsOk)
            {
                return s1Result.Status;
            }

            using Image3<float> sigma1Sq = s1Result.Value!;

            SsimulacraHelper.Multiply(img2, img2, mul);
            var s2Result = blur.Apply(mul);

            if (!s2Result.IsOk)
            {
                return s2Result.Status;
            }

            using Image3<float> sigma2Sq = s2Result.Value!;

            SsimulacraHelper.Multiply(img1, img2, mul);
            var s12Result = blur.Apply(mul);

            if (!s12Result.IsOk)
            {
                return s12Result.Status;
            }

            using Image3<float> sigma12 = s12Result.Value!;
            var mu1Result               = blur.Apply(img1);

            if (!mu1Result.IsOk)
            {
                return mu1Result.Status;
            }

            using Image3<float> mu1 = mu1Result.Value!;
            var mu2Result           = blur.Apply(img2);

            if (!mu2Result.IsOk)
            {
                return mu2Result.Status;
            }

            using Image3<float> mu2 = mu2Result.Value!;

            if (!ComputeScaleSsim(mu1, mu2, sigma1Sq, sigma2Sq, sigma12, mul, ssimulacra))
            {
                return false;
            }

            if (scale == 0 && !simple)
            {
                ComputeScale0EdgeDiff(img1, mu1, img2, mu2, sigma1Sq, ssimulacra);
            }

            return true;
        }

        private static Status ComputeScaleSsim(
            Image3<float> mu1,
            Image3<float> mu2,
            Image3<float> sigma1Sq,
            Image3<float> sigma2Sq,
            Image3<float> sigma12,
            Image3<float> mul,
            Ssimulacra ssimulacra)
        {
            SsimulacraScale sscale = new SsimulacraScale();

            ComputeSsimMap(mu1, mu2, sigma1Sq, sigma2Sq, sigma12, mul, sscale.AvgSsim);

            var ssimMapResult = SsimulacraHelper.Downsample(mul, 4, 4);

            if (!ssimMapResult.IsOk)
            {
                return ssimMapResult.Status;
            }

            using Image3<float> ssimMap = ssimMapResult.Value!;
            bool isFirstFullScale       = ssimulacra.Scales.Count == 0 && !ssimulacra.Simple;

            for (nuint c = 0; c < 3; ++c)
            {
                ImageOps.ImageMinMax(ssimMap.Plane(c), out float minVal, out _);
                sscale.MinSsim[c] = minVal;

                if (isFirstFullScale)
                {
                    RowColAvgP2(ssimMap.Plane(c), out ssimulacra.RowP2[0][c], out ssimulacra.ColP2[0][c]);
                }
            }

            ssimulacra.Scales.Add(sscale);

            return true;
        }

        private static void ComputeScale0EdgeDiff(
            Image3<float> img1,
            Image3<float> mu1,
            Image3<float> img2,
            Image3<float> mu2,
            Image3<float> edgediff,
            Ssimulacra ssimulacra)
        {
            ComputeEdgeDiffMap(img1, mu1, img2, mu2, edgediff, ssimulacra.AvgEdgediff);

            for (nuint c = 0; c < 3; ++c)
            {
                RowColAvgP2(edgediff.Plane(c), out ssimulacra.RowP2[1][c], out ssimulacra.ColP2[1][c]);
            }
        }

        private static StatusOr<Image3<float>> Rgb2Lab(Image3<float> input)
        {
            var outResult = Image3<float>.Create(NoMemoryManager.Instance, input.XSize, input.YSize);

            if (!outResult.IsOk)
            {
                return outResult.Status;
            }

            Image3<float> output = outResult.Value!;

            for (nuint y = 0; y < input.YSize; ++y)
            {
                ReadOnlySpan<float> rowIn0 = input.ConstPlaneRow(0, y);
                ReadOnlySpan<float> rowIn1 = input.ConstPlaneRow(1, y);
                ReadOnlySpan<float> rowIn2 = input.ConstPlaneRow(2, y);
                Span<float> rowOut0        = output.PlaneRow(0, y);
                Span<float> rowOut1        = output.PlaneRow(1, y);
                Span<float> rowOut2        = output.PlaneRow(2, y);

                for (nuint x = 0; x < input.XSize; ++x)
                {
                    int ix = (int)x;

                    Rgb2LabPixel(rowIn0[ix], rowIn1[ix], rowIn2[ix], out rowOut0[ix], out rowOut1[ix], out rowOut2[ix]);
                }
            }

            return output;
        }

        private static void Rgb2LabPixel(float r, float g, float b, out float l, out float a, out float bOut)
        {
            const float kEpsilon = 0.00885645167903563081f;
            const float kS       = 0.13793103448275862068f;
            const float kK       = 7.78703703703703703703f;
            const float kGamma   = 1.0f / 3.0f;

            float fx = r * 0.43393624408206207259f + g * 0.37619779063650710152f + b * 0.18983429773803261441f;
            float fy = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
            float fz = r * 0.01775381083562901744f + g * 0.10945087235996326905f + b * 0.87263921028466483011f;
            float x  = fx > kEpsilon ? MathF.Pow(fx, kGamma) - kS : kK * fx;
            float y  = fy > kEpsilon ? MathF.Pow(fy, kGamma) - kS : kK * fy;
            float z  = fz > kEpsilon ? MathF.Pow(fz, kGamma) - kS : kK * fz;

            l    = y * 1.16f;
            a    = 0.39181818181818181818f + 2.27272727272727272727f * (x - y);
            bOut = 0.49045454545454545454f + 0.90909090909090909090f * (y - z);
        }

        private static void RowColAvgP2(ImagePlane<float> input, out double rp2, out double cp2)
        {
            double[] ravg = new double[input.YSize];
            double[] cavg = new double[input.XSize];

            for (nuint y = 0; y < input.YSize; ++y)
            {
                ReadOnlySpan<float> row = input.ConstRow(y);

                for (nuint x = 0; x < input.XSize; ++x)
                {
                    float val     = row[(int)x];
                    ravg[(int)y] += val;
                    cavg[(int)x] += val;
                }
            }

            Array.Sort(ravg);
            Array.Sort(cavg);
            rp2 = ravg[ravg.Length / 50] / input.XSize;
            cp2 = cavg[cavg.Length / 50] / input.YSize;
        }

        private static void ComputeEdgeDiffMap(
            Image3<float> img1,
            Image3<float> mu1,
            Image3<float> img2,
            Image3<float> mu2,
            Image3<float> output,
            double[] planeAvg)
        {
            for (nuint c = 0; c < 3; ++c)
            {
                double result = 0.0;
                nuint count   = 0;

                for (nuint y = 0; y < img1.YSize; ++y)
                {
                    ReadOnlySpan<float> row1  = img1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> row2  = img2.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowm1 = mu1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowm2 = mu2.ConstPlaneRow(c, y);
                    Span<float> rowOut        = output.PlaneRow(c, y);

                    for (nuint x = 0; x < img1.XSize; ++x)
                    {
                        int ix         = (int)x;
                        float edgediff = Math.Max(Math.Abs(row2[ix] - rowm2[ix]) - Math.Abs(row1[ix] - rowm1[ix]), 0.0f);
                        float v        = 1.0f - edgediff;
                        rowOut[ix]     = v;
                        count         += 1;
                        result        += (v - result) / count;
                    }
                }

                planeAvg[c] = result;
            }
        }

        private static void ComputeSsimMap(
            Image3<float> m1,
            Image3<float> m2,
            Image3<float> s11,
            Image3<float> s22,
            Image3<float> s12,
            Image3<float> output,
            double[] planeAverages)
        {
            for (nuint c = 0; c < 3; ++c)
            {
                double result = 0.0;
                nuint count   = 0;

                for (nuint y = 0; y < output.YSize; ++y)
                {
                    ReadOnlySpan<float> rowM1  = m1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowM2  = m2.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS11 = s11.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS22 = s22.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS12 = s12.ConstPlaneRow(c, y);
                    Span<float> rowOut         = output.PlaneRow(c, y);

                    for (nuint x = 0; x < output.XSize; ++x)
                    {
                        int ix       = (int)x;
                        float mu1    = rowM1[ix];
                        float mu2    = rowM2[ix];
                        float mu11   = mu1 * mu1;
                        float mu22   = mu2 * mu2;
                        float mu12   = mu1 * mu2;
                        float nomM   = 2.0f * mu12 + C1;
                        float nomS   = 2.0f * (rowS12[ix] - mu12) + C2;
                        float denomM = mu11 + mu22 + C1;
                        float denomS = (rowS11[ix] - mu11) + (rowS22[ix] - mu22) + C2;
                        float v      = (nomM * nomS) / (denomM * denomS);
                        rowOut[ix]   = v;
                        count       += 1;
                        result      += (v - result) / count;
                    }
                }

                planeAverages[c] = result;
            }
        }

        private static void PrintItem(string name, int scale, double[] vals, double[] weights)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "scale {0} {1} = [{2:F10} {3:F10} {4:F10}]  w = [{5:F5} {6:F5} {7:F5}]",
                scale,
                name,
                vals[0],
                vals[1],
                vals[2],
                weights[0],
                weights[1],
                weights[2]);

            Console.WriteLine(line);
        }
    }

    /// <summary>
    /// Per-scale SSIMULACRA 2 measurements.
    /// Port of <c>MsssimScale</c> from <c>tools/ssimulacra2.h</c>.
    /// </summary>
    public sealed class MsssimScale
    {
        /// <summary>1-norm and 4-norm SSIM error averages for X, Y, B.</summary>
        public readonly double[] AvgSsim = new double[3 * 2];

        /// <summary>1-norm and 4-norm ringing and smoothing error averages for X, Y, B.</summary>
        public readonly double[] AvgEdgediff = new double[3 * 4];
    }

    /// <summary>
    /// Multi-scale structural similarity result for SSIMULACRA 2.
    /// Port of <c>Msssim</c> from <c>tools/ssimulacra2.h</c>.
    /// </summary>
    public sealed class Msssim
    {
        private static readonly double[] Weights = new double[108]
        {
            0.0,
            0.0007376606707406586,
            0.0,
            0.0,
            0.0007793481682867309,
            0.0,
            0.0,
            0.0004371155730107379,
            0.0,
            1.1041726426657346,
            0.00066284834129271,
            0.00015231632783718752,
            0.0,
            0.0016406437456599754,
            0.0,
            1.8422455520539298,
            11.441172603757666,
            0.0,
            0.0007989109436015163,
            0.000176816438078653,
            0.0,
            1.8787594979546387,
            10.94906990605142,
            0.0,
            0.0007289346991508072,
            0.9677937080626833,
            0.0,
            0.00014003424285435884,
            0.9981766977854967,
            0.00031949755934435053,
            0.0004550992113792063,
            0.0,
            0.0,
            0.0013648766163243398,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            7.466890328078848,
            0.0,
            17.445833984131262,
            0.0006235601634041466,
            0.0,
            0.0,
            6.683678146179332,
            0.00037724407979611296,
            1.027889937768264,
            225.20515300849274,
            0.0,
            0.0,
            19.213238186143016,
            0.0011401524586618361,
            0.001237755635509985,
            176.39317598450694,
            0.0,
            0.0,
            24.43300999870476,
            0.28520802612117757,
            0.0004485436923833408,
            0.0,
            0.0,
            0.0,
            34.77906344483772,
            44.835625328877896,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0008680556573291698,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0005313191874358747,
            0.0,
            0.00016533814161379112,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0004179171803251336,
            0.0017290828234722833,
            0.0,
            0.0020827005846636437,
            0.0,
            0.0,
            8.826982764996862,
            23.19243343998926,
            0.0,
            95.1080498811086,
            0.9863978034400682,
            0.9834382792465353,
            0.0012286405048278493,
            171.2667255897307,
            0.9807858872435379,
            0.0,
            0.0,
            0.0,
            0.0005130064588990679,
            0.0,
            0.00010854057858411537,
        };

        /// <summary>Per-scale measurements (up to 6 scales).</summary>
        public readonly List<MsssimScale> Scales = new List<MsssimScale>();

        /// <summary>
        /// Computes the final SSIMULACRA 2 score (100 = identical, lower = worse).
        /// </summary>
        /// <returns>Computed SSIMULACRA 2 score.</returns>
        public double Score()
        {
            double ssim = 0.0;
            int idx     = 0;

            for (int c = 0; c < 3; ++c)
            {
                for (int scale = 0; scale < Scales.Count; ++scale)
                {
                    MsssimScale s = Scales[scale];

                    for (int n = 0; n < 2; ++n)
                    {
                        ssim += Weights[idx++] * Math.Abs(s.AvgSsim[c * 2 + n]);
                        ssim += Weights[idx++] * Math.Abs(s.AvgEdgediff[c * 4 + n]);
                        ssim += Weights[idx++] * Math.Abs(s.AvgEdgediff[c * 4 + n + 2]);
                    }
                }
            }

            ssim = ssim * 0.9562382616834844;
            ssim = 2.326765642916932 * ssim
                 - 0.020884521182843837 * ssim * ssim
                 + 6.248496625763138e-05 * ssim * ssim * ssim;

            if (ssim > 0.0)
            {
                return 100.0 - 10.0 * Math.Pow(ssim, 0.6276336467831387);
            }

            return 100.0;
        }
    }

    /// <summary>
    /// SSIMULACRA 2 perceptual metric computation.
    /// Port of <c>tools/ssimulacra2.h</c> and <c>tools/ssimulacra2.cc</c>.
    /// </summary>
    public static class Ssimulacra2
    {
        private const float C2        = 0.0009f;
        private const int   NumScales = 6;

        /// <summary>
        /// Computes the SSIMULACRA 2 metric between <paramref name="orig"/> and <paramref name="distorted"/>
        /// using a default alpha background of 0.5f.
        /// </summary>
        /// <param name="orig">Original image bundle.</param>
        /// <param name="distorted">Distorted image bundle.</param>
        /// <returns><see cref="StatusOr{Msssim}"/> containing the multi-scale result.</returns>
        public static StatusOr<Msssim> ComputeSSIMULACRA2(ImageBundle orig, ImageBundle distorted)
        {
            return ComputeSSIMULACRA2(orig, distorted, 0.5f);
        }

        /// <summary>
        /// Computes the SSIMULACRA 2 metric between <paramref name="orig"/> and <paramref name="dist"/>
        /// with the specified alpha background intensity <paramref name="bg"/>.
        /// </summary>
        /// <param name="orig">Original image bundle.</param>
        /// <param name="dist">Distorted image bundle.</param>
        /// <param name="bg">Background luminance in [0, 1] used for alpha blending.</param>
        /// <returns><see cref="StatusOr{Msssim}"/> containing the multi-scale result.</returns>
        public static StatusOr<Msssim> ComputeSSIMULACRA2(ImageBundle orig, ImageBundle dist, float bg)
        {
            JxlMemoryManager memoryManager = NoMemoryManager.Instance;
            var img1Result                 = Image3<float>.Create(memoryManager, orig.XSize(), orig.YSize());

            if (!img1Result.IsOk)
            {
                return img1Result.Status;
            }

            using Image3<float> img1 = img1Result.Value!;
            var img2Result           = Image3<float>.Create(memoryManager, img1.XSize, img1.YSize);

            if (!img2Result.IsOk)
            {
                return img2Result.Status;
            }

            using Image3<float> img2 = img2Result.Value!;
            var orig2Result          = orig.Copy();

            if (!orig2Result.IsOk)
            {
                return orig2Result.Status;
            }

            using ImageBundle orig2 = orig2Result.Value!;
            var dist2Result         = dist.Copy();

            if (!dist2Result.IsOk)
            {
                return dist2Result.Status;
            }

            using ImageBundle dist2 = dist2Result.Value!;

            if (!PrepareBundles(orig, dist, orig2, dist2, bg, img1, img2))
            {
                return Status.Failure("Failed to prepare XYB image bundles for SSIMULACRA2.");
            }

            return RunScales(memoryManager, orig2, dist2, img1, img2);
        }

        private static bool PrepareBundles(
            ImageBundle orig,
            ImageBundle dist,
            ImageBundle orig2,
            ImageBundle dist2,
            float bg,
            Image3<float> img1,
            Image3<float> img2)
        {
            if (orig.HasAlpha())
            {
                AlphaBlend(orig2, bg);
            }

            if (dist.HasAlpha())
            {
                AlphaBlend(dist2, bg);
            }

            orig2.ClearExtraChannels();
            dist2.ClearExtraChannels();

            JxlCmsInterface cms = JxlCms.GetDefaultCms();

            if (!orig2.TransformTo(ColorEncoding.LinearSRGB(orig2.IsGray()), cms) ||
                !dist2.TransformTo(ColorEncoding.LinearSRGB(dist2.IsGray()), cms))
            {
                return false;
            }

            if (!ConvertBundleToPositiveXyb(orig2, img1) ||
                !ConvertBundleToPositiveXyb(dist2, img2))
            {
                return false;
            }

            return true;
        }

        private static StatusOr<Msssim> RunScales(
            JxlMemoryManager memoryManager,
            ImageBundle orig2,
            ImageBundle dist2,
            Image3<float> img1,
            Image3<float> img2)
        {
            Msssim msssim = new Msssim();
            var mulResult = Image3<float>.Create(memoryManager, img1.XSize, img1.YSize);

            if (!mulResult.IsOk)
            {
                return mulResult.Status;
            }

            using Image3<float> mul = mulResult.Value!;
            var blurResult          = SsimulacraHelper.GaussianBlur.Create(img1.XSize, img1.YSize);

            if (!blurResult.IsOk)
            {
                return blurResult.Status;
            }

            using SsimulacraHelper.GaussianBlur blur = blurResult.Value!;

            for (int scale = 0; scale < NumScales; ++scale)
            {
                if (img1.XSize < 8 || img1.YSize < 8)
                {
                    break;
                }

                if (scale > 0 && !DownsampleBundlesToXyb(orig2, dist2, img1, img2))
                {
                    return Status.Failure("Failed to downsample bundles for SSIMULACRA2 scale.");
                }

                Status stepStatus = ProcessScale2(img1, img2, mul, blur, msssim);

                if (!stepStatus)
                {
                    return stepStatus;
                }
            }

            return msssim;
        }

        private static bool DownsampleBundlesToXyb(
            ImageBundle orig2,
            ImageBundle dist2,
            Image3<float> img1,
            Image3<float> img2)
        {
            var tmp1Result = SsimulacraHelper.Downsample(orig2.Color(), 2, 2);

            if (!tmp1Result.IsOk)
            {
                return false;
            }

            if (!orig2.SetFromImage(tmp1Result.Value!, ColorEncoding.LinearSRGB(orig2.IsGray())))
            {
                tmp1Result.Value!.Dispose();

                return false;
            }

            var tmp2Result = SsimulacraHelper.Downsample(dist2.Color(), 2, 2);

            if (!tmp2Result.IsOk)
            {
                return false;
            }

            if (!dist2.SetFromImage(tmp2Result.Value!, ColorEncoding.LinearSRGB(dist2.IsGray())))
            {
                tmp2Result.Value!.Dispose();

                return false;
            }

            return ConvertBundleToPositiveXyb(orig2, img1) && ConvertBundleToPositiveXyb(dist2, img2);
        }

        private static Status ProcessScale2(
            Image3<float> img1,
            Image3<float> img2,
            Image3<float> mul,
            SsimulacraHelper.GaussianBlur blur,
            Msssim msssim)
        {
            if (!mul.ShrinkTo(img1.XSize, img1.YSize) || !blur.ShrinkTo(img1.XSize, img1.YSize))
            {
                return false;
            }

            SsimulacraHelper.Multiply(img1, img1, mul);
            var s1Result = blur.Apply(mul);

            if (!s1Result.IsOk)
            {
                return s1Result.Status;
            }

            using Image3<float> sigma1Sq = s1Result.Value!;

            SsimulacraHelper.Multiply(img2, img2, mul);
            var s2Result = blur.Apply(mul);

            if (!s2Result.IsOk)
            {
                return s2Result.Status;
            }

            using Image3<float> sigma2Sq = s2Result.Value!;

            SsimulacraHelper.Multiply(img1, img2, mul);
            var s12Result = blur.Apply(mul);

            if (!s12Result.IsOk)
            {
                return s12Result.Status;
            }

            using Image3<float> sigma12 = s12Result.Value!;
            var mu1Result               = blur.Apply(img1);

            if (!mu1Result.IsOk)
            {
                return mu1Result.Status;
            }

            using Image3<float> mu1 = mu1Result.Value!;
            var mu2Result           = blur.Apply(img2);

            if (!mu2Result.IsOk)
            {
                return mu2Result.Status;
            }

            using Image3<float> mu2 = mu2Result.Value!;
            MsssimScale sscale      = new MsssimScale();

            ComputeSsimMap2(mu1, mu2, sigma1Sq, sigma2Sq, sigma12, sscale.AvgSsim);
            ComputeEdgeDiffMap2(img1, mu1, img2, mu2, sscale.AvgEdgediff);
            msssim.Scales.Add(sscale);

            return true;
        }

        private static bool ConvertBundleToPositiveXyb(ImageBundle bundle, Image3<float> xyb)
        {
            if (!xyb.ShrinkTo(bundle.XSize(), bundle.YSize()))
            {
                return false;
            }

            if (!ImageOps.CopyImageTo(bundle.Color(), xyb))
            {
                return false;
            }

            if (!EncXyb.ToXYB(bundle, null, xyb, JxlCms.GetDefaultCms()))
            {
                return false;
            }

            MakePositiveXYB(xyb);

            return true;
        }

        private static void MakePositiveXYB(Image3<float> img)
        {
            for (nuint y = 0; y < img.YSize; ++y)
            {
                Span<float> rowX = img.PlaneRow(0, y);
                Span<float> rowY = img.PlaneRow(1, y);
                Span<float> rowB = img.PlaneRow(2, y);

                for (nuint x = 0; x < img.XSize; ++x)
                {
                    int ix   = (int)x;
                    rowB[ix] = (rowB[ix] - rowY[ix]) + 0.55f;
                    rowX[ix] = rowX[ix] * 14.0f + 0.42f;
                    rowY[ix] += 0.01f;
                }
            }
        }

        private static void AlphaBlend(ImageBundle img, float bg)
        {
            Image3<float> color      = img.Color();
            ImagePlane<float>? alpha = img.Alpha();

            if (alpha is null)
            {
                return;
            }

            for (nuint y = 0; y < img.YSize(); ++y)
            {
                Span<float> r          = color.PlaneRow(0, y);
                Span<float> g          = color.PlaneRow(1, y);
                Span<float> b          = color.PlaneRow(2, y);
                ReadOnlySpan<float> a  = alpha.ConstRow(y);

                for (nuint x = 0; x < img.XSize(); ++x)
                {
                    int ix         = (int)x;
                    float alphaVal = a[ix];
                    float invAlpha = (1.0f - alphaVal) * bg;
                    r[ix]          = alphaVal * r[ix] + invAlpha;
                    g[ix]          = alphaVal * g[ix] + invAlpha;
                    b[ix]          = alphaVal * b[ix] + invAlpha;
                }
            }
        }

        private static void ComputeSsimMap2(
            Image3<float> m1,
            Image3<float> m2,
            Image3<float> s11,
            Image3<float> s22,
            Image3<float> s12,
            double[] planeAverages)
        {
            double onePerPixels = 1.0 / (m1.YSize * m1.XSize);

            for (nuint c = 0; c < 3; ++c)
            {
                double sum0 = 0.0;
                double sum1 = 0.0;

                for (nuint y = 0; y < m1.YSize; ++y)
                {
                    ReadOnlySpan<float> rowM1  = m1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowM2  = m2.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS11 = s11.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS22 = s22.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowS12 = s12.ConstPlaneRow(c, y);

                    for (nuint x = 0; x < m1.XSize; ++x)
                    {
                        int ix       = (int)x;
                        float mu1    = rowM1[ix];
                        float mu2    = rowM2[ix];
                        float mu11   = mu1 * mu1;
                        float mu22   = mu2 * mu2;
                        float mu12   = mu1 * mu2;
                        float diff   = mu1 - mu2;
                        float numM   = 1.0f - diff * diff;
                        float numS   = 2.0f * (rowS12[ix] - mu12) + C2;
                        float denomS = (rowS11[ix] - mu11) + (rowS22[ix] - mu22) + C2;
                        double d     = Math.Max(1.0 - ((double)(numM * numS) / denomS), 0.0);
                        double d2    = d * d;
                        sum0        += d;
                        sum1        += d2 * d2;
                    }
                }

                planeAverages[c * 2]     = onePerPixels * sum0;
                planeAverages[c * 2 + 1] = Math.Sqrt(Math.Sqrt(onePerPixels * sum1));
            }
        }

        private static void ComputeEdgeDiffMap2(
            Image3<float> img1,
            Image3<float> mu1,
            Image3<float> img2,
            Image3<float> mu2,
            double[] planeAverages)
        {
            double onePerPixels = 1.0 / (img1.YSize * img1.XSize);

            for (nuint c = 0; c < 3; ++c)
            {
                double sum0 = 0.0;
                double sum1 = 0.0;
                double sum2 = 0.0;
                double sum3 = 0.0;

                for (nuint y = 0; y < img1.YSize; ++y)
                {
                    ReadOnlySpan<float> row1  = img1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> row2  = img2.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowm1 = mu1.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> rowm2 = mu2.ConstPlaneRow(c, y);

                    for (nuint x = 0; x < img1.XSize; ++x)
                    {
                        int ix      = (int)x;
                        double d1   = (1.0 + Math.Abs(row2[ix] - rowm2[ix])) / (1.0 + Math.Abs(row1[ix] - rowm1[ix])) - 1.0;
                        double art  = Math.Max(d1, 0.0);
                        double art2 = art * art;
                        double lost = Math.Max(-d1, 0.0);
                        double lst2 = lost * lost;
                        sum0       += art;
                        sum1       += art2 * art2;
                        sum2       += lost;
                        sum3       += lst2 * lst2;
                    }
                }

                planeAverages[c * 4]     = onePerPixels * sum0;
                planeAverages[c * 4 + 1] = Math.Sqrt(Math.Sqrt(onePerPixels * sum1));
                planeAverages[c * 4 + 2] = onePerPixels * sum2;
                planeAverages[c * 4 + 3] = Math.Sqrt(Math.Sqrt(onePerPixels * sum3));
            }
        }
    }

    internal static class SsimulacraHelper
    {
        public static StatusOr<Image3<float>> Downsample(Image3<float> input, nuint fx, nuint fy)
        {
            nuint outXSize = (input.XSize + fx - 1) / fx;
            nuint outYSize = (input.YSize + fy - 1) / fy;
            var outResult  = Image3<float>.Create(NoMemoryManager.Instance, outXSize, outYSize);

            if (!outResult.IsOk)
            {
                return outResult.Status;
            }

            Image3<float> output = outResult.Value!;
            float normalize      = 1.0f / (fx * fy);

            for (nuint c = 0; c < 3; ++c)
            {
                DownsamplePlane(input, output, c, fx, fy, normalize);
            }

            return output;
        }

        public static void Multiply(Image3<float> a, Image3<float> b, Image3<float> mul)
        {
            for (nuint c = 0; c < 3; ++c)
            {
                for (nuint y = 0; y < a.YSize; ++y)
                {
                    ReadOnlySpan<float> in1 = a.ConstPlaneRow(c, y);
                    ReadOnlySpan<float> in2 = b.ConstPlaneRow(c, y);
                    Span<float> outRow      = mul.PlaneRow(c, y);

                    for (nuint x = 0; x < a.XSize; ++x)
                    {
                        int ix     = (int)x;
                        outRow[ix] = in1[ix] * in2[ix];
                    }
                }
            }
        }

        private static void DownsamplePlane(
            Image3<float> input,
            Image3<float> output,
            nuint c,
            nuint fx,
            nuint fy,
            float normalize)
        {
            nuint inMaxX = input.XSize - 1;

            for (nuint oy = 0; oy < output.YSize; ++oy)
            {
                Span<float> rowOut = output.PlaneRow(c, oy);

                for (nuint ox = 0; ox < output.XSize; ++ox)
                {
                    rowOut[(int)ox] = SumBox(input, c, ox, oy, fx, fy, inMaxX) * normalize;
                }
            }
        }

        private static float SumBox(
            Image3<float> input,
            nuint c,
            nuint ox,
            nuint oy,
            nuint fx,
            nuint fy,
            nuint inMaxX)
        {
            nuint inMaxY = input.YSize - 1;
            float sum    = 0.0f;

            for (nuint iy = 0; iy < fy; ++iy)
            {
                nuint y                   = Math.Min(oy * fy + iy, inMaxY);
                ReadOnlySpan<float> rowIn = input.ConstPlaneRow(c, y);

                for (nuint ix = 0; ix < fx; ++ix)
                {
                    nuint x = Math.Min(ox * fx + ix, inMaxX);
                    sum    += rowIn[(int)x];
                }
            }

            return sum;
        }

        internal sealed class GaussianBlur : IDisposable
        {
            private readonly RecursiveGaussian _rg = GaussBlur.CreateRecursiveGaussian(1.5);
            private ImagePlane<float> _temp;

            private GaussianBlur(ImagePlane<float> temp)
            {
                _temp = temp;
            }

            public static StatusOr<GaussianBlur> Create(nuint xsize, nuint ysize)
            {
                var tempResult = ImagePlane<float>.Create(NoMemoryManager.Instance, xsize, ysize);

                if (!tempResult.IsOk)
                {
                    return tempResult.Status;
                }

                return new GaussianBlur(tempResult.Value!);
            }

            public Status BlurPlane(ImagePlane<float> input, ImagePlane<float> output)
            {
                return GaussBlur.FastGaussian(
                    input.MemoryManager,
                    _rg,
                    input.XSize,
                    input.YSize,
                    y => input.ConstRow(y),
                    y => _temp.Row(y),
                    y => output.Row(y));
            }

            public StatusOr<Image3<float>> Apply(Image3<float> input)
            {
                var outResult = Image3<float>.Create(NoMemoryManager.Instance, input.XSize, input.YSize);

                if (!outResult.IsOk)
                {
                    return outResult.Status;
                }

                Image3<float> output = outResult.Value!;

                if (!BlurPlane(input.Plane(0), output.Plane(0)) ||
                    !BlurPlane(input.Plane(1), output.Plane(1)) ||
                    !BlurPlane(input.Plane(2), output.Plane(2)))
                {
                    output.Dispose();

                    return Status.Failure("Failed to blur image planes.");
                }

                return output;
            }

            public Status ShrinkTo(nuint xsize, nuint ysize)
            {
                return _temp.ShrinkTo(xsize, ysize);
            }

            public void Dispose()
            {
                _temp.Dispose();
            }
        }
    }

    /// <summary>
    /// Options for <see cref="DjxlFuzzer"/> decoder fuzzing.
    /// Port of <c>FuzzSpec</c> from <c>tools/djxl_fuzzer.cc</c>.
    /// </summary>
    public struct FuzzSpec
    {
        /// <summary>Pixel sample data type.</summary>
        public JxlDataType OutputType;

        /// <summary>Pixel byte endianness.</summary>
        public JxlEndianness OutputEndianness;

        /// <summary>Row alignment in bytes.</summary>
        public nuint OutputAlign;

        /// <summary>Whether to request an alpha channel in the main pixel format.</summary>
        public bool GetAlpha;

        /// <summary>Whether to request 1 color channel (grayscale) instead of 3 (RGB).</summary>
        public bool GetGrayscale;

        /// <summary>Whether to feed input incrementally in random-sized chunks.</summary>
        public bool UseStreaming;

        /// <summary>Whether to decode to pixels even if JPEG reconstruction is available.</summary>
        public bool JpegToPixels;

        /// <summary>Whether to use the image-out callback instead of a flat output buffer.</summary>
        public bool UseCallback;

        /// <summary>Whether to preserve encoded orientation instead of applying EXIF orientation.</summary>
        public bool KeepOrientation;

        /// <summary>Whether to subscribe to and decompress container boxes.</summary>
        public bool DecodeBoxes;

        /// <summary>Whether to enable frame coalescing.</summary>
        public bool Coalescing;

        /// <summary>Seed for random chunk sizes and extra channel selection.</summary>
        public uint RandomSeed;
    }

    /// <summary>
    /// Holds outputs produced by <see cref="DjxlFuzzer.DecodeJpegXl(ReadOnlySpan{byte}, JxlMemoryManager?, nuint, nuint, in FuzzSpec, FuzzDecodeResult)"/>.
    /// </summary>
    public sealed class FuzzDecodeResult
    {
        /// <summary>Decoded pixel buffer.</summary>
        public byte[] Pixels     = Array.Empty<byte>();

        /// <summary>Reconstructed JPEG buffer.</summary>
        public byte[] Jpeg       = Array.Empty<byte>();

        /// <summary>Extracted ICC profile buffer.</summary>
        public byte[] IccProfile = Array.Empty<byte>();

        /// <summary>Decoded width in pixels.</summary>
        public nuint XSize;

        /// <summary>Decoded height in pixels.</summary>
        public nuint YSize;
    }

    /// <summary>
    /// JPEG XL decoder fuzz target and test runner.
    /// Port of <c>tools/djxl_fuzzer.cc</c>.
    /// </summary>
    public static class DjxlFuzzer
    {
        private const int StreamingTargetNumberOfChunks = 128;

        /// <summary>
        /// Externally visible accumulator used to ensure decoded bytes are observed.
        /// </summary>
        public static volatile int ExternalCode;

        /// <summary>
        /// Decodes a JPEG XL input buffer according to <paramref name="spec"/>.
        /// </summary>
        /// <param name="jxl">Input JPEG XL bytes.</param>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="maxPixels">Maximum allowed pixels per frame.</param>
        /// <param name="maxTotalPixels">Maximum allowed cumulative pixels across frames/retries.</param>
        /// <param name="spec">Fuzzer specification options.</param>
        /// <param name="result">Output decode buffers and dimensions.</param>
        /// <returns><c>true</c> if decoding completed successfully; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool DecodeJpegXl(
            ReadOnlySpan<byte> jxl,
            JxlMemoryManager? memoryManager,
            nuint maxPixels,
            nuint maxTotalPixels,
            in FuzzSpec spec,
            FuzzDecodeResult result)
        {
            return DecodeJpegXl((ReadOnlyMemory<byte>)jxl.ToArray(), memoryManager, maxPixels, maxTotalPixels, in spec, result);
        }

        /// <summary>
        /// Decodes a JPEG XL input buffer from a managed memory buffer according to <paramref name="spec"/>.
        /// </summary>
        /// <param name="jxl">Input JPEG XL bytes.</param>
        /// <param name="memoryManager">Optional memory manager.</param>
        /// <param name="maxPixels">Maximum allowed pixels per frame.</param>
        /// <param name="maxTotalPixels">Maximum allowed cumulative pixels across frames/retries.</param>
        /// <param name="spec">Fuzzer specification options.</param>
        /// <param name="result">Output decode buffers and dimensions.</param>
        /// <returns><c>true</c> if decoding completed successfully; otherwise <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool DecodeJpegXl(
            ReadOnlyMemory<byte> jxl,
            JxlMemoryManager? memoryManager,
            nuint maxPixels,
            nuint maxTotalPixels,
            in FuzzSpec spec,
            FuzzDecodeResult result)
        {
            using FuzzSession session = new FuzzSession(jxl, memoryManager, maxPixels, maxTotalPixels, in spec, result);

            return session.Run();
        }

        /// <summary>
        /// Runs a single fuzzer input (requiring at least 4 trailing flag bytes).
        /// Port of <c>DoTestOneInput</c> from <c>tools/djxl_fuzzer.cc</c>.
        /// </summary>
        /// <param name="data">Fuzzer input bytes including the 4-byte trailing flag word.</param>
        /// <returns>Always returns 0 on completion.</returns>
        public static int DoTestOneInput(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return 0;
            }

            int payloadSize   = data.Length - 4;
            uint flags        = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(payloadSize, 4));
            int usedFlagBits  = 0;
            FuzzSpec spec     = ParseFuzzSpec(ref flags, ref usedFlagBits, (uint)payloadSize);
            _                 = GetFlag(ref flags, ref usedFlagBits, 0);
            nuint maxPixels   = 1u << 21;
            nuint maxTotal    = 5u * maxPixels;
            var memoryManager = new TrackingMemoryManager(
                1 * TrackingMemoryManager.GiB,
                5 * TrackingMemoryManager.GiB);
            var decodeResult  = new FuzzDecodeResult();

            DecodeJpegXl(data.Slice(0, payloadSize), memoryManager.Get(), maxPixels, maxTotal, in spec, decodeResult);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!memoryManager.Reset())
            {
                throw new InvalidOperationException("TrackingMemoryManager detected unfreed allocations.");
            }

            return 0;
        }

        /// <summary>
        /// Entry point for <c>DjxlFuzzTest</c> test cases.
        /// Port of <c>TestOneInput</c> from <c>tools/djxl_fuzzer.cc</c>.
        /// </summary>
        /// <param name="data">Fuzzer input bytes.</param>
        public static void TestOneInput(ReadOnlySpan<byte> data)
        {
            DoTestOneInput(data);
        }

        /// <summary>
        /// Entry point for <c>DjxlFuzzTest</c> byte array test cases.
        /// </summary>
        /// <param name="data">Fuzzer input byte array.</param>
        public static void TestOneInput(byte[] data)
        {
            DoTestOneInput(data);
        }

        internal static void ConsumeBytes(ReadOnlySpan<byte> bytes)
        {
            int acc = ExternalCode;

            for (int i = 0; i < bytes.Length; ++i)
            {
                byte b = bytes[i];
                acc   ^= b == 0 ? ~0 : b;
            }

            ExternalCode = acc;
        }

        private static FuzzSpec ParseFuzzSpec(ref uint flags, ref int usedFlagBits, uint size)
        {
            FuzzSpec spec         = default;
            spec.RandomSeed       = flags ^ size;
            spec.GetAlpha         = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.GetGrayscale     = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.UseStreaming     = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.JpegToPixels     = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.UseCallback      = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.KeepOrientation  = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.Coalescing       = GetBoolFlag(ref flags, ref usedFlagBits);
            spec.OutputType       = (JxlDataType)GetFlag(ref flags, ref usedFlagBits, (uint)JxlDataType.Float16);
            spec.OutputEndianness = (JxlEndianness)GetFlag(ref flags, ref usedFlagBits, (uint)JxlEndianness.BigEndian);
            spec.OutputAlign      = GetFlag(ref flags, ref usedFlagBits, 16);
            spec.DecodeBoxes      = GetBoolFlag(ref flags, ref usedFlagBits);

            return spec;
        }

        private static bool GetBoolFlag(ref uint flags, ref int usedFlagBits)
        {
            return GetFlag(ref flags, ref usedFlagBits, 1) != 0;
        }

        private static uint GetFlag(ref uint flags, ref int usedFlagBits, uint maxValue)
        {
            uint limit = 1;

            while (limit <= maxValue)
            {
                limit <<= 1;
                usedFlagBits++;

                if (usedFlagBits > 32)
                {
                    throw new InvalidOperationException("Flag bit budget exceeded in DoTestOneInput.");
                }
            }

            uint result = flags % limit;
            flags      /= limit;

            return result % (maxValue + 1);
        }

        private sealed class FuzzSession : IDisposable
        {
            private readonly nuint _initialSize;
            private readonly nuint _maxPixels;
            private readonly nuint _maxTotalPixels;
            private readonly FuzzSpec _spec;
            private readonly FuzzDecodeResult _result;
            private readonly ThreadParallelRunner? _runner;
            private readonly JxlDecoder _dec;
            private readonly Random _rng;
            private readonly JxlPixelFormat _format;
            private readonly DecodeCallbackState _cbState = new DecodeCallbackState();

            private ReadOnlyMemory<byte> _jxl;
            private nuint _leftover;
            private nuint _streamingSize;
            private nuint _totalPixels;
            private nuint _retryPixelTax;
            private JxlBasicInfo _info;
            private bool _seenBasicInfo;
            private bool _seenColorEncoding;
            private bool _seenPreview;
            private bool _seenNeedImageOut;
            private bool _seenFullImage;
            private bool _seenFrame;
            private uint _numFrames;
            private bool _seenJpegReconstruction;
            private bool _seenJpegNeedMoreOutput;
            private bool _testedFlush;
            private bool _wantPreview;
            private byte[] _previewPixels      = Array.Empty<byte>();
            private byte[] _extraChannelPixels = Array.Empty<byte>();
            private byte[] _boxBuffer          = Array.Empty<byte>();
            private bool _hasBoxBuffer;

            public FuzzSession(
                ReadOnlyMemory<byte> jxl,
                JxlMemoryManager? memoryManager,
                nuint maxPixels,
                nuint maxTotalPixels,
                in FuzzSpec spec,
                FuzzDecodeResult result)
            {
                nuint defaultThreads = ThreadParallelRunner.DefaultNumWorkerThreads();
                nuint numThreads     = Math.Min((nuint)2, defaultThreads);
                uint channels        = (spec.GetGrayscale ? 1u : 3u) + (spec.GetAlpha ? 1u : 0u);
                nuint size           = (nuint)jxl.Length;

                _initialSize    = size;
                _jxl            = jxl;
                _leftover       = size;
                _maxPixels      = maxPixels;
                _maxTotalPixels = maxTotalPixels;
                _spec           = spec;
                _result         = result;
                _runner         = ThreadParallelRunner.Create(memoryManager, numThreads);
                _dec            = new JxlDecoder(memoryManager);
                _rng            = new Random((int)spec.RandomSeed);
                _format         = new JxlPixelFormat(channels, spec.OutputType, spec.OutputEndianness, spec.OutputAlign);
            }

            public bool Run()
            {
                if (!ConfigureDecoder())
                {
                    return false;
                }

                for (;;)
                {
                    JxlDecoderStatus status = _dec.ProcessInput();

                    if (status == JxlDecoderStatus.Success)
                    {
                        return HandleSuccess();
                    }

                    if (!DispatchStatus(status))
                    {
                        return false;
                    }
                }
            }

            public void Dispose()
            {
                _dec.Dispose();
                _runner?.Dispose();
            }

            private bool ConfigureDecoder()
            {
                JxlDecoderStatus events = JxlDecoderStatus.BasicInfo
                    | JxlDecoderStatus.ColorEncoding
                    | JxlDecoderStatus.PreviewImage
                    | JxlDecoderStatus.Frame
                    | JxlDecoderStatus.FullImage
                    | JxlDecoderStatus.JpegReconstruction
                    | JxlDecoderStatus.Box;

                if (_dec.SubscribeEvents(events) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                if (_dec.SetParallelRunner(ThreadParallelRunner.Runner, _runner) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                Check(_dec.SetKeepOrientation(_spec.KeepOrientation) == JxlDecoderStatus.Success);
                Check(_dec.SetCoalescing(_spec.Coalescing) == JxlDecoderStatus.Success);

                if (!_spec.UseStreaming)
                {
                    _dec.SetInput(_jxl);
                    _dec.CloseInput();
                }

                if (_spec.DecodeBoxes)
                {
                    _dec.SetDecompressBoxes(true);
                }

                return true;
            }

            private bool DispatchStatus(JxlDecoderStatus status)
            {
                return status switch
                {
                    JxlDecoderStatus.NeedMoreInput        => HandleNeedMoreInput(),
                    JxlDecoderStatus.JpegNeedMoreOutput   => HandleJpegNeedMoreOutput(),
                    JxlDecoderStatus.BasicInfo            => HandleBasicInfo(),
                    JxlDecoderStatus.ColorEncoding        => HandleColorEncoding(),
                    JxlDecoderStatus.NeedPreviewOutBuffer => HandleNeedPreviewOutBuffer(),
                    JxlDecoderStatus.PreviewImage         => HandlePreviewImage(),
                    JxlDecoderStatus.Frame                => HandleFrame(),
                    JxlDecoderStatus.NeedImageOutBuffer   => HandleNeedImageOutBuffer(),
                    JxlDecoderStatus.JpegReconstruction   => HandleJpegReconstruction(),
                    JxlDecoderStatus.FullImage            => HandleFullImage(),
                    JxlDecoderStatus.Box                  => HandleBox(),
                    JxlDecoderStatus.BoxNeedMoreOutput    => HandleBoxNeedMoreOutput(),
                    _                                     => false,
                };
            }

            private bool HandleNeedMoreInput()
            {
                if (!_spec.UseStreaming || _totalPixels > _maxTotalPixels)
                {
                    return false;
                }

                _totalPixels    += _retryPixelTax;
                nuint remaining  = _dec.ReleaseInput();
                nuint used       = _streamingSize - remaining;
                _jxl             = _jxl.Slice((int)used);
                _leftover       -= used;
                _streamingSize  -= used;
                double expSample = -Math.Log(1.0 - _rng.NextDouble()) / StreamingTargetNumberOfChunks;
                nuint chunkSize  = Math.Max((nuint)1, (nuint)(_initialSize * Math.Min(1.0, expSample)));
                nuint addSize    = Math.Min(chunkSize, _leftover - _streamingSize);

                if (addSize == 0)
                {
                    return false;
                }

                _streamingSize += addSize;

                if (_dec.SetInput(_jxl.Slice(0, (int)_streamingSize)) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                if (_leftover == _streamingSize)
                {
                    _dec.CloseInput();
                }

                if (!_testedFlush && _seenFrame)
                {
                    _testedFlush = true;
                    _dec.FlushImage();
                }

                return true;
            }

            private bool HandleJpegNeedMoreOutput()
            {
                Check(!_wantPreview && !_spec.JpegToPixels && _seenJpegReconstruction);
                _seenJpegNeedMoreOutput = true;

                nuint rem       = _dec.ReleaseJPEGBuffer();
                int usedOutput  = _result.Jpeg.Length - (int)rem;
                int newCapacity = Math.Max(4096, _result.Jpeg.Length * 2);

                Array.Resize(ref _result.Jpeg, newCapacity);

                return _dec.SetJPEGBuffer(_result.Jpeg, usedOutput, newCapacity - usedOutput) == JxlDecoderStatus.Success;
            }

            private bool HandleBasicInfo()
            {
                Check(!_seenBasicInfo);
                _seenBasicInfo = true;

                if (_dec.GetBasicInfo(out _info) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                _result.XSize   = _info.XSize;
                _result.YSize   = _info.YSize;
                _cbState.Info   = _info;
                nuint numPixels = _result.XSize * _result.YSize;

                if ((_result.XSize != 0 && numPixels / _result.XSize != _result.YSize) || numPixels > _maxPixels)
                {
                    return false;
                }

                _retryPixelTax = numPixels;

                return ValidatePreviewAndExtraChannels();
            }

            private bool ValidatePreviewAndExtraChannels()
            {
                if (_info.HavePreview)
                {
                    _wantPreview     = true;
                    nuint prevX      = _info.Preview.XSize;
                    nuint prevY      = _info.Preview.YSize;
                    nuint prevPixels = prevX * prevY;

                    if ((prevX != 0 && prevPixels / prevX != prevY) || prevPixels > _maxPixels)
                    {
                        return false;
                    }
                }

                for (nuint ec = 0; ec < _info.NumExtraChannels; ++ec)
                {
                    Check(_dec.GetExtraChannelInfo(ec, out JxlExtraChannelInfo ecInfo) == JxlDecoderStatus.Success);
                    byte[] ecName = new byte[ecInfo.NameLength + 1];
                    Check(_dec.GetExtraChannelName(ec, ecName) == JxlDecoderStatus.Success);
                    ConsumeBytes(ecName);
                }

                return true;
            }

            private bool HandleColorEncoding()
            {
                Check(_seenBasicInfo && !_seenColorEncoding);
                _seenColorEncoding = true;

                if (_dec.GetICCProfileSize(JxlColorProfileTarget.Data, out nuint iccSize) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                _result.IccProfile = new byte[iccSize];

                return _dec.GetColorAsICCProfile(JxlColorProfileTarget.Data, _result.IccProfile) == JxlDecoderStatus.Success;
            }

            private bool HandleNeedPreviewOutBuffer()
            {
                Check(!_seenPreview && _wantPreview && _seenColorEncoding);

                if (_dec.PreviewOutBufferSize(_format, out nuint previewSize) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                _previewPixels = new byte[previewSize];
                Check(_dec.SetPreviewOutBuffer(_format, _previewPixels) == JxlDecoderStatus.Success);

                return true;
            }

            private bool HandlePreviewImage()
            {
                Check(!_seenPreview && _wantPreview && _seenColorEncoding);
                _wantPreview = false;
                _seenPreview = true;
                ConsumeBytes(_previewPixels);

                return true;
            }

            private bool HandleFrame()
            {
                Check(!_wantPreview && _seenColorEncoding && !_seenFrame);
                _seenFrame = true;
                Check(_dec.GetFrameHeader(out JxlFrameHeader frameHeader) == JxlDecoderStatus.Success);

                _cbState.XSize  = frameHeader.LayerInfo.XSize;
                _cbState.YSize  = frameHeader.LayerInfo.YSize;
                nuint numPixels = _cbState.XSize * _cbState.YSize;

                if (numPixels > _maxPixels || _totalPixels > _maxTotalPixels)
                {
                    return false;
                }

                _totalPixels += numPixels;
                _cbState.ResetRows(!_spec.Coalescing);

                byte[] frameName = new byte[frameHeader.NameLength + 1];
                Check(_dec.GetFrameName(frameName) == JxlDecoderStatus.Success);
                ConsumeBytes(frameName);

                if (!_spec.UseStreaming)
                {
                    return true;
                }

                return SetupFrameOutput();
            }

            private bool HandleNeedImageOutBuffer()
            {
                Check(!_wantPreview && _seenColorEncoding && _seenFrame && !_spec.UseStreaming && !_seenNeedImageOut);
                _seenNeedImageOut = true;

                return SetupFrameOutput();
            }

            private bool SetupFrameOutput()
            {
                if (_info.NumExtraChannels > 0)
                {
                    int ecIndex = _rng.Next(0, (int)_info.NumExtraChannels + 1);

                    if ((uint)ecIndex < _info.NumExtraChannels && !SetupExtraChannelOutput(_info.NumExtraChannels - 1))
                    {
                        return false;
                    }
                }

                if (_spec.UseCallback)
                {
                    return _dec.SetImageOutCallback(_format, DecodeCallbackState.Callback, _cbState) == JxlDecoderStatus.Success;
                }

                if (_dec.ImageOutBufferSize(_format, out nuint bufferSize) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                _result.Pixels = new byte[bufferSize];

                return _dec.SetImageOutBuffer(_format, _result.Pixels) == JxlDecoderStatus.Success;
            }

            private bool SetupExtraChannelOutput(uint lastEcIndex)
            {
                if (_dec.ExtraChannelBufferSize(_format, out nuint ecSize, lastEcIndex) != JxlDecoderStatus.Success)
                {
                    return false;
                }

                _extraChannelPixels = new byte[ecSize];

                return _dec.SetExtraChannelBuffer(_format, _extraChannelPixels, lastEcIndex) == JxlDecoderStatus.Success;
            }

            private bool HandleJpegReconstruction()
            {
                Check(!_seenJpegReconstruction);
                _seenJpegReconstruction = true;

                if (!_spec.JpegToPixels)
                {
                    _result.Jpeg = new byte[1];

                    if (_dec.SetJPEGBuffer(_result.Jpeg, 0, 1) != JxlDecoderStatus.Success)
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool HandleFullImage()
            {
                Check(!_wantPreview);

                if (!_spec.JpegToPixels && _seenJpegReconstruction)
                {
                    Check(_seenJpegNeedMoreOutput);
                    nuint rem = _dec.ReleaseJPEGBuffer();
                    Array.Resize(ref _result.Jpeg, _result.Jpeg.Length - (int)rem);
                }
                else
                {
                    Check(_seenNeedImageOut || _seenFrame);
                }

                _seenFullImage    = true;
                bool hadImageOut  = _seenNeedImageOut;
                _seenNeedImageOut = false;
                _seenFrame        = false;
                _numFrames++;

                ConsumeBytes(_result.Pixels);
                ConsumeBytes(_result.Jpeg);

                if (hadImageOut && _spec.UseCallback && _spec.Coalescing)
                {
                    _cbState.VerifyAllPixels(1);
                }

                return true;
            }

            private bool HandleSuccess()
            {
                Check(_seenFullImage);

                if (_seenNeedImageOut && _spec.UseCallback && _spec.Coalescing)
                {
                    _cbState.VerifyAllPixels((int)_numFrames);
                }

                return true;
            }

            private bool HandleBox()
            {
                if (_spec.DecodeBoxes)
                {
                    if (_hasBoxBuffer)
                    {
                        nuint remaining = _dec.ReleaseBoxBuffer();
                        int boxSize     = _boxBuffer.Length - (int)remaining;

                        if (boxSize > 0)
                        {
                            ConsumeBytes(_boxBuffer.AsSpan(0, boxSize));
                        }
                    }

                    _boxBuffer    = new byte[64];
                    _hasBoxBuffer = true;
                    _dec.SetBoxBuffer(_boxBuffer, 0, _boxBuffer.Length);
                }

                return true;
            }

            private bool HandleBoxNeedMoreOutput()
            {
                Check(_spec.DecodeBoxes);
                nuint remaining = _dec.ReleaseBoxBuffer();
                int boxSize     = _boxBuffer.Length - (int)remaining;

                Array.Resize(ref _boxBuffer, _boxBuffer.Length * 2);
                _dec.SetBoxBuffer(_boxBuffer, boxSize, _boxBuffer.Length - boxSize);

                return true;
            }

            private static void Check(bool ok)
            {
                if (!ok)
                {
                    throw new InvalidOperationException("DjxlFuzzer invariant check failed.");
                }
            }
        }

        private sealed class DecodeCallbackState
        {
            public static readonly JxlImageOutCallback Callback = OnImageOut;

            public JxlBasicInfo Info;
            public nuint XSize;
            public nuint YSize;
            private RowEntry[] _rows = Array.Empty<RowEntry>();

            public void ResetRows(bool clearExisting)
            {
                if (clearExisting || _rows.Length != (int)YSize)
                {
                    _rows = new RowEntry[YSize];

                    for (int y = 0; y < _rows.Length; ++y)
                    {
                        _rows[y] = new RowEntry((int)XSize + 1);
                    }

                    return;
                }

                for (int y = 0; y < _rows.Length; ++y)
                {
                    _rows[y].ResizeAndClear((int)XSize + 1);
                }
            }

            public void VerifyAllPixels(int expectedFirstDelta)
            {
                for (int y = 0; y < (int)YSize; ++y)
                {
                    int[] deltas = _rows[y].Delta;

                    if (deltas[0] != expectedFirstDelta || deltas[XSize] != -1)
                    {
                        throw new InvalidOperationException("Callback row boundary delta mismatch.");
                    }

                    for (nuint i = 1; i < XSize; ++i)
                    {
                        if (deltas[i] != 0)
                        {
                            throw new InvalidOperationException("Callback interior pixel delta mismatch.");
                        }
                    }
                }
            }

            private static void OnImageOut(object? opaque, nuint x, nuint y, nuint numPixels, ReadOnlySpan<byte> pixels)
            {
                DecodeCallbackState data = (DecodeCallbackState)opaque!;

                if (numPixels > data.XSize || x + numPixels > data.XSize || y >= data.YSize || (numPixels != 0 && pixels.IsEmpty))
                {
                    throw new InvalidOperationException("Invalid callback parameters in DecodeCallbackState.");
                }

                RowEntry row = data._rows[y];

                lock (row.SyncRoot)
                {
                    row.Delta[x]++;
                    row.Delta[x + numPixels]--;

                    if (!pixels.IsEmpty)
                    {
                        row.Value += pixels[0];
                    }
                }
            }

            private sealed class RowEntry
            {
                public readonly object SyncRoot = new object();
                public int[] Delta;
                public uint Value;

                public RowEntry(int length)
                {
                    Delta = new int[length];
                }

                public void ResizeAndClear(int length)
                {
                    if (Delta.Length != length)
                    {
                        Delta = new int[length];
                    }
                    else
                    {
                        Array.Clear(Delta, 0, Delta.Length);
                    }
                }
            }
        }
    }
}
