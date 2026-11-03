# Correcting the cuts on the page (2026-11-02)

Issue [#90](https://github.com/bisforboman/shotdetector-web/issues/90), backlog row "Correcting the cuts on the page":
a viewer removes a false cut, adds a missed one at the playhead, or moves a cut by a frame (`,` / `.`), with undo and
redo. Every download then comes from the corrected list, made by ShotDetector and not by the page. ShotDetector 3.25.0
added `ShotDetection.LoadScenes(YuvVideo, TextReader, column, options)` for this (shotdetector#647), and the page pins
3.26.0.

## What the issue settles

- **The controls:** remove, add at the playhead, a frame earlier or later, undo and redo.
- **The detected list stays:** corrected cuts are drawn apart from detected ones in the cuts lane and marked `page`,
  because they are the viewer's edit and not the detector's.
- **The exports:** every export comes from the corrected list. It is exact when it equals `LoadScenes(path)` on
  ShotDetector's FFmpeg path for the same list. NativeCheck has a check for that.
- **Keeping corrections:** the corrected list is kept with the kept run in IndexedDB.

## What the page does (the row's choices, no question)

- **An edit, not a list.** The page keeps the corrections as two sets against the detected cuts: the frames removed and
  the frames added. A move is a removal and an addition. The corrected cuts are then (detected − removed) ∪ added.
  Because the edit is stored apart from the detection, it can be applied again to a resumed run's longer list: the
  stopped part's corrections stay, and the rest's detected cuts come in as they are.
- **The export.** Api's `Correct(starts)` takes the corrected shots' first frames, counted from 0. It writes them as a
  one-column scene list CSV (`Start Frame`, 1-based, as scenedetect's list-scenes numbers them) and keeps
  `LoadScenes(video, csv, "Start Frame", run's options)` as the result the downloads are made from: the scenes CSV, the
  shots as JSON and HTML, the editors' timeline files and the YouTube chapters. For a stopped run, `EndTime` is the
  frame where the run stopped, so the corrected result also ends there. When nothing is corrected (or every edit is
  undone), the downloads are `Detect`'s result again, unchanged.
- **The stats CSV stays the run's.** It holds each frame's scores, which no edit changes, and `LoadScenes` decodes
  nothing, so it has no stats.
- **What a run's options still do.** `LoadScenes` uses `MinSceneLength` only for `DropShortScenes` and
  `MergeLastScene`, and the page sets neither. So a cut added a frame after another is kept, as the viewer asked
  (checked: two cuts 3 frames apart come back as they were given).
- **Marks.** The run card stays `exact`: its files are ShotDetector's, checked byte for byte against the FFmpeg path's
  `LoadScenes`. The cuts lane's corrected ticks, the shots table's corrected rows and the edit's count are `page`, and
  their tooltips say so.

## What `LoadScenes` gives that `Detect` didn't

Two samples show a difference between a detected list and the same list loaded back unedited. It shows on the FFmpeg
path too (`Detect(path)` against `LoadScenes(path)`), so it is ShotDetector's behaviour, not the page's description:

- **A variable frame rate** (`vfr-320x240.mp4`): `LoadScenes` times each cut at frame / average rate (2.022 s), while
  `Detect` uses the frame's own pts (2.000 s).
- **An edit list that ends before the media** (`edit-end-352x288-25.mp4`): the last shot runs to nb_frames, 114 frames
  (OpenCV's count), where `Detect`'s ends at the 75 frames FFmpeg plays.

These were sent to the shotdetector coordinator on 2026-11-02, as scenedetect's fidelity or a bug for upstream to
decide. Until it decides, the page follows ShotDetector as it is.

## Questions for the owner

### A finished run's corrections on a later visit

Today only a stopped run is kept (decision 2026-10-30-resume-across-visits.md). The issue says the corrections are kept
"with the kept run". A finished run that a viewer has corrected is kept nowhere, so a reload loses the corrections.

1. **Keep a corrected run, finished or stopped.** A finished run with corrections becomes the kept run (still only the
   last one), shown again with its corrections when its file is picked. Forget drops it. A finished run without
   corrections is kept as it is today: not at all.
2. **Only with a stopped run.** The issue's words: a finished run's corrections last for the visit.

### Corrections where `LoadScenes` differs from `Detect`

On a variable-rate file, or one whose edit list ends early, the first correction changes the times of every cut, or the
last shot's end, in the downloads (above).

1. **Ship as ShotDetector is.** The run card's tooltip says so on such a file, and the page follows upstream's answer.
2. **Refuse corrections on such files** until upstream answers.

## Answers

The owner, 2026-11-02:

- **A finished run's corrections:** 1, keep a corrected run, finished or stopped.
- **Where `LoadScenes` differs:** 1, ship as ShotDetector is.

Upstream, the same day: scenedetect 0.7.2's own load-scenes gives `LoadScenes`' numbers on both samples, so it stays
as it is; ShotDetector adds an overload that takes a `DetectionResult` and the edited cuts and keeps that run's frame
times and length (shotdetector#707). The page moves to it once it ships (backlog, "A corrected list timed as its run").
