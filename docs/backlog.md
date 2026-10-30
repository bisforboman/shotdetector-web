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
derives for display is labelled as the page's own; the CSVs, and the shots as JSON and HTML, stay the only output the
exactness check speaks for.

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
  - Status: Open: [shotdetector#598](https://github.com/bisforboman/shotdetector/issues/598) shipped in ShotDetector
    3.22.0 (`OnShot` on a range from frame 0, and overloads that continue a stopped run from the earlier ranges'
    `RangeScores`), on main since 2026-10-30; decision `2026-10-30-resume-across-visits.md`

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
