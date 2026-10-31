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

- A first visit downloads the .NET bundle once, not twice
  - Why: found by the performance pass (backlog-done "Performance pass on the page", 2026-10-31). On a first visit the
    page starts its worker, whose runtime fetches the worker's bundle (4.8 MB gzipped, 14.6 MB), and registers `sw.js`;
    once the service worker takes over, the page reloads to become cross-origin isolated (`app.js`, `serve()`), which
    aborts the bundle half-loaded, and the reloaded page fetches it all again. Measured on the live site from GitHub's
    runner: 39 to 44 more requests and 4.8 to 5.2 MB more after the reload, interactive at 890 to 950 ms; on a slow
    connection the first download is mostly wasted, and the bytes count twice on a metered one.
  - Where: `app.js`: don't start the worker's runtime (`replaceWorker()` at the bottom, after `serve()`) in a document
    that is about to reload: on a first visit where service workers exist, wait until the page is controlled and
    isolated, or until `serve()` has decided not to reload (a file picked, a reload already made this session, no
    service worker, registration failed), then start it. Whether .NET's loader fetching with `no-cache` (every later
    visit revalidates the 26 files, 304s on Pages) is worth a change too: measure it, and say so in the row either way.
  - Done when: a first visit to the page, measured as the pass did (the AOT bundle served with Pages' headers, headless
    Chromium in CI, and the live site after the deploy), fetches each of the worker bundle's files once, and becomes
    interactive no later than before; the page check passes as it is (isolated, the demo on threads, offline, Firefox),
    nothing detected or downloaded changing.
  - Not: no change to the threaded bundle's loading (started when a file is picked, `threads.js` `warm()`), to the
    service worker's caching, or to what a visit without a service worker does.
  - Status: Open

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
another, a pull request each (the owner's choice, 2026-10-31); rotation and the variable frame rate are done, the row
below is left.

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
  - Status: Maybe. Measured 2026-10-26 ("WebCodecs: are its frames ManagedCodecs' frames?", backlog-done.md; the
    decision's "Not doing" has the tables): Chromium 153's frames are ManagedCodecs' frames, every one, Firefox 136's
    reach the page in RGB, so the decision stands. Chromium alone, with ManagedCodecs elsewhere, is the owner's
    question to open
