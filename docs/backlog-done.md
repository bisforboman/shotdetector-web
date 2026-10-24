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
    FFmpeg.AutoGen 103 KB transferred, FrameReader 26 KB (the trimming row). README "Size")
