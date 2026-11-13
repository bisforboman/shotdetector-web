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

The first release's rows are all done ([backlog-done.md](backlog-done.md)).

### After the first release

The rows below add to what the page shows (the owner's choice, 2026-10-24). They widen the decision's "The page
itself", which lists the first release's page, not every page there will be. Since the page's structure row
(decision [2026-10-25-one-page.md](decisions/2026-10-25-one-page.md)), each is a lane of the timeline or a card, in a
module of its own, so they run side by side once that row has landed. Where a row says "under the player" or "on the
shots strip", read a lane of the timeline. The waveform, audio measures and bitrate rows keep only their export and
panel; their core parts are "Audio in the core" under "Now" and "Packets in the core" (done). Whatever the page
derives for display is labelled as the page's own; the CSVs, the shots as JSON and HTML and the editors' timeline files
stay the only output the exactness check speaks for.

### Performance

The performance pass's rows are all done ([backlog-done.md](backlog-done.md)).

### ManagedCodecs 0.5.0

The owner chose on 2026-11-01 to adopt ManagedCodecs 0.5.0 in one session, a pull request each, in this order: the
bump, then "Each frame's own colour tags" (Maybe), then "The first pts of a track whose edit list starts at 0"
(Waiting, ManagedCodecs#252, fixed in 0.5.0). All three are done (backlog-done).

### ShotDetector 3.25.0 and FrameReader 2.19.0

The owner asked on 2026-11-01 to move the page to the packages released that day: the bump, then deinterlaced runs
split and resumed, the picture's measures and the silences from FrameReader, and odd heights, all done
([backlog-done.md](backlog-done.md)). Left for later, in Maybe: the reference tone left out of the detection, and
correcting the cuts on the page.

### ShotDetector 3.26.0 and FrameReader 2.20.0

The owner's queue of 2026-11-01: the bump first (done, #124), then freeze spans on the page (done): both in
[backlog-done.md](backlog-done.md).

### The files the page refuses (issue #89)

The owner's choice of 2026-11-02 ([decisions/2026-11-02-browser-decoder.md](decisions/2026-11-02-browser-decoder.md)):
a refusal that says what to do first, then VP9 and AV1 through the browser's decoder, held to the FFmpeg path's CSVs.
Both rows are done (backlog-done, #133 and #135); what they left is in Maybe ("WebM runs split, resumed and on
threads", "WebM without a DefaultDuration or a Duration, and VP9 or AV1 in MP4", "WebM on ManagedCodecs' Matroska demuxer").

### The npm package (issue #92)

Built (backlog-done, "The npm package"); its first publish waits on the owner (Waiting, "The npm package's first
publish").

## Waiting

- A stop in the leading reference tone, exactly (upstream)
  - Why: a run leaving the reference tone out has no frames of its own before the leading tone ends, and
    ShotDetector's `Merge` refuses a run stopped there ("StartTime is after the last frame"), as `Detect(path)` refuses a
    file that ends in its tone. The page holds a Stop until two frames past the tone's end (`Api`'s `StopsFrom`), an
    estimate of where ShotDetector starts the run (`ShotDetection.ToneStart`, internal).
  - Where: ShotDetector's `ToneStart` public, or `Merge` giving a run stopped before its start (no shots); then `Api`'s
    `StopsFrom` on it, or the hold dropped.
  - Done when: app-check's tone run stopped in the leading tone gives what ShotDetector says, with no estimate.
  - Status: Waiting (2026-11-09): asked of the shotdetector coordinator (the owner's choice through AskUserQuestion);
    the owner chose a public `ShotDetection.ToneStart(YuvVideo, DetectionOptions?)` (shotdetector#770, its decision
    2026-11-09-tone-start-public.md; `Merge` unchanged: a run stopped one frame past it merges), in the next ShotDetector
    release: then `StopsFrom` is `ToneStart` + 1

- The npm package's first publish
  - Why: the package is built and checked, and CI runs `npm publish --dry-run` (backlog-done, "The npm package"), but
    npm's trusted publishing needs the package to exist on npmjs.com with this repository's `npm.yml` as its trusted
    publisher, which only the owner can set up.
  - Where: npmjs.com (the owner); then a tag `package-v0.1.0` on `main`, which `npm.yml` checks and publishes; issue #92
    closed after.
  - Done when: `npm view shotdetector-web` shows 0.1.0 with provenance.
  - Status: Waiting (2026-11-02): the owner's npm setup

- The threaded runtime deadlocks the page's main thread, now and then (upstream: .NET's emscripten)
  - Why: a run on threads sometimes never answers: the page's main thread spins at 100% for good, answering nothing
    (not even DevTools). Caught on 2026-11-13 (a cloud session, Chromium 153, the interpreted bundle, .NET 10.0.15): 4
    in about 330 pages of page-check's steps, each at a fresh page's first threaded call (`Open`), a WebM or not. Its
    wasm stack (gdb along V8's frame pointers, named from `--perf-basic-prof` and the build's
    `dotnet.native.js.symbols`): a `[JSImport]`'s promise settles, the runtime posts the task's completion to a .NET
    thread (`mono_wasm_invoke_jsexport_async_post`, `emscripten_proxy_async`), `do_proxy` holds the proxying queue's
    mutex while `get_or_add_tasks_for_thread` reallocates for a thread it hasn't posted to yet, `dlmalloc` finds its
    lock taken, and the main thread's futex wait (a spin: the main thread can't block) yields into
    `emscripten_proxy_execute_queue`, which locks the same mutex: the thread waits for itself. Emscripten fixed it
    (emscripten-core/emscripten#24565, in 4.0.11; #28186, 2026-11-10); .NET 10's runtime is built with emscripten
    3.1.56. It looks like dotnet/runtime#106788 (open, "Awaiting a `Task` returned by a `[JSImport]` method sometimes
    causes a deadlock"). Not the page's code, and not the WebM decoder's imports (`JsDecoder`). The row's earlier
    "result that doesn't come" was the page's own (a resumed run built its card twice: backlog-done). Not seen here:
    the unaligned-access trap (WebAssembly's trap for an atomic at an unaligned address; also open in .NET's own tests,
    dotnet/runtime#96191) and the renderer crash, both seen on 2026-11-04 on Playwright's own Chromium, which this
    session's network couldn't fetch (cdn.playwright.dev).
  - Where: upstream (the owner posts the cause on dotnet/runtime#106788); then the .NET bump that builds its runtime
    with emscripten's fix. Nothing to work around here: it is the runtime's first post to each new thread.
  - Done when: a .NET runtime pack with emscripten's fix, and page-check's steps on threads in a loop without a hang.
  - Status: Waiting (2026-11-13): the owner's post upstream, then a .NET release with the fix

The asks filed upstream on 2026-10-24 all shipped on 2026-10-25:
[shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492) and
[#493](https://github.com/bisforboman/shotdetector/issues/493) in ShotDetector 3.15.0 (`DetectAsync` over an
asynchronous frame source, `DetectionOptions.OnShot`), [#494](https://github.com/bisforboman/shotdetector/issues/494)
in FrameReader 2.11.0 (`ReferenceToneFinder`, `WaveformBuilder`); those filed on 2026-10-26 shipped in ShotDetector
3.20.0 and ManagedCodecs 0.1.0 (2026-10-27 and 2026-10-28). Their rows are done ([backlog-done.md](backlog-done.md)), and
ManagedCodecs#138's optional part too, in ManagedCodecs 0.4.0 (#211, 2026-10-30). Those filed on 2026-10-30,
[shotdetector#597](https://github.com/bisforboman/shotdetector/issues/597) and
[#598](https://github.com/bisforboman/shotdetector/issues/598), shipped in ShotDetector 3.22.0 the same day: their rows
are in Next again. [#608](https://github.com/bisforboman/shotdetector/issues/608),
[#609](https://github.com/bisforboman/shotdetector/issues/609) and
[#614](https://github.com/bisforboman/shotdetector/issues/614) shipped in ShotDetector 3.23.0 (2026-10-31), and
ManagedCodecs#225 in 0.4.0 (#231, each frame's interlacing): the rows they waited on, worked by one session one after
another, a pull request each (the owner's choice, 2026-10-31): rotation, the variable frame rate and deinterlacing,
all done. ShotDetector 3.24.0 and FrameReader 2.18.0 (2026-10-31) shipped
[#625](https://github.com/bisforboman/shotdetector/issues/625) (a frame of another size refused),
[#626](https://github.com/bisforboman/shotdetector/issues/626) (each frame's own colour tags),
[#627](https://github.com/bisforboman/shotdetector/issues/627) (10-bit 4:2:0 and 8-bit 4:2:2),
[#633](https://github.com/bisforboman/shotdetector/issues/633) (Subtitle Edit's shot changes),
[#648](https://github.com/bisforboman/shotdetector/issues/648) (YouTube chapters) and FrameReader's `LoudnessMeter`
([#646](https://github.com/bisforboman/shotdetector/issues/646)): the bump is done (backlog-done), and the rows below
that waited on them are open. ShotDetector 3.25.0 and FrameReader 2.19.0 (2026-11-01) shipped
[#628](https://github.com/bisforboman/shotdetector/issues/628) (ranges that deinterlace, `ShotDetection.RangeFrames`),
[#647](https://github.com/bisforboman/shotdetector/issues/647) (`LoadScenes` over a `YuvVideo`), odd heights, and
FrameReader's `PictureMeter` and `SilenceFinder` (the page's measures moved upstream, the owner's decision 2026-10-31
in shotdetector's docs/decisions/): their rows are in Next, under "ShotDetector 3.25.0 and FrameReader 2.19.0".

## Maybe

Widening the scope. ShotDetector's frame-source API (`Detect(YuvVideo, ...)`) takes 8-bit 4:2:0 (of an even height until
3.25.0); since 3.23.0 also rotated, at a variable rate (`FrameTimestamps`) and deinterlaced (`Yadif`), not yet in ranges
(shotdetector#628); since 3.24.0 10-bit 4:2:0 and 8-bit 4:2:2 (#627, in scope here: backlog-done) and each frame's own colour tags (#626),
a frame of another size refused (#625). What it still lacks is in its `docs/frame-source-parity.md` (issues #628 to #630): a
row that needs a change there names it. Every widening adds a sample that isolates it to `tools/make-samples.sh`, moves the matching
file out of `samples/reject/`, and passes the native check.

- WebM on ManagedCodecs' Matroska demuxer
  - Why: `MatroskaReader` is the core's own minimal reader (the owner's choice, 2026-11-02), until ManagedCodecs had a
    Matroska demuxer; 0.8.0 has one (`MatroskaDemuxer`, with `Ebml`), which the core doesn't use yet.
  - Where: a decision first (replacing the core's reader, against the 2026-11-02 choice), then `WebmInput` on the
    demuxer, held to the same CSVs on every WebM sample.
  - Status: Maybe (2026-11-04: ManagedCodecs 0.8.0 on nuget.org)

- A Cinemetrics file
  - Why: issue #91 asked for the shot lengths in a format Cinemetrics' database or tools take. Its current tool's
    `.cmsx` has no published format, and the classic `.cms` is described only by third-party code (README, "Shot
    lengths for film studies"). The page offers a plain shot-lengths CSV instead.
  - Where: a file saved by Cinemetrics' tool, from the owner or a scholar, to check against; then a writer, in
    ShotDetector's `Shots` if the CLI should share it (issue #91's comment), or the shot-lengths card.
  - Done when: a file the page writes opens in Cinemetrics' tool with the run's shots.
  - Status: Maybe (2026-11-02; cinemetrics.uchicago.edu wasn't reachable from the cloud session)
