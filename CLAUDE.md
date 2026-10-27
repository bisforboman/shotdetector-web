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

- `src/ShotDetectorWeb.Core` (net10.0, AOT-compatible): `Mp4Input` (open, describe, frames, detect; `FramesAsync` and
  `DetectAsync`: the frames as an asynchronous source that awaits the caller every N frames and ends early when told;
  `Audio`, the audio track as ffprobe describes it; `Mp4Input.Ranges.cs`: `Ranges(count)`, the video split at keyframes
  from `Packets` into `FrameRange`s about equal in frames, `ScoreRangeAsync` (one range decoded from its keyframe, the
  packets before it not read, for ShotDetector's `ScoreRangeAsync`), `Merge` and `FromStart` (the ranges that follow
  each other from the start), `ShownFrames`), `Mp4Header` (the MP4 boxes FFmpeg reads that ManagedCodecs doesn't
  expose, the audio codec's FFmpeg name among them), `SpsInfo` (the SPS's format and VUI colour
  fields), `VideoPackets` (each video packet's pts, size and keyframe/discard flags, by column, kept by `Mp4Input`'s
  walk when it opens a file), `UnsupportedVideoException`; `Mp4Audio` (the sound in one pass with ManagedCodecs'
  `AacDecoder`: FrameReader's `WaveformBuilder` and `ReferenceToneFinder`, silences as `silencedetect`, peaks, and
  `Loudness.cs`'s BS.1770-4 meter; `NotMeasured` says why not, never an exception, for no audio, not AAC, or a track the
  decoder refuses).
- `src/ShotDetectorWeb.App` (browser-wasm Exe, outside any solution: needs the wasm-tools workload): the page's .NET
  side, `Api`, a partial class in one file per feature: `Api.cs` has the `[JSExport]`s `Open(JSObject)` (a `Blob`, the
  picked `File`, read through `BlobStream`; the `YuvVideo` as JSON, with `audio`: `{ codec, sampleRate,
  channels }` from `Mp4Input.Audio`, or null), `Detect(string optionsJson)` (a promise: shots,
  frame count, rate, duration, `stopped` and `warnings` as JSON; `Mp4Input.DetectAsync`, pausing every 8 frames, `PauseEvery`),
  `Stop()` (the run's frames end at its next pause: the result for the frames read, `stopped` true) and `Cancel()` (the
  run fails, no result), and the `[JSImport]`s `progress(done, expected)`, `shot(json)` (`DetectionOptions.OnShot`:
  each shot as soon as it is known, one object of `Shots.Json`'s array) and `pause(framesRead)` (a promise resolved in
  the worker's next task, `wwwroot/pause.js`); `Api.Csv.cs` has
  `ScenesCsv()` and `StatsCsv()` (the last run's CSVs as strings, not inside the JSON: README "What the heap holds"),
  `Api.Assemblies.cs` `LoadedAssemblies()`; `Api.Audio.cs` `Audio()`: `Mp4Audio.Measure` over the opened file (a
  `BlobStream` of its own), called by `app.js` in a worker of its own alongside the file's first run and told to the
  panels as the `audio` event, its JSON camelCase with times in seconds: `notMeasured` (why not, the core's words: no audio, which the page
  shows nothing for, "The file has no audio track.", checked by app-check.mjs; not AAC; refused by the decoder; null
  when measured), `track`
  (`{ codec, sampleRate, channels, start, duration }` or null), `waveform` (`{ start, sampleRate, samplesPerPoint,
  pointsPerSecond, channels, min, max }`: point `i` from `start + i / pointsPerSecond`, `min` and `max` one array per
  waveform, one unless split, of audiowaveform's 16-bit values, full scale 32767; or null), `referenceTone` (`[{ start,
  end, level }]`), `silences` (`[{ start, end }]`), `peaks` (`[{ sample, true, fullScale }]` per channel, dBFS and
  dBTP, minus infinity as null), `loudness` (`{ integrated, range, truePeak }`, integrated null below the gate; or
  null); `Api.Packets.cs` `Packets()`: `Mp4Input.Packets` as JSON by column, `{ timeBase, pts, sizes, keyframes,
  discarded }` (pts in the time base, sizes in bytes, decode order; the keyframes' and discarded packets' indexes);
  `Api.Ranges.cs` a long video across workers (decision `2026-10-26-workers.md`): `Ranges(count)` (`{ frames, ranges:
  [{ firstFrame, endFrame }] }`), `ScoreRange(optionsJson, count, index)` (a promise of `{ frames, stopped }`; progress,
  pauses, `Stop` and `Cancel` as `Detect`'s) and `RangeBytes()` (its `RangeScores` as bytes), `Merge(optionsJson,
  bytes, lengths)` (the ranges' bytes one after another: `Detect`'s JSON, `stopped` when they don't reach the end; a
  `ShotDetectionException` as "The ranges didn't merge: ...", on which the page detects again with one worker).
  `BlobStream` imports `read(blob, position, view)` (a seekable `Stream` over
  the file in slices of a megabyte). `wwwroot/worker.js` owns the runtime and calls any export by name (`{ type: 'call',
  id, name, args }`, answered in order, one at a time; `progress` and `shot` posted while `Detect` runs; `{ type: 'stop'
  }` and `{ type: 'cancel' }` handled at once, between `Detect`'s pauses, a cancelled call answered `cancelled`),
  `wwwroot/blob-read.js` is its `read` (`FileReaderSync`), `wwwroot/pause.js` its `pause` (a `MessageChannel`). The page (no build step, no dependency, native ES modules; README "Running the page";
  decision `2026-10-25-one-page.md`): `index.html` (the layout and the list of panels), `style.css`, `app.js` (the
  states, the options, the estimate, stop (`terminate()` only for a worker that doesn't answer), the demo, a long video
  split across workers (`SPLIT_SECONDS`, `MOST_WORKERS`, `?workers=N` in the address; the page's own worker scores the
  first range and merges) and the sound's worker; the events panels listen to, `call(name, ...args)` and
  `card(title)`), `timeline.js` (one axis under the `<video>`, the playhead, click to seek, `lane(label, draw)`, `x()`,
  `seek()`) and `panels/`: `cuts.js` (a lane), `ranges.js` (a lane: a split run's range per worker,
  filled as far as it has got, or the line saying why it went back to one worker), `scores.js` (a lane: a `<canvas>` of the run's score column from
  `StatsCsv()`, picked by the header per detector, against the threshold the run used, the cuts marked, still pictures
  as the page's reading; the CSV read once into typed arrays, drawn decimated to the canvas's width), `waveform.js` (a lane: `Audio()`'s waveform, none without audio, one line
  when not measured; `lane(...).show(false)` hides a lane), `run.js` (a card: the summary and downloads), `shots.js`
  (the table below), `warnings.js` (a card, only when a run's `warnings` has any: ShotDetector's sentences as they are),
  `stats.js` (a card: the shots' statistics, the page's own figures from `Detect`'s shots; it names
  the shot playing and marks its row in the table, found by `#shots`), `sound.js` (on the `audio` event: a lane of the tone's spans and the silences, a card of the
  sound's measures, labelled measured, what the page derives labelled as its own), `bitrate.js` (on `opened`: a lane of
  the bitrate per second and the keyframes from `Packets()`, a card with how many cuts fall on a keyframe after a run,
  the page's own reading), `about.js` (a card on the `opened` event: Open's description in a viewer's words, its JSON
  folded under them). `Api` prefixes ManagedCodecs'
  `InvalidDataException` for the page (a .NET exception's type doesn't reach JavaScript).
  - **Adding to the page:** something over time is a lane, anything else a card (or `card(title, { below: true })`
    under the video, as the table). A panel is a module of its own, `wwwroot/panels/<name>.js`, that imports `on`,
    `call` and `card` from `../app.js` and `lane`, `x`, `seek` from `../timeline.js`, plus one `<script
    type="module">` line in `index.html` (the order of those lines is the order of the lanes and cards) and its rules
    in `style.css`. A new export is `Api.<Feature>.cs`, a partial file of its own with its own `JsonSerializerContext`
    if it needs JSON; the panel calls it with `call('<Export>', ...)`, and `worker.js` doesn't change. `app.js` changes
    only for a new event of the page's.
- `tools/NativeCheck`: core vs FFmpeg path, every sample and detector, and `PacketCheck.cs`: `Mp4Input.Packets` vs
  `ffprobe -show_packets` (its lines written to `out/native/<sample>.packets.txt`); `AudioStreamCheck.cs`: `Mp4Input.Audio`
  vs ffprobe's first audio stream; `RangeCheck.cs`: every sample's all-detector run split into ranges, scored and
  merged, vs the whole run's CSVs; `AudioCheck.cs`: `Mp4Audio` vs FFmpeg's measures (FrameReader's `WaveformData.Read`,
  `FindReferenceTone`, `silencedetect`, `ebur128`) within tolerances, and `samples/no-aac/` answered with a reason;
  `tools/app-check.mjs`: the App's published bundle
  under Node through the same exports, the samples as `Blob`s read through the published `blob-read.js`
  (`tools/node-host.mjs`: the host's imports and a `FileReaderSync` for Node), every run also stopped half-way (its
  stats CSV the whole run's rows up to there, its shots the whole run's with the next one cut short), one cancelled,
  `Detect`'s pauses under 1% of the runs' time, every run split into ranges and merged (the whole run's CSVs and shots),
  stopped in its second range (the result for its start), and merges of ranges that don't match failing as the page
  expects; `tools/replace-check.mjs`: a bundle
  rebuilt with a changed FastYuv runs it (`tools/check.sh replace`); `tools/Shared/Runs.cs`: the runs NativeCheck makes,
  which app-check.mjs holds as the App's options JSON (keep the two the same); `tools/check.sh`: both checks (sample files named after the options: only those);
  `tools/make-samples.sh`: the samples; `tools/WebCodecsCheck` (ManagedCodecs' access units and a hash per plane per
  frame), `tools/webcodecs/` (a page any browser opens from `serve.mjs`: `VideoDecoder` on those units, every plane
  compared, timed against `tools/WebCodecsWasm`, ManagedCodecs' decoder on wasm): the WebCodecs measurement (README
  "WebCodecs"), not in the checks or CI.
- `.github/actions/btbn-ffmpeg`: copied from shotdetector (BtbN's FFmpeg pinned by SHA-256); `.github/workflows/ci.yml`:
  pull requests (interpreted on one sample, AOT on all; docs-only ones pass without checks) and the whole of it daily on
  `main` (README "Checks");
  `.github/workflows/pages.yml`: on a push to `main`, the native, AOT and replace checks, then the AOT check's own
  publish's `wwwroot/` plus FastYuv's license,
  `.nojekyll` and the demo clip (`demo/sintel-trailer.mp4`: Sintel's trailer, downloaded pinned by the same SHA-256 as
  `ci.yml` and made by `tools/make-samples.sh`, the file the checks run), checked for the two LGPL files and the clip,
  deployed to https://bisforboman.github.io/shotdetector-web/ only if all of it passes.
- `docs/backlog.md`: the work, in priority order, each row self-contained (Why, Where, Done when, Not, Status);
  `docs/backlog-done.md`: finished rows; `docs/decisions/`: design questions with their choices and answers
  (`2026-10-24-the-page.md`: how the page is built; `2026-10-24-fastyuv-replaceable.md`: FastYuv replaced by
  rebuilding; `2026-10-24-audio.md`: the audio decoded in the worker by ManagedCodecs; `2026-10-25-one-page.md`: one
  timeline and cards, a module per panel; `2026-10-26-workers.md`: long videos across several workers;
  `2026-10-26-new-ui.md`: the page's new UI, B chosen by the owner, built by the backlog row); `docs/mockups/`: static HTML
  mockups a decision refers to, nothing of the real page.

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
- Packages: ShotDetector/FastYuv 3.16.1 and FrameReader 2.11.0 from nuget.org, versions central in
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
  out 1.9966 s after FFmpeg's AAC encoder, too short for `silencedetect`'s default 2 s (which the page shows): the
  tone sample's gap is 3 s, and the check also runs at 1 s.
- The published bundle is trimmed with `UseSystemResourceKeys`: the framework's exception messages come out as resource
  keys (`DeserializeUnableToConvertValue, ...`), the libraries' own (ManagedCodecs, `UnsupportedVideoException`) whole.
- Under wasm AOT the runtime loads some assemblies before `Main` that the interpreter never touches, FFmpeg.AutoGen
  among them (ShotDetector references it, the trimmer keeps it); interpreted, assemblies load on first use. The wasm check's "nothing of
  FFmpeg" (app-check.mjs, `LoadedAssemblies()`) is strict interpreted, "nothing loaded by the runs" under AOT.
- A `[JSExport]`'s task that ends cancelled (an `OperationCanceledException` out of an `async` method) never settles
  the promise JavaScript holds (.NET 10, seen 2026-10-26): `Api.Detect` turns its token's cancellation into a
  `RunCancelledException`, which rejects it.
- A split run's Stop stops every worker at its next pause, each range ending where it got to (ShotDetector: "the range
  ends where the frames stop"), and `Mp4Input.FromStart` keeps the ranges while each reaches into the next one's first
  frame: the first range stopped short ends the merge, as a one-worker run stopped there. With ranges about equal,
  waiting for "finished" ranges only would keep nothing until the last minutes of a run.
- A stopped run is a file that ends there, for the detectors too: the adaptive ratio of the last frames (its window
  after them) is `None`, a cut FlashFilter's merge would decide on frames past the stop isn't there, and the threshold
  detector ends a fade-out the stop falls in with a cut (`PostProcess`), as at a file's end.
