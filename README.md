# shotdetector-web

Shot detection entirely in the browser: a video file is decoded, converted and analysed on WebAssembly, and nothing
leaves the machine. It gives the same shots as [scenedetect](https://www.scenedetect.com/) through
[ShotDetector](https://github.com/bisforboman/shotdetector), byte for byte, without FFmpeg:

- **[ManagedCodecs](https://github.com/bisforboman/ManagedCodecs)** demuxes the MP4 or the AVI and decodes the H.264 or
  the MJPEG (pure managed C#, bit-exact with FFmpeg's decoders);
- **ShotDetector.FastYuv**'s `SwscaleYuv420` converts the frames to BGR (a bit-exact port of FFmpeg's libswscale
  conversion);
- **ShotDetector** detects, through `ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, DetectionOptions)`, its API
  for frames another decoder decoded.

The owner's decisions behind it (a new repository, plain HTML + JSExport, FastYuv a separate DLL, the first version's
scope) are in shotdetector's
[docs/decisions/2026-10-24-managedcodecs-showcase.md](https://github.com/bisforboman/shotdetector/blob/main/docs/decisions/2026-10-24-managedcodecs-showcase.md).

## Status

The page is live at **https://bisforboman.github.io/shotdetector-web/**, compiled ahead of time and deployed by
`.github/workflows/pages.yml` on each push to `main` once the checks pass on it (Checks; it runs locally too: "Running
the page").
`src/ShotDetectorWeb.Core` opens an MP4 or an AVI and detects; the checks show that its scene and stats CSVs equal ShotDetector's
FFmpeg path's on every sample, all five detectors, natively and on browser-wasm (interpreted and AOT), the latter
through the page's own exports (`src/ShotDetectorWeb.App`, the .NET side of the page's worker).

Supported so far: **8-bit 4:2:0 or 4:2:2 and 10-bit 4:2:0 H.264 (High, High 4:2:2, High 10) in an MP4, fragmented or not, at a constant or variable frame rate (a screen
recording's: every frame at its own pts, as FFmpeg's path takes it, given up front where the rate varies), upright or turned a
quarter by its display matrix (a phone's: detected upright, as FFmpeg's path turns it; the picture's crop and colour bars
the upright picture's): progressive, or interlaced coded as MBAFF frames** (libx264's `-flags +ilme+ildct`), detected on the frames as decoded, woven, as
ShotDetector does by default, or deinterlaced by yadif when the page's Deinterlace option says so (`auto`: where the
field order says interlaced, as `--deinterlace auto`; on one worker, not resumed, until ShotDetector's ranges take it,
shotdetector#628); field pictures (PAFF) are refused, having no sample to check them on (decision
`docs/decisions/2026-10-30-interlaced.md`). A 10-bit video's samples go to ShotDetector as decoded (16-bit
little-endian words, yuv420p10le), its chroma location the SPS's (left without one, FFmpeg's H.264 default), which its
conversion interpolates from; its picture isn't measured yet, its frames' types are (backlog, "Picture measures at 10
bits"). **And 4:2:0 or 4:2:2 MJPEG
in an AVI** (yuvj420p, yuvj422p: a JPEG is full range; its audio described, not measured; decision
`docs/decisions/2026-10-30-mjpeg-avi.md`). Anything else is refused when the file is opened, with an
`UnsupportedVideoException` saying what (neither MP4 nor AVI, another codec, 4:4:4, greyscale, 10-bit 4:2:2 or more than
10 bits, a rotation that isn't a quarter turn, an AVI that drops frames or an OpenDML AVI over 1 GB; decision
`docs/decisions/2026-10-30-scope-widening.md`). A frame size that changes midway is refused by
the run, the core's frames saying so before ShotDetector would read the frame at the video's size (as ShotDetector itself
refuses one since 3.24.0, shotdetector#625; `Detect(path)` scales it): `samples/reject/size-change.mp4`. A colour
matrix or range that changes midway in band isn't seen: every frame is converted with the first SPS's (backlog,
"Each frame's own colour tags"). Interlaced (two-field) MJPEG isn't checked: ffmpeg
can't make a sample of it. Damaged files throw ManagedCodecs'
`InvalidDataException`.

The page's "Try the demo" runs Sintel's trailer, re-encoded at deploy time by the recipe CI's checks run on
(`tools/make-samples.sh`), so the file visitors try is the one the exactness check covers. How the page is built is
decided in
[docs/decisions/2026-10-24-the-page.md](docs/decisions/2026-10-24-the-page.md); the work, row by row, is in
[docs/backlog.md](docs/backlog.md).

## Running the page

```
dotnet publish src/ShotDetectorWeb.App -c Release -p:WasmEnableThreads=true -o out/threads-publish   # the threaded bundle
rm -rf src/ShotDetectorWeb.App/obj src/ShotDetectorWeb.App/bin                                     # the next build mustn't reuse it
dotnet publish src/ShotDetectorWeb.App -c Release            # -p:RunAOTCompilation=true (both) for the build Pages gets
mkdir -p src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot/threads
cp -r out/threads-publish/wwwroot/_framework src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot/threads/
python3 -m http.server -d src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot 8000
```

Then open http://localhost:8000 (`localhost`, a secure context: the service worker needs one). `tools/check.sh` publishes
the two bundles the same way. Without `threads/_framework/` the page runs everything on its worker (Threads, below). Any static server works if it serves `.wasm` as `application/wasm` (Python's does). The
page is `wwwroot/index.html`, `style.css`, `app.js` (the page's states), `timeline.js` and a module per panel in
`panels/`, native ES modules with no build step and no dependencies; `worker.js` owns the .NET runtime and calls the
exports the page names, `blob-read.js` reads the picked file for it and `blob-write.js` makes the stats CSV's `Blob`. Pick an MP4 (or drop one), choose the detector,
its threshold (prefilled with ShotDetector's default for it), the minimum shot length and, for adaptive, its window
and minimum content_val (scenedetect's `window_width` and `min_content_val`, prefilled 2 and 15; issue #50), and
detect. Under the player
is a timeline: one time axis, a playhead that follows the video, a click anywhere on it seeks, and one lane per thing
over time, today the cuts (a tick at each, as soon as it is known, and the part of the file read so far shaded), a long
video's ranges (a band per worker, filled as far as it has got: Long videos across workers, below), the
scores (each frame's score from the stats CSV, the column the detector cuts on, against the threshold the run used, a
dot where each cut sits on it, and the spans where `content_val` stays under 1 for a second or more marked as still
pictures, the page's reading), the picture (each frame's luma, its lowest to highest as a band and its mean as a line,
the black spans shaded, the colour bars marked, a tick where pixels leave broadcast range: `Api.Picture`, every run's;
"The picture", below), as soon as the file is opened the bitrate per second and a tick at each keyframe, from
the MP4's packets (`Api.Packets`; Using the library, below), after a run each frame's type, I, P or B, as the decoder
gives it (`Api.FrameTypes`, every run's: FFmpeg's `pict_type`, kept with the picture's measures), and, alongside the file's first run, the sound: its
waveform, decoded in a worker of its own by ManagedCodecs' AAC decoder (`Api.Audio`;
"The sound", below; no lane for a file without audio, one line saying why for audio that isn't AAC). The page
is laid out as decision `2026-10-26-new-ui.md` chose: once a file is picked, the landing folds into a toolbar (the
file, the options and one button: Detect, Stop while a run goes, Cancel while it opens) with a one-line status under
it; then the video, with the shot playing as its caption, beside the run's card (the shots, cuts, length and options
large, the downloads, the warnings when ShotDetector has any); the timeline across the whole width; the shots as a table
whose rows play the video from the shot, the row of the shot playing marked, each row as soon as ShotDetector knows the
shot (a few frames after the cut that ends it, `DetectionOptions.OnShot`) and starting with a still of the shot's middle
frame (the browser's own decode, not ShotDetector's frame: a hidden second `<video>` on the same file, sought one still
at a time as rows come into the table's view; on Sintel's trailer every still is there as the run ends). An AVI, which
no browser plays, has its picture from its own JPEGs: each MJPEG packet is one, cut from the picked file where
`Packets()` says its data is and decoded by the browser, an `<img>` of the frame at the playhead in the video's place
(no sound; the playhead the page's own, moved by the timeline and the shots' rows) and the stills from the same, during
a run too; and last a grid of cards: the shots'
statistics (the number of shots, average and median shot length, shortest and longest, cuts per minute, the run's speed
against real time: the page's own figures, worked out from the shots), the picture's measures (the luma's lowest,
highest and mean, broadcast range, the black spans of 2 s or more, the letterbox as cropdetect finds it, the colour
bars), the sound's measures, the bitrate's numbers and
how many cuts fall on a keyframe, and, as soon as the file is opened, "About this file": the core's description of it
(`Api.Open`: the `YuvVideo` below and the audio track) in a viewer's words, "BT.709" for `bt709`, each field's own value
in its tooltip and Open's JSON one click away. Each card's title has one chip saying whose numbers they are
("scenedetect's, byte for byte", "measured", "the core's", "the page's"; the footer says what the words mean, and
names each library with the version its assembly in the bundle carries, `Api.Versions` filled in by `versions.js`,
which app-check.mjs checks against Directory.Packages.props' pins). At phone width it is one column in the same
order. The scenes and stats CSVs
(`<file>.scenes.csv`, `<file>.stats.csv`, the file's name without its extension) are the ones scenedetect writes; the
shots as JSON and HTML beside them (`<file>.shots.json`, ShotDetector's `Shots.Json`; `<file>.shots.html`, scenedetect's
save-html page, `Shots.Html`, without images) are ShotDetector's, and checked as the CSVs are. For editors, the
card also offers the shots in scenedetect's timeline formats (`Api.TimelineFile`, ShotDetector's `Timeline` with its
defaults through the core's `TimelineFiles`): `<file>.edl` (save-edl, CMX 3600), `<file>.fcpxml` (save-fcp, FCPXML
1.9), `<file>.fcp7.xml` (save-fcp `--format fcp7`), `<file>.otio` (save-otio), `<file>.qp` (save-qp) and
`<file>.keyframes.txt` (save-keyframes, Aegisub's keyframes), and Subtitle Edit's shot changes,
`<file>.shotchanges` (`Timeline.ShotChanges`, shotdetect's `--save-shotchanges`: each cut's time in seconds a line,
which SE's Import shot changes reads), checked as the CSVs are. The browser never learns the picked file's folder, so they are given its name at the root (`/<name>`): the
XMLs' and the OTIO's media path is `file:///<name>` or `/<name>`, which the editor relinks to the file where it is
(scenedetect writes the absolute path it was given; the same string on the FFmpeg path gives the same bytes). And
YouTube chapters (`Api.YouTubeChapters`: ShotDetector's `Timeline.YouTubeChapters` and `ChapterStarts` through
`TimelineFiles.Chapters`, shotdetect's `--save-youtube-chapters`, not scenedetect's): the cuts as a description's chapter
list, `<file>.chapters.txt`, a line a chapter (`1:23 Chapter 2`, `h:mm:ss` from an hour on), shots merged until a
chapter has run the minimum typed beside the download (30 s to begin with; YouTube's own is 10), each cut's time rounded
up to the second, a last chapter shorter than that joined to the one before; YouTube wants the first at 0:00 and at
least three, so with fewer the card says why instead (Timeline's words). The titles are typed on the page, under
Titles, a field a chapter with Timeline's placeholder in it: the download carries them, a chapter left empty keeps
"Chapter n" (the titles last while the run is shown, not kept with a stopped run: backlog, "Chapter titles kept with
the kept run"). A file
of any size runs: the worker gets the `File` itself, and .NET reads it in slices of a megabyte as it needs them
(`BlobStream` over `FileReaderSync`), so it is never copied into memory whole (Large files, below). Before a run the
page says how long it will take, an estimate from the frame count and picture size (this browser's speed from its last
run, else `REFERENCE_FPS` at the top of `app.js`); Stop ends the run where it has got to and shows the result for the
part read, its shots and both CSVs, as a finished run's, labelled "stopped at", and a run with other options then
starts at once on the opened file (Stopping a run, below). A long video is split across several workers, the first
range's shots shown as they are found and the rest once the ranges merge (Long videos across workers, below). Errors are the library's messages, a damaged file's prefixed "The file is damaged or not a video this
page can read:". "Try the demo" fetches `demo/sintel-trailer.mp4` next to the page and runs it as a picked file:
`pages.yml` puts the Sintel sample there; locally, copy `samples/sintel_trailer-480p.mp4` to
`wwwroot/demo/sintel-trailer.mp4` in the publish folder.

## Stopping a run

`Api.Detect` scores the video as one range from frame 0 with ShotDetector's `ScoreRangeAsync` (which tells each shot
as `DetectAsync` would) and merges it (`Merge`: `Detect`'s result byte for byte), so that a stopped run leaves scores
to resume from (Resuming a stopped run, below). Its frames come from `VideoInput.RangeFramesAsync`, which every 8 frames
(`PauseEvery` in `Api.cs`) awaits the worker's next task (`[JSImport] pause`, `wwwroot/pause.js`: a `MessageChannel` message). Between
two pauses the worker handles the page's `stop`, which makes the frames end there as if the file did, so `Detect`
answers with the result for the frames read: the frame count read, its shots (the last one ending where the run
stopped) and both CSVs, and `stopped: true`. A file picked during a run abandons it with `cancel` (the token: no
result), and only a worker that doesn't answer a stop within a second and a few pauses is terminated, losing the run
but for the shots the page was told. ShotDetector 3.16.1 neither warns nor refuses when the frames end before
`YuvVideo.FrameCount`.

A stopped run is the run of a file that ends there. Its stats CSV is the whole run's up to there (a frame's stats
depend only on the frames before it), but for the adaptive ratio of the last 2 frames, `None`, which waits for 2 frames
after them; its shots are the whole run's up to the stop, the next one cut short there, but for a cut decided on frames
past the stop (FlashFilter's merge decides one up to the minimum shot length late). The threshold detector would end a
fade-out the stop falls in with a cut, as at a file's end. `tools/app-check.mjs` checks this on every sample and run.

Measured 2026-10-26 on a 4-core cloud VM: a pause takes 6 µs in Chromium's worker (2,000 in a row through
`pause.js`), so 8 frames of the Sintel trailer at 60 frames per second (133 ms) pay 0.005% for it; under Node, where a
pause takes about 0.3 ms, the pauses were 0.25% of app-check's runs AOT and 0.05% interpreted (it fails above 1%). On
the Sintel trailer, AOT, stopped at frame 600: Detect answered the stop in 201 ms in headless Chromium and 136 ms in
Firefox 136, and the next run's first progress came 98 and 129 ms after its click, without opening the file again.

### Pausing a run

Pause, beside Stop while a run goes, holds the run where it is and Resume goes on from there (backlog-done, "Pause and
resume a run"). The worker's `pause` makes `pause.js` keep the promise `Detect` awaits every 8 frames unresolved
(`hold`), and `resume` resolves it (`release`): .NET simply waits, nothing cancelled or ended, so the result is the one
the run would have had. Every worker of a split run is paused; the sound's worker goes on (its one pass has no
pauses). The bar and the status hold still, and the run's time, its frames per second and the rate the browser keeps
for its estimate leave the pause out. On one worker the status names the frame the run is held at, which the worker
tells once it gets there (`held`: the frames read at that pause, up to 8 past the last progress), so a stop ends
there; split, it says "about" the ranges' progress added up. Stop works while paused: the worker's `stop` tells .NET first, then releases the
run, which ends at the pause it waits at. `tools/app-check.mjs` holds every sample's default run half-way for 150 ms,
fails on any progress meanwhile, and compares the resumed run's CSVs and shots with the run never paused; on the first
sample a run stopped while paused ends at the frame it was paused at. Seen 2026-10-29 in headless Chromium on Sintel's
trailer: paused at frame 320 of 1,253, the bar and status unchanged 4 s later, Resume going on from there to its 8
shots in 107.5 s, the pause left out; paused at 192 and stopped, the result for 192 frames.

### Resuming a stopped run

A stopped run can be resumed, in the same visit or a later one (backlog-done, "Resume after Stop, across visits";
decision [2026-10-30-resume-across-visits.md](docs/decisions/2026-10-30-resume-across-visits.md)). Every run keeps its
ranges' scores and its picture measures; once stopped, the page asks for them (`Api.Kept`: a `Blob`, little-endian,
the frame the rest starts at first) and keeps them in IndexedDB with the file's name, size and modified time and the
run's options, one record in all (the owner's choice: only the last stopped run, of any file). Picking that file again,
in this visit or a later one, puts the run's options in the form and shows the stopped part at once (`Api.Merge` of
the kept bytes alone: its shots, stats, CSVs and picture measures, as the stopped run had them), with Resume and
Forget beside Detect, which starts over. Resume goes on from the last place a range can start (one past a keyframe) at
or before the last frame scored (`Mp4Input.Continue`), so at most a keyframe interval is scored again, frames both
have, which ShotDetector compares: on one worker `Api.Resume` scores the rest with the earlier ranges given
(ShotDetector 3.22.0's `ScoreRangeAsync(video, earlier, ...)`), telling the rest's shots as they are known after the
stopped part's, its last one, cut short at the stop, told again to where it ends; a rest longer than 2 minutes is split
across workers as a run of its length would be (the owner's choice: `Api.Ranges` from the rest's first frame, each
range scored as a split run's are, `Api.Merge` with the kept bytes first). The result is the whole run's, byte for
byte. A finished run of the kept file and options, another stop, or Forget replaces or drops the record; kept scores
that don't resume (another version's, other options) are forgotten and the run detected from the start, saying why.
Nothing leaves the browser, and where storage is off (a private window) a run resumes only in its visit. Kept bytes
are about 97 bytes a frame for the adaptive detector (Long videos across workers, below) plus the picture's, so a
two-hour film stopped half-way keeps about 9 MB.

## Long videos across workers

A video longer than 2 minutes is split across several Web Workers, each with a .NET runtime of its own, and their
results merged into exactly the one-worker result (decision
[2026-10-26-workers.md](docs/decisions/2026-10-26-workers.md); ShotDetector 3.16's `ScoreRangeAsync` and `Merge`,
issue [#19](https://github.com/bisforboman/shotdetector-web/issues/19)). The page splits across as many workers as
`navigator.hardwareConcurrency` says the machine has cores, but one, and at most 4 (`SPLIT_SECONDS` and `MOST_WORKERS`
at the top of `app.js`; `?workers=N` in the page's address forces N, 1 never splitting). `Api.Ranges` plans the ranges
from the packet list: about equal in frames, each after the first starting one frame past the keyframe nearest an
equal split's start (`VideoInput.Ranges`). The page's own worker scores the first range; each other range gets a worker
of its own, which opens the same `File` and scores its range (`Api.ScoreRange`): it decodes from the range's keyframe,
the packets before it walked with their data skipped as opening does, and gives ShotDetector the keyframe's frame
(the frame before the range's first), the range's own frames and the next range's first (the overlap frame, which the
merge compares). Each worker's scores come back as bytes (`Api.RangeBytes`, `RangeScores.ToBytes`, about 97 bytes a
frame for the adaptive detector), and the page's own worker merges them (`Api.Merge`), after which the CSVs come as
from a one-worker run. Meanwhile the timeline's Ranges lane shows each worker's range and how far it has got; the
first range's shots come as its worker (the page's own) finds them, as a one-worker run's, ShotDetector telling a
range from frame 0's shots since 3.22.0; the rest come once the ranges merge (ShotDetector decides them there), and the
merge tells only the shots the first range didn't, the first range's last again from its number on (it ended at the
range's end). On the two-hour control the first shot came at the merge, 5 to 7 s before the end of a 221 to 406 s
run, before this (backlog "Performance pass on the page"). A resumed run's rest has no range from frame 0: its shots
come at the merge.

Stop stops every worker at its next pause; each range ends where its worker got to, and the ranges from the start
merge for as long as each reaches into the next one's first frame, so the result is the one for the video up to where
the first unfinished range stopped, as a one-worker run's stop is (the decision's "As built"). A merge that fails
(ShotDetector's `ShotDetectionException`: an overlap frame that differs, as an open GOP or a decoder that doesn't start
clean at a keyframe would give; another video or options; damaged bytes) or a worker that fails makes the page detect
again with one worker, its reason in the Ranges lane. The file's sound is measured in a worker of its own alongside its
first run, split or not.

Measured 2026-10-27 on the AOT bundle in headless Chromium, 4-core cloud VM, on the two-hour control of Large files
(below, made again by its recipe: 640x360 at 24 fps, 250 kbit/s, 172,800 frames, 1,794 keyframes, 345,642,453 bytes),
adaptive, the runs one after another on an otherwise idle machine; the renderer's RSS (the page and all its workers)
sampled every half second:

| Workers | Run | Frames per second | Against one worker | Renderer's peak RSS | The sound came after |
| --- | --- | --- | --- | --- | --- |
| 1 | 1,322.7 s | 131 | 1 | 878 MB | 52.7 s |
| 2 | 671.7 s | 257 | 1.97 times as fast | 1,065 MB | 52.4 s |
| 3 (this machine's own choice) | 451.3 s | 383 | 2.93 | 1,303 MB | 59.7 s |
| 4 (forced, `?workers=4`) | 367.8 s | 470 | 3.60 | 1,602 MB | 78.3 s |

Every run gave the same 1,105 shots and the same scenes and stats CSVs, byte for byte. Each worker's wasm heap (under
Node, each range's runtime on its own, the same bundle): 174 MB once it has opened the file, most of it ManagedCodecs'
sample index (What the heap holds, below), and 208 MB at the end of its range, as a one-worker run's; the merge, in the
page's own worker, took 1.5 s for the four ranges' 16.8 MB and left its heap as it was. So each worker costs about
190 to 300 MB of renderer on a two-hour film, most of it the index every runtime builds again (measured with the
submodule's 2026-10-24 commit). With ManagedCodecs 0.1.0's sample table (2026-10-29, What the heap holds) a worker
scoring the last of four ranges has 84 MB once it has opened the file and 121 MB at the end of its range, as a
one-worker run's again; the renderer was not measured again. On Sintel's 52 s
trailer splitting paid too: 19.0 s on one worker, 11.1 s on two, 10.0 s on three, 6.7 s on four (the median of two
each; the three ranges at keyframes are less equal than the four), each worker about 100 MB more of renderer; below 2
minutes the page keeps one worker anyway, for its live shots (the decision's "As measured").

## Threads, and the page offline

A run on one worker (a video up to 2 minutes, or `?workers=1`) decodes on several threads where the browser allows it
(decision [2026-10-30-threads.md](docs/decisions/2026-10-30-threads.md)): ManagedCodecs' frame threading
(`H264Decoder.Threads`, through `VideoInput.DecoderThreads`), which gives the same frames, so the same CSVs (the native
check's `ThreadsCheck.cs` and the page check prove it). Measured on the page itself, AOT, headless Chromium on a 4-core
cloud VM, ManagedCodecs 0.2.3, the median of interleaved rounds (2026-10-30): Sintel's trailer took 13.7 s on the page's
worker, 9.6 s on 2 threads and 8.5 s on 4 (1.6 times as fast; five rounds), and the same at 1280x720 19.1 s and 10.3 s
on 4 (1.85 times; four rounds). The decoder is only part of a run (FastYuv's conversion, the detectors and the picture's
measures stay on one thread), so the larger the picture, the more threads give. With ManagedCodecs 0.1.0's slower
decoder the same trailer went 2.6 times as fast (the decision's first measurement).

.NET's threaded runtime (`-p:WasmEnableThreads=true`) starts only on a page's own thread, not in a Web Worker, needs
`SharedArrayBuffer` (so a cross-origin isolated page: COOP and COEP headers), and doesn't run under Node. So the page
carries two bundles: `_framework/`, today's, in its workers (the page's own, a split run's, the sound's), and
`threads/_framework/`, the threaded one, which `threads.js` starts on the page's thread for a one-worker run. There .NET
runs the exports on a thread of its own and the run on its thread pool (`Host.cs`), and the file is read in slices of a
megabyte with `Blob.arrayBuffer()` (the page's thread has no `FileReaderSync`), each one waited for on the pool. The
page stays responsive, the shots come as they are found, and pause, resume and stop work as on the page's worker.
The threaded runtime starts when a file is picked or the demo is fetched, not at Detect: its 31 files (5.6 MB
gzipped) and its start had made a first run's first progress 1.1 to 1.6 s later than on the page's worker (backlog
"Performance pass on the page"); a file that then runs split has fetched them for nothing.
A split run doesn't use threads: its workers already keep the cores busy. The decoder gets as many threads as
`navigator.hardwareConcurrency` says the machine has cores, at most 4 (6 did no better on 4 cores; `MOST_THREADS` at the
top of `app.js`; `?threads=N` in the page's address forces N, 0 or 1 meaning the page's worker).

GitHub Pages can't set headers, so `sw.js`, the page's service worker, adds them to every response it gives (the
`coi-serviceworker` trick). A first visit isn't controlled by it yet: it reloads once when the service worker takes
over, unless a file has been picked by then (threads come with the next visit). The page's worker, whose runtime
fetches the worker's bundle, starts only once the page won't reload (or when a file is picked), so that a first visit
fetches the bundle once. Where there is no service worker (some
private modes) or the page isn't isolated, every run goes on the page's worker as before. A threaded run that doesn't
answer a stop can't be terminated as a worker can: it is abandoned, and runs go on the page's worker until a reload.

The same service worker keeps the page for offline use, network first: online, every request goes to the network as
it always did and its answer replaces the cached copy; offline, the cached copy answers, the page, both bundles and the
demo clip once it has been fetched. A new deploy reaches a visitor as it did before; a visit cut off half-way through a
new deploy's files can leave a mix that fails offline until the next online visit.

## Large files

The page reads the picked file in place: the worker gets the `File` (no bytes copied), `Api.Open` takes it as a
`JSObject`, and each pass over the file opens a `BlobStream` that reads slices of a megabyte through `FileReaderSync`
(`wwwroot/blob-read.js`). Opening reads the file's boxes and decodes the first frame; the pass for the format's
duration skips the packets' data ([decision](docs/decisions/2026-10-24-the-page.md), "The file in memory"). Measured
2026-10-25 on the AOT bundle in headless Chromium (Playwright, 4-core cloud VM), on a two-hour MP4 made by looping
Sintel's trailer: 640x360 at 24 fps, libx264 at 2.5 Mbit/s with AAC audio, 172,800 frames.

| File | Opening | Run (adaptive) | Renderer's peak RSS | Wasm heap after the run |
| --- | --- | --- | --- | --- |
| 2,370,979,581 bytes (2.2 GiB) | 3.4 s | 46 min, 63 fps, 968 shots | 805 MB | 735 MB |
| 288,001,693 bytes, the same at 250 kbit/s (control) | 2.4 s | 34 min, 85 fps, 1,105 shots | 1,043 MB (at the end) | 734 MB |

The renderer is the process the page and its worker run in, sampled every half second; the wasm heap never shrinks, so
its size after the run is its peak. The control has the same frames in an eighth of the bytes and the same heap: the
heap grew with the frame count (about 4 KB a frame; Sintel's 1,253 frames: 67 MB), not with the file, and most of it
was the result's JSON (below, "What the heap holds"). During the control's run the renderer stayed at 450 to 500 MB, and its peak came as the result was built
and shown; with the big file it rose to about 800 MB in the first half hour and no further (the slices `FileReaderSync`
returns, which V8 frees late while .NET holds the thread). Before the duration pass skipped the packets' data, opening
the same file took 43 s and peaked the renderer at 1.7 GB. The estimate before the big file's run said 35 min (the
reference rate scaled by picture size); the next run in the same browser uses its own measured rate.

Measured again on 2026-10-31 on GitHub's 4-core runner (backlog-done "Performance pass on the page"), the control
made again: a run on one worker grows the renderer steadily, its largest process from 425 to 715 MB on the page's
worker and from 683 to 869 MB on 4 threads, while a split run stays at about 600 MB after opening. With a full garbage
collection every 10 s the same runs grew 412 to 472 MB and 662 to 761 MB: nothing leaks. What stays is the run's own
results in the .NET heap, which grow with the frame count (the threaded runtime's memory 166 to 239 MB over the run;
below, "What the heap holds"); the rest is garbage, mostly the slices the file is read in, that the browser collects
late and frees under pressure.

The reference rate (`REFERENCE_FPS`, a first visit's estimate) was measured again on 2026-10-30, after ShotDetector
3.20.0, ManagedCodecs 0.1.0, FrameReader 2.15.0, pause and resume and the stills per shot: the AOT bundle in headless
Chromium on the 4-core cloud VM, each run a first visit (a fresh context), the trailer and the two-hour control above
(made again by its recipe: 172,800 frames, 345,645,821 bytes) alternating. The trailer took 20.4, 18.6, 19.7 and
19.4 s (61 to 67 frames per second, the median of the first three 64, now `REFERENCE_FPS`; it was 46); the control,
split as the page chose (3 workers), 445.4, 467.2 and 442.2 s (370 to 391 frames per second), and on one worker
1,258.7 s (137). Per pixel the film goes about 16% faster than the trailer, whose run is short enough for start-up to
weigh. With 64, a first visit said "about 20 s" (19.6 s) for a trailer run of 20.5 s and "about 9 min" (517 s) for a
film run of 492.1 s with 3 workers, 4% short and 5% long; against the medians, 1% short and 16% long. The VM is shared
and noisy (the same runs differ by up to 10% here, and by tens of percent between days); GitHub's 4-core runner ran
the same trailer at 151 frames per second the same day (the page check's demo in `pages.yml`, its pauses left out),
so on a machine that fast a first estimate is about 2.4 times too long until that browser has its own rate. The owner
chose this VM's rate (2026-10-30).

Measured again the same day on ManagedCodecs 0.2.3 (its decoder faster on wasm): five interleaved rounds of the trailer
on the same VM, each a first visit, on the page's worker (`?threads=0`), on 2 threads and on 4 (the page's own choice
there). The medians: 16.7 s on the worker (72 to 77 frames per second, now `REFERENCE_FPS` 75), 12.0 s on 2 threads
(1.39 times as fast) and 11.1 s on 4 (1.50), now `THREADS_SPEEDUP` (3 threads, not measured, 1.45). A first visit then
said "about 17 s" for a 17.4 s run on the worker, 12 s for 13.1 s on 2 threads and 11 s for 11.2 s on 4. GitHub's
runner ran the trailer at 204 frames per second on 4 threads the same day (the page check's offline demo in
`pages.yml`), about 1.8 times this VM; the owner chose this VM's rate again. The two-hour control wasn't run again.

ShotDetector 3.16.0's FastYuv converts 8-bit 4:2:0 with SIMD, WebAssembly's included (the bundle has it on:
`WasmEnableSIMD` defaults to true), to the same bytes. Measured 2026-10-26 on the AOT bundle, 3.15.0 against 3.16.1
on one 4-core cloud VM, the runs alternating: the Sintel trailer's adaptive run takes 20.8 s instead of 21.9 s in
headless Chromium (60.2 frames per second instead of 57.3, the median of three), 23.1 s instead of 24.0 s under Node
(the median of five). The control above runs in 1,636 s instead of 1,781 s under Node (106 frames per second instead
of 97; the two runs side by side): 4 to 8% less time, so the conversion is a small part of a run.

### What the heap holds

Measured 2026-10-26 under Node on the AOT bundle (`node` driving the App's exports as `tools/app-check.mjs` does), on the
control file above made again by its recipe (172,800 frames and 335,939 audio packets as before; 345,713,834 bytes, its
audio at 128 kbit/s), adaptive with stats, ShotDetector 3.15.0 and 3.16.1 side by side on one 4-core cloud VM: the wasm
heap after each step, and what grew it (the same steps run natively, `GC.GetTotalMemory` around them).

| Step | Wasm heap, 3.15.0 | Wasm heap, 3.16.1 | What grows it |
| --- | --- | --- | --- |
| The runtime started | 34 MB | 34 MB | |
| The file opened | 174 MB | 174 MB | ManagedCodecs' `Mp4Demuxer` indexes every sample, video and audio: 140 MB allocated (about 275 B a sample), 28.5 MB kept (a `Sample[]`, 56 B a sample) |
| The run (`Detect`, its JSON) | 208 MB, flat to its end | 208 MB, flat to its end | the run's own demuxer and decoder; ShotDetector's stats grow inside it, natively 370 B a frame with 3.15.0 (64.0 MB at the end), 65 B with 3.16.1, which keeps them in columns (11.3 MB) |
| The scenes CSV (`ScenesCsv()`) | 208 MB | 208 MB | 113,808 characters |
| The stats CSV (`StatsCsv()`) | 432 MB | 208 MB | a 20.0-million-character string; building it allocates 325 MB with 3.15.0 (a dictionary per frame, the sort, the `StringBuilder`) and 175 MB with 3.16.1 (the `StringBuilder` and the string), which fits in what the heap already has |

The CSVs reach JavaScript as strings of their own (the stats CSV a `Blob` since 2026-10-30, below), without growing the
heap. Until 2026-10-25 they were inside
`Detect`'s JSON, and the heap reached 769 MB: System.Text.Json escapes a string (the CSV's line breaks) through a char
buffer of six times its length rounded up to a power of two, 134 million chars (268 MB) for the stats CSV, and writes
into a 67 MB UTF-8 buffer; `ArrayPool` keeps both, then the 40.6 MB JSON string. Opening took the heap to 208 MB before
it built one demuxer for the first frame and the duration pass instead of two. The run's step measured 250 MB on
2026-10-25 and 208 MB now with either version.

So the heap grew with the frame count because of what the end of a run builds, not what it keeps a frame: the JSON about
2 KB a frame before 2026-10-25, the stats CSV about 1 KB with 3.15.0 (with 3.16.1 still 175 MB allocated, about 1 KB a
frame, but in room the heap already has on this film: the demuxer's index allocates about 1 KB a frame with this file's
audio, and keeps a fifth of it). In headless Chromium as in the table above, the control's run ended with a wasm heap of
432 MB on 2026-10-25 (734 MB before) and the renderer's peak at 788 MB (1,043 MB), the same 1,105 shots; with 3.16.1 the
heap ends where the run leaves it, 208 MB under Node. The CSVs are unchanged (`tools/check.sh`). What was left shipped
upstream on 2026-10-27 and 2026-10-28: ShotDetector 3.20.0's `Stats.WriteCsv`, used here since 2026-10-30 (below), and
ManagedCodecs 0.1.0's sample table, below.

ManagedCodecs 0.1.0, the package since 2026-10-28, keeps each track's samples in columns, 29 bytes a sample, built once
at their final size ([ManagedCodecs#123](https://github.com/bisforboman/ManagedCodecs/pull/123), from
[#138](https://github.com/bisforboman/ManagedCodecs/issues/138)). Measured 2026-10-29 the same way, under Node on the
AOT bundle, adaptive with stats, on the control made again by its recipe (172,800 frames, 337,362 audio packets, 1,804
keyframes, 346,573,946 bytes, 1,197 shots: x264's output differs a little from one build to the next). The two bundles
ran side by side on one 4-core cloud VM: `main` just before the package (the submodule's 2026-10-24 commit, ShotDetector
3.16.1) and `main` with it (ShotDetector 3.20.0). Opening was measured three times each and the run twice, the heap the
same to the megabyte every time. The VM is shared and the bundles ran at once, so their run times say nothing about
speed. Natively, `GC.GetTotalAllocatedBytes` and `GC.GetTotalMemory` around `new Mp4Demuxer` showed the same numbers
on each of three runs.

| Step | Wasm heap, submodule | Wasm heap, 0.1.0 | What changed |
| --- | --- | --- | --- |
| The runtime started | 34 MB | 34 MB | |
| The file opened | 174 MB | 84 MB | `Mp4Demuxer`'s index of the 510,162 samples, video and audio: 138.9 MB allocated and 28.6 MB kept before (273 and 56 B a sample), 39.4 MB and 14.8 MB now (77 and 29 B) |
| The packets (`Packets()`, as the page calls it once opened) | 174 MB | 100 MB | the 2,263,545-character JSON: room the heap had before, 16 MB more now |
| The run (`Detect`) | 208 MB | 121 MB | the run's own demuxer (both tracks indexed again) and decoder, ShotDetector's stats |
| The scenes CSV (`ScenesCsv()`) | 208 MB | 121 MB | 115,095 characters |
| The stats CSV (`StatsCsv()`) | 208 MB | 151 MB | the 20.0-million-character string and its `StringBuilder`: room the heap had before, 30 MB more now (written a row at a time since 2026-10-30, below) |

So a two-hour film's peak went from 208 to 151 MB, and a split run's worker from 208 to 121 MB. #138 asked for three
things: each track's samples filled once and a smaller sample, both shipped, and, optionally, only the tracks asked
for, which did not ship: the demuxer indexes video and audio whatever its caller reads (the opening pass needs both
tracks for the format's duration, but the run's demuxer needs only the video). Measuring turned up a fourth:
`ReadPackets` allocates a new array for every packet's data, even when the caller wants only the timing. Opening's
duration pass skips the reads, so 342 MB of zeroed arrays come out of `Open`'s 405 MB allocated natively, and a run
allocates (and throws away) the audio track's 115 MB. Both were tried in a local build of 0.1.0, patched: the run's
demuxer indexing the video track only, and the duration pass's packets in one reused buffer. Natively `Open` then
allocates 51 MB instead of 405, but the wasm heap stays where it is: 84 MB opened, 121 MB after the run (146 MB after
the stats CSV instead of 151). The arrays die young and are collected before the heap has to grow. For the heap #138
is answered; the rest is allocation, an issue for upstream at low priority (backlog, Waiting).

Since 2026-10-30 `StatsCsv()` writes the CSV a row at a time through ShotDetector 3.20.0's `Stats.WriteCsv`, into a
`Blob` (`BlobWriter`: each part of 256 K characters copied out to `wwwroot/blob-write.js` as it is written), so the
whole CSV is never one string in the heap; the page downloads the `Blob` as it is and the scores lane reads its text.
Measured 2026-10-30 the same way, under Node on the AOT bundle, adaptive with stats, on the control made again by its
recipe (172,800 frames, 335,938 audio packets, 345,716,086 bytes, 1,182 shots): `main` just before (the string) and
this change, side by side on one 4-core cloud VM, each once.

| Step | Wasm heap, the string | Wasm heap, the `Blob` |
| --- | --- | --- |
| The runtime started | 32 MB | 32 MB |
| The file opened | 80 MB | 80 MB |
| The packets (`Packets()`) | 96 MB | 96 MB |
| The run (`Detect`) | 115 MB | 115 MB |
| The scenes CSV (`ScenesCsv()`) | 115 MB | 115 MB |
| The stats CSV (`StatsCsv()`) | 143 MB | 115 MB |

The CSV is the same 19,958,285 characters (the same SHA-256), and the heap now ends where the run leaves it: a
two-hour film's peak is the run's, 115 MB on this file.

An open file also holds its video packet list (`VideoInput.Packets`, below; in the numbers above): 13 bytes a frame,
2.25 MB on a two-hour film.

ManagedCodecs 0.4.0 ([ManagedCodecs#233](https://github.com/bisforboman/ManagedCodecs/pull/233), from
[#211](https://github.com/bisforboman/ManagedCodecs/issues/211)) has the rest: an `Mp4Demuxer` of the tracks asked for
(`MediaTypes`) and `ReadPacketInfo`, the packets without their data, from the sample tables alone. Since 2026-10-31
opening takes the format's duration and the packet list from `ReadPacketInfo` (no packet read or allocated), a run's
demuxer (`Mp4Input.DecodedFrames`, a range's too) indexes and reads the video track only, and the audio pass
(`Mp4Audio`) the audio only, which ended its stream that skipped the video's reads. Measured on the control made again
by its recipe (172,800 frames, 335,939 audio packets, 345,713,834 bytes, 1,184 shots), the code before and after on the
same package (0.4.0), on one 4-core cloud VM. Natively, a stream counting the bytes read, `GC.GetTotalAllocatedBytes`
around each step, opening and the audio pass in three interleaved rounds (the bytes and allocations the same every
round), a run's frames once each:

| Step, natively | Read, before | Read, after | Allocated, before | Allocated, after |
| --- | --- | --- | --- | --- |
| The file opened | 9.8 MB | 9.8 MB | 404 MB (0.9 to 1.4 s) | 49.6 MB (0.36 to 0.38 s) |
| The audio pass (`Mp4Audio.Measure`) | 120.7 MB | 120.7 MB | 3,337 MB | 3,097 MB (about 26 s either way) |
| A run's frames (`Frames()`, 172,800) | 345.7 MB in 508,744 reads | 229.9 MB in 172,805 reads | 19,430 MB | 19,286 MB (141 and 143 s, once each) |

Under Node on the AOT bundle, as above, adaptive with stats, both bundles once (the audio pass in a runtime of its own,
as the page's sound worker), the wasm heap after each step and the bytes `BlobStream` read from the `Blob`:

| Step | Wasm heap, before | Wasm heap, after |
| --- | --- | --- |
| The runtime started | 32 MB | 32 MB |
| The file opened | 80 MB | 80 MB |
| The packets (`Packets()`) | 96 MB | 80 MB |
| The run (`Detect`) | 166 MB | 166 MB |
| The stats CSV (`StatsCsv()`) | 166 MB | 166 MB |
| The audio pass (`Audio()`, after opening) | 96 MB | 96 MB |

So, as the patched build had said, the heap's peak stays where it was: the arrays that went died young. Opening
allocates an eighth of what it did and takes about a third of the time, and natively a run no longer reads the audio
(a third of this file). In the browser it still does: `BlobStream` reads a megabyte from wherever a read starts, and the
audio is interleaved with the video in far less than that, so a run read 360.7 MB of the `Blob` after and 361.8 MB
before (the file and the slices' overlaps), and the audio pass 360.7 MB both times. What a run saves there is the
audio's packets allocated and copied, not the reads. (The heap's 166 MB after the run, against 115 MB on 2026-10-30, is
the same before and after this change.)

## Size

A first visit downloads the page and every file in `_framework/` that `dotnet.js` names. GitHub Pages gzips every
response itself (`.wasm` and `.dat` too), so the site holds the published files as they are, with no resource loader,
and the publish makes no `.br`/`.gz` copies (`CompressionEnabled` false: Pages would never serve them; the site is
17 MB, not 32) ([decision](docs/decisions/2026-10-24-the-page.md), "Delivery"). Measured 2026-10-24 in Chromium, first
load, empty cache:

| Bundle | Transferred | Decoded |
| --- | --- | --- |
| Live, `main` at `4f964cf` (with ICU data) | 5.98 MB | 19.13 MB |
| `InvariantGlobalization` (no `icudt_*.dat`), this repository's publish | about 5.1 MB | 16.96 MB |

The second row's transfer is the publish's files through `gzip -6`, which comes within 0.2% of what Pages sends for
the files that didn't change. `dotnet.native.wasm` is most of it: 4.0 MB transferred, 13.3 MB raw (the runtime and
every assembly's AOT code). Since ShotDetector 3.22.0 its frame-source path no longer reaches its FFmpeg path's code
(shotdetector#597), so the trimmer drops FFmpeg.AutoGen and that code: the AOT publish's `_framework/` is 16.23 MB raw
and 5.12 MB through `gzip -6` (2026-10-30, ManagedCodecs 0.2.4), from 19.86 and 5.97 with 3.21.0 (−18% raw, −14%
compressed), `dotnet.native.wasm` 12.6 MB raw, FrameReader 20 KB and ShotDetector 193 KB. Pages answers with `cache-control: max-age=600` and an
ETag, so a visit within ten minutes reads the cache and a later one revalidates.

The threaded bundle (`threads/_framework/`, 2026-10-30: Threads, above) is a second copy of the runtime and every
assembly, built with `WasmEnableThreads`: AOT, 16.90 MB raw and 5.31 MB through `gzip -6`, against the page's own
bundle's 16.23 and 5.12 (ManagedCodecs 0.2.4, ShotDetector 3.22.0; 20.56 and 6.16 with 3.21.0). A visit loads it only
for a run on threads, after the page's own bundle, so a first visit that runs a short video downloads about 10.4 MB
compressed where it was 5.1;
the site holds both.

The sound (`Api.Audio`, 2026-10-26) brought ManagedCodecs' AAC decoder into the trimmed bundle. The AOT publish's
`_framework/` against `main` before it, files through `gzip -6`: 17.03 to 17.92 MB raw (+889 KB, 5.2%), 5.08 to 5.34 MB
compressed (+258 KB, 5.1%). `ManagedCodecs.Aac.wasm` is 168 KB raw (43 KB compressed); the rest is mostly its AOT code
in `dotnet.native.wasm` (+639 KB, +195 KB compressed), then the core's and the App's new code (+67 KB, +16 KB).

## Using the library

```csharp
using ShotDetector;
using ShotDetectorWeb;

var input = VideoInput.Open("trailer.mp4");        // an MP4 or an AVI; or Open(bytes), or Open(() => a new seekable stream)
var result = input.Detect(new DetectionOptions { Detector = DetectorKind.Content, CollectStats = true });
string scenes = Shots.Csv(result.Shots, cuts: result.Cuts);   // scenedetect list-scenes' CSV
string stats = result.Stats!.Csv(result.Video.Position);       // scenedetect -s's CSV
```

`VideoInput.Open` picks the container by the file's first bytes: `AviInput` for an AVI, `Mp4Input` otherwise (each also
has its own `Open`). Both share detection, pausing, the ranges and the packets (`VideoInput`). `VideoInput.Video` is the
`YuvVideo` handed to ShotDetector: what FFmpeg reports for the file, which is what makes the result equal the FFmpeg
path's. ManagedCodecs doesn't expose all of it, so the library reads it itself. In an MP4:

| YuvVideo | From |
| --- | --- |
| Width, Height | the first decoded frame (after the SPS's cropping) |
| FrameRate | timescale × samples / total duration (FFmpeg's avg_frame_rate): from 'stts', and in a fragmented file from every packet (each 'trun') |
| FrameCount | 'stts' sample count (FFmpeg's nb_frames; an edit list that ends early doesn't change it; null in a fragmented file with an empty 'moov', the 'moov' box's samples alone where it has some) |
| TimeBase | 1 / the 'mdhd' timescale |
| StartPts | the first decoded frame's pts (display order, edit list applied: an empty edit makes it nonzero) |
| ColorSpace, FullRange, PixelFormat | the SPS's VUI where it has them, else the 'colr' box (as FFmpeg's h264 decoder); the SPS's chroma format and bit depth: yuv420p, yuv422p or yuv420p10le, full range yuvj420p or yuvj422p at 8 bits |
| ChromaLocation | the SPS's VUI chroma_sample_loc_type_top_field (FFmpeg's location is it plus one), else left, FFmpeg's H.264 default; ShotDetector uses it for 10-bit only |
| Duration | the format's duration as FFmpeg computes it: earliest stream start to latest stream end, audio included; through ffprobe's six decimals to the nearest tick, as ShotDetector reads it |

In an AVI (`AviHeader` reads the 'strh' and 'strf' of each stream, which ManagedCodecs' `AviDemuxer` doesn't expose):

| YuvVideo | From |
| --- | --- |
| Width, Height | the first decoded frame (the JPEG's SOF) |
| FrameRate, TimeBase | the video 'strh''s dwRate/dwScale, reduced (FFmpeg's avg_frame_rate is the time base's inverse) |
| FrameCount | the video 'strh''s dwLength (FFmpeg's nb_frames); a file with fewer chunks is refused |
| StartPts | the first packet's pts |
| ColorSpace, FullRange, PixelFormat, ChromaLocation | bt470bg, full range, yuvj420p or yuvj422p (the JPEG's sampling), center: FFmpeg's MJPEG decoder |
| Duration | the longest stream's: the video's dwLength, and a VBR audio stream's (dwSampleSize 0: MP3, AAC); a PCM or ADPCM stream has none for FFmpeg |

`VideoInput.Packets` lists the video track's packets as FFmpeg's demuxer gives them, in decode order, edit list applied
and discarded packets included (what `ffprobe -show_packets -select_streams v:0` prints): each one's pts (`Pts`, in
`TimeBase`; `Seconds(i)` as ffprobe's pts_time), size in bytes (`Sizes`) and keyframe and discard flags (`Flags`), the
bitrate over time and the encoder's keyframes without decoding; and in an AVI where each packet's data starts in the
file (`Offsets`, the position of the demuxer's one read of the chunk's data, skipped as it is: empty for an MP4), for the
page's picture of an AVI, each MJPEG packet a JPEG. Opening the file already walks every packet for the
duration, with their data skipped; the list is kept on the way, by column, 13 bytes a packet, its arrays allocated
once at the track's sample count. Measured 2026-10-25 natively against `main` on a two-hour file (172,800 frames at
24 fps, 337,501 AAC packets): `Open` keeps 2.25 MB more and allocates 2.25 MB more, in the same time (0.9 s).

On the page it is `Api.Packets()`, called as soon as the file is opened, whose JSON (CLAUDE.md, "Layout") the
timeline's Bitrate lane and its card read (and, for an AVI, the player and the shots' stills: its `offsets`, the JPEGs
cut from the picked `File` with `slice`, so a run on the page's worker, which answers one call at a time, doesn't hold
them up; the owner's choice, 2026-10-31): the bitrate per second, a tick at each keyframe, and after a run how many
cuts fall on a keyframe (a shot's first frame in display order), the page's own reading of the packets. JSON by
column rather than typed arrays: an export returns a string the worker passes on as it is, and a `[JSImport]` of
typed arrays would need a new import in `worker.js`. Measured 2026-10-26 on a two-hour MP4 (the trailer looped, 640x360
at 250 kbit/s with 128 kbit/s AAC, 345,625,385 bytes, 172,800 video packets, 1,794 keyframes), 4-core cloud VM: the JSON
is 2,257,642 characters (13 a packet); under Node `Packets()` takes 20 to 33 ms compiled ahead of time (71 to 111 ms
interpreted) and `JSON.parse` 7 to 11 ms; in headless Chromium on the AOT bundle, the call through the worker takes 35 to
49 ms and the parse 6 to 7 ms, and the lane and card show 0.17 s after the file is opened. On Sintel's trailer
(1,253 packets) the JSON is 14,742 characters, and 5 of the 7 adaptive cuts fall on one of its 17 keyframes.

`VideoInput.Audio` is the first audio track as ffprobe describes it (`AudioStream`: `codec_name`, `sample_rate`,
`channels`; null without one), nothing decoded: in an MP4 the rate and channels from ManagedCodecs' demuxer (an AAC
track's from its AudioSpecificConfig), the codec's FFmpeg name from the headers, its sample entry and, for 'mp4a', the
'esds' object type (ManagedCodecs names an MP3 in 'mp4a' 'mp4a'; FFmpeg says mp3); in an AVI the 'strf''s
WAVEFORMATEX, its wFormatTag named as libavformat's WAV tags (PCM by its bits per sample). An AVI's audio isn't measured
(`Mp4Audio` says so: ManagedCodecs' AVI demuxer gives no audio).

### The sound

```csharp
var audio = Mp4Audio.Measure("trailer.mp4");       // or Measure(a seekable stream); options: AudioOptions
// audio.NotMeasured: why not (no audio track, not AAC, a track the decoder refuses), else null
// audio.Waveform, audio.ReferenceTone, audio.Silences, audio.Peaks, audio.Loudness (Integrated, Range, TruePeak)
```

One pass over the audio packets with ManagedCodecs' `AacDecoder` (AAC LC and HE-AAC), each packet's samples folded
into the measures and dropped, so the track is never held; only the audio packets' data is read. The waveform is
FrameReader's `WaveformBuilder` (BBC audiowaveform's min and max per point, about a thousand points by default), the
reference tone its `ReferenceToneFinder` (ShotDetector's definition: mostly 1 kHz, steady, at least 5 s), silences as
FFmpeg's `silencedetect` (every channel below -60 dBFS for 2 s or more), and FrameReader's `LoudnessMeter` (2.18.0,
shotdetector#646: ebur128's arithmetic without FFmpeg) for the peaks per channel with how many samples reach full scale
and the loudness per ITU-R BS.1770-4 and EBU R128 (integrated in LUFS, loudness range in LU, true peak in dBTP from the
signal oversampled 4 times, BS.1770-4 Annex 2), given the channel weights of ManagedCodecs' AAC channel order. Times are on the file's timeline, from `Track.Start`. On the Sintel trailer the
pass takes about 0.6 s natively. Checked against FFmpeg within tolerances (Checks, audio).

On the page it is `Api.Audio()`, called in a worker of its own alongside a file's first run ("Running the page";
decision [2026-10-26-workers.md](docs/decisions/2026-10-26-workers.md), "The audio pass"), whose JSON
(CLAUDE.md, "Layout") the timeline's Sound lane reads, and the other sound panels with it. Measured 2026-10-26 on the
AOT bundle in headless Chromium (4-core cloud VM): 0.63 s on the Sintel trailer (0.54 s in Firefox 136); 72 s on a
two-hour MP4 (the trailer looped, 640x360 at 2.5 Mbit/s with 128 kbit/s AAC, 2.37 GB, 7,169 s of stereo 48 kHz audio),
whose JSON is 13.5 KB. Under Node, the trailer's pass takes 0.46 s compiled ahead of time and 2.6 s interpreted.

### The picture

```csharp
var meter = new PictureMeter(input.Video);         // or new PictureMeter(video, range) for one range of a split run
var result = input.Detect(options, picture: meter);  // DetectAsync and ScoreRangeAsync take one too
var m = meter.Measures;                              // per frame: YMin, YAvg, YMax, Brng, IsBlack, IsBars, FrameCrop
// m.BlackSpans(2), m.BarsSpans(), m.Crop(); PictureSummary.Of(m): the page's JSON
```

Every run measures each frame as it reads it, before ShotDetector converts it (decision
[2026-10-30-picture-measures.md](docs/decisions/2026-10-30-picture-measures.md)): one pass of `Vector128` code over
the luma (its sum, lowest and highest, the pixels blackdetect counts black and those outside 16 to 235), a compare
over the chroma (signalstats' BRNG), cropdetect's scan from each edge, and two rows for the colour bars. What it keeps
per frame is what FFmpeg's filters count, so the figures are theirs exactly: `signalstats`' YMIN, YAVG, YMAX and BRNG,
`blackdetect`'s spans (any `d`), `cropdetect`'s per frame (`reset=1`) and with its defaults after the last frame. The
colour bars, which no FFmpeg filter finds, are found by their definition (SMPTE's and EBU's seven bars, 75% or 100%,
BT.601 or BT.709). It costs about 2% of a run: 0.12 ms a frame natively and 0.27 ms on the AOT bundle under Node at
854x480, where a run takes 4.7 and 12 ms a frame. A split run's ranges each measure their own frames and send them
with their scores (`RangeBytes`); `Merge` joins them (`PictureMeasures.Join`), byte for byte the whole run's. A
stopped run's are the frames it read. 8-bit 4:2:2 is measured as 4:2:0 is, a chroma row under each luma row (signalstats'
`vsub`), checked the same way; a 10-bit video's frames keep their types only (`PictureMeasures.NotMeasured`, the
summary's `notMeasured`, the page's card saying why): FFmpeg's filters count 10-bit values there with their own
thresholds (backlog, "Picture measures at 10 bits"). On the page it is `Api.Picture()` after a run, the JSON of `PictureSummary`
(CLAUDE.md, "Layout"), at most 2,000 points for the lane whatever the file's length.

## Checks

```
tools/make-samples.sh        # once: samples/ (FFMPEG=<an ffmpeg with libx264>; SINTEL=<Sintel's 1080p trailer MP4>)
export SHOTDETECTOR_FFMPEG_LIBS=<FFmpeg 8.1 shared build folder>   # also put on LD_LIBRARY_PATH by the script
tools/check.sh               # native, then wasm (interpreted); tools/check.sh wasm aot for the AOT build
tools/check.sh page          # the page in Chromium (Playwright: npm ci and npx playwright install chromium in tools/page-check)
tools/check.sh replace       # FastYuv replaced from its source (needs github.com); add aot for the AOT build
tools/check.sh wasm samples/with-audio-320x240-25.mp4   # only the samples named (the files to refuse and without AAC all)
```

- **native** (`tools/NativeCheck`): for each sample, first the core library's `YuvVideo` against FFmpeg's description
  of the file (FrameReader's probe and first decoded frame, as ShotDetector's own FrameInputTests builds it), field by
  field; then each detector (adaptive, content, threshold, histogram, hash, and all five in one run, with stats, then
  deinterlaced: adaptive with `--deinterlace auto`, all five with `on`) on
  (a) the core library (FastYuv's `Yadif` deinterlacing) and (b) ShotDetector's `Detect(path)` with FFmpeg in-process
  (FFmpeg's yadif filter) and the same FastYuv converter:
  frame count, frame rate, length, duration, processed size, cuts, scene CSV, stats CSV, the shots as JSON and HTML
  (`Shots.Json`, `Shots.Html` without images, the page's other two downloads) and the editors' timeline files
  (`TimelineFiles`: EDL, FCPXML, FCP7 XML, OTIO, QP and Aegisub keyframes, both results given the sample's name) must
  be equal (and Subtitle Edit's shot changes, `TimelineFiles`' `shotchanges`), and the YouTube chapters at 10 and 30 s
  (`<sample>.<run>.chapters-<minimum>.json`, ShotDetector's `Timeline.YouTubeChapters` and `ChapterStarts`) too, within
  YouTube's rules (`ChaptersCheck.cs`). It also
  checks that `samples/reject/` is refused, each file with its own message (when opened, or by its run: a size that
  changes midway), and compares each sample's
  `VideoInput.Packets` with `ffprobe -show_packets -select_streams v:0` (the ffprobe beside the FFmpeg libraries): the
  same packets in the same order, each with the same pts, pts_time, size and K and D flags (`PacketCheck.cs`; ffprobe's
  lines go to `out/native/<sample>.packets.txt` for the wasm check); each sample's frames' types as a run keeps them
  (`PictureMeasures.Types`, ManagedCodecs' `VideoFrame.PictureType`) against `ffprobe -show_entries
  frame=pict_type`, frame by frame in display order, an MJPEG sample's all I, whole, on four decoder threads and in
  ranges joined (`FrameTypeCheck.cs`; ffprobe's go to `out/native/<sample>.frametypes.txt`); each frame's interlaced and
  top-field-first flags, which set yadif's parity, against `ffprobe -show_entries frame=interlaced_frame,top_field_first`
  (`FieldCheck.cs`); each sample's run with all five detectors split
  into ranges at keyframes as the page splits a long video (`VideoInput.Ranges`, at most 4: 2 to 4 on the samples), each
  scored on its own, carried as bytes and merged, against the whole run's CSVs byte for byte (`RangeCheck.cs`);
  the same run decoded on four threads (`VideoInput.DecoderThreads`, as the page's threaded runtime decodes), whole, in
  ranges, and stopped half-way against the same run stopped on one thread, byte for byte (`ThreadsCheck.cs`);
  the default run and the run with all five detectors scored as the page's one-worker run is (one range from frame 0),
  stopped at a third, its scores carried as bytes and resumed (`VideoInput.Continue`, the earlier ranges given to
  `ScoreRangeAsync`), stopped again at two thirds and resumed to the end, against the whole run's CSVs, JSON and HTML
  byte for byte, the shots told across the parts the whole run's and the picture measures joined the whole run's; and
  split into 4 ranges each stopped past 60%, the rest split into 4 again from the continuation's first frame, merged
  with the earlier ranges, against the whole run's too (`ResumeCheck.cs`);
  each sample's and each `samples/no-aac/` file's `VideoInput.Audio` against FrameReader's probe of its first audio
  stream, codec name, sample rate and channels, or no audio on both sides (`AudioStreamCheck.cs`); and the page's
  defaults, read from its `app.js` (the thresholds, the adaptive window and minimum content_val), each typed in giving
  every sample the cuts of the run without it, as the page sends a default as none (`DefaultsCheck.cs`; Sintel's
  trailer is the sample that tells histogram's 0.05 from 0.2). Markdown tables go
  to `out/native-report.md`, (a)'s CSVs, JSON and HTML to `out/native/`.
- **audio** (`tools/NativeCheck/AudioCheck.cs`, part of native): for each sample with audio, the core's `Mp4Audio`
  (ManagedCodecs' AAC decoder, no FFmpeg) against FFmpeg's measures of the same file. These are **tolerances, not
  byte-exact**: the decoded samples are within 3e-7 of FFmpeg's, not equal, and the measures are computed by other code (the loudness by ebur128's own arithmetic in FrameReader's `LoudnessMeter`,
  so on the same samples it would be the same doubles; the true peak by BS.1770-4 Annex 2, ebur128's by resampling).
  The waveform against FrameReader's `WaveformData.Read` (FFmpeg decoding, the same points): each value within 1 (of
  32767); the reference tone against ShotDetector's `FindReferenceTone`: the same segments, each end within a packet,
  the level within 0.1 dB; silences against `silencedetect` at its default 2 s and at 1 s: the same spans, each end
  within a packet; integrated loudness and loudness range against `ebur128` (its frame metadata, three decimals)
  within 0.1 LU, true and sample peaks per channel against its `peak=true+sample` within 0.1 dB (its peaks there are
  linear amplitudes to three decimals, which alone is up to 0.04 dB at -20 dBFS). On the tone sample both tones must
  come out at -20 dBFS. Each file in `samples/no-aac/` (MP3 and Opus in MP4, and AAC Main, which the decoder refuses)
  must come back with the reason it isn't measured, not an exception.
- **picture** (`tools/NativeCheck/PictureCheck.cs`, part of native): for each sample, the picture measures of the
  default run (`PictureMeter`) against FFmpeg's filters on the same file in one ffmpeg pass, **exactly**: the frames
  are FFmpeg's and the filters count integers over them. Per frame, `signalstats=stat=brng`'s YMIN and YMAX equal and
  its YAVG and BRNG to the digits it prints, `cropdetect=reset=1:skip=0`'s x1, x2, y1, y2, w, h, x and y equal;
  `cropdetect`'s crop with its defaults after the last frame; `blackdetect`'s spans at `d=0` and at its default 2 s,
  their times to the digits it prints (`-copyts`, the stream's own pts as ours). The colour bars by their definition:
  on the samples that end in `smptehdbars` (make-samples.sh's three shots), one span, the last shot's frames exactly;
  on the fades sample, one span from within its `smptebars` fade-in to the end; none on the others. The run split
  into ranges, each measured while it is scored, must join into the whole run's measures byte for byte. Each sample's
  summary (the page's JSON) goes to `out/native/<sample>.picture.json` for the wasm check.
- **wasm** (`tools/app-check.mjs`, under Node): the page's own .NET side, `src/ShotDetectorWeb.App` published for
  browser-wasm, called through the exports the page's worker calls (`Open`, `Detect`), on the same samples and runs,
  each sample a `Blob` read in slices through the published `blob-read.js` as in the worker (`tools/node-host.mjs`
  gives Node the `FileReaderSync` it lacks);
  its CSVs, its shots as JSON and HTML (`ShotsJson`, `ShotsHtml`) and its timeline files (`TimelineFile`) written to
  `out/wasm/` and compared with `out/native/` file for file (none missing, none extra), the JSON's shots `Detect`'s,
  and its YouTube chapters (`YouTubeChapters`) and shot changes (`TimelineFile`'s `shotchanges`) the same way; a run paused, split or resumed gives the whole run's
  timeline files and chapters too. It also
  checks that progress reaches the last frame, that the shots told while a run goes (the `shot` import the worker posts
  to the page) are the result's shots exactly, that each run stopped half-way (`Stop`, between `Detect`'s pauses, as
  the worker calls it) gives the whole run's stats rows and shots up to there (Stopping a run), its JSON and HTML its own
  shots, that a run cancelled
  half-way keeps no result, that the pauses take under 1% of the runs' time, that the default run and the run with all five
  detectors, split into ranges as the page's workers score them (`Ranges`, `ScoreRange`, `RangeBytes`, one range after another on the one runtime) and
  merged from their bytes (`Merge`) gives the whole run's CSVs, shots (their JSON and HTML too) and warnings, the merge telling the shots as
  `Detect` does, and stopped half-way through its second range (and every range after it, as the page stops every
  worker) the result for its start, held to the whole run as a stopped run is, that ranges scored with other options
  or with damaged bytes fail the merge with "The ranges didn't merge: " in front (the page then detects with one
  worker), that the same two runs stopped at a third, their kept bytes (`Kept`) shown again after the file is opened
  again (`Merge` of them alone: the stopped run's result exactly), resumed (`Resume`) and stopped again at two thirds,
  then opened again and resumed to the end give the whole run's CSVs, shots, JSON, HTML and picture measures, the shots
  told after the stopped part's the whole run's, and stopped in their second range split, the rest split again from
  the kept bytes' first frame and merged with them, the whole run's too, that kept bytes of other options fail to
  resume with "The stopped run didn't resume: " in front (the page then detects from the start), that `samples/reject/` is refused through
  `Open` (or its `Detect`'s promise rejected with the core's message: a size that changes midway), that `Open`'s `audio` is the track `Audio()` measures (none
where it says the file has none; for `samples/no-aac/`, the codec in the file's name), that `Audio()` (the
  sound, as the page asks for it after a run) gives its JSON in the names the panels read on every sample, the core's
  "The file has no audio track." (which the page shows nothing for) for a sample without audio and a reason for each of
  `samples/no-aac/`, that the tone sample's waveform is loud, silent, loud where make-samples.sh puts the tone and the
  gap, each edge within a video frame, and that each waveform's values are within 1 of the
  native check's (`out/native/<sample>.waveform.json`: the same managed code, another runtime), that `Packets()` (the
  bitrate lane's) lists every sample's video packets as ffprobe does (`out/native/<sample>.packets.txt`): the same
  packets in the same order, each with the same pts, pts_time, size and K and D flags, an AVI's offsets each at bytes
  that hash to ffprobe's `data_hash` of the packet (an MP4's null), that `FrameTypes()` after a
  whole run is ffprobe's types as the native check wrote them (`out/native/<sample>.frametypes.txt`), after a split
  run's merge the whole run's, after a stopped run the whole run's first frames, that `Picture()` after a whole
  run is the native check's `out/native/<sample>.picture.json` exactly, after a split run's merge the whole run's, and
  after a stopped run (one worker or split) the frames its result read, and that the publish
  output has `_framework/ShotDetector.FastYuv.wasm` as its own file and `licenses/ShotDetector.FastYuv/COPYING.LGPL`.
  It fails if either bundle has `FFmpeg.AutoGen.wasm` (the trimmer drops it since ShotDetector 3.22.0, nothing on the
  page's path reaching FFmpeg's code), and if any FFmpeg assembly is loaded at all, the audio pass included
  (`LoadedAssemblies()`), interpreted and compiled ahead of time (`aot`, a few minutes more to build) alike. No FFmpeg
  library exists in the browser either way.
- **page** (`tools/page-check/page-check.mjs`, Playwright 1.63.0 pinned by its lockfile, Chromium, and Firefox for a
  resume across a reload): the page's own
  JavaScript (`app.js`, `worker.js`, `threads.js`, `sw.js`, the panels) in a browser, as a visitor drives it, and the
  only check of the threaded bundle (it doesn't run under Node). The published `wwwroot` is
  served as Pages serves it, without headers, the demo clip in `demo/` (`sintel_trailer-480p.mp4`, as `pages.yml` puts
  it), and the page loaded fresh four times, each first made cross-origin isolated by its service worker (the one
  reload of a first visit): `with-audio-320x240-25.mp4` set on the file input and detected with the options as
  the page prefills them, on threads ("on N threads" in the status); the same on the page's worker (`?threads=0`, as
  where the page isn't isolated); the same on two workers (`?workers=2`: the ranges lane's two bands, "with 2 workers"
  in the status); `mjpeg-320x240-25.avi` picked, which no browser plays: its downloads the native run's, the player an
  `<img>` of the file's own JPEG in the video's place, frame 0 at first, then the frame the playhead is in after a click
  on the timeline and the shot's first frame after a click on its row, each decoded at 320x240, and a still drawn for
  every shot; then what those don't have, each picked on threads and on two workers: `rotated-90-320x240-25.mp4`
  (a quarter turn) and `vfr-320x240.mp4` (a variable frame rate, its ranges and lanes at the frames' own times); `interlaced-tff-320x240-25.mp4` with
  Deinterlace auto, on threads and asked for two workers (it runs on one, the ranges lane saying why), its downloads
  the native check's `deinterlace-auto` run's; "Try the demo", on threads, paused a quarter of the way in (the button reading Resume, the bar and
  the status unchanged for 2 s once the run has reached the pause it waits at) and resumed; the demo detected again,
  paused and stopped while paused, then the page reloaded and the demo picked again, which shows the stopped run kept
  in IndexedDB (`stopped`, its options in the form, Resume offered, its downloads the stopped run's) and resumed on
  threads to the whole run's downloads (screenshots of both in `out/page/`); the page reloaded offline (Playwright's network off), from the service worker's
  cache, still isolated, the demo run again from the cache on threads; the demo on two workers (`?workers=2`) stopped
  at 40% and resumed in the same visit, the rest split again; and in Firefox, on the page's worker (`?threads=0`), the
  demo stopped at 30%, the page reloaded, shown again and resumed (`PAGE_CHECK_FIREFOX=skip` leaves Firefox out, where
  Playwright can't download it: a cloud session). Each whole run must end `done`, its scenes and
  stats CSVs, its shots as JSON and HTML and its timeline files, downloaded from the run's card as a visitor clicks
  them (`out/page/`), must equal the native check's run of the detector picked byte for byte (the timeline files but
  for the file's name, the demo's `sintel-trailer`, the sample's `sintel_trailer-480p`; the YouTube chapters at the
  default 30 s and at 10 s typed in, the native run's, downloaded or their reason shown, and at 10 s a title typed for
  the second chapter carried by the download), the shots table must have a row per shot of
  the CSV, and the picture's card must show the native check's cropdetect crop and colour bars with its lane drawn; the
  demo's sound must be measured. The stopped run must end `stopped` at
  the pause it was held at (up to 8 frames after the frame the status said: backlog, "The frame a paused run is held
  at"), its stats CSV the whole run's rows up to there (the adaptive ratio `None` in the last
  frames of its window, as app-check.mjs holds it), its last shot ending there, its JSON and HTML the scenes CSV's
  shots. Any exception, console error (the
  workers' too), failed request or HTTP error fails it. It reuses the bundle `wasm` published in the same call, else
  publishes one (interpreted unless `aot`): about 2 min 20 s interpreted, most of it the demo's run, and well under a
  minute compiled ahead of time (2026-10-29).
- **replace** (`tools/replace-check.mjs`, under Node): LGPL-2.1, FastYuv replaceable (Licensing). It clones shotdetector
  at the tag of the packaged FastYuv, makes its converter's constructor throw, builds it, publishes the App against it
  (`-p:FastYuvAssembly`) and fails unless `Detect` throws that message: the rebuilt bundle runs the replacement.

Needs the .NET 10 SDK, Node 22 and the `wasm-tools` workload (`dotnet workload install wasm-tools`; the App is a
browser-wasm project, so build it with `dotnet publish src/ShotDetectorWeb.App`, not as part of a solution), and for
page Playwright with its Chromium (`npm ci`, then `npx playwright install chromium`, in `tools/page-check`; a cloud
session's pre-installed Playwright does too).
What runs when, on Linux (measured 2026-10-26, before these runs were split: one run of everything took 29 min, of
which wasm interpreted 19 min 10 s, wasm AOT 5 min, replace 2 min 10 s, native 1 min 47 s):

- **A pull request** (`.github/workflows/ci.yml`, job `build`, the check `main` requires): the checks its changed
  files need, by `tools/ci-scope.sh` (git diff against its base; the widest any file needs):
  - **none** for `docs/`, `*.md`, `LICENSE` and the WebCodecs measurement (`tools/webcodecs/`,
    `tools/WebCodecsCheck/`, `tools/WebCodecsWasm/`): `build` runs and passes without a check, which can't see them;
  - **native** for `tools/NativeCheck/`: the native check alone;
  - **page** for the page's own files that no wasm check loads (`index.html`, `style.css`, `app.js`, `timeline.js`,
    `versions.js`, `worker.js`, `threads.js`, `sw.js`, `panels/`) and `tools/page-check/`: the native check, `DefaultsCheck.cs` reading
    `app.js` among it, then the page in Chromium on an interpreted publish (`tools/check.sh page`);
  - **all** for anything else, a path no rule names included (the core, the App's .NET side, `blob-read.js`,
    `blob-write.js` and `pause.js`, which the wasm checks import, the packages, the samples' recipe, the checks' scripts, the workflows).
    All of it is: native on every sample;
  wasm interpreted on one short sample with audio (`with-audio-320x240-25.mp4`: the nine runs, the audio pass, the
  shots told and progress), plus every file to refuse and every file without AAC, with the strict "no FFmpeg assembly
  loaded at all"; wasm AOT, the build the page deploys, on every sample, and the page in Chromium on that bundle. About 7 minutes (7 min 21 s on 2026-10-27, with
  the default and all-detector runs also split into ranges and stopped there; 5 min 21 s on 2026-10-26).
  A newer push to the pull request cancels its run in progress. A filter that skips too much is caught before
  anything deploys: `pages.yml` runs native, AOT, the page and replace on every push to `main`, and the daily run all
  of it.
- **Every day on `main`, and by hand** (`ci.yml`, `schedule` and `workflow_dispatch`): all of it, wasm interpreted on
  every sample and replace with AOT included.
- **A push to `main`** (`.github/workflows/pages.yml`; `ci.yml` doesn't run on it): `main` needn't be up to date with a
  pull request before it merges, so this checks the merged tree before it deploys. Native on every sample, wasm AOT on
  every sample (that publish is the bundle deployed, copied out as it is, not a second one), the page in Chromium on
  that bundle with the demo clip, and replace with AOT.
  Then it adds FastYuv's license, the demo clip and `.nojekyll`, fails unless the uploaded site has `sw.js`,
  `_framework/ShotDetector.FastYuv.wasm`, `threads/_framework/ShotDetector.FastYuv.wasm` and
  `licenses/ShotDetector.FastYuv/COPYING.LGPL` as files of their own, and
  deploys it to GitHub Pages only if all of that passed. `workflow_dispatch` does the same for `main` by hand.
- **Every night, and by hand** (`.github/workflows/realworld.yml`, never on a pull request, not required by `main`):
  the real-world check below.

So wasm interpreted on every sample, the one place that proves nothing of FFmpeg is touched on every sample's path,
runs once a day rather than on each pull request; the AOT build, what visitors run, is checked on every sample on
every pull request and before every deploy.

The samples (tools/make-samples.sh) each isolate something the description must get right: 25, 29.97 and 23.976 fps;
High with B-frames (b-pyramid) and Baseline; 854x480 (cropped) and 720p; untagged, BT.709, BT.601 and full-range
colour; a stream starting at 1.4 s (an empty edit); an edit list ending before the media; an audio track longer than
the video; fades for detect-threshold; bars and tone (1 kHz at -20 dBFS, 3 s of silence, the tone again) for the audio
measures; MJPEG in AVI (25 and 29.97 fps, no audio, PCM that doesn't count for the duration, MP3 that does; 4:2:2);
10-bit 4:2:0 (no chroma location in the VUI, and topleft with full range and BT.709) and 8-bit 4:2:2 (limited and full
range) H.264; seven files to refuse (three of them AVIs: 4:4:4 JPEG, a dropped frame, H.264; 10-bit 4:2:2 and 4:4:4
H.264; one MP4 whose size changes midway); and three whose audio isn't measured. A deinterlaced run over 10 bits is
compared with FFmpeg's yadif with its SIMD off (FFmpeg 8.1's x86 SIMD yadif differs from run to run there; FastYuv's
`Yadif`, the core's, follows its C code).

### Real world

The samples each isolate one thing; real files mix them. `tools/realworld.sh` runs the native check, whole, and the
page's AOT bundle under Node (`app-check.mjs`) on real files, against the same FFmpeg path, byte for byte, and the
bundle's files against the native run's (the owner's choice, 2026-10-31; shotdetector has its own, `realworld.yml` and
`tools/realworld.py`):

```
export SHOTDETECTOR_FFMPEG_LIBS=<FFmpeg 8.1 shared build folder>   # its ffmpeg makes the variants
tools/realworld.sh list                     # the files, each "same" or "refused", and "light" for the long ones
tools/realworld.sh download [name...]       # into samples/realworld/, each SHA-256 checked, cached files too
tools/realworld.sh check [wasm] [name...]   # BUNDLE=<a published wwwroot>, or it publishes the App AOT
```

The files are Blender Foundation films under CC-BY from download.blender.org, the one host of openly licensed video the
cloud sessions reach, pinned by SHA-256 (of the file kept, a zip's member's): Sintel's 720p trailer (52 s, High, AAC),
Big Buck Bunny at 320x180 (the whole film, 10 min, Baseline, AAC without an edit list), Caminandes: Gran Dillama
(2.5 min, 1080p High, AAC), Tears of Steel at 720p (12 min, in a QuickTime file, MP3) and Elephants Dream at 480p (11
min, a variable rate: refused today). For what no openly licensed real file we reach has, the pinned FFmpeg makes
variants of them, as it makes the demo clip: Caminandes fragmented (streams copied), Sintel's trailer rotated a quarter
(copied) and a minute of Caminandes as 1080i25 MBAFF with AAC. A file expected refused
must be refused with an `UnsupportedVideoException`; once its row widens the scope the nightly run says "Not refused",
and the row moves it to "same". On the files over three minutes (Big Buck Bunny, Tears of Steel) the bundle runs only
the default and every-detector runs, whole and split (`--light`): stop, pause and resume, which don't depend on the
file, stay with the samples. `realworld.yml` publishes the bundle once, then runs a job per group of files, each
cached; Tears of Steel's takes the longest, about three hours. What it finds goes to the backlog.

## WebCodecs

Are the frames the browser's own H.264 decoder (WebCodecs' `VideoDecoder`) gives the frames ManagedCodecs gives? A
measurement, not a check: three tools, none of them in `tools/check.sh` or CI (backlog "WebCodecs: are its frames
ManagedCodecs' frames?", [#15](https://github.com/bisforboman/shotdetector-web/issues/15)). The numbers and the verdict
are in the decision's "Not doing" ([docs/decisions/2026-10-24-the-page.md](docs/decisions/2026-10-24-the-page.md)).
Measured 2026-10-26, headless on a VM without a GPU: Chromium 153's frames are ManagedCodecs' frames, every plane of
every frame of every sample and of a two-hour film, decoded and copied out 7 to 10 times faster than ManagedCodecs on
wasm AOT; Firefox 136 hands its frames to the page in RGB (BGRX), so it has no Y, U and V planes to compare. Not
identical in both browsers: the page keeps ManagedCodecs.

```
dotnet build tools/WebCodecsCheck -c Release
dotnet tools/WebCodecsCheck/bin/Release/net10.0/WebCodecsCheck.dll prepare --out out/webcodecs/data samples/*.mp4
dotnet publish tools/WebCodecsWasm -c Release -p:RunAOTCompilation=true    # ManagedCodecs on wasm, for the timings
node tools/webcodecs/serve.mjs --data out/webcodecs/data --results out/webcodecs/results \
  --wasm tools/WebCodecsWasm/bin/Release/net10.0/publish/wwwroot \
  --reference tools/WebCodecsCheck/bin/Release/net10.0/WebCodecsCheck.dll
# then, in each browser: http://localhost:8090/check.html?compare=all&time=sintel_trailer-480p
```

- `tools/WebCodecsCheck` (native): for each file, its video's access units exactly as ManagedCodecs' `Mp4Demuxer` gives
  them (`<name>.units`), and the first 16 bytes of the SHA-256 of each plane of each frame `Mp4Input` decodes, the
  frames ShotDetector gets (`<name>.hashes`); `diff` says where one frame differs from planes the browser copied out.
- `tools/webcodecs/` (a page, no build step, no dependency): `check.html` feeds those access units to `VideoDecoder`
  (the avcC as its description, each unit a chunk with its pts as the timestamp, a discarded unit's frame left out),
  copies each frame's visible Y, U and V planes out with `VideoFrame.copyTo` (I420, packed) and compares their hashes
  with ManagedCodecs' frame by frame, once per `hardwareAcceleration` (`prefer-software`, `prefer-hardware`) that
  `isConfigSupported` accepts; the first frame that differs goes to `serve.mjs`, which asks `WebCodecsCheck diff` where
  and by how much. With `time=`, it times the decode plus the copy against ManagedCodecs' decoder on wasm fed the same
  access units, the runs alternating, each in a worker of its own (on the page's thread a .NET runtime slowed the
  WebCodecs runs after it several times over). Results go to `out/webcodecs/results/<browser>/`, the tables to
  `report.md` there. Any browser can open it; WebCodecs needs a secure context, which localhost is.
- `tools/WebCodecsWasm` (browser-wasm, AOT): ManagedCodecs' `H264Decoder`, one access unit a call, as `Mp4Input`
  decodes for the page.

Tampering with one access unit (a byte of one frame's slice data, a frame no other refers to) makes the page report that
frame and only it, with
its position and how far apart the values are: "identical" is a comparison that can fail.

## Packages

ShotDetector and ShotDetector.FastYuv (3.24.0; 3.14.0 was the first with `Detect(YuvVideo, ...)`, 3.23.0 the first taking
a rotation, a variable frame rate and deinterlacing there, 3.24.0 the first refusing a frame of another size than the
video's, shotdetector#625), FrameReader (2.18.0),
ManagedCodecs (0.4.0: the `ManagedCodecs.Containers`, `ManagedCodecs.H264`, `ManagedCodecs.Mjpeg` and `ManagedCodecs.Aac` packages, each
pinning `ManagedCodecs.Core` at its own version, and `ManagedCodecs.Core` itself, whose `PictureType` the core uses; on
nuget.org since 0.1.0, 2026-10-28, until then a git submodule referenced by project. 0.2.0 to 0.2.2 decode H.264 wrong
under the wasm interpreter, ManagedCodecs#206, fixed in 0.2.3; 0.3.0 says each frame's type, ManagedCodecs#221;
0.4.0 reads only the tracks asked for and gives packets without their data, ManagedCodecs#233, below "What the heap
holds")
and what they depend on come from nuget.org.
Versions are central, in Directory.Packages.props, and a central version pins only the packages a project references
(a transitive one resolves to the lowest its dependant allows: FrameReader's line pinned nothing until the core
referenced it, 2026-10-29). The page's footer shows the versions its bundle carries (`Api.Versions`), and
`tools/app-check.mjs` fails when they aren't the pinned ones. `tools/mp4-edit-list.py`, which `tools/make-samples.sh`
rewrites an edit list with, is ManagedCodecs' `tools/mp4-edit-list.py` as it was in the submodule (MIT).

## Licensing

MIT ([LICENSE](LICENSE)). The app bundles libraries under their own licenses, listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). **ShotDetector.FastYuv is LGPL-2.1-or-later**: it stays its own assembly
(on WebAssembly its own `_framework/ShotDetector.FastYuv.wasm`, and `threads/_framework/ShotDetector.FastYuv.wasm` in
the threaded bundle), and its license text ships next to it
(`licenses/ShotDetector.FastYuv/COPYING.LGPL` in every build and publish output).

To use a changed FastYuv, build it from its source and publish the app against it. Swapping the `.wasm` file in a
published bundle doesn't work: the bundle is trimmed, `dotnet.js` holds each file's hash, and compiled ahead of time
(as the page is) FastYuv's code is also in `dotnet.native.wasm`
([docs/decisions/2026-10-24-fastyuv-replaceable.md](docs/decisions/2026-10-24-fastyuv-replaceable.md)). Its source is
in the shotdetector repository at the package's version (3.24.0, Directory.Packages.props):

```
git clone --branch v3.24.0 https://github.com/bisforboman/shotdetector
# change shotdetector/src/ShotDetector.FastYuv, then:
dotnet build shotdetector/src/ShotDetector.FastYuv -c Release -p:ShotDetectorVersion=3.24.0 -p:FrameReaderVersion=2.18.0
git clone https://github.com/bisforboman/shotdetector-web
dotnet workload install wasm-tools
dotnet publish shotdetector-web/src/ShotDetectorWeb.App -c Release -p:RunAOTCompilation=true \
  -p:FastYuvAssembly=$PWD/shotdetector/src/ShotDetector.FastYuv/bin/Release/net10.0/ShotDetector.FastYuv.dll
# the page is shotdetector-web/src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot, FastYuv's license in
# publish/licenses/ (pages.yml copies it into the site, as licenses/ShotDetector.FastYuv/COPYING.LGPL); the threaded
# bundle the same, with -p:WasmEnableThreads=true as well, into wwwroot/threads/ (Running the page)
```

`tools/check.sh replace` does this, both bundles, with a visibly changed FastYuv and checks the rebuilt bundle runs it
(the page's own: the threaded one doesn't run under Node); CI runs it compiled ahead of time.
