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

### Found by the real-world check (2026-11-01)

- Tears of Steel's hash_dist under AOT: ShotDetector's fix
  - Why: realworld.yml's tears job fails on 8 rows of `tears_of_steel_720p.all.stats.csv` whose `hash_dist` is 2/64
    off under AOT (backlog-done, "Tears of Steel's every-detector stats under AOT"). The shotdetector coordinator found
    it ShotDetector's (the hash's float path: the cosine table, the median on near-flat frames); the owner chose to fix
    it there (2026-11-01). The clips job fails on it too, once the stop check no longer ended it first (run 41525295922,
    2026-11-03): 10 rows of `elephantsdream-480-h264-st-aac.all.stats.csv`, each 2/64 off, either way (frame 12:
    native 0.46875, wasm 0.5; frame 3723: 0.59375, 0.5625), and there it moves a hash cut (13786 natively, 13797 under
    AOT), so the scenes CSV and the shots JSON and HTML differ too.
  - Where: `Directory.Packages.props`, once ShotDetector releases the fix.
  - Done when: realworld.yml's tears and clips jobs pass on the release.
  - Not: a workaround here.
  - Status: Open (2026-11-03): fixed upstream (shotdetector#701, the cosine table), released in ShotDetector 3.27.0
    (on nuget.org since 2026-11-02); bumped with ShotDetector 3.28.0 (backlog-done.md, ManagedCodecs 0.7.0 row); done when the realworld runs pass on main

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
threads", "WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4", "A Matroska demuxer in ManagedCodecs").

### The npm package (issue #92)

Built (backlog-done, "The npm package"); its first publish waits on the owner (Waiting, "The npm package's first
publish").

## Waiting

- A stopped run's end at a variable rate (upstream)
  - Why: with ShotDetector 3.28.0, frames that end early (the page's Stop) end the last shot at the frames read over
    the average rate, where the FFmpeg path ends a run that ends early at the last frame's pts + 1 / average rate, in
    the stream's time. At a variable rate that is another time altogether (`vfr-320x240.mp4` stopped at frame 75:
    4.740 s against 3.5832 s); at a constant rate whose frames aren't exact in the stream's time base, a millisecond
    or a few microseconds (`vp9-bt709-640x360-2997.webm` at frame 67: 2.236 s against 2.235 s; the 29.97 AVI's and
    Sintel's editors' files). Sent to the shotdetector coordinator 2026-11-03; the owner chose to fix it in
    ShotDetector (the frame source ends where the file path does).
  - Where: the ShotDetector pin, once released; `CorrectionCheck.cs` then compares a stopped corrected run with the
    FFmpeg path's (`EndTime` at the stopped frame), which it holds only to its own run until then.
  - Done when: NativeCheck's stopped corrected runs agree with the FFmpeg path on every sample.
  - Status: Waiting (2026-11-03): on the ShotDetector release with the fix

- The npm package's first publish
  - Why: the package is built and checked, and CI runs `npm publish --dry-run` (backlog-done, "The npm package"), but
    npm's trusted publishing needs the package to exist on npmjs.com with this repository's `npm.yml` as its trusted
    publisher, which only the owner can set up.
  - Where: npmjs.com (the owner); then a tag `package-v0.1.0` on `main`, which `npm.yml` checks and publishes; issue #92
    closed after.
  - Done when: `npm view shotdetector-web` shows 0.1.0 with provenance.
  - Status: Waiting (2026-11-02): the owner's npm setup

- Freeze spans and ShotDetector's black spans on split and resumed runs
  - Why: the page looks for still pictures only in a run that reads every frame in order (decision
    [2026-11-01-freeze-spans.md](decisions/2026-11-01-freeze-spans.md)) and keeps its own black spans, because ranges
    refused `FreezeDetect` and `BlackDetect`. shotdetector#712 (merged 2026-11-03; shotdetector's decision
    2026-11-02-freeze-spans-for-ranges.md) lets ranges take both: `Merge`'s `FreezeSpans` and `BlackSpans` equal
    `Detect`'s however the run is split, with `FirstRangeToScoreAgain`/`RangeFrames` for split runs and the
    `RangeFrames(video, earlier, options, first, end)` overload for resumed ones (`RangeScores` format v2 only when
    either option is set).
  - Where: `Directory.Packages.props`; `VideoInput.Ranges.cs`, `Api.Ranges.cs`, `Api.Resume.cs`, `PictureSummary.cs`,
    `panels/picture.js`; PictureCheck, RangeCheck, ResumeCheck, app-check.mjs, page-check.
  - Done when: a split and a resumed run's still pictures and black spans are the whole run's on every sample, and
    `freezedetect`'s and `blackdetect`'s natively; `tools/check.sh native wasm aot page` passes.
  - Not: building it before the release.
  - Status: Waiting (2026-11-03): the ShotDetector release with shotdetector#712 (3.28.0, not on nuget.org yet)

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

- A corrected list timed as its run (upstream)
  - Why: ShotDetector 3.26.0's `LoadScenes`, which makes a corrected list's downloads (decision
    [2026-11-02-correcting-cuts.md](decisions/2026-11-02-correcting-cuts.md)), times cuts at frame / average rate on a
    variable-rate file and takes the video's length as nb_frames. `Detect` uses the frames' pts and the frames played.
    The first correction on such a file retimes every cut or moves the last shot's end (`fragmented-moov`: 38 of 114
    frames). This is the same on the FFmpeg path, and scenedetect 0.7.2's own load-scenes gives the same numbers, so
    `LoadScenes` stays as it is. Upstream adds an overload that takes a `DetectionResult` and the edited cuts and keeps
    that run's frame times and length ([shotdetector#707](https://github.com/bisforboman/shotdetector/issues/707), the
    owner's choice, 2026-11-02).
  - Where: the ShotDetector pin to the release with shotdetector#711 (merged 2026-11-02, not yet released):
    `LoadScenes(DetectionResult run, IEnumerable<int> cutFrames, DetectionOptions? options)` in `CorrectedCuts`. Its
    cut frames are frames counted as decoded (`run.CutFrames`, `run.FrameOf(position)`), not the scene list's Start
    Frame − 1, which is time × average rate and differs at a variable rate: the page's `detectedCuts()`, `correct()`
    and the kept corrections move to decoded frames. It takes the run's MinSceneLength, DropShortScenes and
    MergeLastScene; StartTime, EndTime and RangeDuration throw (a stopped run's range is the run's). The checks then
    compare against `Detect`'s own exports for the uncorrected list, and `CorrectionCheck.cs`'s note becomes a failure.
  - Done when: a corrected list's downloads keep the run's times and length on every sample, and NativeCheck holds it.
  - Status: In progress (2026-11-03): released in ShotDetector 3.28.0, pinned since #143

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`) takes 8-bit 4:2:0 (of an even height until
3.25.0); since 3.23.0 also rotated, at a variable rate (`FrameTimestamps`) and deinterlaced (`Yadif`), not yet in ranges
(shotdetector#628); since 3.24.0 10-bit 4:2:0 and 8-bit 4:2:2 (#627, in scope here: backlog-done) and each frame's own colour tags (#626),
a frame of another size refused (#625). What it still lacks is in its `docs/frame-source-parity.md` (issues #628 to #630): a
row that needs a change there names it. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- WebM runs split, resumed and on threads
  - Why: a browser-decoded WebM runs on the page's worker alone, and a stopped one isn't kept (decision
    `2026-11-02-webm-browser-decoder.md`, the owner's "one worker only"). NativeCheck's ranges, threads and resume
    checks already pass on the WebM samples with FFmpeg standing in: what is missing is the page's side (a
    `VideoDecoder` per range's worker from its keyframe, the threaded runtime's imports on the main thread).
  - Where: `app.js`'s `ranges`, `go` and `keep` (`browserDecoded`), `threads.js` (the decoder imports), page-check.
  - Done when: page-check splits a WebM across two workers, stops and resumes one, to the native run's files.
  - Status: Maybe (2026-11-02)

- WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4
  - Why: `WebmInput` refuses a WebM whose video track has no DefaultDuration (FFmpeg's rate then comes from the frames
    it probes) or whose Segment has no Duration (a live recording), and AV1 in MP4 (YouTube's) is refused as another
    codec in `Mp4Input`. Each needs FFmpeg's rule for it, a sample, and the checks.
  - Where: `WebmInput`, `Mp4Input` (an MP4 track's 'vp09' or 'av01' through the browser's decoder), make-samples.sh.
  - Status: Maybe (2026-11-02)

- A Matroska demuxer in ManagedCodecs
  - Why: `MatroskaReader` is the core's own minimal reader (the owner's choice, 2026-11-02), until ManagedCodecs has a
    Matroska demuxer that gives packets and the track's headers as its MP4 and AVI demuxers do.
  - Where: an issue on ManagedCodecs (not filed from this session: the repository wasn't in its scope), then
    `WebmInput` on its demuxer.
  - Status: Maybe (2026-11-02)

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

- A Cinemetrics file
  - Why: issue #91 asked for the shot lengths in a format Cinemetrics' database or tools take. Its current tool's
    `.cmsx` has no published format, and the classic `.cms` is described only by third-party code (README, "Shot
    lengths for film studies"). The page offers a plain shot-lengths CSV instead.
  - Where: a file saved by Cinemetrics' tool, from the owner or a scholar, to check against; then a writer, in
    ShotDetector's `Shots` if the CLI should share it (issue #91's comment), or the shot-lengths card.
  - Done when: a file the page writes opens in Cinemetrics' tool with the run's shots.
  - Status: Maybe (2026-11-02; cinemetrics.uchicago.edu wasn't reachable from the cloud session)
