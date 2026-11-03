# An embeddable npm package (2026-11-02)

Issue [#92](https://github.com/bisforboman/shotdetector-web/issues/92), chosen by the owner on 2026-11-02: package the
page's engine so another site can detect shots in its visitor's browser, before an upload, with an npm package and
maybe a `<shot-detector>` web component. This decides the design before any package code; the package is built by its
own backlog row ("The npm package") once this has the owner's answers.

Measured on 2026-11-02 against `main` at `996e910` (ShotDetector/FastYuv 3.26.0, FrameReader 2.20.0, ManagedCodecs
0.5.0, .NET SDK 10.0.114), the App published as `tools/check.sh wasm aot` publishes it (`publish_app`: `-c Release
-warnaserror`, `RunAOTCompilation` false then true), without the threaded bundle.

## What the package is for

A site's upload form takes a video; before it uploads, the page detects the shots in the visitor's browser and uses
them: a storyboard or hover-preview sprite, chapter markers, a thumbnail chooser, "trim to shot". No server-side
FFmpeg, and the video doesn't leave the device before the visitor confirms. The results are this repository's results:
the same shots, CSVs and timeline files as ShotDetector's FFmpeg path, byte for byte (the core idea, CLAUDE.md), on the
same input (CLAUDE.md, "Scope so far"; issue #89 for the limits).

## What exists to build on

- `wwwroot/worker.js` owns the .NET runtime and calls any `Api` export by name (`{ type: 'call', id, name, args }`),
  posts `progress` and `shot` while `Detect` runs, and handles `stop`, `cancel`, `pause` and `resume` at once. It
  imports only `./_framework/dotnet.js`, `blob-read.js`, `blob-write.js` and `pause.js`: nothing of the page's DOM. The
  package uses it as it is, the same file the page and the checks use, so the package and the page can't drift.
- The exports the package needs: `Open(file)`, `Detect(optionsJson)`, `Stop()`, `Cancel()`, `ScenesCsv()`,
  `StatsCsv()`, `ShotsJson()`, `ShotsHtml()`, `TimelineFile(format, name)`, `YouTubeChapters(minimum)`, `Packets()`
  (an AVI's stills), `Versions()`. Their names and JSON are the page's internals and change with the page: the
  package's own module is the stable API over them, and both ship from one commit, so an export can change as long as
  the module follows in the same pull request (its check, below, proves it).
- Not used in a first version: a split run across workers (`Ranges`, `ScoreRange`, `Merge`), resuming (`Kept`,
  `Resume`), the sound (`Audio`), the picture (`Picture`, `FrameTypes`). Each can be added later without changing
  what is there. The threaded bundle is used, opt-in (Answers, 2): `threads.js`' `ThreadedRunner` speaks the same
  messages on the page's thread, with `Api.Threads.cs`' task twins.

## The public API

One ES module, `shotdetector-web`:

```js
import { detectShots, ShotDetector } from 'shotdetector-web';

// One call: a worker started, the file detected, the worker ended.
const run = detectShots(file, {
  detector: 'adaptive',          // Detect's options as the page passes them: detector, threshold, minSceneLength,
  threshold: null,               // frameWindow, minContentVal, deinterlace, or detectors; null or left out:
                                 // ShotDetector's default
  stills: 'middle',              // 'middle' | 'first' | false: a still per shot, the browser's decode (below)
  stillWidth: 320,
  outputs: ['scenesCsv'],        // more of the run to fetch before the worker ends: 'scenesCsv', 'statsCsv',
                                 // 'shotsHtml', 'edl', 'fcpxml', 'fcp7', 'otio', 'qp', 'keyframes',
                                 // 'shotchanges', 'chapters'
  onProgress: ({ done, expected }) => {},
  onShot: shot => {},            // each shot as soon as ShotDetector knows it
  signal,                        // an AbortSignal: cancel, the promise rejects with an AbortError
});
run.stop();                      // the result for the frames read so far, stopped: true
run.pause(); run.resume();
const { video, shots, stills, frames, duration, stopped, warnings, files } = await run;

// A detector kept for more than one run on the same file (its outputs asked for afterwards).
const detector = new ShotDetector({
  base,                          // where the package's files are served (below)
  bundle: 'aot',                 // 'aot' (the default) | 'interpreted': the bundle loaded (Answers, 1)
  threads: 0,                    // decoder threads, opt-in: more than 1 only where crossOriginIsolated (Answers, 2)
});
const video = await detector.open(file);      // Open's description: size, rate, frame count, codec, audio
const result = await detector.detect(options); // the same run handle as detectShots'
const csv = await detector.scenesCsv();       // and statsCsv() (a Blob), shotsJson(), shotsHtml(),
                                              // timeline(format), chapters(minimum)
detector.close();                             // the worker ended
```

- **`shots`** are `ShotsJson()`'s array, ShotDetector's `Shots.Json` (each shot's start and end as frame, seconds and
  timecode): the exact output. `video` is `Open`'s JSON, `warnings` ShotDetector's sentences, `stopped` true after a
  stop.
- **`stills`** are a `Blob` (`image/jpeg`) per shot, or none. They are the browser's decode, not ShotDetector's frames,
  taken as the page's shots table takes them (`panels/shots.js`): a hidden `<video>` of the file seeked to the shot's
  middle (or first) frame, drawn to a canvas; an AVI's from its own JPEG at that time (`Packets()`' offsets, as
  `app.js`'s `frameAt`). They need a document (`<video>`, canvas), so they are taken on the page's thread after the
  run, one seek at a time. A browser that can't play the file (an AVI in a browser without MJPEG, a codec it lacks)
  gives `null` for that still, not an error.
- **Errors** are `Error`s with the core's message: a file out of scope is refused with what it is (`Mp4Input`'s and
  `AviInput`'s words), as on the page.
- **Pausing, stopping, cancelling** are `worker.js`' messages: `stop` and `cancel` behave as on the page (README
  "Stopping a run"). A worker that doesn't answer a stop within a few seconds is terminated and the run rejects, as
  `app.js` does.
- `bundle` and `threads` are options of `detectShots` too. `threads` asked for on a page that isn't cross-origin
  isolated falls back to the worker, and the result says so (`threads: 1`), never an error: the site's headers are
  its own business. On a run on threads the stills, stop, pause and cancel work the same.
- The module never touches the host page's DOM except the hidden `<video>` and canvas for stills, which it removes.

## Package layout

```
shotdetector-web/
  package.json            "type": "module", "exports": { ".": "./index.js" }
  index.js, index.d.ts    the API above, and its types
  aot/                    worker.js, blob-read.js, blob-write.js, pause.js (the App's own files, copied from its
                          publish) and _framework/ (the AOT bundle: dotnet.js, dotnet.native.wasm, the assemblies)
  interpreted/            the same files, _framework/ the interpreted bundle
  threads.js, pause.js, blob-write.js   the page's threaded runner and its imports, as they are (built: at the root,
                          where threads.js finds ./threads/_framework/ and its imports without a change)
  threads/_framework/     the threaded AOT bundle
  licenses/               FastYuv's COPYING.LGPL, THIRD-PARTY-NOTICES.md, LICENSE
  README.md               use, serving the files, the size, the licence and FastYuv's rebuild recipe
```

It is put together from the App's publish by a script (`tools/package.sh`), not written by hand: `index.js`,
`index.d.ts`, `package.json` and the README live in `package/` in this repository, the rest is copied.

**Serving the files.** `index.js` starts its worker with `new Worker(new URL('./worker.js', import.meta.url),
{ type: 'module' })`, the form Vite, webpack 5, Rollup and esbuild recognise, and `dotnet.js` finds `_framework/`
next to itself. A bundler that rewrites `worker.js` would also try to bundle `dotnet.js` and the `.wasm` files it
fetches at run time, so the package says how not to: the files are served as they are, and `base` (an option of
`ShotDetector` and `detectShots`) names where, e.g. `/vendor/shotdetector-web/` after a copy of the package's folder.
From a CDN (jsDelivr, unpkg) a worker can't be started cross-origin; `base` pointing there makes `index.js` start the
worker from a same-origin `blob:` module that imports the CDN's `worker.js`.

**Interpreted and AOT** both ship (Answers, 1), each in a folder of its own with its own copy of `worker.js`, which
imports `./_framework/dotnet.js` relative to itself and so needs no change; a site loads only the one it picks. The
threaded bundle ships AOT only: threads are for speed, which the interpreted bundle has already traded for size. The
tarball on npm holds all three (about 40 MB raw, 14 MB packed); a site serves only the folder it uses.

## The bundle's size

`_framework/` of the App's publish, 2026-11-02, each file compressed on its own (as a server sends it): gzip `-6`
(Pages' and most servers' default) and `-9`, brotli `-q 11` (a CDN's or a precompressed `.br`).

| Bundle | Raw | gzip -6 | gzip -9 | brotli -q 11 | of which `dotnet.native.wasm`, brotli |
| --- | --- | --- | --- | --- | --- |
| Interpreted | 5.33 MB | 2.05 MB | 2.05 MB | 1.71 MB | 0.47 MB (1.49 MB raw) |
| AOT | 17.15 MB | 5.39 MB | 5.36 MB | 3.81 MB | 2.87 MB (13.31 MB raw) |

29 files each (MB here is 10⁶ bytes). `ShotDetector.FastYuv.wasm` is 40 KB raw in both. AOT costs 3.3 MB more through gzip,
2.1 MB more through brotli, all of it in `dotnet.native.wasm`, the assemblies' compiled code. In return a run is about 4
to 5 times faster (decision 2026-10-24-fastyuv-replaceable.md: a 720p sample in 4.5 s interpreted, 1.0 s AOT).
The threaded AOT bundle (Answers, 2) adds about as much as the AOT one again (README "Size": +3%, a second copy).

For comparison: a typical upload page's own JavaScript is a few hundred KB; a site that loads the package loads it
only when a video is picked (a dynamic `import()`), and the browser caches it.

## FastYuv's LGPL

FastYuv stays its own assembly in the package, as on the page: `_framework/ShotDetector.FastYuv.wasm`, its
`COPYING.LGPL` in `licenses/`, THIRD-PARTY-NOTICES.md naming it. What makes it replaceable is the same as the page's
(decision [2026-10-24-fastyuv-replaceable.md](2026-10-24-fastyuv-replaceable.md), choice 1): the published file can't be
swapped alone (trimming, `dotnet.js`' hashes, and under AOT its code compiled into `dotnet.native.wasm`), so the
package is rebuilt from source with a changed FastYuv:

```
tools/package.sh -p:FastYuvAssembly=<your FastYuv.dll>   # its three bundles published against it, then packed
```

The package's README gives the recipe, the source of this repository and of FastYuv at the package's version, and
says that a site using the package must pass the same on: its visitors receive FastYuv (LGPL-2.1 section 6), so the
site names it, ships its licence (the package's `licenses/` served with it) and points at the recipe. The package
check (below) runs `tools/check.sh replace`'s changed FastYuv through the packed package as well, so the recipe is
proven for the package and not only for the page.

## Cross-origin isolation

Only threads need it: the threaded runtime refuses to start without `SharedArrayBuffer` (decision
[2026-10-30-threads.md](2026-10-30-threads.md)), and it must run on the page's main thread. A site would have to send
COOP/COEP on its own pages (or register a service worker that adds them, as this page does), which breaks embeds of
other origins it may have (an ad, a video player, an OAuth popup): too much to ask of an upload form. The
single-threaded bundle in a worker needs nothing: no headers, any page. So threads are opt-in (Answers, 2): the
default is the worker, and `threads: n` uses the threaded bundle on the page's thread only where `crossOriginIsolated`
is true. The package's README says what a site sets (COOP `same-origin`, COEP `require-corp` or `credentialless`) and
what that costs it; the package never registers a service worker of its own on someone else's site.

## Versioning, publishing and CI

- **Version**: the package's own semver, from 0.1.0, independent of ShotDetector's (the API is this repository's);
  0.x while the API settles, a breaking API change a minor then, a major after 1.0. A pull request that changes the
  package's files or bumps a package in `Directory.Packages.props` bumps it (a bump of ShotDetector can change shots:
  a minor). `package.json`'s `shotdetector` field lists the versions inside (`Versions()`), and the README says them.
- **Where**: npm, unscoped, as `shotdetector-web` (free on 2026-11-02: `npm view shotdetector-web` has no package),
  under the owner's npm account (Answers, 4).
- **CI**: `tools/check.sh package` (a new mode): the App published, the package packed with `npm pack`, and a test
  page in `tools/page-check/` that installs the tarball and imports it as a site would, in Chromium: each bundle
  (AOT, interpreted, and threads on an isolated test page, and asked for on one that isn't: the worker): `detectShots` on
  the samples the page check uses (a 4:2:0 MP4, an AVI, a rotated one), its `shots`, `scenesCsv` and `statsCsv` the
  native run's byte for byte, a run stopped and one cancelled, a still per shot, nothing in the host page left
  behind; the changed FastYuv through the package (above). `tools/ci-scope.sh` runs it for `package/`,
  `tools/package.sh` and anything the page check runs for. Publishing: a workflow on a tag `package-v<version>`, the
  same checks as `pages.yml` (native, AOT with the page, replace, package), then `npm publish --provenance` through npm's
  trusted publishing (OIDC from the workflow, no token in the repository), only if all of it passes.

## Questions

Put to the owner on 2026-11-02, the recommended choice first:

1. **Which bundle?** AOT only (one bundle, as the page; recommended); both, the site picks; interpreted only.
2. **Threads?** None in a first version (no headers asked of any site; recommended); opt-in, used only where the page
   is cross-origin isolated.
3. **What beyond the module?** The module only (`detectShots`, `ShotDetector`; recommended); also a `<shot-detector>`
   element; also long videos split across workers.
4. **Name and publishing?** `shotdetector-web` on npm, published by CI on a tag through trusted publishing
   (recommended); `@bisforboman/shotdetector-web`, the same way; published by hand, CI only packing and checking.

## Answers

The owner, 2026-11-02:

1. **Both, the site picks**: `bundle: 'aot' | 'interpreted'`, AOT the default (the page's choice for speed), the
   interpreted bundle for a site that would rather download 1.7 MB than 3.8 (brotli).
2. **Opt-in threads**: `threads: n`, the threaded AOT bundle on the page's thread where `crossOriginIsolated`, the
   worker otherwise.
3. **The module only**: no `<shot-detector>` element and no split runs in the first version; each is a row of its own
   if asked for (the `component` export is left out of `package.json` until then).
4. **`shotdetector-web` on npm, published by CI**: `npm.yml` on a tag `package-v<version>`, after the checks, npm's
   trusted publishing with provenance. The owner creates the package on npmjs.com and adds the repository and
   workflow as its trusted publisher before the first release (a step only they can take).

Built by the backlog row "The npm package" (Next).
