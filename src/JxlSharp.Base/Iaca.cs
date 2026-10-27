// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// IACA (Intel's Code Analyzer) marker functions (ported from <c>lib/jxl/base/iaca.h</c>).
    /// They default to no-ops in normal builds.
    /// </summary>
    public static class Iaca
    {
        /// <summary>
        /// Call before the region of interest.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BeginIACA()
        {
        }

        /// <summary>
        /// Call after the region of interest.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EndIACA()
        {
        }
    }

    /// <summary>
    /// Add to a scope to mark a region for IACA analysis (ported from <c>jxl::ScopeIACA</c>).
    /// </summary>
    public readonly struct ScopeIACA : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeIACA"/> struct and invokes <see cref="Iaca.BeginIACA"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ScopeIACA()
        {
            Iaca.BeginIACA();
        }

        /// <summary>
        /// Invokes <see cref="Iaca.EndIACA"/> at the end of the scope.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Iaca.EndIACA();
        }
    }
}
