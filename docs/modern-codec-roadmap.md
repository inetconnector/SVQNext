# Modern Hybrid Codec Roadmap

This document turns the current "how do we move toward H.264/AV1-class architecture?" discussion into a repo-specific execution plan.

## Goal

Evolve SVQNext from an experimental hybrid codec into a cleaner modern block-based hybrid codec while keeping the implementation royalty-free and fully open-source.

Important constraint:

- no patented H.264/HEVC components are to be copied into SVQNext itself
- FFmpeg/H.264 remains an external benchmark only

## Phase 1: Syntax And Partition Foundation

Objective:

- separate block syntax into distinct concepts instead of overloading a single mode byte
- create a foundation for future quadtree-like partitioning and richer prediction search

Work items:

1. Separate block syntax into:
   - prediction class (`skip`, `inter`, `intra`)
   - partition mode (`full`, `split`)
   - intra predictor (`dc`, `vertical`, `horizontal`, `planar`, `diagonal`, later more)
2. Keep backward-compatible translation to the current legacy block mode representation while migrating the pipeline.
3. Move the bitstream toward context-friendly, tightly packed syntax fields.
4. Keep decoder behavior deterministic and compatible with current roundtrip paths.

Expected outcome:

- cleaner codec internals
- easier extension to more split types and richer intra decisions
- less coupling between block meaning and storage format

## Phase 2: Stronger Inter Architecture

Objective:

- make the inter path behave more like a modern hybrid codec rather than a simple previous-frame predictor

Work items:

1. Add better merge/skip candidate modeling.
2. Introduce multiple references where practical.
3. Improve motion-vector signaling and candidate reuse.
4. Add more stable scene-cut / refresh logic based on actual coding benefit.
5. Prepare for larger coding tree units with recursive subdivision.

Expected outcome:

- lower residual energy
- better bitrate efficiency at similar visual quality
- stronger base for real-world video clips

## Phase 3: Residual And Reconstruction Modernization

Objective:

- make transformed residuals and reconstruction filters significantly more efficient

Work items:

1. Improve transform coefficient ordering and sparse coding.
2. Add stronger pruning and adaptive quantization decisions.
3. Introduce better in-loop filtering / restoration.
4. Tune RDO against actual bitstream cost, not just proxy estimates.
5. Validate against a broader clip suite, especially real video and synthetic/screen content separately.

Expected outcome:

- reduced bitrate overhead from residual syntax
- higher quality at equivalent size
- realistic path to beating H.264 on selected content classes

## Current Focus

Phase 1 has started in code by separating prediction syntax from the older overloaded residual mode representation.
