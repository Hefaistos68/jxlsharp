// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Safe managed reference to a row within a pipeline channel buffer.
    /// </summary>
    public readonly struct PipelineRow
    {
        /// <summary>Backing float array for the plane or scratch buffer.</summary>
        public readonly float[] Buffer;

        /// <summary>Element offset of position 0 within <see cref="Buffer"/>.</summary>
        public readonly int Offset;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineRow"/> struct from a backing array and element offset.
        /// </summary>
        /// <param name="buffer">Backing float array.</param>
        /// <param name="offset">Element offset within <paramref name="buffer"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PipelineRow(float[] buffer, int offset)
        {
            Buffer = buffer;
            Offset = offset;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineRow"/> struct for row <paramref name="y"/> of <paramref name="plane"/>.
        /// </summary>
        /// <param name="plane">Source or destination float plane.</param>
        /// <param name="y">Row index.</param>
        /// <param name="xOffset">Optional horizontal element offset.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PipelineRow(ImagePlane<float> plane, nuint y, int xOffset = 0)
            : this(plane.BackingArray, plane.RowOffset(y) + xOffset)
        {
        }

        /// <summary>Gets a value indicating whether this row reference has no backing buffer.</summary>
        public bool IsEmpty => Buffer is null || Buffer.Length == 0;

        /// <summary>Gets a reference to the sample at horizontal offset <paramref name="x"/>.</summary>
        /// <param name="x">Horizontal offset relative to <see cref="Offset"/>.</param>
        /// <returns>Reference to the float sample.</returns>
        public ref float this[int x]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffer[Offset + x];
        }

        /// <summary>Gets a reference to the sample at horizontal offset <paramref name="x"/>.</summary>
        /// <param name="x">Horizontal offset relative to <see cref="Offset"/>.</param>
        /// <returns>Reference to the float sample.</returns>
        public ref float this[nint x]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffer[Offset + (int)x];
        }

        /// <summary>Gets a reference to the sample at horizontal offset <paramref name="x"/>.</summary>
        /// <param name="x">Horizontal offset relative to <see cref="Offset"/>.</param>
        /// <returns>Reference to the float sample.</returns>
        public ref float this[nuint x]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffer[Offset + (int)x];
        }

        /// <summary>Offsets <paramref name="row"/> forward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator +(PipelineRow row, int delta) => new PipelineRow(row.Buffer, row.Offset + delta);

        /// <summary>Offsets <paramref name="row"/> forward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator +(PipelineRow row, nint delta) => new PipelineRow(row.Buffer, row.Offset + (int)delta);

        /// <summary>Offsets <paramref name="row"/> forward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator +(PipelineRow row, nuint delta) => new PipelineRow(row.Buffer, row.Offset + (int)delta);

        /// <summary>Offsets <paramref name="row"/> backward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator -(PipelineRow row, int delta) => new PipelineRow(row.Buffer, row.Offset - delta);

        /// <summary>Offsets <paramref name="row"/> backward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator -(PipelineRow row, nint delta) => new PipelineRow(row.Buffer, row.Offset - (int)delta);

        /// <summary>Offsets <paramref name="row"/> backward by <paramref name="delta"/> elements.</summary>
        /// <param name="row">Source row.</param>
        /// <param name="delta">Element offset delta.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PipelineRow operator -(PipelineRow row, nuint delta) => new PipelineRow(row.Buffer, row.Offset - (int)delta);

        /// <summary>Returns a <see cref="Span{Single}"/> starting at <c>Offset + startOffset</c>.</summary>
        /// <param name="startOffset">Additional element offset.</param>
        /// <returns>Span over the backing buffer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<float> AsSpan(int startOffset = 0) =>
            Buffer is null ? Span<float>.Empty : Buffer.AsSpan(Offset + startOffset);

        /// <summary>Returns a <see cref="Span{Single}"/> of <paramref name="length"/> elements starting at <c>Offset + startOffset</c>.</summary>
        /// <param name="startOffset">Additional element offset.</param>
        /// <param name="length">Span length in elements.</param>
        /// <returns>Span over the backing buffer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<float> AsSpan(int startOffset, int length) =>
            Buffer is null ? Span<float>.Empty : Buffer.AsSpan(Offset + startOffset, length);
    }

    /// <summary>
    /// Channel processing mode for a <see cref="RenderPipelineStage"/>.
    /// Ported from <c>jxl::RenderPipelineChannelMode</c> in <c>lib/jxl/render_pipeline/render_pipeline_stage.h</c>.
    /// </summary>
    public enum RenderPipelineChannelMode
    {
        /// <summary>This channel is not modified by this stage.</summary>
        Ignored = 0,

        /// <summary>This channel is modified in-place.</summary>
        InPlace = 1,

        /// <summary>This channel is modified and written to a new buffer.</summary>
        InOut = 2,

        /// <summary>This channel is only read (has observable side effects).</summary>
        Input = 3,
    }

    /// <summary>
    /// Base class for a single stage in the JPEG XL rendering pipeline.
    /// Ported from <c>jxl::RenderPipelineStage</c> in <c>lib/jxl/render_pipeline/render_pipeline_stage.h</c>
    /// and <c>lib/jxl/render_pipeline/render_pipeline_stage.cc</c>.
    /// </summary>
    public abstract class RenderPipelineStage : IDisposable
    {
        /// <summary>
        /// Horizontal offset in floats to the first non-extra pixel in row buffers provided to <see cref="RenderPipelineStage"/>.
        /// </summary>
        public const int KRenderPipelineXOffset = 32;

        /// <summary>
        /// Stage border and upsampling shift configuration.
        /// Ported from <c>jxl::RenderPipelineStage::Settings</c>.
        /// </summary>
        public struct Settings
        {
            /// <summary>Horizontal border padding required for <see cref="RenderPipelineChannelMode.InOut"/> channels.</summary>
            public nuint BorderX;

            /// <summary>Vertical border padding required for <see cref="RenderPipelineChannelMode.InOut"/> channels.</summary>
            public nuint BorderY;

            /// <summary>Log2 horizontal upsampling factor for <see cref="RenderPipelineChannelMode.InOut"/> channels.</summary>
            public nuint ShiftX;

            /// <summary>Log2 vertical upsampling factor for <see cref="RenderPipelineChannelMode.InOut"/> channels.</summary>
            public nuint ShiftY;

            /// <summary>
            /// Creates horizontal-only shift and border settings.
            /// </summary>
            /// <param name="shift">Log2 horizontal shift.</param>
            /// <param name="border">Horizontal border.</param>
            /// <returns>Configured <see cref="Settings"/>.</returns>
            public static Settings CreateShiftX(nuint shift, nuint border)
            {
                return new Settings
                {
                    BorderX = border,
                    ShiftX  = shift,
                };
            }

            /// <summary>
            /// Creates vertical-only shift and border settings.
            /// </summary>
            /// <param name="shift">Log2 vertical shift.</param>
            /// <param name="border">Vertical border.</param>
            /// <returns>Configured <see cref="Settings"/>.</returns>
            public static Settings CreateShiftY(nuint shift, nuint border)
            {
                return new Settings
                {
                    BorderY = border,
                    ShiftY  = shift,
                };
            }

            /// <summary>
            /// Creates symmetric horizontal and vertical shift and border settings.
            /// </summary>
            /// <param name="shift">Log2 shift in both axes.</param>
            /// <param name="border">Border in both axes.</param>
            /// <returns>Configured <see cref="Settings"/>.</returns>
            public static Settings Symmetric(nuint shift, nuint border)
            {
                return new Settings
                {
                    BorderX = border,
                    BorderY = border,
                    ShiftX  = shift,
                    ShiftY  = shift,
                };
            }

            /// <summary>
            /// Creates symmetric border-only settings with zero shift.
            /// </summary>
            /// <param name="border">Border in both axes.</param>
            /// <returns>Configured <see cref="Settings"/>.</returns>
            public static Settings SymmetricBorderOnly(nuint border)
            {
                return Symmetric(0, border);
            }
        }

        /// <summary>
        /// Stage border and shift settings.
        /// </summary>
        public Settings StageSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderPipelineStage"/> class.
        /// </summary>
        /// <param name="settings">Stage settings.</param>
        protected RenderPipelineStage(Settings settings)
        {
            StageSettings = settings;
        }

        /// <summary>
        /// Processes one row of input across all active channels, producing <c>1 &lt;&lt; ShiftY</c> output rows.
        /// </summary>
        /// <param name="inputRows">Per-channel array of input rows (<c>[channel][borderY + dy]</c>).</param>
        /// <param name="outputRows">Per-channel array of output rows (<c>[channel][oy]</c>).</param>
        /// <param name="xextra">Extra pixels to process on the left and right (<c>xextra.Left, xextra.Right</c>).</param>
        /// <param name="xsize">Number of non-extra pixels to process.</param>
        /// <param name="xpos">Horizontal image coordinate of the first non-extra pixel.</param>
        /// <param name="ypos">Vertical image coordinate of the center input row.</param>
        /// <param name="threadId">Worker thread index.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public abstract Status ProcessRow(
            PipelineRow[][] inputRows,
            PipelineRow[][] outputRows,
            (nuint Left, nuint Right) xextra,
            nuint xsize,
            nuint xpos,
            nuint ypos,
            nuint threadId);

        /// <summary>
        /// Returns how channel <paramref name="c"/> is processed by this stage.
        /// </summary>
        /// <param name="c">Channel index.</param>
        /// <returns><see cref="RenderPipelineChannelMode"/> for channel <paramref name="c"/>.</returns>
        public abstract RenderPipelineChannelMode GetChannelMode(nuint c);

        /// <summary>
        /// Returns the diagnostic name of this stage.
        /// </summary>
        /// <returns>Stage name.</returns>
        public abstract string GetName();

        /// <summary>
        /// Returns whether the stage was initialized without error.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if initialized.</returns>
        public virtual Status IsInitialized() => true;

        /// <summary>
        /// Informs the stage about the total input size of each channel.
        /// </summary>
        /// <param name="inputSizes">Per-channel <c>(XSize, YSize)</c> list.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status SetInputSizes(IReadOnlyList<(nuint XSize, nuint YSize)> inputSizes) => true;

        /// <summary>
        /// Allocates per-thread scratch state for <paramref name="numThreads"/> worker threads.
        /// </summary>
        /// <param name="numThreads">Number of worker threads.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public virtual Status PrepareForThreads(nuint numThreads) => true;

        /// <summary>
        /// Indicates whether, from this stage onward, the pipeline operates on full image dimensions rather than frame dimensions.
        /// </summary>
        /// <returns><c>true</c> if switching to image dimensions; otherwise <c>false</c>.</returns>
        public virtual bool SwitchToImageDimensions() => false;

        /// <summary>
        /// If <see cref="SwitchToImageDimensions"/> returns <c>true</c>, outputs the full image dimensions and frame origin.
        /// </summary>
        /// <param name="xsize">Output full image width.</param>
        /// <param name="ysize">Output full image height.</param>
        /// <param name="frameOrigin">Output frame origin within the full image.</param>
        public virtual void GetImageDimensions(out nuint xsize, out nuint ysize, out FrameOrigin frameOrigin)
        {
            xsize       = 0;
            ysize       = 0;
            frameOrigin = default;
        }

        /// <summary>
        /// Produces output data for pixels outside of the frame dimensions when <see cref="SwitchToImageDimensions"/> is <c>true</c>.
        /// </summary>
        /// <param name="outputRows">Per-channel output rows.</param>
        /// <param name="xsize">Number of pixels to fill.</param>
        /// <param name="xpos">Horizontal full-image coordinate.</param>
        /// <param name="ypos">Vertical full-image coordinate.</param>
        public virtual void ProcessPaddingRow(
            PipelineRow[][] outputRows,
            nuint xsize,
            nuint xpos,
            nuint ypos)
        {
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Returns a <see cref="PipelineRow"/> to the input row of channel <paramref name="c"/> at vertical <paramref name="offset"/>,
        /// already offset by <see cref="KRenderPipelineXOffset"/>.
        /// </summary>
        /// <param name="inputRows">Input row table.</param>
        /// <param name="c">Channel index.</param>
        /// <param name="offset">Vertical offset in <c>[-BorderY, BorderY]</c>.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static PipelineRow GetInputRow(PipelineRow[][] inputRows, nuint c, int offset)
        {
            PipelineRow[] channelRows = inputRows[c];

            return channelRows[(channelRows.Length / 2) + offset] + KRenderPipelineXOffset;
        }

        /// <summary>
        /// Returns a <see cref="PipelineRow"/> to the output row of channel <paramref name="c"/> at vertical <paramref name="offset"/>,
        /// already offset by <see cref="KRenderPipelineXOffset"/>.
        /// </summary>
        /// <param name="outputRows">Output row table.</param>
        /// <param name="c">Channel index.</param>
        /// <param name="offset">Vertical output row index in <c>[0, 1 &lt;&lt; ShiftY)</c>.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static PipelineRow GetOutputRow(PipelineRow[][] outputRows, nuint c, int offset)
        {
            return outputRows[c][offset] + KRenderPipelineXOffset;
        }

        /// <summary>
        /// Returns a <see cref="PipelineRow"/> to the output row of channel <paramref name="c"/> at vertical <paramref name="offset"/>,
        /// already offset by <see cref="KRenderPipelineXOffset"/>.
        /// </summary>
        /// <param name="outputRows">Output row table.</param>
        /// <param name="c">Channel index.</param>
        /// <param name="offset">Vertical output row index in <c>[0, 1 &lt;&lt; ShiftY)</c>.</param>
        /// <returns>Offset <see cref="PipelineRow"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static PipelineRow GetOutputRow(PipelineRow[][] outputRows, nuint c, nuint offset)
        {
            return outputRows[c][(int)offset] + KRenderPipelineXOffset;
        }
    }

    /// <summary>
    /// Handle providing group input buffers to a <see cref="RenderPipeline"/>.
    /// Ported from <c>jxl::RenderPipelineInput</c> in <c>lib/jxl/render_pipeline/render_pipeline.h</c>.
    /// </summary>
    public sealed class RenderPipelineInput
    {
        internal RenderPipeline? Pipeline;
        internal nuint GroupId;
        internal nuint ThreadId;
        internal (ImagePlane<float> Plane, Rect Rect)[] Buffers = Array.Empty<(ImagePlane<float> Plane, Rect Rect)>();

        /// <summary>
        /// Returns the <c>(Plane, Rect)</c> buffer pair for channel <paramref name="c"/>.
        /// </summary>
        /// <param name="c">Channel index.</param>
        /// <returns>Tuple of <c>(Plane, Rect)</c>.</returns>
        public (ImagePlane<float> Plane, Rect Rect) GetBuffer(nuint c)
        {
            Debug.Assert(c < (nuint)Buffers.Length);

            return Buffers[c];
        }

        /// <summary>
        /// Signals that all input channel buffers for this group have been populated and triggers pipeline processing.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Done()
        {
            if (Pipeline == null)
            {
                return Status.Failure("RenderPipelineInput has no associated pipeline");
            }

            return Pipeline.InputReady(GroupId, ThreadId, Buffers);
        }
    }

    /// <summary>
    /// Base class for the JPEG XL rendering pipeline.
    /// Ported from <c>jxl::RenderPipeline</c> in <c>lib/jxl/render_pipeline/render_pipeline.h</c>
    /// and <c>lib/jxl/render_pipeline/render_pipeline.cc</c>.
    /// </summary>
    public abstract class RenderPipeline : IDisposable
    {
        /// <summary>
        /// Builder for configuring and finalizing a <see cref="RenderPipeline"/>.
        /// Ported from <c>jxl::RenderPipeline::Builder</c>.
        /// </summary>
        public sealed class Builder
        {
            private readonly JxlMemoryManager? _memoryManager;
            private readonly List<RenderPipelineStage> _stages = new List<RenderPipelineStage>();
            private readonly nuint _numC;
            private bool _useSimpleImplementation;

            /// <summary>
            /// Initializes a new instance of the <see cref="Builder"/> class.
            /// </summary>
            /// <param name="memoryManager">Memory manager used for buffer allocations.</param>
            /// <param name="numC">Total number of pipeline channels.</param>
            public Builder(JxlMemoryManager? memoryManager, nuint numC)
            {
                Debug.Assert(numC > 0);
                _memoryManager = memoryManager;
                _numC          = numC;
            }

            /// <summary>
            /// Adds a stage to the pipeline.
            /// </summary>
            /// <param name="stage">Stage to append.</param>
            /// <returns><see cref="Status.OkStatus"/> on success.</returns>
            public Status AddStage(RenderPipelineStage? stage)
            {
                if (stage == null)
                {
                    return Status.Failure("internal: no stage to add");
                }

                _stages.Add(stage);

                return true;
            }

            /// <summary>
            /// Enables using the simple (full-frame buffer) implementation of the pipeline.
            /// </summary>
            public void UseSimpleImplementation()
            {
                _useSimpleImplementation = true;
            }

            /// <summary>
            /// Finalizes setup of the pipeline for <paramref name="frameDimensions"/>.
            /// </summary>
            /// <param name="frameDimensions">Frame dimensions.</param>
            /// <returns><see cref="StatusOr{RenderPipeline}"/> containing the constructed pipeline.</returns>
            public StatusOr<RenderPipeline> Finalize(FrameDimensions frameDimensions)
            {
                if (_stages.Count == 0)
                {
                    return Status.Failure("RenderPipeline must have at least one stage");
                }

                RenderPipelineStage lastStage = _stages[_stages.Count - 1];

                for (nuint c = 0; c < _numC; c++)
                {
                    if (lastStage.GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                    {
                        return Status.Failure("Last stage in RenderPipeline cannot have InOut channel mode");
                    }
                }

                RenderPipeline res = _useSimpleImplementation
                    ? new SimpleRenderPipeline(_memoryManager)
                    : new LowMemoryRenderPipeline(_memoryManager);

                ComputePadding(res, _stages, _numC);
                res.FrameDimensions       = frameDimensions;
                res.GroupCompletedPasses  = new byte[frameDimensions.NumGroups];
                ComputeChannelShifts(res, _stages, _numC);

                res.Stages.AddRange(_stages);
                _stages.Clear();

                Status initStatus = res.Init();

                if (!initStatus)
                {
                    res.Dispose();

                    return initStatus;
                }

                return res;
            }

            private static void ComputePadding(
                RenderPipeline res,
                List<RenderPipelineStage> stages,
                nuint numC)
            {
                int numStages = stages.Count;
                res.Padding   = new (nuint X, nuint Y)[numStages][];

                for (int i = numStages - 1; i >= 0; i--)
                {
                    RenderPipelineStage stage = stages[i];
                    res.Padding[i]            = new (nuint X, nuint Y)[numC];

                    if (i + 1 == numStages)
                    {
                        continue;
                    }

                    for (nuint c = 0; c < numC; c++)
                    {
                        if (stage.GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                        {
                            nuint padX = Common.DivCeil(res.Padding[i + 1][c].X, 1u << (int)stage.StageSettings.ShiftX) +
                                         stage.StageSettings.BorderX;
                            nuint padY = Common.DivCeil(res.Padding[i + 1][c].Y, 1u << (int)stage.StageSettings.ShiftY) +
                                         stage.StageSettings.BorderY;
                            res.Padding[i][c] = (padX, padY);
                        }
                        else
                        {
                            res.Padding[i][c] = res.Padding[i + 1][c];
                        }
                    }
                }
            }

            private static void ComputeChannelShifts(
                RenderPipeline res,
                List<RenderPipelineStage> stages,
                nuint numC)
            {
                int numStages        = stages.Count;
                res.ChannelShifts    = new (nuint X, nuint Y)[numStages][];
                res.ChannelShifts[0] = new (nuint X, nuint Y)[numC];

                for (int i = 1; i < numStages; i++)
                {
                    RenderPipelineStage prev = stages[i - 1];

                    for (nuint c = 0; c < numC; c++)
                    {
                        if (prev.GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                        {
                            res.ChannelShifts[0][c].X += prev.StageSettings.ShiftX;
                            res.ChannelShifts[0][c].Y += prev.StageSettings.ShiftY;
                        }
                    }
                }

                for (int i = 1; i < numStages; i++)
                {
                    RenderPipelineStage prev = stages[i - 1];
                    res.ChannelShifts[i]     = new (nuint X, nuint Y)[numC];

                    for (nuint c = 0; c < numC; c++)
                    {
                        if (prev.GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                        {
                            res.ChannelShifts[i][c] = (
                                res.ChannelShifts[i - 1][c].X - prev.StageSettings.ShiftX,
                                res.ChannelShifts[i - 1][c].Y - prev.StageSettings.ShiftY);
                        }
                        else
                        {
                            res.ChannelShifts[i][c] = res.ChannelShifts[i - 1][c];
                        }
                    }
                }
            }
        }

        /// <summary>Memory manager associated with this pipeline.</summary>
        protected readonly JxlMemoryManager MemoryManager;

        /// <summary>Stages in execution order.</summary>
        protected readonly List<RenderPipelineStage> Stages = new List<RenderPipelineStage>();

        /// <summary>Shifts for every channel at the input of each stage (<c>[stage][channel]</c>).</summary>
        protected (nuint X, nuint Y)[][] ChannelShifts = Array.Empty<(nuint X, nuint Y)[]>();

        /// <summary>Cumulative padding required by each stage and channel (<c>[stage][channel]</c>).</summary>
        protected (nuint X, nuint Y)[][] Padding = Array.Empty<(nuint X, nuint Y)[]>();

        /// <summary>Frame dimensions.</summary>
        protected FrameDimensions FrameDimensions = new FrameDimensions();

        /// <summary>Number of completed passes per group.</summary>
        protected byte[] GroupCompletedPasses = Array.Empty<byte>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderPipeline"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocations.</param>
        protected RenderPipeline(JxlMemoryManager? memoryManager)
        {
            MemoryManager = memoryManager ?? JxlMemoryManager.Default;
        }

        /// <summary>
        /// Checks whether all stages in the pipeline were initialized without error.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> if all stages are initialized.</returns>
        public Status IsInitialized()
        {
            foreach (RenderPipelineStage stage in Stages)
            {
                Status status = stage.IsInitialized();

                if (!status)
                {
                    return status;
                }
            }

            return true;
        }

        /// <summary>
        /// Allocates storage to run with <paramref name="num"/> threads.
        /// </summary>
        /// <param name="num">Number of worker threads.</param>
        /// <param name="useGroupIds">Whether buffers are indexed by group ID rather than thread ID.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status PrepareForThreads(nuint num, bool useGroupIds)
        {
            foreach (RenderPipelineStage stage in Stages)
            {
                Status status = stage.PrepareForThreads(num);

                if (!status)
                {
                    return status;
                }
            }

            return PrepareForThreadsInternal(num, useGroupIds);
        }

        /// <summary>
        /// Retrieves the input buffers for <paramref name="groupId"/> and <paramref name="threadId"/>.
        /// </summary>
        /// <param name="groupId">Group index.</param>
        /// <param name="threadId">Worker thread index.</param>
        /// <returns><see cref="RenderPipelineInput"/> handle.</returns>
        public RenderPipelineInput GetInputBuffers(nuint groupId, nuint threadId)
        {
            Debug.Assert(groupId < (nuint)GroupCompletedPasses.Length);

            return new RenderPipelineInput
            {
                Pipeline = this,
                GroupId  = groupId,
                ThreadId = threadId,
                Buffers  = PrepareBuffers(groupId, threadId),
            };
        }

        /// <summary>
        /// Returns the minimum number of completed passes across all groups.
        /// </summary>
        /// <returns>Number of passes with all group inputs ready.</returns>
        public nuint PassesWithAllInput()
        {
            if (GroupCompletedPasses.Length == 0)
            {
                return 0;
            }

            byte minVal = GroupCompletedPasses[0];

            for (int i = 1; i < GroupCompletedPasses.Length; i++)
            {
                if (GroupCompletedPasses[i] < minVal)
                {
                    minVal = GroupCompletedPasses[i];
                }
            }

            return minVal;
        }

        /// <summary>
        /// Marks group <paramref name="i"/> as not-done for multi-pass re-painting.
        /// </summary>
        /// <param name="i">Group index.</param>
        public virtual void ClearDone(nuint i)
        {
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            foreach (RenderPipelineStage stage in Stages)
            {
                stage.Dispose();
            }

            Stages.Clear();
        }

        internal Status InputReady(
            nuint groupId,
            nuint threadId,
            (ImagePlane<float> Plane, Rect Rect)[] buffers)
        {
            if (groupId >= (nuint)GroupCompletedPasses.Length)
            {
                return Status.Failure("groupId out of range in RenderPipeline.InputReady");
            }

            GroupCompletedPasses[groupId]++;

            return ProcessBuffers(groupId, threadId);
        }

        /// <summary>
        /// Prepares the per-channel input buffers and rects for <paramref name="groupId"/> and <paramref name="threadId"/>.
        /// </summary>
        /// <param name="groupId">Group index.</param>
        /// <param name="threadId">Worker thread index.</param>
        /// <returns>Array of <c>(Plane, Rect)</c> pairs.</returns>
        protected abstract (ImagePlane<float> Plane, Rect Rect)[] PrepareBuffers(nuint groupId, nuint threadId);

        /// <summary>
        /// Processes the populated buffers for <paramref name="groupId"/> and <paramref name="threadId"/>.
        /// </summary>
        /// <param name="groupId">Group index.</param>
        /// <param name="threadId">Worker thread index.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        protected abstract Status ProcessBuffers(nuint groupId, nuint threadId);

        /// <summary>
        /// Allocates internal pipeline buffers for <paramref name="num"/> threads.
        /// </summary>
        /// <param name="num">Number of worker threads.</param>
        /// <param name="useGroupIds">Whether buffers are indexed by group ID.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        protected abstract Status PrepareForThreadsInternal(nuint num, bool useGroupIds);

        /// <summary>
        /// Initializes pipeline state once frame dimensions and stages are known.
        /// </summary>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        protected virtual Status Init() => true;
    }

    /// <summary>
    /// Thread-safe lock-free tracker assigning shared group borders and corners to whichever adjacent group finishes last.
    /// Ported from <c>jxl::GroupBorderAssigner</c> in <c>lib/jxl/dec_group_border.h</c> and <c>lib/jxl/dec_group_border.cc</c>.
    /// </summary>
    public sealed class GroupBorderAssigner
    {
        /// <summary>Maximum number of rectangles returned by <see cref="GroupDone"/> (3).</summary>
        public const int KMaxToFinalize = 3;

        private const uint KTopLeft     = 0x01;
        private const uint KTopRight    = 0x02;
        private const uint KBottomRight = 0x04;
        private const uint KBottomLeft  = 0x08;
        private const nuint KNoSegment  = 3;

        private FrameDimensions _frameDim = new FrameDimensions();
        private int[][] _counters = Array.Empty<int[]>();

        /// <summary>
        /// Prepares the <see cref="GroupBorderAssigner"/> to handle a frame with <paramref name="frameDim"/>.
        /// </summary>
        /// <param name="frameDim">Frame dimensions.</param>
        public void Init(FrameDimensions frameDim)
        {
            _frameDim   = frameDim;
            nuint xSize = _frameDim.XSizeGroups;
            nuint ySize = _frameDim.YSizeGroups;
            _counters   = new int[ySize + 1][];

            nuint rowWords = (xSize + 1 + 7) / 8;

            for (nuint y = 0; y <= ySize; y++)
            {
                _counters[y] = new int[rowWords];
            }

            for (nuint x = 0; x <= xSize; x++)
            {
                SetCornerBits(x, 0, KTopLeft | KTopRight);
                SetCornerBits(x, ySize, KBottomLeft | KBottomRight);
            }

            for (nuint y = 0; y <= ySize; y++)
            {
                SetCornerBits(0, y, KTopLeft | KBottomLeft);
                SetCornerBits(xSize, y, KTopRight | KBottomRight);
            }
        }

        /// <summary>
        /// Marks group <paramref name="groupId"/> as not-done for multi-pass re-paints.
        /// </summary>
        /// <param name="groupId">Group index.</param>
        public void ClearDone(nuint groupId)
        {
            nuint x = groupId % _frameDim.XSizeGroups;
            nuint y = groupId / _frameDim.XSizeGroups;
            ClearCornerBits(x, y, KBottomRight);
            ClearCornerBits(x + 1, y, KBottomLeft);
            ClearCornerBits(x, y + 1, KTopRight);
            ClearCornerBits(x + 1, y + 1, KTopLeft);
        }

        /// <summary>
        /// Marks group <paramref name="groupId"/> as done and populates up to <see cref="KMaxToFinalize"/> ready rectangles.
        /// </summary>
        /// <param name="groupId">Group index.</param>
        /// <param name="padx">Horizontal border padding in frame pixels.</param>
        /// <param name="pady">Vertical border padding in frame pixels.</param>
        /// <param name="rectsToFinalize">Output span of length at least <see cref="KMaxToFinalize"/>.</param>
        /// <param name="numToFinalize">Number of ready rectangles written to <paramref name="rectsToFinalize"/>.</param>
        public void GroupDone(
            nuint groupId,
            nuint padx,
            nuint pady,
            Span<Rect> rectsToFinalize,
            out nuint numToFinalize)
        {
            nuint x        = groupId % _frameDim.XSizeGroups;
            nuint y        = groupId / _frameDim.XSizeGroups;
            nuint blockGrp = _frameDim.GroupDim / FrameDimensions.kBlockDim;
            Rect blockRect = new Rect(
                (int)(x * blockGrp),
                (int)(y * blockGrp),
                (int)blockGrp,
                (int)blockGrp,
                (int)_frameDim.XSizeBlocks,
                (int)_frameDim.YSizeBlocks);

            uint topLeftStatus     = FetchStatus(x, y, KBottomRight);
            uint topRightStatus    = FetchStatus(x + 1, y, KBottomLeft);
            uint bottomRightStatus = FetchStatus(x + 1, y + 1, KTopLeft);
            uint bottomLeftStatus  = FetchStatus(x, y + 1, KTopRight);

            Span<nuint> xpos = stackalloc nuint[4];
            Span<nuint> ypos = stackalloc nuint[4];

            ComputeGroupPositions(in blockRect, x, y, padx, pady, xpos, ypos);

            Span<(nuint First, nuint Second)> horizontalSegments = stackalloc (nuint First, nuint Second)[3];

            CollectHorizontalSegments(
                topLeftStatus,
                topRightStatus,
                bottomRightStatus,
                bottomLeftStatus,
                horizontalSegments);

            EmitFinalizedRects(xpos, ypos, horizontalSegments, rectsToFinalize, out numToFinalize);
        }

        private void SetCornerBits(nuint x, nuint y, uint corners)
        {
            int mask = (int)(corners << (int)(4 * (x & 7u)));

            _counters[y][x / 8] |= mask;
        }

        private void ClearCornerBits(nuint x, nuint y, uint corners)
        {
            int mask = ~(int)(corners << (int)(4 * (x & 7u)));

            Interlocked.And(ref _counters[y][x / 8], mask);
        }

        private uint FetchStatus(nuint x, nuint y, uint bit)
        {
            int shift   = (int)(4 * (x & 7u));
            int bitMask = (int)(bit << shift);
            int prev    = Interlocked.Or(ref _counters[y][x / 8], bitMask);
            uint status = (uint)prev >> shift;

            Debug.Assert((bit & status) == 0);

            return (bit | status) & 0xFu;
        }

        private void ComputeGroupPositions(
            in Rect blockRect,
            nuint x,
            nuint y,
            nuint padx,
            nuint pady,
            Span<nuint> xpos,
            Span<nuint> ypos)
        {
            nuint x0          = (nuint)blockRect.X0;
            nuint y0          = (nuint)blockRect.Y0;
            nuint x1          = (nuint)(blockRect.X0 + blockRect.XSize);
            nuint y1          = (nuint)(blockRect.Y0 + blockRect.YSize);
            bool isLastGroupX = _frameDim.XSizeGroups == x + 1;
            bool isLastGroupY = _frameDim.YSizeGroups == y + 1;

            xpos[0] = x0 == 0 ? 0 : (x0 * FrameDimensions.kBlockDim) - padx;
            xpos[1] = x0 == 0 ? 0 : Math.Min(_frameDim.XSize, (x0 * FrameDimensions.kBlockDim) + padx);
            xpos[2] = isLastGroupX ? _frameDim.XSize : (x1 * FrameDimensions.kBlockDim) - padx;
            xpos[3] = Math.Min(_frameDim.XSize, (x1 * FrameDimensions.kBlockDim) + padx);

            ypos[0] = y0 == 0 ? 0 : (y0 * FrameDimensions.kBlockDim) - pady;
            ypos[1] = y0 == 0 ? 0 : Math.Min(_frameDim.YSize, (y0 * FrameDimensions.kBlockDim) + pady);
            ypos[2] = isLastGroupY ? _frameDim.YSize : (y1 * FrameDimensions.kBlockDim) - pady;
            ypos[3] = Math.Min(_frameDim.YSize, (y1 * FrameDimensions.kBlockDim) + pady);
        }

        private static void CollectHorizontalSegments(
            uint topLeftStatus,
            uint topRightStatus,
            uint bottomRightStatus,
            uint bottomLeftStatus,
            Span<(nuint First, nuint Second)> horizontalSegments)
        {
            Span<bool> mask = stackalloc bool[9];

            mask[(1 * 3) + 1] = true;

            if (topLeftStatus == 0xFu)
            {
                mask[(0 * 3) + 0] = true;
            }

            if (topRightStatus == 0xFu)
            {
                mask[(2 * 3) + 0] = true;
            }

            if (bottomRightStatus == 0xFu)
            {
                mask[(2 * 3) + 2] = true;
            }

            if (bottomLeftStatus == 0xFu)
            {
                mask[(0 * 3) + 2] = true;
            }

            if ((topLeftStatus & KTopRight) != 0)
            {
                mask[(1 * 3) + 0] = true;
            }

            if ((topLeftStatus & KBottomLeft) != 0)
            {
                mask[(0 * 3) + 1] = true;
            }

            if ((topRightStatus & KBottomRight) != 0)
            {
                mask[(2 * 3) + 1] = true;
            }

            if ((bottomLeftStatus & KBottomRight) != 0)
            {
                mask[(1 * 3) + 2] = true;
            }

            for (nuint py = 0; py < 3; py++)
            {
                horizontalSegments[(int)py] = (KNoSegment, KNoSegment);

                for (nuint px = 0; px < 3; px++)
                {
                    if (!mask[(int)((px * 3) + py)])
                    {
                        continue;
                    }

                    if (horizontalSegments[(int)py].First == KNoSegment)
                    {
                        horizontalSegments[(int)py].First = px;
                    }

                    horizontalSegments[(int)py].Second = px + 1;
                }
            }
        }

        private static void EmitFinalizedRects(
            ReadOnlySpan<nuint> xpos,
            ReadOnlySpan<nuint> ypos,
            ReadOnlySpan<(nuint First, nuint Second)> segs,
            Span<Rect> rectsToFinalize,
            out nuint numToFinalize)
        {
            numToFinalize = 0;

            if (segs[0] == segs[1] && segs[0] == segs[2])
            {
                AppendRect(xpos, ypos, (segs[0].First, segs[0].Second, 0, 3), rectsToFinalize, ref numToFinalize);
            }
            else if (segs[0] == segs[1])
            {
                AppendRect(xpos, ypos, (segs[0].First, segs[0].Second, 0, 2), rectsToFinalize, ref numToFinalize);
                AppendRect(xpos, ypos, (segs[2].First, segs[2].Second, 2, 3), rectsToFinalize, ref numToFinalize);
            }
            else if (segs[1] == segs[2])
            {
                AppendRect(xpos, ypos, (segs[0].First, segs[0].Second, 0, 1), rectsToFinalize, ref numToFinalize);
                AppendRect(xpos, ypos, (segs[1].First, segs[1].Second, 1, 3), rectsToFinalize, ref numToFinalize);
            }
            else
            {
                AppendRect(xpos, ypos, (segs[0].First, segs[0].Second, 0, 1), rectsToFinalize, ref numToFinalize);
                AppendRect(xpos, ypos, (segs[1].First, segs[1].Second, 1, 2), rectsToFinalize, ref numToFinalize);
                AppendRect(xpos, ypos, (segs[2].First, segs[2].Second, 2, 3), rectsToFinalize, ref numToFinalize);
            }
        }

        private static void AppendRect(
            ReadOnlySpan<nuint> xpos,
            ReadOnlySpan<nuint> ypos,
            (nuint X0, nuint X1, nuint Y0, nuint Y1) bounds,
            Span<Rect> rectsToFinalize,
            ref nuint numToFinalize)
        {
            Rect rect = new Rect(
                (int)xpos[(int)bounds.X0],
                (int)ypos[(int)bounds.Y0],
                (int)(xpos[(int)bounds.X1] - xpos[(int)bounds.X0]),
                (int)(ypos[(int)bounds.Y1] - ypos[(int)bounds.Y0]));

            if (rect.XSize == 0 || rect.YSize == 0)
            {
                return;
            }

            Debug.Assert(numToFinalize < KMaxToFinalize);
            rectsToFinalize[(int)numToFinalize++] = rect;
        }
    }

    /// <summary>
    /// Full-frame reference implementation of <see cref="RenderPipeline"/> used for testing and slow-pipeline verification.
    /// Ported from <c>jxl::SimpleRenderPipeline</c> in <c>lib/jxl/render_pipeline/simple_render_pipeline.h</c>
    /// and <c>lib/jxl/render_pipeline/simple_render_pipeline.cc</c>.
    /// </summary>
    public sealed class SimpleRenderPipeline : RenderPipeline
    {
        private readonly List<ImagePlane<float>> _channelData = new List<ImagePlane<float>>();
        private nuint _processedPasses;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleRenderPipeline"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocations.</param>
        public SimpleRenderPipeline(JxlMemoryManager? memoryManager)
            : base(memoryManager)
        {
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            foreach (ImagePlane<float> ch in _channelData)
            {
                ch.Dispose();
            }

            _channelData.Clear();
            base.Dispose();
        }

        /// <inheritdoc />
        protected override Status PrepareForThreadsInternal(nuint num, bool useGroupIds)
        {
            if (_channelData.Count != 0)
            {
                return true;
            }

            foreach ((nuint X, nuint Y) entry in ChannelShifts[0])
            {
                nuint w = Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << (int)entry.X) +
                          (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2);
                nuint h = Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << (int)entry.Y) +
                          (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2);
                StatusOr<ImagePlane<float>> chResult = ImagePlane<float>.Create(MemoryManager, w, h);

                if (!chResult.IsOk)
                {
                    return chResult.Status;
                }

                _channelData.Add(chResult.Value!);
            }

            return true;
        }

        /// <inheritdoc />
        protected override (ImagePlane<float> Plane, Rect Rect)[] PrepareBuffers(nuint groupId, nuint threadId)
        {
            (ImagePlane<float> Plane, Rect Rect)[] ret = new (ImagePlane<float> Plane, Rect Rect)[_channelData.Count];

            for (nuint c = 0; c < (nuint)_channelData.Count; c++)
            {
                ret[c] = (_channelData[(int)c], MakeChannelRect(groupId, c));
            }

            return ret;
        }

        /// <inheritdoc />
        protected override Status ProcessBuffers(nuint groupId, nuint threadId)
        {
            if (PassesWithAllInput() <= _processedPasses)
            {
                return true;
            }

            _processedPasses++;

            for (int stageId = 0; stageId < Stages.Count; stageId++)
            {
                Status stageStatus = RunSingleStage(stageId, threadId);

                if (!stageStatus)
                {
                    return stageStatus;
                }
            }

            return true;
        }

        private Rect MakeChannelRect(nuint groupId, nuint channel)
        {
            int baseColorShift = Bits.CeilLog2Nonzero(
                FrameDimensions.XSizeUpsampledPadded / FrameDimensions.XSizePadded);
            nuint gx        = groupId % FrameDimensions.XSizeGroups;
            nuint gy        = groupId / FrameDimensions.XSizeGroups;
            int shiftX      = (int)ChannelShifts[0][channel].X;
            int shiftY      = (int)ChannelShifts[0][channel].Y;
            nuint xgroupdim = (FrameDimensions.GroupDim << baseColorShift) >> shiftX;
            nuint ygroupdim = (FrameDimensions.GroupDim << baseColorShift) >> shiftY;
            int offset      = RenderPipelineStage.KRenderPipelineXOffset;

            return new Rect(
                offset + (int)(gx * xgroupdim),
                offset + (int)(gy * ygroupdim),
                (int)xgroupdim,
                (int)ygroupdim,
                offset + (int)Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << shiftX),
                offset + (int)Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << shiftY));
        }

        private Status RunSingleStage(int stageId, nuint threadId)
        {
            RenderPipelineStage stage               = Stages[stageId];
            int numC                                = _channelData.Count;
            ImagePlane<float>?[] outputChannels     = new ImagePlane<float>?[numC];
            (nuint XSize, nuint YSize)[] inputSizes = new (nuint XSize, nuint YSize)[numC];

            for (int c = 0; c < numC; c++)
            {
                inputSizes[c] = (
                    _channelData[c].XSize - (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2),
                    _channelData[c].YSize - (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2));
            }

            Status allocStatus = AllocateInOutChannels(stage, inputSizes, outputChannels);

            if (!allocStatus)
            {
                return allocStatus;
            }

            MirrorInOutChannels(stage, inputSizes);

            nuint xsize = 0;
            nuint ysize = 0;

            for (int c = 0; c < numC; c++)
            {
                if (stage.GetChannelMode((nuint)c) != RenderPipelineChannelMode.Ignored)
                {
                    xsize = Math.Max(inputSizes[c].XSize, xsize);
                    ysize = Math.Max(inputSizes[c].YSize, ysize);
                }
            }

            if (xsize == 0 || ysize == 0)
            {
                return Status.Failure("SimpleRenderPipeline stage has 0 dimensions");
            }

            Status execStatus = ExecuteStageRows(stage, inputSizes, outputChannels, xsize, ysize, threadId);

            if (!execStatus)
            {
                return execStatus;
            }

            Status swapStatus = SwapAndShrinkStageOutputs(stageId, stage, outputChannels);

            if (!swapStatus)
            {
                return swapStatus;
            }

            if (stage.SwitchToImageDimensions())
            {
                return ApplySwitchToImageDimensions(stage, ref xsize, ref ysize);
            }

            return true;
        }

        private Status AllocateInOutChannels(
            RenderPipelineStage stage,
            (nuint XSize, nuint YSize)[] inputSizes,
            ImagePlane<float>?[] outputChannels)
        {
            nuint offset2 = (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2);

            for (int c = 0; c < _channelData.Count; c++)
            {
                if (stage.GetChannelMode((nuint)c) != RenderPipelineChannelMode.InOut)
                {
                    continue;
                }

                nuint allocW                      = FrameDimensions.XSizeUpsampledPadded + offset2 + 512;
                nuint allocH                      = FrameDimensions.YSizeUpsampledPadded + offset2;
                StatusOr<ImagePlane<float>> newCh = ImagePlane<float>.Create(MemoryManager, allocW, allocH);

                if (!newCh.IsOk)
                {
                    return newCh.Status;
                }

                nuint shrunkW       = (inputSizes[c].XSize << (int)stage.StageSettings.ShiftX) + offset2;
                nuint shrunkH       = (inputSizes[c].YSize << (int)stage.StageSettings.ShiftY) + offset2;
                Status shrinkStatus = newCh.Value!.ShrinkTo(shrunkW, shrunkH);

                if (!shrinkStatus)
                {
                    newCh.Value.Dispose();

                    return shrinkStatus;
                }

                outputChannels[c] = newCh.Value;
            }

            return true;
        }

        private void MirrorInOutChannels(
            RenderPipelineStage stage,
            (nuint XSize, nuint YSize)[] inputSizes)
        {
            int borderX = (int)stage.StageSettings.BorderX;
            int borderY = (int)stage.StageSettings.BorderY;

            for (int c = 0; c < _channelData.Count; c++)
            {
                if (stage.GetChannelMode((nuint)c) != RenderPipelineChannelMode.InOut)
                {
                    continue;
                }

                long inX = (long)inputSizes[c].XSize;
                long inY = (long)inputSizes[c].YSize;

                for (long y = 0; y < inY; y++)
                {
                    PipelineRow row = GetChannelOffsetRow(c, y);

                    for (int ix = 0; ix < borderX; ix++)
                    {
                        row[-ix - 1]        = row[(nint)ImageOps.Mirror(-ix - 1, inX)];
                        row[(nint)inX + ix] = row[(nint)ImageOps.Mirror(inX + ix, inX)];
                    }
                }

                int copyLen = (int)(inX + (2 * borderX));

                for (int y = 0; y < borderY; y++)
                {
                    Span<float> topDst = GetChannelOffsetRow(c, -y - 1).AsSpan(-borderX, copyLen);
                    Span<float> topSrc = GetChannelOffsetRow(c, ImageOps.Mirror(-y - 1, inY)).AsSpan(-borderX, copyLen);
                    topSrc.CopyTo(topDst);

                    Span<float> botDst = GetChannelOffsetRow(c, inY + y).AsSpan(-borderX, copyLen);
                    Span<float> botSrc = GetChannelOffsetRow(c, ImageOps.Mirror(inY + y, inY)).AsSpan(-borderX, copyLen);
                    botSrc.CopyTo(botDst);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PipelineRow GetChannelOffsetRow(int c, long y)
        {
            int offset = RenderPipelineStage.KRenderPipelineXOffset;

            return new PipelineRow(_channelData[c], (nuint)(offset + y), offset);
        }

        private Status ExecuteStageRows(
            RenderPipelineStage stage,
            (nuint XSize, nuint YSize)[] inputSizes,
            ImagePlane<float>?[] outputChannels,
            nuint xsize,
            nuint ysize,
            nuint threadId)
        {
            Status sizeStatus = stage.SetInputSizes(inputSizes);

            if (!sizeStatus)
            {
                return sizeStatus;
            }

            int numC                = _channelData.Count;
            int borderY             = (int)stage.StageSettings.BorderY;
            int shiftY              = (int)stage.StageSettings.ShiftY;
            int offset              = RenderPipelineStage.KRenderPipelineXOffset;
            PipelineRow[][] inRows  = new PipelineRow[numC][];
            PipelineRow[][] outRows = new PipelineRow[numC][];

            for (int c = 0; c < numC; c++)
            {
                inRows[c]  = stage.GetChannelMode((nuint)c) != RenderPipelineChannelMode.Ignored
                    ? new PipelineRow[(2 * borderY) + 1]
                    : Array.Empty<PipelineRow>();
                outRows[c] = outputChannels[c] != null
                    ? new PipelineRow[1 << shiftY]
                    : Array.Empty<PipelineRow>();
            }

            for (nuint y = 0; y < ysize; y++)
            {
                for (int c = 0; c < numC; c++)
                {
                    if (stage.GetChannelMode((nuint)c) != RenderPipelineChannelMode.Ignored)
                    {
                        for (int iy = -borderY; iy <= borderY; iy++)
                        {
                            inRows[c][iy + borderY] = new PipelineRow(_channelData[c], (nuint)((int)y + offset + iy));
                        }
                    }

                    if (outputChannels[c] != null)
                    {
                        for (int iy = 0; iy < outRows[c].Length; iy++)
                        {
                            outRows[c][iy] = new PipelineRow(outputChannels[c]!, (y << shiftY) + (nuint)(iy + offset));
                        }
                    }
                }

                Status rowStatus = stage.ProcessRow(inRows, outRows, (0, 0), xsize, 0, y, threadId);

                if (!rowStatus)
                {
                    return rowStatus;
                }
            }

            return true;
        }

        private Status SwapAndShrinkStageOutputs(
            int stageId,
            RenderPipelineStage stage,
            ImagePlane<float>?[] outputChannels)
        {
            int nextStage = Math.Min(stageId + 1, ChannelShifts.Length - 1);
            nuint offset2 = (nuint)(RenderPipelineStage.KRenderPipelineXOffset * 2);

            for (int c = 0; c < _channelData.Count; c++)
            {
                if (stage.GetChannelMode((nuint)c) == RenderPipelineChannelMode.InOut && outputChannels[c] != null)
                {
                    _channelData[c].Dispose();
                    _channelData[c] = outputChannels[c]!;
                }

                nuint cXSize        = Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << (int)ChannelShifts[nextStage][c].X);
                nuint cYSize        = Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << (int)ChannelShifts[nextStage][c].Y);
                Status shrinkStatus = _channelData[c].ShrinkTo(cXSize + offset2, cYSize + offset2);

                if (!shrinkStatus)
                {
                    return shrinkStatus;
                }
            }

            return true;
        }

        private Status ApplySwitchToImageDimensions(
            RenderPipelineStage stage,
            ref nuint xsize,
            ref nuint ysize)
        {
            stage.GetImageDimensions(out nuint imageXSize, out nuint imageYSize, out FrameOrigin frameOrigin);
            FrameDimensions.Set(imageXSize, imageYSize, 0, 0, 0, false, 1);

            int numC                            = _channelData.Count;
            int offset                          = RenderPipelineStage.KRenderPipelineXOffset;
            nuint offset2                       = (nuint)(offset * 2);
            List<ImagePlane<float>> oldChannels = new List<ImagePlane<float>>(_channelData);

            _channelData.Clear();

            for (int c = 0; c < numC; c++)
            {
                StatusOr<ImagePlane<float>> chResult = ImagePlane<float>.Create(
                    MemoryManager,
                    offset2 + imageXSize,
                    offset2 + imageYSize);

                if (!chResult.IsOk)
                {
                    return chResult.Status;
                }

                _channelData.Add(chResult.Value!);
            }

            PipelineRow[][] outRows = new PipelineRow[numC][];

            for (int c = 0; c < numC; c++)
            {
                outRows[c] = new PipelineRow[1];
            }

            for (nuint y = 0; y < imageYSize; ++y)
            {
                for (int c = 0; c < numC; c++)
                {
                    outRows[c][0] = new PipelineRow(_channelData[c], (nuint)offset + y);
                }

                stage.ProcessPaddingRow(outRows, imageXSize, 0, y);
            }

            return CopyForegroundToImageDimensions(oldChannels, frameOrigin, imageXSize, imageYSize, ref xsize, ref ysize);
        }

        private Status CopyForegroundToImageDimensions(
            List<ImagePlane<float>> oldChannels,
            FrameOrigin frameOrigin,
            nuint imageXSize,
            nuint imageYSize,
            ref nuint xsize,
            ref nuint ysize)
        {
            nint x0    = frameOrigin.X0;
            nint y0    = frameOrigin.Y0;
            nuint x0Fg = 0;
            nuint y0Fg = 0;

            if (x0 < 0)
            {
                xsize = (nuint)((nint)xsize + x0);
                x0Fg  = (nuint)(-x0);
                x0    = 0;
            }

            if ((nuint)x0 + xsize > imageXSize)
            {
                xsize = imageXSize - (nuint)x0;
            }

            if (y0 < 0)
            {
                ysize = (nuint)((nint)ysize + y0);
                y0Fg  = (nuint)(-y0);
                y0    = 0;
            }

            if ((nuint)y0 + ysize > imageYSize)
            {
                ysize = imageYSize - (nuint)y0;
            }

            int offset                 = RenderPipelineStage.KRenderPipelineXOffset;
            Rect rectFgRelativeToImage = new Rect((int)x0, (int)y0, (int)xsize, (int)ysize).Translate(offset, offset);
            Rect rectFg                = new Rect((int)x0Fg, (int)y0Fg, (int)xsize, (int)ysize).Translate(offset, offset);

            for (int c = 0; c < _channelData.Count; c++)
            {
                Status copyStatus = ImageOps.CopyImageTo(rectFg, oldChannels[c], rectFgRelativeToImage, _channelData[c]);
                oldChannels[c].Dispose();

                if (!copyStatus)
                {
                    return copyStatus;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Multithreaded low-memory strip rendering pipeline.
    /// Ported from <c>jxl::LowMemoryRenderPipeline</c> in <c>lib/jxl/render_pipeline/low_memory_render_pipeline.h</c>
    /// and <c>lib/jxl/render_pipeline/low_memory_render_pipeline.cc</c>.
    /// </summary>
    public sealed class LowMemoryRenderPipeline : RenderPipeline
    {
        private const nuint KGroupXAlign = 16;

        private struct RowBufferInfo
        {
            public PipelineRow BaseRow;
            public int YModMinus1;
            public nint Stride;
        }

        private bool _useGroupIds;
        private readonly List<ImagePlane<float>> _bordersHorizontal = new List<ImagePlane<float>>();
        private readonly List<ImagePlane<float>> _bordersVertical = new List<ImagePlane<float>>();
        private readonly GroupBorderAssigner _groupBorderAssigner = new GroupBorderAssigner();
        private (nuint X, nuint Y) _groupBorder;
        private int _baseColorShift;
        private readonly List<ImagePlane<float>[]> _groupData = new List<ImagePlane<float>[]>();
        private nuint _groupDataXBorder;
        private nuint _groupDataYBorder;
        private ImagePlane<float>?[][][] _stageData = Array.Empty<ImagePlane<float>?[][]>();
        private ImagePlane<float>[] _outOfFrameData = Array.Empty<ImagePlane<float>>();
        private int[] _anyc = Array.Empty<int>();
        private Rect[] _imageRect = Array.Empty<Rect>();
        private int[][] _stageInputForChannel = Array.Empty<int[]>();
        private int[] _virtualYpaddingForOutput = Array.Empty<int>();
        private int[] _xpaddingForOutput = Array.Empty<int>();
        private int _firstTrailingStage;
        private FrameOrigin _frameOrigin;
        private nuint _fullImageXSize;
        private nuint _fullImageYSize;
        private int _firstImageDimStage;

        /// <summary>
        /// Initializes a new instance of the <see cref="LowMemoryRenderPipeline"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocations.</param>
        public LowMemoryRenderPipeline(JxlMemoryManager? memoryManager)
            : base(memoryManager)
        {
        }

        /// <inheritdoc />
        public override void ClearDone(nuint i)
        {
            _groupBorderAssigner.ClearDone(i);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            foreach (ImagePlane<float> p in _bordersHorizontal)
            {
                p.Dispose();
            }

            foreach (ImagePlane<float> p in _bordersVertical)
            {
                p.Dispose();
            }

            foreach (ImagePlane<float>[] arr in _groupData)
            {
                foreach (ImagePlane<float> p in arr)
                {
                    p?.Dispose();
                }
            }

            foreach (ImagePlane<float>?[][] threadArr in _stageData)
            {
                if (threadArr == null)
                {
                    continue;
                }

                foreach (ImagePlane<float>?[] chArr in threadArr)
                {
                    if (chArr == null)
                    {
                        continue;
                    }

                    foreach (ImagePlane<float>? p in chArr)
                    {
                        p?.Dispose();
                    }
                }
            }

            foreach (ImagePlane<float> p in _outOfFrameData)
            {
                p?.Dispose();
            }

            base.Dispose();
        }

        /// <inheritdoc />
        protected override Status Init()
        {
            _groupBorder    = (0, 0);
            _baseColorShift = Bits.CeilLog2Nonzero(
                FrameDimensions.XSizeUpsampledPadded / FrameDimensions.XSizePadded);
            (nuint X, nuint Y)[] shifts = ChannelShifts[0];

            ComputeGroupBorders(shifts);

            Status borderStatus = EnsureBordersStorage();

            if (!borderStatus)
            {
                return borderStatus;
            }

            _groupBorderAssigner.Init(FrameDimensions);
            ComputeTrailingAndImageDimStages(out Status stageSizeStatus);

            if (!stageSizeStatus)
            {
                return stageSizeStatus;
            }

            ComputeStageChannelMetadata(shifts);

            return true;
        }

        /// <inheritdoc />
        protected override Status PrepareForThreadsInternal(nuint num, bool useGroupIds)
        {
            (nuint X, nuint Y)[] shifts = ChannelShifts[0];
            _useGroupIds                = useGroupIds;
            nuint numBuffers            = _useGroupIds ? FrameDimensions.NumGroups : num;

            Status groupStatus = AllocateGroupDataBuffers(shifts, numBuffers);

            if (!groupStatus)
            {
                return groupStatus;
            }

            nuint upsampling       = 1u << _baseColorShift;
            nuint groupDim         = FrameDimensions.GroupDim * upsampling;
            nuint padding          = (2 * _groupDataXBorder * upsampling) +
                                     (nuint)(2 * RenderPipelineStage.KRenderPipelineXOffset);
            nuint stageBufferXSize = groupDim + padding;

            Status stageStatus = AllocateStageDataBuffers(shifts, num, stageBufferXSize);

            if (!stageStatus)
            {
                return stageStatus;
            }

            if (_firstImageDimStage != Stages.Count)
            {
                return AllocateOutOfFrameBuffers(shifts, num, groupDim, padding);
            }

            return true;
        }

        /// <inheritdoc />
        protected override (ImagePlane<float> Plane, Rect Rect)[] PrepareBuffers(nuint groupId, nuint threadId)
        {
            int numC                                   = ChannelShifts[0].Length;
            nuint gx                                   = groupId % FrameDimensions.XSizeGroups;
            nuint gy                                   = groupId / FrameDimensions.XSizeGroups;
            nuint idx                                  = _useGroupIds ? groupId : threadId;
            (ImagePlane<float> Plane, Rect Rect)[] ret = new (ImagePlane<float> Plane, Rect Rect)[numC];

            for (nuint c = 0; c < (nuint)numC; c++)
            {
                nuint inX  = GroupInputXSize(c);
                nuint inY  = GroupInputYSize(c);
                nuint endX = Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << (int)ChannelShifts[0][c].X) -
                             (gx * inX) + _groupDataXBorder;
                nuint endY = Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << (int)ChannelShifts[0][c].Y) -
                             (gy * inY) + _groupDataYBorder;
                ret[c]     = (
                    _groupData[(int)idx][c],
                    new Rect(
                        (int)_groupDataXBorder,
                        (int)_groupDataYBorder,
                        (int)inX,
                        (int)inY,
                        (int)endX,
                        (int)endY));
            }

            return ret;
        }

        /// <inheritdoc />
        protected override Status ProcessBuffers(nuint groupId, nuint threadId)
        {
            nuint bufIdx                  = _useGroupIds ? groupId : threadId;
            ImagePlane<float>[] inputData = _groupData[(int)bufIdx];

            for (nuint c = 0; c < (nuint)inputData.Length; c++)
            {
                Status saveStatus = SaveBorders(groupId, c, inputData[c]);

                if (!saveStatus)
                {
                    return saveStatus;
                }
            }

            nuint gy = groupId / FrameDimensions.XSizeGroups;
            nuint gx = groupId % FrameDimensions.XSizeGroups;

            if (_firstImageDimStage != Stages.Count)
            {
                Status padStatus = RenderGroupOutOfFramePadding(groupId, threadId, gx, gy);

                if (!padStatus)
                {
                    return padStatus;
                }
            }

            Span<Rect> readyRects = stackalloc Rect[GroupBorderAssigner.KMaxToFinalize];

            _groupBorderAssigner.GroupDone(groupId, _groupBorder.X, _groupBorder.Y, readyRects, out nuint numReadyRects);

            for (nuint i = 0; i < numReadyRects; i++)
            {
                Rect imageMaxColorChannelRect = readyRects[(int)i];

                for (nuint c = 0; c < (nuint)inputData.Length; c++)
                {
                    Status loadStatus = LoadBorders(groupId, c, in imageMaxColorChannelRect, inputData[c]);

                    if (!loadStatus)
                    {
                        return loadStatus;
                    }
                }

                Rect dataMaxColorChannelRect = new Rect(
                    (int)_groupDataXBorder + imageMaxColorChannelRect.X0 - (int)(gx * FrameDimensions.GroupDim),
                    (int)_groupDataYBorder + imageMaxColorChannelRect.Y0 - (int)(gy * FrameDimensions.GroupDim),
                    imageMaxColorChannelRect.XSize,
                    imageMaxColorChannelRect.YSize);

                Status renderStatus = RenderRect(
                    threadId,
                    inputData,
                    in dataMaxColorChannelRect,
                    in imageMaxColorChannelRect);

                if (!renderStatus)
                {
                    return renderStatus;
                }
            }

            return true;
        }

        private (nuint X, nuint Y) ColorDimensionsToChannelDimensions((nuint X, nuint Y) inDim, nuint c, int stage)
        {
            (nuint X, nuint Y) shift = ChannelShifts[stage][c];
            nuint rx                 = ((inDim.X << _baseColorShift) + (1u << (int)shift.X) - 1) >> (int)shift.X;
            nuint ry                 = ((inDim.Y << _baseColorShift) + (1u << (int)shift.Y) - 1) >> (int)shift.Y;

            return (rx, ry);
        }

        private (nuint X, nuint Y) BorderToStore(nuint c)
        {
            (nuint X, nuint Y) ret = ColorDimensionsToChannelDimensions(_groupBorder, c, 0);

            ret.X += Padding[0][c].X;
            ret.Y += Padding[0][c].Y;

            return ret;
        }

        private nuint GroupInputXSize(nuint c)
        {
            return (FrameDimensions.GroupDim << _baseColorShift) >> (int)ChannelShifts[0][c].X;
        }

        private nuint GroupInputYSize(nuint c)
        {
            return (FrameDimensions.GroupDim << _baseColorShift) >> (int)ChannelShifts[0][c].Y;
        }

        private void ComputeGroupBorders((nuint X, nuint Y)[] shifts)
        {
            for (nuint c = 0; c < (nuint)shifts.Length; c++)
            {
                _groupBorder.X = Math.Max(
                    _groupBorder.X,
                    Common.DivCeil(Padding[0][c].X << (int)shifts[c].X, 1u << _baseColorShift));
                _groupBorder.Y = Math.Max(
                    _groupBorder.Y,
                    Common.DivCeil(Padding[0][c].Y << (int)shifts[c].Y, 1u << _baseColorShift));
            }

            for (nuint c = 0; c < (nuint)shifts.Length; c++)
            {
                if ((int)shifts[c].X >= _baseColorShift)
                {
                    _groupBorder.X = Common.RoundUpTo(_groupBorder.X, 1u << ((int)shifts[c].X - _baseColorShift));
                }

                if ((int)shifts[c].Y >= _baseColorShift)
                {
                    _groupBorder.Y = Common.RoundUpTo(_groupBorder.Y, 1u << ((int)shifts[c].Y - _baseColorShift));
                }
            }

            _groupBorder.X = Common.RoundUpTo(_groupBorder.X, KGroupXAlign);

            (nuint X, nuint Y) maxBorder = (0, 0);

            for (nuint c = 0; c < (nuint)shifts.Length; c++)
            {
                (nuint X, nuint Y) b = BorderToStore(c);
                maxBorder.X          = Math.Max(b.X, maxBorder.X);
                maxBorder.Y          = Math.Max(b.Y, maxBorder.Y);
            }

            _groupDataXBorder = Common.RoundUpTo(maxBorder.X, KGroupXAlign);
            _groupDataYBorder = maxBorder.Y;
        }

        private Status EnsureBordersStorage()
        {
            (nuint X, nuint Y)[] shifts = ChannelShifts[0];

            while (_bordersHorizontal.Count < shifts.Length)
            {
                _bordersHorizontal.Add(new ImagePlane<float>());
                _bordersVertical.Add(new ImagePlane<float>());
            }

            for (nuint c = 0; c < (nuint)shifts.Length; c++)
            {
                (nuint borderX, nuint borderY) = BorderToStore(c);
                nuint numXBorders              = (FrameDimensions.XSizeGroups - 1) * 2;
                nuint numYBorders              = (FrameDimensions.YSizeGroups - 1) * 2;
                nuint downsampledXSize         = Common.DivCeil(FrameDimensions.XSizeUpsampledPadded, 1u << (int)shifts[c].X);
                nuint downsampledYSize         = Common.DivCeil(FrameDimensions.YSizeUpsampledPadded, 1u << (int)shifts[c].Y);

                nuint hW = downsampledXSize;
                nuint hH = borderY * numYBorders;

                if (_bordersHorizontal[(int)c].XSize != hW || _bordersHorizontal[(int)c].YSize != hH)
                {
                    _bordersHorizontal[(int)c].Dispose();
                    StatusOr<ImagePlane<float>> hRes = ImagePlane<float>.Create(MemoryManager, hW, hH);

                    if (!hRes.IsOk)
                    {
                        return hRes.Status;
                    }

                    _bordersHorizontal[(int)c] = hRes.Value!;
                }

                nuint vW = borderX * numXBorders;
                nuint vH = downsampledYSize;

                if (_bordersVertical[(int)c].XSize != vW || _bordersVertical[(int)c].YSize != vH)
                {
                    _bordersVertical[(int)c].Dispose();
                    StatusOr<ImagePlane<float>> vRes = ImagePlane<float>.Create(MemoryManager, vW, vH);

                    if (!vRes.IsOk)
                    {
                        return vRes.Status;
                    }

                    _bordersVertical[(int)c] = vRes.Value!;
                }
            }

            return true;
        }

        private void ComputeTrailingAndImageDimStages(out Status status)
        {
            status                      = Status.OkStatus;
            (nuint X, nuint Y)[] shifts = ChannelShifts[0];
            int numC                    = shifts.Length;

            for (_firstTrailingStage = Stages.Count; _firstTrailingStage > 0; _firstTrailingStage--)
            {
                bool hasInOut = false;

                for (nuint c = 0; c < (nuint)numC; c++)
                {
                    if (Stages[_firstTrailingStage - 1].GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                    {
                        hasInOut = true;
                    }
                }

                if (hasInOut)
                {
                    break;
                }
            }

            _firstImageDimStage = Stages.Count;

            for (int i = 0; i < Stages.Count; i++)
            {
                (nuint XSize, nuint YSize)[] inputSizes = new (nuint XSize, nuint YSize)[numC];

                for (int c = 0; c < numC; c++)
                {
                    inputSizes[c] = (
                        Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << (int)ChannelShifts[i][c].X),
                        Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << (int)ChannelShifts[i][c].Y));
                }

                status = Stages[i].SetInputSizes(inputSizes);

                if (!status)
                {
                    return;
                }

                if (Stages[i].SwitchToImageDimensions())
                {
                    _firstImageDimStage = i + 1;
                    Stages[i].GetImageDimensions(out _fullImageXSize, out _fullImageYSize, out _frameOrigin);

                    break;
                }
            }

            for (int i = _firstImageDimStage; i < Stages.Count; i++)
            {
                if (Stages[i].SwitchToImageDimensions())
                {
                    status = Status.Failure("cannot switch to image dimensions multiple times");

                    return;
                }

                (nuint XSize, nuint YSize)[] inputSizes = new (nuint XSize, nuint YSize)[numC];

                for (int c = 0; c < numC; c++)
                {
                    inputSizes[c] = (_fullImageXSize, _fullImageYSize);
                }

                status = Stages[i].SetInputSizes(inputSizes);

                if (!status)
                {
                    return;
                }
            }
        }

        private void ComputeStageChannelMetadata((nuint X, nuint Y)[] shifts)
        {
            int numStages = Stages.Count;
            int numC      = shifts.Length;
            _anyc         = new int[numStages];

            for (int i = 0; i < numStages; i++)
            {
                for (int c = 0; c < numC; c++)
                {
                    if (Stages[i].GetChannelMode((nuint)c) != RenderPipelineChannelMode.Ignored)
                    {
                        _anyc[i] = c;
                    }
                }
            }

            _stageInputForChannel = new int[numStages][];

            for (int i = 0; i < numStages; i++)
            {
                _stageInputForChannel[i] = new int[numC];
            }

            for (int c = 0; c < numC; c++)
            {
                int input = -1;

                for (int i = 0; i < numStages; i++)
                {
                    _stageInputForChannel[i][c] = input;

                    if (Stages[i].GetChannelMode((nuint)c) == RenderPipelineChannelMode.InOut)
                    {
                        input = i;
                    }
                }
            }

            _imageRect = new Rect[numStages];

            for (int i = 0; i < numStages; i++)
            {
                int c         = _anyc[i];
                nuint x1      = Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << (int)ChannelShifts[i][c].X);
                nuint y1      = Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << (int)ChannelShifts[i][c].Y);
                _imageRect[i] = new Rect(0, 0, (int)x1, (int)y1);
            }

            _virtualYpaddingForOutput = new int[numStages];
            _xpaddingForOutput        = new int[numStages];

            for (int c = 0; c < numC; c++)
            {
                int ypad = 0;
                int xpad = 0;

                for (int i = numStages - 1; i >= 0; i--)
                {
                    int ySampling                  = 1 << (int)ChannelShifts[i][c].Y;
                    int alignedYPad                = (int)Common.RoundUpTo((nuint)ypad, (nuint)ySampling);
                    RenderPipelineChannelMode mode = Stages[i].GetChannelMode((nuint)c);

                    if (mode != RenderPipelineChannelMode.Ignored)
                    {
                        _virtualYpaddingForOutput[i] = Math.Max(_virtualYpaddingForOutput[i], alignedYPad);
                        _xpaddingForOutput[i]        = Math.Max(xpad, _xpaddingForOutput[i]);
                    }

                    if (mode == RenderPipelineChannelMode.InOut)
                    {
                        ypad = alignedYPad + ((int)Stages[i].StageSettings.BorderY * ySampling);
                        xpad = Common.DivCeil(xpad, 1 << (int)Stages[i].StageSettings.ShiftX) +
                               (int)Stages[i].StageSettings.BorderX;
                    }
                }
            }
        }

        private Status AllocateGroupDataBuffers((nuint X, nuint Y)[] shifts, nuint numBuffers)
        {
            while ((nuint)_groupData.Count < numBuffers)
            {
                ImagePlane<float>[] chPlanes = new ImagePlane<float>[shifts.Length];

                for (nuint c = 0; c < (nuint)shifts.Length; c++)
                {
                    StatusOr<ImagePlane<float>> res = ImagePlane<float>.Create(
                        MemoryManager,
                        GroupInputXSize(c) + (_groupDataXBorder * 2),
                        GroupInputYSize(c) + (_groupDataYBorder * 2),
                        (nuint)RenderPipelineStage.KRenderPipelineXOffset);

                    if (!res.IsOk)
                    {
                        return res.Status;
                    }

                    chPlanes[c] = res.Value!;
                }

                _groupData.Add(chPlanes);
            }

            return true;
        }

        private Status AllocateStageDataBuffers(
            (nuint X, nuint Y)[] shifts,
            nuint num,
            nuint stageBufferXSize)
        {
            _stageData = new ImagePlane<float>?[num][][];

            for (nuint t = 0; t < num; t++)
            {
                _stageData[t] = new ImagePlane<float>?[shifts.Length][];

                for (nuint c = 0; c < (nuint)shifts.Length; c++)
                {
                    _stageData[t][c]  = new ImagePlane<float>?[Stages.Count];
                    nuint nextYBorder = 0;

                    for (int i = Stages.Count - 1; i >= 0; i--)
                    {
                        if (Stages[i].GetChannelMode(c) == RenderPipelineChannelMode.InOut)
                        {
                            nuint stageBufferYSize = (2 * nextYBorder) + (1u << (int)Stages[i].StageSettings.ShiftY);
                            stageBufferYSize       = 1u << Bits.CeilLog2Nonzero(stageBufferYSize);
                            nextYBorder            = Stages[i].StageSettings.BorderY;

                            StatusOr<ImagePlane<float>> res = ImagePlane<float>.Create(
                                MemoryManager,
                                stageBufferXSize,
                                stageBufferYSize);

                            if (!res.IsOk)
                            {
                                return res.Status;
                            }

                            _stageData[t][c][i] = res.Value!;
                        }
                    }
                }
            }

            return true;
        }

        private Status AllocateOutOfFrameBuffers(
            (nuint X, nuint Y)[] shifts,
            nuint num,
            nuint groupDim,
            nuint padding)
        {
            Rect imageRect     = new Rect(0, 0, (int)FrameDimensions.XSizeUpsampled, (int)FrameDimensions.YSizeUpsampled);
            Rect fullImageRect = new Rect(0, 0, (int)_fullImageXSize, (int)_fullImageYSize);
            imageRect          = imageRect.Translate(_frameOrigin.X0, _frameOrigin.Y0).Intersection(fullImageRect);

            if (imageRect.XSize == 0 || imageRect.YSize == 0)
            {
                imageRect = new Rect(0, 0, 0, 0);
            }

            nuint leftPadding     = (nuint)Math.Max(0, imageRect.X0);
            nuint middlePadding   = groupDim;
            nuint rightPadding    = _fullImageXSize - (nuint)Math.Max(0, imageRect.X1);
            nuint outOfFrameXSize = padding + Math.Max(leftPadding, Math.Max(middlePadding, rightPadding));
            _outOfFrameData       = new ImagePlane<float>[num];

            for (nuint t = 0; t < num; t++)
            {
                StatusOr<ImagePlane<float>> res = ImagePlane<float>.Create(
                    MemoryManager,
                    outOfFrameXSize,
                    (nuint)shifts.Length);

                if (!res.IsOk)
                {
                    return res.Status;
                }

                _outOfFrameData[t] = res.Value!;
            }

            return true;
        }

        private Status SaveBorders(nuint groupId, nuint c, ImagePlane<float> inPlane)
        {
            nuint gy                                 = groupId / FrameDimensions.XSizeGroups;
            nuint gx                                 = groupId % FrameDimensions.XSizeGroups;
            int hshift                               = (int)ChannelShifts[0][c].X;
            int vshift                               = (int)ChannelShifts[0][c].Y;
            nuint x0                                 = gx * GroupInputXSize(c);
            nuint x1                                 = Math.Min((gx + 1) * GroupInputXSize(c), Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << hshift));
            nuint y0                                 = gy * GroupInputYSize(c);
            nuint y1                                 = Math.Min((gy + 1) * GroupInputYSize(c), Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << vshift));
            (nuint borderXWrite, nuint borderYWrite) = BorderToStore(c);

            if (gy > 0)
            {
                Rect from = new Rect((int)_groupDataXBorder, (int)_groupDataYBorder, (int)(x1 - x0), (int)borderYWrite);
                Rect to   = new Rect((int)x0, (int)(((gy * 2) - 1) * borderYWrite), (int)(x1 - x0), (int)borderYWrite);
                Status s  = ImageOps.CopyImageTo(from, inPlane, to, _bordersHorizontal[(int)c]);

                if (!s)
                {
                    return s;
                }
            }

            if (gy + 1 < FrameDimensions.YSizeGroups)
            {
                Rect from = new Rect(
                    (int)_groupDataXBorder,
                    (int)(_groupDataYBorder + y1 - y0 - borderYWrite),
                    (int)(x1 - x0),
                    (int)borderYWrite);
                Rect to  = new Rect((int)x0, (int)(gy * 2 * borderYWrite), (int)(x1 - x0), (int)borderYWrite);
                Status s = ImageOps.CopyImageTo(from, inPlane, to, _bordersHorizontal[(int)c]);

                if (!s)
                {
                    return s;
                }
            }

            return SaveVerticalBorders(c, inPlane, gx, (x0, x1, y0, y1), borderXWrite);
        }

        private Status SaveVerticalBorders(
            nuint c,
            ImagePlane<float> inPlane,
            nuint gx,
            (nuint X0, nuint X1, nuint Y0, nuint Y1) grp,
            nuint borderXWrite)
        {
            if (gx > 0)
            {
                Rect from = new Rect((int)_groupDataXBorder, (int)_groupDataYBorder, (int)borderXWrite, (int)(grp.Y1 - grp.Y0));
                Rect to   = new Rect((int)(((gx * 2) - 1) * borderXWrite), (int)grp.Y0, (int)borderXWrite, (int)(grp.Y1 - grp.Y0));
                Status s  = ImageOps.CopyImageTo(from, inPlane, to, _bordersVertical[(int)c]);

                if (!s)
                {
                    return s;
                }
            }

            if (gx + 1 < FrameDimensions.XSizeGroups)
            {
                Rect from = new Rect(
                    (int)(_groupDataXBorder + grp.X1 - grp.X0 - borderXWrite),
                    (int)_groupDataYBorder,
                    (int)borderXWrite,
                    (int)(grp.Y1 - grp.Y0));
                Rect to  = new Rect((int)(gx * 2 * borderXWrite), (int)grp.Y0, (int)borderXWrite, (int)(grp.Y1 - grp.Y0));
                Status s = ImageOps.CopyImageTo(from, inPlane, to, _bordersVertical[(int)c]);

                if (!s)
                {
                    return s;
                }
            }

            return true;
        }

        private Status LoadBorders(nuint groupId, nuint c, in Rect r, ImagePlane<float> outPlane)
        {
            nuint gy                                 = groupId / FrameDimensions.XSizeGroups;
            nuint gx                                 = groupId % FrameDimensions.XSizeGroups;
            int hshift                               = (int)ChannelShifts[0][c].X;
            int vshift                               = (int)ChannelShifts[0][c].Y;
            nuint x0                                 = gx * GroupInputXSize(c);
            nuint x1                                 = Math.Min((gx + 1) * GroupInputXSize(c), Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << hshift));
            nuint y0                                 = gy * GroupInputYSize(c);
            nuint y1                                 = Math.Min((gy + 1) * GroupInputYSize(c), Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << vshift));
            nuint paddingX                           = Padding[0][c].X;
            nuint paddingY                           = Padding[0][c].Y;
            (nuint borderXWrite, nuint borderYWrite) = BorderToStore(c);

            nuint x0src = Common.DivCeil((nuint)r.X0 << _baseColorShift, 1u << hshift);

            if (x0src != 0)
            {
                x0src -= paddingX;
            }

            nuint x1src = Common.DivCeil((nuint)(r.X0 + r.XSize) << _baseColorShift, 1u << hshift);
            x1src       = Math.Min(x1src + paddingX, Common.DivCeil(FrameDimensions.XSizeUpsampled, 1u << hshift));

            nuint y0src = Common.DivCeil((nuint)r.Y0 << _baseColorShift, 1u << vshift);

            if (y0src != 0)
            {
                y0src -= paddingY;
            }

            nuint y1src = Common.DivCeil((nuint)(r.Y0 + r.YSize) << _baseColorShift, 1u << vshift);
            y1src       = Math.Min(y1src + paddingY, Common.DivCeil(FrameDimensions.YSizeUpsampled, 1u << vshift));

            Status hStatus = LoadHorizontalBorders(
                c,
                outPlane,
                gy,
                (x0, y0, y1),
                (x0src, x1src, y0src, y1src),
                borderYWrite);

            if (!hStatus)
            {
                return hStatus;
            }

            return LoadVerticalBorders(
                c,
                outPlane,
                gx,
                (x0, x1, y0),
                (x0src, x1src, y0src, y1src),
                borderXWrite);
        }

        private Status LoadHorizontalBorders(
            nuint c,
            ImagePlane<float> outPlane,
            nuint gy,
            (nuint X0, nuint Y0, nuint Y1) grp,
            (nuint X0Src, nuint X1Src, nuint Y0Src, nuint Y1Src) src,
            nuint borderYWrite)
        {
            if (src.Y0Src < grp.Y0)
            {
                Rect from = new Rect(
                    (int)src.X0Src,
                    (int)(((gy * 2) - 2) * borderYWrite),
                    (int)(src.X1Src - src.X0Src),
                    (int)borderYWrite);
                Rect to   = new Rect(
                    (int)((nint)_groupDataXBorder + (nint)src.X0Src - (nint)grp.X0),
                    (int)(_groupDataYBorder - borderYWrite),
                    (int)(src.X1Src - src.X0Src),
                    (int)borderYWrite);
                Status s  = ImageOps.CopyImageTo(from, _bordersHorizontal[(int)c], to, outPlane);

                if (!s)
                {
                    return s;
                }
            }

            if (src.Y1Src > grp.Y1)
            {
                Rect from = new Rect(
                    (int)src.X0Src,
                    (int)(((gy * 2) + 1) * borderYWrite),
                    (int)(src.X1Src - src.X0Src),
                    (int)borderYWrite);
                Rect to   = new Rect(
                    (int)((nint)_groupDataXBorder + (nint)src.X0Src - (nint)grp.X0),
                    (int)(_groupDataYBorder + grp.Y1 - grp.Y0),
                    (int)(src.X1Src - src.X0Src),
                    (int)borderYWrite);
                Status s  = ImageOps.CopyImageTo(from, _bordersHorizontal[(int)c], to, outPlane);

                if (!s)
                {
                    return s;
                }
            }

            return true;
        }

        private Status LoadVerticalBorders(
            nuint c,
            ImagePlane<float> outPlane,
            nuint gx,
            (nuint X0, nuint X1, nuint Y0) grp,
            (nuint X0Src, nuint X1Src, nuint Y0Src, nuint Y1Src) src,
            nuint borderXWrite)
        {
            if (src.X0Src < grp.X0)
            {
                Rect from = new Rect(
                    (int)(((gx * 2) - 2) * borderXWrite),
                    (int)src.Y0Src,
                    (int)borderXWrite,
                    (int)(src.Y1Src - src.Y0Src));
                Rect to   = new Rect(
                    (int)(_groupDataXBorder - borderXWrite),
                    (int)((nint)_groupDataYBorder + (nint)src.Y0Src - (nint)grp.Y0),
                    (int)borderXWrite,
                    (int)(src.Y1Src - src.Y0Src));
                Status s  = ImageOps.CopyImageTo(from, _bordersVertical[(int)c], to, outPlane);

                if (!s)
                {
                    return s;
                }
            }

            if (src.X1Src > grp.X1)
            {
                Rect from = new Rect(
                    (int)(((gx * 2) + 1) * borderXWrite),
                    (int)src.Y0Src,
                    (int)borderXWrite,
                    (int)(src.Y1Src - src.Y0Src));
                Rect to   = new Rect(
                    (int)(_groupDataXBorder + grp.X1 - grp.X0),
                    (int)((nint)_groupDataYBorder + (nint)src.Y0Src - (nint)grp.Y0),
                    (int)borderXWrite,
                    (int)(src.Y1Src - src.Y0Src));
                Status s  = ImageOps.CopyImageTo(from, _bordersVertical[(int)c], to, outPlane);

                if (!s)
                {
                    return s;
                }
            }

            return true;
        }

        private Status RenderGroupOutOfFramePadding(nuint groupId, nuint threadId, nuint gx, nuint gy)
        {
            int groupDim       = (int)(FrameDimensions.GroupDim << _baseColorShift);
            Rect groupRect     = new Rect((int)gx * groupDim, (int)gy * groupDim, groupDim, groupDim);
            Rect imageRect     = new Rect(0, 0, (int)FrameDimensions.XSizeUpsampled, (int)FrameDimensions.YSizeUpsampled);
            Rect fullImageRect = new Rect(0, 0, (int)_fullImageXSize, (int)_fullImageYSize);
            groupRect          = groupRect.Translate(_frameOrigin.X0, _frameOrigin.Y0);
            imageRect          = imageRect.Translate(_frameOrigin.X0, _frameOrigin.Y0).Intersection(fullImageRect);
            groupRect          = groupRect.Intersection(imageRect);

            if (groupId == 0 && (imageRect.XSize == 0 || imageRect.YSize == 0))
            {
                return RenderPadding(threadId, new Rect(0, 0, (int)_fullImageXSize, (int)_fullImageYSize));
            }

            if (groupRect.XSize <= 0 || groupRect.YSize <= 0)
            {
                return true;
            }

            return RenderIntersectingGroupPadding(threadId, gx, gy, in groupRect);
        }

        private Status RenderIntersectingGroupPadding(nuint threadId, nuint gx, nuint gy, in Rect groupRect)
        {
            int x0     = groupRect.X0;
            int y0     = groupRect.Y0;
            int x1     = groupRect.X1;
            int y1     = groupRect.Y1;
            int fullW  = (int)_fullImageXSize;
            int fullH  = (int)_fullImageYSize;
            bool lastX = gx + 1 == FrameDimensions.XSizeGroups;
            bool lastY = gy + 1 == FrameDimensions.YSizeGroups;

            if (gx == 0 && gy == 0 && !RenderPadding(threadId, new Rect(0, 0, x0, y0)))
            {
                return false;
            }

            if (gy == 0 && !RenderPadding(threadId, new Rect(x0, 0, x1 - x0, y0)))
            {
                return false;
            }

            if (gx == 0 && !RenderPadding(threadId, new Rect(0, y0, x0, y1 - y0)))
            {
                return false;
            }

            if (gx == 0 && lastY && !RenderPadding(threadId, new Rect(0, y1, x0, fullH - y1)))
            {
                return false;
            }

            if (lastY && !RenderPadding(threadId, new Rect(x0, y1, x1 - x0, fullH - y1)))
            {
                return false;
            }

            if (gy == 0 && lastX && !RenderPadding(threadId, new Rect(x1, 0, fullW - x1, y0)))
            {
                return false;
            }

            if (lastX && !RenderPadding(threadId, new Rect(x1, y0, fullW - x1, y1 - y0)))
            {
                return false;
            }

            if (lastY && lastX && !RenderPadding(threadId, new Rect(x1, y1, fullW - x1, fullH - y1)))
            {
                return false;
            }

            return true;
        }

        private Status RenderPadding(nuint threadId, Rect rect)
        {
            if (rect.XSize <= 0 || rect.YSize <= 0)
            {
                return true;
            }

            int numC                = ChannelShifts[0].Length;
            PipelineRow[][] inRows  = new PipelineRow[numC][];
            PipelineRow[][] outRows = Array.Empty<PipelineRow[]>();

            for (int c = 0; c < numC; c++)
            {
                inRows[c] = new PipelineRow[] { new PipelineRow(_outOfFrameData[threadId], (nuint)c) };
            }

            for (nuint y = 0; y < (nuint)rect.YSize; y++)
            {
                Stages[_firstImageDimStage - 1].ProcessPaddingRow(
                    inRows,
                    (nuint)rect.XSize,
                    (nuint)rect.X0,
                    (nuint)rect.Y0 + y);

                for (int i = _firstImageDimStage; i < Stages.Count; i++)
                {
                    Status s = Stages[i].ProcessRow(
                        inRows,
                        outRows,
                        (0, 0),
                        (nuint)rect.XSize,
                        (nuint)rect.X0,
                        (nuint)rect.Y0 + y,
                        threadId);

                    if (!s)
                    {
                        return s;
                    }
                }
            }

            return true;
        }

        private Status RenderRect(
            nuint threadId,
            ImagePlane<float>[] inputData,
            in Rect dataMaxColorChannelRect,
            in Rect imageMaxColorChannelRect)
        {
            int numStages      = Stages.Count;
            Rect[] groupRect   = new Rect[numStages];
            Rect imageAreaRect = imageMaxColorChannelRect
                .ShiftLeft(_baseColorShift)
                .Crop((int)FrameDimensions.XSizeUpsampled, (int)FrameDimensions.YSizeUpsampled);

            for (int i = 0; i < numStages; i++)
            {
                int c             = _anyc[i];
                StatusOr<Rect> gr = imageAreaRect.CeilShiftRight(
                    (int)ChannelShifts[i][c].X,
                    (int)ChannelShifts[i][c].Y);

                if (!gr.IsOk)
                {
                    return gr.Status;
                }

                groupRect[i] = gr.Value;
            }

            Rect[] span                          = ComputeStageSpans(groupRect, in imageAreaRect, out nint xPixelsSkip);
            StatusOr<RowBufferInfo[][]> rowsRes  = CreateRowBufferInfos(threadId, inputData, in dataMaxColorChannelRect);

            if (!rowsRes.IsOk)
            {
                return rowsRes.Status;
            }

            return ExecuteVirtualRows(
                threadId,
                inputData.Length,
                rowsRes.Value!,
                groupRect,
                span,
                in imageAreaRect,
                xPixelsSkip);
        }

        private Rect[] ComputeStageSpans(
            Rect[] groupRect,
            in Rect imageAreaRect,
            out nint xPixelsSkip)
        {
            nint fullImageX0 = _frameOrigin.X0 + imageAreaRect.X0;
            xPixelsSkip      = 0;

            if (fullImageX0 < 0)
            {
                xPixelsSkip = -fullImageX0;
                fullImageX0 = 0;
            }

            nint fullImageX1 = _frameOrigin.X0 + imageAreaRect.X1;
            Rect[] span      = new Rect[Stages.Count];

            for (int i = 0; i < Stages.Count; ++i)
            {
                if (i < _firstImageDimStage)
                {
                    span[i] = new Rect(groupRect[i].X0, 0, groupRect[i].XSize, _imageRect[i].YSize);
                }
                else
                {
                    nint croppedX1 = Math.Min(fullImageX1, (nint)_fullImageXSize);
                    nint spanW     = Math.Max(0, croppedX1 - fullImageX0);
                    span[i]        = new Rect((int)fullImageX0, 0, (int)spanW, (int)_fullImageYSize);
                }
            }

            return span;
        }

        private StatusOr<RowBufferInfo[][]> CreateRowBufferInfos(
            nuint threadId,
            ImagePlane<float>[] inputData,
            in Rect dataMaxColorChannelRect)
        {
            int numStages          = Stages.Count;
            int numChannels        = inputData.Length;
            RowBufferInfo[][] rows = new RowBufferInfo[numStages + 1][];

            for (int i = 0; i <= numStages; i++)
            {
                rows[i] = new RowBufferInfo[numChannels];
            }

            for (int i = 0; i < numStages; i++)
            {
                for (int c = 0; c < numChannels; c++)
                {
                    if (Stages[i].GetChannelMode((nuint)c) == RenderPipelineChannelMode.InOut)
                    {
                        ImagePlane<float> buffer = _stageData[threadId][c][i]!;
                        rows[i + 1][c]           = new RowBufferInfo
                        {
                            BaseRow    = new PipelineRow(buffer, 0),
                            YModMinus1 = (int)buffer.YSize - 1,
                            Stride     = buffer.PixelsPerRow,
                        };
                    }
                }
            }

            for (int c = 0; c < numChannels; c++)
            {
                Rect tmp = dataMaxColorChannelRect
                    .Translate(-(int)_groupDataXBorder, -(int)_groupDataYBorder)
                    .ShiftLeft(_baseColorShift);
                StatusOr<Rect> shifted = tmp.CeilShiftRight(
                    (int)ChannelShifts[0][c].X,
                    (int)ChannelShifts[0][c].Y);

                if (!shifted.IsOk)
                {
                    return shifted.Status;
                }

                Rect chRect = shifted.Value.Translate(
                    (int)_groupDataXBorder - RenderPipelineStage.KRenderPipelineXOffset,
                    (int)_groupDataYBorder);
                rows[0][c] = new RowBufferInfo
                {
                    BaseRow    = new PipelineRow(inputData[c], (nuint)chRect.Y0, chRect.X0),
                    Stride     = inputData[c].PixelsPerRow,
                    YModMinus1 = -1,
                };
            }

            return rows;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PipelineRow GetRowBuffer(RowBufferInfo[][] rows, int stage, int y, int c)
        {
            RowBufferInfo info = rows[stage + 1][c];

            return info.BaseRow + (info.Stride * (y & info.YModMinus1));
        }

        private Status ExecuteVirtualRows(
            nuint threadId,
            int numChannels,
            RowBufferInfo[][] rows,
            Rect[] groupRect,
            Rect[] span,
            in Rect imageAreaRect,
            nint xPixelsSkip)
        {
            PipelineRow[][][] inputRows = new PipelineRow[_firstTrailingStage + 1][][];

            for (int i = 0; i < _firstTrailingStage; i++)
            {
                inputRows[i] = new PipelineRow[numChannels][];
            }

            inputRows[_firstTrailingStage] = new PipelineRow[numChannels][];

            for (int c = 0; c < numChannels; c++)
            {
                inputRows[_firstTrailingStage][c] = new PipelineRow[1];
            }

            PipelineRow[][] outputRows = new PipelineRow[numChannels][];

            for (int c = 0; c < numChannels; c++)
            {
                outputRows[c] = new PipelineRow[8];
            }

            int numExtraRows = 0;

            for (int i = 0; i < _virtualYpaddingForOutput.Length; i++)
            {
                if (_virtualYpaddingForOutput[i] > numExtraRows)
                {
                    numExtraRows = _virtualYpaddingForOutput[i];
                }
            }

            for (int vy = -numExtraRows; vy < imageAreaRect.YSize + numExtraRows; vy++)
            {
                Status inOutStatus = ProcessInOutStagesForVy(
                    (vy, numExtraRows, numChannels),
                    rows,
                    groupRect,
                    span,
                    inputRows,
                    outputRows,
                    threadId);

                if (!inOutStatus)
                {
                    return inOutStatus;
                }

                Status trailStatus = ProcessTrailingStagesForVy(
                    (vy, numExtraRows, numChannels, xPixelsSkip),
                    rows,
                    span,
                    in imageAreaRect,
                    inputRows[_firstTrailingStage],
                    outputRows,
                    threadId);

                if (!trailStatus)
                {
                    return trailStatus;
                }
            }

            return true;
        }

        private Status ProcessInOutStagesForVy(
            (int Vy, int NumExtraRows, int NumChannels) vyInfo,
            RowBufferInfo[][] rows,
            Rect[] groupRect,
            Rect[] span,
            PipelineRow[][][] inputRows,
            PipelineRow[][] outputRows,
            nuint threadId)
        {
            for (int i = 0; i < _firstTrailingStage; i++)
            {
                int stageVy = vyInfo.Vy - vyInfo.NumExtraRows + _virtualYpaddingForOutput[i];
                int vShift  = (int)ChannelShifts[i][_anyc[i]].Y;

                if ((stageVy % (1 << vShift)) != 0 || stageVy < -_virtualYpaddingForOutput[i])
                {
                    continue;
                }

                int y      = stageVy >> vShift;
                int imageY = groupRect[i].Y0 + y;

                if (imageY < 0 || imageY >= span[i].Y1)
                {
                    continue;
                }

                PrepareIoRowsForStage((y, i, vyInfo.NumChannels), rows, in groupRect[i], in _imageRect[i], inputRows[i], outputRows);

                if (span[i].XSize == 0)
                {
                    continue;
                }

                nuint xpos        = (nuint)span[i].X0;
                nuint xsize       = (nuint)span[i].XSize;
                nuint xdataRight  = (nuint)_imageRect[i].XSize - xpos - xsize;
                nuint xextraLeft  = Math.Min(xpos, (nuint)_xpaddingForOutput[i]);
                nuint xextraRight = Math.Min(xdataRight, (nuint)_xpaddingForOutput[i]);

                Status s = Stages[i].ProcessRow(
                    inputRows[i],
                    outputRows,
                    (xextraLeft, xextraRight),
                    xsize,
                    xpos,
                    (nuint)imageY,
                    threadId);

                if (!s)
                {
                    return s;
                }
            }

            return true;
        }

        private void PrepareIoRowsForStage(
            (int Y, int StageIdx, int NumChannels) ctx,
            RowBufferInfo[][] rows,
            in Rect stageGroupRect,
            in Rect stageImageRect,
            PipelineRow[][] stageInputRows,
            PipelineRow[][] outputRows)
        {
            int borderY = (int)Stages[ctx.StageIdx].StageSettings.BorderY;
            int borderX = (int)Stages[ctx.StageIdx].StageSettings.BorderX;
            int shiftY  = (int)Stages[ctx.StageIdx].StageSettings.ShiftY;
            int totalIn = (2 * borderY) + 1;

            for (int c = 0; c < ctx.NumChannels; c++)
            {
                RenderPipelineChannelMode mode = Stages[ctx.StageIdx].GetChannelMode((nuint)c);

                if (mode == RenderPipelineChannelMode.Ignored)
                {
                    continue;
                }

                if (stageInputRows[c] != null && stageInputRows[c].Length == totalIn)
                {
                    for (int iy = 0; iy < 2 * borderY; iy++)
                    {
                        stageInputRows[c][iy] = stageInputRows[c][iy + 1];
                    }

                    MakeStageInputRow(
                        (ctx.Y, ctx.StageIdx, c, borderY * 2, borderY, borderX),
                        rows,
                        in stageGroupRect,
                        in stageImageRect,
                        stageInputRows[c]);
                }
                else
                {
                    stageInputRows[c] = new PipelineRow[totalIn];

                    for (int iy = 0; iy < totalIn; iy++)
                    {
                        MakeStageInputRow(
                            (ctx.Y, ctx.StageIdx, c, iy, borderY, borderX),
                            rows,
                            in stageGroupRect,
                            in stageImageRect,
                            stageInputRows[c]);
                    }
                }

                if (mode == RenderPipelineChannelMode.InOut)
                {
                    int bundle = 1 << shiftY;

                    for (int iy = 0; iy < bundle; iy++)
                    {
                        outputRows[c][iy] = GetRowBuffer(rows, ctx.StageIdx, (ctx.Y * bundle) + iy, c);
                    }
                }
            }
        }

        private void MakeStageInputRow(
            (int Y, int StageIdx, int C, int Iy, int BorderY, int BorderX) info,
            RowBufferInfo[][] rows,
            in Rect stageGroupRect,
            in Rect stageImageRect,
            PipelineRow[] chRows)
        {
            int mirroredY   = GetMirroredY(info.Y + info.Iy - info.BorderY, stageGroupRect.Y0, stageImageRect.YSize);
            chRows[info.Iy] = GetRowBuffer(rows, _stageInputForChannel[info.StageIdx][info.C], mirroredY, info.C);
            ApplyXMirroring(chRows[info.Iy], info.BorderX, stageGroupRect.X0, stageGroupRect.XSize, stageImageRect.XSize);
        }

        private Status ProcessTrailingStagesForVy(
            (int Vy, int NumExtraRows, int NumChannels, nint XPixelsSkip) vyInfo,
            RowBufferInfo[][] rows,
            Rect[] span,
            in Rect imageAreaRect,
            PipelineRow[][] trailInputRows,
            PipelineRow[][] outputRows,
            nuint threadId)
        {
            int y = vyInfo.Vy - vyInfo.NumExtraRows;

            if (y < 0 || y >= imageAreaRect.YSize)
            {
                return true;
            }

            for (int c = 0; c < vyInfo.NumChannels; c++)
            {
                trailInputRows[c][0] = GetRowBuffer(rows, _stageInputForChannel[_firstTrailingStage][c], y, c);
            }

            for (int i = _firstTrailingStage; i < _firstImageDimStage; i++)
            {
                if (span[i].XSize == 0)
                {
                    continue;
                }

                int y0 = imageAreaRect.Y0 + y;

                if (y0 >= span[i].Y1)
                {
                    continue;
                }

                Status s = Stages[i].ProcessRow(
                    trailInputRows,
                    outputRows,
                    (0, 0),
                    (nuint)span[i].XSize,
                    (nuint)span[i].X0,
                    (nuint)y0,
                    threadId);

                if (!s)
                {
                    return s;
                }
            }

            if (_firstImageDimStage == Stages.Count)
            {
                return true;
            }

            for (int c = 0; c < vyInfo.NumChannels; c++)
            {
                trailInputRows[c][0] += vyInfo.XPixelsSkip;
            }

            nint fullImageY = _frameOrigin.Y0 + imageAreaRect.Y0 + y;

            if (fullImageY < 0)
            {
                return true;
            }

            for (int i = _firstImageDimStage; i < Stages.Count; i++)
            {
                if (span[i].XSize == 0 || fullImageY >= span[i].Y1)
                {
                    continue;
                }

                Status s = Stages[i].ProcessRow(
                    trailInputRows,
                    outputRows,
                    (0, 0),
                    (nuint)span[i].XSize,
                    (nuint)span[i].X0,
                    (nuint)fullImageY,
                    threadId);

                if (!s)
                {
                    return s;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetMirroredY(int y, int groupY0, int imageYSize)
        {
            if (groupY0 == 0 && (y < 0 || y + groupY0 >= imageYSize))
            {
                return (int)ImageOps.Mirror(y, imageYSize);
            }

            if (y + groupY0 >= imageYSize)
            {
                return (2 * imageYSize) - (y + groupY0) - 1 - groupY0;
            }

            return y;
        }

        private static void ApplyXMirroring(
            PipelineRow row,
            int borderx,
            int groupX0,
            int groupXSize,
            int imageXSize)
        {
            int offset = RenderPipelineStage.KRenderPipelineXOffset;

            if (imageXSize <= borderx)
            {
                if (groupX0 == 0)
                {
                    for (int ix = 0; ix < borderx; ix++)
                    {
                        row[offset - ix - 1] = row[offset + (int)ImageOps.Mirror(-ix - 1, imageXSize)];
                    }
                }

                if (groupXSize + borderx + groupX0 >= imageXSize)
                {
                    for (int ix = 0; ix < borderx; ix++)
                    {
                        row[offset + imageXSize + ix - groupX0] =
                            row[offset + (int)ImageOps.Mirror(imageXSize + ix, imageXSize) - groupX0];
                    }
                }
            }
            else
            {
                if (groupX0 == 0)
                {
                    for (int ix = 0; ix < borderx; ix++)
                    {
                        row[offset - ix - 1] = row[offset + ix];
                    }
                }

                if (groupXSize + borderx + groupX0 >= imageXSize)
                {
                    for (int ix = 0; ix < borderx; ix++)
                    {
                        row[offset + imageXSize - groupX0 + ix] = row[offset + imageXSize - groupX0 - ix - 1];
                    }
                }
            }
        }
    }
}
