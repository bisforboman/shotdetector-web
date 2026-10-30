# Resuming a stopped run across visits (2026-10-30)

Backlog "Resume after Stop, across visits": a stopped run's scores kept in the browser (`RangeScores.ToBytes`, in
IndexedDB), and a later visit to the same file with the same options scoring a range from where it stopped and merging
the two (`ShotDetection.Merge`), exactly the whole video's result. The owner answered on 2026-10-30, the live shot
table's question first, the others once ShotDetector 3.22.0 had shipped it.

## The live shot table

### Question

A run that can be resumed must go through `ScoreRangeAsync` from frame 0, so that it leaves scores to merge. A range
tells no shots (`DetectionOptions.OnShot` isn't honoured there: shots are decided in `Merge`), so such a run would show
no shots until Stop or its end, where a run on `DetectAsync` (today's one-worker run) shows each shot as it is found.
Which way?

### Choices

1. **Ask upstream and wait.** An issue for ShotDetector: `ScoreRange` from frame 0 tells `OnShot` the shots `Detect`
   would. Resume is built once it ships, and nothing is lost meanwhile.
2. **Resumable, no live table.** Every run through `ScoreRangeAsync`; the table fills only at Stop or the end until
   upstream adds `OnShot`.
3. **Only long runs resumable.** Runs split across workers (over 2 minutes of video), which already show no live table;
   one-worker runs keep live shots and start over after a reload.
4. **Drop it.** Pause and resume within a visit is there (backlog-done, "Pause and resume a run"); resuming across a
   reload isn't worth the complexity.

### Answer

**1.** Filed as [shotdetector#598](https://github.com/bisforboman/shotdetector/issues/598) the same day. The backlog
row waits on it, under Waiting.

## The kept run on a later visit

### Question

ShotDetector 3.22.0 shipped #598 the same day, and the row was worked. When a file whose stopped run is kept is picked
again on a later visit, what does the page do?

### Choices

1. **Show it at once.** The form takes the kept run's options, the file opens and the stopped part's result (shots,
   stats, CSVs) is shown as a stopped run's, with Resume and Forget beside Detect; Detect starts over.
2. **Offer Resume only.** A Resume button, only when the form's options match the kept run's; the stopped part shown
   when Resume is clicked, just before the run goes on.

### Answer

**1** (the owner, 2026-10-30).

## How many kept

### Question

How many stopped runs does the browser keep (IndexedDB, keyed by the file's name, size and modified time and by the
options)?

### Choices

1. **One per file**, whatever its options, at most 10 files.
2. **One per file and options**, at most 10 in all.
3. **Only the last**: one stopped run in all, of any file.

### Answer

**3** (the owner, 2026-10-30): a stop replaces it, a finished run of its file and options or Forget drops it.

## Resuming a long video

### Question

How is the rest of a run split across workers (a video over 2 minutes) detected?

### Choices

1. **Split by the rest**: the frames left planned as a new run of that length would be, split across workers above 2
   minutes (no live table, as split runs), else one worker with live shots.
2. **Always one worker**, with the live table; up to 3.6 times as slow for a long rest.

### Answer

**1** (the owner, 2026-10-30).

## Firefox

### Question

The row asks for the resume across a reload in headless Chromium and Firefox. How is Firefox checked?

### Choices

1. **In CI, that part only**: the page check also runs the stop, reload and resume in Playwright's Firefox, pinned by
   the same lockfile; CI installs Firefox for the page check.
2. **Once in the session, not in CI.**

### Answer

**1** (the owner, 2026-10-30).

## As built

- Every one-worker run is one range from frame 0 (`ScoreRangeAsync` with `OnShot`, then `Merge` without it, so the
  shots are told once): `Detect`'s result byte for byte (every check's runs go through it), and its scores are there to
  keep when it is stopped. A split run's ranges tell no shots, the merge does, as before.
- The rest starts at the last place a range can start (one past a keyframe) at or before the last frame scored
  (`Mp4Input.Continue`), decoding from that keyframe as a split run's range does, instead of the row's "dropping frames
  up to the one before": the decode is the same, at most a keyframe interval is scored again, and those frames, which
  both have, are compared by ShotDetector. Earlier ranges starting there or later are scored again whole. With no such
  place but frame 0 the run starts over.
- The kept bytes (`Api.Kept`) are the ranges' `RangeScores.ToBytes` and the picture measures joined, each hashed, behind
  the frame the rest starts at, which the page needs to plan a split rest; a `Blob`, which IndexedDB stores as it is.
- On a resume on one worker the page shows the stopped part's shots but its last, which ShotDetector tells again to
  where it really ends (one detector, as the page sends).
