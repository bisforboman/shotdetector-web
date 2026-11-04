# The reference tone left out of the detection (2026-11-04)

The owner's queue of 2026-11-03 (through the coordinator): the Maybe row "The reference tone left out of the
detection". ShotDetector's `DetectionOptions.TrimReferenceTone` leaves a leading tone (one that starts in the first 5 s)
and a trailing one (ending in the last 5 s) out of the detection, as `Detect(path)` does with FFmpeg finding the tone;
frames from the caller take the tone in `YuvVideo.ReferenceTone`, which the page can find (`Mp4Audio`'s
`ReferenceToneFinder`, the same definition). Two things the row doesn't settle.

## What there is

- ShotDetector 3.28.0: the trim sets the run's time range itself, and `TrimReferenceTone` with the caller's tone is "not
  for ranges": `ScoreRange` refuses a time range (its remarks). The page runs every detection as ranges, a one-worker
  run as one range from frame 0 merged (so that a stop leaves scores to resume from), a long video split across
  workers, a resumed run's rest from a keyframe.
- The page finds the tone in the sound's worker, alongside the run, not before it: the sound reads only the audio
  (seconds for most files), but the run doesn't wait for it.
- The tone sample (`tone-silence-320x240-25.mp4`): tone 0-6 s, silence, tone 9-15 s. Trimmed, the detection runs from
  6 s to 9 s.

## Question 1: how, when ranges can't trim

**A. Ask ShotDetector for the trim in ranges first (recommended).** `ScoreRange`/`Merge` taking the caller's tone (the
trim only decides where the shots start and end, which `Merge` decides anyway), then build: every run trims, split,
stopped and resumed alike. The row waits for that release.

**B. Build now on `Detect`.** With the option on, the page runs ShotDetector's `Detect` on one worker, not ranges: no
split, and a stopped run isn't kept to resume.

**C. Not now.** The row stays Maybe.

## Question 2: when the tone is found

**A. The run waits for the sound's worker (recommended).** The option is off by default; when on, the run starts once
the sound's worker has the tone (it reads the audio only), and the status says it is waiting for it.

**B. The run's own worker first.** The run reads the audio itself before the frames (one more pass over the audio in
that worker), independent of the sound's worker.

## Answer

The owner, 2026-11-04, through AskUserQuestion: **1A**, ask ShotDetector for the trim in ranges first, then build so
every run trims; **2A**, the option off by default, and with it on the run waits for the sound's worker's tone.
Asked of the shotdetector coordinator the same day.
