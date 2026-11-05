# A WebM without a DefaultDuration (2026-11-05)

The owner's split of 2026-11-04 (through AskUserQuestion): the row "WebM without a DefaultDuration", the second of the
three "WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4" became. `WebmInput` refuses a WebM whose
video track has no DefaultDuration, as FFmpeg's average rate then comes from the frames it probes. One thing the row
doesn't settle.

## What FFmpeg 8.1 does (libavformat/demux.c, matroskadec.c)

- Without a DefaultDuration (or the deprecated FrameRate element, which stands for one), matroskadec gives no
  `avg_frame_rate` and no packet durations.
- `avformat_find_stream_info` reads packets until, for the video stream, 40 frame durations are counted (20, doubled for
  a time base coarser than 0.5 ms, Matroska's 1 ms), or 5 MB is read, or the frames read span 5 s. Over each block's
  timestamp it keeps `ff_rfps_add_frame`'s statistics: for each standard rate (n/12 fps up to 30, then 31 to 60, 80,
  120, 240, and 24, 30, 60, 12, 15, 48 × 1000/1001), the error of the timestamps against that rate's grid.
- `ff_rfps_calculate` then sets `r_frame_rate`: 1000/gcd when the durations after the first four share a gcd over
  2 ms (a constant rate in whole milliseconds), else the standard rate with the least error under 0.01.
- `avg_frame_rate` is set to `r_frame_rate` only when the mean probed duration is within one tick (1 ms) of
  `r_frame_rate`'s; otherwise FFmpeg reports none (0/0).

Measured (the DefaultDuration of libvpx-vp9 samples cut out, ffprobe 8.1): 25, 30000/1001, 24000/1001, 60, 12 and
17 fps come out as those rates; a variable rate (frames at 40 ms, then 50 ms) has `r_frame_rate` 25 and no
`avg_frame_rate`, and ShotDetector's FFmpeg path (`Detect(path)`, 3.28.0) reports 25 fps for it: `r_frame_rate` when
the average is unknown.

## Question: what the core does

**A. FFmpeg's estimate, ported (recommended).** `WebmInput` works out `r_frame_rate` and `avg_frame_rate` as above
from the blocks' timestamps, sizes and order (the probing's stops included), and the video's rate is the average, or
`r_frame_rate` without one, as `Detect(path)` reports. Samples: constant rates in whole and in uneven milliseconds
(25, 30000/1001), and a variable rate without an average. NativeCheck holds the description to FFmpeg's as for any
file.

**B. Only a constant rate.** As A when the durations share a gcd (the first path); any other file without a
DefaultDuration stays refused, saying so.

**C. Not now.** The row stays Maybe.

## Answer

The owner, 2026-11-05, through AskUserQuestion: **A**, FFmpeg's estimate, ported.
