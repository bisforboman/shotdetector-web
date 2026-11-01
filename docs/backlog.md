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

### Performance

The performance pass's rows are all done ([backlog-done.md](backlog-done.md)).

### ManagedCodecs 0.5.0

The owner chose on 2026-11-01 to adopt ManagedCodecs 0.5.0 in one session, a pull request each, in this order: the
bump, then "Each frame's own colour tags" (Maybe), then "The first pts of a track whose edit list starts at 0"
(Waiting, ManagedCodecs#252, fixed in 0.5.0). The bump and each frame's colour tags are done (backlog-done).

### ShotDetector 3.25.0 and FrameReader 2.19.0

The owner asked on 2026-11-01 to move the page to the packages released that day. The bump came first (done:
backlog-done); the rows below each need it and can run in any order, one pull request each. Shotdetector's decisions of 2026-10-31
apply: the page's managed measures move upstream into FrameReader and the page drops its copies, and a format built
from a cut list lives on ShotDetector's `Timeline` (the page owns only the UI around it).

- The picture's measures from FrameReader
  - Why: FrameReader 2.19.0's `PictureMeter`, `PictureFormat`, `PictureMeasures` and `PictureType` are the core's
    `PictureMeasures.cs` moved upstream (signalstats, blackdetect, cropdetect and the colour bars at 8 to 16 bits,
    4:2:0, 4:2:2 and 4:4:4); the owner's decision (2026-10-31) is that the page calls them and drops its copy.
  - Where: `PictureMeasures.cs` goes but for the adapter from a `YuvVideo` and a `YuvFrame` to FrameReader's
    `PictureFormat` and `Add` (the frame's planes, pts and ManagedCodecs' picture type as FrameReader's);
    `PictureSummary`, `Api.Picture.cs`, `Api.FrameTypes.cs`, `KeptRun` and `RangeBytes` on FrameReader's types.
    PictureCheck, FrameTypeCheck and app-check unchanged in what they compare. A kept run's bytes from before (in a
    visitor's IndexedDB) may not read with FrameReader's `FromBytes`: they then don't resume, as kept bytes of other
    options don't ("The stopped run didn't resume", the page detects from the start).
  - Done when: the native check's PictureCheck and FrameTypeCheck pass on every sample with the core's copy deleted,
    and app-check's `Picture()` and `FrameTypes()` comparisons (whole, split, stopped and resumed) too.
  - Not: no measure of the page's own beside FrameReader's; a measure missing or wrong there goes upstream.
  - Status: Open (needs the bump)

- The silences and channel weights from FrameReader
  - Why: FrameReader 2.19.0's `SilenceFinder` is silencedetect's counting upstream (the owner's decision, 2026-10-31:
    Mp4Audio's silences move there), and `ChannelWeights.ForLayout` gives ebur128's weights by channel name, which
    `Mp4Audio.Weights` writes out by hand for ManagedCodecs' AAC order.
  - Where: `Mp4Audio.cs`: its internal `SilenceFinder` goes for FrameReader's (instants from the first sample, the
    track's start added here); `Weights` for `ChannelWeights.ForLayout` with the AAC configuration's channel names in
    ManagedCodecs' order. AudioCheck unchanged in what it compares.
  - Done when: AudioCheck passes on every sample (silencedetect at 2 s and 1 s, ebur128) and app-check's `Audio()`
    comparison too.
  - Not: no silence counting of the page's own.
  - Status: Open (needs the bump)

- Odd heights
  - Why: ShotDetector 3.25.0 takes 8-bit 4:2:0 and 4:2:2 frames of an odd height, which it refused before; a 4:2:2
    H.264 stream (its crop unit one row) or an MJPEG AVI can be that tall, and the core passes them on today without a
    sample proving the result is the FFmpeg path's.
  - Where: `tools/make-samples.sh` (an H.264 4:2:2 sample and an MJPEG one of an odd height, checked with ffprobe);
    `Mp4Input`/`AviInput`, `PictureMeter`'s format and FastYuv's conversion as they come out on them; CLAUDE.md's scope.
  - Done when: the native and wasm checks pass on both, byte for byte.
  - Not: odd widths, unless ShotDetector takes them.
  - Status: Open (needs the bump)

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
that waited on them are open. ShotDetector 3.25.0 and FrameReader 2.19.0 (2026-11-01) shipped
[#628](https://github.com/bisforboman/shotdetector/issues/628) (ranges that deinterlace, `ShotDetection.RangeFrames`),
[#647](https://github.com/bisforboman/shotdetector/issues/647) (`LoadScenes` over a `YuvVideo`), odd heights, and
FrameReader's `PictureMeter` and `SilenceFinder` (the page's measures moved upstream, the owner's decision 2026-10-31
in shotdetector's docs/decisions/): their rows are in Next, under "ShotDetector 3.25.0 and FrameReader 2.19.0".

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

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`) takes 8-bit 4:2:0 (of an even height until
3.25.0); since 3.23.0 also rotated, at a variable rate (`FrameTimestamps`) and deinterlaced (`Yadif`), not yet in ranges
(shotdetector#628); since 3.24.0 10-bit 4:2:0 and 8-bit 4:2:2 (#627, in scope here: backlog-done) and each frame's own colour tags (#626),
a frame of another size refused (#625). What it still lacks is in its `docs/frame-source-parity.md` (issues #628 to #630): a
row that needs a change there names it. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- Chapter titles kept with the kept run
  - Why: the titles typed for a run's YouTube chapters (the run card's Titles) last while the run is shown; a stopped
    run kept in IndexedDB and shown again on a later visit (decision `2026-10-30-resume-across-visits.md`) comes back
    without them, and a resumed run's chapters change where the rest adds cuts.
  - Where: `app.js`'s `KEPT` record (the titles by chapter start, beside the options and `Kept()`'s Blob), told to
    `run.js` with the kept run; the page check's reload.
  - Done when: the page check types a title, stops, reloads and finds it in the chapters' download.
  - Status: Maybe (left out of the chapters row, 2026-10-31)

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

- The reference tone left out of the detection
  - Why: ShotDetector 3.25.0's `YuvVideo.ReferenceTone` and `DetectionOptions.TrimReferenceTone` leave a file's
    leading tone (and the bars over it) out of the shots, as `Detect(path)` does with FFmpeg finding the tone; the page
    measures the tone already (`Mp4Audio`, the sound's worker), alongside the run, not before it.
  - Where: the tone found before the run (or the run told it once the sound's worker has it), `YuvVideo.ReferenceTone`
    set from it in `Api`, an option on the page; NativeCheck's run with `TrimReferenceTone` on the tone sample.
  - Done when: the native and wasm checks pass on the tone sample with the option on, byte for byte.
  - Status: Maybe (found adopting ShotDetector 3.25.0, 2026-11-01)

- Correcting the cuts on the page (issue #90)
  - Why: ShotDetector 3.25.0's `LoadScenes(YuvVideo, TextReader, column, options)` (shotdetector#647) reads a scenes
    CSV back over a `YuvVideo`, which a page that lets a viewer add and drop cuts before exporting needs: the edited
    cuts become a result the downloads (CSVs, timeline files, chapters) are made from by ShotDetector, not the page.
  - Where: an export taking the edited scenes CSV; the timeline's cuts lane editable; a decision first
    (docs/decisions/), since what is exact after an edit is the owner's question.
  - Status: Maybe (issue #90; unblocked upstream 2026-11-01)
