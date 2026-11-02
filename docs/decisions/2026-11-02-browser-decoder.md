# The browser's decoder for the files the page refuses (2026-11-02)

[#89](https://github.com/bisforboman/shotdetector-web/issues/89), chosen by the owner on 2026-11-02 as a decision first,
no feature code: should the page read the files it refuses today (HEVC, VP9, AV1, most phone video) through the
browser's own decoder (WebCodecs' `VideoDecoder`, or a hidden `<video>`), with those results marked "not exact"? It
reopens the "Not doing" entry of [2026-10-24-the-page.md](2026-10-24-the-page.md) ("WebCodecs ... as a faster path")
and backlog "WebCodecs as a decoder" (Maybe), for another reason: not speed, but files that get no result at all.

## What was found

### The measurement of 2026-10-26 (H.264, README "WebCodecs")

The page's decision has the tables. In short: Chromium 153's `VideoDecoder` (software: FFmpeg's h264 decoder in
Chromium's build; `prefer-hardware` unsupported on a VM without a GPU) gave ManagedCodecs' frames, every plane of every
frame of every sample and of a two-hour film, decoded and copied out 7 to 10 times faster than ManagedCodecs on wasm
AOT. Firefox 136 handed every frame to the page as `BGRX`: `copyTo` refuses I420, there are no Y, U and V planes, and
so no FastYuv conversion to keep. Safari wasn't run. Verdict then: not identical in both browsers, the page keeps
ManagedCodecs.

### The other codecs in this sandbox's Chromium (2026-11-02)

Run for this decision with a scratch page (not committed) in Playwright's Chromium 153.0.8010.12, headless and the full
build (`channel: 'chromium'`), no GPU, on Linux. Seven clips of `testsrc2` (320x240, 25 fps, 50 frames) made with the
sandbox's FFmpeg 6.1: libx264 High, libx265 Main and Main 10, libvpx-vp9 profile 0 and 2 (10-bit), libaom AV1 8- and
10-bit. Each clip's packets went to `VideoDecoder` (Annex B for H.264 and HEVC, IVF frames for VP9 and AV1), each frame
was copied out with `copyTo`, and a hash of the copy compared with the same frame decoded by FFmpeg
(`-f rawvideo -pix_fmt yuv420p` or `yuv420p10le`; FFmpeg's own vp9 decoder and libdav1d).

| Codec | `isConfigSupported` (`no-preference`, `prefer-software`, `prefer-hardware`) | `<video>` `canPlayType` | Frames | Format as decoded | Against FFmpeg |
| --- | --- | --- | --- | --- | --- |
| H.264 High 8-bit | yes, yes, no | probably | 50 | I420, coded 320x258 | every frame identical |
| HEVC Main | no, no, no | "" | | | not decodable |
| HEVC Main 10 | no, no, no | | | | not decodable |
| VP9 profile 0 | yes, yes, no | probably (WebM) | 50 | I420, coded 384x240 | every frame identical |
| VP9 profile 2 (10-bit) | yes, yes, no | | 50 | I420P10 | every frame identical |
| AV1 Main 8-bit | yes, yes, no | probably (MP4) | 50 | I420 | every frame identical |
| AV1 Main 10-bit | yes, yes, no | | 50 | I420P10 | every frame identical |

So:

- **The decoders are exact; the path to the page may not be.** H.264, HEVC, VP9 and AV1 decoders are specified to the
  bit, and Chromium's software ones (FFmpeg's h264, libvpx, dav1d) gave FFmpeg's frames. What breaks exactness is what
  a browser does around them: Firefox's conversion to RGB before the page sees a frame, a hardware decoder (not
  reachable here) handing NV12 or its own surfaces, frames dropped or reordered on the way.
- **`copyTo` gives what FastYuv and the frame source take.** I420 is the `yuv420p` planes; I420P10 is 16-bit
  little-endian samples, the `yuv420p10le` layout ShotDetector's frame source takes since #627 (the 10-bit hashes above
  are FFmpeg's `yuv420p10le` bytes). The visible rectangle is the picture (the coded size is padded: 384x240 for VP9).
  NV12, which hardware decoders give, needs only its chroma de-interleaved, which shotdetector's parity audit already
  names as the host's job; `copyTo(..., { format: 'I420' })` asks the browser for it. Firefox's BGRX has no such way
  back: a result from it is the browser's conversion, not FastYuv's.
- **The frame's colour tags are not the file's.** Every clip here is untagged (FFmpeg: `unknown`), and every
  `VideoFrame.colorSpace` said `bt709`, `fullRange: false`: Chromium fills in a default. `YuvVideo` must say what FFmpeg
  says, so the colour tags (and the rate, frame count, duration, time base, rotation) still have to come from the
  container and the bitstream, as `Mp4Header`/`SpsInfo` do for H.264: a VP9 frame header, an AV1 sequence header and
  an HEVC SPS/VUI reader, and a WebM/Matroska reader (YouTube's VP9 and AV1 come in WebM as well as MP4; ManagedCodecs
  0.5.0 reads MP4 and AVI only).
- **HEVC, the phone video, is the one Chromium on Linux won't decode at all.** Chrome decodes HEVC only through the
  platform's hardware decoder (Windows, macOS, Android, ChromeOS; Linux with VA-API), so CI's Chromium can't run it.

### Which browsers decode which codecs (documentation, not run here except Chromium on Linux)

| | H.264 | HEVC | VP9 | AV1 | Frames as |
| --- | --- | --- | --- | --- | --- |
| Chrome, Edge | software | hardware only | software | software (dav1d) | I420 in software, NV12 often in hardware (measured: I420, I420P10) |
| Firefox (WebCodecs since 130) | system FFmpeg or platform | Windows hardware at most | software | software | BGRX measured on Linux 2026-10-26 |
| Safari (`VideoDecoder` since 16.4) | hardware | hardware | where the platform has it | recent Apple hardware only | not measured |

A hidden `<video>` reads the same decoders through `requestVideoFrameCallback` and `new VideoFrame(video)`: frames can
be skipped when the page can't keep up, the times are the presentation's, and its frames are often RGB or GPU
textures. It gives no guarantee of every frame once, so it is out of every option below.

### What ShotDetector's frame source still lacks (shotdetector `docs/frame-source-parity.md`, 2026-10-31)

Rotation (#608), a variable rate (#609), deinterlacing (#614), 10-bit 4:2:0 and 8-bit 4:2:2 (#627), per-frame colour
tags (#626), odd heights (#630), ranges that deinterlace (#628) are taken: an approximate path waits on none of them
(the owner's comment on #89 says so). Still refused: a frame of another size midway (#625), timestamps that start over
(two encodes joined), 4:4:4 and greyscale (no managed converter is FFmpeg's), odd width with odd height. A phone's
HEVC (4:2:0, 8- or 10-bit, rotated, variable rate) falls inside what is taken; a 4:4:4 screen recording doesn't.

### Backlog "WebCodecs as a decoder"

That row (Maybe) is WebCodecs for H.264, for speed, closed by the measurement; #15 is closed into #89. Whatever is chosen
here replaces it: the row moves to backlog-done.md as Dropped, pointing here, and the chosen work is a new row.

## The options

### 1. Two tiers, exact and approximate

ManagedCodecs as now for what it decodes; for anything else the browser plays, the browser's `VideoDecoder`, the
frames through FastYuv when the browser gives YUV planes and taken as they come (RGB) when it doesn't, marked "not
exact".

- **Costs.** A demuxer for WebM/Matroska and for HEVC/VP9/AV1 in MP4 (packets, timing, the codec configuration
  `VideoDecoder` needs); a description of each (colour, rate, frames, duration) good enough to call; a frame source on
  the page's thread (`VideoDecoder` is not in .NET: the frames cross from JavaScript, one copy each, and `Host.cs`'s
  rules on threads apply); an RGB path into ShotDetector, which takes only YUV frames today (a new upstream API, or
  the page converting RGB back to YUV, which is neither FFmpeg's nor scenedetect's); ranges and resume over a decoder
  that must restart at keyframes. The most to build and the most to keep.
- **The page's marks.** The run card's chip `page` instead of `exact`, a line in the status ("Decoded by your
  browser: the cuts may differ from scenedetect's"), the downloads' file names or first line saying so. Every card
  whose numbers come from the frames (picture, frame types, scores) the same.
- **Checks.** Chromium in `tools/page-check` could run VP9 and AV1 samples to FFmpeg's results byte for byte (they
  were identical here); Firefox's RGB path only "a result, the same shots within a tolerance"; HEVC not at all in CI.
  An approximate tier is checked loosely by nature, which is what exactness first warns against.

### 2. The browser's decoder only for codecs ManagedCodecs lacks

Never for H.264 or MJPEG (always ManagedCodecs, exact as now); the browser's `VideoDecoder` only for HEVC, VP9 and AV1,
and only when it hands YUV planes (I420, I420P10, NV12 de-interleaved): the conversion stays FastYuv's, the frames go
through `Detect(YuvVideo, ...)` as ManagedCodecs' do. Where the browser gives RGB (Firefox) or can't decode, the
refusal of option 3.

- **Costs.** The demuxer and the description as in option 1 (WebM/Matroska; HEVC, VP9, AV1 headers for the colour
  tags), the frames crossing from JavaScript, ranges restarting the decoder at keyframes. No RGB path, no upstream
  change. Still large: a reader per container and codec; WebM alone could come first (VP9 and AV1, the codecs CI's
  Chromium decodes), HEVC in MP4 after.
- **The page's marks.** A chip of its own on the run card, `browser` ("decoded by your browser's VP9 decoder; checked
  identical to FFmpeg's in Chromium, not in your browser"), the status line saying so, the CSVs as they come.
  Exact by construction where the browser is a conformant software decoder, but not shown so for the visitor's own
  browser: that is the "not exact" of #89, said more precisely.
- **Checks.** NativeCheck can't run a browser; `tools/page-check` can: VP9 and AV1 samples (made by
  `make-samples.sh`, libvpx-vp9 and libaom or SVT-AV1) picked in Chromium, their CSVs, shots and timeline files
  against the FFmpeg path's (`Detect(path)`), byte for byte, as every other sample. The description (`YuvVideo`) against
  ffprobe field by field, natively, as for H.264, since the header readers are .NET. HEVC unchecked in CI (no
  hardware decoder): a real-world file in `realworld.sh` can't help either; it stays "browser, unchecked".

### 3. Keep refusing, and say what to do

No browser decoder. The refusal names the codec in a visitor's words and points to what reads it: the `shotdetect`
CLI and its release binaries (FFmpeg reads everything, same results), and for an iPhone, "Most Compatible" in Camera >
Formats (H.264) or an export to H.264.

- **Costs.** Small: the core's `UnsupportedVideoException` messages say the codec FFmpeg would name (`hevc`, `vp9`,
  `av1`, and for WebM/Matroska that the container isn't read), and the page's error state adds a line and a link
  under them. One row.
- **The page's marks.** None new: every result stays exact.
- **Checks.** app-check.mjs and page-check pick a refused sample (an HEVC MP4, a VP9 WebM: `make-samples.sh` makes
  them; nothing decodes them) and expect the message and the link.

## Recommendation

**Option 3 now, then option 2 for VP9 and AV1 in WebM, checked in Chromium byte for byte.** Option 3 costs one row and
helps every refused visitor today, whatever comes later; option 2's measurement here says VP9 and AV1 through
Chromium's software decoders are FFmpeg's frames, so they can be held to the same CSVs as H.264 in page-check rather
than marked approximate. Option 1's RGB tier is the one result the checks couldn't hold to scenedetect's, which is
what this repository exists to avoid. HEVC (phones) stays refused with option 3's message until a hardware decoder can
be checked; that is the honest cost.

## The answer

The owner, 2026-11-02: **option 3 now, then option 2**, as recommended. Two backlog rows, in order:

1. "A refusal that says what to do" (option 3): the codec named, the `shotdetect` CLI and the iPhone's "Most
   Compatible" pointed to.
2. "VP9 and AV1 in WebM through the browser's decoder" (option 2, after the first): WebM/Matroska and the VP9 and AV1
   headers read in the core, the frames from the browser's `VideoDecoder` only as YUV planes, through FastYuv,
   checked byte for byte against the FFmpeg path in page-check's Chromium, a `browser` chip on the run. H.264 and
   MJPEG stay ManagedCodecs'; a browser giving RGB, and HEVC, get the refusal of row 1.

The "Not doing" entry of [2026-10-24-the-page.md](2026-10-24-the-page.md) stands for H.264 (speed); backlog "WebCodecs
as a decoder" is dropped into these rows.
