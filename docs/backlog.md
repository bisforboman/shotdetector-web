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
threads", "WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4", "A Matroska demuxer in ManagedCodecs").

### The npm package (issue #92)

Built (backlog-done, "The npm package"); its first publish waits on the owner (Waiting, "The npm package's first
publish").

### Found by the real phone files (2026-11-04)

The eight phone files (realworld.sh, run 41787733573) give the FFmpeg path's scenes and stats CSVs, shots and every
run's files byte for byte; four differ in their description or measures, run by hand in realworld.yml's phones-differ
group until their rows move them to phones.

- An Android file's duration
  - Why: `phone-android-2017-1080p.mp4` (1920x1080 Baseline, a variable rate, AAC 48 kHz 2 ch): the core's duration
    4.0960000 s, FFmpeg's format duration 4.0959380 s; the shots and CSVs are the same.
  - Where: `Mp4Input`'s duration (the streams' ends as FFmpeg's mov demuxer takes them: the audio's priming and edit
    list, `Gotchas`), found with the file (realworld.sh downloads it; the cloud session can't reach archive.org's file
    servers).
  - Done when: the file moves to realworld.yml's phones group, "same".
  - Status: Open (2026-11-04)

- A 3GP's chroma location
  - Why: `phone-android-2011.3gp` (720x480 Baseline): the core says chroma location left, FFmpeg says none
    (unspecified).
  - Where: `SpsInfo`'s chroma location (its VUI, or FFmpeg's default for a 3GP), with the file.
  - Done when: the file moves to the phones group, "same".
  - Status: Open (2026-11-04)

- A WhatsApp file's audio peaks
  - Why: `phone-whatsapp-2017.mp4` (AAC 48 kHz 2 ch, very quiet: -64.6 LUFS): true and sample peaks -41.494 dB on both
    channels against FFmpeg's ebur128 -41.938; the loudness and the waveform agree.
  - Where: `Mp4Audio`'s peaks (FrameReader) or ManagedCodecs' AAC decoder on this stream, with the file: report
    upstream if either is theirs.
  - Done when: the file moves to the phones group, "same".
  - Status: Open (2026-11-04)

- An iPhone file's picture measures
  - Why: `phone-iphone-2018.mov` (960x720 High, full range): every frame's signalstats and cropdetect differ from
    FFmpeg's filters (frame 0: YAVG 152.574 against 152.79; cropdetect 0-959 x 0-719 against FFmpeg's 0-871 x 0-651,
    crop 864x640+4+6), while the shots and CSVs equal `Detect(path)`'s: FFmpeg's command line likely crops the frames
    (a container crop, applied by the CLI and not by ShotDetector's in-process decode), or the frames differ.
  - Where: the file's boxes ('clap', the track's size) and PictureCheck's ffmpeg pass, with the file.
  - Done when: the file moves to the phones group, "same".
  - Status: Open (2026-11-04)

## Waiting

- The reference tone left out of the detection
  - Why: ShotDetector 3.25.0's `YuvVideo.ReferenceTone` and `DetectionOptions.TrimReferenceTone` leave a file's
    leading tone (and the bars over it) out of the shots, as `Detect(path)` does with FFmpeg finding the tone; the page
    measures the tone already (`Mp4Audio`, the sound's worker), alongside the run, not before it.
  - Where: decision [2026-11-04-reference-tone.md](decisions/2026-11-04-reference-tone.md), the owner's 1A and 2A:
    ShotDetector's ranges taking the trim first (3.28.0's is "not for ranges": `ScoreRange` refuses a time range),
    then an option on the page, off by default, the run waiting for the sound's worker's tone, `YuvVideo.ReferenceTone`
    set from it in `Api`; NativeCheck's run with `TrimReferenceTone` on the tone sample, whole, split and resumed.
  - Done when: the native and wasm checks pass on the tone sample with the option on, byte for byte.
  - Status: Waiting (2026-11-04): asked of the shotdetector coordinator, the trim in ranges

- A stopped run's end at a variable rate (upstream)
  - Why: with ShotDetector 3.28.0, frames that end early (the page's Stop) end the last shot at the frames read over
    the average rate, where the FFmpeg path ends a run that ends early at the last frame's pts + 1 / average rate, in
    the stream's time. At a variable rate that is another time altogether (`vfr-320x240.mp4` stopped at frame 75:
    4.740 s against 3.5832 s); at a constant rate whose frames aren't exact in the stream's time base, a millisecond
    or a few microseconds (`vp9-bt709-640x360-2997.webm` at frame 67: 2.236 s against 2.235 s; the 29.97 AVI's and
    Sintel's editors' files). Sent to the shotdetector coordinator 2026-11-03; the owner chose to fix it in
    ShotDetector (the frame source ends where the file path does).
  - Where: the ShotDetector pin to the release with shotdetector#730, which ends it where the file path does only when
    `YuvVideo.FrameTimestamps` is given (without them a stop looks like the video's end; the owner's choice, shotdetector's
    decision 2026-11-03-stopped-frame-source-end.md): the core then gives every file's frame timestamps, at a constant
    rate too (an AVI's, a WebM's), not only a variable rate's, and NativeCheck's description check follows;
    `CorrectionCheck.cs` compares a stopped corrected run with the FFmpeg path's (`EndTime` at the stopped frame), which
    it holds only to its own run until then.
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
  - Status: In progress (2026-11-04)



- A Cinemetrics file
  - Why: issue #91 asked for the shot lengths in a format Cinemetrics' database or tools take. Its current tool's
    `.cmsx` has no published format, and the classic `.cms` is described only by third-party code (README, "Shot
    lengths for film studies"). The page offers a plain shot-lengths CSV instead.
  - Where: a file saved by Cinemetrics' tool, from the owner or a scholar, to check against; then a writer, in
    ShotDetector's `Shots` if the CLI should share it (issue #91's comment), or the shot-lengths card.
  - Done when: a file the page writes opens in Cinemetrics' tool with the run's shots.
  - Status: Maybe (2026-11-02; cinemetrics.uchicago.edu wasn't reachable from the cloud session)
