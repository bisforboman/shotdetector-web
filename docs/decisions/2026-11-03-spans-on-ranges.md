# Still pictures and black spans on split and resumed runs (2026-11-03)

The owner's queue of 2026-11-03, after ShotDetector 3.28.0 (#143): the row "Freeze spans and ShotDetector's black
spans on split and resumed runs". 3.28.0's ranges take `FreezeDetect` and `BlackDetect` (shotdetector#712): `Merge`'s
`FreezeSpans` and `BlackSpans` equal `Detect`'s however the run is split, a split run scoring again the ranges
`FirstRangeToScoreAgain` names (with `RangeFrames(video, earlier, options, first, end)`'s frames), and `RangeScores`
written in format v2 only when either option is set. Two things the row doesn't settle.

## What there is

- The page's still pictures (decision 2026-11-01-freeze-spans.md, the owner's A) come from FrameReader's
  `FreezeDetector`, fed the frames as decoded, only in a run that reads every frame in order: a split run (over 120 s)
  and a resumed run show none, and the card says why. NativeCheck holds them to `ffmpeg -vf freezedetect` on the file.
- ShotDetector's `FreezeDetect` compares "the frames as detection reads them (whole, every frame, deinterlaced when
  deinterlacing)": the same `FreezeDetector`, but a deinterlaced run's frames deinterlaced.
- The page's black spans come from FrameReader's `PictureMeter`, whose per-frame measures every range already carries
  (`RangeBytes`) and a kept run keeps: they are on split and resumed runs today. ShotDetector's `BlackSpans` equal
  them on every sample (measured 2026-11-01; the same `PictureMeter`).
- A range scored again for a still picture decodes from that still's first frame, which can be far back: a long still
  across a range's start costs a second decode of it, on one worker, after the others are done.
- A kept run made before this change has v1 ranges, without either option: it must still resume.

## Question 1: a deinterlaced run's still pictures

**A. ShotDetector's for every run (recommended).** Every run sets `FreezeDetect`, and the page shows
`FreezeSpans`, split and resumed runs too. A deinterlaced run's spans are of the deinterlaced frames, as the run reads
them: the card says so, and NativeCheck holds them to `yadif` then `freezedetect` (FFmpeg's C yadif over 8 bits).
One source; FrameReader's `FreezeDetector` leaves the core.

**B. ShotDetector's, but not when deinterlacing.** As A, but a deinterlaced run doesn't set `FreezeDetect` and
shows no spans ("not looked for in a deinterlaced run"). The spans are always the file's frames'.

**C. Keep the page's own on runs in order, ShotDetector's on split and resumed ones.** Two sources that differ on a
deinterlaced run that is split or resumed.

## Question 2: black spans

**A. Keep the page's own (recommended).** They equal ShotDetector's, already come with every range and kept run,
and cost nothing more. `BlackDetect` stays off.

**B. ShotDetector's.** Every run sets `BlackDetect` too and shows `BlackSpans`; the page's own come out. One more
measure in every range for the same numbers.

## Answer

The owner, 2026-11-03, through AskUserQuestion: **1A**, ShotDetector's still pictures on every run, a deinterlaced
run's of its deinterlaced frames; **2A**, the page's own black spans, `BlackDetect` off.
