# VP9 and AV1 in WebM through the browser's decoder: how (2026-11-02)

Backlog "VP9 and AV1 in WebM through the browser's decoder", the second row of issue #89's decision
([2026-11-02-browser-decoder.md](2026-11-02-browser-decoder.md), option 2), chosen by the owner on 2026-11-02 to be built
after "A refusal that says what to do". The row leaves these questions to a decision before the code: where the WebM
file is read, how the browser's frames reach ShotDetector, what checks hold the result to `Detect(path)` when no check
but page-check runs a browser, and which of the page's run kinds (threads, split, resume) a browser-decoded run takes.

## What is known

- Chromium 153's software `VideoDecoder` gave FFmpeg's frames for VP9 profile 0 and 2 and AV1 8- and 10-bit, as I420
  and I420P10 (the decision above). The same decoders run in a dedicated worker (WebCodecs is exposed there).
- ffprobe on WebM from FFmpeg's muxer (libvpx-vp9, libaom-av1; 2026-11-02, the pinned FFmpeg 8.1.3): time base 1/1000,
  `avg_frame_rate` = `r_frame_rate` from the track's DefaultDuration (25/1, 30000/1001), `nb_frames` N/A, the format
  duration from the Segment's Info, `start_pts` the first block's (1400 with `-output_ts_offset 1.4`), `color_range` tv
  or pc and `color_space` from the bitstream (VP9's uncompressed header; AV1's sequence header) where the Colour element
  doesn't say, colour primaries and transfer unknown, `chroma_location` unspecified; `pix_fmt` yuv420p, yuv420p10le, or
  yuv422p for VP9 profile 1 (which ShotDetector's frame source takes since #627).
- ManagedCodecs 0.5.0 reads MP4 and AVI; it has no Matroska demuxer. ShotDetector's frame source takes everything above.
- `VideoFrame.colorSpace` isn't the file's (Chromium says bt709 for an untagged stream): the description is read from
  the file, as `Mp4Header`/`SpsInfo` read an MP4's.

## Choices

1. **The WebM file is read in the core.** A `WebmInput : VideoInput` with a reader of its own (`MatroskaReader`: the
   EBML header, Segment Info, Tracks with their Colour and CodecPrivate, the Clusters' SimpleBlocks and BlockGroups;
   video without lacing, as every muxer writes it) and the codecs' headers for the colour (`Vp9Header`, `Av1Header`).
   `VideoPackets` from its blocks (the bitrate lane, ranges later). Same as `Mp4Header`, it says what FFmpeg says, and
   NativeCheck compares it with ffprobe field by field, natively. A Matroska demuxer in ManagedCodecs would replace the
   reader later; asking for one is an upstream issue, not a wait.
2. **The frames cross one at a time.** The core gives each packet to the host's decoder and awaits its frame: an
   interface `IBrowserDecoder` (configure with the codec string and CodecPrivate, decode a packet, take the next frame
   into .NET's buffers, flush), `[JSImport]`s in the worker over `VideoDecoder` (`prefer-software` first, as measured),
   `copyTo` straight into a .NET-owned buffer (a `Span` view, as `BlobStream` reads). The frames come in display order
   with the timestamps the core gave their packets, so `Pts`, `FrameTimestamps` and the rate grid are the core's own.
   `VideoInput` gains an asynchronous frame path; the MP4 and AVI inputs keep theirs.
3. **Natively, FFmpeg stands in for the browser.** NativeCheck runs each WebM sample through `WebmInput` with frames from
   `ffmpeg -f rawvideo` (the pinned FFmpeg, in display order) and compares every detector's CSVs, JSON, HTML and
   timeline files with `Detect(path)`, as for any sample: the description, the timing and the conversion held there.
   page-check then picks the same samples in Chromium (the browser's decoder) to the same files byte for byte.
   app-check.mjs (Node: no `VideoDecoder`) opens them, says they need the browser's decoder, and checks nothing more.
4. **One worker, no threads, no split, no resume, in this row.** A browser-decoded run goes on the page's worker
   (Chromium's decoder has threads of its own), sequentially, with progress, pause, stop and cancel; a stopped run is
   shown, not kept. Ranges (a `VideoDecoder` per range from its keyframe) and resume are a later row if asked.
5. **Only YUV frames.** A browser that says no to the codec, or whose first frame isn't I420, I420P10 or NV12 (Firefox's
   BGRX), is refused in row 1's words: what reads the file instead.
6. **Marked `browser`.** The run card's chip `browser` ("decoded by your browser's VP9 decoder: checked identical to
   FFmpeg's in Chromium, not in your browser") and the status line; the downloads as they are. The frame types' lane
   and card (from the packets' key flags: I or P, as FFmpeg's vp9 and libdav1d's say for these files, held by
   FrameTypeCheck against ffprobe) and the picture's measures work as for any run.
7. **Samples.** `make-samples.sh`: VP9 profile 0 (untagged, and BT.709 full range), VP9 profile 2 (10-bit), AV1 8-bit
   with Opus, AV1 10-bit; each checked with ffprobe for what it has. WebM's audio is described (codec, rate, channels as
   ffprobe), not measured (`NotMeasured`: not AAC).

## The answer

The owner, 2026-11-02, each as recommended: the core's own WebM reader (1; an upstream issue to replace it later), one
worker only (4: no threads, split or resume in this row), and FFmpeg standing in for the browser natively (3).
