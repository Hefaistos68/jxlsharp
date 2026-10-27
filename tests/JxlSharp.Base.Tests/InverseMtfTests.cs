// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for <see cref="InverseMtf"/>.
    /// </summary>
    [TestFixture]
    public class InverseMtfTests
    {
        /// <summary>
        /// Verifies that <see cref="InverseMtf.InverseMoveToFrontTransform(System.Span{byte}, int)"/>
        /// accurately decodes both small (&lt; 4) and large (&gt;= 4) MTF indices.
        /// </summary>
        [Test]
        public void InverseMoveToFrontTransformWorks()
        {
            // Initial MTF table: [0, 1, 2, ..., 255]
            // Index 0 -> 0, table unchanged
            // Index 2 -> 2, table becomes [2, 0, 1, 3, ...]
            // Index 0 -> 2, table unchanged
            // Index 10 -> 10, table becomes [10, 2, 0, 1, 3, ..., 9, 11, ...]
            // Index 1 -> 2, table becomes [2, 10, 0, 1, ...]
            byte[] data = [0, 2, 0, 10, 1];
            InverseMtf.InverseMoveToFrontTransform(data, data.Length);
            data.ShouldBe(new byte[] { 0, 2, 2, 10, 2 });

            byte[] spanData = [50, 0, 1];
            InverseMtf.InverseMoveToFrontTransform(spanData, spanData.Length);
            spanData.ShouldBe(new byte[] { 50, 50, 0 });
        }
    }
}
