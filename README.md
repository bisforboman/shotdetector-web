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
`.github/workflows/pages.yml` after CI passes on each push to `main` (it runs locally too: "Running the page").
`src/ShotDetectorWeb.Core` opens an MP4 and detects; the checks show that its scene and stats CSVs equal ShotDetector's
FFmpeg path's on every sample, all five detectors, natively and on browser-wasm (interpreted and AOT), the latter
through the page's own exports (`src/ShotDetectorWeb.App`, the .NET side of the page's worker).

Supported so far: **8-bit 4:2:0 H.264 (progressive) in a non-fragmented MP4, at a constant frame rate, not rotated.**
Anything else is refused when the file is opened, with an `UnsupportedVideoException` saying what (not MP4, another
codec, 4:2:2/4:4:4 or 10-bit, interlaced, rotated, fragmented, variable frame rate). Damaged files throw ManagedCodecs'
`InvalidDataException`.

Next: the demo clip. How the page is built is decided in
[docs/decisions/2026-10-24-the-page.md](docs/decisions/2026-10-24-the-page.md); the work, row by row, is in
[docs/backlog.md](docs/backlog.md).

## Running the page

```
dotnet publish src/ShotDetectorWeb.App -c Release            # -p:RunAOTCompilation=true for the build Pages gets
python3 -m http.server -d src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot 8000
```

Then open http://localhost:8000. Any static server works if it serves `.wasm` as `application/wasm` (Python's does). The
page is `wwwroot/index.html`, `app.js` and `style.css`, no build step and no dependencies; `worker.js` owns the .NET
runtime and answers the page's `open` and `detect` messages, and `blob-read.js` reads the picked file for it. Pick an
MP4 (or drop one), choose the detector, its threshold (prefilled with ShotDetector's default for it) and the minimum
shot length, and detect: the shots come as a table whose rows seek the player, and the scenes and stats CSVs
(`<file>.scenes.csv`, `<file>.stats.csv`, the file's name without its extension) are the ones scenedetect writes. A file
of any size runs: the worker gets the `File` itself, and .NET reads it in slices of a megabyte as it needs them
(`BlobStream` over `FileReaderSync`), so it is never copied into memory whole (Large files, below). Before a run the
page says how long it will take, an estimate from the frame count and picture size (this browser's speed from its last
run, else `REFERENCE_FPS` at the top of `app.js`); Cancel stops the worker and starts a new one. Errors are the
library's messages, a damaged file's prefixed "The file is damaged or not an MP4 this page can read:".

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
heap grows with the frame count (about 4 KB a frame; Sintel's 1,253 frames: 67 MB), not with the file (backlog, "Memory
per frame"). During the control's run the renderer stayed at 450 to 500 MB, and its peak came as the result was built
and shown; with the big file it rose to about 800 MB in the first half hour and no further (the slices `FileReaderSync`
returns, which V8 frees late while .NET holds the thread). Before the duration pass skipped the packets' data, opening
the same file took 43 s and peaked the renderer at 1.7 GB. The estimate before the big file's run said 35 min (the
reference rate scaled by picture size); the next run in the same browser uses its own measured rate.

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

## Checks

```
tools/make-samples.sh        # once: samples/ (FFMPEG=<an ffmpeg with libx264>; SINTEL=<Sintel's 1080p trailer MP4>)
export SHOTDETECTOR_FFMPEG_LIBS=<FFmpeg 8.1 shared build folder>   # also put on LD_LIBRARY_PATH by the script
tools/check.sh               # native, then wasm (interpreted); tools/check.sh wasm aot for the AOT build
tools/check.sh replace       # FastYuv replaced from its source (needs github.com); add aot for the AOT build
```

- **native** (`tools/NativeCheck`): for each sample, first the core library's `YuvVideo` against FFmpeg's description
  of the file (FrameReader's probe and first decoded frame, as ShotDetector's own FrameInputTests builds it), field by
  field; then each detector (adaptive, content, threshold, histogram, hash, and all five in one run, with stats) on
  (a) the core library and (b) ShotDetector's `Detect(path)` with FFmpeg in-process and the same FastYuv converter:
  frame count, frame rate, length, duration, processed size, cuts, scene CSV and stats CSV must be equal. It also
  checks that `samples/reject/` is refused, each file with its own message. A Markdown table goes to
  `out/native-report.md`, (a)'s CSVs to `out/native/`.
- **wasm** (`tools/app-check.mjs`, under Node): the page's own .NET side, `src/ShotDetectorWeb.App` published for
  browser-wasm, called through the exports the page's worker calls (`Open`, `Detect`), on the same samples and runs,
  each sample a `Blob` read in slices through the published `blob-read.js` as in the worker (`tools/node-host.mjs`
  gives Node the `FileReaderSync` it lacks);
  its CSVs written to `out/wasm/` and compared with `out/native/` file for file (none missing, none extra). It also
  checks that progress reaches the last frame, that `samples/reject/` is refused through `Open`, and that the publish
  output has `_framework/ShotDetector.FastYuv.wasm` as its own file and `licenses/ShotDetector.FastYuv/COPYING.LGPL`.
  Interpreted, it fails if FFmpeg.AutoGen (in the bundle because ShotDetector references it) is loaded at all
  (`LoadedAssemblies()`): the runtime loads an assembly only when code needs it, so nothing on the path touches
  FFmpeg. Compiled ahead of time (`aot`, a few minutes more to build), the runtime loads some assemblies at start that
  the interpreter never touches, FFmpeg.AutoGen among them, so there it fails only if the runs load one. No FFmpeg
  library exists in the browser either way.
- **replace** (`tools/replace-check.mjs`, under Node): LGPL-2.1, FastYuv replaceable (Licensing). It clones shotdetector
  at the tag of the packaged FastYuv, makes its converter's constructor throw, builds it, publishes the App against it
  (`-p:FastYuvAssembly`) and fails unless `Detect` throws that message: the rebuilt bundle runs the replacement.

Needs the .NET 10 SDK, Node 22 and the `wasm-tools` workload (`dotnet workload install wasm-tools`; the App is a
browser-wasm project, so build it with `dotnet publish src/ShotDetectorWeb.App`, not as part of a solution).
`.github/workflows/ci.yml` runs all of it on Linux; after it passes on a push to `main`,
`.github/workflows/pages.yml` publishes the App ahead of time, adds FastYuv's license and `.nojekyll`, fails unless
the uploaded site has `_framework/ShotDetector.FastYuv.wasm` and `licenses/ShotDetector.FastYuv/COPYING.LGPL` as files
of their own, and deploys it to GitHub Pages.

The samples (tools/make-samples.sh) each isolate something the description must get right: 25, 29.97 and 23.976 fps;
High with B-frames (b-pyramid) and Baseline; 854x480 (cropped) and 720p; untagged, BT.709, BT.601 and full-range
colour; a stream starting at 1.4 s (an empty edit); an edit list ending before the media; an audio track longer than
the video; fades for detect-threshold; and six files to refuse.

## Packages

ShotDetector and ShotDetector.FastYuv (3.15.0; 3.14.0 was the first with `Detect(YuvVideo, ...)`), FrameReader (2.11.0,
which ShotDetector depends on) and what they depend on come from nuget.org. Versions are central, in
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
in the shotdetector repository at the package's version (3.15.0, Directory.Packages.props):

```
git clone --branch v3.15.0 https://github.com/bisforboman/shotdetector
# change shotdetector/src/ShotDetector.FastYuv, then:
dotnet build shotdetector/src/ShotDetector.FastYuv -c Release -p:ShotDetectorVersion=3.15.0 -p:FrameReaderVersion=2.11.0
git clone --recursive https://github.com/bisforboman/shotdetector-web
dotnet workload install wasm-tools
dotnet publish shotdetector-web/src/ShotDetectorWeb.App -c Release -p:RunAOTCompilation=true \
  -p:FastYuvAssembly=$PWD/shotdetector/src/ShotDetector.FastYuv/bin/Release/net10.0/ShotDetector.FastYuv.dll
# the page is shotdetector-web/src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot, FastYuv's license in
# publish/licenses/ (pages.yml copies it into the site, as licenses/ShotDetector.FastYuv/COPYING.LGPL)
```

`tools/check.sh replace` does this with a visibly changed FastYuv and checks the rebuilt bundle runs it; CI runs it
compiled ahead of time.
