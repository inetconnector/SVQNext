# SVQNext

SVQNext is a high-performance experimental video codec pipeline that demonstrates
motion-compensated encoding, scalable bitstreams, forward error correction hooks
and ImageSharp-based image processing.

## Features
- Encoder/decoder that operates on float RGB buffers with configurable GOP
  structure, block size and motion search parameters.
- ImageSharp integration for zero-copy conversions between textures and codec
  buffers.
- Optional demo assets such as synthetic video frames, generated audio and
  subtitles.
- Segmenter that writes HTTP-friendly chunks alongside a manifest.
- Forward error correction integration points to tolerate packet loss.

## Building
The repository targets .NET 8.0. Restore dependencies and compile via:

```
dotnet build SVQNext_v10.sln -c Release
```

## Running
Generate a demo encode and streaming segments:

```
dotnet run --project src/SVQNext.csproj -- \
  --encode --frames 120 --width 320 --height 180 --quality medium
```

Decode an existing `.svqpack` container and export an animated GIF preview:

```
dotnet run --project src/SVQNext.csproj -- \
  --decode out/stream.svqpack --out out_decoded
```

Run the program without arguments to see the list of supported switches.

## Demo utility
The `demo` folder contains a lightweight console app that exercises the FEC
pipeline and optimized primitives:

```
dotnet run --project demo/SVQNext.Demo.csproj
```

## Project layout
- `src/` – primary encoder/decoder implementation.
- `demo/` – small harness showcasing optimized primitives and FEC hooks.
- `Optimized/` – SIMD friendly kernels used by the codec core.

## License
The code is released into the public domain.
