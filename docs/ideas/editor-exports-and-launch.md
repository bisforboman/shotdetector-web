# Idea: editor exports on the page, then a public launch (issue #84)

A scoping note, written 2026-10-31 at the owner's request: what exists, what the outside formats and channels ask, the
gap, a first slice, the risks, and a verdict. No code has changed for it. The backlog row is for the shotdetector
coordinator to add; this note is what the row can point at.

Repositories read: this one at `main` `06aad0f`, and [bisforboman/shotdetector](https://github.com/bisforboman/shotdetector)
at `main` `adcc4a8` (ShotDetector 3.23.0, the version the page pins).

## 1. What exists

### In ShotDetector

`src/ShotDetector/Timeline.cs` (shotdetector) is a static class of six exports, each taking a `DetectionResult` and
returning a string:

| Export | scenedetect's | Needs the video's path |
| --- | --- | --- |
| `Timeline.Edl(result, title, reel = "AX", startTimecode)` | `save-edl`: CMX 3600, one cut event per shot | No (the `TITLE` line uses the video's name: `stream` with no path) |
| `Timeline.Fcpx(result, name)` | `save-fcp --format fcpx`: FCPXML 1.9 | Yes (`media-rep src`, a `file://` URI) |
| `Timeline.Fcp7(result, name)` | `save-fcp --format fcp7`: xmeml 5 | Yes (`pathurl`) |
| `Timeline.Otio(result, name, audio = true)` | `save-otio`: OpenTimelineIO `Timeline.1` JSON | Yes (`target_url`, `Path.GetFullPath`) |
| `Timeline.Qp(result, shiftStart = true)` | `save-qp`: an x264/x265 QP file, a keyframe at each cut | No |
| `Timeline.Keyframes(result)` | 0.7.2's `save-keyframes`: Aegisub keyframes v1 | No |

Each is byte for byte scenedetect's except the EDL's first comment line, which names ShotDetector. Upstream checks
that in CI with `tools/compare-timeline.py` (`.github/workflows/ci.yml`, "compare-timeline"): scenedetect and the
`shotdetect` CLI on the same samples, 25 fps and 30000/1001, with and without a time range, a start timecode, a
reel, a title and a file name with a space. Upstream's `THIRD-PARTY-NOTICES.md` carries PySceneDetect's BSD 3-Clause
notice for these ports.

The frame-source path (`ShotDetection.Detect(YuvVideo, IEnumerable<YuvFrame>, ...)`, the page's) returns a result
with no `VideoPath`, and its documentation says "the CSVs, timelines and stats are" available
(`src/ShotDetector/ShotDetection.cs`, the remarks above that `Detect`). `DetectionResult` is a record whose
`VideoPath` is `init`, so the page can give the exports a name with `result with { VideoPath = "<file name>" }`.

No YouTube-chapters export exists in ShotDetector, and no marker list.

### On the page

- `src/ShotDetectorWeb.App/wwwroot/panels/run.js`: the run's card, four downloads named after the file: the scenes
  CSV and stats CSV (from `Detect`'s JSON and `StatsCsv()`), and the shots as JSON and HTML, asked for on `done`
  with `call('ShotsJson')` and `call('ShotsHtml')`. A stopped run's downloads are for the part read.
- `src/ShotDetectorWeb.App/Api.Csv.cs`: `ScenesCsv()` (`Shots.Csv`) and `StatsCsv()` (a `Blob` written a row at a
  time through `BlobWriter`).
- `src/ShotDetectorWeb.App/Api.Shots.cs`: `ShotsJson()` (`Shots.Json`) and `ShotsHtml()` (`Shots.Html` without
  images).
- An export of the last run's result also needs a task twin in `Api.Threads.cs` and its name in `threads.js`'s
  `THREADED` and `RESULTS` (CLAUDE.md, "Adding to the page").

Nothing on the page calls `Timeline` yet.

### Does `Timeline` pull FFmpeg.AutoGen into the trimmed bundle? No.

I checked locally and committed nothing. I published the App (`dotnet publish src/ShotDetectorWeb.App -c Release`,
interpreted, trimmed, the same as `tools/check.sh`'s `publish_app` minus `-warnaserror`) twice: once as `main` is,
and once with a scratch `Api.TimelineScratch.cs` whose one `[JSExport]` calls all six exports (`Edl`, `Qp` and
`Keyframes` on `Result`, and `Fcpx`, `Fcp7` and `Otio` on `Result with { VideoPath = name }`). Then I compared the
two `_framework/` listings:

- **Same 29 files.** No `FFmpeg.AutoGen.wasm` and no other new assembly. `tools/check.sh` would fail the bundle if
  `FFmpeg.AutoGen.wasm` appeared.
- **+16.9 KB in total** (5,208,164 to 5,225,060 bytes), almost all in `ShotDetector.wasm` (198,933 to 213,781 bytes).
  Before gzip; Pages gzips it.
- **It runs.** Under Node, with `tools/node-host.mjs` as app-check.mjs uses it, I opened a 12 s three-shot H.264 test
  clip and ran `Detect('{}')`. The scratch export gave three EDL events at 00:00:00:00, 00:00:04:00 and
  00:00:08:00, a QP file and Aegisub keyframes at 0, 100 and 200, an FCPXML 1.9 and an xmeml 5 with three clips, and
  an OTIO timeline. With no path given, the EDL's `TITLE` came out `stream`.
- **The path the browser can't know.** The path-taking exports wrote `file:///three.mp4` and `/three.mp4`: the
  browser never learns the file's folder, so the editor shows the media offline and the user relinks it once.

I didn't run the AOT build. The trimmer is the same for AOT, but the slice's PR runs `tools/check.sh wasm aot` anyway.

## 2. The outside formats and channels

A note on sources: this session's network blocks support.google.com, news.ycombinator.com, reddit.com,
readthedocs.io and Adobe's help pages, so I couldn't open them. Where a rule below rests on a search result that
quotes the official page, it says so. Where only third parties were found, it says that too. Before the launch,
someone should read each official page again in a browser.

### YouTube chapters

From Google's help page "Video chapters" ([support.google.com/youtube/answer/9884579](https://support.google.com/youtube/answer/9884579)),
as search results quote it:

- The first timestamp must be `00:00` ("Make sure that the first timestamp you list starts with 00:00").
- At least three timestamps, in ascending order.
- Each chapter at least 10 seconds long, measured to the next timestamp. Third-party guides add that the last chapter
  is measured to the video's end.
- Each timestamp goes on its own line, followed by a title, in the video's description.

The page also mentions eligibility limits, but I couldn't read their text (some channels or videos don't get chapters,
for example when flagged). YouTube also offers automatic chapters of its own.

What this means here: chapters are timestamps in whole seconds with titles, and the page has no titles; "Shot 3" is
not a chapter name. An edited video has a cut every 2 to 5 s, so a chapter list is the cuts thinned to at least 10 s
(better, a minimum the user chooses), rounded to whole seconds with that spacing kept after rounding, starting at 0:00,
with the user writing the titles. It is a format of the page's own (the `page` mark), not scenedetect's.

### DaVinci Resolve

From the Resolve 18.6 manual (a mirror of Blackmagic's PDF):

- **Timelines:** Media Pool → Timelines → Import → AAF/EDL/XML/DRT/ADL/OTIO; "you can select multiple XML, AAF,
  FCP XML, or EDL timelines". OTIO import and export since 18.5; it imports media from the file's paths or leaves
  the clips offline.
- **Markers:** right-click a timeline → Timelines → Import → **Timeline Markers from EDL**. Resolve's own marker EDL
  has one event per marker, one frame long, with the marker's note in the event's comment line.

So the CMX EDL that `Timeline.Edl` writes imports as a timeline of cuts, not as markers. A marker EDL is a different
file: the same events, one frame each, with a note comment. Third-party converters write `|C:ResolveColorBlue |M:name
|D:1` comments, but I found no Blackmagic document of that syntax. It would have to be checked against a file Resolve
itself exports.

### Premiere Pro

I couldn't reach Adobe's help pages; these points come from Adobe's community forums and third-party guides:

- Premiere imports Final Cut Pro 7 XML (File → Import) as a sequence: `Timeline.Fcp7` fits.
- I found no native import of a marker CSV or a marker EDL; users rely on extensions (Markerbox) or converters.
- Its EDL import is unconfirmed by an Adobe source.

So for Premiere, "markers" means FCP7 XML clip or sequence markers, a format ShotDetector doesn't write.

### EDL, FCPXML and OTIO as editors import them

| File | Imported by |
| --- | --- |
| CMX 3600 EDL | Resolve (timeline), Avid and most NLEs. It names a reel (`AX`) and no file, so the user matches the clip by hand. |
| FCPXML 1.9 | Final Cut Pro and Resolve. Not Premiere natively, as far as found. |
| FCP7 XML (xmeml) | Premiere and Resolve |
| OTIO | Resolve 18.5+, Kdenlive (with reported quirks), pipelines through OTIO's own adapters |

### Launch channels

- **Show HN** ([news.ycombinator.com/showhn.html](https://news.ycombinator.com/showhn.html), as search results
  quote it): "something you've made that other people can play with", which you worked on yourself and are around to
  discuss. Not blog posts, sign-up pages, newsletters, lists or other reading material. The page qualifies: it runs
  at once, has a demo button and needs no sign-up. Post the page's URL, not a write-up.
- **r/editors, r/VideoEditing, r/DataHoarder, r/dotnet, r/Blazor:** I couldn't verify their rules.
  - r/editors lists "No self-promotion" among its rules, according to a mirror of r/VideoEditing's sidebar, which
    also sends professionals to r/editors.
  - r/dotnet changed its self-promotion rule in April 2026; comments describe a Saturday window.
  - I found nothing on r/DataHoarder's rules.
  - The page isn't Blazor (plain `[JSExport]` on browser-wasm), so r/Blazor is off-topic unless framed as "no
    Blazor".
  - Each sidebar must be read before posting, and the mods asked where it says so. Reddit's own guideline is about
    one promotional post in ten.
- **.NET community standups:** by invitation, no public rules to check. An email to the hosts with the page's link.

## 3. The gap

1. **No timeline export on the page.** `Api` has no `Timeline` export, `run.js` has no link for one, and no check
   compares them. The checks would be NativeCheck (the core's result `with { VideoPath = path }` against
   `Detect(path)`'s, byte for byte), app-check.mjs (the bundle's against the native run's files) and page-check (the
   downloaded files).
2. **The media path.** The browser never knows the file's folder. FCPXML, FCP7 and OTIO can match scenedetect's only
   when given the same path string. On the page they carry the file name alone, and the editor relinks. The run card's
   mark has to say so (`exact` for EDL, QP and keyframes; the XMLs and OTIO "scenedetect's, but for the media path").
3. **No YouTube chapters anywhere.** The rules are in section 2. Where it lives is a choice: in ShotDetector as
   `Timeline.YouTubeChapters` (the CLI gets it too, unit-tested there), or on the page in JavaScript from `Detect`'s
   shots (`page` mark).
4. **No marker list.** A Resolve marker EDL is a small, separate format, and Premiere has no simple import to target.
   Leave it out unless an editor asks.
5. **Launch readiness:**
   - **Refusal messages.** They say what isn't supported (`Mp4Input.cs`: "The video is 'hvc1', not H.264 ('avc1'):
     only H.264 is supported so far."; the variable-rate message), not what to do instead. They should name the
     `shotdetect` CLI.
   - **`THIRD-PARTY-NOTICES.md`.** It lists ShotDetector as MIT but not PySceneDetect's BSD 3-Clause notice or
     simpletable's MIT notice. The bundle ships ShotDetector's ports of them (and `Shots.Html`), and BSD 3-Clause
     asks for the notice in binary redistributions. Its table also leaves out `ManagedCodecs.Mjpeg`.
   - **No check of "nothing uploaded".** The page says "Nothing leaves your machine: the file is read here, there is
     no upload, no server and no analytics" (`index.html`), and the code bears it out (section 5), but no check holds
     it.

## 4. A first slice, and the size of the whole

**First slice (one PR): the scenedetect timeline exports on the run card.**

- `src/ShotDetectorWeb.App/Api.Timeline.cs`: `TimelineEdl()`, `TimelineFcpx()`, `TimelineFcp7()`, `TimelineOtio()`,
  `TimelineQp()`, each on `Result with { VideoPath = <the opened file's name> }`, ShotDetector's defaults otherwise.
  (`Keyframes` is optional; Aegisub users are subtitlers, not this launch's audience.) Task twins in `Api.Threads.cs`;
  the names in `threads.js`'s `THREADED` and `RESULTS`.
- `panels/run.js`: a second row of downloads, "For editors", named `<file>.edl`, `<file>.fcpxml`, `<file>.xml`,
  `<file>.otio`, `<file>.qp`. Tooltips say which editor reads which and that the media must be relinked.
- `tools/NativeCheck`: a `TimelineCheck.cs`, every sample and run in `tools/Shared/Runs.cs`, the core's exports
  against `Detect(path)`'s with the same `VideoPath`, written to `out/native/` for app-check.mjs. `app-check.mjs`:
  the bundle's against those, including split, stopped and resumed runs. `page-check.mjs`: the downloads against the
  native run's.
- README, CLAUDE.md's layout, the backlog row moved.

Done when `tools/check.sh native wasm aot page` passes with the five files identical to the native run's on every
sample. CI scope: the App's .NET side changes, so everything runs.

**The whole idea in PRs: about four here, one upstream if chosen, and the launch itself, which is not a PR.**

1. The timeline exports (above).
2. YouTube chapters. On the page: a `page`-marked download with a minimum-length field, its rules unit-checked in
   page-check. Or first a ShotDetector PR (`Timeline.YouTubeChapters`, tests there, a release), then a small page PR.
3. Launch readiness: refusal messages that name the `shotdetect` CLI, `THIRD-PARTY-NOTICES.md` completed, a
   page-check assertion that no request leaves the origin and none is made after a file is picked except the bundles'
   lazy loads, and the landing's one-line scope ("H.264 MP4 and MJPEG AVI; phones' HEVC: use the CLI").
4. Maybe, only if asked: a Resolve marker EDL. It needs a Resolve-exported file to copy the syntax from, and a decision
   on whether it belongs in ShotDetector.
5. The launch: the owner's posts, outside the repository.

## 5. Risks

### Licences

- **FastYuv (LGPL-2.1-or-later):** already handled: its own `.wasm`, `COPYING.LGPL` in every output, and replacement
  by rebuilding checked in CI (`tools/check.sh replace aot`). A launch changes nothing, but a reader on HN will ask,
  so README's "Licensing" should be one click from the page's footer, as it is through "Third-party notices".
- **scenedetect's formats:** CMX 3600, FCPXML, xmeml and OTIO are open formats; writing them needs no licence. The
  code that writes them in scenedetect's exact bytes is a port, under BSD 3-Clause, and its notice must travel with
  the bundle: today it doesn't (gap 5). It's cheap to fix and must happen before a launch.
- **Sample media:** the demo is Sintel's trailer, CC-BY 3.0, credited next to the button and in
  `THIRD-PARTY-NOTICES.md`, with the changes stated. Fine. Any launch screenshot or GIF of the page with Sintel in it
  needs the same credit. No other media should appear in launch material without a licence.
- **"Matches PySceneDetect byte for byte":** what this repository checks is the page against ShotDetector's FFmpeg
  path. ShotDetector's own CI checks it against scenedetect. The pitch is true for the CSVs (and the timeline files,
  except the EDL's comment line and the media path) by those two links together. The launch post should link both,
  and say "for the files it accepts".

### GitHub Pages under load

- [GitHub's limits](https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits): a site of
  at most 1 GB, a **soft** bandwidth limit of 100 GB a month, and HTTP 429 when rate-limited.
- A first visit is about 5.1 MB transferred for the main bundle (README "Size"), plus about 5.6 MB gzipped for the
  threaded bundle when a file is picked where the page is isolated (PR #87's figures), plus about 10 MB if the demo is
  pressed: roughly 5 to 21 MB a visitor. The service worker and HTTP cache spare returning visitors.
- So 100 GB is roughly 5,000 to 20,000 first visits a month. A Show HN on the front page can bring that in a day; this
  is an estimate, not a measurement.
- Going over a soft limit means GitHub may get in touch or rate-limit, not an outage by contract. If the owner wants
  margin: keep the demo clip off the first load (it already is: fetched only on the button), and have a second static
  host ready to point at. The page needs nothing but static files and its own service worker for isolation.

### Privacy: "nothing uploaded"

What the code does (read at `06aad0f`):

- **The file:** read only through `Blob` slices (`blob-read.js`'s `FileReaderSync`, `BlobStream`'s `read`). It is never
  passed to `fetch`.
- **The page's network calls:** `fetch('demo/sintel-trailer.mp4')` (`app.js`, the demo button),
  `navigator.serviceWorker.register('sw.js')`, and the runtime's loads of `_framework/` files (same origin).
- **`sw.js`:** answers only same-origin GETs (`origin !== location.origin` returns early), fetches them, caches whole
  200 answers, and adds COOP/COEP. It makes no request of its own and sends nothing anywhere.
- **No third parties:** no analytics, beacons, WebSockets or third-party scripts, styles or fonts. The only off-site
  URLs are plain links in `index.html` and SVG namespace strings. With COEP `require-corp`, a cross-origin subresource
  without CORP would be blocked anyway.
- **IndexedDB:** the kept run (`KEPT`) stores the file's name, size and modified time and `Kept()`'s bytes, locally.
  It isn't uploaded, but a privacy line should mention it ("a stopped run is kept in your browser until you forget
  it").
- **GitHub:** serving the page, GitHub sees each visitor's IP address and the requests for the page's files, under
  GitHub's privacy statement. "No server" means no server of ours, not that no one serves the page.

The claim holds today, but by reading the code, not by a check. Gap 5's page-check assertion (every request
same-origin, none carrying the file, none after the pick but `_framework/` and `threads/_framework/`) would make it a
check.

### Supported scope for a public launch

What is on `main` today (`06aad0f`; CLAUDE.md "Scope so far", `Mp4Input.cs`), and what most phone files hit:

- **HEVC:** refused. Recent iPhones record HEVC by default ("High Efficiency"); "Most Compatible" records H.264.
  Many Android phones default to H.264 but can record HEVC. The pinned ManagedCodecs packages have no HEVC decoder
  (Containers, H264, Mjpeg, Aac), so HEVC is a ManagedCodecs question first.
- **Rotation:** refused on `main` ("The video is rotated or flipped"). Phones always write a rotation matrix for
  portrait video. PR #83 (open) adds quarter turns. Until it lands, every portrait phone clip is refused.
- **Variable frame rate:** refused on `main` (`Mp4Input.VariableRate`). Phones and screen recorders produce it.
  ShotDetector 3.23.0 shipped the upstream part (shotdetector#609). The backlog row here still reads "Waiting on
  upstream" on `main`, and no PR for it is open as of this note.
- **Deinterlacing:** same state (shotdetector#614 shipped, the row here not started). It matters for broadcast
  material, not phones.
- **10-bit (HDR iPhone video, Dolby Vision HEVC):** refused, needs ShotDetector (backlog "Maybe").

So a launch aimed at editors before rotation and VFR land will meet many refusals from the first phone clips people
try. Editors' own exports (H.264 MP4 from an NLE: constant rate, upright, 8-bit 4:2:0) are what the page takes well,
and the launch post should say so.

## 6. Verdict

**Do the exports; park the launch until the scope rows land.**

- **The timeline exports (slice 1): do.** It's small: ShotDetector already has it, the bundle grows by 17 KB, no
  FFmpeg. It fits the page's exactness story, with one honest caveat, the media path. It's useful whether or not a
  launch follows.
- **YouTube chapters: do after, small.** It's a page-marked format, less exact by nature, and the owner chooses where it
  lives (below).
- **Marker lists: park.** There's no clear target in Premiere, and Resolve's format is undocumented beyond its own
  exports.
- **The launch: park until rotation (#83) and VFR are on `main`,** and gap 5's readiness PR has landed. Then it's the
  owner's call.

The owner's decision questions (listed here, not asked):

1. Where do YouTube chapters live: in ShotDetector (`Timeline.YouTubeChapters`, the CLI too) or on the page only?
   And the default minimum chapter length: YouTube's 10 s, or something longer such as 30 s, since cuts every 10 s
   make poor chapters?
2. The media path in FCPXML, FCP7 and OTIO: the file name alone (relink in the editor), or a field where the user can
   type the folder the file lives in, so it opens linked?
3. Is the launch gated on rotation and VFR landing, and on HEVC (a ManagedCodecs decoder, a far larger piece), or only
   on the first two?
4. Which channels, in what order? Show HN first, then the subreddits that allow it after reading their sidebars, and
   the .NET standup by email? And who posts: the owner's accounts only (Show HN requires the maker to be there to
   discuss)?
5. A second static host ready in case Pages rate-limits on launch day: wanted, or accept the risk?
6. Should the pitch keep "byte for byte with PySceneDetect", which needs the two-link explanation in section 5, or say
   "the same results as PySceneDetect, checked byte for byte"?
