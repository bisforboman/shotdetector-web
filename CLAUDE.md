# shotdetector-web

Shot detection entirely in the browser: ManagedCodecs decodes H.264 in MP4 and MJPEG and H.264 in AVI, ShotDetector.FastYuv
converts, ShotDetector detects through `ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, ...)`. Repo:
`github.com/bisforboman/shotdetector-web`, MIT. The owner's decisions behind it are in shotdetector's
docs/decisions/2026-10-24-managedcodecs-showcase.md.

## Core idea

Exactness first: the scene and stats CSVs (and the shots as JSON and HTML) must equal ShotDetector's FFmpeg path's
(`Detect(path)`, FFmpeg in-process, same FastYuv converter) byte for byte, natively and on browser-wasm, before anything
else. That rests on `YuvVideo` saying what FFmpeg says about the file (average frame rate, nb_frames, time base, start
pts, colour tags, duration): ManagedCodecs doesn't expose all of it, so `Mp4Header`/`SpsInfo` (and `AviHeader` for an
AVI) read it, and NativeCheck compares every field with FFmpeg's before it compares results. Where the core differs from
the FFmpeg path, find out whether it's the description (fix it here) or a bug in ShotDetector or ManagedCodecs (report
it there, don't work around it).

## Where things are

| Topic | Where |
|---|---|
| What is supported, what is refused | README, Status ("Supported so far") |
| Each core class, export and its JSON, panel, tool and workflow | README, "Code layout" |
| Running the page; stop, pause, resume; workers | README, "Running the page", "Stopping a run" (and its subsections), "Long videos across workers" |
| Threads, the service worker, the page offline, the installed app | README, "Threads, and the page offline", "The installed app" |
| Memory on large files | README, "Large files", "What the heap holds" |
| Each check, what it compares; the real-world files; WebCodecs | README, "Checks", "Real world", "WebCodecs" |
| Packages and their versions; licensing | README, "Packages", "Licensing" |
| The work, finished rows | docs/backlog.md, docs/backlog-done.md |
| Design decisions | docs/decisions/ (their mockups: docs/mockups/) |

## Working on this repository

- **Ask the owner**: at each decision (which row, a design choice, before a PR or a merge), prompt the owner with
  the question tool rather than deciding or asking in prose; the owner's standing instruction, 2026-11-03.
- **One backlog row, one cloud session, one PR** against `main`, CI green. Set the row's status to In progress (with
  the date) when starting, move it to `docs/backlog-done.md` when it lands, add a row when a gap turns up. A design
  question the row doesn't settle goes to `docs/decisions/` first.
- **Merging**: `main` requires CI's `build` check, and auto-merge is on. Once the PR is finished, with nothing left to
  push, `gh pr merge <n> --auto --merge --delete-branch`. Not earlier, or a green run merges half a row.
- **Scope**: README's Status lists the formats; anything else is refused in `Mp4Input`'s, `AviInput`'s or `WebmInput`'s
  constructor with a message saying what (another codec or container, `VideoInput.Open`'s first bytes too, in `Refusal`'s words:
  FFmpeg's name and `Refusal.Scope`, which the page's `fail()` knows a refusal by). Widening it needs a sample for it in `tools/make-samples.sh`, the checks passing on it,
  and its real-world file in `tools/realworld.sh` moved from "refused" to "same" (Elephants Dream for a variable rate).
- **The browser's decoder** (WebM's VP9 and AV1, decision 2026-11-02-webm-browser-decoder.md): the core reads and
  describes the file; only the frames are the browser's (`IHostDecoder`). A WebM sample is checked natively with FFmpeg
  standing in (`FfmpegDecoder`) and in Chromium by page-check (`--browser`); Node has none (app-check only opens it).
  Such a run goes as any (on threads, split, kept to resume): each runtime's `VideoDecoder` from its range's keyframe,
  the threaded runtime's on the main thread (threads.js gives it the decoder imports).
- **Samples**: a new one isolates one thing and is checked to really have it (ffprobe, `-bsf:v trace_headers`). ffmpeg
  tags its test sources BT.601 (bt470bg): an untagged sample needs `setparams=...=unknown`.
- **FastYuv is LGPL-2.1**: it stays its own DLL/.wasm (no single-file output), its COPYING.LGPL is copied into every
  output (the core csproj), and it is replaced by rebuilding with `-p:FastYuvAssembly=<dll>`, not by swapping the
  published file (docs/decisions/2026-10-24-fastyuv-replaceable.md; `tools/check.sh replace aot` in CI).
- **Packages**: versions central in `Directory.Packages.props`, which pin only what a project references directly (a
  transitive one resolves to the lowest its dependant allows; README, "Packages"). app-check.mjs checks the pins.
- Actions pinned by commit SHA with the tag in a comment. Commit messages: what and why; small commits.

### Before pushing

Prerequisites (tools/check.sh's header; README, "Checks"). In a cloud session `.claude/hooks/session-start.sh` sets up
all but the samples: the wasm-tools workload, BtbN's pinned FFmpeg 8.1 (`SHOTDETECTOR_FFMPEG_LIBS`, `LD_LIBRARY_PATH`,
its ffmpeg with libx264 on `PATH`), and the container's Playwright with `PAGE_CHECK_FIREFOX=skip` (it has no Firefox).

- `samples/` from `tools/make-samples.sh` (once; `FFMPEG=<an ffmpeg with libx264>`, `SINTEL=<Sintel's 1080p trailer
  MP4>`).
- `SHOTDETECTOR_FFMPEG_LIBS`: an FFmpeg 8.1 shared build's folder, also on `LD_LIBRARY_PATH` (native).
- Node and the wasm-tools workload, `dotnet workload install wasm-tools` (wasm, page, replace).
- For page: `npm ci` then `npx playwright install chromium firefox` in `tools/page-check`. For replace: git with access
  to github.com.

Run what the change could break, not everything: CI picks the tier from the PR's files (`tools/ci-scope.sh`: none,
native, page or all), and `pages.yml` and the daily run check all of it on `main`. To see the tier CI will pick:
`git diff --name-only origin/main | tools/ci-scope.sh`.

- `tools/check.sh native`: the core, the page's defaults, NativeCheck.
- `tools/check.sh native page`: the page's own files or the smoke test.
- `tools/check.sh wasm`: the App's .NET side, a package or a wasm check; `tools/check.sh wasm aot` for anything that
  could differ under AOT.
- The fast loop: name samples, `tools/check.sh native samples/<one>.mp4`, then `tools/check.sh wasm samples/<one>.mp4`
  (the files to refuse and those without AAC still all run). wasm and page compare with native's results: run it first.
- A new file the classifier doesn't name runs everything; give it a rule in `tools/ci-scope.sh` if it shouldn't.

Results land in `out/`: `out/native/` (the native run's files, ffprobe's packets and frame types, the picture JSON)
and `out/native-report.md`; `out/wasm/` and `out/wasm-stderr.txt`; `out/page/` (with screenshots); `out/realworld/`. `tools/csv-diff.mjs` prints the first rows where two CSVs
differ.

## Layout

- `src/ShotDetectorWeb.Core` (net10.0, AOT-compatible): `VideoInput` (`Mp4Input`, `AviInput`), the description,
  frames, ranges, audio and picture measures, timeline files.
- `src/ShotDetectorWeb.App` (browser-wasm Exe, outside any solution: needs wasm-tools): `Api`, a partial class with one
  file per feature, and `wwwroot/`, the page (no build step, no dependency, native ES modules). `wwwroot/worker.js` owns
  the runtime and calls any export by name. `Api` prefixes ManagedCodecs' `InvalidDataException` for the page (a .NET
  exception's type doesn't reach JavaScript).
- `tools/`: the checks and their scripts; the WebCodecs measurement (`WebCodecsCheck`, `WebCodecsWasm`, `webcodecs/`)
  is in no check or CI. `tools/Shared/Runs.cs` holds the runs NativeCheck makes, which app-check.mjs holds as the App's options JSON: keep the
  two the same.
- `.github/workflows/`: `ci.yml` (PRs by tier, all of it daily on `main`), `pages.yml` (checks, then deploys the
  page), `realworld.yml` (nightly and by hand, never on a PR). `.github/actions/btbn-ffmpeg` is copied from
  shotdetector.

### Adding to the page

- Something over time is a lane, anything else a card: `card(title, { place })`, `place` one of `'details'` (the
  default: the grid of equal cards last), `'run'` (beside the video, under the run's card: the run and what goes with
  it, as the warnings) or `'below'` (under the timeline, full width, as the table).
- `mark`, whose numbers the card shows, one chip on its title: `'exact'` (scenedetect's, byte for byte: the CSVs),
  `'measured'` (the core's measures, checked against FFmpeg within a tolerance), `'core'` (the core's description,
  checked field by field) or `'page'` (worked out on the page), with `markTitle` saying how they are checked. A row of
  another kind says so in its tooltip, never a dotted underline.
- A panel is a module of its own, `wwwroot/panels/<name>.js`, that imports `on`, `call`, `card` (and `frameSeconds` to
  place frames) from `../app.js` and `lane`, `x`, `seek` from `../timeline.js`, plus one `<script type="module">` line
  in `index.html` (the order of those lines is the order of the lanes, and of the cards in each place) and its rules in
  `style.css`.
- A new export is `Api.<Feature>.cs`, a partial file of its own with its own `JsonSerializerContext` if it needs JSON;
  the panel calls it with `call('<Export>', ...)`, and `worker.js` doesn't change.
- An export of the last run's result (as `ShotsJson`, `Picture`) also needs its task twin in `Api.Threads.cs` and its
  name in `threads.js`'s `THREADED` and `RESULTS`: after a run on threads, `call()` asks the threaded runner for it.
- `app.js` changes only for a new event of the page's.

## Gotchas

- `dotnet workload list` can show wasm-tools installed with no packs in `~/.dotnet/packs` (NETSDK1147 on build):
  `dotnet workload install wasm-tools` again installs them.
- ShotDetector's file path reports `DetectedVideo.Duration` from ffprobe's format duration text (six decimals), to the
  nearest tick (not `TimeSpan.FromSeconds(double)`, which truncates 4.56 s to 4.5599999): `Mp4Input` and
  NativeCheck's oracle do the same.
- ManagedCodecs' audio packets start at negative pts (priming) with `SkipSamples`: a stream's start for FFmpeg's
  format duration is pts + skip (in the stream's time base). A fragmented file has no edit list: the priming isn't
  skipped, the first packet decodes to 1024 samples but lasts longer (3072, 3840), and `silencedetect` times its
  silences by the frames' pts, after that gap: `Mp4Audio` moves its silences by the gaps (`silence-fragmented-*`).
- On wasm under Node the runtime prints `MONO_WASM:` notes on stdout; Emscripten's file system doesn't see the host's,
  so files come in as a `Blob` (`Open(JSObject)`, read by `[JSImport] read`) and results go out as JSON strings.
- In the worker each `FileReaderSync` read is a new `ArrayBuffer` that V8 frees late while .NET holds the thread: a
  pass reading the file as fast as it comes grows the renderer with the file (2.37 GB read: 1.2 to 1.4 GB). A run reads
  at the decoder's pace; `Mp4Input`'s duration pass skips the packets' data (`TimingOnlyStream`). A new pass over a
  whole file should do one or the other.
- Node has no synchronous Blob read: `tools/node-host.mjs`'s `FileReaderSync` reads each slice on a worker thread and
  waits with `Atomics.wait`. Its Blobs hold the file in memory: a file-backed one (`fs.openAsBlob`) aborts Node 22 when
  a worker reads its clone ("Entering the V8 API without proper locking").
- The App's `_framework/` keeps unhashed names (`WasmFingerprintAssets` false: `ShotDetector.FastYuv.wasm`), and
  `dotnet publish` adds to an existing publish folder: `tools/check.sh` deletes it first, for its listing check.
- An interpreted publish after an AOT one can reuse the App's `obj/` from the AOT build, whose IL is stripped: the
  runtime aborts at start (`NIY encountered in method System.OutOfMemoryException:.ctor`). `tools/check.sh` deletes
  `src/ShotDetectorWeb.App/obj` and `bin` before each publish; do the same by hand.
- FFmpeg's `ebur128` summary has one decimal, its frame metadata three (peaks as linear amplitudes): `AudioCheck.cs`
  reads the last frame's (`ametadata=print:file=`). A 2 s gap between tones comes
  out 1.9966 s after FFmpeg's AAC encoder, too short for `silencedetect`'s default 2 s (which the page shows): the tone
  sample's gap is 3 s, and the check also runs at 1 s.
- The published bundle is trimmed with `UseSystemResourceKeys` (the wasm SDK's default, not set here): the framework's
  exception messages come out as resource keys (`DeserializeUnableToConvertValue, ...`), the libraries' own
  (ManagedCodecs, `UnsupportedVideoException`) whole.
- Under wasm AOT the runtime loads some assemblies before `Main` that the interpreter never touches. FFmpeg.AutoGen was
  one until ShotDetector 3.22.0 (shotdetector#597): the trimmer now drops it, `tools/check.sh` fails if a bundle has
  `FFmpeg.AutoGen.wasm`, and the wasm check's "no FFmpeg assembly loaded at all" holds under AOT too.
- A `[JSExport]`'s task that ends cancelled (an `OperationCanceledException` out of an `async` method) never settles
  the promise JavaScript holds (.NET 10): `Api.Detect` turns its token's cancellation into a `RunCancelledException`,
  which rejects it.
- A split run's Stop stops every worker at its next pause, each range ending where it got to, and
  `VideoInput.FromStart` keeps the ranges while each reaches into the next one's first frame: the first range stopped
  short ends the merge, as a one-worker run stopped there (keeping only finished ranges would keep nothing for long).
- .NET's threaded wasm runtime (10.0.14) doesn't start in a Web Worker, refuses to start without `SharedArrayBuffer`, and doesn't run under Node; on the main thread it refuses synchronous
  exports (`jsThreadBlockingMode` `PreventSynchronousJSExport`), runs `[JSImport]`s on the main thread (no
  `FileReaderSync` there), and refuses a synchronous one from inside a synchronous export. Hence `Host.cs`. A
  `[JSImport]`'s `ArraySegment` there ended the runtime when the collector released it on another thread: use a `Span`
  view, valid for the call only. ManagedCodecs' `H264Decoder` has no Dispose (its threads end after an idle timeout):
  frames that end early flush it.
- A stopped run ends its last shot at the last frame's time plus a frame at the average rate (the FFmpeg path stopped by
  its end time), not at the frames read over that rate, only because the core gives `YuvVideo.FrameTimestamps` at every
  rate (ShotDetector 3.29.0): without them a stop looks like the video's end. The page asks `Open`'s `variableRate`,
  not whether `frameTimestamps` are there.
- A stopped run is a file that ends there, for the detectors too: the adaptive ratio of the last frames is `None`, a
  cut FlashFilter's merge would decide on frames past the stop isn't there, and the threshold detector ends a fade-out
  the stop falls in with a cut (`PostProcess`), as at a file's end.
- A cut the page handles (`cutFrames`, `detectedCuts()`, the corrections, `Correct`'s starts) is a frame counted from 0
  as decoded (`DetectionResult.CutFrames`), not a shot's `startFrame` − 1 (a time × the average rate, another frame at
  a variable rate). A corrected list is ShotDetector 3.28.0's `LoadScenes(DetectionResult, cutFrames, options)`,
  which keeps the run's frame times and length; `LoadScenes` of a scene list (scenedetect's) times a cut at frame /
  average rate and ends at nb_frames.
- A run leaving the reference tone out (`TrimReferenceTone`, the request's `tone`) reads every frame, but ShotDetector's
  `DetectionResult.FrameCount` counts only those between the tones: `Api`'s answer gives the frames read from 0 (the
  ranges' end) as `frameCount`, which `stopped`, the picture, the damaged frames and the corrections' bounds use. A stop
  before the run's first frame has nothing `Merge` can give (it throws): `Api` holds it until past the leading tone
  (`StopsFrom`, an estimate of ShotDetector's internal `ToneStart`).
