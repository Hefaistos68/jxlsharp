// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

// Structures that hold the (en/de)coder state for a JPEG XL kVarDCT
// (en/de)coder, per-frame decoder cache, and blending/output render pipeline stages.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using JxlSharp.Base;
using JxlSharp.Cms;
using ThreadPool = JxlSharp.Base.ThreadPool;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Callback function type for receiving decoded image pixel stripes on a single or multiple threads.
    /// Ported from <c>JxlImageOutCallback</c> in <c>include/jxl/decode.h</c>.
    /// </summary>
    /// <param name="opaque">User-supplied opaque state.</param>
    /// <param name="x">Horizontal position of the leftmost pixel.</param>
    /// <param name="y">Vertical position of the scanline.</param>
    /// <param name="numPixels">Number of pixels in the horizontal stripe.</param>
    /// <param name="pixels">Pointer to the interleaved pixel data.</param>
    public unsafe delegate void JxlImageOutCallback(
        object? opaque,
        nuint x,
        nuint y,
        nuint numPixels,
        void* pixels);

    /// <summary>
    /// Initialization callback for multithreaded pixel output.
    /// Ported from <c>JxlImageOutInitCallback</c> in <c>include/jxl/decode.h</c>.
    /// </summary>
    /// <param name="initOpaque">User-supplied initialization state.</param>
    /// <param name="numThreads">Maximum number of concurrent worker threads.</param>
    /// <param name="numPixelsPerThread">Maximum number of pixels passed per <see cref="JxlImageOutRunCallback"/> call.</param>
    /// <returns>Opaque state passed to <see cref="JxlImageOutRunCallback"/> and <see cref="JxlImageOutDestroyCallback"/>, or <c>null</c> on failure.</returns>
    public delegate object? JxlImageOutInitCallback(
        object? initOpaque,
        nuint numThreads,
        nuint numPixelsPerThread);

    /// <summary>
    /// Worker callback for multithreaded pixel output.
    /// Ported from <c>JxlImageOutRunCallback</c> in <c>include/jxl/decode.h</c>.
    /// </summary>
    /// <param name="runOpaque">Opaque state returned by <see cref="JxlImageOutInitCallback"/>.</param>
    /// <param name="threadId">Worker thread index in <c>[0, numThreads)</c>.</param>
    /// <param name="x">Horizontal position of the leftmost pixel.</param>
    /// <param name="y">Vertical position of the scanline.</param>
    /// <param name="numPixels">Number of pixels in the horizontal stripe.</param>
    /// <param name="pixels">Pointer to the interleaved pixel data.</param>
    public unsafe delegate void JxlImageOutRunCallback(
        object? runOpaque,
        nuint threadId,
        nuint x,
        nuint y,
        nuint numPixels,
        void* pixels);

    /// <summary>
    /// Destruction callback for multithreaded pixel output.
    /// Ported from <c>JxlImageOutDestroyCallback</c> in <c>include/jxl/decode.h</c>.
    /// </summary>
    /// <param name="runOpaque">Opaque state returned by <see cref="JxlImageOutInitCallback"/>.</param>
    public delegate void JxlImageOutDestroyCallback(object? runOpaque);

    /// <summary>
    /// Discriminated union pointer to either 16-bit or 32-bit quantized AC coefficients.
    /// Ported from <c>jxl::ACPtr</c> in <c>lib/jxl/dct_util.h</c>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ACPtr
    {
        /// <summary>Pointer to 32-bit signed integer coefficients.</summary>
        [FieldOffset(0)]
        public int* Ptr32;

        /// <summary>Pointer to 16-bit signed integer coefficients.</summary>
        [FieldOffset(0)]
        public short* Ptr16;

        /// <summary>
        /// Initializes a new instance of the <see cref="ACPtr"/> struct with a 16-bit pointer.
        /// </summary>
        /// <param name="p">16-bit coefficient pointer.</param>
        public ACPtr(short* p)
        {
            Ptr32 = null;
            Ptr16 = p;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ACPtr"/> struct with a 32-bit pointer.
        /// </summary>
        /// <param name="p">32-bit coefficient pointer.</param>
        public ACPtr(int* p)
        {
            Ptr16 = null;
            Ptr32 = p;
        }
    }

    /// <summary>
    /// Read-only union pointer to either 16-bit or 32-bit quantized AC coefficients.
    /// Ported from <c>jxl::ConstACPtr</c> in <c>lib/jxl/dct_util.h</c>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ConstACPtr
    {
        /// <summary>Pointer to 32-bit signed integer coefficients.</summary>
        [FieldOffset(0)]
        public int* Ptr32;

        /// <summary>Pointer to 16-bit signed integer coefficients.</summary>
        [FieldOffset(0)]
        public short* Ptr16;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConstACPtr"/> struct with a 16-bit pointer.
        /// </summary>
        /// <param name="p">16-bit coefficient pointer.</param>
        public ConstACPtr(short* p)
        {
            Ptr32 = null;
            Ptr16 = p;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConstACPtr"/> struct with a 32-bit pointer.
        /// </summary>
        /// <param name="p">32-bit coefficient pointer.</param>
        public ConstACPtr(int* p)
        {
            Ptr16 = null;
            Ptr32 = p;
        }
    }

    /// <summary>
    /// Element width of an <see cref="ACImage"/> buffer.
    /// Ported from <c>jxl::ACType</c> in <c>lib/jxl/dct_util.h</c>.
    /// </summary>
    public enum ACType
    {
        /// <summary>16-bit signed integer coefficients.</summary>
        k16 = 0,

        /// <summary>32-bit signed integer coefficients.</summary>
        k32 = 1,
    }

    /// <summary>
    /// Abstract 3-plane quantized AC coefficient image (either 16-bit or 32-bit).
    /// Ported from <c>jxl::ACImage</c> in <c>lib/jxl/dct_util.h</c>.
    /// </summary>
    public abstract class ACImage : IDisposable
    {
        /// <summary>Gets the coefficient storage type (<see cref="ACType.k16"/> or <see cref="ACType.k32"/>).</summary>
        public abstract ACType Type { get; }

        /// <summary>Gets the stride in elements between consecutive rows.</summary>
        public abstract nuint PixelsPerRow { get; }

        /// <summary>Gets a value indicating whether the image is unallocated or empty.</summary>
        public abstract bool IsEmpty { get; }

        /// <summary>
        /// Returns a writable <see cref="ACPtr"/> to channel <paramref name="c"/>, row <paramref name="y"/>, offset by <paramref name="xbase"/>.
        /// </summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <param name="xbase">Horizontal element offset.</param>
        /// <returns><see cref="ACPtr"/> at the requested location.</returns>
        public abstract ACPtr PlaneRow(nuint c, nuint y, nuint xbase);

        /// <summary>
        /// Returns a read-only <see cref="ConstACPtr"/> to channel <paramref name="c"/>, row <paramref name="y"/>, offset by <paramref name="xbase"/>.
        /// </summary>
        /// <param name="c">Channel index (0..2).</param>
        /// <param name="y">Row index.</param>
        /// <param name="xbase">Horizontal element offset.</param>
        /// <returns><see cref="ConstACPtr"/> at the requested location.</returns>
        public abstract ConstACPtr ConstPlaneRow(nuint c, nuint y, nuint xbase);

        /// <summary>Zero-fills all 3 planes.</summary>
        public abstract void ZeroFill();

        /// <summary>Zero-fills plane <paramref name="c"/>.</summary>
        /// <param name="c">Channel index (0..2).</param>
        public abstract void ZeroFillPlane(nuint c);

        /// <inheritdoc />
        public abstract void Dispose();
    }

    /// <summary>
    /// Concrete 3-plane quantized AC coefficient image parameterized by <typeparamref name="T"/> (<see cref="short"/> or <see cref="int"/>).
    /// Ported from <c>jxl::ACImageT&lt;T&gt;</c> in <c>lib/jxl/dct_util.h</c>.
    /// </summary>
    /// <typeparam name="T">Coefficient element type (<see cref="short"/> or <see cref="int"/>).</typeparam>
    public sealed unsafe class ACImageT<T> : ACImage
        where T : unmanaged
    {
        private Image3<T> _img = new Image3<T>();

        /// <summary>
        /// Initializes a new empty instance of the <see cref="ACImageT{T}"/> class.
        /// </summary>
        public ACImageT()
        {
            if (sizeof(T) != 2 && sizeof(T) != 4)
            {
                throw new NotSupportedException("ACImage must be either 32- or 16-bit");
            }
        }

        /// <inheritdoc />
        public override ACType Type => sizeof(T) == 2 ? ACType.k16 : ACType.k32;

        /// <inheritdoc />
        public override nuint PixelsPerRow => (nuint)_img.PixelsPerRow;

        /// <inheritdoc />
        public override bool IsEmpty => _img.XSize == 0 || _img.YSize == 0;

        /// <summary>
        /// Allocates a new <see cref="ACImageT{T}"/> of dimensions <paramref name="xsize"/> x <paramref name="ysize"/>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation.</param>
        /// <param name="xsize">Width in elements.</param>
        /// <param name="ysize">Height in rows.</param>
        /// <returns><see cref="StatusOr{T}"/> containing the allocated <see cref="ACImageT{T}"/>.</returns>
        public static StatusOr<ACImageT<T>> Make(JxlMemoryManager? memoryManager, nuint xsize, nuint ysize)
        {
            ACImageT<T> result = new ACImageT<T>();
            StatusOr<Image3<T>> imgOr = Image3<T>.Create(memoryManager ?? JxlMemoryManager.Default, xsize, ysize);

            if (!imgOr.IsOk)
            {
                return imgOr.Status;
            }

            result._img = imgOr.Value;

            return result;
        }

        /// <inheritdoc />
        public override ACPtr PlaneRow(nuint c, nuint y, nuint xbase)
        {
            T* ptr = _img.PlaneRow(c, y) + xbase;

            return sizeof(T) == 2 ? new ACPtr((short*)ptr) : new ACPtr((int*)ptr);
        }

        /// <inheritdoc />
        public override ConstACPtr ConstPlaneRow(nuint c, nuint y, nuint xbase)
        {
            T* ptr = _img.ConstPlaneRow(c, y) + xbase;

            return sizeof(T) == 2 ? new ConstACPtr((short*)ptr) : new ConstACPtr((int*)ptr);
        }

        /// <inheritdoc />
        public override void ZeroFill()
        {
            ImageOps.ZeroFillImage(_img);
        }

        /// <inheritdoc />
        public override void ZeroFillPlane(nuint c)
        {
            ImageOps.ZeroFillImage(_img.Plane(c));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            _img.Dispose();
        }
    }

    /// <summary>
    /// Holds per-frame additive image features (noise parameters, patches, and splines).
    /// Ported from <c>jxl::ImageFeatures</c> in <c>lib/jxl/passes_state.h</c>.
    /// </summary>
    public sealed class ImageFeatures
    {
        /// <summary>Photon noise synthesis parameters.</summary>
        public NoiseParams NoiseParams = new NoiseParams();

        /// <summary>Decoded patch dictionary.</summary>
        public PatchDictionary Patches;

        /// <summary>Decoded splines collection.</summary>
        public Splines Splines;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageFeatures"/> class.
        /// </summary>
        /// <param name="memoryManager">Memory manager used by patches and splines.</param>
        public ImageFeatures(JxlMemoryManager? memoryManager = null)
        {
            Patches = new PatchDictionary(memoryManager);
            Splines = new Splines(memoryManager);
        }
    }

    /// <summary>
    /// State common to both the VarDCT encoder and decoder.
    /// Ported from <c>jxl::PassesSharedState</c> in <c>lib/jxl/passes_state.h</c> and <c>lib/jxl/passes_state.cc</c>.
    /// </summary>
    public sealed class PassesSharedState : IDisposable
    {
        /// <summary>Memory manager for allocations.</summary>
        public JxlMemoryManager? MemoryManager;

        /// <summary>Codestream metadata.</summary>
        public CodecMetadata? Metadata;

        /// <summary>Frame dimensions.</summary>
        public FrameDimensions FrameDim = new FrameDimensions();

        // Control fields and parameters.

        /// <summary>Per-block AC transform strategy map.</summary>
        public AcStrategyImage AcStrategy = new AcStrategyImage();

        // Dequant matrices + quantizer.

        /// <summary>Dequantization matrices.</summary>
        public DequantMatrices Matrices = new DequantMatrices();

        /// <summary>Quantizer instance bound to <see cref="Matrices"/>.</summary>
        public Quantizer Quantizer;

        /// <summary>Per-block raw quantization field.</summary>
        public ImagePlane<int> RawQuantField = new ImagePlane<int>();

        // Per-block side information for EPF detail preservation.

        /// <summary>Per-block EPF sharpness modulation field.</summary>
        public ImagePlane<byte> EpfSharpness = new ImagePlane<byte>();

        /// <summary>Chroma-from-luma correlation map.</summary>
        public ColorCorrelationMap Cmap = new ColorCorrelationMap();

        /// <summary>Noise, patches, and splines features.</summary>
        public ImageFeatures ImageFeatures;

        // Memory area for storing coefficient orders.
        // `coeff_order_size` is the size used by *one* set of coefficient orders (at
        // most kMaxCoeffOrderSize). A set of coefficient orders is present for each
        // pass.

        /// <summary>Size used by one set of coefficient orders.</summary>
        public nuint CoeffOrderSize;

        /// <summary>Flattened coefficient orders across passes.</summary>
        public uint[] CoeffOrders = Array.Empty<uint>();

        // Decoder-side DC and quantized DC.

        /// <summary>Decoder-side quantized DC plane.</summary>
        public ImagePlane<byte> QuantDc = new ImagePlane<byte>();

        /// <summary>Owned decoder-side DC storage.</summary>
        public Image3<float> DcStorage = new Image3<float>();

        /// <summary>Active DC image reference (points to <see cref="DcStorage"/> or an entry in <see cref="DcFrames"/>).</summary>
        public Image3<float> Dc;

        /// <summary>Block context map for AC entropy decoding.</summary>
        public BlockCtxMap BlockCtxMap = new BlockCtxMap();

        /// <summary>Saved progressive DC frames at levels 0..3.</summary>
        public Image3<float>[] DcFrames = new Image3<float>[4]
        {
            new Image3<float>(),
            new Image3<float>(),
            new Image3<float>(),
            new Image3<float>(),
        };

        /// <summary>Saved reference frames at slots 0..3.</summary>
        public ReferenceFrame[] ReferenceFrames = new ReferenceFrame[4];

        // Number of pre-clustered set of histograms (with the same ctx map), per
        // pass. Encoded as num_histograms_ - 1.

        /// <summary>Number of histogram clusters per pass.</summary>
        public nuint NumHistograms;

        /// <summary>
        /// Initializes a new instance of the <see cref="PassesSharedState"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public PassesSharedState(JxlMemoryManager? memoryManager = null)
        {
            MemoryManager = memoryManager ?? JxlMemoryManager.Default;
            Quantizer     = new Quantizer(Matrices);
            ImageFeatures = new ImageFeatures(MemoryManager);
            Dc            = DcStorage;

            for (int i = 0; i < 4; i++)
            {
                ReferenceFrames[i] = new ReferenceFrame
                {
                    Frame = new ImageBundle(MemoryManager),
                };
            }
        }

        /// <summary>
        /// Initializes the state information shared between encoder and decoder for <paramref name="frameHeader"/>.
        /// Ported from <c>jxl::InitializePassesSharedState</c> in <c>lib/jxl/passes_state.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="shared">Shared state to initialize.</param>
        /// <param name="encoder">Whether called from the encoder.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public static Status InitializePassesSharedState(
            FrameHeader frameHeader,
            PassesSharedState shared,
            bool encoder = false)
        {
            if (frameHeader.NonserializedMetadata == null)
            {
                return Status.Failure("frame_header.nonserialized_metadata != nullptr failed");
            }

            shared.Metadata  = frameHeader.NonserializedMetadata;
            shared.FrameDim  = frameHeader.ToFrameDimensions();
            shared.ImageFeatures.Patches.SetShared(shared.ReferenceFrames);

            Status allocStatus = AllocateSharedControlFields(shared);

            if (!allocStatus)
            {
                return allocStatus;
            }

            // In the decoder, we allocate coeff orders afterwards, when we know how many
            // we will actually need.
            shared.CoeffOrderSize = CoeffOrder.kCoeffOrderMaxSize;
            nuint requiredOrders  = frameHeader.Passes.NumPasses * CoeffOrder.kCoeffOrderMaxSize;

            if (encoder &&
                (nuint)shared.CoeffOrders.Length < requiredOrders &&
                frameHeader.Encoding == FrameEncoding.VarDCT)
            {
                Array.Resize(ref shared.CoeffOrders, (int)requiredOrders);
            }

            return InitializeSharedDcBuffers(frameHeader, shared, encoder);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            AcStrategy.Dispose();
            RawQuantField.Dispose();
            EpfSharpness.Dispose();
            Cmap.Dispose();
            QuantDc.Dispose();
            DcStorage.Dispose();

            for (int i = 0; i < 4; i++)
            {
                DcFrames[i].Dispose();
                ReferenceFrames[i].Dispose();
            }
        }

        private static Status AllocateSharedControlFields(PassesSharedState shared)
        {
            FrameDimensions frameDim = shared.FrameDim;
            JxlMemoryManager mm      = shared.MemoryManager ?? JxlMemoryManager.Default;

            StatusOr<AcStrategyImage> acsOr = AcStrategyImage.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

            if (!acsOr.IsOk)
            {
                return acsOr.Status;
            }

            shared.AcStrategy.Dispose();
            shared.AcStrategy = acsOr.Value;

            StatusOr<ImagePlane<int>> qfOr = ImagePlane<int>.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

            if (!qfOr.IsOk)
            {
                return qfOr.Status;
            }

            shared.RawQuantField.Dispose();
            shared.RawQuantField = qfOr.Value;

            StatusOr<ImagePlane<byte>> epfOr = ImagePlane<byte>.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

            if (!epfOr.IsOk)
            {
                return epfOr.Status;
            }

            shared.EpfSharpness.Dispose();
            shared.EpfSharpness = epfOr.Value;

            StatusOr<ColorCorrelationMap> cmapOr = ColorCorrelationMap.Create(mm, frameDim.XSize, frameDim.YSize);

            if (!cmapOr.IsOk)
            {
                return cmapOr.Status;
            }

            shared.Cmap.Dispose();
            shared.Cmap = cmapOr.Value;

            StatusOr<ImagePlane<byte>> qdcOr = ImagePlane<byte>.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

            if (!qdcOr.IsOk)
            {
                return qdcOr.Status;
            }

            shared.QuantDc.Dispose();
            shared.QuantDc = qdcOr.Value;

            return true;
        }

        private static Status InitializeSharedDcBuffers(
            FrameHeader frameHeader,
            PassesSharedState shared,
            bool encoder)
        {
            FrameDimensions frameDim = shared.FrameDim;
            JxlMemoryManager mm      = shared.MemoryManager ?? JxlMemoryManager.Default;
            bool useDcFrame          = (frameHeader.Flags & FrameHeader.KUseDcFrame) != 0u;

            if (!encoder && useDcFrame)
            {
                if (frameHeader.DcLevel == 4)
                {
                    return Status.Failure($"Invalid DC level for kUseDcFrame: {frameHeader.DcLevel}");
                }

                shared.DcStorage.Dispose();
                shared.DcStorage = new Image3<float>();
                shared.Dc        = shared.DcFrames[frameHeader.DcLevel];

                if (shared.Dc.XSize == 0)
                {
                    return Status.Failure(
                        $"kUseDcFrame specified for dc_level {frameHeader.DcLevel}, but no frame was decoded with level {frameHeader.DcLevel + 1}");
                }

                ImageOps.ZeroFillImage(shared.QuantDc);
            }
            else
            {
                StatusOr<Image3<float>> dcOr = Image3<float>.Create(mm, frameDim.XSizeBlocks, frameDim.YSizeBlocks);

                if (!dcOr.IsOk)
                {
                    return dcOr.Status;
                }

                shared.DcStorage.Dispose();
                shared.DcStorage = dcOr.Value;
                shared.Dc        = shared.DcStorage;
            }

            return true;
        }
    }

    /// <summary>
    /// Wraps multithreaded pixel output callbacks (<see cref="Init"/>, <see cref="Run"/>, <see cref="Destroy"/>).
    /// Ported from <c>jxl::PixelCallback</c> in <c>lib/jxl/dec_cache.h</c>.
    /// </summary>
    public struct PixelCallback
    {
        /// <summary>Initialization callback.</summary>
        public JxlImageOutInitCallback? InitCallback;

        /// <summary>Per-stripe worker callback.</summary>
        public JxlImageOutRunCallback? Run;

        /// <summary>Cleanup callback.</summary>
        public JxlImageOutDestroyCallback? Destroy;

        /// <summary>Opaque user state passed to <see cref="InitCallback"/>.</summary>
        public object? InitOpaque;

        /// <summary>
        /// Initializes a new instance of the <see cref="PixelCallback"/> struct.
        /// </summary>
        /// <param name="init">Initialization callback.</param>
        /// <param name="run">Worker callback.</param>
        /// <param name="destroy">Cleanup callback.</param>
        /// <param name="initOpaque">Opaque user data.</param>
        public PixelCallback(
            JxlImageOutInitCallback? init,
            JxlImageOutRunCallback? run,
            JxlImageOutDestroyCallback? destroy,
            object? initOpaque)
        {
            bool hasInit    = init != null;
            bool hasRun     = run != null;
            bool hasDestroy = destroy != null;
            bool healthy    = (hasInit == hasRun) && (hasRun == hasDestroy);
            Debug.Assert(healthy);

            InitCallback = init;
            Run          = run;
            Destroy      = destroy;
            InitOpaque   = initOpaque;
        }

        /// <summary>Returns whether a valid callback is configured.</summary>
        /// <returns><c>true</c> if <see cref="Run"/> is non-null.</returns>
        public readonly bool IsPresent() => Run != null;

        /// <summary>
        /// Invokes <see cref="InitCallback"/> for <paramref name="numThreads"/> and <paramref name="numPixels"/>.
        /// </summary>
        /// <param name="numThreads">Number of worker threads.</param>
        /// <param name="numPixels">Maximum pixels per stripe.</param>
        /// <returns>Opaque run state returned by <see cref="InitCallback"/>.</returns>
        public readonly object? Init(nuint numThreads, nuint numPixels)
        {
            return InitCallback!(InitOpaque, numThreads, numPixels);
        }
    }

    /// <summary>
    /// Output destination configuration (pixel format, bit depth, callback, or linear buffer).
    /// Ported from <c>jxl::ImageOutput</c> in <c>lib/jxl/dec_cache.h</c>.
    /// </summary>
    public unsafe struct ImageOutput
    {
        /// <summary>Pixel format of the output pixels, used for buffer and callback output.</summary>
        public JxlPixelFormat Format;

        /// <summary>Output bit depth for unsigned data types, used for float-to-int conversion.</summary>
        public nuint BitsPerSample;

        /// <summary>Callback for line-by-line output.</summary>
        public PixelCallback Callback;

        /// <summary>Pixel buffer pointer for image output.</summary>
        public void* Buffer;

        /// <summary>Size of <see cref="Buffer"/> in bytes.</summary>
        public nuint BufferSize;

        /// <summary>Length of a row of <see cref="Buffer"/> in bytes (based on oriented width).</summary>
        public nuint Stride;
    }

    /// <summary>
    /// Per-frame decoder state. All images here are accessed through a group rect (in block or pixel units).
    /// Ported from <c>jxl::PassesDecoderState</c> in <c>lib/jxl/dec_cache.h</c> and <c>lib/jxl/dec_cache.cc</c>.
    /// </summary>
    public sealed unsafe class PassesDecoderState : IDisposable
    {
        /// <summary>Border around sigma image for EPF (1).</summary>
        public const nuint kSigmaBorder = 1;

        /// <summary>Padding around sigma image for EPF (2).</summary>
        public const nuint kSigmaPadding = 2;

        /// <summary>
        /// Options controlling render pipeline construction in <see cref="PreparePipeline"/>.
        /// Ported from <c>jxl::PassesDecoderState::PipelineOptions</c> in <c>lib/jxl/dec_cache.h</c>.
        /// </summary>
        public struct PipelineOptions
        {
            /// <summary>Whether to use the simple (slow) reference pipeline implementation.</summary>
            public bool UseSlowRenderPipeline;

            /// <summary>Whether frame coalescing (blending) is enabled.</summary>
            public bool Coalescing;

            /// <summary>Whether spot color channels should be rendered onto RGB.</summary>
            public bool RenderSpotcolors;

            /// <summary>Whether synthesized noise should be rendered.</summary>
            public bool RenderNoise;
        }

        /// <summary>Owned shared state storage.</summary>
        public PassesSharedState SharedStorage;

        /// <summary>Reference to active <see cref="PassesSharedState"/> (avoids copies for encoder loop).</summary>
        public PassesSharedState Shared;

        /// <summary>8x upsampling stage for DC.</summary>
        public RenderPipelineStage? Upsampler8x;

        /// <summary>ANS entropy codes per pass.</summary>
        public List<ANSCode> Code = new List<ANSCode>();

        /// <summary>ANS context maps per pass.</summary>
        public List<byte[]> ContextMap = new List<byte[]>();

        /// <summary>Multiplier to be applied to the quant matrices of the X channel.</summary>
        public float XDmMultiplier;

        /// <summary>Multiplier to be applied to the quant matrices of the B channel.</summary>
        public float BDmMultiplier;

        /// <summary>Precomputed <c>1 / sigma</c> values for EPF.</summary>
        public ImagePlane<float> Sigma = new ImagePlane<float>();

        /// <summary>Image width before applying <see cref="UndoOrientation"/>.</summary>
        public nuint Width;

        /// <summary>Image height before applying <see cref="UndoOrientation"/>.</summary>
        public nuint Height;

        /// <summary>Main color/alpha output target.</summary>
        public ImageOutput MainOutput;

        /// <summary>Per-extra-channel output targets.</summary>
        public List<ImageOutput> ExtraOutput = new List<ImageOutput>();

        /// <summary>Whether to use fast XYB-to-uint8-sRGB conversion.</summary>
        public bool FastXybSrgb8Conversion;

        /// <summary>If <c>true</c>, RGBA output will be unpremultiplied before writing to the output.</summary>
        public bool UnpremulAlpha;

        /// <summary>Orientation that the render pipeline will apply to bring the image to display orientation.</summary>
        public Orientation UndoOrientation = Orientation.Identity;

        /// <summary>Visible frame index used for seeding noise.</summary>
        public nuint VisibleFrameIndex;

        /// <summary>Non-visible frame index used for seeding noise.</summary>
        public nuint NonvisibleFrameIndex;

        /// <summary>Bitmask of AC strategy types used in the frame (updated atomically across groups).</summary>
        public int UsedAcs;

        /// <summary>Storage for quantized AC coefficients in accumulate mode.</summary>
        public ACImage Coefficients = new ACImageT<int>();

        /// <summary>Active rendering pipeline for the frame.</summary>
        public RenderPipeline? RenderPipeline;

        /// <summary>Storage for the current frame if it can be referenced by future frames.</summary>
        public ImageBundle FrameStorageForReferencing;

        /// <summary>Information for color conversions.</summary>
        public OutputEncodingInfo OutputEncodingInfo = new OutputEncodingInfo();

        /// <summary>
        /// Initializes a new instance of the <see cref="PassesDecoderState"/> class.
        /// </summary>
        /// <param name="memoryManager">Optional memory manager.</param>
        public PassesDecoderState(JxlMemoryManager? memoryManager = null)
        {
            JxlMemoryManager mm        = memoryManager ?? JxlMemoryManager.Default;
            SharedStorage              = new PassesSharedState(mm);
            Shared                     = SharedStorage;
            FrameStorageForReferencing = new ImageBundle(mm);
        }

        /// <summary>Gets the memory manager from <see cref="Shared"/>.</summary>
        public JxlMemoryManager MemoryManager => Shared.MemoryManager ?? JxlMemoryManager.Default;

        /// <summary>
        /// Atomically ORs <paramref name="mask"/> into <see cref="UsedAcs"/>.
        /// </summary>
        /// <param name="mask">Bitmask of AC strategies used by a group.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OrUsedAcs(uint mask)
        {
            Interlocked.Or(ref UsedAcs, (int)mask);
        }

        /// <summary>
        /// Initializes decoder-specific structures using information from <see cref="Shared"/>.
        /// Ported from <c>jxl::PassesDecoderState::Init</c> in <c>lib/jxl/dec_cache.h</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status Init(FrameHeader frameHeader)
        {
            JxlMemoryManager mm = MemoryManager;
            XDmMultiplier       = MathF.Pow(1.0f / 1.25f, (float)frameHeader.XQmScale - 2.0f);
            BDmMultiplier       = MathF.Pow(1.0f / 1.25f, (float)frameHeader.BQmScale - 2.0f);

            MainOutput.Callback = default;
            MainOutput.Buffer   = null;
            ExtraOutput.Clear();

            FastXybSrgb8Conversion = false;
            UnpremulAlpha          = false;
            UndoOrientation        = Orientation.Identity;

            Volatile.Write(ref UsedAcs, 0);

            Upsampler8x?.Dispose();
            Upsampler8x = RenderPipelineStages.GetUpsamplingStage(
                mm,
                Shared.Metadata!.TransformData,
                0,
                3);

            if (frameHeader.LoopFilter.EpfIters > 0)
            {
                StatusOr<ImagePlane<float>> sigmaOr = ImagePlane<float>.Create(
                    mm,
                    Shared.FrameDim.XSizeBlocks + 2 * kSigmaPadding,
                    Shared.FrameDim.YSizeBlocks + 2 * kSigmaPadding);

                if (!sigmaOr.IsOk)
                {
                    return sigmaOr.Status;
                }

                Sigma.Dispose();
                Sigma = sigmaOr.Value;
            }

            return true;
        }

        /// <summary>
        /// Initializes the decoder state after all of DC is decoded.
        /// Ported from <c>jxl::PassesDecoderState::InitForAC</c> in <c>lib/jxl/dec_cache.cc</c>.
        /// </summary>
        /// <param name="numPasses">Number of passes in the frame.</param>
        /// <param name="pool">Optional thread pool.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitForAC(nuint numPasses, ThreadPool? pool = null)
        {
            _ = pool;
            SharedStorage.CoeffOrderSize = 0;
            uint usedAcs                 = (uint)Volatile.Read(ref UsedAcs);

            for (byte o = 0; o < AcStrategy.kNumValidStrategies; ++o)
            {
                if (((1u << o) & usedAcs) == 0)
                {
                    continue;
                }

                byte ord       = CoeffOrder.StrategyOrder[o];
                nuint ordLimit = CoeffOrder.CoeffOrderOffsets[3 * (ord + 1)] * FrameDimensions.kDCTBlockSize;

                if (ordLimit > SharedStorage.CoeffOrderSize)
                {
                    SharedStorage.CoeffOrderSize = ordLimit;
                }
            }

            nuint sz = numPasses * SharedStorage.CoeffOrderSize;

            if (sz > (nuint)SharedStorage.CoeffOrders.Length)
            {
                Array.Resize(ref SharedStorage.CoeffOrders, (int)sz);
            }

            return true;
        }

        /// <summary>
        /// Builds and finalizes the <see cref="RenderPipeline"/> for <paramref name="frameHeader"/>.
        /// Ported from <c>jxl::PassesDecoderState::PreparePipeline</c> in <c>lib/jxl/dec_cache.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="metadata">Codestream image metadata.</param>
        /// <param name="decoded">Destination <see cref="ImageBundle"/> when not writing directly to user buffer/callback.</param>
        /// <param name="options">Pipeline configuration options.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status PreparePipeline(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            ImageBundle? decoded,
            PipelineOptions options)
        {
            JxlMemoryManager mm = MemoryManager;
            nuint numC          = 3 + frameHeader.NonserializedMetadata!.M.NumExtraChannels;
            bool renderNoise    = options.RenderNoise && (frameHeader.Flags & FrameHeader.KNoise) != 0;
            nuint numTmpC       = renderNoise ? 3u : 0u;

            if (frameHeader.CanBeReferenced())
            {
                FrameStorageForReferencing.Dispose();
                FrameStorageForReferencing = new ImageBundle(mm, metadata);
            }

            RenderPipeline.Builder builder = new RenderPipeline.Builder(mm, numC + numTmpC);

            if (options.UseSlowRenderPipeline)
            {
                builder.UseSimpleImplementation();
            }

            Status preStatus = AddPreColorTransformStages(frameHeader, metadata, numC, renderNoise, builder);

            if (!preStatus)
            {
                return preStatus;
            }

            bool hasAlpha = FindAlphaChannel(metadata, out nuint alphaC);
            Status postStatus = FastXybSrgb8Conversion
                ? AddFastXybPipelineStage(frameHeader, metadata, options, (hasAlpha, alphaC), builder)
                : AddStandardColorPipelineStages(frameHeader, metadata, decoded, options, (hasAlpha, alphaC), builder);

            if (!postStatus)
            {
                return postStatus;
            }

            StatusOr<RenderPipeline> pipeOr = builder.Finalize(Shared.FrameDim);

            if (!pipeOr.IsOk)
            {
                return pipeOr.Status;
            }

            RenderPipeline?.Dispose();
            RenderPipeline = pipeOr.Value;

            return RenderPipeline.IsInitialized();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            RenderPipeline?.Dispose();
            Upsampler8x?.Dispose();
            Sigma.Dispose();
            Coefficients.Dispose();
            FrameStorageForReferencing.Dispose();
            SharedStorage.Dispose();
        }

        private Status AddPreColorTransformStages(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            nuint numC,
            bool renderNoise,
            RenderPipeline.Builder builder)
        {
            if (!AddChromaUpsamplingStages(frameHeader, builder))
            {
                return false;
            }

            if (!AddLoopFilterStages(frameHeader.LoopFilter, builder))
            {
                return false;
            }

            if (!AddEarlyExtraChannelUpsamplingStages(frameHeader, out bool lateEcUpsample, builder))
            {
                return false;
            }

            if (!AddFeatureAndUpsamplingStages(frameHeader, lateEcUpsample, numC, renderNoise, builder))
            {
                return false;
            }

            if (frameHeader.DcLevel != 0)
            {
                if (!builder.AddStage(RenderPipelineStages.GetWriteToImage3FStage(
                    MemoryManager,
                    SharedStorage,
                    (int)(frameHeader.DcLevel - 1))))
                {
                    return false;
                }
            }

            if (frameHeader.CanBeReferenced() && frameHeader.SaveBeforeColorTransform)
            {
                if (!builder.AddStage(RenderPipelineStages.GetWriteToImageBundleStage(
                    FrameStorageForReferencing,
                    metadata.ColorEncoding)))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status AddChromaUpsamplingStages(FrameHeader frameHeader, RenderPipeline.Builder builder)
        {
            if (frameHeader.ChromaSubsampling.Is444())
            {
                return true;
            }

            for (nuint c = 0; c < 3; c++)
            {
                if (frameHeader.ChromaSubsampling.HShift(c) != 0)
                {
                    if (!builder.AddStage(RenderPipelineStages.GetChromaUpsamplingStage(c, horizontal: true)))
                    {
                        return false;
                    }
                }

                if (frameHeader.ChromaSubsampling.VShift(c) != 0)
                {
                    if (!builder.AddStage(RenderPipelineStages.GetChromaUpsamplingStage(c, horizontal: false)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Status AddLoopFilterStages(LoopFilter lf, RenderPipeline.Builder builder)
        {
            if (lf.Gab)
            {
                RenderPipelineStage? gabStage = RenderPipelineStages.GetGaborishStage(lf);

                if (gabStage == null || !builder.AddStage(gabStage))
                {
                    return false;
                }
            }

            if (lf.EpfIters >= 3)
            {
                RenderPipelineStage? epf0 = RenderPipelineStages.GetEPFStage(lf, Sigma, EpfStage.Zero);

                if (epf0 == null || !builder.AddStage(epf0))
                {
                    return false;
                }
            }

            if (lf.EpfIters >= 1)
            {
                RenderPipelineStage? epf1 = RenderPipelineStages.GetEPFStage(lf, Sigma, EpfStage.One);

                if (epf1 == null || !builder.AddStage(epf1))
                {
                    return false;
                }
            }

            if (lf.EpfIters >= 2)
            {
                RenderPipelineStage? epf2 = RenderPipelineStages.GetEPFStage(lf, Sigma, EpfStage.Two);

                if (epf2 == null || !builder.AddStage(epf2))
                {
                    return false;
                }
            }

            return true;
        }

        private Status AddEarlyExtraChannelUpsamplingStages(
            FrameHeader frameHeader,
            out bool lateEcUpsample,
            RenderPipeline.Builder builder)
        {
            lateEcUpsample = frameHeader.Upsampling != 1;

            foreach (uint ecups in frameHeader.ExtraChannelUpsampling)
            {
                if (ecups != frameHeader.Upsampling)
                {
                    // If patches are applied, either frame_header.upsampling == 1 or
                    // late_ec_upsample is true.
                    lateEcUpsample = false;
                }
            }

            if (lateEcUpsample)
            {
                return true;
            }

            JxlMemoryManager mm = MemoryManager;

            for (int ec = 0; ec < frameHeader.ExtraChannelUpsampling.Count; ec++)
            {
                uint factor = frameHeader.ExtraChannelUpsampling[ec];

                if (factor != 1)
                {
                    nuint shift = (nuint)Bits.CeilLog2Nonzero(factor);
                    RenderPipelineStage? stage = RenderPipelineStages.GetUpsamplingStage(
                        mm,
                        frameHeader.NonserializedMetadata!.TransformData,
                        (nuint)(3 + ec),
                        shift);

                    if (stage == null || !builder.AddStage(stage))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Status AddFeatureAndUpsamplingStages(
            FrameHeader frameHeader,
            bool lateEcUpsample,
            nuint numC,
            bool renderNoise,
            RenderPipeline.Builder builder)
        {
            if ((frameHeader.Flags & FrameHeader.KPatches) != 0)
            {
                if (!builder.AddStage(RenderPipelineStages.GetPatchesStage(
                    Shared.ImageFeatures.Patches,
                    frameHeader.NonserializedMetadata!.M.ExtraChannelInfo)))
                {
                    return false;
                }
            }

            if ((frameHeader.Flags & FrameHeader.KSplines) != 0)
            {
                if (!builder.AddStage(RenderPipelineStages.GetSplineStage(Shared.ImageFeatures.Splines)))
                {
                    return false;
                }
            }

            if (frameHeader.Upsampling != 1)
            {
                nuint nbChannels = 3 + (lateEcUpsample ? (nuint)frameHeader.ExtraChannelUpsampling.Count : 0u);
                nuint shift      = (nuint)Bits.CeilLog2Nonzero(frameHeader.Upsampling);

                for (nuint c = 0; c < nbChannels; c++)
                {
                    RenderPipelineStage? upsStage = RenderPipelineStages.GetUpsamplingStage(
                        MemoryManager,
                        frameHeader.NonserializedMetadata!.TransformData,
                        c,
                        shift);

                    if (upsStage == null || !builder.AddStage(upsStage))
                    {
                        return false;
                    }
                }
            }

            if (renderNoise)
            {
                if (!builder.AddStage(RenderPipelineStages.GetConvolveNoiseStage(numC)))
                {
                    return false;
                }

                if (!builder.AddStage(RenderPipelineStages.GetAddNoiseStage(
                    Shared.ImageFeatures.NoiseParams,
                    Shared.Cmap.Base,
                    numC)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FindAlphaChannel(ImageMetadata metadata, out nuint alphaC)
        {
            for (int i = 0; i < metadata.ExtraChannelInfo.Count; i++)
            {
                if (metadata.ExtraChannelInfo[i].Type == ExtraChannel.Alpha)
                {
                    alphaC = (nuint)(3 + i);

                    return true;
                }
            }

            alphaC = 0;

            return false;
        }

        private Status AddFastXybPipelineStage(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            PipelineOptions options,
            (bool HasAlpha, nuint AlphaC) alphaInfo,
            RenderPipeline.Builder builder)
        {
            if (Blending.NeedsBlending(frameHeader))
            {
                return Status.Failure("Fast XYB sRGB8 stage cannot be used with blending");
            }

            if (frameHeader.CanBeReferenced() && !frameHeader.SaveBeforeColorTransform)
            {
                return Status.Failure("Fast XYB sRGB8 stage cannot be referenced after color transform");
            }

            if (options.RenderSpotcolors && metadata.Find(ExtraChannel.SpotColor) != null)
            {
                return Status.Failure("Fast XYB sRGB8 stage cannot render spot colors");
            }

            bool isRgba      = MainOutput.Format.NumChannels == 4;
            byte* rgbOutput  = (byte*)MainOutput.Buffer;
            RenderPipelineStage? stage = RenderPipelineStages.GetFastXYBTosRGB8Stage(
                rgbOutput,
                MainOutput.Stride,
                Width,
                Height,
                isRgba,
                alphaInfo.HasAlpha,
                alphaInfo.AlphaC);

            if (stage == null)
            {
                return Status.Failure("Fast XYB sRGB8 stage unavailable");
            }

            return builder.AddStage(stage);
        }

        private Status AddStandardColorPipelineStages(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            ImageBundle? decoded,
            PipelineOptions options,
            (bool HasAlpha, nuint AlphaC) alphaInfo,
            RenderPipeline.Builder builder)
        {
            if (!AddColorTransformAndCoalescingStages(frameHeader, metadata, options, builder, out bool linear))
            {
                return false;
            }

            if (!AddSpotColorStages(frameHeader, metadata, options, builder))
            {
                return false;
            }

            if (!AddToneMappingStages(builder, ref linear))
            {
                return false;
            }

            if (!AddFinalColorConversionStages(builder, ref linear))
            {
                return false;
            }

            return AddOutputWriteStage(decoded, alphaInfo, builder);
        }

        private Status AddColorTransformAndCoalescingStages(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            PipelineOptions options,
            RenderPipeline.Builder builder,
            out bool linear)
        {
            linear = false;

            if (frameHeader.ColorTransform == ColorTransform.YCbCr)
            {
                if (!builder.AddStage(RenderPipelineStages.GetYCbCrStage()))
                {
                    return false;
                }
            }
            else if (frameHeader.ColorTransform == ColorTransform.XYB)
            {
                if (!builder.AddStage(RenderPipelineStages.GetXYBStage(OutputEncodingInfo)))
                {
                    return false;
                }

                if (OutputEncodingInfo.ColorEncoding.GetColorSpace() != ColorSpace.XYB)
                {
                    linear = true;
                }
            }

            if (options.Coalescing && Blending.NeedsBlending(frameHeader))
            {
                if (linear)
                {
                    RenderPipelineStage? fromLinear = RenderPipelineStages.GetFromLinearStage(OutputEncodingInfo);

                    if (fromLinear == null || !builder.AddStage(fromLinear))
                    {
                        return false;
                    }

                    linear = false;
                }

                if (!builder.AddStage(RenderPipelineStages.GetBlendingStage(
                    frameHeader,
                    this,
                    OutputEncodingInfo.ColorEncoding)))
                {
                    return false;
                }
            }

            if (options.Coalescing && frameHeader.CanBeReferenced() && !frameHeader.SaveBeforeColorTransform)
            {
                if (linear)
                {
                    RenderPipelineStage? fromLinear = RenderPipelineStages.GetFromLinearStage(OutputEncodingInfo);

                    if (fromLinear == null || !builder.AddStage(fromLinear))
                    {
                        return false;
                    }

                    linear = false;
                }

                if (!builder.AddStage(RenderPipelineStages.GetWriteToImageBundleStage(
                    FrameStorageForReferencing,
                    metadata.ColorEncoding)))
                {
                    return false;
                }
            }

            return true;
        }

        private static Status AddSpotColorStages(
            FrameHeader frameHeader,
            ImageMetadata metadata,
            PipelineOptions options,
            RenderPipeline.Builder builder)
        {
            if (!options.RenderSpotcolors || frameHeader.NonserializedMetadata!.M.Find(ExtraChannel.SpotColor) == null)
            {
                return true;
            }

            for (int i = 0; i < metadata.ExtraChannelInfo.Count; i++)
            {
                ExtraChannelInfo eci = metadata.ExtraChannelInfo[i];

                if (eci.Type == ExtraChannel.SpotColor)
                {
                    if (!builder.AddStage(RenderPipelineStages.GetSpotColorStage((nuint)i, eci.SpotColor)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Status AddToneMappingStages(RenderPipeline.Builder builder, ref bool linear)
        {
            RenderPipelineStage? toneMappingStage = RenderPipelineStages.GetToneMappingStage(OutputEncodingInfo);

            if (toneMappingStage == null)
            {
                return true;
            }

            if (!linear)
            {
                RenderPipelineStage? toLinearStage = RenderPipelineStages.GetToLinearStage(OutputEncodingInfo);

                if (toLinearStage == null)
                {
                    if (!OutputEncodingInfo.CmsSet)
                    {
                        toneMappingStage.Dispose();

                        return Status.Failure("Cannot tonemap this colorspace without a CMS");
                    }

                    RenderPipelineStage? cmsStage = RenderPipelineStages.GetCmsStage(OutputEncodingInfo);

                    if (cmsStage != null && !builder.AddStage(cmsStage))
                    {
                        toneMappingStage.Dispose();

                        return false;
                    }
                }
                else if (!builder.AddStage(toLinearStage))
                {
                    toneMappingStage.Dispose();

                    return false;
                }

                linear = true;
            }

            return builder.AddStage(toneMappingStage);
        }

        private Status AddFinalColorConversionStages(RenderPipeline.Builder builder, ref bool linear)
        {
            if (linear)
            {
                nuint channelsSrc = OutputEncodingInfo.OrigColorEncoding.IsCMYK()
                    ? 4u
                    : OutputEncodingInfo.OrigColorEncoding.Channels();
                nuint channelsDst = OutputEncodingInfo.ColorEncoding.Channels();
                bool mixingColorAndGrey = channelsDst != channelsSrc;

                if (OutputEncodingInfo.ColorEncodingIsOriginal ||
                    !OutputEncodingInfo.CmsSet ||
                    mixingColorAndGrey)
                {
                    RenderPipelineStage? fromLinear = RenderPipelineStages.GetFromLinearStage(OutputEncodingInfo);

                    if (fromLinear == null || !builder.AddStage(fromLinear))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!OutputEncodingInfo.LinearColorEncoding.CreateICC())
                    {
                        return Status.Failure("Failed to create ICC");
                    }

                    RenderPipelineStage? cmsStage = RenderPipelineStages.GetCmsStage(OutputEncodingInfo);

                    if (cmsStage != null && !builder.AddStage(cmsStage))
                    {
                        return false;
                    }
                }

                linear = false;
            }
            else
            {
                RenderPipelineStage? cmsStage = RenderPipelineStages.GetCmsStage(OutputEncodingInfo, linear: false);

                if (cmsStage != null && !builder.AddStage(cmsStage))
                {
                    return false;
                }
            }

            return true;
        }

        private Status AddOutputWriteStage(
            ImageBundle? decoded,
            (bool HasAlpha, nuint AlphaC) alphaInfo,
            RenderPipeline.Builder builder)
        {
            if (MainOutput.Callback.IsPresent() || MainOutput.Buffer != null)
            {
                return builder.AddStage(RenderPipelineStages.GetWriteToOutputStage(
                    MainOutput,
                    (Width, Height),
                    (alphaInfo.HasAlpha, UnpremulAlpha, alphaInfo.AlphaC),
                    UndoOrientation,
                    ExtraOutput,
                    MemoryManager));
            }

            if (decoded == null)
            {
                return Status.Failure("No output buffer, callback, or ImageBundle specified");
            }

            return builder.AddStage(RenderPipelineStages.GetWriteToImageBundleStage(
                decoded,
                OutputEncodingInfo.ColorEncoding));
        }
    }

    /// <summary>
    /// Temporary images and aligned scratch buffers required for decoding a single group on one worker thread.
    /// Ported from <c>jxl::GroupDecCache</c> in <c>lib/jxl/dec_cache.h</c> and <c>lib/jxl/dec_cache.cc</c>.
    /// </summary>
    public sealed unsafe class GroupDecCache : IDisposable
    {
        private AlignedMemory _floatMemory = new AlignedMemory();
        private AlignedMemory _int32Memory = new AlignedMemory();
        private AlignedMemory _int16Memory = new AlignedMemory();
        private nuint _maxBlockArea;

        // Scratch space used by DecGroupImpl().

        /// <summary>Dequantized float block scratch buffer (3 blocks).</summary>
        public float* DecGroupBlock;

        /// <summary>Quantized 32-bit integer block scratch buffer (3 blocks).</summary>
        public int* DecGroupQBlock;

        /// <summary>Quantized 16-bit integer block scratch buffer (3 blocks).</summary>
        public short* DecGroupQBlock16;

        // For TransformToPixels.

        /// <summary>Transform scratch space (4 blocks).</summary>
        public float* ScratchSpace;

        // AC decoding

        /// <summary>Per-pass non-zero coefficient counts per 8x8 block.</summary>
        public Image3<int>[] NumNzeroes = new Image3<int>[FrameDimensions.KMaxNumPasses];

        // Buffer for DC upsampling.

        /// <summary>Scratch plane for 8x DC upsampling.</summary>
        public ImagePlane<float> DcBuffer = new ImagePlane<float>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupDecCache"/> class.
        /// </summary>
        public GroupDecCache()
        {
            for (int i = 0; i < FrameDimensions.KMaxNumPasses; i++)
            {
                NumNzeroes[i] = new Image3<int>();
            }
        }

        /// <summary>
        /// Allocates per-pass non-zero maps and transform scratch buffers if not already large enough.
        /// Ported from <c>jxl::GroupDecCache::InitOnce</c> in <c>lib/jxl/dec_cache.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation.</param>
        /// <param name="numPasses">Number of passes to allocate <see cref="NumNzeroes"/> for.</param>
        /// <param name="usedAcs">Bitmask of AC strategies used in the frame.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitOnce(JxlMemoryManager? memoryManager, nuint numPasses, nuint usedAcs)
        {
            JxlMemoryManager mm = memoryManager ?? JxlMemoryManager.Default;

            for (nuint i = 0; i < numPasses; i++)
            {
                if (NumNzeroes[i].XSize == 0)
                {
                    StatusOr<Image3<int>> nzOr = Image3<int>.Create(
                        mm,
                        FrameDimensions.KGroupDimInBlocks,
                        FrameDimensions.KGroupDimInBlocks);

                    if (!nzOr.IsOk)
                    {
                        return nzOr.Status;
                    }

                    NumNzeroes[i] = nzOr.Value;
                }
            }

            nuint maxBlockArea = ComputeMaxBlockArea(usedAcs);

            if (maxBlockArea > _maxBlockArea)
            {
                Status allocStatus = ReallocateScratchBuffers(mm, maxBlockArea);

                if (!allocStatus)
                {
                    return allocStatus;
                }
            }

            DecGroupBlock    = (float*)_floatMemory.Address;
            ScratchSpace     = DecGroupBlock != null ? DecGroupBlock + _maxBlockArea * 3 : null;
            DecGroupQBlock   = (int*)_int32Memory.Address;
            DecGroupQBlock16 = (short*)_int16Memory.Address;

            return true;
        }

        /// <summary>
        /// Allocates <see cref="DcBuffer"/> if not yet allocated.
        /// Ported from <c>jxl::GroupDecCache::InitDCBufferOnce</c> in <c>lib/jxl/dec_cache.h</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used for allocation.</param>
        /// <returns><see cref="Status.OkStatus"/> on success.</returns>
        public Status InitDCBufferOnce(JxlMemoryManager? memoryManager)
        {
            if (DcBuffer.XSize == 0)
            {
                StatusOr<ImagePlane<float>> dcOr = ImagePlane<float>.Create(
                    memoryManager ?? JxlMemoryManager.Default,
                    FrameDimensions.KGroupDimInBlocks + RenderPipelineStage.KRenderPipelineXOffset * 2,
                    FrameDimensions.KGroupDimInBlocks + 4);

                if (!dcOr.IsOk)
                {
                    return dcOr.Status;
                }

                DcBuffer = dcOr.Value;
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < NumNzeroes.Length; i++)
            {
                NumNzeroes[i].Dispose();
            }

            DcBuffer.Dispose();
            _floatMemory.Dispose();
            _int32Memory.Dispose();
            _int16Memory.Dispose();
        }

        private static nuint ComputeMaxBlockArea(nuint usedAcs)
        {
            nuint maxBlockArea = 0;

            for (byte o = 0; o < AcStrategy.kNumValidStrategies; ++o)
            {
                if ((usedAcs & (1u << o)) == 0)
                {
                    continue;
                }

                AcStrategy acs = AcStrategy.FromRawStrategy(o);
                nuint area     = acs.CoveredBlocksX() * acs.CoveredBlocksY() * FrameDimensions.kDCTBlockSize;

                if (area > maxBlockArea)
                {
                    maxBlockArea = area;
                }
            }

            return maxBlockArea;
        }

        private Status ReallocateScratchBuffers(JxlMemoryManager mm, nuint maxBlockArea)
        {
            _maxBlockArea = maxBlockArea;

            // We need 3x float blocks for dequantized coefficients and 4x for scratch
            // space for transforms.
            StatusOr<AlignedMemory> floatOr = AlignedMemory.Create(mm, _maxBlockArea * 7 * sizeof(float));

            if (!floatOr.IsOk)
            {
                return floatOr.Status;
            }

            _floatMemory.Dispose();
            _floatMemory = floatOr.Value;

            // We need 3x int32 or int16 blocks for quantized coefficients.
            StatusOr<AlignedMemory> int32Or = AlignedMemory.Create(mm, _maxBlockArea * 3 * sizeof(int));

            if (!int32Or.IsOk)
            {
                return int32Or.Status;
            }

            _int32Memory.Dispose();
            _int32Memory = int32Or.Value;

            StatusOr<AlignedMemory> int16Or = AlignedMemory.Create(mm, _maxBlockArea * 3 * sizeof(short));

            if (!int16Or.IsOk)
            {
                return int16Or.Status;
            }

            _int16Memory.Dispose();
            _int16Memory = int16Or.Value;

            return true;
        }
    }

    public static unsafe partial class RenderPipelineStages
    {
        /// <summary>
        /// Creates a stage that blends the current frame onto the reference frames in <paramref name="decState"/>.
        /// Ported from <c>jxl::GetBlendingStage</c> in <c>lib/jxl/render_pipeline/stage_blending.cc</c>.
        /// </summary>
        /// <param name="frameHeader">Current frame header.</param>
        /// <param name="decState">Current frame decoder state.</param>
        /// <param name="frameColorEncoding">Color encoding of the frame.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> instance.</returns>
        public static RenderPipelineStage GetBlendingStage(
            FrameHeader frameHeader,
            PassesDecoderState decState,
            ColorEncoding frameColorEncoding)
        {
            return new BlendingStage(frameHeader, decState, frameColorEncoding);
        }

        /// <summary>
        /// Creates a terminal stage that writes all color and extra channels into <paramref name="imageBundle"/>.
        /// Ported from <c>jxl::GetWriteToImageBundleStage</c> in <c>lib/jxl/render_pipeline/stage_write.cc</c>.
        /// </summary>
        /// <param name="imageBundle">Destination <see cref="ImageBundle"/>.</param>
        /// <param name="colorEncoding">Color encoding to assign to <paramref name="imageBundle"/>.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> instance.</returns>
        public static RenderPipelineStage GetWriteToImageBundleStage(
            ImageBundle imageBundle,
            ColorEncoding colorEncoding)
        {
            return new WriteToImageBundleStage(imageBundle, colorEncoding);
        }

        /// <summary>
        /// Creates a stage that writes the 3 color channels into <c>shared.DcFrames[dcFrameIndex]</c>.
        /// Ported from <c>jxl::GetWriteToImage3FStage</c> in <c>lib/jxl/render_pipeline/stage_write.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate the <see cref="Image3{T}"/>.</param>
        /// <param name="shared">Shared passes state holding <see cref="PassesSharedState.DcFrames"/>.</param>
        /// <param name="dcFrameIndex">Index in <see cref="PassesSharedState.DcFrames"/> (0..3).</param>
        /// <returns>New <see cref="RenderPipelineStage"/> instance.</returns>
        public static RenderPipelineStage GetWriteToImage3FStage(
            JxlMemoryManager? memoryManager,
            PassesSharedState shared,
            int dcFrameIndex)
        {
            return new WriteToImage3FStage(memoryManager, shared, dcFrameIndex);
        }

        /// <summary>
        /// Creates a stage that writes the 3 color channels into <paramref name="image"/>.
        /// Ported from <c>jxl::GetWriteToImage3FStage</c> in <c>lib/jxl/render_pipeline/stage_write.cc</c>.
        /// </summary>
        /// <param name="memoryManager">Memory manager used to allocate the <see cref="Image3{T}"/>.</param>
        /// <param name="image">Destination 3-plane float image.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> instance.</returns>
        public static RenderPipelineStage GetWriteToImage3FStage(
            JxlMemoryManager? memoryManager,
            Image3<float> image)
        {
            return new WriteToImage3FStage(memoryManager, image);
        }

        /// <summary>
        /// Creates a terminal stage that formats and writes pixels to <paramref name="mainOutput"/> and <paramref name="extraOutput"/>.
        /// Ported from <c>jxl::GetWriteToOutputStage</c> in <c>lib/jxl/render_pipeline/stage_write.cc</c>.
        /// </summary>
        /// <param name="mainOutput">Main output buffer or callback specification.</param>
        /// <param name="dimensions">Unoriented <c>(Width, Height)</c> of the image.</param>
        /// <param name="alphaInfo">Tuple of <c>(HasAlpha, UnpremulAlpha, AlphaC)</c>.</param>
        /// <param name="undoOrientation">Display orientation transform to apply.</param>
        /// <param name="extraOutput">Extra channel output specifications.</param>
        /// <param name="memoryManager">Memory manager used for per-thread conversion buffers.</param>
        /// <returns>New <see cref="RenderPipelineStage"/> instance.</returns>
        public static RenderPipelineStage GetWriteToOutputStage(
            ImageOutput mainOutput,
            (nuint Width, nuint Height) dimensions,
            (bool HasAlpha, bool UnpremulAlpha, nuint AlphaC) alphaInfo,
            Orientation undoOrientation,
            IReadOnlyList<ImageOutput> extraOutput,
            JxlMemoryManager? memoryManager)
        {
            return new WriteToOutputStage(
                mainOutput,
                dimensions,
                alphaInfo,
                undoOrientation,
                extraOutput,
                memoryManager);
        }

        // --------------------------------------------------------------------
        // 15. Blending Stage (stage_blending.cc)
        // --------------------------------------------------------------------

        private sealed class BlendingStage : RenderPipelineStage
        {
            private readonly FrameHeader _frameHeader;
            private readonly PassesSharedState _state;
            private readonly BlendingInfo _info;
            private readonly ImageBundle _bg;
            private readonly nuint _imageXSize;
            private readonly nuint _imageYSize;
            private readonly PatchBlending[] _blendingInfo;
            private readonly IReadOnlyList<ExtraChannelInfo> _extraChannelInfo;
            private float[] _zeroes = Array.Empty<float>();
            private Status _initialized = true;

            public BlendingStage(
                FrameHeader frameHeader,
                PassesDecoderState decState,
                ColorEncoding frameColorEncoding)
                : base(new Settings())
            {
                _ = frameColorEncoding;
                _frameHeader      = frameHeader;
                _state            = decState.Shared;
                _imageXSize       = _frameHeader.NonserializedMetadata!.XSize();
                _imageYSize       = _frameHeader.NonserializedMetadata.YSize();
                _extraChannelInfo = _frameHeader.NonserializedMetadata.M.ExtraChannelInfo;
                _info             = _frameHeader.BlendingInfo;
                _bg               = _state.ReferenceFrames[_info.Source].Frame!;
                _blendingInfo     = new PatchBlending[_frameHeader.ExtraChannelBlendingInfo.Count + 1];

                if (!InitializeZeroesAndCheckXyb(_frameHeader.ExtraChannelBlendingInfo))
                {
                    return;
                }

                Status bgOk = VerifyAllBackgroundSizes(_frameHeader.ExtraChannelBlendingInfo);

                if (!bgOk)
                {
                    _initialized = bgOk;

                    return;
                }

                if (_state.Metadata!.M.XybEncoded && !decState.OutputEncodingInfo.ColorEncodingIsOriginal)
                {
                    _initialized = Status.Failure("Blending in unsupported color space");

                    return;
                }

                PopulateBlendingInfo(_frameHeader.ExtraChannelBlendingInfo);
            }

            public override Status IsInitialized() => _initialized;

            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                _ = outputRows;
                _ = threadId;

                if (!_initialized)
                {
                    return false;
                }

                if (xextra.Left != 0 || xextra.Right != 0)
                {
                    return Status.Failure("Expected zero xextra in BlendingStage");
                }

                if (!TryComputeBlendSpan(xpos, ypos, ref xsize, out nint bgXpos, out nint bgYpos, out int offset))
                {
                    return true;
                }

                nuint numC = Math.Min((nuint)inputRows.Length, (nuint)_extraChannelInfo.Count + 3);
                float** bgRowPtrs = stackalloc float*[(int)numC];
                float** fgRowPtrs = stackalloc float*[(int)numC];

                fixed (float* pZeroes = _zeroes)
                {
                    FillRowPtrArrays(inputRows, numC, offset, (nuint)bgXpos, (nuint)bgYpos, pZeroes, bgRowPtrs, fgRowPtrs);

                    Blending.BlendingRowPointers ptrs = new Blending.BlendingRowPointers(
                        bgRowPtrs,
                        fgRowPtrs,
                        fgRowPtrs,
                        0,
                        xsize);

                    return Blending.PerformBlending(
                        _state.MemoryManager,
                        in ptrs,
                        _blendingInfo[0],
                        _blendingInfo.AsSpan(1),
                        _extraChannelInfo);
                }
            }

            public override RenderPipelineChannelMode GetChannelMode(nuint c)
            {
                _ = c;

                return RenderPipelineChannelMode.InPlace;
            }

            public override bool SwitchToImageDimensions() => true;

            public override void GetImageDimensions(out nuint xsize, out nuint ysize, out FrameOrigin frameOrigin)
            {
                xsize       = _imageXSize;
                ysize       = _imageYSize;
                frameOrigin = _frameHeader.FrameOrigin;
            }

            public override void ProcessPaddingRow(
                float*[][] outputRows,
                nuint xsize,
                nuint xpos,
                nuint ypos)
            {
                FillColorPadding(outputRows, xsize, xpos, ypos);
                FillExtraChannelPadding(outputRows, xsize, xpos, ypos);
            }

            public override string GetName() => "Blending";

            private bool InitializeZeroesAndCheckXyb(List<BlendingInfo> ecInfo)
            {
                if (_bg.XSize() == 0 || _bg.YSize() == 0)
                {
                    _zeroes = new float[_imageXSize];

                    return true;
                }

                if (_state.ReferenceFrames[_info.Source].IbIsInXyb)
                {
                    _initialized = Status.Failure($"Trying to blend XYB reference frame {_info.Source} and non-XYB frame");

                    return false;
                }

                for (int i = 0; i < ecInfo.Count; i++)
                {
                    ImageBundle ecBg = _state.ReferenceFrames[ecInfo[i].Source].Frame!;

                    if (ecBg.XSize() == 0 || ecBg.YSize() == 0)
                    {
                        _zeroes = new float[_imageXSize];

                        break;
                    }
                }

                return true;
            }

            private Status VerifyAllBackgroundSizes(List<BlendingInfo> ecInfo)
            {
                Status ok = VerifyBgSize(_bg);

                for (int i = 0; i < ecInfo.Count; i++)
                {
                    if (ok)
                    {
                        ImageBundle ecBg = _state.ReferenceFrames[ecInfo[i].Source].Frame!;
                        ok               = VerifyBgSize(ecBg);
                    }
                }

                return ok;
            }

            private Status VerifyBgSize(ImageBundle bg)
            {
                if (bg.XSize() != 0 && bg.YSize() != 0 &&
                    (bg.XSize() < _imageXSize || bg.YSize() < _imageYSize ||
                     bg.Origin.X0 != 0 || bg.Origin.Y0 != 0))
                {
                    return Status.Failure($"Trying to use a {bg.XSize()}x{bg.YSize()} crop as a background");
                }

                return true;
            }

            private void PopulateBlendingInfo(List<BlendingInfo> ecInfo)
            {
                _blendingInfo[0] = MakePatchBlending(_info);

                for (int i = 0; i < ecInfo.Count; i++)
                {
                    _blendingInfo[1 + i] = MakePatchBlending(ecInfo[i]);
                }
            }

            private static PatchBlending MakePatchBlending(BlendingInfo info)
            {
                PatchBlendMode mode = info.Mode switch
                {
                    BlendMode.Replace          => PatchBlendMode.kReplace,
                    BlendMode.Add              => PatchBlendMode.kAdd,
                    BlendMode.Mul              => PatchBlendMode.kMul,
                    BlendMode.Blend            => PatchBlendMode.kBlendAbove,
                    BlendMode.AlphaWeightedAdd => PatchBlendMode.kAlphaWeightedAddAbove,
                    _                          => PatchBlendMode.kReplace,
                };

                return new PatchBlending
                {
                    Mode         = mode,
                    AlphaChannel = info.AlphaChannel,
                    Clamp        = info.Clamp,
                };
            }

            private bool TryComputeBlendSpan(
                nuint xpos,
                nuint ypos,
                ref nuint xsize,
                out nint bgXpos,
                out nint bgYpos,
                out int offset)
            {
                FrameOrigin frameOrigin = _frameHeader.FrameOrigin;
                bgXpos                  = (nint)frameOrigin.X0 + (nint)xpos;
                bgYpos                  = (nint)frameOrigin.Y0 + (nint)ypos;
                offset                  = 0;

                if (bgXpos + (nint)xsize <= 0 ||
                    frameOrigin.X0 >= (nint)_imageXSize ||
                    bgYpos < 0 ||
                    bgYpos >= (nint)_imageYSize)
                {
                    return false;
                }

                if (bgXpos < 0)
                {
                    offset -= (int)bgXpos;
                    xsize   = (nuint)((nint)xsize + bgXpos);
                    bgXpos  = 0;
                }

                if ((nuint)bgXpos + xsize > _imageXSize)
                {
                    nint remaining = (nint)_imageXSize - bgXpos;
                    xsize          = (nuint)Math.Max((nint)0, remaining);
                }

                return true;
            }

            private void FillRowPtrArrays(
                float*[][] inputRows,
                nuint numC,
                int offset,
                nuint bgXpos,
                nuint bgYpos,
                float* pZeroes,
                float** bgRowPtrs,
                float** fgRowPtrs)
            {
                bool hasColorBg = _bg.XSize() != 0 && _bg.YSize() != 0;

                for (nuint c = 0; c < numC; ++c)
                {
                    fgRowPtrs[c] = GetInputRow(inputRows, c, 0) + offset;

                    if (c < 3)
                    {
                        bgRowPtrs[c] = hasColorBg
                            ? _bg.Color().ConstPlaneRow(c, bgYpos) + bgXpos
                            : pZeroes;
                    }
                    else
                    {
                        uint ecSource    = _frameHeader.ExtraChannelBlendingInfo[(int)(c - 3)].Source;
                        ImageBundle ecBg = _state.ReferenceFrames[ecSource].Frame!;
                        bgRowPtrs[c]     = (ecBg.XSize() != 0 && ecBg.YSize() != 0)
                            ? ecBg.ExtraChannels[(int)(c - 3)].ConstRow(bgYpos) + bgXpos
                            : pZeroes;
                    }
                }
            }

            private void FillColorPadding(float*[][] outputRows, nuint xsize, nuint xpos, nuint ypos)
            {
                nuint byteCount = xsize * sizeof(float);

                if (_bg.XSize() == 0 || _bg.YSize() == 0)
                {
                    for (nuint c = 0; c < 3; ++c)
                    {
                        new Span<float>(GetInputRow(outputRows, c, 0), (int)xsize).Clear();
                    }
                }
                else
                {
                    for (nuint c = 0; c < 3; ++c)
                    {
                        Buffer.MemoryCopy(
                            _bg.Color().ConstPlaneRow(c, ypos) + xpos,
                            GetInputRow(outputRows, c, 0),
                            byteCount,
                            byteCount);
                    }
                }
            }

            private void FillExtraChannelPadding(float*[][] outputRows, nuint xsize, nuint xpos, nuint ypos)
            {
                nuint byteCount = xsize * sizeof(float);

                for (int ec = 0; ec < _extraChannelInfo.Count; ++ec)
                {
                    uint source      = _frameHeader.ExtraChannelBlendingInfo[ec].Source;
                    ImageBundle ecBg = _state.ReferenceFrames[source].Frame!;
                    float* dst       = GetInputRow(outputRows, (nuint)(3 + ec), 0);

                    if (ecBg.XSize() == 0 || ecBg.YSize() == 0)
                    {
                        new Span<float>(dst, (int)xsize).Clear();
                    }
                    else
                    {
                        Buffer.MemoryCopy(
                            ecBg.ExtraChannels[ec].ConstRow(ypos) + xpos,
                            dst,
                            byteCount,
                            byteCount);
                    }
                }
            }
        }

        // --------------------------------------------------------------------
        // 16. Write Stage (stage_write.cc)
        // --------------------------------------------------------------------

        // 32x32 blue noise dithering pattern from
        // https://momentsingraphics.de/BlueNoise.html#Downloads scaled to have
        // an average of 0 and be fully contained in (0.49219 to -0.49219).
        // Rows are padded to 48 (32 + 16) to allow SIMD to wrap around horizontally.
        private static readonly float[] KDither =
        {
            -0.26057f, 0.32619f, 0.21039f, -0.03281f, -0.10616f, 0.16792f, 0.43042f, -0.48061f,
            -0.00965f, -0.31075f, 0.24899f, -0.35322f, -0.02509f, -0.25285f, 0.02895f, 0.10230f,
            -0.28373f, -0.00193f, 0.23355f, 0.43428f, -0.23741f, 0.18336f, -0.31847f, -0.11002f,
            -0.36094f, 0.26057f, -0.19108f, -0.29531f, 0.40726f, -0.09458f, 0.11002f, -0.48833f,
            -0.26057f, 0.32619f, 0.21039f, -0.03281f, -0.10616f, 0.16792f, 0.43042f, -0.48061f,
            -0.00965f, -0.31075f, 0.24899f, -0.35322f, -0.02509f, -0.25285f, 0.02895f, 0.10230f,
            0.16020f, -0.35708f, -0.18336f, 0.36094f, -0.28373f, -0.34550f, -0.20267f, 0.07914f,
            0.35708f, -0.41498f, 0.47675f, -0.21811f, -0.12546f, 0.44200f, -0.41884f, -0.17178f,
            0.39954f, 0.33778f, -0.33778f, 0.04053f, -0.46517f, 0.27215f, -0.16792f, 0.39182f,
            0.20653f, -0.43814f, -0.02895f, 0.17950f, -0.41498f, 0.01737f, 0.24899f, 0.49219f,
            0.16020f, -0.35708f, -0.18336f, 0.36094f, -0.28373f, -0.34550f, -0.20267f, 0.07914f,
            0.35708f, -0.41498f, 0.47675f, -0.21811f, -0.12546f, 0.44200f, -0.41884f, -0.17178f,
            -0.00965f, 0.08300f, 0.41112f, -0.46903f, 0.04053f, 0.47289f, 0.26057f, -0.05983f,
            -0.13704f, 0.14862f, 0.03281f, 0.29531f, -0.45744f, 0.22583f, 0.14862f, -0.09072f,
            -0.37638f, 0.19881f, -0.14476f, 0.14476f, -0.09072f, 0.48447f, -0.39954f, 0.06369f,
            -0.05983f, -0.26829f, 0.43428f, -0.12546f, 0.28759f, -0.22969f, -0.32619f, -0.15248f,
            -0.00965f, 0.08300f, 0.41112f, -0.46903f, 0.04053f, 0.47289f, 0.26057f, -0.05983f,
            -0.13704f, 0.14862f, 0.03281f, 0.29531f, -0.45744f, 0.22583f, 0.14862f, -0.09072f,
            -0.42270f, 0.23741f, -0.23355f, -0.11774f, 0.18722f, 0.11388f, -0.43814f, -0.24899f,
            0.41884f, 0.21039f, -0.28373f, -0.06756f, 0.07914f, 0.36480f, -0.31075f, 0.30303f,
            -0.03281f, 0.07142f, -0.42656f, 0.38024f, -0.27987f, 0.00579f, 0.12546f, -0.22197f,
            0.29917f, 0.36866f, 0.13704f, -0.47289f, 0.09072f, 0.35708f, -0.04825f, 0.38796f,
            -0.42270f, 0.23741f, -0.23355f, -0.11774f, 0.18722f, 0.11388f, -0.43814f, -0.24899f,
            0.41884f, 0.21039f, -0.28373f, -0.06756f, 0.07914f, 0.36480f, -0.31075f, 0.30303f,
            -0.28759f, -0.07142f, 0.44200f, 0.27601f, -0.38024f, -0.16020f, -0.01737f, 0.30303f,
            -0.33006f, -0.40340f, -0.16792f, 0.40726f, -0.36480f, -0.00579f, -0.19108f, 0.41498f,
            -0.26443f, 0.46903f, -0.21811f, 0.28759f, -0.04053f, 0.22197f, 0.34550f, -0.44972f,
            -0.14476f, -0.34164f, 0.04053f, -0.19494f, 0.45358f, -0.37252f, 0.21425f, 0.05597f,
            -0.28759f, -0.07142f, 0.44200f, 0.27601f, -0.38024f, -0.16020f, -0.01737f, 0.30303f,
            -0.33006f, -0.40340f, -0.16792f, 0.40726f, -0.36480f, -0.00579f, -0.19108f, 0.41498f,
            0.31075f, 0.14090f, -0.33778f, 0.00579f, 0.34550f, -0.29917f, 0.38796f, 0.13704f,
            0.05983f, -0.10230f, 0.34164f, 0.10616f, -0.23741f, 0.19494f, -0.47675f, 0.04439f,
            -0.39568f, 0.24127f, 0.10616f, -0.49219f, -0.17950f, -0.36094f, -0.30303f, 0.45744f,
            -0.01351f, 0.24513f, -0.39182f, -0.07528f, 0.18722f, -0.26057f, -0.11002f, -0.45358f,
            0.31075f, 0.14090f, -0.33778f, 0.00579f, 0.34550f, -0.29917f, 0.38796f, 0.13704f,
            0.05983f, -0.10230f, 0.34164f, 0.10616f, -0.23741f, 0.19494f, -0.47675f, 0.04439f,
            0.46903f, -0.17178f, -0.41112f, 0.07528f, -0.09458f, 0.21811f, -0.20267f, -0.48833f,
            0.44972f, 0.00965f, 0.24127f, -0.42656f, 0.48447f, -0.11774f, 0.26443f, 0.14090f,
            -0.15634f, -0.07142f, -0.32233f, 0.36094f, 0.42270f, 0.19108f, 0.07142f, -0.11002f,
            0.15634f, 0.38024f, -0.28759f, 0.27987f, -0.00193f, 0.33006f, 0.11388f, -0.21039f,
            0.46903f, -0.17178f, -0.41112f, 0.07528f, -0.09458f, 0.21811f, -0.20267f, -0.48833f,
            0.44972f, 0.00965f, 0.24127f, -0.42656f, 0.48447f, -0.11774f, 0.26443f, 0.14090f,
            0.02123f, 0.17950f, 0.38024f, -0.24127f, -0.44586f, 0.48833f, -0.03667f, 0.26829f,
            -0.36866f, -0.22583f, 0.17178f, -0.30689f, 0.29145f, -0.04825f, -0.35322f, 0.43042f,
            0.34936f, 0.00193f, 0.16792f, -0.12932f, 0.03667f, -0.06756f, 0.31847f, -0.40726f,
            -0.24513f, 0.09458f, -0.17564f, 0.47675f, -0.43042f, -0.32233f, 0.40340f, 0.26057f,
            0.02123f, 0.17950f, 0.38024f, -0.24127f, -0.44586f, 0.48833f, -0.03667f, 0.26829f,
            -0.36866f, -0.22583f, 0.17178f, -0.30689f, 0.29145f, -0.04825f, -0.35322f, 0.43042f,
            -0.47675f, -0.12160f, -0.04825f, 0.28759f, 0.10230f, 0.15634f, -0.14862f, -0.27601f,
            0.36094f, -0.12932f, -0.05983f, -0.45358f, -0.17950f, 0.01737f, 0.09458f, -0.29145f,
            -0.22969f, -0.43428f, 0.45744f, -0.38796f, -0.27601f, -0.21039f, -0.46131f, 0.22969f,
            0.41112f, -0.05211f, -0.48061f, 0.16406f, 0.05211f, -0.14862f, -0.03281f, -0.36866f,
            -0.47675f, -0.12160f, -0.04825f, 0.28759f, 0.10230f, 0.15634f, -0.14862f, -0.27601f,
            0.36094f, -0.12932f, -0.05983f, -0.45358f, -0.17950f, 0.01737f, 0.09458f, -0.29145f,
            -0.27215f, 0.34164f, -0.31075f, 0.42656f, -0.38410f, -0.32619f, 0.02895f, 0.19881f,
            0.08300f, 0.42270f, 0.31461f, 0.13318f, 0.45744f, 0.37638f, -0.40726f, 0.31847f,
            -0.08686f, 0.21425f, 0.29917f, 0.07914f, 0.26829f, 0.13704f, 0.48447f, -0.15248f,
            0.02509f, -0.34936f, 0.34936f, -0.10230f, 0.42656f, -0.23741f, 0.22583f, 0.09072f,
            -0.27215f, 0.34164f, -0.31075f, 0.42656f, -0.38410f, -0.32619f, 0.02895f, 0.19881f,
            0.08300f, 0.42270f, 0.31461f, 0.13318f, 0.45744f, 0.37638f, -0.40726f, 0.31847f,
            0.44972f, 0.20267f, 0.04825f, -0.21425f, 0.24513f, -0.07142f, 0.39954f, -0.46131f,
            -0.39568f, -0.01351f, -0.33392f, 0.05597f, -0.26443f, 0.22197f, -0.20653f, 0.15248f,
            0.04439f, -0.46517f, -0.16406f, -0.04439f, -0.34936f, 0.37252f, -0.01351f, -0.30689f,
            0.29917f, 0.20653f, -0.26829f, 0.26443f, 0.13318f, -0.39954f, 0.30303f, -0.08686f,
            0.44972f, 0.20267f, 0.04825f, -0.21425f, 0.24513f, -0.07142f, 0.39954f, -0.46131f,
            -0.39568f, -0.01351f, -0.33392f, 0.05597f, -0.26443f, 0.22197f, -0.20653f, 0.15248f,
            -0.42656f, 0.12932f, -0.14476f, -0.46903f, -0.00579f, 0.34936f, -0.18722f, 0.28373f,
            -0.23741f, 0.22969f, -0.16020f, -0.38024f, -0.08300f, -0.48447f, -0.02123f, -0.14862f,
            0.48061f, -0.31847f, 0.39568f, -0.24899f, 0.18722f, -0.41884f, 0.10230f, -0.08300f,
            -0.38796f, 0.06369f, -0.19881f, -0.44972f, 0.00579f, -0.33392f, 0.37252f, -0.19108f,
            -0.42656f, 0.12932f, -0.14476f, -0.46903f, -0.00579f, 0.34936f, -0.18722f, 0.28373f,
            -0.23741f, 0.22969f, -0.16020f, -0.38024f, -0.08300f, -0.48447f, -0.02123f, -0.14862f,
            -0.02509f, -0.35708f, 0.32619f, 0.46517f, 0.17178f, -0.28373f, 0.10616f, 0.47675f,
            -0.09458f, 0.15248f, 0.43428f, 0.35322f, 0.17564f, 0.27215f, 0.41112f, -0.36480f,
            0.24899f, 0.11774f, 0.01351f, 0.33006f, -0.11388f, -0.18336f, 0.41884f, -0.23355f,
            0.16406f, 0.46131f, 0.38410f, -0.04825f, -0.15634f, 0.49219f, 0.17564f, 0.03667f,
            -0.02509f, -0.35708f, 0.32619f, 0.46517f, 0.17178f, -0.28373f, 0.10616f, 0.47675f,
            -0.09458f, 0.15248f, 0.43428f, 0.35322f, 0.17564f, 0.27215f, 0.41112f, -0.36480f,
            0.40726f, 0.23355f, -0.25285f, -0.08300f, -0.41112f, -0.12160f, -0.35708f, 0.05211f,
            -0.41884f, -0.29531f, 0.02123f, -0.21425f, 0.09844f, -0.30689f, -0.11388f, 0.34550f,
            -0.26443f, -0.07142f, -0.39954f, 0.44586f, 0.05983f, -0.48833f, 0.24127f, 0.34936f,
            -0.44200f, -0.12546f, 0.12160f, -0.30303f, 0.27215f, 0.07528f, -0.48447f, -0.29145f,
            0.40726f, 0.23355f, -0.25285f, -0.08300f, -0.41112f, -0.12160f, -0.35708f, 0.05211f,
            -0.41884f, -0.29531f, 0.02123f, -0.21425f, 0.09844f, -0.30689f, -0.11388f, 0.34550f,
            0.28373f, -0.17564f, 0.09458f, 0.02123f, 0.30689f, 0.41884f, 0.20653f, -0.03667f,
            0.32233f, 0.25671f, -0.45744f, -0.05597f, 0.46517f, -0.41498f, 0.00965f, 0.07142f,
            -0.44586f, 0.16406f, -0.20653f, 0.21811f, -0.29917f, 0.28759f, -0.05597f, 0.03281f,
            -0.32619f, -0.00965f, 0.31847f, -0.37252f, 0.18722f, -0.11002f, -0.22969f, -0.06369f,
            0.28373f, -0.17564f, 0.09458f, 0.02123f, 0.30689f, 0.41884f, 0.20653f, -0.03667f,
            0.32233f, 0.25671f, -0.45744f, -0.05597f, 0.46517f, -0.41498f, 0.00965f, 0.07142f,
            -0.39568f, 0.36866f, -0.45744f, -0.31847f, 0.14476f, -0.22583f, -0.49219f, 0.37638f,
            -0.19494f, -0.13318f, 0.39182f, -0.35322f, 0.29531f, -0.24127f, 0.21039f, -0.18722f,
            0.45358f, 0.31461f, -0.13318f, -0.01737f, -0.36094f, 0.12932f, -0.25671f, 0.43814f,
            -0.16792f, 0.23355f, -0.22197f, 0.44972f, -0.42270f, 0.33392f, 0.42656f, 0.11774f,
            -0.39568f, 0.36866f, -0.45744f, -0.31847f, 0.14476f, -0.22583f, -0.49219f, 0.37638f,
            -0.19494f, -0.13318f, 0.39182f, -0.35322f, 0.29531f, -0.24127f, 0.21039f, -0.18722f,
            -0.13318f, 0.19494f, -0.03667f, 0.44972f, 0.24513f, -0.15248f, 0.08300f, -0.33006f,
            0.00579f, 0.12546f, 0.19494f, 0.05983f, -0.15634f, 0.14476f, 0.36480f, -0.04053f,
            -0.33006f, 0.25671f, -0.46903f, 0.37252f, 0.48833f, -0.09458f, -0.41112f, 0.19108f,
            0.08686f, -0.46903f, -0.07528f, 0.04053f, -0.26829f, -0.02895f, 0.22197f, -0.34164f,
            -0.13318f, 0.19494f, -0.03667f, 0.44972f, 0.24513f, -0.15248f, 0.08300f, -0.33006f,
            0.00579f, 0.12546f, 0.19494f, 0.05983f, -0.15634f, 0.14476f, 0.36480f, -0.04053f,
            0.47289f, -0.21811f, 0.06756f, -0.38410f, -0.27987f, -0.06369f, 0.27987f, 0.43814f,
            -0.25671f, -0.39182f, 0.49219f, -0.27601f, -0.07914f, -0.48061f, 0.42656f, -0.38410f,
            0.11002f, 0.03667f, -0.27215f, 0.15634f, 0.07528f, -0.22197f, 0.33006f, 0.38410f,
            -0.34936f, 0.27987f, 0.15248f, 0.40340f, 0.09844f, -0.16406f, -0.46131f, 0.03281f,
            0.47289f, -0.21811f, 0.06756f, -0.38410f, -0.27987f, -0.06369f, 0.27987f, 0.43814f,
            -0.25671f, -0.39182f, 0.49219f, -0.27601f, -0.07914f, -0.48061f, 0.42656f, -0.38410f,
            -0.29531f, 0.31461f, -0.10616f, 0.39954f, 0.01351f, 0.33778f, -0.43814f, 0.17178f,
            -0.08686f, 0.23741f, -0.44586f, 0.33778f, -0.00193f, -0.31461f, 0.23741f, -0.12932f,
            -0.22583f, -0.06756f, 0.40340f, -0.16792f, -0.43428f, 0.01351f, -0.14476f, -0.04053f,
            -0.29145f, 0.46517f, -0.13704f, -0.39182f, -0.32233f, 0.29531f, 0.38410f, 0.16020f,
            -0.29531f, 0.31461f, -0.10616f, 0.39954f, 0.01351f, 0.33778f, -0.43814f, 0.17178f,
            -0.08686f, 0.23741f, -0.44586f, 0.33778f, -0.00193f, -0.31461f, 0.23741f, -0.12932f,
            -0.44200f, 0.26443f, 0.12546f, -0.42270f, 0.21425f, -0.19881f, -0.35708f, 0.04825f,
            0.36480f, -0.02895f, -0.21425f, 0.09072f, 0.41498f, 0.18336f, 0.04439f, 0.29917f,
            0.47675f, -0.40340f, 0.27601f, -0.31461f, 0.31075f, 0.17564f, 0.24899f, -0.45744f,
            0.05597f, -0.19494f, 0.00193f, 0.36094f, 0.24127f, -0.09844f, -0.24513f, -0.00965f,
            -0.44200f, 0.26443f, 0.12546f, -0.42270f, 0.21425f, -0.19881f, -0.35708f, 0.04825f,
            0.36480f, -0.02895f, -0.21425f, 0.09072f, 0.41498f, 0.18336f, 0.04439f, 0.29917f,
            -0.17564f, -0.05597f, -0.34550f, -0.24899f, 0.48061f, 0.15248f, -0.11388f, 0.45358f,
            -0.16406f, -0.32233f, 0.31461f, -0.11774f, -0.36866f, -0.18722f, -0.25671f, -0.44200f,
            0.13318f, -0.02123f, 0.19881f, -0.10616f, 0.43042f, -0.36866f, -0.24899f, 0.41112f,
            0.11002f, 0.21425f, -0.25671f, -0.47675f, -0.04439f, 0.13704f, -0.37252f, 0.43814f,
            -0.17564f, -0.05597f, -0.34550f, -0.24899f, 0.48061f, 0.15248f, -0.11388f, 0.45358f,
            -0.16406f, -0.32233f, 0.31461f, -0.11774f, -0.36866f, -0.18722f, -0.25671f, -0.44200f,
            0.19108f, 0.03667f, 0.35708f, -0.14090f, 0.08300f, -0.02123f, -0.30303f, -0.48061f,
            0.11774f, 0.20267f, -0.43042f, 0.25285f, 0.14090f, -0.04439f, 0.38796f, 0.34550f,
            -0.34164f, -0.19494f, 0.05983f, -0.48447f, 0.09844f, -0.00579f, -0.07914f, 0.33778f,
            -0.41498f, -0.10230f, 0.30689f, 0.17178f, 0.48833f, -0.20267f, 0.07914f, 0.33392f,
            0.19108f, 0.03667f, 0.35708f, -0.14090f, 0.08300f, -0.02123f, -0.30303f, -0.48061f,
            0.11774f, 0.20267f, -0.43042f, 0.25285f, 0.14090f, -0.04439f, 0.38796f, 0.34550f,
            -0.48833f, -0.30689f, 0.41498f, 0.22969f, -0.44586f, 0.32233f, 0.25285f, 0.39182f,
            -0.23355f, 0.01737f, 0.42270f, -0.27987f, 0.46903f, -0.47289f, 0.02123f, -0.09072f,
            0.21811f, 0.44586f, -0.25285f, 0.36480f, -0.29145f, 0.47289f, -0.18722f, 0.14476f,
            -0.31461f, 0.43814f, -0.36094f, 0.04439f, -0.29917f, -0.41884f, 0.25285f, -0.11774f,
            -0.48833f, -0.30689f, 0.41498f, 0.22969f, -0.44586f, 0.32233f, 0.25285f, 0.39182f,
            -0.23355f, 0.01737f, 0.42270f, -0.27987f, 0.46903f, -0.47289f, 0.02123f, -0.09072f,
            0.46131f, 0.11388f, -0.21039f, -0.07528f, -0.38024f, -0.26057f, 0.06369f, -0.05983f,
            0.29145f, -0.40340f, -0.09072f, 0.06756f, -0.16020f, 0.27601f, -0.31075f, 0.10616f,
            -0.14090f, -0.43042f, 0.25671f, -0.05211f, -0.13318f, 0.23355f, -0.44972f, 0.02895f,
            0.26829f, -0.02895f, -0.17950f, 0.37252f, -0.13704f, 0.40726f, 0.01351f, -0.26443f,
            0.46131f, 0.11388f, -0.21039f, -0.07528f, -0.38024f, -0.26057f, 0.06369f, -0.05983f,
            0.29145f, -0.40340f, -0.09072f, 0.06756f, -0.16020f, 0.27601f, -0.31075f, 0.10616f,
            -0.03281f, -0.40340f, 0.27987f, 0.17564f, 0.02509f, 0.44200f, -0.15248f, -0.34550f,
            0.14862f, -0.19881f, -0.01351f, 0.36866f, -0.38796f, 0.19494f, -0.22197f, 0.32619f,
            -0.37638f, 0.00193f, 0.30689f, 0.12160f, -0.39182f, 0.16792f, -0.34550f, 0.39954f,
            -0.23355f, 0.09072f, -0.43428f, 0.22969f, -0.06369f, 0.12546f, -0.35322f, 0.30689f,
            -0.03281f, -0.40340f, 0.27987f, 0.17564f, 0.02509f, 0.44200f, -0.15248f, -0.34550f,
            0.14862f, -0.19881f, -0.01351f, 0.36866f, -0.38796f, 0.19494f, -0.22197f, 0.32619f,
            -0.09844f, 0.06756f, 0.38410f, -0.33392f, -0.18336f, 0.35322f, 0.21039f, -0.42270f,
            0.48833f, 0.33006f, 0.21811f, -0.33392f, 0.12932f, -0.05211f, 0.39568f, 0.04825f,
            0.48061f, 0.17950f, -0.31847f, -0.21811f, 0.38024f, 0.05211f, 0.32233f, -0.06756f,
            -0.12546f, 0.46131f, 0.16020f, -0.25285f, 0.29531f, -0.44972f, 0.17950f, -0.16406f,
            -0.09844f, 0.06756f, 0.38410f, -0.33392f, -0.18336f, 0.35322f, 0.21039f, -0.42270f,
            0.48833f, 0.33006f, 0.21811f, -0.33392f, 0.12932f, -0.05211f, 0.39568f, 0.04825f,
            0.22583f, -0.46131f, -0.27601f, -0.00579f, 0.12932f, -0.47289f, -0.09844f, 0.10230f,
            -0.28759f, -0.12160f, -0.49219f, -0.24127f, 0.44586f, -0.11388f, -0.45358f, -0.27215f,
            -0.17178f, -0.07528f, -0.47675f, 0.43042f, -0.02509f, -0.27215f, -0.19108f, 0.19881f,
            -0.49219f, -0.37252f, 0.33392f, -0.00193f, -0.33006f, -0.20267f, 0.48061f, 0.34164f,
            0.22583f, -0.46131f, -0.27601f, -0.00579f, 0.12932f, -0.47289f, -0.09844f, 0.10230f,
            -0.28759f, -0.12160f, -0.49219f, -0.24127f, 0.44586f, -0.11388f, -0.45358f, -0.27215f,
            -0.22969f, 0.42270f, -0.12160f, 0.31075f, 0.46903f, -0.22583f, 0.27215f, -0.02509f,
            0.03281f, 0.40340f, 0.25671f, 0.08686f, 0.00965f, 0.29145f, -0.41112f, 0.14090f,
            0.24513f, 0.34164f, 0.08686f, -0.14862f, 0.27601f, -0.42656f, 0.48447f, 0.09844f,
            0.26443f, -0.27987f, 0.05597f, -0.10230f, 0.43428f, 0.08686f, 0.02895f, -0.38024f,
            -0.22969f, 0.42270f, -0.12160f, 0.31075f, 0.46903f, -0.22583f, 0.27215f, -0.02509f,
            0.03281f, 0.40340f, 0.25671f, 0.08686f, 0.00965f, 0.29145f, -0.41112f, 0.14090f,
            0.15634f, 0.09458f, -0.36480f, 0.18336f, -0.05211f, -0.40726f, 0.36866f, -0.33778f,
            -0.19881f, 0.16020f, -0.37638f, -0.16020f, -0.29917f, 0.20267f, 0.41884f, -0.01737f,
            -0.34936f, -0.24127f, 0.02509f, 0.20653f, -0.36480f, -0.08686f, 0.01737f, -0.33778f,
            0.41498f, -0.03667f, 0.37638f, -0.17178f, -0.47289f, 0.26829f, -0.28759f, -0.05597f,
            0.15634f, 0.09458f, -0.36480f, 0.18336f, -0.05211f, -0.40726f, 0.36866f, -0.33778f,
            -0.19881f, 0.16020f, -0.37638f, -0.16020f, -0.29917f, 0.20267f, 0.41884f, -0.01737f,
            0.35708f, 0.00193f, 0.25285f, -0.15634f, -0.30303f, 0.06369f, 0.22197f, 0.45358f,
            -0.43814f, 0.30303f, -0.04053f, 0.46517f, 0.35322f, -0.21039f, 0.06756f, -0.14090f,
            0.37638f, -0.43042f, 0.45744f, -0.29531f, 0.39568f, 0.14862f, 0.23741f, -0.13704f,
            -0.21425f, 0.16406f, -0.40726f, 0.22583f, 0.13318f, 0.38796f, -0.12932f, -0.43428f,
            0.35708f, 0.00193f, 0.25285f, -0.15634f, -0.30303f, 0.06369f, 0.22197f, 0.45358f,
            -0.43814f, 0.30303f, -0.04053f, 0.46517f, 0.35322f, -0.21039f, 0.06756f, -0.14090f,
            -0.31461f, -0.20653f, 0.46131f, -0.45358f, 0.39568f, -0.24513f, -0.14090f, 0.11002f,
            -0.08300f, -0.26829f, 0.05211f, -0.46517f, -0.09844f, -0.39568f, -0.32619f, -0.06369f,
            0.16792f, 0.28373f, 0.11388f, -0.04439f, -0.18336f, -0.44200f, 0.35322f, -0.26057f,
            -0.46517f, 0.31075f, -0.07914f, -0.34164f, -0.24513f, -0.02123f, 0.19108f, 0.44200f,
            -0.31461f, -0.20653f, 0.46131f, -0.45358f, 0.39568f, -0.24513f, -0.14090f, 0.11002f,
            -0.08300f, -0.26829f, 0.05211f, -0.46517f, -0.09844f, -0.39568f, -0.32619f, -0.06369f,
            0.04825f, -0.07914f, -0.39954f, 0.12160f, 0.29145f, 0.00965f, -0.37638f, 0.32233f,
            0.20267f, -0.17564f, 0.39182f, 0.12160f, 0.18336f, 0.32619f, 0.26057f, 0.49219f,
            -0.48447f, -0.20653f, -0.10616f, -0.38796f, 0.31847f, 0.07528f, -0.01737f, 0.44586f,
            0.11774f, 0.02509f, 0.47289f, 0.07142f, 0.33392f, -0.38410f, -0.17950f, 0.28373f,
            0.04825f, -0.07914f, -0.39954f, 0.12160f, 0.29145f, 0.00965f, -0.37638f, 0.32233f,
            0.20267f, -0.17564f, 0.39182f, 0.12160f, 0.18336f, 0.32619f, 0.26057f, 0.49219f,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte MakeUnsigned8(float v, nuint x, nuint y, float mul, nuint c)
        {
            v *= mul;
            nuint xOff = (x + c * 23) % 32;
            nuint yOff = (y + c * 13) % 32;
            v += KDither[yOff * 48 + xOff];
            v  = Math.Clamp(v, 0.0f, mul);

            return (byte)MathF.Round(v, MidpointRounding.ToEven);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort MakeUnsigned16(float v, float mul)
        {
            v = Math.Clamp(v * mul, 0.0f, mul);

            return (ushort)MathF.Round(v, MidpointRounding.ToEven);
        }

        private sealed class WriteToOutputStage : RenderPipelineStage
        {
            private const nuint KChunkSize = 1024;

            private sealed class Output : IDisposable
            {
                public readonly PixelCallback PixelCallback;
                public readonly void* Buffer;
                public readonly nuint BufferSize;
                public readonly nuint Stride;
                public readonly nuint NumChannels;
                public readonly bool SwapEndianness;
                public readonly JxlDataType DataType;
                public readonly nuint BitsPerSample;
                public nuint ChannelIndex;
                public object? RunOpaque;

                public Output(ImageOutput imageOut)
                {
                    PixelCallback  = imageOut.Callback;
                    Buffer         = imageOut.Buffer;
                    BufferSize     = imageOut.BufferSize;
                    Stride         = imageOut.Stride;
                    NumChannels    = imageOut.Format.NumChannels;
                    SwapEndianness = ByteOrder.SwapEndianness(imageOut.Format.Endianness);
                    DataType       = imageOut.Format.DataType;
                    BitsPerSample  = imageOut.BitsPerSample;
                }

                public Status PrepareForThreads(nuint numThreads)
                {
                    if (PixelCallback.IsPresent())
                    {
                        RunOpaque = PixelCallback.Init(numThreads, KChunkSize);

                        return RunOpaque != null;
                    }

                    return Buffer != null;
                }

                public void Dispose()
                {
                    if (RunOpaque != null && PixelCallback.Destroy != null)
                    {
                        PixelCallback.Destroy(RunOpaque);
                        RunOpaque = null;
                    }
                }
            }

            private readonly nuint _width;
            private readonly nuint _height;
            private readonly Output _main;
            private readonly nuint _numColor;
            private readonly bool _wantAlpha;
            private readonly bool _hasAlpha;
            private readonly bool _unpremulAlpha;
            private readonly nuint _alphaC;
            private readonly bool _flipX;
            private readonly bool _flipY;
            private readonly bool _transpose;
            private readonly List<Output> _extraChannels = new List<Output>();
            private readonly float[] _opaqueAlpha;
            private readonly JxlMemoryManager _memoryManager;
            private AlignedMemory[] _tempIn = Array.Empty<AlignedMemory>();
            private AlignedMemory[] _tempOut = Array.Empty<AlignedMemory>();

            public WriteToOutputStage(
                ImageOutput mainOutput,
                (nuint Width, nuint Height) dimensions,
                (bool HasAlpha, bool UnpremulAlpha, nuint AlphaC) alphaInfo,
                Orientation undoOrientation,
                IReadOnlyList<ImageOutput> extraOutput,
                JxlMemoryManager? memoryManager)
                : base(new Settings())
            {
                _width         = dimensions.Width;
                _height        = dimensions.Height;
                _main          = new Output(mainOutput);
                _numColor      = _main.NumChannels < 3 ? 1u : 3u;
                _wantAlpha     = _main.NumChannels == 2 || _main.NumChannels == 4;
                _hasAlpha      = alphaInfo.HasAlpha;
                _unpremulAlpha = alphaInfo.UnpremulAlpha;
                _alphaC        = alphaInfo.AlphaC;
                _flipX         = ShouldFlipX(undoOrientation);
                _flipY         = ShouldFlipY(undoOrientation);
                _transpose     = ShouldTranspose(undoOrientation);
                _opaqueAlpha   = new float[KChunkSize];
                Array.Fill(_opaqueAlpha, 1.0f);
                _memoryManager = memoryManager ?? JxlMemoryManager.Default;

                for (int ec = 0; ec < extraOutput.Count; ++ec)
                {
                    if (extraOutput[ec].Callback.IsPresent() || extraOutput[ec].Buffer != null)
                    {
                        Output extra       = new Output(extraOutput[ec]);
                        extra.ChannelIndex = (nuint)(3 + ec);
                        _extraChannels.Add(extra);
                    }
                }
            }

            public override Status PrepareForThreads(nuint numThreads)
            {
                if (!_main.PrepareForThreads(numThreads))
                {
                    return false;
                }

                for (int i = 0; i < _extraChannels.Count; i++)
                {
                    if (!_extraChannels[i].PrepareForThreads(numThreads))
                    {
                        return false;
                    }
                }

                DisposeAlignedBuffers();
                _tempOut        = new AlignedMemory[numThreads];
                nuint allocSize = sizeof(float) * KChunkSize;

                for (nuint t = 0; t < numThreads; t++)
                {
                    StatusOr<AlignedMemory> outOr = AlignedMemory.Create(_memoryManager, allocSize * _main.NumChannels);

                    if (!outOr.IsOk)
                    {
                        return outOr.Status;
                    }

                    _tempOut[t] = outOr.Value;
                }

                if ((_hasAlpha && _wantAlpha && _unpremulAlpha) || _flipX)
                {
                    nuint totalIn = numThreads * _main.NumChannels;
                    _tempIn       = new AlignedMemory[totalIn];

                    for (nuint i = 0; i < totalIn; i++)
                    {
                        StatusOr<AlignedMemory> inOr = AlignedMemory.Create(_memoryManager, allocSize);

                        if (!inOr.IsOk)
                        {
                            return inOr.Status;
                        }

                        _tempIn[i] = inOr.Value;
                    }
                }

                return true;
            }

            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                _ = outputRows;

                if (xextra.Left != 0 || xextra.Right != 0)
                {
                    return Status.Failure("Expected zero xextra in WriteToOutputStage");
                }

                if (_main.RunOpaque == null && _main.Buffer == null)
                {
                    return Status.Failure("Main output not initialized in WriteToOutputStage");
                }

                if (ypos >= _height || xpos >= _width)
                {
                    return true;
                }

                if (_flipY)
                {
                    ypos = _height - 1u - ypos;
                }

                nuint limit = Math.Min(xsize, _width - xpos);

                fixed (float* pOpaque = _opaqueAlpha)
                {
                    for (nuint x0 = 0; x0 < limit; x0 += KChunkSize)
                    {
                        nuint xstart = xpos + x0;
                        nuint len    = Math.Min(KChunkSize, limit - x0);
                        ProcessChunk(inputRows, threadId, ypos, x0, xstart, len, pOpaque);
                    }
                }

                return true;
            }

            public override RenderPipelineChannelMode GetChannelMode(nuint c)
            {
                if (c < _numColor || (_hasAlpha && c == _alphaC))
                {
                    return RenderPipelineChannelMode.Input;
                }

                for (int i = 0; i < _extraChannels.Count; i++)
                {
                    if (c == _extraChannels[i].ChannelIndex)
                    {
                        return RenderPipelineChannelMode.Input;
                    }
                }

                return RenderPipelineChannelMode.Ignored;
            }

            public override string GetName() => "WritePixelCB";

            public override void Dispose()
            {
                _main.Dispose();

                for (int i = 0; i < _extraChannels.Count; i++)
                {
                    _extraChannels[i].Dispose();
                }

                DisposeAlignedBuffers();
                base.Dispose();
            }

            private void DisposeAlignedBuffers()
            {
                for (int i = 0; i < _tempOut.Length; i++)
                {
                    _tempOut[i].Dispose();
                }

                for (int i = 0; i < _tempIn.Length; i++)
                {
                    _tempIn[i].Dispose();
                }

                _tempOut = Array.Empty<AlignedMemory>();
                _tempIn  = Array.Empty<AlignedMemory>();
            }

            private void ProcessChunk(
                float*[][] inputRows,
                nuint threadId,
                nuint ypos,
                nuint x0,
                nuint xstart,
                nuint len,
                float* pOpaque)
            {
                float** lineBuffers = stackalloc float*[4];

                for (nuint c = 0; c < _numColor; c++)
                {
                    lineBuffers[c] = GetInputRow(inputRows, c, 0) + x0;
                }

                lineBuffers[_numColor] = _hasAlpha
                    ? GetInputRow(inputRows, _alphaC, 0) + x0
                    : pOpaque;

                if (_hasAlpha && _wantAlpha && _unpremulAlpha)
                {
                    UnpremulAlpha(threadId, len, lineBuffers);
                }

                OutputBuffers(_main, threadId, ypos, xstart, len, lineBuffers);

                for (int i = 0; i < _extraChannels.Count; i++)
                {
                    Output extra   = _extraChannels[i];
                    lineBuffers[0] = GetInputRow(inputRows, extra.ChannelIndex, 0) + x0;
                    OutputBuffers(extra, threadId, ypos, xstart, len, lineBuffers);
                }
            }

            private static bool ShouldFlipX(Orientation undoOrientation)
            {
                return undoOrientation == Orientation.FlipHorizontal ||
                       undoOrientation == Orientation.Rotate180 ||
                       undoOrientation == Orientation.Rotate270 ||
                       undoOrientation == Orientation.AntiTranspose;
            }

            private static bool ShouldFlipY(Orientation undoOrientation)
            {
                return undoOrientation == Orientation.FlipVertical ||
                       undoOrientation == Orientation.Rotate180 ||
                       undoOrientation == Orientation.Rotate90 ||
                       undoOrientation == Orientation.AntiTranspose;
            }

            private static bool ShouldTranspose(Orientation undoOrientation)
            {
                return undoOrientation == Orientation.Transpose ||
                       undoOrientation == Orientation.Rotate90 ||
                       undoOrientation == Orientation.Rotate270 ||
                       undoOrientation == Orientation.AntiTranspose;
            }

            private void UnpremulAlpha(nuint threadId, nuint len, float** lineBuffers)
            {
                float** tempIn  = stackalloc float*[4];
                nuint copyBytes = sizeof(float) * len;

                for (nuint c = 0; c < _main.NumChannels; ++c)
                {
                    nuint tix = threadId * _main.NumChannels + c;
                    tempIn[c] = (float*)_tempIn[tix].Address;
                    Buffer.MemoryCopy(lineBuffers[c], tempIn[c], copyBytes, copyBytes);
                }

                float* alphaRow = tempIn[_numColor];

                for (nuint ix = 0; ix < len; ++ix)
                {
                    float mul = 1.0f / MathF.Max(Alpha.kSmallAlpha, alphaRow[ix]);

                    for (nuint c = 0; c < _numColor; ++c)
                    {
                        tempIn[c][ix] *= mul;
                    }
                }

                for (nuint c = 0; c < _main.NumChannels; ++c)
                {
                    lineBuffers[c] = tempIn[c];
                }
            }

            private void OutputBuffers(
                Output output,
                nuint threadId,
                nuint ypos,
                nuint xstart,
                nuint len,
                float** input)
            {
                if (_flipX)
                {
                    FlipX(output, threadId, len, ref xstart, input);
                }

                if (output.DataType == JxlDataType.UInt8)
                {
                    byte* temp = (byte*)_tempOut[threadId].Address;
                    StoreUnsigned8Row(output, input, len, temp, xstart, ypos);
                    WriteToOutput(output, threadId, ypos, xstart, len, temp);
                }
                else if (output.DataType == JxlDataType.UInt16 || output.DataType == JxlDataType.Float16)
                {
                    ushort* temp = (ushort*)_tempOut[threadId].Address;

                    if (output.DataType == JxlDataType.UInt16)
                    {
                        StoreUnsigned16Row(output, input, len, temp);
                    }
                    else
                    {
                        StoreFloat16Row(output, input, len, temp);
                    }

                    if (output.SwapEndianness)
                    {
                        nuint outputLen = len * output.NumChannels;

                        for (nuint j = 0; j < outputLen; ++j)
                        {
                            temp[j] = BinaryPrimitives.ReverseEndianness(temp[j]);
                        }
                    }

                    WriteToOutput(output, threadId, ypos, xstart, len, temp);
                }
                else if (output.DataType == JxlDataType.Float)
                {
                    float* temp = (float*)_tempOut[threadId].Address;
                    StoreFloatRow(output, input, len, temp);

                    if (output.SwapEndianness)
                    {
                        nuint outputLen = len * output.NumChannels;

                        for (nuint j = 0; j < outputLen; ++j)
                        {
                            temp[j] = ByteOrder.BSwapFloat(temp[j]);
                        }
                    }

                    WriteToOutput(output, threadId, ypos, xstart, len, temp);
                }
            }

            private void FlipX(
                Output output,
                nuint threadId,
                nuint len,
                ref nuint xstart,
                float** lineBuffers)
            {
                float** tempIn  = stackalloc float*[4];
                nuint copyBytes = sizeof(float) * len;

                for (nuint c = 0; c < output.NumChannels; ++c)
                {
                    nuint tix = threadId * _main.NumChannels + c;
                    tempIn[c] = (float*)_tempIn[tix].Address;

                    if (tempIn[c] != lineBuffers[c])
                    {
                        Buffer.MemoryCopy(lineBuffers[c], tempIn[c], copyBytes, copyBytes);
                    }
                }

                nuint last = len - 1u;
                nuint num  = len / 2u;

                for (nuint i = 0; i < num; ++i)
                {
                    for (nuint c = 0; c < output.NumChannels; ++c)
                    {
                        (tempIn[c][i], tempIn[c][last - i]) = (tempIn[c][last - i], tempIn[c][i]);
                    }
                }

                for (nuint c = 0; c < output.NumChannels; ++c)
                {
                    lineBuffers[c] = tempIn[c];
                }

                xstart = _width - xstart - len;
            }

            private static void StoreUnsigned8Row(
                Output output,
                float** input,
                nuint len,
                byte* dst,
                nuint xstart,
                nuint ypos)
            {
                float mul   = (1u << (int)output.BitsPerSample) - 1u;
                nuint numCh = output.NumChannels;

                for (nuint i = 0; i < len; ++i)
                {
                    nuint baseIdx = i * numCh;

                    for (nuint c = 0; c < numCh; ++c)
                    {
                        dst[baseIdx + c] = MakeUnsigned8(input[c][i], xstart + i, ypos, mul, c);
                    }
                }
            }

            private static void StoreUnsigned16Row(
                Output output,
                float** input,
                nuint len,
                ushort* dst)
            {
                float mul   = (1u << (int)output.BitsPerSample) - 1u;
                nuint numCh = output.NumChannels;

                for (nuint i = 0; i < len; ++i)
                {
                    nuint baseIdx = i * numCh;

                    for (nuint c = 0; c < numCh; ++c)
                    {
                        dst[baseIdx + c] = MakeUnsigned16(input[c][i], mul);
                    }
                }
            }

            private static void StoreFloat16Row(
                Output output,
                float** input,
                nuint len,
                ushort* dst)
            {
                nuint numCh = output.NumChannels;

                for (nuint i = 0; i < len; ++i)
                {
                    nuint baseIdx = i * numCh;

                    for (nuint c = 0; c < numCh; ++c)
                    {
                        dst[baseIdx + c] = BitConverter.HalfToUInt16Bits((Half)input[c][i]);
                    }
                }
            }

            private static void StoreFloatRow(
                Output output,
                float** input,
                nuint len,
                float* dst)
            {
                nuint numCh = output.NumChannels;

                if (numCh == 1)
                {
                    nuint bytes = len * sizeof(float);
                    Buffer.MemoryCopy(input[0], dst, bytes, bytes);

                    return;
                }

                for (nuint i = 0; i < len; ++i)
                {
                    nuint baseIdx = i * numCh;

                    for (nuint c = 0; c < numCh; ++c)
                    {
                        dst[baseIdx + c] = input[c][i];
                    }
                }
            }

            private void WriteToOutput<T>(
                Output output,
                nuint threadId,
                nuint ypos,
                nuint xstart,
                nuint len,
                T* src)
                where T : unmanaged
            {
                nuint pixelStride = output.NumChannels * (nuint)sizeof(T);

                if (_transpose)
                {
                    if (output.RunOpaque != null)
                    {
                        for (nuint i = 0, j = 0; i < len; ++i, j += output.NumChannels)
                        {
                            output.PixelCallback.Run!(output.RunOpaque, threadId, ypos, xstart + i, 1, src + j);
                        }
                    }
                    else
                    {
                        nuint offset = xstart * output.Stride + ypos * pixelStride;

                        for (nuint i = 0, j = 0; i < len; ++i, j += output.NumChannels)
                        {
                            nuint ix = offset + i * output.Stride;
                            Debug.Assert(ix + pixelStride <= output.BufferSize);
                            Buffer.MemoryCopy(src + j, (byte*)output.Buffer + ix, pixelStride, pixelStride);
                        }
                    }
                }
                else
                {
                    if (output.RunOpaque != null)
                    {
                        output.PixelCallback.Run!(output.RunOpaque, threadId, xstart, ypos, len, src);
                    }
                    else
                    {
                        nuint offset    = ypos * output.Stride + xstart * pixelStride;
                        nuint totalByte = len * pixelStride;
                        Debug.Assert(offset + totalByte <= output.BufferSize);
                        Buffer.MemoryCopy(src, (byte*)output.Buffer + offset, totalByte, totalByte);
                    }
                }
            }
        }

        private sealed class WriteToImageBundleStage : RenderPipelineStage
        {
            private readonly ImageBundle _imageBundle;
            private readonly ColorEncoding _colorEncoding;

            public WriteToImageBundleStage(ImageBundle imageBundle, ColorEncoding colorEncoding)
                : base(new Settings())
            {
                _imageBundle   = imageBundle;
                _colorEncoding = colorEncoding.Clone();
            }

            public override Status SetInputSizes(IReadOnlyList<(nuint XSize, nuint YSize)> inputSizes)
            {
                JxlMemoryManager mm = _imageBundle.MemoryManager ?? JxlMemoryManager.Default;

                if (inputSizes.Count < 3)
                {
                    return Status.Failure("Expected at least 3 channels in WriteToImageBundleStage");
                }

                for (int c = 1; c < inputSizes.Count; c++)
                {
                    if (inputSizes[c].XSize != inputSizes[0].XSize ||
                        inputSizes[c].YSize != inputSizes[0].YSize)
                    {
                        return Status.Failure("Mismatched channel sizes in WriteToImageBundleStage");
                    }
                }

                StatusOr<Image3<float>> tmpOr = Image3<float>.Create(mm, inputSizes[0].XSize, inputSizes[0].YSize);

                if (!tmpOr.IsOk)
                {
                    return tmpOr.Status;
                }

                if (!_imageBundle.SetFromImage(tmpOr.Value, _colorEncoding))
                {
                    tmpOr.Value.Dispose();

                    return false;
                }

                for (int i = 0; i < _imageBundle.ExtraChannels.Count; i++)
                {
                    _imageBundle.ExtraChannels[i].Dispose();
                }

                _imageBundle.ExtraChannels.Clear();

                for (int c = 3; c < inputSizes.Count; c++)
                {
                    StatusOr<ImagePlane<float>> chOr = ImagePlane<float>.Create(mm, inputSizes[c].XSize, inputSizes[c].YSize);

                    if (!chOr.IsOk)
                    {
                        return chOr.Status;
                    }

                    _imageBundle.ExtraChannels.Add(chOr.Value);
                }

                return true;
            }

            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                _ = outputRows;
                _ = threadId;

                if (xextra.Left != 0 || xextra.Right != 0)
                {
                    return Status.Failure("Expected zero xextra in WriteToImageBundleStage");
                }

                nuint copyBytes = sizeof(float) * xsize;

                for (nuint c = 0; c < 3; c++)
                {
                    Buffer.MemoryCopy(
                        GetInputRow(inputRows, c, 0),
                        _imageBundle.Color().PlaneRow(c, ypos) + xpos,
                        copyBytes,
                        copyBytes);
                }

                for (int ec = 0; ec < _imageBundle.ExtraChannels.Count; ec++)
                {
                    if (_imageBundle.ExtraChannels[ec].XSize < xpos + xsize)
                    {
                        return Status.Failure("Extra channel width smaller than xpos + xsize");
                    }

                    Buffer.MemoryCopy(
                        GetInputRow(inputRows, (nuint)(3 + ec), 0),
                        _imageBundle.ExtraChannels[ec].Row(ypos) + xpos - xextra.Left,
                        copyBytes,
                        copyBytes);
                }

                return true;
            }

            public override RenderPipelineChannelMode GetChannelMode(nuint c)
            {
                _ = c;

                return RenderPipelineChannelMode.Input;
            }

            public override string GetName() => "WriteIB";
        }

        private sealed class WriteToImage3FStage : RenderPipelineStage
        {
            private readonly JxlMemoryManager _memoryManager;
            private readonly PassesSharedState? _shared;
            private readonly int _dcFrameIndex;
            private Image3<float>? _image;

            public WriteToImage3FStage(
                JxlMemoryManager? memoryManager,
                PassesSharedState shared,
                int dcFrameIndex)
                : base(new Settings())
            {
                _memoryManager = memoryManager ?? JxlMemoryManager.Default;
                _shared        = shared;
                _dcFrameIndex  = dcFrameIndex;
                _image         = shared.DcFrames[dcFrameIndex];
            }

            public WriteToImage3FStage(
                JxlMemoryManager? memoryManager,
                Image3<float> image)
                : base(new Settings())
            {
                _memoryManager = memoryManager ?? JxlMemoryManager.Default;
                _image         = image;
            }

            public override Status SetInputSizes(IReadOnlyList<(nuint XSize, nuint YSize)> inputSizes)
            {
                if (inputSizes.Count < 3)
                {
                    return Status.Failure("Expected at least 3 channels in WriteToImage3FStage");
                }

                for (int c = 1; c < 3; ++c)
                {
                    if (inputSizes[c].XSize != inputSizes[0].XSize ||
                        inputSizes[c].YSize != inputSizes[0].YSize)
                    {
                        return Status.Failure("Mismatched channel sizes in WriteToImage3FStage");
                    }
                }

                StatusOr<Image3<float>> imgOr = Image3<float>.Create(
                    _memoryManager,
                    inputSizes[0].XSize,
                    inputSizes[0].YSize);

                if (!imgOr.IsOk)
                {
                    return imgOr.Status;
                }

                if (_shared != null)
                {
                    _shared.DcFrames[_dcFrameIndex].Dispose();
                    _shared.DcFrames[_dcFrameIndex] = imgOr.Value;
                    _image                          = imgOr.Value;
                }
                else
                {
                    _image = imgOr.Value;
                }

                return true;
            }

            public override Status ProcessRow(
                float*[][] inputRows,
                float*[][] outputRows,
                (nuint Left, nuint Right) xextra,
                nuint xsize,
                nuint xpos,
                nuint ypos,
                nuint threadId)
            {
                _ = outputRows;
                _ = threadId;

                if (xextra.Left != 0 || xextra.Right != 0)
                {
                    return Status.Failure("Expected zero xextra in WriteToImage3FStage");
                }

                nuint copyBytes = sizeof(float) * xsize;

                for (nuint c = 0; c < 3; c++)
                {
                    Buffer.MemoryCopy(
                        GetInputRow(inputRows, c, 0),
                        _image!.PlaneRow(c, ypos) + xpos,
                        copyBytes,
                        copyBytes);
                }

                return true;
            }

            public override RenderPipelineChannelMode GetChannelMode(nuint c)
            {
                return c < 3 ? RenderPipelineChannelMode.Input : RenderPipelineChannelMode.Ignored;
            }

            public override string GetName() => "WriteI3F";
        }
    }
}
