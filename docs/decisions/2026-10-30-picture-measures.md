# Picture measures (2026-10-30)

Backlog "Picture measures: black frames, brightness, broadcast-safe levels, letterbox, colour bars": the picture side of
a check before delivery, next to the sound's measures ([2026-10-24-audio.md](2026-10-24-audio.md)). The row said the
pass adds time to every run and to measure that before deciding. This is that measurement, then the questions it
leaves.

## What it costs

### The prototype

One pass over each decoded frame as `Mp4Input.Frames` yields it, before ShotDetector converts it, all of it in
`Vector128` (wasm SIMD under AOT):

- **Luma**: per row, the sum, the minimum and maximum, the pixels at or under blackdetect's threshold, the pixels
  outside 16 to 235.
- **Chroma**: per chroma row, a vector compare for U or V outside 16 to 240; only where one is (rare) are the 2x2 luma
  pixels under it looked at, so that the count is signalstats' BRNG (a luma pixel counts once if it or its chroma is
  out of range).
- **Letterbox**: cropdetect's scan per frame (`reset=1`): rows from the top and the bottom, columns from the left and
  the right, until a line's mean luma is over its limit. Rows are summed as vectors, columns sixteen at a time over
  every row. The first scalar version, a column at a time, cost twice the rest together.

A 256-bin histogram (signalstats' YLOW and YHIGH, its 10th and 90th percentiles) can't be vectorised the same way: it
was timed too.

### Timings

Sintel's trailer (854x480, 1253 frames, `samples/sintel_trailer-480p.mp4`), the default run (content detector), in
this session's cloud VM: natively (.NET 10, Release) and the App's bundle compiled AOT under Node 22 (the deployed
page is the AOT publish). Detection alone and detection with the pass were run interleaved (A B, B A, ...), and the
pass alone was timed over the decoded frames, the clearer number where the A/B's differences are within its noise:

| | Native | wasm AOT |
|---|---|---|
| A default run, the whole file | 5.8 to 5.9 s (4.7 ms a frame) | 13.3 to 16.4 s (about 12 ms a frame) |
| The pass, every measure above | 0.12 ms a frame (2.5%) | 0.27 ms a frame (2.2%) |
| Luma only (no BRNG, no letterbox) | 0.07 ms a frame (1.5%) | 0.16 ms a frame (1.3%) |
| Luma as a scalar histogram (adds YLOW, YHIGH) | 0.36 ms a frame (7.7%) | 0.63 ms a frame (5.3%) |
| Interleaved A/B, every measure, paired medians | +2.6% (3 pairs), +8.4% (3 more) | -4% (7 pairs: noise) |

The pass is about 2% of a run; the VM's run-to-run noise (13.3 to 16.4 s for the same run) is several times that, so
the A/B can't see it, and its noise is no reason to keep the measures out of every run. Decoding, not the pass, is
the run's time: H.264 in ManagedCodecs is most of the 12 ms.

### Do they equal FFmpeg's?

The prototype's per-frame figures were compared with FFmpeg 6.1's (`signalstats=stat=brng`, `cropdetect=reset=1:skip=0`,
`metadata=print`) on six samples (fades, Sintel, full range, 854x480, 29.97 BT.709, the edit list's 75 frames: 1932
frames): YMIN, YMAX, the BRNG count and cropdetect's x1, x2, y1, y2 the same on every frame, YAVG to the six digits
FFmpeg prints. That follows from ManagedCodecs' frames being FFmpeg's frames (the CSVs prove it) and the filters being
integer counts over them (FFmpeg 8.1's `vf_blackdetect.c`, `vf_signalstats.c` and `vf_cropdetect.c`, read for this).

## Which measures

### Question

Which of the row's five go in?

### Choices

1. **Black** (`blackdetect`): a frame is black when 98% of its pixels are at or under 16 + 10% of 219 (37), or 10% of
   255 (25) in full range; spans of black frames, from the first black frame's pts to the first frame after (at the
   file's end, the last frame's pts, as FFmpeg does).
2. **Brightness** (`signalstats` YMIN, YAVG, YMAX per frame): a lane of the mean with the min-max band. YLOW and YHIGH
   too would make the pass a histogram: 5% of a run, not 2%.
3. **Broadcast range** (`signalstats` BRNG): the share of each frame's pixels outside 16 to 235 (luma) or 16 to 240
   (chroma); where it is over zero, and the largest. FFmpeg measures it in full range too, where it means little; the
   page would say so.
4. **Letterbox** (`cropdetect`, per frame): the black bars at each edge; the card says the active picture's size and
   aspect ratio where the bars hold for most of the file.
5. **Colour bars**: no FFmpeg filter finds them. They would be found by their definition (SMPTE's seven bars in the
   top two thirds, their Y, U and V levels), checked on `smptebars` and `smptehdbars` samples, as the reference tone is
   on ShotDetector's own definition. It needs the chroma planes in the pass and a definition to settle.

### Answer

Open: the owner's.

## Where they run

### Question

Every run, or only when asked?

### Choices

1. **In every run, in the same pass.** About 2% of a run (above). The measures come with the run, stop when it stops
   (the frames read), and split with it: each range's worker measures its own frames, and the page puts the ranges
   together as `Mp4Input.FromStart` does for the scores.
2. **On demand, in a worker of its own**, as the sound. That decodes the video a second time: as long as the run
   itself, on another core. The sound's worker is cheap because AAC is; H.264 isn't.
3. **In every run, behind an option** (on by default): option 1, with a way to leave them out.

### Answer

Open: the owner's.

## How they are checked

### Question

The CSVs are checked byte for byte, the sound's measures within tolerances (its decode is within 3e-7 of FFmpeg's).
These are integer counts on frames that are FFmpeg's. How strict?

### Choices

1. **Exact**: NativeCheck's `PictureCheck.cs` runs FFmpeg's `blackdetect` (its defaults and `d=0`),
   `signalstats=stat=brng` and `cropdetect=reset=1:skip=0` with `metadata=print` on every sample and compares every
   frame: YMIN, YMAX, BRNG's count (its share times the frame's pixels, rounded), cropdetect's x1, x2, y1, y2, the
   black spans' pts equal; YAVG to FFmpeg's printed six digits. The App's figures equal the native ones (app-check.mjs).
   The mark stays `measured` (they are the core's measures, checked against FFmpeg), its tooltip saying they are equal.
2. **Within tolerances**, as AudioCheck: a little room for nothing that is known to need it.

### Answer

Open: the owner's.

## Not here

- The pass doesn't change the CSVs or the shots: it reads the frames ShotDetector reads, and the native, wasm and page
  checks keep comparing those byte for byte.
- No new FFmpeg dependency in the page: the oracle is NativeCheck's, with the FFmpeg it already loads.
