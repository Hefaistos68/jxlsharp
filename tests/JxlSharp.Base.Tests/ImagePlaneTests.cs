// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ImagePlane{T}"/> and <see cref="Image3{T}"/>.
    /// </summary>
    [TestFixture]
    public class ImagePlaneTests
    {
        /// <summary>
        /// Verifies <see cref="ImagePlane{T}"/> creation, <see cref="PlaneBase.ShrinkTo"/>, <see cref="PlaneBase.Swap"/>,
        /// and <see cref="ImagePlane{T}.Move"/>.
        /// </summary>
        [Test]
        public unsafe void ImagePlaneCreateShrinkSwapAndMoveWork()
        {
            StatusOr<ImagePlane<float>> planeResult = ImagePlane<float>.Create(JxlMemoryManager.Default, 16, 8);
            planeResult.IsOk.ShouldBeTrue();

            using ImagePlane<float> plane = planeResult.Value;
            plane.XSize.ShouldBe((nuint)16);
            plane.YSize.ShouldBe((nuint)8);
            plane.PixelsPerRow.ShouldBeGreaterThanOrEqualTo(16);

            plane.Row(0)[0] = 3.5f;
            plane.ConstRow(0)[0].ShouldBe(3.5f);
            plane.RowSpan(0)[0].ShouldBe(3.5f);

            plane.ShrinkTo(8, 4).IsOk.ShouldBeTrue();
            plane.XSize.ShouldBe((nuint)8);
            plane.YSize.ShouldBe((nuint)4);

            plane.ShrinkTo(16, 8).IsOk.ShouldBeTrue();
            plane.ShrinkTo(32, 8).IsOk.ShouldBeFalse();

            using ImagePlane<float> moved = plane.Move();
            plane.XSize.ShouldBe((nuint)0);
            moved.XSize.ShouldBe((nuint)16);
            moved.Row(0)[0].ShouldBe(3.5f);
        }

        /// <summary>
        /// Verifies <see cref="Image3{T}"/> creation, <see cref="Image3{T}.ShrinkTo"/>, <see cref="Image3{T}.Swap"/>,
        /// and <see cref="Image3{T}.Move"/>.
        /// </summary>
        [Test]
        public unsafe void Image3CreateShrinkSwapAndMoveWork()
        {
            StatusOr<Image3<int>> imgResult = Image3<int>.Create(JxlMemoryManager.Default, 10, 6);
            imgResult.IsOk.ShouldBeTrue();

            using Image3<int> img = imgResult.Value;
            img.XSize.ShouldBe((nuint)10);
            img.YSize.ShouldBe((nuint)6);

            img.PlaneRow(2, 1)[3] = 77;
            img.ConstPlaneRow(2, 1)[3].ShouldBe(77);
            img.PlaneRowSpan(2, 1)[3].ShouldBe(77);

            img.ShrinkTo(5, 3).IsOk.ShouldBeTrue();
            img.XSize.ShouldBe((nuint)5);
            img.YSize.ShouldBe((nuint)3);

            using Image3<int> moved = img.Move();
            img.XSize.ShouldBe((nuint)0);
            moved.XSize.ShouldBe((nuint)5);
            moved.PlaneRow(2, 1)[3].ShouldBe(77);
        }
    }
}
