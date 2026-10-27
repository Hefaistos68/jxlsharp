// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Runtime.CompilerServices;

namespace JxlSharp.Base
{
    /// <summary>
    /// Inverse move-to-front transform (ported from <c>lib/jxl/inverse_mtf-inl.h</c>).
    /// </summary>
    public static class InverseMtf
    {
        /// <summary>
        /// Moves the element at <paramref name="index"/> to the front of <paramref name="v"/>,
        /// shifting elements <c>0..index-1</c> right by 1.
        /// </summary>
        /// <param name="v">Move-to-front lookup table span.</param>
        /// <param name="index">Index of the element to move to index 0.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MoveToFront(Span<byte> v, byte index)
        {
            byte value = v[index];
            byte i     = index;

            if (i < 4)
            {
                for (; i != 0; --i)
                {
                    v[i] = v[i - 1];
                }
            }
            else
            {
                v.Slice(0, index).CopyTo(v.Slice(1, index));
            }

            v[0] = value;
        }

        /// <summary>
        /// Applies the in-place inverse move-to-front transform to the first <paramref name="vLen"/> bytes of <paramref name="v"/>.
        /// </summary>
        /// <param name="v">Buffer of indices to transform in-place.</param>
        /// <param name="vLen">Number of bytes in <paramref name="v"/> to transform.</param>
        public static void InverseMoveToFrontTransform(Span<byte> v, int vLen)
        {
            if (vLen <= 0)
            {
                return;
            }

            Span<byte> mtf = stackalloc byte[256 + 64];

            for (int i = 0; i < 256; ++i)
            {
                mtf[i] = (byte)i;
            }

            for (int i = 0; i < vLen; ++i)
            {
                byte index = v[i];
                v[i]       = mtf[index];

                if (index != 0)
                {
                    MoveToFront(mtf, index);
                }
            }
        }
    }
}
