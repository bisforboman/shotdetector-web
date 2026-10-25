# Backlog: done

Rows finished, moved from [backlog.md](backlog.md), newest last.

- Core library and the native check: ManagedCodecs + FastYuv against the FFmpeg path
  - Why: exactness first; before any UI, the scene and stats CSVs must equal ShotDetector's FFmpeg path's.
  - Status: Done (2026-10-24: every sample, all five detectors and all together, byte for byte; tools/NativeCheck)

- WebAssembly check, interpreted and AOT
  - Why: the same results on browser-wasm, and nothing of FFmpeg on the path.
  - Status: Done (2026-10-24: tools/WasmCheck under Node; FFmpeg.AutoGen never loaded interpreted, never loaded by
    the runs under AOT)

- Repository and CI
  - Why: the showcase lives in its own repository with its own CI (shotdetector's decision 2026-10-24).
  - Status: Done (2026-10-24: handed off from shotdetector's `handoff/shotdetector-web` branch; the first CI run on
    `main` green, 9m41s)

- Packages from nuget.org
  - Why: ShotDetector and ShotDetector.FastYuv 3.14.0 are on nuget.org (released 2026-10-24), so the local feed was a
    workaround for a problem that is gone.
  - Status: Done (2026-10-24: the local-feed source, CI's packing step and SHOTDETECTOR_REF dropped; a restore into
    an empty global packages folder takes both from nuget.org)

- The app project: one browser-wasm project with the page's exports, replacing WasmCheck
  - Why: the page needs a browser-wasm project with `[JSExport]`; one project means the exactness check runs the
    page's own entry points (decision "One wasm project").
  - Status: Done (2026-10-24: `src/ShotDetectorWeb.App` with `Open`, `Detect`, `LoadedAssemblies` and `progress`,
    `wwwroot/worker.js`; `tools/app-check.mjs` under Node, 108 of 108 CSVs identical interpreted and AOT; FastYuv's
    `.wasm` and license checked in the publish output; tools/WasmCheck deleted)

- The Sintel trailer sample: a real trailer in the checks, re-encoded as the demo will be
  - Why: a real trailer has what the synthetic samples isolate, together; the page row checks against it, and the
    demo clip is this same re-encoded file, so the exactness check covers what visitors run.
  - Status: Done (2026-10-24: `sintel_trailer-1080p.mp4` from download.blender.org, pinned by SHA-256 in `ci.yml`,
    re-encoded by `tools/make-samples.sh` to 854x480 at 24 fps, 10.1 MB, ffprobe-verified; native and both wasm checks
    equal on it, 120 of 120 CSVs. CI 26m15s, the interpreted wasm step 18m19s of it, the trailer's six runs about
    two minutes each there)

- The page: pick an MP4, detect, see and download the shots
  - Why: the showcase is the page; everything else is around it.
  - Status: Done (2026-10-24: `wwwroot/index.html`, `app.js`, `style.css`; `Api` prefixes a damaged file's message
    (decision "A damaged file's message"). Checked in Chromium here (interpreted) and, through a one-off CI step since
    reverted, in Chromium and Firefox on the AOT publish: every sample with all five detectors gives the native
    check's scenes and stats CSVs, Sintel's trailer included (60 to 77 frames per second); progress moves; cancel
    mid-run, then a second run; each file in `samples/reject/`, a damaged and a 600 MB file show their messages.
    Safari not tried: the owner's, before the demo clip row)

- FastYuv under AOT: is it still its own, replaceable library?
  - Why: the page ships compiled ahead of time, which puts FastYuv's code in `dotnet.native.wasm` too; LGPL-2.1
    (section 6) asks that a user can replace the library.
  - Status: Done (2026-10-24, PR #7: decision [2026-10-24-fastyuv-replaceable.md](decisions/2026-10-24-fastyuv-replaceable.md),
    choice 1, the owner's: AOT kept, FastYuv replaced by rebuilding the App with `-p:FastYuvAssembly=<dll>`; swapping
    the published file alone was tried and fails three ways (integrity hashes, trimmed facades, the AOT image).
    `tools/check.sh replace aot` in CI builds a visibly changed FastYuv from its tag and proves the rebuilt bundle runs
    it; README "Licensing" and THIRD-PARTY-NOTICES.md give the recipe)

- GitHub Pages deploy
  - Why: the page is for visitors.
  - Status: Done (2026-10-24: `.github/workflows/pages.yml`, on CI's success for a push to `main` (`workflow_run`),
    publishes the App ahead of time; the bundle is the publish's `wwwroot/`, to which it adds `.nojekyll` and
    `licenses/ShotDetector.FastYuv/COPYING.LGPL` (the core csproj puts it in the publish root, outside the bundle),
    uploads it with `include-hidden-files` (`upload-pages-artifact` leaves dotfiles out otherwise) and fails before
    `deploy-pages` unless the uploaded archive lists `_framework/ShotDetector.FastYuv.wasm` and the license as regular
    files. The repository's Pages source is "GitHub Actions" (the owner, 2026-10-24). Before the merge, the workflow's
    own steps run here on an AOT publish, served under `/shotdetector-web/`: in headless Chromium the cuts sample and
    Sintel's trailer gave the native check's scenes and stats CSVs, the two LGPL files served; the check fails
    without them. Live (https://bisforboman.github.io/shotdetector-web/), first deployed by Pages run 40108764388
    from `4f964cf` (its check found both LGPL files in the uploaded archive): in headless Chromium the live page ran
    every sample with the adaptive detector, and the cuts sample and Sintel's trailer with the other four, 36 of 36
    downloaded scenes and stats CSVs identical to the native check's (Sintel's trailer in about 20 s, 60 frames per
    second); `licenses/ShotDetector.FastYuv/COPYING.LGPL` is served, the package's own file byte for byte, and
    `_framework/ShotDetector.FastYuv.wasm` as `application/wasm`)

- Bundle size
  - Why: a .NET browser-wasm publish is tens of megabytes raw; a visitor on a phone gives up. Decision "Delivery".
  - Status: Done (2026-10-24: Pages gzips every response itself, `.wasm` and `.dat` included (live, `curl` and
    Chromium), so no pre-compressed `.gz` and no resource loader. First load from the live site, Chromium, empty
    cache: 5.98 MB transferred, 19.13 MB decoded, under the 8 MB target before any change. `InvariantGlobalization`
    on the App drops `icudt_*.dat` (198 KB of that) and shrinks `dotnet.native.wasm` (14.83 to 13.28 MB raw): about
    5.1 MB transferred, 16.96 MB raw, checked by `tools/check.sh wasm` and `wasm aot` (120 of 120 CSVs each).
    FFmpeg.AutoGen 103 KB transferred, FrameReader 26 KB (the trimming row). `CompressionEnabled` false: the
    publish's own `.br`/`.gz` copies, which Pages never serves, are gone (the site 17 MB, not 32). README "Size")

- ShotDetector 3.15.0 and FrameReader 2.11.0
  - Why: 3.15.0 has what the cancel and live-shots rows need (`DetectAsync` over an asynchronous frame source,
    `DetectionOptions.OnShot`), and FrameReader 2.11.0 what the audio rows need (`ReferenceToneFinder`,
    `WaveformBuilder`). 3.15.0 also fixes a duration that was cut to the tick below (4.56 s came out 4.5599999 s),
    which `Mp4Input` copied on purpose.
  - Status: Done (2026-10-25: ShotDetector and FastYuv 3.15.0 and an explicit FrameReader 2.11.0 in
    Directory.Packages.props. 3.15.0 reads ffprobe's six-decimal duration to the nearest tick (`VideoReader.Seconds`,
    `TimeSpan.FromTicks(Math.Round(s * TicksPerSecond))`); with only the packages moved, the native check differed
    on `DetectedVideo.Duration` for full-range and start-offset (4.5599999 vs 4.56), and `Mp4Input` and NativeCheck's
    oracle now do the same. Every scene and stats CSV byte-identical to the 3.14.0 run natively; wasm interpreted and
    AOT 108 of 108 CSVs identical; `replace aot` from `v3.15.0`. The per-frame colour tags fix changes nothing here:
    no sample's tags change midway, and `YuvFrame` carries none)

- Large files: stream from the file instead of copying it into memory
  - Why: the page refused files above 512 MB; the whole file was copied twice, a .NET array stops below 2 GB and wasm
    at 4 GB. Decision "The file in memory".
  - Status: Done (2026-10-25: `BlobStream` in the App reads the picked `File` in 1 MB slices through `[JSImport] read`
    (`wwwroot/blob-read.js`, `FileReaderSync`); `Open(JSObject)`, the worker passes the `File`; the limit and its
    message gone, and the page gives an estimate of the run's time before it starts. `Mp4Input.Open(Func<Stream>)`, and
    its duration pass skips the packets' data (opening a 2.37 GB file: 43 s and a 1.7 GB renderer peak before, 3.4 s
    after). The checks read the samples as Blobs through the published `blob-read.js` (`tools/node-host.mjs`), 120 of
    120 CSVs identical interpreted and AOT. A two-hour, 2,370,979,581-byte MP4 runs in headless Chromium: 46 min at 63
    fps, renderer peak 805 MB, wasm heap 735 MB, the same heap for the same frames in 288 MB (README "Large files";
    the heap per frame is its own row)

- The demo clip
  - Why: the owner wants a visitor to try it without a file of their own (2026-10-24). Decision "The demo clip".
  - Status: Done (2026-10-25: `pages.yml` downloads Sintel's trailer pinned by `ci.yml`'s SHA-256 (the same cache, the
    same BtbN FFmpeg 8.1 build), runs `tools/make-samples.sh` with `SINTEL=` and serves its
    `samples/sintel_trailer-480p.mp4` as `demo/sintel-trailer.mp4`; its site check fails the deploy without the clip,
    and both workflows print the re-encode's SHA-256 in their step summaries. `index.html`'s "Try the demo" fetches it
    and `app.js` hands it to the file path as a `File`; the credit line (Blender Foundation, CC-BY 3.0) is beside the
    button. Before the merge, in headless Chromium on an interpreted publish with the clip in `demo/`: the demo ran
    to 8 shots in 1,253 frames, and with the clip missing it shows "The demo clip didn't download: 404 File not
    found". The live result: added after the first deploy with it)

- Shots as they're found, kept when a run is cancelled
  - Why: on a long file the user waits a long time for the first shot, and a cancel throws all the work away. The
    owner wants the shots found so far shown, and kept after a cancel (2026-10-24). ShotDetector already reports
    each shot once the cut that ends it is confirmed, and since 3.15.0 tells the caller on every `Detect` overload:
    `DetectionOptions.OnShot` ([shotdetector#493](https://github.com/bisforboman/shotdetector/issues/493)), on the
    thread that runs the detection.
  - Status: Done (2026-10-25: `Api` sets `OnShot` to an `IProgress<Shot>` of its own that calls `[JSImport] shot(json)`
    with one object of `Shots.Json`'s array, synchronously on the detection's thread; `worker.js` posts it, `app.js`
    adds the row, and the finished result rebuilds the table from its own shots. `tools/app-check.mjs`: the shots told
    equal the result's on every sample and run, interpreted and AOT; with one detector all but the last are told
    before the last frame, with all five at the end. Cancel still terminates the worker; the rows stay, with one
    italic line marked unfinished from the last shot's end to the frame of the last progress report, and no
    downloads. Headless Chromium on the AOT bundle, the Sintel trailer: rows at frames 333 to 675 of 1253, the
    finished table equal to the native scenes CSV, a cancel at frame 901 leaving shots 1 to 7 and 00:00:28.000 to
    00:00:37.542 unfinished. Firefox wasn't run: the session's network policy blocks Playwright's and Mozilla's
    download hosts)

- Memory per frame
  - Why: the wasm heap grew with the frame count, about 4 KB a frame: 735 MB after a two-hour film at 640x360
    (172,800 frames), the same with the file at an eighth of the bytes.
  - Status: Done (2026-10-25: measured step by step under Node on the AOT bundle and natively (README "What the heap
    holds"): nothing per frame held it, the end of the run did. The CSVs went to JavaScript inside the result's JSON,
    and escaping the 19.8-million-character stats CSV took a 268 MB char buffer and a 67 MB byte buffer that `ArrayPool`
    kept, 337 MB of the heap. `Api.Detect`'s JSON now holds the shots and the numbers, and `ScenesCsv()` and
    `StatsCsv()` return the CSVs as strings: the two-hour film's heap is 432 MB, not 769 MB, under Node, and 432 MB, not
    734 MB, in headless Chromium (renderer peak 788 MB, not 1,043). `Mp4Input` opens with one demuxer instead of two
    (174 MB after opening, not 208). The rest waits upstream: ShotDetector's stats and their CSV, ManagedCodecs' MP4
    index (Waiting))
