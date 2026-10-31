# Interlaced H.264 (2026-10-30)

Backlog "Interlaced H.264", chosen by the owner on 2026-10-30. Today `Mp4Input`'s constructor refuses any SPS with
`frame_mbs_only_flag` 0. This finds out what ShotDetector's FFmpeg path does with interlaced H.264, what its
frame-source path (`Detect(YuvVideo, ...)`) would need to give the same CSVs, and what is read where.

Measured on 2026-10-30 against `main` at `e2c3e1b` (ShotDetector 3.22.0, ManagedCodecs 0.2.4), with FFmpeg 8.1.3
(BtbN's shared build the checks pin), its sources at `n8.1.3` (`h264_parser.c`, `h264_slice.c`) and ShotDetector's at
`46efe0a`, and a throwaway change that switched the refusal off. The files: `testsrc2`, `mandelbrot` and
`smptehdbars` at 50 fps, 320x240, woven into 25 fps frames by `tinterlace=interleave_top`, then libx264 with
`-flags +ilme+ildct` (MBAFF, top field first; and `-x264-params bff=1` after `setfield=bff`), the same with the
picture timing SEI removed (`filter_units=remove_types=6`), and `-x264-params fake-interlaced=1` (`frame_mbs_only_flag`
0 with every picture a progressive frame).

## What was found

### The FFmpeg path: deinterlacing off by default

ShotDetector's `DetectionOptions.Deinterlace` is `Off` by default, as scenedetect (which never deinterlaces), and
`Detect(path)` then gives the frames as the decoder weaves them. `On` runs yadif (mode 0, one frame out per frame in) on
every frame, `Auto` only when the stream's field order is interlaced (`tt`, `bb`, `tb`, `bt`); `DetectionOptions.Yadif`
(FastYuv's `Yadif`) computes only the rows the resize reads, bit-exact with FFmpeg's filter. Its CLAUDE.md's
"Interlaced video" gotcha is about scenedetect, not this: opencv-python can't convert interlaced frames and gives one
nearly black frame throughout, so CI checks the off case only informationally and `--deinterlace auto` exactly against
scenedetect on a lossless yadif copy.

With the refusal switched off, every run NativeCheck makes on the MBAFF files (top and bottom field first) came out
the FFmpeg path's byte for byte: the `YuvVideo` fields, the scenes and stats CSVs, the shots as JSON and HTML, the
packets against ffprobe, the ranges merged, four decoder threads, a run stopped half-way. ManagedCodecs' frames are
FFmpeg's, and with deinterlacing off nothing else differs. (The picture check flagged colour bars, which
`smptehdbars` has: a sample without bars uses another source.)

### What FFmpeg says about interlacing

- The stream's `field_order` (ffprobe's, ShotDetector's `FieldOrder`, which `Auto` goes by) is the H.264 parser's
  (`h264_parser.c`): for a frame, the picture timing SEI's `pic_struct` when the SPS has `pic_struct_present_flag`
  (3 or 5: `tt`, 4 or 6: `bb`, anything else `progressive`), else the field order counts (top before bottom: `tt`,
  after: `bb`, equal: `progressive`); for a field pair, the order of the two fields. The files gave `tt`, `bb`, `tt`
  without the SEI (from `delta_pic_order_cnt_bottom`), and `progressive` for `fake-interlaced`.
- Each frame's `interlaced_frame` and `top_field_first` (ffprobe's `-show_frames`; what yadif's automatic parity
  reads) are the decoder's (`h264_export_frame_props`): interlaced from `pic_struct` (and the SEI's `ct_type`) when
  present, else from the decoding process (a field pair or an MBAFF frame); top first from the field order counts,
  else from `pic_struct`, else whenever interlaced. All 112 frames of the MBAFF files were `1,1` (top first) or `1,0`;
  `fake-interlaced`'s `0,0`.

Neither is in ManagedCodecs' API: `VideoFrame` has no interlacing flags (its `FieldStatistics` count field pictures for
tests), and its slice headers and SEI parser are internal. The backlog's frame-types row set the rule for this:
no parsing of slice headers here, it goes to ManagedCodecs (ManagedCodecs#221).

### The frame-source path: refuses deinterlacing

`Detect(YuvVideo, ...)` throws an `ArgumentException` for `Deinterlace` other than `Off` or any `Yadif`
(`VideoReader.Frames.cs`: "pass deinterlaced frames"), `YuvVideo` has no field order (the reader sets `Unknown`, so
`DetectedVideo.FieldOrder` and `Deinterlaced` can't be a file's), and `YuvFrame` no per-frame flags. Deinterlacing the
frames here before passing them would be a copy of what the file path does (FrameDecoder's row-wise yadif with its
frame order, first and last frames, and parity); the rule is to report it there.

What upstream needs, for `Auto` and `On` to give a file's results:

- `YuvVideo.FieldOrder` (FFmpeg's stream field order, as `DetectedVideo.FieldOrder`), which `Auto` goes by and
  `DetectedVideo` reports;
- `YuvFrame.Interlaced` and `TopFieldFirst` (FFmpeg's frame flags), yadif's parity per frame;
- `Deinterlace` with `Yadif` accepted for frames from the caller (row-wise only: FFmpeg's filter isn't there), the
  frames held as FrameDecoder's row-wise path holds them (previous, current, next; the first frame its own previous, the
  last its own next), `DetectedVideo.Deinterlaced` reported;
- not yet: deinterlaced ranges (`ScoreRange` leaves `Deinterlace` out of the fingerprint and refuses it): a range's
  first frame needs the frame before it, its last the one after. The page runs a deinterlaced video on one worker
  until then.

### Field pictures (PAFF)

ManagedCodecs decodes field pictures, but no encoder at hand makes them: libx264 codes interlaced video as MBAFF
frames only, and FFmpeg has no other interlaced H.264 encoder. CLAUDE.md's rule is a sample for anything the scope
widens to, so field pictures stay refused until a sample can be made. ManagedCodecs' `FieldStatistics` is for its
tests; telling a field picture apart needs `field_pic_flag` from the slice header, which ManagedCodecs would report
with the frame flags.

## Choices

The owner's answers, 2026-10-30:

1. **Upstream, ShotDetector**: filed as drafted,
   [shotdetector#614](https://github.com/bisforboman/shotdetector/issues/614): `YuvVideo.FieldOrder`,
   `YuvFrame.Interlaced` and `TopFieldFirst`, and `Deinterlace` with `Yadif` accepted for frames from the caller.
2. **The field order and the frames' flags**: from ManagedCodecs, not parsed here (as the frame types,
   ManagedCodecs#221): [ManagedCodecs#225](https://github.com/bisforboman/ManagedCodecs/issues/225), each frame's
   interlaced and top-field-first flags and the field order as FFmpeg computes them, and whether a frame was two
   field pictures. The description leaves the field order out until a release has them.
3. **This row ships MBAFF with deinterlacing off**: `Mp4Input` no longer refuses `frame_mbs_only_flag` 0; three
   samples (MBAFF top field first, bottom field first, and libx264's `fake-interlaced`), each checked with
   `trace_headers` and ffprobe in `make-samples.sh`, through the native and wasm checks byte for byte. Field pictures
   stay unchecked: no encoder here makes a sample, and nothing tells them apart without ManagedCodecs#225 (JM's MP4s,
   one field a sample, stop at ShotDetector's constant-rate check). Deinterlacing, the field order and field pictures
   are the backlog's "Deinterlacing interlaced H.264" row, waiting on both issues.

## Afterwards

ShotDetector 3.23.0 (shotdetector#614, #619) and ManagedCodecs 0.4.0 (#225) shipped what the choices above waited on,
and the backlog row "Deinterlacing interlaced H.264" took them on 2026-10-31: `Mp4Input` describes the field order
(`H264Decoder.FieldOrder`) and each frame's flags, the page's Deinterlace option (off, auto, on) runs FastYuv's yadif on
one worker (ShotDetector's ranges still refuse it, shotdetector#628), and frames coded as field pictures are refused
by ManagedCodecs' `IsCodedAsFields`, still without a sample.
