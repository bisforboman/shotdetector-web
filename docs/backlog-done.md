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

- Packages from nuget.org
  - Why: ShotDetector and ShotDetector.FastYuv 3.14.0 are on nuget.org (released 2026-10-24), so the local feed was a
    workaround for a problem that is gone.
  - Status: Done (2026-10-24: the local-feed source, CI's packing step and SHOTDETECTOR_REF dropped; a restore into
    an empty global packages folder takes both from nuget.org)

- The app project: one browser-wasm project with the page's exports, replacing WasmCheck
  - Why: the page needs a browser-wasm project with `[JSExport]`; one project means the exactness check runs the
    page's own entry points (decision "One wasm project").
  - Status: Done (2026-10-24: `src/ShotDetectorWeb.App` with `Open`, `Detect`, `LoadedAssemblies` and `progress`,
    `wwwroot/worker.js`; `tools/app-check.mjs` under Node, 108 of 108 CSVs identical interpreted and AOT; FastYuv's
    `.wasm` and license checked in the publish output; tools/WasmCheck deleted)

- The Sintel trailer sample: a real trailer in the checks, re-encoded as the demo will be
  - Why: a real trailer has what the synthetic samples isolate, together; the page row checks against it, and the
    demo clip is this same re-encoded file, so the exactness check covers what visitors run.
  - Status: Done (2026-10-24: `sintel_trailer-1080p.mp4` from download.blender.org, pinned by SHA-256 in `ci.yml`,
    re-encoded by `tools/make-samples.sh` to 854x480 at 24 fps, 10.1 MB, ffprobe-verified; native and both wasm checks
    equal on it, 120 of 120 CSVs. CI 26m15s, the interpreted wasm step 18m19s of it, the trailer's six runs about
    two minutes each there)

- The page: pick an MP4, detect, see and download the shots
  - Why: the showcase is the page; everything else is around it.
  - Status: Done (2026-10-24: `wwwroot/index.html`, `app.js`, `style.css`; `Api` prefixes a damaged file's message
    (decision "A damaged file's message"). Checked in Chromium here (interpreted) and, through a one-off CI step since
    reverted, in Chromium and Firefox on the AOT publish: every sample with all five detectors gives the native
    check's scenes and stats CSVs, Sintel's trailer included (60 to 77 frames per second); progress moves; cancel
    mid-run, then a second run; each file in `samples/reject/`, a damaged and a 600 MB file show their messages.
    Safari not tried: the owner's, before the demo clip row)
