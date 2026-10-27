// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Threads;

namespace JxlSharp
{
    /// <summary>
    /// High-level factory and C++ wrapper equivalents (<c>decode_cxx.h</c> and <c>encode_cxx.h</c>)
    /// for creating and managing <see cref="JxlDecoder"/>, <see cref="JxlEncoder"/>,
    /// <see cref="ThreadParallelRunner"/>, and <see cref="ResizableParallelRunner"/> instances.
    /// </summary>
    public static class JxlCodecFacade
    {
        /// <summary>
        /// Returns the decoder library version number (<c>JxlDecoderVersion</c>).
        /// </summary>
        /// <returns>The decoder version as <c>MAJOR * 1000000 + MINOR * 1000 + PATCH</c>.</returns>
        public static uint DecoderVersion()
        {
            return JxlDecoder.Version();
        }

        /// <summary>
        /// Returns the encoder library version number (<c>JxlEncoderVersion</c>).
        /// </summary>
        /// <returns>The encoder version as <c>MAJOR * 1000000 + MINOR * 1000 + PATCH</c>.</returns>
        public static uint EncoderVersion()
        {
            return JxlEncoder.Version();
        }

        /// <summary>
        /// Creates a new <see cref="JxlDecoder"/> instance (<c>JxlDecoderMake</c>).
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlDecoder"/>.</returns>
        public static JxlDecoder CreateDecoder(JxlMemoryManager? memoryManager = null)
        {
            return new JxlDecoder(memoryManager);
        }

        /// <summary>
        /// Creates a new <see cref="JxlEncoder"/> instance (<c>JxlEncoderMake</c>).
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlEncoder"/>.</returns>
        public static JxlEncoder CreateEncoder(JxlMemoryManager? memoryManager = null)
        {
            return new JxlEncoder(memoryManager);
        }

        /// <summary>
        /// Creates a new <see cref="ThreadParallelRunner"/> (<c>JxlThreadParallelRunnerMake</c>).
        /// </summary>
        /// <param name="numWorkerThreads">Optional worker thread count; defaults to processor count when null or zero.</param>
        /// <returns>A disposable <see cref="ThreadParallelRunner"/>.</returns>
        public static ThreadParallelRunner CreateThreadParallelRunner(int? numWorkerThreads = null)
        {
            return new ThreadParallelRunner(numWorkerThreads.GetValueOrDefault(-1));
        }

        /// <summary>
        /// Creates a new <see cref="ResizableParallelRunner"/> (<c>JxlResizableParallelRunnerMake</c>).
        /// </summary>
        /// <returns>A disposable <see cref="ResizableParallelRunner"/>.</returns>
        public static ResizableParallelRunner CreateResizableParallelRunner()
        {
            return new ResizableParallelRunner();
        }

        /// <summary>
        /// Returns the default JPEG XL color management system interface (<c>JxlGetDefaultCms</c>).
        /// </summary>
        /// <returns>The default <see cref="JxlCmsInterface"/> instance.</returns>
        public static JxlCmsInterface GetDefaultCms()
        {
            return JxlCms.GetDefaultCms();
        }
    }
}
