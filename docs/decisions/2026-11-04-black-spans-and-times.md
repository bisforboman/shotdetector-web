# Black spans and the spans' times from ShotDetector (2026-11-04)

After [2026-11-03-spans-on-ranges.md](2026-11-03-spans-on-ranges.md) (PR #147: ShotDetector's still pictures on every
run, the page's own black spans, `BlackDetect` off) and its commit 690de61 (the still pictures timed by the stream's own
pts, ShotDetector's times plus the stream's start). The owner was asked again in a session that had started the same row
before #147 merged; its answers change two things.

## What there is

- ShotDetector 3.28.0's `BlackDetect` gives `BlackSpans` on every run, split and resumed (the ranges carry each frame's
  judgement; `Merge` works the spans out), as its `FreezeDetect` gives `FreezeSpans`. Its `BlackSpans` equal the page's
  own (FrameReader's `PictureMeter`, the same counts) on every sample, frames and pts (measured 2026-11-01). A
  deinterlaced run's are of its deinterlaced frames (`JudgeBlack`), as its still pictures are.
- ShotDetector times both from the video stream's start, as `ffmpeg -vf blackdetect,freezedetect` prints them; the page
  timed its spans by the stream's own pts (`-copyts`). They differ on a stream that doesn't start at 0:
  vfr-fragmented-320x240.mp4 starts at 0.08 s, its still at 5.6 s from the start, 5.68 s by pts. The frames, and so the
  timeline, are the same either way.

## Questions, and the owner's answers (2026-11-04)

1. **Where the black spans come from.** ShotDetector's for every run (one source), or the page's own (2026-11-03's 2A).
   **Answer: ShotDetector's**, as the still pictures.
2. **The spans' times.** ShotDetector's (from the stream's start), or the stream's own pts, as before.
   **Answer: ShotDetector's.** The card says "from the stream's start"; PictureCheck compares with ffmpeg without
   `-copyts`.

## How (not asked)

- The page's options say `black: true` (as `freeze: true`); `DetectRequest.Black` sets `BlackDetect` with `d=0` (every
  span: the card lists every one and marks those blackdetect's default 2 s keeps, by its own rule, `check_black_end`).
  A run kept before (its options without `black`) resumes with `black: false`, its ranges scored without: its black spans
  are then the page's own measures', timed from the stream's start too.
- `PictureFreeze.Of` no longer adds the stream's start.
