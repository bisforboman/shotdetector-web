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
  - Status: Done (2026-10-26: `panels/about.js`, on the `opened` event, the codec, size, frame rate, pixel format,
    colour space and range, frames, duration and audio track, each field's own value in its tooltip and Open's JSON
    folded under them; `Mp4Input.Audio` names the codec as ffprobe does from the headers (ManagedCodecs calls an MP3
    in 'mp4a' 'mp4a') with the demuxer's rate and channels, NativeCheck's `AudioStreamCheck.cs` equal to ffprobe's on
    all 14 samples and no-aac files, app-check.mjs: Open's track is the one `Audio()` measures; every sample shown in
    headless Chromium on the AOT bundle, as its name says. Firefox not run: this session's network policy blocks
    Playwright's Firefox and Mozilla's hosts, and a Firefox from conda-forge was refused by its permissions)

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
  - Status: Done (2026-10-26, #36: `panels/scores.js`, a canvas lane of the column each detector cuts on, picked by
    the header (`adaptive_ratio`, `content_val`, `average_rgb`, `hist_diff`, `hash_dist`), against the threshold the run
    used, a dot on each cut's score; still pictures where `content_val` stays under 1 for 1 s; every cut of the five
    detectors on the Sintel trailer past its line; a two-hour stats CSV read in under 0.1 s, each draw under 50 ms)

- Stop a run and keep what was read, without restarting the worker
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
    `tools/app-check.mjs` calls the new export.
    **Stop keeps what was read** (the owner's question, 2026-10-26: stats for a stopped run). The page's button
    becomes Stop: at the next yield the frame source simply ends, as if the file ended there, instead of cancelling
    the token. `DetectAsync` then finishes normally with the result for the frames read: the shots, the scenes CSV and
    the stats CSV, shown and offered for download like a finished run's, labelled "stopped at" the time reached. The
    token stays for a runtime that has to be abandoned, and `terminate()` for one that doesn't answer.
  - Done when: on the Sintel trailer, a run stopped mid-way shows its shots, its cards and both downloads, and a run
    with another threshold follows at once without opening the file again. `tools/app-check.mjs` stops a run at a
    frame N on every sample and checks that its stats CSV equals the first rows of the full run's up to N (each frame's
    stats depend only on the frames before it), and that its shots are the full run's up to the last cut before N,
    plus one ending at N. Find out from ShotDetector's source whether a frame source that ends before
    `YuvVideo.FrameCount` gives a warning, and show it if so. The yields cost under 1% of the run's time (pick N for
    it); checks green, CSVs unchanged.
  - Not: no shared memory.
  - Depends on: the 3.15.0 row and large files (both edit `Api` and `worker.js`); after "Shots as they're found"
    (done).
  - Status: Done (2026-10-26: `Mp4Input.FramesAsync`/`DetectAsync` pausing every 8 frames through `pause.js`, a
    `MessageChannel`; `Api.Detect` a promise, `Stop` and `Cancel`; `worker.js` handles `stop` and `cancel` between
    pauses. ShotDetector 3.16.1 neither warns nor refuses a source that ends before `FrameCount`. `tools/app-check.mjs`
    stops every run on every sample half-way: the stats CSV the whole run's rows but the adaptive ratio of the last 2
    frames, `None`; the shots the whole run's with the next one cut short, no cut missed. One run cancelled, no result.
    The pauses 0.25% of the runs AOT under Node, 6 µs each in Chromium's worker; 132 of 132 CSVs identical AOT, the
    whole runs' unchanged. In headless Chromium and Firefox 136 on the Sintel trailer, AOT: stopped at frame 608, the
    shots, the cards and both downloads (the stats CSV the native whole run's up to there), the stop answered in 201
    and 136 ms, a run with another threshold started 98 and 129 ms after its click without opening the file; a file
    picked mid-run abandons it on the same worker; a worker that doesn't answer is terminated after 1.5 s. A
    `[JSExport]`'s cancelled task never settles its promise in .NET 10: `Detect` fails instead, CLAUDE.md "Gotchas")

- Warnings on the page
  - Why: `DetectionResult.Warnings` says what the user should know about a run, and `Api.Detect` drops it today.
  - Where: `src/ShotDetectorWeb.App/Api.cs`: `warnings` (the strings as they are) added to `DetectResponse`;
    `app.js` shows them under the summary when there are any.
  - Done when: a run that produces a warning shows it on the page (find which input does, or say none of the samples
    does and show it with a made-up response in the PR); `tools/check.sh wasm` and `wasm aot` green, CSVs unchanged.
  - Not: no new warnings of our own.
  - Status: Done (2026-10-26: `DetectResponse.Warnings`, `DetectionResult.Warnings`' sentences as they are, an empty
    list when none; `panels/warnings.js` a card under the run's, shown only when a finished run has any, gone on a new
    file or run. No sample warns, nor can one: on the frames path (`Detect(YuvVideo, IEnumerable<YuvFrame>, ...)`)
    ShotDetector 3.16.1's only warning is "read sequentially, not in chunks: frames from the caller", for
    `DetectionMode.Parallel`, an option the page doesn't send (every sample in Auto, Sequential and Parallel, natively;
    the HLS and chunk-fallback warnings are the file path's). A frame source that ends before `YuvVideo.FrameCount`
    (the trailer's first 600 of 1253 frames) gives none either. So the PR shows that sentence in a made-up response
    (worker.js patched in the test's route), in headless Chromium, interpreted and AOT, wide and at 390 px; screenshots
    in the PR. `DetectionOptions.Warnings` isn't used: Parallel's is told before the first frame, the same sentence as
    the result's, and telling it live would need an import in `worker.js` and an event in `app.js` for a warning the
    page can't cause. `tools/app-check.mjs` checks the field is a list of strings on every run. `tools/check.sh wasm`
    and `wasm aot`: 132 of 132 CSVs identical. Firefox wasn't run: Playwright's and Mozilla's downloads are blocked by
    the session's network policy. The card is a panel of its own, not in `app.js`: the page's structure row)

- WebCodecs: are its frames ManagedCodecs' frames?
  - Why: [#15](https://github.com/bisforboman/shotdetector-web/issues/15). Decoding is about 80% of a run on wasm,
    and the browser's own `VideoDecoder`, often in hardware, could make a run 2.5 to 4 times faster. The page's
    design ruled it out ("Not doing": its output may differ between browsers and from FFmpeg's). The owner reopened
    that question (2026-10-26), proof first. H.264's decoding is defined exactly by the standard, so the same frames
    are plausible but not yet shown.
  - Where: measure only. Decode every sample, the Sintel trailer and the two-hour film with WebCodecs in headless
    Chromium and Firefox, copy each frame's Y, U and V planes out (`VideoFrame.copyTo`, I420), and compare them with
    ManagedCodecs' frames for the same file, frame by frame (a hash per plane, and the first differing frame and
    position where they differ). Also time the decode and the copy against ManagedCodecs on wasm AOT. A tool under
    `tools/`, not in the page. Write the numbers and the verdict in the decision's "Not doing" section.
  - Done when: a table per browser and sample, either "every frame identical" or where it differs, and the timings.
    The decision changes only if every frame is identical in both browsers; a later row would then build it, with
    ManagedCodecs kept wherever WebCodecs isn't there or differs.
  - Not: no change to the page or the checks.
  - Status: Done (2026-10-26: `tools/WebCodecsCheck`, `tools/webcodecs/`, `tools/WebCodecsWasm`, README
    "WebCodecs". Headless on a 4-core VM without a GPU: Chromium 153 decodes in software (`prefer-hardware`
    unsupported), and every frame of every sample and of the two-hour film (172,800) is identical to ManagedCodecs',
    every plane and timestamp; Firefox 136 decodes with the system's FFmpeg in software but hands every frame to the
    page as BGRX, so there are no Y, U and V planes to compare. Decode plus copy against ManagedCodecs on wasm AOT:
    Chromium 1.71 s against 16.65 s on the trailer, 243 s against 1,656 s on the film; Firefox 12.92 s against
    20.57 s on the trailer, 410 s against 1,929 s on the film. Not every frame identical in both browsers, so the
    decision's "Not doing" stands and no row builds it; the numbers are there)

- The page's new UI
  - Why: ten panels on the first release's layout: the result below the fold, a 540 px timeline beside a column of
    cards twice its height, the run's card second under the bitrate's, three ways of marking whose number it is
    (decision [2026-10-26-new-ui.md](decisions/2026-10-26-new-ui.md), "What a visitor sees today"). The owner asked
    for a proposal to pick a direction from (2026-10-26).
  - Where: the decision's choice B (proposed): the landing folds into a toolbar once a file is picked, the status one
    line under it; the video with the shot playing as its caption beside the run's card (figures, downloads, the
    warnings, a stopped run's line); the timeline across the whole width; the shots with a still each; the details as
    a grid of equal cards; one provenance chip per card. `index.html`'s skeleton, `style.css`, `app.js`'s
    `card(title, { place })`, the "Now" line out of `panels/stats.js`; the lanes unchanged. The mockup:
    [mockups/2026-10-26-new-ui.html](mockups/2026-10-26-new-ui.html).
  - Done when: the direction the owner chose, on the Sintel trailer in headless Chromium at 1280 px and 390 px in
    every state (idle, opening, detecting, stopping, done, stopped, lost, error, and paused if the pause row has
    landed), the video, the run and the whole timeline in the first screen at 1280 px; screenshots in the PR;
    `tools/check.sh wasm` green, the CSVs unchanged (the page's exports don't change).
  - Not: no framework, no build step; no change to what any panel computes or says; no new export.
  - Depends on: the scores, warnings and Stop rows landed on 2026-10-26, so it
    moves their panels rather than editing beside them; "Pause and resume a run" edits `app.js`'s buttons too, so
    whichever lands second merges the other.
  - Status: Done (2026-10-27: B as decided, the page 80rem wide. `index.html`'s toolbar, status line and four rows;
    `app.js`'s `card(title, { place, mark, markTitle })`, the states stopping, stopped and lost of their own, the shot
    playing as the caption under the video, told as `playing` so `shots.js` marks its row; one chip per card, the
    dotted underlines gone, the footer saying what the words mean. "Cuts on a keyframe" stays in the bitrate card,
    the reading of its packets. Every state on the Sintel trailer in headless Chromium (AOT bundle) at 1280x800 and
    390x844, light and dark: no horizontal scroll, every text at WCAG AA, keyboard focus visible; at 1280x800 the
    timeline ends at 796 px, the video letterboxed to fit when the window is shorter. `tools/check.sh wasm` 132 of 132
    CSVs identical. The table's first column is left for "Thumbnails per shot"; Pause waits for its own row)

- Long videos across several Web Workers
  - Why: [#19](https://github.com/bisforboman/shotdetector-web/issues/19). ShotDetector 3.16.0's
    `ShotDetection.ScoreRange`/`ScoreRangeAsync` scores a range of frames on a runtime of its own, and
    `ShotDetection.Merge` merges the ranges' `RangeScores` (carried as bytes, `ToBytes`/`FromBytes`) into exactly
    the result `Detect` gives for the whole video. Workers need no shared memory, so this works on Pages, unlike wasm
    threads. A film could run several times faster on a machine with several cores.
  - Where: decision [2026-10-26-workers.md](decisions/2026-10-26-workers.md), answered by the owner on 2026-10-26:
    progress per range with the shots at the end (one worker keeps live shots); Stop merges the ranges finished from
    the start; several workers above about 10 minutes of video, up to `navigator.hardwareConcurrency` − 1 and at most
    4, each range starting at a keyframe from the packet list; the audio pass in a worker of its own, in parallel.
    The page starts the workers, each opens the same `File`, and `tools/app-check.mjs` checks that a run split into
    ranges gives the same CSVs as one worker, on every sample, and that a stopped split run gives the result for its
    finished start.
  - Done when: the same CSVs split and whole; the speed and the heap on the two-hour film with 1, 2 and 4 workers,
    and the decision's 10 minutes and 4 adjusted to what was measured.
  - Depends on: "Stop a run and keep what was read" (it drives runs through `DetectAsync`).
  - Status: Done (2026-10-27: `Mp4Input.Ranges`/`ScoreRangeAsync`/`Merge`, `Api.Ranges.cs`, `app.js`'s split and
    `panels/ranges.js`, the sound in a worker of its own; NativeCheck's `RangeCheck.cs` and app-check split and stop
    every sample's default and all-detector runs, the same CSVs. On the two-hour film, 2, 3 and 4 workers ran 1.97,
    2.93 and 3.60 times as fast, the renderer at 1,065, 1,303 and 1,602 MB against 878; the 10 minutes became 2, the
    4 stayed: README "Long videos across workers", the decision's "As measured". Stop keeps the ranges from the start
    up to where the first unfinished one stopped: the decision's "As built")

- ManagedCodecs' license
  - Why: THIRD-PARTY-NOTICES.md says the submodule's pinned commit has no LICENSE file; the page ships its code to
    every visitor and should say under what terms.
  - Where: upstream (bisforboman/ManagedCodecs): a LICENSE file; then here: bump the submodule, update
    THIRD-PARTY-NOTICES.md and the page's footer.
  - Status: Done (2026-10-28: ManagedCodecs 0.1.0 on nuget.org is MIT, with a LICENSE file upstream;
    THIRD-PARTY-NOTICES.md says so. The page's footer names ManagedCodecs and links its repository, as before)

- ManagedCodecs from NuGet instead of the submodule
  - Why: a package pins a version in Directory.Packages.props like the others, and a clone needs no `--recursive`.
  - Where: when it is on nuget.org: replace the two project references in the core csproj with a package reference,
    delete `external/` and `.gitmodules`, update README "Packages" and CLAUDE.md, `ci.yml`'s `submodules: true`.
  - Status: Done (2026-10-28: `ManagedCodecs.Containers`, `.H264` and `.Aac` 0.1.0 from nuget.org, each pinning
    `.Core`; the submodule, `.gitmodules` and the workflows' `submodules: true` gone; `tools/mp4-edit-list.py` kept from
    it for make-samples.sh. ShotDetector/FastYuv 3.20.0 and FrameReader 2.14.0 in the same change, the CSVs unchanged
    (`tools/check.sh`); `Mp4Demuxer` moved to `ManagedCodecs.Containers`, the only API change this path saw)

- The packages' versions on the page
  - Why: the owner asked (2026-10-29) whether the footer shows the NuGet versions; it named the libraries without
    them. And a version the page reads from its own bundle is a check the pins can't give: a central version pins only
    the packages a project references, and FrameReader's 2.15.0 line (#48) had pinned nothing, the bundle still
    carrying 2.14.0 through ShotDetector's own range.
  - Where: `Api.Versions.cs` (`Versions()`: the runtime and each package's version from its assembly's informational
    version), `wwwroot/versions.js` filling the footer's slots, `tools/app-check.mjs` failing when the bundle's
    versions aren't Directory.Packages.props'; the core referencing FrameReader directly, which it uses.
  - Status: Done (2026-10-29: the footer says "ManagedCodecs 0.1.0 demuxes ... ShotDetector.FastYuv 3.20.0 converts
    ... ShotDetector 3.20.0 finds the shots and FrameReader 2.15.0 measures the sound, all of it .NET 10.0.x";
    app-check's versions check; FrameReader really 2.15.0, the checks run again on it)

- The adaptive detector's window and minimum content_val in the form
  - Why: a colleague's request, filed by the owner as
    [#50](https://github.com/bisforboman/shotdetector-web/issues/50) (2026-10-29): scenedetect's `window_width` and
    `min_content_val` toggled in the UI. ShotDetector has them as `DetectionOptions.FrameWindow` and `MinContentVal`.
  - Where: two inputs shown while adaptive is picked (`index.html`, `style.css`'s `form[data-detector]`, `app.js`'s
    `options()`: at their defaults sent as none), `DetectRequest`'s `frameWindow` and `minContentVal`, the scores
    lane's rule naming the run's minimum content_val; `tools/Shared/Runs.cs` and app-check's RUNS get the
    "adaptive-window" run (window 1, minimum content_val 5) against the FFmpeg path, and app-check checks that the
    defaults typed in give the default run's CSVs.
  - Status: Done (2026-10-29: the two fields, adaptive only, prefilled 2 and 15; the adaptive-window run byte for
    byte natively, interpreted and ahead of time)

- The form's default thresholds for histogram and hash
  - Why: `app.js`'s `DEFAULT_THRESHOLDS` prefills histogram 0.05 and hash 0.395, scenedetect 0.7.1's defaults, and
    sends a threshold equal to its default as none. ShotDetector's defaults follow `DetectionOptions.Compatibility`,
    0.7.2 unless set, whose are 0.2 and 0.35 (`ShotDetection`'s detector setup). So the form says 0.05 while the run
    compares against 0.2, and a visitor who types 0.05 gets 0.2. Found 2026-10-26 by "A graph of the scores": the
    lane draws the threshold the run used, and Sintel's histogram cuts all sit under 0.8, not 0.95.
  - Where: `app.js`'s `DEFAULT_THRESHOLDS` (histogram 0.2, hash 0.35) and its comment; `panels/scores.js` keeps the
    same defaults to draw the line (its `DETECTORS`).
  - Done when: the form prefills 0.2 and 0.35, and a histogram run with 0.05 typed in compares against 0.05 (the
    scores lane's line at 0.95, and more cuts than the default's on the Sintel trailer).
  - Not: no other compatibility than ShotDetector's default.
  - Status: Done (2026-10-29: measured natively on Sintel's trailer first, since ShotDetector 3.20.0's `Threshold`
    summary still says 0.05 and 0.395: a run without a threshold gives exactly the cuts of 0.2 (8 shots; 0.05 gives 17)
    and of 0.35 (20 shots; 0.395 gives 18). `DEFAULT_THRESHOLDS` now 0.2 and 0.35, and NativeCheck's
    `DefaultsCheck.cs` reads the page's defaults from app.js and checks each against the run without it on every
    sample: with the old values it fails on Sintel's trailer, histogram and hash. The stale summary in ShotDetector, and
    its CLI's help, filed as [shotdetector#591](https://github.com/bisforboman/shotdetector/issues/591))

- CI's checks picked from a pull request's files
  - Why: the owner (2026-10-29): a filter in the pipeline rather than a rule in CLAUDE.md. Every pull request ran every
    check (about 7 minutes in CI), and the rule to run them all locally first cost about an hour in a cloud session,
    for a change to one constant in app.js as much as for a package bump.
  - Where: `tools/ci-scope.sh` (the changed paths on standard input, none, native or all out; a path no rule names is
    all), `ci.yml`'s step "What the change touches" (outputs checks and wasm, the steps gated on them); README "Checks"
    and CLAUDE.md's rule for what to run before pushing.
  - Status: Done (2026-10-29: the classifier's cases checked locally, the step's script run against the last six merges,
    all of which need all; `pages.yml` and the daily run still run everything)

- Pause and resume a run
  - Why: the owner asked (2026-10-26) to resume after stopping. While the page stays open, that is a pause: the run
    stays in memory and goes on where it was.
  - Where: the asynchronous frame source from "Stop a run and keep what was read": a `pause` message makes it wait,
    at its next yield, on a promise that a `resume` message settles. `DetectAsync` simply waits meanwhile; nothing is
    cancelled or ended. `app.js`: a Pause button beside Stop while a run goes, then Resume; the progress bar and the
    frames-per-second figure hold still; live shots stay as they were told. Stop works while paused too.
  - Done when: on the Sintel trailer, a run paused mid-way and resumed gives exactly the CSVs of a run never paused
    (`tools/app-check.mjs` pauses and resumes at a frame on every sample, and compares); in headless Chromium and
    Firefox the buttons and the bar behave (screenshots in the PR).
  - Not: nothing kept across a reload (the next row); no change to the result.
  - Depends on: "Stop a run and keep what was read".
  - Status: Done (2026-10-29: `pause.js`'s `hold` and `release`, the worker's `pause` and `resume`, a Pause/Resume
    button and the paused state in `app.js`, the run's time without its pauses (`runMs()`); app-check pauses every
    sample's default run half-way and compares, and stops one while paused. Seen in headless Chromium on Sintel's
    trailer: README "Pausing a run". Not seen in Firefox: this cloud session's Playwright has Chromium only)

- Thumbnails per shot
  - Why: a table of times is hard to read; a still per shot shows what was found. Decision "Pictures of shots".
  - Where: `app.js`: for each shot, seek the `<video>` to the shot's first frame, `drawImage` to a small canvas,
    lazily as rows scroll into view (one seek at a time; `seeked` events).
  - Done when: a trailer's shots each show a still within a few seconds of the run finishing, and the detection
    result is unchanged (the stills are the browser's decode, not the library's).
  - Not: no second decode in .NET, no image export.
  - The owner's choices (2026-10-29): the stills from a hidden second `<video>` on the same file, not the player; taken
    as rows come into view, live shots during a run included; the shot's middle frame, not its first.
  - Status: Done (2026-10-29: `panels/shots.js`, a Still column first, the middle frame of each shot (the owner's
    choice: the first frame is often black from a fade in) from a hidden `<video>` on the same file, as rows come
    into view, kept by the time taken at. Seen in headless Chromium on Sintel's trailer: stills while it runs, all
    there as it ends, a stopped run's shortened last shot its own middle; no .NET or worker change, so the result is
    the same. Not seen in Firefox: this cloud session's Playwright has Chromium only)

- Real-browser smoke test
  - Why: the page is JavaScript around checked exports; once visitors use it, a change to `app.js` or `worker.js`
    needs a check of its own. Decision "Testing the page".
  - Where: a CI job (Linux, Playwright, Chromium only) that serves the published `wwwroot`, loads the page, sets the
    file input to one sample, runs with the defaults, downloads the scenes CSV and compares it with `out/native/`'s;
    `tools/page-check.mjs` or similar, run by `tools/check.sh page`. Pin Playwright's version.
  - Done when: the job fails when `app.js` breaks the download and passes on `main`.
  - Not: no cross-browser matrix, no visual tests.
  - Status: Done (2026-10-29: `tools/page-check/page-check.mjs`, `tools/check.sh page`, Playwright 1.63.0 by its
    lockfile; a sample picked, the same on two workers, and the demo paused a quarter in (the bar and status held
    still), resumed, then detected again and stopped while paused; each whole run's downloaded CSVs identical to the
    native run's, the stopped one's stats the whole run's rows up to its frame; interpreted in 2 min 20 s; CI's page
    tier for the page's own files, and on the AOT bundle in the full run and `pages.yml`. A copy of the bundle whose
    `app.js` changes the scenes CSV fails all three whole runs, one whose Pause doesn't pause the worker fails the
    pause)

- ManagedCodecs: the MP4 index without its copies
  - Why: `Mp4Demuxer` allocated about 275 B a sample to index a file and kept 56: the wasm heap went from 34 to 174 MB
    when a two-hour film opened, 208 MB once a run built its own (README "What the heap holds", 2026-10-26).
  - Where: upstream, [ManagedCodecs#138](https://github.com/bisforboman/ManagedCodecs/issues/138) (filed 2026-10-26);
    0.1.0 builds each track's sample table once, at its final size, 29 B a sample (ManagedCodecs#123).
  - Status: Done (2026-10-29: measured again the same way, the bundle before the package and with it side by side
    under Node, AOT, on the control made again by its recipe: opened 174 to 84 MB, the run 208 to 121 MB, the stats
    CSV's peak 208 to 151 MB, a split run's worker 174/208 to 84/121 MB; natively 77 B a sample allocated, 29 kept.
    #138 is answered for the heap. What it asked for that didn't ship, only the tracks asked for, and the packets' data
    allocated where the reads are skipped, change only what is allocated: the backlog's Waiting row
    "ManagedCodecs: a demuxer of the tracks asked for, packets without their data";
    [#59](https://github.com/bisforboman/shotdetector-web/pull/59))

- The frame a paused run is held at
  - Why: paused, the status says "Paused at frame N" from the last `progress` (frames processed), but the run is held
    at its next pause, by frames read, up to 8 frames on (Api's `PauseEvery`): stopped there, the result says another
    frame (seen 2026-10-29 by `tools/page-check` on the AOT bundle: "Paused at frame 196", stopped at frame 200).
  - Where: `app.js`'s paused status, and the `pause(framesRead)` import (`pause.js`, `worker.js`), which knows the frame
    the run waits at; README "Pausing a run".
  - Done when: the paused status names the frame a stop would end at; `tools/page-check` holds the stop to it exactly.
  - Not: the stopped result, which is right.
  - Status: Done (2026-10-30: `pause.js` tells the frames read at the pause a held run waits at (`whenHeld`), the
    worker posts it as `held`, and the paused status of a one-worker run names it; a split run's says "about", its
    ranges counting from their keyframes. `tools/page-check` now requires the stop to end at exactly that frame)

- ShotDetector: the stats CSV written row by row
  - Why: the half of this that stored the stats by column shipped in ShotDetector 3.16.1
    (bisforboman/shotdetector#512): on a two-hour film the stats keep 11.3 MB instead of 64.0 (65 B a frame instead of
    370), and `Stats.Csv` no longer grows the wasm heap there (208 MB, where the run leaves it, instead of 432; README
    "What the heap holds", 2026-10-26). The CSV is still built as one string: 20 million characters, 175 MB allocated
    (about 1 KB a frame), still the largest allocation at the end of a run, which fit in the room the heap already had
    on that film.
  - Where: upstream (bisforboman/shotdetector), an issue for the owner to file, its text in
    [#23](https://github.com/bisforboman/shotdetector-web/pull/23) less the columns: `Stats.WriteCsv(TextWriter,
    position)` writing one row at a time. Then here: `Api.StatsCsv` writes through it into a `Blob` in pieces (a
    `[JSImport]` the worker gives), so the whole CSV is never one string.
  - Status: Done (2026-10-30: `Api.StatsCsv` writes through `Stats.WriteCsv` into a `Blob` in parts of 256 K
    characters (`BlobWriter`, `wwwroot/blob-write.js`); the same CSV, byte for byte, in every wasm check and the page's
    downloads; on the two-hour control the wasm heap after the stats CSV is 115 MB, where the run leaves it, instead
    of 143 MB with the string, AOT under Node (README "What the heap holds"))

- The estimate's reference rate measured again
  - Why: `REFERENCE_FPS` in `app.js` (46 frames per second at 854x480, measured 2026-10-25) predates ShotDetector
    3.16's SIMD conversion and today's runs: on 2026-10-27 the two-hour film (640x360) ran at 131 frames per second on
    one worker, where the reference predicts 82, so a first visit's estimate says about twice the time (12 min for a
    run that took 6 with 4 workers).
  - Where: `app.js`'s `REFERENCE_FPS`, measured on the Sintel trailer as before (AOT, headless Chromium, the median of
    three), and the comment's date.
  - Done when: a first visit's estimate on the trailer and on the two-hour film within 20% of the run.
  - Not: no change to how a browser's own rate is kept.
  - Status: Done (2026-10-30: `REFERENCE_FPS` 64, was 46: the trailer's median of three on the AOT bundle in headless
    Chromium, 4-core cloud VM, each a first visit. A first visit's estimate said 19.6 s for a 20.5 s trailer run and
    517 s for a 492.1 s run of the two-hour film on 3 workers (the page's choice there); against the medians of three,
    1% short and 16% long. GitHub's runner ran the trailer at 151 fps the same day; the owner chose this VM's rate.
    README "Large files")

- Shots as JSON and HTML
  - Why: ShotDetector writes the shots as JSON (`Shots.Json`) and as scenedetect's save-html page (`Shots.Html`); a
    visitor who wants the shots in a program or a browser tab gets them without reading the scenes CSV. The owner chose
    it out of Maybe on 2026-10-30.
  - Where: `src/ShotDetectorWeb.App/Api.Shots.cs` (`ShotsJson()`, `ShotsHtml()`: the last run's, the HTML without
    images and with the run's cuts), `wwwroot/panels/run.js` (two more downloads next to the CSVs,
    `<file>.shots.json` and `<file>.shots.html`, a stopped run's too, for the part read: the owner's choices,
    2026-10-30); `tools/Shared/Runs.cs`, `tools/NativeCheck` (the core's against the FFmpeg path's, and the ranges'
    merge's), `tools/app-check.mjs` and `tools/check.sh` (the wasm run's against the native run's, file for file),
    `tools/page-check/page-check.mjs` (the downloads).
  - Done when: NativeCheck's table says "same, same" for every sample and run; `tools/check.sh wasm` compares the
    `.shots.json` and `.shots.html` with the native ones as it does the CSVs, and app-check.mjs holds a paused and
    resumed run's and a merge's to the whole run's, a stopped run's to its own shots; the page check downloads both
    from each whole run, byte for byte the native run's, and the stopped run's carry its scenes CSV's shots.
  - Not: images in the HTML (the page's stills are the browser's decode, not ShotDetector's frames); `app.js`.
  - Status: Done (2026-10-30: NativeCheck "same, same" on every sample and run; the wasm check 308 of 308 CSVs, JSON
    and HTML identical; the page check's downloads byte for byte the native run's, the stopped run's its shots)

- Picture measures: black frames, brightness, broadcast-safe levels, letterbox, colour bars
  - Why: the picture side of a check before delivery, next to the audio measures.
  - Status: Done (2026-10-30: decision [2026-10-30-picture-measures.md](decisions/2026-10-30-picture-measures.md),
    the owner's answers: all five, in every run in the same pass, checked exactly. `PictureMeter` measures each frame
    as a run reads it, about 2% of a run (0.12 ms a frame natively, 0.27 ms AOT at 854x480); a split run's ranges carry
    theirs in `RangeBytes`. `PictureCheck.cs`: signalstats and cropdetect per frame, cropdetect's whole crop and
    blackdetect at d=0 and 2 s equal FFmpeg 8.1's on all 11 samples; the colour bars by their definition where
    make-samples.sh puts them and nowhere else; the ranges joined the whole run's byte for byte. app-check.mjs:
    `Picture()` the native JSON interpreted and AOT; page-check.mjs: the card's crop and bars. The page: a Picture lane
    and card, marked measured)

- ManagedCodecs 0.2.0 on wasm: other pixels under the interpreter
  - Why: 0.2.0 (nuget.org, 2026-10-28 18:28 UTC, 97 commits past 0.1.0, nearly all H.264 decoder speed work in
    `Vector128` paths: deblocking a macroblock at a time, edge strengths and the residual add in vectors) is what the
    page should run, for its frames per second. Tried on 2026-10-29 with the pins otherwise as `main`'s (FrameReader
    2.15.0): natively every check agrees as with 0.1.0. On browser-wasm it doesn't:
    - Interpreted (`tools/check.sh wasm`): every sample's stats CSVs differ from the native ones, the scenes CSVs don't
      (the first sample, baseline-480x270-23976-bt601.mp4, from row 6 on, e.g. row 6's delta_edges 5.340 for 5.337);
      and app-check's stopped run, the same file decoded again in the same runtime, differs from its own whole run
      from row 2 on, the first P frame, by far more (row 4's delta_edges 9.72 for 5.74): the decoder's output varies
      from one decode to the next, which smells of samples read before they are written (`VideoFramePool`'s planes
      are `GC.AllocateUninitializedArray`, the new paths' stackallocs are under `[SkipLocalsInit]`) rather than a
      wrong but deterministic intrinsic. Published with `-p:WasmEnableSIMD=false` (so
      `Vector128.IsHardwareAccelerated` false, the scalar paths), the same two samples' CSVs equal the native ones.
    - Ahead of time (`tools/check.sh wasm aot`): 132 of 132 CSVs identical, so the pixels are right there. Its speed
      against 0.1.0's is not settled: the runs took 227 s for 0.1.0's 207 s the same day on a shared cloud machine
      whose 0.1.0 runs had taken 135 s the day before (Sintel's trailer, adaptive: 23.1 s for 21.5; all detectors:
      30.8 s for 25.1), interpreted 1,029 s for 1,290; a quiet machine, or the page's own frames per second, would
      say.
    ManagedCodecs' own wasm check (its ci.yml's `wasm` job, `tools/wasm-check.sh`) runs ahead of time only, so it
    can't see the interpreted fault.
  - Where: upstream (bisforboman/ManagedCodecs), filed as #206 with the above: (1) the H.264
    decoder's output under the Mono interpreter on browser-wasm with SIMD, wrong and varying between decodes, right
    with SIMD off and compiled ahead of time. To reproduce here: ManagedCodecs 0.2.0 in Directory.Packages.props,
    `tools/check.sh wasm` (the samples from `tools/make-samples.sh`). Then here: the bump, with the page's frames per
    second measured again on a quiet machine (README "Long videos across workers"), 0.2.0 against 0.1.0.
  - Done when: a ManagedCodecs release decodes every sample to the native CSVs interpreted as well as ahead of time
    (`tools/check.sh`, `tools/check.sh wasm aot`), and its speed on wasm is measured against 0.1.0's.
  - Not: no work-around here (CLAUDE.md: a bug in ManagedCodecs is reported there); `WasmEnableSIMD` stays on.
  - Status: Done (2026-10-30: ManagedCodecs 0.2.3 fixed #206 (narrowing that saturated under the interpreter,
    dotnet/runtime#136688); with ShotDetector 3.21.0 and FrameReader 2.16.0, natively every check agrees, and on
    browser-wasm 154 of 154 CSVs are identical interpreted and ahead of time, the page check and the replace check
    passing. Speed against 0.1.0, timed in CI on one runner (AMD EPYC 9V45, 2026-10-30, run 41067439223): the AOT
    bundle under Node, adaptive, six interleaved rounds of three runs each, Sintel's trailer at 480p 9.57 s with 0.1.0
    and 7.23 s with 0.2.3 (median; 131 and 173 frames per second), the 1080p original 32.46 and 25.21 s (39 and 50):
    0.2.3 takes 24 and 22% less time, and every one of its runs was faster than every one of 0.1.0's)

- Threads in the browser
  - Why: ManagedCodecs decodes a 720p P chain in about half the time on 4 threads (`H264Decoder.Threads`). Wasm
    threads need `SharedArrayBuffer`, so COOP/COEP headers, which Pages can't set; a service worker that adds them to
    its own responses (the `coi-serviceworker` trick) can. `DetectionMode.Parallel` doesn't apply to a frame source
    (it reads once, in order), so only decoding would speed up.
  - Status: Done (2026-10-30, decision `2026-10-30-threads.md`, the owner's choice: built for one-worker runs. .NET's
    threaded runtime starts only on the page's own thread, so `threads.js` runs it there, beside the page's worker,
    the run on .NET's thread pool (`Host.cs`); `sw.js` isolates the page. On the page with ManagedCodecs 0.2.3, Sintel's
    trailer on 4 threads in 8.5 s instead of 13.7 s, 1.6 times as fast, 1.85 at 1280x720 (interleaved rounds, AOT,
    4-core VM; 2.6 with 0.1.0's decoder); `ThreadsCheck.cs` and the page check: the same CSVs byte for byte)

- Offline (a service worker caching the bundle)
  - Why: once a service worker exists for threads, caching the bundle is a few lines and the page works on a plane.
  - Status: Done (2026-10-30, decision `2026-10-30-threads.md`: `sw.js`, network first, the cache as the fallback,
    the demo cached once fetched; the page check reloads the page offline and runs the demo from the cache)

- The estimate's reference rate on ManagedCodecs 0.2.3
  - Why: `REFERENCE_FPS` in `app.js` (64 frames per second at 854x480) was measured on ManagedCodecs 0.1.0
    ("The estimate's reference rate measured again", above). 0.2.3, now on `main`, decodes faster: CI's AOT
    bundle under Node ran Sintel 480p at 173 frames per second for 0.1.0's 131, adaptive, and the page's worker ran the
    trailer in 13.7 s (the threads decision's "Measured again"), where 64 predicts 19.6 s, so a first visit's estimate
    says about 40% too long.
  - Where: `app.js`'s `REFERENCE_FPS` and its comment's date, measured as before (AOT, headless Chromium, Sintel's
    trailer, the median of three, each a first visit), the threaded runs too (`THREADS_SPEEDUP` over the new rate).
  - Done when: a first visit's estimate on the trailer within 20% of the run.
  - Not: no change to how a browser's own rate is kept, nor to `SPLIT_SPEEDUP`.
  - Status: Done (2026-10-30: `REFERENCE_FPS` 75, was 64: the trailer's median of five interleaved first-visit rounds
    on the AOT bundle in headless Chromium, 4-core cloud VM, 16.7 s; `THREADS_SPEEDUP` 1.39 and 1.50 from the same
    rounds (12.0 s, 11.1 s), 3 threads 1.45. A first visit said 17 s for a 17.4 s run on the worker, 12 s for 13.1 s on
    2 threads, 11 s for 11.2 s on 4. GitHub's runner ran it at 204 fps on 4 threads; the owner chose this VM's rate
    and the new thread gains. README "Large files")

- Trimming: FFmpeg.AutoGen and the unused parts of ShotDetector out of the bundle
  - Why: nothing on this path uses FFmpeg.AutoGen, yet it ships, and THIRD-PARTY-NOTICES.md has to list it. Measured
    2026-10-29 (ShotDetector 3.20.0, FrameReader 2.15.0): the App's publish already trims every assembly in full
    (`TrimMode` is `full` for browser-wasm; ShotDetector and FrameReader are `IsAotCompatible`, so trimmable, and the
    publish has no trim warnings). FFmpeg.AutoGen comes out at 447 KB of its 648 because ShotDetector's code *reaches*
    it: the frame-source and merge paths the page runs share method bodies with the file path. `ShotDetection.Steps`
    (the iterator behind `RunAsync`, `DetectAsync` and `Merge`) constructs `VideoReader` from a path or a stream and
    calls `FindReferenceTone(path)`, and `VideoReader`'s members branch between in-process decoding and the caller's
    frames (`Frames` → `FramesInProcess`, `TakeTimestampPass` → `InProcessProbe.FrameTimestamps`, `DecodesWithLibraries`,
    `HasFrameThreads`). From the trimmer's dependency dump (`-p:_TrimmerDumpDependencies=true`), those six members are
    every bridge: stubbed with ILLink substitutions (a local measurement, not committed), `FFmpeg.AutoGen.wasm` is gone,
    `tools/check.sh wasm aot` passes (154 of 154 CSVs identical, "No FFmpeg assembly loaded at all." under AOT too),
    and the AOT publish's `_framework/` through `gzip -6` goes from 19.19 MB to 16.31 MB raw (−2.88 MB, 15%) and from
    5.76 to 5.14 MB compressed (−620 KB, 10.8%): `dotnet.native.wasm` −2.31 MB raw (−478 KB; the AOT code of the
    FFmpeg paths), `FFmpeg.AutoGen.wasm` −447 KB (−103 KB), FrameReader 91 to 20 KB, ShotDetector 256 to 222 KB.
  - Where: upstream (bisforboman/shotdetector), filed as #597 with the above: the frame-source entry
    points (`Detect`/`DetectAsync`/`RunAsync` over `YuvFrame`s, `ScoreRangeAsync`, `Merge`) should not reach
    FFmpeg-only code statically, so a trimmed app that never decodes with FFmpeg doesn't ship FFmpeg.AutoGen; e.g.
    `Steps` takes its `VideoReader` from the `frames` factory always (the path and stream entries passing theirs) and
    the reference-tone trim is decided by that entry, and `VideoReader`'s in-process members move behind the file
    path's own type. A check upstream could hold it: benchmarks/wasm's publish with no `FFmpeg.AutoGen` in its bundle.
    Then here: the package bump; app-check.mjs's and check.sh's "No FFmpeg assembly loaded" becomes "not bundled"
    (the listing check fails if `_framework/FFmpeg.AutoGen.wasm` exists), interpreted and AOT alike; THIRD-PARTY-NOTICES.md
    drops FFmpeg.AutoGen if no FFmpeg.AutoGen code is left in `dotnet.native.wasm` either; README "Size" measured again.
  - Done when: `_framework/` has no `FFmpeg.AutoGen.wasm`, the checks are green, and the README's sizes are updated.
  - Not: no change to the core library; no ILLink substitutions here (a stub of ShotDetector's internals would break
    silently when they change, and CLAUDE.md: what belongs upstream is reported there).
  - Status: Done (2026-10-30: ShotDetector 3.22.0 fixed #597; neither bundle has `FFmpeg.AutoGen.wasm` (`tools/check.sh`
    fails if one does), no FFmpeg assembly loads at all interpreted or AOT, 308 of 308 CSVs, JSON and HTML identical
    both ways, the page check and the replace check passing. AOT `_framework/` 16.23 MB raw and 5.12 MB through
    `gzip -6`, from 19.86 and 5.97 (−18% and −14%); the threaded bundle 16.90 and 5.31, from 20.56 and 6.16)

- Fragmented MP4
  - Why: screen recordings and streaming exports are fragmented; ManagedCodecs already demuxes them as FFmpeg does, the
    core refuses them ([decisions/2026-10-30-scope-widening.md](decisions/2026-10-30-scope-widening.md)).
  - Where: `Mp4Header`/`Mp4Input` (the sample count, the rate and the constant-rate check from the packets' walk,
    `FrameCount` FFmpeg's `nb_frames`: null with an empty `moov`, the `moov`'s own count otherwise), two samples in
    `tools/make-samples.sh` (`empty_moov` with B-frames and AAC; `frag_keyframe` alone), README and CLAUDE.md's scope.
  - Done when: `tools/check.sh native` and the wasm checks pass on both samples, every field and run byte for byte.
  - Not: variable rate within fragments (refused, as in `stts`); rotation.
  - Status: Done (2026-10-30: PR #72; both samples byte for byte natively, on wasm (336 of 336 files) and on the
    page in Chromium on threads, one worker and two; rotation and a variable rate are rows of their own, waiting on
    shotdetector#608 and #609)

- Interlaced H.264
  - Why: broadcast material. ManagedCodecs decodes MBAFF frames and field pictures bit-exactly; `Mp4Input` refused
    any SPS with `frame_mbs_only_flag` 0.
  - Status: Done (2026-10-30: MBAFF frames, top and bottom field first, and libx264's fake-interlaced accepted, detected
    on the frames as decoded with deinterlacing off (ShotDetector's default), byte for byte natively and on wasm on
    three samples checked with `trace_headers` and ffprobe; deinterlacing and the field order wait on shotdetector#614
    and ManagedCodecs#225, the "Deinterlacing interlaced H.264" row; decision 2026-10-30-interlaced.md)

- MJPEG in AVI
  - Why: ManagedCodecs' MJPEG decoder is bit-exact with FFmpeg and its Containers package demuxes AVI. Cameras and old
    footage.
  - Status: Done (2026-10-30; decision [2026-10-30-mjpeg-avi.md](decisions/2026-10-30-mjpeg-avi.md): `VideoInput` with
    `Mp4Input` and `AviInput`, `AviHeader` for the audio streams' description and VBR length; three AVI samples, every
    description field, CSV, JSON and HTML byte for byte, natively and on wasm, interpreted and AOT; 4:2:2 MJPEG waits
    for the 4:2:2 row; a dropped frame refused, ManagedCodecs#226; the page's picture of an AVI a row of its own)

- Resume after Stop, across visits
  - Why: the owner asked (2026-10-26) to resume a run after stopping it, with the stopped part's stats shown in
    between. ShotDetector's `ScoreRange` says "the range ends where the frames stop", so a stopped run is a range that
    ended early. Scoring a second range from where it stopped and calling `Merge` on both gives exactly the result for
    the whole video. `RangeScores.ToBytes` makes the stopped part's scores storable, so a later visit can resume too.
  - Where: built with the workers row, on the same `ScoreRangeAsync` and `Merge`:
    - **At Stop:** `Merge` of the ranges so far shows the result for the part read (find out from ShotDetector's source
      that `Merge` takes ranges that end before the video does, as the workers decision's Stop already assumes).
    - **Resume:** a range from the last frame read (decoding from the keyframe before it and dropping frames up to the
      one before), then `Merge` over all ranges.
    - **Kept in the browser:** the scores' bytes in IndexedDB, keyed by the file's name, size and modified time and by
      the options, behind try/catch; "Resume" offered when the same file and options come back, and forgotten on a
      finished run or a click.
    - **Live shots:** `ScoreRange` tells no shots (`OnShot` isn't honoured there: shots are decided in `Merge`), so a
      one-worker run built this way would lose its live table. Ask upstream for `OnShot` on a range starting at frame 0
      (its shots up to the range's end are what `Detect` would tell), with the owner's OK to file it. Until it ships,
      a run on `DetectAsync` keeps no scores to merge, so resuming it would mean scoring its start again: run one-worker
      runs through `ScoreRangeAsync` with progress but no live table, or leave them unresumable. Measure both and put
      the choice to the owner in the PR.
  - Done when: `tools/app-check.mjs` stops at a frame on every sample, resumes, and gets exactly the full run's CSVs;
    the same across a page reload in headless Chromium and Firefox; screenshots in the PR.
  - Not: nothing sent anywhere (the scores stay in the browser); no change to the result.
  - Depends on: "Long videos across several Web Workers".
  - Status: Done (2026-10-30: every one-worker run one range from frame 0, merged; a stopped run's scores kept in
    IndexedDB, one record (the owner's choice), shown again at once when its file is picked, resumed on one worker with
    its shots told, or split for a rest over 2 minutes; the decision's later answers and "As built"; NativeCheck's
    ResumeCheck, app-check.mjs's checkResume on every sample, and the page check across a reload in Chromium (on
    threads) and Firefox, split in the same visit)

- The page's numbers and words after a stop, a resume and a reload
  - Why: after a resumed run the status line's "times real time" divided the whole video by the rest's time (5.2
    against the statistics' 4.7); "resumed from frame 124" after a stop at 216 named the keyframe the rest scores
    again from, not where the run had got to; the kept run shown again after a reload had no sound, and its bitrate
    and statistics cards counted the whole file's keyframes and the cut-short last shot as a length; the picture card
    said "and 3 shorter, on the timeline." after "Black: none of 2 s or more."; Forget, which deletes the kept run,
    looked like Detect.
  - Where: `app.js` (`show`, `resumedFrom`, `restore` measuring the sound as a run does), `panels/stats.js`,
    `panels/bitrate.js`, `panels/picture.js`, `index.html` and `style.css` (Forget's `danger` style),
    `tools/page-check/page-check.mjs`.
  - Done when: the page check asserts each: the two times real time equal after a resume, the resumed status naming
    the stopped frame (the keyframe in brackets), the kept run's sound card and waveform, its bitrate and statistics of
    the frames read, no picture line that isn't a sentence, Forget unfilled and not Detect's colour; the CSVs, JSON and
    HTML byte for byte as before (`tools/check.sh native page`).
  - Not: no change to the layout (the next row) or to any export.
  - Status: Done (2026-10-30: the status line's times real time and the statistics' Run both of the frames read, 1.3
    and 1.3 after a resume across a reload; "resumed from frame 192 (rescored from its keyframe, frame 124)"; the kept
    run's sound measured as for a run, its bitrate "In the 192 frames read: ..." and its statistics without the
    cut-short last shot; "Shorter than 2 s: 7, on the timeline." on a line of its own; Forget underlined in the
    error's colour, unfilled. The page check asserts each (Chromium here; Firefox in CI), the downloads identical to
    the native run's)

- The page's layout and styling
  - Why: the details grid (auto-fill, 17rem) leaves a card alone on a row with holes beside it; the shots table spreads
    five columns over the width with small stills, in a 70vh scroll box that clips its last row even with 8 shots; the
    picture lane's out-of-range ticks paint it red end to end, the playhead's red; every card opens with 3 to 5 lines
    of how it is checked; the cuts lane doesn't show which shot is playing.
  - Where: mostly `style.css`, with `panels/cuts.js`, `panels/picture.js`, `panels/run.js` and the cards' notes; the
    page check's screenshots at 1280 and 390 px, light and dark, of done, kept and resumed.
  - Done when: no details row with more than about 100 px of empty height under a card; every shot row visible
    without an inner scrollbar at 15 shots or fewer; no red in a lane but the playhead; the run card's figures above
    the fold at 1280x800; the owner has seen the screenshots before and after; the CSVs, JSON and HTML byte for byte.
  - Not: the layout B stays; no new export; no mark loses what it means (CLAUDE.md "Adding to the page").
  - Depends on: the row above (it edits the same panels).
  - Status: Done (2026-10-31: the details as two balanced CSS columns, About two pairs to a line where wide; the
    shot statistics beside the shots table, sticky, the table whole up to 15 shots with 8rem stills; the picture
    lane's out-of-range ticks grey and as tall as their share on a log scale, the black spans hatched, the colour bars
    as luma steps, the keyframes violet; the cards' notes in their marks' tooltips, the downloads' in their links';
    the cuts lane a span per shot, the playing one shaded, its number where it fits. The page check's `looks` at
    1280x800 for the demo done, kept and resumed: 47, 9 and 48 px at most under a details card (main: 656), no inner
    scroll, no red in a lane but the playhead, the run's figures down to 324 and 379 px, nothing wider than the window
    or its card; screenshots at 1280 and 390 px, light and dark. The owner saw them before and after and said yes)

- Frame types (I, P, B) on the strip
  - Why: shows the encoder's structure (GOPs, where the B-frames go) next to the bitrate and keyframes. The owner
    chose it on 2026-10-30.
  - Where: first upstream: ManagedCodecs' `VideoFrame` doesn't say what kind of picture it is (`SliceHeader.Type`
    exists inside the decoder, `SliceType` internal; neither 0.2.4 nor main has it, 2026-10-30), filed as
    ManagedCodecs#221: each frame's type as FFmpeg's `pict_type`, checked against `ffprobe -show_frames`. Then here: a
    lane of the frame types from the run's frames after `bitrate.js`'s keyframes lane, marked `core` with a
    NativeCheck comparison against `ffprobe -show_frames` field by field, as `PacketCheck.cs`.
  - Done when: a ManagedCodecs release has it, and the lane shows each frame's type, checked natively against ffprobe.
  - Not: no parsing of slice headers here (CLAUDE.md: report it there, don't work around it).
  - Status: Done (2026-10-30: ManagedCodecs 0.3.0's `VideoFrame.PictureType`, kept by `PictureMeter` as a column of
    `PictureMeasures`; `Api.FrameTypes`, `panels/frametypes.js` marked core; `FrameTypeCheck.cs` the same as
    `ffprobe -show_frames` on all 19 samples, MBAFF and MJPEG (all I) included, whole, on four threads and in ranges;
    app-check.mjs and the page check hold the App and the page to it)

- ManagedCodecs: a demuxer of the tracks asked for, packets without their data
  - Why: ManagedCodecs 0.1.0's `Mp4Demuxer` still indexes video and audio whatever its caller reads, and `ReadPackets`
    allocates every packet's data even where the stream skips the reads. On the two-hour control natively, 342 MB of
    zeroed arrays come out of `Open`'s 405 MB allocated, and a run allocates and throws away the audio track's 115 MB.
    It doesn't grow the wasm heap: both were tried in a patched local build, 84 MB opened and 121 MB after the run either
    way (README "What the heap holds", 2026-10-29). So low priority: allocation and time, not memory.
  - Where: upstream (bisforboman/ManagedCodecs), filed as #211 from the text in
    [#59](https://github.com/bisforboman/shotdetector-web/pull/59), a follow-up to
    [ManagedCodecs#138](https://github.com/bisforboman/ManagedCodecs/issues/138). Then here: the run's demuxer
    (`Mp4Input.DecodedFrames`, the ranges') of the video track only, and the opening pass through the timing-only
    read, measured again as README does.
  - Done when: a ManagedCodecs release has both, and the page uses them with the checks unchanged.
  - Not: no copy of the demuxer here (CLAUDE.md: report it there, don't work around it).
  - Status: Done (2026-10-31: ManagedCodecs 0.4.0's `Mp4Demuxer(stream, MediaTypes, ...)` and `ReadPacketInfo`
    (#233): opening's duration and packet list from `ReadPacketInfo`, a run's and a range's demuxer of the video track
    only, `Mp4Audio`'s of the audio only, its `AudioDataStream` gone; the checks unchanged, native, wasm and AOT. On the
    two-hour control natively opening allocates 49.6 MB instead of 404 and a run reads 229.9 MB instead of 345.7; the
    wasm heap's peak and the bytes read from the `Blob` as before: README "What the heap holds")

- The picture of an AVI on the page
  - Why: no browser plays MJPEG in AVI, so an AVI's page shows a line where the video would be and its shots' table no
    stills (decision `2026-10-30-mjpeg-avi.md`, the owner's choice 3). Each MJPEG packet is a JPEG the browser shows.
  - Where: an export of a packet's bytes (`Api.Frame(index)`, `VideoInput.Packets` says where); the player an `<img>` of
    the packet at the playhead when the `<video>` can't play, no sound; `panels/shots.js`'s stills from the same.
  - Done when: picking `samples/mjpeg-320x240-25.avi` in the page check shows the frame at the playhead and a still per
    shot, and the downloads are the native run's.
  - Not: no transcoding, no decoding of the JPEGs in .NET for the picture (the browser's own JPEG decoder).
  - Status: Done (2026-10-31: `VideoPackets.Offsets`, where the AVI walk's one read of each chunk's data starts, in
    `Packets()` as `offsets`; the page cuts each JPEG from the picked `File` there, not through an `Api.Frame(index)`
    export, which would wait behind a run on the page's worker (the owner's choice that day); the player an `<img>` of
    the frame at the playhead, the playhead the page's own; the shots' stills from the same JPEGs. The packet check
    hashes the bytes at every offset against ffprobe's `data_hash`, natively and on wasm; the page check picks
    `mjpeg-320x240-25.avi`: frame 0, the frame after a timeline click and a shot's first frame shown, a still per shot,
    the downloads the native run's)

- Rotation
  - Why: phones record rotated; the FFmpeg path turns frames upright.
  - Where: first upstream, [shotdetector#608](https://github.com/bisforboman/shotdetector/issues/608)
    (`YuvVideo.Rotation`); then `Mp4Header` reads the `tkhd` matrix's angle as `av_display_rotation_get`, quarter turns
    only, samples at 90, 180 and 270, `PictureMeter`'s crop and colour bars in the upright frame's coordinates (the
    owner's choice, decision 2026-10-30-scope-widening.md).
  - Done when: the native, wasm and page checks pass on the rotated samples byte for byte.
  - Status: Done (2026-10-31: ShotDetector 3.23.0's `YuvVideo.Rotation` (shotdetector#608, #615); `Mp4Header.Rotation`
    from the 'tkhd' matrix after the 'mvhd' one as mov.c, the angle as `av_display_rotation_get`, `Mp4Input` taking a
    quarter turn and refusing any other; three samples, 90, 180 and 270 (FFmpeg's 90, -180, -90), coded turned and
    letterboxed on four different widths, their rotation and coded size checked with ffprobe; the native check's
    rotation field, every run, the ranges, threads, resume and the picture (crop `288:176:8:22` and the colour bars in
    the upright picture) the FFmpeg path's; the page check picks the 90 one on threads and on two workers; the
    About card says how the video is shown; `FrameTypeCheck` without `-show_frames`, whose display matrix side data
    ffprobe prints over lines)

- Performance pass on the page
  - Why: the owner's choice (2026-10-31): measure what a visitor waits for and what the page costs, then fix what
    stands out, a pull request each, the results byte for byte as before.
  - Where: measured by a throwaway workflow on the branch `claude/perf-measure` (`tools/page-check/perf.mjs`, not on
    `main`): the AOT bundle as `pages.yml` publishes it, served with Pages' headers (gzip, `max-age=600`), in headless
    Chromium on GitHub's 4-core runner, and the live site for a first visit. Two runs, 2026-10-31: run 41186034723
    (AMD EPYC 9V45) and run 41187986379 (EPYC 7763, about 1.7 times as slow); figures as "9V45 / 7763" where both.
    - First load: the page 25 files, 195 KB (71 KB gzipped); the worker's bundle 4.8 MB gzipped (14.6 MB). Loaded in
      40 to 80 ms, isolated (the service worker's reload) at 210 to 670 ms, interactive (the footer's versions: the
      worker's runtime answered) at 540 to 1,220 ms; the live site 890 to 950 ms. The reload aborts the first
      document's bundle half-loaded and the second fetches it all again (live: 39 to 44 requests, 4.8 to 5.2 MB);
      .NET's loader fetches with `no-cache`, so every later visit revalidates the 26 files (304s on Pages). A revisit
      was interactive in 430 / 620 ms.
    - The threaded bundle: 31 more files, 5.6 MB gzipped (17.4 MB), fetched and started at the first one-worker run's
      click: the first progress after 1.4 / 2.1 s on threads against 0.33 / 0.5 s on the page's worker, though the
      run ends sooner (the demo done in 5.1 to 5.5 / 8.8 to 9.7 s on 4 threads, 7.3 to 7.6 / 12.4 to 12.6 s on the
      worker; first shot 2.6 to 2.9 / 4.2 to 4.7 s and 2.4 / 4.0 s).
    - The two-hour control (README "Large files", made again: 640x360, 250 kbit/s, 172,800 frames, 1,105 shots):
      split on 3 workers done in 221 / 406 s, its first shot told only by the merge, 5 s / 7 s before the end;
      on one worker on 4 threads done in 291 / 534 s, its first shot at 3.2 / 5.2 s.
    - Memory, the largest renderer process's RSS each second (run 41187986379): split, 812 MB a minute in while the
      runtimes open the file, then flat at about 600 MB, 1,007 MB at its peak, 547 MB after; one worker on threads,
      rising steadily from 673 to 944 MB over the run (about 30 MB a minute), 770 MB after. Run 41186034723's peaks:
      1,047 and 990 MB.
    - The panels after a run: every lane and card drawn 70 to 120 ms after `done` on the demo, 170 to 440 ms on the
      control, no long task over 82 ms. But a split run's merge tells its 1,105 shots one message each, and
      `panels/cuts.js` reads the track's width for each new span, a layout each: 3.7 / 5.2 s of the page's thread in
      the last 7 s before `done` (in tasks under 50 ms, so no long task). The scores lane reads the control's stats
      CSV in 40 ms; the stills are a few ms each.
  - The owner's picks (2026-10-31), all four offered, a pull request each: (1) the cuts lane without a layout per
    span; (2) a split run's first range telling its shots as it finds them; (3) the threaded runtime started when a
    file is picked or the demo fetched, not at the click; (4) the threaded run's growing renderer, found and fixed or
    reported upstream.
  - After, measured together in run 41191247215 (`claude/perf-measure-all`, the three fixes merged): (1) the cuts lane
    17 ms of the page's thread in the split run's last seconds, from 3.7 / 5.2 s; (2) the split control's first shot
    at 2.4 s, from 216 / 399 s; (3) on threads the threaded bundle's files were in 0.6 s after the file was picked,
    and the long file's first progress came at 1.5 s, the threaded bundle no longer fetched after the click.
  - (4), the growing renderer, measured again with a full collection every 10 s (Chromium with `--expose-gc`, `gc()` in
    the page and each worker; run 41194804248): on the page's worker the largest renderer process grew 412 to 472 MB
    over the control's run, against 425 to 715 MB without; on threads 662 to 761 MB against 683 to 869 MB, the
    threaded runtime's wasm memory 166 to 239 MB of it (the JS heap flat at 10 to 16 MB). So nothing leaks, on threads
    or off: the growth is the run's own results in the .NET heap, which grow with the frame count (README "What the
    heap holds"), and garbage the browser collects late (the slices the file is read in, as CLAUDE.md's Gotchas says of
    `FileReaderSync`), which it frees under pressure. No change here and nothing to report upstream (README "Large
    files" says so).
  - Done when: the four are each a pull request with its before and after, the checks as CLAUDE.md's "Before pushing"
    says (the results byte for byte).
  - Not: no change to what is detected or to the downloads; nothing bought with exactness.
  - Status: Done (2026-10-31: #85 the measurements and the cuts lane, #86 a split run's first shots live, #87 the
    threaded runtime started when a file is picked; (4) found to be no leak, the finding in README "Large files")

- Editor exports on the run card (issue [#84](https://github.com/bisforboman/shotdetector-web/issues/84), the first
  slice of [docs/ideas/editor-exports-and-launch.md](ideas/editor-exports-and-launch.md); the owner's choice,
  2026-10-31)
  - Why: editors are the users most likely to come back, and scenedetect's timeline files are what they would use;
    ShotDetector's `Timeline` already writes them byte for byte as scenedetect does, and adds no assembly to the
    bundle (no FFmpeg.AutoGen, +17 KB).
  - Where: the core's `TimelineFiles` (`Timeline.Edl`, `Fcpx`, `Fcp7`, `Otio`, `Qp`, `Keyframes` with their defaults
    (keyframes included and the file referred to by its name only: the owner's choices), the result
    given `VideoPath` `/<file name>`), `Api.Timeline.cs`'s `TimelineFile(format, fileName)` with its twin in
    `Api.Threads.cs` and its name in `threads.js`, a "For editors" row of downloads in `panels/run.js`; NativeCheck,
    app-check.mjs (whole, paused, split and resumed runs) and the page check's downloads against `Detect(path)`'s.
  - Done when: `tools/check.sh native wasm aot page` passes with the six files identical to the native run's on
    every sample and run (the page's but for the file's name).
  - Not: YouTube chapters, marker lists or the public launch (parked in the note); no path field (the editor relinks).
  - Status: Done (2026-10-31: the six files identical to the FFmpeg path's on every sample and run natively, 1330 of
    1330 files on wasm interpreted, and the page's 60 downloads the native run's; AOT in CI)

- Real-world check: the core and the page on real files
  - Why: the generated samples each isolate one thing, and real files mix them: long camera and broadcast-like H.264,
    phone videos (rotation, a variable rate), fragmented and interlaced material, AAC audio, and old encoders' quirks.
    Results must equal ShotDetector's FFmpeg path byte for byte on them too, as NativeCheck and app-check do on the
    samples (the owner's choice, 2026-10-31; shotdetector's own is its `realworld.yml` and `tools/realworld.py`).
  - Where: `tools/realworld.sh` (the files, pinned by SHA-256: Blender Foundation films, CC-BY, from
    download.blender.org, the one host the cloud sandbox reaches, and variants of them the pinned FFmpeg makes:
    fragmented, rotated, 1080i25 MBAFF), `.github/workflows/realworld.yml` (nightly and by hand, never on a pull
    request, not required: the owner's choice, 2026-10-31), `tools/app-check.mjs --light` (the AOT bundle's default
    and every-detector runs, whole and split, on files over three minutes), `tools/ci-scope.sh` (the two new files
    need no pull-request check).
  - Done when: realworld.yml's groups pass, or what they find is a row here or an ask upstream; README "Checks" says
    what it runs.
  - Not: no file in the repository, none that isn't openly licensed; no change to shotdetector.
  - Status: Done (2026-10-31: run here before the nightly's first run. Byte for byte natively and AOT under Node:
    Sintel's 720p trailer and its rotated variant (rotation landed with #83 while this row was open, so it moved from
    refused to same), and Caminandes as 1080i25 MBAFF. Natively all agree on Big Buck Bunny, once `Mp4Audio` was
    fixed (fixed here: with no edit list the audio starts at the first packet that decodes to samples, as FFmpeg's
    first frame). Natively all agree on Tears of Steel's detector runs, packets, frame types, ranges, threads and
    resume, its MP3 now answered with a reason. Caminandes fragmented: description, every run and packets the same,
    the rest still running when the row closed. Elephants Dream is refused as a variable rate, really one (its 'stts'
    mixes 1/24 and 2/24 s frames), for the VFR row. Caminandes' own first pts differs (ManagedCodecs#252, a row in
    Waiting), its CSVs and shots identical. Real phone and broadcast files: a row in Maybe.)
