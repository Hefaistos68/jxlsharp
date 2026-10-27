// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    // Noise parameters shared by encoder/decoder.

    /// <summary>
    /// Parameters for photon noise synthesis lookup table.
    /// Ported from <c>jxl::NoiseParams</c> in <c>lib/jxl/noise.h</c>.
    /// </summary>
    public sealed class NoiseParams
    {
        /// <summary>Fixed-point quantization precision for noise LUT entries (10 bits = 1024.0f).</summary>
        public const float KNoisePrecision = 1024.0f;

        /// <summary>Maximum representable LUT value (<c>1023.4999f / KNoisePrecision</c>).</summary>
        public const float KNoiseLutMax = 1023.4999f / KNoisePrecision;

        /// <summary>Number of LUT control points (8).</summary>
        public const int KNumNoisePoints = 8;

        /// <summary>
        /// Noise strength lookup table indexed by pixel intensity / mean patch intensity.
        /// </summary>
        public readonly float[] Lut = new float[KNumNoisePoints];

        /// <summary>
        /// Resets all lookup table entries to zero.
        /// </summary>
        public void Clear()
        {
            Array.Clear(Lut, 0, Lut.Length);
        }

        /// <summary>
        /// Returns whether any lookup table entry has a non-negligible magnitude (<c>&gt; 1e-3f</c>).
        /// </summary>
        /// <returns><c>true</c> if any LUT entry is active; otherwise <c>false</c>.</returns>
        public bool HasAny()
        {
            foreach (float v in Lut)
            {
                if (MathF.Abs(v) > 1e-3f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies all LUT values from <paramref name="other"/> into this instance.
        /// </summary>
        /// <param name="other">Source <see cref="NoiseParams"/>.</param>
        public void CopyFrom(NoiseParams other)
        {
            Array.Copy(other.Lut, Lut, KNumNoisePoints);
        }

        /// <summary>
        /// Computes the integer LUT index and fractional interpolation weight for intensity <paramref name="x"/>.
        /// Ported from <c>jxl::IndexAndFrac</c> in <c>lib/jxl/noise.h</c>.
        /// </summary>
        /// <param name="x">Normalized intensity value.</param>
        /// <returns>Tuple of <c>(Index, Frac)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int Index, float Frac) IndexAndFrac(float x)
        {
            const int kScaleNumerator = KNumNoisePoints - 2;
            const float kScale        = kScaleNumerator / 1.0f;
            float scaledX             = MathF.Max(0.0f, x * kScale);
            float floorX              = MathF.Floor(scaledX);
            float fracX               = scaledX - floorX;

            if (scaledX >= kScaleNumerator + 1)
            {
                floorX = kScaleNumerator;
                fracX  = 1.0f;
            }

            return ((int)floorX, fracX);
        }
    }

    /// <summary>
    /// Represents a noise level sample at a given intensity.
    /// Ported from <c>jxl::NoiseLevel</c> in <c>lib/jxl/noise.h</c>.
    /// </summary>
    public struct NoiseLevel
    {
        /// <summary>Measured or target noise level.</summary>
        public float NoiseLevelValue;

        /// <summary>Patch intensity level.</summary>
        public float Intensity;
    }

    /// <summary>
    /// Decoder functions for noise parameter bitstreams and random plane generation.
    /// Ported from <c>lib/jxl/dec_noise.h</c> and <c>lib/jxl/dec_noise.cc</c>.
    /// </summary>
    public static class DecNoise
    {
        private const int KFloatsPerBatch = Xorshift128Plus.N * sizeof(ulong) / sizeof(float);

        /// <summary>
        /// Converts 4 random 32-bit unsigned integers to 4 floats in <c>[1, 2)</c>.
        /// Note: as the convolution kernel sums to 0, it does not matter whether inputs are in <c>[0, 1)</c> or <c>[1, 2)</c>.
        /// </summary>
        /// <param name="randomBits">Span of 4 random 32-bit integers.</param>
        /// <param name="floats">Span of 4 output floats.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BitsToFloat4(ReadOnlySpan<uint> randomBits, Span<float> floats)
        {
            Vector128<uint> bits    = Vector128.Create(randomBits);
            Vector128<uint> shifted = Vector128.ShiftRightLogical(bits, 9);
            Vector128<uint> mant    = shifted | Vector128.Create(0x3F800000u);

            mant.AsSingle().CopyTo(floats);
        }

        /// <summary>
        /// Converts a single random 32-bit unsigned integer to a float in <c>[1, 2)</c>.
        /// </summary>
        /// <param name="randomBits">Random 32-bit integer.</param>
        /// <returns>Float in <c>[1, 2)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BitsToFloatScalar(uint randomBits)
        {
            return BitConverter.UInt32BitsToSingle((randomBits >> 9) | 0x3F800000u);
        }

        /// <summary>
        /// Fills <paramref name="rect"/> of <paramref name="noise"/> with uniform random floats in <c>[1, 2)</c>
        /// generated from <paramref name="rng"/>.
        /// </summary>
        /// <param name="rng">Xorshift128+ random number generator.</param>
        /// <param name="rect">Target rectangle within <paramref name="noise"/>.</param>
        /// <param name="noise">Destination float plane.</param>
        public static void RandomImage(Xorshift128Plus rng, in Rect rect, ImagePlane<float> noise)
        {
            nuint xsize         = (nuint)rect.XSize;
            nuint ysize         = (nuint)rect.YSize;
            Span<ulong> batch64 = stackalloc ulong[Xorshift128Plus.N];
            Span<uint> batch32  = stackalloc uint[KFloatsPerBatch];

            for (nuint y = 0; y < ysize; ++y)
            {
                Span<float> row = rect.Row(noise, y);
                nuint x         = 0;

                // Only entire batches (avoids exceeding the image padding).
                for (; x + (nuint)KFloatsPerBatch < xsize; x += (nuint)KFloatsPerBatch)
                {
                    FillBatch32(rng, batch64, batch32);

                    for (int i = 0; i < KFloatsPerBatch; i += 4)
                    {
                        BitsToFloat4(batch32.Slice(i, 4), row.Slice((int)x + i, 4));
                    }
                }

                // Any remaining pixels.
                int batchPos = 0;

                FillBatch32(rng, batch64, batch32);

                for (; x + 4 <= xsize; x += 4)
                {
                    BitsToFloat4(batch32.Slice(batchPos, 4), row.Slice((int)x, 4));
                    batchPos += 4;
                }

                for (; x < xsize; ++x)
                {
                    row[(int)x] = BitsToFloatScalar(batch32[batchPos++]);
                }
            }
        }

        /// <summary>
        /// Generates 3 random noise planes seeded by the frame indices and group pixel coordinates <c>(x0, y0)</c>.
        /// </summary>
        /// <param name="visibleFrameIndex">Visible frame counter.</param>
        /// <param name="nonvisibleFrameIndex">Non-visible frame counter.</param>
        /// <param name="x0">Group horizontal pixel coordinate.</param>
        /// <param name="y0">Group vertical pixel coordinate.</param>
        /// <param name="plane0">First destination plane and rectangle.</param>
        /// <param name="plane1">Second destination plane and rectangle.</param>
        /// <param name="plane2">Third destination plane and rectangle.</param>
        public static void Random3Planes(
            nuint visibleFrameIndex,
            nuint nonvisibleFrameIndex,
            nuint x0,
            nuint y0,
            (ImagePlane<float> Plane, Rect Rect) plane0,
            (ImagePlane<float> Plane, Rect Rect) plane1,
            (ImagePlane<float> Plane, Rect Rect) plane2)
        {
            Xorshift128Plus rng = new Xorshift128Plus(
                (uint)visibleFrameIndex,
                (uint)nonvisibleFrameIndex,
                (uint)x0,
                (uint)y0);

            RandomImage(rng, in plane0.Rect, plane0.Plane);
            RandomImage(rng, in plane1.Rect, plane1.Plane);
            RandomImage(rng, in plane2.Rect, plane2.Plane);
        }

        /// <summary>
        /// Populates the 3 noise input channels in <paramref name="renderPipeline"/> for the given group and thread.
        /// Ported from <c>jxl::PrepareNoiseInput</c> in <c>lib/jxl/dec_noise.cc</c>.
        /// </summary>
        /// <param name="renderPipeline">Active render pipeline.</param>
        /// <param name="visibleFrameIndex">Visible frame counter.</param>
        /// <param name="nonvisibleFrameIndex">Non-visible frame counter.</param>
        /// <param name="frameDim">Frame dimensions.</param>
        /// <param name="frameHeader">Frame header.</param>
        /// <param name="groupIndex">Group index.</param>
        /// <param name="thread">Worker thread index.</param>
        public static void PrepareNoiseInput(
            RenderPipeline renderPipeline,
            nuint visibleFrameIndex,
            nuint nonvisibleFrameIndex,
            in FrameDimensions frameDim,
            FrameHeader frameHeader,
            nuint groupIndex,
            nuint thread)
        {
            nuint groupDim             = frameDim.GroupDim;
            nuint gx                   = groupIndex % frameDim.XSizeGroups;
            nuint gy                   = groupIndex / frameDim.XSizeGroups;
            RenderPipelineInput input  = renderPipeline.GetInputBuffers(groupIndex, thread);
            nuint numExtra             = frameHeader.NonserializedMetadata != null
                ? frameHeader.NonserializedMetadata.M.NumExtraChannels
                : (nuint)frameHeader.ExtraChannelUpsampling.Count;
            nuint noiseCStart          = 3 + numExtra;
            Span<(ImagePlane<float> Plane, Rect Rect)> rects =
                new (ImagePlane<float> Plane, Rect Rect)[3];

            for (nuint iy = 0; iy < frameHeader.Upsampling; iy++)
            {
                for (nuint ix = 0; ix < frameHeader.Upsampling; ix++)
                {
                    for (nuint c = 0; c < 3; c++)
                    {
                        (ImagePlane<float> plane, Rect r) = input.GetBuffer(noiseCStart + c);
                        int x1                            = r.X0 + r.XSize;
                        int y1                            = r.Y0 + r.YSize;
                        rects[(int)c]                     = (
                            plane,
                            new Rect(
                                r.X0 + (int)(ix * groupDim),
                                r.Y0 + (int)(iy * groupDim),
                                (int)groupDim,
                                (int)groupDim,
                                x1,
                                y1));
                    }

                    Random3Planes(
                        visibleFrameIndex,
                        nonvisibleFrameIndex,
                        ((gx * frameHeader.Upsampling) + ix) * groupDim,
                        ((gy * frameHeader.Upsampling) + iy) * groupDim,
                        rects[0],
                        rects[1],
                        rects[2]);
                }
            }
        }

        /// <summary>
        /// Decodes a single 10-bit quantized float parameter from <paramref name="br"/>.
        /// </summary>
        /// <param name="precision">Divisor precision factor.</param>
        /// <param name="val">Decoded floating-point value.</param>
        /// <param name="br">Bit reader.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeFloatParam(float precision, out float val, BitReader br)
        {
            int absvalQuant = (int)br.ReadFixedBits(10);
            val             = absvalQuant / precision;
        }

        /// <summary>
        /// Decodes the 8-entry <see cref="NoiseParams"/> lookup table from <paramref name="br"/>.
        /// Must only be called if <c>(frameHeader.Flags &amp; FrameHeader.KNoise) != 0</c>.
        /// </summary>
        /// <param name="br">Bit reader.</param>
        /// <param name="noiseParams">Destination noise parameters.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status DecodeNoise(BitReader br, NoiseParams noiseParams)
        {
            for (int i = 0; i < NoiseParams.KNumNoisePoints; i++)
            {
                DecodeFloatParam(NoiseParams.KNoisePrecision, out noiseParams.Lut[i], br);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FillBatch32(Xorshift128Plus rng, Span<ulong> batch64, Span<uint> batch32)
        {
            rng.Fill(batch64);
            MemoryMarshal.Cast<ulong, uint>(batch64).Slice(0, KFloatsPerBatch).CopyTo(batch32);
        }
    }
}
