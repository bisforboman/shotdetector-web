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

### Real-world check

- Real-world check: the core and the page on real files
  - Why: the generated samples each isolate one thing, and real files mix them: long camera and broadcast-like H.264,
    phone videos (rotation, a variable rate), fragmented and interlaced material, AAC audio, and old encoders' quirks.
    Results must equal ShotDetector's FFmpeg path byte for byte on them too, as NativeCheck and app-check do on the
    samples (the owner's choice, 2026-10-31; shotdetector's own is its `realworld.yml` and `tools/realworld.py`).
  - Where: `tools/realworld.sh` (the files, pinned by SHA-256: Blender Foundation films, CC-BY, from
    download.blender.org, the one host the cloud sandbox reaches, and variants of them the pinned FFmpeg makes:
    fragmented, rotated, 1080i25 MBAFF), `.github/workflows/realworld.yml` (nightly and by hand, never on a pull
    request, not required: the owner's choice, 2026-10-31), `tools/app-check.mjs --light` (the AOT bundle's default
    and every-detector runs, whole and split, on files over three minutes), `tools/ci-scope.sh` (the two new files
    need no pull-request check).
  - Done when: realworld.yml's groups pass, or what they find is a row here or an ask upstream; README "Checks" says
    what it runs.
  - Not: no file in the repository, none that isn't openly licensed; no change to shotdetector.
  - Status: In progress (2026-10-31)

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
ManagedCodecs#225 in 0.4.0 (#231, each frame's interlacing): the three rows below, worked by one session one after
another, a pull request each (the owner's choice, 2026-10-31).

- Rotation
  - Why: phones record rotated; the FFmpeg path turns frames upright.
  - Where: first upstream, [shotdetector#608](https://github.com/bisforboman/shotdetector/issues/608)
    (`YuvVideo.Rotation`); then `Mp4Header` reads the `tkhd` matrix's angle as `av_display_rotation_get`, quarter turns
    only, samples at 90, 180 and 270, `PictureMeter`'s crop and colour bars in the upright frame's coordinates (the
    owner's choice, decision 2026-10-30-scope-widening.md).
  - Done when: the native, wasm and page checks pass on the rotated samples byte for byte.
  - Status: Waiting on upstream (shotdetector#608, filed 2026-10-30)

- Variable frame rate
  - Why: screen recordings and phones; the FFmpeg path takes it at the frames' own timestamps.
  - Where: first upstream, [shotdetector#609](https://github.com/bisforboman/shotdetector/issues/609) (no grid check,
    ranges by pts, `StartTime` from timestamps); then `Mp4Input.CheckConstantRate` goes, a VFR sample, split runs as
    any other (the owner's choice).
  - Done when: the native, wasm (whole and split) and page checks pass on the VFR sample byte for byte.
  - Status: Waiting on upstream (shotdetector#609, filed 2026-10-30)

- Deinterlacing interlaced H.264
  - Why: broadcast material is interlaced; MBAFF video is detected on its woven frames since the "Interlaced H.264"
    row, as ShotDetector's default, but `--deinterlace auto` (yadif) is the setting for it, and FFmpeg's field order
    belongs in the description (decision [2026-10-30-interlaced.md](decisions/2026-10-30-interlaced.md)).
  - Where: first upstream, [shotdetector#614](https://github.com/bisforboman/shotdetector/issues/614) (`YuvVideo.FieldOrder`,
    `YuvFrame.Interlaced` and `TopFieldFirst`, `Deinterlace` with FastYuv's `Yadif` on frames from the caller) and
    [ManagedCodecs#225](https://github.com/bisforboman/ManagedCodecs/issues/225) (each frame's interlacing and the field
    order as FFmpeg reports them, and whether a frame was two field pictures); then here: `Mp4Input` describes the field
    order (NativeCheck against ffprobe's `field_order` and `-show_frames`' `interlaced_frame,top_field_first`), a
    deinterlace option on the page (on one worker: ShotDetector's ranges refuse `Deinterlace`), the runs with it in
    `tools/Shared/Runs.cs`. Field pictures with a sample muxed one frame a sample (JM's, as ManagedCodecs', put one field
    in each, which the constant-rate check refuses), or refused by ManagedCodecs#225's flag.
  - Done when: the native, wasm and page checks pass byte for byte on the interlaced samples with deinterlacing on
    `auto`, and the field order is checked against ffprobe.
  - Not: no yadif or slice-header parsing here (CLAUDE.md: report it there, don't work around it).
  - Status: Waiting on upstream (shotdetector#614 and ManagedCodecs#225, filed 2026-10-30)

- The first pts of a track whose edit list starts at 0 (found by the real-world check)
  - Why: Caminandes: Gran Dillama's video has an edit list with media_time 0 and a first composition offset of 1000
    (timescale 24000). FFmpeg's mov demuxer (`mov_fix_index`) shifts such a track so its first pts is 0, and
    ManagedCodecs' `Mp4Demuxer` doesn't: the core's `StartPts` is 1000 against FFmpeg's 0, and every packet's pts
    (`PacketCheck`) is 1000 later than ffprobe's. The CSVs and the shots' JSON and HTML are still identical, but the
    description isn't.
  - Where: upstream in ManagedCodecs' `Mp4Demuxer` (sent to the shotdetector coordinator, 2026-10-31); then here a
    ManagedCodecs bump, and a sample with that edit list in make-samples.sh (ffmpeg's muxer writes media_time = the
    first offset, so none of today's samples has it).
  - Done when: `tools/realworld.sh check wasm caminandes_gran_dillama.mp4 caminandes-fragmented.mp4` passes, and the
    native check passes on the new sample.
  - Not: no shift in `Mp4Input` working around the demuxer (CLAUDE.md: report it there).
  - Status: Waiting on upstream (sent to the shotdetector coordinator, 2026-10-31)

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`, 3.14.0) takes 8-bit 4:2:0 of an even
height at a constant frame rate, upright, and refuses `Deinterlace` and `Yadif`: most rows below need a change there
first, named in each. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.


- 10-bit 4:2:0 and 8-bit 4:2:2
  - Why: ManagedCodecs decodes both; FastYuv converts both (`SwscaleYuv420P10`, its 4:2:2 path) with x86's results.
  - Where: first upstream: `YuvFrame` for 16-bit planes and 4:2:2 chroma, and `YuvVideo.PixelFormat` accepting
    yuv420p10le / yuv422p (and yuvj422p: 4:2:2 MJPEG, most cameras', decision `2026-10-30-mjpeg-avi.md`); then here,
    the chroma and bit-depth check in `Mp4Input` and `AviInput`, three samples (already in `samples/reject/`:
    `yuv422p.mp4`, `yuv420p10.mp4`, `mjpeg-422.avi`).
  - Status: Maybe (needs ShotDetector)

- The picture of an AVI on the page
  - Why: no browser plays MJPEG in AVI, so an AVI's page shows a line where the video would be and its shots' table no
    stills (decision `2026-10-30-mjpeg-avi.md`, the owner's choice 3). Each MJPEG packet is a JPEG the browser shows.
  - Where: an export of a packet's bytes (`Api.Frame(index)`, `VideoInput.Packets` says where); the player an `<img>` of
    the packet at the playhead when the `<video>` can't play, no sound; `panels/shots.js`'s stills from the same.
  - Done when: picking `samples/mjpeg-320x240-25.avi` in the page check shows the frame at the playhead and a still per
    shot, and the downloads are the native run's.
  - Not: no transcoding, no decoding of the JPEGs in .NET for the picture (the browser's own JPEG decoder).
  - Status: Maybe

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output may differ between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Maybe. Measured 2026-10-26 ("WebCodecs: are its frames ManagedCodecs' frames?", backlog-done.md; the
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
