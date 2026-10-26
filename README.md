# shotdetector-web

Shot detection entirely in the browser: a video file is decoded, converted and analysed on WebAssembly, and nothing
leaves the machine. It gives the same shots as [scenedetect](https://www.scenedetect.com/) through
[ShotDetector](https://github.com/bisforboman/shotdetector), byte for byte, without FFmpeg:

- **[ManagedCodecs](https://github.com/bisforboman/ManagedCodecs)** demuxes the MP4 and decodes the H.264 (pure managed
  C#, bit-exact with FFmpeg's decoder);
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
`src/ShotDetectorWeb.Core` opens an MP4 and detects; the checks show that its scene and stats CSVs equal ShotDetector's
FFmpeg path's on every sample, all five detectors, natively and on browser-wasm (interpreted and AOT), the latter
through the page's own exports (`src/ShotDetectorWeb.App`, the .NET side of the page's worker).

Supported so far: **8-bit 4:2:0 H.264 (progressive) in a non-fragmented MP4, at a constant frame rate, not rotated.**
Anything else is refused when the file is opened, with an `UnsupportedVideoException` saying what (not MP4, another
codec, 4:2:2/4:4:4 or 10-bit, interlaced, rotated, fragmented, variable frame rate). Damaged files throw ManagedCodecs'
`InvalidDataException`.

The page's "Try the demo" runs Sintel's trailer, re-encoded at deploy time by the recipe CI's checks run on
(`tools/make-samples.sh`), so the file visitors try is the one the exactness check covers. How the page is built is
decided in
[docs/decisions/2026-10-24-the-page.md](docs/decisions/2026-10-24-the-page.md); the work, row by row, is in
[docs/backlog.md](docs/backlog.md).

## Running the page

```
dotnet publish src/ShotDetectorWeb.App -c Release            # -p:RunAOTCompilation=true for the build Pages gets
python3 -m http.server -d src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot 8000
```

Then open http://localhost:8000. Any static server works if it serves `.wasm` as `application/wasm` (Python's does). The
page is `wwwroot/index.html`, `style.css`, `app.js` (the page's states), `timeline.js` and a module per panel in
`panels/`, native ES modules with no build step and no dependencies; `worker.js` owns the .NET runtime and calls the
exports the page names, and `blob-read.js` reads the picked file for it. Pick an MP4 (or drop one), choose the detector,
its threshold (prefilled with ShotDetector's default for it) and the minimum shot length, and detect. Under the player
is a timeline: one time axis, a playhead that follows the video, a click anywhere on it seeks, and one lane per thing
over time, today the cuts (a tick at each, as soon as it is known, and the part of the file read so far shaded) and,
once the first run is done, the sound: its waveform, decoded in the worker by ManagedCodecs' AAC decoder (`Api.Audio`;
"The sound", below; no lane for a file without audio, one line saying why for audio that isn't AAC). Beside
the player are cards, today the run's summary and downloads; below, the shots as a table whose rows play the video from
the shot, each row as soon as ShotDetector knows the shot (a few frames after the cut that ends it,
`DetectionOptions.OnShot`). At phone width the cards stack under the timeline. The scenes and stats CSVs
(`<file>.scenes.csv`, `<file>.stats.csv`, the file's name without its extension) are the ones scenedetect writes. A file
of any size runs: the worker gets the `File` itself, and .NET reads it in slices of a megabyte as it needs them
(`BlobStream` over `FileReaderSync`), so it is never copied into memory whole (Large files, below). Before a run the
page says how long it will take, an estimate from the frame count and picture size (this browser's speed from its last
run, else `REFERENCE_FPS` at the top of `app.js`); Cancel stops the worker and starts a new one, and keeps the shots
found so far, with one more line, marked unfinished, from the last cut to where the run stopped (no downloads for a
cancelled run). Errors are the library's messages, a damaged file's prefixed "The file is damaged or not an MP4 this
page can read:". "Try the demo" fetches `demo/sintel-trailer.mp4` next to the page and runs it as a picked file:
`pages.yml` puts the Sintel sample there; locally, copy `samples/sintel_trailer-480p.mp4` to
`wwwroot/demo/sintel-trailer.mp4` in the publish folder.

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

The CSVs reach JavaScript as strings of their own, without growing the heap. Until 2026-10-25 they were inside
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
heap ends where the run leaves it, 208 MB under Node. The CSVs are unchanged (`tools/check.sh`). What is left waits
upstream (backlog, Waiting): the stats CSV written row by row rather than built as one string, ManagedCodecs' index
built without its copies.

An open file also holds its video packet list (`Mp4Input.Packets`, below; in the 2026-10-26 numbers): 13 bytes a frame,
2.25 MB on a two-hour film.

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
every assembly's AOT code). FFmpeg.AutoGen, which nothing on the page's path runs (backlog, the trimming row), costs
103 KB transferred (447 KB raw); FrameReader 26 KB (80 KB). Pages answers with `cache-control: max-age=600` and an
ETag, so a visit within ten minutes reads the cache and a later one revalidates.

The sound (`Api.Audio`, 2026-10-26) brought ManagedCodecs' AAC decoder into the trimmed bundle. The AOT publish's
`_framework/` against `main` before it, files through `gzip -6`: 17.03 to 17.92 MB raw (+889 KB, 5.2%), 5.08 to 5.34 MB
compressed (+258 KB, 5.1%). `ManagedCodecs.Aac.wasm` is 168 KB raw (43 KB compressed); the rest is mostly its AOT code
in `dotnet.native.wasm` (+639 KB, +195 KB compressed), then the core's and the App's new code (+67 KB, +16 KB).

## Using the library

```csharp
using ShotDetector;
using ShotDetectorWeb;

var input = Mp4Input.Open("trailer.mp4");          // or Open(bytes), or Open(() => a new seekable stream)
var result = input.Detect(new DetectionOptions { Detector = DetectorKind.Content, CollectStats = true });
string scenes = Shots.Csv(result.Shots, cuts: result.Cuts);   // scenedetect list-scenes' CSV
string stats = result.Stats!.Csv(result.Video.Position);       // scenedetect -s's CSV
```

`Mp4Input.Video` is the `YuvVideo` handed to ShotDetector: what FFmpeg reports for the file, which is what makes the
result equal the FFmpeg path's. ManagedCodecs doesn't expose all of it, so the library reads it itself:

| YuvVideo | From |
| --- | --- |
| Width, Height | the first decoded frame (after the SPS's cropping) |
| FrameRate | 'stts': timescale × samples / total duration (FFmpeg's avg_frame_rate) |
| FrameCount | 'stts' sample count (FFmpeg's nb_frames; an edit list that ends early doesn't change it) |
| TimeBase | 1 / the 'mdhd' timescale |
| StartPts | the first decoded frame's pts (display order, edit list applied: an empty edit makes it nonzero) |
| ColorSpace, FullRange, PixelFormat | the SPS's VUI where it has them, else the 'colr' box (as FFmpeg's h264 decoder); full range is yuvj420p |
| Duration | the format's duration as FFmpeg computes it: earliest stream start to latest stream end, audio included; through ffprobe's six decimals to the nearest tick, as ShotDetector reads it |

`Mp4Input.Packets` lists the video track's packets as FFmpeg's demuxer gives them, in decode order, edit list applied
and discarded packets included (what `ffprobe -show_packets -select_streams v:0` prints): each one's pts (`Pts`, in
`TimeBase`; `Seconds(i)` as ffprobe's pts_time), size in bytes (`Sizes`) and keyframe and discard flags (`Flags`), the
bitrate over time and the encoder's keyframes without decoding. Opening the file already walks every packet for the
duration, with their data skipped; the list is kept on the way, by column, 13 bytes a packet, its arrays allocated
once at the track's sample count. Measured 2026-10-25 natively against `main` on a two-hour file (172,800 frames at
24 fps, 337,501 AAC packets): `Open` keeps 2.25 MB more and allocates 2.25 MB more, in the same time (0.9 s).

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
FFmpeg's `silencedetect` (every channel below -60 dBFS for 2 s or more), peaks per channel with how many samples reach
full scale, and loudness per ITU-R BS.1770-4 and EBU R128 (integrated in LUFS, loudness range in LU, true peak in dBTP
from the signal oversampled 4 times). Times are on the file's timeline, from `Track.Start`. On the Sintel trailer the
pass takes about 0.6 s natively. Checked against FFmpeg within tolerances (Checks, audio).

On the page it is `Api.Audio()`, called once a file's first run is done ("Running the page"), whose JSON
(CLAUDE.md, "Layout") the timeline's Sound lane reads, and the other sound panels with it. Measured 2026-10-26 on the
AOT bundle in headless Chromium (4-core cloud VM): 0.63 s on the Sintel trailer (0.54 s in Firefox 136); 72 s on a
two-hour MP4 (the trailer looped, 640x360 at 2.5 Mbit/s with 128 kbit/s AAC, 2.37 GB, 7,169 s of stereo 48 kHz audio),
whose JSON is 13.5 KB. Under Node, the trailer's pass takes 0.46 s compiled ahead of time and 2.6 s interpreted.

## Checks

```
tools/make-samples.sh        # once: samples/ (FFMPEG=<an ffmpeg with libx264>; SINTEL=<Sintel's 1080p trailer MP4>)
export SHOTDETECTOR_FFMPEG_LIBS=<FFmpeg 8.1 shared build folder>   # also put on LD_LIBRARY_PATH by the script
tools/check.sh               # native, then wasm (interpreted); tools/check.sh wasm aot for the AOT build
tools/check.sh replace       # FastYuv replaced from its source (needs github.com); add aot for the AOT build
tools/check.sh wasm samples/with-audio-320x240-25.mp4   # only the samples named (the files to refuse and without AAC all)
```

- **native** (`tools/NativeCheck`): for each sample, first the core library's `YuvVideo` against FFmpeg's description
  of the file (FrameReader's probe and first decoded frame, as ShotDetector's own FrameInputTests builds it), field by
  field; then each detector (adaptive, content, threshold, histogram, hash, and all five in one run, with stats) on
  (a) the core library and (b) ShotDetector's `Detect(path)` with FFmpeg in-process and the same FastYuv converter:
  frame count, frame rate, length, duration, processed size, cuts, scene CSV and stats CSV must be equal. It also
  checks that `samples/reject/` is refused, each file with its own message, and compares each sample's
  `Mp4Input.Packets` with `ffprobe -show_packets -select_streams v:0` (the ffprobe beside the FFmpeg libraries): the
  same packets in the same order, each with the same pts, pts_time, size and K and D flags (`PacketCheck.cs`). Markdown
  tables go to `out/native-report.md`, (a)'s CSVs to `out/native/`.
- **audio** (`tools/NativeCheck/AudioCheck.cs`, part of native): for each sample with audio, the core's `Mp4Audio`
  (ManagedCodecs' AAC decoder, no FFmpeg) against FFmpeg's measures of the same file. These are **tolerances, not
  byte-exact**: the decoded samples are within 3e-7 of FFmpeg's, not equal, and the measures are computed by other code.
  The waveform against FrameReader's `WaveformData.Read` (FFmpeg decoding, the same points): each value within 1 (of
  32767); the reference tone against ShotDetector's `FindReferenceTone`: the same segments, each end within a packet,
  the level within 0.1 dB; silences against `silencedetect` at its default 2 s and at 1 s: the same spans, each end
  within a packet; integrated loudness and loudness range against `ebur128` (its frame metadata, three decimals)
  within 0.1 LU, true and sample peaks per channel against its `peak=true+sample` within 0.1 dB (its peaks there are
  linear amplitudes to three decimals, which alone is up to 0.04 dB at -20 dBFS). On the tone sample both tones must
  come out at -20 dBFS. Each file in `samples/no-aac/` (MP3 and Opus in MP4, and AAC Main, which the decoder refuses)
  must come back with the reason it isn't measured, not an exception.
- **wasm** (`tools/app-check.mjs`, under Node): the page's own .NET side, `src/ShotDetectorWeb.App` published for
  browser-wasm, called through the exports the page's worker calls (`Open`, `Detect`), on the same samples and runs,
  each sample a `Blob` read in slices through the published `blob-read.js` as in the worker (`tools/node-host.mjs`
  gives Node the `FileReaderSync` it lacks);
  its CSVs written to `out/wasm/` and compared with `out/native/` file for file (none missing, none extra). It also
  checks that progress reaches the last frame, that the shots told while a run goes (the `shot` import the worker posts
  to the page) are the result's shots exactly, that `samples/reject/` is refused through `Open`, that `Audio()` (the
  sound, as the page asks for it after a run) gives its JSON in the names the panels read on every sample, the core's
  "The file has no audio track." (which the page shows nothing for) for a sample without audio and a reason for each of
  `samples/no-aac/`, that the tone sample's waveform is loud, silent, loud where make-samples.sh puts the tone and the
  gap, each edge within a video frame, and that each waveform's values are within 1 of the
  native check's (`out/native/<sample>.waveform.json`: the same managed code, another runtime), and that the publish
  output has `_framework/ShotDetector.FastYuv.wasm` as its own file and `licenses/ShotDetector.FastYuv/COPYING.LGPL`.
  Interpreted, it fails if FFmpeg.AutoGen (in the bundle because ShotDetector references it, and FrameReader, whose
  waveform and tone code the audio pass runs) is loaded at all, the audio pass included (`LoadedAssemblies()`): the
  runtime loads an assembly only when code needs it, so nothing on the path touches
  FFmpeg. Compiled ahead of time (`aot`, a few minutes more to build), the runtime loads some assemblies at start that
  the interpreter never touches, FFmpeg.AutoGen among them, so there it fails only if the runs load one. No FFmpeg
  library exists in the browser either way.
- **replace** (`tools/replace-check.mjs`, under Node): LGPL-2.1, FastYuv replaceable (Licensing). It clones shotdetector
  at the tag of the packaged FastYuv, makes its converter's constructor throw, builds it, publishes the App against it
  (`-p:FastYuvAssembly`) and fails unless `Detect` throws that message: the rebuilt bundle runs the replacement.

Needs the .NET 10 SDK, Node 22 and the `wasm-tools` workload (`dotnet workload install wasm-tools`; the App is a
browser-wasm project, so build it with `dotnet publish src/ShotDetectorWeb.App`, not as part of a solution).
What runs when, on Linux (measured 2026-10-26, before these runs were split: one run of everything took 29 min, of
which wasm interpreted 19 min 10 s, wasm AOT 5 min, replace 2 min 10 s, native 1 min 47 s):

- **A pull request** (`.github/workflows/ci.yml`, job `build`, the check `main` requires): native on every sample;
  wasm interpreted on one short sample with audio (`with-audio-320x240-25.mp4`: the six runs, the audio pass, the
  shots told and progress), plus every file to refuse and every file without AAC, with the strict "no FFmpeg assembly
  loaded at all"; wasm AOT, the build the page deploys, on every sample. About 5 minutes (5 min 21 s on 2026-10-26).
  A newer push to the pull request cancels its run in progress. A pull request that changes only `docs/`, `*.md` or
  `LICENSE` (git diff against its base) runs `build` too, which passes without running any check: the checks can't
  see those files.
- **Every day on `main`, and by hand** (`ci.yml`, `schedule` and `workflow_dispatch`): all of it, wasm interpreted on
  every sample and replace with AOT included.
- **A push to `main`** (`.github/workflows/pages.yml`; `ci.yml` doesn't run on it): `main` needn't be up to date with a
  pull request before it merges, so this checks the merged tree before it deploys. Native on every sample, wasm AOT on
  every sample (that publish is the bundle deployed, copied out as it is, not a second one), and replace with AOT.
  Then it adds FastYuv's license, the demo clip and `.nojekyll`, fails unless the uploaded site has
  `_framework/ShotDetector.FastYuv.wasm` and `licenses/ShotDetector.FastYuv/COPYING.LGPL` as files of their own, and
  deploys it to GitHub Pages only if all of that passed. `workflow_dispatch` does the same for `main` by hand.

So wasm interpreted on every sample, the one place that proves nothing of FFmpeg is touched on every sample's path,
runs once a day rather than on each pull request; the AOT build, what visitors run, is checked on every sample on
every pull request and before every deploy.

The samples (tools/make-samples.sh) each isolate something the description must get right: 25, 29.97 and 23.976 fps;
High with B-frames (b-pyramid) and Baseline; 854x480 (cropped) and 720p; untagged, BT.709, BT.601 and full-range
colour; a stream starting at 1.4 s (an empty edit); an edit list ending before the media; an audio track longer than
the video; fades for detect-threshold; bars and tone (1 kHz at -20 dBFS, 3 s of silence, the tone again) for the audio
measures; six files to refuse; and three whose audio isn't measured.

## Packages

ShotDetector and ShotDetector.FastYuv (3.16.1; 3.14.0 was the first with `Detect(YuvVideo, ...)`), FrameReader (2.11.0,
which ShotDetector 3.16.1 depends on) and what they depend on come from nuget.org. Versions are central, in
Directory.Packages.props.

ManagedCodecs isn't on NuGet: it is a git submodule, `external/ManagedCodecs` (`git clone --recursive`, or
`git submodule update --init`), referenced by project.

## Licensing

MIT ([LICENSE](LICENSE)). The app bundles libraries under their own licenses, listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). **ShotDetector.FastYuv is LGPL-2.1-or-later**: it stays its own assembly
(on WebAssembly its own `_framework/ShotDetector.FastYuv.wasm`), and its license text ships next to it
(`licenses/ShotDetector.FastYuv/COPYING.LGPL` in every build and publish output).

To use a changed FastYuv, build it from its source and publish the app against it. Swapping the `.wasm` file in a
published bundle doesn't work: the bundle is trimmed, `dotnet.js` holds each file's hash, and compiled ahead of time
(as the page is) FastYuv's code is also in `dotnet.native.wasm`
([docs/decisions/2026-10-24-fastyuv-replaceable.md](docs/decisions/2026-10-24-fastyuv-replaceable.md)). Its source is
in the shotdetector repository at the package's version (3.16.1, Directory.Packages.props):

```
git clone --branch v3.16.1 https://github.com/bisforboman/shotdetector
# change shotdetector/src/ShotDetector.FastYuv, then:
dotnet build shotdetector/src/ShotDetector.FastYuv -c Release -p:ShotDetectorVersion=3.16.1 -p:FrameReaderVersion=2.11.0
git clone --recursive https://github.com/bisforboman/shotdetector-web
dotnet workload install wasm-tools
dotnet publish shotdetector-web/src/ShotDetectorWeb.App -c Release -p:RunAOTCompilation=true \
  -p:FastYuvAssembly=$PWD/shotdetector/src/ShotDetector.FastYuv/bin/Release/net10.0/ShotDetector.FastYuv.dll
# the page is shotdetector-web/src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot, FastYuv's license in
# publish/licenses/ (pages.yml copies it into the site, as licenses/ShotDetector.FastYuv/COPYING.LGPL)
```

`tools/check.sh replace` does this with a visibly changed FastYuv and checks the rebuilt bundle runs it; CI runs it
compiled ahead of time.
