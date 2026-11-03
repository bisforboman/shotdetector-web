# Freeze spans on the page (2026-11-01)

The owner's queue of 2026-11-01, after the 3.26.0 bump (#124): show still pictures (FFmpeg's `freezedetect`) in the
picture panel's lane and card, marked measured, checked by NativeCheck against `freezedetect`; and use ShotDetector's
`BlackSpans` for the black spans if they equal the page's own. The queue asks how a measure that reads the video in
one pass fits the page's split and resumed runs, which read ranges, and for a decision first if that needs one. It
does.

## What there is

- ShotDetector 3.26.0's `DetectionOptions.FreezeDetect` gives `DetectionResult.FreezeSpans`, and `BlackDetect`
  gives `BlackSpans`. `Detect(YuvVideo, frames)` honours both. `ScoreRange`/`ScoreRangeAsync` refuse both, tried
  2026-11-01: "FreezeDetect isn't available for ranges: freezedetect compares across them; run Detect on the frames",
  and "BlackDetect isn't available for ranges yet".
- The page runs every detection as ranges. A one-worker run is one range from frame 0 merged without its shots,
  `Detect`'s result byte for byte, so that a stopped run leaves scores to resume from (`Api.Detect`). A video over
  `SPLIT_SECONDS` (120 s) is split across workers, and a resumed run scores the rest from a keyframe. So neither
  option can reach the page as it is.
- FrameReader 2.20.0's `FreezeDetector` is `freezedetect`'s arithmetic on frames of the caller's own, fed in order,
  holding the still picture's first frame. It can't start mid-video: a still picture's first frame may be any distance
  back. Its state can't be saved (no bytes, as `PictureMeasures` has).

Measured this session:

- **Black spans:** ShotDetector's `BlackSpans` (`Detect` with `BlackDetect`, `d=0`) equal the page's own
  (`PictureSummary.Black`, from FrameReader's `PictureMeter`) on all 34 samples: frames and times. Only `fades` and
  the Sintel trailer have any, 11 spans in all. They are the same counts: ShotDetector measures with the same
  `PictureMeter`. But ranges refuse `BlackDetect`, so the page keeps its own spans for now. Nothing to report as a
  difference; when ShotDetector takes `BlackDetect` in ranges ("yet"), a row can switch.
- **Freeze, cost:** `FreezeDetector.Add` on Sintel's trailer (854x480, 1253 frames), natively: 0.15 ms a frame, about
  3% of a default run's 4.7 ms a frame. It found 1163 to 1220, ShotDetector's `FreezeSpans` on the same file.

## The question: which runs show freeze spans

**A. The runs that read every frame in order (recommended).** The core feeds `FreezeDetector` the frames as the run
reads them, as `PictureMeter` is fed (`VideoInput.Measured`): a one-worker run from frame 0, on the page's worker or
on threads. A stopped run shows the spans up to where it stopped, one still running there without an end
(`freezedetect` at a file's end). A split run (over 120 s) and a resumed run show no spans: the card says why ("not
measured: a split run reads the video in ranges, and freezedetect compares across them"). Costs about 3% of a run,
no second decode. `?workers=1` measures a long video in one pass.

**B. A as well, plus a pass of its own where A can't.** For a split or a resumed run, a worker of its own decodes the
whole file in order for `FreezeDetector` alone, beside the run, as the sound's worker does. Every run gets spans, but
a split run decodes the video twice and that worker takes a core the ranges would use. The most code of the three.

**C. Always a pass of its own.** Freeze spans come from their own worker for every file, whatever the run, as the
sound does. One model, independent of stop, split and resume, but every file is decoded twice.

**D. A, and ask ShotDetector for a freeze state in `RangeScores`.** With it, a resumed run could continue the
comparison from the stopped run's last still picture. Split runs still couldn't (the ranges run at once), so it helps
only resume.

## Choices made here (not asked)

- The frames compared are the frames as decoded, not cropped, not deinterlaced, as the picture's other measures are:
  `ffmpeg -vf freezedetect` on the file, which NativeCheck compares exactly (spans, frames and times). ShotDetector's
  `FreezeDetect` compares deinterlaced frames when a run deinterlaces; the page's are the file's.
- `freezedetect`'s defaults: `n=0.001` (-60 dB), `d=2` s. FrameReader's `FrozenSpan` gives the pts; the card and
  lane show seconds from the stream's start, as the black spans do.
- FrameReader's `FreezeDetector` in the core, not ShotDetector's `FreezeDetect`: ranges refuse that, and the core
  already measures the picture with FrameReader's `PictureMeter` (shotdetector's decision 2026-10-31).

## Answer

The owner, 2026-11-01: **A**, the runs that read every frame in order. A split or a resumed run shows no freeze
spans, and the card says why.
