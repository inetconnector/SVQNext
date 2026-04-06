# SVQNext — Scalable Vector Quantization Video Codec (AGPLv3)

**SVQNext** is an experimental video codec based on predictive **Scalable Vector Quantization**.  
The name **SVQNext** stands for **Scalable Vector Quantization – Next Generation**, describing the core approach of a scalable, VQ-based video compression pipeline.

> **Project status**: Active research prototype. No stable release yet and all large performance claims remain targets, not validated production benchmarks. The repository does, however, include measured smoke-test comparisons for the current prototype.

SVQNext aims to eventually reach HEVC-class compression while remaining fully **free software** under the **GNU AGPLv3**, **patent-neutral in project intent**, transparent and suitable for research, education and practical use. Current builds should be treated as a laboratory proof-of-concept that still requires extensive optimisation, validation and peer review.

This README is stylistically based on the earlier Git revision `ccc41eca5b9936449238e663c23aa87c362f7608` from `2025-11-05 20:07:16 +0100`, then minimally updated to reflect the current prototype status.

## Overview

SVQNext is a cross-platform video codec written in C#/.NET 8.  
It currently combines block-based vector quantization ideas, motion-compensated prediction, a Hadamard-based residual path, adaptive filtering, HDR support and optional forward-error-correction hooks for resilient streaming.

Unlike mainstream transform codecs (e.g., H.264/H.265/AV1), SVQNext is intentionally developed without CABAC-derived syntax or patent-fee-bearing third-party codec components. The current prototype uses public, open building blocks such as K-Means clustering, Hadamard transforms and simple packed chunk compression, enabling a clean **free-software** implementation.

## MP4 vs. SVQ-Next — What’s the Difference?

MP4 is not a codec, but a container format commonly used to store video encoded with H.264/AVC, H.265/HEVC, AV1, or other codecs. In everyday usage, “MP4” often refers to H.264 video, which is a widely used but patent-encumbered video compression technology.

SVQ-Next is a standalone video codec. It is designed to remain free of patented mechanisms and is based on vector quantization and lightweight open transform coding rather than DCT/CABAC-style mainstream designs.

### Technical Comparison (High-Level)

| Criterion | MP4 (H.264/HEVC/AV1) | SVQ-Next |
|-----------|-------------------------|--------------|
| Core Concept | Transform coding (DCT) with quantization and entropy coding | Hybrid VQ + residual coding |
| Prediction | Complex motion prediction (P/B frames, sub-pixel refinement) | Simpler motion compensation; flexible design |
| Artifacts | Blocking, ringing, banding | Pattern/codebook artifacts; lighter transform artifacts |
| Entropy Coding | CABAC/CAVLC (encumbered) | Packed royalty-free chunk coding in current prototype |
| Color Processing | YCbCr 4:2:0/4:2:2/4:4:4 | YCbCr 4:2:0 pipeline, HDR-capable prototype |
| Error Robustness | Low fault tolerance; bit errors affect large regions | Optional forward error correction hooks |
| Licensing | Patent-encumbered; royalties required for H.264/HEVC | GNU AGPLv3; SVQNext itself aims to avoid royalties |
| Hardware Support | Widely supported in GPUs, SoCs, encoders, decoders | Currently software-only |
| Typical Use Cases | Streaming, consumer video, broadcast | Research, resilient streaming, ML pipelines, open platforms |

### Key Technical Difference Summary

H.264/HEVC rely on transform-heavy coding and highly specialized entropy syntax, which is efficient but tied to widely encumbered standards. SVQ-Next instead explores a structurally simpler free-software path built around motion prediction, vector-quantization concepts and a lightweight Hadamard residual stage. This avoids typical CABAC/DCT-style design dependence and aligns more naturally with research-oriented pattern representation.

## Features

- Vector-quantization-based compression ideas with block-based residual coding
- Motion-compensated prediction with configurable GOP structure
- Experimental forward and bidirectional prediction hooks (research status)
- Scalable bitstream and segmentation hooks for adaptive playback
- 4:2:0 YCbCr pipeline with 8/10/12-bit HDR support
- Hybrid residual path with Hadamard transform + rate/distortion mode selection
- Adaptive loop filtering and perceptual rate-distortion optimisation
- RS-FEC integration points to improve resilience to packet loss
- ImageSharp integration for efficient RGB/YCbCr buffer conversion
- Synthetic demo assets and comparison harness for rapid evaluation

### Streaming & Segmentation

- HTTP-friendly segmentation hooks
- Encapsulated container format (`.svqpack`) holding configuration, frames and optional parity data

## How SVQNext Works (Technical Summary)

SVQNext currently replaces mainstream transform syntax with a hybrid predictive path:

1. **Prediction**: Each frame is predicted from reference frames using motion-compensated blocks.
2. **Residual Coding**: Prediction residuals are currently coded mainly through a Hadamard residual path, while older gain-shape/VQ pieces remain in the codebase for compatibility and research.
3. **Mode Selection**: Blocks are evaluated across skip, inter, intra and split variants with simple rate/distortion decisions.
4. **Bitstream Packing**: Motion fields, syntax flags and sparse transform coefficients are packed into `.svqpack`.
5. **Filtering & Rate Control**: Optional adaptive filters and perceptual weighting improve visual quality at a given bitrate.
6. **Optional RS-FEC**: Parity data can be attached for recovery from channel loss.

## Building

The repository targets **.NET 8.0**. A verified local build for the current prototype is:

```bash
dotnet build demo/SVQNext.Demo.csproj -c Debug
```

The command-line codec project under `src/` is also present for direct encode/decode experiments.

## Running

### Encode a sequence

```bash
dotnet run --project src/SVQNext.csproj -- --encode --frames 120 --width 320 --height 180 --quality medium --gop 24 --bitdepth 10 --colorspace bt709
```

### Decode an existing `.svqpack` and export an animated preview

```bash
dotnet run --project src/SVQNext.csproj -- --decode out/stream.svqpack --out out_decoded
```

Run without arguments to display all supported switches.

## Demo Utility

A lightweight console app is provided to exercise the current end-to-end encode/decode/benchmark pipeline:

```bash
dotnet run --project demo/SVQNext.Demo.csproj
```

Optional H.264 comparison via an external FFmpeg build:

```bash
dotnet run --project demo/SVQNext.Demo.csproj -- --ffmpeg "C:\path\to\ffmpeg.exe"
```

## Project Layout

```text
src/        Primary encoder/decoder implementation
demo/       Console harness for roundtrip and benchmark runs
scripts/    Helper scripts, including FFmpeg Windows build wrappers
external/   External benchmark checkouts/build outputs
out/        Generated reports and demo artefacts
```

## Why SVQNext Matters

The motivation for SVQNext is to explore a free-software, patent-neutral alternative to mainstream codecs. The table below lists the intended direction of travel for the project rather than guaranteed properties of the current prototype.

| Category | HEVC/H.265 | SVQNext (goal) |
|:--|:--|:--|
| Licensing | Patented, royalties | GNU AGPLv3 |
| Compression | Excellent | Targeting comparable quality |
| Encoding Speed | Slow | Targeting faster CPU encode |
| Decoding Speed | Medium | Targeting faster CPU decode |
| Bitstream | Complex, opaque | Educational and readable |

## License and Legal

Distributed under the terms of the **GNU Affero General Public License v3.0**. See the `LICENSE` file for details. Earlier drafts referenced CC0/Public Domain; this repository is now exclusively AGPLv3-licensed to avoid ambiguity. If you require clarification, please open an issue or contact the maintainers.

The project goal is that the **SVQNext codec path itself** remains free of royalty-bearing third-party codec components. External tools and benchmark checkouts under `external/` keep their own upstream licenses and are not linked into SVQNext itself.

## Validation & Next Steps

SVQNext is still under heavy development, but the repo now does include reproducible comparison reports for the current synthetic smoke-test setup. The latest measured reports at the time of writing are:

- `out/codec_demo_quadtree_medium/comparison_report.txt` — created `2026-04-07 00:52:34 +02:00`
- `out/codec_demo_quadtree_ultra/comparison_report.txt` — created `2026-04-07 00:52:49 +02:00`

Current synthetic comparison snapshot (`24` frames at `160x90`):

1. `medium`: SVQNext `43.65 KB`, `29.25 dB PSNR`, `0.9891 SSIM`; H.264 `11.89 KB`, `31.82 dB PSNR`, `0.9947 SSIM`
2. `ultra`: SVQNext `45.28 KB`, `29.49 dB PSNR`, `0.9903 SSIM`; H.264 `11.82 KB`, `31.82 dB PSNR`, `0.9947 SSIM`

That means the prototype is working end-to-end, but is still clearly behind H.264 on the present benchmark. The current engineering focus is:

1. Better partitioning and block decisions
2. Stronger intra/inter modelling on real clips
3. More efficient coefficient coding
4. Specialized royalty-free modes for screen, synthetic and anime-like content

Community contributions toward benchmarking scripts, dataset curation and reproducible measurement are especially welcome.

## Kurzüberblick (Deutsch)

SVQNext ist ein experimenteller Videocodec ohne Lizenz- oder Patentpflichten im Zielbild des Projekts.  
Der aktuelle Prototyp nutzt bewegungskompensierte Prädiktion, mehrere Intra-Modi, Skip-/Split-Entscheidungen, einen Hadamard-basierten Residualpfad, HDR-Support und optionale FEC-Hooks. Vergleiche gegen H.264 laufen über externes FFmpeg nur als Benchmark, nicht als Teil des Codecs selbst.

---

Project Home: https://github.com/inetconnector/SVQNext  
Author: SVQNext Contributors (licensed under AGPLv3)
