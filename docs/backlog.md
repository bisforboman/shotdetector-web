# Backlog

What shotdetector-web does next, in priority order. Three sections: **Next** (open work, the first row first),
**Waiting** (rows that wait on the owner or an outside event) and **Maybe** (ideas not committed to). Update a
status when work starts or lands; add a row when a gap turns up, with why it matters, in Next or Maybe by its status.
A row moves to [backlog-done.md](backlog-done.md) when it is Done, Dropped or Won't do. Design questions go to
[decisions/](decisions/); the page's design is in
[decisions/2026-10-24-the-page.md](decisions/2026-10-24-the-page.md), which every row below assumes.

Each row is meant to be picked up cold, by an agent in a session of its own, as one pull request against `main` that
CI proves: **Why** it matters, **Where** it touches, **Done when** (what the PR shows, and which check proves it),
**Not** (what the row must leave alone), **Status**. Rows that depend on another say so; the rest can run in parallel.
Before pushing: `tools/check.sh` (README, Checks); README and CLAUDE.md say what changed. Commit messages: what and
why, small commits.

Status: **Open**, **In progress**, **Maybe**; finished rows (in backlog-done.md): **Done** (on `main`), **Dropped**,
**Won't do**.

## Next

### Unblocked by ShotDetector 3.15.0

This waited on shotdetector#492, which shipped in ShotDetector 3.15.0 (2026-10-25). It changes `Api.Detect` and
`worker.js`, so it runs after the page's structure row.

- Cancel without restarting the worker
  - Why: Cancel terminates the worker today, so the runtime starts again and the file is opened again: a second or
    two on a trailer, plus opening the file again (streamed since "Large files": seconds on a film). The owner chose (2026-10-24)
    cooperative cancelling through an asynchronous frame source upstream. Decision "Cancelling a run".
  - Where: ShotDetector 3.15.0 has `ShotDetection.DetectAsync(YuvVideo, IAsyncEnumerable<YuvFrame>, DetectionOptions?,
    CancellationToken)` ([shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492)): it runs on the
    caller's own awaits, with no other thread, and gives the synchronous `Detect`'s result. Here: an asynchronous `Mp4Input` frame source that yields to the
    worker's event loop every N frames, so a `cancel` message can run and cancel the token. On single-threaded wasm
    the yield must be a real macrotask: a `[JSImport]` returning a Promise resolved by `setTimeout(0)` or a
    `MessageChannel`, not `Task.Yield()`. `Api.Detect` becomes `async Task<string>` (a Promise to the worker).
    `worker.js` answers `cancel` with a `cancelled` message (an `OperationCanceledException` is not an `error`).
    `app.js` cancels with a message and falls back to `terminate()` when no `cancelled` comes within a second or so.
    `tools/app-check.mjs` calls the new export. A cancelled run's shots (the page keeps those it was told, "Shots as
    they're found", done) get a scenes CSV: `Api` keeps the shots it was told and makes it from them with `Shots.Csv`,
    and `cancelled` carries it (no partial stats CSV); `app.js` offers it as the download, the unfinished line not in it.
  - Done when: on the Sintel trailer, a run cancelled mid-way is followed at once by a run with another threshold,
    without opening the file again; the yields cost under 1% of the run's time (pick N for it); checks green, CSVs
    unchanged.
  - Not: no shared memory. A cancelled run throws and keeps no result, upstream too; its shots are the ones told.
  - Depends on: the 3.15.0 row and large files (both edit `Api` and `worker.js`); after "Shots as they're found"
    (done).
  - Status: Open

### Decisions to make first

The owner chose (2026-10-26) to take these two up, each with its design question answered before anything is built.

- Long videos across several Web Workers
  - Why: [#19](https://github.com/bisforboman/shotdetector-web/issues/19). ShotDetector 3.16.0's
    `ShotDetection.ScoreRange`/`ScoreRangeAsync` scores a range of frames on a runtime of its own, and
    `ShotDetection.Merge` merges the ranges' `RangeScores` (carried as bytes, `ToBytes`/`FromBytes`) into exactly
    the result `Detect` gives for the whole video. Workers need no shared memory, so this works on Pages, unlike wasm
    threads. A film could run several times faster on a machine with several cores.
  - Where: first a decision in `docs/decisions/` with choices for each question, the owner's to answer:
    - **Live shots and cancel:** shots exist only after `Merge`. Keep the live table for one worker and show
      progress per range for several? Merge what's done on cancel?
    - **Memory:** each worker is a runtime of its own (about 13 MB to start) plus its range's share.
    - **How many workers:** from `navigator.hardwareConcurrency`, and from what length on.
    - **Where ranges start:** at keyframes, from "Packets in the core".
    Then, once answered, the build: the page starts the workers, each opens the same `File`, and `tools/app-check.mjs`
    checks that a run split into ranges gives the same CSVs as one worker, on every sample.
  - Done when: the decision is answered and merged, then the build's PR shows the same CSVs split and whole, and the
    speed on the two-hour film with 1, 2 and 4 workers.
  - Depends on: the 3.16.1 row (done, 2026-10-26).
  - Status: Open (decision first)

- WebCodecs: are its frames ManagedCodecs' frames?
  - Why: [#15](https://github.com/bisforboman/shotdetector-web/issues/15). Decoding is about 80% of a run on wasm,
    and the browser's own `VideoDecoder`, often in hardware, could make a run 2.5 to 4 times faster. The page's
    design ruled it out ("Not doing": its output may differ between browsers and from FFmpeg's). The owner reopened
    that question (2026-10-26), proof first. H.264's decoding is defined exactly by the standard, so the same frames
    are plausible but not yet shown.
  - Where: measure only. Decode every sample, the Sintel trailer and the two-hour film with WebCodecs in headless
    Chromium and Firefox, copy each frame's Y, U and V planes out (`VideoFrame.copyTo`, I420), and compare them with
    ManagedCodecs' frames for the same file, frame by frame (a hash per plane, and the first differing frame and
    position where they differ). Also time the decode and the copy against ManagedCodecs on wasm AOT. A tool under
    `tools/`, not in the page. Write the numbers and the verdict in the decision's "Not doing" section.
  - Done when: a table per browser and sample, either "every frame identical" or where it differs, and the timings.
    The decision changes only if every frame is identical in both browsers; a later row would then build it, with
    ManagedCodecs kept wherever WebCodecs isn't there or differs.
  - Not: no change to the page or the checks.
  - Status: Open (measure first)

### First release: the page on GitHub Pages

The first release's rows are all done ([backlog-done.md](backlog-done.md)).

### After the first release

- Real-browser smoke test
  - Why: the page is JavaScript around checked exports; once visitors use it, a change to `app.js` or `worker.js`
    needs a check of its own. Decision "Testing the page".
  - Where: a CI job (Linux, Playwright, Chromium only) that serves the published `wwwroot`, loads the page, sets the
    file input to one sample, runs with the defaults, downloads the scenes CSV and compares it with `out/native/`'s;
    `tools/page-check.mjs` or similar, run by `tools/check.sh page`. Pin Playwright's version.
  - Done when: the job fails when `app.js` breaks the download and passes on `main`.
  - Not: no cross-browser matrix, no visual tests.
  - Status: Open

- Thumbnails per shot
  - Why: a table of times is hard to read; a still per shot shows what was found. Decision "Pictures of shots".
  - Where: `app.js`: for each shot, seek the `<video>` to the shot's first frame, `drawImage` to a small canvas,
    lazily as rows scroll into view (one seek at a time; `seeked` events).
  - Done when: a trailer's shots each show a still within a few seconds of the run finishing, and the detection
    result is unchanged (the stills are the browser's decode, not the library's).
  - Not: no second decode in .NET, no image export.
  - Status: Open

The rows below add to what the page shows (the owner's choice, 2026-10-24). They widen the decision's "The page
itself", which lists the first release's page, not every page there will be. Since the page's structure row
(decision [2026-10-25-one-page.md](decisions/2026-10-25-one-page.md)), each is a lane of the timeline or a card, in a
module of its own, so they run side by side once that row has landed. Where a row says "under the player" or "on the
shots strip", read a lane of the timeline. The waveform, audio measures and bitrate rows keep only their export and
panel; their core parts are "Audio in the core" under "Now" and "Packets in the core" (done). Whatever the page
derives for display is labelled as the page's own; the CSVs stay the only output the exactness check speaks for.

- Warnings on the page
  - Why: `DetectionResult.Warnings` says what the user should know about a run, and `Api.Detect` drops it today.
  - Where: `src/ShotDetectorWeb.App/Api.cs`: `warnings` (the strings as they are) added to `DetectResponse`;
    `app.js` shows them under the summary when there are any.
  - Done when: a run that produces a warning shows it on the page (find which input does, or say none of the samples
    does and show it with a made-up response in the PR); `tools/check.sh wasm` and `wasm aot` green, CSVs unchanged.
  - Not: no new warnings of our own.
  - Status: Open

- About this file
  - Why: the library describes the file as FFmpeg would without FFmpeg (README, the `YuvVideo` table), and `Open`
    already returns that description as JSON, but the page shows none of it.
  - Where: `app.js`, `index.html`, `style.css`: after opening, a small block with the codec, width and height, frame
    rate, pixel format, colour space and range, frame count and duration, in the words a viewer knows (say "BT.709",
    not the enum's name), the raw JSON one click away. Also the audio track's codec, sample rate and channels, or "no
    audio": `Open` adds them from the demuxer's `AudioStreamInfo`.
  - Done when: each sample in `tools/make-samples.sh` shows what its name says (25, 29.97 and 23.976 fps, the colour
    tags, full range, the cropped 854x480, the audio track where there is one); screenshots in the PR.
  - Not: no other fields beyond what the core returns.
  - Status: Open

- A graph of the scores
  - Why: scenedetect's users read the stats as a graph: each frame's score over time against the threshold shows why
    a cut was found and what a different threshold would do. The stats are computed for every run already
    (`statsCsv`), and the page only offers them as a download.
  - Where: `app.js`, `index.html`, `style.css`: a `<canvas>` graph of the run's score column over time, with the
    threshold as a line and the cuts marked, a click seeking the `<video>`, readable at phone width. The columns
    differ per detector (`content_val`, `delta_*`, `adaptive_ratio…`, and more for the others), so pick them by the
    CSV header, not by position, and say in the row's PR which column each detector's graph shows. Where a
    frame-difference score (`content_val`, `delta_*`) stays near zero for a second or more, mark the span as a still
    picture, labelled as the page's reading.
  - Done when: each of the five detectors draws its graph on the Sintel trailer, every cut sits on a score above the
    threshold (or the PR says why it doesn't, for detectors that also use the minimum shot length); screenshots in the PR.
  - Not: no charting library; no re-thresholding in JavaScript (a new threshold is a new run: detecting twice in two
    languages is how results drift).
  - Status: Open

- Bitrate and keyframes
  - Why: the bitrate rises at cuts and encoders put keyframes at scene changes, so a bitrate graph and keyframe marks
    on the shots strip show the encoder's own view of the cuts next to ShotDetector's. It needs no decoding: each
    MP4 packet carries its size, its time and whether it is a keyframe.
  - Where: the core keeps, for each video packet, its pts, its size and its keyframe and discard flags, during the
    walk over every packet `Mp4Input` already makes when it opens a file: `Mp4Input.Packets` (done, "Packets in the
    core"; README "Using the library"). The App returns them from an export of their own, as compact arrays (leave
    out or mark the discarded packets: an edit list's, decoded but not shown). `app.js` draws bitrate per second as a
    graph under the strip, puts keyframe ticks on the strip, and says how many cuts fall on a keyframe (labelled as
    the page's reading).
    `tools/NativeCheck` already compares the list with `ffprobe -show_packets` (pts, size, the K and D flags),
    exactly (`PacketCheck.cs`). Depends on "Shot statistics and a timeline".
  - Done when: the check is green on every sample, and the Sintel trailer's graph and ticks show on the page
    (screenshot in the PR).
  - Not: no I, P and B frame types (`VideoFrame` doesn't carry them; that is a Maybe); no audio bitrate.
  - Status: Open

- Trimming: FFmpeg.AutoGen and the unused parts of ShotDetector out of the bundle
  - Why: nothing on this path uses FFmpeg.AutoGen, yet it ships (the trimmer keeps whole assemblies that don't opt in:
    ShotDetector's package isn't marked `IsTrimmable`), and THIRD-PARTY-NOTICES.md has to list it.
  - Where: measure what it costs (the bundle size row's numbers); try `TrimMode=full` on the App and see what breaks
    (`Shots.Json` uses `JsonSerializer`: reflection-based serialization needs a source-generated context or
    `JsonSerializerIsReflectionEnabledByDefault`); otherwise an upstream change in shotdetector (`IsTrimmable`, the
    trim warnings fixed) and then this row. `tools/check.sh wasm aot` is the proof either way; the
    "FFmpeg not loaded" check turns into "FFmpeg not bundled". Update THIRD-PARTY-NOTICES.md.
  - Done when: `_framework/` has no `FFmpeg.AutoGen.wasm`, the checks are green, and the README's sizes are updated.
  - Not: no change to the core library.
  - Status: Open

## Waiting

The asks filed upstream on 2026-10-24 all shipped on 2026-10-25:
[shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492) and
[#493](https://github.com/bisforboman/shotdetector/issues/493) in ShotDetector 3.15.0 (`DetectAsync` over an
asynchronous frame source, `DetectionOptions.OnShot`), [#494](https://github.com/bisforboman/shotdetector/issues/494)
in FrameReader 2.11.0 (`ReferenceToneFinder`, `WaveformBuilder`). Their rows are under Next.

- ShotDetector: the stats CSV written row by row
  - Why: the half of this that stored the stats by column shipped in ShotDetector 3.16.1
    (bisforboman/shotdetector#512): on a two-hour film the stats keep 11.3 MB instead of 64.0 (65 B a frame instead of
    370), and `Stats.Csv` no longer grows the wasm heap there (208 MB, where the run leaves it, instead of 432; README
    "What the heap holds", 2026-10-26). The CSV is still built as one string: 20 million characters, 175 MB allocated
    (about 1 KB a frame), still the largest allocation at the end of a run, which fit in the room the heap already had
    on that film.
  - Where: upstream (bisforboman/shotdetector), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23) less the columns: `Stats.WriteCsv(TextWriter,
    position)` writing one row at a time. Then here: `Api.StatsCsv` writes through it into a `Blob` in pieces (a
    `[JSImport]` the worker gives), so the whole CSV is never one string.
  - Status: Waiting on the owner to file it (2026-10-25; the columns shipped in 3.16.1 on 2026-10-25)

- ManagedCodecs: the MP4 index without its copies
  - Why: `Mp4Demuxer` allocates about 275 B a sample to index a file and keeps 56 (140 MB and 28.5 MB for a two-hour
    film with audio): it takes the wasm heap from 34 to 174 MB when a file opens, 208 MB once a run builds its own
    (README "What the heap holds", 2026-10-26).
  - Where: upstream (bisforboman/ManagedCodecs), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23): each track's samples filled once, a smaller
    `Sample`, optionally only the tracks asked for. Then here: the submodule bumped, the steps measured again.
  - Status: Waiting on the owner to file it (2026-10-25)

- ManagedCodecs' license
  - Why: THIRD-PARTY-NOTICES.md says the submodule's pinned commit has no LICENSE file; the page ships its code to
    every visitor and should say under what terms.
  - Where: upstream (bisforboman/ManagedCodecs): a LICENSE file; then here: bump the submodule, update
    THIRD-PARTY-NOTICES.md and the page's footer.
  - Status: Waiting on the owner (2026-10-24)

- ManagedCodecs from NuGet instead of the submodule
  - Why: a package pins a version in Directory.Packages.props like the others, and a clone needs no `--recursive`.
  - Where: when it is on nuget.org: replace the two project references in the core csproj with a package reference,
    delete `external/` and `.gitmodules`, update README "Packages" and CLAUDE.md, `ci.yml`'s `submodules: true`.
  - Status: Waiting on upstream (2026-10-24: not on NuGet)

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`, 3.14.0) takes 8-bit 4:2:0 of an even
height at a constant frame rate, upright, and refuses `Deinterlace` and `Yadif`: most rows below need a change there
first, named in each. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- MJPEG in AVI
  - Why: ManagedCodecs' MJPEG decoder is bit-exact with FFmpeg and its AVI demuxer gives FFmpeg's timestamps; 4:2:0
    JPEG comes out yuv420p/yuvj420p, which the frame-source API takes today. Cameras and old footage.
  - Where: an `AviInput` beside `Mp4Input` describing the file as FFmpeg does (frame rate from the AVI header, no
    time base subtleties); `Demuxer.Open` to pick by content; the page's file filter. 4:2:2 JPEG waits for the 4:2:2
    row.
  - Status: Maybe

- Interlaced H.264
  - Why: broadcast material. ManagedCodecs decodes fields and MBAFF; ShotDetector.FastYuv has the row-wise `Yadif`
    the FFmpeg path uses in-process.
  - Where: first upstream: the frame-source API accepting `Yadif` (today an `ArgumentException`); then here, the
    `FrameMbsOnly` check in `Mp4Input` goes, a sample with `-flags +ilme+ildct`.
  - Status: Maybe (needs ShotDetector)

- 10-bit 4:2:0 and 8-bit 4:2:2
  - Why: ManagedCodecs decodes both; FastYuv converts both (`SwscaleYuv420P10`, its 4:2:2 path) with x86's results.
  - Where: first upstream: `YuvFrame` for 16-bit planes and 4:2:2 chroma, and `YuvVideo.PixelFormat` accepting
    yuv420p10le / yuv422p; then here, the chroma and bit-depth check in `Mp4Input`, two samples (already in
    `samples/reject/`).
  - Status: Maybe (needs ShotDetector)

- Fragmented MP4, variable frame rate, rotation
  - Why: screen recordings and phones. ManagedCodecs demuxes fragmented files; FFmpeg's `nb_frames` is 0 for them, so
    the description differs (`Mp4Header` counts `moof` samples, `FrameCount` null, the length from the duration as
    OpenCV's). A variable rate is an error in the frame-source API (a frame more than half a frame off) and in
    scenedetect's model. Rotation: FFmpeg's path reports it and leaves frames upright; the frame-source API applies
    none.
  - Where: each its own row when wanted; each needs a decision about what "the FFmpeg path's result" means for it.
  - Status: Maybe

- Threads in the browser
  - Why: ManagedCodecs decodes a 720p P chain in about half the time on 4 threads (`H264Decoder.Threads`). Wasm
    threads need `SharedArrayBuffer`, so COOP/COEP headers, which Pages can't set; a service worker that adds them to
    its own responses (the `coi-serviceworker` trick) can. `DetectionMode.Parallel` doesn't apply to a frame source
    (it reads once, in order), so only decoding would speed up.
  - Where: the service worker, `WasmEnableThreads`, `Threads` set from `navigator.hardwareConcurrency`, the check that
    results don't change (`tools/check.sh wasm aot`). Decision "Threads".
  - Status: Maybe

- Offline (a service worker caching the bundle)
  - Why: once a service worker exists for threads, caching the bundle is a few lines and the page works on a plane.
  - Status: Maybe

- Shots as JSON and HTML
  - Why: `Shots.Json` and `Shots.Html` exist; two more download buttons.
  - Status: Maybe

- Picture measures: black frames, brightness, broadcast-safe levels, letterbox, colour bars
  - Why: the picture side of a check before delivery, next to the audio measures: where it goes to black, how bright
    each frame is, whether luma leaves 16 to 235, whether there are black bars, whether there are colour bars at the
    head (with the reference tone, "bars and tone"). Each one has an FFmpeg filter to compare with (`blackdetect`,
    `signalstats`, `cropdetect`).
  - Where: a pass over each frame's Y plane as `Mp4Input.Frames` yields it, alongside detection, so it adds time to
    every run (measure how much before deciding). The owner filed it as a Maybe (2026-10-24).
  - Status: Maybe

- Frame types (I, P, B) on the strip
  - Why: shows the encoder's structure (GOPs, where the B-frames go) next to the bitrate and keyframes.
  - Where: first upstream: ManagedCodecs' `VideoFrame` doesn't say what kind of picture it is (`SliceHeader.Type`
    exists inside the decoder); then here, after "Bitrate and keyframes".
  - Status: Maybe (needs ManagedCodecs)

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output may differ between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Reopened for measurement (2026-10-26): "WebCodecs: are its frames ManagedCodecs' frames?" under Next
