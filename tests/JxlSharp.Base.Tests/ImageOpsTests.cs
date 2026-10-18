// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Numerics;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ImageOps"/> (ported from <c>lib/jxl/image_ops_test.cc</c>).
    /// </summary>
    [TestFixture]
    public class ImageOpsTests
    {
        /// <summary>
        /// Ported from <c>ImageTest.TestFill</c> in <c>lib/jxl/image_ops_test.cc</c>.
        /// Ensure y/c/x and c/y/x loops visit pixels no more than once.
        /// </summary>
        [Test]
        public void TestFill()
        {
            TestFillT<byte>().IsOk.ShouldBeTrue();
            TestFillT<short>().IsOk.ShouldBeTrue();
            TestFillT<float>().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Ported from <c>ImageTest.CopyImageToWithPaddingTest</c> in <c>lib/jxl/image_ops_test.cc</c>.
        /// </summary>
        [Test]
        public unsafe void CopyImageToWithPaddingTest()
        {
            StatusOr<ImagePlane<uint>> srcResult = ImagePlane<uint>.Create(JxlMemoryManager.Default, 100, 61);
            srcResult.IsOk.ShouldBeTrue();
            using ImagePlane<uint> src = srcResult.Value;

            for (nuint y = 0; y < src.YSize; y++)
            {
                for (nuint x = 0; x < src.XSize; x++)
                {
                    src.Row(y)[x] = (uint)((x * 1000) + y);
                }
            }

            Rect srcRect = new Rect(10, 20, 30, 40);
            srcRect.IsInside(src).ShouldBeTrue();

            StatusOr<ImagePlane<uint>> dstResult = ImagePlane<uint>.Create(JxlMemoryManager.Default, 60, 50);
            dstResult.IsOk.ShouldBeTrue();
            using ImagePlane<uint> dst = dstResult.Value;

            ImageOps.FillImage(0u, dst);
            Rect dstRect = new Rect(20, 5, 30, 40);
            dstRect.IsInside(dst).ShouldBeTrue();

            ImageOps.CopyImageToWithPadding(srcRect, src, 2, dstRect, dst).IsOk.ShouldBeTrue();

            // ysize is + 3 instead of + 4 because we are at the y image boundary on the
            // source image.
            Rect paddedDstRect = new Rect(20 - 2, 5 - 2, 30 + 4, 40 + 3);

            for (nuint y = 0; y < dst.YSize; y++)
            {
                for (nuint x = 0; x < dst.XSize; x++)
                {
                    if (new Rect((int)x, (int)y, 1, 1).IsInside(paddedDstRect))
                    {
                        uint expected = (uint)((((int)x - dstRect.X0 + srcRect.X0) * 1000) +
                                               ((int)y - dstRect.Y0 + srcRect.Y0));
                        dst.Row(y)[x].ShouldBe(expected);
                    }
                    else
                    {
                        dst.Row(y)[x].ShouldBe(0u);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="ImageOps.LinComb{T}"/>, <see cref="ImageOps.ScaleImage{T}(T, ImagePlane{T})"/>,
        /// <see cref="ImageOps.ImageMinMax{T}"/>, <see cref="ImageOps.Mirror"/>, <see cref="ImageOps.PadImageToBlockMultipleInPlace"/>,
        /// and <see cref="ImageOps.DownsampleImage(ImagePlane{float}, nuint)"/>.
        /// </summary>
        [Test]
        public unsafe void AdditionalImageOpsFunctionsWork()
        {
            using ImagePlane<float> p1 = ImagePlane<float>.Create(JxlMemoryManager.Default, 4, 4).Value;
            using ImagePlane<float> p2 = ImagePlane<float>.Create(JxlMemoryManager.Default, 4, 4).Value;

            ImageOps.FillImage(2.0f, p1);
            ImageOps.FillImage(3.0f, p2);
            ImageOps.ScaleImage(2.0f, p1);

            using ImagePlane<float> comb = ImageOps.LinComb(1.0f, p1, 2.0f, p2).Value;
            comb.Row(0)[0].ShouldBe(10.0f);

            comb.Row(1)[2] = -5.0f;
            comb.Row(3)[3] = 25.0f;
            ImageOps.ImageMinMax(comb, out float min, out float max);
            min.ShouldBe(-5.0f);
            max.ShouldBe(25.0f);

            WrapMirror mirror       = default;
            WrapUnchanged unchanged = default;
            mirror.Invoke(-1, 10).ShouldBe(0L);
            mirror.Invoke(10, 10).ShouldBe(9L);
            unchanged.Invoke(-3, 10).ShouldBe(-3L);

            using ImagePlane<float> ds = ImageOps.DownsampleImage(p2, 2).Value;
            ds.XSize.ShouldBe((nuint)2);
            ds.YSize.ShouldBe((nuint)2);
            ds.Row(0)[0].ShouldBe(3.0f);

            using Image3<float> img3 = Image3<float>.Create(JxlMemoryManager.Default, 8, 8).Value;
            img3.ShrinkTo(5, 5).IsOk.ShouldBeTrue();
            ImageOps.FillImage(1.0f, img3);
            img3.PlaneRow(0, 4)[4] = 9.0f;

            ImageOps.PadImageToBlockMultipleInPlace(img3, 8).IsOk.ShouldBeTrue();
            img3.XSize.ShouldBe((nuint)8);
            img3.YSize.ShouldBe((nuint)8);
            img3.PlaneRow(0, 7)[7].ShouldBe(9.0f);

            using Image3<float> ds3 = ImageOps.DownsampleImage(img3, 2).Value;
            ds3.XSize.ShouldBe((nuint)4);
            ds3.YSize.ShouldBe((nuint)4);
        }

        private static Status TestFillT<T>()
            where T : unmanaged, INumber<T>
        {
            uint[] sizes = [0, 1, 15, 16, 31, 32];

            foreach (uint xsize in sizes)
            {
                foreach (uint ysize in sizes)
                {
                    StatusOr<Image3<T>> imgResult = Image3<T>.Create(JxlMemoryManager.Default, xsize, ysize);

                    if (!imgResult.IsOk)
                    {
                        return imgResult.Status;
                    }

                    using Image3<T> image = imgResult.Value;
                    Status status         = TestFillImpl(image);

                    if (!status)
                    {
                        return status;
                    }
                }
            }

            return true;
        }

        private static unsafe Status TestFillImpl<T>(Image3<T> img)
            where T : unmanaged, INumber<T>
        {
            T one   = T.One;
            T two   = one + one;
            T three = two + one;

            ImageOps.FillImage(one, img);

            for (nuint y = 0; y < img.YSize; ++y)
            {
                for (nuint c = 0; c < 3; ++c)
                {
                    T* row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < img.XSize; ++x)
                    {
                        if (row[x] != one)
                        {
                            return Status.Failure("Expected value 1");
                        }

                        row[x] = two;
                    }
                }
            }

            ImageOps.ZeroFillImage(img);

            for (nuint c = 0; c < 3; ++c)
            {
                for (nuint y = 0; y < img.YSize; ++y)
                {
                    T* row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < img.XSize; ++x)
                    {
                        if (row[x] != T.Zero)
                        {
                            return Status.Failure("Expected value 0");
                        }

                        row[x] = three;
                    }
                }
            }

            return true;
        }
    }
}
