// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Parses an ICC profile and populates <paramref name="c"/> and <paramref name="cmyk"/> with the data.
    /// </summary>
    /// <param name="userData"><see cref="JxlCmsInterface.SetFieldsData"/> passed as-is.</param>
    /// <param name="iccData">The ICC data to parse.</param>
    /// <param name="c">A <see cref="JxlColorEncoding"/> to populate if applicable.</param>
    /// <param name="cmyk">Set to whether the colorspace is a CMYK colorspace.</param>
    /// <returns>Whether the relevant fields in <paramref name="c"/> were successfully populated.</returns>
    public delegate bool JxlCmsSetFieldsFromIccFunc(
        object? userData,
        ReadOnlySpan<byte> iccData,
        ref JxlColorEncoding c,
        out bool cmyk);

    /// <summary>
    /// Allocates and returns the data needed for <paramref name="numThreads"/> parallel transforms
    /// from the <paramref name="inputProfile"/> colorspace to <paramref name="outputProfile"/>.
    /// </summary>
    /// <param name="initData"><see cref="JxlCmsInterface.InitData"/> passed as-is.</param>
    /// <param name="numThreads">Maximum number of threads from which <see cref="JxlCmsInterface.Run"/> will be called.</param>
    /// <param name="pixelsPerThread">Maximum number of pixels that each call to <see cref="JxlCmsInterface.Run"/> will transform.</param>
    /// <param name="inputProfile">Input colorspace for the transform.</param>
    /// <param name="outputProfile">Output colorspace for the transform.</param>
    /// <param name="intensityTarget">Luminance in nits at which (1, 1, 1) will be displayed.</param>
    /// <returns>Transform state object, or <c>null</c> in case of failure.</returns>
    public delegate object? JxlCmsInitFunc(
        object? initData,
        nuint numThreads,
        nuint pixelsPerThread,
        JxlColorProfile inputProfile,
        JxlColorProfile outputProfile,
        float intensityTarget);

    /// <summary>
    /// Returns a buffer that can be used by callers of the interface to store the
    /// input of the conversion or read its result.
    /// </summary>
    /// <param name="userData">The data returned by <see cref="JxlCmsInitFunc"/>.</param>
    /// <param name="thread">The index of the thread for which to return a buffer.</param>
    /// <returns>A buffer that can be used by the caller for passing to <see cref="JxlCmsRunFunc"/>.</returns>
    public delegate float[]? JxlCmsGetBufferFunc(object? userData, nuint thread);

    /// <summary>
    /// Executes one colorspace transform and returns <c>true</c> on success or <c>false</c> on error.
    /// </summary>
    /// <param name="userData">The data returned by <see cref="JxlCmsInitFunc"/>.</param>
    /// <param name="thread">The index of the thread from which the function is being called.</param>
    /// <param name="inputBuffer">The buffer containing the pixel data to be transformed.</param>
    /// <param name="outputBuffer">The buffer receiving the transformed pixel data.</param>
    /// <param name="numPixels">The number of pixels to transform.</param>
    /// <returns><c>true</c> on success; <c>false</c> on failure.</returns>
    public delegate bool JxlCmsRunFunc(
        object? userData,
        nuint thread,
        ReadOnlySpan<float> inputBuffer,
        Span<float> outputBuffer,
        nuint numPixels);

    /// <summary>
    /// Performs the necessary clean-up and frees the resources allocated for user data.
    /// </summary>
    /// <param name="userData">The data returned by <see cref="JxlCmsInitFunc"/>.</param>
    public delegate void JxlCmsDestroyFunc(object? userData);

    /// <summary>
    /// Represents an input or output colorspace to a color transform, as a serialized ICC profile
    /// (ported from <c>include/jxl/cms_interface.h</c>).
    /// </summary>
    public sealed class JxlColorProfile
    {
        /// <summary>
        /// Gets or sets the serialized ICC profile bytes.
        /// </summary>
        public byte[] IccData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets or sets the serialized ICC profile bytes (alias for <see cref="IccData"/>).
        /// </summary>
        public byte[] Icc
        {
            get => IccData;
            set => IccData = value;
        }

        /// <summary>
        /// Gets or sets the structured representation of the colorspace, if applicable.
        /// </summary>
        public JxlColorEncoding ColorEncoding { get; set; }

        /// <summary>
        /// Gets or sets the number of components per pixel.
        /// </summary>
        public nuint NumChannels { get; set; }
    }

    /// <summary>
    /// Interface for performing colorspace transforms
    /// (ported from <c>include/jxl/cms_interface.h</c>).
    /// </summary>
    public sealed class JxlCmsInterface
    {
        /// <summary>
        /// CMS-specific data that will be passed to <see cref="SetFieldsFromIcc"/>.
        /// </summary>
        public object? SetFieldsData { get; set; }

        /// <summary>
        /// Populates a <see cref="JxlColorEncoding"/> from an ICC profile.
        /// </summary>
        public JxlCmsSetFieldsFromIccFunc? SetFieldsFromIcc { get; set; }

        /// <summary>
        /// CMS-specific data that will be passed to <see cref="Init"/>.
        /// </summary>
        public object? InitData { get; set; }

        /// <summary>
        /// Prepares a colorspace transform.
        /// </summary>
        public JxlCmsInitFunc? Init { get; set; }

        /// <summary>
        /// Returns a buffer that can be used as input to <see cref="Run"/>.
        /// </summary>
        public JxlCmsGetBufferFunc? GetSrcBuf { get; set; }

        /// <summary>
        /// Returns a buffer that can be used as output from <see cref="Run"/>.
        /// </summary>
        public JxlCmsGetBufferFunc? GetDstBuf { get; set; }

        /// <summary>
        /// Executes the transform on a batch of pixels.
        /// </summary>
        public JxlCmsRunFunc? Run { get; set; }

        /// <summary>
        /// Cleans up the transform.
        /// </summary>
        public JxlCmsDestroyFunc? Destroy { get; set; }
    }
}
