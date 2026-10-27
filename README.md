# JxlSharp

**JxlSharp** is a pure C# / .NET 10 implementation of the **JPEG XL (`libjxl` v0.13.0)** reference image codec, a complete encoder, decoder, color management system (CMS), multithreaded runner, and external format codecs, plus a windows forms application to demonstrate the usage of the library, and a command line tools to use the codecs from the command line.

It is feature complete and matches the C++ `libjxl` library outputs. It is by no means a equal performant implementation. There is still a lot of improving to do. Any help is welcome.

- **Author:** Hefaistos68
- **Email:** dev@hefaistos68.dev
- **Repository:** [https://github.com/Hefaistos68/jxlsharp](https://github.com/Hefaistos68/jxlsharp)
- **License:** BSD-3-Clause

---

## Features

- **100% Managed .NET 10:** No native C/C++ runtime binaries required; SIMD-accelerated (`Vector256<float>`, AVX2/FMA).
- **Two-Tier API Design:**
  - **Simple Interface (`IJxlSimpleCodec` / `JxlCodec`):** One-call encoding and decoding with the most common parameter defaults (visually lossless quality `90` / Butteraugli distance `1.0`, effort `7` Squirrel, automatic 8-bit sRGB/RGBA pixel layout, and multithreading).
  - **Extended Interface (`IJxlExtendedCodec` / `JxlEncodeOptions` / `JxlDecodeOptions`):** Full control over every JPEG XL encoder and decoder setting (VarDCT & Modular modes, progressive encoding/decoding, lossless JPEG bitstream transcoding & `jbrd` reconstruction, HDR tone mapping, custom bit depths, extra channels, and container metadata boxes).

---

## Windows Apps

The repository includes a Windows desktop application along with standalone command-line tools:

### 1. Windows Desktop Application ([`JxlSharp.WinForms`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.WinForms))
A Windows Forms application providing an interactive GUI for exploring, inspecting, and converting JPEG XL images:
- **Interactive Split Preview:** Side-by-side split visual comparison between reference/source images and encoded/converted results via [SplitPreviewPictureBox](https://github.com/Hefaistos68/jxlsharp/blob/main/src/JxlSharp.WinForms/SplitPreviewPictureBox.cs), featuring a draggable comparison divider and real-time zoom/pan navigation.
- **Detailed Metadata & Property Inspector:** Extracts and visualizes codestream headers, pixel dimensions, color management details (ICC profiles and NCLX transfer functions), extra channels, animation frames, and ISO BMFF container boxes (`brob`, `Exif`, `xml `, `jbrd`) using [JxlPropertyInspector](https://github.com/Hefaistos68/jxlsharp/blob/main/src/JxlSharp.WinForms/JxlPropertyInspector.cs).
- **Interactive Encoding & Decoding:** Dedicated tabs exposing compression parameters (Butteraugli distance/quality, effort, Modular vs VarDCT mode, lossless JPEG transcoding) and decompression options (color space conversion, display nits tone-mapping, downsampling passes).
- **Container Box Manipulation:** Manage, inject, and extract ISO BMFF metadata boxes without re-encoding image pixel data.
- **Integrated CLI Dispatcher:** Supports console launching via `JxlSharp.WinForms.exe --cli <cjxl|djxl|jxlinfo|jxltran> [options...]` to dispatch commands directly to the embedded tools.

### 2. Standalone Command-Line Tools
- **[`cjxl`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.Cli.Cjxl):** JPEG XL encoder / compressor supporting lossless JPEG transcoding, VarDCT/Modular encoding, progressive passes, and ISO BMFF box embedding.
- **[`djxl`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.Cli.Djxl):** JPEG XL decoder / decompressor supporting bit-exact JPEG reconstruction from `jbrd` boxes, color space conversion, tone-mapping, and multi-format exports (.png, .jpg, .ppm, .pfm, .pgx).
- **[`jxlinfo`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.Cli.JxlInfo):** Diagnostic tool outputting codestream metadata, basic image properties, color encodings, frame sequences, and box hierarchies.
- **[`jxltran`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.Cli.JxlTran):** Lossless JPEG XL container manipulation utility for adding, extracting, removing, or packing container metadata boxes.
- **[`benchmark_xl`](https://github.com/Hefaistos68/jxlsharp/tree/main/src/JxlSharp.Cli.BenchmarkXl):** Codec benchmarking suite measuring compression performance, encoding/decoding throughput, and perceptual image quality metrics (Butteraugli, SSIMULACRA).

---

## JXLSharp nuget package 

The JXLSharp repository contains a nuget package with two interfaces, a simple and an advanced one. The simple interface lets you encode and decode images with default settings, while the advanced interface gives you full control over all custom settings. Please refer to the example code in the `JxlSharp.WinForms` project for more information.

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
**Repository**: [https://github.com/Hefaistos68/jxlsharp](https://github.com/Hefaistos68/jxlsharp)

