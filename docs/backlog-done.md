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
    found". Live, first deployed by Pages run 40211814960 from `78958d9` (PR #21): the clip CI's run 40209682275
    checked on that commit, the one that run's Pages build made and the one https://bisforboman.github.io/shotdetector-web/demo/sintel-trailer.mp4
    serves are one file (SHA-256 `861b4a19…2138` in both step summaries and of the download, 10,069,632 bytes). In
    headless Chromium "Try the demo" on the live page ran it in 21 s (59 frames per second) to 8 shots, and its
    downloaded scenes CSV (839 bytes) and stats CSV (143,220 bytes) are byte for byte the native check's adaptive run
    on that file. CI's "Results" artifact couldn't be downloaded from the cloud session (its storage host is outside
    the session's network policy), so the native check was run there on the served file with CI's pinned BtbN FFmpeg
    8.1: its table equals CI's run's log row for row, every detector's shots and stats sizes)

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

- Packets in the core: bitrate and keyframes
  - Why: the core part of "Bitrate and keyframes", done with its check, so that row only adds an export and a panel.
  - Status: Done (2026-10-25: `Mp4Input.Packets` (`VideoPackets`) keeps each video packet's pts, size and keyframe
    and discard flags by column, 13 bytes a packet, during the walk opening makes; `tools/NativeCheck/PacketCheck.cs`
    compares it with `ffprobe -show_packets -select_streams v:0`, exactly: all 10 samples agree, the edit-end
    sample's 39 discarded packets too. On a two-hour film (172,800 frames) it keeps and allocates 2.25 MB, in the same
    time to open. The CSVs are unchanged)

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
  - Status: Done (2026-10-25: `app.js` keeps the states, the options, the estimate, cancel and the demo, and tells
    the panels its events (`chose`, `opened`, `run`, `progress`, `shot`, `done`, `cancelled`); `timeline.js` is the axis,
    the playhead, click and arrow keys to seek and `lane()`; `panels/cuts.js` (the new lane: a tick per cut as it is
    found, the part read shaded), `panels/run.js` (a card: summary, downloads), `panels/shots.js` (the table, below);
    `Api.cs`, `Api.Csv.cs`, `Api.Assemblies.cs`; `worker.js` calls any export by name. `tools/check.sh wasm` and
    `wasm aot`: 120 of 120 CSVs identical, `tools/app-check.mjs` unchanged. In headless Chromium (Playwright) and
    Firefox 136 (Ubuntu's build, Puppeteer over WebDriver BiDi), interpreted and AOT: a sample's CSVs equal the native
    ones, the demo with live shots and ticks, cancel with the unfinished line, a run after it, a refused file, a click
    and keys seeking with the playhead following, cards under the timeline at 390 px; screenshots in the PR)

- Audio in the core: waveform, reference tone, silence, peaks, loudness
  - Why: the core parts of "A waveform from ManagedCodecs' AAC" and "Audio measures", with their native checks, so
    their page rows only add an export and a panel.
  - Status: Done (2026-10-25: `Mp4Audio` and `Loudness.cs`, one pass with `AacDecoder` reading only the audio packets'
    data; `tools/NativeCheck/AudioCheck.cs` within tolerance on the tone sample, the short sine and the Sintel trailer:
    integrated within 0.005 LU of `ebur128`, range within 0.008 LU, peaks within 0.032 dB (`ebur128`'s three-decimal
    linear peaks), tone and silence ends 0 ms from `FindReferenceTone` and `silencedetect`, waveform values within 1 of
    `WaveformData.Read`; `samples/no-aac/` answered with reasons. The tone sample's 2 s gap comes out 1.9966 s after
    FFmpeg's AAC encoder, so neither finds it at `silencedetect`'s default 2 s: the check also compares at 1 s. #26)

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
    pass, `LoadedAssemblies()` must still show no FFmpeg assembly (the interpreted check is strict). The export brings
    ManagedCodecs.Aac into the trimmed bundle: add it to THIRD-PARTY-NOTICES.md's ManagedCodecs row. Depends on the
    3.15.0 row (FrameReader 2.11.0) and on large files (the stream it reads through).
  - Done when: that check passes interpreted and AOT. The Sintel trailer's waveform lines up with its sound in
    Chromium and Firefox (screenshots in the PR). The PR gives how much the bundle grew and how much time the decode
    adds on the trailer.
  - Not: no Web Audio; no change to playback (the `<video>` still plays the sound); no audio-based cut detection
    (ShotDetector has none); no exactness check against FFmpeg's audio, since a waveform has nothing to be exact about.
  - Status: Done (2026-10-26: the core in #26; the page here: `Api.Audio()` (`Api.Audio.cs`, its own JSON context)
    after the file's first run, the `audio` event, `panels/waveform.js` a lane (an SVG over the points' span, none
    without audio, one line when not measured). `tools/app-check.mjs` calls it on every sample: the tone sample loud,
    silent, loud within a frame, every waveform identical to the native one (0 values differ), no FFmpeg loaded,
    interpreted and AOT; 132 of 132 CSVs identical. The trailer's waveform lines up with FFmpeg's `showwavespic` in
    headless Chromium and Firefox 136. The bundle grew 258 KB compressed (889 KB raw), `ManagedCodecs.Aac.wasm` 43 KB
    of it; `Audio()` takes 0.63 s on the trailer and 72 s on a two-hour film, AOT in Chromium (README "The sound").
    The lane is under the timeline's cuts, not on a shots strip: the page's structure row replaced that strip)

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
  - Status: Done (2026-10-26: the core in #26; the page here: `panels/sound.js` on the waveform row's `audio` event (#31),
    a card with integrated loudness, range and true peak, each channel's peaks and full-scale count, "Reference tone:
    1 kHz at -20.0 dBFS, 0:00 to 0:06" per segment and the silences, tagged as measured, a silence's length marked as
    the page's own; a Measures lane under the waveform with the tone's spans and the silences, a click playing from
    one's start; nothing for a file without audio, one line when not measured. The tone sample's gap is now 3 s, so
    `silencedetect`'s default 2 s finds it (2 s came out 1.9966 s). `tools/check.sh`: 132 of 132 CSVs identical, the
    tone sample's silence 0 ms from `silencedetect`. In headless Chromium on the real export: the tone sample's two
    tones at -20.0 dBFS and the silence between them, the trailer's -13.2 LUFS, 14.1 LU, +1.9 dBTP; screenshots in the
    PR. Firefox wasn't run: Playwright's Firefox is blocked by the session's network policy and other installs were
    refused by its permissions. The panel is a lane and a card, not marks on a shots strip: the page's structure row
    replaced that strip)

- ShotDetector 3.16.1
  - Why: 3.16.1 keeps the stats in columns, about 52 B a frame instead of about 400 (bisforboman/shotdetector#512),
    the largest part of [#20](https://github.com/bisforboman/shotdetector-web/issues/20) that was ShotDetector's.
    3.16.0 also converts 8-bit 4:2:0 faster with SIMD on WebAssembly, with the same bytes, and adds `ScoreRange` and
    `Merge` (the workers row below).
  - Where: `Directory.Packages.props`: ShotDetector and ShotDetector.FastYuv to 3.16.1, and FrameReader to what
    3.16.1 depends on. Measure the two-hour film's heap again the way README "What the heap holds" did, and update
    that section and the Waiting row "ShotDetector: the stats in less memory, written row by row" (its column half has
    shipped; streaming the CSV row by row hasn't).
  - Done when: `tools/check.sh` green natively and on wasm interpreted and AOT (CI), with the scene and stats CSVs
    unchanged, and the new heap numbers in the README.
  - Not: no use of `ScoreRange` or `Merge` yet (their row).
  - Status: Done (2026-10-26: FrameReader stays 2.11.0, which 3.16.1 depends on (`[2.11.0, 3.0.0)`) and the latest.
    `tools/check.sh`: native all agree, every CSV and waveform in `out/native` byte for byte 3.15.0's on the same
    samples, the audio table identical; 132 of 132 CSVs identical interpreted and AOT; FastYuv replaced from v3.16.1
    runs. On the two-hour film under Node, AOT: the wasm heap ends at 208 MB instead of 432 (the stats CSV no longer
    grows it), the stats keep 65 B a frame natively instead of 370, the CSV allocates 175 MB instead of 325. FastYuv's
    SIMD: the trailer's adaptive run 20.8 s instead of 21.9 s in headless Chromium, 23.1 s instead of 24.0 s under
    Node, the film 1,636 s instead of 1,781 s: 4 to 8% less time. README "Large files" and "What the heap holds"; the
    Waiting row is now "ShotDetector: the stats CSV written row by row")

- CI: the slow checks once
  - Why: a CI run took 29 min (measured 2026-10-26): the WebAssembly check interpreted 19 min 10 s, AOT 5 min,
    FastYuv replaced 2 min 10 s, native 1 min 47 s, the rest about 1 min. Every merge ran all of it again on `main`,
    then Pages published AOT again and deployed (4 min), and docs-only pull requests paid the full run. Several
    sessions merge every day.
  - Where: `.github/workflows/ci.yml`, `.github/workflows/pages.yml`, `tools/check.sh`, README "Checks". The owner
    chose four changes: (1) a docs-only pull request (`docs/**`, `*.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`) still
    runs `build`, the required check, and skips every heavy step (`git diff --name-only` against the base, `if:` on each
    step; not `paths-ignore`, which never reports); (2) `concurrency` cancels a pull request's superseded runs, never
    `main`'s; (3) the interpreted check on pull requests runs one short sample with audio (`tools/check.sh` takes the
    samples to run), the strict "No FFmpeg assembly loaded at all." kept, and the full interpreted check runs once a
    day on `main` (`schedule`) and by hand; the AOT check stays on every sample on every pull request; (4) the push to
    `main` is tested by `pages.yml` itself: the native check, the AOT check on the very bundle it deploys (one
    publish) and FastYuv replaced, deploying only if all pass; `ci.yml` no longer runs on that push.
  - Done when: a docs-only pull request goes green in under 2 minutes; a code pull request's CI takes about 10
    minutes with the AOT check on every sample; a deliberate one-byte CSV break turns the run red (then reverted); after
    the merge, the push to `main` runs Pages with the checks, and it deploys.
  - Not: no check weakened for what the page deploys: the AOT check stays on every sample, on every pull request and
    before every deploy; the deploy's own checks (FastYuv's `.wasm` and its license in the artifact) stay.
  - Status: Done (2026-10-26: a code pull request's CI 5 min 21 s instead of 29 min (run 40334239866: native 1 min,
    interpreted on `with-audio-320x240-25.mp4` 22 s, 12 of 12 CSVs and "No FFmpeg assembly loaded at all.", AOT 3 min,
    132 of 132 CSVs on 11 samples); a temporary change of the last byte of the scenes CSV for runs over 1,000 frames
    (only the Sintel trailer) turned run 40335198564 red, AOT 126 of 132 identical while the interpreted check stayed
    green, then reverted. After the merge, Pages run 40337621791 on the push: native, AOT 132 of 132 CSVs on the
    bundle it then deployed, FastYuv replaced, the site's checks, and the deploy, 7 min 10 s instead of 29 min of CI
    and 4 of Pages. A docs-only pull request, the one that added this, ran `build` without the checks)

- Bitrate and keyframes
  - Why: the bitrate rises at cuts and encoders put keyframes at scene changes, so a bitrate graph and keyframe marks
    on the shots strip show the encoder's own view of the cuts next to ShotDetector's. It needs no decoding: each
    MP4 packet carries its size, its time and whether it is a keyframe.
  - Status: Done (2026-10-26: `Api.Packets()` (`Api.Packets.cs`) returns `Mp4Input.Packets` as JSON by column, the
    keyframes and discarded packets as indexes: 2.26 MB, 20 to 33 ms (AOT, Node) on a two-hour film, called as soon as
    the file is opened. `panels/bitrate.js`: a lane with the bitrate per second and a tick at each keyframe right
    under the cuts, a card with how many cuts fall on a keyframe after a run (the page's own reading: Sintel's
    trailer, 5 of 7). `tools/app-check.mjs` checks `Packets()` against ffprobe's packets on every sample, exactly
    (the native check writes them to `out/native/<sample>.packets.txt`). The CSVs are unchanged)

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
  - Status: Done (2026-10-26: `panels/stats.js`, a card beside the video, labelled the page's own figures, from the
    shots `Detect` returns: on the Sintel trailer (adaptive) 8 shots, average 6.53 s, median 2.33 s, shortest 0.58 s
    (shot 7), longest 24.21 s (shot 8), 8.0 cuts per minute, each equal to what its scenes CSV gives by hand, and the
    same for the content detector's 12; the AOT bundle ran it at 2.7 times real time in headless Chromium. The strip is
    the timeline's cuts lane (decision `2026-10-25-one-page.md`), where a click seeks into the shot clicked; the card
    names the shot playing and the table marks its row, scrolling its own box, checked by clicking into every shot and
    playing 13 s to 23 s. Firefox not run: its builds' hosts are outside the session's network policy)
