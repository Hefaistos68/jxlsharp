// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using JxlSharp.Base;
using JxlSharp.Cms;
using JxlSharp.Decoder;
using JxlSharp.Encoder;
using JxlSharp.Threads;

namespace JxlSharp
{
    /// <summary>
    /// Unified public facade for JPEG XL (<c>libjxl</c>) encoding, decoding, color management, and multithreading.
    /// </summary>
    public static class JxlCodec
    {
        /// <summary>
        /// Gets the numeric library version (<c>0x000D00</c> for v0.13.0).
        /// </summary>
        public static uint Version => JxlDecoder.Version();

        /// <summary>
        /// Checks whether <paramref name="buf"/> starts with a valid JPEG XL codestream or container signature.
        /// </summary>
        /// <param name="buf">Input byte buffer to inspect.</param>
        /// <returns>The detected <see cref="JxlSignature"/>.</returns>
        public static JxlSignature CheckSignature(ReadOnlySpan<byte> buf)
        {
            return JxlDecoder.CheckSignature(buf);
        }

        /// <summary>
        /// Creates a new <see cref="JxlDecoder"/> instance.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlDecoder"/>.</returns>
        public static JxlDecoder CreateDecoder(JxlMemoryManager? memoryManager = null)
        {
            return JxlDecoder.Create(memoryManager);
        }

        /// <summary>
        /// Creates a new <see cref="JxlEncoder"/> instance.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <returns>A newly initialized <see cref="JxlEncoder"/>.</returns>
        public static JxlEncoder CreateEncoder(JxlMemoryManager? memoryManager = null)
        {
            return JxlEncoder.Create(memoryManager);
        }

        /// <summary>
        /// Returns the default JPEG XL Color Management System (<see cref="JxlCmsInterface"/>) implementation.
        /// </summary>
        /// <returns>Default <see cref="JxlCmsInterface"/>.</returns>
        public static JxlCmsInterface GetDefaultCms()
        {
            return JxlCms.GetDefaultCms();
        }

        /// <summary>
        /// Creates a new <see cref="ThreadParallelRunner"/> with the specified number of worker threads.
        /// </summary>
        /// <param name="memoryManager">Optional custom memory manager.</param>
        /// <param name="numWorkerThreads">Optional worker thread count (defaults to logical processor count).</param>
        /// <returns>A newly initialized <see cref="ThreadParallelRunner"/>.</returns>
        public static ThreadParallelRunner? CreateThreadParallelRunner(
            JxlMemoryManager? memoryManager = null,
            nuint? numWorkerThreads = null)
        {
            nuint workers = numWorkerThreads ?? ThreadParallelRunner.DefaultNumWorkerThreads();

            return ThreadParallelRunner.Create(memoryManager, workers);
        }
    }
}
