# Backlog

What shotdetector-web does next, in priority order. Three sections: **Next** (open work, the first row first),
**Waiting** (rows that wait on the owner or an outside event) and **Maybe** (ideas not committed to). Update a
status when work starts or lands; add a row when a gap turns up, with why it matters, in Next or Maybe by its status.
A row moves to [backlog-done.md](backlog-done.md) when it is Done, Dropped or Won't do. Design questions go to
[decisions/](decisions/); the page's design is in
[decisions/2026-10-24-the-page.md](decisions/2026-10-24-the-page.md), which every row below assumes.

Each row is meant to be picked up cold, by an agent in a session of its own, as one pull request against `main` that
CI proves: **Why** it matters, **Where** it touches, **Done when** (what the PR shows, and which check proves it),
**Not** (what the row must leave alone), **Status**. Rows that depend on another say so; the rest can run in parallel.
Before pushing: `tools/check.sh` (README, Checks); README and CLAUDE.md say what changed. Commit messages: what and
why, small commits.

Status: **Open**, **In progress**, **Maybe**; finished rows (in backlog-done.md): **Done** (on `main`), **Dropped**,
**Won't do**.

## Next

### First release: the page on GitHub Pages

- Bundle size
  - Why: a .NET browser-wasm publish is tens of megabytes raw; a visitor on a phone gives up. Decision "Delivery".
  - Where: measure the published `_framework/` (raw, and transferred from the live site: does Pages gzip
    `application/wasm` and `.dat`?); `InvariantGlobalization` on the App (ICU is large; the CSVs are invariant
    already, `tools/check.sh wasm` proves it); if Pages doesn't compress, pre-compressed `.gz` next to each file and a
    `withResourceLoader` in `worker.js` that fetches the `.gz` and inflates it with `DecompressionStream('gzip')`;
    note what FFmpeg.AutoGen and FrameReader cost (the trimming row). README: the sizes, raw and transferred, with the
    date. Depends on the deploy row (the live numbers).
  - Done when: first load at most 8 MB transferred, measured from the live site and written in the README; checks
    green.
  - Not: no change of what is bundled (that is the trimming row); no brotli library.
  - Status: Open

- The demo clip
  - Why: the owner wants a visitor to try it without a file of their own (2026-10-24). Decision "The demo clip".
  - Where: `pages.yml` downloads the original from download.blender.org pinned by SHA-256 (as `ci.yml`) and runs the
    recipe of `tools/make-samples.sh` (`SINTEL=`), and puts its `samples/sintel_trailer-480p.mp4` next to the page as
    `demo/sintel-trailer.mp4`; `index.html` gets "Try the demo" which fetches it and runs it as a picked file, and a
    credit line with the license (Sintel's trailer, Blender Foundation, CC-BY 3.0). Depends on the Sintel trailer
    sample, the page and deploy rows.
  - Done when: the live page runs the demo to the same scenes CSV the native check writes for the same file in CI.
  - Not: no clip committed to git (if the download disappears, the row to commit the re-encoded file is written
    then); no change to the recipe (the Sintel trailer sample row).
  - Status: Open

### After the first release

- Real-browser smoke test
  - Why: the page is JavaScript around checked exports; once visitors use it, a change to `app.js` or `worker.js`
    needs a check of its own. Decision "Testing the page".
  - Where: a CI job (Linux, Playwright, Chromium only) that serves the published `wwwroot`, loads the page, sets the
    file input to one sample, runs with the defaults, downloads the scenes CSV and compares it with `out/native/`'s;
    `tools/page-check.mjs` or similar, run by `tools/check.sh page`. Pin Playwright's version.
  - Done when: the job fails when `app.js` breaks the download and passes on `main`.
  - Not: no cross-browser matrix, no visual tests.
  - Status: Open

- Large files: stream from the file instead of copying it into memory
  - Why: the page refuses files above 512 MB; a feature-length MP4 is bigger. `Mp4Input` takes a `Func<Stream>`, and
    workers have `FileReaderSync`, so a stream that reads slices of the `File` synchronously needs no change to the
    core. Decision "The file in memory".
  - Where: `src/ShotDetectorWeb.App`: a `BlobStream` (`Stream` over `[JSImport]` slice reads, a buffer of a megabyte
    or so, seekable) and `Open` taking a handle to the file rather than its bytes; `worker.js` and `app.js` pass the
    `File`; drop the size limit; `tools/app-check.mjs` reads samples through the same path under Node (a `Blob` from
    the file). Measure peak memory on a two-hour file.
  - Done when: a 2 GB MP4 runs in Chrome without the heap growing with the file; checks green.
  - Not: no change to how the frames are decoded.
  - Status: Open

- Thumbnails per shot
  - Why: a table of times is hard to read; a still per shot shows what was found. Decision "Pictures of shots".
  - Where: `app.js`: for each shot, seek the `<video>` to the shot's first frame, `drawImage` to a small canvas,
    lazily as rows scroll into view (one seek at a time; `seeked` events).
  - Done when: a trailer's shots each show a still within a few seconds of the run finishing, and the detection
    result is unchanged (the stills are the browser's decode, not the library's).
  - Not: no second decode in .NET, no image export.
  - Status: Open

- Trimming: FFmpeg.AutoGen and the unused parts of ShotDetector out of the bundle
  - Why: nothing on this path uses FFmpeg.AutoGen, yet it ships (the trimmer keeps whole assemblies that don't opt in:
    ShotDetector's package isn't marked `IsTrimmable`), and THIRD-PARTY-NOTICES.md has to list it.
  - Where: measure what it costs (the bundle size row's numbers); try `TrimMode=full` on the App and see what breaks
    (`Shots.Json` uses `JsonSerializer`: reflection-based serialization needs a source-generated context or
    `JsonSerializerIsReflectionEnabledByDefault`); otherwise an upstream change in shotdetector (`IsTrimmable`, the
    trim warnings fixed) and then this row. `tools/check.sh wasm aot` is the proof either way; the
    "FFmpeg not loaded" check turns into "FFmpeg not bundled". Update THIRD-PARTY-NOTICES.md.
  - Done when: `_framework/` has no `FFmpeg.AutoGen.wasm`, the checks are green, and the README's sizes are updated.
  - Not: no change to the core library.
  - Status: Open

## Waiting

- ManagedCodecs' license
  - Why: THIRD-PARTY-NOTICES.md says the submodule's pinned commit has no LICENSE file; the page ships its code to
    every visitor and should say under what terms.
  - Where: upstream (bisforboman/ManagedCodecs): a LICENSE file; then here: bump the submodule, update
    THIRD-PARTY-NOTICES.md and the page's footer.
  - Status: Waiting on the owner (2026-10-24)

- ManagedCodecs from NuGet instead of the submodule
  - Why: a package pins a version in Directory.Packages.props like the others, and a clone needs no `--recursive`.
  - Where: when it is on nuget.org: replace the two project references in the core csproj with a package reference,
    delete `external/` and `.gitmodules`, update README "Packages" and CLAUDE.md, `ci.yml`'s `submodules: true`.
  - Status: Waiting on upstream (2026-10-24: not on NuGet)

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`, 3.14.0) takes 8-bit 4:2:0 of an even
height at a constant frame rate, upright, and refuses `Deinterlace` and `Yadif`: most rows below need a change there
first, named in each. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- MJPEG in AVI
  - Why: ManagedCodecs' MJPEG decoder is bit-exact with FFmpeg and its AVI demuxer gives FFmpeg's timestamps; 4:2:0
    JPEG comes out yuv420p/yuvj420p, which the frame-source API takes today. Cameras and old footage.
  - Where: an `AviInput` beside `Mp4Input` describing the file as FFmpeg does (frame rate from the AVI header, no
    time base subtleties); `Demuxer.Open` to pick by content; the page's file filter. 4:2:2 JPEG waits for the 4:2:2
    row.
  - Status: Maybe

- Interlaced H.264
  - Why: broadcast material. ManagedCodecs decodes fields and MBAFF; ShotDetector.FastYuv has the row-wise `Yadif`
    the FFmpeg path uses in-process.
  - Where: first upstream: the frame-source API accepting `Yadif` (today an `ArgumentException`); then here, the
    `FrameMbsOnly` check in `Mp4Input` goes, a sample with `-flags +ilme+ildct`.
  - Status: Maybe (needs ShotDetector)

- 10-bit 4:2:0 and 8-bit 4:2:2
  - Why: ManagedCodecs decodes both; FastYuv converts both (`SwscaleYuv420P10`, its 4:2:2 path) with x86's results.
  - Where: first upstream: `YuvFrame` for 16-bit planes and 4:2:2 chroma, and `YuvVideo.PixelFormat` accepting
    yuv420p10le / yuv422p; then here, the chroma and bit-depth check in `Mp4Input`, two samples (already in
    `samples/reject/`).
  - Status: Maybe (needs ShotDetector)

- Fragmented MP4, variable frame rate, rotation
  - Why: screen recordings and phones. ManagedCodecs demuxes fragmented files; FFmpeg's `nb_frames` is 0 for them, so
    the description differs (`Mp4Header` counts `moof` samples, `FrameCount` null, the length from the duration as
    OpenCV's). A variable rate is an error in the frame-source API (a frame more than half a frame off) and in
    scenedetect's model. Rotation: FFmpeg's path reports it and leaves frames upright; the frame-source API applies
    none.
  - Where: each its own row when wanted; each needs a decision about what "the FFmpeg path's result" means for it.
  - Status: Maybe

- Threads in the browser
  - Why: ManagedCodecs decodes a 720p P chain in about half the time on 4 threads (`H264Decoder.Threads`). Wasm
    threads need `SharedArrayBuffer`, so COOP/COEP headers, which Pages can't set; a service worker that adds them to
    its own responses (the `coi-serviceworker` trick) can. `DetectionMode.Parallel` doesn't apply to a frame source
    (it reads once, in order), so only decoding would speed up.
  - Where: the service worker, `WasmEnableThreads`, `Threads` set from `navigator.hardwareConcurrency`, the check that
    results don't change (`tools/check.sh wasm aot`). Decision "Threads".
  - Status: Maybe

- Offline (a service worker caching the bundle)
  - Why: once a service worker exists for threads, caching the bundle is a few lines and the page works on a plane.
  - Status: Maybe

- Shots as JSON and HTML
  - Why: `Shots.Json` and `Shots.Html` exist; two more download buttons.
  - Status: Maybe

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output differs between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Won't do (kept here so it isn't proposed again)
