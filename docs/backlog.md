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

- Stop a run and keep what was read, without restarting the worker
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
    `tools/app-check.mjs` calls the new export.
    **Stop keeps what was read** (the owner's question, 2026-10-26: stats for a stopped run). The page's button
    becomes Stop: at the next yield the frame source simply ends, as if the file ended there, instead of cancelling
    the token. `DetectAsync` then finishes normally with the result for the frames read: the shots, the scenes CSV and
    the stats CSV, shown and offered for download like a finished run's, labelled "stopped at" the time reached. The
    token stays for a runtime that has to be abandoned, and `terminate()` for one that doesn't answer.
  - Done when: on the Sintel trailer, a run stopped mid-way shows its shots, its cards and both downloads, and a run
    with another threshold follows at once without opening the file again. `tools/app-check.mjs` stops a run at a
    frame N on every sample and checks that its stats CSV equals the first rows of the full run's up to N (each frame's
    stats depend only on the frames before it), and that its shots are the full run's up to the last cut before N,
    plus one ending at N. Find out from ShotDetector's source whether a frame source that ends before
    `YuvVideo.FrameCount` gives a warning, and show it if so. The yields cost under 1% of the run's time (pick N for
    it); checks green, CSVs unchanged.
  - Not: no shared memory.
  - Depends on: the 3.15.0 row and large files (both edit `Api` and `worker.js`); after "Shots as they're found"
    (done).
  - Status: Open

- Pause and resume a run
  - Why: the owner asked (2026-10-26) to resume after stopping. While the page stays open, that is a pause: the run
    stays in memory and goes on where it was.
  - Where: the asynchronous frame source from "Stop a run and keep what was read": a `pause` message makes it wait,
    at its next yield, on a promise that a `resume` message settles. `DetectAsync` simply waits meanwhile; nothing is
    cancelled or ended. `app.js`: a Pause button beside Stop while a run goes, then Resume; the progress bar and the
    frames-per-second figure hold still; live shots stay as they were told. Stop works while paused too.
  - Done when: on the Sintel trailer, a run paused mid-way and resumed gives exactly the CSVs of a run never paused
    (`tools/app-check.mjs` pauses and resumes at a frame on every sample, and compares); in headless Chromium and
    Firefox the buttons and the bar behave (screenshots in the PR).
  - Not: nothing kept across a reload (the next row); no change to the result.
  - Depends on: "Stop a run and keep what was read".
  - Status: Open

### Decisions to make first

The owner chose (2026-10-26) to take these two up, each with its design question answered before anything is built.

- Long videos across several Web Workers
  - Why: [#19](https://github.com/bisforboman/shotdetector-web/issues/19). ShotDetector 3.16.0's
    `ShotDetection.ScoreRange`/`ScoreRangeAsync` scores a range of frames on a runtime of its own, and
    `ShotDetection.Merge` merges the ranges' `RangeScores` (carried as bytes, `ToBytes`/`FromBytes`) into exactly
    the result `Detect` gives for the whole video. Workers need no shared memory, so this works on Pages, unlike wasm
    threads. A film could run several times faster on a machine with several cores.
  - Where: decision [2026-10-26-workers.md](decisions/2026-10-26-workers.md), answered by the owner on 2026-10-26:
    progress per range with the shots at the end (one worker keeps live shots); Stop merges the ranges finished from
    the start; several workers above about 10 minutes of video, up to `navigator.hardwareConcurrency` − 1 and at most
    4, each range starting at a keyframe from the packet list; the audio pass in a worker of its own, in parallel.
    The page starts the workers, each opens the same `File`, and `tools/app-check.mjs` checks that a run split into
    ranges gives the same CSVs as one worker, on every sample, and that a stopped split run gives the result for its
    finished start.
  - Done when: the same CSVs split and whole; the speed and the heap on the two-hour film with 1, 2 and 4 workers,
    and the decision's 10 minutes and 4 adjusted to what was measured.
  - Depends on: "Stop a run and keep what was read" (it drives runs through `DetectAsync`).
  - Status: Open (decided 2026-10-26)

- Resume after Stop, across visits
  - Why: the owner asked (2026-10-26) to resume a run after stopping it, with the stopped part's stats shown in
    between. ShotDetector's `ScoreRange` says "the range ends where the frames stop", so a stopped run is a range that
    ended early. Scoring a second range from where it stopped and calling `Merge` on both gives exactly the result for
    the whole video. `RangeScores.ToBytes` makes the stopped part's scores storable, so a later visit can resume too.
  - Where: built with the workers row, on the same `ScoreRangeAsync` and `Merge`:
    - **At Stop:** `Merge` of the ranges so far shows the result for the part read (find out from ShotDetector's source
      that `Merge` takes ranges that end before the video does, as the workers decision's Stop already assumes).
    - **Resume:** a range from the last frame read (decoding from the keyframe before it and dropping frames up to the
      one before), then `Merge` over all ranges.
    - **Kept in the browser:** the scores' bytes in IndexedDB, keyed by the file's name, size and modified time and by
      the options, behind try/catch; "Resume" offered when the same file and options come back, and forgotten on a
      finished run or a click.
    - **Live shots:** `ScoreRange` tells no shots (`OnShot` isn't honoured there: shots are decided in `Merge`), so a
      one-worker run built this way would lose its live table. Ask upstream for `OnShot` on a range starting at frame 0
      (its shots up to the range's end are what `Detect` would tell), with the owner's OK to file it. Until it ships,
      a run on `DetectAsync` keeps no scores to merge, so resuming it would mean scoring its start again: run one-worker
      runs through `ScoreRangeAsync` with progress but no live table, or leave them unresumable. Measure both and put
      the choice to the owner in the PR.
  - Done when: `tools/app-check.mjs` stops at a frame on every sample, resumes, and gets exactly the full run's CSVs;
    the same across a page reload in headless Chromium and Firefox; screenshots in the PR.
  - Not: nothing sent anywhere (the scores stay in the browser); no change to the result.
  - Depends on: "Long videos across several Web Workers".
  - Status: Open

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

- The form's default thresholds for histogram and hash
  - Why: `app.js`'s `DEFAULT_THRESHOLDS` prefills histogram 0.05 and hash 0.395, scenedetect 0.7.1's defaults, and
    sends a threshold equal to its default as none. ShotDetector's defaults follow `DetectionOptions.Compatibility`,
    0.7.2 unless set, whose are 0.2 and 0.35 (`ShotDetection`'s detector setup). So the form says 0.05 while the run
    compares against 0.2, and a visitor who types 0.05 gets 0.2. Found 2026-10-26 by "A graph of the scores": the
    lane draws the threshold the run used, and Sintel's histogram cuts all sit under 0.8, not 0.95.
  - Where: `app.js`'s `DEFAULT_THRESHOLDS` (histogram 0.2, hash 0.35) and its comment; `panels/scores.js` keeps the
    same defaults to draw the line (its `DETECTORS`).
  - Done when: the form prefills 0.2 and 0.35, and a histogram run with 0.05 typed in compares against 0.05 (the
    scores lane's line at 0.95, and more cuts than the default's on the Sintel trailer).
  - Not: no other compatibility than ShotDetector's default.
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
  - Status: Waiting on upstream ([shotdetector#519](https://github.com/bisforboman/shotdetector/issues/519), filed
    2026-10-26; the columns shipped in 3.16.1 on 2026-10-25)

- ManagedCodecs: the MP4 index without its copies
  - Why: `Mp4Demuxer` allocates about 275 B a sample to index a file and keeps 56 (140 MB and 28.5 MB for a two-hour
    film with audio): it takes the wasm heap from 34 to 174 MB when a file opens, 208 MB once a run builds its own
    (README "What the heap holds", 2026-10-26).
  - Where: upstream (bisforboman/ManagedCodecs), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23): each track's samples filled once, a smaller
    `Sample`, optionally only the tracks asked for. Then here: the submodule bumped, the steps measured again.
  - Status: Waiting on upstream ([ManagedCodecs#138](https://github.com/bisforboman/ManagedCodecs/issues/138), filed
    2026-10-26)

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
