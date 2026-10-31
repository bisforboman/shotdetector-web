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

### Performance

- Performance pass on the page
  - Why: the owner's choice (2026-10-31): measure what a visitor waits for and what the page costs, then fix what
    stands out, a pull request each, the results byte for byte as before.
  - Where: measured by a throwaway workflow on the branch `claude/perf-measure` (`tools/page-check/perf.mjs`, not on
    `main`): the AOT bundle as `pages.yml` publishes it, served with Pages' headers (gzip, `max-age=600`), in headless
    Chromium on GitHub's 4-core runner, and the live site for a first visit. Two runs, 2026-10-31: run 41186034723
    (AMD EPYC 9V45) and run 41187986379 (EPYC 7763, about 1.7 times as slow); figures as "9V45 / 7763" where both.
    - First load: the page 25 files, 195 KB (71 KB gzipped); the worker's bundle 4.8 MB gzipped (14.6 MB). Loaded in
      40 to 80 ms, isolated (the service worker's reload) at 210 to 670 ms, interactive (the footer's versions: the
      worker's runtime answered) at 540 to 1,220 ms; the live site 890 to 950 ms. The reload aborts the first
      document's bundle half-loaded and the second fetches it all again (live: 39 to 44 requests, 4.8 to 5.2 MB);
      .NET's loader fetches with `no-cache`, so every later visit revalidates the 26 files (304s on Pages). A revisit
      was interactive in 430 / 620 ms.
    - The threaded bundle: 31 more files, 5.6 MB gzipped (17.4 MB), fetched and started at the first one-worker run's
      click: the first progress after 1.4 / 2.1 s on threads against 0.33 / 0.5 s on the page's worker, though the
      run ends sooner (the demo done in 5.1 to 5.5 / 8.8 to 9.7 s on 4 threads, 7.3 to 7.6 / 12.4 to 12.6 s on the
      worker; first shot 2.6 to 2.9 / 4.2 to 4.7 s and 2.4 / 4.0 s).
    - The two-hour control (README "Large files", made again: 640x360, 250 kbit/s, 172,800 frames, 1,105 shots):
      split on 3 workers done in 221 / 406 s, its first shot told only by the merge, 5 s / 7 s before the end;
      on one worker on 4 threads done in 291 / 534 s, its first shot at 3.2 / 5.2 s.
    - Memory, the largest renderer process's RSS each second (run 41187986379): split, 812 MB a minute in while the
      runtimes open the file, then flat at about 600 MB, 1,007 MB at its peak, 547 MB after; one worker on threads,
      rising steadily from 673 to 944 MB over the run (about 30 MB a minute), 770 MB after. Run 41186034723's peaks:
      1,047 and 990 MB.
    - The panels after a run: every lane and card drawn 70 to 120 ms after `done` on the demo, 170 to 440 ms on the
      control, no long task over 82 ms. But a split run's merge tells its 1,105 shots one message each, and
      `panels/cuts.js` reads the track's width for each new span, a layout each: 3.7 / 5.2 s of the page's thread in
      the last 7 s before `done` (in tasks under 50 ms, so no long task). The scores lane reads the control's stats
      CSV in 40 ms; the stills are a few ms each.
  - The owner's picks (2026-10-31), all four offered, a pull request each: (1) the cuts lane without a layout per
    span; (2) a split run's first range telling its shots as it finds them; (3) the threaded runtime started when a
    file is picked or the demo fetched, not at the click; (4) the threaded run's growing renderer, found and fixed or
    reported upstream.
  - Done when: the four are each a pull request with its before and after, the checks as CLAUDE.md's "Before pushing"
    says (the results byte for byte).
  - Not: no change to what is detected or to the downloads; nothing bought with exactness.
  - Status: In progress (2026-10-31: measured)

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
