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
(Waiting, ManagedCodecs#252, fixed in 0.5.0). All three are done (backlog-done).

### ShotDetector 3.25.0 and FrameReader 2.19.0

The owner asked on 2026-11-01 to move the page to the packages released that day: the bump, then deinterlaced runs
split and resumed, the picture's measures and the silences from FrameReader, and odd heights, all done
([backlog-done.md](backlog-done.md)). Left for later, in Maybe: the reference tone left out of the detection, and
correcting the cuts on the page.

### ShotDetector 3.26.0 and FrameReader 2.20.0

The owner's queue of 2026-11-01: the bump first (done, #124), then freeze spans on the page (done): both in
[backlog-done.md](backlog-done.md).

### The files the page refuses (issue #89)

The owner's choice of 2026-11-02 ([decisions/2026-11-02-browser-decoder.md](decisions/2026-11-02-browser-decoder.md)):
a refusal that says what to do first, then VP9 and AV1 through the browser's decoder, held to the FFmpeg path's CSVs.
Both rows are done (backlog-done, #133 and #135); what they left is in Maybe ("WebM runs split, resumed and on
threads", "WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4"), and in Waiting ("WebM on
ManagedCodecs' Matroska demuxer", decision 2026-11-13).

### The npm package (issue #92)

Built (backlog-done, "The npm package"); its first publish waits on the owner (Waiting, "The npm package's first
publish").

## Waiting

- A stop in the leading reference tone, exactly (upstream)
  - Why: a run leaving the reference tone out has no frames of its own before the leading tone ends, and
    ShotDetector's `Merge` refuses a run stopped there ("StartTime is after the last frame"), as `Detect(path)` refuses a
    file that ends in its tone. The page holds a Stop until two frames past the tone's end (`Api`'s `StopsFrom`), an
    estimate of where ShotDetector starts the run (`ShotDetection.ToneStart`, internal).
  - Where: ShotDetector's `ToneStart` public, or `Merge` giving a run stopped before its start (no shots); then `Api`'s
    `StopsFrom` on it, or the hold dropped.
  - Done when: app-check's tone run stopped in the leading tone gives what ShotDetector says, with no estimate.
  - Status: Waiting (2026-11-09): asked of the shotdetector coordinator (the owner's choice through AskUserQuestion);
    the owner chose a public `ShotDetection.ToneStart(YuvVideo, DetectionOptions?)` (shotdetector#770, its decision
    2026-11-09-tone-start-public.md; `Merge` unchanged: a run stopped one frame past it merges), in the next ShotDetector
    release: then `StopsFrom` is `ToneStart` + 1

- WebM on ManagedCodecs' Matroska demuxer (upstream)
  - Why: `MatroskaReader` is the core's own minimal reader (the owner's choice, 2026-11-02), until ManagedCodecs had a
    Matroska demuxer. `MatroskaDemuxer` 0.13.0 gives the same video packets on every WebM sample, but lacks the
    DefaultDuration exactly, FrameRate, FlagInterlaced, a projection's yaw and pitch, a fractional Duration and the
    other tracks' sizes FFmpeg's probe counts, and on a two-hour WebM allocates 190 MB at each opening (35 MB kept,
    both tracks in 64-byte entries) where the reader allocates 8.8 MB
    ([decisions/2026-11-13-webm-matroska-demuxer.md](decisions/2026-11-13-webm-matroska-demuxer.md)).
  - Where: one ManagedCodecs issue with the decision's six asks (filed by the owner or its coordinator: this
    repository's sessions can't reach ManagedCodecs); then `WebmInput` on the demuxer, `MatroskaReader` removed.
  - Done when: every WebM sample and refusal gives the same CSVs and description natively and on the page, with no
    Matroska parsing of the core's own, and a two-hour WebM's opening keeps the heap near the reader's.
  - Status: Waiting (2026-11-13, the owner's choice through AskUserQuestion: keep the reader, ask upstream): the
    ManagedCodecs issue, to be filed

- The npm package's first publish
  - Why: the package is built and checked, and CI runs `npm publish --dry-run` (backlog-done, "The npm package"), but
    npm's trusted publishing needs the package to exist on npmjs.com with this repository's `npm.yml` as its trusted
    publisher, which only the owner can set up.
  - Where: npmjs.com (the owner); then a tag `package-v0.1.0` on `main`, which `npm.yml` checks and publishes; issue #92
    closed after.
  - Done when: `npm view shotdetector-web` shows 0.1.0 with provenance.
  - Status: Waiting (2026-11-02): the owner's npm setup

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

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`) takes 8-bit 4:2:0 (of an even height until
3.25.0); since 3.23.0 also rotated, at a variable rate (`FrameTimestamps`) and deinterlaced (`Yadif`), not yet in ranges
(shotdetector#628); since 3.24.0 10-bit 4:2:0 and 8-bit 4:2:2 (#627, in scope here: backlog-done) and each frame's own colour tags (#626),
a frame of another size refused (#625). What it still lacks is in its `docs/frame-source-parity.md` (issues #628 to #630): a
row that needs a change there names it. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- A threaded WebM run's intermittent failures (an unaligned atomic, a crash, a result that doesn't come)
  - Why: page-check's held, stopped and resumed run of `av1-yuv420p10-320x240-25.webm` on threads (2026-11-04, the
    interpreted publish) gave the native run's files, then the threaded runtime ended: "operation does not support
    unaligned accesses" (WebAssembly's trap for an atomic on an unaligned address) and `RuntimeError: unreachable` in
    `threads/_framework/dotnet.native.js`. Nineteen such runs, the same file and the 8-bit AV1 one, gave it once. The
    page's code has no atomics of its own there: .NET's threaded runtime (10.0.14), or the decoder imports it runs on
    the main thread for a WebM (`JsDecoder`'s `Span` views). Seen since in whole page checks (2026-11-04, a cloud
    session, the page's files those of main at #158): the same trap once on `vp9-320x240-25.webm` on threads; twice
    the resumed 10-bit AV1 run on threads done, but none of its downloads within 10 s (`ShotsJson` and `ShotsHtml` from
    the threaded runner not answering; no error on the page); once Chromium's renderer crashed during
    `vp9-320x240-25.webm` on threads ("Target crashed"). The step alone, or the WebM steps alone, passed each time
    (3 and 1 runs, page-check.mjs on the same published bundle), and a whole page check on main passed once: they came
    only in `tools/check.sh page`, whose WebM steps run first, right after the publish.
  - Where: a repro first (page-check's WebM steps after the rest, in a loop), then .NET's issue tracker if it is the
    runtime's.
  - Status: Maybe (2026-11-04)

- A Cinemetrics file
  - Why: issue #91 asked for the shot lengths in a format Cinemetrics' database or tools take. Its current tool's
    `.cmsx` has no published format, and the classic `.cms` is described only by third-party code (README, "Shot
    lengths for film studies"). The page offers a plain shot-lengths CSV instead.
  - Where: a file saved by Cinemetrics' tool, from the owner or a scholar, to check against; then a writer, in
    ShotDetector's `Shots` if the CLI should share it (issue #91's comment), or the shot-lengths card.
  - Done when: a file the page writes opens in Cinemetrics' tool with the run's shots.
  - Status: Maybe (2026-11-02; cinemetrics.uchicago.edu wasn't reachable from the cloud session)
