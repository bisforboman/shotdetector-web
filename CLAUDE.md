# shotdetector-web

Shot detection entirely in the browser: ManagedCodecs (submodule `external/ManagedCodecs`) decodes H.264 in MP4,
ShotDetector.FastYuv converts, ShotDetector detects through `ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, ...)`.
Repo: `github.com/bisforboman/shotdetector-web`, MIT. README.md has the layout, status and how to run the checks; the
owner's decisions are in shotdetector's docs/decisions/2026-10-24-managedcodecs-showcase.md.

## Core idea

Exactness first: the scene and stats CSVs must equal ShotDetector's FFmpeg path's (`Detect(path)`, FFmpeg in-process,
same FastYuv converter) byte for byte, natively and on browser-wasm, before anything else. That rests on `YuvVideo`
saying what FFmpeg says about the file (average frame rate, nb_frames, time base, start pts, colour tags, duration):
ManagedCodecs doesn't expose all of it, so `Mp4Header`/`SpsInfo` read it, and NativeCheck compares every field with
FFmpeg's before it compares results. Where the core differs from the FFmpeg path, find out whether it's the
description (fix it here) or a bug in ShotDetector or ManagedCodecs (report it there, don't work around it).

## Layout

- `src/ShotDetectorWeb.Core` (net10.0, AOT-compatible): `Mp4Input` (open, describe, frames, detect),
  `Mp4Header` (the MP4 boxes FFmpeg reads that ManagedCodecs doesn't expose), `SpsInfo` (the SPS's format and VUI colour
  fields), `UnsupportedVideoException`.
- `src/ShotDetectorWeb.App` (browser-wasm Exe, outside any solution: needs the wasm-tools workload): the page's .NET
  side, `Api`'s `[JSExport]`s `Open(byte[])` (the `YuvVideo` as JSON), `Detect(string optionsJson)` (shots, scene and
  stats CSVs, frame count, rate, duration as JSON), `LoadedAssemblies()`, and `[JSImport] progress(done, expected)`;
  `wwwroot/worker.js` owns the runtime and answers the page's `open` and `detect` messages.
- `tools/NativeCheck`: core vs FFmpeg path, every sample and detector; `tools/app-check.mjs`: the App's published
  bundle under Node through the same exports; `tools/Shared/Runs.cs`: the runs NativeCheck makes, which app-check.mjs
  holds as the App's options JSON (keep the two the same); `tools/check.sh`: both checks; `tools/make-samples.sh`: the
  samples.
- `.github/actions/btbn-ffmpeg`: copied from shotdetector (BtbN's FFmpeg pinned by SHA-256); `.github/workflows/ci.yml`.
- `docs/backlog.md`: the work, in priority order, each row self-contained (Why, Where, Done when, Not, Status);
  `docs/backlog-done.md`: finished rows; `docs/decisions/`: design questions with their choices and answers
  (`2026-10-24-the-page.md`: how the page is built).

## Working on this repository

- Work is a row of `docs/backlog.md`: one row, one cloud session (the owner's choice, 2026-10-24: not on their
  machine), one pull request against `main`, CI green. Set its status to In progress when starting (with the date),
  move it to `docs/backlog-done.md` when it lands, and add a row when a gap turns up. A design question that the row doesn't settle goes to `docs/decisions/` before the code.
- Scope so far: 8-bit 4:2:0 progressive H.264 in non-fragmented MP4, constant frame rate, not rotated; anything else
  is refused in `Mp4Input`'s constructor with a message saying what. Widening it means a sample for it in
  make-samples.sh and the checks passing on it.
- A new sample isolates one thing and is checked to really have it (ffprobe, `-bsf:v trace_headers`). ffmpeg tags its
  test sources BT.601 (bt470bg): an untagged sample needs `setparams=...=unknown`.
- Before pushing: `tools/make-samples.sh` (once) and `tools/check.sh` (native + wasm interpreted, a few minutes),
  `tools/check.sh wasm aot` for anything that could differ under AOT.
- Packages: ShotDetector/FastYuv 3.14.0 from nuget.org, versions central in `Directory.Packages.props`; ManagedCodecs
  is the submodule, referenced by project. README, "Packages".
- FastYuv is LGPL-2.1: it stays its own DLL/.wasm (no single-file or merged AOT output), and its COPYING.LGPL is copied
  into every output (the core csproj). Workflows pin actions by commit SHA with the tag in a comment, as shotdetector.
- Commit messages: what and why. Small commits.

## Gotchas

- `dotnet workload list` can show wasm-tools installed with no packs in `~/.dotnet/packs` (NETSDK1147 on build):
  `dotnet workload install wasm-tools` again installs them.
- ShotDetector's file path reports `DetectedVideo.Duration` from ffprobe's format duration text (six decimals) through
  `TimeSpan.FromSeconds(double)`, which truncates (4.56 s comes out 4.5599999): `Mp4Input` does the same.
- ManagedCodecs' audio packets start at negative pts (priming) with `SkipSamples`: a stream's start for FFmpeg's
  format duration is pts + skip (in the stream's time base).
- On wasm the runtime prints `MONO_WASM:` notes on stdout under Node; Emscripten's file system doesn't see the host's,
  so files come in as bytes through `Open(byte[])` and results go out as JSON strings (app-check.mjs writes them).
- The App's `_framework/` keeps unhashed names (`WasmFingerprintAssets` false: `ShotDetector.FastYuv.wasm`), and
  `dotnet publish` adds to an existing publish folder: `tools/check.sh` deletes it first so its listing check means
  something.
- The published bundle is trimmed with `UseSystemResourceKeys`: the framework's exception messages come out as resource
  keys (`DeserializeUnableToConvertValue, ...`), the libraries' own (ManagedCodecs, `UnsupportedVideoException`) whole.
- Under wasm AOT the runtime loads some assemblies before `Main` that the interpreter never touches, FFmpeg.AutoGen
  among them (ShotDetector references it, the trimmer keeps it); interpreted, assemblies load on first use. The wasm check's "nothing of
  FFmpeg" (app-check.mjs, `LoadedAssemblies()`) is strict interpreted, "nothing loaded by the runs" under AOT.
