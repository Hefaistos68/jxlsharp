# JxlSharp

**JxlSharp** is a pure C# / .NET 10 implementation of the **JPEG XL (`libjxl` v0.13.0)** reference image codec, bundling the complete encoder, decoder, color management system (CMS), multithreaded runner, and external format codecs into a single self-contained NuGet package.

- **Author:** Hefaistos68
- **Email:** dev@hefaistos68.dev
- **Repository:** [https://github.com/Hefaistos68](https://github.com/Hefaistos68)
- **License:** BSD-3-Clause

---

## Features

- **100% Managed .NET 10:** No native C/C++ runtime binaries required; SIMD-accelerated (`Vector256<float>`, AVX2/FMA).
- **Two-Tier API Design:**
  - **Simple Interface (`IJxlSimpleCodec` / `JxlCodec`):** One-call encoding and decoding with the most common parameter defaults (visually lossless quality `90` / Butteraugli distance `1.0`, effort `7` Squirrel, automatic 8-bit sRGB/RGBA pixel layout, and multithreading).
  - **Extended Interface (`IJxlExtendedCodec` / `JxlEncodeOptions` / `JxlDecodeOptions`):** Full control over every JPEG XL encoder and decoder setting (VarDCT & Modular modes, progressive encoding/decoding, lossless JPEG bitstream transcoding & `jbrd` reconstruction, HDR tone mapping, custom bit depths, extra channels, and container metadata boxes).

---

## Quick Start — Simple Interface (`IJxlSimpleCodec`)

### 1. Encode Raw Pixels or Files with Default Settings

```csharp
using JxlSharp;

// Use static JxlCodec facade or inject IJxlSimpleCodec (new JxlSharpCodec())
IJxlSimpleCodec codec = JxlCodec.Simple;

// Encode 8-bit RGBA pixels using default quality (90 = distance 1.0) and effort (7 = Squirrel)
byte[] jxlBytes = codec.Encode(rgbaPixels, width: 1920, height: 1080, channels: 4);

// Or encode mathematically lossless (distance = 0.0, Modular mode)
byte[] losslessJxl = codec.EncodeLossless(rgbaPixels, width: 1920, height: 1080, channels: 4);

// Convert an existing image file (.png, .jpg, .ppm, .pfm, .pgx) directly to .jxl
codec.EncodeFile("input.png", "output.jxl", quality: 90.0f, effort: 7);
```

### 2. Decode JPEG XL Images with Default Settings

```csharp
using JxlSharp;
using JxlSharp.Base;

IJxlSimpleCodec codec = JxlCodec.Simple;

// Inspect header info without decoding pixels
JxlBasicInfo info = codec.GetInfo(jxlBytes);

// Decode to interleaved pixels (Width, Height, Channels, Pixels)
JxlImageData image = codec.Decode(jxlBytes);

// Decode a .jxl file and save as .png or .ppm
codec.DecodeToFile("output.jxl", "decoded.png");
```

---

## Advanced Usage — Extended Interface (`IJxlExtendedCodec`)

### 1. Full-Parameter Encoding (`JxlEncodeOptions`)

```csharp
using JxlSharp;
using JxlSharp.Base;
using JxlSharp.Encoder;

IJxlExtendedCodec codec = JxlCodec.Extended;

JxlEncodeOptions encodeOptions = new JxlEncodeOptions
{
    Distance        = 1.0f,              // Butteraugli distance (0.0 = lossless, 1.0 = visually lossless)
    AlphaDistance   = 0.0f,              // Lossless alpha channel
    Effort          = 7,                 // 1 (Lightning) .. 9 (Tortoise), or 10..11 with expert options
    DecodingSpeed   = 0,                 // 0 (highest quality) .. 4 (fastest decoding)
    Progressive     = true,              // Enable progressive AC + DC + responsive squeeze
    Epf             = -1,                // Edge-preserving filter (-1 = auto, 0..3)
    Gaborish        = Override.On,       // Enable Gaborish filter
    Dots            = Override.Default,  // Dots synthesis
    Patches         = Override.Default,  // Patch dictionary synthesis
    PhotonNoiseIso  = 0.0f,              // Simulated film grain ISO
    Container       = Override.On,       // Force ISO BMFF container
    CompressBoxes   = true,              // Brotli-compress metadata boxes (brob)
    NumThreads      = -1,                // Use all logical CPU cores
    Stats           = new JxlEncoderStats(),
};

byte[] encoded = codec.Encode(image, encodeOptions);
```

### 2. Full-Parameter Decoding (`JxlDecodeOptions` & `JxlDecodeResult`)

```csharp
using JxlSharp;
using JxlSharp.Base;

IJxlExtendedCodec codec = JxlCodec.Extended;

JxlDecodeOptions decodeOptions = new JxlDecodeOptions
{
    RequestedChannels          = 4,                   // Force 4-channel RGBA output
    RequestedDataType          = JxlDataType.UInt16,  // Decode 16-bit unsigned samples
    ColorSpace                 = string.Empty,        // Embedded profile or explicit color space string
    DisplayNits                = 0.0f,                // Set > 0 to tone-map HDR images to target nits
    KeepOrientation            = false,               // Apply EXIF/header orientation automatically
    UnpremultiplyAlpha         = true,                // Unpremultiply associated alpha
    RenderSpotcolors           = true,                // Render spot color channels
    Coalescing                 = true,                // Coalesce animation/composite frames
    MaxDownsampling            = 1,                   // Progressive downsampling ratio (1, 2, 4, 8)
    ReconstructJpegIfAvailable = true,                // Reconstruct byte-exact JPEG when jbrd box exists
    NumThreads                 = -1,                  // Use all logical CPU cores
};

JxlDecodeResult result = codec.Decode(encoded, decodeOptions);
JxlImageData? primary  = result.PrimaryImage;
```

---

## Supported Options Summary

| Category | `JxlEncodeOptions` Properties | `JxlDecodeOptions` Properties |
| :--- | :--- | :--- |
| **Quality & Mode** | `Quality`, `Distance`, `AlphaDistance`, `Lossless`, `Modular` | `AcceptedFormats`, `RequestedChannels`, `RequestedDataType`, `BitsPerSample`, `OutputBitdepth` |
| **Speed & Threading** | `Effort` (`1..11`), `DecodingSpeed` (`0..4`), `BrotliEffort`, `NumThreads`, `Buffering`, `AllowExpertOptions` | `NumThreads`, `MaxPasses`, `MaxDownsampling` (`1, 2, 4, 8`), `AllowPartialInput`, `UseImageCallback` |
| **Perceptual & Heuristics** | `Epf` (`-1..3`), `Gaborish`, `Dots`, `Patches`, `Noise`, `PhotonNoiseIso`, `KeepInvisible`, `UseFullImageHeuristics`, `DisablePerceptualOptimizations` | `RenderSpotcolors`, `DisplayNits` |
| **Progressive & Resampling** | `Progressive`, `Responsive`, `ProgressiveAc`, `QProgressiveAc`, `ProgressiveDc`, `GroupOrder`, `CenterX`, `CenterY`, `Resampling`, `ExtraChannelResampling`, `AlreadyDownsampled`, `UpsamplingMode` | `Coalescing`, `KeepOrientation` |
| **Modular Specific** | `ColorTransform`, `ModularColorSpace`, `ModularGroupSize`, `ModularPredictor`, `ModularMaTreeLearningPercent`, `ModularNbPrevChannels`, `PaletteColors`, `LossyPalette`, `ChannelColorsGlobalPercent`, `ChannelColorsGroupPercent` | — |
| **Container, Metadata & JPEG** | `Container`, `CompressBoxes`, `FrameIndexBox`, `LosslessJpegTranscode`, `JpegStoreMetadata`, `JpegReconCfl`, `JpegCompressBoxes`, `JpegKeepExif`, `JpegKeepXmp`, `JpegKeepJumbf` | `ReconstructJpegIfAvailable`, `ColorSpace`, `ColorSpaceForCmyk`, `UnpremultiplyAlpha`, `AlphaBlendBackground`, `SizeConstraints` |

---

## License

This package is licensed under the SPDX license expression:
**`BSD-3-Clause AND MIT`**

- **`BSD-3-Clause`**: [libjxl](https://github.com/libjxl/libjxl) (Copyright (c) the JPEG XL Project Authors) and [skcms](https://skia.googlesource.com/skcms) (Copyright (c) Google LLC).
- **`MIT`**: `Xorshift128Plus` PRNG component.
- Highway math routines (`FastMath`) are dual-licensed under `Apache-2.0 OR BSD-3-Clause` and consumed under `BSD-3-Clause`.

**Author**: Hefaistos68  
**Contact**: dev@hefaistos68.dev  
**Repository**: [https://github.com/Hefaistos68](https://github.com/Hefaistos68)

