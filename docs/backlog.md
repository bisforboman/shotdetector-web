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
derives for display is labelled as the page's own; the CSVs, the shots as JSON and HTML and the editors' timeline files
stay the only output the exactness check speaks for.

- YouTube chapters on the run card (issue [#84](https://github.com/bisforboman/shotdetector-web/issues/84), the second
  slice of [docs/ideas/editor-exports-and-launch.md](ideas/editor-exports-and-launch.md); the owner's choices,
  2026-10-31: on the page only, at least 30 s by default, editable)
  - Why: a description's chapter list is what a YouTube uploader would take from the cuts; no library writes it.
  - Where: the core's `YouTubeChapters` (each chapter at a cut rounded down to the second, at least the minimum apart
    and before the end, three or none with a reason; YouTube's rules: the first at 00:00, at least three, each at least
    10 s), `Api.Chapters.cs` with its twin in `Api.Threads.cs` and its name in `threads.js`, a row in `panels/run.js`
    with the minimum's field and the download, marked the page's own in its tooltip; NativeCheck's `ChaptersCheck.cs`
    (YouTube's rules on every run, hand-made cases), app-check.mjs and the page check against the native run's.
  - Done when: `tools/check.sh native wasm page` passes with the chapters at 10 and 30 s the native run's on every
    sample and run, the hand-made cases agreeing.
  - Not: titles (placeholders for the uploader), ShotDetector's CLI, the launch.
  - Status: In progress (2026-10-31)

### Performance

The performance pass's rows are all done ([backlog-done.md](backlog-done.md)).

## Waiting

The asks filed upstream on 2026-10-24 all shipped on 2026-10-25:
[shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492) and
[#493](https://github.com/bisforboman/shotdetector/issues/493) in ShotDetector 3.15.0 (`DetectAsync` over an
asynchronous frame source, `DetectionOptions.OnShot`), [#494](https://github.com/bisforboman/shotdetector/issues/494)
in FrameReader 2.11.0 (`ReferenceToneFinder`, `WaveformBuilder`); those filed on 2026-10-26 shipped in ShotDetector
3.20.0 and ManagedCodecs 0.1.0 (2026-10-27 and 2026-10-28). Their rows are done ([backlog-done.md](backlog-done.md)), and
ManagedCodecs#138's optional part too, in ManagedCodecs 0.4.0 (#211, 2026-10-30). Those filed on 2026-10-30,
[shotdetector#597](https://github.com/bisforboman/shotdetector/issues/597) and
[#598](https://github.com/bisforboman/shotdetector/issues/598), shipped in ShotDetector 3.22.0 the same day: their rows
are in Next again. [#608](https://github.com/bisforboman/shotdetector/issues/608),
[#609](https://github.com/bisforboman/shotdetector/issues/609) and
[#614](https://github.com/bisforboman/shotdetector/issues/614) shipped in ShotDetector 3.23.0 (2026-10-31), and
ManagedCodecs#225 in 0.4.0 (#231, each frame's interlacing): the rows they waited on, worked by one session one after
another, a pull request each (the owner's choice, 2026-10-31): rotation, the variable frame rate and deinterlacing,
all done. ShotDetector 3.24.0 and FrameReader 2.18.0 (2026-10-31) shipped
[#625](https://github.com/bisforboman/shotdetector/issues/625) (a frame of another size refused),
[#626](https://github.com/bisforboman/shotdetector/issues/626) (each frame's own colour tags),
[#627](https://github.com/bisforboman/shotdetector/issues/627) (10-bit 4:2:0 and 8-bit 4:2:2),
[#633](https://github.com/bisforboman/shotdetector/issues/633) (Subtitle Edit's shot changes),
[#648](https://github.com/bisforboman/shotdetector/issues/648) (YouTube chapters) and FrameReader's `LoudnessMeter`
([#646](https://github.com/bisforboman/shotdetector/issues/646)): the bump is done (backlog-done), and the rows below
that waited on them are open.

- Deinterlaced runs split and resumed
  - Why: a deinterlaced run (the page's Deinterlace option) goes on one worker and a stopped one can't be resumed,
    since ShotDetector's ranges refuse yadif, which reads the frames on either side of each; a long broadcast file is
    where both matter most.
  - Where: first upstream, [shotdetector#628](https://github.com/bisforboman/shotdetector/issues/628) (in progress there,
    2026-10-31: a range's first frame given the one before it, its last the one after; with it a public
    `ShotDetection.RangeFrames(video, options, firstFrame, endFrame)`, the frames a range needs, the owner's choice as the
    coordinator told it); then `Api`'s deinterlaced path (`Whole`) goes, `VideoInput.Deinterlaces` (a copy of
    ShotDetector's internal rule) and the ranges' hard-coded margins for `RangeFrames`, `app.js`'s `planFor` and
    `NOT_KEPT` with them, and the deinterlaced runs join the range, threads and resume checks and app-check's
    `SPLIT_RUNS`.
  - Done when: the native, wasm and page checks pass with a deinterlaced run split, stopped and resumed, byte for byte.
  - Status: Waiting on upstream (shotdetector#628)

- The first pts of a track whose edit list starts at 0 (found by the real-world check)
  - Why: Caminandes: Gran Dillama's video has an edit list with media_time 0 and a first composition offset of 1000
    (timescale 24000). FFmpeg's mov demuxer (`mov_fix_index`) shifts such a track so its first pts is 0, and
    ManagedCodecs' `Mp4Demuxer` doesn't: the core's `StartPts` is 1000 against FFmpeg's 0, and every packet's pts
    (`PacketCheck`) is 1000 later than ffprobe's, and so the black spans' times (`PictureCheck`, blackdetect) a frame
    later than FFmpeg's. The CSVs and the shots' JSON and HTML are still identical, but the description isn't.
  - Where: upstream, [ManagedCodecs#252](https://github.com/bisforboman/ManagedCodecs/issues/252) (`Mp4Demuxer`); then here a
    ManagedCodecs bump, and a sample with that edit list in make-samples.sh (ffmpeg's muxer writes media_time = the
    first offset, so none of today's samples has it).
  - Done when: `tools/realworld.sh check wasm caminandes_gran_dillama.mp4 caminandes-fragmented.mp4` passes, and the
    native check passes on the new sample.
  - Not: no shift in `Mp4Input` working around the demuxer (CLAUDE.md: report it there).
  - Status: Waiting on upstream (ManagedCodecs#252, filed 2026-10-31)

- 10-bit 4:2:0 and 8-bit 4:2:2
  - Why: ManagedCodecs decodes both; FastYuv converts both (`SwscaleYuv420P10`, its 4:2:2 path) with x86's results.
  - Where: first upstream: `YuvFrame` for 16-bit planes and 4:2:2 chroma, and `YuvVideo.PixelFormat` accepting
    yuv420p10le / yuv422p (and yuvj422p: 4:2:2 MJPEG, most cameras', decision `2026-10-30-mjpeg-avi.md`); then here,
    the chroma and bit-depth check in `Mp4Input` and `AviInput`, three samples (already in `samples/reject/`:
    `yuv422p.mp4`, `yuv420p10.mp4`, `mjpeg-422.avi`).
  - Status: Open (shipped upstream in ShotDetector 3.24.0,
    [shotdetector#627](https://github.com/bisforboman/shotdetector/issues/627))

- YouTube chapters: the download, and the chapters' titles edited on the page
  - Why: issue #84's chapters. The owner decided on 2026-10-31 (shotdetector#648) that the chapter list, the
    shots-to-chapters rule (first at 0:00, at least three, each long enough) and placeholder titles live on ShotDetector's
    `Timeline`, and the page owns only the UI around it. The standing rule: a file format built from a cut list lives on
    `Timeline`.
  - Where: after the ShotDetector release with #648 and its package bump: `TimelineFiles` gains the chapters format
    (one more download in run.js's "For editors" row, checked byte for byte as the others), then a title per chapter
    edited on the page (with the shots table, kept with the kept run), the download written from the edited titles.
  - Done when: the chapters download equals NativeCheck's `Detect(path)` file for every sample, and with titles typed
    on the page the download carries them (page check).
  - Not: the chapter rule on the page; correcting the cuts themselves (#90, which needs shotdetector#647).
  - Status: Open (shipped upstream in ShotDetector 3.24.0,
    [shotdetector#648](https://github.com/bisforboman/shotdetector/issues/648))

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`) takes 8-bit 4:2:0 of an even height;
since 3.23.0 also rotated, at a variable rate (`FrameTimestamps`) and deinterlaced (`Yadif`), not yet in ranges
(shotdetector#628); since 3.24.0 10-bit 4:2:0 and 8-bit 4:2:2 (#627) and each frame's own colour tags (#626), a frame
of another size refused (#625). What it still lacks is in its `docs/frame-source-parity.md` (issues #628 to #630): a
row that needs a change there names it. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- Each frame's own colour tags (a matrix or range that changes midway)
  - Why: ShotDetector 3.24.0 converts each frame with its own `YuvFrame.ColorSpace` and `FullRange` (shotdetector#626),
    as `Detect(path)` does with each frame's tags from FFmpeg's decoder: a spliced stream whose SPS changes its VUI
    colour matrix or range midway gives other stats here, without a word, since `Mp4Input` describes every frame with
    the first SPS's (`SpsInfo.FromAvcConfig`, the 'avcC''s). ManagedCodecs' `VideoFrame` carries no colour tags (0.4.0),
    so the core can't tell them per frame without parsing each packet's in-band SPS itself and tracking which decoded
    frames each one covers, which is the decoder's knowledge.
  - Where: first upstream, ManagedCodecs: each `VideoFrame`'s colour description from its active SPS (matrix and range;
    an MJPEG frame's are fixed); then `VideoInput`'s frames set `YuvFrame.ColorSpace`/`FullRange` where they differ
    from `YuvVideo`'s, and a sample in make-samples.sh (two streams with other VUI tags, their Annex B concatenated as
    `samples/reject/size-change.mp4`'s are) that NativeCheck runs as every sample.
  - Done when: the native check passes on that sample, byte for byte.
  - Not: no SPS parsing of every packet in the core (the decoder's job: CLAUDE.md, report it there).
  - Status: Maybe (found adopting ShotDetector 3.24.0, 2026-10-31; not filed upstream yet)

- An AVI's JPEGs without Huffman tables
  - Why: an MJPEG chunk may leave out its DHT segment (the AVI1 convention: the standard tables implied), which
    FFmpeg's and ManagedCodecs' decoders supply but a browser's JPEG decoder doesn't; the page's picture and stills of
    such a file (backlog-done "The picture of an AVI on the page") would then be missing (the stills say "didn't show").
    ffmpeg's encoder writes the tables, so no sample has the case yet.
  - Where: `make-samples.sh` (a sample with the DHT stripped, checked with a JPEG parser); the page inserting the
    standard tables before the first SOS when a JPEG has none (bytes copied, no decoding).
  - Done when: the page check shows that sample's picture and stills.
  - Not: decoding in .NET for the picture.
  - Status: Maybe (found 2026-10-31)

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output may differ between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Maybe (issue #89 asks it again for the files the page refuses, marked not exact; #15, speed, closed into
    it). Measured 2026-10-26 ("WebCodecs: are its frames ManagedCodecs' frames?", backlog-done.md; the
    decision's "Not doing" has the tables): Chromium 153's frames are ManagedCodecs' frames, every one, Firefox 136's
    reach the page in RGB, so the decision stands. Chromium alone, with ManagedCodecs elsewhere, is the owner's
    question to open

- Real phone and broadcast files for the real-world check
  - Why: the real-world check's rotated and interlaced files are made from Blender films by FFmpeg, since no openly
    licensed phone video or broadcast capture is on a host the cloud sandbox reaches (download.blender.org only:
    not Wikimedia Commons, archive.org, FFmpeg's samples or test-videos.co.uk, tried 2026-10-31). Real ones have an
    encoder's own SEI, rotation as phones write it, VFR as phones make it, PAFF field pictures.
  - Where: `tools/realworld.sh`'s list: files from Wikimedia Commons (reachable from GitHub's runners), pinned by
    SHA-256 recorded from the workflow's first download, as ci.yml's Sintel trailer was.
  - Done when: realworld.yml runs them, each "same" or "refused" with its row named.
  - Status: Maybe
