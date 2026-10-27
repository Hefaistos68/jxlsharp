// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base
{
    /// <summary>
    /// Unit tests for <see cref="Status"/> and <see cref="StatusOr{T}"/>.
    /// </summary>
    [TestFixture]
    public class StatusTests
    {
        /// <summary>
        /// Verifies <see cref="Status"/> creation, conversions, and fatal error checks.
        /// </summary>
        [Test]
        public void Status_ConversionsAndErrorChecks_ShouldBehaveCorrectly()
        {
            Status ok      = Status.OkStatus;
            Status fromTrue = true;
            Status fail    = Status.Failure("error");
            Status unsup   = Status.Unsupported("unsupported");
            Status needMore = Status.NotEnoughBytes("need more");
            Status fromFalse = false;
            Status fromCode = StatusCode.Unsupported;

            ok.IsOk.ShouldBeTrue();
            ((bool)ok).ShouldBeTrue();
            ok.IsFatalError().ShouldBeFalse();
            ok.Code.ShouldBe(StatusCode.Ok);
            ok.ToString().ShouldBe("Ok");

            (ok == fromTrue).ShouldBeTrue();
            (ok != fail).ShouldBeTrue();
            ok.Equals((object)fromTrue).ShouldBeTrue();
            ok.Equals((object?)null).ShouldBeFalse();
            ok.GetHashCode().ShouldBe(fromTrue.GetHashCode());

            fail.IsOk.ShouldBeFalse();
            ((bool)fail).ShouldBeFalse();
            fail.IsFatalError().ShouldBeTrue();
            (fail == fromFalse).ShouldBeTrue();

            unsup.IsOk.ShouldBeFalse();
            unsup.IsFatalError().ShouldBeTrue();
            (unsup == fromCode).ShouldBeTrue();

            needMore.IsOk.ShouldBeFalse();
            needMore.IsFatalError().ShouldBeFalse();
            needMore.Code.ShouldBe(StatusCode.NotEnoughBytes);
        }

        /// <summary>
        /// Verifies <see cref="StatusOr{T}"/> value and error handling.
        /// </summary>
        [Test]
        public void StatusOr_ValueAndErrorStates_ShouldBehaveCorrectly()
        {
            StatusOr<int> success      = 42;
            StatusOr<int> failedByCode = StatusCode.GenericError;
            StatusOr<int> failedStatus = Status.NotEnoughBytes();

            success.IsOk.ShouldBeTrue();
            success.Status.ShouldBe(Status.OkStatus);
            success.Value.ShouldBe(42);

            failedByCode.IsOk.ShouldBeFalse();
            failedByCode.Status.Code.ShouldBe(StatusCode.GenericError);
            Should.Throw<InvalidOperationException>(() => _ = failedByCode.Value);

            failedStatus.IsOk.ShouldBeFalse();
            failedStatus.Status.Code.ShouldBe(StatusCode.NotEnoughBytes);
            Should.Throw<InvalidOperationException>(() => _ = failedStatus.Value);

            Should.Throw<ArgumentException>(() => _ = new StatusOr<int>(StatusCode.Ok));
        }
    }
}
