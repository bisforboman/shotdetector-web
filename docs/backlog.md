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

### First release: the page on GitHub Pages

The first release's rows are all done ([backlog-done.md](backlog-done.md)).

### After the first release

The rows below add to what the page shows (the owner's choice, 2026-10-24). They widen the decision's "The page
itself", which lists the first release's page, not every page there will be. Since the page's structure row
(decision [2026-10-25-one-page.md](decisions/2026-10-25-one-page.md)), each is a lane of the timeline or a card, in a
module of its own, so they run side by side once that row has landed. Where a row says "under the player" or "on the
shots strip", read a lane of the timeline. The waveform, audio measures and bitrate rows keep only their export and
panel; their core parts are "Audio in the core" under "Now" and "Packets in the core" (done). Whatever the page
derives for display is labelled as the page's own; the CSVs stay the only output the exactness check speaks for.

- Picture measures: black frames, brightness, broadcast-safe levels, letterbox, colour bars
  - Why: the picture side of a check before delivery, next to the audio measures: where it goes to black, how bright
    each frame is, whether luma leaves 16 to 235, whether there are black bars, whether there are colour bars at the
    head (with the reference tone, "bars and tone"). Each one but the bars has an FFmpeg filter to compare with
    (`blackdetect`, `signalstats`, `cropdetect`).
  - Where: a pass over each decoded frame as `Mp4Input.Frames` yields it, alongside detection; a lane and a card
    (`mark: 'measured'`), and a NativeCheck comparison with FFmpeg's filters, as `AudioCheck.cs`. Measured and laid out
    in decision [2026-10-30-picture-measures.md](decisions/2026-10-30-picture-measures.md) (about 2% of a run, natively
    and AOT; the prototype's figures FFmpeg's on every frame); which measures, where they run and how strictly they are
    checked are the owner's answers there.
  - Done when: the measures chosen are on the page from a run, split runs and stopped runs included, and NativeCheck
    and app-check.mjs compare them with FFmpeg's on every sample as the decision says.
  - Not: the CSVs and the shots (the pass only reads the frames).
  - Status: In progress (2026-10-30). The owner chose it from Maybe on 2026-10-30.

## Waiting

The asks filed upstream on 2026-10-24 all shipped on 2026-10-25:
[shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492) and
[#493](https://github.com/bisforboman/shotdetector/issues/493) in ShotDetector 3.15.0 (`DetectAsync` over an
asynchronous frame source, `DetectionOptions.OnShot`), [#494](https://github.com/bisforboman/shotdetector/issues/494)
in FrameReader 2.11.0 (`ReferenceToneFinder`, `WaveformBuilder`); those filed on 2026-10-26 shipped in ShotDetector
3.20.0 and ManagedCodecs 0.1.0 (2026-10-27 and 2026-10-28). Their rows are done ([backlog-done.md](backlog-done.md)), but
ManagedCodecs#138's optional part, the row below.

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
  - Status: Waiting on upstream ([shotdetector#598](https://github.com/bisforboman/shotdetector/issues/598),
    `OnShot` on a range from frame 0; the owner chose to wait for it, decision `2026-10-30-resume-across-visits.md`)

- ManagedCodecs: a demuxer of the tracks asked for, packets without their data
  - Why: ManagedCodecs 0.1.0's `Mp4Demuxer` still indexes video and audio whatever its caller reads, and `ReadPackets`
    allocates every packet's data even where the stream skips the reads. On the two-hour control natively, 342 MB of
    zeroed arrays come out of `Open`'s 405 MB allocated, and a run allocates and throws away the audio track's 115 MB.
    It doesn't grow the wasm heap: both were tried in a patched local build, 84 MB opened and 121 MB after the run either
    way (README "What the heap holds", 2026-10-29). So low priority: allocation and time, not memory.
  - Where: upstream (bisforboman/ManagedCodecs), filed as #211 from the text in
    [#59](https://github.com/bisforboman/shotdetector-web/pull/59), a follow-up to
    [ManagedCodecs#138](https://github.com/bisforboman/ManagedCodecs/issues/138). Then here: the run's demuxer
    (`Mp4Input.DecodedFrames`, the ranges') of the video track only, and the opening pass through the timing-only
    read, measured again as README does.
  - Done when: a ManagedCodecs release has both, and the page uses them with the checks unchanged.
  - Not: no copy of the demuxer here (CLAUDE.md: report it there, don't work around it); stays on 0.1.0
    (ManagedCodecs#206).
  - Status: Waiting on upstream ([ManagedCodecs#211](https://github.com/bisforboman/ManagedCodecs/issues/211), filed
    2026-10-30)

- ManagedCodecs 0.2.0 on wasm: other pixels under the interpreter
  - Why: 0.2.0 (nuget.org, 2026-10-28 18:28 UTC, 97 commits past 0.1.0, nearly all H.264 decoder speed work in
    `Vector128` paths: deblocking a macroblock at a time, edge strengths and the residual add in vectors) is what the
    page should run, for its frames per second. Tried on 2026-10-29 with the pins otherwise as `main`'s (FrameReader
    2.15.0): natively every check agrees as with 0.1.0. On browser-wasm it doesn't:
    - Interpreted (`tools/check.sh wasm`): every sample's stats CSVs differ from the native ones, the scenes CSVs don't
      (the first sample, baseline-480x270-23976-bt601.mp4, from row 6 on, e.g. row 6's delta_edges 5.340 for 5.337);
      and app-check's stopped run, the same file decoded again in the same runtime, differs from its own whole run
      from row 2 on, the first P frame, by far more (row 4's delta_edges 9.72 for 5.74): the decoder's output varies
      from one decode to the next, which smells of samples read before they are written (`VideoFramePool`'s planes
      are `GC.AllocateUninitializedArray`, the new paths' stackallocs are under `[SkipLocalsInit]`) rather than a
      wrong but deterministic intrinsic. Published with `-p:WasmEnableSIMD=false` (so
      `Vector128.IsHardwareAccelerated` false, the scalar paths), the same two samples' CSVs equal the native ones.
    - Ahead of time (`tools/check.sh wasm aot`): 132 of 132 CSVs identical, so the pixels are right there. Its speed
      against 0.1.0's is not settled: the runs took 227 s for 0.1.0's 207 s the same day on a shared cloud machine
      whose 0.1.0 runs had taken 135 s the day before (Sintel's trailer, adaptive: 23.1 s for 21.5; all detectors:
      30.8 s for 25.1), interpreted 1,029 s for 1,290; a quiet machine, or the page's own frames per second, would
      say.
    ManagedCodecs' own wasm check (its ci.yml's `wasm` job, `tools/wasm-check.sh`) runs ahead of time only, so it
    can't see the interpreted fault.
  - Where: upstream (bisforboman/ManagedCodecs), filed as #206 with the above: (1) the H.264
    decoder's output under the Mono interpreter on browser-wasm with SIMD, wrong and varying between decodes, right
    with SIMD off and compiled ahead of time. To reproduce here: ManagedCodecs 0.2.0 in Directory.Packages.props,
    `tools/check.sh wasm` (the samples from `tools/make-samples.sh`). Then here: the bump, with the page's frames per
    second measured again on a quiet machine (README "Long videos across workers"), 0.2.0 against 0.1.0.
  - Done when: a ManagedCodecs release decodes every sample to the native CSVs interpreted as well as ahead of time
    (`tools/check.sh`, `tools/check.sh wasm aot`), and its speed on wasm is measured against 0.1.0's.
  - Not: no work-around here (CLAUDE.md: a bug in ManagedCodecs is reported there); `WasmEnableSIMD` stays on.
  - Status: Waiting on upstream ([ManagedCodecs#206](https://github.com/bisforboman/ManagedCodecs/issues/206), filed
    2026-10-29)

- Trimming: FFmpeg.AutoGen and the unused parts of ShotDetector out of the bundle
  - Why: nothing on this path uses FFmpeg.AutoGen, yet it ships, and THIRD-PARTY-NOTICES.md has to list it. Measured
    2026-10-29 (ShotDetector 3.20.0, FrameReader 2.15.0): the App's publish already trims every assembly in full
    (`TrimMode` is `full` for browser-wasm; ShotDetector and FrameReader are `IsAotCompatible`, so trimmable, and the
    publish has no trim warnings). FFmpeg.AutoGen comes out at 447 KB of its 648 because ShotDetector's code *reaches*
    it: the frame-source and merge paths the page runs share method bodies with the file path. `ShotDetection.Steps`
    (the iterator behind `RunAsync`, `DetectAsync` and `Merge`) constructs `VideoReader` from a path or a stream and
    calls `FindReferenceTone(path)`, and `VideoReader`'s members branch between in-process decoding and the caller's
    frames (`Frames` → `FramesInProcess`, `TakeTimestampPass` → `InProcessProbe.FrameTimestamps`, `DecodesWithLibraries`,
    `HasFrameThreads`). From the trimmer's dependency dump (`-p:_TrimmerDumpDependencies=true`), those six members are
    every bridge: stubbed with ILLink substitutions (a local measurement, not committed), `FFmpeg.AutoGen.wasm` is gone,
    `tools/check.sh wasm aot` passes (154 of 154 CSVs identical, "No FFmpeg assembly loaded at all." under AOT too),
    and the AOT publish's `_framework/` through `gzip -6` goes from 19.19 MB to 16.31 MB raw (−2.88 MB, 15%) and from
    5.76 to 5.14 MB compressed (−620 KB, 10.8%): `dotnet.native.wasm` −2.31 MB raw (−478 KB; the AOT code of the
    FFmpeg paths), `FFmpeg.AutoGen.wasm` −447 KB (−103 KB), FrameReader 91 to 20 KB, ShotDetector 256 to 222 KB.
  - Where: upstream (bisforboman/shotdetector), filed as #597 with the above: the frame-source entry
    points (`Detect`/`DetectAsync`/`RunAsync` over `YuvFrame`s, `ScoreRangeAsync`, `Merge`) should not reach
    FFmpeg-only code statically, so a trimmed app that never decodes with FFmpeg doesn't ship FFmpeg.AutoGen; e.g.
    `Steps` takes its `VideoReader` from the `frames` factory always (the path and stream entries passing theirs) and
    the reference-tone trim is decided by that entry, and `VideoReader`'s in-process members move behind the file
    path's own type. A check upstream could hold it: benchmarks/wasm's publish with no `FFmpeg.AutoGen` in its bundle.
    Then here: the package bump; app-check.mjs's and check.sh's "No FFmpeg assembly loaded" becomes "not bundled"
    (the listing check fails if `_framework/FFmpeg.AutoGen.wasm` exists), interpreted and AOT alike; THIRD-PARTY-NOTICES.md
    drops FFmpeg.AutoGen if no FFmpeg.AutoGen code is left in `dotnet.native.wasm` either; README "Size" measured again.
  - Done when: `_framework/` has no `FFmpeg.AutoGen.wasm`, the checks are green, and the README's sizes are updated.
  - Not: no change to the core library; no ILLink substitutions here (a stub of ShotDetector's internals would break
    silently when they change, and CLAUDE.md: what belongs upstream is reported there).
  - Status: Waiting on upstream ([shotdetector#597](https://github.com/bisforboman/shotdetector/issues/597), filed
    2026-10-30)

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

- Frame types (I, P, B) on the strip
  - Why: shows the encoder's structure (GOPs, where the B-frames go) next to the bitrate and keyframes.
  - Where: first upstream: ManagedCodecs' `VideoFrame` doesn't say what kind of picture it is (`SliceHeader.Type`
    exists inside the decoder); then here, after "Bitrate and keyframes".
  - Status: Maybe (needs ManagedCodecs)

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output may differ between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Maybe. Measured 2026-10-26 ("WebCodecs: are its frames ManagedCodecs' frames?", backlog-done.md; the
    decision's "Not doing" has the tables): Chromium 153's frames are ManagedCodecs' frames, every one, Firefox 136's
    reach the page in RGB, so the decision stands. Chromium alone, with ManagedCodecs elsewhere, is the owner's
    question to open
