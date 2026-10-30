# shotdetector-web

Shot detection entirely in the browser: ManagedCodecs (from nuget.org since 2026-10-28) decodes H.264 in MP4,
ShotDetector.FastYuv converts, ShotDetector detects through `ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, ...)`.
Repo: `github.com/bisforboman/shotdetector-web`, MIT. README.md has the layout, status and how to run the checks; the
owner's decisions are in shotdetector's docs/decisions/2026-10-24-managedcodecs-showcase.md.

## Core idea

Exactness first: the scene and stats CSVs (and the shots as JSON and HTML) must equal ShotDetector's FFmpeg path's (`Detect(path)`, FFmpeg in-process,
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
  each other from the start), `ShownFrames`, `Continue` (a stopped run's `Continuation`: the earlier ranges to keep
  and the rest's range, from the last range start at or before the last frame scored), `Ranges(count, from)` (the rest
  split) and `ScoreRangeAsync(earlier, ...)` (the rest, telling its shots)), `Mp4Input.DecoderThreads`
  (ManagedCodecs' `H264Decoder.Threads`, frame threading: the same frames; a run that ends early drains the decoder),
  `Mp4Header` (the MP4 boxes FFmpeg reads that ManagedCodecs doesn't
  expose, the audio codec's FFmpeg name among them), `SpsInfo` (the SPS's format and VUI colour
  fields), `VideoPackets` (each video packet's pts, size and keyframe/discard flags, by column, kept by `Mp4Input`'s
  walk when it opens a file), `UnsupportedVideoException`; `Mp4Audio` (the sound in one pass with ManagedCodecs'
  `AacDecoder`: FrameReader's `WaveformBuilder` and `ReferenceToneFinder`, silences as `silencedetect`, peaks, and
  `Loudness.cs`'s BS.1770-4 meter; `NotMeasured` says why not, never an exception, for no audio, not AAC, or a track the
  decoder refuses); `PictureMeasures.cs`: `PictureMeter` (each frame measured as a run reads it, the `picture`
  parameter of `Detect`, `DetectAsync`, `ScoreRangeAsync`: what FFmpeg's `signalstats`, `blackdetect` and `cropdetect`
  count, and colour bars by their definition, `BarColours`), `PictureMeasures` (by column; FFmpeg's figures from them,
  `ToBytes`/`FromBytes` hashed, `Join` for a split run's ranges); `PictureSummary.cs`: the page's JSON of them
  (`PictureJson`), which NativeCheck also writes.
- `src/ShotDetectorWeb.App` (browser-wasm Exe, outside any solution: needs the wasm-tools workload): the page's .NET
  side, `Api`, a partial class in one file per feature: `Api.cs` has the `[JSExport]`s `Open(JSObject)` (a `Blob`, the
  picked `File`, read through `BlobStream`; the `YuvVideo` as JSON, with `frames` (`Mp4Input.ShownFrames`, the page's
  count: `frameCount` is FFmpeg's nb_frames, null when fragmented) and `audio`: `{ codec, sampleRate,
  channels }` from `Mp4Input.Audio`, or null), `Detect(string optionsJson)` (`{ detector, threshold, minSceneLength,
  frameWindow, minContentVal }`, each null for the library's default, or `detectors`; a promise: shots, frame count,
  rate, duration, `stopped` and `warnings` as JSON; one range from frame 0 scored with `OnShot` and merged without it,
  `Detect`'s result byte for byte, so that a stopped run leaves scores, pausing every 8 frames, `PauseEvery`),
  `Stop()` (the run's frames end at its next pause: the result for the frames read, `stopped` true) and `Cancel()` (the
  run fails, no result), and the `[JSImport]`s `progress(done, expected)`, `shot(json)` (`DetectionOptions.OnShot`:
  each shot as soon as it is known, one object of `Shots.Json`'s array) and `pause(framesRead)` (a promise resolved in
  the worker's next task, `wwwroot/pause.js`); `Api.Csv.cs` has
  `ScenesCsv()` and `StatsCsv()` (the last run's CSVs, not inside the JSON: the scenes CSV a string, the stats CSV a
  `Blob` written a row at a time by `Stats.WriteCsv` through `BlobWriter`, a write-only `Stream` whose parts go out
  through the `[JSImport]`s `parts`, `append` and `blob`, so it is never one string in the heap: README "What the heap
  holds"); `Api.Shots.cs` has `ShotsJson()` and `ShotsHtml()` (the last run's shots as `Shots.Json` and as
  `Shots.Html` without images, the run card's other two downloads, which `run.js` asks for on `done`),
  `Api.Assemblies.cs` `LoadedAssemblies()`; `Api.Versions.cs` `Versions()` (`{ runtime, packages: { id: version } }`,
  each package's NuGet version from its assembly, for the footer and app-check.mjs's check against the pins); `Api.Audio.cs` `Audio()`: `Mp4Audio.Measure` over the opened file (a
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
  `Api.Picture.cs` `Picture()`: the last run's `PictureSummary` (camelCase: `frames`, `fullRange`, `points` { `framesPerPoint`,
  `min`, `mean`, `max`, `brng` }, `black` and `bars` [{ `firstFrame`, `endFrame`, `start`, `end`, `long` }], `brng` {
  `frames`, `largest`, `at` }, `levels` { `min`, `mean`, `max` }, `crop` cropdetect's or null), and `RangeBytes`' packing
  (the scores' length, the scores, the range's picture measures); `Api.Ranges.cs` a long video across workers (decision `2026-10-26-workers.md`): `Ranges(count, from)` (`{ frames, ranges:
  [{ firstFrame, endFrame }] }`, from 0 or a stopped run's rest's first frame), `ScoreRange(optionsJson, count, index,
  from)` (a promise of `{ frames, stopped }`; progress, pauses, `Stop` and `Cancel` as `Detect`'s) and `RangeBytes()`
  (its `RangeScores` as bytes), `Merge(optionsJson, kept, bytes, lengths)` (a stopped run's kept bytes, empty for none,
  then the ranges' bytes one after another: `Detect`'s JSON, `stopped` when they don't reach the end; the kept bytes
  alone the stopped run's result again; a `ShotDetectionException` as "The ranges didn't merge: ...", on which the page
  detects again with one worker); `Api.Resume.cs` a stopped run resumed (decision
  `2026-10-30-resume-across-visits.md`): `Kept()` (the last run's ranges from the start and picture measures, a `Blob`
  of `KeptRun.ToBytes`, its first int the frame the rest starts at, -1 for none) and `Resume(optionsJson, kept)`
  (`Detect` going on from them, the rest's shots told; kept bytes that don't resume as "The stopped run didn't resume:
  ...", on which the page detects from the start).
  `BlobStream` imports `read(blob, position, view)` (a seekable `Stream` over
  the file in slices of a megabyte). The threaded bundle (`-p:WasmEnableThreads=true`, `THREADS`; decision
  `2026-10-30-threads.md`), published into `wwwroot/threads/_framework/` by `tools/check.sh`'s `publish_app`, runs a
  one-worker run on the page's own thread: `Host.cs` (in it the run goes to the thread pool and each host call back to
  .NET's JavaScript thread; in the worker's bundle a pass-through), `Api.Threads.cs` (`OpenThreaded(file, threads)`,
  `StopThreaded`, `CancelThreaded`, `ScenesCsvThreaded`, `StatsCsvThreaded`: tasks, as .NET on the main thread calls
  nothing synchronously), `BlobStream` reading through `readAsync` and `copy` there. `wwwroot/worker.js` owns the runtime and calls any export by name (`{ type: 'call',
  id, name, args }`, answered in order, one at a time; `progress` and `shot` posted while `Detect` runs; `{ type: 'stop'
  }`, `{ type: 'cancel' }`, `{ type: 'pause' }` and `{ type: 'resume' }` handled at once, between `Detect`'s pauses, a
  cancelled call answered `cancelled`; a stop or cancel while paused releases the run to end there; `{ type: 'held',
  frames }` posted once a paused run waits, the frames read, where a stop ends it: the page's paused status),
  `wwwroot/blob-read.js` is its `read` (`FileReaderSync`), `wwwroot/blob-write.js` its `parts`, `append` and `blob`, `wwwroot/pause.js` its `pause` (a `MessageChannel`; `hold`
  and `release`: while held the promise stays unresolved, so the run waits there, README "Pausing a run");
  `wwwroot/versions.js` fills the footer's version slots from `Versions()` (not a panel); `wwwroot/threads.js` the
  threaded runtime on the page's thread, a `ThreadedRunner` that speaks `worker.js`'s messages (only `Open`, `Detect`,
  `Resume`, the last run's result, stop, cancel, pause, resume), abandoned rather than terminated; `wwwroot/sw.js` the service
  worker: COOP/COEP on every response (cross-origin isolation, which the threads need) and the page offline, network
  first. The page (no build step, no dependency, native ES modules; README "Running the page";
  decisions `2026-10-25-one-page.md` and `2026-10-26-new-ui.md`): `index.html` (the layout: the landing that folds into a
  toolbar once a file is picked, the status line, the result's four rows: the video beside the run's cards, the timeline
  across the width, the cards below it, the details' grid; and the list of panels), `style.css`, `app.js` (the
  states, idle, opening, detecting, paused, stopping, done, stopped, lost and error, as `body[data-state]`, `body.picked` once a
  file is picked; the options, the one-line status, the estimate, pause and resume (`runMs()`: the run's time, pauses
  left out), stop (`terminate()` only for a worker that doesn't
  answer), the demo, the shot playing as the caption under the video (told as the `playing` event), a long video split
  across workers (`SPLIT_SECONDS`, `MOST_WORKERS`, `?workers=N` in the address; the page's own worker scores the first
  range and merges), a one-worker run on threads where the page is isolated (`MOST_THREADS`, `?threads=N`, 0 or 1 the
  page's worker; `serve()` registers `sw.js` and reloads a first visit once), the sound's worker, and a stopped run kept
  in IndexedDB (`KEPT`, one record: the file by name, size and modified time, the options, `Kept()`'s Blob; shown again
  at once when its file is picked, Resume and Forget beside Detect, a rest over `SPLIT_SECONDS` split); the events panels listen to, `call(name, ...args)` and `card(title, {
  place, mark, markTitle })`), `timeline.js` (one axis under the `<video>`, the playhead, click to seek, `lane(label,
  draw)`, `x()`, `seek()`) and `panels/`: `cuts.js` (a lane), `ranges.js` (a lane: a split run's range per worker,
  filled as far as it has got, or the line saying why it went back to one worker), `scores.js` (a lane: a `<canvas>` of the run's score column from
  `StatsCsv()`, picked by the header per detector, against the threshold the run used, the cuts marked, still pictures
  as the page's reading; the CSV read once into typed arrays, drawn decimated to the canvas's width), `waveform.js` (a lane: `Audio()`'s waveform, none without audio, one line
  when not measured; `lane(...).show(false)` hides a lane), `run.js` (a card beside the video: the figures and the downloads, the CSVs and the shots as JSON and HTML), `shots.js`
  (the table below the timeline; marks the shot playing's row; a still of each shot's middle frame, the browser's
  decode from a hidden `<video>` of its own, as rows come into view), `warnings.js` (a card, only when a run's `warnings` has any: ShotDetector's sentences as they are),
  `stats.js` (a card: the shots' statistics, the page's own figures from `Detect`'s shots), `sound.js` (on the `audio`
  event: a lane of the tone's spans and the silences, a card of the sound's measures, marked measured, what the page
  derives saying so in its tooltip), `bitrate.js` (on `opened`: a lane of
  the bitrate per second and the keyframes from `Packets()`, a card with how many cuts fall on a keyframe after a run,
  the page's own reading), `picture.js` (after a run: a lane of each frame's luma band and mean, the black spans, the
  colour bars and out-of-range ticks, and a card of the measures from `Picture()`, marked measured), `about.js` (a card on the `opened` event: Open's description in a viewer's words, its JSON
  folded under them). `Api` prefixes ManagedCodecs'
  `InvalidDataException` for the page (a .NET exception's type doesn't reach JavaScript).
  - **Adding to the page:** something over time is a lane, anything else a card: `card(title, { place })`, `place`
    one of `'details'` (the default: the grid of equal cards last), `'run'` (beside the video, under the run's card:
    the run and what goes with it, as the warnings) or `'below'` (under the timeline, full width, as the table); and
    `mark`, whose numbers the card shows, one chip on its title: `'exact'` (scenedetect's, byte for byte: the CSVs),
    `'measured'` (the core's measures, checked against FFmpeg within a tolerance), `'core'` (the core's description,
    checked field by field) or `'page'` (worked out on the page), with `markTitle` saying how they are checked; a row of
    another kind says so in its tooltip, never a dotted underline. A panel is a module of its own, `wwwroot/panels/<name>.js`, that imports `on`,
    `call` and `card` from `../app.js` and `lane`, `x`, `seek` from `../timeline.js`, plus one `<script
    type="module">` line in `index.html` (the order of those lines is the order of the lanes, and of the cards in each
    place) and its rules
    in `style.css`. A new export is `Api.<Feature>.cs`, a partial file of its own with its own `JsonSerializerContext`
    if it needs JSON; the panel calls it with `call('<Export>', ...)`, and `worker.js` doesn't change. An export of the last run's
    result (as `ShotsJson`, `Picture`) also needs its task twin in `Api.Threads.cs` and its name in `threads.js`'s
    `THREADED` and `RESULTS`: after a run on threads, `call()` asks the threaded runner for it. `app.js` changes
    only for a new event of the page's.
- `tools/NativeCheck`: core vs FFmpeg path, every sample and detector (the CSVs and the shots as JSON and HTML), and `PacketCheck.cs`: `Mp4Input.Packets` vs
  `ffprobe -show_packets` (its lines written to `out/native/<sample>.packets.txt`); `AudioStreamCheck.cs`: `Mp4Input.Audio`
  vs ffprobe's first audio stream; `RangeCheck.cs`: every sample's all-detector run split into ranges, scored and
  merged, vs the whole run's CSVs; `ThreadsCheck.cs`: the all-detector run on four decoder threads, whole, in ranges and
  stopped half-way, vs one thread; `ResumeCheck.cs`: the default and all-detector runs on one range stopped twice and
  resumed, and split, stopped and the rest split again, vs the whole run's CSVs, shots told and picture measures;
  `DefaultsCheck.cs`: the defaults `app.js` prefills (read from it) vs ShotDetector's,
  the run with each typed in vs the run without, on every sample; `AudioCheck.cs`: `Mp4Audio` vs FFmpeg's measures (FrameReader's `WaveformData.Read`,
  `FindReferenceTone`, `silencedetect`, `ebur128`) within tolerances, and `samples/no-aac/` answered with a reason;
  `PictureCheck.cs`: the default run's `PictureMeter` vs `signalstats`, `cropdetect` (per frame and whole) and
  `blackdetect` (d=0 and 2 s) exactly, the colour bars by their definition on the samples that have them, the ranges'
  measures joined vs the whole run's (`out/native/<sample>.picture.json`, which app-check.mjs compares `Picture()` with);
  `tools/app-check.mjs`: the App's published bundle
  under Node through the same exports, the samples as `Blob`s read through the published `blob-read.js`
  (`tools/node-host.mjs`: the host's imports and a `FileReaderSync` for Node), every run also stopped half-way (its
  stats CSV the whole run's rows up to there, its shots the whole run's with the next one cut short), one cancelled,
  `Detect`'s pauses under 1% of the runs' time, the default and all-detector runs stopped at a third, shown again from
  their kept bytes, resumed, stopped again and resumed to the end (and stopped split, the rest split again) to the
  whole run's CSVs, shots and picture, kept bytes of other options not resuming, every sample's default run paused half-way (no progress while held,
  the run never paused's CSVs and shots once resumed; on the first sample one stopped while paused), the default and all-detector runs split into ranges and merged (the whole run's CSVs and shots),
  stopped in its second range (the result for its start), and merges of ranges that don't match failing as the page
  expects, and `Picture()` the native check's (a split run's the whole run's, a stopped run's its frames); `tools/replace-check.mjs`: a bundle
  rebuilt with a changed FastYuv runs it (`tools/check.sh replace`); `tools/page-check/page-check.mjs`: the page itself in
  Chromium (Playwright pinned by its `package-lock.json`; `tools/check.sh page`), the published `wwwroot` served with
  the demo clip, each page isolated by its service worker first, a sample picked on threads, on the page's worker
  (`?threads=0`) and on two workers, the demo on threads paused and resumed, then stopped while paused, then run again
  offline from the cache, the demo stopped while paused shown again after a reload and resumed on threads, split
  across two workers stopped and resumed, and in Firefox (`PAGE_CHECK_FIREFOX=skip` where Playwright has none) stopped,
  reloaded and resumed, each run's downloaded CSVs, JSON and HTML against the native run's (the only check of the
  threaded bundle), the picture card's crop and colour bars the native run's, any page or worker error failing it;
  `tools/Shared/Runs.cs`: the runs NativeCheck makes,
  which app-check.mjs holds as the App's options JSON (keep the two the same); `tools/check.sh`: the checks (sample files named after the options: only those);
  `tools/make-samples.sh`: the samples; `tools/WebCodecsCheck` (ManagedCodecs' access units and a hash per plane per
  frame), `tools/webcodecs/` (a page any browser opens from `serve.mjs`: `VideoDecoder` on those units, every plane
  compared, timed against `tools/WebCodecsWasm`, ManagedCodecs' decoder on wasm): the WebCodecs measurement (README
  "WebCodecs"), not in the checks or CI.
- `.github/actions/btbn-ffmpeg`: copied from shotdetector (BtbN's FFmpeg pinned by SHA-256); `.github/workflows/ci.yml`:
  pull requests (the checks their files need, `tools/ci-scope.sh`: none, native, page (native and the page in
  Chromium, interpreted) or all, which is interpreted on one sample and AOT with the page on all) and the whole of it daily on `main` (README "Checks");
  `.github/workflows/pages.yml`: on a push to `main`, the native, AOT (with the page) and replace checks, then the AOT check's own
  publish's `wwwroot/` plus FastYuv's license,
  `.nojekyll` and the demo clip (`demo/sintel-trailer.mp4`: Sintel's trailer, downloaded pinned by the same SHA-256 as
  `ci.yml` and made by `tools/make-samples.sh`, the file the checks run), checked for the two LGPL files and the clip,
  deployed to https://bisforboman.github.io/shotdetector-web/ only if all of it passes.
- `docs/backlog.md`: the work, in priority order, each row self-contained (Why, Where, Done when, Not, Status);
  `docs/backlog-done.md`: finished rows; `docs/decisions/`: design questions with their choices and answers
  (`2026-10-24-the-page.md`: how the page is built; `2026-10-24-fastyuv-replaceable.md`: FastYuv replaced by
  rebuilding; `2026-10-24-audio.md`: the audio decoded in the worker by ManagedCodecs; `2026-10-25-one-page.md`: one
  timeline and cards, a module per panel; `2026-10-26-workers.md`: long videos across several workers;
  `2026-10-26-new-ui.md`: the page's new UI, B chosen by the owner, built by the backlog row;
  `2026-10-30-resume-across-visits.md`: resuming a stopped run, the kept run shown at once, only the last kept, a long
  rest split, Firefox in CI;
  `2026-10-30-threads.md`: the decoder's threads on the page's thread, isolation by a service worker, the page offline;
  `2026-10-30-picture-measures.md`: the picture's measures, their cost measured and their check against FFmpeg;
  `2026-10-30-scope-widening.md`: fragmented MP4, rotation and a variable rate, what each needs and where;
  `2026-10-30-interlaced.md`: interlaced H.264, MBAFF woven as decoded, deinterlacing upstream first); `docs/mockups/`: static HTML
  mockups a decision refers to, nothing of the real page.

## Working on this repository

- Work is a row of `docs/backlog.md`: one row, one cloud session (the owner's choice, 2026-10-24: not on their
  machine), one pull request against `main`, CI green. Set its status to In progress when starting (with the date),
  move it to `docs/backlog-done.md` when it lands, and add a row when a gap turns up. A design question that the row doesn't settle goes to `docs/decisions/` before the code.
- `main` requires CI's `build` check, and auto-merge is on (the owner's choice, 2026-10-24). Once the PR is finished,
  with nothing left to push, run `gh pr merge <n> --auto --merge --delete-branch`: it merges when CI is green. Not
  earlier, or a green run merges half a row.
- Scope so far: 8-bit 4:2:0 H.264 in MP4 (fragmented too), constant frame rate, not rotated, progressive or interlaced
  as MBAFF frames (woven as decoded, not deinterlaced; field pictures unchecked: no sample); anything else
  is refused in `Mp4Input`'s constructor with a message saying what. Widening it means a sample for it in
  make-samples.sh and the checks passing on it.
- A new sample isolates one thing and is checked to really have it (ffprobe, `-bsf:v trace_headers`). ffmpeg tags its
  test sources BT.601 (bt470bg): an untagged sample needs `setparams=...=unknown`.
- Before pushing: run what the change could break, not everything: CI picks the checks from the pull request's files
  (`tools/ci-scope.sh`), and `pages.yml` and the daily run check all of it on `main`. Locally `tools/make-samples.sh`
  (once), then `tools/check.sh native` for the core, the page's defaults or NativeCheck, `tools/check.sh native page`
  for the page's own files or the smoke test, `tools/check.sh wasm` when the
  App's .NET side, a package or a wasm check changes, `tools/check.sh wasm aot` for anything that could differ under
  AOT. A new file the classifier doesn't name runs everything; give it a rule there if it shouldn't.
- Packages: ShotDetector/FastYuv 3.22.0, FrameReader 2.16.0 and ManagedCodecs 0.2.4 (Containers, H264, Aac, each
  pinning Core) from nuget.org, versions central in `Directory.Packages.props`. README, "Packages".
- FastYuv is LGPL-2.1: it stays its own DLL/.wasm (no single-file output), its COPYING.LGPL is copied into every output
  (the core csproj), and it is replaced by rebuilding with `-p:FastYuvAssembly=<dll>`, not by swapping the published
  file (`docs/decisions/2026-10-24-fastyuv-replaceable.md`; `tools/check.sh replace aot` in CI). Workflows pin actions by commit SHA with the tag in a comment, as shotdetector.
- Commit messages: what and why. Small commits.

## Gotchas

- `dotnet workload list` can show wasm-tools installed with no packs in `~/.dotnet/packs` (NETSDK1147 on build):
  `dotnet workload install wasm-tools` again installs them.
- A central package version (Directory.Packages.props) pins only the packages a project references: a transitive one
  resolves to the lowest its dependant allows, whatever the props say (FrameReader stayed 2.14.0 under a 2.15.0 line
  until the core referenced it, 2026-10-29). app-check.mjs compares the bundle's versions (`Versions()`) with the pins.
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
- Under wasm AOT the runtime loads some assemblies before `Main` that the interpreter never touches; interpreted,
  assemblies load on first use. FFmpeg.AutoGen was one until ShotDetector 3.22.0, whose frame-source path stopped
  reaching FFmpeg's code (shotdetector#597): the trimmer now drops it, `tools/check.sh` fails if a bundle has
  `FFmpeg.AutoGen.wasm`, and the wasm check's "no FFmpeg assembly loaded at all" holds under AOT too.
- A `[JSExport]`'s task that ends cancelled (an `OperationCanceledException` out of an `async` method) never settles
  the promise JavaScript holds (.NET 10, seen 2026-10-26): `Api.Detect` turns its token's cancellation into a
  `RunCancelledException`, which rejects it.
- A split run's Stop stops every worker at its next pause, each range ending where it got to (ShotDetector: "the range
  ends where the frames stop"), and `Mp4Input.FromStart` keeps the ranges while each reaches into the next one's first
  frame: the first range stopped short ends the merge, as a one-worker run stopped there. With ranges about equal,
  waiting for "finished" ranges only would keep nothing until the last minutes of a run.
- .NET's threaded wasm runtime (10.0.14) doesn't start in a Web Worker (it takes the worker for one of its pthreads,
  then asserts on thread ids), refuses to start without `SharedArrayBuffer`, and doesn't run under Node; on the main
  thread it refuses synchronous exports (`jsThreadBlockingMode` `PreventSynchronousJSExport`), runs `[JSImport]`s on
  the main thread (no `FileReaderSync` there), and refuses a synchronous one from inside a synchronous export (seen
  2026-10-30). Hence `Host.cs`. A `[JSImport]`'s `ArraySegment` there (a `MemoryView` pinned behind a GC handle)
  ended the runtime when the collector released it on another thread: a `Span` view, valid for the call only. ManagedCodecs' `H264Decoder` has no Dispose (its threads end after an idle timeout):
  frames that end early flush it.
- A stopped run is a file that ends there, for the detectors too: the adaptive ratio of the last frames (its window
  after them) is `None`, a cut FlashFilter's merge would decide on frames past the stop isn't there, and the threshold
  detector ends a fade-out the stop falls in with a cut (`PostProcess`), as at a file's end.
