# SVQNext — Scalable Vector Quantization Video Codec (AGPLv3)

**SVQNext** is an experimental, fully software video codec written in C#/.NET 8.
It explores a royalty-free codec design centered on motion-compensated prediction, vector-quantization ideas and a lightweight transform/residual path.

> **Project status**: active research prototype. Current results are measured smoke-test benchmarks, not production claims.

## Current State

SVQNext currently includes:

- motion-compensated inter prediction with merge-like neighbor reuse
- adaptive refresh frames / simple scene-refresh logic
- 4:2:0 YCbCr pipeline with predictive chroma coding
- hybrid residual coding:
  - legacy gain-shape VQ path retained for compatibility
  - current main path uses Hadamard residual transform + rate/distortion mode selection
- block mode decisions between skip, inter, intra and split variants
- intra prediction modes including DC, vertical, horizontal, planar and diagonal
- packed `.svqpack` container chunks with royalty-free compression
- objective demo benchmarking against H.264 through external FFmpeg

The codec is still clearly behind H.264 on the current synthetic benchmark, but it now roundtrips end-to-end and produces reproducible reports.

## Licensing

The project goal is that **SVQNext itself remains license-free in the royalty sense** and distributed as **free software** under the **GNU AGPLv3**.

Important distinction:

- `SVQNext` codec code in this repository is intended to stay free of patent-fee-bearing third-party codec components
- `FFmpeg` is used here only as an external benchmark/reference tool
- no `libx264` or patented H.264 encoder is linked into SVQNext itself

This repository currently uses only project code plus general-purpose open algorithms and .NET libraries for the codec path.

## Architecture Summary

At a high level, the current pipeline is:

1. RGB input is converted to YCbCr 4:2:0.
2. Luma is predicted from previous frames using motion compensation.
3. Inter blocks may be skipped, merged from neighbors or coded normally.
4. Each block is evaluated with a simple RDO pass across:
   - inter full block
   - inter split block
   - intra full block
   - intra split block
5. Residuals are transformed with a Hadamard transform and quantized.
6. Modes, motion data and sparse transform coefficients are packed into `.svqpack`.
7. Chroma is coded either absolutely or as motion-predicted residuals.
8. Optional loop filters are applied after reconstruction.

## H.264 Comparison Status

The repo now contains a working comparison flow against a locally built Windows FFmpeg from the official upstream source tree.

Latest measured synthetic smoke-test results:

### Medium-style profile

Source: `24` synthetic frames at `160x90`

- SVQNext: `45.73 KB`, `29.39 dB PSNR`, `0.9896 SSIM`
- H.264 (`ffmpeg` / `h264_mf`): `11.82 KB`, `31.82 dB PSNR`, `0.9947 SSIM`

Report:

- [`out/codec_demo_next4_medium/comparison_report.txt`](out/codec_demo_next4_medium/comparison_report.txt)

### Aggressive profile

Source: `24` synthetic frames at `160x90`

- SVQNext: `42.88 KB`, `29.08 dB PSNR`, `0.9888 SSIM`
- H.264 (`ffmpeg` / `h264_mf`): `11.89 KB`, `31.82 dB PSNR`, `0.9947 SSIM`

Report:

- [`out/codec_demo_next4_ultra/comparison_report.txt`](out/codec_demo_next4_ultra/comparison_report.txt)

Interpretation:

- SVQNext is functioning correctly
- SVQNext is not yet H.264-equivalent on this benchmark
- current work is focused on closing the bitrate-efficiency gap while keeping the codec path royalty-free

## Build

The repository targets **.NET 8**.

Basic build:

```powershell
dotnet build demo\SVQNext.Demo.csproj -c Debug
```

If you use a local sandboxed setup, the repository also supports custom local package/cache directories via environment variables as used during the smoke tests.

## Demo / Benchmark Utility

The demo app runs a complete encode/decode/compare cycle:

- generate or load frames
- encode with SVQNext
- decode and reconstruct
- compute PSNR and SSIM
- optionally encode the same source with H.264 through FFmpeg
- write previews, frame sequences, the `.svqpack` stream and a text report

Basic run:

```powershell
dotnet run --project demo\SVQNext.Demo.csproj
```

Run with explicit FFmpeg path:

```powershell
dotnet run --project demo\SVQNext.Demo.csproj -- --ffmpeg "C:\path\to\ffmpeg.exe"
```

Run against an existing frame directory:

```powershell
dotnet run --project demo\SVQNext.Demo.csproj -- --input-dir sample_frames --ffmpeg "C:\path\to\ffmpeg.exe" --out out\codec_demo_realclip
```

## FFmpeg Reference Build

This repository includes helper scripts to build a native Windows FFmpeg from the official upstream checkout:

- [`scripts/ffmpeg-msvc-configure.cmd`](scripts/ffmpeg-msvc-configure.cmd)
- [`scripts/ffmpeg-msvc-build.cmd`](scripts/ffmpeg-msvc-build.cmd)

The benchmark setup in this repo uses FFmpeg only as an external reference encoder/decoder. It is not part of the SVQNext codec implementation.

## Project Layout

```text
src/       Core codec implementation
demo/      End-to-end demo and benchmark harness
scripts/   Build helpers, including FFmpeg Windows build wrappers
external/  External checkouts/build outputs used for benchmarking
out/       Generated demo outputs and comparison reports
```

## What Is Outdated From Earlier Descriptions

Older project descriptions in this repo and earlier discussions referenced pieces that are no longer accurate as the primary path:

- the current chunk compression path is not `rANS`; it is a simpler packed container path with royalty-free chunk compression
- the current main residual path is not pure VQ-only anymore; it is hybrid and presently driven mainly by transform-coded residuals
- B-frames and advanced scalable streaming should be treated as research direction, not as a finished validated feature set

## Next Engineering Targets

The main planned steps toward better H.264 competitiveness are:

1. stronger coefficient pruning and better sparse coding
2. improved intra prediction and block partition decisions
3. better inter/reference modeling on real clips, not just synthetic scenes
4. optional specialized path for screen/anime/synthetic content where a royalty-free codec has a better chance to beat H.264

## License and Legal

Distributed under the terms of the **GNU Affero General Public License v3.0**.
See [`LICENSE`](LICENSE) for details.

If you need exact legal clarification:

- SVQNext repository code: AGPLv3
- external FFmpeg checkout/build artifacts: licensed under FFmpeg's own upstream terms
- benchmark comparison with H.264 does **not** change the licensing of SVQNext itself

## Kurzüberblick (Deutsch)

SVQNext ist aktuell ein experimenteller, lizenzfreier Videocodec-Forschungsprototyp in C#/.NET 8.
Er nutzt Bewegungskompensation, Skip-/Merge-artige Inter-Modi, mehrere Intra-Modi, prädiktive Chroma-Codierung und einen Hadamard-basierten Residualpfad mit RDO.

Der Codec funktioniert inzwischen end-to-end inklusive Vergleich gegen H.264 über externes FFmpeg, liegt auf dem aktuellen Testclip aber noch sichtbar hinter H.264.

---

Project Home: https://github.com/inetconnector/SVQNext
Author: SVQNext Contributors
