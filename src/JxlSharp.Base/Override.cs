// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// 'Trool' for command line arguments: force enable/disable, or use default.

using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// No effect if Default, otherwise forces a feature (typically a FrameHeader
    /// flag) on or off.
    /// </summary>
    public enum Override : sbyte
    {
        /// <summary>
        /// Use the default setting.
        /// </summary>
        Default = -1,

        /// <summary>
        /// Force the feature off.
        /// </summary>
        Off = 0,

        /// <summary>
        /// Force the feature on.
        /// </summary>
        On = 1,
    }

    /// <summary>
    /// Helper operations for <see cref="Override"/> values.
    /// </summary>
    public static class OverrideHelper
    {
        /// <summary>
        /// Converts a boolean flag into an <see cref="Override"/> value.
        /// </summary>
        /// <param name="flag">Boolean flag.</param>
        /// <returns><see cref="Override.On"/> if <c>true</c>; otherwise, <see cref="Override.Off"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Override OverrideFromBool(bool flag)
        {
            return flag ? Override.On : Override.Off;
        }

        /// <summary>
        /// Applies an <see cref="Override"/> to a default boolean condition.
        /// </summary>
        /// <param name="o">Override value.</param>
        /// <param name="defaultCondition">Default condition when <paramref name="o"/> is <see cref="Override.Default"/>.</param>
        /// <returns>The resolved boolean condition.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ApplyOverride(Override o, bool defaultCondition)
        {
            if (o == Override.On)
            {
                return true;
            }

            if (o == Override.Off)
            {
                return false;
            }

            return defaultCondition;
        }
    }
}
