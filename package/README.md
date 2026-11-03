# shotdetector-web

Shot detection in the visitor's browser, before an upload: [ShotDetector](https://github.com/bisforboman/shotdetector)
on WebAssembly, with [ManagedCodecs](https://www.nuget.org/packages/ManagedCodecs.H264) decoding the video. No server,
no FFmpeg, and the video doesn't leave the device. The shots, CSVs and editors' timeline files are the same as
ShotDetector's FFmpeg path gives (PySceneDetect's formats), byte for byte. The
[shotdetector-web](https://github.com/bisforboman/shotdetector-web) repository checks that on every change.

```js
import { detectShots } from 'shotdetector-web';

const run = detectShots(file, {
  detector: 'adaptive',
  onProgress: ({ done, expected }) => bar.value = done / expected,
  onShot: shot => list.append(shot.startTimecode),
});
stopButton.onclick = () => run.stop();   // the shots up to there
const { shots, stills } = await run;     // stills: a JPEG Blob per shot, the shot's middle frame
```

Uses include storyboards and hover-preview sprites, chapter markers, a thumbnail chooser, and "trim to shot" in an
upload form.

## What it reads

The same input as the [page](https://bisforboman.github.io/shotdetector-web/): H.264 in MP4 (8-bit 4:2:0 or 4:2:2,
10-bit 4:2:0, fragmented too, a constant or variable rate, rotated, progressive or interlaced as MBAFF), MJPEG in AVI,
and VP9 or AV1 in WebM (its frames decoded by the browser's own `VideoDecoder`, in the worker: a browser without it
refuses the run, and such a run never goes on threads). Any other file is refused when it's opened, with an `Error`
saying what it is.

## API

### `detectShots(file, options)`

Opens `file` (a `File` or `Blob`), detects, fetches the outputs asked for, takes the stills and ends its worker. It
returns a promise of `{ video, shots, stills, frames, frameRate, duration, stopped, warnings, threads, files }`, which
also has `stop()`, `pause()`, `resume()` and `cancel()`.

| Option | Default | |
| --- | --- | --- |
| `detector` | `'adaptive'` | `'adaptive'`, `'content'`, `'threshold'`, `'histogram'` or `'hash'` |
| `threshold`, `minSceneLength`, `frameWindow`, `minContentVal` | ShotDetector's | as scenedetect's options (`minSceneLength`: `"15"` frames, `"0.6s"`) |
| `detectors` | | several in one run: `[{ kind: 'content', threshold: 30 }, ...]` |
| `deinterlace` | `'off'` | `'auto'` or `'on'` (yadif) |
| `onProgress`, `onShot` | | each progress `{ done, expected }`; each shot as soon as it's known |
| `onPaused` | | after `pause()`, once the run is held: `{ frames }` read, where a `stop()` now ends it |
| `signal` | | an `AbortSignal`: the run is cancelled and its promise rejects with an `AbortError` |
| `stills` | `'middle'` | a still per shot of its `'middle'` or `'first'` frame, or `false` |
| `stillWidth` | `320` | the stills' width in pixels |
| `outputs` | `[]` | into `files`: `'scenesCsv'`, `'statsCsv'` (a `Blob`), `'shotsJson'`, `'shotsHtml'`, `'chapters'`, `'edl'`, `'fcpxml'`, `'fcp7'`, `'otio'`, `'qp'`, `'keyframes'`, `'shotchanges'` |
| `bundle` | `'aot'` | `'aot'` (faster) or `'interpreted'` (a smaller download): below |
| `threads` | `0` | the decoder's threads, where the page is cross-origin isolated: below |
| `base` | the module's folder | where the package's files are served: below |

A **shot** is ShotDetector's JSON: `{ shot, startFrame, endFrame, startTimecode, endTimecode, durationFrames,
durationSeconds }`. **Stills** are the browser's own decode of the frame (a hidden `<video>`, or an AVI's own JPEG), not
ShotDetector's frame. A still the browser can't show is `null`. **Stop** ends the run at its next pause: the result is
the one for the frames read, `stopped: true`.

### `new ShotDetector({ base, bundle, threads })`

The same worker kept across calls:

```js
const detector = new ShotDetector();
const video = await detector.open(file);   // as FFmpeg describes it: size, rate, frames, codec, audio
const result = await detector.detect({ detector: 'content' });
const csv = await detector.scenesCsv();    // also statsCsv(), shotsJson(), shotsHtml(), timeline(format), chapters(min)
const stills = await detector.stills(result.shots, { at: 'middle', width: 320 });
detector.close();
```

## Serving the files

The module starts a Web Worker from `aot/worker.js` (or `interpreted/worker.js`), and .NET's `dotnet.js` loads the
`.wasm` files next to it, at run time. Serve the package's folder as it is (`.wasm` as `application/wasm`, `.js` and
`.mjs` as `text/javascript`: the threaded runtime's own workers are `.mjs`), and don't
let a bundler rewrite those files: copy `node_modules/shotdetector-web/` to your static files and pass `base`, or load
the module itself from there:

```js
const { detectShots } = await import('/vendor/shotdetector-web/index.js'); // only when a video is picked
```

From a CDN (`https://cdn.jsdelivr.net/npm/shotdetector-web@0.1.0/`), `base` pointing there works: the worker is started
from a same-origin `blob:` module that imports the CDN's.

## Size

Each bundle's `_framework/`, compressed per file as a server sends it (2026-11-02):

| `bundle` | gzip | brotli | speed |
| --- | --- | --- | --- |
| `'aot'` | 5.4 MB | 3.8 MB | about 4–5 times the interpreted |
| `'interpreted'` | 2.1 MB | 1.7 MB | |

A page downloads only the bundle it uses, when it first opens a file, and the browser caches it.

## Threads

`threads: n` (2 or more) decodes on several threads with the threaded AOT bundle, on the page's own thread (.NET's
threaded runtime doesn't start in a worker). It needs `SharedArrayBuffer`, so the page must be cross-origin isolated:
`Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` (or `credentialless`). Those
headers also restrict what else the page may embed. Where the page isn't isolated, the run goes on the worker as
without `threads`, and `result.threads` is 1. There's one threaded runtime per page: one detector on threads at a time.

## Licences

MIT, but for ShotDetector.FastYuv, which is **LGPL-2.1-or-later** and stays its own file in each bundle
(`*/_framework/ShotDetector.FastYuv.wasm`), with its licence in `licenses/ShotDetector.FastYuv/COPYING.LGPL`. The other
libraries' licences are in `licenses/THIRD-PARTY-NOTICES.md`. A site that serves this package passes FastYuv on to its
visitors, so it should serve `licenses/` with it and say where FastYuv's source and this recipe are.

The published `.wasm` file can't be swapped alone: the bundle is trimmed, `dotnet.js` holds each file's hash, and AOT
compiles FastYuv's code into `dotnet.native.wasm`. To use a changed FastYuv, rebuild the package against it, from
FastYuv's source at the version in `package.json`'s `shotdetector` field:

```
git clone --branch v<version> https://github.com/bisforboman/shotdetector
# change shotdetector/src/ShotDetector.FastYuv, then build it:
dotnet build shotdetector/src/ShotDetector.FastYuv -c Release -p:ShotDetectorVersion=<version> -p:FrameReaderVersion=<FrameReader's>
git clone --branch package-v<this version> https://github.com/bisforboman/shotdetector-web
cd shotdetector-web
dotnet workload install wasm-tools
tools/package.sh -p:FastYuvAssembly=<path to the built ShotDetector.FastYuv.dll>
# the package is out/package/: npm pack there
```

The repository's CI proves the recipe: a FastYuv built to throw is packed this way, and every bundle runs it.
