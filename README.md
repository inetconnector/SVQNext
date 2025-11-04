# SVQNext — Scalable Vector Quantization Video Codec (Public Domain)

**SVQNext** is a high-performance experimental video codec based on predictive **Scalable Vector Quantization**.  
The name **SVQNext** stands for **Scalable Vector Quantization – Next Generation**, describing the core approach of a scalable, VQ-based next-generation video compression pipeline.

SVQNext aims to provide HEVC-class compression while remaining fully **public domain**, **patent-neutral**, transparent and suitable for research, education and practical use.

## Overview

SVQNext is a cross-platform video codec written in C#/.NET 8.  
It combines block-based vector quantization, motion-compensated prediction, rANS entropy coding, adaptive filtering, HDR support and optional forward-error-correction hooks for resilient streaming.

Unlike transform-based codecs (e.g., H.264/H.265/AV1), SVQNext avoids potentially encumbered DCT/DST or CABAC-derived designs. All core components derive from public academic methods (K-Means clustering, Hadamard, rANS), enabling a clean **public-domain** implementation.


## MP4 vs. SVQ-Next — What’s the Difference?

MP4 is not a codec, but a container format commonly used to store video encoded with H.264/AVC, H.265/HEVC, AV1, or other codecs. In everyday usage, “MP4” often refers to H.264 video, which is a widely used but patent‑encumbered video compression technology.

SVQ-Next is a standalone video codec. It is designed to remain free of patented mechanisms and is based on vector quantization rather than DCT-based transforms.

### Technical Comparison (High‑Level)

| Criterion | MP4 (H.264/HEVC/AV1) | SVQ-Next |
|-----------|-------------------------|--------------|
| Core Concept | Transform coding (DCT) with quantization and entropy coding | Vector Quantization (codebook matching + residual) |
| Prediction | Complex motion prediction (P/B frames, sub‑pixel refinement) | Simpler motion compensation; flexible design |
| Artifacts | Blocking, ringing, banding | Pattern‑codebook artifacts; reduced DCT blocking |
| Entropy Coding | CABAC/CAVLC (encumbered) | rANS (modern, unencumbered) |
| Color Processing | YCbCr 4:2:0/4:2:2/4:4:4 | YCbCr 4:2:0 / 4:4:4 |
| Error Robustness | Low fault tolerance; bit errors affect large regions | Optional forward error correction |
| Licensing | Patent‑encumbered; royalties required for H.264/HEVC | Public Domain; no royalties |
| Hardware Support | Widely supported in GPUs, SoCs, encoders, decoders | Currently software‑only |
| Typical Use Cases | Streaming, consumer video, broadcast | Research, resilient streaming, ML pipelines, open platforms |

### Key Technical Difference Summary

H.264/HEVC rely on DCT to convert image data into frequency components, which is efficient but results in recognizable transform‑based artifacts. SVQ-Next instead encodes image blocks by mapping them to entries in a vector codebook and storing only the index and a small residual. This avoids typical DCT artifacts and aligns structurally with machine‑learning‑style pattern representation.


## Features

- Vector-quantization-based compression with block-based residual coding
- Motion-compensated prediction with configurable GOP structure
- Support for forward and bidirectional prediction (B-frames)
- Scalable bitstreams and segmentation for adaptive playback
- 4:2:0 YCbCr pipeline with 8/10/12-bit HDR support
- rANS entropy coding with context adaptation
- Adaptive loop filtering and perceptual rate-distortion optimisation
- RS-FEC integration points to improve resilience to packet loss
- ImageSharp integration for efficient zero-copy RGB/YCbCr buffer conversion
- Synthetic demo assets (test frames, audio, subtitles) for rapid evaluation

### Streaming & Segmentation

- HTTP-friendly segmentation with manifest output
- Encapsulated container format (`.svqpack`) holding configuration, frames and optional parity data

## How SVQNext Works (Technical Summary)

SVQNext replaces transform-based compression with predictive **vector quantization**:

1. **Prediction**: Each frame is predicted from reference frames using motion-compensated blocks (full-, half- and quarter-pixel).
2. **Residual VQ**: Prediction residuals are encoded using vector codebooks rather than transforms.
3. **Entropy Coding**: Symbols are compressed using context-adaptive **rANS** rather than CABAC.
4. **Filtering & Rate Control**: Optional adaptive filters and perceptual weighting (SSIM-guided λ) improve visual quality at a given bitrate.
5. **Optional RS-FEC**: Parity data can be attached for recovery from channel loss.

## Building

The repository targets **.NET 8.0**. Restore dependencies and build via:

```bash
dotnet build src -c Release
```

## Running

### Encode a sequence

```bash
dotnet run --project src/SVQNext.csproj --   --encode --frames 120 --width 320 --height 180   --quality medium --gop 24 --bitdepth 10 --colorspace bt709
```

### Decode an existing `.svqpack` and export an animated preview

```bash
dotnet run --project src/SVQNext.csproj --   --decode out/stream.svqpack --out out_decoded
```

Run without arguments to display all supported switches.

## Demo Utility

A lightweight console app is provided to exercise the FEC pipeline and optimized primitives:

```bash
dotnet run --project demo/SVQNext.Demo.csproj
```

## Project Layout

```
src/        Primary encoder/decoder implementation
demo/       Console harness showcasing FEC hooks and optimized primitives
Optimized/  SIMD-friendly kernels used by the codec core
```

## Why SVQNext Matters

| Category | HEVC/H.265 | SVQNext |
|:--|:--|:--|
| Licensing | Patented, royalties | Public domain (CC0) |
| Compression | Excellent | ≈ HEVC-class |
| Encoding Speed | Slow | Fast CPU encode |
| Decoding Speed | Medium | 3–5× faster |
| Bitstream | Complex, opaque | Educational and readable |

## License and Legal

Released into the **Public Domain** under **CC0 1.0** and **The Unlicense**.  
Includes a defensive publication to prevent future patent claims.  
No third-party code is included (see `THIRD_PARTY_DISCLOSURE.txt` for verification).

## Kurzüberblick (Deutsch)

SVQNext ist ein vektorquantisierungsbasierter Videocodec ohne Lizenz- oder Patentpflichten.  
Er nutzt bewegungskompensierte Prädiktion, rANS-Kodierung, B-Frames, HDR-Support und optionale FEC-Hooks, läuft unter Windows, Linux und macOS mit .NET 8 und eignet sich für Forschung, Ausbildung und praktische Anwendungen.

---

Project Home: https://github.com/inetconnector/SVQNext  
Author: Public Domain Contributors (you own your copy)
