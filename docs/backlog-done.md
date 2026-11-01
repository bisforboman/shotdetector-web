

- ManagedCodecs 0.5.0
  - Why: it gives each `VideoFrame`'s colour tags and sample aspect ratio, `VideoStreamInfo`'s colour, 'pasp' and
    rotation, the demuxers' seeking and the streams' durations, and fixes ManagedCodecs#252 (a track's first pts when
    its edit list starts at 0), which the next two rows build on.
  - Where: Directory.Packages.props (every ManagedCodecs package), README "Packages", CLAUDE.md. What 0.5.0 now exposes
    that `Mp4Header`/`SpsInfo`/`AviHeader` read themselves may replace them only where NativeCheck's field-by-field
    comparison stays exact.
  - Done when: `tools/check.sh native`, `wasm` and `wasm aot` pass, app-check.mjs's `Versions()` the pins.
  - Not: each frame's colour tags (the next row), the Caminandes row.
  - Status: Done (2026-11-01: natively every check agrees on every sample, the CSVs, JSON, HTML, packets, frame types,
    picture and audio unchanged; CI's AOT check with the page green, `Versions()` the pins. `Mp4Header`, `SpsInfo`
    and `AviHeader` stay: what 0.5.0 exposes of theirs the next rows use where the checks prove it)
