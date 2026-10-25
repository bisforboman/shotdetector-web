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

### Now: the page's structure, and the core's audio and packets

The owner asked (2026-10-25) to build the missing pieces at once rather than one by one. These three run side by
side: the first touches only the App, the other two only the core and `tools/NativeCheck`. The panel rows below wait
for the first.

- The page's structure: one timeline, cards, a module per panel
  - Why: every panel row edits `app.js`, `Api.cs` and `worker.js`, so they could only run one at a time, and each
    placed its panel its own way. Decision [2026-10-25-one-page.md](decisions/2026-10-25-one-page.md).
  - Where: `src/ShotDetectorWeb.App`. Split `app.js` into the page's states plus native ES modules: `timeline.js`
    (one time axis under the `<video>`, a playhead that follows `timeupdate`, a click seeks, lanes register
    themselves) and `panels/<name>.js`, one per panel (lanes and cards). Move today's shots table into a panel, and
    add one lane, the cuts as ticks, so the timeline is used from the start. `Api` becomes partial class files (`Api.cs`
    keeps open and detect). `worker.js` calls any export by name. `index.html` gets the layout: the video and the
    timeline, cards beside it, the table below, cards stacked at phone width. CLAUDE.md "Layout" says where a new
    lane, card or export goes.
  - Done when: the page works as it does now (the checks green, CSVs unchanged), plus the timeline with the cuts
    lane and the playhead, in Chromium and Firefox and at phone width (screenshots in the PR).
  - Not: no new panels (their rows); no bundler, no framework, no dependency.
  - Status: Open

- Audio in the core: waveform, reference tone, silence, peaks, loudness
  - Why: the core parts of "A waveform from ManagedCodecs' AAC" and "Audio measures", done now, with the native
    checks, so their page rows only add an export and a panel. Decision [2026-10-24-audio.md](decisions/2026-10-24-audio.md).
  - Where: `src/ShotDetectorWeb.Core`, a file of its own (`Mp4Audio.cs` or similar), a reference to
    `ManagedCodecs.Aac`, and FrameReader 2.11.0's `WaveformBuilder` and `ReferenceToneFinder`. Everything the two
    rows' Where says about the core belongs here: one pass over the audio packets, `AacDecoder`, never holding the
    samples, priming and `SkipSamples` and the edit list, no AAC or no audio handled without an error, the sample with
    the tone and the silence. `tools/NativeCheck` compares the waveform with FrameReader's `WaveformData.Read` on the
    same file, the tone with ShotDetector's `FindReferenceTone`, loudness with FFmpeg's `ebur128=peak=true` and
    silences with `silencedetect`, within the tolerances the rows give.
  - Done when: those native checks pass in CI on every sample with audio and on the Sintel trailer.
  - Not: no App export, no page (the two rows); no change to `Mp4Input`'s video path.
  - Status: Open

### Unblocked by ShotDetector 3.15.0

This waited on shotdetector#492, which shipped in ShotDetector 3.15.0 (2026-10-25). It changes `Api.Detect` and
`worker.js`, so it runs after the page's structure row.

- Cancel without restarting the worker
  - Why: Cancel terminates the worker today, so the runtime starts again and the file is opened again: a second or
    two on a trailer, plus opening the file again (streamed since "Large files": seconds on a film). The owner chose (2026-10-24)
    cooperative cancelling through an asynchronous frame source upstream. Decision "Cancelling a run".
  - Where: ShotDetector 3.15.0 has `ShotDetection.DetectAsync(YuvVideo, IAsyncEnumerable<YuvFrame>, DetectionOptions?,
    CancellationToken)` ([shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492)): it runs on the
    caller's own awaits, with no other thread, and gives the synchronous `Detect`'s result. Here: an asynchronous `Mp4Input` frame source that yields to the
    worker's event loop every N frames, so a `cancel` message can run and cancel the token. On single-threaded wasm
    the yield must be a real macrotask: a `[JSImport]` returning a Promise resolved by `setTimeout(0)` or a
    `MessageChannel`, not `Task.Yield()`. `Api.Detect` becomes `async Task<string>` (a Promise to the worker).
    `worker.js` answers `cancel` with a `cancelled` message (an `OperationCanceledException` is not an `error`).
    `app.js` cancels with a message and falls back to `terminate()` when no `cancelled` comes within a second or so.
    `tools/app-check.mjs` calls the new export. A cancelled run's shots (the page keeps those it was told, "Shots as
    they're found", done) get a scenes CSV: `Api` keeps the shots it was told and makes it from them with `Shots.Csv`,
    and `cancelled` carries it (no partial stats CSV); `app.js` offers it as the download, the unfinished line not in it.
  - Done when: on the Sintel trailer, a run cancelled mid-way is followed at once by a run with another threshold,
    without opening the file again; the yields cost under 1% of the run's time (pick N for it); checks green, CSVs
    unchanged.
  - Not: no shared memory. A cancelled run throws and keeps no result, upstream too; its shots are the ones told.
  - Depends on: the 3.15.0 row and large files (both edit `Api` and `worker.js`); after "Shots as they're found"
    (done).
  - Status: Open

### First release: the page on GitHub Pages

The first release's rows are all done ([backlog-done.md](backlog-done.md)).

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

- Thumbnails per shot
  - Why: a table of times is hard to read; a still per shot shows what was found. Decision "Pictures of shots".
  - Where: `app.js`: for each shot, seek the `<video>` to the shot's first frame, `drawImage` to a small canvas,
    lazily as rows scroll into view (one seek at a time; `seeked` events).
  - Done when: a trailer's shots each show a still within a few seconds of the run finishing, and the detection
    result is unchanged (the stills are the browser's decode, not the library's).
  - Not: no second decode in .NET, no image export.
  - Status: Open

The rows below add to what the page shows (the owner's choice, 2026-10-24). They widen the decision's "The page
itself", which lists the first release's page, not every page there will be. Since the page's structure row
(decision [2026-10-25-one-page.md](decisions/2026-10-25-one-page.md)), each is a lane of the timeline or a card, in a
module of its own, so they run side by side once that row has landed. Where a row says "under the player" or "on the
shots strip", read a lane of the timeline. The waveform, audio measures and bitrate rows keep only their export and
panel; their core parts are "Audio in the core" under "Now" and "Packets in the core" (done). Whatever the page
derives for display is labelled as the page's own; the CSVs stay the only output the exactness check speaks for.

- Warnings on the page
  - Why: `DetectionResult.Warnings` says what the user should know about a run, and `Api.Detect` drops it today.
  - Where: `src/ShotDetectorWeb.App/Api.cs`: `warnings` (the strings as they are) added to `DetectResponse`;
    `app.js` shows them under the summary when there are any.
  - Done when: a run that produces a warning shows it on the page (find which input does, or say none of the samples
    does and show it with a made-up response in the PR); `tools/check.sh wasm` and `wasm aot` green, CSVs unchanged.
  - Not: no new warnings of our own.
  - Status: Open

- About this file
  - Why: the library describes the file as FFmpeg would without FFmpeg (README, the `YuvVideo` table), and `Open`
    already returns that description as JSON, but the page shows none of it.
  - Where: `app.js`, `index.html`, `style.css`: after opening, a small block with the codec, width and height, frame
    rate, pixel format, colour space and range, frame count and duration, in the words a viewer knows (say "BT.709",
    not the enum's name), the raw JSON one click away. Also the audio track's codec, sample rate and channels, or "no
    audio": `Open` adds them from the demuxer's `AudioStreamInfo`.
  - Done when: each sample in `tools/make-samples.sh` shows what its name says (25, 29.97 and 23.976 fps, the colour
    tags, full range, the cropped 854x480, the audio track where there is one); screenshots in the PR.
  - Not: no other fields beyond what the core returns.
  - Status: Open

- Shot statistics and a timeline
  - Why: a list of times doesn't show a film's rhythm. Average shot length is the number film analysis uses, and a strip
    of shots under the player shows where the cuts are at a glance.
  - Where: `app.js`, `index.html`, `style.css`: from the shots as `Detect` returns them, the number of shots, average and
    median shot length, shortest and longest, cuts per minute, and how long the run took against the video's length
    ("2.4 times real time"); a strip of shots in proportion to their length under the `<video>`, each one seeking to its
    shot, with the playhead following `timeupdate` and the current shot's table row marked.
  - Done when: the Sintel trailer's numbers match what the scenes CSV gives by hand, and the strip and the table follow
    playback; screenshots in the PR.
  - Not: no change to `Api`, no libraries.
  - Status: Open

- A graph of the scores
  - Why: scenedetect's users read the stats as a graph: each frame's score over time against the threshold shows why
    a cut was found and what a different threshold would do. The stats are computed for every run already
    (`statsCsv`), and the page only offers them as a download.
  - Where: `app.js`, `index.html`, `style.css`: a `<canvas>` graph of the run's score column over time, with the
    threshold as a line and the cuts marked, a click seeking the `<video>`, readable at phone width. The columns
    differ per detector (`content_val`, `delta_*`, `adaptive_ratio…`, and more for the others), so pick them by the
    CSV header, not by position, and say in the row's PR which column each detector's graph shows. Where a
    frame-difference score (`content_val`, `delta_*`) stays near zero for a second or more, mark the span as a still
    picture, labelled as the page's reading.
  - Done when: each of the five detectors draws its graph on the Sintel trailer, every cut sits on a score above the
    threshold (or the PR says why it doesn't, for detectors that also use the minimum shot length); screenshots in the PR.
  - Not: no charting library; no re-thresholding in JavaScript (a new threshold is a new run: detecting twice in two
    languages is how results drift).
  - Status: Open

- A waveform from ManagedCodecs' AAC
  - Why: a waveform under the player shows where the sound changes against the cuts. The owner chose (2026-10-24) to
    decode it with ManagedCodecs' `AacDecoder` (AAC LC and HE-AAC, within 3e-7 of FFmpeg's float output) rather than
    the browser's Web Audio, so every decoder on the page is managed code. Decision
    [2026-10-24-audio.md](decisions/2026-10-24-audio.md) says how.
  - Where: the core: a reference to `ManagedCodecs.Aac` and a way to read the MP4's audio track packet by packet
    into a loudness envelope (peak per bucket, a thousand buckets or so, with each bucket's time in seconds), without
    holding the decoded audio. Make the envelope with FrameReader 2.11.0's `WaveformBuilder` (`Add` each chunk of
    samples, then `Finish`: the points `WaveformData.Read` gives, without FFmpeg,
    [shotdetector#494](https://github.com/bisforboman/shotdetector/issues/494)).
    `Mp4Input` already reads the audio packets for the format's duration; see CLAUDE.md's
    gotcha on priming and `SkipSamples`, and apply the edit list. The App: its own `[JSExport]` (not part of `Detect`,
    so detection's time and results stay as they are), called by `worker.js` after `Detect`. `app.js` draws it on the
    shots strip from "Shot statistics and a timeline" (depends on that row). A file without an audio track draws
    nothing and says nothing. A track that isn't AAC, or that `AacDecoder` refuses (HE-AAC v2 with 34-band parametric
    stereo, for example), gets one line saying why there's no waveform, not an error. A sample in
    `tools/make-samples.sh` with 6 s of a 1 kHz tone at -20 dBFS, 2 s of silence, then 6 s of the tone again (`sine`,
    `volume`, `anullsrc`; the "Audio measures" row reuses it), plus a check in `tools/app-check.mjs` that the envelope
    is loud, silent, loud at those times, within a video frame. FrameReader references FFmpeg.AutoGen: after the audio
    pass, `LoadedAssemblies()` must still show no FFmpeg assembly (the interpreted check is strict). Depends on the
    3.15.0 row (FrameReader 2.11.0) and on large files (the stream it reads through).
  - Done when: that check passes interpreted and AOT. The Sintel trailer's waveform lines up with its sound in
    Chromium and Firefox (screenshots in the PR). The PR gives how much the bundle grew and how much time the decode
    adds on the trailer.
  - Not: no Web Audio; no change to playback (the `<video>` still plays the sound); no audio-based cut detection
    (ShotDetector has none); no exactness check against FFmpeg's audio, since a waveform has nothing to be exact about.
  - Status: Open

- Audio measures: reference tone, silence, peaks, loudness
  - Why: someone checking a master before delivery asks these first. Is there bars and tone at the head (a steady
    1 kHz sine at -18 or -20 dBFS)? Where is it silent? Does it clip? How loud is it, as EBU R128 measures loudness
    (LUFS)? All of it comes from the samples the waveform row already decodes. Decision
    [2026-10-24-audio.md](decisions/2026-10-24-audio.md).
  - Where: the core, in the waveform's pass, with no second decode and without holding the samples:
    - the reference tone, as ShotDetector already defines it (`ShotDetection.FindReferenceTone`, FrameReader's
      `ReferenceTone`): mostly 1 kHz (about ±15 Hz) at a steady level for at least 5 s, breaks of up to 1 s not
      splitting it, given as start, end and level (`ToneSegment`). Found with FrameReader 2.11.0's `ReferenceToneFinder`
      (`Add` each chunk of samples, then `Finish`: the segments `ReferenceTone.Find` gives, without FFmpeg,
      [shotdetector#494](https://github.com/bisforboman/shotdetector/issues/494)), so the definition is shared code;
    - silence: spans below -60 dBFS for 2 s or more (FFmpeg's `silencedetect` defaults);
    - peak per channel in dBFS, and how many samples reach full scale;
    - loudness per ITU-R BS.1770-4 and EBU Tech 3341/3342: integrated loudness, loudness range and true peak.
    The waveform's export returns these too. `app.js` shows a sound panel with the numbers, the tone's span and the
    silences marked on the shots strip. `tools/NativeCheck` compares the loudness numbers with FFmpeg's
    `ebur128=peak=true` (within 0.1 LU and 0.1 dB), the silences with `silencedetect` (within a packet) and the tone
    segments with ShotDetector's `FindReferenceTone` on the same file (within a packet, level within 0.1 dB), on the
    Sintel trailer and on the waveform row's tone sample, where both tones must come out at -20 dBFS. README "Checks"
    says these are tolerances, not byte-exact. Depends on the waveform row.
  - Done when: those checks pass in CI, natively and through the App on wasm interpreted and AOT; the panel shows the
    trailer's numbers (screenshot in the PR); the PR gives the time the measures add.
  - Not: no loudness correction or normalising; no codecs other than AAC; no colour bars detection (the Maybe row on
    picture measures has it).
  - Status: Open

- Bitrate and keyframes
  - Why: the bitrate rises at cuts and encoders put keyframes at scene changes, so a bitrate graph and keyframe marks
    on the shots strip show the encoder's own view of the cuts next to ShotDetector's. It needs no decoding: each
    MP4 packet carries its size, its time and whether it is a keyframe.
  - Where: the core keeps, for each video packet, its pts, its size and its keyframe and discard flags, during the
    walk over every packet `Mp4Input` already makes when it opens a file: `Mp4Input.Packets` (done, "Packets in the
    core"; README "Using the library"). The App returns them from an export of their own, as compact arrays (leave
    out or mark the discarded packets: an edit list's, decoded but not shown). `app.js` draws bitrate per second as a
    graph under the strip, puts keyframe ticks on the strip, and says how many cuts fall on a keyframe (labelled as
    the page's reading).
    `tools/NativeCheck` already compares the list with `ffprobe -show_packets` (pts, size, the K and D flags),
    exactly (`PacketCheck.cs`). Depends on "Shot statistics and a timeline".
  - Done when: the check is green on every sample, and the Sintel trailer's graph and ticks show on the page
    (screenshot in the PR).
  - Not: no I, P and B frame types (`VideoFrame` doesn't carry them; that is a Maybe); no audio bitrate.
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

The asks filed upstream on 2026-10-24 all shipped on 2026-10-25:
[shotdetector#492](https://github.com/bisforboman/shotdetector/issues/492) and
[#493](https://github.com/bisforboman/shotdetector/issues/493) in ShotDetector 3.15.0 (`DetectAsync` over an
asynchronous frame source, `DetectionOptions.OnShot`), [#494](https://github.com/bisforboman/shotdetector/issues/494)
in FrameReader 2.11.0 (`ReferenceToneFinder`, `WaveformBuilder`). Their rows are under Next.

- ShotDetector: the stats in less memory, written row by row
  - Why: since "Memory per frame" the stats CSV is the largest step of a long run: on a two-hour film `Stats.Csv`
    grows the wasm heap from 250 to 432 MB (322 MB allocated for a 19.8-million-character string), and `Stats` holds
    about 370 B a frame, 63.7 MB at the end (README "What the heap holds").
  - Where: upstream (bisforboman/shotdetector), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23): the stats stored by column, and
    `Stats.WriteCsv(TextWriter, position)` writing one row at a time. Then here: `Api.StatsCsv` writes through it into a
    `Blob` in pieces (a `[JSImport]` the worker gives), so the whole CSV is never one string.
  - Status: Waiting on the owner to file it (2026-10-25)

- ManagedCodecs: the MP4 index without its copies
  - Why: `Mp4Demuxer` allocates about 275 B a sample to index a file and keeps 56 (140 MB and 28.5 MB for a two-hour
    film with audio): it takes the wasm heap from 34 to 174 MB when a file opens, 250 MB once a run builds its own
    (README "What the heap holds").
  - Where: upstream (bisforboman/ManagedCodecs), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23): each track's samples filled once, a smaller
    `Sample`, optionally only the tracks asked for. Then here: the submodule bumped, the steps measured again.
  - Status: Waiting on the owner to file it (2026-10-25)

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

- Picture measures: black frames, brightness, broadcast-safe levels, letterbox, colour bars
  - Why: the picture side of a check before delivery, next to the audio measures: where it goes to black, how bright
    each frame is, whether luma leaves 16 to 235, whether there are black bars, whether there are colour bars at the
    head (with the reference tone, "bars and tone"). Each one has an FFmpeg filter to compare with (`blackdetect`,
    `signalstats`, `cropdetect`).
  - Where: a pass over each frame's Y plane as `Mp4Input.Frames` yields it, alongside detection, so it adds time to
    every run (measure how much before deciding). The owner filed it as a Maybe (2026-10-24).
  - Status: Maybe

- Frame types (I, P, B) on the strip
  - Why: shows the encoder's structure (GOPs, where the B-frames go) next to the bitrate and keyframes.
  - Where: first upstream: ManagedCodecs' `VideoFrame` doesn't say what kind of picture it is (`SliceHeader.Type`
    exists inside the decoder); then here, after "Bitrate and keyframes".
  - Status: Maybe (needs ManagedCodecs)

- WebCodecs as a decoder
  - Why not: the browser's own decoder is fast, but its output differs between browsers and from FFmpeg's, and
    identical results are the point. Decision "Not doing".
  - Status: Won't do (kept here so it isn't proposed again)
