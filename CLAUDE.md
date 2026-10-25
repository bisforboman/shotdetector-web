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
  fields), `UnsupportedVideoException`; `Mp4Audio` (the sound in one pass with ManagedCodecs' `AacDecoder`: FrameReader's
  `WaveformBuilder` and `ReferenceToneFinder`, silences as `silencedetect`, peaks, and `Loudness.cs`'s BS.1770-4 meter;
  `NotMeasured` says why not, never an exception, for no audio, not AAC, or a track the decoder refuses).
- `src/ShotDetectorWeb.App` (browser-wasm Exe, outside any solution: needs the wasm-tools workload): the page's .NET
  side, `Api`'s `[JSExport]`s `Open(JSObject)` (a `Blob`, the picked `File`, read through `BlobStream`; the `YuvVideo`
  as JSON), `Detect(string optionsJson)` (shots, frame count, rate, duration as JSON), `ScenesCsv()` and `StatsCsv()`
  (the last run's CSVs as strings, not inside the JSON: README "What the heap holds"), `LoadedAssemblies()`, and `[JSImport]`s `progress(done, expected)`, `shot(json)` (`DetectionOptions.OnShot`: each
  shot as soon as it is known, one object of `Shots.Json`'s array) and `read(blob, position, view)` (`BlobStream`: a
  seekable `Stream` over the file in slices of a megabyte); `wwwroot/worker.js` owns the runtime and answers the page's
  `open` and `detect` messages (posting `progress` and `shot` while a run goes), `wwwroot/blob-read.js` is its `read`
  (`FileReaderSync`); the page is
  `wwwroot/index.html`, `app.js`, `style.css` (no build step, no dependency; README "Running the page"). `Api` prefixes
  ManagedCodecs' `InvalidDataException` for the page (a .NET exception's type doesn't reach JavaScript).
- `tools/NativeCheck`: core vs FFmpeg path, every sample and detector; `AudioCheck.cs`: `Mp4Audio` vs FFmpeg's
  measures (FrameReader's `WaveformData.Read`, `FindReferenceTone`, `silencedetect`, `ebur128`) within tolerances, and
  `samples/no-aac/` answered with a reason; `tools/app-check.mjs`: the App's published bundle
  under Node through the same exports, the samples as `Blob`s read through the published `blob-read.js`
  (`tools/node-host.mjs`: the host's imports and a `FileReaderSync` for Node); `tools/replace-check.mjs`: a bundle
  rebuilt with a changed FastYuv runs it (`tools/check.sh replace`); `tools/Shared/Runs.cs`: the runs NativeCheck makes,
  which app-check.mjs holds as the App's options JSON (keep the two the same); `tools/check.sh`: both checks;
  `tools/make-samples.sh`: the samples.
- `.github/actions/btbn-ffmpeg`: copied from shotdetector (BtbN's FFmpeg pinned by SHA-256); `.github/workflows/ci.yml`;
  `.github/workflows/pages.yml`: after CI on a push to `main`, the AOT publish's `wwwroot/` plus FastYuv's license,
  `.nojekyll` and the demo clip (`demo/sintel-trailer.mp4`: Sintel's trailer, downloaded pinned by the same SHA-256 as
  `ci.yml` and made by `tools/make-samples.sh`, the file the checks run), checked for the two LGPL files and the clip,
  deployed to https://bisforboman.github.io/shotdetector-web/.
- `docs/backlog.md`: the work, in priority order, each row self-contained (Why, Where, Done when, Not, Status);
  `docs/backlog-done.md`: finished rows; `docs/decisions/`: design questions with their choices and answers
  (`2026-10-24-the-page.md`: how the page is built; `2026-10-24-fastyuv-replaceable.md`: FastYuv replaced by
  rebuilding; `2026-10-24-audio.md`: the audio decoded in the worker by ManagedCodecs; `2026-10-25-one-page.md`: one
  timeline and cards, a module per panel).

## Working on this repository

- Work is a row of `docs/backlog.md`: one row, one cloud session (the owner's choice, 2026-10-24: not on their
  machine), one pull request against `main`, CI green. Set its status to In progress when starting (with the date),
  move it to `docs/backlog-done.md` when it lands, and add a row when a gap turns up. A design question that the row doesn't settle goes to `docs/decisions/` before the code.
- `main` requires CI's `build` check, and auto-merge is on (the owner's choice, 2026-10-24). Once the PR is finished,
  with nothing left to push, run `gh pr merge <n> --auto --merge --delete-branch`: it merges when CI is green. Not
  earlier, or a green run merges half a row.
- Scope so far: 8-bit 4:2:0 progressive H.264 in non-fragmented MP4, constant frame rate, not rotated; anything else
  is refused in `Mp4Input`'s constructor with a message saying what. Widening it means a sample for it in
  make-samples.sh and the checks passing on it.
- A new sample isolates one thing and is checked to really have it (ffprobe, `-bsf:v trace_headers`). ffmpeg tags its
  test sources BT.601 (bt470bg): an untagged sample needs `setparams=...=unknown`.
- Before pushing: `tools/make-samples.sh` (once) and `tools/check.sh` (native + wasm interpreted, a few minutes),
  `tools/check.sh wasm aot` for anything that could differ under AOT.
- Packages: ShotDetector/FastYuv 3.15.0 and FrameReader 2.11.0 from nuget.org, versions central in
  `Directory.Packages.props`; ManagedCodecs is the submodule, referenced by project. README, "Packages".
- FastYuv is LGPL-2.1: it stays its own DLL/.wasm (no single-file output), its COPYING.LGPL is copied into every output
  (the core csproj), and it is replaced by rebuilding with `-p:FastYuvAssembly=<dll>`, not by swapping the published
  file (`docs/decisions/2026-10-24-fastyuv-replaceable.md`; `tools/check.sh replace aot` in CI). Workflows pin actions by commit SHA with the tag in a comment, as shotdetector.
- Commit messages: what and why. Small commits.

## Gotchas

- `dotnet workload list` can show wasm-tools installed with no packs in `~/.dotnet/packs` (NETSDK1147 on build):
  `dotnet workload install wasm-tools` again installs them.
- ShotDetector's file path reports `DetectedVideo.Duration` from ffprobe's format duration text (six decimals), to the
  nearest tick since 3.15.0 (3.14.0 went through `TimeSpan.FromSeconds(double)`, which truncates: 4.56 s came out
  4.5599999): `Mp4Input` and NativeCheck's oracle do the same.
- ManagedCodecs' audio packets start at negative pts (priming) with `SkipSamples`: a stream's start for FFmpeg's
  format duration is pts + skip (in the stream's time base).
- On wasm the runtime prints `MONO_WASM:` notes on stdout under Node; Emscripten's file system doesn't see the host's,
  so files come in as a `Blob` through `Open(JSObject)`, read in slices by `[JSImport] read`, and results go out as
  JSON strings (app-check.mjs writes them).
- In the worker each `FileReaderSync` read is a new `ArrayBuffer` that V8 frees late while .NET holds the thread: a pass
  that reads the file as fast as it comes grows the renderer with the file (2.37 GB read: 1.2 to 1.4 GB; seen
  2026-10-25). A run reads at the decoder's pace and stays bounded; `Mp4Input`'s duration pass skips the packets' data
  (`TimingOnlyStream`). A new pass over a whole file (the audio rows) should do the same or read at a decoder's pace.
- Node has no synchronous Blob read: `tools/node-host.mjs`'s `FileReaderSync` reads each slice on a worker thread and
  waits with `Atomics.wait`. Its Blobs hold the file in memory: a file-backed one (`fs.openAsBlob`) aborts Node 22
  when a worker reads its clone ("Entering the V8 API without proper locking", seen 2026-10-25).
- The App's `_framework/` keeps unhashed names (`WasmFingerprintAssets` false: `ShotDetector.FastYuv.wasm`), and
  `dotnet publish` adds to an existing publish folder: `tools/check.sh` deletes it first so its listing check means
  something.
- An interpreted publish after an AOT one (`tools/check.sh wasm aot`, then `tools/check.sh wasm`) can reuse the App's
  `obj/` from the AOT build, whose IL is stripped: the runtime aborts at start (`NIY encountered in method
  System.OutOfMemoryException:.ctor`, seen 2026-10-24). Delete `src/ShotDetectorWeb.App/obj` between them; CI runs
  interpreted first.
- FFmpeg's `ebur128` prints its summary to one decimal and its frame metadata to three, the peaks there as linear
  amplitudes: `AudioCheck.cs` reads the last frame's metadata (`ametadata=print:file=`). A 2 s gap between tones comes
  out 1.9966 s after FFmpeg's AAC encoder, so `silencedetect`'s default 2 s finds nothing in the tone sample: the
  check also runs at 1 s.
- The published bundle is trimmed with `UseSystemResourceKeys`: the framework's exception messages come out as resource
  keys (`DeserializeUnableToConvertValue, ...`), the libraries' own (ManagedCodecs, `UnsupportedVideoException`) whole.
- Under wasm AOT the runtime loads some assemblies before `Main` that the interpreter never touches, FFmpeg.AutoGen
  among them (ShotDetector references it, the trimmer keeps it); interpreted, assemblies load on first use. The wasm check's "nothing of
  FFmpeg" (app-check.mjs, `LoadedAssemblies()`) is strict interpreted, "nothing loaded by the runs" under AOT.
