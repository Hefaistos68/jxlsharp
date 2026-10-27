// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Rect"/>.
    /// </summary>
    [TestFixture]
    public class RectTests
    {
        /// <summary>
        /// Verifies <see cref="Rect"/> construction, clamping, intersection, and transformations.
        /// </summary>
        [Test]
        public void Rect_OperationsAndTransformations_ShouldBehaveCorrectly()
        {
            Rect r1      = new Rect(10, 20, 30, 40);
            Rect clamped = new Rect(10, 20, 30, 40, 25, 35);
            Rect empty   = new Rect(50, 50, 10, 10, 40, 40);

            r1.X0.ShouldBe(10);
            r1.Y0.ShouldBe(20);
            r1.XSize.ShouldBe(30);
            r1.YSize.ShouldBe(40);
            r1.X1.ShouldBe(40);
            r1.Y1.ShouldBe(60);
            r1.Description().ShouldBe("[10..40)x[20..60)");
            r1.ToString().ShouldBe("[10..40)x[20..60)");

            clamped.XSize.ShouldBe(15);
            clamped.YSize.ShouldBe(15);
            empty.XSize.ShouldBe(0);
            empty.YSize.ShouldBe(0);

            Rect cropped = r1.Crop(25, 35);
            cropped.ShouldBe(new Rect(10, 20, 15, 15));

            Rect translated = r1.Translate(5, -5);
            translated.ShouldBe(new Rect(15, 15, 30, 40));

            cropped.IsInside(r1).ShouldBeTrue();
            r1.IsInside(cropped).ShouldBeFalse();

            Rect shifted = new Rect(2, 4, 8, 16).ShiftLeft(1);
            shifted.ShouldBe(new Rect(4, 8, 16, 32));

            StatusOr<Rect> ceilRightOk = shifted.CeilShiftRight(1, 1);
            ceilRightOk.IsOk.ShouldBeTrue();
            ceilRightOk.Value.ShouldBe(new Rect(2, 4, 8, 16));

            StatusOr<Rect> ceilRightFail = new Rect(3, 4, 8, 16).CeilShiftRight(1, 1);
            ceilRightFail.IsOk.ShouldBeFalse();

            Rect parent   = new Rect(0, 0, 100, 100);
            Rect extended = new Rect(5, 5, 10, 10).Extend(10, parent);
            extended.ShouldBe(new Rect(0, 0, 25, 25));

            Rect extendedInner = new Rect(20, 20, 10, 10).Extend(5, parent);
            extendedInner.ShouldBe(new Rect(15, 15, 20, 20));

            (r1 == new Rect(10, 20, 30, 40)).ShouldBeTrue();
            (r1 != clamped).ShouldBeTrue();
            r1.Equals((object)new Rect(10, 20, 30, 40)).ShouldBeTrue();
            r1.Equals((object?)null).ShouldBeFalse();
            r1.GetHashCode().ShouldBe(new Rect(10, 20, 30, 40).GetHashCode());
        }
    }
}
