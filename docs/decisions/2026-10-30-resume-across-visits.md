# Resuming a stopped run across visits (2026-10-30)

Backlog "Resume after Stop, across visits": a stopped run's scores kept in the browser (`RangeScores.ToBytes`, in
IndexedDB), and a later visit to the same file with the same options scoring a range from where it stopped and merging
the two (`ShotDetection.Merge`), exactly the whole video's result. The owner answered on 2026-10-30.

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
