# Fragmented MP4, variable frame rate, rotation (2026-10-30)

Backlog "Fragmented MP4, variable frame rate, rotation", chosen by the owner on 2026-10-30. Today `Mp4Input`'s
constructor refuses each of the three. This finds out, for each, what is missing and where: the description here
(`Mp4Header`, `SpsInfo`, `YuvVideo`), ManagedCodecs, or ShotDetector's frame-source path; and the order to widen them in.

Measured on 2026-10-30 against `main` at `b3d6788` (ShotDetector 3.21.0, ManagedCodecs 0.2.3; 3.22.0's and 0.2.4's
documentation read too), with FFmpeg 8.1.3 (BtbN's shared build the checks pin) and a throwaway worktree in which the
three refusals were switched off and NativeCheck run on files made for the purpose (none is in the repository):
`testsrc2`, `mandelbrot` and `smptehdbars`, 2 s each at 25 fps, 320x240, libx264's defaults (B-frames), then

- fragmented by `-c copy -movflags`: `frag_keyframe+empty_moov`, the same `+default_base_moof` (CMAF-style),
  `+skip_trailer` (no `mfra`), `frag_every_frame+empty_moov`, `-frag_duration 1000000`, the same with an AAC track 0.38 s
  longer, and `frag_keyframe` alone (the first fragment's samples in `moov`);
- a variable rate: `setpts` doubling every frame's duration after frame 50, `-fps_mode vfr`;
- rotated by `-display_rotation 90` and `180` (and `90` with `-display_hflip`) on a copy.

## What was found

### Fragmented MP4: the description only

ManagedCodecs already demuxes it: `Mp4Demuxer` reads every `moof` at open, and its packets (pts, dts, duration) are
ffprobe's `-show_packets` line for line on the files above. The core refuses before that: `Mp4Header` totals `stts`,
which is empty in an `empty_moov` file, so "The video track has no samples."

With the count and duration taken from the demuxer's packets in the throwaway (the walk `FormatDuration` already makes),
every run on every fragmented file came out the FFmpeg path's byte for byte: the `YuvVideo` fields, the scenes and stats
CSVs, the shots as JSON and HTML, and the packets against ffprobe. What FFmpeg says, and the description must say:

| Field | FFmpeg on a fragmented file | Today's core |
| --- | --- | --- |
| `nb_frames` (`FrameCount`) | N/A with an empty `moov`; the `moov`'s own samples (50 of 150) with `frag_keyframe` alone | refuses |
| `avg_frame_rate` | 25/1 (mov.c's `nb_frames_for_fps` / `duration_for_fps`, counted over `stts` and each `trun` it parses) | refuses |
| `start_pts` | 1024: no edit list in the fragments, so libx264's B-frame delay stays (2 frames in 1/12800) | ManagedCodecs' first pts, the same |
| format duration | 6.000000, 6.380000 with the longer audio: the packets' span, with or without `mfra` | the same walk |

So `FrameCount` is null for an empty `moov` (ShotDetector then takes the length from `Duration` times the rate, as
OpenCV; the runs agreed) and the partial count where `moov` has samples (FFmpeg's value, wrong as it is: the runs still
agreed). The constant-rate check moves from `stts` to the packets' durations. Left to measure in the row: a file with
`sidx` (ffmpeg's `+global_sidx` on a copy wrote a file FFmpeg itself can't decode, so that needs another recipe) and
one with `mehd`, whose `fragment_duration` FFmpeg could prefer for the stream's duration; and opening a long fragmented
file in the browser, where the demuxer reads every `moof` up front.

Nothing upstream.

### Rotation: ShotDetector first

The FFmpeg path turns the frames upright: `VideoReader.Turn` is the display matrix's angle alone
(`-round(av_display_rotation_get)`, flips ignored), applied after the conversion (`CvResize.ResizeYuvTurned`, as OpenCV's
`cv::rotate`), and `DetectedVideo` reports `Rotation` and the upright size. The frame-source path has nowhere to say it:
`YuvVideo` has no rotation, and `Detect(YuvVideo, ...)`'s remarks say "upright (no rotation is applied)", in 3.22.0 too.
Run anyway on the rotated copies, the scenes CSV happened to agree but every stats CSV differed (the detectors see the
picture on its side), DetectedVideo's processed size differed at 90°, and at 180° the hash detector's cuts differed.

What upstream needs: a `YuvVideo.Rotation` (FFmpeg's display rotation, as `DetectedVideo.Rotation`: quarter turns only,
anything else an `ArgumentException`), the frames turned after conversion exactly as the file path turns them, the
upright size in `DetectedVideo`. The frames stay as decoded.

What it needs here: `Mp4Header` reads the `tkhd` matrix's angle as `av_display_rotation_get` computes it (today a
`Transformed` flag), refuses what isn't a quarter turn, and the flip `-display_hflip` adds still reads as 90 for FFmpeg
(ffprobe's `rotation=90` on that file, the same shots): flips go with the angle as FFmpeg reads it. The picture's
measures (`PictureMeter`) are on the decoded frame, while `PictureCheck` runs `ffmpeg`, which autorotates before its
filters: luma and black are the same either way, crop and the colour bars are not (a choice below). The page shows the
`<video>` and its stills as the browser turns them, which is already upright.

### Variable frame rate: ShotDetector first

The FFmpeg path takes a variable rate: on the VFR file `Detect(path)` gave three shots at 0, 2 and 6 s to 9.88 s, from the
frames' own timestamps (scenedetect 0.7.1's model, `IDetector`'s positions), at FFmpeg's average rate 3750/247. The
frame-source path refuses at the third frame: "Frame 2 is 0.08 s after the first, not 0.131733 s: frames from the caller
must come at a constant frame rate (variable frame rate and dropped frames aren't supported yet)."
(`VideoReader.CheckConstantRate`), in 3.22.0 too. ManagedCodecs gives what's needed: every packet's pts as ffprobe's,
and each frame carries its packet's pts.

What upstream needs is more than dropping the check, because the constant-rate grid holds up three other things:

1. `Merge` and `ScoreRange` identify a range's frames by the grid from frame 0's pts: with a variable rate they need
   the pts themselves (a range's first pts, or the caller's list), or ranges refused on such a video.
2. `StartTime`: the file path seeks a variable-rate file to "the first frame at or after half a frame before the time",
   from every timestamp; the frame-source path uses round(seconds × fps). It needs the timestamps up front (a
   `YuvVideo` property the caller fills; the core has them in `Packets`) or `StartTime` refused there.
3. The check also catches frames given under the wrong `firstFrame` and dropped frames: a variable-rate source needs
   another way to say "dropped" (none, as for a file, is what the file path does).

What it needs here: `CheckConstantRate` goes (the description's rate is already FFmpeg's, sample count over total
duration: 3750/247 on the file above), and the split run either hands the ranges their pts or runs one worker.

## Order

1. **Fragmented MP4**, now: here only, measured to agree. One PR: `Mp4Header`/`Mp4Input` from the packets, two
   samples (an `empty_moov` file with B-frames and AAC; `frag_keyframe` alone, whose `moov` holds part of the samples),
   the reject list without it, README and CLAUDE.md's scope.
2. **Rotation**, after a ShotDetector release with `YuvVideo.Rotation`: the smaller change upstream, whose turning code
   already exists. One PR here: the matrix's angle, a sample per quarter turn (90, 180, 270), the picture's measures as
   chosen below.
3. **Variable frame rate**, after a ShotDetector release that takes it: the larger change upstream (ranges, start
   time). One PR here: the check gone, a VFR sample, the split run as upstream allows.

Each upstream wait leaves its refusal as it is.

## Upstream issues (drafts)

No issue on either was found in bisforboman/shotdetector on 2026-10-30; filed the same day as
[#608](https://github.com/bisforboman/shotdetector/issues/608) (rotation) and
[#609](https://github.com/bisforboman/shotdetector/issues/609) (variable frame rate).

### Frame source: rotation (`YuvVideo.Rotation`)

> **Title:** Frame-source path: take the display rotation and turn frames as the file path does
>
> `ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, ...)` (and `DetectAsync`, `ScoreRange(Async)`, `Merge`) analyse
> frames as given: "upright (no rotation is applied)". The file path turns a rotated file's frames (`VideoReader.Turn`:
> `-round(av_display_rotation_get)`, flips ignored, after the conversion as `cv::rotate`) and reports `Rotation` and the
> upright size. So a caller with a rotated MP4 (phones) can't get the file's result: on a 320x240 clip copied with
> `ffmpeg -display_rotation 90` (and 180), shotdetector-web's core with its refusal switched off got every stats CSV
> different from `Detect(path)`'s, the processed size different at 90°, and the hash detector's cuts different at 180°.
>
> Asked: a `YuvVideo.Rotation` (degrees counterclockwise, as `DetectedVideo.Rotation`; 0, 90, 180, 270, anything else an
> `ArgumentException`), the frames turned after conversion exactly as the file path turns them
> (`CvResize.ResizeYuvTurned`), `DetectedVideo.Rotation`, `Width` and `Height` as the file's. Frames stay as decoded.
> Done when the frame-source runs of 90/180/270 samples equal `Detect(path)`'s byte for byte, CSVs and shots, natively
> and on wasm. Needed by shotdetector-web's backlog row "Rotation" (decision `2026-10-30-scope-widening.md` there).

### Frame source: variable frame rate

> **Title:** Frame-source path: variable frame rate, as the file path takes it
>
> The frame-source path requires a constant rate (`VideoReader.CheckConstantRate`: "frames from the caller must come at a
> constant frame rate (variable frame rate and dropped frames aren't supported yet)"), while `Detect(path)` takes a
> variable-rate file at its own timestamps. On a clip whose frames last twice as long after frame 50, `Detect(path)` gives
> three shots (0, 2, 6 s to 9.88 s, average rate 3750/247); the frame-source path stops at frame 2. ManagedCodecs gives
> each frame's pts as ffprobe's.
>
> Asked: frames from the caller positioned by their pts without the grid check when the video says its rate varies (or
> always, keeping the check for a constant-rate video), with what the grid holds up today:
> - `ScoreRange`/`Merge`: identify a range's frames by their pts (or refuse ranges on a variable-rate video, said so);
> - `StartTime`: the file path's "first frame at or after half a frame before the time", from timestamps the caller
>   gives up front (e.g. `YuvVideo.FrameTimestamps`, presentation order), or refused without them;
> - dropped frames: what the file path does (nothing), said in the docs.
>
> Done when the frame-source run of a VFR MP4 equals `Detect(path)`'s byte for byte (CSVs, shots), natively and on wasm,
> whole and, if ranges are kept, split and merged. Needed by shotdetector-web's backlog row "Variable frame rate"
> (decision `2026-10-30-scope-widening.md` there).

## Choices

Put to the owner with AskUserQuestion on 2026-10-30; the answers below once given.

1. **Order:** fragmented, then rotation, then variable frame rate, as above.
2. **Upstream issues:** filed by the session from the drafts above (#608, #609).
3. **The picture's measures on a rotated video:** of the upright frame, as FFmpeg's filters see it after autorotate
   (crop and colour bars in the upright picture's coordinates; `PictureCheck` against `ffmpeg` as it runs today).
4. **A split run of a variable-rate video:** the VFR issue asks for ranges identified by their pts, so such a video
   splits across workers as any other; ranges refused upstream would be a new question.
   Shipped that way (ShotDetector 3.23.0's `YuvVideo.FrameTimestamps`, which ranges check each frame against): the
   backlog row, done 2026-10-31.
