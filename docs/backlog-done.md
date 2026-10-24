# Backlog: done

Rows finished, moved from [backlog.md](backlog.md), newest last.

- Core library and the native check: ManagedCodecs + FastYuv against the FFmpeg path
  - Why: exactness first; before any UI, the scene and stats CSVs must equal ShotDetector's FFmpeg path's.
  - Status: Done (2026-10-24: every sample, all five detectors and all together, byte for byte; tools/NativeCheck)

- WebAssembly check, interpreted and AOT
  - Why: the same results on browser-wasm, and nothing of FFmpeg on the path.
  - Status: Done (2026-10-24: tools/WasmCheck under Node; FFmpeg.AutoGen never loaded interpreted, never loaded by
    the runs under AOT)

- Repository and CI
  - Why: the showcase lives in its own repository with its own CI (shotdetector's decision 2026-10-24).
  - Status: Done (2026-10-24: handed off from shotdetector's `handoff/shotdetector-web` branch; the first CI run on
    `main` green, 9m41s)
