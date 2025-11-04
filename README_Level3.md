
# SVQNext v10 — Level 3 Optimized

- Basis: v7 (Master) + Funktionen aus v9 integriert (FEC, ImageSharp statt System.Drawing).
- Optimierung: SIMD/Span (Vector<T>, ProcessPixelRows), unsafe erlaubt, GC-Reduktion.
- Projekte:
  - `src/` — Hauptprojekt (Bibliothek/Codec)
  - `demo/` — Konsolen-Demo

## Build
```
dotnet build SVQNext_v10.sln -c Release
dotnet run --project demo/SVQNext.Demo.csproj -c Release
```

## Hinweise
- `Optimized/CodecCoreOptimized.cs` bietet schnelle Primitive (SAD, XOR, Copy).
- `Optimized/ImageFastOps.cs` kapselt schnelle ImageSharp-Row-Accesses.
- `Pipeline/FecPipelineHook.cs` bindet FEC (Writer/Recover) dynamisch ein, falls vorhanden.
- `SystemDrawingAliases.cs` + `ImageSharpCompatExtensions.cs` stellen Kompatibilität sicher.
